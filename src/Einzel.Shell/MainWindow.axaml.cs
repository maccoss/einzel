using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

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

    /// <summary>Runs the model's transport, filling the viewport as it goes.</summary>
    /// <remarks>
    /// <para>
    /// <b>On a background thread, because the run is the length of the physics.</b> A TIMS
    /// elution is twenty minutes; a window that computed it on the UI thread would be a
    /// window that stopped responding for twenty minutes, which is exactly the state this
    /// button exists to replace.
    /// </para>
    /// <para>
    /// <b>A trajectory model is refused rather than watched</b>, by the command layer and
    /// with a reason: a flight finishes faster than a viewport could draw it part way
    /// through, so the whole bundle arriving at once is sooner than the first frame of a
    /// watch would be. The refusal is shown rather than swallowed - a button that appears
    /// to do nothing is worse than one that says why it did not.
    /// </para>
    /// </remarks>
    private static void Watch(string modelPath, SceneView view, Button button, TextBlock status)
    {
        button.IsEnabled = false;
        status.Text = "einzel run " + modelPath + " --progress 5   ·   solving, then stepping";

        var watcher = new Watcher(
            frame => Dispatcher.UIThread.Post(() => view.Show(frame)),
            TimeSpan.FromMilliseconds(400));

        _ = Task.Run(() =>
        {
            try
            {
                var final = ViewportCommand.Watch(modelPath, watcher);

                Dispatcher.UIThread.Post(() =>
                {
                    // The last frame that held a packet, not the last frame: a run whose
                    // ions all arrived ends with an empty box.
                    if (watcher.LastWithPacket is { } held)
                    {
                        view.Show(held);
                    }

                    status.Text = string.Create(
                        CultureInfo.InvariantCulture,
                        $"run finished · {final.Density.Count} density contours at the last frame with a packet");

                    button.IsEnabled = true;
                });
            }
            catch (Exception error)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    status.Text = "Cannot watch this run: " + error.Message;
                    button.IsEnabled = true;
                });
            }
        });
    }

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

        // The model's own knobs, through the command that exists so the window need not
        // parse the document. An outline that fails is shown as an empty list rather than
        // taking the window down: the picture is still worth having.
        try
        {
            this.FindControl<ItemsControl>("Parameters")!.ItemsSource =
                OutlineCommand.Execute(modelPath).Parameters.Select(ParameterRow.From).ToList();
        }
        catch (Exception outlineFailed)
        {
            this.FindControl<ItemsControl>("Parameters")!.ItemsSource =
                new[] { new ParameterRow("could not read the surface", outlineFailed.Message, string.Empty, null) };
        }

        // Amendment 25: each of these is a camera, not a capability, so none of them needs
        // a command spelling - what a named view changes is where somebody is standing.
        Named("ViewIso", (-32.0, 24.0));
        Named("ViewSide", (0.0, 0.0));
        Named("ViewTop", (0.0, 90.0));
        Named("ViewFront", (90.0, 0.0));

        var transparent = this.FindControl<ToggleButton>("Transparent")!;
        transparent.IsCheckedChanged += (_, _) =>
            view.ConductorOpacity = transparent.IsChecked == true ? 0.35 : 1.0;

        var watch = this.FindControl<Button>("WatchRun")!;
        watch.Click += (_, _) => Watch(modelPath, view, watch, status);

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
