using System.Globalization;

using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// The mesh writer, checked against the format rather than against itself.
/// </summary>
/// <remarks>
/// <para>
/// The surfaces were extractable headlessly and had no way out of the program: the only
/// consumer of <see cref="Surfaces"/> was the Windows viewport, so making a picture of a
/// three-dimensional geometry needed the shell. These tests are about the file, since the
/// geometry is already covered by <c>SurfaceTests</c> against a sphere's area and volume,
/// Pappus, and watertightness.
/// </para>
/// </remarks>
public sealed class ObjWriterTests(ITestOutputHelper output)
{
    /// <summary>A unit triangle, offset so two of them are distinguishable.</summary>
    private static NamedSurface Triangle(string name, double dx) =>
        new(
            name,
            [dx, 0, 0, dx + 1, 0, 0, dx, 1, 0],
            [0, 0, 1, 0, 0, 1, 0, 0, 1],
            [0, 1, 2]);

    /// <summary>Vertex indices run across the file, not from one per object.</summary>
    /// <remarks>
    /// <para>
    /// <b>The one mistake in this format that produces a file which loads cleanly and is
    /// wrong.</b> OBJ indices are one-based and global; restarting them per object gives a
    /// second object drawn from the first object's vertices, with no parse error anywhere.
    /// </para>
    /// <para>
    /// So the check is on the actual index values: the second triangle must reference 4, 5
    /// and 6, and a writer that forgot the offset would emit 1, 2, 3 twice.
    /// </para>
    /// </remarks>
    [Fact]
    public void VertexIndicesAreGlobalAndOneBased()
    {
        var text = ObjWriter.Write([Triangle("first", 0.0), Triangle("second", 10.0)]);

        output.WriteLine(text);

        var faces = text.Split('\n')
            .Where(l => l.StartsWith("f ", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, faces.Count);
        Assert.Equal("f 1//1 2//2 3//3", faces[0].Trim());
        Assert.Equal("f 4//4 5//5 6//6", faces[1].Trim());
    }

    /// <summary>Every object arrives under the name the model author gave it.</summary>
    /// <remarks>
    /// The point of using OBJ rather than a format that merges everything: a loss
    /// itemisation names <c>frontPlateRight</c>, and a figure in which that electrode is
    /// one anonymous lump among sixteen cannot be pointed at.
    /// </remarks>
    [Fact]
    public void EachSurfaceKeepsItsName()
    {
        var text = ObjWriter.Write([Triangle("rodYPlus", 0.0), Triangle("ring-17", 10.0)]);

        var names = text.Split('\n')
            .Where(l => l.StartsWith("o ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim())
            .ToList();

        output.WriteLine(string.Join(", ", names));

        Assert.Equal(["rodYPlus", "ring-17"], names);
    }

    /// <summary>A name with a space in it does not become two tokens.</summary>
    /// <remarks>
    /// OBJ has no quoting and a name runs to the end of the line, so <c>ring 17</c> would
    /// load as an object called <c>ring</c> — silently misnamed rather than refused.
    /// </remarks>
    [Fact]
    public void ANameWithASpaceIsMadeSafe()
    {
        var text = ObjWriter.Write([Triangle("front plate right", 0.0)]);

        Assert.Contains("o front_plate_right", text, StringComparison.Ordinal);
        Assert.DoesNotContain("o front plate right", text, StringComparison.Ordinal);
    }

    /// <summary>An empty surface contributes nothing and does not shift the indices.</summary>
    /// <remarks>
    /// <b>The subtle half.</b> Skipping an empty object is obvious; skipping it without also
    /// skipping its (zero) vertices is what keeps the running offset correct. An electrode
    /// too small to mesh at the sampled resolution produces one of these, so it is not a
    /// hypothetical case.
    /// </remarks>
    [Fact]
    public void AnEmptySurfaceIsSkippedWithoutDisturbingTheIndices()
    {
        var empty = new NamedSurface("nothing", [], [], []);
        var text = ObjWriter.Write([Triangle("first", 0.0), empty, Triangle("second", 10.0)]);

        var faces = text.Split('\n')
            .Where(l => l.StartsWith("f ", StringComparison.Ordinal))
            .ToList();

        output.WriteLine(text);

        Assert.Equal(2, faces.Count);
        Assert.Equal("f 4//4 5//5 6//6", faces[1].Trim());
        Assert.DoesNotContain("o nothing", text, StringComparison.Ordinal);
    }

    /// <summary>Normals are counted separately from positions.</summary>
    /// <remarks>
    /// <para>
    /// <b>OBJ keeps two counters and a writer naturally keeps one.</b> While every object
    /// carries normals the two advance together and a single offset is indistinguishable
    /// from the correct thing — which is why this survived being written, reviewed and
    /// exercised on a real sixteen-electrode model.
    /// </para>
    /// <para>
    /// The first object here has no normals, so from the second object on the two counters
    /// differ by exactly its vertex count. A shared offset points every later face at
    /// normals belonging to something else: no parse error, no missing geometry, just
    /// lighting that is quietly wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void NormalIndicesAreCountedSeparatelyFromPositions()
    {
        var flat = new NamedSurface("flat", [0, 0, 0, 1, 0, 0, 0, 1, 0], [], [0, 1, 2]);

        var text = ObjWriter.Write([flat, Triangle("shaded", 10.0)]);

        output.WriteLine(text);

        var faces = text.Split('\n')
            .Where(l => l.StartsWith("f ", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, faces.Count);

        // No normals on the first object, so its face carries bare position indices.
        Assert.Equal("f 1 2 3", faces[0].Trim());

        // The second object's positions continue from 4, but its normals are the FIRST
        // normals in the file and so start at 1. A shared counter would write 4//4 5//5 6//6.
        Assert.Equal("f 4//1 5//2 6//3", faces[1].Trim());
    }

    /// <summary>The geometry survives the round trip, read back as a parser would.</summary>
    /// <remarks>
    /// Positions are compared as numbers rather than as text, because what matters is that
    /// a reader recovers the vertex — a G9 round trip is the mechanism, not the claim.
    /// </remarks>
    [Fact]
    public void ThePositionsReadBackExactly()
    {
        var mesh = new NamedSurface(
            "probe",
            [1.5, -2.25, 1e-3, 625.0, 0.0, 335.0, -0.125, 3.0, 4.0],
            [1, 0, 0, 0, 1, 0, 0, 0, 1],
            [0, 1, 2]);

        var text = ObjWriter.Write([mesh]);

        var read = text.Split('\n')
            .Where(l => l.StartsWith("v ", StringComparison.Ordinal))
            .SelectMany(l => l[2..].Trim().Split(' '))
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture))
            .ToList();

        output.WriteLine(string.Join(", ", read));

        Assert.Equal(mesh.VerticesMm.Count, read.Count);

        for (var i = 0; i < read.Count; i++)
        {
            Assert.Equal(mesh.VerticesMm[i], read[i], 9);
        }
    }

    /// <summary>The file says what unit it is in.</summary>
    /// <remarks>
    /// OBJ carries no unit, and most renderers treat one unit as one metre — which would
    /// make a 625 mm analyser 0.6 units long. §9 refuses an unlabelled quantity everywhere
    /// else in this platform and a mesh file is not an exception.
    /// </remarks>
    [Fact]
    public void TheHeaderStatesTheUnit()
    {
        var text = ObjWriter.Write([Triangle("only", 0.0)], ["model: probe"]);

        output.WriteLine(text.Split('\n')[0]);
        output.WriteLine(text.Split('\n')[1]);

        Assert.Contains("# units: millimetres", text, StringComparison.Ordinal);
        Assert.Contains("# model: probe", text, StringComparison.Ordinal);
    }
}
