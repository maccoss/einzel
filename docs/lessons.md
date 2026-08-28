# Lessons

Bugs that presented as physics and turned out to be arithmetic, and mistakes in
how things were measured. Each is recorded because it cost real time to find and
because none of them announced itself — every one produced a plausible number.

Ordered roughly by how much they would cost someone who hit them again.

## The measurement that measured the wrong thing

The first comparison of bicubic against bilinear interpolation used a **solved**
field as the reference. On a coarse grid the solve carries its own O(h²)
discretisation error, and that error is larger than the interpolation error it is
supposed to be a backdrop for. The result: bicubic appeared **60× worse** than
bilinear, which is the opposite of the truth.

Sampling the *exact* potential onto the grid nodes isolates the interpolant, and
the ordering inverts to a 29–146× advantage the other way.

The general form: when measuring component A, the reference must not contain a
larger error from component B. Ask what else is in the number before believing
it.

## Fixing the wrong thing about the geometry

The first multi-reflection model stopped the ion at a detector a set distance
along the drift. That makes the arrival time equal *distance ÷ drift velocity*,
which depends on energy alone and not at all on the mirrors — so no energy
focusing is possible by construction, whatever the optics do.

The focusing fit named it unmistakably: c₁ = −0.500, c₂ = 0.3756, c₃ = −0.3133
is the Taylor series of 1/√(1+δ), which is free flight.

Real multi-reflection analyzers fix the **oscillation count** — converging mirrors
bring every ion back after the same number of passes — and the flight time is
that count times the oscillation period, which is where the four-penetration-depth
condition does its work.

The lesson is that a physically-plausible-looking model can be structurally
incapable of showing the effect being studied. Fitting coefficients and reading
them against a known series caught it immediately; a resolving-power number alone
would only have looked disappointing.

## A near-zero field defeats an adaptive controller

Launch an ion in the field-free middle of a mirror pair. Local acceleration is
almost nothing, so the step-size heuristic proposes an enormous step; the
embedded error estimator agrees it was accurate — **correctly**, for a straight
line — and the ion flies 39 metres through both mirrors without ever sampling
them.

The step was not inaccurate. It was uninformed. A gridded field carries no
information below its node spacing, so `IElectrostaticField.ResolutionLength` now
bounds the step and the controller cannot outrun the data.

An error estimator only measures the error of the model it was given. It cannot
tell you the model was never consulted.

## Declaring a discontinuity that is not there

`SolvedField2D` originally marked its whole domain boundary as a field jump.
Where a solve ends in a decayed field there is no jump — and two such phantom
surfaces a few microns apart, which is exactly what two abutting solve domains
produce, defeat the superposition's sign-product tracking: a step crossing both is
treated as crossing neither.

Cost: **2.6e-4 of an ion's energy**, four orders above the ACC-4 budget. It
presented as an intermittent transmission loss that was *non-monotonic in energy*
— −5% fine, −3% lost, +3% fine, +5% lost — which is the signature of step
placement rather than optics.

Declaring structure that is not there is not conservative. It is wrong in a
direction that also disables the machinery meant to handle the real thing.

## Clamping a stencil at a grid boundary

A 4×4 interpolation stencil reaches one node beyond the grid. Repeating the edge
node — the obvious choice — makes the interpolant **non-linear in the boundary
cell even when the underlying field is exactly linear**.

An ion enters and leaves a mirror through that cell twice per reflection. A
clamped stencil put **7.5 ppm** into the flight time of a mirror whose exact
solution is a pure ramp: over the entire ACC-1 budget, caused by nothing but the
corner case. Linear extrapolation of the ghost node took it to 1.9e-10, a factor
of 38,000.

Boundary cases in an interpolant are not edge cases when the geometry puts the
interesting physics at the boundary.

## Coarsening past the point where the geometry exists

Multigrid assumes coarsening preserves the problem. An interior electrode — a
rod, an aperture — occupies a fixed physical size, so each coarsening halves how
many nodes represent it, and past a few levels it is not represented at all. The
coarse grid then solves a *different* problem, and prolonging its correction back
drives the iteration apart. Four discs in a box reached **1e134 V**.

The tell, before divergence, is a convergence factor that **worsens with
refinement** instead of holding steady. A healthy V-cycle is grid-independent;
one that degrades is telling you the coarse levels are not solving your problem.

