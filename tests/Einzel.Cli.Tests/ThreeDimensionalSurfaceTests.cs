using System.Globalization;
using System.Text.Json;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// The command surface against a three-dimensional model.
/// </summary>
/// <remarks>
/// <para>
/// These exist because <c>solve</c> and <c>export</c> read only the two-dimensional
/// element for a whole revision and skipped past every <c>solved3d</c> one. That was
/// not a missing feature - it was worse, because <c>solve</c> then reported
/// <c>converged: true</c> and exit code 0 for a model it had looked at and not
/// touched, which is exactly the shape of answer an agent stops investigating on.
/// </para>
/// <para>
/// A small geometry on purpose: a ring in a grounded box, seventeen nodes an axis.
/// What is under test is whether the verb sees the element at all, and that does not
/// need a fine mesh.
/// </para>
/// </remarks>
public sealed class ThreeDimensionalSurfaceTests : IDisposable
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

    private const string Ring = """
    {
      "schemaVersion": "0.3",
      "name": "ring",
      "description": "A ring electrode in a grounded box, small enough to solve in a test.",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, 0, -8], "unit": "mm" },
        "direction": { "value": [0, 0, 1] },
        "accelerationPotential": { "value": 10, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved3d",
          "solve3d": {
            "minX": { "value": -10, "unit": "mm" },
            "minY": { "value": -10, "unit": "mm" },
            "minZ": { "value": -10, "unit": "mm" },
            "maxX": { "value": 10, "unit": "mm" },
            "maxY": { "value": 10, "unit": "mm" },
            "maxZ": { "value": 10, "unit": "mm" },
            "cellSize": { "value": 1.5, "unit": "mm" },
            "tolerance": 1e-8,
            "electrodes": [
              {
                "name": "ring", "shape": "cylinder", "axis": "z",
                "centreX": { "value": 0, "unit": "mm" },
                "centreY": { "value": 0, "unit": "mm" },
                "radius": { "value": 3.4, "unit": "mm" },
                "lower": { "value": -2, "unit": "mm" },
                "upper": { "value": 2, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 0, 9], "unit": "mm" },
        "normal": { "value": [0, 0, -1] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
    }
    """;

    private string WriteRing()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Path.Combine(_root, "models", "ring.json");
        File.WriteAllText(path, Ring);
        return path;
    }

    [Fact]
    public void SolveReportsAThreeDimensionalElement()
    {
        var model = WriteRing();

        var (exitCode, stdout, _) = Run("solve", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var elements = document.RootElement.GetProperty("elements");

        Assert.Equal(1, elements.GetArrayLength());

        var element = elements[0];

        Assert.Equal(3, element.GetProperty("dimensions").GetInt32());
        Assert.Equal(3, element.GetProperty("nodes").GetArrayLength());
        Assert.Equal(3, element.GetProperty("spacingMm").GetArrayLength());
        Assert.True(element.GetProperty("converged").GetBoolean());
        Assert.True(element.GetProperty("cutLinks").GetInt32() > 0, "a round ring should cut the stencil");

        // The maximum principle: no potential in a Laplace solution may exceed the
        // largest applied value. The cheapest exact proof that a solve has not
        // diverged, and the check that caught coarsening reaching 137 V of 100.
        Assert.InRange(element.GetProperty("peakPotentialVolts").GetDouble(), 99.999, 100.001);

        Assert.True(document.RootElement.GetProperty("converged").GetBoolean());
    }

    [Fact]
    public void SolvePrintsEveryAxisOfAVolume()
    {
        // The terminal printer indexed Nodes[0] and Nodes[1] and SpacingMm[0] and
        // SpacingMm[1], so a volume came out as a plane: this ring printed as 17x17
        // at 1.2500 x 1.2500 mm while --json said three axes. The JSON test above
        // cannot see that, because it never reads the human output.
        var model = WriteRing();

        var (exitCode, stdout, _) = Run("solve", model);
        Assert.Equal(0, exitCode);

        // 20 mm at a requested 1.5 mm rounds up to 16 intervals an axis, so 17 nodes
        // at 1.25 mm - the same 17 nodes an axis ExportWritesAVolume asserts from the
        // .vti extent. Equal spacings on a volume are cubic cells, not square ones.
        Assert.Contains(
            "17x17x17 at 1.2500 x 1.2500 x 1.2500 mm (cubic)", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void SolvePrintsTheAxesInTheOrderJsonGivesThem()
    {
        // The ring is a cube, so the test above passes against a printer that
        // reverses the axes or pairs a spacing with the wrong count. Stretched here
        // so that no two axes agree on either, and the expected line is built from
        // --json rather than worked out by hand, so what is checked is that the two
        // surfaces describe the same grid.
        var model = WriteRing();
        var text = File.ReadAllText(model);

        text = Replace(text, "\"minX\": { \"value\": -10,", "\"minX\": { \"value\": -6,");
        text = Replace(text, "\"maxX\": { \"value\": 10,", "\"maxX\": { \"value\": 6,");
        text = Replace(text, "\"minY\": { \"value\": -10,", "\"minY\": { \"value\": -15,");
        text = Replace(text, "\"maxY\": { \"value\": 10,", "\"maxY\": { \"value\": 15,");
        File.WriteAllText(model, text);

        var (jsonExit, json, _) = Run("solve", model, "--json");
        Assert.Equal(0, jsonExit);

        using var document = JsonDocument.Parse(json);
        var element = document.RootElement.GetProperty("elements")[0];

        var nodes = element.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetInt32().ToString(CultureInfo.InvariantCulture))
            .ToList();
        var spacing = element.GetProperty("spacingMm").EnumerateArray()
            .Select(s => s.GetDouble().ToString("F4", CultureInfo.InvariantCulture))
            .ToList();

        // The guard that gives the comparison teeth: if any two axes agreed, a
        // printer swapping them would still match.
        Assert.Equal(3, nodes.Distinct().Count());
        Assert.Equal(3, spacing.Distinct().Count());

        var (exitCode, stdout, _) = Run("solve", model);
        Assert.Equal(0, exitCode);

        Assert.Contains(
            $"{string.Join("x", nodes)} at {string.Join(" x ", spacing)} mm",
            stdout,
            StringComparison.Ordinal);
    }

    private static string Replace(string text, string oldValue, string newValue)
    {
        // An edit that matches nothing leaves the model unchanged and the test
        // quietly testing the cube again.
        Assert.Contains(oldValue, text, StringComparison.Ordinal);
        return text.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    [Fact]
    public void SolveWillNotCallAModelWithNothingToSolveConverged()
    {
        // The reflectron `init` scaffolds is analytic. Before this, `solve` returned
        // an empty element list, and "every element converged" over an empty list is
        // true - so the verb answered converged, exit 0, having done nothing.
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "reflectron.json");

        var (exitCode, stdout, stderr) = Run("solve", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("no field to solve", stdout + stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportWritesAVolume()
    {
        var model = WriteRing();

        var (exitCode, stdout, _) = Run("export", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var artifacts = document.RootElement.GetProperty("artifacts");

        Assert.Equal(1, artifacts.GetArrayLength());

        var path = artifacts[0].GetString()!;
        Assert.True(File.Exists(path), path);

        var vti = File.ReadAllText(path);

        // A volume, not a plane: the third extent has to span something, and the
        // origin and spacing all three have to be real rather than 0 and 1.
        Assert.Contains("WholeExtent=\"0 16 0 16 0 16\"", vti, StringComparison.Ordinal);
        Assert.DoesNotContain("Spacing=\"1.25 1.25 1\"", vti, StringComparison.Ordinal);

        // Every node written, x fastest.
        var values = vti[(vti.IndexOf("format=\"ascii\">", StringComparison.Ordinal) + 15)..];
        values = values[..values.IndexOf("</DataArray>", StringComparison.Ordinal)];

        Assert.Equal(
            17 * 17 * 17,
            values.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
