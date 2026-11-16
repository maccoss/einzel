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
        var lines = MeasuringChild.Run(
            output,
            [Command, .. arguments],
            new Dictionary<string, string> { ["DOTNET_TieredCompilation"] = "0" });

        Assert.Equal(cases.Length, lines.Length);

        return [.. lines.Select((line, at) =>
        {
            var parts = line.Split(' ');

            Assert.Equal(arguments[at], parts[0]);

            return new MeasuredBuild(
                new LivePeak(
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture),
                    int.Parse(parts[3], CultureInfo.InvariantCulture),
                    long.Parse(parts[4], CultureInfo.InvariantCulture),
                    long.Parse(parts[5], CultureInfo.InvariantCulture),
                    long.Parse(parts[6], CultureInfo.InvariantCulture),
                    int.Parse(parts[7], CultureInfo.InvariantCulture)),
                int.Parse(parts[8], CultureInfo.InvariantCulture),
                int.Parse(parts[9], CultureInfo.InvariantCulture));
        })];
    }

    /// <summary>The child's work: each case built once unmeasured per kind, then measured.</summary>
    /// <param name="cases">The cases, each <c>kind=size</c>.</param>
    /// <returns>The process exit code.</returns>
    internal static int Child(ReadOnlySpan<string> cases)
    {
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
                $"{item} {peak.Held:R} {peak.Bound:R} {peak.Readings} {peak.Unreported} {peak.Foreign} {peak.ForeignBurst} {peak.Discarded} "
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
    /// <param name="environment">Variables to set for the child, or null.</param>
    /// <returns>The non-empty lines the child printed.</returns>
    internal static string[] Run(
        ITestOutputHelper output,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment)
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

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
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
