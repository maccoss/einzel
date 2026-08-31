using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Analysis;

/// <summary>
/// The arrival-time peak formed by an ensemble, and the Class T figures of merit
/// read off it.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 12: "Figures of merit are computed by the engine, since they are
/// what the optimizer, sweep driver, and project tests consume." Class T is the
/// timing class — resolving power, peak shape, focusing order — and section 12
/// asks for resolving power "reported both FWHM-based and fitted, with the peak
/// model named".
/// </para>
/// <para>
/// Both are reported here because they disagree, and the disagreement is
/// information. An FWHM read directly off the sorted arrivals makes no assumption
/// about peak shape and is therefore honest about a peak that is not Gaussian; a
/// Gaussian-equivalent width from the standard deviation assumes a shape the
/// aberrations may not have produced. When a mirror is at its focus the two
/// agree closely; when a second-order term dominates the peak develops a tail and
/// they separate. Quoting only one hides that.
/// </para>
/// </remarks>
public sealed class ArrivalTimePeak
{
    private readonly double[] _arrivals;

    private ArrivalTimePeak(double[] sortedArrivals, int launched)
    {
        _arrivals = sortedArrivals;
        Launched = launched;
    }

    /// <summary>Builds a peak from arrival times, in seconds.</summary>
    /// <param name="arrivals">Arrival times of the ions that reached the detector.</param>
    /// <param name="launched">
    /// How many ions were launched, including any that did not arrive. Transmission
    /// is the ratio, and a resolving power quoted without it can be a very sharp
    /// peak made of three surviving ions.
    /// </param>
    /// <returns>The peak.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arrivals"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two ions arrived, or more arrived than were launched.</exception>
    public static ArrivalTimePeak FromArrivals(IEnumerable<double> arrivals, int launched)
    {
        ArgumentNullException.ThrowIfNull(arrivals);

        var sorted = arrivals.Where(double.IsFinite).ToArray();
        Array.Sort(sorted);

        if (sorted.Length < 2)
        {
            throw new ArgumentException(
                $"a peak needs at least two arrivals; got {sorted.Length}", nameof(arrivals));
        }

        if (sorted.Length > launched)
        {
            throw new ArgumentException(
                $"{sorted.Length} ions arrived but only {launched} were launched", nameof(launched));
        }

        return new ArrivalTimePeak(sorted, launched);
    }

    /// <summary>Ions launched.</summary>
    public int Launched { get; }

    /// <summary>Ions that reached the detector.</summary>
    public int Arrived => _arrivals.Length;

    /// <summary>Arrival times, sorted, in seconds.</summary>
    public IReadOnlyList<double> Arrivals => _arrivals;

    /// <summary>Mean arrival time, in seconds.</summary>
    public double MeanSeconds => _arrivals.Average();

    /// <summary>Standard deviation of arrival time, in seconds.</summary>
    public double StandardDeviationSeconds
    {
        get
        {
            var mean = MeanSeconds;
            var sum = _arrivals.Sum(t => (t - mean) * (t - mean));
            return Math.Sqrt(sum / (_arrivals.Length - 1));
        }
    }

    /// <summary>Full width of the arrival distribution, in seconds.</summary>
    public double FullWidthSeconds => _arrivals[^1] - _arrivals[0];

