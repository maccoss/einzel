using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport.Collisions;

namespace Einzel.Transport.Diffusion;

/// <summary>The density at one instant of a run.</summary>
/// <param name="RequestedSeconds">The instant that was asked for.</param>
/// <param name="AtSeconds">The instant it was actually taken at.</param>
/// <param name="Density">The density there.</param>
/// <remarks>
/// <para>
/// Both times, because they are not the same and the difference is not the caller's to
/// guess. A diffusive step is set by a stability limit and cannot be cut to land on a
/// requested instant without changing the step sequence and therefore the answer - so
/// the snapshot is taken at the first step at or after what was asked for, and says so.
/// </para>
/// <para>
/// The gap is a step, which on the shipped models is nanoseconds against transits of
/// hundreds of microseconds. Reporting it anyway costs one field and removes the
/// question.
/// </para>
/// </remarks>
public sealed record DensitySnapshot(
    double RequestedSeconds,
    double AtSeconds,
    DensityField Density);

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
    IReadOnlyList<(double TimeSeconds, double Ions)> Arrivals)
{
    /// <summary>The density at each requested instant, in order.</summary>
    /// <remarks>
    /// <para>
    /// Empty unless instants were asked for. A run reports the density it <em>ended</em>
    /// with, which for a model whose ions have all arrived is an empty box - correctly,
    /// and uselessly, because the interesting picture is the packet in flight. This is
    /// what lets one be drawn without shortening the run and losing everything after it.
    /// </para>
    /// <para>
    /// Only instants the run actually reached appear. One past the end, or past the step
    /// budget, is absent rather than filled in with the final state - a density that was
    /// never computed is not the density at that instant, and silently substituting the
    /// last one would make a film of a finished run look like a film of a running one.
    /// </para>
    /// <para>
    /// Each is a full copy of the grid, so a hundred snapshots of a 128 by 32 grid is
    /// about 3 MB and a hundred of a 512 by 512 grid is 200 MB. The caller chooses how
    /// many; nothing here caps it, because a cap would silently drop frames.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DensitySnapshot> Snapshots { get; init; } = [];

    /// <summary>Which time discretisation was used.</summary>
    public StepScheme Scheme { get; init; } = StepScheme.Explicit;

    /// <summary>The step actually taken, in seconds.</summary>
    public double StepSeconds { get; init; }

    /// <summary>
    /// How many times larger the step was than the explicit scheme could have taken.
    /// </summary>
    /// <remarks>
    /// One on the explicit path, by construction. It is the number the implicit path
    /// exists to make large, so it is reported rather than left to be inferred from a
    /// step count.
    /// </remarks>
    public double StepGain { get; init; } = 1.0;

    /// <summary>Gauss-Seidel sweeps over the whole run, zero on the explicit path.</summary>
    public long Sweeps { get; init; }

    /// <summary>
    /// The largest relative change any implicit step's last sweep still made.
    /// </summary>
    /// <remarks>
    /// Positivity survives a partial solve but conservation does not, so this is the
    /// quantity that says how much a reader may trust the ion ledger. Zero on the
    /// explicit path, which has no inner solve to leave unfinished. A sweep change
    /// rather than a residual norm - see <see cref="StepReport"/> for why it is named
    /// that way.
    /// </remarks>
    public double WorstSweepChange { get; init; }
}

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
    /// <param name="scheme">
    /// Which time discretisation to use. The explicit one is bounded by the faster of
    /// diffusion and Courant; the implicit one has no stability bound at all.
    /// </param>
    /// <param name="stepGain">
    /// How many times the explicit stability limit to step, for the implicit scheme.
    /// </param>
    /// <param name="snapshotSeconds">
    /// Instants to record the density at, in seconds and in order, or null for none.
    /// Each is taken at the first step at or after it, and reports both times.
    /// </param>
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
        int maximumSteps = 2_000_000,
        StepScheme scheme = StepScheme.Explicit,
        double stepGain = 1.0,
        IReadOnlyList<double>? snapshotSeconds = null)
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

        // The density the declared mobility belongs to. A pressure field grades the
        // gas away from it, and mobility goes as the reciprocal of density, so this
        // is the reference the scaling is against rather than the value used.
        var number = gas.NumberDensitySi;

        // Sampled once. The field does not change during a diffusive run - a
        // sequenced one would need this inside the loop, and does not exist yet.
        var (driftX, driftY, diffusion, potential, gasX, gasY) = SampleCoefficients(
            grid, field, gas, mobility, species, sign, number, initial.Cylindrical);

        // The thermal voltage, which is what turns a potential difference across a
        // face into the exponent Scharfetter-Gummel needs.
        var thermal = BackgroundGas.BoltzmannSi * gas.TemperatureK / species.ChargeSi;

        var stable = StableStep(
            grid, driftX, driftY, gasX, gasY, diffusion, density.LargestRadialWeight());

        // The explicit scheme cannot exceed its stability limit; the implicit one has
        // none, so what bounds it is accuracy and the caller says how far to push.
        // Refusing a gain on the explicit path rather than ignoring it, because a
        // caller who asked for a longer step and silently got the short one would
        // conclude the scheme is slow rather than that the request went nowhere.
        if (scheme == StepScheme.Explicit && stepGain != 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepGain),
                stepGain,
                "the explicit scheme is bounded by its own stability limit, so it cannot "
                + "take a longer step. Ask for StepScheme.Implicit to use a gain.");
        }

        var step = stable * stepGain;

        // The operator is assembled once: everything that decides a face coefficient -
        // the mesh, the mobility, the field, the gas - is fixed for the whole run. The
        // explicit path used to recompute two exponentials per face per step, which a
        // driven funnel pays about a million times over.
        var faces = FaceCoefficients.Assemble(
            density, grid, driftX, driftY, gasX, gasY, diffusion, potential, thermal,
            edges, absorbers);

        var arrivals = new List<(double, double)>();
        var lost = new Dictionary<string, double>(StringComparer.Ordinal);

        var collected = 0.0;
        var time = 0.0;
        var steps = 0;
        var sweeps = 0L;
        var worstChange = 0.0;

        var snapshots = new List<DensitySnapshot>(snapshotSeconds?.Count ?? 0);
        var pending = 0;

        // An instant at or before the launch is the initial density, which no step
        // produces. Taken here so that a caller asking for t = 0 gets the packet as it
        // was seeded rather than as it was after one step.
        while (pending < (snapshotSeconds?.Count ?? 0) && snapshotSeconds![pending] <= time)
        {
            snapshots.Add(new DensitySnapshot(snapshotSeconds[pending], time, density.Clone()));
            pending++;
        }

        while (time < untilSeconds && steps < maximumSteps)
        {
            var dt = Math.Min(step, untilSeconds - time);

            var leaving = DensityStepper.Advance(
                density, next, faces, absorbers, scheme, dt);

            (density, next) = (next, density);

            time += dt;
            steps++;

            sweeps += leaving.Sweeps;
            worstChange = Math.Max(worstChange, leaving.SweepChange);

            collected += leaving.Collected;

            if (leaving.Collected > 0.0)
            {
                arrivals.Add((time, leaving.Collected));
            }

            foreach (var (where, ions) in leaving.Absorbed)
            {
                lost[where] = lost.GetValueOrDefault(where) + ions;
            }

            // At the first step at or after each requested instant. The step is set by a
            // stability limit and cutting it to land exactly would change the step
            // sequence and so the answer, which is a high price for an offset of one
            // step - so the instant actually taken is reported instead.
            while (pending < (snapshotSeconds?.Count ?? 0) && snapshotSeconds![pending] <= time)
            {
                snapshots.Add(new DensitySnapshot(snapshotSeconds[pending], time, density.Clone()));
                pending++;
            }
        }

        return new DiffusionResult(
            density, steps, time, density.Population(), collected, lost, arrivals)
        {
            Snapshots = snapshots,
            Scheme = scheme,
            StepSeconds = step,
            StepGain = stepGain,
            Sweeps = sweeps,
            WorstSweepChange = worstChange,
        };
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

                // Sampled per node, like the flow below and for the same reason. The
                // mobility carries two separate density dependences and this moves
                // both: it goes as 1/n outright, and its field expansion is in E/n.
                // Where no pressure field is declared this is the model's own
                // density at every node, the ratio is exactly one, and the result is
                // bit-identical to what it was.
                var here = gas.NumberDensityAt(in point);
                var local = mobility.At(strength, here, number);

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

}
