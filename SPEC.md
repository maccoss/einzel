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

**1,021 tests across twelve assemblies, green on Linux and Windows.** Warnings are errors; XML documentation is required on public API. Build clean. The EX-1 example corpus runs as a gate inside that suite (EX-2): 37 examples, every expectation a closed form, a published value, or an exact invariant.

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
Einzel.Update       release check, staging, version policy (all of UPD, DST)
Einzel.Wpf          the shell (§16, UI-1) - all eleven required views
```

Two of those four are load-bearing for requirements that are otherwise met on
paper. Without a shell, AGT-2 ("nothing exists only in the window") cannot be
violated *or* confirmed; without `Einzel.Update`, GRD-11's defect taint has no
published floor to compare a version against.

`Einzel.Wpf` is a **deliverable rather than a permission**, and the Windows GUI
capability was part of why the toolchain is C# - a rationale r06 never records. See
[the shell section](#the-shell-and-the-rest-of-16) and Amendment 25.

---

## Delivery: planned against actual

The original plans five phases in sequence. **The project has not run that way**,
and the divergence is worth stating plainly rather than pretending the phases were
followed.

| Phase | Planned scope | Actual |
| --- | --- | --- |
| **1** · Spine, project, CLI | Model, units, symmetry, DC solver, superposition, tricubic, integrator, schema, errors, result objects, manifests, CLI, VTU | **Complete**, and its acceptance is met: ACC-1 on a reflectron, the memo's mirror pair tracked end to end, GRD-1 enforced with no bypass, an agent building a DC model from prose |
| **2** · Extensions, sweeps, shell, figures | Both extension runners, examples corpus v1, sensitivity fields, tolerance MC, optimisation, ILGPU, WPF shell, `Einzel.Render`, installer, update mechanism | **Split.** Sweeps, sensitivity fields, both optimisers, the sandboxed extension runner and `Einzel.Render` are done. The in-process runner, ILGPU, the shell, the installer, the update mechanism and **the examples corpus** are not |
| **3** · RF and pressure | Time-domain RF, statistical diffusion, collision models, gas velocity import, sequencer, space charge, Class B analysis, density export | **Scope complete.** Every named deliverable is built, gas velocity import included. What is left is on the acceptance side: the funnel benchmark needs a §23 decision and an affordable driven diffusive step. **Class B is complete** - the secular spectrum against the Mathieu characteristic exponent, and isolation efficiency against notch width on an arbitrary waveform that recovers the published digital cut-off |
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

**And the same argument arrived a third time, for the inverse.** The C-trap's
electrodes are chains of beads on an arc, and its ejection slot is the gap between
two of them — but the gap between two bead *centres* is not the opening between two
*surfaces*. A sphere of radius `a` centred at radius `R` reaches `asin(a/R)` past
its own centre, which for the shipped numbers is **14.7 degrees on each side**, so
a declared 27-degree slot opened **minus two** and the ejected ion struck the
bounding bead.

So the grammar gained `asinPi`, and the pattern is worth naming: placing something
by angle when what is known is a **length ratio** needs an inverse trigonometric
function, and it needs to return **half turns** so that the result composes with
`cosPi` and `sinPi` without a `pi` appearing in a document. There is no `pi` in the
grammar and there should not be — half turns exist precisely so that a quarter turn
is exact.

This is now the fourth device to find a gap one level down (`log` for the Kingdon
trap, trigonometry for the multipole guide, a parametric `drivePhase` for the
travelling wave, `asinPi` here). LIB-1 says to believe that signal, and each time it
has been a **function**, never an abstraction.

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
hold-converged edge is q_z = 0.85 at a 0.1 mm launch, 0.82 at 0.3 mm and 0.64 at
0.6 mm. The Mathieu equation is linear, so an ideal trap's boundary *cannot* depend
on how far off centre the ion started — a trajectory scaled by a constant is another
trajectory. A real one's does, and the dependence is not small.

**The reason is structural, and the fix is to stop measuring it that way.** A
measurement that registers a loss only when the ion **reaches** an electrode requires
it to cross the whole anharmonic region, so it is never a small-amplitude measurement
whatever it was launched at. The *linear* boundary is a statement about a frequency,
and §12's own secular-frequency spectrum measures it without the ion going anywhere:
calibrating β(V) against Mathieu over a range where the ion stays small fits to a
worst residual of **1.2e-3**, gives an effective radius of **3.8137 mm** against
**3.8195** from the field's curvature with no ion involved, and puts β = 1 at
q_nominal = 0.8254 — **bracketed** by the two ejection edges rather than equal to
either.

**Recommend §12 distinguish the two**, because they are different quantities and only
one of them is the design parameter: a *linear* stability boundary, which is where β
reaches one and is a property of the field; and an *ejection* threshold, which is where
a particular ion launched a particular way leaves within a particular hold, and is what
an instrument actually does.

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

### 21 · A resonance can be found and not named, and §12 has no vocabulary for one

The Paul trap of Amendment 19 loses its ion in a narrow band at q_z = 0.739–0.750,
sixty volts inside the stable region. §12's Class B figures can establish that the
band is there — a scan over amplitude, with controls at twice the mesh, twice the
hold, a quarter of the hold and a third of the launch offset — and can say nothing
about **what** it is, because a nonlinear resonance is defined by a condition on
*frequencies*: `n_z β_z + n_r β_r = 2` for a multipole of order `n_z + n_r`.

§12 does list "secular frequency spectrum" as a Class B figure, and it is now built.
Lomb–Scargle rather than a DFT, because a trajectory is sampled at accepted
integration steps and is therefore not uniform, and resampling it first would be
inventing values the integrator never computed. Checked against Mathieu's
characteristic exponent to **0.007–0.144 per cent** across q = 0.1 to 0.85, with both
micromotion sidebands where theory puts them.

With it, the band is the **octupole**: β_z = 0.6769 and β_r = 0.3225 give
`2β_z + 2β_r = 1.9989`, an order-four condition met to 0.055 per cent and a hundred
times worse either side. Order four is the leading multipole this geometry's symmetry
permits, so it was predicted before it was fitted.

**Recommend §12 name the resonance condition as a reportable quantity**, not merely
the spectrum it is computed from. The spectrum is a diagnostic a human reads; the
condition is a *number* — which order, met how closely — and it is what a tolerance
study or an optimiser would have to be pointed at to avoid one. Nothing in the
current register asks for it, and the trap needed it on its first real use.

A second thing that fell out. **The ideal-Mathieu prediction was wrong and had to
be**: β at the *nominal* q is 0.6156, which satisfies no low-order condition at all.
The measured 0.6769 differs because the effective radius is 3.82 mm rather than the
declared 4.00. That effective radius is now confirmed two independent ways — from
the field's curvature at the centre with no ion involved, and from the secular line
of a flown ion against the closed form — agreeing to **0.02 per cent** at low q, and
diverging at high q exactly as the anharmonicity says it must.

### 22 · Two scales in a Class B measurement that must be derived, not chosen

§12 asks for isolation efficiency against notch width and says nothing about how the
excitation is set up. Two of its scales are not free parameters, and getting either
wrong produces a plausible table rather than a failure.

**The comb spacing must equal `1/T`.** A resonance excited for a time `T` has a width
of about `1/T`, so a comb spaced more widely has *holes* — an ion between two lines
is driven by neither and survives an excitation meant to eject it. A first version
used 5 kHz against a 333 Hz width, and the notch width then toggled every ion at
once, because selectivity had nothing to do with it.

**The amplitude follows from the aperture and the duration.** Resonant growth is
linear, `x(t) = (qE/m)t/2ω`, so reaching the aperture `a` in a time `T` needs
`E = 2amω/qT`. A first version used four orders too much and ejected every ion at
every notch width. An amplitude picked to make a demonstration work is a
demonstration of the amplitude.

**Recommend §12 state both**, since the figure is not well posed without them: an
isolation efficiency is a function of notch width *at a stated excitation amplitude
and duration*, and the comb must resolve the excitation's own linewidth.

A third thing the measurement showed, worth recording because it is not obvious: the
trade only has two arms at sufficient amplitude. At the amplitude that just ejects a
resonant ion the narrow end is free and efficiency is monotone in width; at three
times that the narrow end loses the target and an **interior optimum** appears. A
study run at one amplitude would report the wrong shape and be internally consistent.

### 23 · A time-varying field reached through a time-free interface answers anyway

Found while building the above. `SuperposedField` implements only
`IElectrostaticField`, and a driven member answers that interface with its value at
`t = 0` — so summing a driven element with any other silently produced **a snapshot
of the RF at the top of its cycle**, presented as the instrument. No exception, no
NaN, nothing in the result to distinguish it.

That is the third time this exact shape has appeared: the diffusive mode stepping a
density through a t = 0 snapshot, `einzel solve` reporting the DC pattern for a
driven geometry, and now this. **The class is: a time-varying quantity reached
through a time-free interface does not fail, it answers at an arbitrary instant.**

Fixed structurally rather than by a check — `FieldAssembly` now chooses
`DrivenSuperposedField` when any member is driven, so the composition is decided by
what it contains rather than by what the caller asks for.

**Recommend §10 or §11 state the rule**: any composition of fields must preserve the
strongest interface its members implement, and a static interface over a driven
member is a defect rather than a lossy convenience.

### 25 · The shell is a deliverable, and r06 does not say why the toolchain suits it

**r06 names WPF, Helix Toolkit and a DirectX 11 viewport, and never says why C# was
chosen.** The reason is not incidental: **the Windows GUI capability was part of the
language decision.** An unrecorded rationale is one a later decision cannot respect,
and the question will be asked.

**Windows-only is the decision, not an accident of WPF.** Avalonia was considered and
not chosen, because the shell is not planned for use outside Windows. If that need
appears it gets revisited then — a decision deferred rather than foreclosed, and worth
recording as deferred so that nobody later reads "WPF" as a constraint somebody failed
to notice.

**What makes deferring it cheap is architectural rather than optimistic**, which is the
part that has to stay true for the position to hold:

- **Invariant 1** — no UI type below the shell. Every assembly above `Einzel.Wpf`
  builds and runs on Linux, and CI runs there from the first commit. `Einzel.Render`
  produces a publication figure headlessly with no display, no window manager and no
  font server (RND-1).
- **AGT-2 as strengthened below** — every shell action is expressible as a CLI
  invocation. A capability cannot accumulate in the window that has nowhere else to
  live.

Together those make a later cross-platform shell a **replacement of a presentation
layer**, not a rewrite. **Windows-only applies to the shell and to nothing else** — and
that is the misreading to guard against, because "the GUI is Windows-only" and "the
project is Windows-only" are one word apart and the second would quietly undo the
Linux CI that keeps the first one cheap.

**And the shell is wanted, not merely permitted.** This document's own §16 section
previously closed by quoting §22's scope-creep risk and concluding that a shell should
be built "when a *human* workflow is blocked without it, not when the feature list
looks incomplete." That is a sequencing rule dressed as a scoping rule, and it reads
as though the GUI were a hazard. **Interactive geometry, fields drawn over that
geometry, and animation are intended outcomes.** §22's risk is a thing to manage —
the guard is UI-1's prohibition, not deferral.

**The thesis is the pair, and neither half is the product.** An agent must be able to
drive the entire design process; a human must be able to see and manipulate the same
design interactively. The reason those are one requirement rather than two is AGT-2.

**AGT-2 is right and right for a reason r06 does not give.** r06 says every capability
reachable from the window is reachable from the CLI *through the same command objects*,
and heads the diagram **SURFACES · PEERS, NOT A STACK**. That is the correct
architecture — a shell that shells out cannot drive an interactive viewport at frame
rate, and a hundred milliseconds of process start per slider drag is not a shell. But
it leaves the guarantee resting on discipline, and an invariant checked against one
surface is one that has already been broken by the time anyone notices.

**Recommend AGT-2 additionally require that every shell action be *expressible* as a
CLI invocation, and be journalled as one.** The shell keeps driving command objects
in-process; what changes is that the journal is a list of commands somebody could run.
Three things fall out of it that discipline alone does not give:

- **A capability that cannot be written as a command cannot be added to the window**,
  which is AGT-2 enforced by construction rather than by review.
- **A human's session hands over to an agent**, and an agent's to a human, because the
  journal is the same vocabulary both use. That is the whole point of the pairing and
  it does not work if the window's actions are anonymous.
- **PRJ-3's manifest and the journal converge.** A run is already regenerable from its
  manifest; this makes an interactive *session* regenerable the same way.

The cost is real and worth stating: two representations of every action, and a
temptation to let the in-process path acquire an argument the command form has no
spelling for. That is the specific thing to review when the shell is written, and it is
the same failure mode as the validation one below.

### 24 · "One drive per solve" was a design decision, and two devices refuted it

`CompiledDrive` carried this note from the beginning:

> One drive per solve, not one per electrode. A real instrument has a generator and
> electrodes tapped off it at various amplitudes and phases, and modelling it the
> other way round would let a document declare **two frequencies on one structure —
> which is a different instrument and almost always a mistake.**

It is not a mistake. It is what a trap is. A real travelling-wave guide superposes a
fast confining RF on a slow travelling wave; a trap performing a stored-waveform
isolation runs a low-frequency notched comb across its endcaps while the ring carries
the main drive. The rule cost the shipped travelling-wave guide its radial
confinement, and made the notch-width measurement of Amendment 22 run on an analytic
quadrupole rather than on a solved geometry.

**It cost nothing in the solver to remove**, which is the part worth recording. Basis
superposition is indifferent to what the weights are functions of, so two generators
reaching the same electrodes in the same proportions are one solved pattern carrying
two weights on two clocks — exactly as a DC supply and an RF supply already were.
Measured: 24 rings each tapping two generators reduce to **3 basis solves**, two for
the wave's phase ramp and one for the alternating confinement.

What it does change is step control, and §11 should say so: **a field with several
timescales caps its step by the fastest.** The guide's wave repeats at 0.5 MHz and its
confinement at 3 MHz, and the assembled field reports 333 ns.

**Recommend §9 state that an electrode's excitation is a list of taps**, and that the
number of generators a geometry carries is a property of the instrument rather than a
modelling convenience to be minimised.

**And a negative result worth as much as the capability.** Giving the guide its
confinement did not widen its acceptance at any amplitude tried — 5 of 12 entry radii
arrive with none, 2 at 100 V, 4 at 200 V, 3 at 400 V, 1 at 800 V. The window is narrow
at both ends: above about 200 V on this ring pitch the confining drive's own Mathieu q
passes the stability limit and the ion is *ejected*, and below it the well is shallow
against a 60 V wave. The template ships with the confinement at zero, because shipping
a default that makes a device worse would be worse than shipping none. What the tests
assert is that the generator **reaches** the ion — the acceptance differs with it on —
which is the claim the capability supports.

### 36 - A geometric perturbation can sit below the discretisation floor, and then it must be constructed rather than solved

Amendment 5 records that FLD-1's linearity spike failed because a sub-cell perturbation
was *invisible* to a rasterised boundary, and that cut cells fixed it. **The Astral's
mirror convergence is the same subject one level deeper, and cut cells do not fix it.**

Two mirrors converging by a 200 micron spacer tilt by `alpha` = 2.9e-4. The entire physical
effect is the field anisotropy `Ez/Ex = tan(alpha)`, so the quantity to be resolved is 2.9e-4
of the main field. A second-order solve on a 2.5 mm cell across a 40 mm gap carries roughly
`(h/L)^2` = 0.4% of field error - **fourteen times the signal**. Measured against the closed
form, the solved tilted geometry returned **3.54, 0.011 and -0.57** of the true drift
deceleration depending only on how wide the vacuum gaps between mirror strips were, and 1.52
at half the cell size. Nothing was converging.

**Two distinct failures, and only the first is Amendment 5's.** Abutting electrodes have a
boundary kind cut cells cannot represent at all: a metal-to-metal edge has Dirichlet nodes on
both sides, so there is no vacuum node to store a sub-cell crossing on, and the edge is
rasterised at node resolution. Putting a vacuum gap between the strips makes every face
metal-to-vacuum and restores the cut cell - and the answer is *still* wrong, because of the
floor above. The tilt ladder that verified proportionality "down to a thousandth of a cell"
was run on parallel plates, whose tilted faces are metal-to-vacuum, and was correct about
them.

**The fix is not a finer mesh, and no affordable mesh would do.** Reaching 2.9e-4 of field
accuracy needs `h/L` near 1e-2 in three directions at once. What works is to stop
differencing for the effect and construct it: solve the two-dimensional cross-section, which
is exact in its plane, and **rotate the solved field**. Rotations commute with the Laplacian,
so the rotated field is the exact solution for the rotated geometry, and the anisotropy comes
out of the coordinate transform. Measured at **5.4e-20** in the field and 1.7e-18 through a
model document, against factors of several before. A converging pair is then two
cross-sections rotated oppositely, each carrying one mirror at potential with the other
grounded, which is the ordinary basis decomposition and therefore exact rather than
approximate.

**A shear is the obvious spelling and is wrong twice over.** Laplace is not shear-invariant,
so a sheared field solves nothing; and a shear of a mirror pair translates both mirrors the
same way, which is a rigid translation of the instrument rather than a convergence.

**Recommend section 10 state the corollary to Amendment 5's requirement.** Sensitivity
fields need a boundary representation that varies continuously with the parameter - and
separately, **the perturbation's own signal must exceed the discretisation error of the solve
that will report it.** Where the perturbation is a rigid motion, applying it as a coordinate
transform on the solved field satisfies both conditions exactly and costs one wrapper class.
The general rule, in `docs/lessons.md`: compare the size of a small effect against the
solve's error as a *ratio* before reaching for a finer mesh, and treat a quantity that
changes sign under an irrelevant modelling choice as a measurement of that choice.

A28-style corollary for the record: this also makes each Astral mirror a **two-dimensional**
solve, so the device that motivated the volume solver no longer needs it for its mirrors, and
a solve plus a 15 microsecond flight went from about 26 seconds to 1.4.

### 35 - A volume solve could not declare a face a mirror, and the solver always could

**r06 §9's boundary conditions** are stated for a solve without distinguishing the plane and
volume paths, and the plane path has carried `rightEdge` from the beginning. The volume path
carried nothing: `DirichletMask3D` has all six faces as settable conditions and
`OperatorStencil3D` honours them, and **no document could ask for one**. The capability
existed and was unreachable - the same shape as `ITransportMode` named only in a csproj
description, and `drivePhase` a plain double until a travelling wave needed a ramp.

**A grounded domain boundary is a third electrode**, which this project has already
documented once. A stripe electrode running the length of an analyser's drift makes the field
independent of the drift direction, so grounding those faces imposes an axial field the real
instrument does not have. Measured on two rails spanning their domain in z: **-62,577 V/m of
axial field with the faces grounded, -0.0000 with them mirrored**, and with mirrors the
transverse field is identical to the digit at 5 mm and 20 mm from the face.

**Found by an ion going the wrong way**, not by a failing test: an Astral skeleton at a 3.5
per cent injection angle should drift at +1375 m/s and measured **-480**.

**Recommend r06 say the boundary vocabulary is the same in both dimensions**, since the
argument for it is the same and the omission was an accident of which path was written first.

### 34 - Every 3-D primitive is axis-aligned, and one real device is defined by not being

**r06 §9's shape vocabulary for a volume solve** is a box, a sphere and a cylinder along an
axis. The type's own remarks say why that is enough - "between them they build the devices
the specification's table asks for" - and add the escape clause, "a device that needs a
fourth is a fair reason to add one".

**The device that needs something is the asymmetric-track analyser, and it does not need a
fourth shape.** Its two ion mirrors converge by a couple of hundred microns over a third of
a metre, and that convergence is the **mechanism** rather than a tolerance: it is what makes
the drift decelerate and reverse, which is the whole behaviour the geometry exists for.
Every primitive being axis-aligned means a plate deliberately not parallel to another is not
expressible at any resolution - so such a model would validate, solve, converge, and produce
a drift that never reverses.

**What it needed was an attribute, not an abstraction**, which is the fifth time here: `log`
for the Kingdon trap, trigonometry for the multipole guide, `asinPi` for the C-trap, a
parametric `drivePhase` for the travelling wave, and now a tilt on a box. LIB-1 says to
believe the signal when a device forces a change below `Einzel.Library`; the signal each
time has been narrow and the abstraction has held.

**A tilted box is a box**, so signed distance and first entry rotate the query into the box's
own frame and run unchanged - a rotation is rigid, so a distance measured there is the
distance in the world, and affine, so a segment fraction is preserved exactly. The only
query that genuinely changes is the bounding box.

**Half turns, so a right angle is 0.5 and is exact.** The convention `cosPi` and a drive
phase already use, for the reason this project has now met three times: `Math.Cos(Math.PI/2)`
is 6.1e-17, so a nominally upright plate would be tilted by a rounding and a symmetric
geometry would carry an asymmetry made of floating point.

**Measured: the response is proportional to a thousandth of a cell.** On a 0.5 mm mesh, step
ratios of 2.0000 and 2.5000 for steps of 2 and 2.5 over a two-hundred-fold range, against a
parallel control reporting 7.1e-15 V. That is the cut cell doing what it was built for -
FLD-1's argument, met in a new place.

**And it found a degeneracy worth recording**: a conductor face lying *exactly* on the node
lattice makes the response affine rather than proportional, with an offset worth about
seventeen microns of convergence. A quarter-cell offset removes it entirely. The mechanism
is recorded as a hypothesis; the measurement and the cure are established. **Recommend the
model format's guidance say so** - do not place a conductor face exactly on a cell boundary
when the quantity of interest is a small geometric perturbation.

### 33 - GRD-8's estimate is about a machine and an operation, and was about neither

**r06 §GRD-8**: *"Any operation exceeding a configurable cost threshold requires a prior
estimate."* The requirement is right and the implementation answered a different question
twice over.

**It costed a model, and the operation is a study.** `einzel estimate` took a model file
and reported what one run of it costs. But nobody plans a multi-day job around one run -
they plan around a scan, a sweep, an optimisation or a boundary search, which is where the
hundreds of evaluations are. The gate was therefore short by the evaluation count, and
short *silently*, which is the worst direction for a number whose entire purpose is to
decide whether to start.

**The multiplier needs no pilot**, which is what makes this a defect rather than a
limitation: a study file **states its own extent** - a scan its points, a sweep its draws,
an optimiser and a bisection their evaluation ceilings. Costing it is arithmetic over
numbers already in the document.

**And the arithmetic is not the obvious one.** An evaluation solves the field once and
flies every ensemble member through it, so a study costs
`evaluations x (solve + members x flight)`. Multiplying the model's own total by the
evaluation count charges a whole solve per member - measured at **4.8x over** on the
shipped mirror pair at nine members.

**Second: an absolute time is a statement about a machine, which is Amendment 27 again.**
The rate was one hardcoded constant - 13 s per million nodes, measured on the 2-D
templates on one developer's box - and it was applied to volume solves too, where the
stencil is 27-point rather than five and the coarse levels are Galerkin. On the shipped
C-trap that put a 5.9 s solve at **1.81 s**. It is now measured by solving a coarsened
copy of *the model's own geometry*, which also captures the difference between a
boundary-value problem and one with interior electrodes.

**So GRD-8's estimate is not free, and r06's framing implies it is.** The requirement gates
on a number "available without doing the work", and a number that is *useful* on unseen
hardware cannot be: it costs one coarsened solve and one short flight. **Recommend GRD-8 be
restated** as requiring an estimate whose cost is bounded and stated, rather than one that
does no work - with the opt-out that keeps PERF-8's cold-start budget reachable
(`--no-calibrate`, which says in the basis line that nothing was measured here).

**A study that varies the geometry varies its own cost**, so the flight is sampled at the
ends of the study's own declared range and averaged - measured at **2.2x** across a mirror
separation scan, which an estimate taken at the declared values alone missed by 0.57x. The
sampling is gated on the study being long enough to absorb it: three samples cost about one
and a half evaluations, which is 7 per cent of twenty and under one per cent of the hundreds
a real optimisation declares.

**Sampling the fraction is what makes it wrong**, and getting that wrong first is worth
recording. A pilot flies part of a flight and scales up - and the only length available to
scale against is the declared *maximum* flight time, which is a ceiling rather than an
expectation. The nominal ion arrived inside the fraction and the extremes did not, so they
were scaled by the whole ceiling and the estimate came out **3.4x over**. A study samples
few points against many evaluations, so a sample can afford the whole flight at the real
cell size, and then nothing is extrapolated.

**Third: the mesh a solve gets is not the one the document asked for, and nothing said so.**
Each axis rounds its interval count up to a power of two, so cost is a **step function** of
the cell size and the node count is the product of three such roundings. On a
635 x 48 x 350 mm analyser a requested 1 mm gives 0.62 x 0.75 x 0.68 mm and **34.2 M nodes
where the request implies 10.7 M** - finer than asked on every axis, never coarser, and
**asking for 1.5 mm instead costs 7.9x less**. That is not waste and nothing about it is
wrong; it is a fact about the cost of a plan, and GRD-8's estimate is where it belongs.

**Fourth, and found by reviewing the above: an evaluation is not always an ensemble.** The
arithmetic assumes `members` independent ions through one solved field, which is the ordinary
case and not the only one. A **diffusive** run steps a density and a **space-charge** run
advances the whole packet in lockstep - in both, what the model estimate already covers *is*
one evaluation, flights included. Multiplying by the ion count double-counts, and for a
diffusive model it charges for trajectories that mode does not produce (TRN-2, RND-8) while
flying pilot ions through a model that has none.

**What remains excluded is stated on every estimate**: process start and just-in-time
compilation, being fixed costs that matter for a thirty-second scan and not for a multi-day
one. Measured on a mirror-separation scan - the adversarial case, since it sweeps across a focusing condition and evaluation cost varies 2.2x along it: **23.1 s estimated against 30.3 s of wall clock (0.76x), of which about 5 s is process start the estimate excludes by design** - so 0.89x of the computation itself. Run-to-run spread is 1 per cent.

### 32 - An exact analytic field cannot be one element of a beamline

Section 9 lists the field kinds a document may declare, and section 6's whole architecture
rests on superposition being exact for electrostatics - which it is. What neither says is
that an **analytic** element has no extent: it fills all space, because a formula does.

That is harmless while analytic fields are idealisations of a whole instrument - a uniform
field, a retarding half-space. It stops being harmless the moment one is an exact statement
of a real device that sits *next to* another device. The quadro-logarithmic field of an
orbital trap grows as `z^2`, so declaring it in the same document as the C-trap that
injects it puts an enormous field across the C-trap. The two instruments cannot be composed
even though the sequencer can express the handover and superposition is exact.

**Two solved elements compose correctly**, because each is bounded by its own domain and
decays outside it. So the gap is specific: an exact analytic field cannot be one element of
a multi-element beamline, and the exactness is precisely why anyone would want it there.

**And the obvious escape does not work, which is what makes this an amendment rather than a
task.** The natural answer is "declare the analyser as solved geometry instead, and let its
domain bound it". An orbital trap's electrodes are surfaces of revolution, and an
axisymmetric solve is exactly the tool - but the electrodes **are equipotentials of the very
field they produce**, so their profile satisfies

    -r^2 / 2 + Rm^2 ln(r / Rm) = A - z^2

which is transcendental in r: an `r^2` and a `ln r` in the same equation, invertible only
through Lambert W. The expression grammar has `sqrt` and `log` and no way to invert that,
and the 2-D shape vocabulary is rectangle, disc and edge profile - none of which is a curve
a document can name. So the analyser cannot be declared as geometry either.

The two facts together are the finding: **an exact orbital analyser can be modelled alone
and cannot be modelled beside anything.** Its field is unbounded and its geometry is
inexpressible, and those are the only two ways the format has of placing a device in a
document.

**Recommend section 9 give an analytic element an optional region** - a box outside which
it contributes nothing. That introduces a field discontinuity at the boundary, which is not
a difficulty: the integrator already lands exactly on declared discontinuities, and section
11 makes that a first-class event. What it needs is a decision about whether the region is
declared or inferred, and what happens where two regions overlap.

**Built, with a stated limitation.** An analytic element may declare a `region` — a box
outside which it contributes nothing — and two instruments now share a document. Measured:
an ordinary 1 kV/m accelerating section 75 mm from an orbital analyser feels **−1,499,000
V/m** of that analyser unbounded and **exactly its own 1,000** bounded; on the axis the
unbounded case is worse than swamping, since the model cannot be asked a question there at
all. Inside the region nothing changes, to the bit.

**The potential steps at the boundary, and that costs less than it looks like it should.**
A box is not an equipotential, so the potential does not match across it — and the first
account of this concluded that an ion crossing therefore gains or loses that energy. **It
does not.** An ion is moved by the *field*, which is exactly the declared one on each side,
so a bounded uniform field is an accelerating gap followed by a field-free drift: measured
at **13.658582 µs against a closed form of 13.658582**, with the unbounded control at
10.180506. What the step actually costs is that the energy-drift diagnostic jumps at the
boundary, and that the piecewise field is not conservative across it — an ion crossing more
than once, in by one face and out by another, can gain energy no electrode supplied. The
step is reported on every bounded element at severity `Qualified`, in volts and as a
fraction of the beam potential.

**The failure points straight at the next refinement.** A real device's field is bounded by
a conductor, and a conductor is an equipotential of the very field it produces. Bounding an
analytic element by one of its own level sets rather than by a box would make the potential
continuous by construction — offset so it is zero outside, with the field discontinuous
exactly as `halfSpaceUniform` already is, and the geometry exactly a real electrode. That
is what should replace the box, and it was not built here.

### 31 - A thermal cloud's divergence is not the divergence an ion-optics beam has

The model format carries `energyFractionSpread` with an argument written where it is
taken: it "varies the energy without varying the direction, which a temperature cannot
express". The mirror of that - varying the **direction** without varying the energy - was
deliberately left out, on the grounds that "a thermal cloud already has one, and offering
both lets a document say two things about the same physics".

**That reasoning is right for a source and wrong for a beam.** An ion born warm and then
accelerated does get its divergence from its temperature, in a fixed ratio. A beam defined
downstream by an **aperture** does not, and an einzel lens exists precisely to re-image
such a beam. Every ion-optics description in the wild specifies one that way: *"50 eV with
a 20 degree angular spread"* is a sentence the format could not represent.

**And a temperature cannot stand in for it**, which is what makes this a gap rather than a
verbosity. Matched to give the same divergence at 50 eV, a temperature spreads the energy
by **43%** - turning a 50 +/- 0 eV beam into a 50 +/- 15 eV one, which is a different
study's independent variable. Divergence and energy spread are separable in the instrument
and were not separable in the format.

**Recommend the format carry both, with the interaction stated rather than prevented.**
Schema 0.7 adds `divergence`: a cone half-angle, drawn **uniformly in solid angle**, not a
Gaussian - an aperture truncates rather than weights, and the rays pile near the rim where
the aberration lives. Measured against the closed form, mean cos(theta) comes out
0.969832 against 0.969846 for uniform solid angle and 0.979816 for uniform polar angle: 700
times closer to the right one. A tilt costs no energy exactly (4.5e-13 m/s on 4000), since
rotating a vector does not lengthen it.

**The third design decision this week that was right for its case and wrong for a new
one**, after `SessionJournal` refusing invalid edits and `CompiledDrive` allowing one drive
per solve. The pattern is worth naming: a constraint argued from the cases in front of you
is sound and is not a law. When a new requirement meets one, re-read the argument rather
than the rule - it is usually still true, and usually no longer sufficient.

### 30 - A manifest said what a result was made of and not what it was about

`PRJ-3` requires a run manifest to fully determine its run, and lists what that takes:
model hash, seeds, engine version, solver-behaviour version, transport mode, compute path,
extension identities, machine. **It does not ask for the model's path, and by that list it
does not need to** - the hash is what makes a result regenerable, and it survives a rename
where a path does not.

**Determining a run and identifying which model a result is about are different
questions.** With only the hash recorded, `einzel verify` had to answer the second by
searching for a file whose content still hashed to the recorded value. Two models may
legitimately hold the same content, and then:

- The result attaches to whichever file is found first, which is arbitrary.
- **Editing the model that was actually run makes its drift disappear.** The result
  silently re-attaches to the untouched twin, reports itself current, and the edited model
  reads as never run.

That second one is a stale result reporting as fresh, which is the failure direction the
whole verify mechanism exists to prevent. It is not exotic to reach: a project scaffolded
by `einzel init` and then given a corpus example of the same device is enough, which is
exactly how it was found.

**Recommend PRJ-3 distinguish the two.** A manifest carries what determines the run *and*
what identifies its subject; the hash cannot serve as both, because content is not
identity. `RunManifest.ModelPath` is now recorded, verify prefers it, and the hash search
remains the fallback for older manifests and for a model that has moved - where what is
reported is what can be observed (*the recorded path is gone, the same content is at X*)
rather than a rename, since content alone cannot tell a rename from a twin that was there
all along. Writing "renamed" there was the same mistake once more, in the fix; my own test
caught it.

The general form is worth more than the fix: **when one field is made to answer two
questions, the answers diverge exactly where the two questions differ** - here, wherever
two files hold the same bytes.

### 29 - Enumerating a requirement's own population is a different act from believing it

**GRD-2 names seven layers** - engine, command layer, CLI output, MCP response, exported
file, rendered figure, video. That is a finite, closed list, which makes the requirement
checkable in a way most are not: there is no judgement about coverage to be made, only
seven questions to ask.

**Asking them found two defects on the first pass**, and the register had been claiming
**Met** for both.

**The exported `.vtu` carried no warnings.** `VtuWriter.WriteTrajectory` and
`WriteDensityField` both take an optional `provenance` list; the density path had always
appended the run's warnings to it and the trajectory path had never done so. Same writer,
same parameter, one call site using it. This is the **seventh** time evidence about a
computation has been dropped at a seam here, and the sixth was the same file. A `.vtu` is
the artifact that travels furthest - opened in ParaView, months later, by someone who
never saw the envelope it came from.

**The rendered figure was worse, and it was not a dropped warning.** Asking whether a
warning reached the figure surfaced that the figure was not computing the same thing a
run computes: `SectionRenderer` and `AnimationRenderer` both integrated through
`TrajectoryIntegrator.Integrate`'s optional `collisions` parameter without supplying one,
so **a figure of a model declaring a gas drew the vacuum flight**. On the `thermalisation`
example the run has the ion reach **154.79 mm** and the drawn one reaches **2778.28 mm** -
eighteen times further, silently, on the artifact RND-11 exists to keep honest. The two
figures were byte-identical with and without the gas.

**The general shape, now seen three times with this one quantity**: an optional parameter
whose default is *a different physics* rather than an absence. Forgetting it produces a
plausible answer instead of a failure. The gas had already been found reaching the
figure-of-merit path and not the run path, and the regime inspector's own first draft.

**What the enumeration is worth is not the two fixes.** Both were in code that had been
reviewed, tested and documented as working. What found them was asking a question whose
answer had to be yes or no *per named layer*, rather than asking whether warnings
propagate - which is a question the whole system answers "mostly, yes" to, correctly, and
which conceals exactly the cases that matter.

### 28 - GRD-1's own exception was load-bearing, and nobody had counted what it cost

**r06's GRD-1 is absolute**: every quantitative result carries value, units, uncertainty,
ensemble size or convergence measure, and active warnings, and *the API offers no way to
obtain the scalar alone*. The absolutism is argued - a convenience accessor returning the
value would get added by someone and then used everywhere.

**The exception is also argued, and correct.** A sweep or an optimiser needs an ordering,
and an envelope has none, so `FiguresOfMerit.Evaluator` hands a driver a bare double. That
is written down where it is taken.

**What nobody had written down is the consequence.** Most figures were *only* reachable
through the exception: of the fourteen this build computes, **one** - the flight time, from
a convergence study - carried an envelope anywhere. There was no way to ask for a
turn-around time with an uncertainty on it, through the CLI, through MCP or at all. The
exception had quietly become the rule for everything except the one figure that predated
it.

**Why it went unnoticed is the instructive part.** Every individual number this project
publishes *does* carry an envelope, because they are computed by tests and studies that
build one by hand - the docs are full of "1.0127 +/- 0.0373" and "0.49% on 4000 ions". The
gap was in the *API*, and it took building the view whose whole purpose is displaying
envelopes to make it visible. A requirement can be honoured everywhere it is exercised and
still be unmet where nothing has asked.

**Recommend GRD-1 say that a figure of merit must be obtainable in enveloped form**, with
the ranking accessor an explicitly derived view of it rather than the only path. That is
the shape that makes the exception safe: the envelope exists and ranking discards it,
rather than the bare number existing and an envelope being added where somebody remembered.

**Closing it needed a mechanism rather than thirteen formulas.** Most of these figures are
statistics of an ion cloud and only the fractions have a closed-form error; a full width at
half maximum has none, and an arrival-time peak is measurably skew, so a Gaussian formula
would understate its own error. Resampling covers a width, a mean and a ratio alike and
assumes nothing about the distribution. **Two limitations came with it and are recorded in
`docs/lessons.md`**: the bootstrap is inconsistent for extreme-order statistics, which
matters because "the widest entry radius that still arrives" is one; and it must not be
applied to the deterministic energy sweep, which is a designed scan rather than a draw and
has no sampling uncertainty to report.

### 27 - PERF-7's 50 ms is the cost of starting CPython, not a budget the platform can spend

**r06 §PERF-7 puts a sandboxed extension round trip under 50 ms**, and makes that number
the granularity floor for EXT-4. The requirement is sound in intent and is not measurable
as written, because on an ordinary machine **the 50 ms is process start**.

Measured: launching the interpreter with `-I -c pass` and doing nothing costs
45.0, 49.6, 53.9, 58.2, 40, 51 and 63 ms across seven runs. The budget straddles that spread. So a test asserting
the round trip against 50 ms is asserting that CPython started quickly this time - and it
behaves accordingly: the old assertion **passed and failed on the same commit in two CI
runs minutes apart**, and on a shared build agent a bare launch takes *seconds*.

**What the platform actually controls is small and now measured.** A round trip costs
**1.08x to 1.52x** a bare interpreter launch across seven runs, and on one of them came in
*below* it - the marshalling, the schema check and the JSON on both sides are under the
noise floor of process start.

**Recommend PERF-7 be restated as a bound on the platform's own share** - the round trip
must not cost materially more than starting the interpreter - with the absolute figure
reported rather than asserted. That is scale-free, holds on a developer's machine and a
build agent alike, and measures the thing this project can change.

**Two things follow, and the first is the more important.** EXT-4 is *strengthened*: a
subprocess cannot be invoked per integration step because the boundary costs ~50 ms and
nothing here can reduce it, which is a structural argument rather than a measured
coincidence. And the open decision "whether the in-process runner is worth shipping"
gains its first real evidence: **the in-process runner is the only thing that could meet
PERF-7 as written**, since it removes the term that dominates. That is not an argument to
build it - nothing has yet needed a faster round trip - but the decision should no longer
be recorded as "the evidence says sandboxed-only is sufficient" without saying what
sandboxed-only costs.

**And a note on how this was found**, because it is the kind of thing a green suite hides:
the test had been passing for weeks with the comment *"a hard assertion here would be a
test of the build agent"* written directly above an assertion on the build agent. The
comment was right and the code did not follow it.

### 26 · Helix Toolkit is still the right choice, and its DirectX backend is archived

**r06 §16 names "Helix Toolkit on its DirectX 11 path" and gives the right reason** —
plain WPF Media3D cannot render 10⁴ trajectories interactively. §20 asks for third-party
status to be re-checked before being committed to rather than assumed, and doing that
found two things r06 could not have known:

- **The 2.x line contemporaneous with r06 is .NET Framework-only.** 2.27.3 restores on
  net10 only through the NU1701 compatibility shim. **3.1.2** has real `net8.0-windows`
  targets and restores clean with no fallback, so the version matters and the name alone
  is not enough.
- **Every Helix DirectX package depends on SharpDX, archived since December 2020.** The
  named path rests on an unmaintained project, and r06 records it as a plain choice.

**It is taken anyway, and the reason is §17's own boundary.** That section is emphatic
that the interactive viewport is *screen tuning, not an artifact*: the publication figure
comes from `Einzel.Render`, which is vector, headless and owes nothing to this dependency.
So the archived library is confined to a window, and its failure mode is that the window
stops working — not that a figure cannot be produced. Nothing that leaves Einzel passes
through it. LIC-1 is verified rather than assumed: MIT from the embedded licence file, and
the transitive closure is MIT throughout.

**Recommend §16 record the version and the SharpDX position**, so that a later reader
finds a decision taken with open eyes rather than a name that has quietly stopped meaning
what it did. The exit, if it is ever needed, is bounded by the same boundary: replacing a
viewport backend touches the shell and nothing else.

**A second, smaller finding in the same place: WPF cannot run in globalization-invariant
mode.** `Directory.Build.props` sets `InvariantGlobalization` for the whole solution, for
CLI-5's deterministic output; WPF's font cache constructs `new CultureInfo("en")` while
measuring the first line of text and the window dies before it is shown. The shell
reverses the setting, and what it was protecting is unaffected — this codebase achieves
locale-independence by passing `CultureInfo.InvariantCulture` explicitly at every
formatting and parsing site, and the build flag was the belt to those braces. The parse is
the one that matters: a value typed into the model tree is read invariantly whatever the
host locale, because the file being edited is invariant.

---

## The shell, and the rest of §16

**The register is tag-driven, and §16 carries one tag between eleven required
views.** That is a property of the original document rather than of the shell's
importance, and reading the register alone would leave the impression that the
GUI is a footnote. It is not: §16 is a section, §17 is written largely around what
the shell must *not* own, and AGT-2 — the invariant that nothing exists only in
the window — is the load-bearing claim that the shell is a peer rather than the
product.

**It is a named deliverable, and the toolchain was chosen partly for it.** The
Windows GUI capability was part of the reason for C#; r06 names WPF and Helix Toolkit
on DirectX 11 and does not record that rationale, which Amendment 25 adds. What is
wanted is **interactive geometry, the solved field drawn over it, and animation** —
outcomes, not contingencies.

**The thesis is the pair.** An agent drives the entire design process through the CLI
and MCP; a human sees and manipulates the same design in a window. Those are one
requirement rather than two because of AGT-2, and Amendment 25 strengthens it: every
shell action should be *expressible* as a CLI invocation and journalled as one, so a
human's session hands over to an agent and back in the same vocabulary.

**Six of the eleven views exist**, and the window opens on a model: `einzel-shell
models/reflectron.json` gives a parameter tree, a trajectory bundle coloured by energy over
the drawn instrument and field, the journal, results grouped by §12's accuracy class, the
regime along the path, and the declared timeline. `ShellSession` holds one model, the shared journal, and every
action recorded as the `einzel` command that would reproduce it (Amendment 25).

**Five times now a view could not be built until a command existed**, which is Amendment
25 working rather than an obstacle it created — and every time the command layer gained the
capability rather than the window keeping it. The model tree needed `OutlineCommand`; the
viewport needed `ViewportCommand`; the results view needed `AccuracyClass` on the figure
registry, because §12's taxonomy was recorded nowhere; the regime inspector needed
`RegimeDiagnostics.MeasureAt`, because the numbers had only ever been computed at the worst
point in the gas; and the sequence editor needed `SequenceCommand`. An agent is better off
for each, which is the strongest evidence yet that AGT-2 is real rather than aspirational.

**And one of them found a hole in a load-bearing requirement.** Building the view whose
whole purpose is showing GRD-1 envelopes revealed that only one of fourteen figures had
one — see Amendment 28. A requirement can be honoured everywhere it is exercised and still
be unmet where nothing has asked.

**And the shell compiles on Linux**, which was an open bet and is now measured rather than
assumed: `EnableWindowsTargeting` is enough, XAML markup compilation included. It does not
run there and is not meant to. Only `Einzel.Wpf.Tests` is Windows-only, and on another host
it builds as an ordinary `net10.0` assembly with no sources, so a solution-wide
`dotnet test` walks past it.

**Both invariants are enforced by tests from the first commit**, which is the point of
building the scaffolding before any view. `NothingBelowTheShellReferencesIt` scans every
platform assembly beside the Linux-running test project - an invariant only ever checked
on a developer's Windows box is one already broken - and `TheShellReachesThePlatform
ThroughTheCommandLayer` checks that the shell declares a reference to `Einzel.Commands`
and to nothing else in the engine.

**That second test had to check two different things, and the difference cost a
mutation.** `GetReferencedAssemblies` reports what the *compiler emitted* - what the code
uses - so adding a `ProjectReference` to the whole transport engine left no trace in the
metadata and the test passed. UI-1 is about what the shell may reach *for*, not what it
has reached for so far, so the project file is now checked too. Only the declared-
reference check catches that mutation.

Every view §16 requires:

| View | State | What it needs beyond a window |
| --- | --- | --- |
| 3D viewport — geometry, potentials by colour, equipotentials, trajectory bundles | **Built** | Geometry, the field, and the bundle, on Helix Toolkit 3.1.2 / DirectX 11. **Every conductor is the zero level set of its own signed distance** (invariant 2), so one routine draws them all — and what differs between symmetries is what the solve claims about the third dimension: a cross-section extrudes (uncapped, because the electrode really does extend past what is drawn, with the depth named as a drawing convention per GRD-12), an axisymmetric half-plane revolves, a volume is extracted by surface nets. Checked against closed forms: a sphere's volume 0.99038 / 0.99760 / 0.99940 under refinement, every edge shared by exactly two triangles, normals exact to 1.000000, a revolved tube against Pappus to 0.99990. Equipotentials on the section plane rather than as surfaces, because a nest of closed surfaces hides the trajectories. Two colour scales, both anchored once across everything drawn — viridis for energy, and a **diverging ramp symmetric about zero** for potential, because earth is what every other potential is measured against and a ramp stretched over the observed range puts the neutral colour at 250 V for a lens holding 0 and 500. RND-8 withholds the paths and not the instrument. Helix's status is Amendment 26 |
| Density clouds instead of trajectories for diffusive regions (TRN-2) | **Built** | The viewport draws the density as **nested shells at decades below its peak** - reusing the marching-squares contours the section already draws and the same extrude-or-revolve rule the conductors follow, so a cross-section repeats along z and an axisymmetric half-plane is a solid of revolution. RND-8 on its own is entirely negative: it forbids lines through a diffusive region and, alone, leaves an empty box for the whole pressure range the mode exists to cover - and an empty box is indistinguishable from a model that lost everything. **Drawn at an instant, and the end of the run is the wrong one**: the shipped drift tube collects 9,999.76 of 10,000 ions and leaves 1.8e-302 behind, so anchoring to the end draws nothing for exactly the models that work. The instant is chosen as the middle of those that still hold a packet, is reported rather than implied (GRD-12), and the caller may name its own - which is also the seam animation scrubbing needs. Measured on the corpus drift tube: three shells at 4.555e7 / 4.555e6 / 4.555e5 per m3, nesting outward at 208 / 260 / 292 vertices, the densest spanning **9.7 mm of a 42 mm tracked region centred at 35.2 mm** - a packet three-quarters of the way down the tube rather than a uniform gas |
| Figure composer | **Seam built** | `RenderSpec` is already text in `figures/` that the CLI executes. A composer edits one of these and nothing else — which is UI-1's own test, and the reason it can be built last |
| Animation timeline, per-phase playback rates, scrubbing, frame export | **Partial** | Per-phase playback rates and frame export are built: `einzel render animation` on a declared mapping, with the rate stamped on every frame and a `frames.json` schedule beside them. Scrubbing is a shell interaction and needs the window |
| Model tree with parameter editing, live validation, units on every field | **Built** | `einzel outline` returns the declared surface - value, unit, bounds, description, what it resolves to in SI, and whether it is editable - because UI-1 forbids the shell from parsing the document to build a tree. A verb rather than a shell method (AGT-2), so an agent gets the same service. Every edit goes through the shared journal, so a change in the window is undoable by an agent on the same session. **Delivering it reversed a guard**: `SessionJournal` refused any edit that did not validate, which makes live validation impossible - a person typing 500 into a parameter bounded at 50 must see the tree with the complaint on it, and refusing every invalid document forbids any edit *sequence* that passes through one. Narrowed to refusing what does not *parse*, which is taint-never-block applied to input. `docs/lessons.md` |
| Sequence editor | **Partial** | `SequenceCommand` reports the declared timeline: phases in order, the transport mode of each, and what every electrode holds - marked against the phase before, because a sequenced instrument repeats most of its state and a table repeating every setting buries the rows that change. Bars proportional to duration, since a 2 us hold beside a 100 us flight is the shape of a pulsed extraction. Two things a reader would otherwise assume wrongly are stated: the last phase **holds** after the sequence ends, and a phase changing the mode is SEQ-1's conversion boundary. **It shows rather than edits** - a sequence is a block in the document, so editing one goes through the same journal every other change does, and what is missing is the input surface rather than the path underneath it |
| Results by accuracy class, uncertainty and warnings never behind a disclosure control | **Built** | §12's taxonomy was recorded nowhere in the code and is now on the figure registry - six Class T, four Class S, three Class B, and two deliberately in none, since `flightTime` is the raw arrival quantity the Class T figures are computed *from* and `energyDrift` says in its own description that it is a diagnostic. Every part of the envelope is a line rather than a tooltip, which is the requirement. **Building it found the GRD-1 hole below**, and closing that took the figures carrying an envelope from 1 of 14 to 5 |
| Regime inspector | **Built** | REG-2's numbers *along the path*, which is what §16's word "along" asks for and what a run does not give - a run reports the worst point anywhere in the gas, right for a warning and useless for deciding what to change. Violations are located as stretches in millimetres rather than counted. On a hundredfold density ramp the two ends differ by Kn **4.17 against 0.042** - free-molecular at one end, a continuum at the other, in the same instrument |
| Project view with model-drift and engine-drift state | **Built** | `einzel project` - the models, studies, figures, tests and extensions, with each model in one of four states. The drift itself is `einzel verify`'s, which already separates what invalidates a result from what merely annotates it; **what verify cannot answer is what has never been run**, since it walks the manifests and a model with no result is reported by neither its success nor its failure. That is the state most models in a working project are in. Building it found a defect in verify - see Amendment 30. |
| Extension manager | Not built | **Its engine half is done.** The manifest now carries `licence` beside trust level, versions and compatible range, and `einzel ext list` surfaces it - so what the view needs from below it exists, and building it is presentation. LIC-2's remaining half is the pane itself. An SPDX identifier by convention rather than by validation: a checker that recognised some spellings and not others would report an unrecognised licence as no licence, which is the failure the field exists to prevent |
| Journal with agent and human attribution | **Built** | `SessionJournal` in `Einzel.Commands`, rendered by the window beside the model tree, with the same entries an MCP client writes. A person sees what an agent did to their model, by name, and can undo it - which is MCP-1 and GRD-9 arriving where they were always aimed. Beneath it the same actions as `einzel` command lines (Amendment 25) |
| Update notice with UPD-3's deferral options | Not built | Needs the whole of §18 |

**The pattern in that table is the interesting part.** Almost every row is
"presentation over something that already works" — which is what AGT-2 is supposed
to produce, and is weak evidence that it has. The rows that needed genuinely new
capability were the 3D viewport's raster path, which is now built, and the animation
timeline's scrubbing, which is not.

**AGT-2 is now tested against three surfaces rather than claimed.** Every MCP tool returns
`CommandJson.Write` of the same outcome record the CLI serialises for `--json`, compared
byte for byte; and every shell action is recorded as the `einzel` invocation that would
reproduce it, asserted by test for the viewport and the tree alike. An invariant checked
against one surface is one that has already been broken by the time anyone notices — that
is no longer the position here.

**What the window found that the other two surfaces could not.** Every new command was
written because a view needed it, and each improved the CLI: `einzel outline` gives an
agent a model's knobs without parsing the document, `ViewportCommand` enforces RND-8 by
asking `ITransportMode.ProducesTrajectories` rather than the pressure, and `einzel project`
reports the state of a whole folder including the models nobody has run — which `verify`
walks the manifests and so cannot see. That is AGT-2 running in the direction it was not
designed for — the window pulling capability *into* the command layer rather than
accumulating it privately.

**Three defects were found the same way**, each by a view asking a question no test had:
the density cloud found the viewport anchoring to the end of a run, which is empty for
every model that works; the project view found `verify` identifying a model by content, so
that editing the model that was run made its drift *disappear* onto an identical twin; and
the GRD-2 enumeration found a rendered figure that was not flying the declared gas at all.
None was a failure of the code under test — each was a question nobody had thought to ask
until something had to display the answer.

**And a third thing the window found, in the core rather than in itself.** Extracting a
conductor's surface needed the electrode's own bounding box, and there was no way to ask for
one without switching on the shape — which is exactly what invariant 2 forbids, and which
would need a new case in every caller when a fourth shape arrives. `CompiledElectrode3D.Bounds`
now sits beside `Centre` and `CharacteristicSize`, in the one file that already owns those
cases. The defect that forced it is the instructive part: sampled over the whole solve domain
at 48 cells, **a 1 mm plate is thinner than a cell and produced no surface at all**, with
nothing said.

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
reasonable." The risk is real and the guard against it is **UI-1's prohibition**, not
deferral: the shell owns layout, input, the interactive viewport and the update check,
and owns no physics, no validation rules, no file-format knowledge and no render
output. §17's boundary is what keeps the pull bounded — what leaves Einzel is a vector
figure, a VTU file and ParaView, and the viewport is for *working*, not for
publishing.

**What not having a shell has cost so far is one thing, and it is not nothing:
AGT-2 is untested.** Every other invariant here is checked by something. That one is
checked by nothing, because there is no second surface — and the cheapest second
surface is MCP, not the window.

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
| `AGT-2` | Nothing exists only in the shell Every capability reachable from the window is reachable from the CLI and from MCP, through the same command objects. This ... | Partial | Every capability is a command object and the CLI drives them. Untested against a second surface, because neither MCP nor the shell exists - and an invariant checked against one surface is one that has already been broken by the time anyone notices. **Amendment 25** strengthens it: every shell action should also be *expressible* as a CLI invocation and journalled as one, which enforces it by construction rather than by review and is what lets a human session hand over to an agent. |
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
| `EX-1` | Ship at least thirty validated reference models spanning every device class, each with a prose description, expected results, and assertion tolerances. | Partial | **31 of the thirty by count**, spanning free flight, accelerating gaps, reflectrons, an orthogonal accelerator, a thermal source, an einzel lens, a DC and an RF quadrupole, a hexapole guide, a funnel, a travelling-wave guide captured and ballistic, an extraction trap, a 3-D Paul trap held and ejected, an ion carried by a moving gas, the diffusive mode and a measured transmission. Every expectation is arithmetic, a published value, or an exact invariant. Every named gap is now filled - an MR-TOF, a thermalisation, a three-dimensional geometry and a graded gas - so the count is met and the coverage question is the live one: what is still uncovered is a multipole above four rods in the diffusive mode, a sequenced extraction, and a 3-D geometry with a drive. **Recommend restating EX-1's target as coverage rather than a number.** The 3-D example was deferred once and the reason was a finding: a parallel-plate gap took **49 multigrid cycles at a factor of 0.652** and 124 seconds until Galerkin coarsening landed. A large solid Dirichlet slab is the worst case for the documented interior-electrode limitation, which makes the simplest geometry anybody would write the most expensive one. See `docs/numerics.md`. |
| `EX-2` | The corpus runs in CI; a failing example blocks release. | **Met** | `ExampleCorpusTests` materialises every example into a real project and drives `einzel test` through `Program.Main`. **31 of 31 in 48 s**, so it is affordable on every change rather than at release. It also asserts that every example ships a test and describes itself, and it materialises an example's data files beside its model - which is what lets an imported gas field be covered by the gate at all. |
| `EX-3` | Examples are enumerable and fetchable from both surfaces. | Partial | `einzel examples` enumerates and prints, and `einzel new --from-example` writes the model **and its test**, rewriting the model reference to wherever the file landed. Still one surface, because there is no second one. |

### Extensions (§12)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `EXT-1` | An extension declares type, schemas, trust level, resource needs, and a compatible engine version range . The runtime is an implementation detail of the ... | **Met** | The manifest declares type, schemas, trust level, resource needs and a compatible engine range. `trust` defaults to sandboxed rather than being opted into. |
| `EXT-2` | In-process (CSnakes) for first-party and explicitly trusted extensions. Lowest latency, no isolation. | Not built | The in-process CSnakes runner is not built. Section 23 leaves open whether it is worth shipping at all; sandboxed-only has so far been sufficient. |
| `EXT-3` | Sandboxed subprocess for anything agent-authored or third-party, and the default. Job objects and a restricted token on Windows, namespaces and seccomp on ... | Partial | The subprocess boundary is real: wall-clock timeout with process-tree kill, output ceiling, zero inherited environment, `python -I`, scratch working directory. **Network, filesystem and memory confinement are not enforced** - `extension.isolation-incomplete` is a non-suppressible violation on every sandboxed result. |
| `EXT-4` | Never invoked per integration step. One call per run. | **Met** | Structural rather than advisory: a subprocess cannot be invoked per step at any useful rate. **Strengthened rather than weakened by Amendment 27** - the round trip is process start almost in its entirety (the platform's own share is 1.08x to 1.52x a bare launch, once *below* it), which is exactly why per-step invocation is impossible rather than merely discouraged. |
| `EXT-5` | Large arrays cross by shared memory with an Arrow or raw-buffer layout, never by JSON. | Not built | Large arrays still cross as JSON. No shared memory, no Arrow layout. |
| `EXT-6` | A vendored interpreter ships with the application. | Not built | An interpreter is **discovered**, not vendored. `einzel doctor` says so rather than passing it off. |
| `EXT-7` | Outputs are attributed per | Partial | A deliberate JSON Schema subset - type, required, properties, items, enum, numeric bounds - because a full implementation would put remote `$ref` resolution inside a sandbox whose point is having no network. Unrecognised keywords are ignored rather than refused. |
| `EXT-8` | Before an update is applied, the updater reports which installed extensions fall outside the new engine's compatible range. The cleanest extension of all ... | Not built | Needs the updater, which does not exist. |

### Field subsystem (§10)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `FLD-1` | For each perturbation channel p , cache ∂Φ/∂p by finite difference over a full re-solve. | **Met, with a stated floor** | Cached shape derivatives, validated to 6.5e-6 of the closed form at a 0.11-cell step. **Only after cut cells**: the first spike failed, see Amendment 5. **And cut cells are necessary rather than sufficient.** A perturbation whose own signal is below the solve's field error cannot be differenced for at all - the Astral's 200 micron mirror convergence is an anisotropy of 2.9e-4 against roughly 0.4% of second-order field error, and the solved answer ranged over 3.54, 0.011 and -0.57 of the closed form on gap width alone. Where the perturbation is a **rigid motion** it can be applied as a coordinate transform on the solved field instead, which is exact: 5.4e-20. Two boundary kinds also matter and only one has cut cells - a metal-to-metal edge has Dirichlet nodes both sides and no vacuum node to carry a sub-cell crossing. See Amendment 36. |
| `FLD-2` | Every sweep runs a stratified validation subset; if the maximum residual exceeds | **Met** | The residual is an ordinary Taylor remainder, quadratic in the perturbation to three figures. The limit is (delta/L)^2, so 1 ppm holds to delta/L about 1e-3. |
| `FLD-3` | Field caches are keyed by content hash over geometry, mesh, symmetry declaration, boundary conditions, and the solver-behaviour version — the last term is ... | **Met** | Caches keyed by content hash including the solver-behaviour version. |

### Gas (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `GAS-1` | A gas region carries species, temperature, a pressure field , a bulk velocity field , and a collision model. The velocity field is easy to omit and hard ... | **Met** | Species, temperature, collision model, a uniform bulk velocity, and now an imported velocity **field** - VTK ImageData, sampled trilinearly, conserved at the face, with the overhang past its extent reported. Both transport modes see it: the event-driven models carry the ion's position into the neutral draw, checked against `u + mu E` at **120.000 m/s of carry against a declared 120** with -0.000 across it, and a flow field agrees with an equivalent uniform drift to **1e-9** on the same seed. `gas.flow-extrapolated` reports a collision drawn outside the imported extent. **And the pressure is a field too**, so a differentially pumped instrument is now expressible: `pressureField` with a required unit, read as a density through n = p/kT, with mobility scaled as 1/n (which nothing here did before) and both collision models thinned against a majorant taken at the densest gas anywhere. A field at twice the declared pressure gives a **bit-identical trajectory** to declaring twice the pressure, under both collision models, and agrees to 1e-6 through the diffusive path. The regime numbers are computed where the gas is thickest, because a description that fails anywhere in the instrument has failed. One assumption left, stated: a single **temperature**, which the document already carried. |

### Guardrails (§4)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `GRD-1` | No bare numbers Every quantitative result carries value, units, uncertainty or confidence interval, ensemble size or convergence measure, and active warnings. The API offers no ... | **Partial** | Structurally enforced where it is enforced: `Measured` has no public way to read a bare value, and a test that tried had to go through `Deconstruct`. **But most figures of merit had no enveloped path at all** - `FiguresOfMerit.Evaluator` returns a bare double because ranking needs an ordering, a deliberate exception argued where it is taken, and the consequence nobody had written down is that twelve of the fourteen figures existed *only* in the excepted form. `FiguresOfMerit.Measure` is the counterpart: **1 of 15 figures carried an envelope, now 5**, by resampling the ion cloud. Validated against two closed forms the code has no part in - the mean's sigma/sqrt(N) at 0.987/1.022/0.996 and the median's sqrt(pi/2)sigma/sqrt(N) at 0.975/1.041/0.940. The remaining ten are named on every result by `results.no-envelope` rather than printed bare. See Amendment 28. |
| `GRD-2` | Warnings propagate Validity warnings travel with the result through every layer — engine, command layer, CLI output, MCP response, exported file, rendered ... | **Met** | **Now enumerated rather than asserted.** The requirement names its own population — seven layers — so `WarningPropagationTests` is one test per layer, with the MCP one next door in `Einzel.Mcp.Tests`. The exported file is checked by **set equality with the result**, not containment, because a file carrying one warning and dropping the rest passes any weaker check. **This row previously read "exported VTU/VTI files and figures" and was wrong on both**: the trajectory `.vtu` carried no warnings at all, and the figure carried neither the warnings nor the gas. See Amendment 29. |
| `GRD-3` | Warnings above threshold are not suppressible Validity violations cannot be silenced by any caller, including in batch mode. | **Met** | Validity violations carry a non-suppressible severity and no caller can silence them. |
| `GRD-4` | Validity is checked, not assumed Regime applicability, mesh convergence, ensemble convergence, adiabaticity, and the §10 linearization residual are ... | **Met** | Regime applicability, mesh convergence, ensemble convergence and the linearisation residual are all computed rather than assumed. |
| `GRD-5` | Preview results are labelled and cannot be promoted Tagged permanently; cannot be quoted, exported, fed to an optimizer, or rendered without visible ... | **Met** | The taint rides on the number, and a preview writes nothing - a tainted result in `results/` would be reported as current by `verify`. |
| `GRD-6` | Extension results are attributed Carries the extension identity and version; cannot present itself as first-party. | **Met** | Extension results carry the extension identity and interpreter; the manifest records `null` where no interpreter took part. |
| `GRD-7` | Results are immutable and traceable Every result references a manifest. Every rendered artifact references a result. | **Met** | Every result references a manifest. Studies wrote none at all until recently; sweeps, optimisations and scans all write one now. |
| `GRD-8` | Spending is deliberate Any operation exceeding a configurable cost threshold requires a prior estimate. | **Met** | `einzel estimate` takes **a study as well as a model**, which is the operation anyone actually plans against - short by the evaluation count before, and silently. A diffusive run's step is computable exactly and predicted 901 against 901 actual. A trajectory run's is path-dependent, so it is **measured by a short pilot flight** rather than omitted: the whole flight where it finishes inside the window, otherwise scaled and declared a floor. The solve rate is measured on **this machine, on this geometry** - a hardcoded constant put the C-trap's 5.9 s solve at 1.81 s. End to end: **6.25 s estimated against 7.06 s actual** on a volume model, where the same model was 1.81 s before. A study's flight is sampled across its own declared range, since evaluation cost varies **2.2x** along a scan that crosses a focus; on that scan the estimate is **0.76x of wall clock and 0.89x of the computation**, the difference being process start, which is excluded and said to be. Pilots repeat while repeating is cheap and report the cheapest, which took the rate's run-to-run spread from a factor of two to **2 per cent**. **The mesh is reported too**: each axis rounds its interval count up to a power of two, so cost is a step function of the cell size - a 635 x 48 x 350 mm analyser at a requested 1 mm gets 0.62 x 0.75 x 0.68 mm and 34.2 M nodes, and 1.5 mm costs **7.9x less**. The suggested size is evaluated with the grid's own arithmetic and asserted to deliver what it promises, because a rule of thumb offered 1.24 mm, which lands on the boundary and gives the identical mesh. **What one evaluation IS depends on the transport**: the ordinary case solves once and flies `members` ions, but a diffusive run steps a density and a space-charge run advances the packet in lockstep, so for those the model's own cost already IS one evaluation and multiplying it by the ion count charges twice - for a diffusive model, for trajectories that mode does not produce. Process start is excluded and said to be. See Amendment 33. |
| `GRD-9` | Human work is never silently lost Where an agent and a human share a live model, mutations are attributed in a shared linear journal. | **Met** | `SessionJournal`, served by `Einzel.Mcp`. The attribution and the shared linear stack are MCP-1's row. What this row adds is the *never silently lost* half, and building MCP-1 did **not** deliver it: the journal knew only about mutations made through it, so a person editing the model in their own editor had their change overwritten by the agent's next whole-document edit with nothing anywhere to say so. **The sharper consequence was to undo** - an unrecorded change breaks the chain, so walking back landed on a document predating the person's edit and discarded it as a *side effect of reversing something else*. `Reconcile` now records an outside change as an entry attributed to `outside` (not to the person: another tool, another session and a git checkout look identical from here), refuses an edit written against the document as it was, and makes the refusal recoverable by having `model_read` take the change up. Checked by mutation - a no-op `Reconcile` fails three of nine journal tests. `docs/live-session.md` |
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
| `LIC-2` | Extensions carry their own licences; the extension manager surfaces them. | **Partial** | An extension **carries** one: `licence` on the manifest, scaffolded by `ext register` so a new extension answers from the first minute, and surfaced by `einzel ext list` in both forms. **An undeclared licence prints `NOT DECLARED` rather than being omitted** - the case where care is most needed must not be the one whose line is shortest - and is null in `--json` rather than a placeholder, so a caller cannot mistake "did not say" for a licence it recognises. What is missing is the manager *view*: the shell has no extension pane, so the surfacing is the CLI's alone. |

### Live session (§16)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `MCP-1` | Mutations are attributed and the undo stack is shared and linear. | **Met** | `SessionJournal` in `Einzel.Commands`, served by `Einzel.Mcp` over stdio. Attribution is taken from the client's `initialize` handshake rather than from a tool argument, so an agent has no spelling with which to sign an edit as anybody else - asserted in two halves, the name that comes back and the absence of any parameter that could have offered another. The stack is shared: an agent over the wire reverses an edit made in process, and the entry names both parties. Linear because an undo is itself an entry, so walking back twice appends twice rather than popping. A change made to the file outside the session is recorded and an edit against the moved document refused, which is GRD-9's row. Both claims checked by mutation: private per-author stacks fail three of six journal tests, a popping undo fails a different two, and moving attribution into a tool parameter fails three of five protocol tests. Streamable HTTP hosted by the shell - the primary transport - waits on the shell; the tools are above the transport, so it is a wrapper rather than a rewrite. `docs/live-session.md` |

### Performance (§8)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `PERF-1` | Nominal field solve, all basis solutions < 30 min Cached. Symmetry reduction makes this reachable for a 200-ring funnel | **Met** | A 96-ring travelling-wave guide reduces to two basis solves; a 48-ring funnel to two. Well inside 30 minutes. |
| `PERF-2` | Single ion, cached fields < 100 ms Interactive tuning must feel live | Unverified | Not measured as a target. A single ion through cached fields is fast in practice; no benchmark asserts it. |
| `PERF-3` | Preview tier, any model < 10 s | **Met** | 9 ms on the shipped reflectron against a 10 s budget. |
| `PERF-4` | 10 4 -ion ensemble, Class S < 5 min CPU, embarrassingly parallel | Unverified | Ensembles of 20,000 ions are run in tests, but wall time against the 5-minute budget is not asserted. |
| `PERF-5` | Quadrupole stability scan, 500 × 10 3 < 2 h GPU-bound; why ILGPU is early | Not built | Needs the GPU path. `einzel scan` makes the scan expressible; nothing makes it fast. |
| `PERF-6` | Tolerance sweep, 10 3 geometries × 10 3 ions < 8 h Only reachable via §10 sensitivity fields | Partial | The superposition side is measured - 500 linearised draws at 25 ms against 142 ms for one solve. The full 10^3 x 10^3 campaign has not been run. |
| `PERF-7` | Extension round trip, sandboxed < 50 ms Sets the granularity floor for | **Unverified** | **Not separable from process start, which is not this platform's to control.** Launching the interpreter and doing nothing costs 45.0, 49.6, 53.9, 58.2, 40, 51 and 63 ms across seven runs on one machine; the budget straddles that spread, so asserting it measures CPython's start cost rather than anything here. On a shared build agent a bare launch takes **seconds**, and the old assertion passed and failed on the same commit in two runs minutes apart. What is measured and asserted instead is the platform's own share: a round trip costs **1.08x to 1.52x** a bare launch, and on one run came in *below* it - the marshalling is under the noise floor of process start. The absolute number is reported on every run. See Amendment 27. |
| `PERF-8` | CLI cold start to first output < 500 ms No network call permitted in that path | **Met** | 73-147 ms cold start against 500 ms. |
| `PERF-9` | Vector figure, 10 3 decimated trajectories < 5 s Agents iterate on figures; it must not be a batch job | Unverified | Figures are drawn in tests but not timed against the 5 s budget. |
| `PERF-10` | Vector figure file size, same < 5 MB Must open in a text editor and an illustration program | Partial | The quadrupole PDF is 13 KB. No test asserts the 5 MB ceiling for 10^3 trajectories, because nothing draws 10^3 trajectories yet. |

### Project (§3)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `PRJ-1` | Models, studies, extensions, render specs, and results are text. | **Met** | Models, studies, extensions, render specs and results are all text. |
| `PRJ-2` | Large artifacts are referenced by content hash, never embedded. | **Met** | Large artifacts live in `.einzel/` and are referenced, never embedded. |
| `PRJ-3` | A run manifest fully determines its run. Model hash, seeds, engine version, transport mode, solver settings, extension identities. Results are therefore ... | **Met** | Model hash, seeds, engine version, solver-behaviour version, transport mode, compute path, extension identities, interpreter and machine. **And which model, as distinct from which content**: `modelPath` is recorded alongside the hash. PRJ-3's list does not ask for it and by that list does not need to - the hash determines the run - but identity is a second question, and answering it with the hash meant editing a model could make its drift *disappear* onto an identical twin. Recorded with forward slashes because a manifest travels; absent on older manifests, where the hash search remains the fallback. See Amendment 30. |
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
| `RND-1` | Rendering is an engine capability, not a shell feature. Einzel.Render sits below the shell; the figure composer and einzel render are peer consumers of ... | **Met** | `Einzel.Render` sits below any shell and draws headlessly on the Linux CI runner with no display, window manager or font server. **And the conductor surfaces now leave the program**: the surface-nets extraction was headless, tested on Linux, and consumed only by the Windows viewport, so the artifact that lets an external renderer draw a three-dimensional geometry needed the shell - invariant 1 pointing the wrong way. `einzel export --mesh` writes them as OBJ, one named object per electrode. Building it found the sub-cell failure returning through the *electrode's own aspect ratio*: a 4 x 635 mm stripe meshes to nothing at 48 cells across its longest span, and resolving by the thinnest instead gives 1.16 M triangles and a 77 MB file. Per-axis now; bit-identical on an isotropic shape. |
| `RND-2` | A render spec is text , lives in figures/ , and is versioned with the model. The figure in a paper is regenerable from the repository rather than being a ... | **Met** | A render spec is text in `figures/`, versioned with the model. |
| `RND-3` | 2D sections and orthographic projections emit SVG and PDF , through a geometric projection pipeline that produces paths rather than pixels. This is a ... | **Met** | SVG and PDF from a path pipeline. Both writers are hand-authored; a test walks every PDF cross-reference offset. |
| `RND-4` | Shaded 3D perspective is raster. Hidden-surface vector output is a deep rabbit hole with poor payoff. Schematic 3D with hidden-line removal may be added ... | Not built | No raster path at all, so neither shaded 3D nor `render still`. Section 23 leaves open whether hidden-line vector output is worth building. |
| `RND-5` | Trajectories are decimated with a stated geometric tolerance ( | **Met** | Stated and measured, and the point-to-segment distance is clamped - a reflectron is why. |
| `RND-6` | Text stays text. Labels, dimensions, and axis annotations are selectable and editable in the output, so a figure can be relabelled for a different venue ... | **Met** | Labels are text runs in both SVG and PDF, asserted in both. |
| `RND-7` | ), scrubbing, and frame export. Model tree with parameter editing, live validation, units on every field, template instantiation. Sequence editor : the ... | **Met** | Both halves, and neither is optional in the interface either. A mapping is declared as phases in a render spec, each naming the simulated time it runs to and the rate it plays at - and an animation can only be asked for through a spec, with no `--rate` flag, so **there is no command line that produces one without a declared mapping**. The rate is stamped on every frame in two readings (`500 ns of flight per second of playback - 2,000,000x slower than real time`), written by the renderer rather than offered as a styling option. On the shipped reflectron the turn-around is a fifth of the flight and 69% of the film. |
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
| `SC-1` | For Class T runs the space-charge approximation parameters are validated against the direct method on a reference population. | **Met** | Both methods are `ISelfField` peers and are validated against each other **at matched smoothing**, which is what the requirement needs and what an unmatched comparison cannot give: the sum taken to its own point limit (softening/100, worth 3.5%) against a grid cell of 0.92 mean macroparticle spacings agrees to **0.08%**. Both approximation parameters are declared (`spaceChargeGrid`) and both are reported. The direct sum itself is validated by third law to 1e-14 and the uniform-sphere closed form to 5%. |

### Sequencer (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SEQ-1` | A phase boundary may change transport mode; the conversion is explicit, reported, and named as a source of uncertainty. | **Partial** | **The conversion exists**, in both directions, with the uncertainty named: `PacketConversion`. Trajectories to a density is a bilinear deposit conserving the population by construction (exact to 1e-12), losing the velocity distribution entirely - which is what the diffusive description *is*, since drift-diffusion holds because the velocities have relaxed. A density to trajectories samples position from the density and **invents** the velocity, drawn Maxwellian at the gas temperature plus the local drift; `transport.velocity-assumed` is a non-suppressible violation, because a caller reading a flight time computed from invented velocities and not knowing they were invented has been misled by the platform. Checked: equipartition **1.0021 at 300 K and at 1200 K** (two, because one is consistent with a constant that happens to match), drift against muE **exact**, a Gaussian cloud's centroid and spread recovered to sampling error, a 4000 m/s beam coming back at 0.2 m/s. The discriminating check is cylindrical - a cell is a ring, so a uniform density gives mean radius **2R/3 = 13.3333 mm, measured 13.5177**, against the R/2 = 10.0 a density-weighted draw gives; run the wrong way it gives 10.0245 and **only that one test of the ten fails**. **The timeline is now the instrument's**: schema 0.6 adds a model-level `sequence`, phases are resolved once and handed to every element - a solved geometry re-weighting channels it has already solved, an analytic one compiled per phase and switched by `SequencedField`, an element no phase moves left static. `stages` on a solve stays the older spelling for the single-element case, with both-declared refused. A code review caught the first version reaching only the solved branch, leaving analytic elements frozen at baseline while the solved ones moved. That closes the defect below and is the prerequisite for the mode. **A phase now names a transport mode**, absent meaning the model's - the same rule its parameter overrides follow, so a model with no sequence and one whose every phase runs in the declared mode are the same run. `CompiledModel.Phases` carries the schedule and the mode per phase, and `ChangesTransportMode` says whether any boundary actually converts. **And a run crosses the boundary.** `SequencedRun` walks the phases, each an ordinary run of its own mode over its own duration, converting where the mode changes. On the test instrument - launch, thermalise, extract - the packet advances 1.37 mm in a microsecond while flying and **does not move at all over twenty times longer as a density**, because the diffusive drift is muE and E is zero there. That is the conversion made visible rather than a defect: drift-diffusion holds precisely because the velocity distribution has relaxed, so the momentum genuinely is discarded. Position, the one thing both descriptions carry, survives to the fourth decimal. A trajectory leg starting part-way along the timeline is flown against a new `TimeShiftedField`, because the integrator always starts at t = 0 - wrapped rather than given a start time, which is the precedent `AxisymmetricField` and `PonderomotiveField` set. **The first phase may be the trap**, which is the ordering the requirement was written about, seeded through `DiffusionRun.Seed` rather than a second implementation. Reusing that path corrected two numbers a duplicate had got wrong - a grid built with `new Grid2D` rather than `OverBox`, which rounds to a power of two, so one model got two different grids depending on the path; and a mobility helper ignoring `Derived`, so a cross-section-derived mobility came back as the stored value. A third gap closed with them: the diffusive leg passed no absorbers, so electrodes did not absorb during a diffusive phase. **And `einzel run` reaches it**: the fork tests `ChangesTransportMode` before the model's own mode, since a model may declare `diffusion` and still have a sequence that leaves it. The terminal shows a per-phase table with a dash where a diffusive phase has no trajectories (different from having none left) and `packet centre` rather than `final x`; `--json` carries a `sequence` block; the manifest records `diffusion -> trajectory`, because one mode would claim to determine a run it does not describe. **Two defects already fixed once were met again**: a successful sequenced run exited `ConvergenceFailure` (the exit logic knew two outcome strings), and the printer showed `flight time NaN` (the absent-not-NaN fix was gated on `Diffusion is null`). Both were fixes written as a list of known modes rather than as the question being asked - `docs/lessons.md`. **And a driven geometry inside a diffusive phase gets the cycle average**, through the same `Effective` wrapper the wholly diffusive path uses - shared rather than written twice. That was the **fifth** occurrence of a time-varying quantity reached through a time-free interface answering at an arbitrary instant, and the first one I introduced. Measured on a four-rod quadrupole at 2 mbar with the packet released 1.5 mm off axis: **0.2341 mm after 60 us at 400 V, against 1.5000 mm with the drive off**. The geometry had to be four rods - a first version used two plates, which give a nearly uniform field where the ponderomotive force goes as grad E squared, so the packet moved 0.1% either way and the test passed on a threshold of 'less than where it began'. Nothing outstanding for SEQ-1. A stage is declared on a *solve element* while the transport mode is a property of the *run*, so a per-element stage cannot carry one - two elements would name different modes for the same instant, and there is no superposition of transport modes the way there is of fields. Worse, the existing arrangement is already wrong: `CompileStages` re-resolves the **whole model parameter surface** and re-expands **only its own element**, so two electrodes written as the same expression over the same parameter held **900 V and 300 V** during a stage, on a model that validated cleanly. Refused first - a sequenced model may have one field element - then fixed: `Timeline` resolves the phases once for the model and hands the same surfaces to every element, so a phase moves everything written over its parameters. Verified by mutation in both arms: restoring the per-element behaviour fails its own test and nothing else, separately for the plane and the volume. The 3D arm had no test at all before this - it is a separate copy of the same shape rather than shared code, which is exactly the arrangement where one arm gets fixed and the other is left behind. The remaining work is `run` across modes, sampling the field inside the diffusive loop. `docs/pressure.md`, `docs/lessons.md` |

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
| `UI-1` | The shell owns layout, input, the interactive viewport, and the update check. It owns no physics, no validation rules, no file format knowledge, and no ... | **Partial** | **The shell exists**, and the prohibition is now checked rather than honoured by construction: `ShellBoundaryTests` runs from the Linux-running test project over the assemblies actually present, with a `MustBePresent` guard so a scan that found nothing cannot pass. **Two different checks were needed**, and the difference cost a mutation - `GetReferencedAssemblies` reports what the compiler *emitted*, so declaring a ProjectReference to the transport engine left no trace and the test passed; UI-1 is about what the shell may reach **for**, so the project file is checked too. Every view is built on a command object, and twice a view could not be built until a command existed (`einzel outline`, `ViewportCommand`) - which is Amendment 25 running in the direction it was not designed for. **Partial rather than Met** because the shell does not own the update check: there is nothing to check. |

