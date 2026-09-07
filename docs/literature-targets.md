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

**Worked up as of 2026-09-05**, first through its ancestor - the 2002 LTQ cross-section,
the shipped `linear-ion-trap` template - and then as the Stellar's own trap from its paper,
the shipped `stellar-ion-trap` template. Deliberately listed separately from target 1
rather than folded into it. A radial-ejection linear ion trap is a different optical
problem from a rectilinear transversal-extraction trap: the ejection is through slots in
the rods rather than orthogonal to the axis, the electrode cross-section is round or
hyperbolic rather than flat, and the figure of merit is a mass scan rather than a
turn-around time.

### The Stellar's trap, from its paper

> Remes, Jacob, Heil, Shulman, MacLean, MacCoss, *Hybrid Quadrupole Mass Filter - Radial
> Ejection Linear Ion Trap and Intelligent Data Acquisition Enable Highly Multiplex
> Targeted Proteomics*, J. Proteome Res. 2024, 23, 5476. PMC11956834.

The paper gives the trap in one paragraph, and it is enough to draw it:

| | Stellar (Remes 2024) | 2002 LTQ, for comparison |
| --- | --- | --- |
| Structure | the Velos Pro LIT | the original two-dimensional trap |
| Field radius | 4.0 mm | 4 mm |
| Stretch | **four-fold, 0.76 mm** - both rod pairs out | two-fold, 0.75 mm - the x pair out |
| Slots | all four rods (the Velos design) | one x rod |
| Helium | ~6 mTorr high-pressure cell, **0.5 mTorr** analysing cell | ~3 mTorr |
| Analysis scan rates | 33, 67, 125, 200 kDa/s | 5,555 Da/s |
| Peak widths at m/z 622 | **~0.35, 0.5, 0.7, 1.0 Th** at those rates | unit resolution |
| RF frequency, ejection q, excitation | not given | 1 MHz, 0.88, 3 V + 20 mV per m/z |

**What the geometry alone says, before any ion is flown.** With both pairs out the
cross-section is four-fold symmetric again, and the field shows it: the dipole and the
hexapole that the 2002 trap's single slot leaves (1.5e-3 and 2.1e-4 of the quadrupole)
are gone to rounding (1e-15), and so is the octupole the two-fold stretch added - the
four-fold stretch is not an aberration, it is a change of scale. The quadrupole term is
**0.6966 of the ideal formula's** at r0 = 4 mm, against (4.0 / 4.76)² = 0.7062 for an ideal
trap of the stretched radius; the truncated hyperbolae and the four slots account for the
rest. So the Stellar's q per volt is 0.70 of the textbook value for its field radius, and
its 12-pole is 2.7e-4 of the quadrupole. The paper's own reason for the four-fold stretch -
that the two-fold one "introduced an axial barrier to ion injection ... and reduced the
effectiveness of ion isolation during injection" - is an axial statement this
cross-section cannot check.

**The scan, at the paper's four rates.** The RF frequency, the ejection q and the
excitation are not published, so the 2002 trap's are carried over: 1 MHz, q = 0.88, and its
excitation law at half amplitude (7.7 V at m/z 622, the low-pressure working point the
retuning found) and at full (15.4 V). Forty-eight ions per rate, the RF ramped as one phase
from effective q 0.82 through the stability edge to 0.94. The distributions are a spike
with a tail, so the width is the full width at half maximum of a kernel density (0.05 u):

| rate | model, 7.7 V | model, 15.4 V | paper, at m/z 622 |
| --- | --- | --- | --- |
| 33 kDa/s | 0.19 u | 0.33 u | ~0.35 Th |
| 67 kDa/s | 0.14 u | 0.22 u | ~0.5 Th |
| 125 kDa/s | 0.15 u | 0.18 u | ~0.7 Th |
| 200 kDa/s | 0.15 u | 0.25 u | ~1.0 Th |

