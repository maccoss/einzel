using System.Text.Json.Nodes;

using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The second-generation tunnel: two regions, so one population is stored while another is
/// analysed. The device's claim is spatial decoupling, and that is what is asked here.
/// </summary>
/// <remarks>
/// <para>
/// Silveira's 96 mm tunnel holds an accumulation trap upstream of the analysis region, each
/// a rising edge where ions park by mobility and a plateau they elute across, with the field
/// on the rising edge going as the square root of position. The parallel-accumulation mode
/// then accumulates and analyses at the same time in different places, which is what takes
/// the duty cycle to 100 per cent.
/// </para>
/// <para>
/// <b>The square-root edge is measurably a different device from the analyser's linear
/// one.</b> An ion parks where the field balances the gas, so on a field going as the square
/// root of position the parking point goes as the <em>square</em> of one over the mobility,
/// where the analyser template's linear field gives one over the mobility. That is a
/// discriminating check on the profile rather than a plausibility one: a model that had
/// merely got the magnitude right would show the wrong exponent.
/// </para>
/// <para>
/// The packet is released inside the storage region rather than up the funnel, because the
/// funnel's delivery is what `TimsFrontEndTests` measures; what is isolated here is what the
/// two regions do once ions are in them.
/// </para>
/// </remarks>
public sealed class TimsTandemTests(ITestOutputHelper output)
{
    /// <summary>
    /// The storage region's extents, read from the template rather than repeated here.
    /// </summary>
    /// <remarks>
    /// Repeating them would make the test pass or fail on whether two files agree about a
    /// number, which is not what it is for - and the extents moved once already, when the
    /// first version borrowed the first-generation tunnel's 23 mm rise and 18 mm plateau for
    /// a 96 mm tunnel that has neither.
    /// </remarks>
    private static (double Start, double Rise, double Plateau, double PeakField) Storage()
    {
        var parameters = ModelValidator
            .Validate(ModelJson.Parse(DeviceTemplates.Read("tims-tandem"))).Model!
            .Parameters.Parameters;

        return (parameters["storageStart"].Value.SiValue * 1e3,
                parameters["storageRise"].Value.SiValue * 1e3,
                parameters["storagePlateau"].Value.SiValue * 1e3,
                parameters["storagePeakField"].Value.SiValue);
    }

    private static (DiffusiveOutcome Outcome, CompiledModel Model) Run(
        double mobilityRatio,
        double sourceMm,
        double flightUs,
        double storageField = 5000.0,
        double analysisField = 5000.0,
        double? analysisRampTo = null)
    {
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-tandem"))!;

        document["parameters"]!["sourceX"]!["value"] = sourceMm;
        document["parameters"]!["storagePeakField"]!["value"] = storageField;
        document["parameters"]!["analysisPeakField"]!["value"] = analysisField;

        var mobility = 0.042802 * mobilityRatio;
        document["parameters"]!["mobilityZeroField"]!["value"] = mobility;
        document["transport"]!["mobility"]!["zeroField"]!["value"] = mobility;

        document["transport"]!["maximumFlightTime"] = JsonNode.Parse($$$"""{ "value": {{{flightUs}}}, "unit": "us" }""");
        document["transport"]!["densityGrid"]!["intervalsX"] = 256;
        document["transport"]!["densityGrid"]!["intervalsY"] = 16;

        // One phase, or two where the analysis field is ramped: the shipped sequence's fill
        // is the funnel's business and is tested there.
        document["sequence"] = analysisRampTo is { } to
            ? new JsonArray(
                JsonNode.Parse($$$"""
                    { "name": "hold", "duration": { "value": {{{flightUs / 4.0}}}, "unit": "us" },
                      "set": { "storagePeakField": { "value": {{{storageField}}}, "unit": "V/m" },
                               "analysisPeakField": { "value": {{{analysisField}}}, "unit": "V/m" } } }
                    """),
                JsonNode.Parse($$$"""
                    { "name": "scan", "duration": { "value": {{{flightUs * 0.75}}}, "unit": "us" },
                      "set": { "storagePeakField": { "value": {{{storageField}}}, "unit": "V/m" },
                               "analysisPeakField": { "value": {{{analysisField}}}, "unit": "V/m" } },
                      "ramp": { "analysisPeakField": { "value": {{{to}}}, "unit": "V/m" } } }
                    """))
            : new JsonArray(
                JsonNode.Parse($$$"""
                    { "name": "hold", "duration": { "value": {{{flightUs}}}, "unit": "us" },
                      "set": { "storagePeakField": { "value": {{{storageField}}}, "unit": "V/m" },
                               "analysisPeakField": { "value": {{{analysisField}}}, "unit": "V/m" } } }
                    """));

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var built = FieldAssembly.BuildReported(validation.Model!);
        var outcome = DiffusionRun.Execute(validation.Model!, built.Field, built.Warnings, scheme: StepScheme.Implicit, stepGain: 64.0);
        return (outcome, validation.Model!);
    }

