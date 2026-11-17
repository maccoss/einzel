using Einzel.Core.Numerics;
using Einzel.Fields.Solved;
using Einzel.Testing;

namespace Einzel.Fields.Tests;

/// <summary>A rediscretized solve driven directly, with the hierarchy forced.</summary>
/// <param name="Geometry">The geometry at a box length.</param>
/// <param name="Cycles">Cycles to run, exactly.</param>
/// <param name="Source">Whether to solve a Poisson problem with a uniform source.</param>
internal sealed record DirectCase(Func<double, Geometry3D> Geometry, int Cycles, bool Source);

/// <summary>
/// The builds the solver's memory tests measure, by name, for the shared child process
/// (<see cref="MeasuringChild"/>) to build and measure.
/// </summary>
internal static class MemoryCases
{
    /// <summary>What this test assembly tells the child: its command, its builds, and whose code they run.</summary>
    internal static ChildHost Host { get; } = new("solve-memory", Case, [typeof(PoissonSolver3D), typeof(GridArrays)]);

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

    /// <summary>The build a kind and a size name.</summary>
    private static MeasuredCase Case(string kind, double size)
    {
        if (Direct.TryGetValue(kind, out var direct))
        {
            var geometry = direct.Geometry(size);
            SolveReport? solved = null;

            return new MeasuredCase(
                () => Solve(geometry, direct, report => solved = report),
                () => new() { ["levels"] = solved!.Levels, ["cycles"] = solved.Cycles });
        }

        // Forty small fields, read at every one, with room made for `size` of them before the
        // baseline: GridArraysTests compares a build that outgrows its room with one that does not.
        if (kind == "fields")
        {
            return new MeasuredCase(
                () => Enumerable.Range(0, 40)
                    .Select(_ => new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9)))
                    .ToArray(),
                CollectEveryArray: true,
                Capacity: (int)size);
        }

        return new MeasuredCase(kind switch
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
            _ => throw new ArgumentException($"no build is called '{kind}'", nameof(kind)),
        });
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
/// Every build the solver's memory tests measure, measured once (<see cref="MeasuredCases"/>).
/// </summary>
public sealed class MeasuredBuilds() : MeasuredCases(MemoryCases.Host, OptimizedCases, FirstTierCases)
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
}
