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
        int maximumSteps = 2_000_000)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(untilSeconds);

        var grid = initial.Grid;
        var density = initial.Clone();
        var next = new DensityField(grid, initial.Cylindrical);

        var sign = Math.Sign(species.ChargeSi);
        var number = gas.NumberDensitySi;

        // Sampled once. The field does not change during a diffusive run - a
        // sequenced one would need this inside the loop, and does not exist yet.
        var (driftX, driftY, diffusion, potential) = SampleCoefficients(
            grid, field, gas, mobility, species, sign, number, initial.Cylindrical);

        // The thermal voltage, which is what turns a potential difference across a
        // face into the exponent Scharfetter-Gummel needs.
        var thermal = BackgroundGas.BoltzmannSi * gas.TemperatureK / species.ChargeSi;

        var step = StableStep(grid, driftX, driftY, diffusion);

        var arrivals = new List<(double, double)>();
        var lost = new Dictionary<string, double>(StringComparer.Ordinal);

        var collected = 0.0;
        var time = 0.0;
        var steps = 0;

        while (time < untilSeconds && steps < maximumSteps)
        {
            var dt = Math.Min(step, untilSeconds - time);

            var leaving = Advance(
                density, next, grid, driftX, driftY, diffusion, potential, thermal, dt, edges);

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
    private static double StableStep(
        Fields.Solved.Grid2D grid, double[] driftX, double[] driftY, double[] diffusion)
    {
        var fastest = 0.0;
        var widest = 0.0;

        for (var k = 0; k < diffusion.Length; k++)
        {
            fastest = Math.Max(fastest, (Math.Abs(driftX[k]) / grid.SpacingX)
                + (Math.Abs(driftY[k]) / grid.SpacingY));

            widest = Math.Max(widest, diffusion[k]);
        }

        var byDrift = fastest > 0.0 ? 1.0 / fastest : double.PositiveInfinity;

        var inverseSquares = (1.0 / (grid.SpacingX * grid.SpacingX))
            + (1.0 / (grid.SpacingY * grid.SpacingY));

        var byDiffusion = widest > 0.0
            ? 1.0 / (2.0 * widest * inverseSquares)
            : double.PositiveInfinity;

        var limit = Math.Min(byDrift, byDiffusion);

        return double.IsPositiveInfinity(limit) ? 1e-6 : StabilityMargin * limit;
    }

    private static (double[] DriftX, double[] DriftY, double[] Diffusion, double[] Potential) SampleCoefficients(
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
            }
        }

        // On the axis of a cylindrical solve there is no radial direction, so a
        // radial drift there is a discretisation artefact rather than a velocity.
        if (cylindrical)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                driftY[i] = 0.0;
            }
        }

        return (driftX, driftY, diffusion, potential);
    }

    private static (double Collected, IReadOnlyList<(string Where, double Ions)> Absorbed) Advance(
        DensityField density,
        DensityField next,
        Fields.Solved.Grid2D grid,
        double[] driftX,
        double[] driftY,
        double[] diffusion,
        double[] potential,
        double thermal,
        double dt,
        DomainEdges edges)
    {
        var collected = 0.0;
        var absorbed = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var j = 0; j < grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < grid.CountX; i++)
            {
                var here = density[i, j];
                var outward = 0.0;

                outward += FaceFlux(
                    density, grid, driftX, diffusion, potential, thermal,
                    i, j, i + 1, j, +1, grid.SpacingX,
                    edges.MaxX, "maxX", ref collected, absorbed, volume, dt);

                outward += FaceFlux(
                    density, grid, driftX, diffusion, potential, thermal,
                    i, j, i - 1, j, -1, grid.SpacingX,
                    edges.MinX, "minX", ref collected, absorbed, volume, dt);

                outward += FaceFlux(
                    density, grid, driftY, diffusion, potential, thermal,
                    i, j, i, j + 1, +1, grid.SpacingY,
                    edges.MaxY, "maxY", ref collected, absorbed, volume, dt);

                outward += FaceFlux(
                    density, grid, driftY, diffusion, potential, thermal,
                    i, j, i, j - 1, -1, grid.SpacingY,
                    edges.MinY, "minY", ref collected, absorbed, volume, dt);

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
        double dt)
    {
        var outside = ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY;

        if (outside && edge == Escape.Reflecting)
        {
            return 0.0;
        }

        var k = (j * grid.CountX) + i;
        var here = density[i, j];

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

            // The exponent directly from the local drift, since P = v h / D.
            drop = diffusion[k] > 0.0
                ? direction * drift[k] * spacing / diffusion[k]
                : (direction * drift[k] > 0.0 ? 40.0 : -40.0);
        }
        else
        {
            var neighbour = (nj * grid.CountX) + ni;

            there = density[ni, nj];
            faceDiffusion = 0.5 * (diffusion[k] + diffusion[neighbour]);

            // P = q (phi_here - phi_there) / kT, with the thermal voltage carrying
            // the sign of the charge. Antisymmetric between the two cells by
            // construction, which is what makes the flux conservative.
            drop = (potential[k] - potential[neighbour]) / thermal;
        }

        var peclet = Math.Clamp(drop, -40.0, 40.0);

        var flux = faceDiffusion > 0.0
            ? faceDiffusion / (spacing * spacing) * ((Bernoulli(-peclet) * here) - (Bernoulli(peclet) * there))
            : (peclet > 0.0 ? peclet * here : peclet * there) / spacing;

        if (outside && flux > 0.0)
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
