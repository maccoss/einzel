"""Generate the c-trap template: a curved quadrupole, its rods swept rather than beaded.

    python src/Einzel.Library/Templates/generators/c-trap.py src/Einzel.Library/Templates/c-trap.json

WHY THIS FILE EXISTS. The first version of this template built each bent rod from thirteen
overlapping spheres, because `cylinder` is axis-aligned and a bent rod is not - so beads
needed no new primitive, since `repeat` binds an index and `cosPi` places one anywhere.
Nobody looked at the result until the viewport drew it, and it drew a string of beads:
spheres of 3.439 mm radius on a 3.459 mm pitch, scalloping the rod by 13.6 percent of its
own radius. `beadCount`'s own description stated the criterion the shipped value failed - the
spacing "wants to be comfortably under the rod radius", and its ratio was 1.006.

WHAT IT IS NOW. A curved quadrupole is a straight one bent, and the straight one was already
here: the linear ion trap declares its hyperbolic rods as an outline tracing
r0*sqrt(1 + (v/r0)^2), extruded along z as a prism. The same outline REVOLVED about the bend
axis is a C-trap rod, which is what the `revolve` primitive was added for. So the rods are
now hyperbolic rather than round as well as smooth rather than beaded - which is what the
device actually has, and what the beads were standing in for.

AND THE SLOT RUNS THE LENGTH OF THE ARC, which the beaded version got wrong in a way the
beads hid. It modeled the slot as an angular GAP - the inner rod stopping and starting again
- so only ions near one angle could ever leave. That cannot be what the device does: the
packet fills the whole arc, and the focusing this trap exists for is that every ion is pushed
out along its OWN radius so their velocities converge. An angular hole lets out an angular
slice. The slot is a slit along the arc, so the inner electrode is two half-rods either side
of it, which is exactly how linear-ion-trap-3d.py writes its own slotted rod.

WHAT IS SOURCED AND WHAT IS NOT. Makarov et al., Anal. Chem. 2006;78(7):2113 - the shipped LTQ
Orbitrap - says on p. 2114 col. 2: "The C-trap uses rods with hyperbolic surfaces", enclosed by
two flat lenses, filled with nitrogen at ~1 mTorr, driven at 500-2000 V p-p on a 0 V DC offset,
and ejected by ramping the RF down over 100-200 ns and then pulsing 1200 V on the push-out
electrode, 1000 V on the pull-out and 1100 V on the upper and lower - the ions leaving through
a slot in the PULL-OUT electrode, orthogonally, toward the center of curvature. So the face
shape, the slit and its direction are sourced; the cross-section DIMENSIONS and the bend radius
are not, in either the paper or US 6,872,938 B2, and are marked as guesses below.

NOTE THAT THE PATENT DESCRIBES A DIFFERENT DEVICE. Its preferred curved trap is a stack of
curved PLATES - outer and inner plates near ground sandwiching center plate pairs at RF-, which
sandwich an axis plate pair at RF+. That is the patented principle; the hyperbolic quadrupole
is what shipped four years later, and it is what this models.

docs/literature-targets.md section 7 carries the whole account, including what this template
does not model: the 1100 V float, the quench, the gas, and the two end lenses.

Edit this file and regenerate rather than hand-editing the template.
"""
import json
import sys

FACE_SEGMENTS = 24
OUT = sys.argv[1] if len(sys.argv) > 1 else "c-trap.json"


def q(expr, unit="mm"):
    return {"expression": expr, "unit": unit}


def run(count, x, y):
    return {"count": {"value": count, "unit": "1"}, "index": "k", "x": q(x), "y": q(y)}


def corner(x, y):
    return {"x": q(x), "y": q(y)}


# The transverse coordinate the hyperbolic face is traced against, as a function of the run
# index: the face spans the trap axis symmetrically, from -rodHalfWidth to +rodHalfWidth.
S = f"(-rodHalfWidth + 2 * rodHalfWidth * k / {FACE_SEGMENTS})"

# r0 * sqrt(1 + (s / r0)^2): the hyperbola whose asymptotes are the quadrupole's diagonals,
# and the same expression linear-ion-trap-3d.py writes for a straight rod.
FACE = f"(inscribedRadius * sqrt(1 + ({S} / inscribedRadius) * ({S} / inscribedRadius)))"


def radial_rod(sign):
    """The inner or outer rod: its hyperbolic face turned toward the trap axis in radius."""
    away = "+" if sign > 0 else "-"

    return [
        run(FACE_SEGMENTS + 1, f"bendRadius {away} {FACE}", S),
        corner(f"bendRadius {away} rodDepth", "rodHalfWidth"),
        corner(f"bendRadius {away} rodDepth", "-rodHalfWidth"),
    ]


