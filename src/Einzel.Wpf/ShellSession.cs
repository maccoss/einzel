using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Wpf;

/// <summary>
/// One action a person took in the window, as the command line that would do it.
/// </summary>
/// <param name="Command">The invocation, exactly as it would be typed.</param>
/// <param name="Entry">What the journal recorded, when the action changed the model.</param>
/// <remarks>
/// <para>
/// <b>Amendment 25, which strengthens AGT-2.</b> Every shell action must be expressible
/// as a CLI invocation and journalled as one. That is not a logging convenience: it
/// means a capability with no command spelling <em>cannot be added to the window</em>,
/// and a person's session hands over to an agent in the same vocabulary.
/// </para>
/// <para>
/// The thing to review when the window grows is the in-process path acquiring an argument
/// the command form has no spelling for. That is the moment the amendment is being
/// broken, and it will look like a convenience at the time.
/// </para>
/// </remarks>
public sealed record ShellAction(string Command, JournalEntry? Entry);

/// <summary>
/// What the window is looking at: one model, one journal, one set of commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>UI-1 is the whole design of this type.</b> The shell owns layout, input, the
/// interactive viewport and the update check; it owns no physics, no validation rules, no
/// file format knowledge and no render output. So this holds a
/// <see cref="SessionJournal"/> and calls command objects, and contains not one line that
/// knows what a model means.
/// </para>
/// <para>
/// <b>The same journal the MCP server writes into</b>, which is what makes a shared
/// session shared rather than two parties with two histories. When the shell hosts the
/// MCP tools in process, an agent's edit and a person's edit land on one stack, and
/// either can reverse the other with both names on the record (MCP-1, GRD-9).
/// </para>
/// <para>
/// Every mutation goes through the journal rather than through the file, so a change made
/// in the window is undoable by the agent connected to it and vice versa. Writing the
/// file directly would be the shortest spelling and would silently make the session
/// one-sided - the seam this project has dropped evidence at five times, in the one place
/// where the evidence is the point.
/// </para>
/// </remarks>
public sealed class ShellSession
{
    private readonly List<ShellAction> _actions = [];

    /// <summary>Opens a model in the window.</summary>
    /// <param name="modelPath">The model to work on.</param>
    /// <param name="person">Who is at the keyboard.</param>
    /// <exception cref="ArgumentNullException"><paramref name="person"/> is null.</exception>
    /// <exception cref="EinzelException">The document does not validate.</exception>
    public ShellSession(string modelPath, JournalAuthor person)
    {
        ArgumentNullException.ThrowIfNull(person);

        Journal = new SessionJournal(modelPath);
        Person = person;

        Record($"einzel validate {Quoted(Journal.ModelPath)}", entry: null);
    }

    /// <summary>The shared, attributed, linear journal (MCP-1).</summary>
    public SessionJournal Journal { get; }

    /// <summary>Who is at the keyboard.</summary>
    public JournalAuthor Person { get; }

    /// <summary>Every action this session has taken, as command lines.</summary>
    public IReadOnlyList<ShellAction> Actions => _actions;

    /// <summary>Validates the model as it stands.</summary>
    /// <returns>What the command object reported.</returns>
    /// <remarks>
    /// The shell does not know what makes a model valid and must not learn: UI-1 puts
    /// validation rules outside it, so this is a call to the same command object the CLI
    /// runs and the MCP server exposes.
    /// </remarks>
    public ValidateOutcome Validate()
    {
        Journal.Reconcile();

        Record($"einzel validate {Quoted(Journal.ModelPath)}", entry: null);

        return RunCommand.Validate(Journal.ModelPath);
    }

    /// <summary>Applies an edit made in the window.</summary>
    /// <param name="description">What the person did, in a phrase.</param>
    /// <param name="content">The document as they want it.</param>
    /// <returns>The action, with the journal entry it produced.</returns>
    /// <remarks>
    /// Through the journal, never through the file. A window that wrote the file itself
    /// would leave the agent connected to it unable to undo what the person just did, and
    /// the person unable to see what the agent did - two parties, two histories, which is
    /// exactly what a shared session is not.
    /// </remarks>
    public ShellAction Edit(string description, string content)
    {
        var entry = Journal.Apply(Person, description, content);

        return Record($"einzel validate {Quoted(Journal.ModelPath)}", entry);
    }

    /// <summary>Reverses the most recent edit that still stands.</summary>
    /// <returns>The action, with the journal entry it produced.</returns>
    public ShellAction Undo()
    {
        var entry = Journal.Undo(Person);

        return Record("einzel undo", entry);
    }

    /// <summary>Runs the preview tier over the model as it stands.</summary>
    /// <returns>The action, and the outcome under it.</returns>
    /// <remarks>
    /// AGT-5's cheap loop, which is what a window wants while somebody drags a slider.
    /// GRD-5 marks the result permanently, and the shell must show that mark rather than
    /// tidy it away - a preview number that looks like a run number is the failure the
    /// tier exists to prevent.
    /// </remarks>
    public (ShellAction Action, PreviewOutcome Outcome) Preview()
    {
        Journal.Reconcile();

        var outcome = PreviewCommand.Execute(Journal.ModelPath);

        return (Record($"einzel preview {Quoted(Journal.ModelPath)}", entry: null), outcome);
    }

