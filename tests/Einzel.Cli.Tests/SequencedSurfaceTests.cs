using System.Text.Json;

using Einzel.Cli;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A mode-changing run, through the CLI (SEQ-1).
/// </summary>
/// <remarks>
/// The engine can cross a transport-mode boundary; this is about whether a model author
/// can reach that. A capability nothing can invoke is the "named in a csproj and nowhere
/// else" state this project keeps finding and criticising.
/// </remarks>
public sealed class SequencedSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-seq", Guid.NewGuid().ToString("N"));

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

    private const string TrapThenExtract = """
    {
      "schemaVersion": "0.6",
      "name": "trap-then-extract",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [10, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 5, "unit": "V" },
        "cloud": {
          "ions": 100,
          "seed": 7,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.5, "unit": "mm" },
          "longitudinalSpread": { "value": 0.5, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "trap",    "duration": { "value": 20, "unit": "us" }, "mode": "diffusion" },
        { "name": "extract", "duration": { "value": 5, "unit": "us" },  "mode": "trajectory" }
      ],
      "fields": [{ "type": "fieldFree" }],
      "detector": {
        "planePoint": { "value": [60, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 32
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    private string Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "seq.json");

        File.WriteAllText(path, TrapThenExtract);

        return path;
    }

    /// <summary>A model whose phases change mode runs, and reports each phase.</summary>
    [Fact]
    public void AModeChangingModelRunsAndReportsEachPhase()
    {
        var (exit, stdout, _) = Run("run", Project(), "--json");

        Assert.Equal(0, exit);

        var result = JsonDocument.Parse(stdout).RootElement;
        var sequence = result.GetProperty("sequence");

        output.WriteLine(sequence.ToString());

        Assert.Equal(1, sequence.GetProperty("conversions").GetInt32());

        var phases = sequence.GetProperty("phases").EnumerateArray().ToArray();

        Assert.Equal(2, phases.Length);
        Assert.Equal("diffusion", phases[0].GetProperty("mode").GetString());
        Assert.Equal("trajectory", phases[1].GetProperty("mode").GetString());

        // A diffusive phase has no trajectories at all, which is different from having
        // none left - the population is what carries across a boundary.
        Assert.Equal(0, phases[0].GetProperty("trajectories").GetInt32());
        Assert.True(phases[1].GetProperty("trajectories").GetInt32() > 0);
    }

    /// <summary>
    /// There is no flight time, and it is absent rather than not-a-number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sequenced run ends when its sequence ends, not when an ion arrives. This project
    /// already fixed exactly this for the diffusive mode — a density has no flight time,
    /// and printing NaN made a missing measurement indistinguishable from a failed one —
    /// but that fix was gated on <c>run.Diffusion is null</c>, so a third kind of run
    /// walked straight back into it.
    /// </para>
    /// <para>
    /// The JSON side is the finite-double policy: a non-finite number is written as null,
    /// because absent and zero are different answers and a reader cannot tell them apart
    /// if both print as a number.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThereIsNoFlightTimeAndItIsAbsentRatherThanNotANumber()
    {
        var model = Project();

        var json = JsonDocument.Parse(Run("run", model, "--json").Stdout).RootElement;
        var flight = json.GetProperty("flightTime");

        Assert.Equal(JsonValueKind.Null, flight.GetProperty("value").ValueKind);

        var human = Run("run", model).Stdout;

        output.WriteLine(human);

        Assert.DoesNotContain("NaN", human, StringComparison.Ordinal);
        Assert.DoesNotContain("flight time", human, StringComparison.Ordinal);

        // What it says instead: the packet's centre, labelled as a centre because there
        // is no single ion whose final position it could be.
        Assert.Contains("packet centre", human, StringComparison.Ordinal);
    }

    /// <summary>Every conversion warning reaches the caller (GRD-2).</summary>
    /// <remarks>
    /// The seam this project has dropped evidence at four times. A reader who takes a
    /// number from after a boundary without knowing the velocities were invented there
    /// has been misled by the platform, which is what GRD-3 exists to prevent — so these
    /// are violations and cannot be silenced.
    /// </remarks>
    [Fact]
    public void EveryConversionWarningReachesTheCaller()
    {
        var model = Project();

        var json = JsonDocument.Parse(Run("run", model, "--json").Stdout).RootElement;

        var codes = json.GetProperty("flightTime").GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        output.WriteLine(string.Join("\n", codes));

        Assert.Contains("transport.velocity-assumed", codes);
        Assert.Contains("transport.mode-changed", codes);
        Assert.Contains("transport.mode-changed-in-sequence", codes);

        // And in the terminal too, not only the machine-readable surface - on stderr,
        // because CLI-2 puts results on stdout and diagnostics on stderr. Asserting on
        // stdout here passed my own manual check only because I had merged the streams.
        Assert.Contains(
            "transport.velocity-assumed", Run("run", model).Stderr, StringComparison.Ordinal);
    }

    /// <summary>The manifest records every mode the run used (PRJ-3).</summary>
    /// <remarks>
    /// A manifest fully determines its run. Recording one mode for a run that used two
    /// would make it claim to determine a run it does not describe — and transport mode
    /// is one of the fields §14 names explicitly.
    /// </remarks>
    [Fact]
    public void TheManifestRecordsEveryModeTheRunUsed()
    {
        var json = JsonDocument.Parse(Run("run", Project(), "--json").Stdout).RootElement;

        var mode = json.GetProperty("manifest").GetProperty("transportMode").GetString();

        output.WriteLine(mode!);

        Assert.Contains("diffusion", mode!, StringComparison.Ordinal);
        Assert.Contains("trajectory", mode!, StringComparison.Ordinal);
    }
}
