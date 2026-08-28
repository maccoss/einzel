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

## Three dimensions

The first solver here with no symmetry behind it. A cross-section assumes the
geometry repeats along the third axis and an axisymmetric solve assumes it repeats
all the way round; this assumes nothing, which is what a segmented quadrupole or
an auxiliary DC wedge needs and what neither of the others can express.

Written **beside** the two-dimensional path rather than generalising it. That path
carries every validated number in this engine - the reflectron at 1.3e-13, cut
cells at 3.1e-10, the coaxial and Bessel closed forms - and refactoring a numerical
core that is known to be right, in order to add a case next to it, is how those
numbers get quietly lost. The duplication is the price and it is the cheaper one.

### What is checked

| | |
| --- | --- |
| Harmonic quadratic, reproduced | **4.3e-13** relative |
| Non-polynomial harmonic, observed order | 1.92 / 1.99 |
| Cycle count at 16 / 32 / 64 intervals | 12 / 13 / 13, factor 0.08 |
| Neumann face against the full solve it mirrors | 1.4e-16 V |
| Curved conductor against the 1/r law | 2.8 V of 100 applied |
| Maximum principle at the nodes | exact |
| Tricubic on a linear field | 3.6e-15 V, gradient 6.2e-12 V/m |

The quadratic is the unusual one and the sharpest. The seven-point Laplacian is
**exact** for a quadratic - its truncation error starts at the fourth derivative,
which a quadratic does not have - so a harmonic quadratic imposed on the faces is
not an approximation converging, it is an identity. Nothing about the operator, the
faces or the multigrid transfers can be wrong and still pass it.

### Coarse levels are node-aligned, and that is what makes them work

Multigrid coarsening does not survive interior electrodes when the coarse levels
carry sub-cell surfaces. An electrode loses seven eighths of its nodes per level
rather than three quarters, and an electrode a fraction of a coarse cell across
produces arms a thousandth of a cell long, whose coefficients are enormous - the
correction that comes back does not converge slowly, it converges somewhere else.
A charged sphere reached **137 V of 100 applied** that way.

The fix is to separate what each level is for. **Cut cells on the finest level,
where the accuracy comes from; node-aligned geometry on every level below, where
only acceleration comes from.** A coarse level that is merely crude is a perfectly
good preconditioner; a coarse level that is ill-conditioned is not. An electrode
too small to contain a coarse node has its nearest node pinned, so it stays present
at the smallest size the level can express rather than vanishing.

| | |
| --- | --- |
| Sphere solve, no coarsening | 13 s |
| Same, node-aligned coarse levels | **783 ms, 9 cycles at factor 0.126** |
| Answer | identical to the digit |

Sixteen times faster for the same field. Worth noting that agglomeration was tried
in *two* dimensions and rejected - it grows the electrode a cell per level and
roughly triples the convergence factor - but that comparison was against a
geometry rebuild that worked. Here the rebuild does not work, and stable-but-cruder
beats correct-but-uncoarsenable by a wide margin.

Galerkin coarsening, building the coarse operator from the fine one rather than
from the geometry again, is still the better answer and is still not done.

**Coarse masks are memoised by grid.** They were being rebuilt at every level of
every V-cycle from geometry that had not changed - for the twelve-rod segmented
quadrupole, over a million `Contains` calls per cycle producing a mask that was
bit-identical every time. The hierarchy is now built once per solve and reused.

**Known cost, not fixed:** `CutLinks3D` allocates twelve doubles per node
unconditionally - a fraction and a potential for each of six arms - which is 104 MB
at the shipped segmented-quadrupole mesh and around 410 MB at eleven cells across
r0. Only nodes adjacent to metal are ever cut, so an index array plus compact
per-cut storage would be about 4 bytes a node instead of 96. It is not done, because
it puts an indirection in the smoother's innermost lookup and that is the hottest
loop in the solver; it should be measured, not assumed.

**A coarse level's Dirichlet *values* do not matter, only which nodes it fixes.** A
V-cycle solves for the error, whose boundary data is zero, and the correction array
starts at zero and is never seeded from the mask. So a coarse level that merges two
differently-driven electrodes across a gap it can no longer resolve is crude, not
wrong: it clamps the error to zero somewhere it should be free, which under-corrects
that neighbourhood and slows the iteration. What is *not* allowed is a coarse cell
larger than the electrode itself - that is a different problem rather than a coarser
one, and its correction points elsewhere. This is why the guard tests a physical
size and not a node count.

