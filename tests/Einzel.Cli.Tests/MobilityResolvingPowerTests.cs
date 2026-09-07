using System.Text.Json;
using System.Text.Json.Nodes;

using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Model;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The mobility resolving power of an elution scan, and the path a sequenced diffusive model
/// takes through the figures of merit.
/// </summary>
/// <remarks>
/// <para>
/// A trapped ion mobility analyser releases an ion of mobility <c>K</c> when its ramped
/// field can no longer hold it, at a parameter value proportional to <c>1/K</c>, so
/// <c>K/dK</c> is <c>V/dV</c>: the ramped parameter at the arrival peak over how far it
/// moves during the peak's width. That figure needs the sequenced run's arrivals, and the
/// figures of merit had never taken the sequenced path - <c>transitTime</c> stepped a
/// snapshot of the field with the ramp ignored, exit 0, no warning, while <c>einzel run</c>
/// on the same model went through the sequencer. The transit test here is the one that
/// says the two now agree: a held snapshot releases nothing, so it fails if the snapshot
/// path runs.
/// </para>
/// <para>
/// <b>The models are coarse on purpose and the run cost is stated.</b> The shipped
/// elution shape at a 128 x 8 density grid and gain 32 takes about four minutes a run in a
/// Debug build; these use 64 x 8 at gain 64 or more, which is under a minute. What is
/// asserted is a property (finite, positive, inside a wide band; equal to the run's own
/// mean) and never a value, because a coarse model's numbers are the grid's.
/// </para>
/// <para>
/// <b>And the first coarse model tried put the peak outside the ramp.</b> The ion is
/// released when the exit potential falls to about 27 V of 60, a little over half way
/// through, and then trails its moving balance point down the tunnel and drifts the exit
/// path at the gas speed - about 900 us on the shipped geometry. On a 1500 us ramp that
/// lands the arrival peak 250 us after the ramp has ended, where the release parameter is
/// undefined; the figure says so and reports nothing rather than extrapolating the ramp to
/// a negative voltage. That is tested as a behaviour rather than treated as a failed
/// attempt at the positive case, which uses a longer ramp and a shorter exit path.
/// </para>
/// </remarks>
public sealed class MobilityResolvingPowerTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-mobility-rp", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>How the scaffolded analyser is shaped for one test.</summary>
    /// <param name="Sequence">Whether the model declares a sequence at all.</param>
    /// <param name="Ramp">Whether the elute phase ramps, or holds like the others.</param>
    /// <param name="EluteUs">How long the elute phase lasts.</param>
    /// <param name="DrainUs">How long the drain after it lasts.</param>
    /// <param name="Gain">The implicit density step's gain.</param>
    /// <param name="DetectorPadMm">Where the collecting plane sits past the exit element.</param>
    /// <param name="ExitLengthMm">How far the exit element runs past the tunnel.</param>
    private sealed record Shape(
        bool Sequence = true,
        bool Ramp = true,
        double EluteUs = 1500,
        double DrainUs = 1500,
        int Gain = 64,
        double DetectorPadMm = 12,
        double ExitLengthMm = 15);

    /// <summary>
    /// The shipped elution shape - hold 100 us at 60 V, ramp the exit potential to zero, drain
    /// at zero - on the parent's coarse grid, at a gain that halves the assemblies.
    /// </summary>
    private static readonly Shape Shipped = new();

    /// <summary>
    /// A shape whose arrival peak lands inside the ramp: the ramp slowed to 2400 us and the
    /// exit path shortened to 8 + 1 mm, so the release-to-detector lag is a smaller part of
    /// the ramp than it is on the shipped geometry.
    /// </summary>
    private static readonly Shape Resolved = new(
        EluteUs: 2400, DrainUs: 700, Gain: 128, DetectorPadMm: 1, ExitLengthMm: 8);

    private string Model(Shape shape, string name = "elute")
    {
        Directory.CreateDirectory(Path.Combine(_root, "models"));

        if (!File.Exists(Path.Combine(_root, "AGENTS.md")))
        {
            Assert.Equal(0, Run("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", $"{name}.json");
        Assert.Equal(0, Run("new", path, "--from-template", "tims-analyzer").ExitCode);

        var document = JsonNode.Parse(File.ReadAllText(path))!;

        // Parked at the 60 V balance point, so no time is spent on the approach.
        document["parameters"]!["sourceX"]!["value"] = 21.10;
        document["parameters"]!["detectorPad"]!["value"] = shape.DetectorPadMm;
        document["parameters"]!["exitLength"]!["value"] = shape.ExitLengthMm;

        var totalUs = 100.0 + shape.EluteUs + shape.DrainUs;
        document["transport"]!["maximumFlightTime"] = JsonNode.Parse($$$"""{ "value": {{{totalUs}}}, "unit": "us" }""");
        document["transport"]!["densityStep"] = JsonNode.Parse($$$"""{ "scheme": "implicit", "gain": {{{shape.Gain}}} }""");
        document["transport"]!["densityGrid"]!["intervalsX"] = 64;
        document["transport"]!["densityGrid"]!["intervalsY"] = 8;

        if (shape.Sequence)
        {
            var elute = JsonNode.Parse($$$"""
                {
                  "name": "elute",
                  "duration": { "value": {{{shape.EluteUs}}}, "unit": "us" },
                  "set": { "exitPotential": { "value": 60.0, "unit": "V" } }
                }
                """)!;

            if (shape.Ramp)
            {
                elute["ramp"] = JsonNode.Parse("""{ "exitPotential": { "value": 0.0, "unit": "V" } }""");
            }

            // A held control keeps the field on through the drain too, or the drain would
            // release everything the hold kept.
            var drainVolts = shape.Ramp ? 0.0 : 60.0;

            document["sequence"] = new JsonArray(
                JsonNode.Parse("""
                    { "name": "hold", "duration": { "value": 100, "unit": "us" },
                      "set": { "exitPotential": { "value": 60.0, "unit": "V" } } }
                    """),
                elute,
                JsonNode.Parse($$$"""
                    { "name": "drain", "duration": { "value": {{{shape.DrainUs}}}, "unit": "us" },
                      "set": { "exitPotential": { "value": {{{drainVolts}}}, "unit": "V" } } }
                    """));
        }

        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    /// <summary>Writes a project test expecting one figure of one model.</summary>
    private string Expectation(string modelPath, string figure, double value, string unit, double tolerance)
    {
        var tests = Path.Combine(_root, "tests");
        Directory.CreateDirectory(tests);

        // The scaffolded reflectron test would run too and cost the same again; only the
        // expectation under test is left in the project.
        foreach (var stale in Directory.GetFiles(tests, "*.json"))
        {
            File.Delete(stale);
        }

        var path = Path.Combine(tests, $"{Path.GetFileNameWithoutExtension(modelPath)}-{figure}.test.json");
        var relative = Path.GetRelativePath(tests, modelPath).Replace('\\', '/');

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.1",
              "name": "{{figure}} on the coarse elution scan",
              "description": "A property rather than a value: the model is coarse and its numbers are the grid's.",
              "model": "{{relative}}",
              "expect": [
                {
                  "figureOfMerit": "{{figure}}",
                  "value": {{value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}},
                  "unit": "{{unit}}",
                  "tolerance": {{tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}
                }
              ]
            }
            """);

        return path;
    }

    private static CompiledModel Compile(string path)
    {
        var document = Io.ModelJson.Parse(File.ReadAllText(path));
        var validation = ModelValidator.Validate(document, null, Path.GetDirectoryName(path));

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return validation.Model!;
    }

    private static (double? Observed, bool Passed) FirstAssertion(string testJson)
    {
        using var document = JsonDocument.Parse(testJson);
        var assertion = document.RootElement.GetProperty("tests")[0].GetProperty("assertions")[0];

        double? observed = assertion.TryGetProperty("observed", out var got) && got.ValueKind == JsonValueKind.Number
            ? got.GetDouble()
            : null;

        return (observed, assertion.GetProperty("passed").GetBoolean());
    }

    /// <summary>
    /// A project test may expect the figure, it passes on a scan whose peak lands inside the
    /// ramp, and the result carries the definition it was computed under.
    /// </summary>
    /// <remarks>
    /// Expected 5 at a tolerance of 19 admits anything from -90 to 100; what the test
    /// asserts is that a number came out, that it is finite and positive, and that it is
    /// not absurd. The value itself belongs to a study on a converged grid, not here.
    /// </remarks>
    [Fact]
    public void AProjectTestMayExpectTheFigure()
    {
        var model = Model(Resolved);
        Expectation(model, "mobilityResolvingPower", 5.0, "1", 19.0);

        var (exit, stdout, stderr) = Run("test", _root, "--json");
        output.WriteLine(stderr.Trim());
        Assert.True(exit == 0, $"einzel test exited {exit}: {stderr}");

        var (observed, passed) = FirstAssertion(stdout);
        output.WriteLine($"mobilityResolvingPower on the coarse scan: {observed}");

        Assert.True(passed, "the expectation did not pass");
        Assert.NotNull(observed);
        Assert.True(double.IsFinite(observed!.Value), "the figure is not finite");
        Assert.True(observed.Value > 0.0, $"the figure is not positive: {observed}");
        Assert.True(observed.Value < 100.0, $"the figure is absurdly large for a coarse scan: {observed}");

        // The same evaluation in process, with a sink, is how the warnings are read: the
        // definition rides out on every result as provenance, and the peak landed inside
        // the ramp so nothing says otherwise.
        var warnings = new List<Core.Results.ValidityWarning>();
        var figure = FiguresOfMerit.Evaluator("mobilityResolvingPower", report: warnings.Add)(Compile(model));

        foreach (var warning in warnings.Where(w => w.Code.StartsWith("mobility.", StringComparison.Ordinal)))
        {
            output.WriteLine($"{warning.Code}: {warning.Message}");
        }

        Assert.NotNull(figure);
        Assert.Equal(observed.Value, figure!.Value, 1e-9);
        Assert.Contains(warnings, w => w.Code == "mobility.resolving-power-definition");
        Assert.DoesNotContain(warnings, w => w.Code == "mobility.peak-outside-ramp");
    }

    /// <summary>
    /// On the shipped shape the peak arrives after the ramp has ended, and the figure says
    /// so rather than extrapolating the ramp to a voltage it never held.
    /// </summary>
    [Fact]
    public void APeakAfterTheRampIsNullAndSaysWhy()
    {
        var model = Compile(Model(Shipped));

        var warnings = new List<Core.Results.ValidityWarning>();
        var figure = FiguresOfMerit.Evaluator("mobilityResolvingPower", report: warnings.Add)(model);

        foreach (var warning in warnings.Where(w => w.Code.StartsWith("mobility.", StringComparison.Ordinal)))
        {
            output.WriteLine($"{warning.Code}: {warning.Message}");
        }

        Assert.Null(figure);
        Assert.Contains(warnings, w => w.Code == "mobility.peak-outside-ramp"
            && w.Severity == Core.Results.WarningSeverity.Qualified
            && w.Message.Contains("after the ramp ended", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Code == "mobility.resolving-power-definition"
            && w.Severity == Core.Results.WarningSeverity.Provenance);
    }

    /// <summary>A model with no sequence has no ramp to read against, and is refused.</summary>
    [Fact]
    public void AModelWithNoSequenceIsRefused()
    {
        var path = Model(Shipped with { Sequence = false }, "held");

        var failure = Assert.Throws<EinzelException>(
            () => FiguresOfMerit.Evaluator("mobilityResolvingPower")(Compile(path)));

        output.WriteLine(failure.Error.ToString());
        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal("/sequence", failure.Error.Path);

        // And the refusal reaches the command line as a refusal, not as a failed assertion:
        // a wrong question is a different thing from a wrong answer.
        Expectation(path, "mobilityResolvingPower", 5.0, "1", 19.0);
        var (exit, _, stderr) = Run("test", _root, "--json");

        Assert.NotEqual(0, exit);
        Assert.Contains("/sequence", stderr, StringComparison.Ordinal);
    }

    /// <summary>A sequence in which every phase holds ramps nothing, and is refused.</summary>
    [Fact]
    public void ASequenceThatRampsNothingIsRefused()
    {
        var path = Model(Shipped with { Ramp = false }, "hold");

        var failure = Assert.Throws<EinzelException>(
            () => FiguresOfMerit.Evaluator("mobilityResolvingPower")(Compile(path)));

        output.WriteLine(failure.Error.ToString());
        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal("/sequence", failure.Error.Path);
        Assert.Contains("every phase holds", failure.Error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// The transit time of a sequenced model is the sequenced run's own mean arrival - which
    /// says the figure took the sequenced path, since a held snapshot releases nothing.
    /// </summary>
    [Fact]
    public void TransitTimeIsTheSequencedRunsOwnMeanArrival()
    {
        var model = Model(Shipped);

        var run = Run("run", model, "--json");
        Assert.True(run.ExitCode == 0, run.Stderr);

        double meanArrivalUs;
        using (var document = JsonDocument.Parse(run.Stdout))
        {
            var sequence = document.RootElement.GetProperty("sequence");
            meanArrivalUs = sequence.GetProperty("meanArrivalUs").GetDouble();
            output.WriteLine($"run: {sequence.GetProperty("arrivedIons").GetDouble():F0} ions arrived, mean {meanArrivalUs:F1} us");
        }

        // Within a microsecond of the run's own number: the tolerance is relative, so it
        // is a microsecond over the mean.
        Expectation(model, "transitTime", meanArrivalUs, "us", 1.0 / meanArrivalUs);

        var (exit, stdout, stderr) = Run("test", _root, "--json");
        Assert.True(exit == 0, $"einzel test exited {exit}: {stderr}");

        var (observed, passed) = FirstAssertion(stdout);
        output.WriteLine($"transitTime: {observed} us against the run's {meanArrivalUs:F3} us");

        Assert.NotNull(observed);
        Assert.True(passed, $"transitTime {observed} us is not the run's mean arrival {meanArrivalUs} us");
        Assert.InRange(observed!.Value, meanArrivalUs - 1.0, meanArrivalUs + 1.0);
    }

    /// <summary>
    /// The width of a sequenced run's final density is not something the sequenced outcome
    /// hands out, so the figure refuses rather than measuring a snapshot.
    /// </summary>
    [Fact]
    public void RadialSpreadRefusesASequencedModel()
    {
        var failure = Assert.Throws<EinzelException>(
            () => FiguresOfMerit.Evaluator("radialSpread")(Compile(Model(Shipped))));

        output.WriteLine(failure.Error.ToString());
        Assert.Equal(ErrorCodes.SchemaInvalid, failure.Error.Code);
        Assert.Equal("/sequence", failure.Error.Path);
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
