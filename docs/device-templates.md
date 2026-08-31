# Device templates

A device template is a model document with a declared parameter surface. It is
**data**, not code — embedded JSON in `Einzel.Library/Templates/`.

That is the whole of LIB-1, and the test it sets is sharp: if supporting a new
device requires a change below `Einzel.Library`, either it is genuinely novel
physics or the abstraction is wrong, and almost always the second.

## What ships

| Template | What it is |
| --- | --- |
| `planar-mirror-pair` | Two printed-circuit ion mirrors facing each other, solved across the board gap and reflected to make the pair |
| `quadrupole` | Four round rods in cross-section, alternating potential |
| `rectilinear-trap` | Four flat plates around a square aperture, the front one split by an extraction slot |
| `einzel-lens` | Three coaxial tubes, outer two earthed, solved axisymmetrically |
| `quadrupole-rf` | The same four rods, driven: a mass filter |
| `ion-funnel` | A tapering stack of RF rings with a DC gradient, written as one ring repeated |
| `segmented-quadrupole` | Three axial sections at their own working points, solved in three dimensions |
| `travelling-wave-guide` | A ring stack whose drive phase ramps along it, so the potential travels |
| `multipole-guide` | Any even order — quadrupole, hexapole, octupole, and beyond — from one file |
| `paul-trap` | A driven ring between two earthed endcaps: the three-dimensional quadrupole trap, solved axisymmetrically |
| `kingdon-trap` | A wire on the axis of a cylinder: the electrostatic orbital trap, and the ancestor of the Orbitrap |
| `orbital-trap` | A quadro-logarithmic field: ions circle a spindle while oscillating along it, and the axial frequency is the measurement |
| `c-trap` | Four rods bent around an arc: the curved RF trap that injects an orbital analyser |

They **share no code at all**. They name the same electrode primitives in
different arrangements; everything below reads a Dirichlet mask without knowing
which is which. Adding a device is a new file.

```csharp
DeviceTemplates.Names();
// ["einzel-lens", "ion-funnel", "multipole-guide", "paul-trap",
//  "planar-mirror-pair", "quadrupole", "quadrupole-rf", "rectilinear-trap",
//  "segmented-quadrupole", "travelling-wave-guide"]
DeviceTemplates.Read("quadrupole");
```

Templates are discovered by an embedded-resource glob, so a new JSON file under
`Templates/` registers itself - it appears in `einzel templates`, in
`einzel new --from-template`, and in `DeviceTemplates.Names()` with no code
touched anywhere. That is LIB-1 being true rather than merely intended.

### What the third device cost

Spec section 21 phase 5 sets the test of generality as "a second, unrelated
instrument modelled by someone who did not write the code". The rectilinear trap
is the third, and it needed **no change below `Einzel.Library`** - but it did
force three additions to the *model format*, each of which was an assumption about
beams that a trap does not meet:

- **A source may start at rest.** The accelerating potential was required to be
  non-zero, "or the ion never moves". True of a beam; false of a pulsed extraction
  trap, whose packet sits still until the instrument switches a field on. Zero is
  now legal when a field is declared that could accelerate it, and still refused
  when nothing could.
- **A vector placement may be parametric.** Spec section 9 says every placement is
  an expression rather than a baked number, and scalars always were. Vectors were
  not, so a detector anywhere but the origin had to bake coordinates - which the
  mirror and the quadrupole both did, because both happened to be symmetric about
  something convenient. `planePoint` now takes `["drift", "0", "0"]`.
- **A dimensionless zero satisfies any dimension.** A consequence of the second:
  the expression grammar has no unit literals, so a bare `0` is dimensionless and
  there was no way to write "on axis". Narrow on purpose - zero is the only value
  whose unit conversion is the identity, and a dimensionless *one* is still
  refused.

None of these is device-specific, which is the useful part. The trap did not need
the format bent toward traps; it needed three places where the format had quietly
assumed a beam.

## Writing one

Four things make a template a template rather than just a model.

**Name every dimension that a study might vary**, and give it bounds and a
description. Bounds carry design intent: a mirror depth that may run 20–300 mm
says something a bare nominal does not, and it is what lets a study say "vary
everything over its declared range" instead of restating limits the template
already knows.

```json
"mirrorDepth": {
  "value": 90.0, "unit": "mm",
  "minimum": 20.0, "maximum": 300.0,
  "description": "Depth of the printed mirror, entrance plane to cap."
}
```

**Bound anything an optimiser might search.** `Optimiser` refuses an unbounded
design variable rather than inventing a range from the nominal value, so a
parameter with no `minimum` and `maximum` is one nobody can optimise without
saying the bounds again at the call site. The quadrupole's `rodRatio` is bounded
for exactly that reason, and the optimiser recovers the classical 1.1468 from it.

**Bound the numerics too, not only the physics.** `housingClearance` and
`cellsPerRadius` exist so that the effect of the grounded box and of the mesh on a
result can be *measured* rather than argued about. Both turned out to matter for
the rod ratio, and the mesh mattered more.

**Derive everything that is a consequence.** A derived parameter is not a knob,
and marking it as one would hand an optimiser a dimension it must not search.

```json
"halfGap":  { "expression": "boardGap / 2", "unit": "mm" },
"midPlane": { "expression": "capToCap / 2", "unit": "mm" }
```

Derived parameters re-evaluate against overrides, which is what makes sweeping
meaningful: perturb `capToCap` and `midPlane` follows.

**Express geometry in terms of parameters, never in baked numbers.** Bake a design
down to coordinates and "move this stripe 50 µm and re-solve" stops being sayable,
which is the whole point of the tolerance machinery.

**Write the description for someone who has never seen the platform.** There are
no forum posts and no decades of example files to fall back on. Say what the
device is, what varying each parameter does, and what result to expect. The
shipped templates state their expected behaviour explicitly — the quadrupole's
says the potential should go as (x² − y²) near the axis and the restoring force
should be linear.

## The rectilinear trap, and what solving it bought

Four flat plates around a square aperture at r0 = 2 mm, the front one split by a
1 mm extraction slot, with corner gaps so adjacent plates are not shorted. It is
the cross-section of the Ion Processor
([Literature targets](literature-targets.md)), and it carries two configurations
in one file: set the side plates against the front and back and it is a trap, set
the back plate high and it extracts.

**As a trap, it is a crude quadrupole — and the slot costs more than the plates
do.** Measured the same way as the round-rod device, on the same quantity, so the
comparison means something:

| | Largest unwanted multipole |
| --- | --- |
| Round rods at the classical 1.1468 | order 6, at 2.41e-5 |
| This trap | **order 1**, at 5.43e-2 |

**2,258 times worse.** The dominant term is a *dipole*, not the 12-pole — and a
dipole is not a distortion of the well, it is a displacement of its centre, which
for an extraction trap is the aberration that matters most.

Attributing it takes one more measurement. Narrowing the slot from 1.0 mm to
0.1 mm leaves the flat plates untouched and removes the asymmetry about the
extraction axis:

| | 1.0 mm slot | 0.1 mm slot |
| --- | --- | --- |
| Dipole (order 1) | 5.43e-2 | 5.69e-4 |
| 12-pole (order 6) | 7.12e-3 | 6.06e-3 |

