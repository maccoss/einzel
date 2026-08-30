using System.Globalization;
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

    private string Example(string name)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Run("new", path, "--from-example", name).ExitCode);

        return path;
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

    /// <summary>A diffusive packet can be drawn while it is still in flight.</summary>
    /// <remarks>
    /// <para>
    /// A run reports the density it <em>ended</em> with, so a model whose ions all arrive
    /// drew an empty box - correctly, and uselessly. The only way to see the packet was
    /// to shorten <c>maximumFlightTime</c>, which gets one by throwing away everything
    /// after the moment being looked at.
    /// </para>
    /// <para>
    /// The unit is in the flag's name, as <c>--width-mm</c> already does it. A bare
    /// <c>--at</c> would be ambiguous between microseconds and seconds by a factor of a
    /// million, which is the same rule that makes a bare number a validation error in a
    /// model document.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusivePacketCanBeDrawnInFlight()
    {
        var model = Example("drift-tube-diffusion");

        static int Contours(string svg)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(svg),
                "<g id=\"density\">(.*?)</g>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            return match.Success
                ? System.Text.RegularExpressions.Regex.Count(match.Groups[1].Value, "<path")
                : 0;
        }

        static double Centre(string svg)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(svg),
                "<g id=\"density\">(.*?)</g>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            var xs = System.Text.RegularExpressions.Regex
                .Matches(match.Groups[1].Value, @"[ML]\s*([\d.eE+-]+)")
                .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();

            return (xs.Min() + xs.Max()) / 2.0;
        }

        var atEnd = Path.Combine(_root, "end.svg");
        var early = Path.Combine(_root, "early.svg");
        var late = Path.Combine(_root, "late.svg");

        Assert.Equal(0, Run("render", "section", model, "--out", atEnd).ExitCode);
        Assert.Equal(0, Run("render", "section", model, "--at-us", "50", "--out", early).ExitCode);
        Assert.Equal(0, Run("render", "section", model, "--at-us", "150", "--out", late).ExitCode);

        // At the end every ion has arrived and there is nothing left to contour.
        Assert.Equal(0, Contours(atEnd));

        Assert.True(Contours(early) > 0, "no packet at 50 us");
        Assert.True(Contours(late) > 0, "no packet at 150 us");

        // And it is the packet at that instant rather than one drawing twice: it drifts
        // down the tube between them.
        Assert.True(
            Centre(late) - Centre(early) > 20.0,
            $"the packet moved only {Centre(late) - Centre(early):F1} mm between 50 and 150 us");

        // The instant actually recorded is on the page, because it is not the instant
        // that was asked for - a diffusive step lands where its stability limit puts it.
        Assert.Contains("asked for 50 us", File.ReadAllText(early), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnbuiltKindSaysWhyRatherThanFailingAsATypo()
    {
        // "Not built yet" and "you spelled it wrong" are different problems, and an
        // agent should not have to guess which it hit. Only 'still' is left: it is a
        // raster projection and nothing in this build rasterises.
        var (exitCode, _, stderr) = Run("render", "still", "whatever.json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not built yet", stderr, StringComparison.Ordinal);
        Assert.Contains("render section", stderr, StringComparison.Ordinal);

        // And 'animation' no longer says it. A refusal left behind after the thing was
        // built is the same defect as one missing before it was - both send a caller
        // somewhere the platform is not.
        var (_, _, animation) = Run("render", "animation");

        Assert.DoesNotContain("not built yet", animation, StringComparison.Ordinal);
        Assert.Contains("declared time mapping", animation, StringComparison.Ordinal);
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
