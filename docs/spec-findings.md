# Findings against the specification

Building the platform surfaced places where the specification is incomplete,
where a stated mechanism does not do what it was expected to, or where a
constraint turned out to bind differently than written. Each is recorded here
with the evidence, so the specification can be argued with rather than quietly
diverged from.

None of these are complaints. The spec was written before any of it existed, and
says so.

---

## SYM-1 should name translational invariance

**Spec §9** lists the symmetries a geometry subtree may declare: cylindrical, a
mirror plane, or discrete periodicity.

The first real geometry needed none of those. A printed-circuit ion mirror is
stripe electrodes running along the drift direction, so the potential is
genuinely independent of that direction and the problem reduces exactly to two
dimensions. That is **translational invariance**, and it is not in the list.

It is not a minor omission: it removes an entire dimension from the solve, which
is the difference between a 513 × 513 grid and a 513³ one. Recommend adding it to
SYM-1.

---

## The turning-point step cap does not do what §11 expects

**Spec §11**: *"Turning points get forced step refinement. The velocity minimum
inside a mirror is where relative timing error is largest and where position-error
controllers under-refine."*

The mechanism is implemented. It does not help, and it slightly hurts.

In a smooth field with a turning point and no discontinuity, the flight time
reaches machine precision in **6 steps with the cap off** and is marginally
*worse* with it on at 105 steps. Measured on a uniform-field turnaround, where
the closed form is exact:

| Configuration | Steps | Relative error |
| --- | --- | --- |
| Cap off | 6 | 8.3e-16 |
| Cap at 0.01 | 105 | 1.3e-15 |

The rationale in §11 is sound for the controller it describes. Ours is not that
controller: `ErrorNorm` weights velocity error with its own absolute floor, so it
refines the turnaround on its own without being told to. The requirement's *goal*
is met; its prescribed *mechanism* is redundant here.

The cap remains implemented and on by default, so the code honours the spec as
written. The evidence says `TurningPointStepFactor` should default to 0, and that
§11 should be reworded to state the goal — the turning point must be resolved —
rather than the mechanism.

---

## §11 is missing two step constraints that turned out to be necessary

Neither appears in the specification, and without either the integrator produces
confidently wrong answers.

**A step may not outrun the field's resolution.** A gridded field carries no
information below its node spacing. Launch an ion in a field-free region, and the
local acceleration is near zero, so the step heuristic proposes an enormous step
and the embedded error estimator *correctly* certifies it — for a straight line.
The ion then flies through the entire instrument without sampling it. Observed:
39 metres in one step, through both mirrors of a pair.

**A step may not straddle a declared field discontinuity.** Dormand–Prince stage 4
carries the coefficient −56/15, so intermediate stage samples fall outside the
step interval and can land on the wrong side of a jump even when both endpoints
are inside. Handling boundaries as events took a reflectron from 5.5e-10 to
1.7e-16.

Recommend both as requirements alongside the existing step controls.

---

## ACC-3's prohibition is correct, and now has a number

**Spec §8** forbids trilinear interpolation anywhere on a trajectory path and
requires tricubic with continuous first derivatives as the minimum.

This is the least intuitive numerical claim in the document and it is right. Flying
the same trajectory through an exactly-sampled field, varying only the
interpolant, bilinear on a 64-interval grid contributes **9.4e-6** to the flight
time: nineteen times over the entire ACC-1 budget, not merely over the
interpolation share of it. Bicubic on the same grid contributes 6.4e-8.

Worth adding to §8 as evidence, because the prohibition reads as overcautious
until someone measures it.

---

## LIB-1's test caught the right thing

**Spec §14**: *"If supporting a new device requires a change below
Einzel.Library, either it is genuinely novel physics or the abstraction is wrong.
Almost always the second."*

The first implementation of a mirror was a C# class, and it worked. Applying
LIB-1's test — what would a quadrupole need? — showed the abstraction was wrong,
because a quadrupole would have needed a second class. Reworking geometry into
document data made a quadrupole four discs in a JSON file sharing no code with
the mirror.

The requirement earned its keep as a design check, not as documentation.

---

## Multigrid does not coarsen safely with interior electrodes

**Spec §10** chooses finite-difference multigrid for Phase 1, on the grounds that
it is straightforward to implement correctly. That holds for boundary-value
geometries and **not** for interior electrodes — a rod, an aperture — which every
device except a planar mirror has.

An electrode occupies a fixed physical size, so each coarsening halves how many
nodes represent it and past a few levels it is not represented at all. The coarse
grid then solves a different problem and its correction drives the fine iteration
apart: four discs in a box reached 1e134 V.

Mitigated by limiting coarsening while interior electrodes are present; not
solved. A real fix is Galerkin coarsening or operator-dependent interpolation.
Recommend §10 note it, because "straightforward to implement correctly" is true
of the textbook case and misleading about the general one.

---

## TRJ-1 is met in one direction only

**Spec §11**: *"Trajectory output for rendering is a separately sampled stream
with its own cadence, independent of integration steps."*

The stream can be **coarser** than the integration steps but never **finer**.
Samples are offered at accepted steps and at the ends of analytic advances, so
asking for a 1 ns cadence across a region the integrator crosses in 50 ns steps
yields samples every 50 ns.

Full independence needs dense output — the interpolating polynomial
Dormand–Prince can evaluate anywhere inside a step — which is not implemented.
In practice the gap is narrow: where steps are long the motion is either
field-free, and advanced exactly as a straight line needing only its endpoints, or
smooth enough that the controller had no reason to refine.

---

## The cross-code validation tier is unavailable

