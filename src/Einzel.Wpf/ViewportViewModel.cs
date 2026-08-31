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

    /// <summary>Re-reads what should be drawn.</summary>
    /// <returns>Whether there is a bundle.</returns>
    public bool Refresh()
    {
        ViewportOutcome outcome;

        try
        {
            outcome = _session.Viewport();
        }
        catch (EinzelException refusal)
        {
            Trajectories.Clear();
            Conductors.Clear();
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
