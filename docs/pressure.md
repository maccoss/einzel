# Pressure, collisions, and regime validity

Ions in this engine now fly through gas. Two collision models, the dimensionless
numbers that say whether either applies, and a transport-mode seam that refuses the
mode it does not have.

## The seam REG-1 asks for, and why it exists before its second implementation

`ITransportMode` makes trajectory integration and statistical diffusion **peers**
rather than one being a special case of the other. Spec figure 4 is emphatic about
why: these are different descriptions of different physics, not the same calculation
at different settings. Above about 10⁻² mbar there are no trajectories to compute;
below it there is no density field.

Both are now built, and a model selects one by name: `"mode": "trajectory"` or
`"mode": "diffusion"`. The seam existed before its second implementation did, on
purpose — a mode selected by an `if` somewhere in the run command falls silently
through to whichever was implemented first, where a registry can refuse by name and
say what is missing.

`ProducesTrajectories` is on the interface so a renderer asks the mode rather than
inferring it from the pressure. TRN-2 and RND-8 forbid drawing lines through a
diffusive region, and that rule needs something to ask.

## Two collision models, and why the Langevin one is cheaper

| Model | Regime | For |
| --- | --- | --- |
| `hardSphere` | below ~10⁻⁵ mbar | Residual-gas scattering, the arrival-time pedestal |
| `langevin` | 10⁻⁵ to 10⁻² mbar | Trap and guide damping, thermalization |

**The Langevin rate does not contain the speed.** The polarization capture cross
section goes as 1/v and the rate is the product, so it is a constant — which makes a
Langevin collision a Poisson process with a fixed rate and the time to the next one
a plain exponential draw. That is physics, not convenience: it is also why mobility
in the polarization limit is temperature-independent.

k = q√(πα/ε₀μ), with α as a polarizability volume. For m/z 500 in nitrogen that is
**5.998e-10 cm³/s**, which is where every published Langevin rate coefficient sits —
they cluster near 1e-9 because the only thing that varies is a reduced mass under a
square root.

**Hard spheres need the null-collision method.** The rate depends on the relative
speed, which changes as the ion flies, so events are scheduled at a rate that bounds
the true one from above and each is then accepted with the ratio of the two. The
bound carries five most-probable gas speeds of headroom; a sampled relative speed
that exceeded it would bias the rate low, so `BoundExceeded` reports it rather than
hiding it. The null fraction is reported too, because it is the cost of the method.

Scattering is exact elastic kinematics — relative speed unchanged, isotropic in the
centre of mass — rather than a drag coefficient. The polar angle is drawn from a
uniform **cosine**, not a uniform angle; the latter is the classic mistake and
concentrates directions at the poles, which shows up as too little randomisation of
transverse motion.

## What was checked, against what

Three targets, none of which this code produced.

| | |
| --- | --- |
| Langevin rate coefficient vs its closed form | exact to 1e-12 |
| Scheduled collision rate vs n·σ·⟨g⟩ | **0.9995** over 12,076 collisions |
| Thermalisation to (3/2)kT after 288 collisions | **0.9524** |
| Low-field mobility vs Mason–Schamp | **1.0127 ± 0.0373**, a 0.34σ discrepancy |
| A vacuum flight, with and without a sampler attached | bit-identical |

**Equipartition is the sharpest of these** because it is exact and is not something
the code knows: an ion left in a gas must arrive at (3/2)kT whatever it started
with. It tests the scattering kinematics, the Maxwellian draw, and the isotropy all
at once — get the centre-of-mass share wrong and the ion settles at the wrong
temperature.

**Mason–Schamp is the literature regression.** The ions get their drift by
colliding; the closed form is never used to move anything. So agreement is a
statement about the kinematics rather than about the estimate.

That test also taught a lesson about its own precision. A first version used 40 ions
and reported a ratio of 0.935, which reads as a 6.5% discrepancy. It was **one and a
half standard errors**: a drifting ion also diffuses, and over that flight the
diffusion length is comparable to the drift, so a single ion's displacement carries
~45% of spread. The test now computes its own standard error and asserts against it
rather than against a band chosen to fit.

## Two integrator changes a gas forced

**A collision is an instant**, so it is the same kind of event as a sequencer switch
— a known time — and lands the same way: the step is cut to it, with no root-find
and no new machinery. A step that spanned one would average the velocity either side
of the discontinuity that is the entire physics being modelled.

**The analytic field-free drift had to be bounded by it, not disabled.** A drift
advances the ion in one shot and jumped straight over every scheduled collision —
and that is the path a long field-free flight in a thin gas takes, which is exactly
the residual-gas case. Bounding is correct rather than merely convenient: between
two collisions the motion really is a straight line, so the analytic advance is not
an approximation a gas invalidates, it is the exact solution over a shorter interval.

**The turning-point step cap is fatal in a gas, for the same reason it is fatal in a
driven field.** An ion thermalising in gas spends its whole life near a velocity
minimum, so the cap fires continuously. The symptom is not a slow run but a *wrong
outcome*: an ion drifting in 1 mbar of nitrogen underflowed after eight steps and
32 ns of a 300 µs flight and reported `StepSizeUnderflow` — a numerical failure
standing in for ordinary physics. It is now off whenever collisions are present, as
it already was for a driven field.

## REG-2 is engine behaviour, not documentation

Every run with a declared gas reports its governing numbers, whether or not any of
them crosses a threshold — a reader who sees a Knudsen number of 40 knows the run
was checked, where a reader who sees nothing cannot tell that from its not having
been checked at all.

| Warning | Severity | Fires when |
| --- | --- | --- |
| `regime.trajectory-above-validity` | ValidityViolation | above 10⁻² mbar: no trajectories exist here |
| `regime.overlap-band` | Qualified | 10⁻³ to 10⁻² mbar: both descriptions run, neither is obviously right |
| `regime.knudsen-continuum` | Qualified | mean free path below the tightest aperture |
| `regime.collisions-outrun-rf` | ValidityViolation | more than one collision per drive cycle |
| `regime.model-below-validity` | Qualified | hard spheres above 10⁻⁵ mbar |
| `regime.model-above-validity` | Qualified | Langevin capture below 10⁻⁵ mbar |

None is suppressible; GRD-3 allows only advisories to be silenced and none of these
is advice. The Knudsen number is taken against the **tightest constriction** in the
model rather than the size of the instrument: a gas is a continuum on the scale of
an aperture long before it is one on the scale of a flight tube.

**A regime violation gets its own exit code.** `run` returns 2 (`RegimeViolation`)
rather than 4 (`ConvergenceFailure`) when one is present, because it explains the
other: an ion that never reaches the detector at 1 mbar has not failed to converge,
it has been described by the wrong physics.

## A collisional flight time has no honest interval

The single-ion convergence study still flies the gas — reporting a vacuum flight
time for a model that declares a pressure would be exactly the silent substitution
the validator refuses elsewhere — but the interval it produces measures the
integrator and **not** the stochastic spread. Worse, the two are not independent: a
slightly different state at a scheduling instant maps the same uniform draw to a
slightly different collision time, and that compounds.

So a collisional single-ion flight time carries `collisions.single-ion-interval`,
and the number whose interval means what it looks like is the ensemble one.

## COL-1: scattered ions are kept

An ion that scattered and still reached the detector is tracked to it with its
arrival time recorded, not discarded as a loss. Those late arrivals are the pedestal
under the peak; dropping them would make an instrument look cleaner than it is. The
ensemble reports `collisions` and `scatteredIons` beside the arrival times, because
neither is visible from a transmission figure — which counts a scattered arrival and
a clean one identically.

## What the funnel says, now that it can have gas

The device whose absence of gas hurt most. An ion entering 6 mm off axis:

| pressure | outcome | exit radius | radial speed | collisions |
| --- | --- | --- | --- | --- |
| vacuum | arrived | 1.588 mm | 864.6 m/s | – |
| 1e-3 mbar | arrived | 1.588 mm | 864.6 m/s | **0** |
| 1e-2 mbar | struck a ring | 1.933 mm | 870.8 m/s | 4 |
| 1e-1 mbar | still in flight | **0.423 mm** | **255.7 m/s** | 1455 |

