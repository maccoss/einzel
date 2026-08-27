using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Render;

/// <summary>
/// Draws a plane through an instrument: conductors, equipotentials, and the path
/// an ion takes through them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here knows what a mirror or a quadrupole is (architecture invariant 2).
/// A conductor is drawn as the <em>zero level set of its own signed distance</em>,
/// which the model format already requires every electrode to supply because the
/// solver and the absorber both need it. So a rod, a plate, a ring and a sphere
/// are all drawn by one routine, and a shape added to the format needs no change
/// in this assembly.
/// </para>
/// <para>
/// Headless by construction, per RND-1: there is no drawing surface, no font
/// measurement, and no display. The output is a list of paths and text runs, which
/// is why this can produce a publication figure in CI on Linux with nothing
/// attached.
/// </para>
/// </remarks>
public static class SectionRenderer
{
    /// <summary>Ink colours, kept together so a figure reads as one drawing.</summary>
    private const string Ink = "#1a1a1a";
    private const string ConductorFill = "#c9ccd1";
    private const string ConductorEdge = "#4a4f57";
    private const string Equipotential = "#7a8390";
    private const string TrajectoryInk = "#b3452a";
    private const string TaintInk = "#a8321e";

    /// <summary>A rendered figure, and what it does not claim.</summary>
    /// <param name="Scene">The figure.</param>
    /// <param name="Warnings">Warnings carried onto the drawing, per GRD-2.</param>
    /// <param name="DecimationToleranceMm">
    /// The bound every decimated polyline in the figure respects, in page millimetres.
    /// </param>
    /// <param name="TrajectoryPoints">Points the trajectory kept after decimation.</param>
    /// <param name="TrajectoryPointsBeforeDecimation">Points it had before.</param>
    public sealed record Figure(
        Scene Scene,
        IReadOnlyList<ValidityWarning> Warnings,
        double DecimationToleranceMm,
        int TrajectoryPoints,
        int TrajectoryPointsBeforeDecimation);

