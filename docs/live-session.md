# The live session

One model document, two parties, one attributed and linear journal. This is what
the MCP server is for, and §15 is unusually blunt about how narrow that is:

> Its distinct value is shared live state: an agent operating on the model a human
> has open, with the viewport updating and both parties writing into one attributed
> journal. **Everything else it could do, the CLI does at least as well and with
> less machinery.**

Figure 6 draws the two loops side by side and they are not the same shape. Loop A
is a project folder — read the model, edit it, validate, look at it, test — and the
figure's own caption says it needs "no protocol, no session, no network". Loop B is
a person with the shell open and an agent joining them on the same document.

So the subject here is the session. It is not a second spelling of the CLI, and
the server says so in its own instructions, because the failure to guard against is
an agent treating it as a remote command line, looking for `run` and `sweep` and
`optimise`, not finding them, and concluding the platform cannot do those things.

## What was actually missing

`journal`, `undo` and `attribution` existed only in the `Einzel.Commands` assembly
**description string** — the same "named in a csproj and nowhere else" state
`ITransportMode` was in before its seam was built. So the work was not
*protocol*, it was the thing the protocol is for. A journal only one party can
write to is a file, and a file needs no server.

`SessionJournal` lives in `Einzel.Commands`, where the architecture list already
put it, and `Einzel.Mcp` is delivery on top.

## Shared and linear are two claims

**Shared** means one stack rather than one per party, so an agent's undo can
reverse a person's edit. That is the point rather than a hazard: two private
stacks over one document would let each party reverse changes the other had
already built on, and the document would reach a state neither of them authored.

**Linear** means there is no branch to redo into. It falls out of the walk back
being over ordinary edits only — an undo is itself an entry, and reversing a
reversal would be a redo.

### Undo appends rather than pops

A popping stack loses the fact that somebody undid something, and who — which is
precisely what MCP-1 asks to be recorded. Walking back twice appends twice:

```
   1  human:mike                5 kV
   2  human:mike                6 kV
   3  agent:claude              undo of 2: reverse "6 kV" by human:mike
   4  agent:claude              undo of 1: reverse "5 kV" by human:mike
```

Four entries, not zero. The journal stays an account of what happened rather than
of what survived.

### The whole document, not a patch

A model is meant to stay small and text (PRJ-2), so storing both sides of every
edit is affordable, and what it buys is that **undo needs no inverse operation per
command**. A command that knew how to reverse itself would be a second
implementation of what it does, and the two would part company at the first
command somebody forgot to teach.

It also makes an edit atomic against a concurrent one. A patch applied to a
document that moved underneath it lands somewhere nobody chose; a whole document
either replaces the one it was written against or is visibly a replacement of a
different one.

### In memory, deliberately

A session is live — the shell's window and the agent connected to it — and it ends
when they do. Persisting it would make the journal a second source of truth beside
the model file, where PRJ-4's argument says the durable record of a design is the
document and its git history.

## Human work is never silently lost (GRD-9)

GRD-9 and MCP-1 are the same mechanism stated twice, and building MCP-1 delivered
only part of it. The journal knew about mutations **made through it**. A person
editing the model in their own editor while a session was open had their change
overwritten by the agent's next whole-document edit, with nothing anywhere saying
so — which is the exact words of the requirement, failed.

**The sharper consequence is what an unrecorded change does to undo.** It breaks
the chain: entry *N*'s `After` stops being entry *N+1*'s `Before`. So walking back
lands on a document that predates the person's edit and discards it **as a side
effect of reversing something else**. The agent asks to take back its own change
and takes back theirs too, which is worse than the overwrite because nobody
involved intended anything of the sort.

`Reconcile` reads the file before every mutation and before every read. If it
differs from what the session last saw, it becomes an entry:

```
   1  agent:claude              5 kV
   2  outside                   changed on disk outside this session
   3  human:mike                undo of 2: reverse "changed on disk outside this session" by outside
```

Three decisions in that:

**Attributed to `outside`, not to the person.** Another tool, another session, a
git checkout and the person's editor all look identical from inside the session.
A journal that guesses is worse than one that says it does not know, so
`AuthorKind` has a third case whose honest meaning is *this session does not know
who did this*.

**The edit is refused, not merged.** Recording the change alone would satisfy "not
silently" while still losing the work. The agent's content was written against a
document that no longer exists, so applying it would discard what somebody else
just did. The refusal names the entry the document is now at and says to read
again.

**And the refusal is recoverable, which is why `model_read` reconciles.** Read,
edit from what it now says. A read that returned the session's own stale view
would send the caller round the same refusal for ever. That is ordinary optimistic
concurrency, and the test asserts the whole loop rather than just the refusal.

An outside change that does **not** validate is refused rather than adopted, since
the constructor's invariant is that a session never holds a state no edit through
the journal could have produced.

Checked by mutation: a no-op `Reconcile` fails three of the nine journal tests,
including the undo one.

## Attribution comes from the handshake, not from a parameter

This is the design decision worth keeping.

An `author` argument on the edit tool would make the attribution something the
**mutating party fills in**, which is a signature rather than an attribution — an
agent could sign a change as the person it is working with, by mistake or because
a model decided that read better. MCP-1 asks that mutations *be attributed*, and an
attribution the mutating party chooses does not meet that.