### Update (§18)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `UPD-1` | The shell is the only component that checks for updates, and only at launch. No periodic timer, no background polling. | Not built | The shell exists and does not check for updates, because `Einzel.Update` does not. The half that is a prohibition holds vacuously - nothing anywhere polls - and the half that is a capability needs the whole of section 18. |
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

## Completed in an earlier round of the list

These four were items 10 to 13 of the previous revision of *What to do next* and were
left standing when that list was replaced, with no heading of their own and no items 1
to 9 - so they read as the live list and their item 13 still called itself "the top of
the list". They are kept, and headed, because the reasoning in a completed item is worth
more than the fact of it; the live list is *What to do next*, below.

10. ~~**`render animation` (RND-7)**~~ — **done, and the requirement enforces itself
   through the interface.** An animation is asked for through a render spec and there is
   no `--rate` flag, so there is no command line that produces one without a declared
   mapping. The rate is stamped on every frame in two readings, written by the renderer
   rather than offered as a styling option.

   On the shipped reflectron, three phases give 1.000 / 4.400 / 0.995 s of playback: the
   turn-around is **a fifth of the flight and 69% of the film**, which is precisely what
   one rate cannot show. Frame times are computed from playback time rather than
   accumulated, the final frame is forced onto the arrival, and a frame on a boundary
   announces the incoming rate.

   **A design bug of mine, and the test that missed it.** Handing each frame only the
   part of the flight drawn so far made every frame choose its page from its own prefix —
   a camera following the ion, invisible in any single frame. The test written for it
   **passed with the bug restored**, because it used a *solved* template whose extent
   comes from its declared domain; moved onto an analytic model it fails at once.

   **And it exposed one that had been shipping.** The scaffolded reflectron drew its
   turning point at x = 105,080 mm on a 160 mm page, and had since sections were built.
   An analytic model's extent came from the source and the detector alone, and a
   reflectron catches the ion where it launched — the same point. No render test covered
   it because every one of them uses a device template, and every template declares a
   solve domain.

   **And the field moves, which it did not at first — the fourth sighting of one
   defect.** A driven field implements the time-free `IElectrostaticField` as well and
   answers it at t = 0 without failing, so every frame drew the same instant. After
   `einzel solve` reporting a driven geometry's DC pattern, the diffusive mode stepping a
   density through a snapshot of the RF, and `SuperposedField` becoming a snapshot when a
   driven member was summed in. The instant is now declarable and every frame supplies
   its own, with `render.field-at-instant` on a static section either way.

   Checked exactly over one period of a 1 MHz quadrupole: **20, 0, 20, 0, 20**
   equipotential paths at 0, T/4, T/2, 3T/4, T. Nothing to contour at the zero crossings;
   the same drawing at T as at 0, to the last bit; the rod pairs swapped at T/2. The
   contour levels had to be **fixed once across the animation**, because a driven field's
   range changes through the cycle and per-frame levels would spread over rounding noise
   at a zero crossing — the page defect again, in the other axis.

   Left: **geometry that moves**. A stage may change what an electrode holds and not
   where it is, so the conductors are identical on every frame by construction; a
   mechanism with a moving part is not expressible in the model format at all.

