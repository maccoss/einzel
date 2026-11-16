using System.Text.Json;

using Xunit.Abstractions;
using Xunit.Sdk;

namespace Einzel.Fields.Tests;

/// <summary>
/// A measuring child's numbers are used only if the whole child succeeded, and a child that did
/// not says how far it got.
/// </summary>
[Collection(nameof(MemoryMeasurement))]
public sealed class ChildProtocolTests(ITestOutputHelper output)
{
    private const string Key = "rods=0.0001";

    private static readonly string Version = Environment.Version.ToString();

    /// <summary>A complete, successful run hands its case over.</summary>
    [Fact]
    public void ACompleteRunIsUsed()
    {
        var run = MemoryCases.Interpret([Key], Exit(0, [Runtime(), Begin(), Case(), "END"]), Version);

        Assert.Null(run.Failure);
        Assert.Equal(7, MeasuredBuilds.Find(run, "optimized", output, Key).Peak.Readings);
    }

    /// <summary>
    /// A child that measured every case and then refused its tier fails every case, with its text.
    /// </summary>
    /// <remarks>
    /// The case lines are all there: the child refuses after its last build, when a warm-up would
    /// have promoted first-tier code. Handing those cases over anyway was the defect.
    /// </remarks>
    [Fact]
    public void ALateTierRefusalFailsEveryCase()
    {
        var run = MemoryCases.Interpret(
            [Key],
            Exit(2, [Runtime(), Begin(), Case()], "after the builds: this process was to stay first-tier"),
            Version);

        var failed = Assert.Throws<FailException>(() => MeasuredBuilds.Find(run, "first-tier", output, Key));

        Assert.Contains("failed as a whole", failed.Message, StringComparison.Ordinal);
        Assert.Contains("exited 2", failed.Message, StringComparison.Ordinal);
        Assert.Contains("after the builds", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>Each way a run can be incomplete fails the case it did report.</summary>
    [Theory]
    [InlineData("no end", "stopped without saying it had finished")]
    [InlineData("error stream", "wrote to its error stream")]
    [InlineData("other runtime", "ran on .NET 9.9.9")]
    [InlineData("no runtime", "did not say which runtime")]
    public void AnIncompleteRunFailsEveryCase(string how, string said)
    {
        ChildExit exited = how switch
        {
            "no end" => Exit(0, [Runtime(), Begin(), Case()]),
            "error stream" => Exit(0, [Runtime(), Begin(), Case(), "END"], "Unhandled exception."),
            "other runtime" => Exit(0, ["RUNTIME 9.9.9", Begin(), Case(), "END"]),
            _ => Exit(0, [Begin(), Case(), "END"]),
        };

        var run = MemoryCases.Interpret([Key], exited, Version);
        var failed = Assert.Throws<FailException>(() => MeasuredBuilds.Find(run, "optimized", output, Key));

        Assert.Contains(said, failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real child that dies part way fails the case it finished, and says which it was building.
    /// </summary>
    [Fact]
    public void AChildThatDiesPartWayFailsTheCasesItFinished()
    {
        string[] cases = ["ball-flat=0.016", "test-exit=1"];
        var exited = MeasuringChild.Run([MemoryCases.Command, JitTier.Optimized.Name, .. cases], JitTier.Optimized);
        var run = MemoryCases.Interpret(cases, exited, Version);

        output.WriteLine(run.Text);

        Assert.True(run.Cases.ContainsKey("ball-flat=0.016"), "the first case was not reported, so this tests nothing");
        Assert.NotNull(run.Failure);
        Assert.Contains("exited 3", run.Failure, StringComparison.Ordinal);
        Assert.Contains("building test-exit=1", run.Failure, StringComparison.Ordinal);
        Assert.Throws<FailException>(() => MeasuredBuilds.Find(run, "optimized", output, "ball-flat=0.016"));
    }

    /// <summary>
    /// A real child that does not finish is stopped, and what it wrote before is kept: how far it
    /// got, and what it was doing.
    /// </summary>
    [Fact]
    public void AChildThatRunsOutOfTimeKeepsWhatItWrote()
    {
        string[] cases = ["ball-flat=0.016", "test-hang=1"];
        var exited = MeasuringChild.Run(
            [MemoryCases.Command, JitTier.Optimized.Name, .. cases], JitTier.Optimized, TimeSpan.FromSeconds(20));
        var run = MemoryCases.Interpret(cases, exited, Version);

        output.WriteLine(run.Text);

        Assert.NotNull(exited.Problem);
        Assert.Contains(exited.Lines, line => line.StartsWith("RUNTIME ", StringComparison.Ordinal));
        Assert.True(run.Cases.ContainsKey("ball-flat=0.016"), "the case it finished before hanging was lost");
        Assert.Contains("did not finish in", run.Failure, StringComparison.Ordinal);
        Assert.Contains("building test-hang=1", run.Failure, StringComparison.Ordinal);
    }

    /// <summary>An unknown command, or none, is refused with a usage line and a non-zero exit.</summary>
    [Fact]
    public void AnUnknownCommandIsRefused()
    {
        var exited = MeasuringChild.Run(["no-such-command"], JitTier.Optimized);

        Assert.Equal(64, exited.ExitCode);
        Assert.Contains("usage:", exited.Errors, StringComparison.Ordinal);
    }

    private static string Runtime() => "RUNTIME " + Version;

    private static string Begin() => "BEGIN " + Key;

    private static string Case() => "CASE " + JsonSerializer.Serialize(
        new CaseResult(Key, new MeasuredBuild(new LivePeak(1, 2, 2, 1, 7, 3, 0, 0, 0, null), -1, -1), null));

    private static ChildExit Exit(int code, string[] lines, string errors = "") => new(code, lines, errors, null);
}
