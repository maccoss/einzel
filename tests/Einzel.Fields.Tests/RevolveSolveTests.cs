using Einzel.Core.Model;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// An outline revolved about an axis: the bent rod the shape vocabulary could not express.
/// </summary>
/// <remarks>
/// <para>
/// <b>What forced it was a picture.</b> The C-trap template models each bent rod as a chain
/// of overlapping spheres, because <c>cylinder</c> is axis-aligned and a bent rod is not - so
/// beads needed no new primitive, since <c>repeat</c> binds an index and <c>cosPi</c> places
/// one anywhere. Nobody looked at the result until the viewport drew it, and what it drew was
/// a string of beads: thirteen spheres of 3.439 mm radius on a 3.459 mm pitch, scalloping the
/// rod by 13.6 percent of its own radius. The template's own description of
/// <c>beadCount</c> states the criterion the shipped value fails - the spacing "wants to be
/// comfortably under the rod radius" and its ratio is 1.006.
/// </para>
/// <para>
/// <b>A curved quadrupole is a straight one bent</b>, and this repository already had the
/// straight one: the linear ion trap declares its hyperbolic rods as a <c>polygon</c> outline
/// - a vertex run tracing r0*sqrt(1 + (y/r0)^2) - extruded along z as a <c>prism</c>. So the
/// missing primitive is that outline <em>revolved</em> rather than extruded, and it reuses the
/// outline's own exact two-dimensional distance unchanged. LIB-1's signal for the seventh
/// time, and the third time it asked for a shape rather than a function.
/// </para>
/// <para>
/// <b>Everything here is checked against a closed form the code had no part in.</b> A circular
/// profile revolved is a torus, whose signed distance is one line of arithmetic; a rectangular
/// profile revolved through a full turn is an annular slab; and a profile revolved about an
/// axis a very long way off is a prism, which is the limit the two primitives share.
/// </para>
/// </remarks>
public sealed class RevolveSolveTests(ITestOutputHelper output)
{
    /// <summary>A circle of <paramref name="minor"/> about a ring of <paramref name="major"/>.</summary>
    /// <remarks>
    /// Sampled as a polygon rather than declared as a circle, because the primitive takes an
    /// outline and nothing else - which is the point of it. The vertex count is what sets how
    /// closely it approaches the torus it is compared with.
    /// </remarks>
    private static CompiledElectrode3D Torus(
        CylinderAxis axis, double major, double minor, int vertices,
        double fromHalfTurns = 0.0, double toHalfTurns = 2.0)
    {
        var outline = new (double X, double Y)[vertices];

        for (var i = 0; i < vertices; i++)
        {
            var angle = 2.0 * Math.PI * i / vertices;

            outline[i] = (major + (minor * Math.Cos(angle)), minor * Math.Sin(angle));
        }

        return new CompiledElectrode3D
        {
            Name = "ring",
            Shape = Electrode3DShape.Revolve,
            Axis = axis,
            Vertices = outline,
            FromHalfTurns = fromHalfTurns,
            ToHalfTurns = toHalfTurns,
            Potential = 0.0,
        };
    }

    /// <summary>The exact signed distance to a torus about the z axis.</summary>
    private static double TorusDistance(
        double x, double y, double z, double major, double minor) =>
        Math.Sqrt(
            Math.Pow(Math.Sqrt((x * x) + (y * y)) - major, 2.0) + (z * z)) - minor;

