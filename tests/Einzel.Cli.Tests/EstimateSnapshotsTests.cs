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
        Assert.Equal("/args/--snapshots", refused.Error.Path);
        Assert.Contains("no diffusive mode", refused.Error.Constraint, StringComparison.Ordinal);

        // None asked for is the ordinary estimate.
        Assert.True(EstimateCommand.Execute(trajectory, calibrate: false).MemoryMiB >= 0.0);

        var (exit, _, stderr) = Run("estimate", trajectory, "--snapshots", "3", "--no-calibrate");

        output.WriteLine($"exit {exit}: {stderr.Trim()}");

        Assert.Equal(1, exit);
        Assert.Contains("no diffusive mode", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--snapshots</c> named without a number is refused by name, not read as none and billed
    /// as nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotsWithoutANumberAreRefused(bool followedByAnotherFlag)
    {
        var model = Model("tube-plain");

        // Followed by --json, the refusal is the JSON on stdout; alone, prose on stderr.
        var (exit, stdout, stderr) = followedByAnotherFlag
            ? Run("estimate", model, "--no-calibrate", "--snapshots", "--json")
            : Run("estimate", model, "--no-calibrate", "--snapshots");
        var said = followedByAnotherFlag ? stdout : stderr;

        output.WriteLine($"exit {exit}: {said.Trim()}");

        // The command line's own reader refuses a value flag given no value, at the flag, before
        // the estimate reads it.
        Assert.Equal(1, exit);
        Assert.Contains("--snapshots takes a value, and none was given", said, StringComparison.Ordinal);
        Assert.Contains("/args/--snapshots", said, StringComparison.Ordinal);
    }

    /// <summary>A mixture's run records no instants, so recorded instants are refused for it.</summary>
    [Fact]
    public void RecordedInstantsAreRefusedForAMixture()
    {
        var mixture = Model("tube-mixture");

        var refused = Assert.Throws<EinzelException>(
            () => EstimateCommand.Execute(mixture, calibrate: false, snapshots: 2));

        output.WriteLine(refused.Error.ToString());

        Assert.Equal("/args/--snapshots", refused.Error.Path);
        Assert.Contains("2 species", refused.Error.Constraint, StringComparison.Ordinal);
        Assert.True(EstimateCommand.Execute(mixture, calibrate: false).MemoryMiB > 0.0);
    }

    /// <summary>
    /// A study is refused a number of instants because it records none, and refused what is not a
    /// number because it is not one - the number parsed first, so <c>00</c> is read as none - and
    /// either way told to leave the flag out.
    /// </summary>
    /// <param name="instants">What follows the flag.</param>
    /// <param name="says">What the refusal says.</param>
    [Theory]
    [InlineData("3", "a study records none")]
    [InlineData("three", "'three' is not one")]
    public void AStudyIsRefusedSnapshotsWhateverTheyAre(string instants, string says)
    {
        Directory.CreateDirectory(_root);

        var study = Path.Combine(_root, "study.json");

        File.WriteAllText(study, """{ "schemaVersion": "0.1", "model": "model.json", "figureOfMerit": "transitTime" }""");

        var (exit, _, stderr) = Run("estimate", study, "--snapshots", instants);

        output.WriteLine($"exit {exit}: {stderr.Trim()}");

        Assert.Equal(1, exit);
        Assert.Contains(says, stderr, StringComparison.Ordinal);
        Assert.Contains("leave out --snapshots", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Under <c>--json</c> every refusal of <c>--snapshots</c> is an AGT-3 error on stdout, located at
    /// the flag - the CLI's own and the estimate's alike.
    /// </summary>
    [Theory]
    [InlineData("three", "takes a whole number")]
    [InlineData("3", "no diffusive mode")]
    public void ARefusalOfSnapshotsIsJsonUnderJson(string instants, string says)
    {
        var model = Model("tube-plain");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(model))!;

        document["transport"]!["mode"] = "trajectory";
        File.WriteAllText(model, document.ToJsonString());

        var (exit, stdout, _) = Run("estimate", model, "--no-calibrate", "--snapshots", instants, "--json");

        output.WriteLine($"exit {exit}: {stdout.Trim()}");

        Assert.Equal(1, exit);

        var error = System.Text.Json.Nodes.JsonNode.Parse(stdout)!["errors"]![0]!;

        Assert.Equal("/args/--snapshots", error["path"]!.GetValue<string>());
        Assert.Contains(says, error["constraint"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>A study asked for no recorded instants is asked for what a study records, and runs.</summary>
    /// <remarks>
    /// The number is what is asked, not its spelling: <c>00</c> is none as well as <c>0</c> is, and
    /// one instant is refused.
    /// </remarks>
    /// <param name="instants">What follows the flag.</param>
    /// <param name="refused">Whether the study is refused for it.</param>
    [Theory]
    [InlineData("0", false)]
    [InlineData("00", false)]
    [InlineData("1", true)]
    public void AStudyAskedForNoInstantsIsEstimated(string instants, bool refused)
    {
        var model = Model("tube-plain");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(model))!;

        document["parameters"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "spare": { "value": 1.0, "unit": "mm" } }""");
        File.WriteAllText(model, document.ToJsonString());

        var study = Path.Combine(_root, "study.json");

        File.WriteAllText(study, $$"""
            { "schemaVersion": "0.1", "name": "zero", "model": "{{Path.GetFileName(model)}}",
              "figureOfMerit": "transitTime", "ions": 9,
              "scan": { "parameter": "spare", "from": 1.0, "to": 2.0, "unit": "mm", "points": 3 } }
            """);

        var (exit, _, stderr) = Run("estimate", study, "--no-calibrate", "--snapshots", instants);

        output.WriteLine($"exit {exit}: {stderr.Trim()}");

        if (refused)
        {
            Assert.Equal(1, exit);
            Assert.Contains("a study records none", stderr, StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual(1, exit);
        }
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
