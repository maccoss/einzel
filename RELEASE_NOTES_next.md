# Unreleased

- Reject unsupported RF spectra in the diffusive effective-field path instead of applying one frequency to all components. Inactive generators no longer set the averaging period; trajectory RF support is unchanged.
- Invalidate ponderomotive caches from their defining RF state, not spatial probes. Quiver amplitudes are positive for either charge sign.
- Advance the solver-behaviour version to 2 so older numerical results are not reported current after these corrections. The package version is unchanged.