11. ~~**A density at a chosen instant**~~ — **done.** A diffusive run reported the
   density it *ended* with, so a model whose ions all arrive drew an empty box, and the
   only way to see the packet was to shorten `maximumFlightTime` — which gets one by
   throwing away everything after the moment being looked at. `DriftDiffusion.Run` now
   takes a list of instants and `einzel render section --at-us` draws one: **0 contours
   at the end, 10 at 50 µs centred at 49.5 mm, 11 at 150 µs at 102.9 mm** on the corpus
   drift tube.

   Both times are reported, because they differ: a diffusive step lands where its
   stability limit puts it, and cutting it to land on a requested instant would change
   the step sequence and so the answer. And **recording is bit-identical to not
   recording** — step count, collected ions and every node — which is asserted, since
   snapshots that perturbed the run would be snapshots of a different run.

   **And it unlocked the diffusive animation**, which was refused outright and rightly
   so while a run reported only its final density. The command layer runs the transport
   once with the frames' own instants as its snapshot list and hands the renderer the
   results, as the section path already does. On the corpus drift tube over 200 µs the
   packet drifts 22 → 100 mm, spreads 24 → 59 mm, and narrows again at the end as its
   leading edge is collected — three things a trajectory cannot show.

   **The contour levels had to be anchored once, and that matters more than the page
   did.** Density contours sit at decades below the peak, and a diffusing packet's peak
   falls as it spreads: anchored per frame the levels fall with it, the contours stay the
   same size, and a film of a packet spreading shows a packet doing nothing. Not a
   flicker — a lie. Anchored across the animation, later frames show fewer contours,
   because the density really is lower.

