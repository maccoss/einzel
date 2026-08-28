# Validation

What is tested, what each tier proves, and — as importantly — what is not covered.

570 tests across nine assemblies. Warnings are errors; XML documentation is
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
| The same, bisected to ACC-6 rather than scanned | **q = 0.90508 ± 0.00039** in 11 evaluations |
| Mass-filter band centre against the tabulated apex q = 0.70600 | 0.68992, 2.28% below, approached monotonically |
| Boundary bisection against a step placed at a known value | bracket contains it, 1 part in 512, 11 evaluations |
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
| An imported uniform gas field against a declared uniform one | agree to 2 ulps |
| Diffusive centroid at cell Peclet 105 and 209 | **1.000000** and 0.999999 |
| Diffusive transit against L/(muE) and L/(muE + v_gas) | 0.86% each, the same either way |
| Slit transmission against erf(a / sigma root 2) | 0.6815 against 0.68269, 0.17% |
| Multipole maximum rod ratio against sin(pi/N)/(1 - sin(pi/N)), N = 4..12 | 1e-12 |
| The same at N = 4, against Denison's published 1.1468 | 1.14675 |
| Basis solves against pole count, 4 / 6 / 8 / 12 rods | 1 / 1 / 1 / 1 |
| Trilinear gas-flow sampling against a linear field | exact to 1e-9 |
| VTK ImageData written by this engine and read back | every node exactly |
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
makes them a release gate. **Twenty-six exist**, and the gate is built: every example
is materialised into a real project and driven through `einzel test` via
`Program.Main`.

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
| `travelling-wave-capture` | the distance over the **wave's** speed, 27 mm / 3000 m/s | 8.697 µs against 9.0 |
| `travelling-wave-ballistic` | the same distance over the **injection** speed, 1500 m/s | **exactly 18.000000 µs** |
| `gas-flow-carry` | L / u with no field, 1 m / 200 m/s | 4904.5 µs against 5000 |
| `paul-trap-held` / `-ejected` | the tabulated 3-D trap boundary q_z = 0.90804 | confined at q_z = 0.30, lost at 1.20 |
| `hexapole-guide`, `multipole` orders | the rods fit and the ion is guided | arrives |
| `slit-transmission` | erf of a slit with the field exactly zero | 0.95σ at 20,000 ions |
| `drift-tube-diffusion`, `drift-tube-gas-flow` | L / (μE) and L / (μE + u) | the diffusive mode |

**Two pairs and a control are what carry the weight here.** The quadrupole pair
brackets a published boundary from both sides, which is a stronger claim than either
model alone; the Paul trap pair does the same for the three-dimensional boundary,
deliberately wide, because an example that pinned the edge would be pinning *this
geometry's* edge and calling it Mathieu's.

The travelling-wave pair is the sharpest of the three and the reason is worth stating.
Injected at **half** the wave speed, the ion covers 27 mm in 8.697 µs with the wave on
and in exactly 18.000000 µs with it off. A transit that matched the wave in one case
and the injection speed in the other would be a coincidence twice over; **a transit
that matches the wave whatever the injection speed is capture.** An earlier version of
this measurement compared two *captured* transits to each other, found them 0.75 µs
apart, and concluded there was no capture — two numbers being close proves nothing when
their ballistic values were close too.

And `gas-flow-carry` is far more discriminating than its ten per cent tolerance
suggests. With the declared flow ignored, the same ion damps to rest and covers
**15.8 mm in twenty milliseconds** rather than arriving at all — so the check is not
"is the transit about right" but "is the gas moving at all".

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
- **A gas flow reaches the diffusive mode only.** An imported velocity field
  (GAS-1) is read, sampled and conserved at the face, but `CollisionSampler`
  schedules and draws a neutral velocity without a position, so the event-driven
  mode refuses a field rather than falling back to the uniform value. A uniform
  `driftVelocity` works in both.
- **The pressure is still one number.** GAS-1 asks for a pressure *field* beside
  the velocity field, and a differentially pumped instrument has several.
