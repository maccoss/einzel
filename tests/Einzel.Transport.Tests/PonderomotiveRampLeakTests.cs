using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// What a ramping DC does to the cycle average that is supposed to describe only the drive.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism is structural rather than numerical.</b> `SequencedRun.Instant` hands the
/// solver `new TimeShiftedField(driven, atSeconds)` - a shifted VIEW of a field that is still
/// varying - not a frozen snapshot. So when `MeanSquareOscillating` averages over one drive
/// period it averages over a window in which the ramp is still moving, and to that average a
/// drift is indistinguishable from part of the quiver.
/// </para>
/// <para>
/// <b>`PonderomotiveField`'s own constructor argues the point and guards the wrong case.</b> It
/// refuses a field whose shortest period is not finite, saying "a field that varies in time
/// because a parameter ramps is not an oscillation and is not to be averaged". That is exactly
/// right, and it fires only when the field is a ramp and NOTHING else. A field that is a ramp
/// AND a drive passes, and every elution scan is one.
/// </para>
/// <para>
/// <b>What it produces is a bias, not a jitter, and that distinction is the finding.</b> A
/// linear ramp has a constant slope, so the term it injects is the same at every instant: the
/// well is offset, and a step-to-step comparison cancels the offset exactly. The cache's guard
/// compares step to step, so it is blind to this by construction - and the recorded control
/// that "refuted" the ramp hypothesis was comparing steps, which is why slowing the ramp did
/// not reduce what it was watching. The step-to-step jitter and this bias are two different
/// quantities and only one of them is the ramp's.
/// </para>
/// <para>
/// The expectations here are closed forms, not numbers this engine produced. For a sinusoid
/// sampled at N points across one period against a linear drift of rate r, the identity
/// sum_{s=0}^{N-1} s*cos(2*pi*s/N) = -N/2 gives a cross term of -A*r*T/N against a mean square
/// of A^2/2, so the relative bias is <b>-2*r*T/(N*A)</b> to first order - independent of the
/// DC level, of the probe, and of the instant. With the drift's own variance kept the form is
/// exact: it reproduces every measured digit at all five configurations below.
/// </para>
/// </remarks>
public sealed class PonderomotiveRampLeakTests(ITestOutputHelper output)
{
    private const double FrequencyHz = 850e3;

    private const double PeriodSeconds = 1.0 / FrequencyHz;

    /// <summary>The cycle sample count `PonderomotiveField` defaults to.</summary>
    private const int Samples = 16;

    private const double MassSi = 500.0 * 1.66053906892e-27;

    private const double ChargeSi = 1.602176634e-19;

    /// <summary>Where the well is probed. The bias is not local, so any fixed point does.</summary>
    private static readonly Vec3 Probe = new(0.010, 0.001, 0.0);

    private static readonly Vec3 AlongTheDrive = new(0.0, 1.0, 0.0);

    private static readonly Vec3 AcrossTheDrive = new(1.0, 0.0, 0.0);

    /// <summary>The closed-form relative bias a ramp of this rate puts into the well.</summary>
    /// <remarks>
    /// The window average of |osc + drift - mean|^2 is var(osc) + var(drift) + 2cov, with
    /// var(osc) = A^2/2 exactly for a sinusoid sampled at N points across a period. The
    /// covariance is what the identity sum s*cos(2*pi*s/N) = -N/2 evaluates: -A*r*T/(2N). The
    /// drift's own variance is second order in the rate and is kept because it is 1 per cent
    /// of the answer at the smallest amplitude here - dropping it is what a first-order form
    /// gets wrong, and it gets it wrong exactly where an eye would read the discrepancy as
    /// the model failing rather than as a term left out.
    /// </remarks>
    private static double Predicted(double rate, double amplitude)
    {
        var drift = rate * PeriodSeconds;
        var varianceOfDrift = drift * drift * ((Samples * Samples) - 1.0)
            / (12.0 * Samples * Samples);
        var twiceCovariance = -amplitude * drift / Samples;

        return (varianceOfDrift + twiceCovariance) / (amplitude * amplitude / 2.0);
    }

