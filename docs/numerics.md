# Numerics

Everything here is measured on this codebase unless it says otherwise. Where a
number is quoted, the test that produces it is named.

## The accuracy budget

The specification sets these, and they are the reason for most of the design
decisions below.

| | Bound | Why |
| --- | --- | --- |
| ACC-1 | Flight-time error ≤ 1 ppm | 1/25 of the peak-width budget at R = 20,000 |
| ACC-2 | ≤ 0.25 ppm in validation mode | To reproduce published R = 80,000 results |
| ACC-3 | Interpolation ≤ 0.5 × ACC-1 | The usual offender |
| ACC-4 | Energy drift ≤ 1 ppm in a static field | Cheap conserved-quantity diagnostic |

The counter-intuitive one is ACC-3, and it is worth stating plainly because the
instinct on missing a timing target is to reach for a higher-order integrator.
That is usually the wrong lever. An ion crossing a gridded potential accumulates
error from the interpolant's discontinuous derivatives at every cell boundary,
and the sign of that error is set by the direction of travel — so over 10⁵
crossings it accumulates linearly rather than cancelling as a random error would.

We measured it. Flying the same trajectory through an exactly-sampled harmonic
field, varying only the interpolant:

| Grid | Cells crossed | Bicubic (C¹) | Bilinear (C⁰) | Ratio |
| --- | --- | --- | --- | --- |
| 64 | 51 | 6.43e-8 | 9.41e-6 | 146× |
| 128 | 102 | 3.01e-8 | 2.64e-6 | 88× |
| 256 | 205 | 9.27e-9 | 2.70e-7 | 29× |

Bilinear on the coarsest grid is **nineteen times over the entire ACC-1 budget**,
not merely over the interpolation share of it. `SolvedField2D` therefore refuses
a C⁰ interpolant outright; the escape hatch exists only so that the tests above
can measure what it costs.

## Trajectory integration

`TrajectoryIntegrator` is the scalar reference implementation. It is never
deleted or allowed to rot, because every future SIMD or GPU path is tested
against it.

Five mechanisms, each addressing a distinct way a flight time goes wrong.

**Dormand–Prince 5(4) with per-step error control.** Seven stages, a fifth-order
solution with an embedded fourth-order estimate, first-same-as-last so an
accepted step costs six field evaluations. Coefficients are written as exact
rational quotients so the compiler rounds each once.

**Neumaier-compensated time accumulation.** A 192 µs flight in picosecond steps
is ~10⁵ additions of a small increment onto a growing total; naive summation
loses about a bit per doubling of the term count. Neumaier rather than Kahan
because Kahan drops the correction when the incoming term exceeds the running
total, which happens on the first step and again after every analytic drift.

**Analytic advance through field-free regions.** In a multi-reflection analyzer
most of the path is drift, and integrating a straight line numerically
accumulates error for no physics. `FieldFreeRunLength` is a guarantee, not a
hint: a non-zero return asserts the field is identically zero over that whole
run.

**Exact landing on declared discontinuities.** Runge–Kutta assumes the derivative
is smooth across the step, and DP54's stage 4 carries the coefficient −56/15, so
intermediate stage samples fall *outside* the step interval and can land on the
wrong side of a field jump even when both endpoints are inside. Left unhandled
this put the reflectron error on a floor of 5.5e-10 that barely responded to
tolerance; landing on the boundary as an event took the same case to 1.7e-16.

**A resolution cap.** A gridded field carries no information below its node
spacing, so a step may not outrun `ResolutionLength`. Without it, an ion launched
in a field-free region proposes an enormous step, the error estimator correctly
agrees it was accurate *for a straight line*, and the ion sails through the entire
instrument without sampling it. The step was not inaccurate; it was uninformed.

### Measured

Against the closed-form single-stage reflectron, m/z 500 at 4 keV:

- Flight time within **1.3e-13** of the analytic value when the field is solved
  numerically, and ~1e-10 with the idealised discontinuous analytic field
- Energy drift ~1e-9 to 1e-8 against the ACC-4 budget of 1e-6
- Free flight and the smooth uniform-field turnaround at machine precision, 1e-15

