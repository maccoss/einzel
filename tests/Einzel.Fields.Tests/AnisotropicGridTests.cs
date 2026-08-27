using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// Independent node spacings per axis, and the reason for having them.
/// </summary>
/// <remarks>
/// <para>
/// A grid used to keep its cells square and derive the y interval count from the
/// aspect ratio, rounded up to a power of two so both directions coarsen
/// together. Whatever box that count reached became the box that was solved. For
/// an aspect ratio that did not suit, it reached a long way past the one that had
/// been declared.
/// </para>
/// <para>
/// The Shortley-Weller stencil already carries a spacing per arm, so letting the
/// two axes differ costs the solver nothing and lets the declared domain be
/// meshed exactly. The compromise moves from extent, where it was unbounded and
/// invisible, to cell shape, where it is bounded at two to one and shows up in
/// the grid's own description.
/// </para>
/// </remarks>
public sealed class AnisotropicGridTests(ITestOutputHelper output)
{
    [Fact]
    public void TheSolvedDomainIsTheDeclaredDomain()
    {
        // The case that exposed it. Sixty by twenty millimetres at a one
        // millimetre cell needs 21.3 intervals in y; rounding that to 32 while
        // keeping the x spacing put the top of the grid at +20 mm instead of +10,
        // and the model was solved in a box fifty per cent taller than it asked
        // for, with nothing said.
        var solve = new CompiledSolvedField
        {
            MinX = 0.0,
            MinY = -0.01,
            MaxX = 0.06,
            MaxY = 0.01,
            CellSize = 0.001,
            Tolerance = 1e-12,
            Electrodes = [],
        };

        var grid = GeometryBuilder.BuildGrid(solve);

        output.WriteLine($"{grid}");
        output.WriteLine(
            $"declared x [{solve.MinX * 1e3:F3}, {solve.MaxX * 1e3:F3}] mm, realised max {grid.MaxX * 1e3:F3}");
        output.WriteLine(
            $"declared y [{solve.MinY * 1e3:F3}, {solve.MaxY * 1e3:F3}] mm, realised max {grid.MaxY * 1e3:F3}");

        Assert.Equal(solve.MaxX, grid.MaxX, 1e-12);
        Assert.Equal(solve.MaxY, grid.MaxY, 1e-12);

        // And never coarser than requested, in either direction.
        Assert.True(grid.SpacingX <= solve.CellSize, $"x spacing {grid.SpacingX} exceeds {solve.CellSize}");
        Assert.True(grid.SpacingY <= solve.CellSize, $"y spacing {grid.SpacingY} exceeds {solve.CellSize}");

        // Both counts round up to a power of two from the same requested cell
        // size, so each spacing lies in (cellSize/2, cellSize] and the worst
        // anisotropy is two to one.
        var ratio = Math.Max(grid.SpacingX / grid.SpacingY, grid.SpacingY / grid.SpacingX);
        output.WriteLine($"cell aspect ratio {ratio:F4}");
        Assert.True(ratio < 2.0, $"cell aspect ratio {ratio:F4} exceeds the two-to-one bound");
    }

    [Fact]
    public void ASquareGridIsUnchangedToTheLastBit()
    {
        // The anisotropic stencil scales its y half by (hx/hy) squared. On a square
        // grid that factor has to be exactly one rather than nearly one, because
        // multiplying by exactly one is the only way an isotropic solve is
        // guaranteed to be bit-identical to what it was before anisotropy existed.
        var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.1, intervalsX: 64);

