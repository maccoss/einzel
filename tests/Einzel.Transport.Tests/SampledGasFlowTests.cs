using Einzel.Core.Geometry;
using Einzel.Transport.Collisions;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A neutral velocity field sampled from a grid.
/// </summary>
/// <remarks>
/// GAS-1 asks for a bulk velocity <em>field</em>, and spec figure 4 makes it a
/// requirement above 10^-2 mbar rather than a benefit: "the neutral jet off the
/// inlet capillary drags ions and frequently dominates the axial DC gradient". A
/// jet is not uniform across a ring stack, so a single declared vector cannot say
/// it.
/// </remarks>
public sealed class SampledGasFlowTests(ITestOutputHelper output)
{
    /// <summary>A field on a 3x3x1 grid over a 20 mm square, with vx = 10 + 10 i.</summary>
    private static SampledGasFlow Ramp()
    {
        var values = new double[3 * 3 * 3 * 1];

        for (var j = 0; j < 3; j++)
        {
            for (var i = 0; i < 3; i++)
            {
                var index = 3 * ((j * 3) + i);

                values[index] = 10.0 + (10.0 * i);
                values[index + 1] = 0.0;
                values[index + 2] = 0.0;
            }
        }

        return new SampledGasFlow(
            3, 3, 1, new Vec3(0.0, 0.0, 0.0), new Vec3(0.010, 0.010, 0.0), values);
    }

    [Fact]
    public void ItReturnsTheSampleExactlyAtANode()
    {
        // The floor. An interpolant that is not exact at its own nodes is not
        // interpolating, and every other assertion here rests on this one.
        var flow = Ramp();

        for (var i = 0; i < 3; i++)
        {
            var velocity = flow.VelocityAt(new Vec3(i * 0.010, 0.0, 0.0));

            output.WriteLine($"node {i}: {velocity.X:F6} m/s");

            Assert.Equal(10.0 + (10.0 * i), velocity.X, 1e-12);
            Assert.Equal(0.0, velocity.Y, 1e-12);
        }
    }

    [Fact]
    public void ItIsLinearBetweenNodes()
    {
        // Trilinear reproduces a linear field exactly, which this one is along x.
        // Not an approximation converging - an identity, so anything but machine
        // precision here is an indexing or weighting error rather than a tolerance
        // question.
        var flow = Ramp();

        foreach (var x in new[] { 0.0025, 0.005, 0.0075, 0.012, 0.0195 })
        {
            var velocity = flow.VelocityAt(new Vec3(x, 0.004, 0.0));
            var expected = 10.0 + (1000.0 * x);

            output.WriteLine($"x = {x * 1e3,6:F2} mm   {velocity.X:F9} against {expected:F9}");

            Assert.Equal(expected, velocity.X, 1e-9);
        }
    }

    [Fact]
    public void OutsideTheBoxTheEdgeValueContinues()
    {
        // Clamped, not zeroed. A flow that stopped at the edge of its imported box
        // would put a shear where the instrument has none, and an ion crossing that
        // shear would be deflected by an artefact of the import extent.
        //
        // How much of the tracked region that covers is reported separately rather
        // than absorbed here - see the next test.
        var flow = Ramp();

        Assert.Equal(10.0, flow.VelocityAt(new Vec3(-0.050, 0.0, 0.0)).X, 1e-12);
        Assert.Equal(30.0, flow.VelocityAt(new Vec3(0.050, 0.0, 0.0)).X, 1e-12);

        // And along an axis the field does not resolve at all, which is what a
        // two-dimensional import looks like: one node in z, so z cannot matter.
        Assert.Equal(
            flow.VelocityAt(new Vec3(0.005, 0.0, 0.0)).X,
            flow.VelocityAt(new Vec3(0.005, 0.0, 5.0)).X,
            1e-12);
    }

    [Fact]
    public void TheOverhangIsMeasuredRatherThanAbsorbed()
    {
        // The honest output where a tracked region runs past the imported extent:
        // not a refusal and not silence, but how much of the answer was continued
        // from an edge rather than measured.
        var flow = Ramp();

        var inside = flow.FractionOutside(new Vec3(0.002, 0.002, 0.0), new Vec3(0.018, 0.018, 0.0));
        var half = flow.FractionOutside(new Vec3(0.0, 0.0, 0.0), new Vec3(0.040, 0.020, 0.0));
        var away = flow.FractionOutside(new Vec3(0.100, 0.100, 0.0), new Vec3(0.120, 0.120, 0.0));

        output.WriteLine($"wholly inside   {inside:P2}");
        output.WriteLine($"twice as wide   {half:P2}");
        output.WriteLine($"wholly outside  {away:P2}");

        Assert.Equal(0.0, inside, 1e-12);
        Assert.Equal(0.5, half, 1e-12);
        Assert.Equal(1.0, away, 1e-12);
    }

    [Fact]
    public void TheFastestSpeedIsTheOneAStabilityLimitNeeds()
    {
        // A step is sized by the fastest anything moves anywhere, so the flow
        // reports its own maximum rather than making a caller scan a field it does
        // not own.
        var flow = Ramp();

        Assert.True(flow.IsMoving);
        Assert.Equal(30.0, flow.FastestSpeedSi, 1e-12);

        var still = new SampledGasFlow(
            2, 2, 1, Vec3.Zero, new Vec3(0.01, 0.01, 0.0), new double[3 * 4]);

        Assert.False(still.IsMoving);
        Assert.Equal(0.0, still.FastestSpeedSi, 1e-12);
    }

    [Fact]
    public void ASampleCountThatDoesNotMatchTheExtentIsRefused()
    {
        // A truncated file is the usual cause, and a field read one component short
        // would shear every velocity in it by one node.
        var failure = Assert.Throws<ArgumentException>(() => new SampledGasFlow(
            3, 3, 1, Vec3.Zero, new Vec3(0.01, 0.01, 0.0), new double[26]));

        output.WriteLine(failure.Message);

        Assert.Contains("27", failure.Message, StringComparison.Ordinal);
    }
}
