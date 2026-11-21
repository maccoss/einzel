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
}
