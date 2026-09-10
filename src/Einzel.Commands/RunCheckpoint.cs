using System.Globalization;

using Einzel.Transport.Diffusion;

namespace Einzel.Commands;

/// <summary>Where one population had got to when a checkpoint was written.</summary>
/// <param name="Name">
/// Which species, or empty where the run has one and the model named none.
/// </param>
/// <param name="Ions">Real ions still in the tracked region.</param>
/// <param name="CentroidMm">Where the packet was, in millimeters.</param>
/// <param name="SpreadMm">
/// How wide it was, as one standard deviation along each axis - absent where there was
/// nothing left to measure it on, because zero is a real width and a packet one cell
/// across reports one.
/// </param>
public sealed record CheckpointPopulation(
    string Name,
    double Ions,
    IReadOnlyList<double> CentroidMm,
    IReadOnlyList<double>? SpreadMm);

/// <summary>Which run a checkpoint belongs to, as a manifest would say it (PRJ-3).</summary>
/// <param name="Model">The model, relative to the project root.</param>
/// <param name="ModelHash">The hash of the model as it is being run.</param>
/// <param name="EngineVersion">Which build is running it.</param>
/// <param name="SolverBehaviourVersion">Which numerics.</param>
/// <param name="Machine">Where.</param>
/// <param name="StartedUtc">When it started, rather than when it finished.</param>
/// <remarks>
/// <b>On the checkpoint because the manifest is written after the run returns.</b> A run
/// killed part way therefore leaves a checkpoint and no manifest - and every consumer here
/// enumerates manifests, so without this the account of an interrupted run would be
/// unreachable for exactly the case it was written for. Nothing here is derived from the
/// outcome, which is what makes it writable before the first step.
/// </remarks>
public sealed record RunCheckpointProvenance(
    string Model,
    string ModelHash,
    string EngineVersion,
    int SolverBehaviourVersion,
    string Machine,
    string StartedUtc);

/// <summary>A long run's own account of where it has got to, written while it runs.</summary>
/// <remarks>
/// <para>
/// <b>Not a result, and it says so in the document.</b> `results/` already holds two kinds
/// of stored answer and a report command read one of them, so a third file there that
/// looked like an answer would repeat that mistake deliberately. This one carries a
/// <see cref="Note"/> saying what it is, is named `.progress.json` rather than
/// `.result.json`, and is <b>removed when the run finishes</b> - so its presence means the
/// run did not, which is exactly the question somebody asks after a reboot.
/// </para>
/// <para>
/// <b>The completed phases are the part worth keeping.</b> A relaxation study splits a hold
/// into phases so that every boundary reports a width; a run killed in the sixth phase has
/// five real measurements in it, and before this they were lost with the process.
/// </para>
/// </remarks>
public sealed record RunCheckpointJson
{
    /// <summary>What this document is, in words, for whoever finds it.</summary>
    public required string Note { get; init; }

    /// <summary>Which run this is, as a manifest would say it.</summary>
    public required RunCheckpointProvenance Run { get; init; }

    /// <summary>When it was written.</summary>
    public required string WrittenUtc { get; init; }

    /// <summary>Wall-clock seconds since the run started.</summary>
    public required double ElapsedSeconds { get; init; }

    /// <summary>Which phase is running, as the model author named it.</summary>
    public required string Phase { get; init; }

    /// <summary>Which phase it is, counting from one.</summary>
    public required int PhaseIndex { get; init; }

    /// <summary>How many phases the sequence has; one for an unsequenced run.</summary>
    public required int PhaseCount { get; init; }

    /// <summary>Steps taken in this phase.</summary>
    public required int Steps { get; init; }

    /// <summary>Where this phase has reached, in microseconds of its own duration.</summary>
    public required double AtUs { get; init; }

    /// <summary>How long this phase is, in microseconds.</summary>
    public required double OfUs { get; init; }

    /// <summary>The step it is taking, in microseconds.</summary>
    public required double StepUs { get; init; }

    /// <summary>Real ions collected in this phase so far.</summary>
    public required double CollectedIons { get; init; }

