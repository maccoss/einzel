using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A polygon electrode in a solve: where it coincides with a rectangle the solved
/// field is the rectangle's to the rounding of one cut fraction, a rod written as two
/// halves with the slot closed is the whole rod to the last bit, and four truncated
/// hyperbolic rods carry less 12-pole than round rods at unit ratio.
/// </summary>
/// <remarks>
/// The mask is built from the shape's own <c>Contains</c> and the cut links from its
/// <c>FirstEntry</c>, so a polygon that agrees with the rectangle in both produces
/// the same mask and the same cuts. The rectangle computes a cut fraction as one
/// division and the polygon as a cross-product ratio - algebraically equal, rounded
/// differently in the last bit - so the first comparison is to 1e-13 of the applied
/// potential rather than to the bit, and the second, where both sides run the same
/// polygon code, is exact.
/// </remarks>
public sealed class PolygonSolveTests(ITestOutputHelper output)
{
    private static CompiledSolvedField Box(params CompiledElectrode[] electrodes) =>
        new()
        {
            MinX = -10.0e-3,
            MinY = -10.0e-3,
            MaxX = 10.0e-3,
            MaxY = 10.0e-3,
            CellSize = 0.25e-3,
            Tolerance = 1e-10,
            Electrodes = electrodes,
        };

    private static CompiledElectrode Polygon(string name, double potential, params (double X, double Y)[] vertices) =>
        new()
        {
            Name = name,
            Shape = ElectrodeShape.Polygon,
            Vertices = vertices,
            MinX = vertices.Min(v => v.X),
            MinY = vertices.Min(v => v.Y),
            MaxX = vertices.Max(v => v.X),
            MaxY = vertices.Max(v => v.Y),
            Potential = potential,
        };

    private static double WorstDifference(IElectrostaticField a, IElectrostaticField b)
    {
        var worst = 0.0;
        for (var j = -36; j <= 36; j++)
        {
            for (var i = -36; i <= 36; i++)
            {
                var point = new Vec3(i * 0.25e-3, j * 0.25e-3, 0.0);
                worst = Math.Max(worst, Math.Abs(a.PotentialAt(in point) - b.PotentialAt(in point)));
            }
        }

        return worst;
    }

    /// <summary>The same plate as a rectangle and as a four-vertex polygon: one field, to rounding.</summary>
    [Fact]
    public void ASquarePolygonSolvesToTheRectanglesField()
    {
        // Deliberately off the grid lines, so the cut links carry sub-cell fractions
        // that both shapes have to compute identically.
        var rectangle = new CompiledElectrode
        {
            Name = "plate", Shape = ElectrodeShape.Rectangle,
            MinX = -3.1e-3, MinY = 0.9e-3, MaxX = 2.7e-3, MaxY = 1.6e-3, Potential = 100.0,
        };
        var polygon = Polygon("plate", 100.0, (-3.1e-3, 0.9e-3), (2.7e-3, 0.9e-3), (2.7e-3, 1.6e-3), (-3.1e-3, 1.6e-3));

        var (fieldR, reportR) = GeometryBuilder.Build(Box(rectangle));
        var (fieldP, reportP) = GeometryBuilder.Build(Box(polygon));

        var worst = WorstDifference(fieldR, fieldP);
        output.WriteLine($"rectangle: {reportR.Cycles} cycles, factor {reportR.ConvergenceFactor:F4}; polygon: {reportP.Cycles} cycles, factor {reportP.ConvergenceFactor:F4}");
        output.WriteLine($"worst potential difference {worst:E3} V of 100 applied");

        Assert.Equal(reportR.Cycles, reportP.Cycles);
        Assert.True(worst < 1e-11, $"{worst} V differs by more than rounding of a cut fraction");
    }

    /// <summary>
    /// A rod written as two halves meeting on y = 0 - the way a slotted rod is written
    /// with its slot closed - is the whole rod, to the bit.
    /// </summary>
    [Fact]
    public void TwoHalvesWithTheSlotClosedAreTheWholeRod()
    {
        (double, double)[] whole = [(2.0e-3, -3.0e-3), (7.3e-3, -3.0e-3), (7.3e-3, 3.0e-3), (2.0e-3, 3.0e-3)];
        (double, double)[] upper = [(2.0e-3, 0.0), (7.3e-3, 0.0), (7.3e-3, 3.0e-3), (2.0e-3, 3.0e-3)];
        (double, double)[] lower = [(2.0e-3, -3.0e-3), (7.3e-3, -3.0e-3), (7.3e-3, 0.0), (2.0e-3, 0.0)];

        var (fieldWhole, _) = GeometryBuilder.Build(Box(Polygon("rod", 250.0, whole)));
        var (fieldHalves, _) = GeometryBuilder.Build(Box(Polygon("rodUpper", 250.0, upper), Polygon("rodLower", 250.0, lower)));

        var worst = WorstDifference(fieldWhole, fieldHalves);
        output.WriteLine($"worst potential difference {worst:E3} V of 250 applied");
        Assert.Equal(0.0, worst);
    }