The dipole collapses by 96x and the 12-pole barely moves. So **the 12-pole is what
flat plates cost — 6.1e-3 against round rods' 2.41e-5, about 250x — and the dipole
is what the slot costs, seven times larger again.** Neither is a defect: a
rectilinear trap is chosen because flat plates are easy to make and easy to cut a
slot in, and this is the bill.

> An earlier version of this page reported 7.12e-3 and "296 times worse",
> attributing the 12-pole to the plates and stopping there. That measurement
> projected the potential onto cosines only, which is exact for four identical
> round rods — they are four-fold symmetric, so the odd orders vanish identically —
> and blind for this trap, whose slot breaks the symmetry about the *x* axis and
> puts the asymmetry entirely into the sine terms. The projection now carries both
> phases.

**As an extractor, the closed form is wrong by 19%.** Turn-around time is
2sqrt(2 ln 2) sqrt(mkT) / qE, and the question is what to use for E. Assuming
V / 2 r0 gives 3.448 ns; the field the geometry actually produces is 81.8% of that,
giving 4.215 ns, against 4.243 ns measured by flying the packet.

| | m/z 500, 300 K, 1 kV push |
| --- | --- |
| Closed form at the naive field | 3.448 ns, **18.8% low** |
| Closed form at the solved field | 4.215 ns, **0.7% low** |
| Measured through the geometry | 4.243 ns |

This is the same lesson as the mirror's four-penetration-depth rule being wrong by
10 mm. The formula is right; the number fed into it is not, and only the solve
knows the difference.

**And turn-around is the least of what sets the peak.** Three properties of the
packet reach the arrival time, and switching them on one at a time separates them:

| Contribution | FWHM |
| --- | --- |
| Thermal velocity (turn-around) | 4.28 ns |
| Depth, 0.2 mm along the extraction | 231.9 ns |
| Width, 0.2 mm across it | 12.3 ns |
| All three, measured | 241.4 ns |
| The three in quadrature | 232.3 ns |

Turn-around is **1.8%** of the total, which matters when reading a published
number: a figure near a nanosecond cannot be the arrival spread of a packet this
deep.

Quadrature closes to 3.8% rather than exactly, and the gap is informative. Adding
an aperture makes the three contributions *not quite* independent, because which
ions survive depends on depth and width together — the population that arrives
with all three spreads on is not the population either pair-wise run measured.
That coupling is a property of having a real aperture.

> These figures moved once electrodes started stopping ions. The width row was
> **87.2 ns** when a fifth of the cloud flew through the front plate rather than
> being lost on it; with the plate solid it is 12.3 ns. The thermal and depth rows
> did not move, since neither involves a transverse excursion.

There is also **no useful space focus**. A single-stage extraction should have a
Wiley-McLaren focus at twice the source depth - about 6 mm here - where the ion
that started deeper catches the one in front. Scanning the drift from 2 to 11 mm
the spread grows monotonically at 20.7 ns/mm, so the focus is at essentially zero
drift and any usable detector is far past it. That is what a field varying by a
factor of two across the packet does to a condition derived for a uniform one, and
it is why a real instrument adds a second acceleration stage rather than moving the
detector.

### The slot does something

**Half the beam lands on the plate.** With the shipped parameters — a 1 mm slot and
a 0.2 mm packet — the run reports:

```
cloud         1015 of 2000 ions arrived, transmission 50.7 % +/- 1.1 %
  lost        509 on frontPlateRight (25.5 %)
  lost        466 on frontPlateLeft (23.3 %)
  lost        6 on sidePlateXPlus (0.3 %)
  lost        4 on sidePlateXMinus (0.2 %)
```

That is ACC-5's "transmission itemised by loss surface and mechanism", and the
reason the requirement is written that way: `frontPlateRight` is a thing to move,
where "transmission is 51 percent" is only a thing to worry about.

Note that the loss is much larger than the packet's own width would suggest. A
0.2 mm Gaussian is 98.8% inside a +/-0.5 mm slot at launch, so most of the loss
happens *on the way*: the packet spreads across the 2 mm to the plate, and the
aperture is a diverging lens for an accelerating ion. That is the sort of thing a
solve tells you and an area ratio does not.

### What it does not model

**The auxiliary DC electrodes are not here.** The Ion Processor's are diagonal
wedges and horizontal pairs that impose a gradient *along* the trap axis. Every
solve here is a cross-section with translational invariance along that axis, so an
axial field cannot be represented at all. That needs three dimensions, not another
rectangle.

**Electrodes are solid, with no way to say otherwise.** Real instruments use mesh
and grid electrodes that are transparent to most of the beam, and there is
currently no way to declare one — a mesh would have to be modelled as its wires,
which the cross-section cannot do either.

## The einzel lens, and why it needed a new operator

Three coaxial tubes, the outer two earthed and the middle one at a voltage. It is
the device the platform is named after and **it could not be modelled at all until
this turn**, because a translational cross-section turns three tubes into three
pairs of bars - which deflect rather than focus. `"symmetry": "cylindrical"` on the
solve makes x the axis of rotation and y the radius, and a rectangle in that
half-plane is a ring in space.

With the shipped parameters - 5 mm bore, 500 V on the middle electrode, a 1 keV
beam - a ray launched 1 mm off axis and parallel to it crosses at 129.1 mm, so the
focal length is 81 mm, about sixteen bore radii.

**It converges for either sign of the middle voltage.** That is the classic
non-obvious property of an einzel lens and the check a merely plausible field
fails. The ion passes through one converging gap and one diverging gap whichever
way the electrode is driven; it is slower in the converging one when the middle
decelerates it, and faster in the diverging one, and the asymmetry always favours
convergence.

| Middle electrode | Crossing |
| --- | --- |
| +500 V | 129.1 mm |
| -500 V | 273.3 mm |

The decelerating sign is much the stronger, which is why real lenses are usually
run that way - and why running one too close to the beam energy makes it a mirror
instead.

| Middle / V | Focal length |
| --- | --- |
| 300 | 287.3 mm |
| 400 | 143.8 mm |
| 500 | 81.1 mm |
| 600 | 48.8 mm |

**Spherical aberration comes out in the right direction**: a ray at 2 mm focuses
4.7 mm shorter than one at 0.5 mm. Every real lens has it and it is why a beam
focuses to a blur; a paraxial field would put every ray in the same place.

### What "unipotential" means, measured

Both outer electrodes are earthed, so an ion that starts and ends inside them has
fallen through no net potential. The check is exact and it passes - **total energy
is conserved to 6.4e-10** across a path that crosses a strong field twice.

The ion's *kinetic* energy, though, comes back only to 2.5e-6. That is not the
integrator, it is the instrument: the launch point sits a quarter of the way down
the entrance tube, where the middle electrode's field has not quite finished
decaying, and the potential there is 2.457 mV against a 1000 V beam - which is the
2.458e-6 discrepancy to four figures.

So **a lens is unipotential only to the extent its tubes are long**, the residual
falls as exp(-2.405 L / r), and how long is long enough is a design question the
solve can answer rather than an assumption to make.

## The segmented quadrupole, and what three dimensions cost

A quadrupole cut into three axial sections - prefilter, main, postfilter - each at
its own working point. **The first device here a cross-section cannot express at
any resolution**: what makes it a segmented filter is that the field changes
*along the axis*, and that is exactly the direction a translational solve is
invariant in. It is not a more accurate quadrupole, it is a different instrument.

