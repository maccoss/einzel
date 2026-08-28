# Validation

What is tested, what each tier proves, and — as importantly — what is not covered.

531 tests across nine assemblies. Warnings are errors; XML documentation is
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
| 3D harmonic quadratic, reproduced exactly | 4.3e-13 relative |
| 3D non-polynomial harmonic, observed order | 1.92 / 1.99 |
| 3D curved conductor against the 1/r law | 2.8 V of 100 applied |
| Tricubic interpolant on a linear field | 3.6e-15 V |
| 3D segmented quadrupole mid-section field, under refinement | 0.014% |
| The same, transmitted flight time | 9e-5 |
| 3D segmented quadrupole *segment-gap* field, under refinement | 2.4% then 1.4% - not converged |
| Sphere solve, node-aligned coarse levels against none | identical, 16x faster |
| 3D segmented quadrupole cut-off against tabulated Mathieu | brackets 0.90804 (0.855 through, 0.910 lost) |
| Impact point against the electrode surface it landed on | below 1e-8 m, i.e. at the root-find's own tolerance |
| Arrival-spread decomposition against quadrature of its three parts | 0.2% |
| Turn-around time against 2√(2ln2)√(mkT)/qE | 0.49% on 4000 ions; 0.5–2.0 ns across m/z 195–2722 |
| Thermal cloud width against √(kT/m) per component | 0.4% on 20000 ions, mean indistinguishable from zero |
| Diffusive centroid carried by a moving gas, no field, 40 and 120 m/s | **1.000000** each |
| The same against μE + v_gas, gas at ±60 m/s | **1.000000** each |
| A still gas against no declared gas velocity | bit-identical at every node |
| Scan endpoints from a parameter's minimum to its maximum | exact, both ends inside the bound |
| Cylindrical radial face weight, on the axis and away from it | exactly 4 and exactly 1 ± h/2r |
| Ion ledger on the shipped funnel: cylindrical, absorbing rings, gas flow | **100.0001%** (was 95.99%) |

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
  that does not add up is worse than none, because it reads as complete. In the
  diffusive description the same sum holds with a moving gas across a varying field,
  and with interior electrodes absorbing — the two cases where a face-flux scheme
  stops conserving if the coefficient is sampled at the cell rather than the face.
- **A cylindrical density conserves ions**: a flux computed per unit area is
  conservative only between cells of equal volume, and in an axisymmetric solve a
  cell is a ring. Weighting each radial face by its own area over the cell's volume
  closes it exactly — and the weight on the axis is **4**, because the cell there is
  a disc rather than a ring. Asserted on the weights themselves as well as on the
  population, because a conservation figure can be nearly right with a wrong weight
  and the exact 4 cannot.
- **A conductor takes and never gives**: with the density on the far side of a face
  held at zero, the Scharfetter–Gummel flux reduces to `B(-P) n_here`, which is
  non-negative for any potential drop. Checked with the field driving ions *out* of
  the electrode, which is where a sign error would show.
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
- **Newton's third law in the pairwise sum**: the mutual accelerations in a packet
  must cancel, checked at every Dormand–Prince stage of every step and holding to
  **1e-14** of the acceleration scale. Note what this is *not*: the packet's total
  momentum, which is conserved only in free flight with nothing absorbed — an
  applied field is an external force and a detector removes momentum along with the
  ion carrying it. The first version asserted on total momentum and was asserting
  that mirrors do not reflect.

### Space charge, and what the reference method found

SC-1 asks for an approximate space-charge method validated against direct
summation on a reference population. The direct sum is built first, because an
approximation cannot be validated against something that does not exist — and the
first thing it validated was not an approximation but the *screening estimate*
that had been shipping.

| Check | Result |
| --- | --- |
| Newton's third law, every stage | 1e-14 of the acceleration scale |
| Direct sum vs the uniform-sphere closed form | within 5% at 4,000 points (sampling noise, 1/sqrt(N)) |
| Two ions from rest vs energy conservation | 1e-6 |
| Interaction off vs the analytic reflectron | 1e-6 |
| Free-flight widening vs the corrected screen | within a factor of 3, screen bounding |

