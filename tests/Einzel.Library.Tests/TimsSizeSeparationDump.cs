using System.Text.Json.Nodes;

using Einzel.Commands;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// Four ions of the same mass and different collision cross-section, held in the analyser
/// tunnel at once, and where each of them parks.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what an ion mobility analyser is for.</b> Mobility depends on how large an ion
/// is, not on how heavy it is - the mass enters only weakly, through the reduced mass of the
/// ion-neutral pair - so four conformers of one peptide, identical to a mass spectrometer,
/// separate here purely by their collision cross-section. Holding the mass fixed is what
/// makes the separation attributable to size and nothing else.
/// </para>
/// <para>
/// Cross-sections are turned into mobilities by the engine's own Mason-Schamp implementation
/// rather than by a formula written here, so the size-to-mobility step is the one the
/// transport uses and not a second copy of it.
/// </para>
/// <para>
/// A dump rather than a test of the physics: the parking relation itself is checked to a
/// micrometre in <c>TimsAnalyzerTests</c>. What this asserts is only what a reader of the
/// figure would be misled by - that the four are ordered by size, resolved from one another,
/// and all inside the usable length of the tunnel.
/// </para>
/// </remarks>
public sealed class TimsSizeSeparationDump(ITestOutputHelper output)
{
    /// <summary>Singly-charged, one mass, four sizes: conformers of one peptide.</summary>
    private const double MassToCharge = 922.0;

    private static readonly double[] CrossSectionsAngstromSq = [250.0, 300.0, 350.0, 400.0];

    /// <summary>The gas the template declares, with a cross-section substituted in.</summary>
    private static BackgroundGas GasWith(double angstromSq) => new()
    {
        Model = CollisionModel.HardSphere,
        PressureSi = 260.0,
        TemperatureK = 300.0,
        MassSi = 28.0134 * 1.66053906892e-27,
        CrossSectionSi = angstromSq * 1e-20,
    };

    /// <summary>Low-field mobility for a cross-section, from the engine's own Mason-Schamp.</summary>
    private static double MobilityFor(double angstromSq)
    {
        var gas = GasWith(angstromSq);
        var species = IonSpecies.FromMassToCharge(MassToCharge, 1);

        return Mobility.FromCrossSection(gas, species).ZeroFieldSi;
    }

    [Fact]
    public void FourSizesParkedAtOnce()
    {
        var mobilities = CrossSectionsAngstromSq.Select(MobilityFor).ToArray();

        output.WriteLine($"m/z {MassToCharge:F0}, singly charged, in N2 at 300 K");
        output.WriteLine("");
        output.WriteLine("ccs_A2,K_m2_per_Vs");

        for (var i = 0; i < mobilities.Length; i++)
        {
            output.WriteLine($"{CrossSectionsAngstromSq[i]:F0},{mobilities[i]:G6}");
        }

        var document = JsonNode.Parse(DeviceTemplates.Read("tims-analyzer"))!;

        // A mixture in place of the single ion. The species carry the sizes; everything else
        // about the tunnel is the shipped template.
        document["schemaVersion"] = "0.13";
        document.AsObject().Remove("ion");
        document["transport"]!.AsObject().Remove("mobility");
        document["source"]!["cloud"]!.AsObject().Remove("population");

        var species = new JsonArray();

        for (var i = 0; i < mobilities.Length; i++)
        {
            species.Add(JsonNode.Parse($$"""
                {
                  "name": "ccs{{CrossSectionsAngstromSq[i]:F0}}",
                  "massToCharge": { "value": {{MassToCharge}}, "unit": "Da" },
                  "chargeNumber": 1,
                  "mobility": { "zeroField": { "value": {{mobilities[i]:G17}}, "unit": "m^2/(V s)" } },
                  "population": 200000
                }
                """)!);
        }

        document["species"] = species;
        document["transport"]!["maximumFlightTime"] = JsonNode.Parse("""{ "value": 3000, "unit": "us" }""");
        document["transport"]!["densityGrid"]!["intervalsX"] = 256;
        document["transport"]!["densityGrid"]!["intervalsY"] = 16;

        // Released together at the tunnel entrance, as one packet of mixed sizes would be.
        document["parameters"]!["sourceX"]!["value"] = 6.0;
        document["parameters"]!["exitPotential"]!["value"] = 80.0;

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid
                ? string.Empty
                : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var built = FieldAssembly.BuildReported(validation.Model!);

        var outcome = DiffusionRun.ExecuteMixture(
            validation.Model!, built.Field, built.Warnings,
            scheme: StepScheme.Implicit, stepGain: 64.0);

        output.WriteLine("");
        output.WriteLine($"{outcome.Result.Steps} shared steps, set by '{outcome.Result.StepSetBy}'");
        output.WriteLine("");

        var centres = new List<double>();

        // One axial profile per species, collapsed across the bore: what a detector reading
        // position along the tunnel would see.
        foreach (var member in outcome.Result.Species)
        {
            var grid = member.Density.Grid;
            var profile = new double[grid.CountX];
            var total = 0.0;

            for (var j = 0; j < grid.CountY; j++)
            {
                var volume = member.Density.CellVolume(j);

                for (var i = 0; i < grid.CountX; i++)
                {
                    var ions = member.Density[i, j] * volume;
                    profile[i] += ions;
                    total += ions;
                }
            }

            var (cx, _) = member.Density.Centroid();
            centres.Add(cx * 1e3);

            var peak = profile.Max();

            output.WriteLine($"# {member.Name}: centre {cx * 1e3:F3} mm, {total:G6} ions held");
            output.WriteLine($"mm,{member.Name}");

            for (var i = 0; i < grid.CountX; i++)
            {
                output.WriteLine($"{grid.X(i) * 1e3:F3},{(peak > 0.0 ? profile[i] / peak : 0.0):F5}");
            }

            output.WriteLine("");
        }

        output.WriteLine("summary: ccs_A2,K,parked_mm");

        for (var i = 0; i < centres.Count; i++)
        {
            output.WriteLine($"{CrossSectionsAngstromSq[i]:F0},{mobilities[i]:G6},{centres[i]:F3}");
        }

        // Ordered by size, and every one of them inside the tunnel's usable length.
        for (var i = 1; i < centres.Count; i++)
        {
            Assert.True(
                centres[i] > centres[i - 1],
                $"CCS {CrossSectionsAngstromSq[i]:F0} parked at {centres[i]:F2} mm, not downstream of "
                + $"CCS {CrossSectionsAngstromSq[i - 1]:F0} at {centres[i - 1]:F2}");
        }

        Assert.True(centres[0] > 1.0, $"the smallest ion parked at {centres[0]:F2} mm, on the entrance");
        Assert.True(centres[^1] < 41.0, $"the largest parked at {centres[^1]:F2} mm, past the field peak");
    }
}
