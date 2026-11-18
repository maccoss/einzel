using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Testing;

/// <summary>One build measured in a child process: what it held, and what it said about itself.</summary>
/// <param name="Peak">What the build held at its most.</param>
/// <param name="Facts">
/// What the case reports beside the measurement, by name - the levels a forced solve descended,
/// how often a self-field was solved - for the test to check the build was the one it meant.
/// </param>
/// <param name="Again">
/// The same build measured again in the same process, as many times more as the case asked
/// for, or null: for a test that what one run leaves behind does not grow.
/// </param>
internal sealed record MeasuredBuild(LivePeak Peak, Dictionary<string, double> Facts, List<LivePeak>? Again)
{
    /// <summary>Every measurement of the build, the first included.</summary>
    internal IReadOnlyList<LivePeak> Every => [Peak, .. Again ?? []];

    /// <summary>A fact the case reported, as a whole number; fails by name if it reported none.</summary>
    internal int Fact(string name) => Facts.TryGetValue(name, out var value)
        ? (int)value
        : throw new KeyNotFoundException($"the measured case reported no '{name}'");
}

/// <summary>What the child printed for one case: a measurement, or why there is none.</summary>
/// <param name="Key">The case, as <see cref="MeasuringChild.Key"/> writes it.</param>
/// <param name="Build">The measurement, or null if the case failed.</param>
/// <param name="Failure">Why the case failed, or null.</param>
internal sealed record CaseResult(string Key, MeasuredBuild? Build, string? Failure);

/// <summary>Everything the child processes of one measurement reported.</summary>
/// <param name="Failure">
/// Why no case can be used - a child did not start, refused its tier, ran another runtime or
/// collector - or null.
/// </param>
/// <param name="Cases">Each case's result, by key: a measurement, or why that case has none.</param>
/// <param name="Text">What the children wrote, both streams, for a failing test to show.</param>
internal sealed record ChildRun(string? Failure, IReadOnlyDictionary<string, CaseResult> Cases, string Text);

/// <summary>What one child process came to.</summary>
/// <param name="Failure">Why the child as a whole cannot be used, or null.</param>
/// <param name="Cases">The cases it settled: measured, or failed by name.</param>
/// <param name="Unfinished">The cases it was asked for and never reached, in order.</param>
/// <param name="Text">What it wrote, both streams.</param>
internal sealed record ChildSession(
    string? Failure, IReadOnlyDictionary<string, CaseResult> Cases, IReadOnlyList<string> Unfinished, string Text);

/// <summary>How a child process ended.</summary>
/// <param name="ExitCode">Its exit code; -1 if it was stopped or never started.</param>
/// <param name="Lines">The non-empty lines of its standard output, as far as it got.</param>
/// <param name="ErrorLines">The non-empty lines of its error stream.</param>
/// <param name="Problem">What went wrong in running it - it did not start, it ran out of time - or null.</param>
internal sealed record ChildExit(int ExitCode, IReadOnlyList<string> Lines, IReadOnlyList<string> ErrorLines, string? Problem)
{
    /// <summary>The error stream as one text.</summary>
    internal string Errors => string.Join(Environment.NewLine, ErrorLines);
}

/// <summary>A build a child can measure.</summary>
/// <param name="Build">The build; run once unmeasured and then measured.</param>
/// <param name="Facts">What to report beside the measurement, read after the measured build; or null.</param>
/// <param name="CollectEveryArray">Read at every grid array, not only where the peak could rise.</param>
/// <param name="Capacity">How many arrays to make room for before the baseline.</param>
internal sealed record MeasuredCase(
    Func<object> Build,
    Func<Dictionary<string, double>>? Facts = null,
    bool CollectEveryArray = false,
    int Capacity = LiveSet.InitialCapacity);