- **Only ASCII VTK is read.** Binary, appended and compressed payloads are the
  majority of real VTK files and none is read; such a file is refused by name
  rather than misread.
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

## The secular spectrum, against the Mathieu characteristic exponent

An ion in an RF field moves on two timescales: a slow **secular** oscillation in the
effective well and a fast **micromotion** at the drive. Mathieu theory puts the lines
at `(2n ± β) Ω / 2` for integer `n`, with β given by a continued fraction in `a` and
`q` alone — a closed form this engine has no part in, evaluated in the test rather
than shipped, because a test comparing the engine's β to the engine's spectrum would
be testing self-consistency.

| q | β (closed form) | expected | measured | departure |
| --- | --- | --- | --- | --- |
| 0.10 | 0.070850 | 35.425 kHz | 35.374 kHz | −0.144 % |
| 0.30 | 0.216059 | 108.030 kHz | 108.037 kHz | +0.007 % |
| 0.50 | 0.373744 | 186.872 kHz | 186.937 kHz | +0.035 % |
| 0.70 | 0.563066 | 281.533 kHz | 281.500 kHz | −0.012 % |
| 0.85 | 0.772950 | 386.475 kHz | 386.507 kHz | +0.008 % |

Every one is inside the record's own resolution of 5 kHz, which is what the
assertion is against — a periodogram over 200 µs cannot locate a line more finely
than 1/T however finely the trial frequencies are spaced, and the reported interval
is that width.

**The sidebands are the sharper check.** Finding the lowest line in the right place
says the slow motion has the right frequency; finding the drive split into a *pair*
straddling it says the motion has the form Mathieu's solution gives. At q = 0.5:

| line | expected | measured |
| --- | --- | --- |
| secular, n = 0 | 186.87 kHz | 186.89 kHz (power 0.958) |
| lower sideband, n = 1 | 813.13 kHz | 813.19 kHz (power 0.036) |
| upper sideband, n = −1 | 1186.87 kHz | 1186.94 kHz (power 0.009) |

**Lomb–Scargle rather than a DFT, and that is the load-bearing choice.** A trajectory
is sampled at accepted integration steps, which cluster where the physics is hard —
`TrajectoryRecorder` working as designed — so the series is *not* uniformly spaced. A
DFT would need it resampled onto a uniform grid first, which is inventing values the
integrator never computed and then measuring them. Lomb–Scargle is the closed-form
least-squares fit of a sinusoid at each trial frequency and needs no such step.

## Two independent routes to the same effective radius

The Paul trap's electrodes are flat annuli, so the field at its centre is stronger
than its declared 4 mm inscribed radius implies. That effective radius is measurable
two entirely different ways, sharing nothing but the solved field:

- **From the field.** The curvature at the centre, `dEz/dz = 2V/r0²`, with no ion
  involved at all. **3.8195 mm.**
- **From a trajectory.** Fly an ion for two hundred RF cycles, take the periodogram,
  and compare the secular line against Mathieu's closed form evaluated at
  `q × (r0/r0_eff)²`.

| amplitude | q nominal | q effective | predicted | measured | ratio |
| --- | --- | --- | --- | --- | --- |
| 200 V | 0.2444 | 0.2681 | 96.154 kHz | 96.133 kHz | **0.9998** |
| 300 V | 0.3666 | 0.4021 | 147.095 kHz | 147.035 kHz | **0.9996** |
| 400 V | 0.4888 | 0.5361 | 202.327 kHz | 201.750 kHz | 0.9972 |
| 600 V | 0.7332 | 0.8041 | 347.360 kHz | 336.413 kHz | 0.9685 |

**Two hundredths of a per cent at low q**, from a field curvature and a flight time.
And the departure at high q is the other half of the same statement rather than a
failure: the trap is an ideal quadrupole of radius 3.82 mm *to the extent the ion
stays small*, and stops being one as the excursion grows. That is the anharmonicity
arriving on schedule, and it is why the stability boundary — which is measured by an
ion travelling all the way to an electrode — cannot be predicted from the effective
radius alone.

