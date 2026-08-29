using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// That <c>R A P</c> is the operator it claims to be.
/// </summary>
/// <remarks>
/// <para>
/// The triple product is where a Galerkin implementation goes wrong, and it goes wrong
/// quietly: a coarse operator with a scale factor or a transposed index still converges,
/// to something else. So the checks here are all things a wrong product would fail even
/// though it still ran.
/// </para>
/// <para>
/// The sharpest is the first. Where there is no geometry to lose - no cuts, no interior
/// conductor - every one of the twenty-seven entries has a closed form, derived from the
/// tensor decomposition of the transfers rather than read off a run. Anything else is a
/// bug in the product, and nothing about the geometry can be blamed.
/// </para>
/// <para>
/// <b>What that closed form is not</b>: the rediscretised seven-point Laplacian. That
/// identity holds in one dimension and not in three, where the transfers are tensor
/// products and the off-axis entries are supposed to be there. A first version of this
/// test asserted the seven-point answer and failed on correct code, which is the right
/// way round for a test to be wrong.
/// </para>
/// </remarks>
public sealed class GalerkinOperatorTests(ITestOutputHelper output)
{
    /// <summary>
    /// On an empty box every one of the twenty-seven entries matches its closed form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The triple product is not the rediscretised seven-point operator, and expecting
    /// it to be is the first mistake to make here.</b> That identity holds in one
    /// dimension. In three the transfers are tensor products, so
    /// <c>R A P = sum over axes of (R_a A_a P_a) x (R_b P_b) x (R_c P_c)</c> - and
    /// <c>R_b P_b</c> is not the identity, it is <c>[1/8, 3/4, 1/8]</c>. The off-axis
    /// entries are supposed to be there.
    /// </para>
    /// <para>
    /// Which makes this a much sharper test than "the arms look right", because every
    /// entry has a closed form and none of them came from running the code:
    /// <c>R_a A_a P_a = (1/4)[-1, 2, -1]</c> in the fine operator's own units, and this
    /// solver carries a further factor of one half because its arm coefficient is
    /// <c>1/(f_west + f_east)</c> rather than one. So
    /// </para>
    /// <list type="bullet">
    /// <item><description>centre: <c>3 * (1/4 * 2) * (3/4)^2 * (1/2) = 27/64</c></description></item>
    /// <item><description>face: <c>(-9/64 + 3/64 + 3/64) * (1/2) = -3/128</c></description></item>
    /// <item><description>edge: <c>(-3/128 - 3/128 + 1/128) * (1/2) = -5/256</c></description></item>
    /// <item><description>corner: <c>(-3/256) * (1/2) = -3/512</c></description></item>
    /// </list>
    /// <para>
    /// And they sum to zero, as a Laplacian's row must:
    /// <c>216/512 - 72/512 - 120/512 - 24/512 = 0</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnAnEmptyBoxEveryEntryMatchesItsClosedForm()
    {
        var geometry = Empty(0.00125);

        var fineGrid = GeometryBuilder3D.BuildGrid(geometry);
        var fineMask = GeometryBuilder3D.BuildMask(geometry, fineGrid);

        Assert.Equal(0, fineMask.InteriorFixedCount);
        Assert.Null(fineMask.Cuts);

        var fine = OperatorStencil3D.Assemble(fineMask);

        var coarse = fineGrid.Coarsen();
        var galerkin = GalerkinOperator3D.Form(fine, fineMask, coarse);

        // Well inside, so no domain edge takes part.
        var node = coarse.Index(coarse.CountX / 2, coarse.CountY / 2, coarse.CountZ / 2);

        var sum = 0.0;

        for (var entry = 0; entry < GalerkinOperator3D.Entries; entry++)
        {
            var (di, dj, dk) = GalerkinOperator3D.Offset(entry);

            var steps = Math.Abs(di) + Math.Abs(dj) + Math.Abs(dk);

            var expected = steps switch
            {
                0 => 27.0 / 64.0,
                1 => -3.0 / 128.0,
                2 => -5.0 / 256.0,
                _ => -3.0 / 512.0,
            };

            var value = galerkin.Coefficient(node, entry);

            sum += value;

            output.WriteLine(
                $"({di,2},{dj,2},{dk,2}) {value,12:F9} expected {expected,12:F9}");

            Assert.Equal(expected, value, 1e-13);
        }

        output.WriteLine($"row sum {sum:E3}");

        Assert.Equal(0.0, sum, 1e-13);
    }

