using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>One build measured in a child process: what it held, and what its solve reported.</summary>
/// <param name="Peak">What the build held at its most.</param>
/// <param name="Levels">Coarse levels the solve descended, where the build is a single forced solve; else -1.</param>
/// <param name="Cycles">Cycles that solve ran; else -1.</param>
internal sealed record MeasuredBuild(LivePeak Peak, int Levels, int Cycles);

/// <summary>What the child printed for one case: a measurement, or why there is none.</summary>
/// <param name="Key">The case, as <see cref="MemoryCases.Key"/> writes it.</param>
/// <param name="Build">The measurement, or null if the case failed.</param>
/// <param name="Failure">Why the case failed, or null.</param>
internal sealed record CaseResult(string Key, MeasuredBuild? Build, string? Failure);

/// <summary>Everything one child process reported.</summary>
/// <param name="Failure">Why the child as a whole failed - it did not start, refused its tier, crashed - or null.</param>
/// <param name="Cases">Each case's result, by key; complete only when the child did not fail.</param>
/// <param name="Text">What the child wrote, both streams, for a failing test to show.</param>
internal sealed record ChildRun(string? Failure, IReadOnlyDictionary<string, CaseResult> Cases, string Text);

/// <summary>A rediscretized solve driven directly, with the hierarchy forced.</summary>
/// <param name="Geometry">The geometry at a box length.</param>
/// <param name="Cycles">Cycles to run, exactly.</param>
/// <param name="Source">Whether to solve a Poisson problem with a uniform source.</param>
internal sealed record DirectCase(Func<double, Geometry3D> Geometry, int Cycles, bool Source);

/// <summary>
/// The builds the memory tests measure, by name, and the child process that measures them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a child process.</b> <see cref="LiveSet"/> reads the live set of the whole process,
/// and a test host is not only the test: its own threads allocated 10 to 550 kB during one build
/// here, and whatever of that is live at a reading is read as the build's. That is the likeliest
/// account of the reading "100 to 300 kB high, once a process" that the sampling thread used to
/// return and a middle-of-three used to outvote. A process started for the measurement has
/// nothing else in it. <see cref="LiveSet"/> also counts what other threads allocate - a few
/// hundred bytes a collection, in a child - and reads each end of its interval from the build
/// alone as well, so that what they do cannot move it.
/// </para>
/// <para>
/// <b>Nothing a child says counts unless the whole child succeeded.</b> It writes a line naming
/// its runtime, a <c>BEGIN</c> and a <c>CASE</c> line for each case, and an <c>END</c> line
/// after the last. A run that exits non-zero, writes anything to its error stream, stops before
/// <c>END</c>, runs a different runtime or times out fails every test that asked it for
/// anything, with what it wrote - including a case it measured before it went wrong, since the
/// check that went wrong (its tier, read again after the last build) is about all of them.
/// </para>
/// <para>
/// <b>Every case is built once before it is measured</b>, itself, at its own size, so that what
/// a first call allocates for itself and keeps - statics, tables, whatever a path taken only at
/// that size makes - is in the baseline and not in the reading. The child checks its tier
/// before the first build and again after the last, since a warm-up is exactly what would
/// promote first-tier code if promotion were on.
/// </para>
/// <para>
/// <b>One child for a class</b> (<see cref="MeasuredBuilds"/>): each case is built and measured
/// once however many tests ask for it. The child only measures. What a build should hold, and
/// whether it does, stay in the tests.
/// </para>
/// </remarks>
internal static class MemoryCases
{
    /// <summary>The argument that makes the test assembly's entry point measure these cases.</summary>
    internal const string Command = "solve-memory";

    /// <summary>The directly driven rediscretized solves, by kind.</summary>
    internal static readonly IReadOnlyDictionary<string, DirectCase> Direct = new Dictionary<string, DirectCase>
    {
        // A sphere smaller than two cells: no coarse level, and the bottom solve on the fine grid.
        ["ball-flat"] = new(length => SolveMemoryTests.Ball(length, radius: 0.0016, cell: 0.001), 1, false),

        // A sphere three cells across: resolved by one coarsening and not by two.
        ["ball-one-level"] = new(length => SolveMemoryTests.Ball(length, radius: 0.0015, cell: 0.0005), 2, false),

        // No conductor, thin on two axes: two levels that shrink by much less than an eighth.
        ["thin"] = new(length => new Geometry3D(0.0, -0.004, -0.004, length, 0.004, 0.004, 0.001, []), 2, true),
    };

