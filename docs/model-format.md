# Model format, schema 0.3

A model is declarative, schema-validated, diffable JSON. A model file plus its
referenced artifacts fully determines a run.

Two rules govern the whole format:

**Every quantity carries a unit.** `{"value": 4, "unit": "kV"}`, never `4000`.
This is deliberately more annoying than the alternative, because unit ambiguity
is the commonest source of silent wrongness and an agent building a model from
prose is the actor most likely to introduce it. Making a quantity a two-field
object means the failure happens at parse time with a JSON Pointer rather than at
run time as a factor of 1.602e-19.

**Unit symbols are case-sensitive.** `mm` and `Mm` differ by nine orders of
magnitude. A wrong case is an error that names the right symbol, not a silent
acceptance.

## Skeleton

```json
{
  "schemaVersion": "0.2",
  "name": "...",
  "description": "...",
  "parameters": { },
  "ion":        { },
  "source":     { },
  "fields":     [ ],
  "detector":   { },
  "transport":  { }
}
```

## Quantities

```json
{ "value": 4, "unit": "kV" }
{ "expression": "mirrorDepth * 0.5", "unit": "mm" }
```

Any quantity may be an expression over the declared parameters. Vectors are
`{"value": [x, y, z], "unit": "mm"}`; directions are `{"value": [1, 0, 0]}` with
no unit, normalised on load, so `[2,0,0]` and `[1,0,0]` mean the same thing.

## Parameters

The declared parameter surface. This is what a sweep varies, an optimiser
searches, and a tolerance study draws from.

```json
"parameters": {
  "mirrorDepth": {
    "value": 90.0, "unit": "mm",
    "minimum": 20.0, "maximum": 300.0,
    "description": "Depth of the printed mirror, entrance plane to cap."
  },
  "halfGap":     { "expression": "boardGap / 2", "unit": "mm" },
  "rodRatio":    { "value": 1.1468, "unit": "1" }
}
```

A parameter declares **either** `value` **or** `expression`, never both, and
always a `unit` (`"1"` for a pure ratio). Bounds are optional and given as plain
numbers in the same unit.

Bounds are part of the declaration rather than a separate validation pass because
they carry design intent. A mirror depth that may run 20–300 mm says something a
bare nominal does not, and it is what lets a study be written as "vary everything
over its declared range" instead of restating limits the template already knows.
Bounds are **checked, not clamped**: a sweep that walks a parameter past its
declared range has found something the template author did not intend, and
clamping would hide it.

### Expressions

Arithmetic over other parameters: `+ - * /`, parentheses, unary minus, and
`abs(x)`, `sqrt(x)`, `min(a,b)`, `max(a,b)`.

Expressions evaluate over **quantities, not numbers**, so dimensions propagate
through the arithmetic and the declared unit is checked against the dimension the
expression actually produces. That makes the evaluator a unit checker as much as
a calculator: `boardGap / 2` is a length, and a term adding a length to a voltage
fails where it is written rather than thousands of steps later. `sqrt` is
restricted to dimensionless arguments, because the square root of a length has no
representation in an integer-exponent dimension system and silently dropping the
dimension would defeat the check.

Derived parameters are evaluated in dependency order, and a cycle is refused with
the chain named rather than recursed into. Derived parameters **re-evaluate
against overrides**, which is the property that makes sweeping meaningful:
perturb `capToCap` and everything expressed in terms of it follows.

This is a sandbox in the sense that matters: the grammar is arithmetic, nothing
but a parameter can be named, and evaluation cannot loop. It is not a scripting
language and is not meant to become one — extension goes through Python behind a
process boundary, not through the model format.

## Ion, source, detector

```json
"ion":    { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
"source": {
  "position":              { "value": [-100, 0, 0], "unit": "mm" },
  "direction":             { "value": [1, 0, 0] },
  "accelerationPotential": { "value": 4, "unit": "kV" },
  "energyFraction":        0
},
"detector": {
  "planePoint": { "value": [-100, 0, 0], "unit": "mm" },
  "normal":     { "value": [1, 0, 0] }
}
```