**Twelve rod segments reduce to one basis solve.** The two pairs within a section
are exact negatives, and the sections are tapped off the same generator in a fixed
ratio at the same phase, so the whole structure is a single spatial pattern with a
single weight. The decomposition that finds this is the same code the plane uses -
nothing about it is dimensional.

Switch the analysing DC on and it becomes **two** solves, and that is the physics
rather than an accounting detail. The coupling is a **capacitor**: it passes the RF
and blocks the DC, so the prefilter sees the drive and not the offset, and the two
supplies stop reaching the electrodes in the same proportions. Replace it with a
resistive tap that passes the DC in the same ratio and it collapses back to one.

That capacitive coupling is also what the prefilter is *for*: ions meet a confining
field before they meet the analysing one, instead of crossing the DC fringe on the
way in.

**The sections really do sit at different working points**, measured from the
solved field rather than from the applied voltages:

| Transverse field at r0/2 | |
| --- | --- |
| Prefilter | 224.3 kV/m |
| Main section | 258.7 kV/m |
| Ratio | 0.867 against a declared coupling of 0.850 |

The 2% is each section's own ends bleeding into its middle, which is a real
property of a 22 mm section at r0 = 4 mm.

An ion tracked through the whole structure arrives 0.26 mm off axis after 54 µs and
3,982 steps.

### It filters, and in the right place

| Main amplitude | q | |
| --- | --- | --- |
| 300 V | 0.367 | through |
| 700 V | 0.855 | through |
| 745 V | 0.910 | lost on `mainYMinus` at z = 38.7 mm |

**The cut-off brackets the ideal Mathieu boundary of q = 0.90804** - on round rods,
cut into three sections, with gaps and end fringes, at 8.5 cells across r0.

And the ion is lost in the **main** section, not the prefilter. That is the
segmentation doing its job: the entrance sits at 85% of the main amplitude, so its
q is 0.85 of the main one and it stays stable while the analysing section ejects. A
filter that lost ions in its prefilter would be a filter with an expensive
decoration on the front.

> This is not where the number started. Before the coarse multigrid levels were
> made node-aligned, the ion was lost at **q = 0.611**, and the first explanation
> written down for it was field quality at a coarse mesh. That was wrong: refining
> the mesh moves the mid-section transverse field by **0.014%**, as the table below
> shows. It was an under-converged solve, and fixing the multigrid moved the
> boundary from 0.611 to the right answer. A wrong number with a plausible
> explanation attached is the expensive kind, and the explanation is what made it
> expensive.

### What is converged, and what is not

| asked | grid | cells across r0 | mid-section | segment gap |
| --- | --- | --- | --- | --- |
| 4 | 33x33x129 | 4.26 | 125.38 kV/m | 107.91 kV/m |
| 5 (shipped) | 65x65x129 | 8.53 | 125.36 kV/m, 0.014% | 110.48 kV/m, 2.4% |
| 8 | 65x65x257 | 8.53 | 125.36 kV/m, 0.000% | 112.02 kV/m, 1.4% |

Two probes, because refining one axis at a time is what actually happens here.
`OverBox` rounds each axis up to a power of two independently, so asking for 5 and
asking for 8 give the **same transverse mesh** and differ only axially - and the
shipped mesh is 8.5 cells across r0, not the 5 that was asked for. An earlier
version of this page said "five cells across r0" because it labelled the study by
the request rather than by the grid.

**Mid-section: converged.** 0.014% across a genuine transverse refinement and 0.000%
under axial refinement, which is what the transmission boundary above rests on - the
ion is lost at z = 38.7 mm, in the middle of a 24 mm section, nowhere near a join.

**Segment gap: not converged, and still moving at the finest mesh tested.** 2.4% then
1.4%. At 1 mm the gap is one to two cells across, and a point probe in a steep axial
gradient is the most mesh-sensitive thing this geometry has. So nothing on this page
claims what the gaps *do*. Settling that needs either a mesh this template cannot
afford in three dimensions, or a measure integrated along a trajectory rather than
sampled at a point.

That is worth stating plainly because it is the one claim a segmented quadrupole
would most like to make. The template demonstrates that segments at different
working points can be *declared, decomposed and solved*; it does not yet demonstrate
what the joins between them do to an ion.

### What it costs

A solve is a few seconds; a full run with tracking is fifteen to fifty, depending
on how far the ion gets. At eleven cells across r0 it does not finish in ten
minutes, which is the practical ceiling worth knowing.

## The funnel, and what a stack costs

A column of ring electrodes whose apertures taper from 12 mm to 1.5 mm, driven in
two RF phases with a DC chain pushing ions along - written as **one ring repeated**.

**The solve count does not grow with the ring count.**

| Rings | Electrodes | Basis solves |
| --- | --- | --- |
| 8 | 8 | 2 |
| 24 | 24 | 2 |
| 48 | 48 | 2 |

That is SYM-1's argument measured: "a 200-ring funnel driven in two RF phases needs
two RF basis fields plus a DC gradient, not 200 basis solutions". It comes out at
two rather than three because the two RF phases are exact negatives of one another,
so they are one spatial pattern carrying one weight; three phases that were not
negatives would be three.

The resistor chain is a **single supply holding twenty-four different voltages**,
which is the case that makes "group by spatial pattern" the right rule and "group
by identical potential" the wrong one.

### It funnels, and the RF is why

An ion entering 6 mm off axis - half way to the wall - threads the whole stack and
exits through the 1.5 mm aperture, so it was compressed by at least 4x. Switch the
drive off and only the DC gradient is left, which pushes the ion forward and does
nothing to keep it off the metal: it ends on `ring-14`.

Acceptance falls off with entry radius the way a funnel's should:

| Entry radius | |
| --- | --- |
| 1 mm | through |
| 3 mm | through |
| 6 mm | through |
| 9 mm | lost on `ring-22` |
| 11 mm | lost on `ring-10` |

> **No gas.** A real funnel runs at around a millibar and the collisions are half
> the mechanism - they damp the radial motion so ions settle onto the axis instead
> of ringing about it. Everything here is the field and the confinement without the
> cooling, so the acceptance above is a lower bound on the real one.

### The sign that has to be right

The DC chain starts at zero on the first ring and falls to `-dcGradient` on the
last. Put the high potential at the entrance instead and the grounded boundary
beside it pushes the ion straight back out - which is what happened first, and the
run said so plainly: the ion ended 3 metres upstream.

## The quadrupole, as a worked example

It is four discs and a box:

```json
"parameters": {
  "inscribedRadius":  { "value": 5.0, "unit": "mm", "minimum": 1.0, "maximum": 50.0 },
  "rodRatio":         { "value": 1.1468, "unit": "1", "minimum": 1.0, "maximum": 1.4 },
  "rodPotential":     { "value": 100.0, "unit": "V" },
  "housingClearance": { "value": 1.6, "unit": "1", "minimum": 0.2, "maximum": 8.0 },
  "cellsPerRadius":   { "value": 16.0, "unit": "1", "minimum": 4.0, "maximum": 128.0 },
  "rodRadius":        { "expression": "inscribedRadius * rodRatio", "unit": "mm" },
  "rodCentre":        { "expression": "inscribedRadius + rodRadius", "unit": "mm" }
}
```

