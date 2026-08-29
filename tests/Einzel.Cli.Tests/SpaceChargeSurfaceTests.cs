using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// Asking for space charge from a model document.
/// </summary>
/// <remarks>
/// The physics is checked in Einzel.Transport.Tests, against closed forms and
/// conservation laws. What is checked here is the contract: that a model can ask
/// for it, that a run which models it stops claiming it does not, that the cost is
/// stated before it is spent (GRD-8), and that the three ways to ask for it and
/// not get it are refused rather than quietly run.
/// </remarks>
public sealed class SpaceChargeSurfaceTests : IDisposable
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

    /// <summary>Half a metre of field-free drift with a dense packet.</summary>
    /// <remarks>
    /// Long enough for the effect to be visible, which is a real constraint rather
    /// than a convenience. The packet expands under its own charge for as long as
    /// the flight lasts, so over a 20 mm drift a 20,000-ion packet moved the peak by
    /// 0.2 per cent - a switch that runs different code and produces the same number
    /// is not a feature, and a test over that drift would have passed on a build
    /// where the interaction was never wired up.
    /// </remarks>
    private string Tube(string cloud, string spaceCharge, string gas = "")
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "tube.json");

        File.WriteAllText(path, $$"""
        {
          "schemaVersion": "0.5",
          "name": "tube",
          "description": "Half a metre of drift with a dense packet, for the space-charge surface.",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 4, "unit": "kV" },
            "cloud": {{cloud}}
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [500, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 100, "unit": "us" },
            "spaceCharge": "{{spaceCharge}}"{{gas}}
          }
        }
        """);

        return path;
    }

    private const string Packet = """
        {
          "ions": 40, "population": 20000, "seed": 3,
          "transverseSpread": { "value": 0.3, "unit": "mm" },
          "longitudinalSpread": { "value": 0.3, "unit": "mm" }
        }
        """;

    [Fact]
    public void AModelCanAskForItAndTheRunStopsSayingItIsNotModelled()
    {
        var model = Tube(Packet, "direct");

        var (exitCode, stdout, _) = Run("run", model, "--json");
        Assert.Equal(0, exitCode);

        var warnings = JsonDocument.Parse(stdout).RootElement
            .GetProperty("ensemble")
            .GetProperty("transmission")
            .GetProperty("warnings")
            .EnumerateArray()
            .ToArray();

        // The screening warning exists to say "this matters and the engine is not
        // doing it". Here the engine is doing it, so repeating it would be false.
        Assert.DoesNotContain(
            warnings, w => w.GetProperty("code").GetString() == "spacecharge.ignored");

        var modelled = warnings.Single(
            w => w.GetProperty("code").GetString() == "spacecharge.modelled");

        // Provenance rather than a violation: nothing is wrong, and a reader still
        // has to be told that the trajectories are macroparticles.
        Assert.Equal("Provenance", modelled.GetProperty("severity").GetString());

        var message = modelled.GetProperty("message").GetString()!;

        Assert.Contains("500.0 real ions", message, StringComparison.Ordinal);
        Assert.Contains("softened", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItActuallyChangesTheAnswer()
    {
        // A switch that runs different code and produces the same number is not a
        // feature. Free flight, so the direction is unambiguous: the leading ions
        // are pushed further ahead and the trailing ones further behind, and the
        // arrival spread can only grow.
        var with = Tube(Packet, "direct");
        var (_, pushed, _) = Run("run", with, "--json");

        var without = Tube(Packet, "none");
        var (_, free, _) = Run("run", without, "--json");

        static double Fwhm(string json) =>
            JsonDocument.Parse(json).RootElement
                .GetProperty("ensemble").GetProperty("gaussianFwhmNs").GetDouble();

        Assert.True(
            Fwhm(pushed) > 1.2 * Fwhm(free),
            $"space charge moved the peak from {Fwhm(free):F3} ns to {Fwhm(pushed):F3} ns, which is "
            + "too little to be the mutual force doing anything");
    }

    [Fact]
    public void TheCostIsStatedBeforeItIsSpent()
    {
        // GRD-8. Direct space charge is the first cost here that is quadratic in a
        // number a user types, so the linear intuition is exactly wrong and the
        // estimate says so in words rather than only in a number.
        var model = Tube(Packet, "direct");

        var (exitCode, stdout, _) = Run("estimate", model, "--json");
        Assert.Equal(0, exitCode);

        var basis = JsonDocument.Parse(stdout).RootElement.GetProperty("basis").GetString()!;

        Assert.Contains("QUADRATIC", basis, StringComparison.Ordinal);
        Assert.Contains("780 pairs", basis, StringComparison.Ordinal);

        // And raising the trajectory count by ten raises the estimate by about a
        // hundred, which is the claim the words make.
        static double Seconds(string json) =>
            JsonDocument.Parse(json).RootElement.GetProperty("seconds").GetDouble();

        var ten = Tube(Packet.Replace("\"ions\": 40", "\"ions\": 400", StringComparison.Ordinal), "direct");

        var (_, larger, _) = Run("estimate", ten, "--json");

        Assert.InRange(Seconds(larger) / Seconds(stdout), 50.0, 200.0);
    }

    [Fact]
    public void APacketAtAPointIsRefusedRatherThanRunWithAnUnboundedField()
    {
        // More than one ion at a single point is not a large self-field, it is an
        // infinite one, and it is easy to write.
        var model = Tube("""{ "ions": 40, "population": 20000, "seed": 3 }""", "direct");

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/source/cloud", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleTrajectoryHasNobodyToPushOn()
    {
        var model = Tube(
            """{ "ions": 1, "population": 20000, "transverseSpread": { "value": 0.3, "unit": "mm" } }""",
            "direct");

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("/transport/spaceCharge", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AGasIsRefusedBecauseItWouldTakeNoPartInTheRun()
    {
        // The failure that would look most like success: a declared gas quietly
        // doing nothing, because the packet integrator advances everything in
        // lockstep and has no collision hook.
        var model = Tube(Packet, "direct", gas: """
        ,
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": 1e-4, "unit": "mbar" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "crossSection": { "value": 250, "unit": "Å^2" }
            }
        """);

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);

        var text = stdout + stderr;

        Assert.Contains("REGIME_INVALID", text, StringComparison.Ordinal);
        Assert.Contains("collision hook", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownMethodNamesTheOnesThisBuildHas()
    {
        // AGT-3: an error is a recovery instruction. "particleInCell" is the value
        // someone will try, and the method IS built - under a different spelling. An
        // error that only said "unknown" would leave a reader believing the platform
        // cannot do the thing it can do.
        var model = Tube(Packet, "particleInCell");

        var (exitCode, stdout, stderr) = Run("validate", model, "--json");

        Assert.NotEqual(0, exitCode);

        var text = stdout + stderr;

        Assert.Contains("/transport/spaceCharge", text, StringComparison.Ordinal);
        Assert.Contains("direct", text, StringComparison.Ordinal);
        Assert.Contains("pic", text, StringComparison.Ordinal);
    }
}
