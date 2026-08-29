using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// An imported gas <em>pressure</em> field, driven through the command surface.
/// </summary>
/// <remarks>
/// <para>
/// The last quantity about a gas this engine held as a single number for a whole
/// model. GAS-1's velocity field landed and the ions moved with the jet, but the
/// density stayed uniform - so an imported flow gave the neutrals a velocity
/// everywhere and the same number of them everywhere, which is not a differentially
/// pumped instrument. A funnel behind an inlet capillary spans decades of pressure
/// between its entrance and its exit.
/// </para>
/// <para>
/// The unit is required on the file, and that is section 9's own rule rather than a
/// new one. <c>{"energy": 4000}</c> is a validation error because unit ambiguity is
/// the commonest source of silent wrongness; nothing about that weakens when the
/// number becomes a hundred thousand numbers, and vacuum work is quoted in mbar and
/// torr at least as often as in pascals. A file read as pascals when it holds mbar
/// is a gas a hundred times too thin, which looks entirely plausible.
/// </para>
/// </remarks>
public sealed class GasPressureSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-pressure", Guid.NewGuid().ToString("N"));

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

    /// <summary>Writes a scalar pressure field as VTK ImageData.</summary>
    /// <param name="name">File name, written into the project's models directory.</param>
    /// <param name="pressureAt">The pressure at node i along the tube, in the file's own unit.</param>
    private void WriteField(string name, Func<int, double> pressureAt)
    {
        const int countX = 5;

        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        var extent = $"0 {countX - 1} 0 4 0 0";
        var spacing = 0.060 / (countX - 1);

        text.AppendLine("<?xml version=\"1.0\"?>");
        text.AppendLine("<VTKFile type=\"ImageData\" version=\"1.0\" byte_order=\"LittleEndian\">");
        text.AppendLine(invariant,
            $"  <ImageData WholeExtent=\"{extent}\" Origin=\"-0.010 -0.010 0\" Spacing=\"{spacing:G17} 0.005 1\">");
        text.AppendLine(invariant, $"    <Piece Extent=\"{extent}\">");
        text.AppendLine("      <PointData Scalars=\"pressure\">");
        text.AppendLine(
            "        <DataArray type=\"Float64\" Name=\"pressure\" NumberOfComponents=\"1\" format=\"ascii\">");

        for (var j = 0; j < 5; j++)
        {
            for (var i = 0; i < countX; i++)
            {
                text.Append(invariant, $"{pressureAt(i):G17} ");
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

    /// <summary>A drift tube, with whatever pressure and gas extras are given.</summary>
    private string WriteModel(string name, double pressureMbar, string gasExtra)
    {
        var model = $$"""
        {
          "schemaVersion": "0.4",
          "name": "{{name}}",
          "description": "A drift tube whose gas may be graded along it.",
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
          "fields": [ { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } } ],
          "detector": {
            "planePoint": { "value": [40, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "diffusion",
            "maximumFlightTime": { "value": 4000, "unit": "us" },
            "densityGrid": {
              "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
              "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
              "intervalsX": 128, "intervalsY": 32
            },
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": {{pressureMbar.ToString("G17", CultureInfo.InvariantCulture)}}, "unit": "mbar" },
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
        var (exitCode, stdout, stderr) = Run("run", model, "--json");

        Assert.True(exitCode == 0, stdout + stderr);

        return JsonDocument.Parse(stdout).RootElement.Clone().GetProperty("diffusion");
    }

    /// <summary>The mean transit, having first checked the packet actually arrived.</summary>
    /// <remarks>
    /// The collected fraction is asserted here rather than at each call site because a
    /// transit read off a run that hit its flight-time ceiling is not a transit, it is
    /// the ceiling - and two ceilings agree with each other whatever the physics did.
    /// An earlier version of this file ran at a tenth of the field, collected 0.05 ions
    /// of 10,000, and compared two ceilings.
    /// </remarks>
    private static double TransitMicroseconds(JsonElement diffusion)
    {
        var collected = diffusion.GetProperty("collected").GetDouble();

        Assert.True(
            collected > 5_000.0,
            $"only {collected:F1} of 10,000 ions arrived, so the transit is the flight-time "
            + "ceiling rather than a transit");

        return diffusion.GetProperty("meanTransitUs").GetDouble();
    }

    /// <summary>
    /// A field at twice the declared pressure is the same gas as declaring twice the
    /// pressure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two entirely separate routes to one gas, which is what makes the import
    /// trustworthy. One model says 2 mbar and imports nothing; the other says 1 mbar
    /// and imports a field holding 2 mbar everywhere. Mobility goes as the reciprocal
    /// of density, so the first derives <c>mu</c> at 2 mbar directly and the second
    /// derives it at 1 mbar and halves it - and the two have to arrive at the same
    /// number, or the scaling is wrong in one direction or the other.
    /// </para>
    /// <para>
    /// This is the check that catches a reference density read from the wrong place,
    /// which is the mistake with no other symptom: a model that scales by
    /// <c>n_local/n_ref</c> instead of <c>n_ref/n_local</c> is self-consistent, runs
    /// cleanly, and is wrong by the square of the ratio.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFieldAtTwiceThePressureIsTheGasThatPressureDescribes()
    {
        Project();

        WriteField("double.vti", _ => 2.0);

        var declared = TransitMicroseconds(Transit(WriteModel("declared-2mbar", 2.0, "")));
        var imported = TransitMicroseconds(Transit(WriteModel(
            "one-mbar-plus-field",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"double.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mbar\" }")));

        Assert.Equal(declared, imported, 1e-6 * declared);
    }

    /// <summary>
    /// Halving the gas halves the transit, because mobility goes as the reciprocal of
    /// density.
    /// </summary>
    /// <remarks>
    /// The arithmetic this engine has no part in: steady drift is <c>mu E</c>, and
    /// <c>mu</c> goes as <c>1/n</c>, so a gas at half the density drifts at twice the
    /// speed in the same field and covers the same tube in half the time. Taken as a
    /// ratio between two runs so the mobility, the field and the length all cancel and
    /// what is left is the scaling alone.
    /// </remarks>
    [Fact]
    public void HalvingTheGasHalvesTheTransit()
    {
        Project();

        WriteField("half.vti", _ => 0.5);

        var uniform = TransitMicroseconds(Transit(WriteModel("at-one-mbar", 1.0, "")));
        var thinned = TransitMicroseconds(Transit(WriteModel(
            "half-a-millibar",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"half.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mbar\" }")));

        // Two per cent: the packet also diffuses, and the transit is a mean over an
        // arriving distribution rather than a single ion's flight.
        Assert.Equal(0.5, thinned / uniform, 0.02);
    }

    /// <summary>The same field written in pascals and in millibars is the same gas.</summary>
    /// <remarks>
    /// The unit is applied to the array and nowhere else, so a hundredfold change in
    /// the numbers with a hundredfold change in the unit has to cancel exactly. If it
    /// did not, the unit would be being read somewhere it should not be - or not read
    /// at all, which is the failure the requirement exists to prevent.
    /// </remarks>
    [Fact]
    public void TheUnitIsAppliedToTheFileAndNotToSomethingElse()
    {
        Project();

        WriteField("in-mbar.vti", _ => 1.5);
        WriteField("in-pascals.vti", _ => 150.0);

        var inMbar = TransitMicroseconds(Transit(WriteModel(
            "mbar-file",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"in-mbar.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mbar\" }")));

        var inPascals = TransitMicroseconds(Transit(WriteModel(
            "pascal-file",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"in-pascals.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"Pa\" }")));

        Assert.Equal(inMbar, inPascals, 1e-9 * inMbar);
    }

    /// <summary>
    /// A caller with no model directory is refused rather than run in a uniform gas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The twin of the velocity field's refusal, and the reason the guard was moved.
    /// It used to live at each of four call sites, every one of them naming
    /// <c>velocityField</c> — so a second importable quantity would have needed all
    /// four edited, and three were already silent about this one.
    /// </para>
    /// <para>
    /// It now lives on <c>BackgroundGas.FromModel</c>, the function that cannot read a
    /// file, with <c>WithoutImportedFields</c> as the deliberate exception. So this
    /// refusal was not written for the pressure field at all — it arrived by being the
    /// same code, which is the property being tested.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACallerWithNoModelDirectoryIsRefused()
    {
        Project();

        WriteField("unreachable.vti", _ => 1.0);

        var path = WriteModel(
            "unresolvable",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"unreachable.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mbar\" }");

        var validation = Core.Model.ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(path)), null);

        Assert.True(validation.IsValid);

        var (field, warnings) = Fields.FieldAssembly.BuildReported(validation.Model!);

        // resolved: null - the signature a caller without a path is forced into.
        var failure = Assert.Throws<Core.Errors.EinzelException>(
            () => Commands.DiffusionRun.Execute(validation.Model!, field, warnings));

        Assert.Equal("/transport/gas/pressureField", failure.Error.Path);
        Assert.Contains(
            "no model directory", failure.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("einzel run", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A collision drawn outside the imported field is said so, not absorbed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written because this wiring has broken four times.</b> A sampler computes
    /// something about its own validity and nothing reads it: <c>FieldAssembly.Build</c>
    /// discarding its <c>SolveReport</c>, the sweep evaluator dropping its warnings,
    /// <c>BoundExceeded</c> and <c>SampledOutsideFlow</c> consumed by nobody — and then
    /// <c>SampledOutsideDensity</c>, added with the pressure field and dropped in
    /// exactly the same place on the first draft. Adding a quantity to a sampler is not
    /// the same as reporting it, and the shortest spelling still loses it.
    /// </para>
    /// <para>
    /// Outside the imported extent the edge density is continued. That is a modelling
    /// choice rather than a measurement, and a pressure gradient is steepest at the ends
    /// of a pumped region — which is exactly where continuing the last plane is most
    /// likely to be wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACollisionOutsideTheImportedFieldIsReported()
    {
        Project();

        // The field spans x from -10 to 50 mm; the ion flies well past it. At the
        // declared pressure rather than above it, so the trajectory mode stays inside
        // its own validity - a first version put 1 mbar in the file against 0.001
        // declared, which is a thousandfold denser gas and a REGIME_INVALID exit. The
        // warning under test was present there too; the model was simply describing a
        // run this mode does not describe.
        WriteField("short.vti", _ => 0.001);

        var model = Path.Combine(_root, "models", "beyond-the-field.json");

        File.WriteAllText(model, $$"""
        {
          "schemaVersion": "0.4",
          "name": "beyond-the-field",
          "description": "A trajectory flight that leaves the imported pressure field.",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 20, "unit": "V" }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [400, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 2000, "unit": "us" },
            "gas": {
              "model": "langevin",
              "pressure": { "value": 0.001, "unit": "mbar" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "polarizability": { "value": 1.74, "unit": "Å^3" },
              "crossSection": { "value": 250, "unit": "Å^2" },
              "pressureField": {
                "path": "short.vti", "array": "pressure", "unit": "mbar"
              }
            }
          }
        }
        """);

        var (exitCode, stdout, stderr) = Run("run", model, "--json");

        Assert.True(exitCode == 0, stdout + stderr);

        var codes = JsonDocument.Parse(stdout).RootElement
            .GetProperty("flightTime").GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToList();

        Assert.Contains("gas.pressure-extrapolated", codes);
    }

    /// <summary>A field with no declared unit is refused, not guessed at.</summary>
    [Fact]
    public void AFieldWithoutAUnitIsRefused()
    {
        Project();

        WriteField("unitless.vti", _ => 1.0);

        var model = WriteModel(
            "no-unit",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"unitless.vti\", \"array\": \"pressure\" }");

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/transport/gas/pressureField/unit", stdout + stderr, StringComparison.Ordinal);
    }

    /// <summary>A unit of the wrong dimension is refused by name.</summary>
    [Fact]
    public void AUnitThatIsNotAPressureIsRefused()
    {
        Project();

        WriteField("wrong.vti", _ => 1.0);

        var model = WriteModel(
            "wrong-dimension",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"wrong.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mm\" }");

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not a pressure", stdout + stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The predicted step count is the step count, with the gas graded (GRD-8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// GRD-8 gates on a number available without doing the work, and for the diffusive
    /// mode that number is not modelled: the step is set by two stability limits
    /// computable from the mesh, the mobility and the field, and <c>estimate</c> and
    /// <c>run</c> call the same function. A graded gas moves the mobility, so it moves
    /// both limits, and the two have to keep agreeing.
    /// </para>
    /// <para>
    /// <b>The first version of the estimate over-predicted by 50%</b> — 2,252 steps
    /// against 1,502 — and the reason is worth keeping. It took the thinnest gas
    /// anywhere in the imported <em>field</em>, where the run takes its limit from
    /// per-node arrays over the <em>tracked grid</em>. The field here runs to 0.5 mbar
    /// and the grid only reaches 0.75, so the estimate was answering about a region no
    /// ion is tracked through. It was found by comparing the two numbers rather than
    /// by reading the code, which is the only way this class of disagreement shows.
    /// </para>
    /// <para>
    /// Both cases are asserted, because the graded one alone would pass on an engine
    /// that ignored the field in both places.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePredictedStepCountIsTheStepCountWithTheGasGraded()
    {
        Project();

        // Down the tube, spanning a factor of four, and deliberately wider than the
        // tracked grid so the two regions are not the same region.
        WriteField("gradient.vti", i => 2.0 - (0.375 * i));

        var uniform = WriteModel("uniform-steps", 1.0, "");
        var graded = WriteModel(
            "graded-steps",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"gradient.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mbar\" }");

        Assert.Equal(PredictedSteps(uniform), ActualSteps(uniform));
        Assert.Equal(PredictedSteps(graded), ActualSteps(graded));

        // And the grading actually moved it, or the equality above is a statement
        // about two runs that are the same run.
        Assert.True(
            ActualSteps(graded) > ActualSteps(uniform),
            "a gas thinner than declared over part of the grid must cost more steps, "
            + "because mobility goes as the reciprocal of density");
    }

    /// <summary>Steps the cost gate predicts, read out of its own basis line.</summary>
    private static int PredictedSteps(string model)
    {
        var (exitCode, stdout, stderr) = Run("estimate", model, "--json");

        Assert.True(exitCode == 0, stdout + stderr);

        var basis = JsonDocument.Parse(stdout).RootElement.GetProperty("basis").GetString()!;
        var match = System.Text.RegularExpressions.Regex.Match(basis, @"about ([\d,]+) steps");

        Assert.True(match.Success, $"no step count in the basis line: {basis}");

        return int.Parse(
            match.Groups[1].Value.Replace(",", "", StringComparison.Ordinal),
            CultureInfo.InvariantCulture);
    }

    /// <summary>Steps the run actually took.</summary>
    private static int ActualSteps(string model) =>
        Transit(model).GetProperty("steps").GetInt32();

    /// <summary>
    /// The range is reported on every run that imports one, per REG-2.
    /// </summary>
    /// <remarks>
    /// A reader who sees the range knows the run was checked; one who sees nothing
    /// cannot tell that from its not having been checked. The drift ratio is on the
    /// same line because it is the consequence a reader actually needs: a factor of
    /// four in density is a factor of four in how fast the ion moves.
    /// </remarks>
    [Fact]
    public void TheImportedRangeIsReportedWhetherOrNotItMatters()
    {
        Project();

        // A gradient down the tube, which is what a differentially pumped stack has.
        WriteField("graded.vti", i => 2.0 - (0.375 * i));

        var model = WriteModel(
            "graded",
            1.0,
            ",\n      \"pressureField\": { \"path\": \"graded.vti\", \"array\": \"pressure\", "
            + "\"unit\": \"mbar\" }");

        var (exitCode, stdout, stderr) = Run("run", model, "--json");

        Assert.True(exitCode == 0, stdout + stderr);

        var warnings = JsonDocument.Parse(stdout).RootElement
            .GetProperty("flightTime").GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToList();

        Assert.Contains("gas.pressure-imported", warnings);
    }
}
