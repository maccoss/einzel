# Lessons

Bugs that presented as physics and turned out to be arithmetic, and mistakes in
how things were measured. Each is recorded because it cost real time to find and
because none of them announced itself — every one produced a plausible number.

Ordered roughly by how much they would cost someone who hit them again.

## Narrowing a guard means asking what every caller will then be told

`SessionJournal` refused any edit that did not validate. Section 16's live
validation required narrowing that to refusing only what does not *parse*, which was
right - and I checked the window, saw it showed validity through its own refresh, and
stopped.

The MCP path had no equivalent. `model_edit` went from **refusing** an invalid edit
loudly to **accepting** it and returning sequence, author, description and journal
with nothing saying the model no longer validated. An agent tuning a parameter past
its bound now got the same response shape as a good edit.

Strictly worse than before the change, and produced by a change that was correct in
its own terms. The guard had been doing two jobs - preventing an invalid state, and
*telling the caller* about one - and only the first was the thing being removed.

**The rule: when a guard is narrowed, enumerate its callers and ask what each will be
told afterwards.** A guard that refuses is also, incidentally, a guard that informs;
remove the refusal and the information goes with it unless something replaces it.

This is the sixth time evidence about a computation's own quality has been dropped at
a seam here - after `FieldAssembly.Build` discarding its `SolveReport`, the sweep
evaluator discarding warnings, `CollisionSampler`'s `BoundExceeded` and
`SampledOutsideFlow`, `SampledOutsideDensity` declared and never read, and
`DriveAmplitude` becoming a summary. The others were all *omissions*. This one was a
**removal**, which is why the existing habit - "make the shortest spelling the safe
one" - did not catch it: the shortest spelling was already safe, and I made it
shorter.

**A green suite is what let it through.** Nothing failed, because nothing had ever
asserted what an MCP client is told about an edit's validity - there was no such
thing to assert until the guard moved. The test that exists now would have failed the
moment the guard changed.

## A validity check stricter than anything the spec asked for

`SessionJournal` refused any edit that did not validate. The argument was written down
at the time and reads well: in a shared session an unrunnable model is not one
party's problem, because the other party's next action is against whatever is on
disk.

Section 16 contradicted it. **Live validation needs an invalid state to be
reachable.** A person typing 500 into a parameter bounded at 50 has to see the tree
standing with the complaint against it - being *prevented from typing* makes the
editor most useless at the moment it is most needed. And refusing every invalid
document forbids any edit **sequence** that passes through one: widening a bound and
then setting a value beyond the old bound works in one order and is refused in the
other, for no reason a person could infer from anything.

The platform's own rule settles it, and it was already written down four times over:
**taint, never block.** A preview result, a decimated figure, a defective engine
version, a coarse boundary search - all keep working and carry a non-suppressible
mark. The platform never stops you working; it refuses to let a result look cleaner
than it is.

`Check` now refuses only a document that does not **parse** - there is nothing there
to be wrong, the next party cannot read it, and no edit through the journal could
have produced it. Validity is reported through `SessionJournal.Validate()` instead.

**The general shape**: this was a guard invented while building one feature, justified
by a real argument, that turned out to be stricter than any requirement. It cost
nothing until a later requirement needed the state it forbade - and then it was found
by a *test failing for the right reason*, which is the cheap way to find one.
Guards written from first principles rather than from a requirement are worth
re-reading when a new requirement arrives, because the argument that justified them
is usually still true and still outweighed.

## A confinement test on a geometry that cannot confine

Checking that a driven geometry in a diffusive phase gets the *cycle-averaged* field
rather than a snapshot of the RF needs a geometry where the two differ. The first
version used two parallel plates driven in phase, released a packet 1.5 mm off axis,
and asserted the packet ended closer to the axis than it started.

    drive on   1.1939 mm
    drive off  1.1954 mm

It passed. The threshold was "less than where it began", and 1.1939 is less than
1.2, so it passed on a **0.1 per cent** difference that has nothing to do with the
claim.

The physics says why, and says it before the run: **two plates give a nearly uniform
field between them, and the ponderomotive force goes as the gradient of E squared.**
No gradient, no well. The test could not have discriminated at any amplitude,
because the geometry has nothing for the effect to act on.

Four rods, pairs in antiphase - a real quadrupole - gives:

    drive on   0.2341 mm
    drive off  1.5000 mm

**Two rules.** Before writing a test that an effect is present, check the geometry
can produce it - the closed form for the effect usually says so in one line. And a
threshold set just past the starting value is not a measurement: the drive-off
control is what turns "it moved a bit" into "it moved an order more than nothing
does", and it should have been there from the start rather than added after the
first version passed.

### And the control ran the driven case twice

The drive-off run was built by replacing `"value": 300` in the model - after the
model had been changed to 400 V. The replacement matched nothing, so both runs were
the driven one and their centroids were identical. **Caught only because the test
asserted the two differed**, which is the same `Assert.NotEqual` guard this project
has now needed four times for string surgery in a test. The amplitude is a
placeholder now, so there is no literal to fall out of step with.

## A fix written as a list of known modes learns each new mode the hard way

Wiring a third kind of run to the CLI walked straight into two defects this project
had already found and fixed once each.

**The exit code.** A successful sequenced run exited 4, `ConvergenceFailure`. The
mapping reads:

    run.Outcome is "StopConditionMet" or "DensityEvolved" ? Success : ConvergenceFailure

`DensityEvolved` is in that list because a working *diffusive* run once reported
itself as a failure - the logic knew only `StopConditionMet`. The fix was to add a
string, so the next mode added a third.

**The printer.** `flight time NaN +/- NaN`, `energy drift NaN`, `steps 0`. Those
lines were made *absent* for a diffusive run - a density has no flight time, and a
reader cannot tell a missing measurement from a failed one when both print the same
way - and that fix was gated on `run.Diffusion is null`. A sequenced run has that
null too.

**Both are the same mistake**: the fix was written as a list of the modes known at
the time rather than as the question actually being asked. "Did this run finish what
it was asked to do" and "is there a flight time to print" are properties of the
result; "which mode was it" is a proxy that stops being equivalent the moment a
third mode exists.

The list is now three long. If a fourth kind of run is added, the thing to change is
the question rather than the list - and the comment in `Program.cs` now says so,
because the next person to add a mode will find the list before they find this page.

### And a measurement mistake of my own, in the test for it

The test asserted the conversion warnings appeared in `Stdout`. They appear on
`Stderr`, because CLI-2 puts results on stdout and diagnostics on stderr. It passed
my manual check beforehand only because I had run the command with `2>&1` -
**merging the streams to look at the output destroys the very distinction the
contract is about.**

## Four helpers already existed, and writing them again got two of them wrong

`SequencedRun` needed a density grid, a seed, absorbing cells, domain edges and a
mobility. `DiffusionRun` had all five. I wrote four of them again, and two came out
different:

- the grid, built with `new Grid2D(...)` where `GridFor` uses `Grid2D.OverBox`,
  which **rounds each axis up to a power of two** - so one model got two different
  grids depending on which path ran it;
- the mobility, which ignored `Derived` - so a mobility the document derived from a
  cross section came back as the *stored* zero-field value rather than the one
  re-derived against the gas actually resolved for the run.

A third was not wrong so much as absent: the diffusive leg passed **no absorbers**,
so electrodes did not absorb during a diffusive phase. That is precisely the defect
that once made every diffusive transmission an upper bound with nothing saying so,
reintroduced locally a year later by someone writing the call from memory.

None of the three would have failed a test of the new code. Each produces a
plausible answer that simply differs from what `einzel run` gives for the same
model - which is the same shape as `run` and `test` disagreeing by 1.3e-10 in energy
drift, and has the same fix: **collapse to one implementation.**

**What makes this worth writing down is how it was found**, which was not by a
failing test. It was by asking what the CLI would have to report and noticing the
answer would have to come from somewhere - and the somewhere already existed. A
duplicate is invisible from inside the code that contains it.

## A stage moved a global parameter and only its own element followed

The sequencer's documented rationale is that a stage sets a *parameter* rather than
electrode settings, because "potentials are already expressions over parameters, so
setting one moves everything that depends on it at once - including the derived
parameters". That is the argument for the whole design, and across elements it is
false.

`CompileStages` re-resolves the **whole model parameter surface** with the stage's
overrides, and then re-expands **only that solve's** electrodes against it. So a
model with two elements, an electrode in each written as the same expression
`"potential": "volts"`, and stages on the first:

    A base potential: 300      B base potential: 300
    A during push:    900      B during push:    300

Two electrodes with identical expressions holding different voltages, on a model
that **validated cleanly**, with no diagnostic anywhere.

Found while scoping SEQ-1, not by a failing test - the question was where a
transport mode could live, and the answer turned out to be that the timeline is
already in the wrong place. **A mode is a property of the run, not of one electrode
assembly**, so a per-element stage cannot carry one: two elements would name
different modes for the same instant, and there is no superposition of transport
modes the way there is of fields.

**Refused first, then fixed.** The refusal - a sequenced model may have one field
element - was the honest state while the two coherent readings were open. The
right one is the documented one: the timeline is the instrument's, and every
element recompiles against each phase.

`Timeline` now resolves the phases **once for the model**, before any element is
compiled, and hands the same parameter surfaces to all of them. That also fixes a
second thing the per-element version got wrong: a malformed stage used to be
reported once per field element, turning one typo into a wall of identical
complaints.

### And the first fix reached only half the elements

A code review of that change found the same defect still live one layer along.
`CompileField`'s analytic branches - `fieldFree`, `uniform`, `halfSpaceUniform` -
compile from the base parameter surface and never looked at the timeline at all,
because a `CompiledField` for those kinds has nowhere to put a phase. So a model
whose sequence set a parameter used by a `halfSpaceUniform` cap potential had the
solved elements follow and the analytic one frozen at baseline, validating
cleanly. A model whose *only* elements are analytic compiled a full timeline that
nothing consumed - the sequence was a silent no-op.

**The comment that hid it is the part worth keeping.** `Restage` was a closure
"because only the solve branch needs it, and threading the declared parameters
through every field kind to reach it would put the sequencer in the signature of
things that have nothing to do with it". That argument reads as sound and is
exactly backwards: the analytic kinds have everything to do with the sequencer,
because a phase gives them different numbers. A rationale written for one version
of the code kept the next version from noticing what it had missed.

Fixed with `SequencedField`, a generic switch, rather than a per-phase branch
inside each analytic field. A special case layered on shared infrastructure is
the shape a fix takes when it is not deep enough, and there would be one more of
them every time a field kind is added.