**Sharper than the instrument, and the gap grows with the rate.** In time rather than mass
the instrument's widths are a nearly constant 5 to 10 µs of ejection spread at every rate;
the model's core shrinks from 6 µs at 33 kDa/s to under 1 µs at 200, because an ideal
four-fold trap with a cold cloud and a clean excitation ejects every ion within a few RF
cycles of the ramp reaching resonance. The broadenings the instrument has and the model
does not - a space-charge widened cloud (~1 mm by the 2002 paper's tomography against the
model's 0.05 mm), amplitude noise on the RF and the excitation, real machining, and the
Stellar's actual excitation - are not in the template, so the model's width is a floor. The
floor is worth having: it says the geometry does not limit the Stellar to 0.35 Th at
33 kDa/s. `docs/device-templates.md` has the two things the sweep taught about running a
fast scan (through the edge, into a wall).

### The three sections, in a volume

`linear-ion-trap-3d` extrudes the same half-rod outlines as prisms into the paper's 12, 37
and 12 mm sections with a 2 mm-aperture plate lens at each end, which is the structure the
2002 paper's figure 2 is about:

| | measured on the axis |
| --- | --- |
| Well from the end sections 3 V above the centre | 0.0006 V at the centre, 0.35 at 15 mm, 1.13 at 18 mm (centre section's end), 2.92 at 26 mm |
| Axial reach of a 300 K ion (kT = 26 mV) | **8.7 mm** with the end sections; 17.1 mm with 20 V lenses alone |
| Excitation's transverse field over that reach | uniform to below 0.001 % (three sections); 0.2 % over the lens-confined cloud |
| Excitation's axial component, centre 15 mm | 0.17 % of its transverse field |

The excitation field is one solved pattern and the DC another, so the two configurations
share it; what the end sections change is where the ions sit in it - the paper's figure 2
as numbers. A quadrupolar end offset (x up, y down) is zero on the axis and makes no well;
the first draft had that, and the test caught it.

Scanned at 16,700 u/s with the same twelve ions and excitation as the cross-section, the
volume trap ejects at effective q 0.8703 against the cross-section's 0.8685 - 0.27 %
later - and its quadrupole term at half the inscribed radius is 0.8207 of ideal at the
scan's 0.5 mm cell against the cross-section's 0.8223, 0.19 % lower and converging upward
with the mesh (0.8109 at 1 mm). The offset is the mesh; the three sections scan as the
cross-section does.

### Space charge in the volume trap: the capacity claim, answered no

Thirteen runs against the 2002 paper's capacity argument, now with the axial well holding
the cloud and the slot in front of it. Every pushed width lies inside the *no-push*
realisation spread of its own configuration: cross-section 0.403 and 0.434 u with no push
against 0.434 at 400× the population; volume 1.320, 1.531 and 1.597 against 1.421, 1.539 and
1.575; and 1.401 at 660 macroparticles, the count that removes the softening violation. The
median moves 0.005 u in the cross-section. The paper's argument - that spreading ions along
a line lowers the density and spares the peak - is reproduced, and this model finds no
residual effect at the line densities an LTQ runs at.

Two caveats stated rather than buried. The volume widths are **mesh-limited**: the
cross-section at the volume's own 1 mm cell gives the same 1.39-1.56 u from a geometry with
no axial motion, so a broadening below that could hide there; the cross-section's own mesh is
converged and its null is correspondingly tighter. And the **error bar is realisation spread,
not a bootstrap** - a bootstrap over one draw called two no-push runs differing only in seed
significantly different. `docs/device-templates.md` carries the full table.

### Space charge, with the cloud cooled

A 1 mm slice of the cloud along the axis, 240 macroparticles, cooled 1.5 ms in the paper's
helium and scanned at 16,700 u/s with the packet pushing on itself: at 480, 2,400 and
9,600 ions per millimetre (an LTQ's 30 mm cloud at 1e4-1e5 ions is 300-3,000) the peak
moves -0.001, +0.001 and +0.011 u against the unpushed control and its interquartile width
stays at 0.4-0.5 u. In a near-harmonic trap a dipole excitation drives the centre of mass,
which the mutual force cannot move (Kohn), so the paper's argument that a line cloud's low
density spares the peak is reproduced; what sets a real instrument's capacity - ejection
across the slot and the cloud's axial extent under the end well - is the volume trap's
question. `docs/device-templates.md` has the table and the caveats.


### What is reproduced from the 2002 paper, and how

| Quantity | Paper | This model | Note |
| --- | --- | --- | --- |
| q per volt, m/z 587 at 600 V | q = 0.623 | 0.6245 from the ideal formula | the paper's calibration point, arithmetic |
| Secular frequency at q = 0.83 | 368 kHz | 368.1 kHz, beta(0.83) = 0.7362 | so the paper's q scale is the **effective** q |
| Quadrupole strength with the x pair stretched 0.75 mm | not stated | **0.822 of ideal** | measured from the solved field; the ideal formula's voltages are 22% low for this geometry |
| Field fault of the 0.25 mm slot | "detrimental field effects" | dipole 9.6e-4, hexapole 1.9e-4 of A2 | odd orders, which the symmetric stretch cannot cancel |
| What the stretch adds | "analogous to the stretch in 3D traps" | octupole **1.7e-3** of A2 | the same term a stretched 3-D trap adds on purpose |
| Resonance ejection, 13.5 V at 421 kHz, m/z 524 | ejects at q = 0.88 | ejects from q = 0.870 up, 30 to 5 µs; confined to 0.86 | excitation-off edge between 0.890 and 0.900 (tabulated 0.908, moved by the octupole) |
| Ejection direction | through the x slot | onto the x rods, none on y | the dipole is along x |

The template's parameters carry the published geometry and operating point (r0 4 mm,
1 MHz, slot 0.25 mm, stretch 0.75 mm, He 3 mTorr, excitation 3 V + 20 mV per m/z) and
name what is guessed: the rods' truncation and back, the slot's depth and relief behind
the face, and the hard-sphere cross-section.

### The mass scan, against "unit resolution up to m/z 2000 at 5555 Da/sec"

The paper's figure 8 is a full scan of the calibration mixture (caffeine 195, MRFA 524,
Ultramark 1022 to 1822) at 5,555 u/s, and the text says the 20 µm mechanical tolerance
"was found to be sufficient to obtain unit resolution up to m/z 2000" at that rate. The
model's version: a cloud of twelve ions per species, thermal at 300 K and 0.05 mm wide,
cooled three hundred microseconds in helium, then the RF ramped as a staircase (4 µs
steps) at the rate a 5,555 u/s scan implies for that mass, with the paper's excitation
law (3 V + 20 mV per m/z) at 421.3 kHz. Each ion's ejection instant is read as a mass on
the scan law; the species are flown separately, so there is no space charge.

| m/z | ions ejected | FWHM (u, from the central half) | m/Δm | ejected at effective q |
| --- | --- | --- | --- | --- |
| 195.09 | 12 | 0.75 | 254 | 0.8625 |
| 524.26 | 12 | 0.62 | 830 | 0.8674 |
| 1421.98 | 12 | 0.64 | 2206 | 0.8685 |
| 1521.97 | 12 | 0.54 | 2791 | 0.8687 |

**Unit resolution across the range at the paper's rate, with nothing tuned** - the widths
sit between 0.5 and 0.75 u from m/z 195 to 1522, which is what the paper claims and what
its figure 8 shows. Twelve ions per peak makes each width good to perhaps a quarter of
itself; the statement that survives that is "under one u everywhere". Two things about
the mass axis. Ions leave at an effective q of 0.862 to 0.869 rather than at the
excitation's nominal 0.88 - the excitation captures them from below and pulls them out
early, and a positive octupole (which the stretch supplies) is what lets an ion driven
below its small-amplitude frequency stay in resonance as its amplitude grows - so a scan
calibrated by the ideal formula would read 1.5 per cent low. Every instrument calibrates
its mass axis against known ions rather than from metal, so this is absorbed exactly as it
is in practice; the drift of the ejection q with mass (0.8625 to 0.8687) is what a
multi-point calibration curve is for. The figure is
[`docs/figures/linear-ion-trap-spectrum.svg`](figures/linear-ion-trap-spectrum.svg).

**The Velos claim, asked of the pressure alone, is not reproduced.** Second et al.
attribute the Velos's higher resolution at a given scan rate to its analyser cell's lower
pressure (~4e-4 Torr, 5.3e-4 mbar). The same scan with only the helium pressure and the
rate changed, twelve ions per species:

| pressure | rate | m/z 524 FWHM | m/z 1522 FWHM |
| --- | --- | --- | --- |
| 4.0e-3 mbar | 5,555 u/s | 0.62 u | 0.54 u |
| 5.3e-4 mbar | 5,555 u/s | 1.44 u | 0.90 u |
| 5.3e-4 mbar | 11,111 u/s | 1.25 u | 1.20 u |
| 4.0e-3 mbar | 11,111 u/s | 0.90 u | 0.63 u |

Less gas broadens every peak here at the 2002 excitation, because the gas is what damps
each ion's own thermal phase before the excitation grows it. **Retuning closes the gap**:
at 5.3e-4 mbar, half the paper's excitation amplitude (6.7 V) gives 0.62 u at m/z 524 -
the 3 mTorr width exactly - while the paper's 13.5 V gives 1.44 and twice it 1.46. The
Velos paper compares two tuned instruments and does not itemise the retuning; this
model says a gentler excitation is the part of it that matters for the width. Resolved
as a working-point difference rather than a disagreement about the instrument.

**What the model does not reproduce: ejection through the slot.** With the slot cut as a
0.25 mm channel straight through the rod, three quarters of the ions ejected toward it
strike the channel's walls within a few millimetres of the mouth - the slot mouth is a
diverging aperture lens for an ion leaving a 5e5 V/m RF field into a field-free channel -
and almost none reach the detector. The paper does not give the slot's profile behind the
face; the template now carries a channel depth and a relief behind it as named guesses,
and `docs/device-templates.md` records what each does to the count.

Before working this up, confirm the published geometry and operating point from
the Stellar and Tribrid literature rather than assuming it matches the Astral
lineage — the two share an architecture at the block-diagram level and not much
below it.

### Published geometry and operating point, from the LTQ and Velos papers

The Stellar trap is the dual-pressure linear trap of the LTQ Velos lineage. Its
own paper is not in `papers/` (as of 2026-09-05); what is there, and what this
table paraphrases, are the two papers it descends from — Schwartz, Senko and
Syka, *A two-dimensional quadrupole ion trap mass spectrometer*, JASMS 2002,
13, 659, and Second et al., *Dual-pressure linear ion trap mass spectrometer
improving the analysis of complex protein mixtures*, Anal. Chem. 2009, 81, 7757.
The Stellar-specific numbers are still to be confirmed against its own paper.

| | LTQ (2002) | Velos dual-pressure (2009) |
| --- | --- | --- |
| Rods | hyperbolic, r0 = 4 mm | as LTQ, slots in all four rods (fully symmetric) |
| Axial sections | 12 / 37 / 12 mm, DC-offset for axial trapping | two cells, one aperture lens between them |
| Ejection slot | 0.25 mm high, 30 mm long, one X rod | all four rods |
| Slot compensation | slotted rod pair moved out 0.75 mm | — |
| Main RF | 1 MHz, up to 5 kV peak rod-to-ground | — |
| Resonance ejection | dipole across X rods, q = 0.88 | — |
| Isolation | multi-frequency waveform 5–500 kHz, 0.5 kHz spacing, precursor at q = 0.83 | as LTQ, 4 ms instead of 16 ms |
| Activation | q = 0.25–0.35 | as LTQ, activation time cut 67 % |
| Bath gas | He, ~3 mTorr (4e-3 mbar) | HP cell ~5e-3 Torr (6.7e-3 mbar); LP cell ~4e-4 Torr (5.3e-4 mbar) |
| Scan rate / resolution | 16,000 u/s (LTQ XL) | 33,000 u/s at equal or better resolution; >25,000 FWHM in ultra-zoom |
| Ion cloud | ~1.0 mm radius, ~30 mm long | — |

**What is measurable in this build already.** The 2002 paper's Fig. 2 is a
SIMION field plot: three DC-offset sections against one, showing how the
end-section offsets distort the dipole excitation field. That is a DC solve of
round-or-hyperbolic rods with an axial break — a `solved3d` template with three
segments, nothing new — and the paper's own claim (distortion confined to the
end sections) is checkable. The mass-selective-instability scan (ramp the RF,
eject at q = 0.88 through the slot, count arrivals against m/z) is a `scan`
study over the shipped RF path, and the LP-cell pressure is inside the
event-driven collision models' range. The 15× ion-capacity ratio against a 3-D
trap is a space-charge claim the direct-sum method can be pointed at.