The source states an *energy*, never a velocity; the launch speed is derived from
the ion's own mass and charge, so the document cannot state two things that
disagree. `energyFraction` offsets it fractionally for acceptance studies.

The detector normal points back into the flight volume: the flight ends when the
ion crosses from the positive side to the negative side, and the integrator lands
on the crossing exactly rather than at the end of whichever step passed it,
because the crossing time is the measurement.

### The source cloud

Without a `cloud`, a source launches **one ion down the axis**, and every figure
computed from it is a property of that ion rather than of the instrument. That is
why resolving powers here have carried the caveat "energy aberration only".

```json
"source": {
  "position":              { "value": [-100, 0, 0], "unit": "mm" },
  "direction":             { "value": [1, 0, 0] },
  "accelerationPotential": { "value": 4, "unit": "kV" },
  "cloud": {
    "ions": 1000,
    "seed": 1,
    "temperature":          { "value": 300, "unit": "K" },
    "transverseSpread":     { "value": 0.3, "unit": "mm" },
    "longitudinalSpread":   { "value": 0.1, "unit": "mm" },
    "energyFractionSpread": 0.01
  }
}
```

| Field | Means |
| --- | --- |
| `ions` | How many **trajectories to compute**. A numerical setting: sampling harder only makes a statistic better. ACC-5 wants transmission to ±1% at 95%, which needs about 9,600 at the worst point |
| `population` | How many ions are **physically in the packet**, which is what pushes on itself. Defaults to `ions` |
| `seed` | So the same study gives the same answer twice |
| `temperature` | Thermal velocity, drawn per component as a Gaussian of width √(kT/m). This is the whole turn-around story |
| `transverseSpread` | Gaussian width across the direction of travel. Costs transmission |
| `longitudinalSpread` | Gaussian width along it. Costs arrival time directly — two ions a millimetre apart are a millimetre of flight path apart |
| `energyFractionSpread` | Gaussian width of the acceleration energy. Supply ripple, not temperature |

Every spread defaults to zero, so a model that says nothing about a cloud launches
exactly what it launched before. That is not only backward compatibility: a spread
appearing by default would change every existing result silently, and a resolving
power quietly getting worse is indistinguishable from a bug.

**`ions` and `population` are not the same thing.** One is how hard the source
distribution is sampled; the other is how many ions are actually there at once.
Ten thousand samples of a single-ion experiment is a better statistic. Ten thousand
ions in a bunch is a different experiment, because they push each other apart.

The default is the conservative reading — the ions simulated are the ions
present — so a dense packet is never silently treated as sparse. Set
`"population": 1` when sampling an intrinsic source property one ion at a time.

**There is no angular-divergence setting, on purpose.** A thermal cloud already
has one — an ion with sideways thermal velocity is an ion launched at an angle —
and offering both would let a document say two things about the same physics and
be believed twice. Energy spread *is* separate, because supply ripple varies the
energy without varying the direction, which a temperature cannot express.

**A packet may start at rest.** `accelerationPotential` of zero used to be a
validation error - "or the ion never moves" - which is true of a beam and false of
a pulsed extraction trap, where the packet sits still until the instrument
switches a field on. Zero is accepted when the model declares a field that could
accelerate the ion, and still refused when nothing could.

For a packet at rest the `direction` is doing more work than usual. A moving ion
says which way is downstream by moving; a stationary one does not, so the declared
direction is what orients `longitudinalSpread` against `transverseSpread` - and
those are not interchangeable. Spread along the extraction converts to an energy
spread and then to arrival time; spread across it does not.

A declared cloud is what makes the emittance figures available. `transverseSpread`
and `temperature` are the two widths whose product the emittance is, so a cloud
with one and not the other has an emittance of exactly zero — correctly, since
every ion is then parallel — and a cloud with neither has no packet to measure.

## Driving a geometry