Two refusals remain, and both are about a document saying two things at once. Two
elements each declaring stages is two timelines over one instrument. And declaring
both the model's `sequence` and an element's `stages` is refused rather than
merged - the same argument that refuses a geometry declaring both `drive` and
`drives`.

**Latent is not the same as harmless.** No shipped model had two field elements,
so nothing existing was wrong - but a two-element sequenced model is a perfectly
ordinary thing to write, and the wrong answer it gave was a plausible one.

## A harness that lies in the direction of "the thing under test is broken"

The MCP server, driven by hand - a file of JSON-RPC piped into it - produced
**nothing at all** on stdout. Not a malformed reply, not an error: silence. That
reads unambiguously as a server that does not work, and the next half hour went
into the server.

With a logger attached the SDK said both requests were handled and both responses
were sent. What happens is that a file on stdin hits EOF immediately, the transport
tears down, and the outbound writes are dropped on the way out. A real client holds
stdin open for the life of the session and never sees it. The harness was the
artefact.

**When a harness and the thing under test disagree, establish which one is the
artefact before changing either.** The trap here is that the harness was simpler,
and a simpler thing is easy to assume is the trustworthy one - but simpler meant it
was missing the one property (a stdin that stays open) the thing under test depends
on. What settled it in a minute was asking the SDK to narrate, rather than reasoning
about which side was wrong.

A second, smaller one from the same afternoon: `Environment.ProcessPath` under
`dotnet test` is the **test host**, which is itself an apphost, so passing it a dll
to run fails with "Failed to run as a self-contained app". Launch the apphost the
build already produces.

## The measurement that measured the wrong thing

The first comparison of bicubic against bilinear interpolation used a **solved**
field as the reference. On a coarse grid the solve carries its own O(h²)
discretisation error, and that error is larger than the interpolation error it is
supposed to be a backdrop for. The result: bicubic appeared **60× worse** than
bilinear, which is the opposite of the truth.

Sampling the *exact* potential onto the grid nodes isolates the interpolant, and
the ordering inverts to a 29–146× advantage the other way.

The general form: when measuring component A, the reference must not contain a
larger error from component B. Ask what else is in the number before believing
it.

## Fixing the wrong thing about the geometry

The first multi-reflection model stopped the ion at a detector a set distance
along the drift. That makes the arrival time equal *distance ÷ drift velocity*,
which depends on energy alone and not at all on the mirrors — so no energy
focusing is possible by construction, whatever the optics do.

The focusing fit named it unmistakably: c₁ = −0.500, c₂ = 0.3756, c₃ = −0.3133
is the Taylor series of 1/√(1+δ), which is free flight.

Real multi-reflection analyzers fix the **oscillation count** — converging mirrors
bring every ion back after the same number of passes — and the flight time is
that count times the oscillation period, which is where the four-penetration-depth
condition does its work.

The lesson is that a physically-plausible-looking model can be structurally
incapable of showing the effect being studied. Fitting coefficients and reading
them against a known series caught it immediately; a resolving-power number alone
would only have looked disappointing.

## A near-zero field defeats an adaptive controller

Launch an ion in the field-free middle of a mirror pair. Local acceleration is
almost nothing, so the step-size heuristic proposes an enormous step; the
embedded error estimator agrees it was accurate — **correctly**, for a straight
line — and the ion flies 39 metres through both mirrors without ever sampling
them.

The step was not inaccurate. It was uninformed. A gridded field carries no
information below its node spacing, so `IElectrostaticField.ResolutionLength` now
bounds the step and the controller cannot outrun the data.

An error estimator only measures the error of the model it was given. It cannot
tell you the model was never consulted.

## Declaring a discontinuity that is not there

`SolvedField2D` originally marked its whole domain boundary as a field jump.
Where a solve ends in a decayed field there is no jump — and two such phantom
surfaces a few microns apart, which is exactly what two abutting solve domains
produce, defeat the superposition's sign-product tracking: a step crossing both is
treated as crossing neither.

Cost: **2.6e-4 of an ion's energy**, four orders above the ACC-4 budget. It
presented as an intermittent transmission loss that was *non-monotonic in energy*
— −5% fine, −3% lost, +3% fine, +5% lost — which is the signature of step
placement rather than optics.

Declaring structure that is not there is not conservative. It is wrong in a
direction that also disables the machinery meant to handle the real thing.

## Clamping a stencil at a grid boundary

A 4×4 interpolation stencil reaches one node beyond the grid. Repeating the edge
node — the obvious choice — makes the interpolant **non-linear in the boundary
cell even when the underlying field is exactly linear**.

An ion enters and leaves a mirror through that cell twice per reflection. A
clamped stencil put **7.5 ppm** into the flight time of a mirror whose exact
solution is a pure ramp: over the entire ACC-1 budget, caused by nothing but the
corner case. Linear extrapolation of the ghost node took it to 1.9e-10, a factor
of 38,000.

Boundary cases in an interpolant are not edge cases when the geometry puts the
interesting physics at the boundary.

## Coarsening past the point where the geometry exists

Multigrid assumes coarsening preserves the problem. An interior electrode — a
rod, an aperture — occupies a fixed physical size, so each coarsening halves how
many nodes represent it, and past a few levels it is not represented at all. The
coarse grid then solves a *different* problem, and prolonging its correction back
drives the iteration apart. Four discs in a box reached **1e134 V**.

The tell, before divergence, is a convergence factor that **worsens with
refinement** instead of holding steady. A healthy V-cycle is grid-independent;
one that degrades is telling you the coarse levels are not solving your problem.

Three fixes were tried. Only the third works, and the failures are instructive:

- **Agglomeration** (fix a coarse node if anything in its 3×3 block is fixed) is
  stable, since growing the Dirichlet set only damps, but it grows the electrode
  a cell per level and roughly triples the convergence factor.
- **A flat depth floor** stops small grids coarsening at all, costing a
  33-node case its multigrid entirely.
- **A total fixed-node retention ratio** fails for a subtle reason worth knowing:
  a disc loses three quarters of its nodes per level, which is *exactly* the rate
  healthy coarsening produces, so the ratio stays flat right up until the rod
  vanishes. Counting only **interior** fixed nodes separates the case that must be
  limited from the case that must not.

## A search that fails must say so

The separation bisection in the mirror-pair study assumed four penetration depths
bracketed its root. True for one linear stage; false for two. It converged
silently on its own bracket ceiling and returned geometries that were never at
first-order focus.

Then the ranking made it worse: candidates were ordered by their second-order
coefficient, and an *untuned* mirror can have a small c₂ while its uncancelled c₁
destroys the resolution. So the ranking actively **preferred the failures**.

It presented as "two-stage barely helps, R = 260". After expanding the bracket
until it genuinely contains a sign change, and refusing to rank any candidate
whose first-order term is not actually cancelled, the same profile gives
R = 320,548. A three-order-of-magnitude error that looked exactly like a physics
result.

## A residual of exactly zero is not success

The FLD-1 spike reported a potential residual of **0.000E+000** for a sub-cell
geometry perturbation, and the obvious reading — perfect linearity — was exactly
backwards. The plate had moved less than one mesh cell, so the rasterised
geometry occupied identical nodes, the perturbed solve came back bit-identical,
and the derivative was identically zero. A tolerance study on that would have
concluded the parameter did not matter.

Exact zeros in a numerical result deserve suspicion rather than satisfaction.
They usually mean a quantity was never computed rather than computed to be zero,
and the two are indistinguishable in the number itself.

The underlying cause is fixed — the boundary now moves sub-cell, and the same
measurement returns a proper quadratic Taylor remainder — but the guard stays,
because what it detects is "the model never saw the perturbation", and that can
be true for reasons other than the one that prompted it.

The neighbouring case makes the same point from the other side: at 0.1% and 0.3%
perturbation an earlier fixture returned residuals of 0.2297 and 0.2297 —
identical to four figures. A residual that does not move when its input moves is
not measuring its input.

## A boundary condition that moves when you coarsen

A Dirichlet domain edge was implemented as a ghost node one cell outside the
grid, held at zero, with the edge node itself solved. On a single grid that is
perfectly self-consistent, and it had been right for months.

It is wrong the moment there is more than one grid. The ghost sits one cell out
at the fine level, two at the next, four at the next — so every level of a
V-cycle solves a slightly *larger domain* than the one above it, and the
correction it computes is for a different problem. A cap plate in a grounded box
diverged to **1e50 V**.

Two things about how it stayed hidden are worth more than the bug itself. First,
another limitation was masking it: the interior-electrode coarsening floor
stopped these geometries before they reached a second level, so the solver
silently fell back on plain Gauss–Seidel and reported a convergence factor of
0.83. Poor, but not obviously a bug — and "poor convergence" is exactly the sort
of number one shrugs at. Removing one limitation exposed the other, which is the
usual order of events.

Second, the diagnosis came from a two-by-two table rather than from reading the
code. Cut cells were the new thing and the obvious suspect; running the same
geometry with cuts on and off, and with Dirichlet and Neumann edges, showed the
divergence in the cut-free rows and in none of the Neumann rows. That is four
solves and it eliminated the entire feature that had just been written.

## Measuring the analysis in one mechanics and the trajectory in another

Normalised emittance is conventionally written as the geometric emittance times
βγ. Implemented that way, it drifted by **8.1e-7** across an accelerating stage —
a quantity whose entire purpose is to not change.

Two separate causes, both invisible until an exact test asked for exact
invariance.

**The paraxial term.** A divergence angle is transverse velocity over *axial*
speed, while βγ is built from the *total* speed. The two differ by a factor of
1 + y′²/2, which is around 8e-7 for a milliradian packet — and it does not cancel,
because damping shrinks the divergence and shrinks the term with it. Measuring the
area in (y, p_y) directly instead of scaling an angular area by βγ removes it
exactly: 8.1e-7 → 2.1e-8.

**The remaining 2.1e-8 was γ − 1 at the exit speed**, and it was there because the
analysis was relativistic while the transport is not. Relativistically an axial
force conserves γmv_y exactly, so v_y falls slightly as γ grows. Newtonian
transport holds v_y exactly constant, so γv_y grows. Carrying a γ in the analysis
measured the packet in mechanics the trajectory was not integrated in, and the
mismatch surfaced as an emittance that grew out of nowhere.

