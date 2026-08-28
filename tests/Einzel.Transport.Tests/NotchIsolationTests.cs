using System.Globalization;
using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Isolation efficiency against notch width — the last Class B figure §12 asks for.
/// </summary>
/// <remarks>
/// <para>
/// A stored-waveform isolation applies a broadband excitation across a pair of
/// electrodes whose spectrum covers every secular frequency in the mass range
/// <em>except</em> a notch at the one ion to keep. Everything in the comb is
/// resonantly pumped until it leaves; the ion in the notch is not driven and stays.
/// The design variable is the notch width, and the trade it buys is the figure:
/// <strong>too narrow and the ion of interest is excited by its neighbours' lines,
/// too wide and its neighbours survive.</strong>
/// </para>
/// <para>
/// An ion's secular frequency in a quadrupole is set by its Mathieu q, and q goes as
/// <c>1/m</c> at fixed amplitude — so a mass axis <em>is</em> a frequency axis, and
/// a notch in frequency is a window in mass. That is what makes this measurable with
/// nothing but a comb, an ideal quadrupole and a set of masses.
/// </para>
/// <para>
/// The comb is a harmonic series of a low fundamental, so its lines sit at
/// <c>k f0</c>. Each ion is at its own secular frequency; whether it is excited is
/// whether a surviving line lands on it. Nothing here is device-specific — the same
/// comb applied to a solved trap would do the same thing, and the reason this runs
/// on the analytic quadrupole is that the model format cannot yet declare a
/// supplementary excitation at a second frequency. That gap is stated in
/// docs/model-format.md rather than worked around.
/// </para>
/// </remarks>
public sealed class NotchIsolationTests(ITestOutputHelper output)
{
    private const double DriveHz = 1.0e6;

    /// <summary>How long the excitation lasts, in RF cycles.</summary>
    /// <remarks>
    /// Half a millisecond at 1 MHz. It sets everything else: the resonance width is
    /// <c>1/T</c>, so the comb spacing has to match it, and the amplitude has to be
    /// the one that grows an ion to the aperture in exactly this long.
    /// </remarks>
    private const int Cycles = 500;

    /// <summary>The comb's fundamental: its lines sit at multiples of this.</summary>
    /// <remarks>
    /// <strong>Equal to 1/T, and that is a design constraint rather than a choice.</strong>
    /// A resonance excited for a time T has a width of about 1/T, so a comb spaced
    /// more widely than that has holes in it - an ion falling between two lines is
    /// driven by neither and survives an excitation meant to eject it. Spaced more
    /// finely and the extra lines do nothing but cost. A first version used 5 kHz
    /// against a 333 Hz width and every result was nonsense in an interesting way:
    /// the notch width toggled every ion at once, because selectivity had nothing to
    /// do with it.
    /// </remarks>
    private const double CombFundamentalHz = DriveHz / Cycles;

    /// <summary>The mass to isolate.</summary>
    private const double TargetMass = 500.0;

    private static Quantity Radius => Quantity.From(4.0, "mm");

    /// <summary>
    /// The excitation amplitude that just ejects a resonant ion, in volts per metre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived rather than tuned. A resonantly driven oscillator grows linearly,
    /// <c>x(t) = (qE/m) t / (2 omega)</c>, so reaching the aperture <c>a</c> in a
    /// time <c>T</c> needs <c>E = 2 a m omega / (q T)</c>. Off resonance by one comb
    /// line the response is bounded at <c>(qE/m) / (2 omega dOmega)</c>, about a
    /// sixth of the aperture here - which is the contrast the measurement rests on.
    /// </para>
    /// <para>
    /// The first version used 300 V/m, four orders too much, and ejected every ion
    /// at every notch width. An amplitude picked to make a demonstration work is a
    /// demonstration of the amplitude.
    /// </para>
    /// </remarks>
    private static double AmplitudeFor(double secularHz, double massDa)
    {
        var species = IonSpecies.FromMassToCharge(massDa, 1);
        var omega = 2.0 * Math.PI * secularHz;

        return 2.0 * Radius.In("m") * species.MassSi * omega
            / (species.ChargeSi * (Cycles / DriveHz));
    }

    /// <summary>
    /// The Mathieu q of a mass at a fixed amplitude, relative to the target's.
    /// </summary>
    /// <remarks>
    /// q goes as 1/m, so the target sits at its declared q and everything else
    /// scales. That is the whole reason a frequency notch is a mass window.
    /// </remarks>
    private static double QFor(double mass, double targetQ) => targetQ * TargetMass / mass;

