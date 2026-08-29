using Einzel.Core.Geometry;

namespace Einzel.Fields.Solved;

/// <summary>How a macroparticle's charge is spread over the nodes near it.</summary>
/// <remarks>
/// <para>
/// The choice is not only about accuracy. Both shapes conserve charge exactly and
/// both cancel the self-force when used for the deposit and the gather alike; what
/// separates them is the <em>smoothness of the force they produce</em>, and that is
/// paid for in integrator steps rather than in error.
/// </para>
/// </remarks>
public enum CloudShape
{
    /// <summary>
    /// Cloud-in-cell: two nodes an axis, weights linear in the offset.
    /// </summary>
    /// <remarks>
    /// The force is continuous and its derivative is not - it jumps at every cell
    /// face. An embedded Runge-Kutta estimator reads those kinks as error and refuses
    /// to stride, so a packet flown through a linear gather takes steps in proportion
    /// to the number of faces it crosses: measured at 274, 383 and 656 steps on 16,
    /// 32 and 64 nodes for a flight the direct sum did in 25.
    /// </remarks>
    Linear,

    /// <summary>
    /// Triangular-shaped cloud: three nodes an axis, weights quadratic in the offset.
    /// </summary>
    /// <remarks>
    /// The weights are a quadratic B-spline, so the interpolated field is
    /// continuously differentiable and the step controller sees no kinks. It costs
    /// twenty-seven nodes a particle instead of eight, which is cheap against what
    /// the kinks cost.
    /// </remarks>
    Quadratic,
}

/// <summary>
/// Moves charge onto a grid and a field back off it, with the same weights both ways.
/// </summary>
/// <remarks>
/// <para>
/// The particle half of the approximate space-charge method SC-1 asks for. The direct
/// pairwise sum, which is the reference this is validated against, costs
/// <c>O(N^2)</c>; particle-in-cell costs one solve plus <c>O(N)</c>, which is what
/// makes 10^4 macroparticles affordable when 10^3 already takes hours.
/// </para>
/// <para>
/// <strong>The same weights on the way out as on the way in, and that is not a
/// convenience.</strong> A particle contributes charge to the eight nodes around it in
/// proportion to how close it is to each; gathering the field back with those same
/// proportions makes the force a particle exerts on <em>itself</em> exactly cancel,
/// because the weight with which it wrote to a node is the weight with which it reads
/// from it. Use a different interpolation on the gather - a tricubic, say, which is
/// more accurate for a smooth field - and every particle feels its own charge, the
/// self-force is nonzero, and a packet heats up out of nothing at all. Momentum is
/// conserved by the symmetry rather than by an accounting step.
/// </para>
/// <para>
/// Trilinear rather than higher order for the same reason. ACC-3 forbids trilinear
/// interpolation on a trajectory path, and this is not one: it is the interpolation of
/// a <em>self-consistent</em> field whose accuracy is bounded by the deposit anyway,
/// and where the symmetry between deposit and gather buys more than the extra order
/// would. The applied field an ion flies through is still tricubic.
/// </para>
/// <para>
/// A particle outside the grid is <strong>counted and reported</strong> rather than
/// clamped or dropped. Charge that left the box is charge the solve does not have, and
/// a packet that has drifted off its own grid produces a field that is quietly too
/// weak - which looks exactly like a packet that is more dilute than it is.
/// </para>
/// </remarks>
public static class CloudInCell
{
    /// <summary>The permittivity of free space, in farads per metre.</summary>
    public const double VacuumPermittivitySi = 8.8541878188e-12;

    /// <summary>What a deposit did with the charge it was given.</summary>
    /// <param name="Source">
    /// The right-hand side of <c>grad^2 phi = source</c>, ready for the solver: the
    /// deposited charge density negated and divided by the permittivity.
    /// </param>
    /// <param name="DepositedCoulombs">How much charge landed on the grid.</param>
    /// <param name="OutsideCoulombs">
    /// How much fell outside it. Charge the solve does not have, and the reason a
    /// field can be quietly too weak.
    /// </param>
    public sealed record Deposit(
        ScalarField3D Source, double DepositedCoulombs, double OutsideCoulombs)
    {
        /// <summary>The fraction of the charge that landed outside the grid.</summary>
        public double FractionOutside
        {
            get
            {
                var total = DepositedCoulombs + OutsideCoulombs;

                return total == 0.0 ? 0.0 : OutsideCoulombs / total;
            }
        }
    }

