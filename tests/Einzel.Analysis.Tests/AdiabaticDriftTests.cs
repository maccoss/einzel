using Einzel.Analysis;
using Xunit.Abstractions;

namespace Einzel.Analysis.Tests;

/// <summary>
/// The adiabatic separation of a fast oscillation from a slow drift.
/// </summary>
/// <remarks>
/// Every expectation here is a closed form or an exact invariant. Two of them are
/// sharp in a way a device measurement cannot be: a harmonic effective potential
/// is <b>exactly</b> isochronous, so the spread in period must vanish to rounding
/// whatever amplitude range is asked for, and a linear one has an elementary
/// closed form for both the turning point and the time to reach it. Neither is
/// something this code knows, so passing them is a statement about the quadrature
/// rather than about any instrument.
/// </remarks>
public sealed class AdiabaticDriftTests(ITestOutputHelper output)
{
    // m/z 500, singly charged
    private const double ChargeToMass = 1.602176634e-19 / (500 * 1.66053906660e-27);

    /// <summary>A constant slow force reproduces its elementary closed form.</summary>
    /// <remarks>
    /// With an effective potential <c>phi = g z</c> volts, the slow motion is
    /// uniform deceleration: it turns at <c>z = E / g</c> and takes
    /// <c>sqrt(2 E / ((q/m) g^2))</c> to get there. This is the case the Astral's
    /// bare mirror tilt produces, and it is why that drift's return time is exactly
    /// proportional to the injected sideways speed.
    /// </remarks>
    [Theory]
    [InlineData(3.8594, 11.52)]     // the published Astral drift energy and gradient
    [InlineData(1.0, 5.0)]
    [InlineData(25.0, 200.0)]
    public void AConstantSlowForceMatchesItsClosedForm(double energyVolts, double gradientVoltsPerMetre)
    {
        var expectedTurning = energyVolts / gradientVoltsPerMetre;
        var expectedTime = Math.Sqrt(
            2.0 * energyVolts / (ChargeToMass * gradientVoltsPerMetre * gradientVoltsPerMetre));

        var m = AdiabaticDrift.Motion(
            z => gradientVoltsPerMetre * z, energyVolts, ChargeToMass, searchTo: 10.0);

        output.WriteLine(
            $"E {energyVolts} V, g {gradientVoltsPerMetre} V/m: turning {m.TurningPoint:F6} m "
            + $"(closed form {expectedTurning:F6}), half period {m.HalfPeriod * 1e6:F4} us "
            + $"(closed form {expectedTime * 1e6:F4})");

        Assert.True(m.Bracketed);
        Assert.Equal(expectedTurning, m.TurningPoint, 12);
        Assert.Equal(expectedTime, m.HalfPeriod, expectedTime * 1e-4);
    }

    /// <summary>
    /// A harmonic effective potential is exactly isochronous, and that is the sharp
    /// test of the quadrature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The period of a harmonic oscillator does not depend on its amplitude. So the
    /// spread this routine reports over <i>any</i> range of slow energy must vanish
    /// to rounding - not be small, vanish - and the half period must equal a
    /// quarter of <c>2 pi / omega</c> with <c>omega = sqrt((q/m) k)</c>.
    /// </para>
    /// <para>
    /// This is the same class of check as the maximum principle in the field
    /// solver: an invariant with no tolerance in it, which a quadrature that
    /// mishandles the endpoint singularity cannot pass by being nearly right. The
    /// integrand diverges as one over the square root of the distance to the
    /// turning point, and integrating through that instead of substituting it away
    /// loses half the available digits.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1.0e4)]
    [InlineData(1.0e6)]
    public void AHarmonicWellIsExactlyIsochronous(double stiffnessVoltsPerMetreSquared)
    {
        double Phi(double z) => 0.5 * stiffnessVoltsPerMetreSquared * z * z;

        var omega = Math.Sqrt(ChargeToMass * stiffnessVoltsPerMetreSquared);
        var expectedQuarter = 0.5 * Math.PI / omega;

        var m = AdiabaticDrift.Motion(Phi, slowEnergyVolts: 4.0, ChargeToMass, searchTo: 1.0);
        Assert.True(m.Bracketed);
        Assert.Equal(expectedQuarter, m.HalfPeriod, expectedQuarter * 1e-4);

        // the amplitude-independence, over a range far wider than any real spread
        var wide = AdiabaticDrift.Isochronicity(
            Phi, slowEnergyVolts: 4.0, ChargeToMass, searchTo: 1.0, energyFraction: 0.5);
        Assert.NotNull(wide);

        var (wideValue, wideInterval, _, _) = wide!;
        var relative = wideInterval.WidthSi / wideValue.SiValue;
        output.WriteLine(
            $"k {stiffnessVoltsPerMetreSquared:E0}: quarter period {m.HalfPeriod * 1e6:F4} us "
            + $"(closed form {expectedQuarter * 1e6:F4}); spread over +/-50% of slow energy "
            + $"{relative:E2}");

        // 1e-9 is quadrature noise on a 20,000-interval midpoint rule, not physics.
        Assert.True(
            relative < 1e-9,
            $"a harmonic well is exactly isochronous, so the reported spread should be "
            + $"quadrature noise; it was {relative:E3} over a +/-50% range of slow energy");
    }

