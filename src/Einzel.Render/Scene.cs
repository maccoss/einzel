namespace Einzel.Render;

/// <summary>A point on the page, in millimetres from the top-left corner.</summary>
/// <param name="X">Distance right, in millimetres.</param>
/// <param name="Y">Distance down, in millimetres.</param>
/// <remarks>
/// Y increases downward because both output formats and every illustration
/// program agree on that, and the one place a flip belongs is the world-to-page
/// map. A renderer that flips per-primitive gets it wrong for one of them.
/// </remarks>
public readonly record struct PagePoint(double X, double Y);

/// <summary>How a stroke is dashed.</summary>
public enum DashStyle
{
    /// <summary>Unbroken.</summary>
    Solid,

    /// <summary>Long dashes.</summary>
    Dashed,

    /// <summary>Short dots.</summary>
    Dotted,
}

/// <summary>How a path is painted.</summary>
/// <param name="Stroke">Stroke colour as a hex triplet, or null for no stroke.</param>
/// <param name="WidthMm">Stroke width, in millimetres.</param>
/// <param name="Fill">Fill colour as a hex triplet, or null for no fill.</param>
/// <param name="Dash">Dash pattern.</param>
/// <param name="Opacity">Opacity, 0 to 1.</param>
public sealed record PathStyle(
    string? Stroke,
    double WidthMm = 0.2,
    string? Fill = null,
    DashStyle Dash = DashStyle.Solid,
    double Opacity = 1.0);

/// <summary>A polyline or polygon on the page.</summary>
/// <param name="Points">The vertices, in page millimetres.</param>
/// <param name="Closed">Whether the last vertex joins back to the first.</param>
/// <param name="Style">How it is painted.</param>
/// <param name="Layer">What this path depicts, carried to the output as a group.</param>
public sealed record ScenePath(
    IReadOnlyList<PagePoint> Points,
    bool Closed,
    PathStyle Style,
    string Layer = "geometry");

/// <summary>Where a text run sits relative to its anchor point.</summary>
public enum TextAnchor
{
    /// <summary>The anchor is the left end of the run.</summary>
    Start,

    /// <summary>The anchor is the centre of the run.</summary>
    Middle,

    /// <summary>The anchor is the right end of the run.</summary>
    End,
}

/// <summary>A text run on the page.</summary>
/// <param name="Text">The characters.</param>
/// <param name="At">Where the run is anchored, in page millimetres.</param>
/// <param name="SizePt">Type size, in points.</param>
/// <param name="Anchor">Which end of the run the anchor is.</param>
/// <param name="Colour">Colour as a hex triplet.</param>
/// <param name="Layer">What this text is for, carried to the output as a group.</param>
/// <remarks>
/// A run, not an outline. RND-6 requires labels, dimensions and axis annotations to
/// stay selectable and editable in the output, so a figure can be relabelled for a
/// different venue without regenerating it. Both writers emit real text operators;
/// neither converts a glyph to a path.
/// </remarks>
public sealed record SceneText(
    string Text,
    PagePoint At,
    double SizePt = 7.0,
    TextAnchor Anchor = TextAnchor.Start,
    string Colour = "#1a1a1a",
    string Layer = "labels");

/// <summary>
/// A finished figure, in page coordinates and independent of output format.
/// </summary>
/// <param name="WidthMm">Page width, in millimetres.</param>
/// <param name="HeightMm">Page height, in millimetres.</param>
/// <param name="Paths">Paths, in paint order.</param>
/// <param name="Texts">Text runs, painted above the paths.</param>
/// <param name="Provenance">
/// Lines recorded in the output describing what produced it and what it does not
/// claim: engine version, model hash, decimation tolerance, and any warnings.
/// </param>
/// <remarks>
/// <para>
/// The seam between what the figure is and what file it becomes. SVG and PDF are
/// two writers over this one structure, so a figure cannot come out different in
/// one format than in the other - which is the failure mode of a pipeline where
/// each format re-derives the drawing.
/// </para>
/// <para>
/// Paths rather than pixels, per RND-3. The point of vector output is that a
/// publication figure stays a line drawing all the way from the geometry to the
/// page, so it never has to be redrawn by hand and then drift from the model it
/// depicts.
/// </para>
/// </remarks>
public sealed record Scene(
    double WidthMm,
    double HeightMm,
    IReadOnlyList<ScenePath> Paths,
    IReadOnlyList<SceneText> Texts,
    IReadOnlyList<string> Provenance);