    /// <summary>A case as the child is told it and prints it.</summary>
    internal static string Key(string kind, double size) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}={size:R}");

    /// <summary>Measures builds in one child process, in the order given.</summary>
    /// <param name="tier">The tier of JIT code the child runs as.</param>
    /// <param name="cases">Each build's kind and its size: a cell size, or a box length for a direct solve.</param>
    /// <returns>What the child reported; this does not throw, so that one failure is reported where it belongs.</returns>
    internal static ChildRun Measure(JitTier tier, IReadOnlyList<(string Kind, double Size)> cases)
    {
        string[] arguments = [.. cases.Select(c => Key(c.Kind, c.Size))];

        var exited = MeasuringChild.Run([Command, tier.Name, .. arguments], tier);

        return Interpret(arguments, exited, Environment.Version.ToString());
    }

    /// <summary>What a child's run comes to: its cases, and whether any of them can be trusted.</summary>
    /// <param name="arguments">The cases it was asked for, in order.</param>
    /// <param name="exited">How it ended and what it wrote.</param>
    /// <param name="expectedRuntime">The runtime version it must have run on.</param>
    internal static ChildRun Interpret(IReadOnlyList<string> arguments, ChildExit exited, string expectedRuntime)
    {
        var results = new Dictionary<string, CaseResult>();
        var runtime = (string?)null;
        var begun = (string?)null;
        var malformed = (string?)null;
        var gc = (string?)null;
        var ended = false;

        foreach (var line in exited.Lines)
        {
            if (line.StartsWith(RuntimeLine, StringComparison.Ordinal))
            {
                runtime = line[RuntimeLine.Length..];
            }
            else if (line.StartsWith(BeginLine, StringComparison.Ordinal))
            {
                begun = line[BeginLine.Length..];
            }
            else if (line.StartsWith(CaseLine, StringComparison.Ordinal))
            {
                // A line the child did not finish writing - it was stopped mid-line - or one that
                // reads as nothing is the child failing, not this method: it must not throw, or the
                // fixture would cache the exception for every test and lose what the child said.
                if (Read(line[CaseLine.Length..]) is { } result)
                {
                    results[result.Key] = result;
                    begun = null;
                }
                else
                {
                    malformed ??= line;
                }
            }
            else if (line.StartsWith(GcLine, StringComparison.Ordinal))
            {
                gc = line[GcLine.Length..];
            }
            else if (line == EndLine)
            {
                ended = true;
            }
        }

        var inProgress = begun is null ? "" : $" It was building {begun} at the time.";

        var failure =
            exited.Problem is { } problem ? problem + inProgress
            : exited.ExitCode != 0 ? $"the measuring child exited {exited.ExitCode}.{inProgress}"
            : exited.Errors.Trim().Length > 0 ? "the measuring child wrote to its error stream"
            : malformed is not null ? $"the measuring child wrote a case line that does not read: {Shorten(malformed)}"
            : runtime is null ? "the measuring child did not say which runtime it ran on"
            : runtime != expectedRuntime
                ? $"the measuring child ran on .NET {runtime} and this test process on {expectedRuntime}: the two would "
                    + "not be running the same code, so what one measured is not about the other"
            : gc != ExpectedGc
                ? $"the measuring child ran with the collector {gc ?? "unreported"}, not {ExpectedGc}: a reading means "
                    + "something else under another collector"
            : !ended ? $"the measuring child stopped without saying it had finished.{inProgress}"
            : arguments.FirstOrDefault(a => !results.ContainsKey(a)) is { } missing
                ? $"the measuring child finished without reporting {missing}"
            : null;

        var text = $"exit {exited.ExitCode}{Environment.NewLine}{string.Join(Environment.NewLine, exited.Lines)}"
            + $"{Environment.NewLine}{exited.Errors}";

        return new ChildRun(failure, results, text);
    }

    /// <summary>The prefix of the line naming the runtime the child ran on.</summary>
    internal const string RuntimeLine = "RUNTIME ";

    /// <summary>The prefix of a line reporting one case.</summary>
    internal const string CaseLine = "CASE ";

    /// <summary>The prefix of the line naming the collector the child ran with.</summary>
    internal const string GcLine = "GC ";

    /// <summary>
    /// The collector the child must run with: workstation, and not concurrent, so that the only
    /// full collections are the ones a measurement forces and no background collection runs
    /// between them.
    /// </summary>
    internal const string ExpectedGc = "workstation Batch";

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

    /// <summary>The prefix of the line saying a case is about to be built.</summary>
    internal const string BeginLine = "BEGIN ";

    /// <summary>The line the child writes once it has finished, and checked its tier again.</summary>
    internal const string EndLine = "END";

    /// <summary>
    /// Fails a measurement during which another thread allocated, between two readings, more than
    /// anything but a grid array would come to.
    /// </summary>
    /// <remarks>
    /// The backstop behind <see cref="GridArrays"/>, which records by name any grid array allocated
    /// off the measuring thread: an array a build allocated on a worker <i>without</i> going through
    /// it would be neither read at nor bounded, only counted among what other threads allocated.
    /// Asked of the most between two readings, not of the total: what the runtime's own threads
    /// do is a few hundred bytes a collection, and a total grows with how many collections a build
    /// needs - twenty thousand, for one test here - where an array allocated on a worker arrives
    /// all at once. 64 kB is two orders over that housekeeping and under the smallest grid array
    /// the memory tests build.
    /// </remarks>
    internal static void AssertTheBuildsOwn(string name, LivePeak peak) =>
        Assert.True(
            peak.ForeignBurst <= 64 * 1024,
            $"{name}: {MemoryVerdict.Disturbed} - another thread allocated {peak.ForeignBurst:N0} bytes between two readings "
            + $"({peak.Foreign:N0} in all). Either the measuring process was not the build's alone, or the solver now "
            + $"allocates on worker threads outside {nameof(GridArrays)}, where the live set is neither read nor bounded.");

    /// <summary>The child's work: each case built once unmeasured, then measured.</summary>
    /// <param name="tier">The tier this process is to be running as.</param>
    /// <param name="cases">The cases, each <c>kind=size</c>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Child(JitTier tier, ReadOnlySpan<string> cases)
    {
        Console.WriteLine(RuntimeLine + Environment.Version);
        Console.WriteLine(GcLine + (System.Runtime.GCSettings.IsServerGC ? "server" : "workstation") + " "
            + System.Runtime.GCSettings.LatencyMode);

        if (JitTier.Refusal(tier) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        foreach (var item in cases)
        {
            Console.WriteLine(BeginLine + item);
            Console.WriteLine(CaseLine + JsonSerializer.Serialize(Case(item)));
        }

        // And still, after every build has run twice: a warm-up is what promotes code. Every case
        // above was measured in this tier, so a refusal here is about all of them.
        if (JitTier.Refusal(tier) is { } late)
        {
            Console.Error.WriteLine("after the builds: " + late);
            return 2;
        }

        Console.WriteLine(EndLine);

        return 0;
    }

    /// <summary>One case, built once unmeasured and then measured, or the reason it could not be.</summary>
    private static CaseResult Case(string item)
    {
        try
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            var kind = item[..split];
            var size = double.Parse(item[(split + 1)..], CultureInfo.InvariantCulture);

            SolveReport? solved = null;
            var build = Build(kind, size, report => solved = report);

            build();

            // Forty small fields, read at every one, with room made for `size` of them before the
            // baseline: GridArraysTests compares a build that outgrows its room with one that does not.
            var peak = kind == "fields"
                ? LiveSet.Peak(build, collectEveryArray: true, capacity: (int)size)
                : LiveSet.Peak(build);

            return new CaseResult(
                item,
                new MeasuredBuild(
                    peak,
                    Direct.ContainsKey(kind) ? solved!.Levels : -1,
                    Direct.ContainsKey(kind) ? solved!.Cycles : -1),
                null);
        }
        catch (Exception failed)
        {
            return new CaseResult(item, null, failed.ToString());
        }
    }

    private static Func<object> Build(string kind, double size, Action<SolveReport> report)
    {
        if (Direct.TryGetValue(kind, out var direct))
        {
            var geometry = direct.Geometry(size);

            return () => Solve(geometry, direct, report);
        }

        return kind switch
        {
            "slabs" => () => GeometryBuilder3D.BuildField(SolveMemoryTests.Plates(size)).Field,
            "driven-slabs" => () => GeometryBuilder3D.BuildField(SolveMemoryTests.TwoChannelPlates(size)).Field,
            "sphere" => () => GeometryBuilder3D.BuildField(SolveMemoryTests.Sphere(size)).Field,
            "rods" => () => GeometryBuilder.Build(SolveMemoryTests.Rods(size)).Field,
            "driven-rods" => () => GeometryBuilder.Build(SolveMemoryTests.Rods(size, driven: true)).Field,
            // For GridArraysTests: more grid arrays than the room made for them before the baseline,
            // with a reading after the room has grown, of a build whose peak is known exactly.
            "grow-drop" => () =>
            {
                var kept = GridArrays.New<double>(1_000_000);

                // Held at its reading, then let go: the live set falls 8 MB below the peak read so
                // far, so the twenty thousand small arrays that follow are passed over rather than
                // read. In a frame of its own, so that it is let go when that frame returns in
                // every build configuration - Debug code keeps a local to the end of its method.
                HoldThenLetGo();

                for (var small = 0; small < 20_000; small++)
                {
                    _ = GridArrays.New<byte>(1);
                }

                return new object[] { kept, GridArrays.New<double>(1_100_000) };
            },
            "fields" => () => Enumerable.Range(0, 40)
                .Select(_ => new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9)))
                .ToArray(),

            // For ChildProtocolTests: a child that dies part way, and one that never finishes.
            "test-exit" => () =>
            {
                Environment.Exit(3);
                return new object();
            },
            "test-hang" => () =>
            {
                Thread.Sleep(Timeout.Infinite);
                return new object();
            },
            _ => throw new ArgumentException($"no build is called '{kind}'", nameof(kind)),
        };
    }

    /// <summary>Allocates an array, holds it through its own reading, and lets it go on return.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void HoldThenLetGo() => GC.KeepAlive(GridArrays.New<double>(1_000_000));

    private static ScalarField3D Solve(Geometry3D geometry, DirectCase direct, Action<SolveReport> report)
    {
        var grid = GeometryBuilder3D.BuildGrid(geometry);
        var mask = GeometryBuilder3D.BuildMask(geometry, grid);
        ScalarField3D? rhs = null;

        if (direct.Source)
        {
            rhs = new ScalarField3D(grid);

            for (var k = 0; k < grid.CountZ; k++)
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = 0; i < grid.CountX; i++)
                    {
                        rhs[i, j, k] = 1.0;
                    }
                }
            }
        }

        var (potential, solved) = PoissonSolver3D.Solve(
            mask, 1e-14, direct.Cycles, GeometryBuilder3D.Coarsener(geometry), source: rhs, galerkin: false);

        report(solved);

        return potential;
    }
}

