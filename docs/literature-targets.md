# Literature regression targets

Published geometries reproduced against reported performance. With no SIMION
licence the cross-code tier is unavailable, which promotes this from a
nice-to-have to **the main external check** — and it is the one tier that catches
conceptual errors rather than numerical ones. Self-consistency cannot tell you
the model is of the wrong thing.

Each target names what is reproducible with the engine as it stands, what needs
capability that does not exist yet, and the specific numbers to hit.

---

## Reproduced

### The round-rod quadrupole ratio

**Denison, J. Vac. Sci. Technol. 8 (1971): r/r0 = 1.1468**, the rod radius that
cancels the leading non-ideal multipole of a quadrupole built from round rods.
1.1487 is also in circulation, from a different criterion.

Reproduced by optimisation rather than by assertion: `Optimiser` minimises the
12-pole fraction A6/A2 over `rodRatio`, starting from the template's nominal, and
converges in 45 evaluations to **1.14148 +/- 3.05e-6**, cancelling the 12-pole by
a factor of 880. The result is stable against the radius the multipoles are
sampled on, moving by 0.0016 across 0.45 to 0.75 r0, which is what distinguishes
a property of the field from an artefact of the measurement.

**0.46% below the published value**, and the gap is discretisation rather than
search error or a modelling mistake. Refining the mesh moves the answer toward
the published value and slows down doing it - 1.14148, 1.14426, 1.14487 at 16,
32, 64 cells across the inscribed radius, which is second order and extrapolates
to about **1.1451**. The grounded housing accounts for roughly the remaining
0.002: the classical result assumes no housing, and widening the clearance from
1.6 to 3.0 rod radii moves the 16-cell answer by 0.0018 in the same direction.

The first guess - that the housing was the whole story - was wrong, and only the
refinement study showed it. `housingClearance` and `cellsPerRadius` are template
parameters precisely so that this kind of question is measurable rather than
arguable.

It is worth noting what made the measurement possible at all: the rod surfaces
are cut cells. A rasterised circle is a staircase, and a staircase radiates
harmonics of its own into exactly the multipoles being measured - four parts in
ten thousand of the main term at nominal, and a few parts in a hundred million at
the optimum.

### The einzel lens, against an independent vendor's figures

**Mass Spec Pro, "Einzel Lens"** (massspecpro.com/technology/ion-optics/einzel-lens-0)
— three coaxial cylinders, outer two earthed, centre "uphill". Three parametric studies,
each drawn as ray bundles with no numbers on the axes, and each therefore stating an
**ordering** rather than a value. That is the useful kind of target: an ordering cannot
be satisfied by a coincidence and is not a number this engine produced and then had
enshrined.

All three reproduce, measured as transmission through an aperture at the focal plane —
which is how a focus is measured in practice, and needed no new figure of merit.

| study | the page's claim | measured |
| --- | --- | --- |
| Centre voltage, 50 eV beam | focusing improves as the potential approaches 50 eV | 0 V **0.000**, 20 V 0.003, 30 V 0.003, 40 V 0.123, 42 V **0.207** |
| Kinetic energy spread, 0/+42/0 V | focusing falls apart as the spread widens | 50±0 **0.207**, ±5 0.107, ±10 0.093, ±15 **0.063** |
| Pressure, 0/+42/0 V | collisions make focusing poor | UHV **0.207**, 5e-4 Torr 0.110, 1e-3 0.093, 2e-3 **0.033** |

Monotone in all three, in the stated direction. The mechanism is legible in the itemised
losses rather than only in the totals: at 0 V the beam dies on the **exit tube** (157 of
300 ions), and at +42 V it survives to the **far aperture** (117). That shift is the
focusing.

The pressure study is the one that exercises most: it drives the collision models at the
page's own operating points, and the sixfold degradation from UHV to 2 mTorr is consistent
with its advice that above about 1 mTorr an RF multipole is the better instrument.

**What this cost, and it is the interesting part.** The beam is specified as *"50 eV with
a 20 degree angular spread"*, and the model format had no way to say that. The omission was
deliberate and documented — a thermal cloud already has a divergence, and offering both
would let a document say two things about the same physics. That reasoning is right for a
**source** and wrong for a beam defined downstream by an **aperture**, which is what an
einzel lens exists to re-image. Nor can a temperature stand in: matched to give the same
divergence it spreads the energy by **43%**, turning a 50 ± 0 eV beam into the page's own
50 ± 15 eV case, so the first two studies stop being separable. Schema 0.7 adds
`divergence`. See SPEC.md Amendment 31.

