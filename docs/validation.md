# Validation

What is tested, what each tier proves, and — as importantly — what is not covered.

103 tests across seven assemblies. Warnings are errors; XML documentation is
required on public API; CI builds and tests on Linux and Windows.

## The tiers

### Analytic

Closed-form fields whose exact trajectories are known, so any discrepancy is the
integrator's and nothing else's. **This is the primary reference**, because the
cross-code tier is unavailable — see below.

| Check | Result |
| --- | --- |
| Free-flight timing | Machine precision, ~1e-15 |
| Uniform-field turnaround, exact parabola | 1.3e-15 |
| Single-stage reflectron flight time | ~1e-10 relative, four orders inside ACC-1 |
| Energy drift in a static field | 1e-9 to 1e-15 against ACC-4's 1e-6 |
| First-order energy focusing, L = 4d | dT/dv vanishes; reappears at the predicted magnitude when detuned |
| Quadrupole field | Φ(x) = −Φ(y) exactly; Ex/x constant to 0.17% |
| Parallel-plate gap, boundary swept sub-cell | 3.1e-10 of applied, at every offset |
| Coaxial log potential around a rod | Second order, 1.5e-5 at h = 0.156 mm |
| Shape derivative dV/dL against −1000x/L² | 6.5e-6 relative at a 0.11-cell step |

The detuned reflectron matters more than it looks: without it, a bug that simply
returned a constant flight time would pass the focusing test.

The last three are what a cut-cell boundary bought. The coaxial check in
particular was not available before: §19 asks for it, but a rasterised circle is
a staircase, and the comparison would have measured the staircase rather than the
solver. Driving the outer boundary with the same analytic potential A ln r + B
makes that solution exact over the whole annulus, so the residue is the
discretisation and nothing else.

### Convergence

Every physics claim measured at more than one resolution, with the observed order
asserted against nominal rather than assumed.

- Five-point Laplacian against a manufactured harmonic solution: observed order
  **1.996, 1.997, 2.000** against a nominal 2. The reference is deliberately not a
  polynomial, since the stencil is exact up to quadratics and a polynomial would
  report machine precision at every refinement.
- Multigrid cycle count grid-independent for boundary-value geometries: 8, 7, 7, 7
  from 32 to 256 intervals; 7, 8, 8 for a cut-cell rod, which used to be the case
  that could not coarsen at all.
- The same harmonic solution on a deliberately 2:1 stretched grid: **2.00, 2.00,
  2.00**. A wrong anisotropy factor is a wrong Laplacian, and it converges
  contentedly to the wrong answer — only an order measurement catches it.
- Flight time reported through `FlightTimeStudy`, which integrates at three
  tolerances and derives an interval and an observed order — a single run has no
  honest uncertainty to quote.

### Cross-path

The same instrument computed two independent ways. One path is closed-form; the
other runs a Dirichlet geometry through a multigrid solve, a bicubic interpolant,
and the adaptive integrator. Nothing is shared but the physics.

Agreement: **1.3e-13**. Both give 10.180505718 µs.

This is what carries the weight the cross-code tier would otherwise provide.

### Exact invariants

Checks with no tolerance at all, which are the strongest kind available.

- **The maximum principle**: a harmonic function attains its extremes on the
  boundary, so no potential anywhere may exceed the applied value. This is the
  cheapest possible detector of a diverged solve, and it caught the interior-
  electrode coarsening failure.
- **Reflection symmetry**: a pair built by reflecting one solve must be symmetric
  about its plane, and its two half-periods equal. Unequal halves mean the
  integration went wrong, not the instrument.
- **Superposition linearity**: doubling an electrode's potential doubles its basis
  field.

### Contract and guardrail

Tests that enforce rules rather than measure physics.

- `MeasuredApiSurfaceTests` inspects the public surface of the result envelope by
  reflection and fails if any member returns a bare magnitude — so the rule
  governs members nobody has written yet. Verified by injecting a violation and
  watching it fail, rather than assumed to work.
- Allocation does not grow with step count. Testing a byte threshold would encode
  today's fixed overhead; testing flatness tests the actual property.
- A forbidden interpolant is refused on a trajectory path.
- Bounds are checked, not clamped; a cycle in derived parameters is refused with
  the chain named; an override of the wrong dimension is refused.

### End to end

The CLI is driven through `Program.Main` itself, not through the command objects,
because the things most likely to break an agent loop — exit codes, which stream
output lands on, whether `--json` parses — live in the surface.

## What is not covered

Stated plainly, because a test suite that does not say what it omits reads as
covering everything.

- **No cross-code comparison.** No SIMION licence. Nothing here is checked against
  an independently written ion-optics code.
- **No literature regression yet.** Published geometries reproduced against
  reported performance is the tier that catches *conceptual* errors rather than
  numerical ones, and it does not exist. With cross-code unavailable, this is the
  most valuable missing tier. Targets are worked up in
  [Literature targets](literature-targets.md); the nearest is the Ion Processor's
  turn-around time, which is a DC problem and needs only two additions.
- **No RF anything.** No time-domain RF, no Mathieu stability diagram, which is the
  single best test that an RF path is correct.
- **No statistical-diffusion transport**, so no cross-mode agreement check in the
  overlap band.
- **No 3D.** Every solve is two-dimensional with translational invariance.
- **No space charge, no collisions, no gas flow.**
- **No agent acceptance suite.** Scripted prose tasks run against an agent given
  only a project directory and the CLI, scored on whether it acts on warnings, is
  a release metric the specification asks for and nothing measures.
- **Anisotropy beyond two to one is not handled.** Nothing can currently produce
  it, but a point smoother damps error poorly along a stretched axis, and a grid
  built by hand at, say, 8:1 would converge slowly with nothing to say so.
- **Geometry sensitivity fields are limited by second-order physics**, not by
  the mesh: the linearisation error is (δ/L)², so a 1 ppm gate holds to about
  δ/L = 10⁻³ and the memo's 100–300 µm channels linearise to 1e-5, not 1e-6. See
  [Spec findings](spec-findings.md). Voltage channels linearise to 1.5e-14.
- **Resolving powers quoted are energy-aberration only.** No spatial or angular
  spread, no turn-around time, no detector response.

## Running them

```
dotnet test                                                   # everything
dotnet test --filter FullyQualifiedName~AnalyticTierTests     # one class
dotnet test tests/Einzel.Fields.Tests                         # one assembly
```

The Library suite takes a couple of minutes: it tunes mirror separations by
bisection, and each evaluation is a solve plus several flights.
