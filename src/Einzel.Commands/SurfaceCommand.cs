using System.Globalization;

using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Results;
using Einzel.Io;
using Einzel.Project;
using Einzel.Render;

namespace Einzel.Commands;

/// <summary>What the height of a potential surface stands for.</summary>
public static class SurfaceHeight
{
    /// <summary>The electric potential, in volts. The default.</summary>
    public const string Potential = "potential";

    /// <summary>
    /// The model's ion's potential energy, q times phi, in electronvolts: a positive ion's hill
    /// is a negative ion's valley.
    /// </summary>
    public const string Energy = "potential-energy";

    /// <summary>
    /// The words a caller chooses a height with, the default first: <c>potential</c> and
    /// <c>energy</c>.
    /// </summary>
    /// <remarks>
    /// What is typed, as against what is recorded: a surface's outcome names its height
    /// <see cref="Potential"/> or <see cref="Energy"/>, and <c>potential-energy</c> is that
    /// record's word, not a second spelling of the choice.
    /// </remarks>
    public static readonly IReadOnlyList<string> Spellings = ["potential", "energy"];

    /// <summary>The height one of the <see cref="Spellings"/> chooses.</summary>
    /// <param name="spelling">One of the <see cref="Spellings"/>, exactly.</param>
    /// <returns><see cref="Potential"/> or <see cref="Energy"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="spelling"/> is not one of them.</exception>
    public static string Parse(string spelling)
    {
        ArgumentNullException.ThrowIfNull(spelling);

        if (string.Equals(spelling, Spellings[0], StringComparison.Ordinal))
        {
            return Potential;
        }

        if (string.Equals(spelling, Spellings[1], StringComparison.Ordinal))
        {
            return Energy;
        }

        throw new ArgumentException(
            $"a surface's height is chosen as {string.Join(" or ", Spellings)}; '{spelling}' is neither", nameof(spelling));
    }
}

/// <summary>Where, when and how to draw a potential surface.</summary>
/// <param name="Plane">"xy", "xz" or "yz", or absent for the model's own plane.</param>
/// <param name="OffsetMm">
/// How far along the plane's normal, in millimetres; absent for the middle of the model's domain
/// along that normal. For a cross-section or an axisymmetric model drawn on its own plane, xy,
/// that is z = 0, the plane the solve is declared in.
/// </param>
/// <param name="BoxMm">
/// The sampled rectangle as four numbers in the plane's own axis order (for "xz": x0, z0, x1, z1),
/// in millimetres; absent for the solve domain's extent in the plane.
/// </param>
/// <param name="Samples">Samples along the longer side.</param>
/// <param name="DeepSamples">
/// Samples along the shorter side; absent for the same spacing as the longer side, but never
/// fewer than <see cref="SurfaceCommand.MinimumDeepSamples"/> (or <paramref name="Samples"/>, if
/// that is fewer), so a long thin box still resolves across its width.
/// </param>
/// <param name="MeshLines">Drawn mesh lines along the longer side, so the wire mesh reads as a mesh.</param>
/// <param name="Height">One of the <see cref="SurfaceHeight"/> values.</param>
/// <param name="Relief">
/// The drawn height of the whole range, as a fraction of the box's longer side; used only when no
/// explicit scale is given.
/// </param>
/// <param name="HeightMmPerUnit">
/// An explicit scale: millimetres of drawn height per volt (or per electronvolt for the energy).
/// </param>
/// <param name="AtUs">
/// The instant, in microseconds, or absent when none was asked for. A static field is the same at
/// every instant, so one given for it changes nothing and is warned about.
/// </param>
/// <param name="CycleMean">
/// Whether to draw the potential averaged over one drive cycle. Refused for a field with no drive
/// - a static one, or a sequence whose states are all static - which has no cycle to average.
/// </param>
public sealed record SurfaceRequest(
    string? Plane = null,
    double? OffsetMm = null,
    IReadOnlyList<double>? BoxMm = null,
    int Samples = SurfaceCommand.DefaultSamples,
    int MeshLines = SurfaceCommand.DefaultMeshLines,
    string Height = SurfaceHeight.Potential,
    double Relief = SurfaceCommand.DefaultRelief,
    double? HeightMmPerUnit = null,
    double? AtUs = null,
    bool CycleMean = false,
    int? DeepSamples = null);

/// <summary>A conductor met by the plane, drawn as a flat plateau at its own potential.</summary>
/// <param name="Conductor">
/// Its label: its name, and where two field elements each have a conductor of that name on the
/// plane, the name followed by the element, "plate (element 1)", so the two are told apart.
/// </param>
/// <param name="PotentialV">
/// The potential it is drawn at: what the electrode holds at the instant drawn (or its mean over
/// the cycle, under <c>--cycle-mean</c>), from the solve's own coefficients - not a reading of the
/// field, which inside metal carries anything else the model sums there.
/// </param>
/// <param name="Height">The same in the drawn quantity (volts or electronvolts).</param>
/// <param name="Samples">How many samples fell inside it.</param>
/// <param name="LowestV">
/// The lowest potential the summed field reads anywhere inside it on the plane, which differs from
/// <paramref name="PotentialV"/> where another element is summed inside the metal.
/// </param>
/// <param name="HighestV">The highest the summed field reads inside it.</param>
/// <param name="Element">The field element it belongs to, by index in the model; absent where unknown.</param>
/// <param name="Name">Its name as the model wrote it, which is unique only within its element.</param>
public sealed record SurfacePlateau(
    string Conductor,
    double PotentialV,
    double Height,
    int Samples,
    double LowestV,
    double HighestV,
    int? Element = null,
    string? Name = null)
{
    /// <summary>
    /// The median of the summed field's readings over the deeper half of the samples inside the
    /// conductor: what the plateau used to be drawn at, kept so a reader can see how far the
    /// sum strays from the conductor's own potential.
    /// </summary>
    public double SampledV { get; init; } = PotentialV;
}

/// <summary>A sampled potential surface, before anything is drawn.</summary>
/// <remarks>
/// Values are stored across-major: node (i, j) is <c>i * Deep.Count + j</c>, where i runs along
/// <see cref="AcrossAxis"/> and j along <see cref="DeepAxis"/>.
/// </remarks>
public sealed record PotentialSurface
{
    /// <summary>The plane, as two axis letters.</summary>
    public required string Plane { get; init; }

    /// <summary>Its offset along the normal, in millimetres.</summary>
    public required double OffsetMm { get; init; }

    /// <summary>The model axis running across the picture: the box's longer side.</summary>
    public required string AcrossAxis { get; init; }

    /// <summary>The other in-plane axis, the picture's depth.</summary>
    public required string DeepAxis { get; init; }

    /// <summary>
    /// Which way <see cref="DeepAxis"/> runs in the picture's frame: the picture's z is this
    /// times the deep coordinate. Plus or minus one, chosen so that (across, height, picture z)
    /// is a right-handed frame with the plane's own normal as the height - a rotation of the
    /// model, never its mirror image. Seen from above, with across to the right, the plane is
    /// then the way round it is in the model looked at from its positive normal: for xy, x
    /// right and y up the screen, as <c>render section</c> draws it.
    /// </summary>
    public double DeepSign { get; init; } = 1.0;

    /// <summary>
    /// The size of the potentials this model deals in, in <see cref="Unit"/>: the largest of
    /// what any electrode holds, the ion's acceleration potential, and the largest value on
    /// the plane. A range far below it is the solve's rounding, not a feature (see
    /// <see cref="SurfaceCommand.NoiseFloor"/>). Zero where nothing gives a scale.
    /// </summary>
    public double ReferenceScale { get; init; }

    /// <summary>The lowest value sampled, plateaus at what they hold, in <see cref="Unit"/>.</summary>
    public double SampledLowest { get; init; }

    /// <summary>The highest value sampled, in <see cref="Unit"/>.</summary>
    public double SampledHighest { get; init; }

    /// <summary>
    /// The lowest value the field can take on this plane, from what the conductors hold (earth
    /// included), in <see cref="Unit"/>: the lowest of the per-node bounds; null where that is not
    /// known - a model with an analytic element, which fills all space and has no such bound. See
    /// <c>FieldProbe.AppliedRangeAt</c>.
    /// </summary>
    public double? AppliedLowest { get; init; }

    /// <summary>
    /// The lowest value the field can take at each node, in <see cref="Unit"/>, in the order of
    /// <see cref="Values"/>; null where the bound is not known.
    /// </summary>
    /// <remarks>
    /// Per node, because a bound for the whole plane is a bound for its loosest point: an
    /// overlapping reflected solve is twice its range only where both halves answer, and held to
    /// that everywhere a 560 V overshoot beside a 500 V face was drawn and not flagged.
    /// </remarks>
    public IReadOnlyList<double>? NodeLowest { get; init; }

    /// <summary>The highest value the field can take at each node, as <see cref="NodeLowest"/> is the lowest.</summary>
    public IReadOnlyList<double>? NodeHighest { get; init; }

    /// <summary>The highest value the field can take, as <see cref="AppliedLowest"/> is the lowest.</summary>
    public double? AppliedHighest { get; init; }

    /// <summary>Sample positions along <see cref="AcrossAxis"/>, in millimetres.</summary>
    public required IReadOnlyList<double> Across { get; init; }

    /// <summary>Sample positions along <see cref="DeepAxis"/>, in millimetres.</summary>
    public required IReadOnlyList<double> Deep { get; init; }

    /// <summary>
    /// The drawn quantity at each node - volts, or electronvolts - with every point inside a
    /// conductor set to that conductor's plateau, and NaN where the field refused.
    /// </summary>
    public required IReadOnlyList<double> Values { get; init; }

    /// <summary>
    /// The conductor each node is inside, by its plateau's label
    /// (<see cref="SurfacePlateau.Conductor"/>), or null for free space.
    /// </summary>
    public required IReadOnlyList<string?> Inside { get; init; }

    /// <summary>The plateaus, one per conductor the plane meets.</summary>
    public required IReadOnlyList<SurfacePlateau> Plateaus { get; init; }

    /// <summary>One of the <see cref="SurfaceHeight"/> values.</summary>
    public required string Height { get; init; }

    /// <summary>"V" or "eV".</summary>
    public required string Unit { get; init; }

    /// <summary>The ion's charge in units of e, used for the energy; absent for a potential.</summary>
    public double? ChargeNumber { get; init; }

    /// <summary>Points outside every element's domain, or in a conductor's declared shape past its solve.</summary>
    public required int OutsideDomain { get; init; }

    /// <summary>Points the field refused.</summary>
    public required int Refused { get; init; }

    /// <summary>The value at a node.</summary>
    /// <param name="i">Index along <see cref="Across"/>.</param>
    /// <param name="j">Index along <see cref="Deep"/>.</param>
    /// <returns>The drawn quantity there.</returns>
    public double At(int i, int j) => Values[(i * Deep.Count) + j];

    /// <summary>
    /// The value a node is drawn at: what was sampled, held to what the conductors hold where
    /// that is known (<see cref="AppliedLowest"/>, <see cref="AppliedHighest"/>).
    /// </summary>
    /// <param name="i">Index along <see cref="Across"/>.</param>
    /// <param name="j">Index along <see cref="Deep"/>.</param>
    /// <returns>The value the picture's height and color stand for there; NaN at a hole.</returns>
    /// <remarks>
    /// The scale and the bar are made from the conductors' range, so a surface drawn from the
    /// raw samples stood past its own bar wherever the interpolant overshot - the einzel lens
    /// dipped 32 V below an earth it cannot go below. The drawing agrees with its scale; the
    /// raw value is <see cref="At"/>, which is what the CSV carries.
    /// </remarks>
    public double Drawn(int i, int j)
    {
        var value = At(i, j);
        var k = (i * Deep.Count) + j;

        if (NodeLowest is { } lows && NodeHighest is { } highs)
        {
            return Math.Clamp(value, lows[k], highs[k]);
        }

        return AppliedLowest is { } low && AppliedHighest is { } high ? Math.Clamp(value, low, high) : value;
    }
}

/// <summary>What <c>einzel render surface</c> drew and where it put it.</summary>
public sealed record SurfaceOutcome
{
    /// <summary>The model, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>The model document's content hash (PRJ-3).</summary>
    public required string ModelHash { get; init; }

    /// <summary>The PNG written, or that would be written under <c>--dry-run</c>.</summary>
    public required string Artifact { get; init; }

    /// <summary>Whether it was actually written.</summary>
    public required bool Written { get; init; }

    /// <summary>Width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int HeightPx { get; init; }

    /// <summary>The named view it was drawn from.</summary>
    public required string View { get; init; }

    /// <summary>The plane, as two axis letters.</summary>
    public required string Plane { get; init; }

    /// <summary>Its offset along the normal, in millimetres.</summary>
    public required double OffsetMm { get; init; }

    /// <summary>The model axis across the picture.</summary>
    public required string AcrossAxis { get; init; }

    /// <summary>The model axis that is the picture's depth.</summary>
    public required string DeepAxis { get; init; }

    /// <summary>The sampled extent along the across axis, in millimetres.</summary>
    public required IReadOnlyList<double> AcrossMm { get; init; }

    /// <summary>The sampled extent along the deep axis, in millimetres.</summary>
    public required IReadOnlyList<double> DeepMm { get; init; }

    /// <summary>Samples along each axis, across then deep.</summary>
    public required IReadOnlyList<int> Samples { get; init; }

    /// <summary>
    /// The spacing between samples along each axis, across then deep, in millimetres. The two
    /// differ when the box is long and thin; the picture is drawn at the true positions either
    /// way.
    /// </summary>
    public required IReadOnlyList<double> StepsMm { get; init; }

    /// <summary>Mesh lines drawn along each axis, across then deep.</summary>
    public required IReadOnlyList<int> MeshLines { get; init; }

    /// <summary>What the height stands for: one of the <see cref="SurfaceHeight"/> values.</summary>
    public required string Height { get; init; }

    /// <summary>The height's unit, "V" or "eV".</summary>
    public required string Unit { get; init; }

    /// <summary>The ion's charge in units of e, for the energy; absent for a potential.</summary>
    public double? ChargeNumber { get; init; }

