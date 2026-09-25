using Einzel.Commands;
using Einzel.Fields.Solved;
using Einzel.Library;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel estimate</c> bills memory from the solver's own statement of what it holds, not
/// from a constant of its own.
/// </summary>
/// <remarks>
/// <para>
/// The constant was 48 bytes a node for every solve, plane and volume alike, and a volume
/// solve on the Galerkin hierarchy holds 240. The Astral at a 1 mm cell was billed 3.1 GiB and
/// needs 15. What was measured against real builds is in <c>SolveMemoryTests</c>; what is
/// asserted here is that the estimate asks for that statement rather than restating it, and
/// that it counts the channels a driven geometry decomposes into - which it could not, because
/// it copied the geometry without its drives.
/// </para>
/// <para>
/// Uncalibrated, so nothing is solved or flown: the memory is arithmetic on the declaration,
/// and these run in the time it takes to validate a template.
/// </para>
/// </remarks>
public sealed class SolveMemoryEstimateTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-solve-memory", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private EstimateOutcome Estimate(string template)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, template + ".json");
        File.WriteAllText(path, DeviceTemplates.Read(template));

        return EstimateCommand.Execute(path, calibrate: false);
    }

    /// <summary>A static volume geometry on the Galerkin hierarchy: 240 bytes a node.</summary>
    [Fact]
    public void AStaticVolumeSolveIsBilledTheGalerkinWorkingSet()
    {
        var outcome = Estimate("astral-3d");
        var element = Assert.Single(outcome.Elements, e => e.Nodes.Count == 3);

        var perNode = element.MemoryMiB * 1024.0 * 1024.0 / element.NodeCount;

        output.WriteLine($"{element.NodeCount:N0} nodes, {element.MemoryMiB:F1} MiB, {perNode:F1} B/node");
        output.WriteLine(outcome.Basis);

        Assert.Equal(SolveMemoryModel.VolumeGalerkinPeak, perNode, 6);
        Assert.Contains("Galerkin multigrid hierarchy", outcome.Basis, StringComparison.Ordinal);

        // The template solves two mirror cross-sections before the volume, and their
        // potentials are kept while the volume solves - so the model's peak is the volume's
        // working set plus what the elements before it retain, not the volume's alone.
        var before = outcome.Elements
            .Where(e => e.Index < element.Index)
            .Sum(e => e.NodeCount * SolveMemoryModel.RetainedPerChannel);

        Assert.True(before > 0.0, "the template's plane solves come first");
        Assert.Equal(element.MemoryMiB + (before / (1024.0 * 1024.0)), outcome.MemoryMiB, 6);
    }

    /// <summary>
    /// Two basis channels: one working set, and one kept potential for the channel solved first.
    /// </summary>
    [Fact]
    public void ADrivenVolumeSolveIsBilledItsChannels()
    {
        var outcome = Estimate("linear-ion-trap-3d");
        var element = Assert.Single(outcome.Elements, e => e.Nodes.Count == 3);

        var perNode = element.MemoryMiB * 1024.0 * 1024.0 / element.NodeCount;

        output.WriteLine($"{element.NodeCount:N0} nodes, {element.MemoryMiB:F1} MiB, {perNode:F1} B/node");

        Assert.Equal(
            SolveMemoryModel.VolumeGalerkinPeak + SolveMemoryModel.RetainedPerChannel,
            perNode,
            6);
        Assert.Contains("2 basis channels", outcome.Basis, StringComparison.Ordinal);
    }

    /// <summary>A plane solve is billed the plane statement, which was also a third of the truth.</summary>
    [Fact]
    public void APlaneSolveIsBilledThePlaneWorkingSet()
    {
        var outcome = Estimate("quadrupole");
        var element = Assert.Single(outcome.Elements, e => e.Nodes.Count == 2);

        var perNode = element.MemoryMiB * 1024.0 * 1024.0 / element.NodeCount;

        output.WriteLine($"{element.NodeCount:N0} nodes, {element.MemoryMiB:F3} MiB, {perNode:F1} B/node");

        Assert.Equal(SolveMemoryModel.PlanePeak, perNode, 6);
    }
}
