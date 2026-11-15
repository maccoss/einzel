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
}

/// <summary>Where, when and how to draw a potential surface.</summary>
/// <param name="Plane">"xy", "xz" or "yz", or absent for the model's own plane.</param>
/// <param name="OffsetMm">How far along the plane's normal, in millimetres; absent for the domain's middle.</param>
/// <param name="BoxMm">
/// The sampled rectangle as four numbers in the plane's own axis order (for "xz": x0, z0, x1, z1),
/// in millimetres; absent for the solve domain's extent in the plane.
/// </param>
/// <param name="Samples">Samples along the longer side of the box; the shorter follows at the same spacing.</param>
/// <param name="MeshLines">Drawn mesh lines along the longer side, so the wire mesh reads as a mesh.</param>
/// <param name="Height">One of the <see cref="SurfaceHeight"/> values.</param>
/// <param name="Relief">
/// The drawn height of the whole range, as a fraction of the box's longer side; used only when no
/// explicit scale is given.
/// </param>
/// <param name="HeightMmPerUnit">
/// An explicit scale: millimetres of drawn height per volt (or per electronvolt for the energy).
/// </param>
/// <param name="AtUs">The instant, in microseconds, or absent when none was asked for.</param>
/// <param name="CycleMean">Whether to draw the potential averaged over one drive cycle.</param>
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
    bool CycleMean = false);

