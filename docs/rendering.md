# Rendering

`Einzel.Render` turns a model into a line drawing. It sits below the shell, has no
UI types, and needs no display: RND-1 makes rendering an engine capability rather
than a shell feature, so `einzel render` and a future figure composer are peer
consumers of one pipeline.

The Linux CI job is the check. `Einzel.Render.Tests` draws real device templates as
SVG and PDF on a runner with no display, no window manager and no font server.

## Why vector, and why it matters here

This is a real gap in the incumbent. SIMION's output is essentially screenshots, so
a publication figure gets redrawn by hand in an illustration program - and the
redrawn figure then drifts from the model it depicts, silently, because nothing
connects them any more.

So the pipeline produces **paths, not pixels** (RND-3), and **text stays text**
(RND-6): every label is a real text run in both formats, so a figure can be
relabelled for a different venue without regenerating it. Nothing converts a glyph
to an outline.

A **render spec is text** (RND-2) - a small JSON file in `figures/`, versioned
beside the model, naming the model it draws. The figure in a paper is regenerable
from the repository rather than being a file someone once exported.

```json
{
  "renderSpecVersion": "0.1",
  "kind": "section",
  "model": "../models/quadrupole.json",
  "widthMm": 90,
  "equipotentials": 12,
  "caption": "As it will appear in the paper"
}
```

`einzel render section figures/quad.json` draws it; `einzel render section
models/quad.json` draws a model directly with the defaults. Either file may be
handed to the verb, and which one it is comes from **what it declares** -
`renderSpecVersion` versus `schemaVersion` - not from which folder it sits in. A
spec detected by its folder is a spec broken by being moved.

## Conductors are drawn from their own signed distance

Nothing in this assembly knows what a mirror or a quadrupole is (architecture
invariant 2). **An electrode outline is the zero level set of its signed distance**,
which the model format already requires every electrode to supply because the
solver and the ion absorber both need it.

So one marching-squares routine draws every conductor there is - a rod, a plate, a
ring, a sphere, a box - and it is the *same* routine that draws equipotentials,
because an equipotential is a level set too. A shape added to the model format
needs no change here at all.

## Decimation is a guarantee, not a hint

RND-5 requires a stated geometric tolerance and ACC-7 sets the default at **0.1% of
the drawing's extent**. GRD-12 requires it recorded in the artifact, so it appears
three times: in the file's own metadata, in the `--json` result, and stamped on the
page.

Ramer-Douglas-Peucker, because the guarantee is what makes the number quotable: no
discarded point lies further than the tolerance from the retained line. The cheaper
radial and nth-point schemes reduce point counts without bounding anything.

| tolerance | 4,000 points reduce to | worst deviation |
| --- | --- | --- |
| 0.01 mm | 577 | 0.010000 mm |
| 0.05 mm | 243 | 0.049892 mm |
| 0.20 mm | 104 | 0.199790 mm |
| 1.00 mm | 43 | 0.956524 mm |

Tight against the bound rather than slack under it, which is what says the bound is
the thing being respected rather than a number that happens to appear nearby.

**The point-to-segment distance is clamped, and a reflectron is why.** An ion that
turns round comes back along nearly the same line, so its turning point sits almost
on the chord between the two ends of the flight. Measured to the *infinite line*
through them it looks redundant and gets decimated away, leaving a figure of an ion
that flew straight through a mirror. Pinned by a test.

**Sample finely, then decimate.** The trajectory is flown twice: once at the model's
own cadence to learn how long the flight is, then at a cadence chosen from that.
Drawing whatever the model happens to sample for its VTU export gave the einzel lens
a three-segment curve through a focusing element, which is a drawing of the sampling
interval rather than of the optics.

That a ray through the lens still decimates to three points is the *right* answer:
straight in, kink at the lens, straight out is what a thin lens does to a ray.

## A tainted result is visibly tainted

GRD-2 carries warnings to every surface and RND-11 requires a qualified result to be
**visually distinguishable** in rendered output. A figure is the artifact most likely
to be shown to an audience with none of the uncertainty apparatus attached - RND-10
makes the same argument for video - so metadata alone is not enough.

A figure drawn from a field that missed its solve tolerance gets a hatched rule the
width of the page and a `QUALIFIED` line naming the warning code. Hard to crop out by
accident, and it survives being pasted into a slide.

## Both formats come from one scene

`Scene` is paths and text runs in page millimetres. SVG and PDF are two writers over
it, so a figure cannot come out different in one format than in the other - which is
the failure mode of a pipeline where each format re-derives the drawing.

