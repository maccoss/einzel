using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// An imported neutral velocity field, driven through the command surface.
/// </summary>
/// <remarks>
/// GAS-1 asks a gas region to carry a bulk velocity <em>field</em>, and spec
/// figure 4 makes it a requirement above 10^-2 mbar rather than a benefit: at
/// funnel pressures "the neutral jet off the inlet capillary drags ions and
/// frequently dominates the axial DC gradient". A single declared vector cannot
/// say that, and §21 lists "gas velocity import" among Phase 3's deliverables.
/// </remarks>
public sealed class GasFlowSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-flow", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>
    /// Writes a velocity field as VTK ImageData, in the order VTK reads an extent.
    /// </summary>
    private void WriteField(string name, Func<int, double> axialSpeed, int countX = 5)
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        var extent = $"0 {countX - 1} 0 4 0 0";
        var spacing = 0.060 / (countX - 1);

        text.AppendLine("<?xml version=\"1.0\"?>");
        text.AppendLine("<VTKFile type=\"ImageData\" version=\"1.0\" byte_order=\"LittleEndian\">");
        text.AppendLine(invariant,
            $"  <ImageData WholeExtent=\"{extent}\" Origin=\"-0.010 -0.010 0\" Spacing=\"{spacing:G17} 0.005 1\">");
        text.AppendLine(invariant, $"    <Piece Extent=\"{extent}\">");
        text.AppendLine("      <PointData Vectors=\"velocity\">");
        text.AppendLine(
            "        <DataArray type=\"Float64\" Name=\"velocity\" NumberOfComponents=\"3\" format=\"ascii\">");

        for (var j = 0; j < 5; j++)
        {
            for (var i = 0; i < countX; i++)
            {
                text.Append(invariant, $"{axialSpeed(i):G17} 0 0 ");
            }

            text.AppendLine();
        }

        text.AppendLine("        </DataArray>");
        text.AppendLine("      </PointData>");
        text.AppendLine("      <CellData/>");
        text.AppendLine("    </Piece>");
        text.AppendLine("  </ImageData>");
        text.AppendLine("</VTKFile>");

        File.WriteAllText(Path.Combine(_root, "models", name), text.ToString());
    }

    /// <summary>A drift tube at a millibar, with whatever gas block is given.</summary>
    private string WriteModel(string name, string gasExtra)
    {
        var model = $$"""
        {
          "schemaVersion": "0.4",
          "name": "{{name}}",
          "description": "A drift tube at a millibar with a neutral flow along it.",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [2, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 0.001, "unit": "V" },
            "cloud": {
              "ions": 1, "population": 10000,
              "transverseSpread": { "value": 1.0, "unit": "mm" },
              "longitudinalSpread": { "value": 1.0, "unit": "mm" }
            }
          },
          "fields": [ { "type": "uniform", "field": { "value": [50, 0, 0], "unit": "V/m" } } ],
          "detector": {
            "planePoint": { "value": [40, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "diffusion",
            "maximumFlightTime": { "value": 1500, "unit": "us" },
            "densityGrid": {
              "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
              "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
              "intervalsX": 128, "intervalsY": 32
            },
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": 1, "unit": "mbar" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "crossSection": { "value": 250, "unit": "Å^2" }{{gasExtra}}
            }
          }
        }
        """;

        var path = Path.Combine(_root, "models", name + ".json");

        File.WriteAllText(path, model);

        return path;
    }

    private string Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        return _root;
    }

    /// <summary>The diffusion block of a run, as JSON.</summary>
    private static JsonElement Transit(string model)
    {
        var (exitCode, stdout, _) = Run("run", model, "--json");

        Assert.Equal(0, exitCode);

        return JsonDocument.Parse(stdout).RootElement.Clone().GetProperty("diffusion");
    }

    [Fact]
    public void AnImportedUniformFieldAgreesExactlyWithADeclaredOne()
    {
        // The check that makes the import trustworthy: two entirely separate paths
        // to the same gas - one a vector in the document, the other a file read,
        // interpolated and sampled per node - have to give the same answer.
        //
        // To a couple of units in the last place rather than bit-identically, and
        // the reason is worth knowing: trilinear interpolation of a constant returns
        // that constant only to rounding. Between two nodes both holding 30, the
        // weighted sum 30(1-f) + 30f is 29.999999999999996 for plenty of f. So the
        // imported field is 30 m/s to within an ulp and not exactly, everywhere
        // except on a node - which is inherent to sampling and is not something a
        // reader can fix. Anything larger than that is an indexing, ordering or unit
        // error in the reader.
        Project();

        WriteField("uniform.vti", _ => 30.0);

        var declared = Transit(WriteModel(
            "declared", ",\n      \"driftVelocity\": { \"value\": [30, 0, 0], \"unit\": \"m/s\" }"));

        var imported = Transit(WriteModel(
            "imported",
            ",\n      \"velocityField\": { \"path\": \"uniform.vti\", \"array\": \"velocity\" }"));

        var declaredTransit = declared.GetProperty("meanTransitUs").GetDouble();
        var importedTransit = imported.GetProperty("meanTransitUs").GetDouble();

        var declaredCollected = declared.GetProperty("collected").GetDouble();
        var importedCollected = imported.GetProperty("collected").GetDouble();

        Assert.Equal(declaredTransit, importedTransit, 1e-12 * Math.Abs(declaredTransit));
        Assert.Equal(declaredCollected, importedCollected, 1e-12 * Math.Abs(declaredCollected));

        // And it is reported, not merely used. A field that moved the ions without
        // appearing in the result would be the same silent drop the resolution
        // exists to prevent, one step further down the pipe.
        Assert.Equal(30.0, imported.GetProperty("gasSpeedSi").GetDouble(), 1e-12);
    }

    [Fact]
    public void AFieldThatVariesIsNotAnyUniformValue()
    {
        // What a single declared vector cannot express. A flow accelerating from
        // 10 to 50 m/s carries the density in a time strictly between what 10 and
        // 50 would give - not equal to either, and not equal to their mean either,
        // because transit is an integral of 1/v.
        Project();

        WriteField("ramp.vti", i => 10.0 + (10.0 * i));
        WriteField("slow.vti", _ => 10.0);
        WriteField("fast.vti", _ => 50.0);

        double Transit(string name)
        {
            var path = WriteModel(
                name,
                $",\n      \"velocityField\": {{ \"path\": \"{name}.vti\", \"array\": \"velocity\" }}");

            var (_, stdout, _) = Run("run", path, "--json");

            return JsonDocument.Parse(stdout).RootElement
                .GetProperty("diffusion").GetProperty("meanTransitUs").GetDouble();
        }

        var slow = Transit("slow");
        var ramp = Transit("ramp");
        var fast = Transit("fast");

        Assert.True(fast < ramp, $"the ramp ({ramp:F2} us) was not slower than 50 m/s ({fast:F2})");
        Assert.True(ramp < slow, $"the ramp ({ramp:F2} us) was not faster than 10 m/s ({slow:F2})");
    }

    [Fact]
    public void ATransportThatCannotResolveTheFieldRefusesRatherThanRunningStill()
    {
        // The failure mode GAS-1 is really guarding against, reached from the side a
        // user never sees: a study or a figure of merit meets the transport with no
        // model directory, so it cannot read the file. Running in a stationary gas
        // instead would silently answer about a different instrument - which is
        // exactly what driftVelocity did in the diffusive mode until recently.
        Project();

        WriteField("uniform.vti", _ => 30.0);

        var path = WriteModel(
            "unresolvable",
            ",\n      \"velocityField\": { \"path\": \"uniform.vti\", \"array\": \"velocity\" }");

        var validation = Core.Model.ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(path)), null);

        Assert.True(validation.IsValid);

        var (field, warnings) = Fields.FieldAssembly.BuildReported(validation.Model!);

        // resolved: null - the signature a caller without a path is forced into.
        var failure = Assert.Throws<Core.Errors.EinzelException>(
            () => Commands.DiffusionRun.Execute(validation.Model!, field, warnings));

        Assert.Equal("/transport/gas/velocityField", failure.Error.Path);
        Assert.Contains(
            "no model directory", failure.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("einzel run", failure.Error.Suggestion!, StringComparison.Ordinal);

        // The refusal no longer lives in DiffusionRun. It is BackgroundGas.FromModel's,
        // which is the function that cannot read a file - so every caller without a
        // path gets it rather than only the ones somebody remembered to guard. This
        // test used to assert a phrase from the local guard; what it asserts now is
        // that the behaviour survived being moved, which is the thing worth pinning.
        Assert.Equal(Core.Errors.ErrorCodes.SchemaInvalid, failure.Error.Code);
    }

    [Fact]
    public void AMissingFieldFileIsRefusedByPath()
    {
        // AGT-3: name the file that is not there, and say what the path is relative
        // to - because "relative to the model, not the working directory" is exactly
        // the thing a reader would otherwise get wrong.
        Project();

        var model = WriteModel(
            "absent",
            ",\n      \"velocityField\": { \"path\": \"nowhere.vti\", \"array\": \"velocity\" }");

        var (exitCode, stdout, stderr) = Run("run", model, "--json");

        Assert.NotEqual(0, exitCode);

        var text = stdout + stderr;

        Assert.Contains("nowhere.vti", text, StringComparison.Ordinal);
        Assert.Contains("velocityField", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AVelocityFieldWithNoPathIsRefusedAtValidation()
    {
        Project();

        var model = WriteModel("pathless", ",\n      \"velocityField\": { \"array\": \"velocity\" }");

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains(
            "/transport/gas/velocityField/path", stdout + stderr, StringComparison.Ordinal);
    }
}
