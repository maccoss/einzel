using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
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
    /// <returns>The paths, or none with a reason.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static ViewportOutcome Execute(string modelPath, int samplesPerPath = 256)
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
                + "instead is a density field, which is drawn as contours",
                WarningSeverity.Provenance));

            return new ViewportOutcome(absolute, [], false, null, null, warnings);
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

        return new ViewportOutcome(absolute, paths, true, lowest, highest, warnings);
    }

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
}