with four `disc` electrodes at ±`rodCentre` on each axis, the x-pair at
`+rodPotential` and the y-pair at `−rodPotential`. The 1.1468 ratio is the
classical optimum for approximating a hyperbolic field with round rods.

Verified against the analytic form:

```
   r (mm)     phi(x) (V)    phi(y) (V)     Ex/x (V/m^2)
    0.500        0.9264       -0.9264     -7.4112E+006
    1.000        3.7055       -3.7055     -7.4109E+006
    2.250       18.7492      -18.7492     -7.3986E+006

Ex/x spread 0.17% across the central 45% of r0
ideal hyperbolic ratio 0.9260
```

Φ(x) = −Φ(y) exactly, and Ex/x constant to 0.17% — a linear restoring force,
which is the property that makes a quadrupole a mass filter once the potential is
made to oscillate, and the premise the Mathieu equation rests on. The 0.926 ratio
to the ideal hyperbolic field is the expected round-rod approximation.

## The mirror pair

Two features worth noticing.

**The second mirror is the first, reflected.** `reflectAboutX` declares it in the
document, so both halves are the same solve by construction and a difference
between an inbound and an outbound leg cannot come from their having been meshed
differently.

**The boards are `edgeProfile` electrodes.** One electrode spanning many nodes,
because that is how it is driven — one supply feeding a resistive divider — with
the ramp given as piecewise-linear breakpoints. Setting `firstStageFraction` to 0
gives a single-stage mirror; a positive value gives the two-stage Mamyrin
arrangement.

One consequence of solving rather than assuming, worth stating because a design
that missed it would be designing a mirror it does not have: **the applied stripe
profile and the potential on the ion's path are not the same function.** The kink
at a stage boundary is smoothed over roughly the board gap by the time it reaches
the mid-plane, because the boundary-value problem damps every Fourier component of
the profile by cosh of its wavenumber times the half-gap.

## What is missing

The primitives are `rectangle`, `disc`, and `edgeProfile` in two dimensions with
translational invariance. That covers planar mirrors, plate stacks, apertures, and
multipole cross-sections.

It does not yet cover **axisymmetric** geometry, which is what an einzel lens or
an ion funnel needs, or **discrete periodicity**, which is what a stacked-ring
guide needs to be expressed compactly rather than as hundreds of rectangles.
Both are symmetry declarations the solver would exploit, and both are the natural
next additions.

---

## `multipole-guide` — every even order in one file

LIB-1's test, run deliberately: **what does a multipole above four rods cost?**

It cost exactly one thing below `Einzel.Library`, and it was small and general.
The expression grammar had **no trigonometry**, so `2n` rods at `π/n` intervals
could not be written at all — not awkwardly, not verbosely, but not at all. With
`cosPi` and `sinPi` added it is one template with `poleCount` as a parameter:
four is a quadrupole, six a hexapole, eight an octupole, and nothing else changes.

**Half turns rather than radians**, which is the convention the drive decomposition
already chose and for the same reason: `Math.Cos(Math.PI / 2)` is 6.1e-17 rather
than zero, so a rod placed at a quarter turn lands a hair off axis and the
multipole carries a spurious dipole made of rounding.

### The rods have to fit, and now they cannot not

| poles | largest ratio | closed form | actual | nearest gap |
| --- | --- | --- | --- | --- |
| 4 | 2.41421 | 2.41421 | 1.14675 | 2.970 mm |
| 6 | 1.00000 | 1.00000 | 0.47500 | 2.100 mm |
| 8 | 0.61991 | 0.61991 | 0.29446 | 1.607 mm |
| 10 | 0.44721 | 0.44721 | 0.21243 | 1.298 mm |
| 12 | 0.34920 | 0.34920 | 0.16587 | 1.087 mm |

Rod centres sit on a circle of `r0 + rodRadius`, adjacent centres are
`2(r0 + rodRadius) sin(π/N)` apart, and that must be at least twice the rod
radius — which rearranges to `rodRatio ≤ sin(π/N) / (1 − sin(π/N))`.

**So the knob is `rodFill`, a fraction of that maximum, not the ratio itself.** An
overlapping geometry is then not expressible rather than merely refused. And
`rodFill = 0.475` reproduces Denison's classical quadrupole ratio of **1.1468** at
four poles, reached through the derived-parameter chain rather than written into
it — which is a sharp check on `sinPi` as well as on the geometry.

### Every order is one basis solve

| poles | electrodes | basis solves | cycles | convergence factor |
| --- | --- | --- | --- | --- |
| 4 | 4 | **1** | 8 | 0.0262 |
| 6 | 6 | **1** | 8 | 0.0285 |
| 8 | 8 | **1** | 8 | 0.0236 |
| 12 | 12 | **1** | 8 | 0.0257 |

Twelve rods cost what four do. Adjacent rods alternate in phase, so they are exact
negatives of one another however many there are, and the whole structure is one
spatial pattern whose weight is a function of time. **Exact negation is what does
it** — which is why the amplitude is written `rfAmplitude * (1 - 2 mod(pole, 2))`
rather than as a cosine of the pole index: the second would be right to a rounding
and would split into two channels.

### What is not claimed, and why

The obvious question is whether a higher order accepts a larger offset, and this
template can be made to answer it — a boundary search on `launchOffset` costs
eleven evaluations per order. **It is not claimed, because the measurement as set
up is confounded.**

The template launches at `(offset, offset)`, a 45° diagonal. For a quadrupole,
with rods on the axes, that is the *widest* gap between rods: an ion enters at
r = 4.95 mm and still arrives, outside the 4 mm inscribed radius. For a hexapole
the same diagonal falls between rods at 0° and 60°, a narrower gap. So the
comparison measures the angular gap the launch point happens to sit in at least as
much as it measures the order.

Measured anyway, for the record: at 200 V the hexapole accepts 0.68 r0 and the
octupole 0.58; at 300 V that **reverses** to 0.46 and 0.48. A non-monotone ordering
that flips with amplitude is a sign the variable being scanned is not the one that
matters. Settling it needs a scan over launch *angle* as well as radius, and an
acceptance defined as a solid angle rather than one ray.

## Overlapping conductors are refused

Found by getting the above wrong first. Applying Denison's 1.1468 to six rods puts
them **through one another** — they need a centre circle 9.17 mm across and a
hexapole gives them 8.59 mm — and the engine solved it, converged in eight cycles,
and produced an acceptance measurement that was really a measurement of rods
closing in on the axis.

A Dirichlet mask is built by writing each electrode's nodes in turn, so where two
overlap the last one written wins. Where both hold the same potential and drive
that is harmless and often deliberate: a shape assembled from overlapping
primitives is how a fillet or a shoulder gets built. **Where they disagree it is
ill-posed** — the region is simultaneously at +300 V and −300 V of drive, and the
field returned is the field of a geometry nobody described.

`ElectrodeOverlap` refuses that case, naming both electrodes and what each holds.
Three deliberate limits: tangency is allowed, because exactly touching is a design
and a floating-point equality is a poor thing to refuse on; agreement is allowed,
because the overlap is not the problem; and an **edge profile is skipped**, because
a boundary profile touching an interior electrode is a different question and a
check that guessed would sometimes refuse a legitimate geometry.

## `paul-trap` — the 3-D quadrupole trap, and where its cut-off really is