/// <summary>
/// Every build the memory tests measure, measured once: one child process a tier, started when
/// a test first asks for a build of that tier.
/// </summary>
/// <remarks>
/// A test used to start a child of its own, and three of them built the same four rods. Shared,
/// each case is built and measured once, and a child that fails fails every test that asked it
/// for anything, with what it wrote.
/// </remarks>
public sealed class MeasuredBuilds
{
    private static readonly (string Kind, double Size)[] OptimizedCases =
    [
        ("slabs", 0.0005), ("slabs", 0.00025), ("driven-slabs", 0.00025),
        ("sphere", 0.000625), ("sphere", 0.0003125),
        ("ball-flat", 0.016), ("ball-flat", 0.032),
        ("ball-one-level", 0.016), ("ball-one-level", 0.032),
        ("thin", 4.096), ("thin", 8.192),
        ("rods", 0.0001), ("rods", 0.00005), ("driven-rods", 0.0001),
        ("fields", LiveSet.InitialCapacity), ("fields", 1), ("grow-drop", 1),
    ];

    // Small, because first-tier code is slow.
    private static readonly (string Kind, double Size)[] FirstTierCases = [("rods", 0.0001), ("sphere", 0.000625)];

    // Lazy over a value that cannot throw: a failure is a result, reported to the tests it
    // concerns, not an exception cached and rethrown to every test that asks for anything.
    private readonly Lazy<ChildRun> _optimized = new(() => MemoryCases.Measure(JitTier.Optimized, OptimizedCases));

