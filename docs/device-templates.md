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

They **share no code at all**. All three name the same electrode primitives in
different arrangements; everything below reads a Dirichlet mask without knowing
which is which. Adding a device is a new file.

```csharp
DeviceTemplates.Names();          // ["einzel-lens", "planar-mirror-pair", "quadrupole", "quadrupole-rf", "rectilinear-trap"]
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
