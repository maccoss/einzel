"""Generate the linear-ion-trap template: hyperbolic rods as polygons, a slot in the +x rod.

    python src/Einzel.Library/Templates/generators/linear-ion-trap.py src/Einzel.Library/Templates/linear-ion-trap.json

Edit this file and regenerate rather than hand-editing the template: the eight half-rods
are one function applied eight ways, and a hand edit to one of them is a geometry nobody
described. Every vertex is an expression over the parameter surface, so the document stays
parametric: change inscribedRadius or the slot height and every face moves with it. The
face of each rod is one run of FACE_SEGMENTS + 1 points on the hyperbola r^2 = r0^2 + s^2
between the slot edge and the rod's half-width; the chord sagitta at that sampling is about
two microns on a four millimetre rod.
"""
import json
import sys

FACE_SEGMENTS = 24
OUT = sys.argv[1] if len(sys.argv) > 1 else "linear-ion-trap.json"
VARIANT = sys.argv[2] if len(sys.argv) > 2 else "ltq"     # "ltq" (Schwartz 2002) or "stellar" (Remes 2024)

if VARIANT == "ltq":
    NAME = "linear-ion-trap"
    XSTRETCH, YSTRETCH = 0.75, 0.0
    SLOTS = {"slotXPlus": 0.125, "slotXMinus": 0.0, "slotYPlus": 0.0, "slotYMinus": 0.0}
    PRESSURE = 4.0e-3
    MZ = 524.3
    DESCRIPTION = (
        "A radial-ejection linear ion trap in cross-section, after the two-dimensional quadrupole ion trap "
        "of Schwartz, Senko and Syka (JASMS 2002, 13, 659): four hyperbolic rods at an inscribed radius of 4 mm, "
        "the x pair moved out 0.75 mm to compensate the field fault of a 0.25 mm ejection slot cut through the +x "
        "rod, the main RF at 1 MHz on the rod pairs in antiphase, and a supplementary dipole excitation across the "
        "x rods for resonance ejection through the slot. Helium at three millitorr damps the ion. Each rod is two "
        "polygons meeting at the slot, so a slot of zero height is a whole rod and a slot in every rod - the "
        "symmetric dual-pressure design - is four numbers. The default holds one ion at a Mathieu q near 0.3 with "
        "the excitation off; a study raises the RF into the excitation's resonance and asks what leaves through the "
        "slot, and when. What this cross-section cannot express is the axial structure - three DC sections and the "
        "end lenses - which belongs to a volume solve."
    )
else:
    NAME = "stellar-ion-trap"
    XSTRETCH, YSTRETCH = 0.76, 0.76
    SLOTS = {"slotXPlus": 0.125, "slotXMinus": 0.125, "slotYPlus": 0.125, "slotYMinus": 0.125}
    PRESSURE = 6.666e-4
    MZ = 622.0
    DESCRIPTION = (
        "The mass-analysing cell of the Stellar's linear ion trap in cross-section, as its paper describes it "
        "(Remes, Jacob, Heil, Shulman, MacLean, MacCoss, J. Proteome Res. 2024, 23, 5476): the Velos Pro structure, "
        "a 4.0 mm field radius with a four-fold symmetric stretch of 0.76 mm - both rod pairs moved out, which "
        "restores the x-y symmetry the 2002 trap's two-fold stretch broke - slots in all four rods, and helium at "
        "0.5 mTorr in the low-pressure cell that performs the mass analysis. The paper gives the analysis scan rates "
        "(33, 67, 125 and 200 kDa/s) and the peak widths they produce at m/z 622 (about 0.35, 0.5, 0.7 and 1.0 Th); "
        "it does not give the RF frequency, the ejection q or the excitation, which are carried over from the 2002 "
        "trap and named as guesses. The slot's depth and relief and the rods' truncation are guesses too. The high-"
        "pressure cell (~6 mTorr) that receives ions and performs isolation and activation is the same cross-section "
        "at a different heliumPressure. Generated from the same script as linear-ion-trap: the two are one function "
        "with different numbers."
    )


def q(expr, unit):
    return {"expression": expr, "unit": unit}


def frac(k):
    # k / FACE_SEGMENTS as a literal both coordinates share, so the sampled point
    # lies exactly on the hyperbola whatever the literal rounds to.
    return repr(k / FACE_SEGMENTS)


