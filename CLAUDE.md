# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

The goal is to **build the software described in the specification**: a general, open-source, agent-native ion-optics platform — an open replacement for SIMION. Spec §1's device table spans einzel lenses, quadrupole mass filters, ion funnels, stacked-ring and travelling-wave guides, multipole guides, linear and 3D traps, orthogonal accelerators, reflectrons and MR-TOFs.

**The companion memo's MR-TOF is the first customer, not the design target.** It is a proof of concept that exercises the machinery end to end; the spec's own test of generality is §21 Phase 5 — "a second, unrelated instrument modelled by someone who did not write the code." Nothing device-specific may leak below `Einzel.Library` (architecture invariant 2). When adding capability, ask what it would take for a funnel or a quadrupole, not only for this analyzer.

**Where the work is:** Stages 0 through 4 are complete.

- **Stage 0** — solution, CI, and the three foundational types in `Einzel.Core` that cannot be retrofitted: `Quantity`/`Dimension` (units), `Measured` (the GRD-1 envelope), `EinzelError` (AGT-3).
- **Stage 1** — `Einzel.Transport`: Dormand–Prince 5(4) with per-step error control, Neumaier-compensated time accumulation, analytic field-free drift, exact landing on stop surfaces and declared field discontinuities, and analytic fields (field-free, uniform, retarding half-space). **ACC-1 is demonstrated at ~1e-10 relative against the closed-form single-stage reflectron, four orders inside the 1 ppm budget**, and first-order energy focusing (total field-free path = 4 × penetration depth) is reproduced.
- **Stage 2** — the vertical slice, end to end: model schema v0.1 (`Einzel.Core/Model`), JSON and VTU (`Einzel.Io`), project layout and run manifests (`Einzel.Project`), command objects (`Einzel.Commands`), and the `einzel` CLI. `einzel init` → edit text → `einzel run --vtu` reproduces the analytic 10.1805 µs, writes a manifest, and emits a ParaView trajectory. Cold start ~80 ms against PERF-8's 500 ms.
- **Stage 3** — `Einzel.Fields`: geometric multigrid on a 2D Cartesian grid (red-black Gauss-Seidel, full-weighting restriction, bilinear prolongation, V-cycles), bicubic C¹ interpolation, basis superposition, and `SolvedField2D`. Observed convergence order **1.996–2.000** against the nominal 2 for a five-point stencil; multigrid cycle count is **grid-independent** (8→7→7→7 from 32 to 256 intervals). The solved mirror reproduces the analytic reflectron to **1.3e-13** — the same 10.180505718 µs by two independent routes.
- **Stage 4** — `Einzel.Analysis` (Class T figures of merit: arrival-time peak, resolving power both model-free and Gaussian, transmission, TOF focusing-order coefficients) and `Einzel.Library` (`MirrorProfile`, `PlanarMirror`, `MirrorPair`). The memo's mirror pair is modelled from a solved printed-circuit geometry, and **memo §6 item 1 is answered**.

  | | separation | c1 | c2 | R at ±3% |
  | --- | --- | --- | --- | --- |
  | Single-stage | 290.4 mm | 4.6e-8 | 0.130 | 8,347 |
  | Two-stage, 35% first stage | 767.0 mm | 4.8e-7 | −0.0028 | 316,681 |

  Three things matter more than the numbers. **The four-penetration-depth rule is wrong by 10 mm** — first-order focus is at 290.4 mm, not 300.0 — because the fringe field shifts it; that gap is what solving the geometry buys over assuming it. **R here is energy-aberration only**: no spatial or angular spread, no turn-around time, no detector response, no space charge. So the two-stage result says energy spread stops being the limiting aberration, *not* that the instrument reaches 320k. And **second-order focus costs envelope**: 767 mm cap-to-cap with 1378 mm of drift against the memo's ~420 × 280 × 160 mm shoebox. Buying the aberration back at this mirror depth makes the analyzer too big, which is exactly the trade an optimiser should be pointed at.

- **Stage 5** — `Einzel.Sweeps`: tolerance Monte Carlo, one-at-a-time attribution, sensitivity ranking, FLD-1 sensitivity fields, and both optimisers §13 asks for.

