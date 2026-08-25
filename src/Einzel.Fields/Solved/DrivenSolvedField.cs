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
/// 200 basis solutions" - and it is the target this decomposition reaches, for any
/// number of rings.
/// </para>
/// <para>
/// Grouping is by <em>spatial pattern</em>, not by time dependence, and that is
/// what makes it minimal. Every electrode's potential is first split into the
/// supplies feeding it - one constant, one per distinct drive phase - so a resistor
/// chain down a funnel is a single supply however many distinct voltages it holds,
/// because what makes a supply one supply is that its electrodes move
/// <em>together</em>. Then supplies whose applied potentials are exactly
/// proportional share a solve and carry a weight each: a quadrupole run with DC has
/// a steady supply and an oscillating one, both putting the x pair up and the y
/// pair down by the same relative amounts, so the whole filter is still one solve.
/// </para>
/// </remarks>
public sealed class DrivenSolvedField : ITimeVaryingField, IConductorBounded
{
    private readonly IElectrostaticField[] _channels;
    private readonly double[] _direct;
    private readonly (double Amplitude, double Phase)[][] _harmonics;
    private readonly RfWaveform _waveform;

    // One entry per stage, holding that stage's weights and when it ends. Empty
    // for a geometry held in one state for the whole run.
    private readonly double[] _boundaries;
    private readonly double[][] _stageDirect;
    private readonly (double Amplitude, double Phase)[][][] _stageHarmonics;

    internal DrivenSolvedField(
        IReadOnlyList<IElectrostaticField> channels,
        IReadOnlyList<double> direct,
        IReadOnlyList<IReadOnlyList<(double Amplitude, double Phase)>> harmonics,
        double frequencyHz,
        RfWaveform waveform,
        IReadOnlyList<double>? boundaries = null,
        IReadOnlyList<IReadOnlyList<double>>? stageDirect = null,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double Amplitude, double Phase)>>>? stageHarmonics = null)
    {
        _channels = [.. channels];
        _direct = [.. direct];
        _harmonics = [.. harmonics.Select(h => h.ToArray())];
        _waveform = waveform;

        _boundaries = boundaries is null ? [] : [.. boundaries];
        _stageDirect = stageDirect is null ? [] : [.. stageDirect.Select(d => d.ToArray())];

        _stageHarmonics = stageHarmonics is null
            ? []
            : [.. stageHarmonics.Select(stage => stage.Select(h => h.ToArray()).ToArray())];

        FrequencyHz = frequencyHz;
    }

    /// <summary>How many stages the sequence has. Zero for a geometry held in one state.</summary>
    public int StageCount => _boundaries.Length;

    /// <summary>When the sequence finishes, in seconds. Infinity when there is none.</summary>
    public double SequenceEndsAt => _boundaries.Length == 0 ? double.PositiveInfinity : _boundaries[^1];

    /// <summary>
    /// Which stage is running at an instant.
    /// </summary>
    /// <remarks>
    /// After the last one ends the last one continues to hold, rather than the
    /// field switching off. A sequence describes what the instrument does, and an
    /// instrument left alone stays where it was put - and a field that vanished at
    /// the end of the declared sequence would make every ion still in flight
    /// suddenly coast, which is a physics change disguised as a bookkeeping one.
    /// </remarks>
    private int StageAt(double timeSeconds)
    {
        for (var k = 0; k < _boundaries.Length; k++)
        {
            if (timeSeconds < _boundaries[k])
            {
                return k;
            }
        }

        return _boundaries.Length - 1;
    }

    /// <inheritdoc/>
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

    private double Weight(int channel, double timeSeconds)
    {
        var stage = _boundaries.Length == 0 ? -1 : StageAt(timeSeconds);

        var direct = stage < 0 ? _direct[channel] : _stageDirect[stage][channel];
        var harmonics = stage < 0 ? _harmonics[channel] : _stageHarmonics[stage][channel];

        var total = direct;
        var cycles = FrequencyHz * timeSeconds;

        // More than one term because two supplies can share a spatial pattern: a
        // quadrupole's DC and RF put the same electrodes up and down by the same
        // relative amounts, so they are one solved field carrying two weights.
        foreach (var (amplitude, phase) in harmonics)
        {
            total += amplitude * _waveform.At(cycles + phase);
        }

        return total;
    }

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
