using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// Asking for a density in a driven geometry, and what a density does not have.
/// </summary>
/// <remarks>
/// Found by pointing the diffusive mode at the travelling-wave guide, which is a
/// thing someone would obviously try: a real travelling-wave guide runs in a gas,
/// and the diffusive mode is what this engine has for a gas. It ran, and produced
/// a number.
/// </remarks>
public sealed class DiffusiveDriveTests : IDisposable
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

    /// <summary>A drift tube with a gas and no drive, which the diffusive mode is for.</summary>
    private string Tube()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "tube.json");

        File.WriteAllText(path, """
        {
          "schemaVersion": "0.5",
          "name": "tube",
          "description": "A drift tube at a declared pressure, for the diffusive surface.",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [2, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 0.001, "unit": "V" },
            "cloud": { "ions": 1, "population": 1000,
                       "transverseWidth": { "value": 1.0, "unit": "mm" } }
          },
          "fields": [ { "type": "uniform", "field": { "value": [2000, 0, 0], "unit": "V/m" } } ],
          "detector": {
            "planePoint": { "value": [40, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "diffusion",
            "maximumFlightTime": { "value": 400, "unit": "us" },
            "densityGrid": {
              "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
              "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
              "intervalsX": 64, "intervalsY": 16
            },
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": 1.0, "unit": "mbar" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "crossSection": { "value": 250, "unit": "Å^2" }
            }
          }
        }
        """);

        return path;
    }

    [Fact]
    public void ADrivenGeometryIsRefusedRatherThanSteppedThroughASnapshot()
    {
        // The drift-diffusion solve takes one field and steps a density through it.
        // A driven field's time-free members sample t = 0, so what it would have
        // used is the RF at the top of its cycle: a static field that exists for no
        // length of time, and a density evolved through it means nothing.
        //
        // It used to run and report a transit distribution, with no warning
        // anywhere. That is the one place in this engine a transport mode was
        // selected outside its validity and nothing said so - which is the failure
        // the whole regime apparatus exists to prevent.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "guide.json");

        Assert.Equal(
            0, Run("new", model, "--from-template", "travelling-wave-guide").ExitCode);

        // Replacing the declared mode rather than prepending a second one: a
        // duplicate key is legal JSON and the last one wins, so a first version of
        // this test quietly ran the trajectory mode and asserted against its output.
        var text = File.ReadAllText(model);

        Assert.Contains("\"mode\": \"trajectory\"", text, StringComparison.Ordinal);

        File.WriteAllText(model, text.Replace(
            "\"mode\": \"trajectory\"",
            """
            "mode": "diffusion",
                "mobility": { "zeroField": { "value": 2.0, "unit": "cm^2/(V s)" } },
                "gas": {
                  "model": "langevin",
                  "pressure": { "value": 1.0, "unit": "mbar" },
                  "mass": { "value": 28.0134, "unit": "Da" },
                  "polarizability": { "value": 1.74, "unit": "Å^3" }
                }
            """.Trim(),
            StringComparison.Ordinal));

        var (exitCode, stdout, stderr) = Run("run", model, "--project", _root);

        var report = stdout + stderr;

        // CLI-3: a regime violation gets its own exit code whether it arrives as a
        // refusal or as a warning on a completed run. A caller branching on the
        // code should not have to know which route it took.
        Assert.Equal(2, exitCode);

        Assert.Contains("REGIME_INVALID", report, StringComparison.Ordinal);
        Assert.Contains("/transport/mode", report, StringComparison.Ordinal);

        // AGT-3: the message says what would have to exist for the request to be
        // meetable, not only that it is refused.
        Assert.Contains("effective potential", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ADensityRunPrintsWithoutAFlightTimeItDoesNotHave()
    {
        // A density has no flight time, no energy drift and no final position: it
        // is a field over a whole grid, not an ion with a history. Those lines were
        // printed anyway - the flight time as "NaN +/- NaN", and the final position
        // by indexing an empty list, which threw and reported an ordinary diffusive
        // run as a defect in einzel.
        var model = Tube();

        var (exitCode, stdout, stderr) = Run("run", model, "--project", _root);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("INTERNAL_ERROR", stdout + stderr, StringComparison.Ordinal);

        // Absent rather than not-a-number, which is the rule the rest of this
        // surface follows: a reader cannot tell a missing measurement from a failed
        // one if both print the same way.
        Assert.DoesNotContain("NaN", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("flight time", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("final x", stdout, StringComparison.Ordinal);

        // And it says what it has instead.
        Assert.Contains("transport.no-flight-time", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrajectoryRunStillPrintsAllThree()
    {
        // The control: the lines are omitted for a density, not deleted.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var (exitCode, stdout, _) = Run(
            "run", Path.Combine(_root, "models", "reflectron.json"), "--project", _root);

        Assert.Equal(0, exitCode);

        Assert.Contains("flight time", stdout, StringComparison.Ordinal);
        Assert.Contains("energy drift", stdout, StringComparison.Ordinal);
        Assert.Contains("final x", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonStillCarriesTheDiffusionBlock()
    {
        // The human rendering dropped three lines; the machine surface must not
        // have dropped anything, because CLI-1 makes --json the agent's view and a
        // field disappearing from it is a compatibility break.
        var model = Tube();

        var (exitCode, stdout, _) = Run("run", model, "--project", _root, "--json");

        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;

        Assert.True(root.TryGetProperty("diffusion", out var diffusion));
        Assert.True(diffusion.GetProperty("steps").GetInt64() > 0);
    }
}
