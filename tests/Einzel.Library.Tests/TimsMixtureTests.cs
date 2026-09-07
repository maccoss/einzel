using System.Text.Json.Nodes;

using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// Two ion populations in the tandem tunnel at once, and what their own charge does to the
/// separation between them.
/// </summary>
/// <remarks>
/// <para>
/// The question a trapped mobility analyser is designed around: how much charge can it hold
/// before the ions stop being separated by their mobility and start being separated by their
/// own space charge. Silveira and colleagues put the storable population at 10^6 to 10^7 from a
/// free-space line-charge estimate; this asks the geometry instead, with two populations that
/// can actually push on one another.
/// </para>
/// <para>
/// <b>Two species differing only in mobility.</b> Same mass, same charge, the second ten per
/// cent slower - which is a large gap for a mobility analyser and a small one for anything else.
/// Holding everything but the mobility fixed is what makes the separation attributable: two
/// species differing in mass would also differ in their diffusion and, in a driven structure,
/// in the well they feel.
/// </para>
/// </remarks>
public sealed class TimsMixtureTests(ITestOutputHelper output)
{
    /// <summary>The declared mobility of the template's own ion.</summary>
    private const double BaseMobility = 0.042802;

    /// <summary>How much slower the second population is.</summary>
    private const double SlowFraction = 0.90;

    /// <summary>
    /// The tandem template rewritten as a mixture of two populations.
    /// </summary>
    private static string Document(double totalIons, string spaceCharge, int intervalsX = 256)
    {
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-tandem"))!;

        // A mixture says what it is transporting in `species`, so the singular forms have to go:
        // the document would otherwise be saying the same thing twice and is refused for it.
        document["schemaVersion"] = "0.13";
        document.AsObject().Remove("ion");
        document["transport"]!.AsObject().Remove("mobility");
        document["source"]!["cloud"]!.AsObject().Remove("population");

        document["species"] = new JsonArray(
            JsonNode.Parse($$"""
                {
                  "name": "fast",
                  "massToCharge": { "value": 622.0, "unit": "Da" },
                  "chargeNumber": 1,
                  "mobility": { "zeroField": { "value": {{BaseMobility}}, "unit": "m^2/(V s)" } },
                  "population": {{totalIons / 2.0}}
                }
                """),
            JsonNode.Parse($$"""
                {
                  "name": "slow",
                  "massToCharge": { "value": 622.0, "unit": "Da" },
                  "chargeNumber": 1,
                  "mobility": { "zeroField": { "value": {{BaseMobility * SlowFraction}}, "unit": "m^2/(V s)" } },
                  "population": {{totalIons / 2.0}}
                }
                """));

        document["transport"]!["spaceCharge"] = spaceCharge;
        document["transport"]!["maximumFlightTime"] = JsonNode.Parse("""{ "value": 2500, "unit": "us" }""");
        document["transport"]!["densityGrid"]!["intervalsX"] = intervalsX;
        document["transport"]!["densityGrid"]!["intervalsY"] = 16;

        // Released inside the storage region: what the funnel delivers is measured elsewhere, and
        // what is isolated here is what two populations do to each other once they are parked.
        document["parameters"]!["sourceX"]!["value"] = 10.0;

        // One phase, holding, so nothing is eluting while the packets settle.
        document["sequence"] = new JsonArray(
            JsonNode.Parse("""
                { "name": "hold", "duration": { "value": 2500, "unit": "us" },
                  "set": { "storagePeakField": { "value": 5000, "unit": "V/m" },
                           "analysisPeakField": { "value": 5000, "unit": "V/m" } } }
                """));

        return document.ToJsonString();
    }

