# Einzel v26.1.0 release notes

The first release. An open-source, agent-native ion-optics platform: DC and RF ion optics
across nine decades of pressure, driven from a command line, a live-session server for
agents, and a Windows viewport.

## What it does

- **Fields.** Geometric multigrid in the plane, axisymmetric and in a volume, with cut-cell
  boundaries so a curved electrode is a curve rather than a staircase. Second-order
  convergence measured rather than assumed. Basis superposition, so an RF structure is
  solved once and driven by making its weights functions of time.
- **Transport.** Adaptive Dormand-Prince integration landing exactly on stopping surfaces,
  field discontinuities and collision instants; hard-sphere and Langevin collisions; a
  drift-diffusion density mode for pressures where trajectories stop meaning anything;
  direct-sum and particle-in-cell space charge, now able to run with a gas.
- **Devices as data.** Nineteen templates, from an einzel lens to a curved C-trap to a
  reconstruction of the published Astral analyser. A device is a JSON document, not a class.
- **Analysis.** Arrival-time peaks, resolving power, transmission itemised by the surface
  each ion struck, emittance, turn-around time, secular frequency spectra. Tolerance Monte
  Carlo, parameter scans, boundary bisection, two optimisers.
- **Three surfaces.** `einzel`, `einzel-mcp` and `einzel-shell`, all driving the same
  command objects, so nothing exists in one that cannot be reached from another.

## How it is checked

No SIMION licence, so the load is carried by two other tiers. **Analytic**: a single-stage
reflectron reproduced to 1e-10 relative, four orders inside the 1 ppm budget. **Literature**:
published instruments against published numbers, none produced by this code - the Mathieu
stability boundary on solved round rods, Denison's quadrupole rod ratio, the Langevin rate
coefficient, equipartition in a gas, the LTQ's unit resolution to m/z 2000.

1,203 tests across twelve assemblies, green on Windows and Linux. The examples corpus runs
as a release gate inside that suite: 39 models, every expectation a closed form, a published
value, or an exact invariant.

## Known limits

- **The Windows shell is partial**: seven of the eleven views in the specification exist.
- **No installer and no updater.** The assets are portable: unpack and run. They carry their
  own runtime and never contact the network.
- **The extension sandbox states what it does not enforce** - no network confinement, no
  filesystem confinement, no memory ceiling - on every listing and on every result. That is
  deliberate: a containment measure claimed and not applied is worse than one absent and
  known to be.
