using System.Globalization;

using Einzel.Core.Errors;
using Einzel.Core.Model;

namespace Einzel.Commands;

/// <summary>Who performed a journalled action.</summary>
public enum AuthorKind
{
    /// <summary>A person, working through the shell or the CLI.</summary>
    Human,

    /// <summary>An agent, working through MCP or the CLI.</summary>
    Agent,

    /// <summary>
    /// Something outside the session, which changed the file on disk.
    /// </summary>
    /// <remarks>
    /// Not a kind of actor so much as an honest statement that the session does not
    /// know who it was. Attributing it to the person would usually be right and is
    /// sometimes wrong - another tool, another session, a git checkout - and a journal
    /// that guesses is worse than one that says it does not know.
    /// </remarks>
    Outside,
}

/// <summary>Who did something, and under what kind of authority.</summary>
/// <param name="Name">What to call them in the journal.</param>
/// <param name="Kind">Person or agent.</param>
/// <remarks>
/// Both halves are needed and neither substitutes for the other. The name is who to
/// ask about an edit; the kind is what to assume about it. A reader scanning a journal
/// for "what did the agent change while I was at lunch" is asking about the kind, and a
/// reader asking "why is this 4 kV" is asking about the name.
/// </remarks>
public sealed record JournalAuthor(string Name, AuthorKind Kind)
{
    /// <summary>The author string a journal line carries.</summary>
    public override string ToString() => Kind switch
    {
        AuthorKind.Agent => $"agent:{Name}",
        AuthorKind.Outside => "outside",
        _ => $"human:{Name}",
    };
}

/// <summary>One entry in a session's journal.</summary>
/// <param name="Sequence">Its place in the session, from one.</param>
/// <param name="Author">Who did it.</param>
/// <param name="Description">What they did, in a phrase.</param>
/// <param name="Undoes">
/// The sequence number this entry reverses, or null when it is an ordinary edit.
/// </param>
/// <param name="Before">The document as it was.</param>
/// <param name="After">The document as it became.</param>
/// <remarks>
/// <para>
/// <b>The whole document, before and after, rather than a patch.</b> A model is text and
/// is meant to stay small, text and diffable (PRJ-2), so the storage is affordable — and
/// what it buys is that undo needs no inverse operation per command. A command that knew
/// how to reverse itself would be a second implementation of what it does, and the two
/// would part company at the first command someone forgot to teach.
/// </para>
/// <para>
/// It also means an entry is meaningful without the ones around it: a reader can see what
/// a change actually was without replaying the session to reach it.
/// </para>
/// </remarks>
public sealed record JournalEntry(
    int Sequence,
    JournalAuthor Author,
    string Description,
    int? Undoes,
    string Before,
    string After)
{
    /// <summary>Whether this entry reverses an earlier one.</summary>
    public bool IsUndo => Undoes is not null;

    /// <summary>One line, as a journal is read.</summary>
    public string Line() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Sequence,4}  {Author,-24}  {(IsUndo ? $"undo of {Undoes}: " : string.Empty)}{Description}");
}

/// <summary>
/// One model, edited by more than one party, with every change attributed and reversible.
/// </summary>
/// <remarks>
/// <para>
/// <b>MCP-1, and the reason the MCP server exists at all.</b> The specification is blunt
/// about the scope: the server's "distinct value is shared live state: an agent operating
/// on the model a human has open, with the viewport updating and both parties writing
/// into one attributed journal. Everything else it could do, the CLI does at least as
/// well and with less machinery." So this is the substance and the protocol is the
/// delivery: a journal that only one party can write to is a file, and a file needs no
/// server.
/// </para>
/// <para>
/// <b>Shared and linear</b>, which are two claims. Shared: one stack, not one per party,
/// so an agent's undo can reverse a human's edit. That is the point rather than a hazard
/// — two private stacks over one document would let each party reverse changes the other
/// had already built on, and the document would end up in a state neither of them
/// authored. Linear: there is no branch to redo into, because an undo is itself an entry.
/// </para>
/// <para>
/// <b>Undo appends rather than pops.</b> A popping stack loses the fact that somebody
/// undid something, and who — which is exactly what MCP-1 asks to be recorded. Appending
/// keeps the journal what its name says it is: an account of what happened, in order,
/// with names against it. Walking back twice appends twice.
/// </para>
/// <para>
/// <b>In memory, deliberately.</b> A session is live; it is the shell's window and the
/// agent connected to it, and it ends when they do. Persisting it would make the journal
/// a second source of truth beside the model file, and PRJ-4's argument — that
/// <c>.einzel/</c> is regenerable and version control optional — says the durable record
/// of a design is the document and its git history, not a sidecar.
/// </para>
/// </remarks>
public sealed class SessionJournal
{
    private readonly List<JournalEntry> _entries = [];
    private readonly HashSet<int> _reversed = [];

