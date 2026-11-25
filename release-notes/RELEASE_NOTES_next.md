# Einzel v26.2.0 release notes

Draft. Append as things land; this file is renamed at release time, so a planned patch can
become a feature release without being renamed twice.

## New Features

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
  of a cell), and reaches `einzel run`, `einzel preview`, every figure and `einzel solve`. No
  shipped template or example puts a node on a face two of its electrodes share.
- **The same warning when a conductor meets a grounded face of the domain.** A grounded face is a
  conductor at zero volts, and a node on it that lies on another conductor's surface holds that
  conductor's potential or zero according to rounding: the solve does not see it, but the field
  within about a cell of the contact moves by a fifth of the applied potential, and a conductor
  lying outside the domain against the face is in the solve only by the coin. **Seventeen shipped
  models now carry it** - ring stacks run out to the outer wall, the einzel lens, the Paul trap,
  the Kingdon trap and the two mirror cross-sections - and their figures are marked qualified.
  Fifteen are measured harmless: every figure is identical either side of the coin. In the two
  mirrors, `planar-mirror-pair` and `astral-mirror`, the end cap is a plate with no thickness
  lying in the grounded edge and is in the solve only because two expressions agree exactly; the
  shipped arithmetic lands on the side with a cap.
- **`einzel solve` reports warnings**, in `--json` and on stderr. It reported residuals and node
  counts and nothing else.
- **`einzel render still`** draws a model's shaded 3D view to a PNG, headlessly - the same
  picture the interactive viewport shows, from the same composition and camera. Named views
  (`--view iso|side|top|front`), pixel sizes (`--width-px`, `--height-px`), `--see-through`.
  The PNG carries the engine version, the model's hash and every warning in its text chunks,
  and a validity violation hatches the bottom of the picture.
- **`einzel export` carries a solve's warnings** into each field file's header and into its
  `--json` output, and prints them on stderr.
- **Volume solves hold far less memory, and smooth faster.** Where a conductor's surface
  crosses between two mesh nodes, the solve records how far along and at what potential - a
  cut link. These were stored for all six arms of every node, 96 bytes a node, although about
  4 percent of arms are cut; they are now stored for the cut arms alone, 1.5 bytes a node and
  16 a cut arm. A volume solve's peak falls from about 240 bytes a node to 150 on the Galerkin
  multigrid hierarchy and from 142 to 51 on the rediscretized one, so `astral-3d` at a 1 mm
  cell needs 9.5 GiB where it needed 15.3. The smoother runs 1.2 to 1.5 times faster on the three
  shipped volume templates timed, and every solve gives the same potentials to the last bit.
  `einzel estimate` bills a quarter of a cut arm a node, above every shipped template; a
  geometry with more cut arms than that is under-billed by 16 bytes for each one past it.
- **A scale in every 3D picture.** Stills and the cross-platform viewport carry a ruler in
  whichever corner the instrument covers least: one arm along each model axis, all the same
  round length (`10 mm`, `500 µm`),
  foreshortened exactly as the instrument is, so it stays true in the angled view; in a
  straight-on view it is an L-shaped scale bar. `--no-scale` leaves it out of a still, whose
  `--json` result reports the arm length and the corner. A picture too small to hold the ruler
  gets none.
- **The cross-platform shell edits a model.** `einzel-shell` on Windows, Linux and macOS
  was a viewer: its parameter list could not be changed. Each free parameter is now a box -
  Enter or leaving the box applies it, Escape puts it back - and a derived one shows its
  expression and what that comes to. An edit is recorded in the session journal under your
  name and redraws the viewport; a value outside its declared bounds still applies, with the
  complaint shown in red under the tree and the last picture kept until the model validates
  again; **Undo last edit** reverses it. A new Journal tab lists what has happened, by whom,
  and every action as the `einzel` command that would repeat it, ready to copy. A watched run
  is recorded there too.
- **The cross-platform shell has every panel the Windows one has.** Results by accuracy
  class, the regime along the path, the sequence timeline, the project's models and their
  state, and the extensions with their licences are tabs beside the journal. **Run** and
  **Inspect** work in the background, so the window keeps answering during a long run. The
  viewport turns, moves and zooms with the mouse: drag to turn, right-drag or Shift-drag to
  move, the wheel to zoom toward the cursor, and double-click or **Fit** to fit it again. The
  named views still fit exactly.
- **There is one shell now, and it runs everywhere.** The Windows-only shell has been
  removed. Everything it did the cross-platform one does, and `einzel-shell` is published for
  Windows and Linux alike. If you were running the Windows build, the replacement opens the
  same models, writes the same journal and has views the old one did not - a scale ruler in
  the viewport, undo on the model tree, and panels that keep answering while a run is going.
