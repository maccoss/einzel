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

They **share no code at all**. All three name the same electrode primitives in
different arrangements; everything below reads a Dirichlet mask without knowing
which is which. Adding a device is a new file.

```csharp
DeviceTemplates.Names();          // ["planar-mirror-pair", "quadrupole", "rectilinear-trap"]
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

**As a trap, flat plates are a crude quadrupole.** Measured the same way as the
round-rod device, on the same quantity, so the comparison means something:

| | 12-pole / quadrupole |
| --- | --- |
| Round rods at the classical 1.1468 | 2.41e-5 |
| Flat plates | 7.12e-3 |

**296 times worse**, and not a defect. A rectilinear trap is chosen because flat
plates are easy to make, easy to align, and easy to cut a slot in; the field
quality is what it pays. The number is what turns that from a remark into a
design input.

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
| Width, 0.2 mm across it | 87.2 ns |
| All three, measured | 248.3 ns |
| The three in quadrature | 247.8 ns |

Quadrature agreeing to 0.2% says these really are three independent mechanisms
rather than one counted three times. Turn-around is **1.7%** of the total, which
matters when reading a published number: a figure near a nanosecond cannot be the
arrival spread of a packet this deep.

There is also **no useful space focus**. A single-stage extraction should have a
Wiley-McLaren focus at twice the source depth - about 6 mm here - where the ion
that started deeper catches the one in front. Scanning the drift from 2 to 11 mm
the spread grows monotonically at 20.7 ns/mm, so the focus is at essentially zero
drift and any usable detector is far past it. That is what a field varying by a
factor of two across the packet does to a condition derived for a uniform one, and
it is why a real instrument adds a second acceleration stage rather than moving the
detector.

### What it does not model

**Electrodes do not stop ions.** Nothing in transport tests whether a trajectory
has entered a conductor, so ions pass through the front plate as readily as
through the slot and transmission reads 100% regardless. For this device that is
the difference between a modelled aperture and a decorative one, and it is the
single most important gap the template exposed. The machinery to fix it exists -
`CompiledElectrode.FirstEntry` already finds where a segment enters a conductor in
closed form, and the integrator already lands exactly on declared events - so it
is wiring rather than new numerics.

Until it is wired, **treat every transmission figure from this template as
meaningless** and read only the arrival-time and emittance results, which are
computed over ions that would have got through anyway.

**The auxiliary DC electrodes are not here.** The Ion Processor's are diagonal
wedges and horizontal pairs that impose a gradient *along* the trap axis. Every
solve here is a cross-section with translational invariance along that axis, so an
axial field cannot be represented at all. That needs three dimensions, not another
rectangle.

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
