using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Wpf;

/// <summary>
/// The 3D viewport: geometry and trajectory bundles, or a reason there are none (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>It flies nothing itself.</b> UI-1 puts physics outside the shell, so what is drawn
/// is whatever <see cref="ViewportCommand"/> reported — a viewport that integrated its own
/// trajectories would be a second transport implementation, and the two would part company
/// at the first model that exercised the difference.
/// </para>
/// <para>
/// <b>RND-8 is shown, not silently obeyed.</b> A diffusive model has no trajectories, and
/// the viewport says so on the face of it rather than presenting an empty box — an empty
/// viewport and one whose ions were all lost look identical, and only one of them is a
/// statement about the physics.
/// </para>
/// <para>
/// <b>The colour scale is the command's, not this type's.</b> §16 asks for bundles
/// coloured by energy; the range is anchored over the whole bundle by
/// <see cref="ViewportOutcome"/>, because a scale taken per path would give every ion the
/// same colours whatever its energy.
/// </para>
/// </remarks>
public sealed class ViewportViewModel : INotifyPropertyChanged
{
    private readonly ShellSession _session;
    private string _status = string.Empty;
    private bool _hasBundle;
    private bool _hasField;

    /// <summary>Refreshes started, so a superseded one can stand down.</summary>
    private int _refreshes;

    private bool _isWatching;

    /// <summary>The newest frame a watch has produced, whether or not it has been drawn.</summary>
    private ViewportOutcome? _latest;

    /// <summary>Whether a drain is already queued on the drawing thread.</summary>
    private int _queued;

    /// <summary>The last frame drawn that held a packet at all.</summary>
    /// <remarks>
    /// <b>Because the end of a run is empty whenever the ions arrived.</b> A watch that
    /// applied its final bundle unconditionally would spend minutes drawing a packet and
    /// then, at the last instant, replace it with an empty box - which is exactly the
    /// picture the density work was built to stop a diffusive model producing. The finished
    /// viewport draws the middle of the instants that still hold something, for the same
    /// reason; a watch has no list to pick from, so it keeps the last one that did.
    /// </remarks>
    private ViewportOutcome? _held;

    /// <summary>Opens the viewport over a session.</summary>
    /// <param name="session">The session, which owns the model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public ViewportViewModel(ShellSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A watch has filled the collections with a new frame.</summary>
    /// <remarks>
    /// <b>Raised only by a watch</b>, not by every <c>Apply</c>. An ordinary refresh is
    /// followed by the window's own redraw, so raising it there would draw the same scene
    /// twice - and a frame of a run in flight has nobody else to ask for one.
    /// </remarks>
    public event EventHandler? FrameDrawn;

    /// <summary>The paths to draw, empty when the mode produces none.</summary>
    public ObservableCollection<TrajectoryPath> Trajectories { get; } = [];

    /// <summary>The electrodes, as surfaces.</summary>
    public ObservableCollection<ConductorSurface> Conductors { get; } = [];

    /// <summary>Level sets of the potential on the section plane.</summary>
    public ObservableCollection<Equipotential> Equipotentials { get; } = [];

    /// <summary>The density, as nested shells, for a mode that computes one.</summary>
    /// <remarks>
    /// What a diffusive model has instead of paths (TRN-2). RND-8 withholds the
    /// trajectories; this is the thing it withholds them in favour of, and without it the
    /// requirement leaves a viewport with nothing in it for the whole pressure range the
    /// mode exists to cover.
    /// </remarks>
    public ObservableCollection<DensityShell> Density { get; } = [];

    /// <summary>Where the flight begins and where it is caught, or null when there is none.</summary>
    /// <remarks>
    /// A drawing of an instrument with no beginning and no end is hard to read, and this is
    /// sharpest on an analyser whose ions launch part-way along a drift, reverse, and are
    /// caught behind where they started - nothing in the picture otherwise says which end is
    /// which.
    /// </remarks>
    public FlightEnds? Ends { get; private set; }

    /// <summary>Whether there is a density cloud to show.</summary>
    public bool HasDensity { get; private set; }

    /// <summary>What must be shown alongside the picture (GRD-2).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>The lowest energy anywhere in the bundle, in electronvolts.</summary>
    public double LowestEnergyEv { get; private set; }

    /// <summary>The highest, likewise.</summary>
    public double HighestEnergyEv { get; private set; }

    /// <summary>The lowest potential anywhere on the section plane, in volts.</summary>
    public double LowestPotentialVolts { get; private set; }

    /// <summary>The highest, likewise.</summary>
    public double HighestPotentialVolts { get; private set; }

    /// <summary>Whether there is a potential scale to colour anything on.</summary>
    public bool HasField
    {
        get => _hasField;
        private set
        {
            _hasField = value;
            Changed(nameof(HasField));
        }
    }