- **Stop a watched run.** A watch is the one thing in the window measured in minutes, and
  until now the only ways out were to wait or to close the application. **Stop** ends it after
  the current step and **keeps the packet on screen**, because that is where the run had got
  to and is the reason you pressed it; the status line says the run is partial rather than the
  picture being taken away to say so. A full run and a regime walk cannot be stopped part
  way - their command objects take no cancellation - so the button is the watch's alone.
- **A legend under the viewport, saying what the colours mean.** Trajectories are coloured by
  energy and the field by potential, with the ends and their units written out. The potential
  scale is symmetric about earth rather than stretched across what happens to be in the
  picture, so an earthed electrode is always the neutral colour. Rows appear only where they
  mean something: a model that computes a density has no energy scale, and a geometry entirely
  at earth has no legend at all.
- **`einzel run --progress <seconds>`** reports where a long run has got to on the diagnostic
  stream, thirty seconds by default, and leaves `results/<name>.progress.json` behind. The
  checkpoint is removed when the run writes its answer, so finding one means the run did not
  finish. Reporting is asserted not to change the answer.
- **`einzel report`** renders a project's runs as one self-contained page, with `--json`
  carrying the same account. It reads a checkpoint, so an interrupted run is reported with the
  phases that completed rather than as a run that stored nothing.
- **Imported gas files and study files are recorded in run manifests.** `verify` detects
  changed, missing or retargeted inputs and no longer certifies legacy manifests that did not
  record them.
- **Collisions and direct or particle-in-cell space charge apply during the trajectory legs of
  mixed-mode sequences.** Conductor detection is preserved through time shifts, collision
  diagnostics propagate, and empty packets no longer reseed or crash the printer.
- **`--vtu` on a sequenced run writes the density** it ended with, where it ended in the
  diffusive description, with the run's caveats and their severities in the file's own header.
- **A volume solve's conductor mask is built by asking each electrode only about the nodes and
  links its bounding box reaches.** Every electrode used to be tested against every node and
  link, about half a billion closed-form entry tests per mask on `astral-3d`'s ion-foil solve
  (1.1 million nodes, 86 electrodes). In Release that mask takes 0.15 s where it took 10.3 s,
  and `einzel solve` on the template takes 11.0 s where it took 20.6 s, with every reported
  figure unchanged. The mask is bit-identical to the unculled one on every shipped volume
  template and the corpus's 3-D example, at the shipped mesh and at every coarse level; the
  test suite repeats that comparison on every change, except that `linear-ion-trap-3d` is
  compared at twice its shipped cell there, because its unculled reference alone took over
  two and a half minutes on the Windows CI runner.
- **Diffusion coefficient and face-operator buffers are reused**, separately per species. A
  513 x 65 rebuild allocates about 3.4 kB instead of 7.47 MB in a warmed probe; operator
  values are bit-identical. An allocation reduction, not a claimed end-to-end speedup.
- **A Windows installer.** Each release now carries `MacCossLab.Einzel-stable-Setup.exe`, built by
  Velopack as LabOps is: a per-user install with no administrator rights and the .NET runtime
  included, into `%LocalAppData%\MacCossLab.Einzel\current`, with Start menu and desktop shortcuts to
  the window and `einzel` added to your user PATH (removed again on uninstall, with every other PATH
  entry left as it was). It is not code-signed yet, so SmartScreen warns on first run (More info,
  Run anyway). An installed window checks GitHub once when it starts and, if a newer release exists,
  shows a bar offering **Download and restart**, **Download only**, **Skip this version** and **Not
  now**; nothing is downloaded until one is pressed, a restart is refused while a run is in progress,
  and there is no periodic re-check, so an agent running `einzel` sees one version throughout. The
  command line, the MCP server, the portable archives and a source build never check. See
  `docs/install.md`.
- **`einzel render surface`** draws the potential over a plane as a height - SIMION's
  potential-energy view - to a PNG with its color scale on the picture: conductors are flat
  plateaus at their potential, a trap's well is a valley. `--cycle-mean` shows a driven field's
  cycle average rather than one instant, `--height energy` the potential energy of the model's
  ion, and `--csv` writes the numbers. `--cycle-mean` is refused for a field with no drive to
  average over, the vertical scale is left off (and reported absent) in the top view and in a
  picture too small for its labels, and conductors of one name in two field elements are separate
  plateaus. The picture is a rotation of the model (the top view of `xy` has x right and y up, as
  a section does), a plane whose range is rounding noise is drawn flat and says so, and the CSV is
  reported beside the picture. Coordinate planes at an offset only, and not yet a view in the
  window. See `docs/rendering.md`.
