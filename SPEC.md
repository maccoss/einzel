# Einzel — living specification

**Status of this document.** `einzel-software-spec-r06.html` is the original
specification, written before any of this existed. It stays in the repository
unchanged, as the record of what was intended. **This page is the working
specification**: the same requirements, plus what building them established, plus
the places where the original turned out to be wrong or incomplete.

Where the two disagree, this page states the reality and says what the original
said. Nothing here silently overrides r06; every divergence is in
[Amendments](#amendments-to-the-specification) with the evidence that forced it.

**How to keep it current.** Update the register row when a requirement's status
changes, and add to Amendments when the *requirement itself* turns out to be wrong
rather than merely unbuilt. Both should happen in the same commit as the code, for
the same reason `AGENTS.md` is generated rather than hand-written: a status page
that has drifted is worse than none, because it is trusted.

---

## Where the project is

**570 tests across nine assemblies, green on Linux and Windows.** Warnings are errors; XML documentation is required on public API. Build clean. The EX-1 example corpus runs as a gate inside that suite (EX-2): 23 examples, every expectation a closed form, a published value, or an exact invariant.

| | Requirements |
| --- | --- |
| **Met**, with evidence | 64 |
| **Partial**, with a stated gap | 14 |
| **Not built** | 37 |
| **Unverified** — plausible but unmeasured | 3 |
| Total tagged in r06 | 118 |

A count is a weak summary and it flatters the project: the 37 not built are
concentrated in the update mechanism, distribution, the shell and MCP, which are
whole assemblies that do not exist, while the 64 met are spread across the parts
that carry numbers. The useful reading is the register, not the total.

**And the register under-counts whole sections, because it is tag-driven.** §16
gives eleven required shell views one tag between them, so the GUI appears as a
single row; §6's architecture invariants and §17's scope boundary carry none at
all. [The shell](#the-shell-and-the-rest-of-16) has its own section below for that
reason. Where a section of r06 is prose rather than tagged requirements, absence
from the register means nothing.

### What exists

```
Einzel.Core  Fields  Transport  Analysis  Library  Sweeps
Einzel.Io  Project  Extensions  Render  Commands  Cli
```

### What does not

```
Einzel.Compute      the SIMD and ILGPU dispatch layer (CMP-1, PERF-5)
Einzel.Mcp          the live-session server (MCP-1)
Einzel.Update       release check, staging, version policy (all of UPD, DST)
Einzel.Wpf          the shell (§16, UI-1) - all eleven required views
```

Two of those four are load-bearing for requirements that are otherwise met on
paper. Without a shell, AGT-2 ("nothing exists only in the window") cannot be
violated *or* confirmed; without `Einzel.Update`, GRD-11's defect taint has no
published floor to compare a version against.

---

## Delivery: planned against actual

The original plans five phases in sequence. **The project has not run that way**,
and the divergence is worth stating plainly rather than pretending the phases were
followed.

| Phase | Planned scope | Actual |
| --- | --- | --- |
| **1** · Spine, project, CLI | Model, units, symmetry, DC solver, superposition, tricubic, integrator, schema, errors, result objects, manifests, CLI, VTU | **Complete**, and its acceptance is met: ACC-1 on a reflectron, the memo's mirror pair tracked end to end, GRD-1 enforced with no bypass, an agent building a DC model from prose |
| **2** · Extensions, sweeps, shell, figures | Both extension runners, examples corpus v1, sensitivity fields, tolerance MC, optimisation, ILGPU, WPF shell, `Einzel.Render`, installer, update mechanism | **Split.** Sweeps, sensitivity fields, both optimisers, the sandboxed extension runner and `Einzel.Render` are done. The in-process runner, ILGPU, the shell, the installer, the update mechanism and **the examples corpus** are not |
| **3** · RF and pressure | Time-domain RF, statistical diffusion, collision models, gas velocity import, sequencer, space charge, Class B analysis, density export | **Scope complete.** Every named deliverable is built, gas velocity import included. What is left is on the acceptance side: the funnel benchmark needs a §23 decision and an affordable driven diffusive step, and Class B's spectrum and notch-width halves need an arbitrary waveform |
| **4** · Traps, animation, MCP | Waveform excitation, multi-notch isolation, trap sequences, device library, animation, MCP | Trap sequences and the device library are largely done ahead of schedule. Waveforms, animation and MCP are not started |
| **5** · Generalise and release | BEM, MSH interchange, CAD import, public repository | Not started |

**Why it went this way, and whether that was right.** The order has been driven by
*what makes the physics real* rather than by phase boundaries — each increment
picked the thing that would most change what the engine can honestly say. That has
worked: the numbers in [Validation](docs/validation.md) are the return on it, and
several were only reachable because an earlier increment removed an artefact
(§19's coaxial check needed cut cells; a multipole measurement needed cut-cell rod
surfaces; a turn-around time needed a source that can start at rest).

**What it has cost is the agent thesis.** §21's own sequencing principle is that
"the schema and the CLI are Phase 1 deliverables… which de-risks the thesis early",
and the corpus EX-1 asks for is the other half of that: an agent has no Einzel
forum posts or example files in its training data, and shipping models it can pull
into context is the counter. **One model of thirty exists.** That is now the single
largest gap in the project, and it is not a physics gap.

### Phase acceptance, checked

| Phase | Acceptance criterion | State |
| --- | --- | --- |
| 1 | Analytic and convergence tiers green; ACC-1 on a reflectron | Met — 1e-10, four orders inside |
| 1 | The memo's mirror pair tracked end to end | Met |
| 1 | GRD-1 enforced with no bypass | Met, by reflection over the public surface |
| 1 | An agent builds and runs a DC model from prose | Met, and measured — six tasks, 6 of 6 |
| 2 | An agent authors, tests and registers a working extension | Met |
| 2 | PERF-6 with the FLD-2 residual inside ACC-1 | Partial — the residual behaves (quadratic, (δ/L)²), the full campaign has not been run |
| 2 | Memo §6 items 1, 2 and 9 answered | Item 1 answered. 2 and 9 not worked up |
| 2 | A publication-quality vector figure generated headlessly in CI | Met |
| 2 | An update offered, deferred, later accepted | **Not met** — no update mechanism |
| 3 | Mathieu diagram reproduced | Met twice — ideal field q = 0.90684, solved round rods q = 0.90525 |
| 3 | Quadrupole transmission against resolution | **Met** — the band closes onto the tabulated apex q = 0.70600, R rising 1.6 to 15.6, both edges bisected to ACC-6 |
| 3 | Funnel transmission against a published benchmark | **Not met** — gas flow now exists, so what remains is the §23 decision on whose geometry, and a driven diffusive run being affordable |
| 3 | Cross-mode agreement in the overlap band | Met — 0.43 standard errors |

---

## Amendments to the specification

Places where building it showed the original to be wrong, incomplete, or right for
a reason it did not give. Each is a change to what the specification *should say*,
not merely a note about what is unbuilt.

[Spec findings](docs/spec-findings.md) carries the long form of most of these with
the measurements attached; what follows is the register of them.

### 1 · SYM-1 is missing translational invariance

**r06 §9** lists cylindrical symmetry, a mirror plane, and discrete periodicity.
The first real geometry needed none of them: a printed-circuit ion mirror is stripe
electrodes running along the drift direction, so the potential is genuinely
independent of that direction and the problem reduces exactly to two dimensions.

Not a minor omission — it removes a whole dimension from the solve, which is the
difference between a 513 × 513 grid and a 513³ one. **Recommend adding it to
SYM-1.**

### 2 · §11's turning-point step cap does not do what it says

The mechanism is implemented and it does not help. In a smooth field with a turning
point, the flight time reaches machine precision in **6 steps with the cap off** and
is marginally *worse* with it on at 105. §11's rationale is that position-error
controllers under-refine at a velocity minimum; `ErrorNorm` weights velocity error
with its own absolute floor, so it is not that controller.

**Recommend §11 state the goal — the turning point must be resolved — rather than
the mechanism.** The cap is kept, defaulting on, so the code honours the spec as
written; the evidence says it should default to 0.

### 3 · §11 is missing two step constraints that turned out to be necessary

Neither is in r06, and without either the integrator produces confidently wrong
answers.

- **A step may not outrun the field's resolution.** In a field-free region the
  local acceleration is near zero, the step heuristic proposes an enormous step, and
  the embedded estimator *correctly* certifies it — for a straight line. Observed:
  39 metres in one step, through both mirrors of a pair.
- **A step may not straddle a declared field discontinuity.** Dormand–Prince stage 4
  carries −56/15, so stage samples fall outside the step interval. Handling
  boundaries as events took a reflectron from 5.5e-10 to 1.7e-16.

### 4 · §10 understates what multigrid needs, and the fix is not the textbook one

r06 chooses finite-difference multigrid on the grounds that it is straightforward
to implement correctly. That holds for boundary-value geometries and **not** for
interior electrodes, which every device except a planar mirror has: four discs in a
box reached 1e134 V.

The textbooks point at Galerkin coarsening. What actually fixed it was **cut cells**
— a sub-cell surface has a position at any spacing, so the coarse mask can be
rebuilt from the geometry rather than projected down. 7–8 cycles at 0.019–0.023,
flat under refinement, against 43–47 at 0.55.

**Recommend §10 say that a Cartesian multigrid solver needs a boundary
representation that survives coarsening.**

### 5 · FLD-1 rests on an assumption r06 does not state

§23 recommended spiking the linearity assumption. It was run, **it failed**, and it
failed for a reason §10 does not anticipate. The *physics* is linear where §10 says.
The **discretisation was not**: below one cell a perturbation was invisible — the
perturbed solve returned bit-identical and the derivative field was identically
zero, so a study would report the parameter as having **no influence**. Above one
cell the residual was percent-level. There was no step size in between.

Cut cells fixed it, and the residual is now an ordinary Taylor remainder, quadratic
to three figures. **The limit is (δ/L)², so 1 ppm holds to δ/L ≈ 1e-3** and the
memo's 100–300 µm channels linearise to ≈1e-5. FLD-2 will correctly refuse them at
1 ppm; that is a legible trade rather than an artefact.

**Recommend §10 state the discretisation requirement explicitly:** sensitivity
fields need a boundary representation that varies continuously with the parameter,
and a node-by-node one does not.

### 6 · §19's cross-code tier is unavailable, and §22's risk does not apply

No SIMION licence — its cost is part of why this project exists. **Recommend §19 and
§22 be rewritten around that.** The schedule implication is favourable and the
validation implication is not: what carries the load instead is the analytic tier as
primary reference, literature regression promoted to the main external check, and
agreement between an analytic and a solved path substituting for agreement between
two codes.

### 7 · §9's source model assumed a beam, and §12 already required otherwise

§9 required a non-zero accelerating potential "or the ion never moves". That is
right for every device §9 works through and wrong for the whole class in §1's table
that traps and then releases — and **§12 already asked for turn-around time from
exactly such a device.** Two sections contradicted each other and neither was wrong
alone. Fixed by narrowing rather than removing: zero is legal when a declared field
could accelerate the ion.

### 8 · The parameter surface reached scalars but not vector components

§9 is unambiguous that "every placement is a parametric expression". Scalars
honoured it; `VectorValue` carried three literal numbers. It went unnoticed because
both early devices are symmetric about something convenient. **Now fixed** —
`VectorValue.Expression` takes one expression per component.

Worth keeping as a pattern rather than a bug: **the format was general in the places
the first two devices exercised and specific in the places they did not**, which is
precisely the argument §21 Phase 5 makes for validating generality with an
unrelated instrument.

### 9 · §10 does not say what a cell size means when the box does not divide by it

`Grid2D.OverBox` kept cells square and let the domain grow: a 60 × 20 mm box at a
1 mm cell **was solved as 60 × 30 mm**, silently. Fixed by giving each axis its own
spacing, so the extent is exact, neither direction is coarser than requested, and
the cost lands on cell shape (at worst 2:1) rather than on extent.

**Recommend §10 say which of the two readings it means.** They differ by fifty per
cent on an ordinary geometry.

### 10 · GRD-2 needs a rule about the shortest spelling

Twice, evidence about a computation's own quality was discarded at a seam because
discarding was the shorter code path: `FieldAssembly.Build` dropped its
`SolveReport` at the one place every run, study and test passes through, and
`FiguresOfMerit.Evaluator` dropped a draw's warnings along with its interval.

The rule that generalises, and that belongs in §4: **when a computation produces
evidence about its own quality, discarding that evidence must not be the shortest
spelling.** `BuildReported` returns field and warnings; the bare `Build` now
*throws* rather than concealing, because a plain field has no envelope to taint and
there is no third option between refusing and hiding.

A third instance surfaced later and is the same shape: `transport.gas.driftVelocity`
was honoured by the event-driven mode and silently dropped by the diffusive one.

### 11 · §23's agent-acceptance question, answered

§19 asks for scripted prose tasks; §23 leaves open what they measure and what gates
a release. Settled in [Agent acceptance](docs/agent-acceptance.md). Two decisions
were not obvious from the specification: **score actions, not self-reports**, and
**every task ships plausible wrong answers** that CI asserts must fail.

Recommended gates: 80% capability, 90% warnings, any task at 0% blocks, and **any
drop against the previous release blocks regardless of level** — the regression gate
matters more than the absolute one.

### 12 · LIB-1's test earned its keep, and fired three times in three different ways

Ten device templates now ship, and the rule has fired three times — each time
narrow, each time real, and each time meaning something different, which is the
part worth recording.

**A missing expression.** The travelling-wave guide: `drivePhase` was a plain
`double` while every other placement was an expression, so a phase could not depend
on the repeat index, and a phase that cannot depend on the index cannot ramp.

**A missing function.** `multipole-guide`: a `2n`-pole is `2n` rods at `π/n`
intervals and the grammar had no trigonometry, so that geometry could not be written
at all — not awkwardly, *not at all*. One function below the library bought one
template covering every even order instead of three near-identical files.
Amendment 17.

**A wrong check.** The Paul trap: `ModelValidator.CanDoWork` decided whether a
source may start at rest by asking whether any electrode held non-zero **DC**
potential. A trap holds zero DC and all of its potential as drive, so the archetypal
start-at-rest device was refused as a model in which nothing could move an ion.

The third is the one that needs care. LIB-1 says a change below the library usually
means the abstraction is wrong — but this change said "there is a bug here", and the
abstraction was fine. **Telling those two apart is part of using the rule**, and the
signal that separates them is whether the change *adds* something the format could
not say or *corrects* something it already claimed to support.

### 13 · A conservative operator was written twice and got it right once

The cylindrical Poisson operator is written in conservative form — flux through a
ring's outer face minus its inner face, over the ring's own volume — and r06's own
reasoning for it is recorded. **The cylindrical density solver was not**, and
computed a flux per unit area as though the two cells sharing a radial face had the
same volume. They do not: on the axis a cell is a disc rather than a ring and the
weight is **4**, the same factor of four the Laplacian carries there.

The error was largest on the axis, which is exactly where a funnel concentrates its
ions: the shipped funnel's ion ledger closed to **95.99%**, and closes to
**100.0001%** with the face weights carried.

Two things about how it was found are worth more than the fix. It was invisible
until the ledger was *made* to close — before interior electrodes absorbed
continuously and the seed's own overlap was accounted, launched, collected,
remaining and the named losses never had to add up, so a four per cent leak had
nowhere to show. And every conservation test in the suite was **Cartesian**, where
the weight is identically one; the tests passed for a reason that did not
generalise, which is the same failure mode as the uniform-field conservation test
that hid the cell-centred drift sample.

**Recommend §10 or §11 state it as a rule:** an operator written in conservative
form for one solver is not conservative in another that shares its grid, and a
conservation test on a Cartesian grid tests nothing about a cylindrical one.

### 14 · An unrecognised property was ignored, not refused

**Found by writing the corpus, on its first day.** A source cloud declaring
`transverseWidth` instead of `transverseSpread` parsed cleanly, validated, solved,
ran, and produced an emittance of **7.1e-8 µm where the closed form says 1.798** —
a plausible number from a model that reads as though it says something else.

This is the same rule r06 already argues for at length, applied to the key instead
of the value. §9 makes `{"energy": 4000}` a validation error on purpose, because
"unit ambiguity is the commonest source of silent wrongness and an agent building
from prose is the actor most likely to introduce it". A misspelled *field name*
that is silently dropped is the same failure with a shorter path to it, and §22
names its consequence as the defining risk of the whole thesis.

**Now refused**, with the offending property named by JSON Pointer and a
suggestion pointing at `einzel schema`. Four shipped test fixtures turned out to
be affected: they declared 1 mm clouds and had been running with point sources.

**Recommend §14 state it**: an unrecognised property is an error. The model format
is generated from the document types, so there is no version of "forward
compatibility" this trades away — a property the schema does not contain is a
mistake, not a future feature.

### 15 · ACC-5's transmission could not express zero

Also found by the corpus. `transmission` was read off an arrival-time peak, and a
peak needs two arrivals to have a width — so a quadrupole above its low-mass
cut-off, or an ion lost on a funnel ring, raised `INTERNAL_ERROR: a peak needs at
least two arrivals` and the run reported *itself* as a defect in the engine.

That is exactly backwards for a requirement whose entire subject is transmission
as a measured, itemised quantity. **An instrument that loses everything is the case
a reader most wants reported, and it was the one case the figure could not report.**

Worse one level up: `einzel run` caught the exception and returned *no ensemble at
all*, so the itemised losses — ACC-5's actual deliverable — disappeared precisely
when the transmission was zero.

Fixed by separating the two: a transmission is a count and needs no peak, while a
width still needs two points and is now **absent rather than zero** when there are
not two. The distinction that matters is kept: nothing arrived gives 0.0, a model
that could not be flown gives null.

**Recommend ACC-5 say so explicitly.** "Never 92 percent" is about itemisation;
this is about the endpoints, and a figure of merit that cannot represent total loss
is not measuring transmission.

### 16 · A guard one level above the thing that guards itself

Not a change to the specification so much as a rule the specification implies and
does not state. Scharfetter-Gummel's Bernoulli function handles a large argument
exactly, taking the limits explicitly to avoid an overflow inside `exp`. The flux
clamped that argument to ±40 *before* calling it — which protected nothing and
capped the effective drift at `40 D / h` above a cell Peclet of 40.

Measured on a drift tube whose expected transit is a division: **6.7% long
clamped, 0.86% long unclamped**, and the 0.86% is the packet's own spread because
it is now the same with and without a gas flow.

**Every test in the suite ran below the cap**, at a cell Peclet of 16, and reported
1.000000. They were correct and could not see it. What saw it was a corpus example
whose expectation is arithmetic the engine had no part in.

The rule worth writing into §19: **where a scheme has a dimensionless number in it,
the tests should straddle the values that number switches behaviour at, and should
print it.** A test below a threshold is not weak, it is a test of a different
regime, and nothing about it says which side it is on.

### 17 · §9's expression grammar had no trigonometry, and a multipole needs it

**LIB-1's test, run deliberately.** A multipole above four rods is `2n` rods at
`π/n` intervals, and the expression grammar could not write that — not awkwardly,
not verbosely, but **not at all**. So the choice was three near-identical template
files with coordinates written out longhand, or one function below the library.

One function. `cosPi` and `sinPi`, dimensionless-only for the same reason `sqrt`
is, in **half turns** rather than radians — the convention the drive decomposition
already chose, because `Math.Cos(Math.PI / 2)` is 6.1e-17 and a rod placed at a
quarter turn would carry a spurious dipole made of rounding.

What it bought: `multipole-guide` is **one template** with `poleCount` as a
parameter, and 4 / 6 / 8 / 12 rods each reduce to **one basis solve** (8 cycles,
factor 0.024–0.029). Twelve rods cost what four do.

**Recommend §9 note that the grammar's function set is part of what "every
placement is a parametric expression" means.** A placement that cannot be written
is a device that cannot be a template, and the list of functions is therefore a
statement about which devices are expressible — not a convenience.

### 18 · Two conductors could occupy the same space at different potentials

Found by getting Amendment 17 wrong first. Denison's rod ratio of 1.1468 is the
classical value for a **quadrupole**; applied to six rods it puts them through one
another, and the engine **solved it, converged in eight cycles, and returned a
field**. The acceptance measurement taken from it was really a measurement of rods
closing in on the axis.

A Dirichlet mask is written electrode by electrode, so where two overlap the last
one wins. Where both hold the same thing that is harmless and often deliberate — a
shape assembled from overlapping primitives is how a fillet gets built. Where they
disagree the region is simultaneously at +300 V and −300 V of drive, and the field
returned is of a geometry nobody described.

**Recommend §9 or §10 state it**: overlapping conductors that disagree about their
excitation are ill-posed and must be refused. Now done, naming both electrodes and
what each holds, with three deliberate limits — tangency allowed, agreement
allowed, and edge profiles skipped rather than guessed at.

### 19 · A stability boundary is not a property of the design alone

ACC-6 asks for a boundary resolved to one part in five hundred of the scan, and
`einzel boundary` reaches that in eleven evaluations. §12 and §19 both treat the
result as a property of the instrument. **On the shipped Paul trap it is not, until
the observation window is long enough**, and at sixty RF cycles the "boundary" is a
ragged strip:

```
   V   672 674 676 678 680 682 684 686 688 690 692
held     1   1   0   0   1   0   1   0   1   0   0
```

At two hundred cycles the same scan is a clean step between 672 and 674 V. Nothing
about the design changed. The growth rate goes to zero at the stability edge, so
whether a marginally unstable ion reaches an electrode inside the hold is a property
of **the hold**. Two bisections over brackets differing only in their lower end gave
680.7 V and 694.4 V for the same geometry, which is how it was noticed.

**Recommend §12 state that a Class B boundary is quoted with the observation window
that produced it**, and that a convergence check in that window is part of the
measurement rather than an optional extra — the same standing that grid convergence
already has under ACC-3.

**And with the launch amplitude, which is the sharper half.** The same trap's
hold-converged edge is q_z = 0.860 at a 0.1 mm launch, 0.824 at 0.3 mm and 0.635 at
0.6 mm. The Mathieu equation is linear, so an ideal trap's boundary *cannot* depend
on how far off centre the ion started — a trajectory scaled by a constant is another
trajectory. A real one's does, and the dependence is not small. Worse, there is no
clean small-amplitude limit to extrapolate to, for a structural reason: a
measurement that registers a loss only when the ion **reaches** an electrode is never
a small-amplitude measurement, whatever it was launched at. The launch offset sets
how much of the journey is spent in the anharmonic region, not whether any of it is.

The same geometry also carries a **narrow band of loss at q_z = 0.739–0.750**, sixty
volts inside the main edge, which survives a mesh doubling and a hold doubling and
vanishes at a 0.1 mm launch and at 60 cycles — a nonlinear resonance, and §12's
Class B vocabulary has no way to report one. It was found by the confirmation walk
of Amendment 20, from a search whose bisection had converged cleanly.

### 20 · Bisection cannot check its own premise, and now does

A corollary of Amendment 19, and separable from it. Bisection assumes the predicate
flips once across the bracket. **Every step it takes is consistent with that
assumption by construction**, so a clean cut-off and a frayed edge produce identical
search histories and the result looks equally confident either way. §12's "boundary
resolution" therefore says nothing about whether the located value is *the* edge.

What separates them is a walk outward from the converged bracket at geometrically
growing offsets, asking whether the predicate flips back. It costs about `log2` of
the range over the bracket width — roughly doubling an eleven-evaluation search,
against a grid that would cost five hundred and one.

`boundary.multiple-crossings` is a **validity violation**, because a value quoted as
the edge of a region when it is one of several is wrong rather than imprecise.
`boundary.single-crossing-checked` carries the probe count when nothing was found —
REG-2's rule that a check made and passed must be visible, or a reader cannot tell it
from a check never made. Two limits are stated rather than papered over: the walk
stays inside the declared range, and its first step is the bracket width, so a flip
narrower than that is stepped over.

---

## The shell, and the rest of §16

**The register is tag-driven, and §16 carries one tag between eleven required
views.** That is a property of the original document rather than of the shell's
importance, and reading the register alone would leave the impression that the
GUI is a footnote. It is not: §16 is a section, §17 is written largely around what
the shell must *not* own, and AGT-2 — the invariant that nothing exists only in
the window — is the load-bearing claim that the shell is a peer rather than the
product.

**None of it is built.** `Einzel.Wpf` does not exist. Every view §16 requires:

| View | State | What it needs beyond a window |
| --- | --- | --- |
| 3D viewport — geometry, potentials by colour, equipotentials, trajectory bundles | Not built | A raster path. Nothing here rasterises; Helix Toolkit on DirectX 11 is the named choice and is unverified since r06 |
| Density clouds instead of trajectories for diffusive regions (TRN-2) | **Half built** | The density exists, is exported as `.vti` and is drawn as contours in a section. What is missing is only the interactive surface |
| Figure composer | **Seam built** | `RenderSpec` is already text in `figures/` that the CLI executes. A composer edits one of these and nothing else — which is UI-1's own test, and the reason it can be built last |
| Animation timeline, per-phase playback rates, scrubbing, frame export | Not built | RND-7's non-linear time mapping, and a frame writer. Phase 4 |
| Model tree with parameter editing, live validation, units on every field | Not built | The validation and the units are done and reachable; this is presentation over `ModelValidator` |
| Sequence editor | Not built | The sequencer exists and stages are declared in the document; this is presentation over it |
| Results by accuracy class, uncertainty and warnings never behind a disclosure control | Not built | The envelope is enforced end to end, so the data is there. The requirement is really about layout, and it is the one most easily violated by a designer who has not read §4 |
| Regime inspector | Not built | REG-2's numbers are computed on every run already |
| Project view with model-drift and engine-drift state | Not built | `einzel verify` computes both |
| Extension manager | Not built | The manifest carries trust level, versions and compatible range; LIC-2 wants licences surfaced and nothing does |
| Journal with agent and human attribution | Not built | Needs MCP-1's shared linear undo stack, which needs the MCP server |
| Update notice with UPD-3's deferral options | Not built | Needs the whole of §18 |

**The pattern in that table is the interesting part.** Almost every row is
"presentation over something that already works" — which is what AGT-2 is supposed
to produce, and is weak evidence that it has. Only three rows need genuinely new
capability: the 3D viewport needs a raster path, the animation timeline needs
RND-7, and the journal needs MCP.

**But the invariant is untested, and that is the honest position.** AGT-2 says
every capability reachable from the window is reachable from the CLI through the
same command object. Today there is no window, so the claim cannot be violated and
cannot be confirmed either. An invariant only ever checked against one surface is
one that has already been broken by the time anyone notices — the same argument
this project makes for running CI on Linux from the first commit. The MCP server
is the cheaper second surface and would test it first.

**UI-1's prohibition is the half worth protecting.** The shell owns layout, input,
the interactive viewport and the update check, and owns no physics, no validation
rules, no file format knowledge and no render output. Two of those are already
guarded structurally: `Einzel.Render` sits below any shell and is exercised
headlessly in CI (RND-1), and the figure composer edits a spec the CLI executes.
The one to watch when a shell is written is **validation** — a live model tree
wants to check a field as it is typed, and re-implementing "is this a length?" in
the window is the obvious way to do it and the wrong one.

**Scope, which §22 names as a standing risk.** "Scope creep into building a
visualization application. The pull is constant and each step is individually
reasonable." Not having a shell has so far cost the project nothing that matters
and has kept §17's boundary honest: what leaves Einzel is a vector figure, a VTU
file and ParaView. The shell should be built when a *human* workflow is blocked
without it, not when the feature list looks incomplete.

## The requirement register

Statuses are assigned against the documentation and the code, not asserted from
intent. **Met** means there is evidence, and the evidence is named. **Unverified**
means the thing plausibly works and nothing measures it — it is not a synonym for
met.

The requirement column is abridged from the r06 HTML, which remains authoritative
for the full wording; a few statements run into neighbouring text where the tag sits
in a table.

### Accuracy (§8)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `ACC-1` | Numerical flight-time error, full analyzer ≤ 1 ppm 1/25 of the R = 20k peak-width budget | **Met** | 1e-10 relative on the analytic reflectron; 1.3e-13 between the analytic and solved paths. |
| `ACC-2` | Same, high-resolution validation mode ≤ 0.25 ppm To reproduce published R = 80k results | Not built | No separate high-resolution validation mode exists. Analytic cases already clear 0.25 ppm; nothing selects a tighter tier. |
| `ACC-3` | Field interpolation contribution ≤ 0.5 × | **Met** | Tricubic enforced; a forbidden interpolant is refused on a trajectory path. Bilinear measured at 9.4e-6 against bicubic 6.4e-8. |
| `ACC-4` | Energy drift, static field ≤ 1 ppm Cheap conserved-quantity diagnostic | **Met** | 1e-9 to 1e-15 in static fields. Reports NaN in a driven field, where energy drift is not a diagnostic. |
| `ACC-5` | Class S transmission interval ≤ 1% abs, 95% Drives minimum ensemble size per point | **Met** | Losses itemised by the surface name the author wrote; checked against erf for a slit at 0.95 sigma on 20,000 ions. A transmission of **zero** is now expressible - see Amendment 15, where it was not. |
| `ACC-6` | Class B boundary resolution ≤ 1/500 of scan Enough to resolve a mass filter peak shape | **Met** | `einzel boundary` bisects onto the crossing and reports it as an envelope whose interval **is** the bracket. Measured: a step at a known value bracketed to 1 part in 512 in 11 evaluations, against 501 for a grid; the quadrupole low-mass cut-off at **q = 0.90508 +/- 0.00039** against a tabulated 0.90804. The search now also **walks outward from its converged bracket** looking for the predicate flipping back, which is the one thing bisection structurally cannot see - every step of its own path is consistent with a single crossing by construction. `boundary.multiple-crossings` is a validity violation; the confirmation is reported whether or not anything was found. |
| `ACC-7` | Rendered geometric tolerance ≤ 0.1% of extent Default decimation bound for vector output; recorded per | **Met** | Ramer-Douglas-Peucker measured tight against its bound: 4,000 points to 577 at a worst deviation of 0.010000 mm against 0.01. |

### Agent instructions (§3)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `AGD-1` | einzel init writes an AGENTS.md containing both layers, the first generated and clearly delimited. Generate, never hand-write, the platform layer ... | **Met** | `einzel init` writes AGENTS.md with a generated, delimited, version-stamped platform layer; `einzel agents-md` regenerates it. |

### Agent-native design (§5)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `AGT-1` | The model is text Declarative, schema-validated, diffable JSON. A model file plus referenced artifacts fully determines a run. | **Met** | Schema-versioned JSON, currently 0.5. `einzel schema` generates the JSON Schema by reflection over the document records. An unrecognised property is **refused**, not ignored - see Amendment 14. |
| `AGT-2` | Nothing exists only in the shell Every capability reachable from the window is reachable from the CLI and from MCP, through the same command objects. This ... | Partial | Every capability is a command object and the CLI drives them. Untested against a second surface, because neither MCP nor the shell exists. |
| `AGT-3` | Errors are recovery instructions Machine-readable code, offending path, violated constraint, observed value, suggested correction. | **Met** | Code, JSON Pointer path, constraint, observed value, suggestion, severity. Validation collects every error rather than throwing on the first. |
| `AGT-4` | Results carry their own uncertainty See §4. No quantitative result is ever returned as a bare number. | **Met** | GRD-1 enforced by reflection over the public surface of `Measured`, verified by injecting a violation and watching it fail. |
| `AGT-5` | Feedback loops are cheap A preview tier returns in seconds and is permanently labelled. | **Met** | `einzel preview` is 9 ms against a full run on the shipped reflectron, tainted on the number itself, and writes nothing. |
| `AGT-6` | Feedback is visual where that helps Rendered images an agent can inspect. Geometry errors are obvious in a picture and invisible in JSON. | Partial | `einzel render section` produces SVG and PDF headlessly. There is no raster path, so an agent cannot get a picture of a 3D geometry. |
| `AGT-7` | The platform is self-describing at runtime Schemas carry descriptions and units on every field; templates, extension types, and examples are enumerable. | **Met** | Schema descriptions come from the XML doc comments the build already requires; missing XML degrades to structure and says so. |
| `AGT-8` | The environment is stable within a session Nothing about the installed platform changes while an agent is working. See §18. The error object { "code" : ... | **Met** | The CLI makes no network call in any path. Only the shell would check for updates, and there is no shell. |

### Command line (§15)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `CLI-1` | Every command emits structured output. A --json flag on every verb produces machine-readable output including the full | **Met** | `--json` on every verb. |
| `CLI-2` | Results on stdout, progress and diagnostics on stderr. | **Met** | Results on stdout, diagnostics on stderr, driven through `Program.Main` in the end-to-end tests. |
| `CLI-3` | Exit codes are meaningful and documented. Distinct codes for validation failure, regime violation, cost-gate refusal, convergence failure, engine-pin ... | **Met** | Distinct exit codes per failure class, including a regime violation getting its own code rather than a convergence one. |
| `CLI-4` | Every mutating command supports --dry-run . | **Met** | `--dry-run` on every mutating command, asserted to write nothing. |
| `CLI-5` | Output ordering is deterministic. | **Met** | Deterministic output ordering. |
| `CLI-6` | Startup is fast and offline. No command touches the network unless the user explicitly asks it to. Verb Purpose einzel init Create a project; --vcs git ... | **Met** | Cold start 73-147 ms against PERF-8 500 ms, with no network call in that path. |

### Compute (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `CMP-1` | The scalar reference implementation is never deleted or allowed to rot. Collisions and space charge Model Regime Used for Mobility-based, no discrete ... | Not built | `Einzel.Compute` does not exist, so there is one path and nothing to test it against. The scalar implementation is the only implementation. |

### Collisions (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `COL-1` | Scattered ions remaining within acceptance are tracked to the detector with arrival times recorded, not discarded as losses. | **Met** | Scattered ions inside acceptance are tracked to the detector with their arrival times, not discarded. |

### Distribution (§18)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `DST-1` | Windows: a per-user installer requiring no administrator rights, so it works on a locked-down instrument PC, published as an asset on the GitHub release. | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |
| `DST-2` | Portable zip alongside, for machines where installing is not permitted. | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |
| `DST-3` | Linux: a tarball, engine and CLI only. No installer and no silent updater. | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |
| `DST-4` | SHA256SUMS.txt published with every release. | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |
| `DST-5` | Releases are built by CI on a version tag. Nothing distributed is ever built locally. | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |
| `DST-6` | The vendored Python runtime ships in the installer and the tarball. | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |
| `DST-7` | Optional external tools are detected, never bundled : the video encoder ( | Not built | No installer, no signed build, no release artifacts. Nothing here is released software. |

### Examples corpus (§5)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `EX-1` | Ship at least thirty validated reference models spanning every device class, each with a prose description, expected results, and assertion tolerances. | Partial | **23 of the thirty**, spanning free flight, accelerating gaps, reflectrons, an orthogonal accelerator, a thermal source, an einzel lens, a DC and an RF quadrupole, a hexapole guide, a funnel, a travelling-wave guide, an extraction trap, a 3-D Paul trap held and ejected, the diffusive mode and a measured transmission. Every expectation is arithmetic, a published value, or an exact invariant. Missing: an MR-TOF and a collisional example. |
| `EX-2` | The corpus runs in CI; a failing example blocks release. | **Met** | `ExampleCorpusTests` materialises every example into a real project and drives `einzel test` through `Program.Main`. 17 of 17 in 29 s, so it is affordable on every change rather than at release. It also asserts that every example ships a test and describes itself. |
| `EX-3` | Examples are enumerable and fetchable from both surfaces. | Partial | `einzel examples` enumerates and prints, and `einzel new --from-example` writes the model **and its test**, rewriting the model reference to wherever the file landed. Still one surface, because there is no second one. |

### Extensions (§12)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `EXT-1` | An extension declares type, schemas, trust level, resource needs, and a compatible engine version range . The runtime is an implementation detail of the ... | **Met** | The manifest declares type, schemas, trust level, resource needs and a compatible engine range. `trust` defaults to sandboxed rather than being opted into. |
| `EXT-2` | In-process (CSnakes) for first-party and explicitly trusted extensions. Lowest latency, no isolation. | Not built | The in-process CSnakes runner is not built. Section 23 leaves open whether it is worth shipping at all; sandboxed-only has so far been sufficient. |
| `EXT-3` | Sandboxed subprocess for anything agent-authored or third-party, and the default. Job objects and a restricted token on Windows, namespaces and seccomp on ... | Partial | The subprocess boundary is real: wall-clock timeout with process-tree kill, output ceiling, zero inherited environment, `python -I`, scratch working directory. **Network, filesystem and memory confinement are not enforced** - `extension.isolation-incomplete` is a non-suppressible violation on every sandboxed result. |
| `EXT-4` | Never invoked per integration step. One call per run. | **Met** | Structural rather than advisory: a subprocess cannot be invoked per step at any useful rate. Measured round trip 49 ms against PERF-7 50 ms. |
| `EXT-5` | Large arrays cross by shared memory with an Arrow or raw-buffer layout, never by JSON. | Not built | Large arrays still cross as JSON. No shared memory, no Arrow layout. |
| `EXT-6` | A vendored interpreter ships with the application. | Not built | An interpreter is **discovered**, not vendored. `einzel doctor` says so rather than passing it off. |
| `EXT-7` | Outputs are attributed per | Partial | A deliberate JSON Schema subset - type, required, properties, items, enum, numeric bounds - because a full implementation would put remote `$ref` resolution inside a sandbox whose point is having no network. Unrecognised keywords are ignored rather than refused. |
| `EXT-8` | Before an update is applied, the updater reports which installed extensions fall outside the new engine's compatible range. The cleanest extension of all ... | Not built | Needs the updater, which does not exist. |

### Field subsystem (§10)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `FLD-1` | For each perturbation channel p , cache ∂Φ/∂p by finite difference over a full re-solve. | **Met** | Cached shape derivatives, validated to 6.5e-6 of the closed form at a 0.11-cell step. **Only after cut cells**: the first spike failed, see Amendments. |
| `FLD-2` | Every sweep runs a stratified validation subset; if the maximum residual exceeds | **Met** | The residual is an ordinary Taylor remainder, quadratic in the perturbation to three figures. The limit is (delta/L)^2, so 1 ppm holds to delta/L about 1e-3. |
| `FLD-3` | Field caches are keyed by content hash over geometry, mesh, symmetry declaration, boundary conditions, and the solver-behaviour version — the last term is ... | **Met** | Caches keyed by content hash including the solver-behaviour version. |

### Gas (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `GAS-1` | A gas region carries species, temperature, a pressure field , a bulk velocity field , and a collision model. The velocity field is easy to omit and hard ... | Partial | Species, temperature, collision model, a uniform bulk velocity, and now an imported velocity **field** - VTK ImageData, sampled trilinearly, conserved at the face, with the overhang past its extent reported. Two gaps left: the **pressure** is still a single number, and the event-driven mode refuses a field rather than using one, because it draws a neutral velocity without a position. |

### Guardrails (§4)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `GRD-1` | No bare numbers Every quantitative result carries value, units, uncertainty or confidence interval, the ensemble size or convergence measure behind it, ... | **Met** | No member of `Measured` returns a bare magnitude; enforced by reflection so the rule governs members nobody has written yet. It survives to the wire - `MeasuredJson` is built only by deconstructing. |
| `GRD-2` | Warnings propagate Validity warnings travel with the result through every layer — engine, command layer, CLI output, MCP response, exported file, rendered ... | **Met** | Warnings propagate through engine, command layer, CLI, exported VTU/VTI files and figures. Two places where they were being dropped have been found and fixed; see Amendments. |
| `GRD-3` | Warnings above threshold are not suppressible Validity violations cannot be silenced by any caller, including in batch mode. | **Met** | Validity violations carry a non-suppressible severity and no caller can silence them. |
| `GRD-4` | Validity is checked, not assumed Regime applicability, mesh convergence, ensemble convergence, adiabaticity, and the §10 linearization residual are ... | **Met** | Regime applicability, mesh convergence, ensemble convergence and the linearisation residual are all computed rather than assumed. |
| `GRD-5` | Preview results are labelled and cannot be promoted Tagged permanently; cannot be quoted, exported, fed to an optimizer, or rendered without visible ... | **Met** | The taint rides on the number, and a preview writes nothing - a tainted result in `results/` would be reported as current by `verify`. |
| `GRD-6` | Extension results are attributed Carries the extension identity and version; cannot present itself as first-party. | **Met** | Extension results carry the extension identity and interpreter; the manifest records `null` where no interpreter took part. |
| `GRD-7` | Results are immutable and traceable Every result references a manifest. Every rendered artifact references a result. | **Met** | Every result references a manifest. Studies wrote none at all until recently; sweeps, optimisations and scans all write one now. |
| `GRD-8` | Spending is deliberate Any operation exceeding a configurable cost threshold requires a prior estimate. | **Met** | `einzel estimate` reports cost before the run. A diffusive run's step is computable exactly and predicted 901 against 901 actual; a trajectory run's cost is path-dependent and the estimate says so. |
| `GRD-9` | Human work is never silently lost Where an agent and a human share a live model, mutations are attributed in a shared linear journal. | Not built | Needs a shared live session, which needs MCP. Nothing yet. |
| `GRD-10` | Drift is detectable, in both directions A stored result can be checked against both the current model and the currently installed engine. | **Met** | `einzel verify` separates drift from notes: an edited model or a changed solver-behaviour version invalidates; a different engine build with identical numerics does not. |
| `GRD-11` | Known-defective versions taint their output A result produced by a version below the published floor (§18) carries a non-suppressible defect warning. The ... | Partial | The taint mechanism exists and rides in the warning list. There is no published defect floor to compare against, because there are no releases. |
| `GRD-12` | A rendering never looks more precise than its data Decimation tolerance, time compression, and preview status are recorded in every rendered artifact ... | **Met** | Decimation tolerance recorded in the file, the `--json` result and stamped on the page. Density contour levels likewise. |

### Device library (§14)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `LIB-1` | Device templates are data in the same schema as any other model , plus a declared parameter surface. If supporting a new device requires a change below ... | **Met** | Ten device templates as data in the model schema. Two have needed a change below `Einzel.Library` to *express* the device, and both were narrow and general: `drivePhase` becoming an expression (the travelling wave), and trigonometry in the expression grammar (any multipole above four rods). The second yielded **one** template covering quadrupole, hexapole, octupole and beyond rather than three files. A third change - the Paul trap - is worth distinguishing: nothing was missing, a validator was **wrong**. `CanDoWork` asked whether any electrode held non-zero **DC**, so a trap holding all of its potential as drive was refused as a model in which nothing could move an ion. LIB-1 says to believe the signal when a template needs a change below the library; this one said "there is a bug here", not "the abstraction is wrong", and telling those apart is part of using the rule. |

### Licensing (§20)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `LIC-1` | No GPL dependency in the default build, ever. Where GPL functionality is genuinely useful it is invoked out-of-process as a tool the user supplies, and ... | **Met** | No GPL dependency. The PDF writer is hand-written partly for this reason; `Directory.Packages.props` carries a licence note on every entry. |
| `LIC-2` | Extensions carry their own licences; the extension manager surfaces them. | Not built | No extension manager, so nothing surfaces extension licences. |

### Live session (§16)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `MCP-1` | Mutations are attributed and the undo stack is shared and linear. | Not built | `Einzel.Mcp` does not exist. Phase 4. |

### Performance (§8)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `PERF-1` | Nominal field solve, all basis solutions < 30 min Cached. Symmetry reduction makes this reachable for a 200-ring funnel | **Met** | A 96-ring travelling-wave guide reduces to two basis solves; a 48-ring funnel to two. Well inside 30 minutes. |
| `PERF-2` | Single ion, cached fields < 100 ms Interactive tuning must feel live | Unverified | Not measured as a target. A single ion through cached fields is fast in practice; no benchmark asserts it. |
| `PERF-3` | Preview tier, any model < 10 s | **Met** | 9 ms on the shipped reflectron against a 10 s budget. |
| `PERF-4` | 10 4 -ion ensemble, Class S < 5 min CPU, embarrassingly parallel | Unverified | Ensembles of 20,000 ions are run in tests, but wall time against the 5-minute budget is not asserted. |
| `PERF-5` | Quadrupole stability scan, 500 × 10 3 < 2 h GPU-bound; why ILGPU is early | Not built | Needs the GPU path. `einzel scan` makes the scan expressible; nothing makes it fast. |
| `PERF-6` | Tolerance sweep, 10 3 geometries × 10 3 ions < 8 h Only reachable via §10 sensitivity fields | Partial | The superposition side is measured - 500 linearised draws at 25 ms against 142 ms for one solve. The full 10^3 x 10^3 campaign has not been run. |
| `PERF-7` | Extension round trip, sandboxed < 50 ms Sets the granularity floor for | **Met** | 49 ms median round trip against the 50 ms budget. |
| `PERF-8` | CLI cold start to first output < 500 ms No network call permitted in that path | **Met** | 73-147 ms cold start against 500 ms. |
| `PERF-9` | Vector figure, 10 3 decimated trajectories < 5 s Agents iterate on figures; it must not be a batch job | Unverified | Figures are drawn in tests but not timed against the 5 s budget. |
| `PERF-10` | Vector figure file size, same < 5 MB Must open in a text editor and an illustration program | Partial | The quadrupole PDF is 13 KB. No test asserts the 5 MB ceiling for 10^3 trajectories, because nothing draws 10^3 trajectories yet. |

### Project (§3)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `PRJ-1` | Models, studies, extensions, render specs, and results are text. | **Met** | Models, studies, extensions, render specs and results are all text. |
| `PRJ-2` | Large artifacts are referenced by content hash, never embedded. | **Met** | Large artifacts live in `.einzel/` and are referenced, never embedded. |
| `PRJ-3` | A run manifest fully determines its run. Model hash, seeds, engine version, transport mode, solver settings, extension identities. Results are therefore ... | **Met** | Model hash, seeds, engine version, solver-behaviour version, transport mode, compute path, extension identities, interpreter and machine. |
| `PRJ-4` | A plain folder is the default and fully supported. Every feature, guardrail, and agent workflow works in a directory with no repository. Requiring one ... | **Met** | A plain folder is the default. `--vcs git` writes an ignore file and changes no behaviour anywhere else. |
| `PRJ-5` | Version control, where wanted, is scaffolded. einzel init --vcs git writes an ignore file excluding .einzel/ , an AGENTS.md , and a starter layout. | **Met** | `einzel init --vcs git`. |
| `PRJ-6` | Nothing depends on a hosting provider. Agent instructions in the project An agent needs two distinct things: how to use Einzel (platform knowledge, ... | **Met** | Nothing depends on a hosting provider; the whole loop is files and commands. |
| `PRJ-7` | A project may declare assertions — expected resolving power, transmission floors, envelope constraints — runnable as einzel test . An agent that edits a ... | **Met** | A test is a file in `tests/` naming a model and a figure of merit with a relative tolerance. `init` ships one whose expectation is a closed form. |

### Regime (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `REG-1` | Trajectory integration and statistical diffusion are peer implementations of ITransportMode . | **Met** | Both modes are `ITransportMode` implementations and both are available. `ProducesTrajectories` is on the interface so a renderer asks rather than infers. |
| `REG-2` | The engine computes the governing dimensionless numbers along every path and raises a non-suppressible warning when the selected mode is outside validity. | **Met** | Knudsen, mean free path, collisions per flight and per RF cycle computed on every run and **reported whether or not anything crosses a threshold**. A regime violation gets its own exit code. |
| `REG-3` | In the overlap band both modes run on the same model and the comparison is a supported operation with its own report. Accuracy classes Class T, timing. ... | **Met** | Trajectory 13.2555 +/- 1.3584 m/s against diffusion 13.8418 - 0.43 standard errors, between machineries sharing only a cross section. `einzel compare` is the supported operation. |

### Rendering (§17)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `RND-1` | Rendering is an engine capability, not a shell feature. Einzel.Render sits below the shell; the figure composer and einzel render are peer consumers of ... | **Met** | `Einzel.Render` sits below any shell and draws headlessly on the Linux CI runner with no display, window manager or font server. |
| `RND-2` | A render spec is text , lives in figures/ , and is versioned with the model. The figure in a paper is regenerable from the repository rather than being a ... | **Met** | A render spec is text in `figures/`, versioned with the model. |
| `RND-3` | 2D sections and orthographic projections emit SVG and PDF , through a geometric projection pipeline that produces paths rather than pixels. This is a ... | **Met** | SVG and PDF from a path pipeline. Both writers are hand-authored; a test walks every PDF cross-reference offset. |
| `RND-4` | Shaded 3D perspective is raster. Hidden-surface vector output is a deep rabbit hole with poor payoff. Schematic 3D with hidden-line removal may be added ... | Not built | No raster path at all, so neither shaded 3D nor `render still`. Section 23 leaves open whether hidden-line vector output is worth building. |
| `RND-5` | Trajectories are decimated with a stated geometric tolerance ( | **Met** | Stated and measured, and the point-to-segment distance is clamped - a reflectron is why. |
| `RND-6` | Text stays text. Labels, dimensions, and axis annotations are selectable and editable in the output, so a figure can be relabelled for a different venue ... | **Met** | Labels are text runs in both SVG and PDF, asserted in both. |
| `RND-7` | ), scrubbing, and frame export. Model tree with parameter editing, live validation, units on every field, template instantiation. Sequence editor : the ... | Not built | No animation, so no time mapping to display. Phase 4. |
| `RND-8` | Diffusive regions animate as evolving density fields , never as particles ( | **Met** | Enforced rather than stated: the renderer asks the mode and draws no trajectories when it says no. **And now draws the density instead**, which the prohibition previously left empty. |
| `RND-9` | ) einzel export vtu Fields, trajectories, or density clouds for ParaView einzel ext test / register Extension authoring loop einzel schema / templates / ... | Not built | No video, so no external encoder to detect. |
| `RND-10` | Videos carry provenance visibly , as a corner stamp with engine version and model hash, in addition to container metadata. A video is the artifact most ... | Not built | No video. |
| `RND-11` | Preview-tier results are visually distinguishable in any rendered output ( | **Met** | A hatched rule the width of the page and a QUALIFIED line naming the code. |
| `RND-12` | Fields, trajectories, and density clouds export to VTK/VTU. The consequence is deliberate and worth stating as a scope decision rather than leaving ... | **Met** | Fields export as `.vti`, trajectories as `.vtu`, and densities as `.vti` - the last only since the density became an output at all. |
| `RND-13` | Gmsh MSH is supported for import and interchange, through an Einzel-authored reader and writer. GPL attaches to Gmsh's implementation, not to the format; ... | Not built | No MSH reader or writer. Phase 5. |
| `RND-14` | MSH is interchange, never the native representation , for the reason given in §9: a mesh is already discretized and cannot carry the parametric intent ... | **Met** | Vacuously, and by design: the native representation is parametric JSON and there is no mesh path to be tempted by. |
| `RND-15` | Where meshing itself is eventually needed — with the boundary element solver, not before — Gmsh is detected, not bundled. Shelling out to a GPL binary the ... | Not built | No BEM solver, so no meshing, so nothing to detect. |

### Space charge (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SC-1` | For Class T runs the space-charge approximation parameters are validated against the direct method on a reference population. | Partial | The direct pairwise sum is built and validated (third law to 1e-14, uniform-sphere closed form to 5%). The approximate method it exists to validate - particle-in-cell - is not built. |

### Sequencer (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SEQ-1` | A phase boundary may change transport mode; the conversion is explicit, reported, and named as a source of uncertainty. | Not built | A sequence may change parameters; it may not change transport mode. Nothing converts a packet between descriptions. |

### Symmetry (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SYM-1` | A geometry subtree may declare cylindrical symmetry, a mirror plane, or discrete periodicity. The solver reduces accordingly and the interpolant ... | Partial | Cylindrical, mirror planes and discrete periodicity all implemented and measured. **Translational invariance is missing from the requirement**, and is the one the first real geometry needed - see Amendments. |

### Trajectory output (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `TRJ-1` | Trajectory output for rendering is a separately sampled stream with its own cadence, independent of integration steps. Integration steps cluster where the ... | Partial | The render stream can be coarser than the integration steps but never finer. Full independence needs dense output, which is not implemented. |

### Transport (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `TRN-1` | Mobility is an explicit input with stated field dependence. | **Met** | Mobility is a declared input; a derived one is marked `mobility.derived`, and `IsWithinFit` refuses to leave the caller to work out whether the field dependence still holds. |
| `TRN-2` | Diffusive transport emits a time-resolved density field rather than trajectories, because that is what it computes. This is what §17 renders for a funnel. ... | **Met** | A density field, now with somewhere to go: exported as `.vti`, drawn as contours, and assertable through the `transitTime` figure of merit - which did not exist, so the mode's principal scalar could not be pinned by a project test or ranked by a study. |

### Test (§19)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `TST-1` | Every performance path is tested against the scalar reference. | Not built | There is no second performance path, so nothing is tested against the scalar reference. Vacuously true and not worth much. |
| `TST-2` | Every golden-file tolerance carries a comment explaining its magnitude. | Partial | Tolerances in the suite are justified in comments as a matter of practice, and every literature comparison states its source. Not enforced. |

### Shell (§16)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `UI-1` | The shell owns layout, input, the interactive viewport, and the update check. It owns no physics, no validation rules, no file format knowledge, and no ... | Not built | No shell. The half of it that is a *prohibition* - the shell owns no physics, no validation, no format knowledge and no render output - is honoured by construction and untested, because there is no shell to violate it. The figure composer seam it names does exist: `RenderSpec` is text a composer would edit and the CLI already executes. |

### Update (§18)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `UPD-1` | The shell is the only component that checks for updates, and only at launch. No periodic timer, no background polling. | Not built | No shell, so nothing checks for updates. |
| `UPD-2` | The CLI never contacts the network. einzel doctor reports from a cache the shell writes; --check performs an explicit live check because the user asked. ... | **Met** | Satisfied in the strong direction: the CLI makes no network call at all, and there is no cache for `doctor` to read because nothing writes one. |
| `UPD-3` | . UI-1 The shell owns layout, input, the interactive viewport, and the update check. It owns no physics, no validation rules, no file format knowledge, ... | Not built | Not built. |
| `UPD-4` | The notice is non-modal. | Not built | Not built. |
| `UPD-5` | A project may pin an engine version. A pinned project run under a different version reports the mismatch and exits with a distinct code rather than ... | Not built | Not built. |
| `UPD-6` | An update never applies while a run, sweep, or solve is active , and never mid-session. Staged on download, applied on next launch. | Not built | Not built. |
| `UPD-7` | Applying an update regenerates the platform layer of AGENTS.md in projects opened afterward. | Not built | Not built. |
| `UPD-8` | Before applying, the updater reports what will change : extensions falling outside the new engine's compatible range, whether field caches will be ... | Not built | Not built. |
| `UPD-9` | Linux and portable installs never self-update. einzel self-update is always explicit. The version floor, without coercion A published policy file declares ... | Not built | Not built. |
| `UPD-10` | A version below the floor keeps working normally . | Not built | Not built. |
| `UPD-11` | Every result it produces carries a non-suppressible defect warning naming the defect ( | Not built | Not built. |
| `UPD-12` | einzel verify reports which stored results were produced by a version now known defective. This is the recall mechanism , and it works retrospectively on ... | Not built | Not built. |

---

## Open decisions

§23's list, with what has been settled since.

| Decision | State |
| --- | --- |
| Availability on NuGet, GitHub, and as a mark | Open |
| Spike the FLD-1 linearity assumption before Phase 2 | **Closed.** Run, failed, fixed by cut cells. The two-week estimate was right and the spike returned a negative result first, which is what a spike is for |
| Whether hidden-line 3D vector output is worth building | Open, and cheaper to leave open than it looks: there is no raster path either |
| Whether the provenance stamp on figures is default-on | **Closed in practice** — default-on, with the taint on the page |
| Whether to write the VTU writer or take a dependency | **Closed.** Written. Under a week, and it now writes fields, trajectories, 3D volumes and densities |
| What triggers revisiting code signing | Open |
| What the defect-floor policy file contains | Open, and untestable until there are releases |
| What the agent acceptance suite measures and what gates a release | **Closed.** See Amendment 11 |
| Whether the in-process extension runner is worth shipping at all | Open, and the evidence so far says sandboxed-only is sufficient: nothing has hit the 49 ms granularity floor |
| Whether the funnel benchmark uses a published geometry or one of ours | **Open, and now blocking.** It gates a Phase 3 acceptance criterion, and the study should not be built before it is settled |
| Governance if this becomes a collaboration | Open |

---

## What to do next

Ordered by what unblocks the most, with the reasoning rather than just the list.

1. **Finish the examples corpus (EX-1).** 23 of thirty, and the gate (EX-2) is
   built and green. What the first seventeen cost was mostly *deciding what can
   honestly be asserted*, and that work is now done — the remaining seven are
   breadth: an MR-TOF, a collisional example, and more of the diffusive mode, which
   `transitTime` now makes assertable. Worth finishing, and worth
   noticing what the first tranche already returned: **two defects that no test
   written from inside the project would have caught**, because both were about a
   model that validates and answers a different question.
2. ~~**Class B analysis**~~ — **done.** `einzel boundary` bisects to ACC-6, and the
   transmission-against-resolution curve closes onto the tabulated apex, which is
   Phase 3 acceptance criterion 3. What is left of §12's Class B needs an
   **arbitrary waveform**: the secular frequency spectrum, and isolation efficiency
   against notch width. §9 lists arbitrary waveforms as an excitation an electrode
   may carry and this build has only sinusoid and rectangular, so that is the
   unblocking piece rather than more analysis.
3. ~~**A gas velocity field (GAS-1)**~~ — **imported fields work.** VTK ImageData,
   sampled trilinearly, conserved at the face, agreeing with a declared uniform
   vector to two ulps. Two gaps remain and both are worth naming: the **pressure**
   is still a single number for the whole model, which a differentially pumped
   instrument is not; and the **event-driven mode refuses a field** rather than
   using one, because `CollisionSampler` draws a neutral velocity without a
   position. Threading a position through the collision path is the work.
4. **Make a driven diffusive run affordable.** The ponderomotive well's gradient at
   the ring edges sets the explicit step: on the shipped funnel at 2 mbar the step
   is 1.067 ns against a diffusion limit of 5.2 µs, a factor of 4,900, so 900 µs
   would be about 843,000 steps. Attributed by control — 15.5 ns at 0 V RF, 8.93 ns
   at 25 V, 1.067 ns at 100 V — so it is the RF and roughly as E₀². An implicit or
   operator-split step is the fix.
5. **Particle-in-cell space charge (SC-1).** The reference method it is validated
   against now exists, which is the right order and was the reason for building the
   direct sum first.
