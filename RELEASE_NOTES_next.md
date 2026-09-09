# Unreleased

- Record imported gas files and study files in run manifests. `verify` detects changed, missing or retargeted inputs and no longer certifies legacy manifests that did not record them.

- Apply collisions and direct/PIC space charge during trajectory legs of mixed-mode sequences. Preserve conductor detection through time shifts, propagate collision diagnostics, and handle empty packets without reseeding or crashing the printer.
- Refuse diffusive detectors that do not coincide with a supported grid face. Correct existing detector declarations to the faces the solver already used; grids and expected numerical results are unchanged.

- Reject unsupported RF spectra in the diffusive effective-field path instead of applying one frequency to all components. Inactive generators no longer set the averaging period; trajectory RF support is unchanged.
- Invalidate ponderomotive caches from their defining RF state, not spatial probes. Quiver amplitudes are positive for either charge sign.
- Advance the solver-behaviour version to 2 so older numerical results are not reported current after these corrections. The package version is unchanged.