**What is not.** Both the HP cell's 5e-3 Torr and the LTQ's 3 mTorr are above
the event-driven mode's stated validity and below where the diffusive mode's
drift-diffusion description holds, so isolation and activation efficiency —
the two things the dual-pressure design buys — sit in the band neither mode
owns cleanly. That is the same band the funnel benchmark sat in, and the same
hard-sphere-against-Langevin bracket applies.

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

> Grinfeld, Stewart, Balschun, Skoblin, Hock and Makarov, *Multi-reflection Astral mass
> spectrometer with isochronous drift in elongated ion mirrors*, Nucl. Instrum. Methods Phys.
> Res. A **1060** (2024) 169017.  **[C]**

**Nothing here came from conversation with anyone at the vendor.** That is deliberate: the
value of this model is that it is derived from public information, and a number obtained
privately would contaminate that. Everything below is paraphrased and cited rather than
reproduced; the paper texts are in `papers/`, which is not tracked, so this is the tracked
record of what they say.

The current state of the model is in `docs/device-templates.md`; the pixel measurement of
the ion foil and the narrative of the reconstruction are in `docs/astral-log.md`.

### The published register

| | | |
| --- | --- | --- |
| beam energy | 4 keV | A, B, C |
| mirror electrodes | five per mirror - one grounded, one strongly accelerating (which provides the spatial focusing), three reflecting | B |
| nominal drift length | 335 mm | C |
| drift distance, varying with injection angle | 310-360 mm | B |
| effective mirror separation | 641 mm | C |
| number of oscillations | 25 | C |
| oscillations / flight path | 24 / >30 m | A, B |
| flight path in the analyser | ~32 m | C |
| mirror convergence | a 200 um spacer; stated as an angle | B, C |
| nominal injection angle | 1.78 degrees | C |
| stripe bias | -13.8 V | C |
| resolving power | > 100,000 | A |
| detector | HDR | A |