    /// <summary>
    /// The published Astral drift shape puts a stationary point of the drift period
    /// exactly at the nominal energy, which is what an isochronous drift means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grinfeld et al. (Nucl. Instrum. Methods Phys. Res. A 1060, 2024, 169017) give the
    /// optimised drift pseudopotential outright: the mirrors' part is <c>psi_m = a0 eta</c>
    /// with <c>a0 = 0.83999</c>, the stripe's is a fifth-order polynomial, and the two sum
    /// to 1 at <c>eta = 1</c>, which is what makes the ion turn there.
    /// </para>
    /// <para>
    /// <b>What is asserted is the stationary point, not a spread</b>, and that distinction
    /// cost a wrong claim in three documents before it was noticed. A spread depends on the
    /// range chosen and on whether it is measured end to end or as a maximum over the range
    /// - measured end to end over a narrow range the published shape looks like one part in
    /// a million, and the same shape over the published plus or minus ten per cent gives one
    /// part in a hundred. The design property that does not depend on either choice is that
    /// <c>d(tau)/d(energy)</c> <b>vanishes at the operating point</b>: the period has a
    /// minimum there, so the residual is quadratic rather than linear in the energy offset.
    /// </para>
    /// <para>
    /// The control is the second theory case: a bare mirror tilt gives a linear effective
    /// potential, whose period goes as the square root of the drift energy, so the same
    /// derivative is exactly +0.5. That is what makes the published shape's 1e-5 a
    /// measurement rather than a small number - it is five orders below the uncorrected
    /// drift it replaces.
    /// </para>
    /// <para>
    /// <b>Not reproduced:</b> the paper's stated 2.1e-6 over a plus or minus ten per cent
    /// range. This formulation gives about 2e-3 there, and the gap is not the printed
    /// coefficients - closing their sum to exactly one, or perturbing one by a unit in its
    /// last printed digit, changes nothing. See <c>docs/astral-handoff.md</c> section 51.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("published stripe plus tilt", 1.0e-4)]
    [InlineData("bare tilt, the uncorrected drift", 0.6)]
    public void ThePublishedAstralDriftHasAStationaryPeriodAtNominal(string which, double bound)
    {
        const double A0 = 0.83999;
        double[] c = [0.75160, -7.52535, 14.0242, -9.17661, 2.08613];
        const double DriftLengthM = 0.335;
        var driftEnergy = 4000.0 * Math.Pow(Math.Sin(1.78 * Math.PI / 180.0), 2.0);

        var stripe = which.StartsWith("published", StringComparison.Ordinal);

        // the coefficients must sum with a0 to 1 at eta = 1, or they have been mistyped
        var sum = A0;
        foreach (var ck in c)
        {
            sum += ck;
        }

        Assert.Equal(1.0, sum, 4);

        double Phi(double z)
        {
            var eta = z / DriftLengthM;
            var psi = A0 * eta;
            if (stripe)
            {
                var power = eta;
                foreach (var ck in c)
                {
                    psi += ck * power;
                    power *= eta;
                }
            }
            else
            {
                // the bare tilt alone must still turn the ion at eta = 1, or the two cases
                // are being compared at different drift lengths
                psi = eta;
            }

            return driftEnergy * psi;
        }

        double HalfPeriod(double ratio)
        {
            var m = AdiabaticDrift.Motion(Phi, driftEnergy * ratio, ChargeToMass, searchTo: 0.6);
            Assert.True(m.Bracketed, $"no turning point at E/E0 = {ratio}");
            return m.HalfPeriod;
        }

        var t0 = HalfPeriod(1.0);
        var slope = (HalfPeriod(1.02) - HalfPeriod(0.98)) / 0.04 / t0;

        output.WriteLine(
            $"{which}: half period {t0 * 1e6:F3} us, d(t/t)/d(E/E0) = {slope:+.5f}");

        if (stripe)
        {
            Assert.InRange(Math.Abs(slope), 0.0, bound);

            // and it is a minimum, so both sides are slower - the residual is quadratic
            Assert.True(HalfPeriod(0.95) > t0, "the period should rise below nominal");
            Assert.True(HalfPeriod(1.05) > t0, "the period should rise above nominal");
        }
        else
        {
            // t goes as sqrt(E) for a constant force, so the derivative is exactly +0.5
            Assert.Equal(0.5, slope, 0.01);
        }
    }