    /// <summary>Reads what the interactive viewport should draw.</summary>
    /// <returns>The paths, or none with a reason (RND-8).</returns>
    /// <remarks>
    /// <para>
    /// Recorded as <c>einzel render section</c> rather than invented as a verb of its own.
    /// Amendment 25 requires every shell action to be expressible as a CLI invocation, and
    /// the honest spelling of "look at the geometry and the paths" is the render command -
    /// a viewport is the interactive tier of the same question, per §17's split between
    /// screen tuning and an artifact.
    /// </para>
    /// <para>
    /// The window flies nothing itself: UI-1 puts physics outside the shell, so this is a
    /// call to the same command object anything else would use.
    /// </para>
    /// </remarks>
    public ViewportOutcome Viewport()
    {
        Journal.Reconcile();

        Record($"einzel render section {Quoted(Journal.ModelPath)}", entry: null);

        return ViewportCommand.Execute(Journal.ModelPath);
    }

    /// <summary>Reads what the viewport should draw, without holding the caller's thread.</summary>
    /// <returns>The paths, or none with a reason (RND-8).</returns>
    /// <remarks>
    /// <para>
    /// <b>Because a redraw runs the transport.</b> That was always true and was always
    /// cheap, until the viewport learned to follow a model's sequence - a timed instrument
    /// steps its whole timeline, which for a mobility analyzer is minutes even truncated to
    /// its first phase. A window that stops answering for a minute is one somebody force
    /// quits.
    /// </para>
    /// <para>
    /// <b>The journal is reconciled on the caller's thread, not the worker's.</b> Only the
    /// command runs in the background, and it is handed the path rather than the journal -
    /// so an edit arriving while a transport is in flight cannot race a reconcile. That is
    /// GRD-9's subject: an unrecorded outside change breaks the undo chain, and doing the
    /// recording from two threads would be a way to produce one.
    /// </para>
    /// </remarks>
    public Task<ViewportOutcome> ViewportAsync()
    {
        Journal.Reconcile();

        Record($"einzel render section {Quoted(Journal.ModelPath)}", entry: null);

        // Captured, so the worker touches nothing the UI thread may be changing.
        var path = Journal.ModelPath;

        return Task.Run(() => ViewportCommand.Execute(path));
    }

    /// <summary>Runs the model's transport, handing each frame back as it is drawn.</summary>
    /// <param name="frames">
    /// Told each frame. Called on a background thread, so a caller binding to it marshals.
    /// </param>
    /// <param name="wanted">
    /// Whether a frame is wanted now, given the steps taken and the wall-clock seconds
    /// since the last one. Asked once per step, so it must be cheap.
    /// </param>
    /// <param name="stopping">Watched for a request to give up.</param>
    /// <returns>The bundle the finished run leaves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frames"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Watching is what one does instead of waiting.</b> <see cref="ViewportAsync"/>
    /// stops after the first phase because somebody is staring at a window; this walks the
    /// whole timeline, because the reason to start it is to see what the instrument does to
    /// a packet over all of it. A TIMS elution is twenty minutes, and twenty minutes of
    /// watching is a different proposition from twenty minutes of a frozen window.
    /// </para>
    /// <para>
    /// <b>Cancellation is checked rather than enforced.</b> A density step is not
    /// interruptible part way, so what stopping does is decline to take the next one - the
    /// run then ends where it stands and returns what it has. The alternative, aborting the
    /// thread, would leave the solver's buffers in a state nothing could describe.
    /// </para>
    /// </remarks>
    public Task<ViewportOutcome> WatchAsync(
        Action<ViewportOutcome> frames,
        Func<int, double, bool> wanted,
        CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(wanted);

        Journal.Reconcile();

        // Amendment 25: every shell action is expressible as a command line. This one is
        // `einzel run --progress`, which is the same transport writing a checkpoint instead
        // of a picture - the run is the same run, and what differs is who is told about it.
        Record($"einzel run {Quoted(Journal.ModelPath)} --progress 5", entry: null);

        var path = Journal.ModelPath;

        return Task.Run(
            () => ViewportCommand.Watch(path, new Relay(frames, wanted, stopping)), stopping);
    }

    /// <summary>Passes frames on, and declines them when nobody is waiting for one.</summary>
    private sealed class Relay(
        Action<ViewportOutcome> frames,
        Func<int, double, bool> wanted,
        CancellationToken stopping) : ViewportCommand.IViewportProgress
    {
        private readonly System.Diagnostics.Stopwatch _clock =
            System.Diagnostics.Stopwatch.StartNew();

        private double _lastAt = double.NegativeInfinity;

        public bool Wants(int steps, double timeSeconds)
        {
            stopping.ThrowIfCancellationRequested();

            return wanted(steps, _clock.Elapsed.TotalSeconds - _lastAt);
        }

        public void Reached(ViewportOutcome frame)
        {
            _lastAt = _clock.Elapsed.TotalSeconds;

            frames(frame);
        }
    }

