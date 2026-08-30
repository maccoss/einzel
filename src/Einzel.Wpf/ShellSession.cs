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
