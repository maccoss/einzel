using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;

namespace Einzel.Commands;

/// <summary>What one electrode holds during one phase.</summary>
/// <param name="Element">Which field element it belongs to.</param>
/// <param name="Name">The name the model author gave it.</param>
/// <param name="PotentialVolts">Its DC potential during this phase.</param>
/// <param name="DriveAmplitudeVolts">Its drive amplitude, signed, or zero where static.</param>
/// <param name="Changed">Whether either differs from the previous phase.</param>
/// <remarks>
/// <b>Both the DC and the drive, because reading one is a mistake this project has made six
/// times.</b> A quadrupole's rods hold zero volts of DC and all of their potential as drive,
/// so an editor showing the DC alone would present a mass filter as an earthed box that
/// never changes.
/// </remarks>
public sealed record PhaseElectrode(
    string Element,
    string Name,
    double PotentialVolts,
    double DriveAmplitudeVolts,
    bool Changed);

/// <summary>One state of the instrument, and how long it is held.</summary>
/// <param name="Name">What the phase is for.</param>
/// <param name="StartsAtUs">When it begins, in microseconds from launch.</param>
/// <param name="EndsAtUs">When it ends.</param>
/// <param name="DurationUs">How long it lasts.</param>
/// <param name="Mode">The transport mode it is described in.</param>
/// <param name="ModeChanged">Whether that differs from the previous phase (SEQ-1).</param>
/// <param name="Electrodes">The excitations during it.</param>
/// <param name="ChangedCount">How many electrodes this phase moves.</param>
public sealed record SequencePhase(
    string Name,
    double StartsAtUs,
    double EndsAtUs,
    double DurationUs,
    string Mode,
    bool ModeChanged,
    IReadOnlyList<PhaseElectrode> Electrodes,
    int ChangedCount);

