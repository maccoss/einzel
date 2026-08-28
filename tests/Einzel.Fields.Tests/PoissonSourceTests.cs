using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The multigrid solver with a non-zero right-hand side.
/// </summary>
/// <remarks>
/// <para>
/// Everything solved here until now has been <em>Laplace</em> — a potential with no
/// charge in it, fixed on conductors. SC-1's approximate method needs
/// <em>Poisson</em>: deposit the packet's own charge onto a grid, solve
/// <c>grad^2 phi = -rho / epsilon0</c>, and gather the field back. The cycle already
/// carried a right-hand side and had only ever been handed zeros, so the source costs
/// one argument and no numerics.
/// </para>
/// <para>
/// Checked by the method of manufactured solutions, which is the sharpest thing
/// available: pick a potential, differentiate it analytically to get the source that
/// produces it, hand the solver that source, and compare. There is no reference
/// implementation involved and no discretisation on the exact side.
/// </para>
/// </remarks>
public sealed class PoissonSourceTests(ITestOutputHelper output)
{
    /// <summary>A box with every edge held at zero.</summary>
    private static DirichletMask Grounded(int intervals)
    {
        var grid = Grid2D.OverBox(0.0, 0.0, 1.0, 1.0, intervals);
        var mask = new DirichletMask(grid);

        for (var i = 0; i < grid.CountX; i++)
        {
            mask.Fix(i, 0, 0.0);
            mask.Fix(i, grid.CountY - 1, 0.0);
        }

        for (var j = 0; j < grid.CountY; j++)
        {
            mask.Fix(0, j, 0.0);
            mask.Fix(grid.CountX - 1, j, 0.0);
        }

        return mask;
    }

    /// <summary>
    /// The manufactured solution: sin(pi x) sin(pi y), zero on every edge of the
    /// unit square, with Laplacian -2 pi^2 times itself.
    /// </summary>
    private static double Exact(double x, double y) => double.SinPi(x) * double.SinPi(y);

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void AManufacturedSourceIsSolvedToSecondOrder(int intervals)
    {
        var mask = Grounded(intervals);
        var grid = mask.Grid;

        var source = new ScalarField2D(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                // grad^2 (sin pi x sin pi y) = -2 pi^2 sin pi x sin pi y, exactly.
                source[i, j] = -2.0 * Math.PI * Math.PI * Exact(grid.X(i), grid.Y(j));
            }
        }

        var (potential, report) = PoissonSolver2D.Solve(
            mask, tolerance: 1e-12, maximumCycles: 200, source: source);

        var worst = 0.0;

        for (var j = 1; j < grid.CountY - 1; j++)
        {
            for (var i = 1; i < grid.CountX - 1; i++)
            {
                worst = Math.Max(worst, Math.Abs(potential[i, j] - Exact(grid.X(i), grid.Y(j))));
            }
        }

        output.WriteLine(
            $"{intervals,4} intervals: worst {worst:E4}, "
            + $"{report.Cycles} cycles at factor {report.ConvergenceFactor:F4}");

        Assert.True(report.Converged);

        // Second order in the cell size, which is what a five-point stencil gives.
        // The bound is the theoretical constant for this solution, pi^2 h^2 / 12,
        // with a factor of two of slack.
        var h = 1.0 / intervals;
        var bound = 2.0 * Math.PI * Math.PI * h * h / 12.0;

        Assert.True(worst < bound, $"{worst:E4} against a second-order bound of {bound:E4}");
    }

    [Fact]
    public void TheErrorFallsAsTheSquareOfTheCellSize()
    {
        // The convergence claim, measured rather than bounded. A source that entered
        // the smoother and the residual inconsistently would still converge - to the
        // wrong answer - and would show it here as an order that is not two.
        output.WriteLine("intervals    worst error    order");

        var previous = 0.0;

        foreach (var intervals in new[] { 32, 64, 128 })
        {
            var mask = Grounded(intervals);
            var grid = mask.Grid;
            var source = new ScalarField2D(grid);

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    source[i, j] = -2.0 * Math.PI * Math.PI * Exact(grid.X(i), grid.Y(j));
                }
            }

            var (potential, _) = PoissonSolver2D.Solve(
                mask, tolerance: 1e-12, maximumCycles: 200, source: source);

            var worst = 0.0;

            for (var j = 1; j < grid.CountY - 1; j++)
            {
                for (var i = 1; i < grid.CountX - 1; i++)
                {
                    worst = Math.Max(worst, Math.Abs(potential[i, j] - Exact(grid.X(i), grid.Y(j))));
                }
            }

            var order = previous == 0.0 ? double.NaN : Math.Log2(previous / worst);

            output.WriteLine($"{intervals,9}    {worst:E4}    {order,5:F3}");

            if (previous != 0.0)
            {
                Assert.InRange(order, 1.85, 2.15);
            }

            previous = worst;
        }
    }

    [Fact]
    public void NoSourceIsTheLaplaceSolveItAlwaysWas()
    {
        // The control. Passing null must give exactly what the solver gave before a
        // source existed - not nearly, exactly - or every number this engine has
        // published from a solved field has moved.
        var mask = Grounded(64);

        for (var i = 0; i < mask.Grid.CountX; i++)
        {
            mask.Fix(i, mask.Grid.CountY - 1, 100.0);
        }

        var (withoutSource, first) = PoissonSolver2D.Solve(mask, tolerance: 1e-12);
        var (withZeroSource, second) = PoissonSolver2D.Solve(
            mask, tolerance: 1e-12, source: new ScalarField2D(mask.Grid));

        var worst = 0.0;

        for (var j = 0; j < mask.Grid.CountY; j++)
        {
            for (var i = 0; i < mask.Grid.CountX; i++)
            {
                worst = Math.Max(worst, Math.Abs(withoutSource[i, j] - withZeroSource[i, j]));
            }
        }

        output.WriteLine($"worst difference {worst:E3} over {first.Cycles} and {second.Cycles} cycles");

        Assert.Equal(0.0, worst);
        Assert.Equal(first.Cycles, second.Cycles);
    }

    [Fact]
    public void ASourceOnTheWrongGridIsRefused()
    {
        // Values that do not correspond to the nodes being solved. Refused rather
        // than indexed into, because the shapes would often be compatible enough to
        // run and the field would be of a charge distribution nobody described.
        var mask = Grounded(32);
        var other = new ScalarField2D(Grid2D.OverBox(0.0, 0.0, 1.0, 1.0, 64));

        var error = Assert.Throws<ArgumentException>(
            () => PoissonSolver2D.Solve(mask, source: other));

        output.WriteLine(error.Message);

        Assert.Contains("different grid", error.Message, StringComparison.Ordinal);
    }
}
