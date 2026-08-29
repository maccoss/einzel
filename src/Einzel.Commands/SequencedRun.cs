using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>What one phase of a sequenced run did.</summary>
/// <param name="Name">The phase's name.</param>
/// <param name="Mode">The transport mode it ran in.</param>
/// <param name="DurationSeconds">How long it lasted.</param>
/// <param name="EndsAtSeconds">When it ended, on the instrument's timeline.</param>
/// <param name="Population">
/// How many real ions the packet held when the phase ended.
/// </param>
/// <param name="Trajectories">
/// How many trajectories carried them, or zero in a diffusive phase where there are none.
/// </param>
/// <param name="CentroidMm">Where the packet was when the phase ended, in millimetres.</param>
/// <param name="Converted">
/// Whether the packet was converted into this phase's description at its start.
/// </param>
/// <param name="Arrived">Trajectories that reached the detector during this phase.</param>
/// <param name="Losses">
/// Trajectories that left another way, by the surface the model author named.
/// </param>
/// <remarks>
/// <para>
/// <b>Every trajectory is accounted for within a phase</b>, which ACC-5 requires and
/// which a leg bounded by time makes easy to get wrong: it ends with some still flying,
/// some arrived and some struck, and only the first group is handed on. Keeping just
/// those would make the packet shrink between phases with nothing saying where the rest
/// went.
/// </para>
/// <para>
/// <b>The ledger closes within a phase and not across a conversion</b>, and that is
/// physics rather than bookkeeping. A density is re-sampled into however many
/// trajectories are asked for, so the trajectory count is a numerical choice on the far
/// side of a boundary while the <em>population</em> - the real ions - is what carries
/// across. That is exactly the <c>ions</c> against <c>population</c> distinction the
/// space-charge work already had to draw, met again from the other direction.
/// </para>
/// </remarks>
public sealed record PhaseOutcome(
    string Name,
    string Mode,
    double DurationSeconds,
    double EndsAtSeconds,
    double Population,
    int Trajectories,
    IReadOnlyList<double> CentroidMm,
    bool Converted,
    int Arrived,
    IReadOnlyList<LossChannel> Losses);

/// <summary>What a run across a changing transport mode did.</summary>
/// <param name="Phases">Each phase, in order.</param>
/// <param name="Conversions">How many mode boundaries the packet crossed.</param>
/// <param name="Warnings">
/// Everything the run and its conversions had to say, per GRD-2.
/// </param>
/// <param name="Arrived">Real ions that reached the detector, over every phase.</param>
/// <param name="Losses">
/// Every other way ions left, by named surface, in real ions.
/// </param>
/// <remarks>
/// In <em>ions</em> rather than trajectories, because a conversion re-samples and the
/// trajectory count on the far side of one is a numerical choice. Weighted by what each
/// trajectory stood for at the phase it left in, so the totals are comparable across a
/// boundary where the counts are not.
/// </remarks>
public sealed record SequencedOutcome(
    IReadOnlyList<PhaseOutcome> Phases,
    int Conversions,
    IReadOnlyList<ValidityWarning> Warnings,
    double Arrived,
    IReadOnlyList<WeightedLoss> Losses);

/// <summary>Ions lost one way, in real ions rather than in trajectories.</summary>
/// <param name="Surface">Where they went, named as the model author named it.</param>
/// <param name="Ions">How many real ions.</param>
public sealed record WeightedLoss(string Surface, double Ions);

