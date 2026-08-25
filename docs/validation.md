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
| Rosenbrock and a 6-D sphere, both optimisers | Optima to 1e-3 in parameters, 3e-13 in objective |
| Parallel-plate gap, boundary swept sub-cell | 3.1e-10 of applied, at every offset |
| Coaxial log potential around a rod | Second order, 1.5e-5 at h = 0.156 mm |
| Shape derivative dV/dL against −1000x/L² | 6.5e-6 relative at a 0.11-cell step |
| Mathieu stability boundary, sinusoidal drive | q = 0.90684 against a tabulated 0.90804 |
| Meissner boundary, square-wave drive | q = 0.71113 against a published 0.712 |
| Digital working point from duty cycle | a = 0.2630 against a published 0.2640 |
| Thermal packet emittance, sigma_x sqrt(kT/m) / v | 0.77% at 6,000 ions |
| Turn-around through a solved trap, against the closed form at the solved field | 0.7% |
| Drift-scan slope under mesh refinement (20 to 40 cells/r0) | 20.7 ns/mm at both, points within 0.1% |
| Transmission through a slit, against erf(a / sigma sqrt 2) | 0.95 sigma at 20,000 ions |
| Coaxial potential in r-z, against A ln r + B | 1.3e-3 V of 100 V |
| Tube field penetration, against the first Bessel zero | 2.40503 against 2.404826 |
| Axisymmetric convergence order, 32 to 256 cells | 1.84 / 2.00 / 1.95 |
| Total energy across an einzel lens | 6.4e-10 |
| Low-mass cut-off on **solved** round rods, against tabulated Mathieu | q = 0.90525 against 0.90804, 0.31% |
| Funnel basis solves against ring count (8 / 24 / 48 rings) | 2 / 2 / 2 |
| Sequenced run against the same flight stitched from two runs | 1.3e-9 relative |
| Impact point against the electrode surface it landed on | below 1e-8 m, i.e. at the root-find's own tolerance |
| Arrival-spread decomposition against quadrature of its three parts | 0.2% |
| Turn-around time against 2√(2ln2)√(mkT)/qE | 0.49% on 4000 ions; 0.5–2.0 ns across m/z 195–2722 |
| Thermal cloud width against √(kT/m) per component | 0.4% on 20000 ions, mean indistinguishable from zero |

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
- **Conservation of ions**: every launched ion either reaches the detector or is
  named on a loss surface, and the two sum to the launch count. An itemisation
  that does not add up is worse than none, because it reads as complete.
- **Liouville's theorem**: a conservative force cannot change phase-space area. A
  field-free drift to a plane preserves the emittance to **1.5e-14** and an ideal
  thin lens to **8.1e-15**, while the same lens given a cubic term — spherical
  aberration — grows it by 1.58x. This is a conserved quantity *independent of
  energy*: energy conservation is blind to a map that shears phase space, so it
  checks an axis of the integrator that ACC-4's energy drift does not reach.
- **Adiabatic damping**: accelerating a packet along its axis divides the geometric
  emittance by exactly the speed ratio — observed 0.031606977 against a closed-form
  0.031606977 across a 10 V to 2 kV stage — while the normalised emittance holds to
  **3.0e-16**.

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
- **Literature regression is started, not finished.** Published geometries
  reproduced against reported performance is the tier that catches *conceptual*
  errors rather than numerical ones, and with cross-code unavailable it is the most
  valuable one. Three targets are worked up in
  [Literature targets](literature-targets.md) and the quadrupole stability
  boundaries are reproduced against two of them. What remains is mostly geometry:
  the Ion Processor now has both figures of merit it needs — turn-around time and
  emittance — but not the trap geometry to measure them on, and the segmented
  quadrupole needs three dimensions.
- **No waveform library.** A drive is a sinusoid or a rectangular wave; there is no
  way to declare an arbitrary waveform, which spec section 9 lists as one of the
  excitations an electrode may carry, and no multi-notch isolation waveform.
- **No statistical-diffusion transport**, so no cross-mode agreement check in the
  overlap band.
- **No 3D.** Every solve is two-dimensional - translational or axisymmetric. This
  is what stops the Ion Processor's auxiliary DC electrodes being modelled: they
  impose a gradient along the trap axis, which is the direction the solve is
  invariant in.
- **Electrodes are solid, with no way to say otherwise.** Real instruments use
  mesh and grid electrodes that pass most of the beam. There is no way to declare
  one, and a mesh cannot be modelled as its wires either, because the wires run
  along the invariant axis of a 2D solve.
- **Space charge is screened, not modelled.** Ions do not push on each other. A run
  reports the flight-time error the packet's own charge implies and warns
  non-suppressibly past the budget, but the trajectories ignore it. A real
  treatment advances every ion together and recomputes their shared field each
  step, which inverts the integration loop and is Phase 3.
- **No collisions, no gas flow.** This bites hardest on the funnel: a real one runs
  at around a millibar and the gas is half the mechanism, damping radial motion so
  ions settle onto the axis rather than ringing about it. The acceptance measured
  without it is a lower bound on the real one.
- **The agent acceptance suite has no measured pass rate yet.** The corpus, the
  scoring, and the release gates exist and are self-validating in CI — every
  task's worked solution passes and every distractor fails — but no agent has been
  run against it, so there is no rate to report. See
  [Agent acceptance](agent-acceptance.md).
- **Anisotropy beyond two to one is not handled.** Nothing can currently produce
  it, but a point smoother damps error poorly along a stretched axis, and a grid
  built by hand at, say, 8:1 would converge slowly with nothing to say so.
- **Geometry sensitivity fields are limited by second-order physics**, not by
  the mesh: the linearisation error is (δ/L)², so a 1 ppm gate holds to about
  δ/L = 10⁻³ and the memo's 100–300 µm channels linearise to 1e-5, not 1e-6. See
  [Spec findings](spec-findings.md). Voltage channels linearise to 1.5e-14.
- **No detector response.** A perfect plane that stops an ion the instant it
  arrives; no time constant, no dead time, no spatial response.
- **Resolving powers are only as honest as the source they were computed from.**
  A model that declares no cloud launches one ion down the axis, and its resolving
  power measures energy aberration alone. Declaring a cloud - temperature, spatial
  width, energy spread - makes it a property of the instrument. The default is
  still the single ion, so every earlier number in these pages carries the older
  meaning.
- **The optimiser handles box constraints and nothing else.** No general
  constraint functions, no multiple objectives, no way to say "maximise resolving
  power subject to fitting in the envelope" except by folding the envelope into
  the objective as a penalty by hand. The mirror-pair result says that trade is
  the interesting one, so this is a real gap rather than a theoretical one.
- **No optimiser has been pointed at the mirror pair.** Each evaluation is about
  23 seconds, so a search runs to minutes and does not belong in the test suite;
  it belongs in a study. The target is well posed - the second-order coefficient
  changes sign across the scan, so the root is bracketed.

## Running them

```
dotnet test                                                   # everything
dotnet test --filter FullyQualifiedName~AnalyticTierTests     # one class
dotnet test tests/Einzel.Fields.Tests                         # one assembly
```

The Library suite takes a couple of minutes: it tunes mirror separations by
bisection, and each evaluation is a solve plus several flights.
