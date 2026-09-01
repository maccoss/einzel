using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A volume solve may mirror its half, which halves the dominant cost of a 3-D run.
/// </summary>
/// <remarks>
/// <para>
/// An instrument symmetric about a plane costs twice what it needs to: the far half is the
/// near half seen backwards. The plane path has had <c>reflectAboutX</c> from the beginning;
/// the volume path — where the solve is 94 per cent of a run and the saving is therefore
/// worth most — did not.
/// </para>
/// <para>
/// It composes unchanged because <see cref="ReflectedField"/> mirrors a coordinate of any
/// field rather than knowing how that field was meshed, so a <c>SolvedField3D</c> reflects
/// exactly as a plane one does.
/// </para>
/// </remarks>
public sealed class ReflectedVolumeTests(ITestOutputHelper output)
{
    private const double Cell = 0.0008;
    private const double Mid = 0.020;

    /// <summary>A charged plate near each end of a box, symmetric about x = Mid.</summary>
    /// <remarks>
    /// Symmetric on purpose: the whole test is that solving one half and mirroring it gives
    /// the field the full solve gives, so the geometry has to be one a mirror can reproduce.
    /// </remarks>
    private static CompiledElectrode3D Plate(string name, double minX, double maxX) =>
        new()
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            MinX = minX,
            MaxX = maxX,
            MinY = -0.004,
            MaxY = 0.004,
            MinZ = -0.004,
            MaxZ = 0.004,
            Potential = 100.0,
        };

    /// <summary>The whole instrument, solved end to end.</summary>
    private static Geometry3D Whole() => new(
        0.0, -0.010, -0.010, 2.0 * Mid, 0.010, 0.010, Cell,
        [
            Plate("near", 0.004, 0.008),
            Plate("far", (2.0 * Mid) - 0.008, (2.0 * Mid) - 0.004),
        ]);

    /// <summary>Half of it, with the mid-plane a symmetry plane and mirrored back.</summary>
    private static Geometry3D Half() => new(
        0.0, -0.010, -0.010, Mid, 0.010, 0.010, Cell,
        [Plate("near", 0.004, 0.008)])
    {
        // The pairing the plane path uses: a mirror plane is a symmetry plane, so the face
        // it sits on is Neumann. Grounding it instead would put a conductor down the middle
        // of the instrument, and the two halves would meet at zero volts.
        Faces =
        [
            EdgeCondition.Dirichlet, EdgeCondition.Neumann,
            EdgeCondition.Dirichlet, EdgeCondition.Dirichlet,
            EdgeCondition.Dirichlet, EdgeCondition.Dirichlet,
        ],
        ReflectAboutX = Mid,
    };

    /// <summary>The mirrored half reproduces the full solve.</summary>
    /// <remarks>
    /// <para>
    /// <b>Sampled across the whole instrument, including the far half the mirror invents.</b>
    /// Checking only the solved half would pass for a reflection that did nothing at all.
    /// </para>
    /// <para>
    /// The agreement is not exact and should not be expected to be: the full solve meshes
    /// 2 x Mid of domain and the half meshes Mid, and each axis rounds its own interval count
    /// up to a power of two, so the two are discretisations of the same problem rather than
    /// the same discretisation. What is asserted is that they agree to a fraction of the
    /// applied potential far below any physical effect being modelled.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMirroredHalfReproducesTheFullSolve()
    {
        var (whole, wholeReport) = GeometryBuilder3D.BuildField(Whole());
        var (half, halfReport) = GeometryBuilder3D.BuildField(Half());

        output.WriteLine(
            $"whole   {wholeReport.Cycles} cycles, converged {wholeReport.Converged}");
        output.WriteLine(
            $"half    {halfReport.Cycles} cycles, converged {halfReport.Converged}");
        output.WriteLine(string.Empty);
        output.WriteLine("       x mm     whole V      mirrored V        diff");

        var worst = 0.0;

        for (var x = 0.002; x < 2.0 * Mid; x += 0.002)
        {
            var at = new Vec3(x, 0.0, 0.0);

            var a = whole.PotentialAt(at);
            var b = half.PotentialAt(at);

            worst = Math.Max(worst, Math.Abs(a - b));

            if (Math.Abs((x * 1e3) % 8.0) < 1e-9)
            {
                output.WriteLine($"    {x * 1e3,7:F1}  {a,10:F4}  {b,14:F4}  {a - b,10:F5}");
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"worst difference {worst:F5} V of 100 applied ({worst / 100.0:P4})");

        Assert.True(
            worst < 0.5,
            $"the mirrored half differs from the full solve by {worst:F4} V of 100 applied. "
            + "The two are different discretisations of one problem, so exact agreement is not "
            + "expected - but half a volt is far more than that explains");
    }

    /// <summary>The reflection is what makes the far half exist at all.</summary>
    /// <remarks>
    /// The control, and it is the whole test: without it, "the mirrored half agrees with the
    /// full solve" would pass for a half-solve that simply reported nothing out there and
    /// happened to be compared where both were near zero. With the mirror off, the far half
    /// is empty and the disagreement is enormous.
    /// </remarks>
    [Fact]
    public void WithoutTheMirrorTheFarHalfIsMissing()
    {
        var (whole, _) = GeometryBuilder3D.BuildField(Whole());
        var (unmirrored, _) = GeometryBuilder3D.BuildField(Half() with { ReflectAboutX = null });

        // Deep in the far half, right where the mirrored plate should be.
        var at = new Vec3((2.0 * Mid) - 0.006, 0.0, 0.0);

        var expected = whole.PotentialAt(at);
        var without = unmirrored.PotentialAt(at);

        output.WriteLine($"at x = {at.X * 1e3:F1} mm");
        output.WriteLine($"  full solve       {expected,10:F3} V");
        output.WriteLine($"  half, no mirror  {without,10:F3} V");

        // The far plate is at 100 V, so the full solve sees essentially all of it.
        Assert.True(expected > 50.0, "the sample point must be inside the far electrode's field");

        Assert.True(
            Math.Abs(expected - without) > 10.0,
            $"with the mirror off the far half read {without:F3} V against {expected:F3} V, so "
            + "the comparison in the test above would pass whether or not anything was "
            + "reflected");
    }
}
