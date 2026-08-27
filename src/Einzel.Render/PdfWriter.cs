using System.Globalization;
using System.Text;

namespace Einzel.Render;

/// <summary>Writes a scene as a PDF.</summary>
/// <remarks>
/// <para>
/// Written here rather than taken from a library, and the reason is LIC-1 as much
/// as weight: the capable PDF libraries in .NET are variously GPL, AGPL, or
/// dual-licensed in a way that has to be re-checked per release, and a figure
/// writer is not where this project wants a licence question. What a section
/// figure needs is a small subset - paths, strokes, fills, and text in a base
/// font - and that subset is a few hundred lines of a format that has been stable
/// since 1993.
/// </para>
/// <para>
/// Text is set with the PDF text operators in Helvetica, one of the fourteen fonts
/// every reader is required to have, so nothing is embedded and RND-6 holds: the
/// labels are selectable and editable in the output rather than being outlines
/// that happen to look like letters.
/// </para>
/// <para>
/// PDF measures in points and puts the origin at the bottom left; the scene is in
/// millimetres from the top left. Both conversions happen once, in the content
/// stream's transformation matrix, rather than per primitive.
/// </para>
/// </remarks>
public static class PdfWriter
{
    /// <summary>Millimetres to PostScript points.</summary>
    private const double PointsPerMm = 72.0 / 25.4;

    /// <summary>Renders a scene as a PDF document.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The document bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public static byte[] Write(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var invariant = CultureInfo.InvariantCulture;
        var content = new StringBuilder(4096);

        var widthPt = scene.WidthMm * PointsPerMm;
        var heightPt = scene.HeightMm * PointsPerMm;

        // Millimetres, y down from the top left, in one matrix: scale to points and
        // flip about the page height. Every coordinate below is then written in the
        // scene's own units.
        content.Append(invariant, $"q\n{PointsPerMm:G8} 0 0 {-PointsPerMm:G8} 0 {heightPt:G8} cm\n");

        content.Append(invariant, $"1 1 1 rg\n0 0 {scene.WidthMm:G6} {scene.HeightMm:G6} re f\n");

        foreach (var path in scene.Paths)
        {
            if (path.Points.Count == 0)
            {
                continue;
            }

            WritePath(content, invariant, path);
        }

        content.Append("Q\n");

        foreach (var run in scene.Texts)
        {
            WriteText(content, invariant, run, heightPt);
        }

        return Assemble(content.ToString(), widthPt, heightPt, scene.Provenance);
    }

    private static void WritePath(StringBuilder content, CultureInfo invariant, ScenePath path)
    {
        content.Append("q\n");

        if (path.Style.Fill is { } fill)
        {
            var (r, g, b) = Colour(fill);
            content.Append(invariant, $"{r:G4} {g:G4} {b:G4} rg\n");
        }

        if (path.Style.Stroke is { } stroke)
        {
            var (r, g, b) = Colour(stroke);
            content.Append(invariant, $"{r:G4} {g:G4} {b:G4} RG\n");
            content.Append(invariant, $"{path.Style.WidthMm:G6} w\n1 j\n1 J\n");

            var dash = path.Style.Dash switch
            {
                DashStyle.Dashed => $"[{path.Style.WidthMm * 6.0:G4} {path.Style.WidthMm * 4.0:G4}] 0 d\n",
                DashStyle.Dotted => $"[{path.Style.WidthMm:G4} {path.Style.WidthMm * 3.0:G4}] 0 d\n",
                _ => null,
            };

            if (dash is not null)
            {
                content.Append(dash);
            }
        }

        for (var i = 0; i < path.Points.Count; i++)
        {
            content.Append(
                invariant,
                $"{path.Points[i].X:G6} {path.Points[i].Y:G6} {(i == 0 ? "m" : "l")}\n");
        }

        if (path.Closed)
        {
            content.Append("h\n");
        }

        content.Append(
            (path.Style.Fill, path.Style.Stroke) switch
            {
                (not null, not null) => "B\n",
                (not null, null) => "f\n",
                (null, not null) => "S\n",
                _ => "n\n",
            });

        content.Append("Q\n");
    }

    private static void WriteText(
        StringBuilder content, CultureInfo invariant, SceneText run, double heightPt)
    {
        var (r, g, b) = Colour(run.Colour);

        // Helvetica's advance widths are not carried here, so a centred or
        // right-aligned run is placed from an average-width estimate. That is
        // honest for a label and wrong for a table; the estimate is 0.52 em, which
        // is close for mixed-case Latin text.
        var width = run.Text.Length * run.SizePt * 0.52;

        var shift = run.Anchor switch
        {
            TextAnchor.Middle => -0.5 * width,
            TextAnchor.End => -width,
            _ => 0.0,
        };

        var x = (run.At.X * PointsPerMm) + shift;
        var y = heightPt - (run.At.Y * PointsPerMm);

        content.Append("BT\n");
        content.Append(invariant, $"/F1 {run.SizePt:G6} Tf\n");
        content.Append(invariant, $"{r:G4} {g:G4} {b:G4} rg\n");
        content.Append(invariant, $"1 0 0 1 {x:G8} {y:G8} Tm\n");
        content.Append('(').Append(EscapeText(run.Text)).Append(") Tj\n");
        content.Append("ET\n");
    }

    private static byte[] Assemble(
        string content, double widthPt, double heightPt, IReadOnlyList<string> provenance)
    {
        var invariant = CultureInfo.InvariantCulture;
        var contentBytes = Encoding.ASCII.GetByteCount(content);

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            string.Create(
                invariant,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt:G8} {heightPt:G8}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            string.Create(invariant, $"<< /Length {contentBytes} >>\nstream\n{content}endstream"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Producer (Einzel) /Title (" + EscapeText(string.Join("; ", provenance)) + ") >>",
        };

        var document = new StringBuilder(contentBytes + 2048);
        var offsets = new List<int>(objects.Count);

        document.Append("%PDF-1.4\n");

        // A binary comment, so a transfer that guesses at the file type treats this
        // as binary rather than mangling line endings through it.
        document.Append("%âãÏÓ\n");

        foreach (var line in provenance)
        {
            document.Append("% ").Append(line.Replace('\n', ' ')).Append('\n');
        }

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(document.Length);
            document.Append(invariant, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var startXref = document.Length;

        document.Append(invariant, $"xref\n0 {objects.Count + 1}\n");
        document.Append("0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            document.Append(invariant, $"{offset:D10} 00000 n \n");
        }

        document.Append(
            invariant,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info {objects.Count} 0 R >>\n");

        document.Append(invariant, $"startxref\n{startXref}\n%%EOF\n");

        return Encoding.Latin1.GetBytes(document.ToString());
    }

    private static (double R, double G, double B) Colour(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            return (0.0, 0.0, 0.0);
        }

        static double Channel(string hex, int at) =>
            Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;

        return (Channel(hex, 1), Channel(hex, 3), Channel(hex, 5));
    }

    private static string EscapeText(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal);
}