```json
"solve": {
  "drive": {
    "frequency": { "value": 1, "unit": "MHz" },
    "waveform": "sinusoid",
    "dutyCycle": 0.5
  },
  "electrodes": [
    { "name": "rodXPlus", ...,
      "potential":      { "value": 0, "unit": "V" },
      "driveAmplitude": { "value": 500, "unit": "V" },
      "drivePhase": 0 },
    { "name": "rodYPlus", ...,
      "potential":      { "value": 0, "unit": "V" },
      "driveAmplitude": { "value": -500, "unit": "V" } }
  ]
}
```

**One generator per solve.** A real instrument has a supply and electrodes tapped
off it at various amplitudes and phases; modelling it the other way round would let
a document declare two frequencies on one structure, which is a different
instrument and almost always a mistake.

| | |
| --- | --- |
| `frequency` | On the solve. The one thing every electrode shares |
| `waveform` | `sinusoid` (Mathieu) or `rectangular` (Meissner). Their stability boundaries are in different places: the square-wave cut-off is q = 0.712 against a sinusoid's 0.908 |
| `dutyCycle` | Rectangular only. Away from one half the wave carries a mean of 2d - 1, which enters the equation of motion exactly where a DC offset would - the trick of a digital mass filter |
| `driveAmplitude` | Per electrode, zero to peak, **signed**. A negative amplitude is the same as a half-cycle of phase |
| `drivePhase` | Per electrode, as a fraction of a cycle. Zero when omitted; a half is antiphase; a ramp along a structure is a travelling wave |
| `potential` | Still the DC part, and still what an undriven electrode holds |

An amplitude with no `drive` block is **refused**, naming the electrode. A document
that thinks it declared RF and did not is the expensive kind of silence.

Nothing is re-solved as the drive swings: electrodes sharing a time dependence
share one basis solve, and a quadrupole's two pairs are exact negatives, so four
rods reduce to one. See [Numerics](numerics.md).

## Three dimensions

```json
{
  "type": "solved3d",
  "solve3d": {
    "minX": ..., "minY": ..., "minZ": ...,
    "maxX": ..., "maxY": ..., "maxZ": ...,
    "cellSize": { "value": 0.8, "unit": "mm" },
    "drive": { "frequency": { "value": 1, "unit": "MHz" } },
    "electrodes": [
      { "name": "rod", "shape": "cylinder", "axis": "z",
        "centreX": ..., "centreY": ..., "radius": ...,
        "lower": ..., "upper": ...,
        "potential": ..., "driveAmplitude": ... }
    ]
  }
}
```

Three primitives: `box` (a plate, a segment wall, a housing), `cylinder` (a rod, a
tube, a ring - along `x`, `y` or `z`), and `sphere` (a bead, a rounded end).
Everything else is as it is for `solved2d`: repeats, drives, stages, sub-cell
boundaries, solid electrodes.

**Three dimensions cost the cube of the resolution.** A cross-section at sixteen
cells across a bore is 128 by 128 nodes; the same in three dimensions is two
million. So a 3D model is meshed far more coarsely than a plane one, and field
quality is the thing to check first rather than to assume - see
[Numerics](numerics.md) for what the solver does and does not manage.

Use it when the geometry genuinely varies along all three axes. A device that is a
cross-section extruded, or a half-plane rotated, is enormously cheaper and more
accurate as `solved2d` with the matching symmetry.

## Operating a geometry through stages

The sequencer the architecture calls a timed state machine. A trap fills,
isolates, then extracts, and the electrode potentials differ in each:

```json
"solve": {
  "stages": [
    { "name": "fill",    "duration": { "value": 200, "unit": "us" } },
    { "name": "isolate", "duration": { "value": 50,  "unit": "us" },
      "set": { "rfAmplitude": { "value": 300, "unit": "V" } } },
    { "name": "extract", "duration": { "value": 10,  "unit": "us" },
      "set": { "rfAmplitude": { "value": 0, "unit": "V" },
               "pushPotential": { "value": 1000, "unit": "V" } } }
  ],
  ...
}
```

