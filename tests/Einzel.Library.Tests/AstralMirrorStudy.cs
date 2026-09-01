using Einzel.Analysis;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Sweeps;
using Einzel.Core.Units;
using Einzel.Transport;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The Astral analyser's ion mirror, at its published potentials.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is published and what is not.</b> Stewart et al. give the mirror's electrical
/// design completely — five electrodes per mirror, "one grounded U0, one strongly
/// accelerating to provide spatial focusing U1, and three reflecting U2–U4", with the
/// potential of each as a coefficient of the nominal ion energy, optimised in simulation
/// to produce a flat time-of-flight over an interval of <b>4000 V ± 100 V</b>. What no
/// paper states is the electrode <i>lengths</i>, which is what decides where those
/// potential steps sit along the axis.
/// </para>
/// <para>
/// So the lengths are the free parameters here and the potentials are not. That makes this
/// an inverse problem rather than a reproduction: find a geometry consistent with the
/// published potentials and the published acceptance window. If the patent literature later
/// gives real dimensions they check what this found, which is a stronger result than being
/// handed them.
/// </para>
/// <para>
/// <b>This is the symmetric pair, not the asymmetric track.</b> The instrument's mirrors
/// converge slightly, so an ion drifts down their length and successive reflections sample
/// different field — which is exactly what <see cref="MirrorPair.Fly"/> cannot express,
/// since it computes one period and multiplies. That is a separate piece of work; what is
/// asked here is the question that does not depend on it, which is whether this potential
/// set focuses energy the way the paper says.
/// </para>
/// </remarks>
public sealed class AstralMirrorStudy(ITestOutputHelper output)
{
    /// <summary>Nominal ion energy, in electronvolts. Stewart et al.</summary>
    private const double NominalEnergy = 4000.0;

    /// <summary>The published acceptance half-width: 4000 V ± 100 V.</summary>
    private const double AcceptanceVolts = 100.0;

    /// <summary>
    /// Table 1's C coefficients: the potential of each electrode as a multiple of the
    /// nominal ion energy, at the optimised working point (TE1 = TE2 = 0).
    /// </summary>
    /// <remarks>
    /// U0 is grounded and is the profile's entrance. U1 is strongly accelerating — negative
    /// for a positive ion, so the beam is sped up on entry, which is what provides the
    /// spatial focusing. U2 is still below earth; U3 and U4 straddle the beam energy, so an
    /// ion at the nominal 4 keV turns between them.
    /// </remarks>
    private static readonly string[] Names = ["d1", "d2", "d3", "d4"];

    private static readonly double[] Coefficients = [-1.840, -1.158, 0.916, 1.503];

    /// <summary>Derived, not published: 30 m over 24 oscillations, out and back.</summary>
    private const double CapToCap = 30.0 / 24.0 / 2.0;

    /// <summary>The inclination the second prism sets, per the Astral paper.</summary>
    private const double InclinationDegrees = 2.0;

