using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Results;
using Einzel.Shell.Presentation;

namespace Einzel.Shell;

/// <summary>The shell's window.</summary>
/// <remarks>
/// <para>
/// Layout and input, and nothing else. Every question about what a model contains, what is
/// wrong with it, or what an edit does goes to <see cref="ModelTreeViewModel"/> and through it
/// to the command layer (UI-1); every action goes through <see cref="ShellSession"/>, which
/// records it as the command line that would repeat it (Amendment 25).
/// </para>
/// <para>
/// <b>Through the session and not around it.</b> The first version of this window called the
/// command objects directly, which drew the same picture and left nothing behind: no journal,
/// so nothing a person did could be undone or seen by an agent on the same model, and no
/// record of the commands, so the window was the one surface whose actions could not be
/// replayed. The session is what makes the window a peer of the CLI rather than a viewer.
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window
{
    private readonly TextBlock _heading;
    private readonly TextBlock _status;
    private readonly SelectableTextBlock _validation;
    private readonly Border _validationBar;
    private readonly Panel _host;
    private readonly ItemsControl _parameters;
    private readonly Button _undo;
    private readonly Button _watch;
    private readonly Button _stop;
    private readonly ItemsControl _legend;

    private ShellSession? _session;
    private ModelTreeViewModel? _tree;
    private ResultsViewModel? _results;
    private RegimeViewModel? _regime;
    private SequenceViewModel? _sequence;
    private ProjectViewModel? _project;
    private ExtensionsViewModel? _extensions;
    private SceneView? _view;

    /// <summary>The panels that have been read since the model last changed.</summary>
    /// <remarks>
    /// The cheap ones - timeline, project, extensions - are read the first time their tab is
    /// opened rather than when the window opens, because each read is journalled as a
    /// command, and a journal that opens with five reads nobody asked for buries the ones
    /// they did. An edit forgets the timeline and the project, which it can change.
    /// </remarks>
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);

    /// <summary>Where a drag in the viewport last was, while one is in progress.</summary>
    private Avalonia.Point? _dragFrom;

    /// <summary>Whether the drag in progress moves the picture rather than turning it.</summary>
    private bool _panning;
    private (double Azimuth, double Elevation) _angles = ViewportPicture.Views["iso"];
    private double _opacity = 1.0;

    /// <summary>The redraw, one at a time, with every request made meanwhile folded into one more.</summary>
    /// <remarks>
    /// A redraw is a field solve and a transport, and each edit asks for one. Three quick edits
    /// would otherwise run three solves side by side for two pictures nobody sees - and the
    /// first version did, numbering them so a stale one stood down only after it had paid for
    /// itself.
    /// </remarks>
    private readonly Coalescing _redraw;

    /// <summary>Whether the viewport shows the document as it now stands.</summary>
    /// <remarks>
    /// False from an edit until the redraw it asked for has drawn, and after a redraw that
    /// could not be drawn. A watch is refused while it is false, because its frames are of the
    /// document on disk and would be drawn over the geometry of an earlier one.
    /// </remarks>
    private bool _current;

    private bool _watching;
    private CancellationTokenSource? _stopping;

    /// <summary>Whether the results or the regime panel is running the model.</summary>
    /// <remarks>
    /// Each closes the tree while it runs, as a watch does: what it runs is the document on
    /// disk, and an edit part way through would give figures of one revision shown beside a
    /// tree of another. Closing the tree is cheaper and plainer than describing the mixture.
    /// </remarks>
    private bool _resultsRunning;

    private bool _regimeRunning;

    /// <summary>Whether the results or the regime panel is showing something an edit would date.</summary>
    private bool _resultsShown;

    private bool _regimeShown;

    /// <summary>Opens the window, on a model if one was named.</summary>
    /// <param name="modelPath">The model document to open, or null for an empty shell.</param>
    public MainWindow(string? modelPath = null)
    {
        InitializeComponent();

        _heading = this.FindControl<TextBlock>("Heading")!;
        _status = this.FindControl<TextBlock>("Status")!;
        _validation = this.FindControl<SelectableTextBlock>("Validation")!;
        _validationBar = this.FindControl<Border>("ValidationBar")!;
        _host = this.FindControl<Panel>("ViewportHost")!;
        _parameters = this.FindControl<ItemsControl>("Parameters")!;
        _undo = this.FindControl<Button>("UndoButton")!;
        _watch = this.FindControl<Button>("WatchRun")!;
        _stop = this.FindControl<Button>("StopWatch")!;
        _legend = this.FindControl<ItemsControl>("Legend")!;

        _redraw = new Coalescing(DrawOnceAsync);
        _redraw.BusyChanged += (_, _) => UpdateControls();

        Wire();
        Open(modelPath);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Connects the controls, once, whatever is later drawn behind them.</summary>
    private void Wire()
    {
        // Amendment 25: each of these is a camera, not a capability, so none of them needs
        // a command spelling - what a named view changes is where somebody is standing.
        // From the command layer's table, so the window and `einzel render still --view`
        // mean the same thing by each name.
        Named("ViewIso", ViewportPicture.Views["iso"]);
        Named("ViewSide", ViewportPicture.Views["side"]);
        Named("ViewTop", ViewportPicture.Views["top"]);
        Named("ViewFront", ViewportPicture.Views["front"]);

        this.FindControl<Button>("ViewFit")!.Click += (_, _) => _view?.Fit(_view.View);

        var transparent = this.FindControl<ToggleButton>("Transparent")!;

        transparent.IsCheckedChanged += (_, _) =>
        {
            _opacity = transparent.IsChecked == true ? 0.35 : 1.0;

            if (_view is { } view)
            {
                view.ConductorOpacity = _opacity;
            }
        };

        _watch.Click += (_, _) => _ = WatchAsync();
        _stop.Click += (_, _) =>
        {
            _stopping?.Cancel();

            UpdateControls();
        };
        _undo.Click += (_, _) => Undo();

        this.FindControl<Button>("ResultsRun")!.Click += (_, _) => _ = ResultsAsync(preview: false);
        this.FindControl<Button>("ResultsPreview")!.Click += (_, _) => _ = ResultsAsync(preview: true);
        this.FindControl<Button>("RegimeInspect")!.Click += (_, _) => _ = RegimeAsync();
        this.FindControl<Button>("SequenceRead")!.Click += (_, _) => Read("Sequence");
        this.FindControl<Button>("ProjectRead")!.Click += (_, _) => Read("Project");
        this.FindControl<Button>("ExtensionsRead")!.Click += (_, _) => Read("Extensions");

        this.FindControl<TabControl>("Panels")!.SelectionChanged += (_, _) => ReadIfUnread();

        // The viewport's gestures, on the panel that holds it: the scale drawn over the
        // picture takes no clicks, and the panel has a background, so it is hit wherever the
        // picture is.
        _host.PointerPressed += OnViewportPressed;
        _host.PointerMoved += OnViewportMoved;
        _host.PointerReleased += (_, e) =>
        {
            _dragFrom = null;
            e.Pointer.Capture(null);
        };
        _host.PointerCaptureLost += (_, _) => _dragFrom = null;
        _host.PointerWheelChanged += OnViewportWheel;
        _host.DoubleTapped += (_, _) => _view?.Fit(_view.View);

        // On the list rather than in the template, so every row's box reports here whatever
        // the list has been rebuilt into. Tunnelling for the keys, because a text box may
        // take Enter for itself before it bubbles.
        _parameters.AddHandler(InputElement.KeyDownEvent, OnValueKey, RoutingStrategies.Tunnel);
        _parameters.AddHandler(InputElement.LostFocusEvent, OnValueLeft, RoutingStrategies.Bubble);

        void Named(string name, (double Azimuth, double Elevation) at) =>
            this.FindControl<Button>(name)!.Click += (_, _) =>
            {
                _angles = at;

                // Fitted exactly, leaving nothing of an orbit, a zoom or a pan behind: a
                // named view is the same picture every time it is pressed.
                _view?.Fit(at);
            };
    }

    /// <summary>Opens a model, or says why it could not be.</summary>
    /// <param name="modelPath">The model document, or null for an empty shell.</param>
    /// <remarks>
    /// <para>
    /// <b>Who is at the keyboard is declared here and nowhere else.</b> MCP-1's attribution
    /// comes from the party rather than from an argument on each action, for the same reason
    /// the MCP server takes it from the <c>initialize</c> handshake: an author supplied per
    /// action is a signature, and a signature can be filled in with somebody else's name.
    /// The name is the operating-system user, because the shell is a person's window on
    /// their own machine and asking would be ceremony.
    /// </para>
    /// <para>
    /// The tree is read on the UI thread, because it compiles the document and solves
    /// nothing; the picture is drawn off it, because drawing flies the bundle.
    /// </para>
    /// </remarks>
    private void Open(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            _heading.Text = "Einzel";
            _status.Text = "No model. Start the shell with a model document to draw one: "
                + "einzel-shell models/reflectron.json";
            UpdateControls();
            return;
        }

        _heading.Text = modelPath;

        try
        {
            _session = new ShellSession(
                modelPath, new JournalAuthor(Environment.UserName, AuthorKind.Human));
            _tree = new ModelTreeViewModel(_session);
        }
        catch (Exception refused)
        {
            // Named rather than swallowed. A window that opens empty because the model was
            // refused looks exactly like one that opened empty because the renderer failed,
            // and only one of those is the reader's to fix.
            _session = null;
            _tree = null;
            _status.Text = "Could not open this model: " + Reason(refused);
            UpdateControls();
            return;
        }

        _parameters.ItemsSource = _tree.Parameters;
        this.FindControl<ItemsControl>("JournalList")!.ItemsSource = _tree.Journal;
        this.FindControl<ItemsControl>("CommandList")!.ItemsSource = _tree.Commands;

        _results = new ResultsViewModel(_session);
        _regime = new RegimeViewModel(_session);
        _sequence = new SequenceViewModel(_session);
        _project = new ProjectViewModel(_session);
        _extensions = new ExtensionsViewModel(_session);

        Items("ResultsList", _results.Rows);
        Items("ResultsWarnings", _results.Warnings);
        Items("RegimeGrid", _regime.Samples);
        Items("ExcursionList", _regime.Excursions);
        Items("RegimeWarnings", _regime.Warnings);
        Items("PhaseList", _sequence.Phases);
        Items("SequenceWarnings", _sequence.Warnings);
        Items("ProjectModels", _project.Models);
        Items("ProjectContents", _project.Contents);
        Items("ProjectWarnings", _project.Warnings);
        Items("ExtensionGrid", _extensions.Extensions);
        Items("ExtensionUnenforced", _extensions.Unenforced);

        Text("ResultsStatus", _results.Status);
        Text("RegimeStatus", _regime.Status);

        ShowTree(refused: false);

        _ = _redraw.RequestAsync();
    }

    /// <summary>
    /// Builds the scene through the session and puts it in the viewport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UI-1 is the whole of this method.</b> The window does not parse the document, does
    /// not extract a conductor surface and does not fly an ion: the session calls
    /// <see cref="ViewportCommand"/> and the window draws what comes back. A viewport that
    /// integrated its own trajectories would be a second transport implementation, which is
    /// how <c>run</c> and <c>test</c> came to disagree twice.
    /// </para>
    /// <para>
    /// <b>An edit that leaves the model invalid leaves the picture as it was</b>, and says so.
    /// The tree stands with the complaint against it - taint, never block - but there is no
    /// field to draw for a model that does not compile, and blanking the viewport would hide
    /// the instrument a person is in the middle of changing.
    /// </para>
    /// </remarks>
    private async Task DrawOnceAsync()
    {
        if (_session is null)
        {
            return;
        }

        var path = _session.Journal.ModelPath;

        _status.Text = "einzel render section " + path + "   ·   solving";

        Task<ViewportOutcome> drawing;

        try
        {
            drawing = _session.ViewportAsync();
        }
        catch (Exception refused)
        {
            _current = false;
            _status.Text = "Could not draw this model: " + Reason(refused);
            return;
        }

        // The command was recorded when the redraw started, so it is listed now rather than
        // after a solve that may take a minute.
        _tree?.RefreshHistory();

        ViewportOutcome outcome;

        try
        {
            outcome = await drawing.ConfigureAwait(true);
        }
        catch (Exception refused)
        {
            _current = false;
            _status.Text = (_view is null
                ? "Could not draw this model: "
                : "The viewport still shows the model as it last drew, because this one "
                    + "could not be: ")
                + Reason(refused);

            return;
        }

        Draw(path, outcome);
        _current = true;
    }

    /// <summary>Puts a measured scene into the window.</summary>
    /// <param name="modelPath">The model document that was drawn.</param>
    /// <param name="outcome">What the command layer produced.</param>
    /// <remarks>
    /// On the UI thread, and separate from the measuring so that the split between what is
    /// computed and what is displayed is visible rather than implied.
    /// </remarks>
    private void Draw(string modelPath, ViewportOutcome outcome)
    {
        if (_view is null)
        {
            _view = new SceneView { Scene = outcome, View = _angles, ConductorOpacity = _opacity };
            _host.Children.Add(_view);

            // Over the viewport, so it is never hidden by the instrument and never takes a click.
            _host.Children.Add(new ScaleOverlay(_view));
        }
        else
        {
            _view.Replace(outcome);
        }

        _heading.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.ModelPath} — {outcome.ElectrodeCount()} electrodes, {outcome.Trajectories.Count} trajectories");

        // RND-8 is the transport mode's answer carried through, not a decision taken here:
        // a diffusive model produces no trajectories, and saying so is different from
        // having drawn none.
        var paths = outcome.ProducesTrajectories
            ? string.Create(CultureInfo.InvariantCulture, $"{outcome.Trajectories.Count} flights drawn")
            : "this model computes a density, so it has no trajectories to draw";

        _status.Text = "einzel render section " + modelPath + "   ·   " + paths
            + Worst(outcome.Warnings);

        Scales(outcome);
    }

    /// <summary>The legend under the viewport, saying what the colors in it mean.</summary>
    /// <remarks>
    /// <para>
    /// <b>Rebuilt from whatever is drawn, including nothing.</b> The rows are
    /// <see cref="ViewportPicture.Scales"/>'s answer for this bundle, so a model with no
    /// trajectories loses its energy row and a geometry entirely at earth has no legend at
    /// all - rather than a stale one left over from the last model, which is the failure a
    /// legend is most likely to have and the hardest to notice.
    /// </para>
    /// <para>
    /// Not refreshed per frame of a watch: a watched run is diffusive, so its colors are the
    /// field's and the metal's and those do not move - the sequencer refuses a stage that
    /// moves an electrode.
    /// </para>
    /// </remarks>
    private void Scales(ViewportOutcome outcome)
    {
        var rows = ViewportPicture.Scales(outcome).Select(s => new LegendRow(s)).ToList();

        _legend.ItemsSource = rows;
        _legend.IsVisible = rows.Count > 0;
    }

    /// <summary>Runs the model's transport, filling the viewport as it goes.</summary>
    /// <remarks>
    /// <para>
    /// <b>On a background thread, because the run is the length of the physics.</b> A TIMS
    /// elution is twenty minutes; a window that computed it on the UI thread would be a
    /// window that stopped responding for twenty minutes, which is exactly the state this
    /// button exists to replace.
    /// </para>
    /// <para>
    /// <b>The tree is closed to edits while it runs.</b> The run is of the document as it was
    /// when the button was pressed, and an edit part way through would leave the viewport
    /// drawing frames of one instrument over the geometry of another.
    /// </para>
    /// <para>
    /// <b>And it does not start until the viewport shows the document as it stands</b> - not
    /// while an edit's redraw is still computing, and not after one that could not be drawn.
    /// The watch runs the document on disk, so starting it early would draw its packet over
    /// the geometry of the model before the edit. Since the tree is closed while a watch runs,
    /// no redraw can be asked for until it ends, and none can replace its last frame.
    /// </para>
    /// <para>
    /// <b>A trajectory model is refused rather than watched</b>, by the command layer and
    /// with a reason: a flight finishes faster than a viewport could draw it part way
    /// through, so the whole bundle arriving at once is sooner than the first frame of a
    /// watch would be. The refusal is shown rather than swallowed - a button that appears
    /// to do nothing is worse than one that says why it did not.
    /// </para>
    /// </remarks>
    private async Task WatchAsync()
    {
        if (_session is null || _view is not { } view || _watching || _redraw.Busy || !_current)
        {
            return;
        }

        Watching(true);

        _status.Text = "einzel run " + _session.Journal.ModelPath
            + " --progress 5   ·   solving, then stepping";

        var watcher = new Watcher(
            frame => Dispatcher.UIThread.Post(() => view.Show(frame)),
            TimeSpan.FromMilliseconds(400));

        // Checked rather than enforced, and the session is where it is checked: a density
        // step is not interruptible part way, so stopping declines to take the next one and
        // the run ends where it stands. Aborting the thread would leave the solver's buffers
        // in a state nothing could describe.
        using var stopping = new CancellationTokenSource();

        _stopping = stopping;

        // After the field is set, so the Stop button is enabled by the same rule that
        // disables it rather than by a second one here.
        UpdateControls();

        try
        {
            var running = _session.WatchAsync(watcher, stopping.Token);

            _tree?.RefreshHistory();

            var final = await running.ConfigureAwait(true);

            // The last frame that held a packet, not the last frame: a run whose ions all
            // arrived ends with an empty box.
            var shown = watcher.LastWithPacket ?? final;

            if (watcher.LastWithPacket is { } held)
            {
                view.Show(held);
            }

            // Counted off the frame ACTUALLY SHOWN rather than off the final bundle. A run
            // whose ions arrived ends empty, so reading the count from `final` printed
            // "0 density contours" over a viewport displaying five - a number describing
            // something other than the picture beside it.
            var drawn = shown.Density.Count > 0
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{shown.Density.Count} density contours at the last frame with a packet")
                : "no packet remained at any frame";

            _status.Text = "run finished · " + drawn + Worst(final.Warnings);
        }
        catch (OperationCanceledException)
        {
            // A STOPPED WATCH IS NOT A FAILED ONE. The packet it drew is where the run had
            // got to, which is a real state of the instrument and the reason somebody
            // pressed stop; clearing the viewport would throw away the only thing the watch
            // produced. The status says the run is partial, rather than the drawing being
            // taken away to say it.
            if (watcher.LastWithPacket is { } held)
            {
                view.Show(held);
            }

            _status.Text = watcher.LastWithPacket is null
                ? "run stopped before any packet was drawn"
                : "run stopped · showing the last frame that held a packet, which is where it "
                    + "had got to rather than where it ends";
        }
        catch (Exception refused)
        {
            _status.Text = "Cannot watch this run: " + Reason(refused);
        }
        finally
        {
            _stopping = null;

            Watching(false);
        }
    }

    /// <summary>Closes the tree and the buttons that change the model, or opens them again.</summary>
    private void Watching(bool on)
    {
        _watching = on;
        UpdateControls();
    }

    /// <summary>Enables what may be pressed now, and nothing else.</summary>
    private void UpdateControls()
    {
        _watch.IsEnabled = _session is not null
            && _view is not null
            && _current
            && !_redraw.Busy
            && !_watching;

        // Enabled only while there is something to stop, and disabled again the moment it
        // has been asked: pressing it twice should not read as the first press not having
        // worked, since what follows is one more step rather than nothing.
        _stop.IsEnabled = _watching && _stopping is { IsCancellationRequested: false };

        var open = !_watching && !_resultsRunning && !_regimeRunning;

        _parameters.IsEnabled = open;
        _undo.IsEnabled = open && _tree?.CanUndo == true;
    }

    /// <summary>Reverses the most recent edit that still stands.</summary>
    private void Undo()
    {
        if (_tree is null || _watching)
        {
            return;
        }

        var undone = _tree.Undo();

        if (undone)
        {
            _current = false;
        }

        ShowTree(refused: !undone);

        if (undone)
        {
            Changed();
            _ = _redraw.RequestAsync();
        }
    }

    /// <summary>Runs the model, or previews it, and shows its figures by accuracy class.</summary>
    /// <param name="preview">Whether to use the cheap, permanently marked tier.</param>
    /// <remarks>
    /// <para>
    /// <b>In the background, because a run is the length of the physics.</b> The WPF shell ran
    /// it on its UI thread behind a wait cursor, which on a volume model is minutes of a window
    /// that does not answer. The figures are filled in on this thread once the run returns.
    /// </para>
    /// <para>
    /// <b>The tree is closed while it runs</b>, for the reason <see cref="_resultsRunning"/>
    /// gives. An agent on the same session can still edit the file; the command checks that
    /// the run was of the text it read and refuses rather than mixing two revisions.
    /// </para>
    /// <para>
    /// <b>A full run stores a result, so the project view is read again</b> - its state for
    /// this model has just changed from "not run" or "stale" to "current", and a project tab
    /// still saying otherwise would be describing the folder before the run.
    /// </para>
    /// </remarks>
    private async Task ResultsAsync(bool preview)
    {
        if (_results is null || _session is null)
        {
            return;
        }

        var run = this.FindControl<Button>("ResultsRun")!;
        var look = this.FindControl<Button>("ResultsPreview")!;
        var taint = this.FindControl<Border>("ResultsTaint")!;

        run.IsEnabled = look.IsEnabled = false;
        _resultsRunning = true;
        UpdateControls();
        Text("ResultsStatus", preview ? "previewing..." : "running - the same work einzel run does...");

        try
        {
            var refreshing = _results.RefreshAsync(preview);

            _tree?.RefreshHistory();

            await refreshing.ConfigureAwait(true);

            Text("ResultsStatus", _results.Status);
            _resultsShown = true;

            if (!preview)
            {
                _read.Remove("Project");
            }
        }
        catch (Exception refused)
        {
            Text("ResultsStatus", "Could not run this model: " + Reason(refused));
        }
        finally
        {
            // GRD-5's taint, where it cannot be missed rather than in a line somebody may have
            // scrolled past.
            taint.IsVisible = _results.Tainted;
            run.IsEnabled = look.IsEnabled = true;
            _resultsRunning = false;
            UpdateControls();
            ReadIfUnread();
        }
    }

    /// <summary>Walks the path and reads the regime at each step.</summary>
    /// <remarks>It flies the model, so it runs in the background for the reason a run does.</remarks>
    private async Task RegimeAsync()
    {
        if (_regime is null || _session is null)
        {
            return;
        }

        var inspect = this.FindControl<Button>("RegimeInspect")!;

        inspect.IsEnabled = false;
        _regimeRunning = true;
        UpdateControls();
        Text("RegimeStatus", "walking the path...");

        try
        {
            var refreshing = _regime.RefreshAsync();

            _tree?.RefreshHistory();

            await refreshing.ConfigureAwait(true);

            Text("RegimeStatus", _regime.Status);
            _regimeShown = true;
        }
        catch (Exception refused)
        {
            Text("RegimeStatus", "Could not walk this model: " + Reason(refused));
        }
        finally
        {
            inspect.IsEnabled = true;
            _regimeRunning = false;
            UpdateControls();
        }
    }

    /// <summary>Reads one of the cheap panels: the timeline, the project or the extensions.</summary>
    /// <param name="panel">The tab's header.</param>
    /// <remarks>
    /// On the UI thread, because each is a read of files rather than a run: the timeline is a
    /// validation, the project a walk of the folder, the extensions a listing.
    /// </remarks>
    private void Read(string panel)
    {
        try
        {
            switch (panel)
            {
                case "Sequence" when _sequence is not null:
                    _sequence.Refresh();
                    Text("SequenceStatus", _sequence.Status);
                    break;

                case "Project" when _project is not null:
                    _project.Refresh();
                    Text("ProjectStatus", _project.Status);
                    break;

                case "Extensions" when _extensions is not null:
                    _extensions.Refresh();
                    Text("ExtensionStatus", _extensions.Status);
                    Text("ExtensionInterpreter", _extensions.Interpreter);
                    break;

                default:
                    return;
            }

            _read.Add(panel);
        }
        catch (Exception refused)
        {
            Text(panel switch
            {
                "Sequence" => "SequenceStatus",
                "Project" => "ProjectStatus",
                _ => "ExtensionStatus",
            }, "Could not read this: " + Reason(refused));
        }

        _tree?.RefreshHistory();
    }

    /// <summary>Reads the panel just opened, if it has not been read since the model changed.</summary>
    private void ReadIfUnread()
    {
        if (this.FindControl<TabControl>("Panels")!.SelectedItem is TabItem { Header: string panel }
            && !_read.Contains(panel))
        {
            Read(panel);
        }
    }

    /// <summary>Forgets the panels an edit can change, and re-reads the one on screen.</summary>
    /// <remarks>
    /// Results and the regime are not re-run - each is the length of a run - so what they show
    /// is marked as of the model before the edit instead, where the figures are read. A panel
    /// that kept its status after an edit would present the figures of the previous model as
    /// those of the one in the tree beside it.
    /// </remarks>
    private void Changed()
    {
        _read.Remove("Sequence");
        _read.Remove("Project");
        ReadIfUnread();

        const string Dated = " - the model has been edited since, so these are of the model "
            + "before the edit; run again for the one in the tree";

        if (_resultsShown && _results is not null)
        {
            _resultsShown = false;
            Text("ResultsStatus", _results.Status + Dated);
        }

        if (_regimeShown && _regime is not null)
        {
            _regimeShown = false;
            Text("RegimeStatus", _regime.Status + Dated);
        }
    }

    private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_view is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_host);
        var buttons = point.Properties;

        // Turn with the left button; move with the right or middle one, or with Shift - the
        // last because a trackpad has no middle button and a laptop's right click is awkward
        // to drag with.
        _panning = buttons.IsRightButtonPressed
            || buttons.IsMiddleButtonPressed
            || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        _dragFrom = point.Position;
        e.Pointer.Capture(_host);
        e.Handled = true;
    }

    private void OnViewportMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from || _view is not { } view)
        {
            return;
        }

        var at = e.GetPosition(_host);
        var width = Math.Max(1.0, _host.Bounds.Width);
        var height = Math.Max(1.0, _host.Bounds.Height);

        if (_panning)
        {
            // Clip space runs two units across the viewport, and up where the screen runs down.
            view.PanBy(2.0 * (at.X - from.X) / width, -2.0 * (at.Y - from.Y) / height);
        }
        else
        {
            // The point under the cursor follows it, both ways: dragging right brings the near side
            // of the instrument right, and dragging down brings it down. Checked live against the
            // near corner of a plate, after a first version turned it the wrong way sideways.
            view.Orbit(OrbitDegreesPerPixel * (at.X - from.X), OrbitDegreesPerPixel * (at.Y - from.Y));
        }

        _dragFrom = at;
    }

    private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_view is not { } view)
        {
            return;
        }

        var at = e.GetPosition(_host);
        var width = Math.Max(1.0, _host.Bounds.Width);
        var height = Math.Max(1.0, _host.Bounds.Height);

        view.ZoomAbout(
            Math.Pow(1.15, e.Delta.Y),
            (2.0 * at.X / width) - 1.0,
            1.0 - (2.0 * at.Y / height));

        e.Handled = true;
    }

    /// <summary>How far a drag across one pixel turns the camera.</summary>
    /// <remarks>
    /// About a half turn across a typical viewport, which is enough to go from the side view
    /// to the end view in one sweep and small enough to set an angle by eye.
    /// </remarks>
    private const double OrbitDegreesPerPixel = 0.4;

    private void Items(string name, System.Collections.IEnumerable items) =>
        this.FindControl<ItemsControl>(name)!.ItemsSource = items;

    private void Text(string name, string text) =>
        this.FindControl<SelectableTextBlock>(name)!.Text = text;

    private void OnValueKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox box)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            Commit(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && box.DataContext is ParameterRow row)
        {
            // Back to what the model holds, which is what Escape means in every other box.
            box.Text = row.Shown;
            e.Handled = true;
        }
    }

    private void OnValueLeft(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox box)
        {
            Commit(box);
        }
    }

    /// <summary>Puts what was typed through the tree, and redraws if it changed the model.</summary>
    /// <remarks>
    /// <para>
    /// <b>A row the tree no longer holds is ignored.</b> An edit re-reads the tree, which
    /// replaces every row, and the box that was committed with Enter then loses focus on its
    /// way out of the list - carrying the value that was just applied, against a row that
    /// still shows the old one. Committing that would write the same edit to the journal
    /// twice.
    /// </para>
    /// <para>
    /// A refused value is put back to what the model holds. Two values for one parameter,
    /// with nothing saying which is real, is what a model tree exists to prevent.
    /// </para>
    /// </remarks>
    private void Commit(TextBox box)
    {
        if (_tree is null
            || _watching
            || box.DataContext is not ParameterRow row
            || !_tree.Parameters.Contains(row))
        {
            return;
        }

        switch (_tree.Commit(row, box.Text))
        {
            case ParameterEdit.Unchanged:
                return;

            case ParameterEdit.Refused:
                box.Text = row.Shown;
                ShowTree(refused: true);
                return;

            case ParameterEdit.Applied:
                _current = false;
                ShowTree(refused: false);
                Changed();
                _ = _redraw.RequestAsync();
                return;
        }
    }

    /// <summary>Shows whether the model validates, and what the tree last had to say.</summary>
    /// <param name="refused">Whether the last thing asked of the tree was refused.</param>
    /// <remarks>
    /// Three states and three colours: a model that validates, a model that does not, and a
    /// request that was turned down while the model stayed as it was. The last two read
    /// differently on purpose - one is about the document, the other is about what was just
    /// typed, and a person should not have to read the sentence to know which.
    /// </remarks>
    private void ShowTree(bool refused)
    {
        if (_tree is null)
        {
            return;
        }

        _validation.Text = _tree.Status;

        (_validationBar.Background, _validation.Foreground) = (refused, _tree.Valid) switch
        {
            (true, _) => (Brush("#4A3B12"), Brush("#F0DFA8")),
            (false, false) => (Brush("#5A1F1F"), Brush("#F2D0D0")),
            _ => (Brush("#242424"), Brush("#C8C8C8")),
        };

        UpdateControls();
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    /// <summary>What went wrong, as AGT-3 already phrased it where it can.</summary>
    /// <remarks>
    /// An <see cref="EinzelException"/> is a recovery instruction - what is wrong and what to
    /// do about it - so the window shows that rather than writing its own.
    /// </remarks>
    private static string Reason(Exception error) => error is EinzelException refusal
        ? refusal.Error.Constraint
            + (refusal.Error.Suggestion is { } how ? " - " + how : string.Empty)
        : error.Message;

    /// <summary>The validity violations on a result, ready to append to a status line.</summary>
    /// <param name="warnings">Everything the command layer reported.</param>
    /// <returns>The codes, or an empty string where there are none.</returns>
    /// <remarks>
    /// <para>
    /// <b>GRD-2 reaches the window or it does not hold.</b> A warning that survives the
    /// engine, the command layer and the result record and then stops at the last surface a
    /// person actually reads has been dropped, and this project has dropped evidence at a
    /// seam seven times. The watch path had its own line and showed none of them.
    /// </para>
    /// <para>
    /// Violations only, because that is the class GRD-3 makes unsuppressible; a status bar
    /// carrying every advisory teaches the reader to skim the one kind that must never be
    /// skimmed - the same argument that keeps the report's hatched band off everything but
    /// a violation.
    /// </para>
    /// </remarks>
    private static string Worst(IReadOnlyList<ValidityWarning> warnings)
    {
        var codes = warnings
            .Where(w => w.Severity == WarningSeverity.ValidityViolation)
            .Select(w => w.Code)
            .ToList();

        return codes.Count > 0 ? "   ·   " + string.Join(", ", codes) : string.Empty;
    }
}
