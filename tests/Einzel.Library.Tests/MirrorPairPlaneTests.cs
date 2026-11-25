using Einzel.Core.Geometry;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A mirror pair is summed about its mid-plane the way its halves meet there: as a union where
/// the solved mirror ends before the plane, as an overlap of two tails where it runs past it.
/// </summary>
/// <remarks>
/// The pair used to give its image no side, so the image answered everywhere but exactly on the
/// plane. Where the solve runs past the plane - the single-stage pair at focus, whose 60 mm
/// field-free run ends at 150 mm against a plane at 145.2 mm - that made the potential jump at
/// the plane, where the ion is launched every period. Where the solve ends before the plane the
/// image must answer only on its own side, or one ulp below a power-of-two plane its reflection
/// rounds onto the plane and both halves answer.
/// </remarks>
public sealed class MirrorPairPlaneTests(ITestOutputHelper output)
{
    private static PlanarMirror Mirror() => PlanarMirror.Solve(
        MirrorProfile.SingleStage(0.090, 4800.0), boardGap: 0.030, fieldFreeRun: 0.060, cellsPerGap: 32);

    /// <summary>Where the halves overlap, the sum is continuous across the plane and symmetric about it.</summary>
    [Fact]
    public void OverlappingHalvesAreSummedOnThePlaneToo()
    {
        var mirror = Mirror();
        var plane = 0.1452;
        Assert.True(mirror.Potential.Grid.MaxX > plane, "the solve runs past the plane");

        var pair = new MirrorPair(mirror, 2.0 * plane);
        double At(double x) => pair.Field.PotentialAt(new Vec3(x, 0.001, 0.0));

        var on = At(plane);
        var below = At(plane - 1e-9);
        var above = At(plane + 1e-9);

        output.WriteLine($"below {below:R}  on {on:R}  above {above:R}");

        Assert.True(Math.Abs(below) > 1e-9, "a tail there to compare");
        Assert.Equal(below, above, 1e-12);
        Assert.Equal(below, on, 1e-9 * Math.Abs(below) + 1e-15);
    }

    /// <summary>
    /// Where the solve ends before the plane, the image answers only on its own side, so no
    /// rounding of a reflection near a power-of-two plane counts a point twice.
    /// </summary>
    [Fact]
    public void AHalfThatEndsBeforeThePlaneIsAUnion()
    {
        var mirror = Mirror();
        var plane = 0.25;
        Assert.True(mirror.Potential.Grid.MaxX <= plane);

        var pair = new MirrorPair(mirror, 2.0 * plane);
        var alone = mirror.Field();

        foreach (var x in new[] { 0.149, double.BitDecrement(plane), plane })
        {
            var point = new Vec3(x, 0.001, 0.0);
            Assert.Equal(alone.PotentialAt(in point), pair.Field.PotentialAt(in point));
        }
    }
}
