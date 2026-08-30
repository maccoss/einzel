# The shell

WPF on Windows, and §16's eleven views. Three exist: the model tree, the journal, and
the 3D viewport.

**UI-1 is the whole design.** The shell owns layout, input, the interactive viewport and
the update check. It owns no physics, no validation rules, no file format knowledge and
no render output. **Amendment 25** adds the constructive half: every shell action must be
*expressible* as a CLI invocation and journalled as one — so a capability with no command
spelling cannot be added to the window, and a person's session hands over to an agent in
the same vocabulary.

That is not decoration. It has now twice meant a window feature could not be built until
a command existed:

| The view | The command it needed | Why the window could not just do it |
| --- | --- | --- |
| Model tree | `OutlineCommand` | A window that parsed the model to build a tree would grow its own idea of what a model is, and the two would come to disagree |
| 3D viewport | `ViewportCommand` | A viewport that integrated its own trajectories would be a second transport implementation |

Both are the same argument, arriving twice. The one to watch is the in-process path
acquiring an argument the command form has no spelling for; that is the moment the
amendment is being broken, and it will look like a convenience at the time.

## Windows-only is the shell, and nothing else

Architecture invariant 1 says nothing below the shell may reference it and every assembly
above it builds and runs on Linux. Two tests enforce it, both running on Linux:
`NothingBelowTheShellReferencesIt` and `TheShellReachesThePlatformThroughTheCommandLayer`.

**The second had to check two different things, and the difference cost a mutation.**
`GetReferencedAssemblies` reports what the *compiler emitted* — what the code actually
uses — so adding a `ProjectReference` to the whole transport engine left no trace in the
metadata and the test passed. UI-1 is about what the shell may reach *for*, not what it
has reached for so far, so the project file is parsed too.

**And the shell itself compiles on Linux**, which was an open bet and is now measured:
`EnableWindowsTargeting` is enough, XAML markup compilation included. The whole solution
builds on Ubuntu with the .NET 10 SDK, zero warnings, `einzel-shell.dll` among the
outputs. It does not *run* there and is not meant to.

`Einzel.Wpf.Tests` is the one Windows-only test project here. On a non-Windows host it
builds as an ordinary `net10.0` assembly with no sources and no test adapter, so a
solution-wide `dotnet test` walks past it. Compiling a stub that asserted something would
be asserting it about a shell that is not there.

## Globalization: the one build setting the shell reverses

`Directory.Build.props` sets `InvariantGlobalization` for everything. **WPF cannot run
under it**: the font cache constructs `new CultureInfo("en")` while measuring the first
line of text, which throws, and the window dies before it is shown — a
`TypeInitializationException` out of `MS.Internal.FontCache.MajorLanguages`.

What that setting was protecting is unaffected. Its stated purpose is that number and
date formatting must not vary with the host locale, because that would leak into CLI
output (CLI-5) and into golden-file comparisons — and the way this codebase achieves that
is by passing `CultureInfo.InvariantCulture` explicitly at every formatting and parsing
site. The build flag was the belt to those braces, and the braces are what hold.

**The parse is the one that matters.** A person typing a value into the model tree is
parsed invariantly whatever their locale, because the file they are editing is invariant.
`1,5` is refused rather than quietly read as fifteen.

## The model tree

`einzel outline` returns the declared parameter surface: value or expression, unit,
bounds, description, what each resolves to in SI, and whether it is editable. All of it
was already declared, which is the pleasing part — LIB-1 gave parameters units and bounds
so a *study* could perturb them, and those turn out to be exactly what a person editing
one needs to see.

**Every edit goes through the shared journal, not to the file**, so a change made in the
window is undoable by an agent connected to the same session and vice versa (MCP-1,
GRD-9). Writing the file directly would be the shortest spelling and would silently make
the session one-sided.

