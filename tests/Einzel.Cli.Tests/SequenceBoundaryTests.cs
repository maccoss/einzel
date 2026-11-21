using System.Text.Json.Nodes;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>Where one phase of a sequence ends and the next begins, nothing is lost or refused wrongly.</summary>
public sealed class SequenceBoundaryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-sequence-boundary", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Model(string sequence)
    {
        var document = JsonNode.Parse(RunMemoryCases.Document("tube-plain", 64))!;

        document["parameters"] = JsonNode.Parse("""{ "pushField": { "value": 0.0, "unit": "V/m" } }""");
        document["fields"]![0] = JsonNode.Parse("""{ "type": "uniform", "field": { "expression": [ "pushField", "0", "0" ] } }""");
        document["sequence"] = JsonNode.Parse(sequence);

        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, Guid.NewGuid().ToString("N")[..8] + ".json");

        File.WriteAllText(path, document.ToJsonString());

        return path;
    }

    /// <summary>
    /// A trajectory leg whose field changes at its end, with its ions still in flight, runs to
    /// the boundary and hands its packet on, where it used to stop with a step-size underflow
    /// an attosecond short of the switch.
    /// </summary>
    /// <param name="settle">The first phase, in microseconds.</param>
    /// <param name="extract">The second, in microseconds.</param>
    /// <param name="hold">The diffusive third, in microseconds.</param>
    [Theory]
    [InlineData("0.1", "0.1", "3.3")]
    [InlineData("1", "1", "3")]
    public void ALegEndingAtAFieldChangeRunsToTheBoundary(string settle, string extract, string hold)
    {
        var path = Model($$"""
            [ { "name": "settle", "duration": { "value": {{settle}}, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 1000, "unit": "V/m" } } },
              { "name": "extract", "duration": { "value": {{extract}}, "unit": "us" }, "mode": "trajectory", "set": { "pushField": { "value": 2000000, "unit": "V/m" } } },
              { "name": "hold", "duration": { "value": {{hold}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        var model = Core.Model.ModelValidator.Validate(Io.ModelJson.Parse(File.ReadAllText(path)), null, _root).Model!;
        var (field, _) = Fields.FieldAssembly.BuildReported(model);

        var run = SequencedRun.Execute(model, field, Io.GasFlowImport.Resolve(model.Gas, _root));

        output.WriteLine(string.Join("; ", run.Phases.Select(p => $"{p.Name}: {p.Trajectories} trajectories, {p.Arrived} arrived")));

        Assert.Equal(3, run.Phases.Count);
    }

    /// <summary>
    /// A phase the run cannot resolve - its end, added up as the run adds it, equal to its start,
    /// or after it by less than the integrator's smallest step - is refused as a model error at its
    /// duration, where it used to pass validation and fail in the field as an internal error, or
    /// reach the run as a leg that underflows at once. A zero duration is refused at the same path.
    /// </summary>
    /// <param name="before">Each of the two phases before it, in microseconds.</param>
    /// <param name="duration">The third phase's duration, in microseconds.</param>
    /// <param name="says">What the refusal says.</param>
    [Theory]
    [InlineData("3", "1e-30", "its end is its start")]
    [InlineData("0.5", "1e-13", "smallest step")]
    [InlineData("3", "0", "positive time")]
    public void APhaseThatEndsWhereItBeginsIsRefusedAtItsDuration(string before, string duration, string says)
    {
        var path = Model($$"""
            [ { "name": "hold", "duration": { "value": {{before}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } },
              { "name": "push", "duration": { "value": {{before}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 100, "unit": "V/m" } } },
              { "name": "blip", "duration": { "value": {{duration}}, "unit": "us" }, "mode": "diffusion", "set": { "pushField": { "value": 0, "unit": "V/m" } } } ]
            """);

        foreach (var verb in new[] { "validate", "run" })
        {
            var (exit, stdout, stderr) = Run(verb, path, "--json");

            output.WriteLine($"{verb}: exit {exit}: {stdout.Trim()} {stderr.Trim()}");

            Assert.Equal(1, exit);

            var error = JsonNode.Parse(stdout)!["errors"]![0]!;

            Assert.Equal("VALUE_OUT_OF_BOUNDS", error["code"]!.GetValue<string>());
            Assert.Equal("/sequence/2/duration", error["path"]!.GetValue<string>());
            Assert.Contains(says, error["constraint"]!.GetValue<string>(), StringComparison.Ordinal);
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