Three fixes were tried. Only the third works, and the failures are instructive:

- **Agglomeration** (fix a coarse node if anything in its 3×3 block is fixed) is
  stable, since growing the Dirichlet set only damps, but it grows the electrode
  a cell per level and roughly triples the convergence factor.
- **A flat depth floor** stops small grids coarsening at all, costing a
  33-node case its multigrid entirely.
- **A total fixed-node retention ratio** fails for a subtle reason worth knowing:
  a disc loses three quarters of its nodes per level, which is *exactly* the rate
  healthy coarsening produces, so the ratio stays flat right up until the rod
  vanishes. Counting only **interior** fixed nodes separates the case that must be
  limited from the case that must not.

## A search that fails must say so

The separation bisection in the mirror-pair study assumed four penetration depths
bracketed its root. True for one linear stage; false for two. It converged
silently on its own bracket ceiling and returned geometries that were never at
first-order focus.

Then the ranking made it worse: candidates were ordered by their second-order
coefficient, and an *untuned* mirror can have a small c₂ while its uncancelled c₁
destroys the resolution. So the ranking actively **preferred the failures**.

It presented as "two-stage barely helps, R = 260". After expanding the bracket
until it genuinely contains a sign change, and refusing to rank any candidate
whose first-order term is not actually cancelled, the same profile gives
R = 320,548. A three-order-of-magnitude error that looked exactly like a physics
result.

## A residual of exactly zero is not success

The FLD-1 spike reported a potential residual of **0.000E+000** for a sub-cell
geometry perturbation, and the obvious reading — perfect linearity — was exactly
backwards. The plate had moved less than one mesh cell, so the rasterised
geometry occupied identical nodes, the perturbed solve came back bit-identical,
and the derivative was identically zero. A tolerance study on that would have
concluded the parameter did not matter.

Exact zeros in a numerical result deserve suspicion rather than satisfaction.
They usually mean a quantity was never computed rather than computed to be zero,
and the two are indistinguishable in the number itself.

The underlying cause is fixed — the boundary now moves sub-cell, and the same
measurement returns a proper quadratic Taylor remainder — but the guard stays,
because what it detects is "the model never saw the perturbation", and that can
be true for reasons other than the one that prompted it.

The neighbouring case makes the same point from the other side: at 0.1% and 0.3%
perturbation an earlier fixture returned residuals of 0.2297 and 0.2297 —
identical to four figures. A residual that does not move when its input moves is
not measuring its input.

## A boundary condition that moves when you coarsen

A Dirichlet domain edge was implemented as a ghost node one cell outside the
grid, held at zero, with the edge node itself solved. On a single grid that is
perfectly self-consistent, and it had been right for months.

It is wrong the moment there is more than one grid. The ghost sits one cell out
at the fine level, two at the next, four at the next — so every level of a
V-cycle solves a slightly *larger domain* than the one above it, and the
correction it computes is for a different problem. A cap plate in a grounded box
diverged to **1e50 V**.

Two things about how it stayed hidden are worth more than the bug itself. First,
another limitation was masking it: the interior-electrode coarsening floor
stopped these geometries before they reached a second level, so the solver
silently fell back on plain Gauss–Seidel and reported a convergence factor of
0.83. Poor, but not obviously a bug — and "poor convergence" is exactly the sort
of number one shrugs at. Removing one limitation exposed the other, which is the
usual order of events.

Second, the diagnosis came from a two-by-two table rather than from reading the
code. Cut cells were the new thing and the obvious suspect; running the same
geometry with cuts on and off, and with Dirichlet and Neumann edges, showed the
divergence in the cut-free rows and in none of the Neumann rows. That is four
solves and it eliminated the entire feature that had just been written.

## Measuring the analysis in one mechanics and the trajectory in another

Normalised emittance is conventionally written as the geometric emittance times
βγ. Implemented that way, it drifted by **8.1e-7** across an accelerating stage —
a quantity whose entire purpose is to not change.

Two separate causes, both invisible until an exact test asked for exact
invariance.

