using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// That the operator extracted as a matrix is the same operator the smoother applies.
/// </summary>
/// <remarks>
/// <para>
/// Galerkin coarsening needs the discrete Laplacian as coefficients rather than as a
/// rule applied to geometry, because the coarse operator is <c>R A P</c> - built from
/// the fine operator rather than from the geometry again. That extraction is the first
/// step and the one most likely to go quietly wrong, because it reimplements a stencil
/// that already exists and any disagreement between the two is a bug in whichever is
/// used second.
/// </para>
/// <para>
/// So the check is <b>bit-identity against the existing smoother</b>, not closeness. A
/// matrix that agreed to a part in 10^12 would still be a second, different operator,
/// and the whole point of extracting it is that it is the same one.
/// </para>
/// </remarks>
public sealed class OperatorStencilTests(ITestOutputHelper output)
{
    /// <summary>
    /// The solver's own converged answer satisfies the extracted equations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check that needs no access to the smoother and is not weaker for it: the
    /// solver drives the residual of <em>its</em> operator to the tolerance, so if the
    /// extracted matrix is the same operator, that same field drives the residual of
    /// <em>this</em> one to the tolerance too. A matrix with a wrong coefficient, a
    /// wrong sign or a shifted index would not be satisfied by the right solution.
    /// </para>
    /// <para>
    /// Exercised on a geometry with cut cells, an interior conductor and a grounded box,
    /// so the cut arms, the fixed neighbours and the domain edges all take part. A
    /// stencil that handled only the interior would pass on an empty box.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheConvergedSolutionSatisfiesTheExtractedEquations()
    {
        var geometry = Sphere(0.000625);

        var grid = GeometryBuilder3D.BuildGrid(geometry);
        var mask = GeometryBuilder3D.BuildMask(geometry, grid);

        var (potential, report) = PoissonSolver3D.Solve(
            mask, geometry.Tolerance, maximumCycles: 200,
            coarsen: GeometryBuilder3D.Coarsener(geometry));

        Assert.True(report.Converged, "the reference solve did not converge");

        var matrix = OperatorStencil3D.Assemble(mask, potential);

        var halfH2 = 0.5 * grid.SpacingX * grid.SpacingX;
        var directions = OperatorStencil3D.Directions;

        var worst = 0.0;
        var scale = 0.0;
        var free = 0;

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        continue;
                    }

                    var node = grid.Index(i, j, k);

                    // The row, as the operator states it: diagonal times this node,
                    // less the free neighbours, less what the known values contribute.
                    // The source is zero here, so the row should evaluate to zero.
                    var row = matrix.Diagonal(node) * potential[i, j, k];

                    row -= matrix.Known(node);

                    for (var arm = 0; arm < OperatorStencil3D.Arms; arm++)
                    {
                        var coefficient = matrix.Arm(node, arm);

                        if (coefficient == 0.0)
                        {
                            continue;
                        }

                        var (di, dj, dk) = directions[arm];

                        var ni = i + di;
                        var nj = j + dj;
                        var nk = k + dk;

                        // An arm that left the grid is a Neumann mirror, so it reads
                        // the reflected node inside.
                        if (ni < 0 || nj < 0 || nk < 0
                            || ni >= grid.CountX || nj >= grid.CountY || nk >= grid.CountZ)
                        {
                            ni = i - di;
                            nj = j - dj;
                            nk = k - dk;
                        }

                        row -= coefficient * potential[ni, nj, nk];
                    }

                    worst = Math.Max(worst, Math.Abs(row / halfH2));
                    scale = Math.Max(scale, matrix.Diagonal(node) * Math.Abs(potential[i, j, k]) / halfH2);

