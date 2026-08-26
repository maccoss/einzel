using System.Globalization;
using System.Text;

namespace Einzel.Render;

/// <summary>Writes a scene as SVG.</summary>
/// <remarks>
/// <para>
/// Paths and text runs, never pixels and never glyph outlines (RND-3, RND-6). The
/// page is measured in millimetres and the user units are millimetres too, so a
/// figure placed in a document is the size it says it is rather than the size a
/// nominal 96 dots per inch happened to make it.
/// </para>
/// <para>
/// Provenance goes in as an XML comment and as a visible stamp, because GRD-12
/// requires a rendering never to look more precise than its data and metadata
/// nobody opens is not provenance - the same argument RND-10 makes for video.
/// </para>
/// </remarks>
public static class SvgWriter
{
    /// <summary>Renders a scene as an SVG document.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public static string Write(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder(4096);

        text.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");

        if (scene.Provenance.Count > 0)
        {
            text.Append("<!--\n");

            foreach (var line in scene.Provenance)
            {
                text.Append("  ").Append(line.Replace("--", "- -", StringComparison.Ordinal)).Append('\n');
            }

            text.Append("-->\n");
        }

        text.Append(string.Create(
            invariant,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{scene.WidthMm:G6}mm\" height=\"{scene.HeightMm:G6}mm\" viewBox=\"0 0 {scene.WidthMm:G6} {scene.HeightMm:G6}\">\n"));

        text.Append(string.Create(
            invariant,
            $"  <rect x=\"0\" y=\"0\" width=\"{scene.WidthMm:G6}\" height=\"{scene.HeightMm:G6}\" fill=\"#ffffff\"/>\n"));

        foreach (var layer in Layers(scene))
        {
            text.Append(invariant, $"  <g id=\"{Escape(layer)}\">\n");

            foreach (var path in scene.Paths)
            {
                if (path.Layer != layer || path.Points.Count == 0)
                {
                    continue;
                }

                WritePath(text, invariant, path);
            }

            foreach (var run in scene.Texts)
            {
                if (run.Layer != layer)
                {
                    continue;
                }

                WriteText(text, invariant, run);
            }

            text.Append("  </g>\n");
        }

        text.Append("</svg>\n");

        return text.ToString();
    }

    private static List<string> Layers(Scene scene)
    {
        var seen = new List<string>();

        foreach (var path in scene.Paths)
        {
            if (!seen.Contains(path.Layer))
            {
                seen.Add(path.Layer);
            }
        }

        foreach (var run in scene.Texts)
        {
            if (!seen.Contains(run.Layer))
            {
                seen.Add(run.Layer);
            }
        }

        return seen;
    }

    private static void WritePath(StringBuilder text, CultureInfo invariant, ScenePath path)
    {
        text.Append("    <path d=\"");

        for (var i = 0; i < path.Points.Count; i++)
        {
            text.Append(i == 0 ? 'M' : 'L');
            text.Append(invariant, $"{path.Points[i].X:G6} {path.Points[i].Y:G6}");

            if (i + 1 < path.Points.Count)
            {
                text.Append(' ');
            }
        }

        if (path.Closed)
        {
            text.Append('Z');
        }

        text.Append("\" fill=\"").Append(path.Style.Fill ?? "none").Append('"');

        if (path.Style.Stroke is { } stroke)
        {
            text.Append(string.Create(
                invariant, $" stroke=\"{stroke}\" stroke-width=\"{path.Style.WidthMm:G4}\""));
            text.Append(" stroke-linejoin=\"round\" stroke-linecap=\"round\"");

            var dash = Dashes(path.Style);

            if (dash is not null)
            {
                text.Append(" stroke-dasharray=\"").Append(dash).Append('"');
            }
        }

        if (path.Style.Opacity < 1.0)
        {
            text.Append(invariant, $" opacity=\"{path.Style.Opacity:G4}\"");
        }

        text.Append("/>\n");
    }

    private static void WriteText(StringBuilder text, CultureInfo invariant, SceneText run)
    {
        var anchor = run.Anchor switch
        {
            TextAnchor.Middle => "middle",
            TextAnchor.End => "end",
            _ => "start",
        };

        text.Append(string.Create(
            invariant,
            $"    <text x=\"{run.At.X:G6}\" y=\"{run.At.Y:G6}\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"{run.SizePt * 25.4 / 72.0:G4}\" fill=\"{run.Colour}\" text-anchor=\"{anchor}\">"));

        text.Append(Escape(run.Text)).Append("</text>\n");
    }

    private static string? Dashes(PathStyle style) => style.Dash switch
    {
        DashStyle.Dashed => $"{style.WidthMm * 6.0:G4} {style.WidthMm * 4.0:G4}",
        DashStyle.Dotted => $"{style.WidthMm:G4} {style.WidthMm * 3.0:G4}",
        _ => null,
    };

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