def half_rod(axis, sign, half, slot_param, stretch_expr):
    """One half of a rod: the face from the slot edge to the rod's half-width as one run, then the back.

    axis: 'x' for the pair on the x axis (face x = stretch + sqrt(r0^2 + y^2)),
          'y' for the pair on the y axis (face y = sqrt(r0^2 + x^2)).
    sign: +1 or -1, which side of the axis.
    half: +1 or -1, which side of the slot (the transverse coordinate's sign).
    """
    # Transverse coordinate s runs from the slot edge to the half-width over the run.
    s = f"({slot_param} + (rodHalfWidth - {slot_param}) * k / {FACE_SEGMENTS})"
    r = f"({stretch_expr}inscribedRadius * sqrt(1 + ({s} / inscribedRadius) * ({s} / inscribedRadius)))"
    along = f"{'' if sign > 0 else '-'}{r}"
    across = f"{'' if half > 0 else '-'}{s}"
    back = f"{'' if sign > 0 else '-'}({stretch_expr}rodDepth)"
    # The slot is a channel of the face's half-height for slotDepth behind the face, then
    # opens to a relief of reliefRatio times the half-height. With the slot closed the
    # relief closes with it and the rod is solid.
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


def electrode(name, axis, sign, half, slot_param, stretch_expr, potential, taps):
    return {
        "name": name,
        "shape": "polygon",
        "vertices": half_rod(axis, sign, half, slot_param, stretch_expr),
        "potential": q(potential, "V"),
        "taps": taps,
    }


rf_x = {"drive": "rf", "amplitude": q("-rfAmplitude", "V")}
rf_y = {"drive": "rf", "amplitude": q("rfAmplitude", "V")}
ex_plus = {"drive": "excite", "amplitude": q("0.5 * exciteAmplitude", "V")}
ex_minus = {"drive": "excite", "amplitude": q("-0.5 * exciteAmplitude", "V")}

electrodes = [
    electrode("rodXPlusUpper", "x", +1, +1, "slotXPlus", "xStretch + ", "dcOffset", [rf_x, ex_plus]),
    electrode("rodXPlusLower", "x", +1, -1, "slotXPlus", "xStretch + ", "dcOffset", [rf_x, ex_plus]),
    electrode("rodXMinusUpper", "x", -1, +1, "slotXMinus", "xStretch + ", "dcOffset", [rf_x, ex_minus]),
    electrode("rodXMinusLower", "x", -1, -1, "slotXMinus", "xStretch + ", "dcOffset", [rf_x, ex_minus]),
    electrode("rodYPlusRight", "y", +1, +1, "slotYPlus", "yStretch + ", "-dcOffset", [rf_y]),
    electrode("rodYPlusLeft", "y", +1, -1, "slotYPlus", "yStretch + ", "-dcOffset", [rf_y]),
    electrode("rodYMinusRight", "y", -1, +1, "slotYMinus", "yStretch + ", "-dcOffset", [rf_y]),
    electrode("rodYMinusLeft", "y", -1, -1, "slotYMinus", "yStretch + ", "-dcOffset", [rf_y]),
]


def wall(name, x0, x1, y0, y1):
    """A grounded housing wall along one edge of the domain: electrically the same as the
    grounded domain edge, but an electrode, so an ion that leaves through a slot strikes
    metal with a name instead of coasting out of the box. With slots in all four rods
    three of the four ways out have no detector behind them."""
    return {
        "name": name, "shape": "rectangle",
        "minX": q(x0, "mm"), "maxX": q(x1, "mm"), "minY": q(y0, "mm"), "maxY": q(y1, "mm"),
        "potential": {"value": 0, "unit": "V"},
    }


electrodes += [
    wall("housingLeft", "-domainHalfWidth", "-(domainHalfWidth - wallThickness)", "-domainHalfWidth", "domainHalfWidth"),
    wall("housingBottom", "-domainHalfWidth", "domainHalfWidth", "-domainHalfWidth", "-(domainHalfWidth - wallThickness)"),
    wall("housingTop", "-domainHalfWidth", "domainHalfWidth", "domainHalfWidth - wallThickness", "domainHalfWidth"),
]