Two checks that [C]'s table is read correctly off a two-column extraction: twice 25 times
641 mm is 32.05 m against the stated ~32 m, and the remark that the convergence is a few
hundred micrometres over the entire drift length is what the stated angle gives over 335 mm.

**A counting trap, written out once.** Three quantities in these papers are easy to conflate
and two are numerically identical:

| | |
| --- | --- |
| flight path | **>30 m** - metres, and the commonest thing to misremember as a count |
| **total** oscillations, whole flight | **24 to 26** |
| oscillations **outbound**, to the drift reversal | **12 to 13** ([A]: "the first 12-13 oscillations", then "the following 12-13") |
| reflections per oscillation | **2** |
| so **reflections outbound** | **24 to 26** |

The last row and the second are the same numbers and different quantities, so a measurement
reported in oscillations must be halved before it is compared with anything here. **This is
still a live source of confusion in the model's own write-ups**, where "oscillations
outbound" is sometimes used for the quantity this table calls reflections outbound. The
drift-per-reflection the published set implies is 335 / 25 = **13.40 mm**, which is the
unambiguous form and the one to compare against.

### The regression targets

This section records only what is a target and its status, so the register above and the
model's own page do not drift into each other.

The device the whole 3-D path exists for. It is also the first target whose geometry had
to be **measured out of a published figure** rather than read off a table - the electrode
positions, the back wall and the e0-e1 gap are all read off [A] figure 1, and the mirror is
now reproduced against the same figure's on-axis potential and period-slope curve (handoff
§§58-71). What is not yet reproduced is the drift register with the stripe in the model.

