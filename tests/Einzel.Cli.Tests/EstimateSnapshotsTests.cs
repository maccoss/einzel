using Einzel.Commands;
using Einzel.Core.Errors;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The diffusive working set and its recorded instants are billed wherever a run steps a
/// density, and only there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asked of every mode the run uses, not the model's own.</b> A trajectory model whose
/// sequence enters a diffusive phase steps a density there, through the same arrays a wholly
/// diffusive run holds; routing the estimate on the model's own mode billed it nothing, and
/// <c>--snapshots</c> changed nothing.
/// </para>
/// <para>
/// <b>Refused where it cannot apply.</b> A recorded instant is a copy of a density, so a run that
/// steps none records none; accepting the option and billing nothing would let a caller believe
/// an animation of a trajectory model had been costed.
/// </para>
/// </remarks>
public sealed class EstimateSnapshotsTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-estimate-snapshots", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Model(string kind, int intervals = 64)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, kind + ".json");

        File.WriteAllText(path, RunMemoryCases.Document(kind, intervals));

        return path;
    }

    /// <summary>
    /// A trajectory model that enters a diffusive phase is billed the density's working set, and
    /// a recorded instant on top of it.
    /// </summary>
    [Fact]
    public void ATrajectoryModelEnteringADiffusivePhaseIsBilledItsDensity()
    {
        const int Intervals = 64;
        var nodes = (Intervals + 1.0) * (Intervals + 1.0);

        var entered = Model("tube-entered", Intervals);
        var plain = Model("tube-plain", Intervals);

        var bytes = (string path, int snapshots) =>
            EstimateCommand.Execute(path, calibrate: false, snapshots: snapshots).MemoryMiB * 1024.0 * 1024.0;

        var enteredPerNode = bytes(entered, 0) / nodes;
        var plainPerNode = bytes(plain, 0) / nodes;
        var perInstant = (bytes(entered, 60) - bytes(entered, 0)) / nodes / 60.0;

        output.WriteLine(
            $"entered {enteredPerNode:F2} B/node, the diffusive tube {plainPerNode:F2}; each instant {perInstant:F3}");

        Assert.Equal(plainPerNode, enteredPerNode, 6);
        Assert.Equal(sizeof(double), perInstant, 6);
    }

    /// <summary>
    /// <c>--snapshots</c> on a run that steps no density is refused by name, through the command
    /// and through the CLI.
    /// </summary>
    [Fact]
    public void RecordedInstantsAreRefusedForARunThatStepsNoDensity()
    {
        var trajectory = Model("tube-plain");

        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(trajectory))!;

        document["transport"]!["mode"] = "trajectory";
        File.WriteAllText(trajectory, document.ToJsonString());

        var refused = Assert.Throws<EinzelException>(
            () => EstimateCommand.Execute(trajectory, calibrate: false, snapshots: 3));

        output.WriteLine(refused.Error.ToString());

        Assert.Equal(ErrorCodes.SchemaInvalid, refused.Error.Code);
        Assert.Contains("no diffusive mode", refused.Error.Constraint, StringComparison.Ordinal);

        // None asked for is the ordinary estimate.
        Assert.True(EstimateCommand.Execute(trajectory, calibrate: false).MemoryMiB >= 0.0);

        var (exit, _, stderr) = Run("estimate", trajectory, "--snapshots", "3", "--no-calibrate");

        output.WriteLine($"exit {exit}: {stderr.Trim()}");

        Assert.Equal(1, exit);
        Assert.Contains("no diffusive mode", stderr, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