A driven ring with an earthed endcap either side of it, on the axis of rotation.
**Axisymmetric, so it is a half-plane solve rather than a volume** — SYM-1 is what
makes a three-dimensional trap cost what a two-dimensional cross-section costs.
Three electrodes, and because the endcaps are earthed there is only one thing that
moves: **one basis solve**, 10 cycles at a convergence factor of 0.0587.

The classical geometry has `r0² = 2z0²`, which collapses

```
q_z = 8 z e V / (m Ω² (r0² + 2 z0²))    →    4 z e V / (m Ω² r0²)
```

— the same volts per unit `q` as a linear quadrupole of the same inscribed radius,
so the two are directly comparable and the amplitude is arithmetic. `z0` is
*derived* from `r0` rather than declared, because departing from that ratio is a
different device rather than a different size.

### A trap needs a figure of merit that is not an arrival

Everything else here is measured by ions arriving somewhere. **A trapped ion never
arrives anywhere**, so a transmission reads zero for a trap that works and zero
again for one that lost everything, and no figure that counts arrivals can tell
those apart. `confined` is the complement — the fraction still inside when the hold
ends, having struck nothing and passed no detector — and the model puts its
detector *outside* the trap so the three outcomes stay distinct: **struck, escaped,
held**.

### Measured

| | |
| --- | --- |
| Basis solves for three electrodes | **1** |
| Ejection boundary, 0.3 mm launch, 200 cycles, 128 × 64 | **672–674 V**, q_z = 0.8218–0.8236 |
| The same at 256 × 128 | **672–674 V** — mesh-converged |
| The same at 800 cycles | **674 V** — hold-converged |
| Tabulated Mathieu boundary, a = 0 line | q_z = 0.90804 |
| Where the ion is lost | an **endcap**, at exactly ±z0 |
| Effective r0 from the solved field | **3.8195 mm** against 4.0000 declared |
| Boundary a scale factor alone would predict | **677.5 V**, q_z = 0.828 |

**Most of the 9.4 per cent shortfall is one number, and the rest is not.** These
electrodes are flat annuli, and a flat annulus at the nominal radius lies *inside*
the hyperbola sharing its vertex everywhere except at that vertex — at z = 2.23 mm
the ring hyperbola would be at r = 5.09 mm and this ring is at 4.00; at r = 3.4 mm
the endcap hyperbola would be at z = 3.71 mm and this endcap is at 2.83. Metal
closer in means a stronger field at the centre than `r0` implies, which is a smaller
effective radius, which is a larger `q` per volt, which is ejection at a **lower**
amplitude. That accounts for the sign and for 0.828 of the 0.908.

### The ejection edge is amplitude-dependent, and it is not the linear boundary

| launch offset | hold-converged edge | q_z |
| --- | --- | --- |
| 0.1 mm | 695–700 V | 0.849–0.856 |
| 0.3 mm | 665–670 V | 0.813–0.819 |
| 0.6 mm | ~520 V | 0.635 |

The Mathieu equation is linear, so a trajectory scaled by a constant is another
trajectory and **an ideal trap's stability boundary cannot depend on how far off
centre the ion started**. This edge depends on it strongly, and it is **hold-converged**
— 800 and 2000 RF cycles give the same answer at both offsets, so this is not the
observation window.

**The reason is structural and it is not fixable by measuring more carefully.** A
boundary found by asking *did the ion reach an electrode* requires the ion to travel
from wherever it started all the way to z0, through the whole anharmonic region. So it
is never a small-amplitude measurement, whatever it was launched at — the launch offset
sets how much of the journey is spent in the anharmonic region, not whether any of it
is. A small launch survives past the linear boundary because the anharmonic frequency
shift halts the growth before z0; a larger one is lost below it.

### The linear boundary, measured without the ion going anywhere

β needs no journey. It is read off the spectrum of an ion that stays small, so the
linear boundary can be located by calibrating β(V) against Mathieu across a range where
the ion *is* small, and asking where the calibration puts β = 1.

**β is amplitude-independent where it should be**, which is the premise:

| amplitude | β at 0.05 mm | β at 0.20 mm | spread |
| --- | --- | --- | --- |
| 300 V | 0.29554 | 0.29495 | 2.0e-3 |
| 450 V | 0.46746 | 0.46540 | 4.4e-3 |
| 600 V | 0.69923 | 0.68749 | 1.7e-2 |

A fourfold change in launch amplitude moves β by two parts in a thousand at low q, and
measurably more at high q — which is the anharmonicity appearing in the *frequency*
rather than in a loss, and is the control that says the shift is real and small.

Fitting one number — the scale `s` with `β_measured(V) = β_Mathieu(q_nominal(V)·s)` —
across four amplitudes:

| | measured | predicted | ratio |
| --- | --- | --- | --- |
| 300 V | 0.29524 | 0.29515 | 1.0003 |
| 390 V | 0.39470 | 0.39442 | 1.0007 |
| 480 V | 0.50618 | 0.50590 | 1.0006 |
| 570 V | 0.64114 | 0.64191 | 0.9988 |

**Worst residual 1.2e-3.** The trap is one ideal quadrupole across the whole range, of
effective radius **3.8137 mm** — against **3.8195 mm** from the field curvature with no
ion involved at all, and in the direction that measurement's own δ-dependence predicts
(3.8195 at a 0.4 mm sampling radius, 3.8286 at 0.6, 3.8438 at 0.8, so falling as δ→0).
**Two routes sharing nothing but the solved field, agreeing to 0.15 per cent.**

That puts β = 1 — the published boundary q = 0.90804 — at **675.5 V, q_nominal =
0.82543**. And the two ejection edges **bracket it**: 665–670 V at 0.3 mm and
695–700 V at 0.1 mm.

**A caveat about the tool.** The endpoint is anchored to the *tabulated* 0.90804 rather
than to the continued fraction used everywhere else here, deliberately: that expansion
has a near-singularity exactly at β = 1, where its n = 1 denominator `(β−2)²` goes to
one, and it puts the crossing at q = 0.9117 — four parts in a thousand off. It is
accurate where it is used, at β from 0.3 to 0.8, and not at the endpoint.

**So the 9.4 per cent shortfall from the tabulated boundary is one geometric factor,
now measured three independent ways** — the field's curvature at the centre, the secular
frequency of a flown ion, and the ejection edges that straddle it.

### A resonance band inside the stable region, found by the confirmation walk

At a 0.3 mm launch there is a narrow band of loss at **605–614 V** (q_z =
0.739–0.750), sixty volts *below* the main edge and well inside what the Mathieu
chart calls stable. Every control says it is real:

| control | result |
| --- | --- |
| 256 × 128 grid | identical band, 605–614 V |
| 400 cycles | identical band |
| 60 cycles | **gone** — the growth is slow and secular, not exponential |
| 0.1 mm launch | **gone** — so it is driven by the field's higher multipoles |

That combination is the signature of a **nonlinear resonance**: a linear instability
would be exponential (visible at 60 cycles) and amplitude-independent (visible at
0.1 mm), and this is neither. **Which** resonance is not established — β_z there is
0.615, which lands on no `n_z β_z + n_r β_r = 2` for any multipole order up to six —
and settling that needs a frequency analysis of the secular motion rather than a
loss test. Recorded as measured rather than explained.