- **A flag that takes a value and is given none is refused**, naming the flag, by `render
  surface`, `render still`, `probe` and `export`. `--at-us --cycle-mean` or `--csv --json` used to
  drop the flag and exit 0.
- **A switch no longer swallows the argument after it**, in every verb: `einzel run --json
  model.json` and `render surface --cycle-mean model.json` used to read the model as the switch's
  value.
- **Every verb refuses a malformed command line before doing anything**, naming what is wrong: a
  flag the verb does not read (`unknown flag --no-scales for render surface`), a flag that takes a
  value and was given none (`--out --json`), and an argument the verb does not
  take (`--no-trajectory out.svg`, `validate a.json b.json`). Under `--json` the refusal is JSON on
  stdout as well. These used to be accepted and ignored, exit 0. `--help` and `--json` behave
  exactly as before on every verb, and every flag a verb documents is unchanged. **What changes:**
  a flag a verb never read is now an error - `--dry-run` on `validate`, `project`, `estimate`,
  `solve`, `run`, `preview`, `test`, `verify`, `compare`, `schema`, `templates`, `examples` and
  `doctor`; `--project` on `init`, `new`, `validate`, `outline`, `estimate`, `solve`, `probe`,
  `preview`, `compare`, `schema`, `templates`, `examples` and `agents`; and any other flag a verb
  does not read. `docs/cli.md` lists each verb's flags. A bare `--progress` is still the default
  interval; every other flag that takes a value was read as though absent when given bare, and is
  now refused.
- **A flag's value is checked where it is typed, on every verb.** `render animation --fps 30fps`,
  `--width-mm wide` and `compare --ions many` were internal errors, exit 6; they are exit 1 naming
  the flag. A whole-number flag is digits alone within its range (`--width-px +64`, `--samples
  2.5` and `--equipotentials -3` are refused). A flag that names one of a few words refuses any
  other, where it used to fall back silently: `--format pfd` drew an SVG, `ext register --kind
  objectiv` registered an objective, `init --vcs hg` made no repository. `--height` takes
  `potential` or `energy` only. `ext test --input` given a file that is not JSON is exit 1, not
  6. Every such refusal is located at `/args/--flag` and is JSON on stdout under `--json`.
- **`render surface` draws each conductor at what it holds**, not at what the summed field reads
  inside it, and says so where the two disagree (`render.plateau-disagrees`); a long thin box is
  sampled across its width, `--samples NxM` sets both counts, `--out` and `--csv` may not be one
  file, and a negative or unplaceably late `--at-us` is refused (also by `probe` and `render
  section`).
- **`render surface` on a reflected solve stops a conductor at its image.** A conductor declared
  running past the mirror plane is metal only up to the plane and in its reflection; the part
  beyond was drawn as a plateau and `probe` called it inside-conductor. The instant on a driven
  field's picture is written finely enough to tell phases apart (`t=100.25 µs`, where both it and
  `t=100 µs` were captioned alike), and the scale no longer stands on the surface's edge in
  `--view front`.
- **`render surface` states the range its conductors hold.** `lowest`, `highest`, the height
  scale and the scale bar no longer follow interpolant overshoot, which changed with `--samples`
  (the shipped einzel lens read -25, -32 or -14.8 V for its 0 V floor); the raw extremes are
  `sampledLowest` and `sampledHighest`, the conductors' range `appliedLowest` and
  `appliedHighest`, and `render.interpolant-overshoot` says how far past and where. Earth is
  always labelled on the bar. `--relief` is at most 10 and is refused together with an explicit
  scale; a scale that makes the heights infinite is refused where it drew a picture.
- **A reflected solve is no longer doubled on its mirror plane.** A point exactly on the plane
  of a `reflectAboutX` solve read twice the potential and twice the field along the plane, in a
  run as in `probe`; either side of it was right. A flight is unchanged unless a step lands
  exactly on the plane.
- **`render surface` is drawn no further than its scale**: heights and colors are held to the
  range the conductors hold, as the bar already was; the CSV keeps the raw samples.
- **Under `--json` every refusal is JSON on stdout**, whichever layer raised it. `ext` and
  `agents` flags are checked per sub-verb (`ext list --kind y` is refused). A destination that is
  the model itself, or another destination, is refused; a failed write restores the file that
  was there.
- **`einzel run --progress ./models/r.json`** reads the path as the model, not the interval.
  `einzel run --dry-run` is refused (it was ignored, and the model ran), pointing at `einzel
  preview` and `einzel estimate`.