**The paraxial term.** A divergence angle is transverse velocity over *axial*
speed, while βγ is built from the *total* speed. The two differ by a factor of
1 + y′²/2, which is around 8e-7 for a milliradian packet — and it does not cancel,
because damping shrinks the divergence and shrinks the term with it. Measuring the
area in (y, p_y) directly instead of scaling an angular area by βγ removes it
exactly: 8.1e-7 → 2.1e-8.

**The remaining 2.1e-8 was γ − 1 at the exit speed**, and it was there because the
analysis was relativistic while the transport is not. Relativistically an axial
force conserves γmv_y exactly, so v_y falls slightly as γ grows. Newtonian
transport holds v_y exactly constant, so γv_y grows. Carrying a γ in the analysis
measured the packet in mechanics the trajectory was not integrated in, and the
mismatch surfaced as an emittance that grew out of nowhere.

Dropping γ took it to **3.0e-16**, machine precision. What that gives up is bounded
and remote: γ − 1 reaches 1 ppm at around 460 keV for m/z 500, against the few keV
an ion-optical instrument runs at. If a relativistic transport mode is ever added,
the term has to come back — **in both places at once**, which is the actual lesson.
An analysis and the integrator it consumes must agree about the mechanics, and a
conserved quantity is the only thing that will tell you when they do not.

## A packet with no area has no orientation

A cloud with spatial spread and no temperature is perfectly parallel. Its emittance
is exactly zero — a real answer, not a degenerate one — but the Twiss α that
describes the *tilt* of its phase-space ellipse is undefined, because there is no
ellipse. It came out `NaN`, JSON cannot represent `NaN`, and the serialiser took
the entire result document down.

This is the **second** time that exact failure has happened here; the first was a
convergence residual. Both appeared only under `--json`, both were caught by a test
written for something else, and in both cases the field was one that is normally
finite and only fails in a case nobody pictured.

The fix that generalises is not another `Finite()` guard: it is that an undefined
measurement should be **absent, not zero**. Zero is a real emittance and a real α,
so a reader cannot tell a measured zero from an absent measurement if both print as
zero. The field is nullable and omitted when there was nothing to measure.

## Projecting onto half a basis

The rectilinear trap's field quality was measured by expanding the potential on a
circle in multipoles and comparing the largest unwanted term against the round-rod
quadrupole's. The helper was copied from the quadrupole study, where it projects
onto **cos(nθ) only**.

That is exact there and wrong here. Four identical round rods are four-fold
symmetric, so every sine term vanishes identically and a cosine projection loses
nothing. This trap has a slot in one plate and not the other: it is mirror
symmetric in x and not in y, and an asymmetry about the x axis lands **entirely in
the sine terms**. The cosine projection reported the odd orders as 1e-9 — which
read as "the slot costs nothing" — and the published figure named the 12-pole as
the worst aberration at 7.12e-3.

With both phases, the dipole is 5.43e-2, seven times the 12-pole and the largest
term by far. The headline changed from 296x worse than round rods to 2,258x, and
the attribution changed with it: the 12-pole is what flat plates cost, the dipole
is what the slot costs.

What makes this worth recording is that the near-zero was **evidence**, not
reassurance. Odd multipoles at 1e-9 in a geometry that is visibly asymmetric is
not a small effect, it is an absent measurement, and the right response to a
suspiciously exact zero is to ask what could make it exact rather than to bank it.
The same instinct is already in this file under a residual of exactly zero not
being success.

Generalises to: **a symmetry that makes half a basis redundant is a property of
the device, not of the method.** Reusing the reduced form on a device without that
symmetry measures a projection of the answer and reports it as the answer.

## A mirror plane the interpolant did not know about

The bicubic stencil reaches one node outside the grid and fills it by linear
extrapolation. That is already a considered choice - clamping was wrong and cost
7.5 ppm, and is recorded above. What was missed is that extrapolation is right for
a *Dirichlet* edge, where the data simply ends, and wrong for a **Neumann** edge,
which is a mirror plane: the field continues across it as its own reflection, so
the ghost is the node one step back inside rather than the ramp continued outward.

It surfaced on the axis of the first axisymmetric solve, where the radial field
must be exactly zero because there is no radial direction to point in. It read
**14 V/m**. An ion launched exactly on axis would have drifted off it - slowly,
plausibly, and in whichever direction the interpolant happened to lean. Reflecting
took it to exactly zero.