`AnalyticTierTests`, `IntegratorBehaviourTests`, `SolvedReflectronTests`.

### The turning-point cap

Spec §11 requires forced step refinement at turning points, on the grounds that
"position-error controllers under-refine" at the velocity minimum. Ours is not a
position-error controller — `ErrorNorm` weights velocity error with its own
absolute floor — and measurement says the cap does not help: in a smooth field
the flight time is at machine precision with 6 steps and marginally *worse* with
105. It is implemented and on by default to honour the spec as written. See
[Spec findings](spec-findings.md).

## Field solving

Geometric multigrid on a uniform Cartesian grid: a Shortley–Weller stencil,
red-black Gauss–Seidel smoothing, full-weighting restriction, bilinear
prolongation, V-cycles, correction scheme.

Red-black rather than lexicographic ordering because each colour sweep is
internally independent, and the architecture puts a SIMD and GPU dispatch layer
under the engine — a smoother that cannot be parallelised would have to be
replaced later.

### Measured

**Second-order convergence**, against a manufactured harmonic solution
Φ = A sinh(ky) sin(kx):

| Intervals | Max error | Observed order |
| --- | --- | --- |
| 16 | 1.281 V | |
| 32 | 0.321 V | 1.996 |
| 64 | 0.0804 V | 1.997 |
| 128 | 0.0201 V | 2.000 |

The reference is deliberately not a polynomial: the five-point Laplacian is exact
up to quadratics, so a polynomial reference would report machine precision at
every refinement and reveal nothing about the order.

**Grid-independent cycle count** for boundary-only geometries — 8, 7, 7, 7 cycles
from 32 to 256 intervals, at a residual reduction near 0.03 per cycle. That
property, not the stencil, is what makes a full basis campaign tractable.

**Basis superposition** matches a direct solve to 1.4e-11 V on a 4800 V scale.
Solving once per electrode at unit potential turns voltage optimisation from a
solve-per-iteration problem into arithmetic, which every sweep and optimiser
depends on. It breaks the moment geometry changes, which is why tolerance work
needs sensitivity fields rather than more superposition.

## Cut cells

A conductor surface almost never lands on a grid node. Deciding node by node
whether each one is inside or outside — rasterising — is the obvious thing to do
and it costs two quite different things.

**Accuracy.** The boundary is placed at the nearest node, so it is wrong by up to
half a cell. That is a first-order error, on an otherwise second-order scheme,
sitting exactly where the field is usually most interesting.

**Differentiability.** The discrete operator becomes a staircase function of
electrode position. Move an electrode by a fifth of a cell and *nothing* changes;
move it by a cell and everything does. Shape derivatives then measure the
staircase rather than the physics, which is what made the FLD-1 spike fail.

The fix is the Shortley–Weller stencil: a second difference on unequal spacings,

    d2f/dx2 = 2/(h- + h+) [ f-/h- + f+/h+ - f0 (1/h- + 1/h+) ]

so an arm of the stencil may stop at a conductor surface partway to the next
node. With every spacing equal it reduces to the familiar five-point formula, so
there is no separate uniform code path and no arithmetic cost where there is no
cut. `CompiledElectrode.FirstEntry` gives the crossing in closed form per
primitive, and `CutLinks` stores, per node and per direction, how far the surface
is as a fraction of a cell and what potential it holds there.

Two details that are not incidental:

- **Entry, not straddling.** A crossing is found by asking where a segment first
  enters a conductor, not by testing whether its two endpoints are on opposite
  sides. An electrode thinner than a cell lies wholly *between* two nodes, so a
  straddle test reports nothing and the electrode disappears — which is every
  coarse level of a multigrid hierarchy, and is where geometry used to dissolve.
- **Cell units.** The stencil is carried with coefficients of order one and the
  mesh applied exactly once. Folding 1/h² into the neighbour sum scales every
  term by millions while leaving the difference between them — the quantity
  actually wanted — unchanged, and spends precision for nothing.

### Measured

**A planar boundary is where it says it is.** A 20 mm parallel-plate gap on a
0.625 mm mesh, with the plate face swept across a whole cell in twenty steps. The
exact potential is a straight ramp, which any consistent second-difference
stencil reproduces exactly, so the residue is a direct measure of where the
solver thinks the boundary is:

| | worst error over the sweep |
| --- | --- |
| Cut cells | **3.1e-10** of the applied potential — solver tolerance |
| Snapped to the nearest node | up to 1.6e-2 |

A factor of fifty million, and more to the point the error no longer depends on
where in the cell the boundary fell. The second differences of a probe potential
across the sweep run 5.4e-3 to 6.2e-3 V and vary monotonically; a staircase shows
up there as alternating spikes.

**A curved boundary converges at second order.** A rod of 5 mm radius in a
40 mm box whose edges carry the analytic coaxial potential A ln r + B, which is
then the exact solution over the whole annulus:

| Intervals | h | Max error | Observed order | Cycles | Factor |
| --- | --- | --- | --- | --- | --- |
| 64 | 0.625 mm | 2.30e-4 | | 7 | 0.019 |
| 128 | 0.3125 mm | 5.76e-5 | 2.00 | 8 | 0.022 |
| 256 | 0.1562 mm | 1.49e-5 | 1.95 | 8 | 0.023 |

Section 19 asks for coaxial fields against closed form. That check was not
available before, because a rasterised circle is a staircase and the comparison
would have measured the staircase rather than the solver.

### The one approximation left

A surface passing arbitrarily close to a node gives an arbitrarily small spacing
on one side of the stencil, and the coefficient grows as its reciprocal. Nothing
breaks mathematically — the operator stays an M-matrix and Gauss–Seidel still
converges — but a single node carrying a coefficient millions of times its
neighbours' dominates the residual norm, and a convergence test measured against
that norm stops describing the rest of the grid.

So `CutLinks.MinimumFraction` floors the fraction at 1e-3, and the price is a
boundary knowingly moved by at most a thousandth of a cell. In the parallel-plate
sweep above, a floor of 0.05 cost 3.0e-4 of the applied potential when a face
landed 0.04 cells from a node; at 1e-3 the same case costs at most 3.1e-5, and
every position outside that window solves to 1e-11. The window is a thousandth
of a cell wide, so it is not where a shape derivative usually finds itself.

It has not been removed altogether because doing so trades a small bounded
geometric error for an unbounded numerical one, which is the worse of the two.

## Interior electrodes

Coarsening assumes it preserves the problem, and with a rasterised boundary that
**failed for interior electrodes** — a rod, an aperture. An electrode occupies a
fixed physical size, so each coarsening halved how many nodes represented it, and
past a few levels it was not represented at all. The coarse grid then solved a
different problem and prolonging its correction back drove the iteration apart:
four discs in a box reached **1e134 V**.

Cut cells fix this, because they change what "represented" means. A surface has a
position at any spacing, whether or not a node happens to fall behind it, so the
coarse mask is *rebuilt from the geometry* rather than projected down from the
fine one — `PoissonSolver2D.Solve` takes a `coarsen` factory for exactly that.
An electrode too small to contain a coarse node still cuts the links around it.

Measured on the coaxial rod above, where the old coarsening limit allowed two
levels:

| | Cycles | Factor |
| --- | --- | --- |
| Limited coarsening (projected mask) | 43–47 | 0.52–0.55 |
| Full coarsening (rebuilt mask) | 7–8 | 0.019–0.023 |

Grid-independent, on interior-electrode geometry, which is what it was supposed
to be all along. The `Einzel.Sweeps` test suite went from 2 m 15 s to 4 s on the
same machine as a direct consequence.

The old floor of 128 interior fixed nodes still applies to a mask that has no
geometry to rebuild from — one assembled node by node, as `BasisFieldSet` does —
because such a mask really does lose a quarter of an electrode per level.
`InteriorElectrodeSolveTests` asserts the maximum principle, that no potential
anywhere may exceed the applied value, which is an exact check rather than a
tolerance and the cheapest possible detector of divergence.

Three approaches were tried and rejected, recorded so they are not re-tried:

- **Agglomerating the mask** (a coarse node is fixed if anything in its 3×3 block
  is) is stable, because growing the Dirichlet set only damps the correction, but
  it grows the electrode by a cell at every level: the convergence factor went
  from 0.075 to 0.31 at 64 intervals and 0.15 to 0.47 at 128.