    /// <summary>
    /// The height scale: millimetres of drawn height per volt (or per electronvolt). Earth - zero -
    /// is drawn at the height of the plane itself.
    /// </summary>
    public required double HeightMmPerUnit { get; init; }

    /// <summary>The same scale the other way up: volts (or electronvolts) per drawn millimetre.</summary>
    public required double UnitsPerMm { get; init; }

    /// <summary>
    /// The lowest value of the surface, in <see cref="Unit"/>: the lowest sampled, held to the
    /// lowest the conductors hold where that is known (<see cref="AppliedLowest"/>). What the
    /// height scale and the scale bar are made from.
    /// </summary>
    /// <remarks>
    /// Not the raw extreme of the samples, which is <see cref="SampledLowest"/>: beside a step
    /// on the boundary a cubic interpolant overshoots by a few percent, and by an amount that
    /// changes with where the samples fall, so the raw extreme of a 0 to 500 V lens read -25 V
    /// at 61 samples and -32 V at 241. A field with no charge in it is nowhere outside what its
    /// boundary holds; where the samples are, <c>render.interpolant-overshoot</c> says by how much.
    /// </remarks>
    public required double Lowest { get; init; }

    /// <summary>The highest value of the surface, as <see cref="Lowest"/> is the lowest.</summary>
    public required double Highest { get; init; }

    /// <summary>The lowest value sampled, in <see cref="Unit"/>, overshoot included.</summary>
    public required double SampledLowest { get; init; }

    /// <summary>The highest value sampled, in <see cref="Unit"/>, overshoot included.</summary>
    public required double SampledHighest { get; init; }

    /// <summary>
    /// The lowest value the conductors hold, earth included, in <see cref="Unit"/>; absent where
    /// the model has an analytic element, which has no such bound.
    /// </summary>
    public required double? AppliedLowest { get; init; }

    /// <summary>The highest value the conductors hold, as <see cref="AppliedLowest"/> is the lowest.</summary>
    public required double? AppliedHighest { get; init; }

    /// <summary>Whether the field varies with time.</summary>
    public required bool TimeVarying { get; init; }

    /// <summary>The instant drawn, in microseconds; absent for a static field.</summary>
    public double? InstantUs { get; init; }

    /// <summary>Whether the height is the mean over one drive cycle rather than an instant.</summary>
    public required bool CycleMean { get; init; }

    /// <summary>The cycle-mean window, in microseconds; absent unless <see cref="CycleMean"/>.</summary>
    public double? CycleMeanWindowUs { get; init; }

    /// <summary>The conductors the plane meets, each a flat plateau.</summary>
    public required IReadOnlyList<SurfacePlateau> Plateaus { get; init; }

    /// <summary>Whether a validity violation marked the picture with a hatched band.</summary>
    public required bool Tainted { get; init; }

    /// <summary>
    /// The CSV of the sampled values that was written beside the picture, or that would be
    /// under <c>--dry-run</c>, as an absolute path; absent when none was asked for.
    /// </summary>
    public string? Csv { get; init; }

    /// <summary>
    /// Whether the vertical scale was drawn on the picture. False when it was not asked for, when
    /// the view looks straight down the height (the top view, where the bar is a point), and
    /// when a small picture has no room for the bar and its labels beside the surface; the scale is in
    /// <see cref="HeightMmPerUnit"/> and the PNG's text either way.
    /// </summary>
    public required bool ScaleDrawn { get; init; }

    /// <summary>Everything that qualifies the picture, which the PNG also carries (GRD-2).</summary>
    public required IReadOnlyList<WarningJson> Warnings { get; init; }
}

/// <summary>
/// The potential on a plane, drawn as height above it: SIMION's potential-energy view.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sampled by the probe, so it is the field a run flies through.</b> Every height comes from
/// <see cref="FieldProbe"/>, which assembles the field as every run does - superposition,
/// regions, mirrors and the axisymmetric mapping honoured, and the assembly's warnings carried.
/// A second sampler would be a second account of the field.
/// </para>
/// <para>
/// <b>Composed here, rasterized in <c>Einzel.Render</c></b>, the split <c>render still</c>
/// makes: the mesh, its colors, the camera and the scale are decided in this assembly, so the
/// window can draw the same picture later, and the rasterizer is handed triangles and lines.
/// </para>
/// <para>
/// <b>A height with no scale is a shape, not a measurement.</b> The scale is in the outcome,
/// in the PNG's text, and on the picture as a vertical bar labelled in volts.
/// </para>
/// </remarks>
public static class SurfaceCommand
{
    /// <summary>Samples along the longer side unless asked otherwise.</summary>
    public const int DefaultSamples = 241;

    /// <summary>The most samples along the longer side.</summary>
    public const int MaximumSamples = 1001;

    /// <summary>
    /// The fewest samples across the shorter side unless fewer were asked for along the longer:
    /// with square cells a 947 by 30 mm analyser got nine rows, too few for a board or a gap.
    /// </summary>
    public const int MinimumDeepSamples = 61;

    /// <summary>
    /// How far, as a fraction of the potentials the model deals in, the summed field may read
    /// inside a conductor from what the conductor holds before the surface says so.
    /// </summary>
    public const double PlateauTolerance = 0.02;

    /// <summary>Mesh lines along the longer side unless asked otherwise.</summary>
    public const int DefaultMeshLines = 40;

    /// <summary>
    /// The fewest mesh lines a surface may be drawn with: the one statement of the rule, which
    /// the request check applies and the CLI's reader takes its range from.
    /// </summary>
    public const int MinimumMeshLines = 1;

    /// <summary>The drawn height of the whole range, as a fraction of the longer side.</summary>
    public const double DefaultRelief = 0.35;

    /// <summary>The largest relief: a surface ten times taller than it is wide is already a spike.</summary>
    public const double MaximumRelief = 10.0;

    /// <summary>
    /// The tallest a surface may be drawn, as a multiple of its longer side, whatever scale asks
    /// for it. Past this the picture is one vertical line, and far enough past it the heights
    /// are not numbers at all.
    /// </summary>
    public const double MaximumDrawnHeight = 1000.0;

    /// <summary>The planes a surface may be asked for by name.</summary>
    public static readonly IReadOnlyList<string> Planes = ["xy", "xz", "yz"];

    /// <summary>The camera for a surface: higher than the viewport's iso, so the valleys show.</summary>
    public static readonly IReadOnlyDictionary<string, (double Azimuth, double Elevation)> Views =
        new Dictionary<string, (double, double)>(StringComparer.Ordinal)
        {
            ["iso"] = (-35.0, -32.0),
            ["side"] = (0.0, 0.0),
            ["front"] = (90.0, 0.0),
            ["top"] = (0.0, -90.0),
        };

    /// <summary>
    /// The color a conductor's plateau is drawn in, given the ramp color of its potential: dark,
    /// so it reads as metal against the pale surface, and leaning toward its potential's hue so a
    /// positive plateau and a negative one - a hill and a pit seen from above - can be told apart.
    /// </summary>
    /// <param name="ramp">The ramp color at the plateau's value.</param>
    /// <returns>The plateau's color.</returns>
    public static (double R, double G, double B) Plateau((double R, double G, double B) ramp) =>
        ((0.5 * ramp.R * 0.55) + (0.5 * 0.22), (0.5 * ramp.G * 0.55) + (0.5 * 0.23), (0.5 * ramp.B * 0.55) + (0.5 * 0.27));

    /// <summary>The direction a surface is lit from, in the picture's frame (x across, y up, z deep).</summary>
    public static readonly (double X, double Y, double Z) Light = (-0.35, 1.0, 0.45);

    /// <summary>How brightly the surface is drawn, given its normal.</summary>
    /// <param name="nx">Normal, x.</param>
    /// <param name="ny">Normal, y.</param>
    /// <param name="nz">Normal, z.</param>
    /// <returns>A factor from 0.55 to one.</returns>
    /// <remarks>
    /// Softer than the viewport's, on purpose: a height field is read by its mesh lines and its
    /// color, and the viewport's quarter of ambient light paints dark bands across every slope
    /// that happens to face sideways to the light - shading that reads as a feature of the
    /// potential when it is a feature of the lamp.
    /// </remarks>
    public static double Brightness(double nx, double ny, double nz)
    {
        var length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        if (!(length > 0.0))
        {
            return 0.8;
        }

        var (lx, ly, lz) = Light;
        var light = Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
        var lambert = Math.Max(0.0, ((nx * lx) + (ny * ly) + (nz * lz)) / (length * light));

        return 0.55 + (0.45 * lambert);
    }

    /// <summary>How far toward white the surface under the mesh is tinted, so the lines read over it.</summary>
    public const double SurfaceTint = 0.62;

    // How many times the frame is refitted round the scale's labels before giving up on it
    // settling; a picture too small for its labels never settles, and the fit check leaves the
    // scale out.
    private const int LabelFitPasses = 12;

    /// <summary>
    /// The smallest range, as a fraction of <see cref="PotentialSurface.ReferenceScale"/>, that
    /// is drawn at the full relief. Below it the automatic scale is the one this fraction of
    /// the reference would get, so a plane through a field-free drift - whose "range" is the
    /// last bits of a solve, 8e-15 V beside electrodes at kilovolts - is drawn flat instead of
    /// having its rounding stretched into hills at 1e15 mm per volt.
    /// </summary>
    public const double NoiseFloor = 1e-9;

    /// <summary>
    /// How far the wire mesh is brought toward the viewer before it is tested against the
    /// surface it lies on, as a distance along the view direction worth this many pixels on
    /// screen.
    /// </summary>
    public const double MeshDepthBiasPx = 2.0;

    /// <summary>The depth bias of the wire mesh for a picture, in the units the depth test compares.</summary>
    /// <param name="matrix">The picture's projection, column-major.</param>
    /// <param name="widthPx">The picture's width in pixels.</param>
    /// <returns>The bias, in clip-space depth.</returns>
    /// <remarks>
    /// <see cref="MeshDepthBiasPx"/> pixels' worth of millimetres, converted to depth. A line
    /// lies exactly on the surface's own triangle edges, so what the bias has to cover is the
    /// line's own width on a sloping surface: a couple of pixels. A fixed number in clip space
    /// is instead a fixed fraction of the scene's depth, which is two millimetres on a 600 mm
    /// box and hundreds of pixels looking along a long one - enough for the mesh on the far
    /// side of a thin ridge to be drawn through it.
    /// </remarks>
    public static double MeshDepthBias(IReadOnlyList<double> matrix, int widthPx)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var pxPerMm = Math.Sqrt((matrix[0] * matrix[0]) + (matrix[4] * matrix[4]) + (matrix[8] * matrix[8])) * widthPx / 2.0;
        var depthPerMm = Math.Sqrt((matrix[2] * matrix[2]) + (matrix[6] * matrix[6]) + (matrix[10] * matrix[10]));