12. ~~**Dimensioned callouts**~~ — **done.** The memo's own figures are line drawings
   *with* dimensions, and a section without them says what the instrument looks like and
   not how big any of it is.

   **The number is measured, never written down.** A `dimensions` entry declares the two
   points it spans; the length is computed when the figure is drawn. `label` names the
   span and does not carry the value, because a typed number is a second statement of
   something the model already says and the two part company at the first parameter
   change — which is precisely what a dimensioned drawing exists to prevent.

   **And the points may be expressions over the model's parameters.** §9's rule for a
   model, "every placement is a parametric expression, never a baked number", is not
   weaker for a drawing of it. Changing `turningDepth` from 50 to 80 mm and re-rendering
   the same spec gives `penetration 50 mm` then `penetration 80 mm` with no edit in
   between; the test asserts exactly that — one spec, two models, two measurements.

13. ~~**A sequenced example, and the defect that blocks it.**~~ - **done.** `sequenced-extraction` ships, at 1.0e-7 of its closed form, and the defect that blocked it was `FlightTimeStudy` refining an absolute velocity floor to 1e-11 m/s, unsatisfiable for an ion starting from rest. The claim below that this is "the top of the list" is therefore stale, and was the clearest symptom of this block having been orphaned. The corpus does not
   exercise the sequencer, which is the one Phase 4 capability it misses. Writing the
   example found two defects, both fixed — `CanDoWork` reading the base potentials and
   not the stages (the fourth sighting of one pattern, the third in that function), and a
   stage `set` to an expression being read as its absent literal zero.

   **Fixed.** `FlightTimeStudy` refines by scaling the relative tolerance *and both absolute
   floors*. At its deepest rung `AbsoluteVelocityTolerance` reaches **1e-11 m/s** — ten
   picometres per second, against thermal speeds of hundreds of metres — and for an ion
   starting from rest the normalised velocity error is unsatisfiable at any step size.
   Isolated by tightening each of the three alone: only the velocity floor reproduces it.
   That floor is load-bearing; it is what stops `ErrorNorm` being a position-error
   controller. `einzel preview`, which does one run, gives **2.9106 µs against a closed
   form of 2 + 0.910572113**.

   Holding the floor leaves the reflectron **bit-identical** and makes its interval
   **17× narrower** (1.48e-10 µs against 2.58e-09) — a measured residual instead of a
   saturated floor. It also broke
   `AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact`, and the reason is
   the part worth keeping: **that model's bit-exact rung agreement depended on the ladder
   over-tightening the very floor at issue.** The test had been asserting a coincidence.
   Since nothing reachable through the study's API reproduces the collapse, the rule was
   given a name — `FlightTimeStudy.ConvergenceResidual` — and is now tested directly on
   runs that agree to the bit, which states the rule instead of hoping a model will
   demonstrate it.

   **`sequenced-extraction` ships**, the corpus's first: hold at rest, then extract.
   Predicted 2 µs + 0.910572113 µs, measured **2.9105718 — 1.0e-7 out**, which is the
   finite plates and the grounded boundary rather than the sequencer. Corpus 30 → 31, and
   Phase 4's sequencer is exercised by the release gate for the first time.

   **This is the top of the list**: it is a defect rather than a gap, it blocks a Phase 4
   deliverable from being demonstrated, and the machinery it blocks (traps, pulsed
   extraction) is what the memo's §6 item 5 is a choice between.

