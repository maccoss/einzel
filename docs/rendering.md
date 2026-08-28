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

## Not built

- **`render still`** - a raster projection. Nothing in this build rasterises.
- **`render animation`** - a frame sequence with the explicit non-linear time
  mapping RND-7 requires, and the current rate displayed throughout playback.
  Neither part is optional, and it needs the sequencer's timeline and a frame
  writer.
- **Dimensioned callouts.** The memo's own figures are line drawings *with
  dimensions*, and there is no way to declare one yet.
- **Filled density bands, and a colour scale.** Contour lines carry the levels in
  the provenance; a filled and keyed plot would carry them on the page.
- **A density at a chosen instant.** What is drawn is the density at the end of the
  run, which is what the solver returns. Seeing the packet mid-flight means
  shortening `maximumFlightTime`.

Both unbuilt verbs are named by the CLI and refused with a reason rather than
falling through as unknown commands, because "not built yet" and "you spelled it
wrong" are different problems and an agent should not have to guess which it hit.
