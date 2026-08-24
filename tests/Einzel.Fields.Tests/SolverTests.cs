using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

public sealed class SolverTests(ITestOutputHelper output)
{
    private static HarmonicReference Reference => new(amplitude: 100.0, wavenumber: Math.PI / 0.1);

    [Fact]
    public void ParallelPlateIsExact()
    {
        // Spec section 19's analytic tier: "parallel-plate and coaxial fields
        // against closed form". A linear potential is in the null space of the
        // five-point stencil's truncation error, so the only residual is round-off
        // and the solver has nowhere to hide.
        var grid = Grid2D.OverBox(0.0, -0.05, 0.06, 0.05, intervalsX: 64);
        var mask = new DirichletMask(grid)
        {
            // Symmetry above and below: with no y dependence imposed, the exact
            // solution is a pure ramp in x.
            TopEdge = EdgeCondition.Neumann,
            BottomEdge = EdgeCondition.Neumann,
        };

        const double capPotential = 4800.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            mask.Fix(0, j, 0.0);
            mask.Fix(grid.CountX - 1, j, capPotential);
        }

        var (solved, report) = PoissonSolver2D.Solve(mask, tolerance: 1e-12);

        Assert.True(report.Converged, $"solve did not converge: {report}");

        var worst = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var exact = capPotential * (grid.X(i) - grid.OriginX) / (grid.MaxX - grid.OriginX);
                worst = Math.Max(worst, Math.Abs(solved[i, j] - exact));
            }
        }

        output.WriteLine($"worst error {worst:E3} V on {capPotential} V");
        Assert.True(worst < 1e-6, $"parallel plate should be exact, but the worst error is {worst:E3} V");
    }

    [Fact]
    public void ManufacturedSolutionConvergesAtSecondOrder()
    {
        // GRD-4 and spec section 8: convergence order is observed and asserted
        // against nominal, not assumed. The five-point Laplacian is nominally
        // second order.
        var reference = Reference;
        var errors = new List<(int Intervals, double Error)>();

        foreach (var intervals in new[] { 16, 32, 64, 128 })
        {
            var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.1, intervals);
            var (solved, report) = reference.SolveOn(grid);

            Assert.True(report.Converged, $"solve on {grid} did not converge: {report}");
            errors.Add((intervals, reference.MaximumError(solved)));
        }

        foreach (var (intervals, error) in errors)
        {
            output.WriteLine($"{intervals,4} intervals: max error {error:E3} V");
        }

        for (var k = 1; k < errors.Count; k++)
        {
            var order = Math.Log(errors[k - 1].Error / errors[k].Error) / Math.Log(2.0);
            output.WriteLine($"observed order {errors[k - 1].Intervals} -> {errors[k].Intervals}: {order:F3}");

            Assert.True(
                order is > 1.8 and < 2.2,
                $"observed order {order:F3} between {errors[k - 1].Intervals} and {errors[k].Intervals} "
                + "intervals is not the nominal 2 for a five-point stencil");
        }
    }

    [Fact]
    public void ConvergenceIsGridIndependent()
    {
        // The property that makes this multigrid rather than a smoother with extra
        // steps: the residual reduction per cycle does not degrade as the grid
        // refines. Without it, PERF-1's thirty-minute budget for a full basis
        // campaign is unreachable at any useful resolution.
        var reference = Reference;
        var factors = new List<(int Intervals, int Cycles, double Factor)>();

        foreach (var intervals in new[] { 32, 64, 128, 256 })
        {
            var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.1, intervals);
            var (_, report) = reference.SolveOn(grid, tolerance: 1e-10);

            Assert.True(report.Converged, $"solve on {grid} did not converge");
            factors.Add((intervals, report.Cycles, report.ConvergenceFactor));
        }

        foreach (var (intervals, cycles, factor) in factors)
        {
            output.WriteLine($"{intervals,4} intervals: {cycles,3} cycles, factor {factor:F4}");
        }

        Assert.All(factors, f => Assert.True(
            f.Factor < 0.5,
            $"convergence factor {f.Factor:F3} at {f.Intervals} intervals is too close to 1 for a working V-cycle"));

        // Sixteen times the nodes must not cost materially more cycles.
        var growth = factors[^1].Cycles - factors[0].Cycles;
        Assert.True(
            growth <= 3,
            $"cycle count grew by {growth} from {factors[0].Intervals} to {factors[^1].Intervals} intervals, "
            + "which is not grid-independent convergence");
    }

    [Fact]
    public void BasisSuperpositionEqualsADirectSolve()
    {
        // Spec section 10: solve once per electrode at unit potential, then any
        // voltage set is a weighted sum. This is the check that the arithmetic
        // really does stand in for the solve.
        var grid = Grid2D.OverBox(0.0, 0.0, 0.08, 0.04, intervalsX: 64);

        var left = Nodes(grid, 0, 0, 0, grid.CountY - 1);
        var right = Nodes(grid, grid.CountX - 1, 0, grid.CountX - 1, grid.CountY - 1);
        var middle = Nodes(grid, grid.CountX / 2, 0, grid.CountX / 2, grid.CountY / 4);

        ElectrodeNodes[] electrodes =
        [
            new("left", left),
            new("right", right),
            new("stub", middle),
        ];

        static void Configure(DirichletMask mask)
        {
            mask.TopEdge = EdgeCondition.Neumann;
            mask.BottomEdge = EdgeCondition.Neumann;
        }

        var basis = BasisFieldSet.Solve(grid, electrodes, Configure, tolerance: 1e-13);
        double[] volts = [-250.0, 4800.0, 1200.0];
        var superposed = basis.Superpose(volts);

        // The same geometry solved directly at those potentials.
        var mask = new DirichletMask(grid);
        Configure(mask);

        for (var e = 0; e < electrodes.Length; e++)
        {
            foreach (var (i, j) in electrodes[e].Nodes)
            {
                mask.Fix(i, j, volts[e]);
            }
        }

        var (direct, report) = PoissonSolver2D.Solve(mask, tolerance: 1e-13);
        Assert.True(report.Converged);

        var worst = 0.0;
        var scale = volts.Max(Math.Abs);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                worst = Math.Max(worst, Math.Abs(superposed[i, j] - direct[i, j]));
            }
        }

        output.WriteLine($"worst superposition difference {worst:E3} V on a {scale} V scale");
        Assert.True(
            worst / scale < 1e-9,
            $"superposition differs from a direct solve by {worst:E3} V, relative {worst / scale:E3}");
    }

    [Fact]
    public void BasisFieldsAreLinearInTheirOwnElectrode()
    {
        var grid = Grid2D.OverBox(0.0, 0.0, 0.04, 0.02, intervalsX: 32);
        ElectrodeNodes[] electrodes =
        [
            new("left", Nodes(grid, 0, 0, 0, grid.CountY - 1)),
            new("right", Nodes(grid, grid.CountX - 1, 0, grid.CountX - 1, grid.CountY - 1)),
        ];

        var basis = BasisFieldSet.Solve(grid, electrodes, m => { }, tolerance: 1e-13);

        var single = basis.Superpose([1000.0, 0.0]);
        var doubled = basis.Superpose([2000.0, 0.0]);

        for (var j = 0; j < grid.CountY; j += 4)
        {
            for (var i = 0; i < grid.CountX; i += 4)
            {
                Assert.Equal(2.0 * single[i, j], doubled[i, j], 1e-9);
            }
        }
    }

    private static List<(int I, int J)> Nodes(Grid2D grid, int i0, int j0, int i1, int j1)
    {
        var nodes = new List<(int, int)>();

        for (var j = j0; j <= j1; j++)
        {
            for (var i = i0; i <= i1; i++)
            {
                nodes.Add((i, j));
            }
        }

        return nodes;
    }
}
