using System.Text.Json;
using Einzel.Library;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A ramped phase and a fine staircase of held phases are the same scan: an ion flown
/// through the round-rod quadrupole with its RF ramped over forty microseconds ends where
/// a forty-step staircase puts it, and not where holding the starting amplitude does.
/// </summary>
/// <remarks>
/// This is the check that licenses both spellings: the linear-ion-trap studies were run as
/// staircases before the ramp existed, and the ramp is what a document should say. The
/// control - holding the start value - is what makes the agreement mean something, since
/// two runs that both did nothing would agree too.
/// </remarks>
public sealed class RampScanTests(ITestOutputHelper output) : IDisposable
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
    public void ARampAndAFineStaircaseFlyTheSameIon()
    {
        var (init, _, initErr) = Run("init", _root);
        Assert.Equal(0, init);

        // Round rods at 1 MHz, m/z 500, r0 = 4 mm: V(q) = q * 818.33 V. Ramp q from 0.50 to 0.80.
        const double V1 = 818.33;
        var vFrom = 0.50 * V1;
        var vTo = 0.80 * V1;
        const double RampUs = 40.0;

        var rampPhases = $$"""
            [ { "name": "hold", "duration": { "value": 10, "unit": "us" }, "set": { "rfAmplitude": { "value": {{vFrom:F4}}, "unit": "V" } } },
              { "name": "scan", "duration": { "value": {{RampUs}}, "unit": "us" }, "set": { "rfAmplitude": { "value": {{vFrom:F4}}, "unit": "V" } }, "ramp": { "rfAmplitude": { "value": {{vTo:F4}}, "unit": "V" } } } ]
            """;

        var steps = new List<string> { $$"""{ "name": "hold", "duration": { "value": 10, "unit": "us" }, "set": { "rfAmplitude": { "value": {{vFrom:F4}}, "unit": "V" } } }""" };
        const int Steps = 40;
        for (var k = 0; k < Steps; k++)
        {
            var v = vFrom + ((vTo - vFrom) * (k + 0.5) / Steps);
            steps.Add($$"""{ "name": "step{{k}}", "duration": { "value": {{RampUs / Steps}}, "unit": "us" }, "set": { "rfAmplitude": { "value": {{v:F4}}, "unit": "V" } } }""");
        }

        var staircasePhases = "[ " + string.Join(", ", steps) + " ]";
        var holdPhases = $$"""[ { "name": "hold", "duration": { "value": 50, "unit": "us" }, "set": { "rfAmplitude": { "value": {{vFrom:F4}}, "unit": "V" } } } ]""";

        var ramp = FinalPosition(Model(rampPhases), "ramp");
        var staircase = FinalPosition(Model(staircasePhases), "staircase");
        var hold = FinalPosition(Model(holdPhases), "hold");

        var rampVsStair = Distance(ramp, staircase);
        var rampVsHold = Distance(ramp, hold);
        var excursion = Math.Sqrt((ramp[0] * ramp[0]) + (ramp[1] * ramp[1]));

        output.WriteLine($"ramp ends at ({ramp[0]:F4}, {ramp[1]:F4}) mm, staircase at ({staircase[0]:F4}, {staircase[1]:F4}), hold at ({hold[0]:F4}, {hold[1]:F4})");
        output.WriteLine($"ramp vs staircase {rampVsStair * 1e3:F2} um; ramp vs hold {rampVsHold * 1e3:F2} um; excursion {excursion:F4} mm");

        // The staircase's amplitude error is at most half a step, 0.375 per cent of the span,
        // so the two flights part company by far less than the ramp parts from the control.
        Assert.True(rampVsHold > 0.02, "ramping the RF must move the ion measurably against holding it");
        Assert.True(rampVsStair < 0.2 * rampVsHold, $"ramp and staircase differ by {rampVsStair:F5} mm against {rampVsHold:F5} mm from the control");
    }

    private double[] FinalPosition(string modelJson, string tag)
    {
        var path = Path.Combine(_root, "models", $"{tag}.json");
        File.WriteAllText(path, modelJson);
        var (exit, stdout, stderr) = Run("run", path, "--json");
        Assert.True(exit == 0 || exit == 2 || exit == 4, $"{tag}: exit {exit}: {stderr}");
        using var result = JsonDocument.Parse(stdout);
        var final = result.RootElement.GetProperty("finalPositionMm");
        return [final[0].GetDouble(), final[1].GetDouble(), final[2].GetDouble()];
    }

    private static double Distance(double[] a, double[] b) =>
        Math.Sqrt(((a[0] - b[0]) * (a[0] - b[0])) + ((a[1] - b[1]) * (a[1] - b[1])));

    /// <summary>The shipped round-rod RF quadrupole, one ion at rest 0.3 mm off axis, held for the sequence's length.</summary>
    private static string Model(string sequence)
    {
        var template = DeviceTemplates.Read("quadrupole-rf");
        using var document = JsonDocument.Parse(template);
        var root = document.RootElement;

        // Rewrite the few parts that differ: the ion starts at rest, the flight lasts the
        // sequence, and the sequence is the one under test. Everything else is the template.
        var parameters = root.GetProperty("parameters").GetRawText();
        var fields = root.GetProperty("fields").GetRawText();
        return $$"""
        {
          "schemaVersion": "0.9",
          "name": "ramp-scan",
          "description": "The RF quadrupole with its amplitude ramped, for the ramp-against-staircase check.",
          "parameters": {{parameters}},
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0.3, 0.2, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 0, "unit": "V" }
          },
          "fields": {{fields}},
          "sequence": {{sequence}},
          "detector": { "planePoint": { "value": [0, 0, 200], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "relativeTolerance": 1e-9, "maximumFlightTime": { "value": 50, "unit": "us" } }
        }
        """;
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
