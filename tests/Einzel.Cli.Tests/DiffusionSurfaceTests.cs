using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// A model that declares diffusive transport, driven through the command surface.
/// </summary>
/// <remarks>
/// REG-1 makes the two transport modes peers. This is the wiring that makes that
/// true of a model <em>document</em> rather than only of the engine: a source
/// becomes an initial density, a detector a collecting boundary, and the result has
/// no flight time because a density does not have one.
/// </remarks>
public sealed class DiffusionSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

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
    /// A 40 mm drift tube at 1 mbar, driven hard enough that the ions arrive.
    /// </summary>
    /// <remarks>
    /// A millibar rather than a hundredth of one, because the diffusion coefficient
    /// goes as one over pressure: a thinner gas diffuses faster, needs a smaller
    /// explicit step, and takes longer to run. The cheap end of this mode is the
    /// dense end, which is the opposite of the intuition and the opposite of the
    /// event-driven mode.
    /// </remarks>
    private static string Tube(string mode, string transport) => $$"""
    {
      "schemaVersion": "0.4",
      "name": "drift-tube",
      "description": "A drift tube at a millibar, pushed by a uniform axial field.",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [2, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 0.001, "unit": "V" },
        "cloud": {
          "ions": 1, "population": 10000,
          "transverseWidth": { "value": 1.0, "unit": "mm" },
          "longitudinalWidth": { "value": 1.0, "unit": "mm" }
        }
      },
      "fields": [
        { "type": "uniform", "field": { "value": [2000, 0, 0], "unit": "V/m" } }
      ],
      "detector": {
        "planePoint": { "value": [40, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "{{mode}}",
        "maximumFlightTime": { "value": 400, "unit": "us" },
        {{transport}}
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    private const string Grid = """
        "densityGrid": {
          "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
          "intervalsX": 128, "intervalsY": 32
        },
        """;

    private string Write(string name, string mode, string transport)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");
        File.WriteAllText(path, Tube(mode, transport));

        return path;
    }

    [Fact]
    public void ADiffusiveRunReportsADensityRatherThanAFlightTime()
    {
        var model = Write("tube", "diffusion", Grid);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.Equal("DensityEvolved", root.GetProperty("outcome").GetString());
        Assert.Equal("diffusion", root.GetProperty("manifest").GetProperty("transportMode").GetString());

        // TRN-2: there is no flight time, and the absence is stated rather than
        // filled in with a plausible number.
        Assert.False(root.GetProperty("flightTime").TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Number);

        Assert.Contains(
            root.GetProperty("flightTime").GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "transport.no-flight-time");

        var diffusion = root.GetProperty("diffusion");

        // mu E over the 38 mm from source to detector. Mason-Schamp gives
        // 0.0924 m^2/(V s) at this density, so 2000 V/m is 184.7 m/s and 38 mm takes
        // 206 microseconds. That is a closed form, not a stored expectation.
        Assert.InRange(diffusion.GetProperty("meanTransitUs").GetDouble(), 195.0, 220.0);

        Assert.InRange(diffusion.GetProperty("transmission").GetDouble(), 0.95, 1.0);
        Assert.True(diffusion.GetProperty("steps").GetInt32() > 10);

        // ACC-5 survives the change of description: a loss is named by where it went.
        Assert.NotEqual(JsonValueKind.Undefined, diffusion.GetProperty("losses").ValueKind);
    }

    [Fact]
    public void AMobilityDerivedFromACrossSectionSaysSo()
    {
        // TRN-1 wants mobility declared. Deriving it is offered so the two modes can
        // describe the same gas, but a derived value carries the cross section's
        // uncertainty plus a first-order Chapman-Enskog approximation on top, and
        // the result says which it was.
        var model = Write("tube", "diffusion", Grid);

        var (_, stdout, _) = Run("run", model, "--json");

        using var document = JsonDocument.Parse(stdout);

        Assert.True(document.RootElement.GetProperty("diffusion").GetProperty("mobilityDerived").GetBoolean());

        Assert.Contains(
            document.RootElement.GetProperty("flightTime").GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "mobility.derived");
    }

    [Fact]
    public void ADiffusiveModelWithNoGasIsRefused()
    {
        // The diffusive mode describes ions moving through a gas. A model that
        // selects it and declares none is a model that has not said what it means,
        // and running it in vacuum would be the silent substitution the validator
        // refuses everywhere else.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "vacuum.json");

        File.WriteAllText(
            path,
            Tube("diffusion", Grid).Replace(
                "\"model\": \"hardSphere\"", "\"model\": \"none\"", StringComparison.Ordinal));

        var (exitCode, stdout, stderr) = Run("validate", path, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/transport/gas", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareRunsBothModesAndReportsTheDisagreement()
    {
        // REG-3: in the overlap band both modes run on the same model and the
        // comparison is a supported operation with its own report.
        var model = Write("tube", "trajectory", Grid);

        var (exitCode, stdout, _) = Run("compare", model, "--ions", "20", "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.True(root.GetProperty("trajectoryTransitUs").GetDouble() > 0.0);
        Assert.True(root.GetProperty("diffusionTransitUs").GetDouble() > 0.0);

        // The number that says whether they disagree or merely differ. A relative
        // difference with no error beside it cannot tell a real disagreement from an
        // under-sampled ensemble.
        Assert.True(root.GetProperty("standardErrors").GetDouble() >= 0.0);

        // 1 mbar is above the overlap band, and the report says so rather than
        // quietly comparing two numbers one of which is outside its own validity.
        Assert.False(root.GetProperty("inOverlapBand").GetBoolean());

        Assert.Contains(
            root.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("code").GetString() == "regime.comparison-outside-band");
    }

    [Fact]
    public void ARendererDrawsNoTrajectoriesForADiffusiveModel()
    {
        // RND-8 and TRN-2: a diffusive region emits a density field, and lines
        // through it would depict something the model never produced. Asked of the
        // transport mode rather than inferred from the pressure.
        var model = Write("tube", "diffusion", Grid);

        var (exitCode, stdout, stderr) = Run("render", "section", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.Equal(0, document.RootElement.GetProperty("trajectoryPoints").GetInt32());

        Assert.False(
            document.RootElement.GetProperty("paths").TryGetProperty("trajectory", out _),
            "a trajectory was drawn for a model that computes no trajectories");

        Assert.Contains("render.no-trajectories", stdout + stderr, StringComparison.Ordinal);
    }
}
