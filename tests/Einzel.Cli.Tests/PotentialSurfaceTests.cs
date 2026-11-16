using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel render surface</c>: the potential on a plane drawn as height above it.
/// </summary>
/// <remarks>
/// Every expectation is a closed form the surface has no part in - a uniform field's linear
/// potential, an ideal quadrupole's saddle, an electrode's declared volts - or a pixel read out
/// of the file at a place the test projects for itself.
/// </remarks>
public sealed class PotentialSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-surface", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A uniform field draws a plane of exactly the right tilt.</summary>
    /// <remarks>
    /// 10 kV/m along x is phi = -10 V/mm times x, the same at every y: exact at the corners and
    /// linear at every node, and nothing at all along y. A surface whose axes were swapped would
    /// tilt the wrong way and fail here.
    /// </remarks>
    [Fact]
    public void AUniformFieldIsAPlaneOfTheRightTilt()
    {
        var model = Model("example", "accelerating-gap");

        var (surface, _, _) = SurfaceCommand.Sample(model, new SurfaceRequest(Plane: "xy", BoxMm: [-10, -5, 10, 5], Samples: 21));

        Assert.Equal("x", surface.AcrossAxis);
        Assert.Equal("y", surface.DeepAxis);

        Assert.Equal(100.0, surface.At(0, 0), 9);
        Assert.Equal(-100.0, surface.At(surface.Across.Count - 1, surface.Deep.Count - 1), 9);

        for (var i = 0; i < surface.Across.Count; i++)
        {
            for (var j = 0; j < surface.Deep.Count; j++)
            {
                Assert.Equal(-10.0 * surface.Across[i], surface.At(i, j), 9);
            }
        }

        Assert.Empty(surface.Plateaus);
    }

    /// <summary>An ideal quadrupole draws a saddle with the quadrupole's symmetries.</summary>
    /// <remarks>
    /// phi = V (x^2 - y^2) / r0^2 at the top of the cycle: even in x and in y, and odd under
    /// swapping them, so a hill along one axis is the same valley along the other.
    /// </remarks>
    [Fact]
    public void AnIdealQuadrupoleIsASaddleWithItsSymmetries()
    {
        var model = Model("example", "rf-quadrupole-confined");

        var (surface, _, _) = SurfaceCommand.Sample(
            model, new SurfaceRequest(Plane: "xy", BoxMm: [-2, -2, 2, 2], Samples: 41, AtUs: 0.0));

        var n = surface.Across.Count;
        Assert.Equal(n, surface.Deep.Count);

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                var (x, y) = (surface.Across[i], surface.Deep[j]);
                Assert.Equal(200.0 * ((x * x) - (y * y)) / 16.0, surface.At(i, j), 9);
                Assert.Equal(surface.At(i, j), surface.At(n - 1 - i, j), 9);
                Assert.Equal(surface.At(i, j), surface.At(i, n - 1 - j), 9);
                Assert.Equal(-surface.At(i, j), surface.At(j, i), 9);
            }
        }

        Assert.Equal(0.0, surface.At(n / 2, n / 2), 12);
    }

    /// <summary>A conductor is a flat plateau at its own potential.</summary>
    [Fact]
    public void AConductorIsAFlatPlateauAtItsPotential()
    {
        var model = Model("template", "einzel-lens");

        var (surface, _, _) = SurfaceCommand.Sample(
            model, new SurfaceRequest(BoxMm: [14, -10, 82, 10], Samples: 121));

        var centre = surface.Plateaus.Single(p => p.Conductor == "centre");
        Assert.Equal(500.0, centre.PotentialV, 9);
        Assert.Equal(0.0, surface.Plateaus.Single(p => p.Conductor == "entrance").PotentialV, 9);
        Assert.Equal(0.0, surface.Plateaus.Single(p => p.Conductor == "exit").PotentialV, 9);

        var inside = Enumerable.Range(0, surface.Values.Count).Where(k => surface.Inside[k] == "centre").ToList();
        Assert.NotEmpty(inside);
        Assert.All(inside, k => Assert.Equal(500.0, surface.Values[k], 9));

        // And free space is not flattened: the bore between the plateaus climbs toward them.
        var bore = Enumerable.Range(0, surface.Values.Count).Where(k => surface.Inside[k] is null).Select(k => surface.Values[k]).ToList();
        Assert.True(bore.Max() > 300.0 && bore.Max() < 500.0, $"bore peaks at {bore.Max()} V");
    }

    /// <summary>The potential energy is q phi: a negative ion's hills are a positive ion's valleys.</summary>
    [Fact]
    public void ThePotentialEnergyFlipsSignForANegativeIon()
    {
        var positive = Model("example", "accelerating-gap");
        var negative = Path.Combine(_root, "negative.json");
        File.WriteAllText(negative, File.ReadAllText(positive).Replace("\"chargeNumber\": 1", "\"chargeNumber\": -1", StringComparison.Ordinal));

        var request = new SurfaceRequest(Plane: "xy", BoxMm: [-10, -5, 10, 5], Samples: 11, Height: SurfaceHeight.Energy);

        var (volts, _, _) = SurfaceCommand.Sample(positive, request with { Height = SurfaceHeight.Potential });
        var (up, _, _) = SurfaceCommand.Sample(positive, request);
        var (down, _, _) = SurfaceCommand.Sample(negative, request);

        Assert.Equal("eV", down.Unit);
        Assert.Equal(-1.0, down.ChargeNumber!.Value, 9);
        Assert.Equal(1.0, up.ChargeNumber!.Value, 9);

        for (var k = 0; k < volts.Values.Count; k++)
        {
            Assert.Equal(volts.Values[k], up.Values[k], 9);
            Assert.Equal(-volts.Values[k], down.Values[k], 9);
        }
    }

    /// <summary>
    /// A driven field differs between instants, says so when no instant is given, and has a
    /// cycle mean that is its part that does not oscillate.
    /// </summary>
    [Fact]
    public void ADrivenFieldIsDrawnAtAStatedInstantOrAsItsCycleMean()
    {
        var model = Model("example", "rf-quadrupole-confined");
        var box = "-2,-2,2,2";

        using var top = Json("render", "surface", model, "--box-mm", box, "--samples", "21", "--at-us", "0", "--dry-run");
        using var half = Json("render", "surface", model, "--box-mm", box, "--samples", "21", "--at-us", "0.5", "--dry-run");

        // Half a period on, the rod pairs have swapped sign: the highest point becomes the lowest.
        Assert.Equal(50.0, Number(top, "highest"), 9);
        Assert.Equal(-Number(top, "highest"), Number(half, "lowest"), 9);
        Assert.Equal(0.5, Number(half, "instantUs"), 12);
        Assert.DoesNotContain("render.field-at-instant", Codes(top));

        using var unstated = Json("render", "surface", model, "--box-mm", box, "--samples", "21", "--dry-run");
        Assert.Contains("render.field-at-instant", Codes(unstated));
        Assert.Equal(0.0, Number(unstated, "instantUs"), 12);

        // A pure sinusoid's cycle mean is zero everywhere, and it needs no instant.
        var (mean, _, warnings) = SurfaceCommand.Sample(
            model, new SurfaceRequest(Plane: "xy", BoxMm: [-2, -2, 2, 2], Samples: 21, CycleMean: true));

        Assert.All(mean.Values, v => Assert.True(Math.Abs(v) < 1e-9, $"cycle mean {v} V"));
        Assert.Contains(warnings, w => w.Code == "render.cycle-mean");
        Assert.DoesNotContain(warnings, w => w.Code == "render.field-at-instant");
    }

    /// <summary>
    /// The cycle mean keeps the DC: a quadrupole carrying a DC offset beside its RF draws the
    /// DC's saddle and none of the RF's.
    /// </summary>
    [Fact]
    public void TheCycleMeanIsTheDcPartOfADrivenGeometry()
    {
        var template = Model("template", "quadrupole-rf");
        var text = File.ReadAllText(template);
        var document = JsonDocument.Parse(text);
        var dc = document.RootElement.GetProperty("parameters").GetProperty("dcPotential").GetProperty("value").GetDouble();

        // The template's own DC; a second copy with the DC at zero is the control.
        var withDc = Path.Combine(_root, "with-dc.json");
        File.WriteAllText(withDc, SetParameter(text, "dcPotential", 10.0));
        var withoutDc = Path.Combine(_root, "without-dc.json");
        File.WriteAllText(withoutDc, SetParameter(text, "dcPotential", 0.0));

        var request = new SurfaceRequest(Plane: "xy", BoxMm: [-3, -3, 3, 3], Samples: 31, CycleMean: true);
        var (mean, _, _) = SurfaceCommand.Sample(withDc, request);
        var (none, _, _) = SurfaceCommand.Sample(withoutDc, request);

        output.WriteLine($"template dc {dc} V; mean range {mean.Values.Min()} to {mean.Values.Max()} V");

        // With no DC the mean is zero to rounding; with 10 V it is a saddle of order 10 V.
        Assert.All(none.Values, v => Assert.True(Math.Abs(v) < 1e-6, $"{v}"));
        Assert.True(mean.Values.Max() > 1.0 && mean.Values.Min() < -1.0, "a DC saddle");
        Assert.All(mean.Values, v => Assert.True(Math.Abs(v) <= 10.0 + 1e-6, $"{v}"));
    }

    /// <summary>
    /// The picture has the surface where the test projects it, the plateaus in their own color,
    /// and its scale in the outcome and the file.
    /// </summary>
    [Fact]
    public void ThePictureShowsTheSurfaceAndCarriesItsScale()
    {
        var model = Model("template", "quadrupole");
        var png = Path.Combine(_root, "quad.png");

        var (code, stdout, stderr) = Cli(
            "render", "surface", model, "--view", "top", "--no-scale", "--width-px", "600", "--height-px", "500",
            "--out", png, "--json");
        output.WriteLine(stderr);

        Assert.Equal(0, code);

        using var outcome = JsonDocument.Parse(stdout);

        // The scale: the whole range, earth to each extreme, is drawn at the relief's fraction
        // of the longer side.
        var across = outcome.RootElement.GetProperty("acrossMm");
        var longer = across[1].GetDouble() - across[0].GetDouble();
        var drawn = Number(outcome, "heightMmPerUnit") * (Number(outcome, "highest") - Number(outcome, "lowest"));
        Assert.Equal(SurfaceCommand.DefaultRelief * longer, drawn, 9);
        Assert.Equal(1.0 / Number(outcome, "heightMmPerUnit"), Number(outcome, "unitsPerMm"), 12);
        Assert.Equal("V", outcome.RootElement.GetProperty("unit").GetString());

        var bytes = File.ReadAllBytes(png);
        Assert.Contains(Text(bytes), t => t.Keyword == "Scale" && t.Text.Contains("mm per V", StringComparison.Ordinal));

        // Where each place lands, through the same framing the picture was drawn with.
        var request = new SurfaceRequest();
        var (surface, _, _) = SurfaceCommand.Sample(model, request);
        var scale = SurfaceCommand.Scale(surface, request);
        var layers = SurfaceCommand.Compose(surface, scale, request.MeshLines);
        var points = layers.SelectMany(l => l.Meshes.SelectMany(m => Triples(m.VerticesMm)).Concat(l.Lines.SelectMany(p => Triples(p.PointsMm))));
        var matrix = Framing.Around(points, 0.0, -90.0).Matrix(600.0 / 500.0);

        var (width, height, rgb) = Decode(bytes);

        (int R, int G, int B) Seen(double x, double y, double z)
        {
            var px = (int)(((matrix[0] * x) + (matrix[4] * y) + (matrix[8] * z) + matrix[12] + 1.0) * 0.5 * width);
            var py = (int)((1.0 - ((matrix[1] * x) + (matrix[5] * y) + (matrix[9] * z) + matrix[13])) * 0.5 * height);
            var i = 3 * ((py * width) + px);
            return (rgb[i], rgb[i + 1], rgb[i + 2]);
        }

        foreach (var (rod, warm) in new[] { ("rodXPlus", true), ("rodYPlus", false) })
        {
            var nodes = Enumerable.Range(0, surface.Values.Count).Where(k => surface.Inside[k] == rod).ToList();
            var i = nodes.Select(k => k / surface.Deep.Count).Average();
            var j = nodes.Select(k => k % surface.Deep.Count).Average();
            var x = surface.Across[(int)Math.Round(i)];
            var z = surface.Deep[(int)Math.Round(j)];
            var level = surface.Plateaus.Single(p => p.Conductor == rod).Height * scale;

            var (r, g, b) = Seen(x, level, z);
            output.WriteLine($"{rod}: ({r}, {g}, {b})");

            Assert.True(r + g + b < 330, $"{rod}'s plateau is dark, got ({r}, {g}, {b})");
            Assert.True(warm ? r > b : b > r, $"{rod}'s plateau leans toward its potential's hue, got ({r}, {g}, {b})");

            // Halfway to the rod the surface itself is free space, tinted by the potential there:
            // warm on the way up a positive rod, cool down toward a negative one. The brightest
            // pixel round the point is read, so a mesh line through it does not decide.
            var (hx, hz) = (0.5 * x, 0.5 * z);
            var best = (R: 0, G: 0, B: 0);

            foreach (var dx in new[] { -0.3, 0.0, 0.3 })
            {
                foreach (var dz in new[] { -0.3, 0.0, 0.3 })
                {
                    var seen = Seen(hx + dx, 0.0, hz + dz);
                    best = seen.R + seen.G + seen.B > best.R + best.G + best.B ? seen : best;
                }
            }

            output.WriteLine($"halfway to {rod}: {best}");
            Assert.True(warm ? best.R - best.B > 15 : best.B - best.R > 15, $"the surface halfway to {rod} takes its potential's hue, got {best}");
        }

        // The middle of the saddle is free space: pale, not metal and not ground. A mesh line
        // runs through the exact middle, so the brightest pixel a little way round it is read.
        var brightest = (R: 0, G: 0, B: 0);

        foreach (var dx in new[] { -0.4, 0.0, 0.4 })
        {
            foreach (var dz in new[] { -0.4, 0.0, 0.4 })
            {
                var seen = Seen(dx, 0.0, dz);
                brightest = seen.R + seen.G + seen.B > brightest.R + brightest.G + brightest.B ? seen : brightest;
            }
        }

        var (cr, cg, cb) = brightest;
        output.WriteLine($"centre: ({cr}, {cg}, {cb})");
        Assert.True(cr + cg + cb > 450 && cr + cg + cb < 765, $"the saddle's middle is the pale surface, got ({cr}, {cg}, {cb})");
    }

    /// <summary>The scale is on the picture: a labelled bar is drawn left of the surface.</summary>
    [Fact]
    public void TheScaleIsDrawnOnThePictureUnlessAskedNotTo()
    {
        var model = Model("template", "quadrupole");
        var with = Path.Combine(_root, "with.png");
        var without = Path.Combine(_root, "without.png");

        Assert.Equal(0, Cli("render", "surface", model, "--width-px", "600", "--height-px", "400", "--out", with).ExitCode);
        Assert.Equal(0, Cli("render", "surface", model, "--width-px", "600", "--height-px", "400", "--out", without, "--no-scale").ExitCode);

        static int Ink(string png)
        {
            var (w, h, rgb) = Decode(File.ReadAllBytes(png));
            var count = 0;

            // The scale's ink: a dark neutral gray, which neither ramp's colors nor the quadrupole's
            // warm and cool plateaus are.
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = 3 * ((y * w) + x);
                    var (r, g, b) = (rgb[i], rgb[i + 1], rgb[i + 2]);
                    count += r < 80 && g < 80 && b < 80 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 10 ? 1 : 0;
                }
            }

            return count;
        }

        var inkWith = Ink(with);
        var inkWithout = Ink(without);
        output.WriteLine($"scale-ink pixels: {inkWith} with the scale, {inkWithout} without");
        Assert.True(inkWith > 200 && inkWithout < inkWith / 4, $"{inkWith} against {inkWithout}");
    }

    /// <summary>Malformed requests are refused as validation failures, not crashes.</summary>
    [Theory]
    [InlineData("--plane", "xx")]
    [InlineData("--box-mm", "1,2,3")]
    [InlineData("--samples", "2")]
    [InlineData("--height", "kinetic")]
    [InlineData("--box", "1,2,3,4")]
    [InlineData("--width-px", "1600px")]
    [InlineData("--at-us", "soon")]
    public void AMalformedRequestIsRefused(string flag, string value)
    {
        var model = Model("template", "quadrupole");
        var (code, _, stderr) = Cli("render", "surface", model, flag, value, "--dry-run");
        output.WriteLine(stderr);
        Assert.Equal(1, code);
    }

    /// <summary>A cycle mean of a field with no drive cycle is refused, with what to do instead.</summary>
    /// <param name="kind">Example or template.</param>
    /// <param name="name">The model.</param>
    /// <remarks>
    /// A sequence whose states are all static varies with time and has no drive period: it used
    /// to dereference the absent window and exit 6, "a defect in einzel", after sampling the
    /// instantaneous field under the mean's name. A static model accepted the flag and ignored
    /// it. Neither has a cycle, so neither has a cycle mean.
    /// </remarks>
    [Theory]
    [InlineData("example", "sequenced-extraction")]
    [InlineData("example", "sequenced-uniform")]
    [InlineData("example", "slit-transmission")]
    public void ACycleMeanOfAFieldWithNoDriveIsRefused(string kind, string name)
    {
        var model = Model(kind, name);

        var (code, stdout, stderr) = Cli(
            "render", "surface", model, "--cycle-mean", "--box-mm", "-5,-5,5,5", "--samples", "11", "--dry-run");

        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains("/surface/cycle-mean", stderr, StringComparison.Ordinal);
        Assert.Contains("drop --cycle-mean", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("INTERNAL_ERROR", stdout + stderr, StringComparison.Ordinal);

        // And the same field drawn at an instant is fine: the control.
        Assert.Equal(0, Cli("render", "surface", model, "--box-mm", "-5,-5,5,5", "--samples", "11", "--dry-run").ExitCode);

        // The probe itself does not hand back an instant under a mean's name either.
        var probe = FieldProbe.Open(model);
        Assert.Null(probe.CycleMeanWindowSeconds);
        Assert.Throws<InvalidOperationException>(
            () => probe.At(0, new Einzel.Core.Geometry.Vec3(0, 0, 0), 0.0, cycleMean: true));
    }

    /// <summary>A driven field's cycle mean still draws, with its window reported.</summary>
    [Fact]
    public void ADrivenFieldStillHasACycleMean()
    {
        using var json = Json(
            "render", "surface", Model("template", "quadrupole-rf"), "--cycle-mean", "--samples", "21", "--dry-run");

        Assert.True(json.RootElement.GetProperty("cycleMean").GetBoolean());
        Assert.True(Number(json, "cycleMeanWindowUs") > 0.0);
    }

    /// <summary>An instant given for a static field is said to have changed nothing.</summary>
    [Fact]
    public void AnInstantOnAStaticFieldIsWarnedAbout()
    {
        var model = Model("example", "slit-transmission");

        using var with = Json("render", "surface", model, "--at-us", "3", "--samples", "21", "--dry-run");
        using var without = Json("render", "surface", model, "--samples", "21", "--dry-run");

        Assert.Contains("render.instant-ignored", Codes(with));
        Assert.DoesNotContain("render.instant-ignored", Codes(without));
        Assert.False(with.RootElement.TryGetProperty("instantUs", out var instant) && instant.ValueKind == JsonValueKind.Number);
    }

    /// <summary>The hash recorded is of the text the field was built from: one read (PRJ-3).</summary>
    /// <remarks>
    /// The file on disk holds one revision and the command is handed another, which is what an
    /// edit landing between two reads amounts to. With a second read the hash would name the
    /// disk's revision beside a surface of the other's.
    /// </remarks>
    [Fact]
    public void TheHashIsOfTheTextTheFieldWasBuiltFrom()
    {
        var path = Model("example", "slit-transmission");
        var onDisk = File.ReadAllText(path);

        var node = System.Text.Json.Nodes.JsonNode.Parse(onDisk)!;
        node["fields"]![0]!["solve"]!["electrodes"]![0]!["potential"]!["value"] = 75.0;
        var handed = node.ToJsonString();

        var outcome = SurfaceCommand.Execute(
            path,
            new Einzel.Project.ProjectLayout(_root),
            new SurfaceRequest(Samples: 81),
            width: 200,
            height: 150,
            dryRun: true,
            modelText: handed);

        Assert.Equal(Einzel.Project.ContentHash.OfText(handed), outcome.ModelHash);
        Assert.NotEqual(Einzel.Project.ContentHash.OfText(onDisk), outcome.ModelHash);
        Assert.Equal(75.0, outcome.Plateaus.Single(p => p.Conductor == "slitUpper").PotentialV, 9);

        // And with no text handed over, the one read is the file's.
        var read = SurfaceCommand.Execute(
            path, new Einzel.Project.ProjectLayout(_root), new SurfaceRequest(Samples: 81), width: 200, height: 150, dryRun: true);

        Assert.Equal(Einzel.Project.ContentHash.OfText(onDisk), read.ModelHash);
    }

    /// <summary>Seen from straight above, the vertical bar is a point: it is left out and reported absent.</summary>
    [Fact]
    public void TheTopViewHasNoVerticalScale()
    {
        var model = Model("example", "slit-transmission");
        var top = Path.Combine(_root, "top.png");
        var bare = Path.Combine(_root, "top-bare.png");

        using var json = Json("render", "surface", model, "--view", "top", "--samples", "81", "--out", top);
        using var side = Json("render", "surface", model, "--view", "side", "--samples", "81", "--dry-run");
        using var iso = Json("render", "surface", model, "--samples", "81", "--dry-run");

        Assert.False(json.RootElement.GetProperty("scaleDrawn").GetBoolean());
        Assert.True(side.RootElement.GetProperty("scaleDrawn").GetBoolean());
        Assert.True(iso.RootElement.GetProperty("scaleDrawn").GetBoolean());

        // The scale itself is still in the outcome and in the file.
        Assert.True(Number(json, "heightMmPerUnit") > 0.0);
        Assert.Contains(Text(File.ReadAllBytes(top)), t => t.Keyword == "Scale");

        // And the picture is the one --no-scale draws: nothing of the bar, and no room left for it.
        Assert.Equal(0, Cli("render", "surface", model, "--view", "top", "--samples", "81", "--no-scale", "--out", bare).ExitCode);
        Assert.Equal(Decode(File.ReadAllBytes(bare)).Rgb, Decode(File.ReadAllBytes(top)).Rgb);
    }

    /// <summary>
    /// A picture too small for the scale's labels is drawn without the scale and says so; one
    /// with room keeps every label inside the picture.
    /// </summary>
    /// <remarks>
    /// The einzel lens's labels are "500 V" and "-3 V". Room for them used to be reserved once,
    /// in millimetres at the first framing's scale, and the refit that made the room shrank the
    /// scale under them.
    /// </remarks>
    [Fact]
    public void ASmallPictureKeepsItsLabelsInsideOrLeavesTheScaleOut()
    {
        var model = Model("example", "einzel-lens");
        var small = Path.Combine(_root, "small.png");
        var bare = Path.Combine(_root, "small-bare.png");

        using var json = Json("render", "surface", model, "--width-px", "200", "--height-px", "150", "--samples", "61", "--out", small);
        Assert.Equal(0, Cli("render", "surface", model, "--width-px", "200", "--height-px", "150", "--samples", "61", "--no-scale", "--out", bare).ExitCode);

        Assert.False(json.RootElement.GetProperty("scaleDrawn").GetBoolean());
        Assert.Equal(Decode(File.ReadAllBytes(bare)).Rgb, Decode(File.ReadAllBytes(small)).Rgb);

        foreach (var (w, h) in new[] { (60, 60), (120, 400), (320, 200), (400, 120), (600, 400) })
        {
            var with = Path.Combine(_root, $"fit-{w}x{h}.png");
            var without = Path.Combine(_root, $"fit-{w}x{h}-bare.png");
            var size = new[] { "--width-px", $"{w}", "--height-px", $"{h}", "--samples", "61" };

            using var fitted = Json(["render", "surface", model, .. size, "--out", with]);
            Assert.Equal(0, Cli(["render", "surface", model, .. size, "--no-scale", "--out", without]).ExitCode);

            var drawn = fitted.RootElement.GetProperty("scaleDrawn").GetBoolean();
            var (_, _, rgb) = Decode(File.ReadAllBytes(with));
            var (_, _, plain) = Decode(File.ReadAllBytes(without));

            output.WriteLine($"{w} x {h}: scale {(drawn ? "drawn" : "left out")}");

            if (!drawn)
            {
                Assert.Equal(plain, rgb);
                continue;
            }

            // Drawn: nothing but ground on the outermost pixels, so no label runs off an edge.
            var ground = (rgb[0], rgb[1], rgb[2]);

            for (var x = 0; x < w; x++)
            {
                foreach (var y in new[] { 0, h - 1 })
                {
                    var i = 3 * ((y * w) + x);
                    Assert.Equal(ground, (rgb[i], rgb[i + 1], rgb[i + 2]));
                }
            }

            for (var y = 0; y < h; y++)
            {
                foreach (var x in new[] { 0, w - 1 })
                {
                    var i = 3 * ((y * w) + x);
                    Assert.Equal(ground, (rgb[i], rgb[i + 1], rgb[i + 2]));
                }
            }
        }

        // The default size has room, and says so.
        using var roomy = Json("render", "surface", model, "--width-px", "600", "--height-px", "400", "--samples", "61", "--dry-run");
        Assert.True(roomy.RootElement.GetProperty("scaleDrawn").GetBoolean());
    }

    /// <summary>
    /// Two elements that each have a conductor of one name are two plateaus, each at its own
    /// potential, labelled by element.
    /// </summary>
    /// <remarks>
    /// A name is unique within one solve and no further. Keyed by name alone, the two were one
    /// plateau at one median - a conductor drawn at a potential it does not hold.
    /// </remarks>
    [Fact]
    public void SameNamedConductorsInTwoElementsAreTwoPlateaus()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();

        foreach (var electrode in fields[0]!["solve"]!["electrodes"]!.AsArray())
        {
            electrode!["potential"]!["value"] = 40.0;
        }

        // The same slit again, 40 mm downstream in a domain of its own, at another potential.
        var second = fields[0]!.DeepClone();
        second["solve"]!["minX"]!["value"] = 20;
        second["solve"]!["maxX"]!["value"] = 60;

        foreach (var electrode in second["solve"]!["electrodes"]!.AsArray())
        {
            electrode!["minX"]!["value"] = 40;
            electrode["maxX"]!["value"] = 41;
            electrode["potential"]!["value"] = -30.0;
        }

        fields.Add(second);

        var path = Path.Combine(_root, "two-slits.json");
        File.WriteAllText(path, node.ToJsonString());

        var (surface, _, _) = SurfaceCommand.Sample(
            path, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 60, 10], Samples: 161));

        foreach (var plateau in surface.Plateaus)
        {
            output.WriteLine($"{plateau.Conductor}: {plateau.PotentialV} V, element {plateau.Element}, {plateau.Samples} samples");
        }

        Assert.Equal(4, surface.Plateaus.Count);

        foreach (var name in new[] { "slitUpper", "slitLower" })
        {
            var first = surface.Plateaus.Single(p => p.Conductor == $"{name} (element 0)");
            var other = surface.Plateaus.Single(p => p.Conductor == $"{name} (element 1)");

            Assert.Equal(40.0, first.PotentialV, 6);
            Assert.Equal(-30.0, other.PotentialV, 6);
            Assert.Equal((0, name), (first.Element, first.Name));
            Assert.Equal((1, name), (other.Element, other.Name));
        }

        // A name that does not repeat keeps its plain label.
        var (alone, _, _) = SurfaceCommand.Sample(
            Model("example", "slit-transmission"), new SurfaceRequest(Samples: 81));

        Assert.Equal(["slitLower", "slitUpper"], alone.Plateaus.Select(p => p.Conductor));
        Assert.All(alone.Plateaus, p => Assert.Equal(0, p.Element));
    }

    /// <summary>
    /// With no offset given, a plane is drawn through the middle of the domain along its normal;
    /// a 2-D model's own plane is the one its solve is declared in, z = 0.
    /// </summary>
    /// <remarks>
    /// The mirror pair's domain runs from -90 mm to well past 700 mm in x, so the middle along x
    /// is nowhere near zero: the default used to be 0 for every plane of a 2-D model, which for
    /// yz is a cut 90 mm inside one end rather than through the middle.
    /// </remarks>
    [Fact]
    public void TheDefaultOffsetIsTheDomainsMiddleAlongTheNormal()
    {
        var model = Model("template", "planar-mirror-pair");
        var domain = FieldProbe.Open(model).DomainMm()!.Value;
        var middle = (domain.Min.X + domain.Max.X) / 2.0;

        var (yz, _, _) = SurfaceCommand.Sample(model, new SurfaceRequest(Plane: "yz", Samples: 5));
        var (xy, _, _) = SurfaceCommand.Sample(model, new SurfaceRequest(Samples: 5));
        var (asked, _, _) = SurfaceCommand.Sample(model, new SurfaceRequest(Plane: "yz", OffsetMm: 12.5, Samples: 5));

        output.WriteLine($"x from {domain.Min.X} to {domain.Max.X} mm; yz drawn at {yz.OffsetMm} mm");

        Assert.True(Math.Abs(middle) > 100.0, "the control: the middle is far from zero");
        Assert.Equal(middle, yz.OffsetMm, 9);
        Assert.Equal("xy", xy.Plane);
        Assert.Equal(0.0, xy.OffsetMm);
        Assert.Equal(12.5, asked.OffsetMm);
    }

    /// <summary>The CSV quotes a name per RFC 4180 and keeps a multi-line warning in comments.</summary>
    [Fact]
    public void TheCsvQuotesNamesAndKeepsWarningsInComments()
    {
        var surface = new PotentialSurface
        {
            Plane = "xy",
            OffsetMm = 0.0,
            AcrossAxis = "x",
            DeepAxis = "y",
            Across = [0.0, 1.0],
            Deep = [0.0, 1.0],
            Values = [1.0, 2.0, 3.0, 4.0],
            Inside = [null, "plate, \"upper\"", "plain", "two\nlines"],
            Plateaus = [],
            Height = SurfaceHeight.Potential,
            Unit = "V",
            OutsideDomain = 0,
            Refused = 0,
        };

        var csv = SurfaceCommand.ToCsv(
            surface,
            [new Einzel.Core.Results.ValidityWarning("a.code", "first line\r\nsecond, line\nthird", Einzel.Core.Results.WarningSeverity.Qualified)]);

        output.WriteLine(csv);

        var lines = csv.Split('\n');

        Assert.Equal("# Qualified: a.code: first line", lines[1]);
        Assert.Equal("# second, line", lines[2]);
        Assert.Equal("# third", lines[3]);
        Assert.Equal("x_mm,y_mm,potential_V,conductor", lines[4]);
        Assert.Equal("0,0,1,", lines[5]);
        Assert.Equal("0,1,2,\"plate, \"\"upper\"\"\"", lines[6]);
        Assert.Equal("1,0,3,plain", lines[7]);
        Assert.Equal("1,1,4,\"two", lines[8]);
        Assert.Equal("lines\"", lines[9]);
    }

    private static IEnumerable<(double X, double Y, double Z)> Triples(IReadOnlyList<double> flat)
    {
        for (var i = 0; i + 2 < flat.Count; i += 3)
        {
            yield return (flat[i], flat[i + 1], flat[i + 2]);
        }
    }

    private static string SetParameter(string json, string name, double value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["parameters"]![name]!["value"] = value;
        return node.ToJsonString();
    }

    private static double Number(JsonDocument document, string name) =>
        document.RootElement.GetProperty(name).GetDouble();

    private static string[] Codes(JsonDocument document) =>
        [.. document.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()!)];

    private static JsonDocument Json(params string[] args)
    {
        var (code, stdout, stderr) = Cli([.. args, "--json"]);
        Assert.True(code == 0, stderr);
        return JsonDocument.Parse(stdout);
    }

    private string Model(string kind, string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{name}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, kind == "template" ? "--from-template" : "--from-example", name).ExitCode);
        }

        return path;
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();

        for (var at = 8; at + 8 <= png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            chunks.Add((type, png.AsSpan(at + 8, length).ToArray()));
            at += 12 + length;
        }

        return chunks;
    }

    private static (int Width, int Height, byte[] Rgb) Decode(byte[] png)
    {
        var chunks = Chunks(png);
        var header = chunks.Single(c => c.Type == "IHDR").Data;
        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));

        using var inflated = new MemoryStream();

        using (var zlib = new ZLibStream(
            new MemoryStream([.. chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data)]),
            CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        var stride = 3 * width;
        var rgb = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            Array.Copy(raw, (y * (stride + 1)) + 1, rgb, y * stride, stride);
        }

        return (width, height, rgb);
    }

    private static List<(string Keyword, string Text)> Text(byte[] png) =>
        [.. Chunks(png).Where(c => c.Type == "iTXt").Select(c =>
        {
            var end = Array.IndexOf(c.Data, (byte)0);
            return (Encoding.ASCII.GetString(c.Data, 0, end),
                Encoding.UTF8.GetString(c.Data, end + 5, c.Data.Length - end - 5));
        })];

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
}