- **A bare `--progress` takes the next word as its interval only if the whole word is a number**:
  `einzel run --progress 2026-lens.json` runs that model at the default interval, `--progress 5`
  is still five seconds, and `einzel run m.json --progress often` is refused for the stray 'often'
  (write `--progress=often` to have the interval itself refused by name). `--progress 1e400` is
  refused as an interval rather than opened as a model, and a word read as a model after a bare
  `--progress` (`einzel run --progress 5s`) is named in the refusal. A negative `--progress` is
  refused (only `--progress 0` turns reporting off), and a surface label or caption never writes a
  nonzero value as 0, nor a narrow scale near a large value as "1.23e+3"; the labels on one scale
  share one notation. An empty argument (`einzel run ""`) is refused rather than exiting 6. An empty value after `=`
  (`--out=`) is refused at the flag rather than exiting 6; `einzel render --dry-run m.json` is held
  to the section's flags, so a typo there is refused rather than ignored; and an unknown `ext`,
  `agents` or `render` sub-verb is refused naming the known ones. Every flag error is located at `/args/--flag`,
  whichever verb raised it (`probe` and `render section` had their own pointers). The probe's
  `--csv` quotes a conductor name holding a comma or a quote. `einzel export` of several solved
  elements holds one element's solve at a time.
- **A reflected solve's metal is where its field is, on both sides of the mirror**: an electrode
  declared past the plane no longer stops ions in the image's half. A surface's bound on what the
  field can be is taken at each node.
- **Under `--json` every failure is a JSON document on stdout** - a missing model and an internal
  error included - and `--json=true` counts. A word that follows a bare `--progress` and is not a
  number is refused at `--progress` whenever there is a positional too many, whatever the order.
- **`--box-mm` whose corners are finite but whose width is not** (`-1e308,0,1e308,1`) is
  refused at the flag. Two CLI calls running in one process no longer share the "has written"
  flag that decides whether a refusal is written as JSON.
- **A reflected solve whose mirror plane lies inside its own box is symmetric about it**: the
  half and its image overlap there and are summed on both sides, where only the half answered
  below the plane. The image's conductors stop ions only where its field answers. An animation
  is written whole or not at all, and two conductors whose surface labels would read alike are
  two plateaus.
- **A point in a conductor's declared shape past the edge of its solve is `outside-domain`**, with
  a note naming the shape, even where another element covers it; it was `ok` there. This changes
  `probe` rows, the `probe.outside-domain` count of `export --composite`, and where `render surface`
  counts outside points.
- **A reflected solve is counted once on its mirror plane to the last bit**: one ulp below a
  power-of-two plane the image's reflected coordinate rounded back onto the plane and the sum
  doubled there. The plane and the declared side are the declared half's, the other side the
  image's, judged on the point itself.
- **A field made of parts lands on each part's surfaces separately.** A crossing is any part's
  discontinuity changing sign, so two surfaces at one place - two abutting solve domains, the two
  halves of a reflection meeting at their plane - are one landing, where combined into one sign
  they cancelled and the ion crossed a real jump with no landing. Every shipped model's flight is
  unchanged to the last digit. This holds through every field that wraps another - a driven or
  sequenced solve, a bounded analytic element - and a bounded element's own plane (a half-space
  inside its region) is now landed on even where a face of the region is nearer than it. Magnets
  are tracked the same way: the shared face of two abutting hard-edge magnets is landed on, where
  their combined sign cancelled. An element's own surface crossed in the same step that leaves its
  region is landed on before the region's face, whichever side of the surface the ion started on. Each
  half of a reflected solve declares its surfaces only on its own side of the plane, so an ion is no
  longer landed on the mirror of a surface the half it is in does not have. A sequence phase that
  moves an analytic element's region or a half-space's plane is refused at validation, naming the
  element and the phase: a phase may change what an element holds, not where it is.
- **A reflected solve's mirror plane is a surface the integrator lands on unless its edge is a
  mirror**: a grounded (Dirichlet) edge or face on the plane makes the plane a surface of its own,
  carried by the declared half, wherever an ion crosses it - over the box, where the field jumps,
  and beyond it, where it costs a landing and nothing more. Apart from the plane, a reflected
  solve declares exactly the mirror image of what its declared side declares. Decided by the plane edge's own condition
  alone, when the solve is built: a volume solve gets it on a grounded face, and so does a
  cross-section whatever its `boundaryIsDiscontinuous`, which speaks for its far edges. **A
  reflected cross-section with a grounded plane edge now lands on the plane, and its flights may
  move in their last digits.** A Neumann edge, or a box stopping short of the plane by more than a
  rounding, gets none; both shipped reflected templates have a Neumann edge, and their flights are
  unchanged. A reflected cross-section declaring its edges keeps both top edges as surfaces, which
  it lost at the rim before.
