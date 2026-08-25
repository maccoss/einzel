# Agent acceptance

The platform rests on a claim that nothing else here tests: **an agent can drive
it from a folder and a command line, with no tutorials and no window.**

That claim is not obviously true. SIMION has thirty years of forum posts, example
files, and published geometries in the training data of every model anyone would
use. Einzel has none of that. So it has to be able to explain itself — through the
schema, the catalogue, and the error messages — and this is the measurement of
whether it does.

Spec §19 asks for "scripted prose tasks run against an agent given a project
directory, the CLI, and nothing else", with "a separate track measuring whether
agents act on warnings". §23 leaves open **what it measures and what pass rate
gates a release**, and says it needs settling before Phase 1 ends. This page is
that answer.

## The two decisions that shape it

**Score actions, not self-reports.** Asking an agent which warnings it saw
measures whether it can copy a list. Asking whether it widened the search interval
and ran again measures whether it understood. Every check here looks at what the
agent left behind in the project, never at what it said about it.

**Every task carries wrong answers as well as a right one.** A check that passes
the worked solution proves nothing on its own — it has to reject the plausible
mistakes too, or it is testing that a file exists. Each task ships two or three
distractors, each a mistake an agent would credibly make, and CI asserts that all
of them fail.

That second property is the one that decays quietly. A check written against one
wrong answer often accepts a different one, and nothing about a green suite says
so.

## What it measures

Two tracks, tracked separately because they fail differently.

### Capability — can the thing be done at all

| Task | What it discriminates |
| --- | --- |
| `drift-tube` | The floor. No field, no geometry, one closed-form answer. What it really tests is whether the format is discoverable — units on every quantity, the shape of a source and a detector — from the schema and the errors alone |
| `fix-the-units` | Recovery. A seeded model has a quantity in a unit of the wrong dimension; everything needed to repair it is in the error and nowhere else. This measures whether errors are recovery instructions or complaints |
| `quadrupole-from-template` | Whether the catalogue is discoverable. Building a quadrupole from scratch is a day; reproducing one from a shipped template is a minute, and the difference is entirely whether the agent finds out the template exists |
| `which-dimension-binds-first` | The question the tolerance machinery exists for, asked the way an instrument builder asks it. Needs the study format, a figure of merit, and units on a half-width — none of which the prompt names |

### Warnings — is a warning acted on, or reported past

| Task | The trap |
| --- | --- |
| `quote-a-result` | The obvious approach is the wrong one. `preview` is faster, appears in the help before `run`, and gives an answer to four figures that looks entirely quotable — while carrying a mark saying it is not. Scored by whether a manifest exists, because a preview leaves none |
| `optimum-on-a-bound` | The prompt suggests a search interval that does not contain the optimum. The obvious study returns the edge of its own box — a perfectly good number meaning something entirely different from "the best value", and looking identical. Acting on the warning means widening and re-running, which is visible in the study left behind |

A warnings failure is worse than a capability failure and the tracks are separated
for that reason. A capability failure produces no answer. A warnings failure
produces a **confident answer that is wrong**, and nothing downstream can tell.

## What gates a release

Agents are not deterministic, so a task is attempted several times — five is
enough to distinguish "usually works" from "sometimes works" — and the metric is a
rate, not a boolean.

| Gate | Threshold | Why |
| --- | --- | --- |
| Capability pass rate | ≥ 80% | The suite should be hard enough that something fails; a suite everything passes is not measuring |
| Warnings pass rate | ≥ 90% | The failure mode is silent wrongness, so the bar is higher |
| Any task at 0% | blocks | A task nothing ever passes says the platform cannot express something, not that agents are weak |
| Any drop against the previous release | blocks | Same argument the spec makes for cross-version testing: "should I update?" needs an answerable form |

The regression gate matters more than the absolute one. A schema change that makes
a field harder to discover will show up as a rate falling from 90% to 60% long
before it shows up as anything else, and the absolute gate would still be met.

## What it is measuring, and what it is not

**It measures the platform, not the agent.** If a capable model fails a task, the
finding is that the schema or the error message was unclear — which is the thing
to fix. Read the other way round it becomes a leaderboard, and a leaderboard
produces pressure to make the tasks easier.

Two consequences follow. Prompts never name a CLI verb or a JSON key: that would
test whether an agent can follow instructions, which is not in doubt. And the
agent under test never gets the `agents` verb — it gets a project directory and
the rest of the CLI, which is the situation being measured.

## Running it

```
einzel agents tasks                          # the corpus
einzel agents tasks optimum-on-a-bound       # the prompt, alone, for piping to an agent
einzel agents setup optimum-on-a-bound work  # prepare the starting project
#   ... the agent works in work/, with the CLI and nothing else ...
einzel agents score optimum-on-a-bound work  # what it left behind
```

Scoring exits 0 when every check held and 1 otherwise, so a harness can loop
without parsing anything. `--json` gives the full scorecard.

The agent run itself is out of band: it needs a model, a network, and time, and it
does not give the same answer twice. What runs in CI is everything that decides
whether the measurement is worth anything — every task's worked solution scoring
full marks, and every distractor failing.

## Limitations worth stating

**The spec asks for regime-invalid traps and this cannot build one yet.** Regime
validity is about transport mode against pressure, which is Phase 3. Today's
warnings track uses the non-suppressible warnings a DC model can actually
produce — the preview taint and an optimum on a bound. When statistical diffusion
lands, a task whose obvious approach draws trajectories through a funnel at 1 mbar
belongs here, and it will be the sharpest one in the suite.

**Six tasks is a small corpus.** It covers building, repairing, using the
catalogue, studying, and two traps, which is the shape of the thing rather than
its full extent. It should grow with the device library.

**No task exercises RF, collisions, or space charge**, because none of those
exist.