**The damping is real and visible** — 864.6 m/s of radial speed becomes 255.7, and
1.59 mm of exit radius becomes 0.42. That is a funnel doing what a funnel is for,
and none of it existed before there was a gas.

Two awkward things it also shows. At 1e-3 mbar an ion crosses this funnel in a few
microseconds and collides with **nothing at all** — the trajectory is bit-identical
to the vacuum one — so the damping only appears at or above the validity boundary of
the mode computing it. And at 1e-1 mbar the ion is damped axially too and never
leaves inside the flight-time ceiling: a real funnel is pushed through by a gas
flow, and a stationary gas has no such push.

Neither is a bug. Both are the reason this device wants the diffusive mode rather
than a longer ceiling. At the 2 mbar a funnel actually runs at, the regime check
reports a mean free path of **21.4 µm**, a Knudsen number of **0.0143**, and **29
collisions per RF cycle** — an ion that never completes an oscillation, so the
pseudopotential the whole device is designed around does not exist for it.

## Statistical diffusion, the second mode

REG-1 makes trajectory integration and statistical diffusion peers, and the second
one now exists: a drift-diffusion solve on a grid, with **mobility as a declared
input** (TRN-1) and **a density field as the output** (TRN-2). There are no
trajectories in it to draw even if something wanted to, which is what RND-8 needs to
be checkable rather than merely stated.

**The flux uses Scharfetter–Gummel**, the exponentially-fitted upwind form. That
matters for the same reason cut cells did in the field solver: centred differencing
here is not merely less accurate, it oscillates and produces negative densities as
soon as drift outruns diffusion, which in a funnel it does everywhere. A negative
density is not a small error — it is a quantity that has stopped meaning anything.

| | |
| --- | --- |
| Free diffusion against √(2Dt), three times | **1.0000** each |
| Drift against μE | **1.00000** |
| Boltzmann equilibrium, seeded and evolved | **1.00000** over three decades of density |
| Ion conservation, every loss named | 100.0000% |
| Lowest density at cell Péclet 483 | non-negative |

**The Boltzmann check is the sharp one**, and it is sharp because the scheme is
*built* so that its zero-flux state is exactly the Boltzmann factor: setting the flux
to zero gives n_there/n_here = B(−P)/B(P) = exp(P), and P is precisely qΔφ/kT. So the
discrete equilibrium is the continuous one, not an approximation converging to it —
and anything worse than a per cent is a bug.

**It found one.** A first version sampled the drift at the *cell centre* and used it
for both of that cell's faces. Where the field is uniform that is the same thing;
where it varies, the two cells sharing a face disagree about how much crossed it, and
the scheme stops conserving. A seeded equilibrium in a well drained from the middle
at 4.7× per millisecond. The conservation test **passed throughout**, because its
field was uniform — a test that passed for a reason that did not generalise.
Everything about a face is now a property of the face: the exponent from the
potential difference between the two nodes, the diffusivity from their average.

## REG-3: both modes, one physics

For the comparison to mean anything the two modes must describe the **same gas**, so
the event-driven side uses hard-sphere scattering off a declared cross section and
the diffusive side takes its mobility from that same cross section through
Mason–Schamp. Comparing Langevin capture against a mobility fitted to something else
would be comparing two instruments and calling the difference a numerical
disagreement.

At 10⁻² mbar and 6.2 townsend — inside both validities:

| | |
| --- | --- |
| Trajectory, 200 ions | 13.2555 ± 1.3584 m/s |
| Diffusion, 4891 steps | 13.8418 m/s |
| **Disagreement** | **0.43 standard errors** |

One samples exponential waiting times and rotates velocity vectors; the other pushes
a density through Bernoulli-weighted faces. They share a cross section and nothing
else.

**The field has to be chosen against E/N, not picked.** At this pressure the gas is
thin and the mobility is 9.2 m²/Vs, so 40 V/m is **166 townsend** — deep into where
the ion is heated by the field. There the two modes legitimately disagree:

| | |
| --- | --- |
| Trajectory at 166 Td | 265.2 ± 4.1 m/s |
| Low-field μE | 369.4 m/s |
| **Overstated by** | **1.393×** |

The event-driven mode gets this right without being told, because it is colliding an
ion that is genuinely moving faster. The diffusive mode is only as good as the
mobility it was handed — which is what TRN-1 means by an explicit input with stated
field dependence, and why `Mobility.IsWithinFit` returns false here rather than
leaving the caller to work it out.

## Diffusion from a model document

`"mode": "diffusion"` now runs. A source becomes an initial density — a Gaussian at
the source position with the cloud's declared spreads, normalised to the declared
population — a detector becomes a collecting boundary, and an electrode becomes a
region ions flow into and do not come back from.

A diffusive result has **no flight time**, and the absence is stated rather than
filled in: `transport.no-flight-time` is on the envelope, and what a density has
instead is a transit-time *distribution*, a transmission, and a spread.

```
einzel run models/tube.json          # a density, not trajectories
einzel compare models/tube.json      # both modes, and the disagreement
```

Checked against a closed form: μE at 1 mbar is 184.7 m/s, so 38 mm from source to
detector takes **206 µs** — and the run reports **206.6 µs**.

**Mobility is derived when not declared, and says so.** TRN-1 wants it measured;
deriving it from the cross section by Mason–Schamp is offered so both modes can
describe the same gas, and `mobility.derived` marks every result that used one.

## REG-3 as a supported operation

`einzel compare` runs both modes on one model and reports the difference **in units
of the trajectory ensemble's own standard error** — because a relative difference
with no error beside it cannot tell a real disagreement from an under-sampled
ensemble, which is the mistake this engine's own first mobility check made.

Building it surfaced three ways the comparison can be meaningless, all now warned
about and none of which were obvious beforehand:

- **`regime.comparison-mismatched-mechanism`.** A model declaring Langevin capture
  for the event-driven side while the diffusive side derives its mobility from a
  cross section is comparing rigid-sphere against polarization capture. Different
  scattering, so the disagreement is between the two *inputs*.
- **`regime.comparison-incomplete`.** A mean transit over the subset that arrived is
  not a transit time, and the two subsets are not the same ions. At 6.2 townsend the
  drift was so weak that 0% of the density and 70% of the ions reached the collector
  in the available time, and the modes "disagreed" by 145% — almost all of it the
  ceiling.
- **`regime.comparison-unmatched-boundaries`.** The density grid has edges and a
  bare trajectory model does not. With no declared geometry an ion flies to the
  detector however far off axis it wanders, while the density is absorbed at the
  edge of its box: 89% of it, in the case that exposed this. **The two modes were
  being asked about different instruments, one with walls and one without.**

With those understood, the honest reading of a 1e-2 mbar comparison at 82.8 townsend
— trajectory 252.1 ± 6.8 µs against diffusion 174.1 µs, 1.45× apart — is not that
the solvers disagree. It is that the low-field mobility is outside its fitted range,
which `mobility.outside-fit` says on the same report.

## What a diffusive run costs, before it starts

GRD-8 gates on a number that has to be available without doing the work. For a
diffusive run that number is **exact**, not modelled: the step is set by two
stability limits, both computable from the mesh, the mobility and the field.

| | predicted | actual |
| --- | --- | --- |
| 1 mbar, 257×65, drift-limited | 901 steps, 6.27 s | 901 steps, 5.30 s |
| 10⁻² mbar, 129×33, diffusion-limited | 6,266 steps, 11.11 s | 6,266 steps, 9.21 s |

**Step counts exact in both**, because `estimate` and `run` call the same function —
an estimate computed by a second implementation of the step rule is an estimate of
that implementation. Wall time is deliberately conservative by ~20%, from a measured
2.4 million cell updates per second against 2.4–3.0 observed.

The drift limit needs the field. Where every element is analytic that is free to
sample, so it is included and the estimate is exact; where anything must be solved it
is omitted and the basis line **says so**, because solving the field to estimate the
cost of the run defeats the point.