    private readonly Lazy<ChildRun> _first = new(() => MemoryCases.Measure(JitTier.First, FirstTierCases));

    /// <summary>A build measured as fully optimized code.</summary>
    internal MeasuredBuild Optimized(ITestOutputHelper output, string kind, double size) =>
        Find(_optimized.Value, "optimized", output, kind, size);

    /// <summary>A build measured as first-tier code that is never promoted.</summary>
    internal MeasuredBuild FirstTier(ITestOutputHelper output, string kind, double size) =>
        Find(_first.Value, "first-tier", output, kind, size);

    private static MeasuredBuild Find(ChildRun run, string tier, ITestOutputHelper output, string kind, double size) =>
        Find(run, tier, output, MemoryCases.Key(kind, size));

    /// <summary>A case's measurement, or a failure naming why there is none to trust.</summary>
    /// <remarks>
    /// The child as a whole first: a case it measured counts only if every check the child makes
    /// about itself passed, some of which it makes after its last case.
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
            Assert.Fail($"{key} is not among the builds the {tier} child measures: add it to the list");
        }

        if (result.Failure is { } failure)
        {
            Assert.Fail($"{key} failed in the {tier} measuring child, and only this case did: {failure}");
        }

        var build = result.Build!;

        output.WriteLine($"{key}: {JsonSerializer.Serialize(build)}");

        // The specific failure first: grid arrays allocated off the measuring thread are named
        // as such, before the count of other threads' bytes they also inflate.
        MemoryVerdict.Covered(key, build.Peak);
        MemoryCases.AssertTheBuildsOwn(key, build.Peak);

        return build;
    }
}

