"""Builds the models behind docs/gallery-einzel-lens.md from the einzel-lens template.

Run from an `einzel init` project:  python gallery-einzel-lens-build.py
It writes models/f<V>.json (centre voltage series) and models/p_<model>_<Torr>.json
(pressure series at +42 V). Render and run them with the commands on the gallery page.
"""
import json, subprocess

TORR_MBAR = 1.33322368
subprocess.run(["einzel", "new", "models/lens.json", "--from-template", "einzel-lens"], check=True)


def make(name, vc, gas=None, ions=100, mz=100):
    m = json.load(open("models/lens.json"))
    p = m["parameters"]
    for k, v in dict(boreRadius=3, entranceLength=16, centreLength=20, gap=3, driftLength=24,
                     wallThickness=3, centrePotential=vc, beamEnergy=50, launchRadius=0).items():
        p[k]["value"] = v
    p["launchX"] = {"value": 1.0, "unit": "mm", "minimum": 0, "maximum": 100}
    m["source"]["position"] = {"expression": ["launchX", "0", "0"], "unit": "mm"}
    m["ion"]["massToCharge"]["value"] = mz
    # divergence is the HALF angle of the cone: 10 deg gives the 20 deg full cone
    m["source"]["cloud"] = {"ions": ions, "seed": 7, "population": 1,
                            "temperature": {"value": 0, "unit": "K"},
                            "divergence": {"value": 10, "unit": "deg"}}
    for e in m["fields"][0]["solve"]["electrodes"]:  # carry the centre tube past the grounded edge
        if e["name"] == "centre":
            e["maxY"] = {"expression": "outerRadius + wallThickness", "unit": "mm"}
    if gas:
        m["transport"]["gas"] = gas
    json.dump(m, open(f"models/{name}.json", "w"), indent=1)


def gas(torr, model):
    g = {"model": model, "pressure": {"value": torr * TORR_MBAR, "unit": "mbar"},
         "temperature": {"value": 300, "unit": "K"}, "seed": 2026,
         "mass": {"value": 28.0134, "unit": "Da"},
         "polarizability": {"value": 1.74, "unit": "angstrom^3"}}
    if model == "hardSphere":
        g["crossSection"] = {"value": 80, "unit": "angstrom^2"}
    return g


for v in (0, 10, 20, 30, 40, 42):
    make(f"f{v}", v)
for model in ("hardSphere", "langevin"):
    for t in (5e-4, 1e-3, 2e-3):
        make(f"p_{model}_{t}", 42, gas(t, model))