    /// <summary>
    /// The width containing a given fraction of the arrivals, centred on the
    /// median, in seconds.
    /// </summary>
    /// <param name="fraction">Fraction of arrivals to enclose, in (0, 1].</param>
    /// <returns>The width, in seconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The fraction is outside (0, 1].</exception>
    /// <remarks>
    /// A quantile width rather than a histogram width. Binning an ensemble of a
    /// few thousand ions to find a half maximum makes the answer depend on the bin
    /// size, and the dependence is strongest exactly where the peak is narrowest —
    /// which is where the number matters.
    /// </remarks>
    public double CentralWidthSeconds(double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fraction);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1.0);

        var tail = (1.0 - fraction) / 2.0;
        return Quantile(1.0 - tail) - Quantile(tail);
    }

    /// <summary>
    /// The Gaussian-equivalent full width at half maximum, in seconds.
    /// </summary>
    /// <remarks>
    /// Derived from the standard deviation by the Gaussian factor of
    /// 2 sqrt(2 ln 2). Named for the assumption it makes so that a caller
    /// comparing it against <see cref="CentralWidthSeconds"/> knows which of the
    /// two is the model-free one.
    /// </remarks>
    public double GaussianEquivalentFwhmSeconds => 2.0 * Math.Sqrt(2.0 * Math.Log(2.0)) * StandardDeviationSeconds;

    /// <summary>Asymmetry of the peak: the skewness of the arrival distribution.</summary>
    /// <remarks>
    /// Zero for a symmetric peak. A single-stage mirror away from its focus
    /// produces a one-sided second-order tail, and the sign says which side.
    /// </remarks>
    public double Skewness
    {
        get
        {
            var mean = MeanSeconds;
            var deviation = StandardDeviationSeconds;

            if (deviation == 0.0)
            {
                return 0.0;
            }

            var sum = _arrivals.Sum(t => Math.Pow((t - mean) / deviation, 3));
            return sum / _arrivals.Length;
        }
    }

    /// <summary>
    /// Resolving power from the model-free central width, as the GRD-1 envelope.
    /// </summary>
    /// <param name="fraction">
    /// Fraction of arrivals the width encloses. The default of 0.5 makes this the
    /// half-width resolving power.
    /// </param>
    /// <returns>The resolving power, with its uncertainty and evidence.</returns>
    /// <remarks>
    /// R = t / (2 dt), the convention the companion memo uses throughout. The
    /// interval comes from the sampling uncertainty of a quantile width, which
    /// falls as the square root of the ensemble size — so a resolving power quoted
    /// from a hundred ions carries a visibly wider interval than the same number
    /// from ten thousand, which is the point of quoting it at all.
    /// </remarks>
    public Measured ResolvingPower(double fraction = 0.5)
    {
        var width = CentralWidthSeconds(fraction);
        var mean = MeanSeconds;

        if (width <= 0.0)
        {
            // Every ion arrived within one tick of the others. Real, and worth
            // reporting as unbounded rather than as a division by zero.
            return Unresolved(mean);
        }

        var value = mean / (2.0 * width);

        // The relative uncertainty of a quantile width from n samples goes as
        // 1/sqrt(2n); the resolving power inherits it.
        var relative = 1.0 / Math.Sqrt(2.0 * Arrived);
        var quantity = Quantity.Number(value);

        var warnings = new List<ValidityWarning>();

        if (Arrived < Launched)
        {
            warnings.Add(new ValidityWarning(
                "ENSEMBLE_INCOMPLETE",
                $"{Launched - Arrived} of {Launched} ions did not reach the detector; "
                + "this resolving power describes the survivors only",
                WarningSeverity.Qualified));
        }

        if (Arrived < 100)
        {
            warnings.Add(new ValidityWarning(
                "ENSEMBLE_SMALL",
                $"{Arrived} ions is a thin basis for a peak width; the interval reflects that but the "
                + "peak shape may not be resolved at all",
                WarningSeverity.Qualified));
        }

        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(quantity, Quantity.Number(value * relative), confidenceLevel: 0.68),
            new Evidence.Ensemble(Arrived, Converged: Arrived >= 100),
            warnings);
    }

    /// <summary>
    /// Resolving power from the Gaussian-equivalent width, as the GRD-1 envelope.
    /// </summary>
    /// <returns>The resolving power, with the peak model named in its evidence.</returns>
    public Measured GaussianResolvingPower()
    {
        var width = GaussianEquivalentFwhmSeconds;
        var mean = MeanSeconds;

        if (width <= 0.0)
        {
            return Unresolved(mean);
        }

        var value = mean / (2.0 * width);
        var relative = 1.0 / Math.Sqrt(2.0 * Arrived);
        var quantity = Quantity.Number(value);

        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(quantity, Quantity.Number(value * relative), confidenceLevel: 0.68),
            new Evidence.Analytic($"Gaussian-equivalent FWHM from n = {Arrived}, skewness {Skewness:F3}"),
            [
                new ValidityWarning(
                    "PEAK_MODEL_ASSUMED",
                    $"this resolving power assumes a Gaussian peak; the observed skewness is {Skewness:F3}",
                    WarningSeverity.Advisory),
            ]);
    }

    /// <summary>Transmission: the fraction of launched ions that arrived.</summary>
    /// <returns>Transmission, with a binomial interval.</returns>
    public Measured Transmission()
    {
        var value = (double)Arrived / Launched;

        // Binomial standard error, floored so a perfect ensemble still reports an
        // interval rather than claiming certainty from a finite sample.
        var error = Math.Max(Math.Sqrt(value * (1.0 - value) / Launched), 1.0 / Launched);
        var quantity = Quantity.Number(value);

        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(quantity, Quantity.Number(error), confidenceLevel: 0.68),
            new Evidence.Ensemble(Launched, Converged: Launched >= 100));
    }

    /// <summary>The resolving power of a peak with no width: undefined, not zero.</summary>
    /// <remarks>
    /// <para>
    /// <b>It used to report zero, which is the exact opposite of the truth and contradicted
    /// the warning printed beside it.</b> Resolving power is t / 2dt, so a width of zero
    /// makes it unbounded — the best conceivable value — and a reader saw <c>resolving 0</c>,
    /// the worst. The infinity was even computed, assigned to a local, and then not used.
    /// </para>
    /// <para>
    /// NaN rather than infinity, because absent is what this surface means by "there is no
    /// answer here": <c>FiniteDoubleConverter</c> writes a non-finite double as null, so
    /// both would serialise the same, and NaN does not invite arithmetic that would
    /// propagate silently. It is the rule already applied to an undefined Twiss orientation,
    /// to a peak width with fewer than two arrivals, and to an energy drift with no scale.
    /// </para>
    /// </remarks>
    private Measured Unresolved(double mean)
    {
        var undefined = Quantity.Number(double.NaN);

        return new Measured(
            undefined,
            // A zero half-width around an undefined value: the interval API refuses a
            // non-finite width, and it is the VALUE that carries the signal here.
            UncertaintyInterval.Symmetric(undefined, Quantity.Number(0.0), confidenceLevel: 0.68),
            new Evidence.Ensemble(Arrived, Converged: false),
            [
                new ValidityWarning(
                    "PEAK_UNRESOLVED",
                    "every arrival fell within floating-point resolution of the others, so the width is zero "
                    + "and the resolving power is unbounded; the ensemble carries no spread to resolve",
                    WarningSeverity.ValidityViolation),
            ]);
    }

    private double Quantile(double probability)
    {
        if (_arrivals.Length == 1)
        {
            return _arrivals[0];
        }

        var position = probability * (_arrivals.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, _arrivals.Length - 1);
        var t = position - lower;

        return ((1.0 - t) * _arrivals[lower]) + (t * _arrivals[upper]);
    }
}
