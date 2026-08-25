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

## Multigrid with interior electrodes needs more than §10 implies

**Spec §10** chooses finite-difference multigrid for Phase 1, on the grounds that
it is straightforward to implement correctly. That holds for boundary-value
geometries and **not** for interior electrodes — a rod, an aperture — which every
device except a planar mirror has.

An electrode occupies a fixed physical size, so each coarsening halved how many
nodes represented it and past a few levels it was not represented at all. The
coarse grid then solved a different problem and its correction drove the fine
iteration apart: four discs in a box reached 1e134 V. Limiting the coarsening
kept it stable at the cost of the thing multigrid is for — two levels and a
convergence factor of 0.55, which is smoothing wearing a V-cycle.

**Resolved by cut cells**, which is not the fix §10 or the textbooks point at
(Galerkin coarsening, operator-dependent interpolation) but is the one that
turned out to matter. A sub-cell surface has a position at any spacing, so the
coarse mask can be rebuilt from the geometry instead of projected down from the
fine one, and an electrode too small to contain a coarse node still cuts the
links around it. The same geometry now runs 7–8 cycles at 0.019–0.023, flat under
refinement.

A second, unrelated defect was hiding behind the same limit: a Dirichlet domain
edge was implemented as a ghost node one cell outside the grid, so the boundary
moved outward at every level and a cap plate in a grounded box diverged to 1e50 V
once coarsening was allowed to proceed. Grounding the edge node itself fixes it.

Recommend §10 note that a Cartesian multigrid solver needs a boundary
representation that survives coarsening, because "straightforward to implement
correctly" is true of the textbook case and misleading about the general one.

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

## FLD-1 rests on an assumption the specification does not state

**Spec §10** caches the partial derivative of potential with respect to each
perturbation channel by finite difference over a full re-solve, then builds every
perturbed geometry by superposition. **FLD-2** gates the study on a stratified
validation subset: if the residual exceeds ACC-1 the sweep is void. **§23**
recommends spiking the linearity assumption before Phase 2 commits, at an
estimated two weeks.

The spike was run twice. The first run failed, and not for the reason §10
anticipates; the second, after the discretisation was changed, passes.

### What failed

Measured on a plate inside a fixed domain with a 0.94 mm mesh, at a nominal
position of 40 mm, with the boundary rasterised onto nodes:

| half-width | % of nominal | cells moved | potential residual | within 1 ppm |
| --- | --- | --- | --- | --- |
| 0.20 mm | 0.50% | 0.2 | **0.000E+000** | "yes" |
| 2.50 mm | 6.25% | 2.7 | 5.37e-2 | no |
| 7.50 mm | 18.75% | 8.0 | 1.03e-1 | no |

Two failures, and the first is worse than the second. Below one cell the
perturbation was **invisible**: moving an electrode less than a cell changed
which nodes it occupied not at all, the perturbed solve returned bit-identical to
the nominal, and the derivative field was identically zero. A tolerance study
built on that reports the parameter as having **no influence** — the opposite of
the truth, arrived at silently. Sub-cell is exactly where machining tolerances
live. Above one cell the residual was percent-level, four orders over the budget
FLD-2 gates on. There was no step size in between: the two failure modes met.

The premise §10 argues from is that 100–300 µm against a 10 mm standoff is a
1–3% perturbation and therefore linear. The *physics* is linear there. The
**discretisation was not**. So the assumption that failed is not "the field
responds linearly to a small geometry change" — it is the unstated one
underneath: *that the discrete problem varies smoothly with the geometry
parameter at all.*

### What fixed it

A cut-cell (Shortley–Weller) discretisation, which was the smallest of the three
routes and the only one that keeps the Cartesian multigrid. Same fixture, same
mesh, boundary now placed sub-cell:

