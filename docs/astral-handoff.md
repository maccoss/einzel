# Astral 3-D modelling: handoff to a dedicated machine

**Written 2026-08-31, status current to 2026-09-01.** For moving the Astral analyser work
onto a faster machine and running it unattended. It says what exists, what is measured,
what is broken, and — most importantly — what will waste your time if you do not know it
in advance.

**Start with *Where this stands*, immediately below.** The rest of the page accreted
chronologically and five of its sections are marked superseded in place.

Read `SPEC.md` first, as always. This page is scoped to the Astral work.

---

## Where this stands

**The drift reversal is reproduced exactly, by the mirror tilt alone.** At a convergence of
0.56 mm and the 2.29° the published figures themselves imply, with **no ion foil in the
model**, it reverses at **334.76 mm after 25 reflections with 13.39 mm of drift per
reflection** — against a published **310 to 360 mm, 24 to 26 reflections, 13.40 mm** (§17).
Two published numbers fix the two unknowns and the third checks.

**The convergence is the one number to question.** 0.56 mm is 2.8× what this document used,
and the whole factor is in what "a 200 µm thick spacer" tilts over: 200 µm closing the *gap*
across the 350 mm drift, or 200 µm per mirror over a ~250 mm baseline. None of the four
papers says. It is now the most consequential unpublished number here.

**The full track flies end to end** - 25 oscillations out to a 334.61 mm reversal and 25 back,
31.27 m in 853.7 µs against a published ~30 m in ~779, with every geometric register number
reproduced on one flight (§20). The 10% in flight time is the guessed mirror depths.

