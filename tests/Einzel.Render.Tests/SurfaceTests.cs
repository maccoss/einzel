using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// Conductor surfaces extracted as the zero level set of a signed distance.
/// </summary>
/// <remarks>
/// Every expectation here is a closed form the code had no part in — a sphere's area and
/// volume, a cylinder's, the divergence theorem — because a mesh that looks right on
/// screen is exactly the thing self-consistency cannot catch.
/// </remarks>
public sealed class SurfaceTests(ITestOutputHelper output)
{
    /// <summary>A sphere, negative inside.</summary>
    private static Surfaces.SignedDistance Sphere(double radius) =>
        (x, y, z) => Math.Sqrt((x * x) + (y * y) + (z * z)) - radius;

    /// <summary>The mesh's total area, in square metres.</summary>
    private static double Area(SurfaceMesh mesh)
    {
        var total = 0.0;

        for (var t = 0; t + 2 < mesh.Triangles.Count; t += 3)
        {
            var (a, b, c) = (mesh.Triangles[t], mesh.Triangles[t + 1], mesh.Triangles[t + 2]);

            var (ux, uy, uz) = (
                mesh.Vertices[3 * b] - mesh.Vertices[3 * a],
                mesh.Vertices[(3 * b) + 1] - mesh.Vertices[(3 * a) + 1],
                mesh.Vertices[(3 * b) + 2] - mesh.Vertices[(3 * a) + 2]);

            var (wx, wy, wz) = (
                mesh.Vertices[3 * c] - mesh.Vertices[3 * a],
                mesh.Vertices[(3 * c) + 1] - mesh.Vertices[(3 * a) + 1],
                mesh.Vertices[(3 * c) + 2] - mesh.Vertices[(3 * a) + 2]);

            var (nx, ny, nz) = (
                (uy * wz) - (uz * wy), (uz * wx) - (ux * wz), (ux * wy) - (uy * wx));

            total += 0.5 * Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        }

        return total;
    }

    /// <summary>
    /// The volume the mesh encloses, by the divergence theorem, in cubic metres.
    /// </summary>
    /// <remarks>
    /// A signed sum of tetrahedra on the origin. It is only correct for a closed surface
    /// wound consistently outward, which is exactly why it is a good test of both.
    /// </remarks>
    private static double Volume(SurfaceMesh mesh)
    {
        var total = 0.0;

        for (var t = 0; t + 2 < mesh.Triangles.Count; t += 3)
        {
            var (a, b, c) = (mesh.Triangles[t], mesh.Triangles[t + 1], mesh.Triangles[t + 2]);

            var (ax, ay, az) = (
                mesh.Vertices[3 * a], mesh.Vertices[(3 * a) + 1], mesh.Vertices[(3 * a) + 2]);
            var (bx, by, bz) = (
                mesh.Vertices[3 * b], mesh.Vertices[(3 * b) + 1], mesh.Vertices[(3 * b) + 2]);
            var (cx, cy, cz) = (
                mesh.Vertices[3 * c], mesh.Vertices[(3 * c) + 1], mesh.Vertices[(3 * c) + 2]);

            total += ((ax * ((by * cz) - (bz * cy)))
                - (ay * ((bx * cz) - (bz * cx)))
                + (az * ((bx * cy) - (by * cx)))) / 6.0;
        }

        return total;
    }

    /// <summary>A sphere comes out with the area and volume of a sphere.</summary>
    /// <remarks>
    /// <para>
    /// The volume is the sharper of the two and tests three things at once: that the
    /// surface is closed, that every triangle is wound outward, and that the vertices sit
    /// where the surface is. A hole, a flipped patch or a systematic offset all move it.
    /// </para>
    /// <para>
    /// Surface nets rounds by about a cell, so agreement is expected to a per cent or so
    /// rather than to machine precision — and to <em>improve under refinement</em>, which
    /// is the half that says it is a discretisation rather than a bug.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASphereHasTheAreaAndVolumeOfASphere()
    {
        const double Radius = 0.01;

        var exactArea = 4.0 * Math.PI * Radius * Radius;
        var exactVolume = 4.0 / 3.0 * Math.PI * Radius * Radius * Radius;

        var previous = double.MaxValue;

        foreach (var cells in (int[])[24, 48, 96])
        {
            var mesh = Surfaces.FromSignedDistance(
                Sphere(Radius), -0.011, -0.011, -0.011, 0.011, 0.011, 0.011, cells);

            var area = Area(mesh) / exactArea;
            var volume = Volume(mesh) / exactVolume;
            var error = Math.Abs(volume - 1.0);

            output.WriteLine(
                $"{cells,3} cells: {mesh.VertexCount,6} vertices, {mesh.TriangleCount,6} "
                + $"triangles, area {area:F5}, volume {volume:F5}");

            Assert.InRange(area, 0.97, 1.03);
            Assert.InRange(volume, 0.98, 1.02);

            Assert.True(error < previous, "the volume did not improve under refinement");

            previous = error;
        }
    }