## Naming a nonlinear resonance

The same trap loses its ion in a narrow band at 605–614 V, sixty volts inside what
the Mathieu chart calls stable. A loss scan can establish that the band is real —
identical at twice the mesh and twice the hold, absent at a quarter of the hold and
at a third of the launch offset — and can never say *what* it is, because a
resonance is defined by a condition on frequencies.

| amplitude | β_z | β_r | best condition | value | miss |
| --- | --- | --- | --- | --- | --- |
| 560 V | 0.6109 | 0.2809 | 2β_z + 2β_r | 1.7836 | 0.2164 |
| **610 V** | **0.6769** | **0.3225** | **2β_z + 2β_r** | **1.9989** | **0.0011** |
| 660 V | 0.7500 | 0.3507 | 6β_r | 2.1042 | 0.1042 |

`n_z β_z + n_r β_r = 2` at order four is an **octupole** resonance, met to 0.055 per
cent at the band centre and a hundred times worse either side.

**Why that is an identification rather than a fit.** Nine candidate conditions are
searched and one of them is always nearest, so a near miss on an arbitrary one would
prove nothing. Order four is the one predicted in advance: the trap is symmetric
about its own centre plane and about the axis, so every odd multipole vanishes and
four is the first order available. And it is independently corroborated by the field
measurement — the curvature ratio `dEz/dz ÷ dEr/dr` departs from its exact −2 by an
amount that *grows with radius*, which is an even multipole seen without flying
anything.

Note also that the ideal-Mathieu prediction fails here and had to: β at the *nominal*
q of 0.745 is 0.6156, which satisfies no low-order condition at all. The measured
0.6769 is the one the resonance condition is about, and the difference between them
is the effective radius plus the anharmonic shift.

## The arbitrary waveform, and isolation efficiency against notch width

§9 lists an arbitrary waveform among the excitations an electrode may carry, and
§12's last unbuilt Class B figure — isolation efficiency against notch width —
cannot be measured without one. It is a **Fourier series**, which is not a
restriction on "arbitrary": every periodic waveform is one, and a notch is more
naturally written as a list of harmonics with a gap in it than as the samples of
whatever waveform happens to have that spectrum. It is also smooth by construction,
where a sampled table is piecewise something and a discontinuity in the drive is a
discontinuity in the acceleration.

### What it must reduce to

**A single term of order one is the sinusoid**, to 6.1e-16 across 721 phases.

**A half turn of phase is exactly antiphase** where the argument is representable —
exactly zero at phases k/16, and 1.0e-14 at arbitrary phases across orders 1 to 17.
That second number is the honest limit: `2(nt + ½)` is itself rounded and the error
grows with the order, so the exactness is a property of the convention at
representable phases rather than a guarantee at every instant of a flight.

**The Fourier series of a square wave converges on the square wave**, with textbook
Gibbs behaviour:

| terms | interior worst | edge overshoot |
| --- | --- | --- |
| 5 | 0.182 | 0.182 |
| 20 | 0.051 | 0.179 |
| 80 | 0.013 | 0.177 |

The interior falls by fourteen-fold; the edge overshoot does not fall at all. **A
series that showed no overshoot would not be a Fourier series**, so both are
asserted.

**And it recovers the published digital cut-off.** Schrader, Anderson and Russell
(JASMS 2024) put the square-wave low-mass cut-off at q = 0.712. Driving the same
geometry two ways:

| waveform | last q through | first q lost |
| --- | --- | --- |
| rectangular, direct | 0.710 | 0.715 |
| the 80-term series | **0.710** | **0.715** |

Same bracket, containing the published number. That is the check that says the
arbitrary-waveform path *drives an ion* rather than merely evaluating to the right
numbers.

