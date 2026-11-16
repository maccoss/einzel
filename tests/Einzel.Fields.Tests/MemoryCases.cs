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
/// nothing else in it, and <see cref="LiveSet"/> checks that: it counts what other threads
/// allocated between readings - a few hundred bytes a collection, in a child - and takes the
/// measurement again if much more than that arrives at once.
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

        var (failure, stdout, text) = MeasuringChild.Run([Command, tier.Name, .. arguments], tier);
        var results = new Dictionary<string, CaseResult>();
        var runtime = (string?)null;

        foreach (var line in stdout)
        {
            if (line.StartsWith(RuntimeLine, StringComparison.Ordinal))
            {
                runtime = line[RuntimeLine.Length..];
            }
            else if (line.StartsWith(CaseLine, StringComparison.Ordinal))
            {
                var result = JsonSerializer.Deserialize<CaseResult>(line[CaseLine.Length..])!;
                results[result.Key] = result;
            }
        }

        var expected = Environment.Version.ToString();

        failure ??= runtime is null
            ? "the measuring child did not say which runtime it ran on"
            : runtime != expected
                ? $"the measuring child ran on .NET {runtime} and this test process on {expected}: the two would not "
                    + "be running the same code, so what one measured is not about the other"
                : arguments.FirstOrDefault(a => !results.ContainsKey(a)) is { } missing
                    ? $"the measuring child stopped before it reported {missing}"
                    : null;

        return new ChildRun(failure, results, text);
    }

    /// <summary>The prefix of the line naming the runtime the child ran on.</summary>
    internal const string RuntimeLine = "RUNTIME ";

    /// <summary>The prefix of a line reporting one case.</summary>
    internal const string CaseLine = "CASE ";

    /// <summary>
    /// Fails a measurement during which other threads allocated a large part of what the build
    /// held.
    /// </summary>
    /// <remarks>
    /// The backstop behind <see cref="GridArrays"/>, which refuses by name to allocate off the
    /// measuring thread: an array a build allocated on a worker <i>without</i> going through it
    /// would be neither read at nor bounded, only counted among what other threads allocated. A
    /// quarter of the build's own peak is far above a runtime's housekeeping - a few hundred
    /// bytes a reading in a child - and is what a solver that moved its arrays onto workers
    /// wholesale would show. It does not see one small scratch array allocated that way; the
    /// source test is what stops that being written.
    /// </remarks>
    internal static void AssertTheBuildsOwn(string name, LivePeak peak) =>
        Assert.True(
            peak.Foreign <= 0.25 * peak.Held,
            $"{name}: {MemoryVerdict.Disturbed} - threads other than the build's allocated {peak.Foreign:N0} bytes during a build "
            + $"that held {peak.Held:N0}. Either the measuring process was not the build's alone, or the solver now "
            + $"allocates on worker threads outside {nameof(GridArrays)}, where the live set is neither read nor bounded.");

    /// <summary>The child's work: each case built once unmeasured, then measured.</summary>
    /// <param name="tier">The tier this process is to be running as.</param>
    /// <param name="cases">The cases, each <c>kind=size</c>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Child(JitTier tier, ReadOnlySpan<string> cases)
    {
        Console.WriteLine(RuntimeLine + Environment.Version);

        if (JitTier.Refusal(tier) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        foreach (var item in cases)
        {
            Console.WriteLine(CaseLine + JsonSerializer.Serialize(Case(item)));
        }

        // And still, after every build has run twice: a warm-up is what promotes code.
        if (JitTier.Refusal(tier) is { } late)
        {
            Console.Error.WriteLine("after the builds: " + late);
            return 2;
        }

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

            var peak = LiveSet.Peak(build);

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
            _ => throw new ArgumentException($"no build is called '{kind}'", nameof(kind)),
        };
    }

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

    private static MeasuredBuild Find(ChildRun run, string tier, ITestOutputHelper output, string kind, double size)
    {
        var key = MemoryCases.Key(kind, size);

        if (run.Cases.TryGetValue(key, out var result) && result.Build is { } build)
        {
            output.WriteLine($"{key}: {JsonSerializer.Serialize(build)}");

            // The specific failure first: grid arrays allocated off the measuring thread are named
            // as such, before the count of other threads' bytes they also inflate.
            MemoryVerdict.Covered(key, build.Peak);
            MemoryCases.AssertTheBuildsOwn(key, build.Peak);

            return build;
        }

        if (result?.Failure is { } failure)
        {
            Assert.Fail($"{key} failed in the {tier} measuring child, and only this case did: {failure}");
        }

        if (run.Failure is { } child)
        {
            Assert.Fail(
                $"the {tier} measuring child failed as a whole, so no case it was to measure has a result: {child}"
                + $"{Environment.NewLine}{run.Text}");
        }

        Assert.Fail($"{key} is not among the builds the {tier} child measures: add it to the list");

        return null!;
    }
}

/// <summary>Runs this test assembly's entry point as a child process and returns what it printed.</summary>
internal static class MeasuringChild
{
    /// <summary>Runs the child.</summary>
    /// <param name="arguments">The entry point's arguments.</param>
    /// <param name="tier">The tier of JIT code the child is to run as.</param>
    /// <returns>
    /// Why it failed as a whole, or null; the non-empty lines of its standard output; and both
    /// its streams, for a message.
    /// </returns>
    internal static (string? Failure, string[] Lines, string Text) Run(IReadOnlyList<string> arguments, JitTier tier)
    {
        if (Host() is not { } host)
        {
            return ($"no dotnet host was found to run the measuring child: looked for {string.Join(", ", HostCandidates())}", [], "");
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

        // Every tiering variable, under both prefixes the runtime reads, whatever the parent
        // was started with: a shell that exports one would otherwise choose the child's tier.
        foreach (var (name, value) in tier.Environment())
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var child = Process.Start(start);

        if (child is null)
        {
            return ($"the measuring child did not start: {host}", [], "");
        }

        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();

        if (!child.WaitForExit(TimeSpan.FromMinutes(20)))
        {
            child.Kill(entireProcessTree: true);
            return ("the measuring child did not finish in twenty minutes", [], "");
        }

        var output = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();
        var text = $"{host} exited {child.ExitCode}{Environment.NewLine}{output}{Environment.NewLine}{errors}";
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return (child.ExitCode == 0 ? null : $"the measuring child exited {child.ExitCode}: {errors.Trim()}", lines, text);
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
