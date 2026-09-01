using System.Globalization;
using System.Text;

namespace Einzel.Render;

/// <summary>One named surface, as a mesh writer takes it.</summary>
/// <param name="Name">What the model author called it, used as the object name.</param>
/// <param name="VerticesMm">Consecutive x, y, z triples, in millimetres.</param>
/// <param name="Normals">One outward unit normal per vertex.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <param name="Note">Provenance for the object, written as a comment above it.</param>
public sealed record NamedSurface(
    string Name,
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles,
    string? Note = null);

/// <summary>
/// Writes conductor surfaces as Wavefront OBJ, so anything can render the geometry.
/// </summary>
/// <remarks>
/// <para>
/// <b>The surfaces were already extracted headlessly and could not get out of the
/// program.</b> <see cref="Surfaces"/> builds them from each electrode's own signed
/// distance and its tests run on the Linux runner, but the only consumer was the
/// Windows viewport — so the one artifact that lets an external renderer make a picture
/// of the geometry was reachable only through the shell. That is invariant 1 the wrong
/// way round: nothing below the shell may depend on it, and here something useful below
/// the shell was only usable through it.
/// </para>
/// <para>
/// <b>OBJ rather than a VTK format, and the reason is the audience.</b> This engine
/// already writes VTK ImageData and UnstructuredGrid, and consistency would argue for
/// PolyData — but those exist to be read by ParaView for analysis, and this exists to be
/// read by a renderer for a picture. OBJ names each object, so every electrode arrives
/// under the name the model author gave it, which is the same name a loss itemisation or
/// an error message uses. ParaView reads it too, though it merges the objects.
/// </para>
/// <para>
/// <b>Hand-written, for the reason the PDF writer is</b> (LIC-1): the format is a few
/// line kinds and taking a dependency to emit them would put a licence question where
/// none needs to be.
/// </para>
/// <para>
/// <b>Millimetres, stated in the header.</b> OBJ carries no unit and most renderers treat
/// one unit as one metre, which would make an analyser 0.6 units long and awkward to light.
/// Millimetres match every other human-facing number this CLI prints, and a file that does
/// not say which it used is the ambiguity §9 refuses everywhere else.
/// </para>
/// </remarks>
public static class ObjWriter
{
    /// <summary>Writes the surfaces as one OBJ document.</summary>
    /// <param name="parts">The named surfaces, in the order they should appear.</param>
    /// <param name="header">Provenance for the file as a whole, one line per entry.</param>
    /// <returns>The OBJ text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parts"/> is null.</exception>
    public static string Write(
        IReadOnlyList<NamedSurface> parts, IReadOnlyList<string>? header = null)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var text = new StringBuilder();

        text.Append("# Wavefront OBJ written by einzel\n");
        text.Append("# units: millimetres\n");

        foreach (var line in header ?? [])
        {
            text.Append("# ").Append(line.Replace('\n', ' ')).Append('\n');
        }

        // OBJ indices are one-based and run across the whole file rather than restarting
        // per object, so the offset has to be carried. Getting that wrong produces a file
        // that loads without complaint and draws the second object using the first
        // object's vertices.
        var vertexBase = 1;

        // Positions and normals have SEPARATE counters in OBJ. One offset serving both is
        // correct exactly while every object carries normals, and silently wrong from the
        // first object that does not: every later object then points at normals belonging
        // to something else, which is shading nonsense rather than a parse error.
        var normalBase = 1;

        foreach (var part in parts)
        {
            if (part.Triangles.Count == 0)
            {
                continue;
            }

            if (part.Note is { } note)
            {
                text.Append("# ").Append(note.Replace('\n', ' ')).Append('\n');
            }

            text.Append("o ").Append(Sanitised(part.Name)).Append('\n');

            for (var i = 0; i + 2 < part.VerticesMm.Count; i += 3)
            {
                text.Append("v ")
                    .Append(Number(part.VerticesMm[i])).Append(' ')
                    .Append(Number(part.VerticesMm[i + 1])).Append(' ')
                    .Append(Number(part.VerticesMm[i + 2])).Append('\n');
            }

            var hasNormals = part.Normals.Count == part.VerticesMm.Count;

            if (hasNormals)
            {
                for (var i = 0; i + 2 < part.Normals.Count; i += 3)
                {
                    text.Append("vn ")
                        .Append(Number(part.Normals[i])).Append(' ')
                        .Append(Number(part.Normals[i + 1])).Append(' ')
                        .Append(Number(part.Normals[i + 2])).Append('\n');
                }
            }

            for (var i = 0; i + 2 < part.Triangles.Count; i += 3)
            {
                var a = part.Triangles[i] + vertexBase;
                var b = part.Triangles[i + 1] + vertexBase;
                var c = part.Triangles[i + 2] + vertexBase;

                text.Append("f ");
                Face(text, a, part.Triangles[i] + normalBase, hasNormals);
                text.Append(' ');
                Face(text, b, part.Triangles[i + 1] + normalBase, hasNormals);
                text.Append(' ');
                Face(text, c, part.Triangles[i + 2] + normalBase, hasNormals);
                text.Append('\n');
            }

            vertexBase += part.VerticesMm.Count / 3;

            if (hasNormals)
            {
                normalBase += part.Normals.Count / 3;
            }
        }

        return text.ToString();
    }

    /// <summary>One face vertex, with its normal where there is one.</summary>
    /// <param name="text">Where to write it.</param>
    /// <param name="vertex">One-based position index, counted across the whole file.</param>
    /// <param name="normal">One-based normal index, counted separately from positions.</param>
    /// <param name="hasNormals">Whether this object supplied normals at all.</param>
    private static void Face(StringBuilder text, int vertex, int normal, bool hasNormals)
    {
        text.Append(vertex.ToString(CultureInfo.InvariantCulture));

        if (hasNormals)
        {
            // The empty texture slot is required: "v//vn" is a vertex and its normal,
            // where "v/vn" would be read as a vertex and a texture coordinate.
            text.Append("//").Append(normal.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A number OBJ can read, and the same one on every machine.</summary>
    private static string Number(double value) =>
        value.ToString("G9", CultureInfo.InvariantCulture);

    /// <summary>An object name with nothing in it that would end the line.</summary>
    /// <remarks>
    /// OBJ has no quoting, so a name is whatever runs to the end of the line. An electrode
    /// called <c>ring 17</c> would otherwise become an object called <c>ring</c> with a
    /// stray token after it, which loads and is silently misnamed.
    /// </remarks>
    private static string Sanitised(string name)
    {
        var clean = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            clean.Append(char.IsWhiteSpace(c) ? '_' : c);
        }

        return clean.Length == 0 ? "unnamed" : clean.ToString();
    }
}