**A mistake worth recording**: the first version wrote the series with zero phase on
every term. A square wave's series is a **sine** series, so that is a square wave
shifted a quarter cycle — a different waveform, which converged perfectly well and
moved the cut-off to about 0.703. **A reduction that converges to the wrong thing is
worse than one that does not converge**, because convergence is the thing being
checked and it looked fine.

### Resonant ejection

A supplementary uniform field oscillating at the ion's own secular frequency pumps it
until it leaves; the same amplitude off resonance does almost nothing. Measured at
q = 0.4, secular 146.27 kHz, 400 V/m over 1200 RF cycles:

| excitation | excursion | outcome |
| --- | --- | --- |
| on resonance | 4.000 mm | **ejected** |
| half the frequency | 0.264 mm | held |
| twice the frequency | 0.133 mm | held |

### Isolation efficiency against notch width

A comb of harmonics with a band removed. An ion's secular frequency is set by its
Mathieu q and q goes as 1/m, so **a mass axis is a frequency axis** and a notch in
frequency is a window in mass. With the target at m/z 500, q = 0.4, secular
146.28 kHz — comb order 73.1 — and a notch over orders 71 to 75:

| m/z | q | secular | order | excursion | survived |
| --- | --- | --- | --- | --- | --- |
| 420 | 0.4762 | 176.94 kHz | 88.5 | 4.000 mm | no |
| 460 | 0.4348 | 160.08 kHz | 80.0 | 4.000 mm | no |
| 490 | 0.4082 | 149.50 kHz | 74.7 | 2.308 mm | **yes** |
| 500 | 0.4000 | 146.28 kHz | 73.1 | 1.513 mm | **yes** |
| 510 | 0.3922 | 143.21 kHz | 71.6 | 1.932 mm | **yes** |
| 545 | 0.3670 | 133.44 kHz | 66.7 | 4.000 mm | no |
| 600 | 0.3333 | 120.58 kHz | 60.3 | 4.000 mm | no |

**The trade needs two amplitudes to show both its arms**, and that is a finding
rather than a test artefact:

| half-width | just ejecting (76 V/m) | three times that (229 V/m) |
| --- | --- | --- |
| 0 | **1.00** | 0.00 — target lost |
| 2 | 0.50 | 0.00 — target lost |
| 6 | 0.00 | **0.75** |
| 12 | 0.00 | 0.25 |

At the amplitude that just ejects a resonant ion the narrow end is free — the
target's off-resonance response is 1.5 mm against a 4 mm aperture — so efficiency is
simply monotone in width. Push three times harder and the narrow end starts losing
the target, and **an interior optimum appears at half-width 6**. Efficiency counts a
run that lost the target as zero however many neighbours it ejected, which is not a
scoring convention: a purified sample of nothing is not a purification.

### Two scales that had to be derived rather than chosen

Both first versions were wrong by orders of magnitude, in ways that produced
plausible tables.

**The comb spacing must equal 1/T.** A resonance excited for a time T has a width of
about 1/T, so a comb spaced more widely has *holes* — an ion falling between two
lines is driven by neither and survives an excitation meant to eject it. A first
version used 5 kHz against a 333 Hz width, and every result was nonsense in an
interesting way: the notch width toggled every ion at once, because selectivity had
nothing to do with it.

**The amplitude follows from the aperture and the duration.** A resonantly driven
oscillator grows linearly, `x(t) = (qE/m)t/2ω`, so reaching the aperture `a` in a
time `T` needs `E = 2amω/qT` — 76 V/m here. A first version used 300 V/m, four
orders too much, and ejected every ion at every notch width. **An amplitude picked to
make a demonstration work is a demonstration of the amplitude.**

### What this does not yet reach

The measurement runs on the analytic quadrupole, not on a solved geometry, because
**the model format cannot declare a supplementary excitation**: a `solve` carries one
`drive` with one frequency, and a stored-waveform isolation needs the main RF and a
low-frequency comb at once, on different electrodes. That is the same limitation
already recorded for the travelling-wave guide, which needs a fast confining RF
superposed on a slow travelling wave. The mechanism is built and validated; what is
missing is a way to *say* it in a document.