    /// <summary>
    /// Spreads point charges onto the nodes around them and returns the source term.
    /// </summary>
    /// <param name="grid">The grid to deposit onto.</param>
    /// <param name="positions">Where the macroparticles are, in metres.</param>
    /// <param name="chargeCoulombs">
    /// What each carries. A macroparticle standing for many real ions carries all of
    /// their charge, which is what makes a thousand trajectories describe a packet of
    /// a million.
    /// </param>
    /// <param name="shape">
    /// How each macroparticle's charge is spread. The gather must use the same shape
    /// or a particle feels its own charge.
    /// </param>
    /// <returns>The source term and the charge accounting.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The counts do not match.</exception>
    public static Deposit Charge(
        Grid3D grid,
        IReadOnlyList<Vec3> positions,
        IReadOnlyList<double> chargeCoulombs,
        CloudShape shape = CloudShape.Linear)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(chargeCoulombs);

        if (positions.Count != chargeCoulombs.Count)
        {
            throw new ArgumentException(
                $"{positions.Count} position(s) against {chargeCoulombs.Count} charge(s)",
                nameof(chargeCoulombs));
        }

        var density = new ScalarField3D(grid);
        var cellVolume = grid.SpacingX * grid.SpacingY * grid.SpacingZ;

        var deposited = 0.0;
        var outside = 0.0;

        Span<int> nx = stackalloc int[3];
        Span<int> ny = stackalloc int[3];
        Span<int> nz = stackalloc int[3];
        Span<double> wx = stackalloc double[3];
        Span<double> wy = stackalloc double[3];
        Span<double> wz = stackalloc double[3];

        for (var k = 0; k < positions.Count; k++)
        {
            var charge = chargeCoulombs[k];

            if (!Inside(grid, positions[k]))
            {
                outside += Math.Abs(charge);
                continue;
            }

            deposited += Math.Abs(charge);

            var n = Axis(positions[k].X, grid.OriginX, grid.SpacingX, grid.CountX, shape, nx, wx);

            Axis(positions[k].Y, grid.OriginY, grid.SpacingY, grid.CountY, shape, ny, wy);
            Axis(positions[k].Z, grid.OriginZ, grid.SpacingZ, grid.CountZ, shape, nz, wz);

            // The weights on each axis sum to exactly one whatever the offset, so the
            // charge is conserved by construction rather than by normalising
            // afterwards - which would pass the same test while hiding a weighting
            // error rather than preventing one.
            for (var c = 0; c < n; c++)
            {
                for (var b = 0; b < n; b++)
                {
                    for (var a = 0; a < n; a++)
                    {
                        density[nx[a], ny[b], nz[c]] +=
                            charge * wx[a] * wy[b] * wz[c] / cellVolume;
                    }
                }
            }
        }