**The PDF writer is hand-written, and LIC-1 is the reason** as much as weight. The
capable PDF libraries in .NET are variously GPL, AGPL, or dual-licensed in a way that
has to be re-checked per release, and a figure writer is not where this project wants
a licence question. What a section figure needs - paths, strokes, fills, and text in
a base font - is a few hundred lines of a format stable since 1993. Text is set in
Helvetica, one of the fourteen fonts every reader must have, so nothing is embedded.

Its cross-reference table is checked by a test that walks every offset and asserts it
lands on the object it claims. That is the part a reader refuses on, and it is the
part most likely to be wrong in a writer built by hand.

## An axisymmetric solve is drawn as a whole section

A cylindrical solve is a half-plane in (axial, radial), and a section through the
axis of one shows **both halves** - a ring is two conductors on the page, not one.
That is SYM-1, declared on the solve, so it is symmetry knowledge rather than device
knowledge; the renderer still has no idea what the rings are for.

The field needs no special handling: a cylindrical solve is already wrapped as an
axisymmetric field, which samples at the radius of the point it is asked about and so
answers for a negative one already. Only the electrode signed distances are mirrored.

## What marching squares got wrong first

Cases 1 and 14 both emit the segment `(Left, Bottom)` - they are complementary cells,
and a consistently oriented contour would traverse them in opposite directions. So
**segment orientation is not consistent**, and a joining pass that matches only
head-to-tail breaks the contour at every such crossing.

A rectangular conductor came out as four separate runs instead of one. That looks
identical on screen until the path is filled or dashed, and it is not free: joining
undirected took the einzel lens from 10 conductor runs to 6 (three tubes, mirrored),
the quadrupole's equipotentials from 338 paths to 28, and its PDF from **112 KB to
13 KB** for the same drawing.

Endpoints are bucketed by rounded coordinate so joining is linear rather than
quadratic in the segment count; a contour over a fine sample grid is tens of
thousands of segments and the quadratic form is minutes.

## A density is drawn where a trajectory cannot be

RND-8 and TRN-2 forbid drawing lines through a diffusive region: above about
10^-2 mbar the model computes a density field and no trajectories exist, so lines
would depict something the model never produced. The renderer asks the transport
mode `ProducesTrajectories` rather than inferring it from the pressure, and draws
none when the answer is no.

On its own that rule is entirely negative, and it was: the mode's principal output
had no drawing at all, so the honest figure of a funnel at a millibar was an empty
box with a warning on it. Density contours are what goes there instead.

**Decades, not even fractions.** A density spans orders of magnitude - the core of
a packet and its tail differ by six - so evenly spaced levels put every line inside
the core and draw the extent, which is the part a reader most wants, not at all.
Six levels at 10^-1 through 10^-6 of the peak, fainter with each decade, because
the eye reads line weight as concentration whatever the caption says.

**The levels are recorded in the figure's provenance.** A density plotted without
them is a shape rather than a measurement, which is GRD-12's argument applied to a
contour set.

**Lines rather than filled bands.** Marching squares gives runs, and a filled band
needs them nested into rings and holes - a different algorithm whose failures are
silent, since a hole drawn as a solid reads as a *denser* region rather than as a
bug.

**An empty density is named.** A run whose ions have all reached a boundary leaves
a residue many orders below one ion in the whole domain, and contouring that draws
the shape of the round-off. Below the floor nothing is drawn and
`render.density-empty` says so, along with the change that would produce a picture:
drawing nothing and saying nothing is indistinguishable from a figure where the
density was never computed at all.

The density is passed in rather than computed here. Running the transport is the
command layer's job - it owns turning a model document into a density problem - and
a renderer that could do it would be a renderer that decides how long a run lasts.
Where the transport refuses outright, the figure is still drawn from the geometry
and the field, and the warning says which of the two it got.

## An animation declares how it compresses time, and says so on every frame

RND-7 is unusually emphatic, and the whole design follows from it: an animation
"declares an explicit non-linear time mapping — playback rate per sequence phase — and
the current rate is displayed on screen throughout playback. **Neither part is
optional.** This is the animation equivalent of GRD-1: the artifact may compress, but it
may not hide that it compressed."

The reason is §22's own: six orders of magnitude of timescale cannot be shown honestly
at one rate, and *a viewer cannot detect the compression*. An ion spends nanoseconds
turning round in a mirror and hundreds of microseconds drifting. An animation that skips
the first to keep the second watchable has removed the part the instrument was designed
around, and nothing on screen would say so.

### The requirement enforces itself through the interface

`einzel render animation` takes **a render spec, never a bare model**, and there is no
`--rate` flag. A model document has nowhere to declare a time mapping, so the only way
to ask for an animation is to have written one down. A convenience flag would have made
the hidden-compression case the easy one.

