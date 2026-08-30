using Einzel.Analysis;
using Einzel.Core.Results;

using Xunit.Abstractions;

namespace Einzel.Analysis.Tests;

/// <summary>
/// A sampling uncertainty for any statistic of a finite sample (GRD-1).
/// </summary>
/// <remarks>
/// Most figures of merit here are statistics of an ion cloud, and only the ones with a
/// closed-form error had an envelope: a transmission is a fraction and has a binomial
/// standard error, while a full width at half maximum has no such formula. The bootstrap
/// covers all of them at once — so the checks below are that it reproduces the closed forms
/// where they exist, and behaves sensibly where they do not.
/// </remarks>
public sealed class BootstrapTests(ITestOutputHelper output)
{
    /// <summary>The half-width of a symmetric interval, in SI.</summary>
    /// <remarks>
    /// From the width, because <c>Measured</c> deliberately offers no way to read a bare
    /// value or a bare error - GRD-1's absolutism, which the test has to work with rather
    /// than around.
    /// </remarks>
    private static double Error(Measured measured) => 0.5 * measured.Uncertainty.WidthSi;

    /// <summary>The magnitude, in SI, through the deconstruction GRD-1 permits.</summary>
    private static double Si(Measured measured)
    {
        var (value, _, _, _) = measured;

        return value.SiValue;
    }

    /// <summary>A reproducible normal sample, by Box-Muller.</summary>
    /// <remarks>
    /// Its own generator, not the bootstrap's: a test that drew its data from the thing
    /// under test would be checking that one stream agrees with itself.
    /// </remarks>
    private static double[] Normal(int count, double mean, double sigma, int seed)
    {
        var random = new Random(seed);
        var sample = new double[count];

        for (var i = 0; i < count; i++)
        {
            var u = 1.0 - random.NextDouble();
            var v = random.NextDouble();

            sample[i] = mean + (sigma * Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
        }

        return sample;
    }

    private static double? Mean(IReadOnlyList<double> values) => values.Sum() / values.Count;

    /// <summary>
    /// The bootstrap error of a mean is the standard error of the mean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The check that says the mechanism is right rather than merely plausible.</b> For
    /// a mean there <em>is</em> a closed form — sigma over root N — and a bootstrap must
    /// reproduce it. Nothing else here has one, which is why the mechanism exists; this is
    /// the one place it can be held against arithmetic the code had no part in.
    /// </para>
    /// <para>
    /// Agreement to a few per cent, not to machine precision: the bootstrap estimates the
    /// spread from a finite number of replicates, so its own error falls as one over the
    /// root of that. Asserting equality would be asserting that a stochastic estimate is
    /// exact.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(50)]
    [InlineData(200)]
    [InlineData(1000)]
    public void TheBootstrapErrorOfAMeanIsTheStandardErrorOfTheMean(int count)
    {
        const double Sigma = 3.0;

        var sample = Normal(count, mean: 10.0, sigma: Sigma, seed: 12345);

        // The closed form, from the sample's own spread rather than from the sigma it was
        // drawn with: that is what a bootstrap can possibly know.
        var observed = Mean(sample)!.Value;
        var variance = sample.Sum(x => (x - observed) * (x - observed)) / (count - 1);
        var closedForm = Math.Sqrt(variance / count);

        var measured = Bootstrap.Measure(sample, Mean, "1", replicates: 2000, seed: 99);

        Assert.NotNull(measured);

        var error = Error(measured!);

        output.WriteLine(
            $"{count,5} observations: bootstrap {error:G6}, sigma/sqrt(N) {closedForm:G6}, "
            + $"ratio {error / closedForm:F4}");

        Assert.Equal(1.0, error / closedForm, 1);
    }

    /// <summary>The value is the sample's statistic, not the mean of the replicates.</summary>
    /// <remarks>
    /// A bootstrap estimates an <em>error</em>, not a better estimate. Substituting the
    /// replicate mean would move the number being reported for no reason a reader could
    /// see, and would make the reported value depend on the resampling seed.
    /// </remarks>
    [Fact]
    public void TheValueIsTheSampleStatisticAndNotTheReplicateMean()
    {
        var sample = Normal(80, mean: 5.0, sigma: 2.0, seed: 7);

        var observed = Mean(sample)!.Value;

        var first = Bootstrap.Measure(sample, Mean, "1", seed: 1);
        var second = Bootstrap.Measure(sample, Mean, "1", seed: 2);

        output.WriteLine($"sample mean {observed:G12}");
        output.WriteLine($"seed 1 value {Si(first!):G12}, error {Error(first!):G4}");
        output.WriteLine($"seed 2 value {Si(second!):G12}, error {Error(second!):G4}");

        Assert.Equal(observed, Si(first!), 12);
        Assert.Equal(observed, Si(second!), 12);
    }

    /// <summary>Two runs of the same measurement agree exactly.</summary>
    /// <remarks>
    /// An uncertainty that moved between identical runs would be indistinguishable from the
    /// thing it is measuring. Seeded for the same reason the energy spread is a
    /// deterministic sweep rather than a Gaussian draw.
    /// </remarks>
    [Fact]
    public void TwoRunsOfTheSameMeasurementAgreeExactly()
    {
        var sample = Normal(60, mean: 1.0, sigma: 0.25, seed: 3);

        var first = Bootstrap.Measure(sample, Mean, "1");
        var second = Bootstrap.Measure(sample, Mean, "1");

        Assert.Equal(Error(first!), Error(second!), 15);
    }

    /// <summary>
    /// It reproduces the median's asymptotic error too, which no formula in the code knows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second closed form, and an independent one: for a normal sample the standard error
    /// of the median is <c>sqrt(pi/2)</c> times that of the mean — about 1.2533 sigma over
    /// root N. The mechanism has no idea which statistic it is being handed, so agreeing
    /// with two different closed forms is much stronger than agreeing with one.
    /// </para>
    /// <para>
    /// A median is also the smallest step away from a mean into territory where the formula
    /// is <em>asymptotic</em> rather than exact, which is the situation every real figure of
    /// merit here is in.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1000)]
    [InlineData(4000)]
    [InlineData(16000)]
    public void ItReproducesTheMediansAsymptoticErrorAsWell(int count)
    {
        const double Sigma = 2.0;

        static double? Median(IReadOnlyList<double> values)
        {
            var sorted = values.Order().ToArray();

            return sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : 0.5 * (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]);
        }

        var sample = Normal(count, mean: 0.0, sigma: Sigma, seed: 2024);

        var closedForm = Math.Sqrt(Math.PI / 2.0) * Sigma / Math.Sqrt(count);

        var measured = Bootstrap.Measure(sample, Median, "1", replicates: 1200, seed: 17);

        Assert.NotNull(measured);

        var error = Error(measured!);

        output.WriteLine(
            $"{count,5} observations: bootstrap {error:G6}, "
            + $"sqrt(pi/2)*sigma/sqrt(N) {closedForm:G6}, ratio {error / closedForm:F4}");

        // Measured at 0.975, 1.041 and 0.940 for these three sizes. Fifteen per cent,
        // because the formula is asymptotic and the sample's own sigma differs from the one
        // it was drawn with - the bootstrap can only know the former.
        Assert.InRange(error / closedForm, 0.85, 1.15);
    }