    /// <summary>Runs the model and reports its figures by §12's accuracy class.</summary>
    /// <param name="preview">
    /// Whether to use the preview tier, which is cheaper, writes nothing, and is
    /// permanently marked (AGT-5, GRD-5).
    /// </param>
    /// <returns>The figures, grouped.</returns>
    /// <remarks>
    /// Recorded as the invocation that produced it, which for a full run writes a manifest
    /// and a result. That is correct rather than a side effect to apologise for: Amendment
    /// 25 requires every shell action to be expressible as a command line, and a view that
    /// computed the same numbers without leaving the record behind would be a capability
    /// the command line does not have.
    /// </remarks>
    public ResultsOutcome Results(bool preview = false)
    {
        Journal.Reconcile();

        Record(
            preview
                ? $"einzel preview {Quoted(Journal.ModelPath)}"
                : $"einzel run {Quoted(Journal.ModelPath)}",
            entry: null);

        return ResultsCommand.Execute(Journal.ModelPath, preview);
    }

    /// <summary>Reads REG-2's dimensionless numbers along the model's own path.</summary>
    /// <returns>The profile, and where the selected description does not hold.</returns>
    /// <remarks>
    /// Recorded as <c>einzel run</c>, because that is the invocation whose output carries
    /// the same numbers - a run reports them at the worst point in the gas, and this
    /// reports them where the ion actually goes. The window computes none of it: UI-1 puts
    /// physics outside the shell, and a viewport or an inspector deciding for itself where
    /// a regime boundary lies would be a second copy of spec figure 4.
    /// </remarks>
    public RegimeProfile Regime()
    {
        Journal.Reconcile();

        Record($"einzel run {Quoted(Journal.ModelPath)}", entry: null);

        return RegimeCommand.Execute(Journal.ModelPath);
    }

    /// <summary>Reads the model's declared timeline.</summary>
    /// <returns>The phases, with what each holds.</returns>
    /// <remarks>
    /// Recorded as <c>einzel validate</c>, because validating is what compiles and checks
    /// the timeline - the phases shown here are the compiled ones, and a malformed stage is
    /// a validation error rather than something this view discovers.
    /// </remarks>
    public SequenceOutcome Sequence()
    {
        Journal.Reconcile();

        Record($"einzel validate {Quoted(Journal.ModelPath)}", entry: null);

        return SequenceCommand.Execute(Journal.ModelPath);
    }

    /// <summary>Reads the project the open model belongs to.</summary>
    /// <returns>Its models, studies and figures, with the state of each.</returns>
    /// <remarks>
    /// <para>
    /// Recorded as <c>einzel project</c> (Amendment 25). The open model is one row of it,
    /// which is the point of the view: a person editing one model wants to know what else
    /// is here and whether any of it has been left behind by the edit.
    /// </para>
    /// <para>
    /// The root is found by walking up from the model, so a model kept outside any project
    /// falls back to its own directory rather than to whatever the shell happened to be
    /// launched from - the same rule <c>InferProjectRoot</c> was corrected to follow after
    /// a study wrote its results into an unrelated tree.
    /// </para>
    /// </remarks>
    public ProjectOutcome Project()
    {
        Journal.Reconcile();

        var outcome = ProjectCommand.ForModel(Journal.ModelPath);

        Record($"einzel project {Quoted(outcome.Root)}", entry: null);

        return outcome;
    }

    /// <summary>The extensions this project carries, and what the sandbox does not enforce.</summary>
    /// <returns>
    /// The listing, or null when the model does not sit inside a project - there is nowhere
    /// for an extension to live then, which is a different thing from having none.
    /// </returns>
    /// <remarks>
    /// LIC-2 asks that extensions carry their own licences and that the manager surface
    /// them. The engine half was built with the manifest's <c>licence</c> field; this is
    /// the half that shows it to somebody deciding whether to install one.
    /// </remarks>
    public ExtensionListOutcome? Extensions()
    {
        var layout = Einzel.Project.ProjectLayout.Find(Journal.ModelPath);

        if (layout is null)
        {
            return null;
        }

        var outcome = ExtensionCommand.List(layout);

        Record("einzel ext list", entry: null);

        return outcome;
    }

    private ShellAction Record(string command, JournalEntry? entry)
    {
        var action = new ShellAction(command, entry);

        _actions.Add(action);

        return action;
    }

    /// <summary>A path as a command line would carry it.</summary>
    /// <remarks>
    /// Quoted only where it needs to be, because the point of writing the command down is
    /// that somebody can run it - and a path wrapped in quotes it does not need is one
    /// more thing between them and doing so.
    /// </remarks>
    private static string Quoted(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