`--fps` *is* offered, because a frame rate is a property of the playback device rather
than of the physics and changing it changes no claim the animation makes.

```json
"animation": {
  "framesPerSecond": 10,
  "phases": [
    { "until": {"value": 4.0,    "unit": "us"}, "rate": {"value": 4.0, "unit": "us/s"}, "label": "inbound" },
    { "until": {"value": 6.2,    "unit": "us"}, "rate": {"value": 0.5, "unit": "us/s"}, "label": "turn-around" },
    { "until": {"value": 10.1805,"unit": "us"}, "rate": {"value": 4.0, "unit": "us/s"}, "label": "outbound" }
  ]
}
```

On the shipped reflectron that is **1.000 s, 4.400 s and 0.995 s of playback**: the
turn-around is a fifth of the flight and **69% of the film**. Sixty-five frames, and the
frame at playback 1.000 s shows exactly 4.0000 µs.

### Units on the rate, for the reason units are on everything else

The rate is *simulated time per second of playback*, written `us/s`, `ns/s`, `ms/s`.
Dimensionless — it is a time over a time — and what makes it a *rate* is that the
denominator is a second of playback rather than a second of flight, which no dimension
can carry. So the field is called `rate` and the unit spells the playback second out.

The stamp gives **two readings of the one number**, because they answer different
questions:

> `t = 5.000 µs · turn-around · 500 ns of flight per second of playback — 2,000,000x slower than real time`

The time-per-second is what converts anything on screen back into flight time and
carries a unit, which is what GRD-1 asks of every quantity here. The slow-down factor is
the intuition, and alone it says nothing about how long the flight is. The unit is picked
from the magnitude rather than fixed, because one animation may span nanoseconds of
turn-around and milliseconds of trapping.

### Frame times are computed, never accumulated

Each frame's instant comes from its own playback time by one lookup and one multiply. A
phase whose playback duration is not a whole number of frames would otherwise push a
fractional frame of error into every phase after it, and a six-phase animation would
drift visibly against its own declared mapping.

Two details that are decisions rather than details. **The final frame is forced onto the
end**, because for a flight the last instant is precisely the one a reader wants — the
arrival, the ejection, the packet at the detector — and a frame grid that is not a whole
number of frames long otherwise stops short of it. And **a frame landing exactly on a
boundary announces the incoming rate**: it shows the boundary instant and is followed by
a frame's worth of playback at the new speed, so naming the rate that has just stopped
applying would be naming the wrong one.

### Fly once, draw many

A section figure solves the field and flies the ion inside the renderer. For three
hundred frames that is a multigrid solve per frame, which is unaffordable — and worse
than unaffordable, because two frames that flew separately are two frames that can
disagree about the flight. `FramePlan` is what a caller drawing many figures of one model
computes once: the field, its warnings, the whole trajectory, and the banner.

### The camera does not follow the ion

The first version handed each frame **the part of the flight drawn so far**. An analytic
model takes its extent from the flight, so every frame chose its page from its own
prefix: the scale changed frame to frame and the ion sat pinned to the edge of a box that
grew to meet it. It reads as a camera tracking the ion rather than as an instrument being
flown through, and *nothing about any single frame reveals it*.

So the plan carries the whole flight and `UpToSeconds` truncates it for drawing. The test
that pins this asserts one page across all frames — and **its first version passed with
the bug restored**, because it used the einzel lens, a solved geometry whose extent comes
from its declared domain and never touches the flight. Moved onto an analytic reflectron
it fails immediately. Third time tonight that a test exercised a path not containing the
line under test.

### The field moves, and the cycle closes

An animation of a driven structure drew the same instant on every frame. A driven field
implements the time-free `IElectrostaticField` as well as `ITimeVaryingField`, and
**answers the time-free one at t = 0 without failing** — so the renderer got a field, a
plausible one, and the same one every time.

That is the fourth appearance of one defect: `einzel solve` reporting the DC pattern of
a driven geometry, the diffusive mode stepping a density through a snapshot of the RF, a
`SuperposedField` becoming a snapshot when a driven member was summed into it, and now
this. **A time-varying quantity reached through a time-free interface does not fail; it
answers at an arbitrary instant.**

The instant is now declarable — `atSeconds` on a render spec, defaulting to the launch —
and an animation supplies each frame's own. A section of a driven model carries
`render.field-at-instant` either way, because a figure of a driven structure is a frame
of a film whether or not it is drawn as one.

The check is exact. A 1 MHz sinusoid has a 1 µs period; over one period, at the quarter
points:

| t | 0 | T/4 | T/2 | 3T/4 | T |
| --- | --- | --- | --- | --- | --- |
| equipotential paths | 20 | **0** | 20 | **0** | 20 |