**The FLD-1 spike §23 asked for was run, failed, and now passes.** It failed for a reason §10 does not anticipate: the *physics* is linear where §10 says, but a rasterised boundary made the *discretisation* a staircase function of electrode position — invisible below one cell (derivative identically zero, a study would report the parameter as having **no influence**) and percent-level above one cell, with no step size in between.

**Fixed by a cut-cell (Shortley–Weller) discretisation.** `CutLinks` stores, per node and per direction, how far a conductor surface is as a fraction of a cell and what potential it holds; `CompiledElectrode.FirstEntry` finds the crossing in closed form. The stencil reduces exactly to the five-point formula where nothing is cut. What it bought:

- A planar boundary swept across a whole cell solves to **3.1e-10** of applied at every offset, against up to 1.6e-2 rasterised.
- Curved boundaries converge at **second order** (coaxial log potential, order 2.00/1.95), which makes §19's coaxial closed-form check possible for the first time.
- The shape derivative matches its closed form to **6.5e-6** at a 0.11-cell step, and the FLD-1 residual is now an ordinary Taylor remainder — quadratic in the perturbation to three figures. The limit is (δ/L)², so 1 ppm holds to δ/L ≈ 10⁻³ and the memo's 100–300 µm channels linearise to ≈1e-5. FLD-2 will correctly refuse them at 1 ppm; that is now a legible trade rather than an artefact.
- **Interior-electrode multigrid works.** The coarse mask is rebuilt from geometry rather than projected down, so an electrode too small to hold a coarse node still cuts the links around it: 7–8 cycles at a factor of 0.019–0.023, flat under refinement, against 43–47 at 0.55 before. `Einzel.Sweeps` tests went from 2 m 15 s to 4 s.

Two defects surfaced underneath it, both now fixed and recorded in `docs/lessons.md`. A **Dirichlet domain edge** was a ghost node one cell outside the grid, so the boundary moved outward at every coarsening and a cap plate in a grounded box diverged to 1e50 V once deep coarsening was allowed; `GeometryBuilder` now grounds the edge node itself. And **`Grid2D.OverBox` solved a domain up to 50% taller than declared** — a 60×20 mm box at a 1 mm cell became 60×30 mm, silently — because the y interval count rounded up to a power of two while the x spacing was kept.

**Grids now carry independent spacings per axis** (`SpacingX`/`SpacingY`). Each axis rounds its own interval count up to a power of two from the same requested cell size, so the domain is meshed exactly, neither direction is ever coarser than asked, and the worst cell aspect ratio is 2:1. The stencil scales its y half by (hx/hy)² — exactly 1.0 on a square grid, so isotropic solves are bit-identical. Verified at **observed order 2.00, 2.00, 2.00** on a deliberately 2:1 grid, which is the test that matters: a wrong aspect factor is a wrong Laplacian and it converges contentedly to the wrong answer.

**The optimiser is built** — Nelder–Mead and CMA-ES over the declared parameter surface, behind one `Optimiser.Run`. Everything happens in a normalised box, so a length, a voltage, and a dimensionless ratio all take steps of comparable size; out-of-box candidates are repaired and penalised rather than refused; a failed design is a large *finite* number rather than an infinity, because a simplex reflecting onto infinity learns nothing about which way to go. GRD-1 applies: the optimum is one `Measured` per variable whose interval is the spread of the final simplex or population — a convergence measure saying how *sharply* the optimum is defined, not a confidence interval. New `Evidence.Search(Evaluations, Converged, SpreadSi)` carries it. Three non-suppressible warnings, of which `optimiser.optimum-at-bound` is the one that matters most: an optimum stopped by the box looks identical to a real one.

**The first literature regression that passes.** `Optimiser` recovers the classical round-rod quadrupole ratio — **r/r₀ = 1.14148 ± 3.05e-6** in 45 evaluations against Denison's published 1.1468 — by minimising the 12-pole fraction A₆/A₂, cancelling it 880-fold from nominal. Stable to 0.0016 across sampling radii, so it is a property of the field rather than of the measurement. **It is only measurable because the rod surfaces are cut cells**: a rasterised circle is a staircase and a staircase radiates into exactly the multipoles being measured. The remaining 0.46% is **discretisation**, established by refinement rather than assumed — 1.14148 / 1.14426 / 1.14487 at 16 / 32 / 64 cells per r₀, second order, extrapolating to ≈1.1451, with the grounded housing worth the other ≈0.002. The first guess (that the housing was the whole story) was wrong. Details in `docs/optimisation.md` and `docs/literature-targets.md`.

