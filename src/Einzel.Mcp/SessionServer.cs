using System.ComponentModel;

using Einzel.Commands;
using Einzel.Core.Errors;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Einzel.Mcp;

/// <summary>
/// The session's tools, as MCP primitives.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="SessionTools"/> so the session is usable without a protocol
/// attached. That is not tidiness: the shell drives the same session in process, and if
/// the only way to reach it were through a transport then the window would be talking to
/// itself over JSON-RPC to edit its own document.
/// </para>
/// <para>
/// Built with <see cref="McpServerTool.Create(Delegate, McpServerToolCreateOptions)"/>
/// over closures rather than by attribute discovery, because each tool needs the one
/// session this server is over. Attribute discovery would want a container and a scope
/// per request, which is machinery for a case that does not arise: one server, one open
/// model, for as long as the two parties are working on it.
/// </para>
/// </remarks>
public static class SessionServer
{
    /// <summary>What the server tells a joining client it is for.</summary>
    /// <remarks>
    /// A client reads this before it reads any tool. What it most needs to know is what
    /// this server is <em>not</em> — an agent that treats it as a remote CLI will look
    /// for verbs that are not here and conclude the platform lacks them, when the answer
    /// is that they are one process launch away and better there.
    /// </remarks>
    public const string Instructions =
        "A live Einzel session: one model document, shared by you and a person, with "
        + "every change attributed and reversible through one linear journal. Your edits "
        + "are signed with the name your client declared at initialize; you cannot sign "
        + "them as anybody else.\n\n"
        + "This is not a remote command line. The rest of the platform - run, sweep, "
        + "optimise, scan, render, test, export - is the `einzel` CLI, which needs no "
        + "session and no protocol, and is the right tool when nobody is watching. Use "
        + "this server when a person has the model open and you are working on it "
        + "together.\n\n"
        + "Every result is the same JSON the CLI emits for --json, warnings included. A "
        + "warning is part of the result rather than commentary on it: a validity "
        + "violation cannot be suppressed and must reach whoever reads the number.";

    /// <summary>Builds the tool collection for a session.</summary>
    /// <param name="session">The session the tools act on.</param>
    /// <returns>The tools, in a stable order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public static IReadOnlyList<McpServerTool> Tools(SessionTools session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return
        [
            Tool(
                "model_read",
                "The model document as it now stands, with the session's journal so far. "
                + "Read this before editing: the other party may have changed it since "
                + "you last looked.",
                () => Guarded(session.Read)),

            Tool(
                "model_edit",
                "Replace the model document. Give the whole document, not a patch. The "
                + "change is signed with your client's declared name and is refused, not "
                + "staged, if the result does not validate - the person you are working "
                + "with acts on whatever is on disk next.",
                (RequestContext<CallToolRequestParams> context,
                 [Description("What you are doing, in a phrase, for the journal.")] string description,
                 [Description("The complete model document.")] string content) =>
                    Guarded(() => session.Edit(SessionTools.Caller(context.Server), description, content))),

            Tool(
                "model_undo",
                "Reverse the most recent edit that still stands. The stack is shared, so "
                + "this may take back a change the person made rather than one of yours; "
                + "the journal records who made it and who reversed it.",
                (RequestContext<CallToolRequestParams> context) =>
                    Guarded(() => session.Undo(SessionTools.Caller(context.Server)))),

            Tool(
                "session_journal",
                "Every change in this session, in order, with who made it and what it "
                + "reversed. An undo is an entry rather than an erasure, so this is an "
                + "account of what happened and not only of what survived.",
                () => Guarded(session.History)),

            Tool(
                "model_validate",
                "Check the document: units, bounds, expressions, geometry, and regime "
                + "validity. Fast, and the right thing to call after an edit.",
                () => Guarded(session.Validate)),

            Tool(
                "model_preview",
                "Fly the model at preview accuracy - seconds rather than a full run. The "
                + "result is permanently labelled as a preview and cannot be quoted, "
                + "exported, or fed to an optimiser. Use it to see whether a change "
                + "helped; use the CLI's `einzel run` for a number anybody will rely on.",
                () => Guarded(session.Preview)),
        ];
    }

    /// <summary>Runs a session over stdio until the client disconnects.</summary>
    /// <param name="session">The session to serve.</param>
    /// <param name="cancellation">Stops the server.</param>
    /// <returns>A task that completes when the client goes away.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <remarks>
    /// stdio because it works with no shell. §15 makes streamable HTTP hosted in process
    /// by the shell the primary transport and stdio "a convenience", which is the right
    /// ordering for a finished platform and the wrong one to build in: the convenience
    /// runs today and the primary needs a window that does not exist yet. The tools are
    /// the same either way, which is the whole reason they are built above the transport.
    /// </remarks>
    public static async Task ServeStdioAsync(
        SessionTools session, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "einzel",
                Title = "Einzel live session",
                Version = typeof(SessionServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            },
            ServerInstructions = Instructions,
            ToolCollection = [.. Tools(session)],
        };

        await using var transport = new StdioServerTransport(options);
        await using var server = McpServer.Create(transport, options);

        await server.RunAsync(cancellation).ConfigureAwait(false);
    }

    private static McpServerTool Tool(string name, string description, Delegate body) =>
        McpServerTool.Create(body, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
        });

    /// <summary>
    /// Turns a refusal into the error AGT-3 specifies rather than a stack trace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <see cref="EinzelException"/> already carries a machine-readable code, the
    /// offending path, the constraint, and a suggested correction. Letting it surface as
    /// a transport-level fault would keep the message and lose the structure, and the
    /// structure is the part an agent can act on without guessing.
    /// </para>
    /// <para>
    /// It is returned as a tool result rather than raised as a protocol error for the
    /// same reason: a refusal is an answer about the model, not a failure of the call.
    /// The caller asked whether an edit was acceptable and found out that it was not.
    /// </para>
    /// </remarks>
    private static string Guarded(Func<string> body)
    {
        try
        {
            return body();
        }
        catch (EinzelException refusal)
        {
            return CommandJson.Write(new { error = refusal.Error });
        }
    }
}
