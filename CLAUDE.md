# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read SPEC.md first, and update it last

**`SPEC.md` is the living specification. Read it at the start of every session**,
before proposing or planning anything. It carries all 118 tagged requirements from
`einzel-software-spec-r06.html` with a status and the evidence behind each, the
delivery phases planned against actual, the amendments where building it showed
the original wrong, and a ranked list of what to do next. It answers "where is this
project" in one place, which nothing else here does — this file is a changelog and
the `docs/` pages are per-subsystem.

**Update it in the same change that alters what it says.** Specifically:

- A requirement's status changed → update its register row *and* its evidence. The
  evidence is the point: **Met** means a measurement is named, not that something
  was attempted. Use **Unverified** when a thing plausibly works and nothing
  measures it; it is not a synonym for met.
- The requirement itself turned out to be wrong, incomplete, or right for a reason
  r06 does not give → add an entry to **Amendments**, with the evidence that forced
  it. Do not silently diverge: r06 stays unchanged as the record of intent, and
  every disagreement is written down as a disagreement.
- A §23 open decision got settled → move it and say how.
- The "What to do next" list changed → reorder it, and say why rather than only
  what.

A status page that has drifted is worse than none, because it is trusted. This is
the same argument that makes the platform layer of `AGENTS.md` generated rather
than hand-written, applied to the one document here that is not generated.

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

- **A code review found the 3D CLI verbs were blind, and GRD-2 had a hole at the field seam.** Three things worth keeping:

  **`einzel solve` answered `converged: true`, exit 0, for models it never touched.** It read only `Solve` (2D) and `continue`d past every `Solve3D` element, and `Elements.All(e => e.Converged)` is vacuously true over an empty list. So the verb whose entire job is to report a residual gave a clean bill of health to a field it had skipped — the shape of answer that stops an investigation, which makes it worse than a failure. `solve` now reports **one entry per basis channel** (a driven structure is one solve per spatial pattern, not one per element), names `dimensions` and `channel`, and refuses a model with nothing to solve. `export` writes a **3D `.vti` volume**, one file per channel, because a single file called "the field" would be picking an RF phase without saying which.

  **`FieldAssembly.Build` discarded the `SolveReport` at the one seam every run, study and test passes through** — `elements.Add(BuildField(geometry).Field)`, one character shorter than handling it. A field that stopped short of its tolerance is indistinguishable from one that met it, which is precisely how the q = 0.611 result survived a revision. `BuildReported` now returns the field **and** its warnings; `run` and `preview` carry them onto every number; the bare `Build` **throws** rather than concealing, because a plain field has no envelope to taint and no third option between refusing and hiding. The rule that generalises: **when a computation produces evidence about its own quality, discarding that evidence must not be the shortest spelling.**

  **The coarsening guard asked the wrong spacing.** `Representable` tested `MinimumSpacing` — the *finest* of three axes — against the smallest electrode. Since each axis rounds its own interval count up to a power of two, a 2:1 aspect is ordinary, and the shipped segmented quadrupole descended to a level whose **z cell was 4.875 mm against a 4.587 mm rod radius**: the exact condition the guard exists to refuse, passed because another axis was still fine. Now `MaximumSpacing`. The test written for it first asserted the maximum principle and **passed with the bug restored** — no teeth. What actually discriminates is the convergence factor, 0.213 against 0.303, and only trying it both ways revealed that.

  Also fixed: coarse masks are now **memoised by grid** (the twelve-rod quadrupole was rebuilding an identical mask over a million `Contains` calls per cycle); the coarse-level flag became `GeometryBuilder3D.Coarsener(...)`, a whole function rather than a `bool coarse = false` default whose obvious spelling was the harmful one; a coarse pin can no longer overwrite a grounded face or collide with another electrode's; and `Electrode3D`'s shape switches were **disagreeing about their default arms** — size fell through to a box, centre to a sphere — so a fourth shape would have been sized as one thing and centred as another with no diagnostic. All three named cases now, and a throw for the rest.

- **Statistical diffusion — the second transport mode, and REG-3 measured.** A drift-diffusion solve on a grid, with **mobility as a declared input** (TRN-1) and a **density field as the output** (TRN-2). RND-8's rule that a diffusive region must never be drawn as trajectories now has something to ask: `ITransportMode.ProducesTrajectories`.

  **Scharfetter–Gummel fluxes, for the same reason cut cells mattered in the field solver.** Centred differencing here is not merely less accurate — it oscillates and produces negative densities as soon as drift outruns diffusion, which in a funnel is everywhere, and a negative density is a quantity that has stopped meaning anything.

  | | |
  | --- | --- |
  | Free diffusion vs √(2Dt), three times | **1.0000** each |
  | Drift vs μE | **1.00000** |
  | Boltzmann equilibrium, seeded and evolved | **1.00000** over three decades |
  | Ion conservation, every loss named | 100.0000% |
  | Lowest density at cell Péclet 483 | non-negative |

  **The Boltzmann check is sharp because the scheme is *built* to be exact there**: zero flux gives n₂/n₁ = B(−P)/B(P) = exp(P), and P is precisely qΔφ/kT. The discrete equilibrium *is* the continuous one, so anything worse than a per cent is a bug — and it found one. A first version sampled the drift at the **cell centre** and used it for both faces; where the field varies, two cells sharing a face then disagree about how much crossed it and the scheme stops conserving. A seeded equilibrium drained from the middle at 4.7× per millisecond. **The conservation test passed throughout, because its field was uniform** — a test that passed for a reason that did not generalise.

  **REG-3, measured.** For the comparison to mean anything both modes must describe the *same gas*, so the event-driven side scatters off a declared cross section and the diffusive side takes its mobility from that same cross section via Mason–Schamp. At 1e-2 mbar and 6.2 Td: trajectory **13.2555 ± 1.3584 m/s**, diffusion **13.8418 m/s** — **0.43 standard errors**, between machineries that share nothing but a cross section.

  **And where they legitimately disagree.** The field has to be chosen against E/N, not picked: at 1e-2 mbar the mobility is 9.2 m²/Vs, so 40 V/m is **166 townsend**, deep into field heating. There the trajectory mode gives 265.2 ± 4.1 m/s against a low-field μE of 369.4 — **overstated 1.393×**. The event-driven mode gets it right without being told; the diffusive mode is only as good as the mobility handed to it, which is exactly what TRN-1's "stated field dependence" is for, and `Mobility.IsWithinFit` returns false rather than leaving the caller to work it out.

  **`"mode": "diffusion"` now runs from a model document, and `einzel compare` is REG-3's supported operation.** A source becomes a Gaussian initial density normalised to the declared population, a detector a collecting boundary, an electrode a region the seed is emptied from. Checked against a closed form: μE at 1 mbar is 184.7 m/s, so 38 mm takes **206 µs**, and the run reports **206.6 µs**. A diffusive result has **no flight time** and says so (`transport.no-flight-time`) rather than filling one in — what a density has instead is a transit *distribution*, a transmission, and a spread. Schema **0.4** carries `mobility` and `densityGrid`.

  **Building `compare` surfaced three ways the comparison can be meaningless, none obvious beforehand, all now warned about.** They are the useful output of the increment:
  - **Mismatched mechanism.** Langevin capture on the event-driven side against a Mason–Schamp mobility derived from a *cross section* is rigid-sphere versus polarization — different scattering, so the disagreement is between the two **inputs**.
  - **Incomplete arrival.** A mean transit over the subset that arrived is not a transit time, and the two subsets are not the same ions. At 6.2 Td the drift was so weak that 0% of the density and 70% of the ions arrived, and the modes "disagreed" by 145% — almost all of it the flight-time ceiling.
  - **Unmatched boundaries.** The density grid has edges and a bare trajectory model does not: with no declared geometry an ion flies to the detector however far it wanders, while the density is absorbed at the edge of its box — **89%** of it, in the case that exposed this. The two modes were being asked about *different instruments*, one with walls and one without.

  With those understood, a 1e-2 mbar comparison at 82.8 Td — trajectory 252.1 ± 6.8 µs against diffusion 174.1 µs, 1.45× apart — is not the solvers disagreeing. It is the low-field mobility being outside its fitted range, which `mobility.outside-fit` says on the same report.

  **RND-8 is now enforced rather than stated**: the renderer asks the mode whether it produces trajectories and draws none when it does not, with `render.no-trajectories` on the figure. And a successful diffusive run no longer exits `ConvergenceFailure` — the exit logic only knew `StopConditionMet`.

  **A diffusive run's cost is now known before it starts, exactly.** GRD-8 gates on a number available without doing the work, and for this mode that number is not modelled: the step is set by two stability limits computable from the mesh, the mobility and the field. Predicted **901 steps against 901 actual** (drift-limited, 1 mbar) and **6,266 against 6,266** (diffusion-limited, 1e-2 mbar), because `estimate` and `run` call the *same function* — an estimate computed by a second implementation of the step rule is an estimate of that implementation. Wall time is conservative by ~20% from a measured 2.4 Mcell/s. The drift limit needs the field, so it is included where every element is analytic (free to sample) and omitted with a stated caveat where anything must be solved. **A thinner gas costs more**: a hundredth of the pressure on a six times coarser grid is still twice the work, which the basis line says in words because a number that surprises without explaining itself gets worked around rather than understood.

  **Not built:** an electrode that keeps absorbing rather than only emptying the seed. Details in `docs/pressure.md`.

