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
    /// A run's own working set, a node, in each configuration the estimate bills differently:
    /// the slope between two density grids of one model, which cancels whatever the run holds
    /// that does not grow with the grid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One case for each term of the estimate's statement, so that no term stands unmeasured: an
    /// estimate that runs under is discovered by the run failing, which is what GRD-8's gate is
    /// there to prevent. The plain run on a Cartesian tube is the run's own arrays; in a
    /// cylindrical bore whose earthed wall lies inside the density grid it keeps the map of
    /// which conductor owns each node as well. Then the implicit scheme; an RF drive, whose
    /// cycle-averaged well is cached; four recorded instants; two species, each with all of it
    /// again; two species sharing a self-field; and a sequence whose second phase ramps the
    /// wall's potential, re-assembling the operator at every step, through the sequenced path.
    /// </para>
    /// <para>
    /// Each asserts that the run was the one it meant - that the ramped one re-assembled, that
    /// the charged one refreshed inside its loop - since a run that did not would hold less and
    /// pass.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("tube-plain")]
    [InlineData("bore-plain")]
    [InlineData("tube-implicit")]
    [InlineData("tube-driven")]
    [InlineData("tube-snapshots")]
    [InlineData("tube-mixture")]
    [InlineData("tube-mixture-charged")]
    [InlineData("bore-ramped")]
    public void ADiffusiveRunHoldsWhatTheEstimateCharges(string kind)
    {
        var small = runs.Optimized(output, kind, Small);
        var large = runs.Optimized(output, kind, Large);
        var nodes = (double)(large.Fact("nodes") - small.Fact("nodes"));

        var stated = (Stated(kind, Large) - Stated(kind, Small)) / nodes;

        var lower = (large.Peak.Least - small.Peak.Bound) / nodes;
        var upper = (large.Peak.Bound - small.Peak.Least) / nodes;

        output.WriteLine(
            $"{kind}: {lower:F2} to {upper:F2} bytes a node between {small.Fact("nodes"):N0} and "
            + $"{large.Fact("nodes"):N0} nodes, {stated:F2} stated");

        foreach (var run in (MeasuredBuild[])[small, large])
        {
            if (kind == "bore-ramped")
            {
                Assert.True(
                    run.Fact("assemblies") >= 100,
                    $"the operator was assembled {run.Fact("assemblies")} time(s), so the ramp did not re-assemble it as it went");
            }

            if (kind == "tube-mixture-charged")
            {
                Assert.True(
                    run.Fact("solves") >= 2,
                    $"the self-field was solved {run.Fact("solves")} time(s), so no refresh happened inside the loop");
            }
        }

        MemoryVerdict.Within(
            $"the {kind} run's slope", lower, upper, 0.90 * stated, 1.02 * stated, nodes,
            small.Peak.Foreign + large.Peak.Foreign);
    }

    /// <summary>
    /// What a configuration adds to a run: the same run with it and without, at one grid, against
    /// what the estimate says it adds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A term is a small part of a run - a self-field is 62 bytes a node on 248, a recorded
    /// instant 8 - so held to the whole run's band an array too many in it would pass. Held to
    /// its own, it does not: one array of the grid is an eighth of a self-field and all of an
    /// instant.
    /// </para>
    /// <para>
    /// <b>A charged run has to refresh inside the stepping loop</b>, and the test asserts that it
    /// did. The first Poisson solve is done before the loop, when the step's own arrays do not
    /// exist yet, and a run of one step reads 25 bytes a node for the self-field where the run
    /// reads 61: every later refresh solves on top of the step. A run too short to refresh would
    /// pass against a statement of 25 and say nothing about the peak.
    /// </para>
    /// <para>
    /// <b>Two geometries for the self-field.</b> A Cartesian tube with nothing in it, where its
    /// mask fixes only the edges; and a cylindrical bore whose earthed wall lies inside the
    /// density grid, where the mask fixes every absorbing node of the wall and the solve is the
    /// axisymmetric one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("tube-charged", "tube-plain", Small)]
    [InlineData("tube-charged", "tube-plain", Large)]
    [InlineData("bore-charged", "bore-plain", Small)]
    [InlineData("bore-charged", "bore-plain", Large)]
    [InlineData("tube-mixture-charged", "tube-mixture", Large)]
    [InlineData("tube-mixture", "tube-plain", Large)]
    [InlineData("tube-driven", "tube-plain", Large)]
    [InlineData("tube-snapshots", "tube-plain", Large)]
    public void AConfigurationAddsWhatTheEstimateCharges(string with, string without, int intervals)
    {
        var added = runs.Optimized(output, with, intervals);
        var plain = runs.Optimized(output, without, intervals);
        var nodes = (double)added.Fact("nodes");

        var stated = (Stated(with, intervals) - Stated(without, intervals)) / nodes;

        var lower = (added.Peak.Least - plain.Peak.Bound) / nodes;
        var upper = (added.Peak.Bound - plain.Peak.Least) / nodes;

        output.WriteLine(
            $"{with} over {without} at {nodes:N0} nodes: {lower:F2} to {upper:F2} bytes a node over "
            + $"{added.Fact("solves")} solves, {stated:F2} stated; the run without "
            + $"{plain.Peak.Least / nodes:F2} to {plain.Peak.Bound / nodes:F2}");

        Assert.Equal(nodes, plain.Fact("nodes"));
        Assert.Equal(0, plain.Fact("solves"));

        if (with.EndsWith("-charged", StringComparison.Ordinal))
        {
            Assert.True(
                added.Fact("solves") >= 2,
                $"the self-field was solved {added.Fact("solves")} time(s), so no refresh happened inside the loop");
        }

        MemoryVerdict.Within(
            $"what {with} adds", lower, upper, 0.90 * stated, 1.02 * stated, nodes,
            added.Peak.Foreign + plain.Peak.Foreign);
    }

    /// <summary>
    /// A run with a self-field, measured four times in one process, holds the same each time and
    /// leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// This is what five sampled repetitions that had to agree were for: something kept from one
    /// run into the next - a pool or a cache that grows - which no single measurement shows,
    /// since each takes its own baseline.
    /// </remarks>
    [Theory]
    [InlineData("tube")]
    [InlineData("bore")]
    public void ARepeatedRunLeavesNothingBehind(string geometry)
    {
        var measured = runs.Optimized(output, geometry + "-charged", Small, times: 4).Every;

        output.WriteLine(string.Join("; ", measured.Select(r => $"{r.Least:N0} to {r.Bound:N0}, {r.Retained:N0} left")));

        MemoryVerdict.Repeatable($"the charged {geometry} run", measured);
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

            // Told how many instants the run records, as `einzel estimate --snapshots` is.
            var snapshots = kind.EndsWith("-snapshots", StringComparison.Ordinal) ? RunMemoryCases.SnapshotSeconds.Length : 0;

            return EstimateCommand.Execute(path, calibrate: false, snapshots: snapshots).MemoryMiB * 1024.0 * 1024.0;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