- **A flat depth floor** stops small grids coarsening at all, and cost the
  32-interval manufactured-solution case its multigrid entirely (factor 0.508).
- **A total fixed-node retention ratio** does not work either: a disc loses three
  quarters of its nodes per level, exactly the rate healthy coarsening produces,
  so the ratio stays flat until the rod disappears and then it is too late. Only
  counting *interior* fixed nodes separates the two cases.

## Independent spacings per axis

A grid carries `SpacingX` and `SpacingY`, and they need not be equal. The
Shortley–Weller stencil already holds a spacing per arm, so the y half is scaled
by (hx/hy)² to bring it into the x cell units the rest of the operator works in.
On a square grid that factor is exactly one, and multiplying by one is exact, so
an isotropic solve is unchanged to the last bit.

The reason to want it is that the alternative was worse. Keeping cells square
meant deriving the y interval count from the aspect ratio, rounding it up to a
power of two, and accepting whatever box that reached — so a 60 × 20 mm domain at
a 1 mm cell needed 21.3 intervals in y, rounded to 32, and was **solved as
60 × 30 mm**. Fifty per cent taller than declared, silently, and nothing checked.

Now each axis rounds its own interval count up to a power of two from the same
requested cell size. Both spacings therefore lie in (cellSize/2, cellSize], which
means the extent is exact, neither direction is ever coarser than asked, and the
worst cell aspect ratio is two to one.

Two to one is comfortable for a point smoother. Well beyond it, error damps
poorly along the coarse direction and the fix is line smoothing or
semi-coarsening — but nothing here can produce that, by construction.

Measured on a deliberately 2:1 grid against the manufactured harmonic solution:

| Grid | hx | hy | Max error | Observed order |
| --- | --- | --- | --- | --- |
| 17×17 | 6.250 mm | 3.125 mm | 32.03 V | |
| 33×33 | 3.125 mm | 1.562 mm | 8.026 V | 2.00 |
| 65×65 | 1.562 mm | 0.781 mm | 2.010 V | 2.00 |
| 129×129 | 0.781 mm | 0.391 mm | 0.503 V | 2.00 |

That measurement is the point of the exercise rather than a formality: a wrong
aspect factor is a wrong Laplacian, the solve converges perfectly happily to the
wrong answer, and only an order measurement notices. A cut boundary running
across y on a stretched grid — the arrangement that would go unnoticed if the
factor were applied to the wrong half — solves to 1.1e-16 of applied.

## Domain edges

A Dirichlet domain edge means the potential is zero **on the edge**, and
`GeometryBuilder` grounds those nodes unless an electrode has already claimed
them. The alternative reading — a ghost node one cell outside the grid held at
zero, with the edge node itself solved — is self-consistent on any single grid,
and it is what the solver did.

It is wrong as soon as there is more than one grid. The ghost sits one cell out
at the fine level, two at the next, four at the next, so every level of a V-cycle
solves a slightly larger domain than the one above it and the correction it
computes is for a different problem. A cap plate in a grounded box diverged to
**1e50 V**.

It went unnoticed because the interior-electrode coarsening limit stopped these
geometries before they reached a second level. The solver fell back on plain
Gauss–Seidel and reported a convergence factor of 0.83 — poor, but not obviously
a bug. Grounding the edge takes the same case to 9 cycles at 0.039.

A Neumann edge is unchanged: it is a mirror plane, so the ghost node outside it
equals its reflection inside, which is exact at any spacing and coarsens
faithfully.

## Electrodes stop ions

An electrode used to be a boundary condition on the potential and nothing else, so
an ion flew through a plate as readily as through the hole beside it. That made
every aperture scenery and every transmission figure 100% by construction.

A field that has conductors declares them through `IConductorBounded` as a
**signed distance**, negative inside. That choice is the whole design: an impact is
then the zero of a scalar function along the step, which is the same kind of event
as a stopping surface and is found by the same bracketed root-find - so an ion
lands *on* the surface rather than a step short of it or a step inside it, and
there is no second event mechanism with its own edge cases.

