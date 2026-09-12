# Unreleased

- Reuse diffusion coefficient and face-operator buffers, separately per species. A 513 x 65 rebuild allocates about 3.4 kB instead of 7.47 MB in a warmed probe; operator values remain bit-identical. This is an allocation reduction, not a claimed end-to-end speedup.

- Record imported gas files and study files in run manifests. `verify` detects changed, missing or retargeted inputs and no longer certifies legacy manifests that did not record them.

- Apply collisions and direct/PIC space charge during trajectory legs of mixed-mode sequences. Preserve conductor detection through time shifts, propagate collision diagnostics, and handle empty packets without reseeding or crashing the printer.
- Refuse diffusive detectors that do not coincide with a supported grid face. A diffusive detector's position was previously ignored - the collecting face was chosen from its normal alone - so a document could name a plane the solver did not use.

  **This is a breaking change for existing diffusive models**, which are refused at run time rather than silently collected at a different plane. Align the detector with the corresponding `densityGrid` boundary, or move that boundary to the detector.

  Three corpus examples were corrected to the faces the solver already used, and their numbers are unchanged. **Three shipped templates instead had their grid moved to the declared detector**, so their tracked region and their figures do change: `tims-analyzer`, `tims-front-end` and `tims-tandem`. On the front end, `maxX` moves from `tunnelLength + exitLength` (15 mm) to `+ detectorPad` (12 mm), and the elution peak moves from 20.9878 V to 21.1843 V with the resolving power going 7.481 to 7.674. Re-measure any figure taken from those templates.

- Reject unsupported RF spectra in the diffusive effective-field path instead of applying one frequency to all components. Inactive generators no longer set the averaging period; trajectory RF support is unchanged.
- Invalidate ponderomotive caches from their defining RF state, not spatial probes. Quiver amplitudes are positive for either charge sign.
- Advance the solver-behaviour version to 2 so older numerical results are not reported current after these corrections. The package version is unchanged.

Also unreleased, and not previously listed here - this file was added by the branch above,
so the work merged before it had no entries:

- `einzel run --progress <seconds>` reports where a long run has got to on the diagnostic stream, thirty seconds by default, and leaves `results/<name>.progress.json` behind. The checkpoint is removed when the run writes its answer, so finding one means the run did not finish. Reporting is asserted not to change the answer.

- `einzel report` renders a project's runs as one self-contained page, with `--json` carrying the same account. It reads a checkpoint, so an interrupted run is reported with the phases that completed rather than as a run that stored nothing.

- A sequenced run writes a result document beside its manifest. Three of the four run paths already did; that one stored provenance and no answer, and it is the path every mobility study takes. Its manifest also recorded absolute artifact paths and now records portable ones.

- `--vtu` on a sequenced run writes the density it ended with, where it ended in the diffusive description, with the run's caveats and their severities in the file's own header.

- A sequence whose phases all name a transport mode the model does not was flown in the model's, with the timeline ignored and exit 0. The fork now asks for the set of modes the run uses rather than whether two adjacent phases differ.

- Arrival figures for a continuous population require the collected ions to reach a millionth of those launched, and are absent with `sequence.nothing-eluted` below that. Scharfetter-Gummel's flux across a collecting face behind a barrier is the Boltzmann factor of that barrier, so a held packet emits a stream of values hundreds of orders below one ion from its first step - and a mean and a spread were being reported over 7.74e-245 of them.
