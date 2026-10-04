using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// A node on a face two disagreeing conductors share is reported by <c>einzel solve</c>
/// and <c>einzel run</c>, in both output forms.
/// </summary>
/// <remarks>
/// <c>solve</c> carried no warnings at all before this: it reported residuals, cycles and
/// node counts, so a mesh that put nodes on a shared face came back converged and clean
/// from the one verb whose job is to say how the discretization went. <c>run</c> reaches
/// the finding through <c>FieldAssembly.BuildReported</c>, which is the seam every figure
/// passes through too.
/// </remarks>
public sealed class SharedFaceSurfaceTests : IDisposable
{
    private const string Code = "mesh.node-on-shared-face";

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
    /// Two boxes stacked along z, meeting at <paramref name="face"/> millimeters, in a
    /// 16 mm grounded box at a 1 mm cell - so a face at 2 mm is on a plane of nodes.
    /// </summary>
    private static string Stacked(string face) => $$"""
    {
      "schemaVersion": "0.3",
      "name": "stacked",
      "description": "Two boxes at opposite potentials sharing a face, and an ion flying past them.",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [6.5, 0, -7], "unit": "mm" },
        "direction": { "value": [0, 0, 1] },
        "accelerationPotential": { "value": 1000, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved3d",
          "solve3d": {
            "minX": { "value": -8, "unit": "mm" },
            "minY": { "value": -8, "unit": "mm" },
            "minZ": { "value": -8, "unit": "mm" },
            "maxX": { "value": 8, "unit": "mm" },
            "maxY": { "value": 8, "unit": "mm" },
            "maxZ": { "value": 8, "unit": "mm" },
            "cellSize": { "value": 1, "unit": "mm" },
            "tolerance": 1e-8,
            "electrodes": [
              {
                "name": "lower", "shape": "box",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": -4, "unit": "mm" },
                "minZ": { "value": -4, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "maxZ": { "value": {{face}}, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              },
              {
                "name": "upper", "shape": "box",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": -4, "unit": "mm" },
                "minZ": { "value": {{face}}, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "maxZ": { "value": 4, "unit": "mm" },
                "potential": { "value": -100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 0, 7], "unit": "mm" },
        "normal": { "value": [0, 0, -1] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
    }
    """;

    private string Write(string face)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Path.Combine(_root, "models", "stacked.json");
        File.WriteAllText(path, Stacked(face));
        return path;
    }

    [Fact]
    public void SolveReportsItOncePerElement()
    {
        var model = Write("2");

        var (exitCode, stdout, _) = Run("solve", model, "--json");

        // Qualified, so the solve still succeeds: a caveat on the field, not a failure of it.
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var warnings = document.RootElement.GetProperty("warnings");

        var warning = Assert.Single(warnings.EnumerateArray());
        Assert.Equal(Code, warning.GetProperty("code").GetString());
        Assert.StartsWith(
            "field element 0 (solved3d): 81 mesh nodes",
            warning.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SolvePrintsItOnStandardError()
    {
        // CLI-2: a warning is a diagnostic, and anything above advisory goes to stderr so a
        // pipe that keeps only stdout does not lose it.
        var model = Write("2");

        var (_, stdout, stderr) = Run("solve", model);

        Assert.Contains($"[Qualified] {Code}", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void RunCarriesItOntoTheResult()
    {
        var model = Write("2");

        var (_, stdout, _) = Run("run", model, "--json");

        Assert.Contains(Code, stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void AMeshAQuarterCellOffTheFaceIsQuietEverywhere()
    {
        var model = Write("2.25");

        var (_, solveOut, solveErr) = Run("solve", model, "--json");
        var (_, runOut, runErr) = Run("run", model, "--json");

        Assert.DoesNotContain(Code, solveOut + solveErr + runOut + runErr, StringComparison.Ordinal);
    }
}