def slotted_half(half):
    """Half of the inner rod: from the edge of the slit out to the rod's far side.

    The two halves together are the inner electrode with a slit cut along the whole arc, so
    a packet spread along the trap axis leaves through all of it at once. Each half is its
    own polygon rather than the two being one outline with a notch, because they are
    separate pieces of metal - and writing them as one would make the slit a region the
    even-odd rule has to resolve rather than a gap between conductors.
    """
    edge = "slotHalfWidth" if half > 0 else "-slotHalfWidth"
    far = "rodHalfWidth" if half > 0 else "-rodHalfWidth"

    # From the lip of the slit out to the rod's outer edge, along the hyperbola.
    s = (f"({edge} + ({far} - ({edge})) * k / {FACE_SEGMENTS})")
    face = f"(inscribedRadius * sqrt(1 + ({s} / inscribedRadius) * ({s} / inscribedRadius)))"

    return [
        run(FACE_SEGMENTS + 1, f"bendRadius - {face}", s),
        corner("bendRadius - rodDepth", far),
        corner("bendRadius - rodDepth", edge),
    ]


def axial_rod(sign):
    """The top or bottom rod: its face turned toward the trap axis along the bend's own axis."""
    away = "" if sign > 0 else "-"

    return [
        run(FACE_SEGMENTS + 1, f"bendRadius + {S}", f"{away}{FACE}"),
        corner("bendRadius + rodHalfWidth", f"{away}rodDepth"),
        corner("bendRadius - rodHalfWidth", f"{away}rodDepth"),
    ]


def rod(name, vertices, potential, amplitude, from_turns, to_turns):
    return {
        "name": name,
        "shape": "revolve",
        "axis": "z",
        "fromHalfTurns": {"expression": from_turns, "unit": "1"},
        "toHalfTurns": {"expression": to_turns, "unit": "1"},
        "vertices": vertices,
        "potential": q(potential, "V"),
        "driveAmplitude": q(amplitude, "V"),
        "drivePhase": {"expression": "ejectPhase"},
    }


electrodes = [
    # The inner electrode is slit along its whole length, so it is two half-rods at the same
    # potential with the ejection slot between them. Both sweep the entire arc.
    rod("rodInnerUpper", slotted_half(+1), "0", "rfAmplitude", "0", "arcHalfTurns"),
    rod("rodInnerLower", slotted_half(-1), "0", "rfAmplitude", "0", "arcHalfTurns"),

    # The outer rod is the one ejection pushes against, so it carries the push.
    rod("rodOuter", radial_rod(+1), "ejectVolts", "rfAmplitude", "0", "arcHalfTurns"),

    # The other pair, in antiphase: four rods, two spatial patterns, one basis solve.
    rod("rodTop", axial_rod(+1), "0", "-rfAmplitude", "0", "arcHalfTurns"),
    rod("rodBottom", axial_rod(-1), "0", "-rfAmplitude", "0", "arcHalfTurns"),
]