- **A cross-section both tilted and reflected runs** again: every command reading one exited 6 with
  an invalid cast while turning its conductors.
- **A CSV text cell beginning with `=`, `+`, `-`, `@`, a tab or a carriage return** - a conductor's
  name in `probe --csv` or `render surface --csv` - is written as `"'=..."`, so a spreadsheet shows
  it as text rather than running it, as OWASP's guidance on CSV injection has it. Numbers are never
  altered, and `--json` carries every name exactly. A warning running over several lines stays all
  comments in `probe --csv`, as it already did in `render surface --csv`.
- **The window draws its layers in the order a still does**: opaque, then depth-biased, then
  translucent, by one rule.
- **The note that a word after a bare `--progress` may have been its value** is given for a unit
  after a number in any case (`2.MS`, `5.MM`), and not for a word naming something on disk or a
  name with a unit-like extension (`trap.ps`, `fit.m`).
- **A destination reached through a linked folder is the file it is**: an input there is refused
  as that input, where only a link that was the file itself was followed before. A destination
  reached through a loop of links, or below a folder that cannot be read, is refused at its flag
  rather than ending the command as an internal error; a destination named like an input that
  cannot be followed is refused, since it may be that input; and a set of files whose link was
  pointed elsewhere while they were made is refused at the commit, with nothing placed.
- **Files are replaced in one rename**, written through a symbolic link, and a read-only
  destination is refused; every destination - including the render spec a figure was drawn from -
  is checked before the work, `--dry-run` included. A file no flag named is refused with its own
  path rather than at `--project` or `--out`.
- **Flags may come before a sub-verb** again: `einzel ext --json list`, `einzel agents --json
  tasks`; and a bare `--progress` before the model is the default interval again (`einzel run
  --progress models/reflectron.json`) rather than an interval called the model's path. A
  destination that is a symbolic link to the model, or to another destination, is refused as that
  file. `render surface --cycle-mean` no longer refuses an `--at-us` too late to place in the
  drive cycle, since no instant of the drive is drawn.
- **`einzel schema --units`** lists every unit symbol with its dimension and SI factor - the
  command an unknown unit's error has always named.
- **A file that cannot be written is exit 1 at its flag**, not an internal error: `--out` or
  `--csv` naming a directory, on `render surface`, `render still`, `render section`, `render
  animation`, `probe` and `report`. `render surface` writes its picture and its table together or
  neither.
- **A gallery page** reproducing an einzel lens's published voltage and pressure series
  (`docs/gallery-einzel-lens.md`), with every choice the source leaves open listed.
- **The `paul-trap` template has endcap holes and a stretch.** Three new parameters, all zero by
  default, and at zero the template solves to the field it always had, to the bit:
  `endcapEntranceHoleFraction` and `endcapExitHoleFraction` cut a bore on the axis of each endcap,
  as a fraction (at most 0.9) of the endcap's radius, and `endcapStretchFraction` moves both
  endcaps outward by a fraction of z0. An ion ejected through the exit hole arrives at the
  detector; one that meets the endcap is itemised on `endcapExit`.
- **Every way out of the `paul-trap` solve is itemised.** Two grounded stops, `entranceStop` and
  `outerStop`, are always present and change no field value. An ion that leaves at the entrance
  end or through the outer radius is now reported as striking one of them, where it could coast
  out of the domain and be counted as confined.
- **Know before scanning a hole size**: the bore is exactly the hole declared, and a bore whose
  radius is a multiple of the radial mesh spacing (0.125 mm as shipped) sits on a step in the
  solved field - 0.2 V of 400 at 0.5 mm - with no warning. Keep hole radii off those multiples.
- **With holes, the ejection edge is a strip and not a point.** Two holes of 0.175 of the endcap
  radius move it out to q_eff 0.917-0.920 (the ideal trap: 0.906); inside that strip held and lost
  amplitudes interleave, and where a search lands moves with the integrator tolerance. Hold 800
  cycles or more and scan the edge; do not bisect it. Measurements are in
  `docs/device-templates.md`.
- **Measured on a cloud of ions, the stretch brings the holed trap's ejection edge a quarter of
  the way back.** With 200 ions released in a 0.13 mm ball at random RF phase, half are lost at
  q_eff 0.9058 in the ideal trap, 0.9176 with two holes and 0.9146 with two holes and a 0.107
  stretch: the stretch recovers 0.0030 (0.0027 to 0.0037 over tolerance, cloud, hold and mesh)
  of the 0.0120 the holes cost. Twice that stretch recovers under half. A holed edge quoted
  finer than 0.0006 belongs to the hold: it falls by that much between 800 and 2000 cycles.