    /// <summary>A circular profile revolved is a torus, and converges on one.</summary>
    /// <remarks>
    /// <para>
    /// <b>The sharpest check available, because a torus has a closed form and this does not
    /// know it.</b> The primitive is handed a sampled circle and computes the distance to a
    /// polygon in a half-plane; the comparison is against <c>hypot(hypot(x,y) - R, z) - r</c>,
    /// which shares no code with it.
    /// </para>
    /// <para>
    /// <b>It converges rather than matching, and that is what says it is right.</b> An
    /// inscribed regular polygon is inside its circle by <c>r(1 - cos(pi/n))</c>, so the error
    /// must fall as one over the vertex count squared - and a discrepancy that behaves that way
    /// is the sampling, while one that does not is the operator.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(CylinderAxis.Z)]
    [InlineData(CylinderAxis.X)]
    [InlineData(CylinderAxis.Y)]
    public void ACircularProfileRevolvedIsATorus(CylinderAxis axis)
    {
        const double Major = 20.0e-3;
        const double Minor = 3.0e-3;

        var probes = new List<(double A, double B, double Along)>();
        var random = new Random(11);

        for (var i = 0; i < 400; i++)
        {
            probes.Add((
                (random.NextDouble() - 0.5) * 60.0e-3,
                (random.NextDouble() - 0.5) * 60.0e-3,
                (random.NextDouble() - 0.5) * 20.0e-3));
        }

        var previous = double.NaN;

        foreach (var count in (int[])[32, 64, 128])
        {
            var ring = Torus(axis, Major, Minor, count);
            var worst = 0.0;

            foreach (var (a, b, along) in probes)
            {
                // The probe is written in the ring's own frame and put back into world
                // axes, so one set of points exercises all three orientations.
                var (x, y, z) = axis switch
                {
                    CylinderAxis.X => (along, a, b),
                    CylinderAxis.Y => (a, along, b),
                    _ => (a, b, along),
                };

                worst = Math.Max(
                    worst,
                    Math.Abs(ring.SignedDistance(x, y, z) - TorusDistance(a, b, along, Major, Minor)));
            }

            var expected = Minor * (1.0 - Math.Cos(Math.PI / count));

            output.WriteLine(
                $"{count,4} vertices: worst {worst * 1e6:F3} um, "
                + $"inscribed-polygon bound {expected * 1e6:F3} um"
                + (double.IsNaN(previous) ? "" : $", fell {previous / worst:F2}x"));

            // Inside the bound an inscribed polygon carries, with a little room for the
            // probes not landing exactly where it is worst.
            Assert.True(
                worst <= expected * 1.05,
                $"{count} vertices differ from the torus by {worst * 1e6:F3} um, "
                + $"past the {expected * 1e6:F3} um an inscribed polygon can account for");

            if (!double.IsNaN(previous))
            {
                // Second order in the vertex count: doubling it must take about four times
                // off. A first-order fall would say the profile is being used and the
                // operator is wrong.
                Assert.InRange(previous / worst, 3.5, 4.5);
            }

            previous = worst;
        }
    }

    /// <summary>Far from its axis, a revolved outline is the prism of the same outline.</summary>
    /// <remarks>
    /// <b>The limit the two primitives share, and the one a reader can check by eye.</b> Bend
    /// a rod round a circle of a kilometer and over a few millimeters it is straight. So the
    /// same outline, revolved at a large radius and extruded over the arc length it sweeps
    /// there, must give the same distances - which tests the revolve against code that was
    /// already right rather than against itself.
    /// </remarks>
    [Fact]
    public void FarFromItsAxisARevolvedOutlineIsAPrism()
    {
        const double Major = 1000.0;          // a kilometer, in meters
        const double Sweep = 1.0e-5;          // half turns, so about 31 mm of arc

        // An L, so the outline is neither convex nor symmetric and a mistake cannot cancel.
        (double X, double Y)[] profile =
        [
            (Major - 2.0e-3, -2.0e-3), (Major + 2.0e-3, -2.0e-3),
            (Major + 2.0e-3, 0.0e-3), (Major - 0.5e-3, 0.0e-3),
            (Major - 0.5e-3, 2.0e-3), (Major - 2.0e-3, 2.0e-3),
        ];

        var revolved = new CompiledElectrode3D
        {
            Name = "bent", Shape = Electrode3DShape.Revolve, Axis = CylinderAxis.Z,
            Vertices = profile, FromHalfTurns = -Sweep, ToHalfTurns = Sweep, Potential = 0.0,
        };

        // The prism of the same outline, straight: its cross-section plane for an axis of y
        // is (x, z) in world order, so the outline's (radius, axial) becomes (x, z) directly.
        var straight = new CompiledElectrode3D
        {
            Name = "straight", Shape = Electrode3DShape.Prism, Axis = CylinderAxis.Y,
            Vertices = profile,
            Lower = -Major * Sweep * Math.PI, Upper = Major * Sweep * Math.PI,
            Potential = 0.0,
        };

        var worst = 0.0;
        var random = new Random(7);

        for (var i = 0; i < 500; i++)
        {
            var x = Major + ((random.NextDouble() - 0.5) * 8.0e-3);
            var y = (random.NextDouble() - 0.5) * 20.0e-3;
            var z = (random.NextDouble() - 0.5) * 8.0e-3;

            worst = Math.Max(
                worst,
                Math.Abs(revolved.SignedDistance(x, y, z) - straight.SignedDistance(x, y, z)));
        }

        output.WriteLine($"worst disagreement over 500 probes: {worst * 1e9:F3} nm");

        // The curvature that is left: over 10 mm either side of the middle, a kilometer
        // radius sags by 10^2 / (2 * 1000) = 50 nm.
        Assert.True(worst < 1.0e-7, $"{worst * 1e9:F3} nm apart, which is more than the bend");
    }

