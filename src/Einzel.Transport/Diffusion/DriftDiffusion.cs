using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport.Collisions;

namespace Einzel.Transport.Diffusion;

/// <summary>What a diffusive run did.</summary>
/// <param name="Density">The density at the end.</param>
/// <param name="Steps">Time steps taken.</param>
/// <param name="ElapsedSeconds">Simulated time.</param>
/// <param name="Remaining">Ions still in the domain.</param>
/// <param name="Collected">Ions that left through the collecting boundary.</param>
/// <param name="Lost">Ions that left any other way, by boundary name.</param>
/// <param name="Arrivals">
/// When ions reached the collecting boundary: one bin per time step, in ions.
/// </param>
public sealed record DiffusionResult(
    DensityField Density,
    int Steps,
    double ElapsedSeconds,
    double Remaining,
    double Collected,
    IReadOnlyDictionary<string, double> Lost,
    IReadOnlyList<(double TimeSeconds, double Ions)> Arrivals);

/// <summary>Where ions leave the domain, and what that means.</summary>
public enum Escape
{
    /// <summary>Ions are reflected. A wall the model does not care about.</summary>
    Reflecting,

    /// <summary>Ions leave and are counted as collected. The detector or exit.</summary>
    Collecting,

    /// <summary>Ions leave and are counted as lost. A wall they stick to.</summary>
    Absorbing,
}

/// <summary>
/// Transport as an evolving density: drift down the field, diffusion outward.
/// </summary>
/// <remarks>
/// <para>
/// The second half of REG-1, and the description that applies where trajectory
/// integration does not. Above about 10^-2 mbar the collision frequency vastly
/// exceeds everything else in the problem and residence times are of order a
/// millisecond, so integrating collision by collision is not merely slow - each ion
/// has forgotten where it came from long before it arrives, and what survives is a
/// distribution.
/// </para>
/// <para>
/// The flux between two cells uses the Scharfetter-Gummel form, which is the
/// exponentially-fitted upwind scheme and is <em>exact for a potential that varies
/// linearly across the cell</em>. That matters for the same reason cut cells did in
/// the field solver: centred differencing here is not merely less accurate, it
/// oscillates and produces negative densities as soon as drift outruns diffusion,
/// which in a funnel it does everywhere. A negative density is not a small error, it
/// is a quantity that has stopped meaning anything.
/// </para>
/// <para>
/// Explicit in time, with the step taken from the stability limits rather than
/// declared. That is affordable here because the limits are generous at the
/// pressures this mode is for - a funnel at a millibar runs in a few thousand steps
/// - and it avoids a linear solve per step whose convergence would be a second thing
/// to have to trust.
/// </para>
/// </remarks>
public static class DriftDiffusion
{
    /// <summary>Fraction of the stability limit a step actually takes.</summary>
    /// <remarks>
    /// Both limits below are exact thresholds for the linear problem, and taking
    /// them exactly leaves nothing for the nonlinearity of a field-dependent
    /// mobility. Half is the usual margin and costs a factor of two in steps.
    /// </remarks>
    private const double StabilityMargin = 0.5;