**Delivering it reversed a guard.** `SessionJournal` refused any edit that did not
validate. Live validation makes that untenable: a person typing 500 into a parameter
bounded at 50 must see the tree standing with the complaint against it, and refusing every
invalid document forbids any edit *sequence* that passes through one — widening a bound
and then setting a value beyond the old bound works in one order and is refused in the
other. Narrowed to refusing what does not *parse*, which is taint-never-block applied to
input. What that cost is in `lessons.md`: narrowing a guard without asking what each of
its callers will be told afterwards is how evidence gets dropped at a seam, and the MCP
server was the caller that stopped being told.

## The 3D viewport

§16: geometry, electrode potentials by colour, equipotential surfaces or slices,
trajectory bundles coloured by energy, m/z or fate, and density clouds rather than
trajectories for diffusive regions. **All of that is built except the density cloud.**

The window draws the instrument, the field on its section plane, and the ions, each on a
colour scale anchored once across everything shown. Layers can be turned off individually,
and the electrodes are semi-transparent by default — the whole reason to look at metal and
an ion together is to see where the ion goes relative to the metal, and an opaque rod in
front of the axis makes that the one thing the picture cannot show.

### Helix Toolkit, and what taking it knowingly means

r06 names Helix Toolkit on its DirectX 11 path, with the reason that plain WPF Media3D
cannot render 10⁴ trajectories interactively. Two things r06 could not know, both found
by checking rather than assuming (§20 asks for exactly that):

- **The 2.x line contemporaneous with r06 is .NET Framework-only** and restores on net10
  only through the NU1701 compatibility shim. **3.1.2** has real `net8.0-windows` targets
  and restores clean with no fallback.
- **Every Helix DirectX package depends on SharpDX, archived since December 2020.** That
  is a dependency on an unmaintained project, taken with open eyes.

It is taken because **§17 makes this path screen tuning, not an artifact**. The
publication figure comes from `Einzel.Render` — vector, headless, no third-party
dependency — so the archived library is confined to a window, and its failure mode is that
the window stops working, not that a figure cannot be produced. LIC-1 is verified rather
than assumed: MIT from the embedded licence file, and the transitive closure is MIT
throughout.

### A conductor is the zero level set of its own signed distance

The same argument `Contours` makes for a vector section, one dimension up: an electrode
outline is the zero set of its signed distance, so one routine draws every conductor there
is and a shape added to the model format needs no change here (architecture invariant 2).
Drawing from the declared primitives instead — a box as a box, a cylinder as a cylinder —
would be shorter today and would need a new case for the fourth shape.

**What differs between symmetries is not the shape but what the solve claims about the
third dimension**, and getting that wrong would be wrong rather than merely ugly:

| The solve says | So the conductor is | Because |
| --- | --- | --- |
| a cross-section (translational) | an **extruded prism**, uncapped, as far as the ions go | the geometry repeats along z, so the electrode extends past anything drawn — capping it would draw an end the model does not have. The invariant axis is the one the beam travels along, so a quadrupole's rods run the length of the flight; using the transverse span instead made them 32 mm of a 200 mm instrument and put them in the corner of the picture |
| an axisymmetric half-plane | a **solid of revolution** | the half-plane is not a picture of the geometry, it is a geometry that repeats all the way round: a rectangle there is a tube in space. A domain reaching below the axis needs no guard here — `ModelValidator` refuses one, and a second weaker copy of that rule would read as though the case existed |
| a volume | a **surface extraction** | nothing is claimed, so nothing can be assumed |

**Where the prism stops is a drawing convention and the figure says so** — `render.extruded-depth`
names the depth and calls it what it is (GRD-12). A convention that is not stated is
indistinguishable from a dimension of the instrument.

**Surface nets rather than marching cubes** for the volume case: one vertex per cell at the
mean of that cell's edge crossings, one quad per sign-changing lattice edge. Watertight by
construction, no 256-case table, and a vertex that sits where the surface is rather than on
a cell edge. What it gives up is sharpness at a true crease — a box corner is rounded by
about a cell — and §17 is explicit that this path is screen tuning rather than an artifact,
so a rounded corner costs nothing that leaves Einzel.