    /// <summary>A segment entering a torus is caught where the closed form says it is.</summary>
    /// <remarks>
    /// <para>
    /// <b>Entry is what a cut cell asks for</b>, and it is the half most likely to be subtly
    /// wrong: the distance can be right while a crossing is missed, and a missed crossing is a
    /// conductor an ion flies through. Each edge of the profile revolves into a quadric, so
    /// every crossing is a root of a quadratic and none is found by bisection.
    /// </para>
    /// <para>
    /// <b>Checked against the distance rather than against a second formula</b>, because the
    /// distance has already been checked against the torus: an entry fraction is right when
    /// the signed distance is zero at it, negative just after, and positive just before.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASegmentEntersARevolvedOutlineWhereItsSurfaceIs()
    {
        const double Major = 20.0e-3;
        const double Minor = 3.0e-3;

        var ring = Torus(CylinderAxis.Z, Major, Minor, 256);
        var random = new Random(3);
        var hits = 0;
        var worst = 0.0;

        for (var i = 0; i < 600; i++)
        {
            var fromX = (random.NextDouble() - 0.5) * 80.0e-3;
            var fromY = (random.NextDouble() - 0.5) * 80.0e-3;
            var fromZ = (random.NextDouble() - 0.5) * 30.0e-3;
            var toX = (random.NextDouble() - 0.5) * 80.0e-3;
            var toY = (random.NextDouble() - 0.5) * 80.0e-3;
            var toZ = (random.NextDouble() - 0.5) * 30.0e-3;

            var entry = ring.FirstEntry(fromX, fromY, fromZ, toX, toY, toZ);

            if (entry is not { } t)
            {
                // Nothing found: then no point along the segment may be inside. Sampled
                // finely, because a claim of no crossing is the one that hides a miss.
                for (var k = 0; k <= 400; k++)
                {
                    var f = k / 400.0;

                    Assert.False(
                        ring.Contains(
                            fromX + ((toX - fromX) * f),
                            fromY + ((toY - fromY) * f),
                            fromZ + ((toZ - fromZ) * f)),
                        $"segment {i} was said to miss and is inside at {f:F3}");
                }

                continue;
            }

            hits++;

            var length = Math.Sqrt(
                Math.Pow(toX - fromX, 2.0) + Math.Pow(toY - fromY, 2.0) + Math.Pow(toZ - fromZ, 2.0));

            double At(double f) => ring.SignedDistance(
                fromX + ((toX - fromX) * f),
                fromY + ((toY - fromY) * f),
                fromZ + ((toZ - fromZ) * f));

            if (t > 0.0)
            {
                // On the surface at the entry, and outside a hair before it.
                worst = Math.Max(worst, Math.Abs(At(t)));

                Assert.True(
                    At(Math.Max(t - (1.0e-6 / length), 0.0)) > -1.0e-9,
                    $"segment {i} was already inside before its entry at {t:F6}");
            }

            Assert.True(
                At(t + (1.0e-6 / length)) < 1.0e-9,
                $"segment {i} is not inside just past its entry at {t:F6}");
        }

        output.WriteLine(
            $"{hits} of 600 segments met the ring; worst distance at entry {worst * 1e9:F3} nm");

        Assert.True(hits > 100, $"only {hits} segments met the ring, too few to say much");
        Assert.True(worst < 1.0e-9, $"entry lands {worst * 1e9:F3} nm off the surface");
    }

