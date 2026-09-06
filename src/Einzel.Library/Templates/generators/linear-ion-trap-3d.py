"""Generate the linear-ion-trap-3d template: the 2002 trap's three axial sections in a volume solve.

    python src/Einzel.Library/Templates/generators/linear-ion-trap-3d.py src/Einzel.Library/Templates/linear-ion-trap-3d.json

The cross-section is the one linear-ion-trap.py draws - the same hyperbolic half-rod outline,
the same slot - extruded along z as prisms: three sections per rod at their own DC, the
ejection slot only in the centre section, and two end lenses with a square aperture. The
axial DC structure is what the cross-section cannot say and what this template is for.
Edit this file and regenerate rather than hand-editing the template.
"""
import json
import sys

FACE_SEGMENTS = 24
OUT = sys.argv[1] if len(sys.argv) > 1 else "linear-ion-trap-3d.json"


def q(expr, unit):
    return {"expression": expr, "unit": unit}


def half_rod_vertices(axis, sign, half, slot_param, stretch_expr):
    """The 2-D half-rod outline, in (x, y), as one run on the hyperbola plus the back and slot corners."""
    s = f"({slot_param} + (rodHalfWidth - {slot_param}) * k / {FACE_SEGMENTS})"
    r = f"({stretch_expr}inscribedRadius * sqrt(1 + ({s} / inscribedRadius) * ({s} / inscribedRadius)))"
    along = f"{'' if sign > 0 else '-'}{r}"
    across = f"{'' if half > 0 else '-'}{s}"
    back = f"{'' if sign > 0 else '-'}({stretch_expr}rodDepth)"
    mouth = f"({stretch_expr}inscribedRadius * sqrt(1 + ({slot_param} / inscribedRadius) * ({slot_param} / inscribedRadius)) + slotDepth)"
    channel_end = f"{'' if sign > 0 else '-'}{mouth}"
    relief = f"{'' if half > 0 else '-'}(reliefRatio * {slot_param})"
    verts = [
        (along, across, FACE_SEGMENTS + 1),
        (back, f"{'' if half > 0 else '-'}rodHalfWidth", None),
        (back, relief, None),
        (channel_end, relief, None),
        (channel_end, f"{'' if half > 0 else '-'}{slot_param}", None),
    ]

    def vertex(a, b, count):
        x, y = (a, b) if axis == "x" else (b, a)
        v = {"x": q(x, "mm"), "y": q(y, "mm")}
        if count is not None:
            v = {"count": {"value": count, "unit": "1"}, "index": "k", **v}
        return v

    return [vertex(a, b, c) for a, b, c in verts]


def prism(name, axis, sign, half, slot_param, stretch_expr, lower, upper, potential, taps):
    return {
        "name": name,
        "shape": "prism",
        "axis": "z",
        "lower": q(lower, "mm"),
        "upper": q(upper, "mm"),
        "vertices": half_rod_vertices(axis, sign, half, slot_param, stretch_expr),
        "potential": q(potential, "V"),
        "taps": taps,
    }


rf_x = {"drive": "rf", "amplitude": q("-rfAmplitude", "V")}
rf_y = {"drive": "rf", "amplitude": q("rfAmplitude", "V")}
ex_plus = {"drive": "excite", "amplitude": q("0.5 * exciteAmplitude", "V")}
ex_minus = {"drive": "excite", "amplitude": q("-0.5 * exciteAmplitude", "V")}

# Three sections: front (z negative), centre, back. The slot is cut only in the centre section's +x rod.
# The quadrupolar DC (dcOffset, x pair up and y pair down) sets Mathieu a; the end offset
# is common to all four rods of a section - that is what makes an axial well. Written
# quadrupolar, as the first draft had it, it is zero on the axis and makes none.
sections = [
    ("front", "-(centreHalfLength + sectionGap + endLength)", "-(centreHalfLength + sectionGap)", "dcOffset + endOffset", "-dcOffset + endOffset", "noSlot"),
    ("centre", "-centreHalfLength", "centreHalfLength", "dcOffset", "-dcOffset", "slotXPlus"),
    ("back", "centreHalfLength + sectionGap", "centreHalfLength + sectionGap + endLength", "dcOffset + endOffset", "-dcOffset + endOffset", "noSlot"),
]