**The direction is the part worth reading.** D goes as 1/P, so a *thinner* gas
diffuses faster, needs a smaller step, and costs more. In the table above a hundredth
of the pressure on a **six times coarser grid** is still twice the work. That is the
opposite of the event-driven mode, where a thinner gas means fewer collisions, and it
is the opposite of most people's intuition — so the basis line says it in words as
well as numbers, because a number that surprises without explaining itself gets
worked around rather than understood.

## RF in the diffusive mode

Between about 1e-2 and 10 mbar neither transport mode could describe a driven
structure. Trajectory integration is outside its validity there; the diffusive
mode steps a density through one static field, and a driven structure has none.
That band is where ion funnels, travelling-wave guides and collision cells run,
so the funnel's acceptance figures were a lower bound and the travelling-wave
guide could not be operated the way a real one is.

`PonderomotiveField` wraps a driven field as the cycle average a slow ion feels.
The solver needs no change: it asks for a potential at a point and gets the
effective one, which is the same thing `AxisymmetricField` does for a half-plane
solve.

### The collisional well, and why it is not the textbook one

An ion quivering in E0 cos(Omega t) and damped at rate nu obeys
m(v' + nu v) = q E0 cos(Omega t). Solving for the steady quiver and averaging
q (dE0/dx) delta over a cycle gives the gradient of

    Psi = q^2 E0^2 / (4 m (Omega^2 + nu^2))

which is Dehmelt's q^2 E0^2 / (4 m Omega^2) when collisions are rare and is
**suppressed by Omega^2/(Omega^2 + nu^2)** when they are not. Every textbook
writes the collisionless form; at the pressures these devices run at, that is an
overestimate.

| Ion funnel, 2 mbar N2, 1 MHz, m/z 500 | |
| --- | --- |
| Mobility, Mason-Schamp from the cross section | 0.04618 m^2/(V s) |
| Momentum-transfer rate | 4.179e6 /s |
| Drive | 6.283e6 rad/s |
| **Suppression** | **0.693** |
| Largest quiver on the grid | 0.849 mm |
| Cell | 0.312 mm |

So the collisionless pseudopotential overstates this funnel's confining well by
**44%** — and the quiver is larger than the mesh, which trips
`rf.quiver-exceeds-mesh`, a non-suppressible violation. Averaging over an
excursion only describes something if the field is roughly linear across it, and
at 100 V it is not.

### The damping rate is the momentum-transfer rate

nu = q/(m mu), from the mobility the solve already has, rather than from the
number of collisions per cycle. Two reasons, and the first is quantitative: a
heavy ion in a light gas gives up only about the mass ratio of its momentum per
collision, so for m/z 500 in nitrogen the collision count overstates the damping
by roughly twenty times. The second is that a separately estimated collision
frequency would be a second number for a quantity the drift term already fixes,
free to disagree with it.

### What is checked

Closed forms, because the ideal quadrupole has them: its RF field is exactly
linear in position, so the well is exactly harmonic.

| | |
| --- | --- |
| Collisionless well against Dehmelt | exact |
| Its curvature against the secular frequency q Omega / sqrt(8) | exact |
| Suppression at nu = Omega | exactly 0.5 |
| Suppression at nu = 10 Omega | exactly 1/101 |
| Quiver amplitude at nu = Omega | exactly 1/sqrt(2) |

The secular frequency is written in the Mathieu parameter rather than in volts,
because q is what every published quadrupole result is quoted in and it is the
one spelling of the geometry the test does not get to choose.

### Two mistakes worth keeping

A first version of the tests wrote the quadrupole's field amplitude as
V r / r0^2, and every closed form came out **exactly four times too small** —
that field's potential is V (x^2 - y^2) / r0^2, so its gradient carries a factor
of two. The tests now ask the field for its own amplitude by sampling it, which
removes the whole class of mistake: what is under test is the relation between
the field and the well, not who spells the field which way.

And `ResolutionLength` is **positive infinity** for an analytic field, meaning it
has no resolution limit rather than an enormous one. Reading it as a differencing
step gave a step of infinity, a difference of infinity minus infinity, and an
effective field of NaN — while every potential stayed correct, so only the
gradient was wrong.


## Electrodes absorb for the whole run, not only the seed

An electrode used to empty the *initial* density and nothing more. That stops a
source placed inside metal from starting there, which is the case that reads as an
instrument losing everything — and it does nothing at all about density that
arrives later. A funnel's rings shaped the field and then let the density pass
straight through them, so every diffusive transmission figure was an upper bound
with nothing saying so.

The mask is now built once and handed to the solver, which holds those cells at
zero at every step. A conductor is **an open boundary with a name**: the density on
the far side of the face is zero, so the Scharfetter–Gummel flux reduces to
`B(-P) n_here`, which is non-negative for any potential drop across the face. An
electrode can therefore only take and never give, and that falls out of the scheme
rather than needing a clamp — including when the field is pushing ions the other
way, which is the case a sign error would show up in.

| Check | Result |
| --- | --- |
| A wall across the channel, against the same run without it | 100.00% collected to 0.00%, all of it named on the wall |
| An electrode downstream of the source, nothing inside it at t = 0 | 100.00% lands on it |
| Ions conserved with an absorber and the field reversed | 100.0000% |

The control matters more than it looks. On its own "almost nothing was collected"
is equally consistent with a solver that lost the density somewhere, in a scheme
whose whole point is not doing that; what is asserted is the difference the metal
makes.

**The seed's own overlap now joins the same ledger.** It used to be deleted after
the launched population had already been counted, so launched, collected, remaining
and the named losses did not add up — and an itemisation that does not add up is
worse than none, because it reads as complete.

## The gas can move, and the diffusive mode now sees it

`transport.gas.driftVelocity` has been in the model format since the collision
models landed, and the event-driven side has always used it: a moving gas shifts
the Maxwellian the ion scatters off. **The diffusive mode ignored it entirely** —
declared, validated, carried through compilation, and dropped at the solver. That
is the same shape as the two evidence-discarding bugs already recorded here: a
declared input that one path honours and another silently does not.

Advection by a moving neutral **is not the gradient of anything**, so it cannot
enter as a potential difference. It enters the Scharfetter–Gummel exponent
directly, as `P_gas = v.n h / D`, which is the same exponent the field term already
is — by the Einstein relation `q(phi_here - phi_there)/kT` *is* `v h / D` — so the
two simply add and the scheme stays exact for a linearly varying total drift.

**Sampled at the face, averaged over its two nodes.** That is what keeps it
conservative: the neighbouring cell computes the same average with the opposite
sign, so the two cells sharing a face agree about how much crossed it. Sampling the
gas at the cell centre instead would repeat, exactly, the bug that made a seeded
Boltzmann equilibrium drain from the middle at 4.7x per millisecond.

| Check | Result |
| --- | --- |
| Centroid speed with no field, gas at 40 and 120 m/s | **1.000000** each |
| Centroid speed against muE + v_gas, gas plus and minus 60 m/s | **1.000000** each |
| A still gas against no gas velocity at all | bit-identical, every node |
| Ions conserved with a moving gas across a varying field | 100.0000% |

Tight on purpose. Scharfetter–Gummel is exact for a drift that varies linearly
across a cell and a uniform one trivially is, so the first moment is not an
approximation converging with the mesh — it is the scheme's own answer, and a band
wide enough for a discretisation error would accept a term that is merely the right
size. The reversed gas is the control: a sign error is invisible when the gas and
the field push the same way.

**Both cases are reported, per REG-2.** A model that declares a flow gets the ratio
that says which is carrying its ions — at 50 V/m and 30 m/s of gas, `gas.flow`
says 6.5 and "the gas is carrying these ions, not the field". A model that declares
*none*, above the 10⁻² mbar where spec figure 4 makes a velocity field a
requirement rather than a benefit, gets `gas.stationary-above-flow-threshold`.
A stationary gas is a modelling choice and it does not look like one in the output.

**The event-driven mode refuses a flow field rather than ignoring one.**
`CollisionSampler` schedules and draws without a position — `Collide` takes a time
and a velocity — so it cannot evaluate a velocity that varies with position. A
uniform `driftVelocity` it uses as it always did; a `Flow` object it refuses by
name, because the alternative is a run that quietly used the uniform value and flew
an ion through a declared jet as though the gas were standing still.

## A conservative operator, written twice, right once

The cylindrical Poisson operator is in conservative form — flux through a ring's
outer face minus its inner face, over the ring's own volume — and the reasoning is
recorded in [Numerics](numerics.md). **The density solver was not.** It computed a
flux per unit area and applied it as though the two cells sharing a radial face had
the same volume. In an axisymmetric solve they do not.

The weight a face needs is its area over the cell's volume, `A hy / V`, which is
identically **1** in the plane — so an isotropic solve multiplies by one and is
unchanged to the last bit — and `1 ± hy/2r` in a cylindrical one. **On the axis it
is 4**, because the inner face has no area and the cell is a disc rather than a
ring: the same factor of four the cylindrical Laplacian carries there, from the same
geometry.

The error was therefore largest on the axis, which is exactly where a funnel
concentrates its ions. On the shipped funnel at 2 mbar, cylindrical, with absorbing
rings and a 50 m/s gas flow, the ion ledger closed to **95.99%**. With the face
weights carried it closes to **100.0001%**.

Two things about finding it are worth more than the fix.

**It was invisible until the ledger was made to close.** Before interior electrodes
absorbed continuously and the seed's own overlap was accounted for, launched,
collected, remaining and the named losses never had to add up — so a four per cent
leak had nowhere to show.

**Every conservation test in the suite was Cartesian**, where the weight is
identically one. They passed for a reason that did not generalise, which is the same
failure mode as the uniform-field conservation test that hid the cell-centred drift
sample. The new test is cylindrical, and it is backed by an exact assertion on the
weights — 4 on the axis, `1 ± h/2r` off it — because a conservation figure can be
nearly right with a wrong weight and an exact 4 cannot.

The stability limit had to follow: a weighted face scales the outward coefficient
with it, so the explicit step on the axis is four times shorter than the unweighted
rate says. `einzel estimate` takes the weight from the same function the run does,
so the two still cannot disagree about what a step is.

## The gas can vary from place to place

A single declared vector is a stream, not a jet. Spec figure 4 requires a velocity
**field** above 10^-2 mbar and §21 lists "gas velocity import" among Phase 3's
deliverables, and the reason is written into GAS-1 itself: the neutral jet off an
inlet capillary "drags ions and frequently dominates the axial DC gradient", and it
is not uniform across a ring stack.

```json
"gas": {
  "model": "hardSphere",
  "pressure": { "value": 2, "unit": "mbar" },
  "velocityField": { "path": "flow.vti", "array": "velocity" }
}
```

**VTK ImageData, which is what this engine already writes.** No format to decide
and no dependency to take: reading a *format* carries no licence obligation, and
linking a library would (RND-13). The path resolves against the **model document's
own directory**, not the working directory, so a model means the same thing
wherever the command is run from.

**ASCII only, and stated rather than discovered.** Binary, appended and compressed
payloads are the majority of real VTK files and none is read; a file this cannot
read is refused by name with the ParaView setting that fixes it. Same kind of
deliberate subset as EXT-7's JSON Schema one — the alternative is base64 and zlib
inside a reader whose whole job is to get a few thousand numbers into an array.

**Einzel consumes a velocity field and does not compute one.** That boundary is the
same one §17 draws around visualisation. A compressible flow through a
differentially pumped stack is a CFD problem, and a half-hearted one inside an
ion-optics engine would be worse than none, because it would look like an answer.

### What is checked

| Check | Result |
| --- | --- |
| An imported *uniform* field against a *declared* uniform one | agree to 2 ulps |
| Trilinear against a linear field | exact, 1e-9 over the whole box |
| Exactly at a node | the sample itself |
| An accelerating flow against uniform at each end | strictly between, both ways |
| A file this engine wrote, read back | every node exactly |

The first is the one that makes the import trustworthy: two entirely separate paths
to the same gas — a vector in the document, and a file read, interpolated and
sampled per node — give the same answer. **Two ulps rather than bit-identical, and
the reason is worth knowing:** interpolating a constant returns that constant only
to rounding, because 30(1−f) + 30f is 29.999999999999996 for plenty of f. That is
inherent to sampling and is not something a reader can fix.

### Two refusals and a qualification

**A caller that cannot resolve the path is refused, not run in a still gas.**
Resolving needs the model file's directory, and a study or a figure of merit meets
the transport without one. That is precisely the shape of the bug where
`driftVelocity` was honoured by the event-driven mode and silently dropped by the
diffusive one, so the transport refuses rather than substituting a gas that stands
still.

**A scalar array where a vector is needed is refused by name**, because a CFD export
carries pressure and density alongside velocity and the first array in the file is
as likely to be either.

**The overhang is reported rather than absorbed.** An imported field covers the box
it was solved on, and the tracked region need not be the same box. Outside it the
edge value is continued — right for a stream through a tube, wrong for the end of a
jet, and the samples do not say which — so `gas.flow-imported` states what fraction
of the tracked region was extrapolated rather than measured.

## A clamp that protected nothing and capped the drift

Scharfetter-Gummel's exponent `P = v h / D` is the ratio of drift to diffusion
across one cell, and the Bernoulli function it feeds handles a large one **exactly**:
zero above +40 and `-x` below -40 are the true limits, not approximations to them.

The flux clamped `P` to ±40 before calling it. That protected nothing — Bernoulli
guards its own exponential — and it capped the effective drift at `40 D / h`,
whatever the field and the gas actually were.

| | |
| --- | --- |
| Cell Peclet on the corpus drift tube, field alone | 25.4 |
| The same with a 120 m/s gas flow | 42.3 |
| Closed form `L / (mu E + v_gas)` | 126.7 us |
| Measured, clamped | 135.1 us, **6.7% long** |
| Measured, unclamped | 127.8 us, **0.86% long** |

The 0.86% is the packet's own spread, and the thing that makes it convincing is
that it is now **the same 0.86% with and without the flow** — a residual
independent of the drift speed is a packet effect; one that grows with the drift is
a scheme effect.

**What found it, and what did not.** The advection tests already in the suite run
at a cell Peclet of 16, below the clamp, and pass at 1.000000. They were correct
and they could not see this. What saw it was an example whose expected number is a
*division* — `L / (mu E + v_gas)`, with both declared rather than derived, so there
was nothing for the engine to agree with itself about. A scheme checked only
against its own past output keeps a defect like this indefinitely.

The suite now has a case at cell Peclet 105 and 209, exact to a part in a million.
The stability step needed no change: the Courant limit was always taken against the
true drift, so removing the cap makes the flux agree with the step rather than
sitting conservatively under it.

## Crossing between the two modes (SEQ-1)

§9 says an instrument is a timed state machine of "ordered phases with durations,
excitation overrides, **transport mode**, and transition conditions", and SEQ-1
adds that "a phase boundary may change transport mode; the conversion is explicit,
reported, and named as a source of uncertainty".

That is a real instrument's ordinary behaviour, not an exotic case. Ions are
collected and thermalised in a gas-filled trap, where the description is a
density; then extracted into vacuum and flown, where it is trajectories. Until
now the two modes were peers that could not hand anything to each other.

**The third clause is the substance.** These are not two encodings of one state.
One direction discards information; the other needs information the source does
not have, and the only honest thing is to assume it and say so.

### Trajectories to a density: the velocities are gone

A density field is a scalar per cell. There is nowhere for a velocity
distribution to live — and that is not an implementation limit, it is what the
diffusive description *is*. Drift-diffusion holds precisely because the velocity
distribution has relaxed to the local equilibrium, so carrying one would be
carrying a quantity the model assumes away. Also gone: which ion was where, so
nothing downstream can correlate an outcome with a starting condition.

Bilinear deposit, and the population is conserved **by construction** — the four
weights sum to exactly one whatever the position, so no normalising pass is
needed. Normalising afterwards would pass the same test while hiding a weighting
error rather than preventing one, which is the argument the cloud-in-cell deposit
already makes.

An ion outside the grid is **counted, not clamped**. Clamping piles the escaped
population onto the boundary and makes a leaky instrument look confining.

### A density to trajectories: the velocities are invented

Position can be sampled — the density *is* a distribution over position. Velocity
cannot: a density says nothing whatever about how fast anything is moving. What is
assumed is the assumption the diffusive description already made — a Maxwellian at
the gas temperature, plus the local drift μE.

That is the right assumption and it is still an assumption. It is exactly right
while the ions are in the gas that thermalised them, and wrong the moment anything
has happened faster than the momentum-transfer time. `transport.velocity-assumed`
is a **validity violation** for that reason: a caller reading a flight time
computed from invented velocities, who does not know they were invented, has been
misled by the platform.

### What was checked

| | |
| --- | --- |
| Deposited population against declared | **exact** (4.0e6 to 1e-12) |
| Gaussian cloud's centroid, 20,000 ions | 10.0197 mm against 10.0000 |
| Its spread, x and y | 3.9544 / 3.9949 mm against 4.0000 |
| Equipartition of drawn velocities, 300 K | **1.0021** |
| The same at 1200 K | **1.0021** |
| Drift added, against μE | 18.472423 against **18.472423** m/s |
| A 4000 m/s beam, after a round trip | 0.2 m/s |

The two temperatures matter: one alone is consistent with a thermal draw *and*
with a constant that happens to match. The drift is taken as a difference between
two runs at the same seed, so the thermal part cancels and what is left is checked
against arithmetic the conversion has no part in.

### The discriminating check is cylindrical

**In an axisymmetric field a cell is a ring whose volume grows with radius**, so a
uniform density holds far more ions at the wall than on the axis. Drawing cells by
their density *value* would over-sample the axis — and the resulting packet looks
entirely reasonable: a cloud, in the right place, of about the right extent.

What separates the two is a closed form. For a uniform density in a cylinder of
radius R the radial distribution is p(r) ∝ r, so the mean radius is **2R/3**.
Weighting by density alone gives a uniform p(r) and **R/2**.

| | mean radius |
| --- | --- |
| Measured, 40,000 samples | **13.5177 mm** |
| 2R/3, population-weighted | 13.3333 mm |
| R/2, density-weighted — wrong | 10.0000 mm |

Run with the weighting removed it gives **10.0245 mm**, and **only that one test
of the ten fails**. The other nine pass, which is the whole point: a wrong
cylindrical weighting produces a packet nothing about the picture would question.

The azimuth is drawn uniformly, because an axisymmetric density genuinely does not
distinguish points on a ring. That is information the conversion *creates* rather
than carries, and it is why a round trip is not the identity even in distribution
for a packet that was never axisymmetric.

### A phase names its mode, and a run crosses the boundary

Schema **0.6**: a phase carries `mode`, and absent means the model's — the same rule
its parameter overrides follow, so a model with no sequence and one whose every
phase runs in the declared mode are the same run. `CompiledModel.Phases` carries
the schedule and `ChangesTransportMode` says whether any boundary actually
converts, which a sequenced run that stays in one description does not.

`SequencedRun` walks the phases. Each is an ordinary run of its own mode over its
own duration, and the orchestration is the boundaries. On the shipped test
instrument — launch, thermalise, extract:

```
settle       trajectory  ends    1.0 us  population 200  centroid x  11.370 mm
thermalise   diffusion   ends   21.0 us  population 200  centroid x  11.370 mm  converted
extract      trajectory  ends   26.0 us  population 200  centroid x  11.380 mm  converted
```

**The middle row is the conversion made visible.** Flying, the packet advances
1.37 mm in a microsecond at the momentum it was launched with. As a density it
does not move at all over twenty times longer, because the diffusive drift is μE
and E is zero here. That is not a defect — it is what the conversion *means*.
Drift-diffusion holds precisely because the velocity distribution has relaxed, so
the momentum genuinely is discarded, and this is what discarding it looks like from
outside. Position, the one thing both descriptions carry, survives to the fourth
decimal.

**The first phase may be the trap**, which is the ordering the requirement was
written about — ions are collected and thermalised in a gas, and only then extracted.
Seeded through `DiffusionRun.Seed`, the same function a wholly diffusive `einzel run`
uses, rather than a second implementation: `run` and `test` once computed one flight
time two ways and disagreed by 1.3e-10, and the fix was to collapse them.

**Reusing that path corrected two numbers.** A first version of the orchestrator
built its grid with `new Grid2D(...)` where `GridFor` uses `Grid2D.OverBox`, which
rounds intervals up to a power of two — so one model got *two different grids*
depending on which path ran it. And its mobility helper ignored `Derived`, so a
mobility the document derived from a cross section came back as the stored value
rather than the re-derived one. A third gap closed with them: the diffusive leg
passed no absorbers, so electrodes did not absorb during a diffusive phase —
the defect that once made every diffusive transmission an upper bound with nothing
saying so, reintroduced locally.

**A trajectory leg starting part-way along the timeline is flown against a
`TimeShiftedField`.** The integrator always starts at t = 0, so a leg beginning at
21 µs has to be handed an instrument shifted by 21 µs rather than a start time it
has nowhere to put. Wrapped rather than adding one to `IntegrationSettings`, which
is the precedent `AxisymmetricField` and `PonderomotiveField` set: the transport
core carries every validated number here, and refactoring it to add a case beside
it is how those get quietly lost.

### Through the CLI

`einzel run` forks on `ChangesTransportMode` before it forks on the model's own
mode, because a model may declare `diffusion` and still have a sequence that leaves
it — the sequence is the more specific statement.

```
packet centre 9.999117 mm

  trap         diffusion   to    20.00 us   200 ions in     - trajectories  x  10.000 mm
  extract      trajectory  to    25.00 us   200 ions in   200 trajectories  x   9.999 mm  converted

sequence      1 mode conversion(s), 0 ions arrived
```

**The dash is not a zero.** A diffusive phase has no trajectories at all, which is a
different statement from having none left. And `packet centre` rather than
`final x`, because a sequenced run has no single ion whose final position it could
be.

`--json` carries a `sequence` block with the same per-phase account, `flightTime`
as `null` under the finite-double policy, and every conversion warning on it. The
manifest records **`diffusion -> trajectory`**: one mode would make a manifest claim
to determine a run it does not describe, and transport mode is a field §14 names
explicitly.

**Two defects this project had already fixed once, met again.** A successful
sequenced run exited `ConvergenceFailure`, because the exit logic knew
`StopConditionMet` and `DensityEvolved` and nothing else — the same list that had
to learn `DensityEvolved` after a working diffusive run reported itself as a
failure. And the printer showed `flight time NaN +/- NaN`, because the fix that
made those lines *absent* for a density was gated on `run.Diffusion is null` and a
sequenced run has that null too. Both are cases of a fix written as a list of known
modes rather than as the question being asked.

### A driven geometry inside a diffusive phase

A driven structure has no static field to step a density through, and the time-free
interface answers with the RF at the phase's first instant — a field that exists for
no length of time. What a slow ion in a gas feels is the cycle average, and the
sequenced path now uses **the same `Effective` wrapper the wholly diffusive path
does**, shared rather than written twice.

**This was the fifth occurrence of one defect**, and the first one I introduced
myself: a time-varying quantity reached through a time-free interface does not fail,
it answers at an arbitrary instant. Before this it was `einzel solve` reporting the
DC pattern of a driven geometry, the diffusive mode stepping a density through a
snapshot, `SuperposedField` becoming a snapshot when a driven member was summed in,
and the renderer drawing the same instant on every frame.

Measured on a four-rod quadrupole at 2 mbar, packet released 1.5 mm off axis:

| | packet after 60 µs |
| --- | --- |
| 400 V drive | **0.2341 mm** |
| drive off | 1.5000 mm |

**The geometry had to be four rods, and the first version of this test taught that.**
Two plates give a nearly *uniform* field between them, and the ponderomotive force
goes as ∇E² — so there was no well, the packet moved 0.1% whether the drive was on
or off, and the test passed on a threshold of "less than where it began". A
confinement test on a geometry that cannot confine measures nothing.

### Not built

Nothing outstanding for SEQ-1. The remaining diffusive-mode gap is unrelated to
sequencing: `IGasFlow` has one implementation beyond an imported field, so a funnel's
transmission is still computed in a gas that is either standing still or moving all
in one piece.

## The density is an output you can look at

TRN-2 makes a density the output of this mode the way a trajectory is the output of
integration, and RND-8 forbids drawing lines through one. Until recently the
prohibition had nothing on the other side of it: the mode's principal result could
not be looked at in any form, only summarised into a transmission and a transit
time.

`einzel run --vtu` on a diffusive model now writes `<model>.density.vti` — VTK
ImageData on the density grid, ions per cubic metre at the nodes, with the
warnings recorded in the file's own header per GRD-2. Section 21's argument for
VTU in Phase 1, that ParaView supplies the whole visualisation story before any
shell exists, applies to a density at least as strongly as to a field.

`einzel render section` draws it too, as contour lines at **decades** below the
peak rather than at even fractions: a density spans orders of magnitude, so evenly
spaced levels draw the top decade several times and the tail not at all. The levels
are recorded in the figure's own provenance, because a density plotted without them
is a shape rather than a measurement. A run whose ions have all reached a boundary
leaves an empty box — correctly — and says so as `render.density-empty` with the
change that would produce a picture, since drawing nothing and saying nothing looks
identical to a figure where the density was never computed.

## An implicit step, and when it is worth taking

The explicit scheme is bounded by the faster of two limits: diffusion, `h²/2dD`, and
Courant, `h/v`. In a driven structure the second is severe and for a reason worth
stating — the ponderomotive well's gradient is steepest at an electrode edge, which is
exactly where the density is almost zero. **The step is set by a region where nothing
is happening.**

Backward Euler has no stability limit. It is solved by red-black Gauss-Seidel on the
same assembled Scharfetter-Gummel coefficients the explicit path uses, and the
load-bearing property is not the stability:

> **Positivity survives a partial solve.** The update is
> `n' = (n + dt Σ b n'_neighbour) / (1 + dt Σ a)`, and every term in it is
> non-negative — the densities, the flux coefficients and the step. Each sweep is a
> non-negative combination of non-negative numbers, so the iterate is a valid density
> however far from converged it is. A scheme that went negative on the way would be
> unusable however stable it was, because a negative density is a quantity that has
> stopped meaning anything.

### It pays where Courant binds and costs where diffusion does

The gain buys steps and charges sweeps, and how many sweeps depends on which limit the
explicit scheme was up against. **The Gauss-Seidel iteration's difficulty is set by the
diffusive part of the operator**, so a step that is long by Courant's standard but
still short by diffusion's converges in a few sweeps.

The shipped ion funnel at 2 mbar, 5 µs of flight, 257 × 129 nodes — drift limit 195 ps
against a diffusion limit of 747 ns, a factor of 3,800:

| scheme | gain | steps | sweeps/step | wall | speedup | vs explicit |
| --- | --- | --- | --- | --- | --- | --- |
| explicit | 1 | 25,615 | — | 69.07 s | 1.0× | — |
| implicit | 4 | 6,404 | 3.0 | 50.58 s | 1.4× | 0.008% |
| implicit | 16 | 1,601 | 3.0 | 14.80 s | 4.7× | 0.028% |
| implicit | 64 | 401 | 3.0 | 6.39 s | **10.8×** | **0.108%** |
| implicit | 256 | 101 | 4.0 | 3.90 s | 17.7× | 0.427% |
| implicit | 1024 | 26 | 4.9 | 3.22 s | 21.4× | 1.673% |

**The error is exactly linear in the step** — 0.008 / 0.028 / 0.108 / 0.427 / 1.673 per
cent for gains rising fourfold — which is textbook first-order backward Euler and is
itself a check that the implicit path is right rather than merely stable.

**And it does not accumulate over a longer flight; it falls.** The same comparison over
50 µs rather than 5:

| scheme | gain | steps | sweeps/step | wall | speedup | vs explicit |
| --- | --- | --- | --- | --- | --- | --- |
| explicit | 1 | 256,143 | — | 709.45 s | 1.0× | — |
| implicit | 4 | 64,036 | 3.0 | 577.00 s | 1.2× | 0.004% |
| implicit | 16 | 16,009 | 3.0 | 127.50 s | 5.6× | 0.015% |
| implicit | 64 | 4,003 | 3.0 | 33.69 s | **21.1×** | **0.057%** |
| implicit | 256 | 1,001 | 4.0 | 12.64 s | 56.1× | 0.225% |
| implicit | 1024 | 251 | 5.0 | 5.91 s | **120.1×** | 0.894% |

At gain 64 the error **halves** against the 5 µs window, 0.108% to 0.057%, and at 1024
it goes 1.673% to 0.894%. The backward-Euler error is concentrated in the initial
transient, where the density is changing fastest; once the packet has settled into the
well the scheme tracks it. So the shorter window's figures are the pessimistic ones, and
a real 900 µs run is better than either table says.

**The speedup grows with the window for the same reason the sweeps do not**: the
explicit cost is linear in the flight while the implicit one stays at three sweeps a
step. 10.8× at 5 µs becomes 21.1× at 50 µs, at the same gain and a smaller error.

**And the opposite case, stated because it is equally true.** A plain drift tube whose
explicit limit is already near its diffusion limit gets no such bargain: the sweeps per
step climb from 11.0 at gain 1 to 88.7 at gain 16, and at a useful accuracy the
implicit scheme is *slower* than stepping explicitly. Quoting only the funnel would be
selling a general speed-up that does not exist.

So the funnel's 843,000 steps over 900 µs become about 13,000 at gain 64, and a run
that took hours takes minutes.

### What says it is correct rather than merely stable

Stability and positivity are cheap to satisfy and cannot see a wrong operator. What can
is the **Boltzmann equilibrium**: Scharfetter-Gummel is built so that its zero-flux
state is exactly `n_there/n_here = B(-P)/B(P) = exp(P)`, with P precisely `q dφ/kT`. That
is a property of the *space* discretisation, so backward Euler must hold it at any step
— the right-hand side is already the fixed point.

**It holds to 8.9e-16 in log density over three decades, at a step a thousand times the
explicit limit, in two steps and two sweeps.** One sweep per step, because the previous
density *is* the answer and Gauss-Seidel recognises it immediately.

The test was verified by breaking the solve the way a real mistake would — gathering a
neighbour with this cell's outward coefficient instead of its inward one. That stays
non-negative and stays stable, and **the non-negativity tests still passed**; the
equilibrium moved by factors of 6 to 18.

### What it cost elsewhere: the flux is now assembled once

Both schemes read one `FaceCoefficients`, built once per run. Everything that decides a
face coefficient — the mesh, the mobility, the field, the gas, the face weight — is
fixed for the whole run, so the explicit path stopped recomputing two exponentials per
face per step, which a driven funnel was paying about a million times over.

**The refactor is bit-identical**, asserted rather than assumed: over four
configurations spanning Cartesian and cylindrical meshes, still and moving gas,
interior absorbers and all four edge kinds, the density field, the collected count and
every named loss come back the same to the last bit. That required keeping the
*factored* form — storing `scale`, `B(-P)` and `B(P)` separately rather than the two
products — because `(w·s·b)·n` and `w·(s·(b·n))` differ in the last bit, and a refactor
of the code carrying every validated diffusion figure here deserves the one check with
no slack in it. The ledger reads the same expression for the same reason; a first
version used `Out × density` and came back 1 to 3 ulps out.

## Not built

*Three entries that used to head this list are gone — a gas flow in the event-driven
mode, a pressure field, and pressure gradients. See "The event-driven models see a gas
that moves" and "The gas can be thin in one place and thick in another" below.*

- **A temperature field.** The one thing about a gas that still cannot vary from place
  to place. An imported pressure is read as a density through `n = p/kT` at the model's
  single declared temperature, which is an assumption the document already made — but
  it is now the only one left, and a real differentially pumped instrument has a
  temperature gradient as well as a pressure one.
- **Inelastic channels.** Collisions are elastic. No fragmentation, no
  collision-induced dissociation, no internal energy at all.
- **A default that chooses the scheme.** The implicit step below is opt-in and takes
  a gain the caller picks. Both limits are computable before the run, so their ratio
  could pick the scheme and set the gain — but what the gain should be is an
  *accuracy* question, and nothing here measures the accuracy of a step it has not
  taken. Richardson extrapolation over a doubled step would, at three solves a step
  instead of one.
- ~~**A density snapshot mid-run.**~~ Built: `DriftDiffusion.Run` takes a list of
  instants and returns the density at each, and `einzel render section --at-us` draws
  one. Recording is bit-identical to not recording - the snapshots are clones taken
  between steps - and each reports the instant it was actually taken at as well as the
  one asked for, because a diffusive step lands where its stability limit puts it.

## The event-driven models see a gas that moves

GAS-1 asks for an imported neutral velocity field. The diffusive mode could see one;
the event-driven models **refused** it, and refusing was right at the time — a
collision was drawn from a time and a velocity with no place to evaluate the flow at,
so the alternative would have been a run that used the uniform drift and said nothing,
flying an ion through a declared jet as though the gas stood still.

The change is one argument: the ion's **position** is carried into the draw, so a
collision samples a Maxwellian about the bulk velocity *where the ion is*.

### The closed form it is checked against

In a gas moving at `u` with a field `E`, an ion's steady drift is `u + μE` — the flow
carries it, the field pushes it, and the two add, because the mobility is defined in
the frame the gas is at rest in. So the flow's contribution is the **difference**
between two runs, which cancels the collision model, the cross section and the
temperature:

| | along the flow | across it |
| --- | --- | --- |
| still gas | −5.405 m/s | 1005.209 m/s |
| moving at 120 m/s | 114.595 | 1005.209 |
| **difference** | **120.000** | **−0.000** |

A declared 120, recovered to the printed precision, with exactly nothing across it.

**And the control that makes it mean something.** A `UniformGasFlow` and a declared
`driftVelocity` are the same gas said two ways, so with the same seed they must give
the same *trajectory* rather than the same average — measured identical to **1e-9**.
If the flow path and the drift path disagreed about what the neutral velocity is, that
is where it would show.

### A flow that varies with position

The thing a uniform drift structurally cannot express. A gas standing still below a
plane and moving at 200 m/s above it: the ion drifts at 465.9 m/s before the step and
670.4 after, a difference of **204.5** against the declared 200. The residual is the
few collisions it takes to equilibrate with the new gas.

**A mistake worth recording.** The first version put the step at 3 mm, which the ion
crosses in six microseconds — so the "before" average was over three samples of an ion
still accelerating from rest and read 308 m/s against a steady drift of about 500. The
difference then came out at 361 against a declared 200, which looks like a physics
discrepancy and is a launch transient. Moving the step to 250 mm fixed it.

### Two things the sampler knew and nobody read

`BoundExceeded` and the new `SampledOutsideFlow` were computed and consumed by
nothing. That is the third time evidence about a computation's own quality has been
produced here and dropped at a seam — after `FieldAssembly.Build` discarding its
`SolveReport` and the sweep evaluator discarding its warnings. Both now reach the
result:

- **`collisions.rate-underestimated`** (validity violation) — a sampled relative speed
  exceeded the null-collision bound, so the rate was too low for at least one event
  and everything depending on it is biased. A biased rate looks exactly like a correct
  one.
- **`gas.flow-extrapolated`** (qualified) — a collision was drawn outside the imported
  field, where the flow is the edge value continued rather than anything measured.

- **`gas.pressure-extrapolated`** (qualified) — the same statement about the other
  imported quantity. Outside the box the edge density continues, and a pressure gradient
  is steepest at the ends of a pumped region, which is exactly where continuing the last
  plane is most likely to be wrong. Every collision rate, mean free path and mobility
  there is scaled by it.
  Right for a stream, wrong for the end of a jet, and the samples cannot say which.

### The trap that removing a refusal set

The trajectory path built its gas with `BackgroundGas.FromModel`, which does not
resolve a declared `velocityField` — only the diffusive path called
`GasFlowImport.Resolve`. So lifting the refusal without also resolving there would
have reintroduced **exactly** the failure the refusal existed to prevent: a model
declaring a jet, flown as though the gas stood still, with nothing to say so. Both
paths now resolve.

## The gas can be thin in one place and thick in another

The last quantity about a gas here that was a single number for a whole model. GAS-1's
velocity field landed and the ions moved with the jet; the **density** stayed uniform,
so an imported flow gave the neutrals a velocity everywhere and *the same number of them
everywhere*. That is not a differentially pumped instrument, which is what every device
this platform is aimed at above 1e-2 mbar actually is — a funnel behind an inlet
capillary spans decades of pressure between its entrance and its exit, and every
collision rate, mean free path, mobility and diffusion coefficient in it varies with
that.

`"pressureField": { "path": "...", "array": "...", "unit": "mbar" }` on the gas block,
read as VTK ImageData like the velocity field and for the same reasons: it is the format
this engine already writes, reading a *format* carries no licence obligation where
linking a library would (RND-13), and ASCII only, refused by name rather than misread.

### The unit is required, and that is section 9's rule rather than a new one

The velocity field has no unit field because a CFD velocity is metres per second
essentially always. A pressure is not: vacuum work is quoted in mbar and torr at least
as often as in pascals, and **a file read as pascals when it holds mbar is a gas a
hundred times too thin**, which looks entirely plausible and never announces itself.

Section 9 makes `{"energy": 4000}` a validation error on purpose, because "unit
ambiguity is the commonest source of silent wrongness and an agent building from prose
is the actor most likely to introduce it". Nothing in that argument weakens when the
number becomes a hundred thousand numbers. The symbol is resolved through the same
`UnitRegistry` a scalar's is, and one of the wrong dimension is refused by name.

### Reported whether or not it matters (REG-2)

`gas.pressure-imported` lands on every diffusive run that reads a field, and states the
range, the factor between its ends, and the consequence a reader actually needs — how
much faster the ion drifts where the gas is thinnest, since mobility goes as the
reciprocal of density. A reader who sees the range knows the run was checked; one who
sees nothing cannot tell that from its not having been checked.

`gas.pressure-extrapolated` is the qualification: at least one collision was drawn
outside the imported box, where the edge density continues. A pressure gradient is
steepest at the ends of a pumped region, which is exactly where continuing the last
plane is most likely to be wrong.

### Mobility goes as the reciprocal of density, and nothing here did that

The part that is physics rather than plumbing. An ion drifts further between collisions
in a thinner gas, so **mu N is the constant** — which is why the literature tabulates
*reduced* mobility rather than mobility. `Mobility.At(field, localN, referenceN)` scales
by `referenceN/localN`, and the declared `pressure` becomes the reference the declared or
derived mobility belongs to rather than the value used.

There are **two separate density dependences and they are not the same one**: this
factor is how *much* gas, and the existing E/N expansion is how hard the ion is pushed
*between* collisions. A version scaling only the second would leave the drift speed flat
across a pressure gradient while reporting a changing field dependence — which reads as
the mobility having been handled.

### A graded density turns Langevin into a null-collision method

The hard-sphere model has always used null collisions, because its rate contains the
relative speed. The Langevin rate does not, so **in a uniform gas every scheduled event
is a real one and there is no rejection step at all**. A graded gas makes that rate
position-dependent instead, which is the same mechanism reached a second way: schedule
at the *highest* density anywhere, then accept with probability `n(x)/n_max`.

Both bounds are majorants over the whole field now, because an event is scheduled before
it is known where the ion will be when it lands. A bound taken at the declared density
would be exceeded wherever the field is denser than declared, and **an exceeded bound
biases the rate low**.

The thinning is short-circuited on `IsGraded`, and that is load-bearing rather than an
optimisation: with a uniform density it would accept with probability exactly one and
*still consume a random draw*, moving every subsequent number in the stream. A seeded run
has to be bit-identical to what it was before a pressure field could be declared.

### What is checked

| | |
| --- | --- |
| Mobility at half and twice the reference density | **2.000000 / 0.500000** |
| The scaled form at the declared density | **bit-identical** to the unscaled one |
| A constant imported field vs the density its pressure means | 1 ulp |
| A field at 2x declared pressure vs *declaring* 2x the pressure, event-driven | **bit-identical trajectory**, both collision models |
| The same, diffusive, through the CLI | transit agrees to 1e-6 |
| Langevin thinning at three points on a 4x ramp | 0.25 / 0.625 / 1.00, to 0.01 |
| Halving the gas | transit halves, to 2% |
| A field in mbar vs the same field in Pa | 1e-9 |

The **bit-identical trajectory** is the sharpest of these. Two entirely separate routes
to one gas — `pressure: 2 mbar` with no field, against `pressure: 1 mbar` plus a constant
field holding 2 mbar — must produce the same flight from the same seed. Every scheduled
rate, every null-collision bound and every rejection has to read the field rather than
the declared scalar for that to hold; any one that did not would consume a different
random draw and diverge visibly. A tolerance there would hide exactly the defect being
looked for.

The diffusive equivalence catches the mistake with no other symptom: **a reference
density read the wrong way round**. Scaling by `n_local/n_ref` instead of
`n_ref/n_local` is self-consistent, runs cleanly, and is wrong by the square of the
ratio.

### The test that had no teeth, and how that showed

The first version of the equivalence test used Langevin only, and **a mutation that made
the local density read return the declared scalar did not fail it** — because the
Langevin branch short-circuits its thinning where the density is uniform, so a *flat*
imported field never reads a position at all. Correct behaviour, and no test of the read.
Running the same test under both collision models fixed it.

The graded-gas test had the same weakness in a different form. It asserted that a ramp
collides more than the thin gas alone, which sounds discriminating and is not: with the
density read at the wrong place the count lands *close to* the thin gas and a bare
"more than" still passes. What discriminates is **reversing the ramp** — the same
densities over the same box, arranged the other way round, so any reading blind to
position gives the two an identical count. 11,458 against 19,700.

Both are the same lesson in different clothes: *a test passes a mutation when the code
path it exercises does not contain the mutated line.* Run the mutation, look at which
tests fail, and treat the ones that did not as untested rather than as corroboration.

### The cost gate had to be re-derived, and the first version was 50% out

GRD-8 gates on a number available without doing the work, and for the diffusive mode
that number is not modelled: the step is set by two stability limits computable from the
mesh, the mobility and the field, and `estimate` and `run` call the same function. A
graded gas moves the mobility, so it moves both limits.

The first version took the thinnest gas **anywhere in the imported field**. The run
takes its limit from per-node arrays over the **tracked grid**. Those are not the same
region — a CFD field is usually solved on a larger box than the ions are tracked
through — and on the case at hand the field ran down to 0.5 mbar while the grid only
reached 0.75. The estimate said **2,252 steps against an actual 1,502**.

| | predicted | actual |
| --- | --- | --- |
| uniform, 1 mbar | 1,126 | **1,126** |
| graded, 2.0 to 0.75 mbar across the grid | 1,502 | **1,502** |

Two things this cost. The drift sweep now reads the density where it reads the field
and at every node rather than every other one, because the fastest drift is a product
of two quantities that peak in different places once the mobility varies — a stride is
harmless on a smooth field and is not harmless on a product. And the E/N fit check and
the reported regime numbers had to pick opposite ends: **E/N is worst where the gas is
thinnest**, the **Knudsen number and collision counts are worst where it is thickest**,
and reading the declared pressure for either would report a regime the instrument is in
nowhere.

It was found by running `estimate` and `run` and comparing the two numbers, not by
reading the code. Nothing about the wrong version looks wrong.

### A refusal moved to where it cannot be forgotten

Resolving a declared field needs the model document's own directory, which a study or a
figure of merit does not have. The existing rule was right — refuse rather than run in a
gas the document does not describe — but it lived as **a guard at each of four call
sites, naming `velocityField`**. Adding a second importable quantity would have needed
all four edited, and three of them were already silent about it.

`BackgroundGas.FromModel` now refuses an unresolved field itself, and
`WithoutImportedFields` is the deliberate exception whose name says what it gives up.
Two call sites that *did* have the model path — `einzel compare` and the diffusive cost
estimate — now resolve rather than refuse, which they should always have done: an
estimate taken in a gas the model does not declare is an estimate of a different run, and
GRD-8 exists to be relied on before the work is done.

This is the same rule as `FieldAssembly.Build` throwing rather than discarding its
`SolveReport`: **make the shortest spelling the safe one.**

### In the corpus, and what that took

The embedded-resource glob for examples was `*.json` only, so neither imported gas field
could appear in one — and so neither was covered by the EX-2 release gate that runs on
every change. `ExampleModels.Assets`/`WriteAssets` now write an example's data files
beside its model, under their whole file name so two examples cannot collide over a
`pressure.vti`.

`drift-tube-pressure-gradient` is a 38 mm tube whose gas thickens from 1 mbar where the
packet starts to 2 mbar at the detector. Its expected number is an integral:

    v(x) = mu_ref n_ref E / n(x)
    T    = integral of dx / v(x) = integral of n(x) dx / (mu_ref n_ref E)

which is the uniform transit scaled by the **mean** density along the path. For a linear
ramp that is the average of the two ends, so 1.5 — predicted **316.667 us**, measured
**320.236, 1.13% out**, matching the 0.86% packet spread the uniform drift-tube example
already reports. Ignoring the gradient gives 211 us, a third away.

What it deliberately cannot see is the **arrangement**: a drift transit depends only on
the integral of n along the path, so *any* reflection of the profile gives the same
answer. That is a property of the physics rather than a weakness to design away, and it
is why the reversed-ramp unit test exists to pin the arrangement separately.

### The model now carries where it came from

`einzel test` could not test a model with an imported field at all. The seam between a
study and the transport is a `Func<CompiledModel, double?>` — there is nowhere in it to
put a path — so a figure of merit reached `BackgroundGas.FromModel` and was refused,
correctly and uselessly.

`CompiledModel.SourceDirectory` is the fix: the directory the document was read from,
set by every loader, so any consumer can resolve a referenced file the same way
`einzel run` does. **Null stays the safe value.** A model compiled from a string has no
directory and its consumer is refused rather than handed a uniform stationary gas, so a
loader that forgets degrades to the refusal — the direction a mistake here should fail
in. It is never serialised, so no absolute machine path reaches a manifest.

All four study drivers take the directory alongside the document now, so a **sweep,
scan, optimisation or boundary search over a model with an imported field runs**. The
warning survives that seam too — the ledger reports `gas.pressure-imported` with its
per-evaluation count, which is what distinguishes "on a corner of the box" from "on
every draw".

### Still assumed: one temperature

What is imported is a pressure field read as a density field through `n = p/kT` at the
model's single declared temperature. A real differentially pumped instrument has a
temperature gradient too. That assumption was already made by there being one
`temperature` in the document — importing a pressure inherits it rather than adding it —
but it is worth stating, because it is now the *only* thing about the gas that cannot
vary from place to place.

A **non-positive sample is refused rather than clamped**, because mobility goes as 1/n:
a zero is an infinite drift and a stability limit of zero, so the run does not answer
wrongly, it never finishes. The refusal names the alternative — a collisionless region is
described by trajectory integration, not by diffusion.
