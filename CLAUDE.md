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

- **Space charge is screened, not modelled**, and the distinction it forced is the interesting part. `ions` (how many trajectories to compute — numerical) and `population` (how many ions are physically in the packet — what pushes on itself) are now separate fields; `population` defaults to `ions`, the conservative reading, so a dense packet is never silently sparse. A run reports the flight-time error the packet's own charge implies whether or not it crosses a threshold, warns **non-suppressibly** past ACC-1's 1 ppm, and names the population the packet could hold within budget.

  The numbers are uncomfortable and worth knowing: **a 0.5 mm packet at 4 kV holds only ~5,600 ions within 1 ppm.** Ten thousand — roughly what ACC-5 wants for a transmission interval — is already 1.6–3 ppm over. **ACC-5's ensemble size and ACC-1's timing budget pull against each other**, which is a genuine tension in the spec and the reason the two fields had to be separated. The Ion Processor's 140,000-ion capacity figure is 12.6 ppm, so that figure *is* a space-charge limit rather than a detail of the trap.

  The estimate is deliberately an upper bound (free-flight conversion of energy spread to timing; a first-order focus suppresses it further). A real treatment inverts the integration loop — advance all ions one step, recompute their shared field, repeat — which is Phase 3. **The existing multigrid is the right machine for it**: particle-in-cell is the same Poisson operator with a non-zero source, and SC-1 already specifies the validation (approximate method against direct N² on a reference population).

- **The agent acceptance suite exists** (`docs/agent-acceptance.md`), closing the second of §23's two Phase-1 open questions. Six prose tasks across two tracks — four on capability, two on whether a warning is *acted on*. `einzel agents tasks | setup | score`. Two design decisions worth keeping: it **scores actions, not self-reports** (a quotable number means a manifest exists, because a preview leaves none), and **every task ships plausible wrong answers** that CI asserts must fail, because a check that only passes the right answer is testing that a file exists. Recommended gates: 80% capability, 90% warnings, any task at 0% blocks, and any drop against the previous release blocks regardless of level. **No agent has been run against it yet, so there is no pass rate.** The regime-invalid traps §19 asks for need Phase 3.

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

  **The limitation, stated with a number: 3-D multigrid does not survive interior electrodes.** An electrode loses 7/8 of its nodes per level rather than 3/4, and past the point where a cell exceeds the electrode the coarse grid solves a different problem — the correction does not converge slowly, it converges elsewhere. A charged sphere hit **137 V of 100 applied** through two levels and was correct to 2.7 V with none. The solver now refuses to descend past a level resolving the smallest electrode to a quarter of its size, so interior geometries run as a smoother: correct and slow. Same fix pending as in 2-D — Galerkin coarsening or operator-dependent interpolation.

  Three things this cost, all worth keeping. The residual norm had to become **RMS rather than max**: with sub-cell surfaces one tiny-arm node dominates the maximum, so the norm rises while the solution improves, and judging convergence on it stopped a good solve after two cycles. `OverBox` rounds intervals **up to a power of two**, so asking for 24 and asking for 32 gives the same mesh — a refinement study that did not know that reported an observed order of exactly zero. And the maximum principle is a statement about **nodes**: a cubic through the step at a conductor overshoots ~2% by construction, which is the interpolant behaving normally.

  **Not yet wired to the model format** — no `solved3d` field type, so nothing above `Einzel.Fields` can reach it and the segmented quadrupole is still out of reach end to end.

- **The sequencer — a geometry operated through timed states.** A solve may declare `stages`, each a duration plus a set of parameter values. **A stage sets parameters, not electrode settings**, which is the whole design: potentials are already expressions over parameters, so setting one moves everything depending on it *including derived parameters*. Listing settings instead would let a stage change an amplitude while leaving the quantity it was derived from behind. It also costs no new vocabulary — the same override mechanism a sweep uses to *perturb* a design is what a sequence uses to *operate* one.

  **Landing on a switch needs no root-find**, unlike a boundary in space, because the time is known: the integrator asks `NextSwitchAfter` and refuses to step past it. The check is that a sequenced run equals the same flight computed as **two separate runs stitched together** — the same physics written two ways, needing no closed form. Disagreement is **1.0e-8 / 4.6e-8 / 1.3e-9** relative at tolerances 1e-8 / 1e-10 / 1e-12: parts per billion throughout, which is round-off between two different step sequences rather than the parts per thousand a straddled switch would leave. Not monotone, because which steps each route takes is luck rather than a trend.

  Two rules that are enforced rather than documented. **A stage may change what an electrode holds, not where it is** — moving metal would change the mask, so each stage would need its own solve and grid, and the field would still be computed and still be wrong. Refused, naming the electrode and the stage. And **the last stage holds after the sequence ends**: an instrument left alone stays where it was put, and a field switching off would make every ion still in flight suddenly coast — a physics change disguised as a bookkeeping one.

  A sequence needs no drive: a pulsed extraction is DC that switches, and a solve with stages and no `drive` is exactly that. Stages sharing a spatial pattern share their solve, so a trap that holds at one voltage and pushes at another costs one basis field.

- **Repeated geometry (SYM-1's discrete periodicity), and the funnel it makes possible.** An electrode may declare `repeat: { count, index }`; the index binds as an ordinary parameter so every expression on the electrode sees it, and copies are named by position (`ring-17`) so an error or a loss itemisation says which. Two functions were added for it — `floor` and `mod`, both dimensionless-only for the reason `sqrt` is; `mod` is Euclidean so `mod(-1,2)` is 1 and a backwards index still alternates.

  This is what keeps a 200-ring stack a *parametric* document rather than a generated one: "move every ring 50 µm and re-solve" is still sayable, which is the whole basis of §13's tolerance work.

- **`ion-funnel.json`** — a tapering stack of RF rings with a DC chain, written as one ring repeated. **The solve count does not grow with the ring count**: 8 / 24 / 48 rings all reduce to **2** basis solves. That is SYM-1's own argument measured, and it comes out at 2 rather than 3 because the two RF phases are exact negatives — one spatial pattern, one weight.

  An ion entering 6 mm off axis threads the whole stack and exits the 1.5 mm aperture, so it was compressed at least 4×. **The RF is demonstrably what confines**: switch the drive off and the ion ends on `ring-14`. Acceptance falls off properly with entry radius (1/3/6 mm through, 9/11 mm lost on named rings).

  **No gas**, which bites hardest here — a real funnel runs near a millibar and collisions damp the radial motion so ions settle onto the axis rather than ringing. The acceptance above is a lower bound. And a sign that has to be right: the DC chain starts at zero to match the grounded entrance, because putting the high potential there instead makes the boundary push the ion straight back out — which is what happened first, and the run said so, 3 metres upstream.

Adding a travelling-wave guide or a multipole should need only one more file — axisymmetry, repeats and RF all exist now. If it needs a change below `Einzel.Library`, LIB-1 says the abstraction is wrong — believe it.

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

Effort estimates, performance targets, regime boundaries, and the numerical error budget are **engineering judgement, not measured values**. Third-party library status, licence terms, MCP SDK capabilities, and CSnakes' Python version support were current at writing and must be re-checked before being committed to. §23 lists decisions still open — treat them as genuinely open rather than inferring an answer from elsewhere in the document. **Two of them are now closed and recorded in `docs/spec-findings.md`:** the FLD-1 linearity spike was run (it failed, then passed once cut cells landed), and the agent acceptance suite has been designed and built — see `docs/agent-acceptance.md` for what it measures and the recommended release gates.
