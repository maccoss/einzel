using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A region on an analytic element, which is what lets an exact analyser be one device in
/// a beamline rather than the only device in a document.
/// </summary>
/// <remarks>
/// <para>
/// An analytic field has no extent, because a formula does not. A quadro-logarithmic
/// potential grows as z squared, so an orbital analyser declared beside the trap that
/// injects it puts an enormous field across that trap — the two instruments cannot be
/// composed even though superposition is exact and the sequencer can express the handover.
/// SPEC.md Amendment 32.
/// </para>
/// <para>
/// <b>The escape of declaring the analyser as solved geometry does not exist</b>, which is
/// why this is a feature rather than a workaround: an orbital trap's electrodes are
/// equipotentials of the field they produce, so their profile satisfies
/// <c>-r^2/2 + Rm^2 ln(r/Rm) = A - z^2</c> — transcendental in r, invertible only through
/// Lambert W — and the 2-D shape vocabulary is rectangle, disc and edge profile, none of
/// which is a curve a document can name.
/// </para>
/// </remarks>
public sealed class BoundedFieldTests(ITestOutputHelper output)
{
    private static FieldRegion Box(double half) =>
        new(-half, half, -half, half, -half, half);

    private static QuadroLogarithmicField Orbital() =>
        QuadroLogarithmicField.Create(
            Quantity.From(20.0, "V/mm^2"),
            Quantity.From(20.0, "mm"),
            Vec3.Zero);

    /// <summary>Inside the box the bounded field is the field it wraps, to the bit.</summary>
    /// <remarks>
    /// The control that makes every other test here mean something. If bounding changed the
    /// field where it is supposed to apply, a region would be a different instrument rather
    /// than the same one confined — and the change would be invisible against a closed form
    /// evaluated at the same points.
    /// </remarks>
    [Fact]
    public void InsideTheRegionNothingChanges()
    {
        var inner = Orbital();
        var bounded = BoundedField.Around(inner, Box(0.030));

        var worstField = 0.0;
        var worstPotential = 0.0;

        for (var k = 0; k < 40; k++)
        {
            // Off the axis, where the logarithm is defined, and well inside the box.
            var p = new Vec3(
                -0.020 + (0.001 * k),
                0.004 + (0.0003 * k),
                0.002 - (0.00005 * k));

            var a = inner.ElectricFieldAt(in p);
            var b = bounded.ElectricFieldAt(in p);

            worstField = Math.Max(worstField, Math.Sqrt(Vec3.Dot(a - b, a - b)));
            worstPotential = Math.Max(
                worstPotential, Math.Abs(inner.PotentialAt(in p) - bounded.PotentialAt(in p)));
        }

        output.WriteLine($"worst field difference inside: {worstField:E1} V/m");
        output.WriteLine($"worst potential difference:    {worstPotential:E1} V");

        Assert.Equal(0.0, worstField);
        Assert.Equal(0.0, worstPotential);
    }

    /// <summary>Outside it the element is silent, which is the whole point.</summary>
    /// <remarks>
    /// <b>Measured against what the unbounded field would have done there</b>, not merely
    /// asserted to be zero. A quadro-logarithmic potential grows as z squared, so at the
    /// place a neighbouring instrument would sit it is not a small field being neglected —
    /// it is a large one, and the number below is what a C-trap would have been subjected
    /// to.
    /// </remarks>
    [Fact]
    public void OutsideTheRegionTheElementIsSilent()
    {
        var inner = Orbital();
        var bounded = BoundedField.Around(inner, Box(0.030));

        // Where a C-trap would sit: 20 mm bend radius about a point 60 mm away.
        var neighbour = new Vec3(0.060, 0.020, 0.0);

        var unbounded = inner.ElectricFieldAt(in neighbour);
        var confined = bounded.ElectricFieldAt(in neighbour);

        output.WriteLine(
            $"at a neighbouring instrument, unbounded: {Math.Sqrt(Vec3.Dot(unbounded, unbounded)):N0} V/m, "
            + $"potential {inner.PotentialAt(in neighbour):N0} V");

        output.WriteLine(
            $"                              bounded:   {Math.Sqrt(Vec3.Dot(confined, confined)):N0} V/m, "
            + $"potential {bounded.PotentialAt(in neighbour):N0} V");

        Assert.Equal(Vec3.Zero, confined);
        Assert.Equal(0.0, bounded.PotentialAt(in neighbour));

        // And the thing being prevented is large rather than negligible.
        Assert.True(
            Math.Sqrt(Vec3.Dot(unbounded, unbounded)) > 1.0e5,
            "the unbounded field at a neighbour's position was small, so this test is not "
            + "demonstrating what a region is for");
    }