## What to do next

Ordered by what unblocks the most, with the reasoning rather than just the list.
Everything struck through was on this list and is now done; it is kept because *why*
each turned out to be cheap or expensive is worth more than the fact of it.

1. ~~**Wire particle-in-cell to the packet integrator (SC-1)**~~ — **done, and it
   found something.** Both methods are now `ISelfField` peers, so they can be handed
   the same configuration and differenced. The grid is the packet's own and lives in
   the packet's frame, which makes uniform translation **exact** (1e-11 across
   250 mm) and free; the refresh criterion is therefore written on *shape*, since
   shape is the only thing that ages. Against the reference: **0.5 per cent** on a
   flown packet's widening, about a per cent through the body of a static one.

   **The finding is that a linear gather costs 27× the integrator steps.** The
   previous commit argued that ACC-3's ban on trilinear interpolation does not reach
   a self-consistent field whose accuracy the deposit already bounds. That is right
   about accuracy and wrong about cost: a trilinear force kinks at every cell face and
   an embedded Runge–Kutta estimator reads a kink as error. Measured at 274/383/656
   steps on 16/32/64 nodes against the direct sum's 25 — the count tracking the node
   count is what identifies the mechanism. A quadratic B-spline keeps the
   deposit/gather symmetry, is continuously differentiable, and takes it to
   45/65/95.

   **Where it starts paying: about 850 macroparticles.** Below that the reference is
   simply faster and reaching for the approximation buys nothing; at 2,000 the grid is
   3.2× ahead. Worth stating as a crossing rather than as asymptotics.

   **And it is now declarable** — `"spaceCharge": "pic"` with an optional
   `spaceChargeGrid` block carrying `nodes`, `padding` and `refreshTolerance`, refused
   against any other method rather than ignored. Closing that gap forced two
   measurements the earlier claim of agreement did not survive:

   **The reference has an approximation in it too, and the old comparison measured the
   difference between two of them.** The direct sum softens at the mean macroparticle
   spacing and the grid smooths at the cell, so "agreeing to a few per cent" at each
   method's defaults was a coincidence of comparable smoothing lengths. Taking the sum
   to its own point limit is worth **3.5%**, and against *that* the grid at a cell of
   0.92 spacings agrees to **0.08%**.

   **Accuracy has an optimum rather than a floor**, which is the opposite of every
   other resolution knob here: −15.1%, −4.2%, +0.08%, +4.4% at 3.68, 1.84, 0.92 and
   0.46 cells per mean macroparticle spacing. Refining past the match makes it worse,
   and refining is what a reader does when they want a better answer. Confirmed as a
   *sampling* artefact rather than a resolution one by holding the cell fixed and
   raising the macroparticle count — 4.42% to 1.55% to 0.93% as macroparticles per cell
   go 0.012 to 0.049 to 0.195. `spacecharge.grid-resolution` now reports the ratio on
   every run whether or not it crosses a threshold, and names the node count that would
   match.

   The estimate had to learn the same lesson: 200 macroparticles take **0.99 s at 16
   nodes and 124 s at 128**, so a cost model blind to a knob a document can now set was
   gating on a number missing its dominant term. It is two terms now — linear in the
   cloud, cubic in the node count — pinned by the measured crossing and a measured
   43/57 split, and it tracks the measured 54× ratio to within 10%.

   Still undone: the refresh criterion converges (12.68% → 6.16% → 1.01% → −0.54% as
   the tolerance tightens 0.30 → 0.02) but nothing chooses it automatically, and the
   solve is a full multigrid V-cycle from scratch at every refresh rather than a few
   cycles from the previous answer.

