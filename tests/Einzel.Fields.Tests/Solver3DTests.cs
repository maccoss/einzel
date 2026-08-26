using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The three-dimensional Poisson solver, against closed forms.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of check, and the first is the unusual one. The seven-point Laplacian
/// is <em>exact</em> for a quadratic - the truncation error starts at the fourth
/// derivative, which a quadratic does not have - so a harmonic quadratic imposed on
/// the faces must be reproduced in the interior to solver tolerance rather than to
/// order h squared. That makes it a test with no tolerance to argue about: any
/// error at all is a wrong operator, a wrong boundary condition, or a wrong
/// transfer between levels.
/// </para>
/// <para>
/// The second is the ordinary one: a harmonic function that is <em>not</em>
/// polynomial, refined, to see the error fall by four each time.
/// </para>
/// </remarks>
public sealed class Solver3DTests(ITestOutputHelper output)
{
    /// <summary>x^2 + y^2 - 2z^2, which has zero Laplacian.</summary>
    private static double Quadratic(double x, double y, double z) =>
        (x * x) + (y * y) - (2.0 * z * z);

    /// <summary>sin(ax) sin(by) sinh(cz) with c^2 = a^2 + b^2, which also has zero Laplacian.</summary>
    private static double Wave(double x, double y, double z)
    {
        const double A = 60.0;
        const double B = 80.0;

        var c = Math.Sqrt((A * A) + (B * B));

        return Math.Sin(A * x) * Math.Sin(B * y) * Math.Sinh(c * z);
    }