- **A published three-program comparison is a test.** The single-ion case of Forbes et al. 1999
  (SIMION, ITSIM, ISIS) runs on `paul-trap`: the secular frequencies agree with all three
  programs; the tabulated excursions do not, and do not agree with the ideal field either, which
  is recorded in `docs/literature-targets.md`. This is against published numbers, not a SIMION
  run.

## Bug Fixes

- **Two unnamed electrodes in a driven or sequenced solve were solved at the same potential.**
  Every unnamed electrode was called `electrode`, and a driven or sequenced geometry is solved
  through channels keyed by name, so the later of two set both: a deflector of two unnamed
  plates at +2 V and -2 V was switched off, on a run that validated, converged and exited 0.
  The same happened to two electrodes given one name. An unnamed electrode is now called
  `electrode-N` after its place in the list, and **two electrodes in one solve sharing a name
  are refused** at validation, since a lost ion is reported by its electrode's name as well.
  Two field elements may still share names, as the Astral's mirror solves do for the same
  metal. A static geometry's field was right and is unchanged; only its unnamed electrodes'
  names in reports change. No shipped template or example is affected.
- **Two taps of one electrode on one generator kept only the later.** At one phase, or at two
  phases of a sinusoid, they reach the same supply, and 100 V and 50 V flew exactly as 50 V
  alone. They now add.
- **`astral-3d`'s flight time depended on rounding.** Three faces shared by foil slices at
  different voltages lay exactly on planes of mesh nodes, so which voltage the stencil arms in
  those planes took was decided by the last bit of arithmetic - worth 0.27 percent. The foil
  mesh now sits a tenth of a cell off every such face (`meshShiftZ`). The shipped flight time is
  784.90 us (was 786.44) and the drift reversal 336.06 mm (was 336.15). Moving the mesh also
  showed that the foil's coarse mesh carries a 0.83 percent spread by placement alone; the
  mesh-converged flight time is about 775 us, a percent below the published arithmetic rather
  than the 0.4 previously quoted.
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
- **`einzel estimate`'s memory figure was a fifth of the truth.** It billed every solve 48
  bytes a node, where a volume solve held about 240 on the Galerkin multigrid hierarchy, 142 on
  the rediscretized one and 143 on a plane, measured against real builds: `astral-3d` at a 1 mm
  cell was quoted 3.1 GiB and needed 15.3. The volume figures are now about 150 and 51, and
  that `astral-3d` 9.5 GiB, since the cut links are stored for the cut arms alone (above). The
  estimate now counts basis channels, charges a
  diffusive mixture once per species, adds what earlier elements keep, says what its figure
  counted, and says so when the peak is more than half of what the process may use on this
  machine - whether or not anything in the model is solved.
- **`einzel estimate` billed a parallel study for one evaluation's memory.** A scan or tolerance
  sweep runs up to `maxParallelism` evaluations at once - one per processor when the study does
  not say - and each solves its own field, so a scan running sixteen points at once needs sixteen
  times what was quoted. The estimate now multiplies by how many are in flight, reports that
  count (`inFlight`) and the per-evaluation figure beside the total, and says when the total will
  not fit this machine. An optimization or a boundary search runs one point at a time and is
  unchanged. Each evaluation is billed at the dearest end of the study's range rather than at the
  model's declared values, since a study that varies the mesh varies what an evaluation holds.
- **Mean-field space charge was billed no memory.** A diffusive run with
  `"spaceCharge": "meanField"` holds about 62 bytes a density node more than one without - its
  potential, the density it was solved for, a mask, and the Poisson solve it refreshes during the
  run - once however many species there are. The estimate now charges it.
- **A driven solve held finished channels' geometry** while solving the next: 353.7 bytes a
  node for two volume channels where 248.7 is what they need, and the driven plane build kept
  its first channel's mask, 73 bytes a node, through every later channel.
- **Building a volume solve's conductor mask allocated per stencil arm** on revolved and
  extruded electrodes: 5.5 kB a node on `c-trap`, 260 bytes on `linear-ion-trap-3d`, against
  the 105 the mask keeps. It now allocates only what it keeps, and the masks are bit-identical.
  `c-trap`'s build at 0.5 mm takes 14 s where it took 16; `linear-ion-trap-3d`'s is unchanged.