doc = {
    "schemaVersion": "0.9",
    "name": NAME,
    "description": DESCRIPTION,
    "parameters": {
        "inscribedRadius": {
            "value": 4.0, "unit": "mm", "minimum": 1.0, "maximum": 20.0,
            "description": "Axis to the vertex of each hyperbolic rod face, r0. Published: 4 mm.",
        },
        "rodHalfWidth": {
            "value": 6.0, "unit": "mm", "minimum": 1.0, "maximum": 30.0,
            "description": "Half the width of a rod across the face, where the hyperbola is truncated. Not published; a truncation at one and a half inscribed radii is typical of machined hyperbolic rods.",
        },
        "rodDepth": {
            "value": 12.0, "unit": "mm", "minimum": 4.0, "maximum": 60.0,
            "description": "Axis to the flat back of an unstretched rod. Must exceed the face's height at the truncation, sqrt(r0^2 + halfWidth^2).",
        },
        "xStretch": {
            "value": XSTRETCH, "unit": "mm", "minimum": -2.0, "maximum": 5.0,
            "description": "How far the x rod pair is moved outward from the ideal position. The published trap moves the slotted rod and the rod opposite out 0.75 mm to compensate the slot's field fault, the analogue of the stretched 3-D trap. It costs a sixth of the quadrupole strength (q per volt 0.823 of ideal) and adds an octupole of 1.7e-3 of the quadrupole; zero is the ideal quadrupole.",
        },
        "yStretch": {
            "value": YSTRETCH, "unit": "mm", "minimum": -2.0, "maximum": 5.0,
            "description": "How far the y rod pair is moved outward. Zero in the 2002 trap, whose stretch is two-fold; 0.76 mm in the Velos Pro and Stellar traps, whose stretch is four-fold - both pairs out - which restores the x-y symmetry the two-fold stretch broke and removes the axial barrier to injection that asymmetry caused.",
        },
        "slotXPlus": {
            "value": SLOTS["slotXPlus"], "unit": "mm", "minimum": 0.0, "maximum": 2.0,
            "description": "Half the height of the ejection slot through the +x rod. Published slot: 0.25 mm high. Zero closes it and the rod is whole.",
        },
        "slotDepth": {
            "value": 0.5, "unit": "mm", "minimum": 0.0, "maximum": 20.0,
            "description": "How far behind the face a slot keeps its face height before opening into the relief. Not published. Measured here: a 0.25 mm channel run straight through 7 mm of rod catches three quarters of the ions ejected toward it on its own walls, because the slot mouth is a diverging aperture lens for an ion leaving the RF field; half a millimetre lets most of them through.",
        },
        "reliefRatio": {
            "value": 8.0, "unit": "1", "minimum": 1.0, "maximum": 40.0,
            "description": "The slot opens behind its channel to this multiple of its half-height; one is no relief, the channel running through to the back at the face's height. Not published. Measured at effective q 0.875 with twenty ions: the straight channel passes none or one of the ions ejected toward it and puts the rest on its walls, an eightfold relief half a millimetre behind the face passes four to nine, and the rest strike the relief's walls or the face beside the slot. Scales with the slot, so a closed slot has no relief and the rod is solid.",
        },
        "slotXMinus": {
            "value": SLOTS["slotXMinus"], "unit": "mm", "minimum": 0.0, "maximum": 2.0,
            "description": "Half-height of a slot through the -x rod. Zero in the 2002 trap, which ejects one way; 0.125 for the symmetric design with slots in all four rods.",
        },
        "slotYPlus": {
            "value": SLOTS["slotYPlus"], "unit": "mm", "minimum": 0.0, "maximum": 2.0,
            "description": "Half-height of a slot through the +y rod. Zero in the 2002 trap.",
        },
        "slotYMinus": {
            "value": SLOTS["slotYMinus"], "unit": "mm", "minimum": 0.0, "maximum": 2.0,
            "description": "Half-height of a slot through the -y rod. Zero in the 2002 trap.",
        },
        "rfAmplitude": {
            "value": 257.0, "unit": "V", "minimum": 0.0, "maximum": 5000.0,
            "description": "Main RF, zero to peak, applied to the y pair with the x pair at its negative. The ideal formula q = 4 z e V / (m omega^2 r0^2) gives m/z 524 a q of V / 858 V at 1 MHz and r0 = 4 mm - but the stretched x pair weakens the quadrupole term to 0.823 of that (measured from the solved field), so the real q is V / 1043 V: 257 V is q = 0.25, and the q = 0.88 the published scan ejects at needs 918 V. The paper's q scale is the effective one, since it quotes the ideal secular frequency for its q = 0.83.",
        },
        "dcOffset": {
            "value": 0.0, "unit": "V", "minimum": -500.0, "maximum": 500.0,
            "description": "Steady offset on the x pair, its negative on the y pair. Sets Mathieu a; zero is the a = 0 line every published working point sits on.",
        },
        "driveFrequency": {
            "value": 1.0, "unit": "MHz", "minimum": 0.1, "maximum": 10.0,
            "description": "Main RF frequency. Published: 1 MHz.",
        },
        "exciteFrequency": {
            "value": 0.4213, "unit": "MHz", "minimum": 0.001, "maximum": 5.0,
            "description": "Supplementary dipole excitation across the x rods. Resonance ejection at q = 0.88 needs the secular frequency there, beta(0.88) = 0.8427 of half the drive: 421.3 kHz at 1 MHz. The paper isolates at q = 0.83, which is 368 kHz by the same arithmetic and is the number it quotes.",
        },
        "exciteAmplitude": {
            "value": 0.0, "unit": "V", "minimum": 0.0, "maximum": 200.0,
            "description": "Zero to peak, as the potential difference between the x rods; each rod carries half. Zero is off. The published scan uses 3 V plus 20 mV per m/z, 13.5 V at m/z 524.",
        },
        "heliumPressure": {
            "value": PRESSURE, "unit": "mbar", "minimum": 1.0e-6, "maximum": 2.0e-2,
            "description": "Helium bath. Published: about three millitorr, which is 4.0e-3 mbar. The dual-pressure design holds its first cell near 6.7e-3 mbar and its second near 5.3e-4.",
        },
        "collisionCrossSection": {
            "value": 150.0, "unit": "angstrom^2", "minimum": 10.0, "maximum": 2000.0,
            "description": "Hard-sphere ion-helium cross-section. 150 square angstroms is a tetrapeptide's helium collision cross-section; what it sets is how fast the ion cools, not where it ejects.",
        },
        "launchOffset": {
            "value": 0.3, "unit": "mm", "minimum": 0.0, "maximum": 3.0,
            "description": "Where the ion starts, off axis in both transverse directions, at rest. A thermal cloud in this well at 300 K has a width of about a tenth of a millimetre.",
        },
        "holdTime": {
            "value": 200.0, "unit": "us", "minimum": 1.0, "maximum": 100000.0,
            "description": "How long the flight runs. With the excitation off an ion is still inside at the end; a scan study sets this to the length of its ramp.",
        },
        "housingClearance": {
            "value": 1.5, "unit": "mm", "minimum": 0.5, "maximum": 20.0,
            "description": "From the deepest rod back to the grounded housing. The real rods sit in a grounded chamber further away; this is where the solve stops. The +x side is left to the detector plane; the other three sides are grounded walls, so an ion leaving through a slot with no detector behind it strikes metal with a name.",
        },
        "wallThickness": {
            "value": 0.25, "unit": "mm", "minimum": 0.05, "maximum": 2.0,
            "description": "Thickness of the grounded housing walls on the three sides without a detector. Electrically nothing - the domain edge is grounded anyway - but an electrode is something an ion can strike and be counted against.",
        },
        "cellsPerRadius": {
            "value": 32.0, "unit": "1", "minimum": 4.0, "maximum": 128.0,
            "description": "Grid cells across the inscribed radius. 32 is a 0.125 mm cell, which puts two cells across the slot; the faces are cut cells either way.",
        },

        "rodBackX": {"expression": "xStretch + rodDepth", "unit": "mm"},
        "domainHalfWidth": {"expression": "rodBackX + housingClearance", "unit": "mm"},
        "detectorX": {"expression": "rodBackX + 0.5 * housingClearance", "unit": "mm"},
        "cellSize": {"expression": "inscribedRadius / cellsPerRadius", "unit": "mm"},
    },
    "ion": {"massToCharge": {"value": MZ, "unit": "Da"}, "chargeNumber": 1},
    "source": {
        "position": {"expression": ["launchOffset", "launchOffset", "0"], "unit": "mm"},
        "direction": {"value": [1, 0, 0]},
        "accelerationPotential": {"value": 0, "unit": "V"},
        "cloud": {"ions": 1, "temperature": {"value": 300, "unit": "K"}, "seed": 20260905},
    },
    "fields": [
        {
            "type": "solved2d",
            "solve": {
                "drives": [
                    {"name": "rf", "frequency": q("driveFrequency", "MHz"), "waveform": "sinusoid"},
                    {"name": "excite", "frequency": q("exciteFrequency", "MHz"), "waveform": "sinusoid"},
                ],
                "minX": q("-domainHalfWidth", "mm"),
                "minY": q("-domainHalfWidth", "mm"),
                "maxX": q("domainHalfWidth", "mm"),
                "maxY": q("domainHalfWidth", "mm"),
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
            "pressure": q("heliumPressure", "mbar"),
            "temperature": {"value": 300, "unit": "K"},
            "mass": {"value": 4.002602, "unit": "Da"},
            "crossSection": q("collisionCrossSection", "angstrom^2"),
            "seed": 20260905,
        },
    },
}

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
print(f"wrote {OUT}: {len(electrodes)} electrodes, {sum(len(e.get('vertices', [])) for e in electrodes)} vertex entries")