2. ~~**Make a driven diffusive run affordable**~~ — **done, with a trade that has to
   be stated both ways.** `"densityStep": { "scheme": "implicit", "gain": 64 }` is
   backward Euler on the same Scharfetter-Gummel coefficients, solved by red-black
   Gauss-Seidel. **21.1× the speed for 0.057% error** on the shipped funnel at 2 mbar
   over a 50 µs window, so its 843,000 steps over 900 µs become about 13,000 and a run
   that took hours takes minutes. **The error does not accumulate over a longer flight,
   it falls** — the same gain gives 0.108% over 5 µs and 0.057% over 50 — because
   backward Euler's error is concentrated in the initial transient, while the speedup
   grows because the explicit cost is linear in the window and the sweeps per step are
   not.

   **The load-bearing property is not the stability, it is that positivity survives a
   partial solve.** Every term in the update is non-negative, so the iterate is a valid
   density however far from converged it is — a scheme that went negative on the way
   would be unusable however stable, because a negative density has stopped meaning
   anything.

   **And it is not a general speed-up, which is the half that would be easy to leave
   out.** The Gauss-Seidel iteration's difficulty is set by the *diffusive* part of the
   operator, so a step long by Courant's standard but still short by diffusion's costs
   about three sweeps — while a plain drift tube, already near its diffusion limit,
   climbs from 11 sweeps a step at gain 1 to 88.7 at gain 16 and comes out **slower**
   than stepping explicitly. Both are measured and both are documented.

   **What says it is correct rather than merely stable is the Boltzmann equilibrium**,
   which Scharfetter-Gummel is built to hold exactly and which backward Euler must
   therefore hold at any step. It holds to **8.9e-16 in log density over three decades
   at a gain of 1000, in two steps and two sweeps** — one sweep per step, because the
   previous density *is* the answer. Verified by breaking the solve the way a real
   mistake would: the non-negativity tests still passed, and the equilibrium moved by
   factors of 6 to 18.

   **The flux is now assembled once**, which the explicit path wanted anyway: it was
   recomputing two exponentials per face per step. **Bit-identical**, asserted over four
   configurations spanning Cartesian and cylindrical meshes, still and moving gas,
   interior absorbers and every edge kind — density, collected count and every named
   loss, to the last bit.

   Still undone: nothing chooses the gain. Both limits are computable before the run,
   but what gain is acceptable is an *accuracy* question and nothing here measures the
   accuracy of a step it has not taken. Richardson extrapolation over a doubled step
   would, at three solves a step instead of one.

   ~~The ponderomotive well's gradient at
   the ring edges sets the explicit step: on the shipped funnel at 2 mbar the step
   is 1.067 ns against a diffusion limit of 5.2 µs, a factor of 4,900, so 900 µs
   would be about 843,000 steps. Attributed by control — 15.5 ns at 0 V RF, 8.93 ns
   at 25 V, 1.067 ns at 100 V — so it is the RF and roughly as E₀². An implicit or
   operator-split step is the fix.~~ This is the last thing standing between the funnel
   benchmark and a number.

3. ~~**A region on an analytic field element, so an exact analyser can join a
   beamline.**~~ — **built, and one measurement corrected my account of what it
   costs.** Amendment 32. An analytic element may declare a box outside which it
   contributes nothing: an ordinary 1 kV/m section 75 mm from an orbital analyser
   feels **−1,499,000 V/m** of it unbounded and **exactly its own 1,000** bounded,
   and on the axis the unbounded case is worse than swamping, since the model cannot
   be asked a question there at all.

   **The potential steps at the boundary and I first called that an energy the ion
   gains.** It is not: an ion is moved by the *field*, which is exactly the declared
   one on each side, so a bounded uniform field is an accelerating gap followed by a
   drift — **13.658582 µs against a closed form of 13.658582**, with the unbounded
   control at 10.180506. What it really costs is the energy-drift diagnostic and
   non-conservation for an ion crossing *more than once*. `bounded-accelerating-gap`
   puts it in the release gate, and the gate's teeth were checked by mutation.

   **What remains is the better boundary.** A real device's field is bounded by a
   conductor, and a conductor is an equipotential of the very field it produces —
   so bounding an analytic element by one of its own level sets, offset to zero
   outside, would make the potential continuous *by construction*. That is what
   should replace the box, and it is not what was built.

   An analytic field has no extent, because a formula does not. That is harmless for
   an idealisation of a whole instrument — a uniform field, a retarding half-space —
   and stops being harmless the moment one is an exact statement of a real device
   sitting *next to* another. The quadro-logarithmic potential grows as `z^2`, so an
   orbital trap declared beside the C-trap that injects it puts an enormous field
   across the C-trap.

   **The cheap escape does not exist**, which is what makes this a task rather than a
   note. Declaring the analyser as solved geometry so its own domain bounds it fails:
   its electrodes are equipotentials of the field they produce, so their profile
   satisfies `-r^2/2 + Rm^2 ln(r/Rm) = A - z^2` — transcendental in `r`, invertible
   only through Lambert W — and the 2-D shape vocabulary is rectangle, disc and edge
   profile, none of which is a curve a document can name.

   What it needs is a box outside which an analytic element contributes nothing. The
   field discontinuity that introduces is not a difficulty: §11 already makes a
   declared discontinuity a first-class event and the integrator lands exactly on one.
   What needs deciding is whether the region is declared or inferred, and what happens
   where two overlap.

   **Until then both pairings are two models with a measured handover**, which is done
   and is worth having on its own — see the two entries in `docs/device-templates.md`.
   The handover is a *number*, not a hope: for the C-trap, a 60.02 ns arrival spread
   against a 3.1983 µs axial period, coherence 0.9990. For the ion processor, a
   4.220 ns turn-around against a 55.9366 µs analyser period, crossing the mirror's own
   aberration limit at 48 oscillations.