/// <summary>
/// A run whose phases are not all in the same transport description (SEQ-1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The instrument this exists for is ordinary.</b> Ions are collected and thermalised
/// in a gas-filled trap, where the right description is a density, and then extracted
/// into vacuum and flown, where it is trajectories. The two modes have been peers since
/// REG-1's seam was built; what was missing was a run that could hold both.
/// </para>
/// <para>
/// <b>Each phase is an ordinary run of its own mode</b>, over the phase's duration, and
/// the orchestration is the boundaries between them. A trajectory leg starting part-way
/// along the timeline is flown against a <see cref="TimeShiftedField"/>, so the
/// integrator - which always starts at t = 0 - sees the instrument at the instant the leg
/// actually begins, and nothing inside it knows a sequence exists.
/// </para>
/// <para>
/// <b>A boundary where the mode changes is where SEQ-1's uncertainty lives.</b> Going one
/// way discards the velocity distribution entirely, because a density has nowhere to
/// hold one; going the other invents it, because a density says nothing about how fast
/// anything is moving. Both are non-suppressible on the result.
/// </para>
/// </remarks>
public static class SequencedRun
{
    /// <summary>Runs a model whose phases change transport mode.</summary>
    /// <param name="model">The model, whose phases carry the modes.</param>
    /// <param name="field">The assembled field.</param>
    /// <param name="gas">The gas, resolved so an imported field reaches both modes.</param>
    /// <returns>What each phase did, and what the conversions cost.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">The model cannot be run this way.</exception>
    public static SequencedOutcome Execute(
        CompiledModel model, IElectrostaticField field, BackgroundGas gas)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(gas);

