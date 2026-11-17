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
    /// Under <c>--cycle-mean</c> no phase is drawn, so an instant too late to place in the cycle
    /// is not refused: it picks the operating point, and the mean is the same as at any other.
    /// </summary>
    /// <remarks>
    /// The resolution check asks whether a double can say where in the drive cycle an instant
    /// falls - the right question for a picture of one phase, and none at all for the mean over
    /// all of them. It refused <c>--at-us 1e300 --cycle-mean</c>, naming a phase nothing drew.
    /// </remarks>
    [Fact]
    public void ALateInstantIsNotRefusedForACycleMean()
    {
        var text = File.ReadAllText(Model("template", "quadrupole-rf"));
        var model = Path.Combine(_root, "late-mean.json");
        File.WriteAllText(model, SetParameter(text, "dcPotential", 10.0));

        var request = new SurfaceRequest(Plane: "xy", BoxMm: [-3, -3, 3, 3], Samples: 21, CycleMean: true);
        var (early, _, _) = SurfaceCommand.Sample(model, request with { AtUs = 0.0 });
        var (late, _, _) = SurfaceCommand.Sample(model, request with { AtUs = 1e300 });

        Assert.Equal(early.Values, late.Values);
        Assert.True(early.Values.Max() > 1.0, "the DC saddle is there to compare");

        Assert.Equal(0, Cli("render", "surface", model, "--at-us", "1e300", "--cycle-mean", "--samples", "21", "--dry-run").ExitCode);

        // The instant drawn as itself is still refused: there the phase is the picture.
        Assert.Equal(1, Cli("render", "surface", model, "--at-us", "1e300", "--samples", "21", "--dry-run").ExitCode);

        // An analytic drive, whose mean is sampled across the window rather than taken in closed
        // form: a window opened at 1e300 us would sample one rounded phase sixty-four times and
        // draw the instant under the mean's name. Opened where the cycle can be resolved, it is
        // the pure sinusoid's mean - zero - as at t = 0.
        var analytic = Model("example", "rf-quadrupole-confined");
        var box = request with { BoxMm = [-1, -1, 1, 1] };
        var (atZero, _, _) = SurfaceCommand.Sample(analytic, box with { AtUs = 0.0 });
        var (atLate, _, _) = SurfaceCommand.Sample(analytic, box with { AtUs = 1e300 });

        Assert.All(atZero.Values, v => Assert.True(Math.Abs(v) < 1e-9, $"{v}"));
        Assert.Equal(atZero.Values, atLate.Values);
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
        Assert.Contains("/args/--cycle-mean", stderr, StringComparison.Ordinal);
        Assert.Contains("drop --cycle-mean", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("INTERNAL_ERROR", stdout + stderr, StringComparison.Ordinal);

        // And the same field drawn at an instant is fine: the control.
        Assert.Equal(0, Cli("render", "surface", model, "--box-mm", "-5,-5,5,5", "--samples", "11", "--dry-run").ExitCode);

        // The probe itself does not hand back an instant under a mean's name either.
        var probe = FieldProbe.Open(model);
        Assert.Null(probe.CycleMeanWindowSecondsAt(0.0));
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

    /// <summary>
    /// A conductor may be named anything, and its name goes in a PNG text chunk's text, never in
    /// its keyword.
    /// </summary>
    /// <remarks>
    /// A PNG keyword is at most 79 characters of printable Latin-1. The name used to be the
    /// keyword ("Plateau " + name), so "électrode", "plate µ1" or a long name made a valid model
    /// an internal error, exit 6, with no picture and no CSV.
    /// </remarks>
    [Fact]
    public void AConductorNamedAnythingStillDraws()
    {
        var accented = "électrode µ1, \"upper\"";
        var lengthy = "a name much longer than the seventy-nine characters a PNG keyword is allowed to be, as a sentence is";

        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var electrodes = node["fields"]![0]!["solve"]!["electrodes"]!;
        electrodes[0]!["name"] = accented;
        electrodes[1]!["name"] = lengthy;

        var path = Path.Combine(_root, "names.json");
        var png = Path.Combine(_root, "names.png");
        var csv = Path.Combine(_root, "names.csv");
        File.WriteAllText(path, node.ToJsonString());

        var (code, stdout, stderr) = Cli("render", "surface", path, "--samples", "81", "--out", png, "--csv", csv);
        output.WriteLine(stderr);

        Assert.Equal(0, code);
        Assert.True(File.Exists(png) && File.Exists(csv), stdout);

        var text = Text(File.ReadAllBytes(png));

        Assert.All(text, t => Assert.True(t.Keyword.Length <= 79 && t.Keyword.All(c => c is >= ' ' and <= '~'), t.Keyword));
        Assert.Contains(text, t => t.Keyword.StartsWith("Plateau ", StringComparison.Ordinal) && t.Text.StartsWith(accented + ":", StringComparison.Ordinal));
        Assert.Contains(text, t => t.Keyword.StartsWith("Plateau ", StringComparison.Ordinal) && t.Text.StartsWith(lengthy + ":", StringComparison.Ordinal));
    }

    /// <summary>
    /// A plateau's element is its index in the model, whatever the model's other elements are.
    /// </summary>
    /// <remarks>
    /// A field-free element adds nothing to the assembled sum, and neither does a magnet, so a
    /// member's position in the sum is not its element's index: with a field-free element first,
    /// every conductor was attributed to the element before its own.
    /// </remarks>
    [Fact]
    public void APlateausElementCountsTheElementsThatAddNothing()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();

        var second = fields[0]!.DeepClone();
        second["solve"]!["minX"]!["value"] = 20;
        second["solve"]!["maxX"]!["value"] = 60;

        foreach (var electrode in second["solve"]!["electrodes"]!.AsArray())
        {
            electrode!["minX"]!["value"] = 40;
            electrode["maxX"]!["value"] = 41;
        }

        fields.Add(second);
        fields.Insert(0, System.Text.Json.Nodes.JsonNode.Parse("{ \"type\": \"fieldFree\" }"));

        var path = Path.Combine(_root, "field-free-first.json");
        File.WriteAllText(path, node.ToJsonString());

        var (surface, probe, _) = SurfaceCommand.Sample(
            path, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 60, 10], Samples: 161));

        Assert.Equal(
            ["slitLower (element 1)", "slitLower (element 2)", "slitUpper (element 1)", "slitUpper (element 2)"],
            surface.Plateaus.Select(p => p.Conductor));

        // The probe's own lookup, at a point inside each slit's upper jaw.
        Assert.Equal(1, ElementAt(probe, new Einzel.Core.Geometry.Vec3(0.5, 5.0, 0.0)));
        Assert.Equal(2, ElementAt(probe, new Einzel.Core.Geometry.Vec3(40.5, 5.0, 0.0)));
        Assert.Null(ElementAt(FieldProbe.Open(Model("example", "accelerating-gap")), new Einzel.Core.Geometry.Vec3(0, 0, 0)));
    }

    /// <summary>
    /// A sequenced model driven at one frequency has a cycle mean with that drive's period as
    /// its window and no claim of a second frequency.
    /// </summary>
    /// <remarks>
    /// A sequence that is not held at an operating point has no one state to take a spectrum
    /// of, and says so by reporting no single period. Read unheld, that became "this field
    /// carries more than one frequency" (Qualified) on every sequenced single-frequency model,
    /// while the mean itself was taken on the held field, which has exactly one.
    /// </remarks>
    [Fact]
    public void ASequencedSingleFrequencyModelIsNotCalledMultiFrequency()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("template", "quadrupole-rf")))!;
        node["schemaVersion"] = "0.14";
        node["sequence"] = System.Text.Json.Nodes.JsonNode.Parse(
            """
            [
              { "name": "hold", "duration": { "value": 5.0, "unit": "us" } },
              { "name": "raise", "duration": { "value": 5.0, "unit": "us" },
                "set": { "rfAmplitude": { "value": 1000.0, "unit": "V" } } }
            ]
            """);

        var path = Path.Combine(_root, "sequenced-quadrupole.json");
        File.WriteAllText(path, node.ToJsonString());

        using var json = Json("render", "surface", path, "--cycle-mean", "--at-us", "7", "--samples", "21", "--dry-run");

        var warning = json.RootElement.GetProperty("warnings").EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "render.cycle-mean");

        output.WriteLine(warning.GetProperty("message").GetString());

        Assert.Equal("Provenance", warning.GetProperty("severity").GetString());
        Assert.DoesNotContain("more than one frequency", warning.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(1.0, Number(json, "cycleMeanWindowUs"), 9);

        var probe = FieldProbe.Open(path);
        Assert.True(probe.MonochromaticAt(7e-6));
        Assert.Equal(1e-6, probe.CycleMeanWindowSecondsAt(7e-6)!.Value, 12);
    }

    /// <summary>
    /// The picture is a rotation of the model and never its mirror image: seen from above, the
    /// xy plane has x to the right and y up the screen, as <c>render section</c> draws it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three conductors that no reflection maps onto themselves: +50 V in the upper right of the
    /// plane (x and y positive), -50 V in the lower left, and an earthed one in the lower right.
    /// Each is found in the picture by its plateau's color - warm, cool, neutral - with no
    /// projection of the test's own, so nothing here shares arithmetic with the camera.
    /// </para>
    /// <para>
    /// The frame used to map (across, height, deep) onto the camera's (x, y, z), which is
    /// left-handed for this plane: the top view had y running down the screen and every other
    /// view showed an instrument of the opposite chirality.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePictureIsARotationOfTheModelNotItsMirrorImage()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var electrodes = node["fields"]![0]!["solve"]!["electrodes"]!.AsArray();
        electrodes.Add(electrodes[0]!.DeepClone());

        void Place(int index, string name, double x0, double x1, double y0, double y1, double volts)
        {
            var e = electrodes[index]!;
            e["name"] = name;
            e["minX"]!["value"] = x0;
            e["maxX"]!["value"] = x1;
            e["minY"]!["value"] = y0;
            e["maxY"]!["value"] = y1;
            e["potential"]!["value"] = volts;
        }

        Place(0, "plus", 8, 14, 3, 8, 50.0);
        Place(1, "minus", -16, -10, -8, -3, -50.0);
        Place(2, "earth", 8, 14, -8, -3, 0.0);

        var path = Path.Combine(_root, "asymmetric.json");
        File.WriteAllText(path, node.ToJsonString());

        // Where each plateau is in a picture, in pixels with y UP: the centroid of its color.
        ((double X, double Y) Plus, (double X, double Y) Minus, (double X, double Y) Earth) Find(string view)
        {
            var png = Path.Combine(_root, $"asymmetric-{view}.png");
            Assert.Equal(0, Cli("render", "surface", path, "--view", view, "--no-scale", "--width-px", "600", "--height-px", "400", "--out", png).ExitCode);

            var (w, h, rgb) = Decode(File.ReadAllBytes(png));
            var sums = new (double X, double Y, int N)[3];

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = 3 * ((y * w) + x);
                    int r = rgb[i], g = rgb[i + 1], b = rgb[i + 2];

                    if (r + g + b > 260)
                    {
                        continue;
                    }

                    var which = r > b + 20 ? 0 : b > r + 20 ? 1 : Math.Abs(r - b) < 12 && r + g + b > 150 ? 2 : -1;

                    if (which >= 0)
                    {
                        sums[which] = (sums[which].X + x, sums[which].Y + (h - 1 - y), sums[which].N + 1);
                    }
                }
            }

            output.WriteLine($"{view}: plus {sums[0].N} px, minus {sums[1].N} px, earth {sums[2].N} px");
            Assert.All(sums, s => Assert.True(s.N > 150, $"a plateau was not found in the {view} view"));

            return ((sums[0].X / sums[0].N, sums[0].Y / sums[0].N),
                (sums[1].X / sums[1].N, sums[1].Y / sums[1].N),
                (sums[2].X / sums[2].N, sums[2].Y / sums[2].N));
        }

        // Which way round plus, minus, earth go: positive is counter-clockwise, y up.
        static double Turn(((double X, double Y) P, (double X, double Y) M, (double X, double Y) E) at) =>
            ((at.M.X - at.P.X) * (at.E.Y - at.P.Y)) - ((at.M.Y - at.P.Y) * (at.E.X - at.P.X));

        // In the model, looked at from +z with x right and y up: plus (11, 5.5), minus (-13, -5.5),
        // earth (11, -5.5) go round counter-clockwise.
        Assert.True(Turn(((11, 5.5), (-13, -5.5), (11, -5.5))) > 0.0);

        var top = Find("top");

        Assert.True(top.Plus.X > 300 && top.Plus.Y > 200, $"plus is upper right in the section; here ({top.Plus.X:F0}, {top.Plus.Y:F0})");
        Assert.True(top.Minus.X < 300 && top.Minus.Y < 200, $"minus is lower left; here ({top.Minus.X:F0}, {top.Minus.Y:F0})");
        Assert.True(top.Earth.X > 300 && top.Earth.Y < 200, $"earth is lower right; here ({top.Earth.X:F0}, {top.Earth.Y:F0})");
        Assert.True(Turn(top) > 0.0);

        // Any view from above the plane is a rotation of that, so the three go round the same way.
        Assert.True(Turn(Find("iso")) > 0.0, "the iso view shows the model's mirror image");
    }

    /// <summary>A flag that takes a value and is given none is refused by name, for every verb.</summary>
    /// <param name="args">The command line.</param>
    /// <param name="flag">The flag left without its value.</param>
    /// <remarks>
    /// The command line records such a flag as present with no value, which is what
    /// <c>--json</c> is; read as "absent", <c>--at-us --cycle-mean</c> and <c>--csv --json</c>
    /// dropped the flag and exited 0.
    /// </remarks>
    [Theory]
    [InlineData("render surface --at-us --cycle-mean --dry-run", "--at-us")]
    [InlineData("render surface --height-mm-per-v --json --dry-run", "--height-mm-per-v")]
    [InlineData("render surface --dry-run --csv --json", "--csv")]
    [InlineData("render surface --dry-run --out", "--out")]
    [InlineData("render surface --dry-run --plane --json", "--plane")]
    [InlineData("render surface --dry-run --samples", "--samples")]
    [InlineData("render still --dry-run --width-px", "--width-px")]
    [InlineData("render still --dry-run --view --json", "--view")]
    [InlineData("probe --at-mm 0,0,0 --at-us --json", "--at-us")]
    [InlineData("probe --at-mm 0,0,0 --csv --json", "--csv")]
    [InlineData("export --composite --dry-run --step-mm", "--step-mm")]
    [InlineData("export --composite --dry-run --box-mm --json", "--box-mm")]
    public void AFlagGivenNoValueIsRefusedByName(string args, string flag)
    {
        var words = args.Split(' ');
        var verb = words[0] == "render" ? 2 : 1;
        var model = Model("example", "slit-transmission");

        var (code, stdout, stderr) = Cli([.. words[..verb], model, .. words[verb..]]);
        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains(flag + " takes", stderr, StringComparison.Ordinal);
        Assert.Contains("none was given", stderr, StringComparison.Ordinal);
        AssertRefusal(stdout, words.Contains("--json"), "/args/" + flag);
    }

    // Under --json a refused command line is the error as JSON on stdout (AGT-3); otherwise
    // stdout is empty and the reason is on stderr alone (CLI-2).
    private static void AssertRefusal(string stdout, bool json, string path)
    {
        if (!json)
        {
            Assert.Equal(string.Empty, stdout);
            return;
        }

        using var document = JsonDocument.Parse(stdout);
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.False(document.RootElement.GetProperty("ran").GetBoolean());
        Assert.Equal("SCHEMA_INVALID", error.GetProperty("code").GetString());
        Assert.Equal(path, error.GetProperty("path").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("suggestion").GetString()));
    }

    /// <summary>The CSV is in the outcome, on the terminal, and announced by a dry run.</summary>
    [Fact]
    public void TheCsvIsReportedWhereverThePictureIs()
    {
        var model = Model("example", "slit-transmission");
        var csv = Path.Combine(_root, "reported.csv");
        var png = Path.Combine(_root, "reported.png");

        using (var json = Json("render", "surface", model, "--samples", "21", "--csv", csv, "--out", png, "--dry-run"))
        {
            Assert.Equal(Path.GetFullPath(csv), json.RootElement.GetProperty("csv").GetString());
            Assert.False(File.Exists(csv));
        }

        using (var none = Json("render", "surface", model, "--samples", "21", "--out", png, "--dry-run"))
        {
            Assert.False(none.RootElement.TryGetProperty("csv", out var absent) && absent.ValueKind == JsonValueKind.String);
        }

        var dry = Cli("render", "surface", model, "--samples", "21", "--csv", csv, "--out", png, "--dry-run");
        Assert.Contains($"would write {Path.GetFullPath(csv)}", dry.Stdout, StringComparison.Ordinal);

        var wet = Cli("render", "surface", model, "--samples", "21", "--csv", csv, "--out", png);
        Assert.Contains($"wrote {Path.GetFullPath(csv)}", wet.Stdout, StringComparison.Ordinal);
        Assert.True(File.Exists(csv));
    }

    /// <summary>The wire mesh's depth bias is two pixels' worth, whatever the scene's size or shape.</summary>
    /// <param name="across">The scene's extent across, in millimetres.</param>
    /// <param name="deep">Its extent in depth.</param>
    /// <param name="view">The named view.</param>
    /// <remarks>
    /// Worked out here from what a framing is - so many pixels per millimetre across the screen,
    /// so much depth per millimetre along the view - as a distance on screen. The fixed 4e-3 of
    /// clip depth it replaces was a fraction of the scene's depth: about six pixels in the iso
    /// view and hundreds looking along a long box, which draws the mesh on the far side of a
    /// thin ridge through it.
    /// </remarks>
    [Theory]
    [InlineData(30.0, 20.0, "iso")]
    [InlineData(600.0, 80.0, "iso")]
    [InlineData(600.0, 20.0, "front")]
    [InlineData(20.0, 600.0, "side")]
    public void TheMeshBiasIsAFewPixelsWhateverTheScene(double across, double deep, string view)
    {
        var (azimuth, elevation) = SurfaceCommand.Views[view];
        var corners = new[] { (0.0, 0.0, 0.0), (across, 0.0, 0.0), (0.0, 0.0, deep), (across, 0.35 * across, deep) };
        var matrix = Framing.Around(corners, azimuth, elevation).Matrix(1.6);
        const int Width = 1600;

        // Two points a millimetre apart along the screen's horizontal, and two a millimetre apart
        // along the line of sight: the first pair says what a pixel is, the second what depth is.
        double Clip(int row, (double X, double Y, double Z) p) =>
            (matrix[row] * p.X) + (matrix[4 + row] * p.Y) + (matrix[8 + row] * p.Z) + matrix[12 + row];

        (double X, double Y, double Z) Unit(int row)
        {
            var n = Math.Sqrt((matrix[row] * matrix[row]) + (matrix[4 + row] * matrix[4 + row]) + (matrix[8 + row] * matrix[8 + row]));
            return (matrix[row] / n, matrix[4 + row] / n, matrix[8 + row] / n);
        }

        var pixelsPerMm = (Clip(0, Unit(0)) - Clip(0, (0, 0, 0))) * Width / 2.0;
        var depthPerMm = Math.Abs(Clip(2, Unit(2)) - Clip(2, (0, 0, 0)));

        var biasPx = SurfaceCommand.MeshDepthBias(matrix, Width) / depthPerMm * pixelsPerMm;
        var oldPx = 4e-3 / depthPerMm * pixelsPerMm;

        output.WriteLine($"{across} x {deep} mm, {view}: bias {biasPx:F2} px; the fixed 4e-3 was {oldPx:F1} px");

        Assert.Equal(SurfaceCommand.MeshDepthBiasPx, biasPx, 9);
        Assert.InRange(SurfaceCommand.MeshDepthBiasPx, 1.0, 3.0);
        Assert.True(oldPx > 2.5 * biasPx, "the control: the fixed bias was several times this in every one of these scenes");
    }

    /// <summary>The surface's own sampler agrees with the probe's row at every kind of point.</summary>
    /// <remarks>
    /// The surface asks the probe through a path that allocates nothing and resolves a conductor
    /// once. It is a second spelling of <see cref="FieldProbe.At(int, Einzel.Core.Geometry.Vec3, double)"/>,
    /// so it is held to it: free space, inside a conductor, and outside the domain.
    /// </remarks>
    [Fact]
    public void TheSurfacesSamplerAgreesWithTheProbesRow()
    {
        var probe = FieldProbe.Open(Model("template", "quadrupole"));

        foreach (var mm in new[] { new Einzel.Core.Geometry.Vec3(1.0, 0.5, 0.0), new Einzel.Core.Geometry.Vec3(8.5, 0.0, 0.0), new Einzel.Core.Geometry.Vec3(500.0, 0.0, 0.0) })
        {
            var row = probe.At(0, mm, 0.0);
            var metres = FieldProbe.MillimetresToMetres(mm);
            var status = probe.SamplePotential(in metres, 0.0, cycleMean: false, out var potential, out var distance, out var conductor, out var element, out _);

            output.WriteLine($"({mm.X}, {mm.Y}) mm: {status}, {potential} V, {conductor}");

            Assert.Equal(row.Status, status);
            Assert.Equal(row.PotentialV, potential);
            Assert.Equal(row.ConductorDistanceMm, distance * 1e3);

            if (status == ProbeStatus.InsideConductor)
            {
                Assert.Equal(row.Conductor, conductor);
                Assert.Equal(0, element);
            }
            else
            {
                Assert.Null(conductor);
                Assert.Null(element);
            }
        }

        Assert.Contains(ProbeStatus.InsideConductor, new[] { probe.At(0, new Einzel.Core.Geometry.Vec3(8.5, 0.0, 0.0), 0.0).Status });

        // The row names the elements a point is outside, which the shared core collects.
        Assert.Equal([0], probe.At(0, new Einzel.Core.Geometry.Vec3(500.0, 0.0, 0.0), 0.0).OutsideElements);
        Assert.Empty(probe.At(0, new Einzel.Core.Geometry.Vec3(1.0, 0.5, 0.0), 0.0).OutsideElements);
    }

    /// <summary>
    /// A range that is rounding noise beside the model's own potentials is drawn flat, and the
    /// picture says so.
    /// </summary>
    /// <remarks>
    /// The mirror pair's yz plane through its field-free drift spans 8e-15 V beside a 4.8 kV
    /// cap. Scaled to fill the relief that was 1.3e15 mm per volt: the last bits of a solve
    /// drawn as hills. The control is a plane with a real range, which keeps its scale.
    /// </remarks>
    [Fact]
    public void RoundingNoiseIsDrawnFlatAndSaidToBe()
    {
        var model = Model("template", "planar-mirror-pair");

        using var noise = Json("render", "surface", model, "--plane", "yz", "--samples", "21", "--dry-run");
        using var real = Json("render", "surface", model, "--samples", "41", "--dry-run");
        using var asked = Json("render", "surface", model, "--plane", "yz", "--samples", "21", "--height-mm-per-v", "2", "--dry-run");

        var span = Number(noise, "highest") - Math.Min(Number(noise, "lowest"), 0.0);
        var drawnMm = span * Number(noise, "heightMmPerUnit");

        output.WriteLine($"span {span:G3} V at {Number(noise, "heightMmPerUnit"):G4} mm per V: {drawnMm:G3} mm of drawn height");

        Assert.True(span < 1e-9, "the control: this plane's range is rounding");
        Assert.Contains("render.surface-flat", Codes(noise));
        Assert.True(drawnMm < 1e-3, $"{drawnMm} mm of height drawn from rounding noise");
        Assert.Equal(0.35 * 30.0 / (SurfaceCommand.NoiseFloor * 4800.0), Number(noise, "heightMmPerUnit"), 6);

        Assert.DoesNotContain("render.surface-flat", Codes(real));
        Assert.Equal(0.35 * 947.0 / 4800.0, Number(real, "heightMmPerUnit"), 6);

        Assert.DoesNotContain("render.surface-flat", Codes(asked));
        Assert.Equal(2.0, Number(asked, "heightMmPerUnit"));
    }

    /// <summary>
    /// A plateau is drawn at what the conductor holds, not at what the summed field reads inside
    /// it, and where the two disagree the surface says so.
    /// </summary>
    /// <remarks>
    /// The slit's jaws are held at 40 V, and a uniform analytic field is summed over the whole
    /// model - which has no conductors and fills all space, so inside the jaws the sum reads
    /// about -960 V. The plateau used to be the median of those readings: a conductor drawn at a
    /// potential nothing holds, reported as its own in the picture, the outcome, the PNG and the
    /// CSV. The control is the same model without the analytic field, where the two agree and
    /// nothing is said.
    /// </remarks>
    [Fact]
    public void APlateauIsWhatTheConductorHoldsNotWhatTheSumReads()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;

        foreach (var electrode in node["fields"]![0]!["solve"]!["electrodes"]!.AsArray())
        {
            electrode!["potential"]!["value"] = 40.0;
        }

        var alone = Path.Combine(_root, "jaws.json");
        File.WriteAllText(alone, node.ToJsonString());

        node["fields"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{ "type": "uniform", "field": { "value": [2000000.0, 0, 0], "unit": "V/m" } }"""));

        var summed = Path.Combine(_root, "jaws-in-a-field.json");
        File.WriteAllText(summed, node.ToJsonString());

        using var with = Json("render", "surface", summed, "--samples", "161", "--dry-run");
        using var without = Json("render", "surface", alone, "--samples", "161", "--dry-run");

        foreach (var plateau in with.RootElement.GetProperty("plateaus").EnumerateArray())
        {
            output.WriteLine($"{plateau.GetProperty("conductor")}: {plateau.GetProperty("potentialV")} V, the sum reads {plateau.GetProperty("sampledV")} V");

            Assert.Equal(40.0, plateau.GetProperty("potentialV").GetDouble());
            Assert.True(plateau.GetProperty("sampledV").GetDouble() < -500.0, "the control: the sum inside the jaw is far from what it holds");
        }

        var disagree = with.RootElement.GetProperty("warnings").EnumerateArray()
            .Where(w => w.GetProperty("code").GetString() == "render.plateau-disagrees")
            .Select(w => w.GetProperty("message").GetString()!)
            .ToList();

        Assert.Equal(2, disagree.Count);
        Assert.Contains(disagree, m => m.Contains("'slitUpper'", StringComparison.Ordinal) && m.Contains("holds 40 V", StringComparison.Ordinal));
        Assert.Equal("Qualified", with.RootElement.GetProperty("warnings").EnumerateArray()
            .First(w => w.GetProperty("code").GetString() == "render.plateau-disagrees").GetProperty("severity").GetString());

        Assert.DoesNotContain("render.plateau-disagrees", Codes(without));
        Assert.All(without.RootElement.GetProperty("plateaus").EnumerateArray(), p => Assert.Equal(40.0, p.GetProperty("potentialV").GetDouble()));
    }

    /// <summary>
    /// A driven electrode's plateau is its potential at the instant drawn, over every generator
    /// it taps and every stage a sequence puts it in, and its cycle mean under
    /// <c>--cycle-mean</c>.
    /// </summary>
    /// <remarks>
    /// The rods of <c>quadrupole-rf</c> hold nothing of DC and 500 V of drive at 1 MHz: an eighth
    /// of a cycle in, 500 cos(pi/4) = 353.553 V, and over a cycle nothing. A second stage at
    /// 1000 V holds 1000 V at a whole number of cycles. Read as the DC alone - the recurring way
    /// a driven electrode has been read as earthed here - every one of these is zero.
    /// </remarks>
    [Fact]
    public void ADrivenPlateauIsItsPotentialAtTheInstant()
    {
        var model = Model("template", "quadrupole-rf");

        double Rod(string path, params string[] more)
        {
            using var json = Json(["render", "surface", path, "--samples", "41", "--dry-run", .. more]);
            return json.RootElement.GetProperty("plateaus").EnumerateArray()
                .Select(p => Math.Abs(p.GetProperty("potentialV").GetDouble())).Distinct().Single();
        }

        Assert.Equal(500.0 * Math.Cos(Math.PI / 4.0), Rod(model, "--at-us", "0.125"), 9);
        Assert.Equal(500.0, Rod(model), 9);
        Assert.Equal(0.0, Rod(model, "--cycle-mean"), 9);

        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(model))!;
        node["schemaVersion"] = "0.14";
        node["sequence"] = System.Text.Json.Nodes.JsonNode.Parse(
            """
            [
              { "name": "hold", "duration": { "value": 5.0, "unit": "us" } },
              { "name": "raise", "duration": { "value": 5.0, "unit": "us" },
                "set": { "rfAmplitude": { "value": 1000.0, "unit": "V" } } }
            ]
            """);

        var sequenced = Path.Combine(_root, "raised-quadrupole.json");
        File.WriteAllText(sequenced, node.ToJsonString());

        Assert.Equal(500.0, Rod(sequenced, "--at-us", "2"), 9);
        Assert.Equal(1000.0, Rod(sequenced, "--at-us", "7"), 9);
    }

    /// <summary>The largest potential any electrode reaches counts its drive and every stage.</summary>
    [Fact]
    public void ThePeakElectrodePotentialCountsDrivesAndStages()
    {
        var model = Model("template", "quadrupole-rf");
        Assert.Equal(500.0, SurfaceCommand.PeakElectrodePotential(FieldProbe.Open(model).Model), 9);

        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(model))!;
        node["schemaVersion"] = "0.14";
        node["sequence"] = System.Text.Json.Nodes.JsonNode.Parse(
            """
            [
              { "name": "hold", "duration": { "value": 5.0, "unit": "us" } },
              { "name": "raise", "duration": { "value": 5.0, "unit": "us" },
                "set": { "rfAmplitude": { "value": 1500.0, "unit": "V" } } }
            ]
            """);

        var sequenced = Path.Combine(_root, "peak-quadrupole.json");
        File.WriteAllText(sequenced, node.ToJsonString());

        Assert.Equal(1500.0, SurfaceCommand.PeakElectrodePotential(FieldProbe.Open(sequenced).Model), 9);
        Assert.Equal(100.0, SurfaceCommand.PeakElectrodePotential(FieldProbe.Open(Model("template", "quadrupole")).Model), 9);
    }

    /// <summary>
    /// A long, thin box is sampled across its width as well as along it, and the counts and
    /// spacings are reported; <c>--samples NxM</c> sets both.
    /// </summary>
    /// <remarks>
    /// The mirror pair's domain is 947 by 30 mm. Square cells from 81 samples along it gave 4
    /// rows across it, and from the default 241 gave nine: a board or a gap was a facet or two
    /// and <c>--samples</c> could not help, being capped on the long side. The cells may now be
    /// longer than wide, and the drawn plane is the box, not a stretched one.
    /// </remarks>
    [Fact]
    public void ALongThinBoxIsSampledAcrossItsWidth()
    {
        var model = Model("template", "planar-mirror-pair");

        int[] Samples(JsonDocument d) => [.. d.RootElement.GetProperty("samples").EnumerateArray().Select(e => e.GetInt32())];
        double[] Steps(JsonDocument d) => [.. d.RootElement.GetProperty("stepsMm").EnumerateArray().Select(e => e.GetDouble())];

        using (var json = Json("render", "surface", model, "--samples", "81", "--dry-run"))
        {
            Assert.Equal([81, SurfaceCommand.MinimumDeepSamples], Samples(json));
            Assert.Equal(947.0 / 80.0, Steps(json)[0], 6);
            Assert.Equal(30.0 / (SurfaceCommand.MinimumDeepSamples - 1), Steps(json)[1], 6);
        }

        using (var json = Json("render", "surface", model, "--samples", "101x31", "--dry-run"))
        {
            Assert.Equal([101, 31], Samples(json));
        }

        // Square where square is enough, and fewer across than asked along is respected.
        using (var json = Json("render", "surface", Model("template", "quadrupole"), "--samples", "41", "--dry-run"))
        {
            Assert.Equal([41, 41], Samples(json));
            Assert.Equal(Steps(json)[0], Steps(json)[1], 12);
        }

        using (var json = Json("render", "surface", model, "--samples", "21", "--dry-run"))
        {
            Assert.Equal([21, 21], Samples(json));
        }

        // The plane is drawn at its true size: across 947 mm and deep 30 mm in the picture's frame.
        var (surface, _, _) = SurfaceCommand.Sample(model, new SurfaceRequest(Samples: 81));
        var mesh = SurfaceCommand.Compose(surface, 1.0, 10)[0].Meshes[0].VerticesMm;
        var zs = Enumerable.Range(0, mesh.Count / 3).Select(k => mesh[(3 * k) + 2]).ToList();
        Assert.Equal(30.0, zs.Max() - zs.Min(), 6);

        Assert.Equal(1, Cli("render", "surface", model, "--samples", "81x1", "--dry-run").ExitCode);
        Assert.Equal(1, Cli("render", "surface", model, "--samples", "81y31", "--dry-run").ExitCode);
    }

    /// <summary>A switch never takes the argument after it as its value.</summary>
    /// <remarks>
    /// "A following argument that is not itself an option is this option's value", so
    /// <c>--cycle-mean models/q.json</c> read the model as the switch's value and printed the
    /// usage, and <c>--no-scale out.png</c> dropped the file name without a word.
    /// </remarks>
    [Fact]
    public void ASwitchDoesNotSwallowTheNextArgument()
    {
        var model = Model("template", "quadrupole-rf");

        var first = Cli("render", "surface", "--cycle-mean", model, "--samples", "21", "--dry-run");
        Assert.True(first.ExitCode == 0, first.Stderr);

        var stray = Cli("render", "surface", model, "--samples", "21", "--dry-run", "--no-scale", "out.png");
        output.WriteLine(stray.Stderr);
        Assert.Equal(1, stray.ExitCode);
        Assert.Contains("'out.png'", stray.Stderr, StringComparison.Ordinal);

        var still = Cli("render", "still", model, "--dry-run", "--see-through", "picture.png");
        Assert.Equal(1, still.ExitCode);
        Assert.Contains("'picture.png'", still.Stderr, StringComparison.Ordinal);

        var json = Cli("render", "surface", "--json", model, "--samples", "21", "--dry-run");
        Assert.True(json.ExitCode == 0, json.Stderr);
        Assert.StartsWith("{", json.Stdout.TrimStart(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A point inside a declared shape but outside the solve it belongs to is in no conductor:
    /// no plateau is drawn there, and the probe does not call it inside.
    /// </summary>
    /// <remarks>
    /// The slit's upper jaw is declared running 10 mm past the solve's edge, as
    /// <c>mesh.node-on-shared-face</c> advises carrying a conductor past a face, and the box is
    /// wider than the domain. The shape alone said the samples out there were inside the jaw,
    /// and the held value said 40 V, so a plateau was drawn where nothing had been solved. With
    /// an analytic element summed in, those points are not outside every element, which is the
    /// case a status of "outside the domain" could not catch.
    /// </remarks>
    [Fact]
    public void AConductorPastItsSolveIsNoPlateauThere()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var electrodes = node["fields"]![0]!["solve"]!["electrodes"]!.AsArray();
        electrodes[0]!["potential"]!["value"] = 40.0;
        electrodes[0]!["minX"]!["value"] = 10.0;
        electrodes[0]!["maxX"]!["value"] = 30.0;

        var alone = Path.Combine(_root, "jaw-past-the-edge.json");
        File.WriteAllText(alone, node.ToJsonString());

        node["fields"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{ "type": "uniform", "field": { "value": [10.0, 0, 0], "unit": "V/m" } }"""));

        var summed = Path.Combine(_root, "jaw-past-the-edge-summed.json");
        File.WriteAllText(summed, node.ToJsonString());

        foreach (var path in new[] { alone, summed })
        {
            var (surface, probe, _) = SurfaceCommand.Sample(path, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 30, 10], Samples: 101));

            var past = Enumerable.Range(0, surface.Across.Count).Where(i => surface.Across[i] > 20.0)
                .SelectMany(i => Enumerable.Range(0, surface.Deep.Count).Select(j => surface.Inside[(i * surface.Deep.Count) + j]))
                .Count(label => label is not null);
            var within = Enumerable.Range(0, surface.Across.Count).Where(i => surface.Across[i] is > 10.0 and < 20.0)
                .SelectMany(i => Enumerable.Range(0, surface.Deep.Count).Select(j => surface.Inside[(i * surface.Deep.Count) + j]))
                .Count(label => label == "slitUpper");

            output.WriteLine($"{Path.GetFileName(path)}: {within} jaw samples inside the solve, {past} past it");

            Assert.True(within > 50, "the control: the jaw is a plateau where it was solved");
            Assert.Equal(0, past);

            var row = probe.At(0, new Einzel.Core.Geometry.Vec3(25.0, 5.0, 0.0), 0.0);
            Assert.NotEqual(ProbeStatus.InsideConductor, row.Status);
            Assert.Equal(ProbeStatus.InsideConductor, probe.At(0, new Einzel.Core.Geometry.Vec3(15.0, 5.0, 0.0), 0.0).Status);

            var metres = FieldProbe.MillimetresToMetres(new Einzel.Core.Geometry.Vec3(25.0, 5.0, 0.0));
            probe.SamplePotential(in metres, 0.0, cycleMean: false, out _, out _, out var conductor, out _, out var held);
            Assert.Null(conductor);
            Assert.Null(held);
        }
    }

    /// <summary>
    /// Both halves of a reflected solve are conductors where the solve put them: the probe says
    /// inside, and the surface draws a plateau, in the declared half and in its mirror image.
    /// </summary>
    /// <remarks>
    /// A reflected solve is one element whose field is itself a sum, the solve plus its mirror
    /// image. The probe once took every sum apart and mapped the pieces by position against the
    /// assembly's one-entry-per-element list, so the mirror image had no element: every point in
    /// it read as free space and its conductors drew no plateau - the planar mirror pair and both
    /// Astral analysers among them. Checked in a cross-section and in a volume.
    /// </remarks>
    [Fact]
    public void BothHalvesOfAReflectedSolveAreConductors()
    {
        // A cross-section: the slit, mirrored about its domain's far edge at x = 20 mm.
        var plane = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var solve = plane["fields"]![0]!["solve"]!;
        solve["reflectAboutX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 20, "unit": "mm" }""");

        foreach (var electrode in solve["electrodes"]!.AsArray())
        {
            electrode!["potential"]!["value"] = 40.0;
        }

        plane["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");
        var flat = Path.Combine(_root, "reflected-slit.json");
        File.WriteAllText(flat, plane.ToJsonString());

        // A volume: the parallel plates, mirrored about the domain's edge at x = 30 mm.
        var volume = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "parallel-plate-gap-3d")))!;
        volume["fields"]![0]!["solve3d"]!["reflectAboutX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 30, "unit": "mm" }""");
        var solid = Path.Combine(_root, "reflected-plates.json");
        File.WriteAllText(solid, volume.ToJsonString());

        foreach (var (path, inDeclared, inMirror, box, planeName, name) in new[]
        {
            (flat, new Einzel.Core.Geometry.Vec3(0.5, 5.0, 0.0), new Einzel.Core.Geometry.Vec3(39.5, 5.0, 0.0), new double[] { -20, -10, 60, 10 }, "xy", "slitUpper"),
            (solid, new Einzel.Core.Geometry.Vec3(0.0, 0.0, 5.5), new Einzel.Core.Geometry.Vec3(60.0, 0.0, 5.5), new double[] { -30, -15, 90, 15 }, "xz", "upper"),
        })
        {
            var probe = FieldProbe.Open(path);

            foreach (var point in new[] { inDeclared, inMirror })
            {
                var row = probe.At(0, point, 0.0);
                output.WriteLine($"{Path.GetFileName(path)} ({point.X}, {point.Y}, {point.Z}): {row.Status} {row.Conductor}");

                Assert.Equal(ProbeStatus.InsideConductor, row.Status);
                Assert.Equal(name, row.Conductor);
                Assert.Equal(0, ElementAt(probe, point));
            }

            var (surface, _, _) = SurfaceCommand.Sample(path, new SurfaceRequest(Plane: planeName, BoxMm: box, Samples: 121));
            var plateau = surface.Plateaus.Single(p => p.Conductor == name);
            var mirrorX = surface.Across.Select((x, i) => (x, i)).Where(c => c.x > (planeName == "xy" ? 20 : 30)).Select(c => c.i).ToHashSet();
            var inMirrorHalf = Enumerable.Range(0, surface.Inside.Count).Count(k => surface.Inside[k] == name && mirrorX.Contains(k / surface.Deep.Count));

            output.WriteLine($"  plateau {plateau.Conductor} at {plateau.PotentialV} V over {plateau.Samples} samples, {inMirrorHalf} in the mirror half");

            Assert.Equal(0, plateau.Element);
            Assert.True(inMirrorHalf > 0 && inMirrorHalf * 3 > plateau.Samples, "the mirror half has a plateau too");
        }
    }

    /// <summary>The element a point's conductor belongs to is given only for a point in metal.</summary>
    /// <remarks>
    /// It used to answer for any point near a conductor - the vacuum on the axis of the einzel
    /// lens's centre tube had element 0, against the method's own documentation.
    /// </remarks>
    [Fact]
    public void AnElementIsGivenOnlyForAPointInMetal()
    {
        var probe = FieldProbe.Open(Model("example", "einzel-lens"));

        Assert.Null(ElementAt(probe, new Einzel.Core.Geometry.Vec3(0.0, 0.0, 0.0)));
        Assert.Equal(ProbeStatus.Ok, probe.At(0, new Einzel.Core.Geometry.Vec3(0.0, 0.0, 0.0), 0.0).Status);
        Assert.Equal(0, ElementAt(probe, new Einzel.Core.Geometry.Vec3(0.0, 6.5, 0.0)));
    }

    /// <summary>
    /// A field sum says what a named conductor holds only where the name cannot mean two
    /// things.
    /// </summary>
    /// <remarks>
    /// Names are unique within one element. Two elements each with a <c>plate</c>, at +100 V and
    /// -100 V, both read +100 V when the sum answered with its first member's; a wrapper round
    /// the sum would have carried that onward. A reflected solve - one element seen twice - is
    /// the case where the halves agree, and there the sum answers.
    /// </remarks>
    [Fact]
    public void ASumDoesNotAnswerForANameTwoElementsShare()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();
        var first = fields[0]!["solve"]!["electrodes"]!.AsArray();
        first.RemoveAt(1);
        first[0]!["name"] = "plate";
        first[0]!["potential"]!["value"] = 100.0;

        var second = fields[0]!.DeepClone();
        second["solve"]!["minX"]!["value"] = 20.0;
        second["solve"]!["maxX"]!["value"] = 60.0;
        second["solve"]!["electrodes"]![0]!["minX"]!["value"] = 40.0;
        second["solve"]!["electrodes"]![0]!["maxX"]!["value"] = 41.0;
        second["solve"]!["electrodes"]![0]!["potential"]!["value"] = -100.0;
        fields.Add(second);
        node["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");

        var path = Path.Combine(_root, "two-plates.json");
        File.WriteAllText(path, node.ToJsonString());

        var probe = FieldProbe.Open(path);
        var (field, _, leaves) = Einzel.Fields.FieldAssembly.BuildMapped(probe.Model);

        // The sum is not asked at all: it has no answer to give for a name two elements share.
        Assert.False(field is Einzel.Fields.IHeldConductors);

        // Each element's leaf says what its own holds, which is how the probe and the surface ask.
        Assert.Equal([0, 1], leaves.Select(l => l.Element));
        Assert.Equal([100.0, -100.0], leaves.Select(l => l.Held.HeldPotentialOf("plate", 0.0) ?? double.NaN));

        // Each conductor's label is one string, shared by every sample inside it: a name two
        // elements share is labelled with its element, and that label was built per sample.
        var (labelled, _, _) = SurfaceCommand.Sample(path, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 60, 10], Samples: 161));

        foreach (var label in new[] { "plate (element 0)", "plate (element 1)" })
        {
            var inside = labelled.Inside.Where(l => l == label).ToList();

            Assert.True(inside.Count > 1, $"{label}: {inside.Count} samples");
            Assert.All(inside, l => Assert.Same(inside[0], l));
        }
        Assert.Equal(-100.0, probe.At(0, new Einzel.Core.Geometry.Vec3(40.5, 5.0, 0.0), 0.0).Status == ProbeStatus.InsideConductor
            ? SurfaceCommand.Sample(path, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 60, 10], Samples: 161)).Surface.Plateaus.Single(p => p.Element == 1).PotentialV
            : double.NaN);

        // A reflected solve is one element seen twice: its halves agree, and the sum answers.
        var reflected = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        reflected["fields"]![0]!["solve"]!["reflectAboutX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 20, "unit": "mm" }""");
        reflected["fields"]![0]!["solve"]!["electrodes"]![0]!["potential"]!["value"] = 40.0;
        reflected["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");
        var mirrored = Path.Combine(_root, "mirrored-plate.json");
        File.WriteAllText(mirrored, reflected.ToJsonString());

        var (_, _, halves) = Einzel.Fields.FieldAssembly.BuildMapped(FieldProbe.Open(mirrored).Model);

        Assert.Equal([false, true], halves.Select(l => l.Mirrored));
        Assert.Equal([0, 0], halves.Select(l => l.Element));
        Assert.Same(halves[0].Held, halves[1].Held);
        Assert.Equal(40.0, halves[1].Held.HeldPotentialOf("slitUpper", 0.0));
    }

    /// <summary>
    /// Each half of a reflected solve is metal only on its own side of the mirror: a conductor
    /// declared running past the plane ends there, and its image takes over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declared half is solved in the declared box, and what lies beyond the plane is that
    /// box's image. A conductor declared from 18 mm to 40 mm against a mirror at 20 mm is
    /// therefore metal from 18 to 22: the two millimetres on the near side, and their image.
    /// </para>
    /// <para>
    /// The probe asked whether a conductor's ELEMENT was solved at a point, and a reflected
    /// solve is one element with two halves - so the declared shape counted as metal throughout
    /// the image's half too, and a point at 30 mm read inside-conductor with a plateau drawn
    /// from 22 mm to 40 mm over what is vacuum. Asked per leaf, in a cross-section and a volume.
    /// </para>
    /// </remarks>
    [Fact]
    public void AConductorDeclaredPastTheMirrorPlaneEndsAtItsImage()
    {
        var plane = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var solve = plane["fields"]![0]!["solve"]!;
        solve["reflectAboutX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 20, "unit": "mm" }""");
        solve["electrodes"]![0]!["minX"]!["value"] = 18.0;
        solve["electrodes"]![0]!["maxX"]!["value"] = 40.0;
        solve["electrodes"]![0]!["potential"]!["value"] = 40.0;
        plane["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");
        var flat = Path.Combine(_root, "past-the-mirror.json");
        File.WriteAllText(flat, plane.ToJsonString());

        var volume = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "parallel-plate-gap-3d")))!;
        var solve3d = volume["fields"]![0]!["solve3d"]!;
        solve3d["reflectAboutX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 30, "unit": "mm" }""");
        solve3d["electrodes"]![1]!["minX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 28, "unit": "mm" }""");
        solve3d["electrodes"]![1]!["maxX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 50, "unit": "mm" }""");
        var solid = Path.Combine(_root, "past-the-mirror-3d.json");
        File.WriteAllText(solid, volume.ToJsonString());

        foreach (var (path, near, image, beyond, box, planeName, name, edge) in new[]
        {
            (flat, new Einzel.Core.Geometry.Vec3(19.0, 5.0, 0.0), new Einzel.Core.Geometry.Vec3(21.0, 5.0, 0.0), new Einzel.Core.Geometry.Vec3(30.0, 5.0, 0.0), new double[] { -20, -10, 60, 10 }, "xy", "slitUpper", 22.0),
            (solid, new Einzel.Core.Geometry.Vec3(29.0, 0.0, 5.5), new Einzel.Core.Geometry.Vec3(31.0, 0.0, 5.5), new Einzel.Core.Geometry.Vec3(45.0, 0.0, 5.5), new double[] { -30, -15, 90, 15 }, "xz", "upper", 32.0),
        })
        {
            var probe = FieldProbe.Open(path);

            // On the near side, and in its image: metal.
            foreach (var point in new[] { near, image })
            {
                var row = probe.At(0, point, 0.0);
                output.WriteLine($"{Path.GetFileName(path)} x = {point.X}: {row.Status} {row.Conductor}");

                Assert.Equal(ProbeStatus.InsideConductor, row.Status);
                Assert.Equal(name, row.Conductor);
            }

            // Past the image, inside the declared shape and in the image's half: vacuum.
            var past = probe.At(0, beyond, 0.0);
            output.WriteLine($"{Path.GetFileName(path)} x = {beyond.X}: {past.Status}");

            Assert.Equal(ProbeStatus.Ok, past.Status);
            Assert.Null(ElementAt(probe, beyond));

            // And the plateau stops where the image does.
            var (surface, _, _) = SurfaceCommand.Sample(path, new SurfaceRequest(Plane: planeName, BoxMm: box, Samples: 161));
            var reach = Enumerable.Range(0, surface.Inside.Count)
                .Where(k => surface.Inside[k] == name)
                .Max(k => surface.Across[k / surface.Deep.Count]);

            output.WriteLine($"  the plateau of {name} reaches x = {reach} mm");

            Assert.InRange(reach, edge - 1.0, edge + 0.5);
        }
    }

    /// <summary>
    /// The range a surface states is what its conductors hold, whatever the sampling: the
    /// shipped einzel lens, 0 and 500 V, is 0 to 500 V at three sample counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The range came from the raw samples, and beside a step on the boundary a cubic
    /// interpolant overshoots by an amount that depends on where the samples fall: the lens
    /// read -25 to 500 V at 61 samples, -32 to 500 at 241 and -14.8 to 537 at 1001, and the
    /// relief and the scale bar followed it. A field with no charge in it is nowhere outside
    /// what its boundary holds.
    /// </para>
    /// <para>
    /// Two layers. A sample on the face of a conductor carried to the edge of its solve is the
    /// conductor's - which is what removed the 537 V. And what the samples still read past the
    /// conductors' range, on the earthed edge beside the tube's end, is reported as overshoot
    /// and not as the field: the stated range, the scale and the bar are the conductors'.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStatedRangeIsWhatTheConductorsHoldAtAnySampling()
    {
        var lens = Model("template", "einzel-lens");
        var scales = new List<double>();

        foreach (var samples in new[] { 61, 241, 1001 })
        {
            using var document = Json("render", "surface", lens, "--samples", samples.ToString(System.Globalization.CultureInfo.InvariantCulture), "--dry-run");
            var root = document.RootElement;

            output.WriteLine(
                $"{samples}: stated {Number(document, "lowest")} to {Number(document, "highest")} V, "
                + $"sampled {Number(document, "sampledLowest")} to {Number(document, "sampledHighest")} V");

            Assert.Equal(0.0, Number(document, "lowest"));
            Assert.Equal(500.0, Number(document, "highest"));
            Assert.Equal(0.0, Number(document, "appliedLowest"));
            Assert.Equal(500.0, Number(document, "appliedHighest"));

            // The face on the domain's edge is the tube's: nothing reads above what it holds.
            Assert.Equal(500.0, Number(document, "sampledHighest"));

            // What is left is said, with how much and where.
            Assert.True(Number(document, "sampledLowest") < -1.0, "the control: this sampling does overshoot");

            var warning = root.GetProperty("warnings").EnumerateArray()
                .Single(w => w.GetProperty("code").GetString() == "render.interpolant-overshoot");

            Assert.Contains("0 to 500 V the conductors hold", warning.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Contains("y = -12 mm", warning.GetProperty("message").GetString(), StringComparison.Ordinal);

            scales.Add(Number(document, "heightMmPerUnit"));
        }

        // The same picture height at every sampling: the scale is made from the stated range.
        Assert.All(scales, scale => Assert.Equal(scales[0], scale, 12));

        // A sample on the centre tube's face at the domain's edge is the tube's.
        var (surface, probe, _) = SurfaceCommand.Sample(lens, new SurfaceRequest(Samples: 241));
        var onFace = Enumerable.Range(0, surface.Across.Count)
            .Where(i => surface.Inside[i * surface.Deep.Count] == "centre")
            .ToList();

        Assert.Equal(-12.0, surface.Deep[0], 9);
        Assert.NotEmpty(onFace);
        Assert.All(onFace, i => Assert.Equal(500.0, surface.At(i, 0)));

        // No such range where an element fills all space: an analytic field has no boundary to
        // be bounded by, and nothing is stated or warned.
        Assert.Null(FieldProbe.Open(Model("example", "accelerating-gap")).AppliedRange(0.0, cycleMean: false));
        Assert.NotNull(probe.AppliedRange(0.0, cycleMean: false));
    }

    /// <summary>
    /// Every kind of field element that solves a geometry hands out leaves for its conductors,
    /// and a run does not depend on them.
    /// </summary>
    /// <remarks>
    /// The check that every conductor-bearing element has a leaf was inside the assembly, which
    /// builds every run's field: metadata only a probe and a drawing read could have failed a
    /// run, a sweep or a test. It is the probe's now, made as it opens.
    /// </remarks>
    [Fact]
    public void EverySolvedElementKindHandsOutLeavesAndARunDoesNotReadThem()
    {
        // Every kind there is, each said to solve a geometry or not: a new kind fails here until
        // it is on one side or the other.
        var solving = new[] { Einzel.Core.Model.CompiledFieldKind.Solved2D, Einzel.Core.Model.CompiledFieldKind.Solved3D };
        var analytic = new[]
        {
            Einzel.Core.Model.CompiledFieldKind.FieldFree, Einzel.Core.Model.CompiledFieldKind.Uniform, Einzel.Core.Model.CompiledFieldKind.HalfSpaceUniform,
            Einzel.Core.Model.CompiledFieldKind.IdealQuadrupoleRf, Einzel.Core.Model.CompiledFieldKind.QuadroLogarithmic, Einzel.Core.Model.CompiledFieldKind.UniformMagnetic,
        };

        Assert.Equal(Enum.GetValues<Einzel.Core.Model.CompiledFieldKind>().Order(), solving.Concat(analytic).Order());

        var seen = new HashSet<Einzel.Core.Model.CompiledFieldKind>();

        foreach (var (kind, name) in new[]
        {
            ("example", "slit-transmission"), ("example", "parallel-plate-gap-3d"), ("example", "accelerating-gap"),
            ("example", "rf-quadrupole-confined"), ("template", "quadrupole-rf"),
        })
        {
            var model = FieldProbe.Open(Model(kind, name)).Model;
            var (_, _, leaves) = Einzel.Fields.FieldAssembly.BuildMapped(model);

            for (var index = 0; index < model.Fields.Count; index++)
            {
                var element = model.Fields[index].Kind;
                seen.Add(element);

                Assert.Equal(solving.Contains(element), leaves.Any(l => l.Element == index));
            }
        }

        // The control: both solving kinds were among the models looked at.
        Assert.Subset(seen, solving.ToHashSet());

        // And a run is a run: the volume example flies, reading no leaf.
        var project = Path.Combine(_root, "run");
        Assert.Equal(0, Cli("init", project).ExitCode);
        var flown = Path.Combine(project, "models", "plates.json");
        Assert.Equal(0, Cli("new", flown, "--from-example", "parallel-plate-gap-3d").ExitCode);
        Assert.Equal(0, Cli("run", flown, "--progress", "0").ExitCode);
    }

    /// <summary>
    /// On the mirror plane of a reflected solve the potential is the field's, once: a half
    /// einzel lens reads what its electrode holds there, through the probe and on the surface.
    /// </summary>
    /// <remarks>
    /// A point on the plane is its own image, and both halves of the solve answered for it: the
    /// 500 V centre tube of a lens written as one half and its mirror read 999.5 V on the plane,
    /// in <c>probe</c> and at the middle column of a surface's default sampling.
    /// </remarks>
    [Fact]
    public void TheMirrorPlaneOfAReflectedSolveIsCountedOnce()
    {
        // The slit, mirrored about its domain's edge at x = 20 mm, that edge a mirror, and an
        // electrode at 40 V reaching the plane.
        var plane = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var solve = plane["fields"]![0]!["solve"]!;
        solve["reflectAboutX"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "value": 20, "unit": "mm" }""");
        solve["rightEdge"] = "neumann";
        solve["electrodes"]![0]!["minX"]!["value"] = 15.0;
        solve["electrodes"]![0]!["maxX"]!["value"] = 20.0;
        solve["electrodes"]![0]!["potential"]!["value"] = 40.0;
        plane["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");
        var path = Path.Combine(_root, "half-and-mirror.json");
        File.WriteAllText(path, plane.ToJsonString());

        var probe = FieldProbe.Open(path);

        // In the gap, on the plane and a hair either side of it.
        var before = probe.At(0, new Einzel.Core.Geometry.Vec3(20.0 - 1e-6, 0.0, 0.0), 0.0);
        var on = probe.At(0, new Einzel.Core.Geometry.Vec3(20.0, 0.0, 0.0), 0.0);
        var after = probe.At(0, new Einzel.Core.Geometry.Vec3(20.0 + 1e-6, 0.0, 0.0), 0.0);

        output.WriteLine($"{before.PotentialV!.Value:F6} V before, {on.PotentialV!.Value:F6} V on, {after.PotentialV!.Value:F6} V after the plane");

        Assert.InRange(on.PotentialV!.Value, 1.0, 40.0);
        Assert.Equal(before.PotentialV!.Value, on.PotentialV!.Value, 3);
        Assert.Equal(after.PotentialV!.Value, on.PotentialV!.Value, 3);

        // And nowhere on a surface across the plane does a sample read past what is held.
        var (surface, _, warnings) = SurfaceCommand.Sample(path, new SurfaceRequest(Plane: "xy", BoxMm: [0, -10, 40, 10], Samples: 241));

        Assert.Contains(20.0, surface.Across);
        Assert.InRange(surface.SampledHighest, 39.0, 40.0 * 1.05);
        Assert.Equal(40.0, surface.AppliedHighest);
    }

    /// <summary>
    /// The surface is drawn no further than its scale: where the samples overshoot what the
    /// conductors hold, the drawn height and color are held to that range, and the raw readings
    /// stay in the table.
    /// </summary>
    /// <remarks>
    /// The range, the ramp and the bar were held to the conductors' range and the vertices were
    /// not, so the einzel lens's surface dipped 32 V below the earth its own bar ended at.
    /// </remarks>
    [Fact]
    public void TheSurfaceIsDrawnNoFurtherThanItsScale()
    {
        var (surface, _, warnings) = SurfaceCommand.Sample(Model("template", "einzel-lens"), new SurfaceRequest(Samples: 241));
        var scale = SurfaceCommand.Scale(surface, new SurfaceRequest(Samples: 241));

        Assert.True(surface.SampledLowest < -1.0, "the control: the samples do overshoot");

        var heights = SurfaceCommand.Compose(surface, scale, 40)
            .SelectMany(layer => layer.Meshes.SelectMany(m => Triples(m.VerticesMm)).Concat(layer.Lines.SelectMany(l => Triples(l.PointsMm))))
            .Select(p => p.Y)
            .Where(double.IsFinite)
            .ToList();

        output.WriteLine($"drawn from {heights.Min() / scale:G6} to {heights.Max() / scale:G6} V; sampled from {surface.SampledLowest:G6} V");

        Assert.NotEmpty(heights);
        Assert.True(heights.Min() >= 0.0, $"the surface is drawn down to {heights.Min() / scale:G6} V, below the foot of its bar");
        Assert.True(heights.Max() <= (500.0 * scale) + 1e-9);

        // Node by node: the drawn value is the sampled one held to the range.
        var lowest = Enumerable.Range(0, surface.Values.Count).MinBy(k => surface.Values[k]);
        var (i, j) = (lowest / surface.Deep.Count, lowest % surface.Deep.Count);

        Assert.Equal(surface.SampledLowest, surface.At(i, j));
        Assert.Equal(0.0, surface.Drawn(i, j));

        // The table keeps what was read, and the warning says the drawing is held.
        Assert.Contains(surface.SampledLowest.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), SurfaceCommand.ToCsv(surface, warnings), StringComparison.Ordinal);
        Assert.Contains("the drawing is held to it", warnings.Single(w => w.Code == "render.interpolant-overshoot").Message, StringComparison.Ordinal);

        // Where nothing bounds the field, the drawing is the samples.
        var (analytic, _, _) = SurfaceCommand.Sample(
            Model("example", "accelerating-gap"), new SurfaceRequest(Plane: "xy", BoxMm: [0, -5, 10, 5], Samples: 21));

        Assert.Null(analytic.AppliedLowest);
        Assert.Equal(analytic.At(3, 3), analytic.Drawn(3, 3));
    }

    /// <summary>
    /// A failure while sampling comes out of the parallel loop as itself, not wrapped: a refusal
    /// stays a refusal.
    /// </summary>
    /// <remarks>
    /// A parallel loop gathers what its bodies throw into an aggregate, which the command line
    /// reports as an unknown failure - exit 6, "a defect in einzel" - whatever was thrown.
    /// </remarks>
    [Fact]
    public void AFailureWhileSamplingComesOutAsItself()
    {
        var refusal = new Einzel.Core.Errors.EinzelException(new Einzel.Core.Errors.EinzelError
        {
            Code = Einzel.Core.Errors.ErrorCodes.RegimeInvalid,
            Path = "/fields/0",
            Constraint = "the field refuses this row",
        });

        var thrown = Assert.Throws<Einzel.Core.Errors.EinzelException>(
            () => SurfaceCommand.ForEachRow(64, null, row =>
            {
                if (row == 17)
                {
                    throw refusal;
                }
            }));

        Assert.Same(refusal, thrown);

        // Any other failure too, as what it is.
        Assert.Throws<InvalidOperationException>(
            () => SurfaceCommand.ForEachRow(8, 1, _ => throw new InvalidOperationException("a defect")));

        // The control: with nothing thrown, every row is visited.
        var visited = new int[64];
        SurfaceCommand.ForEachRow(64, null, row => visited[row]++);
        Assert.All(visited, count => Assert.Equal(1, count));
    }

    /// <summary>
    /// A destination that is the model, or the other destination, is refused: nothing a command
    /// reads is written over.
    /// </summary>
    /// <remarks>
    /// <c>--out model.json</c> was accepted and the picture replaced the model it was a picture
    /// of.
    /// </remarks>
    [Fact]
    public void NothingIsWrittenOverTheModel()
    {
        var lens = Path.Combine(_root, "lens.json");
        Directory.CreateDirectory(_root);
        File.Copy(Model("template", "einzel-lens"), lens);
        var before = File.ReadAllText(lens);

        foreach (var args in new[]
        {
            new[] { "render", "surface", lens, "--samples", "21", "--out", lens },
            ["render", "surface", lens, "--samples", "21", "--out", Path.Combine(_root, "p.png"), "--csv", lens],
            ["render", "still", lens, "--width-px", "32", "--height-px", "32", "--out", lens],
            ["render", "section", lens, "--out", lens],
            ["probe", lens, "--line-mm", "0,0,0", "--to-mm", "1,0,0", "--csv", lens],
        })
        {
            var (code, _, stderr) = Cli(args);
            output.WriteLine($"{string.Join(' ', args[..2])}: exit {code}: {stderr.Trim().Split('\n')[0]}");

            Assert.Equal(1, code);
            Assert.Contains("/args/--" + args[^2][2..], stderr, StringComparison.Ordinal);
            Assert.Contains("the file this command reads", stderr, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllText(lens));
        }

        Assert.False(File.Exists(Path.Combine(_root, "p.png")));

        // Compared as the file system compares names.
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(1, Cli("render", "surface", lens, "--samples", "21", "--out", lens.ToUpperInvariant()).ExitCode);
            Assert.Equal(before, File.ReadAllText(lens));
        }
    }

    /// <summary>
    /// A write that fails part way leaves everything as it was: a file that was there before is
    /// still there, with what it held.
    /// </summary>
    /// <remarks>
    /// Undoing a write by deleting what was put in place deleted the picture of an earlier run
    /// when the table beside the new one could not be written.
    /// </remarks>
    [Fact]
    public void AFailedWriteLeavesAnEarlierFileAsItWas()
    {
        Directory.CreateDirectory(_root);
        var picture = Path.Combine(_root, "keep.png");
        var table = Path.Combine(_root, "keep.csv");

        File.WriteAllBytes(picture, [1, 2, 3]);
        File.WriteAllBytes(table, [4, 5]);

        // The second destination cannot be replaced: held open by a reader that shares nothing.
        // A lock on Windows; elsewhere a move replaces an open file and there is no failure to undo.
        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(table, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var refusal = Assert.Throws<Einzel.Core.Errors.EinzelException>(
                    () => ArtifactFiles.WriteAll([("out", picture, [9]), ("csv", table, [9])]));

                Assert.Equal("/args/--csv", refusal.Error.Path);
            }

            Assert.Equal([1, 2, 3], File.ReadAllBytes(picture));
            Assert.Equal([4, 5], File.ReadAllBytes(table));
        }

        // The control: with nothing in the way, both are replaced and nothing else is left.
        ArtifactFiles.WriteAll([("out", picture, [7]), ("csv", table, [8])]);

        Assert.Equal([7], File.ReadAllBytes(picture));
        Assert.Equal([8], File.ReadAllBytes(table));
        Assert.Equal(["keep.csv", "keep.png"], Directory.GetFiles(_root, "keep.*").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Earth is always labelled on the scale; an end that would sit on top of it gives way.
    /// </summary>
    /// <remarks>
    /// Both ends were labelled whatever they collided with and earth only if it had room, so a
    /// minimum a hair below zero took the foot's label and <c>0 V</c> was left out.
    /// </remarks>
    [Fact]
    public void EarthIsAlwaysLabelledOnTheScale()
    {
        // 500 V over 500 pixels, text 20 pixels tall.
        static double Row(double volts) => 600.0 - volts;

        // A minimum a hair below earth: earth is labelled, the minimum is not.
        Assert.Equal([0.0, 500.0], SurfaceCommand.ScaleTicks(-0.4, 500.0, [], Row, 20.0));

        // The control: a minimum clear of earth is labelled with it.
        Assert.Equal([0.0, 500.0, -200.0], SurfaceCommand.ScaleTicks(-200.0, 500.0, [], Row, 20.0));

        // A surface wholly above earth still has earth at its foot, and a plateau where it fits.
        Assert.Equal([0.0, 500.0, 250.0], SurfaceCommand.ScaleTicks(100.0, 500.0, [250.0, 495.0, 10.0], Row, 20.0));

        // A flat surface has the one label.
        Assert.Equal([0.0], SurfaceCommand.ScaleTicks(0.0, 0.0, [], Row, 20.0));
    }

    /// <summary>
    /// A height scale that makes the surface infinitely tall, or a thousand times taller than it
    /// is wide, is refused by the flag that asked for it, and nothing is written.
    /// </summary>
    /// <param name="flag">The flag.</param>
    /// <param name="value">Its value.</param>
    /// <param name="path">Where the refusal is located.</param>
    /// <remarks>
    /// <c>--relief 1e308</c> gave "height Infinity mm per V", <c>"written": true</c>, a null
    /// scale in the JSON and a PNG drawn from heights that were not numbers.
    /// </remarks>
    [Theory]
    [InlineData("--relief", "1e308", "/args/--relief")]
    [InlineData("--relief", "11", "/args/--relief")]
    [InlineData("--relief", "0", "/args/--relief")]
    [InlineData("--height-mm-per-v", "1e300", "/args/--height-mm-per-v")]
    [InlineData("--height-mm-per-v", "1000", "/args/--height-mm-per-v")]
    public void AHeightScaleThatCannotBeDrawnIsRefused(string flag, string value, string path)
    {
        var target = Path.Combine(_root, "tall.png");
        var (code, stdout, stderr) = Cli("render", "surface", Model("template", "einzel-lens"), flag, value, "--samples", "21", "--out", target, "--json");

        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains(path, stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
        Assert.DoesNotContain("Infinity", stdout + stderr, StringComparison.Ordinal);
    }

    /// <summary>The control for the theory above: the largest relief, and an ordinary explicit scale, are drawn.</summary>
    [Fact]
    public void TheLargestReliefIsDrawn()
    {
        using var relief = Json("render", "surface", Model("template", "einzel-lens"), "--relief", "10", "--samples", "21", "--dry-run");
        using var scale = Json("render", "surface", Model("template", "einzel-lens"), "--height-mm-per-v", "0.01", "--samples", "21", "--dry-run");

        Assert.True(double.IsFinite(Number(relief, "heightMmPerUnit")));
        Assert.Equal(0.01, Number(scale, "heightMmPerUnit"));
    }

    /// <summary>
    /// A destination that cannot be written is refused by the flag that named it, exit 1, before
    /// anything is solved - and a picture is never left behind without its table.
    /// </summary>
    /// <remarks>
    /// <c>--out</c> or <c>--csv</c> naming a directory came out of the write as an access
    /// exception: exit 6, a stack trace, a request to report a bug. And the picture was written
    /// before the table was tried.
    /// </remarks>
    [Fact]
    public void ADestinationThatCannotBeWrittenIsRefusedByItsFlag()
    {
        var lens = Model("template", "einzel-lens");
        var folder = Path.Combine(_root, "a-folder");
        Directory.CreateDirectory(folder);
        var picture = Path.Combine(_root, "picture.png");

        // --csv is a directory: refused at --csv, and the picture is not written either.
        var (code, _, stderr) = Cli("render", "surface", lens, "--samples", "21", "--out", picture, "--csv", folder);

        Assert.Equal(1, code);
        Assert.Contains("/args/--csv", stderr, StringComparison.Ordinal);
        Assert.Contains("is a directory", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(picture));

        // --out is a directory.
        (code, _, stderr) = Cli("render", "surface", lens, "--samples", "21", "--out", folder);
        Assert.Equal(1, code);
        Assert.Contains("/args/--out", stderr, StringComparison.Ordinal);

        // Before anything is read, let alone solved: with no model there at all, it is still the
        // destination that is refused.
        (code, _, stderr) = Cli("render", "surface", Path.Combine(_root, "no-such-model.json"), "--out", folder);
        Assert.Equal(1, code);
        Assert.Contains("/args/--out", stderr, StringComparison.Ordinal);

        // A folder on the way is a file.
        var blocked = Path.Combine(picture + ".txt", "table.csv");
        File.WriteAllText(picture + ".txt", "in the way");
        (code, _, stderr) = Cli("render", "surface", lens, "--samples", "21", "--out", picture, "--csv", blocked);
        Assert.Equal(1, code);
        Assert.Contains("/args/--csv", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(picture));

        // The other verbs that write where they are told.
        foreach (var args in new[]
        {
            new[] { "render", "still", lens, "--width-px", "32", "--height-px", "32", "--out", folder },
            ["render", "section", lens, "--out", folder],
            ["probe", lens, "--line-mm", "0,0,0", "--to-mm", "1,0,0", "--csv", folder],
        })
        {
            (code, _, stderr) = Cli(args);
            output.WriteLine($"{string.Join(' ', args[..2])}: exit {code}: {stderr.Trim().Split('\n')[0]}");

            Assert.Equal(1, code);
            Assert.Contains("/args/--" + args[^2][2..], stderr, StringComparison.Ordinal);
        }

        // The control: with somewhere to write, both are written.
        var table = Path.Combine(_root, "table.csv");
        Assert.Equal(0, Cli("render", "surface", lens, "--samples", "21", "--out", picture, "--csv", table).ExitCode);
        Assert.True(File.Exists(picture) && File.Exists(table));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    /// <summary>Several files are written all together or not at all, and no temporary file is left.</summary>
    [Fact]
    public void SeveralFilesAreWrittenTogetherOrNotAtAll()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.bin");
        var second = Path.Combine(_root, "second.bin");

        ArtifactFiles.WriteAll([("out", first, [1, 2, 3]), ("csv", second, [4, 5])]);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(first));
        Assert.Equal([4, 5], File.ReadAllBytes(second));

        // The second cannot be put in place: a file held open by another reader that shares
        // nothing. That is a lock on Windows; elsewhere a move replaces an open file, so there
        // the refusal is exercised by the directory cases above.
        if (OperatingSystem.IsWindows())
        {
            File.Delete(first);

            using (new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var refusal = Assert.Throws<Einzel.Core.Errors.EinzelException>(
                    () => ArtifactFiles.WriteAll([("out", first, [9]), ("csv", second, [9])]));

                Assert.Equal("/args/--csv", refusal.Error.Path);
            }

            Assert.False(File.Exists(first), "the first file was put in place and must be taken away again");
            Assert.Equal([4, 5], File.ReadAllBytes(second));
        }

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    /// <summary>
    /// The instant on the picture names the phase drawn: two instants a quarter of a drive
    /// cycle apart are captioned differently, and a field with no drive keeps a short caption.
    /// </summary>
    [Fact]
    public void TheCaptionTellsApartInstantsAQuarterCycleApart()
    {
        // A 1 MHz drive: a period of one microsecond.
        Assert.Equal("t=100 µs", SurfaceCommand.Instant(100.0, 1.0));
        Assert.Equal("t=100.25 µs", SurfaceCommand.Instant(100.25, 1.0));
        Assert.NotEqual(SurfaceCommand.Instant(100.0, 1.0), SurfaceCommand.Instant(100.25, 1.0));

        // To a thousandth of the period and no further, in whichever unit is shown.
        Assert.Equal("t=100.251 µs", SurfaceCommand.Instant(100.2512, 1.0));
        Assert.Equal("t=15.00025 ms", SurfaceCommand.Instant(15000.25, 1.0));
        Assert.Equal("t=20.000000025 s", SurfaceCommand.Instant(20000000.025, 0.1));
        Assert.Equal("t=0 µs", SurfaceCommand.Instant(0.0, 1.0));

        // A slow drive needs no more digits than it has phases to tell apart.
        Assert.Equal("t=100 µs", SurfaceCommand.Instant(100.25, 1e4));

        // No drive, no phase: three figures, as before.
        Assert.Equal("t=100 µs", SurfaceCommand.Instant(100.25));
        Assert.Equal("t=2.5 µs", SurfaceCommand.Instant(2.5));

        // And it is the drive's period the picture is captioned with: the shipped quadrupole.
        var quadrupole = Model("template", "quadrupole-rf");
        var period = ((Einzel.Fields.ITimeVaryingField)FieldProbe.Open(quadrupole).Field).ShortestPeriodSeconds * 1e6;

        Assert.NotEqual(SurfaceCommand.Instant(100.0, period), SurfaceCommand.Instant(100.0 + (period / 4.0), period));
    }

    /// <summary>
    /// The vertical scale stands clear of the surface in every named view, at the same corner
    /// however the last bit of a cosine falls.
    /// </summary>
    /// <remarks>
    /// Its gap was always along the across axis, which from the front points at the viewer: the
    /// gap was no pixels at all and the bar stood on the surface's edge. And from the front two
    /// corners are equally far left, so which one was chosen hung on cos(90 degrees) being
    /// 6e-17.
    /// </remarks>
    [Fact]
    public void TheScaleStandsClearOfTheSurfaceFromTheFrontAndTheSide()
    {
        var (surface, _, _) = SurfaceCommand.Sample(
            Model("template", "einzel-lens"), new SurfaceRequest(Plane: "xy", Samples: 41));

        var (a0, a1) = (surface.Across[0], surface.Across[^1]);
        var (d0, d1) = (surface.DeepSign * surface.Deep[0], surface.DeepSign * surface.Deep[^1]);
        var corners = new[] { (a0, 0.0, d0), (a0, 0.0, d1), (a1, 0.0, d0), (a1, 0.0, d1) };

        foreach (var view in new[] { "front", "side", "iso" })
        {
            var (azimuth, elevation) = SurfaceCommand.Views[view];
            var foot = SurfaceCommand.ScaleFoot(surface, azimuth, elevation);

            // On a picture 1600 pixels wide, framed on the base and the foot together.
            var matrix = Framing.Around([.. corners, (foot.Across, 0.0, foot.Z)], azimuth, elevation).Matrix(1.6);
            double Px(double x, double z) => ((matrix[0] * x) + (matrix[8] * z) + matrix[12] + 1.0) * 800.0;

            var gap = corners.Min(c => Px(c.Item1, c.Item3)) - Px(foot.Across, foot.Z);
            output.WriteLine($"{view}: foot at ({foot.Across:G6}, {foot.Z:G6}), {gap:F1} px left of the surface");

            Assert.True(gap > 20.0, $"{view}: the scale stands {gap:F1} px from the surface's edge");

            // The same corner a hair either side of the view's angle: no coin toss.
            foreach (var nudge in new[] { -1e-9, 1e-9 })
            {
                var near = SurfaceCommand.ScaleFoot(surface, azimuth + nudge, elevation);

                Assert.Equal(foot.Across, near.Across, 9);
                Assert.Equal(foot.Z, near.Z, 9);
            }
        }
    }

    /// <summary>
    /// A frame measured once and extended is the frame of all the points measured together, to
    /// the bit.
    /// </summary>
    /// <remarks>
    /// The label fit re-framed every drawn vertex on each of up to twelve passes; it now
    /// measures them once and adds the scale's few points per pass. The picture must not move.
    /// </remarks>
    [Fact]
    public void AFrameExtendedIsTheFrameOfEverythingTogether()
    {
        var random = new Random(7);
        (double, double, double) Point() => ((random.NextDouble() * 80) - 40, (random.NextDouble() * 9) - 3, (random.NextDouble() * 50) - 25);

        var drawn = Enumerable.Range(0, 5000).Select(_ => Point()).ToList();

        foreach (var (azimuth, elevation) in SurfaceCommand.Views.Values)
        {
            var once = Framing.Extent.Of(drawn, azimuth, elevation);

            for (var pass = 0; pass < 4; pass++)
            {
                var extra = Enumerable.Range(0, 5).Select(_ => Point()).Select(p => (p.Item1 * 1.5, p.Item2 * 1.5, p.Item3 * 1.5)).ToList();
                var together = Framing.Around(drawn.Concat(extra), azimuth, elevation);

                Assert.Equal(together, once.Including(extra).Frame());
                Assert.Equal(together.Matrix(1.6), once.Including(extra).Frame().Matrix(1.6));
            }

            // Including leaves the first measurement as it was.
            Assert.Equal(Framing.Around(drawn, azimuth, elevation), once.Frame());
        }
    }

    /// <summary>
    /// The rule that a conductor counts only where its element was solved holds in a model of
    /// any size.
    /// </summary>
    /// <remarks>
    /// The per-element domain flags lived on the stack up to 256 elements, and above that the
    /// rule was skipped and every conductor counted everywhere. Here the slit's jaw runs past
    /// its solve, and 300 bounded analytic elements of no field take the model past the cap.
    /// </remarks>
    [Fact]
    public void TheDomainRuleHoldsPastTwoHundredAndFiftySixElements()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();
        var jaw = fields[0]!["solve"]!["electrodes"]!.AsArray();
        jaw[0]!["maxX"]!["value"] = 30.0;
        jaw[0]!["minX"]!["value"] = 10.0;

        for (var i = 0; i < 300; i++)
        {
            fields.Add(System.Text.Json.Nodes.JsonNode.Parse(string.Create(System.Globalization.CultureInfo.InvariantCulture, $$"""
            { "type": "uniform", "field": { "value": [0, 0, 0], "unit": "V/m" },
              "region": { "minX": { "value": {{1000 + (2 * i)}}, "unit": "mm" }, "maxX": { "value": {{1001 + (2 * i)}}, "unit": "mm" },
                          "minY": { "value": 0, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                          "minZ": { "value": 0, "unit": "mm" }, "maxZ": { "value": 1, "unit": "mm" } } }
            """)));
        }

        // And one unbounded element, so a point past the solve is not outside every element.
        fields.Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "type": "uniform", "field": { "value": [0, 0, 0], "unit": "V/m" } }"""));

        var path = Path.Combine(_root, "many-elements.json");
        File.WriteAllText(path, node.ToJsonString());

        var probe = FieldProbe.Open(path);
        Assert.True(probe.Elements.Count > 256);

        var past = probe.At(0, new Einzel.Core.Geometry.Vec3(25.0, 5.0, 0.0), 0.0);
        var within = probe.At(0, new Einzel.Core.Geometry.Vec3(15.0, 5.0, 0.0), 0.0);

        output.WriteLine($"past the solve: {past.Status} ({past.Note}); within: {within.Status} {within.Conductor}");

        // In the jaw's declared shape past its solve: outside what any solve holds for that metal,
        // whatever the other elements cover - never ok.
        Assert.Equal(ProbeStatus.OutsideDomain, past.Status);
        Assert.Null(ElementAt(probe, new Einzel.Core.Geometry.Vec3(25.0, 5.0, 0.0)));
        Assert.Equal(ProbeStatus.InsideConductor, within.Status);
    }

    /// <summary>
    /// A probe row's status, distance and conductor agree: a point inside a shape declared past
    /// its own solve, in another solve's domain, is not reported "ok" beside a negative distance
    /// to the shape it is "in".
    /// </summary>
    [Fact]
    public void AProbeRowsStatusDistanceAndConductorAgree()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();
        var jaw = fields[0]!["solve"]!["electrodes"]!.AsArray();
        jaw[0]!["minX"]!["value"] = 10.0;
        jaw[0]!["maxX"]!["value"] = 60.0;

        var second = fields[0]!.DeepClone();
        second["solve"]!["minX"]!["value"] = 20.0;
        second["solve"]!["maxX"]!["value"] = 60.0;
        var block = second["solve"]!["electrodes"]!.AsArray();
        block.RemoveAt(1);
        block[0]!["name"] = "block";
        block[0]!["minX"]!["value"] = 30.0;
        block[0]!["maxX"]!["value"] = 40.0;
        block[0]!["minY"]!["value"] = 4.0;
        block[0]!["maxY"]!["value"] = 6.0;
        fields.Add(second);
        node["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");

        var path = Path.Combine(_root, "row-agrees.json");
        File.WriteAllText(path, node.ToJsonString());

        using var json = Json("probe", path, "--at-mm", "25,5,0");
        var row = json.RootElement.GetProperty("samples")[0];

        output.WriteLine(row.ToString());

        // Inside the jaw's declared shape past its own solve, in the other solve's domain: not ok,
        // and the note says whose shape it is.
        Assert.Equal("outside-domain", row.GetProperty("status").GetString());
        Assert.Equal("block", row.GetProperty("conductor").GetString());
        Assert.True(row.GetProperty("conductorDistanceMm").GetDouble() >= 0.0);
        Assert.Contains("'slitUpper'", row.GetProperty("note").GetString(), StringComparison.Ordinal);

        // In the block, everything says so; in vacuum, no note.
        var probe = FieldProbe.Open(path);
        var inBlock = probe.At(0, new Einzel.Core.Geometry.Vec3(35.0, 5.0, 0.0), 0.0);
        Assert.Equal(ProbeStatus.InsideConductor, inBlock.Status);
        Assert.True(inBlock.ConductorDistanceMm < 0.0);
        Assert.Null(probe.At(0, new Einzel.Core.Geometry.Vec3(25.0, -0.5, 0.0), 0.0).Note);
    }

    /// <summary>
    /// A conductor one element declares past its own solve does not hide the conductor another
    /// element really has at that point.
    /// </summary>
    /// <remarks>
    /// The first solve's jaw is declared running from 10 to 60 mm, though the solve stops at
    /// 20; the second solve, from 20 to 60 mm, has a block at 30 to 40. At (35, 5) the point is
    /// deep inside the jaw's declared shape and less deep inside the block, so a search for the
    /// nearest conductor that applied the domain test afterwards found the jaw, rejected it,
    /// and reported free space where the block is.
    /// </remarks>
    [Fact]
    public void AShapePastItsSolveDoesNotHideAnotherSolvesConductor()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();
        var jaw = fields[0]!["solve"]!["electrodes"]!.AsArray();
        jaw[0]!["minX"]!["value"] = 10.0;
        jaw[0]!["maxX"]!["value"] = 60.0;

        var second = fields[0]!.DeepClone();
        second["solve"]!["minX"]!["value"] = 20.0;
        second["solve"]!["maxX"]!["value"] = 60.0;
        var block = second["solve"]!["electrodes"]!.AsArray();
        block.RemoveAt(1);
        block[0]!["name"] = "block";
        block[0]!["minX"]!["value"] = 30.0;
        block[0]!["maxX"]!["value"] = 40.0;
        block[0]!["minY"]!["value"] = 4.0;
        block[0]!["maxY"]!["value"] = 6.0;
        block[0]!["potential"]!["value"] = 25.0;
        fields.Add(second);
        node["detector"]!["planePoint"]!["value"] = System.Text.Json.Nodes.JsonNode.Parse("[59, 0, 0]");

        var path = Path.Combine(_root, "shape-past-solve.json");
        File.WriteAllText(path, node.ToJsonString());

        var probe = FieldProbe.Open(path);
        var row = probe.At(0, new Einzel.Core.Geometry.Vec3(35.0, 5.0, 0.0), 0.0);

        output.WriteLine($"(35, 5): {row.Status} {row.Conductor}");

        Assert.Equal(ProbeStatus.InsideConductor, row.Status);
        Assert.Equal("block", row.Conductor);
        Assert.Equal(1, ElementAt(probe, new Einzel.Core.Geometry.Vec3(35.0, 5.0, 0.0)));

        var (surface, _, _) = SurfaceCommand.Sample(path, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 60, 10], Samples: 161));
        var plateau = surface.Plateaus.Single(p => p.Name == "block");
        Assert.Equal(25.0, plateau.PotentialV);
        Assert.True(plateau.Samples > 20);
    }

    /// <summary>
    /// The surface sampled across many threads is byte for byte the surface sampled on one:
    /// the picture, the outcome and the CSV.
    /// </summary>
    [Fact]
    public void SamplingInParallelChangesNothing()
    {
        foreach (var (kind, name, request) in new (string, string, SurfaceRequest)[]
        {
            ("template", "quadrupole", new SurfaceRequest(Samples: 161)),
            ("template", "quadrupole-rf", new SurfaceRequest(Samples: 161, CycleMean: true)),
            ("template", "einzel-lens", new SurfaceRequest(Samples: 161, AtUs: 0.0)),
        })
        {
            var model = Model(kind, name);
            var results = new List<(byte[] Png, string Json, string Csv)>();

            foreach (var parallelism in new int?[] { 1, null })
            {
                var png = Path.Combine(_root, $"{name}-{parallelism?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "all"}.png");
                var csv = Path.ChangeExtension(png, ".csv");
                var outcome = SurfaceCommand.Execute(
                    model, new Einzel.Project.ProjectLayout(_root), request, width: 400, height: 260,
                    outputPath: png, csvPath: csv, maxParallelism: parallelism);

                results.Add((File.ReadAllBytes(png), CommandJson.Write(outcome with { Artifact = "", Csv = "" }), File.ReadAllText(csv)));
            }

            Assert.Equal(results[0].Png, results[1].Png);
            Assert.Equal(results[0].Json, results[1].Json);
            Assert.Equal(results[0].Csv, results[1].Csv);
        }
    }

    /// <summary>
    /// Which of two overlapping conductors a point is credited to does not change when an RF
    /// element is added to the model.
    /// </summary>
    /// <remarks>
    /// A static sum took the member whose conductor the point is deepest inside; a driven sum
    /// took the first member containing it. Here the point is just inside the slit's jaw and
    /// well inside a block a second solve declares over it, so the static model credited the
    /// block and, once an ideal RF quadrupole was summed in, the driven one credited the jaw.
    /// </remarks>
    [Fact]
    public void AddingAnRfElementDoesNotChangeWhichConductorIsCredited()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        var fields = node["fields"]!.AsArray();
        var second = fields[0]!.DeepClone();
        var block = second["solve"]!["electrodes"]!.AsArray();
        block.RemoveAt(1);
        block[0]!["name"] = "block";
        block[0]!["minX"]!["value"] = -2.0;
        block[0]!["maxX"]!["value"] = 3.0;
        block[0]!["minY"]!["value"] = 0.5;
        block[0]!["maxY"]!["value"] = 5.0;
        block[0]!["potential"]!["value"] = 25.0;
        fields.Add(second);

        var still = Path.Combine(_root, "overlap-static.json");
        File.WriteAllText(still, node.ToJsonString());

        fields.Add(System.Text.Json.Nodes.JsonNode.Parse(
            """
            { "type": "idealQuadrupoleRf",
              "directPotential": { "value": 0.0, "unit": "V" },
              "driveAmplitude": { "value": 50.0, "unit": "V" },
              "driveFrequency": { "value": 1.0, "unit": "MHz" },
              "inscribedRadius": { "value": 50.0, "unit": "mm" } }
            """));

        var driven = Path.Combine(_root, "overlap-driven.json");
        File.WriteAllText(driven, node.ToJsonString());

        var point = new Einzel.Core.Geometry.Vec3(0.5, 1.05, 0.0);
        var a = FieldProbe.Open(still);
        var b = FieldProbe.Open(driven);

        Assert.Equal(1, ElementAt(a, point));
        Assert.Equal(ElementAt(a, point), ElementAt(b, point));
        Assert.Equal(a.At(0, point, 0.0).Conductor, b.At(0, point, 0.0).Conductor);
        Assert.Equal("block", b.At(0, point, 0.0).Conductor);

        // What the transport names when an ion strikes there is NOT this rule, and is left as
        // it was: the driven sum names the first member containing the point, the jaw. Loss
        // itemisation (ACC-5) is not changed under a drawing; the probe picks by its own rule.
        var (field, _, _) = Einzel.Fields.FieldAssembly.BuildMapped(b.Model);
        var sum = Assert.IsType<Einzel.Fields.DrivenSuperposedField>(field);
        var metres = FieldProbe.MillimetresToMetres(point);
        Assert.Equal("slitUpper", sum.ConductorAt(in metres));

        // Under a cycle mean the held value comes from the field held at the operating point,
        // piece by piece, and each piece is still the element it was: the slit's jaws, the block
        // and the RF quadrupole are three pieces, and a held field whose pieces came out in
        // another order would ask the RF quadrupole what the block holds.
        var (mean, _, _) = SurfaceCommand.Sample(driven, new SurfaceRequest(Plane: "xy", BoxMm: [-20, -10, 20, 10], Samples: 81, CycleMean: true));
        Assert.Equal(25.0, mean.Plateaus.Single(p => p.Name == "block").PotentialV, 12);
        Assert.Equal(0.0, mean.Plateaus.Single(p => p.Name == "slitLower").PotentialV, 12);
    }

    /// <summary>
    /// <c>render section</c> checks the instant it draws at, whether it came from the flag or
    /// from the render spec, and refuses one too late to place in the drive's cycle.
    /// </summary>
    [Fact]
    public void ASectionChecksItsInstantHoweverItArrives()
    {
        var model = Model("template", "quadrupole-rf");

        string Spec(string name, double atSeconds)
        {
            var path = Path.Combine(_root, name + ".render.json");
            File.WriteAllText(path, string.Create(System.Globalization.CultureInfo.InvariantCulture, $$"""
            {
              "renderSpecVersion": "0.1",
              "model": {{System.Text.Json.JsonSerializer.Serialize(model)}},
              "atSeconds": {{atSeconds:R}}
            }
            """));
            return path;
        }

        // Each refusal names and locates where the instant came from: the spec's own atSeconds,
        // or the --at-us flag that overrode it.
        foreach (var (args, path, words) in new (string[], string, string)[]
        {
            (["render", "section", Spec("before-zero", -1e-6), "--dry-run"], "/atSeconds", "atSeconds"),
            (["render", "section", Spec("too-late", 1e294), "--dry-run"], "/atSeconds", "too late"),
            (["render", "section", model, "--at-us", "1e300", "--dry-run"], "/render/at-us", "too late"),
            (["render", "section", model, "--at-us", "-1", "--dry-run"], "/render/at-us", "--at-us"),
            (["render", "section", Spec("overridden", -1e-6), "--at-us", "-2", "--dry-run"], "/render/at-us", "--at-us"),
        })
        {
            var (code, stdout, stderr) = Cli(args);
            output.WriteLine($"{Path.GetFileName(args[2])} {string.Join(' ', args[3..])}: exit {code}: {stderr.Trim()}");

            Assert.Equal(1, code);
            Assert.Contains("at " + path + ":", stdout + stderr, StringComparison.Ordinal);
            Assert.Contains(words, stdout + stderr, StringComparison.Ordinal);
        }

        Assert.Equal(0, Cli("render", "section", Spec("placeable", 2.5e-6), "--dry-run").ExitCode);
    }

    /// <summary>A bad explicit height scale is reported at the flag that was given.</summary>
    [Fact]
    public void ABadHeightScaleIsReportedAtItsOwnFlag()
    {
        var model = Model("template", "quadrupole");

        var ev = Cli("render", "surface", model, "--height", "energy", "--height-mm-per-ev", "-1", "--samples", "21", "--dry-run");
        var v = Cli("render", "surface", model, "--height-mm-per-v", "-1", "--samples", "21", "--dry-run");

        Assert.Equal(1, ev.ExitCode);
        Assert.Contains("/args/--height-mm-per-ev", ev.Stdout + ev.Stderr, StringComparison.Ordinal);
        Assert.Equal(1, v.ExitCode);
        Assert.Contains("/args/--height-mm-per-v", v.Stdout + v.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// An analytic RF quadrupole drawn when its drive is through zero is rounding, said to be,
    /// because its declared amplitude counts toward what the model deals in.
    /// </summary>
    /// <remarks>
    /// <c>rf-quadrupole-confined</c> declares 200 V of drive and nothing solved, and launches its
    /// ion at a millivolt. At a quarter period the potential everywhere is the rounding of a
    /// cosine at its zero; the peak used to count solved electrodes alone, so the reference was
    /// that millivolt and the rounding was stretched to the full relief.
    /// </remarks>
    [Fact]
    public void AnAnalyticQuadrupoleAtItsZeroIsDrawnFlat()
    {
        var model = Model("example", "rf-quadrupole-confined");

        Assert.Equal(200.0, SurfaceCommand.PeakElectrodePotential(FieldProbe.Open(model).Model), 9);

        using var json = Json("render", "surface", model, "--box-mm", "-2,-2,2,2", "--at-us", "0.25", "--samples", "41", "--dry-run");
        var span = Number(json, "highest") - Math.Min(Number(json, "lowest"), 0.0);

        output.WriteLine($"span {span:G3} V at {Number(json, "heightMmPerUnit"):G4} mm per V");

        Assert.True(span < 1e-9, "the control: at a quarter period the field is rounding");
        Assert.Contains("render.surface-flat", Codes(json));
        Assert.True(span * Number(json, "heightMmPerUnit") < 1e-3);
    }

    /// <summary>The CSV and the picture may not be one file: refused before anything is drawn.</summary>
    [Fact]
    public void TheCsvAndThePictureMustBeDifferentFiles()
    {
        var model = Model("template", "quadrupole");
        var target = Path.Combine(_root, "both.png");

        var (code, _, stderr) = Cli("render", "surface", model, "--samples", "21", "--out", target, "--csv", target);
        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains("/args/--csv", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(target));

        var other = Cli("render", "surface", model, "--samples", "21", "--out", target, "--csv", Path.Combine(_root, "BOTH.csv"));
        Assert.Equal(0, other.ExitCode);
    }

    /// <summary>
    /// An instant before the clock starts is refused by every verb that takes one, and one too
    /// late to place in the drive's cycle is refused by the surface and the probe.
    /// </summary>
    [Fact]
    public void AnInstantThatCannotBeDrawnIsRefused()
    {
        var quadrupole = Model("template", "quadrupole-rf");
        var sequenced = Model("example", "sequenced-extraction");

        foreach (var (args, path) in new (string[] Args, string Path)[]
        {
            (["render", "surface", sequenced, "--at-us", "-1", "--samples", "21", "--dry-run"], "/args/--at-us"),
            (["render", "section", sequenced, "--at-us", "-1", "--dry-run"], "/render/at-us"),
            (["probe", sequenced, "--at-mm", "0,0,0", "--at-us", "-1"], "/probe/at-us"),
            (["render", "surface", quadrupole, "--at-us", "1e300", "--samples", "21", "--dry-run"], "/args/--at-us"),
            (["probe", quadrupole, "--at-mm", "0,0,0", "--at-us", "1e300"], "/probe/at-us"),
        })
        {
            var (code, stdout, stderr) = Cli(args);
            output.WriteLine($"{string.Join(' ', args[..2])} {args[^4..].Last()}: exit {code}: {stderr.Trim()}");

            Assert.Equal(1, code);
            Assert.Contains(path, stdout + stderr, StringComparison.Ordinal);
        }

        // Late but placeable is fine: 1e6 us is a whole number of 1 us cycles.
        Assert.Equal(0, Cli("render", "surface", quadrupole, "--at-us", "1000000", "--samples", "21", "--dry-run").ExitCode);

        Assert.Equal("t=2.5 µs", SurfaceCommand.Instant(2.5));
        Assert.Equal("t=15 ms", SurfaceCommand.Instant(1.5e4));
        Assert.Equal("t=1e+294 s", SurfaceCommand.Instant(1e300));
    }

    /// <summary>The probe may be sampled from several threads at several instants at once.</summary>
    [Fact]
    public void TheProbeAnswersTheSameFromManyThreads()
    {
        var probe = FieldProbe.Open(Model("template", "quadrupole-rf"));
        var point = FieldProbe.MillimetresToMetres(new Einzel.Core.Geometry.Vec3(1.0, 0.5, 0.0));
        var instants = Enumerable.Range(0, 64).Select(i => i * 0.0173e-6).ToArray();

        double Mean(double at)
        {
            probe.SamplePotential(in point, at, cycleMean: true, out var v, out _, out _, out _, out _);
            return v;
        }

        var sequential = instants.Select(Mean).ToArray();
        var parallel = new double[instants.Length];

        Parallel.For(0, instants.Length * 20, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n =>
        {
            var i = n % instants.Length;
            parallel[i] = Mean(instants[i]);
        });

        Assert.Equal(sequential, parallel);
    }

    /// <summary>
    /// The wire mesh is one polyline per mesh line, and draws exactly what a line per segment
    /// drew.
    /// </summary>
    [Fact]
    public void TheWireMeshIsPolylinesThatDrawWhatSegmentsDrew()
    {
        var (surface, _, _) = SurfaceCommand.Sample(Model("template", "quadrupole"), new SurfaceRequest(Samples: 121));
        var mesh = SurfaceCommand.Compose(surface, SurfaceCommand.Scale(surface, new SurfaceRequest()), 40)
            .Single(l => l.Name == "mesh").Lines.Where(l => l.SegmentColors is not null).ToList();

        var segments = new List<Einzel.Render.RasterLine>();

        foreach (var line in mesh)
        {
            for (var s = 0; 3 * (s + 1) < line.PointsMm.Count; s++)
            {
                var c = line.SegmentColors!;
                segments.Add(new Einzel.Render.RasterLine(
                    [.. line.PointsMm.Skip(3 * s).Take(6)], c[3 * s], c[(3 * s) + 1], c[(3 * s) + 2]));
            }
        }

        output.WriteLine($"{mesh.Count} polylines for {segments.Count} segments");
        Assert.True(mesh.Count * 20 < segments.Count, "the polylines are not one per segment");

        var points = mesh.SelectMany(l => Triples(l.PointsMm)).ToList();
        var matrix = Framing.Around(points, -35.0, -32.0).Matrix(1.5);
        var lighting = new Einzel.Render.RasterLighting((_, _, _) => 1.0, 1.0);

        byte[] Draw(IReadOnlyList<Einzel.Render.RasterLine> lines) => Einzel.Render.Rasterizer.Draw(
            [new Einzel.Render.RasterLayer([], lines, 1.0, false)], matrix, lighting, (1.0, 1.0, 1.0), 600, 400, lineWidth: 1.2);

        var polylines = Draw([.. mesh.Select(l => new Einzel.Render.RasterLine(l.PointsMm, l.R, l.G, l.B, l.SegmentColors))]);
        Assert.Equal(Draw(segments), polylines);
        Assert.Contains(polylines, b => b < 200);
    }

    // The element the conductor a point is in belongs to, as the surface asks the probe for it.
    private static int? ElementAt(FieldProbe probe, Einzel.Core.Geometry.Vec3 positionMm)
    {
        var metres = FieldProbe.MillimetresToMetres(positionMm);
        probe.SamplePotential(in metres, 0.0, cycleMean: false, out _, out _, out _, out var element, out _);

        return element;
    }

    /// <summary>
    /// Outside every solve, in a model that also sums an unbounded analytic field, a probe row
    /// names the nearest declared conductor and its distance, as main does.
    /// </summary>
    /// <remarks>
    /// An analytic element has no conductors, so it does not make a point "inside a domain"
    /// for the purpose of naming one. The sixth review's rule asked "is the point outside every
    /// element", which an unbounded analytic element never lets it be, and the row came back
    /// with no conductor and no distance.
    /// </remarks>
    [Fact]
    public void OutsideTheSolveARowStillNamesTheNearestConductor()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Model("example", "slit-transmission")))!;
        node["fields"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "type": "uniform", "field": { "value": [100.0, 0, 0], "unit": "V/m" } }"""));
        var path = Path.Combine(_root, "slit-plus-field.json");
        File.WriteAllText(path, node.ToJsonString());

        using var json = Json("probe", path, "--at-mm", "30,5,0");
        var row = json.RootElement.GetProperty("samples")[0];

        Assert.Equal("ok", row.GetProperty("status").GetString());
        Assert.Equal("slitUpper", row.GetProperty("conductor").GetString());
        Assert.Equal(29.0, row.GetProperty("conductorDistanceMm").GetDouble(), 9);
    }

    /// <summary>A dry run reports exactly what a real run does, without drawing the picture.</summary>
    [Fact]
    public void ADryRunReportsWhatARunDoes()
    {
        foreach (var (name, request) in new[] { ("quadrupole", new SurfaceRequest(Samples: 81)), ("quadrupole-rf", new SurfaceRequest(Samples: 81, CycleMean: true)) })
        {
            var model = Model("template", name);
            var png = Path.Combine(_root, name + "-dry.png");
            var dry = SurfaceCommand.Execute(model, new Einzel.Project.ProjectLayout(_root), request, width: 300, height: 200, outputPath: png, dryRun: true);
            var wet = SurfaceCommand.Execute(model, new Einzel.Project.ProjectLayout(_root), request, width: 300, height: 200, outputPath: png);

            Assert.False(dry.Written);
            Assert.Equal(CommandJson.Write(wet with { Written = false }), CommandJson.Write(dry));
        }
    }

    /// <summary>A value that is not a number has no label, rather than the label 0.</summary>
    [Fact]
    public void ANonFiniteValueHasNoLabel()
    {
        Assert.Equal(string.Empty, SurfaceCommand.Format(double.NaN));
        Assert.Equal(string.Empty, SurfaceCommand.Format(double.PositiveInfinity, 20.0));
        Assert.Equal("0", SurfaceCommand.Format(0.0));
        Assert.Equal("2.5", SurfaceCommand.Format(2.5));
    }

    /// <summary>The mesh-line counts reported are those of the mesh drawn.</summary>
    [Fact]
    public void TheReportedMeshLinesAreTheOnesDrawn()
    {
        var model = Model("template", "planar-mirror-pair");
        var request = new SurfaceRequest(Samples: 121, MeshLines: 30);
        var outcome = SurfaceCommand.Execute(model, new Einzel.Project.ProjectLayout(_root), request, width: 300, height: 200, dryRun: true);
        var (surface, _, _) = SurfaceCommand.Sample(model, request);
        SurfaceCommand.Compose(surface, outcome.HeightMmPerUnit, request.MeshLines, out var drawn);

        Assert.Equal(drawn, outcome.MeshLines);
        Assert.True(drawn[1] > 1, "the shorter side has mesh lines of its own");
    }

    /// <summary>Each plateau is drawn over its own vertices, not every vertex of the surface.</summary>
    /// <remarks>
    /// Handed the whole grid, a plateau's mesh was walked whole by the framing and the
    /// rasterizer once per conductor. The pictures are the same either way, which a byte
    /// comparison of three templates against the previous build confirmed.
    /// </remarks>
    [Fact]
    public void APlateauIsDrawnOverItsOwnVertices()
    {
        var (surface, _, _) = SurfaceCommand.Sample(Model("template", "quadrupole"), new SurfaceRequest(Samples: 121));
        var plateaus = SurfaceCommand.Compose(surface, 0.05, 40).Single(l => l.Name == "plateaus").Meshes;

        Assert.Equal(4, plateaus.Count);

        foreach (var mesh in plateaus)
        {
            var used = mesh.Triangles.Distinct().Count();

            Assert.Equal(used, mesh.VerticesMm.Count / 3);
            Assert.Equal(mesh.VerticesMm.Count, mesh.Normals.Count);
            Assert.True(used < surface.Values.Count / 4);
        }
    }

    /// <summary>
    /// A node beside a hole in the surface is lit by the slope on its finite side, not as flat.
    /// </summary>
    [Fact]
    public void ANodeBesideAHoleKeepsItsSlope()
    {
        var surface = new PotentialSurface
        {
            Plane = "xy",
            OffsetMm = 0.0,
            AcrossAxis = "x",
            DeepAxis = "y",
            Across = [0.0, 1.0, 2.0],
            Deep = [0.0, 1.0, 2.0],
            Values = [0, double.NaN, 0, 10, 10, 10, 30, 30, 30],
            Inside = new string?[9],
            Plateaus = [],
            Height = SurfaceHeight.Potential,
            Unit = "V",
            OutsideDomain = 0,
            Refused = 1,
        };

        var normals = SurfaceCommand.Compose(surface, 1.0, 2)[0].Meshes[0].Normals;

        // Node (1, 1): across, the neighbour at x = 0 is the hole; the slope toward x = 2 is 20.
        Assert.Equal(-20.0, normals[3 * ((1 * 3) + 1)], 12);

        // The control: a node with both neighbours finite has its central slope, (30 - 0) / 2.
        Assert.Equal(-15.0, normals[3 * ((1 * 3) + 0)], 12);
    }

    /// <summary>
    /// A line whose segment colors do not fit its segments is refused before anything is drawn,
    /// naming its layer - however the line was made.
    /// </summary>
    /// <remarks>
    /// Checked once, where every drawn line passes. The first check was in two constructors with
    /// two copies of the arithmetic, and a <c>with</c> copy runs no initializer, so the third
    /// case here - a good line given bad colors afterwards - went through both.
    /// </remarks>
    [Fact]
    public void SegmentColorsThatDoNotFitAreRefusedBeforeAnythingIsDrawn()
    {
        static byte[] Draw(Einzel.Render.RasterLine line, string? name = "mesh") => Einzel.Render.Rasterizer.Draw(
            [new Einzel.Render.RasterLayer([], [line], 1.0, true, Name: name)],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
            new Einzel.Render.RasterLighting((_, _, _) => 1.0, 1.0),
            (1.0, 1.0, 1.0),
            16,
            16);

        var good = new Einzel.Render.RasterLine([0, 0, 0, 1, 1, 1, 2, 2, 2], 0, 0, 0, [1, 1, 1, 0, 0, 0]);

        // Made wrong.
        var made = Assert.Throws<ArgumentException>(
            () => Draw(new Einzel.Render.RasterLine(good.PointsMm, 0, 0, 0, [1, 1, 1])));
        Assert.Contains("layer 'mesh', line 0", made.Message, StringComparison.Ordinal);
        Assert.Contains("3 segment color values for 2 segments", made.Message, StringComparison.Ordinal);

        // Made right and copied wrong, which no constructor sees.
        var copied = Assert.Throws<ArgumentException>(() => Draw(good with { SegmentColors = [1, 1] }));
        Assert.Contains("layer 'mesh'", copied.Message, StringComparison.Ordinal);

        // A layer with no name is named by its place.
        Assert.Contains("layer 0, line 0", Assert.Throws<ArgumentException>(() => Draw(good with { SegmentColors = [1] }, null)).Message, StringComparison.Ordinal);

        // The control: three numbers for each of two segments is a line, and is drawn.
        Assert.NotEmpty(Draw(good));
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
