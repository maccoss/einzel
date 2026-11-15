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
public sealed class RenderSurfaceTests(ITestOutputHelper output) : IDisposable
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
    public void AMalformedRequestIsRefused(string flag, string value)
    {
        var model = Model("template", "quadrupole");
        var (code, _, stderr) = Cli("render", "surface", model, flag, value, "--dry-run");
        output.WriteLine(stderr);
        Assert.Equal(1, code);
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
