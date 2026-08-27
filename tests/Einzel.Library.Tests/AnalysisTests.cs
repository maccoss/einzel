using Einzel.Analysis;
using Einzel.Core.Results;

namespace Einzel.Library.Tests;

/// <summary>
/// The Class T figures of merit, checked against distributions whose answers are
/// known in closed form.
/// </summary>
public sealed class AnalysisTests
{
    /// <summary>Arrivals spread uniformly about a centre.</summary>
    private static double[] Uniform(double centre, double fullWidth, int count) =>
        [.. Enumerable.Range(0, count).Select(k => centre - (fullWidth / 2.0) + (fullWidth * k / (count - 1)))];

    [Fact]
    public void ResolvingPowerFollowsTheHalfWidthConvention()
    {
        // R = t / (2 dt), the convention the analyzer literature uses. For a
        // uniform spread the central half-width is exactly half the full width.
        var peak = ArrivalTimePeak.FromArrivals(Uniform(100e-6, 8e-9, 1001), 1001);

        var halfWidth = peak.CentralWidthSeconds(0.5);
        Assert.Equal(4e-9, halfWidth, 1e-11);

        var (value, _, _, _) = peak.ResolvingPower();
        Assert.Equal(100e-6 / (2.0 * 4e-9), value.SiValue, 1.0);
    }

    [Fact]
    public void TheIntervalNarrowsAsTheEnsembleGrows()
    {
        // GRD-1: the same number from a hundred ions and from ten thousand must
        // not carry the same interval.
        var small = ArrivalTimePeak.FromArrivals(Uniform(100e-6, 8e-9, 101), 101);
        var large = ArrivalTimePeak.FromArrivals(Uniform(100e-6, 8e-9, 10001), 10001);

        var (_, smallInterval, _, _) = small.ResolvingPower();
        var (_, largeInterval, _, _) = large.ResolvingPower();

        Assert.True(
            largeInterval.WidthSi < smallInterval.WidthSi / 5.0,
            $"a hundredfold ensemble should narrow the interval about tenfold: "
            + $"{smallInterval.WidthSi:E3} against {largeInterval.WidthSi:E3}");
    }

    [Fact]
    public void ASmallEnsembleSaysSoRatherThanQuotingQuietly()
    {
        var peak = ArrivalTimePeak.FromArrivals(Uniform(100e-6, 8e-9, 20), 20);
        var (_, _, evidence, warnings) = peak.ResolvingPower();

        Assert.Contains(warnings, w => w.Code == "ENSEMBLE_SMALL");
        Assert.False(Assert.IsType<Evidence.Ensemble>(evidence).Converged);
    }

    [Fact]
    public void LostIonsAreReportedWithTheResolvingPowerTheySurvived()
    {
        // A very sharp peak made of three surviving ions is not a resolving power,
        // and the envelope has to say which it is.
        var peak = ArrivalTimePeak.FromArrivals(Uniform(100e-6, 8e-9, 400), 1000);
        var (_, _, _, warnings) = peak.ResolvingPower();

        Assert.Contains(warnings, w => w.Code == "ENSEMBLE_INCOMPLETE");

        var (transmission, _, _, _) = peak.Transmission();
        Assert.Equal(0.4, transmission.SiValue, 1e-12);
    }

