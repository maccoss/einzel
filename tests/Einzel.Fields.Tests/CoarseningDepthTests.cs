using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// How far the V-cycle actually descends, and why the guard that stops it is
/// load-bearing rather than cautious.
/// </summary>
/// <remarks>
/// <para>
/// A multigrid solver's defining property is that its cycle count does not depend on
/// the mesh, and that rests on the bottom of the V being small. Here it often is not:
/// <c>Representable</c> stops coarsening once a coarse cell would exceed the smallest
/// electrode dimension, and <b>that is a physical size, so it does not move when the
/// mesh is refined</b>. The levels get added at the top and the bottom stays where it
/// was.
/// </para>
/// <para>
/// These tests exist because none of that is visible in a cycle count, and cycle
/// counts are what get compared. A cycle at zero levels is several hundred smoothing
/// sweeps over the finest grid; a cycle at five levels is a handful per level. Two
/// solves whose cycle counts differ by eight can differ a hundredfold in work, in the
/// other direction.
/// </para>
/// </remarks>
public sealed class CoarseningDepthTests(ITestOutputHelper output)
{
    /// <summary>
    /// The guard is what keeps a thin-slab geometry correct, and without it the solve
    /// is faster, converges, and is wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The measurement this pins: relaxing the guard on two 1 mm slabs at a 0.25 mm
    /// cell takes the solve from 45 cycles and 145 seconds to <b>5 cycles and 4
    /// seconds</b> - and to a <b>peak of 486 V of 100 applied</b>. It reports
    /// converged. Only the maximum principle catches it.
    /// </para>
    /// <para>
    /// The mechanism is not that a coarse grid is cruder. At four levels down a 1 mm
    /// slab is smaller than a cell, so it is <em>pinned to a single node</em> - and the
    /// coarse problem then constrains the error at two isolated points where the fine
    /// problem constrains it over two whole planes. The correction that comes back is a
    /// solution to a different problem, and prolonging it adds a field nobody asked
    /// for.
    /// </para>
    /// <para>
    /// So this test guards a constant somebody will reasonably want to raise, having
    /// seen that raising it makes everything faster. It is asserted against the
    /// <em>rediscretised</em> hierarchy specifically, because that is the one the guard
    /// belongs to: the Galerkin hierarchy descends all the way and stays correct, which
    /// is asserted separately in <c>GalerkinOperatorTests</c>. Removing the guard is
    /// still wrong; removing the need for it is what Galerkin did.
    /// </para>
    /// </remarks>
    [Fact]
    public void AThinSlabHoldsTheMaximumPrincipleBecauseItRefusesToCoarsen()
    {
        var geometry = Plates(0.0005);

        var grid = GeometryBuilder3D.BuildGrid(geometry);
        var mask = GeometryBuilder3D.BuildMask(geometry, grid);

        var (potential, report) = PoissonSolver3D.Solve(
            mask, geometry.Tolerance, maximumCycles: 200,
            coarsen: GeometryBuilder3D.Coarsener(geometry), galerkin: false);

        var peak = 0.0;

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    peak = Math.Max(peak, Math.Abs(potential[i, j, k]));
                }
            }
        }

        output.WriteLine(
            $"{grid.CountX}x{grid.CountY}x{grid.CountZ}: {report.Levels} level(s), "
            + $"coarsest {report.CoarsestNodes:N0} node(s), {report.Cycles} cycles, "
            + $"{report.Sweeps:N0} sweeps, peak {peak:F2} V");

        // The tolerance-free check. No potential in a Laplace solution may exceed the
        // largest applied value, so anything above 100 V is proof of divergence and
        // needs no reference answer to compare against.
        Assert.InRange(peak, 99.99, 100.01);

        // And the reason it is correct: it barely coarsened. If this ever rises, the
        // assertion above is the one that matters - a deeper descent on this geometry
        // was measured at 486 V.
        Assert.InRange(report.Levels, 0, 1);
    }

    /// <summary>
    /// A cycle is not a unit of work, and the report says so rather than implying it.
    /// </summary>
    /// <remarks>
    /// At zero levels the whole V-cycle reduces to relaxation on the finest grid, so
    /// the coarsest level <em>is</em> the finest one. That equality is the compact
    /// statement of "this was not multigrid", and it is worth asserting because the
    /// convergence factor looks healthy in exactly that case: the shipped parallel-plate
    /// geometry reports 0.015 at a 0.5 mm cell while doing 400 sweeps a cycle.
    /// </remarks>
    [Fact]
    public void ZeroLevelsMeansTheCoarsestGridIsTheFinestOne()
    {
        var geometry = Plates(0.001);

        var grid = GeometryBuilder3D.BuildGrid(geometry);
        var mask = GeometryBuilder3D.BuildMask(geometry, grid);

        var (_, report) = PoissonSolver3D.Solve(
            mask, geometry.Tolerance, maximumCycles: 200,
            coarsen: GeometryBuilder3D.Coarsener(geometry), galerkin: false);

        output.WriteLine(
            $"{report.Levels} level(s), coarsest {report.CoarsestNodes:N0} of "
            + $"{grid.NodeCount:N0} node(s), {report.Sweeps:N0} sweeps");

        Assert.Equal(0, report.Levels);
        Assert.Equal(grid.NodeCount, report.CoarsestNodes);

        // Many sweeps per cycle, which is what a cycle count hides.
        Assert.True(
            report.Sweeps > 20L * report.Cycles,
            $"{report.Sweeps} sweeps over {report.Cycles} cycles is not the relaxation "
            + "this geometry is documented as falling back to");
    }

    /// <summary>
    /// Two 1 mm slabs across a 5 mm gap, in a box only big enough to hold them.
    /// </summary>
    /// <remarks>
    /// Small on purpose. What sets the coarsening depth is the slab's <em>thickness</em>,
    /// which is a property of the electrode rather than of the domain, so the same
    /// behaviour appears on a mesh cheap enough to put in a test suite.
    /// </remarks>
    private static Geometry3D Plates(double cell) => new(
        -0.006, -0.006, -0.005, 0.006, 0.006, 0.005, cell,
        [
            Box("lower", -0.004, -0.004, -0.0035, 0.004, 0.004, -0.0025, 100.0),
            Box("upper", -0.004, -0.004, 0.0025, 0.004, 0.004, 0.0035, 0.0),
        ]);

    private static CompiledElectrode3D Box(
        string name, double x0, double y0, double z0, double x1, double y1, double z1, double v) =>
        new()
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            MinX = x0,
            MinY = y0,
            MinZ = z0,
            MaxX = x1,
            MaxY = y1,
            MaxZ = z1,
            Potential = v,
        };
}