    /// <summary>Renders a section of a model.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="spec">What to draw.</param>
    /// <param name="provenance">Lines to record in the output and stamp on the page.</param>
    /// <returns>The figure.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The model has no extent to draw.</exception>
    public static Figure Render(
        CompiledModel model, RenderSpec spec, IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(spec);

        // Reported, not bare: a field that stopped short of its tolerance must taint
        // the picture drawn from it as surely as it taints a number (GRD-2). A
        // figure is the artifact most likely to be shown with none of the
        // uncertainty apparatus attached, which is RND-10's argument for video and
        // applies here for the same reason.
        var (field, warnings) = FieldAssembly.BuildReported(model);

        var plane = PlaneFor(model, spec);
        var (minU, minV, maxU, maxV) = Extent(model, plane);

        var spanU = maxU - minU;
        var spanV = maxV - minV;

        if (!(spanU > 0.0) || !(spanV > 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(model), "this model has no extent in the section plane to draw");
        }

        var drawWidth = spec.WidthMm - (2.0 * spec.MarginMm);
        var scale = drawWidth / spanU;
        var drawHeight = spanV * scale;

        var captionRoom = (spec.Caption is null ? 0.0 : 5.0) + 9.0;
        var pageHeight = drawHeight + (2.0 * spec.MarginMm) + captionRoom;

        // ACC-7: a fraction of the drawing's extent, converted once into the page
        // units every polyline is decimated in.
        var tolerance = spec.DecimationFraction * Math.Max(drawWidth, drawHeight);

        PagePoint ToPage(double u, double v) => new(
            spec.MarginMm + ((u - minU) * scale),
            spec.MarginMm + ((maxV - v) * scale));

        var paths = new List<ScenePath>();
        var texts = new List<SceneText>();

        var equipotentialStyle = new PathStyle(Equipotential, 0.13, Dash: DashStyle.Solid, Opacity: 0.85);

        if (spec.Equipotentials > 0)
        {
            DrawEquipotentials(
                paths, field, plane, spec, minU, minV, spanU, spanV, tolerance, ToPage, equipotentialStyle);
        }

        DrawConductors(paths, model, plane, spec, minU, minV, spanU, spanV, tolerance, ToPage);

        var kept = 0;
        var raw = 0;

        // RND-8 and TRN-2: a diffusive region emits a density field, and there are no
        // trajectories in it to draw. Lines through a funnel would depict something
        // the model never computed - which is a stronger objection than inaccuracy,
        // because a reader cannot tell an invented line from a computed one.
        //
        // Asked of the transport mode rather than inferred from the pressure, so the
        // rule holds wherever a mode says it produces no trajectories.
        var mode = Transport.TransportModes.All.FirstOrDefault(
            m => string.Equals(m.Name, model.TransportMode, StringComparison.OrdinalIgnoreCase));

        var drawable = mode?.ProducesTrajectories ?? true;

        if (spec.Trajectory && drawable)
        {
            (kept, raw) = DrawTrajectory(paths, model, spec, field, plane, tolerance, ToPage);
        }
        else if (spec.Trajectory)
        {
            warnings =
            [
                .. warnings,
                new ValidityWarning(
                    "render.no-trajectories",
                    $"this model declares '{model.TransportMode}' transport, which computes a "
                    + "density rather than trajectories, so none were drawn. RND-8 forbids drawing "
                    + "lines through a diffusive region: they would depict something the model "
                    + "never produced",
                    WarningSeverity.Provenance),
            ];
        }

        DrawFrame(paths, texts, spec, minU, maxU, minV, maxV, scale, drawWidth, drawHeight);

        var lines = new List<string>(provenance ?? []);
        lines.Add($"decimation tolerance {tolerance:G3} mm of a {drawWidth:F1} by {drawHeight:F1} mm drawing");

        foreach (var warning in warnings)
        {
            lines.Add($"WARNING {warning.Code}: {warning.Message}");
        }

        DrawStamp(paths, texts, spec, pageHeight, warnings, tolerance);

        if (spec.Caption is { } caption)
        {
            texts.Add(new SceneText(
                caption,
                new PagePoint(spec.MarginMm, spec.MarginMm + drawHeight + 6.0),
                7.5,
                TextAnchor.Start,
                Ink,
                "caption"));
        }

        var scene = new Scene(spec.WidthMm, pageHeight, paths, texts, lines);

        return new Figure(scene, warnings, tolerance, kept, raw);
    }

    private static SectionPlane PlaneFor(CompiledModel model, RenderSpec spec)
    {
        if (spec.Plane is { Normal.Count: 3 } declared)
        {
            var normal = new Vec3(declared.Normal[0], declared.Normal[1], declared.Normal[2]);

            var across = declared.AcrossMm is { Count: 3 } a
                ? new Vec3(a[0], a[1], a[2])
                : (Vec3?)null;

            return new SectionPlane(normal * (declared.OffsetMm * 1e-3), normal, across);
        }

        // A two-dimensional solve is invariant along z, so its own plane is the
        // section and no cut has to be chosen. Nothing here needs to know that a
        // model is two-dimensional other than that its field does not vary along z.
        var flat = model.Fields.All(f => f.Solve3D is null);

        return flat
            ? new SectionPlane(Vec3.Zero, new Vec3(0.0, 0.0, 1.0), new Vec3(1.0, 0.0, 0.0))
            : new SectionPlane(Vec3.Zero, new Vec3(0.0, 1.0, 0.0), new Vec3(0.0, 0.0, 1.0));
    }