    private const int Oscillations = 24;


    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);
        Assert.True(validation.IsValid, validation.IsValid ? string.Empty : validation.Errors[0].Constraint);
        return validation.Model!;
    }

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>The five-electrode profile, with the four boundaries as free lengths.</summary>
    private static MirrorProfile Astral(IReadOnlyList<double> boundaries) =>
        new(
            [0.0, .. boundaries],
            [0.0, .. Coefficients.Select(c => c * NominalEnergy)]);

    /// <summary>The published potentials, and where an ion turns in them.</summary>
    /// <remarks>
    /// Before optimising anything: the potential set alone fixes the turning point, because
    /// an ion of energy E turns where the potential reaches E/q whatever the lengths are.
    /// At the nominal 4 keV that is between U3 (3664 V) and U4 (6012 V), which is what a
    /// reflecting stage is for — and the check that the coefficients have been read with
    /// the right sign convention.
    /// </remarks>
    [Fact]
    public void ThePublishedPotentialsReflectAFourKilovoltIon()
    {
        var volts = Coefficients.Select(c => c * NominalEnergy).ToArray();

        output.WriteLine($"U0  {0.0,10:F1} V   (grounded)");

        for (var k = 0; k < volts.Length; k++)
        {
            output.WriteLine(
                $"U{k + 1}  {volts[k],10:F1} V   ({Coefficients[k]:+0.000;-0.000} x {NominalEnergy:F0} eV)");
        }

        // U1 and U2 accelerate a positive ion: it enters at 4 keV and speeds up.
        Assert.True(volts[0] < 0.0 && volts[1] < 0.0);

        // And it turns between U3 and U4, because reflection is where the potential
        // reaches the beam energy.
        Assert.True(volts[2] < NominalEnergy, "U3 is below the beam energy, so the ion is still moving there");
        Assert.True(volts[3] > NominalEnergy, "U4 is above it, so the ion has turned before reaching it");

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"an ion at {NominalEnergy:F0} eV turns between U3 and U4, "
            + $"{(NominalEnergy - volts[2]) / (volts[3] - volts[2]):P1} of the way from U3 to U4");
    }

    /// <summary>The shipped mirror pair is at its focus when flown from a document.</summary>
    /// <remarks>
    /// <para>
    /// <b>It was not, and this is the regression guard.</b> `planar-mirror-pair.json`
    /// launched its ion at x = 0 — the mirror <i>entrance</i> — and collected it there, so
    /// the document flew the whole gap and back for every bounce. <see cref="MirrorPair"/>
    /// launches at the <i>mid-plane</i>, where one bounce has half the gap either side of
    /// it, and every published number for that device came from the library path.
    /// </para>
    /// <para>
    /// A first-order energy focus is a condition on the ratio of drift time to mirror time,
    /// so twice the drift per bounce is not a detuning to be trimmed out — it is a
    /// different instrument. The template declares 767 mm as its focus and the document
    /// reported <b>R = 92</b> there; a scan found the document's own optimum near 390 mm,
    /// which is half of it. Moving the launch to the mid-plane took the same declared
    /// geometry to <b>R = 136,915</b>, a factor of about 1,470.
    /// </para>
    /// <para>
    /// The remaining gap to the 316,681 in `docs/device-templates.md` is not this defect:
    /// that figure is measured at 6 degrees of inclination over a 41-point energy scan, and
    /// this is on-axis over nine. What is asserted here is the order of magnitude, because
    /// what regressed was three of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedMirrorPairIsAtItsFocusWhenFlownFromADocument()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("planar-mirror-pair"));

        var model = Compile(document);

        foreach (var window in new[] { 0.025, 0.03 })
        {
            var r = FiguresOfMerit.Evaluator("resolvingPower", energySpread: window, ions: 9)(model);

            output.WriteLine($"declared separation, +/-{window:P1}   R = {r:N0}");

            // Three orders below what this device does is what launching at the entrance
            // cost. The bound is loose on purpose: what is being guarded is that the
            // document flies the same drift-to-mirror ratio the library does, not a
            // particular resolving power.
            Assert.True(
                r > 20_000.0,
                $"the shipped mirror pair reported R = {r:N0} at its own declared "
                + "separation, which is far below what a two-stage mirror at a first-order "
                + "focus does. Check that the source and detector are at the mid-plane: at "
                + "the mirror entrance the document flies twice the drift per bounce");
        }
    }

    /// <summary>The electrode lengths that flatten the published acceptance window.</summary>
    /// <remarks>
    /// <para>
    /// <b>The inverse problem.</b> The potentials are published and fixed; the lengths are
    /// not published and are the design variables. What is asked of them is the property
    /// the paper states its coefficients were optimised for — a flat time of flight over
    /// <b>4000 V ± 100 V</b> — expressed as the resolving power over exactly that window.
    /// </para>
    /// <para>
    /// <b>One oscillation, and the answer holds for twenty-four.</b> Resolving power is
    /// t / 2dt and <see cref="MirrorPair"/> composes a periodic flight by multiplying one
    /// period, so both scale together and the ratio is the same at any count. That is the
    /// arithmetic-not-physics caveat recorded for the ion-processor handover, and here it
    /// is what makes the search affordable: the optimisation runs on a single bounce.
    /// </para>
    /// <para>
    /// Driven through the shipped optimiser over the template's own declared parameter
    /// surface, rather than a search written here — the same machinery that recovered
    /// Denison's round-rod ratio to 1.14148.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheElectrodeLengthsThatFlattenThePublishedWindow()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-mirror"));

        // Exactly the published window: 4000 +/- 100 V is +/- 2.5 per cent.
        var spread = AcceptanceVolts / NominalEnergy;

        var objective = FiguresOfMerit.Evaluator("resolvingPower", energySpread: spread, ions: 9);

        var before = objective(Compile(document));

        output.WriteLine($"published window      4000 +/- {AcceptanceVolts:F0} V  (+/-{spread:P1})");
        output.WriteLine($"guessed geometry      R = {before:N0}");

        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = Optimiser.Run(
            document,
            [
                new DesignVariable("d1", Quantity.From(5.0, "mm"), Quantity.From(120.0, "mm")),
                new DesignVariable("d2", Quantity.From(10.0, "mm"), Quantity.From(200.0, "mm")),
                new DesignVariable("d3", Quantity.From(20.0, "mm"), Quantity.From(280.0, "mm")),
                new DesignVariable("d4", Quantity.From(30.0, "mm"), Quantity.From(340.0, "mm")),
            ],
            objective,
            ObjectiveSense.Maximise,
            OptimisationAlgorithm.NelderMead,
            new OptimisationSettings { MaximumEvaluations = 240, Restarts = 1 });

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"optimised             R = {result.Objective.Format("1")}  "
            + $"in {result.Evaluations} evaluations, {clock.Elapsed.TotalSeconds:F0} s"
            + $"{(result.Converged ? string.Empty : " (budget exhausted)")}");

        foreach (var entry in result.Best.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {entry.Key}  {entry.Value.Format("mm")}");
        }

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"  [{warning.Severity}] {warning.Code}");
        }

        // The depths have to stay ordered, or the profile is not a mirror: an electrode
        // deeper than the one behind it would make the potential non-monotonic in a way
        // the published set is not.
        // Deconstructed, because Measured offers no way to take the scalar alone
        // (GRD-1) - the envelope comes with it whether the caller wants it or not.
        static double Millimetres(Core.Results.Measured measured)
        {
            var (value, _, _, _) = measured;
            return value.In("mm");
        }

        var depths = Names.Select(n => Millimetres(result.Best[n])).ToArray();

        output.WriteLine(
            $"  ordered? {string.Join(" < ", depths.Select(x => $"{x:F1}"))}");

        // The search found something better than the guess, which is the claim this test
        // makes on its own. What that number IS gets compared against the paper next door.
        Assert.NotNull(before);

        var (optimum, _, _, _) = result.Objective;

        Assert.True(
            optimum.SiValue > (before ?? 0.0),
            $"the optimiser returned R = {optimum.SiValue:N0} against {before:N0} "
            + "for the starting geometry, so the search found nothing");
    }
}
