using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Project;

namespace Einzel.Cli.Tests;

public sealed class InputProvenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "einzel-inputs", Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static void Cli(params string[] args)
    {
        var oldOut = Console.Out;
        var oldError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var code = Program.Main(args);
            Assert.True(code == 0, $"exit {code}: {error}");
        }
        finally { Console.SetOut(oldOut); Console.SetError(oldError); }
    }

    [Fact]
    public void ImportedPressureEditsAndDeletionInvalidateTheRun()
    {
        Cli("init", _root);
        var model = Path.Combine(_root, "models", "graded.json");
        Cli("new", model, "--from-example", "drift-tube-pressure-gradient");
        Cli("run", model);
        Assert.True(VerifyCommand.Execute(_root).AllCurrent);
        var manifestPath = Directory.GetFiles(Path.Combine(_root, "results"), "*.manifest.json").Single();
        var manifest = RunManifest.FromJson(File.ReadAllText(manifestPath))!;
        var dependency = Assert.Single(manifest.InputHashes!);
        var data = Path.GetFullPath(RunManifest.Local(dependency.Key), _root);
        var original = File.ReadAllText(data);
        File.AppendAllText(data, "\n<!-- edited imported field -->\n");
        Assert.False(VerifyCommand.Execute(_root).AllCurrent);
        File.WriteAllText(data, original);
        Assert.True(VerifyCommand.Execute(_root).AllCurrent);
        File.Delete(data);
        Assert.False(VerifyCommand.Execute(_root).AllCurrent);
    }

    [Fact]
    public void MovingTheModelCannotSilentlyRetargetItsRelativeInput()
    {
        Cli("init", _root);
        Directory.CreateDirectory(Path.Combine(_root, "source"));
        var model = Path.Combine(_root, "source", "graded.json");
        Cli("new", model, "--from-example", "drift-tube-pressure-gradient");
        Cli("run", model);
        Assert.True(VerifyCommand.Execute(_root).AllCurrent);
        File.Move(model, Path.Combine(_root, "models", "graded.json"));
        var result = VerifyCommand.Execute(_root);
        Assert.True(Assert.Single(result.Results).ModelMatches);
        Assert.False(result.AllCurrent);
    }

    [Fact]
    public void LegacyManifestCannotCertifyUnrecordedImportedData()
    {
        Cli("init", _root);
        var model = Path.Combine(_root, "models", "graded.json");
        Cli("new", model, "--from-example", "drift-tube-pressure-gradient");
        Cli("run", model);
        var path = Directory.GetFiles(Path.Combine(_root, "results"), "*.manifest.json").Single();
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(json.Remove("inputHashes"));
        File.WriteAllText(path, json.ToJsonString());
        Assert.False(VerifyCommand.Execute(_root).AllCurrent);
    }

    [Fact]
    public void StudyEditsInvalidateTheStudyEvenWhenTheModelIsUnchanged()
    {
        Cli("init", _root);
        var path = Path.Combine(_root, "studies", "test.json");
        File.WriteAllText(path, """
            { "model": "../models/reflectron.json", "figureOfMerit": "flightTime",
              "draws": 2, "seed": 7, "channels": [
                { "parameter": "turningDepth", "distribution": "normal", "halfWidth": 0.01, "unit": "mm" }
              ] }
            """);
        Cli("sweep", path);
        Assert.True(VerifyCommand.Execute(_root).AllCurrent);
        var original = File.ReadAllText(path);
        File.WriteAllText(path, original.Replace("\"draws\": 2", "\"draws\": 3", StringComparison.Ordinal));
        Assert.False(VerifyCommand.Execute(_root).AllCurrent);
    }
}