    /// <summary>A box with a harmonic function imposed on all six faces.</summary>
    private static DirichletMask3D Faces(Grid3D grid, Func<double, double, double, double> exact)
    {
        var mask = new DirichletMask3D(grid);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    var onFace = i == 0 || j == 0 || k == 0
                        || i == grid.CountX - 1 || j == grid.CountY - 1 || k == grid.CountZ - 1;

                    if (onFace)
                    {
                        mask.Fix(i, j, k, exact(grid.X(i), grid.Y(j), grid.Z(k)));
                    }
                }
            }
        }

        return mask;
    }

    private static (double Worst, SolveReport Report) SolveAgainst(
        int cells, Func<double, double, double, double> exact)
    {
        var grid = Grid3D.OverBox(-0.01, -0.01, -0.01, 0.01, 0.01, 0.01, 0.02 / cells);
        var mask = Faces(grid, exact);

        var (potential, report) = PoissonSolver3D.Solve(mask, tolerance: 1e-13, maximumCycles: 200);

        var worst = 0.0;

        for (var k = 1; k < grid.CountZ - 1; k++)
        {
            for (var j = 1; j < grid.CountY - 1; j++)
            {
                for (var i = 1; i < grid.CountX - 1; i++)
                {
                    worst = Math.Max(
                        worst, Math.Abs(potential[i, j, k] - exact(grid.X(i), grid.Y(j), grid.Z(k))));
                }
            }
        }

        return (worst, report);
    }

    [Fact]
    public void AHarmonicQuadraticIsReproducedExactly()
    {
        // The seven-point Laplacian is exact for a quadratic, so this is not an
        // approximation converging - it is an identity, and the only error is
        // round-off and whatever residual the iteration was stopped at. Nothing
        // about the operator, the faces or the multigrid transfers can be wrong and
        // still pass it.
        var (worst, report) = SolveAgainst(32, Quadratic);

        var scale = 2.0 * 0.01 * 0.01;

        output.WriteLine($"{report.Cycles} cycles at factor {report.ConvergenceFactor:F4}");
        output.WriteLine($"worst interior error {worst:E3} V");
        output.WriteLine($"against a solution of order {scale:E3} V, so {worst / scale:E3} relative");

        Assert.True(report.Converged, "the solve did not converge");
        Assert.True(worst / scale < 1e-9, $"a harmonic quadratic came out {worst / scale:E3} wrong");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void TheCycleCountDoesNotGrowWithTheMesh(int cells)
    {
        // What multigrid is for. A single-grid smoother needs cycles going as the
        // square of the node count per side; a working V-cycle needs about the same
        // number at every mesh, and that flatness is the property to test rather
        // than any particular count.
        var (_, report) = SolveAgainst(cells, Wave);

        output.WriteLine(
            $"{cells,3} cells   {report.Cycles,3} cycles   factor {report.ConvergenceFactor:F4}");

        Assert.True(report.Converged);
        Assert.True(report.Cycles < 30, $"{report.Cycles} cycles at {cells} cells is not multigrid behaviour");
    }

    [Fact]
    public void ANonPolynomialHarmonicConvergesAtSecondOrder()
    {
        // The ordinary check, on a function the stencil is not exact for. Order is
        // what says the operator is the right one: a wrong stencil can be accurate
        // at one mesh by coincidence and will not quarter its error twice running.
        output.WriteLine("cells    worst error      observed order");

        var errors = new List<double>();

        foreach (var cells in new[] { 16, 32, 64 })
        {
            var (worst, _) = SolveAgainst(cells, Wave);
            errors.Add(worst);

            var order = errors.Count > 1 ? Math.Log2(errors[^2] / worst) : double.NaN;

            output.WriteLine(
                $"{cells,5}    {worst,11:E4}    {(double.IsNaN(order) ? string.Empty : order.ToString("F2")),16}");
        }

        for (var k = 1; k < errors.Count; k++)
        {
            var order = Math.Log2(errors[k - 1] / errors[k]);

            Assert.True(order is > 1.7 and < 2.3, $"observed order {order:F2}, not two");
        }
    }

    [Fact]
    public void ANeumannFaceIsAMirror()
    {
        // A face declared Neumann reflects, so a solve on half a symmetric problem
        // must reproduce the half it did not solve. Checked against the same
        // quadratic with the z-minimum face made a mirror at z = 0, where the
        // quadratic is already flat in z.
        var full = Grid3D.OverBox(-0.01, -0.01, -0.01, 0.01, 0.01, 0.01, 0.02 / 32.0);
        var half = Grid3D.OverBox(-0.01, -0.01, 0.0, 0.01, 0.01, 0.01, 0.02 / 32.0);

        var (whole, _) = PoissonSolver3D.Solve(Faces(full, Quadratic), tolerance: 1e-13);

        var halfMask = Faces(half, Quadratic);
        halfMask.LowerZ = EdgeCondition.Neumann;

        // The mirror face must not also be pinned, or it is Dirichlet wearing a
        // label. Rebuilt without it.
        var mirrored = new DirichletMask3D(half) { LowerZ = EdgeCondition.Neumann };

        for (var k = 0; k < half.CountZ; k++)
        {
            for (var j = 0; j < half.CountY; j++)
            {
                for (var i = 0; i < half.CountX; i++)
                {
                    var onFace = i == 0 || j == 0
                        || i == half.CountX - 1 || j == half.CountY - 1 || k == half.CountZ - 1;

                    if (onFace)
                    {
                        mirrored.Fix(i, j, k, Quadratic(half.X(i), half.Y(j), half.Z(k)));
                    }
                }
            }
        }

        var (halved, report) = PoissonSolver3D.Solve(mirrored, tolerance: 1e-13);

        output.WriteLine($"{report.Cycles} cycles on the mirrored half");

        var worst = 0.0;

        for (var k = 0; k < half.CountZ; k++)
        {
            for (var j = 1; j < half.CountY - 1; j++)
            {
                for (var i = 1; i < half.CountX - 1; i++)
                {
                    // The full solve's node at the same physical place. z = 0 is
                    // the midplane, node 16 of 32 intervals.
                    var reference = whole[i, j, k + 16];

                    worst = Math.Max(worst, Math.Abs(halved[i, j, k] - reference));
                }
            }
        }

        output.WriteLine($"half against whole, worst difference {worst:E3} V");

        Assert.True(worst < 1e-9, $"the mirrored half differs from the full solve by {worst:E3} V");
    }
}