/// <summary>What a test assembly tells the shared child about itself.</summary>
/// <param name="Command">The first argument that makes the assembly's entry point measure.</param>
/// <param name="Cases">The build a kind and a size name; throws for a kind it does not know.</param>
/// <param name="Measured">A type from each assembly whose code the builds run, for the tier check.</param>
internal sealed record ChildHost(string Command, Func<string, double, MeasuredCase> Cases, IReadOnlyList<Type> Measured);

/// <summary>How long a child may take, case by case.</summary>
/// <param name="Startup">
/// How long it may go without starting a case: from its start to its first, between one case
/// and the next, and from its last to its exit.
/// </param>
/// <param name="Case">How long a case may take, from the line that says it began.</param>
internal sealed record ChildBudget(TimeSpan Startup, Func<string, TimeSpan> Case)
{
    /// <summary>Three minutes to start, ten for any one case.</summary>
    internal static ChildBudget Default { get; } = new(TimeSpan.FromMinutes(3), _ => TimeSpan.FromMinutes(10));
}

/// <summary>
/// The child process a memory test measures builds in: this test assembly, run as a program.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a child process.</b> <see cref="LiveSet"/> reads the live set of the whole process,
/// and a test host is not only the test: its own threads allocated 10 to 550 kB during one build,
/// and whatever of that is live at a reading is read as the build's. A process started for the
/// measurement has nothing else in it, and what the host is doing meanwhile - the rest of the
/// suite, on every core - does not reach it. <see cref="LiveSet"/> also counts what other
/// threads allocate - a few hundred bytes a collection, in a child - and reads each end of its
/// interval from the build alone as well, so that what they do cannot move it.
/// </para>
/// <para>
/// <b>One harness, linked as source</b> into each test assembly that measures
/// (<c>Einzel.Fields.Tests</c>, <c>Einzel.Cli.Tests</c>): the assembly is an executable whose entry
/// point hands its arguments to <see cref="Enter"/> with a <see cref="ChildHost"/> naming its
/// builds, and its tests ask <see cref="MeasuredCases"/> for a build by kind and size.
/// </para>
/// <para>
/// <b>What the child says.</b> On its output: its runtime, its collector, then for each case a
/// <c>BEGIN</c> line, and a <c>CASE</c> line once the case is built, measured and the tier
/// checked again; <c>END</c> after the last. On its error stream it marks where each case begins
/// and ends, so that anything else written there belongs to the case it was written in.
/// </para>
/// <para>
/// <b>A failure belongs to whatever caused it.</b> A case that throws, writes to the error
/// stream, runs past its own time, or takes the process down fails by name, alone, with what
/// was written; the parent stops that child and starts another for the cases after it. What
/// fails every case is only what is about every case: a child that does not start, runs another
/// runtime or collector than the tests', or is not in the JIT tier asked for - checked before
/// the first case and again after each, since a warm-up is exactly what would promote
/// first-tier code if promotion were on.
/// </para>
/// <para>
/// <b>Every case is built once before it is measured</b>, itself, at its own size, so that what
/// a first call allocates for itself and keeps - statics, tables, whatever a path taken only at
/// that size makes - is in the baseline and not in the reading.
/// </para>
/// </remarks>
internal static class MeasuringChild
{
    /// <summary>The prefix of the line naming the runtime the child ran on.</summary>
    internal const string RuntimeLine = "RUNTIME ";

    /// <summary>The prefix of the line naming the collector the child ran with.</summary>
    internal const string GcLine = "GC ";

    /// <summary>The prefix of the line saying a case is about to be built.</summary>
    internal const string BeginLine = "BEGIN ";

    /// <summary>The prefix of a line reporting one case.</summary>
    internal const string CaseLine = "CASE ";

    /// <summary>The prefix of the line saying the child is not in the tier it was asked for.</summary>
    internal const string RefusedLine = "REFUSED ";

    /// <summary>The line the child writes once it has finished every case.</summary>
    internal const string EndLine = "END";

    /// <summary>The prefix of the error-stream line marking where a case's own output begins.</summary>
    internal const string ErrorBegin = "##case-begins ";

