# Architecture

## Assemblies

Engine outward. Each references only what is above it in this list.

| Assembly | Holds |
| --- | --- |
| `Einzel.Core` | Units, dimensions, geometry, the result envelope, the error taxonomy, the model format and its validation |
| `Einzel.Fields` | DC solvers, interpolants, analytic and solved fields, basis superposition |
| `Einzel.Transport` | Integrators, the trajectory recorder, ion species |
| `Einzel.Analysis` | Figures of merit by accuracy class |
| `Einzel.Library` | Device templates as data, and the loader that enumerates them |
| `Einzel.Io` | Model JSON, VTU export, the wire form of a result |
| `Einzel.Project` | Project layout, run manifests, content hashing |
| `Einzel.Commands` | Command objects: every operation as one serialisable thing |
| `Einzel.Cli` | The primary surface |

Not yet built: `Einzel.Sweeps`, `Einzel.Extensions`, `Einzel.Render`, `Einzel.Compute`, `Einzel.Mcp`, `Einzel.Update`, `Einzel.Wpf`.

The CLI, the future MCP server, and the future shell are **peers, not a stack**.
All three drive the same command objects. That is what makes "nothing exists only
in the window" enforceable rather than aspirational: a capability added to one
surface is added to the command layer, and the others get it for free.

## The four invariants

Violating one is a design bug, not a shortcut.

### 1. No UI type below the shell

Nothing may reference `Einzel.Wpf`, and every assembly above it must build and run
on Linux. CI builds and tests on `ubuntu-latest` *and* `windows-latest` from the
first commit, because an invariant only ever checked on a developer's Windows box
is one that has already been broken by the time anyone notices.

This gets its hardest test when rendering arrives: `Einzel.Render` must produce a
publication figure headlessly, in CI, on a machine with no display.

### 2. No device class below `Einzel.Library`

The engine knows about fields, integrators, and figures of merit. It does not know
what a reflectron is.

This shows up in naming. The primitive an ion mirror is built from is called
`HalfSpaceUniformField` — field-free on one side of a plane, uniformly retarding
on the other — not `IonMirror`. The closed-form reflectron used as a test
reference lives in the *test* project. `ArrivalTimePeak` computes resolving power
without knowing what produced the arrivals.

LIB-1 states the test: if supporting a new device requires a change below
`Einzel.Library`, either it is genuinely novel physics or the abstraction is
wrong, and almost always the second. A mirror pair and a quadrupole currently
share no code at all — they name the same three electrode primitives in different
arrangements. See [Device templates](device-templates.md).

### 3. No extension code inside the engine loop

Extensions are coarse-grained: whole input in, whole output out, one call per run,
never per integration step. The subprocess boundary makes per-step scripting
physically impossible rather than merely discouraged. Not yet built, but the
constraint shapes what the interfaces may look like.

### 4. No GPL dependency in the default build, ever

Where GPL functionality is genuinely useful it is invoked out of process as a tool
the user supplies, and its absence degrades a feature rather than blocking the
platform. Note the distinction that makes this workable: parsing a *format*
carries no obligation; linking a *library* does. `Directory.Packages.props`
carries a licence note on every entry.

## The result envelope

Every quantitative result carries its value, units, uncertainty or confidence
interval, the ensemble size or convergence measure behind it, and any active
warnings. The API offers no way to obtain the value alone.

`Measured` therefore exposes no property or method returning a bare magnitude. The
only route to the value is `Deconstruct`, which hands back the uncertainty, the
evidence, and the warnings in the same call. `MeasuredApiSurfaceTests` enforces
this by reflection, so the rule governs members nobody has written yet — the
concern being that a convenience accessor gets added by someone eventually and
then used everywhere.

To be exact about what is enforced: a caller can still write
`var (v, _, _, _) = result`. Discarding is possible; it is just visible and
greppable rather than the path of least resistance.

The rule survives to the wire. `MeasuredJson` is built *only* by deconstructing a
`Measured`, so no serialiser here can emit a number alone even if someone tried.

Preview status, extension attribution, and defect taint ride in the warning list
at a non-suppressible severity rather than as separate fields, because they
behave identically to validity warnings — one propagation path to get right
instead of four.

## Errors as recovery instructions

An error names a machine-readable code, the offending path as a JSON Pointer, the
constraint violated, the value observed, and a suggested correction.

```json
{
  "code": "UNITS_INCOMPATIBLE",
  "path": "/source/accelerationPotential",
  "constraint": "this field requires a quantity of dimension m^2 kg s^-3 A^-1",
  "observed": { "value": 4, "unit": "mm" },
  "suggestion": "'mm' has dimension m; supply a unit of dimension m^2 kg s^-3 A^-1"
}
```

Validation collects *every* error rather than throwing on the first, because the
recovery an agent wants is the whole list — fixing one unit per round trip is the
behaviour this avoids.

Codes are a compatibility surface that callers branch on. They are added, never
reworded or repurposed.

## A project is a directory

```
models/  studies/  figures/  results/  tests/  extensions/   small, text, tracked
AGENTS.md                                       generated + hand-written guidance
.einzel/                                        caches and trajectories; regenerable
```

Everything defining a modelling effort is small, text, and diffable; everything
large is derived and discardable. `.einzel/` can be deleted without losing
anything, because the run manifest fully determines the run.

Nothing touches version control. A plain folder is the default and fully
supported; `--vcs git` writes an ignore file and changes no behaviour anywhere
else. Requiring a repository would reinstate exactly the barrier the project
exists to remove.

The platform layer of `AGENTS.md` is **generated and version-stamped, never
hand-written**. Instructions shipped with one version that describe another are
worse than none, because an agent trusts them and cannot detect the drift.

## Build settings that are load-bearing

Two things in `Directory.Build.props` are not stylistic.

`TreatWarningsAsErrors` — a numerical engine held to 1 ppm cannot afford a culture
of ignored diagnostics. It caught a sign-extension bug in `Dimension` on the very
first build.

`GenerateDocumentationFile` with CS1591 unsuppressed, so undocumented public API
fails the build. Schema descriptions and CLI help are meant to generate from the
same metadata, which only works if the metadata exists.

`global.json` pins the SDK, because several .NET versions are typically installed
and the repository must not silently build on one that is out of support.
