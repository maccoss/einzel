using System.Text.Json.Serialization;

namespace Einzel.Render;

/// <summary>What kind of figure a spec asks for.</summary>
/// <remarks>
/// Written and read as a name. A render spec is a document a person edits and an
/// agent writes (RND-2), and a format that spells this as <c>0</c> is a format
/// nobody can author by hand or check by reading.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<FigureKind>))]
public enum FigureKind
{
    /// <summary>A plane cut through the instrument, drawn as line work.</summary>
    Section,
}

/// <summary>An output format.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FigureFormat>))]
public enum FigureFormat
{
    /// <summary>Scalable Vector Graphics.</summary>
    Svg,

    /// <summary>Portable Document Format.</summary>
    Pdf,
}

/// <summary>The plane a section is cut on.</summary>
/// <param name="Normal">The plane normal, as three components.</param>
/// <param name="OffsetMm">How far along the normal from the origin, in millimetres.</param>
/// <param name="AcrossMm">
/// Which direction should run across the page, as three components, or null to
/// let the renderer choose.
/// </param>
public sealed record PlaneSpec(
    IReadOnlyList<double> Normal,
    double OffsetMm = 0.0,
    IReadOnlyList<double>? AcrossMm = null);

/// <summary>What to draw, as text that lives beside the model.</summary>
/// <remarks>
/// <para>
/// RND-2: a render spec is text, lives in <c>figures/</c>, and is versioned with
/// the model. The figure in a paper is then regenerable from the repository rather
/// than being a file someone once exported and can no longer reproduce - which is
/// the failure this replaces, since the incumbent's output is essentially
/// screenshots and publication figures get redrawn by hand in an illustration
/// program, after which the redrawn figure drifts from the model it depicts.
/// </para>
/// <para>
/// It is also the AGT-2 seam. A future figure composer edits one of these and the
/// CLI executes it identically, so nothing about composing a figure can end up
/// existing only in a window.
/// </para>
/// </remarks>
public sealed record RenderSpec
{
    /// <summary>The spec format version.</summary>
    public string RenderSpecVersion { get; init; } = "0.1";

    /// <summary>What kind of figure.</summary>
    public FigureKind Kind { get; init; } = FigureKind.Section;

    /// <summary>The model to draw, as a path relative to the project root.</summary>
    public string? Model { get; init; }

    /// <summary>The plane to cut on. Defaults to the plane of a 2D model.</summary>
    public PlaneSpec? Plane { get; init; }

    /// <summary>Page width, in millimetres. The height follows from the aspect ratio.</summary>
    public double WidthMm { get; init; } = 160.0;

    /// <summary>Blank margin around the drawing, in millimetres.</summary>
    public double MarginMm { get; init; } = 10.0;

    /// <summary>How many equipotentials to draw, or zero for none.</summary>
    public int Equipotentials { get; init; } = 12;

    /// <summary>Sample columns across the section when contouring.</summary>
    /// <remarks>
    /// Contour quality, not field quality. The field was solved on its own grid;
    /// this is how finely that field is resampled to trace a level set, and it
    /// trades file size against how polygonal a curve looks.
    /// </remarks>
    public int SampleColumns { get; init; } = 400;

    /// <summary>Whether to fly the model's ion and draw its path.</summary>
    public bool Trajectory { get; init; } = true;

    /// <summary>
    /// How finely to sample the trajectory before decimating it.
    /// </summary>
    /// <remarks>
    /// Not how many points the drawing keeps - that follows from the decimation
    /// bound. This is how densely the flight is sampled so that the bound has
    /// something to bound; a curve drawn at whatever cadence the model exports VTU
    /// at is a drawing of the sampling interval rather than of the optics.
    /// </remarks>
    public int TrajectorySamples { get; init; } = 2000;

    /// <summary>
    /// Decimation bound as a fraction of the drawing's extent (ACC-7).
    /// </summary>
    /// <remarks>
    /// The default is ACC-7's own figure, one part in a thousand. It is recorded in
    /// every output per GRD-12, so a reader can tell what the drawing does not
    /// claim; a tolerance that is applied and not stated is exactly the case that
    /// requirement exists for.
    /// </remarks>
    public double DecimationFraction { get; init; } = 1e-3;

    /// <summary>The output format.</summary>
    public FigureFormat Format { get; init; } = FigureFormat.Svg;

    /// <summary>An optional caption drawn under the figure.</summary>
    public string? Caption { get; init; }

    /// <summary>Whether to draw a scale bar.</summary>
    public bool ScaleBar { get; init; } = true;

    /// <summary>Whether to draw the axis line, where the section crosses it.</summary>
    /// <remarks>
    /// A construction line rather than a part, and the line an axisymmetric device
    /// is drawn about. Drawn only when the section actually crosses it, so a cut
    /// off to one side does not gain a line that means nothing there.
    /// </remarks>
    public bool DrawAxis { get; init; } = true;
}
