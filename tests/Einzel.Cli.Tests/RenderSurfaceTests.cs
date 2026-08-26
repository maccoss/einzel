using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// The render verb, driven through the command surface.
/// </summary>
/// <remarks>
/// RND-1 makes rendering an engine capability rather than a shell feature, so the
/// CLI is a first-class consumer of it rather than an afterthought - and these run
/// headless in CI on Linux, which is the requirement stated as a test.
/// </remarks>
public sealed class RenderSurfaceTests : IDisposable
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

    private string Model(string template)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{template}.json");

        Assert.Equal(0, Run("new", path, "--from-template", template).ExitCode);

        return path;
    }

    [Fact]
    public void RenderSectionWritesAnSvgIntoFigures()
    {
        var model = Model("quadrupole");

        var (exitCode, stdout, _) = Run("render", "section", model, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.Equal("section", root.GetProperty("kind").GetString());
        Assert.Equal("svg", root.GetProperty("format").GetString());
        Assert.True(root.GetProperty("written").GetBoolean());

        var path = root.GetProperty("artifacts")[0].GetString()!;

        // PRJ-1: a figure is a small tracked text file in figures/, not something
        // large and binary in the scratch directory.
        Assert.Equal(Path.Combine(_root, "figures"), Path.GetDirectoryName(path));
        Assert.True(File.Exists(path));

        var svg = File.ReadAllText(path);

        Assert.StartsWith("<?xml", svg, StringComparison.Ordinal);
        Assert.Contains("<path ", svg, StringComparison.Ordinal);

        // GRD-12 and PRJ-3: what made it, and what it does not claim.
        Assert.Contains("decimation tolerance", svg, StringComparison.Ordinal);
        Assert.Contains("sha256:", svg, StringComparison.Ordinal);

        Assert.True(root.GetProperty("decimationToleranceMm").GetDouble() > 0.0);
    }

    [Fact]
    public void DryRunWritesNothing()
    {
        // CLI-5: --dry-run on every mutating command.
        var model = Model("quadrupole");

        var (exitCode, stdout, _) = Run("render", "section", model, "--json", "--dry-run");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.False(document.RootElement.GetProperty("written").GetBoolean());
        Assert.False(File.Exists(document.RootElement.GetProperty("artifacts")[0].GetString()!));
    }

    [Fact]
    public void APdfComesOutAsAPdf()
    {
        var model = Model("quadrupole");

        var (exitCode, stdout, _) = Run("render", "section", model, "--format", "pdf", "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);
        var path = document.RootElement.GetProperty("artifacts")[0].GetString()!;

        Assert.EndsWith(".pdf", path, StringComparison.Ordinal);

        var bytes = File.ReadAllBytes(path);

        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public void ARenderSpecNamesItsOwnModel()
    {
        // RND-2: the spec is text in figures/, versioned with the model, so the
        // figure in a paper is regenerable from the repository.
        var model = Model("quadrupole");

        var spec = Path.Combine(_root, "figures", "quad-section.json");
        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);

        File.WriteAllText(spec, """
        {
          "renderSpecVersion": "0.1",
          "kind": "section",
          "model": "../models/quadrupole.json",
          "widthMm": 90,
          "equipotentials": 6,
          "caption": "As it will appear in the paper"
        }
        """);

        var (exitCode, stdout, _) = Run("render", "section", spec, "--json");
        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        Assert.Equal(
            Path.GetFullPath(model),
            Path.GetFullPath(document.RootElement.GetProperty("modelPath").GetString()!));

        Assert.Equal(90.0, document.RootElement.GetProperty("pageMm")[0].GetDouble());

        var svg = File.ReadAllText(document.RootElement.GetProperty("artifacts")[0].GetString()!);

        Assert.Contains("As it will appear in the paper", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnbuiltKindsSayWhyRatherThanFailingAsTypos()
    {
        // "Not built yet" and "you spelled it wrong" are different problems, and an
        // agent should not have to guess which it hit.
        foreach (var kind in new[] { "still", "animation" })
        {
            var (exitCode, _, stderr) = Run("render", kind, "whatever.json");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("not built yet", stderr, StringComparison.Ordinal);
            Assert.Contains("render section", stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFigureFromAnUnconvergedFieldIsMarkedOnStderrAndOnThePage()
    {
        // GRD-2 through to the artifact, and RND-11: a qualified result has to be
        // visually distinguishable in rendered output, not only mentioned in
        // metadata nobody opens.
        var model = Model("quadrupole");

        // A tolerance below round-off: the solve stalls and reports not-converged.
        var text = File.ReadAllText(model);
        var at = text.IndexOf("\"solve\":", StringComparison.Ordinal);

        Assert.True(at >= 0, "expected a solved2d element to strain");

        at = text.IndexOf('{', at) + 1;
        text = text[..at] + "\"tolerance\": 1e-30," + text[at..];

        File.WriteAllText(model, text);

        var (exitCode, stdout, stderr) = Run("render", "section", model);

        Assert.Equal(0, exitCode);
        Assert.Contains("field.not-converged", stderr, StringComparison.Ordinal);

        var path = stdout.Split('\n')[0]["wrote ".Length..].Trim();
        var svg = File.ReadAllText(path);

        Assert.Contains("QUALIFIED", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"taint\"", svg, StringComparison.Ordinal);
    }
}