The client declares itself once, in `initialize`, before any tool exists to call.
Every edit in the session is stamped with that, and the agent has no spelling with
which to claim otherwise.

The test is therefore in two halves, and the second is what makes the first a
property rather than a default:

1. the name that comes back is the one declared at initialize —
   `agent:surveyor/3.1`;
2. `model_edit`'s schema has exactly `description` and `content`, so there is no
   argument through which a different name could have been offered.

A tool that took an author and ignored it would pass the first half alone.

A client that declares no name is `agent:unidentified` rather than an error. The
handshake makes the field optional, and refusing the session would trade a vague
attribution for none at all.

## The tools

| | |
| --- | --- |
| `model_read` | the document as it stands, and the journal so far |
| `model_edit` | replace it, attributed, refused if it does not validate |
| `model_undo` | reverse the most recent edit that still stands |
| `session_journal` | who did what, in order |
| `model_validate` | units, bounds, expressions, geometry, regime |
| `model_preview` | AGT-5's cheap tier, permanently labelled |

**A full run is not here**, and the reason is not that it would be hard. It
belongs with the shell, where there is a progress surface and a viewport to put
the answer in. Until then `einzel run` is the better spelling and is one process
launch away. Preview *is* here because AGT-5 built it for exactly this case —
seconds, and a result GRD-5 marks so it cannot be quoted, exported or optimised
against.

### Results are the CLI's own JSON, byte for byte

Every tool returns `CommandJson.Write` of the same outcome record the CLI
serialises for `--json`. That is AGT-2 made literal instead of asserted: there is
one serialiser and one command object behind both surfaces, so they cannot drift.

It carries GRD-2 for free. The warnings are fields on the outcome, so they reach
an MCP client *by being there* rather than by anyone remembering to copy them
across — which is the failure this project has already had three times at other
seams (`FieldAssembly.Build` discarding its `SolveReport`, the sweep evaluator
discarding its warnings, the collision sampler's `BoundExceeded`).

`AToolResultIsTheSameJsonTheCliEmits` asserts the byte equality rather than
trusting it.

### A refusal is a result, not a fault

An `EinzelException` already carries AGT-3's machine-readable code, offending
path, constraint and suggested correction. Letting it surface as a transport-level
fault would keep the sentence and lose all four, and the structure is the part an
agent can act on without guessing.

It is a tool result rather than a protocol error because a refusal is an answer
about the model: the caller asked whether an edit was acceptable and found out
that it was not.

## Transport

stdio, because it works with no shell:

```
einzel-mcp models/reflectron.json --human mike
```

§15 makes streamable HTTP hosted in process by the shell the **primary** transport
and stdio "a convenience". That is the right ordering for a finished platform and
the wrong one to build in: the convenience runs today and the primary needs a
window that does not exist yet. The tools are the same either way, which is the
whole reason `SessionTools` is built above the transport and `SessionServer` only
wraps it.

**`einzel-mcp` is its own executable, not a verb on `einzel`.** Figure 3 puts the
three surfaces side by side as peers, and figure 6 is emphatic that loop A has no
protocol, no session and no network. A `serve` verb inside `einzel` would put a
server in the binary whose distinguishing property is that it is not one.

Nothing but protocol goes to stdout — a stray line there is a malformed message.
That makes CLI-2's convention (results on stdout, diagnostics on stderr) a hard
requirement here rather than a courtesy.

## What the shell will do differently

It hosts the same `SessionTools` **in process**. `SessionJournal` is exposed on
the session for that reason: the window renders the journal in a panel and redraws
the viewport when it changes. A journal only the protocol could see would make the
person the one party in the session who cannot tell what happened.

`AnAgentOverTheWireReversesAnEditMadeInProcess` is that arrangement rather than a
simulation of it — the person's edit goes through the session directly, the
agent's goes over a transport, and they meet in one journal.

## The dependency

The official MCP C# SDK, which §20's table names and marks "verify current version
and licence". Verified rather than remembered:

- `ModelContextProtocol.Core` **2.2.0** declares `Apache-2.0` as an SPDX
  expression in its own nuspec.
- Its whole transitive closure is **ten `Microsoft.Extensions.*` packages, all
  MIT**. LIC-1 is clear.
- `.Core` rather than the full package: it carries the server, the tool
  primitives and the stdio transport, and leaves behind the
  `Microsoft.Extensions.Hosting` integration this does not use.

This is the first non-test dependency the project has taken.

## Two things that cost time

**`Environment.ProcessPath` is not the muxer under `dotnet test`.** The test
launches the real server as a process, and passing `einzel-mcp.dll` to the running
process failed with "Failed to run as a self-contained app" — the running process
is the *test host*, which is itself an apphost, so handing it a dll makes it look
for `hostpolicy.dll` beside itself. The fix is to launch the apphost the build
already produces.

**A manual stdio harness is not a client, and it lies in a specific direction.**
Driving the server by piping a file into it produced *nothing at all* on stdout,
which reads exactly like a server that does not work. With a logger attached the
SDK showed both requests handled and both responses sent. What happens is that a
file on stdin hits EOF immediately, the transport tears down, and the outbound
writes are dropped — a real client holds stdin open and never sees this. The
lesson is the general one: **when a harness and the thing under test disagree,
establish which one is the artefact before changing either.** Half an hour went
into the server before the logger said the server was fine.