**The screen was wrong by 527 times, in the unsafe direction, in a number
documented as an upper bound.** It converted the self-potential to a timing error
as half a fractional energy spread. That describes ions leaving a trap from
different depths of the self-potential well; it is not what dominates in flight,
where the packet *expands* and the relative speed the self-field imparts comes
from converting phi into relative kinetic energy — sqrt(2 q phi / m) — rather than
from perturbing a beam energy thousands of times larger. See
[Lessons](lessons.md#a-formula-that-was-right-about-the-wrong-mechanism).

Two things this tier could not have caught on its own. The formula was
dimensionally right, monotone in every parameter, and covered by three tests that
all asserted the wrong relation — the tests were the same mistake written twice.
And a hand calculation had been done and agreed, because the arithmetic was never
the problem.

**A reflectron at first-order focus measurably improves space charge**, which
looked like a sign error. The mutual push correlates position with energy in
exactly the sign the mirror corrects, so a leading, faster ion penetrates deeper
and spends longer: 24.9 ns of arrival spread free against 11.3 ns pushed. Tested
by *detuning* the mirror off the focusing condition, which weakens the
compensation — the control that separates this from an integrator that narrows
every packet at every drift length.

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

### The example corpus

EX-1 asks for thirty validated reference models "spanning every device class, each
with a prose description, expected results, and assertion tolerances", and EX-2
makes them a release gate. Seventeen exist, and the gate is built: every example is
materialised into a real project and driven through `einzel test` via
`Program.Main`, in 29 seconds.

**Every expectation is arithmetic, a published value, or an exact invariant.** That
is what makes the corpus a check on the engine rather than on its own past output -
a failure means the engine has moved away from a closed form, not away from a
golden file.

| Example | Asserted against | Observed |
| --- | --- | --- |
| `free-flight` | L / sqrt(2qU/m) | **exactly 0** error |
| `accelerating-gap` | sqrt(2dm/qE) from rest | 3.5e-16 |
| `uniform-field-turnaround` | the positive root of v t - a t^2/2 + d | 1.1e-15 |
| `single-stage-reflectron` | 2L/v + 2v/a at L = 4d | 1.3e-11 |
| `reflectron-off-focus` | the same at L = 6d | **exactly 0** |
| `reflectron-heavy-ion` | the same scaled by sqrt(2000/500) = 2 | 9.9e-13 |
| `orthogonal-accelerator` | sqrt(2d/a) + L/sqrt(2ad) | 5.0e-12 |
| `turn-around-time` | 2 sqrt(2 ln 2) sqrt(mkT)/qE | 0.93%, inside the 4000-ion sampling error |
| `thermal-emittance` | sigma_x sqrt(kT/m)/v | 0.72%, inside the 6000-ion sampling error |
| `einzel-lens`, `quadrupole-dc`, `rectilinear-trap-extraction` | energy drift in a static field (ACC-4) | 4.2e-9, 6.4e-10, 1.5e-8 |
| `quadrupole-rf-stable` / `-unstable` | the tabulated Mathieu cut-off q = 0.90804 | transmits 1.0 at q = 0.70, 0.0 at q = 0.95 |
| `ion-funnel-rf` / `-no-rf` | the RF is what confines | threads the stack; lost on a named ring |
| `travelling-wave-guide` | an ion is carried the length of the guide | arrives |

The quadrupole pair is the one worth noticing: neither number comes from this
engine, and **bracketing a published boundary from both sides is a stronger claim
than either model alone**.

**Two defects came out of writing the first seventeen**, both of the kind no test
written from inside the project would catch, because both were about a model that
validates and answers a different question. An unrecognised property was ignored
rather than refused, and a transmission of zero could not be expressed. Both are
recorded in [Spec findings](spec-findings.md) as amendments.

**What is missing is breadth**: no multipole above four rods, no three-dimensional
trap, no MR-TOF, and nothing in the diffusive mode - which `transitTime` now makes
assertable and which nothing yet asserts.

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
- **A trajectory run still has no cost estimate.** A diffusive one now does, exactly
  - its step is stability-limited and computable before the run - but trajectory
  integration's cost depends on the path, which depends on the field, which is the
  thing not yet solved. `einzel estimate` reports the solve and says the integration
  is not included.
- **A density is reported at the end of the run and at no other instant.** It is
  exported as `.vti` and drawn as decade contours, but a model whose ions have all
  arrived leaves an empty box — correctly, and it says so. Seeing the packet in
  flight means shortening `maximumFlightTime` and running again.
- **Collisions are elastic only.** No fragmentation, no collision-induced
  dissociation, no internal energy. An ion that scatters keeps its identity.
- **One pressure for the whole model.** A differentially pumped instrument has
  several, and the interfaces between them are where much of the interesting
  physics is.
- **3D runs coarse and slow.** A solve costs the cube of its resolution. The
  segmented quadrupole template solves at 8.5 cells across r0 where the plane
  studies use sixteen, and at eleven it does not finish in ten minutes.
- **A 1 mm segment gap is not resolved.** The segmented quadrupole's mid-section
  field is converged to 0.014% under refinement; the field *inside a gap between
  sections* moves 2.4% then 1.4% and is still moving at the finest mesh that
  finishes. So the template shows that segments at different working points can be
  declared, decomposed and solved, and does not yet show what the joins do to an
  ion. See [Device templates](device-templates.md).
- **3D multigrid coarsens with node-aligned levels, not Galerkin ones.** Cut cells
  on the finest level, node-aligned geometry below, and a guard that refuses a
  level whose coarsest cell exceeds the smallest electrode. That is a working
  hierarchy - a sphere solve went from 13 s to 783 ms at the same answer - but the
  coarse operator is still rebuilt from the geometry rather than derived from the
  fine one, so a level that merges two nearby electrodes is a crude preconditioner
  rather than a wrong one. Galerkin coarsening would remove the guard rather than
  tune it, and it is not done. See [Numerics](numerics.md).
- **A figure is checked for structure, not for looking right.** The render tests
  assert that conductors, equipotentials and a trajectory are present, that every
  coordinate lands on the page, that labels are text in both formats, that the PDF
  cross-reference table is valid, and that the decimation bound is respected. None
  of that says the drawing is *legible* - that is a judgement no test makes, and
  the figures should be looked at when they change.
- **Electrodes are solid, with no way to say otherwise.** Real instruments use
  mesh and grid electrodes that pass most of the beam. There is no way to declare
  one, and a mesh cannot be modelled as its wires either, because the wires run
  along the invariant axis of a 2D solve.
- **Space charge is modelled by direct summation and by nothing else.** The
  pairwise sum SC-1 names as the reference exists and is checked; the *approximate*
  method it is meant to validate — particle-in-cell — does not.
- **The gas has one bulk velocity, not a velocity field.** A uniform
  `driftVelocity` is honoured by both transport modes, and the diffusive one takes
  it at the face so it conserves. What spec figure 4 actually requires above
  10⁻² mbar is a velocity *field*, and the jet off an inlet capillary is not uniform
  across a ring stack. `IGasFlow` is the seam; `UniformGasFlow` is the only
  implementation. Until an imported field exists, a funnel's transmission is
  computed in a gas that is either standing still or moving all in one piece.
- **A scan reports where a transition is, not what value it is at.** ACC-6 asks for
  a boundary resolved to one part in five hundred of the scan variable, which needs
  a bisection onto the transition; `einzel scan` reports the steepest interval on
  the grid it computed and how coarse that grid is. Class B proper — peak shape
  against a scan line, secular frequency against notch width — is not built.
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