    [Fact]
    public void AGaussianPeakAgreesWithItsGaussianEquivalentWidth()
    {
        // Where the peak really is Gaussian the model-free and model-assuming
        // widths should track; it is their divergence that carries information.
        var random = new Random(20260824);
        var arrivals = new double[20000];

        for (var i = 0; i < arrivals.Length; i++)
        {
            // Box-Muller.
            var u1 = random.NextDouble();
            var u2 = random.NextDouble();
            arrivals[i] = 100e-6 + (2e-9 * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        var peak = ArrivalTimePeak.FromArrivals(arrivals, arrivals.Length);

        // For a Gaussian the central 76.1% width equals the FWHM.
        var fwhm = peak.CentralWidthSeconds(0.7610);

        Assert.Equal(peak.GaussianEquivalentFwhmSeconds, fwhm, fwhm * 0.05);
        Assert.InRange(Math.Abs(peak.Skewness), 0.0, 0.1);
    }

    [Fact]
    public void AOneSidedTailShowsUpAsSkew()
    {
        // A single-stage mirror away from focus puts a one-sided second-order tail
        // on the peak, and the sign says which side.
        var arrivals = new List<double>();

        for (var k = 0; k < 1000; k++)
        {
            var d = -0.05 + (0.1 * k / 999.0);
            arrivals.Add(100e-6 * (1.0 + (0.5 * d * d)));
        }

        var peak = ArrivalTimePeak.FromArrivals(arrivals, arrivals.Count);
        Assert.True(peak.Skewness > 0.5, $"a quadratic aberration should skew positive; got {peak.Skewness:F3}");
    }

    [Fact]
    public void APeakWithNoSpreadIsAValidityViolationNotAnInfiniteResolution()
    {
        var peak = ArrivalTimePeak.FromArrivals(Enumerable.Repeat(100e-6, 50), 50);
        var (_, _, _, warnings) = peak.ResolvingPower();

        Assert.Contains(warnings, w => w.Code == "PEAK_UNRESOLVED" && !w.IsSuppressible);
    }

    [Fact]
    public void FocusingAnalysisRecoversKnownCoefficients()
    {
        // A constructed T(d) whose coefficients are known exactly.
        var samples = new List<(double, double)>();

        for (var k = 0; k < 11; k++)
        {
            var d = -0.05 + (0.1 * k / 10.0);
            samples.Add((d, 100e-6 * (1.0 + (0.25 * d) - (0.5 * d * d) + (0.125 * d * d * d))));
        }

        var focus = FocusingAnalysis.Fit(samples);

        Assert.Equal(0.25, focus.Coefficients[0], 1e-6);
        Assert.Equal(-0.5, focus.Coefficients[1], 1e-6);
        Assert.Equal(0.125, focus.Coefficients[2], 1e-6);
        Assert.Equal(1, focus.BindingOrder);
        Assert.Equal(100e-6, focus.NominalFlightTime, 1e-12);
        Assert.True(focus.ResidualOfFit < 1e-9);
    }

    [Fact]
    public void ACancelledFirstOrderTermRaisesTheBindingOrder()
    {
        var samples = new List<(double, double)>();

        for (var k = 0; k < 11; k++)
        {
            var d = -0.05 + (0.1 * k / 10.0);
            samples.Add((d, 100e-6 * (1.0 + (0.4 * d * d))));
        }

        var focus = FocusingAnalysis.Fit(samples);

        Assert.Equal(2, focus.BindingOrder);
        Assert.Equal(0.4, focus.Coefficients[1], 1e-6);
    }

    [Fact]
    public void FreeFlightIsRecognisableFromItsCoefficients()
    {
        // The signature that caught a structurally broken analyzer model: an
        // arrival time going as one over the square root of energy is free flight,
        // whatever the optics are supposed to be doing.
        var samples = new List<(double, double)>();

        for (var k = 0; k < 11; k++)
        {
            var d = -0.05 + (0.1 * k / 10.0);
            samples.Add((d, 100e-6 / Math.Sqrt(1.0 + d)));
        }

        var focus = FocusingAnalysis.Fit(samples);

        Assert.Equal(-0.5, focus.Coefficients[0], 1e-3);
        Assert.Equal(0.375, focus.Coefficients[1], 1e-2);
        Assert.Equal(-0.3125, focus.Coefficients[2], 1e-1);
    }

    [Fact]
    public void FittingNeedsEnoughSamplesForTheRequestedOrder()
    {
        var samples = new List<(double, double)> { (-0.01, 1e-6), (0.0, 1e-6), (0.01, 1e-6) };
        Assert.Throws<ArgumentException>(() => FocusingAnalysis.Fit(samples, maximumOrder: 5));
    }
}
