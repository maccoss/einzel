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

### The design condition, and the gap to the published instrument narrowing to 6.3x

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