Two things worth keeping. The bug had been live in a shipped template all along,
because the mirror pair declares a Neumann edge too; it never failed a test because
no test asked what the field was *on* that plane. And the fix is one line per edge
that the original comment had already argued its way to the doorstep of - it says a
Neumann edge is a mirror, in the solver, three files away from the interpolant that
did not act on it.

Generalises to: **a boundary condition is a statement about the field, not only
about the solve.** Anything that samples the field afterwards has to honour it too,
and the place that is easiest to forget is the one where the answer is a clean zero
that nobody thought to check.

## Fixing an instance four times before fixing the class

JSON has no not-a-number and no infinity. Every result surface here is JSON, and a
single non-finite double does not degrade a document - it takes the whole thing
down, at the serialiser, after the run has already succeeded and the numbers are
in hand.

It happened four times, on four unrelated fields:

1. a convergence residual, when there was no order to resolve;
2. a Twiss orientation, for a packet with no phase-space area to be tilted;
3. a space-charge fraction, for a packet with no beam energy to be a fraction of;
4. the energy drift of a driven field, which reports not-a-number *deliberately*,
   because a field that does work on purpose has no conservation to diagnose.

Each was found the same way - a run that worked and a `--json` that did not - and
each was fixed where it was found. Three guards, then a fourth field nobody had
guarded.

The fix for the class is a converter: **a non-finite double is written as null.**
That is the policy the rest of the surface had already arrived at by hand - an
undefined measurement is absent, not zero, because zero is a real answer and a
reader cannot tell the two apart if both print as zero - so making it structural
costs nothing and closes the whole family. Reading is the mirror, so a stored
result still round-trips, which `verify` needs.

The lesson is about *when* to generalise. Once is an incident; twice is a
coincidence; by the third the shape of the class was already visible in the
comments being written, and the fourth was avoidable. A guard that has to be
remembered per field is a guard that will be forgotten.

## The evidence was computed and then thrown away

The segmented quadrupole lost its ion at q = 0.611 instead of 0.908 for a whole
revision, because a solve stopped short of its tolerance. The solver knew. It
returned a `SolveReport` carrying `Converged`, the cycle count and the final
residual — and `FieldAssembly.Build` wrote

```csharp
elements.Add(GeometryBuilder3D.BuildField(geometry).Field);
```

dropping `.Report` on the floor at the one seam every run, study and test passes
through. Downstream, an unconverged field is **indistinguishable from a converged
one**: it is a grid of plausible numbers with no marker on it.

Fixing the multigrid fixed the symptom. It did nothing about the reason nobody
noticed for a revision, which is the part that generalises. **GRD-2 exists for this
exact failure** — warnings propagate through engine, command layer, CLI and
exported files — and a seam that converts a reported result into a bare object is
where that requirement quietly stops holding.

Two ways out, and the choice depends on whether there is anywhere to put a taint:

- Where a **result** is produced, taint it. `BuildReported` hands back the field
  and its warnings, and `run` and `preview` carry them onto every number computed
  through the field.
- Where only a **field** is produced, there is nowhere to attach anything, so
  `Build` throws. "Taint, never block" is about results; a bare object with no
  envelope has no third option between refusing and concealing, and concealing is
  what it was doing.

The generalisable rule: **when a computation produces evidence about its own
quality, the type that returns it may not have a shape that makes discarding the
evidence the shortest spelling.** `.Field` was one character shorter than handling
the report, and that was the whole mechanism.

A companion, found in the same review: `SolveOutcome.Converged` was
`Elements.All(e => e.Converged)`, which is `true` for an empty list. `einzel solve`
read only the two-dimensional element, skipped every three-dimensional one, and
answered `converged: true`, exit code 0. Vacuous truth is worse than a failure
because it terminates the investigation.

## Two arithmetic slips, for completeness

**Velocity fraction is not energy fraction.** v ∝ √E, so a fractional energy
offset δ is a fractional velocity offset of about δ/2 — and a factor of two in the
linear term is a factor of four in the quadratic. A hand estimate of the
single-stage resolving power came out 8× low because of it.

