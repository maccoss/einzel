using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field seen from an instant part-way through the instrument's timeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The integrator always starts at t = 0</b>, and a leg of a sequenced run does not.
/// A trajectory phase resuming after a diffusive one begins at, say, 100 µs of the
/// instrument's timeline, and every time-varying quantity — an RF phase, a sequence
/// switch — has to be evaluated there rather than at the start of the run.
/// </para>
/// <para>
/// <b>A wrapper rather than a start time on the integrator</b>, which is this project's
/// own precedent: <c>AxisymmetricField</c> presents a half-plane solve as a field in
/// space, and <c>PonderomotiveField</c> presents a driven field as the cycle-averaged one
/// a slow ion feels, both without touching the transport core. That core carries every
/// validated number here, and refactoring it to add a case beside it is how those numbers
/// get quietly lost.
/// </para>
/// <para>
/// The arithmetic it costs is one addition per query, and what it buys is that a leg is
/// an ordinary integration: the caller adds the offset back to the reported flight time
/// and nothing inside the integrator knows a sequence exists.
/// </para>
/// </remarks>
public sealed class TimeShiftedField : ITimeVaryingField, IConductorBounded
{
    private readonly ITimeVaryingField _inner;
    private readonly double _offsetSeconds;

    // Where the leg ends on the instrument's timeline: a window of switches is read no further
    // than the first one past it.
    private readonly double _endsAtSeconds;

    // The window of switches this leg holds, on its own clock and the instrument's, in order: read
    // the first time a query needs it, and read again from where a query has gone past it, so an
    // evaluation is a binary search over at most a window's worth and never a scan of the
    // instrument. Read lazily because the ramped diffusive path builds a leg at every density step
    // and holds it at once, and the leg it builds is never asked; a held leg's inner field reports
    // no switch, and reading none allocates nothing. Null until read.
    private Window? _window;

    /// <summary>Wraps a field so t = 0 here is <paramref name="offsetSeconds"/> there.</summary>
    /// <param name="inner">The field as the instrument declares it.</param>
    /// <param name="offsetSeconds">Where this leg starts on the instrument's timeline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The offset is negative or not finite.</exception>
    public TimeShiftedField(ITimeVaryingField inner, double offsetSeconds)
        : this(inner, offsetSeconds, double.PositiveInfinity)
    {
    }

    /// <summary>Wraps a field for a leg that ends where it says, so t = 0 here is <paramref name="offsetSeconds"/> there.</summary>
    /// <param name="inner">The field as the instrument declares it.</param>
    /// <param name="offsetSeconds">Where this leg starts on the instrument's timeline.</param>
    /// <param name="endsAtSeconds">
    /// Where it ends on the instrument's timeline, or infinity for no end: a window of switches is
    /// read no further than the first one past it, so a leg of a long staircase reads its own few.
    /// An instant past them is still answered, by reading the next window.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The offset is negative or not finite, or the end is NaN or before the offset.
    /// </exception>
    public TimeShiftedField(ITimeVaryingField inner, double offsetSeconds, double endsAtSeconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetSeconds);

        // Answers ITimeVaryingField only, so a magnetic field wrapped here would lose its B
        // on the way to the integrator. A sequenced leg of a magnetic model never reaches
        // here (the validator refuses the diffusive phase that makes legs), and if one did
        // it must refuse rather than fly straight through the magnet.
        Magnetic.MagneticRefusal.ThrowIfMagnetic(inner, "a time-shifted sequence leg");

