using Einzel.Core.Geometry;

namespace Einzel.Fields.Solved;

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
    /// <returns>The source term and the charge accounting.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The counts do not match.</exception>
    public static Deposit Charge(
        Grid3D grid, IReadOnlyList<Vec3> positions, IReadOnlyList<double> chargeCoulombs)
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

        for (var k = 0; k < positions.Count; k++)
        {
            var charge = chargeCoulombs[k];

            if (!Weights(grid, positions[k], out var i, out var j, out var l,
                    out var fx, out var fy, out var fz))
            {
                outside += Math.Abs(charge);
                continue;
            }

            deposited += Math.Abs(charge);

            // Eight nodes, weights summing to exactly one, so the charge is conserved
            // by construction rather than by normalising afterwards.
            for (var dz = 0; dz <= 1; dz++)
            {
                var wz = dz == 0 ? 1.0 - fz : fz;

                for (var dy = 0; dy <= 1; dy++)
                {
                    var wy = dy == 0 ? 1.0 - fy : fy;

                    for (var dx = 0; dx <= 1; dx++)
                    {
                        var wx = dx == 0 ? 1.0 - fx : fx;

                        density[i + dx, j + dy, l + dz] += charge * wx * wy * wz / cellVolume;
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
    public static Vec3 Field(ScalarField3D potential, in Vec3 at)
    {
        ArgumentNullException.ThrowIfNull(potential);

        var grid = potential.Grid;

        if (!Weights(grid, at, out var i, out var j, out var l, out var fx, out var fy, out var fz))
        {
            return Vec3.Zero;
        }

        var total = Vec3.Zero;

        for (var dz = 0; dz <= 1; dz++)
        {
            var wz = dz == 0 ? 1.0 - fz : fz;

            for (var dy = 0; dy <= 1; dy++)
            {
                var wy = dy == 0 ? 1.0 - fy : fy;

                for (var dx = 0; dx <= 1; dx++)
                {
                    var wx = dx == 0 ? 1.0 - fx : fx;

                    total += NodalField(potential, i + dx, j + dy, l + dz) * (wx * wy * wz);
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

    /// <summary>The lower node of the cell a point is in, and its fractional offsets.</summary>
    private static bool Weights(
        Grid3D grid, in Vec3 at,
        out int i, out int j, out int l,
        out double fx, out double fy, out double fz)
    {
        i = j = l = 0;
        fx = fy = fz = 0.0;

        var x = (at.X - grid.OriginX) / grid.SpacingX;
        var y = (at.Y - grid.OriginY) / grid.SpacingY;
        var z = (at.Z - grid.OriginZ) / grid.SpacingZ;

        if (x < 0.0 || y < 0.0 || z < 0.0
            || x > grid.CountX - 1 || y > grid.CountY - 1 || z > grid.CountZ - 1)
        {
            return false;
        }

        i = Math.Min((int)x, grid.CountX - 2);
        j = Math.Min((int)y, grid.CountY - 2);
        l = Math.Min((int)z, grid.CountZ - 2);

        fx = x - i;
        fy = y - j;
        fz = z - l;

        return true;
    }
}