        // grad^2 phi = -rho / epsilon0, which is the convention the solver's residual
        // already fixes.
        for (var l = 0; l < grid.CountZ; l++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    density[i, j, l] = -density[i, j, l] / VacuumPermittivitySi;
                }
            }
        }

        return new Deposit(density, deposited, outside);
    }

    /// <summary>
    /// Reads the field back at a point, with the weights the deposit used.
    /// </summary>
    /// <param name="potential">The solved potential.</param>
    /// <param name="at">Where to sample, in metres.</param>
    /// <param name="shape">
    /// The shape the deposit used. A different one here makes a particle feel its own
    /// charge, and the packet expands for a reason nobody put in.
    /// </param>
    /// <returns>The field vector, in volts per metre, and zero outside the grid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="potential"/> is null.</exception>
    /// <remarks>
    /// The gradient is taken by central differences on the nodes and then interpolated
    /// trilinearly, rather than by differentiating an interpolant. That keeps the
    /// gather symmetric with the deposit: the field a particle reads is the weighted
    /// mean of the eight nodal fields it wrote charge to, so its own contribution to
    /// each of them is read back with the weight it was written with, and the
    /// self-force cancels.
    /// </remarks>
    public static Vec3 Field(
        ScalarField3D potential, in Vec3 at, CloudShape shape = CloudShape.Linear)
    {
        ArgumentNullException.ThrowIfNull(potential);

        var grid = potential.Grid;

        if (!Inside(grid, at))
        {
            return Vec3.Zero;
        }

        Span<int> nx = stackalloc int[3];
        Span<int> ny = stackalloc int[3];
        Span<int> nz = stackalloc int[3];
        Span<double> wx = stackalloc double[3];
        Span<double> wy = stackalloc double[3];
        Span<double> wz = stackalloc double[3];

        var n = Axis(at.X, grid.OriginX, grid.SpacingX, grid.CountX, shape, nx, wx);

        Axis(at.Y, grid.OriginY, grid.SpacingY, grid.CountY, shape, ny, wy);
        Axis(at.Z, grid.OriginZ, grid.SpacingZ, grid.CountZ, shape, nz, wz);

        var total = Vec3.Zero;

        for (var c = 0; c < n; c++)
        {
            for (var b = 0; b < n; b++)
            {
                for (var a = 0; a < n; a++)
                {
                    total += NodalField(potential, nx[a], ny[b], nz[c])
                        * (wx[a] * wy[b] * wz[c]);
                }
            }
        }

        return total;
    }

    /// <summary>The field at one node, by central differences where they exist.</summary>
    private static Vec3 NodalField(ScalarField3D potential, int i, int j, int l)
    {
        var grid = potential.Grid;

        return new Vec3(
            -Difference(potential, i, j, l, 1, 0, 0, grid.CountX, grid.SpacingX),
            -Difference(potential, i, j, l, 0, 1, 0, grid.CountY, grid.SpacingY),
            -Difference(potential, i, j, l, 0, 0, 1, grid.CountZ, grid.SpacingZ));
    }

    private static double Difference(
        ScalarField3D potential, int i, int j, int l, int di, int dj, int dl, int count, double h)
    {
        var index = (di * i) + (dj * j) + (dl * l);

        // One-sided at the faces. A packet whose own grid has it against a face is
        // already reporting charge outside, so the accuracy there is not what limits
        // the answer.
        if (index == 0)
        {
            return (potential[i + di, j + dj, l + dl] - potential[i, j, l]) / h;
        }

        if (index == count - 1)
        {
            return (potential[i, j, l] - potential[i - di, j - dj, l - dl]) / h;
        }

        return (potential[i + di, j + dj, l + dl] - potential[i - di, j - dj, l - dl]) / (2.0 * h);
    }

    /// <summary>Whether a point lies within the grid at all.</summary>
    private static bool Inside(Grid3D grid, in Vec3 at)
    {
        var x = (at.X - grid.OriginX) / grid.SpacingX;
        var y = (at.Y - grid.OriginY) / grid.SpacingY;
        var z = (at.Z - grid.OriginZ) / grid.SpacingZ;

        return x >= 0.0 && y >= 0.0 && z >= 0.0
            && x <= grid.CountX - 1 && y <= grid.CountY - 1 && z <= grid.CountZ - 1;
    }

    /// <summary>The nodes one axis touches, and what each is weighted by.</summary>
    /// <remarks>
    /// <para>
    /// Linear gives two nodes and weights <c>1 - f</c> and <c>f</c> about the cell's
    /// lower node. Quadratic gives three about the <em>nearest</em> node, weighted by
    /// the quadratic B-spline <c>(1/2)(1/2 - u)^2</c>, <c>3/4 - u^2</c>,
    /// <c>(1/2)(1/2 + u)^2</c>.
    /// </para>
    /// <para>
    /// Both sum to exactly one for any offset - the quadratic identity holds for
    /// <em>any</em> u, not only inside half a cell, which is what lets the index be
    /// clamped at the faces without charge being lost. A particle that close to the
    /// boundary of its own box is already a modelling problem the padding exists to
    /// prevent, and <c>FractionOutside</c> reports the case where it has left.
    /// </para>
    /// </remarks>
    private static int Axis(
        double coordinate, double origin, double spacing, int count,
        CloudShape shape, Span<int> nodes, Span<double> weights)
    {
        var x = (coordinate - origin) / spacing;

        if (shape == CloudShape.Linear)
        {
            var i = Math.Clamp((int)x, 0, count - 2);
            var f = x - i;

            nodes[0] = i;
            nodes[1] = i + 1;
            weights[0] = 1.0 - f;
            weights[1] = f;

            return 2;
        }

        var centre = Math.Clamp((int)Math.Round(x), 1, count - 2);

        // Clamped as well as the index, and the two clamps are not the same thing. The
        // index clamp keeps the three-node stencil on the grid; without this the offset
        // it implies can then exceed half a cell, and the middle weight 0.75 - u^2 goes
        // NEGATIVE - at x = 0 the weights are 1.125, -0.25, 0.125. They still sum to
        // one, so charge is conserved and nothing in a conservation test notices, but a
        // positive macroparticle depositing a negative density is not a density.
        //
        // At the limit the weights are 0.5, 0.5, 0 - the quadratic shape degrading into
        // the linear one exactly where the third node would have left the grid, which
        // is the right thing for it to do. What it gives up is stated: within the
        // outermost half cell the charge is placed as though the particle were on the
        // half-cell boundary.
        var u = Math.Clamp(x - centre, -0.5, 0.5);

        nodes[0] = centre - 1;
        nodes[1] = centre;
        nodes[2] = centre + 1;

        weights[0] = 0.5 * (0.5 - u) * (0.5 - u);
        weights[1] = 0.75 - (u * u);
        weights[2] = 0.5 * (0.5 + u) * (0.5 + u);

        return 3;
    }
}
