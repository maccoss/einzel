namespace Einzel.Transport.Diffusion;

/// <summary>How the density is advanced in time.</summary>
public enum StepScheme
{
    /// <summary>Forward Euler, bounded by diffusion and Courant limits.</summary>
    Explicit,

    /// <summary>Backward Euler, solved by Gauss-Seidel, with no stability limit.</summary>
    Implicit,
}

/// <summary>What one step did, beyond moving the density.</summary>
/// <param name="Collected">Ions that reached a collecting edge.</param>
/// <param name="Absorbed">Ions lost, by the name of the surface that took them.</param>
/// <param name="Sweeps">Gauss-Seidel sweeps taken, zero on the explicit path.</param>
/// <param name="SweepChange">
/// The largest change the last sweep made to any cell, relative to the largest
/// density.
/// </param>
/// <remarks>
/// <b>A change rather than a residual, named as one.</b> The stopping criterion is how
/// much the last sweep moved the answer, not the norm of <c>b - Ax</c>. For a monotone
/// iteration on an M-matrix - which this is - the two are proportional and the change
/// bounds the remaining error by a factor of one over one minus the convergence rate;
/// the true residual would cost an extra pass over the grid. Calling it a residual
/// would invite a reader to assume the stronger quantity.
/// </remarks>
public readonly record struct StepReport(
    double Collected,
    IReadOnlyList<(string Where, double Ions)> Absorbed,
    int Sweeps,
    double SweepChange);

/// <summary>
/// Advances a density one step, explicitly or implicitly.
/// </summary>
/// <remarks>
/// <para>
/// Both schemes use the same assembled <see cref="FaceCoefficients"/>, which is what
/// makes them comparable: a disagreement between them is the time discretisation and
/// nothing else, because the space discretisation is not merely equivalent but the
/// same arrays.
/// </para>
/// <para>
/// <b>Why the implicit path exists.</b> The explicit step is bounded by the faster of
/// diffusion and Courant, and in a driven structure the ponderomotive well's gradient
/// at an electrode edge makes the Courant bound tiny: on the shipped funnel at 2 mbar
/// it is 1.067 ns against a diffusion limit of 5.2 us, a factor of 4,900, so 900 us of
/// physical time is about 843,000 steps. The bound is set by cells at the edge of a
/// conductor, where the well is steepest and the density is almost zero - the step is
/// governed by a region where nothing is happening.
/// </para>
/// <para>
/// <b>Positivity survives a partial solve, which is what makes this usable.</b> The
/// backward-Euler update solved by Gauss-Seidel is
/// <c>n' = (n + dt sum b n'_neighbour) / (1 + dt sum a)</c>, and every term in it is
/// non-negative. So each sweep is a non-negative combination of non-negative numbers
/// and the iterate is a valid density however far from converged it is. A scheme that
/// went negative on the way would be unusable however stable, because a negative
/// density is a quantity that has stopped meaning anything.
/// </para>
/// <para>
/// <b>What convergence costs is conservation, not positivity</b>, and that is reported
/// rather than assumed: the ledger is closed against the density actually reached, so
/// an unconverged sweep shows up as a reported change rather than as ions that quietly went
/// missing.
/// </para>
/// </remarks>
public static class DensityStepper
{
    /// <summary>Gauss-Seidel sweeps before giving up on a step.</summary>
    /// <remarks>
    /// Reached only where the step is enormously past the explicit limit. It is a
    /// bound rather than a target: the sweep change is reported either way, so a step that
    /// used all of them is visible rather than silently worse.
    /// </remarks>
    public const int MaximumSweeps = 200;

