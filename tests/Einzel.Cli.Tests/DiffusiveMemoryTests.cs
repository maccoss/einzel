using Einzel.Commands;
using Einzel.Core.Numerics;
using Einzel.Testing;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The tests that read what a child process measured of a diffusive run. They share the
/// children - each run is measured once - and the measuring is done in processes of its own.
/// </summary>
[CollectionDefinition(nameof(RunsMeasuredInAChild))]
public sealed class RunsMeasuredInAChild : ICollectionFixture<MeasuredRuns>;

/// <summary>
/// What <c>einzel estimate</c> says a diffusive run holds over its density grid, against what
/// one holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read from inside the run, at each allocation</b> (<see cref="LiveSet"/>): a run's live set
/// can only rise at an allocation, and every array the size of the density grid - the
/// densities, the step's coefficients, the face operator, the conductors' map, the self-field
/// and its Poisson solve - comes from <see cref="GridArrays"/>, which tells the measurement as
/// it allocates. This was a second thread sampling the heap while the run went, the mechanism
/// that missed a solve's peak on loaded CI runners; here it read low once on an ubuntu runner
/// (1821.7 KiB beside four readings near 5080) and the test was made to outvote it. Nothing is
/// sampled now, so nothing is outvoted, warmed up past, or taken five times.
/// </para>
/// <para>
/// <b>Each measurement is an interval</b>, the least the run can have held at its most and the
/// most, and each figure here is a difference of two, so its lower end is one run's least less
/// the other's most. They are a few kilobytes wide on a megabyte: the run allocates almost
/// nothing outside its arrays, which is itself new - its stepping loop was making 224 bytes of
/// garbage a node a step (<c>FaceCoefficients.Offsets</c>), and a bound on what is held between
/// two arrays is what found it.
/// </para>
/// <para>
/// <b>In a child process</b> (<see cref="MeasuringChild"/>), where the live set is the run's and
/// nothing else's, built once unmeasured and then measured, as fully optimized code.
/// </para>
/// </remarks>
[Collection(nameof(RunsMeasuredInAChild))]
public sealed class DiffusiveMemoryTests(MeasuredRuns runs, ITestOutputHelper output)
{
    private const int Small = 128, Large = 256;

    /// <summary>
    /// A run's own working set, a node: the slope between two density grids of one model, which
    /// cancels whatever the run holds that does not grow with the grid.
    /// </summary>
    /// <remarks>
    /// Two geometries: a Cartesian tube with nothing in it, and a cylindrical bore whose earthed
    /// wall lies inside the density grid, so the run keeps the map of which conductor owns each
    /// node as well.
    /// </remarks>
    [Theory]
    [InlineData("tube")]
    [InlineData("bore")]
    public void ADiffusiveRunHoldsWhatTheEstimateCharges(string geometry)
    {
        var kind = geometry + "-plain";
        var small = runs.Optimized(output, kind, Small);
        var large = runs.Optimized(output, kind, Large);
        var nodes = (double)(large.Fact("nodes") - small.Fact("nodes"));

        var stated = (Stated(kind, Large) - Stated(kind, Small)) / nodes;

        var lower = (large.Peak.Least - small.Peak.Bound) / nodes;
        var upper = (large.Peak.Bound - small.Peak.Least) / nodes;

        output.WriteLine(
            $"{geometry}: {lower:F2} to {upper:F2} bytes a node between {small.Fact("nodes"):N0} and "
            + $"{large.Fact("nodes"):N0} nodes, {stated:F2} stated");

        MemoryVerdict.Within(
            $"the {geometry} run's slope", lower, upper, 0.90 * stated, 1.02 * stated, nodes,
            small.Peak.Foreign + large.Peak.Foreign);
    }

    /// <summary>
    /// What mean-field space charge adds to a run: the same run with it and without, at one
    /// grid, and at another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The run has to refresh inside the stepping loop</b>, and the test asserts that it did.
    /// The first Poisson solve is done before the loop, when the step's own arrays do not exist
    /// yet, and a run of one step reads 25 bytes a node for the self-field where the run reads
    /// 61: every later refresh solves on top of the step. A run too short to refresh would pass
    /// against a statement of 25 and say nothing about the peak.
    /// </para>
    /// <para>
    /// <b>Two geometries.</b> A Cartesian tube with nothing in it, where the self-field's mask
    /// fixes only the edges; and a cylindrical bore whose earthed wall lies inside the density
    /// grid, where the mask fixes every absorbing node of the wall and the solve is the
    /// axisymmetric one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("tube", Small)]
    [InlineData("tube", Large)]
    [InlineData("bore", Small)]
    [InlineData("bore", Large)]
    public void ASelfFieldHoldsWhatTheEstimateCharges(string geometry, int intervals)
    {
        var charged = runs.Optimized(output, geometry + "-charged", intervals);
        var plain = runs.Optimized(output, geometry + "-plain", intervals);
        var nodes = (double)charged.Fact("nodes");

        var stated = (Stated(geometry + "-charged", intervals) - Stated(geometry + "-plain", intervals)) / nodes;

        var lower = (charged.Peak.Least - plain.Peak.Bound) / nodes;
        var upper = (charged.Peak.Bound - plain.Peak.Least) / nodes;

        output.WriteLine(
            $"{geometry} self-field at {nodes:N0} nodes: {lower:F2} to {upper:F2} bytes a node over "
            + $"{charged.Fact("solves")} solves, {stated:F2} stated; the run alone "
            + $"{plain.Peak.Least / nodes:F2} to {plain.Peak.Bound / nodes:F2}");

        Assert.Equal(nodes, plain.Fact("nodes"));
        Assert.Equal(0, plain.Fact("solves"));

        Assert.True(
            charged.Fact("solves") >= 2,
            $"the self-field was solved {charged.Fact("solves")} time(s), so no refresh happened inside the loop");

        MemoryVerdict.Within(
            $"the {geometry} self-field", lower, upper, 0.90 * stated, 1.02 * stated, nodes,
            charged.Peak.Foreign + plain.Peak.Foreign);
    }

    /// <summary>What the estimate says a run holds, in bytes: every term, the solved field's included.</summary>
    private static double Stated(string kind, int intervals)
    {
        var root = Path.Combine(Path.GetTempPath(), "einzel-run-memory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "model.json");

            File.WriteAllText(path, RunMemoryCases.Document(kind, intervals));

            return EstimateCommand.Execute(path, calibrate: false).MemoryMiB * 1024.0 * 1024.0;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
