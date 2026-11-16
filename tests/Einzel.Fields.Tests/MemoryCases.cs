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
/// The child builds each kind once before measuring it, so that what a first call allocates for
/// itself and keeps - statics, tables - is in the baseline rather than in the first reading.
/// </para>
/// <para>
/// The child only measures. What a build should hold, and whether it does, stay in the tests.
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

    /// <summary>Measures builds in one child process, in the order given.</summary>
    /// <param name="output">Where the child's output is copied.</param>
    /// <param name="cases">Each build's kind and its size: a cell size, or a box length for a direct solve.</param>
    /// <returns>One measurement per case.</returns>
    internal static MeasuredBuild[] Measure(ITestOutputHelper output, params (string Kind, double Size)[] cases)
    {
        var arguments = cases
            .Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Kind}={c.Size:R}"))
            .ToArray();

        // Fully optimized from the first call, so that what a local's lifetime is does not
        // depend on which methods the runtime has got round to promoting by the time they are
        // measured. FirstTierMemoryTests pins the other end.
        var lines = MeasuringChild.Run(output, [Command, .. arguments], JitTier.Optimized);

        Assert.Equal(cases.Length, lines.Length);

        return [.. lines.Select((line, at) =>
        {
            var parts = line.Split(' ');

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

            return new MeasuredBuild(
                peak,
                int.Parse(parts[9], CultureInfo.InvariantCulture),
                int.Parse(parts[10], CultureInfo.InvariantCulture));
        })];
    }

    /// <summary>
    /// Fails a measurement during which other threads allocated a large part of what the build
    /// held.
    /// </summary>
    /// <remarks>
    /// <see cref="AllocationProbe"/> is per thread: an array a build allocates on a worker thread
    /// is not read at and not bounded, only counted among what other threads allocated. The
    /// solvers allocate on the thread that calls them, and in a child process that count is a
    /// few hundred bytes a reading. A quarter of the build's own peak is far above anything a
    /// runtime's housekeeping does and far below a solver that has moved its per-node arrays to
    /// workers, which would allocate several times its peak there.
    /// </remarks>
    internal static void AssertTheBuildsOwn(string name, LivePeak peak) =>
        Assert.True(
            peak.Foreign <= 0.25 * peak.Held,
            $"{name}: threads other than the build's allocated {peak.Foreign:N0} bytes during a build that held "
            + $"{peak.Held:N0}. If the solver now allocates on worker threads, {nameof(AllocationProbe)} - which is per "
            + "thread - neither reads the live set at those allocations nor bounds them, and the observer has to "
            + "follow the build onto them. Otherwise the measuring process was not the build's alone.");


    /// <summary>The child's work: each case built once unmeasured per kind, then measured.</summary>
    /// <param name="cases">The cases, each <c>kind=size</c>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Child(ReadOnlySpan<string> cases)
    {
        if (JitTier.Refusal(JitTier.Optimized) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        var warmed = new HashSet<string>();

        foreach (var item in cases)
        {
            var split = item.IndexOf('=', StringComparison.Ordinal);
            var kind = item[..split];
            var size = double.Parse(item[(split + 1)..], CultureInfo.InvariantCulture);

            SolveReport? solved = null;
            var build = Build(kind, size, report => solved = report);

            if (warmed.Add(kind))
            {
                build();
            }

            var peak = LiveSet.Peak(build);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{item} {peak.Least:R} {peak.Held:R} {peak.Bound:R} {peak.Readings} {peak.Unreported} {peak.Foreign} "
                + $"{peak.ForeignBurst} {peak.Retaken} "
                + $"{(Direct.ContainsKey(kind) ? solved!.Levels : -1)} {(Direct.ContainsKey(kind) ? solved!.Cycles : -1)}"));
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

/// <summary>Runs this test assembly's entry point as a child process and returns what it printed.</summary>
internal static class MeasuringChild
{
    /// <summary>Runs the child and returns its standard output's lines.</summary>
    /// <param name="output">Where both of the child's streams are copied.</param>
    /// <param name="arguments">The entry point's arguments.</param>
    /// <param name="tier">The tier of JIT code the child is to run as.</param>
    /// <returns>The non-empty lines the child printed.</returns>
    internal static string[] Run(ITestOutputHelper output, IReadOnlyList<string> arguments, JitTier tier)
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

        if (!child.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            child.Kill(entireProcessTree: true);
            Assert.Fail("the measuring child did not finish in ten minutes");
        }

        var text = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();

        output.WriteLine(text);
        output.WriteLine(errors);

        Assert.True(child.ExitCode == 0, $"the measuring child exited {child.ExitCode}: {errors}");

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

/// <summary>Which tier of JIT code a measuring child runs as, set by its parent and checked by it.</summary>
/// <remarks>
/// <para>
/// What a solve holds depends on the tier: first-tier code keeps a local alive to the end of its
/// method and optimized code to its last use. Which a method runs as is otherwise the machine's
/// choice - how often it has been called, how busy the background compiler is - so each child is
/// pinned to one end, and <b>checks which end it is at by what the code does</b> rather than by
/// what the environment says: it drops the last use of an object and sees whether a collection
/// takes it. A shell that exports <c>DOTNET_TieredCompilation=0</c> used to turn the first-tier
/// child into an optimized one that passed for the wrong reason.
/// </para>
/// <para>
/// A Debug build's code keeps its locals in every tier, so there the optimized child cannot be
/// optimized and says so instead of refusing; the first-tier child's premise holds either way.
/// </para>
/// </remarks>
internal sealed class JitTier
{
    private static readonly string[] Variables =
        ["TieredCompilation", "TC_QuickJit", "TC_QuickJitForLoops", "TC_CallCounting", "TieredPGO", "TC_OnStackReplacement", "ReadyToRun"];

    private readonly bool _optimized;

    private JitTier(bool optimized) => _optimized = optimized;

    /// <summary>Everything compiled fully optimized at its first call.</summary>
    internal static JitTier Optimized { get; } = new(true);

    /// <summary>Everything compiled quickly and never promoted.</summary>
    internal static JitTier First { get; } = new(false);

    /// <summary>The variables to set, and with a null value to remove, for a child of this tier.</summary>
    internal IEnumerable<(string Name, string? Value)> Environment()
    {
        foreach (var variable in Variables)
        {
            yield return ($"COMPlus_{variable}", null);
            yield return ($"DOTNET_{variable}", null);
        }

        if (_optimized)
        {
            yield return ("DOTNET_TieredCompilation", "0");
        }
        else
        {
            // Nothing is counted, so nothing is promoted: every method stays first-tier.
            yield return ("DOTNET_TieredCompilation", "1");
            yield return ("DOTNET_TC_QuickJit", "1");
            yield return ("DOTNET_TC_QuickJitForLoops", "1");
            yield return ("DOTNET_TC_OnStackReplacement", "0");
            yield return ("DOTNET_TC_CallCounting", "0");
            yield return ("DOTNET_TieredPGO", "0");
        }
    }

    /// <summary>
    /// Why this process is not running as the tier asked for, or null if it is.
    /// </summary>
    internal static string? Refusal(JitTier wanted)
    {
        var keeps = ALocalOutlivesItsLastUse();
        var unoptimizedBuild = !IsOptimizedBuild(typeof(PoissonSolver3D)) || !IsOptimizedBuild(typeof(JitTier));

        if (wanted._optimized)
        {
            // A Debug build is unoptimized in every tier, and is measured as what it is.
            return keeps && !unoptimizedBuild
                ? "this process was to run fully optimized code and a local outlives its last use: "
                    + "tiering is on, so what a build holds would depend on what has been promoted"
                : null;
        }

        return keeps
            ? null
            : "this process was to run first-tier code and a local dies at its last use: the code is optimized "
                + "(DOTNET_TieredCompilation=0, or a tiering variable set outside), so a first-tier hold cannot show";
    }

    private static bool IsOptimizedBuild(Type type) =>
        type.Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false) is not [DebuggableAttribute debuggable, ..]
        || !debuggable.IsJITOptimizerDisabled;

    /// <summary>Whether, in the code this process is running, an object survives its local's last use.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool ALocalOutlivesItsLastUse()
    {
        var held = new object();
        var weak = new WeakReference(held);

        Use(held);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        return weak.IsAlive;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Use(object value) => GC.KeepAlive(value);
}
