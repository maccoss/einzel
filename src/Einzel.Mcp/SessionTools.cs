using Einzel.Commands;
using Einzel.Core.Errors;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Einzel.Mcp;

/// <summary>
/// The tools an agent joining a live session gets, over one shared journal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The scope is the session, not the CLI.</b> Figure 6 draws two agent loops and
/// they are not the same shape. Loop A is a project folder — read the model, edit it,
/// validate, look at it, test — and it needs "no protocol, no session, no network".
/// Loop B is a human with the shell open and an agent joining them on the same document,
/// and §15 says what that buys: "shared live state: an agent operating on the model a
/// human has open, with the viewport updating and both parties writing into one
/// attributed journal. Everything else it could do, the CLI does at least as well and
/// with less machinery."
/// </para>
/// <para>
/// So the surface here is deliberately narrow. Reading the document, editing it, undoing,
/// and reading the journal are the session; validate and preview are the feedback that
/// makes an edit loop mean something, and preview is already the tier AGT-5 built for
/// exactly this — seconds, and permanently labelled. A full run is not here, and the
/// reason is not that it would be hard: it belongs with the shell, where there is a
/// progress surface and a viewport to put the answer in. Until then <c>einzel run</c> is
/// the better spelling and is one process launch away.
/// </para>
/// <para>
/// <b>Results are the CLI's own JSON, byte for byte.</b> Every tool returns
/// <see cref="CommandJson.Write{T}"/> of the same outcome record the CLI serialises for
/// <c>--json</c>. That is AGT-2 made literal instead of asserted: the two surfaces cannot
/// drift, because there is one serialisation and one command object behind both. It also
/// carries GRD-2 for free — the warnings are fields on the outcome, so they propagate
/// into an MCP response by being there rather than by anyone remembering to copy them.
/// </para>
/// </remarks>
public sealed class SessionTools
{
    private readonly SessionJournal _journal;
    private readonly JournalAuthor _human;

    /// <summary>Opens a session over a model.</summary>
    /// <param name="modelPath">The model both parties are working on.</param>
    /// <param name="human">
    /// Who the person at the other end is. In a stdio session there may not be one, and
    /// the name is then nominal; when the shell hosts these tools in process it supplies
    /// the real one, which is the case the journal exists for.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="human"/> is null.</exception>
    /// <exception cref="EinzelException">The document does not validate.</exception>
    public SessionTools(string modelPath, JournalAuthor human)
    {
        ArgumentNullException.ThrowIfNull(human);

        _journal = new SessionJournal(modelPath);
        _human = human;
    }

    /// <summary>The journal this session writes into.</summary>
    /// <remarks>
    /// Exposed so the shell can render it in a panel and redraw the viewport when it
    /// changes. A journal only the protocol can see would make the human the one party
    /// in the session who cannot tell what happened.
    /// </remarks>
    public SessionJournal Journal => _journal;

    /// <summary>Who the person in this session is.</summary>
    public JournalAuthor Human => _human;

    /// <summary>
    /// Who an incoming request is from, taken from the initialize handshake.
    /// </summary>
    /// <param name="server">The server the request arrived on.</param>
    /// <returns>The agent, named as it declared itself.</returns>
    /// <remarks>
    /// <para>
    /// <b>Attribution is not a parameter, and that is the point.</b> A <c>author</c>
    /// argument on the edit tool would be a field the caller fills in, which means an
    /// agent could sign a change with the human's name — by mistake, or because a model
    /// decided that read better. MCP-1 asks that mutations be attributed, and an
    /// attribution the mutating party chooses is a signature, not an attribution.
    /// </para>
    /// <para>
    /// The client declares itself once, in <c>initialize</c>, before any tool exists to
    /// call. Every edit in the session is then stamped with that, and the agent has no
    /// spelling with which to claim otherwise.
    /// </para>
    /// <para>
    /// A client that declares no name is <c>agent:unidentified</c> rather than an error.
    /// The handshake makes the field optional, and refusing the session would trade a
    /// vague attribution for none at all.
    /// </para>
    /// </remarks>
    public static JournalAuthor Caller(McpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new JournalAuthor(Name(server.ClientInfo), AuthorKind.Agent);
    }

    private static string Name(Implementation? client)
    {
        if (client is null)
        {
            return "unidentified";
        }

        var name = string.IsNullOrWhiteSpace(client.Name) ? "unidentified" : client.Name.Trim();

        return string.IsNullOrWhiteSpace(client.Version)
            ? name
            : $"{name}/{client.Version.Trim()}";
    }

    /// <summary>The model as it now stands, and who has changed it.</summary>
    /// <returns>The document text and the session's state.</returns>
    /// <remarks>
    /// Takes up anything that changed on disk outside the session first, which is what
    /// makes a stale-edit refusal recoverable: the caller is told to read again, and
    /// reading again is what clears the drift. A read that returned the session's own
    /// stale view would send the caller round the same refusal for ever.
    /// </remarks>
    public string Read()
    {
        _journal.Reconcile();

        var validation = _journal.Validate();

        return CommandJson.Write(new SessionState(
            _journal.ModelPath,
            _journal.Content,
            _journal.CanUndo,
            [.. _journal.Lines()],
            validation.IsValid,
            validation.Errors));
    }