**The guard tests the coarsest of the three spacings, not the finest.** Each axis
rounds its own interval count up to a power of two, so a 2:1 aspect ratio is
ordinary here rather than exceptional. Asking `MinimumSpacing` let the shipped
segmented quadrupole descend to a level whose z cell was **4.875 mm against a 4.587
mm rod radius** - the exact condition the guard exists to refuse, passed because one
of the other two axes was still fine enough. A guard that only has to be satisfied
in its best direction is not a guard.

**The interpolant overshoots about 2% just outside a conductor surface**, which is
what a cubic through a step does. Measured at 101.8 V of 100 applied, in a shell
one cell thick where an ion is about to be absorbed anyway. The maximum principle
is therefore a statement about nodes, and is checked there.

## Landing on a switch

A sequencer switches state at known times, and a Runge-Kutta step that spans one
averages two different fields into a single answer - plausible, and wrong.

Unlike a boundary in space this needs **no root-find at all**, because the time is
known in advance. The integrator asks the field when it next switches and refuses
to take a step past it, so it lands on the boundary exactly and the next step
starts in the new state with the derivative recomputed there. `NextSwitchAfter`
returns infinity for a continuously driven field, however fast: a sinusoid has no
discontinuity, and a rectangular one is handled by the steps-per-cycle cap rather
than by landing on every edge.

### Measured

The check is that a sequenced run equals the same flight computed as **two separate
runs stitched together** - the same physics written two ways, needing no closed
form to compare against.

| Integrator tolerance | Disagreement, relative |
| --- | --- |
| 1e-8 | 1.0e-8 |
| 1e-10 | 4.6e-8 |
| 1e-12 | 1.3e-9 |

Parts per billion at every tolerance, which is round-off between two different step
sequences rather than the parts per thousand a straddled switch would leave. It is
not monotone, because which steps each route happens to take is luck rather than a
trend - what matters is that the disagreement is at round-off from the start, not
that it shrinks.

Stages that share a spatial pattern share their solve, so a trap that holds at one
voltage and pushes at another costs one basis field, not three.

## RF on solved geometry

Driving a real geometry costs almost nothing beyond solving it once, because
**basis superposition already was the mechanism**. The field is linear in the
applied potentials, so solving at unit potential and then making the weights
functions of time *is* the RF - nothing is re-solved as the drive swings, and the
Poisson equation is never stepped in time at all.

### Channels, not electrodes

SYM-1 makes the point in passing - "a 200-ring funnel driven in two RF phases needs
two RF basis fields plus a DC gradient, not 200 basis solutions" - and it
generalises. Electrodes whose potentials are the same function of time, or exact
negatives of one another, share a basis.

A quadrupole's two pairs are exact negatives, so **four rods reduce to one basis
solve**. The channel's weight swings 500 V to 0 to -500 V across a cycle and the
field is that weight times one solved basis. A q scan, or a mass scan, re-solves
nothing whatever.

The grouping is exact rather than approximate: two electrodes share a channel when
their DC and RF parts are equal or both exactly negated, at the same phase - which
is what a real instrument produces, because the electrodes are wired to the same
supply. A tolerance here would silently merge two channels that were meant to
differ, and the field would be plausible.

Grouping is by **spatial pattern**, not by time dependence, and that is what makes
it minimal. Every electrode's potential is first split into the supplies feeding
it - one constant, one per distinct drive phase - so a resistor chain down a funnel
is a *single* supply however many distinct voltages it holds, because what makes a
supply one supply is that its electrodes move **together**, not that they move to
the same place. Then supplies whose applied potentials are exactly proportional
share a solve and carry a weight each.

Measured on the funnel template, which is the device SYM-1 argues from:

| Rings | Electrodes | Basis solves |
| --- | --- | --- |
| 8 | 8 | 2 |
| 24 | 24 | 2 |
| 48 | 48 | 2 |

Two, not three - the two RF phases are exact negatives of one another, so they are
one spatial pattern with one weight. Three phases that were not negatives, as a
travelling-wave guide has, would be three.

### Measured

The a-q diagram had been recovered before, but against an analytic field that is
exactly quadrupolar by construction. That tests the integrator and the drive; it
does not test the solver, because there is no solve in it. On four round rods with
a mesh, cut cells and a grounded housing:

| | Low-mass cut-off |
| --- | --- |
| Solved round rods | **q = 0.90525** |
| This engine, ideal hyperbolic field | q = 0.90684 |
| Tabulated Mathieu | q = 0.90804 |

