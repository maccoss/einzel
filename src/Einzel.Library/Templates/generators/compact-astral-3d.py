"""Generate the compact-astral-3d template: the published Astral analyzer, scaled.

    python src/Einzel.Library/Templates/generators/compact-astral-3d.py \
        src/Einzel.Library/Templates/astral-3d.json \
        src/Einzel.Library/Templates/compact-astral-3d.json

WHY THIS FILE EXISTS. The compact analyzer this project is designing is an asymmetric-track
multi-reflection time-of-flight instrument of the Astral's family, explored here as the
reproduced Astral scaled down by five. Its only template
was `planar-mirror-pair`: one plane of a mirror pair with the boards as continuous voltage
ramps - no discrete electrodes, no tilt, no drift and no reversal - which is a cross-section
study rather than an instrument. `astral-3d` is the instrument, reproduced from the
literature. This is that instrument, with a knob for its size.

WHY SCALING IS EXACT. Electrostatics has no length scale of its own. Multiply every length by
s and hold every potential fixed, and the potential at the scaled point is the same, the
field is 1/s larger, an ion of the same energy follows the same path scaled by s, and every
flight time is s times as long. Angles do not change - the mirror tilt is a ratio of two
lengths, the injection angle a tangent - and neither do the voltages, so nothing but lengths
and times is touched here. What does NOT scale is anything with an absolute duration of its
own: the detector's response and the ions' turn-around time. A compact analyzer keeps the
full-size one's aberrations and loses resolving power only to those, which is the design
question this template exists to ask.

WHY THE MESH SCALES TOO. The cell sizes are lengths, so they scale with everything else, and a
solve grid rounds each axis's interval count from extent over cell - which is then the same
number at every scale. So the compact solve is the full-size solve's discrete problem exactly,
in smaller units, and costs the same to run. A cell held fixed while the instrument shrank
would be a five-times coarser model of the same device, and would look like one that works
worse.

WHY THE FULL-SIZE VALUES ARE KEPT. A published length multiplied by a chosen factor is not
published, and relabeling it "chosen" would lose the fact that it came from the paper. So each
literal length and duration keeps its full-size value, bounds and provenance under the name
`<name>FullSize`, one `scale` parameter is added and marked chosen, and the working length is
derived as `<name>FullSize * scale`. The geometry's own expressions refer to the working names
and are untouched. `einzel outline` then shows exactly what is published and what was chosen,
and `scale` is a knob: 0.2 for the compact analyzer, 1.0 for the published instrument.

This reads astral-3d.json rather than restating it, so a correction to the published
reconstruction reaches the compact one the next time this runs.
"""

import copy
import json
import sys

SCALE = 0.2

SOURCE = sys.argv[1] if len(sys.argv) > 1 else "src/Einzel.Library/Templates/astral-3d.json"
OUT = sys.argv[2] if len(sys.argv) > 2 else "src/Einzel.Library/Templates/compact-astral-3d.json"

LENGTHS = {"mm", "um", "m"}
TIMES = {"us", "ns", "ms", "s"}

with open(SOURCE, encoding="utf-8") as f:
    full = json.load(f)

model = copy.deepcopy(full)

# ---- the parameter surface: full-size values kept, working lengths derived -------------------

scale_parameter = {
    "value": SCALE,
    "unit": "1",
    "minimum": 0.05,
    "maximum": 1.0,
    "description": (
        "How large this analyzer is against the published one: every length and duration is "
        "its full-size value times this, and every potential and angle is unchanged. 1.0 is "
        "the published Astral; 0.2 is the compact analyzer this project is designing. Exact "
        "by electrostatic similarity - the trajectory scales with the instrument and every "
        "flight time with it - except for anything with an absolute duration of its own, "
        "such as the detector's response and the ions' turn-around time."
    ),
    "provenance": "chosen",
}

full_size = {}
derived = {}
rest = {}

for name, parameter in full["parameters"].items():
    literal = isinstance(parameter.get("value"), (int, float))

    if literal and parameter.get("unit") in LENGTHS | TIMES:
        kept = dict(parameter)
        kept["description"] = (
            "The full-size value, before scaling. " + parameter.get("description", "")
        ).strip()
        full_size[name + "FullSize"] = kept

        derived[name] = {
            "expression": f"{name}FullSize * scale",
            "unit": parameter["unit"],
            "description": f"{name}FullSize at this analyzer's scale.",
        }
    else:
        rest[name] = parameter

# ---- literal lengths and durations outside the parameters: the meshes and the ceiling --------

extra = {}


def lift(node, path, name):
    """Replace a literal length or duration with an expression over a new full-size parameter."""
    unit = node["unit"]
    extra[name + "FullSize"] = {
        "value": node["value"],
        "unit": unit,
        "description": f"The full-size {path}, before scaling.",
        "provenance": "chosen",
    }
    node.pop("value")
    node["expression"] = f"{name}FullSize * scale"


for index, field in enumerate(model["fields"]):
    solve = field.get("solve") or field.get("solve3d")
    cell = solve.get("cellSize") if solve else None

    if cell and isinstance(cell.get("value"), (int, float)):
        lift(cell, f"field {index} cell size", f"cellSize{index}")

ceiling = model["transport"]["maximumFlightTime"]

if isinstance(ceiling.get("value"), (int, float)):
    lift(ceiling, "flight-time ceiling", "maximumFlightTime")

# Anything left literal would not follow `scale`, and a length that silently stays full-size in
# a shrunken instrument is exactly the error this generator exists to prevent.
remaining = []


def audit(node, path):
    if isinstance(node, dict):
        if node.get("unit") in LENGTHS | TIMES and isinstance(node.get("value"), (int, float)) \
                and not path.startswith("/parameters/") and node.get("value") != 0:
            remaining.append(path)
        for key, child in node.items():
            audit(child, f"{path}/{key}")
    elif isinstance(node, list):
        for i, child in enumerate(node):
            audit(child, f"{path}/{i}")


audit(model, "")

if remaining:
    raise SystemExit("literal lengths left unscaled: " + ", ".join(remaining))

# The scale first, then every full-size value, then the working lengths derived from them, then
# the published template's own derived parameters - which refer to the working names.
model["parameters"] = {"scale": scale_parameter, **full_size, **extra, **derived, **rest}

model["name"] = "compact-astral-3d"
model["description"] = (
    "The published Thermo Astral analyzer at a chosen scale - one fifth as shipped - as a "
    "complete three-dimensional instrument: the compact asymmetric-track multi-reflection "
    "time-of-flight analyzer this project is designing. GENERATED from astral-3d.json by "
    "generators/compact-astral-3d.py: every length and duration is its full-size value "
    "(kept as a parameter with its provenance) times `scale`, and every potential and angle "
    "is the published instrument's. That is exact rather than approximate: electrostatics has "
    "no length scale, so at fixed voltages an ion of the same energy follows the same path "
    "scaled by the same factor, and every flight time scales with it. The meshes scale too, "
    "so the solve is the full-size discrete problem in smaller units and costs the same. What "
    "does not scale is anything with an absolute duration - the detector's response and the "
    "ions' turn-around time - so this analyzer keeps the full-size one's aberrations and "
    "loses resolving power only to those. See astral-3d for what is published and what is "
    "reconstructed, and docs/astral-log.md for how. The full-size description follows. "
    + full["description"]
)

with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(model, f, indent=2, ensure_ascii=False)
    f.write("\n")

print(f"wrote {OUT}: {len(full_size) + len(extra)} full-size values under one scale of {SCALE}")
for name in [*full_size, *extra]:
    print("  ", name)
