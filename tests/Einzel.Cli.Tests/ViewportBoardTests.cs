using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Printed boards and reflected solves in the viewport, which is what the memo's own mirror
/// pair is made of - and it was drawn as one end cap and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two gaps in one template, and they hid each other.</b> An edge profile has no interior,
/// so the level-set extraction every other shape goes through found no surface, and the two
/// mirror boards were missing. And a solve declaring <c>reflectAboutX</c> is composed with its
/// mirror image by the field builder but was drawn as the declared half alone, so a mirror
/// PAIR was drawn with one mirror. The only two templates using either feature are the two
/// whose whole subject is the pair, which is why nothing else ever showed it.
/// </para>
/// <para>
/// Every expectation below is taken from the template's own declared numbers - a mirror
/// 90 mm deep, 30 mm between its boards, 767 mm cap to cap, the profile's three knots - so
/// the check is arithmetic the viewport had no part in.
/// </para>
/// </remarks>
public sealed class ViewportBoardTests(ITestOutputHelper output) : IDisposable
{
    // The planar mirror pair as shipped, in millimetres and volts.
    private const double CapX = -90.0;
    private const double FirstStageX = -31.5;
    private const double MidPlane = 383.5;
    private const double HalfGap = 15.0;
    private const double CapVolts = 4800.0;
    private const double FirstStageVolts = 2800.0;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-viewport-board", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Each board is drawn across its whole edge, on the edge.</summary>
    /// <remarks>
    /// The whole edge rather than between its first and last knots, because that is what the
    /// solve holds: <c>RasteriseEdgeProfile</c> fixes every node of the edge, continuing the
    /// end values past the profile's ends.
    /// </remarks>
    [Fact]
    public void EachBoardIsDrawnAcrossItsWholeEdge()
    {
        var outcome = ViewportCommand.Execute(Template("planar-mirror-pair"));

        foreach (var (name, y) in new[] { ("topBoard", HalfGap), ("bottomBoard", -HalfGap) })
        {
            var declared = Half(outcome, name, declared: true);

            Assert.NotEmpty(declared);

            var xs = declared.SelectMany(Xs).ToArray();
            var ys = declared.SelectMany(Ys).ToArray();

            output.WriteLine($"{name}: {declared.Count} pieces, x {xs.Min():F3} to {xs.Max():F3} mm, "
                + $"y {ys.Min():F3} to {ys.Max():F3} mm");

            Assert.Equal(CapX, xs.Min(), 9);
            Assert.Equal(MidPlane, xs.Max(), 9);
            Assert.All(ys, value => Assert.Equal(y, value, 9));
        }
    }

    /// <summary>
    /// Each piece of a board carries the potential the profile gives at its middle, and no
    /// piece straddles a knot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The straddle is the sharper half. A piece spanning the knot where the steep first
    /// stage meets the shallow second would take a potential averaged across a change of
    /// slope - a color no point of the board actually holds - and every value would still
    /// look plausible. So each piece's extent is checked against every knot.
    /// </para>
    /// <para>
    /// The profile is interpolated here from the template's four declared points rather than
    /// by calling the engine's own <c>ProfileAt</c>, so the two cannot agree by sharing a
    /// mistake.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachPieceCarriesTheProfileAtItsMiddle()
    {
        var outcome = ViewportCommand.Execute(Template("planar-mirror-pair"));
        var pieces = Half(outcome, "topBoard", declared: true);

        var worst = 0.0;

        foreach (var piece in pieces)
        {
            var low = Xs(piece).Min();
            var high = Xs(piece).Max();

            foreach (var knot in new[] { FirstStageX, 0.0 })
            {
                Assert.False(
                    low < knot - 1e-9 && high > knot + 1e-9,
                    $"a piece from {low:F3} to {high:F3} mm straddles the knot at {knot} mm");
            }

            var expected = Profile(0.5 * (low + high));
            worst = Math.Max(worst, Math.Abs(piece.PotentialVolts - expected));
        }

        output.WriteLine($"{pieces.Count} pieces, worst departure from the declared profile {worst:E2} V");

        Assert.True(worst < 1e-6, $"a piece departs from the declared profile by {worst:E3} V");

        // And the ramp is actually there: the cap end is near the cap, the field-free end is earth.
        Assert.True(pieces.Max(p => p.PotentialVolts) > 0.95 * CapVolts);
        Assert.Equal(0.0, pieces.Min(p => p.PotentialVolts), 12);
    }

    /// <summary>A reflected solve is drawn with its mirror image, where the field puts it.</summary>
    /// <remarks>
    /// The field is composed as <c>x -> 2p - x</c> about the declared plane, so the second cap
    /// belongs at <c>2 x 383.5 + 90 = 857</c> mm. Asserted as that number rather than as "there
    /// are two caps", because two caps drawn at the same place would pass a count.
    /// </remarks>
    [Fact]
    public void AReflectedSolveIsDrawnWithItsMirrorImage()
    {
        var outcome = ViewportCommand.Execute(Template("planar-mirror-pair"));

        var caps = outcome.Conductors.Where(c => c.Name == "cap").ToList();

        Assert.Equal(2, caps.Count);

        var at = caps.Select(c => Xs(c).Average()).Order().ToArray();

        output.WriteLine($"caps at {at[0]:F3} and {at[1]:F3} mm; mid-plane {0.5 * (at[0] + at[1]):F3}");

        Assert.Equal(CapX, at[0], 9);
        Assert.Equal((2.0 * MidPlane) - CapX, at[1], 9);

        // The image of each board runs from the mid-plane to the far cap.
        var image = Half(outcome, "topBoard", declared: false).SelectMany(Xs).ToArray();

        Assert.Equal(MidPlane, image.Min(), 9);
        Assert.Equal((2.0 * MidPlane) - CapX, image.Max(), 9);

        // Three electrodes declared, drawn twice over and in pieces - and counted as three.
        Assert.Equal(3, outcome.ElectrodeCount());
    }