    /// <summary>An escaping slow motion is reported, not extrapolated.</summary>
    /// <remarks>
    /// A slow energy above the effective potential everywhere in the range has no
    /// turning point, and reporting the end of the range as one would be a drift
    /// length invented by the search bound. This is the case a drift-control
    /// electrode that stops too short produces: past its end the potential decays,
    /// and an ion with slightly more sideways energy runs on.
    /// </remarks>
    [Fact]
    public void AnEscapingSlowMotionIsReportedRatherThanExtrapolated()
    {
        // a well 1 V deep, and 5 V of slow energy
        var m = AdiabaticDrift.Motion(
            z => 1.0 * (1.0 - Math.Exp(-z / 0.05)), slowEnergyVolts: 5.0, ChargeToMass, searchTo: 1.0);

        Assert.False(m.Bracketed);

        var iso = AdiabaticDrift.Isochronicity(
            z => 1.0 * (1.0 - Math.Exp(-z / 0.05)), 5.0, ChargeToMass, searchTo: 1.0);

        Assert.Null(iso);
        output.WriteLine("a slow motion that never turns is unbracketed, and the isochronicity null");
    }

    /// <summary>
    /// Fast-orbit weights that do not sum to one are refused rather than normalised.
    /// </summary>
    /// <remarks>
    /// The weights are the fraction of the fast period spent at each sample, so a sum
    /// that is not one scales the whole effective potential by that factor - and a
    /// scaled effective potential gives a turning point and a period that both look
    /// entirely reasonable. Normalising instead of refusing would pass this test
    /// while hiding a caller who has computed arc-length weights and called them time
    /// weights.
    /// </remarks>
    [Fact]
    public void FastOrbitWeightsThatDoNotSumToOneAreRefused()
    {
        (double Sample, double Weight)[] bad = [(0.0, 0.5), (1.0, 0.2)];
        var ex = Assert.Throws<ArgumentException>(
            () => AdiabaticDrift.EffectivePotential<double>(
                (_, z) => z, bad, [0.0, 0.1]));
        Assert.Contains("0.7", ex.Message, StringComparison.Ordinal);

        (double Sample, double Weight)[] good = [(0.0, 0.5), (1.0, 0.5)];
        var profile = AdiabaticDrift.EffectivePotential<double>(
            (sample, z) => sample + z, good, [0.0, 1.0]);

        // the average of samples 0 and 1 at equal weight, plus z
        Assert.Equal(0.5, profile[0], 12);
        Assert.Equal(1.5, profile[1], 12);
        output.WriteLine($"weights summing to 0.7 refused; equal weights average correctly");
    }
}