        if (!double.IsFinite(offsetSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offsetSeconds), offsetSeconds, "the offset must be a finite time");
        }

        if (double.IsNaN(endsAtSeconds) || endsAtSeconds < offsetSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endsAtSeconds), endsAtSeconds, "a leg ends at or after it starts");
        }

        _inner = inner;
        _offsetSeconds = offsetSeconds;
        _endsAtSeconds = endsAtSeconds;
    }

    /// <summary>A window of a leg's switches, in order, and the instants it answers for.</summary>
    /// <param name="Reported">Each switch as the leg reports it, distinct and increasing.</param>
    /// <param name="First">The first instrument boundary reported at that instant: what is before it.</param>
    /// <param name="Last">The last instrument boundary reported at that instant: what starts there.</param>
    /// <param name="From">The leg instant the window was read from; it answers for none earlier.</param>
    /// <param name="Complete">Whether the instrument has no switch past the window, which then answers for every later instant.</param>
    /// <param name="Collapsed">How many instrument switches fell on the same leg instant as the one before.</param>
    private sealed record Window(double[] Reported, double[] First, double[] Last, double From, bool Complete, int Collapsed)
    {
        /// <summary>No switch from the leg's start onward: shared, so a leg that meets none allocates nothing.</summary>
        public static readonly Window None = new([], [], [], 0.0, true, 0);

        /// <summary>Whether this window answers for a leg instant.</summary>
        /// <remarks>
        /// Short of the last switch held where more may follow: the next instrument boundary can
        /// report at that same leg instant, and only a window read from there holds them both.
        /// </remarks>
        public bool Answers(double timeSeconds) =>
            timeSeconds >= From && (Complete || timeSeconds < Reported[^1]);
    }

    /// <summary>The window that answers for a leg instant, read where none held does.</summary>
    /// <remarks>
    /// Thread-safe without a lock: a window is immutable, the field holding it is replaced whole,
    /// and two threads reading one each publish a window that answers for what they asked.
    /// </remarks>
    private Window WindowFor(double timeSeconds)
    {
        var held = Volatile.Read(ref _window);

        if (held is not null && held.Answers(timeSeconds))
        {
            return held;
        }

        // The first window is read from the leg's start, which answers for every instant a leg has.
        var read = Read(_inner, _offsetSeconds, _endsAtSeconds, held is null ? Math.Min(0.0, timeSeconds) : timeSeconds);
        Volatile.Write(ref _window, read);

        if (read.Collapsed > 0)
        {
            Volatile.Write(ref _collapsed, true);
        }

        return read;
    }

    /// <summary>Whether any window this leg read held two instrument switches at one leg instant.</summary>
    /// <remarks>
    /// Two instrument boundaries closer together than the leg's own clock can tell apart report at
    /// one leg instant. The leg cannot stop between them, so at that instant it is given the state
    /// before the first and after the last: the phase between them lasts less than one tick of the
    /// leg's clock, and is said here rather than skipped without a word.
    /// </remarks>
    public bool HasCollapsedSwitches => Volatile.Read(ref _collapsed);

    // Set when a window read holds a collapsed switch, and never cleared.
    private bool _collapsed;

    /// <summary>Reads a window of switches from a leg instant onward.</summary>
    /// <param name="inner">The field as the instrument declares it.</param>
    /// <param name="offsetSeconds">Where the leg starts on the instrument's timeline.</param>
    /// <param name="endsAtSeconds">Where it ends there; reading stops at the first switch past it.</param>
    /// <param name="fromSeconds">The leg instant to read from.</param>
    /// <returns>The window.</returns>
    /// <remarks>
    /// From the first boundary after a few ulps before the instant, which is as far as a
    /// subtraction and an addition can move a reported switch from the boundary it means.
    /// </remarks>
    private static Window Read(ITimeVaryingField inner, double offsetSeconds, double endsAtSeconds, double fromSeconds)
    {
        var boundary = inner.NextSwitchAfter(Before(fromSeconds + offsetSeconds));

        if (!double.IsFinite(boundary))
        {
            return fromSeconds <= 0.0 ? Window.None : new Window([], [], [], fromSeconds, true, 0);
        }

        var reported = new List<double>();
        var first = new List<double>();
        var last = new List<double>();
        var collapsed = 0;
        var complete = false;

        while (true)
        {
            var at = boundary - offsetSeconds;

            if (reported.Count > 0 && at == reported[^1])
            {
                last[^1] = boundary;
                collapsed++;
            }
            else
            {
                reported.Add(at);
                first.Add(boundary);
                last.Add(boundary);
            }

            // The first switch past the leg's end is the last it can be asked about; a window is
            // no more than a window, and the next is read when an instant passes it. Either way
            // not before a switch past the instant read from, so the window answers for it.
            if ((boundary > endsAtSeconds || reported.Count >= WindowSize) && at > fromSeconds)
            {
                break;
            }

            var next = inner.NextSwitchAfter(boundary);

            if (!double.IsFinite(next) || !(next > boundary))
            {
                complete = true;
                break;
            }

            boundary = next;
        }

        return new Window(reported.ToArray(), first.ToArray(), last.ToArray(), fromSeconds, complete, collapsed);
    }

    /// <summary>The most switches a window holds.</summary>
    private const int WindowSize = 4096;

    /// <summary>Where this leg starts on the instrument's timeline, in seconds.</summary>
    public double OffsetSeconds => _offsetSeconds;

    /// <inheritdoc/>
    /// <remarks>
    /// On a switch this leg reported, the instrument's own boundary - the starting phase - and
    /// otherwise the leg's instant plus the offset, bit-identical to what it always was.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
        _inner.ElectricFieldAt(position, OnTheInstrument(timeSeconds, before: false));

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        _inner.PotentialAt(position, OnTheInstrument(timeSeconds, before: false));

    /// <inheritdoc/>
    /// <remarks>
    /// The switch is handed on as the instrument's own boundary, so the inner field compares it
    /// by identity rather than receiving a sum that may round to either side of it.
    /// </remarks>
    public Vec3 ElectricFieldBefore(in Vec3 position, double switchSeconds) =>
        _inner.ElectricFieldBefore(position, OnTheInstrument(switchSeconds, before: true));

    /// <inheritdoc/>
    public double PotentialBefore(in Vec3 position, double switchSeconds) =>
        _inner.PotentialBefore(position, OnTheInstrument(switchSeconds, before: true));

    /// <summary>
    /// A leg's instant on the instrument's clock: the instrument's boundary itself where the
    /// instant is a switch this leg reported, the instant plus the offset anywhere else.
    /// </summary>
    /// <param name="timeSeconds">The instant on this leg's clock.</param>
    /// <param name="before">
    /// Whether the phase ending there is wanted: where several instrument boundaries report at the
    /// instant, the first of them, and the last for the phase starting.
    /// </param>
    /// <returns>The instant on the instrument's.</returns>
    /// <remarks>
    /// <b>By identity, not by distance.</b> This leg reports a switch as <c>boundary - offset</c>,
    /// so a leg's instant is a switch exactly when it equals that expression for some boundary -
    /// an equality, which picks the right one of two boundaries however close together they lie.
    /// Adding the offset back instead lands an ulp past the boundary for some pairs (a leg from
    /// 1.40141743e-6 to 3.00152224e-5 s), which the inner field reads as the next phase.
    /// </remarks>
    private double OnTheInstrument(double timeSeconds, bool before)
    {
        var window = WindowFor(timeSeconds);
        var at = SortedInstants.FirstAtOrAfter(window.Reported, timeSeconds);

        return at < window.Reported.Length && window.Reported[at] == timeSeconds
            ? (before ? window.First[at] : window.Last[at])
            : timeSeconds + _offsetSeconds;
    }

    /// <summary>A few ulps before an instant: as far as a leg's instant can sit from the boundary it means.</summary>
    /// <param name="instant">The instant on the instrument's clock.</param>
    /// <returns>The instant moved back that far.</returns>
    private static double Before(double instant)
    {
        for (var k = 0; k < Ulps; k++)
        {
            instant = Math.BitDecrement(instant);
        }

        return instant;
    }

    /// <summary>How many ulps before an instant the boundaries are looked for.</summary>
    private const int Ulps = 16;

    /// <inheritdoc/>
    /// <remarks>
    /// The instant this leg starts, which is a stated instant rather than an accidental
    /// one — unlike a driven field answering the time-free interface at whatever t = 0
    /// happens to mean, which is the defect this project has found four times.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        _inner.ElectricFieldAt(position, _offsetSeconds);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) =>
        _inner.PotentialAt(position, _offsetSeconds);

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;

    /// <inheritdoc/>
    public double OscillatingResolutionLength => _inner.OscillatingResolutionLength;

    /// <inheritdoc/>
    public double ShortestPeriodSeconds => _inner.ShortestPeriodSeconds;

    /// <inheritdoc/>
    public double SignedDistanceToConductor(in Vec3 position) => _inner is IConductorBounded bounded
        ? bounded.SignedDistanceToConductor(in position) : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) => _inner is IConductorBounded bounded
        ? bounded.ConductorAt(in position) : null;

    /// <inheritdoc/>
    public double MonochromaticPeriodSeconds => _inner.MonochromaticPeriodSeconds;

    /// <inheritdoc/>
    public bool HasSameOscillationAs(ITimeVaryingField other) =>
        other is TimeShiftedField shifted && _offsetSeconds == shifted._offsetSeconds
        && _inner.HasSameOscillationAs(shifted._inner);

    /// <summary>Compares complete monochromatic cycles, allowing a common time shift.</summary>
    /// <param name="other">The other complete field, not a member of a superposition.</param>
    /// <returns>Whether a full-cycle mean square is unchanged.</returns>
    public bool HasSameCycleAs(TimeShiftedField other) =>
        double.IsFinite(MonochromaticPeriodSeconds)
        && _inner.HasSameOscillationAs(other._inner);

    /// <inheritdoc/>
    /// <remarks>
    /// The window opens at this leg's own instant, so the offset is added and the inner
    /// field answers on the instrument's clock. Delegating rather than sampling is what
    /// lets the saving survive a sequenced leg, which is every ramped diffusive phase.
    /// </remarks>
    public double CycleMeanPotentialAt(
        in Vec3 position, double fromSeconds, double periodSeconds, int samples) =>
        _inner.CycleMeanPotentialAt(
            in position, fromSeconds + _offsetSeconds, periodSeconds, samples);

    /// <inheritdoc/>
    /// <remarks>
    /// The instant is on the instrument's timeline, the same one this wrapper's offset is
    /// measured on, so it passes through unshifted. Returns this instance where the inner
    /// field has nothing to hold, which keeps an unsequenced leg bit-identical.
    /// </remarks>
    public ITimeVaryingField AtOperatingPoint(double timeSeconds)
    {
        var held = _inner.AtOperatingPoint(timeSeconds);

        return ReferenceEquals(held, _inner) ? this : new TimeShiftedField(held, _offsetSeconds, _endsAtSeconds);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Shifted back into this leg's own clock, and never negative: a switch already
    /// behind the offset is not a switch this leg will meet. The integrator refuses to
    /// step past what this returns, so reporting a past instant would stall it.
    /// <para>
    /// Strictly after the leg's instant on the leg's own clock, the same arithmetic it is reported
    /// in: a boundary whose reported value is not past the instant - the one just landed on,
    /// whatever adding the offset back makes of it - is not the next one, so landing on a switch
    /// moves the leg on to the one after rather than stalling at the one it is on.
    /// </para>
    /// </remarks>
    public double NextSwitchAfter(double timeSeconds)
    {
        var window = WindowFor(timeSeconds);
        var after = SortedInstants.FirstAfter(window.Reported, timeSeconds);

        return after < window.Reported.Length ? window.Reported[after] : double.PositiveInfinity;
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _inner.SignedDistanceToDiscontinuity(position);

    /// <inheritdoc/>
    public int DiscontinuityMembers => _inner.DiscontinuityMembers;

    /// <inheritdoc/>
    public double MemberDistanceToDiscontinuity(int member, in Vec3 position) =>
        _inner.MemberDistanceToDiscontinuity(member, position);

    /// <inheritdoc/>
    public void MemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances) =>
        _inner.MemberDistancesToDiscontinuity(position, distances);

    /// <inheritdoc/>
    public void DiscontinuityMemberGates(Span<int> gates) => _inner.DiscontinuityMemberGates(gates);

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
        _inner.FieldFreeRunLength(position, direction);
}