Checked against closed forms the code had no part in:

| | |
| --- | --- |
| A sphere's volume at 24 / 48 / 96 cells | **0.99038 / 0.99760 / 0.99940**, improving under refinement |
| Its area, likewise | 0.99484 / 0.99870 / 0.99968 |
| Edges shared by exactly two triangles | **all 11,970** |
| Worst normal against the true outward radius | **1.000000** |
| A revolved tube against Pappus, 16 / 64 / 256 facets | 0.97450 / 0.99839 / **0.99990**, inscribed so approaching from below |

**Orientation comes from the field, not from the winding.** Every producer emits triangles
in whatever order falls out, and one pass then sets each normal to the gradient of the same
signed distance that defined the surface and flips any triangle that disagrees. That is
exact and needs no reasoning about which way a marching-squares run happens to run — which
is worth having, because the segments `Contours` emits are deliberately undirected. A sign
error there gives a shape lit from the inside, which looks like a hole.

### Two defects the geometry found

**A 1 mm plate vanished.** Extracting over the whole solve domain at 48 cells makes a cell
1.25 mm across, and a plate thinner than that falls between lattice planes: the
three-dimensional example produced **no conductors at all**, silently. The extraction now
runs over the electrode's own bounding box, which is both correct and much cheaper — and
the box is asked of the electrode rather than switched on its shape, because that switch is
exactly what invariant 2 forbids. `CompiledElectrode3D.Bounds` sits beside `Centre` and
`CharacteristicSize`, in the one file that already owns the shape cases.

**A profile that closes on itself has two vertices at one place.** A closed run from
`Contours` repeats its first point at the end, so a prism built straightforwardly from one
has a seam running down its side — invisible on screen and not invisible to anything asking
whether the surface is closed. Welded, so a prism has exactly the eight open edges of its
two deliberately open ends.

### The field is drawn as equipotentials on a slice

§16 offers "equipotential surfaces or slices" and the slice is the half a reader can see
*through*. A nest of closed surfaces hides everything inside the outermost one, including
the trajectories the viewport exists to show.

**Asked at an instant explicitly.** A driven field implements the time-free interface too
and answers at t = 0 through it without failing — which is how a section, a solve report, a
summed field and a diffusive run have each ended up describing an arbitrary moment of an RF
cycle. Zero is still the instant drawn; what differs is that it is chosen.

### One line geometry for the whole bundle

§16's reason for requiring a DirectX path is 10⁴ trajectories drawn interactively, and 10⁴
scene nodes is what makes that impossible. Every path goes into one vertex buffer with its
own per-vertex colours, and the scene holds a single `LineGeometryModel3D`.

### Two colour scales, and the potential one is centred on earth

§16 asks for bundles coloured by energy and electrodes coloured by potential. Those are
different kinds of quantity and get different ramps.

**Energy is sequential and gets viridis** — a floor at zero and no special interior value.

**But a sequential ramp drawn as thin lines is illegible on any single ground, and that is
worth knowing.** Viridis spans dark to light by construction, so it passes through
*whatever* the background is: measured across grounds from `#101010` to `#D0D0D0`, the worst
contrast anywhere on the ramp never rises above **1.25**. Lightening the viewport does not
fix it, and neither does truncating — skipping the darkest 60% still only reaches 2.83,
because the whole lower half of the ramp is dark. The ions it hides are the slow ones at a
turning point, which are the interesting ones.

**What works is lifting the ramp off the ground and then putting the ground as far below it
as it will go.** Blending toward white by an amount that falls to zero at the bright end
keeps the hue progression, the ordering and the top colour exactly. **0.44 is where the two
requirements meet**: lifting further breaks monotone lightness — the lifted floor rises
above the mid-ramp and the scale dips in the middle — which is the property the ramp was
chosen for. Jointly optimised with the ground, the worst contrast anywhere becomes **4.70 on
the energy scale and 6.77 on the potential one, against 1.01 and 2.49 before**, and a test
asserts both stay above 3.

