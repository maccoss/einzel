# Einzel v26.2.0 release notes

Draft. Append as things land; this file is renamed at release time, so a planned patch can
become a feature release without being renamed twice.

## What is new

- **`compact-astral-3d`**: the published Astral analyzer at one fifth of its size, as a whole
  three-dimensional instrument - both converging mirrors, the foil, the drift and its reversal.
  Every published length is kept beside one `scale` parameter that multiplies it, so it is
  exactly similar to `astral-3d` rather than approximately: its flight time is the full-size
  one times the scale to 1.6e-8. `planar-mirror-pair` and `astral-mirror` are now described as
  the cross-section studies they are.

## Fixed

- **`astral-3d`'s flight time depended on rounding.** Three faces shared by foil slices at
  different voltages lay exactly on mesh nodes, so which voltage those nodes took was decided
  by the last bit of arithmetic - worth 0.27 percent. The foil mesh now sits a tenth of a cell
  off every such face (`meshShiftZ`). The shipped flight time is 784.90 us (was 786.44) and the
  drift reversal 336.06 mm (was 336.15). Moving the mesh also showed that the foil's coarse mesh
  carries a 0.83 percent spread by placement alone; the mesh-converged flight time is about
  775 us, a percent below the published arithmetic rather than the 0.4 previously quoted.

## Known limits

Carried forward from v26.1.0 until they change:

- **The Windows shell is partial**: seven of the eleven views in the specification exist.
- **No installer and no updater.** The assets are portable: unpack and run.
- **The extension sandbox states what it does not enforce** — no network confinement, no
  filesystem confinement, no memory ceiling — on every listing and on every result.