/// <summary>A conductor met by the plane, drawn as a flat plateau at its own potential.</summary>
/// <param name="Conductor">Its name.</param>
/// <param name="PotentialV">
/// The potential it is drawn at: the median of the field's values over the deeper half of the samples
/// inside it, which for a solved element is the electrode's own potential.
/// </param>
/// <param name="Height">The same in the drawn quantity (volts or electronvolts).</param>
/// <param name="Samples">How many samples fell inside it.</param>
/// <param name="LowestV">The lowest potential the field reported anywhere inside it on the plane.</param>
/// <param name="HighestV">The highest, so a plateau that is not really flat says so.</param>
public sealed record SurfacePlateau(
    string Conductor, double PotentialV, double Height, int Samples, double LowestV, double HighestV);

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

    /// <summary>The other in-plane axis, running into the picture.</summary>
    public required string DeepAxis { get; init; }

    /// <summary>Sample positions along <see cref="AcrossAxis"/>, in millimetres.</summary>
    public required IReadOnlyList<double> Across { get; init; }

    /// <summary>Sample positions along <see cref="DeepAxis"/>, in millimetres.</summary>
    public required IReadOnlyList<double> Deep { get; init; }

    /// <summary>
    /// The drawn quantity at each node - volts, or electronvolts - with every point inside a
    /// conductor set to that conductor's plateau, and NaN where the field refused.
    /// </summary>
    public required IReadOnlyList<double> Values { get; init; }

    /// <summary>The conductor each node is inside, or null for free space.</summary>
    public required IReadOnlyList<string?> Inside { get; init; }

    /// <summary>The plateaus, one per conductor the plane meets.</summary>
    public required IReadOnlyList<SurfacePlateau> Plateaus { get; init; }

    /// <summary>One of the <see cref="SurfaceHeight"/> values.</summary>
    public required string Height { get; init; }

    /// <summary>"V" or "eV".</summary>
    public required string Unit { get; init; }

    /// <summary>The ion's charge in units of e, used for the energy; absent for a potential.</summary>
    public double? ChargeNumber { get; init; }

    /// <summary>Points outside every element's domain.</summary>
    public required int OutsideDomain { get; init; }

    /// <summary>Points the field refused.</summary>
    public required int Refused { get; init; }

    /// <summary>The value at a node.</summary>
    /// <param name="i">Index along <see cref="Across"/>.</param>
    /// <param name="j">Index along <see cref="Deep"/>.</param>
    /// <returns>The drawn quantity there.</returns>
    public double At(int i, int j) => Values[(i * Deep.Count) + j];
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

    /// <summary>The model axis into the picture.</summary>
    public required string DeepAxis { get; init; }

    /// <summary>The sampled extent along the across axis, in millimetres.</summary>
    public required IReadOnlyList<double> AcrossMm { get; init; }

    /// <summary>The sampled extent along the deep axis, in millimetres.</summary>
    public required IReadOnlyList<double> DeepMm { get; init; }

    /// <summary>Samples along each axis, across then deep.</summary>
    public required IReadOnlyList<int> Samples { get; init; }

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

    /// <summary>The lowest value drawn, in <see cref="Unit"/>.</summary>
    public required double Lowest { get; init; }

    /// <summary>The highest value drawn, in <see cref="Unit"/>.</summary>
    public required double Highest { get; init; }

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

    /// <summary>Whether the vertical scale was drawn on the picture.</summary>
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

    /// <summary>Mesh lines along the longer side unless asked otherwise.</summary>
    public const int DefaultMeshLines = 40;

    /// <summary>The drawn height of the whole range, as a fraction of the longer side.</summary>
    public const double DefaultRelief = 0.35;

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

    private const double ElementaryCharge = 1.602176634e-19;

    /// <summary>Samples a model's potential on a plane.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="request">Where and how.</param>
    /// <returns>The sampled surface, the probe it came from, and the warnings sampling earned.</returns>
    /// <exception cref="EinzelException">The model does not validate, or the request is malformed.</exception>
    public static (PotentialSurface Surface, FieldProbe Probe, IReadOnlyList<ValidityWarning> Warnings) Sample(
        string modelPath, SurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Check(request);

        var probe = FieldProbe.Open(modelPath);
        var model = probe.Model;
        var domain = probe.DomainMm();
        var twoD = model.Fields.Any(f => f.Solve is not null) && !model.Fields.Any(f => f.Solve3D is not null);

        var plane = request.Plane ?? DefaultPlane(domain, twoD);
        var (a, b, n) = Axes(plane);

        var centre = domain is { } d ? Component(Mid(d.Min, d.Max), n) : 0.0;
        var offset = request.OffsetMm ?? (twoD ? 0.0 : centre);

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
                "/surface/box-mm",
                "this model has no solved domain to take the sampled rectangle from (every element "
                + "is analytic and fills all space); give --box-mm with the rectangle in the plane");
        }

        if (!(a1 > a0) || !(b1 > b0))
        {
            throw Refusal("/surface/box-mm", "the rectangle must have a positive extent along both axes");
        }

        // The longer side runs across the picture, which is how an instrument is drawn.
        var swap = (b1 - b0) > (a1 - a0);
        var (across, deep) = swap ? (b, a) : (a, b);
        var (u0, u1, v0, v1) = swap ? (b0, b1, a0, a1) : (a0, a1, b0, b1);

        var nu = request.Samples;
        var step = (u1 - u0) / (nu - 1);
        var nv = Math.Max(2, Math.Min(MaximumSamples, (int)Math.Round((v1 - v0) / step) + 1));

        var us = Grid(u0, u1, nu);
        var vs = Grid(v0, v1, nv);

        var energy = request.Height == SurfaceHeight.Energy;
        var charge = model.ChargeSi / ElementaryCharge;
        var factor = energy ? charge : 1.0;

        var at = (request.AtUs ?? 0.0) * 1e-6;
        var cycleMean = request.CycleMean && probe.TimeVarying;

        var raw = new double[nu * nv];
        var inside = new string?[nu * nv];
        var depth = new double[nu * nv];
        var outside = 0;
        var refused = 0;

        for (var i = 0; i < nu; i++)
        {
            for (var j = 0; j < nv; j++)
            {
                var k = (i * nv) + j;
                var point = Point(across, deep, n, us[i], vs[j], offset);
                var sample = probe.At(k, point, at, cycleMean);

                raw[k] = sample.PotentialV ?? double.NaN;

                if (sample.Status == ProbeStatus.Refused)
                {
                    refused++;
                }
                else if (sample.Status == ProbeStatus.OutsideDomain)
                {
                    outside++;
                }
                else if (sample.Status == ProbeStatus.InsideConductor)
                {
                    inside[k] = sample.Conductor ?? "conductor";
                    depth[k] = sample.ConductorDistanceMm ?? 0.0;
                }
            }
        }

        // A plateau at the conductor's own potential, from the samples deep inside it. On a solved
        // element every node inside is pinned to the electrode, so that is the electrode's
        // potential exactly; nearer the surface an interpolant overshoots by a per cent or two by
        // construction, which is why the deep samples and a median rather than an average.
        var plateaus = new List<SurfacePlateau>();
        var level = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var group in Enumerable.Range(0, raw.Length)
            .Where(k => inside[k] is not null && double.IsFinite(raw[k]))
            .GroupBy(k => inside[k]!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // The deeper half, by signed distance, and its median value: deepest alone fails on a
            // conductor written as two halves meeting on a line, where a plane beside the seam is
            // never more than its offset deep and the "deepest" sample is any of them, including
            // one beside a face where the interpolant overshoots.
            var ordered = group.OrderBy(k => depth[k]).ToList();
            var deeper = ordered.Take(Math.Max(1, ordered.Count / 2)).Select(k => raw[k]).Order().ToList();
            var potential = deeper[deeper.Count / 2];
            level[group.Key] = potential;

            plateaus.Add(new SurfacePlateau(
                group.Key,
                potential,
                factor * potential,
                group.Count(),
                group.Min(k => raw[k]),
                group.Max(k => raw[k])));
        }

        var values = new double[raw.Length];

        for (var k = 0; k < raw.Length; k++)
        {
            var v = inside[k] is { } name && level.TryGetValue(name, out var flat) ? flat : raw[k];
            values[k] = (factor * v) + 0.0;
        }

        var warnings = new List<ValidityWarning>(probe.Warnings);

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

        if (cycleMean)
        {
            var window = probe.CycleMeanWindowSeconds!.Value * 1e6;

            warnings.Add(probe.Monochromatic
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
        }

        if (energy && model.IsMixture)
        {
            warnings.Add(new ValidityWarning(
                "render.first-species",
                $"this model carries {model.Species.Count} populations; the potential energy is drawn "
                + $"for the first, charge {charge:G4} e",
                WarningSeverity.Provenance));
        }

        if (outside > 0)
        {
            warnings.Add(new ValidityWarning(
                "probe.outside-domain",
                $"{outside} of {raw.Length} surface point(s) lie outside every element's declared "
                + "domain: zero there is the model's answer, not a measurement of vacuum",
                WarningSeverity.Qualified));
        }

        if (refused > 0)
        {
            warnings.Add(new ValidityWarning(
                "probe.refused",
                $"{refused} of {raw.Length} surface point(s) were refused by the field (a singular "
                + "axis, say) and are left as holes in the surface",
                WarningSeverity.Qualified));
        }

        var surface = new PotentialSurface
        {
            Plane = plane,
            OffsetMm = offset,
            AcrossAxis = Name(across),
            DeepAxis = Name(deep),
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
        };

        return (surface, probe, warnings);
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
        return span > 0.0 ? request.Relief * longer / span : 1.0;
    }

    /// <summary>Composes the surface as layers, in model millimetres with height up.</summary>
    /// <param name="surface">The sampled surface.</param>
    /// <param name="mmPerUnit">The height scale.</param>
    /// <param name="meshLines">Mesh lines along the longer side.</param>
    /// <returns>The surface, the plateaus, and the wire mesh with the frame, in drawing order.</returns>
    /// <remarks>
    /// <para>
    /// The picture's own frame: x runs along the across axis, y is the drawn height (volts times
    /// the scale), z runs along the deep axis. Earth is at y = 0, the plane itself.
    /// </para>
    /// <para>
    /// <b>The surface writes depth and the mesh is tested against it with a bias</b>, so lines on
    /// the far side of a hill are hidden and lines on the near side are not lost to rounding.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PictureLayer> Compose(PotentialSurface surface, double mmPerUnit, int meshLines)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var nu = surface.Across.Count;
        var nv = surface.Deep.Count;
        var (low, high) = Range(surface);
        var span = Math.Max(Math.Abs(low), Math.Abs(high));

        (double R, double G, double B) Ramp(double value) =>
            ColorRamp.Diverging(span > 0.0 ? 0.5 + (0.5 * Math.Clamp(value / span, -1.0, 1.0)) : 0.5);

        double Y(int i, int j) => surface.At(i, j) * mmPerUnit;

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
                vertices[(3 * k) + 2] = surface.Deep[j];

                // Central differences, one-sided at the edges; a NaN neighbour contributes nothing.
                var il = Math.Max(0, i - 1);
                var ih = Math.Min(nu - 1, i + 1);
                var jl = Math.Max(0, j - 1);
                var jh = Math.Min(nv - 1, j + 1);

                var dydu = (Y(ih, j) - Y(il, j)) / (surface.Across[ih] - surface.Across[il]);
                var dydv = (Y(i, jh) - Y(i, jl)) / (surface.Deep[jh] - surface.Deep[jl]);

                dydu = double.IsFinite(dydu) ? dydu : 0.0;
                dydv = double.IsFinite(dydv) ? dydv : 0.0;

                normals[3 * k] = -dydu;
                normals[(3 * k) + 1] = 1.0;
                normals[(3 * k) + 2] = -dydv;

                var (r, g, b) = Ramp(surface.At(i, j));
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
        var flatNormals = new double[normals.Length];

        for (var k = 0; k < nu * nv; k++)
        {
            flatNormals[(3 * k) + 1] = 1.0;
        }

        var meshes = new List<PictureMesh> { new(vertices, normals, surfaceTriangles, 1.0, 1.0, 1.0, colors) };
        var plateaus = new List<PictureMesh>();

        foreach (var (name, triangles) in plateauTriangles.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var flat = surface.Plateaus.First(p => p.Conductor == name).Height;
            var (pr, pg, pb) = Plateau(Ramp(flat));
            plateaus.Add(new PictureMesh(vertices, flatNormals, triangles, pr, pg, pb));
        }

        meshes.AddRange(Skirts(surface, mmPerUnit, Ramp));

        var lines = WireMesh(surface, mmPerUnit, meshLines, Ramp);

        // The plane itself at earth, so a reader can see where zero is drawn.
        var (fr, fg, fb) = (0.55, 0.55, 0.58);
        var (a0, a1, d0, d1) = (surface.Across[0], surface.Across[^1], surface.Deep[0], surface.Deep[^1]);
        lines.Add(new PictureLine([a0, 0, d0, a1, 0, d0, a1, 0, d1, a0, 0, d1, a0, 0, d0], fr, fg, fb));

        return
        [
            new PictureLayer("surface", meshes, [], 1.0, WritesDepth: true),
            new PictureLayer("plateaus", plateaus, [], 1.0, WritesDepth: true),
            new PictureLayer("mesh", [], lines, 1.0, WritesDepth: false, DepthBias: 4e-3),
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
        string? csvPath = null)
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
        var hash = ContentHash.OfText(File.ReadAllText(absolute));

        var (surface, probe, warnings) = Sample(absolute, request);
        var mmPerUnit = Scale(surface, request);
        var layers = Compose(surface, mmPerUnit, request.MeshLines);

        var (low, high) = Range(surface);
        var bar = Bar(surface, mmPerUnit, angles, low, high);

        var points = layers.SelectMany(Points).Concat(scale ? bar.Points : []).ToList();
        var framing = Framing.Around(points, angles.Azimuth, angles.Elevation);
        var matrix = framing.Matrix((double)width / height);

        // Room for the scale's labels, which sit left of the bar: measured with a first framing,
        // as a point that far left of the bar in the picture's own screen direction, then fitted
        // again. The second fit is a little smaller, so the labels fit with room to spare.
        if (scale)
        {
            var textHeight = ScaleIndicator.TextHeightFor(width, height);
            var widest = new[] { low, high, 0.0 }.Concat(surface.Plateaus.Select(p => p.Height))
                .Max(v => StrokeFont.Width($"{Format(v, high - low)} {surface.Unit}", textHeight));
            var widestCaption = Math.Max(StrokeFont.Width("cycle mean", textHeight), widest);

            var across = Math.Sqrt((matrix[0] * matrix[0]) + (matrix[4] * matrix[4]) + (matrix[8] * matrix[8]));
            var pxPerMm = across * width / 2.0;
            var reach = (widestCaption + (1.6 * textHeight) + 8.0) / pxPerMm;
            var (lx, ly, lz) = (-matrix[0] / across, -matrix[4] / across, -matrix[8] / across);

            foreach (var (x, y, z) in bar.Points)
            {
                points.Add((x + (lx * reach), y + (ly * reach), z + (lz * reach)));
            }

            // And above the bar, for the caption naming the instant.
            var up = Math.Sqrt((matrix[1] * matrix[1]) + (matrix[5] * matrix[5]) + (matrix[9] * matrix[9]));
            var lift = 3.0 * textHeight / (up * height / 2.0);
            var (tx, ty, tz) = bar.Points[1];
            points.Add((tx + (matrix[1] / up * lift), ty + (matrix[5] / up * lift), tz + (matrix[9] / up * lift)));

            framing = Framing.Around(points, angles.Azimuth, angles.Elevation);
            matrix = framing.Matrix((double)width / height);
        }

        var tainted = warnings.Any(w => w.Severity == WarningSeverity.ValidityViolation);
        var cycleMean = request.CycleMean && probe.TimeVarying;
        var instantUs = probe.TimeVarying ? request.AtUs ?? 0.0 : (double?)null;

        var overlay = scale
            ? Overlay(surface, bar, matrix, width, height, low, high, mmPerUnit, cycleMean, instantUs)
            : null;

        var rgb = Rasterizer.Draw(
            [.. layers.Select(StillCommand.Raster)],
            matrix,
            new RasterLighting(Brightness, 1.0),
            ColorRamp.Ground,
            width,
            height,
            lineWidth: 1.2,
            overlay: overlay);

        if (tainted)
        {
            Rasterizer.Hatch(rgb, width, height);
        }

        var path = outputPath is { } named
            ? Path.GetFullPath(named)
            : Path.Combine(project.Figures, Path.GetFileNameWithoutExtension(absolute) + ".surface.png");

        var windowUs = cycleMean ? probe.CycleMeanWindowSeconds * 1e6 : null;

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
            MeshLines = [MeshIndices(surface.Across.Count, Stride(surface, request.MeshLines)).Count,
                MeshIndices(surface.Deep.Count, Stride(surface, request.MeshLines)).Count],
            Height = surface.Height,
            Unit = surface.Unit,
            ChargeNumber = surface.ChargeNumber,
            HeightMmPerUnit = mmPerUnit,
            UnitsPerMm = 1.0 / mmPerUnit,
            Lowest = low,
            Highest = high,
            TimeVarying = probe.TimeVarying,
            InstantUs = instantUs,
            CycleMean = cycleMean,
            CycleMeanWindowUs = windowUs,
            Plateaus = surface.Plateaus,
            Tainted = tainted,
            ScaleDrawn = overlay is not null,
            Warnings = warningJson,
        };

        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, PngWriter.Write(rgb, width, height, Provenance(outcome, warnings)));

            if (csvPath is not null)
            {
                var csv = Path.GetFullPath(csvPath);
                Directory.CreateDirectory(Path.GetDirectoryName(csv)!);
                File.WriteAllText(csv, ToCsv(surface, warnings));
            }
        }

        return outcome;
    }

    /// <summary>The sampled surface as CSV: one row per node, the unit in each column's name.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="warnings">What qualifies it, written as comment lines above the table (GRD-2).</param>
    /// <returns>The CSV text.</returns>
    /// <remarks>
    /// The numbers behind the picture, so a well depth read off it can be read off the data
    /// instead. A node inside a conductor carries the plateau's value and the conductor's name.
    /// </remarks>
    public static string ToCsv(PotentialSurface surface, IReadOnlyList<ValidityWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(warnings);

        var invariant = CultureInfo.InvariantCulture;
        var text = new System.Text.StringBuilder();

        text.Append(invariant, $"# {surface.Height} on the {surface.Plane} plane at {surface.OffsetMm:G17} mm\n");

        foreach (var warning in warnings)
        {
            text.Append(invariant, $"# {warning.Severity}: {warning.Code}: {warning.Message}\n");
        }

        text.Append(invariant, $"{surface.AcrossAxis}_mm,{surface.DeepAxis}_mm,{(surface.Unit == "V" ? "potential_V" : "potential_energy_eV")},conductor\n");

        for (var i = 0; i < surface.Across.Count; i++)
        {
            for (var j = 0; j < surface.Deep.Count; j++)
            {
                var value = surface.At(i, j);
                var cell = double.IsFinite(value) ? value.ToString("G17", invariant) : string.Empty;
                text.Append(invariant, $"{surface.Across[i]:G17},{surface.Deep[j]:G17},{cell},{surface.Inside[(i * surface.Deep.Count) + j]}\n");
            }
        }

        return text.ToString();
    }

    /// <summary>A height in words: three significant figures, fixed notation, no trailing zeros.</summary>
    /// <param name="value">The value.</param>
    /// <param name="span">
    /// The range the value is read against, or zero to use the value's own size: labels on one
    /// scale share a resolution, so a rounding residue of -0.0002 V beside a 20 V range reads 0.
    /// </param>
    /// <returns>Its text, using only characters the stroke font draws.</returns>
    public static string Format(double value, double span = 0.0)
    {
        if (value == 0.0 || !double.IsFinite(value))
        {
            return "0";
        }

        var magnitude = span > 0.0 && double.IsFinite(span) ? span : Math.Abs(value);
        var decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(magnitude)), 0, 6);
        var text = Math.Round(value, decimals).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text == "-0" ? "0" : text;
    }

    /// <summary>The lowest and highest finite values on a surface.</summary>
    private static (double Low, double High) Range(PotentialSurface surface)
    {
        var finite = surface.Values.Where(double.IsFinite).ToList();

        return finite.Count == 0 ? (0.0, 0.0) : (finite.Min(), finite.Max());
    }

    private static int Stride(PotentialSurface surface, int meshLines) =>
        Math.Max(1, (int)Math.Round((Math.Max(surface.Across.Count, surface.Deep.Count) - 1) / (double)Math.Max(1, meshLines)));

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
    private static List<PictureLine> WireMesh(
        PotentialSurface surface,
        double mmPerUnit,
        int meshLines,
        Func<double, (double R, double G, double B)> ramp)
    {
        var nu = surface.Across.Count;
        var nv = surface.Deep.Count;
        var stride = Stride(surface, meshLines);
        var lines = new List<PictureLine>();

        void Segment(int i0, int j0, int i1, int j1)
        {
            var va = surface.At(i0, j0);
            var vb = surface.At(i1, j1);

            if (!double.IsFinite(va) || !double.IsFinite(vb))
            {
                return;
            }

            var na = surface.Inside[(i0 * nv) + j0];
            var nb = surface.Inside[(i1 * nv) + j1];

            if (na is not null && na == nb)
            {
                return;
            }

            var (r, g, b) = ramp(0.5 * (va + vb));

            lines.Add(new PictureLine(
                [surface.Across[i0], va * mmPerUnit, surface.Deep[j0], surface.Across[i1], vb * mmPerUnit, surface.Deep[j1]],
                r * 0.8,
                g * 0.8,
                b * 0.8));
        }

        foreach (var j in MeshIndices(nv, stride))
        {
            for (var i = 0; i + 1 < nu; i++)
            {
                Segment(i, j, i + 1, j);
            }
        }

        foreach (var i in MeshIndices(nu, stride))
        {
            for (var j = 0; j + 1 < nv; j++)
            {
                Segment(i, j, i, j + 1);
            }
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
                var (va, vb) = (surface.At(i0, j0), surface.At(i1, j1));

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
                    vertices.AddRange([surface.Across[i], top ? surface.At(i, j) * mmPerUnit : 0.0, surface.Deep[j]]);
                    normals.AddRange([nx, 0.0, nz]);
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
        var (a0, a1, d0, d1) = (surface.Across[0], surface.Across[^1], surface.Deep[0], surface.Deep[^1]);
        var corners = new[] { (a0, d0), (a0, d1), (a1, d0), (a1, d1) };

        // Leftmost on screen, measured with a framing at the picture's own angles.
        var probe = Framing.Around(corners.Select(c => (c.Item1, 0.0, c.Item2)), angles.Azimuth, angles.Elevation)
            .Matrix(1.0);

        var (x, z) = corners.MinBy(c => (probe[0] * c.Item1) + (probe[8] * c.Item2));

        // Set out from the corner, so the bar does not stand on the surface's own edge.
        var gap = 0.06 * Math.Max(a1 - a0, d1 - d0);
        x += x == a0 ? -gap : gap;

        var bottom = Math.Min(low, 0.0) * mmPerUnit;
        var top = Math.Max(high, 0.0) * mmPerUnit;

        return (x, z, [(x, bottom, z), (x, top, z)]);
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
        double? instantUs)
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
        var placed = new List<double>();

        void Tick(double value, bool force)
        {
            var (px, py) = Pixel(bar.X, value * mmPerUnit, bar.Z);

            if (!force && placed.Any(p => Math.Abs(p - py) < 1.5 * textHeight))
            {
                return;
            }

            placed.Add(py);
            strokes.Add(new OverlayStroke([px - tick, py, px, py], ir, ig, ib));

            var label = $"{Format(value, high - low)} {surface.Unit}";
            var labelWidth = StrokeFont.Width(label, textHeight);
            texts.Add(new OverlayText(label, px - tick - (0.4 * textHeight) - (0.5 * labelWidth), py, textHeight, ir, ig, ib));
        }

        Tick(Math.Max(high, 0.0), force: true);
        Tick(Math.Min(low, 0.0), force: true);

        if (low < 0.0 && high > 0.0)
        {
            Tick(0.0, force: false);
        }

        foreach (var plateau in surface.Plateaus.OrderByDescending(p => p.Samples))
        {
            Tick(plateau.Height, force: false);
        }

        // What instant the heights are, above the bar: a picture of a driven field is a frame.
        var caption = cycleMean ? "cycle mean" : instantUs is { } t ? $"t={Format(t)} µs" : null;

        if (caption is not null)
        {
            var captionWidth = StrokeFont.Width(caption, textHeight);
            texts.Add(new OverlayText(
                caption, Math.Max(topPx.X, 0.5 * captionWidth + 2), topPx.Y - (1.8 * textHeight), textHeight, ir, ig, ib));
        }

        return new RasterOverlay(strokes, texts, ScaleIndicator.LineWidthPx);
    }

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
            ? $"the mean over one drive cycle ({outcome.CycleMeanWindowUs:G6} us window, operating point t = {outcome.InstantUs:G6} us)"
            : outcome.InstantUs is { } t ? $"the instant t = {t:G6} us" : "a static field";

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

        foreach (var plateau in outcome.Plateaus)
        {
            text.Add(($"Plateau {plateau.Conductor}", string.Create(
                invariant, $"{plateau.PotentialV:G6} V over {plateau.Samples} samples")));
        }

        for (var i = 0; i < warnings.Count; i++)
        {
            var w = warnings[i];
            text.Add(($"Warning {i + 1}", $"[{w.Severity}] {w.Code}: {w.Message}"));
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
        _ => throw Refusal("/surface/plane", $"the plane is xy, xz or yz; got '{plane}'"),
    };

    private static string Name(int axis) => axis switch { 0 => "x", 1 => "y", _ => "z" };

    private static double Component(Vec3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static Vec3 Mid(Vec3 a, Vec3 b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);

    private static Vec3 Point(int across, int deep, int normal, double u, double v, double offset)
    {
        var c = new double[3];
        c[across] = u;
        c[deep] = v;
        c[normal] = offset;

        return new Vec3(c[0], c[1], c[2]);
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
        if (request.Plane is { } plane && plane is not ("xy" or "xz" or "yz"))
        {
            throw Refusal("/surface/plane", $"the plane is xy, xz or yz; got '{plane}'");
        }

        if (request.OffsetMm is { } offset && !double.IsFinite(offset))
        {
            throw Refusal("/surface/offset-mm", "the offset must be a finite number of millimetres");
        }

        if (request.BoxMm is { } box && (box.Count != 4 || box.Any(v => !double.IsFinite(v))))
        {
            throw Refusal(
                "/surface/box-mm",
                "the rectangle is four finite numbers in millimetres, in the plane's own axis order (for xz: x0,z0,x1,z1)");
        }

        if (request.Samples < 3 || request.Samples > MaximumSamples)
        {
            throw Refusal("/surface/samples", $"samples along the longer side are 3 to {MaximumSamples}; got {request.Samples}");
        }

        if (request.MeshLines < 1)
        {
            throw Refusal("/surface/mesh-lines", $"at least one mesh line; got {request.MeshLines}");
        }

        if (request.Height is not (SurfaceHeight.Potential or SurfaceHeight.Energy))
        {
            throw Refusal("/surface/height", $"the height is potential or energy; got '{request.Height}'");
        }

        if (!(request.Relief > 0.0) || !double.IsFinite(request.Relief))
        {
            throw Refusal("/surface/relief", "the relief is a positive fraction of the longer side");
        }

        if (request.HeightMmPerUnit is { } scale && (!(scale > 0.0) || !double.IsFinite(scale)))
        {
            throw Refusal("/surface/height-mm-per-v", "the height scale must be a positive, finite number of millimetres per unit");
        }

        if (request.AtUs is { } instant && !double.IsFinite(instant))
        {
            throw Refusal("/surface/at-us", "the instant must be a finite number of microseconds");
        }
    }

    private static EinzelException Refusal(string path, string constraint) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = "see 'einzel render surface' for the flags",
        });
}
