using Einzel.Core.Model;

using Xunit.Abstractions;

namespace Einzel.Core.Tests;

/// <summary>
/// A box may be tilted, because two plates that are deliberately not parallel is a
/// geometry the three axis-aligned primitives could not express at all.
/// </summary>
/// <remarks>
/// <para>
/// An asymmetric-track analyser's two mirrors converge by a couple of hundred microns over
/// a third of a metre, and that convergence is the <b>mechanism</b> rather than a tolerance:
/// it is what makes the drift decelerate and reverse. Without it the model is a generic
/// multi-reflection analyser wearing the right dimensions.
/// </para>
/// <para>
/// <b>Written as a rotation of the query, not as a fourth shape.</b> A tilted box is a box;
/// giving it its own signed-distance and first-entry code would be two implementations of
/// one solid, and two implementations of one solid disagree eventually. Signed distance and
/// first entry both work in the box's own frame and so need nothing beyond the transform -
/// a rotation is rigid, so a distance measured in that frame is the distance in the world,
/// and affine, so the fraction along a segment is preserved exactly.
/// </para>
/// </remarks>
public sealed class TiltedBoxTests(ITestOutputHelper output)
{
    /// <summary>A 10 x 2 x 10 mm plate centred on the origin, tilted about x.</summary>
    private static CompiledElectrode3D Plate(double halfTurns, CylinderAxis about = CylinderAxis.X) =>
        new()
        {
            Name = "plate",
            Shape = Electrode3DShape.Box,
            MinX = -5e-3,
            MaxX = 5e-3,
            MinY = -1e-3,
            MaxY = 1e-3,
            MinZ = -5e-3,
            MaxZ = 5e-3,
            Potential = 0.0,
            TiltAxis = about,
            TiltHalfTurns = halfTurns,
        };

    /// <summary>An untilted box is bit-identical to one that never heard of tilting.</summary>
    /// <remarks>
    /// The control that makes every other assertion here mean something. Every shipped
    /// geometry is untilted, so if this moved by a bit, every published 3-D number moved
    /// with it. Not "close": the transform is skipped entirely when the tilt is zero, so the
    /// arithmetic is the same arithmetic.
    /// </remarks>
    [Fact]
    public void AnUntiltedBoxIsUnchangedToTheBit()
    {
        var plate = Plate(0.0);

        foreach (var (x, y, z) in new[]
        {
            (0.0, 0.0, 0.0), (3e-3, 0.5e-3, -2e-3), (7e-3, 4e-3, 1e-3), (-9e-3, -3e-3, 8e-3),
        })
        {
            var distance = plate.SignedDistance(x, y, z);

            // The axis-aligned formula, written out here so the test does not simply ask
            // the code under test what it thinks.
            var dx = Math.Max(-5e-3 - x, x - 5e-3);
            var dy = Math.Max(-1e-3 - y, y - 1e-3);
            var dz = Math.Max(-5e-3 - z, z - 5e-3);

            var expected = dx <= 0.0 && dy <= 0.0 && dz <= 0.0
                ? Math.Max(dx, Math.Max(dy, dz))
                : Math.Sqrt(
                    (Math.Max(dx, 0.0) * Math.Max(dx, 0.0))
                    + (Math.Max(dy, 0.0) * Math.Max(dy, 0.0))
                    + (Math.Max(dz, 0.0) * Math.Max(dz, 0.0)));

            Assert.Equal(expected, distance);
        }
    }

    /// <summary>A right angle is exact, not a rounding away from one.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why half turns rather than radians.</b> <c>double.CosPi(0.5)</c> is exactly zero
    /// where <c>Math.Cos(Math.PI / 2)</c> is 6.1e-17 — so a plate declared upright would be
    /// tilted by a rounding, and a nominally symmetric geometry would carry a spurious
    /// asymmetry made of floating point. The same argument the drive decomposition and the
    /// expression grammar's <c>cosPi</c> already make.
    /// </para>
    /// <para>
    /// <b>The unit is one half turn, so 1.0 is 180° and a right angle is 0.5.</b> A first
    /// version of this test used 0.25 and expected a right angle — that is 45°, and the
    /// extents came out at 6 mm / sqrt(2). Worth keeping because the same confusion had
    /// reached the validator's own error message, where it would have taught it to whoever
    /// tripped the bound.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARightAngleIsExact()
    {
        // Turned a right angle about x, the 2 mm y-extent becomes the z-extent and the
        // 10 mm z-extent becomes the y-extent. Exactly, or the convention is not worth
        // having.
        var turned = Plate(0.5);

        var (minX, minY, minZ, maxX, maxY, maxZ) = turned.Bounds;

        output.WriteLine($"x  {minX * 1e3,7:F4} .. {maxX * 1e3,7:F4} mm");
        output.WriteLine($"y  {minY * 1e3,7:F4} .. {maxY * 1e3,7:F4} mm");
        output.WriteLine($"z  {minZ * 1e3,7:F4} .. {maxZ * 1e3,7:F4} mm");

        Assert.Equal(-5e-3, minX);
        Assert.Equal(5e-3, maxX);

        // Exactly swapped. Not approximately.
        Assert.Equal(5e-3, maxY);
        Assert.Equal(1e-3, maxZ);
    }