    /// <summary>It understates a quantile's error on a small sample.</summary>
    /// <remarks>
    /// <para>
    /// <b>The milder cousin of the extrema limitation, and worth pinning for the same
    /// reason.</b> A resampled median can only take values already present, and at two
    /// hundred observations there are few distinct values near the centre to take - so the
    /// replicates cluster and the estimated error comes out low. Measured: <b>0.669</b> of
    /// the asymptotic standard error at 200 observations, against 0.975 at 1000.
    /// </para>
    /// <para>
    /// It matters because the default ensemble here is twenty-one ions. A figure that is a
    /// quantile of that - a median arrival, a half-maximum width - carries an interval that
    /// is real and optimistic, which is why a sample under thirty is qualified rather than
    /// reported plain.
    /// </para>
    /// </remarks>
    [Fact]
    public void ItUnderstatesAQuantilesErrorOnASmallSample()
    {
        const double Sigma = 2.0;
        const int Count = 200;

        static double? Median(IReadOnlyList<double> values)
        {
            var sorted = values.Order().ToArray();

            return sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : 0.5 * (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]);
        }

        var sample = Normal(Count, mean: 0.0, sigma: Sigma, seed: 2024);
        var closedForm = Math.Sqrt(Math.PI / 2.0) * Sigma / Math.Sqrt(Count);

        var measured = Bootstrap.Measure(sample, Median, "1", replicates: 1200, seed: 17);

        Assert.NotNull(measured);

        var ratio = Error(measured!) / closedForm;

        output.WriteLine(
            $"{Count} observations: bootstrap {Error(measured!):G6} against an asymptotic "
            + $"{closedForm:G6} - {ratio:F3} of it");

