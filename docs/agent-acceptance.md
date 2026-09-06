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

## The harness gives an agent what a real project gives it

`einzel agents setup` creates the directories and the task's starting files, and
for a while it did **not** write `AGENTS.md` — which `einzel init` does. So the
harness handed an agent strictly less than a real user would have, and a pass rate
measured on it understated the platform.

Worse in the other direction: it meant the acceptance suite was the one thing that
never exercised `AGENTS.md`. That file is generated and version-stamped precisely
because guidance describing an older build is worse than none, and this suite is
the only place its drift would show up. Fixed before the suite was first run.

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

## The first run: six agents, six passes

Run once, one attempt per task, against build `0.1.0` - the pre-release numbering, before versions became `YY.feature.patch`. Each agent got the project
directory, the path to the CLI, and its prompt — no repository source, no tests,
no `docs/`, and not the `agents` verb.

| Task | Track | Scored |
| --- | --- | --- |
| `drift-tube` | Capability | pass |
| `fix-the-units` | Capability | pass |
| `quadrupole-from-template` | Capability | pass |
| `which-dimension-binds-first` | Capability | pass |
| `optimum-on-a-bound` | Warnings | pass |
| `quote-a-result` | Warnings | pass |

**6 of 6, on every individual check.** One attempt each, so this is not yet a
rate and the gates above are not yet meetable — five attempts per task is what
they are written against. Read it as "the suite runs end to end and the platform
can do these six things", not as a pass rate.

**A suite everything passes is not measuring**, which the gate table says in so
many words, and it is the honest reading of this table. Two qualifications keep
it from being worthless. The tasks were written before any agent attempted them,
and the distractors CI asserts must fail did fail. And the value delivered was
not the score.

## The second run: six agents, six passes, and two defects in one command

Run against `26.1.0`, one attempt per task again, same protocol: the project directory, the
path to the CLI, and the prompt. **6 of 6.** As before the score is not the point, and this
time the transcripts found two defects in a single verb, both of which had been shipped for
months and neither of which any test written from inside the project would have caught,
because both are about what a command *appears* to do.

**`einzel outline --set --dry-run` wrote the file.** A straight CLI-4 violation - the
contract says the flag says what would be written and writes nothing. Two agents hit it
independently. One had reached for it precisely because they wanted to preview a change
without touching their model; it silently rewrote the model, and they spent three commands
blaming the wrong thing before isolating it. A `--dry-run` that mutates is worse than no
`--dry-run`, because it is the flag somebody uses when they are being careful.

**Repeated `--set` silently applied only the last one.** The option parser is a dictionary,
so a repeated flag overwrote: two edits went in, one came out, exit 0, nothing said. The
agent that used it inspected the result, saw its second edit, and reported the repetition as
an undocumented convenience that worked. It had lost the first edit and did not know.

Both are fixed, with five tests. The command now also says what it wrote, which is what made
the first defect invisible: its output was identical whether it had written or not.

### Three more, now fixed

All three are the same shape as the two above: the platform was arithmetically right and
told the reader something that was not true of their model.

**The cost gate charged seven flights for every three flown.** `estimate` predicted 29 s for
a 2000-draw tolerance sweep that runs in 1.08 s, exited 3, and offered no override. It billed
every figure the study's declared ion count - right for an ensemble, and wrong for
`flightTime`, which is one ion down a three-rung convergence ladder. The agent shrank its
study to get past a number that was twenty-six times too high, which is the gate degrading the
science it exists to protect.

The registry now carries a `FlightBasis` per figure - convergence ladder, one flight,
ensemble, or the declared cloud - and the estimate asks it. The reflectron sweep costs
3 trajectories per evaluation rather than 21, and 4 s rather than 29. An unknown figure falls
back to the ensemble count, which is the conservative direction: over-charging something
nobody has classified is better than under-charging it, since the gate exists to stop a
surprise rather than to permit one. `--threshold <seconds>` moves the gate, GRD-8 having asked
for it to be configurable all along, and the refusal now says in its first clause that the
study is **not blocked** - only `estimate` exits 3, and every study verb runs regardless.
Saying only "this is above the threshold", on an exit code named cost-gate refusal, reads as a
prohibition and was obeyed as one.

**`CONVERGENCE_ORDER_BELOW_NOMINAL` prescribed a finer grid to a model with no grid.** It
fired on 578 of 1505 evaluations of an analytic field, unsuppressibly, recommending a remedy
that model cannot take - and the agent spent a deliberate second pass establishing that the
number it was about to quote was sound. The field knows which it is, so the advice now asks
it: a gridded field is still told that the floor is usually interpolation error and that
refining is the fix, and an analytic one is told there is no grid to refine and that the floor
is the arithmetic itself or a discontinuity the ladder is straddling. The finding is identical
in both; only the remedy differs.

**An analytic half-space has no far side, and nothing said so.** A `halfSpaceUniform` field is
a ramp whose cap potential is what the model says the plate holds at the declared turning
depth, and the arithmetic continues past it. Lower the cap below the beam energy and the ion
turns round *behind* the plate - 5.6 mm behind it at 3600 V against a 4000 V beam - in a
region the document does not describe, with a flight time reported to full precision either
way. The suggested search window in one of the tasks lies entirely in that region.
`field.beyond-declared-depth` now says where the ion actually turned and against what cap.