    /// <summary>A sweep short of a full turn has ends, and they are where they were declared.</summary>
    /// <remarks>
    /// <b>The end caps are the part a full revolution never exercises.</b> A C-trap's rods
    /// stop: a quarter turn of rod has two flat faces, ions reach them, and a primitive that
    /// quietly closed the ring would confine a packet that should have left. So this asserts
    /// the same profile is present inside the sweep and absent outside it, and that the
    /// boundary is exactly at the declared angle rather than a cell away from it.
    /// </remarks>
    [Fact]
    public void ASweepShortOfAFullTurnHasEndsWhereItSaysItDoes()
    {
        const double Major = 20.0e-3;
        const double Minor = 3.0e-3;

        // A quarter circle, from the +x axis round to the +y axis.
        var arc = Torus(CylinderAxis.Z, Major, Minor, 256, fromHalfTurns: 0.0, toHalfTurns: 0.5);
        var ring = Torus(CylinderAxis.Z, Major, Minor, 256);

        // On the ring's own circle, sampled right round.
        for (var degrees = -170; degrees <= 180; degrees += 10)
        {
            var angle = degrees * Math.PI / 180.0;
            var x = Major * Math.Cos(angle);
            var y = Major * Math.Sin(angle);

            var inside = degrees >= 0 && degrees <= 90;

            Assert.True(ring.Contains(x, y, 0.0), "the full ring is everywhere on its circle");
            Assert.Equal(inside, arc.Contains(x, y, 0.0));
        }

        // And the cap face is flat, at exactly the declared angle: a point a micrometer
        // outside it is outside, one a micrometer inside is inside.
        // Outward means away from the swept solid, which is the angles between the two caps.
        // Leaving the far cap means turning FURTHER, and the tangent there points along -x:
        // at angle t the position is R(cos t, sin t) and its derivative R(-sin t, cos t), so
        // at a quarter turn that is (-R, 0). Getting this backwards is what the test caught,
        // and it caught the test rather than the primitive.
        (double Cap, double Nx, double Ny)[] caps = [(0.0, 0.0, -1.0), (0.5, -1.0, 0.0)];

        foreach (var (cap, nx, ny) in caps)
        {
            var onX = Major * double.CosPi(cap);
            var onY = Major * double.SinPi(cap);

            Assert.True(
                arc.Contains(onX - (nx * 1.0e-6), onY - (ny * 1.0e-6), 0.0),
                $"the cap at {cap} half turns is not solid a micrometer inside it");

            Assert.False(
                arc.Contains(onX + (nx * 1.0e-6), onY + (ny * 1.0e-6), 0.0),
                $"the cap at {cap} half turns is solid a micrometer outside it");
        }

        // The bounding box of a quarter sweep is a quadrant, not the whole annulus - which
        // is what stops the viewport sampling four times the volume it needs and a coarse
        // level pinning nodes where there is no metal.
        var bounds = arc.Bounds;

        output.WriteLine(
            $"quarter sweep bounds: x [{bounds.MinX * 1e3:F2}, {bounds.MaxX * 1e3:F2}] "
            + $"y [{bounds.MinY * 1e3:F2}, {bounds.MaxY * 1e3:F2}] "
            + $"z [{bounds.MinZ * 1e3:F2}, {bounds.MaxZ * 1e3:F2}] mm");

        // EXACTLY ZERO AT TWO FACES, not minus the profile radius, and the difference is the
        // point of measuring a sector rather than its annulus. A cap is flat in its own
        // half-plane: at the quarter-turn end the whole profile sits at x = 0, and at the
        // other end all of it sits at y = 0. The solid never crosses either axis, so the
        // quadrant is the box - a quarter of the ring's, which is what it should be.
        Assert.Equal(0.0, bounds.MinX, 9);
        Assert.Equal(Major + Minor, bounds.MaxX, 9);
        Assert.Equal(0.0, bounds.MinY, 9);
        Assert.Equal(Major + Minor, bounds.MaxY, 9);
        Assert.Equal(-Minor, bounds.MinZ, 9);
        Assert.Equal(Minor, bounds.MaxZ, 9);

        // And the control: the full ring's box really is four times the area, so the
        // sector's is not merely a box that happens to fit.
        var whole = ring.Bounds;

        Assert.Equal(-(Major + Minor), whole.MinX, 9);
        Assert.Equal(-(Major + Minor), whole.MinY, 9);
    }

