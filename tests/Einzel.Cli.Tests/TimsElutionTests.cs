using System.Text.Json;
using System.Text.Json.Nodes;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A diffusive phase may ramp a parameter, and a sequenced model that stays diffusive is a
/// sequenced run. Together those are a mobility analyser's elution scan.
/// </summary>
/// <remarks>
/// <para>
/// Both were wrong in ways that produced a clean answer. The validator refused a ramp in a
/// diffusive phase outright, because the density solver stepped through a field it held
/// fixed within a phase. And a model that declared <c>diffusion</c> and a sequence that never
/// left it was routed to the plain diffusive path, which reads the field through the
/// time-free interface - so with the refusal lifted and nothing else changed, the elution
/// ramp ran <em>with the ramp silently ignored</em>: exit 0, a density, no warning. The
/// sixth time in this project a time-varying quantity reached through a time-free interface
/// answered at an arbitrary instant rather than failing.
/// </para>
/// <para>
/// The discriminating control is the held run. The trap holds - nothing reaches the
/// detector while the field stands - so if the ramped run collects and the held one does
/// not, the ramp is what eluted them, and the sequenced path is what ran.
/// </para>
/// </remarks>
public sealed class TimsElutionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tims-elution", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// The shipped analyser with a short, coarse elution sequence: hold, then walk the exit
    /// potential to zero. Coarse so two runs fit in a test, and started at the parking point
    /// so no time is spent on the approach.
    /// </summary>
    private string Model(bool ramp)
    {
        Directory.CreateDirectory(Path.Combine(_root, "models"));
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", ramp ? "elute.json" : "hold.json");
        Assert.Equal(0, Run("new", path, "--from-template", "tims-analyzer").ExitCode);

        var document = JsonNode.Parse(File.ReadAllText(path))!;

        document["parameters"]!["sourceX"]!["value"] = 21.10;
        document["transport"]!["maximumFlightTime"] = JsonNode.Parse("""{ "value": 3200, "unit": "us" }""");
        document["transport"]!["densityStep"] = JsonNode.Parse("""{ "scheme": "implicit", "gain": 32 }""");
        document["transport"]!["densityGrid"]!["intervalsX"] = 128;
        document["transport"]!["densityGrid"]!["intervalsY"] = 8;

        var elute = JsonNode.Parse("""
            {
              "name": "elute",
              "duration": { "value": 1500, "unit": "us" },
              "set": { "exitPotential": { "value": 60.0, "unit": "V" } }
            }
            """)!;

        if (ramp)
        {
            elute["ramp"] = JsonNode.Parse("""{ "exitPotential": { "value": 0.0, "unit": "V" } }""");
        }

        // The held control keeps the field ON through its last phase too, or it would drain
        // as well and the comparison would be between two draining runs.
        var drainVolts = ramp ? 0.0 : 60.0;

        // Hold, ramp, then a drain with the field off, so that everything the ramp released
        // has time to reach the detector inside the run. A first version ended the run
        // when the ramp ended and found 975 ions collected of the tens of thousands
        // released - not because the ramp had failed but because release comes at about
        // 800 us of a 1600 us run and the transit to the detector takes most of what was
        // left. The held control was zero either way, which is what said the ramp was
        // fine and the window was not.
        document["sequence"] = new JsonArray(
            JsonNode.Parse("""
                { "name": "hold", "duration": { "value": 100, "unit": "us" },
                  "set": { "exitPotential": { "value": 60.0, "unit": "V" } } }
                """),
            elute,
            JsonNode.Parse($$"""
                { "name": "drain", "duration": { "value": 1500, "unit": "us" },
                  "set": { "exitPotential": { "value": {{drainVolts}}, "unit": "V" } } }
                """));

        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    /// <summary>A ramp in a diffusive phase validates, where it used to be refused.</summary>
    [Fact]
    public void ARampInADiffusivePhaseIsAccepted()
    {
        var (exit, _, stderr) = Run("validate", Model(ramp: true));
        output.WriteLine(stderr.Trim());

        Assert.Equal(0, exit);
        Assert.DoesNotContain("holds fixed within a phase", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ramp elutes the trapped density, and the held field does not - which says the
    /// sequenced path ran and the ramp is what it stepped through.
    /// </summary>
    [Fact]
    public void TheRampElutesWhatTheHeldFieldKeeps()
    {
        var ramped = Run("run", Model(ramp: true), "--json");
        Assert.Equal(0, ramped.ExitCode);

        var held = Run("run", Model(ramp: false), "--json");
        Assert.Equal(0, held.ExitCode);

        using var rampedDoc = JsonDocument.Parse(ramped.Stdout);
        using var heldDoc = JsonDocument.Parse(held.Stdout);

        // Routing: a sequenced diffusive model reports a sequence, not a plain density.
        Assert.True(
            rampedDoc.RootElement.TryGetProperty("sequence", out var sequence),
            "a sequenced diffusive model took the plain diffusive path, which steps a snapshot");

        var arrived = sequence.GetProperty("arrivedIons").GetDouble();
        var heldArrived = heldDoc.RootElement.GetProperty("sequence").GetProperty("arrivedIons").GetDouble();

        output.WriteLine($"ramped: {arrived:F0} ions reached the detector; held: {heldArrived:F3}");

        // The trap holds. This is the control that gives the next line its meaning.
        Assert.True(heldArrived < 1.0, $"the held field let {heldArrived:F1} ions through - it is not a trap");

        // And the ramp releases them - most of what survived the radial loss, since this
        // stage carries no RF and the bore wall takes a good part of the density.
        Assert.True(arrived > 1000.0, $"the ramp eluted only {arrived:F0} ions");

        // With a mean arrival after the hold and inside the run.
        var mean = sequence.GetProperty("meanArrivalUs").GetDouble();
        output.WriteLine($"mean arrival {mean:F0} us, spread {sequence.GetProperty("arrivalSpreadUs").GetDouble():F0} us");

        Assert.InRange(mean, 100.0, 3100.0);

        // The spectrum is written whole, because a mean and a width have lost its shape.
        var artifacts = rampedDoc.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(a => a.GetString()!).ToList();
        // Resolved against the project root, because that is what a manifest's artifact
        // paths are relative to. The sequenced path used to store them absolute - the one
        // path of four that did - so this read them straight off the document and worked
        // by accident, and a manifest naming files by where they sat on the machine that
        // wrote them cannot travel, which is half of what PRJ-3 is for.
        var spectrum = Path.Combine(
            _root,
            Assert.Single(artifacts, a => a.EndsWith(".arrivals.csv", StringComparison.Ordinal)));

        Assert.True(File.Exists(spectrum), $"the arrivals file {spectrum} was not written");
        Assert.True(File.ReadLines(spectrum).Skip(1).Any(), "the arrivals file has a header and no rows");

        // Elution is in mobility order and the release voltage is v_g / (mu E_peak), so
        // the release comes when the exit potential has fallen to about 32 V of 60 - a
        // little over half way through the ramp - and the arrivals follow it after a
        // transit. Before the hold ends nothing physical can arrive.
        //
        // Read as a quantile, not as the first non-empty bin. A first version asserted on
        // the first bin and found ions "arriving" at 22 us, during the hold: the
        // Scharfetter-Gummel flux moves an exponentially small amount of density across
        // every face at every step, so the collecting boundary sees 1e-100 of an ion from
        // the first step onward. That is the scheme's tail, not a transit, and a
        // spectrum's onset is where a measurable fraction has arrived.
        var bins = File.ReadLines(spectrum).Skip(1)
            .Select(line => line.Split(','))
            .Select(cells => (
                Us: double.Parse(cells[0], System.Globalization.CultureInfo.InvariantCulture),
                Ions: double.Parse(cells[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        var total = bins.Sum(b => b.Ions);
        var running = 0.0;
        var onsetUs = bins.First(b => (running += b.Ions) >= 0.01 * total).Us;
        output.WriteLine($"onset (1 per cent of arrivals) at {onsetUs:F0} us");

        Assert.True(onsetUs > 100.0, "one per cent of the ions had arrived before the hold ended, so the trap did not hold");
    }

    /// <summary>
    /// A held phase assembles its operator once, whatever the phase after it does; a ramp
    /// assembles every step.
    /// </summary>
    /// <remarks>
    /// The probe that decides whether a phase changes the field sampled the phase's END,
    /// which for a staged field is already the next phase's weights - so a hold followed by
    /// a step read as a change and the solver rebuilt its operator every step of the hold,
    /// for a ramp that was not there. The density was right; the cost was not, and nothing
    /// reported it. The assembly count per phase is what makes this a test rather than a
    /// stopwatch, and the step after the hold is what makes it a test of the boundary rather
    /// than of a hold followed by more of the same.
    /// </remarks>
    [Fact]
    public void AHeldPhaseBeforeAStepAssemblesOnce()
    {
        var path = Model(ramp: true);
        var document = JsonNode.Parse(File.ReadAllText(path))!;

        document["transport"]!["maximumFlightTime"] = JsonNode.Parse("""{ "value": 300, "unit": "us" }""");
        document["sequence"] = new JsonArray(
            JsonNode.Parse("""
                { "name": "hold", "duration": { "value": 100, "unit": "us" },
                  "set": { "exitPotential": { "value": 60.0, "unit": "V" } } }
                """),
            JsonNode.Parse("""
                { "name": "step", "duration": { "value": 100, "unit": "us" },
                  "set": { "exitPotential": { "value": 40.0, "unit": "V" } } }
                """),
            JsonNode.Parse("""
                { "name": "walk", "duration": { "value": 100, "unit": "us" },
                  "set": { "exitPotential": { "value": 40.0, "unit": "V" } },
                  "ramp": { "exitPotential": { "value": 20.0, "unit": "V" } } }
                """));

        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var (exit, stdout, stderr) = Run("run", path, "--json");
        Assert.True(exit == 0, stderr);

        using var doc = JsonDocument.Parse(stdout);
        var phases = doc.RootElement.GetProperty("sequence").GetProperty("phases").EnumerateArray()
            .Select(p => (Name: p.GetProperty("name").GetString(), Assemblies: p.GetProperty("assemblies").GetInt32()))
            .ToList();

        output.WriteLine(string.Join(", ", phases.Select(p => $"{p.Name}: {p.Assemblies} assemblies")));

        Assert.Equal(1, phases[0].Assemblies);
        Assert.Equal(1, phases[1].Assemblies);
        Assert.True(phases[2].Assemblies > 1, "the ramped phase assembled once, so the ramp was not followed");
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