**Still to point the optimiser at:** the mirror-pair second-order focus. c₂ changes sign across the scan, so the root is bracketed, but each evaluation is ~23 s and a search would run minutes — worth doing as a study rather than a test.

- **The CLI is the primary surface and now has most of §15.** `init | new | validate | estimate | preview | solve | run | sweep | optimise | test | verify | export | schema | templates | examples | agents-md | doctor`, plus the CLI-1..6 contract: `--json` on every verb, results on stdout and diagnostics on stderr, `--dry-run` on every mutating command, distinct exit codes per failure class, deterministic ordering. Cold start 73–147 ms against PERF-8's 500 ms. **Not built: `render`, `ext`, `self-update`** — all three need assemblies that do not exist.

  Four things here are load-bearing rather than plumbing. **`einzel schema` is generated by reflection** over the document records with descriptions from the XML doc comments the build already requires (AGT-7), so the format an agent reads cannot drift from the code; missing XML degrades to structure-without-descriptions and *says so*. **A study is a file** (`schema --study`) naming a figure of merit out of a registry — `flightTime`, `energyDrift`, `resolvingPower`, `transmission` — which is the seam §12's Python objectives will register into. **`verify` separates drift from notes**: an edited model or a changed solver-behaviour version invalidates; a different engine build with identical numerics, or another machine, does not. **`preview` taints the number itself** (GRD-5) and writes nothing, because a tainted result in `results/` would be reported as current by `verify`.

  `einzel init` now scaffolds a schema-0.2 parameterised reflectron *and a test for it*, so `init` → `test` works from the first minute and the expected value is a closed form rather than something this engine produced once.

**A standing literature-regression target**, recorded in `docs/literature-targets.md`: the Stewart/Grinfeld Ion Processor (JASMS 2023, PMC10767742). **Do not confuse this with the memo's Stellar HP/LP LIT pair** — that is existing hardware, a radial-ejection linear ion trap; this is the new rectilinear conjoined collision cell plus transversal pulsed-extraction trap. Memo §6 item 5 is precisely the choice between them, so both are targets. Its Δt* = 0.8–1.2 ns turn-around time across m/z 195–2722 is **a DC problem and reproducible far sooner than the rest**, needing only turn-around-time/emittance figures of merit (§12, missing) and ensemble launching from a thermal distribution. Extraction efficiency, ion capacity, and the pressure gradient are Phase 3. As important as the MR-TOF work, not less.

**LIB-1 is now satisfied.** Schema 0.2 adds a declared parameter surface (named values with units, bounds, descriptions, and derived expressions checked dimensionally) and a `solved2d` field type carrying electrode geometry as data. Device templates are embedded JSON in `Einzel.Library/Templates/`, not classes:

- `planar-mirror-pair.json` — 11 parameters, an edge-profile board pair, a cap, and a declared reflection.
- `quadrupole.json` — four discs. **Shares no code with the mirror at all**, which is the point: adding a device is a new file, not a new class. Verified against the analytic form — Φ(x) = −Φ(y) exactly, and Ex/x constant to **0.17%** across the central 45% of r₀, i.e. a linear restoring force. Ratio to the ideal hyperbolic field is 0.926, the expected round-rod approximation.

Adding an einzel lens or a funnel should need only a fourth file. If it needs a change below `Einzel.Library`, LIB-1 says the abstraction is wrong — believe it.

Two findings from Stage 1 that bear on the spec:

1. **The turning-point step cap (§11) does not help and slightly hurts.** In a smooth field the flight time is at machine precision with 6 steps and marginally worse with 105. §11's rationale is that "position-error controllers under-refine" at the velocity minimum — but `ErrorNorm` weights velocity error with its own absolute floor, so it is not a position-error controller and does not under-refine. The cap is implemented and on by default (`TurningPointStepFactor = 0.01`) to honour the spec as written; the evidence says it should default to 0.
2. **A field discontinuity is a real error source and must be landed on exactly.** Dormand–Prince stage 4 carries the coefficient −56/15, so intermediate stage samples fall outside the step interval and can land on the wrong side of a field jump even when both endpoints are inside. Handling the boundary as an event took the reflectron from 5.5e-10 to 1.7e-16. A residual around 1e-10 remains and behaves as noise rather than as a controlled error — it is an artifact of idealised *discontinuous* analytic fields and should not appear in solved, interpolated fields, but it is what sets the achievable tolerance on finite-difference tests.

The two design documents remain the source of truth. Tracked alongside them: `README.md`, `LICENSE` (Apache 2.0).

- `einzel-software-spec-r06.html` — the software specification, rev 0.6. **The source of truth for every architectural decision below.** Tracked in git. Read the relevant `§` section before proposing or changing design.
- `compact-mrtof-stellar-memo.html` — companion working memo, rev 0.7. The instrument the platform must model first; the spec's acceptance criteria reference it by section (e.g. "memo §6 item 5", "the memo's mirror pair tracked end to end"). Phase 1 is not done until that mirror pair runs at ACC-1. **Gitignored and not published** — it carries the patent and freedom-to-operate analysis and this remote is public, so it exists only in the local working tree. Do not add it to git, and do not quote its patent or competitive analysis into tracked files.

Both are hand-authored, self-contained HTML documents: inline `<style>` blocks over an IBM Plex / CSS-variable palette, figures as inline `<svg>`. Edit the HTML directly; there is no generator and no markdown source. Revisions are new files with a bumped suffix (`-r06` → `-r07`), not in-place overwrites, and the change line at the top of the document records what the revision added.

**Detailed documentation lives in `docs/`** — architecture and the four invariants, the model format in full, device templates, numerics with every measured figure, the lessons from bugs that presented as physics, the CLI contract, validation coverage *and its gaps*, and findings against the specification. Read the relevant page before changing something in that area; it records why things are the way they are, and several of the decisions cost real time to reach.

## Commands

```powershell
dotnet build                                  # warnings are errors; XML docs required on public API
dotnet test                                   # all tests
dotnet test --filter FullyQualifiedName~MeasuredApiSurfaceTests   # one class
dotnet test --filter "FullyQualifiedName~QuantityTests.RoundTripsThroughANamedUnit"  # one test
start <file>.html                             # preview a design document

# The CLI, once built (src/Einzel.Cli/bin/Debug/net10.0/einzel.exe)
einzel init <dir> [--vcs git]                 # create a project
einzel validate models/reflectron.json        # units, bounds, regime validity
einzel run models/reflectron.json --vtu       # run; --vtu writes a ParaView trajectory
einzel run models/reflectron.json --json      # machine-readable, for the agent loop
```

**Two numerical rules that cost real accuracy when broken**, both found by tests that failed for the right reason:

- **A four-by-four interpolation stencil must extrapolate at grid boundaries, never clamp.** Clamping repeats the edge node, which makes the interpolant non-linear in the boundary cell even when the field is exactly linear. An ion enters and leaves a mirror through that cell twice per reflection; a clamped stencil put **7.5 ppm** into a flight time whose exact solution is a pure ramp — over the whole ACC-1 budget. Linear extrapolation of the ghost node took it to 1.9e-10.
- **Measure the interpolant against a *sampled* exact field, never a solved one.** A solved field carries its own O(h²) discretization error, and on a coarse grid that error is larger than the interpolation error it is supposed to be a backdrop for. Comparing against a solved field measures the solver and reports it as the interpolant's — it initially made bicubic look 60× *worse* than bilinear.

`global.json` pins SDK 10.0.400 (`rollForward: latestFeature`), which matters because 8, 9, and 10 are all installed on this machine and the repo must not silently build on an out-of-support runtime. Toolchain per the spec: **C# / .NET 10 (LTS)**, vendored CPython for extensions, ILGPU for GPU paths, WPF (Windows-only) for the shell, everything else cross-platform.