| half-width | δ/L | potential residual | ratio to previous |
| --- | --- | --- | --- |
| 0.05 mm | 1.3e-3 | 1.11e-6 | |
| 0.10 mm | 2.6e-3 | 4.46e-6 | 4.00 |
| 0.20 mm | 5.1e-3 | 1.79e-5 | 4.01 |
| 0.40 mm | 1.0e-2 | 7.16e-5 | 4.01 |

That is an ordinary Taylor remainder: quadratic in the perturbation, to three
figures, over an order of magnitude in step size. The potential in the gap goes
as 1/L, so the second-order term is (δ/L)²; at the largest step the closed form
gives 1.05e-4 against a measured 7.16e-5, which is the right size and correctly
a little under.

The shape derivative itself is now right rather than merely non-zero. At a step
of **0.11 cells** — previously literally invisible — dV/dL matches its closed
form −1000·x/L² to **6.5e-6** relative across forty probe points.

### What this changes for the specification

FLD-1 is usable, with a stated and predictable limit rather than an unstated one.
The linearisation error is (δ/L)², so:

- 1 ppm holds out to δ/L ≈ 10⁻³ — about 40 µm on a 39 mm standoff.
- A 100 µm tolerance linearises to ≈ 7e-6; 300 µm to ≈ 6e-5.

So the memo's 100–300 µm channels do **not** meet a 1 ppm gate, and FLD-2 will
correctly refuse them. That is now a legible engineering trade with a formula
attached — refine the mesh, narrow the channel, or accept a 1e-5 linearisation —
rather than a discretisation artefact that could not be reasoned about.

Recommend §10 state the discretisation requirement explicitly: sensitivity fields
need a boundary representation that varies continuously with the parameter, and
a node-by-node one does not. Recommend §23 record that the two-week spike was the
right call: it returned a negative result first, which is what a spike is for.

Note the contrast that made the diagnosis clean throughout: a **voltage** channel
linearises to 1.5e-14, and superposition reproduces a re-solve to 6.4e-12 V on
1150 V. The machinery was always right. It was the moving boundary the mesh could
not represent.

---

## Interior electrodes made sensitivity campaigns expensive

A consequence of the coarsening limitation above, and it did not become visible
until sweeps existed. FLD-1's economic argument is that one solve campaign of
`2 × channels + 1` solves replaces a thousand. That holds only if each solve is
cheap — and while the coarsening limit was in force it was not. A campaign over a
513 × 257 grid **brought down the test host**.

Measured against it: 500 linearised draws cost 25 ms where a single solve cost
142 ms, so the superposition side of the argument was always sound. It was the
campaign side that needed the solver fixed, and cut cells fixed it — the
`Einzel.Sweeps` suite went from 2 m 15 s to 4 s.

Worth keeping as a record of the shape of the mistake: an economic argument that
looks like it is about algorithms can rest entirely on a numerical property
nobody wrote down.

---

## The solve domain is not the domain that was declared

`Grid2D.OverBox` takes the interval count along x and derives the count along y
by rounding to a power of two, so both directions coarsen together. The spacing
comes from x. Nothing constrains the result to land on the declared `maxY`, and
nothing reports that it did not.

The overshoot depends entirely on the aspect ratio. The shipped templates are
fine — `quadrupole` is exact, `planar-mirror-pair` is 0.4 mm short on a 0.92 mm
cell, well under half a cell. But a 60 × 20 mm box at a 1 mm cell needs 21.3
intervals in y, rounds to 32, and **solves a 60 × 30 mm box**: fifty per cent
taller than asked for, silently.

That is not a rounding detail, it is a different problem. It was found because a
test fixture's plate was declared to span the domain in y and then did not, so
the field went round the end of it and a closed form that should have applied did
not. The physics changed and nothing said so.

Recommend the model format either refuse a box whose aspect ratio cannot be
meshed with square cells at power-of-two counts — with the achievable cell sizes
named, in the AGT-3 style — or the grid gain independent spacings per axis, which
the Shortley–Weller stencil already supports since it carries a spacing per arm.
The second is more work and the better answer.

**Not yet fixed.**

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