**A stage sets parameters, not electrode settings**, and that is the whole design.
Electrode potentials are already expressions over parameters, so setting one moves
everything that depends on it at once - including *derived* parameters. Listing
electrode settings instead would let a stage change an amplitude while leaving the
quantity it was derived from behind, and the two would disagree silently.

It also costs no new vocabulary: the same override mechanism a sweep or an
optimiser uses to *perturb* a design is what a sequence uses to *operate* one.

Anything a stage does not name keeps the value it has outside the sequence, and
after the last stage ends the last state holds - an instrument left alone stays
where it was put. A field that switched off at the end of the declared sequence
would make every ion still in flight suddenly coast, which is a physics change
disguised as a bookkeeping one.

**A stage may change what an electrode holds, not where it is.** Moving metal
between stages would change the mask, so each stage would need its own solve and
its own grid - and the field would still be computed, and it would be wrong in a
way nothing else catches. It is refused, naming the electrode and the stage.

A sequence needs no drive: a pulsed extraction is DC that switches, and a solve
with stages and no `drive` block is exactly that.

## Repeating an electrode

A stack of rings is one ring, written once:

```json
{
  "name": "ring",
  "shape": "rectangle",
  "repeat": { "count": { "expression": "ringCount" }, "index": "ring" },
  "minX": { "expression": "ring * ringPitch", "unit": "mm" },
  "minY": { "expression": "entranceRadius - ring * taper", "unit": "mm" },
  "potential":      { "expression": "-dcGradient * ring / (ringCount - 1)", "unit": "V" },
  "driveAmplitude": { "expression": "rfAmplitude * (1 - 2 * mod(ring, 2))", "unit": "V" }
}
```

The index runs from zero and is bound as an ordinary parameter, so every expression
on the electrode sees it. The copies are named by position - `ring-0`, `ring-17` -
so an error, a loss itemisation or a channel report says *which* one.

This is the discrete periodicity SYM-1 lists beside cylindrical symmetry and mirror
planes, and it is what keeps a two-hundred-ring stack a **parametric** document
rather than a generated one: the placements are still expressions, so "move every
ring 50 microns and re-solve" is still sayable, which is what the whole tolerance
apparatus rests on.

An index name that collides with a declared parameter is refused rather than
shadowing it.

### Two more functions

`floor(x)` and `mod(a, b)` join `abs`, `sqrt`, `min` and `max`. Indexed geometry
needs them: the alternating sign of a two-phase stack is `1 - 2 * mod(index, 2)`.

Both are dimensionless-only, for the reason `sqrt` is - the floor of a length
depends on which unit you take it in, and that is precisely the ambiguity the
evaluator exists to refuse. `mod` is Euclidean rather than truncated, so
`mod(-1, 2)` is 1: an index counted backwards still alternates the way it should.

## What a solve is a cross-section of

```json
"solve": {
  "symmetry": "cylindrical",
  "minY": { "value": 0, "unit": "mm" },
  ...
}
```

`translational` (the default) extrudes the plane along the third axis: a
cross-section, which is what a mass filter or a rectilinear trap is.
`cylindrical` rotates it about the x axis, with **y as the radius**, so the domain
must lie at y >= 0 and a negative `minY` is refused.

This is not presentation. It changes the operator - in cylindrical coordinates the
radial part of the Laplacian gains a term from the shrinking circumference of a
ring near the axis - and a field solved with the wrong one converges perfectly well
to the wrong answer. See [Numerics](numerics.md) for the discretisation and what it
is checked against.

Everything else behaves as before, and that is the point of SYM-1: an electrode is
declared the same way, and a rectangle that was a bar becomes a ring. The ion is
launched and tracked in ordinary three-dimensional space; nothing above the field
knows the solve happened in a half-plane.

The axis is a symmetry plane whatever `bottomEdge` says, and is forced to be one.
Requiring the author to declare it would be an opportunity to get it wrong, and
there is no second thing it could be.

## Placements are expressions, including vectors

