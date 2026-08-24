# Command line

The primary surface. A peer of the future MCP server and shell, not a layer under
them — all three drive the same command objects, so a capability reachable from
one is reachable from all.

## The loop

No protocol, no session, no network. Read files, edit files, run commands, read
output.

```
einzel init demo                             # create a project
$EDITOR demo/models/reflectron.json          # edit text
einzel validate demo/models/reflectron.json  # instant
einzel run demo/models/reflectron.json --vtu # run, and write a ParaView trajectory
```

## Commands

| Command | Does |
| --- | --- |
| `einzel init [dir] [--vcs git]` | Create a project directory, an example model, and `AGENTS.md` |
| `einzel validate <model.json>` | Units, bounds, dimensions, regime validity. Instant |
| `einzel run <model.json>` | Run; writes a manifest and a result |
| `einzel --version` | Engine version |

| Option | Effect |
| --- | --- |
| `--json` | Machine-readable output, including the full result envelope |
| `--vtu` | Also write the trajectory for ParaView |
| `--project <dir>` | Project root; otherwise inferred by walking up from the model |

Not yet built: `preview`, `estimate`, `solve`, `sweep`, `test`, `verify`,
`render`, `export`, `ext`, `schema`, `templates`, `examples`, `agents-md`,
`doctor`, `self-update`.

## Contract

**Results on stdout, diagnostics on stderr.** A script may pipe stdout to a JSON
parser without filtering.

**Exit codes are distinct per failure class**, so a caller branches without
parsing output:

| Code | Meaning |
| --- | --- |
| 0 | Success — though warnings may still be attached |
| 1 | Validation failure: schema, units, bounds, solvability |
| 2 | Regime violation: the transport mode is outside its validity |
| 3 | Cost-gate refusal |
| 4 | Convergence failure |
| 5 | Engine-pin mismatch |
| 6 | Internal error |

**Output ordering is deterministic**, and number formatting is culture-invariant,
so golden comparisons do not depend on the host locale.

**Startup is fast and offline.** No command touches the network. Measured cold
start to first output is about **80 ms** against a 500 ms budget; arguments are
parsed by hand rather than through a library partly to keep it there.

## What a run prints

```
flight time   10.180506 +/- 0 us
              convergence in integrator tolerance, residual at round-off, no order to resolve
energy drift  3.08E-015 relative (ACC-4 budget 1e-6)
steps         131, 0.2000 m advanced analytically
final x       -100.000000 mm
engine        0.1.0+f69b53a, model sha256:f0b4199...

wrote results\reflectron.manifest.json
wrote .einzel\reflectron.trajectory.vtu
wrote results\reflectron.result.json
```

The value never appears without what qualifies it — not in JSON, and not here.
Warnings print alongside; anything above advisory goes to stderr and cannot be
silenced.

Note "residual at round-off, no order to resolve" rather than a blank: the three
tolerance refinements agreed to the last bit, so there is no convergence order to
report. Printing an empty number would read as a missing measurement rather than a
perfect one.

ASCII `+/-` rather than a plus-minus sign, deliberately: console encoding is not
ours to assume, and a mangled character in a reported uncertainty is the wrong
place to be clever.

## What a failure prints

```
1 problem(s) in .../reflectron.json:

  UNITS_INCOMPATIBLE
    at         /source/accelerationPotential
    constraint this field requires a quantity of dimension m^2 kg s^-3 A^-1
    observed   4 mm
    try        'mm' has dimension m; supply a unit of dimension m^2 kg s^-3 A^-1
```

Every error found is reported, not just the first — the recovery an agent wants
is the whole list.

## Files a run writes

| Path | Contents |
| --- | --- |
| `results/<name>.manifest.json` | Model hash, engine version, solver-behaviour version, transport mode, compute path, machine, timestamp |
| `results/<name>.result.json` | The figures of merit, each as a full envelope |
| `.einzel/<name>.trajectory.vtu` | The sampled trajectory, with provenance in a comment block |

The manifest fully determines the run, which is what makes `.einzel/` safe to
delete and results regenerable rather than precious. It is also what lets drift be
detected in both directions: a stored result can be checked against both the
current model and the currently installed engine, in a plain folder, with no
repository involved.