**A symmetric energy spread is not a symmetric velocity spread**, for the same
reason. Ions at ±5% energy do *not* arrive together at a first-order focus; the
residual is the second-order term evaluated at two different magnitudes. Asserting
they coincide would have been asserting the wrong physics. The closed form
ε²/(2(1+ε)) predicts a spread of 3.132e-5 and the integrator produced 3.132e-5.

## A bare double at the one seam every study crosses

`FiguresOfMerit.Evaluator` hands a sweep driver a `Func<CompiledModel, double?>`,
because ranking needs an ordering and there is no ordering on a GRD-1 envelope.
That much is right, and the discard was documented as deliberate.

What was discarded was not only the interval. The evaluator destructured the
whole `Measured` — value, uncertainty, evidence, **warnings** — and returned the
value. So every warning the flight behind a draw earned stopped there: a field
that missed its tolerance, a mode outside its validity, an integration that hit
its ceiling instead of the detector. A thousand draws could each have been
computed in a field that never converged, and the study would report a
distribution, a ranking, and nothing else.

The shape is familiar from `FieldAssembly.Build` discarding its `SolveReport`:
**a computation produced evidence about its own quality, and discarding it was
the shortest spelling.** `var (value, _, _, _) = measured` is one character
shorter than carrying the fourth field, and it reads as tidy.

Three things about the fix are worth keeping.

**The sink is per-evaluation, not per-emission.** An ensemble figure builds the
field once per ion, so a field warning arrives twenty-one times for one draw.
Counting emissions would report "on 21 of 3 evaluations", which is nonsense in a
way that discredits the whole line. The unit being counted is the draw.

**The count is the point.** "on 3 of 1000 draws" and "on 1000 of 1000 draws" are
the difference between a corner of the tolerance box and a study to throw away,
and a warning with no count cannot distinguish them.

**Taint, never block — but only where there is something to taint.** `Setup` used
to call `FieldAssembly.Build`, which *throws* on an unconverged solve, so a sweep
died rather than reporting. It now uses `BuildReported` when it has a sink and
`Build` when it does not, which is the honest reading of the rule: carry the
warning if you can, refuse if you cannot, and never drop it. A bare field has
nowhere to carry it, so for that caller the throw is still right.

The control test is the part that took a second attempt. Asserting that a clean
model's study warns about *nothing* failed — correctly — because a clean study
now reports the same convergence provenance a run does. That is the seam working,
not a regression. What the control has to assert is the absence of the specific
claim made about the strained model, which is a weaker statement and a true one.

## A formula that was right about the wrong mechanism

The space-charge screen converted a packet's self-potential into a flight-time
error as `½ × phi / V`: the self-potential is an energy spread, time goes as the
inverse square root of energy, so halve it. Every step of that is correct. The
answer was wrong by **527 times**, in the direction that under-reports.

The reasoning describes ions leaving a trap from different depths of the
self-potential well, which really does give them different energies and really
does give that timing error. It is not what dominates once they are flying. In
flight the packet **expands**: the self-field keeps pushing for the whole drift,
and the relative speed it imparts is set by turning the self-potential into
*relative* kinetic energy, sqrt(2 q phi / m). For 40,000 ions in a half-millimetre
ball at 4 kV that is 149 m/s. The old reading — perturb a 4 keV beam energy by
0.058 eV and see what it does to the speed — gives 0.28 m/s, because it is asking
what happens to an ion already travelling at 39 km/s rather than what happens
between two ions travelling together.

Both are questions about the same self-potential. Only one of them is the question.

**Nothing internal could have caught it.** The formula was dimensionally right,
monotone in every parameter, checked against a hand calculation, and covered by
three tests — all of which asserted the wrong relation with conviction. The unit
tests were the same mistake written twice. What caught it was building the direct
pairwise sum SC-1 asks for and comparing, and the first comparison failed by two
and a half orders of magnitude, which is a large enough gap that it could not be
argued away as a modelling choice.

That is what SC-1 is for, and it is worth stating in general: **a screening
estimate cannot be validated against reasoning, only against a computation that
does not share its assumptions.** Ours shared its assumptions with its own tests
for as long as those were the only thing checking it.

Two smaller things fell out of the fix.

**The inversion is a maximum where the forward direction is a minimum.** The
estimate takes `min(linear, escape)`, and `min(a, b)` is within budget as soon as
*either* is — so the population that satisfies it is the *larger* of the two
inversions. Writing the minimum in both places looked symmetric and reported a
population limit of three thousandths of an ion, which at least failed loudly.

