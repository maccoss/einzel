using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Render;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>One ion's path, as something can draw it.</summary>
/// <param name="PointsMm">The path, in millimetres, in order.</param>
/// <param name="EnergyEv">Kinetic energy at each point, in electronvolts.</param>
/// <param name="Fate">How it ended, named as the model author named the surface.</param>
/// <remarks>
/// Energy per point rather than per path, because §16 asks for trajectory bundles
/// coloured by energy - and an ion that has crossed a mirror twice has had several
/// energies, so one number per path would be a colour for a quantity that varied.
/// </remarks>
public sealed record TrajectoryPath(
    IReadOnlyList<IReadOnlyList<double>> PointsMm,
    IReadOnlyList<double> EnergyEv,
    string Fate);

/// <summary>One conductor, as a surface something can draw.</summary>
/// <param name="Name">The name the model author gave it.</param>
/// <param name="PotentialVolts">What it holds, in volts. The DC part where it is driven.</param>
/// <param name="DriveAmplitudeVolts">Its drive amplitude, or zero where it is static.</param>
/// <param name="VerticesMm">Positions, as consecutive x, y, z triples in millimetres.</param>
/// <param name="Normals">Outward unit normals, one triple per vertex.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <remarks>
/// <b>The DC potential and the drive amplitude both, because reading only the first is a
/// mistake this project has made five times.</b> A quadrupole's rods hold zero volts of DC
/// and all of their potential as drive; an electrode coloured by its DC alone would paint
/// a mass filter as an earthed box.
/// </remarks>
public sealed record ConductorSurface(
    string Name,
    double PotentialVolts,
    double DriveAmplitudeVolts,
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles);

/// <summary>Where a flight begins and where it is caught.</summary>
/// <param name="SourceMm">The launch point, as x, y, z in millimetres.</param>
/// <param name="LaunchDirection">Unit vector the ion sets off along.</param>
/// <param name="DetectorMm">A point on the detector plane, in millimetres.</param>
/// <param name="DetectorNormal">The plane normal, pointing back into the flight volume.</param>
/// <param name="SpanMm">
/// How large to draw them, in millimetres - a fraction of the instrument, so a marker is
/// visible on a 600 mm analyser and not overwhelming on a 20 mm lens.
/// </param>
/// <remarks>
/// <para>
/// <b>A drawing of an instrument with no beginning and no end is hard to read</b>, and this
/// analyser made it obvious: ions launch 10 mm into a 350 mm drift, reverse, and are caught
/// on a plane behind where they started. Nothing in the picture said which end was which,
/// so a viewer had to infer the direction of travel from the trajectory's own shape.
/// </para>
/// <para>
/// <b>Both are drawing conventions rather than dimensions</b> (GRD-12). A source is a point
/// and a detector is an unbounded plane; the sphere and the quad drawn for them have sizes
/// chosen to be visible, and <see cref="SpanMm"/> is that choice made explicit rather than
/// left in the shell where a reader could mistake it for the detector's extent.
/// </para>
/// </remarks>
public sealed record FlightEnds(
    IReadOnlyList<double> SourceMm,
    IReadOnlyList<double> LaunchDirection,
    IReadOnlyList<double> DetectorMm,
    IReadOnlyList<double> DetectorNormal,
    double SpanMm);

/// <summary>One shell of the density, at a stated level.</summary>
/// <param name="DensityPerCubicMetre">The level this surface sits at, in ions per m3.</param>
/// <param name="DecadesBelowPeak">
/// How far below the packet's peak it is, which is the number a reader actually uses.
/// </param>
/// <param name="VerticesMm">Consecutive x, y, z triples, in millimetres.</param>
/// <param name="Normals">One unit normal per vertex, outward.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <remarks>
/// <b>The level is on the record, not implied by the order</b> (GRD-12). A shell drawn
/// without the density it stands for is a shape rather than a measurement, and a reader
/// looking at three nested surfaces has no way to tell a packet spanning one decade from
/// one spanning six.
/// </remarks>
public sealed record DensityShell(
    double DensityPerCubicMetre,
    int DecadesBelowPeak,
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles);

/// <summary>One equipotential, as polylines on the section plane.</summary>
/// <param name="PotentialVolts">The level, in volts.</param>
/// <param name="PathsMm">Each path as consecutive x, y, z triples in millimetres.</param>
public sealed record Equipotential(
    double PotentialVolts,
    IReadOnlyList<IReadOnlyList<double>> PathsMm);

