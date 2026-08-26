# Extensions

Agents must extend the platform, not only drive it. An extension path requiring
C#, a compile and a restart is not usable in a loop, so the language is Python and
the sandbox is paid for architecturally — embedded CPython cannot be sandboxed
in-process.

## The authoring loop

```
einzel ext register shortest        # scaffolds a working extension
einzel ext test shortest --input payload.json
einzel ext list                     # what is installed, and what this build cannot contain
```

`register` writes an extension that **runs immediately**, for the same reason
`einzel init` writes a model that runs: an agent with a template that works can
change one thing and see what happens, where an agent with a stub has to fix
somebody else's code before it can start.

An extension is a folder under `extensions/` with a manifest and a Python file.
Nothing has to be told about it — copying a folder in is a complete install,
because a project is a directory.

## One manifest, two runners

```json
{
  "name": "shortest",
  "version": "0.1.0",
  "kind": "objective",
  "trust": "sandboxed",
  "entry": "extension.py",
  "function": "run",
  "figures": ["resolvingPower", "flightTime"],
  "engineMinimum": "0.1.0",
  "outputSchema": {
    "type": "object",
    "required": ["value"],
    "properties": { "value": { "type": "number" } }
  }
}
```

EXT-1: type, schemas, trust, resource needs, engine range. **The runtime is an
implementation detail of the manifest**, which is the point of having one — the
same declaration can be run two ways without the extension knowing which.

`trust` defaults to **sandboxed** rather than being something a manifest opts into,
because a trust level that has to be asked for is one that gets granted by
accident. The in-process runner (EXT-2, CSnakes) is not built; the subprocess
runner is, and it is the default.

**Five extension points**, all coarse-grained: `geometry`, `analysis`, `objective`,
`sequence`, `interchange`. What is *closed* is per-step physics, the field solver
inner loop, and the integrator — and the process boundary makes that structural
rather than advisory. EXT-4 gives an extension one call per run; a subprocess
cannot be invoked per integration step at any useful rate, so per-step scripting is
not discouraged here, it is impossible.

## What the sandbox does, and what it does not

This is the part to read before running anything agent-authored.

| Measure | Enforced |
| --- | --- |
| Wall-clock timeout, process tree killed | **yes** |
| Output size ceiling | **yes** |
| No inherited environment | **yes** |
| Interpreter isolation (`python -I`) | **yes** |
| Scratch working directory, not the project | **yes** |
| No stdin beyond the payload | **yes** |
| **No network** | **no** |
| **Filesystem confinement** | **no** |
| **Memory and CPU ceilings** | **no** |

EXT-3 asks for job objects and a restricted token on Windows, and namespaces and
seccomp on Linux. Neither is built. What is built is everything that can be done
portably from managed code.

**The gap is stated rather than implied by the word "sandbox".** A containment
measure that is claimed and not applied is worse than one that is absent and known
to be: the first makes someone run untrusted code they would otherwise have read
first. So `extension.isolation-incomplete` is attached to every sandboxed result,
is a **validity violation** (GRD-3 forbids suppressing it), and `einzel ext list`
prints the unenforced list on stderr every time.

The environment scrub is worth its own line: a child that starts with the parent's
environment starts with its credentials, its proxy settings, and its `PYTHONPATH`.
The child sees **zero** environment variables, which a test asserts.

## Attribution, per GRD-6

An extension result carries the extension's name and version and **cannot present
itself as first-party**. A figure of merit computed by somebody's Python stays
distinguishable from one the engine computed however far downstream it travels.

## Output is checked against the declared schema

EXT-7. Without it an extension returning the wrong shape produces a null or a zero
somewhere downstream and the traceback points at the *engine* — which is exactly
the debugging session that makes people stop writing extensions.

`SchemaCheck` implements a deliberate subset of JSON Schema: `type`, `required`,
`properties`, `items`, `enum`, and numeric bounds. That covers what an extension
contract actually says, and the alternative — a dependency implementing the whole
specification including remote `$ref` resolution — would put a network fetch inside
a sandbox whose entire purpose is not having one. Unrecognised keywords are
**ignored rather than refused**, so a richer schema written for a human reader still
validates on the parts this understands.

