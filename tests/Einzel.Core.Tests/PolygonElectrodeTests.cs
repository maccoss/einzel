using Einzel.Core.Model;
using Xunit.Abstractions;

namespace Einzel.Core.Tests;

/// <summary>
/// The polygon electrode's geometry: its signed distance and first-entry agree with
/// the rectangle's wherever both can describe the shape, it handles a re-entrant
/// outline and either winding, and the overlap check treats a shared edge as tangency.
/// </summary>
/// <remarks>
/// A polygon is the general cross-section, so the sharpest test available is that it
/// reproduces the specialised primitive exactly where they coincide: an axis-aligned
/// square written as four vertices has to give the rectangle's signed distance to the
/// last bit, because the mask and the cut links are built from nothing else.
/// </remarks>
public sealed class PolygonElectrodeTests(ITestOutputHelper output)
{
    private static CompiledElectrode Polygon(string name, params (double X, double Y)[] vertices) =>
        new()
        {
            Name = name,
            Shape = ElectrodeShape.Polygon,
            Vertices = vertices,
            MinX = vertices.Min(v => v.X),
            MinY = vertices.Min(v => v.Y),
            MaxX = vertices.Max(v => v.X),
            MaxY = vertices.Max(v => v.Y),
        };

    private static CompiledElectrode Square(double minX, double minY, double maxX, double maxY, bool reversed = false)
    {
        (double, double)[] v = [(minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)];
        return Polygon("square", reversed ? [.. v.Reverse()] : v);
    }

    private static CompiledElectrode Rectangle(double minX, double minY, double maxX, double maxY) =>
        new()
        {
            Name = "rectangle",
            Shape = ElectrodeShape.Rectangle,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
        };

    /// <summary>A square polygon and the same square as a rectangle agree to the bit, both windings.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASquarePolygonIsTheRectangle(bool reversed)
    {
        var polygon = Square(-2.0e-3, -1.0e-3, 3.0e-3, 4.0e-3, reversed);
        var rectangle = Rectangle(-2.0e-3, -1.0e-3, 3.0e-3, 4.0e-3);

        var random = new Random(20260905);
        var worst = 0.0;

        for (var k = 0; k < 20000; k++)
        {
            var x = (random.NextDouble() * 12.0e-3) - 6.0e-3;
            var y = (random.NextDouble() * 12.0e-3) - 6.0e-3;

            var a = polygon.SignedDistance(x, y);
            var b = rectangle.SignedDistance(x, y);

            worst = Math.Max(worst, Math.Abs(a - b));
            Assert.Equal(b, a, 1e-15);
            Assert.Equal(rectangle.Contains(x, y), polygon.Contains(x, y));
        }

        output.WriteLine($"worst signed-distance disagreement {worst:E2} m over 20000 points ({(reversed ? "reversed" : "forward")} winding)");
    }

    /// <summary>
    /// First entry along axis-aligned segments - the only kind the mask builder asks
    /// about - agrees with the rectangle's, including a segment that starts inside,
    /// one that misses, and one that ends short of the surface.
    /// </summary>
    [Fact]
    public void FirstEntryMatchesTheRectangleAlongGridLinks()
    {
        var polygon = Square(-2.0e-3, -1.0e-3, 3.0e-3, 4.0e-3);
        var rectangle = Rectangle(-2.0e-3, -1.0e-3, 3.0e-3, 4.0e-3);

        var random = new Random(7);
        var entries = 0;
        var misses = 0;

        for (var k = 0; k < 20000; k++)
        {
            var x = (random.NextDouble() * 12.0e-3) - 6.0e-3;
            var y = (random.NextDouble() * 12.0e-3) - 6.0e-3;
            var h = 0.25e-3 * (1 + random.Next(0, 40));
            var (toX, toY) = (random.Next(0, 4)) switch
            {
                0 => (x + h, y),
                1 => (x - h, y),
                2 => (x, y + h),
                _ => (x, y - h),
            };

            var a = polygon.FirstEntry(x, y, toX, toY);
            var b = rectangle.FirstEntry(x, y, toX, toY);

            Assert.Equal(b is null, a is null);

            if (a is { } fa && b is { } fb)
            {
                entries++;
                Assert.Equal(fb, fa, 1e-12);
            }
            else
            {
                misses++;
            }
        }

        output.WriteLine($"{entries} entries and {misses} misses agree");
        Assert.True(entries > 1000 && misses > 1000, "the sample has to contain both");
    }

