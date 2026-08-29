using Einzel.Commands;
using Einzel.Core.Errors;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// One model, two parties, one attributed and linear journal (MCP-1).
/// </summary>
/// <remarks>
/// <para>
/// The specification is blunt about why this exists: the MCP server's "distinct value is
/// shared live state: an agent operating on the model a human has open, with the viewport
/// updating and both parties writing into one attributed journal. Everything else it could
/// do, the CLI does at least as well and with less machinery."
/// </para>
/// <para>
/// So the journal is the substance and the protocol is delivery. These tests are about the
/// two claims MCP-1 makes — that mutations are attributed, and that the undo stack is
/// shared and linear — and none of them needs a server to state.
/// </para>
/// </remarks>
public sealed class SessionJournalTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-journal", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly JournalAuthor Person = new("mike", AuthorKind.Human);
    private static readonly JournalAuthor Robot = new("claude", AuthorKind.Agent);

    /// <summary>A model on disk, at a declared acceleration.</summary>
    private string Model(double kilovolts)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, "reflectron.json");

        File.WriteAllText(path, Document(kilovolts));

        return path;
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

    /// <summary>Every change carries a name and a kind, and the file follows.</summary>
    [Fact]
    public void EveryChangeIsAttributedAndReachesTheFile()
    {
        var journal = new SessionJournal(Model(4.0));

        journal.Apply(Person, "raise the beam to 5 kV", Document(5.0));
        journal.Apply(Robot, "try 6 kV", Document(6.0));

        foreach (var line in journal.Lines())
        {
            output.WriteLine(line);
        }

        Assert.Equal(2, journal.Entries.Count);
        Assert.Equal(Person, journal.Entries[0].Author);
        Assert.Equal(Robot, journal.Entries[1].Author);

        Assert.Equal(AuthorKind.Human, journal.Entries[0].Author.Kind);
        Assert.Equal(AuthorKind.Agent, journal.Entries[1].Author.Kind);

        // The document on disk is what the journal says it is. A journal describing a
        // file nobody has is worse than no journal.
        Assert.Equal(Document(6.0), File.ReadAllText(journal.ModelPath));
        Assert.Equal(Document(6.0), journal.Content);
    }

    /// <summary>
    /// The stack is shared: an agent's undo reverses a human's edit, and says so.
    /// </summary>
    /// <remarks>
    /// This is the half of MCP-1 that a private undo stack cannot provide, and it is the
    /// point rather than a hazard. Two stacks over one document would let each party
    /// reverse changes the other had already built on, and the document would reach a
    /// state neither of them authored.
    /// </remarks>
    [Fact]
    public void AnAgentCanReverseAPersonsEditAndBothNamesSurvive()
    {
        var journal = new SessionJournal(Model(4.0));

        journal.Apply(Person, "raise the beam to 5 kV", Document(5.0));

        var undo = journal.Undo(Robot);

        output.WriteLine(undo.Line());

        // The reverser is the author of the new entry; the original author is named in
        // what it says. An edit that vanished with no record of who removed it is the
        // failure a shared session has and a private one does not.
        Assert.Equal(Robot, undo.Author);
        Assert.Contains("human:mike", undo.Description, StringComparison.Ordinal);
        Assert.Equal(1, undo.Undoes);

        Assert.Equal(Document(4.0), File.ReadAllText(journal.ModelPath));
    }

    /// <summary>Undo appends rather than pops, so the account stays complete.</summary>
    /// <remarks>
    /// A popping stack loses the fact that somebody undid something, and who — which is
    /// exactly what MCP-1 asks to be recorded. Walking back twice appends twice, and the
    /// journal remains an account of what happened rather than of what survived.
    /// </remarks>
    [Fact]
    public void UndoIsRecordedRatherThanErasingWhatItReverses()
    {
        var journal = new SessionJournal(Model(4.0));

        journal.Apply(Person, "5 kV", Document(5.0));
        journal.Apply(Person, "6 kV", Document(6.0));

        journal.Undo(Robot);
        journal.Undo(Robot);

        foreach (var line in journal.Lines())
        {
            output.WriteLine(line);
        }

        // Two edits and two reversals: four entries, not zero.
        Assert.Equal(4, journal.Entries.Count);
        Assert.Equal([false, false, true, true], journal.Entries.Select(e => e.IsUndo));

        // And it walks back in order - the second undo reverses the FIRST edit, not the
        // first undo. Reversing a reversal would be a redo, and this journal is linear.
        Assert.Equal(2, journal.Entries[2].Undoes);
        Assert.Equal(1, journal.Entries[3].Undoes);

        Assert.Equal(Document(4.0), journal.Content);
        Assert.False(journal.CanUndo);
    }

    /// <summary>
    /// It is linear: an edit after an undo has nothing to redo into.
    /// </summary>
    /// <remarks>
    /// What makes the stack linear is that the walk back is over ordinary edits only, so
    /// there is never a second history to choose between. A new edit after an undo simply
    /// appends, and the undone change stays undone and stays on the record.
    /// </remarks>
    [Fact]
    public void AnEditAfterAnUndoLeavesNoBranch()
    {
        var journal = new SessionJournal(Model(4.0));

        journal.Apply(Person, "5 kV", Document(5.0));
        journal.Undo(Robot);
        journal.Apply(Robot, "7 kV instead", Document(7.0));

        Assert.Equal(Document(7.0), journal.Content);

        // Undoing now reverses the 7 kV edit and lands back at 4 - not at 5, which was
        // already taken back. There is no branch in which 5 kV survived.
        journal.Undo(Person);

        Assert.Equal(Document(4.0), journal.Content);
        Assert.False(journal.CanUndo);

        foreach (var line in journal.Lines())
        {
            output.WriteLine(line);
        }
    }

    /// <summary>An edit that does not validate is refused, not staged.</summary>
    /// <remarks>
    /// In a shared session an invalid document is not one party's problem: the other
    /// party's next action is against whatever is on disk. The file must be unchanged
    /// after a refusal, which is asserted rather than assumed — a refusal that had
    /// already written would be the worst of both.
    /// </remarks>
    [Fact]
    public void AnInvalidEditIsRefusedAndTheFileIsUntouched()
    {
        var journal = new SessionJournal(Model(4.0));

        var broken = Document(4.0).Replace(
            "\"unit\": \"kV\"", "\"unit\": \"mm\"", StringComparison.Ordinal);

        var failure = Assert.Throws<EinzelException>(
            () => journal.Apply(Robot, "break it", broken));

        output.WriteLine($"{failure.Error.Path}: {failure.Error.Constraint}");

        Assert.Contains("shared session", failure.Error.Suggestion!, StringComparison.Ordinal);

        Assert.Empty(journal.Entries);
        Assert.Equal(Document(4.0), File.ReadAllText(journal.ModelPath));
    }

    /// <summary>Undoing an empty session says so rather than throwing something opaque.</summary>
    [Fact]
    public void ThereIsNothingToUndoInAFreshSession()
    {
        var journal = new SessionJournal(Model(4.0));

        Assert.False(journal.CanUndo);

        var failure = Assert.Throws<EinzelException>(() => journal.Undo(Person));

        Assert.Equal("/journal", failure.Error.Path);
        Assert.Contains("nothing has been changed", failure.Error.Constraint, StringComparison.Ordinal);
    }
}