electrodes = []
for section, lower, upper, x_dc, y_dc, x_slot in sections:
    electrodes += [
        prism(f"{section}XPlusUpper", "x", +1, +1, x_slot, "xStretch + ", lower, upper, x_dc, [rf_x, ex_plus]),
        prism(f"{section}XPlusLower", "x", +1, -1, x_slot, "xStretch + ", lower, upper, x_dc, [rf_x, ex_plus]),
        prism(f"{section}XMinusUpper", "x", -1, +1, "noSlot", "xStretch + ", lower, upper, x_dc, [rf_x, ex_minus]),
        prism(f"{section}XMinusLower", "x", -1, -1, "noSlot", "xStretch + ", lower, upper, x_dc, [rf_x, ex_minus]),
        prism(f"{section}YPlusRight", "y", +1, +1, "noSlot", "yStretch + ", lower, upper, y_dc, [rf_y]),
        prism(f"{section}YPlusLeft", "y", +1, -1, "noSlot", "yStretch + ", lower, upper, y_dc, [rf_y]),
        prism(f"{section}YMinusRight", "y", -1, +1, "noSlot", "yStretch + ", lower, upper, y_dc, [rf_y]),
        prism(f"{section}YMinusLeft", "y", -1, -1, "noSlot", "yStretch + ", lower, upper, y_dc, [rf_y]),
    ]


def lens(name, z_lower, z_upper):
    """A plate with a square aperture, as four boxes round the hole."""
    a = "lensAperture / 2"
    w = "lensHalfWidth"
    boxes = []
    for part, (x0, x1, y0, y1) in {
        "Top": (f"-{w}", w, a, w),
        "Bottom": (f"-{w}", w, f"-{w}", f"-{a}"),
        "Left": (f"-{w}", f"-{a}", f"-{a}", a),
        "Right": (a, w, f"-{a}", a),
    }.items():
        boxes.append({
            "name": f"{name}{part}", "shape": "box",
            "minX": q(x0, "mm"), "maxX": q(x1, "mm"),
            "minY": q(y0, "mm"), "maxY": q(y1, "mm"),
            "minZ": q(z_lower, "mm"), "maxZ": q(z_upper, "mm"),
            "potential": q("lensPotential", "V"),
        })
    return boxes


electrodes += lens("frontLens", "-(rodHalfLength + lensGap + lensThickness)", "-(rodHalfLength + lensGap)")
electrodes += lens("backLens", "rodHalfLength + lensGap", "rodHalfLength + lensGap + lensThickness")