Spec section 9: every placement is a parametric expression, never a baked number.
Scalars take `expression` in place of `value`; vectors take three of them, one per
component, because the components are independent.

```json
"detector": {
  "planePoint": { "expression": ["plateOuter + driftLength", "0", "0"], "unit": "mm" },
  "normal": { "value": [-1, 0, 0] }
}
```

When expressions are present the `unit` is not consulted - each expression carries
its own dimension, exactly as a scalar expression does.

**A dimensionless zero satisfies any dimension.** The grammar has no unit literals,
so a bare `0` is dimensionless and there would otherwise be no way to write "on
axis" for the two components that are. This is the one safe exception: zero is the
only value whose unit conversion is the identity, and the ambiguity that makes
units mandatory here - is 4000 volts or kilovolts - does not exist at zero. A
dimensionless *one* is still refused, with the offending component named:

```
UNITS_INCOMPATIBLE
  at         /detector/planePoint/expression/1
  constraint this field requires a vector of dimension m
  observed   1 1
  try        the expression '1' produces dimension 1
```

## Fields

A list, superposed. Superposition is exact for electrostatics.

```json
{ "type": "fieldFree" }

{ "type": "uniform",
  "field": { "value": [0, -80000, 0], "unit": "V/m" } }

{ "type": "halfSpaceUniform",
  "planePoint":   { "value": [0, 0, 0], "unit": "mm" },
  "inwardNormal": { "value": [1, 0, 0] },
  "capPotential": { "value": 4, "unit": "kV" },
  "turningDepth": { "value": 50, "unit": "mm" } }
```

`halfSpaceUniform` is field-free on one side of a plane and uniformly retarding on
the other — the primitive an ideal single-stage ion mirror is built from. It is
named for what it is rather than what it builds, because no device class may
appear below the template library.

### Solved fields

```json
{ "type": "solved2d",
  "solve": {
    "minX": { "expression": "-mirrorDepth", "unit": "mm" },
    "minY": { "expression": "-halfGap",     "unit": "mm" },
    "maxX": { "expression": "midPlane",     "unit": "mm" },
    "maxY": { "expression": "halfGap",      "unit": "mm" },
    "cellSize": { "expression": "boardGap / 32", "unit": "mm" },
    "rightEdge": "neumann",
    "boundaryIsDiscontinuous": false,
    "reflectAboutX": { "expression": "midPlane", "unit": "mm" },
    "electrodes": [ ]
  } }
```

| Key | Meaning |
| --- | --- |
| `minX`…`maxY` | The solve domain |
| `cellSize` | Requested node spacing. Each axis rounds its interval count **up** to a power of two independently, so the spacing is never coarser than asked in either direction and the grid spans exactly the declared box. Cells need not be square; the worst ratio is two to one |
| `leftEdge`, `rightEdge`, `bottomEdge`, `topEdge` | `dirichlet` (default) or `neumann`, a symmetry plane |
| `boundaryIsDiscontinuous` | Whether the field genuinely jumps at the domain edge. **Set false when the domain was drawn wide enough that the field has decayed** — see below |
| `reflectAboutX` | Reflect the solved field through a plane and superpose, for a symmetric pair |
| `tolerance` | Relative residual for the solve, default 1e-12 |

`boundaryIsDiscontinuous` is worth understanding before setting it. Declaring a
jump that is not there is worse than declaring none: two such phantom surfaces a
few microns apart — which is what two abutting solve domains produce — defeat the
superposition's sign tracking, so a step crossing both is treated as crossing
neither. That cost an ion 2.6e-4 of its energy, four orders above the ACC-4
budget, and presented as an intermittent transmission loss rather than a fault.

### Electrodes

Three primitives, chosen for coverage rather than convenience.

```json
{ "name": "plate", "shape": "rectangle",
  "minX": …, "minY": …, "maxX": …, "maxY": …,
  "potential": { "value": 0, "unit": "V" } }

{ "name": "rodXPlus", "shape": "disc",
  "centreX": …, "centreY": …, "radius": …,
  "potential": { "expression": "rodPotential", "unit": "V" } }

{ "name": "topBoard", "shape": "edgeProfile", "edge": "top",
  "profile": [
    { "at": …, "potential": … },
    { "at": …, "potential": … }
  ] }
```

