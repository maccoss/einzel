using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

using Einzel.Commands;
using Einzel.Core.Results;

namespace Einzel.Shell;

/// <summary>The shell's window.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Opens the window, on a model if one was named.</summary>
    /// <param name="modelPath">The model document to open, or null for an empty shell.</param>
    public MainWindow(string? modelPath = null)
    {
        InitializeComponent();
        Load(modelPath);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Builds the scene through the command layer and hands it to the viewport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UI-1 is the whole of this method.</b> The window does not parse the document, does
    /// not extract a conductor surface and does not fly an ion: it calls
    /// <see cref="ViewportCommand.Execute(string, int, double?)"/> and draws what comes
    /// back. A viewport that integrated its own trajectories would be a second transport
    /// implementation, which is how <c>run</c> and <c>test</c> came to disagree twice.
    /// </para>
    /// <para>
    /// <b>Amendment 25</b>: the action is the CLI invocation a person could have typed, and
    /// it is shown rather than merely honoured - so what the window did is legible to the
    /// agent that takes the session over.
    /// </para>
    /// </remarks>
    private void Load(string? modelPath)
    {
        var heading = this.FindControl<TextBlock>("Heading")!;
        var status = this.FindControl<TextBlock>("Status")!;
        var host = this.FindControl<Panel>("ViewportHost")!;

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            heading.Text = "Einzel";
            status.Text = "No model. Start the shell with a model document to draw one.";
            return;
        }

        ViewportOutcome outcome;

        try
        {
            outcome = ViewportCommand.Execute(modelPath);
        }
        catch (Exception error)
        {
            // Named rather than swallowed. A window that opens empty because the model was
            // refused looks exactly like a window that opened empty because the renderer
            // failed, and only one of those is the reader's to fix.
            heading.Text = modelPath;
            status.Text = "Could not open this model: " + error.Message;
            return;
        }

        var view = new SceneView { Scene = outcome };
        host.Children.Add(view);

        // Amendment 25: each of these is a camera, not a capability, so none of them needs
        // a command spelling - what a named view changes is where somebody is standing.
        Named("ViewIso", (-32.0, 24.0));
        Named("ViewSide", (0.0, 0.0));
        Named("ViewTop", (0.0, 90.0));
        Named("ViewFront", (90.0, 0.0));

        var transparent = this.FindControl<ToggleButton>("Transparent")!;
        transparent.IsCheckedChanged += (_, _) =>
            view.ConductorOpacity = transparent.IsChecked == true ? 0.35 : 1.0;

        void Named(string name, (double Azimuth, double Elevation) at) =>
            this.FindControl<Button>(name)!.Click += (_, _) => view.View = at;

        heading.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.ModelPath} — {outcome.Conductors.Count} conductors, {outcome.Trajectories.Count} trajectories");

        // RND-8 is the transport mode's answer carried through, not a decision taken here:
        // a diffusive model produces no trajectories, and saying so is different from
        // having drawn none.
        var paths = outcome.ProducesTrajectories
            ? string.Create(CultureInfo.InvariantCulture, $"{outcome.Trajectories.Count} flights drawn")
            : "this model computes a density, so it has no trajectories to draw";

        var worst = outcome.Warnings
            .Where(w => w.Severity == WarningSeverity.ValidityViolation)
            .Select(w => w.Code)
            .ToList();

        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"einzel render section {modelPath}   ·   {paths}")
            + (worst.Count > 0 ? "   ·   " + string.Join(", ", worst) : string.Empty);
    }
}