    /// <summary>Rotating the query is the same as rotating the point back.</summary>
    /// <remarks>
    /// The defining property, checked against arithmetic done here rather than against the
    /// implementation: a point rotated <i>with</i> the box must have exactly the distance
    /// the original point had to the untilted box, because a rotation is rigid.
    /// </remarks>
    [Theory]
    [InlineData(0.05)]
    [InlineData(-0.13)]
    [InlineData(0.25)]
    public void ARigidRotationPreservesDistance(double halfTurns)
    {
        var flat = Plate(0.0);
        var tilted = Plate(halfTurns);

        var cos = double.CosPi(halfTurns);
        var sin = double.SinPi(halfTurns);

        foreach (var (x, y, z) in new[]
        {
            (0.0, 0.0, 0.0), (2e-3, 0.4e-3, 3e-3), (0.0, 6e-3, 0.0), (-4e-3, -2e-3, 7e-3),
        })
        {
            // The same material point, carried round with the box: about x, y and z mix.
            var carriedY = (y * cos) - (z * sin);
            var carriedZ = (y * sin) + (z * cos);

            var before = flat.SignedDistance(x, y, z);
            var after = tilted.SignedDistance(x, carriedY, carriedZ);

            output.WriteLine(
                $"{halfTurns,6:F2} turns  ({x * 1e3,5:F1},{y * 1e3,5:F1},{z * 1e3,5:F1}) mm  "
                + $"{before * 1e3,9:F6} -> {after * 1e3,9:F6} mm");

            Assert.Equal(before, after, 12);
        }
    }