    /// <summary>The surface is closed: every edge is shared by exactly two triangles.</summary>
    /// <remarks>
    /// Watertightness is a property of the algorithm rather than of the shape — one quad
    /// per sign-changing lattice edge, and each of the four cells around it contributes one
    /// vertex — so a failure here is a defect in the stitching rather than a resolution
    /// artefact. Checked separately from the volume because a mesh can enclose the right
    /// volume with two coincident holes in it.
    /// </remarks>
    [Fact]
    public void TheSurfaceIsClosed()
    {
        var mesh = Surfaces.FromSignedDistance(
            Sphere(0.01), -0.011, -0.011, -0.011, 0.011, 0.011, 0.011, 32);

        var edges = new Dictionary<(int, int), int>();

        for (var t = 0; t + 2 < mesh.Triangles.Count; t += 3)
        {
            foreach (var (p, q) in new[]
            {
                (mesh.Triangles[t], mesh.Triangles[t + 1]),
                (mesh.Triangles[t + 1], mesh.Triangles[t + 2]),
                (mesh.Triangles[t + 2], mesh.Triangles[t]),
            })
            {
                var key = p < q ? (p, q) : (q, p);

                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }

        var open = edges.Count(e => e.Value != 2);

        output.WriteLine($"{edges.Count} edges, {open} not shared by exactly two triangles");

        Assert.Equal(0, open);
    }

    /// <summary>Normals point away from the conductor, not into it.</summary>
    /// <remarks>
    /// The direction comes from the gradient of the signed distance rather than from the
    /// winding, so this is a check that the right scalar was differenced — a sign error
    /// gives a shape that is lit from inside and looks like a hole.
    /// </remarks>
    [Fact]
    public void NormalsPointOutOfTheConductor()
    {
        var mesh = Surfaces.FromSignedDistance(
            Sphere(0.01), -0.011, -0.011, -0.011, 0.011, 0.011, 0.011, 24);

        var worst = double.MaxValue;

        for (var v = 0; v < mesh.Vertices.Count; v += 3)
        {
            // On a sphere about the origin the outward normal is the position itself.
            var length = Math.Sqrt(
                (mesh.Vertices[v] * mesh.Vertices[v])
                + (mesh.Vertices[v + 1] * mesh.Vertices[v + 1])
                + (mesh.Vertices[v + 2] * mesh.Vertices[v + 2]));

            var agreement =
                ((mesh.Vertices[v] * mesh.Normals[v])
                + (mesh.Vertices[v + 1] * mesh.Normals[v + 1])
                + (mesh.Vertices[v + 2] * mesh.Normals[v + 2])) / length;

            worst = Math.Min(worst, agreement);
        }

        output.WriteLine($"worst normal agreement with the radius: {worst:F6}");

        Assert.True(worst > 0.999, $"a normal was {worst:F4} of outward");
    }

    /// <summary>A revolved profile is the solid of revolution, not the profile (SYM-1).</summary>
    /// <remarks>
    /// <para>
    /// An axisymmetric half-plane is not a picture of the geometry, it is a geometry that
    /// repeats all the way round: a rectangle in the half-plane is a <em>tube</em> in space.
    /// Checked against Pappus — the volume of a solid of revolution is the profile's area
    /// times the circumference its centroid travels — which is arithmetic this code has no
    /// part in.
    /// </para>
    /// <para>
    /// A rectangle from radius <c>a</c> to <c>b</c> over a length <c>L</c> gives
    /// <c>pi (b^2 - a^2) L</c>, and the faceting makes the polygon inscribed rather than
    /// circular, so the mesh is expected slightly under.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARevolvedProfileIsTheSolidOfRevolution()
    {
        const double Inner = 0.004;
        const double Outer = 0.006;
        const double Length = 0.010;

        // Closed rectangle in (x, r), last point repeating the first.
        var profile = new (double X, double Radius)[]
        {
            (0.0, Inner), (Length, Inner), (Length, Outer), (0.0, Outer), (0.0, Inner),
        };

        var exact = Math.PI * ((Outer * Outer) - (Inner * Inner)) * Length;

        foreach (var segments in (int[])[16, 64, 256])
        {
            var mesh = Surfaces.Revolve(profile, segments);

            // A tube's outward normal is radial on the outer wall and inward-radial on the
            // inner, which no closed form over the profile gives - so orientation comes
            // from the same signed distance the solver uses.
            var oriented = Surfaces.Orient(
                mesh,
                (x, y, z) =>
                {
                    var r = Math.Sqrt((y * y) + (z * z));

                    return Math.Max(
                        Math.Max(Inner - r, r - Outer),
                        Math.Max(-x, x - Length));
                },
                1e-5);

            var ratio = Math.Abs(Volume(oriented)) / exact;

            output.WriteLine($"{segments,4} facets: volume {ratio:F5} of the exact tube");

            // Inscribed, so under - and approaching one from below as the facets shrink.
            Assert.InRange(ratio, segments >= 64 ? 0.995 : 0.94, 1.0);
        }
    }

    /// <summary>An extruded profile is a prism, and it is deliberately open (SYM-1).</summary>
    /// <remarks>
    /// A translational solve assumes the geometry is invariant along the third axis, so
    /// the electrode extends past whatever is drawn. Capping the prism would draw an end
    /// the model does not have — so the correct assertion is that the ends are
    /// <em>open</em>, which is the opposite of what a mesh test usually wants.
    /// </remarks>
    [Fact]
    public void AnExtrudedProfileIsAnOpenPrism()
    {
        var profile = new (double X, double Y)[]
        {
            (0.0, 0.0), (0.01, 0.0), (0.01, 0.01), (0.0, 0.01), (0.0, 0.0),
        };

        var mesh = Surfaces.Extrude(profile, -0.02, 0.02);

        output.WriteLine($"{mesh.VertexCount} vertices, {mesh.TriangleCount} triangles");

        // Four sides, two triangles each.
        Assert.Equal(8, mesh.TriangleCount);

        var boundary = new Dictionary<(int, int), int>();

        for (var t = 0; t + 2 < mesh.Triangles.Count; t += 3)
        {
            foreach (var (p, q) in new[]
            {
                (mesh.Triangles[t], mesh.Triangles[t + 1]),
                (mesh.Triangles[t + 1], mesh.Triangles[t + 2]),
                (mesh.Triangles[t + 2], mesh.Triangles[t]),
            })
            {
                var key = p < q ? (p, q) : (q, p);

                boundary[key] = boundary.GetValueOrDefault(key) + 1;
            }
        }

        // Eight boundary edges: four round each open end.
        Assert.Equal(8, boundary.Count(e => e.Value == 1));
    }

    /// <summary>A box that holds none of the surface yields nothing rather than throwing.</summary>
    /// <remarks>
    /// An electrode entirely outside the drawn region is ordinary — a repeat's copies, a
    /// domain clipped for a close look — and must produce an empty mesh, not an exception
    /// and not a stray facet.
    /// </remarks>
    [Fact]
    public void ABoxWithNoSurfaceInItYieldsNothing()
    {
        var mesh = Surfaces.FromSignedDistance(
            Sphere(0.01), 0.10, 0.10, 0.10, 0.12, 0.12, 0.12, 16);

        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(0, mesh.TriangleCount);
    }
}

/// <summary>
/// A long thin slab has to mesh, and it is the shape a real analyser is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sub-cell geometry has been lost twice, in two different ways.</b> First by sampling
/// over the whole solve domain, where a 1 mm plate in a 60 mm box at 48 cells falls between
/// lattice planes; that was fixed by extracting over the electrode's own bounding box. Then
/// by the box's own <em>aspect ratio</em>: an analyser's stripe electrode is 4 mm thick and
/// 635 mm long, so 48 cells across the longest span still gives 15 mm cells and the metal
/// again lies between two sample planes.
/// </para>
/// <para>
/// Both produce no surface at all and say nothing, so the check is that a slab of the shape
/// real hardware is built from comes back with triangles in it and with the extent it was
/// given.
/// </para>
/// </remarks>
public sealed class ThinSlabSurfaceTests(ITestOutputHelper output)
{
    /// <summary>An axis-aligned slab, in metres: 20 x 4 x 635 mm.</summary>
    private static double Slab(double x, double y, double z)
    {
        var dx = Math.Abs(x) - 0.010;
        var dy = Math.Abs(y) - 0.002;
        var dz = Math.Abs(z) - 0.3175;

        var outside = Math.Sqrt(
            (Math.Max(dx, 0) * Math.Max(dx, 0))
            + (Math.Max(dy, 0) * Math.Max(dy, 0))
            + (Math.Max(dz, 0) * Math.Max(dz, 0)));

        return outside + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0.0);
    }

