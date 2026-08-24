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

Geometric multigrid on a uniform Cartesian grid: five-point stencil, red-black
Gauss–Seidel smoothing, full-weighting restriction, bilinear prolongation,
V-cycles, correction scheme.

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

### Interior electrodes: a real limitation

Coarsening assumes it preserves the problem. That holds for boundary-only
Dirichlet geometries and **fails for interior electrodes** — a rod, an aperture.
An electrode occupies a fixed physical size, so each coarsening halves how many
nodes represent it, and past a few levels it is not represented at all. The
coarse grid then solves a different problem, and prolonging its correction back
drives the iteration apart. Four discs in a box reached **1e134 V** that way.

Convergence factors with interior electrodes degrade with refinement rather than
holding steady: 0.075 at 64 intervals and 0.153 at 128, against a flat ~0.03 for
boundary-only geometries.

**This is mitigated, not solved.** `PoissonSolver2D` refuses a coarsening that
would leave fewer than 128 interior fixed nodes, which is enough for the shipped
templates; `InteriorElectrodeSolveTests` asserts the maximum principle, that no
potential anywhere may exceed the applied value, which is an exact check rather
than a tolerance and the cheapest possible detector of divergence. A real fix is
Galerkin coarsening or operator-dependent interpolation, and it should happen
before anyone solves a large rod geometry.

Two approaches were tried and rejected, recorded so they are not re-tried:

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
