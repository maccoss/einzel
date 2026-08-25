using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;

namespace Einzel.Fields.Solved;

/// <summary>
/// A solved geometry whose electrodes are driven, presented as a field in time.
/// </summary>
/// <remarks>
/// <para>
/// RF on real geometry costs almost nothing beyond the static case, because
/// <em>basis superposition already was the mechanism</em>. The field is linear in
/// the applied potentials, so solving once per independent channel at unit
/// potential and then making the weights functions of time is RF, with nothing
/// re-solved and no time-stepping of the Poisson equation at all.
/// </para>
/// <para>
/// Channels rather than electrodes. SYM-1 makes the point in passing - "a 200-ring
/// funnel driven in two RF phases needs two RF basis fields plus a DC gradient, not
/// 200 basis solutions" - and it generalises: electrodes whose potentials are the
/// same function of time, or exact negatives of one another, share a basis. A
/// quadrupole's two pairs are negatives, so the whole device is <em>one</em> solve;
/// a driven geometry with a grounded housing costs one more for the static part.
/// </para>
/// <para>
/// What this does not do is group electrodes whose potentials are merely
/// <em>proportional</em> - a resistor chain down a funnel, where each ring holds a
/// different fraction of the same supply. Those still cost one solve each. That is
/// the remaining piece before a 200-ring funnel is practical.
/// </para>
/// </remarks>
public sealed class DrivenSolvedField : ITimeVaryingField, IConductorBounded
{
    private readonly IElectrostaticField[] _channels;
    private readonly double[] _direct;
    private readonly double[] _amplitude;
    private readonly double[] _phase;
    private readonly RfWaveform _waveform;

    internal DrivenSolvedField(
        IReadOnlyList<IElectrostaticField> channels,
        IReadOnlyList<double> direct,
        IReadOnlyList<double> amplitude,
        IReadOnlyList<double> phase,
        double frequencyHz,
        RfWaveform waveform)
    {
        _channels = [.. channels];
        _direct = [.. direct];
        _amplitude = [.. amplitude];
        _phase = [.. phase];
        _waveform = waveform;

        FrequencyHz = frequencyHz;
    }

    /// <summary>The drive frequency, in hertz.</summary>
    public double FrequencyHz { get; }

    /// <summary>How many basis solves the geometry reduced to.</summary>
    /// <remarks>
    /// Reported because it is the number that decides what a driven geometry costs,
    /// and because a template author who expected two and got two hundred has
    /// written a document whose electrodes do not share their time dependence.
    /// </remarks>
    public int ChannelCount => _channels.Length;

    /// <inheritdoc/>
    public double ShortestPeriodSeconds => 1.0 / FrequencyHz;

    /// <inheritdoc/>
    public double ResolutionLength => _channels[0].ResolutionLength;

    /// <summary>The potential a channel holds at an instant, in volts.</summary>
    /// <param name="channel">Channel index.</param>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The potential.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No such channel.</exception>
    public double WeightAt(int channel, double timeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, _channels.Length);

        return Weight(channel, timeSeconds);
    }

    private double Weight(int channel, double timeSeconds) =>
        _direct[channel]
        + (_amplitude[channel] == 0.0
            ? 0.0
            : _amplitude[channel] * _waveform.At((FrequencyHz * timeSeconds) + _phase[channel]));

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        var total = Vec3.Zero;

        for (var k = 0; k < _channels.Length; k++)
        {
            var weight = Weight(k, timeSeconds);

            if (weight != 0.0)
            {
                total += _channels[k].ElectricFieldAt(in position) * weight;
            }
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var total = 0.0;

        for (var k = 0; k < _channels.Length; k++)
        {
            var weight = Weight(k, timeSeconds);

            if (weight != 0.0)
            {
                total += _channels[k].PotentialAt(in position) * weight;
            }
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>The field at the start of the cycle.</remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>The potential at the start of the cycle.</remarks>
    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>
    /// Zero, always. Every channel shares one grid, so a region that is field-free
    /// in one is field-free in all - but only at this instant, and the guarantee
    /// this returns is about a whole run. A driven field has no field-free regions
    /// that stay that way.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _channels[0].SignedDistanceToDiscontinuity(in position);

    /// <inheritdoc/>
    /// <remarks>
    /// Every channel is the same geometry at different potentials, so the metal is
    /// in the same place in all of them and the first is as good as any.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position) =>
        _channels[0] is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(in position)
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        _channels[0] is IConductorBounded bounded ? bounded.ConductorAt(in position) : null;
}