    /// <summary>A face flat only to rounding is still a face, and both sides of a rod get one.</summary>
    /// <remarks>
    /// <para>
    /// <b>An outline is written as expressions, so a face meant to be flat comes out flat only
    /// to rounding.</b> The C-trap's inner rod traces a hyperbola from -rodHalfWidth to
    /// +rodHalfWidth and then closes with a corner at the same half-width: the run's last
    /// vertex arrives at 3.0000000000000009 mm and the corner is written 3.0000000000000001,
    /// so the edge between them slopes by 8.7e-19 m over three millimeters of radius. Tested
    /// against zero that is not an annular disc but a cone of slope 2e15, whose quadratic is
    /// so ill-conditioned that its root is lost and the crossing simply disappears.
    /// </para>
    /// <para>
    /// <b>What made it expensive is that the other face of the same rod was exactly flat</b>,
    /// because -rodHalfWidth and the run's first vertex round the same way. So one side of a
    /// mirror-symmetric electrode got its cut cells and the other did not, the conductor mask
    /// was symmetric either way, and the solved field came out eleven percent asymmetric
    /// across a plane the geometry is symmetric about. A wrong field, from a right-looking
    /// model, with nothing reporting anything.
    /// </para>
    /// <para>
    /// <b>The assertion is the cut fraction rather than the field</b>, because that is the
    /// quantity the defect changed and it needs no solve: a link crossing the near-flat face
    /// must be cut at the same depth as its mirror through the exactly-flat one. Restoring
    /// <c>dh == 0.0</c> in <c>RevolveEntry</c> takes the worst disagreement from 2.2e-16 to
    /// 0.25 of a link - so on this outline the ill-conditioned cone <em>displaces</em> the
    /// crossing where on the C-trap's it lost it entirely. Both are asserted, because which
    /// of the two happens depends on how nearly the quadratic degenerates and neither is the
    /// safe one.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFaceFlatOnlyToRoundingIsStillCutWhereItIs()
    {
        const double Major = 20.0e-3;
        const double HalfWidth = 3.0e-3;
        const double HalfDepth = 2.0e-3;

        // A rod section, symmetric about the plane the axial coordinate calls zero. The
        // upper face is written the way an expression produces it - the last vertex of a
        // run and the corner that closes it, agreeing to the last bit but not beyond it -
        // and the lower face is written exactly, which is the pair that made this hard to
        // see rather than a case anybody would construct deliberately.
        var nudged = Math.BitIncrement(Math.BitIncrement(HalfWidth));

        (double X, double Y)[] profile =
        [
            (Major - HalfDepth, -HalfWidth),
            (Major + HalfDepth, -HalfWidth),
            (Major + HalfDepth, nudged),
            (Major - HalfDepth, HalfWidth),
        ];

        var rod = new CompiledElectrode3D
        {
            Name = "rod", Shape = Electrode3DShape.Revolve, Axis = CylinderAxis.Z,
            Vertices = profile, FromHalfTurns = 0.0, ToHalfTurns = 0.5, Potential = 0.0,
        };

        output.WriteLine(
            $"upper face slopes by {(nudged - HalfWidth) * 1e9:E3} nm over "
            + $"{2.0 * HalfDepth * 1e3:F1} mm of radius");

        Assert.NotEqual(HalfWidth, nudged);

        // Links along the axis, from outside the rod to past its middle, at radii and
        // azimuths spread across the sweep. Each is mirrored through the plane the rod is
        // symmetric about, so the two must be cut at the same depth.
        var worst = 0.0;
        var missed = 0;
        var probes = 0;

        // Inside the quarter sweep the rod spans, since a link outside it meets the
        // caps rather than the faces under test.
        foreach (var degrees in (double[])[5.0, 23.0, 45.0, 67.0, 85.0])
        {
            var c = Math.Cos(degrees * Math.PI / 180.0);
            var s = Math.Sin(degrees * Math.PI / 180.0);

            foreach (var radius in (double[])[
                Major - 1.9e-3, Major - 1.0e-3, Major, Major + 1.0e-3, Major + 1.9e-3])
            {
                var x = radius * c;
                var y = radius * s;

                // The link starts a millimeter clear of the face and ends inside, so a
                // crossing exists whichever way round it is taken.
                const double Outside = HalfWidth + 1.0e-3;

                var above = rod.FirstEntry(x, y, Outside, x, y, 0.0);
                var below = rod.FirstEntry(x, y, -Outside, x, y, 0.0);

                probes++;

                if (above is null || below is null)
                {
                    missed++;
                    continue;
                }

                worst = Math.Max(worst, Math.Abs(above.Value - below.Value));
            }
        }

        output.WriteLine(
            $"{probes} mirrored link pairs; {missed} with a crossing missing on one side; "
            + $"worst cut-fraction disagreement {worst:E3}");

        Assert.Equal(0, missed);

        // A displaced crossing would be a fraction of a cell; a missing one is the whole
        // link. The bound sits far below anything a real geometric difference could be and
        // far above the rounding two nearly-equal roots carry.
        Assert.True(
            worst < 1.0e-9,
            $"the two faces of a symmetric rod are cut {worst:E3} apart in the link fraction");

        // And the control, so the pairs above are not agreeing because nothing is there:
        // the rod really does start at the half-width on each side.
        Assert.True(rod.Contains(Major * Math.Cos(0.4), Major * Math.Sin(0.4), 0.0));
        Assert.False(
            rod.Contains(
                Major * Math.Cos(0.4), Major * Math.Sin(0.4), HalfWidth + 1.0e-6));
    }
}