    /// <summary>
    /// The solved tunnel, built once for the whole study.
    /// </summary>
    /// <remarks>
    /// <b>The geometry does not change across these runs and the solve is nearly all the cost.</b>
    /// Only the populations and whether their charge is modelled vary, and neither touches an
    /// electrode - so re-solving 55 rings and two funnels per configuration would spend the study
    /// on one answer computed eight times. A first version did exactly that and ran for over an
    /// hour without finishing. <c>ExecuteMixture</c> takes the field as an argument precisely so
    /// a caller can hoist it.
    /// </remarks>
    private static readonly Lazy<(IElectrostaticField Field, IReadOnlyList<ValidityWarning> Warnings)> Tunnel =
        new(() =>
        {
            var reference = ModelValidator.Validate(ModelJson.Parse(Document(1e6, "none"))).Model!;
            var built = FieldAssembly.BuildReported(reference);

            return (built.Field, built.Warnings);
        });

    private static (MixtureOutcome Outcome, double Seconds) Run(
        double totalIons, string spaceCharge, int intervalsX = 256)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Document(totalIons, spaceCharge, intervalsX)));

        Assert.True(
            validation.IsValid,
            validation.IsValid
                ? string.Empty
                : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var (field, warnings) = Tunnel.Value;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var outcome = DiffusionRun.ExecuteMixture(
            validation.Model!, field, warnings,
            scheme: StepScheme.Implicit, stepGain: 64.0);

        return (outcome, clock.Elapsed.TotalSeconds);
    }

    /// <summary>Where a species sits along the axis, and how wide it is there, in millimetres.</summary>
    private static (double CentreMm, double WidthMm, double Ions) Peak(MixtureSpeciesResult species)
    {
        var density = species.Density;
        var grid = density.Grid;

        double sum = 0.0, first = 0.0, second = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < grid.CountX; i++)
            {
                var ions = density[i, j] * volume;
                var x = grid.X(i) * 1e3;

                sum += ions;
                first += ions * x;
                second += ions * x * x;
            }
        }

        if (sum <= 0.0)
        {
            return (double.NaN, double.NaN, 0.0);
        }

        var centre = first / sum;
        var variance = Math.Max(0.0, (second / sum) - (centre * centre));

        return (centre, Math.Sqrt(variance), sum);
    }

    /// <summary>
    /// Silveira's reference configuration expressed as a line density: 10^6 charges over 23 mm.
    /// </summary>
    /// <remarks>
    /// <b>"How many ions" is not a comparable quantity and this is.</b> Their estimate is a
    /// uniformly charged line, and what enters it is charge per unit length - so a population
    /// held in a short packet is a denser line than the same count spread over their 23 mm, and
    /// comparing the two by ion count alone would be comparing different configurations. The
    /// packets here park at well under a millimetre, so their ion counts are not their numbers.
    /// </remarks>
    private const double PublishedLineDensity = 1.0e6 / 23.0e-3;

    /// <summary>
    /// The two populations separate by mobility, and their own charge is what eventually stops
    /// them separating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ratio reported here is a spatial one and is not the instrument's resolving
    /// power.</b> These packets are held rather than eluted: the gap is how far apart their
    /// centres sit along the tunnel and the width is how far each is spread along it, both in
    /// millimetres, at an equilibrium reached long before the run ends. A real analyser's
    /// resolving power is measured in the time domain after a ramp, and the ramp is most of
    /// where it comes from - so this number is one or two orders below a published one by
    /// construction and must not be compared with it. What it is good for is a ratio against
    /// itself: the same geometry, the same two mobilities, with and without the populations'
    /// own charge.
    /// </para>
    /// <para>
    /// <b>The equilibrium is reached.</b> The packets are seeded 2 mm wide and settle to well
    /// under a millimetre; the approach is exponential with a time constant of about
    /// <c>1/(mu E')</c>, which for this mobility and this gradient is roughly 260 us against a
    /// 2500 us hold. The uncharged rows coming out identical at every population is the check
    /// that the settling is complete and population-independent, and it is asserted.
    /// </para>
    /// <para>
    /// <b>Ion count is not the comparable quantity.</b> These packets are sub-millimetre, where
    /// the published line-charge estimate is 10^6 charges spread over 23 mm - so the same count
    /// here is a far denser line. The table reports charge per metre per population for that
    /// reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void SpaceChargeCloseTheMobilitySeparation()
    {
        output.WriteLine($"two populations at K = {BaseMobility:G6} and {BaseMobility * SlowFraction:G6} "
            + $"m^2/(V s), a {1.0 - SlowFraction:P0} difference, split evenly");
        output.WriteLine("");
        var solved = System.Diagnostics.Stopwatch.StartNew();
        _ = Tunnel.Value;
        output.WriteLine($"the tunnel solved once in {solved.Elapsed.TotalSeconds:F1} s, and is reused below");
        output.WriteLine("");
        output.WriteLine("      total ions   charge   fast (mm)   slow (mm)   gap (mm)   width (mm)   R = gap/width   held       per m each   own V   cost");

        var measured = new List<(double Ions, string Charge, double Gap, double Width, double R,
            double Held, double PerMetre, double PeakVolts)>();

        foreach (var ions in new[] { 1e5, 1e7, 1e8 })
        {
            foreach (var mode in new[] { "none", "meanField" })
            {
                var (outcome, seconds) = Run(ions, mode);
                var fast = Peak(outcome.Result.Species[0]);
                var slow = Peak(outcome.Result.Species[1]);

                var gap = slow.CentreMm - fast.CentreMm;
                var width = 0.5 * (fast.WidthMm + slow.WidthMm);
                var resolution = width > 0.0 ? gap / width : double.NaN;

                // What is still in the tracked region, so a width that stops growing can be told
                // from ions that have stopped being there. A packet losing charge to the bore
                // looks exactly like one that has reached an equilibrium.
                var held = fast.Ions + slow.Ions;

                // The quantity the published estimate is actually about, and it is PER
                // POPULATION. A uniform line of length L has an RMS width of L/sqrt(12), so a
                // packet's equivalent length is sqrt(12) times the width measured here. The two
                // populations end up six to nine millimetres apart, which is many times either
                // width, so they are two separate lines rather than one - dividing the combined
                // count by a single width would report each of them sitting in twice the charge
                // it actually sits in.
                var equivalentLengthM = Math.Sqrt(12.0) * width * 1e-3;
                var perMetre = equivalentLengthM > 0.0 ? 0.5 * held / equivalentLengthM : 0.0;

                measured.Add((ions, mode, gap, width, resolution, held, perMetre,
                    outcome.Result.PeakSelfPotentialVolts));

                output.WriteLine($"    {ions,12:G3}   {mode,9}   {fast.CentreMm,9:F3}   {slow.CentreMm,9:F3}   "
                    + $"{gap,8:F3}   {width,10:F3}   {resolution,13:F2}   {held,9:G3}   {perMetre,10:G4}   "
                    + $"{outcome.Result.PeakSelfPotentialVolts,6:G3}   "
                    + $"[{outcome.Result.Steps,5} steps, {outcome.Result.SelfFieldSolves,4} solves, {seconds,6:F1} s]");
            }
        }

        output.WriteLine("");

        foreach (var ions in new[] { 1e5, 1e7, 1e8 })
        {
            var blind = measured.First(m => m.Ions == ions && m.Charge == "none");
            var charged = measured.First(m => m.Ions == ions && m.Charge == "meanField");

            output.WriteLine($"    {ions,12:G3}: charge widens the peaks {charged.Width / blind.Width:F3}x, "
                + $"moves the gap {charged.Gap / blind.Gap:F3}x, and takes the resolving power to "
                + $"{charged.R / blind.R:P1} of what it would be");
        }

        output.WriteLine("");
        output.WriteLine($"Silveira's reference line: {PublishedLineDensity:G4} charges per metre "
            + "(10^6 over 23 mm), which is what their estimate is a function of");

        foreach (var ions in new[] { 1e5, 1e7, 1e8 })
        {
            var charged = measured.First(m => m.Ions == ions && m.Charge == "meanField");

            output.WriteLine($"    {ions,12:G3}: {charged.PerMetre / PublishedLineDensity:F2}x their line density");
        }

        // What sets a held packet's width is the balance between the trapping gradient and the
        // thermal energy, so the scale a self-potential has to be compared against is kT/q and
        // not the analytical field. That is a different criterion from the one the published
        // line-charge estimate uses, and it bites at a much lower population.
        var thermal = 1.380649e-23 * 300.0 / 1.602176634e-19;

        output.WriteLine("");
        output.WriteLine($"kT/q at 300 K is {thermal:G4} V, which is what a held packet's width is set "
            + "against - so a self-potential above it is already the thing deciding the width");

        foreach (var row in measured.Where(m => m.Charge == "meanField"))
        {
            output.WriteLine($"    {row.Ions,12:G3}: peak self-potential {row.PeakVolts:G4} V = "
                + $"{row.PeakVolts / thermal:F1}x thermal");
        }

        // The uncharged rows must agree whatever the population, since with no charge the count
        // enters nothing. Without this the whole table could be reporting a run that did not vary
        // what it says it varied. To a tolerance rather than to the bit: the seed is scaled by the
        // population, so a width computed from it agrees mathematically and not in the last bits.
        var blindWidths = measured.Where(m => m.Charge == "none").Select(m => m.Width).ToArray();

        foreach (var w in blindWidths)
        {
            Assert.Equal(blindWidths[0], w, blindWidths[0] * 1e-9);
        }

        // The populations separate at all, which everything else here is relative to.
        var lowest = measured.First(m => m.Ions == 1e5 && m.Charge == "meanField");

        Assert.True(lowest.Gap > 0.0, $"the slower population is not downstream of the faster one: "
            + $"the gap is {lowest.Gap:F3} mm");

        // Charge widens the peaks, monotonically in the population.
        var withCharge = measured.Where(m => m.Charge == "meanField").ToArray();

        for (var k = 1; k < withCharge.Length; k++)
        {
            Assert.True(
                withCharge[k].Width > withCharge[k - 1].Width,
                $"{withCharge[k].Ions:G3} ions gave a width of {withCharge[k].Width:F3} mm against "
                + $"{withCharge[k - 1].Width:F3} at {withCharge[k - 1].Ions:G3}");
        }

        // And it pushes the two populations FURTHER apart while making the separation worse,
        // which is the finding: the gap grows and the resolving power still falls, because the
        // peaks widen faster than their centres move.
        var uncharged = measured.First(m => m.Charge == "none");
        var densest = withCharge[^1];

        Assert.True(densest.Gap > uncharged.Gap, "mutual repulsion did not push the populations apart");

        Assert.True(
            densest.R < 0.5 * uncharged.R,
            $"the resolving power went from {uncharged.R:F2} to {densest.R:F2}, which is not the "
            + "degradation this measures");

        // And the tunnel has a capacity: past it the extra charge does not broaden the peak
        // further, it simply does not stay. That is the more useful reading of the widths
        // levelling off between the two densest rows, and it can only be told from an
        // equilibrium by looking at what is still there.
        var byIons = withCharge.OrderBy(m => m.Ions).ToArray();
        var launchedRatio = byIons[^1].Ions / byIons[^2].Ions;
        var heldRatio = byIons[^1].Held / byIons[^2].Held;

        output.WriteLine("");
        output.WriteLine($"launching {launchedRatio:F0}x more held only {heldRatio:F2}x more, so the tunnel "
            + $"stops accepting somewhere near {byIons[^1].Held:G3} ions");

        Assert.True(
            heldRatio < 0.5 * launchedRatio,
            $"launching {launchedRatio:F0}x more held {heldRatio:F2}x more, so nothing here is "
            + "saturating and the widths levelling off is something else");

        // The uncharged control keeps everything it was given at the same populations, so what
        // saturates is the charge rather than the geometry.
        var blindDensest = measured.First(m => m.Charge == "none" && m.Ions == byIons[^1].Ions);

        Assert.True(
            byIons[^1].Held < 0.5 * blindDensest.Held,
            $"the charged run held {byIons[^1].Held:G3} against {blindDensest.Held:G3} uncharged, which "
            + "is not the spilling this asserts");
    }
}