    /// <summary>Each population, in the order it was declared.</summary>
    public required IReadOnlyList<CheckpointPopulation> Populations { get; init; }

    /// <summary>The phases that finished, whole, in order.</summary>
    public required IReadOnlyList<SequencePhaseJson> Completed { get; init; }
}

/// <summary>
/// Told how far a run has got, and asked to say so - both on the terminal and on disk.
/// </summary>
/// <remarks>
/// A superset of <see cref="IDensityProgress"/> because a sequenced run has a structure the
/// transport knows nothing about: the transport is stepping one leg and cannot say which of
/// eight phases that is, or what the earlier ones measured.
/// </remarks>
public interface IRunProgress : IDensityProgress
{
    /// <summary>A phase is starting.</summary>
    /// <param name="phase">Its name.</param>
    /// <param name="index">Which phase it is, counting from one.</param>
    /// <param name="count">How many phases there are.</param>
    /// <param name="ofSeconds">How long it lasts.</param>
    void Entering(string phase, int index, int count, double ofSeconds);

    /// <summary>A phase has finished, whole.</summary>
    /// <param name="phase">What it measured.</param>
    void Completed(PhaseOutcome phase);
}

/// <summary>How often a long run should say where it has got to, and who to tell.</summary>
/// <param name="IntervalSeconds">
/// Wall-clock seconds between reports. Zero or less asks for none.
/// </param>
/// <param name="Announce">
/// Told each report as one line of prose, or null to write only the checkpoint file. The
/// CLI passes stderr here, because progress is a diagnostic and CLI-2 keeps stdout for the
/// result.
/// </param>
public sealed record RunProgress(double IntervalSeconds, Action<string>? Announce = null);

/// <summary>
/// Writes a checkpoint beside a run's manifest while the run is still going.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A driven diffusive window is set by a Courant limit against a
/// ponderomotive gradient, so it is hundreds of thousands of steps whatever each one costs,
/// and until this existed such a run produced its first byte of output when it finished.
/// The TIMS front-end sequence failed to finish three times - 4.75 CPU-hours, then 40
/// minutes, then 7.6 wall-hours ended by a Windows update rebooting the machine - and
/// nothing was observed on any of the three, so whether the estimate was low or the run did
/// not terminate could not be told apart. A run that cannot be watched cannot be run at all
/// on a machine nobody fully controls.
/// </para>
/// <para>
/// <b>The wall clock rather than a step count</b>, because what a person wants is a line
/// every so often and the steps a run takes per second span four orders across the models
/// here. That makes <em>which</em> steps report non-deterministic, which is fine and is the
/// reason nothing reported reaches the solve: the answer cannot depend on it, and a test
/// asserts the two are bit-identical.
/// </para>
/// <para>
/// <b>Written through a temporary file and moved into place</b>, so a reader never sees half
/// a document and a process killed mid-write leaves the previous checkpoint rather than a
/// broken one. And a failed write is <em>announced and swallowed</em>: a full disk must not
/// end an eight-hour run at hour seven, which would make the watching cost more than the
/// thing being watched.
/// </para>
/// </remarks>
public sealed class RunCheckpointWriter : IRunProgress
{
    private readonly string _path;
    private readonly double _interval;
    private readonly Action<string>? _announce;
    private readonly System.Diagnostics.Stopwatch _clock =
        System.Diagnostics.Stopwatch.StartNew();

    private readonly List<SequencePhaseJson> _completed = [];
    private readonly RunCheckpointProvenance _run;

    private string _phase = "";
    private int _index = 1;
    private int _count = 1;
    private double _ofSeconds;
    private double _reportedAt = double.NegativeInfinity;
    private bool _complained;

    // Where this phase's stepping was when it was first reported on, so a projection can
    // be made from the STEP RATE rather than from total elapsed time. The difference is
    // not cosmetic: the solve is a one-off cost paid before the first step, so dividing
    // total elapsed by the fraction simulated charges that solve to every remaining
    // microsecond - measured at 82 minutes against an actual 23 on the shipped analyzer,
    // and 17,576 minutes on the first report of all.
    private double _baseElapsed = double.NaN;
    private double _baseTime = double.NaN;

