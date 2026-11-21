using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A cross-section's cut links are what their definition says, and building them allocates
/// what the mask keeps.
/// </summary>
/// <remarks>
/// <para>
/// The plane cut-link builder walked its electrodes through an interface for every arm of every
/// free node, which allocates an enumerator each time: 101 bytes a node of garbage across a
/// mask. No test of what a build holds can see garbage - it was found when the memory tests
/// started bounding what a build allocates between two readings - and nothing compared a plane
/// mask against anything when the loop was rewritten. The volume builder has both checks
/// (<c>MaskAllocationTests</c>, <c>VolumeMaskCullingTests</c>); these are the plane's.
/// </para>
/// <para>
/// <b>The reference is the definition, written here</b>: an arm from a free node is cut at the
/// nearest positive first entry over every electrode, earlier electrodes winning ties, and holds
/// that electrode's potential. It shares only <see cref="CompiledElectrode.FirstEntry"/> with
/// the builder, and it is compared bit for bit.
/// </para>
/// </remarks>
public sealed class PlaneMaskTests(ITestOutputHelper output)
{
    private static readonly (int Di, int Dj, StencilDirection Direction)[] Arms =
    [
        (1, 0, StencilDirection.East),
        (-1, 0, StencilDirection.West),
        (0, 1, StencilDirection.North),
        (0, -1, StencilDirection.South),
    ];

    /// <summary>Every arm of every free node holds the nearest entry of the definition, to the bit.</summary>
    [Theory]
    [InlineData(0.00025)]
    [InlineData(0.0001)]
    public void EveryCutLinkIsTheNearestEntryOverEveryElectrode(double cell)
    {
        var solve = Mixed(cell);
        var grid = GeometryBuilder.BuildGrid(solve);
        var mask = GeometryBuilder.BuildMask(solve, grid);

        Assert.NotNull(mask.Cuts);

        var cut = 0;
        var wrong = 0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                foreach (var (di, dj, direction) in Arms)
                {
                    var expectedFraction = 1.0;
                    var expectedPotential = 0.0;

                    var ni = i + di;
                    var nj = j + dj;

                    if (!mask.IsFixed(i, j) && ni >= 0 && nj >= 0 && ni < grid.CountX && nj < grid.CountY)
                    {
                        foreach (var electrode in solve.Electrodes)
                        {
                            if (electrode.FirstEntry(grid.X(i), grid.Y(j), grid.X(ni), grid.Y(nj)) is { } entry
                                && entry > 0.0
                                && entry < expectedFraction)
                            {
                                expectedFraction = entry;
                                expectedPotential = electrode.Potential;
                            }
                        }
                    }

                    if (expectedFraction < 1.0)
                    {
                        cut++;
                        expectedFraction = Math.Max(expectedFraction, CutLinks.MinimumFraction);
                    }

                    var fraction = mask.Cuts.Fraction(i, j, direction);
                    var potential = mask.Cuts.Potential(i, j, direction);

                    if (BitConverter.DoubleToInt64Bits(fraction) != BitConverter.DoubleToInt64Bits(expectedFraction)
                        || (expectedFraction < 1.0
                            && BitConverter.DoubleToInt64Bits(potential) != BitConverter.DoubleToInt64Bits(expectedPotential)))
                    {
                        wrong++;
                    }
                }
            }
        }

        output.WriteLine($"{grid.CountX}x{grid.CountY} nodes: {cut:N0} cut arms, {wrong} differ from the definition");

        // Discs and a rectangle, at two potentials each: enough arms that a loop which skipped
        // an electrode, or kept the last instead of the nearest, would differ on many.
        Assert.True(cut > 200, $"only {cut} arms are cut");
        Assert.Equal(mask.Cuts.CutCount, cut);
        Assert.Equal(0, wrong);
    }

    /// <summary>Building a plane mask allocates the arrays it keeps and next to nothing else.</summary>
    [Fact]
    public void APlaneMaskAllocatesOnlyWhatItKeeps()
    {
        var solve = Mixed(0.0001);
        var grid = GeometryBuilder.BuildGrid(solve);

        double PerNode()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var built = GeometryBuilder.BuildMask(solve, grid);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(built.Cuts?.CutCount > 0, "no arm was cut, so no entry test was run");

            return allocated / (double)grid.NodeCount;
        }

        var cheapest = Enumerable.Range(0, 5).Select(_ => PerNode()).Min();

        // The fixed flags and values, and a fraction and a potential for each of four arms.
        const double kept = 1 + 8 + (2 * 4 * 8);

        output.WriteLine(
            $"{grid.CountX}x{grid.CountY} nodes: {cheapest:F2} bytes a node allocated, against {kept:F0} the mask keeps");

        // An enumerator for every arm read 174 here, and in a Debug build a handle stub for
        // every node of a coarse mask 18 more.
        Assert.InRange(cheapest, kept, kept + 1.0);
    }

    /// <summary>Four discs and a slab, the discs at two potentials, in a 16 mm box.</summary>
    private static CompiledSolvedField Mixed(double cell) => new()
    {
        MinX = -0.008,
        MaxX = 0.008,
        MinY = -0.008,
        MaxY = 0.008,
        CellSize = cell,
        Tolerance = 1e-10,
        Electrodes =
        [
            .. SolveMemoryTests.Rods(cell).Electrodes,
            new CompiledElectrode
            {
                Name = "slab",
                Shape = ElectrodeShape.Rectangle,
                MinX = -0.00123,
                MaxX = 0.00117,
                MinY = -0.00071,
                MaxY = 0.00064,
                Potential = 37.0,
            },
        ],
    };
}
