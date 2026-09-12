using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A diffusive model may declare that the density's own charge enters its field, and it
/// reaches the solver from both diffusive paths.
/// </summary>
/// <remarks>
/// <para>
/// The wiring is what these test, not the physics - the self-potential is checked against
/// Gauss's law in <c>DensitySelfFieldTests</c>. What matters here is that a document can ask
/// for it, that asking is reported, that a method which cannot act is refused rather than
/// ignored, and above all that <em>both</em> diffusive call sites honour it: the wholly
/// diffusive path and a sequenced run's diffusive legs. A capability wired into one and not
/// the other is how this project has repeatedly produced a run that answers while leaving out
/// the physics it was asked for.
/// </para>
/// </remarks>
public sealed class MeanFieldSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-mean-field", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A drift tube carrying a declared population, coarse enough to run in a test.
    /// </summary>
    private string Model(string spaceCharge, int population, bool sequenced, string mode = "diffusion")
    {
        Directory.CreateDirectory(Path.Combine(_root, "models"));
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"tube-{spaceCharge}-{population}-{sequenced}-{mode}.json");

        var sequence = sequenced
            ? """
              ,
              "sequence": [
                { "name": "hold", "duration": { "value": 60, "unit": "us" } },
                { "name": "more", "duration": { "value": 60, "unit": "us" } }
              ]
              """
            : string.Empty;

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.12",
              "name": "mean-field-tube",
              "ion": { "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [4, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 0, "unit": "V" },
                "cloud": { "ions": 1, "population": {{population}},
                           "transverseSpread": { "value": 0.4, "unit": "mm" },
                           "longitudinalSpread": { "value": 0.4, "unit": "mm" } }
              },
              "detector": {
                "planePoint": { "value": [32, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "{{mode}}",
                "spaceCharge": "{{spaceCharge}}",
                "maximumFlightTime": { "value": 120, "unit": "us" },
                "mobility": { "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } },
                "densityGrid": {
                  "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                  "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                  "intervalsX": 64, "intervalsY": 32
                },
                "gas": { "model": "hardSphere", "pressure": { "value": 2, "unit": "mbar" },
                         "mass": { "value": 28.0134, "unit": "Da" },
                         "crossSection": { "value": 250, "unit": "angstrom^2" } }
              },
              "fields": [
                { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } }
              ]{{sequence}}
            }
            """);

        return path;
    }

    /// <summary>A mean-field run reports what it solved, on the record and in a warning.</summary>
    [Fact]
    public void AMeanFieldRunReportsTheChargeItModelled()
    {
        var (exit, stdout, stderr) = Run("run", Model("meanField", 3_000_000, sequenced: false), "--json");
        Assert.True(exit == 0, stdout + stderr);

        using var document = JsonDocument.Parse(stdout);
        var diffusion = document.RootElement.GetProperty("diffusion");

        var solves = diffusion.GetProperty("selfFieldSolves").GetInt32();
        var peak = diffusion.GetProperty("peakSelfPotentialVolts").GetDouble();

        var codes = Codes(document.RootElement);

        output.WriteLine($"{solves} self-field solve(s), peak {peak:G4} V");
        output.WriteLine("warnings: " + string.Join(", ", codes));

        Assert.True(solves >= 1, "the run declared meanField and never solved the self-potential");
        Assert.True(peak > 0.0, $"the self-potential peaked at {peak:G4} V, so no charge reached the field");
        Assert.Contains("spacecharge.mean-field", codes);
    }

    /// <summary>
    /// And a run that does not ask reports nothing rather than zero, so "did not model charge"
    /// is distinguishable from "modelled it and found none".
    /// </summary>
    [Fact]
    public void ARunThatDoesNotAskReportsNothing()
    {
        var (exit, stdout, stderr) = Run("run", Model("none", 3_000_000, sequenced: false), "--json");
        Assert.True(exit == 0, stdout + stderr);

        using var document = JsonDocument.Parse(stdout);
        var diffusion = document.RootElement.GetProperty("diffusion");

        Assert.False(diffusion.TryGetProperty("selfFieldSolves", out var solves) && solves.ValueKind != JsonValueKind.Null,
            "a run with no space charge reported a solve count, which reads as having modelled it");

        Assert.DoesNotContain("spacecharge.mean-field", Codes(document.RootElement));
    }

    /// <summary>
    /// The sequenced path honours it too, which is the assertion that matters: the same
    /// document run through the sequencer must model the same physics.
    /// </summary>
    [Fact]
    public void TheSequencedPathHonoursItAsWell()
    {
        var (exit, stdout, stderr) = Run("run", Model("meanField", 3_000_000, sequenced: true), "--json");
        Assert.True(exit == 0, stdout + stderr);

        using var document = JsonDocument.Parse(stdout);

        Assert.True(
            document.RootElement.TryGetProperty("sequence", out _),
            "the sequenced model did not take the sequenced path, so this asserts nothing");

        var codes = Codes(document.RootElement);

        output.WriteLine("warnings: " + string.Join(", ", codes));

        Assert.Contains("spacecharge.mean-field", codes);

        // And every diffusive phase says what it solved, per phase. A warning on the run says
        // the physics was modelled somewhere; the per-phase numbers are what say where, and a
        // sequence is exactly where that matters - a hold and the pulse after it carry the
        // same ions at very different densities.
        var phases = document.RootElement.GetProperty("sequence").GetProperty("phases");

        Assert.NotEmpty(phases.EnumerateArray());

        foreach (var phase in phases.EnumerateArray())
        {
            var name = phase.GetProperty("name").GetString();
            var solves = phase.GetProperty("selfFieldSolves");
            var peak = phase.GetProperty("peakSelfPotentialVolts");

            output.WriteLine($"  phase '{name}': {solves} solve(s), peak {peak} V");

            Assert.NotEqual(JsonValueKind.Null, solves.ValueKind);
            Assert.True(solves.GetInt32() >= 1, $"phase '{name}' solved the self-field {solves.GetInt32()} times");
            Assert.True(peak.GetDouble() > 0.0, $"phase '{name}' reported no self-potential");
        }
    }

    /// <summary>
    /// A method that cannot act is refused rather than ignored, both ways round.
    /// </summary>
    [Theory]
    [InlineData("meanField", "trajectory", "has none: every phase")]
    [InlineData("direct", "diffusion", "pushes trajectories")]
    public void AMethodThatCannotActIsRefused(string spaceCharge, string mode, string expected)
    {
        var (exit, _, stderr) = Run("validate", Model(spaceCharge, 1000, sequenced: false, mode));

        output.WriteLine(stderr.Trim());

        Assert.NotEqual(0, exit);
        Assert.Contains("/transport/spaceCharge", stderr, StringComparison.Ordinal);
        Assert.Contains(expected, stderr, StringComparison.Ordinal);
    }

    /// <summary>The warning codes on a run's result.</summary>
    /// <remarks>
    /// Under <c>flightTime</c>, which is where they are for a diffusive run: GRD-1 hangs a
    /// result's warnings on the quantity they qualify, and there is no root-level list. That
    /// the quantity in question is a flight time a density does not have is a wrinkle worth
    /// knowing rather than one to reshape - field names here are a surface agent workflows
    /// bind to. An absent list is how "none" is spelled, so it is read defensively.
    /// </remarks>
    private static List<string?> Codes(JsonElement root) =>
        root.TryGetProperty("flightTime", out var flightTime)
        && flightTime.TryGetProperty("warnings", out var warnings)
        && warnings.ValueKind == JsonValueKind.Array
            ? [.. warnings.EnumerateArray().Select(w => w.GetProperty("code").GetString())]
            : [];

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
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