**0.31% below the tabulated ideal**, and in the right direction: round rods carry a
12-pole component a hyperbola does not, and cancelling it at r/r0 = 1.1468 still
leaves the 20-pole and the housing.

That it is the *geometry* rather than a formula is checkable directly - changing the
rod ratio to 1.30 moves the cut-off to 0.89978. A field that had quietly come from
an equation could not do that.

And "unstable" now means something physical. On an ideal field an unstable ion
leaves an aperture the test had to invent; here the rods are solid, so it ends on a
named surface - `rodYPlus`, which is the pair that goes unstable first on the a = 0
line.

## Cylindrical symmetry

SYM-1 asks that a geometry may declare cylindrical symmetry, that the solver
reduce accordingly, and that the interpolant reconstruct the full field
transparently. Section 22 calls it load-bearing for funnels; it is load-bearing
for rather more than that, because **most of section 1's device table is
rotationally symmetric** - einzel lenses, ion funnels, stacked-ring guides,
apertures, drift tubes. A translational cross-section cannot express any of them:
what makes them work is that the electrode wraps all the way round, and the same
declaration in a plane is a pair of bars.

`"symmetry": "cylindrical"` on a solve makes **x the axis of rotation and y the
radius**, with the domain at y >= 0.

### The operator

In cylindrical coordinates the radial part of the Laplacian is
(1/r) d/dr (r dphi/dr), not d2phi/dr2, because a ring of given thickness has less
circumference the closer it sits to the axis. It is written in **conservative
form** - flux through the outer face of a ring minus flux through its inner face,
divided by the ring's own volume - which makes the discrete operator conserve what
the continuous one does, and is stable near the axis where discretising the 1/r
term directly is not.

Face radii are in cells, so the cut-cell machinery carries over unchanged: a node
at radius rho with arms reaching fSouth and fNorth has faces at rho - fSouth/2 and
rho + fNorth/2, and the ring's measure is the difference of their squares.

**On the axis the inner face has zero area**, so no flux crosses it and the ring is
a disc. That limit gives 4(phi_1 - phi_0)/h^2 for a uniform arm - **twice** what a
mirrored plane stencil gives. A solve that treats the axis as an ordinary symmetry
plane is wrong by that factor and converges contentedly.

### The axis is a mirror, and the interpolant has to know

Finding this cost a test failure that looked like nothing. The bicubic stencil
reaches one node outside the grid and fills it by **linear extrapolation**, which is
right at a Dirichlet edge - simply where the data ends - and wrong at a Neumann
edge, which is a mirror plane. Extrapolating across a mirror leaves a spurious
normal field on it. On the axis that is a radial field at a place with no radial
direction, and it read **14 V/m**; an ion launched exactly on axis would have
drifted off it, slowly and plausibly.

Reflecting instead - the ghost is the node one step back inside - takes it to
**exactly zero**. The fix is general: it applies to every Neumann edge, and the
shipped mirror-pair template has one.

### Measured

| | |
| --- | --- |
| Coaxial pair against phi = A ln r + B | 1.3e-3 V of 100 V applied |
| The same geometry against a linear profile | 19.3 V, so the two are far apart |
| Convergence order, 32 to 256 cells | 1.84 / 2.00 / 1.95 |
| Field penetration into a grounded tube, against the first Bessel zero | 2.40503 against 2.404826 |
| The plane operator would give, for the same geometry | pi/2 = 1.5708 |
| Radial field on the axis | exactly 0 |
| Azimuthal field anywhere | exactly 0 |

The coaxial test sounds like it proves nothing, since phi = A ln r + B holds in both
geometries - and that is exactly what makes it sharp. The **plane** solver gets a
linear profile there, so agreeing with the logarithm is precisely what the radial
weighting is responsible for.

The Bessel test is the one that is specific to cylindrical geometry. Inside a
grounded tube the field from an end cap decays along the axis as exp(-j x / R) with
j the first zero of J0 - the radial eigenfunction of the cylindrical Laplacian - so
the decay rate reads out the operator directly. It converges to j01 **from below**,
because the cap's mode coefficients go as 1/(j_n J1(j_n)) and J1 alternates in sign
at its zeros, so the second mode enters negative and slows the apparent decay until
it dies.

### Reconstruction