        if (model.Phases.Count == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "a sequenced run needs a sequence, and this model declares none",
                Suggestion = "add a \"sequence\", or run the model in its declared transport "
                    + "mode with einzel run",
            });
        }

        var species = IonSpecies.FromModel(model);
        var warnings = new List<ValidityWarning>();
        var outcomes = new List<PhaseOutcome>(model.Phases.Count);
        var conversions = 0;

        // Every ion leaves the packet exactly once, and these are where it goes - in
        // real ions, because a conversion re-samples and a trajectory count on the far
        // side of one is a numerical choice rather than a quantity of anything.
        var arrivedTotal = 0.0;
        var lostTotal = new Dictionary<string, double>(StringComparer.Ordinal);

        // What one trajectory stands for. It starts as the declared population spread
        // over the launched cloud, and a conversion back from a density re-derives it
        // from the population that survived.
        var perTrajectory = Population(model) / Math.Max(model.Cloud.Ions, 1);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        // The packet, in whichever description the current phase uses. Exactly one of
        // these is live at a time - that is what a transport mode *is* - and the
        // conversion at a boundary is what swaps which.
        PhaseState[]? states = null;
        DensityField? density = null;

        var started = 0.0;

        for (var i = 0; i < model.Phases.Count; i++)
        {
            var phase = model.Phases[i];
            var trajectory = string.Equals(phase.Mode, "trajectory", StringComparison.Ordinal);
            var converted = false;

            // Enter the phase in its own description, converting if the packet is in
            // the other one. The first phase has nothing to convert from: it starts
            // from the source.
            if (trajectory && states is null)
            {
                if (density is null)
                {
                    states = IonCloud.Draw(
                        Launch(model), species, model.Cloud, model.SourceDirection);
                }
                else
                {
                    var back = PacketConversion.ToTrajectories(
                        density,
                        Math.Max(model.Cloud.Ions, 1),
                        species,
                        gas,
                        Instant(field, started),
                        Mobility(model, gas, species),
                        model.Cloud.Seed);

                    states = back.States;
                    perTrajectory = back.PopulationPerIon;
                    warnings.AddRange(back.Warnings);
                    density = null;
                    converted = true;
                    conversions++;
                }
            }
            else if (!trajectory && density is null)
            {
                if (states is null)
                {
                    // A trap-then-extract instrument starts in the trap, so the first
                    // phase being diffusive is the ordinary case rather than a corner.
                    // Seeded by the same function `einzel run` uses for a wholly
                    // diffusive model - one implementation, because `run` and `test`
                    // once computed one flight time two ways and disagreed by 1.3e-10.
                    density = DiffusionRun.Seed(
                        model, DiffusionRun.GridFor(model), Cylindrical(model));
                }
                else
                {
                    var forward = PacketConversion.ToDensity(
                        states, states.Length * perTrajectory,
                        DiffusionRun.GridFor(model), Cylindrical(model));

                    density = forward.Density;
                    warnings.AddRange(forward.Warnings);
                    states = null;
                    converted = true;
                    conversions++;
                }
            }

            // Run the phase in its own mode, for its own duration.
            if (trajectory)
            {
                var (flying, arrived, lost) = Fly(
                    states!, species, field, detector, started, phase.DurationSeconds, model);

                states = flying;
                arrivedTotal += arrived * perTrajectory;

                foreach (var (channel, count) in lost)
                {
                    lostTotal[channel] =
                        lostTotal.GetValueOrDefault(channel) + (count * perTrajectory);
                }

                outcomes.Add(new PhaseOutcome(
                    phase.Name, phase.Mode, phase.DurationSeconds, phase.EndsAtSeconds,
                    states.Length * perTrajectory, states.Length,
                    Centroid(states), converted,
                    arrived,
                    [.. lost.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new LossChannel(pair.Key, pair.Value))]));
            }
            else
            {
                density = Diffuse(density!, model, field, gas, species, started, phase);

                var (cx, cy) = density.Centroid();

                // A density's losses are the solver's own ledger, in ions rather than
                // in counts, and folding them into a trajectory tally would add two
                // different quantities. Reported as the population that survives the
                // phase instead, which is what the next conversion will carry.
                outcomes.Add(new PhaseOutcome(
                    phase.Name, phase.Mode, phase.DurationSeconds, phase.EndsAtSeconds,
                    density.Population(), 0, [cx * 1e3, cy * 1e3], converted, 0, []));
            }

            started = phase.EndsAtSeconds;
        }

        if (conversions > 0)
        {
            warnings.Add(new ValidityWarning(
                "transport.mode-changed-in-sequence",
                $"the packet crossed {conversions} boundary where the transport mode "
                + "changed. Each crossing is a change of description rather than of "
                + "instrument, and neither direction is lossless: read the conversion "
                + "warnings above before comparing anything across one.",
                WarningSeverity.ValidityViolation));
        }

        return new SequencedOutcome(
            outcomes,
            conversions,
            warnings,
            arrivedTotal,
            [.. lostTotal.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new WeightedLoss(pair.Key, pair.Value))]);
    }

    /// <summary>The field as a leg starting part-way along the timeline sees it.</summary>
    /// <remarks>
    /// The integrator starts at t = 0, so a leg beginning at 100 us has to be handed an
    /// instrument shifted by 100 us rather than a start time it has nowhere to put. A
    /// static field needs no shift and is passed through, which keeps an unsequenced
    /// element bit-identical.
    /// </remarks>
    private static IElectrostaticField Instant(IElectrostaticField field, double atSeconds) =>
        field is ITimeVaryingField driven && atSeconds > 0.0
            ? new TimeShiftedField(driven, atSeconds)
            : field;

    private static PhaseState Launch(CompiledModel model) =>
        new(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

    /// <summary>How many real ions the packet holds.</summary>
    /// <remarks>
    /// `population` when declared, and the trajectory count otherwise - the conservative
    /// reading the space-charge screen already established, so a dense packet is never
    /// silently sparse.
    /// </remarks>
    private static double Population(CompiledModel model) =>
        model.Cloud.Population is { } declared and > 0
            ? declared
            : Math.Max(model.Cloud.Ions, 1);

    private static bool Cylindrical(CompiledModel model) =>
        model.Fields.Any(f => f.Solve?.Symmetry == SolveSymmetry.Cylindrical);

    /// <summary>The mobility this run drifts at.</summary>
    /// <remarks>
    /// <b>`Derived` is the part that is easy to drop.</b> A mobility the document derived
    /// from a cross section carries a stored zero-field value that is not the one to use -
    /// the derivation has to be redone against the gas actually resolved for this run.
    /// A first version of this helper read the stored value unconditionally, which is a
    /// different mobility from the one `einzel run` uses on the same model.
    /// </remarks>
    private static Mobility Mobility(
        CompiledModel model, BackgroundGas gas, IonSpecies species) =>
        model.Mobility is { Derived: false } declared
            ? new Mobility(declared.ZeroFieldSi, declared.Alpha, declared.ValidToTownsend)
            : Transport.Diffusion.Mobility.FromCrossSection(gas, species);

    private static double[] Centroid(PhaseState[] states)
    {
        if (states.Length == 0)
        {
            return [0.0, 0.0];
        }

        var x = states.Average(s => s.Position.X);
        var y = states.Average(s => s.Position.Y);

        return [x * 1e3, y * 1e3];
    }

    /// <summary>Flies the packet for one phase, and keeps whatever is still in flight.</summary>
    /// <remarks>
    /// Bounded by the phase rather than by a detector: what the next phase needs is where
    /// the packet is when this one ends. An ion that leaves - reaching a detector or
    /// striking metal - simply is not in the list handed on, which is the same accounting
    /// the ensemble figures already use.
    /// </remarks>
    private static (PhaseState[] Flying, int Arrived, Dictionary<string, int> Lost) Fly(
        PhaseState[] states,
        IonSpecies species,
        IElectrostaticField field,
        TrajectoryStopFunction detector,
        double startedAt,
        double durationSeconds,
        CompiledModel model)
    {
        var seen = Instant(field, startedAt);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = durationSeconds,
        };

        var flying = new List<PhaseState>(states.Length);
        var lost = new Dictionary<string, int>(StringComparer.Ordinal);
        var arrived = 0;

        foreach (var state in states)
        {
            var flight = TrajectoryIntegrator.Integrate(state, species, seen, settings, detector);

            // Three outcomes, and all three are accounted for. An ion that ran the whole
            // phase is handed to the next one; one that reached the detector arrived; one
            // that left any other way is itemised by the surface the model author named.
            //
            // ACC-5 wants every launched ion to appear exactly once, and a leg bounded by
            // time is where that is easiest to get wrong: keeping only the survivors makes
            // the packet shrink between phases with nothing saying where it went.
            switch (flight.Outcome)
            {
                case TrajectoryOutcome.MaximumFlightTimeReached:
                    flying.Add(flight.FinalState);
                    break;

                case TrajectoryOutcome.StopConditionMet:
                    arrived++;
                    break;

                default:
                    var channel = flight.Outcome == TrajectoryOutcome.StruckElectrode
                        ? flight.StruckSurface ?? "an electrode"
                        : flight.Outcome.ToString();

                    lost[channel] = lost.GetValueOrDefault(channel) + 1;
                    break;
            }
        }

        return ([.. flying], arrived, lost);
    }

    /// <summary>Steps the density for one phase.</summary>
    private static DensityField Diffuse(
        DensityField density,
        CompiledModel model,
        IElectrostaticField field,
        BackgroundGas gas,
        IonSpecies species,
        double startedAt,
        CompiledPhase phase)
    {
        var grid = DiffusionRun.GridFor(model);

        // The electrodes absorb during a diffusive phase exactly as they do in a wholly
        // diffusive run. Leaving them out would let a density pass through metal, which
        // is the defect that made every diffusive transmission an upper bound with
        // nothing saying so.
        var (absorbers, _) = DiffusionRun.Absorb(model, grid, density);

        var mobility = Mobility(model, gas, species);

        // A driven geometry has no static field to step a density through, and the
        // time-free interface would answer with the RF at this phase's first instant -
        // a field that exists for no length of time. What a slow ion in a gas feels is
        // the cycle average, and `Effective` is the same wrapper the wholly diffusive
        // path uses.
        //
        // Stepping through a snapshot is what this did before, which is the FIFTH time
        // in this project a time-varying quantity reached through a time-free interface
        // has answered at an arbitrary instant rather than failing.
        var seen = Instant(field, startedAt);
        _ = DiffusionRun.Effective(ref seen, species, mobility, gas);

        var result = DriftDiffusion.Run(
            density,
            seen,
            gas,
            mobility,
            species,
            phase.DurationSeconds,
            DiffusionRun.EdgesFor(model, grid),
            absorbers);

        return result.Density;
    }
}