The instinct was to *lighten* the background, and the measurement says the opposite.

**Potential is signed and gets a diverging ramp, symmetric about zero.** Stretching a ramp
across the observed range puts the neutral colour at the arithmetic middle, which for a lens
holding 0 V and 500 V is 250 V — an earthed tube would then be painted the same blue as a
genuinely negative one. Cool for negative, warm for positive, **and bright at both ends**:
the print-standard cool-warm ramp runs from a dark navy to a dark crimson and both sink into
a dark viewport, so the ends of the scale — the electrodes doing the most — become the
hardest things to see. The figure on paper is drawn by `Einzel.Render` and does not use this. Earth is the value a reader looks for first: it is where an ion feels
no force and what every other potential is measured against, so the neutral colour has to
sit there. The legend shows the scale's own ends rather than the observed range, or it would
not describe the picture.

**An electrode is coloured by the peak its drive reaches, not the DC it sits at.** A
quadrupole's rods hold zero volts of DC and all of their potential as drive, so colouring by
the DC alone paints a mass filter as an earthed box — the sixth appearance of one mistake,
guarded by a test this time.

### The colour scale is anchored across the bundle, and that is why it lives in the command

§16 asks for bundles coloured by energy. **A scale taken per path gives every ion the same
colours whatever its energy** — so two ions a kilovolt apart look identical and the picture
says they were the same. `ViewportOutcome` reports the range over everything drawn, once.

That is the same failure the animation's contour levels had in the other axis: anchored per
frame, a film of a packet spreading showed a packet doing nothing.

The discriminating test is not "the range is wider than the widest single path" — that
margin is 1.5e-5 on a packet launched from rest, and thin. What discriminates whatever the
magnitudes are is that **no single path owns both ends of the scale**: any per-path
anchoring reports some one path's own extremes, and fails.

A **degenerate range gives a colour, not a division**. A packet whose ions all carry the
same energy is a monoenergetic beam in a field-free drift — the simplest model anyone
writes — and a scale that divided by a zero width would paint the whole bundle NaN. Half,
rather than either end, because there is no top or bottom of a scale with no width.

**Viridis, and the reason is not taste.** The ramp is part of how a quantity is read. A
rainbow — the default almost everywhere, and what a naive blue-to-red gives — has
non-monotone lightness, so it invents boundaries where the data is smooth and hides them
where it is not, and it collapses under the commonest colour vision deficiencies. The test
asserts monotone luminance by the Rec. 709 weights over 65 samples, which is what stops
the ramp being "improved" into a rainbow later.

### RND-8 is on the face of the viewport

Above about 1e-2 mbar the model computes a density and no trajectories exist. Lines through
a funnel would depict something the model never computed — which is worse than drawing
nothing, because a picture is the artifact most likely to be shown with none of the
uncertainty apparatus attached.

**Asked of the transport mode, not of the pressure.** A viewport that decided from the
pressure would be re-deriving a decision `ITransportMode.ProducesTrajectories` already
owns, and the two would part company at the first model that declared them inconsistently.

And the reason is *stated where the picture would be*: an empty viewport and one whose ions
were all lost look identical, and only one of them is a statement about the physics.

**The warnings say it first when they say it at all.** `render.no-trajectories` is the
engine's own words; printing a second paraphrase above it reads as two separate problems.
The summary is the fallback for a bundle missing with nothing on the record — which
`ViewportCommand` does not currently produce, and that is exactly why the window must not
depend on its not doing so.

The field's own warnings ride out with the picture (GRD-2), because a bundle drawn through
a field that never converged looks exactly like one drawn through a field that did. This is
the seam this project has dropped evidence at six times.

### Adjusting the view

Rotate, pan and zoom have always worked and nothing said so. There is now a toolbar —
**Side, Top, Front, Iso, Fit** — plus the view cube, and layer toggles for the electrodes,
the field, the paths and the transparency.