        return pxPerMm > 0.0 ? MeshDepthBiasPx / pxPerMm * depthPerMm : 0.0;
    }

    /// <summary>
    /// How much of its unlabelled size the surface must keep once room is made for the scale's
    /// labels. Below this the labels are taking most of a small picture and the surface is a
    /// smudge beside them, so the scale is left off and the surface drawn at full size.
    /// </summary>
    public const double SmallestLabelledFraction = 0.5;

    /// <summary>Samples a model's potential on a plane.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="request">Where and how.</param>
    /// <param name="maxParallelism">Rows sampled at once; null for one per processor, 1 to sample serially.</param>
    /// <returns>The sampled surface, the probe it came from, and the warnings sampling earned.</returns>
    /// <exception cref="EinzelException">The model does not validate, or the request is malformed.</exception>
    public static (PotentialSurface Surface, FieldProbe Probe, IReadOnlyList<ValidityWarning> Warnings) Sample(
        string modelPath, SurfaceRequest request, int? maxParallelism = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);

        return Sample(absolute, File.ReadAllText(absolute), request, maxParallelism);
    }

    /// <summary>Samples the potential of model text already read.</summary>
    /// <param name="modelPath">Where the text came from; imported files resolve against its directory.</param>
    /// <param name="modelText">The model document.</param>
    /// <param name="request">Where and how.</param>
    /// <param name="maxParallelism">Rows sampled at once; null for one per processor, 1 to sample serially.</param>
    /// <returns>
    /// The sampled surface, the probe it came from - whose <see cref="FieldProbe.ModelHash"/> is
    /// the hash of this text - and the warnings sampling earned.
    /// </returns>
    /// <exception cref="EinzelException">The model does not validate, or the request is malformed.</exception>
    /// <remarks>
    /// One read of the model (PRJ-3): the field and the hash recorded beside it are of the same
    /// revision however the file is edited meanwhile.
    /// </remarks>
    public static (PotentialSurface Surface, FieldProbe Probe, IReadOnlyList<ValidityWarning> Warnings) Sample(
        string modelPath, string modelText, SurfaceRequest request, int? maxParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Check(request);

        var probe = FieldProbe.Open(modelPath, modelText);
        var model = probe.Model;

        // Under --cycle-mean no phase is drawn: the instant picks the operating point and the mean
        // is over a whole period wherever it starts, so how finely a double can place the instant
        // in the cycle does not matter (the probe opens the window at zero where it cannot).
        if (!request.CycleMean)
        {
            Einzel.Fields.FieldInstants.CheckResolution(request.AtUs * 1e-6, probe.Field, FlagPath.Of("at-us"));
        }

        // A mean over a cycle that does not exist is not a number this can draw, and drawing the
        // instant under that name would be a picture captioned with something it is not.
        // Asked of the field held at the chosen instant, which is the field the mean is taken on:
        // a sequence may be driven in one phase and static in the next.
        var at = (request.AtUs ?? 0.0) * 1e-6;

        if (request.CycleMean && probe.CycleMeanWindowSecondsAt(at) is null)
        {
            throw Refusal(
                FlagPath.Of("cycle-mean"),
                probe.TimeVarying
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"--cycle-mean averages the potential over one drive period, and at t = {at * 1e6:G6} us this field has no drive: it is a sequence whose state there is static, so there is no cycle to average over")
                    : "--cycle-mean averages the potential over one drive period, and this field is "
                      + "static: it has no drive and no cycle to average over",
                probe.TimeVarying
                    ? "drop --cycle-mean to draw that state as it is, with --at-us <t> to choose it; or give --at-us <t> inside a phase that is driven"
                    : "drop --cycle-mean; a static field is already the part that does not oscillate");
        }

        var domain = probe.DomainMm();
        var twoD = model.Fields.Any(f => f.Solve is not null) && !model.Fields.Any(f => f.Solve3D is not null);

        var plane = request.Plane ?? DefaultPlane(domain, twoD);
        var (a, b, n) = Axes(plane);

        // The middle of the domain along the normal - except on a 2-D model's own plane, where
        // the normal is the axis the solve is invariant along (or rotated about) and z = 0 is the
        // plane it is declared in.
        var centre = domain is { } d ? Component(Mid(d.Min, d.Max), n) : 0.0;
        var offset = request.OffsetMm ?? (twoD && plane == "xy" ? 0.0 : centre);

        double a0, b0, a1, b1;

        if (request.BoxMm is { } box)
        {
            (a0, b0, a1, b1) = (box[0], box[1], box[2], box[3]);
        }
        else if (domain is { } extent)
        {
            (a0, a1) = (Component(extent.Min, a), Component(extent.Max, a));
            (b0, b1) = (Component(extent.Min, b), Component(extent.Max, b));
        }
        else
        {
            throw Refusal(
                FlagPath.Of("box-mm"),
                "this model has no solved domain to take the sampled rectangle from (every element "
                + "is analytic and fills all space); give --box-mm with the rectangle in the plane");
        }

        if (!(a1 > a0) || !(b1 > b0))
        {
            throw Refusal(FlagPath.Of("box-mm"), "the rectangle must have a positive extent along both axes");
        }

        // The longer side runs across the picture, which is how an instrument is drawn.
        var swap = (b1 - b0) > (a1 - a0);
        var (across, deep) = swap ? (b, a) : (a, b);

        // (across, deep, normal) is an even or an odd permutation of (x, y, z). The picture's
        // frame is (across, height, depth) with the normal as the height, which is right-handed
        // when depth runs against the deep axis for an even permutation and along it for an odd
        // one. Without this the picture was the model's mirror image in every view.
        var even = (across, deep, n) is (0, 1, 2) or (1, 2, 0) or (2, 0, 1);
        var deepSign = even ? -1.0 : 1.0;
        var (u0, u1, v0, v1) = swap ? (b0, b1, a0, a1) : (a0, a1, b0, b1);

        // Square cells by default, but never so few across the shorter side that a thin box is a
        // handful of facets: the cells may then be longer than they are wide, and the picture
        // is drawn at the true positions either way.
        var nu = request.Samples;
        var step = (u1 - u0) / (nu - 1);
        var nv = request.DeepSamples
            ?? Math.Clamp((int)Math.Round((v1 - v0) / step) + 1, Math.Min(MinimumDeepSamples, nu), MaximumSamples);

        var us = Grid(u0, u1, nu);
        var vs = Grid(v0, v1, nv);

        var energy = request.Height == SurfaceHeight.Energy;
        var charge = model.ChargeSi / Einzel.Transport.IonSpecies.ElementaryChargeSi;
        var factor = energy ? charge : 1.0;

        var cycleMean = request.CycleMean;

        var raw = new double[nu * nv];
        var names = new string?[nu * nv];
        var elements = new int?[nu * nv];
        var depth = new double[nu * nv];
        var held = new double[nu * nv];
        var boundLow = new double[nu * nv];
        var boundHigh = new double[nu * nv];
        var outsideRows = new int[nu];
        var refusedRows = new int[nu];

        // Row by row across the processors: every node writes only its own slots, the probe is
        // safe to share, and the counts are kept per row and summed after, so the result is the
        // same whatever the order the rows finish in - asserted byte for byte against a serial
        // pass.
        ForEachRow(nu, maxParallelism, i =>
        {
            for (var j = 0; j < nv; j++)
            {
                var k = (i * nv) + j;
                var point = FieldProbe.MillimetresToMetres(PlanePoint(across, deep, n, us[i], vs[j], offset));
                var status = probe.SamplePotential(
                    in point, at, cycleMean, out raw[k], out var distance, out var conductor, out var element, out var volts);

                // What the conductors hold bounds the field at this node: summed over the elements
                // answering here, each reflected half counted where it answers.
                (boundLow[k], boundHigh[k]) = probe.AppliedRangeAt(in point, at, cycleMean) is { } local
                    ? local
                    : (double.NaN, double.NaN);

                if (status == ProbeStatus.Refused)
                {
                    refusedRows[i]++;
                }
                else if (status == ProbeStatus.OutsideDomain)
                {
                    outsideRows[i]++;
                }
                else if (status == ProbeStatus.InsideConductor)
                {
                    // Only a conductor with one potential is a plateau: an edge-profiled board's
                    // varies along it, so it is drawn as the field sampled there.
                    if (volts is { } holds)
                    {
                        names[k] = conductor ?? "conductor";
                        elements[k] = element;
                        depth[k] = distance ?? 0.0;
                        held[k] = holds;
                    }
                }
            }
        });

        var outside = outsideRows.Sum();
        var refused = refusedRows.Sum();

        // A plateau at the potential the conductor holds, from the solve's own coefficients. Not
        // a reading of the field there: an interpolant overshoots inside a thin conductor, and
        // inside metal the sum carries every other element's potential too - an analytic element
        // has no conductors and fills all space. The readings are kept beside it, as the range
        // and the deeper half's median, so the difference can be seen.
        //
        // Keyed by element and name together: a name is unique only within one solve, so two
        // elements may each have a "plate", and one median over both would draw two conductors
        // at a potential neither holds. Where a name does repeat, the label says which element.
        // One pass over the nodes, bucketing each sample in metal by its conductor's identity -
        // the element and the name together. A label is derived from the identity afterwards
        // and only for showing: grouping by the label merged a conductor named "plate" in one
        // element with one literally named "plate (element 1)" in another.
        var keys = new List<(int? Element, string Name)>();
        var keyIndex = new Dictionary<(int? Element, string Name), int>();
        var keyOf = new int[raw.Length];
        var members = new List<List<int>>();

        for (var k = 0; k < raw.Length; k++)
        {
            keyOf[k] = -1;

            if (names[k] is not { } name)
            {
                continue;
            }

            var identity = (elements[k], name);

            if (!keyIndex.TryGetValue(identity, out var index))
            {
                index = keys.Count;
                keyIndex[identity] = index;
                keys.Add(identity);
                members.Add([]);
            }

            keyOf[k] = index;

            if (double.IsFinite(raw[k]))
            {
                members[index].Add(k);
            }
        }

        var labels = Labels(keys);
        var level = new double?[keys.Count];
        var plateaus = new List<SurfacePlateau>();
        var onFaceOnly = new HashSet<string>(StringComparer.Ordinal);

        foreach (var index in Enumerable.Range(0, keys.Count).Where(i => members[i].Count > 0).OrderBy(i => labels[i], StringComparer.Ordinal))
        {
            var group = members[index];

            // The deeper half, by signed distance, and its median value: deepest alone fails on a
            // conductor written as two halves meeting on a line, where a plane beside the seam is
            // never more than its offset deep and the "deepest" sample is any of them, including
            // one beside a face where the interpolant overshoots.
            //
            // And of the samples properly inside, where there are any: one on the conductor's
            // face at the edge of its solve is drawn at what the conductor holds, but what the
            // interpolant reads there is a reading of the step and says nothing about what is
            // summed inside the metal.
            var read = new List<int>(group.Count);
            var readLow = double.PositiveInfinity;
            var readHigh = double.NegativeInfinity;

            foreach (var k in group)
            {
                if (depth[k] < 0.0)
                {
                    read.Add(k);
                }
            }

            if (read.Count == 0)
            {
                // Met only on its face: nothing was read inside it to disagree with what it holds.
                onFaceOnly.Add(labels[index]);
                read.AddRange(group);
            }

            foreach (var k in read)
            {
                readLow = Math.Min(readLow, raw[k]);
                readHigh = Math.Max(readHigh, raw[k]);
            }

            // By depth, equal depths in node order - the order the stable LINQ sort it replaces
            // gave, so the median is the same sample.
            var ordered = read.ToArray();
            Array.Sort(ordered, (x, y) => depth[x].CompareTo(depth[y]) is var c && c != 0 ? c : x.CompareTo(y));

            var deeper = new double[Math.Max(1, ordered.Length / 2)];

            for (var m = 0; m < deeper.Length; m++)
            {
                deeper[m] = raw[ordered[m]];
            }

            Array.Sort(deeper);

            var potential = held[ordered[0]];
            level[index] = potential;

            plateaus.Add(new SurfacePlateau(
                labels[index],
                potential,
                factor * potential,
                group.Count,
                readLow,
                readHigh,
                keys[index].Element,
                keys[index].Name)
            {
                SampledV = deeper[deeper.Length / 2],
            });
        }

        var values = new double[raw.Length];

        // Each node in metal names its conductor's label - one string per conductor, shared.
        var inside = new string?[raw.Length];

        for (var k = 0; k < raw.Length; k++)
        {
            inside[k] = keyOf[k] >= 0 ? labels[keyOf[k]] : null;

            var v = keyOf[k] >= 0 && level[keyOf[k]] is { } flat ? flat : raw[k];
            values[k] = (factor * v) + 0.0;
        }

        var warnings = new List<ValidityWarning>(probe.Warnings);

        // What "small" is measured against: the potentials the model's own electrodes hold, the
        // ion's acceleration potential, and the largest value on the plane, in the drawn unit.
        var reference = Math.Abs(model.AccelerationPotentialSi);

        reference = Math.Max(reference, PeakElectrodePotential(model));

        foreach (var v in raw)
        {
            if (double.IsFinite(v))
            {
                reference = Math.Max(reference, Math.Abs(v));
            }
        }

        reference *= Math.Abs(factor);

        // The plateaus are in volts and the reference in the drawn unit.
        var referenceVolts = factor != 0.0 ? reference / Math.Abs(factor) : reference;

        foreach (var plateau in plateaus)
        {
            var stray = Math.Max(Math.Abs(plateau.LowestV - plateau.PotentialV), Math.Abs(plateau.HighestV - plateau.PotentialV));

            if (Math.Abs(plateau.SampledV - plateau.PotentialV) > PlateauTolerance * referenceVolts && stray > 0.0
                && !onFaceOnly.Contains(plateau.Conductor))
            {
                warnings.Add(new ValidityWarning(
                    "render.plateau-disagrees",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"inside '{plateau.Conductor}' the summed field reads {plateau.LowestV:G6} to {plateau.HighestV:G6} V (median {plateau.SampledV:G6} V) where the conductor holds {plateau.PotentialV:G6} V: another element's potential is summed inside the metal - an analytic element has no conductors and fills all space, and a solve whose domain overlaps this conductor adds its own potential there - so the plateau is drawn at the conductor's own potential and the surface beside it is the sum, which steps at the conductor's edge"),
                    WarningSeverity.Qualified));
            }
        }

        // What the conductors hold bounds what the field can be, where every element is a solve:
        // no charge, so nowhere above the highest potential on the boundary or below the lowest.
        var (sampledLow, sampledHigh) = Range(values);
        (double Low, double High)? applied = null;
        double[]? nodeLow = null;
        double[]? nodeHigh = null;

        if (probe.AppliedRange(at, cycleMean) is not null)
        {
            // Per node, in the drawn unit; the plane's bound is the loosest of them.
            nodeLow = new double[raw.Length];
            nodeHigh = new double[raw.Length];

            for (var k = 0; k < raw.Length; k++)
            {
                (nodeLow[k], nodeHigh[k]) = factor >= 0.0
                    ? (factor * boundLow[k], factor * boundHigh[k])
                    : (factor * boundHigh[k], factor * boundLow[k]);
            }

            applied = (nodeLow.Min(), nodeHigh.Max());
        }

        var (lowest, highest) = Stated(sampledLow, sampledHigh, applied);

        if (applied is { } bound)
        {
            // Past the bound by more than rounding: not the field, the interpolant.
            var slack = 1e-9 * Math.Max(reference, Math.Max(Math.Abs(bound.Low), Math.Abs(bound.High)));
            var worst = -1;
            var over = 0.0;

            for (var k = 0; k < values.Length; k++)
            {
                if (!double.IsFinite(values[k]))
                {
                    continue;
                }

                var excess = Math.Max(values[k] - nodeHigh![k], nodeLow![k] - values[k]);

                if (excess > over)
                {
                    over = excess;
                    worst = k;
                }
            }

            if (worst >= 0 && over > slack)
            {
                var unit = energy ? "eV" : "V";

                warnings.Add(new ValidityWarning(
                    "render.interpolant-overshoot",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the samples reach {values[worst]:G6} {unit} at {Name(across)} = {us[worst / nv]:G6} mm, {Name(deep)} = {vs[worst % nv]:G6} mm, {over:G4} {unit} outside the {nodeLow![worst]:G6} to {nodeHigh![worst]:G6} {unit} the conductors hold there: a field with no charge in it is nowhere outside what its boundary holds, so that is the interpolant overshooting beside a step in what the boundary holds - at a conductor's face, and most where its edge meets the earthed edge of its solve - and not the field. The range stated, the height scale and the scale bar are the conductors' range, and the drawing is held to it: the surface is drawn no further than its bar. The raw readings are in the CSV and in sampledLowest and sampledHighest"),
                    WarningSeverity.Qualified));
            }
        }

        var span = Math.Max(highest, 0.0) - Math.Min(lowest, 0.0);

        if (request.HeightMmPerUnit is null && span > 0.0 && span < NoiseFloor * reference)
        {
            var unit = energy ? "eV" : "V";

            warnings.Add(new ValidityWarning(
                "render.surface-flat",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the values on this plane span {span:G3} {unit}, less than {NoiseFloor:G1} of the {reference:G4} {unit} this model deals in: that is the solve's rounding and not a feature of the field, so the surface is drawn flat - at the height scale a range of {NoiseFloor * reference:G3} {unit} would get - and not stretched to fill the relief; give --height-mm-per-{(energy ? "ev" : "v")} to draw it at a scale of your own"),
                WarningSeverity.Provenance));
        }

        if (probe.TimeVarying && request.AtUs is null && !(cycleMean && !probe.HasOperatingPoints))
        {
            warnings.Add(new ValidityWarning(
                "render.field-at-instant",
                cycleMean
                    ? "this field is sequenced or ramped, so even its cycle mean depends on the "
                      + "instant, and no --at-us was given: the surface is the cycle mean at "
                      + "t = 0 us; give --at-us to choose the operating point"
                    : "this field varies with time (it is driven or sequenced) and no --at-us was "
                      + "given, so the surface is the potential at t = 0 us - for a sinusoidal drive "
                      + "the top of its cycle - and not a field the instrument holds; give --at-us "
                      + "to choose the instant, or --cycle-mean for the part that does not oscillate",
                WarningSeverity.Provenance));
        }

        if (!probe.TimeVarying && request.AtUs is { } unused)
        {
            warnings.Add(new ValidityWarning(
                "render.instant-ignored",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"--at-us {unused:G6} was given and this field is static - nothing in the model is driven or sequenced - so the surface is the same at every instant and the flag changed nothing"),
                WarningSeverity.Advisory));
        }

        if (cycleMean)
        {
            var window = probe.CycleMeanWindowSecondsAt(at)!.Value * 1e6;

            warnings.Add(probe.MonochromaticAt(at)
                ? new ValidityWarning(
                    "render.cycle-mean",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the height is the potential averaged over one drive period ({window:G6} us), not an instant: for a sinusoidal drive that is exactly the part that does not oscillate (the DC), and for a rectangular one it includes the wave's own mean, 2*duty - 1 of its amplitude"),
                    WarningSeverity.Provenance)
                : new ValidityWarning(
                    "render.cycle-mean",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the height is the potential averaged over one period of the fastest drive ({window:G6} us); this field carries more than one frequency, so a slower drive is averaged over part of its own cycle and its contribution is not its cycle mean unless its amplitude is zero"),
                    WarningSeverity.Qualified));

            // Said, not substituted silently: an instant too late to place in the cycle picks the
            // operating point, and the window it is averaged over opens at zero.
            if (probe.CycleMeanWindowOpensAt(at) is { } opens && opens != at)
            {
                warnings.Add(new ValidityWarning(
                    "render.cycle-mean-window-moved",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"t = {at * 1e6:G6} us is too late for a double to place in the drive's cycle, so it picks only the operating point and the {window:G6} us window is averaged from t = {opens * 1e6:G6} us of the field held there: a whole period of a periodic drive has the same mean wherever it opens, and for several frequencies the window's opening is a choice this states rather than one the caption can"),
                    WarningSeverity.Provenance));
            }
        }

        if (energy && model.IsMixture)
        {
            warnings.Add(new ValidityWarning(
                "render.first-species",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"this model carries {model.Species.Count} populations; the potential energy is drawn for the first, charge {charge:G4} e"),
                WarningSeverity.Provenance));
        }

        if (outside > 0)
        {
            warnings.Add(new ValidityWarning(
                "probe.outside-domain",
                string.Create(CultureInfo.InvariantCulture, $"{outside} of {raw.Length} surface point(s) lie outside every element's domain ")
                + "or inside a conductor's declared shape past the edge of its solve: "
                + "the height there is what the field returns outside what was solved or declared "
                + "(not necessarily zero), not a solved value and not a measurement of vacuum",
                WarningSeverity.Qualified));
        }

        if (refused > 0)
        {
            warnings.Add(new ValidityWarning(
                "probe.refused",
                string.Create(CultureInfo.InvariantCulture, $"{refused} of {raw.Length} surface point(s) were refused by the field (a singular ")
                + "axis, say) and are left as holes in the surface",
                WarningSeverity.Qualified));
        }

        var surface = new PotentialSurface
        {
            Plane = plane,
            OffsetMm = offset,
            AcrossAxis = Name(across),
            DeepAxis = Name(deep),
            DeepSign = deepSign,
            ReferenceScale = reference,
            Across = us,
            Deep = vs,
            Values = values,
            Inside = inside,
            Plateaus = plateaus,
            Height = energy ? SurfaceHeight.Energy : SurfaceHeight.Potential,
            Unit = energy ? "eV" : "V",
            ChargeNumber = energy ? charge : null,
            OutsideDomain = outside,
            Refused = refused,
            SampledLowest = sampledLow,
            SampledHighest = sampledHigh,
            AppliedLowest = applied?.Low,
            AppliedHighest = applied?.High,
            NodeLowest = nodeLow,
            NodeHighest = nodeHigh,
        };

        return (surface, probe, warnings);
    }

    /// <summary>
    /// Runs a body for every row, across the processors, and lets a failure out as itself.
    /// </summary>
    /// <param name="rows">How many rows.</param>
    /// <param name="maxParallelism">The most rows at once; null for one per processor.</param>
    /// <param name="body">What to do for a row.</param>
    /// <remarks>
    /// A parallel loop gathers what its bodies throw into an <see cref="AggregateException"/>,
    /// which is not the exception anybody threw: a refusal raised while sampling - an
    /// <see cref="EinzelException"/>, exit 1 - reached the command line as an unknown failure,
    /// exit 6, "a defect in einzel". The first failure is thrown again as what it is, with its
    /// own stack.
    /// </remarks>
    public static void ForEachRow(int rows, int? maxParallelism, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            Parallel.For(0, rows, new ParallelOptions { MaxDegreeOfParallelism = maxParallelism ?? -1 }, body);
        }
        catch (AggregateException failures) when (failures.InnerExceptions.Count > 0)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures.Flatten().InnerExceptions[0]).Throw();
        }
    }

    /// <summary>The height scale a surface is drawn at.</summary>
    /// <param name="surface">The sampled surface.</param>
    /// <param name="request">The request, for the relief or an explicit scale.</param>
    /// <returns>Millimetres of drawn height per volt (or electronvolt).</returns>
    /// <remarks>
    /// The range is measured from earth as well as across the values, so a surface that never
    /// crosses zero is still drawn with zero at the plane - the height then reads as a potential
    /// rather than as a difference from an arbitrary floor.
    /// </remarks>
    public static double Scale(PotentialSurface surface, SurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(request);

        if (request.HeightMmPerUnit is { } explicitScale)
        {
            return explicitScale;
        }

        var (low, high) = Range(surface);
        var span = Math.Max(high, 0.0) - Math.Min(low, 0.0);
        var longer = Math.Max(
            surface.Across[^1] - surface.Across[0], surface.Deep[^1] - surface.Deep[0]);

        // Flat everywhere: nothing to scale, so one millimetre per unit and the bar says so.
        // And a range that is rounding noise beside the model's own potentials is not stretched
        // to the relief: it is drawn at the scale the noise floor would get, which is flat.
        return span > 0.0 ? request.Relief * longer / Math.Max(span, NoiseFloor * surface.ReferenceScale) : 1.0;
    }

    /// <summary>Composes the surface as layers, in model millimetres with height up.</summary>
    /// <param name="surface">The sampled surface.</param>
    /// <param name="mmPerUnit">The height scale.</param>
    /// <param name="meshLines">Mesh lines along the longer side.</param>
    /// <returns>The surface, the plateaus, and the wire mesh with the frame, in drawing order.</returns>
    /// <remarks>
    /// <para>
    /// The picture's own frame: x runs along the across axis, y is the drawn height (volts times
    /// the scale), z runs along or against the deep axis. Earth is at y = 0, the plane itself.
    /// </para>
    /// <para>
    /// <b>The surface writes depth and the mesh is tested against it with a bias</b>, so lines on
    /// the far side of a hill are hidden and lines on the near side are not lost to rounding.
    /// The bias depends on the projection (<see cref="MeshDepthBias"/>), so the layer here
    /// carries none and whoever frames the picture sets it.
    /// </para>
    /// <para>
    /// The picture's z is <see cref="PotentialSurface.DeepSign"/> times the deep coordinate, so
    /// the picture is a rotation of the model and not its mirror image.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PictureLayer> Compose(PotentialSurface surface, double mmPerUnit, int meshLines) =>
        Compose(surface, mmPerUnit, meshLines, out _);

    /// <summary>Composes the surface, and says how many mesh lines it drew along each axis.</summary>
    /// <param name="surface">The sampled surface.</param>
    /// <param name="mmPerUnit">The height scale.</param>
    /// <param name="meshLines">Mesh lines along the longer side.</param>
    /// <param name="drawn">
    /// The mesh-line positions the wire mesh was drawn at along each axis, across then deep -
    /// the numbers an outcome reports, taken from the drawing rather than worked out again.
    /// </param>
    /// <returns>The layers, as <see cref="Compose(PotentialSurface, double, int)"/>.</returns>
    public static IReadOnlyList<PictureLayer> Compose(
        PotentialSurface surface, double mmPerUnit, int meshLines, out IReadOnlyList<int> drawn)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var nu = surface.Across.Count;
        var nv = surface.Deep.Count;
        var zs = PictureDeep(surface);
        var (low, high) = Range(surface);
        var span = Math.Max(Math.Abs(low), Math.Abs(high));

        (double R, double G, double B) Ramp(double value) =>
            ColorRamp.Diverging(span > 0.0 ? 0.5 + (0.5 * Math.Clamp(value / span, -1.0, 1.0)) : 0.5);

        double Y(int i, int j) => surface.Drawn(i, j) * mmPerUnit;

        // The surface mesh: one vertex per node, colored by its value and tinted toward white.
        var vertices = new double[3 * nu * nv];
        var normals = new double[3 * nu * nv];
        var colors = new double[3 * nu * nv];

        for (var i = 0; i < nu; i++)
        {
            for (var j = 0; j < nv; j++)
            {
                var k = (i * nv) + j;
                var y = Y(i, j);

                vertices[3 * k] = surface.Across[i];
                vertices[(3 * k) + 1] = double.IsFinite(y) ? y : 0.0;
                vertices[(3 * k) + 2] = zs[j];

                // Central differences, one-sided at the edges; a NaN neighbour contributes nothing.
                var il = Math.Max(0, i - 1);
                var ih = Math.Min(nu - 1, i + 1);
                var jl = Math.Max(0, j - 1);
                var jh = Math.Min(nv - 1, j + 1);

                var dydu = Slope(Y(il, j), Y(i, j), Y(ih, j), surface.Across[il], surface.Across[i], surface.Across[ih]);
                var dydv = Slope(Y(i, jl), Y(i, j), Y(i, jh), surface.Deep[jl], surface.Deep[j], surface.Deep[jh]);

                normals[3 * k] = -dydu;
                normals[(3 * k) + 1] = 1.0;
                normals[(3 * k) + 2] = -dydv * surface.DeepSign;

                var (r, g, b) = Ramp(surface.Drawn(i, j));
                colors[3 * k] = r + ((1.0 - r) * SurfaceTint);
                colors[(3 * k) + 1] = g + ((1.0 - g) * SurfaceTint);
                colors[(3 * k) + 2] = b + ((1.0 - b) * SurfaceTint);
            }
        }

        var surfaceTriangles = new List<int>();
        var plateauTriangles = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var i = 0; i + 1 < nu; i++)
        {
            for (var j = 0; j + 1 < nv; j++)
            {
                int k00 = (i * nv) + j, k10 = ((i + 1) * nv) + j, k01 = k00 + 1, k11 = k10 + 1;

                if (!double.IsFinite(surface.Values[k00]) || !double.IsFinite(surface.Values[k10])
                    || !double.IsFinite(surface.Values[k01]) || !double.IsFinite(surface.Values[k11]))
                {
                    continue;
                }

                var name = surface.Inside[k00];
                var plateau = name is not null
                    && name == surface.Inside[k10] && name == surface.Inside[k01] && name == surface.Inside[k11];

                if (plateau)
                {
                    if (!plateauTriangles.TryGetValue(name!, out var list))
                    {
                        plateauTriangles[name!] = list = [];
                    }

                    list.AddRange([k00, k10, k11, k00, k11, k01]);
                }
                else
                {
                    surfaceTriangles.AddRange([k00, k10, k11, k00, k11, k01]);
                }
            }
        }

        // A plateau is flat, so it is lit as flat whatever the neighbouring slope.
        var meshes = new List<PictureMesh> { new(vertices, normals, surfaceTriangles, 1.0, 1.0, 1.0, colors) };
        var plateaus = new List<PictureMesh>();

        foreach (var (name, triangles) in plateauTriangles.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var flat = surface.Plateaus.Single(p => p.Conductor == name).Height;
            var (pr, pg, pb) = Plateau(Ramp(flat));

            // Over its own vertices, not the whole surface's: a mesh handed every node of the
            // grid is walked whole by the framing and the rasterizer, once per conductor - a
            // thirty-ring stack at 1001 samples a side is thirty million vertices.
            var index = new Dictionary<int, int>();
            var own = new List<double>();
            var remapped = new int[triangles.Count];

            for (var t = 0; t < triangles.Count; t++)
            {
                var node = triangles[t];

                if (!index.TryGetValue(node, out var compact))
                {
                    compact = own.Count / 3;
                    index[node] = compact;
                    own.AddRange([vertices[3 * node], vertices[(3 * node) + 1], vertices[(3 * node) + 2]]);
                }

                remapped[t] = compact;
            }

            var up = new double[own.Count];

            for (var k = 1; k < up.Length; k += 3)
            {
                up[k] = 1.0;
            }

            plateaus.Add(new PictureMesh(own, up, remapped, pr, pg, pb));
        }

        meshes.AddRange(Skirts(surface, mmPerUnit, Ramp));

        var lines = WireMesh(surface, mmPerUnit, meshLines, Ramp, out drawn);

        // The plane itself at earth, so a reader can see where zero is drawn.
        var (fr, fg, fb) = (0.55, 0.55, 0.58);
        var (a0, a1, d0, d1) = (surface.Across[0], surface.Across[^1], zs[0], zs[^1]);
        lines.Add(new PictureLine([a0, 0, d0, a1, 0, d0, a1, 0, d1, a0, 0, d1, a0, 0, d0], fr, fg, fb));

        return
        [
            new PictureLayer("surface", meshes, [], 1.0, WritesDepth: true),
            new PictureLayer("plateaus", plateaus, [], 1.0, WritesDepth: true),
            new PictureLayer("mesh", [], lines, 1.0, WritesDepth: false),
        ];
    }

    /// <summary>Draws a model's potential on a plane as a height field, to a PNG.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="project">Where figures go by default.</param>
    /// <param name="request">Where, when and how.</param>
    /// <param name="view">A name from <see cref="Views"/>.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="outputPath">Where to write, or null to name it after the model.</param>
    /// <param name="dryRun">Whether to draw without writing.</param>
    /// <param name="scale">Whether to draw the vertical scale on the picture.</param>
    /// <param name="csvPath">Where to write the sampled values as CSV, or null for none.</param>
    /// <param name="modelText">
    /// The model document, for a caller that has already read it; null to read
    /// <paramref name="modelPath"/>, once. Either way the hash recorded is of the text the field
    /// was built from (PRJ-3).
    /// </param>
    /// <param name="maxParallelism">Rows sampled at once; null for one per processor, 1 to sample serially.</param>
    /// <returns>What was drawn and where it went.</returns>
    /// <exception cref="EinzelException">The model does not validate, or the request is malformed.</exception>
    /// <exception cref="ArgumentException">The view has no such name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside 16 to 8192 pixels.</exception>
    public static SurfaceOutcome Execute(
        string modelPath,
        ProjectLayout project,
        SurfaceRequest request,
        string view = "iso",
        int width = 1600,
        int height = 1000,
        string? outputPath = null,
        bool dryRun = false,
        bool scale = true,
        string? csvPath = null,
        string? modelText = null,
        int? maxParallelism = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 8192);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 8192);

        if (!Views.TryGetValue(view, out var angles))
        {
            throw new ArgumentException(
                $"no view named '{view}'; the named views are " + string.Join(", ", Views.Keys),
                nameof(view));
        }

        var absolute = Path.GetFullPath(modelPath);
        var path = outputPath is { } named
            ? Path.GetFullPath(named)
            : Path.Combine(project.Figures, Path.GetFileNameWithoutExtension(absolute) + ".surface.png");
        var csv = csvPath is null ? null : Path.GetFullPath(csvPath);

        // Before anything is read, let alone solved: a destination that is a directory, that is
        // the model itself, or that is the other destination is known at once. Written in turn to
        // one file the table would replace the picture, and written to the model either would
        // replace what it is a picture of.
        var outFlag = outputPath is null ? null : "out";
        ArtifactFiles.CheckAll(csv is null ? [(outFlag, path)] : [(outFlag, path), ("csv", csv)], absolute);

        // One read (PRJ-3): the hash is the probe's, of the very text the field was built from.
        var (surface, probe, warnings) = Sample(absolute, modelText ?? File.ReadAllText(absolute), request, maxParallelism);
        var hash = probe.ModelHash;
        var mmPerUnit = Scale(surface, request);
        var (low, high) = Range(surface);

        // Before anything is composed: a scale that makes the heights infinite, or a surface a
        // thousand times taller than it is wide, is refused by the flag that asked for it. It
        // used to be drawn - from heights that were not numbers - and reported as written.
        var longest = Math.Max(surface.Across[^1] - surface.Across[0], surface.Deep[^1] - surface.Deep[0]);
        var tallest = mmPerUnit * Math.Max(
            Math.Max(Math.Abs(surface.SampledLowest), Math.Abs(surface.SampledHighest)), Math.Max(Math.Abs(low), Math.Abs(high)));

        if (!double.IsFinite(mmPerUnit) || !(mmPerUnit > 0.0) || !double.IsFinite(tallest) || tallest > MaximumDrawnHeight * longest)
        {
            var flag = request.HeightMmPerUnit is null
                ? "relief"
                : surface.Height == SurfaceHeight.Energy ? "height-mm-per-ev" : "height-mm-per-v";

            throw Refusal(
                FlagPath.Of(flag),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"at {mmPerUnit:G6} mm per {surface.Unit} the surface would stand {tallest:G6} mm tall over a plane {longest:G6} mm long, more than {MaximumDrawnHeight:G} times its length"),
                $"give a smaller --{flag}");
        }

        var layers = Compose(surface, mmPerUnit, request.MeshLines, out var meshDrawn);
        var bar = Bar(surface, mmPerUnit, angles, low, high);

        // Measured once: the frame is refitted below for the scale's labels, and every refit
        // adds a handful of points to this, not another walk over every vertex drawn.
        var drawn = Framing.Extent.Of(layers.SelectMany(Points), angles.Azimuth, angles.Elevation);
        var aspect = (double)width / height;
        var matrix = drawn.Frame().Matrix(aspect);

        var tainted = warnings.Any(w => w.Severity == WarningSeverity.ValidityViolation);
        var cycleMean = request.CycleMean;
        var instantUs = probe.TimeVarying ? request.AtUs ?? 0.0 : (double?)null;

        // What the caption has to resolve: the fastest drive's period, where there is one.
        var periodUs = probe.Field is Einzel.Fields.ITimeVaryingField timed && double.IsFinite(timed.ShortestPeriodSeconds)
            ? timed.ShortestPeriodSeconds * 1e6
            : (double?)null;

        RasterOverlay? overlay = null;

        if (scale)
        {
            // Room for the scale's labels, which sit left of the bar, and for the caption above
            // it. Their sizes are in pixels and the frame is fitted in millimetres, and making
            // room shrinks the picture, which makes the same pixels more millimetres: so the room
            // is fitted again until the scale stops moving, not once.
            var textHeight = ScaleIndicator.TextHeightFor(width, height);
            var widest = new[] { low, high, 0.0 }.Concat(surface.Plateaus.Select(p => p.Height))
                .Where(double.IsFinite)
                .Max(v => StrokeFont.Width($"{Format(v, LabelScale.OfBar(low, high))} {surface.Unit}", textHeight));
            var reachPx = Math.Max(StrokeFont.Width("cycle mean", textHeight), widest) + (1.6 * textHeight) + 8.0;
            var liftPx = 3.0 * textHeight;
            var fitted = matrix;

            for (var pass = 0; pass < LabelFitPasses; pass++)
            {
                var across = Math.Sqrt((fitted[0] * fitted[0]) + (fitted[4] * fitted[4]) + (fitted[8] * fitted[8]));
                var up = Math.Sqrt((fitted[1] * fitted[1]) + (fitted[5] * fitted[5]) + (fitted[9] * fitted[9]));
                var reach = reachPx / (across * width / 2.0);
                var lift = liftPx / (up * height / 2.0);

                var points = new List<(double X, double Y, double Z)>(bar.Points);

                foreach (var (x, y, z) in bar.Points)
                {
                    points.Add((x - (fitted[0] / across * reach), y - (fitted[4] / across * reach), z - (fitted[8] / across * reach)));
                }

                var (tx, ty, tz) = bar.Points[1];
                points.Add((tx + (fitted[1] / up * lift), ty + (fitted[5] / up * lift), tz + (fitted[9] / up * lift)));

                var next = drawn.Including(points).Frame().Matrix(aspect);
                var nextAcross = Math.Sqrt((next[0] * next[0]) + (next[4] * next[4]) + (next[8] * next[8]));
                fitted = next;

                if (Math.Abs((nextAcross / across) - 1.0) < 1e-3)
                {
                    break;
                }
            }

            // Left out where it cannot be read, as the still's ruler is: seen from straight above
            // the bar is a point with every label on top of the last, and in a picture too small
            // for its labels they would run off the edge or squeeze the surface to a smudge. The
            // picture is then framed without it.
            var candidate = Overlay(surface, bar, fitted, width, height, low, high, mmPerUnit, cycleMean, instantUs, periodUs);

            var kept = Math.Sqrt((fitted[0] * fitted[0]) + (fitted[4] * fitted[4]) + (fitted[8] * fitted[8]))
                / Math.Sqrt((matrix[0] * matrix[0]) + (matrix[4] * matrix[4]) + (matrix[8] * matrix[8]));

            if (BarShows(fitted, width, height)
                && kept >= SmallestLabelledFraction
                && Fits(candidate, width, height))
            {
                overlay = candidate;
                matrix = fitted;
            }
        }

        // Now that the frame is settled: the mesh's bias is a number of pixels, and what a pixel
        // is worth in depth is the projection's to say.
        var bias = MeshDepthBias(matrix, width);
        layers = [.. layers.Select(layer => layer.Name == "mesh" ? layer with { DepthBias = bias } : layer)];

        // Under --dry-run nothing is written, so nothing is drawn: every number in the outcome is
        // settled above, and the raster is what costs.
        var rgb = dryRun
            ? null
            : Rasterizer.Draw(
                [.. layers.Select(StillCommand.Raster)],
                matrix,
                new RasterLighting(Brightness, 1.0),
                ColorRamp.Ground,
                width,
                height,
                lineWidth: 1.2,
                overlay: overlay);

        if (tainted && rgb is not null)
        {
            Rasterizer.Hatch(rgb, width, height);
        }

        var windowUs = cycleMean ? probe.CycleMeanWindowSecondsAt((request.AtUs ?? 0.0) * 1e-6) * 1e6 : null;

        var warningJson = warnings.Select(w => new WarningJson
        {
            Code = w.Code,
            Message = w.Message,
            Severity = w.Severity.ToString(),
            Suppressible = w.IsSuppressible,
        }).ToList();

        var outcome = new SurfaceOutcome
        {
            ModelPath = absolute,
            ModelHash = hash,
            Artifact = path,
            Written = !dryRun,
            Width = width,
            HeightPx = height,
            View = view,
            Plane = surface.Plane,
            OffsetMm = surface.OffsetMm,
            AcrossAxis = surface.AcrossAxis,
            DeepAxis = surface.DeepAxis,
            AcrossMm = [surface.Across[0], surface.Across[^1]],
            DeepMm = [surface.Deep[0], surface.Deep[^1]],
            Samples = [surface.Across.Count, surface.Deep.Count],
            MeshLines = meshDrawn,
            StepsMm = [
                (surface.Across[^1] - surface.Across[0]) / (surface.Across.Count - 1),
                (surface.Deep[^1] - surface.Deep[0]) / (surface.Deep.Count - 1)],
            Height = surface.Height,
            Unit = surface.Unit,
            ChargeNumber = surface.ChargeNumber,
            HeightMmPerUnit = mmPerUnit,
            UnitsPerMm = 1.0 / mmPerUnit,
            Lowest = low,
            Highest = high,
            SampledLowest = surface.SampledLowest,
            SampledHighest = surface.SampledHighest,
            AppliedLowest = surface.AppliedLowest,
            AppliedHighest = surface.AppliedHighest,
            TimeVarying = probe.TimeVarying,
            InstantUs = instantUs,
            CycleMean = cycleMean,
            CycleMeanWindowUs = windowUs,
            Plateaus = surface.Plateaus,
            Tainted = tainted,
            ScaleDrawn = overlay is not null,
            Csv = csv,
            Warnings = warningJson,
        };

        if (!dryRun)
        {
            // The picture and the table together, or neither: the picture used to be written
            // before the table was tried.
            var files = new List<(string? Flag, string Path, byte[] Content)>
            {
                (outFlag, path, PngWriter.Write(rgb!, width, height, Provenance(outcome, warnings))),
            };

            if (csv is not null)
            {
                files.Add(("csv", csv, ArtifactFiles.Utf8(ToCsv(surface, warnings))));
            }

            ArtifactFiles.WriteAll(files, absolute);
        }

        return outcome;
    }

    /// <summary>The sampled surface as CSV: one row per node, the unit in each column's name.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="warnings">What qualifies it, written as comment lines above the table (GRD-2).</param>
    /// <returns>The CSV text.</returns>
    /// <remarks>
    /// The numbers behind the picture, so a well depth read off it can be read off the data
    /// instead. A node inside a conductor carries the plateau's value and the conductor's label,
    /// quoted per RFC 4180 where it holds a comma, a quote or a line break - a name is whatever
    /// the model's author wrote.
    /// </remarks>
    public static string ToCsv(PotentialSurface surface, IReadOnlyList<ValidityWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(warnings);

        var invariant = CultureInfo.InvariantCulture;
        var text = new System.Text.StringBuilder();

        text.Append(invariant, $"# {surface.Height} on the {surface.Plane} plane at {surface.OffsetMm:G17} mm\n");

        // A message may run over several lines; each one stays a comment, or the second line
        // would be read as a row of the table.
        foreach (var warning in warnings)
        {
            text.Append(Csv.Comment(warning));
        }

        text.Append(invariant, $"{surface.AcrossAxis}_mm,{surface.DeepAxis}_mm,{(surface.Unit == "V" ? "potential_V" : "potential_energy_eV")},conductor\n");

        for (var i = 0; i < surface.Across.Count; i++)
        {
            for (var j = 0; j < surface.Deep.Count; j++)
            {
                var value = surface.At(i, j);
                var cell = double.IsFinite(value) ? value.ToString("G17", invariant) : string.Empty;
                text.Append(invariant, $"{surface.Across[i]:G17},{surface.Deep[j]:G17},{cell},{Csv.Field(surface.Inside[(i * surface.Deep.Count) + j])}\n");
            }
        }

        return text.ToString();
    }

    /// <summary>A height in words, as one label read on its own scale.</summary>
    /// <param name="value">The value.</param>
    /// <param name="span">
    /// The range the value is read against, or zero to use the value's own size. With no other
    /// labels to agree with, the value is the largest on its scale.
    /// </param>
    /// <returns>Its text, using only characters the stroke font draws; empty for a value that is not finite.</returns>
    /// <remarks>
    /// A label beside others on one scale goes through <see cref="LabelScale"/>, which chooses the
    /// notation and the precision once for all of them.
    /// </remarks>
    public static string Format(double value, double span = 0.0) =>
        double.IsFinite(value) ? Format(value, LabelScale.For(Math.Abs(value), span > 0.0 && double.IsFinite(span) ? span : 0.0)) : string.Empty;

    /// <summary>A height in words, in the notation and precision its scale chose for every label on it.</summary>
    /// <param name="value">The value.</param>
    /// <param name="scale">The scale it is read on.</param>
    /// <returns>Its text, using only characters the stroke font draws; empty for a value that is not finite.</returns>
    public static string Format(double value, LabelScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        // Not a number is no label at all: "0" beside a NaN would state a value nothing measured.
        if (!double.IsFinite(value))
        {
            return string.Empty;
        }

        if (value == 0.0 || Math.Abs(value) < scale.Resolution)
        {
            return "0";
        }

        if (scale.Figures is { } figures)
        {
            return Exponent(value, figures);
        }

        var text = Math.Round(value, scale.Decimals).ToString("F" + scale.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        // A value at or over the scale's resolution cannot round to zero at its decimals; this
        // guards the rule rather than the arithmetic.
        return text is "0" or "-0" ? Exponent(value) : text;
    }

    /// <summary>
    /// How every label on one scale is written: chosen once, from the scale's span and its largest
    /// value, so ticks on one bar never mix notations or precisions.
    /// </summary>
    /// <param name="Figures">Significant figures in exponent form, or null for fixed notation.</param>
    /// <param name="Decimals">Decimal places in fixed notation.</param>
    /// <param name="Resolution">Below this a value reads 0: under half the scale's last digit.</param>
    /// <param name="Flat">
    /// Whether the span is below what a double can resolve at the scale's size, so the labels would
    /// differ only in noise: written to the value's own three figures, and alike.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>One choice per scale.</b> Choosing per label switched notation at a thousandth of a unit,
    /// so 0.0009 and 0.0012 on one bar came out in two forms. Exponent form is used only where the
    /// largest value on the scale is itself small; otherwise fixed notation with the decimals the
    /// span needs - 1234.5 to 1234.50003 V is written with seven.
    /// </para>
    /// <para>
    /// <b>Never past what a double holds.</b> Fixed decimals are capped by the significant digits
    /// left at the scale's size, fifteen in all, so 1000 V is never written to a dozen places
    /// that are rounding. A span below the double's own resolution there is <see cref="Flat"/>.
    /// </para>
    /// <para>
    /// <b>A nonzero value read at its own size is never written 0</b>: 3e-8 V is "3e-8". On a scale
    /// shared with larger labels a value under half its last digit does read 0, deliberately - it
    /// is zero to the resolution every label there is read at.
    /// </para>
    /// </remarks>
    public sealed record LabelScale(int? Figures, int Decimals, double Resolution, bool Flat)
    {
        /// <summary>The scale for labels between two values.</summary>
        /// <param name="low">The scale's lower end.</param>
        /// <param name="high">Its upper end.</param>
        /// <returns>The choice every label on it is written with.</returns>
        public static LabelScale Of(double low, double high) =>
            For(Math.Max(double.IsFinite(low) ? Math.Abs(low) : 0.0, double.IsFinite(high) ? Math.Abs(high) : 0.0), high - low);

        /// <summary>The scale for the labels of a bar drawn from earth to a range's ends.</summary>
        /// <param name="low">The range's lower end.</param>
        /// <param name="high">Its upper end.</param>
        /// <returns>The choice, from the span the bar actually draws: earth to the farther end.</returns>
        /// <remarks>
        /// A surface's bar runs from <c>min(low, 0)</c> to <c>max(high, 0)</c>, and its labels are
        /// read against that: precision taken from <c>high - low</c> alone labelled a 0 to 1234.5 V
        /// bar "1234.50003" for a range a few microvolts wide.
        /// </remarks>
        public static LabelScale OfBar(double low, double high) => Of(Math.Min(low, 0.0), Math.Max(high, 0.0));

        /// <summary>The scale for labels whose largest magnitude and span are given.</summary>
        /// <param name="largest">The largest magnitude on the scale.</param>
        /// <param name="span">The range the labels are read against; zero or less for a value read at its own size.</param>
        /// <returns>The choice every label on it is written with.</returns>
        public static LabelScale For(double largest, double span)
        {
            if (!(largest > 0.0) || !double.IsFinite(largest))
            {
                // Nothing but zeros to write, or nothing finite: any choice writes "0".
                return new LabelScale(null, 0, 0.0, Flat: false);
            }

            var top = (int)Math.Floor(Math.Log10(largest));

            // Below what a double resolves at this size - a few hundred ulps - the labels would
            // differ only in rounding: read the scale at the value's own size instead.
            var ulp = Math.BitIncrement(largest) - largest;
            var flat = !(span > 0.0) || !double.IsFinite(span) || span < 256.0 * ulp;
            var reach = flat ? top : (int)Math.Floor(Math.Log10(span));

            if (largest < 1e-3)
            {
                var figures = Math.Clamp(top - reach + 3, 3, 15);
                return new LabelScale(figures, 0, flat ? 0.0 : 0.5 * Math.Pow(10.0, reach - 2), flat);
            }

            // Fifteen significant digits in all, so no more decimals than are left after the
            // integer part.
            var decimals = Math.Clamp(2 - reach, 0, Math.Min(15, Math.Max(0, 14 - top)));
            return new LabelScale(null, decimals, flat ? 0.0 : 0.5 * Math.Pow(10.0, -decimals), flat);
        }
    }

    // Significant figures in exponent form, three unless more are asked for, in characters the
    // stroke font draws: 3e-8, -1.25e-12.
    private static string Exponent(double value, int figures = 3) =>
        value.ToString("0." + new string('#', Math.Max(figures - 1, 0)) + "e+0", CultureInfo.InvariantCulture);

    /// <summary>An instant as the picture states it: compact at any size, in characters the stroke font draws.</summary>
    /// <param name="microseconds">The instant.</param>
    /// <param name="periodMicroseconds">
    /// The fastest drive's period, where the field has one: the caption then carries enough
    /// digits to place the instant in the cycle. Null for a field with no drive.
    /// </param>
    /// <returns>"t=2.5 µs", "t=12.3 ms", or with an exponent past a thousand seconds.</returns>
    /// <remarks>
    /// <para>
    /// <b>A caption on a driven field names the phase drawn.</b> Rounded to three figures,
    /// 100 µs and 100.25 µs on a 1 MHz drive were both "t=100 µs" over surfaces a quarter
    /// cycle apart - a rod pair at its peak and the same pair at zero. So where there is a
    /// drive the instant is written to <see cref="Einzel.Fields.FieldInstants.ResolutionFraction"/>
    /// of its period, the same fraction an instant must be placeable to at all, and then
    /// shortened to the fewest digits that still say it: 100.25 stays 100.25, and 100 stays 100.
    /// </para>
    /// <para>
    /// Without a drive there is no phase to tell apart and the caption stays at three figures.
    /// </para>
    /// </remarks>
    public static string Instant(double microseconds, double? periodMicroseconds = null)
    {
        var invariant = CultureInfo.InvariantCulture;
        var a = Math.Abs(microseconds);
        var (scale, unit) = a < 1e4 ? (1.0, "µs") : a < 1e7 ? (1e-3, "ms") : (1e-6, "s");

        if (periodMicroseconds is { } period && period > 0.0 && double.IsFinite(period))
        {
            // Decimal places that resolve the fraction of a period, in the unit shown.
            var step = Einzel.Fields.FieldInstants.ResolutionFraction * period * scale;
            var decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)), 0, 15);
            var text = (microseconds * scale).ToString("F" + decimals.ToString(invariant), invariant);

            if (text.Contains('.', StringComparison.Ordinal))
            {
                text = text.TrimEnd('0').TrimEnd('.');
            }

            // Never a nonzero instant written 0: an instant under the digits a period needs is
            // still not t = 0, and a caption saying it was would name a state not drawn.
            return text is "0" or "-0"
                ? microseconds == 0.0 ? $"t=0 {unit}" : $"t={Exponent(microseconds * scale)} {unit}"
                : $"t={text} {unit}";
        }

        return a < 1e9
            ? $"t={Format(microseconds * scale)} {unit}"
            : $"t={Exponent(microseconds / 1e6)} s";
    }

    /// <summary>
    /// The slope at a node from its neighbours: central where both are finite, one-sided against
    /// the finite one beside a hole, and flat only where neither is.
    /// </summary>
    /// <remarks>
    /// A refused point is NaN, and a central difference across it is NaN; zeroing that lit every
    /// node beside a hole as flat. The finite neighbour still says which way the surface slopes.
    /// </remarks>
    internal static double Slope(double low, double here, double high, double lowAt, double hereAt, double highAt)
    {
        var central = (high - low) / (highAt - lowAt);

        if (double.IsFinite(central))
        {
            return central;
        }

        var up = (high - here) / (highAt - hereAt);

        if (double.IsFinite(up))
        {
            return up;
        }

        var down = (here - low) / (hereAt - lowAt);

        return double.IsFinite(down) ? down : 0.0;
    }

    /// <summary>
    /// The range a surface is stated and scaled by: what was sampled, held to what the conductors
    /// hold where that is known.
    /// </summary>
    private static (double Low, double High) Range(PotentialSurface surface) => Stated(
        surface.SampledLowest,
        surface.SampledHighest,
        surface.AppliedLowest is { } low && surface.AppliedHighest is { } high ? (low, high) : null);

    private static (double Low, double High) Stated(double sampledLow, double sampledHigh, (double Low, double High)? applied)
    {
        if (applied is not { } bound)
        {
            return (sampledLow, sampledHigh);
        }

        var low = Math.Clamp(sampledLow, bound.Low, bound.High);
        var high = Math.Clamp(sampledHigh, bound.Low, bound.High);

        return (low, high);
    }

    /// <summary>The lowest and highest finite values on a surface.</summary>
    private static (double Low, double High) Range(double[] values)
    {
        var (low, high) = (double.PositiveInfinity, double.NegativeInfinity);

        for (var k = 0; k < values.Length; k++)
        {
            var v = values[k];

            if (double.IsFinite(v))
            {
                low = Math.Min(low, v);
                high = Math.Max(high, v);
            }
        }

        return low <= high ? (low, high) : (0.0, 0.0);
    }

    /// <summary>The deep samples in the picture's own z: see <see cref="PotentialSurface.DeepSign"/>.</summary>
    private static double[] PictureDeep(PotentialSurface surface)
    {
        var zs = new double[surface.Deep.Count];

        for (var j = 0; j < zs.Length; j++)
        {
            zs[j] = surface.DeepSign * surface.Deep[j];
        }

        return zs;
    }

    // Every how many samples a mesh line is drawn along each axis: the lines are a fixed
    // distance apart on both, the longer side over the number asked for, so a box sampled with
    // cells longer than they are wide still has a square mesh.
    private static (int Across, int Deep) Strides(PotentialSurface surface, int meshLines)
    {
        var across = surface.Across[^1] - surface.Across[0];
        var deep = surface.Deep[^1] - surface.Deep[0];
        var spacing = Math.Max(across, deep) / Math.Max(1, meshLines);

        static int Of(double extent, int count, double spacing) =>
            Math.Max(1, (int)Math.Round(spacing / (extent / Math.Max(1, count - 1))));

        return (Of(across, surface.Across.Count, spacing), Of(deep, surface.Deep.Count, spacing));
    }

    private static List<int> MeshIndices(int count, int stride)
    {
        var indices = new List<int>();

        for (var i = 0; i < count; i += stride)
        {
            indices.Add(i);
        }

        if (indices[^1] != count - 1)
        {
            indices.Add(count - 1);
        }

        return indices;
    }

    /// <summary>
    /// The wire mesh: every <c>stride</c>-th row and column of samples, at the sample resolution
    /// so a line follows the surface rather than cutting across it, colored by the value at each
    /// segment's middle. A segment with both ends inside one conductor is a plateau and is left
    /// undrawn, so the mesh stops at the plateau's edge.
    /// </summary>
    /// <remarks>
    /// One polyline per mesh line, broken only where a segment is left out, with a color per
    /// segment: twenty thousand two-point lines at the default size were the same picture drawn
    /// with twenty thousand records.
    /// </remarks>
    private static List<PictureLine> WireMesh(
        PotentialSurface surface,
        double mmPerUnit,
        int meshLines,
        Func<double, (double R, double G, double B)> ramp,
        out IReadOnlyList<int> drawn)
    {
        var nu = surface.Across.Count;
        var nv = surface.Deep.Count;
        var (strideU, strideV) = Strides(surface, meshLines);
        var lines = new List<PictureLine>();

        List<double>? points = null;
        List<double>? colors = null;

        void Flush()
        {
            if (points is { Count: > 3 })
            {
                lines.Add(new PictureLine(points, colors![0], colors[1], colors[2], colors));
            }

            points = null;
            colors = null;
        }

        void Segment(int i0, int j0, int i1, int j1)
        {
            var va = surface.Drawn(i0, j0);
            var vb = surface.Drawn(i1, j1);

            var na = surface.Inside[(i0 * nv) + j0];
            var nb = surface.Inside[(i1 * nv) + j1];

            if (!double.IsFinite(va) || !double.IsFinite(vb) || (na is not null && na == nb))
            {
                Flush();
                return;
            }

            if (points is null)
            {
                points = [surface.Across[i0], va * mmPerUnit, surface.DeepSign * surface.Deep[j0]];
                colors = [];
            }

            var (r, g, b) = ramp(0.5 * (va + vb));

            points.AddRange([surface.Across[i1], vb * mmPerUnit, surface.DeepSign * surface.Deep[j1]]);
            colors!.AddRange([r * 0.8, g * 0.8, b * 0.8]);
        }

        var rows = MeshIndices(nv, strideV);
        var columns = MeshIndices(nu, strideU);
        drawn = [columns.Count, rows.Count];

        foreach (var j in rows)
        {
            for (var i = 0; i + 1 < nu; i++)
            {
                Segment(i, j, i + 1, j);
            }

            Flush();
        }

        foreach (var i in columns)
        {
            for (var j = 0; j + 1 < nv; j++)
            {
                Segment(i, j, i, j + 1);
            }

            Flush();
        }

        return lines;
    }

    /// <summary>
    /// A curtain down each edge of the sampled rectangle, from the surface to the plane at earth.
    /// </summary>
    /// <remarks>
    /// Without it, an electrode running out to the edge of the box - an einzel lens's tube to the
    /// domain's outer radius - is a plateau with nothing under its open side, and the picture
    /// shows a hole through the instrument where the box simply ends.
    /// </remarks>
    private static List<PictureMesh> Skirts(
        PotentialSurface surface, double mmPerUnit, Func<double, (double R, double G, double B)> ramp)
    {
        var nu = surface.Across.Count;
        var nv = surface.Deep.Count;
        var vertices = new List<double>();
        var normals = new List<double>();
        var colors = new List<double>();
        var triangles = new List<int>();

        void Edge(IReadOnlyList<(int I, int J)> nodes, double nx, double nz)
        {
            for (var e = 0; e + 1 < nodes.Count; e++)
            {
                var (i0, j0) = nodes[e];
                var (i1, j1) = nodes[e + 1];
                var (va, vb) = (surface.Drawn(i0, j0), surface.Drawn(i1, j1));

                if (!double.IsFinite(va) || !double.IsFinite(vb))
                {
                    continue;
                }

                var na = surface.Inside[(i0 * nv) + j0];
                var metal = na is not null && na == surface.Inside[(i1 * nv) + j1];
                var (r, g, b) = metal ? Plateau(ramp(0.5 * (va + vb))) : ramp(0.5 * (va + vb));

                if (!metal)
                {
                    (r, g, b) = (r + ((1.0 - r) * SurfaceTint * 0.7), g + ((1.0 - g) * SurfaceTint * 0.7), b + ((1.0 - b) * SurfaceTint * 0.7));
                }

                var start = vertices.Count / 3;

                foreach (var (i, j, top) in new[] { (i0, j0, true), (i1, j1, true), (i1, j1, false), (i0, j0, false) })
                {
                    vertices.AddRange([surface.Across[i], top ? surface.Drawn(i, j) * mmPerUnit : 0.0, surface.DeepSign * surface.Deep[j]]);
                    normals.AddRange([nx, 0.0, nz * surface.DeepSign]);
                    colors.AddRange([r, g, b]);
                }

                triangles.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
            }
        }

        Edge([.. Enumerable.Range(0, nu).Select(i => (i, 0))], 0.0, -1.0);
        Edge([.. Enumerable.Range(0, nu).Select(i => (i, nv - 1))], 0.0, 1.0);
        Edge([.. Enumerable.Range(0, nv).Select(j => (0, j))], -1.0, 0.0);
        Edge([.. Enumerable.Range(0, nv).Select(j => (nu - 1, j))], 1.0, 0.0);

        return triangles.Count == 0 ? [] : [new PictureMesh(vertices, normals, triangles, 1.0, 1.0, 1.0, colors)];
    }

    /// <summary>Where the vertical scale stands: beside the base corner that appears leftmost.</summary>
    private static (double X, double Z, List<(double X, double Y, double Z)> Points) Bar(
        PotentialSurface surface, double mmPerUnit, (double Azimuth, double Elevation) angles, double low, double high)
    {
        var (x, z) = ScaleFoot(surface, angles.Azimuth, angles.Elevation);

        var bottom = Math.Min(low, 0.0) * mmPerUnit;
        var top = Math.Max(high, 0.0) * mmPerUnit;

        return (x, z, [(x, bottom, z), (x, top, z)]);
    }

    /// <summary>
    /// Where the foot of the vertical scale stands on the base plane, in the picture's own
    /// coordinates: across, and the picture's z (the deep coordinate times
    /// <see cref="PotentialSurface.DeepSign"/>).
    /// </summary>
    /// <param name="surface">The sampled surface.</param>
    /// <param name="azimuth">The view's turn about the vertical, in degrees.</param>
    /// <param name="elevation">The view's tilt, in degrees.</param>
    /// <returns>The foot: the base corner that appears leftmost, set out from the surface.</returns>
    /// <remarks>
    /// <para>
    /// <b>Set out along the plane axis that shows on screen.</b> The gap was always along the
    /// across axis, which seen from the front (azimuth 90) points at the viewer and projects to
    /// nothing: the bar stood on the surface's own edge. It is set out along whichever of the
    /// two base axes moves it further across the screen.
    /// </para>
    /// <para>
    /// <b>And the corner is chosen with a tolerance.</b> From the front or the side two
    /// corners are equally far left, and which was "leftmost" hung on the cosine of ninety
    /// degrees being 6e-17 and not zero. Corners within a billionth of the base's width on
    /// screen are level, and of level corners the first in a fixed order is taken.
    /// </para>
    /// </remarks>
    public static (double Across, double Z) ScaleFoot(PotentialSurface surface, double azimuth, double elevation)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var (a0, a1, d0, d1) = (
            surface.Across[0], surface.Across[^1], surface.DeepSign * surface.Deep[0], surface.DeepSign * surface.Deep[^1]);
        var corners = new[] { (a0, d0), (a0, d1), (a1, d0), (a1, d1) };

        // Leftmost on screen, measured with a framing at the picture's own angles.
        var probe = Framing.Around(corners.Select(c => (c.Item1, 0.0, c.Item2)), azimuth, elevation)
            .Matrix(1.0);

        double Screen((double A, double D) c) => (probe[0] * c.A) + (probe[8] * c.D);

        var leftmost = corners.Min(Screen);
        var level = 1e-9 * (corners.Max(Screen) - leftmost);
        var (x, z) = corners.First(c => Screen(c) - leftmost <= level);

        // Set out from the corner, so the bar does not stand on the surface's own edge: away
        // from the surface along the base axis that shows more on screen.
        var gap = 0.06 * Math.Max(a1 - a0, Math.Abs(d1 - d0));

        if (Math.Abs(probe[0]) >= Math.Abs(probe[8]))
        {
            x += x == a0 ? -gap : gap;
        }
        else
        {
            z += z == d0 ? (d0 <= d1 ? -gap : gap) : (d1 <= d0 ? -gap : gap);
        }

        return (x, z);
    }

    /// <summary>The vertical scale and its labels, in pixels over the finished picture.</summary>
    private static RasterOverlay Overlay(
        PotentialSurface surface,
        (double X, double Z, List<(double X, double Y, double Z)> Points) bar,
        double[] matrix,
        int width,
        int height,
        double low,
        double high,
        double mmPerUnit,
        bool cycleMean,
        double? instantUs,
        double? periodUs)
    {
        (double X, double Y) Pixel(double x, double y, double z)
        {
            var cx = (matrix[0] * x) + (matrix[4] * y) + (matrix[8] * z) + matrix[12];
            var cy = (matrix[1] * x) + (matrix[5] * y) + (matrix[9] * z) + matrix[13];

            return ((cx + 1.0) * 0.5 * width, (1.0 - cy) * 0.5 * height);
        }

        var (ir, ig, ib) = ScaleIndicator.Ink;
        var textHeight = ScaleIndicator.TextHeightFor(width, height);
        var tick = 0.6 * textHeight;

        var bottom = Pixel(bar.X, bar.Points[0].Y, bar.Z);
        var topPx = Pixel(bar.X, bar.Points[1].Y, bar.Z);

        var strokes = new List<OverlayStroke> { new([bottom.X, bottom.Y, topPx.X, topPx.Y], ir, ig, ib) };
        var texts = new List<OverlayText>();

        var values = ScaleTicks(
            low,
            high,
            surface.Plateaus.OrderByDescending(p => p.Samples).Select(p => p.Height),
            v => Pixel(bar.X, v * mmPerUnit, bar.Z).Y,
            textHeight);

        foreach (var (value, label) in ScaleMarks(low, high, values, surface.Unit))
        {
            var (px, py) = Pixel(bar.X, value * mmPerUnit, bar.Z);

            // Every requested mark keeps its tick; a label is left off only where it would repeat
            // one already written.
            strokes.Add(new OverlayStroke([px - tick, py, px, py], ir, ig, ib));

            if (label is not null)
            {
                var labelWidth = StrokeFont.Width(label, textHeight);
                texts.Add(new OverlayText(label, px - tick - (0.4 * textHeight) - (0.5 * labelWidth), py, textHeight, ir, ig, ib));
            }
        }

        // What instant the heights are, above the bar: a picture of a driven field is a frame.
        var caption = cycleMean ? "cycle mean" : instantUs is { } t ? Instant(t, periodUs) : null;

        if (caption is not null)
        {
            var captionWidth = StrokeFont.Width(caption, textHeight);
            texts.Add(new OverlayText(
                caption, Math.Max(topPx.X, 0.5 * captionWidth + 2), topPx.Y - (1.8 * textHeight), textHeight, ir, ig, ib));
        }

        return new RasterOverlay(strokes, texts, ScaleIndicator.LineWidthPx);
    }

    /// <summary>
    /// The scale's marks and what each is labelled: written with one notation and precision from
    /// the span the bar draws, and a label that would repeat one already written left off - its
    /// tick still drawn.
    /// </summary>
    /// <param name="low">The lowest value of the surface.</param>
    /// <param name="high">The highest value of the surface.</param>
    /// <param name="values">The values marked, as <see cref="ScaleTicks"/> chose them.</param>
    /// <param name="unit">The unit, after each label.</param>
    /// <returns>Each value, with its label or null where it shares the label above it.</returns>
    /// <remarks>
    /// Two marks a rounding apart - on a bar from earth to 1000 V, the top and a plateau a
    /// nanovolt below it - write the same text, and the first version dropped the repeat together
    /// with its tick on a flat scale: a requested mark, earth or a plateau, gone without a word.
    /// Every mark is kept; repeated text is merged into the one label.
    /// </remarks>
    internal static IReadOnlyList<(double Value, string? Label)> ScaleMarks(double low, double high, IEnumerable<double> values, string unit)
    {
        var labels = LabelScale.OfBar(low, high);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var marks = new List<(double, string?)>();

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
            {
                continue;
            }

            var label = $"{Format(value, labels)} {unit}";
            marks.Add((value, written.Add(label) ? label : null));
        }

        return marks;
    }

    /// <summary>
    /// The values labelled on the vertical scale, in the order they are chosen: earth, the two
    /// ends, then the plateaus by size - each kept only where its label has room.
    /// </summary>
    /// <param name="low">The lowest value of the surface.</param>
    /// <param name="high">The highest value of the surface.</param>
    /// <param name="plateaus">The plateaus' heights, the largest plateau first.</param>
    /// <param name="rowOf">Where a value sits on the bar, in pixels down the picture.</param>
    /// <param name="textHeightPx">The height of a label's text, in pixels.</param>
    /// <returns>The values to label.</returns>
    /// <remarks>
    /// <b>Earth first, always.</b> The bar runs from the lower of the lowest value and zero to
    /// the higher of the highest and zero, so zero is on it; it is the level the picture is
    /// drawn about and the one a reader looks for. Both ends used to be labelled whatever they
    /// collided with and earth only if it had room, so a minimum a hair below zero took the
    /// foot's label and left <c>0 V</c> out. An end that would sit on top of earth gives way.
    /// </remarks>
    public static IReadOnlyList<double> ScaleTicks(
        double low, double high, IEnumerable<double> plateaus, Func<double, double> rowOf, double textHeightPx)
    {
        ArgumentNullException.ThrowIfNull(plateaus);
        ArgumentNullException.ThrowIfNull(rowOf);

        var labelled = new List<double>();
        var rows = new List<double>();

        foreach (var value in new[] { 0.0, Math.Max(high, 0.0), Math.Min(low, 0.0) }.Concat(plateaus))
        {
            if (!double.IsFinite(value))
            {
                continue;
            }

            var row = rowOf(value);

            if (rows.Any(r => Math.Abs(r - row) < 1.5 * textHeightPx))
            {
                continue;
            }

            rows.Add(row);
            labelled.Add(value);
        }

        return labelled;
    }

    /// <summary>
    /// Whether the vertical bar reads as a length in this view: false where it points at the
    /// viewer, by the rule the still's ruler uses for an arm (<see cref="ScaleIndicator.HiddenBelow"/>).
    /// </summary>
    private static bool BarShows(double[] matrix, int width, int height)
    {
        // A millimetre of height on screen against a millimetre across the screen. Asked of the
        // view, not of the bar's own length, so a flat surface - whose bar has no length in any
        // view - is still drawn with its one label from the side and still left out from above.
        var seen = Math.Sqrt(Math.Pow(matrix[4] * width / 2.0, 2) + Math.Pow(matrix[5] * height / 2.0, 2));
        var full = Math.Sqrt((matrix[0] * matrix[0]) + (matrix[4] * matrix[4]) + (matrix[8] * matrix[8])) * width / 2.0;

        return seen >= ScaleIndicator.HiddenBelow * full;
    }

    /// <summary>Whether every stroke and every line of text of an overlay lies inside the picture.</summary>
    private static bool Fits(RasterOverlay overlay, int width, int height)
    {
        foreach (var stroke in overlay.Strokes)
        {
            for (var i = 0; i + 1 < stroke.PointsPx.Count; i += 2)
            {
                if (stroke.PointsPx[i] < 0.0 || stroke.PointsPx[i] > width
                    || stroke.PointsPx[i + 1] < 0.0 || stroke.PointsPx[i + 1] > height)
                {
                    return false;
                }
            }
        }

        foreach (var text in overlay.Texts)
        {
            var half = 0.5 * StrokeFont.Width(text.Text, text.HeightPx);

            if (text.CenterXPx - half < 0.0 || text.CenterXPx + half > width
                || text.CenterYPx - (0.5 * text.HeightPx) < 0.0 || text.CenterYPx + (0.5 * text.HeightPx) > height)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The largest potential any electrode of a model reaches, in volts: over every state a
    /// sequence puts it in (a ramp's end included), its DC plus the amplitude of every generator
    /// it taps, and for an edge-profiled board the largest value on its profile - and for an
    /// analytic element, the potential it declares, in every phase.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>Volts; zero for a model with no electrodes.</returns>
    /// <remarks>
    /// Over every state and every tap, because reading the DC alone - the recurring way this
    /// project has read a driven electrode as earthed - made an RF-only model at rest look like
    /// a model of nothing, so its rounding was stretched to the full relief.
    /// </remarks>
    public static double PeakElectrodePotential(Core.Model.CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var peak = 0.0;

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } plane)
            {
                foreach (var state in Core.Model.ElectrodeOverlap.StatesOf(plane.Electrodes, plane.Stages))
                {
                    foreach (var electrode in state)
                    {
                        var dc = electrode.Shape == Core.Model.ElectrodeShape.EdgeProfile
                            ? electrode.Profile.Select(point => Math.Abs(point.Potential)).DefaultIfEmpty(0.0).Max()
                            : Math.Abs(electrode.Potential);

                        peak = Math.Max(peak, dc + electrode.Taps.Sum(t => Math.Abs(t.Amplitude)));
                    }
                }
            }

            if (element.Solve3D is { } volume)
            {
                foreach (var state in Core.Model.ElectrodeOverlap3D.StatesOf(volume.Electrodes, volume.Stages))
                {
                    foreach (var electrode in state)
                    {
                        peak = Math.Max(peak, Math.Abs(electrode.Potential) + electrode.Taps.Sum(t => Math.Abs(t.Amplitude)));
                    }
                }
            }

            // An analytic element has no electrodes, but it declares the potentials it stands
            // for, in its base state and in every phase of a sequence.
            foreach (var state in element.Phases.Prepend(element))
            {
                peak = Math.Max(peak, AnalyticPeak(state));
            }
        }

        return peak;
    }

    /// <summary>The largest potential an analytic element declares, in volts.</summary>
    /// <param name="element">The element, in one state.</param>
    /// <returns>Volts; zero for a kind that declares none.</returns>
    /// <remarks>
    /// An ideal quadrupole's rods hold its DC plus its drive amplitude; a half-space's cap holds
    /// its gradient times its turning depth; a uniform field bounded by a region spans its field
    /// times the region's diagonal. A uniform field filling all space, and the orbital trap's
    /// quadro-logarithmic potential, have no applied potential to name - their size is on the
    /// plane, and the plane's own range is in the reference too.
    /// </remarks>
    private static double AnalyticPeak(Core.Model.CompiledField element) => element.Kind switch
    {
        Core.Model.CompiledFieldKind.IdealQuadrupoleRf =>
            Math.Abs(element.DirectPotentialSi) + Math.Abs(element.DriveAmplitudeSi),
        Core.Model.CompiledFieldKind.HalfSpaceUniform =>
            Math.Abs(element.PotentialGradientSi * element.TurningDepthSi),
        Core.Model.CompiledFieldKind.Uniform when element.Region is { } box =>
            element.Field.Length * Math.Sqrt(
                ((box.MaxX - box.MinX) * (box.MaxX - box.MinX))
                + ((box.MaxY - box.MinY) * (box.MaxY - box.MinY))
                + ((box.MaxZ - box.MinZ) * (box.MaxZ - box.MinZ))),
        _ => 0.0,
    };

    private static IEnumerable<(double X, double Y, double Z)> Points(PictureLayer layer)
    {
        foreach (var mesh in layer.Meshes)
        {
            for (var i = 0; i + 2 < mesh.VerticesMm.Count; i += 3)
            {
                yield return (mesh.VerticesMm[i], mesh.VerticesMm[i + 1], mesh.VerticesMm[i + 2]);
            }
        }

        foreach (var line in layer.Lines)
        {
            for (var i = 0; i + 2 < line.PointsMm.Count; i += 3)
            {
                yield return (line.PointsMm[i], line.PointsMm[i + 1], line.PointsMm[i + 2]);
            }
        }
    }

    private static List<(string Keyword, string Text)> Provenance(
        SurfaceOutcome outcome, IReadOnlyList<ValidityWarning> warnings)
    {
        var invariant = CultureInfo.InvariantCulture;
        var model = Path.GetFileName(outcome.ModelPath);
        var when = outcome.CycleMean
            ? string.Create(invariant, $"the mean over one drive cycle ({outcome.CycleMeanWindowUs:G6} us window, operating point t = {outcome.InstantUs:G6} us)")
            : outcome.InstantUs is { } t ? string.Create(invariant, $"the instant t = {t:G6} us") : "a static field";

        var text = new List<(string, string)>
        {
            ("Title", $"{model}, {outcome.Height} surface on the {outcome.Plane} plane"),
            ("Software", $"einzel {EngineBuild.Version}, solver behaviour {EngineBuild.SolverBehaviourVersion}"),
            ("Source", $"{model} {outcome.ModelHash}"),
            ("Description", string.Create(
                invariant,
                $"einzel render surface: the {outcome.Height} on the {outcome.Plane} plane at {outcome.OffsetMm:G6} mm, {outcome.AcrossAxis} from {outcome.AcrossMm[0]:G6} to {outcome.AcrossMm[1]:G6} mm across and {outcome.DeepAxis} from {outcome.DeepMm[0]:G6} to {outcome.DeepMm[1]:G6} mm deep, {outcome.Samples[0]} by {outcome.Samples[1]} samples, drawn as height in the {outcome.View} view; {when}; conductors are flat plateaus at their own potential")),
            ("Scale", string.Create(
                invariant,
                $"height {outcome.HeightMmPerUnit:G6} mm per {outcome.Unit} ({outcome.UnitsPerMm:G6} {outcome.Unit} per drawn mm), earth at the plane; drawn values from {outcome.Lowest:G6} to {outcome.Highest:G6} {outcome.Unit}")),
        };

        // Numbered keywords, with the conductor's name in the text: a PNG keyword is at most 79
        // characters of printable Latin-1 and a name is whatever the model's author wrote -
        // "électrode", "plate µ1", a sentence - while the text of an iTXt chunk is UTF-8 of any
        // length. A name used as the keyword made a valid model an internal error with no picture.
        for (var i = 0; i < outcome.Plateaus.Count; i++)
        {
            var plateau = outcome.Plateaus[i];
            text.Add((string.Create(invariant, $"Plateau {i + 1}"), string.Create(
                invariant, $"{plateau.Conductor}: {plateau.PotentialV:G6} V over {plateau.Samples} samples")));
        }

        for (var i = 0; i < warnings.Count; i++)
        {
            var w = warnings[i];
            text.Add((string.Create(invariant, $"Warning {i + 1}"), $"[{w.Severity}] {w.Code}: {w.Message}"));
        }

        return text;
    }



    private static string DefaultPlane((Vec3 Min, Vec3 Max)? domain, bool twoD)
    {
        // A cross-section or an axisymmetric solve is drawn on its own plane. A volume is cut
        // through its middle by the plane holding its two longer extents - the plane normal to
        // its thinnest side, y before x before z on a tie, so a trap along z is cut through x.
        if (twoD || domain is not { } d)
        {
            return "xy";
        }

        var (sx, sy, sz) = (d.Max.X - d.Min.X, d.Max.Y - d.Min.Y, d.Max.Z - d.Min.Z);

        return sy <= sx && sy <= sz ? "xz" : sx <= sz ? "yz" : "xy";
    }

    private static (int A, int B, int N) Axes(string plane) => plane switch
    {
        "xy" => (0, 1, 2),
        "xz" => (0, 2, 1),
        "yz" => (1, 2, 0),
        _ => throw Refusal(FlagPath.Of("plane"), $"the plane is xy, xz or yz; got '{plane}'"),
    };

    private static string Name(int axis) => axis switch { 0 => "x", 1 => "y", _ => "z" };

    private static double Component(Vec3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static Vec3 Mid(Vec3 a, Vec3 b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);

    /// <summary>A label for each conductor met on the plane, unique among them.</summary>
    /// <param name="keys">Each conductor's element and name.</param>
    /// <returns>The labels, in the order of <paramref name="keys"/>.</returns>
    /// <remarks>
    /// The name alone where only one element has a conductor of that name, and <c>name (element
    /// N)</c> where several do. A label that still collides - a conductor literally named
    /// <c>plate (element 1)</c> beside element 1's <c>plate</c> - takes a further <c>[2]</c>,
    /// <c>[3]</c>, in the order of element and name, so no two conductors share one: a shared
    /// label was a shared plateau.
    /// </remarks>
    public static IReadOnlyList<string> Labels(IReadOnlyList<(int? Element, string Name)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var elementsOf = new Dictionary<string, HashSet<int?>>(StringComparer.Ordinal);

        foreach (var (element, name) in keys)
        {
            if (!elementsOf.TryGetValue(name, out var set))
            {
                elementsOf[name] = set = [];
            }

            set.Add(element);
        }

        var labels = new string[keys.Count];
        var used = new HashSet<string>(StringComparer.Ordinal);

        // Plain names first, so a conductor whose own name is unique keeps it.
        var order = Enumerable.Range(0, keys.Count)
            .OrderBy(i => elementsOf[keys[i].Name].Count > 1 ? 1 : 0)
            .ThenBy(i => keys[i].Element ?? -1)
            .ThenBy(i => keys[i].Name, StringComparer.Ordinal);

        foreach (var i in order)
        {
            var (element, name) = keys[i];
            var label = elementsOf[name].Count > 1
                ? string.Create(CultureInfo.InvariantCulture, $"{name} (element {element?.ToString(CultureInfo.InvariantCulture) ?? "?"})")
                : name;

            var candidate = label;

            for (var n = 2; !used.Add(candidate); n++)
            {
                candidate = string.Create(CultureInfo.InvariantCulture, $"{label} [{n}]");
            }

            labels[i] = candidate;
        }

        return labels;
    }

    /// <summary>A point on the sampled plane, in the model's axes.</summary>
    /// <param name="across">The model axis (0 x, 1 y, 2 z) the plane's first coordinate runs along.</param>
    /// <param name="deep">The model axis its second coordinate runs along.</param>
    /// <param name="normal">The model axis normal to the plane.</param>
    /// <param name="u">The first coordinate.</param>
    /// <param name="v">The second coordinate.</param>
    /// <param name="offset">How far along the normal.</param>
    /// <returns>The point.</returns>
    /// <remarks>
    /// Called once per sample inside the parallel loop, so it allocates nothing: it used to fill a
    /// three-element array per sample, a million arrays for a surface at its largest.
    /// </remarks>
    public static Vec3 PlanePoint(int across, int deep, int normal, double u, double v, double offset)
    {
        double On(int axis) => axis == across ? u : axis == deep ? v : axis == normal ? offset : double.NaN;

        return new Vec3(On(0), On(1), On(2));
    }

    private static double[] Grid(double from, double to, int count)
    {
        var values = new double[count];

        for (var i = 0; i < count; i++)
        {
            // Ends exact, as the probe's line is: an interpolated end lands an ulp off.
            values[i] = i == 0 ? from : i == count - 1 ? to : from + ((to - from) * i / (count - 1));
        }

        return values;
    }

    private static void Check(SurfaceRequest request)
    {
        if (request.Plane is { } plane && !Planes.Contains(plane, StringComparer.Ordinal))
        {
            throw Refusal(FlagPath.Of("plane"), $"the plane is xy, xz or yz; got '{plane}'");
        }

        if (request.OffsetMm is { } offset && !double.IsFinite(offset))
        {
            throw Refusal(FlagPath.Of("offset-mm"), "the offset must be a finite number of millimetres");
        }

        if (request.BoxMm is { } box && (box.Count != 4 || box.Any(v => !double.IsFinite(v))))
        {
            throw Refusal(
                FlagPath.Of("box-mm"),
                "the rectangle is four finite numbers in millimetres, in the plane's own axis order (for xz: x0,z0,x1,z1)");
        }

        // Each corner finite and the extent not: -1e308 to 1e308 is an infinite width, an
        // infinite step between samples, and no surface.
        if (request.BoxMm is { Count: 4 } corners && (!double.IsFinite(corners[2] - corners[0]) || !double.IsFinite(corners[3] - corners[1])))
        {
            throw Refusal(
                FlagPath.Of("box-mm"),
                "the rectangle's width and height must be finite numbers of millimetres; these corners are finite and the distance between them is not");
        }

        if (request.Samples < 3 || request.Samples > MaximumSamples)
        {
            throw Refusal(FlagPath.Of("samples"), $"samples along the longer side are 3 to {MaximumSamples}; got {request.Samples}");
        }

        if (request.MeshLines < MinimumMeshLines)
        {
            throw Refusal(FlagPath.Of("mesh-lines"), $"at least {MinimumMeshLines} mesh line; got {request.MeshLines}");
        }

        if (request.Height is not (SurfaceHeight.Potential or SurfaceHeight.Energy))
        {
            throw Refusal(FlagPath.Of("height"), $"the height is potential or energy; got '{request.Height}'");
        }

        if (!(request.Relief > 0.0) || !(request.Relief <= MaximumRelief))
        {
            throw Refusal(
                FlagPath.Of("relief"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the relief is the drawn height of the whole range as a fraction of the longer side, more than 0 and at most {MaximumRelief:G}; got {request.Relief:G6}"));
        }

        if (request.HeightMmPerUnit is { } scale && (!(scale > 0.0) || !double.IsFinite(scale)))
        {
            // Located at the flag that was given: the energy's scale is per electronvolt.
            throw Refusal(
                request.Height == SurfaceHeight.Energy ? FlagPath.Of("height-mm-per-ev") : FlagPath.Of("height-mm-per-v"),
                "the height scale must be a positive, finite number of millimetres per unit");
        }

        // The rule probe and render section apply: a model's clock starts at zero, so a negative
        // instant has no state to draw - a sequence would answer with its first phase under a
        // time the instrument never reaches.
        ProbeCommand.CheckInstant(request.AtUs);

        if (request.DeepSamples is { } deep && (deep < 2 || deep > MaximumSamples))
        {
            throw Refusal(FlagPath.Of("samples"), $"samples along the shorter side are 2 to {MaximumSamples}; got {deep}");
        }
    }

    private static EinzelException Refusal(string path, string constraint, string? suggestion = null) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = suggestion ?? "see 'einzel render surface' for the flags",
        });
}
