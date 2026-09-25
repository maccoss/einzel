# Einzel v26.2.0 release notes

Draft. Append as things land; this file is renamed at release time, so a planned patch can
become a feature release without being renamed twice.

## What is new

- **`einzel render still`** draws a model's shaded 3D view to a PNG, headlessly - the same
  picture the interactive viewport shows, from the same composition and camera. Named views
  (`--view iso|side|top|front`), pixel sizes (`--width-px`, `--height-px`), `--see-through`.
  The PNG carries the engine version, the model's hash and every warning in its text chunks,
  and a validity violation hatches the bottom of the picture.

## Fixed

- **Printed mirror boards are drawn in the viewport.** The planar mirror pair and the Astral
  mirror were drawn as their end caps alone, because a board declared on a domain edge has no
  interior for the surface extraction to find.
- **A reflected solve is drawn whole.** A mirror pair declared as one half and reflected was
  drawn with one mirror.
- **A zero-width electrode is lit.** Its surface had no normals, so neither viewport could shade it.
- **The cross-platform viewport fits the instrument to the frame.** A long, thin analyzer was
  drawn across the middle third of the picture.
- **`pnnl-ion-funnel` runs again.** Its density grid ended half a millimeter past its detector,
  which the diffusive mode refuses; the grid now ends at the detector by expression.
- **`einzel solve` printed a volume as a plane.** The terminal output gave only the first two
  node counts and spacings, so a 257x17x257 solve read as 257x17 with no z spacing. It now
  prints every axis. `--json` was always correct.
- **`einzel solve` printed a driven element's channels as identical blocks.** A driven
  structure is one solve per basis channel, and each was labeled only `field 0`, so the RF
  funnel's two channels looked like one solve printed twice. Each is now labeled
  `field 0 channel 0`, `field 0 channel 1`, matching the `channel` field in `--json`.

## Known limits

Carried forward from v26.1.0 until they change:

- **The Windows shell is partial**: seven of the eleven views in the specification exist.
- **A still cannot check the window's own GPU path**, and it does not draw the name of a
  validity violation as text: the codes are in the file's text chunks and on stderr.
- **No installer and no updater.** The assets are portable: unpack and run.
- **The extension sandbox states what it does not enforce** — no network confinement, no
  filesystem confinement, no memory ceiling — on every listing and on every result.
