using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// One element, switched between compiled states by the instrument's timeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The generic form of what a sequence does to an element.</b> A solved geometry
/// carries its phases inside <c>DrivenSolvedField</c>, because there the phases are
/// re-weightings of channels that are already solved and nothing else changes. An
/// analytic element has no channels to re-weight — a phase simply gives it different
/// numbers — so it needs a switch rather than a weighting, and this is it.
/// </para>
/// <para>
/// Written once here rather than taught to each analytic kind. A uniform field, a
/// half-space and anything added later all switch the same way, and the alternative is a
/// per-phase branch inside every one of them: special cases layered on shared
/// infrastructure, which is the shape a fix takes when it is not deep enough.
/// </para>
/// <para>
/// <b>The last phase holds after the sequence ends</b>, which is the rule the solved path
/// already enforces and is a physics statement rather than a bookkeeping one: an
/// instrument left alone stays where it was put, and a field that switched off would make
/// every ion still in flight suddenly coast.
/// </para>
/// </remarks>
public sealed class SequencedField : ITimeVaryingField
{
    private readonly IReadOnlyList<IElectrostaticField> _states;
    private readonly IReadOnlyList<double> _boundaries;

    /// <summary>Wraps one element's per-phase states.</summary>
    /// <param name="states">The element as it stands during each phase, in order.</param>
    /// <param name="boundaries">
    /// The instant each phase ends, cumulative from zero and strictly increasing.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are no states, or the counts disagree, or the boundaries do not increase.
    /// </exception>
    public SequencedField(
        IReadOnlyList<IElectrostaticField> states, IReadOnlyList<double> boundaries)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(boundaries);

        if (states.Count == 0)
        {
            throw new ArgumentException("a sequenced field needs at least one state", nameof(states));
        }

        if (states.Count != boundaries.Count)
        {
            throw new ArgumentException(
                $"a sequenced field needs one boundary per state, and was given "
                + $"{states.Count} states against {boundaries.Count} boundaries",
                nameof(boundaries));
        }

        for (var i = 1; i < boundaries.Count; i++)
        {
            if (boundaries[i] <= boundaries[i - 1])
            {
                throw new ArgumentException(
                    "phase boundaries are cumulative and must increase, and "
                    + $"boundary {i} at {boundaries[i]} s does not exceed "
                    + $"{boundaries[i - 1]} s",
                    nameof(boundaries));
            }
        }

        _states = states;
        _boundaries = boundaries;
    }

    /// <summary>The state that holds at an instant.</summary>
    /// <remarks>
    /// A time exactly on a boundary belongs to the phase that is starting, not the one
    /// that is ending — the integrator lands exactly on switch instants by design, so
    /// which side of the comparison it falls on is a real decision rather than a
    /// tie-break. Starting is the right one: the switch has happened.
    /// </remarks>
    private IElectrostaticField At(double timeSeconds)
    {
        for (var i = 0; i < _boundaries.Count; i++)
        {
            if (timeSeconds < _boundaries[i])
            {
                return _states[i];
            }
        }

        return _states[^1];
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
        At(timeSeconds).ElectricFieldAt(position);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        At(timeSeconds).PotentialAt(position);

    /// <inheritdoc/>
    /// <remarks>
    /// A time-free caller gets the first phase, which is the instrument as it starts.
    /// This is the interface a driven field answers at an arbitrary instant without
    /// failing — the defect this project has now found four times — so a caller that
    /// reaches an element through it gets a stated instant rather than an accidental one.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => _states[0].ElectricFieldAt(position);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) => _states[0].PotentialAt(position);

    /// <inheritdoc/>
    public double ResolutionLength => _states.Min(s => s.ResolutionLength);

    /// <inheritdoc/>
    /// <remarks>
    /// A sequence is not periodic, so there is no shortest period to report. The step
    /// control that matters here is landing on the switches, which is what
    /// <see cref="NextSwitchAfter"/> is for.
    /// </remarks>
    public double ShortestPeriodSeconds => double.PositiveInfinity;

    /// <inheritdoc/>
    /// <remarks>
    /// The integrator refuses to step past this, so a switch needs no root-find: unlike a
    /// boundary in space, the time is known in advance.
    /// </remarks>
    public double NextSwitchAfter(double timeSeconds)
    {
        foreach (var boundary in _boundaries)
        {
            if (boundary > timeSeconds)
            {
                return boundary;
            }
        }

        return double.PositiveInfinity;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Taken from the first state. Every phase of one element is the same geometry with
    /// different numbers on it — the validator refuses a phase that moves metal — so the
    /// discontinuity surfaces are the same throughout.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _states[0].SignedDistanceToDiscontinuity(position);

    /// <inheritdoc/>
    /// <remarks>
    /// Zero rather than the first state's answer. A field-free run length is a promise
    /// about a whole straight segment, and a switch part-way along it would break that
    /// promise silently; the analytic drift is an optimisation, so giving it up is a cost
    /// in speed and never in accuracy.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;
}