    /// <summary>Replaces the document, attributed to the calling agent.</summary>
    /// <param name="author">Who is making the change.</param>
    /// <param name="description">What they are doing, in a phrase.</param>
    /// <param name="content">The document as they want it.</param>
    /// <returns>The entry recorded, and the session's state after it.</returns>
    /// <remarks>
    /// <para>
    /// <b>The whole document rather than a patch</b>, matching what the journal stores
    /// and for the same reason: there is then no inverse operation to keep in step with
    /// the forward one. It also makes an edit atomic against a concurrent one — a patch
    /// applied to a document that moved underneath it lands somewhere nobody chose,
    /// whereas a whole document either replaces the one it was written against or is
    /// visibly a replacement of a different one.
    /// </para>
    /// <para>
    /// Refused rather than staged if it does not validate, because the other party's next
    /// action is against whatever is on disk.
    /// </para>
    /// </remarks>
    public string Edit(JournalAuthor author, string description, string content)
    {
        var entry = _journal.Apply(author, description, content);

        return CommandJson.Write(Outcome(entry));
    }

    /// <summary>Reverses the most recent edit that still stands.</summary>
    /// <param name="author">Who is reversing it.</param>
    /// <returns>The entry recorded, and the session's state after it.</returns>
    /// <remarks>
    /// The stack is shared, so this may take back an edit the other party made — which
    /// is what MCP-1 asks for and what a private stack cannot do. The entry names both.
    /// </remarks>
    public string Undo(JournalAuthor author)
    {
        var entry = _journal.Undo(author);

        return CommandJson.Write(Outcome(entry));
    }

    /// <summary>One entry, with what the document is after it.</summary>
    /// <remarks>
    /// Written once so an edit and an undo cannot come to report validity differently -
    /// an undo can take a model from valid to invalid exactly as an edit can, since it
    /// restores whatever was there before.
    /// </remarks>
    private EditOutcome Outcome(JournalEntry entry)
    {
        var validation = _journal.Validate();

        return new EditOutcome(
            entry.Sequence,
            entry.Author.ToString(),
            entry.Description,
            [.. _journal.Lines()],
            validation.IsValid,
            validation.Errors);
    }

    /// <summary>The attributed account of the session so far.</summary>
    public string History() => CommandJson.Write(new JournalOutcome(
        _journal.ModelPath,
        [.. _journal.Entries.Select(e => new JournalLine(
            e.Sequence, e.Author.ToString(), e.Description, e.Undoes))]));

    /// <summary>Validates the document as it now stands.</summary>
    public string Validate() =>
        CommandJson.Write(RunCommand.Validate(_journal.ModelPath));

    /// <summary>Runs the preview tier over the document as it now stands.</summary>
    /// <remarks>
    /// AGT-5's cheap feedback loop, and GRD-5's permanently labelled result. A preview
    /// writes nothing into <c>results/</c> on purpose, so an agent leaning on it during
    /// a session cannot leave a tainted number behind for <c>verify</c> to report as
    /// current.
    /// </remarks>
    public string Preview() => CommandJson.Write(PreviewCommand.Execute(_journal.ModelPath));
}

/// <summary>The session as a joining party finds it.</summary>
/// <param name="ModelPath">The document both parties are on.</param>
/// <param name="Content">Its text as it now stands.</param>
/// <param name="CanUndo">Whether there is an edit left to reverse.</param>
/// <param name="Journal">The account so far, one line per entry.</param>
/// <param name="Valid">Whether the document validates as it stands.</param>
/// <param name="Errors">What is wrong with it, when it does not.</param>
public sealed record SessionState(
    string ModelPath,
    string Content,
    bool CanUndo,
    IReadOnlyList<string> Journal,
    bool Valid,
    IReadOnlyList<EinzelError> Errors);

/// <summary>What a mutation did.</summary>
/// <param name="Sequence">Its place in the session.</param>
/// <param name="Author">Who did it.</param>
/// <param name="Description">What they did.</param>
/// <param name="Journal">The account after it, one line per entry.</param>
/// <param name="Valid">Whether the document validates after it.</param>
/// <param name="Errors">What is wrong with it, when it does not.</param>
/// <remarks>
/// <para>
/// <b>Validity is on the outcome because the journal stopped enforcing it.</b> An edit
/// that does not validate is allowed through now - §16's live validation needs an
/// invalid state to be reachable, and taint-never-block is the platform's rule - so the
/// thing that must not happen is a caller being handed an unqualified success for an
/// edit that broke the model.
/// </para>
/// <para>
/// The window shows this because it re-reads the outline after every edit. This is the
/// same service for an agent, which had none: narrowing the guard without asking what
/// the other caller would then be told is how evidence gets dropped at a seam, and this
/// project has now done that six times.
/// </para>
/// </remarks>
public sealed record EditOutcome(
    int Sequence,
    string Author,
    string Description,
    IReadOnlyList<string> Journal,
    bool Valid,
    IReadOnlyList<EinzelError> Errors);

/// <summary>One entry, as a reader of the journal gets it.</summary>
/// <param name="Sequence">Its place in the session.</param>
/// <param name="Author">Who did it.</param>
/// <param name="Description">What they did.</param>
/// <param name="Undoes">The entry it reverses, or null for an ordinary edit.</param>
public sealed record JournalLine(int Sequence, string Author, string Description, int? Undoes);

/// <summary>The attributed account of a session.</summary>
/// <param name="ModelPath">The document it is over.</param>
/// <param name="Entries">Every entry, in order.</param>
public sealed record JournalOutcome(string ModelPath, IReadOnlyList<JournalLine> Entries);
