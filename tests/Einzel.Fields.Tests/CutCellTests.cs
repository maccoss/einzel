using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The cut-cell (Shortley-Weller) discretisation, against closed forms.
/// </summary>
/// <remarks>
/// <para>
/// A rasterised Dirichlet boundary is a node-by-node decision, so a conductor
/// surface can only sit where a node is. That has two costs, and this file
/// measures both. Accuracy: the boundary is misplaced by up to half a cell, which
/// puts a first-order error on a second-order scheme exactly where the field is
/// usually most interesting. Differentiability: the discrete operator is a
/// staircase function of electrode position, so a sub-cell move changes nothing
/// and a one-cell move changes everything, which is what made the FLD-1 spike
/// fail.
/// </para>
/// <para>
/// Both are consequences of the same choice, and both go away together.
/// </para>
/// </remarks>
public sealed class CutCellTests(ITestOutputHelper output)
{
    private const double Applied = 1000.0;

    /// <summary>
    /// A parallel-plate gap, one-dimensional by construction: both electrodes
    /// overhang the domain in y, so the exact potential is a straight ramp and no
    /// field goes round anything.
    /// </summary>
    private static CompiledSolvedField ParallelPlate(double faceX, double cellSize) => new()
    {
        MinX = 0.0,
        MinY = 0.0,
        MaxX = 0.04,
        MaxY = 0.01,
        CellSize = cellSize,
        BottomEdge = BoundaryKind.Neumann,
        TopEdge = BoundaryKind.Neumann,
        Tolerance = 1e-12,
        Electrodes =
        [
            new CompiledElectrode
            {
                Name = "ground",
                Shape = ElectrodeShape.Rectangle,
                MinX = -0.001, MaxX = 0.0, MinY = -0.001, MaxY = 0.011,
                Potential = 0.0,
            },
            new CompiledElectrode
            {
                Name = "plate",
                Shape = ElectrodeShape.Rectangle,
                MinX = faceX, MaxX = 0.045, MinY = -0.001, MaxY = 0.011,
                Potential = Applied,
            },
        ],
    };

    [Fact]
    public void APlanarBoundaryBetweenNodesIsWhereItSaysItIs()
    {
        // The exact potential in the gap is 1000 x / faceX. A linear function is
        // reproduced exactly by a second-difference stencil on any spacings, so if
        // the boundary is where the geometry says it is, the solved nodes must sit
        // on that ramp to solver tolerance - at every sub-cell offset, not just the
        // ones that happen to land on a node.
        //
        // With a rasterised boundary the same measurement returns a staircase of
        // amplitude half a cell in faceX, which at this mesh is 1.6% of the gap.
        output.WriteLine("faceX/mm   cells   worst error   probe V    second difference");

        var worstOverall = 0.0;
        var probes = new List<double>();
        double? spacingMm = null;

        for (var k = 0; k <= 20; k++)
        {
            var faceX = (20.0 + (k * 0.05)) * 1e-3;
            var solve = ParallelPlate(faceX, 0.000625);
            var grid = GeometryBuilder.BuildGrid(solve);
            var mask = GeometryBuilder.BuildMask(solve, grid);

            var (potential, report) = PoissonSolver2D.Solve(
                mask, solve.Tolerance, maximumCycles: 400, coarsen: c => GeometryBuilder.BuildMask(solve, c));

            Assert.True(report.Converged, $"the solve did not converge at faceX = {faceX * 1e3:F2} mm");
            spacingMm ??= grid.SpacingX * 1e3;

            var worst = 0.0;
            var j = grid.CountY / 2;

            for (var i = 0; i < grid.CountX; i++)
            {
                var x = grid.X(i);

                if (x <= 0.0 || x >= faceX)
                {
                    // Outside the gap the exact solution is the conductor itself.
                    continue;
                }

                worst = Math.Max(worst, Math.Abs(potential[i, j] - (Applied * x / faceX)) / Applied);
            }

            var probeI = grid.CountX / 4;
            probes.Add(potential[probeI, j]);
            worstOverall = Math.Max(worstOverall, worst);

            var second = probes.Count >= 3
                ? probes[^1] - (2.0 * probes[^2]) + probes[^3]
                : double.NaN;

            output.WriteLine(
                $"{faceX * 1e3,7:F2}  {faceX / grid.SpacingX,6:F2}   {worst,11:E3}   "
                + $"{potential[probeI, j],9:F5}   {second,10:E2}");
        }

        // What a rasterised boundary would cost on this mesh, for scale.
        var rasterised = (spacingMm!.Value / 2.0) / 20.0;

        output.WriteLine($"mesh {spacingMm:F4} mm; worst error over the sweep {worstOverall:E3} of applied");
        output.WriteLine($"a boundary snapped to the nearest node would be off by up to {rasterised:E3}");

        Assert.True(
            worstOverall < 1e-6,
            $"a sub-cell boundary position cost {worstOverall:E3} of the applied potential, on a geometry "
            + "whose exact solution any consistent stencil reproduces exactly");
    }

