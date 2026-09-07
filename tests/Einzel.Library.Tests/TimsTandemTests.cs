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
    /// <summary>Where the storage region's rising edge ends and its plateau begins, in mm.</summary>
    private const double StorageRiseMm = 23.0;

    /// <summary>Where the storage region's plateau ends, in mm.</summary>
    private const double StorageEndMm = 41.0;

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

        return ions / outcome.Launched;
    }

    /// <summary>
    /// On a rising edge whose field goes as the square root of position, the parking point
    /// goes as the square of one over the mobility.
    /// </summary>
    [Fact]
    public void ParkingPositionGoesAsTheSquareOfOneOverMobility()
    {
        var measured = new Dictionary<double, double>();

        foreach (var ratio in new[] { 1.5, 1.0, 0.75 })
        {
            var (outcome, _) = Run(ratio, sourceMm: 10.0, flightUs: 1500.0);
            var (x, _) = outcome.Result.Density.Centroid();
            measured[ratio] = x * 1e3;

            // Where the declared profile balances the gas: E = 5000 sqrt(z / 23 mm) and the
            // ion is held where mu E = v_g, so z = 23 mm (v_g / (K * 5000))^2.
            var balance = StorageRiseMm * Math.Pow(150.0 / (0.042802 * ratio * 5000.0), 2.0);

            output.WriteLine($"K x {ratio:F2}: parked at {measured[ratio]:F2} mm, closed form {balance:F2} mm");

            Assert.Equal(balance, measured[ratio], 1.5);
            Assert.True(measured[ratio] < StorageRiseMm, "the ion parked past the rising edge, where the balance is not stable");
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

        var stayed = Between(scanned, 0.0, StorageEndMm);
        var leaked = Between(scanned, StorageEndMm, 200.0);
        var (x, _) = scanned.Result.Density.Centroid();

        output.WriteLine($"analysis field ramped 5000 -> 0 V/m: {stayed:P2} still in the storage region "
            + $"(centre {x * 1e3:F2} mm), {leaked:P3} past it, {scanned.Result.Collected / scanned.Launched:P3} collected");

        Assert.True(stayed > 0.95, $"only {stayed:P1} of the stored population stayed put while the analysis region was scanned");
        Assert.True(scanned.Result.Collected / scanned.Launched < 0.01, "the scan eluted the stored population, so the regions are not decoupled");
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

        var heldInStorage = Between(held, 0.0, StorageEndMm);
        var pulsedInStorage = Between(pulsed, 0.0, StorageEndMm);
        var pulsedDownstream = Between(pulsed, StorageEndMm, 200.0);
        var (hx, _) = held.Result.Density.Centroid();
        var (px, _) = pulsed.Result.Density.Centroid();

        output.WriteLine($"storage held:   {heldInStorage:P2} in the storage region, centre {hx * 1e3:F2} mm");
        output.WriteLine($"storage pulsed: {pulsedInStorage:P2} in the storage region, {pulsedDownstream:P2} downstream, centre {px * 1e3:F2} mm");

        Assert.True(heldInStorage > 0.95, "the held control did not hold, so the pulse has nothing to be compared against");
        Assert.True(pulsedDownstream > 0.5, $"the pulse moved only {pulsedDownstream:P1} of the population downstream");
        Assert.True(px > hx + 0.020, "the pulsed packet did not travel at least 20 mm further than the held one");
    }
}
