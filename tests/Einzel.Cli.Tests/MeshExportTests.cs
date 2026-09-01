using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// The conductor surfaces can leave the program, and a section says when it missed them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two gaps found by trying to make a picture of a three-dimensional model.</b> The
/// surface extraction was headless and correct - its tests run on the Linux runner against
/// a sphere's area and volume, Pappus, and watertightness - and its only consumer was the
/// Windows viewport, so the artifact that lets an external renderer draw the geometry could
/// not be obtained without the shell. And a section whose plane cut no metal simply omitted
/// the count, leaving "the plane misses", "this device has no metal here" and "the
/// extraction failed" indistinguishable.
/// </para>
/// </remarks>
public sealed class MeshExportTests : IDisposable
{
    /// <summary>The model text, kept out of the method so the JSON reads as JSON.</summary>
    private const string ModelText = """
        {
          "schemaVersion": "0.7",
          "name": "stripes",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [2, 0, 5], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 1000, "unit": "V" }
          },
          "fields": [
            {
              "type": "solved3d",
              "solve3d": {
                "minX": { "value": 0, "unit": "mm" },
                "maxX": { "value": 40, "unit": "mm" },
                "minY": { "value": -8, "unit": "mm" },
                "maxY": { "value": 8, "unit": "mm" },
                "minZ": { "value": 0, "unit": "mm" },
                "maxZ": { "value": 40, "unit": "mm" },
                "cellSize": { "value": 2, "unit": "mm" },
                "electrodes": [
                  {
                    "name": "topStripe",
                    "shape": "box",
                    "minX": { "value": 8, "unit": "mm" },
                    "maxX": { "value": 16, "unit": "mm" },
                    "minY": { "value": 4, "unit": "mm" },
                    "maxY": { "value": 6, "unit": "mm" },
                    "minZ": { "value": 0, "unit": "mm" },
                    "maxZ": { "value": 40, "unit": "mm" },
                    "potential": { "value": 300, "unit": "V" }
                  },
                  {
                    "name": "bottomStripe",
                    "shape": "box",
                    "minX": { "value": 8, "unit": "mm" },
                    "maxX": { "value": 16, "unit": "mm" },
                    "minY": { "value": -6, "unit": "mm" },
                    "maxY": { "value": -4, "unit": "mm" },
                    "minZ": { "value": 0, "unit": "mm" },
                    "maxZ": { "value": 40, "unit": "mm" },
                    "potential": { "value": 300, "unit": "V" }
                  }
                ]
              }
            }
          ],
          "detector": {
            "planePoint": { "value": [38, 0, 5], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "maximumFlightTime": { "value": 20, "unit": "us" },
            "relativeTolerance": 1e-8
          }
        }
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-mesh", Guid.NewGuid().ToString("N"));

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
    /// A stripe on a board: thin one way and long another, the shape that lost its surface.
    /// </summary>
    private string Model()
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, "stripes.json");

        File.WriteAllText(path, ModelText);

        return path;
    }

    /// <summary>The conductors come out as a mesh, named, with their potentials.</summary>
    /// <remarks>
    /// <b>The names are the point of the format.</b> A loss itemisation says
    /// <c>bottomStripe</c>, and a mesh in which that electrode is one anonymous lump among
    /// sixteen is a picture nobody can be pointed at. The potential rides along as a comment
    /// because a grey mesh of identical plates is not much use for a figure and the number a
    /// reader wants to colour by is the one the model declared.
    /// </remarks>
    [Fact]
    public void TheConductorsExportAsANamedMesh()
    {
        var (exit, stdout, _) = Run("export", Model(), "--mesh", "--project", _root, "--json");

        Assert.Equal(0, exit);

        var outcome = JsonDocument.Parse(stdout).RootElement;

        Assert.Equal("conductors", outcome.GetProperty("what").GetString());
        Assert.Equal("obj", outcome.GetProperty("format").GetString());

        var file = outcome.GetProperty("artifacts")[0].GetString()!;
        var text = File.ReadAllText(file);

        var objects = text.Split('\n').Count(l => l.StartsWith("o ", StringComparison.Ordinal));
        var faces = text.Split('\n').Count(l => l.StartsWith("f ", StringComparison.Ordinal));

        Assert.Equal(2, objects);
        Assert.True(faces > 0, "the mesh has no faces in it");

        Assert.Contains("o topStripe", text, StringComparison.Ordinal);
        Assert.Contains("o bottomStripe", text, StringComparison.Ordinal);

        // GRD-12: the file states its unit, because OBJ carries none and a reader treating
        // one unit as one metre gets an instrument forty thousandths of a unit long.
        Assert.Contains("# units: millimetres", text, StringComparison.Ordinal);
        Assert.Contains("300 V", text, StringComparison.Ordinal);
    }

    /// <summary>A section that cuts no metal says so, and says how far away it was.</summary>
    /// <remarks>
    /// <para>
    /// The stripes sit at |y| between 4 and 6 mm and the ion flies at y = 0, so the plane
    /// containing the trajectory - the one a section defaults to for a beamline - holds no
    /// metal at all. That is not a defect in the device: it is what any stripe-on-boards
    /// analyser looks like, and the figure is correct and shows no instrument.
    /// </para>
    /// <para>
    /// <b>The distance is the actionable half.</b> "No conductor lies on this plane" sends a
    /// reader looking for a bug; "the nearest is 4 mm away" tells them to move the plane.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASectionThatMissesTheMetalSaysSo()
    {
        var (exit, stdout, _) = Run(
            "render", "section", Model(), "--out", Path.Combine(_root, "s.svg"),
            "--equipotentials", "4", "--project", _root, "--json");

        Assert.Equal(0, exit);

        var outcome = JsonDocument.Parse(stdout).RootElement;

        // Nothing was drawn, which is the state that used to be silent.
        Assert.False(outcome.GetProperty("paths").TryGetProperty("conductors", out _));

        var warnings = outcome.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToList();

        Assert.Contains("render.plane-misses-conductors", warnings);

        var message = outcome.GetProperty("warnings").EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "render.plane-misses-conductors")
            .GetProperty("message").GetString()!;

        Assert.Contains("2 declared conductor(s)", message, StringComparison.Ordinal);
        Assert.Contains("mm from this plane", message, StringComparison.Ordinal);
    }

    /// <summary>And a plane that does cut metal draws it, with no complaint.</summary>
    /// <remarks>
    /// The control, and it is what makes the test above a test. Without it, asserting a
    /// warning fires proves only that the warning exists - a renderer that raised it on
    /// every figure would pass.
    /// </remarks>
    [Fact]
    public void APlaneThroughTheMetalDrawsItAndDoesNotComplain()
    {
        _ = Model();

        var spec = Path.Combine(_root, "onmetal.json");

        File.WriteAllText(spec, """
            {
              "renderSpecVersion": "0.1",
              "kind": "section",
              "model": "stripes.json",
              "plane": { "normal": [0, 1, 0], "offsetMm": 5.0, "acrossMm": [1, 0, 0] },
              "equipotentials": 4,
              "trajectory": false
            }
            """);

        var (exit, stdout, _) = Run(
            "render", "section", spec, "--out", Path.Combine(_root, "m.svg"),
            "--project", _root, "--json");

        Assert.Equal(0, exit);

        var outcome = JsonDocument.Parse(stdout).RootElement;

        Assert.True(
            outcome.GetProperty("paths").TryGetProperty("conductors", out var drawn)
            && drawn.GetInt32() > 0,
            "a plane cutting the middle of a stripe drew no conductors");

        var warnings = outcome.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToList();

        Assert.DoesNotContain("render.plane-misses-conductors", warnings);
    }
}