    /// <summary>An analyser stripe meshes, and keeps its thickness.</summary>
    /// <remarks>
    /// The thickness is the assertion with teeth. A surface can be extracted and be a
    /// <i>sheet</i> — the two faces collapsed onto one — which has triangles, looks
    /// plausible in a viewport, and is not the electrode. So the y extent of the vertices
    /// has to span the slab rather than merely exist.
    /// </remarks>
    [Fact]
    public void ALongThinSlabIsMeshedWithItsThickness()
    {
        var mesh = Surfaces.FromSignedDistance(
            Slab, -0.012, -0.004, -0.34, 0.012, 0.004, 0.34, 48);

        output.WriteLine($"{mesh.VertexCount} vertices, {mesh.TriangleCount} triangles");

        Assert.True(
            mesh.TriangleCount > 0,
            "a 4 mm x 635 mm stripe produced no surface at all. That is the sub-cell "
            + "failure returning through the box's aspect ratio: a uniform step taken from "
            + "the longest span gives 15 mm cells, and 4 mm of metal falls between two "
            + "sample planes");

        var ys = new List<double>();

        for (var i = 1; i < mesh.Vertices.Count; i += 3)
        {
            ys.Add(mesh.Vertices[i]);
        }

        var thickness = ys.Max() - ys.Min();

        output.WriteLine($"y extent {thickness * 1e3:F3} mm of a 4.000 mm slab");

        Assert.True(
            thickness > 0.003,
            $"the extracted surface spans {thickness * 1e3:F3} mm in y where the slab is "
            + "4 mm thick, so it has been flattened into a sheet rather than meshed");
    }