    /// <summary>The prefix of the error-stream line marking where a case's own output ends.</summary>
    internal const string ErrorEnd = "##case-ends ";

    /// <summary>
    /// The collector the child must run with: workstation, and not concurrent, so that the only
    /// full collections are the ones a measurement forces and no background collection runs
    /// between them.
    /// </summary>
    internal const string ExpectedGc = "workstation Batch";

    /// <summary>A case as the child is told it and prints it.</summary>
    /// <param name="kind">The build's kind.</param>
    /// <param name="size">Its size.</param>
    /// <param name="times">How many times to measure it in the one process.</param>
    internal static string Key(string kind, double size, int times = 1) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}={size:R}") + (times > 1 ? $"*{times}" : "");

    /// <summary>Measures cases in child processes, in the order given.</summary>
    /// <param name="host">The test assembly's builds.</param>
    /// <param name="tier">The tier of JIT code the children run as.</param>
    /// <param name="cases">The cases, each as <see cref="Key"/> writes it.</param>
    /// <param name="budget">How long a child may take; <see cref="ChildBudget.Default"/> if null.</param>
    /// <returns>What was reported; this does not throw, so that a failure is reported where it belongs.</returns>
    internal static ChildRun Measure(ChildHost host, JitTier tier, IReadOnlyList<string> cases, ChildBudget? budget = null)
    {
        // A turn on the machine: one measuring child at a time across every test process, and
        // none while a test that times itself is timing (QuietMachine).
        using var turn = QuietMachine.Hold();

        var results = new Dictionary<string, CaseResult>();
        var text = new List<string>();
        IReadOnlyList<string> remaining = cases;
        string? failure = null;

        // One child while nothing goes wrong. A case that fails by taking its child with it -
        // out of time, a crash - is settled by name, and the cases after it get a fresh child.
        while (remaining.Count > 0 && failure is null)
        {
            var exited = Run([host.Command, tier.Name, .. remaining], tier, budget);
            var session = Interpret(remaining, exited, Environment.Version.ToString());

            text.Add(session.Text);

            foreach (var (key, result) in session.Cases)
            {
                results[key] = result;
            }

            failure = session.Failure
                ?? (session.Unfinished.Count == remaining.Count
                    ? "the measuring child settled no case and gave no reason"
                    : null);

            remaining = session.Unfinished;
        }

        return new ChildRun(failure, results, string.Join(Environment.NewLine, text));
    }

    /// <summary>What one child's run comes to: the cases it settled, and whether any can be trusted.</summary>
    /// <param name="arguments">The cases it was asked for, in order.</param>
    /// <param name="exited">How it ended and what it wrote.</param>
    /// <param name="expectedRuntime">The runtime version it must have run on.</param>
    internal static ChildSession Interpret(IReadOnlyList<string> arguments, ChildExit exited, string expectedRuntime)
    {
        var results = new Dictionary<string, CaseResult>();
        var runtime = (string?)null;
        var gc = (string?)null;
        var refused = (string?)null;
        var begun = (string?)null;
        var ended = false;

        // The error stream, by the case it was written in.
        var (within, outside) = Attribute(exited.ErrorLines);

        string Said(string key) => within.TryGetValue(key, out var said) && said.Count > 0
            ? $" It wrote to its error stream:{Environment.NewLine}{string.Join(Environment.NewLine, said)}"
            : "";

        foreach (var line in exited.Lines)
        {
            if (line.StartsWith(RuntimeLine, StringComparison.Ordinal))
            {
                runtime = line[RuntimeLine.Length..];
            }
            else if (line.StartsWith(GcLine, StringComparison.Ordinal))
            {
                gc = line[GcLine.Length..];
            }
            else if (line.StartsWith(RefusedLine, StringComparison.Ordinal))
            {
                refused = line[RefusedLine.Length..];
            }
            else if (line.StartsWith(BeginLine, StringComparison.Ordinal))
            {
                begun = line[BeginLine.Length..];
            }
            else if (line.StartsWith(CaseLine, StringComparison.Ordinal))
            {
                // A line the child did not finish writing - it was stopped mid-line - or one that
                // reads as nothing is that case failing, not this method: it must not throw, or
                // the fixture would cache the exception for every test and lose what was said.
                var read = Read(line[CaseLine.Length..]);
                var key = read?.Key ?? begun;

                if (key is null)
                {
                    continue;
                }

                results[key] = read is null
                    ? new CaseResult(key, null, $"the measuring child wrote a case line that does not read: {Shorten(line)}")
                    : Said(key) is { Length: > 0 } said
                        ? new CaseResult(key, null, $"{key} wrote to the measuring child's error stream, so its measurement is not used.{said}")
                        : read;

                begun = null;
            }
            else if (line == EndLine)
            {
                ended = true;
            }
        }

        // What is about every case, first.
        var failure =
            refused is not null ? $"the measuring child is not in the tier it was asked for: {refused}"
            : exited.Problem is { } problem && begun is null ? problem
            : outside.Count > 0
                ? $"the measuring child wrote to its error stream outside any case:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, outside)
            : runtime is null
                ? exited.ExitCode != 0
                    ? $"the measuring child exited {exited.ExitCode} before saying which runtime it ran on"
                    : "the measuring child did not say which runtime it ran on"
            : runtime != expectedRuntime
                ? $"the measuring child ran on .NET {runtime} and this test process on {expectedRuntime}: the two would "
                    + "not be running the same code, so what one measured is not about the other"
            : gc != ExpectedGc
                ? $"the measuring child ran with the collector {gc ?? "unreported"}, not {ExpectedGc}: a reading means "
                    + "something else under another collector"
            : exited.ExitCode != 0 && begun is null ? $"the measuring child exited {exited.ExitCode} between cases"
            : null;

        // Then the case the child was in when it stopped, which fails by name and alone.
        if (failure is null && begun is not null)
        {
            var why = exited.Problem
                ?? (exited.ExitCode != 0
                    ? $"the measuring child exited {exited.ExitCode} while building {begun}."
                    : $"the measuring child stopped while building {begun}.");

            results[begun] = new CaseResult(begun, null, why + Said(begun));
        }

        var unfinished = arguments.Where(argument => !results.ContainsKey(argument)).ToArray();

        failure ??=
            ended && unfinished.Length > 0 ? $"the measuring child finished without reporting {unfinished[0]}"
            : !ended && begun is null && unfinished.Length > 0 && exited.Problem is null
                ? $"the measuring child stopped without saying it had finished, before {unfinished[0]}"
            : !ended && begun is null && unfinished.Length == 0 && exited.Problem is null
                ? "the measuring child reported every case and stopped without saying it had finished"
            : null;

        var text = $"exit {exited.ExitCode}{Environment.NewLine}{string.Join(Environment.NewLine, exited.Lines)}"
            + $"{Environment.NewLine}{exited.Errors}";

        return new ChildSession(failure, results, unfinished, text);
    }

    /// <summary>The error stream's lines by the case they were written in, and those written in none.</summary>
    private static (Dictionary<string, List<string>> Within, List<string> Outside) Attribute(IReadOnlyList<string> lines)
    {
        var within = new Dictionary<string, List<string>>();
        var outside = new List<string>();
        string? current = null;

        foreach (var line in lines)
        {
            if (line.StartsWith(ErrorBegin, StringComparison.Ordinal))
            {
                current = line[ErrorBegin.Length..];
                within.TryAdd(current, []);
            }
            else if (line.StartsWith(ErrorEnd, StringComparison.Ordinal))
            {
                current = null;
            }
            else if (current is not null)
            {
                within[current].Add(line);
            }
            else
            {
                outside.Add(line);
            }
        }

        return (within, outside);
    }

    /// <summary>A case line's result, or null if it does not read as one.</summary>
    private static CaseResult? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CaseResult>(json) is { Key: not null } result ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string line) => line.Length <= 200 ? line : line[..200] + "...";

    /// <summary>The test assembly's entry point: measures cases if asked to, or says how to ask.</summary>
    /// <param name="host">The test assembly's builds.</param>
    /// <param name="args">The process's arguments.</param>
    /// <returns>The process exit code.</returns>
    internal static int Enter(ChildHost host, string[] args)
    {
        if (args is [var command, var tier, .. var cases]
            && command == host.Command
            && JitTier.Named(tier) is { } wanted
            && cases.Length > 0)
        {
            return Child(host, wanted, cases);
        }

        Console.Error.WriteLine(
            $"usage: {host.Command} {JitTier.Optimized.Name}|{JitTier.First.Name} <kind>=<size> [<kind>=<size> ...]"
            + Environment.NewLine
            + $"got: {(args.Length == 0 ? "no arguments" : string.Join(' ', args))}");

        return 64;
    }

    /// <summary>The child's work: each case built once unmeasured, then measured.</summary>
    private static int Child(ChildHost host, JitTier tier, ReadOnlySpan<string> cases)
    {
        Console.WriteLine(RuntimeLine + Environment.Version);
        Console.WriteLine(GcLine + (System.Runtime.GCSettings.IsServerGC ? "server" : "workstation") + " "
            + System.Runtime.GCSettings.LatencyMode);

        if (JitTier.Refusal(tier, host.Measured) is { } refusal)
        {
            Console.WriteLine(RefusedLine + refusal);
            return 2;
        }

        foreach (var item in cases)
        {
            Console.WriteLine(BeginLine + item);
            Console.Error.WriteLine(ErrorBegin + item);

            var result = Case(host, item);

            Console.Error.WriteLine(ErrorEnd + item);

            // And still, after the build has run twice: a warm-up is what promotes code. Checked
            // before the case is reported, so that every case reported was measured in this tier.
            if (JitTier.Refusal(tier, host.Measured) is { } late)
            {
                Console.WriteLine(RefusedLine + $"after building {item}: {late}");
                return 2;
            }

            Console.WriteLine(CaseLine + JsonSerializer.Serialize(result));
        }

        Console.WriteLine(EndLine);

        return 0;
    }

    /// <summary>One case, built once unmeasured and then measured, or the reason it could not be.</summary>
    private static CaseResult Case(ChildHost host, string item)
    {
#pragma warning disable CA1031 // Any exception: a case that throws is that case's failure, by name.
        try
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            var star = item.IndexOf('*', StringComparison.Ordinal);
            var kind = item[..split];
            var size = double.Parse(item[(split + 1)..(star < 0 ? item.Length : star)], CultureInfo.InvariantCulture);
            var times = star < 0 ? 1 : int.Parse(item[(star + 1)..], CultureInfo.InvariantCulture);

            var measured = TestCase(kind, size) ?? host.Cases(kind, size);

            measured.Build();

            // A build measured more than once is read at every grid array, so that the most of
            // its own arrays alive at once is the most at any of them - the same every time
            // for the same build - rather than the most at the readings the live set's bound
            // happened to call for, which other threads move: a charged bore run read 33,824
            // bytes of arrays fewer once, a coarse level alive at a reading that was skipped.
            var every = measured.CollectEveryArray || times > 1;

            // And the measurement is run once on nothing first, discarded, so that what its own
            // first use allocates and keeps - 32 bytes here, 152 on a Linux runner, in the
            // reading and not in any baseline before it - is in the baseline of the reading that
            // counts and not added to the first build's.

            _ = LiveSet.Peak(WarmMeasurement, every, measured.Capacity);

            var peak = LiveSet.Peak(measured.Build, every, measured.Capacity);
            var facts = measured.Facts?.Invoke() ?? [];
            var again = new List<LivePeak>();

            // Again, in this process, with nothing reset between: what a run leaves behind
            // that the next one adds to shows as readings that do not agree.
            for (var more = 1; more < times; more++)
            {
                again.Add(LiveSet.Peak(measured.Build, every, measured.Capacity));
            }

            return new CaseResult(item, new MeasuredBuild(peak, facts, times > 1 ? again : null), null);
        }
        catch (Exception failed)
        {
            return new CaseResult(item, null, failed.ToString());
        }
