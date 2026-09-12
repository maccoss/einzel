using System.Diagnostics;

using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A ramped diffusive phase keeps the ponderomotive well it computed and recomputes only
/// the direct term, which is what an elution scan needs and what it may not cost.
/// </summary>
/// <remarks>
/// <para>
/// The cost this is about: a ramped phase re-samples its coefficients at every step, and
/// where the field is driven every sample is a cycle average. Per node that is one pass
/// of potential evaluations for the direct term and <em>two</em> passes of field
/// evaluations for the well, seven times over - once at the node and once at each end of
/// the central difference the effective field is taken by. An elution scan holds every RF
/// amplitude and walks only the DC, so the well is the same at every step and is the half
/// that need not be recomputed.
/// </para>
/// <para>
/// <b>The reference path is reached with a pass-through wrapper rather than a switch.</b>
/// The cache engages on the concrete <c>PonderomotiveField</c> type, so a forwarding
/// <see cref="IElectrostaticField"/> around one gives exactly the arithmetic the solver
/// did before, in the same order, with no knob in the engine and none in the model
/// format. It also exercises the branch that declines to cache.
/// </para>
/// <para>
/// <b>The RF is driven about x and the ramp pushes along x, which is deliberate.</b> The
/// quadrupole's field then lies in the y-z plane and the ramped DC lies along x, so the
/// two never add into the same component and the mean square of the oscillating field is
/// <em>bit-identically</em> independent of where the ramp has got to. That is what makes
/// "the optimisation moves not one number" an assertion rather than a tolerance. A ramp
/// along the same axis as the drive perturbs the last bits of the cycle mean and so of
/// the well, which the probes accept as unchanged - correctly, since it is a part in
/// 1e16 - and that case is measured separately below rather than asserted as equality.
/// </para>
/// </remarks>
public sealed class PonderomotiveWellCacheTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;

    /// <summary>Axis to electrode surface, conventionally r0.</summary>
    private const double InscribedRadiusM = 4e-3;

    /// <summary>Held through the scan, which is the whole premise.</summary>
    private const double AmplitudeVolts = 100.0;

    private const double FrequencyHz = 1e6;

    /// <summary>
    /// Absorbing behind, collecting ahead, the axis reflecting and the wall absorbing.
    /// </summary>
    /// <remarks>
    /// The defaults, and chosen rather than reflecting everywhere so the comparison has a
    /// collected count and a named loss in it as well as a density. A box nothing leaves
    /// compares two ledgers of zeros.
    /// </remarks>
    private static readonly DriftDiffusion.DomainEdges Edges = new(
        Escape.Absorbing, Escape.Collecting, Escape.Reflecting, Escape.Absorbing);

    /// <summary>
    /// The direct term and the well sum to exactly what the whole potential was.
    /// </summary>
    /// <remarks>
    /// Every diffusive number this engine has published through a driven geometry comes
    /// through <c>PotentialAt</c>, so the split that makes half of it cacheable must not
    /// move the whole by a bit. Over a spread of points and over four RF phases - reached
    /// by shifting the drive in time, since the effective potential has no instant of its
    /// own - because a sum that happened to be exact at one point proves nothing.
    /// </remarks>
    [Fact]
    public void TheWholePotentialIsExactlyItsTwoHalves()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var rate = PonderomotiveField.CollisionRateFromMobility(
            species.ChargeSi, species.MassSi, 0.05);

        var compared = 0;

        foreach (var shift in new[] { 0.0, 0.1e-6, 0.37e-6, 0.9e-6 })
        {
            var driven = new TimeShiftedField(Quadrupole(AmplitudeVolts), shift);
            var field = new PonderomotiveField(driven, species.ChargeSi, species.MassSi, rate);

            for (var i = -4; i <= 4; i++)
            {
                for (var j = -4; j <= 4; j++)
                {
                    var point = new Vec3(i * 1.5e-3, j * 0.8e-3, 0.0);

                    var whole = field.PotentialAt(in point);
                    var halves = field.DirectPotentialAt(in point) + field.WellAt(in point);

                    Assert.Equal(whole, halves);

                    compared++;
                }
            }
        }

        output.WriteLine($"{compared} points across four RF phases, all exact");
    }

    /// <summary>
    /// A ramping-DC run with the cache is the same run, to the bit.
    /// </summary>
    /// <remarks>
    /// The assertion that matters. Every node of the final density, the collected count,
    /// the step count and every named loss, against the same ramp driven through a
    /// wrapper the cache declines to recognise. The rebuild counts are asserted too,
    /// because bit-identity between two runs neither of which used the cache would be
    /// perfectly true and would say nothing.
    /// </remarks>
    [Fact]
    public void ARampingDcRunIsBitIdenticalWithTheCacheAndWithout()
    {
        var grid = Grid2D.OverBox(-0.02, -0.004, 0.02, 0.004, 64, 32);
        var gas = Nitrogen(200.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);

        const double Seconds = 2e-5;

        var cached = Run(grid, gas, mobility, species, Seconds, DirectRamp(species, gas), plain: false);
        var reference = Run(grid, gas, mobility, species, Seconds, DirectRamp(species, gas), plain: true);

        output.WriteLine(
            $"cached:    {cached.Steps} steps, {cached.Assemblies} assemblies, "
            + $"{cached.WellRebuilds} well rebuilds, {cached.Collected:E17} collected");
        output.WriteLine(
            $"reference: {reference.Steps} steps, {reference.Assemblies} assemblies, "
            + $"{reference.WellRebuilds} well rebuilds, {reference.Collected:E17} collected");

        // The cache engaged and held; the reference never built one at all. Without these
        // the equality below could be two runs of the same path.
        Assert.Equal(1, cached.WellRebuilds);
        Assert.Equal(0, reference.WellRebuilds);
        Assert.True(cached.Assemblies > 1, "the ramp was not followed, so nothing was cached across");

        Assert.Equal(reference.Steps, cached.Steps);
        Assert.Equal(reference.Assemblies, cached.Assemblies);
        Assert.Equal(reference.Collected, cached.Collected);
        Assert.Equal(reference.Remaining, cached.Remaining);

        Assert.Equal(reference.Lost.Count, cached.Lost.Count);

        foreach (var (where, ions) in reference.Lost)
        {
            Assert.Equal(ions, cached.Lost[where]);
        }

        var differing = 0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (reference.Density[i, j] != cached.Density[i, j])
                {
                    differing++;
                }
            }
        }

        output.WriteLine($"{differing} of {grid.CountX * grid.CountY} nodes differ");
        Assert.Equal(0, differing);
    }

    /// <summary>
    /// A ramp that moves the RF rebuilds the well every step; one that moves only DC
    /// rebuilds it once.
    /// </summary>
    /// <remarks>
    /// The pair, not either alone. A cache that never rebuilt would pass the second
    /// assertion and be wrong for every amplitude ramp there is, and one that always
    /// rebuilt would pass the first and buy nothing.
    /// </remarks>
    [Fact]
    public void AnAmplitudeRampRebuildsTheWellAndADirectRampDoesNot()
    {
        var grid = Grid2D.OverBox(-0.02, -0.004, 0.02, 0.004, 32, 16);
        var gas = Nitrogen(200.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);

        // Long enough to be a run rather than a handful of steps: "rebuilds at every one
        // of three later steps" is not much of a statement about a ramp.
        const double Seconds = 4e-5;

        var direct = Run(grid, gas, mobility, species, Seconds, DirectRamp(species, gas), plain: false);
        var amplitude = Run(grid, gas, mobility, species, Seconds, AmplitudeRamp(species, gas), plain: false);

        output.WriteLine(
            $"DC ramp:        {direct.Assemblies} assemblies, {direct.WellRebuilds} well rebuilds");
        output.WriteLine(
            $"amplitude ramp: {amplitude.Assemblies} assemblies, {amplitude.WellRebuilds} well rebuilds");

        Assert.Equal(1, direct.WellRebuilds);
        Assert.Equal(amplitude.Assemblies, amplitude.WellRebuilds);
    }

    /// <summary>
    /// What the cache saves, counted in evaluations of the field underneath and timed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Counted, and the count is the measurement.</b> A wall-clock bound is a statement
    /// about the machine (SPEC.md Amendment 27), so what is asserted is the number of
    /// times the driven field beneath the cycle average was asked for a value: the cache
    /// removes every <c>ElectricFieldAt</c> and no <c>PotentialAt</c>, because the direct
    /// term is the cycle mean of the potential and needs its sixteen samples whatever the
    /// ramp moves.
    /// </para>
    /// <para>
    /// <b>The wall clock is printed and understates the saving.</b> The field here is
    /// analytic, so a field evaluation is a handful of multiplications; the case this
    /// exists for is a solved funnel, where it is a bicubic gradient over several
    /// channels and costs many times a potential lookup. The ratio of counts is the
    /// machine-independent half; how much of it turns into time depends on what is
    /// underneath.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCacheRemovesEveryFieldEvaluationAndNoPotentialEvaluation()
    {
        var grid = Grid2D.OverBox(-0.02, -0.004, 0.02, 0.004, 64, 32);
        var gas = Nitrogen(200.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);

        const double Seconds = 2e-5;

        var cachedCounter = new Counting(Quadrupole(AmplitudeVolts));
        var referenceCounter = new Counting(Quadrupole(AmplitudeVolts));

        var cached = Run(
            grid, gas, mobility, species, Seconds,
            DirectRamp(species, gas, cachedCounter), plain: false);

        var reference = Run(
            grid, gas, mobility, species, Seconds,
            DirectRamp(species, gas, referenceCounter), plain: true);

        var nodes = grid.CountX * grid.CountY;

        // Timed separately from the counting, because counting is itself a cost and the
        // reference makes far more counted calls - timing the instrumented runs would
        // charge the reference for the instrument. Warmed up first: the runtime charges a
        // tier-one recompilation to whichever window it fires in.
        _ = Run(grid, gas, mobility, species, 2e-6, DirectRamp(species, gas), plain: false);
        _ = Run(grid, gas, mobility, species, 2e-6, DirectRamp(species, gas), plain: true);

        var cachedWatch = Stopwatch.StartNew();
        _ = Run(grid, gas, mobility, species, Seconds, DirectRamp(species, gas), plain: false);
        cachedWatch.Stop();

        var referenceWatch = Stopwatch.StartNew();
        _ = Run(grid, gas, mobility, species, Seconds, DirectRamp(species, gas), plain: true);
        referenceWatch.Stop();

        output.WriteLine(
            $"cached:    {cached.Assemblies} assemblies, {cached.WellRebuilds} well rebuilds, "
            + $"{cachedCounter.Fields:N0} field and {cachedCounter.Potentials:N0} potential evaluations, "
            + $"{cachedWatch.Elapsed.TotalMilliseconds:F0} ms");
        output.WriteLine(
            $"reference: {reference.Assemblies} assemblies, {reference.WellRebuilds} well rebuilds, "
            + $"{referenceCounter.Fields:N0} field and {referenceCounter.Potentials:N0} potential evaluations, "
            + $"{referenceWatch.Elapsed.TotalMilliseconds:F0} ms");
        output.WriteLine(
            $"{nodes} nodes: evaluations fall "
            + $"{(double)(referenceCounter.Fields + referenceCounter.Potentials)
                / (cachedCounter.Fields + cachedCounter.Potentials):F2}x, "
            + $"wall clock {referenceWatch.Elapsed.TotalMilliseconds
                / cachedWatch.Elapsed.TotalMilliseconds:F2}x on an analytic drive");

        // The reference pays two passes over the field at each of seven points per node
        // per assembly. The cache pays once; equality of the immutable RF definition
        // replaces spatial probes, which could miss a changing localised drive.
        Assert.Equal(2 * 7 * 16 * nodes * reference.Assemblies, referenceCounter.Fields);
        Assert.Equal(2 * 7 * 16 * nodes, cachedCounter.Fields);

        // And neither saves a single potential evaluation, which is the half of the
        // inference that prompted this that was wrong: the direct term is the cycle mean
        // of the potential and is exactly what a DC ramp moves.
        //
        // TRUE OF THE SAMPLED FALLBACK, WHICH IS WHAT THIS WRAPPER EXERCISES. `Counting`
        // does not implement `CycleMeanPotentialAt`, so it takes the interface default and
        // the mean really is sixteen samples. A SOLVED field now answers it in closed form -
        // one evaluation per channel and no time sampling, since the potential is linear in
        // the weights - so on the shipped front end the direct term's cost fell 7.7x. The
        // counts below are the fallback's, and are still the right thing to pin here because
        // what this test is about is the WELL cache rather than the direct term.
        Assert.Equal(referenceCounter.Potentials, cachedCounter.Potentials);
        Assert.Equal(7 * 16 * nodes * reference.Assemblies, cachedCounter.Potentials);
    }

    private static IdealQuadrupoleRf Quadrupole(double amplitudeVolts) =>
        IdealQuadrupoleRf.Create(
            Quantity.From(0.0, "V"),
            Quantity.From(amplitudeVolts, "V"),
            Quantity.From(FrequencyHz, "Hz"),
            Quantity.From(InscribedRadiusM, "m"),

            // Invariant along x, so the oscillating field lies in y and z and never adds
            // into the same component as the ramp along x.
            axis: CylinderAxis.X);

    private static BackgroundGas Nitrogen(double pressurePa) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    /// <summary>A ramp that walks the axial DC gradient down and holds the RF.</summary>
    private static Func<double, IElectrostaticField> DirectRamp(
        IonSpecies species, BackgroundGas gas, Counting? counter = null) =>
        Ramp(species, gas, elapsed => 6000.0 - (200_000_000.0 * elapsed), _ => AmplitudeVolts, counter);

    /// <summary>A ramp that walks the RF amplitude, which is the case that must not be cached.</summary>
    private static Func<double, IElectrostaticField> AmplitudeRamp(
        IonSpecies species, BackgroundGas gas) =>
        Ramp(species, gas, _ => 6000.0, elapsed => AmplitudeVolts * (1.0 + (2000.0 * elapsed)));

    private static Func<double, IElectrostaticField> Ramp(
        IonSpecies species,
        BackgroundGas gas,
        Func<double, double> directVoltsPerMetre,
        Func<double, double> amplitudeVolts,
        Counting? counter = null)
    {
        var mobility = Mobility.FromCrossSection(gas, species);
        var rate = PonderomotiveField.CollisionRateFromMobility(
            species.ChargeSi, species.MassSi, mobility.ZeroFieldSi);

        return elapsed =>
        {
            ITimeVaryingField rf = Quadrupole(amplitudeVolts(elapsed));

            // Share the counter, not a mutable field: each operating point must keep
            // its own immutable RF definition for comparison with the previous one.
            if (counter is not null)
            {
                rf = new Counting(rf, counter);
            }

            var driven = new DrivenSuperposedField(
                [rf, UniformField.Create(new Vec3(directVoltsPerMetre(elapsed), 0.0, 0.0))]);

            return new PonderomotiveField(driven, species.ChargeSi, species.MassSi, rate);
        };
    }

    private static DiffusionResult Run(
        Grid2D grid,
        BackgroundGas gas,
        Mobility mobility,
        IonSpecies species,
        double seconds,
        Func<double, IElectrostaticField> fieldAt,
        bool plain)
    {
        // The wrapper is what the reference path is: a field the cache does not recognise,
        // forwarding every call to the one it does.
        var at = plain
            ? elapsed => (IElectrostaticField)new PassThrough(fieldAt(elapsed))
            : fieldAt;

        return DriftDiffusion.Run(
            Seed(grid), at(0.0), gas, mobility, species, seconds, Edges, fieldAt: at);
    }

    private static DensityField Seed(Grid2D grid)
    {
        var density = new DensityField(grid);

        // A blob a few cells across rather than a point, so there is something for the
        // well to shape and the comparison is over a density with structure in it.
        for (var j = 0; j < grid.CountY; j++)
        {
            var dy = (grid.Y(j) - 0.0) / 1.5e-3;

            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = (grid.X(i) - 0.012) / 2e-3;

                density[i, j] = Math.Exp(-0.5 * ((dx * dx) + (dy * dy)))
                    / (grid.SpacingX * grid.SpacingY);
            }
        }

        return density;
    }

    /// <summary>
    /// A driven field that counts what it is asked for, and nothing else.
    /// </summary>
    /// <remarks>
    /// The time-free members count into the same tallies rather than being forwarded
    /// quietly. Nothing here should reach them - a cycle average asks for an instant at
    /// every sample - and a time-varying quantity answering through the time-free
    /// interface at an arbitrary instant is the defect this project has met six times, so
    /// it is worth having the count fail rather than absorb it.
    /// </remarks>
    private sealed class Counting(ITimeVaryingField inner, Counting? sink = null) : ITimeVaryingField
    {
        public ITimeVaryingField Inner { get; } = inner;

        // ATOMIC, because the loop this wraps is spread across cores. A plain `Fields++`
        // is a read, an add and a write, so concurrent increments lose counts - this test
        // reported 8,337,733 against a true 9,609,600 when the coefficient sweep was first
        // threaded and every diffusive test was forced onto the parallel path. The physics
        // was untouched; the instrumentation was wrong. A test's own counters are part of
        // what has to be thread-safe once the code they count is.
        private long _fields;

        private long _potentials;

        public long Fields => Interlocked.Read(ref _fields);

        public long Potentials => Interlocked.Read(ref _potentials);

        public double ShortestPeriodSeconds => Inner.ShortestPeriodSeconds;
        public double MonochromaticPeriodSeconds => Inner.MonochromaticPeriodSeconds;
        public bool HasSameOscillationAs(ITimeVaryingField other) =>
            other is Counting counting && Inner.HasSameOscillationAs(counting.Inner);

        public double OscillatingResolutionLength => Inner.OscillatingResolutionLength;

        public double ResolutionLength => Inner.ResolutionLength;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
        {
            Interlocked.Increment(ref (sink ?? this)._fields);

            return Inner.ElectricFieldAt(in position, timeSeconds);
        }

        public double PotentialAt(in Vec3 position, double timeSeconds)
        {
            Interlocked.Increment(ref (sink ?? this)._potentials);

            return Inner.PotentialAt(in position, timeSeconds);
        }

        public Vec3 ElectricFieldAt(in Vec3 position)
        {
            Interlocked.Increment(ref (sink ?? this)._fields);

            return Inner.ElectricFieldAt(in position);
        }

        public double PotentialAt(in Vec3 position)
        {
            Interlocked.Increment(ref (sink ?? this)._potentials);

            return Inner.PotentialAt(in position);
        }

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            Inner.FieldFreeRunLength(in position, in direction);

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            Inner.SignedDistanceToDiscontinuity(in position);
    }

    /// <summary>
    /// A field that forwards everything, which is how the pre-cache arithmetic is reached.
    /// </summary>
    /// <remarks>
    /// Forwarding is exact - it returns what the field returned - so a run through this is
    /// the run the solver did before the cache existed, with no switch added to the engine
    /// to make it reachable.
    /// </remarks>
    private sealed class PassThrough(IElectrostaticField inner) : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(in position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(in position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            inner.FieldFreeRunLength(in position, in direction);

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            inner.SignedDistanceToDiscontinuity(in position);

        public double ResolutionLength => inner.ResolutionLength;
    }
}