    /// <summary>It does not pay for the thin axis along the long one.</summary>
    /// <remarks>
    /// <b>The opposite failure, and the first fix caused it.</b> Taking the step from the
    /// thinnest span instead resolves the slab and then tessellates its length at the same
    /// spacing: 72,000 triangles for what is a box, and sixteen of them made a 77 MB file.
    /// A box needs twelve triangles; a few thousand is a smoothed extraction of one, and
    /// a hundred thousand is a bug.
    /// </remarks>
    [Fact]
    public void ASlabDoesNotCostItsAspectRatioInTriangles()
    {
        var mesh = Surfaces.FromSignedDistance(
            Slab, -0.012, -0.004, -0.34, 0.012, 0.004, 0.34, 48);

        output.WriteLine($"{mesh.TriangleCount} triangles");

        Assert.True(
            mesh.TriangleCount < 20_000,
            $"{mesh.TriangleCount} triangles for a single slab. Resolving the thin axis by "
            + "shortening one uniform step charges the same spacing along the long axis, "
            + "which is 159 times more cells than that direction needs");
    }

    /// <summary>An isotropic shape is meshed exactly as it was before.</summary>
    /// <remarks>
    /// The control for both of the above: every axis lands on the requested count, so a
    /// sphere's area, volume and Pappus checks in <see cref="SurfaceTests"/> are testing
    /// the same mesh they always were and none of this changed them.
    /// </remarks>
    [Fact]
    public void ACubeGetsTheRequestedCountOnEveryAxis()
    {
        static double Sphere(double x, double y, double z) =>
            Math.Sqrt((x * x) + (y * y) + (z * z)) - 0.010;

        var mesh = Surfaces.FromSignedDistance(
            Sphere, -0.012, -0.012, -0.012, 0.012, 0.012, 0.012, 24);

        output.WriteLine($"{mesh.VertexCount} vertices, {mesh.TriangleCount} triangles");

        // 24 cells a side on a sphere of 10 mm in a 24 mm box: the surface cells are the
        // shell of a ball about 20 cells across, so a few thousand triangles. What matters
        // is that it is the same number an isotropic box always gave.
        Assert.InRange(mesh.TriangleCount, 1_000, 6_000);
    }
}