#pragma warning restore CA1031
    }

    /// <summary>A build of nothing but two small grid arrays and a reading between them.</summary>
    private static object WarmMeasurement()
    {
        var first = Einzel.Core.Numerics.GridArrays.New<double>(16);

        LiveSet.Checkpoint();

        return (first, Einzel.Core.Numerics.GridArrays.New<double>(16));
    }

    /// <summary>The builds that exist to test this harness: a child that dies, hangs, is slow, or complains.</summary>
    private static MeasuredCase? TestCase(string kind, double size) => kind switch
    {
        "test-exit" => new(() =>
        {
            Environment.Exit(3);
            return new object();
        }),
        "test-hang" => new(() =>
        {
            Thread.Sleep(Timeout.Infinite);
            return new object();
        }),
        "test-slow" => new(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(size));
            return new object();
        }),
        "test-stderr" => new(() =>
        {
            Console.Error.WriteLine("a complaint from inside the build");
            return new object();
        }),
        _ => null,
    };

    /// <summary>Runs this test assembly's entry point as a child process and returns what it printed.</summary>
    /// <param name="arguments">The entry point's arguments.</param>
    /// <param name="tier">The tier of JIT code the child is to run as.</param>
    /// <param name="budget">How long it may take; <see cref="ChildBudget.Default"/> if null.</param>
    /// <returns>How it ended and everything it wrote, however it ended.</returns>
    /// <remarks>
    /// The time allowed is a case's own, counted by this process from the moment it reads the
    /// line saying the case began - not one limit for the whole child, which a slow case early
    /// on would spend for the cases after it, and which says nothing about which case was slow.
    /// </remarks>
    internal static ChildExit Run(IReadOnlyList<string> arguments, JitTier tier, ChildBudget? budget = null)
    {
        budget ??= ChildBudget.Default;

        if (Host() is not { } host)
        {
            return new ChildExit(
                -1, [], [], $"no dotnet host was found to run the measuring child: looked for {string.Join(", ", HostCandidates())}");
        }

        var start = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(MeasuringChild).Assembly.Location);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // A controlled environment, not the parent's: whatever the parent was started with -
        // a tracing host, a server or tuned collector, a tiering variable - would otherwise
        // either write to the child's error stream or quietly change what a reading means.
        // What a process needs to start, and then the tier and the collector pinned.
        Controlled(start.Environment, tier);

        using var child = new Process { StartInfo = start };

        var gate = new object();
        var lines = new List<string>();
        var errors = new List<string>();
        var clock = Stopwatch.StartNew();
        var deadline = budget.Startup;
        var allowed = budget.Startup;
        string? building = null;

        child.OutputDataReceived += (_, received) =>
        {
            if (received.Data is not { } line || line.Trim().Length == 0)
            {
                return;
            }

            lock (gate)
            {
                lines.Add(line.Trim());

                // A case's time starts when this process reads that it began, and the time
                // between cases when it reads that one was reported.
                if (line.StartsWith(BeginLine, StringComparison.Ordinal))
                {
                    building = line[BeginLine.Length..].Trim();
                    allowed = budget.Case(building);
                    deadline = clock.Elapsed + allowed;
                }
                else if (line.StartsWith(CaseLine, StringComparison.Ordinal))
                {
                    building = null;
                    allowed = budget.Startup;
                    deadline = clock.Elapsed + allowed;
                }
            }
        };

        child.ErrorDataReceived += (_, received) =>
        {
            if (received.Data is { } line && line.Trim().Length > 0)
            {
                lock (gate)
                {
                    errors.Add(line.TrimEnd());
                }
            }
        };

        try
        {
            child.Start();
        }
        catch (Exception failed) when (failed is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ChildExit(-1, [], [], $"the measuring child did not start: {host}: {failed.Message}");
        }

        // And on one core in fact, where the platform allows it: a processor count of one sizes
        // the child's thread pool but does not stop a parallel loop from taking a thread a core.
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                child.ProcessorAffinity = 1;
            }
        }
        catch (Exception refused) when (refused is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Exited already, or not ours to pin: it runs as it would have.
        }

        child.BeginOutputReadLine();
        child.BeginErrorReadLine();

        string? problem = null;

        while (!child.WaitForExit(50))
        {
            lock (gate)
            {
                if (clock.Elapsed <= deadline)
                {
                    continue;
                }

                problem = building is null
                    ? $"the measuring child started no case in {Seconds(allowed)} and was stopped."
                    : $"{building} did not finish in {Seconds(allowed)}, so the measuring child was stopped.";
            }

            child.Kill(entireProcessTree: true);
            break;
        }

        // Without a limit: this also waits for both streams to be read to their ends, so what a
        // stopped child had written before is all here, and says how far it got.
        child.WaitForExit();

        lock (gate)
        {
            return new ChildExit(problem is null ? child.ExitCode : -1, [.. lines], [.. errors], problem);
        }
    }

    private static string Seconds(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0.#} s");

    /// <summary>The variables a child keeps from the parent: what starting a process needs, nothing that tunes it.</summary>
    internal static readonly string[] Kept =
    [
        "PATH", "SystemRoot", "SYSTEMROOT", "windir", "WINDIR", "ComSpec", "TEMP", "TMP", "TMPDIR",
        "HOME", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
        "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT(x86)",
    ];

    /// <summary>Replaces an environment with the controlled one a measuring child runs in.</summary>
    internal static void Controlled(IDictionary<string, string?> environment, JitTier tier)
    {
        var kept = Kept
            .Where(environment.ContainsKey)
            .ToDictionary(name => name, name => environment[name], StringComparer.Ordinal);

        environment.Clear();

        foreach (var (name, value) in kept)
        {
            environment[name] = value;
        }

        foreach (var (name, value) in tier.Environment())
        {
            environment[name] = value;
        }

        // Workstation and not concurrent: see ExpectedGc. The child reports what it ran with,
        // and is refused if that is anything else.
        environment["DOTNET_gcServer"] = "0";
        environment["DOTNET_gcConcurrent"] = "0";

        // One processor, as far as the child can tell, and pinned to one when it starts (Run): a
        // measurement reads one thread, and a ramped run's coefficient sweep spread across every
        // core took 114 s of processor time in 27 s of a twenty-four-core workstation - on a
        // four-core runner, every core, for longer, under the other test assemblies and the
        // tests in them that time themselves.
        environment["DOTNET_PROCESSOR_COUNT"] = "1";
    }

    /// <summary>
    /// The <c>dotnet</c> host of the runtime this test process runs on, or null if none is found.
    /// </summary>
    /// <remarks>
    /// The running runtime's own installation first, since that is the runtime the child must
    /// match - and is found from the runtime itself, so a test host that is not <c>dotnet</c>
    /// (<c>testhost.exe</c>, an IDE's runner) finds it as well. Then the host the CLI says it
    /// is, then <c>DOTNET_ROOT</c>. Then, where none of those is there - a runtime laid out
    /// some other way - this process itself if it is <c>dotnet</c>, and last each <c>dotnet</c>
    /// on the path. Those last can be another installation, and what makes them safe to try is
    /// that the child says which runtime it ran on and is refused, by name, if it differs.
    /// </remarks>
    internal static string? Host() => HostCandidates().FirstOrDefault(File.Exists);

    internal static IEnumerable<string> HostCandidates()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        // <root>/shared/Microsoft.NETCore.App/<version>/
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());

        if (runtime.Parent?.Parent?.Parent is { } root)
        {
            yield return Path.Combine(root.FullName, name);
        }

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath)
        {
            yield return hostPath;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            yield return Path.Combine(dotnetRoot, name);
        }

        // After the precise ones: this process, if it is the host, and then the path.
        if (Environment.ProcessPath is { Length: > 0 } self
            && string.Equals(Path.GetFileName(self), name, StringComparison.OrdinalIgnoreCase))
        {
            yield return self;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, name);
        }
    }
}

