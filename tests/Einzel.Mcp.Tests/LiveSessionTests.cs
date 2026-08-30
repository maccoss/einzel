using System.Text.Json;

using Einzel.Commands;
using Einzel.Mcp;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Xunit.Abstractions;

namespace Einzel.Mcp.Tests;

/// <summary>
/// A real client, over a real transport, against the shipped server.
/// </summary>
/// <remarks>
/// <para>
/// These launch <c>einzel-mcp</c> as a process and speak MCP to it, rather than calling
/// <see cref="SessionTools"/> directly. The session's own behaviour is tested next door
/// in <c>SessionJournalTests</c>; what is being tested here is everything the protocol
/// adds — that the tools are declared, that a refusal survives the round trip with its
/// structure intact, and above all that attribution comes from the handshake.
/// </para>
/// <para>
/// That last one cannot be tested any other way. The claim is about what a caller
/// <em>cannot</em> do, and a direct call to <c>Edit</c> can pass any author it likes;
/// only a client on the far side of a transport is restricted to the tool's declared
/// arguments.
/// </para>
/// </remarks>
public sealed class LiveSessionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-mcp", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // The server process may still hold the directory a moment after the
                // client disposes. A leftover temp directory is not worth failing a test.
            }
        }
    }

    private static string Document(double kilovolts) => $$"""
    {
      "schemaVersion": "0.1",
      "name": "shared",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [-100, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": {{kilovolts}}, "unit": "kV" }
      },
      "fields": [
        {
          "type": "halfSpaceUniform",
          "planePoint": { "value": [500, 0, 0], "unit": "mm" },
          "inwardNormal": { "value": [1, 0, 0] },
          "capPotential": { "value": {{kilovolts}}, "unit": "kV" },
          "turningDepth": { "value": 200, "unit": "mm" }
        }
      ],
      "detector": {
        "planePoint": { "value": [-100, 0, 0], "unit": "mm" },
        "normal": { "value": [1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 100, "unit": "us" }
      }
    }
    """;

    private string WriteModel()
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, "reflectron.json");

        File.WriteAllText(path, Document(4.0));

        return path;
    }

    /// <summary>The server as a client actually reaches it: a launched process.</summary>
    private static async Task<McpClient> JoinAsync(string modelPath, string as_, string version)
    {
        // The apphost the build produces, not "dotnet <dll>": under `dotnet test` the
        // running process is the test host, which is itself an apphost, so handing it a
        // dll makes it try to launch as a self-contained app and fail.
        var server = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "einzel-mcp.exe" : "einzel-mcp");

        Assert.True(File.Exists(server), $"the server was not copied to {server}");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "einzel",
            Command = server,
            Arguments = [modelPath, "--human", "mike"],
        });

        return await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new Implementation { Name = as_, Version = version },
        });
    }

    private static JsonElement Result(CallToolResult call)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(call.Content)).Text;

        return JsonDocument.Parse(text).RootElement;
    }

    /// <summary>
    /// An agent's edits are signed with the name it declared at initialize, and it has
    /// no way to sign them as anybody else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the reason attribution is not a tool parameter.</b> MCP-1 asks that
    /// mutations be attributed; an <c>author</c> argument would make the attribution
    /// something the mutating party fills in, which is a signature rather than an
    /// attribution — an agent could sign a change as the person it is working with, by
    /// mistake or because a model decided that read better.
    /// </para>
    /// <para>
    /// The client declares itself once, in the handshake, before any tool exists to call.
    /// So the test is in two halves: the name that comes back is the declared one, and
    /// the edit tool's schema has no argument through which a different one could be
    /// offered.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEditIsSignedWithTheNameFromTheHandshake()
    {
        var model = WriteModel();

        await using var client = await JoinAsync(model, "surveyor", "3.1");

        var edit = await client.CallToolAsync("model_edit", new Dictionary<string, object?>
        {
            ["description"] = "raise the beam to 5 kV",
            ["content"] = Document(5.0),
        });

        var author = Result(edit).GetProperty("author").GetString();

        output.WriteLine($"author: {author}");

        Assert.Equal("agent:surveyor/3.1", author);

        // And there is no argument through which it could have claimed otherwise. This
        // half is what makes the first half a property rather than a default: a tool
        // taking an author and ignoring it would pass the assertion above.
        var tools = await client.ListToolsAsync();
        var schema = tools.Single(t => t.Name == "model_edit").JsonSchema;
        var properties = schema.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).ToArray();

        output.WriteLine($"model_edit takes: {string.Join(", ", properties)}");

        Assert.Equal(["description", "content"], properties);
    }

    /// <summary>
    /// The undo stack is shared: an agent reverses the person's edit, and both names
    /// survive in the journal.
    /// </summary>
    /// <remarks>
    /// The human's edit is made through the session directly, because that is what the
    /// shell will do — it drives the session in process, not over a transport. So this is
    /// the actual Loop B arrangement rather than a simulation of it: one journal, one
    /// party in process and one over the wire.
    /// </remarks>
    [Fact]
    public async Task AnAgentOverTheWireReversesAnEditMadeInProcess()
    {
        var model = WriteModel();

        // The person, through the shell: in process, no protocol.
        var shell = new SessionTools(model, new JournalAuthor("mike", AuthorKind.Human));
        shell.Edit(shell.Human, "raise the beam to 6 kV", Document(6.0));

        Assert.Equal(Document(6.0), File.ReadAllText(model));

        // The agent joins the same document. In this test that is a second session over
        // the same file rather than the same object, which is what a stdio client gets;
        // the shell hosting these tools in process shares the object.
        await using var client = await JoinAsync(model, "surveyor", "3.1");

        var read = Result(await client.CallToolAsync("model_read"));

        Assert.Equal(Document(6.0), read.GetProperty("content").GetString());

        var edit = Result(await client.CallToolAsync("model_edit",
            new Dictionary<string, object?>
            {
                ["description"] = "try 7 kV",
                ["content"] = Document(7.0),
            }));

        Assert.Equal(1, edit.GetProperty("sequence").GetInt32());

        var undo = Result(await client.CallToolAsync("model_undo"));

        foreach (var line in undo.GetProperty("journal").EnumerateArray())
        {
            output.WriteLine(line.GetString()!);
        }

        Assert.Contains("agent:surveyor/3.1", undo.GetProperty("author").GetString()!,
            StringComparison.Ordinal);
        Assert.Equal(Document(6.0), File.ReadAllText(model));
    }

    /// <summary>
    /// A refused edit comes back as AGT-3's structured error, not as a transport fault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is refused is a document that does not <em>parse</em>. One that merely fails
    /// validation goes through and is reported, because §16's live validation needs an
    /// invalid state to be reachable and refusing every one also forbids any edit
    /// sequence that passes through one. Taint, never block.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// A refusal is an answer about the model rather than a failure of the call — the
    /// caller asked whether an edit was acceptable and found out it was not — so it is a
    /// tool result. What matters is that the structure survives: the code, the path and
    /// the suggested correction are what an agent acts on without guessing, and a
    /// transport-level fault would keep the sentence and lose all three.
    /// </para>
    /// <para>
    /// And the file must be unchanged, which is asserted rather than assumed. In a shared
    /// session an invalid document is not one party's problem: the person's next action
    /// is against whatever is on disk.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARefusedEditKeepsItsStructureAndLeavesTheFileAlone()
    {
        var model = WriteModel();

        await using var client = await JoinAsync(model, "surveyor", "3.1");

        var refusal = Result(await client.CallToolAsync("model_edit",
            new Dictionary<string, object?>
            {
                ["description"] = "break it",
                ["content"] = "{ not json at all",
            })).GetProperty("error");

        output.WriteLine(refusal.ToString());

        Assert.False(string.IsNullOrWhiteSpace(refusal.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(refusal.GetProperty("path").GetString()));
        Assert.Contains("does not parse",
            refusal.GetProperty("suggestion").GetString()!, StringComparison.Ordinal);

        Assert.Equal(Document(4.0), File.ReadAllText(model));
    }

    /// <summary>
    /// A tool result is the CLI's own JSON for the same command, byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AGT-2 says nothing exists only in one surface and that all three go through the
    /// same command objects. That is easy to claim and easy to drift from — the usual way
    /// being a second serialisation on one side that quietly rounds a number, drops a
    /// null, or reorders a list.
    /// </para>
    /// <para>
    /// Asserting byte equality against <see cref="CommandJson.Write{T}"/> makes the claim
    /// checkable: there is one serialiser and one outcome record behind both surfaces,
    /// so a warning added to a result reaches an MCP client by being on the record rather
    /// than by anyone remembering to copy it across (GRD-2).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AToolResultIsTheSameJsonTheCliEmits()
    {
        var model = WriteModel();

        await using var client = await JoinAsync(model, "surveyor", "3.1");

        var call = await client.CallToolAsync("model_validate");
        var overWire = Assert.IsType<TextContentBlock>(Assert.Single(call.Content)).Text;

        var direct = CommandJson.Write(RunCommand.Validate(model));

        output.WriteLine(overWire);

        Assert.Equal(direct, overWire);
    }

    /// <summary>
    /// An edit written against a document that moved is refused over the wire, and
    /// reading again is what clears it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GRD-9 through the protocol. The person edits the model in their own editor while
    /// the agent is connected; the agent's next whole-document edit would otherwise
    /// overwrite it, since the agent wrote it against what it last read.
    /// </para>
    /// <para>
    /// The refusal has to be recoverable or it is just a wall, so the recovery is
    /// asserted too: `model_read` takes up the outside change, and the same edit then
    /// lands. That is the whole optimistic-concurrency loop, and it is the reason
    /// `model_read` reconciles rather than returning the session's own stale view.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEditAgainstAMovedDocumentIsRefusedAndReadingAgainClearsIt()
    {
        var model = WriteModel();

        await using var client = await JoinAsync(model, "surveyor", "3.1");

        // The person, in their editor, outside the session entirely.
        await File.WriteAllTextAsync(model, Document(9.0));

        var refused = Result(await client.CallToolAsync("model_edit",
            new Dictionary<string, object?>
            {
                ["description"] = "try 6 kV",
                ["content"] = Document(6.0),
            }));

        output.WriteLine(refused.ToString());

        Assert.Contains("changed outside this session",
            refused.GetProperty("error").GetProperty("constraint").GetString()!,
            StringComparison.Ordinal);

        // Their work is untouched.
        Assert.Equal(Document(9.0), await File.ReadAllTextAsync(model));

        // Read again, and the same edit lands.
        var read = Result(await client.CallToolAsync("model_read"));

        Assert.Equal(Document(9.0), read.GetProperty("content").GetString());
        Assert.Contains(read.GetProperty("journal").EnumerateArray(),
            l => l.GetString()!.Contains("outside", StringComparison.Ordinal));

        var edit = Result(await client.CallToolAsync("model_edit",
            new Dictionary<string, object?>
            {
                ["description"] = "try 6 kV",
                ["content"] = Document(6.0),
            }));

        Assert.Equal("agent:surveyor/3.1", edit.GetProperty("author").GetString());
        Assert.Equal(Document(6.0), await File.ReadAllTextAsync(model));
    }

    /// <summary>
    /// The server says what it is for, and what it is not.
    /// </summary>
    /// <remarks>
    /// A client reads the instructions before it reads a tool. The failure this guards
    /// against is an agent treating the session as a remote command line, looking for
    /// verbs that are not here, and concluding the platform lacks them — when the answer
    /// is that they are in the CLI, need no session, and are better there. §15 makes that
    /// a scope decision rather than an omission, so the server should say so.
    /// </remarks>
    [Fact]
    public async Task TheServerSaysWhatItIsForAndWhatItIsNot()
    {
        var model = WriteModel();

        await using var client = await JoinAsync(model, "surveyor", "3.1");

        var instructions = client.ServerInstructions;

        output.WriteLine(instructions);

        Assert.NotNull(instructions);
        Assert.Contains("not a remote command line", instructions, StringComparison.Ordinal);
        Assert.Contains("einzel", instructions, StringComparison.Ordinal);

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).OrderBy(n => n).ToArray();

        output.WriteLine(string.Join(", ", tools));

        Assert.Equal(
            ["model_edit", "model_preview", "model_read", "model_undo", "model_validate",
             "session_journal"],
            tools);
    }

    /// <summary>
    /// An edit that breaks the model succeeds, and says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The regression this guards.</b> The journal used to refuse any edit that did
    /// not validate; §16's live validation required narrowing that to refusing only what
    /// does not parse. That was right for the window, which re-reads the outline after
    /// every edit and shows the result — and it left the agent with nothing: a success
    /// response, no warnings, no validity, for an edit that broke the model.
    /// </para>
    /// <para>
    /// GRD-2's exact subject, and the sixth time evidence about a computation's own
    /// quality has been dropped at a seam here. Narrowing a guard means asking what every
    /// caller will then be told, not just the one being worked on.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEditThatBreaksTheModelSucceedsAndSaysSo()
    {
        var model = WriteModel();

        await using var client = await JoinAsync(model, "surveyor", "3.1");

        // Parses, does not validate: a potential in millimetres.
        var broken = Document(4.0).Replace(
            "\"unit\": \"kV\"", "\"unit\": \"mm\"", StringComparison.Ordinal);

        var edit = Result(await client.CallToolAsync("model_edit",
            new Dictionary<string, object?>
            {
                ["description"] = "a kilovolt in millimetres",
                ["content"] = broken,
            }));

        output.WriteLine(edit.ToString());

        // It applied - taint, never block.
        Assert.Equal(1, edit.GetProperty("sequence").GetInt32());
        Assert.Equal(broken, await File.ReadAllTextAsync(model));

        // And the agent is told, rather than handed an unqualified success.
        Assert.False(edit.GetProperty("valid").GetBoolean());
        Assert.NotEmpty(edit.GetProperty("errors").EnumerateArray().ToArray());

        // Reading says the same, so an agent that did not look at the edit's response
        // still finds out before it acts on the model.
        var read = Result(await client.CallToolAsync("model_read"));

        Assert.False(read.GetProperty("valid").GetBoolean());

        // And undoing it restores a model that validates, which is what makes allowing
        // the edit safe in the first place.
        var undo = Result(await client.CallToolAsync("model_undo"));

        Assert.True(undo.GetProperty("valid").GetBoolean());
    }
}