    /// <summary>Opens a journal over a model document.</summary>
    /// <param name="modelPath">The model being edited.</param>
    /// <exception cref="ArgumentException">The path is blank.</exception>
    /// <exception cref="FileNotFoundException">There is no file there.</exception>
    /// <exception cref="EinzelException">The document there does not validate.</exception>
    /// <remarks>
    /// The document is validated on opening, so a session never starts from a state no
    /// edit could have produced. An invalid file is a thing to fix with an editor, not a
    /// thing to hand two parties and a shared undo stack.
    /// </remarks>
    public SessionJournal(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        ModelPath = Path.GetFullPath(modelPath);

        if (!File.Exists(ModelPath))
        {
            throw new FileNotFoundException($"model file not found: {ModelPath}", ModelPath);
        }

        Content = File.ReadAllText(ModelPath);

        Check(Content);
    }

    /// <summary>The model this session is over.</summary>
    public string ModelPath { get; }

    /// <summary>The document as it now stands.</summary>
    public string Content { get; private set; }

    /// <summary>Every entry, in order.</summary>
    public IReadOnlyList<JournalEntry> Entries => _entries;

    /// <summary>Whether there is an edit left to reverse.</summary>
    public bool CanUndo => Live() is not null;

    /// <summary>Applies an edit, records it, and writes the file.</summary>
    /// <param name="author">Who is making the change.</param>
    /// <param name="description">What they are doing, in a phrase.</param>
    /// <param name="content">The document as they want it.</param>
    /// <returns>The entry that was recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="author"/> is null.</exception>
    /// <exception cref="ArgumentException">The description or content is blank.</exception>
    /// <exception cref="EinzelException">The proposed document does not validate.</exception>
    /// <remarks>
    /// <para>
    /// <b>Validated before it is recorded, and refused rather than staged.</b> In a shared
    /// session an invalid document is not one party's problem: the other party's next
    /// action is against whatever is on disk. An edit that leaves the model unrunnable is
    /// therefore not a state to pass to somebody else, and the journal is the one place
    /// that can say so before it happens.
    /// </para>
    /// <para>
    /// An edit that changes nothing is still recorded. Somebody did something, and a
    /// journal that quietly drops no-ops is a journal a reader cannot trust to be
    /// complete.
    /// </para>
    /// </remarks>
    public JournalEntry Apply(JournalAuthor author, string description, string content)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        // Whatever happened on disk is on the record before this edit is judged, so
        // the caller is told which document it is being refused against.
        var moved = Reconcile();