/// <summary>What the interactive viewport draws.</summary>
/// <param name="ModelPath">The model, as an absolute path.</param>
/// <param name="Trajectories">The paths, empty when the mode produces none.</param>
/// <param name="ProducesTrajectories">
/// Whether this model's transport mode produces trajectories at all (RND-8, TRN-2).
/// </param>
/// <param name="LowestEnergyEv">
/// The lowest kinetic energy anywhere in the bundle, or absent when there is no bundle.
/// </param>
/// <param name="HighestEnergyEv">The highest, likewise.</param>
/// <param name="Conductors">The electrodes, as surfaces.</param>
/// <param name="Equipotentials">Level sets of the potential on the section plane.</param>
/// <param name="LowestPotentialVolts">
/// The lowest potential anywhere on the section plane, or absent when there is no field.
/// </param>
/// <param name="HighestPotentialVolts">The highest, likewise.</param>
/// <param name="Density">
/// The density, as nested shells, for a mode that computes one rather than trajectories.
/// </param>
/// <param name="PeakDensityPerCubicMetre">
/// The peak the shells are measured from, or absent where there is no density.
/// </param>
/// <param name="DensityAtUs">
/// The instant the shells are the density at, or absent where there is no density.
/// </param>
/// <param name="Ends">Where the flight begins and where it is caught, or null when
/// the model has no flight to draw ends for.</param>
/// <param name="Warnings">What the viewport must show alongside (GRD-2).</param>
/// <remarks>
/// <para>
/// <b>The energy range is reported once for the whole bundle, and that is the point of
/// its being here at all.</b> §16 asks for trajectory bundles coloured by energy, and a
/// colour scale taken per path would give every ion the same colours whatever its energy
/// - so two ions a kilovolt apart would look identical and the picture would say they
/// were the same. The scale has to be anchored across everything being drawn.
/// </para>
/// <para>
/// The same failure the animation's contour levels had in the other axis: anchored per
/// frame, a film of a packet spreading showed a packet doing nothing.
/// </para>
/// </remarks>
public sealed record ViewportOutcome(
    string ModelPath,
    IReadOnlyList<TrajectoryPath> Trajectories,
    bool ProducesTrajectories,
    double? LowestEnergyEv,
    double? HighestEnergyEv,
    IReadOnlyList<ConductorSurface> Conductors,
    IReadOnlyList<Equipotential> Equipotentials,
    double? LowestPotentialVolts,
    double? HighestPotentialVolts,
    IReadOnlyList<DensityShell> Density,
    double? PeakDensityPerCubicMetre,
    double? DensityAtUs,
    FlightEnds? Ends,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>
/// The data an interactive viewport draws, for anything that needs it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The third time the window has needed something no command returned.</b> After the
/// model tree needed <c>outline</c>, and for the same reason: UI-1 puts file format
/// knowledge and physics outside the shell, so a viewport cannot fly its own ions any
/// more than a model tree can parse its own document.
/// </para>
/// <para>
/// <b>RND-8 is enforced here rather than trusted to the caller.</b> Above about 1e-2 mbar
/// the model computes a density and no trajectories exist; lines through a funnel then
/// depict something the model never computed. The renderer already asks the mode whether
/// it produces trajectories, and so does this - a viewport that asked the pressure
/// instead would be re-deriving a decision the transport mode already owns.
/// </para>
/// <para>
/// <b>Fly twice, sample for the display.</b> The model's own cadence is chosen for VTU
/// and gives a focusing element three segments; so the flight is scouted once to learn
/// how long it is, then re-flown at a cadence chosen from that. The same pattern the
/// section renderer uses, and for the same reason.
/// </para>
/// </remarks>
public static class ViewportCommand
{
    /// <summary>Reads what a viewport should draw.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="samplesPerPath">How finely to sample each path.</param>
    /// <param name="densityAtSeconds">
    /// The instant to draw a diffusive model's density at, or null to choose one. The
    /// end of a run is the wrong default: a packet whose ions arrived leaves nothing
    /// there, so a viewport anchored to it shows an empty box for every model that works.
    /// </param>
    /// <returns>The paths, or none with a reason.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static ViewportOutcome Execute(
        string modelPath, int samplesPerPath = 256, double? densityAtSeconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerPath, 2);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors[0]);
        }

        var model = validation.Model!;
        var warnings = new List<ValidityWarning>();

        // The field's own warnings ride out with the picture, because a viewport is a
        // number a person reads with their eyes: a bundle drawn through a field that
        // never converged looks exactly like one drawn through a field that did.
        var (field, built) = FieldAssembly.BuildReported(model);

        warnings.AddRange(built);

        var mode = TransportModes.All.FirstOrDefault(
            m => string.Equals(m.Name, model.TransportMode, StringComparison.Ordinal));

        if (!(mode?.ProducesTrajectories ?? true))
        {
            // RND-8: not an omission to be filled in later, a statement that there is
            // nothing of this kind to draw. A viewport that drew lines here would be
            // depicting something the model never computed.
            warnings.Add(new ValidityWarning(
                "render.no-trajectories",
                $"the '{model.TransportMode}' transport mode computes a density rather "
                + "than trajectories, so there are no paths to draw. What this model has "
                + "instead is a density field, drawn here as nested shells at decades "
                + "below its peak",
                WarningSeverity.Provenance));

            // The geometry is still drawn. RND-8 forbids trajectories through a
            // diffusive region, not the instrument they would have flown through - and a
            // funnel with no rings shown is a picture of nothing at all.
            var (conductors, low, high) = Geometry(model, field, warnings);

            var (shells, peak, at) = Cloud(model, field, warnings, densityAtSeconds);

            return new ViewportOutcome(
                absolute, [], false, null, null,
                conductors, Levels(model, field, low, high), low, high,
                shells, peak, at, Ends(model, []), warnings);
        }

        var species = IonSpecies.FromModel(model);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        var nominal = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var cloud = IonCloud.Draw(in nominal, species, model.Cloud, model.SourceDirection);
        var paths = new List<TrajectoryPath>(cloud.Length);

        foreach (var launch in cloud)
        {
            if (Fly(launch, species, field, settings, detector,
                    model.SampleIntervalSi, samplesPerPath) is { } path)
            {
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            warnings.Add(new ValidityWarning(
                "render.no-path",
                "no ion produced a path with two points in it, so there is nothing to "
                + "draw. An ion that fails at its first step has a position and no "
                + "trajectory",
                WarningSeverity.Provenance));
        }

        // Anchored over everything drawn, not per path. Absent rather than zero when
        // there is no bundle, because zero is a real energy and a reader cannot tell the
        // two apart if both print as zero.
        double? lowest = null;
        double? highest = null;

        foreach (var energy in paths.SelectMany(p => p.EnergyEv))
        {
            lowest = lowest is { } low ? Math.Min(low, energy) : energy;
            highest = highest is { } high ? Math.Max(high, energy) : energy;
        }

        var (surfaces, floor, ceiling) = Geometry(model, field, warnings, paths);

        return new ViewportOutcome(
            absolute, paths, true, lowest, highest,
            surfaces, Levels(model, field, floor, ceiling, paths), floor, ceiling,
            [], null, null, Ends(model, paths), warnings);
    }

    /// <summary>Where the flight begins and ends, with a size to draw them at.</summary>
    /// <remarks>
    /// <b>The span is a fraction of what is drawn, not a fixed length.</b> A marker sized in
    /// millimetres is invisible on a 600 mm analyser and swallows a 20 mm lens; taken from
    /// the extent, one number works for both. Measured over the trajectories where there are
    /// any and the geometry otherwise, which is the same extent the camera frames.
    /// </remarks>
    private static FlightEnds Ends(CompiledModel model, IReadOnlyList<TrajectoryPath> paths)
    {
        var low = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var high = new[] { double.MinValue, double.MinValue, double.MinValue };

        void Cover(double x, double y, double z)
        {
            Span<double> at = [x, y, z];

            for (var i = 0; i < 3; i++)
            {
                low[i] = Math.Min(low[i], at[i]);
                high[i] = Math.Max(high[i], at[i]);
            }
        }

        foreach (var point in paths.SelectMany(p => p.PointsMm))
        {
            Cover(point[0], point[1], point[2]);
        }

        Cover(model.SourcePosition.X * 1e3, model.SourcePosition.Y * 1e3, model.SourcePosition.Z * 1e3);
        Cover(model.DetectorPoint.X * 1e3, model.DetectorPoint.Y * 1e3, model.DetectorPoint.Z * 1e3);

        var extent = 0.0;

        for (var i = 0; i < 3; i++)
        {
            extent = Math.Max(extent, high[i] - low[i]);
        }

        // A fortieth of the instrument: large enough to find, small enough not to hide the
        // geometry it sits in. Floored so a degenerate extent still draws something.
        var span = Math.Max(extent / 40.0, 0.5);

        return new FlightEnds(
            [model.SourcePosition.X * 1e3, model.SourcePosition.Y * 1e3, model.SourcePosition.Z * 1e3],
            [model.SourceDirection.X, model.SourceDirection.Y, model.SourceDirection.Z],
            [model.DetectorPoint.X * 1e3, model.DetectorPoint.Y * 1e3, model.DetectorPoint.Z * 1e3],
            [model.DetectorNormal.X, model.DetectorNormal.Y, model.DetectorNormal.Z],
            span);
    }

    /// <summary>The density, as nested shells at decades below its peak.</summary>
    /// <remarks>
    /// <para>
    /// <b>The half of TRN-2 the window was missing.</b> RND-8 says never to draw
    /// trajectories through a diffusive region, which the viewport already honoured - and
    /// on its own that is entirely negative. The mode's principal result could be
    /// summarised into a transmission and a transit time and looked at in no other form,
    /// so the honest picture of a funnel at a millibar was an empty box. The 2-D section
    /// has drawn density contours since the mode was wired up; the viewport drew nothing,
    /// while its own warning said the density "is drawn as contours".
    /// </para>
    /// <para>
    /// <b>Decades, and the same rule the section uses</b>, because a density spans orders
    /// of magnitude - a packet's tail is a millionth of its core, not a small fraction of
    /// it - so evenly spaced shells draw the top decade several times over and the extent
    /// not at all.
    /// </para>
    /// <para>
    /// <b>Extruded or revolved by what the solve claims about the third dimension</b>,
    /// which is the rule the conductors already follow: a cross-section says the geometry
    /// repeats along z, an axisymmetric half-plane says it repeats all the way round. A
    /// density computed on a half-plane grid really is a solid of revolution, and drawing
    /// it flat would understate where the ions are by a whole dimension. The flag is taken
    /// from the density the solver produced rather than re-derived from the model, so the
    /// drawing cannot disagree with the thing it draws.
    /// </para>
    /// <para>
    /// <b>Drawn at an instant, and the default is not the end of the run.</b> A diffusive
    /// run reports the density it <em>ended</em> with, which for any model whose packet
    /// actually arrives is empty: the shipped drift tube collects 9,999.76 of 10,000 ions
    /// and leaves 1.8e-302 behind. Drawing that is drawing nothing, for exactly the models
    /// that work. The 2-D section learned this and gained an instant to draw at; this is
    /// the same lesson met a second time, caught by its own test rather than shipped.
    /// </para>
    /// <para>
    /// So the run is asked for snapshots across its flight and the drawing is taken from
    /// the <b>middle of those that hold a drawable density</b> - mid-flight while there is
    /// still a packet to see. The instant is reported rather than implied, because a
    /// density drawn without saying when is a shape rather than a measurement, and the
    /// caller may name its own to scrub through the flight.
    /// </para>
    /// <para>
    /// <b>Recording does not perturb what it records</b> - asserted where the snapshots
    /// were built, not assumed here - so asking for them costs the memory of the clones
    /// and nothing in the answer.
    /// </para>
    /// <para>
    /// <b>This runs the transport</b>, which a camera move must not. It is on the same
    /// explicit redraw the rest of the viewport is, and the cost is the run's - a number
    /// GRD-8 makes available before the work starts for exactly this mode.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<DensityShell> Shells, double? Peak, double? AtUs) Cloud(
        CompiledModel model,
        IElectrostaticField field,
        List<ValidityWarning> warnings,
        double? atSeconds)
    {
        // Spread across the declared flight rather than the elapsed one, which is not
        // known until the run is over. A run that stops early simply leaves the later
        // instants unfilled, and those are skipped below.
        var ceiling = model.MaximumFlightTimeSi;

        var wanted = atSeconds is { } named
            ? (IReadOnlyList<double>)[Math.Clamp(named, 0.0, ceiling)]
            : [.. Enumerable.Range(1, DensityInstants).Select(
                i => ceiling * i / (DensityInstants + 1.0))];

        DiffusiveOutcome run;

        try
        {
            run = DiffusionRun.Execute(model, field, [], snapshotSeconds: wanted);
        }
        catch (Core.Errors.EinzelException failure)
        {
            // A refusal is a thing to say, not a thing to draw nothing about. The
            // viewport is where somebody looks when they cannot tell why a model shows
            // them an empty box, so the reason has to arrive with the emptiness.
            warnings.Add(new ValidityWarning(
                "render.no-density",
                "this model computes a density but the transport refused, so there is "
                + $"nothing to draw: {failure.Error.Constraint}",
                WarningSeverity.Provenance));

            return ([], null, null);
        }

        warnings.AddRange(run.Warnings);

        var grid = run.Grid;

        var spanU = grid.MaxX - grid.OriginX;
        var spanV = grid.MaxY - grid.OriginY;
        var area = Math.Max(spanU * spanV, 1e-12);

        // Not merely positive: a run that collected everything leaves a residue orders
        // below one ion in the whole domain, and contouring that draws the shape of the
        // round-off. The same floor the section uses, for the same reason.
        static double Floor(Transport.Diffusion.DensityField field, double area) =>
            1e-6 * Math.Max(1.0, field.Population()) / area;

        // The middle of the instants that hold something, so the packet is as far along
        // as it can be while still being a packet. The final density is the fallback
        // rather than the default, which is the way round this was wrong first.
        var usable = run.Result.Snapshots
            .Where(snapshot => snapshot.Density.Peak() > Floor(snapshot.Density, area))
            .ToList();

        var chosen = usable.Count > 0 ? usable[usable.Count / 2] : null;

        var density = chosen?.Density ?? run.Result.Density;
        var atSecondsDrawn = chosen?.AtSeconds ?? run.Result.ElapsedSeconds;
        var peak = density.Peak();
        var floor = Floor(density, area);

        if (!(peak > floor))
        {
            warnings.Add(new ValidityWarning(
                "render.no-density",
                "no instant of this run holds a density worth drawing, so there are no "
                + "shells. A packet that has been collected or has left the grid is a "
                + "result rather than a failure - what it means is that the ions arrived",
                WarningSeverity.Provenance));

            return ([], peak, atSecondsDrawn * 1e6);
        }

        // GRD-12: the instant is part of the measurement. Three shells of a packet say
        // nothing at all without it, since a density that has spread for a microsecond
        // and one that has spread for a millisecond are the same three shells at
        // different sizes.
        warnings.Add(new ValidityWarning(
            "render.density-at-instant",
            $"the density is drawn at t = {atSecondsDrawn * 1e6:G6} us of a "
            + $"{run.Result.ElapsedSeconds * 1e6:G6} us run"
            + (atSeconds is null
                ? ", chosen as the middle of the instants that still hold a packet. The "
                  + "end of a run is empty whenever the ions arrived"
                : ", as asked for"),
            WarningSeverity.Provenance));

        var columns = Math.Max(8, DensityColumns);
        var rows = Math.Max(8, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        // [column, row] - u first, then v - which is the convention Contours.Sample
        // builds and Contours.Trace reads. Transposed, the contour comes out somewhere
        // the density is not, and every normal is exactly zero because the field being
        // differenced is flat there. That is what the unit-normal test caught.
        var values = new double[columns, rows];

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                values[column, row] = density.SampleAt(
                    grid.OriginX + (column * stepU), grid.OriginY + (row * stepV));
            }
        }

        var cylindrical = density.Cylindrical;
        var shells = new List<DensityShell>();

        for (var k = 1; k <= DensityShellCount; k++)
        {
            var level = peak * Math.Pow(10.0, -k);

            if (level <= floor)
            {
                break;
            }

            var traced = Contours.Trace(
                values, grid.OriginX, grid.OriginY, stepU, stepV, level);

            if (traced.Count == 0)
            {
                continue;
            }

            var mesh = Join(traced.Select(contour => cylindrical
                ? Surfaces.Revolve([.. contour.Points], RevolutionFacets)
                : Surfaces.Extrude([.. contour.Points], grid.OriginY, grid.MaxY)));

            if (mesh.Triangles.Count == 0)
            {
                continue;
            }

            // Outward means away from the denser side, so a shell is lit from the side a
            // viewer is on. Negative inside, which is the convention a conductor's own
            // signed distance already uses, so Orient needs no second rule.
            //
            // The step is the density's own cell, not the metre-scale one a conductor
            // uses. Orient differences the function it is given, and a signed distance
            // changes by the step itself while a density changes by whatever it happens
            // to change by - so a 1 um step across a packet spanning millimetres lands on
            // the rounding floor, the gradient comes back exactly zero, and Orient leaves
            // the normal at zero. That renders as a black facet, which reads as structure
            // in the density. Same shape as reading an analytic field's infinite
            // ResolutionLength as a differencing step.
            var oriented = Surfaces.Orient(
                mesh,
                cylindrical
                    ? (x, y, z) => level - density.SampleAt(x, Math.Sqrt((y * y) + (z * z)))
                    : (x, y, _) => level - density.SampleAt(x, y),
                0.5 * Math.Min(grid.SpacingX, grid.SpacingY));

            shells.Add(new DensityShell(
                level,
                k,
                [.. oriented.Vertices.Select(v => v * 1e3)],
                oriented.Normals,
                oriented.Triangles));
        }

        return (shells, peak, atSecondsDrawn * 1e6);
    }

    /// <summary>The box the drawing covers, in metres.</summary>
    /// <remarks>
    /// The union of every solve domain and the flight itself. The flight matters because
    /// an analytic model declares no domain at all - the scaffolded reflectron's only
    /// declared points are its source and its detector, which in a reflectron are the same
    /// point, and a page chosen from those alone put its turning point 105 metres off.
    /// </remarks>
    private static (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)?
        Extent(CompiledModel model, IReadOnlyList<TrajectoryPath> paths)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var any = false;

        void Cover(double x, double y, double z)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
            any = true;
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } plane)
            {
                // A cylindrical half-plane is a solid of revolution, so the instrument
                // reaches its outer radius in every transverse direction rather than only
                // in the positive half of one.
                var radius = plane.Symmetry == SolveSymmetry.Cylindrical
                    ? Math.Max(Math.Abs(plane.MinY), Math.Abs(plane.MaxY))
                    : 0.0;

                Cover(plane.MinX, Math.Min(plane.MinY, -radius), -radius);
                Cover(plane.MaxX, Math.Max(plane.MaxY, radius), radius);
            }

            if (element.Solve3D is { } volume)
            {
                Cover(volume.MinX, volume.MinY, volume.MinZ);
                Cover(volume.MaxX, volume.MaxY, volume.MaxZ);
            }
        }

        // A diffusive run has no paths and its region is the density grid, which is the
        // one thing that says where such a model reaches. Without it a drift tube in a
        // uniform field - no solve domain, no trajectories - has no extent at all, and
        // the viewport would draw the field over nothing.
        if (model.DensityGrid is { } density)
        {
            Cover(density.MinX, density.MinY, 0.0);
            Cover(density.MaxX, density.MaxY, 0.0);
        }

        foreach (var path in paths)
        {
            foreach (var point in path.PointsMm)
            {
                Cover(point[0] * 1e-3, point[1] * 1e-3, point[2] * 1e-3);
            }
        }

        if (!any)
        {
            return null;
        }

        // A degenerate axis is padded rather than left flat. An analytic reflectron flies
        // straight down x, so its transverse extent is exactly zero and the field would be
        // sampled on a grid one row deep - the equipotentials of such a model are vertical
        // lines, which is correct and informative, and there has to be somewhere to draw
        // them.
        var widest = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        var floor = 0.05 * widest;

        void Widen(ref double low, ref double high)
        {
            if (high - low >= floor)
            {
                return;
            }

            var middle = 0.5 * (low + high);

            low = middle - (0.5 * floor);
            high = middle + (0.5 * floor);
        }

        Widen(ref minX, ref maxX);
        Widen(ref minY, ref maxY);
        Widen(ref minZ, ref maxZ);

        return (minX, minY, minZ, maxX, maxY, maxZ);
    }

    /// <summary>The conductors, and the potential range they and the field cover.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every conductor is the zero level set of its own signed distance</b>, which is
    /// what keeps this free of device knowledge (architecture invariant 2). What differs
    /// between a cross-section, an axisymmetric half-plane and a volume is not the shape
    /// but what the solve claims about the third dimension - repeats along it, repeats
    /// round it, or says nothing - so the same contour becomes a prism, a solid of
    /// revolution, or a genuine extracted surface.
    /// </para>
    /// <para>
    /// The potential range is anchored across every conductor and the sampled field at
    /// once, for the same reason the energy scale is: a colour scale taken per electrode
    /// would paint a rod at +500 V and one at -500 V identically.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<ConductorSurface> Conductors, double? Low, double? High)
        Geometry(
            CompiledModel model,
            IElectrostaticField field,
            List<ValidityWarning> warnings,
            IReadOnlyList<TrajectoryPath>? paths = null)
    {
        var surfaces = new List<ConductorSurface>();
        double? low = null;
        double? high = null;

        // Once, not once per element: a bundle of four thousand ions is a million points
        // to walk, and every translational element would walk all of them again.
        var flight = Along(paths);

        void Cover(double potential)
        {
            low = low is { } l ? Math.Min(l, potential) : potential;
            high = high is { } h ? Math.Max(h, potential) : potential;
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } plane)
            {
                // A prism has to stop somewhere and the model does not say where: a
                // translational solve asserts the geometry is invariant along z, so the
                // conductor genuinely extends past anything drawn.
                //
                // Drawn as far as the ions go, because that is the part of an infinite
                // structure anybody is looking at - the invariant axis is the one the beam
                // travels along, so a quadrupole's rods should run the length of the
                // flight. The transverse span is the fallback for a model with no flight
                // to measure, and it is a poor one: it made the rods 32 mm of a 200 mm
                // instrument and put them in the corner of the picture.
                //
                // Either way it is a drawing convention and not a dimension, which is what
                // the warning says (GRD-12).
                var half = 0.5 * (plane.MaxY - plane.MinY);
                var (nearZ, farZ) = flight ?? (-half, half);

                if (plane.Symmetry == SolveSymmetry.Translational && plane.Electrodes.Count > 0)
                {
                    warnings.Add(new ValidityWarning(
                        "render.extruded-depth",
                        "this is a cross-section, so its electrodes have no declared extent "
                        + $"along z. They are drawn from {nearZ * 1e3:F1} to {farZ * 1e3:F1} mm, "
                        + (flight is null
                            ? "which is the transverse span of the solve domain because "
                                + "nothing was flown to measure"
                            : "which is as far as the ions reach")
                        + " - a drawing convention rather than a dimension of the instrument",
                        WarningSeverity.Provenance));
                }

                foreach (var electrode in plane.Electrodes)
                {
                    Cover(electrode.Potential);
                    Cover(electrode.Potential + electrode.DriveAmplitude);
                    Cover(electrode.Potential - electrode.DriveAmplitude);

                    var mesh = plane.Symmetry == SolveSymmetry.Cylindrical
                        ? Revolved(electrode, plane)
                        : Extruded(electrode, plane, nearZ, farZ);

                    if (mesh.TriangleCount > 0)
                    {
                        surfaces.Add(Surface(
                            electrode.Name, electrode.Potential, electrode.DriveAmplitude, mesh));
                    }
                }
            }

            if (element.Solve3D is { } volume)
            {
                foreach (var electrode in volume.Electrodes)
                {
                    Cover(electrode.Potential);
                    Cover(electrode.Potential + electrode.DriveAmplitude);
                    Cover(electrode.Potential - electrode.DriveAmplitude);

                    // The electrode's own box, not the solve domain. Extracting over the
                    // domain misses anything thinner than a cell - a 1 mm plate in a
                    // 60 mm box at 48 cells falls between lattice planes and produces no
                    // surface at all, silently. Padded so the surface is strictly inside
                    // and the extraction is not clipped by its own box.
                    var around = electrode.Bounds;

                    // Padded per axis by a fraction of THAT axis, not of the largest.
                    // A stripe 4 mm thick and 635 mm long was being padded by 8 per cent
                    // of 635 - inflating its thinnest direction twenty-six-fold, so the
                    // metal occupied a fortieth of the box being sampled and disappeared.
                    // The padding exists to keep the surface strictly inside the box, and
                    // for that each axis only needs a fraction of itself.
                    var padX = Pad(around.MaxX - around.MinX);
                    var padY = Pad(around.MaxY - around.MinY);
                    var padZ = Pad(around.MaxZ - around.MinZ);

                    var mesh = Surfaces.FromSignedDistance(
                        electrode.SignedDistance,
                        around.MinX - padX, around.MinY - padY, around.MinZ - padZ,
                        around.MaxX + padX, around.MaxY + padY, around.MaxZ + padZ,
                        VolumeCells);

                    if (mesh.TriangleCount > 0)
                    {
                        surfaces.Add(Surface(
                            electrode.Name, electrode.Potential, electrode.DriveAmplitude, mesh));
                    }
                }
            }
        }

        // An analytic field has no electrodes and still has a potential, so the range has
        // to come from the field as well - otherwise a reflectron's equipotentials would
        // have no scale to sit on.
        if (Extent(model, paths ?? []) is { } box)
        {
            foreach (var value in Plane(field, box, PlaneColumns))
            {
                if (double.IsFinite(value))
                {
                    Cover(value);
                }
            }
        }

        return (surfaces, low, high);
    }

    /// <summary>Where the ions reach along the invariant axis, in metres.</summary>
    /// <remarks>
    /// <para>
    /// The range they actually occupy, not a half-span about the origin: a flight running
    /// from zero to 400 mm would otherwise be drawn as a rod from -420 to +420, with half
    /// its length somewhere no ion goes. Nothing says an instrument is centred on the
    /// origin, and several are not.
    /// </para>
    /// <para>
    /// Padded past the outermost ion by a twentieth of the span, so a rod ends beyond the
    /// beam rather than flush with it. Absent when nothing was flown.
    /// </para>
    /// </remarks>
    private static (double Min, double Max)? Along(IReadOnlyList<TrajectoryPath>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return null;
        }

        var low = double.MaxValue;
        var high = double.MinValue;

        foreach (var point in paths.SelectMany(p => p.PointsMm))
        {
            low = Math.Min(low, point[2] * 1e-3);
            high = Math.Max(high, point[2] * 1e-3);
        }

        var pad = 0.05 * (high - low);

        return high - low > 0.0 ? (low - pad, high + pad) : null;
    }

    /// <summary>A cross-section's conductor, drawn as the prism the solve says it is.</summary>
    private static SurfaceMesh Extruded(
        CompiledElectrode electrode, CompiledSolvedField plane, double nearZ, double farZ)
    {
        var mesh = Join(
            Trace(electrode, plane)
                .Select(run => Surfaces.Extrude([.. run.Points], nearZ, farZ)));

        return Surfaces.Orient(
            mesh, (x, y, _) => electrode.SignedDistance(x, y), OrientStepMetres);
    }

    /// <summary>An axisymmetric conductor, drawn as the solid of revolution it is.</summary>
    private static SurfaceMesh Revolved(CompiledElectrode electrode, CompiledSolvedField plane)
    {
        var mesh = Join(
            Trace(electrode, plane)
                .Select(run => Surfaces.Revolve([.. run.Points], RevolutionFacets)));

        return Surfaces.Orient(
            mesh,
            (x, y, z) => electrode.SignedDistance(x, Math.Sqrt((y * y) + (z * z))),
            OrientStepMetres);
    }

    /// <summary>The zero contour of one electrode's signed distance in the section plane.</summary>
    /// <remarks>
    /// <b>An axisymmetric domain reaching below the axis needs no guard here</b>, which was
    /// worth finding out rather than assuming. Revolving a profile found at a negative
    /// radius would draw the same surface a second time, coincident with the first - but
    /// <c>ModelValidator</c> refuses such a document outright, naming the path and saying
    /// that y is the radius. Clamping here as well would be a second, weaker copy of a rule
    /// that already holds for every consumer.
    /// </remarks>
    private static IReadOnlyList<Contours.Run> Trace(
        CompiledElectrode electrode, CompiledSolvedField plane)
    {
        var minV = plane.MinY;

        var spanU = plane.MaxX - plane.MinX;
        var spanV = plane.MaxY - minV;

        var columns = SectionColumns;
        var rows = Math.Max(16, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var values = new double[columns, rows];

        for (var j = 0; j < rows; j++)
        {
            for (var i = 0; i < columns; i++)
            {
                values[i, j] = electrode.SignedDistance(
                    plane.MinX + (i * stepU), minV + (j * stepV));
            }
        }

        return Contours.Trace(values, plane.MinX, minV, stepU, stepV, 0.0);
    }

    /// <summary>Equipotentials on the section plane, at evenly spaced levels.</summary>
    /// <remarks>
    /// <para>
    /// <b>Lines on a plane rather than surfaces in space</b>, which is the half of section
    /// 16's "equipotential surfaces or slices" a reader can see through. A nest of closed
    /// surfaces hides everything inside the outermost one, including the trajectories the
    /// viewport exists to show.
    /// </para>
    /// <para>
    /// The levels are fixed from the same anchored range the conductors are coloured on,
    /// so a contour and an electrode at the same potential are the same colour.
    /// </para>
    /// </remarks>
    private static List<Equipotential> Levels(
        CompiledModel model,
        IElectrostaticField field,
        double? low,
        double? high,
        IReadOnlyList<TrajectoryPath>? paths = null)
    {
        if (low is not { } floor || high is not { } ceiling || ceiling - floor <= 0.0
            || Extent(model, paths ?? []) is not { } box)
        {
            return [];
        }

        var values = Plane(field, box, PlaneColumns);

        var columns = values.GetLength(0);
        var rows = values.GetLength(1);

        var stepU = (box.MaxX - box.MinX) / (columns - 1);
        var stepV = (box.MaxY - box.MinY) / (rows - 1);

        var levels = new List<Equipotential>();

        for (var n = 1; n <= EquipotentialCount; n++)
        {
            var level = floor + ((ceiling - floor) * n / (EquipotentialCount + 1.0));

            var traced = new List<IReadOnlyList<double>>();

            foreach (var run in Contours.Trace(values, box.MinX, box.MinY, stepU, stepV, level))
            {
                var flat = new List<double>(3 * (run.Points.Count + 1));

                foreach (var (u, v) in run.Points)
                {
                    flat.Add(u * 1e3);
                    flat.Add(v * 1e3);
                    flat.Add(0.0);
                }

                if (run.Closed && run.Points.Count > 0)
                {
                    flat.Add(run.Points[0].U * 1e3);
                    flat.Add(run.Points[0].V * 1e3);
                    flat.Add(0.0);
                }

                traced.Add(flat);
            }

            if (traced.Count > 0)
            {
                levels.Add(new Equipotential(level, traced));
            }
        }

        return levels;
    }

    /// <summary>The potential sampled over the section plane, z = 0.</summary>
    /// <remarks>
    /// <b>Asked at an instant explicitly.</b> A driven field implements the time-free
    /// interface too and answers at t = 0 through it without failing - which is how a
    /// section, a solve report, a summed field and a diffusive run have each ended up
    /// describing an arbitrary moment of an RF cycle. Zero is still the instant drawn; what
    /// differs is that it is chosen rather than fallen into, and said so on the figure.
    /// </remarks>
    private static double[,] Plane(
        IElectrostaticField field,
        (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ) box,
        int columns)
    {
        var spanU = Math.Max(box.MaxX - box.MinX, 1e-9);
        var spanV = Math.Max(box.MaxY - box.MinY, 1e-9);

        var rows = Math.Clamp((int)Math.Round(columns * spanV / spanU), 8, columns);

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var values = new double[columns, rows];

        for (var j = 0; j < rows; j++)
        {
            for (var i = 0; i < columns; i++)
            {
                var point = new Vec3(box.MinX + (i * stepU), box.MinY + (j * stepV), 0.0);

                values[i, j] = field is ITimeVaryingField driven
                    ? driven.PotentialAt(in point, 0.0)
                    : field.PotentialAt(in point);
            }
        }

        return values;
    }

    /// <summary>Concatenates meshes into one, offsetting each one's indices.</summary>
    private static SurfaceMesh Join(IEnumerable<SurfaceMesh> parts)
    {
        var vertices = new List<double>();
        var triangles = new List<int>();

        foreach (var part in parts)
        {
            var offset = vertices.Count / 3;

            vertices.AddRange(part.Vertices);
            triangles.AddRange(part.Triangles.Select(i => i + offset));
        }

        return new SurfaceMesh(vertices, new double[vertices.Count], triangles);
    }

    /// <summary>A mesh in metres, as a conductor in millimetres.</summary>
    private static ConductorSurface Surface(
        string name, double potential, double amplitude, SurfaceMesh mesh) =>
        new(name,
            potential,
            amplitude,
            [.. mesh.Vertices.Select(v => v * 1e3)],
            mesh.Normals,
            mesh.Triangles);

    /// <summary>Flies one ion and returns its path, sampled for display.</summary>
    private static TrajectoryPath? Fly(
        PhaseState launch,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector,
        double scoutInterval,
        int samples)
    {
        // Scouted at the model's own cadence to learn how long the flight is. Drawing at
        // that cadence is what gave an einzel lens a three-segment curve through a
        // focusing element - it is chosen for VTU, not for a picture.
        var scout = new TrajectoryRecorder(scoutInterval);

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, scout);

        if (scout.Samples.Count < 2)
        {
            return null;
        }

        var flight = scout.Samples[^1].TimeSeconds - scout.Samples[0].TimeSeconds;

        var recorded = flight > 0.0
            ? Resample(launch, species, field, settings, detector, flight / samples, samples)
            : scout.Samples;

        var points = new List<IReadOnlyList<double>>(recorded.Count);
        var energies = new List<double>(recorded.Count);

        foreach (var sample in recorded)
        {
            points.Add([
                sample.Position.X * 1e3,
                sample.Position.Y * 1e3,
                sample.Position.Z * 1e3,
            ]);

            energies.Add(
                0.5 * species.MassSi * sample.Velocity.LengthSquared / ElementaryCharge);
        }

        return new TrajectoryPath(points, energies, Fate(result));
    }

    private static IReadOnlyList<TrajectorySample> Resample(
        PhaseState launch,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector,
        double interval,
        int samples)
    {
        var recorder = new TrajectoryRecorder(interval, capacity: 4 * samples);

        TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, recorder);

        return recorder.Samples;
    }

    /// <summary>How an ion ended, by the name the model author wrote where there is one.</summary>
    /// <remarks>
    /// §16 asks for bundles coloured by fate, and "struck rodYPlus" is a thing to move
    /// while "lost" is not - which is ACC-5's argument applied to a picture.
    /// </remarks>
    private static string Fate(TrajectoryResult result) => result.Outcome switch
    {
        TrajectoryOutcome.StopConditionMet => "arrived",
        TrajectoryOutcome.StruckElectrode => result.StruckSurface ?? "an electrode",
        _ => result.Outcome.ToString(),
    };

    /// <summary>The elementary charge, in coulombs.</summary>
    private const double ElementaryCharge = 1.602176634e-19;

    /// <summary>Cells along the longest axis when extracting a volume conductor.</summary>
    private const int VolumeCells = 48;

    /// <summary>How far to pad one axis of an electrode's box, in metres.</summary>
    /// <remarks>
    /// A fraction of the axis's own span, with a floor so a degenerate axis still gets a
    /// box with something in it: a perfectly flat electrode has zero extent one way, and
    /// zero padding there would ask for a sample box of no volume, which the extractor
    /// refuses rather than meshes.
    /// </remarks>
    private static double Pad(double span) => Math.Max(0.08 * span, 1e-6);

    /// <summary>Columns when contouring a conductor in the section plane.</summary>
    /// <remarks>
    /// Finer than the field sampling, because a conductor edge is a hard line and the eye
    /// reads a polygonal circle immediately where it forgives a polygonal equipotential.
    /// </remarks>
    private const int SectionColumns = 240;

    /// <summary>Columns when sampling the potential over the section plane.</summary>
    private const int PlaneColumns = 160;

    /// <summary>Facets around a solid of revolution.</summary>
    private const int RevolutionFacets = 48;

    /// <summary>Columns the density is resampled onto before contouring.</summary>
    /// <remarks>
    /// The density solver's own grid is usually coarser than this and is what the values
    /// come from; resampling is about the smoothness of the extracted shell, not about
    /// inventing resolution the run does not have.
    /// </remarks>
    private const int DensityColumns = 160;

    /// <summary>How many decades below the peak to draw.</summary>
    /// <remarks>
    /// Three shells reach a thousandth of the peak, which is the useful extent of a
    /// packet; a fourth is mostly the numerical tail and, drawn as a solid, hides the
    /// three inside it.
    /// </remarks>
    private const int DensityShellCount = 3;

    /// <summary>How many instants of a diffusive run to look at.</summary>
    /// <remarks>
    /// Enough that the middle of the usable ones is somewhere near mid-flight whatever
    /// fraction of the run the packet survives, and few enough that the clones they cost
    /// are not worth counting.
    /// </remarks>
    private const int DensityInstants = 7;

    /// <summary>Equipotential levels drawn between the lowest and highest potential.</summary>
    private const int EquipotentialCount = 12;

    /// <summary>Differencing step for the outward normal, in metres.</summary>
    /// <remarks>
    /// A micron: small against any electrode this platform models and large against the
    /// rounding in a signed distance built from a chain of arithmetic.
    /// </remarks>
    private const double OrientStepMetres = 1e-6;
}