Three things make it sound rather than approximate.

**The chord is safe because the step is already capped.** A gridded field limits
the step to its own cell spacing, so a trajectory cannot arc into an electrode and
back out between two samples: an electrode is many cells thick and the chord and
the arc differ by far less. The cap was added for a different reason - an ion in a
field-free region proposing an enormous step - and it turns out to be what makes
this safe too.

**Order matters.** An electrode is checked ahead of the detector, because an ion
that hits metal on the way did not arrive; and behind a declared field
discontinuity, because an electrode cannot be on the far side of a surface the ion
has not crossed yet.

**A source inside a conductor is refused rather than flown.** Otherwise it reads as
an instrument that loses everything, rather than a model with its source in the
metal.

### Measured

| | |
| --- | --- |
| Transmission through a slit, against erf(a / sigma sqrt 2) | 0.95 sigma at 20,000 ions |
| Impact point against the surface struck | below 1e-8 m |
| Ions accounted for (through, or on a named surface) | exact |

The slit test is the sharp one: with every electrode grounded the field is
identically zero, so the ions fly straight and the fraction that gets through is
the fraction of the launch distribution inside the opening - an error function, and
nothing to do with this code.

## Emittance

The phase-space area a packet occupies, reported per transverse plane about the
packet's own mean velocity. Two forms, and the difference between them is not
cosmetic.

**Geometric**, √(⟨y²⟩⟨y′²⟩ − ⟨yy′⟩²) with y′ = v_y/v_axial, in m·rad. What an
aperture cares about. Root-mean-square rather than a bounding ellipse, because a
bounding area is set by whichever ion strayed furthest and a real distribution has
tails.

**Normalised**, the same area measured against transverse momentum — ⟨y, v_y/c⟩
rather than ⟨y, y′⟩×βγ. Invariant under axial acceleration, which the conventional
βγ form is not; see [Lessons](lessons.md) for the two terms that separate them and
for why the momentum is Newtonian.

A radian is dimensionless, so an emittance has the dimension of length. **1 mm·mrad
is exactly 1 µm**, so the figure of merit is registered in `um` and the number
reads as the conventional unit without conversion.

### Measured

| | |
| --- | --- |
| Thermal packet against σ_x·√(kT/m)/v | 0.77% at 6,000 ions |
| Uncorrelated lattice against (2/3)·σ·δ | exact to 15 figures |
| Preserved through a field-free drift to a plane | 1.5e-14 |
| Preserved through an ideal thin lens | 8.1e-15 |
| Grown by a cubic lens term (spherical aberration) | 1.58x |
| Geometric emittance ratio across a 10 V → 2 kV stage | 0.031606977, closed form 0.031606977 |
| Normalised emittance across the same stage | 3.0e-16 |

The drift and lens figures are Liouville's theorem, and they are a check on the
integrator as much as on the figure of merit — a conserved quantity **independent
of energy**, so it constrains an axis that ACC-4's energy drift does not reach. The
aberration row is the control: a metric blind to nonlinearity would report 1.00x
there and the conservation rows would prove nothing.

## Interpolation

`BicubicInterpolant` is the Catmull-Rom form of bicubic Hermite: node derivatives
are central differences, which makes the result C¹ across cell boundaries.
Measured derivative jump at a cell boundary is 4.6e-4 V/m against bilinear's
3.6e2.

**Grid-boundary stencils must extrapolate, never clamp.** The 4×4 stencil reaches
one node beyond the grid, and repeating the edge node makes the interpolant
non-linear in the boundary cell even when the field is exactly linear. An ion
enters and leaves a mirror through that cell twice per reflection, and a clamped
stencil put **7.5 ppm** into the flight time of a mirror whose exact solution is a
pure ramp — over the whole ACC-1 budget, from the corner case alone. Linear
extrapolation of the ghost node took the same case to 1.9e-10.

## Determinism

Bit-reproducibility across machines is not a requirement; run-to-run
reproducibility on one machine is. Golden comparisons use documented tolerances
with stated reasons. `Deterministic` and `InvariantGlobalization` are set in
`Directory.Build.props` so that number formatting cannot vary with the host
locale and leak into CLI output or golden files.