Dropping γ took it to **3.0e-16**, machine precision. What that gives up is bounded
and remote: γ − 1 reaches 1 ppm at around 460 keV for m/z 500, against the few keV
an ion-optical instrument runs at. If a relativistic transport mode is ever added,
the term has to come back — **in both places at once**, which is the actual lesson.
An analysis and the integrator it consumes must agree about the mechanics, and a
conserved quantity is the only thing that will tell you when they do not.

## A packet with no area has no orientation

A cloud with spatial spread and no temperature is perfectly parallel. Its emittance
is exactly zero — a real answer, not a degenerate one — but the Twiss α that
describes the *tilt* of its phase-space ellipse is undefined, because there is no
ellipse. It came out `NaN`, JSON cannot represent `NaN`, and the serialiser took
the entire result document down.

This is the **second** time that exact failure has happened here; the first was a
convergence residual. Both appeared only under `--json`, both were caught by a test
written for something else, and in both cases the field was one that is normally
finite and only fails in a case nobody pictured.

The fix that generalises is not another `Finite()` guard: it is that an undefined
measurement should be **absent, not zero**. Zero is a real emittance and a real α,
so a reader cannot tell a measured zero from an absent measurement if both print as
zero. The field is nullable and omitted when there was nothing to measure.

## Projecting onto half a basis

The rectilinear trap's field quality was measured by expanding the potential on a
circle in multipoles and comparing the largest unwanted term against the round-rod
quadrupole's. The helper was copied from the quadrupole study, where it projects
onto **cos(nθ) only**.

That is exact there and wrong here. Four identical round rods are four-fold
symmetric, so every sine term vanishes identically and a cosine projection loses
nothing. This trap has a slot in one plate and not the other: it is mirror
symmetric in x and not in y, and an asymmetry about the x axis lands **entirely in
the sine terms**. The cosine projection reported the odd orders as 1e-9 — which
read as "the slot costs nothing" — and the published figure named the 12-pole as
the worst aberration at 7.12e-3.

With both phases, the dipole is 5.43e-2, seven times the 12-pole and the largest
term by far. The headline changed from 296x worse than round rods to 2,258x, and
the attribution changed with it: the 12-pole is what flat plates cost, the dipole
is what the slot costs.

What makes this worth recording is that the near-zero was **evidence**, not
reassurance. Odd multipoles at 1e-9 in a geometry that is visibly asymmetric is
not a small effect, it is an absent measurement, and the right response to a
suspiciously exact zero is to ask what could make it exact rather than to bank it.
The same instinct is already in this file under a residual of exactly zero not
being success.

Generalises to: **a symmetry that makes half a basis redundant is a property of
the device, not of the method.** Reusing the reduced form on a device without that
symmetry measures a projection of the answer and reports it as the answer.

## A mirror plane the interpolant did not know about

The bicubic stencil reaches one node outside the grid and fills it by linear
extrapolation. That is already a considered choice - clamping was wrong and cost
7.5 ppm, and is recorded above. What was missed is that extrapolation is right for
a *Dirichlet* edge, where the data simply ends, and wrong for a **Neumann** edge,
which is a mirror plane: the field continues across it as its own reflection, so
the ghost is the node one step back inside rather than the ramp continued outward.

It surfaced on the axis of the first axisymmetric solve, where the radial field
must be exactly zero because there is no radial direction to point in. It read
**14 V/m**. An ion launched exactly on axis would have drifted off it - slowly,
plausibly, and in whichever direction the interpolant happened to lean. Reflecting
took it to exactly zero.

Two things worth keeping. The bug had been live in a shipped template all along,
because the mirror pair declares a Neumann edge too; it never failed a test because
no test asked what the field was *on* that plane. And the fix is one line per edge
that the original comment had already argued its way to the doorstep of - it says a
Neumann edge is a mirror, in the solver, three files away from the interpolant that
did not act on it.

Generalises to: **a boundary condition is a statement about the field, not only
about the solve.** Anything that samples the field afterwards has to honour it too,
and the place that is easiest to forget is the one where the answer is a clean zero
that nobody thought to check.

## Fixing an instance four times before fixing the class

JSON has no not-a-number and no infinity. Every result surface here is JSON, and a
single non-finite double does not degrade a document - it takes the whole thing
down, at the serialiser, after the run has already succeeded and the numbers are
in hand.

It happened four times, on four unrelated fields:

1. a convergence residual, when there was no order to resolve;
2. a Twiss orientation, for a packet with no phase-space area to be tilted;
3. a space-charge fraction, for a packet with no beam energy to be a fraction of;
4. the energy drift of a driven field, which reports not-a-number *deliberately*,
   because a field that does work on purpose has no conservation to diagnose.

Each was found the same way - a run that worked and a `--json` that did not - and
each was fixed where it was found. Three guards, then a fourth field nobody had
guarded.

The fix for the class is a converter: **a non-finite double is written as null.**
That is the policy the rest of the surface had already arrived at by hand - an
undefined measurement is absent, not zero, because zero is a real answer and a
reader cannot tell the two apart if both print as zero - so making it structural
costs nothing and closes the whole family. Reading is the mirror, so a stored
result still round-trips, which `verify` needs.

The lesson is about *when* to generalise. Once is an incident; twice is a
coincidence; by the third the shape of the class was already visible in the
comments being written, and the fourth was avoidable. A guard that has to be
remembered per field is a guard that will be forgotten.

## The evidence was computed and then thrown away

The segmented quadrupole lost its ion at q = 0.611 instead of 0.908 for a whole
revision, because a solve stopped short of its tolerance. The solver knew. It
returned a `SolveReport` carrying `Converged`, the cycle count and the final
residual — and `FieldAssembly.Build` wrote

```csharp
elements.Add(GeometryBuilder3D.BuildField(geometry).Field);
```

dropping `.Report` on the floor at the one seam every run, study and test passes
through. Downstream, an unconverged field is **indistinguishable from a converged
one**: it is a grid of plausible numbers with no marker on it.

Fixing the multigrid fixed the symptom. It did nothing about the reason nobody
noticed for a revision, which is the part that generalises. **GRD-2 exists for this
exact failure** — warnings propagate through engine, command layer, CLI and
exported files — and a seam that converts a reported result into a bare object is
where that requirement quietly stops holding.

Two ways out, and the choice depends on whether there is anywhere to put a taint:

- Where a **result** is produced, taint it. `BuildReported` hands back the field
  and its warnings, and `run` and `preview` carry them onto every number computed
  through the field.
- Where only a **field** is produced, there is nowhere to attach anything, so
  `Build` throws. "Taint, never block" is about results; a bare object with no
  envelope has no third option between refusing and concealing, and concealing is
  what it was doing.

The generalisable rule: **when a computation produces evidence about its own
quality, the type that returns it may not have a shape that makes discarding the
evidence the shortest spelling.** `.Field` was one character shorter than handling
the report, and that was the whole mechanism.

**It has now happened four times, and the fourth is the one worth reading.** After
`FieldAssembly.Build` dropping its `SolveReport`, the sweep evaluator dropping its
warnings, and `CollisionSampler.BoundExceeded` / `SampledOutsideFlow` being computed
and consumed by nobody — a pressure field was added, `SampledOutsideDensity` was added
beside its sibling on the sampler, and on the first draft it was **dropped in exactly
the same place as the two above it, in a file whose comment already said this was the
third time.**

Reading the comment is not the same as being protected by it. The property was
declared, set, and never read; the code compiled, every test passed, and a run that
extrapolated its gas density past the imported box said nothing about it. What fixed it
was not vigilance but a question asked deliberately after the fact — *grep for every
new public member and check something reads it* — and then a test that drives the
warning end to end through the CLI, because the wiring is what keeps breaking rather
than the computation.

So the rule needs a second half. The first is that discarding evidence must not be the
shortest spelling. The second: **adding a quantity to a type that already reports
several is not the same as reporting it**, and the existing reporting code is exactly
where the eye slides past.

A companion, found in the same review: `SolveOutcome.Converged` was
`Elements.All(e => e.Converged)`, which is `true` for an empty list. `einzel solve`
read only the two-dimensional element, skipped every three-dimensional one, and
answered `converged: true`, exit code 0. Vacuous truth is worse than a failure
because it terminates the investigation.

## Two arithmetic slips, for completeness

**Velocity fraction is not energy fraction.** v ∝ √E, so a fractional energy
offset δ is a fractional velocity offset of about δ/2 — and a factor of two in the
linear term is a factor of four in the quadratic. A hand estimate of the
single-stage resolving power came out 8× low because of it.

**A symmetric energy spread is not a symmetric velocity spread**, for the same
reason. Ions at ±5% energy do *not* arrive together at a first-order focus; the
residual is the second-order term evaluated at two different magnitudes. Asserting
they coincide would have been asserting the wrong physics. The closed form
ε²/(2(1+ε)) predicts a spread of 3.132e-5 and the integrator produced 3.132e-5.

## A bare double at the one seam every study crosses

`FiguresOfMerit.Evaluator` hands a sweep driver a `Func<CompiledModel, double?>`,
because ranking needs an ordering and there is no ordering on a GRD-1 envelope.
That much is right, and the discard was documented as deliberate.

What was discarded was not only the interval. The evaluator destructured the
whole `Measured` — value, uncertainty, evidence, **warnings** — and returned the
value. So every warning the flight behind a draw earned stopped there: a field
that missed its tolerance, a mode outside its validity, an integration that hit
its ceiling instead of the detector. A thousand draws could each have been
computed in a field that never converged, and the study would report a
distribution, a ranking, and nothing else.

The shape is familiar from `FieldAssembly.Build` discarding its `SolveReport`:
**a computation produced evidence about its own quality, and discarding it was
the shortest spelling.** `var (value, _, _, _) = measured` is one character
shorter than carrying the fourth field, and it reads as tidy.

Three things about the fix are worth keeping.

**The sink is per-evaluation, not per-emission.** An ensemble figure builds the
field once per ion, so a field warning arrives twenty-one times for one draw.
Counting emissions would report "on 21 of 3 evaluations", which is nonsense in a
way that discredits the whole line. The unit being counted is the draw.

**The count is the point.** "on 3 of 1000 draws" and "on 1000 of 1000 draws" are
the difference between a corner of the tolerance box and a study to throw away,
and a warning with no count cannot distinguish them.

**Taint, never block — but only where there is something to taint.** `Setup` used
to call `FieldAssembly.Build`, which *throws* on an unconverged solve, so a sweep
died rather than reporting. It now uses `BuildReported` when it has a sink and
`Build` when it does not, which is the honest reading of the rule: carry the
warning if you can, refuse if you cannot, and never drop it. A bare field has
nowhere to carry it, so for that caller the throw is still right.