doc = {
    "schemaVersion": "0.9",
    "name": "linear-ion-trap-3d",
    "description": (
        "The 2002 linear ion trap (Schwartz, Senko, Syka, JASMS 2002) as a volume: the same hyperbolic half-rods and "
        "0.25 mm slot as the linear-ion-trap cross-section, extruded along the axis as prisms and cut into the paper's "
        "three axial sections of 12, 37 and 12 mm, each at its own DC offset, with the slot only in the centre section "
        "and a plate lens with a 2 mm aperture at each end. The end sections' DC offset makes the axial well; the paper "
        "argues that applying the RF and the dipole excitation equally across all three sections keeps the excitation "
        "field free of an axial component in the centre section, which a single-section trap with DC end lenses cannot "
        "do, and that is what this template exists to measure. The cross-section is generated from the same outline as "
        "linear-ion-trap, so anything measured there about the slot and the stretch holds here. Not modelled: the gaps' "
        "exact widths and the lenses' thickness, which the paper does not give."
    ),
    "parameters": {
        "inscribedRadius": {"value": 4.0, "unit": "mm", "minimum": 1.0, "maximum": 20.0, "description": "Axis to the vertex of each hyperbolic rod face, r0. Published: 4 mm."},
        "rodHalfWidth": {"value": 6.0, "unit": "mm", "minimum": 1.0, "maximum": 30.0, "description": "Half the width of a rod across the face, where the hyperbola is truncated. Not published."},
        "rodDepth": {"value": 12.0, "unit": "mm", "minimum": 4.0, "maximum": 60.0, "description": "Axis to the flat back of an unstretched rod."},
        "xStretch": {"value": 0.75, "unit": "mm", "minimum": -2.0, "maximum": 5.0, "description": "How far the x rod pair is moved outward. Published: 0.75 mm."},
        "yStretch": {"value": 0.0, "unit": "mm", "minimum": -2.0, "maximum": 5.0, "description": "How far the y rod pair is moved outward. Zero in the 2002 trap."},
        "slotXPlus": {"value": 0.125, "unit": "mm", "minimum": 0.0, "maximum": 2.0, "description": "Half the height of the ejection slot through the centre section's +x rod. Published: 0.25 mm high, along the centre section."},
        "noSlot": {"value": 0.0, "unit": "mm", "minimum": 0.0, "maximum": 0.0, "description": "A closed slot, for the rods and sections that have none. Fixed at zero; it exists so every half-rod is the same outline."},
        "slotDepth": {"value": 0.5, "unit": "mm", "minimum": 0.0, "maximum": 20.0, "description": "How far behind the face the slot keeps its face height before opening into the relief. Not published."},
        "reliefRatio": {"value": 8.0, "unit": "1", "minimum": 1.0, "maximum": 40.0, "description": "The slot opens behind its channel to this multiple of its half-height. Not published."},
        "centreHalfLength": {"value": 18.5, "unit": "mm", "minimum": 5.0, "maximum": 100.0, "description": "Half the centre section's length. Published: 37 mm."},
        "endLength": {"value": 12.0, "unit": "mm", "minimum": 2.0, "maximum": 50.0, "description": "Each end section's length. Published: 12 mm."},
        "sectionGap": {"value": 1.0, "unit": "mm", "minimum": 0.1, "maximum": 5.0, "description": "Axial gap between sections. Not published."},
        "lensGap": {"value": 1.0, "unit": "mm", "minimum": 0.1, "maximum": 10.0, "description": "Gap between the end of the rods and each lens. Not published."},
        "lensThickness": {"value": 1.0, "unit": "mm", "minimum": 0.1, "maximum": 5.0, "description": "Each lens plate's thickness. Not published."},
        "lensAperture": {"value": 2.0, "unit": "mm", "minimum": 0.5, "maximum": 8.0, "description": "The lens aperture, a square of this side. Published: 2 mm."},
        "lensHalfWidth": {"value": 13.0, "unit": "mm", "minimum": 4.0, "maximum": 30.0, "description": "Half the lens plate's width."},
        "rfAmplitude": {"value": 257.0, "unit": "V", "minimum": 0.0, "maximum": 5000.0, "description": "Main RF, zero to peak, y pair up and x pair down. As in linear-ion-trap: the stretched x pair makes q per volt 0.822 of the ideal formula's."},
        "dcOffset": {"value": 0.0, "unit": "V", "minimum": -500.0, "maximum": 500.0, "description": "Steady quadrupolar offset on the centre section: x pair up, y pair down. Zero is the a = 0 line."},
        "endOffset": {"value": 3.0, "unit": "V", "minimum": -100.0, "maximum": 100.0, "description": "How far above the centre section both end sections sit. This is the axial well: the paper holds the ends 3 V above the centre for ejection and 20 V for its resolution measurement."},
        "lensPotential": {"value": 20.0, "unit": "V", "minimum": -500.0, "maximum": 500.0, "description": "Both end lenses' potential."},
        "driveFrequency": {"value": 1.0, "unit": "MHz", "minimum": 0.1, "maximum": 10.0, "description": "Main RF frequency. Published: 1 MHz."},
        "exciteFrequency": {"value": 0.4213, "unit": "MHz", "minimum": 0.001, "maximum": 5.0, "description": "Dipole excitation across the x rods, all three sections: the secular frequency at q = 0.88."},
        "exciteAmplitude": {"value": 0.0, "unit": "V", "minimum": 0.0, "maximum": 200.0, "description": "Zero to peak, as the difference between the x rods; zero is off."},
        "launchOffset": {"value": 0.3, "unit": "mm", "minimum": 0.0, "maximum": 3.0, "description": "Where the ion starts, off axis, at rest, at the centre of the trap."},
        "holdTime": {"value": 200.0, "unit": "us", "minimum": 1.0, "maximum": 100000.0, "description": "How long the flight runs."},
        "housingClearance": {"value": 1.5, "unit": "mm", "minimum": 0.5, "maximum": 20.0, "description": "From the deepest rod back to the grounded domain edge."},
        "cellSize": {"value": 0.5, "unit": "mm", "minimum": 0.1, "maximum": 2.0, "description": "Grid cell. Half a millimetre is eight cells across the inscribed radius and half a million nodes; the faces are cut cells either way."},

        "rodBackX": {"expression": "xStretch + rodDepth", "unit": "mm"},
        "domainHalfWidth": {"expression": "rodBackX + housingClearance", "unit": "mm"},
        "rodHalfLength": {"expression": "centreHalfLength + sectionGap + endLength", "unit": "mm"},
        "domainHalfLength": {"expression": "rodHalfLength + lensGap + lensThickness + housingClearance", "unit": "mm"},
        "detectorX": {"expression": "rodBackX + 0.5 * housingClearance", "unit": "mm"},
    },
    "ion": {"massToCharge": {"value": 524.3, "unit": "Da"}, "chargeNumber": 1},
    "source": {
        "position": {"expression": ["launchOffset", "launchOffset", "0"], "unit": "mm"},
        "direction": {"value": [1, 0, 0]},
        "accelerationPotential": {"value": 0, "unit": "V"},
        "cloud": {"ions": 1, "temperature": {"value": 300, "unit": "K"}, "seed": 20260906},
    },
    "fields": [
        {
            "type": "solved3d",
            "solve3d": {
                "drives": [
                    {"name": "rf", "frequency": q("driveFrequency", "MHz"), "waveform": "sinusoid"},
                    {"name": "excite", "frequency": q("exciteFrequency", "MHz"), "waveform": "sinusoid"},
                ],
                "minX": q("-domainHalfWidth", "mm"), "maxX": q("domainHalfWidth", "mm"),
                "minY": q("-domainHalfWidth", "mm"), "maxY": q("domainHalfWidth", "mm"),
                "minZ": q("-domainHalfLength", "mm"), "maxZ": q("domainHalfLength", "mm"),
                "cellSize": q("cellSize", "mm"),
                "electrodes": electrodes,
            },
        }
    ],
    "detector": {
        "planePoint": {"expression": ["detectorX", "0", "0"], "unit": "mm"},
        "normal": {"value": [-1, 0, 0]},
    },
    "transport": {
        "mode": "trajectory",
        "relativeTolerance": 1e-9,
        "maximumFlightTime": q("holdTime", "us"),
        "gas": {
            "model": "hardSphere",
            "pressure": {"value": 4.0e-3, "unit": "mbar"},
            "temperature": {"value": 300, "unit": "K"},
            "mass": {"value": 4.002602, "unit": "Da"},
            "crossSection": {"value": 150.0, "unit": "angstrom^2"},
            "seed": 20260906,
        },
    },
}

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
print(f"wrote {OUT}: {len(electrodes)} electrodes")