    /// <summary>The boundary is found as the zero of the declared discontinuity.</summary>
    /// <remarks>
    /// Spec section 11 makes a declared field discontinuity a first-class event: the
    /// integrator brackets the sign change and lands exactly on it. A region has to present
    /// its surface the same way or an ion would step across the boundary and take whatever
    /// field the far side of the step happened to give.
    /// </remarks>
    [Fact]
    public void TheBoundaryIsSignedAndItsZeroIsTheSurface()
    {
        var region = Box(0.030);

        Assert.True(region.SignedDistance(Vec3.Zero) < 0.0);
        Assert.True(region.SignedDistance(new Vec3(0.100, 0.0, 0.0)) > 0.0);

        // Exact on a face, and exact on a corner too - a box distance that is only right
        // near the faces would put the landing in the wrong place near an edge.
        Assert.Equal(0.0, region.SignedDistance(new Vec3(0.030, 0.0, 0.0)), 12);
        Assert.Equal(0.0, region.SignedDistance(new Vec3(0.030, 0.030, 0.030)), 12);

        Assert.Equal(
            Math.Sqrt(3.0) * 0.010,
            region.SignedDistance(new Vec3(0.040, 0.040, 0.040)),
            12);

        // Inside, the least distance to any face.
        Assert.Equal(-0.005, region.SignedDistance(new Vec3(0.025, 0.0, 0.0)), 12);
    }

    /// <summary>A driven field keeps its time dependence through the wrapper.</summary>
    /// <remarks>
    /// <b>This project has found the same defect five times</b>: a time-varying quantity
    /// reached through the time-free interface does not fail, it answers at an arbitrary
    /// instant. <c>einzel solve</c> reporting the DC of a driven geometry, the diffusive
    /// mode stepping a density through a snapshot of the RF, <c>SuperposedField</c> becoming
    /// a snapshot when a driven member was summed in, the renderer drawing one instant on
    /// every frame, and a driven geometry inside a diffusive phase. A plain wrapper round a
    /// driven field would be the sixth, so the choice is made by what the field IS.
    /// </remarks>
    [Fact]
    public void ADrivenFieldStaysDriven()
    {
        var driven = IdealQuadrupoleRf.Create(
            Quantity.From(0.0, "V"),
            Quantity.From(500.0, "V"),
            Quantity.From(1.0, "MHz"),
            Quantity.From(3.0, "mm"));

        var bounded = BoundedField.Around(driven, Box(0.030));

        Assert.IsAssignableFrom<ITimeVaryingField>(bounded);

        var varying = (ITimeVaryingField)bounded;
        var inside = new Vec3(0.001, 0.0005, 0.0);

        var atZero = varying.ElectricFieldAt(in inside, 0.0);
        var atHalf = varying.ElectricFieldAt(in inside, 0.5e-6);

        output.WriteLine($"driven, t=0    {atZero.X:E3} V/m");
        output.WriteLine($"driven, t=T/2  {atHalf.X:E3} V/m");

        // Half a cycle of a sinusoid is the exact negative, which is a sharper check than
        // "the two differ": a wrapper that dropped the time argument would return the same
        // number twice, and one that passed it through some other path would rarely land
        // exactly on the negative.
        Assert.Equal(-atZero.X, atHalf.X, 9);

        // And outside it is silent at every instant, not merely at t = 0.
        var outside = new Vec3(0.100, 0.0, 0.0);

        foreach (var t in new[] { 0.0, 0.1e-6, 0.25e-6, 0.5e-6, 0.9e-6 })
        {
            Assert.Equal(Vec3.Zero, varying.ElectricFieldAt(in outside, t));
        }
    }

    /// <summary>The wrapper reports its inner field's own discontinuity as well.</summary>
    /// <remarks>
    /// A bounded half-space still has its own plane. Reporting only the box would let an ion
    /// step across the inner surface without landing on it, which is the error that took a
    /// reflectron from 5.5e-10 to 1.7e-16 when it was fixed the first time.
    /// </remarks>
    [Fact]
    public void AnInnerDiscontinuityIsStillReportedInside()
    {
        var half = HalfSpaceUniformField.Create(
            new Vec3(0.010, 0.0, 0.0),
            new Vec3(-1.0, 0.0, 0.0),
            Quantity.From(1000.0, "V/m"));

        var bounded = BoundedField.Around(half, Box(0.050));

        // Just inside the box and close to the inner plane: the nearer surface wins.
        var near = new Vec3(0.0105, 0.0, 0.0);

        Assert.True(
            Math.Abs(bounded.SignedDistanceToDiscontinuity(in near)) < 0.001,
            "the inner plane is 0.5 mm away and the box is 39.5 mm away, so the reported "
            + "surface should be the plane");

        // Near the box face, the box wins.
        var far = new Vec3(0.0499, 0.0, 0.0);

        Assert.True(Math.Abs(bounded.SignedDistanceToDiscontinuity(in far)) < 0.001);
    }
}