model = {
    "schemaVersion": "0.14",
    "name": "c-trap",
    "description": (
        "A curved quadrupole: four hyperbolic rods bent around a quarter circle, with the "
        "inner one split by an ejection slot. The device that accumulates and cools a packet "
        "and then pushes it sideways into an orbital analyzer, and the first template whose "
        "geometry is invariant under nothing - a translational solve assumes the geometry "
        "repeats along an axis and an axisymmetric one assumes it repeats all the way round, "
        "and a curved axis does neither. Each rod is the linear ion trap's own hyperbolic "
        "outline revolved about the bend axis rather than extruded along a line, which is "
        "what makes a bent rod expressible at all: before the 'revolve' shape existed this "
        "template chained overlapping spheres, and the rod that produced scalloped by 13.6 "
        "percent of its own radius."
    ),
    "parameters": {
        "inscribedRadius": {
            "value": 3.0, "unit": "mm", "minimum": 0.5, "maximum": 20.0,
            "description":
                "GUESSED - neither the paper nor the patent gives a cross-section dimension. "
                "r0, the distance from "
                "the trap axis to the vertex of each rod's face. The quadrupole's own scale: "
                "Mathieu q goes as 1/r0^2, so halving it quadruples q at the same amplitude.",
        },
        "rodHalfWidth": {
            "value": 3.0, "unit": "mm", "minimum": 0.5, "maximum": 20.0,
            "description":
                "GUESSED. How far the hyperbolic face extends either side of the trap axis "
                "before the rod back takes over. Wider follows the ideal field further out "
                "and leaves less gap at the corners for the field to leak through. That the "
                "face IS a hyperbola is sourced - the paper says so in as many words - but no "
                "dimension of it is.",
        },
        "rodDepth": {
            "value": 6.0, "unit": "mm", "minimum": 1.0, "maximum": 40.0,
            "description":
                "GUESSED. From the trap axis to the flat back of a rod. It has to clear the "
                "face it is behind, which reaches inscribedRadius * sqrt(1 + (rodHalfWidth/"
                "inscribedRadius)^2) - 4.243 mm at the shipped values.",
        },
        "bendRadius": {
            "value": 20.0, "unit": "mm", "minimum": 5.0, "maximum": 200.0,
            "description":
                "GUESSED. Neither the paper nor the patent gives a radius of curvature - the "
                "paper says only that the axis follows a C-shaped arc. This is what focuses an "
                "ejected packet - every ion is pushed out along its own radius so their "
                "velocities converge, where a straight trap's ejection is a rigid "
                "translation. A study below 20 mm is a parameter sweep rather than a model of "
                "the device.",
        },
        "arcHalfTurns": {
            "value": 0.5, "unit": "1", "minimum": 0.02, "maximum": 1.0,
            "description":
                "How far round the arc goes, in half turns - 0.5 is a quarter circle, the "
                "usual shape. Half turns rather than radians because cosPi(0.5) is exactly "
                "zero where Math.Cos(pi/2) is 6.1e-17, and a rod meant to end on an axis "
                "would otherwise end a rounding off it.",
        },
        "slotHalfWidth": {
            "value": 0.5, "unit": "mm", "minimum": 0.05, "maximum": 3.0,
            "description":
                "GUESSED width; SOURCED placement and direction. Half the opening of the "
                "ejection slit, measured along the bend axis. The paper puts the slot in the "
                "PULL-OUT electrode - the one closest to the center of curvature - and has "
                "ions leave orthogonally toward that center, and collisional cooling forms "
                "a thin, long thread along the curved axis which must leave all at once. So "
                "the slit runs the whole arc; an angular hole would let out an angular slice. "
                "The opening itself is a guess: neither source gives a width.",
        },
        "rfAmplitude": {
            "value": 500.0, "unit": "V", "minimum": 0.0, "maximum": 1000.0,
            "description":
                "SOURCED. Zero to peak on each rod, the pairs in antiphase. The paper gives "
                "500-2000 V peak to peak on a 0 V DC offset, which is 250-1000 V zero to peak; "
                "the shipped default sits in the middle of that at 1000 V p-p.",
        },
        "driveFrequency": {
            "value": 3.0, "unit": "MHz", "minimum": 0.1, "maximum": 20.0,
            "description": "The confining drive.",
        },
        "ejectPhase": {"value": 0.0, "unit": "1", "minimum": 0.0, "maximum": 2.0},
        "ejectVolts": {
            "value": 0.0, "unit": "V", "minimum": -2000.0, "maximum": 2000.0,
            "description":
                "A SIMPLIFICATION of what the paper describes, and the difference matters. "
                "The instrument pulses 1200 V on the push-out electrode, 1000 V on the "
                "pull-out and 1100 V on the upper and lower - a common 1100 V float, which is "
                "the acceleration toward the analyzer, with 100 V either side of it to drive "
                "ions to the slit. This template has the differential and not the float, "
                "because the float needs a downstream reference the document cannot declare: "
                "the patent lenses 310 and liner 380. Pushing from the outer rod against "
                "earth is that differential written one-sided; the bound is 2 kV so the "
                "sourced numbers are reachable.",
        },
        "launchHalfTurns": {"value": 0.125, "unit": "1", "minimum": 0.0, "maximum": 1.0},
        "launchVolts": {"value": 0.5, "unit": "V", "minimum": 0.0, "maximum": 100.0},
        "cellsPerRadius": {
            "value": 4.0, "unit": "1", "minimum": 2.0, "maximum": 24.0,
            "description": "Cells across the inscribed radius, which sets the solve cost.",
        },
        "cellSize": {"expression": "inscribedRadius / cellsPerRadius", "unit": "mm"},
        "margin": {"expression": "rodDepth + inscribedRadius", "unit": "mm"},
        "reach": {"expression": "bendRadius + rodDepth + inscribedRadius", "unit": "mm"},
    },
    "ion": {"massToCharge": {"value": 500.0, "unit": "Da"}, "chargeNumber": 1},
    "source": {
        "position": {
            "expression": [
                "bendRadius * cosPi(launchHalfTurns)",
                "bendRadius * sinPi(launchHalfTurns)",
                "0",
            ],
            "unit": "mm",
        },
        "direction": {
            "expression": ["-sinPi(launchHalfTurns)", "cosPi(launchHalfTurns)", "0"],
        },
        "accelerationPotential": {"expression": "launchVolts", "unit": "V"},
    },
    "fields": [
        {
            "type": "solved3d",
            "solve3d": {
                "drive": {
                    "frequency": {"expression": "driveFrequency", "unit": "MHz"},
                    "waveform": "sinusoid",
                },
                "minX": q("-margin"),
                "maxX": q("reach"),
                "minY": q("-margin"),
                "maxY": q("reach"),
                "minZ": q("-margin"),
                "maxZ": q("margin"),
                "cellSize": q("cellSize"),
                "electrodes": electrodes,
            },
        }
    ],
    "detector": {
        "planePoint": {
            "expression": [
                "bendRadius * cosPi(arcHalfTurns)",
                "bendRadius * sinPi(arcHalfTurns)",
                "0",
            ],
            "unit": "mm",
        },
        "normal": {"expression": ["sinPi(arcHalfTurns)", "-cosPi(arcHalfTurns)", "0"]},
    },
    "transport": {
        "maximumFlightTime": {"value": 120, "unit": "us"},
        "relativeTolerance": 1e-09,
    },
}

with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(model, f, indent=2)
    f.write("\n")

print(f"wrote {OUT}: {len(electrodes)} electrodes, {FACE_SEGMENTS + 1} vertices on each face")