- **Building a cross-section's conductor mask allocated per stencil arm**: an enumerator for
  each of a free node's four arms, 101 bytes a node of garbage, 27 MB on a quarter-million-node
  plane. It allocates what it keeps now; the masks are unchanged. A Debug build allocated a
  further 18 bytes a node while zeroing each coarse mask's cut potentials, and no longer does.
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
- **Extensions could not start on a Windows machine whose Python is 3.10 or older.** The
  sandbox started the interpreter with no environment at all, and CPython up to 3.10 reaches
  the random source it seeds hash randomisation from through CryptoAPI, which fails without
  `SYSTEMROOT`: every extension died with "Fatal Python error: _Py_HashRandomization_Init"
  before its first line, and `einzel ext test` failed on every project. The child now keeps
  `SYSTEMROOT` on Windows - the path of the Windows directory, and nothing else - and nothing
  anywhere else. CI never saw it because the Windows runner's Python is 3.12, which uses
  `BCryptGenRandom` and starts with an empty environment.
- **The shell's numbers no longer follow the desktop's locale.** On a computer set to a
  language that writes a decimal comma, parameter bounds read `0,1 to 20 kV` beside a box
  that accepts `0.1` and refuses `0,1`, and so would warnings. The shell now writes every
  number with a point, as model files are.
- **A sequence whose phases all name a transport mode the model does not** was flown in the
  model's, with the timeline ignored and exit 0. The fork now asks for the set of modes the
  run uses rather than whether two adjacent phases differ.
- **A sequenced run writes a result document beside its manifest.** Three of the four run
  paths already did; that one stored provenance and no answer, and it is the path every
  mobility study takes. Its manifest recorded absolute artifact paths and now records
  portable ones.
- **Arrival figures for a continuous population require the collected ions to reach a
  millionth of those launched**, and are absent with `sequence.nothing-eluted` below that.
  Scharfetter-Gummel's flux across a collecting face behind a barrier is the Boltzmann factor
  of that barrier, so a held packet emitted values hundreds of orders below one ion from its
  first step - and a mean and a spread were being reported over 7.74e-245 of them.
- **Ponderomotive caches are invalidated from their defining RF state**, not spatial probes,
  and quiver amplitudes are positive for either charge sign.

## Breaking Changes

- **A diffusive detector must coincide with a supported grid face, and one that does not is
  refused.** Its position was previously ignored - the collecting face was chosen from its
  normal alone - so a document could name a plane the solver did not use. **This is a
  breaking change for existing diffusive models**, which are refused at run time rather than
  silently collected at a different plane. Align the detector with the corresponding
  `densityGrid` boundary, or move that boundary to the detector. Three corpus examples were
  corrected to the faces the solver already used, and their numbers are unchanged. **Three
  shipped templates instead had their grid moved to the declared detector**, so their tracked
  region and their figures change: `tims-analyzer`, `tims-front-end` and `tims-tandem`. On the
  front end, `maxX` moves from `tunnelLength + exitLength` (15 mm) to `+ detectorPad` (12 mm),
  and the elution peak moves from 20.9878 V to 21.1843 V with the resolving power going 7.481
  to 7.674. Re-measure any figure taken from those templates.
- **Unsupported RF spectra in the diffusive effective-field path are rejected** instead of
  applying one frequency to all components. Inactive generators no longer set the averaging
  period; trajectory RF support is unchanged.
- **`einzel estimate` states less memory for a diffusive run: 248 bytes a density node and
  species where it stated 263**, and 4 more, once, for the map of which conductor owns each
  node where a solved element has electrodes. 248 is exactly the run's arrays; 263 was a
  sampled figure that had read garbage as held. A diffusive estimate is 5.7 percent lower
  without conductors and 4.2 percent lower with them.
- **`einzel estimate --snapshots <n>`** bills the instants a diffusive run is to record its
  density at - 8 bytes a density node each, kept to the end. A run records none; a render at an
  instant asks for one and an animation for one a frame. It is refused for a model that steps
  no density in any phase, for a mixture, whose run records none, and when given no number,
  rather than accepted and billed for copies nothing makes; under `--json` the refusal is JSON.
- **A diffusive hold beside a strong extraction is no longer billed at the extraction's pace**:
  the drift limit is read at the diffusive phases' own instants, by the run's own shift. A
  trajectory phase whose pilot fails is never billed nothing, and a study over a model none of
  whose pilots could be flown is gated rather than billed without its flights
  (`trajectoryMeasured: false`). `--snapshots 00` is accepted for a study, as `0` is, and a
  refusal of the flag is located at `/args/--snapshots`. A study over a sequenced model bills
  the flights one evaluation makes: the sequence's cloud (one packet under space charge) for a
  figure read off the sequenced run, the figure's own ions otherwise. A pilot that fails through
  the coarsened field is flown again at the declared resolution before the gate refuses.
