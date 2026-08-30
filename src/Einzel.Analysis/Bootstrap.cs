using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Analysis;

/// <summary>
/// A GRD-1 envelope for any statistic of a finite sample, by resampling it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> GRD-1 says every quantitative result carries an uncertainty and
/// that the API offers no way to obtain the scalar alone. Most figures of merit here are
/// statistics of an ion cloud - a peak width, an emittance, a resolving power, a mean
/// energy - and only the ones with a closed-form error had an envelope. A transmission is a
/// fraction and has a binomial standard error; a full width at half maximum has no such
/// formula, so it was reported bare or not at all.
/// </para>
/// <para>
/// <b>The bootstrap is the mechanism that covers all of them at once.</b> Draw the sample
/// again from itself with replacement, recompute the statistic, and let the spread of the
/// replicates be the standard error. It assumes nothing about the distribution, which
/// matters here because an arrival-time peak is measurably skew - the shipped reflectron's
/// is +3.27 - and a formula derived for a Gaussian would understate its own error.
/// </para>
/// <para>
/// <b>What it measures, and what it does not.</b> This is the <em>sampling</em>
/// uncertainty: how much of the number is the particular ions that were drawn. It is not
/// the discretisation error, not the integrator's, and not the model's. A figure whose
/// dominant error is any of those will report a tight interval and still be wrong, which
/// is why the evidence says <see cref="Evidence.Ensemble"/> and names the sample size
/// rather than claiming a confidence in the answer.
/// </para>
/// <para>
/// <b>Deterministic by construction.</b> The resampling is driven by a seeded generator, so
/// two runs of the same study agree exactly - the same argument that makes the energy
/// spread a deterministic sweep rather than a Gaussian draw. An uncertainty that moved
/// between identical runs would be indistinguishable from the thing it is measuring.
/// </para>
/// <para>
/// <b>WHERE IT DOES NOT WORK, WHICH MATTERS AS MUCH AS WHERE IT DOES.</b> The bootstrap is
/// inconsistent for <em>extreme-order statistics</em> - a minimum, a maximum, a range. A
/// resampled draw can only contain values already in the sample, so the resampled maximum
/// is drawn from a handful of the largest observations however many replicates are taken,
/// and the estimated error does not settle as the sample grows. Measured here: the error on
/// the range of a normal sample went 0.181, 0.313, 0.225 at 100, 400 and 1600 observations
/// - not falling, and not converging to anything.
/// </para>
/// <para>
/// This is a property of the method rather than of the implementation, and no amount of
/// replication fixes it. It bears on ion optics directly: <em>the widest entry radius that
/// still arrives</em> is an extreme-order statistic, and this project has already had to
/// replace one such measurement with a count over a fixed grid because a maximum over a
/// ragged set gave 0.65 mm on one radius grid and 0.20 mm on another for the same geometry.
/// Every figure of merit this is currently used for - a mean, a fraction, a width at half
/// maximum, an emittance - is a smooth functional of the sample, where the bootstrap is
/// consistent.
/// </para>
/// </remarks>
public static class Bootstrap
{
    /// <summary>How many replicates, unless a caller says otherwise.</summary>
    /// <remarks>
    /// The standard error of a bootstrap standard error falls as 1/sqrt(B), so 400 puts
    /// the error on the error at about 5% - well inside the significant figures anybody
    /// reads off an interval, and cheap because a replicate only recomputes a statistic
    /// over ions that were flown once.
    /// </remarks>
    public const int DefaultReplicates = 400;

    /// <summary>The seed the resampling uses, unless a caller says otherwise.</summary>
    public const int DefaultSeed = 20260830;

    /// <summary>Wraps a statistic of a sample in its sampling uncertainty.</summary>
    /// <param name="sample">The observations the statistic is computed from.</param>
    /// <param name="statistic">
    /// The statistic, given a resampled set. Returning null marks a replicate as
    /// uncomputable - a width needs two distinct arrivals, and a draw may not have them.
    /// </param>
    /// <param name="unit">The unit the result is expressed in.</param>
    /// <param name="replicates">How many times to resample.</param>
    /// <param name="seed">The resampling seed.</param>
    /// <returns>The statistic of the whole sample, with the spread of the replicates.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than one replicate.</exception>
    /// <remarks>
    /// The <em>value</em> is the statistic of the sample as observed, never the mean of the
    /// replicates: a bootstrap estimates an error, not a better estimate, and substituting
    /// the replicate mean would move the number being reported for no reason a reader could
    /// see.
    /// </remarks>
    public static Measured? Measure<T>(
        IReadOnlyList<T> sample,
        Func<IReadOnlyList<T>, double?> statistic,
        string unit,
        int replicates = DefaultReplicates,
        int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(statistic);
        ArgumentOutOfRangeException.ThrowIfLessThan(replicates, 1);

        if (statistic(sample) is not { } value || !double.IsFinite(value))
        {
            return null;
        }

        var quantity = Quantity.From(value, unit);

        if (sample.Count < 2)
        {
            // One observation carries no information about its own spread. Reported with a
            // warning rather than with a zero interval, because a zero interval is a claim
            // of certainty and this is the opposite of one.
            return new Measured(
                quantity,
                UncertaintyInterval.Symmetric(quantity, Quantity.From(0.0, unit), 0.68),
                new Evidence.Ensemble(sample.Count, Converged: false),
                [
                    new ValidityWarning(
                        "ensemble.too-small-to-resample",
                        $"this figure was computed from {sample.Count} observation(s), which "
                        + "carries no information about its own sampling spread. The interval "
                        + "is zero because there is nothing to measure, not because the value "
                        + "is certain",
                        WarningSeverity.ValidityViolation),
                ]);
        }

        var random = new Random(seed);
        var draw = new T[sample.Count];
        var replicated = new List<double>(replicates);

        for (var b = 0; b < replicates; b++)
        {
            for (var i = 0; i < draw.Length; i++)
            {
                draw[i] = sample[random.Next(sample.Count)];
            }

            if (statistic(draw) is { } replicate && double.IsFinite(replicate))
            {
                replicated.Add(replicate);
            }
        }

        if (replicated.Count < 2)
        {
            return new Measured(
                quantity,
                UncertaintyInterval.Symmetric(quantity, Quantity.From(0.0, unit), 0.68),
                new Evidence.Ensemble(sample.Count, Converged: false),
                [
                    new ValidityWarning(
                        "ensemble.resampling-failed",
                        "the statistic could not be computed on the resampled draws, so it "
                        + "has no measured sampling spread. A width needs two distinct "
                        + "observations and a resample may hold only one",
                        WarningSeverity.ValidityViolation),
                ]);
        }

        var mean = replicated.Sum() / replicated.Count;
        var variance = replicated.Sum(r => (r - mean) * (r - mean)) / (replicated.Count - 1);
        var error = Math.Sqrt(variance);

        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(quantity, Quantity.From(error, unit), 0.68),

            // Converged where the sample is large enough for the interval to mean
            // something. Thirty is the conventional floor for a sampling distribution to
            // have settled, and below it a bootstrap reports the sample's own lumpiness as
            // though it were the population's.
            new Evidence.Ensemble(sample.Count, Converged: sample.Count >= 30),
            sample.Count >= 30
                ? []
                : [
                    new ValidityWarning(
                        "ensemble.small-sample",
                        $"{sample.Count} observations: the interval is a resampling of a "
                        + "sample too small for its own spread to have settled, so read it "
                        + "as an order of magnitude rather than a bound",
                        WarningSeverity.Qualified),
                ]);
    }
}