The control test is the part that took a second attempt. Asserting that a clean
model's study warns about *nothing* failed — correctly — because a clean study
now reports the same convergence provenance a run does. That is the seam working,
not a regression. What the control has to assert is the absence of the specific
claim made about the strained model, which is a weaker statement and a true one.

## A formula that was right about the wrong mechanism

The space-charge screen converted a packet's self-potential into a flight-time
error as `½ × phi / V`: the self-potential is an energy spread, time goes as the
inverse square root of energy, so halve it. Every step of that is correct. The
answer was wrong by **527 times**, in the direction that under-reports.

The reasoning describes ions leaving a trap from different depths of the
self-potential well, which really does give them different energies and really
does give that timing error. It is not what dominates once they are flying. In
flight the packet **expands**: the self-field keeps pushing for the whole drift,
and the relative speed it imparts is set by turning the self-potential into
*relative* kinetic energy, sqrt(2 q phi / m). For 40,000 ions in a half-millimetre
ball at 4 kV that is 149 m/s. The old reading — perturb a 4 keV beam energy by
0.058 eV and see what it does to the speed — gives 0.28 m/s, because it is asking
what happens to an ion already travelling at 39 km/s rather than what happens
between two ions travelling together.

Both are questions about the same self-potential. Only one of them is the question.

**Nothing internal could have caught it.** The formula was dimensionally right,
monotone in every parameter, checked against a hand calculation, and covered by
three tests — all of which asserted the wrong relation with conviction. The unit
tests were the same mistake written twice. What caught it was building the direct
pairwise sum SC-1 asks for and comparing, and the first comparison failed by two
and a half orders of magnitude, which is a large enough gap that it could not be
argued away as a modelling choice.

That is what SC-1 is for, and it is worth stating in general: **a screening
estimate cannot be validated against reasoning, only against a computation that
does not share its assumptions.** Ours shared its assumptions with its own tests
for as long as those were the only thing checking it.

Two smaller things fell out of the fix.

**The inversion is a maximum where the forward direction is a minimum.** The
estimate takes `min(linear, escape)`, and `min(a, b)` is within budget as soon as
*either* is — so the population that satisfies it is the *larger* of the two
inversions. Writing the minimum in both places looked symmetric and reported a
population limit of three thousandths of an ion, which at least failed loudly.

**And the packet's total momentum is not the invariant it looks like.** The
obvious check on a pairwise sum is that internal forces do not move the centre of
mass. In a real flight that is false for two reasons that are not errors: the
applied field is an external force, and a detector removes momentum along with
the ion carrying it. Asserting on it was asserting that mirrors do not reflect.
The invariant that *is* exactly true is the balance of the mutual accelerations
themselves, checked at every stage of every step — which also covers the case the
naive version was reaching for, an indexing error over absorbed members.

## The same operator, written twice, conservative once

The cylindrical Poisson operator is written in conservative form — flux through a
ring's outer face minus its inner face, over the ring's own volume — and the
reasoning behind it is written down. Months later the drift-diffusion solver was
built on the same grid class, with the same `Cylindrical` flag, and its face flux
was computed per unit area and applied to both neighbours as though their volumes
were equal. In an axisymmetric solve they are not: a cell is a ring, so the ion
count crossing a face is created on one side and destroyed on the other.

The weight a face needs is `A_face · h / V`, identically 1 in the plane and
`1 ± h/2r` in a cylindrical one. **On the axis it is 4** — the inner face has no
area, so the cell is a disc rather than a ring. That is the *same* factor of four
the Laplacian carries on the axis, and it was already documented, one file away, as
the thing a plane operator gets wrong there.

**Three things about how it hid.**

*The tests were all Cartesian.* Every conservation check in the suite ran on a
plane grid, where the weight is exactly one and a scheme with no weights at all is
correct. They passed for a reason that did not generalise — the identical failure
mode as the uniform-field conservation test that hid a cell-centred drift sample,
recorded above.

*The ledger did not have to close.* An electrode emptied only the initial density,
and the ions it deleted were removed after the launched population had been
counted. So launched, collected, remaining and the named losses were never required
to add up, and a four per cent leak on the shipped funnel had nowhere to appear.
Making the itemisation complete is what made the defect visible; the fix to the
bookkeeping found the fix to the physics.

*It was worst exactly where it mattered.* The weight departs from one as `h/2r`, so
the error is negligible at the wall and total on the axis — which is where a funnel
puts its ions, and a funnel is the device this transport mode exists for.

**The check that discriminates is not the conservation figure.** A wrong weight can
still conserve to a few per cent over a short run, and the population sum was
99.9995% on the first off-axis fixture that exercised it. What cannot be nearly
right is the weight itself: exactly 4 on the axis, exactly `1 ± h/2r` off it. Assert
the quantity with an exact value, not the symptom with a tolerance.

**The rule.** A conservative discretisation is a property of an *operator on a
geometry*, not of a grid class. Sharing `Grid2D` and a `Cylindrical` flag with a
solver that got it right transfers none of it, and the second author of an operator
on the same mesh is the least likely person to re-derive the face areas.

## A guard that guarded nothing, under a test that could not reach it

Scharfetter-Gummel's exponent is `P = v h / D`, the ratio of drift to diffusion
across one cell, and it feeds a Bernoulli function `B(x) = x / (exp(x) - 1)`. That
function already handles a large argument **exactly** - it is zero above +40 and
`-x` below -40, which are the true limits and not approximations to them, and it
takes them explicitly to avoid an overflow inside `exp`.

The flux clamped `P` to ±40 *before* calling it. Reading the two together, the
clamp looks like it is protecting the exponential. It is not: the exponential
protects itself, one function down. What the clamp actually did was cap the
effective drift at `40 D / h`, so above a cell Peclet of 40 the density moved too
slowly by exactly the ratio the cap imposed.

**Every existing test ran below the cap.** The advection checks are at a cell
Peclet of 16 and report 1.000000. They are correct. They could not see this.

**What saw it was an expectation that was a division.** A corpus example - a drift
tube with a declared mobility and a declared gas flow, so `L / (mu E + v_gas)` is
arithmetic with nothing of the engine's in it - came out 6.7% long. Unclamped it
comes out 0.86% long, which is the packet's own spread, and the convincing part is
that the *same* 0.86% now appears with and without the flow. A residual independent
of the drift speed is a packet effect; a residual that grows with the drift is a
scheme effect, and only having both cases separates them.

**Three things generalise.**

A guard placed one level above the thing that already guards itself is not
redundant, it is a second policy - and the outer one wins silently. Look for the
inner guard before adding an outer one.

A test whose parameter sits below the threshold of the bug is not a weak test, it
is a test of a different regime. The suite's advection checks were not sloppy;
nothing about them says which side of 40 they are on. Where a scheme has a
dimensionless number in it, the tests should straddle the values that number
switches behaviour at, and should print it.

And an expectation that is *arithmetic the engine had no part in* catches a class
of thing that self-consistency cannot. That is the whole argument for EX-1's
corpus, and it paid for itself on the second batch.

## Reading the DC of an electrode that holds none, three times

A basis-superposed field is linear in the applied potentials, so an electrode's
excitation is *two* numbers — a DC potential and a drive amplitude — and code that
asks only about the first is asking about the half that is often zero. That has now
been the bug three separate times, in three unrelated places:

- **`einzel solve`, 3-D.** It iterated `Solve` elements and `continue`d past every
  `Solve3D`, then answered `Elements.All(e => e.Converged)` — vacuously true over an
  empty list. `converged: true`, exit 0, for a field it never touched.
- **`einzel solve`, 2-D.** Fixed in three dimensions and still wrong in two: it
  built one mask from the electrodes' DC potentials. For the shipped `quadrupole-rf`,
  whose every electrode holds zero DC, that was a solve of an earthed box — **peak
  potential 0 V, zero cycles, converged, exit 0.**
- **`ModelValidator.CanDoWork`.** A source at rest is legal exactly when some field
  could accelerate it, and the test was `Electrodes.Any(e => e.Potential != 0)`. So
  the **Paul trap**, the archetypal device whose ions sit still until the RF moves
  them, was refused as a model in which nothing could move an ion. The 3-D arm of
  the same switch fell through to `_ => true` and inspected nothing at all — the
  same bug wearing the opposite mask, one over-refusing and one under-refusing.

**Why it keeps happening.** `e.Potential` is the obvious spelling, it is correct for
every DC device, and every failure is silent and confident: an earthed box solves
fine, and a refusal names a plausible-sounding constraint. Nothing about the
symptom points at the drive.

The generalisable rule is the one already recorded under *the evidence was computed
and then thrown away*: **when a quantity has two parts, the shortest spelling must
not be the one that silently drops a part.** `IsDriven` exists on both electrode
records precisely so the complete question has a short name. It is worth grepping
for `.Potential` the next time something driven behaves as though it were earthed.

## The refinement ladder tightened a floor into meaninglessness

A pulsed-extraction model - two plates at zero for a 2 us hold, then plus and minus
500 V - gave `StepSizeUnderflow` at exactly the switch after 63 accepted steps. A fixed
count, invariant under tolerance, cell size, flight time and the ion's speed, which is
the signature of a step being *rejected* at every size rather than a controller
converging.

`FlightTimeStudy` refined by scaling the relative tolerance **and both absolute floors**
by the same factor. At its deepest rung `AbsoluteVelocityTolerance` reached **1e-11 m/s** -
ten picometres per second, against thermal speeds of hundreds of metres. For an ion
starting from rest the normalised velocity error is then unsatisfiable at any step size.

Isolated by tightening each of the three alone: the relative tolerance and the position
floor both cross the switch; the velocity floor alone reproduces it. And that floor is
load-bearing - it is what stops `ErrorNorm` being a position-error controller, which
section 11's own findings turn on.

**A floor states what is negligible, and what is negligible does not change because a
more accurate answer was asked for.** The ladder now refines the relative tolerance and
the position floor, and holds the velocity floor.

### What the fix cost, and what that revealed

The reflectron's flight time is **bit-identical** either way. Its interval narrows
seventeenfold - 1.48e-10 us against 2.58e-09 - because it becomes a measured residual
instead of a saturated floor.

But it broke `AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact`, and the
reason is the part worth keeping: **that model's bit-exact agreement between its two
finest rungs depended on the ladder over-tightening the very floor at issue.** The test
had been asserting a coincidence - and asserting it carefully, with the premise checked
rather than assumed, which is the only reason the breakage was legible rather than
mysterious.