**And the packet's total momentum is not the invariant it looks like.** The
obvious check on a pairwise sum is that internal forces do not move the centre of
mass. In a real flight that is false for two reasons that are not errors: the
applied field is an external force, and a detector removes momentum along with
the ion carrying it. Asserting on it was asserting that mirrors do not reflect.
The invariant that *is* exactly true is the balance of the mutual accelerations
themselves, checked at every stage of every step — which also covers the case the
naive version was reaching for, an indexing error over absorbed members.

## The same operator, written twice, conservative once

The cylindrical Poisson operator is written in conservative form — flux through a
ring's outer face minus its inner face, over the ring's own volume — and the
reasoning behind it is written down. Months later the drift-diffusion solver was
built on the same grid class, with the same `Cylindrical` flag, and its face flux
was computed per unit area and applied to both neighbours as though their volumes
were equal. In an axisymmetric solve they are not: a cell is a ring, so the ion
count crossing a face is created on one side and destroyed on the other.

The weight a face needs is `A_face · h / V`, identically 1 in the plane and
`1 ± h/2r` in a cylindrical one. **On the axis it is 4** — the inner face has no
area, so the cell is a disc rather than a ring. That is the *same* factor of four
the Laplacian carries on the axis, and it was already documented, one file away, as
the thing a plane operator gets wrong there.

**Three things about how it hid.**

*The tests were all Cartesian.* Every conservation check in the suite ran on a
plane grid, where the weight is exactly one and a scheme with no weights at all is
correct. They passed for a reason that did not generalise — the identical failure
mode as the uniform-field conservation test that hid a cell-centred drift sample,
recorded above.

*The ledger did not have to close.* An electrode emptied only the initial density,
and the ions it deleted were removed after the launched population had been
counted. So launched, collected, remaining and the named losses were never required
to add up, and a four per cent leak on the shipped funnel had nowhere to appear.
Making the itemisation complete is what made the defect visible; the fix to the
bookkeeping found the fix to the physics.

*It was worst exactly where it mattered.* The weight departs from one as `h/2r`, so
the error is negligible at the wall and total on the axis — which is where a funnel
puts its ions, and a funnel is the device this transport mode exists for.

**The check that discriminates is not the conservation figure.** A wrong weight can
still conserve to a few per cent over a short run, and the population sum was
99.9995% on the first off-axis fixture that exercised it. What cannot be nearly
right is the weight itself: exactly 4 on the axis, exactly `1 ± h/2r` off it. Assert
the quantity with an exact value, not the symptom with a tolerance.

**The rule.** A conservative discretisation is a property of an *operator on a
geometry*, not of a grid class. Sharing `Grid2D` and a `Cylindrical` flag with a
solver that got it right transfers none of it, and the second author of an operator
on the same mesh is the least likely person to re-derive the face areas.

## A guard that guarded nothing, under a test that could not reach it

Scharfetter-Gummel's exponent is `P = v h / D`, the ratio of drift to diffusion
across one cell, and it feeds a Bernoulli function `B(x) = x / (exp(x) - 1)`. That
function already handles a large argument **exactly** - it is zero above +40 and
`-x` below -40, which are the true limits and not approximations to them, and it
takes them explicitly to avoid an overflow inside `exp`.

The flux clamped `P` to ±40 *before* calling it. Reading the two together, the
clamp looks like it is protecting the exponential. It is not: the exponential
protects itself, one function down. What the clamp actually did was cap the
effective drift at `40 D / h`, so above a cell Peclet of 40 the density moved too
slowly by exactly the ratio the cap imposed.

**Every existing test ran below the cap.** The advection checks are at a cell
Peclet of 16 and report 1.000000. They are correct. They could not see this.

**What saw it was an expectation that was a division.** A corpus example - a drift
tube with a declared mobility and a declared gas flow, so `L / (mu E + v_gas)` is
arithmetic with nothing of the engine's in it - came out 6.7% long. Unclamped it
comes out 0.86% long, which is the packet's own spread, and the convincing part is
that the *same* 0.86% now appears with and without the flow. A residual independent
of the drift speed is a packet effect; a residual that grows with the drift is a
scheme effect, and only having both cases separates them.