`AxisymmetricField` wraps the half-plane solve and presents the field in space: an
ion at (x, y, z) is sampled at (x, sqrt(y^2 + z^2)) and the radial field it finds is
pointed back along its own azimuth. Conductors come with it - a rectangle in the
half-plane is a **ring** in space, which is what makes an aperture an aperture
rather than a slot.

One thing is given up: `FieldFreeRunLength` returns zero, so no run is ever taken
analytically. A straight line in space traces a curve in (axial, radial), so a
direction mapped once is only instantaneously right, and the guarantee this returns
is that the field is identically zero over the *whole* run.

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

## A parallel-plate capacitor is the worst 3-D case, and that is the wrong way round

The documented multigrid limitation — coarsening does not preserve interior
electrodes, so the convergence factor degrades — has a concrete cost that is easy to
underestimate, because the geometry that shows it worst is the simplest one anybody
would write.

Two slabs 10 mm apart in a grounded box, 0.5 mm cells, 65 x 65 x 46 nodes:

| | cycles | factor |
| --- | --- | --- |
| **parallel plates** | **49** | **0.652** |
| the shipped segmented quadrupole, twelve rods | 12–13 | 0.08 |
| a charged sphere, node-aligned coarse levels | 9 | 0.126 |

**124 seconds** for the plates, against 11 for a whole segmented-quadrupole run. A
factor of 0.65 means the V-cycle is barely doing anything and the solve is close to
plain relaxation.

The reason is structural rather than surprising once seen. A rod is thin, so
coarsening loses it quickly and the pinning fix restores its presence; **a slab is a
large solid Dirichlet region**, and a coarse level that half-represents a slab is
solving a different problem over a large volume rather than a small one. The error the
coarse grid feeds back is wrong where most of the domain is.

**This is why the example corpus has no three-dimensional model.** One was written — a
parallel-plate gap checked against `sqrt(2 d² m / (q V))`, the same closed form the
analytic accelerating-gap example uses — and it was not shipped, for two reasons worth
separating:

- **It costs two minutes** against a gate that runs the other twenty-six examples in
  forty-two seconds. That is the multigrid limitation, not the example.
- **It was 3.2 per cent off**, which is the *geometry*: a finite plate in a grounded
  box 2 mm behind it is not an infinite capacitor, and asserting `V/d` to one per cent
  would be asserting that it is. Fixing that means a larger domain, which costs more
  solve, not less.

So the volume solver, its tricubic interpolant and its cut cells are exercised by
`Einzel.Fields.Tests` and by the segmented-quadrupole study, and **not** by the
release gate. That is a stated gap rather than an oversight, and the thing that closes
it is Galerkin coarsening — which would also make the plates converge like everything
else.

## Poisson, not only Laplace

Every solve here until now has been **Laplace** - a potential with no charge in it,
fixed on conductors. SC-1's *approximate* space-charge method needs **Poisson**:
deposit the packet's own charge onto a grid, solve grad2 phi = -rho/eps0, gather the
field back.

**The cycle already carried a right-hand side and had only ever been handed zeros.**
The smoother subtracts it, the residual is defined against it, and the coarse levels
receive the restricted *residual* - which is what they need whatever the fine level's
source is. So the source costs one argument and no numerics.

Checked by the **method of manufactured solutions**, which is the sharpest thing
available: pick a potential, differentiate it analytically to get the source that
produces it, hand the solver that source, and compare. Nothing on the exact side is
discretised and no reference implementation is involved. With
phi = sin(pi x) sin(pi y) on the unit square, whose Laplacian is -2 pi^2 phi exactly:

| intervals | worst error | order | cycles | factor |
| --- | --- | --- | --- | --- |
| 32 | 8.0358e-4 | | 11 | 0.0632 |
| 64 | 2.0082e-4 | **2.000** | 11 | 0.0659 |
| 128 | 5.0201e-5 | **2.000** | 11 | 0.0702 |

Second order to three figures, and the cycle count is grid-independent. **The order is
the load-bearing check**: a source entering the smoother and the residual
inconsistently would still converge - to the wrong answer - and would show it as an
order that is not two rather than as a failure.

And the control: passing no source gives **exactly** what the solver gave before one
existed, bit for bit and in the same number of cycles. Not nearly - exactly, or every
number this engine has published from a solved field has moved.

What is left for SC-1's approximate method is the particle side: cloud-in-cell
deposit, the same weights on the gather (or momentum is not conserved), and validation
against the direct pairwise sum, which exists and is the reason it was built first.