    /// <summary>How much of the density lies between two axial positions, of what was launched.</summary>
    private static double Between(DiffusiveOutcome outcome, double fromMm, double toMm)
    {
        var density = outcome.Result.Density;
        var ions = 0.0;

        for (var j = 0; j < density.Grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < density.Grid.CountX; i++)
            {
                var x = density.Grid.X(i) * 1e3;

                if (x >= fromMm && x < toMm)
                {
                    ions += density[i, j] * volume;
                }
            }
        }

        // Of what SURVIVES, not of what was launched. This stage carries no radial confinement
        // over the funnel-wide tracked region, so about a fifth of any packet reaches the bore
        // during a millisecond hold - equally in every arm of every comparison here. Dividing
        // by the launched population would make "where is the density" unanswerable above
        // four fifths and would have the tests failing on a loss they are not about.
        return ions / outcome.Result.Remaining;
    }

    /// <summary>What fraction of the launched population is still in flight at all.</summary>
    private static double Surviving(DiffusiveOutcome outcome) =>
        outcome.Result.Remaining / outcome.Launched;

    /// <summary>
    /// On a rising edge whose field goes as the square root of position, the parking point
    /// goes as the square of one over the mobility.
    /// </summary>
    [Fact]
    public void ParkingPositionGoesAsTheSquareOfOneOverMobility()
    {
        var measured = new Dictionary<double, double>();
        var (start, rise, _, peak) = Storage();

        foreach (var ratio in new[] { 1.5, 1.0, 0.75 })
        {
            var (outcome, _) = Run(ratio, sourceMm: 10.0, flightUs: 2500.0);
            var (x, _) = outcome.Result.Density.Centroid();
            measured[ratio] = x * 1e3;

            // Where the declared profile balances the gas: the field is peak sqrt(z / rise)
            // and the ion is held where mu E = v_g, so z = rise (v_g / (K peak))^2.
            var balance = start + (rise * Math.Pow(150.0 / (0.042802 * ratio * peak), 2.0));

            output.WriteLine($"K x {ratio:F2}: parked at {measured[ratio]:F2} mm, closed form {balance:F2} mm");

            Assert.Equal(balance, measured[ratio], 2.5);
            Assert.True(measured[ratio] < start + rise, "the ion parked past the rising edge, where the balance is not stable");
        }

        // The exponent, which is what separates this profile from the analyser's linear one:
        // halving the mobility should move the parking point out by four, not by two.
        var ratioOfPositions = measured[0.75] / measured[1.5];
        var square = Math.Pow(1.5 / 0.75, 2.0);

        output.WriteLine($"position ratio across a factor of two in mobility: {ratioOfPositions:F2} against {square:F2} for an inverse square, {1.5 / 0.75:F2} for an inverse");

        Assert.Equal(square, ratioOfPositions, 0.8);
    }

    /// <summary>
    /// Scanning the analysis region does not release what the storage region holds, which is
    /// the whole claim of the two-region design.
    /// </summary>
    [Fact]
    public void ScanningTheAnalysisRegionLeavesTheStoredPopulationAlone()
    {
        // Held while the analysis field is walked all the way to zero.
        var (scanned, _) = Run(1.0, sourceMm: 10.0, flightUs: 3000.0, analysisRampTo: 0.0);

        var (start, rise, plateau, _) = Storage();
        var end = start + rise + plateau;
        var stayed = Between(scanned, 0.0, end);
        var leaked = Between(scanned, end, 200.0);
        var (x, _) = scanned.Result.Density.Centroid();

        output.WriteLine($"analysis field ramped 5000 -> 0 V/m: {stayed:P2} of the surviving density still in the "
            + $"storage region (centre {x * 1e3:F2} mm), {leaked:P3} past it, "
            + $"{scanned.Result.Collected / scanned.Launched:P3} collected; {Surviving(scanned):P1} of the launched "
            + "population survived the hold at all");

        // The claim is decoupling, so what must be true is that nothing crossed out of the
        // storage region and nothing reached the detector - not that no ion was lost to the
        // wall, which happens equally whatever the analysis region is doing.
        Assert.True(stayed > 0.99, $"only {stayed:P1} of the surviving density stayed in the storage region while the analysis region was scanned");
        Assert.True(leaked < 0.005, $"{leaked:P2} of the density crossed out of the storage region during the scan");
        Assert.True(scanned.Result.Collected / scanned.Launched < 0.001, "the scan eluted the stored population, so the regions are not decoupled");
    }

    /// <summary>
    /// And pulsing the storage field down does release it: the gas carries it over what was
    /// the barrier and into the analysis region.
    /// </summary>
    [Fact]
    public void ThePulseTransfersTheStoredPopulationIntoTheAnalysisRegion()
    {
        var (held, _) = Run(1.0, sourceMm: 10.0, flightUs: 2500.0);
        var (pulsed, _) = Run(1.0, sourceMm: 10.0, flightUs: 2500.0, storageField: 0.0);

        var (start, rise, plateau, _) = Storage();
        var end = start + rise + plateau;
        var heldInStorage = Between(held, 0.0, end);
        var pulsedInStorage = Between(pulsed, 0.0, end);
        var pulsedDownstream = Between(pulsed, end, 200.0);
        var (hx, _) = held.Result.Density.Centroid();
        var (px, _) = pulsed.Result.Density.Centroid();

        // Where the analysis region's own rising edge balances the gas for this ion, which is
        // where a transferred population should come to rest: the same closed form as the
        // storage region's, on the analysis region's start, length and field.
        var parameters = ModelValidator
            .Validate(ModelJson.Parse(DeviceTemplates.Read("tims-tandem"))).Model!
            .Parameters.Parameters;
        var analysisBalance =
            (parameters["analysisStart"].Value.SiValue * 1e3)
            + (parameters["analysisRise"].Value.SiValue * 1e3
               * Math.Pow(150.0 / (0.042802 * parameters["analysisPeakField"].Value.SiValue), 2.0));

        output.WriteLine($"storage held:   {heldInStorage:P2} in the storage region, centre {hx * 1e3:F2} mm");
        output.WriteLine($"storage pulsed: {pulsedInStorage:P2} in the storage region, {pulsedDownstream:P2} downstream, "
            + $"centre {px * 1e3:F2} mm against the analysis region's own balance point at {analysisBalance:F2} mm");

        Assert.True(heldInStorage > 0.99, "the held control did not hold, so the pulse has nothing to be compared against");
        Assert.True(pulsedDownstream > 0.9, $"the pulse moved only {pulsedDownstream:P1} of the density downstream");

        // And it does not merely leave - it parks where the analysis region holds it, which is
        // what makes this a transfer between two traps rather than a loss out of one.
        Assert.Equal(analysisBalance, px * 1e3, 2.5);
    }
}