**Three things generalise.**

A guard placed one level above the thing that already guards itself is not
redundant, it is a second policy - and the outer one wins silently. Look for the
inner guard before adding an outer one.

A test whose parameter sits below the threshold of the bug is not a weak test, it
is a test of a different regime. The suite's advection checks were not sloppy;
nothing about them says which side of 40 they are on. Where a scheme has a
dimensionless number in it, the tests should straddle the values that number
switches behaviour at, and should print it.

And an expectation that is *arithmetic the engine had no part in* catches a class
of thing that self-consistency cannot. That is the whole argument for EX-1's
corpus, and it paid for itself on the second batch.

## Reading the DC of an electrode that holds none, three times

A basis-superposed field is linear in the applied potentials, so an electrode's
excitation is *two* numbers — a DC potential and a drive amplitude — and code that
asks only about the first is asking about the half that is often zero. That has now
been the bug three separate times, in three unrelated places:

- **`einzel solve`, 3-D.** It iterated `Solve` elements and `continue`d past every
  `Solve3D`, then answered `Elements.All(e => e.Converged)` — vacuously true over an
  empty list. `converged: true`, exit 0, for a field it never touched.
- **`einzel solve`, 2-D.** Fixed in three dimensions and still wrong in two: it
  built one mask from the electrodes' DC potentials. For the shipped `quadrupole-rf`,
  whose every electrode holds zero DC, that was a solve of an earthed box — **peak
  potential 0 V, zero cycles, converged, exit 0.**
- **`ModelValidator.CanDoWork`.** A source at rest is legal exactly when some field
  could accelerate it, and the test was `Electrodes.Any(e => e.Potential != 0)`. So
  the **Paul trap**, the archetypal device whose ions sit still until the RF moves
  them, was refused as a model in which nothing could move an ion. The 3-D arm of
  the same switch fell through to `_ => true` and inspected nothing at all — the
  same bug wearing the opposite mask, one over-refusing and one under-refusing.

**Why it keeps happening.** `e.Potential` is the obvious spelling, it is correct for
every DC device, and every failure is silent and confident: an earthed box solves
fine, and a refusal names a plausible-sounding constraint. Nothing about the
symptom points at the drive.

The generalisable rule is the one already recorded under *the evidence was computed
and then thrown away*: **when a quantity has two parts, the shortest spelling must
not be the one that silently drops a part.** `IsDriven` exists on both electrode
records precisely so the complete question has a short name. It is worth grepping
for `.Potential` the next time something driven behaves as though it were earthed.

## The pattern

Every one of these produced a *plausible* number. None threw. The things that
actually caught them were:

- **Closed forms.** An exact answer to compare against turns a plausible number
  into a wrong one.
- **Coefficients rather than summary figures.** c₁ = −0.500, c₂ = 0.3756 named a
  bug that "R is disappointing" would not have.
- **Convergence behaviour, not single values.** An error that fails to fall with
  refinement, or a cycle count that grows with it, is diagnostic on its own.
- **Reverting the fix to check the test.** An assertion that passes with the bug
  restored is not testing the bug. The anisotropic-coarsening test first asserted
  the maximum principle, which held either way; the convergence factor was what
  actually moved — 0.213 against 0.303 — and only trying it both ways showed which.
- **Exact invariants.** The maximum principle — no potential may exceed the
  applied value — is a tolerance-free check that a solve has not diverged.
  Liouville's theorem is the same kind of check on the integrator, and being
  independent of energy it catches things energy conservation cannot. Both found
  bugs that presented as small, plausible drifts.
- **Removing a refusal without removing its reason.** `CollisionSampler` refused a
  gas flow because it had no position to evaluate one at. Threading the position in
  made the refusal obsolete — but the trajectory run path built its gas with
  `FromModel`, which never resolves a declared velocity field, while only the
  diffusive path called `Resolve`. Lifting the refusal alone would have reintroduced
  the exact failure it existed to prevent, silently. **A guard is removed correctly
  only when the thing it guarded against is checked for directly.**