Two build settings are load-bearing rather than stylistic, and both live in `Directory.Build.props`: `TreatWarningsAsErrors` (a 1 ppm engine cannot afford a culture of ignored diagnostics — it caught a sign-extension bug in `Dimension` on the first build) and `GenerateDocumentationFile` with CS1591 unsuppressed, so undocumented public API fails the build. That second one is AGT-7: schema descriptions and CLI help are meant to generate from the same metadata.

The CLI being built is the primary surface and the thing to keep working first (spec §15):

```
einzel init | new --from-example | validate | preview | estimate
einzel solve | run | sweep | test | verify
einzel render section|still|animation | export vtu
einzel ext test|register | schema | templates | examples
einzel agents-md | doctor | self-update
```

CLI contract: `--json` on every verb, results on stdout and diagnostics on stderr, documented distinct exit codes per failure class, `--dry-run` on every mutating command, deterministic output ordering, and cold start to first output under 500 ms with no network call in that path (CLI-1..6, PERF-8).

## Architecture (spec §6)

Assemblies, engine outward:

```
Einzel.Core        model, units, geometry, symmetry, parameters, validation
Einzel.Fields      DC/RF solvers, basis + sensitivity fields, interpolants
Einzel.Transport   integrators, statistical diffusion, collisions, space charge
Einzel.Analysis    figures of merit by accuracy class, spectra, aberrations
Einzel.Sweeps      tolerance Monte Carlo, optimization drivers
Einzel.Library     device templates as DATA, plus a parameterization API
Einzel.Extensions  manifest, schema validation, in-process + sandboxed runners
Einzel.Render      projection, vector emit, frame sequences, VTU export
Einzel.Project     project layout, manifests, drift detection, AGENTS.md generation
Einzel.Commands    command objects: validate/apply/diff/undo/journal/attribution
Einzel.Compute     scalar / SIMD / ILGPU kernel dispatch
Einzel.Io          model format, field import, mesh interchange, export
Einzel.Cli         the primary surface
Einzel.Mcp         live-session server
Einzel.Update      release check, download, staging, version policy
Einzel.Wpf         shell, viewport, panels
```

CLI, MCP server, and WPF shell are **peers, not a stack** — all three drive the same serializable command objects.

Four invariants. Violating one is a design bug, not a shortcut:

1. **No UI type below the shell.** Nothing may reference `Einzel.Wpf`; every assembly above it builds and runs on Linux. `Einzel.Render` must produce a publication figure headlessly in CI with no display attached.
2. **No device class below `Einzel.Library`.** Quadrupole, funnel, LIT, reflectron exist as data templates in the same schema as any other model. If supporting a new device requires a change lower down, it is almost always the abstraction that is wrong (LIB-1).
3. **No extension code inside the engine loop.** Extensions are coarse-grained — whole input in, whole output out, one call per run (EXT-4). The subprocess boundary makes per-step scripting physically impossible rather than merely discouraged.
4. **No GPL dependency in the default build, ever** (LIC-1). GPL functionality (ffmpeg, Gmsh) is invoked out-of-process as a tool the user supplies; its absence degrades a feature, never blocks the platform. Note RND-13: parsing the `.msh` *format* carries no obligation — linking the *library* would.

## Rules that shape almost every implementation decision

The spec tags requirements (`AGT-`, `GRD-`, `PRJ-`, `EXT-`, `REG-`, `ACC-`, `FLD-`, `RND-`, `UPD-`, `CLI-`, `LIC-`, `TST-`). **Cite the tag** when justifying code against the spec. The load-bearing ones:

- **GRD-1, no bare numbers.** Every quantitative result carries value, units, uncertainty/CI, ensemble size or convergence measure, and active warnings. *The API offers no way to obtain the scalar alone.* The absolutism is deliberate: a convenience accessor returning the value would get added by someone and then used everywhere.
- **GRD-2/3, warnings propagate and are not suppressible** above threshold — through engine, command layer, CLI, MCP, exported files, figures, and video.
- **AGT-2, nothing exists only in the shell.** Every window capability is reachable from CLI and MCP through the same command object. The figure composer edits a text render spec that the CLI executes identically.
- **SI internally, units explicit at every boundary.** `{"energy": 4000}` is a validation error, on purpose — unit ambiguity is the commonest source of silent wrongness and an agent building from prose is the likeliest to introduce it.
- **AGT-3, errors are recovery instructions**: machine-readable code, offending path, violated constraint, observed value, suggested correction, severity.
- **PRJ-3, a run manifest fully determines its run** (model hash, seeds, engine version, solver-behaviour version, transport mode, compute path, extension identities, machine). Results are regenerable rather than precious — which is what makes `.einzel/` safe to discard and version control optional (PRJ-4).
- **Taint, never block.** A defective engine version, a preview-tier result, a decimated figure: all keep working and carry a non-suppressible mark (GRD-5, GRD-11, GRD-12, UPD-10/11). The platform never stops you working; it refuses to let a result look cleaner than it is.
- **AGT-8, the environment is stable within a session.** The CLI never touches the network (UPD-2); only the shell checks for updates, only at launch (UPD-1). An agent issuing 300 commands sees one version throughout.
- **REG-2, regime validity is computed, not assumed.** Trajectory integration and statistical diffusion are peer `ITransportMode` implementations. Selecting one outside its validity raises a non-suppressible warning, and in the overlap band running both and reporting the disagreement is a supported operation.
- **RND-8 / TRN-2, never draw trajectories for diffusive transport.** Above ~10⁻² mbar the model computes a density field and no trajectories exist; lines through a funnel depict something the model never computed.
- **Interpolation, not the integrator, dominates timing error.** Trilinear interpolation is forbidden anywhere on a trajectory path; tricubic with continuous first derivatives is the floor, and grid convergence is a first-class test from the first commit (ACC-3).
- **The inner loop allocates nothing.** Particles are structure-of-arrays in pooled `double[]` buffers so GC cannot interrupt a run, and the scalar reference implementation is never deleted or allowed to rot (CMP-1, TST-1).

## A project is a directory (spec §3)

The unit of work is a folder, not a session or a protocol — read files, edit files, run commands, read output. No network, no handshake:

```
models/ extensions/ studies/ figures/ results/ tests/   small, text, tracked
AGENTS.md    generated platform layer + hand-written project guidance
.einzel/     field caches, trajectories, frames — large, binary, regenerable, ignored
```

The platform layer of `AGENTS.md` is **generated (`einzel agents-md`) and version-stamped, never hand-written** — instructions shipped with v1.2 that describe v1.0 behaviour are worse than none, because an agent trusts them and cannot detect the drift.

## Delivery phases (spec §21)

1. **Spine, project, CLI** — model/units/symmetry, DC multigrid solver, basis superposition, tricubic interpolation, adaptive integrator, JSON schema, error taxonomy, result objects with uncertainty, manifests, full CLI, VTU export. Accepts when the memo's mirror pair is tracked end to end at ACC-1 and an agent builds a DC model from prose with nothing but a project directory and the CLI.
2. **Extensions, sweeps, shell, figures** — both extension runners, examples corpus, sensitivity fields, tolerance Monte Carlo, optimization, ILGPU, WPF shell, `Einzel.Render` vector sections, installer and update mechanism.
3. **RF and pressure** — time-domain RF, statistical diffusion, collision models, gas velocity import, sequencer, space charge, Class B analysis.
4. **Traps, animation, MCP** — waveform excitation, trap sequences, animation with non-linear time mapping, live-session server.
5. **Generalize and release** — BEM solver, MSH interchange, CAD import, public repository.

Sequencing principles: seams first (transport mode, symmetry, accuracy class, device library, extension host stubbed in Phase 1 with one implementation behind each); the schema and CLI are Phase 1 deliverables so the agent thesis is de-risked early; VTU export lands in Phase 1 so ParaView supplies the whole visualization story a year before the shell exists.

## A solver limitation to know about

The multigrid V-cycle assumes coarsening preserves the problem. That holds for boundary-only Dirichlet geometries — Stage 3 measured 8→7→7→7 cycles from 32 to 256 intervals — but **not for interior electrodes** such as rods or apertures. An electrode occupies a fixed physical size, so each coarsening halves how many nodes represent it, and past a few levels it is not represented at all; the coarse grid then solves a different problem and its correction, prolonged back, drives the iteration apart. Four discs in a box reached **1e134 V** that way.