It is worth saying how it was found: **the confirmation walk in `einzel boundary`
turned it up on its first real use**, from a search whose bisection had converged
cleanly onto the main edge sixty volts above. The bisection itself reported nothing
unusual, and could not have — see `optimisation.md`.

The effective radius is read off the field itself, from `dEz/dz = 2V/r0²`, and the
same samples give the anharmonicity for free. `dEz/dz ÷ dEr/dr` is exactly −2
wherever the quadratic term dominates — that is Laplace's equation in cylindrical
coordinates — and here it drifts from −1.9867 to −1.9461 as the sampling radius
doubles from 0.4 to 0.8 mm. **A hyperbolic trap would hold −2 everywhere by
construction**, so a departure growing with radius is the higher multipole flat
electrodes buy. That growth is what the test asserts, rather than a blanket
tolerance: a departure that did *not* grow with radius would be discretisation or a
bug.

### The finding worth keeping: a boundary needs its observation window

At **60 RF cycles** the ejection boundary is not a boundary. It is a ragged strip:

```
   V   672 674 676 678 680 682 684 686 688 690 692
held     1   1   0   0   1   0   1   0   1   0   0
```

At **200 cycles** the same scan is a clean step between 672 and 674 with no
survivors above it. Nothing about the design changed. The growth rate goes to zero
at the stability edge, so whether a marginally unstable ion reaches an electrode
inside the hold is a property of *the hold*, not of the trap.

Two consequences. The template holds for 200 cycles by default and says why. And
**`einzel boundary` now walks outward from its converged bracket** looking for the
predicate flipping back — because bisection on the 60-cycle scan lands anywhere in
that strip depending on the path it took, and every step of that path is consistent
with a clean edge. Two runs over slightly different brackets gave 680.7 V and
694.4 V for the same geometry, which is how the fraying was noticed at all. See
`optimisation.md`.

### What it cost below the library: one line, and it was a real gap

`ModelValidator` refused the trap outright — *"the accelerating potential may only
be zero when a field can accelerate the ion, and this model declares none that
can."* The check asked whether any electrode held a non-zero **DC** potential. A
Paul trap holds zero volts of DC on every electrode and all of its potential as
drive, so the archetypal start-at-rest device was declared incapable of moving an
ion. Now it asks about the drive as well, in both two and three dimensions — the
3-D arm had never inspected anything at all and passed by default, which is the
same bug wearing the opposite mask.

Same shape as two defects already recorded: `einzel solve` reporting the DC pattern
for a driven geometry, and the 3-D verb reporting `converged: true` for a field it
never touched. **Reading only the DC of a driven electrode is a recurring mistake
here**, and it is worth grepping for the next time something driven behaves as
though it were earthed.

## The travelling-wave guide gets its second generator, and it does not help yet

The shipped guide now declares **two** generators — a slow travelling wave whose phase
ramps along the stack, and a fast confining RF on the same rings in adjacent antiphase
— which is what a real stacked-ring travelling-wave guide is and what this template
could not say at all until a solve could carry more than one drive.

| | |
| --- | --- |
| Electrodes | 24 rings, each tapping both generators |
| Generators | wave at 0.5 MHz, confinement at 3 MHz |
| Basis solves | **3** — two for the wave's phase ramp, one for the alternating confinement |
| Shortest period the field reports | **333.33 ns**, the confinement's, not the wave's |

**And the confinement does not widen the acceptance.** Measured as the fraction of
entry radii from 0.1 to 1.2 mm that arrive, on a 2 mm bore:

| confinement | arrivals of 12 |
| --- | --- |
| none | **5** |
| 100 V | 2 |
| 200 V | 4 |
| 400 V | 3 |
| 800 V | 1 |
| 200 V at half the frequency | 1 |
| 400 V at half the frequency | 1 |

**The window is narrow at both ends, and that is the explanation rather than a
disappointment.** Above about 200 V on this ring pitch the confining drive's own
Mathieu q passes the stability limit, so the ion is RF-*unstable* and is ejected
rather than held — at 800 V the confinement removes ions that would otherwise have
arrived. Below it the pseudopotential well is shallow against a 60 V wave, and the
alternating field decays as `exp(−2πr/pitch)` so what reaches the axis is a small
fraction of what sits at the rings.

Whether a working point exists is a two-dimensional question in wave and confinement
amplitude together, and it is a design study rather than a test. **The template
therefore ships with the confinement at zero volts**: shipping a default that makes a
device worse would be worse than shipping none.

What the tests assert is the part that is settled — the generator is declarable, costs
one extra solve, sets the step by the faster clock, and **reaches the ion**: the
acceptance differs with it on, so it is neither inert nor being silently dropped
somewhere between the document and the trajectory. An excitation that ejects is still
an excitation that arrived.

**A statistic that had to be replaced.** The first measurement was "the widest entry
radius that still arrives", which read 0.65 mm on one radius grid and 0.20 mm on
another for the same geometry — a maximum over a ragged set is a maximum over noise.
Counting arrivals over a fixed grid is the same measurement made stable.


## `kingdon-trap` — orbital motion, and the invariants that are exact

A wire on the axis of a cylinder, the wire held negative to positive ions. The oldest
electrostatic trap there is, and the device the Orbitrap descends from. It is worth having
for three reasons, none of which is the device itself.

**It is the first thing here that combines an axisymmetric solve with genuinely
three-dimensional motion.** The geometry is two coaxial cylinders and is solved in a
half-plane; the ion circles, so it uses all three coordinates. `AxisymmetricField` was
built for exactly that and no device had exercised it.

**Its closed forms are exact and strange.** In `phi = A ln(r)` the inward force goes as
`1/r`, so the circular-orbit condition `m v^2 / r = q A / r` has the radius **cancel out of
it**: every circular orbit has the same speed, whatever its radius. That is not a paraxial
limit or a small-angle approximation. An inverse-square potential does the opposite — that
is Kepler's third law — so the property is a statement about the logarithm rather than
about orbits, and a field that is even slightly not logarithmic fails it.

| launch radius | launch speed | radius wanders by |
| --- | --- | --- |
| 1.5 mm | 2047.02 m/s | 1.59% |
| 4.0 mm | **the same** 2047.02 m/s | 1.03% |
| 7.5 mm | **the same** 2047.02 m/s | 0.22% |

A factor of five in radius, one speed, taken from `sqrt(q V / (m log(b/a)))` — which
contains no radius at all. The template writes it as a derived parameter, so changing the
geometry moves the launch with it.

**And it has a tolerance-free invariant.** An axisymmetric solve has *exactly* zero
azimuthal field, so there is no torque about the axis and angular momentum cannot change.
Not to an accuracy — as an identity:

| | |
| --- | --- |
| Angular momentum over 16 orbits | **2.9e-12** relative |
| Axial excursion | **0.000 um** |
| Orbital speed vs the closed form | ratio **1.000000000** |
| Solved potential vs `A ln(r) + B`, r = 8 mm | 0.0002 V of 100 applied |
| The same at r = 2 mm | 0.28 V |

That last pair is the useful shape of an error: the departure from the logarithm is largest
**near the wire**, which is 0.1 mm across on a 0.25 mm cell and therefore under-resolved,
and it falls by three orders of magnitude on the way out. The orbit wander follows the same
ordering. An error that is largest where the mesh is worst is discretisation; one that is
uniform, or largest where the mesh is best, is a wrong operator.