## The traceback reaches the caller

AGT-3 makes an error a recovery instruction, and the only thing that says what went
wrong inside somebody's Python is their own traceback. It is carried into the
`EinzelError` suggestion, truncated rather than dropped.

## An extension as a figure of merit

Section 13 has an optimiser composing objectives from section 12, which may be
Python extensions. A study names one with an `ext:` prefix:

```json
{ "figureOfMerit": "ext:shortest", "sense": "minimise", ... }
```

A prefix rather than a new field, because a figure of merit is already selected by
name and `ext:` is a namespace rather than a second mechanism — which is what keeps
the optimiser from having to know the difference.

The extension is handed the model's declared parameters in SI and whichever
built-in figures its manifest asks for. **Declared rather than inferred**, because
each ensemble figure flies a cloud and computing all of them for every draw of a
thousand-draw study would spend most of the study on numbers nobody asked for. A
figure that could not be computed is present and `null` rather than absent, so an
extension can tell "this design loses its beam" from "you did not ask for that".

An extension objective is reported dimensionless and under its own name. That is
honest rather than lazy: the extension returns a bare number, and inventing a unit
for it here would be the platform asserting something only the author knows.

## Measured

| | |
| --- | --- |
| Sandboxed round trip, median of five | **49 ms** against PERF-7's 50 ms |
| Environment variables the child sees | 0 |
| Runaway extension killed at a 1200 ms declared timeout | 1276 ms |

The round trip is process start almost entirely, which is why PERF-7 sets the
granularity floor for EXT-4: anything needing to happen more often than that cannot
be an extension.

**A kill has to be waited on.** `Process.Kill` only *asks*: it returns before the
operating system has finished, so a timeout that does not then wait has not bounded
anything - the extension is still running and still holding whatever it had open.
On Windows that showed up immediately as a working directory that could not be
deleted, which is how it was found, on CI rather than locally.

## Not built

- **The in-process runner (EXT-2).** CSnakes is alive and maintained — 1.2.1, some
  450,000 downloads at the time of writing — and its licence has not been checked
  against LIC-1 because nothing depends on it yet. The subprocess runner is the
  default and the security-relevant one, so it came first.
- **A vendored interpreter (EXT-6).** One is *discovered* instead, and `einzel
  doctor` says so rather than passing it off as the vendored path. Discovery means
  an extension behaves differently on a machine with a different Python.

  **This is an installer decision, deferred to the installer.** The likely shape is
  `uv python install --install-dir` into a directory einzel owns under the user's
  data folder: uv is Apache-2.0 or MIT, the standalone CPython builds it fetches are
  permissive, and pointing at a directory einzel owns rather than at uv's shared
  store keeps another tool's `uv python uninstall` from breaking this one. Two
  constraints it has to respect whenever it happens: provisioning downloads, so it
  can never be implicit in a run (UPD-2 forbids the CLI touching the network, AGT-8
  makes the environment stable within a session), and resolution has to stay a
  filesystem lookup so PERF-8's 500 ms cold start survives it.

  **One consequence is already a gap.** A run manifest fully determines its run
  (PRJ-3), and an extension result now depends on which interpreter computed it -
  but the manifest records the engine version, the solver-behaviour version and the
  machine, and not the interpreter. Whatever the installer decides, the manifest has
  to start recording it.
- **Shared-memory transport (EXT-5).** Large arrays should cross by shared memory
  with an Arrow or raw-buffer layout, never by JSON. The small-payload path is
  built, which is what an objective or an analysis extension needs. The manifest
  does not mention the transport, so adding the buffer path later changes no
  extension that does not want it.
- **OS-level isolation.** See the table above.
- **The updater's compatibility report (EXT-8).** The comparison is built and
  `einzel ext list` uses it; the updater that would run it before applying an
  update is not.
- **Geometry, sequence and interchange extension points.** Declared in the manifest
  and refused where a number is expected. Only `objective` and `analysis` are wired
  to anything.

## The cleanest extension of all

The spec's own aside, worth repeating: for model authoring specifically, the best
extension is not an extension. A Python script in `extensions/` that emits a model
document has zero coupling to the engine, needs no sandbox because it never runs
inside the platform, and is trivially testable.
