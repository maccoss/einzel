# Astral 3-D modelling: handoff to a dedicated machine

**Written 2026-08-31.** For moving the Astral analyser work onto a faster machine and
running it unattended. It says what exists, what is measured, what is broken, and — most
importantly — what will waste your time if you do not know it in advance.

Read `SPEC.md` first, as always. This page is scoped to the Astral work.

---

## 1. What this is trying to be

A three-dimensional model of the **Thermo Astral** analyser: an asymmetric-track
multi-reflection time-of-flight instrument. Published, and used here:

| | |
| --- | --- |
| Beam energy | 4 keV |
| Mirror electrodes | five per mirror — one grounded, one strongly accelerating, three reflecting |
| Table 1 coefficients (× ion energy) | U1 −1.840, U2 −1.158, U3 +0.916, U4 +1.503 |
| Optimised for | flat time-of-flight over **4000 V ± 100 V** |
| Oscillations / flight path | **24 / 30 m** → 625 mm cap-to-cap (derived, not stated) |
| Drift distance | 310–360 mm |
| Mirror convergence | **200 µm spacer**, drift decelerates over the first 12–13 oscillations |
| Resolving power | > 100,000 |
| Ion foil | electrodes above and below the path, biased 0 to −20 V |

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

### Committed and green (1,047 tests)

- **`astral-mirror.json`** — the 2-D five-electrode mirror at published potentials, with
  `d1..d4` as free lengths. `AstralMirrorStudy` drives the shipped optimiser over them.
- **Tilted boxes** (`tiltAxis`, `tiltHalfTurns`) — the convergence is now expressible.
  Measured proportional down to **a thousandth of a cell**. Half turns, so `1.0` is 180°
  and a right angle is `0.5`; the Astral's 200 µm over 350 mm is `1.8e-4`.
- **Neumann faces on `solve3d`** (`lowerZEdge` … ) — see §4, this was the blocker.
- **`einzel estimate` costs a study**, calibrated on the machine that will run it. Use it.

### Scratch, not committed

The skeleton itself is throwaway JSON. **Do not copy it — regenerate it** from the script
in §3, which is the corrected version and is the only copy that matters.

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

- **Ion foil electrodes.** Published as biased 0 to −20 V above and below the path, shaped
  to compensate temporal aberration. Not in the skeleton.
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

1. ~~**Make one ion complete one flight**~~ — done, §3. 120.058 µs against a predicted
   120.1, drift exact, 16,012 steps.
2. **Refine the mesh and watch the energy drift.** At a 4 mm cell it is 2.16e-6, just over
   ACC-4's 1e-6 budget. It should fall with refinement; if it does not, that is a finding
   rather than a nuisance. Sit at the cheap side of the mesh cliff (§5) and confirm the
   flight time is unchanged.
3. **Add the convergence** (`tiltHalfTurns` on the mirror boards, tilted about x) and show
   **the drift decelerates and reverses**. This is the first result that is about *this*
   instrument rather than a generic MR-TOF, and it is what should raise the oscillation
   count from 3.77 toward 24 (§3, last subsection).
4. **Then** the inverse problem: shard a scan over `d1..d4` (§6) against the published
   4000 ± 100 V window, using the 2-D `AstralMirrorStudy` result as a starting point.
5. Ion foil and prisms, if 1–4 hold up.

Estimate before each of 2, 3 and 4, and read the basis line rather than only the number.

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
