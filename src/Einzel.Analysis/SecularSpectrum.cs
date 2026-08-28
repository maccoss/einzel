using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Transport.Integration;

namespace Einzel.Analysis;

/// <summary>One line in a secular-motion spectrum.</summary>
/// <param name="FrequencyHz">Where the line sits.</param>
/// <param name="Power">
/// Normalised Lomb-Scargle power, between 0 and 1. One means the sinusoid at this
/// frequency accounts for the whole variance of the signal.
/// </param>
public readonly record struct SpectralLine(double FrequencyHz, double Power);

/// <summary>
/// The frequency content of an ion's motion in a driven field.
/// </summary>
/// <remarks>
/// <para>
/// An ion in an RF trap or guide moves on two timescales at once: a slow
/// <em>secular</em> oscillation in the effective well, and a fast
/// <em>micromotion</em> at the drive frequency. Mathieu theory says exactly where
/// the lines are — at <c>(2n ± β) Ω / 2</c> for integer <c>n</c>, with the secular
/// line at <c>n = 0</c> — so a measured spectrum is checkable against a closed form
/// this engine has no part in. Spec section 12 asks for the secular frequency
/// spectrum as a Class B figure.
/// </para>
/// <para>
/// <strong>It is also the only way to name a nonlinear resonance.</strong> A
/// resonance is the condition <c>n_z β_z + n_r β_r = 2</c> for some multipole order
/// <c>n_z + n_r</c>, which is a statement about frequencies. A loss measurement can
/// find the band and cannot say what it is; that is exactly where the shipped Paul
/// trap's 605–614 V band stands.
/// </para>
/// <para>
/// <strong>Lomb-Scargle rather than a discrete Fourier transform, and that is the
/// load-bearing choice.</strong> A trajectory is sampled at accepted integration
/// steps, which cluster where the physics is hard — that is
/// <see cref="TrajectoryRecorder"/> working as designed, and it means the series is
/// <em>not</em> uniformly spaced. A DFT would need the signal resampled onto a
/// uniform grid first, which is inventing values the integrator never computed and
/// then measuring them. Lomb-Scargle is the least-squares fit of a sinusoid at each
/// trial frequency, in closed form, and it needs no such step: it uses the samples
/// where they actually are.
/// </para>
/// <para>
/// Two limits, both stated rather than papered over. The frequency
/// <strong>resolution</strong> is <c>1/T</c> for a record of length <c>T</c>, which
/// is what the reported uncertainty is — a line cannot be located more finely than
/// the record it was measured in. And there is no Nyquist frequency in the usual
/// sense, because there is no uniform sampling interval; what replaces it is that a
/// line far above the typical sample rate will alias, so the search band has to be
/// chosen rather than assumed, and is a required argument here for that reason.
/// </para>
/// </remarks>
public sealed record SecularSpectrum
{
    private SecularSpectrum(
        IReadOnlyList<SpectralLine> lines,
        int samples,
        double recordSeconds,
        double resolutionHz)
    {
        Lines = lines;
        Samples = samples;
        RecordSeconds = recordSeconds;
        ResolutionHz = resolutionHz;
    }

    /// <summary>Power at each trial frequency, in ascending frequency order.</summary>
    public IReadOnlyList<SpectralLine> Lines { get; }

    /// <summary>How many trajectory samples went into it.</summary>
    public int Samples { get; }

    /// <summary>The length of the record, in seconds.</summary>
    public double RecordSeconds { get; }

    /// <summary>
    /// The frequency resolution, <c>1/T</c>, in hertz.
    /// </summary>
    /// <remarks>
    /// Two lines closer together than this are one line. It is also the uncertainty
    /// on every frequency reported here, which is the honest reading: a record of
    /// finite length cannot locate a line more finely than its own inverse duration,
    /// however finely the trial frequencies are spaced.
    /// </remarks>
    public double ResolutionHz { get; }