    /// <summary>
    /// The coarse operator annihilates a constant, as a Laplacian must.
    /// </summary>
    /// <remarks>
    /// A row sum of zero says the operator has no source term of its own. Restriction
    /// and prolongation both preserve constants, so <c>R A P</c> applied to a constant
    /// is <c>R</c> applied to <c>A</c> applied to a constant, which is zero - <b>except
    /// next to a conductor</b>, where the fine operator's row deliberately does not sum
    /// to zero because a known potential has been moved to the right-hand side. So this
    /// is asserted in the interior and not at the boundary, which is the honest
    /// statement rather than the convenient one.
    /// </remarks>
    [Fact]
    public void InteriorRowsSumToZero()
    {
        var geometry = Sphere(0.000625);

        var fineGrid = GeometryBuilder3D.BuildGrid(geometry);
        var fineMask = GeometryBuilder3D.BuildMask(geometry, fineGrid);

        var fine = OperatorStencil3D.Assemble(fineMask);

        var coarse = fineGrid.Coarsen();
        var galerkin = GalerkinOperator3D.Form(fine, fineMask, coarse);

        var worst = 0.0;
        var scale = 0.0;
        var counted = 0;

        for (var k = 2; k < coarse.CountZ - 2; k++)
        {
            for (var j = 2; j < coarse.CountY - 2; j++)
            {
                for (var i = 2; i < coarse.CountX - 2; i++)
                {
                    var node = coarse.Index(i, j, k);

                    if (galerkin.Diagonal(node) == 0.0 || Near(fineMask, fineGrid, i, j, k))
                    {
                        continue;
                    }

                    var sum = 0.0;
                    var magnitude = 0.0;

                    for (var entry = 0; entry < GalerkinOperator3D.Entries; entry++)
                    {
                        var value = galerkin.Coefficient(node, entry);

                        sum += value;
                        magnitude += Math.Abs(value);
                    }

                    worst = Math.Max(worst, Math.Abs(sum));
                    scale = Math.Max(scale, magnitude);
                    counted++;
                }
            }
        }

        output.WriteLine($"{counted:N0} interior coarse rows, worst sum {worst:E3} of {scale:E3}");

        Assert.True(counted > 500, "too few interior rows for this to mean anything");
        Assert.True(worst < 1e-12 * scale, $"a row summed to {worst:E3}");
    }

    /// <summary>Whether any fine node near a coarse one is fixed.</summary>
    /// <remarks>
    /// A coarse row reaches fine nodes up to three cells away through the product, so
    /// "interior" has to mean interior at that reach rather than at one cell.
    /// </remarks>
    private static bool Near(DirichletMask3D mask, Grid3D fine, int ci, int cj, int ck)
    {
        for (var dk = -3; dk <= 3; dk++)
        {
            for (var dj = -3; dj <= 3; dj++)
            {
                for (var di = -3; di <= 3; di++)
                {
                    var fi = (2 * ci) + di;
                    var fj = (2 * cj) + dj;
                    var fk = (2 * ck) + dk;

                    if (fi < 0 || fj < 0 || fk < 0
                        || fi >= fine.CountX || fj >= fine.CountY || fk >= fine.CountZ)
                    {
                        return true;
                    }

                    if (mask.IsFixed(fi, fj, fk))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static Geometry3D Empty(double cell) => new(
        -0.010, -0.010, -0.010, 0.010, 0.010, 0.010, cell, []);

    private static Geometry3D Sphere(double cell) => new(
        -0.010, -0.010, -0.010, 0.010, 0.010, 0.010, cell,
        [
            new CompiledElectrode3D
            {
                Name = "ball",
                Shape = Electrode3DShape.Sphere,
                CentreX = 0.0,
                CentreY = 0.0,
                CentreZ = 0.0,
                Radius = 0.003,
                Potential = 100.0,
            },
        ]);
}