    /// <summary>
    /// Four truncated hyperbolic rods against four round rods at unit ratio: the
    /// hyperbola's 12-pole is smaller, and the quadrupole term is the same to a few
    /// per cent.
    /// </summary>
    /// <remarks>
    /// Not a claim that truncated hyperbolic rods are ideal - truncation puts a
    /// 12-pole back - only that the polygon path produces the field its geometry
    /// implies. Round rods at a ratio of one carry a well-known 12-pole of about
    /// two per cent of the quadrupole; a hyperbola truncated at one and a half
    /// inscribed radii carries far less.
    /// </remarks>
    [Fact]
    public void HyperbolicRodsCarryLessTwelvePoleThanRoundRodsAtUnitRatio()
    {
        const double R0 = 4.0e-3;
        const double HalfWidth = 6.0e-3;
        const double Back = 12.0e-3;
        const int Segments = 48;

        static (double X, double Y)[] Face(int quadrant)
        {
            var v = new List<(double X, double Y)>();
            for (var k = 0; k <= Segments; k++)
            {
                var s = -HalfWidth + (2.0 * HalfWidth * k / Segments);
                var r = Math.Sqrt((R0 * R0) + (s * s));
                v.Add(quadrant switch
                {
                    0 => (r, s),      // +x rod: face x = sqrt(r0^2 + y^2)
                    1 => (s, r),      // +y rod
                    2 => (-r, s),     // -x rod
                    _ => (s, -r),     // -y rod
                });
            }

            v.Add(quadrant switch { 0 => (Back, HalfWidth), 1 => (HalfWidth, Back), 2 => (-Back, HalfWidth), _ => (HalfWidth, -Back) });
            v.Add(quadrant switch { 0 => (Back, -HalfWidth), 1 => (-HalfWidth, Back), 2 => (-Back, -HalfWidth), _ => (-HalfWidth, -Back) });
            return [.. v];
        }

        var hyperbolic = new CompiledSolvedField
        {
            MinX = -16.0e-3, MinY = -16.0e-3, MaxX = 16.0e-3, MaxY = 16.0e-3, CellSize = 0.25e-3, Tolerance = 1e-10,
            Electrodes =
            [
                Polygon("xPlus", 100.0, Face(0)), Polygon("xMinus", 100.0, Face(2)),
                Polygon("yPlus", -100.0, Face(1)), Polygon("yMinus", -100.0, Face(3)),
            ],
        };

        static CompiledElectrode Disc(string name, double x, double y, double potential) =>
            new() { Name = name, Shape = ElectrodeShape.Disc, CentreX = x, CentreY = y, Radius = R0, Potential = potential };

        var round = hyperbolic with
        {
            Electrodes =
            [
                Disc("xPlus", 2.0 * R0, 0.0, 100.0), Disc("xMinus", -2.0 * R0, 0.0, 100.0),
                Disc("yPlus", 0.0, 2.0 * R0, -100.0), Disc("yMinus", 0.0, -2.0 * R0, -100.0),
            ],
        };

        var (fieldH, reportH) = GeometryBuilder.Build(hyperbolic);
        var (fieldR, reportR) = GeometryBuilder.Build(round);

        var h = Multipoles(fieldH, 0.5 * R0);
        var r = Multipoles(fieldR, 0.5 * R0);

        output.WriteLine($"hyperbolic: {reportH.Cycles} cycles, factor {reportH.ConvergenceFactor:F4}; A2 {h[2]:F4} V, A6/A2 {h[6] / h[2]:E3}, A10/A2 {h[10] / h[2]:E3}");
        output.WriteLine($"round r/r0=1: {reportR.Cycles} cycles, factor {reportR.ConvergenceFactor:F4}; A2 {r[2]:F4} V, A6/A2 {r[6] / r[2]:E3}, A10/A2 {r[10] / r[2]:E3}");

        Assert.True(reportH.Converged);
        Assert.True(h[6] / h[2] < 0.25 * (r[6] / r[2]), $"hyperbolic 12-pole {h[6] / h[2]:E3} is not well below round {r[6] / r[2]:E3}");
        Assert.InRange(h[2] / r[2], 0.9, 1.1);
    }

    private static double[] Multipoles(IElectrostaticField field, double radius)
    {
        const int Samples = 1024;
        const int Highest = 10;
        var cosine = new double[Highest + 1];
        var sine = new double[Highest + 1];

        for (var k = 0; k < Samples; k++)
        {
            var theta = 2.0 * Math.PI * k / Samples;
            var point = new Vec3(radius * Math.Cos(theta), radius * Math.Sin(theta), 0.0);
            var phi = field.PotentialAt(in point);
            for (var order = 0; order <= Highest; order++)
            {
                cosine[order] += phi * Math.Cos(order * theta);
                sine[order] += phi * Math.Sin(order * theta);
            }
        }

        var magnitude = new double[Highest + 1];
        for (var order = 0; order <= Highest; order++)
        {
            magnitude[order] = Math.Sqrt((cosine[order] * cosine[order]) + (sine[order] * sine[order])) * 2.0 / Samples;
        }

        return magnitude;
    }
}