Two things about it are worth keeping. It is measured at **three sigma of a declared energy
spread** rather than at the nominal ion, because the tail is the population that overshoots
first, and while only the tail is past the plate the effect is a selective loss of the fastest
ions rather than a wrong flight time - the harder thing to notice, and the likelier to be read
as physics. And the overshoot must clear **a millionth of the declared depth** before it is
reported: the gradient is cap over depth, a division, so multiplying it back returns the cap
only to within an ulp or two, and a model deliberately placed at the boundary - which the
scaffolded reflectron is, and says so in its own description - would otherwise trip on which
way that rounding fell.

Fixed with fourteen tests, each checked by mutation.

### And the rest of the list, checked one at a time

Six further observations were carried out of the transcripts as notes rather than
diagnoses. Re-running each against the build settles them, and most do not survive
contact — which is the point of checking before writing them down as defects.

**`outline --set` on a derived parameter does not corrupt the model.** The note said it
injected a `"value": 0` beside the expression, which the schema then calls an error. It
refuses instead, naming the expression, the parameters it is over, and both ways out:
*"editing a derived parameter would edit a consequence, and the two would disagree at the
next resolve."* The file is untouched and still validates.

**A scanned figure of merit is not a bare number, but it does lose its interval.** The
study result carries the figure's name, unit, description and accuracy class once at the
top, and every warning on the ledger; what a row carries is the parameter value and the
figure. That is `FiguresOfMerit.Evaluator`'s documented exception - ranking needs an
ordering and a GRD-1 envelope has none - and the thing that exception once *also* dropped,
the warnings, has been carried since the `WarningLedger` landed. So this is the boundary of
GRD-1 at the study seam rather than a hole in it, and worth stating as such: **a scan tells
you the shape of a curve, not the uncertainty on any point of it.**

**`class` and `basis` were serialising as bare enum integers**, which is real and was found
here rather than in a transcript - `"class": 1, "basis": 0` tells a reader nothing. Both now
carry the house `JsonStringEnumConverter` the extension and render enums already use, so
they read `"Trajectory"` and `"ConvergenceLadder"`.

**`estimate` will cost a study that the verb you meant then refuses**, which is true and
milder than it sounds: hand a scan file to `sweep` and it exits 1 naming the missing
channels, while `estimate` costs it happily. But `estimate`'s own basis line says *"this is
a study: a scan of 3 points over 'capPotential'"*, so it does state which kind of study it
read. Left as it is.

**`schema --study` is one flat object over four study kinds, and that one stands.** All
twenty properties of a sweep, a scan, a boundary search and an optimisation sit at the same
level, with no `oneOf` and no `required`, so nothing in the schema says that `channels`
belongs to a sweep and `scan` to a scan, nor which of them any given study must have. An
agent has to infer the four shapes from the property names. **This is the one open item of
the six** - AGT-7's claim is that the format an agent reads cannot drift from the code, and
this does not drift, it is under-specified. The fix is a `oneOf` over four branches emitted
from the same reflection pass.

**`new --from-example` writes a closed form, not a captured value** - and checking it found
a different defect. The note said the scaffolded expectation pins whatever the engine
produced. It does not: `free-flight` expects 25.451264294677983 µs, and `L / sqrt(2qU/m)`
evaluated independently is 25.451264294677983 µs, to the last digit. What was wrong was the
**description**, which said the ion travels at 39291.5 m/s and arrives in 25.4508 µs against
a true 39290.78 and 25.45126 - a fifth-digit disagreement between an example's prose and its
own expectation, in the model the description itself calls "the model to run first when
checking that an installation works at all".

Every corpus description was then audited the same way: any number in the prose within two
per cent of the expectation but not equal to it. Six hit, five of them correct - four are
the expectation rounded for reading, and two deliberately quote the *measured* value beside
an arithmetic expectation (`gas-flow-carry` at 4904.5 against 5000, `travelling-wave-capture`
at 8.875 against 9.0), which is the comparison those examples exist to make. `free-flight`
was the only error, and it is fixed.


### And one thing the suite cannot currently see

`optimum-on-a-bound` scores whether the bound warning was acted on, which is its job and
which it did. The agent widened the interval, the warning cleared, and the task passed on an
answer of 4025 V - which the agent then argued from three independent routes is wrong, the
focus being at 4000 V. **A task can pass while the platform returns a wrong number**, because
the Warnings track scores the response to the warning rather than the physics. That is the
design working as written, and it is worth knowing the boundary of what a pass means here.

### What the run was actually worth

The score said nothing. The transcripts said a great deal, and roughly twenty
defects came out of six agents attempting six tasks — several of them the kind of
thing no test written from inside the project would have caught, because they are
about what is *discoverable* rather than what is correct.

The four that mattered most, all now fixed:

- **`einzel test` passed with zero tests**, and `einzel solve` reported
  `converged: true` over a model with nothing to solve. Both are vacuous truths
  over an empty collection, and both are the shape of answer that stops an
  investigation.
- **A source sitting inside an electrode validated and solved cleanly** and only
  failed at `run`. An agent asked for a model that validates and solves would have
  shipped one whose ion dies at step zero, with two clean bills of health saying
  otherwise.
- **`run` and `test` computed the same flight time differently** — one
  integration against a three-level convergence study — and disagreed by 1.3e-10,
  five orders in energy drift. So the most obvious workflow there is, quote what
  `run` prints and pin it with `einzel test`, failed for no stated reason.
- **A result was quoted as `10.180506 ± 0 µs`.** The agent refused to publish it
  and measured its own tolerance ladder instead, which is the right instinct and
  should not have been necessary. `±0` is now a floor with a
  `convergence.at-resolution` note attached.

The pattern across all four is the same one the engine keeps finding in itself:
**a result that looks cleaner than it is**. That the agents found four more
instances in an afternoon is the argument for running this regularly, whatever
the score does.

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