/// <summary>
/// The builds a test assembly's memory tests measure, each measured once, when a test first
/// asks for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A build is measured when it is asked for, in a child of its own.</b> One test run alone
/// - a filter, a rerun of one failure - starts the children for the builds it reads and no
/// others; this used to measure every build of the class, each built twice, whichever test
/// asked. A child costs a few tenths of a second to start, which is what measuring them all
/// in one saved. A build is still measured once however many tests ask for it, and a failure
/// is that build's alone by construction.
/// </para>
/// <para>
/// A test assembly derives a fixture from this, naming its builds. The children only measure:
/// what a build should hold, and whether it does, stay in the tests.
/// </para>
/// </remarks>
public abstract class MeasuredCases
{
    private readonly ChildHost _host;
    private readonly Dictionary<(string Tier, string Key), ChildRun> _measured = [];

    /// <summary>Names the builds; nothing runs until a test asks.</summary>
    private protected MeasuredCases(ChildHost host) => _host = host;

    /// <summary>A build measured as fully optimized code.</summary>
    internal MeasuredBuild Optimized(ITestOutputHelper output, string kind, double size, int times = 1) =>
        Measured(JitTier.Optimized, "optimized", output, MeasuringChild.Key(kind, size, times));

    /// <summary>A build measured as first-tier code that is never promoted.</summary>
    internal MeasuredBuild FirstTier(ITestOutputHelper output, string kind, double size) =>
        Measured(JitTier.First, "first-tier", output, MeasuringChild.Key(kind, size));