Measured convergence factors with interior electrodes degrade with refinement rather than holding steady: 0.028 / 0.061 / 0.141 at 32 / 64 / 128 intervals with a grounded box, and 0.43 at 64 intervals without one. **This is mitigated, not solved.** The shipped templates are sized where it demonstrably converges, `InteriorElectrodeSolveTests` asserts the maximum principle (no potential anywhere may exceed the applied value — the cheapest exact check that a solve has not diverged), and a retention check refuses the clearest dissolving coarsenings. A real fix is Galerkin coarsening or operator-dependent interpolation, and it should happen before anyone solves a large rod geometry.

Two things that did *not* work, so they are not re-tried: agglomerating the mask (fixed if anything in the 3×3 block is) is stable but grows the electrode a cell per level and roughly triples the convergence factor; a flat depth floor stops small grids coarsening at all and cost Stage 3's 32-interval case its multigrid entirely.

## Four numerical rules learned the hard way in Stage 4

Each of these presented as *physics* and turned out to be *numerics*. All four are general, not mirror-specific.

1. **Never declare a discontinuity that is not there.** `SolvedField2D` used to mark its whole domain boundary as a field jump. Where a solve ends in a decayed field there is no jump, and two such phantom surfaces a few microns apart — which is what two abutting solve domains produce — defeat `SuperposedField`'s sign-product tracking: a step crossing both is treated as crossing neither. That cost an ion **2.6e-4 of its energy**, four orders above the ACC-4 budget, and presented as an intermittent transmission loss. Pass `boundaryIsDiscontinuous: false` when the field has decayed at the edge.
2. **A gridded field must cap the step by its own resolution** (`IElectrostaticField.ResolutionLength`). Launch an ion in a field-free region and the local acceleration is ~0, so the step heuristic proposes an enormous step, the embedded error estimate *correctly* agrees it was accurate for a straight line, and the ion sails through both mirrors without sampling them. The step was not inaccurate; it was uninformed.
3. **For a periodic flight, measure one period and multiply — do not stitch legs.** Each leg boundary is a root-find the ion starts exactly on, and 12 of them give 12 chances to miss a crossing and silently return a flight that is 13 half-periods long. Two legs instead of twelve is both more robust and more accurate.
4. **Fixing the drift distance destroys energy focusing.** Stop an MR-TOF at a detector a set distance along the drift and the arrival time is that distance over the drift velocity — dependent only on energy, not on the mirrors. The focusing coefficients say so unmistakably: c1 = −0.500, c2 = 0.3756, c3 = −0.3133 is the Taylor series of 1/√(1+δ), i.e. free flight. Real analyzers fix the *oscillation count*.

## Validation without SIMION

**There is no SIMION licence available** (~$600/yr — its cost is part of why this project exists). Spec §19's cross-code tier is therefore unavailable, and §22's "validation against SIMION takes far longer than budgeted" risk does not apply. Do not plan work against either. What carries the load instead:

- **The analytic tier is the primary reference.** Closed-form fields with exact trajectories: free flight, parallel-plate, ideal single-stage reflectron focusing, Mathieu stability boundaries. Already the sharpest check available and now also the main one.
- **Literature regression is promoted to the main external check.** Published reflectron, MR-TOF, quadrupole, and funnel geometries reproduced against reported performance. These catch conceptual errors that self-consistency cannot.
- **Convergence and cross-mode tiers are unchanged**, and matter more as internal evidence.
- **For the field solver (Stage 3), use a free FEM code out-of-process** — Elmer, FEniCS, or deal.II solving the same Poisson problem — as the independent check on the multigrid solve. Out-of-process comparison, so LIC-1 is untouched.

## Caveats the spec places on itself

Effort estimates, performance targets, regime boundaries, and the numerical error budget are **engineering judgement, not measured values**. Third-party library status, licence terms, MCP SDK capabilities, and CSnakes' Python version support were current at writing and must be re-checked before being committed to. §23 lists decisions still open — treat them as genuinely open rather than inferring an answer from elsewhere in the document. Two are worth resolving early because Phase 2 depends on them: whether Phase 1 spikes the FLD-1 linearity assumption, and what the agent acceptance suite measures.
