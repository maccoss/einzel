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

Only trajectory integration is built. `DiffusiveTransport` is declared anyway, is
`IsAvailable = false`, and asking for it produces an AGT-3 error that names what it
needs. That matters because *"this mode does not exist"* and *"you spelled it
wrong"* are different problems an agent cannot tell apart on its own — and because
a mode selected by an `if` somewhere in the run command falls silently through to
whichever mode happened to be implemented first.

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

## Not built

- **Statistical diffusion.** The mobility description with no discrete events, above
  10⁻² mbar, emitting a time-resolved density field. Declared, refused by name, and
  the reason every funnel number above is a lower bound.
- **A neutral velocity field.** The gas is stationary, or moving with one declared
  bulk velocity. Spec figure 4 requires a velocity *field* above 10⁻² mbar, and gas
  velocity import is listed with it.
- **Inelastic channels.** Collisions are elastic. No fragmentation, no
  collision-induced dissociation, no internal energy at all.
- **Pressure gradients.** One pressure for the whole model. A real differentially
  pumped instrument has several, and the interfaces between them are where much of
  the interesting physics is.
- **Space charge during transport.** Still screened rather than modelled; see the
  space-charge estimate, which is unchanged by any of this.