        // Asserted as the shortfall it is. A test that tolerated either would document
        // nothing; one that demanded agreement would be demanding something untrue.
        Assert.InRange(ratio, 0.5, 0.85);
    }

    /// <summary>
    /// It is inconsistent for an extreme-order statistic, and that is a property of the
    /// method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recorded so that nobody reaches for it here.</b> A resampled draw can only contain
    /// values already in the sample, so the resampled maximum is drawn from a handful of the
    /// largest observations however many replicates are taken. The estimated error does not
    /// settle as the sample grows, and no amount of replication fixes it.
    /// </para>
    /// <para>
    /// This bears on ion optics directly: <em>the widest entry radius that still arrives</em>
    /// is an extreme-order statistic, and this project already had to replace one such
    /// measurement with a count over a fixed grid after it gave 0.65 mm on one radius grid
    /// and 0.20 mm on another for the same geometry.
    /// </para>
    /// <para>
    /// The assertion is the <em>failure</em> — that the error does not fall monotonically —
    /// because a test asserting it behaves would be asserting something untrue, and one
    /// asserting nothing would let somebody discover this the expensive way.
    /// </para>
    /// </remarks>
    [Fact]
    public void ItIsInconsistentForAnExtremeOrderStatistic()
    {
        static double? Range(IReadOnlyList<double> values) =>
            values.Count >= 2 ? values.Max() - values.Min() : null;

        var errors = new List<double>();

        foreach (var count in (int[])[100, 400, 1600])
        {
            var sample = Normal(count, mean: 0.0, sigma: 1.0, seed: 42);
            var measured = Bootstrap.Measure(sample, Range, "1", replicates: 1500, seed: 5);

            Assert.NotNull(measured);

            errors.Add(Error(measured!));

            output.WriteLine(
                $"{count,5} observations: range {Si(measured!):F4} +/- {Error(measured!):F4}");
        }

        // Sixteen times the sample and the error does not fall: the signature of the
        // inconsistency. A smooth statistic would have roughly quartered it.
        Assert.False(
            errors[0] > errors[1] && errors[1] > errors[2],
            "the error fell monotonically - if the bootstrap has become consistent for "
            + "extrema, the caveat in Bootstrap's remarks needs revisiting");

        output.WriteLine(
            $"errors {errors[0]:F4}, {errors[1]:F4}, {errors[2]:F4} - not converging, as "
            + "the method's own limitation predicts");
    }

    /// <summary>A sample of one says so rather than claiming certainty.</summary>
    /// <remarks>
    /// One observation carries no information about its own spread. A zero interval is a
    /// claim of certainty and this is the opposite of one, so it is reported as a validity
    /// violation rather than as a tight result.
    /// </remarks>
    [Fact]
    public void ASampleOfOneSaysSoRatherThanClaimingCertainty()
    {
        var measured = Bootstrap.Measure([4.0], Mean, "1");

        Assert.NotNull(measured);

        var said = Assert.Single(measured.Warnings);

        output.WriteLine($"[{said.Severity}] {said.Code}: {said.Message}");

        Assert.Equal("ensemble.too-small-to-resample", said.Code);
        Assert.Equal(WarningSeverity.ValidityViolation, said.Severity);
        Assert.Equal(4.0, Si(measured!), 12);
    }

    /// <summary>A small sample is qualified rather than refused.</summary>
    /// <remarks>
    /// Taint, never block. Twenty-one ions is the default ensemble here and it does produce
    /// an interval - one to read as an order of magnitude, which is what the warning says.
    /// </remarks>
    [Fact]
    public void ASmallSampleIsQualifiedRatherThanRefused()
    {
        var sample = Normal(21, mean: 2.0, sigma: 0.5, seed: 11);

        var measured = Bootstrap.Measure(sample, Mean, "1");

        Assert.NotNull(measured);

        var said = Assert.Single(measured.Warnings, w => w.Code == "ensemble.small-sample");

        output.WriteLine($"[{said.Severity}] {said.Message}");
        output.WriteLine($"value {Si(measured!):G6} +/- {Error(measured!):G4}");

        Assert.Equal(WarningSeverity.Qualified, said.Severity);
        Assert.True(Error(measured!) > 0.0);

        var evidence = Assert.IsType<Evidence.Ensemble>(measured.Evidence);

        Assert.Equal(21, evidence.EnsembleSize);
        Assert.False(evidence.Converged);
    }

    /// <summary>A statistic the sample cannot support is absent, not zero.</summary>
    [Fact]
    public void AStatisticTheSampleCannotSupportIsAbsent()
    {
        static double? NeedsThree(IReadOnlyList<double> values) =>
            values.Count >= 3 ? values.Sum() : null;

        Assert.Null(Bootstrap.Measure([1.0, 2.0], NeedsThree, "1"));
    }
}