    /// <summary>Evolves a density until a time, or until the ions have gone.</summary>
    /// <param name="initial">The starting density.</param>
    /// <param name="field">The electrostatic field driving the drift.</param>
    /// <param name="gas">The gas, for temperature and number density.</param>
    /// <param name="mobility">The declared mobility (TRN-1).</param>
    /// <param name="species">The ion, for its charge sign.</param>
    /// <param name="untilSeconds">How long to run.</param>
    /// <param name="edges">What happens at each domain edge.</param>
    /// <param name="absorbers">
    /// Interior cells that swallow whatever reaches them, named by surface, or null
    /// where the tracked region has no geometry in it.
    /// </param>
    /// <param name="maximumSteps">A runaway guard.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public static DiffusionResult Run(
        DensityField initial,
        IElectrostaticField field,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        double untilSeconds,
        DomainEdges edges,
        AbsorbingCells? absorbers = null,
        int maximumSteps = 2_000_000)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(untilSeconds);

        absorbers ??= AbsorbingCells.None;

        var grid = initial.Grid;
        var density = initial.Clone();
        var next = new DensityField(grid, initial.Cylindrical);

        var sign = Math.Sign(species.ChargeSi);
        var number = gas.NumberDensitySi;

        // Sampled once. The field does not change during a diffusive run - a
        // sequenced one would need this inside the loop, and does not exist yet.
        var (driftX, driftY, diffusion, potential, gasX, gasY) = SampleCoefficients(
            grid, field, gas, mobility, species, sign, number, initial.Cylindrical);

        // The thermal voltage, which is what turns a potential difference across a
        // face into the exponent Scharfetter-Gummel needs.
        var thermal = BackgroundGas.BoltzmannSi * gas.TemperatureK / species.ChargeSi;

        var step = StableStep(
            grid, driftX, driftY, gasX, gasY, diffusion, density.LargestRadialWeight());

        var arrivals = new List<(double, double)>();
        var lost = new Dictionary<string, double>(StringComparer.Ordinal);

        var collected = 0.0;
        var time = 0.0;
        var steps = 0;

        while (time < untilSeconds && steps < maximumSteps)
        {
            var dt = Math.Min(step, untilSeconds - time);

            var leaving = Advance(
                density, next, grid, driftX, driftY, gasX, gasY,
                diffusion, potential, thermal, dt, edges, absorbers);

            (density, next) = (next, density);

            time += dt;
            steps++;

            collected += leaving.Collected;

            if (leaving.Collected > 0.0)
            {
                arrivals.Add((time, leaving.Collected));
            }

            foreach (var (where, ions) in leaving.Absorbed)
            {
                lost[where] = lost.GetValueOrDefault(where) + ions;
            }
        }

        return new DiffusionResult(
            density, steps, time, density.Population(), collected, lost, arrivals);
    }

    /// <summary>What happens at each edge of the domain.</summary>
    /// <param name="MinX">The lower x edge.</param>
    /// <param name="MaxX">The upper x edge.</param>
    /// <param name="MinY">The lower y edge, which is the axis in a cylindrical solve.</param>
    /// <param name="MaxY">The upper y edge.</param>
    public readonly record struct DomainEdges(
        Escape MinX = Escape.Absorbing,
        Escape MaxX = Escape.Collecting,
        Escape MinY = Escape.Reflecting,
        Escape MaxY = Escape.Absorbing);

    /// <summary>
    /// The largest step both stability limits allow.
    /// </summary>
    /// <remarks>
    /// Two separate conditions, and which one binds says what the run is doing.
    /// Diffusion binds as h squared over D, so refining the mesh costs quadratically;
    /// drift binds as h over v, only linearly. A run whose step is set by diffusion
    /// is one where the mesh, not the physics, is the expense.
    /// </remarks>
    /// <summary>
    /// The step a run will take, from the mesh and the coefficients alone.
    /// </summary>
    /// <param name="grid">The grid the density is tracked on.</param>
    /// <param name="diffusionSi">The diffusion coefficient, in square metres per second.</param>
    /// <param name="fastestCrossingRateSi">
    /// The largest value of |vx|/hx + |vy|/hy anywhere, in reciprocal seconds, or
    /// zero when the field is not known. A <em>rate</em> rather than a speed,
    /// because that is what the Courant condition is on: an axial drift on an
    /// anisotropic mesh crosses a cell at a different rate from a diagonal one of
    /// the same speed, and quoting a speed loses which.
    /// </param>
    /// <param name="largestRadialWeight">
    /// The largest conservative face weight anywhere on the grid, from
    /// <see cref="DensityField.LargestRadialWeight"/>. One in the plane, four on the
    /// axis of a cylindrical solve.
    /// </param>
    /// <returns>The step, and which limit set it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Public because a cost estimate needs it before the run rather than after, and
    /// GRD-8 gates on a number that has to be available without doing the work.
    /// </para>
    /// <para>
    /// The diffusion limit is knowable without solving anything - D comes from the
    /// mobility and the temperature, and the mesh is declared - while the drift limit
    /// needs the field. So an estimate that has not solved the field can bound the
    /// step from above and say so, which is the right direction: an estimate that
    /// runs under is worse than one that runs over.
    /// </para>
    /// </remarks>
    public static (double Seconds, string Limit) StepFor(
        Fields.Solved.Grid2D grid,
        double diffusionSi,
        double fastestCrossingRateSi = 0.0,
        double largestRadialWeight = 1.0)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // A weighted radial face scales the outward coefficient with it, so the
        // largest weight anywhere scales the limit. One in the plane; four on the
        // axis of a cylindrical solve, where the cell is a disc rather than a ring.
        // Taking the step from the unweighted rate there is stepping four times too
        // far, at the one place a funnel puts most of its ions.
        var weight = Math.Max(1.0, largestRadialWeight);

        var inverseSquares = (1.0 / (grid.SpacingX * grid.SpacingX))
            + (weight / (grid.SpacingY * grid.SpacingY));

        var byDiffusion = diffusionSi > 0.0
            ? 1.0 / (2.0 * diffusionSi * inverseSquares)
            : double.PositiveInfinity;

        var byDrift = fastestCrossingRateSi > 0.0
            ? 1.0 / (weight * fastestCrossingRateSi)
            : double.PositiveInfinity;

        if (double.IsPositiveInfinity(byDiffusion) && double.IsPositiveInfinity(byDrift))
        {
            return (1e-6, "neither: nothing is moving");
        }

        return byDiffusion <= byDrift
            ? (StabilityMargin * byDiffusion, "diffusion")
            : (StabilityMargin * byDrift, "drift");
    }

    private static double StableStep(
        Fields.Solved.Grid2D grid,
        double[] driftX,
        double[] driftY,
        double[] gasX,
        double[] gasY,
        double[] diffusion,
        double largestRadialWeight)
    {
        var fastest = 0.0;
        var widest = 0.0;

        for (var k = 0; k < diffusion.Length; k++)
        {
            // The Courant condition is on how fast the ions actually move, which is
            // the field drift and the gas carrying them added. Taking it from the
            // field alone would let a fast gas outrun the step in a model whose own
            // field is weak - which is precisely the funnel case, where the gas is
            // half the mechanism.
            fastest = Math.Max(
                fastest, CrossingRate(grid, driftX[k] + gasX[k], driftY[k] + gasY[k]));

            widest = Math.Max(widest, diffusion[k]);
        }

        // The same function the cost estimate calls, so the two cannot disagree
        // about what a run will do. An estimate computed by a second implementation
        // of the step rule is an estimate of that implementation.
        return StepFor(grid, widest, fastest, largestRadialWeight).Seconds;
    }

    /// <summary>How fast a drift crosses a cell, in reciprocal seconds.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="driftXSi">Drift along x, in metres per second.</param>
    /// <param name="driftYSi">Drift along y, in metres per second.</param>
    /// <returns>The Courant rate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public static double CrossingRate(Fields.Solved.Grid2D grid, double driftXSi, double driftYSi)
    {
        ArgumentNullException.ThrowIfNull(grid);

        return (Math.Abs(driftXSi) / grid.SpacingX) + (Math.Abs(driftYSi) / grid.SpacingY);
    }

    private static (double[] DriftX, double[] DriftY, double[] Diffusion, double[] Potential, double[] GasX, double[] GasY) SampleCoefficients(
        Fields.Solved.Grid2D grid,
        IElectrostaticField field,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        int sign,
        double number,
        bool cylindrical)
    {
        var count = grid.CountX * grid.CountY;

        var driftX = new double[count];
        var driftY = new double[count];
        var diffusion = new double[count];
        var potential = new double[count];
        var gasX = new double[count];
        var gasY = new double[count];

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var point = new Vec3(grid.X(i), grid.Y(j), 0.0);
                var electric = field.ElectricFieldAt(in point);

                var strength = Math.Sqrt((electric.X * electric.X) + (electric.Y * electric.Y));
                var local = mobility.At(strength, number);

                var k = (j * grid.CountX) + i;

                driftX[k] = sign * local * electric.X;
                driftY[k] = sign * local * electric.Y;

                diffusion[k] = Mobility.DiffusionSi(gas.TemperatureK, species.ChargeSi, local);
                potential[k] = field.PotentialAt(in point);

                // Sampled per node rather than taken once, even though only a
                // uniform flow can be declared today. A flow field is what GAS-1
                // asks for above 1e-2 mbar and the face averaging below is only
                // correct if the velocity is known at both nodes of a face.
                var flow = gas.VelocityAt(in point);

                gasX[k] = flow.X;
                gasY[k] = flow.Y;
            }
        }

        // On the axis of a cylindrical solve there is no radial direction, so a
        // radial drift there is a discretisation artefact rather than a velocity.
        // The same argument applies to the gas: a neutral flow with a radial
        // component on the axis is describing a jet emerging from the axis itself.
        if (cylindrical)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                driftY[i] = 0.0;
                gasY[i] = 0.0;
            }
        }

        return (driftX, driftY, diffusion, potential, gasX, gasY);
    }

    private static (double Collected, IReadOnlyList<(string Where, double Ions)> Absorbed) Advance(
        DensityField density,
        DensityField next,
        Fields.Solved.Grid2D grid,
        double[] driftX,
        double[] driftY,
        double[] gasX,
        double[] gasY,
        double[] diffusion,
        double[] potential,
        double thermal,
        double dt,
        DomainEdges edges,
        AbsorbingCells absorbers)
    {
        var collected = 0.0;
        var absorbed = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var j = 0; j < grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < grid.CountX; i++)
            {
                // A cell inside metal holds nothing. Skipped rather than stepped,
                // because a conductor that computed an outward flux would be a
                // conductor that emits - and it is emptied at every step rather than
                // only at the start, which is what makes an electrode a boundary for
                // the whole run instead of only for the seed.
                if (absorbers.Blocks((j * grid.CountX) + i))
                {
                    next[i, j] = 0.0;
                    continue;
                }

                var here = density[i, j];
                var outward = 0.0;

                outward += FaceFlux(
                    density, grid, driftX, gasX, diffusion, potential, thermal,
                    i, j, i + 1, j, +1, grid.SpacingX,
                    edges.MaxX, "maxX", ref collected, absorbed, volume, dt, absorbers);

                outward += FaceFlux(
                    density, grid, driftX, gasX, diffusion, potential, thermal,
                    i, j, i - 1, j, -1, grid.SpacingX,
                    edges.MinX, "minX", ref collected, absorbed, volume, dt, absorbers);

                // The radial faces carry a weight, because in a cylindrical solve
                // the two cells sharing one are rings of different volume and a flux
                // per unit area is not a flux per cell. Exactly 1 in the plane, so
                // an isotropic solve multiplies by one and is unchanged.
                outward += FaceFlux(
                    density, grid, driftY, gasY, diffusion, potential, thermal,
                    i, j, i, j + 1, +1, grid.SpacingY,
                    edges.MaxY, "maxY", ref collected, absorbed, volume, dt, absorbers,
                    density.RadialFaceWeight(j, +1));

                outward += FaceFlux(
                    density, grid, driftY, gasY, diffusion, potential, thermal,
                    i, j, i, j - 1, -1, grid.SpacingY,
                    edges.MinY, "minY", ref collected, absorbed, volume, dt, absorbers,
                    density.RadialFaceWeight(j, -1));

                next[i, j] = Math.Max(0.0, here - (dt * outward));
            }
        }

        return (collected, [.. absorbed.Select(p => (p.Key, p.Value))]);
    }

    /// <summary>
    /// Net flow per unit density across one face, by Scharfetter-Gummel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exponentially-fitted upwind flux. With P the potential drop across the
    /// face in units of the thermal voltage, the exact steady flux out of the near
    /// cell across a face of width h is
    ///     J = (D/h^2) [ B(-P) n_here - B(P) n_there ],
    /// where B is the Bernoulli function x/(exp(x)-1). It reduces to centred
    /// differencing when drift is negligible and to pure upwinding when drift
    /// dominates, and it never produces a negative density in between.
    /// </para>
    /// <para>
    /// <strong>Everything here is a property of the face, not of the cell asking.</strong>
    /// P comes from the potential difference between the two nodes and D from their
    /// average, so the two cells sharing a face compute the same flux with opposite
    /// signs and nothing is created or destroyed at it.
    /// </para>
    /// <para>
    /// The first version of this used the drift sampled at the <em>cell centre</em>,
    /// which is the same thing wherever the field is uniform and a different thing
    /// wherever it is not. The conservation test passed - its field was uniform - and
    /// a seeded Boltzmann equilibrium in a well drained from the middle at 4.7 times
    /// per millisecond, because at every face where the field varied the two cells
    /// disagreed about how much crossed it.
    /// </para>
    /// </remarks>
    private static double FaceFlux(
        DensityField density,
        Fields.Solved.Grid2D grid,
        double[] drift,
        double[] gasDrift,
        double[] diffusion,
        double[] potential,
        double thermal,
        int i,
        int j,
        int ni,
        int nj,
        int direction,
        double spacing,
        Escape edge,
        string name,
        ref double collected,
        Dictionary<string, double> absorbed,
        double volume,
        double dt,
        AbsorbingCells absorbers,
        double faceWeight = 1.0)
    {
        if (faceWeight == 0.0)
        {
            // A face with no area. On the axis of a cylindrical solve this is the
            // whole statement: there is no radial direction there and nothing
            // crosses.
            return 0.0;
        }

        var outside = ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY;

        if (outside && edge == Escape.Reflecting)
        {
            return 0.0;
        }

        var k = (j * grid.CountX) + i;
        var here = density[i, j];

        // A neighbour inside metal is an open edge with a name. The two cases are
        // the same physics - the density beyond is zero and nothing comes back - so
        // they share the accounting below rather than getting a second path through
        // it, and the ions land under the surface's own name rather than under a
        // domain edge they never reached.
        var swallowed = !outside && absorbers.Blocks((nj * grid.CountX) + ni);

        if (swallowed)
        {
            name = absorbers.NameAt((nj * grid.CountX) + ni)!;
            edge = Escape.Absorbing;
        }

        double there;
        double faceDiffusion;
        double drop;

        if (outside)
        {
            // Beyond an open edge the density is zero: ions that reach it are gone,
            // and nothing comes back. There is no neighbour to take a potential
            // difference against, so the cell's own field stands in for one - which
            // is exact for the linear potential the scheme assumes anyway.
            there = 0.0;
            faceDiffusion = diffusion[k];

            // The exponent directly from the local drift, since P = v h / D, and the
            // gas carries the ions across the edge alongside the field.
            var total = drift[k] + gasDrift[k];

            drop = diffusion[k] > 0.0
                ? direction * total * spacing / diffusion[k]
                : (direction * total > 0.0 ? 40.0 : -40.0);
        }
        else
        {
            var neighbour = (nj * grid.CountX) + ni;

            // Zero inside a conductor, and it stays zero: with no density on the far
            // side the Scharfetter-Gummel flux reduces to B(-P) n_here, which is
            // non-negative for any P. So an electrode can only take, never give -
            // that falls out of the scheme rather than needing a clamp.
            there = swallowed ? 0.0 : density[ni, nj];

            faceDiffusion = 0.5 * (diffusion[k] + diffusion[neighbour]);

            // P = q (phi_here - phi_there) / kT, with the thermal voltage carrying
            // the sign of the charge. Antisymmetric between the two cells by
            // construction, which is what makes the flux conservative.
            drop = (potential[k] - potential[neighbour]) / thermal;

            // The gas adds a velocity the potential cannot express: advection by a
            // moving neutral is not the gradient of anything, so it enters the
            // exponent directly as P_gas = v.n h / D rather than as a potential
            // difference. That is the same exponent the field term already is - by
            // the Einstein relation q(phi_here - phi_there)/kT *is* v h / D - so the
            // two simply add, and Scharfetter-Gummel stays exact for a linearly
            // varying total drift.
            //
            // Averaged over the two nodes of the face, and signed by which way the
            // face points. Both are what keep it conservative: the neighbour
            // computes the same average with the opposite sign, so the two cells
            // sharing a face agree about how much crossed it. Sampling the gas at
            // the cell centre instead would repeat, exactly, the bug that made a
            // seeded Boltzmann equilibrium drain from the middle.
            var faceGas = 0.5 * (gasDrift[k] + gasDrift[neighbour]);

            if (faceGas != 0.0)
            {
                drop += faceDiffusion > 0.0
                    ? direction * faceGas * spacing / faceDiffusion
                    : (direction * faceGas > 0.0 ? 40.0 : -40.0);
            }
        }

        var peclet = Math.Clamp(drop, -40.0, 40.0);

        var flux = faceDiffusion > 0.0
            ? faceDiffusion / (spacing * spacing) * ((Bernoulli(-peclet) * here) - (Bernoulli(peclet) * there))
            : (peclet > 0.0 ? peclet * here : peclet * there) / spacing;

        // Area over volume, in the units the rest of this is written in. Both cells
        // sharing a face take the same face radius and so the same area, while each
        // divides by its own volume - which is what makes the ion counts balance
        // rather than only the densities.
        flux *= faceWeight;

        if ((outside || swallowed) && flux > 0.0)
        {
            var leaving = flux * dt * volume;

            if (edge == Escape.Collecting)
            {
                collected += leaving;
            }
            else
            {
                absorbed[name] = absorbed.GetValueOrDefault(name) + leaving;
            }
        }

        return flux;
    }

    /// <summary>The Bernoulli function x / (exp(x) - 1), continuous at zero.</summary>
    /// <remarks>
    /// The naive form loses everything to cancellation near zero, which is where
    /// most of a low-field domain sits, so the series is used there. At large
    /// positive x it underflows to zero and at large negative x it is -x; both are
    /// the correct limits and taking them explicitly avoids an overflow.
    /// </remarks>
    private static double Bernoulli(double x)
    {
        if (x > 40.0)
        {
            return 0.0;
        }

        if (x < -40.0)
        {
            return -x;
        }

        if (Math.Abs(x) < 1e-6)
        {
            return 1.0 - (0.5 * x) + (x * x / 12.0);
        }

        return x / (Math.Exp(x) - 1.0);
    }
}
