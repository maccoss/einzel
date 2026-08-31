using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// GRD-2's layers, enumerated: a validity warning must reach every one of them.
/// </summary>
/// <remarks>
/// <para>
/// The requirement names its own population, which is what makes it checkable rather
/// than aspirational: <i>validity warnings travel with the result through every layer —
/// engine, command layer, CLI output, MCP response, exported file, rendered figure and
/// video.</i> That is seven, and a test per layer is the whole of it.
/// </para>
/// <para>
/// <b>Enumerating them found two defects on the first pass</b>, both in the layers
/// furthest from the engine, which is where this project has dropped evidence every time.
/// The exported <c>.vtu</c> carried no warnings at all — through the same writer and the
/// same optional <c>provenance</c> parameter that the density path beside it has always
/// used. And the rendered figure not only dropped the warnings but was not flying the gas
/// (see <see cref="GasFigureTests"/>), so a figure of an ion at a millibar drew the vacuum
/// flight.
/// </para>
/// <para>
/// <b>The MCP layer is asserted in <c>Einzel.Mcp.Tests</c> rather than here</b>, because
/// this project deliberately does not reference that assembly from the CLI tests. It
/// carries warnings structurally: every tool result is <c>CommandJson.Write</c> of the
/// same outcome record the CLI serialises, compared byte for byte there, so a warning
/// reaches an MCP client by being on the record rather than by anyone remembering to
/// copy it across.
/// </para>
/// <para>
/// <c>gas-flow-carry</c> is the model throughout: at 0.008 mbar it sits in the overlap
/// band REG-2 exists for, so it earns real validity warnings rather than provenance ones,
/// and it earns them from the physics rather than from a contrivance.
/// </para>
/// </remarks>
public sealed class WarningPropagationTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>A warning this model must earn, used to anchor every layer.</summary>
    /// <remarks>
    /// A regime warning rather than a convergence one, because REG-2's are the warnings
    /// GRD-3 makes non-suppressible and so the ones the requirement is really about.
    /// </remarks>
    private const string Anchor = "regime.overlap-band";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-grd2", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
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

    private string Model()
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", "gas.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, "--from-example", "gas-flow-carry").ExitCode);
        }

        return path;
    }

    /// <summary>The warning codes on a <c>--json</c> run result.</summary>
    private static IReadOnlyList<string> ResultWarnings(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement
            .GetProperty("flightTime")
            .GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()!)];
    }

    /// <summary>Layers 1 and 2: the engine computes it and the command layer carries it.</summary>
    /// <remarks>
    /// These two are one test because the command layer's result <em>is</em> the engine's
    /// envelope — <c>Measured</c> with its warnings attached — rather than a copy of it.
    /// Separating them would assert that a record equals itself.
    /// </remarks>
    [Fact]
    public void TheEngineAndTheCommandLayerCarryIt()
    {
        var model = Model();

        var project = Project.ProjectLayout.Find(model);

        Assert.NotNull(project);

        var (run, _) = Commands.RunCommand.Execute(
            model, project!, exportVtu: false, DateTimeOffset.UnixEpoch);

        Assert.NotNull(run);

        var codes = run!.FlightTime.Warnings.Select(w => w.Code).ToList();

        output.WriteLine(string.Join(", ", codes));

        Assert.Contains(Anchor, codes, StringComparer.Ordinal);
    }

    /// <summary>Layer 3a: <c>--json</c> on stdout, which is what an agent reads.</summary>
    [Fact]
    public void TheJsonResultCarriesIt()
    {
        var (exitCode, stdout, _) = Cli("run", Model(), "--json");

        Assert.Equal(0, exitCode);

        var codes = ResultWarnings(stdout);

        output.WriteLine(string.Join(", ", codes));

        Assert.Contains(Anchor, codes, StringComparer.Ordinal);
    }

    /// <summary>Layer 3b: the human output, where diagnostics go to stderr (CLI-2).</summary>
    /// <remarks>
    /// Asserted on stderr specifically, and the streams are captured separately, because
    /// merging them destroys the distinction CLI-2 is about — a mistake made in this
    /// project's own manual checking once already.
    /// </remarks>
    [Fact]
    public void TheHumanOutputCarriesItOnStderr()
    {
        var (exitCode, stdout, stderr) = Cli("run", Model());

        Assert.Equal(0, exitCode);

        output.WriteLine(stderr.Trim());

        Assert.Contains(Anchor, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Anchor, stdout, StringComparison.Ordinal);
    }

    /// <summary>Layer 5: the exported file — and it must carry the whole set.</summary>
    /// <remarks>
    /// <para>
    /// <b>The layer that was broken, and set equality rather than containment is the
    /// point.</b> The trajectory <c>.vtu</c> carried no warnings at all while the density
    /// <c>.vti</c> beside it — same writer, same optional <c>provenance</c> parameter —
    /// always had. Asserting only that the anchor appears would pass on a file that
    /// carried one warning and dropped the rest.
    /// </para>
    /// <para>
    /// A <c>.vtu</c> is the artifact that travels furthest: opened in ParaView, months
    /// later, by someone who never saw the result envelope it came from. It is the layer
    /// where a dropped warning does the most damage and the last one anybody checks.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExportedFileCarriesTheSameSetAsTheResult()
    {
        var model = Model();

        var (exitCode, stdout, _) = Cli("run", model, "--vtu", "--json");

        Assert.Equal(0, exitCode);

        var expected = ResultWarnings(stdout).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(Anchor, expected, StringComparer.Ordinal);

        var vtu = Directory
            .EnumerateFiles(Path.Combine(_root, ".einzel"), "*.trajectory.vtu")
            .Single();

        var text = File.ReadAllText(vtu);

        var found = expected.Where(c => text.Contains(c, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"result: {string.Join(", ", expected.Order(StringComparer.Ordinal))}");
        output.WriteLine($"file:   {string.Join(", ", found.Order(StringComparer.Ordinal))}");

        Assert.Equal(expected.Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
    }

    /// <summary>Layer 6: the rendered figure, on the page and in its result.</summary>
    /// <remarks>
    /// <para>
    /// Both halves, because they fail independently. A figure whose <c>--json</c> lists a
    /// warning and whose page does not is the RND-11 failure exactly — the page is what
    /// gets shown, and it is shown detached from the JSON that qualified it.
    /// </para>
    /// <para>
    /// The visible <c>QUALIFIED</c> rule is asserted too: a validity warning must taint the
    /// drawing and not merely appear in its provenance block.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRenderedFigureCarriesIt()
    {
        var svg = Path.Combine(_root, "figures", "section.svg");

        Directory.CreateDirectory(Path.GetDirectoryName(svg)!);

        var (exitCode, stdout, _) = Cli("render", "section", Model(), "--out", svg, "--json");

        Assert.Equal(0, exitCode);

        using var document = JsonDocument.Parse(stdout);

        var codes = document.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()!).ToList();

        output.WriteLine($"figure result: {string.Join(", ", codes)}");

        Assert.Contains(Anchor, codes, StringComparer.Ordinal);

        var page = File.ReadAllText(svg);

        Assert.Contains(Anchor, page, StringComparison.Ordinal);
        Assert.Contains("QUALIFIED", page, StringComparison.Ordinal);
    }

    /// <summary>Layer 7: video — every frame, not only the first.</summary>
    /// <remarks>
    /// <para>
    /// RND-10's argument is that a frame is extracted, cropped and shown on its own, so a
    /// warning on the first frame alone is a warning on a frame nobody kept. Every frame
    /// is checked for that reason.
    /// </para>
    /// <para>
    /// The mapping is required rather than defaulted (RND-7), so the spec declares one —
    /// there is deliberately no command line that produces an animation without it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryAnimationFrameCarriesIt()
    {
        var model = Model();
        var spec = Path.Combine(_root, "figures", "film.json");

        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);

        File.WriteAllText(spec, """
        {
          "renderSpecVersion": "0.1",
          "model": "../models/gas.json",
          "widthMm": 120,
          "equipotentials": 0,
          "animation": {
            "framesPerSecond": 6,
            "phases": [
              { "until": { "value": 4000.0, "unit": "us" },
                "rate": { "value": 2000.0, "unit": "us/s" }, "label": "flight" }
            ]
          }
        }
        """);

        var directory = Path.Combine(_root, "figures", "film");

        var (exitCode, stdout, stderr) = Cli(
            "render", "animation", spec, "--out", directory, "--json");

        Assert.True(exitCode == 0, $"{stdout}\n{stderr}");

        var frames = Directory.EnumerateFiles(directory, "*.svg").Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(frames);

        output.WriteLine($"{frames.Count} frames");

        var without = frames
            .Where(f => !File.ReadAllText(f).Contains(Anchor, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            without.Count == 0,
            $"{without.Count} of {frames.Count} frames carry no '{Anchor}'. A frame is "
            + "extracted and shown on its own, so a warning missing from one is a warning "
            + "missing from whichever frame someone keeps");
    }
}