**Spec §19** lists curated geometries with SIMION results as golden files, and
**§22** names validation against SIMION as historically the largest overrun in
this class of project.

No SIMION licence is available — its cost is part of why this project exists — so
that tier cannot be run, and the §22 risk does not apply. What carries the load
instead: the analytic tier becomes the primary reference, literature regression is
promoted to the main external check, and agreement between an analytic path and a
solved path substitutes for agreement between two codes. For the field solver
specifically, a free FEM code out of process (Elmer, FEniCS, deal.II) is the
practical independent check.

Recommend §19 and §22 be rewritten around that, since the schedule implication is
favourable and the validation implication is not.

---

## FLD-1 sensitivity fields do not work on a rasterised boundary

**Spec §10** caches the partial derivative of potential with respect to each
perturbation channel by finite difference over a full re-solve, then builds every
perturbed geometry by superposition. **FLD-2** gates the study on a stratified
validation subset: if the residual exceeds ACC-1 the sweep is void. **§23**
recommends spiking the linearity assumption before Phase 2 commits, at an
estimated two weeks.

The spike was run. **It fails, and not for the reason the specification
anticipates.**

Measured on a plate inside a fixed domain with 0.469 mm cells, at a nominal
position of 40 mm:

| half-width | % of nominal | cells moved | potential residual | within 1 ppm |
| --- | --- | --- | --- | --- |
| 0.10 mm | 0.25% | 0.2 | **0.000E+000** | "yes" |
| 0.50 mm | 1.25% | 1.1 | 1.93e-2 | no |
| 2.50 mm | 6.25% | 5.3 | 5.62e-2 | no |

Two failures, and the first is worse than the second.

**Below one cell, the perturbation is invisible.** An electrode is rasterised
onto grid nodes, so moving it less than a cell changes which nodes it occupies
not at all. The perturbed solve returns bit-identical to the nominal, the
difference is exactly zero, and the derivative field is identically zero. A
tolerance study built on that reports the parameter as having **no influence** —
the opposite of the truth, arrived at silently. And sub-cell is exactly where
machining tolerances live: the memo's channels are 100 to 300 µm.

**Above one cell, the residual is percent-level** — four orders over the ACC-1
budget FLD-2 gates on. The premise §10 argues from is that 100–300 µm against a
10 mm standoff is a 1–3% perturbation and therefore linear. The *physics* is
plausibly linear there. The **discretisation is not**: a rasterised boundary
moves in steps, so the discrete operator is a staircase function of the
parameter, and its finite difference measures the staircase.

So the assumption that fails is not "the field responds linearly to a small
geometry change". It is the unstated one underneath: *that the discrete problem
varies smoothly with the geometry parameter at all.*

### What this changes

FLD-1 as written cannot support a geometry tolerance study on a staircase mesh,
at any perturbation size: too small and it reports zero, large enough and it
reports the rasterisation. That is not a tuning problem with a step size in
between — the two failure modes meet.

Three routes, none of them small:

- **Body-fitted or deformable mesh**, so the boundary moves continuously with the
  parameter and the discrete operator moves with it.
- **Cut-cell or immersed-boundary discretisation**, where a boundary between
  nodes is represented sub-cell, so the operator varies smoothly with its
  position. This is the smallest change that keeps the Cartesian multigrid.
- **Analytic shape derivatives**, which sidesteps finite differencing entirely
  and is the largest change.

Recommend §10 and §23 be rewritten around this. The two-week spike was the right
call and it returned a negative result, which is what a spike is for.

**In the meantime the guard is an error, not a warning.** A channel whose
perturbation leaves the rasterised geometry unchanged is refused outright, because
there is no correct number to return and silently reporting zero sensitivity is
the single most damaging thing this code could do.

Note the contrast that makes the diagnosis clean: a **voltage** channel linearises
to 1.5e-14, and superposition reproduces a re-solve to 2.3e-11 V on 1150 V. The
machinery is right. It is the moving boundary that the mesh cannot represent.

---

## Interior electrodes make sensitivity campaigns expensive

A consequence of the coarsening limitation above, and it did not become visible
until sweeps existed. FLD-1's economic argument is that one solve campaign of
`2 × channels + 1` solves replaces a thousand. That holds only if each solve is
cheap — and on interior-electrode geometry the coarsening limit leaves few
multigrid levels, so it is not. A campaign over a 513 × 257 grid **brought down
the test host**.

Measured against it: 500 linearised draws cost 25 ms where a single solve costs
142 ms, so the superposition side of the argument is sound. The campaign side is
what needs the solver fixed.

This raises the priority of Galerkin coarsening from a tidy-up to a prerequisite:
geometry sweeps are not usable on real devices without it.

---

## Notes on the companion memo

**Memo §6 item 1** asks for the six-oscillation mirror pair at 20,000 across
±3–5% energy acceptance. Modelled result: a single-stage pair tuned to first-order
focus reaches R = 8,347 at ±3%, short by 2.4×, with a second-order coefficient of
0.130 binding it. A two-stage profile cancels that term and clears the target.

Two qualifications belong with that number. It is **energy-aberration only** — no
spatial or angular spread, no turn-around time, no detector response, no space
charge — so it says energy spread stops being the limiting aberration, not that
the instrument reaches that figure. And second-order focus **costs envelope**: 767
mm cap-to-cap with 1378 mm of drift, against the shoebox the memo argues for.

**Memo §4's four-penetration-depth rule is wrong by 10 mm** at this geometry.
First-order focus lands at 290.4 mm, not the 300.0 mm the rule predicts, because
the fringe field shifts it. Small, but it is exactly the sort of discrepancy that
would appear on a built instrument as unexplained resolution loss.