| target | published | status |
| --- | --- | --- |
| oscillations / flight path | 24 / 30 m | **24 outbound**, flight time 786.44 us against 783.2 by arithmetic - 0.4 per cent, and the register test excludes 25 by 4.2 per cent |
| drift reversal distance | 310-360 mm, mean 335 | **336.15 mm**, on the reproduced mirror with the published stripe shape in the model. The tilt alone gives 404 mm; the stripe brings it to 336, which is [C]'s own account of the mechanism - tilt term plus stripe term |
| resolving power, mirror alone | ~180,000 over ±2.5 per cent, from the published period-slope curve | **120,000-220,000** on the drawn layout at the paper's three-point condition (the range is a first-order residual at the 1e-4 level, the floor of the solve; log §73), slope amplitude ±0.034 against ±0.035 ppm/eV, on-axis potential to 0.16 kV rms; log §71 |
| resolving power, drift alone | - | **73,500** over the full ±11 per cent angular acceptance, from the published stripe shape (log §55-56) |
| energy acceptance | period stationary at 4000 and 4000 ± 100 V | **met by construction** at the solved gap, U3 and U4: c1 = 0.00000, c3 at the fit's noise floor, c2 on the balance point; log §71 |
| `(t\|e)` sensitivity to the C(1) perturbation | **~2.5 ppm/V at TE1 = 0.01** | **0.987 of published** - dc1/dTE1 measured at four depths, and C(2) reduces c2 as published; log §49 |
| ion foil geometry | not stated in text | **measured off [A] figure 1** at 1.92 mm/px; shipped in `astral-3d.json` |
| mirror convergence angle | stated in [C]'s table | **fitted at 0.56 mm before [C] was read**, which is 503 um across the 641 mm effective separation and 196 um over the 250 mm mirror body - the spacer. The fit and the specification agree once the baseline is identified, and the fit identified it |
| nominal injection angle | 1.78 degrees | **2.29 degrees fitted**, and not reconciled. The reversal distance goes as its fourth power, so 29 per cent is not a rounding difference |

