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

    /// <summary>What must be shown alongside the picture (GRD-2).</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>The lowest energy anywhere in the bundle, in electronvolts.</summary>
    public double LowestEnergyEv { get; private set; }

    /// <summary>The highest, likewise.</summary>
    public double HighestEnergyEv { get; private set; }

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
    public double Fraction(double energyEv)
    {
        var span = HighestEnergyEv - LowestEnergyEv;

        return span > 0.0
            ? Math.Clamp((energyEv - LowestEnergyEv) / span, 0.0, 1.0)
            : 0.5;
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
            Warnings.Clear();
            HasBundle = false;

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

        Warnings.Clear();

        foreach (var warning in outcome.Warnings)
        {
            Warnings.Add($"{warning.Code}: {warning.Message}");
        }

        LowestEnergyEv = outcome.LowestEnergyEv ?? 0.0;
        HighestEnergyEv = outcome.HighestEnergyEv ?? 0.0;

        Changed(nameof(LowestEnergyEv));
        Changed(nameof(HighestEnergyEv));

        HasBundle = outcome.Trajectories.Count > 0;

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

        if (outcome.Trajectories.Count == 0)
        {
            return "no ion produced a path";
        }

        var fates = outcome.Trajectories
            .GroupBy(t => t.Fate, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.Trajectories.Count} paths, {outcome.LowestEnergyEv:G4} to "
            + $"{outcome.HighestEnergyEv:G4} eV - {string.Join(", ", fates)}");
    }

    private void Changed(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