    /// <summary>
    /// A uniform drive plus a uniform DC that ramps, with the two directions declared
    /// separately so a test can make them parallel or perpendicular.
    /// </summary>
    /// <remarks>
    /// Deliberately the simplest field carrying the mechanism. A solved geometry carries it
    /// too, and also carries an interpolation error, a mesh and a shape, none of which is
    /// being measured here. Amplitudes and rates are in volts per metre.
    /// </remarks>
    private sealed class RampingDriven(
        double amplitudeVoltsPerMetre,
        double dcVoltsPerMetre,
        double rampVoltsPerMetrePerSecond,
        Vec3 dcDirection) : ITimeVaryingField
    {
        public double ShortestPeriodSeconds => PeriodSeconds;

        public double ResolutionLength => double.PositiveInfinity;

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
        {
            var drive =
                amplitudeVoltsPerMetre * Math.Cos(2.0 * Math.PI * FrequencyHz * timeSeconds);
            var dc = dcVoltsPerMetre + (rampVoltsPerMetrePerSecond * timeSeconds);

            return (AlongTheDrive * drive) + (dcDirection * dc);
        }

        public double PotentialAt(in Vec3 position, double timeSeconds) =>
            -Vec3.Dot(ElectricFieldAt(in position, timeSeconds), position);

        public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

        public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;
    }

    /// <summary>The well at the probe, seen from an instant the way a ramped phase sees it.</summary>
    private static double WellAt(
        double amplitude, double rate, Vec3 dcDirection, double atSeconds = 0.0)
    {
        ITimeVaryingField driven =
            new RampingDriven(amplitude, dcVoltsPerMetre: 1287.6, rate, dcDirection);

        var shifted = atSeconds > 0.0 ? new TimeShiftedField(driven, atSeconds) : driven;

        return new PonderomotiveField(shifted, ChargeSi, MassSi, collisionRateSi: 0.0)
            .WellAt(in Probe);
    }

    /// <summary>The relative difference a ramp of this rate makes to the well.</summary>
    private static double Bias(double amplitude, double rate, Vec3 dcDirection)
    {
        var held = WellAt(amplitude, 0.0, dcDirection);

        return (WellAt(amplitude, rate, dcDirection) - held) / held;
    }

    /// <summary>
    /// The control: with no ramp under it, a held drive gives the same well at every instant.
    /// </summary>
    /// <remarks>
    /// This is what makes the rest of the file mean anything. The sample instants are
    /// `PeriodSeconds * s / _samples` from the field's own zero, so shifting by a whole period
    /// lands on the same set of phases. It agrees to rounding rather than to the bit, and the
    /// reason is worth knowing: `TimeShiftedField` adds its offset in floating point, so
    /// `(t + T) + sT/N` and `(t + sT/N) + T` differ in their last bits. That ~1e-16 is the
    /// floor every measurement in this file sits on, and it is four orders below the bias.
    /// </remarks>
    [Fact]
    public void AHeldDriveGivesTheSameWellAtEveryInstant()
    {
        var first = WellAt(100.0, 0.0, AcrossTheDrive);
        var worst = 0.0;

        for (var s = 1; s <= 8; s++)
        {
            var now = WellAt(100.0, 0.0, AcrossTheDrive, s * PeriodSeconds);

            worst = Math.Max(worst, Math.Abs(now - first) / Math.Abs(first));
        }

        output.WriteLine($"held drive, eight periods apart: worst relative change {worst:G6}");

        Assert.True(worst < 1e-14, $"a held drive's well moved by {worst:G3} across a whole period");
    }

    /// <summary>
    /// A ramp under the drive biases the well, by the closed form and not merely in the same
    /// direction.
    /// </summary>
    /// <remarks>
    /// Three rates spanning a hundredfold, against `-2*r*T/(N*A)`. Asserting the closed form
    /// rather than a ratio between the measurements is what makes this a check on the
    /// mechanism: a scaling test passes for anything linear in the rate, including a linear
    /// term that is there for a different reason and of a different size.
    /// </remarks>
    [Fact]
    public void ARampBiasesTheWellByItsClosedForm()
    {
        const double amplitude = 100.0;

        double[] rates = [1.61e3, 1.61e4, 1.61e5];

        output.WriteLine("rate (V/m/s)        measured        predicted     ratio");

        foreach (var rate in rates)
        {
            var measured = Bias(amplitude, rate, AlongTheDrive);
            var predicted = Predicted(rate, amplitude);

            output.WriteLine(
                $"{rate,12:G3}   {measured,13:G6}   {predicted,13:G6}   {measured / predicted:F4}");

            Assert.InRange(measured / predicted, 0.999, 1.001);
        }
    }