/// <summary>How a child process ended.</summary>
/// <param name="ExitCode">Its exit code; -1 if it was killed or never started.</param>
/// <param name="Lines">The non-empty lines of its standard output, as far as it got.</param>
/// <param name="Errors">Its error stream.</param>
/// <param name="Problem">What went wrong in running it - it did not start, it ran out of time - or null.</param>
internal sealed record ChildExit(int ExitCode, IReadOnlyList<string> Lines, string Errors, string? Problem);

/// <summary>Runs this test assembly's entry point as a child process and returns what it printed.</summary>
internal static class MeasuringChild
{
    /// <summary>Runs the child.</summary>
    /// <param name="arguments">The entry point's arguments.</param>
    /// <param name="tier">The tier of JIT code the child is to run as.</param>
    /// <param name="timeout">How long it may take; twenty minutes unless a test says otherwise.</param>
    /// <returns>How it ended and everything it wrote, however it ended.</returns>
    internal static ChildExit Run(IReadOnlyList<string> arguments, JitTier tier, TimeSpan? timeout = null)
    {
        if (Host() is not { } host)
        {
            return new ChildExit(
                -1, [], "", $"no dotnet host was found to run the measuring child: looked for {string.Join(", ", HostCandidates())}");
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
        // either write to the child's error stream and fail every case, or quietly change what a
        // reading means. What a process needs to start, and then the tier and the collector
        // pinned.
        Controlled(start.Environment, tier);

        using var child = Process.Start(start);

        if (child is null)
        {
            return new ChildExit(-1, [], "", $"the measuring child did not start: {host}");
        }

        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        var limit = timeout ?? TimeSpan.FromMinutes(20);
        var finished = child.WaitForExit(limit);

        if (!finished)
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
        }

        // Read whatever arrived in either case: a killed child's streams close, and what it had
        // written before is what says how far it got.
        var output = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return finished
            ? new ChildExit(child.ExitCode, lines, errors, null)
            : new ChildExit(-1, lines, errors, $"the measuring child did not finish in {limit.TotalMinutes:0.#} minutes and was stopped.");
    }

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
            if (value is not null)
            {
                environment[name] = value;
            }
        }

        // Workstation and not concurrent: see MemoryCases.ExpectedGc. The child reports what it ran
        // with, and is refused if that is anything else.
        environment["DOTNET_gcServer"] = "0";
        environment["DOTNET_gcConcurrent"] = "0";
    }

    /// <summary>
    /// The <c>dotnet</c> host of the runtime this test process runs on, or null if none is found.
    /// </summary>
    /// <remarks>
    /// The running runtime's own installation first, since that is the runtime the child must
    /// match - and is found from the runtime itself, so a test host that is not <c>dotnet</c>
    /// (<c>testhost.exe</c>, an IDE's runner) finds it as well. Then the host the CLI says it
    /// is, then <c>DOTNET_ROOT</c>. Never a bare <c>dotnet</c> off the path, which can be another
    /// installation; and the child says which runtime it ran on, and is refused if it differs.
    /// </remarks>
    internal static string? Host() => HostCandidates().FirstOrDefault(File.Exists);

    private static IEnumerable<string> HostCandidates()
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
    }
}