Nothing reachable through the study's own API reproduces the collapse: a refinement ratio
of 1.001 still diverges at 1.7e-12, and loosened floors make the rungs differ *more*
rather than less. **A rule that can only be exercised by a coincidence is a rule with no
test**, so the rule was given a name - `FlightTimeStudy.ConvergenceResidual` - and is now
tested directly on hand-built runs that agree to the bit. That is a better test than the
one it replaces: it states the rule instead of hoping a model will demonstrate it.

### Four eliminations, and a control that was not one

Recorded because each cost time. **Not the ion's speed** - at 1e-6, 1e-3 and 1 V it failed
identically. **Not the turning-point cap**, off for a time-varying field. **Not the switch
or the stopping surface** - `SwitchCrossingTests` crosses the same shape in 123 steps.
**Not the facing pair**, which crosses in 112. **Not `FieldAssembly`** - a single
integration through it passes at every tolerance from 1e-8 to 1e-14.

And the one to remember: a run with both stages energised completed, and that looked like
a control isolating "a change at the switch". It was not - with the field on from the
start the ion reached the detector at 0.879 us and **never got to the switch**. *A control
has to reach the thing being controlled for*, and checking that the successful run
exercised the mechanism would have taken one glance at its flight time.

## Reading the DC of an electrode that holds none, a fourth time

`ModelValidator.CanDoWork` decides whether a source may start at rest by asking whether
anything in the model can accelerate an ion. It asked whether any electrode held a
non-zero potential or a drive.

**A pulsed-extraction trap holds neither until its second stage.** So the archetypal
start-at-rest device - the one §12's turn-around time is defined for, and the one CLAUDE.md
already cites as the reason a source may start at rest at all - was refused on the grounds
that nothing could move its ion.

This is the fourth appearance of the same pattern, and the third in this one function:

1. `einzel solve` reported the DC pattern for every driven 2-D geometry.
2. `CanDoWork` asked only about DC, so the Paul trap was refused.
3. `CanDoWork`'s three-dimensional arm inspected nothing at all and passed by default.
4. `CanDoWork` reads the base potentials and not the **stages**.

The repository's own advice was already written down - *"grep for `.Potential` the next
time something driven behaves as though it were earthed"* - and it is not enough, because
the fourth case is not about the drive at all. The wider statement: **a check that asks
what an instrument is doing must ask over every configuration the instrument has**, and a
sequenced one has as many configurations as it has stages.

The control matters as much as the fix. Widening a check until it accepts the case in
front of you is easy and useless; what says the widening was correct is that a sequence
which never energises anything is *still* refused.

## A stage set to an expression was read as zero

`CompileStages` built its override dictionary with `Quantity.From(value.Value, value.Unit)`
and never looked at `value.Expression`. `QuantityValue.Value` defaults to zero, so a stage
declaring `{"expression": "extractionVolts", "unit": "V"}` applied **nothing**.

The model validated. The field solved. The run reported an ion that never moved. There
was no diagnostic anywhere, because from the engine's point of view the author had asked
for zero volts - and zero volts is a perfectly ordinary thing for a stage to apply, since
that is exactly what the *first* stage of an extraction does.

Now refused, rather than supported, because what an expression should mean here is a
design question: the parameter surface it would evaluate against is the one the stage is
in the middle of changing. Refusing is the answer that does not require settling it.

Same family as an unrecognised property being ignored rather than refused, and the same
consequence: **a document that means something other than what it says, with nothing
anywhere to say so.**

## The mutation passed four tests and failed two, and the four were the interesting ones

A gas whose density varies from place to place needed the collision rate read at the
ion rather than off the declared pressure. Six tests were written for it, all passing.
Then the check that matters: **restore the bug and see which fail.**

Making `BackgroundGas.NumberDensityAt` ignore the field and return the declared scalar
failed only *two* of the six — and neither was the headline test, the one asserting
that a field at twice the declared pressure gives a bit-identical trajectory to
declaring twice the pressure.

The reason is worth keeping. That test used the Langevin model, whose rate does not
contain the relative speed — so in a **uniform** gas every scheduled event is real and
there is no thinning step at all. The branch is short-circuited on `IsGraded`, which is
correct and deliberate (a thinning that always accepts would still consume a random
draw and move every seeded result in the engine). A *flat* imported field is uniform.
So the test never reached the mutated line. It was a real test of the scheduled rate
and the null-collision bound, and no test whatever of the local read.

The second weak one was subtler. A ramp from n to 4n across the box was asserted to
collide *more than* the uniform thin gas — which sounds discriminating and is not. With
the density read at the wrong place the effective rate collapses back to roughly the
thin one, the count lands **close to** that end of the bracket, and a bare "more than"
survives on noise.

Both were fixed by making the test contain the thing it claims to test:

- The equivalence test runs under **both** collision models, and hard spheres read the
  local density unconditionally.
- The ramp test **reverses itself**. The same densities over the same box arranged the
  other way round is a configuration that *any* position-blind reading gives an
  identical count for. 11,458 against 19,700.

**A test passes a mutation when the path it exercises does not contain the mutated
line.** That is not a weak test in general — it may be an excellent test of something
else — but it is not evidence about the mutation, and counting it as corroboration is
how a suite comes to have a hole exactly where its author thought it was strongest. So
run the mutation, read *which* tests failed, and treat every test that did not as
untested rather than as confirmation.

The corollary is about brackets. An assertion of the form "between A and B" is only as
good as the distance from the true value to whichever end the bug moves it to. When a
bug's effect is to collapse a quantity onto one end of its own bracket, the bracket
cannot see it — and a *symmetry* the bug destroys (here, reversal) can.

## A time-varying quantity read through a time-free interface, four times

`ITimeVaryingField` extends `IElectrostaticField`. That is the right relationship - a
driven field *is* a field - and it has one consequence that has now cost four separate
defects: **a caller holding the base interface gets an answer, at t = 0, without
anything failing.**

1. `einzel solve` built its mask from the electrodes' DC potentials. For the shipped
   RF quadrupole, whose electrodes hold zero DC and all their potential as drive, that
   was a solve of a grounded box reported as `converged: true`, exit 0.
2. The diffusive mode accepted a driven geometry and stepped a density through the RF at
   the top of its cycle - a static field that exists for no length of time - and reported
   a transit distribution with no warning anywhere.
3. `SuperposedField` implements only `IElectrostaticField`, so **a driven element summed
   with anything else silently became a snapshot**. Fixed structurally: `FieldAssembly`
   picks a driven superposition when any member is driven, so the composition is chosen
   by what it contains rather than by what the caller asks for.
4. The renderer drew equipotentials through `PotentialAt(point)`, so every frame of an
   animation showed the same instant. The picture was plausible - at t = 0 a sinusoid is
   at its peak, so it was the field at full amplitude - which is why nobody looked.

None of these threw. Each produced a field that is *a* field the instrument has, at
*an* instant, and nothing on the output said which.

The structural fix is the one made in (3): choose the implementation by what the thing
contains, not by what the caller asks for. Where that is not available, the fix is to
make the instant an argument rather than a default - which is what (4) did, and it
brought a warning with it, because a section of a driven structure is a frame of a film
whether or not it is drawn as one.

**The thing to grep for is a call to the base interface on a value that might be
driven.** It will not be a compile error and it will not be a wrong-looking number.

## The test used a solved model, and the bug was in the analytic branch

Three times in one night a test passed with its bug restored, each for the same reason
in different clothes. The third is the cleanest.

An animation frame was choosing its page from the part of the flight drawn so far, so
the scale changed frame to frame and the ion sat pinned to the edge of a box that grew
to meet it. The test written to pin the fix asserted the obvious thing - every frame has
the same page - and it passed with the fix reverted.

The model it used was the einzel lens. A **solved** geometry declares its domain, and
the extent comes from that domain; the flight cannot change the page at all. So the test
was a perfectly good test of something, and no test whatever of the thing it was named
for. Rewritten against an analytic reflectron - no declared domain, extent taken from
the flight - it fails immediately.

The generalisation is worth more than the instance. **Where a function has branches
chosen by the shape of its input, a test fixture chooses a branch.** A fixture picked for
being convenient and realistic will pick the *common* branch, and the bug will be in the
other one - because the common branch is the one that gets exercised by everything else
and has already had its bugs shaken out.

That is exactly why the reflectron's own extent had been wrong since sections were built:
**every render test uses a device template, and every device template declares a solve
domain.** The analytic branch had no coverage at all, and the first thing a new user
renders goes through it.

## A guard written four times, silent about the fifth thing

Resolving a declared gas field needs the model document's own directory, which a study
or a figure of merit reaching the transport does not have. The rule was right and had
been reasoned about carefully: refuse, rather than run in a gas the document does not
describe, because a run that quietly uses a still uniform gas succeeds and answers about
a different instrument.

It was implemented as a guard at each of **four call sites**, and each named
`velocityField` — because that was the only importable quantity when they were written.
Adding a pressure field meant every one of them was now silent about half of what it
was guarding.

The fix was to move the check to the function that *cannot* read a file:
`BackgroundGas.FromModel` refuses an unresolved field itself, and
`WithoutImportedFields` is the deliberate exception, named for what it gives up rather
than for what it does. This is the same rule as `FieldAssembly.Build` throwing rather
than discarding its `SolveReport`, and as `Setup` using `BuildReported` where it has a
sink: **make the shortest spelling the safe one.**

It found a real defect within minutes of being written — and, pleasingly, in the
*importer*. `GasFlowImport.Resolve` began by calling `FromModel`, so the guard fired on
the one caller that was about to do the right thing, and `einzel run` on a diffusive
model with a declared field was refused. A guard that catches its own author on the
first run is a guard placed where the mistake actually lives.

The general form: **when a rule is enforced once per caller, adding a case to the rule
means auditing every caller, and the audit is invisible if you forget.** Enforce it
where the callers converge.

## A ratio of two ceilings is one

A test compared the transit through a drift tube at one pressure against the same tube
with an imported field at half of it. The mobility scaling says the ratio should be
0.5; it read 0.908, which is close enough to one to look like the scaling not working
at all.

The scaling was fine. The field was 50 V/m over 38 mm — under two volts of push — and
at 1 mbar almost nothing reached the detector inside the declared flight time:
**0.05 ions of 10,000 collected**, with a "mean transit" of 3869 µs against a ceiling of
4000. The thinner run collected 1982 and genuinely finished. What was being compared was
a real transit against a truncated one.