    /// <summary>Starts watching a run.</summary>
    /// <param name="path">Where to write the checkpoint.</param>
    /// <param name="progress">How often, and who else to tell.</param>
    /// <param name="run">Which run this is, so the checkpoint stands on its own.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RunCheckpointWriter(
        string path, RunProgress progress, RunCheckpointProvenance run)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _interval = progress.IntervalSeconds;
        _announce = progress.Announce;
        _run = run;
    }

    /// <summary>Where the checkpoint is written.</summary>
    public string CheckpointPath => _path;

    /// <summary>How many reports have been made.</summary>
    public int Reports { get; private set; }

    /// <inheritdoc/>
    public void Entering(string phase, int index, int count, double ofSeconds)
    {
        _phase = phase ?? "";
        _index = index;
        _count = count;
        _ofSeconds = ofSeconds;

        // A NEW PHASE REPORTS AT ONCE rather than waiting out the interval. The phase
        // boundaries are where a sequenced run's own structure is, and a reader watching an
        // eight-phase run wants to know it has moved on - which is a different question
        // from how far into the current leg it is.
        _reportedAt = double.NegativeInfinity;

        // Each phase times its own stepping. A ramped phase and a held one differ tenfold
        // in cost per microsecond here, so carrying a rate across a boundary would project
        // the wrong phase's.
        _baseElapsed = double.NaN;
        _baseTime = double.NaN;
    }

    /// <inheritdoc/>
    public void Completed(PhaseOutcome phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        _completed.Add(RunCommand.Phase(phase));

        // A finished phase is a measurement, so the checkpoint is written whether or not
        // the interval has come round: this is precisely the state a killed run should be
        // found in, and the phase widths it carries are what the study is for.
        Write(phase.DurationSeconds, []);

        _announce?.Invoke(
            string.Format(
                CultureInfo.InvariantCulture,
                "  finished phase {0} of {1}, {2}, at {3:F1} s of wall clock",
                _index,
                _count,
                phase.Name,
                _clock.Elapsed.TotalSeconds));
    }

    /// <inheritdoc/>
    public bool Wants(int steps, double timeSeconds)
        => _interval > 0.0 && _clock.Elapsed.TotalSeconds - _reportedAt >= _interval;

    /// <inheritdoc/>
    public void Reached(DensityProgressReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        _reportedAt = _clock.Elapsed.TotalSeconds;
        Reports++;

        var populations = new List<CheckpointPopulation>(report.Species.Count);

        foreach (var species in report.Species)
        {
            var (cx, cy) = species.Density.Centroid();
            var (sx, sy) = species.Density.Spread();
            var ions = species.Density.Population();

            populations.Add(new CheckpointPopulation(
                species.Name,
                ions,
                [cx * 1e3, cy * 1e3],

                // Absent rather than zero where there is nothing left. A width of zero is
                // a real answer for a packet inside one cell, and the two must not print
                // alike - the rule this surface reached after an undefined orientation
                // went out as a NaN.
                ions > 0.0 ? [sx * 1e3, sy * 1e3] : null));
        }

        Write(report.TimeSeconds, populations, report.Steps, report.StepSeconds,
            report.CollectedIons, report.UntilSeconds);

        if (_announce is null)
        {
            return;
        }

        var line = new System.Text.StringBuilder();

        line.Append(CultureInfo.InvariantCulture,
            $"  {_clock.Elapsed.TotalSeconds,8:F1} s");

        // A plain diffusive run has no phases, and "phase 1/1" on every line of one is the
        // kind of field a reader learns to skip past - taking the ones that mean something
        // with it.
        if (_count > 1 || _phase.Length > 0)
        {
            line.Append(CultureInfo.InvariantCulture, $"  phase {_index}/{_count} {_phase,-10}");
        }
        line.Append(CultureInfo.InvariantCulture,
            $" {report.TimeSeconds * 1e6,10:F1} of {report.UntilSeconds * 1e6,-10:F1} us");
        line.Append(CultureInfo.InvariantCulture, $"  {report.Steps,9:N0} steps");

        foreach (var population in populations)
        {
            line.Append(CultureInfo.InvariantCulture,
                $"  {population.Ions,10:G6} ions at x {population.CentroidMm[0],8:F3}");

            if (population.SpreadMm is { } spread)
            {
                line.Append(CultureInfo.InvariantCulture, $" +- {spread[0]:F4} mm");
            }
        }

        // THE PROJECTION, FROM THE STEP RATE MEASURED BETWEEN REPORTS. What a person
        // wants is when it will be done, and the only honest basis is how fast it is
        // stepping now - so the first report of a phase records the mark and makes no
        // projection, and every one after it divides the simulated time gained by the wall
        // clock spent gaining it. That excludes the solve and the jitting, exactly as
        // `einzel estimate` excludes process start and for the same reason.
        //
        // Linear in the remaining window, which is right where the step is set by a
        // stability limit that is not moving and wrong across a ramp that moves it - so it
        // is a projection for THIS phase and says so.
        if (double.IsNaN(_baseElapsed))
        {
            _baseElapsed = _reportedAt;
            _baseTime = report.TimeSeconds;
        }
        else if (report.TimeSeconds > _baseTime && report.UntilSeconds > report.TimeSeconds)
        {
            var rate = (_reportedAt - _baseElapsed) / (report.TimeSeconds - _baseTime);
            var remaining = rate * (report.UntilSeconds - report.TimeSeconds);

            // Seconds while there are few of them, because "~1 min" for a phase with
            // forty seconds in it is a rounding presented as a schedule.
            line.Append(remaining < 120.0
                ? string.Format(
                    CultureInfo.InvariantCulture, "  ~{0:F0} s left in this phase", remaining)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "  ~{0:F0} min left in this phase",
                    remaining / 60.0));
        }

        _announce(line.ToString());
    }

    /// <summary>
    /// Removes the checkpoint, because the run finished and its result supersedes it.
    /// </summary>
    /// <remarks>
    /// <b>The absence of this file is the statement.</b> A checkpoint left beside a
    /// complete result would be a second, staler answer in a directory that already holds
    /// two kinds - and a reader finding one after a reboot needs it to mean "this run did
    /// not finish" without having to compare timestamps.
    /// </remarks>
    public void Discard()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // A checkpoint that cannot be deleted is a stale file, which is worth a word
            // and is not worth failing a run that has already produced its answer.
            _announce?.Invoke($"  could not remove {_path}: {exception.Message}");
        }
    }

    private void Write(
        double timeSeconds,
        IReadOnlyList<CheckpointPopulation> populations,
        int steps = 0,
        double stepSeconds = 0.0,
        double collected = 0.0,
        double? ofSeconds = null)
    {
        var document = new RunCheckpointJson
        {
            Note = "a run in progress, not its answer - the answer is written beside this "
                + "as <name>.result.json when the run finishes, and this file is removed "
                + "then. Finding it means the run did not finish.",
            Run = _run,
            WrittenUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ElapsedSeconds = _clock.Elapsed.TotalSeconds,
            Phase = _phase,
            PhaseIndex = _index,
            PhaseCount = _count,
            Steps = steps,
            AtUs = timeSeconds * 1e6,
            OfUs = (ofSeconds ?? _ofSeconds) * 1e6,
            StepUs = stepSeconds * 1e6,
            CollectedIons = collected,
            Populations = populations,
            Completed = _completed,
        };

        try
        {
            var temporary = _path + ".tmp";

            File.WriteAllText(temporary, CommandJson.Write(document));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // Once, not every interval: a full disk would otherwise fill the terminal with
            // the same line for hours, which is how the output that matters gets lost.
            if (!_complained)
            {
                _complained = true;
                _announce?.Invoke($"  could not write {_path}: {exception.Message}");
            }
        }
    }
}
