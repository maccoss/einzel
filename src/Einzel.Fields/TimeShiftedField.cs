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
public sealed class TimeShiftedField : ITimeVaryingField
{
    private readonly ITimeVaryingField _inner;
    private readonly double _offsetSeconds;

    /// <summary>Wraps a field so t = 0 here is <paramref name="offsetSeconds"/> there.</summary>
    /// <param name="inner">The field as the instrument declares it.</param>
    /// <param name="offsetSeconds">Where this leg starts on the instrument's timeline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The offset is negative or not finite.</exception>
    public TimeShiftedField(ITimeVaryingField inner, double offsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetSeconds);

        if (!double.IsFinite(offsetSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offsetSeconds), offsetSeconds, "the offset must be a finite time");
        }

        _inner = inner;
        _offsetSeconds = offsetSeconds;
    }

    /// <summary>Where this leg starts on the instrument's timeline, in seconds.</summary>
    public double OffsetSeconds => _offsetSeconds;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
        _inner.ElectricFieldAt(position, timeSeconds + _offsetSeconds);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        _inner.PotentialAt(position, timeSeconds + _offsetSeconds);

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
    public double ShortestPeriodSeconds => _inner.ShortestPeriodSeconds;

    /// <inheritdoc/>
    /// <remarks>
    /// Shifted back into this leg's own clock, and never negative: a switch already
    /// behind the offset is not a switch this leg will meet. The integrator refuses to
    /// step past what this returns, so reporting a past instant would stall it.
    /// </remarks>
    public double NextSwitchAfter(double timeSeconds)
    {
        var next = _inner.NextSwitchAfter(timeSeconds + _offsetSeconds);

        return double.IsPositiveInfinity(next) ? next : next - _offsetSeconds;
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _inner.SignedDistanceToDiscontinuity(position);

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
        _inner.FieldFreeRunLength(position, direction);
}