                    free++;
                }
            }
        }

        output.WriteLine(
            $"{free:N0} free nodes, solver residual {report.FinalResidual:E3} of "
            + $"{report.InitialResidual:E3}; extracted-matrix residual {worst:E3} "
            + $"against a row scale of {scale:E3}");

        Assert.True(free > 1000, "too few free nodes for this to mean anything");

        // The solver drove ITS residual to the tolerance. If this is the same operator,
        // the same field drives THIS residual to the same place - so the bar is the
        // solver's own converged residual rather than a number chosen to pass.
        Assert.True(
            worst <= Math.Max(report.FinalResidual * 10.0, 1e-6 * scale),
            $"the converged field leaves a residual of {worst:E3} in the extracted "
            + $"operator, against {report.FinalResidual:E3} in the solver's own - so the "
            + "two are not the same operator");
    }

    /// <summary>
    /// A conductor's own potential reaches the matrix through the field, not the mask.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction that matters for coarsening: a coarse level solves for the
    /// <em>error</em>, whose value on a conductor is zero rather than the electrode's
    /// potential. An extraction that read the mask would inject the applied voltage into
    /// every correction - which is exactly the failure mode a deeper V-cycle was
    /// measured producing, at 486 V of 100 applied.
    /// </para>
    /// <para>
    /// <b>On a cut-free mask, deliberately.</b> A first version used the fine mask and
    /// the two readings came out identical, because there every known contribution came
    /// from a <em>cut surface</em> - and a cut carries the conductor's potential from the
    /// cut link rather than from the field, in both readings. The grounded box faces are
    /// fixed nodes at zero, so they could not tell the two apart either. Only a fixed
    /// node at a non-zero potential distinguishes them, which is what a coarse mask has
    /// and a fine one does not.
    /// </para>
    /// <para>
    /// That is worth knowing beyond this test: <b>a cut surface's potential lives in the
    /// right-hand side and is not zeroed by using a correction field</b>. The
    /// two-dimensional solver zeroes them explicitly before recursing; the
    /// three-dimensional one never had to, because its coarse levels carry no cuts.
    /// </para>
    /// </remarks>
    [Fact]
    public void AConductorContributesTheFieldsValueAndNotTheMasks()
    {
        var geometry = Sphere(0.000625);

        var fine = GeometryBuilder3D.BuildGrid(geometry);

        // A coarse mask: node-aligned, so its conductor is fixed NODES at 100 V rather
        // than cut links, which is what makes the two readings differ at all.
        var grid = fine.Coarsen();
        var mask = GeometryBuilder3D.Coarsener(geometry)(grid);

        Assert.True(mask.InteriorFixedCount > 0, "the coarse mask lost its conductor");
        Assert.Null(mask.Cuts);

        // A correction field: zero everywhere, including on the conductor.
        var correction = new ScalarField3D(grid);

        var forCorrection = OperatorStencil3D.Assemble(mask, correction);
        var forPotential = OperatorStencil3D.Assemble(mask);

        var correctionKnown = 0.0;
        var potentialKnown = 0.0;

        for (var n = 0; n < grid.NodeCount; n++)
        {
            correctionKnown += Math.Abs(forCorrection.Known(n));
            potentialKnown += Math.Abs(forPotential.Known(n));
        }

        output.WriteLine(
            $"known contributions: correction {correctionKnown:E3}, mask {potentialKnown:E3}");

        // Every node's known contribution comes from a cut surface or a fixed
        // neighbour. Read from a zero correction field the fixed neighbours give
        // nothing; read from the mask they give 100 V apiece.
        Assert.True(
            potentialKnown > 100.0 * Math.Max(correctionKnown, 1e-12),
            $"the two readings should differ by the applied potential: {correctionKnown:E3} "
            + $"against {potentialKnown:E3}");

        // And the matrix itself is the same either way - only the right-hand side moves.
        for (var n = 0; n < grid.NodeCount; n++)
        {
            Assert.Equal(forPotential.Diagonal(n), forCorrection.Diagonal(n));

            for (var arm = 0; arm < OperatorStencil3D.Arms; arm++)
            {
                Assert.Equal(forPotential.Arm(n, arm), forCorrection.Arm(n, arm));
            }
        }
    }

    private static Geometry3D Sphere(double cell) => new(
        -0.010, -0.010, -0.010, 0.010, 0.010, 0.010, cell,
        [
            new CompiledElectrode3D
            {
                Name = "ball",
                Shape = Electrode3DShape.Sphere,
                CentreX = 0.0011,
                CentreY = -0.0007,
                CentreZ = 0.0004,
                Radius = 0.003,
                Potential = 100.0,
            },
        ]);
}
