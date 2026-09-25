# Einzel v26.2.0 release notes

Draft. Append as things land; this file is renamed at release time, so a planned patch can
become a feature release without being renamed twice.

## What is new

_Nothing yet._

## Fixed

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
- **No installer and no updater.** The assets are portable: unpack and run.
- **The extension sandbox states what it does not enforce** — no network confinement, no
  filesystem confinement, no memory ceiling — on every listing and on every result.
