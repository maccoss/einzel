using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// A closed-form harmonic potential, used as the manufactured solution the solver
/// is measured against.
/// </summary>
/// <remarks>
/// <para>
/// Phi = A sinh(k y) sin(k x) satisfies Laplace's equation exactly: the two second
/// derivatives are equal and opposite everywhere. Imposing it on the boundary and
/// solving the interior gives an error that is entirely the discretisation's, so
/// the observed convergence order can be compared against the five-point
/// stencil's nominal order of two — which is what spec section 19 asks for when it
/// requires "every physics test at two mesh densities and two tolerances,
/// asserting observed convergence order against nominal".
/// </para>
/// <para>
/// Deliberately not a polynomial. The five-point Laplacian is exact for anything
/// up to a quadratic, so a quadratic reference would report machine precision at
/// every refinement and reveal nothing about the order.
/// </para>
/// </remarks>
internal sealed class HarmonicReference(double amplitude, double wavenumber) : IElectrostaticField
{
    public double Amplitude { get; } = amplitude;

    public double Wavenumber { get; } = wavenumber;

    public double Potential(double x, double y) =>
        Amplitude * Math.Sinh(Wavenumber * y) * Math.Sin(Wavenumber * x);

    public double DPotentialDx(double x, double y) =>
        Amplitude * Wavenumber * Math.Sinh(Wavenumber * y) * Math.Cos(Wavenumber * x);

    public double DPotentialDy(double x, double y) =>
        Amplitude * Wavenumber * Math.Cosh(Wavenumber * y) * Math.Sin(Wavenumber * x);

    public Vec3 ElectricFieldAt(in Vec3 position) =>
        new(-DPotentialDx(position.X, position.Y), -DPotentialDy(position.X, position.Y), 0.0);

    public double PotentialAt(in Vec3 position) => Potential(position.X, position.Y);

    /// <summary>Solves the interior of a box with this potential imposed on the boundary.</summary>
    public (ScalarField2D Potential, SolveReport Report) SolveOn(Grid2D grid, double tolerance = 1e-12)
    {
        var mask = new DirichletMask(grid);
        mask.FixDirichletEdges(Potential);
        return PoissonSolver2D.Solve(mask, tolerance, maximumCycles: 400);
    }

    /// <summary>
    /// The exact potential sampled onto grid nodes, with no solve involved.
    /// </summary>
    /// <remarks>
    /// Isolating the interpolant is the only way to measure what ACC-3 actually
    /// budgets. A solved field carries its own O(h^2) discretisation error, and on
    /// a coarse grid that error is larger than the interpolation error it is
    /// supposed to be a backdrop for — so a comparison against a solved field
    /// measures the solver and reports it as the interpolant's.
    /// </remarks>
    public ScalarField2D SampleOn(Grid2D grid)
    {
        var field = new ScalarField2D(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                field[i, j] = Potential(grid.X(i), grid.Y(j));
            }
        }

        return field;
    }

    /// <summary>Largest absolute difference between a solved field and the closed form.</summary>
    public double MaximumError(ScalarField2D solved)
    {
        var grid = solved.Grid;
        var worst = 0.0;

        for (var j = 1; j < grid.CountY - 1; j++)
        {
            for (var i = 1; i < grid.CountX - 1; i++)
            {
                worst = Math.Max(worst, Math.Abs(solved[i, j] - Potential(grid.X(i), grid.Y(j))));
            }
        }

        return worst;
    }
}
