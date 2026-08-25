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

They **share no code at all**. Both name the same three electrode primitives in
different arrangements; everything below reads a Dirichlet mask without knowing
which is which. Adding a third device is a fourth file.

```csharp
DeviceTemplates.Names();          // ["planar-mirror-pair", "quadrupole"]
DeviceTemplates.Read("quadrupole");
```

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
