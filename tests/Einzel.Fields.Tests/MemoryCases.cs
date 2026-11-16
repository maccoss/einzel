using System.Diagnostics;
using System.Globalization;

using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>One build measured in a child process: what it held, and what its solve reported.</summary>
/// <param name="Peak">What the build held at its most.</param>
/// <param name="Levels">Coarse levels the solve descended, where the build is a single forced solve; else -1.</param>
/// <param name="Cycles">Cycles that solve ran; else -1.</param>
internal sealed record MeasuredBuild(LivePeak Peak, int Levels, int Cycles);

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
    /// <returns>Each case's measurement and the line the child printed for it, by <see cref="Key"/>.</returns>
    internal static Dictionary<string, (MeasuredBuild Build, string Line)> Measure(
        JitTier tier, IReadOnlyList<(string Kind, double Size)> cases)
    {
        string[] arguments = [.. cases.Select(c => Key(c.Kind, c.Size))];

        var lines = MeasuringChild.Run([Command, tier.Name, .. arguments], tier);

        Assert.Equal(cases.Count, lines.Length);

        var measured = new Dictionary<string, (MeasuredBuild, string)>();

        for (var at = 0; at < lines.Length; at++)
        {
            var parts = lines[at].Split(' ');

            Assert.Equal(arguments[at], parts[0]);

            var peak = new LivePeak(
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture),
                int.Parse(parts[4], CultureInfo.InvariantCulture),
                long.Parse(parts[5], CultureInfo.InvariantCulture),
                long.Parse(parts[6], CultureInfo.InvariantCulture),
                long.Parse(parts[7], CultureInfo.InvariantCulture),
                int.Parse(parts[8], CultureInfo.InvariantCulture));

            AssertTheBuildsOwn(arguments[at], peak);

            measured.Add(
                arguments[at],
                (new MeasuredBuild(
                    peak,
                    int.Parse(parts[9], CultureInfo.InvariantCulture),
                    int.Parse(parts[10], CultureInfo.InvariantCulture)),
                lines[at]));
        }

        return measured;
    }

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
            $"{name}: threads other than the build's allocated {peak.Foreign:N0} bytes during a build that held "
            + $"{peak.Held:N0}. If the solver now allocates on worker threads outside {nameof(GridArrays)}, the live "
            + "set is neither read at those allocations nor bounded. Otherwise the measuring process was not the "
            + "build's alone.");

    /// <summary>The child's work: each case built once unmeasured, then measured.</summary>
    /// <param name="tier">The tier this process is to be running as.</param>
    /// <param name="cases">The cases, each <c>kind=size</c>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Child(JitTier tier, ReadOnlySpan<string> cases)
    {
        if (JitTier.Refusal(tier) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        foreach (var item in cases)
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            var kind = item[..split];
            var size = double.Parse(item[(split + 1)..], CultureInfo.InvariantCulture);

            SolveReport? solved = null;
            var build = Build(kind, size, report => solved = report);

            build();

            var peak = LiveSet.Peak(build);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{item} {peak.Least:R} {peak.Held:R} {peak.Bound:R} {peak.Readings} {peak.Unreported} {peak.Foreign} "
                + $"{peak.ForeignBurst} {peak.Retaken} "
                + $"{(Direct.ContainsKey(kind) ? solved!.Levels : -1)} {(Direct.ContainsKey(kind) ? solved!.Cycles : -1)}"));
        }

        // And still, after every build has run twice: a warm-up is what promotes code.
        if (JitTier.Refusal(tier) is { } late)
        {
            Console.Error.WriteLine("after the builds: " + late);
            return 2;
        }

        return 0;
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

    private readonly Lazy<Dictionary<string, (MeasuredBuild Build, string Line)>> _optimized =
        new(() => MemoryCases.Measure(JitTier.Optimized, OptimizedCases));

    private readonly Lazy<Dictionary<string, (MeasuredBuild Build, string Line)>> _first =
        new(() => MemoryCases.Measure(JitTier.First, FirstTierCases));

    /// <summary>A build measured as fully optimized code.</summary>
    internal MeasuredBuild Optimized(ITestOutputHelper output, string kind, double size) =>
        Find(_optimized.Value, output, kind, size);

    /// <summary>A build measured as first-tier code that is never promoted.</summary>
    internal MeasuredBuild FirstTier(ITestOutputHelper output, string kind, double size) =>
        Find(_first.Value, output, kind, size);

    private static MeasuredBuild Find(
        Dictionary<string, (MeasuredBuild Build, string Line)> measured, ITestOutputHelper output, string kind, double size)
    {
        var key = MemoryCases.Key(kind, size);

        Assert.True(measured.ContainsKey(key), $"{key} is not among the builds the child measures: add it to the list");

        output.WriteLine($"child: {measured[key].Line}");

        return measured[key].Build;
    }
}

/// <summary>Runs this test assembly's entry point as a child process and returns what it printed.</summary>
internal static class MeasuringChild
{
    /// <summary>Runs the child and returns its standard output's lines.</summary>
    /// <param name="arguments">The entry point's arguments.</param>
    /// <param name="tier">The tier of JIT code the child is to run as.</param>
    /// <returns>The non-empty lines the child printed.</returns>
    internal static string[] Run(IReadOnlyList<string> arguments, JitTier tier)
    {
        var start = new ProcessStartInfo(Host())
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

        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();

        if (!child.WaitForExit(TimeSpan.FromMinutes(20)))
        {
            child.Kill(entireProcessTree: true);
            Assert.Fail("the measuring child did not finish in twenty minutes");
        }

        var text = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();

        Assert.True(
            child.ExitCode == 0,
            $"the measuring child exited {child.ExitCode}: {errors}{Environment.NewLine}after printing:{Environment.NewLine}{text}");

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>The <c>dotnet</c> host this test process runs under.</summary>
    private static string Host()
    {
        // Set by the dotnet CLI for the processes it starts, test hosts included.
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host))
        {
            return host;
        }

        var self = Environment.ProcessPath;

        return self is not null && Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? self
            : "dotnet";
    }
}