    /// <summary>
    /// Computes the periodogram of one Cartesian component of the motion.
    /// </summary>
    /// <param name="samples">The recorded trajectory.</param>
    /// <param name="axis">Which component: 0 for x, 1 for y, 2 for z.</param>
    /// <param name="lowHz">The bottom of the search band.</param>
    /// <param name="highHz">The top of it.</param>
    /// <param name="steps">How many trial frequencies to place across the band.</param>
    /// <returns>The spectrum.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The axis is not 0, 1 or 2; the band is not increasing and positive; or the
    /// step count is below two.
    /// </exception>
    /// <exception cref="ArgumentException">Fewer than four samples, or no variance in them.</exception>
    public static SecularSpectrum From(
        IReadOnlyList<TrajectorySample> samples, int axis, double lowHz, double highHz, int steps = 2000)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(axis);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(axis, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lowHz);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(highHz, lowHz);
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 2);

        if (samples.Count < 4)
        {
            throw new ArgumentException(
                "a periodogram needs at least four samples", nameof(samples));
        }

        var times = new double[samples.Count];
        var values = new double[samples.Count];

        for (var k = 0; k < samples.Count; k++)
        {
            var sample = samples[k];

            times[k] = sample.TimeSeconds;
            values[k] = axis switch
            {
                0 => sample.Position.X,
                1 => sample.Position.Y,
                _ => sample.Position.Z,
            };
        }

        // Mean removed, because a line at zero frequency is a displaced trap rather
        // than an oscillation and would otherwise dominate everything.
        var mean = values.Average();
        var variance = 0.0;

        for (var k = 0; k < values.Length; k++)
        {
            values[k] -= mean;
            variance += values[k] * values[k];
        }

        variance /= values.Length;

        if (!(variance > 0.0))
        {
            throw new ArgumentException(
                "the motion along this axis has no variance, so it has no spectrum",
                nameof(samples));
        }

        var record = times[^1] - times[0];
        var lines = new SpectralLine[steps];

        for (var s = 0; s < steps; s++)
        {
            var frequency = lowHz + ((highHz - lowHz) * s / (steps - 1.0));

            lines[s] = new SpectralLine(frequency, Power(times, values, variance, frequency));
        }

        return new SecularSpectrum(lines, samples.Count, record, 1.0 / record);
    }

    /// <summary>
    /// The strongest line in the spectrum, as a GRD-1 envelope.
    /// </summary>
    /// <remarks>
    /// The interval is the frequency resolution <c>1/T</c> either side, which is the
    /// only honest width: the trial-frequency spacing can be made arbitrarily fine
    /// and would then report a precision the record does not contain.
    /// </remarks>
    /// <param name="minimumPower">
    /// Below this the peak is not a line. The default of 0.1 means the sinusoid must
    /// account for at least a tenth of the variance.
    /// </param>
    /// <returns>The peak frequency, or null if nothing clears the threshold.</returns>
    public Measured? Peak(double minimumPower = 0.1)
    {
        var best = Lines[0];

        foreach (var line in Lines)
        {
            if (line.Power > best.Power)
            {
                best = line;
            }
        }

        if (best.Power < minimumPower)
        {
            return null;
        }

        var warnings = new List<ValidityWarning>();

        // Within one resolution width of an end, not exactly on it. A line has the
        // width 1/T whatever the trial spacing, so a peak that close to the edge is
        // a peak whose line overlaps the edge - and a line clipped by the band can
        // put its apparent maximum at an interior trial frequency while still being
        // the shoulder of something outside. Testing for the last grid point misses
        // exactly that case, which is the one this warning is for.
        if (best.FrequencyHz - Lines[0].FrequencyHz <= ResolutionHz
            || Lines[^1].FrequencyHz - best.FrequencyHz <= ResolutionHz)
        {
            warnings.Add(new ValidityWarning(
                "spectrum.peak-at-band-edge",
                $"the strongest line is at {best.FrequencyHz:G6} Hz, within one resolution width "
                + $"({ResolutionHz:G4} Hz) of an end of the searched band "
                + $"[{Lines[0].FrequencyHz:G6}, {Lines[^1].FrequencyHz:G6}] Hz. A line clipped by the "
                + "band looks identical to one that is genuinely there - widen the band and see "
                + "whether the peak moves",
                WarningSeverity.ValidityViolation));
        }

        if (ResolutionHz > 0.05 * best.FrequencyHz)
        {
            warnings.Add(new ValidityWarning(
                "spectrum.short-record",
                $"the record is {RecordSeconds:G4} s, so the resolution is {ResolutionHz:G4} Hz - "
                + $"more than five per cent of the {best.FrequencyHz:G6} Hz line it is measuring. "
                + "Fewer than about twenty cycles were observed, and a line cannot be located more "
                + "finely than the record containing it",
                WarningSeverity.Qualified));
        }

        var hertz = Dimension.Frequency;

        return new Measured(
            Quantity.Si(best.FrequencyHz, hertz),
            UncertaintyInterval.Between(
                Quantity.Si(best.FrequencyHz - ResolutionHz, hertz),
                Quantity.Si(best.FrequencyHz + ResolutionHz, hertz),
                1.0),
            new Evidence.Ensemble(Samples, true),
            warnings);
    }

    /// <summary>
    /// Every local maximum above a power threshold, strongest first.
    /// </summary>
    /// <remarks>
    /// What a resonance identification needs, because the question is which lines
    /// are present rather than which is loudest: a secular line and its micromotion
    /// sidebands are all real and only one of them is the peak.
    /// </remarks>
    /// <param name="minimumPower">The threshold, as a fraction of the variance.</param>
    /// <returns>The lines, descending in power.</returns>
    public IReadOnlyList<SpectralLine> Peaks(double minimumPower = 0.05)
    {
        var found = new List<SpectralLine>();

        for (var k = 1; k < Lines.Count - 1; k++)
        {
            if (Lines[k].Power >= minimumPower
                && Lines[k].Power > Lines[k - 1].Power
                && Lines[k].Power >= Lines[k + 1].Power)
            {
                found.Add(Lines[k]);
            }
        }

        found.Sort((a, b) => b.Power.CompareTo(a.Power));

        return found;
    }

    /// <summary>
    /// The Lomb-Scargle power at one trial frequency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The closed-form least-squares fit of <c>a cos(ω(t − τ)) + b sin(ω(t − τ))</c>,
    /// where <c>τ</c> is chosen to make the cosine and sine terms orthogonal over the
    /// sample times whatever those times are. That choice is the whole trick, and it
    /// is why this works on a non-uniform series where a naive projection onto
    /// <c>cos</c> and <c>sin</c> would not: the two basis functions are only
    /// orthogonal on a uniform grid, and off one the fit leaks between them.
    /// </para>
    /// </remarks>
    private static double Power(double[] times, double[] values, double variance, double frequency)
    {
        var omega = 2.0 * Math.PI * frequency;

        // The time offset that orthogonalises the basis: tan(2 omega tau) is the
        // ratio of the summed sine and cosine of twice the phase.
        var sum2 = 0.0;
        var cos2 = 0.0;

        for (var k = 0; k < times.Length; k++)
        {
            var phase = 2.0 * omega * times[k];

            sum2 += Math.Sin(phase);
            cos2 += Math.Cos(phase);
        }

        var tau = 0.5 * Math.Atan2(sum2, cos2) / omega;

        double cosNumerator = 0.0, cosDenominator = 0.0;
        double sinNumerator = 0.0, sinDenominator = 0.0;

        for (var k = 0; k < times.Length; k++)
        {
            var phase = omega * (times[k] - tau);
            var c = Math.Cos(phase);
            var s = Math.Sin(phase);

            cosNumerator += values[k] * c;
            cosDenominator += c * c;
            sinNumerator += values[k] * s;
            sinDenominator += s * s;
        }

        var power = 0.0;

        if (cosDenominator > 0.0)
        {
            power += cosNumerator * cosNumerator / cosDenominator;
        }

        if (sinDenominator > 0.0)
        {
            power += sinNumerator * sinNumerator / sinDenominator;
        }

        // Normalised so a pure sinusoid gives exactly 1. For y = A cos(omega(t - tau))
        // over many cycles the cosine term collects A^2 N / 2 and the sine term
        // nothing, while the sum of squares is also A^2 N / 2 - so dividing by it
        // puts the power on a scale where 1 means "this one sinusoid accounts for
        // the whole variance" and 0.5 means half of it. That is a more useful scale
        // than the classical (N-1)/2 maximum, because it is comparable between
        // records of different length.
        return power / (values.Length * variance);
    }
}