This is the **incomplete arrival** trap that `einzel compare` already documents and
warns about — a mean transit over the subset that arrived is not a transit time, and
the two subsets are not the same ions — met again from a direction where nothing was
watching for it. The accessor now asserts the packet arrived before reading a transit
off it, which is where the check belongs: at the point the number is taken, not at each
place it is used.

**A summary statistic computed over a truncated population is not a smaller version of
the right answer. It is a measurement of the truncation.**

## The property an instrument is built on cannot validate the field it rests in

The quadro-logarithmic field's radial component went in negated. `-dU/dr` is
`k(r/2 - Rm^2/2r)`; what I wrote was `k(Rm^2/2r - r/2)`, so it pushed ions outward where
the doc comment two lines above said "pulls inward inside it".

**Every frequency test passed with it.** The axial motion obeys `m z'' = -q k z` with no
`r` anywhere in it, so it is exactly decoupled from the radial coordinate - which is the
whole design of an orbital analyser, and the reason its frequency measures mass. A radial
field of entirely the wrong sign does not touch it. Five cases spanning radius, tangential
speed and axial amplitude all agreed with `sqrt(q k / m)` to parts in a hundred million,
while the ions they were computed from were being flung outward instead of held.

What caught it was the two checks that couple the components back together: `E = -grad U`
by numerical differencing, and energy conservation along a trajectory.

**The general form is worth more than the bug.** A designed invariance is a designed
blindness. When an instrument is built so that one quantity does not depend on the others,
measuring that quantity cannot tell you the others are right - and it is exactly the
quantity a test writer reaches for first, because it is the one with the clean closed form.
Pair it with something that spans the parts: a gradient check, an energy check, a
conservation law that involves every component.

A related trap in the same file, and it is why the first run reported twelve failures
rather than two: `Assert.Equal(expected, measured, 3)` on a frequency of order 1e6 asks for
three *decimal places*, which is 3e-10 relative. The physics was right to 5e-8 and the
assertion was wrong by two orders of magnitude. **A decimal-place assertion is an absolute
one**, and on a large number it silently becomes far stricter than anything the code could
deliver - and on a small number, far looser.

## A verb that works, is documented, and cannot be found

`einzel outline` had no line in `einzel --help`. It worked. It was documented in
`docs/cli.md`. It is the verb the shell's model tree is built on and the one an agent would
use to read a model's parameters without parsing the document. And running `einzel --help`
did not mention it existed. So did `render animation`.

For a surface whose entire argument is that an agent drives it, **a capability that cannot
be discovered from the tool is close to one that does not exist.** This is the same
reasoning that makes the platform layer of `AGENTS.md` generated rather than hand-written:
an instruction set that has drifted is worse than none, because it is trusted.

Nothing checked it, so `HelpCoversEveryVerbTests` now does - **against the dispatcher's own
switch rather than a list kept in the test**, because a hardcoded list drifts for exactly
the reason the help did. Both directions are asserted: a verb that dispatches and is not
listed, and a line listing a verb that no longer dispatches. The second fails differently -
it sends a reader to a command that answers "unknown".

Writing it took two corrections, both mine and both instructive about what the check is
*for*. The first regex missed `"optimise" or "optimize" =>`, so an alias chain read as a
help line with no verb behind it; aliases are now recognised and deliberately not required
to appear, since listing both spellings would suggest they differ. The second matched the
*nested* `agents` sub-switch and demanded top-level lines for `tasks`, `setup` and `score`,
which are correctly listed as `agents tasks` and so on - one line per thing a person types.
**Both failures were the test misreading the structure, and neither was a defect** - which
is worth saying, because a new check that fires immediately is as likely to be wrong as the
code is.

## One field answering two questions diverges where the questions differ

A run manifest recorded the model's **content hash** and not its path. By PRJ-3's own list
that is complete: the hash is what makes a result regenerable, and it survives a rename
where a path does not.

So `einzel verify` had to identify the model by searching for a file that still hashed to
the recorded value. That conflates *what this result was made of* with *what it is about*,
and the two come apart wherever two files hold the same bytes:

- The result attaches to whichever file is found first, which is arbitrary.
- **Editing the model that was actually run makes its drift disappear** - the result
  silently re-attaches to the untouched twin, reports itself current, and the edited model
  reads as never run.

The second is a stale result reporting as fresh, which is the one direction verify exists
to prevent. Reaching it takes no contrivance: `einzel init` scaffolds a reflectron, and
adding the corpus's own reflectron gives a project with two identical models. That is how
it was found - not by suspecting it, but by building a view that listed both files side by
side, where the state jumping from one row to the other was visible at a glance.

The fix records the path as well, prefers it, and keeps the hash search as the fallback
for older manifests and for the case it was written for. The general rule: **when one field
is made to answer two questions, it answers the second one wrongly exactly where the two
questions differ.** Ask what a value identifies as well as what it determines.

**And the same mistake once more, in my own fix.** I wrote the fallback's message as *"the
model has been renamed"*. It cannot know that. The recorded path being gone while identical
bytes sit elsewhere is equally consistent with a rename and with a twin that was there all
along - which is the very coincidence the whole defect turned on. My own test caught it:
deleting one of two identical models produced a "rename" rather than the orphan the test
expected, and the right response was to correct the message rather than the test. It now
says what is observed - the recorded path is gone, the same content is at X, so the result
still stands - and adds that this is a rename *if nothing else held that content*.

**Do not report a history you did not observe.** A message describing a state is checkable;
one describing an event is a guess about how the state arose, and it reads with exactly the
same authority.

## The end of a run is where the answer is, and where the picture is not

A diffusive run reports the density it *ended* with. The section renderer learned that
this is the wrong thing to draw and gained an instant to draw at; the note written at the
time says it plainly - "a model whose ions have all arrived left an empty box - correctly,
and uselessly, because the picture worth having is the packet in flight."

Adding the density cloud to the viewport, I anchored it to the end of the run. Its own
test failed immediately, and the numbers say why: the shipped drift tube launches 10,000
ions, **collects 9,999.76, and leaves 1.8e-302 behind**. Drawing that is drawing nothing,
for exactly the models that work.

The general form is worth more than the fix. **A result and a picture of a result want
different instants.** The end of a run is the only moment that answers "what happened" -
transmission, transit time, where the ions went - and it is the one moment guaranteed to
be empty of the thing a picture is of. Any surface that draws a time-evolving quantity
needs an instant chosen for the drawing, and needs to say which instant it chose.

The viewport now takes snapshots across the flight and draws the middle of those still
holding a packet, reporting it as `render.density-at-instant`. That the caller can name
its own instant is the same seam animation scrubbing will need.

## Every normal was exactly zero, and that located a transposition

The density shells passed every structural check written for them: three coordinates per
vertex, one normal per vertex, triangle indices in range, levels a decade apart. What
failed was that **760 of 760 normals had length zero**.

`Surfaces.Orient` takes the normal from the gradient of a scalar and leaves it at zero
where the gradient vanishes. A zero everywhere means the scalar was flat everywhere the
surface is - and the vertex it named was at y = 36.4 mm on a grid spanning +/-6 mm.

The cause was that I filled the sample array as `values[row, column]` where
`Contours.Sample` builds `values[column, row]`. Transposed, the contour is traced
somewhere the density is not: still a well-formed mesh, still watertight, still correctly
indexed, and sitting in a region where the density is uniformly zero.

Two things generalise.

- **A geometric invariant catches what a structural check cannot.** Counts, ranges and
  parities all held. What could not hold was a normal being a unit vector, because that
  depends on the surface sitting where the field actually varies.
- **A zero-length normal is a locator, not just a defect.** It says "the field is flat
  here", and printing *where* turned a wrong answer into a coordinate that was obviously
  outside the domain. The first version of the test only reported the worst magnitude,
  which was `1.000` and said nothing at all.

A near-miss on the same fix is worth recording: I first assumed the cause was the
differencing step, since `OrientStepMetres` is 1e-6 and is chosen for a signed distance -
which changes by the step itself - while a density changes by whatever it changes by.
That reasoning is correct and the step is now the density's own half-cell, but it was not
the bug. **A plausible explanation that fixes nothing is the expensive kind**, and only
re-measuring separated them.

## Enumerating a requirement's population, rather than believing it

`GRD-2` says validity warnings travel with the result through every layer, and then
**names them**: engine, command layer, CLI output, MCP response, exported file, rendered
figure, video. Seven. The register had said **Met** for a long time, and the evidence
column said "exported VTU/VTI files and figures".

Asking the seven questions one at a time found that two of them were no.

**The exported `.vtu` carried nothing.** `VtuWriter.WriteTrajectory` and
`WriteDensityField` both take an optional `provenance` list. The density call site
appended the run's warnings to it; the trajectory call site did not. Same writer, same
parameter, one line apart in intent and two thousand in the file.

**The rendered figure was not flying the gas**, which is the more interesting half because
it is not what was being looked for. The question was whether a *warning* reached the
figure. The answer was that the figure had never been computing the same thing a run
computes: both renderers called `TrajectoryIntegrator.Integrate` without supplying the
optional `collisions` argument, so a model at a millibar was drawn in vacuum. On the
`thermalisation` example that is a drawn flight of **2778 mm against the 155 mm the run
reports** — and the two figures, gas declared and gas removed, were byte-identical.

Three things generalise.

- **A closed population makes a requirement checkable.** "Do warnings propagate?" is
  answered "mostly, yes" — correctly — and that answer conceals precisely the layers
  where they do not. "Does a warning reach the exported file?" has no such refuge.
- **Set equality, not containment, for a carried set.** The file test asserts the `.vtu`
  carries *exactly* the warnings the result does. Asserting the anchor appears would pass
  on a file that carried one and dropped four.
- **An optional parameter whose default is a different physics is a trap, not a default.**
  This is the third time the gas has reached one path and not another — after the
  figure-of-merit path that made `einzel test` disagree with `einzel run`, and the regime
  inspector's own first draft. An omitted `collisions` argument does not fail; it produces
  a well-formed drawing of a different instrument. Where an argument selects *what physics
  runs*, absence should mean refusal, as `BackgroundGas.FromModel` now does for an
  unresolved imported field.

**The audit was worth more than either fix.** Both defects were in code that had been
reviewed, tested, and written up as working.

## The pattern

Every one of these produced a *plausible* number. None threw. The things that
actually caught them were:

- **Closed forms.** An exact answer to compare against turns a plausible number
  into a wrong one.
- **Coefficients rather than summary figures.** c₁ = −0.500, c₂ = 0.3756 named a
  bug that "R is disappointing" would not have.