/// <summary>The instrument as a timed state machine.</summary>
/// <param name="ModelPath">The model, as an absolute path.</param>
/// <param name="Phases">The states, in order.</param>
/// <param name="TotalUs">How long the whole sequence runs.</param>
/// <param name="Sequenced">Whether the model declares a sequence at all.</param>
/// <param name="Warnings">What the reader needs alongside it (GRD-2).</param>
public sealed record SequenceOutcome(
    string ModelPath,
    IReadOnlyList<SequencePhase> Phases,
    double TotalUs,
    bool Sequenced,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>
/// The declared timeline: phases, their excitations, and the mode of each (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>The declared sequence, not a run's account of one.</b> A run reports the phases it
/// executed and what the packet did in each; this reports what the document says the
/// instrument does, which is what a person editing it needs and is available without
/// solving anything.
/// </para>
/// <para>
/// <b>What changes between phases is the information.</b> A sequenced instrument repeats
/// most of its state from one phase to the next - a trap that holds at one voltage and
/// pushes at another moves one electrode and leaves the rest - so a table repeating every
/// setting for every phase buries the two rows that matter. Each electrode is marked
/// against the phase before it, and each phase counts what it moves.
/// </para>
/// <para>
/// <b>The mode is a property of the run rather than of an element</b>, which is why it sits
/// on the phase: two elements naming different modes for one instant is not something a
/// superposition can resolve the way it resolves two fields. A phase that changes it is
/// marked, because that is SEQ-1's boundary and the point at which a packet is converted
/// between descriptions - losing its velocities in one direction and having them invented
/// in the other.
/// </para>
/// </remarks>
public static class SequenceCommand
{
    /// <summary>Reads a model's declared timeline.</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>The phases, with what each holds.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    public static SequenceOutcome Execute(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        var model = validation.Model!;
        var warnings = new List<ValidityWarning>();

        if (model.Phases.Count == 0)
        {
            // Not an empty timeline - no timeline. An instrument that holds one state for
            // the whole run is the ordinary case and is a different thing from a sequence
            // with nothing in it, which is what an empty table would suggest.
            warnings.Add(new ValidityWarning(
                "sequence.none",
                "this model declares no sequence, so the instrument holds one state for the "
                + "whole run. A sequence is a `sequence` block at the model level, or "
                + "`stages` on a single solve element",
                WarningSeverity.Provenance));

            return new SequenceOutcome(absolute, [], 0.0, false, warnings);
        }

        var phases = new List<SequencePhase>(model.Phases.Count);
        Dictionary<string, (double Potential, double Drive)>? previous = null;
        var previousMode = (string?)null;

        for (var index = 0; index < model.Phases.Count; index++)
        {
            var phase = model.Phases[index];
            var settings = Settings(model, index);

            var electrodes = settings
                .Select(e => new PhaseElectrode(
                    e.Key.Element,
                    e.Key.Name,
                    e.Value.Potential,
                    e.Value.Drive,
                    Changed(previous, e.Key.Key, e.Value)))
                .ToList();

            phases.Add(new SequencePhase(
                phase.Name,
                (phase.EndsAtSeconds - phase.DurationSeconds) * 1e6,
                phase.EndsAtSeconds * 1e6,
                phase.DurationSeconds * 1e6,
                phase.Mode,
                previousMode is not null
                    && !string.Equals(previousMode, phase.Mode, StringComparison.Ordinal),
                electrodes,
                electrodes.Count(e => e.Changed)));

            previous = settings.ToDictionary(e => e.Key.Key, e => e.Value, StringComparer.Ordinal);
            previousMode = phase.Mode;
        }

        // The sequencer holds the last state after the sequence ends rather than switching
        // everything off, and a reader of a timeline will otherwise assume the instrument
        // stops when the table does. An ion still in flight would suddenly coast - a physics
        // change disguised as a bookkeeping one.
        warnings.Add(new ValidityWarning(
            "sequence.last-phase-holds",
            $"the last phase ('{phases[^1].Name}') holds after the sequence ends at "
            + $"{phases[^1].EndsAtUs:G6} us. An instrument left alone stays where it was "
            + "put, so an ion still in flight continues in that field rather than in none",
            WarningSeverity.Provenance));

        if (phases.Any(p => p.ModeChanged))
        {
            warnings.Add(new ValidityWarning(
                "sequence.mode-changes",
                "this sequence crosses between transport descriptions (SEQ-1). Trajectories "
                + "to a density discards the velocities entirely, and a density to "
                + "trajectories invents them - drawn Maxwellian at the gas temperature plus "
                + "the local drift, which is right while the ions are in the gas that "
                + "thermalised them and wrong the moment anything happens faster than the "
                + "momentum-transfer time",
                WarningSeverity.Provenance));
        }

        return new SequenceOutcome(
            absolute, phases, phases[^1].EndsAtUs, true, warnings);
    }

    /// <summary>What every electrode holds during one phase, across all elements.</summary>
    /// <remarks>
    /// Keyed by element and name together, because two elements may each have a
    /// <c>ring</c> and they are different conductors. The element is carried into the row
    /// rather than folded into the name so a reader can tell which is which without
    /// parsing a compound string.
    /// </remarks>
    private static Dictionary<
        (string Element, string Name, string Key), (double Potential, double Drive)>
        Settings(CompiledModel model, int phase)
    {
        var settings = new Dictionary<
            (string, string, string), (double, double)>();

        for (var e = 0; e < model.Fields.Count; e++)
        {
            var element = model.Fields[e];

            // A field element has no name of its own in the format, so it is identified by
            // its position - which is what a validation error would name it by too.
            var where = $"element {e + 1}";

            // The stage for this phase where the element has one, otherwise the electrodes
            // as declared - an element no phase moves is static, which is a distinction
            // rather than an oversight.
            if (element.Solve is { } plane)
            {
                var electrodes = phase < plane.Stages.Count
                    ? plane.Stages[phase].Electrodes
                    : plane.Electrodes;

                foreach (var electrode in electrodes)
                {
                    settings[(where, electrode.Name, $"{e}/{electrode.Name}")] =
                        (electrode.Potential, electrode.DriveAmplitude);
                }
            }

            // Volume geometries carry stages too, and leaving them out would show a
            // sequenced 3-D instrument as one that never changes - the shape of omission
            // that is worse than a refusal, because it looks like an answer.
            if (element.Solve3D is { } volume)
            {
                var electrodes = phase < volume.Stages.Count
                    ? volume.Stages[phase].Electrodes
                    : volume.Electrodes;

                foreach (var electrode in electrodes)
                {
                    settings[(where, electrode.Name, $"{e}/{electrode.Name}")] =
                        (electrode.Potential, electrode.DriveAmplitude);
                }
            }
        }

        return settings;
    }

    /// <summary>Whether an electrode moved since the phase before.</summary>
    /// <remarks>
    /// Exact comparison, not a tolerance. A stage sets a parameter and the electrodes fall
    /// out of the expressions over it, so two phases that leave one alone produce the same
    /// double - and a tolerance would hide a deliberate change of a millivolt while
    /// claiming to filter noise there is none of.
    /// </remarks>
    private static bool Changed(
        Dictionary<string, (double Potential, double Drive)>? previous,
        string key,
        (double Potential, double Drive) now) =>
        previous is not null
        && (!previous.TryGetValue(key, out var before)
            || before.Potential != now.Potential
            || before.Drive != now.Drive);
}
