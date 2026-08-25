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

## The pattern

Every one of these produced a *plausible* number. None threw. The things that
actually caught them were:

- **Closed forms.** An exact answer to compare against turns a plausible number
  into a wrong one.
- **Coefficients rather than summary figures.** c₁ = −0.500, c₂ = 0.3756 named a
  bug that "R is disappointing" would not have.
- **Convergence behaviour, not single values.** An error that fails to fall with
  refinement, or a cycle count that grows with it, is diagnostic on its own.
- **Exact invariants.** The maximum principle — no potential may exceed the
  applied value — is a tolerance-free check that a solve has not diverged.
- **Factorial experiments over code reading.** Two binary switches and four runs
  localised a divergence to a feature nobody suspected, faster than reading the
  diff would have.