    /// <summary>The mirror image is wound the same way its original is.</summary>
    /// <remarks>
    /// <para>
    /// <b>A reflection reverses orientation</b>, so an image made by moving the vertices and
    /// nothing else has every triangle wound backwards against its normal - lit from inside
    /// in any renderer that culls or shades by winding. What is asserted is not a convention
    /// but agreement: whatever sense the declared half's triangles take relative to their
    /// normals, the image's must take the same, on every triangle.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMirrorImageIsWoundLikeItsOriginal()
    {
        var outcome = ViewportCommand.Execute(Template("planar-mirror-pair"));

        foreach (var name in new[] { "topBoard", "bottomBoard", "cap" })
        {
            var original = Senses(Half(outcome, name, declared: true));
            var image = Senses(Half(outcome, name, declared: false));

            output.WriteLine($"{name}: declared {original.Positive}+/{original.Negative}-, "
                + $"image {image.Positive}+/{image.Negative}-");

            Assert.True(original.Positive + original.Negative > 0, $"{name} has no oriented triangles");

            // One sense throughout each half, and the same sense in both.
            Assert.True(original.Positive == 0 || original.Negative == 0);
            Assert.Equal(original.Positive > 0, image.Positive > 0);
            Assert.Equal(original.Negative > 0, image.Negative > 0);
        }
    }

    /// <summary>A solve that declares no reflection is drawn once.</summary>
    /// <remarks>The control: without it, a viewport doubling everything would pass the rest.</remarks>
    [Fact]
    public void ASolveWithNoReflectionIsDrawnOnce()
    {
        var outcome = ViewportCommand.Execute(Template("einzel-lens"));

        Assert.Equal(3, outcome.ElectrodeCount());
        Assert.Equal(3, outcome.Conductors.Count);
    }

    private static double Profile(double x) => x switch
    {
        <= CapX => CapVolts,
        <= FirstStageX => CapVolts + ((FirstStageVolts - CapVolts) * (x - CapX) / (FirstStageX - CapX)),
        <= 0.0 => FirstStageVolts * (0.0 - x) / (0.0 - FirstStageX),
        _ => 0.0,
    };

    private static List<ConductorSurface> Half(ViewportOutcome outcome, string name, bool declared) =>
        [.. outcome.Conductors
            .Where(c => c.Name == name)
            .Where(c => declared
                ? Xs(c).Average() < MidPlane
                : Xs(c).Average() > MidPlane)];

    private static IEnumerable<double> Xs(ConductorSurface surface)
    {
        for (var i = 0; i + 2 < surface.VerticesMm.Count; i += 3)
        {
            yield return surface.VerticesMm[i];
        }
    }

    private static IEnumerable<double> Ys(ConductorSurface surface)
    {
        for (var i = 0; i + 2 < surface.VerticesMm.Count; i += 3)
        {
            yield return surface.VerticesMm[i + 1];
        }
    }

    /// <summary>How many triangles are wound with their normal, and how many against it.</summary>
    private static (int Positive, int Negative) Senses(IEnumerable<ConductorSurface> surfaces)
    {
        int positive = 0, negative = 0;

        foreach (var s in surfaces)
        {
            var v = s.VerticesMm;
            var n = s.Normals;

            for (var t = 0; t + 2 < s.Triangles.Count; t += 3)
            {
                int a = s.Triangles[t], b = s.Triangles[t + 1], c = s.Triangles[t + 2];

                double ux = v[(3 * b) + 0] - v[(3 * a) + 0], uy = v[(3 * b) + 1] - v[(3 * a) + 1], uz = v[(3 * b) + 2] - v[(3 * a) + 2];
                double wx = v[(3 * c) + 0] - v[(3 * a) + 0], wy = v[(3 * c) + 1] - v[(3 * a) + 1], wz = v[(3 * c) + 2] - v[(3 * a) + 2];

                double gx = (uy * wz) - (uz * wy), gy = (uz * wx) - (ux * wz), gz = (ux * wy) - (uy * wx);

                var nx = n[(3 * a) + 0] + n[(3 * b) + 0] + n[(3 * c) + 0];
                var ny = n[(3 * a) + 1] + n[(3 * b) + 1] + n[(3 * c) + 1];
                var nz = n[(3 * a) + 2] + n[(3 * b) + 2] + n[(3 * c) + 2];

                var dot = (gx * nx) + (gy * ny) + (gz * nz);

                // A degenerate triangle or one with no normal says nothing either way.
                if (Math.Abs(dot) < 1e-12)
                {
                    continue;
                }

                if (dot > 0.0)
                {
                    positive++;
                }
                else
                {
                    negative++;
                }
            }
        }

        return (positive, negative);
    }

    private string Template(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-template", name).ExitCode);

        return path;
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
}