**The C(1) row was run first because it is different in kind from the others.** Every
other row needs the absolute geometry to be right, because it compares a number this model
produces against a number the instrument produces. C(1) and C(2) are *differential*: apply
the published perturbation to the published potentials and measure how much the coefficient
moves. It came out at 0.987 of published on a mirror that was then still wrong in polarity
and position - which is what a differential check is for. **Three numbers are solved rather
than read** and are the ones the papers do not give: the board gap (41.4 mm), U3 (0.974
against the table's 0.916) and U4 (1.479 against 1.503), in a table that has U2's sign
wrong. Everything else in the mirror is as published or as drawn.

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

## 5. The PNNL electrodynamic ion funnel — the funnel benchmark

> Kim, Tolmachev, Harkewicz, Prior, Anderson, Udseth, Smith, *Design and implementation
> of a new electrodynamic ion funnel*, Anal. Chem. 2000, 72, 2247.
> <https://doi.org/10.1021/ac991412x>  **[K]** — with the simulation of Tolmachev, Kim,
> Udseth, Smith, Bailey, Futrell, Int. J. Mass Spectrom. 2000, 203, 31, reproduced as
> Figure 6 of the open-access review Kelly, Tolmachev, Page, Tang, Smith, Mass Spectrom.
> Rev. 2010, 29, 294 (PMC2824015).
>
> Page, Tolmachev, Tang, Smith, *Theoretical and experimental evaluation of the low m/z
> transmission of an electrodynamic ion funnel*, J. Am. Soc. Mass Spectrom. 2006, 17, 586.
> <https://doi.org/10.1016/j.jasms.2005.12.013>  **[P]** — open access, PMC1829303.
>
> Lynn, Chung, Han, *Characterizing the transmission properties of an ion funnel*, Rapid
> Commun. Mass Spectrom. 2000, 14, 2129 — SIMION with a collisional drag model on the
> earlier 28-electrode funnel, against the measured m/z transmission window. **[L]**

**Why this device, and why these papers.** The §23 open decision — a published funnel
geometry or one of ours — is settled here in favour of published, and this is the one:
one device whose dimensions are fully in print, two independent measured curves, a closed
form for one of them, and a SIMION comparison on the same family for the cross-code check
§19 asked for and could never have. Digitised curves and the theory's definitions are in
`papers/funnel/` (gitignored, like the rest of that directory).

**The geometry, as published in [P] and [K]:** 100 ring electrodes of 0.5 mm brass on
0.5 mm Teflon spacers (pitch 1.0 mm), holes cut by wire EDM; the first ~58 at 25.4 mm
inner diameter, the last 42 tapering linearly to 2.5 mm ([K]'s earlier build: 55 and 45,
to 1.5 mm); a DC-only conductance limit of 2.0 mm inner diameter after the last ring; a
6.5 mm jet disrupter about 20 mm in from the inlet capillary. RF of opposite phase on
adjacent rings through 10 nF; a 500 kΩ resistor chain for the DC gradient. Shipped as
`pnnl-ion-funnel.json`.

| target | published | conditions | status |
| --- | --- | --- | --- |
| transmission against RF amplitude [K] | threshold: 3% at 10 Vpp, 40% at 15, 85% at 20, plateau from 25; plateau 3.3 nA of 5 nA in (65%) | 1 Torr N₂, 0.7 MHz, 16 V/cm, gramicidin, 5 nA | **threshold reproduced** — diffusive mode, nothing tuned: 0.17 / 0.57 / 0.90 / 0.97 / 0.99 at 10 / 15 / 20 / 25 / 30 Vpp against 0.05 / 0.39 / 0.85 / 0.97 / 1.00 normalised; 50 % near 14 Vpp against 16. The plateau's absolute 65 % is not compared: space charge and the inlet are not modelled. Handoff §78 |
| the same, Tolmachev's simulation [K] | same threshold, plateau 3.3 nA | same, with space charge | comparison partner, not a target |
| low-m/z cutoff against RF frequency, m/z 118.2 [P] Fig. 3 | 50% at 425 / 485 / 565 kHz for 9.0 / 19.1 / 29.1 V/cm | 1.9 Torr, 80 Vpp, singly charged betaine | **mechanism, shape and gradient ordering reproduced; 17–22 % low in frequency** — trajectory mode, hard-sphere collisions, 20 ions a point: 50 % at ~320 / ~380 / ~470 kHz, every loss below the cutoff on a tapered ring. with Langevin collisions instead the 50 % point is ~500 kHz against the measured 485, rising too slowly above it (0.75 at 600 kHz against 0.97): the two limiting collision models bracket the measured curve; log §77 |
| cutoff frequency against m/z, six ions [P] Fig. 4 | at 19.1 V/cm: 485, 290, 195, 170, 140, 130 kHz for m/z 118, 322, 622, 922, 1522, 2122 | 1.9 Torr, 80 Vpp | not yet attempted |
| cutoff independent of RF amplitude [P] Fig. 5 | the same curve at 60, 80, 100, 120 Vpp | 19.1 V/cm | not yet attempted |
| the closed form [P] eq. 7 | (m/z)ₗₒw = 8 e E_DC sin A / (m_u ω² δ), δ = pitch/π = 0.318 mm, tan A = 0.25 | predicts 511 kHz for m/z 118 at 19.1 V/cm against a measured 485; 351 against 425 at 9.0; 631 against 565 at 29.1 | the analytic partner |
| m/z transmission window, SIMION with drag [L] | "compares favourably" with the measured window of the 28-electrode funnel | 1–10 Torr | the cross-code partner; the paper is behind Wiley and only its abstract has been read |

**Three caveats that travel with every comparison here.** The transmission measurement
carried 5 nA of ion current, so space charge is in it and Tolmachev's simulation included
it; this model does not, and should match the threshold and the shape rather than the
plateau. Both measurements sit behind a gas jet from the inlet capillary that neither paper
characterises and this model omits, releasing the ions 20 mm in where the jet disrupter
ends. And the cutoff is a breakdown of the averaged-field picture — a low-mass ion is pulled
into a ring within one RF cycle — so the diffusive mode cannot see it by construction, and
the measurement is a test of the collision-by-collision mode at a pressure above the band
it claims, which is REG-3's overlap-band comparison made on a published instrument.

---

## 6. Trapped ion mobility — the Bruker timsTOF analyser

> Hernandez, DeBord, Ridgeway, Kaplan, Park, Fernandez-Lima, *Ion dynamics in a trapped ion
> mobility spectrometer*, Analyst 2014;139:1913. <https://doi.org/10.1039/c3an02174b>
> (open access, PMC4144823)  **[H]**
>
> Ridgeway, Lubeck, Jordens, Mann, Park, *Trapped ion mobility spectrometry: a short
> review*, Int. J. Mass Spectrom. 2018;425:22.  **[R]**
>
> Michelmann, Silveira, Ridgeway, Park, *Fundamentals of Trapped Ion Mobility Spectrometry*,
> J. Am. Soc. Mass Spectrom. 2015;26:14.  **[M]**
>
> Silveira, Ridgeway, Laukien, Mann, Park, *Parallel accumulation for 100% duty cycle
> trapped ion mobility-mass spectrometry*, Int. J. Mass Spectrom. 2017;413:168.  **[S]**

Paraphrased and cited rather than reproduced; the copies are in `papers/`, which is not
tracked. **[H]** is open access and was read through the PubMed tools; the rest are
image-only owner-password-protected scans read by rendering their pages.

**Why this device.** TIMS holds ions stationary against a moving gas rather than pushing
them through a still one, so the answer is *where a population parks* rather than how fast
it transits — which is exactly the balance the drift-diffusion solver computes. Every other
target at these pressures has been a transmission question.

### The published register

| | | |
| --- | --- | --- |
| sections | entrance funnel, tunnel, exit funnel | H |
| lengths | 50 mm, **46 mm**, 15 mm (sequential); **96 mm** tunnel for parallel accumulation | H, R |
| bore | 26 → 8 mm, then **8 mm constant**, then 8 → 1 mm | H |
| electrodes | segmented rings on PC board, 1.6 mm thick, **four isolated segments each** | H |
| spacing | 1.5 mm in the funnels; **0.125 mm kapton** in the tunnel, gas-tight | H |
| RF phasing | funnels alternate between adjacent **plates** (dipolar); the tunnel alternates between adjacent **segments** (quadrupolar) | H |
| RF | **850 kHz, 200 Vpp** as the stated example; the 2011 prototype was ~880 kHz | R |
| axial RF component | "essentially no axial component", so it does not interfere with the measurement | R |
| pressure | entrance 1.0-2.6 mbar, exit 1.0 mbar; ~3 mbar as operated | H, R |
| gas | N2 at 300 K, cylindrically symmetric, **parabolic** radially | H |
| axial gas velocity | **75 m/s at the tunnel entrance rising to ~130 m/s at 45 mm** on axis; ~115 at r = 1.4 mm | R fig 2c |
| axial pressure | **2.61 mbar at z = 0 falling to 2.30 at 45 mm** | R fig 2d |
| temperature | 297.5 K falling to ~294 K on axis — a 6 K variation | R fig 2e |
| radial profile | parabolic, peak ~127 m/s at z = 43 mm and ~95 at z = 7 mm, over an 8 mm bore | R fig 2f |
| axial field | up to **~70 V/cm** from **under 300 V** across the tunnel | R |
| EFG | DC superimposed per electrode through a **resistor divider**; fixed at the exit, **ramped at the entrance** | R |
| operating E/N | **45-150 Td** at the optimum ~140 m/s flow | R |
| resolving power | 100-250 (H); ~200 singly and ~300 multiply charged routine, 400 achieved (R) | H, R |
| operating sequence | **fill, trap, ramp, wait**, with three traces: the deflector plate, the entrance potential, and the ramp | H fig 2 |
| named electrodes | deflector plate, entrance, ramp, out; P1 measured at the entrance funnel and P2 at the exit | H fig 1 |
| fill time | ~10 ms typical; trap times to a few seconds for kinetics | H, R |

Two numbers are derived here rather than published. The tunnel holds about **27 plates**,
from 46 mm over a 1.6 + 0.125 mm pitch. And the storage and analysis regions sit at
**23-41 mm** along the tunnel, read off [R] fig. 1a.

### The regression targets

| target | published | status |
| --- | --- | --- |
| elution field | `E_e = v_g / K` | **Met on the 46 mm tunnel.** The density parks where the solved field balances the gas to **1 micrometre**, and the position goes as 1/K to 0.17 per cent across three mobilities. `tims-analyzer`; `docs/device-templates.md` |
| plateau transit | `t_p = sqrt(2 L_p / (K beta))`, beta the field scan rate | not yet run |
| resolving power | `R = v_g (2L_p/beta)^(1/4) K^(-3/4) sqrt(q / 16 ln2 kT)` — same form as Hill's drift-tube law with the effective path `v_g t_p` in place of the tube length | not yet run |
| R against scan rate | R goes as `beta^(-1/4)` | not yet run |
| R against mobility | R goes as `K^(-3/4)` | not yet run |
| mobility calibration | `1/K` linear in elution voltage, with one instrument constant | not yet run |

**The resolving-power law is the target that matters**, because it is a *shape* over two
independent variables rather than a single number: R must fall as the fourth root of the
scan rate and as the three-quarter power of the mobility. A model that lands on one point
by tuning cannot land on that surface.

### What this needs from the engine

Most of it exists. The diffusive mode is built for 1-10 mbar; the collisional
pseudopotential is measured on the shipped funnel at 2 mbar; gas **velocity** and
**pressure** fields both import; mobility comes from a cross section by Mason-Schamp; and a
sequence phase can **ramp** a parameter linearly, which is the elution ramp itself.

Four things are new.

**The gas field has to be written, and it can be.** Einzel consumes a velocity field and
deliberately does not compute one. [R] fig. 2 quantifies it well enough to author directly:
an axial profile from 75 to 130 m/s, a parabolic radial profile over an 8 mm bore, and a
pressure ramp from 2.61 to 2.30 mbar. That is an imported field with **every number cited**,
which is a better position than the Astral started from.

**Segment-level RF phasing is expressible but unexercised.** Every stack shipped so far
alternates by plate. Four segments per ring alternating in pairs is a quadrupole, and
adjacent segments being exact negatives means it still costs one basis solve — but nothing
has driven a stack that way yet.

**Mobility resolving power does not exist as a figure of merit.** The engine has
arrival-time resolving power; this is `K/dK` off an elution profile against a ramped field.

**And the operating point straddles the low-field limit, which the two sources do not
agree about.** [H] states it as `E/p < 10 V cm^-1 torr^-1` **at all times**, which at 300 K
is about **28 to 31 Td**. [R] says optimum performance is reached at E/N of **45 to 150 Td**.
Those are different regimes, and both are probably true of what they describe: [H] is a 2014
prototype run deliberately in the low-field limit so that drift-tube calibration transfers,
and [R] is the commercial instrument tuned for resolving power. **The model has to declare
which one it is**, and at the commercial operating point a low-field mobility is not valid.

Einzel already refuses to pretend here — `Mobility.IsWithinFit` returns false and
`mobility.outside-fit` rides on the result — so a faithful commercial model needs a
**field-dependent mobility**, which is what TRN-1's "stated field dependence" exists for and
which no shipped model has yet declared. **Start at [H]'s low-field point**, where the
existing machinery is valid and the calibration is checkable, and treat the commercial
operating point as the second step rather than the first.

### The sequence maps onto the engine directly

[H] fig. 2 gives the timing as four phases — **fill, trap, ramp, wait** — driving three
potentials: the deflector plate, the entrance, and the ramp. Schema 0.6's model-level
`sequence` with a `ramp` on a phase expresses that as written, and the ramp is linear in the
parameter, which is exactly what [R] says the instrument does ("ramping the potential at the
entrance in a linear manner ramps the field strength at the plateau in a linear manner").

That is a pleasing fit and it should be treated with suspicion until it runs: the sequencer
has never driven a diffusive phase whose *purpose* is to hold a population stationary, and
"the density stops moving" is not a thing any existing test asserts.