    /// <summary>Whether there is a bundle to draw at all.</summary>
    public bool HasBundle
    {
        get => _hasBundle;
        private set
        {
            _hasBundle = value;
            Changed(nameof(HasBundle));
        }
    }

    /// <summary>What the viewport is showing, or why it is showing nothing.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Changed(nameof(Status));
        }
    }

    /// <summary>Where on the scale an energy sits, as a fraction.</summary>
    /// <param name="energyEv">The energy, in electronvolts.</param>
    /// <returns>Zero at the bottom of the bundle's range, one at the top.</returns>
    /// <remarks>
    /// <b>A degenerate range is a real case and gives one half, not a division.</b> A
    /// packet whose ions all carry the same energy — a monoenergetic beam in a field-free
    /// drift, which is the simplest model anyone writes — has a range of zero width, and
    /// a scale that divided by it would paint the whole bundle NaN. That is the same
    /// family as the four non-finite doubles that took the JSON surface down.
    /// </remarks>
    public double Fraction(double energyEv) =>
        Between(energyEv, LowestEnergyEv, HighestEnergyEv);

    /// <summary>Where on the potential scale a voltage sits, as a fraction.</summary>
    /// <param name="volts">The potential, in volts.</param>
    /// <returns>Zero at minus the widest excursion, one at plus it, one half at earth.</returns>
    /// <remarks>
    /// <para>
    /// <b>Symmetric about zero rather than spanning the range, and that is the whole point
    /// of a diverging scale.</b> Earth is the value a reader looks for first - it is where
    /// an ion feels no force and what every other potential is measured against - so the
    /// neutral colour has to sit there. Stretching the ramp across the observed range
    /// instead puts the neutral colour at the arithmetic middle, which for a lens holding
    /// 0 V and 500 V is 250 V: an earthed tube would then be painted the same blue as a
    /// genuinely negative one.
    /// </para>
    /// <para>
    /// Anchored across the conductors and the sampled field together, so an electrode and
    /// an equipotential at the same voltage are the same colour whichever they are.
    /// </para>
    /// </remarks>
    public double Potential(double volts)
    {
        var widest = Math.Max(
            Math.Abs(LowestPotentialVolts), Math.Abs(HighestPotentialVolts));

        return Between(volts, -widest, widest);
    }

    /// <summary>Where a value sits on a scale, with a degenerate scale giving one half.</summary>
    /// <remarks>
    /// <b>A range of zero width is a real case, not a defect.</b> A monoenergetic beam in
    /// a field-free drift is the simplest model anyone writes, and so is a geometry with
    /// every electrode earthed - and a scale that divided by the width would paint the
    /// whole picture NaN. That is the family of failure that took the JSON surface down
    /// four times.
    /// </remarks>
    private static double Between(double value, double low, double high)
    {
        var span = high - low;

        return span > 0.0 ? Math.Clamp((value - low) / span, 0.0, 1.0) : 0.5;
    }

    /// <summary>Re-reads what should be drawn, holding the calling thread.</summary>
    /// <returns>Whether there is a bundle.</returns>
    public bool Refresh()
    {
        try
        {
            return Apply(_session.Viewport());
        }
        catch (EinzelException refusal)
        {
            return Refused(refusal);
        }
    }

    /// <summary>Re-reads what should be drawn, without holding the calling thread.</summary>
    /// <returns>Whether there is a bundle.</returns>
    /// <remarks>
    /// <b>The transport runs in the background and the collections are filled on the
    /// caller's thread</b>, which is what an <c>ObservableCollection</c> bound to a
    /// viewport requires. `ConfigureAwait(true)` says so rather than relying on the
    /// default, because the default is what somebody changes.
    /// </remarks>
    public async Task<bool> RefreshAsync()
    {
        // WHICH REFRESH THIS IS, and it has to be counted HERE rather than at the window.
        // Two can be in flight at once - a parameter edit while the last is still stepping,
        // or a model opened on top of it - and the transport takes minutes now, so they
        // finish in whatever order they finish. Guarding only the redraw is not enough: the
        // collections are filled inside this method, so a superseded refresh would stomp
        // them with an older model's packet and the next redraw of any kind would show it.
        //
        // A generation rather than cancellation, because the work is not cancellable. What
        // has to be prevented is applying the result, not computing it.
        var generation = ++_refreshes;

        try
        {
            var outcome = await _session.ViewportAsync().ConfigureAwait(true);

            return generation == _refreshes ? Apply(outcome) : HasBundle;
        }
        catch (EinzelException refusal)
        {
            // A stale refusal is withheld for the same reason a stale success is: the
            // status line would be describing a model the window has already left.
            return generation == _refreshes ? Refused(refusal) : HasBundle;
        }
    }

    /// <summary>How many refreshes have actually filled the collections.</summary>
    /// <remarks>
    /// Exposed so the superseding can be asserted rather than reasoned about: two
    /// overlapping refreshes must leave this at one more than it started, not two.
    /// </remarks>
    public int Applied { get; private set; }

    /// <summary>Whether a run is being watched into this viewport right now.</summary>
    public bool IsWatching
    {
        get => _isWatching;
        private set
        {
            _isWatching = value;
            Changed(nameof(IsWatching));
        }
    }

    /// <summary>How often a watch asks for a frame, in wall-clock seconds.</summary>
    /// <remarks>
    /// <b>The wall clock rather than a step count</b>, for the reason the checkpoint writer
    /// already gives: the steps a run takes per second span four orders across the models
    /// here, so a step cadence is a frame a minute on one and a hundred a second on another.
    /// Two seconds is slow enough that building a frame is a rounding error against the run
    /// and fast enough to read as motion.
    /// </remarks>
    public double FrameIntervalSeconds { get; set; } = 2.0;

    /// <summary>Runs the model's transport, drawing the packet as it goes.</summary>
    /// <param name="stopping">Watched for a request to give up.</param>
    /// <returns>
    /// Whether a packet is drawn. <b>Not <see cref="HasBundle"/>, which a refresh returns</b>
    /// - that is whether there are trajectories, and a watch only ever runs on a model that
    /// has none by construction, so it would answer false on every successful watch there
    /// is.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This is the thing a long run was missing.</b> A driven diffusive window is
    /// hundreds of thousands of steps, and until now the choices were to wait for it in a
    /// frozen window or to read a checkpoint file describing a packet in numbers. What the
    /// viewport can do that a checkpoint cannot is show the shape: a packet that is still
    /// narrowing, one that has hit a wall, one that never left where it was seeded.
    /// </para>
    /// <para>
    /// <b>Frames are coalesced rather than queued.</b> The newest frame replaces any that
    /// has not been drawn yet, because a viewport showing a packet from four frames ago
    /// while three more wait behind it is worse than one showing where the packet is now.
    /// The flag is cleared before the frame is read, so the race can post twice and can
    /// never drop the last one.
    /// </para>
    /// </remarks>
    public async Task<bool> WatchAsync(CancellationToken stopping = default)
    {
        var generation = ++_refreshes;

        // Captured here, on the thread that owns the collections, because that is the
        // thread every frame has to be applied on.
        var drawing = SynchronizationContext.Current;

        IsWatching = true;

        try
        {
            var final = await _session.WatchAsync(
                frame => Offer(drawing, generation, frame),
                (steps, since) => since >= FrameIntervalSeconds,
                stopping).ConfigureAwait(true);

            if (generation != _refreshes)
            {
                return HasDensity;
            }

            if (final.Density.Count == 0 && _held is { } held)
            {
                Apply(held);

                // GRD-2 rather than a tidier picture: the run's own warnings are what the
                // finished bundle earned and the held frame never saw, so they are carried
                // across rather than dropped with the bundle they came on.
                foreach (var warning in final.Warnings)
                {
                    var line = $"{warning.Code}: {warning.Message}";

                    if (!Warnings.Contains(line))
                    {
                        Warnings.Add(line);
                    }
                }

                Status = "the run finished with nothing left to draw - what is shown is the "
                    + "last instant that still held a packet, which for a run whose ions "
                    + "arrived is the informative one";
            }
            else
            {
                Apply(final);
            }

            return HasDensity;
        }
        catch (OperationCanceledException)
        {
            // A stopped watch is not a failure and the packet it drew is not wrong. What
            // it is is incomplete, which the status line says rather than the drawing
            // being cleared - clearing it would throw away the one thing that was gained.
            if (generation == _refreshes)
            {
                Status = "stopped - the packet drawn is where the run had got to, not where it ends";
            }

            return HasDensity;
        }
        catch (EinzelException refusal)
        {
            if (generation != _refreshes)
            {
                return HasDensity;
            }

            Refused(refusal);

            return false;
        }
        finally
        {
            if (generation == _refreshes)
            {
                IsWatching = false;
            }
        }
    }

    /// <summary>Takes a frame from the run's thread and asks the drawing thread for it.</summary>
    private void Offer(SynchronizationContext? drawing, int generation, ViewportOutcome frame)
    {
        Volatile.Write(ref _latest, frame);

        if (Interlocked.Exchange(ref _queued, 1) == 1)
        {
            // One is already waiting to be drawn, and it will pick up this frame instead.
            return;
        }

        if (drawing is null)
        {
            Drain(generation);
            return;
        }

        drawing.Post(_ => Drain(generation), null);
    }

    /// <summary>Draws whatever the newest frame is, on the thread that owns the collections.</summary>
    private void Drain(int generation)
    {
        // Cleared BEFORE the frame is read, so a frame arriving in between queues another
        // drain rather than being dropped. The other order loses the last frame of a run.
        Interlocked.Exchange(ref _queued, 0);

        var frame = Volatile.Read(ref _latest);

        if (frame is null || generation != _refreshes)
        {
            return;
        }

        Apply(frame);

        if (frame.Density.Count > 0)
        {
            _held = frame;
        }

        FrameDrawn?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows a refusal instead of a drawing.</summary>
    private bool Refused(EinzelException refusal)
    {
        Trajectories.Clear();
        Conductors.Clear();
        Ends = null;
        Equipotentials.Clear();
        Density.Clear();
        Warnings.Clear();
        HasBundle = false;
        HasField = false;
        HasDensity = false;

        // AGT-3's error is already a recovery instruction, so it is shown rather than
        // reworded.
        Status = refusal.Error.Constraint
            + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty);

        return false;
    }

    /// <summary>Fills the bound collections from an outcome, on the caller's thread.</summary>
    private bool Apply(ViewportOutcome outcome)
    {
        Applied++;

        Trajectories.Clear();

        foreach (var path in outcome.Trajectories)
        {
            Trajectories.Add(path);
        }

        Conductors.Clear();

        foreach (var conductor in outcome.Conductors)
        {
            Conductors.Add(conductor);
        }

        Equipotentials.Clear();

        foreach (var level in outcome.Equipotentials)
        {
            Equipotentials.Add(level);
        }

        Density.Clear();

        foreach (var shell in outcome.Density)
        {
            Density.Add(shell);
        }

        HasDensity = outcome.Density.Count > 0;
        Ends = outcome.Ends;

        Warnings.Clear();

        foreach (var warning in outcome.Warnings)
        {
            Warnings.Add($"{warning.Code}: {warning.Message}");
        }

        LowestEnergyEv = outcome.LowestEnergyEv ?? 0.0;
        HighestEnergyEv = outcome.HighestEnergyEv ?? 0.0;
        LowestPotentialVolts = outcome.LowestPotentialVolts ?? 0.0;
        HighestPotentialVolts = outcome.HighestPotentialVolts ?? 0.0;

        Changed(nameof(LowestEnergyEv));
        Changed(nameof(HighestEnergyEv));
        Changed(nameof(LowestPotentialVolts));
        Changed(nameof(HighestPotentialVolts));

        HasBundle = outcome.Trajectories.Count > 0;
        HasField = outcome.LowestPotentialVolts is not null;

        Status = Describe(outcome);

        return HasBundle;
    }

    /// <summary>What the viewport is showing, in a phrase.</summary>
    /// <remarks>
    /// The mode producing no trajectories is stated as what the model computes instead,
    /// not as an absence. An empty viewport and one whose ions were all lost look the
    /// same, and only one of them is a statement about the physics.
    /// </remarks>
    private static string Describe(ViewportOutcome outcome)
    {
        if (!outcome.ProducesTrajectories)
        {
            return "no trajectories: this model computes a density field, which is drawn "
                + "as contours rather than as paths";
        }

        // A diffusive model is not a failed trajectory model, so it gets its own line
        // rather than "no ion produced a path" - which is true of it and says the wrong
        // thing, because there was never going to be an ion.
        if (!outcome.ProducesTrajectories)
        {
            var geometry3 = outcome.Conductors.Count > 0
                ? $"{outcome.Conductors.Count} electrodes, "
                : string.Empty;

            if (outcome.Density.Count == 0)
            {
                return $"{geometry3}a density, with nothing left to draw at any instant";
            }

            // The peak and the instant, because three nested shells are the same three
            // shells whatever the density is and whenever it was (GRD-12).
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{geometry3}{outcome.Density.Count} density shells, peak "
                + $"{outcome.PeakDensityPerCubicMetre:G4} /m3 at "
                + $"t = {outcome.DensityAtUs:G4} us - no paths, this mode computes none");
        }

        if (outcome.Trajectories.Count == 0)
        {
            return "no ion produced a path";
        }

        var geometry = outcome.Conductors.Count > 0
            ? $"{outcome.Conductors.Count} electrodes, "
            : string.Empty;

        var fates = outcome.Trajectories
            .GroupBy(t => t.Fate, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{geometry}{outcome.Trajectories.Count} paths, {outcome.LowestEnergyEv:G4} to "
            + $"{outcome.HighestEnergyEv:G4} eV - {string.Join(", ", fates)}");
    }

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