    private MeasuredBuild Measured(JitTier tier, string name, ITestOutputHelper output, string key)
    {
        ChildRun run;

        // One at a time: two children measuring at once would each be the other's busy machine,
        // which moves no reading and does lengthen both.
        lock (_measured)
        {
            if (!_measured.TryGetValue((name, key), out run!))
            {
                // Never throws: a failure is a result, kept and reported to every test that asks.
                _measured[(name, key)] = run = MeasuringChild.Measure(_host, tier, [key]);
            }
        }

        return Find(run, name, output, key);
    }

    /// <summary>A case's measurement, or a failure naming why there is none to trust.</summary>
    /// <remarks>
    /// What is about every case first, then this case's own failure, which is this case's alone.
    /// </remarks>
    internal static MeasuredBuild Find(ChildRun run, string tier, ITestOutputHelper output, string key)
    {
        if (run.Failure is { } child)
        {
            Assert.Fail(
                $"the {tier} measuring child failed as a whole, so no case it measured can be used: {child}"
                + $"{Environment.NewLine}{run.Text}");
        }

        if (!run.Cases.TryGetValue(key, out var result))
        {
            Assert.Fail($"{key} was not reported by the {tier} measuring child{Environment.NewLine}{run.Text}");
        }

        if (result.Failure is { } failure)
        {
            Assert.Fail($"{key} failed in the {tier} measuring child, and only this case did: {failure}");
        }

        var build = result.Build!;

        output.WriteLine($"{key}: {JsonSerializer.Serialize(build)}");

        // The specific failure first: grid arrays allocated off the measuring thread are named
        // as such, before the count of other threads' bytes they also inflate.
        foreach (var peak in build.Every)
        {
            MemoryVerdict.Covered(key, peak);
            MemoryVerdict.TheBuildsOwn(key, peak);
        }

        return build;
    }
}