A **rectangle** gives plates, apertures, and any stripe of a printed board. A
**disc** is a rod in cross-section, which is what a quadrupole, hexapole, or
octopole is. An **edge profile** is a domain edge whose potential varies along it
by piecewise-linear interpolation, which is how a printed-circuit mirror applies
its ramp — one electrode spanning many nodes, because that is how it is driven:
one supply feeding a resistive divider.

The test of whether these are the right primitives is LIB-1's: if a new device
needs a change below the template library, either it is genuinely novel physics
or the abstraction is wrong, and almost always the second. A mirror and a
quadrupole differ only in which primitives they use and where they put them.

## Transport

```json
"transport": {
  "mode": "trajectory",
  "relativeTolerance": 1e-11,
  "maximumFlightTime": { "value": 1, "unit": "ms" },
  "sampleInterval":    { "value": 20, "unit": "ns" }
}
```

`maximumFlightTime` is **required**, as a runaway guard. `sampleInterval` sets the
cadence of the trajectory stream written for rendering and export, which is
independent of integration steps — though only in one direction: the stream can
be coarser than the steps, never finer, because there is no dense output to
interpolate within a step.

`statisticalDiffusion` is a declared peer mode that this build does not implement.
Asking for it is a regime violation with a distinct exit code, not a silent
substitution.

### Space charge

```json
"transport": {
  "spaceCharge": "direct"
}
```

`none`, the default, flies each ion through a field that does not know the others
exist. That is exactly right for a sparse beam and wrong for a dense packet, and
a run says which it is either way — the screening estimate is reported whether or
not it crosses a threshold.

`direct` sums every pair. It is the reference method SC-1 names, and it is a
string rather than a flag because particle-in-cell will be a third value and a
boolean would have to be replaced rather than extended.

**The weighting is the cloud's own two fields.** `ions` is how many trajectories
are computed; `population` is how many ions are physically present. Each computed
trajectory therefore stands in for `population / ions` real ions and carries their
charge *and* their mass together — so charge-to-mass is unchanged, motion in the
applied field is bit-identical to the unweighted case, and only the pairwise sum
notices. Lowering `ions` while keeping `population` is how a dense packet becomes
affordable, and it is a declared approximation rather than a hidden one.

**The cost is quadratic in `ions`**, and `einzel estimate` says so in words as
well as in a number, because the linear intuition is exactly wrong: 150
trajectories through the shipped trap took 87 seconds and 2,000 would take about
four hours. Trajectory integration is otherwise excluded from the estimate — its
cost depends on a path that depends on a field not yet solved — so this is the one
transport cost stated in advance.

Three ways to ask for it and not get it are **refused rather than run**, because
each would produce a result that looks like the one asked for:

| | |
| --- | --- |
| Fewer than two trajectories | nobody to push on |
| A cloud with no spatial spread | an unbounded self-field, not a large one |
| A declared gas | the packet advances in lockstep and has no collision hook, so the gas would take no part in the run |

What it gives up, stated rather than discovered: the packet integrator **cannot
land exactly on a declared field discontinuity**, because a shared step cannot land
on a surface each macroparticle reaches at its own instant. It caps the step short
of the first arrival instead. That is why this is the reference method for space
charge rather than a replacement for the path that carries ACC-1.

## Versioning

Schema 0.1 through 0.5 all load. Every bump ships a migration and a test that the
prior corpus still loads. Codes and field names are a compatibility surface that
agent workflows bind to: they are added, never reworded or repurposed.

0.3 adds the source cloud and 0.5 the mutual Coulomb force. Both purely additive,
so every earlier document still reads — but a document whose ions push on each
other genuinely is not a 0.4 document, and saying so is cheaper than an older
build reading it, ignoring the field it does not know, and reporting a different
flight with nothing to indicate that anything was dropped.