        if (moved is not null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = ModelPath,
                Constraint = "the document changed outside this session since you last "
                    + $"read it, and is now at entry {moved.Sequence}",
                Suggestion = "read the model again and edit from what it now says. This "
                    + "edit was written against a document that no longer exists, so "
                    + "applying it would discard the change somebody else just made",
            });
        }

        Check(content);

        return Record(author, description, undoes: null, content);
    }

    /// <summary>Reverses the most recent edit that still stands.</summary>
    /// <param name="author">Who is reversing it.</param>
    /// <returns>The entry recording the reversal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="author"/> is null.</exception>
    /// <exception cref="EinzelException">There is nothing left to reverse.</exception>
    /// <remarks>
    /// The stack is shared, so this may reverse an edit somebody else made — and the
    /// entry it writes names both parties, the one who made the change and the one who
    /// took it back. That pairing is the whole of MCP-1's attribution requirement: an
    /// edit that vanished with no record of who removed it is the failure mode a shared
    /// session has and a private one does not.
    /// </remarks>
    public JournalEntry Undo(JournalAuthor author)
    {
        ArgumentNullException.ThrowIfNull(author);

        // An outside change is an entry like any other, so an undo that follows one
        // reverses *it* rather than silently stepping over it to an older state.
        Reconcile();

        if (Live() is not { } target)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/journal",
                Constraint = _entries.Count == 0
                    ? "nothing has been changed in this session"
                    : "every edit in this session has already been reversed",
                Suggestion = "the journal only reverses edits made through it. Changes made to "
                    + "the file by other means are outside the session and are not the session's "
                    + "to undo",
            });
        }

        _reversed.Add(target.Sequence);

        return Record(
            author,
            $"reverse \"{target.Description}\" by {target.Author}",
            target.Sequence,
            target.Before);
    }

    /// <summary>
    /// Takes up whatever changed on disk outside the session, and records it.
    /// </summary>
    /// <returns>The entry recorded, or null if the document is as the session left it.</returns>
    /// <exception cref="EinzelException">
    /// The file is gone, or what is there no longer validates.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>GRD-9: human work is never silently lost.</b> The person may edit the model in
    /// their own editor while a session is open, and the journal only ever knew about
    /// mutations made through it. Without this, an agent's next whole-document edit
    /// overwrote that change with nothing anywhere to say so.
    /// </para>
    /// <para>
    /// <b>The sharper consequence is what it does to undo.</b> An unrecorded change
    /// breaks the chain - entry N's <c>After</c> stops being entry N+1's <c>Before</c> -
    /// so walking back lands on a document that predates the person's edit and discards
    /// it as a side effect of reversing something else entirely. Recording it keeps the
    /// chain intact, and makes the change reversible on the same shared stack as any
    /// other.
    /// </para>
    /// <para>
    /// Attributed to <see cref="AuthorKind.Outside"/> rather than to the person, because
    /// the session does not know who did it: another tool, another session and a git
    /// checkout all look identical from here. A journal that guesses is worse than one
    /// that says it does not know.
    /// </para>
    /// </remarks>
    public JournalEntry? Reconcile()
    {
        string onDisk;

        try
        {
            onDisk = File.ReadAllText(ModelPath);
        }
        catch (Exception gone) when (gone is IOException or UnauthorizedAccessException)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = ModelPath,
                Constraint = $"the model can no longer be read: {gone.Message}",
                Suggestion = "the document a session is over must stay where the session "
                    + "found it. Restore the file, or start a session over its new location",
            });
        }

        if (string.Equals(onDisk, Content, StringComparison.Ordinal))
        {
            return null;
        }

        // Validated like any other edit. A session that adopted a broken document would
        // hand the next caller a state no edit through the journal could have produced,
        // which is the invariant the constructor establishes.
        Check(onDisk);

        var entry = new JournalEntry(
            _entries.Count + 1,
            new JournalAuthor("outside", AuthorKind.Outside),
            "changed on disk outside this session",
            Undoes: null,
            Content,
            onDisk);

        Content = onDisk;
        _entries.Add(entry);

        return entry;
    }

    /// <summary>The journal as a person reads it.</summary>
    /// <returns>One line per entry, in order.</returns>
    public IReadOnlyList<string> Lines() => [.. _entries.Select(e => e.Line())];

    /// <summary>The most recent edit that has not been reversed.</summary>
    /// <remarks>
    /// An undo entry is not itself a candidate: reversing a reversal is a redo, and this
    /// journal is linear on purpose. What makes it linear is that the walk back is over
    /// ordinary edits only, so there is never a second history to choose between.
    /// </remarks>
    private JournalEntry? Live()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (!_entries[i].IsUndo && !_reversed.Contains(_entries[i].Sequence))
            {
                return _entries[i];
            }
        }

        return null;
    }

    private JournalEntry Record(
        JournalAuthor author, string description, int? undoes, string content)
    {
        var entry = new JournalEntry(
            _entries.Count + 1, author, description, undoes, Content, content);

        // The file is written before the entry is kept, so a journal never claims a
        // change that is not on disk. The other order would leave a reader of the
        // journal describing a document nobody has.
        File.WriteAllText(ModelPath, content);

        Content = content;
        _entries.Add(entry);

        return entry;
    }

    private static void Check(string content)
    {
        ModelDocument document;

        try
        {
            document = Io.ModelJson.Parse(content);
        }
        catch (EinzelException malformed)
        {
            throw new EinzelException(malformed.Error with
            {
                Suggestion = malformed.Error.Suggestion
                    + ". A shared session refuses an edit that does not parse rather than "
                    + "staging it, because the next thing the other party does is against "
                    + "whatever is on disk",
            });
        }

        var validation = ModelValidator.Validate(document, null);

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0] with
            {
                Suggestion = validation.Errors[0].Suggestion
                    + ". A shared session refuses an edit that does not validate rather than "
                    + "staging it: an unrunnable model is not one party's problem when another "
                    + "party is working from the same file",
            });
        }
    }
}
