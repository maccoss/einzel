using System.Text.Json;

using Einzel.Testing;

using Xunit.Abstractions;
using Xunit.Sdk;

namespace Einzel.Fields.Tests;

/// <summary>
/// A measuring child's failure belongs to whatever caused it: to one case by name, or - only
/// when it is about every case - to all of them.
/// </summary>
/// <remarks>
/// These start children of their own and read what they say; nothing is measured in this
/// process, so they run in parallel with the rest of the suite.
/// </remarks>
public sealed class ChildProtocolTests(ITestOutputHelper output)
{
    private const string Key = "rods=0.0001";
    private const string Other = "sphere=0.000625";
    private const string Small = "ball-flat=0.016";

    private static readonly string Version = Environment.Version.ToString();

    /// <summary>A complete, successful run hands its case over.</summary>
    [Fact]
    public void ACompleteRunIsUsed()
    {
        var session = MeasuringChild.Interpret([Key], Exit(0, [Runtime(), Begin(), Case(), "END"]), Version);

        Assert.Null(session.Failure);
        Assert.Empty(session.Unfinished);
        Assert.Equal(7, MeasuredCases.Find(Whole(session), "optimized", output, Key).Peak.Readings);
    }

    /// <summary>
    /// A child that measured a case and then found itself in the wrong tier fails every case.
    /// </summary>
    /// <remarks>
    /// The child checks its tier again after each build, when a warm-up would have promoted
    /// first-tier code, and before reporting the case. The cases reported before are in the
    /// output all the same, and handing those over was the defect.
    /// </remarks>
    [Fact]
    public void ALateTierRefusalFailsEveryCase()
    {
        var session = MeasuringChild.Interpret(
            [Key, Other],
            Exit(2, [Runtime(), Begin(), Case(), "BEGIN " + Other, "REFUSED after building " + Other + ": this process was to stay first-tier"]),
            Version);

        var failed = Assert.Throws<FailException>(() => MeasuredCases.Find(Whole(session), "first-tier", output, Key));

        Assert.Contains("failed as a whole", failed.Message, StringComparison.Ordinal);
        Assert.Contains("not in the tier it was asked for", failed.Message, StringComparison.Ordinal);
        Assert.Contains("after building", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>What is about every case fails the case that was reported.</summary>
    [Theory]
    [InlineData("no end", "stopped without saying it had finished")]
    [InlineData("error stream", "wrote to its error stream outside any case")]
    [InlineData("other runtime", "ran on .NET 9.9.9")]
    [InlineData("no runtime", "did not say which runtime")]
    [InlineData("other collector", "ran with the collector server Interactive")]
    [InlineData("exit between", "exited 5 between cases")]
    public void WhatIsAboutEveryCaseFailsEveryCase(string how, string said)
    {
        ChildExit exited = how switch
        {
            "no end" => Exit(0, [Runtime(), Begin(), Case()]),
            "error stream" => Exit(0, [Runtime(), Begin(), Case(), "END"], "Unhandled exception."),
            "other runtime" => Exit(0, ["RUNTIME 9.9.9", "GC " + MeasuringChild.ExpectedGc, Begin(), Case(), "END"]),
            "other collector" => Exit(0, ["RUNTIME " + Version, "GC server Interactive", Begin(), Case(), "END"]),
            "exit between" => Exit(5, [Runtime(), Begin(), Case()]),
            _ => Exit(0, ["GC " + MeasuringChild.ExpectedGc, Begin(), Case(), "END"]),
        };

        var session = MeasuringChild.Interpret([Key], exited, Version);
        var failed = Assert.Throws<FailException>(() => MeasuredCases.Find(Whole(session), "optimized", output, Key));

        Assert.Contains("failed as a whole", failed.Message, StringComparison.Ordinal);
        Assert.Contains(said, failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What one case did wrong fails that case by name, and the case measured beside it is used.
    /// </summary>
    [Theory]
    [InlineData("truncated case", "a case line that does not read")]
    [InlineData("null case", "a case line that does not read: CASE null")]
    [InlineData("error stream", "a complaint")]
    [InlineData("exit during", "exited 3 while building " + Other)]
    [InlineData("out of time", Other + " did not finish in 2 s")]
    public void WhatOneCaseDidFailsThatCaseAlone(string how, string said)
    {
        string[] first = [Runtime(), Begin(), Case()];

        ChildExit exited = how switch
        {
            "truncated case" => Exit(0, [.. first, "BEGIN " + Other, Case(Other)[..40], "END"]),
            "null case" => Exit(0, [.. first, "BEGIN " + Other, "CASE null", "END"]),
            "error stream" => Exit(
                0,
                [.. first, "BEGIN " + Other, Case(Other), "END"],
                $"##case-begins {Key}\n##case-ends {Key}\n##case-begins {Other}\na complaint\n##case-ends {Other}"),
            "exit during" => Exit(3, [.. first, "BEGIN " + Other], $"##case-begins {Other}\nUnhandled exception."),
            _ => new ChildExit(
                -1, [.. first.SelectMany(line => line.Split('\n')), "BEGIN " + Other], [], Other + " did not finish in 2 s, so the measuring child was stopped."),
        };

        // A case whose child went down with it leaves the case after it for a fresh child; one
        // that only failed lets the same child go on to its end.
        var downWithIt = how is "exit during" or "out of time";
        string[] asked = downWithIt ? [Key, Other, Small] : [Key, Other];

        var session = MeasuringChild.Interpret(asked, exited, Version);
        var run = Whole(session);

        Assert.Null(session.Failure);
        Assert.Equal(7, MeasuredCases.Find(run, "optimized", output, Key).Peak.Readings);

        var failed = Assert.Throws<FailException>(() => MeasuredCases.Find(run, "optimized", output, Other));

        Assert.Contains("only this case did", failed.Message, StringComparison.Ordinal);
        Assert.Contains(said, failed.Message, StringComparison.Ordinal);

        Assert.Equal(downWithIt ? [Small] : [], session.Unfinished);

        if (how == "exit during")
        {
            Assert.Contains("Unhandled exception.", failed.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A real child that dies part way fails the case it died in, by name; the case before it is
    /// used, and the case after it is measured by a fresh child.
    /// </summary>
    [Fact]
    public void AChildThatDiesPartWayFailsOnlyTheCaseItDiedIn()
    {
        string[] cases = [Small, "test-exit=1", "ball-flat=0.032"];
        var run = MeasuringChild.Measure(MemoryCases.Host, JitTier.Optimized, cases);

        output.WriteLine(run.Text);

        Assert.Null(run.Failure);
        Assert.True(MeasuredCases.Find(run, "optimized", output, Small).Peak.Readings > 0);
        Assert.True(MeasuredCases.Find(run, "optimized", output, "ball-flat=0.032").Peak.Readings > 0);

        var failed = Assert.Throws<FailException>(() => MeasuredCases.Find(run, "optimized", output, "test-exit=1"));

        Assert.Contains("only this case did", failed.Message, StringComparison.Ordinal);
        Assert.Contains("exited 3 while building test-exit=1", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real child stuck in one case is stopped when that case's own time is up, counted from
    /// the moment the case began - however long the child had been running by then - and the
    /// cases around it are measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here races. A case's time starts when this process reads the line saying that
    /// case began, which the child writes only after it has finished the case before; so the
    /// stuck case is given its two seconds whenever it starts, and nothing is assumed about how
    /// long a cold child takes to get there. This used to give a child twenty seconds in all to
    /// start, build a real case twice and hang, and on a loaded runner the child could be
    /// stopped before its first case was reported.
    /// </para>
    /// <para>
    /// The first case sleeps two seconds, twice - built once unmeasured, once measured - so the
    /// child is more than four seconds old when the second begins. The second sleeps half a
    /// second, twice, and is allowed three: inside its own time, and long past it if time were
    /// counted from the child's start, or as one limit for the whole child. Then the stuck one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACaseOutOfTimeIsStoppedOnItsOwnClock()
    {
        string[] cases = ["test-slow=2", "test-slow=0.5", "test-hang=1", Small];

        var budget = new ChildBudget(
            ChildBudget.Default.Startup,
            key => key switch
            {
                "test-slow=0.5" => TimeSpan.FromSeconds(3),
                "test-hang=1" => TimeSpan.FromSeconds(2),
                _ => ChildBudget.Default.Case(key),
            });

        var run = MeasuringChild.Measure(MemoryCases.Host, JitTier.Optimized, cases, budget);

        output.WriteLine(run.Text);

        Assert.Null(run.Failure);
        Assert.NotNull(MeasuredCases.Find(run, "optimized", output, "test-slow=2"));
        Assert.NotNull(MeasuredCases.Find(run, "optimized", output, "test-slow=0.5"));

        // And the case after the stuck one, by a fresh child.
        Assert.True(MeasuredCases.Find(run, "optimized", output, Small).Peak.Readings > 0);

        var failed = Assert.Throws<FailException>(() => MeasuredCases.Find(run, "optimized", output, "test-hang=1"));

        Assert.Contains("only this case did", failed.Message, StringComparison.Ordinal);
        Assert.Contains("test-hang=1 did not finish in 2 s", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real case that writes to the error stream fails alone, with what it wrote, and the same
    /// child goes on to measure the case after it.
    /// </summary>
    [Fact]
    public void ACaseThatWritesToTheErrorStreamFailsAlone()
    {
        string[] cases = [Small, "test-stderr=1", "ball-flat=0.032"];
        var exited = MeasuringChild.Run([MemoryCases.Host.Command, JitTier.Optimized.Name, .. cases], JitTier.Optimized);
        var session = MeasuringChild.Interpret(cases, exited, Version);
        var run = Whole(session);

        output.WriteLine(session.Text);

        Assert.Null(session.Failure);
        Assert.Empty(session.Unfinished);
        Assert.True(MeasuredCases.Find(run, "optimized", output, Small).Peak.Readings > 0);
        Assert.True(MeasuredCases.Find(run, "optimized", output, "ball-flat=0.032").Peak.Readings > 0);

        var failed = Assert.Throws<FailException>(() => MeasuredCases.Find(run, "optimized", output, "test-stderr=1"));

        Assert.Contains("only this case did", failed.Message, StringComparison.Ordinal);
        Assert.Contains("a complaint from inside the build", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The child runs in a controlled environment: what the parent was started with neither
    /// writes to its error stream nor changes the collector it measures under.
    /// </summary>
    /// <remarks>
    /// <c>COREHOST_TRACE</c> makes the host write a trace to the error stream, which fails every
    /// case; <c>DOTNET_gcServer</c> and <c>DOTNET_gcConcurrent</c> change what a reading of the live
    /// set is a reading of. Passed here in the environment a child would inherit, they reach it
    /// only if its environment is not replaced.
    /// </remarks>
    [Fact]
    public void TheParentsEnvironmentDoesNotReachTheChild()
    {
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = "path",
            ["COREHOST_TRACE"] = "1",
            ["DOTNET_gcServer"] = "1",
            ["DOTNET_gcConcurrent"] = "1",
            ["DOTNET_GCgen0size"] = "0x10000000",
            ["DOTNET_TieredCompilation"] = "1",
            ["SOME_TOOL_SETTING"] = "x",
        };

        MeasuringChild.Controlled(environment, JitTier.Optimized);

        Assert.Equal("path", environment["PATH"]);
        Assert.Equal("0", environment["DOTNET_gcServer"]);
        Assert.Equal("0", environment["DOTNET_gcConcurrent"]);
        Assert.Equal("0", environment["DOTNET_TieredCompilation"]);
        Assert.False(environment.ContainsKey("COREHOST_TRACE"));
        Assert.False(environment.ContainsKey("DOTNET_GCgen0size"));
        Assert.False(environment.ContainsKey("SOME_TOOL_SETTING"));

        // And a real child says what it ran with.
        string[] cases = [Small];
        var session = MeasuringChild.Interpret(
            cases, MeasuringChild.Run([MemoryCases.Host.Command, JitTier.Optimized.Name, .. cases], JitTier.Optimized), Version);

        output.WriteLine(session.Text);

        Assert.Null(session.Failure);
        Assert.Contains("GC " + MeasuringChild.ExpectedGc, session.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host is looked for where the running runtime is installed first, and only after
    /// that, and after what the environment names, as this process itself and on the path.
    /// </summary>
    /// <remarks>
    /// The last two can be another installation. They are safe to fall back to because a child
    /// that ran on another runtime says so and is refused by name, which
    /// <see cref="WhatIsAboutEveryCaseFailsEveryCase"/> holds.
    /// </remarks>
    [Fact]
    public void TheHostIsLookedForPreciselyFirstAndOnThePathLast()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var candidates = MeasuringChild.HostCandidates().ToList();
        var runtime = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());

        output.WriteLine(string.Join(Environment.NewLine, candidates));

        Assert.Equal(Path.Combine(runtime.Parent!.Parent!.Parent!.FullName, name), candidates[0]);

        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, name))
            .ToList();

        Assert.NotEmpty(onPath);

        // Every path entry is offered, and all of them after everything else.
        Assert.Equal(onPath, candidates.Skip(candidates.Count - onPath.Count));
        Assert.NotNull(MeasuringChild.Host());
    }

    /// <summary>An unknown command, or none, is refused with a usage line and a non-zero exit.</summary>
    [Fact]
    public void AnUnknownCommandIsRefused()
    {
        var exited = MeasuringChild.Run(["no-such-command"], JitTier.Optimized);

        Assert.Equal(64, exited.ExitCode);
        Assert.Contains("usage:", exited.Errors, StringComparison.Ordinal);
    }

    private static ChildRun Whole(ChildSession session) => new(session.Failure, session.Cases, session.Text);

    private static string Runtime() => "RUNTIME " + Version + "\nGC " + MeasuringChild.ExpectedGc;

    private static string Begin() => "BEGIN " + Key;

    private static string Case(string key = Key) => "CASE " + JsonSerializer.Serialize(
        new CaseResult(key, new MeasuredBuild(new LivePeak(1, 2, 2, 1, 7, 3, 0, 0, 0, 0, 0, null), [], null), null));

    private static ChildExit Exit(int code, string[] lines, string errors = "") =>
        new(
            code,
            [.. lines.SelectMany(line => line.Split('\n'))],
            [.. errors.Split('\n', StringSplitOptions.RemoveEmptyEntries)],
            null);
}
