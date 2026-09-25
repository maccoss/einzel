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
- **A warning when a solve's mesh samples a face two conductors share** (`mesh.node-on-shared-face`).
  Conductors at different potentials may share a face, but if a mesh node, or the point where a
  stencil arm first meets metal, lies exactly on it, which potential it takes is decided by
  rounding - and no refinement study can see it, because every power-of-two mesh keeps the face
  on a node. The warning names the count, one example and the fix (move the solve domain a tenth
  of a cell), and reaches `einzel run`, `einzel preview`, every figure and `einzel solve`. Every
  shipped template and example is clear of it.
- **`einzel solve` reports warnings**, in `--json` and on stderr. It reported residuals and node
  counts and nothing else.
- **`einzel export` carries a solve's warnings** into each field file's header and into its
  `--json` output, and prints them on stderr.

## Fixed

- **`astral-3d`'s flight time depended on rounding.** Three faces shared by foil slices at
  different voltages lay exactly on planes of mesh nodes, so which voltage the stencil arms in
  those planes took was decided by the last bit of arithmetic - worth 0.27 percent. The foil mesh now sits a tenth of a cell
  off every such face (`meshShiftZ`). The shipped flight time is 784.90 us (was 786.44) and the
  drift reversal 336.06 mm (was 336.15). Moving the mesh also showed that the foil's coarse mesh
  carries a 0.83 percent spread by placement alone; the mesh-converged flight time is about
  775 us, a percent below the published arithmetic rather than the 0.4 previously quoted.
- **`einzel export` wrote an RF cross-section as a grounded box.** A two-dimensional geometry
  whose electrodes hold their potential as drive rather than DC exported every node at zero -
  `quadrupole-rf`, `linear-ion-trap`, `stellar-ion-trap`, `multipole-guide`, `paul-trap` and
  `travelling-wave-guide` - and one with a DC chain as well exported the chain alone, dropping
  the RF (`ion-funnel`, `pnnl-ion-funnel`, `tims-front-end`, `tims-tandem`). It now writes one file per basis channel, as
  three-dimensional export already did, named `.channel0.vti`, `.channel1.vti` where there is
  more than one. A driven channel is a pattern with its leading electrode at 1 V rather than a
  potential, so its array is named `channelN_V_per_V` and its header says which electrodes carry
  it; a static geometry's values are unchanged, and every file now carries its provenance and
  warnings in a header.
- **`einzel solve` reported the planar mirror pair without its boards.** A static geometry's
  edge-profile electrodes were solved at zero; the peak it printed was the cap's, so it looked
  right. Runs were never affected.

## Known limits

Carried forward from v26.1.0 until they change:

- **The Windows shell is partial**: seven of the eleven views in the specification exist.
- **No installer and no updater.** The assets are portable: unpack and run.
- **The extension sandbox states what it does not enforce** — no network confinement, no
  filesystem confinement, no memory ceiling — on every listing and on every result.