4. ~~**Finish the examples corpus (EX-1).**~~ — **met.** 37 against the thirty §5 asks
   for, and the gate (EX-2) is built and green at about 51 s. What the first seventeen
   cost was mostly *deciding what can honestly be asserted*, and that work is done. The
   three named as remaining are all shipped: `mr-tof-oscillations`, `thermalisation` and
   `parallel-plate-gap-3d`.

   Breadth beyond thirty is now an ordinary way to add a check rather than an outstanding
   deliverable — `bounded-accelerating-gap` was added the same night the region was built,
   which is the loop working as intended.

   ~~The last is deliberately deferred~~ — **`parallel-plate-gap-3d` now ships**, which
   is the deferral closed by item 4: two square plates in a cubic box, reducing to
   neither a cross-section nor an axis, reproducing `sqrt(2 d m / (q E))` to **a part in
   a million** in under two seconds. The whole gate is 27 examples in 42 s.

   **Two mistakes cost three orders of magnitude each, and both are in the example's own
   description because both are things a model author makes.** The gap in the closed
   form is between the *facing surfaces*, so placing a 1 mm plate's centre on the gap
   boundary makes the real gap 9 mm — the field came out **11.111% high**, which is
   exactly 1000/0.009. And the applied voltage has to be split as ±V/2 rather than V and
   zero, because **the grounded domain boundary is a third electrode**: holding one
   plate at zero makes the boundary an extension of it, and the problem is asymmetric
   about the mid-plane although the geometry is not. That is worth 0.31% of the field at
   the ends of the flight and 0.11% of the answer; splitting it gives 0.0005% and
   1.2e-6. **Both were mesh-converged** — identical at 1 mm and 0.5 mm cells — so
   neither was a discretisation artefact a finer grid would have removed, which is what
   the first reading assumed.

   ~~Remaining: an MR-TOF and a thermalisation.~~ **`thermalisation` now ships** —
   0.039339 eV against (3/2)kT = 0.038778 on 240 ions, 1.45% high against a 5.3%
   standard error. It needed a new figure, `meanKineticEnergy`, because equipartition is
   the sharpest check the collision models have and was measured only in a unit test.

   **Building it found two defects, both larger than the example.** A declared gas took
   no part in *any* figure of merit — the single-ion path never built a collision
   sampler — so `run` and `test` disagreed by 95 µs on every model with a gas. And
   `gas-flow-carry`'s tolerance was written as if absolute where the format compares a
   relative error, so it admitted any positive answer: **an example in the release gate
   that could not fail.** Both recorded in `docs/lessons.md`.

   ~~Remaining: an MR-TOF.~~ **`mr-tof-oscillations` now ships** — energy drift
   **7.05e-11 over fifty crossings of a declared field discontinuity**, a hundredfold
   inside ACC-4, with teeth: the drift accumulates with reflections, 1.55e-11 at one
   crossing pair against 7.05e-11 at fifty, so it is not sitting at a floor. It also
   asserts the flight time to **1.6e-13** against a closed form, deliberately — that
   number is the drift distance over the drift speed and contains nothing about the
   mirrors, which is a trap this document already records, so the example *documents*
   the decoupling rather than pretending to measure focusing. A real analyzer fixes the
   oscillation count, which the model format cannot declare.

   **Corpus 29 to 30, and the corpus can carry a data file now.** The embedded-resource
   glob was `*.json` only, so neither imported gas field — velocity or pressure — could
   appear in an example at all, and so neither could be covered by the EX-2 gate that
   runs on every change. `ExampleModels.Assets`/`WriteAssets` write an example's data
   files beside its model, under their whole file name so two examples cannot collide
   over a `pressure.vti`.

   `drift-tube-pressure-gradient` is the first: a 38 mm tube whose gas thickens from
   1 mbar at the packet to 2 mbar at the detector. **The expectation is an integral this
   engine has no part in** — the drift speed is `mu_ref n_ref E / n(x)`, so the transit
   is the integral of `n(x) dx` over `mu_ref n_ref E`, which is the uniform answer
   scaled by the *mean* density along the path; for a linear ramp that is the average of
   the ends, 1.5. Predicted 316.667 us, measured **320.236, 1.13% out** — the packet's
   own spread, matching the 0.86% the uniform drift-tube example already reports.
   Discriminating far past its 5% tolerance: ignoring the gradient gives 211 us, a third
   away.

   What it deliberately cannot see is the *arrangement* — a drift transit depends only
   on the integral along the path, so any reflection of the profile gives the same
   answer. That is a property of the physics rather than a weakness to design away, and
   it is why the arrangement is pinned separately by the reversed-ramp unit test.

   **It needed one change below the corpus, and that change is the more useful half.**
   `einzel test` could not test a model with an imported field at all: the seam between
   a study and the transport is a `Func<CompiledModel, double?>` with nowhere to put a
   path, so a figure of merit met `BackgroundGas.FromModel` and was refused. A compiled
   model now carries `SourceDirectory` — where its document was read from — set by every
   loader, so any consumer can resolve a referenced file. **Null stays the safe value**:
   a model compiled from a string has no directory and its consumer is refused rather
   than run in a gas the document does not describe, so a loader that forgets degrades
   to the refusal.

   **And the four study drivers take it too**, so a sweep, scan, optimisation or
   boundary search over a model with an imported field runs rather than refusing —
   §13's whole subject is a design being optimised, and a device with a gas jet through
   it is exactly the kind that wants optimising. The warning survives that seam: the
   ledger reports `gas.pressure-imported` with its per-evaluation count, which is what
   distinguishes a corner of the box from every draw.

   **`sequenced-uniform` closes the sequenced-extraction gap named below**, and it is
   the sharpest sequenced check the corpus has: an ion held at rest in nothing, then
   pushed by a uniform field a phase switches on. Predicted `hold + sqrt(2 d m / (q E))`
   = 5.219358580 us, measured **5.2193585800816775 - 1.6e-11 relative**, five orders
   inside its tolerance, because an analytic field has no geometry error to absorb where
   the plate version carries 1.0e-7 of fringe. It is also the corpus's only model written
   with the **model-level `sequence`** and the only one whose timeline moves an
   **analytic** element. Its teeth were measured rather than predicted: with the
   analytic-phase fix reverted it is refused at validation - "the accelerating potential
   may only be zero when a field can accelerate the ion" - so it fails before the ion is
   launched, and that one refusal guards both that defect and the fifth occurrence of the
   can-anything-accelerate check reading only one configuration.

   **All the named remaining examples are done.** The count is 32 of thirty by number,
   which is the wrong way to read it: the list said "four are breadth" and then named
   three, so what thirty means was already the open question rather than which example
   is missing. **Recommend restating EX-1's target as the coverage it wants rather than a
   number** — what is genuinely uncovered is a multipole above four rods in the
   *diffusive* mode, a sequenced extraction, and a 3-D geometry with a drive.

   The three added most recently set a pattern worth keeping. **`travelling-wave-capture`
   and `travelling-wave-ballistic` are a pair, and neither is worth much alone**: a
   transit matching the wave in one case and the injection speed in the other would be
   a coincidence twice over; a transit matching the wave *whatever* the injection speed
   is capture. And **`gas-flow-carry` is discriminating far past its ten per cent
   tolerance**, because a run that ignored the declared flow would not arrive at all —
   it would damp to rest and cover 15.8 mm in twenty milliseconds.

   Worth finishing, and worth noticing what the first tranche already returned: **two
   defects that no test written from inside the project would have caught**, because
   both were about a model that validates and answers a different question.

5. ~~**Galerkin coarsening, or operator-dependent interpolation**~~ — **built, and it
   restores the property multigrid is supposed to have.** `A_coarse = R A_fine P`: the
   coarse levels are built from the fine operator rather than from the geometry, so they
   cannot lose it. The finest level is untouched — it keeps its cut cells and its
   geometry-driven smoother, because that is where the accuracy comes from.

   On two 1 mm slabs at a 0.25 mm cell: **1 level and a 274,625-node bottom becomes 6
   levels and 27**, 45 cycles becomes 13, and 160 seconds becomes 13. **The cycle count
   stops depending on the mesh** — 14 at 65³ against 13 at 129³, where before it was 6
   against 45.

   **And it is the same answer**, which is what separates it from the fast wrong one.
   Deeper *rediscretised* coarsening was thirty times faster and gave 486 V of 100
   applied; the two hierarchies agree to 1.1e-7 to 4.0e-7 relative, the tolerance both
   were driven to.

   **Neither hierarchy dominates, so the solver picks.** Galerkin is 11.9× on the slabs,
   4.6× on four rods and **0.64× on a sphere** — a loss, because there the cheap
   hierarchy already reached a 4,913-node bottom and the 27-point stencil and the
   assembly are pure overhead. What separates the cases is the size of the bottom the
   cheap hierarchy can reach, which needs no solve to evaluate, so that is what the
   choice is made on. `SolveReport.Galerkin` says which ran.

   Still to do: the two-dimensional solver has the same seam and does not need it (it
   already reaches 9 to 99 nodes), and the 3-D corpus example is now affordable and not
   yet written.

   **The measurement that motivated it, kept because it is what made the case.** `Representable` stops coarsening once a coarse cell would exceed the smallest
   electrode dimension, and that is a *physical* size — so refinement adds levels at the
   top and never removes the bottom. The 3-D V-cycle descends **0 to 2 levels on every
   device geometry**, against 4 to 6 with no interior electrode, and the bottom level's
   node count does not fall as the mesh refines: two 1 mm slabs bottom out at **274,625
   nodes at 65³ and still 274,625 at 129³**. The shipped segmented quadrupole bottoms
   out at 9,537 nodes; the shipped 2-D templates bottom out at **9 to 99**. It is a
   three-dimensional problem, not a dimensional necessity — the two solvers use
   different coarsening rules.

   **The guard is load-bearing, and that was established by removing it.** Letting the
   0.25 mm slabs descend further takes the solve from 45 cycles and 145 seconds to 5
   cycles and 4 seconds — and to **486 V of 100 applied**. It reports converged at a
   healthy factor; only the maximum principle catches it. At four levels down a 1 mm
   slab is smaller than a cell and is pinned to a single node, so the coarse problem
   constrains the error at two isolated points where the fine problem constrains it over
   two planes. `R A P` inherits that structure through the operator instead, which is
   why Galerkin removes the guard rather than tuning it.

   `SolveReport` now carries `Levels`, `Sweeps` and `CoarsestNodes` so none of this is
   invisible again: a cycle at zero levels is four hundred sweeps over the finest grid
   and a cycle at five levels is a handful per level, and the convergence factors in
   `docs/numerics.md` were being compared across geometries as though a cycle were a
   unit of work.

6. ~~**Two narrower gaps, both stated where they bite.**~~ — **both closed.** The gas
   **density** was a single number for the whole model, so a differentially pumped
   instrument was not expressible: an imported field gave the neutrals a velocity
   everywhere and the same number of them everywhere. `pressureField` closes it — see
   item 9, which also carries the physics that was missing underneath it (mobility goes
   as 1/n) and the two tests that had no teeth until a mutation was run against them.

   ~~And the `solved3d` document form still spells one `drive`~~ — **closed.** A
   `solve3d` now takes `drives` and its electrodes take `taps`, so a volume geometry can
   express what a cross-section already could. **Shared rather than reimplemented**: both
   electrode documents implement one `ITappedElectrode` interface and the tap validation
   is one function, so the refusals for declaring both forms arrived in three dimensions
   by *being* the same code. That choice was made deliberately on the evidence of the
   same night's other finding — a computation copied across a seam is how a declared gas
   came to take part in a run and not in a figure of merit.

   Verified on a volume geometry: two generators reaching the same electrodes in the
   same proportions collapse to **one** basis solve carrying two weights on two clocks,
   and two distinct spatial patterns give **two**.

7. ~~**Class B analysis**~~ — **done.** `einzel boundary` bisects to ACC-6, the
   transmission-against-resolution curve closes onto the tabulated apex (Phase 3
   acceptance criterion 3), the **secular frequency spectrum** matches the Mathieu
   characteristic exponent to 0.007–0.144 per cent with both sidebands in place, and
   **isolation efficiency against notch width** is measured on an
   `RfWaveform.Harmonic` comb that independently recovers the published digital
   cut-off at q = 0.712.

8. ~~**A drive per supply rather than per solve**~~ — **done for 2-D.** A `solve`
   declares `drives` and each electrode `taps` them by name. The travelling-wave
   guide now carries both of its generators: 24 rings on a wave at 0.5 MHz and a
   confinement at 3 MHz reduce to **3 basis solves**, and the field reports the
   confinement's 333 ns as its shortest period rather than the wave's. **The
   confinement does not yet widen the acceptance** and the template ships with it at
   zero — the usable amplitude window is narrow at both ends and finding a working
   point is a design study; see Amendment 24.

9. ~~**A gas velocity field (GAS-1)**~~ — **both modes see one now.** VTK ImageData,
   sampled trilinearly, conserved at the face, agreeing with a declared uniform
   vector to two ulps; and the event-driven models no longer refuse it — the ion's
   position is carried into the neutral draw, so a collision samples the gas where
   the ion is. Checked against `u + μE`: the difference between a moving gas and a
   still one is **120.000 m/s against a declared 120**, with **−0.000** across it,
   and a flow field agrees with an equivalent `driftVelocity` to **1e-9** on the same
   seed.