**What it needed below the library was a logarithm**, and that is LIB-1 working. Every
closed form here is `ln(b/a)`, so without it the launch speed could only be a baked number
— which section 9 forbids, and which would silently stop being right the moment anyone
changed a radius. `log` is dimensionless-only for the reason `sqrt` is, and refuses a
non-positive argument rather than propagating a negative infinity into a geometry. The same
pattern as `multipole-guide`, which found the grammar had no trigonometry.

**What it does not do:** confine axially. A wire and a cylinder are radially confining and
axially indifferent; a real Kingdon trap adds end electrodes, and this one launches with no
axial velocity so the ion stays in its plane. That is a real limitation of the template
rather than of the platform — end electrodes are two more rectangles.


## `c-trap` — a curved axis, which is invariant under nothing

Four rods bent around a quarter circle, holding ions until a sequence pushes them out
sideways through a slot. It injects an orbital analyser, and the curvature is the point: a
packet ejected radially from a curved trap converges as it flies, so it arrives spatially
focused rather than as a line.

**It is the first device here that is neither a cross-section nor a surface of
revolution.** A translational solve assumes the geometry repeats along an axis; an
axisymmetric one assumes it repeats all the way round. A curved axis does neither, so this
needs a genuine volume solve — the first template to.

**The rods are chains of overlapping spheres**, because a `cylinder` in this format is
axis-aligned and a bent rod is not. That needed **no new primitive**: `repeat` binds an
index and `cosPi`/`sinPi` place a bead anywhere. Overlapping copies at one potential are
deliberate rather than tolerated — the overlap check refuses only conductors that
*disagree* about what they hold.

| | |
| --- | --- |
| 49 electrode declarations | **1 basis channel** |
| Cycles, convergence factor | 19, 0.3316 |
| Solve time, 65 x 65 x 33 | 9.1 s |
| Worst bead spacing | 1.005 rod radii |
| Out-of-plane excursion, in-plane launch | **0.000 nm** |

Four bent rods reduce to one solve for the same reason four straight ones do: the in-plane
pair and the out-of-plane pair are exact negatives. Bending changes nothing about that, and
it matters more here than in a cross-section — a second channel in a volume solve is
another pass over the whole grid.

**The drive is what carries the ion round**, checked against the same model with the
amplitude at zero:

| | outcome | closest approach, late in flight |
| --- | --- | --- |
| drive on | arrives at the arc's end | **8.4 um**, 0.5% of its own worst |
| drive off | strikes a rod at 25.9 us | never nearer than **782 um**, 26% of its worst |

Bounded against unbounded, which is what confinement means. Three earlier versions of that
assertion compared the wrong pair of quantities and are written up in `docs/lessons.md`.

### The slot, and what the beads eat of it

Ejection needs a hole in the inner electrode, so it is declared as two segments with an
angular gap. **The gap is not the opening.** The bounding bead at each end is a sphere of
the rod radius sitting on the inner arc, and it reaches `asin(rodRadius / innerArcRadius)`
past its own centre — 14.7 degrees on each side for the shipped numbers, so a declared
27-degree gap opens **minus two**.

Found by ejecting into it: before the slot existed the ion struck metal after travelling
exactly the inscribed radius, and with a 27-degree gap it struck the bounding bead.

**`slotHalfTurns` is now the opening**, measured between the two metal surfaces, and the
bead reach is a derived parameter the segments are placed by. A parameter that means
something other than what it says is worse than one that is missing — the arithmetic is
right either way, and only one of the two spellings is right when somebody reads it.

**That needed `asinPi` in the expression grammar**, which is the same shape as the Kingdon
trap needing `log` and the multipole guide finding no trigonometry at all. Placing
something by angle when what is known is a *length ratio* needs an inverse sine, and in
half turns so that the result feeds straight back into `cosPi`/`sinPi` — there is no `pi`
in the grammar, on purpose.

The slot also had to move to the **middle** of the arc. At a quarter of the way along, the
metal would have to stop `slotHalfTurns/2 + beadHalfTurns` short of the slot centre on the
entrance side, which is a negative span.

**And it has to be a cooled ion.** Launched at 439 m/s the packet drifts 12 degrees along
the arc while it is being pushed out, and clips the far edge of the opening. A real C-trap
cools its ions in gas before ejecting them, and modelling that is what makes the ejection
work. `launchVolts` is that temperature, written the way a source declares one.

### The push has to be against earth

`ejectVolts` puts the outer rod at +V and the inner rods at **earth**, not at −V. The
difference is the whole flight. The space inside the arc is bounded by the grounded domain
and sits near earth, so with the inner rod at −V an ion falls V through the slot and then
climbs the same V back out — it arrives where the analyser would be with nothing left, and
turns round. Pushing against earth leaves that space field-free, and a field-free space is
where a converging packet does its converging.

This was not found by a failing test. It was found by asking where the energy goes, after
the first ejection scan produced ions at 173, 346, 361 and 856 mm from a 20 mm trap.

### The curvature focuses the packet, and not where the geometry says

This is what the curvature is *for*, and the template claimed it in prose from the day it
was written with nothing measuring it. Every ion is pushed out along its own radius, so
their velocities all point inward and the packet converges as it flies. **A straight trap
pushes every ion in the same direction**, so whatever length of trap the ions occupied they
still occupy at the analyser.

Five ions spread over ±7.2 degrees of arc, cooled, ejected at 60 V with the drive off:

| bend radius | launch extent | waist | convergence | focus at |
| --- | --- | --- | --- | --- |
| 15 mm | 1.331 mm | 0.0547 mm | **24.3x** | 25.94 mm = **1.73 R** |
| 20 mm | 1.774 mm | 0.0852 mm | **20.8x** | 38.38 mm = **1.92 R** |
| *a straight trap* | — | — | *exactly 1.0x* | *never* |

The straight-trap column needs no run: a parallel ejection is a rigid translation and a
translation preserves every distance, so the comparison is arithmetic rather than a second
model.

**The focus is not at the arc centre**, which is the part a design has to know. Velocities
aimed along radii meet at the centre, one bend radius away; measured, the packet crosses
the centre *still converging* and reaches its waist at 1.73 and 1.92 bend radii. The slot
is a lens as well as a hole — the ion is accelerated up to it and drifts field-free after
it, which is an aperture lens by construction.

**What is deliberately not claimed is a strength for that lens.** A thin-lens fit to the
shorter bend — one fixed slot lens in series with a curvature whose focal length is the
bend radius — implies `f_slot = −35.5 mm` and predicts **46.0 mm** for the longer bend
against a measured **38.4**. So the two are not one fixed lens and one variable one, and
the reason is visible in the geometry: the slot is declared as an *angle*, so its own
opening scales with the bend as well. The prediction is recorded because it failed;
carrying it as a formula would have been carrying a 17% error dressed as a model.

### Leaving the drive on refocuses the ejection, through its cycle average

A real C-trap switches its RF off to eject. With it left running the packet still
converges, but it converges **three times sooner and two and a half times less well**:

| | convergence | focus at |
| --- | --- | --- |
| drive off | **20.8x** | 38.37 mm |
| drive on, phase 0.00 | 8.3x | 11.57 mm |
| drive on, phase 0.25 | 6.1x | 12.21 mm |
| drive on, phase 0.50 | 8.8x | 11.59 mm |
| drive on, phase 0.75 | 8.7x | 11.07 mm |

So an analyser placed where the quiet ejection focuses would be in entirely the wrong
place. Whether the drive is on at the instant of ejection is a decision about where the
analyser goes, not a detail of the hold.

**The phase sweep is the half that says what mechanism it is, and it refuted the guess that
prompted it.** An ejection into a field reversing at three megahertz looks like it should
depend on where in the cycle the push arrived — every ion in the packet sees the same
phase, so a kick would aim the whole packet somewhere different. It does not: over a whole
cycle the focal distance moves **1.10x**, against the **3.14x** the drive itself causes. So
what acts on the packet is the **cycle-averaged** force — the pseudopotential — and not the
instantaneous field. The ion crosses about seventeen RF periods on its way to the waist,
which is why the phase it started at washes out, and the tenth that remains is the one
partial cycle at the beginning.

Sweeping it at all is the point. One ejection with the drive running is a single sample of
something periodic, and this project has already recorded what comes of quoting one: an
isolation-efficiency curve whose shape reversed at an amplitude nobody had swept.

### C-trap to orbital analyser: the handover, since the composition is not possible

The two instruments cannot be composed into one document — see below, and SPEC.md
Amendment 32. What *can* be done is the handover: measure what one delivers, measure what
the other needs, and compare them in a currency both share.

**That currency is time, not space, and the reason is the analyser's defining property.**
In a quadro-logarithmic field the axial frequency depends on nothing but `m/q` — not the
orbit radius, not the axial amplitude, not the energy — which `QuadroLogarithmicFieldTests`
pins directly. So the analyser is indifferent to almost everything an injected packet
varies in. Two ions at the same frequency still cancel if they start at different
**phases**, and phase is set by when an ion arrived.

So the injection specification is one ratio: the packet's spread in arrival time over the
analyser's axial period. Both numbers come from the shipped templates — the spread by
ejecting `c-trap`, the period by compiling `orbital-trap` and reading its own declared
parameter.

| | arrival spread | of a period | coherence |
| --- | --- | --- | --- |
| analyser axial period | 3.1983 us | — | — |
| ejected, drive off | **60.02 ns** | 1.88% | **0.9990** |
| ejected, drive on | 170.93 ns | 5.34% | 0.9893 |

Coherence is the modulus of the mean of `exp(i omega t)` over the packet — the amplitude
of the image current the ions actually induce, 1 for a packet that arrived together and 0
for one smeared over a whole cycle. **The C-trap can inject this analyser**, with room to
spare, and leaving the drive on costs a factor of 2.8 in spread and almost nothing in
coherence.

**What this does not show is that the curvature delivers the coherence.** Every ion sits
the same distance from the rods whether the trap is bent or straight, so they fall through
the same potential either way and a straight trap would arrive just as together. The 60 ns
is the *slot's* doing — ions nearer its edge see a different fringe than ions at its
centre. The curvature buys the other thing, measured above: a packet 20.8 times narrower in
space, which is about passing an entrance aperture.

That split is worth stating because it says where design effort goes. In this field the
axial frequency is exactly amplitude-independent, so a spatially broad packet is **not** a
dephased one — ions from different axial offsets oscillate at one frequency and stay in
step, and only their amplitudes differ. A real analyser's field imperfections make the
frequency weakly amplitude-dependent and give spatial compactness a second job; this model
has no such imperfection and should not be read as though it did.

### What cannot be done yet: the two instruments in one document

The C-trap injects an orbital analyser, and **the two cannot be composed into one model.**
Not because the sequencer cannot express the handover — it can — but because
**analytic fields fill all space**. The quadro-logarithmic potential grows as `z^2`, so an
orbital trap declared anywhere in the same document puts an enormous field across the
C-trap.

Two *solved* elements compose correctly, because each is bounded by its own domain and
decays outside it. It is specifically an exact analytic field that cannot be one element of
a beamline.

The fix is a spatial region on an analytic element, outside which it contributes nothing —
which introduces a field discontinuity at the region boundary, and the integrator already
lands exactly on declared discontinuities. That is a real design question rather than an
oversight, and it is the next thing this needs.
## Ion processor to a multi-reflection analyser: the second handover

The other injection path this platform is pointed at, and it has the same shape as the
C-trap's: a trap accumulates and cools ions, then pulses them into an analyser. Here the
trap is the shipped `rectilinear-trap` — the low-pressure region of an ion processor — and
the analyser is the shipped mirror pair at its two-stage design point.

**What is modelled and what is not.** Both halves are solved geometry. It is *not* a model
of any particular commercial instrument: an asymmetric-track analyser gets its reflection
count from a slow drift along the mirror axis, and nothing here models that drift. What is
asked is the question that does not depend on it.

**The currency is the arrival-time spread, and turn-around is the part no analyser can
undo.** A time-of-flight analyser refocuses energy spread — that is what its mirrors are
for — but ions that left the source at different *instants* stay apart for the whole
flight. So the trap's turn-around is a floor on the peak width, and the resolving power it
permits is `t / 2dt`, growing linearly with flight time.

| | |
| --- | --- |
| trap turn-around | **4.220 ± 0.067 ns** (2,000 ions, converged) |
| analyser period | 55.9366 us per oscillation, 767.0 mm cap to cap |
| analyser energy limit, ±3% | R = 321,018 |

| oscillations | flight time | path | R from turn-around |
| --- | --- | --- | --- |
| 1 | 55.9 us | 1.53 m | 6,627 |
| 4 | 223.7 us | 6.14 m | 26,507 |
| 8 | 447.5 us | 12.27 m | 53,015 |
| 16 | 895.0 us | 24.54 m | **106,029** |
| 32 | 1790.0 us | 49.09 m | 212,058 |
| 64 | 3579.9 us | 98.18 m | 424,117 |

**The two limits cross at 48 oscillations**, 2.7 ms of flight and 74 m of path. Below that
the *trap* binds and a colder or harder-pushed source is worth more than a better mirror;
above it the *mirror* binds and more reflections buy nothing at all. Every practical
instrument sits well below the crossing, so **for this pairing the turn-around time is the
thing to improve**.

**One number in that comparison is arithmetic wearing a measurement's clothes**, and it is
worth saying which. The energy-aberration limit comes out at 321,018 for 3, 6 and 12
oscillations — identical to the digit, because `MirrorPair.Fly` computes one period and
multiplies it rather than stitching legs (a choice recorded in `docs/lessons.md`, made
because twelve legs give twelve chances to miss a root-find). So the flatness confirms the
arithmetic, not the physics. The physical claim underneath is that every oscillation is
identical — true of a periodic analyser, and **exactly what an asymmetric track gives up**,
since its ions drift along the mirror axis and successive reflections sample different
field. That departure is unmeasured here.

**And the trap's own arrival spread is the wrong number to use**, which is the trap this
comparison exists to avoid. At its own detector this packet is ~241 ns wide, almost all of
it the spread in extraction depth — but that is an energy spread, and refocusing energy
spread is what mirrors are for. Turn-around is the 1.8% of it that survives. Using the
241 ns would understate the reachable resolving power by two orders of magnitude.