    /// <summary>
    /// A re-entrant outline: the notch of an L is outside, the arms are inside, and
    /// the distance in the notch is to the nearest inner face.
    /// </summary>
    [Fact]
    public void AReentrantOutlineIsHandled()
    {
        // An L: a 4 x 4 square with the top-right 2 x 2 removed.
        var l = Polygon("L", (0, 0), (4, 0), (4, 2), (2, 2), (2, 4), (0, 4));

        Assert.True(l.Contains(1.0, 1.0));       // the corner arm
        Assert.True(l.Contains(3.0, 1.0));       // the bottom arm
        Assert.True(l.Contains(1.0, 3.0));       // the left arm
        Assert.False(l.Contains(3.0, 3.0));      // the notch

        Assert.Equal(1.0, l.SignedDistance(3.0, 3.0), 12);    // nearest inner face is one away
        Assert.Equal(-1.0, l.SignedDistance(1.0, 1.0), 12);   // one from the outer faces
        Assert.Equal(0.0, l.SignedDistance(2.0, 3.0), 12);    // on the inner face

        // A link from the notch westward enters the left arm at its inner face.
        Assert.Equal(0.5, l.FirstEntry(3.0, 3.0, 1.0, 3.0)!.Value, 12);
        // A link that stays in the notch misses.
        Assert.Null(l.FirstEntry(3.0, 3.0, 3.9, 3.9));
    }

    /// <summary>
    /// A hyperbolic rod face as a polyline: the vertex sits at r0 and the chord
    /// error is the sagitta of one segment, microns on a rod sampled every quarter
    /// millimetre.
    /// </summary>
    [Fact]
    public void AHyperbolicFaceIsResolvedToItsSagitta()
    {
        const double R0 = 4.0e-3;
        const double HalfWidth = 6.0e-3;
        const int Segments = 48;

        var vertices = new List<(double X, double Y)>();
        for (var k = 0; k <= Segments; k++)
        {
            var x = -HalfWidth + (2.0 * HalfWidth * k / Segments);
            vertices.Add((x, Math.Sqrt((R0 * R0) + (x * x))));
        }

        vertices.Add((HalfWidth, 12.0e-3));
        vertices.Add((-HalfWidth, 12.0e-3));

        var rod = Polygon("rod", [.. vertices]);
        var chord = 2.0 * HalfWidth / Segments;
        var sagitta = chord * chord / (8.0 * R0);   // curvature radius at the vertex is r0

        output.WriteLine($"chord {chord * 1e3:F3} mm, sagitta {sagitta * 1e6:F2} um");

        // The face vertex is a polygon vertex, so the distance from the axis is exactly r0.
        Assert.Equal(R0, rod.SignedDistance(0.0, 0.0), 15);

        // Between vertices the polyline lies inside the true hyperbola by at most the sagitta.
        var worst = 0.0;
        for (var k = 0; k < 2000; k++)
        {
            var x = -HalfWidth + (2.0 * HalfWidth * (k + 0.5) / 2000);
            var yTrue = Math.Sqrt((R0 * R0) + (x * x));
            var d = rod.SignedDistance(x, yTrue);
            worst = Math.Max(worst, Math.Abs(d));
        }

        output.WriteLine($"worst departure from the true hyperbola {worst * 1e6:F3} um");
        Assert.True(worst <= sagitta * 1.01, $"{worst} exceeds the sagitta {sagitta}");
        Assert.True(worst < 5.0e-6);
    }

    /// <summary>
    /// Two polygons sharing an edge are tangent, not overlapping; two that share
    /// interior are refused; a disc reaching into a polygon is refused; a disc
    /// tangent to it is not.
    /// </summary>
    [Fact]
    public void OverlapTreatsASharedEdgeAsTangency()
    {
        var upper = Polygon("upper", (0, 0), (4, 0), (4, 2), (0, 2));
        var lower = Polygon("lower", (0, -2), (4, -2), (4, 0), (0, 0));
        var shifted = Polygon("shifted", (1, -1), (5, -1), (5, 1), (1, 1));

        var errors = new List<Errors.EinzelError>();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, lower with { Potential = 2.0 }], "/f", errors);
        Assert.Empty(errors);

        errors.Clear();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, shifted with { Potential = 2.0 }], "/f", errors);
        Assert.Single(errors);
        output.WriteLine(errors[0].Constraint);

        // One polygon wholly inside another, no edge crossing at all.
        var inner = Polygon("inner", (1, 0.5), (2, 0.5), (2, 1.5), (1, 1.5));
        errors.Clear();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, inner with { Potential = 2.0 }], "/f", errors);
        Assert.Single(errors);

        var tangentDisc = new CompiledElectrode
        {
            Name = "disc", Shape = ElectrodeShape.Disc, CentreX = 2.0, CentreY = 3.0, Radius = 1.0, Potential = 5.0,
        };
        errors.Clear();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, tangentDisc], "/f", errors);
        Assert.Empty(errors);

        errors.Clear();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, tangentDisc with { CentreY = 2.5 }], "/f", errors);
        Assert.Single(errors);

        // And a rectangle against a polygon goes through the same arithmetic.
        errors.Clear();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, Rectangle(3, 1, 6, 3) with { Potential = 9.0 }], "/f", errors);
        Assert.Single(errors);
        errors.Clear();
        ElectrodeOverlap.Check([upper with { Potential = 1.0 }, Rectangle(4, 0, 6, 2) with { Potential = 9.0 }], "/f", errors);
        Assert.Empty(errors);
    }
}
