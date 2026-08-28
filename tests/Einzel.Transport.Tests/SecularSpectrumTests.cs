using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The secular-motion spectrum, against the Mathieu characteristic exponent.
/// </summary>
/// <remarks>
/// <para>
/// §12 asks for a secular frequency spectrum as a Class B figure, and the reason it
/// is worth having is not that a number is missing: it is that a nonlinear resonance
/// is <em>defined</em> by a frequency condition, <c>n_z β_z + n_r β_r = 2</c>, so a
/// loss measurement can find a resonance band and can never say what it is.
/// </para>
/// <para>
/// This is checkable against a closed form the engine has no part in. Mathieu theory
/// gives the characteristic exponent β from a continued fraction in a and q alone,
/// and puts the spectral lines at <c>(2n ± β) Ω / 2</c> — the secular line at
/// <c>n = 0</c> and the micromotion sidebands at <c>n = ±1</c>. The continued
/// fraction is evaluated here, in the test, from a and q; nothing about it comes
/// from the integrator, the field, or the periodogram.
/// </para>
/// </remarks>
public sealed class SecularSpectrumTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private const double DriveHz = 1.0e6;

    /// <summary>
    /// The Mathieu characteristic exponent, from its continued fraction.
    /// </summary>
    /// <remarks>
    /// The standard recursion — β² = a + upward tail + downward tail, each tail a
    /// continued fraction in q² — solved by damped fixed point. This is the closed
    /// form the measurement is checked against, so it is deliberately written here
    /// rather than shipped in the engine: a test that compared the engine's β to the
    /// engine's spectrum would be testing self-consistency.
    /// </remarks>
    private static double Beta(double a, double q, int depth = 40)
    {
        var beta = Math.Sqrt(Math.Max(a + (q * q / 2.0), 1e-6));

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var up = 0.0;
            var down = 0.0;

            for (var n = depth; n >= 1; n--)
            {
                up = q * q / (((beta + (2 * n)) * (beta + (2 * n))) - a - up);
                down = q * q / (((beta - (2 * n)) * (beta - (2 * n))) - a - down);
            }

            var next = Math.Sqrt(Math.Max(a + up + down, 1e-12));

            if (Math.Abs(next - beta) < 1e-14)
            {
                return next;
            }

            beta = 0.5 * (beta + next);
        }

        return beta;
    }

    /// <summary>Flies an ion in an ideal quadrupole and records its path.</summary>
    private static IReadOnlyList<TrajectorySample> Fly(double a, double q, int cycles)
    {
        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");
        var frequency = Quantity.From(DriveHz, "Hz");

        var field = IdealQuadrupoleRf.FromMathieu(
            a, q, species.Mass(), species.Charge(), frequency, radius);

        // Off axis in x only, so the x spectrum is the x motion and nothing else.
        var start = new PhaseState(new Vec3(0.2e-3, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0));

        var flight = cycles * field.ShortestPeriodSeconds;
        var aperture = radius.In("m");

        TrajectoryStopFunction inside = (in PhaseState s) =>
            aperture - Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Y));

        // Sixteen samples per RF cycle. Enough that the micromotion sidebands at
        // 1 +/- beta/2 times the drive are far below any aliasing, and the record is
        // long enough that 1/T is small against the secular line.
        var recorder = new TrajectoryRecorder(field.ShortestPeriodSeconds / 16.0);

        TrajectoryIntegrator.Integrate(
            start,
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-10, MaximumFlightTime = flight },
            inside,
            recorder);

        return recorder.Samples;
    }

    [Theory]
    [InlineData(0.10)]
    [InlineData(0.30)]
    [InlineData(0.50)]
    [InlineData(0.70)]
    [InlineData(0.85)]
    public void TheSecularLineIsWhereMathieuSaysItIs(double q)
    {
        var beta = Beta(0.0, q);
        var expected = beta * DriveHz / 2.0;

        var samples = Fly(0.0, q, 200);
        var spectrum = SecularSpectrum.From(samples, 0, 0.02 * DriveHz, 0.60 * DriveHz, 4000);

        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, interval, _, _) = peak;
        var measured = value.In("Hz");

        output.WriteLine(
            $"q {q:F2}  beta {beta:F6}  expected {expected / 1e3:F3} kHz  "
            + $"measured {measured / 1e3:F3} kHz  ({100.0 * (measured - expected) / expected:+0.000;-0.000} %)  "
            + $"resolution {spectrum.ResolutionHz / 1e3:F3} kHz over {spectrum.Samples} samples");

        // Within the record's own resolution, which is the only claim a periodogram
        // of finite length can make. The trial-frequency spacing is far finer than
        // this on purpose - a peak located to the trial spacing would be quoting a
        // precision the record does not contain.
        Assert.Equal(expected, measured, 1.5 * spectrum.ResolutionHz);

        Assert.True(interval.LowerSi <= expected && expected <= interval.UpperSi,
            "the reported interval should contain the closed form");
    }

    [Fact]
    public void TheMicromotionSidebandsAreThereToo()
    {
        // The structural check, and a sharper one than the secular line alone. An
        // ion in an RF field does not oscillate at one frequency: Mathieu's solution
        // is a sum over (2n +/- beta) Omega / 2, so the drive appears in the motion
        // as a pair of sidebands straddling it, at (1 -/+ beta/2) times the drive.
        // Finding both, at the right places, says the motion has the form the theory
        // gives and not merely the right lowest frequency.
        const double Q = 0.5;

        var beta = Beta(0.0, Q);
        var samples = Fly(0.0, Q, 200);

        // Up to 1.5 times the drive, so both n = +/-1 lines are inside the band.
        var spectrum = SecularSpectrum.From(samples, 0, 0.02 * DriveHz, 1.5 * DriveHz, 8000);
        var peaks = spectrum.Peaks(0.001);

        output.WriteLine($"beta {beta:F6}, {peaks.Count} line(s) above 0.001 power");

        foreach (var line in peaks.Take(6))
        {
            output.WriteLine($"  {line.FrequencyHz / 1e3,10:F2} kHz   power {line.Power:F5}");
        }

        foreach (var (name, expected) in new[]
                 {
                     ("secular  (n = 0)", beta * DriveHz / 2.0),
                     ("lower sideband", (2.0 - beta) * DriveHz / 2.0),
                     ("upper sideband", (2.0 + beta) * DriveHz / 2.0),
                 })
        {
            var nearest = peaks.OrderBy(l => Math.Abs(l.FrequencyHz - expected)).First();

            output.WriteLine(
                $"{name,-18} expected {expected / 1e3:F2} kHz, nearest line "
                + $"{nearest.FrequencyHz / 1e3:F2} kHz at power {nearest.Power:F5}");

            Assert.Equal(expected, nearest.FrequencyHz, 2.0 * spectrum.ResolutionHz);
        }
    }

    [Fact]
    public void AShortRecordSaysSoRatherThanQuotingAPrecisionItDoesNotHave()
    {
        // Ten cycles of the drive is about one secular period at q = 0.5, so the
        // resolution is comparable to the line being measured. The number is still
        // returned - taint, never block - and carries the qualification.
        var samples = Fly(0.0, 0.5, 10);
        var spectrum = SecularSpectrum.From(samples, 0, 0.02 * DriveHz, 0.60 * DriveHz, 4000);

        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, _, _, warnings) = peak;

        output.WriteLine(
            $"{value.In("Hz") / 1e3:F3} kHz, resolution {spectrum.ResolutionHz / 1e3:F3} kHz "
            + $"over {spectrum.RecordSeconds * 1e6:F2} us");

        Assert.Contains(warnings, w => w.Code == "spectrum.short-record");
    }

    [Fact]
    public void APeakStoppedByTheBandEdgeIsAViolationRatherThanAnAnswer()
    {
        // The same failure the optimiser's optimum-at-bound warning exists for: a
        // peak at the edge of the searched band looks exactly like a real one, and
        // the only thing that distinguishes them is that the band was declared
        // rather than discovered.
        // The secular line at q = 0.5 is 186.9 kHz, and this band stops at 180, so
        // the strongest thing inside it is the shoulder of a line that is not.
        var samples = Fly(0.0, 0.5, 200);
        var spectrum = SecularSpectrum.From(samples, 0, 0.02 * DriveHz, 0.18 * DriveHz, 2000);

        var peak = spectrum.Peak(0.01);

        Assert.NotNull(peak);

        var (value, _, _, warnings) = peak;

        output.WriteLine($"peak at {value.In("Hz") / 1e3:F3} kHz in a band ending at 180 kHz");

        Assert.Contains(warnings, w => w.Code == "spectrum.peak-at-band-edge");
    }

    [Fact]
    public void MotionWithNoVarianceHasNoSpectrum()
    {
        // An ion launched exactly on axis never moves, and a periodogram of a
        // constant is a division by zero dressed as an answer. Refused by name.
        var species = Peptide;

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, 0.5, species.Mass(), species.Charge(),
            Quantity.From(DriveHz, "Hz"), Quantity.From(4.0, "mm"));

        var recorder = new TrajectoryRecorder(field.ShortestPeriodSeconds / 16.0);

        TrajectoryIntegrator.Integrate(
            new PhaseState(Vec3.Zero, Vec3.Zero),
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-10,
                MaximumFlightTime = 20.0 * field.ShortestPeriodSeconds,
            },
            (in PhaseState s) => 1.0,
            recorder);

        Assert.Throws<ArgumentException>(
            () => SecularSpectrum.From(recorder.Samples, 0, 1e4, 1e6, 100));
    }
}