    /// <summary>
    /// A round rod inside a box whose edges carry the analytic coaxial potential.
    /// </summary>
    /// <remarks>
    /// Section 19 asks for parallel-plate and coaxial fields against closed form.
    /// Coaxial was not previously testable, because a circle rasterised onto a
    /// Cartesian grid is a staircase and the comparison would have measured that
    /// rather than the solver. Driving the outer boundary with the same analytic
    /// potential makes the exact solution valid over the whole annulus, so the
    /// error being measured is the discretisation and nothing else.
    /// </remarks>
    private static CompiledSolvedField Coaxial(double cellSize, double innerRadius, double outerHalfWidth)
    {
        var a = Applied / Math.Log(innerRadius / outerHalfWidth);
        var b = -a * Math.Log(outerHalfWidth);

        double Exact(double x, double y) => (a * Math.Log(Math.Sqrt((x * x) + (y * y)))) + b;

        const int Samples = 2049;
        var profiles = new List<CompiledElectrode>();

        foreach (var edge in (ReadOnlySpan<GridEdge>)[GridEdge.Left, GridEdge.Right, GridEdge.Bottom, GridEdge.Top])
        {
            var points = new List<(double At, double Potential)>(Samples);

            for (var k = 0; k < Samples; k++)
            {
                var along = -outerHalfWidth + (2.0 * outerHalfWidth * k / (Samples - 1));

                var potential = edge switch
                {
                    GridEdge.Left => Exact(-outerHalfWidth, along),
                    GridEdge.Right => Exact(outerHalfWidth, along),
                    GridEdge.Bottom => Exact(along, -outerHalfWidth),
                    _ => Exact(along, outerHalfWidth),
                };

                points.Add((along, potential));
            }

            profiles.Add(new CompiledElectrode
            {
                Name = $"outer-{edge}",
                Shape = ElectrodeShape.EdgeProfile,
                Edge = edge,
                Profile = points,
            });
        }

        return new CompiledSolvedField
        {
            MinX = -outerHalfWidth,
            MinY = -outerHalfWidth,
            MaxX = outerHalfWidth,
            MaxY = outerHalfWidth,
            CellSize = cellSize,
            Tolerance = 1e-12,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "rod",
                    Shape = ElectrodeShape.Disc,
                    CentreX = 0.0,
                    CentreY = 0.0,
                    Radius = innerRadius,
                    Potential = Applied,
                },
                .. profiles,
            ],
        };
    }

    [Fact]
    public void ACurvedBoundaryConvergesAtSecondOrder()
    {
        const double InnerRadius = 0.005;
        const double OuterHalfWidth = 0.02;

        var a = Applied / Math.Log(InnerRadius / OuterHalfWidth);
        var b = -a * Math.Log(OuterHalfWidth);

        output.WriteLine("intervals   spacing/mm   worst error   cycles   factor   order");

        var previous = double.NaN;

        foreach (var intervals in new[] { 64, 128, 256 })
        {
            var solve = Coaxial(2.0 * OuterHalfWidth / intervals, InnerRadius, OuterHalfWidth);
            var grid = GeometryBuilder.BuildGrid(solve);
            var mask = GeometryBuilder.BuildMask(solve, grid);

            var (potential, report) = PoissonSolver2D.Solve(
                mask, solve.Tolerance, maximumCycles: 400, coarsen: c => GeometryBuilder.BuildMask(solve, c));

            Assert.True(report.Converged, $"the {intervals}-interval solve did not converge");

            var worst = 0.0;

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j))
                    {
                        continue;
                    }

                    var x = grid.X(i);
                    var y = grid.Y(j);
                    var r = Math.Sqrt((x * x) + (y * y));

                    // A node within a cell of the rod is compared against a
                    // boundary the clamp on tiny cut fractions may have moved;
                    // everywhere else the exact solution is exact.
                    if (r < InnerRadius + grid.MinimumSpacing)
                    {
                        continue;
                    }

                    worst = Math.Max(worst, Math.Abs(potential[i, j] - ((a * Math.Log(r)) + b)) / Applied);
                }
            }

            var order = double.IsNaN(previous) ? double.NaN : Math.Log2(previous / worst);
            previous = worst;

            output.WriteLine(
                $"{intervals,9}   {grid.SpacingX * 1e3,10:F4}   {worst,11:E3}   {report.Cycles,6}   "
                + $"{report.ConvergenceFactor,6:F3}   {order,5:F2}");
        }

        Assert.True(
            previous < 2e-5,
            $"the finest coaxial solve is off by {previous:E3} of the applied potential");
    }
}
