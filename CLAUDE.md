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

  **Not built:** `render still` (raster — nothing here rasterises) and `render animation` (needs RND-7's explicit non-linear time mapping, displayed throughout playback). Both are named by the CLI and refused with a reason rather than falling through as unknown verbs, because "not built yet" and "you spelled it wrong" are different problems. Also missing: dimensioned callouts, which the memo's own figures have. Details in `docs/rendering.md`.

- **The CLI is the primary surface and now has most of §15.** `init | new | validate | estimate | preview | solve | run | compare | sweep | optimise | test | verify | export | render | ext | schema | templates | examples | agents-md | doctor`, plus the CLI-1..6 contract: `--json` on every verb, results on stdout and diagnostics on stderr, `--dry-run` on every mutating command, distinct exit codes per failure class, deterministic ordering. Cold start 73–147 ms against PERF-8's 500 ms. **Not built: `self-update`**, which needs `Einzel.Update`. `render section` and `ext list|test|register` now exist; `render still`, `render animation`, and the in-process extension runner do not.

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

  **Not built:** a neutral velocity *field*. `IGasFlow` is the seam and `UniformGasFlow` is the only implementation, so a funnel's transmission is still computed in a gas that is either standing still or moving all in one piece — and the jet off an inlet capillary is neither.

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

  **Still missing:** thirteen more, and the gap is breadth rather than machinery — no multipole above four rods, no 3-D trap, no MR-TOF, and nothing in the diffusive mode.

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

  **Two gaps left, both named.** The **pressure** is still a single number for the whole model, which a differentially pumped instrument is not. And the **event-driven mode refuses a field** rather than using one: `CollisionSampler` schedules and draws a neutral velocity without a position, so threading a position through the collision path is the work.

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

  **And the boundary is amplitude-dependent, which an ideal one cannot be** — 0.860 at a 0.1 mm launch, 0.8236 at 0.3 mm, 0.635 at 0.6 mm, all converged in hold time (the 0.1 mm edge moves 704 → 700 V between 200 and 800 cycles; the 0.3 mm edge not at all). The Mathieu equation is linear, so a trajectory scaled by a constant is another trajectory. **So the scale factor is not the whole account and its agreement at 0.3 mm is partly coincidence** — my first write-up said "closes to under 1%" on the strength of that one launch offset, which is exactly the mistake of quoting a number whose controls have not been run. There is no clean small-amplitude limit to compare against either, and the reason is structural: a measurement that registers a loss only when the ion *reaches* z0 is never a small-amplitude measurement, whatever it was launched at.

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

Adding a travelling-wave guide or a multipole should need only one more file — axisymmetry, repeats and RF all exist now. If it needs a change below `Einzel.Library`, LIB-1 says the abstraction is wrong — believe it.

Two findings from Stage 1 that bear on the spec:

1. **The turning-point step cap (§11) does not help and slightly hurts.** In a smooth field the flight time is at machine precision with 6 steps and marginally worse with 105. §11's rationale is that "position-error controllers under-refine" at the velocity minimum — but `ErrorNorm` weights velocity error with its own absolute floor, so it is not a position-error controller and does not under-refine. The cap is implemented and on by default (`TurningPointStepFactor = 0.01`) to honour the spec as written; the evidence says it should default to 0.
2. **A field discontinuity is a real error source and must be landed on exactly.** Dormand–Prince stage 4 carries the coefficient −56/15, so intermediate stage samples fall outside the step interval and can land on the wrong side of a field jump even when both endpoints are inside. Handling the boundary as an event took the reflectron from 5.5e-10 to 1.7e-16. A residual around 1e-10 remains and behaves as noise rather than as a controlled error — it is an artifact of idealised *discontinuous* analytic fields and should not appear in solved, interpolated fields, but it is what sets the achievable tolerance on finite-difference tests.

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

Effort estimates, performance targets, regime boundaries, and the numerical error budget are **engineering judgement, not measured values**. Third-party library status, licence terms, MCP SDK capabilities, and CSnakes' Python version support were current at writing and must be re-checked before being committed to. §23 lists decisions still open — treat them as genuinely open rather than inferring an answer from elsewhere in the document. **Two of them are now closed and recorded in `docs/spec-findings.md`:** the FLD-1 linearity spike was run (it failed, then passed once cut cells landed), and the agent acceptance suite has been designed and built — see `docs/agent-acceptance.md` for what it measures and the recommended release gates.