**Caveats, since the page publishes no dimensions.** The geometry is ours — a compact lens,
because a 20 degree cone does not survive the shipped template's 28 mm of 5 mm bore (182 of
300 ions are on the entrance tube before reaching the lens). So what is reproduced is the
three orderings and the mechanism, not any absolute number, and the absolute transmission
is low because the beam overfills this bore. A vendor page is also not a peer-reviewed
source; it is an independent implementation of textbook optics, which is worth exactly that
much and no more.

---

## 1. The Ion Processor — conjoined collision cell and pulsed extraction trap

> Stewart, Grinfeld, Wagner et al., *A Conjoined Rectilinear Collision Cell and
> Pulsed Extraction Ion Trap with Auxiliary DC Electrodes*, J. Am. Soc. Mass
> Spectrom., 2023. [PMC10767742](https://pmc.ncbi.nlm.nih.gov/articles/PMC10767742/)

Directly on the path to the companion memo's instrument. Note carefully **which**
device this is: the memo's §5 ion path contains two dual-pressure stages that are
easy to conflate and are not the same thing.

| | Stellar HP/LP LIT pair | Ion Processor (this paper) |
| --- | --- | --- |
| Status in the memo | Existing hardware | New hardware, the Astral-lineage device |
| Geometry | Linear ion trap, round or hyperbolic rods | **Rectilinear** — flat electrodes |
| Ejection | **Radial**, through slots in the rods, to dynode and PMT | **Transversal pulsed extraction**, orthogonally into a TOF |
| Purpose of the pair | HP cell traps, isolates and fragments; LP cell mass-analyses by radial ejection | HP region is a collision cell; LP region is the pulsed extraction trap |
| Role here | Front end and one of the two regulators | Conditions and pulses packets into the analyzer |

The memo's §6 item 5 is exactly the choice between them: *can the LP LIT deliver a
packet positioned against an extraction slot well enough to drive the injector
directly, or is the processor's pressurized region genuinely needed?* It calls
that the governing decision of the design.

That makes **both** devices modelling targets, and the comparison between them the
thing worth being able to run — which is a good argument for the geometry
primitives staying general rather than growing a rectilinear-trap special case.

Worth noting what the authors used: **MASIM3D**, an in-house package. A device of
this importance being simulated in software nobody outside the group can run is
the project thesis restated as a fact.

### Geometry and operating point

| | |
| --- | --- |
| High-pressure region | 100 mm long, inscribed radius r₀ = 2 mm |
| Low-pressure region | 80 mm long, same r₀ |
| Electrodes | Rectilinear — flat, not round rods — plus auxiliary DC |
| Auxiliary DC | Diagonal wedges in HP; horizontal pairs in LP, laser-cut, 4–7 mm feature variation |
| RF | 250–2000 V peak-to-peak at 3.7 MHz |
| DC offsets | +3 to +24 V auxiliary; 1–4 kV lift and extraction |
| Extraction | 500–1000 V pulsed transversal field |
| Pressure | HP (0.1–2)×10⁻² mbar N₂; LP ~1.75×10⁻³ mbar |
| m/z | 195–2722, up to ~30 kDa proteins |
| Injection energy | 5–200 eV |

### Reported results to reproduce

| Quantity | Reported |
| --- | --- |
| Time-of-flight spread, corrected | Δt* = 0.8–1.2 ns across m/z 195–2722 |
| Ion beam spatial width | 2.4 mm (6σ), measured by IonCCD |
| Extraction efficiency | ~84% at m/z 1522 |
| Ion capacity | >140,000 ions at 5 ms injection |
| Repetition rate | 200 Hz |
| Pressure gradient | ~one order of magnitude between regions |

**An open question about the first row, raised by being able to compute it.**
Turn-around time from a thermal source is now measurable and agrees with its
closed form to 0.5%: FWHM = 2√(2ln2)√(mkT)/qE. That scales as √m, so across m/z
195 to 2722 it spreads by a factor of 3.7 — at 1 kV/mm and 300 K, 0.54 ns to
2.04 ns.

The paper reports 0.8–1.2 ns across the same range, which is roughly *constant*.
Those cannot both be a simple thermal turn-around. Either "corrected" in that row
means something specific (normalised by m/z, perhaps), or the extraction is not a
uniform pulse, or another mechanism dominates. What is recorded here is a summary
of the paper rather than the paper, so this is a question to settle against the
source before either number is quoted as agreement or disagreement.

It is worth noticing that the machinery raised the question at all. A target that
cannot be computed cannot disagree with anything.

### Measured, now that the cross-section exists

The rectilinear cross-section is a device template
([Device templates](device-templates.md)), so the DC half of this target can be
computed rather than argued about. At r0 = 2 mm, a 1 kV transversal push, 300 K:

| m/z | Turn-around FWHM, 1 kV push | at 4 kV push | Naive V/2r0 at 1 kV | Solved / naive |
| --- | --- | --- | --- | --- |
| 195 | 2.636 ns | 0.652 ns | 2.153 ns | 1.224 |
| 500 | 4.220 ns | 1.044 ns | 3.448 ns | 1.224 |
| 1522 | 7.363 ns | 1.821 ns | 6.015 ns | 1.224 |
| 2722 | 9.847 ns | 2.436 ns | 8.044 ns | 1.224 |

**The last column is a constant, and that is worth more than any single row.**
The solved field gives a turn-around 22.4% longer than the naive V/2r0 closed
form at every mass and, to 1%, at both extraction voltages (1.224 at 1 kV, 1.211
at 4 kV). So what the slot and the fringe take out of the extraction field is a
single geometric factor — the field at the packet is 0.82 of V/2r0 — and not
something that has to be re-measured per operating point. That is the number the
solve buys over the formula, and it is reusable.

**Two things follow, and both bear on the reported 0.8-1.2 ns.**

The modelled range spans **3.74x**, which is exactly sqrt(2722/195). It has to:
thermal turn-around goes as the square root of mass, and no choice of field or
temperature changes that. The reported range spans **1.5x**. So the published row
cannot be a raw turn-around FWHM plotted against m/z - the scaling is wrong in a
way that no parameter fixes.

Divide the modelled figures by sqrt(m/z) and they are **0.190, 0.190, 0.190** -
flat to three figures. A quantity normalised that way would look "roughly
constant" across the range, which is what the paper reports. That is a plausible
reading of what "corrected" means in that row, and it is an inference rather than
a finding: settle it against the source before quoting either agreement or
disagreement.

Separately, the magnitudes at 1 kV are 2 to 8 times the reported ones. Pushing at
4 kV - the top of the paper's stated 1-4 kV "lift and extraction", though above
its 500-1000 V transversal pulse - lands **m/z 195 at 0.652 ns and m/z 500 at
1.044 ns**, straddling the reported 0.8-1.2 ns band. So the magnitude is
reproducible at a plausible operating point.

The *spread* still is not, and cannot be. Holding every mass from 195 to 2722
inside a 1.5x band needs a quantity that varies by 1.5x, and a thermal turn-around
varies by 3.74x whatever the field. Scanning the extraction voltage with mass
would fix it in principle - and 4x of voltage range is almost exactly the 3.74x
needed, which is a suspicious coincidence - but the absolute voltages required
run from 2.6 kV at m/z 195 to 9.7 kV at m/z 2722, and the upper half of that is
outside the stated range. So the normalisation reading above remains the better
one.

**Turn-around is also not what limits the peak.** Decomposing the arrival spread
of a 0.2 mm packet in this geometry gives 4.28 ns from temperature, 231.9 ns from
depth along the extraction, and 12.3 ns from width across it - so turn-around is
1.8% of the total, and depth is almost all of it. A published figure near a
nanosecond therefore describes either a far tighter packet, a space-focused
geometry, or a corrected quantity.

### Extraction efficiency, and what a wider slot costs

The paper reports **~84% extraction efficiency at m/z 1522**. At the shipped 1 mm
slot this model gives 51.5%, itemised on the two halves of the front plate — so
the question is what would have to change, and the obvious candidate is the slot.
Scanning it at m/z 1522 and a 4 kV push, 2000 ions:

| Slot width | Transmission | Turn-around | Dipole A1/A2 | 12-pole A6/A2 |
| --- | --- | --- | --- | --- |
| 0.5 mm | — | — | 1.25e-2 | 6.38e-3 |
| 1.0 mm | 51.5% | 1.821 ns | 5.43e-2 | 7.12e-3 |
| 1.5 mm | 69.0% | 1.834 ns | — | — |
| 2.0 mm | 81.7% | 1.847 ns | 2.33e-1 | 7.90e-3 |
| 2.5 mm | 89.2% | 1.863 ns | — | — |
| 3.0 mm | 94.0% | 1.878 ns | 6.55e-1 | 7.25e-3 |

**The paper's 84% falls between 2.0 and 2.5 mm**, on a 2 mm inscribed radius. That
is a real comparison rather than a coincidence of scale: it says the reported
efficiency is consistent with a slot roughly the width of r0, which is a
statement about their geometry derived from ours.

**And the trade is badly asymmetric, in a direction that is easy to miss.**
Turn-around barely notices the slot — 1.821 ns at 1.0 mm against 1.878 ns at
3.0 mm, three per cent over a threefold widening. Watch only turn-around and a
wide slot looks free. It is not: the dipole grows **53-fold** across the same
range, roughly as the square of the width, while the 12-pole stays flat at
6.4e-3 to 7.9e-3. At 3 mm the dipole reaches 0.655 of the quadrupole term and the
trap is barely a trap on that side.

That flat 12-pole column is also the cleanest confirmation of the attribution
made when the template landed: **the 12-pole is what flat plates cost and the
dipole is what the slot costs**, now measured across a sixfold range rather than
at two points. A dipole displaces the trapping centre, which for a device whose
job is to present a packet against a slot is precisely the aberration that
matters — so the efficiency is bought with the quantity the design is most
sensitive to, and the figure that would have flagged it is not turn-around.

Asserted in `RectilinearTrapStudy.WideningTheSlotBuysExtractionEfficiencyAndPaysForItInFieldQuality`,
which checks the monotonicity and the size of the dependence; the transmission
column is a study rather than a test, because five ion clouds is not a unit test.

### What is reproducible now

More than it first appears, because **the extraction itself is a DC problem**.
Once the RF is switched off and the extraction pulse applied, ions fly in a static
field, and the resulting time spread is governed by the ion cloud's spatial and
thermal velocity distribution — not by the RF that produced it.

- ~~**The rectilinear cross-section as a solved field.**~~ **Done** - the
  `rectilinear-trap` template. One correction to the note that used to sit here:
  the auxiliary DC electrodes are *not* more rectangles in this plane. They impose
  a gradient along the trap axis, which is exactly the direction the 2D solve is
  invariant in, so they cannot be represented at all without three dimensions.
- **The static extraction field**, including the auxiliary DC contribution.
- **Δt\*, the turn-around time**, given an initial cloud with a spatial extent and
  a thermal velocity spread. This is the headline number and the most valuable
  single check, because turn-around time is set by the field and the initial
  conditions and nothing else.
- **The 2.4 mm beam width**, as the spatial extent that field confines.

### What it needs first

Both of the analysis-side prerequisites are now **done**, which moves this target
from "needs machinery" to "needs geometry".

- ~~**Turn-around time and packet emittance as figures of merit.**~~ Both exist,
  both check against closed forms: turn-around to 0.5%, emittance to 0.8% against
  σ_x·√(kT/m)/v at 6,000 ions. Emittance is reported in both transverse planes
  with its Twiss orientation, and in the normalised form, which is the one to quote
  for a source that feeds an accelerating stage — as this trap does.
- ~~**Ensemble launching from a distribution.**~~ `IonCloud.Draw` samples position
  and per-component thermal velocity, and a model declares it in the `source`
  block.

What is left for Δt\* is the **rectilinear cross-section as a solved template**,
which is geometry rather than capability: flat electrodes are `rectangle`
primitives and the auxiliary DC electrodes are more of them.

Note also that the 2.4 mm beam width is now checkable against something better
than a width. A spatial extent alone does not say whether a packet will survive
the extraction; the emittance of the extracted packet does, and it is measurable
against the paper's stated injection energies and beam size.

### What needs Phase 3 and beyond

Do not attempt these before the RF and pressure work lands:

- **Time-domain RF** for the trapping itself, and the sequencer to switch from
  trapping to extraction
- **Collisions** — at 10⁻² mbar the collision frequency is far above the RF
  frequency, so this is the damped, event-driven regime
- **Space charge**, which the 140,000-ion capacity figure is entirely about. The
  screening estimate now puts a number on it: 140,000 ions in a 1 mm packet at
  4 kV carry about 100 mV across themselves, a 12.6 ppm flight-time error, an order
  of magnitude past the timing budget. So that figure is not a detail of the trap,
  it *is* a space-charge limit, and reproducing it needs the self-field solved
  rather than estimated
- **Gas dynamics**, for the pressure gradient between regions; Einzel consumes a
  pressure field, it does not compute one

So the 84% extraction efficiency and the ion-capacity figure are Phase 3 targets.
Δt\* is not, and should be attempted much sooner.

### Suggested order

1. ~~Add turn-around time and packet emittance to `Einzel.Analysis`~~ — done
2. ~~Add ensemble launching from a spatial and thermal distribution~~ — done
3. ~~Build the rectilinear cross-section as a template, DC only~~ — done, and it
   moved the answer: the closed form at the naive field is 19% wrong, at the solved
   field 0.7% wrong
4. ~~Make electrodes stop ions~~ — done. Transmission is a measured quantity
   itemised by named surface, checked against erf for a slit at 0.95 sigma
5. **Match the paper's extraction geometry** - slot width, packet size, and the
   second acceleration stage its 1-4 kV lift implies - and compare the ~84%
   efficiency at m/z 1522 directly
4. Reproduce Δt\* = 0.8–1.2 ns across m/z 195–2722 — a strong test, because the
   mass dependence of turn-around time is a sharp signature
5. Defer efficiency, capacity, and the pressure gradient to Phase 3

---

---

## 2. The Stellar dual-pressure linear ion trap

Not yet worked up, and deliberately listed separately from target 1 rather than
folded into it. A radial-ejection linear ion trap is a different optical problem
from a rectilinear transversal-extraction trap: the ejection is through slots in
the rods rather than orthogonal to the axis, the electrode cross-section is round
or hyperbolic rather than flat, and the figure of merit is a mass scan rather than
a turn-around time.

Before working this up, confirm the published geometry and operating point from
the Stellar and Tribrid literature rather than assuming it matches the Astral
lineage — the two share an architecture at the block-diagram level and not much
below it.

What it would need: time-domain RF, collisional damping at high-pressure-cell
conditions, and Class B analysis for the secular frequency spectrum and ejection
efficiency. All Phase 3 or later. The DC-only fraction is much smaller than for
target 1, because a radial-ejection trap's behaviour is RF behaviour.

---

## 3. The segmented quadrupole driven by rectangular waveforms

**Schrader, Anderson and Russell**, *Increasing Isolation Efficiency Using a
Segmented Quadrupole Mass Filter Operated with Rectangular Waveforms*, J. Am. Soc.
Mass Spectrom. **35** (2024) 1237-1244.

A switching drive rather than a resonant one. That is not an engineering
convenience: it changes the equation of motion from Mathieu's to Meissner's and
moves the stability boundaries with it. It also removes the DC supply, because an
asymmetric duty cycle carries its own mean and that mean enters the equation
exactly where a DC offset would.

### Reproduced

| Quantity | Reported | Einzel |
| --- | --- | --- |
| Square-wave low-mass cut-off | q = 0.712 | **0.71113** |
| Sinusoidal cut-off, for scale | q = 0.908 | 0.90684 |
| Effective a at 61.15/38.85 duty, q = 0.5897 | a = -0.2640 | **0.2630** |

Three independent numbers, none of which comes from this code. The duty-cycle one
is the most satisfying: a = 2q(2d - 1) is arithmetic that can be checked against
the paper before writing any simulation at all, and it agrees to a part in
250 - which says the digital working point is being placed where the authors place
it.

### Geometry and operating point

| | |
| --- | --- |
| Quadrupoles | Thermo 4 mm r0 (203 mm total) and 5.25 mm r0, both segmented |
| Segmentation | 22 mm prefilter, 159 mm main section, 22 mm postfilter |
| Coupling | 4000 pF capacitors, giving the prefilter q = 0.5897, a = -0.2640 |
| Drive | Rectangular, 150 V zero-to-peak, 500 kHz (4 mm) or 381 kHz (5.25 mm) |
| Duty cycles | 60.95/39.05, 61.1/38.9, 61.18/38.82 |
| Pressures | funnel 1.1 Torr, q0 0.27 Torr, mass filter 8e-4 Torr |
| Ion energy | ~4.25 +/- 0.5 eV, 1.5 mm beam, 5 degree half-angle |
| RF cycles in the filter | 88 (4 mm) and 67 (5.25 mm) |

### Still out of reach, and why

- **Isolation efficiency** - approximately 100% at 50 m/z peak width, 20% at
  5 m/z, and 90% for the larger r0. Needs the *segmented* geometry: three axial
  sections at different working points, with ions passing between them. That is a
  three-dimensional problem, and every solve here is two-dimensional.
- **Peak splitting**, which the authors reproduce only once aperture losses are
  applied. Needs an aperture at the exit and an ion cloud with the stated energy
  spread and divergence - the cloud exists, the aperture does not.
- **The pressure stages.** Phase 3, like everything else involving gas.

### Worth noting

The authors used **SIMION 8.1**. That is the second target in this file whose
results live in software the reader cannot run - the Ion Processor was simulated
in an in-house package - and it is the project thesis restated as a fact rather
than as an argument.

---

---

## 4. The Astral analyser — asymmetric-track MR-TOF

> Stewart, Grinfeld et al., *Parallelized Acquisition of Orbitrap and Astral Analyzers
> Enables High-Throughput Quantitative Analysis*, Anal. Chem. 2023;95(42):15656-15664.
> <https://doi.org/10.1021/acs.analchem.3c02856>  **[A]**
>
> Stewart et al., *Crowd control of ions in the Astral analyzer*, J. Mass Spectrom.
> 2024;59(4):e5006. <https://doi.org/10.1002/jms.5006>  **[B]**

**The full published register, the pixel measurement of the ion foil, and the current
state of the model are in `docs/astral-handoff.md`** - §1 and §11 respectively. This entry
records only what is a *regression target* and its status, so the two do not drift.

The device the whole 3-D path exists for, and the only target here that is not yet
reproduced in any respect. It is also the first target whose geometry had to be
**measured out of a published figure** rather than read off a table.

| target | published | status |
| --- | --- | --- |
| oscillations / flight path | 24 / 30 m | **not reached** - under 4 at the published injection angle |
| drift reversal distance | 310-360 mm, mean 335 | reverses, but needs **57x** the published 200 µm convergence |
| resolving power | > 100,000 | **6.56** - dominated by thermal drift spread that nothing refocuses |
| energy acceptance | flat T over 4000 ± 100 V | mirrors **do** energy-focus: R = 2,600 on energy spread alone |
| `(t\|e)` sensitivity to the C(1) perturbation | **~2.5 ppm/V at TE1 = 0.01** | **not attempted, and the best next test** |
| ion foil geometry | not stated in text | **measured off [A] figure 1** at 1.92 mm/px; shipped in `astral-3d.json` |

**The C(1) row is the one to run next, and it is different in kind from the others.**
Every other row needs the absolute geometry to be right first, because it compares a
number this model produces against a number the instrument produces. C(1) and C(2) are
*differential*: apply the published perturbation to the published potentials, measure how
much the time-energy coefficient moves, and compare to a published sensitivity. A model
whose focus is in the wrong place can still get that right or wrong informatively. It is
the only Astral regression currently available that does not wait on fitting `d1..d4`.

**Two cautions carried from [B] for anyone comparing numbers.** Their own simulations ran
**22 oscillations rather than 24**. And the design condition is a **third-order** temporal
focus - the optimum is where the locus of best resolution has zero inclination at its point
of inflection - so a model reproducing first-order focusing has not reproduced the tuning.

**What is deliberately absent.** No number in this entry or in the handoff came from
conversation with anyone at the vendor. That is the point of the exercise: a geometry
derived from public information is a result, and one obtained privately is not.

---

## Candidates not yet worked up

- **Reflectron and MR-TOF geometries** with published resolving powers, to check
  the mirror work against something other than its own closed form.
- **Quadrupole mass filter transmission against resolution**, once RF lands. The
  Mathieu stability diagram is analytic and belongs in the analytic tier; a
  measured peak shape against a published scan line belongs here.
- **Ion funnel transmission** against a published benchmark, once statistical
  diffusion and gas flow exist. Note the memo's open question of whether to use a
  published geometry or one of ours.

## Why these matter more than they look

An analytic test proves the integrator solves the equations it was given. A
convergence test proves the discretisation is converging to something. Neither can
tell you the model is of the wrong thing — that the geometry was misread, a
symmetry misapplied, or an effect left out that matters. Only agreement with a
real instrument does that, and short of building one, a published instrument is
the closest available.
