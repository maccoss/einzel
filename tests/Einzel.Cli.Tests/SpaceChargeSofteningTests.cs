using System.Text.Json;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A direct-sum space-charge run reports its softening against the packet's thinnest
/// dimension: a violation when the softening exceeds it, provenance when it does not.
/// </summary>
/// <remarks>
/// The case that found it: forty macroparticles along ten millimetres of a linear trap's
/// axis and fifty microns across it. The mean spacing, set by the RMS radius, was 1.7 mm -
/// thirty-four times the transverse size - so the force across the packet was softened to
/// almost nothing and a scan with 4,000 ions came back identical to one with none, with no
/// word said. A limit of the method is reported whether or not it bites.
/// </remarks>
public sealed class SpaceChargeSofteningTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void AThinLinePacketIsWarnedAndACompactOneIsNot()
    {
        var (init, _, _) = Run("init", _root);
        Assert.Equal(0, init);

        var thin = Warnings(Model(transverseMm: 0.05, longitudinalMm: 10.0), "thin");
        var compact = Warnings(Model(transverseMm: 0.5, longitudinalMm: 0.5), "compact");

        var thinSoftening = Assert.Single(thin, w => w.Code == "spacecharge.softening");
        var compactSoftening = Assert.Single(compact, w => w.Code == "spacecharge.softening");
        output.WriteLine($"thin:    [{thinSoftening.Severity}] {thinSoftening.Message}");
        output.WriteLine($"compact: [{compactSoftening.Severity}] {compactSoftening.Message}");

        Assert.Equal("ValidityViolation", thinSoftening.Severity);
        Assert.Contains("exceeds the packet's thin dimension", thinSoftening.Message, StringComparison.Ordinal);
        Assert.Equal("Provenance", compactSoftening.Severity);
    }

    private List<(string Code, string Severity, string Message)> Warnings(string modelJson, string tag)
    {
        var path = Path.Combine(_root, "models", $"{tag}.json");
        File.WriteAllText(path, modelJson);
        var (exit, stdout, stderr) = Run("run", path, "--json");
        Assert.True(exit == 0 || exit == 2 || exit == 4, $"{tag}: exit {exit}: {stderr}");
        using var result = JsonDocument.Parse(stdout);
        var list = new List<(string, string, string)>();
        // Warnings travel on the quantities (GRD-2), not at the top of the document: the
        // space-charge ones ride on the ensemble's transmission.
        foreach (var w in result.RootElement.GetProperty("ensemble").GetProperty("transmission").GetProperty("warnings").EnumerateArray())
        {
            list.Add((w.GetProperty("code").GetString()!, w.GetProperty("severity").GetString()!, w.GetProperty("message").GetString()!));
        }

        return list;
    }

    /// <summary>Twenty macroparticles for two thousand ions drifting in a weak uniform field, in vacuum.</summary>
    private static string Model(double transverseMm, double longitudinalMm) => $$"""
    {
      "schemaVersion": "0.9",
      "name": "softening",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [2, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 1, "unit": "V" },
        "cloud": { "ions": 20, "population": 2000, "temperature": { "value": 300, "unit": "K" },
                   "transverseSpread": { "value": {{transverseMm}}, "unit": "mm" },
                   "longitudinalSpread": { "value": {{longitudinalMm}}, "unit": "mm" }, "seed": 11 }
      },
      "fields": [ { "type": "uniform", "field": { "value": [100.0, 0, 0], "unit": "V/m" } } ],
      "detector": { "planePoint": { "value": [40, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 200, "unit": "us" }, "spaceCharge": "direct" }
    }
    """;

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
