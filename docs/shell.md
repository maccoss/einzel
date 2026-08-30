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
trajectories for diffusive regions. **Trajectory bundles are built; the geometry and the
equipotentials are not.**

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

### One line geometry for the whole bundle

§16's reason for requiring a DirectX path is 10⁴ trajectories drawn interactively, and 10⁴
scene nodes is what makes that impossible. Every path goes into one vertex buffer with its
own per-vertex colours, and the scene holds a single `LineGeometryModel3D`.

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

### Two things that were wrong first

**The camera was perspective**, which is Helix's default. An ion-optics drawing is read for
where things are along the axis, and that is the one thing perspective distorts.
Orthographic, looking along −z with x across and y up — the layout of every instrument
drawing in the literature.

**`ZoomExtents` fired before layout**, so the reflectron's 1.3 m flight ran off the right
edge of the viewport: the control had been given a model but not yet measured, so the fit
was to whatever size it had before. Deferred to `DispatcherPriority.Loaded`.

## What is not built

- **Geometry, potentials by colour, equipotential surfaces.** The section renderer already
  extracts conductors as the zero level set of their own signed distance, and
  equipotentials by the same routine; what the viewport lacks is a 3-D version of that and
  a surface to hand it to.
- **Density clouds** for diffusive regions. The density exists, is exported as `.vti`, and
  is drawn as contours by `einzel render section`. What is missing is only the interactive
  surface.
- **Scrubbing**, which is the animation timeline's shell half. Per-phase playback rates and
  frame export are built.
- **Six more views**: figure composer, animation timeline, sequence editor, results by
  accuracy class, regime inspector, project view, extension manager, update notice.

The pattern is worth noticing: almost every remaining row is presentation over something
that already works, which is what AGT-2 is supposed to produce and is weak evidence that it
has.