    /// <summary>Advances the density by one step.</summary>
    /// <param name="density">The density now.</param>
    /// <param name="next">Where the new density is written.</param>
    /// <param name="faces">The assembled flux operator.</param>
    /// <param name="absorbers">Cells inside a conductor.</param>
    /// <param name="scheme">Which time discretisation to use.</param>
    /// <param name="dt">The step, in seconds.</param>
    /// <param name="tolerance">Relative sweep change the implicit solve stops at.</param>
    /// <returns>What the step did.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    public static StepReport Advance(
        DensityField density,
        DensityField next,
        FaceCoefficients faces,
        AbsorbingCells absorbers,
        StepScheme scheme,
        double dt,
        double tolerance = 1e-10)
    {
        ArgumentNullException.ThrowIfNull(density);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(absorbers);

        var sweeps = 0;
        var change = 0.0;

        // Backward Euler evaluates its flux at the NEW time and forward Euler at the
        // old one, so which density the ledger reads is not a detail: reading the
        // wrong one leaves it short by exactly what the step changed in the boundary
        // cells, which at a long step is not small.
        DensityField ledgerAt;

        if (scheme == StepScheme.Explicit)
        {
            Forward(density, next, faces, absorbers, dt);

            ledgerAt = density;
        }
        else
        {
            (sweeps, change) = Solve(density, next, faces, absorbers, dt, tolerance);

            ledgerAt = next;
        }

        var (collected, absorbed) = Ledger(ledgerAt, density, faces, absorbers, dt);

        return new StepReport(collected, absorbed, sweeps, change);
    }

    private static void Forward(
        DensityField density,
        DensityField next,
        FaceCoefficients faces,
        AbsorbingCells absorbers,
        double dt)
    {
        var grid = density.Grid;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var cell = (j * grid.CountX) + i;

                // A cell inside metal holds nothing. Skipped rather than stepped,
                // because a conductor that computed an outward flux would be a
                // conductor that emits - and it is emptied at every step rather than
                // only at the start, which is what makes an electrode a boundary for
                // the whole run instead of only for the seed.
                if (absorbers.Blocks(cell))
                {
                    next[i, j] = 0.0;
                    continue;
                }

                var here = density[i, j];
                var outward = 0.0;

                for (var face = 0; face < FaceCoefficients.Faces; face++)
                {
                    // A leaving face reads the far density as zero, which is what the
                    // old code said by assigning there = 0 for an open edge or a
                    // neighbour inside metal.
                    var neighbour = faces.Leaves(cell, face)
                        ? -1
                        : faces.Neighbour(cell, face, grid.CountY);

                    var there = neighbour < 0
                        ? 0.0
                        : density[neighbour % grid.CountX, neighbour / grid.CountX];

                    outward += faces.Flux(cell, face, here, there);
                }

                next[i, j] = Math.Max(0.0, here - (dt * outward));
            }
        }
    }

    private static (int Sweeps, double Change) Solve(
        DensityField density,
        DensityField next,
        FaceCoefficients faces,
        AbsorbingCells absorbers,
        double dt,
        double tolerance)
    {
        var grid = density.Grid;

        // The previous density is the initial guess, which is what makes the first
        // sweep cheap where the step is small and the answer barely moves.
        var largest = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var value = absorbers.Blocks((j * grid.CountX) + i) ? 0.0 : density[i, j];

                next[i, j] = value;
                largest = Math.Max(largest, value);
            }
        }

        if (largest == 0.0)
        {
            return (0, 0.0);
        }

        var sweeps = 0;
        var change = 0.0;

        // Red-black rather than lexicographic, for the same reason the Poisson
        // smoother is: the two colours are independent within a sweep, so the update
        // does not depend on the order cells are visited and the result is
        // reproducible whatever the loop does.
        while (sweeps < MaximumSweeps)
        {
            change = 0.0;

            for (var colour = 0; colour < 2; colour++)
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = (j + colour) % 2; i < grid.CountX; i += 2)
                    {
                        var cell = (j * grid.CountX) + i;

                        if (absorbers.Blocks(cell))
                        {
                            continue;
                        }

                        var gathered = density[i, j];

                        for (var face = 0; face < FaceCoefficients.Faces; face++)
                        {
                            if (faces.Leaves(cell, face))
                            {
                                continue;
                            }

                            var neighbour = faces.Neighbour(cell, face, grid.CountY);

                            if (neighbour >= 0)
                            {
                                gathered += dt * faces.In(cell, face)
                                    * next[neighbour % grid.CountX, neighbour / grid.CountX];
                            }
                        }

                        // Every quantity here is non-negative - the densities, the
                        // coefficients and the step - so the iterate cannot go
                        // negative at any sweep count. That is the property the whole
                        // scheme rests on.
                        var updated = gathered / (1.0 + (dt * faces.Outward(cell)));

                        change = Math.Max(change, Math.Abs(updated - next[i, j]));

                        next[i, j] = updated;
                    }
                }
            }

            sweeps++;

            if (change <= tolerance * largest)
            {
                break;
            }
        }

        return (sweeps, change / largest);
    }

    /// <summary>
    /// Counts what crossed a leaving face, from the density the flux is evaluated at.
    /// </summary>
    /// <remarks>
    /// The flux is read with <see cref="FaceCoefficients.Flux"/> rather than as
    /// <c>Out</c> times the density, because <c>(w*s*b)*n</c> and <c>w*(s*(b*n))</c>
    /// differ in the last bit - and the whole point of the factored form is that the
    /// rewrite of this file can be checked as bit-identical rather than close.
    /// </remarks>
    private static (double Collected, IReadOnlyList<(string Where, double Ions)> Absorbed) Ledger(
        DensityField at,
        DensityField density,
        FaceCoefficients faces,
        AbsorbingCells absorbers,
        double dt)
    {
        var grid = density.Grid;

        var collected = 0.0;
        var absorbed = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var j = 0; j < grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < grid.CountX; i++)
            {
                var cell = (j * grid.CountX) + i;

                if (absorbers.Blocks(cell))
                {
                    continue;
                }

                for (var face = 0; face < FaceCoefficients.Faces; face++)
                {
                    if (!faces.Leaves(cell, face))
                    {
                        continue;
                    }

                    // The same expression the stepper uses, not Out times the
                    // density: (w*s*b)*n and w*(s*(b*n)) differ in the last bit, and
                    // the point of the factored form is that they do not have to.
                    var flux = faces.Flux(cell, face, at[i, j], 0.0);

                    if (flux <= 0.0)
                    {
                        continue;
                    }

                    var leaving = flux * dt * volume;

                    if (faces.Collects(cell, face))
                    {
                        collected += leaving;
                    }
                    else
                    {
                        var name = faces.NameOf(cell, face)!;

                        absorbed[name] = absorbed.GetValueOrDefault(name) + leaving;
                    }
                }
            }
        }

        return (collected, [.. absorbed.Select(p => (p.Key, p.Value))]);
    }
}