- **Convergence behaviour, not single values.** An error that fails to fall with
  refinement, or a cycle count that grows with it, is diagnostic on its own.
- **Reverting the fix to check the test.** An assertion that passes with the bug
  restored is not testing the bug. The anisotropic-coarsening test first asserted
  the maximum principle, which held either way; the convergence factor was what
  actually moved — 0.213 against 0.303 — and only trying it both ways showed which.
- **Exact invariants.** The maximum principle — no potential may exceed the
  applied value — is a tolerance-free check that a solve has not diverged.
  Liouville's theorem is the same kind of check on the integrator, and being
  independent of energy it catches things energy conservation cannot. Both found
  bugs that presented as small, plausible drifts.
- **Enumerating a requirement's own named population.** Where a requirement lists
  what it applies to, the list is the test plan. Two GRD-2 layers were failing while
  the register said Met, and neither would have been found by asking whether warnings
  propagate in general.
- **Removing a refusal without removing its reason.** `CollisionSampler` refused a
  gas flow because it had no position to evaluate one at. Threading the position in
  made the refusal obsolete — but the trajectory run path built its gas with
  `FromModel`, which never resolves a declared velocity field, while only the
  diffusive path called `Resolve`. Lifting the refusal alone would have reintroduced
  the exact failure it existed to prevent, silently. **A guard is removed correctly
  only when the thing it guarded against is checked for directly.**
- **A transient inside the measurement window.** A stepped gas flow read a difference
  of 361 m/s against a declared 200, which looks like a physics discrepancy. The step
  sat 3 mm from the launch and the ion crossed it in six microseconds, so the "before"
  average was over three samples of an ion still accelerating from rest. Moving the
  step past the settling distance gave 204.5. **An average is over whatever the window
  contains, including the part that is not yet the thing being measured.**
- **A convenience accessor that quietly became a summary.** Adding a second
  generator turned `CompiledElectrode.DriveAmplitude` from *the* amplitude into *the
  first tap's* amplitude, and left it as a property with the same name. Every reader
  kept compiling. One of them was `ElectrodeOverlap.Agrees`, the check that refuses
  two conductors occupying the same space at different excitations — so two
  electrodes agreeing about the main RF and differing about a supplementary one were
  judged identical, and the mask kept whichever was written last. **The one check
  that exists to prevent a field of a geometry nobody described became a route to
  one.** Found by asking, after the change, which readers of the old scalar were
  asking a question the scalar no longer answers.
- **Measuring a linear property with a measurement that cannot be linear.** A
  stability boundary is a statement about a *frequency* - where the characteristic
  exponent reaches one - but the obvious way to find it is to ask whether the ion was
  lost. That requires it to travel to an electrode through the whole anharmonic
  region, so the answer depends on where it started and no amount of care removes the
  dependence: the shipped Paul trap's hold-converged edge is q_z = 0.85 at a 0.1 mm
  launch and 0.82 at 0.3 mm. **The fix was not a better loss measurement but a
  different quantity.** Reading beta off the spectrum of an ion that stays small
  locates the linear boundary to a worst residual of 1.2e-3 and needs no journey at
  all. When a measurement is amplitude-dependent and the thing being measured is not,
  suspect the measurement rather than adding controls to it.
- **A stability test cannot see a wrong operator, and a positivity test cannot
  either.** Backward Euler on the Scharfetter-Gummel coefficients is unconditionally
  stable and stays non-negative at every Gauss-Seidel sweep, and both properties
  survive a genuinely wrong assembly - gathering a neighbour with *this* cell's outward
  coefficient instead of its inward one passed every stability and non-negativity test
  in the suite. What caught it was the **Boltzmann equilibrium**, which the scheme is
  built to hold exactly and which moved by factors of 6 to 18. The rule: when a scheme
  has an exact fixed point, the test that matters is that it sits there - the
  well-behavedness tests are satisfied by too many wrong schemes to discriminate.

- **A convergence study needs the packet still in the domain.** A first version of the
  implicit order test compared densities after a window long enough for the packet to
  have been collected. What remained was a residue, and the relative L2 difference
  between two nearly empty fields came out at 39 to 71 per cent and scaled with nothing.
  It reads as a broken scheme. The same measurement over a window the packet has not
  left is clean. **A norm over a field that is mostly gone is a norm over what is left.**

- **The reference in a convergence study carries its own error, so the linear quantity
  is not what it looks like.** Comparing an implicit run at gain g against an explicit
  reference, the two are (g - 1) base steps apart, not g - so what must be constant is
  error/(g - 1), and dividing by g makes a correct first-order scheme look like it is
  converging at the wrong rate. Only visible because the gains spanned a factor of
  eight; at two gains either reading fits.

- **A figure of merit and the run that reports it must be the same computation, and
  saying so once does not keep it true.** `einzel run` flew its ions through a declared
  gas and `einzel test` flew the same model in vacuum, because the figure-of-merit path
  built the launch, the field and the detector but never a collision sampler. They
  disagreed by 95 us on the corpus example whose entire subject is a gas carrying an ion,
  and nothing compared them. **This is the second time**: `run` and `test` computing a
  flight time two ways was found and fixed once already, by collapsing them to one
  implementation - and the gas then arrived on only one side of the seam. A shared entry
  point is not the same as a shared computation, and every argument added to one of them
  is a chance for the two to drift apart again.

- **An example whose expected value coincides with the broken answer can never catch the
  break.** `gas-flow-carry` launches its ion at exactly the gas velocity so the transit
  is `L/u` by arithmetic - and in vacuum an ion launched at that speed covers the same
  distance in the same time. The vacuum answer was not close to the expectation, it *was*
  the expectation, and closer to it than the physical answer. **When choosing the
  conditions that make an example's arithmetic clean, check that they do not also make
  the failure mode invisible.**

- **A tolerance in the wrong units is an assertion that cannot fail.** Expectations in
  the test format compare a *relative* error, and one example's tolerance read `500.0` -
  written as plus or minus 500 us on 5000. As a fraction that admits any positive answer,
  so the example was in the release gate asserting nothing, and its own description said
  "discriminating far past its ten per cent tolerance" - the same misreading, written
  down. An audit of all 29 expectations found one other at 50%. **A number whose units
  are ambiguous should be read against what it would mean if taken the other way**, and
  here one reading was a tenth and the other five hundred.
, and a conservation test passes with positivity
  broken.** The quadratic B-spline deposit clamps its three-node stencil onto the grid
  at a boundary; leaving the *offset* unclamped with it makes the middle weight
  `0.75 - u^2` negative — at the very edge the weights are **1.125, −0.25, 0.125**. They
  sum to one, so charge is exact and every existing test was satisfied; a positive
  macroparticle was depositing a negative density, and the gather shares those weights so
  the self-field was built from it. **The argument that let it through was written down
  in the commit message**: the weights "sum to exactly one for any offset, which is what
  lets the index be clamped at a face without losing charge". True, and it settles a
  different question than the one that mattered. When a property is invoked to license a
  shortcut, check that it is the property the shortcut needs.

- **A reference method has approximations in it too, and comparing against it at
  default settings compares two of them.** Particle-in-cell was reported as agreeing
  with the direct pairwise sum "to a few per cent". Both numbers were right and the
  comparison was not meaningful: the sum softens at the mean macroparticle spacing and
  the grid smooths at the cell, so what was being measured was the difference between
  two smoothing lengths that happened to be comparable. Taking the sum to its own point
  limit (softening / 100, worth **3.5%**) and setting the cell to the mean spacing gives
  **0.08%** - a much stronger claim, and one that says what makes them agree. **Before
  quoting an agreement, ask what each side approximates and whether the two can be set
  to the same thing.**

- **Refinement is not always an improvement, and the case where it is not is the one
  someone will walk into.** Halving the particle-in-cell cell size past the mean
  macroparticle spacing makes the answer *worse* - -15.1%, -4.2%, +0.08%, +4.4% across a
  16x range, with an optimum in the middle. Raising a resolution number is what a reader
  does when they want a better answer, and here it silently buys a worse one. Two things
  follow: the optimum has to be *reported* rather than left to be discovered, and a
  "converges under refinement" test would have passed on the wrong side of it. Checked
  by control rather than asserted - holding the cell fixed and raising the macroparticle
  count took the error from 4.42% to 1.55% to 0.93%, which is what makes it a sampling
  artefact rather than a resolution one.

- **An argument that was right about accuracy and wrong about cost.** ACC-3 forbids
  trilinear interpolation on a trajectory path. Particle-in-cell's gather looked
  exempt - it is a self-consistent field whose accuracy the deposit already bounds,
  and the gather must share the deposit's weights or the self-force does not cancel -
  so the first version used it. The reasoning holds; what it missed is that a
  trilinear force is kinked at every cell face and an embedded Runge-Kutta estimator
  reads a kink as error. **The packet took 27 times the integrator steps**, which no
  accuracy argument would have predicted. A quadratic B-spline is smooth, keeps the
  deposit/gather symmetry, and takes it back to 2x. When exempting something from a
  smoothness rule, ask what else reads the derivative.
- **Factorial experiments over code reading.** Two binary switches and four runs
  localised a divergence to a feature nobody suspected, faster than reading the
  diff would have.
- **Running the same measurement twice, slightly differently.** Two boundary
  searches over brackets differing only in their lower end returned 680.7 V and
  694.4 V for the same trap. Either alone reads as a measurement; the pair says the
  predicate is frayed, which is what sent the observation window from 60 cycles to
  200 and put a confirmation walk into `BoundarySearch`.

- **A test that passes for a reason that will not scale to the general case.** The
  viewport's colour scale must be anchored across the whole bundle, not per path — a
  per-path scale gives two ions a kilovolt apart the same colours. The obvious assertion
  is "the reported range is wider than the widest single path", and it is *correct*: on a
  packet launched from rest the margin is 0.3 eV in 20000, or 1.5e-5. That is a real
  discrimination and a fragile one, because it depends on the ions having different
  energies at all. What discriminates whatever the magnitudes are is that **no single
  path owns both ends of the scale** — any per-path anchoring reports some one path's own
  extremes and fails it. When an assertion works by comparing two magnitudes, ask whether
  there is a structural statement of the same thing; a structural one cannot be made thin
  by a change of model.

- **A default that is right for a screen and wrong for the subject.** Helix Toolkit
  defaults to a perspective camera, which is right for a game and wrong for an
  instrument: an ion-optics drawing is read for where things are along the axis, and
  that is the one thing perspective distorts. And `ZoomExtents` fired before layout, so a
  1.3 m flight ran off the edge of the viewport — the control had been given a model but
  not yet measured, so the fit was to whatever size it had before. Both are the same
  shape: **a framework default is a decision somebody else made about a different
  problem**, and the ones that produce a plausible-looking picture are the expensive kind.