    /// <summary>Flies one mass through the comb and says whether it survived.</summary>
    private static (bool Survived, double ExcursionMm, double SecularHz) Fly(
        double mass, double targetQ, RfWaveform comb, double amplitudeSi, int cycles)
    {
        var species = IonSpecies.FromMassToCharge(mass, 1);
        var q = QFor(mass, targetQ);

        var quadrupole = IdealQuadrupoleRf.FromMathieu(
            0.0, q, species.Mass(), species.Charge(), Quantity.From(DriveHz, "Hz"), Radius);

        var dipole = OscillatingUniformField.Create(
            new Vec3(amplitudeSi, 0.0, 0.0), Quantity.From(CombFundamentalHz, "Hz"), comb);

        var field = new DrivenSuperposedField([quadrupole, dipole]);
        var aperture = Radius.In("m");

        var recorder = new TrajectoryRecorder(1.0 / (16.0 * DriveHz));

        var result = TrajectoryIntegrator.Integrate(
            new PhaseState(new Vec3(0.05e-3, 0.0, 0.0), Vec3.Zero),
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = cycles / DriveHz,
            },
            (in PhaseState s) => aperture - Math.Abs(s.Position.X),
            recorder);

        var excursion = 0.0;

        foreach (var sample in recorder.Samples)
        {
            excursion = Math.Max(excursion, Math.Abs(sample.Position.X));
        }