- **A transient inside the measurement window.** A stepped gas flow read a difference
  of 361 m/s against a declared 200, which looks like a physics discrepancy. The step
  sat 3 mm from the launch and the ion crossed it in six microseconds, so the "before"
  average was over three samples of an ion still accelerating from rest. Moving the
  step past the settling distance gave 204.5. **An average is over whatever the window
  contains, including the part that is not yet the thing being measured.**
- **A convenience accessor that quietly became a summary.** Adding a second
  generator turned `CompiledElectrode.DriveAmplitude` from *the* amplitude into *the
  first tap's* amplitude, and left it as a property with the same name. Every reader
  kept compiling. One of them was `ElectrodeOverlap.Agrees`, the check that refuses
  two conductors occupying the same space at different excitations — so two
  electrodes agreeing about the main RF and differing about a supplementary one were
  judged identical, and the mask kept whichever was written last. **The one check
  that exists to prevent a field of a geometry nobody described became a route to
  one.** Found by asking, after the change, which readers of the old scalar were
  asking a question the scalar no longer answers.
- **Measuring a linear property with a measurement that cannot be linear.** A
  stability boundary is a statement about a *frequency* - where the characteristic
  exponent reaches one - but the obvious way to find it is to ask whether the ion was
  lost. That requires it to travel to an electrode through the whole anharmonic
  region, so the answer depends on where it started and no amount of care removes the
  dependence: the shipped Paul trap's hold-converged edge is q_z = 0.85 at a 0.1 mm
  launch and 0.82 at 0.3 mm. **The fix was not a better loss measurement but a
  different quantity.** Reading beta off the spectrum of an ion that stays small
  locates the linear boundary to a worst residual of 1.2e-3 and needs no journey at
  all. When a measurement is amplitude-dependent and the thing being measured is not,
  suspect the measurement rather than adding controls to it.
- **A reference method has approximations in it too, and comparing against it at
  default settings compares two of them.** Particle-in-cell was reported as agreeing
  with the direct pairwise sum "to a few per cent". Both numbers were right and the
  comparison was not meaningful: the sum softens at the mean macroparticle spacing and
  the grid smooths at the cell, so what was being measured was the difference between
  two smoothing lengths that happened to be comparable. Taking the sum to its own point
  limit (softening / 100, worth **3.5%**) and setting the cell to the mean spacing gives
  **0.08%** - a much stronger claim, and one that says what makes them agree. **Before
  quoting an agreement, ask what each side approximates and whether the two can be set
  to the same thing.**

- **Refinement is not always an improvement, and the case where it is not is the one
  someone will walk into.** Halving the particle-in-cell cell size past the mean
  macroparticle spacing makes the answer *worse* - -15.1%, -4.2%, +0.08%, +4.4% across a
  16x range, with an optimum in the middle. Raising a resolution number is what a reader
  does when they want a better answer, and here it silently buys a worse one. Two things
  follow: the optimum has to be *reported* rather than left to be discovered, and a
  "converges under refinement" test would have passed on the wrong side of it. Checked
  by control rather than asserted - holding the cell fixed and raising the macroparticle
  count took the error from 4.42% to 1.55% to 0.93%, which is what makes it a sampling
  artefact rather than a resolution one.

- **An argument that was right about accuracy and wrong about cost.** ACC-3 forbids
  trilinear interpolation on a trajectory path. Particle-in-cell's gather looked
  exempt - it is a self-consistent field whose accuracy the deposit already bounds,
  and the gather must share the deposit's weights or the self-force does not cancel -
  so the first version used it. The reasoning holds; what it missed is that a
  trilinear force is kinked at every cell face and an embedded Runge-Kutta estimator
  reads a kink as error. **The packet took 27 times the integrator steps**, which no
  accuracy argument would have predicted. A quadratic B-spline is smooth, keeps the
  deposit/gather symmetry, and takes it back to 2x. When exempting something from a
  smoothness rule, ask what else reads the derivative.
- **Factorial experiments over code reading.** Two binary switches and four runs
  localised a divergence to a feature nobody suspected, faster than reading the
  diff would have.
- **Running the same measurement twice, slightly differently.** Two boundary
  searches over brackets differing only in their lower end returned 680.7 V and
  694.4 V for the same trap. Either alone reads as a measurement; the pair says the
  predicate is frayed, which is what sent the observation window from 60 cycles to
  200 and put a confirmation walk into `BoundarySearch`.
