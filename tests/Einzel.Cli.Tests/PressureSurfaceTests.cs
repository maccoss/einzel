using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// A model that declares a gas, driven through the command surface.
/// </summary>
/// <remarks>
/// REG-2 is the requirement under test: the engine computes the governing
/// dimensionless numbers and raises a non-suppressible warning when the selected
/// mode is outside validity. That is engine behaviour rather than documentation
/// because the defining risk of this whole project is an agent producing fifty
/// plausible numbers in an afternoon, three of them computed outside the validity
/// of the model used.
/// </remarks>
public sealed class PressureSurfaceTests : IDisposable
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

    /// <summary>A field-free flight tube with a declared gas, and nothing else.</summary>
    private static string Tube(string gas) => $$"""
    {
      "schemaVersion": "0.4",
      "name": "tube",
      "description": "A metre of field-free drift, for measuring what a gas does to a flight.",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 1000, "unit": "V" },
        "cloud": {
          "ions": 60,
          "seed": 11,
          "transverseWidth": { "value": 0.2, "unit": "mm" }
        }
      },
      "fields": [ { "type": "fieldFree" } ],
      "detector": {
        "planePoint": { "value": [1000, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "gas": {{gas}}
      }
    }
    """;

    private string Write(string gas)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "tube.json");
        File.WriteAllText(path, Tube(gas));

        return path;
    }

    [Fact]
    public void ADeclaredGasIsReportedWithItsRegimeNumbers()
    {
        // 1e-6 mbar of nitrogen: comfortably inside the hard-sphere regime, a long
        // mean free path, and a Knudsen number well above one. Nothing should warn,
        // and the numbers should still be reported - a reader who sees them knows
        // the run was checked, where a reader who sees nothing cannot tell that
        // from its not having been checked at all.
        var model = Write("""
        {
          "model": "hardSphere",
          "pressure": { "value": 1e-6, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" },
          "seed": 4242
        }
        """);

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var regime = document.RootElement.GetProperty("regime");

        Assert.Equal("hardSphere", regime.GetProperty("collisionModel").GetString());
        Assert.Equal(1e-6, regime.GetProperty("pressureMbar").GetDouble(), 1e-12);

        Assert.True(regime.GetProperty("knudsen").GetDouble() > 1.0);
        Assert.True(regime.GetProperty("meanFreePathMm").GetDouble() > 0.0);

        // Some ions collide over a metre at this pressure and some do not, which is
        // exactly what residual-gas scattering is.
        var ensemble = document.RootElement.GetProperty("ensemble");

        Assert.True(ensemble.GetProperty("collisions").GetInt32() >= 0);
        Assert.Equal(60, ensemble.GetProperty("launched").GetInt32());
    }

    [Fact]
    public void VacuumReportsNoRegimeBlockAtAll()
    {
        // Absent rather than a block of zeros. A pressure of zero and "no gas
        // declared" are different statements, and a reader cannot tell them apart
        // if both print as zero - the same rule the emittance of a packet with no
        // area follows.
        var model = Write("""{ "model": "none" }""");

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.False(document.RootElement.TryGetProperty("regime", out _));
    }

    [Fact]
    public void AboveTheDiffusiveBoundaryTheRunIsMarkedOutsideValidity()
    {
        // REG-2. At 1 mbar there are no trajectories to compute: the collision
        // frequency vastly exceeds anything else in the problem and the description
        // is a density field. The run still produces numbers - taint, never block -
        // and says plainly that they are outside the validity of every mode this
        // engine has.
        var model = Write("""
        {
          "model": "langevin",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "polarizability": { "value": 1.74, "unit": "Å^3" },
          "seed": 7
        }
        """);

        var (exitCode, stdout, stderr) = Run("run", model, "--json");

        // Taint, never block: the run completes and writes its result. But CLI-3
        // gives a regime violation its own exit code so a caller can tell it from a
        // flight that merely failed to converge - and at 1 mbar the ion thermalises
        // within a millimetre and never reaches the detector, which would otherwise
        // be reported as a convergence failure and hide the reason.
        Assert.Equal(2, exitCode);

        Assert.Contains("regime.trajectory-above-validity", stdout + stderr, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(stdout);

        var warnings = document.RootElement
            .GetProperty("flightTime")
            .GetProperty("warnings")
            .EnumerateArray()
            .ToArray();

        var outside = warnings.Single(
            w => w.GetProperty("code").GetString() == "regime.trajectory-above-validity");

        // GRD-3: only advisories may be silenced, and this is not advice.
        Assert.Equal("ValidityViolation", outside.GetProperty("severity").GetString());
        Assert.False(outside.GetProperty("suppressible").GetBoolean());
    }

    [Fact]
    public void AskingForStatisticalDiffusionSaysWhatIsMissing()
    {
        // REG-1 declares the two modes peers. Only one is built, and the refusal
        // names what the other one needs rather than listing alternatives that do
        // not do what was asked.
        var model = Path.Combine(_root, "models", "tube.json");

        Assert.Equal(0, Run("init", _root).ExitCode);

        File.WriteAllText(
            model,
            Tube("""{ "model": "none" }""").Replace(
                "\"mode\": \"trajectory\"", "\"mode\": \"statisticalDiffusion\"", StringComparison.Ordinal));

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);

        var text = stdout + stderr;

        // Both modes now exist, so an old name is a spelling error with a one-word
        // fix rather than a statement that the physics is unavailable.
        Assert.Contains("SCHEMA_INVALID", text, StringComparison.Ordinal);
        Assert.Contains("diffusion", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AGasWithNoCrossSectionIsRefusedRatherThanTreatedAsVacuum()
    {
        // The failure that would look most like success: a declared gas quietly
        // doing nothing. Every field with a defensible default has one; the ones
        // that decide the physics do not.
        var model = Write("""
        {
          "model": "hardSphere",
          "pressure": { "value": 1e-4, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" }
        }
        """);

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/transport/gas/crossSection", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ACollisionalRunIsReproducibleFromItsSeed()
    {
        // PRJ-3: a manifest fully determines its run. Two runs of the same file
        // must agree exactly; changing only the seed must not.
        var model = Write("""
        {
          "model": "hardSphere",
          "pressure": { "value": 1e-4, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" },
          "seed": 100
        }
        """);

        static int Collisions(string json) =>
            JsonDocument.Parse(json).RootElement.GetProperty("ensemble").GetProperty("collisions").GetInt32();

        var (_, first, _) = Run("run", model, "--json");
        var (_, again, _) = Run("run", model, "--json");

        Assert.Equal(Collisions(first), Collisions(again));
        Assert.True(Collisions(first) > 0, "no collisions happened, so this proves nothing");

        File.WriteAllText(model, File.ReadAllText(model).Replace(
            "\"seed\": 100", "\"seed\": 101", StringComparison.Ordinal));

        var (_, moved, _) = Run("run", model, "--json");

        Assert.NotEqual(Collisions(first), Collisions(moved));
    }
}
