using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A prism is a two-dimensional outline given a length. Where it coincides with a box it
/// must be the box - same signed distance, same first entry, same solved field - and a
/// re-entrant outline must find a link's entry through its notch.
/// </summary>
public sealed class PrismSolveTests(ITestOutputHelper output)
{
    private static CompiledElectrode3D Box(double x0, double y0, double z0, double x1, double y1, double z1, double potential = 100.0) =>
        new()
        {
            Name = "plate", Shape = Electrode3DShape.Box,
            MinX = x0, MinY = y0, MinZ = z0, MaxX = x1, MaxY = y1, MaxZ = z1, Potential = potential,
        };

    private static CompiledElectrode3D Prism(CylinderAxis axis, double lower, double upper, double potential, params (double X, double Y)[] vertices) =>
        new()
        {
            Name = "plate", Shape = Electrode3DShape.Prism, Axis = axis, Lower = lower, Upper = upper,
            Vertices = vertices, Potential = potential,
        };

    /// <summary>The same slab as a box and as a square prism along each axis: distance and entry agree to rounding.</summary>
    [Theory]
    [InlineData(CylinderAxis.Z)]
    [InlineData(CylinderAxis.X)]
    [InlineData(CylinderAxis.Y)]
    public void ASquarePrismIsTheBox(CylinderAxis axis)
    {
        // The box: x in [-3, 2], y in [-1, 4], z in [0, 6] mm. The prism's outline is the
        // two coordinates other than its axis, in world order.
        var box = Box(-3e-3, -1e-3, 0.0, 2e-3, 4e-3, 6e-3);
        var prism = axis switch
        {
            CylinderAxis.Z => Prism(axis, 0.0, 6e-3, 100.0, (-3e-3, -1e-3), (2e-3, -1e-3), (2e-3, 4e-3), (-3e-3, 4e-3)),
            CylinderAxis.X => Prism(axis, -3e-3, 2e-3, 100.0, (-1e-3, 0.0), (4e-3, 0.0), (4e-3, 6e-3), (-1e-3, 6e-3)),
            _ => Prism(axis, -1e-3, 4e-3, 100.0, (-3e-3, 0.0), (2e-3, 0.0), (2e-3, 6e-3), (-3e-3, 6e-3)),
        };

        Assert.Equal(box.Bounds, prism.Bounds);
        Assert.Equal(box.Centre.X, prism.Centre.X, 15);
        Assert.Equal(box.Centre.Y, prism.Centre.Y, 15);
        Assert.Equal(box.Centre.Z, prism.Centre.Z, 15);

        var random = new Random(20260906);
        var worst = 0.0;
        var entries = 0;
        for (var k = 0; k < 20000; k++)
        {
            var x = (random.NextDouble() * 16e-3) - 8e-3;
            var y = (random.NextDouble() * 16e-3) - 8e-3;
            var z = (random.NextDouble() * 16e-3) - 8e-3;
            worst = Math.Max(worst, Math.Abs(box.SignedDistance(x, y, z) - prism.SignedDistance(x, y, z)));

            var h = 0.25e-3 * (1 + random.Next(0, 40));
            var (tx, ty, tz) = random.Next(0, 6) switch
            {
                0 => (x + h, y, z), 1 => (x - h, y, z), 2 => (x, y + h, z), 3 => (x, y - h, z), 4 => (x, y, z + h), _ => (x, y, z - h),
            };
            var a = box.FirstEntry(x, y, z, tx, ty, tz);
            var b = prism.FirstEntry(x, y, z, tx, ty, tz);
            Assert.Equal(a is null, b is null);
            if (a is { } fa && b is { } fb)
            {
                entries++;
                Assert.Equal(fa, fb, 1e-12);
            }
        }

        output.WriteLine($"axis {axis}: worst signed-distance disagreement {worst:E2} m, {entries} entries agree");
        Assert.True(worst < 1e-15);
        Assert.True(entries > 1000);
    }

    /// <summary>A re-entrant prism: a link from inside the notch of an L enters the arm at its inner face, and a link along the notch misses.</summary>
    [Fact]
    public void AReentrantPrismFindsItsNotch()
    {
        var l = Prism(CylinderAxis.Z, 0.0, 10.0, 1.0, (0, 0), (4, 0), (4, 2), (2, 2), (2, 4), (0, 4));

        Assert.False(l.Contains(3.0, 3.0, 5.0));
        Assert.True(l.Contains(1.0, 3.0, 5.0));
        Assert.Equal(0.5, l.FirstEntry(3.0, 3.0, 5.0, 1.0, 3.0, 5.0)!.Value, 12);
        Assert.Null(l.FirstEntry(3.0, 3.0, 5.0, 3.9, 3.9, 5.0));

        // Along the axis, entry is at the end face, and a link that never reaches it misses.
        Assert.Equal(0.5, l.FirstEntry(1.0, 1.0, -10.0, 1.0, 1.0, 10.0)!.Value, 12);
        Assert.Null(l.FirstEntry(1.0, 1.0, -10.0, 1.0, 1.0, -1.0));
        Assert.Equal(1.0, l.SignedDistance(3.0, 3.0, 5.0), 12);
        Assert.Equal(-1.0, l.SignedDistance(1.0, 1.0, 5.0), 12);
        Assert.Equal(Math.Sqrt(10.0), l.SignedDistance(5.0, 5.0, 5.0), 12);   // nearest corner is (4, 2)
    }

    /// <summary>A square prism solves to the box's field to rounding, in three dimensions.</summary>
    [Fact]
    public void ASquarePrismSolvesToTheBoxesField()
    {
        var box = Box(-3.1e-3, 0.9e-3, -2.2e-3, 2.7e-3, 1.6e-3, 2.3e-3);
        var prism = Prism(CylinderAxis.Z, -2.2e-3, 2.3e-3, 100.0, (-3.1e-3, 0.9e-3), (2.7e-3, 0.9e-3), (2.7e-3, 1.6e-3), (-3.1e-3, 1.6e-3));

        static Geometry3D Geometry(CompiledElectrode3D electrode) =>
            new(-8e-3, -8e-3, -8e-3, 8e-3, 8e-3, 8e-3, 0.5e-3, [electrode], 1e-10);

        var (fieldB, reportB) = GeometryBuilder3D.Build(Geometry(box));
        var (fieldP, reportP) = GeometryBuilder3D.Build(Geometry(prism));

        var worst = 0.0;
        var random = new Random(3);
        for (var k = 0; k < 2000; k++)
        {
            var point = new Vec3((random.NextDouble() * 14e-3) - 7e-3, (random.NextDouble() * 14e-3) - 7e-3, (random.NextDouble() * 14e-3) - 7e-3);
            worst = Math.Max(worst, Math.Abs(fieldB.PotentialAt(in point) - fieldP.PotentialAt(in point)));
        }

        output.WriteLine($"box {reportB.Cycles} cycles, prism {reportP.Cycles}; worst potential difference {worst:E3} V of 100");
        Assert.Equal(reportB.Cycles, reportP.Cycles);
        Assert.True(worst < 1e-10, $"{worst} V");
    }
}
