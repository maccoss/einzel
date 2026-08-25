using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// Rasterisation of the three electrode primitives, which is the whole of what
/// makes a device a document rather than a class.
/// </summary>
public sealed class GeometryBuilderTests
{
    /// <remarks>
    /// Every edge is Neumann so that these tests see the electrodes and nothing
    /// else. A Dirichlet edge grounds the nodes along it, which is right for the
    /// solve and noise for a test about rasterisation; it has its own test below.
    /// </remarks>
    private static CompiledSolvedField Field(params CompiledElectrode[] electrodes) => new()
    {
        MinX = -0.01,
        MinY = -0.01,
        MaxX = 0.01,
        MaxY = 0.01,
        CellSize = 0.0005,
        LeftEdge = BoundaryKind.Neumann,
        RightEdge = BoundaryKind.Neumann,
        BottomEdge = BoundaryKind.Neumann,
        TopEdge = BoundaryKind.Neumann,
        Electrodes = electrodes,
        Tolerance = 1e-12,
        BoundaryIsDiscontinuous = true,
    };

    [Fact]
    public void TheGridIsNeverCoarserThanRequested()
    {
        // Interval counts round up to a power of two, so the actual spacing is at
        // least as fine as asked for. A grid coarser than requested would quietly
        // under-resolve a geometry the author had sized deliberately.
        var solve = Field(new CompiledElectrode { Name = "e", Shape = ElectrodeShape.Rectangle });
        var grid = GeometryBuilder.BuildGrid(solve);

        Assert.True(
            grid.SpacingX <= solve.CellSize && grid.SpacingY <= solve.CellSize,
            $"requested {solve.CellSize} m but got {grid.SpacingX} by {grid.SpacingY} m");

        Assert.True(int.IsPow2(grid.CountX - 1), $"{grid.CountX} nodes is not a power of two plus one");
    }

