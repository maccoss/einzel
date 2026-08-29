using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Mcp;

// The stdio entry point. One model, given on the command line, served until the client
// goes away.
//
// Deliberately not a verb on the `einzel` CLI. Figure 3 puts the three surfaces side by
// side as peers, and figure 6 is emphatic that loop A - the project folder - has "no
// protocol, no session, no network". A `serve` verb inside `einzel` would put a server
// in the binary whose distinguishing property is that it is not one.
//
// Nothing is written to stdout but protocol. A stray line there is a malformed message,
// so every diagnostic goes to stderr, which is the CLI's rule (CLI-2) arriving here as a
// hard requirement rather than a convention.
if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("""
        einzel-mcp - the live-session server

          einzel-mcp <model.json> [--human <name>]

        Serves one model over MCP on stdio: a shared, attributed, linear journal that
        an agent and a person write into together. Your edits are signed with the name
        your client declares at initialize.

        The rest of the platform is the `einzel` CLI, which needs no session.
        """);

    return args.Length == 0 ? 2 : 0;
}

var modelPath = args[0];
var human = Environment.UserName;

for (var i = 1; i < args.Length; i++)
{
    if (args[i] is "--human" && i + 1 < args.Length)
    {
        human = args[++i];
        continue;
    }

    Console.Error.WriteLine($"einzel-mcp: unrecognised argument '{args[i]}'");

    return 2;
}

try
{
    var session = new SessionTools(
        modelPath, new JournalAuthor(human, AuthorKind.Human));

    Console.Error.WriteLine(
        $"einzel-mcp: {session.Journal.ModelPath}, with {session.Human}");

    await SessionServer.ServeStdioAsync(session);

    return 0;
}
catch (EinzelException refusal)
{
    // AGT-3: the code, the path and the constraint, not a stack trace. On stderr,
    // because stdout is the protocol.
    Console.Error.WriteLine(CommandJson.Write(new { error = refusal.Error }));

    return 1;
}
catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"einzel-mcp: {failure.Message}");

    return 1;
}