    private static (double MinU, double MinV, double MaxU, double MaxV) Extent(
        CompiledModel model, SectionPlane plane)
    {
        var minU = double.PositiveInfinity;
        var minV = double.PositiveInfinity;
        var maxU = double.NegativeInfinity;
        var maxV = double.NegativeInfinity;

        void Include(Vec3 point)
        {
            var (u, v) = plane.Project(point);

            minU = Math.Min(minU, u);
            maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v);
            maxV = Math.Max(maxV, v);
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } flat)
            {
                // A cylindrical solve is a half-plane in (axial, radial), and a
                // section through the axis of one shows both halves - a ring is two
                // conductors on the page, not one. SYM-1, declared on the solve, so
                // this is symmetry knowledge rather than device knowledge; the
                // renderer still has no idea what the rings are for.
                var mirrored = flat.Symmetry == SolveSymmetry.Cylindrical;

                foreach (var x in new[] { flat.MinX, flat.MaxX })
                {
                    foreach (var y in mirrored
                        ? [-flat.MaxY, flat.MaxY]
                        : new[] { flat.MinY, flat.MaxY })
                    {
                        Include(new Vec3(x, y, 0.0));
                    }
                }
            }

            if (element.Solve3D is { } volume)
            {
                foreach (var x in new[] { volume.MinX, volume.MaxX })
                {
                    foreach (var y in new[] { volume.MinY, volume.MaxY })
                    {
                        foreach (var z in new[] { volume.MinZ, volume.MaxZ })
                        {
                            Include(new Vec3(x, y, z));
                        }
                    }
                }
            }
        }

        // An analytic model has no declared domain, so the instrument's own points
        // are the extent: where the ion starts and where it is caught.
        if (double.IsInfinity(minU))
        {
            Include(model.SourcePosition);
            Include(model.DetectorPoint);

            var pad = 0.1 * Math.Max(1e-3, (model.DetectorPoint - model.SourcePosition).Length);

            minU -= pad;
            maxU += pad;
            minV -= pad;
            maxV += pad;
        }

        // A section exactly on the axis of a symmetric instrument has no height at
        // all. Give it one rather than dividing by zero later.
        if (maxV - minV < 1e-12)
        {
            var pad = 0.1 * Math.Max(1e-6, maxU - minU);

            minV -= pad;
            maxV += pad;
        }

        return (minU, minV, maxU, maxV);
    }

    private static void DrawEquipotentials(
        List<ScenePath> paths,
        IElectrostaticField field,
        SectionPlane plane,
        RenderSpec spec,
        double minU,
        double minV,
        double spanU,
        double spanV,
        double tolerance,
        Func<double, double, PagePoint> toPage,
        PathStyle style)
    {
        var columns = Math.Max(8, spec.SampleColumns);
        var rows = Math.Max(8, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var values = Contours.Sample(
            plane, minU, minV, stepU, stepV, columns, rows, point => field.PotentialAt(in point));

        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
            {
                continue;
            }

            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        if (!double.IsFinite(low) || high - low <= 0.0)
        {
            return;
        }

        for (var k = 1; k <= spec.Equipotentials; k++)
        {
            var level = low + ((high - low) * k / (spec.Equipotentials + 1.0));

            foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, level))
            {
                Emit(paths, run, tolerance, toPage, style, "equipotentials");
            }
        }
    }

    private static void DrawConductors(
        List<ScenePath> paths,
        CompiledModel model,
        SectionPlane plane,
        RenderSpec spec,
        double minU,
        double minV,
        double spanU,
        double spanV,
        double tolerance,
        Func<double, double, PagePoint> toPage)
    {
        // Finer than the equipotentials, because a conductor edge is a hard line and
        // the eye reads a polygonal circle immediately where it forgives a polygonal
        // equipotential.
        var columns = Math.Max(16, (int)(spec.SampleColumns * 1.5));
        var rows = Math.Max(16, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var fill = new PathStyle(ConductorEdge, 0.18, ConductorFill);

        foreach (var element in model.Fields)
        {
            // The half-plane of a cylindrical solve is mirrored about the axis, so a
            // ring drawn from its own signed distance appears on both sides. The
            // field needs no such treatment: a cylindrical solve is already wrapped
            // as an axisymmetric field, which samples at the radius of the point it
            // is asked about and so answers for a negative one already.
            var mirrored = element.Solve?.Symmetry == SolveSymmetry.Cylindrical;

            foreach (var electrode in element.Solve?.Electrodes ?? [])
            {
                var values = Contours.Sample(
                    plane, minU, minV, stepU, stepV, columns, rows,
                    point => electrode.SignedDistance(
                        point.X, mirrored ? Math.Abs(point.Y) : point.Y));

                foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, 0.0))
                {
                    Emit(paths, run, tolerance, toPage, fill, "conductors");
                }
            }

            foreach (var electrode in element.Solve3D?.Electrodes ?? [])
            {
                var values = Contours.Sample(
                    plane, minU, minV, stepU, stepV, columns, rows,
                    point => electrode.SignedDistance(point.X, point.Y, point.Z));

                foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, 0.0))
                {
                    Emit(paths, run, tolerance, toPage, fill, "conductors");
                }
            }
        }
    }

    private static (int Kept, int Raw) DrawTrajectory(
        List<ScenePath> paths,
        CompiledModel model,
        RenderSpec spec,
        IElectrostaticField field,
        SectionPlane plane,
        double tolerance,
        Func<double, double, PagePoint> toPage)
    {
        var species = IonSpecies.FromModel(model);
        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // Twice: once at the model's own cadence to learn how long the flight is,
        // then at a cadence chosen from that. The alternative - drawing whatever the
        // model happens to sample for its VTU export - gave the einzel lens a
        // three-segment curve through a focusing element, which is a drawing of the
        // sampling interval rather than of the optics.
        //
        // Sample finely and decimate to a stated bound is the whole shape of RND-5.
        // Sampling coarsely and drawing it undecimated loses the guarantee and the
        // curve at once.
        var scout = new TrajectoryRecorder(model.SampleIntervalSi);

        TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, scout);

        if (scout.Samples.Count < 2)
        {
            return (0, scout.Samples.Count);
        }

        var flight = scout.Samples[^1].TimeSeconds - scout.Samples[0].TimeSeconds;
        var samples = Math.Max(16, spec.TrajectorySamples);

        var recorder = flight > 0.0
            ? new TrajectoryRecorder(flight / samples, capacity: 4 * samples)
            : scout;

        if (!ReferenceEquals(recorder, scout))
        {
            TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, recorder);
        }

        if (recorder.Samples.Count < 2)
        {
            return (0, recorder.Samples.Count);
        }

        var page = new List<PagePoint>(recorder.Samples.Count);

        foreach (var sample in recorder.Samples)
        {
            var (u, v) = plane.Project(sample.Position);
            page.Add(toPage(u, v));
        }

        var reduced = Decimation.Reduce(page, tolerance);

        paths.Add(new ScenePath(
            reduced, false, new PathStyle(TrajectoryInk, 0.28), "trajectory"));

        return (reduced.Count, page.Count);
    }

    private static void DrawFrame(
        List<ScenePath> paths,
        List<SceneText> texts,
        RenderSpec spec,
        double minU,
        double maxU,
        double minV,
        double maxV,
        double scale,
        double drawWidth,
        double drawHeight)
    {
        var border = new PathStyle(Ink, 0.15);

        paths.Add(new ScenePath(
            [
                new PagePoint(spec.MarginMm, spec.MarginMm),
                new PagePoint(spec.MarginMm + drawWidth, spec.MarginMm),
                new PagePoint(spec.MarginMm + drawWidth, spec.MarginMm + drawHeight),
                new PagePoint(spec.MarginMm, spec.MarginMm + drawHeight),
            ],
            true,
            border,
            "frame"));

        if (spec.DrawAxis)
        {
            // The axis of rotation, which for an axisymmetric device is the line the
            // whole drawing is about and the line an on-axis ion runs along. Dashed,
            // because it is a construction line rather than a part.
            var axisY = spec.MarginMm + ((maxV - 0.0) * scale);

            if (axisY > spec.MarginMm && axisY < spec.MarginMm + drawHeight)
            {
                paths.Add(new ScenePath(
                    [
                        new PagePoint(spec.MarginMm, axisY),
                        new PagePoint(spec.MarginMm + drawWidth, axisY),
                    ],
                    false,
                    new PathStyle(Ink, 0.12, Dash: DashStyle.Dashed, Opacity: 0.55),
                    "axis"));
            }
        }

        if (!spec.ScaleBar)
        {
            return;
        }

        // A round number of millimetres that occupies roughly a fifth of the
        // drawing. A scale bar of 23.7 mm is arithmetically correct and useless.
        var wanted = 0.2 * (maxU - minU) * 1e3;
        var decade = Math.Pow(10.0, Math.Floor(Math.Log10(wanted)));
        var lengthMm = decade * (wanted / decade >= 5.0 ? 5.0 : wanted / decade >= 2.0 ? 2.0 : 1.0);

        var barPage = lengthMm * 1e-3 * scale;

        var y = spec.MarginMm + drawHeight - 4.0;
        var x = spec.MarginMm + 4.0;

        paths.Add(new ScenePath(
            [new PagePoint(x, y), new PagePoint(x + barPage, y)],
            false,
            new PathStyle(Ink, 0.4),
            "scale"));

        texts.Add(new SceneText(
            lengthMm >= 1.0 ? $"{lengthMm:G3} mm" : $"{lengthMm * 1e3:G3} um",
            new PagePoint(x + (0.5 * barPage), y - 1.2),
            6.5,
            TextAnchor.Middle,
            Ink,
            "scale"));
    }

    private static void DrawStamp(
        List<ScenePath> paths,
        List<SceneText> texts,
        RenderSpec spec,
        double pageHeight,
        IReadOnlyList<ValidityWarning> warnings,
        double tolerance)
    {
        var baseline = pageHeight - 3.5;

        texts.Add(new SceneText(
            $"decimated to {tolerance:G3} mm",
            new PagePoint(spec.WidthMm - spec.MarginMm, baseline),
            5.5,
            TextAnchor.End,
            "#6b7280",
            "provenance"));

        if (warnings.Count == 0)
        {
            return;
        }

        // RND-11 and GRD-5: a figure carrying a warning has to be visually
        // distinguishable, not merely annotated in a corner nobody reads. A hatched
        // rule the width of the page is hard to crop out by accident and survives
        // being pasted into a slide.
        var y = pageHeight - 8.0;

        for (var x = 0.0; x < spec.WidthMm; x += 3.0)
        {
            paths.Add(new ScenePath(
                [new PagePoint(x, y + 1.6), new PagePoint(Math.Min(x + 1.6, spec.WidthMm), y)],
                false,
                new PathStyle(TaintInk, 0.35),
                "taint"));
        }

        texts.Add(new SceneText(
            warnings.Count == 1
                ? $"QUALIFIED: {warnings[0].Code}"
                : $"QUALIFIED: {warnings.Count} warnings, first {warnings[0].Code}",
            new PagePoint(spec.MarginMm, baseline),
            5.5,
            TextAnchor.Start,
            TaintInk,
            "taint"));
    }

    private static void Emit(
        List<ScenePath> paths,
        Contours.Run run,
        double tolerance,
        Func<double, double, PagePoint> toPage,
        PathStyle style,
        string layer)
    {
        if (run.Points.Count < 2)
        {
            return;
        }

        var page = new List<PagePoint>(run.Points.Count);

        foreach (var (u, v) in run.Points)
        {
            page.Add(toPage(u, v));
        }

        var reduced = Decimation.Reduce(page, tolerance);

        if (reduced.Count < 2)
        {
            return;
        }

        paths.Add(new ScenePath(reduced, run.Closed, style, layer));
    }
}