- **A build setting that is a backstop reads exactly like one that is load-bearing.**
  `InvariantGlobalization` is set solution-wide for CLI-5's deterministic output, and WPF
  cannot run under it at all — the font cache constructs `new CultureInfo("en")` while
  measuring the first line of text. The question that mattered was not "can we turn it
  off" but **what is it actually protecting**: locale-independence here comes from
  passing `CultureInfo.InvariantCulture` explicitly at every formatting and parsing site,
  and the flag was the belt to those braces. Turning it off for one assembly is therefore
  safe and checkable; turning it off in a codebase that relied on it would move every
  number a French-locale user saw. Before removing a global setting, find the thing it
  duplicates — if there is nothing, it is not a backstop.

- **The failure mode where the data is right and the picture is empty.** The viewport's
  electrodes were extracted correctly, uploaded correctly and drawn correctly, and were
  invisible — there was no light in the scene, so every Phong surface rendered at its
  ambient term alone. The instinct on seeing nothing is to go and check the data, and the
  data was fine. Where a pipeline ends in a picture, the last stage has inputs that are not
  the data: **a missing light, a camera pointing elsewhere, a transparent material and an
  empty buffer all look identical**, and only one of them is about the thing being
  computed.

- **A resolution chosen from the container, not from the thing in it.** Extracting a
  conductor's surface over the whole solve domain at 48 cells makes a cell 1.25 mm across,
  and a 1 mm plate falls between lattice planes: the three-dimensional example produced
  **no conductors at all**, with nothing said. The generalisation is that a sampling grid
  sized to the domain silently loses every feature smaller than a cell, and *the smallest
  feature is usually the interesting one* — an aperture, a slot, a gap. Size the grid to
  what is being resolved, and where that means asking an object how big it is, add the
  accessor rather than switching on its type at the call site: `CompiledElectrode3D.Bounds`
  went next to `Centre` and `CharacteristicSize` for exactly that reason.

- **A defect that only a screenshot can see, found by driving the buttons rather than
  reading them.** The first named view after startup came out as the top view whichever
  button was pressed, because the camera's look, position and up were written one property
  at a time and each raises its own change notification — the control saw a momentarily
  inconsistent basis and re-derived one of them. No assertion over the view-model would have
  caught it and no amount of reading the handler suggested it. What caught it was invoking
  each button through UI Automation and reading the axis indicator out of the resulting
  image. **Where the output is a picture, the test harness has to look at the picture** —
  and the first attempt at that was itself misleading, because clicking one button and
  believing the result is not a measurement until a second button has been clicked to
  compare against.

- **A sequential colour ramp drawn as thin lines is illegible on every background, and no
  choice of background fixes it.** Viridis spans dark to light by construction, so it passes
  through whatever luminance the ground has: measured across grounds from `#101010` to
  `#D0D0D0`, the worst contrast anywhere on the ramp never rises above **1.25**. Truncating
  the dark end barely helps — skipping the darkest 60% still only reaches 2.83. What works is
  **lifting the ramp off the ground and then moving the ground further away from it**, which
  is the opposite of the instinct the symptom produces ("it's too dark, lighten the
  background"). The general form: when a scale and its ground overlap in the one dimension
  that separates them, only one of them can be moved out of the way, and it is the scale.
  Worth measuring rather than eyeballing — the optimum here (a 0.44 lift against a `#081019`
  ground) is not a value anyone would have picked, and it is bounded by a *different*
  property: lifting further makes the ramp non-monotone in lightness, which is the thing the
  ramp was chosen for.

- **The invariant axis of a cross-section is the one the beam travels along, so that is how
  far to draw it.** A translational solve says the geometry repeats along the third axis and
  never says how far; drawing to the transverse span made a quadrupole's rods 32 mm of a
  200 mm instrument, sitting in the corner of the picture beside a trajectory six times
  longer. The reach of the ions is the part of an infinite structure anyone is looking at.
  Generalises past drawing: **where a model declines to bound something, the bound worth
  choosing is usually the one the rest of the model already implies.**

- **Correcting a framework's choice after the fact is a race; telling it the choice is
  not.** The viewport control installs a camera of its own when it has none, and the
  opening view was set afterwards from the window's `Loaded` handler — so which view a
  model opened in depended on how long its field took to solve. Three increasingly
  elaborate fixes (a later dispatch priority, an atomically-assigned camera, a deferred
  fit) each made it work on the model in front of me and fail on the next. The actual fix
  was one line: set `DefaultCamera`, which is the property the control reaches for, so
  there is nothing to race. **When a fix has to be re-tuned per case, the thing being fixed
  is usually ordering, and ordering is not fixed by trying harder at the same point.**

- **A guard that cannot be reached, found by a test that could not construct its input.**
  The viewport clamped an axisymmetric trace to the axis, because revolving a profile at a
  negative radius would draw the same surface twice. Writing the test for it turned out to
  be impossible: `ModelValidator` refuses such a document outright, with the path, the
  reason and the correction. The clamp had never done anything and never could. **A second,
  weaker copy of a rule that already holds is worse than none** — it reads as though a case
  exists, and the next person has to work out which of the two is load-bearing. The test
  now asserts the rule where it lives, which is also where it holds for every other
  consumer. The general move: when a guard is easy to write and its test is hard, ask
  whether the state it guards against is reachable at all.

- **A comment that was right, directly above code that did not follow it.** A performance
  test read *"a hard assertion here would be a test of the build agent"* and then asserted
  2,000 ms — a guess about the build agent. It had been green for weeks and then passed and
  failed **on the same commit in two CI runs minutes apart**. Chasing it found something
  larger: PERF-7's whole 50 ms budget is the cost of starting CPython (45–63 ms measured
  here), so the requirement is not separable from a term the platform does not control.
  Two lessons. **When a comment states a hazard, check that the code below it avoids that
  hazard** — the comment is evidence somebody saw the problem, not evidence they solved it.
  And **a flaky test is worth chasing to its root rather than loosening**: the loose version
  had hidden a real finding about a requirement.

- **A gate that only fires in the cases guaranteed to fail.** Fixing the above, I gated the
  absolute assertion on "the interpreter starts in under the budget" — which means it runs
  precisely when process start has consumed the whole budget and left nothing for the work.
  It failed on its second run. **A conditional assertion needs its condition checked against
  the failing case, not just the passing one**: the question is not "when is this safe to
  assert" but "what does the population of runs that reach the assertion look like".

- **A command's return type is part of the architectural boundary, and a caller acquires a
  dependency by reading a property.** `ResultsCommand` handed back
  `Einzel.Io.MeasuredJson` — the type the CLI already serialises, so it looked like reuse
  rather than a decision. The shell then referenced `Einzel.Io` without a single `using`
  directive being written for it, because reading `measured.Uncertainty.Lower` is enough.
  UI-1's invariant test caught it, which is the point of having the invariant checked by a
  machine: nobody reviewing the view model would have seen a boundary being crossed, since
  the code that crosses it looks like ordinary property access. **Where an assembly
  boundary matters, the types on the public surface are the boundary** — not the using
  directives, and not the project references somebody remembered to omit.

- **A control experiment before a fix, when the symptom appeared alongside a change.** The
  shell window started rendering blank white immediately after I added three new panels, so
  the panels were the obvious suspect. Reverting the XAML and rebuilding produced the same
  blank window from code that had rendered correctly an hour earlier — the graphics state
  had degraded over about fifteen launches and hard kills in one session. Without the
  control I would have spent the time "fixing" working code. The visual tree was
  independently readable through UI Automation throughout, which is what let the work
  continue: **when the display is unreliable, assert on the content rather than the
  picture** — and for "are the right numbers in the right cells" that is the better check
  anyway.

- **A designed scan is not a sample, and putting a sampling interval on one is a category
  error.** Closing the GRD-1 envelope gap meant resampling the ion cloud to get an
  uncertainty for statistics that have no closed-form error. It worked, and it also
  produced intervals for models that declare *no* cloud — where the acceptance is swept
  deterministically, evenly spaced and seed-free, the same every run. Resampling that
  reports the scan's own spacing as though it were a sampling error. The codebase had
  already been bitten by the same confusion from the other direction: `DefaultEnergySpread`
  carries a paragraph written because somebody compared a deterministic sweep with a
  cloud's random draw and read the difference as noise in an objective. **Before attaching
  an uncertainty, ask what would have to vary for the number to vary** — if the answer is
  "nothing, it is the same every run", there is no uncertainty of that kind to report.

- **The bootstrap is inconsistent for extreme-order statistics, and I demonstrated it by
  accident.** Wanting a statistic with no closed-form error, I picked the range — and its
  estimated error went 0.181, 0.313, 0.225 across a sixteenfold increase in sample size,
  not falling and not converging. That is a known property: a resampled draw can only
  contain values already present, so the resampled maximum comes from a handful of the
  largest observations however many replicates are taken. It matters here rather than
  academically, because *the widest entry radius that still arrives* is an extreme-order
  statistic and this project has already had to replace one such measurement with a count
  over a fixed grid after it gave 0.65 mm on one radius grid and 0.20 mm on another for the
  same geometry. The test now asserts the **failure**, because a test that tolerated either
  outcome would document nothing and one demanding success would demand something untrue.

- **"Absent" and "zero" are different answers, and so are their reasons.** A model with no
  source temperature has a turn-around time of exactly nought — an analytic statement about
  the model, needing no ensemble and carrying no sampling interval. My test asserted it
  should be absent, on the reasoning that ensemble figures need a cloud. Both halves were
  right and the conclusion was wrong: it is not that the figure could not be computed, it is
  that it *is* nought. **When a figure is missing, the question is not "was there enough
  data" but "is there a value" — and sometimes the value is zero and the evidence is a
  derivation rather than a measurement.**

- **A heuristic about size, standing in for a statement about behaviour.** Checking that a
  sequence's phase-to-phase diff discriminated, I asserted a phase must change fewer than
  all its electrodes — reasoning that marking everything would mean the diff had stopped
  discriminating. The corpus's extraction moves **both** its plates, to +500 V and −500 V,
  because push-pull extraction is what it is. The assertion was a claim about instrument
  size wearing the costume of a claim about the diff. Replaced by recomputing the expected
  diff in the test and comparing it exactly, which cannot be satisfied by a wrong
  implementation and does not care how many electrodes there are.