**Named views matter more than the gestures for an instrument.** Ion optics is read as an
axial section and two transverse ones, and getting to one by dragging is approximate where a
button is exact. Front on the einzel lens gives the annulus down the bore with the section
plane edge-on; Side gives the three tubes with the equipotentials bunched in the gaps.

**It opens oblique, and that is chosen rather than fallen into.** The straight-on section is
how ion optics is usually drawn and is one click away — but it is also exactly the view in
which a ring and a rectangle look the same, so the first thing a person sees should be the
one that says the geometry is three-dimensional.

### Four things that were wrong first, all about the camera

**Perspective**, which is Helix's default. An ion-optics drawing is read for where things
are along the axis, and that is the one thing perspective distorts. Orthographic now.

**`ZoomExtents` fired before layout**, so the reflectron's 1.3 m flight ran off the right
edge: the control had been given a model but not yet measured, so the fit was to whatever
size it had before.

**The opening view was silently discarded.** Set in the constructor, it framed a scene with
nothing in it, and framing an empty scene leaves the camera wherever that left it — so every
model opened in the side view whatever was asked for. It is now set once, after the first
draw that has something in it.

**And never again after that**, because a redraw is what follows a parameter edit — yanking
the camera back each time would take the view away from the person watching the thing they
just changed.

**And then it was still overridden**, because the control installs its own camera when *it*
loads — so a view set from the window's `Loaded` handler was applied and then replaced, and
which view you got depended on how long the model took to solve: the lens opened oblique,
the trap from above. Fixed by *telling* the control rather than correcting it afterwards —
`Viewport3DX.DefaultCamera` is what it reaches for when it has none, so there is nothing to
race.

**And the fit ran before there was anything to fit to, twice over.** `ZoomExtents` is
computed from scene bounds the control establishes while rendering, so calling it in the
same breath as assigning a camera fits to the bounds it had before: on a quadrupole that
framed the 32 mm cross-section and ran the 420 mm of rod off both edges. Deferred a frame,
and with animation off so the camera the next line of code sees is the one on screen.

**The first named view after startup came out as the top view whichever button was
pressed.** `SetView` wrote look, position and up into the live camera one property at a
time, and each raises its own change notification, so the control saw a camera whose look and
up were momentarily inconsistent and re-derived one of them. A whole camera, assigned at
once, fixes it. Found by clicking the buttons through UI Automation and reading the axis
indicator in the screenshot, which is the only way this class of defect shows up at all.

**The scene had no light**, so every Phong surface rendered at its ambient term alone — the
electrodes were extracted correctly, drawn correctly, and could not be seen. Worth stating
because it is the failure mode where the data is right and the picture is empty, and the
instinct is to go looking at the data.

## What is not built

- **Density clouds** for diffusive regions. The density exists, is exported as `.vti`, and
  is drawn as contours by `einzel render section`. What is missing is only the interactive
  surface — the viewport draws such a model's geometry and field and withholds only the
  paths, which is the correct half of RND-8.
- **A diffusive example with a geometry in it.** No corpus example declares one, so
  "electrodes appear on the RND-8 path" is exercised through the field rather than through
  conductors. That is a gap in the corpus rather than in the code, and a pointed one: the
  device this mode exists for is a funnel, which is nothing but electrodes.
- **Equipotential surfaces** as opposed to slices. §16 offers either; the slice was chosen
  because a nest of closed surfaces hides the trajectories.
- **Colouring a bundle by m/z or by fate.** §16 offers all three and energy is built; the
  data for the other two is already on each path.
- **Scrubbing**, which is the animation timeline's shell half. Per-phase playback rates and
  frame export are built.
- **Six more views**: figure composer, animation timeline, sequence editor, results by
  accuracy class, regime inspector, project view, extension manager, update notice.

The pattern is worth noticing: almost every remaining row is presentation over something
that already works, which is what AGT-2 is supposed to produce and is weak evidence that it
has.