    [Fact]
    public void ARectangleFixesTheNodesInsideIt()
    {
        var electrode = new CompiledElectrode
        {
            Name = "plate",
            Shape = ElectrodeShape.Rectangle,
            MinX = -0.002,
            MaxX = 0.002,
            MinY = -0.001,
            MaxY = 0.001,
            Potential = 250.0,
        };

        var solve = Field(electrode);
        var grid = GeometryBuilder.BuildGrid(solve);
        var mask = GeometryBuilder.BuildMask(solve, grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var inside = grid.X(i) >= -0.002 && grid.X(i) <= 0.002
                    && grid.Y(j) >= -0.001 && grid.Y(j) <= 0.001;

                Assert.Equal(inside, mask.IsFixed(i, j));

                if (inside)
                {
                    Assert.Equal(250.0, mask.ValueAt(i, j));
                }
            }
        }
    }

    [Fact]
    public void ADiscFixesNodesWithinItsRadius()
    {
        var electrode = new CompiledElectrode
        {
            Name = "rod",
            Shape = ElectrodeShape.Disc,
            CentreX = 0.003,
            CentreY = -0.002,
            Radius = 0.0025,
            Potential = -100.0,
        };

        var solve = Field(electrode);
        var grid = GeometryBuilder.BuildGrid(solve);
        var mask = GeometryBuilder.BuildMask(solve, grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = grid.X(i) - 0.003;
                var dy = grid.Y(j) + 0.002;
                var inside = (dx * dx) + (dy * dy) <= 0.0025 * 0.0025;

                Assert.Equal(inside, mask.IsFixed(i, j));
            }
        }

        // A disc of radius five cells should cover roughly pi r squared nodes,
        // counted in cells of whatever shape this grid has.
        var expected = Math.PI * 0.0025 * 0.0025 / (grid.SpacingX * grid.SpacingY);
        Assert.InRange(mask.FixedCount, expected * 0.85, expected * 1.15);
    }

    [Fact]
    public void AnEdgeProfileInterpolatesAlongItsEdge()
    {
        var electrode = new CompiledElectrode
        {
            Name = "board",
            Shape = ElectrodeShape.EdgeProfile,
            Edge = GridEdge.Top,
            Profile = [(-0.01, 1000.0), (0.0, 0.0), (0.01, 0.0)],
        };

        var solve = Field(electrode);
        var grid = GeometryBuilder.BuildGrid(solve);
        var mask = GeometryBuilder.BuildMask(solve, grid);
        var top = grid.CountY - 1;

        for (var i = 0; i < grid.CountX; i++)
        {
            Assert.True(mask.IsFixed(i, top), $"node {i} on the top edge should be fixed");

            var x = grid.X(i);
            var expected = x <= 0.0 ? 1000.0 * (-x / 0.01) : 0.0;

            Assert.Equal(expected, mask.ValueAt(i, top), 1e-9);
        }

        // Only that edge.
        Assert.False(mask.IsFixed(grid.CountX / 2, 0));
    }

    [Fact]
    public void InteriorAndBoundaryFixedNodesAreCountedSeparately()
    {
        // The distinction the multigrid depth limit rests on: a boundary curve may
        // coarsen freely, an interior region may not.
        var boundary = Field(new CompiledElectrode
        {
            Name = "edge",
            Shape = ElectrodeShape.EdgeProfile,
            Edge = GridEdge.Bottom,
            Profile = [(-0.01, 0.0), (0.01, 0.0)],
        });

        var interior = Field(new CompiledElectrode
        {
            Name = "rod",
            Shape = ElectrodeShape.Disc,
            CentreX = 0.0,
            CentreY = 0.0,
            Radius = 0.002,
            Potential = 100.0,
        });

        var boundaryGrid = GeometryBuilder.BuildGrid(boundary);
        var interiorGrid = GeometryBuilder.BuildGrid(interior);

        var boundaryMask = GeometryBuilder.BuildMask(boundary, boundaryGrid);
        var interiorMask = GeometryBuilder.BuildMask(interior, interiorGrid);

        Assert.True(boundaryMask.FixedCount > 0);
        Assert.Equal(0, boundaryMask.InteriorFixedCount);
        Assert.True(interiorMask.InteriorFixedCount > 0);
    }

    [Fact]
    public void ABasisSolveCanRaiseOneElectrodeWithoutRestatingItsShape()
    {
        var a = new CompiledElectrode
        {
            Name = "a", Shape = ElectrodeShape.Rectangle,
            MinX = -0.009, MaxX = -0.008, MinY = -0.009, MaxY = 0.009, Potential = 500.0,
        };

        var b = new CompiledElectrode
        {
            Name = "b", Shape = ElectrodeShape.Rectangle,
            MinX = 0.008, MaxX = 0.009, MinY = -0.009, MaxY = 0.009, Potential = -500.0,
        };

        var solve = Field(a, b);
        var grid = GeometryBuilder.BuildGrid(solve);

        var basis = GeometryBuilder.BuildMask(solve, grid, e => e.Name == "a" ? 1.0 : 0.0);

        var insideA = (int)Math.Round((-0.0085 - grid.OriginX) / grid.SpacingX);
        var insideB = (int)Math.Round((0.0085 - grid.OriginX) / grid.SpacingX);
        var middle = grid.CountY / 2;

        Assert.Equal(1.0, basis.ValueAt(insideA, middle));
        Assert.Equal(0.0, basis.ValueAt(insideB, middle));
    }

    [Fact]
    public void ADirichletEdgeGroundsTheEdgeItselfRatherThanAGhostOutsideIt()
    {
        // Where the boundary is has to be the same at every level of a V-cycle,
        // and that is what forces this convention. Holding a ghost node one cell
        // outside the grid at zero is perfectly consistent on one grid, and it is
        // what the solver used to do; but the ghost is one cell out on the fine
        // grid, two on the next, four on the next, so each coarse level solves a
        // slightly larger domain than the one above it. The correction it computes
        // is then for a different problem. It diverged to 1e50 V.
        var solve = new CompiledSolvedField
        {
            MinX = -0.01, MinY = -0.01, MaxX = 0.01, MaxY = 0.01,
            CellSize = 0.0005,
            Tolerance = 1e-12,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "cap", Shape = ElectrodeShape.Rectangle,
                    MinX = -0.01, MaxX = -0.0095, MinY = -0.01, MaxY = 0.01, Potential = 1000.0,
                },
            ],
        };

        var grid = GeometryBuilder.BuildGrid(solve);
        var mask = GeometryBuilder.BuildMask(solve, grid);

        // The cap reaches the left edge and keeps it; the other three are grounded.
        Assert.True(mask.IsFixed(0, grid.CountY / 2));
        Assert.Equal(1000.0, mask.ValueAt(0, grid.CountY / 2));

        Assert.True(mask.IsFixed(grid.CountX - 1, grid.CountY / 2));
        Assert.Equal(0.0, mask.ValueAt(grid.CountX - 1, grid.CountY / 2));
        Assert.Equal(0.0, mask.ValueAt(grid.CountX / 2, 0));
        Assert.Equal(0.0, mask.ValueAt(grid.CountX / 2, grid.CountY - 1));

        var (_, report) = PoissonSolver2D.Solve(
            mask, solve.Tolerance, maximumCycles: 400, coarsen: c => GeometryBuilder.BuildMask(solve, c));

        Assert.True(report.Converged, $"the solve did not converge: {report}");

        // A working V-cycle, not Gauss-Seidel wearing one. Before the fix this
        // geometry could not coarsen at all and took 148 cycles at a factor of
        // 0.83; a ghost-node edge that was allowed to coarsen diverged outright.
        Assert.True(
            report.Cycles < 20,
            $"took {report.Cycles} cycles at a convergence factor of {report.ConvergenceFactor:F3}, which is "
            + "smoothing rather than multigrid");
    }

    [Fact]
    public void AReflectedGeometryIsSymmetricAboutItsPlane()
    {
        var solve = Field(new CompiledElectrode
        {
            Name = "cap", Shape = ElectrodeShape.Rectangle,
            MinX = -0.01, MaxX = -0.0095, MinY = -0.01, MaxY = 0.01, Potential = 1000.0,
        }) with { ReflectAboutX = 0.01, BoundaryIsDiscontinuous = false };

        var (field, report) = GeometryBuilder.Build(solve);
        Assert.True(report.Converged);

        // Reflection about x = 0.01: a point at 0.01 - d must match 0.01 + d.
        foreach (var offset in new[] { 0.002, 0.005, 0.008 })
        {
            var left = new Core.Geometry.Vec3(0.01 - offset, 0.0, 0.0);
            var right = new Core.Geometry.Vec3(0.01 + offset, 0.0, 0.0);

            Assert.Equal(field.PotentialAt(in left), field.PotentialAt(in right), 1e-9);

            // The field flips sign with the coordinate.
            Assert.Equal(-field.ElectricFieldAt(in left).X, field.ElectricFieldAt(in right).X, 1e-6);
        }
    }
}