- **A sequenced run is estimated phase by phase**: each trajectory phase by a pilot flown
  through its own field over its own duration, diffusive phases by density steps over theirs, and the basis says both. A
  trajectory model entering a diffusive phase was billed nothing for its density, and a
  diffusive model leaving its density was stepped through its trajectory phases.
- **A mixture's self-field no longer makes a grid of garbage at every step**: its summed charge
  is refilled in place.
- **A density that is not a number is refused where it is handed to a trajectory phase**, naming
  the cell and the phase that produced it (`CONVERGENCE_FAILED` at `/sequence/<index>`), instead of being drawn from: every ion was
  placed in an arbitrary cell with nothing said.
- **A diffusive run no longer allocates in its stepping loop.** Reading the face offsets built
  a new array four times a node a step - 224 bytes of garbage a node a step, a generation
  collected every step on a large grid. No number changes.
- **A trajectory leg that ends where the field switches runs to the switch.** A sequenced run
  whose trajectory phase was followed by one that changes the field, with ions still in flight,
  stopped with a step-size underflow about an attosecond before the switch and refused the
  run. The step that lands on a switch now samples the phase it belongs to at its end, rather
  than the one starting; a sequenced flight that lands on a switch moves in its last digits
  (the analytic sequenced example now agrees with its closed form to 3e-16, from 4.6e-13), and
  every flight that meets no switch is bit-identical.
- **A phase the integrator cannot resolve is refused as a model error** at its duration
  (`VALUE_OUT_OF_BOUNDS`, exit 1): one that rounds to nothing against the time before it, which
  passed validation and ended in an internal error, and one shorter than the smallest step.
- **A flight cut to its maximum flight time ends there**, where the sum of its steps could leave
  it an ulp short and end it as a step-size underflow - whether the steps, an analytic drift or a
  step ending on a switch and the ceiling together left it there. A switch closer than the
  smallest step is reached rather than ending the flight.
- **A space-charge packet lands on a sequence switch** as a single ion does, where it took
  dozens of refused steps at each switch and crossed it on the smallest step.
- **A rectangular drive whose edge lies on a sequence switch is landed on** without a run of
  refused steps, and a field held at an operating point is one state on both sides of every
  boundary.
- **Validation reports every mistake in a sequence's phases in one round**, where a phase after
  the first mistake had its set, ramp and mode left unchecked.
- **A detector, electrode or field discontinuity reached exactly at a sequence switch** is met
  by the step that reached it, in the phase ending there, where it used to be re-integrated with
  the next phase's field at its end.
- **A phase shorter than the trajectory integrator's smallest step is no longer refused**: it
  runs. Only a phase that lasts no time at all is refused, and one mistake met by several phases
  is reported once.
- **Two sequence switches closer together than a leg's clock can tell apart** are no longer lost:
  the leg holds the state before the first and after the last, and says so when a flight reaches
  that instant.
- **A sequenced analytic element bounded to a region lands on its switches**, where a flight used
  to step across them.
- **`einzel estimate` takes `--project` for a study**, refuses it for a model, refuses a study
  whose extension the project does not hold, and bills a figure that does not walk the sequence
  its solves and its flights through the whole instrument.
- **`einzel estimate` bills an extension objective as the sequenced run** when its manifest
  declares a figure that reads it.
- **A phase duration that converts to infinity is refused as not finite**, and a phase refused
  for another reason still counts toward the next phase's start.
- **`einzel estimate` does not solve a dear model at its declared resolution to cost a pilot**,
  and its pilot through a sequenced phase flies the leg the run flies.
- **The solver-behaviour version is advanced to 3**, so older numerical results are not
  reported current after these corrections. It also covers the landing of a step on a
  composite field's member discontinuities, reworked on main at version 2 (the render-surface
  work); main did not advance it, so 3 is the first number under which both are in. The
  package version is unchanged.

## Known limits

Carried forward from v26.1.0 until they change:

- **The shell is partial**: nine of the eleven views in the specification exist. The
  figure composer and scrubbing on the animation timeline are not built. There is one
  shell now - the Windows-only one has been removed - and it runs where the engine runs.
- **A still or the viewport of a model in gas shows vacuum rays.** `render still` and the window
  fly the drawn bundle with no collisions, whatever gas the model declares; `einzel run` on the
  same model collides. The numbers are right and the picture is not.
- **A still cannot check the window's own GPU path**, and it does not draw the name of a
  validity violation as text: the codes are in the file's text chunks and on stderr.
- **No installer and no updater.** The assets are portable: unpack and run.
- **The extension sandbox states what it does not enforce** — no network confinement, no
  filesystem confinement, no memory ceiling — on every listing and on every result.
