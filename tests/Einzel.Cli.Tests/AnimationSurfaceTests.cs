using System.Globalization;
using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel render animation</c>, driven through the command surface.
/// </summary>
/// <remarks>
/// <para>
/// RND-7: "an animation declares an explicit non-linear time mapping — playback rate
/// per sequence phase — and the current rate is displayed on screen throughout
/// playback. Neither part is optional."
/// </para>
/// <para>
/// The interface is shaped by that. An animation is asked for through a render spec
/// and there is no <c>--rate</c> flag, so there is no command line that produces one
/// without a declared mapping — the requirement enforces itself rather than being
/// checked.
/// </para>
/// </remarks>
public sealed class AnimationSurfaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-anim", Guid.NewGuid().ToString("N"));

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

    private string Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        return _root;
    }

    /// <summary>Writes a render spec beside the scaffolded reflectron.</summary>
    private string WriteSpec(string name, string animation)
    {
        var path = Path.Combine(_root, "figures", name + ".json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(path, $$"""
        {
          "renderSpecVersion": "0.1",
          "model": "../models/reflectron.json",
          "widthMm": 120,
          "equipotentials": 4
          {{animation}}
        }
        """);

        return path;
    }

    /// <summary>The whole path: a spec in, numbered frames and a manifest out.</summary>
    /// <remarks>
    /// The mapping here is the case the requirement exists for. A single-stage
    /// reflectron drifts for four microseconds, turns round in two, and drifts back —
    /// and the turn-around is the part the instrument is designed around. At one rate it
    /// is a fifth of the film; at these it is most of it, and every frame says which.
    /// </remarks>
    [Fact]
    public void ASpecWithADeclaredMappingProducesFramesAndAManifest()
    {
        Project();

        var spec = WriteSpec("reflectron.anim", """
        ,
          "animation": {
            "framesPerSecond": 10,
            "phases": [
              { "until": { "value": 4.0, "unit": "us" },
                "rate": { "value": 4.0, "unit": "us/s" }, "label": "inbound" },
              { "until": { "value": 6.2, "unit": "us" },
                "rate": { "value": 0.5, "unit": "us/s" }, "label": "turn-around" },
              { "until": { "value": 10.1805, "unit": "us" },
                "rate": { "value": 4.0, "unit": "us/s" }, "label": "outbound" }
            ]
          }
        """);

        var (exitCode, stdout, stderr) = Run("render", "animation", spec, "--json");

        Assert.True(exitCode == 0, stdout + stderr);

        var outcome = JsonDocument.Parse(stdout).RootElement;

        Assert.Equal("animation", outcome.GetProperty("kind").GetString());

        var frames = outcome.GetProperty("frames").GetInt32();

        // Playback: 1.0 s inbound, 4.4 s through the turn, 0.995125 s outbound.
        Assert.Equal(6.395125, outcome.GetProperty("playbackSeconds").GetDouble(), 1e-9);
        Assert.Equal(65, frames);

        var directory = Path.Combine(_root, "figures", "reflectron.animation");

        Assert.Equal(frames, Directory.GetFiles(directory, "frame-*.svg").Length);

        var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, "frames.json"))).RootElement;

        var listed = manifest.GetProperty("frames").EnumerateArray().ToList();

        Assert.Equal(frames, listed.Count);

        // The boundary frames land exactly on the declared instants, and announce the
        // rate that is about to apply rather than the one that has just stopped.
        var atBoundary = listed.Single(f => f.GetProperty("index").GetInt32() == 10);

        Assert.Equal(4.0e-6, atBoundary.GetProperty("simulatedSeconds").GetDouble(), 1e-18);
        Assert.Equal("turn-around", atBoundary.GetProperty("phase").GetString());

        // And the last frame is the arrival, not the last point of a frame grid that
        // stopped short of it.
        Assert.Equal(
            10.1805e-6, listed[^1].GetProperty("simulatedSeconds").GetDouble(), 1e-16);

        // Most of the film is the turn-around, which is a fifth of the flight. That is
        // the whole reason the mapping is declared rather than uniform.
        var slow = listed.Count(f => f.GetProperty("phase").GetString() == "turn-around");

        Assert.True(slow > frames / 2, $"only {slow} of {frames} frames are the turn-around");
    }

    /// <summary>Every frame on disk carries the rate, in the two readings.</summary>
    /// <remarks>
    /// The half of RND-7 that survives the file being copied somewhere else. Checked on
    /// the written SVG rather than on the scene, because that is the artifact that gets
    /// shown.
    /// </remarks>
    [Fact]
    public void EveryWrittenFrameCarriesTheRate()
    {
        Project();

        var spec = WriteSpec("stamped", """
        ,
          "animation": {
            "framesPerSecond": 6,
            "phases": [
              { "until": { "value": 10.1805, "unit": "us" },
                "rate": { "value": 2.0, "unit": "us/s" } }
            ]
          }
        """);

        Assert.Equal(0, Run("render", "animation", spec).ExitCode);

        var files = Directory.GetFiles(
            Path.Combine(_root, "figures", "reflectron.animation"), "frame-*.svg");

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            Assert.Contains("2 µs of flight per second of playback", text, StringComparison.Ordinal);
            Assert.Contains("500,000x slower than real time", text, StringComparison.Ordinal);
        }
    }

    /// <summary>A diffusive model is animated as a moving density.</summary>
    /// <remarks>
    /// <para>
    /// RND-8 forbids drawing lines through a diffusive region, so for a long time an
    /// animation of one was refused outright: a run reported the density it <em>ended</em>
    /// with, and the frames would all have been the same box. With the density
    /// recordable at chosen instants the frames have something that moves, and the
    /// refusal narrows to the case it was actually about.
    /// </para>
    /// <para>
    /// What is asserted is the physics: the packet drifts down the tube, it spreads as it
    /// goes, and the contour levels stay put while it does.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusiveModelIsAnimatedAsAMovingDensity()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "drift-tube-diffusion.json");

        Assert.Equal(
            0, Run("new", model, "--from-example", "drift-tube-diffusion").ExitCode);

        var spec = Path.Combine(_root, "figures", "dt.anim.json");

        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);

        File.WriteAllText(spec, """
        {
          "renderSpecVersion": "0.1",
          "model": "../models/drift-tube-diffusion.json",
          "widthMm": 140,
          "equipotentials": 0,
          "densityContours": 6,
          "animation": {
            "framesPerSecond": 8,
            "phases": [
              { "until": { "value": 200.0, "unit": "us" },
                "rate": { "value": 50.0, "unit": "us/s" }, "label": "drifting" }
            ]
          }
        }
        """);

        var (exitCode, stdout, stderr) = Run("render", "animation", spec, "--json");

        Assert.True(exitCode == 0, stdout + stderr);

        var files = Directory
            .GetFiles(Path.Combine(_root, "figures", "drift-tube-diffusion.animation"), "frame-*.svg")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(33, files.Count);

        static (int Contours, double Centre, double Width, string Levels) Density(string file)
        {
            var text = File.ReadAllText(file);

            var group = System.Text.RegularExpressions.Regex.Match(
                text,
                "<g id=\"density\">(.*?)</g>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            var levels = System.Text.RegularExpressions.Regex.Match(
                text, @"density contours, ions per cubic metre: ([^\r\n]*)");

            if (!group.Success)
            {
                return (0, 0.0, 0.0, levels.Success ? levels.Groups[1].Value : string.Empty);
            }

            var xs = System.Text.RegularExpressions.Regex
                .Matches(group.Groups[1].Value, @"[ML]\s*([\d.eE+-]+)")
                .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                .ToList();

            return (
                System.Text.RegularExpressions.Regex.Count(group.Groups[1].Value, "<path"),
                (xs.Min() + xs.Max()) / 2.0,
                xs.Max() - xs.Min(),
                levels.Success ? levels.Groups[1].Value : string.Empty);
        }

        var first = Density(files[0]);
        var middle = Density(files[24]);

        Assert.True(first.Contours > 0, "the first frame drew no density");
        Assert.True(middle.Contours > 0, "the packet had vanished by three quarters through");

        // It drifts down the tube...
        Assert.True(
            middle.Centre - first.Centre > 40.0,
            $"the packet moved only {middle.Centre - first.Centre:F1} mm on the page");

        // ...and it spreads as it goes, which is the other half of what the mode
        // computes and the half a trajectory cannot show at all.
        Assert.True(
            middle.Width > 1.5 * first.Width,
            $"the packet went from {first.Width:F1} to {middle.Width:F1} mm without spreading");

        // And the levels are the same on every frame. Anchored per frame they would
        // track the falling peak, the contours would stay the same size, and a film of a
        // packet spreading would show a packet doing nothing.
        Assert.Equal(first.Levels, middle.Levels);
        Assert.NotEqual(string.Empty, first.Levels);

        foreach (var file in files)
        {
            Assert.Equal(first.Levels, Density(file).Levels);
        }
    }

    /// <summary>A mapping the run cannot reach is refused, not padded.</summary>
    /// <remarks>
    /// Repeating the last density for the frames past the end would show a packet
    /// sitting still, which is what a finished run looks like and is not what it is.
    /// </remarks>
    [Fact]
    public void ADiffusiveMappingPastTheRunIsRefused()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var model = Path.Combine(_root, "models", "drift-tube-diffusion.json");

        Assert.Equal(
            0, Run("new", model, "--from-example", "drift-tube-diffusion").ExitCode);

        var spec = Path.Combine(_root, "figures", "toolong.json");

        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);

        // The example caps its flight at 1500 us.
        File.WriteAllText(spec, """
        {
          "renderSpecVersion": "0.1",
          "model": "../models/drift-tube-diffusion.json",
          "densityContours": 4,
          "animation": {
            "framesPerSecond": 4,
            "phases": [
              { "until": { "value": 5000.0, "unit": "us" },
                "rate": { "value": 2000.0, "unit": "us/s" } }
            ]
          }
        }
        """);

        var (exitCode, stdout, stderr) = Run("render", "animation", spec, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("have no density", stdout + stderr, StringComparison.Ordinal);
    }

    /// <summary>A spec with no mapping is refused, and says why.</summary>
    [Fact]
    public void ASpecWithNoMappingIsRefused()
    {
        Project();

        var spec = WriteSpec("no-mapping", "");

        var (exitCode, stdout, stderr) = Run("render", "animation", spec, "--json");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("RND-7", stdout + stderr, StringComparison.Ordinal);
    }

    /// <summary>A bare model is refused, because it cannot carry a mapping.</summary>
    /// <remarks>
    /// The interface doing the enforcing. <c>render section</c> takes either a model or
    /// a spec; an animation takes only a spec, because a model has nowhere to declare
    /// how time is compressed and there is no flag that supplies one.
    /// </remarks>
    [Fact]
    public void ABareModelIsRefused()
    {
        Project();

        var (exitCode, stdout, stderr) = Run(
            "render", "animation", Path.Combine(_root, "models", "reflectron.json"));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("render spec", stdout + stderr, StringComparison.Ordinal);
    }

    /// <summary>--dry-run writes nothing (CLI-4).</summary>
    [Fact]
    public void ADryRunWritesNothing()
    {
        Project();

        var spec = WriteSpec("dry", """
        ,
          "animation": {
            "framesPerSecond": 4,
            "phases": [
              { "until": { "value": 10.0, "unit": "us" },
                "rate": { "value": 5.0, "unit": "us/s" } }
            ]
          }
        """);

        var (exitCode, stdout, _) = Run("render", "animation", spec, "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("would write", stdout, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "figures", "reflectron.animation")));
    }
}
