using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// Interior Dirichlet regions, which is what a rod or an aperture is, and which
/// the solver did not meet until a quadrupole arrived.
/// </summary>
/// <remarks>
/// The failure this guards against was not subtle in its effect and was entirely
/// invisible in its cause: potentials of 1e134 V from a geometry that is four
/// discs in a box. Coarsening past the point where an electrode is resolved gives
/// the coarse grid a different problem to solve, and its correction, prolonged
/// back, drives the iteration apart. The tell is a convergence factor that
/// worsens with refinement rather than holding steady.
/// </remarks>
public sealed class InteriorElectrodeSolveTests(ITestOutputHelper output)
{
    [Fact]
    public void InteriorDirichletRegions()
    {
        // Four discs in a box, which is the quadrupole reduced to its essentials.
        // Stage 3 only ever pinned boundary nodes; interior regions are new.
        foreach (var intervals in new[] { 32, 64, 128 })
        {
            var grid = Grid2D.OverBox(-0.02, -0.02, 0.02, 0.02, intervals);
            var mask = new DirichletMask(grid);

            // Outer box explicitly grounded.
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

            AddDisc(mask, grid, 0.010734, 0.0, 0.005734, 100.0);
            AddDisc(mask, grid, -0.010734, 0.0, 0.005734, 100.0);
            AddDisc(mask, grid, 0.0, 0.010734, 0.005734, -100.0);
            AddDisc(mask, grid, 0.0, -0.010734, 0.005734, -100.0);

            var (potential, report) = PoissonSolver2D.Solve(mask, 1e-10, maximumCycles: 200);

            var peak = 0.0;

            foreach (var v in potential.Values)
            {
                peak = Math.Max(peak, Math.Abs(v));
            }

            output.WriteLine(
                $"{intervals,4} intervals: converged {report.Converged}, {report.Cycles} cycles, "
                + $"factor {report.ConvergenceFactor:F4}, peak |phi| {peak:E3} V");

            Assert.True(report.Converged, $"the solve did not converge at {intervals} intervals: {report}");

            // The maximum principle: a harmonic function attains its extremes on
            // the boundary, so nothing anywhere may exceed the applied potential.
            // It is the cheapest possible check that a solve has not diverged, and
            // it is exact rather than a tolerance.
            Assert.True(
                peak <= 100.0 + 1e-6,
                $"peak potential {peak:E3} V exceeds the 100 V applied, which violates the maximum principle");

            Assert.True(
                report.ConvergenceFactor < 0.5,
                $"convergence factor {report.ConvergenceFactor:F3} at {intervals} intervals is too close to 1");
        }
    }

    [Fact]
    public void InteriorDirichletWithoutAGroundedBox()
    {
        // The same, but with nothing pinned on the outer edge — which is what the
        // quadrupole template declares, since it names only the rods.
        var grid = Grid2D.OverBox(-0.02, -0.02, 0.02, 0.02, 64);
        var mask = new DirichletMask(grid);

        AddDisc(mask, grid, 0.010734, 0.0, 0.005734, 100.0);
        AddDisc(mask, grid, -0.010734, 0.0, 0.005734, 100.0);
        AddDisc(mask, grid, 0.0, 0.010734, 0.005734, -100.0);
        AddDisc(mask, grid, 0.0, -0.010734, 0.005734, -100.0);

        var (potential, report) = PoissonSolver2D.Solve(mask, 1e-10, maximumCycles: 200);

        var peak = 0.0;

        foreach (var v in potential.Values)
        {
            peak = Math.Max(peak, Math.Abs(v));
        }

        output.WriteLine(
            $"no grounded box: converged {report.Converged}, {report.Cycles} cycles, "
            + $"factor {report.ConvergenceFactor:F4}, peak |phi| {peak:E3} V");

        Assert.True(report.Converged, $"the solve did not converge: {report}");
        Assert.True(peak <= 100.0 + 1e-6, $"peak potential {peak:E3} V exceeds the applied 100 V");
    }

    private static void AddDisc(
        DirichletMask mask, Grid2D grid, double cx, double cy, double radius, double potential)
    {
        var r2 = radius * radius;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = grid.X(i) - cx;
                var dy = grid.Y(j) - cy;

                if ((dx * dx) + (dy * dy) <= r2)
                {
                    mask.Fix(i, j, potential);
                }
            }
        }
    }
}