The drive passes through zero at the quarter points, so there is no field to contour. At
`T` it is the **same drawing as at 0, to the last bit** — a sinusoid is exactly periodic
and nothing between the field and the page is not. At `T/2` the two rod pairs have
swapped sign, so it is neither.

### The contour levels are fixed once, or they flicker

Levels are spread over the field's range, and a driven field's range changes through the
cycle. Taken per frame they would be spread over whatever range that instant happened to
have — and **at a zero crossing that is rounding noise**, so the frame would fill with
contours of nothing. The same defect as a page chosen per frame, in the other axis.

`SectionRenderer.PotentialRange` samples a couple of dozen instants across the animation
on a coarse grid — coarse is enough, because the extremes of a Laplace solution are on
its boundaries, and this is a range rather than a contour. Fixed once, the contours move
because the field moves.

### The head is marked, and interpolated onto the instant

A polyline that grows says only where the ion has *been*. The head is a small closed
polygon — the scene has paths and text and nothing else, which is what lets one scene go
to SVG and to a hand-written PDF without either backend knowing about shapes.

It sits at the instant rather than at the last recorded sample. An adaptive integrator
keeps points fastest where the physics is hardest, so a marker snapping to samples would
stutter in exactly the places an animation exists to show. Interpolated linearly, which
is what the drawn polyline already is, so the head sits exactly on the line it
terminates.

### A manifest beside the frames

`frames.json` records the model and its hash, the frame rate, the playback duration, and
for every frame its file, its playback time, its instant, its rate and its phase. Every
frame carries its rate on the page, which is what RND-7 requires; the manifest carries
the whole schedule, so the compression can be audited rather than trusted one frame at a
time. It is also what a player needs: frames are equally spaced in *playback* time and
not in flight time.

### What it refuses

- **A diffusive model.** RND-8 forbids drawing lines through a diffusive region, and a
  run reports the density it *ended* with rather than one per instant — so the frames
  would all be the same box and the film would show motion that was never computed. That
  is worse than no film, because it looks like one.
- **A spec with no phases**, a phase that does not advance, a non-positive rate, a
  non-positive frame rate.
- **A spec with `trajectory: false`.** The geometry and the field are identical on every
  frame, so with the ion left out the sequence is one drawing repeated - the same
  argument as the diffusive refusal, one step further in.

And two things it warns about rather than refusing, because both are legitimate to ask
for and neither looks like a choice: `animation.past-arrival`, where the mapping outlives
the flight and the last frames show an ion that is not stationary but finished; and
`animation.stops-short`, where it ends in mid-flight, which reads as a loss.

### No video, and that is LIC-1

Nothing here rasterises, and a GPL dependency is forbidden in the default build — ffmpeg
is exactly what would be reached for. What comes out is a numbered sequence of vector
frames; assembling them is an out-of-process step with a tool the user supplies, so its
absence degrades a feature rather than blocking the platform.

## An analytic flight sets its own page

A model with no declared solve domain takes its extent from the instrument's own points,
and those used to be the source and the detector alone. **In a reflectron they are the
same point** — the ion is caught where it launched — so the page was a box a tenth of a
millimetre across while the ion travelled 1.3 m into the mirror and back.

The scaffolded reflectron, which is the first thing anybody renders, drew its turning
point at **x = 105,080 mm on a 160 mm page**. It had been doing so since sections were
built, and no test caught it because every render test uses a device template, and every
device template declares a solve domain.

The flight is now included in the extent, and the pad is taken from the extent actually
gathered rather than from the separation of source and detector — which is zero in any
instrument that catches the ion where it launched. It costs nothing: the trajectory had
to be flown to be drawn, and it is now flown *before* the page is chosen rather than
after.

## Not built

- **`render still`** - a raster projection. Nothing in this build rasterises.
- **Dimensioned callouts.** The memo's own figures are line drawings *with
  dimensions*, and there is no way to declare one yet.
- **Filled density bands, and a colour scale.** Contour lines carry the levels in
  the provenance; a filled and keyed plot would carry them on the page.
- **A density at a chosen instant.** What is drawn is the density at the end of the
  run, which is what the solver returns. Seeing the packet mid-flight means
  shortening `maximumFlightTime`.

`render still` is named by the CLI and refused with a reason rather than falling
through as an unknown command, because "not built yet" and "you spelled it wrong" are
different problems and an agent should not have to guess which it hit.

Also not built for animations: **geometry that moves**. A stage may change what an
electrode holds and not where it is, which is a rule the sequencer already enforces, so
the conductors are the same on every frame by construction. That is correct rather than
a limitation — but a mechanism with a moving part is not expressible at all, in the model
format or here.
