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

**An open question about that row, raised by being able to compute it.** Turn-around
time from a thermal source is now measurable and agrees with its closed form to
0.5%: FWHM = 2√(2ln2)√(mkT)/qE. That scales as √m, so across m/z 195 to 2722 it
spreads by a factor of 3.7 — at 1 kV/mm and 300 K, 0.54 ns to 2.04 ns.

The paper reports 0.8–1.2 ns across the same range, which is roughly *constant*.
Those cannot both be a simple thermal turn-around. Either "corrected" in that row
means something specific (normalised by m/z, perhaps), or the extraction is not a
uniform pulse, or another mechanism dominates. What is recorded here is a summary
of the paper rather than the paper, so this is a question to settle against the
source before either number is quoted as agreement or disagreement.

It is worth noticing that the machinery raised the question at all. A target that
cannot be computed cannot disagree with anything.
| Ion beam spatial width | 2.4 mm (6σ), measured by IonCCD |
| Extraction efficiency | ~84% at m/z 1522 |
| Ion capacity | >140,000 ions at 5 ms injection |
| Repetition rate | 200 Hz |
| Pressure gradient | ~one order of magnitude between regions |

### What is reproducible now

More than it first appears, because **the extraction itself is a DC problem**.
Once the RF is switched off and the extraction pulse applied, ions fly in a static
field, and the resulting time spread is governed by the ion cloud's spatial and
thermal velocity distribution — not by the RF that produced it.

- **The rectilinear cross-section as a solved field.** Flat electrodes are
  `rectangle` primitives; the auxiliary DC electrodes are more rectangles. The
  existing 2D solver with translational invariance along the trap axis handles the
  cross-section directly.
- **The static extraction field**, including the auxiliary DC contribution.
- **Δt\*, the turn-around time**, given an initial cloud with a spatial extent and
  a thermal velocity spread. This is the headline number and the most valuable
  single check, because turn-around time is set by the field and the initial
  conditions and nothing else.
- **The 2.4 mm beam width**, as the spatial extent that field confines.

### What it needs first

- **Turn-around time and packet emittance as figures of merit.** Spec §12 lists
  both under Class T and neither exists. Required for Δt\*.
- **Ensemble launching from a distribution** — position and thermal velocity —
  rather than single ions. The analysis side (`ArrivalTimePeak`) already handles
  the resulting arrival distribution.

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

1. Add turn-around time and packet emittance to `Einzel.Analysis` (Class T, §12)
2. Add ensemble launching from a spatial and thermal distribution
3. Build the rectilinear cross-section as a template, DC only
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
