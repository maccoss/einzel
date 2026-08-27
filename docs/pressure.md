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
region ions are absorbed in.

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


## Not built

- **Interior electrodes as continuous sinks.** An electrode empties the *initial*
  density, which stops a source placed inside metal from starting there. Zeroing the
  interior at every step is the full treatment.
- **Interior electrodes as continuous sinks.** See above.
- **A neutral velocity field.** The gas is stationary, or moving with one declared
  bulk velocity. Spec figure 4 requires a velocity *field* above 10⁻² mbar, and gas
  velocity import is listed with it. This is what a funnel needs most: it is pushed
  through by gas flow, and a stationary gas has no such push.
- **Inelastic channels.** Collisions are elastic. No fragmentation, no
  collision-induced dissociation, no internal energy at all.
- **Pressure gradients.** One pressure for the whole model. A real differentially
  pumped instrument has several, and the interfaces between them are where much of
  the interesting physics is.
- **Space charge during transport.** Still screened rather than modelled; see the
  space-charge estimate, which is unchanged by any of this.
