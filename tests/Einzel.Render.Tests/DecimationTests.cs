using Einzel.Render;
using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// The decimation bound is a guarantee, not a hint.
/// </summary>
/// <remarks>
/// RND-5 requires a stated geometric tolerance and GRD-12 requires it recorded in
/// the artifact. Both are worth nothing if the number is not actually respected:
/// a figure that says it is accurate to 0.1% of its extent and is not is exactly
/// the artifact GRD-12 exists to prevent.
/// </remarks>
public sealed class DecimationTests(ITestOutputHelper output)
{
    private static List<PagePoint> Sample(Func<double, PagePoint> curve, int count)
    {
        var points = new List<PagePoint>(count);

        for (var i = 0; i < count; i++)
        {
            points.Add(curve(i / (count - 1.0)));
        }

        return points;
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.05)]
    [InlineData(0.2)]
    [InlineData(1.0)]
    public void NoDiscardedPointLiesFurtherThanTheTolerance(double tolerance)
    {
        // A curve with structure at several scales, so a decimator that happens to
        // keep every nth point would fail rather than pass by luck.
        var points = Sample(
            t => new PagePoint(
                120.0 * t,
                40.0 + (18.0 * Math.Sin(6.0 * Math.PI * t)) + (3.0 * Math.Sin(41.0 * Math.PI * t))),
            4000);

        var reduced = Decimation.Reduce(points, tolerance);
        var worst = Decimation.WorstDeviation(points, reduced);

        output.WriteLine(
            $"tolerance {tolerance,5:F2} mm: {points.Count} -> {reduced.Count,4} points, "
            + $"worst deviation {worst:F6} mm");

        Assert.True(worst <= tolerance, $"deviation {worst:G6} exceeds the stated {tolerance:G6}");

        // Both ends survive, or the curve has been shortened rather than simplified.
        Assert.Equal(points[0], reduced[0]);
        Assert.Equal(points[^1], reduced[^1]);
    }

    [Fact]
    public void ATighterToleranceKeepsMorePoints()
    {
        // Monotone in the bound, which is the property that makes the tolerance a
        // usable dial rather than a number that happens to appear in the output.
        var points = Sample(
            t => new PagePoint(100.0 * t, 30.0 * Math.Sin(4.0 * Math.PI * t)), 2000);

        var previous = int.MaxValue;

        foreach (var tolerance in new[] { 0.001, 0.01, 0.1, 1.0, 5.0 })
        {
            var reduced = Decimation.Reduce(points, tolerance);

            output.WriteLine($"{tolerance,6:G3} mm -> {reduced.Count,4} points");

            Assert.True(reduced.Count <= previous);
            previous = reduced.Count;
        }
    }

    [Fact]
    public void ATrajectoryThatTurnsRoundKeepsItsTurningPoint()
    {
        // The case that catches an unclamped point-to-line distance. A reflectron
        // sends an ion out and back along nearly the same line, so the turning point
        // sits almost on the chord between the two ends - and measuring to the
        // infinite line rather than to the segment calls it redundant and decimates
        // the reflection away, leaving a figure of an ion that flew straight through
        // a mirror.
        var points = new List<PagePoint>();

        for (var i = 0; i <= 500; i++)
        {
            var t = i / 500.0;
            points.Add(new PagePoint(10.0 + (80.0 * Math.Sin(Math.PI * t)), 40.0 + (0.02 * t)));
        }

        var reduced = Decimation.Reduce(points, 0.1);

        var furthest = reduced.Max(p => p.X);

        output.WriteLine($"{points.Count} -> {reduced.Count} points, furthest x {furthest:F2} mm");

        Assert.True(furthest > 89.0, $"the turning point at x = 90 was decimated away, kept {furthest:F2}");
        Assert.True(Decimation.WorstDeviation(points, reduced) <= 0.1);
    }

    [Fact]
    public void AStraightLineReducesToItsEnds()
    {
        var points = Sample(t => new PagePoint(5.0 + (90.0 * t), 20.0 + (30.0 * t)), 1000);

        var reduced = Decimation.Reduce(points, 1e-9);

        output.WriteLine($"{points.Count} collinear points -> {reduced.Count}");

        Assert.Equal(2, reduced.Count);
    }
}
