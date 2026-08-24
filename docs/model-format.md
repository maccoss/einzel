# Model format, schema 0.2

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
| `cellSize` | Requested node spacing; interval counts round **up** to a power of two, so the actual spacing is never coarser than asked |
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

## Versioning

Schema 0.1 and 0.2 both load. Every bump ships a migration and a test that the
prior corpus still loads. Codes and field names are a compatibility surface that
agent workflows bind to: they are added, never reworded or repurposed.