        return (result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached,
            excursion * 1e3,
            Beta(q) * DriveHz / 2.0);
    }

    /// <summary>The Mathieu characteristic exponent, from its continued fraction.</summary>
    private static double Beta(double q, int depth = 40)
    {
        var beta = Math.Sqrt(Math.Max(q * q / 2.0, 1e-6));

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var up = 0.0;
            var down = 0.0;

            for (var n = depth; n >= 1; n--)
            {
                up = q * q / (((beta + (2 * n)) * (beta + (2 * n))) - up);
                down = q * q / (((beta - (2 * n)) * (beta - (2 * n))) - down);
            }

            var next = Math.Sqrt(Math.Max(up + down, 1e-12));

            if (Math.Abs(next - beta) < 1e-14)
            {
                return next;
            }

            beta = 0.5 * (beta + next);
        }

        return beta;
    }

    [Fact]
    public void ANotchedCombEjectsTheNeighboursAndKeepsTheTarget()
    {
        // The measurement. The target at m/z 500 sits at q = 0.4, whose secular
        // frequency is about 146 kHz - comb order 29. Neighbours at other masses sit
        // at other orders. A notch over orders 28 to 30 leaves the target undriven
        // and everything else driven.
        const double TargetQ = 0.4;

        var target = Fly(TargetMass, TargetQ, new RfWaveform.Sinusoid(), 0.0, 100);

        var order = (int)Math.Round(target.SecularHz / CombFundamentalHz);
        var amplitude = AmplitudeFor(target.SecularHz, TargetMass);

        output.WriteLine(
            $"target m/z {TargetMass:F0} at q {TargetQ:F2}: secular {target.SecularHz / 1e3:F2} kHz, "
            + $"comb order {order}");
        output.WriteLine(
            $"comb fundamental {CombFundamentalHz / 1e3:F2} kHz over {Cycles} RF cycles, "
            + $"amplitude {amplitude:F4} V/m");

        var comb = RfWaveform.Harmonic.NotchedComb(30, 120, order - 2, order + 2);

        output.WriteLine($"comb orders 30 to 120, notch {order - 2} to {order + 2}");
        output.WriteLine("   m/z        q   secular (kHz)   order   excursion (mm)   survived");

        var kept = new List<double>();
        var ejected = new List<double>();

        foreach (var mass in new[] { 420.0, 460.0, 490.0, 500.0, 510.0, 545.0, 600.0 })
        {
            var (survived, excursion, secular) = Fly(mass, TargetQ, comb, amplitude, Cycles);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{mass,6:F0}  {QFor(mass, TargetQ),7:F4}  {secular / 1e3,14:F2}  "
                + $"{secular / CombFundamentalHz,6:F1}  {excursion,14:F4}   {survived}"));

            (survived ? kept : ejected).Add(mass);
        }

        output.WriteLine($"kept {string.Join(", ", kept)}");
        output.WriteLine($"ejected {string.Join(", ", ejected)}");

        // The target survives, and the masses well away from it do not. That is
        // isolation: not that every neighbour is ejected - the ones inside the notch
        // by construction are not - but that the notch selects.
        Assert.Contains(TargetMass, kept);
        Assert.Contains(420.0, ejected);
        Assert.Contains(600.0, ejected);
    }

    [Fact]
    public void EfficiencyIsATradeAgainstNotchWidth()
    {
        // The figure section 12 actually asks for. What makes it a trade rather than
        // a parameter to turn up is that BOTH failure modes are reachable, and they
        // pull opposite ways: widen the notch and neighbours inside it survive an
        // ejection meant for them; narrow it and the target is driven by the lines
        // that used to be notched out.
        //
        // Reaching both needs two amplitudes, and that is a finding rather than a
        // test artefact. At the amplitude that just ejects a resonant ion, the
        // narrow end is free - the target's off-resonance response is 1.5 mm against
        // a 4 mm aperture, so a notch one line wide costs nothing and efficiency is
        // simply monotone in width. Push the excitation harder and the narrow end
        // starts losing the target, and an interior optimum appears.
        const double TargetQ = 0.4;

        var target = Fly(TargetMass, TargetQ, new RfWaveform.Sinusoid(), 0.0, 100);
        var order = (int)Math.Round(target.SecularHz / CombFundamentalHz);
        var nominal = AmplitudeFor(target.SecularHz, TargetMass);

        // Chosen so two sit within a couple of orders of the target and two do not,
        // which is what lets a widening notch spare some and not others.
        var neighbours = new[] { 470.0, 485.0, 515.0, 535.0 };

        output.WriteLine(
            $"target order {order}, comb 30 to 120, fundamental {CombFundamentalHz / 1e3:F2} kHz");

        var curves = new Dictionary<string, List<(int Width, bool Kept, int Ejected, double Efficiency)>>(
            StringComparer.Ordinal);

        foreach (var (label, amplitude) in new[]
                 {
                     ("just ejecting", nominal),
                     ("three times that", 3.0 * nominal),
                 })
        {
            output.WriteLine($"\n{label}, {amplitude:F1} V/m");
            output.WriteLine(" half-width   target kept   neighbours ejected   efficiency");

            var rows = new List<(int Width, bool Kept, int Ejected, double Efficiency)>();

            foreach (var half in new[] { 0, 2, 6, 12 })
            {
                var comb = RfWaveform.Harmonic.NotchedComb(30, 120, order - half, order + half);

                var kept = Fly(TargetMass, TargetQ, comb, amplitude, Cycles).Survived;
                var gone = neighbours.Count(m => !Fly(m, TargetQ, comb, amplitude, Cycles).Survived);

                // An isolation that lost the ion it was isolating has efficiency
                // zero however many neighbours it ejected. That is not a scoring
                // convention - a purified sample of nothing is not a purification.
                var efficiency = kept ? gone / (double)neighbours.Length : 0.0;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{half,11}   {kept,11}   {gone,18}   {efficiency,10:F2}"));

                rows.Add((half, kept, gone, efficiency));
            }

            curves[label] = rows;
        }

        // At the gentler amplitude the narrow end costs nothing, so efficiency is
        // simply monotone in the notch width and best at its narrowest.
        var gentle = curves["just ejecting"];

        Assert.True(gentle[0].Kept, "the target should survive a one-line notch at this amplitude");
        Assert.Equal(1.0, gentle[0].Efficiency, 1e-12);

        for (var k = 1; k < gentle.Count; k++)
        {
            Assert.True(
                gentle[k].Efficiency <= gentle[k - 1].Efficiency,
                $"efficiency should not rise with notch width: {gentle[k - 1].Efficiency:F2} at "
                + $"{gentle[k - 1].Width} then {gentle[k].Efficiency:F2} at {gentle[k].Width}");
        }

        Assert.Equal(0.0, gentle[^1].Efficiency, 1e-12);

        // At the harder amplitude both arms are present and the optimum is interior:
        // the target is lost at the narrowest notch and the neighbours survive the
        // widest, so there is a width and it is neither end.
        var hard = curves["three times that"];

        Assert.False(hard[0].Kept, "a harder excitation should reach the target through a one-line notch");
        Assert.Equal(0.0, hard[0].Efficiency, 1e-12);

        var best = hard.MaxBy(r => r.Efficiency);

        output.WriteLine(
            $"\nbest at half-width {best.Width}, efficiency {best.Efficiency:F2}");

        // Strictly inside the range tested, with both ends worse: the narrow end
        // loses the target and the wide end spares the neighbours. That is the
        // trade, and an optimum at either end would mean only one arm was reached.
        Assert.True(
            best.Width > hard[0].Width && best.Width < hard[^1].Width,
            $"the optimum should be interior, and it is at half-width {best.Width}");

        Assert.True(
            best.Efficiency > hard[0].Efficiency && best.Efficiency > hard[^1].Efficiency,
            $"the optimum {best.Efficiency:F2} should beat both ends "
            + $"({hard[0].Efficiency:F2} and {hard[^1].Efficiency:F2})");
    }

    [Fact]
    public void ANotchThatSwallowsTheCombIsRefused()
    {
        // A notch wider than the comb leaves no excitation at all, which ejects
        // nothing and reads as perfect isolation. Refused by name rather than
        // returning a waveform of zero.
        var error = Assert.Throws<ArgumentException>(
            () => RfWaveform.Harmonic.NotchedComb(10, 20, 5, 30));

        output.WriteLine(error.Message);

        Assert.Contains("no excitation at all", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCombDeclaresItsFastestHarmonicAsItsPeriod()
    {
        // The step controller has to be told the truth about the fastest thing in
        // the field. A comb reaching order 120 carries information a hundred and twenty times faster
        // than its own repeat rate, and a controller given only the fundamental
        // would step over every one of those oscillations while its error estimator
        // agreed the step was accurate - for the field the step was shown.
        var comb = RfWaveform.Harmonic.NotchedComb(30, 120, 70, 76);

        var field = OscillatingUniformField.Create(
            new Vec3(1.0e3, 0.0, 0.0), Quantity.From(CombFundamentalHz, "Hz"), comb);

        output.WriteLine(
            $"fundamental {1.0 / CombFundamentalHz * 1e6:F1} us, "
            + $"shortest period {field.ShortestPeriodSeconds * 1e6:F4} us");

        Assert.Equal(1.0 / (CombFundamentalHz * 120.0), field.ShortestPeriodSeconds, 1e-15);
    }
}