- **`Einzel.Extensions` — a Python extension surface, and invariant 3 goes from constraint to mechanism.** §5's argument is that agents must extend the platform, not only drive it, and an extension path requiring C#, a compile and a restart is not usable in a loop. `einzel ext register | test | list` is that loop; `register` scaffolds an extension that **runs immediately**, for the same reason `init` writes a model that runs.

  **One manifest, two runners — and only the sandboxed one is built, which is the right one to build first.** `trust` defaults to `sandboxed` rather than being opted into, because a trust level that has to be asked for gets granted by accident. EXT-4 is structural rather than advisory: a subprocess cannot be invoked per integration step at any useful rate, and the measured round trip is **49 ms median against PERF-7's 50 ms**, which is precisely where the granularity floor sits.

  **The sandbox states what it does not do, and that is the load-bearing decision.** Enforced: wall-clock timeout with process-tree kill, output ceiling, **zero inherited environment variables** (asserted — a child inheriting the parent's environment inherits its credentials and proxy settings), `python -I` isolation, a scratch working directory. **Not enforced: network, filesystem, memory** — those need job objects and a restricted token on Windows, namespaces and seccomp on Linux. `extension.isolation-incomplete` is a **non-suppressible validity violation** on every sandboxed result, and `ext list` prints the gap on stderr every time. A containment measure claimed and not applied is worse than one absent and known to be: the first makes someone run untrusted code they would otherwise have read.

  **EXT-7's schema check is a deliberate subset** — type, required, properties, items, enum, numeric bounds — because a full JSON Schema dependency would put remote `$ref` resolution inside a sandbox whose entire point is having no network. Unrecognised keywords are ignored rather than refused. **The extension's own traceback reaches the caller** (AGT-3), because "the extension failed" is the message that stops people writing a second one.

  **§13's sentence is now true**: a study naming `ext:name` gets a Python objective the optimiser drives without knowing the difference. A prefix rather than a new field, since a figure of merit is already selected by name. The extension is handed the model's parameters in SI plus whichever built-in figures its manifest **declares** — declared rather than inferred, because each ensemble figure flies a cloud and computing all of them for every draw of a thousand-draw study would spend the study on numbers nobody asked for. A figure that could not be computed is `null` rather than absent, so "lost the beam" is distinguishable from "did not ask".

  **The manifest records which interpreter ran an extension** (PRJ-3), and `null` when none did — an interpreter that took no part in a run is not provenance, and recording it would imply it mattered. Studies also write a manifest now: they wrote results and **no manifest at all**, which is GRD-7 missing rather than thin, and a sweep is exactly the operation whose thousand draws are worth being able to regenerate.

  **Not built:** the in-process CSnakes runner (checked as promised — 1.2.1, ~450k downloads, alive; its licence is unchecked against LIC-1 because nothing depends on it yet), a **vendored interpreter** (EXT-6 — one is *discovered*, and `doctor` says so rather than passing it off), shared-memory transport for large arrays (EXT-5), OS-level isolation, and the geometry/sequence/interchange extension points. Details in `docs/extensions.md`.

- **Pressure: ions fly through gas, and REG-2 is enforced rather than documented.** Two event-driven collision models — **hard sphere** below ~1e-5 mbar (residual-gas scattering, the arrival-time pedestal) and **Langevin** from there to ~1e-2 mbar (trap and guide damping, thermalization). Schema **0.4** adds a `gas` block to `transport`; absent means vacuum, and a vacuum flight is **bit-identical** with and without a sampler attached, asserted rather than assumed.

  **Three literature-grade checks, none of which this code produced.** The Langevin rate coefficient against its closed form (5.998e-10 cm³/s for m/z 500 in N₂, where every published value clusters); the scheduled collision rate against n·σ·⟨g⟩ at **0.9995** over 12,076 collisions; **thermalization to (3/2)kT at 0.9524** after 288 collisions — the sharpest, because equipartition is exact and is not something the code knows, and it tests the kinematics, the Maxwellian draw and the isotropy at once; and **Mason–Schamp low-field mobility at 1.0127 ± 0.0373**, a 0.34σ discrepancy, where the ions get their drift by colliding and the closed form moves nothing.

  **That last test taught a lesson about its own precision.** A first version used 40 ions and reported 0.935, which reads as a 6.5% discrepancy. It was **one and a half standard errors**: a drifting ion also diffuses, and over that flight the diffusion length is comparable to the drift, so a single ion carries ~45% of spread. It now computes its own standard error and asserts against that rather than a band chosen to fit.

  **The Langevin rate does not contain the speed** — the capture cross-section goes as 1/v and the rate is the product — so it is a plain exponential draw, while hard spheres need the **null-collision method** with a bound five thermal speeds above the true rate. A bound that is ever exceeded biases the rate low, so it is *reported* rather than hidden.

  **Two integrator changes a gas forced.** A collision is an instant, so it lands like a sequencer switch — the time is known, the step is cut to it, no root-find. And **the analytic field-free drift had to be bounded by it rather than disabled**: a drift jumps over every scheduled collision, and that is precisely the path a long flight in a thin gas takes. Bounding is *correct*, not convenient — between two collisions the motion really is a straight line, so the analytic advance is the exact solution over a shorter interval. Separately, **the turning-point step cap is fatal in a gas for the same reason it is fatal in a driven field**: an ion thermalising sits at a velocity minimum permanently, and an ion drifting in 1 mbar underflowed after eight steps and 32 ns of a 300 µs flight, reporting `StepSizeUnderflow` — a numerical failure standing in for ordinary physics.

  **REG-1's seam now exists** (`ITransportMode`), with `DiffusiveTransport` declared, `IsAvailable = false`, and refused by name with what it would need. It was only ever named in a csproj description before. `ProducesTrajectories` is on the interface so a renderer *asks* rather than inferring from pressure, which is what TRN-2/RND-8 need to be checkable. **REG-2** computes Knudsen, mean free path, collisions per flight and collisions per RF cycle on every run and reports them **whether or not anything crosses a threshold** — a reader who sees Kn = 40 knows the run was checked; one who sees nothing cannot tell that from its not having been checked. Six non-suppressible warnings, and a **regime violation gets its own exit code** (2, not 4): an ion that never arrives at 1 mbar has not failed to converge, it has been described by the wrong physics.

  **The funnel, finally with gas.** An ion entering 6 mm off axis: vacuum and 1e-3 mbar are **bit-identical (0 collisions)**; at 1e-1 mbar, 1455 collisions take radial speed from **864.6 to 255.7 m/s** and exit radius from 1.59 to 0.42 mm. That is a funnel doing what a funnel is for, and it did not exist before. Two awkward things it also shows: the damping only appears **at or above the validity boundary of the mode computing it**, and at 1e-1 mbar the ion is damped axially too and never leaves — a real funnel is pushed through by a gas *flow*, and a stationary gas has no such push. At the 2 mbar a funnel actually runs at: mean free path **21.4 µm**, Kn **0.0143**, **29 collisions per RF cycle** — an ion that never completes an oscillation, so the pseudopotential the device is designed around does not exist for it.

  **Not built:** statistical diffusion (the density-field mode above 1e-2 mbar, which is why every funnel number is still a lower bound), a neutral velocity *field*, inelastic channels, and pressure gradients. Details in `docs/pressure.md`.

- **`Einzel.Render` — vector sections, and invariant 1's hardest test now passes.** RND-1 makes rendering an engine capability rather than a shell feature, so `einzel render section` and a future figure composer are peer consumers of one pipeline. `Einzel.Render.Tests` draws real templates as SVG and PDF on the Linux runner, where there is **no display, no window manager and no font server** — there is no drawing surface anywhere in the pipeline and no font measurement, because a scene is a list of paths and text runs.

  **A conductor is drawn as the zero level set of its own signed distance**, which the model format already requires for the solver and the ion absorber. One marching-squares routine draws every conductor there is, and the *same* routine draws equipotentials, because an equipotential is a level set too. A shape added to the format needs no change in this assembly — invariant 2 in a new place.

  **Decimation is a guarantee, not a hint** (RND-5, ACC-7 at 0.1% of extent, recorded per GRD-12 in the file, the `--json` result, and stamped on the page). Ramer–Douglas–Peucker, measured tight against its bound: 4,000 points → 577 at a worst deviation of 0.010000 mm against 0.01. The point-to-segment distance is **clamped**, and a reflectron is why — an ion that turns round has its turning point almost on the chord between the ends of the flight, so measuring to the *infinite line* decimates the reflection away and draws an ion flying straight through a mirror.

  **Sample finely, then decimate.** The trajectory is flown twice — once at the model's cadence to learn the flight time, then at a cadence chosen from that — because drawing whatever the model samples for VTU gave the einzel lens a three-segment curve through a focusing element. That a ray *still* decimates to three points is the right answer: straight in, kink, straight out is what a thin lens does.

  **A tainted figure is visibly tainted** (RND-11/GRD-5): a hatched rule the width of the page and a `QUALIFIED` line naming the code, because a figure is the artifact most likely to be shown with none of the uncertainty apparatus attached. **Text stays text** in both formats (RND-6), so a figure can be relabelled for another venue without regenerating it.

  **The PDF writer is hand-written, and LIC-1 is the reason** as much as weight: the capable .NET PDF libraries are variously GPL, AGPL or dual-licensed in a way that needs re-checking per release, and a figure writer is not where this project wants a licence question. Base-14 Helvetica, nothing embedded; a test walks every cross-reference offset and asserts it lands on the object it names.

  **What marching squares got wrong first:** cases 1 and 14 both emit `(Left, Bottom)`, so segment orientation is *not* consistent and a head-to-tail join breaks the contour at every complementary crossing. A rectangular conductor came out as four runs instead of one — identical on screen until the path is filled or dashed, and not free: joining undirected took the lens from 10 conductor runs to 6, the quadrupole's equipotentials from 338 paths to 28, and its PDF from **112 KB to 13 KB** for the same drawing.

  **Not built:** `render still` (raster — nothing here rasterises) and `render animation` (needs RND-7's explicit non-linear time mapping, displayed throughout playback). Both are named by the CLI and refused with a reason rather than falling through as unknown verbs, because "not built yet" and "you spelled it wrong" are different problems. Dimensioned callouts now exist - see below. Details in `docs/rendering.md`.

- **The CLI is the primary surface and now has most of §15.** `init | new | validate | estimate | preview | solve | run | compare | sweep | optimise | test | verify | export | render | ext | schema | templates | examples | agents-md | doctor`, plus the CLI-1..6 contract: `--json` on every verb, results on stdout and diagnostics on stderr, `--dry-run` on every mutating command, distinct exit codes per failure class, deterministic ordering. Cold start 73–147 ms against PERF-8's 500 ms. **Not built: `self-update`**, which needs `Einzel.Update`. `render section`, `render animation` and `ext list|test|register` now exist; `render still` and the in-process extension runner do not.

  Four things here are load-bearing rather than plumbing. **`einzel schema` is generated by reflection** over the document records with descriptions from the XML doc comments the build already requires (AGT-7), so the format an agent reads cannot drift from the code; missing XML degrades to structure-without-descriptions and *says so*. **A study is a file** (`schema --study`) naming a figure of merit out of a registry — `flightTime`, `energyDrift`, `resolvingPower`, `transmission` — which is the seam §12's Python objectives will register into. **`verify` separates drift from notes**: an edited model or a changed solver-behaviour version invalidates; a different engine build with identical numerics, or another machine, does not. **`preview` taints the number itself** (GRD-5) and writes nothing, because a tainted result in `results/` would be reported as current by `verify`.

  `einzel init` now scaffolds a schema-0.2 parameterised reflectron *and a test for it*, so `init` → `test` works from the first minute and the expected value is a closed form rather than something this engine produced once.

- **Ion clouds, and with them Class S.** A source may declare a cloud — ion count, seed, temperature, transverse and longitudinal width, energy spread — and every figure of merit computed over it becomes a property of the *instrument* rather than of one ion. That removes three of the four caveats every resolving power here carried. **Turn-around time** is the sharpest check: FWHM = 2√(2ln2)√(mkT)/qE, matched to **0.49%** on 4000 ions, 0.54/0.87/2.04 ns at m/z 195/500/2722. Schema **0.3**; every spread defaults to zero so nothing existing changes. No angular-divergence knob on purpose — a thermal cloud already has one, and offering both lets a document say two things about the same physics.

  Two things the measurement taught rather than confirmed. **`run` reports two peak widths, both named**, because the model-free central half and the Gaussian-equivalent FWHM disagree whenever the peak has a tail (skew +3.27 on the shipped reflectron), and printing one beside a resolving power computed from the other invites the wrong reconciliation. And **a first-order energy focus suppresses thermal spread quadratically** — sixteen times the temperature gives sixteen times the width, not four, because the first-order term is cancelled by construction. A test written expecting √T was wrong and the measurement corrected it.

  **An open question raised by being able to compute it:** the Ion Processor paper reports Δt* roughly constant at 0.8–1.2 ns across m/z 195–2722, while thermal turn-around goes as √m and would spread 3.7×. Recorded in `docs/literature-targets.md` as a question to settle against the source, not a discrepancy to claim.

- **The RF path exists and the stability diagram is recovered**, which §19 calls "the best single test that the RF path is correct". Time threads through every Dormand–Prince stage (each at its own c coefficient); `ITimeVaryingField` is separate from `IElectrostaticField` because a static field conserves energy and can be sampled in any order and a driven one does neither. **Three literature numbers, none from this code:** sinusoidal cut-off **q = 0.90684** vs a tabulated 0.90804; square-wave **q = 0.71113** vs a published 0.712 (Schrader/Anderson/Russell, JASMS 2024); and a digital working point **a = 0.2630** vs their 0.2640 at 61.15/38.85 duty. The a–q diagram's shape comes out too, closing toward the apex at a = 0.237, q = 0.706.

  RF cost far less than expected because **basis superposition already was the mechanism** — the field is linear in the applied potentials, so making the weights functions of time *is* RF with nothing re-solved. Two things it broke, both instructive. **The turning-point step cap (§11) is fatal in a driven field**: an oscillating ion is at a velocity minimum twice per cycle, so the cap fires continuously — nineteen steps and a quarter of a cycle before underflow. It is disabled for driven fields, which strengthens the existing finding that the cap "does not help and slightly hurts". And **energy drift stops being a diagnostic** (a driven field does work deliberately), so it reports NaN rather than a number that looks like a diagnostic and means nothing; refinement replaces it. A static field is bit-unchanged, asserted not assumed.

  **Not yet:** no arbitrary-waveform library — a drive is a sinusoid or a rectangular wave, and §9 lists an arbitrary waveform as one of the excitations an electrode may carry.

- **Emittance, which completes the Class T figures §12 asks for.** The phase-space area a packet occupies — position against divergence — and the quantity that says whether it will fit through the next aperture, since optics trade size against divergence and cannot reduce the product. Reported in both transverse planes about the packet's own mean velocity (so a mirror or a deflector needs no special handling), with the Twiss orientation that says which side of the waist the detector is on. Checked against σ_x·√(kT/m)/v to **0.77%** on 6,000 ions.

  **It is also an integrator check, and a sharper one than expected.** Liouville's theorem says a conservative force cannot change phase-space area, and this is a conserved quantity *independent of energy* — energy conservation is blind to a map that shears phase space. A field-free drift preserves it to **1.5e-14**, an ideal thin lens to **8.1e-15**, and the same lens with a cubic term grows it by 1.58×, which is the control that makes the other two mean something.

  **Two findings came out of insisting the normalised form be exactly invariant**, both in `docs/lessons.md`. Written the conventional way — geometric emittance times βγ — it drifted **8.1e-7** across an accelerating stage, because a divergence is measured against *axial* speed while βγ is built from *total* speed and the paraxial term between them shrinks as the beam damps. Measuring the area against transverse momentum directly fixes that. The residual **2.1e-8** was then exactly γ−1 at the exit speed: the analysis was relativistic while transport is Newtonian, so the two disagreed about whether v_y or γv_y is conserved. Dropping γ took it to **3.0e-16**. What that gives up is bounded and remote — γ−1 reaches 1 ppm near 460 keV for m/z 500 — but if a relativistic transport mode is ever added the term must come back **in both places at once**.

  A third, smaller one: a cloud with spatial spread and no temperature is perfectly parallel, so its emittance is exactly zero and its Twiss α is *undefined*. That reached the serialiser as NaN and took the whole `--json` document down — the second time that exact failure has happened here. The fix that generalises is that an undefined measurement is **absent, not zero**, because zero is a real answer and a reader cannot tell the two apart if both print as zero.

- **Space charge: the screen, and the distinction it forced.** `ions` (how many trajectories to compute — numerical) and `population` (how many ions are physically in the packet — what pushes on itself) are now separate fields; `population` defaults to `ions`, the conservative reading, so a dense packet is never silently sparse. A run reports the flight-time error the packet's own charge implies whether or not it crosses a threshold, warns **non-suppressibly** past ACC-1's 1 ppm, and names the population the packet could hold within budget.

  **The screen said a 0.5 mm packet at 4 kV holds ~5,600 ions within 1 ppm. It holds about ten**, and finding that is what building the reference method bought.

  The old conversion was `timingFraction = ½ × selfPotential / beamEnergy` — a fractional energy spread, halved because time goes as the inverse square root of energy. That is a real mechanism (ions extracted from different depths of the self-potential well leave with different energies) and it is **not the one that dominates in flight**. What dominates is that the packet *expands*: the self-field pushes for the whole drift, and the relative speed it imparts comes from converting the self-potential into **relative** kinetic energy, √(2qφ/m), not from perturbing a beam energy thousands of times larger. For 40,000 ions in a 0.5 mm ball at 4 kV those are 149 m/s against a beam speed of 39,291 m/s — 3.8e-3, where the old formula said 7.2e-6. **A factor of 527, in the unsafe direction, in a number documented as an upper bound.**

  The estimate now takes the flight time and reports `min(a₀T, √(2qφ/m))/v`, where `a₀ = 2qφ/(mR)` is the surface acceleration: linear while the packet has not had time to expand, saturating at the escape value after. Both branches are needed — the escape value alone would report two ions half a millimetre apart as catastrophic, and eventually is 200 times the flight. `PopulationLimit` inverts it, and the inversion takes the **maximum** of the two branches rather than the minimum, because `min(a,b)` is within budget as soon as *either* is; taking the minimum looked symmetric with the forward direction and reported a limit of three thousandths of an ion.

  Two caveats stated rather than buried. The uniform-sphere model is **harsh for a real bunch**, which is thin in one direction and much less dense than a ball of the same extent — the correction is to the *conversion*, not to the geometry model, and the geometry model was always an order-of-magnitude screen. And a first-order energy focus partly undoes it, measurably: see below.

- **Space charge is now modelled, not only screened** — `Einzel.Transport.Interaction`, the direct pairwise sum SC-1 names as the reference an approximate method is validated against. Built first because an approximation cannot be validated against something that does not exist, and useful in its own right: a pulsed-extraction packet is thousands of ions, not the 10²⁰ that particle-in-cell exists for.

  `CoulombInteraction` sums every pair with macroparticle weighting — a packet of 10,000 modelled by 500 trajectories gives each 20 ions' worth of charge *and* mass, so charge-to-mass is unchanged and motion in the applied field is bit-identical; the weight touches only the mutual force. Plummer softening at the mean macroparticle spacing, **reported rather than hidden**, because the force between two macroparticles closer than that is deliberately not the Coulomb force.

  `PacketIntegrator` inverts the loop: the ordinary path flies ion 1 to its detector before ion 2 is launched, so there is no instant at which both exist and it is *structurally* incapable of space charge. The step is **shared and has to be** — a mutual force between ions at different times is not a force between anything — and Dormand–Prince is evaluated a stage at a time across the packet, because the mutual part of stage 3's field depends on where the others are at stage 3. Written beside the single-ion path rather than by generalising it, the same choice the 3-D solver made and for the same reason.

  What it gives up, stated: it **cannot land exactly on a declared field discontinuity**, because a shared step cannot land on a surface each macroparticle reaches at its own instant. It caps the step short of the first arrival instead — the honest weaker guarantee, and why this is the reference method for space charge rather than a replacement for the path that carries ACC-1.

  | Check | Result |
  | --- | --- |
  | Newton's third law, every stage of every step | balanced to 1e-14 of the acceleration scale |
  | Direct sum vs the uniform-sphere closed form | within 5% at 4,000 points (sampling noise, 1/√N) |
  | Two ions from rest vs energy conservation | 1e-6 |
  | No interaction vs the analytic reflectron | 1e-6 (linear landing, no analytic drift) |
  | Free-flight widening vs the corrected screen | within a factor of 3, screen bounding |

  **The momentum invariant had to be rewritten before it meant anything.** Total packet momentum is conserved only in free flight with nothing absorbed: an applied field is an external force and a detector removes momentum with the ion carrying it. Asserting on it was asserting that mirrors do not reflect. What is checked instead is the *interaction's* own balance at every stage — exactly true whatever the applied field is doing, and where an indexing error over absorbed members would show.

  **And a result that looked like a sign error and is not: a reflectron at first-order focus makes space charge better.** The mutual push correlates position with energy — the ion at the front is accelerated along its travel, the one at the back retarded — and a leading, faster ion penetrates the mirror deeper and spends longer in it. That is the compensation a reflectron exists to provide, applied to a spread the packet's own charge created. Measured at 24.9 ns free against 11.3 ns pushed. A plausible story is not a finding, so it is **tested by detuning**: move the drift length off the focusing condition and the compensation has to weaken, which it does. An integrator with a sign wrong would narrow the packet at every drift length.

  **Wired to the model format as schema 0.5**: `"transport": { "spaceCharge": "direct" }`. A string rather than a flag, because particle-in-cell will be a third value. **No new field was needed for the weighting** — `ions` (trajectories computed) and `population` (ions physically present) already meant exactly macroparticles and real ions, and two fields that must agree would have been one too many.

  Three ways to ask for it and not get it are **refused rather than run**, because each would produce a result that looks like the one asked for: fewer than two trajectories (nobody to push on), a cloud with no spatial spread (an unbounded self-field, not a large one), and a declared gas (the packet advances in lockstep and has no collision hook, so the gas would take no part). And **`einzel estimate` states the cost in words as well as in a number** (GRD-8) — 150 trajectories through the shipped trap take 87 s and 2,000 would take about four hours, so the linear intuition is exactly wrong and saying "quadratic" is worth more than the figure.

  Two things the wiring caught. **Turn-around time inherited the setting and came back as 0.000 ns**: it works by flying a thermal-only cloud with every other spread switched off, which has no spatial extent by construction — so its self-field is unbounded, the packet blew up, too few ions arrived, and the catch returned zero. It reads exactly like a measurement. The sub-model now forces `none`, which is also the physically right answer: turn-around is the temperature's contribution alone. And **a 20 mm test drift showed a 0.2% change**, which would have passed on a build where the interaction was never wired up — a switch that runs different code and produces the same number is not a feature. The test drifts half a metre.

  **Still to build:** particle-in-cell as the *approximate* method — deposit to a grid, one multigrid solve with a non-zero source, gather back. The existing multigrid is the right machine, and now there is something to validate it against.

- **The agent acceptance suite exists** (`docs/agent-acceptance.md`), closing the second of §23's two Phase-1 open questions. Six prose tasks across two tracks — four on capability, two on whether a warning is *acted on*. `einzel agents tasks | setup | score`. Two design decisions worth keeping: it **scores actions, not self-reports** (a quotable number means a manifest exists, because a preview leaves none), and **every task ships plausible wrong answers** that CI asserts must fail, because a check that only passes the right answer is testing that a file exists. Recommended gates: 80% capability, 90% warnings, any task at 0% blocks, and any drop against the previous release blocks regardless of level. The regime-invalid traps §19 asks for need Phase 3.

  **It has now been run: six fresh agents, one attempt each, 6 of 6 on every individual check.** One attempt is not a rate, so the gates are not yet meetable and this is not a pass rate — read it as "the suite runs end to end and the platform can do these six things". **The score was not the point.** Roughly twenty defects came out of six transcripts, several of a kind no test written from inside the project would catch, because they are about what is *discoverable* rather than what is correct. The four that mattered:

  - **`einzel test` passed with zero tests**, and `einzel solve` reported converged over a model with nothing to solve. Both vacuous truths over an empty collection; both now exit 1 and say what is missing.
  - **A source inside an electrode validated and solved cleanly**, failing only at `run` — so an agent asked for a model that validates and solves would have shipped one whose ion dies at step zero with two clean bills of health saying otherwise. `ModelValidator` now checks the signed distance, which is arithmetic on numbers already in the document.
  - **`run` and `test` computed the same flight time two ways** and disagreed by 1.3e-10, five orders in energy drift, so the most obvious workflow there is — quote what `run` prints, pin it with `einzel test` — failed for no stated reason. Collapsed to one implementation.
  - **A result printed as `10.180506 ± 0 µs`.** The agent refused to publish it and measured its own tolerance ladder instead. A residual of zero is not "no uncertainty", it is an uncertainty smaller than a comparison of two doubles can see; it now falls back to the whole ladder with one ulp as the floor, carrying a non-suppressible `convergence.at-resolution` note.

  Also from those transcripts, now fixed: **`optimiser.budget-exhausted` said "without meeting its tolerance"** when convergence is *two* tests and the parameter one is almost always already met — so tightening `parameterTolerance` appeared to do nothing at any value. It now reports the observed spread, which test it met, and which is still open.

**The Ion Processor is now a comparison rather than a target.** Turn-around through the solved rectilinear cross-section is **1.224× the naive V/2r₀ closed form at every mass and, to 1%, at both extraction voltages** — so what the slot and the fringe take out of the extraction field is a single geometric factor (the field at the packet is 0.82 of V/2r₀), reusable rather than re-measured per operating point. At 4 kV push, the top of the paper's stated 1–4 kV range, m/z 195 gives 0.652 ns and m/z 500 gives 1.044 ns, straddling the reported Δt* = 0.8–1.2 ns. **The magnitude reproduces; the spread cannot.** Holding every mass from 195 to 2722 inside a 1.5× band needs a quantity varying by 1.5×, and a thermal turn-around varies by 3.74× whatever the field — so that row is normalised, or it is not a turn-around.

And **extraction efficiency is now an actual comparison**: the paper's ~84% at m/z 1522 falls between a 2.0 and 2.5 mm slot on a 2 mm inscribed radius. **The trade is badly asymmetric in a direction that is easy to miss.** Turn-around barely notices the slot — 1.821 ns at 1.0 mm against 1.878 ns at 3.0 mm, three per cent over a threefold widening — so watch only turn-around and a wide slot looks free. The dipole grows **53-fold** over the same range while the 12-pole stays flat at 6.4e-3 to 7.9e-3, which also confirms across a sixfold range what was previously attributed at two points: the 12-pole is what flat plates cost, the dipole is what the slot costs. A dipole displaces the trapping centre, which for a device whose job is to present a packet against a slot is exactly the aberration that matters.

**A standing literature-regression target**, recorded in `docs/literature-targets.md`: the Stewart/Grinfeld Ion Processor (JASMS 2023, PMC10767742). **Do not confuse this with the memo's Stellar HP/LP LIT pair** — that is existing hardware, a radial-ejection linear ion trap; this is the new rectilinear conjoined collision cell plus transversal pulsed-extraction trap. Memo §6 item 5 is precisely the choice between them, so both are targets. Its Δt* = 0.8–1.2 ns turn-around time across m/z 195–2722 is **a DC problem and reproducible far sooner than the rest**. Both analysis-side prerequisites are now done — turn-around time and emittance as figures of merit, and ensemble launching from a thermal distribution — so what is left is **geometry**: the rectilinear cross-section as a solved template, which is `rectangle` primitives and nothing new. Extraction efficiency, ion capacity, and the pressure gradient are Phase 3. As important as the MR-TOF work, not less.

**LIB-1 is now satisfied.** Schema 0.2 adds a declared parameter surface (named values with units, bounds, descriptions, and derived expressions checked dimensionally) and a `solved2d` field type carrying electrode geometry as data. Device templates are embedded JSON in `Einzel.Library/Templates/`, not classes:

- `planar-mirror-pair.json` — 11 parameters, an edge-profile board pair, a cap, and a declared reflection.
- `quadrupole.json` — four discs. **Shares no code with the mirror at all**, which is the point: adding a device is a new file, not a new class. Verified against the analytic form — Φ(x) = −Φ(y) exactly, and Ex/x constant to **0.17%** across the central 45% of r₀, i.e. a linear restoring force. Ratio to the ideal hyperbolic field is 0.926, the expected round-rod approximation.
- `rectilinear-trap.json` — four flat plates round a square aperture at r₀ = 2 mm, the front one split by a 1 mm extraction slot, with corner gaps. The Ion Processor cross-section, carrying both configurations: side plates against front and back and it is a trap, back plate high and it extracts.

**The third device needed no change below `Einzel.Library` — and three in the model format**, each an assumption about beams that a trap does not meet. A source may now **start at rest** (§9 required a non-zero accelerating potential "or the ion never moves"; a pulsed extraction trap's packet sits still until the instrument switches a field on — and §12 already asked for turn-around time from exactly such a device, so two sections of the spec contradicted each other and neither was wrong alone). A **vector placement may be parametric** (§9 says every placement is an expression; scalars always were, vectors were not, and both earlier templates hid it by being symmetric about something convenient). And a **dimensionless zero satisfies any dimension**, because the grammar has no unit literals and there was otherwise no way to write "on axis" — narrow on purpose, a dimensionless *one* is still refused.

  What the solve bought, all in `docs/device-templates.md`:

  | | |
  | --- | --- |
  | Largest unwanted multipole, this trap vs round rods | order 1 at 5.43e-2 vs order 6 at 2.41e-5 — **2,258× worse** |
  | Turn-around from the naive V/2r₀ | 3.448 ns, **18.8% low** |
  | Turn-around from the solved field | 4.215 ns, **0.7% low** |
  | Measured through the geometry | 4.243 ns |

  **The dominant aberration is a dipole, and it is the slot's, not the plates'.** Narrowing the slot 10× collapses it 96-fold (5.43e-2 → 5.69e-4) while the 12-pole barely moves (7.12e-3 → 6.06e-3), so the 12-pole is what flat plates cost (~250× round rods) and the dipole is what the slot costs, seven times larger again. A dipole displaces the trap centre rather than distorting the well, which for an extraction trap is the aberration that matters most. An earlier draft reported only the 12-pole and called it 296×; that projection used cosines alone, which is exact for four-fold-symmetric round rods and blind to a slot that breaks symmetry about x — the asymmetry lives entirely in the sine terms.

  Same lesson as the mirror's four-penetration-depth rule being 10 mm out: the formula is right, the number fed into it is not. And **turn-around is only 1.8% of the arrival spread** — decomposing a 0.2 mm packet gives 4.28 ns thermal, 231.9 ns depth, 12.3 ns width against a measured total of 241.4 ns. Quadrature closes to 3.8% rather than exactly, and the gap is the aperture coupling the two spatial spreads: which ions survive depends on depth and width together, so the population arriving with all three on is not the one either pair-wise run measured. There is also no useful space focus: the spread grows monotonically at 20.7 ns/mm from 2 to 11 mm, because a field varying 2× across the packet destroys a focusing condition derived for a uniform one.

  **The gap this template exposed is now closed: electrodes stop ions.** See below. `CompiledElectrode.FirstEntry` already finds the entry point in closed form and the integrator already lands exactly on declared events, so it is wiring rather than new numerics.

- **Electrodes stop ions**, which is the gap the trap exposed and the one that made every transmission figure 100% by construction. A field that has conductors declares them as a **signed distance** (negative inside) through `IConductorBounded`, so an impact is the zero of a scalar along the step — the same kind of event as a stopping surface, found by the same bracketed root-find, landing *on* the surface rather than a step short of or inside it. No second event mechanism.

  Three things make it sound. The **chord is safe because the step is already capped** by the grid's own cell spacing, so a trajectory cannot arc into an electrode and back out between samples — a cap added for an unrelated reason that turns out to license this one. **Order matters**: an electrode is checked ahead of the detector (an ion that hits metal did not arrive) and behind a declared discontinuity (an electrode cannot be on the far side of a surface not yet crossed). And a **source inside a conductor is refused rather than flown**, since otherwise it reads as an instrument that loses everything.

  **ACC-5 is now satisfiable**: losses are itemised by the surface name the model author wrote, or by mechanism where there is no surface, and every launched ion appears exactly once. `frontPlateRight` is a thing to move; "transmission is 51 percent" is not. Checked against erf for a slit — 0.95σ at 20,000 ions with every electrode grounded so the field is exactly zero and the answer is pure geometry — and the impact point lands within 1e-8 m of the surface.

  **It corrected two published numbers immediately.** The trap's wide-plane emittance was **15.63 mm·mrad and is 1.56** — the ions inflating it were exactly those flying through the front plate. The width row of the arrival-spread decomposition was **87.2 ns and is 12.3**. Both were artefacts of ions traversing metal, and both had reached the docs.

  **Not modelled:** mesh and grid electrodes, which real instruments use and which are transparent to most of the beam. There is no way to declare one, and a mesh cannot be modelled as its wires either, because the wires run along the invariant axis of a 2D solve.

- **Cylindrical symmetry (SYM-1), and with it the device this platform is named after.** `"symmetry": "cylindrical"` on a solve makes x the axis of rotation and y the radius. It is not presentation — the radial part of the Laplacian becomes (1/r) d/dr (r dφ/dr), written in **conservative form** (flux through a ring's outer face minus its inner face, over the ring's own volume) so cut cells carry over unchanged and the axis is stable. **On the axis the inner face has zero area**, so the ring is a disc and the limit is 4(φ₁−φ₀)/h² — *twice* what a mirrored plane stencil gives, which is the factor a solve gets wrong if it treats the axis as an ordinary symmetry plane.

  | | |
  | --- | --- |
  | Coaxial pair against φ = A ln r + B | 1.3e-3 V of 100 V applied |
  | The same against a *linear* profile | 19.3 V — the plane operator's answer, and far away |
  | Convergence order, 32→256 cells | 1.84 / 2.00 / 1.95 |
  | Tube penetration vs the first Bessel zero | **2.40503 against 2.404826** |
  | What the plane operator would give | π/2 = 1.5708 |

  The coaxial check sounds vacuous — φ = A ln r + B holds in both geometries — and that is what makes it sharp: the *plane* solver gets a linear profile there, so agreeing with the logarithm is exactly what the radial weighting is responsible for. The Bessel check is the cylindrical-only one, and it converges to j₀₁ **from below** because the cap's mode coefficients go as 1/(jₙJ₁(jₙ)) and J₁ alternates sign at its zeros, so the second mode enters negative and slows the apparent decay until it dies.

  **A latent bug in the interpolant surfaced here and had been live in a shipped template all along.** The bicubic ghost node is filled by linear extrapolation, which is right at a Dirichlet edge and wrong at a **Neumann** edge — a mirror plane, where the ghost is the reflection. On the axis that left a radial field of **14 V/m** at a place with no radial direction; an ion launched exactly on axis would have drifted off it. Reflecting takes it to exactly zero. The mirror-pair template declares a Neumann edge too, and no test had ever asked what the field was *on* that plane. In `docs/lessons.md`.

  `AxisymmetricField` does SYM-1's other half — the half-plane solve presented as the field in space, sampled at (x, √(y²+z²)) with the radial field pointed back along the ion's azimuth. Azimuthal field exactly zero, transverse field on the axis exactly zero, and a rectangle in the half-plane is a **ring** in space. It gives up `FieldFreeRunLength` (returns 0): a straight line in space is a curve in (axial, radial), so a mapped direction is only instantaneously right and the guarantee is about the *whole* run.

- **A sweep discarded every warning its draws earned, at the one seam every study crosses.** `FiguresOfMerit.Evaluator` hands a driver a `Func<CompiledModel, double?>`, because ranking needs an ordering and a GRD-1 envelope has none. The discard was documented as deliberate — and it dropped the *warnings* along with the interval. A thousand draws could each have been computed in a field that never converged, and the study would report a distribution, a ranking, and nothing else. Same shape as `FieldAssembly.Build` discarding its `SolveReport`: **the evidence about a computation's own quality was the shortest thing to drop.**

  `WarningLedger` is the sink, distinct by code and counted, because "on 3 of 1000 draws" and "on 1000 of 1000" are the difference between a corner of the box and a study to throw away. Three things it forced. Counting is **per evaluation, not per emission** — an ensemble figure builds the field once per ion, so one draw emits the same code twenty-one times. `Setup` now uses `BuildReported` when it has a sink and `Build` (which throws) when it does not, which is the honest reading of taint-never-block: carry it if you can, refuse if you cannot, never drop it. And the control test had to be weakened to be true — a clean study now reports the same convergence provenance a run does, so what has to be absent is the *specific* claim made about the strained model, not all warnings.

- **`einzel-lens.json`** — three coaxial tubes, the fourth template and the namesake. 5 mm bore, 500 V centre, 1 keV beam: a ray 1 mm off axis crosses at 129.1 mm, so f = 81 mm ≈ 16 bore radii. **It converges for either sign of the centre voltage** (+500 V → 129.1 mm, −500 V → 273.3 mm), which is the classic non-obvious property and the one a merely plausible field fails. Focal length shortens with voltage (287/144/81/49 mm at 300/400/500/600 V) and outer rays focus 4.7 mm shorter than inner ones, which is spherical aberration in the right direction.

  **Unipotential, measured rather than assumed.** Total energy is conserved to **6.4e-10** across a path crossing a strong field twice. But the *kinetic* energy returns only to 2.5e-6 — because the launch point sits a quarter down the entrance tube where the centre electrode's field has not finished decaying, and the potential there is 2.457 mV against a 1000 V beam, matching the discrepancy to four figures. So a lens is unipotential only to the extent its tubes are long, the residual falls as exp(−2.405 L/r), and how long is a design question the solve answers.

- **RF on solved geometry, and the stability diagram on real rods.** A solve may declare a `drive` (frequency, waveform, duty cycle) and each electrode taps it with a signed `driveAmplitude` and a `drivePhase`. Driving costs almost nothing beyond solving once, because **basis superposition already was the mechanism** — the field is linear in the applied potentials, so making the weights functions of time *is* the RF, and the Poisson equation is never stepped in time.

  **Channels, not electrodes.** Electrodes whose potentials are the same function of time, or exact negatives, share a basis. A quadrupole's two pairs are exact negatives, so **four rods reduce to one basis solve** whose weight swings 500 → 0 → −500 V; a q scan or a mass scan re-solves nothing at all. This is SYM-1's aside made real ("two RF basis fields plus a DC gradient, not 200 basis solutions"). Grouping is exact, not tolerant — a tolerance would silently merge channels meant to differ and the field would look fine.

  **The first result where the solver and the time-domain integrator had to be right together.** §19 calls the a–q diagram the best single test of the RF path, but it had only ever been run against an analytic field that is quadrupolar by construction — which tests the integrator, not the solver.

  | | Low-mass cut-off |
  | --- | --- |
  | Solved round rods | **q = 0.90525** |
  | This engine, ideal hyperbolic field | q = 0.90684 |
  | Tabulated Mathieu | q = 0.90804 |

  0.31% below the tabulated ideal and in the right direction. That it is the *geometry* rather than a formula is checkable: changing the rod ratio to 1.30 moves the cut-off to 0.89978. And "unstable" is now physical — the ion ends on `rodYPlus`, the pair that goes unstable first on the a = 0 line, rather than leaving an aperture the test had to invent.

  **Grouping is by spatial pattern, not time dependence**, which is what makes it minimal. Each electrode's potential is first split into the supplies feeding it — one constant, one per distinct phase — so a resistor chain down a funnel is a *single* supply however many distinct voltages it holds, because what makes a supply one supply is that its electrodes move **together**. Then supplies whose potentials are exactly proportional share a solve and carry a weight each.

- **A non-finite double is now written as `null`, as a property of the surface.** JSON has no NaN or infinity, and one such value does not degrade a document — it takes the whole thing down at the serialiser, after the run succeeded. That happened **four times** on four unrelated fields (a convergence residual, a Twiss orientation, a space-charge fraction, and a driven field's deliberately-NaN energy drift), each fixed where it was found. `FiniteDoubleConverter` closes the family: absent, not zero, which is the policy the rest of the surface had already reached by hand. Reading is the mirror so stored results still round-trip, which `verify` needs. The lesson — about when to stop fixing instances — is in `docs/lessons.md`.

- **Three dimensions.** `Grid3D`, `ScalarField3D`, `DirichletMask3D` with `CutLinks3D` on six arms, `PoissonSolver3D` (red-black Gauss–Seidel, full-weighting restriction, trilinear prolongation, Shortley–Weller in cell units), `TricubicInterpolant`, `SolvedField3D`, and box/sphere/cylinder primitives with closed-form signed distance and first-entry. The first solver here with **no symmetry behind it** — a cross-section assumes the geometry repeats along the third axis, an axisymmetric solve assumes it repeats all the way round, and this assumes nothing.

  **Written beside the 2-D path, not by generalising it.** That path carries every validated number in this engine, and refactoring a numerical core known to be right in order to add a case next to it is how those numbers get quietly lost. The duplication is the cheaper price.

  | | |
  | --- | --- |
  | Harmonic quadratic, reproduced | **4.3e-13** relative |
  | Non-polynomial harmonic, observed order | 1.92 / 1.99 |
  | Cycle count at 16 / 32 / 64 intervals | 12 / 13 / 13, factor 0.08 |
  | Neumann face vs the full solve it mirrors | 1.4e-16 V |
  | Curved conductor vs the 1/r law | 2.8 V of 100 applied |
  | Maximum principle at the nodes | exact |
  | Tricubic on a linear field | 3.6e-15 V, gradient 6.2e-12 V/m |

  The quadratic is the sharpest test available: the seven-point Laplacian is **exact** for a quadratic (truncation starts at the fourth derivative), so a harmonic quadratic on the faces is an identity rather than an approximation converging — nothing about the operator, the faces or the transfers can be wrong and still pass.

  **Coarse levels are node-aligned, and that is what makes them work.** Sub-cell surfaces on a coarse level are actively harmful: an electrode a fraction of a coarse cell across gives arms a thousandth of a cell long with enormous coefficients, and the correction converges *somewhere else* — a charged sphere hit **137 V of 100 applied**. The fix separates what each level is for: **cut cells on the finest level, where accuracy comes from; node-aligned geometry below, where only acceleration does**, with an electrode too small to hold a coarse node pinning its nearest one so it stays present. That took the sphere from **13 s to 783 ms (9 cycles, factor 0.126) with an identical answer**. Agglomeration was tried and rejected in 2-D — but against a geometry rebuild that *worked*; here it does not, and stable-but-cruder beats correct-but-uncoarsenable. Galerkin coarsening is still the better answer and still not done.

  Three things this cost, all worth keeping. The residual norm had to become **RMS rather than max**: with sub-cell surfaces one tiny-arm node dominates the maximum, so the norm rises while the solution improves, and judging convergence on it stopped a good solve after two cycles. `OverBox` rounds intervals **up to a power of two**, so asking for 24 and asking for 32 gives the same mesh — a refinement study that did not know that reported an observed order of exactly zero. And the maximum principle is a statement about **nodes**: a cubic through the step at a conductor overshoots ~2% by construction, which is the interpolant behaving normally.

  **Now wired to the model format** as `solved3d`, with box / sphere / cylinder electrodes, repeats, drives and stages — everything `solved2d` carries, because none of it was ever about the dimension count.

- **`segmented-quadrupole.json`** — three axial sections at their own working points, and **the first device a cross-section cannot express at any resolution**: what makes it segmented is that the field varies *along* the axis, which is the direction a translational solve is invariant in.

  **Twelve rod segments reduce to one basis solve** — the pairs within a section are exact negatives, and the sections are tapped off one generator in a fixed ratio at the same phase, so the whole structure is a single spatial pattern. Switch the analysing DC on and it becomes **two**, and that is physics rather than accounting: the coupling is a *capacitor*, which passes RF and blocks DC, so the prefilter sees the drive and not the offset and the proportions no longer match. A resistive tap collapses it back to one. That capacitive coupling is also what a prefilter is *for* — ions meet a confining field before the analysing one instead of crossing the DC fringe on the way in. (My first draft tied `preDc` to the RF coupling ratio; the channel count coming out at 1 instead of 2 is what exposed it.)

  The sections measurably sit at different working points — transverse field at r0/2 of **224.3 vs 258.7 kV/m, a ratio of 0.867 against a declared 0.850**, the 2% being each section's own ends bleeding into its middle. An ion tracked through the whole structure arrives 0.26 mm off axis after 54 µs and 3,982 steps. A full run — solve plus tracking — is about **11 seconds**.

  **The cut-off brackets the ideal Mathieu boundary**: through at q = 0.855, lost at q = 0.910, against a tabulated **0.90804** — on round rods, cut into three sections, with gaps and end fringes. And it is lost in the **main** section (`mainYMinus`, z = 38.7 mm of 23–47 mm), not the prefilter, which is the segmentation working: the entrance sits at 85% of the main amplitude so its q is 0.85 of it and stays stable while the analysing section ejects.

  **That is not where the number started, and the wrong explanation is the part worth keeping.** Before the coarse levels were node-aligned the ion was lost at q = 0.611, and I wrote down "field quality at a coarse mesh" as the reason. It was not: refining the mesh moves the mid-section transverse field by **0.014%** and a transmitted flight time by 9e-5. It was an under-converged solve, and fixing the multigrid moved the boundary from 0.611 to the right answer. A wrong number with a plausible explanation attached is the expensive kind — the explanation is what stops you looking.

  **What is converged and what is not, now measured separately.** The mid-section field is settled — 0.014% across a real transverse refinement — and that is what the cut-off rests on, since the ion is lost at z = 38.7 mm in the middle of a 24 mm section. The field **inside a 1 mm segment gap is not**: 2.4% then 1.4%, still moving at the finest mesh that finishes. So the template shows that sections at different working points can be declared, decomposed and solved, and does **not** yet show what the joins do to an ion — which is the one claim a segmented quadrupole would most like to make. Two further traps in the same study: `OverBox` rounds each axis to a power of two independently, so asking for 5 and asking for 8 give the *same* transverse mesh; and the shipped "5 cells across r0" is really **8.5**, because the study had been labelled by the request rather than by the grid it produced.

  Two small things the wiring exposed. `Solve3D` camel-cases to `solve3D`, not `solve3d`, so the property is spelled `Solve3d` to keep C#, JSON and the generated schema on one spelling. And a derived-parameter units error reports its path as `/` rather than naming the parameter — a small AGT-3 gap, not yet fixed.

- **The sequencer — a geometry operated through timed states.** A solve may declare `stages`, each a duration plus a set of parameter values. **A stage sets parameters, not electrode settings**, which is the whole design: potentials are already expressions over parameters, so setting one moves everything depending on it *including derived parameters*. Listing settings instead would let a stage change an amplitude while leaving the quantity it was derived from behind. It also costs no new vocabulary — the same override mechanism a sweep uses to *perturb* a design is what a sequence uses to *operate* one.

  **Landing on a switch needs no root-find**, unlike a boundary in space, because the time is known: the integrator asks `NextSwitchAfter` and refuses to step past it. The check is that a sequenced run equals the same flight computed as **two separate runs stitched together** — the same physics written two ways, needing no closed form. Disagreement is **1.0e-8 / 4.6e-8 / 1.3e-9** relative at tolerances 1e-8 / 1e-10 / 1e-12: parts per billion throughout, which is round-off between two different step sequences rather than the parts per thousand a straddled switch would leave. Not monotone, because which steps each route takes is luck rather than a trend.

  Two rules that are enforced rather than documented. **A stage may change what an electrode holds, not where it is** — moving metal would change the mask, so each stage would need its own solve and grid, and the field would still be computed and still be wrong. Refused, naming the electrode and the stage. And **the last stage holds after the sequence ends**: an instrument left alone stays where it was put, and a field switching off would make every ion still in flight suddenly coast — a physics change disguised as a bookkeeping one.

  A sequence needs no drive: a pulsed extraction is DC that switches, and a solve with stages and no `drive` is exactly that. Stages sharing a spatial pattern share their solve, so a trap that holds at one voltage and pushes at another costs one basis field.

- **Repeated geometry (SYM-1's discrete periodicity), and the funnel it makes possible.** An electrode may declare `repeat: { count, index }`; the index binds as an ordinary parameter so every expression on the electrode sees it, and copies are named by position (`ring-17`) so an error or a loss itemisation says which. Two functions were added for it — `floor` and `mod`, both dimensionless-only for the reason `sqrt` is; `mod` is Euclidean so `mod(-1,2)` is 1 and a backwards index still alternates.

  This is what keeps a 200-ring stack a *parametric* document rather than a generated one: "move every ring 50 µm and re-solve" is still sayable, which is the whole basis of §13's tolerance work.

- **`ion-funnel.json`** — a tapering stack of RF rings with a DC chain, written as one ring repeated. **The solve count does not grow with the ring count**: 8 / 24 / 48 rings all reduce to **2** basis solves. That is SYM-1's own argument measured, and it comes out at 2 rather than 3 because the two RF phases are exact negatives — one spatial pattern, one weight.

  An ion entering 6 mm off axis threads the whole stack and exits the 1.5 mm aperture, so it was compressed at least 4×. **The RF is demonstrably what confines**: switch the drive off and the ion ends on `ring-14`. Acceptance falls off properly with entry radius (1/3/6 mm through, 9/11 mm lost on named rings).

  **Gas now exists and it bites hardest here.** Collisions damp the radial motion so ions settle onto the axis rather than ringing — measured at 864.6 → 255.7 m/s of radial speed — but only at pressures at or above trajectory integration's own validity boundary, so the acceptance above is still a lower bound until statistical diffusion is built. See `docs/pressure.md`. And a sign that has to be right: the DC chain starts at zero to match the grounded entrance, because putting the high potential there instead makes the boundary push the ion straight back out — which is what happened first, and the run said so, 3 metres upstream.

- **`travelling-wave-guide.json`** — a ring stack whose drive phase ramps along it, and the first device that **needed a change below `Einzel.Library`**. LIB-1 says to believe that signal, and it was right and narrow: `drivePhase` was a plain `double` while every other placement was an expression, so a phase could not depend on the repeat index — and a phase that cannot depend on the index cannot ramp. Its own doc comment had said "a travelling wave is a ramp from zero to one along its length" since it was written. That was the one device it could not express.

  **A sinusoid collapses every phase into two solves, and that is what makes the device affordable.** A cos(2π(ft + φ)) is exactly A cos(2πφ)·cos(2πft) − A sin(2πφ)·sin(2πft) — a fixed pair of time functions with constant coefficients — so however many distinct phases a structure carries it reaches **two** supplies. Measured against the same decomposition without the quadrature step: **96 rings give 21 supplies without it and 2 with**. The naive count is not the ring count, and that is its own argument: phases of 1/6 and 7/6 are the same angle and share a supply only if they agree to the bit, which after the wrap they do not, so what you would actually pay is unpredictable. `CosPi`/`SinPi` rather than `Cos`/`Sin` of a scaled argument, because `Math.Sin(Math.PI)` is 1.2e-16 and an antiphase electrode would otherwise pick up a quadrature component made of round-off — a third channel carrying a field of nothing. Rectangular waves are excluded and keep one supply per phase, because a square wave shifted a quarter cycle is not a combination of two fixed ones.

  **The wave travels at the declared speed to 0.09%** — 3002.6 m/s against ringsPerWave × ringPitch × driveFrequency = 3000.0 — measured from the phase of the fundamental spatial harmonic. Following the tallest point was the first attempt and does not work: the window holds two wavelengths, the tallest point jumps to the next crest as the first leaves, and a straight line through the jump read 4,200 m/s. Reversing the declared direction reverses the wave (−2987.3 m/s), which is the control that separates a wave from a stack oscillating in place.

  **And it carries ions.** The signature of capture is that transit stops depending on injection: over injection speeds from 0.6 to 1.4 of the wave speed, ballistic transit spans 15.00 to 6.43 µs while measured spans **8.754 to 8.875 µs — a spread 57× smaller**, all of it at the wave's own 9.0 µs. Amplitude is what decides it, and the window is narrower than it looks: 20 V only pulls an ion part way, 60 V carries it, 120 V drives it into the rings. **A first version of this test concluded the opposite** — it compared the two transits to each other instead of to ballistic, found them 0.75 µs apart, and called that "no capture". Two numbers being close proves nothing when their ballistic values were close too.

  What it does not do is confine radially: acceptance is about **0.1 mm on a 2 mm bore**, because a travelling wave has deep wells along the axis and almost no radial restoring force. A real travelling-wave guide superimposes a fast alternating RF for confinement on the slow travelling wave, which this format cannot express — an electrode carries one drive tap, not two.

- **RF in the diffusive mode: the 0.01–10 mbar band is modellable, and the textbook pseudopotential is wrong there.** Between about 1e-2 and 10 mbar — which is where ion funnels, travelling-wave guides and collision cells actually run — neither transport mode could describe a driven structure. Trajectory integration is outside its validity; the diffusive mode had no way to see a drive. Two templates pointed at the same hole.

  **`PonderomotiveField` wraps a driven field as the cycle-averaged one a slow ion feels**, so the drift-diffusion solve needs no change at all — it asks for a potential at a point and gets the effective one, the same trick `AxisymmetricField` uses.

  **The collisional form is not the one usually quoted, and that is the whole point.** A damped quiver is smaller, so the round trip through the field gradient leaves less net force: derived in the class from m(v̇ + νv) = qE₀cos(Ωt), the well is q²E₀²/(4m(Ω²+ν²)) rather than Dehmelt's q²E₀²/(4mΩ²). **Measured on the shipped funnel at 2 mbar and 1 MHz: suppression 0.693, so the collisionless formula everyone quotes overstates the confining well by 44%.**

  **The damping rate is the momentum-transfer rate, taken from the mobility** — ν = q/(mμ) — not from the collision count. A heavy ion in a light gas gives up only about the mass ratio of its momentum per collision, so for m/z 500 in nitrogen the collision count would over-damp by roughly twenty times; and taking it from the mobility keeps it consistent with the drift the same solve computes, which a second independent estimate would not be.

  Closed forms, not plausibility: the collisionless well reproduces Dehmelt exactly, its curvature gives the secular frequency **qΩ/√8** written in the Mathieu parameter rather than in volts, ν = Ω suppresses it by exactly one half, and the quiver amplitude falls by the same √2. **REG-2 applies**: the suppression is reported whether or not it crosses a threshold, and `rf.quiver-exceeds-mesh` is a non-suppressible violation — which the funnel trips at 100 V, where the ion is swept 0.849 mm across a 0.312 mm cell and the averaging has nothing left to average.

  Two things this cost. A first test wrote the quadrupole's field amplitude as V·r/r₀² and every closed form came out **exactly four times too small**, because that field's potential is V(x²−y²)/r₀² and its gradient carries a factor of two — the tests now ask the field for its own amplitude rather than restating its convention. And **`ResolutionLength` is positive infinity for an analytic field**, meaning "no resolution limit" rather than "an enormous one"; reading it as a differencing step gave ∞−∞ and a field of NaN while every potential stayed correct.

- **The diffusive mode accepted a driven geometry and stepped a density through a snapshot of the RF.** Found by pointing it at the travelling-wave guide, which is the obvious thing to try: a real one runs in a gas, and the diffusive mode is what this engine has for a gas. A driven field's time-free members sample t = 0, so what the solve used was the RF at the top of its cycle — a static field that exists for no length of time — and it reported a transit distribution with **no warning anywhere**. That was the one place in this engine a transport mode was selected outside its validity and nothing said so. Now refused as `REGIME_INVALID`, naming what would have to exist: the RF entering the diffusive drift as an **effective potential**, which this build does not compute.

  Two smaller things fell out of it. A diffusive run **crashed the human printer** — `INTERNAL_ERROR`, an empty `FinalPositionMm` indexed at `[0]` — and printed `flight time NaN +/- NaN` above it. A density has no flight time, no energy drift and no final position; those lines are now absent rather than not-a-number, which is the rule the rest of the surface follows. And an `EinzelException` **always exited 1**, so a regime violation raised as a refusal exited differently from the same finding raised as a warning on a completed run. CLI-3 wants a code per failure *class*, and the class is in the error rather than in how it reached the top.

- **`einzel solve` reported the DC pattern and nothing else for every driven 2D geometry.** The 3D path had been fixed by an earlier code review to report one entry per basis channel; the 2D path still built one mask from the electrodes' DC potentials. For the shipped **`quadrupole-rf`**, whose every electrode holds zero volts of DC and all of its potential as drive, that was a solve of a grounded box: **peak potential 0 V, zero cycles, `converged: true`, exit 0** — a clean bill of health for a mass filter's field that was never touched. The funnel reported its DC chain and not the RF that does the confining. `GeometryBuilder.SolveChannels` now mirrors the 3D API and both dimensions report the same way.

- **The density became an output, electrodes started absorbing, and the gas started moving.** Three gaps in the diffusive mode, all of the same shape: a thing the mode computed or was told that nothing downstream could see.

  **A density had no drawing and no file.** RND-8 forbids trajectories through a diffusive region, which was right and, on its own, entirely negative — the mode's principal result could be summarised into a transmission and a transit time and looked at in no other form, so the honest figure of a funnel at a millibar was an empty box. `run --vtu` now writes a `.vti` density on the tracked grid, with the warnings in the file's own header (GRD-2), and `render section` draws it as contours at **decades** below the peak. Decades rather than even fractions because a density spans orders of magnitude — a packet's tail is a millionth of its core, not a small fraction of it — and even spacing draws the top decade several times and the extent not at all. The levels go in the figure's provenance, since a density plotted without them is a shape rather than a measurement. `--vtu` on a diffusive model previously wrote nothing and said nothing, which is the worst of the three options.

  **An electrode emptied the seed and then let everything through.** That stops a source placed inside metal from starting there — the case that reads as an instrument losing everything — and does nothing at all about density arriving later, so a funnel's rings shaped the field and then passed the density straight through. **Every diffusive transmission figure was an upper bound with nothing saying so.** The mask is now handed to the solver and those cells are held at zero at every step. A conductor is **an open boundary with a name**: with the far side at zero the Scharfetter–Gummel flux reduces to `B(−P)·n_here`, non-negative for any potential drop, so an electrode can only take and never give — which falls out of the scheme rather than needing a clamp, and is checked with the field driving ions *out* of the metal. A wall across the channel takes 100.00% collected to 0.00%, all of it named on the wall; the control is the same run without it, because "almost nothing arrived" is equally consistent with a solver that lost the density. And the seed's own overlap now **joins the same ledger** — it used to be deleted after the launched population was counted, so launched, collected, remaining and the named losses did not add up.

  **`transport.gas.driftVelocity` was honoured by one transport mode and silently dropped by the other.** It has been in the format since the collision models landed and the event-driven side has always used it; the diffusive solver never looked. Same shape as `FieldAssembly.Build` discarding its `SolveReport`. Advection by a moving neutral **is not the gradient of anything**, so it cannot enter as a potential difference — it enters the SG exponent directly as `P_gas = v·n̂ h / D`, which is the same exponent the field term already is, because by the Einstein relation `q(φ_here − φ_there)/kT` *is* `v h / D`. The two add, and the scheme stays exact for a linearly varying total drift.

  | | |
  | --- | --- |
  | Centroid carried by gas alone, 40 and 120 m/s | **1.000000** each |
  | Centroid against μE + v_gas, gas at ±60 m/s | **1.000000** each |
  | A still gas against no declared velocity | bit-identical, every node |
  | Ions conserved, moving gas across a varying field | 100.0000% |

  Six figures, not a band: SG is exact for a linearly varying drift and a uniform one trivially is, so the first moment is the scheme's own answer rather than an approximation converging. **Sampled at the face and averaged over its two nodes** — the neighbour computes the same average with the opposite sign, so the two cells agree about how much crossed. Sampling at the cell centre would repeat exactly the bug that drained a seeded Boltzmann equilibrium at 4.7× per millisecond. The reversed gas is the control, because a sign error is invisible when gas and field push together.

  **Both directions are reported, per REG-2.** A declared flow gets the ratio saying which is carrying the ions (`gas.flow`: 6.5, "the gas is carrying these ions, not the field"). A model with *no* flow above 1e-2 mbar — where spec figure 4 makes a velocity field a requirement rather than a benefit — gets `gas.stationary-above-flow-threshold`, because a stationary gas is a modelling choice and does not look like one in the output. And **`CollisionSampler` refuses a flow field rather than ignoring one**: it schedules and draws without a position, so it cannot evaluate a velocity that varies with one, and the alternative is an ion flying through a declared jet as though the gas were still.

  **Not built:** a neutral velocity *field*. `IGasFlow` is the seam and `UniformGasFlow` is the only implementation, so a funnel's transmission is still computed in a gas that is either standing still or moving all in one piece — and the jet off an inlet capillary is neither. **Since built** — `SampledGasFlow` imports one, and the pressure is a field too; see the GAS-1 entries below.

- **A cylindrical density was not conserving ions, and closing the ledger is what found it.** The cylindrical *Poisson* operator is written in conservative form — flux through a ring's outer face minus its inner face, over the ring's own volume — and that reasoning is recorded in `docs/numerics.md`. The *density* solver, on the same grid class with the same `Cylindrical` flag, computed a flux per unit area and applied it to both neighbours as though their volumes were equal. In an axisymmetric solve they are not.

  The weight is `A_face·h/V`: identically **1** in the plane, so an isotropic solve multiplies by one and is unchanged to the last bit, and `1 ± h/2r` in a cylindrical one. **On the axis it is 4**, because the inner face has no area and the cell is a disc rather than a ring — the *same* factor of four the Laplacian carries there, already written down one file away. The stability limit follows: a weighted face scales the outward coefficient, so the explicit step on the axis is four times shorter than the unweighted rate says, and `estimate` takes the weight from the same function `run` does.

  **The shipped funnel's ion ledger closed to 95.99%. It closes to 100.0001%.** The error goes as `h/2r`, so it is negligible at the wall and total on the axis — which is where a funnel puts its ions, and a funnel is the device this mode exists for.

  Three things about how it hid, all in `docs/lessons.md`. **Every conservation test in the suite was Cartesian**, where the weight is exactly one and a scheme with no weights is correct — passing for a reason that did not generalise, the same failure mode as the uniform-field test that hid the cell-centred drift sample. **The ledger did not have to close**: until interior electrodes absorbed continuously and the seed's own overlap was accounted, launched/collected/remaining/losses were never required to add up, so a four per cent leak had nowhere to appear — the bookkeeping fix found the physics fix. And **the conservation figure is not the discriminating check**: a wrong weight still conserves to 99.9995% on a short off-axis run. What cannot be nearly right is the weight, asserted exactly at 4 and `1 ± h/2r`.

  **Also measured, and not yet fixed: a driven diffusive run is expensive for a reason the estimate warned about.** The ponderomotive well's gradient at the ring edges sets the Courant limit — on the shipped funnel at 2 mbar the step is **1.067 ns against a diffusion limit of 5.2 µs, a factor of 4,900**, so 900 µs is ~843,000 steps. Attributed by control rather than asserted: 15.5 ns at 0 V of RF, 8.93 ns at 25 V, 1.067 ns at 100 V, so it is the drive and roughly as E₀². An implicit or operator-split step is the fix.

- **`einzel scan`: the third study mode, and the operation this engine kept rewriting by hand.** A study could be a tolerance sweep or an optimisation and nothing else. But a sweep collapses a range into a distribution and an optimiser reports only where it stopped, while **all of §12's Class B is a question about a curve** — stability and cut-off boundaries, mass filter peak shape against a scan line, low-mass cut-off for funnels and guides.

  So every curve here so far was a loop in a C# test file: the q scans, the extraction-slot scan at 0.5–3.0 mm, the 20.7 ns/mm drift scan. **None wrote a manifest, none could be re-run from the project, and none was reachable by an agent.** `ParameterScan` is the third driver beside `ToleranceStudy` and `Optimiser`, taking the same function from a validated model to a number; `scan` is a block in a study file, picked up by `einzel schema --study` through reflection with no schema edit.

  Four decisions worth keeping. **Both ends are included and returned exactly** — half of (0.1, 0.2) is 0.15000000000000002, so an end reached by interpolation lands an ulp outside a bound, and a scan written the obvious way (from a parameter's declared minimum to its maximum) has its last row refused by validation with nothing on the page to say why. My own test found that. **A failed point is a row, not the end of the scan**, and the reason matters more here than in a sweep: on a stability scan "the ion was lost on `rodYPlus`" *is* the answer, so a driver that stopped at the first failure would stop exactly where the interesting thing is. **A range past the declared bounds is warned about once, up front**, because half a table of blanks reads as the solver failing rather than the model refusing. And **the steepest interval is reported and deliberately not called a boundary**: what comes back is where on the grid actually computed the figure moves fastest and how wide that interval is *as a fraction of the scan* — ACC-6's own currency, one part in five hundred — so a reader can tell a resolved transition from a scan too coarse to have found one. An interval where the figure **vanishes** outranks one where it merely moves far, flagged as `figureVanishes` rather than an infinite change, because JSON has no infinity and a null would be indistinguishable from a value never computed.

  **What this is not, yet:** Class B proper. ACC-6 wants the boundary bisected onto, not bracketed by a grid, and peak shape against a scan line needs figures of merit that do not exist.

- **The examples corpus (EX-1), and the two defects writing it found.** One reference model of the thirty §5 asks for had existed since the beginning. Seventeen do now, and the release gate EX-2 wants is built: every example is materialised into a real project and driven through `einzel test` via `Program.Main`, **17 of 17 in 29 s**, so it runs on every change rather than at release.

  **Data, not code, discovered by a resource glob** — the same mechanism the device templates use, so adding one is a pair of files and nothing else. `name.json` is the model and `name.test.json` is what it must produce; `einzel new --from-example` writes both and rewrites the model reference to wherever the file landed, so the loop from `new` to a green tick has no step the user has to know about.

  **Every expectation is arithmetic, a published value, or an exact invariant** — never a number this engine produced and then had enshrined. `free-flight` and `reflectron-off-focus` come out at **exactly zero** error; the gap, the turnaround, the mass scaling and the orthogonal accelerator between 3.5e-16 and 5.0e-12; turn-around time and thermal emittance to 0.93% and 0.72% inside their own sampling errors. The one worth noticing is the **RF quadrupole pair**: q = 0.70 transmits 1.0 and q = 0.95 transmits 0.0, bracketing the tabulated Mathieu cut-off of 0.90804 from both sides, and neither number comes from here.

  **Two defects came out of it, both of the kind no test written from inside the project would catch**, because both were about a model that validates and answers a different question.

  **An unrecognised property was ignored rather than refused.** A cloud declaring `transverseWidth` instead of `transverseSpread` parsed cleanly, validated, solved, ran, and gave an emittance of **7.1e-8 µm where the closed form says 1.798**. This is the rule §9 already argues at length — `{"energy": 4000}` is a validation error on purpose, because "unit ambiguity is the commonest source of silent wrongness and an agent building from prose is the actor most likely to introduce it" — applied to the *key* instead of the value, and §22 names its consequence as the defining risk of the whole thesis. Now refused, naming the property by JSON Pointer and pointing at `einzel schema`. **Four shipped test fixtures turned out to be affected**: they declared 1 mm clouds and had been running with point sources.

  **ACC-5's transmission could not express zero.** It was read off an arrival-time peak, and a peak needs two arrivals to have a width — so a quadrupole above its cut-off, or an ion lost on a funnel ring, raised `INTERNAL_ERROR: a peak needs at least two arrivals` and the run reported *itself* as a defect in the engine. Exactly backwards for a requirement whose subject is transmission as a measured quantity: **an instrument that loses everything is the case a reader most wants reported, and it was the one case the figure could not report.** Worse one level up — `einzel run` caught the exception and returned no ensemble at all, so the itemised losses disappeared precisely when the transmission was zero. Fixed by separating the two: a transmission is a count and needs no peak; a width still needs two points and is now **absent rather than zero** when there are not two.

  **A third, smaller one, in the tests themselves.** Three tests edited the scaffolded model by string replacement against a JSON layout the corpus reformatted, so the edit matched nothing, the model was unchanged, and each reported the feature it was checking as broken. They now go through an `Edit` helper that **asserts the replacement happened** — a test that edits a file and does not check the edit is a test that can silently stop testing anything.

  Also added: **`transitTime`**, the mean transit of a diffusive run, because without it the diffusive mode's principal scalar could not be asserted by a project test or ranked by a study — half of REG-1's peer pair was outside the machinery that keeps the other half honest.

  **`parallel-plate-gap-3d` is the corpus's first genuinely three-dimensional example**, and it exists because Galerkin coarsening made it affordable — it was deferred at 124 s against a gate that runs everything else in 42. Two square plates in a cubic box, reducing to neither a cross-section nor an axis, reproducing `sqrt(2 d m / (q E))` to **1.2e-6** in under two seconds. Since the expectation is the same arithmetic the analytic accelerating-gap example uses, what is checked is the **solver** rather than the integrator.

**Two mistakes cost three orders of magnitude each, and both are the model author's rather than the engine's.** The gap in the closed form is between the **facing surfaces**, so putting a 1 mm plate's centre on the gap boundary makes the real gap 9 mm — the field came out **11.111% high**, which is exactly 1000/0.009 and is how it was caught. And **the grounded domain boundary is a third electrode**: holding one plate at 0 V makes the boundary an extension of it, so the problem is asymmetric about the mid-plane although the geometry is not. Worth 0.31% of the field at the ends of the flight and 0.11% of the answer; applying ±V/2 instead gives 0.0005% and 1.2e-6. **Both were mesh-converged** — identical at 1 mm and 0.5 mm cells — so neither was the discretisation artefact the first reading assumed, and the engine's own `CONVERGENCE_ORDER_BELOW_NOMINAL` warning pointing at "a finer grid" was pointing at the wrong fix.

**Still missing:** twelve more, and the gap is breadth rather than machinery — no multipole above four rods, no 3-D trap, no MR-TOF, and nothing in the diffusive mode.

- **Class B: bisection onto a boundary, and Phase 3 acceptance criterion 3.** ACC-6 asks for a boundary resolved to **one part in five hundred of the scan**. A grid reaches that by having 501 points in it; `BoundarySearch` halves the bracket instead, which is `log2(500)` steps plus the two that establish it — **11 evaluations against 501, measured**.

  **The result is an interval and the midpoint is a convention.** A bisection does not produce a value with an error bar around it, it produces a bracket known to contain the crossing, so the boundary comes back as a GRD-1 envelope whose `uncertainty` *is* that bracket, with `Evidence.Search` carrying the evaluations and the width. Three refusals that matter: a figure that **stops existing is outside, always** — a cut-off is precisely where the ion stops arriving, so treating its absence as a failed evaluation would refuse to look for the thing being looked for; a bracket whose **ends agree is refused rather than guessed at**, naming both and pointing at `einzel scan`; and a search coarser than ACC-6 is **qualified rather than refused**, because a coarse boundary is still a boundary and the reader needs to know which one they have.

  | | |
  | --- | --- |
  | Tabulated Mathieu cut-off, a = 0 | q = 0.90804 |
  | This engine, ideal analytic field | q = 0.90684 |
  | **Solved round rods, bisected** | **q = 0.90508 ± 0.00039**, 11 evaluations, 5.5 s |

  **And transmission against resolution, which is Phase 3's acceptance criterion 3.** Hold U/V fixed, scan V, and the width of the stability band *is* the width in mass — q goes as V/m, so a band of relative width dV/V passes one of relative width dm/m.  Both edges bisected:

  | U/V | q low | q high | q centre | R = V/dV |
  | --- | --- | --- | --- | --- |
  | 0.100 | 0.40521 | 0.77519 | 0.59020 | 1.6 |
  | 0.130 | 0.53383 | 0.74246 | 0.63815 | 3.1 |
  | 0.150 | 0.62226 | 0.72179 | 0.67203 | 6.8 |
  | 0.160 | 0.66781 | 0.71203 | 0.68992 | 15.6 |

  **The band closes onto the tabulated apex.** The first stability region's apex is at a = 0.23699, q = 0.70600, so the scan line runs out at U/V = a/2q = 0.16785 — and the centre walks monotonically to **0.68992, 2.28% below**, while R rises tenfold. Both halves are asserted, because either alone is much weaker: a band narrowing onto the *wrong* q is the wrong geometry, and one sitting at the right q that never narrows is not filtering.

  **The first version was wrong for a reason worth keeping.** Its stability criterion was "did the ion strike a rod within twenty RF cycles". Near the low-q edge the instability is weak and takes far longer than that to grow past the inscribed radius, so it **called the whole low-q region stable** and the bracket had no edge in it — the search correctly refused, saying both ends were inside. The criterion has to be reaching the *detector*, and the window has to be the transit time: **a stability test whose window is shorter than the instability's growth time measures the window.**

  **What Class B still lacks needs an arbitrary waveform, not more analysis.** The secular frequency spectrum and isolation efficiency against notch width are §12 items that §9's arbitrary-waveform excitation would unblock; this build has sinusoid and rectangular only.

- **GAS-1: the gas can vary from place to place.** A single declared vector is a stream, not a jet. Spec figure 4 requires a velocity **field** above 1e-2 mbar and §21 lists "gas velocity import" among Phase 3's deliverables, and GAS-1 says why in its own words: the neutral jet off an inlet capillary "drags ions and frequently dominates the axial DC gradient", and it is not uniform across a ring stack.

  **VTK ImageData, which is what this engine already writes** — no format to decide and no dependency to take, because reading a *format* carries no licence obligation while linking a library would (RND-13). The path resolves against the **model document's own directory**, not the working directory, so a model means the same thing wherever the command is run from. **ASCII only, stated rather than discovered**: binary, appended and compressed payloads are most real VTK files and none is read, so such a file is refused by name with the ParaView setting that fixes it — the same kind of deliberate subset as EXT-7's JSON Schema one.

  | Check | Result |
  | --- | --- |
  | An imported *uniform* field against a *declared* uniform one | agree to **2 ulps** |
  | Trilinear against a linear field | exact, 1e-9 over the box |
  | An accelerating flow against uniform at each end | strictly between, both ways |
  | A file this engine wrote, read back | every node exactly |

  The first is what makes the import trustworthy: two entirely separate paths to the same gas — a vector in the document, and a file read, interpolated and sampled per node — give the same answer. **Two ulps rather than bit-identical, and the reason is worth knowing:** interpolating a constant returns that constant only to rounding, because 30(1−f) + 30f is 29.999999999999996 for plenty of f. Inherent to sampling, not fixable in a reader.

  **A caller that cannot resolve the path is refused, not run in a still gas.** Resolving needs the model file's directory, and a study or a figure of merit meets the transport without one — which is precisely the shape of the bug where `driftVelocity` was honoured by the event-driven mode and silently dropped by the diffusive one. **The overhang is reported rather than absorbed**: outside the imported extent the edge value is continued, right for a stream and wrong for the end of a jet, and the samples do not say which, so `gas.flow-imported` states what fraction of the tracked region was extrapolated. One bug found writing it, in my own overhang arithmetic: the helper conflated a **flat axis** (a 2-D import makes no claim about z, so it covers all of it) with **no overlap** (covers none), so a box far outside the field reported itself as fully covered.

  **Einzel consumes a velocity field and does not compute one** — the same boundary §17 draws around visualisation. A compressible flow through a differentially pumped stack is a CFD problem, and a half-hearted one inside an ion-optics engine would be worse than none because it would look like an answer.

  **Both gaps named here are now closed** — the event-driven mode carries the ion's position into the draw, and the pressure is a field too. See below.

- **A clamp that guarded nothing and capped the drift, found by an expectation that was a division.** Scharfetter–Gummel's exponent `P = v h / D` feeds a Bernoulli function that already handles a large argument **exactly** — zero above +40 and `−x` below −40 are the true limits, taken explicitly to avoid an overflow inside `exp`. The flux clamped `P` to ±40 *before* calling it. Read together the clamp looks like it protects the exponential; it does not, the exponential protects itself one function down. What it did was **cap the effective drift at `40 D / h`** whatever the field and the gas actually were.

  | | |
  | --- | --- |
  | Cell Péclet on the corpus drift tube, field alone | 25.4 |
  | The same with a 120 m/s gas flow | 42.3 |
  | Closed form `L / (μE + v_gas)` | 126.7 µs |
  | Measured, clamped | 135.1 µs, **6.7% long** |
  | Measured, unclamped | 127.8 µs, **0.86% long** |

  The 0.86% is the packet's own spread, and what makes that convincing is that it is now **the same 0.86% with and without the flow**: a residual independent of the drift speed is a packet effect, one that grows with the drift is a scheme effect, and only having both cases separates them.

  **Every advection test in the suite runs at cell Péclet 16, below the cap, and reports 1.000000.** They were correct and could not see this. What saw it was a corpus example whose expected number is a *division* — a drift tube with a **declared** mobility and a declared gas flow, so there was nothing for the engine to agree with itself about. The suite now has a case at cell Péclet 105 and 209, exact to a part in a million. The stability step needed no change: the Courant limit was always taken against the true drift, so removing the cap makes the flux agree with the step rather than sitting conservatively under it.

  **Three rules generalise**, all in `docs/lessons.md`. A guard placed one level above the thing that already guards itself is not redundant, it is a second policy, and the outer one wins silently. **A test whose parameter sits below the threshold of a bug is not a weak test, it is a test of a different regime** — where a scheme has a dimensionless number in it, the tests should straddle the values that number switches behaviour at, and should print it. And an expectation that is arithmetic the engine had no part in catches a class of thing self-consistency cannot, which is the whole argument for EX-1's corpus.

  **Corpus 17 → 20**, and the diffusive mode has examples for the first time because `transitTime` made its principal scalar assertable: `drift-tube-diffusion` against `L/(μE)` at 0.86%, `drift-tube-gas-flow` against `L/(μE + v_gas)` at the same 0.86%, and `slit-transmission` against **erf(a/σ√2) = 0.68269 at 0.17%** — the slit's jaws both grounded so the field is exactly zero and the transmission is pure geometry.

  **And a flaky test made diagnosable.** `AllocationDoesNotGrowWithStepCount` failed inside the full parallel suite and passed alone, which is the worst way for a test to be wrong because it reads as a regression in the thing under test. It now takes the **cheapest of five runs** — the runtime charges one-off costs like a tier-1 recompilation to whichever window they fire in, and the property is a floor, so the minimum is the right statistic — and prints the numbers: 240 bytes over 41 steps, 240 bytes over 2030.

- **`multipole-guide` — every even order in one file, and the overlapping rods that found a gap.** LIB-1's test run deliberately: what does a multipole above four rods cost below `Einzel.Library`? **One function, and it was general.** A 2n-pole is 2n rods at π/n intervals, and the expression grammar had **no trigonometry** — so that geometry could not be written at all. Not awkwardly, not verbosely: not at all. The choice was three near-identical template files with coordinates longhand, or `cosPi`/`sinPi` in the grammar.

  **Half turns rather than radians**, the convention the drive decomposition already chose and for the same reason: `Math.Cos(Math.PI/2)` is 6.1e-17, so a rod at a quarter turn lands a hair off axis and the multipole carries a spurious dipole made of rounding. `cosPi(0.5)` is exactly zero.

  | poles | electrodes | basis solves | cycles | convergence |
  | --- | --- | --- | --- | --- |
  | 4 | 4 | **1** | 8 | 0.0262 |
  | 6 | 6 | **1** | 8 | 0.0285 |
  | 8 | 8 | **1** | 8 | 0.0236 |
  | 12 | 12 | **1** | 8 | 0.0257 |

  **Twelve rods cost what four do**, because adjacent rods are exact negatives however many there are. Exact negation is what does it — which is why the amplitude is `rfAmplitude * (1 - 2 mod(pole, 2))` rather than a cosine of the pole index: the second would be right to a rounding and would split into two channels.

  **The rods have to fit, and now they cannot not.** `rodRatio ≤ sin(π/N)/(1 − sin(π/N))` — 2.414 at four rods, 1.000 at six, 0.620 at eight — so the knob is `rodFill`, a *fraction* of that maximum, and an overlapping geometry is not expressible rather than merely refused. `rodFill = 0.475` reproduces **Denison's 1.1468** at four poles through the derived chain, which is a sharp check on `sinPi` as well as on the geometry.

  **A gap found by getting that wrong first.** Applying Denison's *quadrupole* ratio to six rods puts them through one another, and **the engine solved it, converged in eight cycles, and returned a field** — the acceptance measurement taken from it was really a measurement of rods closing in on the axis. A Dirichlet mask is written electrode by electrode, so where two overlap the last one wins; where both hold the same thing that is harmless and often deliberate (a fillet is built that way), and where they **disagree** the region is simultaneously at +300 V and −300 V and the field is of a geometry nobody described. `ElectrodeOverlap` now refuses that, naming both electrodes and what each holds, with three deliberate limits — tangency allowed, agreement allowed, edge profiles skipped rather than guessed at.

  **What is deliberately not claimed:** whether a higher order accepts a larger offset. The template launches on a 45° diagonal, which for a quadrupole is the *widest* gap between rods — an ion enters at r = 4.95 mm and still arrives, outside the 4 mm inscribed radius — and for a hexapole is not, so the comparison measures the angular gap at least as much as the order. Measured anyway for the record: at 200 V the hexapole accepts 0.68 r₀ and the octupole 0.58, and at 300 V that **reverses** to 0.46 and 0.48. A non-monotone ordering that flips with amplitude is a sign the scanned variable is not the one that matters; settling it needs a scan over launch *angle* and an acceptance defined as a solid angle rather than one ray.

- **`paul-trap` — the 3-D quadrupole trap, a figure of merit that is not an arrival, and a boundary that was not one.** A driven ring between two earthed endcaps on the axis of rotation. **Axisymmetric, so it is a half-plane solve rather than a volume** — SYM-1 is what makes a three-dimensional trap cost what a cross-section costs — and because the endcaps are earthed, three electrodes reduce to **one basis solve** (10 cycles, factor 0.0587).

  **A trap cannot be measured by anything that counts arrivals.** A trapped ion never arrives anywhere, so a transmission reads zero for a trap that works and zero again for one that lost everything. `confined` is the complement — still inside at the end of the hold, having struck nothing and reached no detector — and the model puts its detector **outside** the trap so the three outcomes stay distinct: struck, escaped, held.

  | | |
  | --- | --- |
  | Ejection boundary, 0.3 mm launch, 200 cycles, 128 × 64 | **672–674 V**, q_z = 0.8218–0.8236 |
  | The same at 256 × 128, and at 800 cycles | **unchanged** — mesh- and hold-converged |
  | Tabulated Mathieu, a = 0 line | q_z = 0.90804 |
  | Where the ion is lost | an **endcap**, at exactly ±z0 |
  | Effective r0 from the solved field | **3.8195 mm** against 4.0000 declared |
  | Boundary a scale factor alone would predict | **677.5 V**, q_z = 0.828 |

  **Most of the 9.4% shortfall is one number, and the rest is not.** Flat annuli at the nominal radius lie *inside* the hyperbola sharing their vertex everywhere except at that vertex — at z = 2.23 mm the ring hyperbola would be at r = 5.09 mm and this ring is at 4.00 — so the field at the centre is stronger than r0 implies, the effective radius is smaller, q per volt is larger, and ejection comes at a **lower** amplitude. That accounts for the sign and for 0.828 of the 0.908. The same samples give the anharmonicity free: dEz/dz ÷ dEr/dr is exactly −2 wherever the quadratic dominates, and drifts −1.9867 → −1.9461 as the sampling radius doubles. **A hyperbolic trap would hold −2 everywhere by construction**, so a departure *growing with radius* is the higher multipole flat electrodes buy — and that growth is what the test asserts, because a departure that did not grow would be discretisation or a bug.

  **And the ejection edge is amplitude-dependent, which an ideal boundary cannot be** — q_z = 0.85 at a 0.1 mm launch, 0.82 at 0.3 mm, 0.64 at 0.6 mm, all hold-converged (800 and 2000 cycles agree at both offsets). The Mathieu equation is linear, so a trajectory scaled by a constant is another trajectory. My first write-up said the scale factor "closes to under 1%" on the strength of the 0.3 mm offset alone, which is the mistake of quoting a number whose controls have not been run.

  **The reason is structural, and the fix is to stop measuring it that way.** A boundary found by asking *did the ion reach an electrode* requires the ion to cross the whole anharmonic region, so it is never a small-amplitude measurement whatever it was launched at: a small launch survives past the linear edge because the anharmonic frequency shift halts the growth before z0, and a larger one is lost below it.

  **β needs no journey, and it settles it.** Calibrating β(V) against Mathieu over a range where the ion stays small — β is amplitude-independent to 2e-3 there, and measurably shifted at high q, which is the control — fits one number to a worst residual of **1.2e-3** and gives an effective radius of **3.8137 mm** against **3.8195** from the field's curvature with no ion involved. Two routes sharing nothing but the solved field, **0.15% apart**, and in the direction the curvature's own δ-dependence predicts. That puts β = 1 at **675.5 V, q_nominal 0.8254**, which the two ejection edges **bracket**: 665–670 V at 0.3 mm, 695–700 V at 0.1 mm.

  **So the 9.4% shortfall is one geometric factor, measured three independent ways.** A caveat about the tool, though: the endpoint is anchored to the *tabulated* 0.90804 rather than to the continued fraction used elsewhere here, because that expansion has a near-singularity exactly at β = 1 — its n = 1 denominator (β−2)² goes to one — and puts the crossing at 0.9117, four parts in a thousand off. It is accurate at β from 0.3 to 0.8 and not at the endpoint.

  **Two quantities, not one, and only one is the design parameter.** A *linear* stability boundary is where β reaches one and is a property of the field; an *ejection* threshold is where a particular ion launched a particular way leaves within a particular hold, and is what an instrument does. Conflating them is what made the first account confusing.

  **A resonance band inside the stable region, found by the confirmation walk on its first real use.** 605–614 V (q_z = 0.739–0.750), sixty volts below the main edge. Identical at 256 × 128 and at 400 cycles; **gone** at 60 cycles (so the growth is slow and secular, not exponential) and **gone** at a 0.1 mm launch (so it is driven by the higher multipoles). That combination is a nonlinear resonance and nothing else. **Which** resonance is not established — β_z there is 0.615, landing on no n_zβ_z + n_rβ_r = 2 up to order six — and it is recorded as measured rather than explained. The bisection that found the main edge reported nothing unusual, and structurally could not have.

  **The finding worth keeping: a stability boundary is not a property of the design alone.** At 60 RF cycles the ejection edge is a ragged strip — held at 674, lost at 676 and 678, held at 680, lost at 682, held at 684, solid loss from 690. At 200 cycles the same scan is a clean step between 672 and 674. Nothing about the design changed: the growth rate goes to zero at the edge, so whether a marginally unstable ion reaches an electrode inside the hold is a property of *the hold*. Two bisections over brackets differing only at their lower end gave **680.7 V and 694.4 V for the same geometry**, which is how it was noticed at all.

  **So `einzel boundary` now checks its own premise.** Every step of a bisection is consistent with a single crossing *by construction*, so a clean edge and a frayed one produce identical histories and the result looks equally confident either way. It now walks outward from the converged bracket at geometrically growing offsets asking whether the predicate flips back — about log2 of the range over the bracket width, roughly doubling an 11-evaluation search against a grid's 501. `boundary.multiple-crossings` is a **validity violation**; the confirmation is reported with its probe count whether or not anything was found. Two limits stated rather than papered over: the walk stays inside the declared range, and its first step is the bracket width, so a flip narrower than that is stepped over.

  **What it cost below the library was a bug, not a capability — and it is the third time the same mistake has been made.** `ModelValidator.CanDoWork` decided whether a source may start at rest by asking whether any electrode held non-zero **DC**. A Paul trap holds zero DC and all of its potential as drive, so the archetypal start-at-rest device was refused as one in which nothing could move an ion; the 3-D arm of the same switch inspected nothing at all and passed by default, the same bug wearing the opposite mask. `einzel solve` had already been wrong this way twice — reporting the DC pattern for every driven 2-D geometry, and answering `converged: true` over an empty list of 3-D elements. **Reading only the DC of a driven electrode is a recurring mistake here**; grep for `.Potential` the next time something driven behaves as though it were earthed. Recorded in `docs/lessons.md`.

  Also fixed while here: **`InferProjectRoot` fell back to the working directory**, so a study kept outside any project wrote `results/` into whatever tree the caller happened to be standing in and said only "wrote results\x.json". It now falls back to the file's own directory.

  Corpus 21 → 23 (`paul-trap-held` at q_z = 0.30, `paul-trap-ejected` at q_z = 1.20 — bracketing the published boundary from both sides, deliberately wide, because an example that pinned the edge would be pinning this geometry's edge and calling it Mathieu's). Details in `docs/device-templates.md` and `docs/optimisation.md`.

- **The secular frequency spectrum, and the resonance it named.** §12 lists it as a Class B figure; the reason to build it now was that the trap above had a loss band nothing could identify. **A nonlinear resonance is *defined* by a frequency condition** — `n_z β_z + n_r β_r = 2` for a multipole of order n_z + n_r — so a scan over amplitude can establish that a band exists and can never say what it is.

  **Lomb–Scargle rather than a DFT, and that is the load-bearing choice.** A trajectory is sampled at accepted integration steps, which cluster where the physics is hard — `TrajectoryRecorder` working as designed — so the series is **not uniform**. A DFT would need it resampled first, which is inventing values the integrator never computed and then measuring them. Lomb–Scargle is the closed-form least-squares fit of a sinusoid at each trial frequency and needs no such step.

  Checked against the Mathieu characteristic exponent — a continued fraction in a and q, evaluated **in the test** rather than shipped, because comparing the engine's β to the engine's spectrum would be testing self-consistency:

  | q | β | expected | measured |
  | --- | --- | --- | --- |
  | 0.10 | 0.070850 | 35.425 kHz | 35.374 kHz (−0.144%) |
  | 0.30 | 0.216059 | 108.030 kHz | 108.037 kHz (+0.007%) |
  | 0.50 | 0.373744 | 186.872 kHz | 186.937 kHz (+0.035%) |
  | 0.70 | 0.563066 | 281.533 kHz | 281.500 kHz (−0.012%) |
  | 0.85 | 0.772950 | 386.475 kHz | 386.507 kHz (+0.008%) |

  **The sidebands are the sharper check**: finding the lowest line right says the slow motion has the right frequency, but finding the drive split into a *pair* straddling it — 813.19 against 813.13 kHz, 1186.94 against 1186.87 — says the motion has the form Mathieu's solution gives. The reported uncertainty is 1/T, because a record of finite length cannot locate a line more finely however fine the trial spacing; `spectrum.short-record` and `spectrum.peak-at-band-edge` say when that bites.

  **The band is the octupole.** β_z = 0.6769, β_r = 0.3225, so **2β_z + 2β_r = 1.9989** — order four, met to 0.055% at the band centre and a hundred times worse either side. Nine candidate conditions are searched and one is always nearest, so what makes this an identification rather than a fit is that **order four was predicted in advance**: the trap is symmetric about its centre plane and about the axis, so every odd multipole vanishes and four is the first available. It is independently corroborated by the field, where the curvature ratio departs from −2 by an amount that grows with radius.

  **And the effective radius is now confirmed twice over, from routes sharing nothing but the solved field** — the curvature at the centre with no ion involved (3.8195 mm), and the secular line of a flown ion against Mathieu at q × (r0/r0_eff)². They agree to **0.02% at q = 0.27**, 0.04% at 0.40, 0.28% at 0.54 — and diverge to 3.1% at 0.80, which is the other half of the same statement rather than a failure: the trap is an ideal quadrupole of radius 3.82 mm *to the extent the ion stays small*. That is why the stability boundary cannot be predicted from the effective radius alone — a boundary measurement requires the ion to travel all the way to an electrode, through the anharmonic region.

  **The ideal-Mathieu prediction was wrong and had to be**: β at the *nominal* q of 0.745 is 0.6156, which satisfies no low-order condition. Working in the nominal geometry rather than the solved one is what made the resonance unidentifiable in the first place.

  Exposed as `secularFrequencyX|Y|Z`, so a study can scan or optimise against it. Refused with `secular.no-drive` for a static field — a secular frequency is the slow oscillation in an RF field's *effective* well, and a static field has no such well. The band searched is 2–90% of the drive, taken from the field's own shortest period and stopping below the drive deliberately, because micromotion at the drive is the largest line in most spectra and reporting it back would be reporting the input as a discovery. Details in `docs/validation.md`.

- **The arbitrary waveform, and the last Class B figure.** §9 lists an arbitrary waveform among the excitations an electrode may carry; §12's isolation efficiency against notch width cannot be measured without one. It is a **Fourier series**, which is not a restriction — every periodic waveform is one — and is the natural spelling for the thing being designed: a notch is a list of harmonics with a gap in it, where a sampled table is a waveform someone has to inverse-transform first. It is also smooth by construction, where a table is piecewise something and a jump in the drive is a jump in the acceleration. **One basis solve however many harmonics**, since a sum of harmonics is still one scalar function of time.

  **Three reductions it must satisfy, and it does.** A single order-one term *is* the sinusoid (6.1e-16 over 721 phases). A half turn of phase is *exactly* antiphase where the argument is representable (exactly 0 at phases k/16; 1.0e-14 at arbitrary phases across orders 1–17 — the honest limit, since `2(nt + ½)` is itself rounded and the error grows with order). And the Fourier series of a square wave converges on it with textbook Gibbs — interior 0.182 → 0.051 → 0.013 at 5/20/80 terms while the **edge overshoot holds at 0.182/0.179/0.177**, which is asserted too, because a series showing no overshoot would not be a Fourier series.

  **The literature check**: the 80-term series recovers the published digital-mass-filter cut-off by the same bracket as the direct rectangular wave — last through 0.710, first lost 0.715, containing Schrader/Anderson/Russell's 0.712. That is what says the path *drives an ion* rather than evaluating to the right numbers. **A mistake worth keeping**: the first version wrote the series with zero phase, but a square wave's series is a **sine** series — so it was a square wave shifted a quarter cycle, which converged perfectly well and moved the cut-off to ~0.703. A reduction that converges to the *wrong thing* is worse than one that does not converge.

  **Isolation efficiency against notch width, measured.** A mass axis is a frequency axis (q goes as 1/m), so a notch in frequency is a window in mass: with the target at m/z 500 and a notch over comb orders 71–75, masses 490/500/510 survive and 420/460/545/600 are ejected. **The trade needs two amplitudes to show both arms** — at the amplitude that just ejects a resonant ion the narrow end is free and efficiency is monotone (1.00 → 0.50 → 0 → 0); at three times that the narrow end loses the target and an **interior optimum appears at half-width 6, efficiency 0.75**. A study run at one amplitude reports the wrong shape and is internally consistent.

  **Two scales that had to be derived, both first got wrong by orders of magnitude.** The comb spacing must equal **1/T** — a resonance excited for time T has width 1/T, so a wider comb has *holes* and an ion between lines survives; at 5 kHz against a 333 Hz width the notch width toggled every ion at once. And the amplitude follows from `E = 2amω/qT`, 76 V/m here; 300 V/m ejected everything at every width. **An amplitude picked to make a demonstration work is a demonstration of the amplitude.**

  **And a defect found on the way, the third of its exact shape.** `SuperposedField` implements only `IElectrostaticField`, and a driven member answers that interface at t = 0 — so **a driven element summed with anything else silently became a snapshot of the RF at the top of its cycle**, with no exception and nothing in the result to say so. The diffusive mode was found doing this, `einzel solve` was found doing it, and now this. The class is: **a time-varying quantity reached through a time-free interface does not fail, it answers at an arbitrary instant.** Fixed structurally — `FieldAssembly` picks `DrivenSuperposedField` when any member is driven, so the composition is chosen by what it contains rather than by what the caller asks for. Step control follows: the shortest period is the minimum over members, and `OscillatingUniformField` reports its **highest harmonic's** period rather than its fundamental's, since a comb reaching order 120 carries information 120× faster than its repeat rate.

  **What it does not reach: the model format cannot declare a supplementary excitation.** A `solve` carries one `drive` with one frequency, so the notch measurement runs on the analytic quadrupole. Same limitation the travelling-wave guide has. The fix is **a drive per supply rather than per solve** — the decomposition already groups electrodes into supplies by spatial pattern, and a supply is exactly the thing that has a frequency, so it costs one level of schema nesting and nothing in the solver. Details in `docs/validation.md` and `docs/model-format.md`.

- **Several generators on one geometry — and the design note that had to be retracted to get there.** `CompiledDrive` said, from the beginning: *"one drive per solve ... modelling it the other way round would let a document declare two frequencies on one structure — which is a different instrument and almost always a mistake."* **It is not a mistake, it is what a trap is.** A real travelling-wave guide superposes a fast confining RF on a slow travelling wave; a stored-waveform isolation runs a notched comb across the endcaps while the ring carries the main drive. The rule cost the shipped guide its radial confinement and forced the notch measurement onto an analytic quadrupole.

  A `solve` now declares `drives` and each electrode `taps` them **by name**. Both spellings coexist — `drive` and `driveAmplitude` stay the short form for the common single-generator case — and **declaring both is refused rather than merged**, because a document saying a geometry has one drive and also three is not a document with a default to fall back on.

  **It cost nothing in the solver, which is the part worth keeping.** Basis superposition is indifferent to what the weights are functions of, so two generators reaching the same electrodes in the same proportions are **one solved pattern carrying two weights on two clocks** — exactly as a DC supply and an RF supply already were. Measured on the guide: **24 rings each tapping both generators → 3 basis solves**, two for the wave's phase ramp (a sinusoid collapses to a fixed quadrature pair however many rings) and one for the alternating confinement. The quadrature collapse is now decided **per generator**, since an instrument may run a sinusoidal confinement and a switched excitation at once.

  **It does change step control**: `ShortestPeriodSeconds` is the minimum over generators, so the guide reports **333.33 ns** — its 3 MHz confinement's period, not its 0.5 MHz wave's.

  **And the negative result is worth as much as the capability.** Giving the guide its confinement did **not** widen the acceptance at any amplitude tried — 5 of 12 entry radii arrive with none, then 2 / 4 / 3 / 1 at 100 / 200 / 400 / 800 V, and 1 at half the frequency. The window is narrow at both ends: above ~200 V on this ring pitch the confining drive's own Mathieu q passes the stability limit and the ion is **ejected**, while below it the well is shallow against a 60 V wave and the alternating field decays as exp(−2πr/pitch) so little of it reaches the axis. **The template ships with the confinement at zero volts** — a default that makes a device worse is worse than no default. What the tests assert is that the generator *reaches* the ion (acceptance differs with it on), which is the claim the capability supports; whether a working point exists is a two-dimensional study in wave and confinement amplitude.

  **A statistic that had to be replaced on the way**: "widest entry radius that still arrives" gave 0.65 mm on one radius grid and 0.20 mm on another for the same geometry. A maximum over a ragged set is a maximum over noise; counting arrivals over a fixed grid is the same measurement made stable.

  **Now done for 3-D too**: a `solve3d` takes `drives` and its electrodes take `taps`, so a volume geometry expresses what a cross-section already could. **Shared rather than reimplemented** — both electrode documents implement one `ITappedElectrode` interface and the tap validation is one function, so the refusals for declaring both forms arrived in three dimensions by *being* the same code. Verified on a volume geometry: two generators reaching the same electrodes in the same proportions collapse to **one** basis solve carrying two weights on two clocks, and two distinct spatial patterns give **two**. Details in `docs/model-format.md` and `docs/device-templates.md`.

- **A gas that moves, in the event-driven mode — GAS-1's last transport gap.** The diffusive mode could see an imported velocity field; the trajectory models **refused** one, and refusing was right at the time: a collision was drawn from a time and a velocity with no place to evaluate the flow at, so the alternative was a run that used the uniform drift and said nothing. The change is one argument — the ion's **position** goes into the draw, so a collision samples the Maxwellian about the bulk velocity *where the ion is*.

  **Checked against `u + μE`**, which is a closed form the engine has no part in, and taken as a *difference* so it cancels the collision model, the cross section and the temperature:

  | | along the flow | across it |
  | --- | --- | --- |
  | still gas | −5.405 m/s | 1005.209 m/s |
  | moving at 120 m/s | 114.595 | 1005.209 |
  | **difference** | **120.000** | **−0.000** |

  And the control: a `UniformGasFlow` and a declared `driftVelocity` are the same gas said two ways, so on the same seed they must give the same *trajectory* rather than the same average — **1e-9**. A stepped flow (still below a plane, 200 m/s above) gives **204.5 m/s** of carry across the step.

  **The trap that removing a refusal set, and it nearly shipped.** The trajectory path built its gas with `BackgroundGas.FromModel`, which does **not** resolve a declared `velocityField` — only the diffusive path called `GasFlowImport.Resolve`. Lifting the refusal without also resolving there would have reintroduced *exactly* the failure the refusal existed to prevent: a model declaring a jet, flown as though the gas stood still, silently. **A guard is removed correctly only when the thing it guarded against is checked for directly.**

  **And two things the sampler knew that nobody read.** `BoundExceeded` and the new `SampledOutsideFlow` were computed and consumed by nothing — the third time evidence about a computation's own quality has been dropped at a seam here, after `FieldAssembly.Build` discarding its `SolveReport` and the sweep evaluator discarding its warnings. Both now reach the result: `collisions.rate-underestimated` (a biased collision rate looks exactly like a correct one) and `gas.flow-extrapolated` (outside the imported box the flow is the edge value continued, which is right for a stream and wrong for the end of a jet).

  **A measurement mistake worth keeping**: the stepped-flow test first put the step 3 mm from the launch, which the ion crosses in 6 µs — so the "before" average was three samples of an ion still accelerating from rest, and the carry read 361 against a declared 200. That looks like a physics discrepancy and is a launch transient. **An average is over whatever the window contains, including the part that is not yet the thing being measured.**

  Details in `docs/pressure.md`.

- **The pressure is a field too, and mobility goes as 1/n — which nothing here did.** The density was the last quantity about a gas held as a single number for a whole model, so an imported flow gave the neutrals a velocity everywhere and *the same number of them everywhere*. That is not a differentially pumped instrument, which is what every device above 1e-2 mbar actually is: a funnel behind an inlet capillary spans decades of pressure between entrance and exit.

  **The unit is required on the file, and that is §9's rule rather than a new one.** A CFD velocity is metres per second essentially always; a pressure is not, and **a file read as pascals when it holds mbar is a gas a hundred times too thin** — entirely plausible, and it never announces itself. `{"energy": 4000}` is a validation error for exactly this reason, and nothing in that argument weakens when the number becomes a hundred thousand numbers.

  **The physics that was missing:** an ion drifts further between collisions in a thinner gas, so **μN is the constant** — which is why the literature tabulates *reduced* mobility. The declared `pressure` becomes the reference the mobility belongs to and the field grades away from it. Two separate density dependences, and they are not the same one: this factor is how *much* gas, the existing E/N expansion is how hard the ion is pushed *between* collisions. Scaling only the second leaves the drift flat across a gradient while reporting a changing field dependence, which reads as the mobility having been handled.

  **A graded density turns Langevin into a null-collision method** — the same mechanism hard spheres already ran for a speed-dependent rate, reached a second way: schedule at the highest density anywhere, accept with probability n(x)/n_max. Both bounds are majorants over the whole field, because an event is scheduled before it is known where the ion will be when it lands. The thinning is short-circuited where the density is uniform, and that is **load-bearing rather than an optimisation**: it would otherwise accept with probability exactly one and *still consume a random draw*, moving every seeded result this engine has published.

  | | |
  | --- | --- |
  | A field at 2× declared pressure vs *declaring* 2× the pressure, event-driven | **bit-identical trajectory**, both collision models |
  | The same, diffusive, through the CLI | 3515.229021382981**5** vs **1** µs |
  | Mobility at half and twice the reference density | 2.000000 / 0.500000 |
  | The scaled form at the declared density | **bit-identical** to the unscaled one |
  | Langevin thinning at three points of a 4× ramp | 0.25 / 0.625 / 1.00 to 0.01 |
  | 151 existing transport tests | unchanged |

  **Two tests that had no teeth, found by running the mutation rather than by reading them.** The equivalence test used Langevin only, and making the local density read return the declared scalar *did not fail it* — the Langevin branch short-circuits its thinning where the density is uniform, so a flat imported field never reads a position at all. Correct behaviour, and no test of the read. The graded-gas test asserted only that a ramp collides more than the thin gas alone, which still passes with the density read at the wrong place because the count lands *close to* the thin gas. What discriminates is **reversing the ramp**: same densities, same box, opposite arrangement, so anything blind to position gives the two an identical count. 11,458 against 19,700. The rule that generalises: **a test passes a mutation when the path it exercises does not contain the mutated line** — so read which tests failed, and treat the rest as untested rather than as corroboration.

  **And the same seam broke a fourth time, in the file whose comment says it is the third.** `SampledOutsideDensity` was added to `CollisionSampler` beside `SampledOutsideFlow` and, on the first draft, dropped in exactly the place the surrounding comment warns about — declared, set, read by nothing, everything compiling and every test green while a run extrapolating its gas past the imported box said so nowhere. Now `gas.pressure-extrapolated`, with a CLI test driving it end to end because the wiring is what keeps breaking rather than the computation. **Adding a quantity to a type that already reports several is not the same as reporting it** — the existing reporting code is where the eye slides past.

  **The cost gate had to be re-derived, and the first version was 50% out.** GRD-8's claim for this mode is that `estimate` and `run` call the same step function and agree exactly. A graded gas moves the mobility and so both stability limits — and the first version took the thinnest gas *anywhere in the imported field* where the run takes its limit from per-node arrays *over the tracked grid*. A CFD field is usually solved on a larger box than the ions are tracked through: here it ran to 0.5 mbar while the grid reached 0.75, and the estimate said **2,252 steps against an actual 1,502**. Now 1,126/1,126 uniform and 1,502/1,502 graded. Found by comparing the two numbers, not by reading the code — nothing about the wrong version looks wrong. The same asymmetry runs through the diagnostics and had to be right in both directions: **E/N is worst where the gas is thinnest**, the **Knudsen number and collision counts are worst where it is thickest**.

  **The pseudopotential is graded too.** The momentum-transfer rate goes as the density, so a funnel whose gas thins toward its exit has a well that deepens toward its exit — and the gradient of that is a real force, which differencing the effective potential picks up. A version holding the damping at one declared value would report the well as flat where it is not.

  **A refusal moved to where it cannot be forgotten.** Resolving a declared field needs the model document's own directory, which a study or a figure of merit does not have. Refusing was right, but it lived as a guard at each of **four call sites, naming `velocityField`** — and three were already silent about a pressure field. `BackgroundGas.FromModel` now refuses an unresolved field itself, with `WithoutImportedFields` as the deliberate exception whose name says what it gives up. It caught a real one immediately: `GasFlowImport.Resolve` reached `FromModel` itself, so `einzel run` on a diffusive model was refused by the guard meant for callers without a path. Two sites that *did* have the path — `einzel compare` and the diffusive cost estimate — now resolve rather than refuse, which they should always have done. Same rule as `FieldAssembly.Build` throwing rather than discarding its `SolveReport`: **make the shortest spelling the safe one.**

  **Also caught by its own test:** an earlier draft of the CLI test ran at 50 V/m over 38 mm, collected **0.05 ions of 10,000**, and compared two flight-time ceilings — the incomplete-arrival trap this project already documents for `einzel compare`, met again from the other side. The transit accessor now asserts the packet arrived before reading a transit off it.

  **The corpus can carry a data file now, and does.** The embedded-resource glob was `*.json` only, so neither imported gas field could appear in an example — and so neither was covered by the EX-2 gate that runs on every change. `drift-tube-pressure-gradient` is a 38 mm tube whose gas thickens from 1 mbar at the packet to 2 mbar at the detector, and **its expected number is an integral**: the transit is the uniform answer scaled by the *mean* density along the path, which for a linear ramp is the average of the ends. Predicted 316.667 µs, measured **320.236, 1.13% out** — the packet's own spread. Ignoring the gradient gives 211 µs, a third away. What it deliberately cannot see is the *arrangement*, since a drift transit depends only on the integral along the path and any reflection gives the same answer; that is pinned separately by the reversed-ramp unit test. Corpus 29 → 30.

  **It needed one change below the corpus, and that is the more useful half.** `einzel test` could not test a model with an imported field at all — the seam between a study and the transport is a `Func<CompiledModel, double?>` with nowhere to put a path, so a figure of merit met `BackgroundGas.FromModel` and was refused. A compiled model now carries **`SourceDirectory`**, set by every loader, so any consumer can resolve a referenced file. **Null stays the safe value**: a model compiled from a string has no directory and its consumer is refused rather than run in a gas the document does not describe, so a loader that forgets degrades to the refusal rather than to a silent wrong answer. **And the four study drivers take it too**, so a sweep, scan, optimisation or boundary search over such a model runs rather than refusing — §13's whole subject is a design being optimised, and a device with a gas jet through it is exactly the kind that wants it. The warning survives that seam: the ledger reports `gas.pressure-imported` with its per-evaluation count.

  **Still assumed: one temperature.** What is imported is a pressure field read as a density through n = p/kT at the model's single declared temperature — an assumption the document already made, but now the only thing about a gas that cannot vary from place to place. A **non-positive sample is refused rather than clamped**, because mobility goes as 1/n: a zero is an infinite drift and a stability limit of zero, so the run does not answer wrongly, it never finishes. Details in `docs/pressure.md`.

- **Corpus 23 → 26, exercising what the night built.** Three examples, each with an expectation that is arithmetic and nothing else.

  **`gas-flow-carry`** — no field at all, a gas streaming at 200 m/s down a metre of tube. Collisions drive the ion toward the frame the gas is at rest in, so the steady drift *is* the gas velocity and the transit is L/u = **5000 µs by arithmetic**; measured 4904.5. The ion is launched at exactly 200 m/s (0.103642697 V for m/z 500) so there is no equilibration lag either. **The ten per cent tolerance understates how discriminating it is**: with the flow ignored the same ion damps to rest and covers **15.8 mm in twenty milliseconds** instead of arriving.

  **`travelling-wave-capture` and `travelling-wave-ballistic` are a pair, and neither is worth much alone.** Injected at *half* the wave speed: with the wave on the transit is the distance over the **wave's** speed — 27 mm / 3000 m/s = 9.0 µs, measured 8.697 — and with it off, 27 mm / 1500 m/s = **18.000000 µs exactly**, because a guide with no amplitude is field-free and the analytic drift is exact. A transit matching the wave in one case and the injection speed in the other would be a coincidence twice over; a transit matching the wave *whatever* the injection speed is capture. That is the distinction an earlier version of this measurement got wrong by comparing two captured transits to each other.

  Remaining for EX-1: an MR-TOF, a thermalisation, and a three-dimensional geometry.

- **A review of the night's own work, and the defect it found.** Adding a second generator turned `CompiledElectrode.DriveAmplitude` from *the* amplitude into *the first tap's* amplitude, and left it as a property with the same name. Everything kept compiling. One reader was `ElectrodeOverlap.Agrees` — the check that refuses two conductors occupying the same space at different excitations — so **two electrodes agreeing about the main RF and differing about a supplementary one were judged identical**, and the Dirichlet mask kept whichever was written last. The one check that exists to prevent a field of a geometry nobody described had become a route to one.

  Now compared over **every** tap, with order significant (the conservative reading: two electrodes whose taps are the same set in a different order really do hold the same thing, so refusing them costs a spurious complaint rather than a silent wrong field). **Three tests, and all three were run with the fix reverted** — two fail with the bug restored, which is what makes them tests of the bug rather than tests that a file exists.

  Also swept: `CanDoWork` now asks `IsDriven` rather than the first tap's amplitude, and `einzel schema` was checked to carry `drives`, `taps` and `TapTermDocument` — AGT-7 says the format an agent reads cannot drift from the code, and a reflection-generated schema is only as good as the records it reflects over.

  The general lesson, in `docs/lessons.md`: **a convenience accessor that quietly becomes a summary keeps every caller compiling and changes what some of them mean.** After widening a scalar into a list, ask which readers of the scalar were asking a question it no longer answers.

- **The corpus has no 3-D example, and finding out why was worth more than the example.** One was written — a parallel-plate gap checked against `sqrt(2d²m/(qV))`, the same closed form the analytic accelerating-gap example uses. It was not shipped:

  | | cycles | factor |
  | --- | --- | --- |
  | **parallel plates, 2 slabs in a grounded box** | **49** | **0.652** |
  | the shipped segmented quadrupole, 12 rods | 12–13 | 0.08 |
  | a charged sphere, node-aligned coarse levels | 9 | 0.126 |

  **124 seconds** for the plates, against 11 for a whole segmented-quadrupole *run*, and against a gate that does the other twenty-six examples in forty-two. A factor of 0.65 means the V-cycle is barely doing anything.

  **The simplest 3-D geometry anybody would write is the worst case for the documented interior-electrode limitation**, and that is the wrong way round. A rod is thin, so coarsening loses it fast and the pinning fix restores its presence; **a slab is a large solid Dirichlet region**, so a coarse level that half-represents it is solving a different problem over most of the domain rather than a corner of it.

  It was also **3.2% off**, which is the geometry rather than the solver — a finite plate in a grounded box 2 mm behind it is not an infinite capacitor, and asserting V/d to 1% would be asserting that it is. Fixing that needs a *larger* domain, so more solve, not less.

  So the volume solver, its tricubic interpolant and its cut cells are exercised by `Einzel.Fields.Tests` and the segmented-quadrupole study, and **not by the release gate** — a stated gap rather than an oversight. Galerkin coarsening closes it, and would make the plates converge like everything else. In `docs/numerics.md`.

- **Poisson, not only Laplace - the field half of SC-1's approximate method.** Every solve here has been Laplace: a potential with no charge in it, fixed on conductors. Particle-in-cell needs grad2 phi = -rho/eps0. **The cycle already carried a right-hand side and had only ever been handed zeros** - the smoother subtracts it, the residual is defined against it, and the coarse levels get the restricted *residual*, which is what they need whatever the fine source is. One argument, no numerics.

  Checked by **manufactured solution**, the sharpest thing available: pick a potential, differentiate it analytically for the source that produces it, compare. Nothing on the exact side is discretised. With phi = sin(pi x) sin(pi y), Laplacian -2 pi^2 phi:

  | intervals | worst error | order | cycles | factor |
  | --- | --- | --- | --- | --- |
  | 32 | 8.0358e-4 | | 11 | 0.0632 |
  | 64 | 2.0082e-4 | **2.000** | 11 | 0.0659 |
  | 128 | 5.0201e-5 | **2.000** | 11 | 0.0702 |

  **The order is the load-bearing check**: a source entering the smoother and the residual inconsistently would still converge - to the wrong answer - and would show it as an order that is not two rather than as a failure. And a null source gives **exactly** the old Laplace answer in the same cycle count; not nearly, or every number published from a solved field has moved.

  Left for SC-1: cloud-in-cell deposit, the same weights on the gather (or momentum is not conserved), and the comparison against the direct pairwise sum - which exists, and is why it was built first.

- **Cloud-in-cell: the particle half of SC-1's approximate method.** The direct sum costs O(N^2); particle-in-cell costs one solve plus O(N), which is what makes 10^4 macroparticles affordable when 10^3 already takes hours. Charge is conserved **by construction** - the eight weights sum to exactly one whatever the position, so 8.010883e-14 C in is 8.010883e-14 C on the grid, and normalising afterwards would pass the same test while hiding a weighting error rather than preventing one. Charge that leaves the grid is **counted**, not clamped or dropped: a packet off its own grid gives a field quietly too weak, which looks exactly like a packet more dilute than it is.

  **The same weights on the way out, and it is not a convenience.** A particle writes charge to a node with a weight and reads the field back with the same weight, so its own contribution cancels. Measured as a fraction of the field a neighbour one cell away feels, against a nearest-node gather sharing no weights:

  | offset in the cell | matched | mismatched |
  | --- | --- | --- |
  | 0.00 | 8.05e-5 | 0.521 |
  | 0.50 | 1.15e-4 | 0.495 |
  | 0.89 | 1.68e-4 | 0.485 |

  **Three and a half orders of magnitude**, and the mismatched column is what makes the matched one a property of the *symmetry* rather than of the grid being fine. Half the neighbour field felt by a particle from itself is a packet that expands for a reason nobody put in — and a tricubic gather, which is *more accurate* for a smooth field, would do exactly that. **ACC-3 is not violated**: it forbids trilinear interpolation on a *trajectory path*, and this is the interpolation of a self-consistent field whose accuracy the deposit already bounds. The applied field an ion flies through is still tricubic.

  It is **not exactly zero** and the test says so: the cancellation is exact on a uniform periodic grid with centred differences, and an earthed box breaks it slightly through its images. So the assertion is a ratio to the scale that would matter, not a claim of zero.

  A uniform ball of 20,000 macroparticles reproduces Qr/(4πε₀R³) and Q/(4πε₀r²) to 1–8% in 11 cycles at factor 0.110 — the residual being the earthed cube rather than the method, since the closed form is for a sphere alone in space.

  **Left for SC-1:** the integration — which grid a drifting packet deposits onto, when to re-solve, and the comparison against the direct sum on the same configuration. Every piece exists; nothing wires them to `PacketIntegrator` yet. In `docs/numerics.md`.

- **Particle-in-cell, wired to the packet integrator — and an argument of mine that was right about accuracy and wrong about cost.** Both methods are now `ISelfField` peers (positions in, accelerations accumulated out), which is what lets them be handed the same configuration and differenced. SC-1's "validated against" means nothing without that.

  **Three design questions, answered in the code.** The grid is *the packet's own, in the packet's frame* — a packet crossing a metre cannot have a grid over the instrument at any useful resolution — so every deposit and gather is relative to the centroid and **uniform translation is exact** (1e-11 across 250 mm) and free. Because translation is exact, the only thing that ages is *shape*, so the refresh criterion is a fractional change in RMS radius rather than a step count. And the boundary is an earthed box, which a packet in flight is not in; centring it is what keeps that cheap, since a centred distribution induces almost no field at its own centre.

  **The finding.** The cloud-in-cell commit argued that ACC-3's ban on trilinear interpolation does not reach a self-consistent field whose accuracy the deposit already bounds, and that the deposit/gather symmetry buys more than the extra order would. **Right about accuracy, wrong about cost** — a trilinear force kinks at every cell face and an embedded Runge–Kutta estimator reads a kink as error:

  | nodes | steps, linear | steps, quadratic |
  | --- | --- | --- |
  | 16 | 274 | **45** |
  | 32 | 383 | **65** |
  | 64 | 656 | **95** |

  against the direct sum's 25. **The step count tracking the node count is what identifies the mechanism** — more nodes, more faces per unit path; a fixed overhead would not scale. A quadratic B-spline (27 nodes, not 8) is continuously differentiable, is used for deposit *and* gather so the self-force still cancels, and its weights still sum to exactly one for *any* offset — which is what lets the index be clamped at a face without losing charge.

  **Where it starts paying: about 850 macroparticles** — 0.16× at 250, 1.21× at 1000, 3.21× at 2000. Worth stating as a crossing rather than as asymptotics: below it the reference method is simply faster and reaching for the approximation buys nothing.

  **Against the reference**: 0.5% on a flown packet's widening over 2 µs (0.384 → 1.907 mm direct, 1.916 mm grid), about a per cent through the body of a static ball. The outermost radial bin is the worst and has to be — it straddles the surface, where a smoothed deposit and a point-softened sum disagree about a discontinuity by construction.

  **A trade that showed itself**: keeping the box across refreshes needs headroom above the requested padding, and at 1.6× that cut rebuilds from 32 to 4 and cost the surface bin 0.94 → 0.83, because a bigger box at fixed nodes resolves the packet with fewer cells. 1.15× keeps the accuracy at 11 rebuilds.

  **A defect a code review found in exactly this, and the argument of mine that let it through.** The quadratic deposit clamps its three-node stencil onto the grid at a boundary, and leaving the *offset* unclamped with it makes the middle weight `0.75 − u²` **negative** — at the edge the weights are 1.125, −0.25, 0.125. They sum to one, so charge stayed exact and **every test passed at 673 green**; what was wrong is that a positive macroparticle deposited a negative density, and the gather shares those weights. The licensing argument is written down two paragraphs up — "the weights still sum to exactly one for *any* offset, which is what lets the index be clamped at a face without losing charge" — which is true and settles a different question. **Conservation is not positivity.** Clamping the offset too degrades the quadratic shape into the linear one exactly where the third node would leave the grid, which is the right thing for it to do. `NoDepositWeightIsEverNegative` sweeps the whole axis, because the middle is where this cannot happen.

  **Now declarable, and closing that gap broke the agreement claim above.** `"spaceCharge": "pic"` takes an optional `spaceChargeGrid` block (`nodes`, `padding`, `refreshTolerance`), refused against any other method rather than ignored. Two measurements came out of making the knobs sayable, and both matter more than the wiring.

  **A reference method has approximations in it too.** The direct sum softens at the mean macroparticle spacing; the grid smooths at the cell. So "they agree to a few per cent" was comparing two different smoothing lengths that happened to be comparable — agreement there is a coincidence of magnitudes and disagreement would not have been evidence of a defect. The sum has a limit it can be taken to (softening/100, worth **3.5%**) and the grid has a scale it can be set to, so the comparison can be made properly: at a cell of **0.92 mean spacings the two agree to 0.08%**.

  **And accuracy has an optimum rather than a floor** — the opposite of every other resolution knob in this engine:

  | cells per mean spacing | vs the sum's point limit |
  | --- | --- |
  | 3.68 | **−15.1%** |
  | 1.84 | −4.2% |
  | 0.92 | **+0.08%** |
  | 0.46 | **+4.4%** |

  Refining past the match makes it *worse*, and refining is exactly what someone does when they want a better answer. **Confirmed as a sampling artefact rather than a resolution one** by holding the cell fixed at 128 nodes and raising the macroparticle count: 4.42% → 1.55% → 0.93% as macroparticles per cell go 0.012 → 0.049 → 0.195. Below about one macroparticle per cell the deposit stops representing a density and starts representing lumps. `spacecharge.grid-resolution` reports the ratio on **every** run whether or not it crosses a threshold (REG-2's rule on a new quantity), as a validity violation outside 0.7–2.0, and names the node count that would match — computable with no run at all, since the cell and the spacing both scale with the packet radius and it cancels to `2·padding·∛N/nodes`.

  **The estimate was blind to a term that now varies 500-fold.** 200 macroparticles take **0.99 s at 16 nodes and 124 s at 128**; the cost model had one linear-in-trajectories term because nodes were not declarable when it was written. Two terms now — linear in the cloud for the gather, cubic in the node count for the solve — pinned by the measured crossing and a measured 43/57 split, tracking the measured 54× ratio to within 10%.

  **The refresh criterion is a controlled approximation, measured**: +12.68 / +6.16 / +1.01 / −0.54 % as the tolerance tightens 0.30 → 0.15 → 0.05 → 0.02. The sign at the coarse end was *predicted* — a field held across a refresh is the field of a denser packet, so it always pushes too hard — and the crossing to negative at 0.02 is staleness falling below the smoothing difference above, not the prediction failing.

  **A trap that caught me again**: `Grid3D.OverBox` rounds each axis up to a power of two, so 24 and 32 are the same mesh. A first node-count table ran 16/24/32/48/64 and produced two pairs of identical numbers, which reads as insensitivity to resolution over a fourfold range. Already written down for the 3-D solver. Details in `docs/numerics.md`.

- **A driven diffusive run is affordable, and the trade has to be stated both ways.** `"densityStep": { "scheme": "implicit", "gain": 64 }` — backward Euler on the same Scharfetter-Gummel coefficients, solved by red-black Gauss-Seidel. On the shipped funnel at 2 mbar, where the ponderomotive well's gradient at a ring edge sets a 195 ps Courant limit against a 747 ns diffusion limit:

  | gain | steps | sweeps/step | speedup | error |
  | --- | --- | --- | --- | --- |
  | 4 | 6,404 | 3.0 | 1.4x | 0.008% |
  | 16 | 1,601 | 3.0 | 4.7x | 0.028% |
  | 64 | 401 | 3.0 | **10.8x** | **0.108%** |
  | 256 | 101 | 4.0 | 17.7x | 0.427% |
  | 1024 | 26 | 4.9 | 21.4x | 1.673% |

  So 843,000 steps over 900 µs become about 13,000, and a run that took hours takes minutes. **The error is exactly linear in the step**, which is textbook first-order backward Euler and is itself a check that the path is right rather than merely stable.

  **And it does not accumulate over a longer flight — it falls, while the speedup grows.** The same comparison over 50 µs rather than 5 gives **21.1× at gain 64 for 0.057%** (against 10.8% for 0.108%), and **120× at gain 1024 for 0.894%** (against 21.4× for 1.673%). The error is concentrated in the initial transient where the density changes fastest; the explicit cost is linear in the window while the implicit sweeps-per-step stays at three. So the short-window figures are the pessimistic ones.

  **The explicit step was set by a region where nothing is happening** — the well is steepest at an electrode edge, which is exactly where the density is almost zero.

  **The load-bearing property is not the stability, it is that positivity survives a partial solve.** The update is `n' = (n + dt Σ b n'_neighbour) / (1 + dt Σ a)` and every term in it is non-negative, so each sweep is a non-negative combination of non-negative numbers and the iterate is a valid density however far from converged. A scheme that went negative on the way would be unusable however stable it was.

  **And it is not a general speed-up**, which is the half easiest to leave out. The Gauss-Seidel iteration's difficulty is set by the *diffusive* part of the operator, so a step long by Courant's standard and still short by diffusion's costs three sweeps — while a plain drift tube already near its diffusion limit climbs from 11 sweeps a step at gain 1 to **88.7 at gain 16** and comes out slower than stepping explicitly. `diffusion.implicit-not-paying` says so on the run.

  **What says it is correct rather than merely stable is the Boltzmann equilibrium.** Scharfetter-Gummel is built so its zero-flux state is exactly `B(−P)/B(P) = exp(P)`; that is a property of the *space* discretisation, so backward Euler must hold it at any step. It holds to **8.9e-16 in log density over three decades at a gain of 1000, in two steps and two sweeps** — one sweep per step, because the previous density *is* the answer and Gauss-Seidel recognises it immediately. **Verified by breaking the solve** the way a real mistake would (gathering a neighbour with this cell's outward coefficient): every stability and non-negativity test still passed, and the equilibrium moved by factors of 6 to 18. **A stability test cannot see a wrong operator and neither can a positivity test.**

  **The flux is assembled once now**, which the explicit path wanted anyway — it was recomputing two exponentials per face per step, about a million times over on a driven funnel. **Bit-identical**, asserted rather than assumed over four configurations spanning Cartesian and cylindrical meshes, still and moving gas, interior absorbers and every edge kind: density, collected count and every named loss to the last bit. That needed keeping the *factored* form — `scale`, `B(−P)` and `B(P)` stored separately rather than the two products — because `(w·s·b)·n` and `w·(s·(b·n))` differ in the last bit. The ledger reads the same expression for the same reason; a first version used `Out × density` and came back 1–3 ulps out.

  **Two measurement mistakes worth keeping.** A convergence study whose window let the packet be collected compares two nearly empty fields — the relative difference came out at 39–71% and scaled with nothing, which reads as a broken scheme. And the reference in such a study **carries its own error**: at gain g the two runs are (g−1) base steps apart, not g, so what must be constant is error/(g−1), and dividing by g makes a correct first-order scheme look wrong.

  **Not done:** nothing chooses the gain. Both limits are computable before the run, but what gain is acceptable is an accuracy question and nothing here measures the accuracy of a step it has not taken. Richardson extrapolation over a doubled step would, at three solves a step instead of one. Details in `docs/pressure.md`.

- **The live session, and MCP-1 met — but the work was not the protocol.** `journal`, `undo` and `attribution` existed only in the `Einzel.Commands` assembly **description string**, the same "named in a csproj and nowhere else" state `ITransportMode` was in before its seam was built. §15 says why that is the whole job: the server's "distinct value is shared live state ... everything else it could do, the CLI does at least as well and with less machinery." A journal only one party can write to is a file, and a file needs no server.

  **Attribution comes from the `initialize` handshake, not from a tool parameter.** An `author` argument would make the attribution something the *mutating party fills in*, which is a signature rather than an attribution — an agent could sign a change as the person it is working with, by mistake or because a model decided that read better. The client declares itself once, before any tool exists to call. **The test is in two halves and the second is what makes the first a property rather than a default**: the name that comes back is `agent:surveyor/3.1`, *and* `model_edit`'s schema has exactly `description` and `content`, so there is no argument through which another could have been offered. A tool that took an author and ignored it would pass the first half alone.

  **Shared and linear are two claims.** Shared means one stack rather than one per party, which is the point rather than a hazard: two private stacks over one document would let each party reverse changes the other had already built on, and the document would reach a state neither of them authored. Linear falls out of the walk back being over ordinary edits only. And **undo appends rather than pops**, because a popping stack loses the fact that somebody undid something, and who — which is exactly what MCP-1 asks to be recorded. Walking back twice appends twice.

  **The whole document before and after, not a patch**, so undo needs no inverse operation per command — a command that knew how to reverse itself would be a second implementation of what it does, and the two would part company at the first command somebody forgot to teach. **In memory**, because PRJ-4's argument says the durable record of a design is the document and its git history, not a sidecar.

  All three claims checked by **mutation** rather than by assertion count: private per-author stacks fail three of six journal tests, a popping undo fails a *different* two, and moving attribution into a tool parameter fails three of five protocol tests.

  **The tool surface is deliberately not a second CLI**, and the server says so in its own instructions — the failure to guard against is an agent looking for `run` and `sweep`, not finding them, and concluding the platform cannot do those things. `model_read | model_edit | model_undo | session_journal | model_validate | model_preview`. Preview is here because AGT-5 built it for exactly this; a full run is not, because it belongs where there is a progress surface and a viewport, and `einzel run` is one process launch away meanwhile.

  **Every result is `CommandJson.Write` of the same outcome record the CLI serialises for `--json`, asserted byte for byte.** That makes AGT-2 literal instead of claimed, and carries GRD-2 for free: a warning reaches an MCP client by being on the record rather than by anyone remembering to copy it across — which is the seam this project has already dropped evidence at three times. A refusal comes back as a tool *result* carrying AGT-3's code, path, constraint and suggestion, not as a transport fault, because a refusal is an answer about the model rather than a failure of the call.

  **`einzel-mcp` is its own executable, not a verb on `einzel`.** Figure 3 puts the three surfaces side by side as peers and figure 6 is emphatic that the project-folder loop has "no protocol, no session, no network"; a `serve` verb inside `einzel` would put a server in the binary whose distinguishing property is that it is not one. Nothing but protocol reaches stdout, which turns CLI-2's convention into a hard requirement.

  **The first non-test dependency the project has taken**, and §20's table asks for the licence to be verified rather than assumed: `ModelContextProtocol.Core` 2.2.0 declares Apache-2.0 as an SPDX expression in its own nuspec, and its whole transitive closure is ten `Microsoft.Extensions.*` packages, all MIT. `.Core` rather than the full package, which leaves behind a hosting integration this does not use.

  **A harness that lied in the worst direction.** Driving the server by hand — a file of JSON-RPC piped in — produced *nothing at all* on stdout, which reads unambiguously as a server that does not work. With a logger attached the SDK showed both requests handled and both responses sent: a file on stdin hits EOF immediately, the transport tears down, and the outbound writes are dropped. A real client holds stdin open and never sees it. **When a harness and the thing under test disagree, establish which one is the artefact before changing either** — the trap being that the simpler thing looks like the trustworthy one, when simpler here meant missing the one property the server depends on.

  **And GRD-9 was not delivered by building MCP-1, which is the same requirement stated twice.** The journal knew only about mutations made *through it*, so a person editing the model in their own editor had their change overwritten by the agent's next whole-document edit with nothing saying so — the requirement's own words, failed, in code that was an hour old. **The sharper consequence is what an unrecorded change does to undo**: it breaks the chain, so walking back lands on a document predating the person's edit and discards it *as a side effect of reversing something else*. `Reconcile` records an outside change as an entry attributed to `outside` — not to the person, because another tool, another session and a git checkout look identical from inside — refuses an edit written against the document as it was, and makes the refusal recoverable by having `model_read` take the change up. A no-op `Reconcile` fails three of nine journal tests.

  **Not built:** streamable HTTP hosted in process by the shell, which §15 makes the primary transport and which needs the shell. The tools sit above the transport, so that is a wrapper rather than a rewrite. Details in `docs/live-session.md`.

- **A packet can cross between the two transport descriptions (SEQ-1's conversion).** §9 says an instrument is a timed state machine of "ordered phases with durations, excitation overrides, **transport mode**, and transition conditions", and SEQ-1 adds that a phase boundary may change it. That is ordinary instrument behaviour rather than an exotic case — ions are collected and thermalised in a gas-filled trap, where the description is a density, then extracted into vacuum and flown, where it is trajectories. The two modes have been peers since REG-1's seam was built and could not hand anything to each other.

  **The third clause of SEQ-1 is the substance: "named as a source of uncertainty".** These are not two encodings of one state. **Trajectories to a density loses the velocities entirely** — a density field is a scalar per cell and there is nowhere for a distribution to live, which is not an implementation limit but what the diffusive description *is*: drift-diffusion holds precisely because the velocities have relaxed to the local equilibrium. **A density to trajectories has to invent them**, drawn Maxwellian at the gas temperature plus the local drift — the assumption the diffusive description already made, exactly right while the ions are in the gas that thermalised them and wrong the moment anything happens faster than the momentum-transfer time. `transport.velocity-assumed` is a **non-suppressible violation** for that reason: a caller who reads a flight time computed from invented velocities and does not know they were invented has been misled by the platform.

  | | |
  | --- | --- |
  | Deposited population against declared | **exact** (1e-12) |
  | Equipartition of drawn velocities, 300 K and 1200 K | **1.0021** each |
  | Drift added, against μE | 18.472423 against **18.472423** m/s |
  | A Gaussian cloud's centroid, 20,000 ions | 10.0197 mm against 10.0000 |
  | A 4000 m/s beam after a round trip | 0.2 m/s |

  Two temperatures because one alone is consistent with a thermal draw *and* with a constant that happens to match; the drift is a difference between two runs at one seed, so the thermal part cancels.

  **The discriminating check is cylindrical, and it is the one a plausible implementation fails.** A cell is a ring whose volume grows with radius, so a uniform density holds far more ions at the wall than on the axis — drawing cells by their density *value* over-samples the axis and produces a packet that looks entirely reasonable. The closed form separates them: p(r) ∝ r gives mean radius **2R/3 = 13.3333 mm, measured 13.5177**, against **R/2 = 10.0** for the wrong weighting. Run the wrong way it gives 10.0245, and **only that one test of the ten fails**.

  Population is conserved **by construction** — the four bilinear weights sum to one whatever the position — rather than by a normalising pass, which would pass the same test while hiding a weighting error rather than preventing one. An ion outside the grid is **counted, not clamped**, because clamping piles the escaped population onto the boundary and makes a leaky instrument look confining. And the azimuth of a cylindrical sample is drawn uniformly, which is information the conversion *creates*: a round trip is not the identity even in distribution for a packet that was never axisymmetric.

  **Not built: a stage cannot yet name a transport mode — and scoping that found the reason, which is a defect.** A stage is declared on a *solve element* while the transport mode is a property of the *run*, so a per-element stage cannot carry one: two elements would name different modes for the same instant, and there is no superposition of transport modes the way there is of fields. Worse, the existing arrangement is already wrong. `CompileStages` re-resolves the **whole model parameter surface** with the stage's overrides and then re-expands **only its own element** — so two electrodes in different elements, written as the *same expression* over the same parameter, came out at **900 V and 300 V** during a stage, on a model that validated cleanly with no diagnostic anywhere. The stage design's own rationale is the claim that fails: setting a parameter "moves everything that depends on it at once". It moves everything in one element.

  **Refused first, then fixed.** The refusal — a sequenced model may have one field element — was the honest state while the two readings were open; the right one is the documented one. **The timeline is the instrument's**, so `Timeline` resolves the phases *once for the model*, before any element is compiled, and hands the same parameter surfaces to all of them. **Every element follows it, and how depends on what it is** — a solved geometry re-weights channels it has already solved; an analytic one is compiled once per phase and switched by `SequencedField`. An element no phase moves stays static, which is a distinction rather than an optimisation: wrapping it would hand the integrator switch instants to land on for a field identical on both sides of them.

  **A code review found the first version of this fix reached only half the elements**, which is the more useful half of the story. The analytic branches compile from the base surface and a `CompiledField` for those kinds had nowhere to put a phase — so a sequence setting a parameter used by a `halfSpaceUniform` cap potential had the solved elements follow and the analytic one frozen, validating cleanly; a model whose *only* elements are analytic compiled a timeline nothing consumed, making the sequence a silent no-op. **The comment that hid it is the part to keep**: `Restage` was a closure "because only the solve branch needs it, and threading the declared parameters through every field kind would put the sequencer in the signature of things that have nothing to do with it" — an argument that reads as sound and is exactly backwards. A rationale written for one version of the code kept the next from noticing what it had missed. Schema **0.6** adds a model-level `sequence`, which is the spelling that says what it means; `stages` on a solve stays the older one for the single-element case. Verified by mutation: restoring the per-element behaviour fails that test and nothing else.

  It also fixed something the per-element version got wrong quietly — a malformed stage was reported **once per field element**, turning one typo into a wall of identical complaints. Two refusals remain, both about a document saying two things at once: two elements each declaring stages is two timelines over one instrument, and declaring both `sequence` and `stages` is refused rather than merged, the same argument that refuses a geometry declaring both `drive` and `drives`. **A phase now names a transport mode, and a run crosses the boundary (SEQ-1).** Schema 0.6 puts `mode` on a phase, absent meaning the model's — the same rule its parameter overrides follow, so a model with no sequence and one whose every phase runs in the declared mode are the same run. `SequencedRun` walks the phases, each an ordinary run of its own mode over its own duration, converting where the mode changes. On the test instrument — launch, thermalise, extract — the packet advances **1.37 mm in a microsecond while flying and does not move at all over twenty times longer as a density**, because the diffusive drift is μE and E is zero there. That is the conversion made visible rather than a defect: drift-diffusion holds precisely because the velocity distribution has relaxed, so the momentum genuinely is discarded. Position, the one thing both descriptions carry, survives to the fourth decimal.

  A trajectory leg starting part-way along the timeline is flown against a new **`TimeShiftedField`**, because the integrator always starts at t = 0 and a leg beginning at 21 µs has to be handed an instrument shifted by 21 µs rather than a start time it has nowhere to put. Wrapped rather than adding one to `IntegrationSettings` — the precedent `AxisymmetricField` and `PonderomotiveField` set, and for the reason this file already gives: that core carries every validated number here, and refactoring it to add a case beside it is how those get quietly lost.

  **A sixth occurrence of the check that keeps learning new configurations.** The diffusive requirements — a gas, a mobility, a density grid — were gated on the model's own `transport.mode`, so a trajectory model with a diffusive phase skipped all of them, validated cleanly, and would have failed at run time asking for the gas it never declared. Fixed by asking the right question rather than adding a case: `Modes` returns every mode the run uses, and the requirements attach to that.

  **The first phase may be the trap** — the ordering the requirement was written about, since ions are collected and thermalised in a gas and only then extracted. Seeded through `DiffusionRun.Seed`, the same function a wholly diffusive `einzel run` uses, rather than a second implementation.

  **Reusing that path corrected two numbers a duplicate had got wrong.** A first version built its grid with `new Grid2D(...)` where `GridFor` uses `Grid2D.OverBox`, which rounds intervals up to a power of two — so one model got **two different grids** depending on which path ran it. And its mobility helper ignored `Derived`, so a mobility the document derived from a cross section came back as the stored value rather than the re-derived one. A third gap closed with them: the diffusive leg passed **no absorbers**, so electrodes did not absorb during a diffusive phase — the defect that once made every diffusive transmission an upper bound with nothing saying so, reintroduced locally. The rule: **four helpers already existed, and writing them again got two of them wrong.**

  **Not built:** nothing is wired to the CLI, so `einzel run` still forks on the model's own mode and per-phase outcomes reach no surface; and a diffusive leg samples its field once, which is right for a phase whose parameters hold for its duration and wrong for a driven field inside one.

  **Corpus 31 → 32, and the new one is the sharpest sequenced check there is.** `sequenced-uniform` holds an ion at rest in nothing and then pushes it with a uniform field a phase switches on: predicted `hold + sqrt(2 d m / (q E))` = 5.219358580 µs, measured **5.2193585800816775, 1.6e-11 relative** — five orders inside tolerance, because an analytic field has no geometry error to absorb where the plate version carries 1.0e-7 of fringe. The corpus's only model in the **model-level `sequence`** spelling and the only one whose timeline moves an **analytic** element. **Its teeth were measured rather than predicted**: with the analytic-phase fix reverted it is refused at validation before the ion is launched, and that one refusal guards both defects at once.

  Also added, and overdue by five schema bumps: a test that **every version `SupportedVersions` claims to read actually reads**. §14 has asked for one since 0.2 and none existed, so "every earlier document still reads" has been an assertion in a list rather than a measurement. It ships with a control — an unknown version is refused — because a reader that accepted anything would pass the first half without reading a single version right. It also caught a slip in itself on the first run: the guard asserting the edit had happened fired on the identity case, where replacing 0.3 with 0.3 changes nothing. Details in `docs/model-format.md`, `docs/pressure.md` and `docs/lessons.md`.

Adding a travelling-wave guide or a multipole should need only one more file — axisymmetry, repeats and RF all exist now. If it needs a change below `Einzel.Library`, LIB-1 says the abstraction is wrong — believe it.

Two findings from Stage 1 that bear on the spec:

1. **The turning-point step cap (§11) does not help and slightly hurts.** In a smooth field the flight time is at machine precision with 6 steps and marginally worse with 105. §11's rationale is that "position-error controllers under-refine" at the velocity minimum — but `ErrorNorm` weights velocity error with its own absolute floor, so it is not a position-error controller and does not under-refine. The cap is implemented and on by default (`TurningPointStepFactor = 0.01`) to honour the spec as written; the evidence says it should default to 0.
2. **A field discontinuity is a real error source and must be landed on exactly.** Dormand–Prince stage 4 carries the coefficient −56/15, so intermediate stage samples fall outside the step interval and can land on the wrong side of a field jump even when both endpoints are inside. Handling the boundary as an event took the reflectron from 5.5e-10 to 1.7e-16. A residual around 1e-10 remains and behaves as noise rather than as a controlled error — it is an artifact of idealised *discontinuous* analytic fields and should not appear in solved, interpolated fields, but it is what sets the achievable tolerance on finite-difference tests.

- **A declared gas took no part in any figure of merit, and an example in the release gate could not fail.** Two defects found while adding a thermalisation example, both larger than the example.

  **`einzel run` and `einzel test` disagreed on every model with a gas.** The figure-of-merit path built the launch, the field and the detector but never a collision sampler, so it flew in vacuum however much gas the document declared: **4904.4862 µs against 5000** on `gas-flow-carry`. Verified by deleting the gas block and getting an identical answer to the last digit. **This is the second time run and test have drifted** — the first was found and fixed by collapsing them to one implementation, and the gas then arrived on only one side of the seam. A shared entry point is not a shared computation.

  **That example could never have caught it.** It launches its ion at exactly the gas velocity so the transit is `L/u` by arithmetic — and in vacuum an ion launched at that speed covers the same metre in the same 5000 µs. The vacuum answer *was* the expectation, and closer to it than the physical answer.

  **And its tolerance was vacuous.** Expectations compare a *relative* error; that one read `500.0`, written as ±500 µs on 5000, which as a fraction admits any positive answer. Its description said "discriminating far past its ten per cent tolerance" — the same misreading, written down. An audit of all 29 corpus expectations found one other at 50%; both are now 10%.

  New: **`meanKineticEnergy`**, because equipartition is the sharpest check the collision models have — an ion left in a gas must reach (3/2)kT whatever it started with — and it was measured only in a unit test, outside the machinery that keeps every other figure honest. Over the ions still in flight rather than the arrivals, since a thermalised packet has no preferred direction and selecting on arrival selects the fast ones. **`CloudFlight.Remaining`** carries them, so a packet that never arrives still has measurable state. The `thermalisation` corpus example gives **0.039339 eV against 0.038778** on 240 ions, 1.45% high against a 5.3% standard error.

- **`render animation`, and RND-7 enforced by the interface rather than checked.** The
  requirement is emphatic: an animation "declares an explicit non-linear time mapping —
  playback rate per sequence phase — and the current rate is displayed on screen
  throughout playback. **Neither part is optional.** This is the animation equivalent of
  GRD-1: the artifact may compress, but it may not hide that it compressed."

  **So an animation is asked for through a render spec and there is no `--rate` flag.** A
  model document has nowhere to declare a mapping, so there is no command line that
  produces an animation without one. `--fps` *is* offered, because a frame rate is a
  property of the playback device and changes no claim the animation makes.

  On the shipped reflectron, three phases — 4 µs/s inbound, **0.5 µs/s through the
  turn-around**, 4 µs/s out — give 1.000 s, 4.400 s and 0.995 s of playback. The
  turn-around is **a fifth of the flight and 69% of the film**, which is exactly what one
  rate cannot show. Sixty-five frames; the frame at playback 1.000 s shows 4.0000 µs.

  **The rate has a unit, and two readings.** `us/s`, `ns/s`, `ms/s` — dimensionless,
  since it is a time over a time, and what makes it a *rate* is that the denominator is a
  second of playback rather than of flight, which no dimension can carry. The stamp reads
  `t = 5.000 µs · turn-around · 500 ns of flight per second of playback — 2,000,000x
  slower than real time`: the first converts anything on screen back into flight time and
  carries a unit (GRD-1); the second is the intuition and alone says nothing about how
  long the flight is.

  **Frame times are computed, never accumulated** — one lookup and one multiply from each
  frame's own playback time, so a phase that is not a whole number of frames long cannot
  push error into the phases after it. The final frame is forced onto the end, because
  for a flight the last instant is the one a reader wants. And a frame landing exactly on
  a boundary announces the **incoming** rate, since it is followed by a frame's worth of
  playback at the new speed.

  **A design bug I introduced and the test that did not catch it.** The first version
  handed each frame the part of the flight drawn so far. An analytic model takes its
  extent from the flight, so every frame chose its page from its own prefix — the scale
  changed frame to frame and the ion sat pinned to the edge of a box that grew to meet
  it, which reads as a camera following the ion. Nothing about a single frame reveals it.
  Fixed by handing over the whole flight with an instant to truncate at. **The test
  written for it passed with the bug restored**, because it used the einzel lens — a
  *solved* geometry whose extent comes from its declared domain and never touches the
  flight. Moved onto an analytic reflectron it fails at once. Third time in one night
  that a test exercised a path not containing the line under test.

  Refused: a spec with `trajectory: false` on a trajectory model (the geometry and the
  field are identical on every frame, so the sequence would be one drawing repeated), a
  spec with no phases, a phase that does not advance, a non-positive rate, and a
  mapping a diffusive run cannot reach. Warned about rather than refused: `animation.past-arrival` and
  `animation.stops-short`, because both are legitimate to ask for and neither looks like
  a choice.

  **No video, and that is LIC-1** — ffmpeg is exactly what would be reached for. What
  comes out is numbered vector frames plus a `frames.json` schedule; assembling them is
  an out-of-process step with a tool the user supplies.

  **The field moves too, and finding that it did not was the fourth sighting of one
  defect.** A driven field implements the time-free `IElectrostaticField` as well as
  `ITimeVaryingField` and **answers the time-free one at t = 0 without failing**, so the
  renderer drew the same instant on every frame — after `einzel solve` reporting the DC
  pattern of a driven geometry, the diffusive mode stepping a density through a snapshot
  of the RF, and `SuperposedField` becoming a snapshot when a driven member was summed
  into it. The instant is now declarable (`atSeconds` on a render spec) and every frame
  supplies its own; a section of a driven model carries `render.field-at-instant` either
  way, because a figure of a driven structure is a frame of a film whether or not it is
  drawn as one.

  Checked exactly, over one period of a 1 MHz quadrupole: **20, 0, 20, 0, 20**
  equipotential paths at 0, T/4, T/2, 3T/4, T. The drive is through zero at the quarter
  points so there is nothing to contour; at T it is the *same drawing as at 0, to the last
  bit*; at T/2 the rod pairs have swapped sign. **The contour levels had to be fixed once
  across the animation** — a driven field's range changes through the cycle, and levels
  taken per frame would be spread over rounding noise at a zero crossing and fill the
  frame with contours of nothing. Same defect as a page chosen per frame, in the other
  axis.


  **And a diffusive model is animated as a moving density.** It was refused outright, and
  rightly while a run reported only the density it *ended* with — the frames would all
  have been the same box. Now the command layer runs the transport once with the frames'
  own instants as its snapshot list and hands the renderer the results, as the section
  path already does. On the corpus drift tube over 200 µs the packet **drifts (22 → 100
  mm), spreads (24 → 59 mm), and narrows again at the end** as its leading edge is
  collected — three things a trajectory cannot show.

  **The contour levels are anchored once, and that matters more than the page did.**
  Density contours sit at decades below the peak, and a diffusing packet's peak falls as
  it spreads: anchored per frame the levels would fall with it, the contours would stay
  the same size, and *a film of a packet spreading would show a packet doing nothing*.
  Not flicker — a lie. Anchored across the animation, later frames show **fewer**
  contours, because the density really is lower.

  **Not built: geometry that moves.** A stage may change what an electrode holds and not
  where it is, which the sequencer already enforces, so the conductors are identical on
  every frame by construction. A mechanism with a moving part is not expressible at all.

- **The scaffolded reflectron drew its turning point 105 metres off a 160 mm page**, and
  had done since sections were built. A model with no declared solve domain takes its
  extent from the instrument's own points, and those were the source and the detector
  alone — which in a reflectron are **the same point**, because the ion is caught where it
  launched. The pad was then a tenth of a millimetre around a flight of 1.3 m.

  Found by animating it. **No test caught it because every render test uses a device
  template, and every device template declares a solve domain** — so the analytic branch
  of the extent had no coverage at all. The flight is now included, and the pad comes
  from the extent actually gathered rather than from a separation that is zero. It costs
  nothing: the trajectory had to be flown to be drawn, and it is now flown before the
  page is chosen rather than after.

- **A density can be drawn while it is still moving.** A diffusive run reports the
  density it *ended* with, so a model whose ions have all arrived left an empty box —
  correctly, and uselessly, because the picture worth having is the packet in flight.
  The only way to get one was to shorten `maximumFlightTime`, which gets a packet by
  throwing away everything after the moment being looked at.

  `DriftDiffusion.Run` takes a list of instants and returns the density at each;
  `einzel render section --at-us <t>` draws one. On the corpus drift tube: **0 contours
  at the end, 10 at 50 µs with the packet centred at 49.5 mm, 11 at 150 µs at 102.9 mm**
  — it drifts and it spreads, both visible.

  **The instant recorded is not quite the one asked for, and the figure says both.** A
  diffusive step is set by a stability limit and cutting it to land exactly would change
  the step sequence and so the answer — a high price for an offset of one step. The
  provenance carries `density at t = 50.1302 us, asked for 50 us`.

  **Recording does not perturb what it records**, asserted rather than assumed: the same
  run with and without snapshots is bit-identical in step count, collected ions and every
  node of the final density. An instant past the end is **absent** rather than filled in
  with the final state, because a density never computed is not the density at that
  instant and substituting the last one would make a figure of a finished run look like
  one of a running run.

  The unit is in the flag's name, as `--width-mm` already does it: a bare `--at` would be
  ambiguous between microseconds and seconds by a factor of a million.

- **A dimension is measured, never written down.** The memo's own figures are line
  drawings *with* dimensions, and a section without them says what the instrument looks
  like and not how big any of it is. What a `dimensions` entry declares is **the two
  points it spans**; the length is the distance between them, computed when the figure is
  drawn. `label` names the span — "drift", "bore" — and does not carry the value, because
  a typed number is a second statement of something the model already says and the two
  part company at the first parameter change.

  **The points may be expressions over the model's own parameters**, so a dimension
  describes the geometry rather than where the geometry used to be. Changing
  `turningDepth` from 50 to 80 mm and re-rendering the *same figure spec* gives
  `penetration 50 mm` then `penetration 80 mm`, with no edit in between — which is what
  the test asserts: one spec, two models, two measurements. §9's rule for a model, "every
  placement is a parametric expression, never a baked number", is not weaker for a
  drawing of it.

  Ordinary drafting convention: extension lines clear of the feature and past the
  dimension line, an offset dimension line, arrowheads, and the measurement above it on
  the side away from what it measures. The offset is signed so two dimensions over one
  feature can go opposite ways, and the unit is chosen from the magnitude because one
  figure may carry a 300 mm drift and a 50 µm channel.

- **Two defects and one unresolved one, from trying to write a pulsed-extraction
  example.** The corpus has no sequenced example — the one Phase 4 capability it does not
  exercise — and it still does not, because the run does not complete. But the trip found
  two real defects that are fixed.

  **`CanDoWork` read the base potentials and not the stages**, so a pulsed-extraction trap
  — which holds everything at zero until it switches — was refused as an instrument in
  which nothing could move an ion. That is the **fourth** appearance of one pattern and
  the *third* in this one function: `einzel solve` reporting the DC of a driven geometry;
  `CanDoWork` asking only about DC and refusing the Paul trap; its 3-D arm inspecting
  nothing at all; and now the stages. The advice already written down — "grep for
  `.Potential` the next time something driven behaves as though it were earthed" — does
  not catch the fourth, because it is not about the drive. The wider statement: **a check
  that asks what an instrument is doing must ask over every configuration it has**, and a
  sequenced one has as many as it has stages. The control is asserted too: a sequence
  that never energises anything is still refused.

  **A stage set to an expression was read as zero.** `CompileStages` built its overrides
  with `Quantity.From(value.Value, value.Unit)` and never looked at `value.Expression`,
  and `Value` defaults to zero — so a stage meant to apply a kilovolt applied nothing.
  The model validated, the field solved, and the run reported an ion that never moved,
  with no diagnostic anywhere, because zero volts is exactly what the *first* stage of an
  extraction legitimately applies. Now refused rather than supported: what an expression
  should mean there is a design question, since the surface it would evaluate against is
  the one the stage is changing.

  **Fixed, and the fix is one line.** `FlightTimeStudy`
  refines by scaling the relative tolerance *and both absolute floors*, and at its deepest
  rung `AbsoluteVelocityTolerance` reaches **1e-11 m/s** — ten picometres per second,
  against thermal speeds of hundreds of metres. For an ion starting from rest the
  normalised velocity error is then unsatisfiable at any step size, so the step halves 63
  times. Isolated by tightening each of the three alone: only the velocity floor
  reproduces it, and that floor is what stops `ErrorNorm` being a position-error
  controller. `einzel preview`, one run, gives **2.9106 µs against a closed form of
  2 + 0.910572113** — the model was always right.

  Holding the floor leaves the reflectron **bit-identical** and makes its interval **17×
  narrower** — a measured residual instead of a saturated floor. It also broke the test
  documenting `convergence.at-resolution`, and the reason is the part worth keeping:
  **that model's bit-exact rung agreement depended on the ladder over-tightening the very
  floor at issue** — the test had been asserting a coincidence. Nothing reachable through
  the study's API reproduces the collapse, so the rule was given a name,
  `FlightTimeStudy.ConvergenceResidual`, and is tested directly on runs that agree to the
  bit. **A rule that can only be exercised by a coincidence is a rule with no test.**

  **`sequenced-extraction` ships** — the corpus's first sequenced model, and Phase 4's
  sequencer in the release gate for the first time. Predicted 2 µs + 0.910572113 µs,
  measured **2.9105718, 1.0e-7 out**: the finite plates and the grounded boundary, not
  the sequencer. Corpus 30 → 31. Full diagnosis in `docs/lessons.md`.

**`SPEC.md` is the living specification** — see the note at the top of this file for what it holds and when to update it.

The two design documents remain the source of truth for *intent*. Tracked alongside them: `SPEC.md`, `README.md`, `LICENSE` (Apache 2.0).

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

CLI, MCP server, and WPF shell are **peers, not a stack** — all three drive the same serializable command objects. **Two of the three now exist**, and the relationship is asserted rather than described: every MCP tool returns `CommandJson.Write` of the same outcome record the CLI serialises for `--json`, compared byte for byte by a test. A shell that shelled out could not drive an interactive viewport at frame rate; a hundred milliseconds of process start per slider drag is not a shell.

**The shell is a named deliverable, and the Windows GUI capability was part of why C# was chosen** — a rationale r06 never records, which is SPEC.md Amendment 25. **Windows-only is the decision, not an accident of WPF**: Avalonia was considered and not chosen because the shell is not planned for use outside Windows, and that gets revisited if the need appears. It stays cheap to revisit because of invariant 1 (no UI type below the shell — everything above `Einzel.Wpf` builds and runs on Linux, and CI runs there) and Amendment 25's CLI-expressibility, which together make a later cross-platform shell a replacement of a presentation layer rather than a rewrite. **Windows-only applies to the shell and to nothing else** — that is the misreading to guard against, since "the GUI is Windows-only" and "the project is Windows-only" are one word apart and the second would undo the Linux CI that keeps the first one cheap. What is wanted is interactive geometry, the solved field drawn over it, and animation. §22's scope-creep risk is managed by UI-1's prohibition (the shell owns layout, input, the viewport and the update check, and owns no physics, no validation, no format knowledge and no render output), not by deferring the window.

**The thesis is the pair, and neither half is the product**: an agent drives the entire design process through CLI and MCP, and a human sees and manipulates the same design in a window. Amendment 25 strengthens AGT-2 to make that work — **every shell action should be expressible as a CLI invocation and journalled as one**. The shell still drives command objects in-process; what changes is that its journal is a list of commands somebody could run. A capability with no command spelling then cannot be added to the window, and a human's session hands over to an agent in the same vocabulary. The thing to review when the shell is written is the in-process path acquiring an argument the command form has no spelling for.

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

## Galerkin coarsening: built, and chosen against the cheaper hierarchy

`A_coarse = R A_fine P` — coarse levels built from the fine operator rather than from
the geometry, so they cannot lose it. **The finest level is untouched**: cut cells and
the geometry-driven smoother stay exactly as they were, because that is where the
accuracy comes from.

Two 1 mm slabs at a 0.25 mm cell: **1 level and a 274,625-node bottom becomes 6 levels
and 27**, 45 cycles becomes 13, 160 s becomes 13 s. **The cycle count stops depending on
the mesh** — 14 at 65³ against 13 at 129³, where it was 6 against 45. And it is the
**same answer**, 1.1e-7 to 4.0e-7 relative, which is what separates it from the fast
wrong one (deeper *rediscretised* coarsening was 30× faster and gave 486 V of 100).

**Neither hierarchy dominates, so the solver picks from the geometry before solving
anything**: 11.9× on the slabs, 4.6× on four rods, **0.64× on a sphere** — a loss, where
the cheap hierarchy already reached a small bottom and the 27-point stencil is overhead.
The criterion is the size of the bottom the cheap hierarchy can reach; the threshold is
20,000 nodes and is measured rather than derived. `SolveReport.Galerkin` says which ran.

Two things that were easy to get wrong and are worth knowing. **A 27-point stencil is
closed under this coarsening** (restriction one cell, operator one, prolongation one =
three fine cells = one coarse), so the hierarchy needs one operator type. And
**`halfH2` stays at the finest level's value all the way down**, because the coarse
operator inherited the fine one's units — recomputing it per level would be wrong by 4×
per level and would still converge, to something else.

**A test that failed on correct code, the right way round.** The first operator check
asserted `R A P` reproduces the rediscretised 7-point Laplacian. That holds in *one*
dimension; in three the transfers are tensor products and `R_b P_b = [1/8, 3/4, 1/8]`,
so the off-axis entries belong there. Deriving what they should be instead pinned every
coefficient against arithmetic the code had no part in — centre 27/64, face −3/128, edge
−5/256, corner −3/512, to 1e-13, row summing to exactly zero.

## A solver limitation to know about

**Measured, and worse than it reads below.** `SolveReport` now carries `Levels`,
`Sweeps` and `CoarsestNodes`, and `einzel solve` prints them. What they say: the 3-D
V-cycle descends **0-2 levels on every device geometry** (4-6 with no interior
electrode), because `Representable` stops at a *physical* cell size — so refinement adds
levels at the top and never removes the bottom. Two 1 mm slabs bottom out at **274,625
nodes at 65³ and still 274,625 at 129³**; the shipped segmented quadrupole bottoms out
at 9,537; the shipped **2-D** templates bottom out at **9-99**, because the two solvers
coarsen by different rules. At a 0.5 mm cell the slabs coarsen *zero* times, so their
"6 cycles at factor 0.015" is 400 relaxation sweeps a cycle over the finest grid — which
is why a 65³ Laplace solve takes 36 seconds. **A cycle is not a unit of work and the
factors below were being compared as though it were.**

**The guard is load-bearing, established by removing it**: letting the 0.25 mm slabs
descend further takes 45 cycles and 145 s down to 5 cycles and 4 s, and gives **486 V of
100 applied**, reported as converged. Only the maximum principle catches it. A plausible
alternative explanation — coarse masks carrying the electrodes' real potentials, so each
cycle injects 100 V — was checked and is wrong: coarse correction fields start at zero
and never have a mask applied. What actually happens is that a 1 mm slab four levels
down is smaller than a cell and gets **pinned to a single node**, so the coarse problem
constrains the error at two points where the fine one constrains it over two planes.
That is precisely what `R A P` fixes. Details in `docs/numerics.md`.


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

Effort estimates, performance targets, regime boundaries, and the numerical error budget are **engineering judgement, not measured values**. Third-party library status, licence terms, MCP SDK capabilities, and CSnakes' Python version support were current at writing and must be re-checked before being committed to. §23 lists decisions still open — treat them as genuinely open rather than inferring an answer from elsewhere in the document. **Two of them are now closed and recorded in `docs/spec-findings.md`:** the FLD-1 linearity spike was run (it failed, then passed once cut cells landed), and the agent acceptance suite has been designed and built — see `docs/agent-acceptance.md` for what it measures and the recommended release gates.