    /// <summary>A segment's entry fraction survives the rotation exactly.</summary>
    /// <remarks>
    /// First entry returns a fraction along a segment, and the caller uses that fraction in
    /// world coordinates — so it is only meaningful if the transform preserves it. A
    /// rotation is affine, so it does; this asserts it rather than trusting it, because a
    /// fraction quietly measured in the wrong frame would put an ion's impact point
    /// somewhere plausible and wrong.
    /// </remarks>
    [Fact]
    public void AnEntryFractionIsPreservedByTheRotation()
    {
        const double HalfTurns = 0.08;

        var flat = Plate(0.0);
        var tilted = Plate(HalfTurns);

        var cos = double.CosPi(HalfTurns);
        var sin = double.SinPi(HalfTurns);

        // A ray coming down onto the plate from well outside it.
        (double X, double Y, double Z) from = (0.0, 9e-3, 0.0);
        (double X, double Y, double Z) to = (0.0, -9e-3, 0.0);

        static (double X, double Y, double Z) Carry(
            (double X, double Y, double Z) p, double cos, double sin) =>
            (p.X, (p.Y * cos) - (p.Z * sin), (p.Y * sin) + (p.Z * cos));

        var carriedFrom = Carry(from, cos, sin);
        var carriedTo = Carry(to, cos, sin);

        var before = flat.FirstEntry(from.X, from.Y, from.Z, to.X, to.Y, to.Z);
        var after = tilted.FirstEntry(
            carriedFrom.X, carriedFrom.Y, carriedFrom.Z, carriedTo.X, carriedTo.Y, carriedTo.Z);

        output.WriteLine($"untilted entry at {before:F9} of the segment");
        output.WriteLine($"tilted   entry at {after:F9} of the segment");

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before!.Value, after!.Value, 12);
    }

    /// <summary>The Astral's own convergence, and it is far below one cell.</summary>
    /// <remarks>
    /// <para>
    /// <b>The case this exists for.</b> Two hundred microns over 350 mm is 5.7e-4 radians,
    /// or 1.8e-4 half turns — and on the 1.2 mm mesh such an analyser is solved at, the whole
    /// convergence is a sixth of a cell. A rasterised boundary would round it to nothing on
    /// every cell and the two mirrors would come out exactly parallel: the analyser would
    /// model, converge, and produce a drift that never reverses.
    /// </para>
    /// <para>
    /// It survives because the surface is a cut cell — Shortley–Weller stores how far the
    /// conductor is as a fraction of a cell, so a sub-cell displacement is carried rather
    /// than rounded. The same property that made FLD-1's shape derivative legible, met
    /// again in a different place.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAstralsConvergenceIsSubCellAndSurvives()
    {
        const double Length = 0.350;
        const double Convergence = 200e-6;

        // atan of a small ratio, in half turns.
        var halfTurns = Math.Atan(Convergence / Length) / Math.PI;

        output.WriteLine($"{Convergence * 1e6:F0} um over {Length * 1e3:F0} mm "
            + $"= {halfTurns:E3} half turns = {halfTurns * Math.PI * 1e3:F4} mrad");

        var board = new CompiledElectrode3D
        {
            Name = "board",
            Shape = Electrode3DShape.Box,
            MinX = 0.0,
            MaxX = 0.635,
            MinY = 20e-3,
            MaxY = 24e-3,
            MinZ = 0.0,
            MaxZ = Length,
            Potential = 0.0,
            TiltAxis = CylinderAxis.X,
            TiltHalfTurns = halfTurns,
        };

        // WHERE THE INNER FACE ACTUALLY IS, found by bisecting the signed distance in y at
        // a fixed z. Reading the distance from one fixed point instead does not measure
        // this: outside a box the signed distance is the Euclidean distance to the nearest
        // feature, which near an end is an edge rather than the face, and the answer comes
        // out about half. That was the first version of this test and it was wrong about
        // the geometry, not about the tilt.
        double FaceAt(double z)
        {
            var inside = 30e-3;   // clear of the board, in the gap
            var outside = 15e-3;  // well within it

            for (var step = 0; step < 200; step++)
            {
                var middle = 0.5 * (inside + outside);

                if (board.SignedDistance(0.3, middle, z) > 0.0)
                {
                    inside = middle;
                }
                else
                {
                    outside = middle;
                }
            }

            return 0.5 * (inside + outside);
        }

        // Sampled away from the ends, so the measurement is of the face rather than of a
        // corner: a tenth of the length in from each.
        var near = FaceAt(0.1 * Length);
        var far = FaceAt(0.9 * Length);

        var expected = 0.8 * Convergence;
        var separation = Math.Abs(far - near);

        output.WriteLine($"inner face at z={0.1 * Length * 1e3,6:F1} mm  {near * 1e6,12:F3} um");
        output.WriteLine($"inner face at z={0.9 * Length * 1e3,6:F1} mm  {far * 1e6,12:F3} um");
        output.WriteLine($"difference over 0.8 L         {separation * 1e6,10:F3} um "
            + $"against {expected * 1e6:F1} expected");

        // The face slopes by the declared convergence over the whole length, so over
        // four fifths of it, four fifths of the convergence.
        Assert.Equal(expected, separation, 8);

        // And the thing that makes it worth having: this is a fraction of a cell on the
        // mesh such an analyser is actually solved at.
        const double Cell = 1.24e-3;

        output.WriteLine($"as a fraction of a {Cell * 1e3:F2} mm cell: {Convergence / Cell:F3}");

        Assert.True(
            Convergence < Cell,
            "if the convergence were larger than a cell this test would no longer be about "
            + "the case that matters, which is a tilt a rasterised boundary would lose");
    }

    /// <summary>A tilted box's bounding box grows, and by the right amount.</summary>
    /// <remarks>
    /// The one query a rotation really changes: signed distance and first entry work in the
    /// box's own frame, but a bounding box is a statement in world coordinates. The shell
    /// extracts a conductor's surface over exactly this box, so one that is too small loses
    /// the tilted corners — silently, as a shape drawn slightly wrong.
    /// </remarks>
    [Fact]
    public void ATiltedBoundingBoxGrowsToContainTheCorners()
    {
        var flat = Plate(0.0).Bounds;
        var tilted = Plate(0.1).Bounds;

        output.WriteLine($"flat    y {flat.MinY * 1e3,7:F4} .. {flat.MaxY * 1e3,7:F4} mm");
        output.WriteLine($"tilted  y {tilted.MinY * 1e3,7:F4} .. {tilted.MaxY * 1e3,7:F4} mm");

        Assert.True(tilted.MaxY > flat.MaxY, "a tilt about x mixes y and z, so y must grow");
        Assert.True(tilted.MaxZ > flat.MaxZ);

        // x is the rotation axis and is untouched, exactly.
        Assert.Equal(flat.MinX, tilted.MinX);
        Assert.Equal(flat.MaxX, tilted.MaxX);

        // Exact rather than bounded: a box's corners realise the extent, so the half-extent
        // is |hy cos| + |hz sin| and nothing is left over.
        var cos = Math.Abs(double.CosPi(0.1));
        var sin = Math.Abs(double.SinPi(0.1));

        Assert.Equal((1e-3 * cos) + (5e-3 * sin), tilted.MaxY, 12);

        // And every corner of the tilted box really is inside the reported bounds.
        foreach (var sy in new[] { -1.0, 1.0 })
        {
            foreach (var sz in new[] { -1.0, 1.0 })
            {
                var cornerY = (sy * 1e-3 * double.CosPi(0.1)) - (sz * 5e-3 * double.SinPi(0.1));
                var cornerZ = (sy * 1e-3 * double.SinPi(0.1)) + (sz * 5e-3 * double.CosPi(0.1));

                Assert.InRange(cornerY, tilted.MinY - 1e-15, tilted.MaxY + 1e-15);
                Assert.InRange(cornerZ, tilted.MinZ - 1e-15, tilted.MaxZ + 1e-15);
            }
        }
    }
}