**The mirrors can be made to focus on a half oscillation, and R > 100,000 is reached there - but
not on the full track**, where the same designs give R = 60-70 at the acceptance with a first-order
coefficient 25x the half-oscillation one (§23). The mirrors carry to the full track exactly (c1 = -0.012
with the foil off, against the half-oscillation's 0.012); **the foil adds c1 ≈ -0.27 on its own** and is
the whole gap. Either the real mirrors cancel it or the real foil shape lacks it (§23). `einzel optimise` over
three electrode depths, maximising resolving power at the published ±2.5% acceptance, takes
the model from **R = 1,086 to 47,657** at that acceptance and to **150,036 at ±0.5%** and
**317,944 at ±0.25%** — 44× the shipped design, from a three-minute search (§18). The
published potentials were always compatible with the published resolving power; what was
missing was a geometry fitted to them, and the seam to fit it through.

**What remains beyond that** is the drift, and the fourth paper names the mechanism. A tilted mirror pair applies a *constant* force, so the drift period depends on
amplitude; the published requirement is that it be constant to **5e-6**, and the ion foil's
stated job is to "counter ToF aberrations induced by the converging ion mirrors" — exactly
that. There is a dedicated paper on it (Grinfeld et al., Nucl. Instrum. Methods Phys. Res. A 1060 (2024) 169017), which is the one to get next. The other half is that these mirrors have no
time-energy focus and cannot have one, because Thermo's optimised potential coefficients are
applied here to guessed electrode depths — which is what finally makes the depth fit
well-posed (§16).

### Verified, with controls

| | | where |
| --- | --- | --- |
| the ion flies the analyser | 120.058 µs against a predicted 120.1 — *on the earlier 3-D skeleton* | §3 |
| drift rate | 1374 m/s against `v·sinθ` = 1374 | §3 |
| a z-invariant geometry exerts no axial force | v_z held at 1371.2 m/s over 20 reflections and 411 mm of drift, **exactly** | §13 |
| drift impulse per reflection | **V·sin(2α) exactly** — ratio 1.000000000 at three tilts, and unchanged by an eightfold change of mirror gradient | §13 |
| the anisotropy a tilt creates | exact to **5.4e-20** in the field, 1.7e-18 through a document | §12 |
| reversal against the closed form | 618.31 mm measured against 618.0 predicted; 61 reflections against 61.1 | §14 |
| **the drift reversal, from the shipped template** | **334.61 mm, 25 reflections, 13.38 mm per reflection** against a published 310–360 mm, 24–26, 13.40 mm — mirror tilt alone, foil at 0 V | §17 |
| the injection angle, inverted from published D and N | **2.04° to 2.56°**, against [A]'s "about two degrees" | §14 |
| the foil produces a drift well | −19.9 V of −20 applied; shallow at 41% of the drift and deep at 67%, matching its own measured contour | §11 |
| a volume solve contributes nothing outside its box | mirrored half reproduces the full solve to 0.00000 V of 100 applied | §7a |

The controls carry more weight than the values. A parallel pair reporting the same drift
rate in every segment *to the last digit* is what says the deceleration is the convergence
and not a numerical artefact; the tilted case is meaningless without it.

### Where the two numbers stand

**Reversal: reproduced.** The drift impulse of one reflection is `V·sin(2α)` exactly, from
three conservation facts and with no reference to the electrode design (§13) — 22.4519 m/s
at the published spacer, confirmed to 0.05% on the drift distance. That is 31 to 38 per cent
of what the published reversal needs, and the ion foil supplies the rest: measured, a graded
foil closes it (§15).

Two earlier accounts of this gap are **withdrawn**: the 57× figure (a bisection predicate
that could not tell reversal from striking an electrode) and the 1.33× reconciliation
(measured on a tilt the solver could not see, with the sign inverted — §12).

### The resolving power, and the route to the published figure

**Every R here is this model's, not the instrument's.** The Astral reaches about 100,000 at
m/z 200; these are m/z 500.

| | model R | scaling |
| --- | --- | --- |
| as shipped, mirrors unfitted | 760 at ±2.5% | **`21.5/s`** — first-order limited |
| `d2` = 36 mm, `c1` cancelled | 2,044 at ±2.5%; **1,138,010 at ±0.1%** | **`1.2/s²`** — second-order limited |
| with a 300 K thermal cloud on top | 1.31 | the drift, not the mirror (§16) |

| **optimised over `d1`, `d2`, `d3`** | **47,657 at ±2.5%; 317,944 at ±0.25%** | `~800/s` — first-order limited with `c1` 27× smaller |

**The scaling is what identifies the binding order**, and `R·s` or `R·s²` holding constant
across a 25-fold range of spread is how each row above was established rather than asserted.

Three things follow, all of which cost a wrong turn to learn. **`|c1|` is the wrong thing to
minimise** — R does not peak where `c1` is cancelled, because `c2` binds at the acceptance.
**The spread you optimise at picks the geometry** — at ±2.5% a scan prefers `d2` = 44 mm and
by ±0.1% that choice is six-fold worse than `d2` = 36. And **maximising R balances the orders
rather than cancelling one**, so it buys a resolving power at an operating point but not a
change of scaling; changing the scaling is what [B]'s "third-order temporal focus" and its
two `TE` correction vectors are for (§18).

### What is assumed rather than derived

- **`d1..d4`** (20 / 50 / 90 / 130 mm) — guesses. The mirror field shape follows from them,
  and so does how strongly the tilt acts on the drift. **Never fitted.**
- **Board gap** 40 mm — assumed.
- **Injection angle** 1.28° in the template — from a ballistic oscillation count made here.
  The paper states **about 2°**, so the template currently contradicts a published number.
- **Drift z-faces are Neumann**, which asserts the structure repeats along z. True while the
  boards are parallel, false the moment they converge. Known wrong, stated on the template,
  and the alternative is far worse — see §4.
- **No injection optics at all** — no einzel lenses, no prism deflectors, no pulsed packet
  from the ion processor. See §7.

### Next, in order

1. **Settle the convergence.** §17 reproduces every published reversal figure at 0.56 mm and
   nothing at 0.20 mm, and the whole 2.8× is in what a 200 µm spacer tilts over. Look for a
   mirror-assembly length or a mounting baseline in the patent literature or the detector
   paper's figures. **Every reversal number in this document depends on it.**
2. **Get Grinfeld, Stewart, Makarov, Nucl. Instrum. Methods Phys. Res. A 1060 (2024) 169017** —
   *isochronous drift in elongated ion mirrors*. §16 derives the requirement (drift period
   constant to 5e-6) and §17 confirms the foil's published job is exactly to meet it. There
   is a whole paper on how; read it before optimising blind.
3. **Optimise the foil's 16-slice profile for drift isochronicity**, if (2) does not simply
   give the answer. 16 parameters, and the first thing here that genuinely wants
   `Einzel.Sweeps`.
4. **Fit `d1..d4` against the energy focus.** The figure of merit is **`R × spread`**,
   measured at 21.5 and constant across a 25-fold range of spread, which says the
   first-order energy term is uncancelled. §13 forbids fitting the depths against the
   reversal; this is the target they do control, and cancelling first order should move
   `R × spread` by orders rather than per cents.
5. **Table 1's `C⁽¹⁾` perturbation**, ~2.5 ppm/V per unit `TE1` — differential, so it
   does not wait on (4).
6. **A corpus example pinning the reversal**, once (1) is settled.

### Reading the rest of this page

It accreted chronologically and **five sections are marked superseded in place** — read the
marker before trusting any number in them. The three corrections that matter most:

- **The tilt axis was rotating the boards, not the mirrors** (§3, *The tilt axis was
  rotating the wrong thing*) — so the model contained none of the mechanism it exists to
  demonstrate. Fixing it took the gap to the published spacer from 6.3× to 1.33×.
- **Three reversal measurements used a predicate that cannot tell reversal from striking an
  electrode** (§3, *What was wrong with all three measurements below*).
- **The foil shape and its role were both wrong** (§11) — the shape is measurable off the
  published figure, and measuring the well it produces shows it is a lens rather than a
  decelerator.

---

## 1. What this is trying to be

A three-dimensional model of the **Thermo Astral** analyser: an asymmetric-track
multi-reflection time-of-flight instrument. Published, and used here:

### The published register

Everything below is from the open literature. **Nothing here came from conversation with
anyone at the vendor**, which is deliberate: the value of this model is that it is derived
from public information, and a number obtained privately would contaminate that. Two
sources carry almost all of it:

- **[A]** Stewart, Grinfeld et al., *Anal. Chem.* **2023**;95(42):15656-15664 -
  <https://doi.org/10.1021/acs.analchem.3c02856>. The instrument paper. Its figure 1 is
  drawn to scale and is measured in §11.
- **[B]** Stewart et al., *J. Mass Spectrom.* **2024**;59(4):e5006 -
  <https://doi.org/10.1002/jms.5006>. "Crowd control of ions in the Astral analyzer." Space
  charge, and by far the most detailed published account of how the analyser is *operated*.

A third, the ion processor, is the source of the injected packet and is registered
separately in `docs/literature-targets.md`: Stewart, Grinfeld et al., *J. Am. Soc. Mass
Spectrom.* 2023, [PMC10767742](https://pmc.ncbi.nlm.nih.gov/articles/PMC10767742/).

#### A counting trap in the published figures

Three quantities in these papers are easy to conflate, and two of them are numerically
identical, so this is written out once and referred to throughout.

| | |
| --- | --- |
| flight path | **>30 m** — metres, and the commonest thing to misremember as an oscillation count |
| **total** oscillations, whole flight | **24 to 26** |
| oscillations **outbound**, to the drift reversal | **12 to 13** ([A]: "the first 12–13 oscillations", then "the following 12–13") |
| reflections per oscillation | **2** — 30 m / 24 = 1.25 m, and the turning points are 625 mm apart, so a round trip is 1.25 m |
| so **reflections outbound** | **24 to 26** |

**The last row and the second are the same numbers and different quantities.** Every
comparison in §§13 to 15 is against *reflections outbound*, and the drift-per-reflection
figure the published set implies is 335 / 25 = **13.40 mm**. A measurement reported in
oscillations must be halved before it is compared with anything here.

#### The analyser

| | | |
| --- | --- | --- |
| Beam energy | 4 keV | A, B |
| Mirror electrodes | five per mirror - one grounded `U0`, one strongly accelerating `U1` *which provides the spatial focusing*, three reflecting `U2..U4` | B |
| Oscillations / flight path | **24 / 30 m**, giving 625 mm cap-to-cap (cap-to-cap is derived here, not stated) | A, B |
| Drift distance | **310-360 mm, mean 335**, varying with injection angle | B |
| Mirror convergence | **200 µm spacer** | B |
| Resolving power | > 100,000 | A |
| Detector | HDR | A |

#### What [A] says, verbatim, about the track

Checked against the PMC full text rather than recalled. Each of these bears on a modelling
decision and the wording matters:

> After the first reflection, the inclination angle of ion packets is adjusted by the
> second electrostatic prism to the optimal value of **about two degrees**.

So 2° is the *drift* angle, set after the first reflection, not a prism setting to be
reconciled with a derived one. The template's 1.28° contradicts this and came from a
ballistic oscillation count, which halves the true count because the drift decelerates.

> The asymmetric ion mirrors are designed to be slightly converging toward each other,
> making the ion drift decelerate over the course of the first **12-13 oscillations**
> toward the distant end of the mirrors.

> The drift is eventually reversed by a returning electrostatic potential formed by
> **mirror tilt as well as refraction on the ion foil**, and over the following 12-13
> oscillations, the ions drift back to the second electrostatic prism of the injection
> optics.

**This sentence is in tension with §11's conclusion that the foil is a lens and not a
decelerator.** [A] names the foil as one of two contributors to the *returning* potential;
[B] names the convergence as what stops the drift and convergence-plus-foil as what
refocuses. The measured contour (§11) produces a well deepest at two-thirds of the drift,
which in a single-bias configuration cannot form a returning potential at 310-360 mm in
either sign. The likeliest resolutions, none established: the four plates are biased
independently rather than at one potential; the drift-fraction calibration of the contour
is off by more than its stated ±5%; or [A]'s "refraction" is describing the refocusing [B]
attributes to the foil. Recorded as open rather than resolved.

> The ion foil electrodes are mounted between the mirrors, above and below the ion path,
> and are biased with a small tunable potential between 0 and -20 V.

> The precise shapes of the ion foil electrodes serve to compensate for the temporal
> aberration induced by the mirror asymmetry and improve the quality of the spatial focus
> at the detector, as well as to compensate for mechanical misalignments of the mirrors.

> While making a complete set of **24-26 oscillations** over the >30 m track, the ion
> packets are separated according to their mass-to-charge ratios and, being **refocused
> spatially**, arrive at a high dynamic range detector located at the **proximal end next
> to the ion processor**.

And from [B], the second prism is also the *drift focusing* control: detuning it by ±5 V
moved the analyser "slightly detuned from its apex" and made space-charge overtones
appear sooner. So the second prism sets both the drift angle and where the drift focus
lands, which is one knob and not two.

#### The mirror calibration - the most directly usable thing in either paper

Mirror potentials are not four numbers but a **two-parameter family**, and [B] gives it in
closed form:

> `U_k = e0 * ( Ck(0) + TE1 * Ck(1) + TE2 * Ck(2) )`

where `e0` is the nominal ion energy. Table 1 of [B]:

| | C(0) | C(1) | C(2) |
| --- | --- | --- | --- |
| U1 | -1.840 | 5.67 | -0.256 |
| U2 | -1.158 | -1.616 | -0.654 |
| U3 | 0.916 | -0.715 | 0.032 |
| U4 | 1.503 | -2.963 | -0.361 |

- **C(0)** was optimised for flat oscillation time versus energy over **4000 ± 100 V**. This
  is the set the template carries.
- **TE1 = 0.01 shifts the `(t|e)` dependence by a constant ~2.5 ppm/V** across the populated
  energy range, "correspondingly sparing the spatial focusing of the mirrors".
- **TE2 = 0.1** adds a *linear* trend to `(t|e)`, hence a quadratic ToF-versus-energy term.
- Calibration in practice: inject isolated MRFA, scan TE1 and ion energy, record the loci of
  best resolution. Those loci form a wavy line approximately cubic in shape; **the optimum
  is zero inclination at its point of inflection, which generates the third-order temporal
  focus and the best achievable resolving power.**

**Why this matters more than it looks.** C(1) and C(2) are *differential* statements about
the mirrors, with a published sensitivity attached. They can be tested against a model whose
absolute focus is still wrong: apply the perturbation, measure the change in `(t|e)`,
compare against 2.5 ppm/V. That is the sharpest literature regression available here and it
does not wait on fitting `d1..d4`. It is item 4 of *Next, in order*.

Note also that the design condition is a **third-order** temporal focus, not first-order.

#### The injection chain [B]

Ions arrive already bunched. None of this is modelled yet (§7).

| | |
| --- | --- |
| Extraction from the ion processor | **±900 V pulsed "push and pull"** on opposing halves of the trapping structure, through an extraction slot |
| Trap axial confinement | auxiliary DC electrodes, **wedged**, **4 mm long**, biased **-5 V**, hosted in the equatorial splits of the RF electrodes |
| Trap RF | **4.5 MHz**; amplitudes used 1000 Vp-p and 1400 V, **1800 V maximum** |
| On entry to the analyser | accelerated to **4 keV**, shaped by **a pair of rectangular einzel lenses** (Lens 1, Lens 2) |
| Injection angle set by | **a pair of prism-shaped deflectors** - plate electrodes above and below the beam, where the angle of the plate against the trajectory induces the deflection |

#### What happens to the packet in the drift [B]

This is the passage that most directly indicts the current model, so it is quoted rather
than paraphrased:

> The ions drifted down the gap between the mirrors and **dispersed under their thermal
> velocity spread.** ... During the drift expansion, **the size of the ion bunch
> substantially exceeds the distance between the trajectories on different oscillations**
> ... Therefore, the ion populations on different oscillations overlap in space.
> Nevertheless, **the optimized convergence of the mirrors and a set of specially shaped
> electrodes, referred to as ion foil, cause the drift spread to reduce on the way back from
> the drift reversal point** so that the ions arrive at the detector as a single bunch
> focused both spatially and temporally.

And elsewhere in [B], that the expansion reaching **up to 50 mm** is deliberate - "a key
point of minimization of the Coulomb repulsion forces in the ion bunch".

Three things follow that the model must eventually satisfy and does not:

1. Thermal spread in the drift direction is **the** dominant term. Confirmed here
   independently: it alone gives R = 11.5 and a 25.8 mm packet.
2. The spread is allowed to reach 50 mm and is then **refocused on the return leg**, by the
   convergence **and** the foil together. Two mechanisms, and [B] names both.
3. Because the bunch is wider than the oscillation pitch, **populations from different
   oscillations overlap in space**. A model that tracks one ion cannot see this at all, and
   it is a real constraint on any detector or aperture reasoning.

#### Space charge [B]

Not modelled here, and it is not an excuse for the resolving-power gap - space charge only
degrades, and this model has none. Recorded because it bounds what "good" means.

| | |
| --- | --- |
| Resonant (in-peak, similar m/z) effects become strong | **~1e3 ions in peak** |
| Self-bunching and coalescence | **~1e4 charges in peak** |
| 50 k resolving power crossing, isolated MRFA | **2500 ions in peak** |
| the same, with the trapped cloud broadened by other calibrants | **5000** - broadening the *trap* population makes the analyser more tolerant |
| Trapped charges within the device's linear capacity | 30 k yes; **100 k and 300 k beyond it** |
| Primary effect of a space charge load | **shifts the focal plane**, roughly correctable by adjusting a single potential (TE1) |
| m/z dependence | low m/z loses the most resolution (deeper RF well, higher charge density); high m/z shows a positive m/z shift |

**Their own simulation used 22 oscillations rather than the usual 24**, which is worth
knowing before comparing any simulated number against a measured one.

**Not published: the electrode lengths and apertures.** They are what the published
coefficients were optimised against, so they are the free parameters of an inverse problem
— find a geometry consistent with the published potentials *and* the published acceptance
window. If patent literature later gives real dimensions, they check what this found, which
is a stronger result than being handed them.

**The convergence is the mechanism, not a tolerance.** It is what makes the drift
decelerate and reverse — the "asymmetric track". A model without it is a generic MR-TOF
wearing the right dimensions.

---

## 2. What exists now

### Committed and green (1,101 tests)

- **`astral-mirror.json`** — the 2-D five-electrode mirror at published potentials, with
  `d1..d4` as free lengths. `AstralMirrorStudy` drives the shipped optimiser over them.
- **Tilted boxes** (`tiltAxis`, `tiltHalfTurns`) — the convergence is now expressible.
  Measured proportional down to **a thousandth of a cell**. Half turns, so `1.0` is 180°
  and a right angle is `0.5`; the Astral's 200 µm over 350 mm is `1.8e-4`.
- **Neumann faces on `solve3d`** (`lowerZEdge` … ) — see §4, this was the blocker.
- **`einzel estimate` costs a study**, calibrated on the machine that will run it. Use it.

### The skeleton is a shipped template now

`src/Einzel.Library/Templates/astral-3d.json` — 20 electrodes (16 mirror boards, 4 foil
plates), schema 0.7, 4 mm cell, about 5 s to solve in Release. It was throwaway JSON when
this page was written; §3's generator script is kept below as the record of how it was
derived, but **the template is the copy that matters** and the script no longer carries
the foil.

---

## 3. The 3-D skeleton — it flies

**Two bugs, both mine, both fixed.** The first attempt gave `MaximumStepsExceeded` after
20,000,000 steps without arriving. It now flies:

| flight window | outcome | steps | x | y | z |
| --- | --- | --- | --- | --- | --- |
| 5 µs | in flight | 713 | 529.68 | −0.00 | 181.87 |
| 20 µs | in flight | 2,763 | **83.54** | 0.00 | 202.49 |
| 60 µs | in flight | 8,034 | 349.62 | −0.00 | 257.46 |
| 400 µs | **arrived** | 16,012 | 389.07 | −0.00 | 340.00 |

It oscillates in x (529 → 83 → 349), drifts forward in z, and reaches the detector.

| | |
| --- | --- |
| flight time | **120.058 µs** against a predicted 165 mm / 1374 m/s = 120.1 µs |
| drift rate | 1374 m/s against `v·sinθ` = **1374 m/s** |
| path | 4.72 m = 3.77 oscillations |
| transverse | y = −0.00 mm throughout — no spurious force |
| energy drift | 2.16e-6, **just over ACC-4's 1e-6** — expected at a 4 mm cell, watch it as you refine |

### Bug 1: the drift faces were grounded

See §4. Fixed by `"lowerZEdge": "neumann"`.

### Bug 2: the mirror electrodes were inside-out

Depth must be measured from each mirror's **mouth** inward, so U4 (+6012 V, the reflector)
sits *furthest* from the beam and U1 (−7360 V, accelerating) nearest it. The first generator
measured depth from x = 0, which put the reflector at the mouth — the ion met +6012 V on
arrival instead of being accelerated in. Combined with bug 1 it escaped to **x = 4643 mm**
in a 635 mm analyser and then coasted, which is where the 20 M steps went.

### It is a shipped template now, and parametric

```bash
einzel new models/astral.json --from-template astral-3d
```

The hand-written generator this page used to carry is gone: the geometry is a template with a
**declared parameter surface**, so `convergence`, `injectionAngle`, `driftLength`, `boardGap`
and the four electrode depths are things a study can scan, sweep or optimise rather than
numbers to regenerate a file for. It reproduces the hand-written skeleton exactly — 128.3455
µs, x = 508.35, z = 340.00, both ways.

Two things about writing it are worth knowing before you edit it:

- **Every length must be a declared parameter.** The grammar has no unit literals, so
  `mouth - 20` is metres minus a pure number and is refused. That is why `vacuumPad`,
  `boardThickness` and `detectorInset` exist as parameters rather than as constants.
- **Use unary minus, not `0 - x`.** A dimensionless zero satisfies any dimension in some
  positions and not as the left operand of a subtraction; `-halfGap` always works.

The tilt is `asinPi(convergence / 2 / driftLength)` because the grammar has no arctangent.
At this angle the two agree to about 7e-10 of the value, which is stated in the parameter's
own description rather than left for a reader to wonder about.

Work at a **4 mm cell** (0.56 M nodes, solves in seconds) while debugging kinematics — field
accuracy does not matter until the trajectory is sane.

### The convergence works: the drift decelerates, and it reverses

**Measured, with a clean control.** Tilting the mirror boards about x makes the gap vary
along the drift, which is what breaks the translational invariance and gives the drift
somewhere to go.

First, the tilt reaches the solved field, and proportionally — the potential on axis at
100 mm depth, compared between one end of the drift and the other:

| convergence | difference between the ends |
| --- | --- |
| none | 2e-6 V — round-off |
| 200 µm | **7.417 V** |
| 800 µm | **30.91 V** |

4.167x for a 4x tilt: proportional, as the cut-cell measurement predicted at millimetre
scale and this confirms at instrument scale.

Then the drift velocity along the analyser, per 40 mm segment:

| segment | parallel boards | converging 800 µm |
| --- | --- | --- |
| 175 → 200 mm | 1374.34 m/s | 1363.25 |
| 200 → 240 | 1374.34 | 1333.11 |
| 240 → 280 | 1374.34 | 1294.98 |
| 280 → 320 | 1374.34 | 1246.95 |
| 320 → 340 | **1374.34** | **1174.03** |

**The parallel control is constant to the last digit printed**, which is what a
translationally invariant analyser must do — there is no axial force for it to feel. The
converging case decelerates monotonically, and the decrements grow: 11, 30, 38, 48, 73 m/s.

**And pushed further, it reverses.** At 12.8 mm of convergence the ion never reaches the
detector: it goes out to z = 314 mm, stops, and comes back to z = 64 mm.

| t µs | 20 | 60 | 100 | 140 | 180 | 220 | 260 | 300 | 400 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| z mm | 198.6 | 231.8 | 258.1 | 291.0 | **313.9** | 281.1 | 250.6 | 224.3 | 64.4 |

That is the asymmetric track's defining behaviour, and it is what a generic MR-TOF cannot do.

### Two things this measurement is NOT

> **Partly settled.** The Neumann-face caveat below is real and has since been measured: it is worth about 11 per cent, converged at 50 mm of z padding, and grounding those faces instead pins the ion in z entirely. See *The drift faces are worth 11 per cent*.


**It is not quantitative near the z boundaries, and the reason is a boundary condition that
is now wrong.** The skeleton declares both z faces Neumann because stripe electrodes make
the geometry repeat along the drift — which was true while the boards were parallel and is
**false the moment they converge**. A Neumann face is a mirror, so beyond z = 350 the gap
widens again and beyond z = 0 likewise: the modelled instrument is a bowtie, not a wedge.
The returning velocities above (−2045 m/s against +1180 outbound) are larger than a
conservative axial well should give, and that asymmetry is the most likely place the wrong
boundary shows.

**Fixing it is a modelling question, not an engine one.** A real analyser ends in deflectors
and an ion foil, not in a symmetry plane. Until those exist, take the deceleration and the
reversal as demonstrated and the numbers as indicative.

**And the foil is not only an end effect** — see §7. If it controls z focusing along the
whole drift, the missing element is not at the boundary but everywhere the ion goes, and the
convergence in this model is standing in for two mechanisms at once.

### The convergence needed is set by the oscillation count, and that was testable

The 12.8 mm above is **not** a disagreement with the published 200 µm. The deceleration
accumulates *per reflection*, and this skeleton makes 3.77 oscillations where the instrument
makes 24 — so the same total deceleration needs far more per reflection. Holding the
convergence at **800 µm** and varying only the injection angle:

| injection | oscillations | outcome | mean drift |
| --- | --- | --- | --- |
| 2.0° | 4.03 | arrives | **+1285.6 m/s** |
| 1.0° | ≥37.7 | never arrives | **−350.1 m/s** |
| 0.5° | ≥37.7 | never arrives | **−303.5 m/s** |

A negative mean drift means the ion finished *behind* where it started. So at the published
order of magnitude for the convergence, and a plausible oscillation count, the track
reverses. The 1.0° profile:

| t µs | 50 | 150 | 300 | 450 | 600 | 750 | 900 | 1050 | 1200 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| z mm | 207.8 | 264.3 | **321.3** | 305.2 | 233.8 | 134.1 | 7.7 | −118.8 | −245.2 |
| drift m/s | +656 | +565 | +380 | **−108** | −476 | −664 | −843 | −843 | −843 |

**The turn-around happens inside the modelled region** — at z ≈ 321 mm of a 350 mm domain —
so it is not an artefact of the boundary. The flat −843 m/s at the end is the ion having
left the solve box, where the field is now zero and it genuinely coasts; before the
out-of-domain fix it would have been flung by a fabricated field instead.

### The reversal threshold, bisected — and it is a very steep power law

> **Superseded** — bisected on a predicate that cannot tell reversal from striking an electrode. See *What was wrong with all three measurements below*.


`einzel boundary` finds the convergence at which the drift reverses: the ion arrives below
it and does not above, and a figure that stops existing is outside by construction, so a
cut-off is exactly what this search is for. Eleven to fourteen evaluations rather than a
grid.

| injection | oscillations | reversal convergence | bracket |
| --- | --- | --- | --- |
| 2.0° | 4.0 | **11.447 mm** | [11.428, 11.467] |
| 1.0° | ~8 | **0.4638 mm** | [0.4405, 0.4872] |
| 0.5° | ~16 | **< 0.05 mm** | bounded above only |

**24.7x for one halving of the injection angle** — an exponent of 4.6 over that octave.
Two mechanisms multiply: halving the angle doubles the reflections, so the deceleration
accumulates over twice as many, *and* it quarters the axial kinetic energy that has to be
removed. That predicts a cube; the measurement says closer to a fourth power, so something
else is contributing and two points cannot say what.

**The control that makes these numbers mean anything**: at 0.5° with the convergence set to
*exactly* zero, the ion arrives in **479.96 µs** against a ballistic 479.9 — so the geometry
transports it perfectly when nothing is converging, and every reversal above is the
convergence rather than an accumulating error.

**A trap paid for here**: the first attempt gave every angle a 400 µs flight ceiling. The
0.5° case has a *ballistic* transit of 480 µs, so it would have been scored as reversed at
every convergence including zero — a window shorter than the phenomenon measures the window.
The ceiling is now 2.5x each angle's own ballistic transit.

**Qualifications the search itself reports**: `boundary.below-acc6` (bisected to 1 per cent,
where ACC-6 asks for 1/500), `TRAJECTORY_INCOMPLETE` on the reversed side, which is what a
reversed ion *is*, and `ENERGY_DRIFT_EXCEEDS_BUDGET` at this 4 mm cell.

### The injection conditions are published, and they do not rescue the resolving power

The packet had been invented — 0.5 mm spreads chosen for roundness. The Ion Processor paper
(Stewart et al., *J. Am. Soc. Mass Spectrom.* 2023, doi:10.1021/jasms.3c00311, retrieved
from PubMed Central) measures it:

| | |
| --- | --- |
| beam spatial profile | Gaussian, **6σ = 2.4 mm**, so **σ = 0.4 mm** |
| and it matches | the 2.5 mm length of the negative auxiliary DC trapping electrodes |
| temperature | the room temperature of the buffer gas it thermalised in |
| extraction | orthogonal, after a 4 kV lift, RF quenched at a zero crossing, ±900 V push/pull |
| slot | 8 mm long × 0.8 mm wide |
| tuning | acceleration voltages set to the **first time-focus** |

**Substituting the measured packet changes nothing**, and the arithmetic said so first: a
20 per cent change in a term worth 742 ns cannot move a total dominated by 41,423 ns of
thermal spread. R went 6.56 → 6.51.

| | invented 0.5 mm | measured 0.4 mm |
| --- | --- | --- |
| arrival width | 73,876 ns | 74,440 ns |
| resolving power | 6.56 | 6.51 |

So the packet's **size** was never the problem. Its **velocity** spread is, and no upstream
conditioning removes it: 300 K on m/z 524 is 69 m/s in one dimension, which over a 934 µs
flight is 64 mm of transverse travel if the ion travels ballistically. What bounds it is
periodic focusing — every reflection in a gridless mirror is a lens — so the transverse
motion should be oscillatory rather than straight. This model's 17 mm packet is less than
the 64 mm ballistic figure, so the mirrors here focus, weakly.

### Space charge is a published problem, and it is not this model's excuse

The run raises `spacecharge.ignored` as a non-suppressible violation, and the effect is real:
"Crowd control of ions in the Astral analyzer" (Stewart et al., *J. Mass Spectrom.* 2024;
59(4):e5006, doi:10.1002/jms.5006 — full text now extracted and registered in §1) calls
space charge "the Achilles' heel of all high-resolution ion optical devices" and identifies
two mechanisms for this analyser: a **resonant effect between ions of similar m/z in flight**,
and **expansion of trapped packets prior to extraction**. The remedies described are
operational — optimum operating points and compensated ion mirror calibration.

**But it cannot explain the gap here, and the direction is the reason.** Space charge only
degrades resolving power. This model does not include it, so **R = 6.5 is an upper bound on
what this geometry can do** — while the instrument reaches beyond 100,000 *with* space charge
present. The optics modelled here are therefore wrong by more than four orders, not fewer.

**And one phrase in that abstract is independent confirmation of where the focusing lives:**
it describes the analyser as "incorporating ion focusing via a pair of converging ion
mirrors". The convergence is not only the drift-reversal mechanism measured above — it is the
focusing element. That is the same conclusion the acceptance decomposition reached from the
other direction, and it puts `d1..d4` at the centre of both.

### What was wrong with all three measurements below

Three faults, found by re-measuring with the outcome **reported** rather than inferred.

**1. The predicate could not tell reversal from striking metal.** Every bisection here
asked "does a flight time exist", which is false for a reversed ion, a struck ion and a
timed-out ion alike. Re-run reporting `outcome`:

| d4 | 0.10 mm | 0.20 | 0.40 | 0.80 | 1.60 |
| --- | --- | --- | --- | --- | --- |
| 130 mm | arrives | arrives | arrives | arrives | **reversed** |
| 195 mm | arrives | arrives | **struck** | struck | struck |
| 231.9 mm | **struck** | struck | struck | struck | struck |
| 286 mm | arrives | **reversed** | reversed | reversed | reversed |

At 195 mm the search found the *arrives → strikes metal* crossing. At 231.9 mm the ion
strikes across the whole declared range. **Only 130 and 286 mm measured a reversal at
all**, so the "power law" through four points was drawn through two.

**The engine was not at fault, and that is the uncomfortable part.** `BoundarySearch`
throws when both bracket ends agree, and its confirmation walk raises
`boundary.multiple-crossings` when the predicate flips back — both built for exactly
this. The analysis script read `boundary.value` and dropped `warnings`: the same *the
shortest spelling discards the evidence* shape this project has fixed four times inside
the engine, committed once outside it. A warning that is emitted and not read costs the
same as one never emitted.

**2. It was a scan along a trade, and was written up as a scan in one variable.**
`mouth = d4`, so the mirror's back sits at the cap and its mouth at depth `d4` — which is
**physically right for a fixed envelope**, since a deeper mirror must reach further toward
the centre. What it is not is a free depth axis: at a fixed 625 mm cap-to-cap, depth and
field-free length are one degree of freedom, not two, and scaling `d1..d4` walks the
mirrors together:

| d4 | field-free gap = capToCap − 2·d4 |
| --- | --- |
| 130 mm | 365 mm |
| 195 mm | 235 mm |
| 286 mm | **53 mm** |

So the far ends of that scan are not deeper versions of one instrument, they are different
points on the depth-against-drift trade, and comparing them needs saying which. **Varying
depth at fixed field-free length is a different question and needs a bigger envelope**, not
a different parameterisation — `mouth` freed from `d4` would just put the mirror's back
outside the cap.

**3. A bisection is only comparable across runs that share a bracket.** The confirming
run used `[0.02, 2.0]` where every other depth used `[0.01, 8.0]`, and returned
**1.9884 mm** — *worse* than the 1.2513 mm baseline it was meant to improve on. A
non-monotone result from a monotone physical trend is the signal that the brackets, not
the physics, differ.

### A better observable: the transit diverges, so fit it rather than bracketing it

The drift decelerates, so the transit **lengthens smoothly and diverges** rather than
flipping — 185.13, 198.87, 238.18 µs at 0.05, 0.40, 1.00 mm, then pinned at the flight-time
ceiling. Fitting `Z = v_z0·t − ½·k·c·t²` to **two** runs gives both the launch drift speed
and the deceleration constant:

| | |
| --- | --- |
| `v_z0` from the fit | **858.3 m/s** |
| `v_z0` from the low-convergence transit alone | 850.8 m/s |
| `k` | 1.6533e-3 µs⁻² |
| reversal convergence `v_z0²/(2kZ)` | 1.4145 mm |
| the same, bisected in 11 evaluations | 1.2513 mm |

**Two runs against eleven, agreeing to 14 per cent**, and what comes back is a physical
constant rather than a bracket that means nothing outside its own range. The 14 per cent
is the deceleration not being quite uniform; a bracket cannot report that at all.

### The drift faces are worth 11 per cent, and the alternative is catastrophic

§4 recorded the Neumann drift faces as the leading caveat — a mirror face says the
structure repeats along z, which is true of parallel boards and **false the moment they
converge**, so the modelled instrument is a bowtie rather than a wedge. That is still
true, and it is now *measured* rather than feared. Padding the domain in z while holding
the ion's drift fixed at 10 → 325 mm:

| z padding | k (µs⁻²) | reversal convergence |
| --- | --- | --- |
| none (as shipped) | 1.6533e-3 | 1.4145 mm |
| 50 mm | 1.5535e-3 | 1.5665 mm |
| 150 mm | 1.5473e-3 | 1.5778 mm |

**7.4 per cent from none to 50 mm, then 0.4 per cent** — converged. So the shipped
template's zero padding costs about 11 per cent of the answer, and the caveat is real but
an order of magnitude too small to matter for the question this model exists to ask.

**And grounding those faces instead is not a slightly different answer, it is a different
instrument.** With `lowerZEdge`/`upperZEdge` removed, the domain walls become earthed
plates 10 mm from where the ion is trying to turn round, and the mirror stack at kilovolts
against them digs an axial well the ion simply sits in: **z = 168.0 mm at c = 0.10 and
169.1 mm at c = 0.80**, launched at 167.5, for the whole 450 µs. It does not drift at all.
Neumann is the right choice of the two, and now for a measured reason.

### The tilt axis was rotating the wrong thing

**`tiltAxis: "x"` converges the boards, not the mirrors.** A rotation about x mixes y and
z, so it closes the y gap between the two boards along the drift. The mirror surfaces have
their normals along **x**, and a rotation about x leaves them exactly where they were — so
the model contained **no mirror-tilt impulse at all**.

That is the whole missing factor, and the arithmetic says so twice. A tilted mirror gives
each reflection a z-impulse of θ times its x-impulse, and the x-impulse is fixed at
`2·m·v_x` — so `Δv_z = −2·v_x·θ` per reflection, **independent of mirror depth**, and
`a_z = 2·v_x·c/(L·T)`. Inverting the measured `k` gives an effective oscillation period of
**151 µs**. The ion's real period is **at most ~32 µs** — its 182 µs flight covers at most
7.14 m (39.2 mm/µs, which it only reaches outside the mirrors) at no less than the 1.25 m
mouth-to-mouth round trip, so at least 5.7 oscillations. **That makes 4.7× an upper bound
on the shortfall rather than a measurement of it**, which is enough to say the mechanism is
wrong and not enough to say by how much; the direct comparison below is what settles that.
What the board convergence does decelerate the drift by is the transverse confinement stiffening as the gap narrows, which is real and weaker.

Rebuilding the same geometry with `tiltAxis: "y"` on the mirror stacks, near and far
tilted oppositely so the mirrors converge along the drift:

| tilt | c = 0.10 | c = 0.80 | k (µs⁻²) |
| --- | --- | --- | --- |
| boards, axis x | 182.54 µs | 211.04 µs | +1.547e-3 |
| mirrors, axis y, near +θ | 171.75 µs | 138.60 µs | **−4.682e-3** |
| mirrors, axis y, near −θ | 188.68 µs | **reversed at z = 204.7** | +4.530e-3 |

**The sign is a control, not a nuisance.** One sign shortens the transit — the mirrors
*diverge* and the drift accelerates — and the other lengthens it and reverses. A mechanism
that produced deceleration whichever way the mirrors were tilted would not be a tilt.

### Against the published spacer, reconciled to 1.33x

Two changes, each measured on its own: the tilt axis, and launching at the **start** of
the drift rather than its middle. The second is not a fudge — the skeleton launched at
`driftLength / 2` and so threw away half the length the instrument has to turn the ion
round in, and the reversal condition depends on the drift available.

| | reversal convergence | against a 200 µm spacer |
| --- | --- | --- |
| boards (axis x), launch mid-drift, no z padding | 1.2513 mm bisected, 1.4145 fitted | 6.3–7.1× |
| boards, converged domain | 1.5778 mm | 7.9× |
| **mirrors (axis y), launch mid-drift** | **0.5397 mm** | **2.70×** |
| **mirrors (axis y), launch at drift start** | **0.267 mm** | **1.33×** |

At the full drift the transit lengthens 377.43 → 400.77 → 431.94 → 478.47 µs across
c = 0.05 → 0.20 mm and the drift has reversed by 0.30, so the fitted 0.267 mm sits inside
a measured bracket. **The remaining 1.33× is smaller than the uncertainty in the geometry
that is still guessed** — the four electrode depths, the board gap, the exact ion energy —
so there is no longer a discrepancy to attribute to them.

**A flight-time ceiling impersonated physics on the way here, and it is worth recording
because it nearly became the headline.** At the model's 450 µs ceiling, c = 0.20 mm
appeared to reverse: the run ended `MaximumFlightTimeReached` at z = 312.1 mm, short of
the 325 mm detector. Raised to 2000 µs, the same model **arrives**, at 478.47 µs — the ion
was still moving forward the whole time, 13 mm short when the clock stopped. This is the
incomplete-arrival trap already documented for `einzel compare`, met from the other side:
*a mean over the subset that arrived is not a transit time, and a run that stopped early
is not a run that turned round.* The verdict now tests where the ion **ended**, not that
it ran out of time.

**The 0.267 mm is a tested prediction, not a fitted number.** The fit used c = 0.05 and
0.20 only; two further runs, which took no part in it, bracket it:

| c | outcome | |
| --- | --- | --- |
| 0.26 mm | arrives, 616.66 µs | |
| 0.28 mm | reversed, ends at z = −782.7 mm | driven back out of the analyser |

so **c_rev = 0.267 mm, bracketed [0.26, 0.28]**. The transit at 0.26 is 616.66 µs against
377.43 at 0.05 — the divergence is what makes the threshold sharp.

**And the fit's other output is checkable against a closed form the engine had no part
in.** The launch drift speed is `sqrt(2qV/m)·sinα` = **877.69 m/s** for m/z 500 at 4 kV
and 1.28°, and the fit is not told any of those numbers — it sees two transit times through
a solved 3-D field. Better still, it is a second and independent reason to pad the domain:

| | fitted `v_z0` | against 877.69 |
| --- | --- | --- |
| no z padding | 858.3 m/s | −2.2% |
| 150 mm padding | 876.9, 877.6, 877.8 m/s | **−0.1%** |

**The fit converges onto the closed form as the domain converges.** With the Neumann face
10 mm from the turning point the fit absorbs the boundary's distortion into the one
parameter that should not depend on the geometry at all, and says so by being 2.2% wrong
about a quantity fixed by the ion's energy and launch angle.

### The oscillation count has a closed form, and it carries the mirror's efficiency

Out-and-back time is `2·v_z0/(k·c)`, so the oscillation count is `N = 2·v_z0/(k·c·T)`.
The specular argument says `k·T = 2·v_x/L`, which would cancel both and leave `α·L/c`.
**Measured, it does not cancel to one.** Write the shortfall as a dimensionless efficiency

> **η = k · T · L / (2 · v_x)** and then **N = α · L / (η · c)**

`η` is the fraction of the ideal specular z-impulse the mirror actually delivers, and for
this geometry it is **0.578**, so the coefficient is 1.73 rather than 1.

| | |
| --- | --- |
| measured out-and-back at c = 0.30 mm | 1356.96 µs |
| measured oscillation period | 29.54 µs |
| **so N measured** | **45.94** |
| `α·L/c` | 24.95 |
| ratio | **1.84** |

**The functional form is exactly right and the coefficient is a property of the mirror.**
What `α`, `L` and `c` fix is `N·η`; the mirror design fixes `η` and the period separately.
That is still a separation of the design into two halves — it is just that the second half
has a name and a measurable value rather than being absent.

**And it reopens the electrode depths as a well-posed question.** The specular impulse
`2·m·v_x·θ` per reflection is depth-independent, which is why the depth scan was unlikely
to constrain anything — but `η` is not: it is set by how the equipotentials are oriented
along the part of the mirror the ion actually traverses, which is exactly what the stage
depths and potentials determine. **`η` is the figure of merit the depths move**, and it is
measured from two runs and a trajectory.

**Validated end to end, with nothing refitted.** A model at c = 0.30 mm — just above the
0.27 mm reversal threshold, so the ion turns round inside the drift — with a detector placed
behind the launch point to catch the return:

| | |
| --- | --- |
| predicted out-and-back time, `2·v_z0/(k·c)` | 1275 µs |
| measured | **1356.96 µs** |
| | 6.0 per cent |

`v_z0` and `k` come from the two-run fit at a *different* convergence and a *different*
drift length, and the detector geometry took no part in either.

**Two falsifiable scalings, and both hold.** The `1/c` dependence, anchored on the measured
c = 0.30 point — a wrong mechanism would give some other power. Anchored on the
measured c = 0.30 point and predicting the rest:

| c | predicted | measured | |
| --- | --- | --- | --- |
| 0.40 mm | 1017.7 µs | 1010.96 µs | 0.993 |
| 0.60 mm | 678.5 µs | 665.11 µs | 0.980 |
| 0.90 mm | 452.3 µs | 434.87 µs | 0.961 |

The ratio drifting *down* with `c` is the constant-`a_z` approximation degrading in the
direction it should: a larger spacer turns the ion round sooner, so it samples less of the
drift and less of the geometry the fit averaged over.

**And the `α/c` invariance, which is the sharper of the two** because it varies two things
and predicts *no change*, so there is nothing for a coincidence to hide behind. Scaling both
together leaves `N` alone while the turning point moves as `α²/c`, so the ion is genuinely
flying a different trajectory each time:

| scale on both α and c | time | vs reference |
| --- | --- | --- |
| 1.00 (reference) | 1356.96 µs | — |
| 0.75 | 1350.97 µs | 0.996 |
| 0.50 | 1354.31 µs | 0.998 |
| 0.35 | 1364.07 µs | 1.005 |

**Invariant to 0.5 per cent over a threefold range of both parameters**, with the turning
point moving by the same factor of three. Scaled *down* rather than up on purpose: the
turning point goes as `α²/c`, so doubling both would put it 600 mm past the launch and
outside the padded domain, where the field is zero and the ion would never come back. The 6 per cent is the
deceleration not being quite uniform — the mirrors are closer at the far end, so the
oscillation period shortens as the ion drifts, and a constant-`a_z` model reverses the ion
slightly too late.

**A caution this relation makes concrete.** At the published 200 µm and this model's 350 mm
drift, `N = 24` requires **α = 0.0137 (0.79°)**, where the design condition recorded earlier
in this document is 1.28°. That earlier figure came from a *ballistic* count — the drift
speed held constant across the traverse — and the drift is precisely what decelerates, so
the average speed is about half the launch value and the true count is about twice the
ballistic one. **Which of these matches the real instrument is not settled here**, because
the published "prism angle" of about 2° is a third number again and it is not clear which
angle it names.

### Depth is a weak lever, and η is not where it acts

`η` looked like the figure of merit the electrode depths had been missing. Measured, it is
not — it barely moves. Depths scaled together at a fixed 625 mm envelope, with `k` from two
runs and the period counted from a trajectory at each:

| d4 | field-free gap | period | **η** | reversal convergence |
| --- | --- | --- | --- | --- |
| 100 mm | 425 mm | 32.29 µs | **0.587** | 0.2871 mm |
| 130 mm | 365 mm | 29.53 µs | **0.578** | 0.2666 mm |

**η is flat to 1.5 per cent across a 30 per cent change in depth**, so the impulse
efficiency is a property of the mirror's *shape* — the stage proportions and potentials,
held fixed here — rather than of its scale.

What does move is the period, and it moves **down** with depth: at a fixed envelope the
field-free gap shrinks faster than the penetration grows, so a deeper mirror is a *shorter*
oscillation. Since `c_rev ∝ T/η` and `η` is flat, **the whole depth dependence of the
reversal convergence runs through the period** — 7.7 per cent in `c_rev` against 9.3 per
cent in `T`, which is the relation closing on itself.

**So depth is a weak lever: about 8 per cent of `c_rev` for 30 per cent of depth.** That is
the honest replacement for the retracted power law, and it is one more reason the tilt axis
rather than the depths had to be the 6× — nothing available in `d1..d4` at this envelope
comes close.

**A free validity check on every fit, which earned its keep immediately.** The fitted `v_z0`
has a closed form the fit is not told — `sqrt(2qV/m)·sinα` = 877.69 m/s — so a fit that
comes back with anything else is not describing a decelerating drift. A third depth,
d4 = 160 mm, returned **14,453 m/s** and an η of 10.57. Arithmetic on nonsense, caught by one
comparison rather than by the number looking wrong — 10.57 sits perfectly plausibly next to
0.578 if nobody is checking, and it is exactly what a power law would have been drawn
through.

**What it is doing there is worth knowing, and it is the geometry rather than the solver.**
The two runs the fit combined are not the same kind of trajectory at all:

| c | outcome | time | implied drift |
| --- | --- | --- | --- |
| 0.05 mm | arrives | 378.85 µs | 831.5 m/s — decelerated, as expected |
| 0.20 mm | arrives | **68.54 µs** | **4596 m/s — accelerated 5.2×** |

At a 305 mm field-free gap between two 160 mm mirrors the ion stops behaving like a
drifting oscillator and is pushed *along* the drift instead. Whether that is a real
property of a deep, closely-spaced pair or an artefact of a 4 mm cell in a now-crowded
domain is **not established here**, and it is the reason the depth table above stops at two
points rather than three. Fitting a rate to two trajectories of different kinds produces a
number rather than an error, which is the whole argument for checking `v_z0` against its
closed form on every fit.

### What this does and does not establish

**It does not establish that the real instrument tilts its mirrors rather than its
boards.** What it establishes is that *this* model, with mirror convergence, needs a
spacer of 0.27 mm where the published instrument uses 0.200, and with board convergence
needs 1.58 mm. A drift reversal is what an asymmetric-track analyser is for, and only one
of the two mechanisms produces it at the published scale — which is evidence, not proof,
and the template records it as such.

**The four electrode depths remain unmeasured.** The scan that appeared to constrain them
did not, and the analytic model says why it was unlikely to: a tilted mirror's z-impulse
is `2·m·v_x·θ` whatever its depth, so depth enters only through the oscillation period.
Constraining `d1..d4` needs a figure of merit they actually move — the mirror's own
energy-focusing order, which `astral-mirror` already measures in two dimensions.

### The design condition, and the gap to the published instrument narrowing to 6.3x

> **Superseded — read **What was wrong with all three measurements below**, immediately above, first.** The reversal convergences quoted here were bisected on a predicate that cannot tell reversal from striking an electrode, and the depth conclusion is withdrawn.


**24 oscillations is an out-and-back number, and that fixes the injection angle.** With the
published 30 m path, 625 mm cap-to-cap and a 335 mm drift, the arithmetic is
`oscillations = 2 x drift / (2 x capToCap x sin θ)` — the factor of two because the ion
comes back — and 24 of them needs **1.28°**, against a published prism angle of about 2°.
A one-way track would need 0.64°, which is further from the published figure, so the
out-and-back reading is the one the numbers support.

At that design condition the reversal threshold is:

| | |
| --- | --- |
| bisected | **1.2513 mm**, bracket [1.2357, 1.2669] |
| predicted by the angle scaling law | ~1.4 mm |
| published spacer | 0.200 mm |

**The scaling law was right to 12 per cent at an angle it was never fitted on**, which is
what makes it a law rather than a curve through two points.

> **The 1.28° is wrong and so is the 6.3× and the later 1.33×.** Stewart et al. give the
> working inclination as **about 2°**, adjusted by the second prism after the first
> reflection — not a prism angle to be reconciled with a derived one. The 1.28° here came
> from a *ballistic* oscillation count, which halves the true count because the drift
> decelerates. At the paper's 2° the convergence mirror tilt alone would need is 0.49 mm at
> best against a published 0.200, so the gap is about 4× and its cause is named in the paper:
> the ion foil. See §7.

**And getting the oscillation count right closed most of the gap**: 57x at the original
2°/4-oscillation skeleton, **6.3x** here. The remainder is in the geometry that is guessed.

### It is not the board gap, which was the obvious guess

> **Superseded** — same contaminated predicate; the gap trend has not been re-measured.


| board gap | reversal convergence |
| --- | --- |
| 40 mm | 1.2513 mm |
| 30 mm | **2.5487 mm** |

**Narrower needs *more* convergence, not less** — about `gap^-2.5` — so the board gap moves
the threshold strongly and in the wrong direction to explain the gap to 200 µm. Closing 6.3x
by gap alone would need about **84 mm**, which is not a credible board separation for this
envelope.

**And below about 20 mm the question stops being well posed**: at a 20 mm gap the search
refuses, correctly, because both ends of its bracket are on the same side — the ion at 6 mm
of convergence **strikes an electrode at 69.5 µs** rather than reversing. There is a maximum
useful convergence set by the gap and the beam's transverse extent, and past it the mirrors
close on the beam before the drift can turn around.

**So the remaining 6.3x is most likely in the electrode depths `d1..d4`** — the parameters no
paper states, and the ones this model exists to solve for.

### Against the published instrument

> **Superseded** — the gap is now 1.33x, not 57x, and the cause was the tilt axis rather than the electrode depths. See *Against the published spacer, reconciled to 1.33x*.


Not reconciled, and the gap is in the right direction. The instrument uses **200 µm** at
about 2° with **24 oscillations**; this skeleton needs **11.4 mm** at 2° with **4**. More
reflections need less convergence each, which accounts for some of it — the published device
packs 0.072 oscillations per mm of drift against this skeleton's 0.024 — but not a factor of
57. The rest is in the guesses: the electrode depths `d1..d4` are free parameters and the
board gap is assumed. **That is the inverse problem this model exists to pose**, and it is
now a study rather than a hand-edited file.

### The injection angle does not give 24 oscillations, and that is the point

At 3.5% the ion crosses the 350 mm drift in **3.77 oscillations**, not 24. Getting 24 over a
310–360 mm drift needs `sinθ ≈ 0.011` one-way, or ≈0.022 if the drift **reverses** and comes
back. The published prism angle is ~2° (0.035), which is consistent with the reversing case.

**The reversal is what the mirror convergence provides**, and it is not modelled yet. So the
oscillation count is the first real test of the convergence — see step 3 of §9.

---

## 4. Why the drift faces must be mirrors

The skeleton's stripe electrodes span the **full domain in z**, and the domain faces were
grounded. A grounded domain boundary **is a third electrode** — this project already
documented that for the parallel-plate example, where it was worth 3 orders of magnitude —
and here the electrodes at ±6 kV collide with a grounded wall they touch.

**The symptom:** at a 3.5% injection angle the ion should drift +z at 1375 m/s. Measured
over 5 µs it went **20 mm → 17.6 mm — backwards**.

**The gap:** the 3-D solver has always supported Neumann faces; **no document could ask for
one**. The 2-D path has `rightEdge`; the 3-D path had nothing. Same shape as several
defects already recorded here — a capability named in one place and unreachable from the
format.

Now declarable, and it is the physically right answer: stripe electrodes running along the
drift make the field independent of z, so those faces are **mirrors**, not walls.

```json
"solve3d": {
  "lowerZEdge": "neumann",
  "upperZEdge": "neumann",
  ...
}
```

Dirichlet stays the default — a grounded box is right for a device in a housing, and is the
safe default. Neumann is also *cheaper*: a domain that must contain end fields has to be
longer than the region that matters.

---

## 5. Costs, measured on this machine — read before planning

### Build Release. It is 3.27× faster.

2.16 s against 7.06 s on the shipped C-trap. **Every timing in the session notes is Debug
unless it says otherwise.**

```bash
dotnet build -c Release
```

### The mesh is a step function of cell size

Each axis rounds its interval count **up to a power of two**, so the node count is the
product of three roundings. At the Astral's aspect ratio:

| requested | mesh actually built | nodes | memory |
| --- | --- | --- | --- |
| 2.0 mm | 1.24 × 1.50 × 1.37 mm | 4.4 M | 199 MiB |
| 1.5 mm | 1.24 × 1.50 × 1.37 mm | 4.4 M | 199 MiB |
| 1.0 mm | 0.62 × 0.75 × 0.68 mm | 34.2 M | 1.6 GiB |
| 0.5 mm | 0.31 × 0.38 × 0.34 mm | **271 M** | **12.4 GiB** |

**1.5 mm and 2.0 mm give the identical mesh; 1.0 mm costs 7.9× more.** `einzel estimate`
now reports the achieved spacing and names the next cheaper request. Trust it over
intuition — a rule of thumb suggested 1.24 mm, which lands exactly on the boundary and
produces the identical mesh.

**Plan the memory.** 0.5 mm needs 12.4 GiB for one field. Check the target machine's RAM
before committing to a resolution.

### Always estimate first

```bash
einzel estimate models/astral-3d.json          # a model
einzel estimate studies/lengths.json           # a whole study, with the evaluation count
```

Accuracy, measured: **0.89× of the computation**, 0.76× of wall clock — the difference is
process start, which it excludes and says so. On the 3-D skeleton it read 3348 s against an
actual 2265 s (1.48× over, the safe direction). It self-calibrates, so it will report the
new machine's speed and the Release speed-up without being told.

### The solve dominates, and refining makes it worse

**Corrected.** An earlier reading of this page said the flight dominated the solve about
20:1. That was measured on the **broken** skeleton, whose ion escaped the analyser and
coasted for 20,000,000 steps. With a working model:

| 4 mm cell, Release | | |
| --- | --- | --- |
| solve | **5.298 s** | **94.3%** |
| flight | 0.321 s | 5.7% — 16,012 steps at 20 µs each |

And the gap widens with refinement: **node count goes as 1/cell³ while the step count goes
as 1/cell**, because the step is capped by the cell size. Halving the cell is ~8× the solve
and ~2× the flight.

The solve is healthy, not pathological — 3 levels (limited by the thin 17-node y axis), 13
cycles, convergence factor 0.20, about 52 M node-updates/s on one core. It is slow because
there is a lot of it.

**A study inherits this.** One evaluation is `solve + members × flight`; at 4 mm with nine
members that is 5.3 + 2.9 s, so the solve is 65% — and at 1 mm it is ~97%.

---

## 6. A faster machine helps less than you would expect — read this

**Einzel is single-threaded throughout.** No `Parallel.For`, no `Vector<T>`, no ILGPU
anywhere in `Einzel.Fields` or `Einzel.Transport`. `Einzel.Compute` does not exist. CMP-1
and PERF-5 are both "Not built".

So a 32-core machine runs one core, and **clock speed is the only thing that helps a single
run.**

### The way to use a big machine today is process-level sharding

A study is embarrassingly parallel across evaluations, and the CLI is the seam. Split a
scan into N sub-ranges, run N processes, merge:

```bash
# 240 points over d4, sharded 12 ways
for i in $(seq 0 11); do
  python - "$i" <<'PY' > "studies/shard-$i.json"
import json, sys
i = int(sys.argv[1]); lo, hi, n = 30.0, 340.0, 240
per = n // 12
a = lo + (hi - lo) * (i * per) / (n - 1)
b = lo + (hi - lo) * ((i + 1) * per - 1) / (n - 1)
json.dump({"schemaVersion": "0.1", "name": f"d4-{i}", "model": "../models/astral-3d.json",
           "figureOfMerit": "resolvingPower", "ions": 9,
           "scan": {"parameter": "d4", "from": a, "to": b, "unit": "mm", "points": per}},
          sys.stdout, indent=2)
PY
done

for i in $(seq 0 11); do einzel scan "studies/shard-$i.json" --json > "results/shard-$i.json" & done
wait
```

Each shard writes its own manifest (PRJ-3), so the merged result is regenerable.

**Caveat:** each process solves the field independently, so sharding multiplies memory by
the shard count. At 34 M nodes (1.6 GiB) twelve shards need ~20 GiB. At 4.4 M nodes
(199 MiB) they need 2.4 GiB. **This is another reason to sit at the cheap side of the mesh
cliff.**

**An optimisation cannot be sharded this way** — Nelder–Mead and CMA-ES are sequential.
Shard *across starting points* instead and take the best, which is a restart strategy rather
than parallelism.

### If you want real parallelism, in priority order

1. ~~**Evaluation-level parallelism in the study drivers.**~~ **Built, and it gives about
   5x rather than the ~14x this page first predicted.** `ParameterScan.Run` and
   `ToleranceStudy.Run` take a `maxParallelism`, a study file declares it, and results are
   bit-identical at any setting. What the prediction got wrong is measured below.
2. **A multi-threaded red-black smoother.** Red-black Gauss–Seidel is the textbook parallel
   case — every node of one colour is independent of the others. This is what helps a
   *single* solve, which evaluation parallelism cannot. **But the measurement below says to
   expect little**: the solve is already bandwidth-saturated at eight threads, and threading
   *inside* one solve competes for the same bandwidth rather than adding any.
3. **GPU (ILGPU) last.** A real project with genuine numerics risk, and TST-1 requires the
   scalar reference implementation be kept and never allowed to rot. A GPU has its *own*
   memory bandwidth, which — given the finding below — is the one thing that would actually
   lift the ceiling.

**1 and 2 compete for the same cores** — you cannot multiply them. For a study, 1 is
strictly better; for one big solve, 2 is the only option.

### What parallelism actually bought, measured

Two ladders in process, on this 8-core / 16-thread i9-9900K, so the CLI's cold start does
not swamp a short study:

| DOP | solve-bound (32-point mirror scan) | CPU-bound control (no solve) |
| --- | --- | --- |
| 1 | 1.00x | 1.00x |
| 2 | 1.57x | 2.06x |
| 4 | 2.53x | 3.93x |
| 8 | **5.25x** | 3.92x |
| 16 | 4.75x — *worse than 8* | **6.74x** |

**The parallel machinery is fine; the solve is memory-bandwidth bound.** The CPU-bound
control — same driver, same evaluator shape, arithmetic instead of a solve — reaches 6.74x
and *benefits* from hyperthreading. The solve-bound ladder peaks at the eight physical cores
and then loses ground, which is what a stencil sweep does when the memory bus is already
saturated: the extra threads add no bandwidth and cost cache.

**So plan on about 5x, and do not expect the 16 logical cores to help.** A 240-evaluation
Astral search is a fifth of its sequential time, not a fourteenth.

**And a measurement error worth not repeating.** This page first recorded 12.8x, from
comparing a *Debug* sequential baseline against a *Release* parallel run. Release is 3.27x
faster on its own, so almost all of the apparent speedup was the build. Compare like with
like: same binary, same study file, only the degree of parallelism moving.

---

## 7. What is not modelled

Named so nobody rediscovers them as bugs:

- **Ion foil electrodes.** Mounted between the mirrors, above and below the ion path, at a
  tunable 0 to −20 V. **Not in the model, and the paper says outright that this is why the
  drift does not reverse here.**

  Stewart et al., *Anal. Chem.* 2023 (doi:10.1021/acs.analchem.3c02856) states that the
  drift is eventually reversed by a returning electrostatic potential formed by the mirror
  tilt *as well as* refraction on the ion foil. So reversal has two contributors and this
  model has one — for the sufficient reason that mirror convergence is the only thing in it
  acting along the drift.

  **How much is missing is calculable from the paper's own numbers**, and the conclusion does
  not rest on this project's guessed geometry. The paper gives an inclination of about 2°,
  set by the second prism after the first reflection, and a drift decelerating to rest over
  12–13 oscillations. Decelerating a 2° drift to rest in 12.5 oscillations by mirror tilt
  alone requires:

  | | convergence needed |
  | --- | --- |
  | at the measured impulse efficiency η = 0.578 | **0.846 mm** |
  | in the ideal specular limit, η = 1 | **0.489 mm** |
  | **published spacer** | **0.200 mm** |

> **Superseded** — the foil shape here was inferred from a low-resolution figure and is
> wrong in four ways, and the conclusion that the foil supplies most of the returning
> impulse does not survive measuring the well it produces. See §11.

  **Even the specular upper bound is 2.4× short**, so mirror tilt alone provably cannot
  reverse this drift at the published parameters and the foil is doing the majority of the
  work: between about **59% and 76%** of the returning impulse, with the tilt supplying the
  rest.

  Its other stated jobs — compensating the temporal aberration the mirror asymmetry induces,
  improving the spatial focus at the detector, and absorbing mechanical misalignment of the
  mirrors — are the ones this document previously recorded, and they are real but are not
  the load-bearing omission. The load-bearing one is that it helps turn the ions round.

  **The shape is constrained by a measured design curve — and by how much that curve
  depends on what surrounds the foil.** On-axis penetration is a local, essentially
  two-dimensional property: at any given z the cross-section is two plates of half-width w
  at gap g, long in the drift direction. Measuring it as a 2-D problem converges in seconds
  where the 68 M-node volume solve did not converge at all, and moved the answer by a third:

  | cell | w/gap 0.5 | w/gap 1.0 | w/gap 2.0 |
  | --- | --- | --- | --- |
  | 2.00 mm | 85.9% | 93.5% | 98.6% |
  | 0.25 mm | 85.6% | 93.6% | 98.7% |

  Converged, and the full curve at an 8 mm gap runs **72.2% at w/gap 0.125** through 85.5%
  at 0.5 to **99.9% at w/gap 4** — so the foil saturates as a *level* by about four gaps
  wide, which is why the first parameterisation at w/gap 42 produced nothing at all.

  **The swing, which is what the physics needs, is not a property of the foil alone.**
  Between a wide end at w/gap 2 and a narrow end at w/gap 0.375, moving the grounded wall
  that stands in for the surroundings:

  | half-box | swing |
  | --- | --- |
  | 20 mm | 4.759 V |
  | 40 mm | 3.308 V |
  | 80 mm | 2.538 V |
  | 160 mm | **2.250 V** |

  It halves as the surroundings recede. **The required 2.9–3.7 V sits inside that range**, so
  a foil of entirely plausible size can supply the missing returning impulse — but which
  point on it is the real one is set by the analyser's own structure, not by a box chosen for
  the study. The mirrors are 170 mm away in x and there is no grounded surface near the foil,
  which argues for the open end of the range and therefore for a narrower far end than
  w/gap 0.375.

  **So the 2-D study picks candidates and cannot settle the value.** That has to come from
  the foil solved inside the full analyser, which is what the template now carries. The
  earlier version of this section quoted 80.6% / 98.3% / 3.54 V from a 4 mm cell in the
  volume solve; those were **not mesh-converged** and were 36 per cent high. They are
  withdrawn.

  **The published figures settle the shape class, and it is not a wedge.** The analyser
  panel shows the foil as **leaf shapes — broad at mid-drift and tapering to points at both
  ends of it.** That is a different thing from a monotonic taper, and the difference is the
  whole mechanism:

  | shape | on-axis potential | what it does |
  | --- | --- | --- |
  | monotonic wedge | a ramp | a constant returning force, and no focusing |
  | **leaf, widest in the middle** | **a well centred in the drift** | **focuses the drift and returns it** |

  Wider means more negative on axis, so a leaf puts a potential *well* at mid-drift and a
  positive ion displaced either way along the drift is pulled back toward the centre. One
  shape therefore does both jobs the sources describe — the focusing Stewart names, and the
  "refraction on the ion foil" the paper credits for part of the reversal — where a wedge
  can only do the second and does it without focusing.

  **It also reframes the flight.** "Decelerating over the first 12–13 oscillations and
  drifting back over the following 12–13" reads naturally as **half a period of slow axial
  oscillation in the foil's well**, rather than as a linear ramp that happens to run out in
  the right place. That is a prediction rather than a restatement: the well's axial period
  should be about twice the flight time, and measuring it would confirm the foil model
  instead of merely fitting it.

  The template's profile is a parabola peaking at mid-drift — `foilMidHalfWidth` and
  `foilEndHalfWidth`, needing only multiplication and so no new grammar. Verified from the
  mesh as symmetric, 4.57 mm at the first and last slice centres rising to 15.93 mm at the
  middle, which is the parabola evaluated exactly at those centres rather than an
  approximation to it.

  **Two electrodes, where the figures show four.** Two is the minimum that produces the well,
  and going to four is a parameter change rather than a structural one; there is no
  measurement yet asking for the other two.

  **What is still needed to build it**: the electrode shapes. The paper says the shapes are
  precise and purpose-made and does not give them, which leaves them where `d1..d4` are — a
  free parameter of the inverse problem rather than a number to look up.

- **Drift deceleration and reversal.** A consequence of the convergence, which is now
  expressible but not yet exercised in a flying model.
- **Prism deflectors** setting the ~2° inclination. The skeleton fakes this with an
  injection angle.
- **The einzel lenses** in the injection path.
- ~~**`reflectAboutX` for `solve3d`.**~~ **Built, and it delivers the factor of two.** On the
  skeleton: 257 x 17 x 129 = 0.56 M nodes in 4.87 s whole, against 129 x 17 x 129 = 0.28 M in
  **2.57 s** halved — **1.90x** — at the *same* 13 cycles and the same 0.1997 convergence
  factor, so the half is the same problem rather than an easier one. The flight is identical:
  **120.0580 us** both ways, landing at x = 389.07, z = 340.00 both ways.

  Declare the mid-plane face Neumann alongside it, keep only the electrodes in the solved
  half, and the reflection supplies the rest:

  ```json
  "maxX":        { "value": 312.5, "unit": "mm" },
  "upperXEdge":  "neumann",
  "reflectAboutX": { "value": 312.5, "unit": "mm" }
  ```

  **It also uncovered a defect worth knowing about** — see the next entry.
- **`MirrorPair.Fly` cannot express an asymmetric track at all.** It computes one period and
  multiplies, so any resolving power it reports for a *symmetric* pair is arithmetic, flat
  to the digit across oscillation counts. Do not read it as a 3-D result.

---

## 7a. A volume field used to invent a field outside its own box

**Fixed, and it changes how an earlier observation on this page should be read.** A
`SolvedField3D` called its tricubic unconditionally, with no bounds check, and a tricubic
asked for a point it was never fitted over continues the cubic rather than declining. On a
20 mm box holding one plate at 100 V it reported **−486,643 V at 8.1 MV/m** 180 mm outside —
four orders past the applied potential.

**So "the ion escaped to x = 4643 mm and then coasted" was wrong.** It was not coasting; it
was being accelerated by a field nobody declared. Expect escaped ions to behave differently
now — they stop being accelerated at the domain wall, so a geometry that used to fling them
away will now let them drift.

The plane path has always returned zero outside its grid, which is also what makes
superposing a half with its mirror a union rather than a sum. Nothing in the suite moved
when this was fixed, so no published number depended on it.

## 8. Traps already paid for — do not re-pay them

- **A grounded domain boundary is a third electrode.** §4. Cost 3 orders of magnitude in
  the parallel-plate example and the backwards drift here.
- **The gap in a closed form is between the facing surfaces.** Putting a 1 mm plate's
  *centre* on the gap boundary makes a 10 mm gap into 9 mm — an 11.1% error that looks like
  a solver problem and is not.
- **Do not place a conductor face exactly on a cell boundary** when the quantity of interest
  is a small geometric perturbation. It makes the response affine rather than proportional,
  with an offset worth ~17 µm of convergence here. A quarter-cell offset removes it.
- **`OverBox` rounds each axis to a power of two independently**, so asking for 24 and
  asking for 32 gives the same mesh. A refinement study that does not know this reports an
  observed order of exactly zero.
- **A wall-clock comparison on a loaded machine measures the load.** A scan timed at 50.6 s
  against 31.0 s minutes earlier — the test suite was running. Run timings on an idle box.
- **Two points cannot tell a slope from an offset.** Use a ladder when checking that a
  response is proportional to a perturbation; the *direction* the ratio drifts names the
  cause.

---

## 9. Suggested order of work

**The live list is in *Where this stands* at the top of this page.** What follows is the
original plan with its outcomes, kept because two of its items were completed in a way that
changed the plan and one of them is written here incorrectly.

1. ~~**Make one ion complete one flight**~~ — done, §3. 120.058 µs against a predicted
   120.1, drift exact, 16,012 steps.
2. **Refine the mesh and watch the energy drift.** Still open. At a 4 mm cell it is 2.16e-6,
   just over ACC-4's 1e-6 budget. It should fall with refinement; if it does not, that is a
   finding rather than a nuisance. Sit at the cheap side of the mesh cliff (§5) and confirm
   the flight time is unchanged.
3. ~~**Add the convergence and show the drift decelerates and reverses**~~ — done, §3.
   **Note the error preserved in the original wording: it said "tilted about x".** That is
   exactly the bug that made the model converge the boards rather than the mirrors, so the
   plan itself carried it. Tilt is about **y**.

   It did *not* raise the oscillation count toward 24. At 2° the ion still crosses the drift
   in under four oscillations, because reversal at the published 200 µm spacer needs a
   restoring force this model does not have — the first of the two open numbers at the top
   of this page.
4. **The inverse problem: fit `d1..d4`.** Still the next substantial piece, and now better
   posed — fit against the *measured reversal distance* rather than only the 4000 ± 100 V
   window, since reversal is where the model is 1.3–4× out and the energy window is already
   reproduced (R = 2,600 on energy spread alone).
5. ~~**Ion foil**~~ — built, and then rebuilt from a pixel measurement of the published
   figure. Its shape is no longer a free parameter. **Prisms and the rest of the injection
   optics are still not modelled** (§7).

Estimate before each of 2 and 4, and read the basis line rather than only the number.

---

## 10. One more trap, from writing this page

A probe script read `flightTimeSeconds` from `--json` and printed **0.000 µs** for a flight
of 120.058. There is no such key: `flightTime` is a **GRD-1 envelope** — value, unit,
uncertainty, evidence and warnings — and `dict.get(k) or 0` turned the miss into a plausible
zero.

The engine was right and the reader was wrong, which is the failure mode GRD-1 exists to
prevent being *reintroduced by the consumer*. When scripting against `--json`, read the
envelope:

```python
d["flightTime"]["value"], d["flightTime"]["unit"]
d["flightTime"]["warnings"]        # do not drop these
```

The warnings are the point. The 4 mm run carries a non-suppressible
`ENERGY_DRIFT_EXCEEDS_BUDGET`, and a script that reads only `["value"]` would report a
flight time the engine itself has qualified.

## 11. The foil shape, measured off the published schematic

The shape carried above — two leaves centred on the axis, a parabola in width peaking at
mid-drift — was inferred from a low-resolution figure and is wrong in four separate ways.
The figure is drawn to scale and the shape can be *measured* from it rather than guessed.

**Source:** Anal. Chem. 2023;95(42):15656–15664, figure 1, Astral analyser panel
(<https://doi.org/10.1021/acs.analchem.3c02856>), image `ac3c02856_0001.jpg`, 666 × 358.
Pixel classification of the pale-blue foil regions against the white gaps.

**The scale is 1.92 mm/px, established three independent ways** and not assumed:

| feature | pixels | implied | template says |
| --- | --- | --- | --- |
| panel height | 323 | 620 mm | `capToCap` 625 mm |
| mirror mouth to mirror mouth | 190 | 365 mm | 365 mm |
| mirror stack depth | 60 | 115 mm | `d4` 130 mm |
| panel width | 173 | 332 mm | paper's mean drift 335 mm |

The mid-plane-to-mouth distance measures **182 mm** and the template computes
`midPlane - mouth` = **182.5 mm**, from geometry that was never fitted to this figure. That
agreement is what licenses placing the foil in absolute millimetres.

**What the figure shows.** Each plate has a **straight outer edge** — flat to within one
pixel over the whole drift, at 146 mm from the mid-plane — and a **contoured inner edge**
running between 117 mm and 94 mm. The contour is **non-monotone**, which no parabola is:

| drift fraction | inner edge, of the 182 mm reach | plate radial extent |
| --- | --- | --- |
| 0.245 | 0.579 | 30 mm |
| **0.41** | **0.641** — furthest out, gap widest | 27 mm |
| **0.67** | **0.516** — closest in, gap narrowest | 50 mm |
| 0.99 | 0.621 | 31 mm |

A single cosine of wavelength `2 × (0.67 − 0.41)` = 0.52 of the drift fits all four points
to ±0.02, which is why the template carries `foilInnerMid`, `foilInnerAmplitude`,
`foilThinAt` and `foilThickAt` rather than a table.

**Four plates, and the count is derivable rather than counted off the picture.** The two
plates straddle the mid-plane in the mirror-oscillation direction, and the ions oscillate
straight through those *x* positions — so each must be duplicated above and below the ion
plane for the packet to pass between them. Two × two.

**The dark teal shape between them is not an electrode.** It is the ion envelope: it is
pointed at the left where the injector prism is, which no electrode would be, and the
`Ion Foil` leader line in the figure lands on the pale-blue band and not on it. Reading it
as a third and fourth electrode is what produced the axis-centred leaf shape.

**Mounted flush with the boards.** `foilGap` defaults to `halfGap`, making the foil a
shaped conductive region on the inner face of each board rather than a separate aperture in
the flight path — the plausible construction for a printed-circuit analyser, and what the
projection would look like either way. Measured cost of moving it there from an 8 mm
half-gap: 15% of the well depth and **no change in the well's shape**.

### What the shape does, by differencing two solves

`foilVolts` at −20 V minus `foilVolts` at 0, so the mirror field cancels and what is left is
the foil's own contribution. Between the plates it reaches **−19.9 V of −20 applied** —
essentially complete penetration, as the plate extent over the gap predicts.

Averaged across the free-flight gap at each drift position (a crude stand-in for the
cycle average, which would weight by 1/v and so weight the slow turning region more):

| z, mm | 20 | 80 | **140** | 200 | **240** | 300 | 340 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| flush, V | −0.13 | −3.04 | **−5.29** | −6.73 | **−7.31** | −5.69 | −3.97 |

**The well's shallow point is at 140 mm and its deep point at 240 mm — 41% and 67% of the
drift, exactly where the measured contour puts them.** That correspondence is the check
that the geometry is producing the field the figure describes rather than something
incidental to having put metal in the box.

### A premise this corrects

The section above concludes that mirror tilt alone cannot reverse the drift at the
published spacer, and that *the foil supplies 59–76% of the returning impulse*. The first
half stands. **The second half is wrong, and measuring the well is what shows it.**

A potential that reverses a drift must *rise monotonically* over the region where the
reversal happens, by more than the 4.872 eV of drift energy. The measured contour produces
no such ramp in either sign of bias:

- **Negative bias** (a well): the ion is accelerated outward over the first 240 mm and
  decelerated only over the last 100, arriving at 340 mm with 3.97 eV *more* drift energy
  than it started with. Net anti-reversal.
- **Positive bias** (a hill peaking at 240 mm): the ion does reverse, but on the rising
  flank — at φ = 4.872 V, which the table puts at **z ≈ 105 mm**, against a published
  310–360 mm. And a hill is a *defocusing* lens in z, an unstable equilibrium at its top.

**A well centred at two-thirds of the drift is a lens, not a decelerator** - and here the
two papers pull in different directions. [A] says the drift "is eventually reversed by a
returning electrostatic potential formed by mirror tilt **as well as refraction on the ion
foil**", naming the foil as a contributor to reversal. [B] describes what the well does: the convergence "reduced the drift rate of each ion, and its drift was
eventually stopped at a distance L", while "the optimized convergence of the mirrors **and**
a set of specially shaped electrodes, referred to as ion foil, cause the drift **spread** to
reduce on the way back from the drift reversal point" (J. Mass Spectrom. 2024;59(4):e5006,
<https://doi.org/10.1002/jms.5006>). Under [B]'s reading: two mechanisms, two jobs - convergence stops the drift, the foil
focuses it - and the 2.9–3.7 V "required swing" derived above is the answer to a question
the foil is not answering. Under [A]'s reading the foil *does* help reverse, and the
measured contour then cannot be the whole story: either the four plates are biased
independently, or the contour's drift-fraction calibration is off by more than its stated
±5%, or [A]'s "refraction" is loose language for [B]'s refocusing. **Unresolved**, and
recorded in §1 under *What [A] says, verbatim*.

**So the reversal deficit is now unexplained, and that is the finding to carry forward.**
Mirror tilt at the published 200 µm spacer is 2.4× short even in the specular limit, and the
foil cannot make it up. What remains are the free parameters: `d1..d4` are guesses, the
board gap is assumed, and the injection angle in the template (1.28°) came from a ballistic
oscillation count while the paper states about 2°. Fitting those against the measured
reversal distance is the next study, and it is now a well-posed one because the foil's
geometry is no longer among the unknowns.

**Not yet measured:** that the well actually focuses a z-spread packet, and with what focal
length. The restoring force has the right sign by inspection of the table; its strength
against a 50 mm spread over the return leg is a flight, not a solve.

### The raw measurement, so it need not be repeated

Everything above is derived from these numbers. Recorded in full because the figure is
low-resolution and someone should be able to disagree with the reading without re-doing it.

**Image.** `ac3c02856_0001.jpg`, 666 × 358, the single figure asset of the PMC record for
[A]. The Astral panel occupies roughly x 463-622, y 12-330.

**Classifier.** A pixel is foil if `b>222 && g>200 && r>145 && r<228 && b>=g && g>=r` -
the pale blue fill, distinguished from the white gaps, the dark blue mirror stripes, the
grey housings and the teal ion envelope. The dashed red trajectory crosses both bands and
punches holes in the runs; columns where it did are dropped rather than interpolated, which
is why the sampling below is uneven.

**Vertical structure**, median colour over drift columns x 540-600:

| feature | y |
| --- | --- |
| upper mirror stack | 29-89 |
| white gap | 90-105 |
| **upper foil band** | **108-135** |
| white gap | 137-154 |
| ion envelope (teal and grey) | 155-198 |
| white gap | 199-226 |
| **lower foil band** | **229-254** |
| white gap | 257-277 |
| lower mirror stack | 279-325 |

So the mirror mouths are at y 89 and 279, the mid-plane at **y = 184**, and the free-flight
gap is **190 px**. Against a template `capToCap - 2 * mouth` of 365 mm that is
**1.92 mm/px**, which the panel height (323 px, 620 mm against `capToCap` 625) and the panel
width (173 px, 332 mm against the paper's mean drift of 335) both corroborate.

**The contour.** Outer edges are flat: upper 108-109, lower 253-255, across every column.
Inner edges, by drift column x:

| x | 502 | 510 | 514 | 518 | 522 | 526 | 534 | 538 | 542 | 546 | 550 | 554 | 562 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| upper | 129 | 126 | 125 | 124 | **123** | **123** | **123** | 125 | 126 | 128 | 130 | 132 | 134 |
| lower | 234 | - | - | 238 | **239** | **239** | **242** | 235 | 236 | 234 | - | 231 | 229 |

| x | 566 | 574 | 578 | 582 | 586 | 590 | 594 | 598 | 602 | 606 | 610 | 618 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| upper | **135** | **135** | 134 | 134 | 133 | 132 | 131 | 130 | 129 | 128 | 127 | 125 |
| lower | **228** | **228** | 229 | 229 | 230 | 231 | - | 233 | - | - | 236 | 239 |

The two bands are mirror images about y = 184 to within a pixel throughout, which is the
internal check that the classifier is finding electrode edges and not trajectory artefacts.

**Reduction.** Distance from the mid-plane, in units of the 95 px mid-plane-to-mouth reach,
at the four turning points of the contour:

| x | drift fraction | upper band | fraction of reach |
| --- | --- | --- | --- |
| 502 | 0.245 | 55 px | 0.579 |
| 528 | **0.41** | 61 px | **0.641** - furthest out, gap widest |
| 570 | **0.67** | 49 px | **0.516** - closest in, gap narrowest |
| 618 | 0.99 | 59 px | 0.621 |

Drift fractions are `(x - 463) / 159` and carry about ±5%, because the panel's right border
did not threshold cleanly and was taken as x = 622 from the rendered view.

A cosine `mid + amp * cos(pi * (s - thinAt) / (thickAt - thinAt))` with `mid = 0.578`,
`amp = 0.063`, `thinAt = 0.41`, `thickAt = 0.67` reproduces all four to ±0.024, worst at the
0.245 end. Those are exactly the template's `foilInnerMid`, `foilInnerAmplitude`,
`foilThinAt` and `foilThickAt`. Its wavelength, `2 * (0.67 - 0.41)` = **0.52 of the drift**,
is a physical statement rather than a fitting artefact and is the number to challenge first
if the shape turns out wrong.

## 12. The tilt was invisible to the solver, and its sign was backwards

**Every convergence-dependent number in this document was measured on a mirror tilted by
about eight per cent of what was declared, in the wrong direction.** Found on 2026-09-01 by
asking the simplest question not yet asked: what z-kick does one reflection off the tilted
mirror deliver, against the specular `2·α·v`?

### The measurement

An ion launched with zero injection angle, one reflection, and the drift displacement read
at a detector behind the launch point. For any rigidly tilted 1-D mirror `v_z(t) = α(V −
v_x(t))` pointwise, so integrating over the flight gives **`Δz = α·V·T` exactly** — no
velocity needed, and an analytic control has to return 1.

| mirror | conv | cell | efficiency |
| --- | --- | --- | --- |
| analytic tilted half-space | 3.0 / 30 mm | — | **1.0025 / 1.0025** (the ¼% is the 1 mm launch offset) |
| solved Astral, strips abutting | 0.3 / 1.0 / 3.0 mm | 4 mm | **0.447 / 0.447 / 0.447** |
| solved Astral, strips abutting | 0.3 mm | 2 mm | 0.303 |
| solved Astral, strips abutting | 3.0 mm | 2 mm | **−2.82** — wrong sign |
| **solved Astral, 3 mm gaps between strips** | 0.3 mm | 4 mm | **1.045** |

Linear in the tilt at fixed mesh, so it is a property of the *solve* and not a nonlinearity;
scrambled rather than improved by refinement, so it is not a resolution trend either. The
grounded foil alone kicks the ion −0.082 mm (4 mm) / −0.065 mm (2 mm) and is subtracted as
the control.

### The field itself, with no ion

`E_z/E_x` inside the far mirror at the strips' rotation centre, where a rigid tilt gives
exactly α:

| x | strip | fraction of α, 4 mm | fraction, 2 mm |
| --- | --- | --- | --- |
| 529 | U2 | **0.109** (0.3 mm) / 0.084 (3.0 mm) | −0.650 |
| 559 | U3 | **0.014** / 0.009 | −1.420 |

And at the U2/U3 edge 1.25 mm below the board surface, where a 1.5 mm slide of the edge
across the drift would move the potential by ~1600 V: it moves **0.57 V**. The boundary is
not moving.

**Not the Neumann z-faces.** Moving them four times further away (`zPad` 150 → 600) gives
0.111 / 0.014 against 0.109 / 0.014. The template's "known wrong" label on those faces is
not where this lives.

### The mechanism

`Electrode3D.ToLocal` is correct, and both solver paths — the `Contains` mask and the
`FirstEntry` cut links — go through it. What a tilt about y *moves* is the problem. The
mirror strips abut on a flat board: rotating them about y leaves the board surface at
y = 20 exactly where it was and slides only the **metal-to-metal edges** between strips.
Cut cells resolve a metal-to-vacuum surface to a thousandth of a cell; an edge with
Dirichlet nodes on both sides has **no cut-cell representation at all** and is rasterised at
node resolution. At 0.3 mm of convergence the edges move 0.075 mm — 3% of a cell — and the
solver cannot see it. The ~8% that leaks through is the one metal-to-vacuum face that does
tilt: the mouth. At a 2 mm cell the 0.75 mm displacement (3.0 mm conv) crosses nodes at
arbitrary z, and the field is a staircase with the wrong sign.

**This is FLD-1's staircase in a new guise.** The Stage 3 fix — Shortley–Weller cut cells —
covered the metal-to-vacuum boundary, which is the only kind a rasterised electrode has.
Abutting electrodes have a second kind, and the tilt ladder that reported "proportional to a
thousandth of a cell" was run on parallel plates, whose tilted faces are metal-to-vacuum.

**Established by the discriminating test**, two strips at ±1000 V on a board, tilted by the
same α, sampled 10 mm off the board:

| | E_z/E_x over α |
| --- | --- |
| abutting | 0.093 / 0.055 / 0.079 |
| **one vacuum cell between them** | **−1.104 / −1.093 / −1.109** |
| gapped, untilted | 0.000 / 0.000 / 0.000 |

### And the sign

Once the tilt is visible, the kick is **positive** — toward +z, *accelerating* a drift that
goes +z. The analytic control with its face tilted toward the mid-plane gives a negative
kick. Working `ToLocal` through for the template's `near = −mirrorTilt, far = +mirrorTilt`:
the near mouth face sits at `x = maxX − (z − z_c)·α` and the far at `x = minX + (z − z_c)·α`,
so **both mouths recede from the mid-plane as z increases**. The declared geometry
*diverges* along the drift. The 8% artefact happened to carry the decelerating sign, which
is what made every "convergence works" demonstration look like convergence.

**Confirmed by swapping the sign on the gapped geometry: efficiency −1.045**, the exact mirror
of +1.045, and the sign the analytic control had. **And on the shipped template as corrected
(sign swapped, 3 mm gaps, foil flush with the boards): −0.983** — the mechanism is real in
the file that ships.

**A side finding from that last run.** Its zero-tilt control kicked the ion **−0.537 mm** per
reflection, against −0.082 with the foil at an 8 mm half-gap. Nothing was tilted and the
foil was at 0 V: this is a *grounded* conductor, flush with the board and spanning z from
87 to 350 mm, pulling the mirror's fringe field asymmetrically about the launch point at
z = 175. It is worth about **0.6 mm of convergence** on its own. That is a real effect of a
real conductor — and it is plausibly what [A] means by "refraction on the ion foil"
contributing to the returning potential (§1). It is not yet separated from the tilt in any
measurement above, and every earlier probe's control subtracted a foil in a different
position; the drift-direction force the foil exerts by its *presence* deserves its own
measurement before the bias-dependent well of §11 is interpreted further.

### What it takes down

- **η = 0.578 was never a property of the mirror.** It is the fraction of the declared tilt
  the discretisation let through, with the wrong sign, plus a mesh-dependent staircase.
- The deceleration and reversal demonstrations in §3 were produced by that artefact. Every
  convergence quoted there (0.267 mm, 0.5397 mm, the 1.33×, the fourth-power law,
  `N = α·L/(η·c)`) is withdrawn. **The true gap to the published spacer is unmeasured.**
- The "specular mirror needs ~0.49 mm at 2°" arithmetic stands, since it uses no solve.

### How good the stopgap is, measured

Single-reflection efficiency against the specular `Δz = α·V·T`, on the corrected template
(sign fixed, 3 mm strip gaps), at a 4 mm cell. Zero injection angle, so the ion sits at the
strips' rotation centre and samples only the tilt's gradient. The foil at 0 V is subtracted
as a control (it kicks −0.537 mm on its own, of which more below).

| convergence | mirrors only | with the foil |
| --- | --- | --- |
| 0.10 mm | −0.900 | −0.957 |
| **0.20 mm — the published spacer** | **−0.903** | **−0.958** |
| 0.30 mm | −0.933 | −0.983 |
| 0.50 mm | −1.110 | −1.154 |
| 1.00 mm | −1.762 | −1.807 |
| 2.00 mm | −2.800 | −2.840 |

**Two separate things are visible, and only one of them matters at the operating point.**

**Flat and linear below ~0.3 mm, at 0.90 of specular.** Efficiency changes by 0.3% between
0.1 and 0.2 mm, so in the region the instrument actually uses the tilt is resolved and
under-delivers by a constant 10%. That is a systematic deficit, not staircase noise.

**Above 0.3 mm a spurious quadratic takes over.** Fitting the raw displacement gives
`Δz ≈ 0.747·c + 0.72·c²` mm, so the quadratic term equals the linear one at c = 1 mm. It
cannot be physics: a genuine second-order geometric term goes as α², and α at c = 1 mm is
1.4e-3, so matching the linear term would need a coefficient near 700. It is the
discretisation again, and it is **outside the operating point** — which is why the physics
below is worth doing on this build.

**The likely cause of both, not yet confirmed.** The gap is 3 mm against `hx` = 2.488 mm,
so 1.2 cells, and whether a *node* falls inside the gap depends on where the gap has slid
to — which under a tilt depends on z. A gap of at least two cells would always contain a
node. Against that, the gap itself removes 3 mm from each of three internal boundaries per
mirror, about 7% of the mirror's depth, which is a plausible source of a systematic ~10%
in its own right and would get **worse** with a wider gap. The two candidates therefore
predict opposite things about gap width, which makes it a clean experiment: sweep the gap
at a fixed convergence, and refine the cell at a fixed gap.

**Either way the shear is still the right destination**, because both candidates are
artefacts of representing a rigid rotation on a fixed grid, and a sheared 2-D solve has
neither.

### The fix, and what shipped

Three routes were possible. **The second was built.**

1. Vacuum gaps between strips, wide enough to resolve. Tried, and it is what produced the
   -0.57 to +3.54 spread above. Rejected.
2. **Solve each mirror as a two-dimensional cross-section and rotate the solved field.**
   Exact, because rotations commute with the Laplacian: if the inner field solves Laplace
   for some geometry, the rotated field solves it exactly for the rotated geometry. The
   anisotropy is *constructed* from the geometry rather than resolved by differencing, so
   it carries no discretisation error at all. **A shear would not do** - Laplace is not
   shear-invariant, and a shear of a mirror pair translates both mirrors the same way,
   which is no convergence.
3. Refine to 0.075 mm cells. Not available.

**Shipped as `RotatedField` plus a `tiltHalfTurns` on a `solved2d` element** (schema 0.8,
with `tiltCentreX` and `tiltCentreZ`). Measured:

| | Ez/Ex against tan(alpha) |
| --- | --- |
| rotated half-space vs one declared tilted | **0.000E+000** - bit-identical |
| anisotropy at the Astral's own tilt | worst error **5.4e-20** |
| the same, declared through a model document | worst error **1.7e-18** |

**A converging pair is two elements rotated oppositely**, each carrying one mirror at
potential with the other grounded - the ordinary basis decomposition
`phi = sum_k V_k psi_k`, and therefore exact rather than an approximation.
`astral-3d.json` is now exactly that: two `solved2d` cross-sections, 16 rectangles each,
no tilted geometry anywhere.

**Three things it bought beyond correctness.** The strip gaps are gone, because nothing is
rotated in the geometry and abutting strips cost nothing. `zPad` is gone for the mirrors,
because a cross-section is infinite along z by construction - so the template's "known
wrong: the drift faces are Neumann" caveat no longer applies to them. And a solve plus a
15 microsecond flight went from about **26 seconds to 1.4**.

**What it gives up, stated:** the ion foil cannot be in the mirrors' solve, because the
foil is not z-invariant and a cross-section cannot hold it. So a grounded foil's shadowing
of the mirror field - measured at -185 m/s per reflection, and 5.7x too strong to be the
published mechanism - is absent from this model. The foil's own field can still be
superposed as a third, three-dimensional element.

## 13. The drift deceleration has a closed form, and it forbids most of §3

Three facts fix the drift impulse of one reflection with nothing left over:

- the **speed** is conserved, because the ion returns to the potential it left;
- the component along **n̂ = (sin α, 0, cos α)** is conserved, because an electrostatic
  structure invariant under translation along n̂ conserves `v·n̂`, and a mirror rotated about
  y is invariant along exactly that;
- the component along the **mirror normal** reverses.

Solving those three gives `v_x = −V·cos 2α` and

> **Δv_z = V·sin(2α) per reflection, exactly.**

Both mirrors contribute the same sign — their tilts are opposite *and* the sense of their
`v_x` reversals is opposite, so the two minus signs cancel. **Nothing in the derivation
refers to the electrode potentials, depths, apertures or shapes.**

The familiar `2V·tan α` is the small-angle form and is short by `cos²α`. That was found by
the test rather than assumed: written against `2V·tan α` it failed at 0.999013 for
α = 0.0314 rad, which is `cos²α` to six figures. For the Astral's own tilt the two forms
differ by 8e-8.

**Measured against the integrator, in `RotatedMirrorDriftTests`:**

| tilt, half turns | V sin 2α | measured Δv_z | ratio |
| --- | --- | --- | --- |
| 9.0929e-5 — the Astral's 200 µm spacer | 22.447754 m/s | 22.447754 | **1.000000000** |
| 1.0e-3 | 246.869621 | 246.869621 | **1.000000000** |
| 1.0e-2 | 2467.088424 | 2467.088424 | **1.000000000** |

And the sharper half, since a magnitude right at one operating point proves little: an
**eightfold change in the mirror's field gradient**, from 20 to 160 kV/m, moves the turning
depth and the flight time by that factor and leaves the impulse at **22.447754 m/s in both
cases**. The impulse is a property of the tilt alone.

### Three consequences

**`d1..d4` cannot affect the drift deceleration, at all.** §3 reasons at length that the
electrode depths set an "impulse efficiency" η, that η = 0.578 is a property of the mirror,
and that η "is the figure of merit the depths move". **That is wrong in principle**, not
merely unmeasured: η is identically 1 for any rigidly tilted mirror. The plan to fit the
depths against the reversal distance is therefore void, and the *Next, in order* list has
been corrected.

**The 2.4× gap to the published instrument is not in the mirror geometry.** At the published
c = 0.2 mm and θ = 2°, the exact law gives 61 reflections to stop the drift and 618 mm of
drift distance, against a published 24–26 and 310–360 mm — the same factor 2.4 in both. No
choice of electrode design changes either number. Whatever supplies the missing
deceleration, it is not the mirrors, and [A] names the candidate: "mirror tilt **as well as
refraction on the ion foil**".

**And it gives a test with no free parameters**, which is what exposed the next finding.

### The solved 3-D mirror fails it by 3.5×

A 2×2 factorial at the template's own `zPad`, 2° injection, 300 µs, 4 mm cell, measuring
Δv_z per reflection directly from the trajectory:

| | Δv_z per reflection | of the invariant |
| --- | --- | --- |
| **c = 0, no foil — control** | **−0.00 m/s** | **0.000** |
| c = 0.2, no foil | −79.55 | **3.543** |
| c = 0, foil grounded | −185.27 | 8.252 |
| c = 0.2, foil grounded | −194.62 | 8.669 |

**The control is exactly zero** — v_z holds at 1371.2 m/s over 20 reflections and 411 mm of
drift, because that geometry really is z-invariant. So the integrator, the trajectory
export, the reflection counting and the launch are all sound, and the tilted rows are
measuring the solve.

**3.543 against a law that admits no free parameters**, on the same geometry whose
*single-reflection* efficiency measured 0.903. Two measurements of one quantity disagreeing
by 4× is the signature of a field that is not the field of a rigidly tilted structure. The
geometry itself is rigid — every strip's z-dependent face displacement is `(z − z_c)·tan α`
independent of its own centre, and the `1/cos α` stretch is 2e-6 mm — so this is entirely
discretisation.

**The two cases differ in whether the ion drifts.** The single-reflection probe sat at the
rotation centre with no drift and sampled one z; this one travels 91 mm in z while
reflecting, and samples the staircase the tilted boundary makes as it slides across nodes.

### What the foil would have to be, and what it is

The same factorial measures the foil, and at c = 0 nothing is tilted, so that row carries no
tilt artefact:

| per reflection, at θ = 2° | Δv_z | of the invariant |
| --- | --- | --- |
| reversal in 25 reflections requires | −54.8 m/s | 2.44 |
| mirror tilt, exact | −22.45 | 1.00 |
| **so the foil must supply** | **−32.4** | **1.44** |
| **foil as modelled, measured** | **−185.3** | **8.25** |

**The modelled foil is 5.7× too strong** — while grounded, so this is pure geometric
asymmetry rather than anything about its bias. It reverses the drift in 7 reflections
against a published 24–26.

**The likeliest cause is `zPad`, which was never a physical parameter.** The template pads
the mirrors 150 mm beyond the drift at each end so the ion does not see their ends, and the
foil spans only z = 87.5 to 350 mm. That leaves a 650 mm mirror with a 262 mm grounded patch
in the middle of it, and the patch's two edges are strong z-asymmetries. The real analyser
has no such padding. Raising `zPad` to 550 mm for one experiment made the foil force worse
(−185 → −247 m/s per reflection equivalent), which is the direction that hypothesis
predicts. **`zPad` must be treated as a modelling parameter with a measured effect, not as
numerical headroom.**

### On the shipped template, measured

| convergence | cell | V sin 2a | measured | ratio |
| --- | --- | --- | --- | --- |
| 0.2 mm | 0.5 mm | -22.45187 | -22.44928 | **0.999884** |
| 0.2 mm | 1.0 mm | -22.45187 | -22.44930 | **0.999885** |
| 0.2 mm | 0.25 mm | -22.45187 | -22.44928 | **0.999885** |
| 1.0 mm | 0.5 mm | -112.25925 | -112.24629 | **0.999884** |
| 3.0 mm | 0.5 mm | -336.77501 | -336.73614 | **0.999885** |

Against 3.543 and 0.903 for the same geometry solved with the tilt in three dimensions.

**Constant to six figures across a fourfold range of cell size and a fifteenfold range of
convergence**, which is the signature of an anisotropy that is constructed rather than
resolved - and which also says the residual 1.15e-4 is not discretisation.

**The residual is the superposition, and it is measured rather than guessed at.** Flying
the same ion through the far mirror's solve *alone* gives **0.9999983**. The two elements
each ground the other mirror at their own rotation, so the composite field is invariant
along neither n direction exactly. At 1.15e-4 of the deceleration it is far below the
uncertainty in `d1..d4`, and removing it would need a solve whose grounded set is rotated
one way and whose live set the other - which is not a thing a single cross-section can be.

Two hypotheses were checked and rejected on the way, both recorded because they were
plausible: the local potential at the mid-plane (0.0024 V, and using the trajectory's own
speed does not move the ratio) and mesh convergence (the ratio does not move between 0.25
and 1.0 mm cells).

## 14. What the published numbers imply, now that the mirror is exact

> **Partly superseded by §17.** The closed-form reproduction and the `D/N` inversion stand.
> The conclusion that the ion foil supplies 58 to 69 per cent of the returning impulse does
> not: it rested on a convergence too small by a factor of 2.8.


With the mirror law exact and the analyser length re-derived, the published figures can be
inverted rather than merely compared against. Three published quantities, one exact law,
and the arithmetic closes.

### The reversal reproduces the closed form

At 2 degrees, launched at z = 0, cross-sections infinite along z so nothing bounds the
drift:

| convergence | predicted N | measured reflections | predicted D | measured max z |
| --- | --- | --- | --- | --- |
| **0.200 mm, published** | 61.1 | **61** | 618.0 mm | **618.31 mm** |
| 0.400 mm | 30.6 | **31** | 309.0 mm | **309.27 mm** |
| 0.800 mm | 15.3 | **15** | 154.5 mm | **154.77 mm** |

0.05% on the drift distance and exact on the count. **The mirror is no longer a suspect in
anything.** (Predictions use the pre-correction `t_r`; with `capToCap` at 716.6 mm the
0.200 mm case measures 714.81 mm and the count is unchanged at 61, which is itself a check
- N depends on the angle and the tilt and not at all on how long the analyser is.)

### The published drift distance and oscillation count fix the injection angle

`D / N = t_r · V · tan(theta) / 2` **contains no convergence term**, so those two published
numbers determine the injection angle on their own, whatever supplies the deceleration:

| published D | N | D/N | implied theta | total k needed | **not from mirror tilt** |
| --- | --- | --- | --- | --- | --- |
| 310 mm | 26 | 11.92 mm | 2.037 deg | 2.394 | **58.2%** |
| 335 mm | 25 | 13.40 mm | **2.290 deg** | **2.799** | **64.3%** |
| 360 mm | 24 | 15.00 mm | 2.563 deg | 3.263 | **69.4%** |

Every implied angle is "about two degrees", which is what [A] states. And **at exactly 2
degrees the two published numbers cannot both be met** - the count needs k = 2.44 and the
distance needs k = 2.14 - so 2 degrees is a rounded figure and `D/N` recovers the
unrounded one.

### So the ion foil supplies 58 to 69 per cent of the returning impulse

The mirror tilt delivers `V sin(2 alpha)` = 22.4519 m/s per reflection at the published
spacer, exactly and unimprovably. The published reversal needs 2.4 to 3.3 times that.
**The remainder cannot come from the mirrors** - the closed form has no free parameter -
and [A] names what it does come from: "a returning electrostatic potential formed by mirror
tilt **as well as refraction on the ion foil**".

In energy terms the whole returning job is `(v_z0/V)^2 · 4000 eV` = **6.4 eV**, of which
the foil must supply about **4.1 eV** as a rise in the on-axis potential along the drift.
Against a foil biased between 0 and -20 V that is a penetration of about 20 per cent, which
is entirely ordinary - and the well measured in section 11 swings 2.19 V at -20 V, the same
order.

**An earlier estimate in section 3 put the foil's share at "between 59% and 76%".** That
number was reached through the impulse efficiency eta, which section 13 shows was the
discretisation rather than the mirror, so the reasoning was void. The conclusion happens to
have been right, and is now derived from an exact law instead.

### The requirement, as one number

The two mechanisms can be put in the same units, which makes the requirement a single
field strength rather than a ratio.

Per reflection the tilt delivers a fixed `V sin(2 alpha)`, and the ion advances `v_z t_r`
along the drift, so

> `d(mv_z^2/2)/dz = m V sin(2 alpha) / t_r`

which **does not contain `v_z`**. The mirror tilt is therefore exactly a *constant force*
along the drift - equivalently a uniform axial field - which is why the drift motion is
exactly parabolic and why `D = t_r N v_z0 / 2` came out right to 0.05%:

| | equivalent uniform axial field | rise over the drift |
| --- | --- | --- |
| **mirror tilt at the published spacer** | **6.820 V/m** | 2.28 V |
| total needed to reverse at 310 mm | 20.63 V/m | 6.40 V |
| total needed to reverse at 335 mm | 19.09 V/m | 6.40 V |
| total needed to reverse at 360 mm | 17.77 V/m | 6.40 V |
| **so the ion foil must supply** | **11.0 to 13.8 V/m** | **3.94 to 4.28 V** |

So the whole of the remaining question is: **can the ion foil, biased somewhere between 0
and -20 V, raise the cycle-averaged on-axis potential by about 4 V monotonically across the
drift?** Four volts from a 20 V electrode is a penetration near 20 per cent, which is
unremarkable - the well measured in section 11 already swings 2.19 V at -20 V. What is not
unremarkable is the *monotonicity*, and that is where the measured contour fails.

### Measured: a uniform foil bias is 4 to 6 times too weak

The foil wired back as a third element - a three-dimensional solve carrying the four plate
groups at their bias with all sixteen mirror strips grounded, which is the correct basis
field `psi_foil`. At 0 V it contributes exactly nothing, so that is the control and the
mirrors alone.

| foil bias | the foil's part of the impulse | of the mirror tilt | per volt |
| --- | --- | --- | --- |
| -2 V | -0.827 m/s | +0.037 **decelerating** | 0.413 |
| -5 V | -1.827 | +0.081 | 0.366 |
| **+2 V** | **+0.885** | **-0.039 accelerating** | 0.442 |
| **+5 V** | **+2.412** | **-0.107** | 0.482 |
| -20 V | -5.197 | +0.231 | 0.260 |

**The published polarity is the decelerating one**, which is a real check on the model
rather than an assumption: nothing in the geometry was chosen to make that come out.

**And a first version of this measurement got it wrong**, which is worth recording. Run at
+/-20 and +/-60 V, *both* signs appeared to decelerate - 0.231 at -20 V and 2.457 at +20 V.
That reads as a mechanism quadratic in the bias, which for a DC field would be strange. It
is trajectory feedback: a bias large enough to change the drift changes how much foil the
ion samples, and the response stops being linear. **The small-bias scan is the one that
measures the mechanism**, and it shows a clean linear response that flips sign, at 0.41 +/-
0.05 m/s per volt.

**At that rate the required 31 to 52 m/s needs 77 to 126 V of foil bias**, against a
published range of 0 to -20 V. So a **uniform** bias on the measured geometry is short by a
factor of 4 to 6.

The reason is structural rather than a matter of size. A uniformly biased foil makes the
on-axis potential nearly *flat* along the middle of the drift - the plates are the same
everywhere, so there is no axial gradient except near their two z-ends. Almost all of a
uniform foil's 20 V does no work on the drift at all.

### But the measured foil contour cannot do it at a single bias

This is the open problem, and it is much sharper than "the reversal deficit is unexplained".

The foil must produce a **monotone rise** of about 4 V in the on-axis potential from
injection to the reversal point. The contour measured off the published figure (section 11)
produces a well that is **not monotone** - deepest at 67% of the drift - and at either sign
of bias it fails:

- **negative bias**, a well: the potential *falls* 0 to 240 mm and rises only over the last
  100, so the net change from injection to 335 mm is about -4 V. The foil would *accelerate*
  the drift.
- **positive bias**, a hill: the ion does lose drift energy, but it has given up all 6.4 eV
  by about z = 185 mm and reverses there, well short of the published 310 to 360.

So one of three things is true, and none is established: the four plates are **biased
independently** rather than at one potential, which would let a non-monotone geometry make a
monotone on-axis profile; the contour's **drift-fraction calibration** is wrong by more than
its stated 5 per cent; or the reversal is shaped by something not yet in this model at all.
**Independent biases are the cheapest to test and the most likely** - [A] describes the foil
as "electrodes", plural, and four of them were counted.

## 15. The foil closes the gap, graded and spanning the whole drift

> **Superseded by §17.** The measurements here are correct and a graded foil really does
> decelerate the drift, but it is not what the instrument does — the mirror tilt alone
> reverses the drift once the convergence is read correctly.


Section 14 established that the ion foil must supply 11.0 to 13.8 V/m of axial field, a
3.9 to 4.3 V rise across the drift, and that a **uniform** bias on the measured contour
delivers a quarter of that. This section finds the configuration that delivers it, and
every plate potential stays inside the published 0 to -20 V.

### Why a uniform bias cannot, and why a grade alone cannot either

**A uniform bias makes the on-axis potential nearly flat along the middle of the drift.**
The plates are identical everywhere, so there is no axial gradient except near their two
z-ends. Measured, a uniform -20 V foil starting at 25% of the drift gives a well 7 V deep
whose *net* rise from injection to 340 mm is -4.9 V - the wrong sign, because the ion falls
into the well and climbs out again.

**And grading the bias, on its own, does nothing at all.** That was a hypothesis of mine,
refuted by its own control: a grade from -20 V at entry to 0 at the far end gave 0.130 of
the mirror tilt's impulse and the *reversed* grade gave 0.139, both matching a uniform bias
at their mean of 0.142. A gradient-driven mechanism would have made those two differ
strongly and oppositely.

**The reason is the more useful half.** The net work a conservative axial field does over
the drift is `q(phi_start - phi_end)`, and in that test the ion drifted to about 550 mm,
past the foil's 350 mm end. It crossed the whole potential bump and netted nothing. **So
the drift reversal has to happen inside the foil's z-extent, on the rising flank** - which
is a constraint on the device, not on the measurement, and it means the observable is the
potential profile rather than a flight that runs past it.

### The configuration that works

The cycle-averaged on-axis potential from the foil, averaged across the free-flight gap,
with the foil starting at the injection point rather than a quarter of the way along:

| z, mm | start 25%, graded | **start 0%, graded** | start 0%, uniform |
| --- | --- | --- | --- |
| 0 | -0.047 | **-3.919** | -4.011 |
| 40 | -0.411 | **-6.185** | -6.850 |
| 119 | -4.759 | -3.855 | -5.681 |
| 200 | -3.719 | -2.730 | -6.566 |
| 279 | -1.678 | -1.207 | -6.220 |
| 340 | -0.203 | **-0.131** | -3.964 |
| **net rise, 0 to 340** | **-0.40 V** | **+5.37 V** | +1.12 V |

**Two changes together**, and neither works without the other: the bias graded from -20 V
at the injection end to 0 at the far end, *and* the foil spanning the whole drift rather
than starting at a quarter of it. The profile dips 2.3 V over the leading 40 mm - the
foil's own entrance fringe, unavoidable - and then rises **6.05 V monotonically** from
z = 40 to 340.

### The energy budget closes

| | |
| --- | --- |
| drift energy to remove, at 2.29 degrees | **6.40 eV** |
| removed by the graded foil, z = 0 to 340 | 3.79 eV |
| removed by the mirror tilt at the published 200 micron spacer | 2.28 eV |
| **total** | **6.07 eV** |

**This is arithmetic on the measured potential profile, not yet a flight.** It implies
reversal a little past 340 mm against a published 310 to 360, with the foil supplying 62%
against the 58 to 69% section 14 requires - and a bias nearer 14 to 16 V than 20,
comfortably inside the paper stated range. The direct confirmation is a flight in this
configuration and is the first thing to run.

### The flight, and a correction

The arithmetic above is confirmed by flying it, and the flight corrects one claim made from
the profile alone.

| configuration | max z | reflections | oscillations | reversed |
| --- | --- | --- | --- | --- |
| **no foil**, 2.00 deg | 701.5 mm | 53 | 26.5 | **no** - still climbing at the 900 us ceiling |
| **graded -20 to 0**, 2.00 deg | **342.7 mm** | 27 | **13.5** | **yes** |
| uniform -20 V, 2.00 deg | 384.3 mm | 21 | 10.5 | yes |
| graded -14 to 0, 2.00 deg | 446.4 mm | 43 | 21.5 | yes |
| graded -20 to 0, 2.29 deg | 551.8 mm | 49 | 24.5 | yes |

**Published: 310 to 360 mm after 12 to 13 oscillations.** The graded case gives 342.7 mm
and 13.5 oscillations - both reproduced, from published inputs, with an exact mirror law and
a foil inside the published bias range. The no-foil control does not reverse at all inside
900 microseconds, so the foil is unambiguously doing the work.

**The correction.** This section previously said a uniformly biased foil "cannot close it at
all". That is wrong once the foil spans the whole drift: a uniform -20 V reverses too, at
384.3 mm. What distinguishes them is not whether they reverse but **`D/N`, the drift per
reflection**, which section 14 shows depends on the injection angle and *not* on whatever
supplies the deceleration:

| | drift per reflection | against the published 13.40 mm |
| --- | --- | --- |
| **graded -20 to 0** | **12.70 mm** | **5% low** |
| uniform -20 V | 18.30 mm | 37% high |

So the grade is what makes the *shape* of the deceleration right, and a uniform bias
reverses the drift at roughly the right place for the wrong reason - by decelerating too
hard too early, which costs it a third of its oscillations.

**And that revises section 14's inverted injection angle.** The 2.29 degrees derived there
assumed a uniform deceleration, which a real foil profile is not: flown at 2.29 degrees this
configuration overshoots to 551.8 mm. At the paper's stated 2.00 degrees it lands at 342.7.
**So `D/N` inverts to about 2 degrees once the deceleration profile is the measured one**,
and the paper's round figure needs no correction after all - section 14's inversion should be
read as what a *uniform* deceleration would imply, which is a bound rather than a value.

### Shipped, and reproduced from the template itself

The configuration is now part of `astral-3d.json` as a third element: a volume solve
carrying the four shaped plates at their graded bias with every mirror strip **grounded**,
which is what makes it the basis field `psi_foil` rather than an approximation. `foilGrade`
is the knob - 0 biases every slice alike, 1 grades linearly from `foilVolts` at the
injection end to zero at the far end, which is a resistor chain.

`einzel run` on the shipped template, with no scratch scaffolding:

| `foilGrade` | max z | reflections | oscillations | drift per reflection |
| --- | --- | --- | --- | --- |
| **1.0, shipped** | **342.74 mm** | 27 | **13.5** | **12.69 mm** |
| 0.0, uniform control | 384.26 mm | 21 | 10.5 | 18.30 mm |
| **published** | **310 to 360 mm** | 24 to 26 | **12 to 13** | **13.40 mm** |

Three properties of that element are asserted in `AstralMirrorDecompositionTests`, because
each was got wrong on the way there: the mirror strips must be present **and grounded**, the
foil must **span the whole drift**, and the bias must be **graded**. None of the three
announces itself when wrong - each produces a plausible converging analyser.

**Not done:** a corpus example pinning 342.74 mm. The flight is 468 microseconds and the
volume solve another 25 seconds, which would roughly double the release gate that currently
does thirty-odd examples in forty seconds. The template structure is asserted cheaply
instead, and the reversal itself is a study rather than a test.

### What this does and does not claim

**It does not claim the Astral's foil is graded this way.** What it claims is narrower and
checkable: given the published convergence, drift distance and oscillation count, and given
an exact mirror law, *a foil biased within the published range and graded monotonically
along the drift reproduces both published figures*, while a uniformly biased one reverses
the drift at roughly the right place with a third too few oscillations. That is a structural
conclusion about what the device must do, derived from published numbers.

**It also does not conflict with the measured contour** (section 11). The contour sets how
much of the plate bias reaches the axis at each z; the grade sets the profile. A wavy
contour modulates the penetration - a second-order effect on the deceleration, and the
natural candidate for the *spatial* focusing [A] and [B] both credit the foil with. Two
jobs, two features of the same electrode.

## 16. The resolving power, and why it has two separate limits

Reversal is reproduced (§15). The resolving power is not, and measuring it on the
reproducing configuration decomposes it into two independent problems with different owners.

A thermal cloud of 8 ions of m/z 500 launched at the injection end, flown out and back to a
detector there, arrival spread read off the ensemble. **These are the model's figures; the
instrument's is about 100,000 at m/z 200:**

| case | arrival width | R |
| --- | --- | --- |
| graded foil, 300 K | **355 µs** | 1.31 |
| graded foil, **0 K** — energy spread only | **19.2 µs** | 20.4 |
| uniform foil, 300 K | 84.9 µs | 4.20 |

Published R exceeds 100,000, and at this 779 µs flight that needs an arrival width under
**3.9 ns**. So both rows above are limits, and neither is close.

### Limit one: the drift is not isochronous, and a constant force cannot be

**This is a structural result, not a tuning failure.** §14 shows the mirror tilt is exactly
a *constant* force along the drift, and a linearly graded foil adds another constant one. Under
a constant force the time to reverse and return is `2 v_z0 / a` — **linear in the initial
drift velocity**. A 300 K thermal spread gives `sqrt(kT/m)` = 70.6 m/s on a `v_z0` of 1372,
so ±5%, and 5% of 779 µs is ±39 µs. That is the scale observed, and no amount of tuning a
constant force removes it.

**What does remove it is a harmonic drift potential.** A quadratic well is isochronous: its
period is `2 pi sqrt(m / q phi'')`, which contains neither `v_z0` nor the ion energy. Every
ion returns to the injection plane at the same instant whatever its drift velocity, and an
energy spread does not change the period either.

And it is expressible inside the published bias range. Biasing the foil as
`foilVolts (1 - u^2)` with `u` the drift fraction gives an on-axis potential rising as `z^2`
while every plate stays between 0 and -20 V - the same trick as the linear grade of §15,
one power up. **That is the natural reading of why the published contour is not a simple
taper.**

### Measured: the mirrors do no energy focusing at all

The energy limit above was quoted from a cloud carrying 0.4 mm of spatial spread as well,
which conflated two things. Energy spread alone, 9 ions, 0 K, no spatial extent, foil at
0 V:

| energy spread | arrival width | model R | **R x spread** |
| --- | --- | --- | --- |
| ±2.50% | 564.8 ns | 760 | **19.0** |
| ±0.50% | 96.2 ns | 4,463 | **22.3** |
| ±0.10% | 19.9 ns | 21,566 | **21.6** |

**`R x spread` is constant over a 25-fold range**, so `R` goes exactly as one over the
spread and the flight time is **first-order** limited in energy. The mirrors are doing no
focusing whatever - which is what applying Thermo's optimised `C(0)` coefficients to guessed
electrode depths should be expected to give, and it makes the diagnostic sharp: the figure
of merit for a depth fit is **`R x spread`**, currently 21.5, and cancelling the first-order
term should move it by orders rather than per cents.

**And the earlier ±2.5% figure was pessimistic in two ways worth naming.** That is the
mirrors' *acceptance window* - the interval `C(0)` was optimised to keep the oscillation
time flat across, per §1 - not a beam's actual energy spread, and using it as a cloud width
asks the model to fly the worst case the design tolerates. Pure energy spread at that width
gives R = 760, not the 20 first reported; the 20 came from adding 0.4 mm of spatial spread
on top. Both are the model's numbers, not the instrument's.

**A consistency check that the published figure is the first-order-cancelled one.** With the
first-order term uncancelled this model gives `R = 21.5 / spread`. A first-order focus
replaces that with `R = k / spread^2`, and reaching about 100,000 across the stated ±2.5%
acceptance needs `k` near 0.06 - a small second-order coefficient, i.e. some second-order
correction as well, which is exactly what §1's `TE2` parameter tunes and what [B] means by
"the third-order temporal focus". So the published resolving power is consistent with a
mirror focused to second order over its acceptance, and this model is consistent with one
focused to none.

**Mass.** Everything here is m/z 500. The instrument's figure is usually quoted at m/z 200,
which is the demanding end - turn-around time goes as the square root of mass, so a light
ion is harder. Any comparison of these numbers against the published one should say which
mass it means.

### The isochronicity requirement is 5e-6, and it is an optimisation not a guess

**The prediction above was tested and failed.** Biasing the foil as `foilVolts (1 - u^2)`
to make the on-axis potential quadratic gave an arrival width of 258 microseconds against
the linear grade's 355 - a 27 per cent improvement where the argument predicted orders of
magnitude. And the *uniform* bias was the best of the three at 84.9 microseconds.

**Two things were wrong with the reasoning, and both are worth keeping.**

**The constant-force estimate was 4.4 times low.** A constant force gives a return time
linear in `v_z0`, so a plus or minus 5.1 per cent thermal spread should give plus or minus
39 microseconds - about 80 wide, not 355. The period is therefore not merely linear in
`v_z0`, it is strongly *amplitude*-dependent, which means the well is far from harmonic.

**Measured, it is.** Fitting the on-axis profile to a pure `c z^2` about the launch point
and reporting the worst residual as a fraction of the swing:

| foil bias | rise, 0 to 340 mm | deviation from a pure quadratic |
| --- | --- | --- |
| linear `1 - u` | +5.37 V | **25%** |
| quadratic `1 - u^2` | +5.31 V | **15%** |
| uniform | +1.12 V | 53% |

**And the well's centre is not the launch point.** All three profiles dip to a minimum
around z = 40 to 61 mm and rise after it. That dip is the foil's own leading-edge fringe,
which reaches about 40 mm inward - the board gap - and is unavoidable at any bias. The ion
is launched on the inner flank of a well whose centre is 60 mm downstream of it, so the
motion is not a harmonic oscillation about the launch point even where the potential is
locally quadratic.

**What the requirement actually is.** For `R` to reach the published figure the arrival
width must be a fixed fraction of the flight:

| R | arrival width at a 779 us flight | as a fraction |
| --- | --- | --- |
| 10,000 | 38.9 ns | 5.0e-5 |
| 50,000 | 7.8 ns | 1.0e-5 |
| **100,000** | **3.9 ns** | **5.0e-6** |

So **the drift period must be constant to about 5e-6 across a plus or minus 5.1 per cent
spread in `v_z0`**, which needs the well harmonic to roughly 1e-4 of its depth. The best of
the three profiles above is 15 per cent - five orders short.

**That is not something a bias grade will reach, and it is the right shape of problem for
the machinery this project already has.** The foil is built from 16 slices along the drift,
each with its own potential expression, so the profile is a 16-parameter surface and making
the drift period amplitude-independent is an objective over it. `Einzel.Sweeps` has both
optimisers and §13's figure-of-merit registry is where such an objective registers. **This
is the first thing in the Astral work that genuinely wants the optimiser** rather than a
derivation, and it is also the most likely reason the published contour is "specially
shaped" rather than a simple taper: a shape optimised for high-order isochronicity is not a
shape anybody would guess.

### Limit two: these mirrors have no time-energy focus, and cannot have one

Even at 0 K the arrival width is 19.2 µs, which caps R at 20 by itself. That is the energy
spread, and removing it is what the mirrors' `(t|e)` tuning exists for.

**But this model cannot be energy-focused as it stands, for a reason worth stating plainly.**
The published `C(0)` coefficients (§1) were optimised against Thermo's electrode geometry.
`d1..d4` here are guesses. Applying their potentials to different depths gives a mirror that
is *not* at its own focus - there is no reason it should be. So the 19.2 µs is the expected
consequence of an unfitted geometry rather than a defect.

**This is what makes `d1..d4` a well-posed fit at last.** §13 forbids fitting them against the
drift reversal, because the impulse law has no free parameter there. The energy focus is the
opposite case: it depends on the mirror's field shape and on nothing else, so the depths are
exactly what moves it. Fit them by minimising the arrival width of an energy-spread cloud at
zero temperature, and the target is a number the mirrors alone control.

**And it makes Table 1's perturbation vectors the right check.** `C(1)` shifts `(t|e)` by a
published ~2.5 ppm/V per unit `TE1`; comparing this model's response to that perturbation
tests the mirrors' energy behaviour *differentially*, without needing the absolute focus to
be right first. It was already the sharpest available literature regression; it is now also
the natural companion to the depth fit.

## 17. The tilt alone reverses the drift. Sections 14 and 15 are superseded.

A fourth paper settles this, and it arrived after §§14 and 15 were written.

> **[C]** Stewart, Petzoldt, Shanley, Grinfeld, Denisov et al., *A High Dynamic Range Ion
> Detector for Multireflection Time-of-Flight Analyzers*, J. Am. Soc. Mass Spectrom.
> **2024**;35:2390–2399. Reports >1e4 single-shot dynamic range and **>100k resolving
> power** with 10 keV postacceleration, focal-plane correction and an integrated tilt
> corrector.

Its description of the analyser is unambiguous, and it is quoted rather than paraphrased
because it contradicts what this document previously concluded:

> **Asymmetry, or tilting of the mirrors relative to one another, applies a counter force
> reducing the ions' drift velocity and ultimately halting and reversing it**, so that ions
> are returned back to the postaccelerator, where they are accelerated to 14 keV and focused
> onto the detector surface. **The ion foil compensation electrodes serve to both counter
> ToF aberrations induced by the converging ion mirrors** and improve the spatial focus of
> the returned ions, maximizing transmission through the analyzer.

So the **tilt does the reversal on its own**, and the foil's job is to counter the *time-of-flight
aberration* the converging mirrors induce - which is precisely the amplitude-dependent drift
period §16 measured. [C] also cites a dedicated paper on it: **Grinfeld, Stewart, Makarov,
*Multi-reflection [TOF] with isochronous drift in elongated ion mirrors*, Nucl. Instrum. Methods Phys. Res. A 1060 (2024) 169017** - which is the paper to get next.

### Measured: mirrors only, no foil

| convergence, injection angle | max z | reflections | drift per reflection |
| --- | --- | --- | --- |
| 0.20 mm, 2.00° — this document's earlier reading | 714.81 mm | 61 | 11.72 mm |
| 0.40 mm, 2.00° | 357.53 mm | 31 | 11.53 mm |
| **0.56 mm, 2.29°** | **334.76 mm** | **25** | **13.39 mm** |
| **published** | **310 to 360 mm** | **24 to 26** | **13.40 mm** |

**All three published figures, exactly, with no foil in the model.** And it is not a fit with
spare parameters: `D/N` fixes the injection angle without reference to the convergence
(§14), and the convergence then follows from either `N` or `D` alone. Two published numbers,
two unknowns, and the third number checks.

### What was wrong, and it was one thing

`α = 8.0e-4` is needed; this document used `2.857e-4`. The factor of 2.8 is entirely in what
"a 200-µm thick spacer" is taken to tilt over. The template computed
`asin(convergence / 2 / driftLength)` - reading the *gap* as closing by 200 µm across the
*drift length*. The value that works corresponds to a 200 µm spacer acting over a **~250 mm
baseline**, tilting each mirror by 200 µm rather than closing the gap by it. Which of those
the hardware means is not stated in any of the three papers, and it is now the single most
consequential unpublished number in this model.

**So §14's conclusion that the foil supplies 58 to 69 per cent of the returning impulse is
withdrawn**, along with §15's search for a foil configuration to deliver it. Both were built
on a convergence too small by 2.8x, and the deficit they attributed to a missing mechanism
was the geometry. §15's graded-foil result stands as a measurement - a graded foil *does*
decelerate the drift, and the numbers in it are correct - but it is not what the instrument
does.

**And §14's inverted injection angle of 2.29° was right.** It was withdrawn in §15 because the
graded-foil configuration overshot at that angle; the overshoot was the foil contribution
that should not have been there. `D/N` inverts to 2.29° and the tilt-only model reproduces
everything at it.

### Shipped, and the published input separated from the guess

The template now declares the published quantity and the guessed one apart, because the
whole uncertainty in the drift reversal sits in one of them:

| parameter | value | status |
| --- | --- | --- |
| `spacerThickness` | 0.200 mm | **published** |
| `tiltBaseline` | 250 mm | **guessed** — the length the spacer tilts each mirror over |
| `mirrorTilt` | `asinPi(spacerThickness / tiltBaseline)` | derived |
| `convergence` | 0.56 mm | derived, and reported because earlier revisions declared it directly |
| `injectionAngle` | 0.03998, i.e. 2.29° | from `D/N`, which contains no convergence term |
| `foilVolts` | **0 V** | inside the published 0 to −20 V, so the foil contributes nothing |

`einzel run` on the shipped file, unmodified except for a detector at the injection end:

| | drift | reflections | drift per reflection |
| --- | --- | --- | --- |
| **measured** | **334.61 mm** | **25** | **13.38 mm** |
| published | 310 to 360 mm | 24 to 26 | 13.40 mm |

**The foil ships at zero bias on purpose.** It contributes exactly nothing there, so the
reversal this template reproduces is unambiguously the tilt's and not a foil contribution
standing in for a geometry error - which is precisely the mistake that produced the
superseded §§14 and 15. The geometry is kept because countering the mirrors' time-of-flight
aberration is the foil's published job and the profile that does it is an unrun
optimisation.

### What this does to the resolving power

It makes §16 sharper rather than obsolete. The foil's published job is exactly the problem
§16 identified and quantified: the drift period must be constant to **5e-6** across the
thermal spread in `v_z0`, the constant force of a tilted mirror pair makes it
amplitude-dependent instead, and closing that is a shape optimisation over the foil's
profile. [C] calls that "countering ToF aberrations induced by the converging ion mirrors",
and Grinfeld et al. 2024 is a whole paper about it.

**The one thing to fix first is the convergence**, since every reversal number in this
document depends on it and the model now reproduces the published instrument when it is
right.

## 18. How to make the mirrors focus, and why c1 is not the objective

§16 measured that this model's mirrors do no energy focusing: `R x spread` is constant at
21.5 over a 25-fold range, so the flight time is first-order limited. This section is the
route out, and it corrects the obvious first answer.

### What was missing was a seam, not a capability

A mirror focuses when the first-order time-energy coefficient is cancelled - `c1` in
`T/T0 = 1 + c1 d + c2 d^2 + ...`, with `d` the fractional energy offset.
`FocusingAnalysis.Fit` has computed those coefficients since Stage 4 and §12 asks for them
by name. **They were not exposed as figures of merit**, so no study or optimiser could name
one - the same "exists only in an assembly description" state `ITransportMode` and the
journal were once in.

`focusingC1` and `focusingC2` are now in the registry, taking it from 18 nameable figures to
20. Three decisions in them:

- **Reported as magnitudes.** The target is zero from either side, and an optimiser
  minimising a *signed* coefficient would drive it to minus infinity.
- **The fit's own residual is the GRD-1 uncertainty**, divided by the scan's half-width: a
  coefficient multiplies `d`, so a relative flight-time residual of `r` across a scan of
  half-width `s` constrains it to about `r/s`. A residual not small against the coefficient
  raises `focusing.fit-residual`, because a cubic that did not capture the behaviour reports
  a binding order that should not be believed.
- **A declared cloud is deliberately ignored.** Every other ensemble figure flies the cloud
  when there is one, because those are properties of a packet. A focusing coefficient is a
  property of the *optics*: measuring a derivative needs ions at known offsets, not drawn
  from a distribution. Flying a cloud gives a scatter to fit through instead of a curve.

**And one template defect had to go first.** `mouth` was defined as `d4`, so varying the
electrode depths walked both mirror mouths inward and shrank the field-free gap - physically
right for a fixed envelope, where depth and drift length are one degree of freedom rather
than two, but it makes a depth scan a scan along a trade. SPEC.md's item 13 records that
trap being fallen into once already. `mouth` is now independent.

### The lever is strong, and the fit is deterministic

Scanning `d2` on one reflection, mirrors only, +/-2.5% energy:

| `d2`, mm | 22 | 30 | 34 | **36** | 38 | **40** |
| --- | --- | --- | --- | --- | --- | --- |
| `\|c1\|` | 0.127 | 0.0275 | 0.0054 | **0.000102** | 0.0030 | 0.0030 |
| `\|c2\|` | 2.31 | 1.44 | 0.96 | 0.75 | 0.52 | **0.30** |
| **R** | 162 | 849 | 1506 | **2044** | 3268 | **5674** |

`|c1|` falls **210-fold** below its nominal 0.0189, and the minimum is identical at 11 and
21 ions per point, so it is deterministic rather than fit noise.

### But R does not peak where c1 is cancelled

**This is the finding, and it corrects the obvious plan.** `|c1|` bottoms at `d2` = 36 mm
with R = 2044, and R goes on climbing to **5674 at 40 mm** where `c1` is thirty times
larger. The reason is that `c2` falls monotonically across the whole scan and **`c2` is what
binds at this acceptance**: at `d2` = 36 the second-order term is
`0.75 x 0.025^2` = 4.7e-4 against the first-order term's 2.6e-6, so `c2` dominates by 180.
Cancelling `c1` there buys almost nothing.

**So `resolvingPower` is the objective and the coefficients are diagnostics.** That is what
`FocusingOrder`'s own remarks say - "the coefficients say which term is responsible, and
therefore whether the fix is a longer flight path, a different mirror, or a narrower energy
acceptance" - and it is worth stating because minimising `|c1|` is the plan anybody would
reach for first, including this document an hour before it was measured. The coefficients
earn their place by saying *which* term to attack; they are the wrong thing to attack
directly.

### R > 100,000 is reachable, and one parameter got there

Scanning the energy spread at each depth separates the two regimes, and the scaling settles
which order binds without any appeal to the fit:

| `d2` = 36 mm, `c1` cancelled | R | **R x s^2** |
| --- | --- | --- |
| ±2.50% | 2,044 | 1.278 |
| ±1.00% | 12,330 | 1.233 |
| ±0.50% | 48,621 | 1.216 |
| ±0.25% | 187,471 | 1.172 |
| ±0.10% | **1,138,010** | **1.138** |

| `d2` = 44 mm, `c1` merely small | R | **R x s** |
| --- | --- | --- |
| ±2.50% | 9,552 | 238.8 |
| ±1.00% | 18,463 | 184.6 |
| ±0.50% | 36,527 | 182.6 |
| ±0.25% | 74,016 | 185.0 |
| ±0.10% | 175,552 | 175.6 |

**`R x s^2` is constant at 36 mm and `R x s` is constant at 44 mm**, each over a 25-fold
range. So 36 mm is genuinely first-order focused and second-order limited, while 44 mm is
still first-order limited with a `c1` merely 6.8 times smaller than nominal - and
0.01885/0.00276 = 6.8 accounts for its whole gain. **44 mm is not a focus.**

**Which depth is better depends on the spread you optimise at, and that is the trap.** At
±2.5% the scan picks 44 mm, 9,552 against 2,044. At ±0.1% the ordering reverses six-fold,
1,138,010 against 175,552, because `1/s^2` overtakes `1/s`. **Optimising at the acceptance
window rather than at the beam's actual spread selects the wrong geometry** - and ±2.5% is
the acceptance, per §1.

**One parameter reaches the published figure.** With `c1` cancelled, `R x s^2` = 1.2, so
R = 100,000 at an energy spread of **±0.35%**, and better than that below it.

### Why the paper says "third-order temporal focus"

The scaling makes the design legible. Each order cancelled costs one degree of freedom and
buys one power of the energy spread:

| orders cancelled | free parameters needed | scaling | R at ±2.5% |
| --- | --- | --- | --- |
| none | - | `1/s` | 760, measured |
| `c1` | 1 | `1/s^2` | 2,044, measured |
| `c1`, `c2` | 2 | `1/s^3` | **not what an R-maximising search produces** — it balances the two instead; see below |
| **`c1`, `c2`, `c3`** | **3** | **`1/s^4`** | **>1e5** |

So reaching the published resolving power *across the published ±2.5% acceptance* needs
three orders cancelled, which is exactly what [B] describes: "Such regime generates the
**third-order temporal focus** and provides, correspondingly, the best possible resolving
power of the analyzer." And it is why the calibration has **two** correction vectors,
`TE1` and `TE2`, on top of the electrode geometry - `d1..d4` supply the geometric degrees of
freedom and the two parameters trim the orders the geometry cannot hold exactly.

**That makes the next step concrete rather than exploratory**: cancel `c1` and `c2`
simultaneously over two depths and check that `R x s^3` goes constant. Two parameters, one
objective each, and the prediction is a scaling law rather than a number - which is the kind
of target that cannot be hit by accident.

### The optimiser, run over three depths

`einzel optimise` over `d1`, `d2` and `d3`, maximising `resolvingPower` at the published
±2.5% acceptance, Nelder-Mead, 120 evaluations, 3 minutes. The acceptance is the right
place to optimise *because it is the design condition* - the mirrors are specified flat
across it - and optimising at a narrower spread would prefer a `c1`-only cancellation, which
the section above shows is the wrong geometry.

| design | `d1` | `d2` | `d3` | R at ±2.5% |
| --- | --- | --- | --- | --- |
| shipped | 20 | 50 | 90 | 1,096 |
| best one-parameter scan | 20 | 44 | 90 | 9,552 |
| **optimiser, three parameters** | **19.596** | **43.073** | **89.862** | **48,311** |

**Forty-four times the shipped design and five times what one parameter reached**, and
within a factor of two of the published >100,000 at the same acceptance.

**Two qualifications the run reported itself, neither suppressible.**
`optimiser.budget-exhausted`: the search stopped at its 120-evaluation ceiling with a final
simplex spread of 0.0102 of the box against a `parameterTolerance` of 1e-4, so **this is the
best design found and not an optimum** - the number should be expected to improve. And
`ENSEMBLE_SMALL`: at R = 48,311 the arrival peak is 8 ns wide and fifteen ions is a thin
basis for a half-maximum, so the figure carries real sampling error.

**What the design is not.** It is *a* geometry consistent with the published potentials, not
Thermo's. Four depths against three cancellation conditions leaves a one-parameter family
even before `d4`, `mouth` and the two `TE` vectors are counted, and nothing here pins the
turning depth that §14's `capToCap` derivation rests on. What it establishes is that the
published resolving power is reachable from the published potentials by fitting the geometry,
which was the open question - not what the geometry is.

### Confirmed with 41 ions, and the design balances orders rather than cancelling one

| design | spread | R | `\|c1\|` | `\|c2\|` | **R x s** |
| --- | --- | --- | --- | --- | --- |
| optimised | ±2.50% | **47,657** | 0.00069 | 0.0447 | 1191 |
| optimised | ±1.00% | **86,419** | 0.00069 | 0.0493 | 864 |
| optimised | ±0.50% | **150,036** | 0.00067 | 0.0509 | 750 |
| optimised | ±0.25% | **317,944** | 0.00063 | 0.0409 | 795 |
| shipped | ±2.50% | 1,086 | 0.0189 | 0.4465 | 27 |

**R exceeds 100,000** at ±0.5% and below, and 47,657 at the acceptance with 41 ions confirms
the 48,311 measured with 15, so the thin-ensemble warning did not bite.

**But `R x s` is roughly constant at about 800 across the three narrower spreads**, so the
optimised design is *still first-order limited* - `c1` is merely 27 times smaller than
shipped, not cancelled, and `R x s` went from 27 to 800 by exactly that. The table above in
this section predicted a two-order cancellation would give `1/s^3`; that is **not** what
maximising R at a fixed spread produces.

**Why, and it is the more useful statement.** At ±2.5% the two terms are comparable:
`c1 s` = 1.7e-5 against `c2 s^2` = 2.8e-5. An optimiser maximising R at one spread will
**balance** the orders there rather than cancel either, because cancelling `c1` alone costs
more in `c2` than it gains - which is exactly what the `d2` = 36 against `d2` = 44
comparison showed with one parameter. So:

- **to reach a given R at a given spread**, maximise R at that spread and accept a balance;
- **to change the scaling**, cancel orders explicitly - minimise `\|c1\|`, then `\|c2\|` on the
  locus where `c1` stays cancelled, which needs a constrained search or a Python objective
  combining them, and is what [B]'s two `TE` correction vectors do on top of the geometry.

The second is what the published instrument does, and it is why "third-order temporal focus"
is a statement about *scaling* rather than about a resolving power at one operating point.

**One caveat on the narrow-spread rows.** At ±0.25% and R = 317,944 the arrival spread is
23 ps on a 14.7 microsecond single-reflection flight, 1.6e-6 relative. That it agrees with
`1/(2 c1 s)` = 317,460 from an independently fitted `c1` is reassuring, but it is close
enough to the solved field's own accuracy that the number should be re-established on a
refined mesh before being quoted as an optical limit rather than a numerical one.

**Two things not to read into the numbers yet.** This is **one reflection**, so it measures
a mirror in isolation rather than twenty-five of them compounding. And `d1..d4` are four
parameters against one condition, so cancelling anything leaves a three-parameter family -
`c2`, the spatial focus, and the turning depth that §14's `capToCap` derivation rests on are
what pick a point in it. R was still rising at the edge of the scan above; where it turns
over, and what it reaches, is the next measurement.

## 19. Two checks that moved the goalposts

Both were run to answer "what would convince us the model is right", and both came back
saying the focusing work in §18 is narrower than it read.

### The `C(1)` test: the one unfitted published check, and it is a factor of two out

Every good agreement so far is one of two kinds: a closed form the engine ought to match, or
a number the model was fitted to. Table 1's `C(1)` vector is neither. The paper says applying
it with `TE1` = 0.01 shifts `(t|e)` by about 2.5 ppm/V; since `c1 = e0 (t|e)` and `e0` =
4000 V, that predicts **`dc1/dTE1` = 1.0** - dimensionless, and nothing in this model was
fitted to it. Potentials written as `ionEnergy (C0_k + te1 C1_k)`, shipped depths, one
reflection, ±2.5%:

| `te1` | -0.030 | -0.020 | -0.010 | 0.000 | 0.010 | 0.020 | 0.030 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `\|c1\|` | 0.0560 | 0.0423 | 0.0254 | **0.0043** | 0.0228 | 0.0584 | 0.1064 |

**The slope near zero is 1.85 to 2.12 against a published 1.0.** Same order, a factor of two,
and the factor is plausibly a definitional half in how `(t|e)` is written - T goes as one over
the root of energy, so a natural `(t|e)` may absorb the half. Not resolved, and not the clean
confirmation it was meant to be. The response is also **not linear** over the range: the slope
runs 1.37 to 4.80 across ±0.03, so "shifts by a constant" holds near zero and not far from
it. Recorded as a discrepancy to settle against the paper's definition, not as a failure.

### `c1` depends on the field-free path, so a mirror is focused FOR a drift length

The `te1` = 0 row gives `|c1|` = 0.0043 at the shipped depths, where §18 measured 0.0189 -
same mirror, different launch and detector offsets. Isolated directly, same depths, one
reflection, only the free path changing:

| launch offset | flight | `\|c1\|` |
| --- | --- | --- |
| 5 mm | 17.449 µs | **0.0224** |
| 20 mm | 18.595 µs | **0.0518** |
| 60 mm | 21.648 µs | **0.1151** |
| 120 mm | 25.904 µs | **0.1730** |

**`c1` grows 7.7-fold with the free path.** It is not a property of the mirror; it is a
property of the mirror *and* the drift together - which is physically right, since first-order
focus is the condition that the mirror's positive `dT/dE` cancels the free flight's negative
one. A mirror is focused *for* a given path.

**So §18's optimised depths are the right answer to the wrong question.** They focus a free
path of about 530 mm - one reflection with a 10 mm offset - where the Astral's is about
1.08 m per oscillation. The scaling laws stand (they are about which order binds); the
particular depths, and every R quoted from them, are for a measurement geometry rather than
for the instrument.

**The consequence is the same one §18 already listed and under-weighted: the focusing has to
be measured on the real track.** One reflection with an arbitrary launch point is not the
experiment. The full flight - 24 to 26 oscillations, the real drift per oscillation, a thermal
cloud, a detector - is the only configuration in which "the mirror is focused" means what it
means for the instrument, and it has never been run.

### What convincing would take, ranked

| | status |
| --- | --- |
| **the full flight**, 24-26 oscillations, thermal cloud, to a detector | never run - the experiment everything above stands in for |
| **`C(1)` sensitivity** settled against the paper's `(t|e)` definition | measured 2.0 against 1.0; definition unresolved |
| **drift isochronicity** - R with a 300 K cloud | 1.31; the foil's real job, untouched |
| **`tiltBaseline`** settled or bounded | a 2.8-fold ambiguity every reversal number rests on |
| **mesh convergence** on R at narrow spread | 23 ps may be numerical |
| **spatial focus** at the detector | not measured |

The first row is the one that matters. Everything else is a proxy for it.

## 20. The full track flies, end to end

§19 said the full flight had never been run and that everything else was a proxy for it. It
has now: one ion of m/z 500 at 4 keV, launched at the injection end at the shipped 2.29°,
shipped depths, `te1` = 0, detected on a plane 2 mm behind the launch point on its return.
43 seconds of wall time, 8,105 trajectory samples.

| | published | this flight |
| --- | --- | --- |
| oscillations, total | 24-26 | **25** - 25 reflections out, 25 back |
| drift reversal | 310-360 mm | **334.61 mm**, at t = 420.1 µs |
| drift per reflection | 13.40 mm | **13.38 mm** |
| path length | ~30 m | **31.27 m** |
| flight time | ~779 µs | **853.69 µs**, 9.6% long |
| `\|v_z\|` returned against launched | - | 1572.8 against 1569.6 m/s |

**Every geometric number in the register reproduces on one flight.** The reversal and the
per-reflection drift were already matched in §17 on the outbound half; what is new is that
the return half is the mirror image the tilt mechanism says it must be - 25 reflections each
way, `|v_z|` conserved to 0.2% over the whole round trip, and the ion arriving back where it
started. §13's closed form (`Δv_z = V sin 2α` per reflection, independent of the electrode
design) predicts exactly this symmetry, and this is it measured on the whole instrument
rather than on one reflection.

**The flight time is 10% long, and the split is informative.** The path is 4% long (31.27
against ~30 m), which is the reversal sitting at 335 rather than the middle of the published
range. The remaining ~5% is time spent decelerated inside the mirrors, which depends on the
turning depth and the potential profile - i.e. on `d1..d4`, which are still guesses. A fitted
mirror should move it, and which way is a check on the fit.

**Counting, once more, because it has been wrong before.** 50 reflections is 25 oscillations
total; the paper's 24-26 is the total and its 12-13 is the outbound half. 25 reflections
outbound over 334.61 mm is 13.38 mm per reflection. All three readings are consistent with
each other and with [A].

**What this changes about §18 and §19.** The wrong-path caveat was that one reflection with an
arbitrary launch point is not the instrument. This is the instrument. The focusing fit is now
being run on the true per-oscillation path (half an oscillation from the mid-plane, which has
the same `c1` as the whole track if every oscillation is identical), and its result should be
confirmed *here*, by flying an energy-spread cloud round the whole track and reading R off the
arrivals. That confirmation is the measurement §19 said was missing.

## 21. Three measurements on the true path

All three were run once the full track flew, and each closes or reopens something above.

### `te1` on the true per-oscillation path: it cancels `c1` and worsens everything else

Half an oscillation from the mid-plane, shipped depths, 21 ions, ±2.5%:

| `te1` | -0.012 | -0.008 | -0.004 | 0.000 | **0.004** | 0.008 | 0.012 | 0.020 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `\|c1\|` | 0.0369 | 0.0295 | 0.0214 | 0.0124 | **0.0025** | 0.0084 | 0.0206 | 0.0494 |
| `\|c2\|` | 0.324 | 0.359 | 0.400 | 0.448 | **0.503** | 0.567 | 0.642 | 0.832 |
| `\|c3\|` | 1.27 | 1.49 | 1.74 | 2.03 | **2.37** | 2.75 | 3.21 | 4.47 |

`c1` crosses zero near `te1` = 0.005, and **`c2` and `c3` grow monotonically as it does** - so at
the shipped depths, trimming `c1` to zero leaves `c2` = 0.52 and caps R near 1,500 at the
acceptance. `te1` is the instrument's own `c1` knob and it cannot reach the higher orders; that
is why the paper has a second vector, and why **the depths must carry `c2` and `c3`**. The fit is
now well-posed for the first time: depths for the higher orders, `te1` for the first. The
slope `dc1/dte1` is 2.2 here against the published 1.0 - the same factor of two as §19.

### The drift is a constant-force motion, measured

Same full track as §20, injection angle scaled ±5% so `v_z0` varies at fixed `|v|`:

| angle / a0 | flight | dT/T | dv/v | **(dT/T)/(dv/v)** |
| --- | --- | --- | --- | --- |
| 0.950 | 811.29 µs | -4.97% | -5.0% | 0.993 |
| 0.975 | 832.47 | -2.49% | -2.5% | 0.994 |
| 1.025 | 874.95 | +2.49% | +2.5% | 0.996 |
| 1.050 | 896.22 | +4.98% | +5.0% | 0.996 |

**Ratio +0.99: the return time is exactly proportional to `v_z0`**, which is what a constant
axial force gives (round trip `2 v_z0 / a`, with the reversal point scaling as `v_z0^2`). Not
isochronous. At 300 K the thermal `v_z` spread is about 4.5%, so **the drift alone caps R near
11** with the mirrors perfect. That is the foil's published job, now quantified: make the return
time independent of `v_z0`. A harmonic axial well *centred at the injection point* does exactly
that, since its period is amplitude-independent; §15's quadratic attempt reached 27% rather than
orders because its well centre landed at z ≈ 61 mm.

**And the published foil is a contoured plate at a uniform voltage, not a graded one.** Its inner
edge follows the cosine measured from the pixels in §11 - thin at 41% of the drift, thick at 67% -
and the on-axis potential varies along z because the *shape* brings metal nearer or further,
with the whole plate at one voltage in the published 0 to -20 V range. §15's graded-voltage
ramp was a different mechanism. The test being run is the published one: uniform -20 V,
contoured, does it move the ratio.

### Mesh convergence: `c2` and `c3` converge, `c1` has a floor

Shipped depths, `te1` = 0, true path, 21 ions:

| cell | `\|c1\|` | `\|c2\|` | `\|c3\|` | flight |
| --- | --- | --- | --- | --- |
| 1.00 mm | 0.01074 | 0.4568 | 2.132 | 17.1083 µs |
| 0.50 mm | 0.01241 | 0.4477 | 2.032 | 17.0931 |
| 0.25 mm | 0.01082 | 0.4440 | 2.044 | 17.1084 |

`c2` converges at roughly second order (changes 9.1e-3 then 3.8e-3) and `c3` settles. **`c1` does
not converge - it wanders by ±0.0015 across three meshes**, so this solve has a noise floor on
the first-order coefficient of about that size, and `|c1|` below ~0.002 is unresolved. Two
consequences. Any depth fit here cannot demonstrate a first-order focus sharper than the floor,
which alone caps R near 13,000 at ±2.5% (`1/(2 x 0.0015 x 0.025)`). And §18's `|c1|` = 0.000102
at `d2` = 36 was inside the floor - the scaling law there still holds, since `c2 s` dominates
`c1` for spreads above 0.003 either way, but the cancellation was not resolved to that figure.
The FLD-1 floor of Amendment 36, met in a new place: a first-order time-energy coefficient is
a small difference of large flight times, and the strips' cut cells set how small a difference
survives. Refining the mesh did not move it, so it is not simple discretisation of the
Laplacian; the fit's sensitivity to the scan endpoints is the next suspect.

## 22. The published foil does the published job

§21 quantified the foil's task: the bare tilt gives a return time exactly proportional to
`v_z0` (ratio +0.99), and isochronicity needs that ratio driven to zero. The test was the
foil **as published** - the contoured plate whose inner edge follows the pixel-measured
cosine of §11, at a **uniform** voltage in the published 0 to -20 V range. Full track, three
injection angles per case:

| foil, uniform | T(0.95) | T(1.00) | T(1.05) | **(dT/T)/(dv/v)** | |
| --- | --- | --- | --- | --- | --- |
| 0 V | 811.29 µs | 853.69 | 896.22 | **+0.995** | bare tilt, constant force |
| -10 V | 723.52 | 709.87 | 674.46 | **-0.691** | |
| **-20 V** | 652.16 | 626.72 | 594.20 | **-0.925** | published maximum |
| +20 V | 553.13 | 619.11 | never returned | broken | sign-reversed control |

**The ratio crosses zero between 0 and -10 V** - about -6 V by interpolation, inside the
published range - and the sign-reversed control breaks the instrument (the fast ion is pushed
out and never comes back), so the published polarity is the right one. This is the mechanism
[C] describes, reproduced with the published shape at a uniform bias: **the contoured foil
controls the drift's isochronicity, and a voltage inside the published window makes the return
time independent of `v_z0`.** §15's graded-voltage ramp was never the published mechanism, and
the user's objection to its shape was right.

**A second published number converges on the same voltage.** The flight time falls from 854 µs
at 0 V to 710 at -10 V, because the attractive well pulls the reversal inward; interpolated to
-6 V that is about **768 µs against the published ~779**. Two register numbers - the
isochronous condition and the flight time - pointing at one foil setting, neither fitted. The
bracket at -4, -6, -8 V is being run to pin it rather than interpolate, and to read the
reversal point there, which the pull-in must have moved from 335 mm.

**What "isochronous" buys, and its limit.** At ratio 0 the first-order dependence of return
time on `v_z0` is gone, and the thermal cap of R ≈ 11 from §21 lifts; what is left is the
second-order term, which a three-point scan cannot see and a five-point one at the pinned
voltage will. That is the same structure as the mirrors' `c1`/`c2` story, in the drift.

### Pinned: -4 V nearly cancels first order, and the flight time lands on the published value

| foil, uniform | T(0.95) | T(1.00) | T(1.05) | ratio |
| --- | --- | --- | --- | --- |
| **-4 V** | 774.02 | **784.85** | 765.54 | **-0.108** |
| -6 V | 756.35 | 756.82 | 727.44 | -0.382 |
| -8 V | 739.56 | 731.97 | 698.14 | -0.566 |

**At -4 V the return time is peaked at the nominal angle** - both neighbours are shorter - so
the first-order dependence on `v_z0` is nearly gone (slope a tenth of ballistic) and what shows
is the *second-order* term. The zero of the first-order slope is near -3.6 V by interpolation
against the 0 V row. **And the flight time at -4 V is 784.85 µs against the published ~779**,
0.75%: the register's flight time and its isochronous condition both point at one foil voltage
inside the published window, and neither was fitted. A five-point scan at -3 and -4 V is
running to fit the first- and second-order coefficients properly and read the reversal point,
which the attractive well must have pulled in from 335 mm.

**Why the foil dominates although the mid-plane axis barely feels it.** Sampled *on the
mid-plane axis* the foil at -20 V contributes only -0.03 to -0.05 V, which cannot move a
6.4 eV axial motion. But the mid-plane is the *gap between the two foil plates* - they sit at
x ∈ [176, 240] and [477, 541] mm, |y| ∈ [20, 22] - and the ion zigzags across the whole free
gap at 39 km/s, so the slow z-motion feels the potential **averaged over x**. That average is
-4.0 V at injection, -7.0 V at its deepest (z = 233 mm, the thick part of the contour), -4.3 V
at the 335 mm reversal: a well about **3 V deep against 6.4 eV of axial energy**, which is
why -20 V overshoots and a fifth of it is about right. The well has the foil's shape - two
lobes at 63 and 233 mm with a saddle at 155, the cosine's thick-thin-thick - and is not
harmonic, which is what will set the second-order term.

### Five points at -3 and -4 V: first order cancelled 22-fold, second order is what remains

Fitting `T/T0 = 1 + a(f-1) + b(f-1)^2` over five injection angles on the full track:

| foil | T0 | reversal | **a** (first order) | **b** (second order) |
| --- | --- | --- | --- | --- |
| bare tilt | 853.7 µs | 334.6 mm | **+1.000** | - |
| **-3 V** | **800.4 µs** | 335.6 mm | **+0.046** | -6.2 |
| -4 V | 784.9 µs | 335.8 mm | -0.149 | -7.0 |

**At -3 V the first-order dependence on `v_z0` is cancelled 22-fold**, with the zero near
-3.2 V. Two things about it matter more than the number.

**The reversal point does not move.** 335.6 mm with the foil against 334.6 without, at either
voltage. The x-averaged well is nearly the same depth at injection (-4.0 V of -20) and at the
reversal (-4.3 V), so the ion is accelerated into the well and decelerated out of it and arrives
at the turning point with the axial energy the tilt alone would give it. **The foil is a pure
timing correction that leaves the track where the tilt put it** - which is why [C] can say the
tilt reverses the drift and the foil corrects the aberration, as two separate statements.

**What remains is second order, and it is the shape.** `b` ≈ -6.5 at either voltage, so at a
4.5% thermal spread in `v_z` the drift is limited to about R ≈ 35 by curvature alone - up from
11, but far from the instrument. A harmonic well has `b` = 0 by construction; the well the
cosine contour makes is two-lobed with a saddle and is not harmonic, and that is the cost. So
the foil's *voltage* is settled inside the published window by two register numbers, and the
foil's *shape* is the next inverse problem: which contour makes `b` vanish. That is the subject
of the isochronous-drift paper, and the pixel measurement of §11 is its starting point rather
than its answer.

**Shipped.** The template now carries `foilVolts` = -3 V and `foilGrade` = 0 - the published
arrangement - and the test that pinned the foil at zero now pins it here, with the reason.

### The two fits on the true path agree in R and disagree in geometry

`einzel optimise` over `d1`, `d2`, `d3` and `te1`, maximising R at ±2.5% on the true
per-oscillation path, 300 evaluations each:

| | `d1` | `d2` | `d3` | `te1` | R at ±2.5% | |
| --- | --- | --- | --- | --- | --- | --- |
| shipped | 20 | 50 | 90 | 0 | ~1,100 | |
| Nelder-Mead | 22.47 | 42.53 | 89.53 | -0.0027 | **36,707** | 2 of 15 ions lost |
| CMA-ES | 10.47 | 34.68 | 83.99 | +0.0290 | **36,532** | |

**Same objective to half a per cent, designs 12 mm apart in `d1`** - the degenerate family
§18 predicted, one balancing condition against four parameters. Neither converged at 300
evaluations. The Nelder-Mead design loses the two extreme-energy ions on one reflection, which
on fifty is a transmission problem, so the CMA design is the one to carry forward. Both are now
being confirmed on the full track with the foil at -4 V, which is the measurement §19 said was
missing.

### The `c1` floor is in the field, not the fit

At a 0.5 mm cell, `|c1|` = 0.01241 to five digits whatever the fit sees - scan half-width
2.5%, 1.25% or 0.63%; 11, 21 or 41 ions - and only the mesh moves it, non-monotonically
(0.0107, 0.0124, 0.0108 at 1.0, 0.5, 0.25 mm). So the fit is exact given the field, and the
floor is how the strips' cut cells sit on the lattice at each spacing - the same mechanism as
Amendment 34's plate faces landing on nodes. Refinement alone will not remove it; moving the
strip faces off the cell boundaries, or a mesh whose spacing is not a power-of-two multiple of
the strip pitch, is what to try.

## 23. Resolving power on the full track, and the number that undoes the half-oscillation work

§19 and §20 said the focusing had to be confirmed on the real track. It has been, and the
confirmation failed in the most informative way.

Energy-spread resolving power, 25 oscillations, foil at -4 V, 11 ions, three mirror designs:

| design | `d1` / `d2` / `d3` / `te1` | R at ±2.5% | `\|c1\|` | R at ±0.5% |
| --- | --- | --- | --- | --- |
| shipped | 20 / 50 / 90 / 0 | **70** | **0.285** | 359 |
| Nelder-Mead fit | 22.47 / 42.53 / 89.53 / -0.003 | 60 | 0.338 | 296 |
| CMA-ES fit | 10.47 / 34.68 / 83.99 / +0.029 | 59 | 0.342 | 286 |

**The half-oscillation measured `c1` = 0.012 at the shipped depths. The full track measures
0.29.** Twenty-five times larger, and the two fitted designs - each at R ≈ 36,500 on the half
oscillation - are *no better than shipped* here. So something on the full track adds a
first-order time-energy dependence of order 0.3 that the mirrors' x-focusing does not touch,
and the mirror fit was optimising a quantity the instrument's resolving power does not depend
on. **The fitted depths therefore stay out of the template.** The shipped 20/50/90 is kept, as
a guess that is not worse than the fits.

**The suspect is the foil, by an argument about scaling.** For the bare tilt, the round-trip
drift time is `T_z = 2 v_z0 / a_z` with `a_z = 2 |v| sin(2α) / τ_x` - each reflection delivers
`|v| sin 2α` and there are two per x-period. That gives `T_z = (sin θ / sin 2α) τ_x`: **the
flight time is the x-period times a purely geometric factor**, so the whole track inherits the
mirrors' `c1` and nothing else, and the number of oscillations is fixed by geometry. That is
why a half-oscillation measurement was supposed to suffice. **But the foil's force does not
scale with energy while the tilt's scales as `v²`** (`|v|` per reflection, `|v|/L` reflections
per second), so the balance between them shifts with energy and the drift return time acquires
a `c1` of its own that no mirror can cancel. The discriminating measurement - the same
`T(E)` scan with the foil at 0 V - is running: if `c1` collapses toward 0.01 there, the foil
is the whole story and the mirror fit stands; if it stays at 0.3, something in the track itself
is responsible.

**Either way the lesson is the one §19 drew and §20 under-weighted.** The resolving power of
this instrument is a property of the drift and the mirrors *together*, and a figure measured on
either alone is a figure about a different instrument. The half-oscillation `c1`, the
isochronicity ratio and the full-track R are three different quantities, and only the last is
the one in the register.

### Settled: the foil is the whole gap, and the mirrors carry to the full track exactly

The discriminating scan - `T(E)` on the full track at fixed injection angle, shipped mirrors,
foil off and on:

| foil | T(-2.5%) | T(0) | T(+2.5%) | **c1** |
| --- | --- | --- | --- | --- |
| 0 V | 854.179 | 853.689 | 853.674 | **-0.0118** |
| -4 V | 790.222 | 784.852 | 779.172 | **-0.2816** |

**With the foil off, the full track gives `c1` = -0.012 - the half-oscillation's 0.0124 to the
third digit.** So the mirrors' focusing carries to the whole instrument exactly as
`T_z = (sin θ / sin 2α) τ_x` says it must, the half-oscillation is the right place to measure
a mirror, and the fits of §22 stand as measurements of the mirrors. **The foil adds `c1` ≈
-0.27 on its own**, and that single term is the entire gap between R = 36,500 on the half
oscillation and R = 60-70 on the full track. The device that makes the drift isochronous in
sideways *speed* (§22) makes it non-isochronous in *energy*.

**The mechanism's shape, without a derivation of its sign.** The tilt's sideways deceleration
is `a_z = 2 |v| sin 2α / τ_x`; for a focused mirror `τ_x` is energy-independent, so `a_z`
scales as `|v|`, while the foil's force is a fixed field and scales as nothing. Their balance
therefore shifts with energy and the return time inherits a first-order energy dependence the
mirrors' x-focusing cannot reach. The magnitude and sign are the measurement's: -0.27 at -4 V,
and **-0.243 at the shipped -3 V, measured** (805.03 / 802.77 / 800.35 / 797.87 / 795.31 µs across ±2.5%), so the
template as shipped sits near R ≈ 80 at the acceptance on the full track. An earlier paragraph in this section argued the
tilt force scales as `v²`; that was wrong - the `1/τ_x` is constant for a focused mirror - and
the naive fixed-well argument predicts the wrong sign, so the sign is not understood.

**What this leaves, and it is a fork with two prongs that can be told apart.** The instrument
reaches 100,000 with a foil, so one of two things is true. **Either the mirrors are deliberately
over-focused** to `c1_x` ≈ +0.27, cancelling the foil's term - which is precisely what a
first-order correction vector like `TE1` exists to do, though the paper's `TE1` = 0.01 example
moves `c1` by only 0.01-0.02 and the depths would have to carry most of it; **or the real foil
shape has no energy term**, the drift being made isochronous in speed and energy at once by a
contour this model's pixel measurement does not reproduce - which is what a paper called
"isochronous drift" would be about. The discriminating experiment is a foil-shape optimisation
against *both* conditions on the full track; the discriminating reading is reference [D].

**So the mirror fit was not wasted, and it was not the instrument.** §18 and §22 measure the
mirrors correctly; the instrument's resolving power is the mirrors and the foil together, and
the foil's energy term is now the one number between this model and the register's >100,000.

## 24. The drift's effective potential, derived and then corrected

The two prongs of §23 were to be discriminated by a foil-shape optimisation on the full track.
Before spending hours of flights on a blind search, the problem turned out to have a
derivation - which then turned out to be wrong in an instructive way.

### The scaling argument, and what it predicts

Write the slow drift motion with both mechanisms, at fractional energy `ε`:

- the tilt's deceleration is `a₀√ε` - the ion is reflected more often when faster, and `τ_x` is
  energy-independent for a focused mirror;
- the injection speed is `v_z0√ε`;
- **the foil's potential energy `U(z)` does not scale with `ε` at all.**

Substituting `z = √ε ζ` makes the first two terms scale as `ε` exactly, so the `ζ`-motion -
and therefore the round-trip time - is energy-independent **if and only if `U(√ε ζ) ∝ ε`, that
is `U ∝ z²`**. A harmonic well about the injection point, and nothing else. That is a strong
claim: energy-isochronicity would not be one option among many but a single computable shape.

It also explains the bare-tilt result exactly. With `U = 0` the round trip is
`T_z = τ_x sinθ / sin 2α`, in which every energy has cancelled - which is why §23 measured
`c1` = -0.012 with the foil off, the mirrors' own value and nothing more.

### Tested by quadrature, and the averaging that had to be fixed

`T(ε)` computed by quadrature from the well shape alone, with no new flights:

| foil | `T` predicted | `T` measured | `c1` predicted | `c1` measured |
| --- | --- | --- | --- | --- |
| 0 V | 852.73 µs | 853.69 | **-0.0000** | -0.0118 |
| -3 V, well averaged uniformly in `x` | 775.77 | 800.35 | -0.339 | -0.243 |
| -3 V, well averaged over an **x-period** | **799.98** | **800.35** | -0.100 | -0.243 |

**The 0 V row is the sharp one**: the quadrature contains no mirror physics whatever and
returns exactly zero, so the measured -0.0118 is the mirrors' own `c1` and the two
contributions are separable and additive.

The averaging had to be right. A uniform average over `x` overweights the plates and
overstates the well 4.9-fold; binning the real trajectory by `z` **aliases**, because each
`z`-bin is crossed during a fraction of one `x`-oscillation and therefore samples a nearly
fixed `x`-phase, giving a profile that swings between -0.23 and -10.56 V. The drift
potential exists only as an average over a **full x-period** - 34.13 µs and 26.8 mm of drift
here - and averaged that way it reproduces the flight time to **0.05%** at -3 V and 0.12% at
-4 V.

### The argument is wrong, and the reason is geometric

The quadrature got the *value* right and both *derivatives* wrong - `c1` = -0.100 against
-0.243, and the speed ratio +1.18 against +0.046. That is diagnostic: the well's scale is
right and its response to the varied parameter is not, because **the well is not fixed**.
Measured on unperturbed paths at three energies:

| `dE/E` | x-period | penetration | net rise, injection to reversal |
| --- | --- | --- | --- |
| -2.5% | 34.15 µs | 47.2 mm | 1.0852 V |
| 0 | 34.13 | 45.9 | 1.1700 |
| +2.5% | 34.13 | 44.5 | 1.2191 |

**A faster ion penetrates deeper into the mirrors**, spends more of its time beyond the foil
plates in `x`, and feels a different average - the rise growing about **2.3% per 1% of
energy**. Feeding an energy- and speed-dependent well back into the quadrature gives `c1` =
-0.212 against a measured -0.231 and the speed ratio's collapse from 1.00 to -0.024 against
+0.046, with the -4 V row worse, as a perturbative treatment built on the unperturbed path
should be.

**So `U` depends on `ε` and the scaling argument's premise fails.** A harmonic well in `z`
does not buy energy-isochronicity, because the well's own depth moves with energy. The
mechanism is not a property of the foil's profile along the drift at all; it is the foil's
extent in `x` against the mirrors' penetration depth.

**What that leaves is a question about independence rather than about shape**: the foil's
`x`-extent and its `z`-contour are separate knobs, so if they move the two isochronicity
conditions in different directions, both can be zeroed at once and §23's second prong stands.
That is a 2x2 Jacobian, and it is measurable directly.

## 25. Both prongs fail, and the reason is structural

§23 offered two ways the instrument might reach its resolving power. Both were measured
tonight, and both fail in the shipped parameterisation - which is the useful outcome, because
the reason they fail is the same reason and it is forced.

### The two conditions are nearly collinear in the foil's knobs

The 2x2 Jacobian on the full track, five flights per configuration, foil at the shipped -3 V:

| configuration | T | `c1` | ratio |
| --- | --- | --- | --- |
| nominal | 800.35 µs | -0.2428 | +0.0831 |
| `foilOuterFrac` +0.12 | 791.14 | -0.3321 | -0.2073 |
| `foilInnerAmplitude` +0.025 | 793.64 | -0.1559 | +0.2558 |
| `foilVolts` -1 V | 784.85 | -0.2816 | -0.1080 |

| knob | d(`c1`) | d(ratio) | direction |
| --- | --- | --- | --- |
| `foilOuterFrac` | -0.0893 | -0.2904 | -107.1° |
| `foilInnerAmplitude` | +0.0869 | +0.1728 | +63.3° |
| `foilVolts` | -0.0388 | -0.1910 | -101.5° |

The determinant is +0.0098, non-zero, so the conditions are *formally* independent. But the
two shape vectors are **9.6° from exactly opposed** and the voltage knob is 5.6° from the
first, so all three are very nearly one effective knob. Solving the linear system for the
simultaneous zero asks for `foilOuterFrac` = **1.40** - a plate extending past the mirror
mouth - and an inner-edge amplitude 4.6 times the measured one. Neither is the published
geometry, and a linear extrapolation that far is not to be believed anyway.

### And the mirrors cannot supply what the foil needs cancelled

Walking `d2` from 30 to 74 mm at the published voltages, on the true per-oscillation path:

| `d2` | 30 | 36 | 44 | 50 | 56 | 62 | 68 | 74 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `\|c1\|` | 0.0218 | 0.0062 | 0.0038 | 0.0124 | 0.0265 | 0.0278 | 0.0026 | 0.0871 |
| `\|c2\|` | 1.459 | 0.769 | 0.040 | 0.448 | 0.742 | 0.988 | 1.353 | 2.279 |

**The mirrors' whole range along this axis is ±0.09 and the foil needs +0.24 cancelled** -
a third of the way. `TE1` can supply the rest arithmetically (at the measured sensitivity,
`TE1` ≈ 0.11), but the `te1` scan of §21 shows `c2` climbing monotonically as it does, reaching
0.83 by `te1` = 0.02, so the cancellation is bought at a second-order cost that is worse than
the first-order gain.

### Why they fail together: the foil's benefit and its defect are the same quantity

Both failures have one cause - though **the mechanism first written here had the sign
backwards, and §27 corrects it**. What dominates is that a faster ion reaches further along
the drift (`z_rev` goes as the square root of energy) and so climbs more of the foil's own
rise, decelerating more and arriving sooner: a *negative* `c1`, which is what is measured.

The time-split effect is real and was measured directly - **the fraction of each oscillation
the ion spends inside the mirrors grows with energy**, 0.3406, 0.3489, 0.3556 across
plus or minus 2.5%, or +88% per unit `dE/E`, with penetration 47.2, 45.9, 44.5 mm. That holds
*even for a perfectly focused mirror*, because focusing fixes the total period and says
nothing about how it divides between mirror and drift. But the foil acts only during free
flight, so less free flight at higher energy means *less* foil action, a *longer* flight, and
a **positive** `c1` - opposed to the dominant term, and smaller.

What survives unchanged is the empirical part, because the Jacobian was measured rather than
derived: the benefit and the defect scale together, since both are set by how much foil the
ion sees, and that is why its two columns are 9.6 degrees apart rather than orthogonal -
shrinking the foil buys the second only by giving up the first.

**So the energy term is structural, not a defect of the measured contour**, and the model as
it stands cannot reach the published resolving power by either route. Something in the real
instrument breaks the proportionality, and the guessed geometry is where to look.

### The published `C(1)` sensitivity is a constraint on the depths, not a check

The one unfitted published number now earns a second job. The crowd-control paper's Figure 2
defines the quantity outright - `(t|e) = T⁻¹ ∂T/∂ε` - and the text states that `TE1` = 0.01
shifts it by about 2.5 ppm/V; the beam is stated as 4 keV. So `dc1/dTE1` = 1.0, and the
measured 2.2 is neither a units error nor a definitional half. **It is a statement that the
mirror geometry is wrong**, because how much timing a given voltage perturbation buys depends
on where the electrodes are. Fitting `d1..d4` to reproduce `dc1/dTE1` = 1.0 uses a published
number that no other part of this model has touched, and it constrains exactly the four
quantities that are guesses. That is the next fit, and it is better posed than anything
tried so far: a published target, an unfitted residual, and the same parameters that must
also carry `c2`.

## 26. The published `C(1)` sensitivity picks a depth

§25 turned the one unfitted published number into a constraint. Measured against each of the
four depths in turn, on the true per-oscillation path, with `dc1/dTE1` taken as
`(|c1|(+0.03) + |c1|(-0.03)) / 0.06` - exact for `|c1| = |s(te1 - t0)|` whenever the vertex
lies inside the window, which matters because `c1` changes sign within it and a naive
difference would read the sign flip as signal:

| knob | across its range | `dc1/dTE1` |
| --- | --- | --- |
| `d1` | 12 → 20 → 30 mm | 2.664 → 2.670 → 2.716 |
| **`d2`** | 40 → 50 → 62 mm | **1.116** → 2.670 → 4.451 |
| `d3` | 78 → 90 → 104 mm | 3.414 → 2.670 → diverges (`\|c2\|` = 22.6) |
| `d4` | 112 → 130 → 150 mm | 19.399 → 2.670 → 2.523 |

**`d1` is inert** - three per cent across a 2.5-fold change - so it does not control this
quantity at all. `d3` and `d4` move it but destructively, `d3` into a solve whose `c2` is
22.6 and `d4` into 19.4 at 112 mm. **`d2` is the knob**, monotonic and steep, and it reaches
the published 1.0 near **40 mm** against the guessed 50. `|c2|` there is 0.310, better than
the shipped 0.448, so the constraint does not have to be bought with second order.

This is the first quantity in the whole reconstruction that pins an unpublished dimension
against a published number **without any fitting**: the sensitivity was measured before the
depth was chosen, and the depth follows from it.

**What it is worth depends on the next measurement.** The foil's energy term arises from how
each oscillation divides between mirror and drift (§25), and `d2` moves the penetration - so
the geometry the published number selects may or may not also shrink the -0.24 that has to be
cancelled. Those are logically independent, and measuring `c1_foil = c1(foil on) - c1(foil
off)` at both depths on the full track settles it. If the term shrinks, the two published
constraints agree with each other and the reconstruction closes; if it does not, `d2` = 40 mm
is a correct depth in an instrument that still cannot reach its resolving power, and the
remaining error is in `d3`, `d4`, the tilt baseline, or the foil's extent.

## 27. `d2` = 38.0 mm, and the sign that corrects §25

### The depth the published number picks

Refining `d2` against `dc1/dTE1` = 1.0, on the true per-oscillation path:

| `d2`, mm | 34 | 36 | **38** | 40 | 42 | 46 |
| --- | --- | --- | --- | --- | --- | --- |
| `dc1/dTE1` | 0.736 | 0.909 | **1.022** | 1.116 | 1.444 | 2.130 |
| `\|c2\|` | 0.979 | 0.769 | 0.530 | 0.310 | **0.142** | 0.201 |

**`d2` = 38.0 mm reproduces the published sensitivity to 2 per cent**, against a guess of
50 mm. Taking the paper's "about 2.5 ppm/V" as good to a fifth puts the constraint at
**38 +2.5 / -3 mm**. This is the only unpublished dimension in the whole reconstruction fixed
by a published number with no fitting anywhere in the chain: the sensitivity was measured
before the depth was chosen, and the depth follows.

`|c2|` falls monotonically across the same range and bottoms at 42 mm, four millimetres away.
That tension is not a problem but a use for the other three depths, which have so far been
held at their guesses - `d1` is inert for this quantity (§26), so `d3` and `d4` are where the
second condition has to be met.

### The mechanism in §25 had the sign backwards

§25 attributed the foil's energy term to the mirror/drift time split. That split is real and
was measured directly - `t_mirror/T` = 0.3406, 0.3489, 0.3556 across ±2.5% of energy, **+88%
per unit `dE/E`**, and a first-order focused single-stage mirror satisfies `t_free = t_mirror`
exactly, which is the four-penetration-depth rule this project already records from another
direction.

But its **sign is wrong for the observation**. Less free flight at higher energy means *less*
foil action, a *longer* flight, and a **positive** `c1`; the measurement is -0.23. So the
dominant term is the other one, and it is §24's original scaling argument after all: the
reversal point goes as `√ε`, so a faster ion climbs further up the foil's own rise, decelerates
more, and arrives sooner. Negative, and larger.

**So the two mechanisms oppose**, which is worth more than either alone:

- it explains why the fixed-well quadrature of §24 got -0.100 where the measurement is -0.231
  and the energy-dependent well took it to -0.212 - the corrections are not independent
  contributions to be added but partly cancelling ones;
- it means a **harmonic well would kill the dominant term and leave the secondary**, whose
  sign is opposite - so the energy-isochronous foil is not simply `U ∝ z²` but `U ∝ z²`
  detuned enough to leave a small negative residue against the time-split's positive one;
- and §24's conclusion that "a harmonic well does not buy energy-isochronicity" is too strong.
  It buys most of it. What remains is a second, smaller, opposite-signed term that a slight
  detuning can absorb.

§25's empirical finding is untouched by this: the Jacobian's 9.6° was measured, not derived,
and the benefit and the defect still scale together because both are set by how much foil the
ion sees. What changes is that the residual after a harmonic well is now expected to be small
and of known sign, which makes the foil-shape problem better posed than §25 concluded.

## 28. The constraint a single reflection cannot see

§26 and §27 picked `d2` = 38 mm from the published `C(1)` sensitivity, and §27 called it the
first unpublished dimension fixed by a published number without fitting. Flown on the full
track it is a mirror the ion does not survive.

| `d2` | `dE` | T | reflections | outcome |
| --- | --- | --- | --- | --- |
| **38** | -2.5% / 0 / +2.5% | 457 / 439 / 475 µs | 25 / 24 / 26 | **StruckElectrode** |
| **40** | -2.5% / 0 / +2.5% | 737 / 795 / 888 | 41 / 45 / 50 | **StruckElectrode** (two of three) |
| 50 | -2.5% / 0 / +2.5% | 854.18 / 853.69 / 853.67 | 50 / 50 / 50 | arrives |

**The ion is lost transversely.** At `d2` = 38 it ends at `y` = -20.0 mm - on the board -
after 24 reflections, deep inside the far mirror at `x` = 642 mm. The mirror is not confining
in `y` over the length of the track.

**So §23's `c1_foil` comparison at `d2` = 40 was measuring the time to hit a rod.** It reported
`c1` = +3.80 and `c1_foil` = +1.90; both are meaningless. The tell was in the same table and I
should have read it first: the bare-tilt speed ratio came out **1.95** where §13's closed form
requires exactly 1.0, and a ratio that is not 1.0 for a bare tilt says the flight is not the
flight being modelled. `half.json` and `full.json` were checked afterwards and agree in every
geometry parameter, so this is physics and not a script mismatch.

### What it means for everything measured on a half oscillation

**A single reflection cannot see transverse stability.** `c1`, `c2`, `c3`, the `C(1)`
sensitivity, the whole depth scan of §26 and the refinement of §27 were all measured on one
reflection, where an ion has no time to walk off axis. Every one of those numbers is correct
for what it measures and none of them can tell whether the geometry is *flyable*. The
half-oscillation proxy was justified in §23 by `c1` carrying to the full track exactly - and
it does, but only among geometries that survive.

That adds a third constraint on the four depths, and it is a hard one rather than a
preference:

1. `dc1/dTE1` = 1.0, published, and `d2` is the only depth that controls it (§26);
2. `c2` small, a design requirement, minimised near `d2` = 42;
3. **transverse confinement over 50 reflections**, physically necessary, and satisfied at
   `d2` = 50 but not at 38 or 40 with the other three depths left at their guesses.

Constraints 1 and 3 are in direct conflict along the `d2` axis alone. Since `d1` is inert for
constraint 1, `d1`, `d3` and `d4` are free to buy constraint 3 back, and whether they can is
the measurement now running - the survival of the full track at `d2` = 38 while each of the
other three is moved in turn.

### d3 buys survival back, and it is the only depth that can

Testing whether the other three depths can restore the full track at `d2` = 38 mm - one
flight each, foil off:

| geometry `d1/d2/d3/d4` | T | final `y` | outcome |
| --- | --- | --- | --- |
| 20/38/90/130 | 439.1 us | -20.0 mm | struck |
| **20/38/84/130** | **855.0** | **-0.0** | **arrives, 50 reflections** |
| 20/38/78/130 | 627.0 | +20.0 | struck |
| 20/38/96/130 | - | -3362.6 | escaped the analyser |
| 20/38/104/130 | 210.8 | -20.0 | struck |
| 12 / 16 / 26 / 32 for `d1` | - | -20 / +20 / -7089 / -21.4 | struck, struck, escaped, struck |
| 115 / 122 / 140 / 152 for `d4` | - | -2553 / -20 / +20 / +20 | escaped, struck, struck, struck |

And the survival boundary along `d2` alone, with the others at their guesses: struck at 38,
arriving at 42, 44, 46, 48 and 50.

**`d3` is the only one of the three that rescues it**, and only in a narrow window - 84 mm
works, 78 and 104 strike, 96 lets the ion out of the analyser entirely. `d1` fails at every
value tried, which is consistent with §26 finding it inert for the timing sensitivity as well:
`d1` is the shallowest strip and the ion barely reaches it.

So **20/38/84/130 is a flyable geometry at the depth the published number selects**, which is
the first candidate that satisfies the published constraint and the physical one together.
Whether it still satisfies the published constraint after `d3` moved - the sensitivity depends
on all four depths, and `d3` = 90 gave 2.67 at `d2` = 50 - is the measurement in flight, along
with the `c1_foil` the whole fork turns on.

**The methodological lesson generalises past this instrument.** A figure of merit measured on
a fraction of the device is a statement about that fraction. Where the device's whole point is
that a small per-pass effect accumulates - which is what a multi-reflection analyser is - the
accumulated failure modes are invisible to the per-pass measurement, and they are not
subtleties: here it is the difference between an instrument and a beam dump.

## 29. The flyable geometry, and the foil term that does not move

`20/38/84/130` satisfies all three constraints at once - the first geometry in this
reconstruction to do so:

| | value | against |
| --- | --- | --- |
| survives 50 reflections | T = 855.03 us, all energies arrive | the constraint of section 28 |
| `dc1/dTE1` | **1.071** | published 1.0, within 7% |
| `\|c2\|` | **0.0939** | 0.448 at the shipped depths, 4.8x better |
| bare-tilt speed ratio | **0.9949** | the closed form's exact 1.0 |

That last row is the check section 28 added and it passes here, so unlike the `d2` = 40 flights
this one is the flight being modelled. Moving `d3` from 90 to 84 mm did not cost the published
sensitivity; it improved second order fivefold *and* restored the track.

### But the foil's energy term is indifferent to the mirrors

| geometry | `c1` mirrors alone | `c1` with foil | **`c1_foil`** |
| --- | --- | --- | --- |
| 20/50/90/130, shipped | -0.0118 | -0.2428 | **-0.2310** |
| 20/38/84/130, flyable | -0.0322 | -0.2558 | **-0.2236** |

**Unchanged to 3 per cent** across a geometry change that moved two depths by 12 and 6 mm,
improved `c2` fivefold and halved the timing sensitivity. So the foil's energy term is a
property of the foil and the drift, not of the mirror - which is the cleanest evidence yet
that the mirrors are not where the resolving power is lost.

**And it is the whole limit.** With `|c1|` = 0.256 the acceptance-wide resolving power is
about 77 whatever `c2` does, since `c1 s` = 6.4e-3 against `c2 s^2` = 5.9e-5, a factor of 108.
Reaching 100,000 across the published +/-2.5% needs `|c1| < 2e-4`: **the foil's term must be
cancelled to one part in a thousand of itself.** Nothing measured tonight can do that - the
mirrors' whole range is 0.09, the foil's own shape knobs are 9.6 degrees from opposed, and
`TE1` buys `c1` only at a `c2` cost.

So either the real foil's energy term is orders of magnitude smaller than this model's, or
the real instrument cancels it by a mechanism not yet identified. The scaling argument of
section 24 says which foil would have no term at all - one whose potential rises as `z^2` from
the injection point, because that is the single profile making every term in the drift
equation scale with energy together. The foil's sixteen slices can carry any voltage law, so
that well is buildable directly: a flat plate at `foilVolts (1 - f^2)`, rising from the bias
at injection to zero at the far end. Testing it against uniform and linear controls is the
measurement in flight, and it is a sharp one - the prediction is not a smaller `c1` but a
collapse.

## 30. The energy term changes sign, so a zero exists

Section 24's scaling argument named the one foil profile with no energy term: a potential
rising as `z^2` from the injection point. The foil's sixteen slices can carry any voltage
law, so it is buildable directly - a flat plate (contour removed, so the law *is* the well) at
`phi = V (1 - f^2)`, `f = slice/(slices-1)`. Flown on `20/38/84/130`, against uniform and
linear controls:

| profile | V | T | **`c1_foil`** | speed ratio |
| --- | --- | --- | --- | --- |
| off | 0 | 855.03 us | - | +0.9949 |
| uniform | -3 | 807.32 | **-0.3659** | -0.3393 |
| linear | -6 | 747.30 | **+0.1092** | +1.0190 |
| quadratic | -6 | 725.23 | **+0.0793** | +0.8209 |
| quadratic | -12 | 645.12 | **+0.0683** | +0.4791 |

**The energy term changes sign with the voltage law**, from -0.366 uniform to +0.109 linear.
That is the finding: a zero lies between them, so a foil with no energy term exists inside
this parameterisation and does not need a plate past the mirror mouth or an inner edge 4.6
times the measured one. The speed ratio changes sign over the same span, from -0.339 to
+1.019.

Two things are worth noting before the search. **The measured contour was helping**: a flat
uniform plate gives -0.366 where the pixel-measured contour at the same bias gives -0.224, so
the published shape is already doing part of this job - which is a point in favour of the
contour being the real instrument's answer rather than an artefact of my pixel reading. And a
*quadratic* law does not by itself zero the term - it overshoots to +0.079 - which is the
expected consequence of section 27's correction: the harmonic well kills the dominant term and
leaves the smaller, opposite-signed time-split residue, so the profile that zeroes the total
is quadratic **detuned**, exactly as predicted there.

**This reopens what section 25 closed.** That section concluded both routes fail because the
foil's benefit and defect are 9.6 degrees from proportional. That Jacobian was measured over
the *shape* knobs - `foilOuterFrac`, `foilInnerAmplitude`, `foilVolts` - and remains true of
them. The **voltage law along the drift** is a knob section 25 never tested, and it moves the
two quantities very differently: uniform to quadratic swings `c1_foil` by 0.445 while the
ratio swings 1.16, in the same direction but at a ratio of 2.6 rather than the near-collinear
1.05 of the shape knobs. A two-parameter search over bias and quadratic fraction is now
running, and unlike section 25's it has room to converge.

### The inverse problem is exactly determined

Four published quantities against four free parameters, and the pairs separate:

| condition | published | knob |
| --- | --- | --- |
| `c1_foil` = 0 | implied by R > 100,000 | foil voltage law `V`, `g` |
| speed ratio = 0 | implied by the same | foil voltage law `V`, `g` |
| `T` = 779 us | stated | injection angle and tilt |
| `z_rev` = 335 mm | stated (310-360) | injection angle and tilt |

The geometry pair separates cleanly. For a constant axial force `T ~ sin(theta)/sin(2 alpha)`
and `z_rev ~ sin^2(theta)/sin(2 alpha)`, so their ratio is `sin(theta)` alone: the injection
angle is fixed by the two published numbers, foil or no foil, and the tilt follows. Checked
against the flight - `v_z0 = 4 z_rev / T` gives **1567.8 m/s against the trajectory's
1569.6**, 0.11%, and `theta` = 2.287 degrees against the template's 2.29.

**That matters because the foil law costs flight time.** Deepening the bias shortens `T`
sharply - 794 us at -4 V, 718 at -12, and further still beyond - so a foil law chosen for the
two isochronicity conditions will not leave `T` at 779. The geometry knobs absorb that at
fixed drift distance, which is why the problem is determined rather than over-determined, and
why `T` should not be used to reject a foil law.

## 31. The gap closes with bias, and the published maximum is where it lands

Scanning `phi = V (1 - g f^2)` on a flat plate over bias and quadratic fraction, on
`20/38/84/130`, reading off where each condition is met:

| `V` | `c1_foil` = 0 at | ratio = 0 at | **gap in `g`** |
| --- | --- | --- | --- |
| -4 | 0.751 | 0.377 | 0.374 |
| -8 | 0.707 | 0.540 | 0.167 |
| -12 | 0.710 | 0.585 | 0.125 |
| -16 | 0.641 | 0.577 | 0.064 |
| **-20** | 0.639 | 0.582 | **0.057** |
| -26 | 0.616 | 0.587 | 0.029 |

**The gap closes as the bias deepens**, roughly as `1/|V|`, so the two conditions become
compatible in the same limit rather than at some isolated point. At the published maximum of
**-20 V**, taking the quadratic fraction that zeroes the speed ratio leaves `c1_foil` = -0.034
- a **sevenfold reduction** from the -0.231 the shipped foil carries, with the drift fully
isochronous in speed.

**And at that point the mirrors' own `c1` is no longer negligible**: -0.032 against the foil's
-0.034. Which is exactly the situation `TE1` exists for, at the sensitivity this reconstruction
already pinned to the published value (`dc1/dTE1` = 1.071, section 29). So the closing move is
three knobs, all inside published ranges: the foil bias at its stated maximum, the quadratic
fraction of its voltage law, and the first correction vector.

**A caveat on the trend, stated because it is the weak point.** The gap goes as `1/|V|` and
therefore approaches zero asymptotically rather than crossing it - at -26 V it is still 0.029
and the extrapolation does not obviously reach zero at any finite bias. So the expected
outcome is not both conditions exactly zero but both small, with `TE1` absorbing what is left
of the first-order term. Whether "small" is small enough is a resolving-power measurement, not
an argument, and that is what runs next.

**What is given up to get here.** The plate is flat: `foilInnerAmplitude` = 0, the
pixel-measured contour removed, with the voltage law doing the work the shape did. That is a
*different foil* from the published one, and section 30 already noted the contour was helping
- a flat uniform plate is worse than a contoured one at the same bias. So this is a
demonstration that a foil law exists which meets both conditions, not a claim about how the
real instrument is built. Combining the measured contour with a graded law is the obvious next
parameterisation and has not been tried.

## 32. Both conditions nearly closed, at the published maximum bias

Scanning the quadratic fraction and the mirror trim at `V` = -20 V, the published maximum,
on `20/38/84/130`:

| `g` | `te1` | T | `c1_total` | speed ratio | R, first order |
| --- | --- | --- | --- | --- | --- |
| 0.57 | 0 | 655.1 us | -0.0650 | -0.0202 | 197 |
| 0.57 | 0.05 | 669.0 | -0.0982 | -0.2088 | 42 |
| 0.60 | 0 | 649.6 | -0.0421 | +0.0715 | 117 |
| 0.60 | 0.05 | 665.3 | -0.0336 | -0.0931 | 99 |
| 0.63 | 0 | 643.9 | -0.0316 | +0.1500 | 66 |
| **0.63** | **0.05** | **660.8** | **+0.0197** | **+0.0085** | **570** |
| any | 0.10 | 304.3 | ion lost | ion lost | - |

**`c1` = +0.020 and the speed ratio +0.0085**, both nearly closed, with every knob inside a
published range: bias at the stated maximum, and a first correction vector at `TE1` = 0.05.
Against the shipped configuration's ~77 that is 7.4 times better.

Three things the table says beyond the best point. **`TE1` = 0.10 loses the ion at every `g`**,
so the mirror trim has a usable range of roughly 0 to 0.08 and cannot be leaned on harder -
which is consistent with section 25's finding that buying `c1` from `TE1` costs second order,
now with a hard edge rather than a gradient. **The surface is not simple**: at `g` = 0.57 the
same trim makes the ratio ten times worse, so the two knobs interact and a 3x3 grid is not a
search. And **flight time has fallen to 661 us** against the published 779, which section 31's
argument says the injection angle and tilt should absorb at fixed drift distance - untested.

**570 is a first-order figure and is probably wrong.** The drift's second-order term was
-6.5 for the contoured foil (section 22), and at a 4.5% thermal spread `b (dv/v)^2` alone is
1.3e-2, which caps R near 38 - fifteen times below the first-order estimate. Either this foil
law has a much smaller `b`, or the 570 is meaningless. A five-point expansion in each
direction plus a direct thermal-cloud flight settles it, and is running.

## 33. First-order closure buys nothing: the second-order drift term binds

The full expansion at section 32's best point, five samples in each direction rather than
three:

| term | value | contribution to arrival spread |
| --- | --- | --- |
| `c1` energy, first order | +0.0197 | 4.93e-4 |
| `c2` energy, second order | -0.169 | 1.05e-4 |
| `a` speed, first order | +0.0215 | 9.68e-4 |
| **`b` speed, second order** | **-3.597** | **7.28e-3** |

**R from the full expansion is 56, not the 570 the first-order figure implied**, and a direct
thermal cloud gives **29.5**. So closing both first-order conditions bought nothing: the
second-order drift term is now 15 times the first-order one, and it was always going to be -
section 22 measured `b` = -6.5 for the contoured foil and the consequence was written down
there and then not carried forward.

**The comparison is worse than a null result and I cannot yet call it either way.** The
shipped configuration measured R = 70.3 on 24 ions; this one gives 29.5 on 12. Different ion
counts, both below the hundred that `ENSEMBLE_SMALL` warns at, and my practice of summing the
absolute terms disagrees with the cloud in **both directions** - it predicted 22 for the
shipped case that measured 70, and 56 for this one that measured 30. So the estimator is not
calibrated and a twelve-ion peak cannot separate 30 from 70. An equal-ion-count comparison is
the only honest way to say whether the optimised law is better or worse, and it has not been
run.

**Why `b` is hard to remove, and it is structural.** A purely harmonic well has `b` = 0
exactly, because a harmonic oscillator's period is amplitude-independent. But the drift's
total potential is the tilt's *linear* term plus the foil's, and linear-plus-quadratic is
simple harmonic motion about a **shifted** centre - the ion starts away from that centre, and
its return time to the starting plane then does depend on amplitude. `b` vanishes only if the
foil dominates the reversal outright, and section 22 established it does not: the reversal
point is 335 mm with the foil and without it, so the tilt sets the turning point and the foil
only reshapes the timing.

**The requirement, stated so it can be aimed at.** For R = 100,000 the total arrival spread
must be below 5e-6. With a 4.5% thermal spread in `v_z` that needs `b` below **2.5e-3**,
against the -3.597 measured - a factor of 1400. Two things could supply it and both are now
worth testing rather than arguing: a foil coupling strong enough to dominate the reversal
(the plates sit at the boards, 20 mm from the beam, and moving them closer raises the well per
volt without exceeding the published bias), or a `v_z` spread far below thermal, which is
exactly what the "pair of rectangular einzel lenses" in the published beam line would do -
trading angular spread for beam width at constant emittance, with room for about a tenfold
reduction inside the 40 mm board gap. The first is a scan now running; the second is a
statement about the injection optics that this model does not contain.

## 34. The foil is one-dimensional for timing, and the reason names the fix

Mapping the second-order drift term against the foil's remaining knobs, five-point angle
scans on `20/38/84/130` with `te1` = 0.05:

| case | `a` first order | `b` second order | `a` term | `b` term |
| --- | --- | --- | --- | --- |
| V -20, g 0.63, gap 20 mm | **+0.0215** | -3.597 | 9.7e-4 | 7.28e-3 |
| V -20, g 0.90, gap 20 | +0.3066 | **+0.748** | 1.38e-2 | 1.51e-3 |
| V -26, g 0.63, gap 20 | +0.0379 | -2.393 | 1.7e-3 | 4.85e-3 |
| V -20, g 0.63, gap 12 | +0.3076 | **-0.640** | 1.39e-2 | 1.30e-3 |
| V -20, g 0.63, gap 8 | +0.3487 | +6.881 | 1.57e-2 | 1.39e-2 |

**`b` changes sign twice** - through `g` between 0.63 and 0.90, and through the plate gap
between 12 and 8 mm - so `b` = 0 is reachable two independent ways. But **every route raises
`a` fourteenfold**, and the three knobs move `(a, b)` within 5 degrees of a single direction:
`g` at 86.2, the gap at 84.5, the bias at 89.2. The total drift spread is therefore
*minimised near the starting point*, and neither extreme helps.

**That is the third time a near-collinear Jacobian has appeared in the foil's parameters**
(section 25 over the shape knobs, section 31 over bias against quadratic fraction, and now
over bias, quadratic fraction and plate gap). The pattern is worth naming: **as far as the
drift's timing is concerned the foil has one degree of freedom, its coupling strength**, and
every geometric or electrical parameter merely scales it.

### Why, and what the reason implies

The law tested throughout was `phi = V (1 - g f^2)`, whose **force is zero at the injection
plane** - the derivative of `f^2` vanishes at `f` = 0. So the tilt's constant force is
uncancelled there, and the total potential is constant-plus-linear-force: simple harmonic
motion about a centre **displaced to negative z**, with the ion starting away from that centre.
The return time to the starting plane then depends on amplitude, which is exactly `b` not equal
to zero, and pushing the quadratic term harder moves the centre without ever putting the ion
at it.

**Inject at the well's centre and both terms vanish together.** If the total axial force is
zero at `z` = 0 and linear in `z`, the drift is simple harmonic *about the injection plane*,
the return to `z` = 0 is exactly half a period, and a half period is amplitude-independent -
so `a` = 0 and `b` = 0 are the same condition rather than a trade. The requirement is a
**linear** term in the foil law large enough to cancel the tilt at injection:

    phi = V (1 - c f - g f^2)

with `c` setting the force at the injection plane and `g` the restoring curvature. Estimated
from the tilt's measured deceleration of 3.68e6 m/s^2 and the fivefold reduction the x-average
imposes (section 24), `c` should be of order one. That scan is running, and unlike the three
before it the two conditions are not expected to trade, because they are the same condition
seen twice.

## 35. The sign of the linear term, got wrong and then measured

Section 34 predicted that a linear term in the foil law would cancel the tilt at the injection
plane and make `a` and `b` vanish together. Scanned at `V` = -20:

| `c` | `g` | T | `a` | `b` | drift spread |
| --- | --- | --- | --- | --- | --- |
| **0** | 0.63 | 660.8 us | **+0.0215** | -3.597 | **8.3e-3** |
| 0.40 | 2.00 | 440.2 | +0.2162 | -0.916 | 1.16e-2 |
| 0.90 | 1.20 | 458.3 | +0.3667 | -0.795 | 1.81e-2 |
| 1.60 | 1.20 | 397.8 | +0.4023 | +0.165 | 1.84e-2 |

**Every point is worse than `c` = 0, and `a` grew tenfold instead of vanishing.** The flight
time is the tell: 661 us at `c` = 0 falling to 398 as `c` rises, so the added term is
*decelerating* the ion further rather than cancelling anything. With `phi = V (1 - c f)` and
`V` negative, `phi` **rises** along the drift for positive `c`, and a rising potential
decelerates a positive ion - the same direction the tilt already pushes. **The linear term
needs the opposite sign.**

The magnitude follows from quantities already measured rather than being scanned for. The
tilt's deceleration is 3.68e6 m/s^2 (section 24, from the bare-tilt reversal and flight time),
so as a potential gradient it is `a0/(q/m)` = 3.68e6 / 1.93e5 = **19.1 V/m**. The foil must
supply that with the opposite sign at `z` = 0, and the x-average costs a factor of about five
(section 24), so `c` should be of order **-1.6 to -2.5** on the plate. That scan is running.

**Two things worth keeping from getting it wrong.** The physical argument in section 34 is
untouched - injecting at the well's centre still makes `a` and `b` the same condition, and the
scan did not test that claim, it tested a law that moved the centre *further away*. And the
diagnostic that caught it was the flight time, not the coefficients: a term meant to cancel a
decelerating force should lengthen the flight, and this one shortened it by 40 per cent. **When
a knob is supposed to oppose something, check the sign of its first-order effect on the
simplest observable before reading its second-order effect on the interesting one.**

## 36. Centring the well works: the trade breaks

With the linear term's sign corrected, at `V` = -20 on `20/38/84/130`:

| `c` | `g` | T | `a` | `b` | `a` term | `b` term | drift spread |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **0** | 0.63 | 660.8 us | +0.0215 | -3.597 | 9.7e-4 | 7.28e-3 | **8.3e-3** |
| -0.50 | 0.60 | 605.9 | -1.0038 | +3.510 | 4.52e-2 | 7.11e-3 | 5.23e-2 |
| -0.50 | 1.50 | 577.0 | +0.1628 | +0.760 | 7.33e-3 | 1.54e-3 | 8.87e-3 |
| -0.50 | 3.00 | 436.1 | +0.0583 | +0.351 | 2.62e-3 | 7.11e-4 | 3.34e-3 |
| -1.20 | 3.00 | 482.2 | **+0.0509** | **-0.381** | 2.29e-3 | 7.72e-4 | **3.06e-3** |
| -2.50 | 3.00 | 559.6 | -0.1953 | +0.249 | 8.79e-3 | 5.04e-4 | 9.29e-3 |

**The trade is broken.** At `c` = -1.2, `g` = 3.0 the second-order term is **nine times
smaller** than the baseline's -3.597 while the first-order term stays small - where every
previous knob bought one only by surrendering the other. The total drift spread falls 2.7-fold,
from 8.3e-3 to 3.06e-3, which is the first real improvement in the drift since the foil was
introduced.

That is section 34's argument confirmed with the sign fixed: putting the ion nearer the well's
centre makes `a` and `b` stop opposing each other, because a half period is
amplitude-independent whatever the amplitude is. It also confirms the argument was right about
*mechanism* while wrong about *implementation*, which is worth separating - the physics was
sound and the algebra was not.

**Two limits are visible in the table and neither is yet reached.** `g` = 3.0 is the **edge of
the scan** and is best at both values of `c` tried there, so the optimum lies outside; a scan
to `g` = 8 is running. And the flight time has fallen to 436-482 us against the published 779,
which section 31's separation says the injection angle and tilt should absorb at fixed drift
distance - stated there, still untested, and now the largest untested assumption in the chain.

**A caveat on the law's reach.** At `c` = -1.2, `g` = 3.0 the plate potential runs from -20 V
at injection to `V (1 - c - g)` = **+64 V** at the far end. That is a graded electrode spanning
84 V, not a plate at a bias inside the published 0 to -20 V window, so this is no longer the
published foil in either shape or excitation. What it demonstrates is that the drift's timing
*can* be made nearly isochronous to both orders by an axial potential of the right form; how
the real instrument realises that form is a separate question, and the published bias range
says it is not by this route.

## 37. A first-order term that nearly vanishes, and a scan edge I built myself

Extending the scan at `V` = -20 on `20/38/84/130`:

| `c` | `g` | T | `a` | `b` | drift spread |
| --- | --- | --- | --- | --- | --- |
| 0 | 0.63 | 660.8 us | +0.0215 | -3.597 | 8.3e-3 |
| -0.80 | 3.00 | 454.3 | +0.0823 | +0.893 | 5.51e-3 |
| **-1.50** | **3.00** | **502.8** | **+0.0060** | **-0.712** | **1.71e-3** |
| -2.20 | 3.00 | 548.9 | +0.0760 | +0.917 | 5.28e-3 |
| any | 5.00, 8.00 | - | - | - | run failed |

**`a` = +0.0060 at `c` = -1.5**, thirty-five times smaller than at `c` = 0 and the smallest
first-order drift term measured anywhere in this work, with `b` = -0.712 - five times smaller
than baseline. The total drift spread is **1.71e-3, a 4.9-fold improvement**, and `c` = -1.5
is a clear interior optimum bracketed by -0.8 and -2.2 on both sides.

**The `g` = 5 and 8 rows did not fail physically.** They returned no flight time at all, which
is a validation refusal rather than a lost ion: I had declared `foilQuad` with bounds of
plus or minus 4, so every value above 4 was refused before a solve began. **The scan edge I
spent the previous section describing as physical was one I had set myself**, in the same
script, eight lines above the scan. Worth stating because the failure mode is
indistinguishable from a real one in the output table - both print as a missing row - and the
distinguishing evidence was in the outcome field, which showed `None` rather than
`StruckElectrode`. A refused model and a lost ion should never look alike in a results table,
and in mine they did.

Bounds widened to plus or minus 20 and the scan is running again over `g` up to 7. The
interior optimum in `c` is real regardless, since it was bracketed within the valid range.

## 38. Both drift orders cross zero at the same place

Rescanned with the bounds fixed, at `V` = -20 on `20/38/84/130`:

| `c` | `g` = 3.0 | `g` = 4.5 | `g` = 7.0 |
| --- | --- | --- | --- |
| -1.20 | `a` +0.0509, `b` -0.381, **3.06e-3** | +0.0509/-0.550, 3.19e-3 | -0.0905/+0.560, 5.21e-3 |
| -1.50 | `a` +0.0060, `b` -0.712, **1.71e-3** | -0.0544/+0.389, 3.24e-3 | -0.0740/+0.249, 3.83e-3 |
| -1.90 | `a` -0.0039, `b` +0.826, **1.85e-3** | -0.0371/+0.929, 3.55e-3 | -0.0956/+0.247, 4.80e-3 |

**Both terms change sign between `c` = -1.5 and -1.9**: `a` from +0.0060 to -0.0039, `b` from
-0.712 to +0.826. Interpolating, `a` = 0 at **`c` = -1.74** and `b` = 0 at **`c` = -1.68**.

**They are the same condition, to four per cent in `c`.** That is section 34's argument
confirmed quantitatively rather than in sign only: when the ion sits at the well's centre the
return is half a period, a half period is amplitude-independent, and both orders of the
amplitude expansion vanish together. Every earlier attempt traded them because the well's
centre was elsewhere; here there is a single value of one parameter at which both go to zero.

**Also, `g` = 3.0 is best at every `c`, so the optimum in `g` is at or below 3.** The previous
section's claim that it lay beyond the scan edge was wrong twice over - wrong because the edge
was my own parameter bound rather than physics, and wrong because larger `g` is worse anyway.
The bounds bug cost nothing except a paragraph of mistaken reasoning.

A fine scan of `c` from -1.62 to -1.78 against `g` from 2.2 to 3.0 is running. The expectation
is a drift spread well below the present 1.71e-3, limited by whatever the *third* order does -
and if the well is genuinely harmonic about the injection plane there is no third order in the
amplitude either, so the limit should be the well's departure from quadratic rather than the
expansion itself.

## 39. The drift coefficients are at the solve's numerical floor

The fine scan of section 38 does not confirm its own prediction, and the failure is
informative. At `g` = 3.0:

| `c` | -1.50 | -1.62 | -1.70 | -1.78 | -1.90 |
| --- | --- | --- | --- | --- | --- |
| `a` | +0.0060 | -0.0278 | -0.0412 | -0.0333 | -0.0039 |
| `b` | -0.712 | -0.311 | +0.587 | +1.051 | +0.826 |

`a` was interpolated in section 38 to cross zero near `c` = -1.74. It does not: across the
whole interval it sits at -0.03 plus or minus 0.01 and is **non-monotonic**, and at `g` = 2.6
`b` reads +1.36, +2.00, +1.46 at the three values of `c` - wandering by 0.3 between adjacent
points where a smooth dependence would move steadily.

**These coefficients are at the numerical floor of the solve.** A quadratic fitted over
plus or minus 5 per cent of injection angle turns a flight-time error `d` into
`a` about `d/(T x 0.05)` and `b` about `d/(T x 0.0025)`. The observed scatter needs only
**1 ns on a 500 microsecond flight** - 2e-6 relative, which is exactly the energy drift this
4 mm-cell Astral solve already reports. So section 38's coincident zeros were an
interpolation through noise, and its claim that the two conditions meet "to four per cent in
`c`" is withdrawn.

**The solve is deterministic, so this is not randomness.** Repeating a configuration gives the
same answer to the bit. What varies non-smoothly is the *error*, because moving a parameter
moves the geometry against a fixed mesh - which is Amendment 36's finding met for the third
time in this work, now in a quantity two derivatives removed from the field.

**What survives, and what does not.** The reduction from the baseline's 8.3e-3 to about
2e-3 is far above the floor and stands: the drift spread really did improve three- to
fourfold, and the mechanism - centring the well on the injection plane - is real, since it was
confirmed by the sign of `T`'s response as well as by the coefficients. What does **not**
survive is any ranking among the configurations clustered at 1.7e-3 to 3.1e-3, or any claim
that a particular `c` zeroes either order. The measurement cannot separate them.

**The floor also caps what this model can ever say about resolving power.** An arrival spread
of 2e-6 relative is `R` = 250,000 as a *ceiling on measurability*, and the drift spread must be
read against it: at 2e-3 there are three orders of headroom, which is why the improvement was
visible, but a foil law aiming at the published `R` would need coefficients ten times below the
floor. **Refining the mesh and tightening the integrator is therefore a prerequisite for the
next round of foil optimisation, not an afterthought** - and a test at 4, 3 and 2 mm cells plus
a tighter tolerance is running to establish how much refinement buys.

### A note on the harness rather than the physics

Three scripting faults cost compute or reasoning tonight, all of one kind - a scan whose
limits or output were wrong in a way that looked like a result:

1. **`foilQuad` bounded at plus or minus 4** while the scan asked for 5 and 8, so the model was
   refused before a solve began. The rows printed as missing and I read them as a physical
   edge (section 37).
2. **A results table that formats a refusal and a lost ion identically.** Both appear as a
   blank row; only the outcome field distinguished `None` from `StruckElectrode`.
3. **A basis expression that was wrong twice over.** Building a per-slice basis needs a
   delta, and `1 - d^2` is neither dimensionless-safe (the engine correctly refused it: a
   potential must be volts, and bare literals carry no unit) nor a delta - it goes *negative*
   for `|d| > 1`, so slices two away would have carried -3 V. `floor(1 / (1 + d^2))` is the
   indicator the grammar can express. The refusal was the engine doing its job; the negative
   lobe it would not have caught.
4. **A reader looking for the wrong key.** `einzel export` reports `artifacts`; the script
   asked for `files`, got nothing, and printed "export failed" over a successful export. An
   earlier script in the same directory checked both keys - so this was a regression against
   my own prior fix, in a fresh copy of the same three lines.
5. **A print statement that crashed on an absent value** - `energyDrift` is correctly absent
   for a field that does work, and formatting it as a float threw after all the flights had
   run, discarding the whole measurement.

Item 5 has a general fix now applied: **write the raw results to disk before formatting
anything**, so a presentation bug cannot destroy compute. Items 1 and 2 share a rule -
*a study's own refusals must be visually distinct from its physical failures* - which is the
same argument GRD-3 makes about warnings, applied to a scratch harness rather than to the
engine. Item 4 is the one worth dwelling on: **five scripts in this directory each reimplement
the same read of the same CLI output**, and the copy that was right did not stop the copy that
was wrong. That is the argument for the engine's own figures of merit over ad-hoc harness
readers, made from the wrong side.

**And the common thread across all five: every one of them produced a plausible-looking
output rather than a crash.** A refused model printed as a missing row, a wrong basis printed
as a field, a failed key printed as "export failed" over a success. In a night of scanning
where the interesting answers are also the surprising ones, that is the failure mode that
costs most - which is why the physics claims that survived tonight are the ones confirmed by
two independent observables, and the ones withdrawn were single-observable interpolations.

## 40. Refinement does not converge the drift coefficients, and the alternative is already validated

Same configuration (`c` = -1.5, `g` = 3.0, `V` = -20), mesh and integrator varied:

| case | T | `a` | `b` | drift spread |
| --- | --- | --- | --- | --- |
| 4 mm cell | 502.164 us | +0.0066 | -0.675 | 1.66e-3 |
| 3 mm | 499.060 | +0.0094 | -0.257 | 9.46e-4 |
| 2 mm | 498.196 | +0.0303 | -0.183 | 1.74e-3 |
| 4 mm, tolerance 1e-12 | **502.164** | **+0.0066** | **-0.675** | 1.66e-3 |

**The integrator is not the error source.** Tightening the relative tolerance to 1e-12 gives
values identical to five decimals, so the trajectory is already far tighter than the field it
flies through. Refining the *mesh* is the only lever.

**The flight time converges and the coefficients do not.** T goes 502.16, 499.06, 498.20 -
differences of 3.10 and 0.86 us, a ratio of 3.6, so roughly second order, extrapolating to
about 497.8. But `a` **grows** under refinement, 0.0066 to 0.0303, while `b` shrinks, -0.675 to
-0.183. Halving the cell moves `a` by 0.024, which is the same size as the scatter seen across
`c` in section 39. So refinement does not rescue the measurement; it moves it by as much as
the thing being measured.

**The arithmetic explains it.** `a` is read from `T(+5%) - T(-5%)`, which at `a` = 0.0066 is
**330 ns**, sitting on a mesh error in T of about **3000 ns**. It is measurable at all only
because that error largely cancels between the two angles - but the cancellation is about 60
per cent effective, and the residual pins `a` at plus or minus 0.02 whatever the cell size.
**Every configuration called "best" in sections 36 to 38 - 1.66e-3, 9.46e-4, 1.74e-3 - sits at
that floor**, and the reduction from 8.3e-3 is real only because it is four times above it.

### What to use instead, and it is already tested

The measurement is limited by *differencing large flight times*. The quadrature of section 24
is not: it extracts the adiabatic well from one trajectory and integrates the drift equation
directly, so its accuracy is set by the well's own sampling rather than by cancellation
between two nearly equal microsecond quantities. It already reproduced the measured flight
time to **0.05 per cent** and both derivatives to within 10 per cent at the shipped
configuration, and it costs one flight per configuration instead of five.

So the next round of foil optimisation should run **against the quadrature, not against
full-track flights** - with full-track flights kept for confirmation at the handful of
candidates the quadrature nominates. That is a thousandfold reduction in cost and an
improvement in precision at the same time, which is unusual enough to be worth stating twice.

**What this leaves standing from tonight**, stated so the morning does not have to reconstruct
it: the drift spread fell from 8.3e-3 to the measurement floor of about 2e-3, the mechanism
(centring the well on the injection plane) is confirmed by two independent observables, and
no finer ranking than that is available from full-track flights at any mesh this machine can
afford.

## 41. A fast evaluator for foil laws, and the assumption that had to be dropped

Section 40 concluded that foil optimisation should run against the section 24 quadrature
rather than against full-track flights, because flight-time differencing is floored at about
plus or minus 0.02 in `a` by mesh error at any cell size. Building that evaluator took three
attempts and the failures are the useful part.

**The architecture.** The foil is sixteen slices, the field is linear in their potentials, and
the adiabatic well is a linear functional of the field, so

    W(z) = sum_k  V_k  W_k(z)

and any voltage law costs no solve at all once the sixteen basis wells `W_k` are known. The
drift's coefficients then come from quadrature of the drift equation against `W`, which is
what section 24 validated to 0.05 per cent on flight time.

**First attempt: one kernel, translated.** If the slices are identical and the ion's x-motion
is barely perturbed by the foil, one slice's well serves for all sixteen by translation - one
solve instead of sixteen. Validated against a directly measured flat-plate well at -20 V
uniform, it reproduces the **depth to 5.1 per cent** and the quantity the drift actually feels,
`phi(z) - phi(0)`, to only **43 per cent of its span**, with the error concentrated at the ends:
+0.49 V at `z` = 26 mm and +0.26 V at 334 mm against +0.04 V in the middle. The kernel's
support is 112 mm against a 21.9 mm slice pitch, so a slice's well overlaps five neighbours
and the end slices are genuinely not translates of the middle one. **Rejected**: the drift
coefficients depend on exactly the shape it got wrong.

**Two weighting errors found on the way, both already documented one section earlier and both
made again.** The first kernel was averaged **uniformly over x**, which section 24 established
overstates the well 4.9-fold; weighting it over a full x-period along the real trajectory
instead took the peak from 0.1313 V to 0.0725 V. And the first validation compared a
flat-plate convolution against a **contoured-plate** measurement - two different geometries -
which showed up as a flat model against a two-lobed measurement and looked like a modelling
failure rather than a mismatched control.

**What the geometry check bought.** With the trajectory weighting, slice 8's well peaks at
187.6 mm against a slice centre of 185.9 mm - a 1.7 mm agreement that confirms the basis is
indexed correctly. That check is cheap, independent of the physics, and would have caught an
off-by-one in the slice indexing immediately; it is worth doing before any use of a basis.

Sixteen separate solves are now running, each processed to its profile and its 30 MB volume
deleted before the next. That removes the translation assumption entirely: superposition over
true per-slice wells is exact up to interpolation.

## 42. The evaluator's range bug, and the basis rebuilt on the geometry

The sixteen-well basis validated well against a directly measured flat-plate well - 0.51 per
cent on depth, **3.1 per cent on the shape the drift feels**, against the translated kernel's
43 per cent. Then the quadrature built on it disagreed with the flown coefficients by one to
three orders: `a` = +3.18 against a flown +0.0215, `b` = +83 against -3.60.

**The diagnosis was one number repeated.** Every law, however different, returned
`z_rev` = **344.0 mm** and `phi(0)` = **0.000 V**. The basis wells span 26 to 334 mm, because
they were extracted from the bare-tilt trajectory: the first full x-period average is centred
at 26 mm and the last at the reversal. Outside that the interpolator returns zero. So the well
at the injection plane was zero for every law, the well collapsed to zero just past 334 mm,
and the resulting step invented an enormous force at exactly the place the quadrature looks for
a turning point. Every coefficient came from that artefact rather than from the foil.

**A constant appearing where a variable was expected is the cheapest diagnostic there is**, and
it was in the first column of the first table. What made it visible was printing `z_rev` and
`phi(0)` beside the coefficients rather than the coefficients alone - three extra numbers that
identified the fault immediately, where the coefficients alone had only said "wrong".

**The fix removes the drift from the construction.** The mirrors are invariant along z apart
from the tilt, so the ion's x-orbit is the same orbit at every z: one x-period taken from the
trajectory can weight a **uniform z-grid** of any extent. The basis then describes the geometry
rather than a particular flight, and covers -10 to 900 mm - past any reversal the drift can
reach - at 5 mm resolution.

That is also the more honest object. The previous basis silently mixed two things: the foil's
field, which is geometry, and the bare-tilt trajectory's sampling, which is one flight of one
ion. Only the first belongs in a reusable basis, and separating them is what makes the
evaluator's answers independent of the flight that happened to build it.

## 43. The evaluator is a screen, not a measurement - and it ranks correctly

With the basis rebuilt on the geometry, the quadrature against four flown laws:

| law | T quad / flown | `a` quad / flown | `b` quad / flown |
| --- | --- | --- | --- |
| bare tilt | 852.73 / 853.69 us | - | - |
| V -20, g 0.63 | 642 / 661 | +0.163 / +0.022 | -1.83 / -3.60 |
| V -20, c -1.5, g 3.0 | 490 / 503 | +0.024 / +0.007 | +0.13 / -0.68 |
| V -20, c -1.2, g 3.0 | 470 / 482 | +0.067 / +0.051 | -0.64 / -0.38 |
| V -20, c -1.9, g 3.0 | 516 / 528 | -0.042 / -0.004 | +0.82 / +0.83 |

**Flight times are systematically 2.4 to 2.8 per cent low** and the bare tilt is exact to
0.11 per cent, so the bias is in the averaging rather than in the calibration. The
coefficients are good to a factor of one to ten and occasionally carry the wrong sign, which
is **not good enough to be an objective**: `a` = 0.0215 means T moves 0.2 per cent across the
angle scan, and a 2.5 per cent systematic that varies even slightly with angle swamps it.

**That is the adiabatic approximation's own limit, not a bug.** The x-period is 34 us against a
drift of 500, a separation of only 15 to 1, so the averaged potential carries percent-level
error and any quantity built from small differences of it inherits that. Neither method can
measure these coefficients well: flights are floored by mesh error at plus or minus 0.02 in
`a` (section 40), the quadrature by adiabaticity at a comparable level. **The two failures are
independent, which is why they agree on the ranking and not on the values.**

**And the ranking is exactly right.** Drift spread, best to worst -

| | flown | screen |
| --- | --- | --- |
| `c` -1.5, `g` 3.0 | 1.71e-3 | 1.35e-3 |
| `c` -1.9, `g` 3.0 | 1.85e-3 | 3.55e-3 |
| `c` -1.2, `g` 3.0 | 3.06e-3 | 4.31e-3 |
| `g` 0.63 | 8.3e-3 | 11.0e-3 |

- same order, all four, with one chance in twenty-four of that by accident. On four points
that is suggestive rather than proven, but combined with correct signs on `a` throughout and
`b` matching to one per cent in one case, the screen is fit for nominating candidates.

**So the division of labour section 40 asked for now exists**: optimise against the screen at
zero flight cost, confirm the nominees by flying them. A Nelder-Mead over all sixteen slice
potentials - the shape optimisation section 16 called for and nothing could afford until now -
is running against it.

## 44. An objective that could be fitted rather than satisfied

The screen made the sixteen-parameter shape optimisation affordable, so it was run:
Nelder-Mead over all sixteen slice potentials, minimising the drift's contribution to arrival
spread as `|a| x 0.045 + |b| x 0.045^2`. It reported the objective driven from 1.34e-3 to
**4.1e-13** - an improvement of 3.3 billion - from potentials that barely moved:

| slice | 0 | 1 | 2 | 8 | 15 |
| --- | --- | --- | --- | --- | --- |
| start V | -20.00 | -21.73 | -22.93 | -18.93 | +10.00 |
| "optimised" V | -19.83 | -19.20 | -22.83 | -18.97 | +10.27 |

**No physical quantity improves nine orders from a one per cent nudge**, and that mismatch is
the whole diagnosis. `a` and `b` are coefficients of a **five-point quadratic fit** to the
quadrature; with sixteen free parameters, driving two fitted coefficients to machine zero is
trivial and constrains nothing about `T` between or beyond those five sample points. The
optimiser found the objective's numerical structure, not the physics - the classic failure of
an under-determined objective, met here with an eight-to-one ratio of parameters to
constraints.

**The general rule this belongs to**, and it is the same one this project already applies to
warnings and to figures of merit: *an objective built from a truncated fit can be satisfied by
cancelling the fit rather than by improving the thing fitted.* The defence is not more
parameters or tighter tolerances but an objective that never forms the coefficients - here,
the **full width of `T` sampled across plus or minus three sigma of the thermal `v_z`
distribution**, seventeen points, no expansion. That cannot be gamed by cancellation because
there is nothing to cancel.

That rerun is in flight, with its result verified at a finer integration than it optimises
against, so a spurious win from quadrature noise would show up as a disagreement between the
two.

**Cost note worth recording.** The first optimisation took 57 minutes for 600 Nelder-Mead
iterations, because each objective evaluation ran eleven quadratures at 30,000 integration
points in pure Python. Screening does not need that resolution: the rerun uses 4,000 points
and checks the winner at 30,000. The screen's whole value is that it is cheap, and spending it
on integration accuracy that the adiabatic approximation's own 2.5 per cent bias makes
irrelevant is the wrong trade.

### The robust objective, and an error it exposes in every R quoted above

Rerun with the full width of `T` across plus or minus three sigma of the thermal `v_z`
distribution, seventeen samples, no expansion:

| | spread | drift-limited R |
| --- | --- | --- |
| best hand-tuned law (`c` -1.5, `g` 3.0) | 6.3624e-3 | 79 |
| **sixteen-parameter optimised** | **1.7244e-3** | **290** |

**3.69-fold, and identical at 4,000 and 30,000 integration points** - so it is not quadrature
noise, which is what verifying the winner at a finer integration than the search used is for.
The potentials move only about two per cent, which is credible here where the previous 3.3e9
was not: the width is a near-cancellation, so a small change to the well can flatten it, and
the check is that the improvement survives refinement.

**And it corrects a number used throughout sections 33 to 40.** The drift spread was computed
as `|a| x 0.045 + |b| x 0.045^2` - the truncated expansion evaluated at one sigma. For this
same law the full width over three sigma is **6.36e-3 against that expression's 1.71e-3**, so
**every drift-limited R quoted from the expansion is optimistic by about 3.7 times**. The
coefficients were fitted over plus or minus five per cent and the thermal distribution reaches
13.5, where the higher orders the fit discards are no longer small.

That does not change any ranking - the correction is a near-common factor - but it does mean
the absolute figures in those sections should be read as expansion values rather than as
arrival spreads. The three measures now in play differ by an order of magnitude and it is
worth naming which is which: the **expansion at one sigma** (optimistic, 1.71e-3 for the hand
law), the **full width over three sigma** (pessimistic, being peak-to-peak rather than a half
maximum, 6.36e-3), and a **direct thermal cloud** (the only one that is an arrival spread, and
which gave 29.5 for a related configuration). Only the third is a resolving power; the first
two are proxies, and their job is ranking.

The nominee is now being flown on the full track against the hand-tuned law, both measured by
the same three-sigma width the screen used, plus a direct cloud on each. If the flights
reproduce the 3.69 times, the screen-then-confirm loop is established end to end.

## 45. The nominee flies, and the objective is still the wrong statistic

The screen's nominee flown on the full track against the best hand-tuned law, both measured
by the same three-sigma width the screen optimised, plus a direct thermal cloud on each:

| law | T | ±3 sigma width | spread | R from that | **cloud R, 16 ions** |
| --- | --- | --- | --- | --- | --- |
| hand-tuned (`c` -1.5, `g` 3.0) | 502.83 us | 4.522 us | 8.99e-3 | 56 | **206.2** |
| **screen's nominee** | 504.42 | **1.721** | 3.41e-3 | 147 | **224.3** |

**The screen-then-confirm loop works.** It predicted 3.69 times and the flights give **2.63
times** - right direction, right order, forty per cent optimistic. A screen that ranks
correctly and is optimistic by a known factor is a usable instrument, and this is the first
time in this work that a foil law has been designed rather than guessed.

**But the resolving power barely moved: 206.2 to 224.3, a factor of 1.09.** Converting both to
a half maximum, the cloud's FWHM went 1.22 to 1.13 us while the three-sigma peak-to-peak fell
4.52 to 1.72. **The optimisation flattened the tails and left the core alone.**

That is a defect in the objective, not in the optimiser or the foil. `R` is set by the
**full width at half maximum**, which the core of the thermal distribution determines; a
peak-to-peak width over plus or minus three sigma is dominated by the tails, where almost no
ions are. Minimising it is minimising a statistic the instrument does not report.

**Three objectives, three failures, and the sequence is the lesson.**

1. `|a| x 0.045 + |b| x 0.045^2` - a **truncated fit**, satisfiable by cancelling two
   coefficients with sixteen parameters. Caught by a 3.3e9-fold improvement from a one per
   cent nudge (section 44).
2. **Peak-to-peak over plus or minus three sigma** - unfittable, verified against refinement,
   and it genuinely improved 2.63 times when flown. But it weights the tails, so the
   improvement did not reach the reported figure.
3. **Density-weighted standard deviation**, scaled by 2.3548 to be an FWHM - the statistic a
   cloud actually measures. Now running.

Each objective was more robust than the last and each was still wrong, in a different way:
the first could be gamed, the second was honest about the wrong quantity. **The check that
would have caught the second earlier is the one that did catch it - carry a direct cloud
measurement alongside the proxy from the start**, because a proxy that improves while the real
figure does not is otherwise indistinguishable from progress.

## 46. The drift stops binding, the mirrors take over, and the knob for that was in the paper

With the density-weighted FWHM objective - the statistic a cloud actually reports - the
sixteen-parameter optimisation gains **1.60 times**, not the 3.69 the peak-to-peak claimed:

| | FWHM-equivalent spread | screen R | flown cloud R |
| --- | --- | --- | --- |
| hand-tuned (`c` -1.5, `g` 3.0) | 1.9706e-3 | 254 | **206** |
| FWHM-optimised, sixteen free | **1.2335e-3** | **405** | flying |

Identical at 4,000 and 30,000 integration points. And the screen's *absolute* value now agrees
with reality - 254 predicted against 206 measured, **23 per cent optimistic** - where the
previous objective disagreed by an order of magnitude. The third objective is both unfittable
and calibrated.

### The requirement, and it inverts the night's priority

Taking the optimiser's own coefficients (`a` = +0.0097, `b` = +0.211 for the drift; `c1` =
+0.0197, `c2` = -0.169 for the energy) and asking what injected `v_z` spread the published
resolving power would need:

| sigma(`v_z`) | drift terms | energy terms | total | R |
| --- | --- | --- | --- | --- |
| 4.500% (thermal, 300 K) | 8.6e-4 | 5.98e-4 | 1.46e-3 | 342 |
| 0.900% | 1.04e-4 | 5.98e-4 | 7.03e-4 | 712 |
| 0.450% | 4.8e-5 | 5.98e-4 | 6.46e-4 | 774 |
| 0.045% | 4.4e-6 | 5.98e-4 | 6.03e-4 | **830** |

**The energy terms floor R at 836 whatever the drift does.** Below about one per cent of `v_z`
spread the drift stops binding entirely and the mirrors are the whole limit. Every section
from 22 onwards treated the drift as the problem; optimised, it is not.

That is a better place to be than it sounds, because **the mirrors are the part with published
constraints**. For R = 100,000 the energy terms need `c1` below 1e-4 and `c2` below 4e-3.
Section 18 already measured **|c1| = 0.000102** on a half oscillation, so the first is
demonstrably reachable; `|c2|` = 0.0939 at `20/38/84/130` and 0.0398 at another depth, so the
second needs about ten times more.

### And the knob for it has been in the archived paper all night

Table 1 of the crowd-control paper gives **three** columns, not two:

| electrode | `C(0)` | `C(1)` | **`C(2)`** |
| --- | --- | --- | --- |
| U1 | -1.840 | 5.67 | **-0.256** |
| U2 | -1.158 | -1.616 | **-0.654** |
| U3 | 0.916 | -0.715 | **0.032** |
| U4 | 1.503 | -2.963 | **-0.361** |

and states what each does: modifying the voltages along the two vectors "modifies the linear
and quadratic time-energy coefficients `(t|e)` and `(t|ee)` correspondingly, sparing the
spatial focusing of the mirrors", with `TE2` = 0.1 as its worked example. **`C(2)` is the
published knob for exactly the coefficient that now floors the resolving power**, and nothing
in this work has used it - every mirror run all night carried `te1` alone.

A scan of `TE2` along `C(2)` is running. If `c2` responds the way `C(1)` moved `c1`, the
energy floor lifts and the mirrors' three published vectors are doing the three jobs the paper
says they do: `C(0)` sets the focus, `C(1)` trims first order, `C(2)` trims second - which is
also what "third-order temporal focus" requires, since cancelling three orders needs three
controls.

### `C(2)` makes `c2` worse, which is the sharpest constraint yet

Scanning `TE2` along the published `C(2)`, at `20/38/84/130` with `te1` = 0:

| `te2` | `\|c1\|` | `\|c2\|` | `\|c3\|` | energy term | R floor |
| --- | --- | --- | --- | --- | --- |
| -0.60 | 0.1437 | 0.2269 | 0.66 | 3.74e-3 | 134 |
| -0.30 | 0.0993 | 0.1930 | 0.84 | 2.60e-3 | 192 |
| **0.00** | **0.0330** | **0.0939** | 1.41 | **8.83e-4** | **566** |
| +0.30 | 0.0820 | 0.2364 | 3.43 | 2.20e-3 | 228 |
| +0.60 | 0.3417 | 1.7739 | 15.86 | 9.65e-3 | 52 |

**Zero is optimal: the published vector makes `c2` worse in both directions**, where its stated
purpose is to modify the quadratic time-energy coefficient. It also degrades `c1` and `c3`
simultaneously, so it is not trading orders - it is simply detuning the mirror.

**That is the same signature as `C(1)`**, whose sensitivity came out 2.2 against a published
1.0 (section 26). Both published correction vectors were derived in the four-dimensional space
of *Thermo's* mirror voltages at *Thermo's* electrode positions; applied to guessed depths they
do not do their published jobs. Which is not a disappointment but the **sharpest constraint in
this reconstruction so far**, because it is two independent, entirely unfitted published
behaviours that the geometry must reproduce:

1. `dc1/dTE1` = **1.0** - met at `d2` = 38 mm to two per cent, and 2.2 at the shipped 50;
2. `TE2` along `C(2)` must **reduce** `c2` near zero - currently violated at every geometry
   tried, since zero is a local minimum in `te2`.

Constraint 2 is the more powerful of the two because it is a *sign* condition rather than a
magnitude, so it cannot be met by accident and needs no assumption about how good "about
2.5 ppm/V" is. A depth set where both hold is a geometry in which the published calibration
scheme works as published, and that is a far stronger claim than matching any single number.

**The morning task is therefore well posed for the first time**: search `d1..d4` for a
geometry that simultaneously flies the full track, gives `dc1/dTE1` = 1.0, and makes `C(2)`
reduce `c2`. Three conditions, four parameters, all three published or physically necessary,
and none of them fitted to a number this model produced. The foil work of sections 30 to 45
can then be redone on that geometry - it is cheap now, via the screen - and the resolving
power read off with the drift no longer binding.

### The foil confirmation is inconclusive, not negative

The FWHM-optimised law flown against the hand-tuned one:

| law | ±3 sigma width | **cloud R, 16 ions** |
| --- | --- | --- |
| hand-tuned | 4.522 us | 206.2 |
| FWHM-optimised | 4.889 us | **220.1** |

Predicted 1.60 times, delivered **1.07**. The earlier peak-to-peak-optimised law delivered
1.09. So neither objective moved the reported resolving power, and it would be easy to write
the foil optimisation off.

**But sixteen ions cannot see the difference.** A FWHM from `N` ions carries about
`1/sqrt(2N)`, which is 18 per cent here, so the two measurements are 206 plus or minus 37 and
220 plus or minus 40. The predicted value - 273, from taking the screen's drift spread and
adding the measured energy terms - sits **inside** those error bars. The experiment cannot
distinguish "no improvement" from "exactly the predicted improvement", and reporting either
would be reporting the sampling error.

A 72-ion comparison is running, which brings the FWHM uncertainty to about 8 per cent and
makes a 1.4-fold change decisive either way.

**The methodological point is the one that keeps recurring tonight**: the confirmation must be
able to resolve the effect it is confirming. Sections 39 and 40 found the drift coefficients
sitting at the mesh error floor; section 45 found the objective measuring the wrong statistic;
this is the same failure in the *sample size*. Each time the arithmetic that would have caught
it in advance - error propagation through the statistic actually being compared - is two lines
long and was not done first.

## 47. The Grinfeld paper, and the reconstruction closes

Mike obtained the isochronous-drift paper by interlibrary loan. **It is the design document**,
and it settles most of what sections 24 to 46 were inferring. Two corrections to how this
project has cited it: it is **Nuclear Instruments and Methods in Physics Research A** 1060
(2024) 169017, not Nucl. Instrum. Methods Phys. Res. A as recorded everywhere above; and the drift-control
electrode is called a **stripe** here, with "ion foil" being other papers' name for it, while
"prism" in this paper means the *steering deflector* - a pair of 90-degree wedges that set the
injection angle. Two different devices.

### What it confirms

- **Section 3 is titled "Adiabatic drift"** and derives exactly the separation this work
  arrived at independently: the action on an x-oscillation is an adiabatic invariant, and with
  energy conservation it gives a drift equation in an **effective pseudopotential**. The
  paper's treatment is the more correct one - the invariant is the **action** `J(y, eps)`,
  where section 24 used the time-averaged potential, which is why that quadrature carried a
  2.5 per cent systematic.
- **The tilt's effect**: "a decrease of the drift angle by 2 alpha on every two subsequent
  reflections in converging mirrors" - the same closed form as section 13's
  `dv_z = V sin 2 alpha`, with `alpha` there being the angle *between* the mirrors where
  section 13's is per mirror.
- **Both drift-control methods** are the two this work used: a biased stripe whose shape
  function sets its extent, and mirror convergence.

### Table 1, against this model

| | published | this model | |
| --- | --- | --- | --- |
| nominal drift length `y0` | **335 mm** | 334.61 mm | 1.001 |
| oscillations | **25** | 25 | exact |
| flight path | **~32 m** | 31.27 m | 0.977 |
| acceleration | **4000 V** | 4000 V | exact |
| nominal injection angle | **1.78 deg** | 2.29 deg | 1.29 |
| mirror convergence angle | **0.045 deg** | 0.0458 per mirror | see below |
| stripe bias | **-13.8 V** | -3.0 V | 4.6x low |
| effective mirror separation | **641 mm** | (cap-to-cap 716.6) | - |

**Three independent readings agree that the tilt was twice too large**, which resolves the
most consequential guess in this reconstruction. The published 0.045 degrees is the angle
between the mirrors - the paper says the convergence is "only a few hundred micrometers on the
entire drift length", and 0.045 total gives 263 microns where a per-mirror 0.0458 gives 536.
Independently, the tilt must supply only `a0` = 0.84 of the pseudopotential, so the tilt alone
must reverse at 335/0.84 = 399 mm, which at the published 1.78 degrees needs 0.0232 per
mirror. And `tiltBaseline` = 494 mm follows, against the 250 mm guessed. The guess was wrong
by exactly a factor of two, and section 17's warning that it carried a 2.8-fold ambiguity was
the right warning.

### The stripe shape, published

The optimised drift pseudopotential is given outright:

    psi_m(eta) = a0 eta                                    a0 = 0.83999
    psi_s(eta) = c1 eta + c2 eta^2 + ... + c5 eta^5
    c = [0.75160, -7.52535, 14.0242, -9.17661, 2.08613]

normalised so `psi_m(1) + psi_s(1) = 1`. **Summing the published coefficients gives 0.99996**,
which confirms both the values and the reading of the parameterisation. Evaluated against
Fig. 4's plotted curves it agrees again: `psi_s(0.5)` = **-0.2608** against a plotted dip near
-0.27, and `psi_s(1)` = **+0.15997** against a plotted +0.16.

**That answers section 46's open question and explains why nothing tried tonight got close.**
`psi_s` **dips negative** to -0.26 at mid-drift before rising - the stripe *accelerates* the
drift over the first half and retards it over the second - and it is fifth order. Every law
tried in sections 30 to 45 was monotonic and second order at best: two terms of five, and the
wrong sign over half the range. The published isochronicity is **2.1e-6 over plus or minus 10
per cent of drift length** (Fig. 4, right), which is R = 238,000; the best this work reached
was 1.2e-3, **580 times worse**.

### And the design is now directly implementable

The sixteen per-slice basis wells of section 42 make "produce this pseudopotential" a **linear
least-squares problem** rather than an optimisation: find `V_k` minimising
`|| sum_k V_k W_k(z) - eps_y0 psi_s(z/L) ||`, with `eps_y0 = 4000 sin^2(1.78 deg)` = 3.859 V.
Solved with a small ridge term (the wells overlap five deep, so the normal matrix is
ill-conditioned), it fits the published pseudopotential to **0.37 per cent rms on a 1.64 V
span**, with smooth slice potentials from +0.17 V rising to -5.30 V at mid-drift and +5.77 V
at the far end.

So the night's screen and basis - built to make a blind search affordable - turn out to be
exactly the machinery needed to *implement a published design* instead. That is a better use
of them, and it needs no pixel measurement of a perspective drawing: the shape is analytic,
and only the two scaling lengths `w0` and `w1` are unpublished, which the fit bypasses
entirely by solving for potentials rather than for a plate outline.

### Flying it: a units error of mine, and what the diagnosis cost

The first attempt at the published design put the tilt-only reversal at **40.6 mm** where
399 was predicted. The ratio is 9.83, which is 3.14 squared - and `z_rev` goes as the square
of the injection angle, so the angle was pi times too small.

`injectionAngle` is the **tangent** of the angle, which its own description in the template
says outright ("Tangent of the angle between the trajectory and the mirror axis, setting the
drift rate as V tan(theta)"). I passed `degrees/180` as though it were half turns - the
convention used for `mirrorTilt` and every other angle in this template - giving 0.009889
where `tan(1.78 deg)` = 0.031077. Correcting it predicts 40.6 x 9.88 = **401 mm** against the
399 expected, to half a per cent, so the model was right and the conversion was not.

**Worth recording because the diagnosis was free and the error was avoidable.** The factor was
identifiable from a single ratio - 9.83 is conspicuously pi squared, and only one quantity in
the drift enters squared - and the correct convention was written in the parameter I was
setting. Two habits would each have caught it: reading the description of a parameter before
overriding it, and checking the tilt-only reversal against its closed form *before* adding the
stripe on top. The second is the general one: **when implementing a two-part design, verify
each part alone against its own prediction before combining them**, because the combined
result has no unique diagnosis.

This is the sixth convention or harness error of the night, and they all share a shape: a
quantity read under the wrong convention, producing a plausible number rather than a failure.
The others are collected in the harness note above.

### The published design flown: the geometry reproduces to one per cent

With the injection angle corrected to a tangent, `theta0` = 1.78 degrees, `tiltBaseline` =
494 mm and the sixteen slice potentials fitted to `psi_s`:

| | published | this model | |
| --- | --- | --- | --- |
| tilt-only reversal (predicted 335/0.84 = 399) | - | **400.3 mm** | 1.003 |
| reversal with the stripe | **335 mm** | **338.6 mm** | 1.011 |
| reflections | **50** | **51** | +1 |
| flight time | **~779 us** | 873.2 us | 1.121 |

**The tilt-only reversal at 400.3 mm against a predicted 399 is the sharp one**, because it
tests the tilt calibration alone with nothing else in play - and it confirms that `a0` = 0.84
means what section 47 read it to mean. The reversal with the stripe then lands within 1.1 per
cent of the published 335 mm at the right reflection count, which is the published drift
design reproduced in an independently built model.

**And the flight time's 12 per cent has a single-number explanation.** The paper's effective
mirror separation is **641 mm** against this template's 716.6, and `T` is proportional to it:
779 x 716.6/641 = **871 us** against the measured 873.2, agreeing to **0.26 per cent**. So the
remaining flight-time error is the mirror separation and nothing else - a quantity this model
guessed from the published flight path and can now take from Table 1 instead. Note the
published 641 mm is an *effective* distance ("the ion's nominal velocity times half the
period"), not a physical cap-to-cap, so it is not a drop-in replacement for `capToCap`; the
ratio is what the measurement pins.

**A first isochronicity reading gave `a` = +5.20 where the design wants zero, and the
explanation offered here was wrong.** It claimed the scan had strayed outside the published
plateau, reasoning that `eta_D` goes as `sin^2(theta)` so `eta_D` = 1 ± 0.1 is only ± 5 per
cent in angle. **That scaling holds for a constant-force drift and not for this one**: the
designed `psi_total` rises steeply and nonlinearly, so an energy ratio of 1.232 moves the
turning point only to `eta` = 1.099 - meaning ± 10 per cent in `eta_D` is ± 11.7 per cent in
angle, and the ± 6.75 per cent scan was comfortably *inside* the plateau. The real cause was
a crest in the fitted potential at the foil's far edge, section 48. Re-measuring inside it is the test that means
something, and the arithmetic relating the two ranges is one line that should have been done
before the first scan rather than after it.

**And the re-measurement itself failed first, in a way this repository already documents.** The
new angle list was patched into the script by string replacement and applied; the *reporting*
block's replacement matched nothing, did nothing silently, and the old reporting code then
crashed looking for the old keys - after the flights had run. CLAUDE.md records exactly this
failure from the corpus work ("three tests edited the scaffolded model by string replacement
against a JSON layout the corpus reformatted, so the edit matched nothing, the model was
unchanged, and each reported the feature it was checking as broken"), and its fix is an edit
helper that **asserts the replacement happened**. I made the same mistake in a scratch script
with no assertion. The rewrite is a separate module that *imports* the builder from the first
script rather than duplicating it, so the geometry cannot drift between the two - which is the
same argument, applied to code rather than to text.

### The knife-edge detector, and a measurement that does not need one

The in-plateau scan gave flight times that were not smooth at all: 815.7, 818.9, 831.4,
**873.2**, then **1641.5**, 1502.8, 1439.0 microseconds across angle factors 0.955 to 1.045.
A near-doubling between x1.000 and x1.011 is not a flight time varying.

**The trajectories say why**: `v_z` changes sign **once** at x1.000 and **three times** at
x1.011 and x1.045, with 51 reflections becoming 96 and 84. The ion returns toward the
injection plane, **fails to cross the detector at z = -2 mm, turns round and drifts out
again**, and is caught on a later pass. The number of drift excursions is what changed.

**The first hypothesis was wrong and cheap to refute.** The fit was constrained only over
z in [0, 335] mm, so the foil's potential outside that range is unconstrained and could in
principle build a barrier behind the injection plane. Sampled, it does not: the total falls
monotonically from 0.0334 V at z = 0 to -0.29 V at -30 mm, with no maximum anywhere in
z <= 0. Nothing reflects the ion there.

**What actually happens is that the drift does not close exactly.** The adiabatic invariant is
approximate - which is the whole basis of the pseudopotential treatment, and the paper says as
much - so a little energy passes between the x-oscillation and the drift over 25 oscillations,
and the ion returns to a few tenths of a millimetre either side of where it started. A
detector 2 mm behind the injection plane is then a **threshold**, and whether a given ion
clears it on the first return is decided by that asymmetry rather than by anything being
measured.

**So the measurement was replaced rather than the model.** The time to the **drift turning
point** needs no detector, is half the drift period by symmetry, is present in every
trajectory however many excursions follow it, and is the quantity the paper's Eq. (7) is built
from. That is what should have been measured from the start: **an observable that depends on a
threshold being crossed is not a good place to look for a part-per-million effect.**

## 48. The stripe must outrun the drift, and a fit that stopped where the drift did

The turning-point measurement removed the detector and the discontinuity survived: `z_turn`
jumps from **338.65 mm to 409.05 mm** between angle factors 1.000 and 1.011, a 21 per cent
change in reversal for a 1.1 per cent change in angle. And 409 is close to the **tilt-only**
reversal of 400 mm, which is the clue.

Sampling the fitted drift potential past the fitted range:

| z, mm | 320 | 335 | **350** | 365 | **380** | 395 | 410 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| total, V | 3.460 | 3.847 | **3.947** | 3.855 | **3.832** | 3.893 | 3.999 |
| intended, V | 3.459 | 3.859 | 4.265 | 4.673 | 5.080 | 5.484 | 5.884 |

**The fitted potential has a local crest at 350 mm and then dips**, where the intended one
rises monotonically, and the two diverge from exactly 335 mm - the edge of the fitted range.
The cause is physical rather than numerical: **the foil ends at 350 mm in this template**, so
past it the stripe's contribution decays away and cancels part of the tilt's rise.

The drift energy at angle x1.011 is **3.9447 V against the 3.9471 V crest** - a margin of
0.06 per cent. The ion clears it, coasts through the dip, and is finally stopped at 409 mm
where the tilt's rise overtakes it, which is the measured number. At nominal energy
(3.8594 V) it never reaches the crest and turns at 338 mm as designed.

### The requirement this exposes

**The stripe must extend far enough beyond the nominal drift length that its far-edge fringe
forms no crest inside the operating range of drift lengths.** The published plateau spans
`eta_D` = 1 ± 0.1, so drift lengths reach 368 mm, and the crest here sits at 350 - inside the
range that must work. The margin between the nominal drift energy and the crest is 2.3 per
cent while the plateau needs ±2.2 per cent, so they very nearly overlap, and that near-overlap
is the whole instability.

This is a design constraint the pseudopotential formalism does not state and the papers do not
mention, because in a correctly built instrument it is satisfied comfortably: their stripe
presumably runs well past 368 mm. In this template `driftLength` is 350 mm and the foil spans
all of it, which puts the far edge 15 mm past the nominal reversal - far too close.

**And it is a fitting error as much as a geometry one.** The least-squares fit of section 47
was constrained over `0 <= z <= L` because that is where the published `psi_s` is defined. Past
`L` the basis sum was free, and "free" meant "decays as the foil's fringe field does", which is
not what the design needs there. **A fit over the range the observable occupies is not enough
when the dynamics can leave that range** - and a drift whose whole purpose is to reverse near
the edge of the fitted domain will leave it whenever it is perturbed.

### The design reproduces, and the precision limit is the discretisation

With the stripe extended and the fit run past the plateau:

| angle | ×0.955 | ×0.978 | ×0.989 | **×1.000** | ×1.011 | ×1.022 | ×1.045 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `t_turn`, us | 413.547 | 408.255 | 406.980 | **406.592** | **406.371** | 406.471 | 410.674 |
| `z_turn`, mm | 324.62 | 331.13 | 334.16 | 337.23 | 340.27 | 343.36 | 350.17 |

**The turning-point time is flat to 0.6 us over 3.3 per cent of injection angle, with an
interior minimum near ×1.011 and rising at both ends** - which is the shape of Fig. 4's
`tau(eta_D)`. `z_turn` is monotonic throughout, so the discontinuity is gone. The drift now
behaves as the design says it should.

Quantitatively it is **1.5e-3 against the published 2.1e-6**, and the factor of 700 has an
arithmetic explanation rather than a mysterious one. **The isochronicity is a cancellation to
2.1e-6, so the drift pseudopotential has to be right to about that same relative precision** -
roughly **4 microvolts on a 3.9 V well**. The sixteen-slice fit achieves 7 millivolts rms,
0.18 per cent, which is a thousand times too coarse and predicts a residual of order 1e-3.
That is what was measured.

**Which says something about the instrument rather than about this model.** A *segmented*
stripe cannot meet this specification: sixteen independently biased slices reproduce the
required shape to 0.2 per cent where six orders are needed. The paper shapes a **continuous**
electrode instead - its shape function `w(y)` - and its literature review notes as a drawback
that an earlier scheme "had to split all mirror electrodes into multiple segments with
individual voltages". The discretisation limit found here is a measurement of why that choice
was made.

So the model reproduces the design's mechanism and geometry faithfully, and its precision
ceiling is the way this template approximates the stripe, not the physics.

### The fit is the floor, not the mesh - established by quadrature with no flight in it

Two things could set the 1.5e-3 spread: the 0.18 per cent residual of the sixteen-slice fit,
or the flights themselves (the 4 mm foil mesh, the integrator). A half period computed by
quadrature over the fitted potential - `t = integral dz / v_z` with `v_z` from the
pseudopotential, no mesh, no trajectory - separates them, and the published polynomial run
through the same quadrature is the control:

| angle | fitted, us | design, us | flown, us |
| --- | --- | --- | --- |
| ×0.989 | 408.888 | 408.815 | 406.980 |
| ×1.000 | 408.001 | 408.778 | 406.592 |
| ×1.011 | 407.522 | 408.816 | 406.371 |
| ×1.022 | 407.686 | 408.928 | 406.471 |
| spread ±1.1% | **3.35e-3** | **9.9e-7** | **1.50e-3** |
| spread ±2.2% | 4.81e-3 | 1.6e-7 | 4.39e-3 |
| spread ±4.5% | 3.08e-3 | 1.6e-5 | 7.07e-3 |

**The fitted potential predicts the flown spread to within a factor of two, and the design
polynomial predicts the published 2.1e-6.** So the 0.18 per cent residual is where the
1.5e-3 comes from, the flights add little, and refining the mesh would buy nothing. The
design column is worth a sentence of its own: it is the published `psi_m + psi_s` put through
a quadrature this project wrote, giving 1e-6 over the inner plateau - the paper's own claim,
reproduced independently of the paper, which is also a check on the quadrature.

The absolute half period agrees across all three routes to 0.5 per cent (408.0 / 408.8 /
406.6 us), so the quadrature is quantitatively right and not only right in shape. What that
licenses is using it as the *objective* for the stripe design: any potential that the
quadrature says is isochronous to 1e-6 will fly that way to within what the mesh adds,
and the quadrature costs milliseconds where a flight costs a minute.

## 49. The mirrors focus, and both published correction vectors do their published jobs

Section 47 called `C(2)`'s apparent failure "the sharpest constraint in this reconstruction so
far", on the strength of a table of `|c2|` against `te2` on a 0.3 grid, and concluded that the
published vector makes the quadratic coefficient worse in both directions. **That was an
artefact of the measurement.** Fitting the *signed* cubic through five energies at each `te2`:

| `te2` | -0.20 | -0.15 | -0.10 | -0.05 | 0.00 | +0.05 | +0.10 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `c2` | -0.1684 | -0.1537 | -0.1363 | -0.1155 | -0.0901 | -0.0589 | -0.0210 |

Monotonic, linear at slope +0.484, **crossing zero at `te2` = +0.169**. So `C(2)` reduces the
quadratic coefficient exactly as published, and the old table's rise at `te2` = +0.30 is
*overshoot past the zero* - which is what a working correction vector does when it is driven too
far. **A magnitude cannot show a sign change**, and a 0.3 grid cannot bracket a zero at 0.169.
The lesson generalises past this instrument: where a quantity is being *cancelled*, measure it
signed, and put grid points close enough together to bracket the crossing.

And `dc1/dTE1` = **+0.987 against the published 1.0** at these depths, measured at `te1` = 0.

### Two knobs, two coefficients, solved simultaneously

The published calibration is a two-parameter family, so the natural thing to ask is where both
coefficients vanish at once. Newton on the 2x2 Jacobian, damped and seeded by continuation:

| | `TE1` | `TE2` | `c1` | `c2` | `c3` | R at +/-3% |
| --- | --- | --- | --- | --- | --- | --- |
| uncorrected | 0 | 0 | -0.0330 | -0.0977 | +1.41 | 263 |
| one linear solve | +0.0072 | +0.0923 | +0.0058 | +0.0292 | +2.28 | 1,066 |
| **converged** | **+0.0050** | **+0.0860** | **+2e-5** | **+1e-4** | +2.07 | **4,424** |
| the paper's own worked point | 0.01 | 0.10 | +0.0135 | +0.0695 | +2.56 | 528 |

**A seventeen-fold gain, at a working point close to the paper's own worked examples** on a
geometry fitted to neither. The control that matters is the row that is not in the table: `TE1`
alone, driven to zero `c1`, gives `c2` = **+0.288**, three times worse than uncorrected. One
knob cannot do this, which is presumably why the paper carries two.

**The undamped Newton had to be fixed first, and the failure was instructive.** Away from focus
the response curves hard, so an undamped step ran `TE2` out to 0.45 with `c2` still 0.36 - and
`c3` read at an unconverged point says nothing at all, because `c3` depends on `TE1` and `TE2`
as well as on the geometry. Damping to 0.6 with a 0.06 step limit and seeding each geometry from
a converged neighbour puts the iterate inside the linear region from the start.

### What is left is `c3`, and that is what the depths are for

Two knobs can zero two coefficients; the third is a property of the geometry. The paper calls a
**third-order** temporal focus the design condition, so **`c3` = 0 at the double zero is an
unfitted condition on `d1..d4`** - and it comes from the published calibration scheme rather
than from any number this model produced. That makes the depth search well posed for the first
time: four parameters, one sharp condition, plus `dc1/dTE1` = 1 as an independent check.

### And it sets the energy acceptance of a lower-resolution instrument

With `c1` and `c2` cancelled the arrival spread goes as `c3` s^3, so resolving power goes as
s^-3:

| energy half-spread | +/-3.0% | +/-2.0% | +/-1.8% | +/-1.0% | +/-0.5% |
| --- | --- | --- | --- | --- | --- |
| R, mirror aberration alone | 4,482 | 15,126 | **20,749** | 121,007 | 968,054 |

**So 20,000 comes with +/-1.82% of energy acceptance from the two-vector calibration alone**,
and reaching the +/-3 to 5% a compact design would like needs `c3` cancelled by the geometry
too. That is the number that decides whether a lower-resolution instrument needs the
third-order focus, and it is now measured rather than assumed.