    /// <summary>
    /// And the closed form holds across amplitude, where the relative bias goes as 1/A.
    /// </summary>
    /// <remarks>
    /// The cross term goes as `r*A` and the well as `A^2`, so the relative bias goes as `1/A`.
    /// Worth asserting separately because it is the scaling the shipped amplitude scan
    /// reported for the step-to-step jitter - 7.0e-5 / 1.8e-5 / 1.2e-6 at 25 / 100 / 400 V,
    /// an exponent of 0.98 across its first interval and 1.95 across its second. This file
    /// deliberately does not claim that scan is this effect: it is measured on a solved
    /// geometry and this is a uniform field, and the step-to-step test below shows the ramp
    /// contributes no step-to-step change at all.
    /// </remarks>
    [Fact]
    public void TheBiasScalesInverselyWithTheDriveAmplitude()
    {
        const double rate = 1.61e5;

        double[] amplitudes = [25.0, 100.0, 400.0];

        output.WriteLine("amplitude (V/m)     measured        predicted     ratio");

        foreach (var amplitude in amplitudes)
        {
            var measured = Bias(amplitude, rate, AlongTheDrive);
            var predicted = Predicted(rate, amplitude);

            output.WriteLine(
                $"{amplitude,15:F0}   {measured,13:G6}   {predicted,13:G6}   {measured / predicted:F4}");

            Assert.InRange(measured / predicted, 0.999, 1.001);
        }
    }

    /// <summary>
    /// The bias is invisible to a step-to-step comparison, which is what the cache's guard
    /// makes and what the recorded control measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A linear ramp's slope is constant, so the term it injects is identical at every
    /// instant. The well is offset and it is offset by the same amount every step, so
    /// differencing successive steps returns the floating-point floor whatever the rate is.
    /// </para>
    /// <para>
    /// <b>This is why slowing the ramp a hundredfold did not reduce the observed jitter.</b>
    /// The jitter was never the ramp's, so slowing the ramp could not move it. The two
    /// quantities have to be measured separately or a control on one will keep appearing to
    /// refute a hypothesis about the other.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStepToStepChangeIsRoundOffAndNotTheRamp()
    {
        const double amplitude = 100.0;

        output.WriteLine("rate (V/m/s)     bias         worst step-to-step change");

        foreach (var rate in (double[])[0.0, 1.61e3, 1.61e5])
        {
            var previous = WellAt(amplitude, rate, AlongTheDrive);
            var worst = 0.0;

            for (var s = 1; s <= 8; s++)
            {
                var now = WellAt(amplitude, rate, AlongTheDrive, s * PeriodSeconds);

                worst = Math.Max(worst, Math.Abs(now - previous) / Math.Abs(previous));
                previous = now;
            }

            var bias = rate > 0.0 ? Bias(amplitude, rate, AlongTheDrive) : 0.0;

            output.WriteLine($"{rate,12:G3}   {bias,11:G4}   {worst:G6}");

            Assert.True(
                worst < 1e-13,
                $"a rate of {rate:G3} produced a step-to-step change of {worst:G3}, so the ramp "
                + "does contribute one and the bias is not the whole story");
        }

        // The discrimination: at the fastest rate the bias is four orders above the floor the
        // step-to-step comparison sits at, so a guard built on differences cannot see it.
        var atSpeed = Math.Abs(Bias(amplitude, 1.61e5, AlongTheDrive));

        Assert.True(atSpeed > 1e-5, $"the bias itself came out at {atSpeed:G3}, too small to matter");
    }

    /// <summary>
    /// A ramp PERPENDICULAR to the drive biases nothing, because there is no cross term for it
    /// to enter through.
    /// </summary>
    /// <remarks>
    /// This is what makes the effect a property of the geometry rather than of the ramp alone,
    /// and it matters for reading the shipped analyser: its DC gradient is axial and its
    /// quadrupole RF is transverse, so on the axis the two are orthogonal and there is no
    /// bias at all, while off-axis the DC acquires a radial component and the cross term opens
    /// up in proportion to it. Estimating the effect from the axial gradient alone gives the
    /// wrong answer, and any fix has to be tested where the fields are not orthogonal.
    /// </remarks>
    [Fact]
    public void APerpendicularRampBiasesNothing()
    {
        const double rate = 1.61e5;

        var along = Math.Abs(Bias(100.0, rate, AlongTheDrive));
        var across = Math.Abs(Bias(100.0, rate, AcrossTheDrive));

        output.WriteLine($"ramp along the drive   {along:G6}");
        output.WriteLine($"ramp across it         {across:G6}");

        Assert.True(along > 1e-5, "the parallel control produced no bias, so this asserts nothing");

        Assert.True(
            across < along / 100.0,
            $"a perpendicular ramp biased the well by {across:G3} against {along:G3} for a "
            + "parallel one, which is not the orders of magnitude a missing cross term costs");
    }
}