10. ~~**A gas pressure field (GAS-1's last gap)**~~ — **done.** The density was the
   last quantity about a gas here that was a single number for a whole model, so an
   imported flow gave the neutrals a velocity everywhere and *the same number of them
   everywhere*. `pressureField` on the gas block, VTK ImageData like the velocity
   field, with **the unit required on the file** — §9's own rule, because a file read
   as pascals when it holds mbar is a gas a hundred times too thin and looks entirely
   plausible.

   The physics that had been missing: **mobility goes as the reciprocal of density**,
   and nothing here did that. μN is the constant, which is why the literature
   tabulates *reduced* mobility; the declared `pressure` becomes the reference the
   declared or derived mobility belongs to, and the field grades away from it. There
   are two separate density dependences and they are not the same one — this factor
   is how *much* gas, the existing E/N expansion is how hard the ion is pushed
   *between* collisions.

   **A graded density turns Langevin into a null-collision method**, which is the
   same mechanism hard spheres already used for a speed-dependent rate, reached a
   second way: schedule at the highest density anywhere, accept with probability
   n(x)/n_max. Both bounds are now majorants over the whole field, because an event
   is scheduled before it is known where the ion will be when it lands. The thinning
   is short-circuited where the density is uniform, and that is load-bearing rather
   than an optimisation: it would otherwise accept with probability exactly one and
   *still consume a random draw*, moving every seeded result this engine has
   published.

   | | |
   | --- | --- |
   | A field at 2× the declared pressure vs *declaring* 2× the pressure, event-driven | **bit-identical trajectory**, both collision models |
   | The same, through the diffusive path and the CLI | 3515.229021382981**5** vs **1** µs |
   | Mobility at half and twice the reference density | 2.000000 / 0.500000 |
   | The scaled form at the declared density | **bit-identical** to the unscaled one |
   | Langevin thinning at three points of a 4× ramp | 0.25 / 0.625 / 1.00 to 0.01 |
   | A field in mbar vs the same field in Pa | 1e-9 |
   | 151 existing transport tests | unchanged |

   **Two tests that had no teeth, found by running the mutation.** The equivalence
   test used Langevin only, and a mutation making the local density read return the
   declared scalar *did not fail it* — the Langevin branch short-circuits its thinning
   where the density is uniform, so a flat imported field never reads a position at
   all. Correct behaviour, and no test of the read. And the graded-gas test asserted
   only that a ramp collides more than the thin gas alone, which with the density read
   at the wrong place still passes because the count lands *close to* the thin gas.
   What discriminates is **reversing the ramp** — same densities, same box, opposite
   arrangement, so anything blind to position gives the two an identical count.
   11,458 against 19,700.

   **And the same seam broke a fourth time, in the file whose comment says it is the
   third.** `SampledOutsideDensity` was added to `CollisionSampler` beside
   `SampledOutsideFlow`, and on the first draft was dropped in exactly the place the
   surrounding comment warns about — declared, set, read by nothing, everything
   compiling and every test passing while a run extrapolating its gas past the imported
   box said so nowhere. Reading the comment is not the same as being protected by it.
   Now `gas.pressure-extrapolated`, with a CLI test that drives it end to end, because
   the wiring is what keeps breaking rather than the computation. The rule needs a
   second half: **adding a quantity to a type that already reports several is not the
   same as reporting it**, and the existing reporting code is exactly where the eye
   slides past.

   **The cost gate had to be re-derived, and the first version was 50% out.** GRD-8's
   claim for this mode is that `estimate` and `run` call the same step function and
   agree exactly. A graded gas moves the mobility and so both stability limits — and
   the first version took the thinnest gas *anywhere in the imported field* where the
   run takes its limit from per-node arrays *over the tracked grid*. A CFD field is
   usually solved on a larger box than the ions are tracked through: here it ran to
   0.5 mbar while the grid reached 0.75, and the estimate said **2,252 steps against
   an actual 1,502**. Now 1,126/1,126 uniform and 1,502/1,502 graded. Found by
   comparing the two numbers, not by reading the code.

   The same asymmetry runs through the diagnostics and had to be got right in both
   directions: **E/N is worst where the gas is thinnest**, the **Knudsen number and
   collision counts are worst where it is thickest**, and reading the declared
   pressure for either reports a regime the instrument is in nowhere.

   **A refusal moved to where it cannot be forgotten.** Resolving a declared field
   needs the model document's directory, which a study or a figure of merit does not
   have; the rule to refuse rather than run in a gas the document does not describe
   was right, but lived as a guard at each of four call sites *naming `velocityField`*
   — and three were already silent about a pressure field.
   `BackgroundGas.FromModel` now refuses an unresolved field itself, with
   `WithoutImportedFields` as the deliberate exception whose name says what it gives
   up. It immediately caught a real one: `einzel run` on a diffusive model reached
   `FromModel` through `GasFlowImport.Resolve` itself. Two call sites that *did* have
   the path — `einzel compare` and the diffusive cost estimate — now resolve rather
   than refuse, which they should always have done.

   **Still assumed: one temperature.** What is imported is a pressure field read as a
   density field at the model's single declared temperature. That assumption was
   already made by there being one `temperature` in the document, but it is now the
   only thing about the gas that cannot vary from place to place.

11. ~~**The live session (MCP-1)**~~ - **done, and the work was not the protocol.**
    `journal`, `undo` and `attribution` existed only in the `Einzel.Commands`
    assembly *description string* - the same "named in a csproj and nowhere else"
    state `ITransportMode` was in before its seam was built. So "build MCP" was
    really "build the journal, then put a protocol on it", and §15 says as much: the
    server's distinct value is shared live state, and "everything else it could do,
    the CLI does at least as well and with less machinery". A journal only one party
    can write to is a file, and a file needs no server.

    **Attribution comes from the `initialize` handshake, not from a tool parameter.**
    An `author` argument would make the attribution something the *mutating party
    fills in*, which is a signature rather than an attribution - an agent could sign
    a change as the person it is working with, by mistake or because a model decided
    that read better. The client declares itself once, before any tool exists to
    call. The test is in two halves and the second is what makes the first a property
    rather than a default: the name that comes back is `agent:surveyor/3.1`, **and**
    `model_edit`'s schema has exactly `description` and `content`, so there was no
    argument through which another could have been offered. A tool that took an
    author and ignored it would pass the first half alone.

    **Shared and linear are two claims.** Shared means one stack rather than one per
    party, which is the point rather than a hazard: two private stacks over one
    document would let each party reverse changes the other had already built on, and
    the document would reach a state neither of them authored. Linear falls out of the
    walk back being over ordinary edits only. And **undo appends rather than pops**,
    because a popping stack loses the fact that somebody undid something, and who -
    which is exactly what MCP-1 asks to be recorded.

    All three checked by mutation rather than by assertion count: private per-author
    stacks fail three of six journal tests, a popping undo fails a *different* two,
    and moving attribution into a tool parameter fails three of five protocol tests.

    **The tool surface is deliberately not a second CLI**, and the server says so in
    its own instructions - the failure to guard against is an agent looking for `run`
    and `sweep`, not finding them, and concluding the platform cannot do those things.
    Every result is `CommandJson.Write` of the same outcome record the CLI serialises
    for `--json`, asserted **byte for byte**, which makes AGT-2 literal instead of
    claimed and carries GRD-2 for free: a warning reaches an MCP client by being on
    the record rather than by anyone remembering to copy it across. That is the seam
    this project has already dropped evidence at three times.

    **What remains needs the shell.** §15 makes streamable HTTP hosted in process the
    primary transport and stdio "a convenience" - the right ordering for a finished
    platform and the wrong one to build in, since the convenience runs today. The
    tools are built above the transport, so adding HTTP is a wrapper. A full `run` is
    also held back deliberately: it belongs where there is a progress surface and a
    viewport to put the answer in, and `einzel run` is one process launch away
    meanwhile. The shell now exists and has a viewport, so that holds back nothing but
    itself.

    **The first non-test dependency the project has taken**, and §20's table asks for
    the licence to be verified rather than assumed: `ModelContextProtocol.Core` 2.2.0
    declares Apache-2.0 as an SPDX expression in its own nuspec, and its whole
    transitive closure is ten `Microsoft.Extensions.*` packages, all MIT. LIC-1 clear.

12. **The shell (§16).** **Six of the eleven views exist** — the table in
    [the shell section](#the-shell-and-the-rest-of-16) is the current one; this entry
    said three for a while after it stopped being true. The window opens on a model, and
    what remains divides into three kinds rather than one:

    - **Presentation over something that already works** — the sequence editor shows a
      timeline and does not edit it, and the animation timeline has per-phase rates and
      frame export but no scrubbing. Both have the path underneath them; what is missing
      is the input surface. The figure composer is the same shape and can be built last,
      since `RenderSpec` is already text the CLI executes.
    - **A view with a requirement behind it.** The extension manager is the only one that
      would retire a tagged requirement rather than present an existing capability:
      **LIC-2 asks that extensions carry their own licences and the manager surface
      them, and nothing surfaces them.** That makes it the sixth instance of the pattern
      §16 has produced five times already — a view that cannot be built until the command
      layer gains something — and the one with the most engine-side content left.
    - **A view that needs a whole assembly.** The update notice needs §18 and
      `Einzel.Update`, which does not exist.

    **Twice a view could not be built until a command existed, and both times the
    command layer gained the capability.** The model tree needed `einzel outline`,
    because a window that parsed the document to build a tree would grow its own idea of
    what a model is; the viewport needed `ViewportCommand`, because one that integrated
    its own trajectories would be a second transport implementation. That is Amendment
    25 running in the direction it was not designed for — the window pulling capability
    *into* the command layer rather than accumulating it privately — and it is the
    strongest evidence so far that AGT-2 is real rather than aspirational.

    **The viewport's own finding is about the colour scale.** §16 asks for bundles
    coloured by energy, and a scale taken per path gives every ion the same colours
    whatever its energy — two ions a kilovolt apart look identical and the picture says
    they were the same. The range is therefore reported by the command over the whole
    bundle. It is the same failure the animation's contour levels had in the other axis,
    where anchoring per frame made a film of a spreading packet show a packet doing
    nothing. The discriminating test is not that the range is wider than the widest
    single path — that margin is 1.5e-5 on a packet launched from rest — but that **no
    single path owns both ends of the scale**, which any per-path anchoring fails
    whatever the magnitudes are.

    **RND-8 is on the face of the window**, asked of `ITransportMode.ProducesTrajectories`
    rather than of the pressure: a diffusive model draws no paths and says what it has
    instead, because an empty viewport and one whose ions were all lost look identical
    and only one of them is a statement about the physics.

    **Two open bets are now settled by measurement rather than argument.** The whole
    solution **builds on Linux, XAML markup compilation included** — `EnableWindowsTargeting`
    is enough — and `Einzel.Wpf.Tests`, the one Windows-only test project, is walked past
    by a solution-wide `dotnet test` there. 848 tests on Windows, 843 on Linux, both green.

    **And two things the third surface cost.** WPF cannot run in globalization-invariant
    mode, which the whole solution sets for CLI-5; the shell reverses it, and what that
    setting protected is unaffected because every formatting and parsing site passes
    `CultureInfo.InvariantCulture` explicitly (Amendment 26). And Helix Toolkit's DirectX
    backend is SharpDX, **archived since December 2020** — taken knowingly, because §17
    confines this path to screen tuning and nothing that leaves Einzel passes through it.

    **The viewport now draws the instrument and the field**, not only the ions. Every
    conductor is the zero level set of its own signed distance, so one routine draws them
    all and a shape added to the format needs no change (invariant 2); what differs between
    symmetries is what the solve claims about the third dimension, which is why a
    cross-section extrudes, an axisymmetric half-plane revolves, and a volume is extracted.
    The mesh maths is in `Einzel.Render` and its tests run on Linux, checked against a
    sphere's area and volume, Pappus, and watertightness rather than against how it looks.

    **The geometry found a defect in the core and a gap in the corpus.** A 1 mm plate is
    thinner than a cell of a 48-cell grid over the whole solve domain, so the
    three-dimensional example produced **no conductors at all**, silently - fixed by asking
    the electrode for its bounds, which needed `CompiledElectrode3D.Bounds` beside `Centre`
    and `CharacteristicSize` because switching on the shape is what invariant 2 forbids. And
    **no diffusive example declares a geometry**, so the claim that RND-8 withholds the
    paths and not the instrument is exercised through the field rather than through
    conductors - a pointed gap, since the device that mode exists for is a funnel.

    **Three more views since**, each of which needed the command layer to gain something
    first: results by §12's accuracy class (which found Amendment 28's GRD-1 hole), the
    regime inspector (REG-2's numbers *along* the path rather than at the worst point
    anywhere, so "outside validity" becomes "between 12 and 31 millimetres"), and the
    sequence editor (the declared timeline, marked with what each phase moves).

    **One gap closed since**: the conductor surfaces can be exported (`einzel export --mesh`),
    so a three-dimensional geometry can be rendered without Windows — and doing it found that
    the viewport itself was drawing none of the Astral's sixteen stripes, because an
    electrode's own bounding box can be as badly proportioned as the solve domain was.

    **Next, in order of what unblocks the most:** the density cloud, which needs only a
    surface since the density is already computed and contoured; the figure composer, whose
    seam is already text the CLI executes; then the animation timeline's scrubbing. The
    update notice needs `Einzel.Update`, which does not exist.

13. **The Astral inverse problem, and the drift reversal is now reproduced.** This item has
    been rewritten twice. Both earlier versions attributed the gap between this model and the
    published instrument to the four unknown electrode depths, and both were wrong; the
    reasoning is kept in the handoff because the wrong turns are instructive, and the
    superseded parts are marked there.

    **Reproduced, from the shipped template, with no ion foil contribution:**

    | | published | measured |
    | --- | --- | --- |
    | drift distance | 310 to 360 mm | **334.61 mm** |
    | reflections outbound | 24 to 26 | **25** |
    | drift per reflection | 13.40 mm | **13.38 mm** |

    **It is not a fit with spare parameters.** `D/N = t_r V tan(theta) / 2` contains no
    convergence term, so the published drift distance and oscillation count fix the injection
    angle on their own; the convergence then follows from either one alone, and the third
    number checks. Two unknowns, two equations, one prediction.

    **What had been wrong, in order, and each caught by its own control.**

    The tilt was invisible to the solver *and* its sign was inverted, two errors whose product
    looked like the mechanism for weeks. See Amendment 36: the anisotropy `Ez/Ex` = 2.9e-4 is
    fourteen times below the field error of any affordable mesh, so the solved answer ranged
    over 3.54, 0.011 and -0.57 of the closed form on gap width alone. Rotating a
    two-dimensional cross-section instead is exact at 5.4e-20, and makes each mirror a 2-D
    solve.

    **`N = alpha L / (eta c)` and `eta = 0.578` are withdrawn.** The drift impulse of one
    reflection has a closed form - `Delta v_z = V sin(2 alpha)` exactly, from three
    conservation facts - and it is **independent of the electrode design**, so eta is
    identically 1 and there was never anything for the depths to move. Confirmed against the
    integrator at 1.000000000 across three tilts and unchanged by an eightfold change of
    mirror gradient. **That forbids the whole plan of fitting `d1..d4` against the reversal.**

    **`capToCap` = 625 mm was derived wrongly** - the turning-point separation mistaken for
    the cap-to-cap distance. The ion turns 84.2 mm past the mirror mouth and that offset is
    independent of `capToCap`, so the published 24 oscillations over 30 m gives 716.6 mm.

    **And the foil does not reverse the drift.** An interim conclusion had it supplying 58 to
    69 per cent of the returning impulse; the detector paper (J. Am. Soc. Mass Spectrom.
    2024;35:2390) says the tilt does it alone and the foil "counter[s] ToF aberrations
    induced by the converging ion mirrors". The apparent deficit was a convergence too small
    by 2.8x.

    **The one number to question now is `tiltBaseline`.** The published quantity is a 200
    micron spacer; what it tilts *over* is not published. Read as the gap closing across the
    350 mm drift it gives 714.81 mm of drift and 61 reflections; read as 200 microns per
    mirror over a ~250 mm baseline it gives the table above. The template declares the two
    apart so the uncertainty sits where it belongs.

    **The full track now flies end to end, and the resolving power has been located to one
    term.** One ion at the shipped injection angle goes out to a 334.6 mm reversal and back in
    25 oscillations, 31.27 m, 853.7 µs bare and 800.4 µs with the foil, against a published
    24-26, 310-360 mm, ~30 m and ~779 µs - every geometric register number on one flight.
    Two mechanisms were then measured separately on that track and both do what the papers say.
    **The tilt** gives a return time exactly proportional to sideways speed (ratio +0.99), a
    constant force and not an isochronous well. **The published foil** - the contoured plate at
    a *uniform* voltage, not the graded ramp this model first tried - cancels that first-order
    dependence 22-fold at -3 V inside the published 0 to -20 V window, overshoots at -20 V, and
    breaks the instrument sign-reversed; the flight time there lands on the published value to
    3%. The template ships that arrangement. What the foil leaves is second order (-6.5), which
    is the well's shape not being harmonic - the subject of Grinfeld, Stewart, Makarov,
    *Int. J. Mass Spectrom.* 2024, 1060, 169017, still the paper to read first.

    **The mirrors are measured and understood, and were not the instrument's limit.** The
    focusing coefficients `c1..c3` are figures of merit now, and the scaling law is the check:
    `R x s` constant means first-order limited (shipped depths, 21.5), `R x s^2` constant means
    second-order (one depth moved, 1.2), each cancelled order buying one power of spread. **A
    mirror is focused *for* a drift length** (`c1` grows 7.7-fold with the free path), so it
    must be measured on the instrument's own per-oscillation path - and it carries to the full
    track exactly, `c1` = -0.012 there against 0.012 on a half oscillation. **The foil adds
    `c1` = -0.231 on its own** and is the entire gap between the half-oscillation R of 36,500
    and the full track's 60-70.

    **Both ways out of that were measured and both fail as parameterised.** A 2x2 Jacobian on
    the full track puts the foil's two shape knobs **9.6 degrees from exactly opposed**, so the
    device's speed-isochronising benefit and its energy defect are very nearly the same
    quantity and shrinking one surrenders the other; the simultaneous zero asks for a plate
    extending past the mirror mouth. And the mirrors cannot supply the cancellation: `|c1|`
    ranges only 0.003 to 0.087 across `d2`, a third of what is needed, with `TE1` making up the
    rest only at a `c2` cost that climbs monotonically.

    **The one unfitted published number became a constraint on the geometry.** The
    crowd-control paper defines `(t|e) = T^-1 dT/d(epsilon)` outright and states the 4 keV
    beam, so the measured `dc1/dTE1` = 2.2 against a published 1.0 is neither a units error nor
    a definitional half - it says the depths are wrong, and `d2` is the only depth that
    controls it (`d1` is inert, `d3` and `d4` move it destructively). `d2` = 38.0 mm reproduces
    it to 2 per cent against a guess of 50.

    **And then the constraint no half-oscillation measurement can see.** At `d2` = 38 the ion
    **strikes the board at y = -20 mm after 24 of 50 reflections** - the mirror does not confine
    transversely over the track. Every coefficient above was measured on one reflection, where
    an ion has no time to walk off axis; each is correct for what it measures and none can say
    whether a geometry is flyable. That invalidated an earlier `c1_foil` comparison which had
    been timing an ion hitting a rod, and whose tell was a bare-tilt speed ratio of 1.95 where
    the closed form requires exactly 1.0. `d3` = 84 mm is the only rescue found: **20/38/84/130
    flies the full 50 reflections**, and whether it still meets the published sensitivity and
    what it does to `c1_foil` is the measurement in flight. Two floors stated rather than
    hidden: `c1` has a mesh floor of +/-0.0015 from how the strips sit on the lattice, and the
    `d2` constraint carries the paper's own "about" at +2.5/-3 mm. Handoff sections 18-28.

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
