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

**The mechanism is verified; the instrument is not reproduced.** Every physical effect the
analyser depends on is present and behaves correctly under controls. Two numbers are badly
wrong, and they are probably one problem rather than two.

### Verified, with controls

| | | where |
| --- | --- | --- |
| the ion flies the analyser | 120.058 µs against a predicted 120.1 | §3 |
| drift rate | 1374 m/s against `v·sinθ` = 1374 | §3 |
| parallel mirrors exert no axial force | 1374.34 m/s in **every** 40 mm segment, to the last digit | §3 |
| ~~converging mirrors decelerate the drift~~ | ~~monotone to 1174 m/s at 800 µm~~ — produced by the §12 artefact, not by the declared tilt | §12 |
| ~~and reverse it~~ | ~~out to z = 321 mm, stops, returns~~ — same | §12 |
| ~~reversal threshold against injection angle~~ | ~~fourth-power law~~ — an artefact of an unresolved, wrong-signed tilt, see §12 | §12 |
| the foil produces a drift well | −19.9 V of −20 applied; shallow at 41% of the drift and deep at 67%, matching its own measured contour | §11 |
| a volume solve contributes nothing outside its box | mirrored half reproduces the full solve to 0.00000 V of 100 applied | §7a |

The controls carry more weight than the values. A parallel pair reporting the same drift
rate in every segment *to the last digit* is what says the deceleration is the convergence
and not a numerical artefact; the tilted case is meaningless without it.

### The two numbers that are wrong

**Reversal at the published spacer is unmeasured, because every convergence result so far
was computed on a mirror tilted by about 8% of what was declared, in the wrong direction —
see §12.** The tilt of abutting strips moves only metal-to-metal edges, which cut cells do
not represent, so the solver saw a mesh-dependent fraction of it; and the template's tilt
sign made the mirrors diverge along the drift. The 57× figure that circulated earlier is
doubly superseded. The numbers below are what the document *had* reconciled to, kept for
the record and **withdrawn**:

| | model reverses at | published spacer | gap |
| --- | --- | --- | --- |
| template's 1.28° (a ballistic count, and wrong) | **0.267 mm** | 0.200 mm | **1.33×** |
| paper's 2°, at the measured impulse efficiency η = 0.578 | ~0.85 mm | 0.200 mm | **~4×** |
| paper's 2°, a perfectly specular mirror (η = 1) | ~0.49 mm | 0.200 mm | **~2.5×** |

**η = 0.578 was the discretisation, not the mirror.** A single reflection off the solved
mirror delivers 0.447 of the specular kick at a 4 mm cell; with one resolvable vacuum gap
between the strips it delivers 1.045; an analytic tilted mirror delivers 1.0025 (§12). What
survives is arithmetic that uses no solve: **a specular mirror with the published 200 µm
spacer reverses a 335 mm drift only if that drift is about 1.47°**, against [A]'s "about two
degrees" — so at a strict 2° something beyond mirror tilt must supply a returning potential,
and [A] names the ion foil for that role while §11 finds the measured foil contour cannot
do it at a single bias. **That contradiction is real and still open, but it cannot be
attacked until the tilt is solved correctly.** The fix is §12's sheared-field wrapper, which
also turns each mirror into a 2-D solve.

**Resolving power is 6.56 against a published >100,000.** Decomposed on the energised
template, each spread on its own with the others at zero:

| spread | R | packet radius |
| --- | --- | --- |
| energy ±2.5% | 2,600 | 0.70 mm |
| longitudinal 0.5 mm | 1,750 | 0.02 mm |
| transverse 0.5 mm | 628 | 1.61 mm |
| **300 K thermal** | **11.5** | **25.8 mm** |
| all together | 6.56 | 19.9 mm |

**The mirrors do energy-focus** — R = 2,600 on energy alone says so, and that is the thing
an MR-TOF is for. What destroys the resolving power is thermal spread in the *drift*
direction, which nothing in this model ever undoes.

### They are probably one problem

The crowd-control paper says the packet is **deliberately** allowed to spread to 50 mm, to
minimise Coulomb repulsion in the bunch, and is then refocused on the return leg by the
mirror convergence and the ion foil *together*. So the real instrument carries a strong
drift-direction restoring force that both stops the drift at 200 µm of convergence and
pulls the spread back in. This model has neither.

**One missing mechanism would account for both numbers.** Hunting two separate explanations
is the likely way to waste a week here.

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

0. **Make the tilt real** — §12. Done in the template as a stopgap: sign corrected, 3 mm
   vacuum gaps between the strips. Proper fix: solve each mirror untilted as a 2-D
   cross-section and query it through a shear, `φ₀(x − α(z − z_c), y)`. **Then re-measure
   every convergence result in §3**, since all of them are withdrawn.
1. **Fit `d1..d4` against the measured reversal distance**, potentials held at the published
   ratios. Well-posed once the tilt is real.
2. **Correct the injection angle to 2°.** Cheap, and it removes a contradiction with a
   published value.
3. **Measure whether the foil well focuses a z-spread packet, and with what focal length.**
   The restoring force has the right sign by inspection of the well; its strength against a
   50 mm spread is a flight, not a solve.
4. **Table 1's perturbation vectors** (C⁽¹⁾, C⁽²⁾, ~2.5 ppm/V per unit `TE1`) give a
   *differential* test of the mirrors that does not require the absolute focus to be right
   first — the sharpest literature regression available here. J. Mass Spectrom.
   2024;59(4):e5006, <https://doi.org/10.1002/jms.5006>.

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

### Committed and green (1,085 tests)

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

### The fix, three ways

1. **Vacuum gaps between strips, at least one cell wide, and the tilt sign corrected** — a
   stopgap that needs no code, and is physically honest: a printed board has gaps between
   its traces. At 4 mm cells and 1.0 mm convergence the efficiency drifts to 1.85, so the
   gap is itself only marginally resolved at that cell; a stopgap, not an answer.
2. **Solve the mirror untilted and query it through a shear.** The exact field of a rigidly
   rotated z-invariant structure is the rotated field, so `φ(x, y, z) = φ₀(x − α(z − z_c), y)`
   is right to O(α²) ≈ 2e-7. Same pattern as `AxisymmetricField`, `TimeShiftedField` and
   `ReflectedField`. **And it makes each mirror a 2-D solve** — the solver that carries every
   validated number here — leaving only the foil genuinely three-dimensional. This is the
   right fix.
3. Refine until an edge displacement exceeds a cell: 0.075 mm cells. Not available.

The lesson, recorded in `docs/lessons.md`: **a geometric perturbation that moves only
metal-to-metal boundaries is invisible to cut cells**, and a discretisation check on one
boundary type says nothing about the other.