        Assert.True(grid.IsSquare);
        Assert.Equal(1.0, grid.AspectSquared);
        Assert.Equal(grid.SpacingX, grid.MinimumSpacing);
    }

    [Fact]
    public void SecondOrderConvergenceSurvivesAStretchedGrid()
    {
        // The check that the stencil's y scaling is actually right. A wrong
        // aspect factor is a wrong Laplacian: the solve still converges, and it
        // converges to the wrong answer, so the error stops falling as h squared.
        // Nothing but an order measurement catches that.
        //
        // Deliberately a two-to-one grid, which is the worst BuildGrid can
        // produce, against a manufactured harmonic solution.
        var reference = new HarmonicReference(1000.0, 2.0 * Math.PI / 0.1);
        var errors = new List<(int Intervals, double Error)>();

        output.WriteLine("grid                                 hx/mm    hy/mm   max error   order");

        var previous = double.NaN;

        foreach (var intervals in new[] { 16, 32, 64, 128 })
        {
            // 0.1 by 0.05 with equal interval counts: hy is exactly half hx.
            var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.05, intervals, intervals);
            var (solved, report) = reference.SolveOn(grid);

            Assert.True(report.Converged, $"solve on {grid} did not converge: {report}");
            Assert.Equal(2.0, grid.SpacingX / grid.SpacingY, 1e-12);

            var error = reference.MaximumError(solved);
            var order = double.IsNaN(previous) ? double.NaN : Math.Log2(previous / error);
            previous = error;
            errors.Add((intervals, error));

            output.WriteLine(
                $"{grid,-36} {grid.SpacingX * 1e3,7:F4} {grid.SpacingY * 1e3,8:F4}   {error,9:E3}   {order,5:F2}");
        }

        for (var k = 1; k < errors.Count; k++)
        {
            var order = Math.Log2(errors[k - 1].Error / errors[k].Error);

            Assert.True(
                order is > 1.8 and < 2.2,
                $"observed order {order:F3} between {errors[k - 1].Intervals} and {errors[k].Intervals} "
                + "intervals on a stretched grid is not the nominal 2");
        }
    }

    [Fact]
    public void ACutBoundaryStillLandsWhereItSaysOnAStretchedGrid()
    {
        // Cut fractions are measured in cells, and cells are no longer square, so
        // a fraction along y and a fraction along x are different lengths. The
        // stencil has to keep those straight. Here the surface being cut is
        // horizontal, so it is the y arms that are cut on a grid whose y spacing
        // is half its x spacing - the arrangement that would go unnoticed if the
        // aspect factor were applied to the wrong half.
        //
        // The exact potential is a ramp in y, which any consistent stencil
        // reproduces exactly wherever the boundary sits.
        const double Applied = 1000.0;
        const double Gap = 0.005123;

        var solve = new CompiledSolvedField
        {
            MinX = 0.0,
            MinY = 0.0,
            MaxX = 0.04,
            MaxY = 0.0075,
            CellSize = 0.000625,
            LeftEdge = BoundaryKind.Neumann,
            RightEdge = BoundaryKind.Neumann,
            Tolerance = 1e-12,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "ground",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = -0.001, MaxX = 0.041, MinY = -0.001, MaxY = 0.0,
                    Potential = 0.0,
                },
                new CompiledElectrode
                {
                    Name = "plate",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = -0.001, MaxX = 0.041, MinY = Gap, MaxY = 0.0085,
                    Potential = Applied,
                },
            ],
        };

        var grid = GeometryBuilder.BuildGrid(solve);
        var mask = GeometryBuilder.BuildMask(solve, grid);

        var (potential, report) = PoissonSolver2D.Solve(
            mask, solve.Tolerance, maximumCycles: 400, coarsen: c => GeometryBuilder.BuildMask(solve, c));

        Assert.True(report.Converged, $"the solve did not converge: {report}");

        var worst = 0.0;
        var i = grid.CountX / 2;

        for (var j = 0; j < grid.CountY; j++)
        {
            var y = grid.Y(j);

            if (y <= 0.0 || y >= Gap)
            {
                continue;
            }

            worst = Math.Max(worst, Math.Abs(potential[i, j] - (Applied * y / Gap)) / Applied);
        }

        output.WriteLine($"{grid}, gap {Gap * 1e3:F4} mm = {Gap / grid.SpacingY:F3} y cells");
        output.WriteLine($"worst error {worst:E3} of applied, {report.Cycles} cycles at {report.ConvergenceFactor:F4}");

        Assert.False(grid.IsSquare, "this test is pointless on a square grid");
        Assert.True(
            worst < 1e-6,
            $"a boundary cut across y on a stretched grid cost {worst:E3} of the applied potential");
    }
}
