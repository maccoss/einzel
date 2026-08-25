# Einzel documentation

Einzel is an open-source, agent-native ion-optics platform — an open replacement
for SIMION. It covers DC and RF ion optics across nine decades of pressure, and
is designed so that an AI agent, not only a human, can author models, run
studies, read results, and extend the platform.

The authoritative design document is `einzel-software-spec-r06.html` at the
repository root. These pages document what has been **built**, why it was built
that way, and what was learned building it. Where the two disagree, the spec
states the intent and these pages state the reality; both are noted.

## Contents

| Page | What it covers |
| --- | --- |
| [Architecture](architecture.md) | Assemblies, layering, the four invariants, and why each exists |
| [Model format](model-format.md) | Schema 0.3 in full: parameters, expressions, fields, electrodes, source clouds |
| [Device templates](device-templates.md) | Writing a new device as data, and the three shipped examples |
| [Numerics](numerics.md) | Integrator, field solver, interpolation, and the accuracy budget |
| [Sweeps and optimisation](optimisation.md) | Tolerance studies, sensitivity fields, Nelder-Mead and CMA-ES |
| [Agent acceptance](agent-acceptance.md) | The prose-task suite, what it measures, and what gates a release |
| [Lessons](lessons.md) | Bugs that presented as physics and were arithmetic |
| [CLI](cli.md) | Command reference, exit codes, and the agent loop |
| [Literature targets](literature-targets.md) | Published instruments to reproduce, and what each needs |
| [Validation](validation.md) | The test tiers, what each proves, and what is not covered |
| [Spec findings](spec-findings.md) | Places where building it revealed something about the specification |

## The two ideas everything follows from

**Open and free.** No licence, no seat count, no barrier to a student or a
collaborator running the same model. This is not only a licensing position: an
agent working against a closed binary infers behaviour from documentation and
guesses the rest, while an agent working against Einzel can read the
implementation.

**Agent-native.** Designed so the loop of *read files, edit files, run commands,
read output* closes in seconds, with no protocol and no session. A project is a
directory. Every capability reachable from a future GUI is reachable from the
command line and from MCP, through the same command objects.

These reinforce each other, and they share a danger. Agent-friendliness makes
wrong answers cheaper to produce: an agent generating fifty plausible transmission
numbers in an afternoon, three of them computed outside the validity of the model
used, is worse than a slow tool that forces a human to look at each one. That is
why the guardrails are engine behaviour rather than advice, and why
[Numerics](numerics.md) and [Validation](validation.md) are as detailed as they
are.

## Current state

Stages 0 through 5 of the delivery plan are complete: units and the result
envelope, the trajectory integrator, the model format and CLI, the field solver,
Class T analysis with device templates, and `Einzel.Sweeps` — tolerance Monte
Carlo, sensitivity ranking, and both optimisers.

Since then: source **ion clouds** and the Class S figures they make possible, a
**space-charge screen** that estimates what is not modelled and warns
non-suppressibly, a **time-domain RF** path that recovers the Mathieu and Meissner
stability boundaries against published values, and **emittance** — which completes
the Class T figures §12 asks for and doubles as a Liouville check on the
integrator.

Not yet built: extensions, rendering, the compute dispatch layer, the MCP server,
the update mechanism, and the shell.

Nothing here is released software. Effort estimates, performance targets, and the
numerical error budget in the specification are engineering judgement rather than
measured values, except where these pages quote a measurement.
