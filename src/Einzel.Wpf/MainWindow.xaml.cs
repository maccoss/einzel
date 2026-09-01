using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;

using Color = System.Windows.Media.Color;

// Both WPF and Helix declare a camera of this name, and only Helix's is a
// Viewport3DX camera - the WPF one compiles here and would be rejected at run time.
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using OrthographicCamera = HelixToolkit.Wpf.SharpDX.OrthographicCamera;

namespace Einzel.Wpf;

/// <summary>The shell window.</summary>
/// <remarks>
/// Layout and input, and nothing else. Every question about what a model contains, what
/// is wrong with it, or what an edit does goes to <see cref="ModelTreeViewModel"/> and
/// through it to the command layer (UI-1).
/// </remarks>
public partial class MainWindow : Window
{
    private ModelTreeViewModel? _tree;
    private ViewportViewModel? _viewport;
    private ResultsViewModel? _results;
    private RegimeViewModel? _regime;
    private SequenceViewModel? _sequence;
    private ProjectViewModel? _project;
    private bool _framed;
    private bool _loaded;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Viewport.EffectsManager = new DefaultEffectsManager();

        // Told, rather than corrected afterwards. The control installs a camera of its own
        // when it has none, and replacing that one after the fact is a race that was lost
        // differently on every model - one opened looking down the axis, another from
        // above. `DefaultCamera` is what it reaches for instead, and `Reset` returns here.
        Viewport.DefaultCamera = Camera("iso");
        Viewport.Camera = Camera("iso");

        Loaded += (_, _) =>
        {
            _loaded = true;

            Show(_tree);
            Draw(_viewport);
        };
    }

    /// <summary>Opens a model in the window.</summary>
    /// <param name="tree">The tree over the session.</param>
    /// <param name="viewport">The viewport over the same session.</param>
    /// <exception cref="ArgumentNullException">Either is null.</exception>
    /// <param name="results">Its figures by accuracy class.</param>
    /// <param name="regime">Its dimensionless numbers along the path.</param>
    /// <param name="sequence">Its declared timeline.</param>
    /// <param name="project">The project it belongs to.</param>
    public void Open(
        ModelTreeViewModel tree,
        ViewportViewModel viewport,
        ResultsViewModel results,
        RegimeViewModel regime,
        SequenceViewModel sequence,
        ProjectViewModel project)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(regime);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(project);

        _tree = tree;
        _viewport = viewport;
        _results = results;
        _regime = regime;
        _sequence = sequence;
        _project = project;

        ProjectModels.ItemsSource = project.Models;
        ProjectContents.ItemsSource = project.Contents;
        ProjectWarnings.ItemsSource = project.Warnings;
        ProjectStatus.Text = project.Status;

        PhaseList.ItemsSource = sequence.Phases;
        SequenceWarnings.ItemsSource = sequence.Warnings;
        SequenceStatus.Text = sequence.Status;

        ResultsList.ItemsSource = results.Rows;
        ResultsWarnings.ItemsSource = results.Warnings;
        ResultsStatus.Text = results.Status;

        RegimeGrid.ItemsSource = regime.Samples;
        ExcursionList.ItemsSource = regime.Excursions;
        RegimeWarnings.ItemsSource = regime.Warnings;
        RegimeStatus.Text = regime.Status;

        Show(tree);

        // Only if the window is already up. Opening happens before Loaded, and drawing
        // twice means building the field twice - which for a 24-ring funnel is thirty
        // seconds of solving thrown away, and is invisible on anything that solves fast.
        if (_loaded)
        {
            Draw(viewport);
        }
    }

    /// <summary>Shows why there is nothing to look at.</summary>
    /// <param name="reason">What is wrong, and what to do about it.</param>
    /// <remarks>
    /// An empty window is indistinguishable from a model with nothing in it, which is the
    /// same argument RND-8 makes about an empty viewport. AGT-3's shape - what is wrong,
    /// and the correction - applies to the shell as much as to a command.
    /// </remarks>
    public void Refuse(string reason)
    {
        StatusText.Text = reason;
        StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xE0, 0xE0));
    }

    /// <summary>Draws the instrument, the field and the bundle.</summary>
    /// <remarks>
    /// <para>
    /// <b>One line geometry for the whole bundle, not one per ion.</b> §16's reason for
    /// requiring a DirectX path is 10^4 trajectories drawn interactively, and 10^4 scene
    /// nodes is what makes that impossible - so every path goes into one vertex buffer
    /// with its own colours, and the scene holds a single model. The equipotentials go
    /// into a second, for the same reason.
    /// </para>
    /// <para>
    /// <b>Electrodes are one model each, and that is deliberate</b>, because they are tens
    /// rather than thousands and each holds a different potential - which is what §16
    /// means by "electrode potentials by colour". Merging them would need per-vertex
    /// colour on a lit surface and would lose the name behind each one.
    /// </para>
    /// <para>
    /// The window computes nothing about the instrument. It receives meshes, polylines and
    /// scalars and turns them into vertices (UI-1).
    /// </para>
    /// </remarks>
    private void Draw(ViewportViewModel? viewport)
    {
        if (viewport is null)
        {
            return;
        }

        viewport.Refresh();

        Viewport.Items.Clear();

        // The ground comes from ColourRamp, which is where the ramps it has to be legible
        // against live. Set here rather than in the XAML so the two cannot be edited apart.
        Viewport.BackgroundColor = System.Windows.Media.Color.FromRgb(
            (byte)Math.Round(ColourRamp.Ground.R * 255.0),
            (byte)Math.Round(ColourRamp.Ground.G * 255.0),
            (byte)Math.Round(ColourRamp.Ground.B * 255.0));

        // Without a light in the scene a Phong surface renders at its ambient term alone,
        // which is to say almost black - the electrodes were drawn correctly and could not
        // be seen. A headlight rather than a fixed direction, so a surface never goes dark
        // as the view turns; ambient to keep the side facing away from being a silhouette.
        Viewport.Items.Add(new AmbientLight3D
        {
            Color = Color.FromRgb(0x9A, 0x9A, 0xA2),
        });

        Viewport.Items.Add(new DirectionalLight3D
        {
            Color = Color.FromRgb(0xD8, 0xD8, 0xD0),
            Direction = new Vector3D(-0.4, -0.5, -1.0),
        });

        // A second light from behind, so a surface turned away from the first is a
        // silhouette rather than a hole. A conductor is a closed shape and the far side of
        // one is in view through the near side whenever it is drawn see-through.
        Viewport.Items.Add(new DirectionalLight3D
        {
            Color = Color.FromRgb(0x6E, 0x76, 0x80),
            Direction = new Vector3D(0.5, 0.4, 1.0),
        });

        DrawConductors(viewport);
        DrawField(viewport);
        DrawPaths(viewport);
        DrawDensity(viewport);
        DrawEnds(viewport);
        DrawFates(viewport);

        Scales(viewport);
        Notes(viewport);

        // Framed once, on the first draw that has something in it, and never again.
        //
        // Two things had to be true at once. It has to happen AFTER layout, because
        // ZoomExtents needs the viewport's own size and at construction the control has
        // been given a model but not measured - fitting then fits to whatever size it had
        // before, and a 1.3 m flight ran off the edge. And it has to happen AFTER the
        // scene is populated, because framing an empty scene leaves the camera wherever
        // that left it: setting the opening view in the constructor looked right and was
        // silently discarded, so every model opened in the side view whatever was asked
        // for.
        //
        // Never again, because a redraw is what follows a parameter edit - and yanking the
        // camera back each time would take the view away from the person watching the
        // thing they just changed.
        if (!_framed && Viewport.Items.Count > 0)
        {
            _framed = true;

            // Oblique, and chosen rather than fallen into. A straight-on section is how
            // ion optics is usually drawn and is one click away, but it is also exactly
            // the view in which a ring and a rectangle look the same - so the first thing
            // a person sees should be the one that says the geometry is three-dimensional.
            // At ContextIdle, which is below Loaded and below Render, so the control has
            // finished settling. The camera is already the one wanted - it is this
            // window's DefaultCamera - so what this is really for is the fit, which needs
            // geometry to fit to. Setting the view again costs nothing and makes the
            // opening state independent of whether the control honoured DefaultCamera.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                () => SetView("iso"));
        }
    }

    /// <summary>The electrodes, coloured by what they hold.</summary>
    /// <remarks>
    /// <b>Semi-transparent by default, because a solid electrode hides the ion.</b> The
    /// whole reason to look at an instrument and a trajectory together is to see where the
    /// ion goes relative to the metal, and an opaque rod in front of the axis makes that
    /// the one thing the picture cannot show. The control to turn it off is there because
    /// transparency costs depth cues in return.
    /// </remarks>
    /// <summary>The density, as nested translucent shells.</summary>
    /// <remarks>
    /// <para>
    /// <b>What RND-8 withholds the trajectories in favour of.</b> The requirement is that
    /// a diffusive region is never drawn as lines, which on its own leaves an empty box
    /// for the entire pressure range the mode exists to cover - and an empty box and a
    /// model that lost everything look the same.
    /// </para>
    /// <para>
    /// <b>Always see-through, whatever the transparency toggle says</b>, and that is not
    /// an oversight. Three nested shells drawn opaque are one shell: the outermost hides
    /// the two inside it, so the picture would show the packet's tail and nothing of its
    /// core - the exact inversion of where the ions are. The toggle governs conductors,
    /// which are genuinely solid.
    /// </para>
    /// <para>
    /// <b>Viridis by decade, not by level.</b> A density is sequential, so it gets the
    /// sequential ramp - and the position on it comes from how many decades down the shell
    /// is rather than from the density itself, because the levels span orders of magnitude
    /// and a linear position would put all three at one end.
    /// </para>
    /// </remarks>
    private void DrawDensity(ViewportViewModel viewport)
    {
        if (viewport.Density.Count == 0)
        {
            return;
        }

        var count = viewport.Density.Count;

        foreach (var shell in viewport.Density)
        {
            if (shell.Triangles.Count == 0)
            {
                continue;
            }

            var positions = new Vector3Collection(shell.VerticesMm.Count / 3);
            var normals = new Vector3Collection(shell.VerticesMm.Count / 3);

            for (var v = 0; v + 2 < shell.VerticesMm.Count; v += 3)
            {
                positions.Add(new Vector3(
                    (float)shell.VerticesMm[v],
                    (float)shell.VerticesMm[v + 1],
                    (float)shell.VerticesMm[v + 2]));

                normals.Add(new Vector3(
                    (float)shell.Normals[v],
                    (float)shell.Normals[v + 1],
                    (float)shell.Normals[v + 2]));
            }

            // Brightest at the core and dimmer outward, so the eye reads concentration -
            // the same argument the section's line weights make, in colour.
            var (r, g, b) = ColourRamp.At(
                count > 1 ? 1.0 - ((shell.DecadesBelowPeak - 1.0) / (count - 1.0)) : 1.0);

            // Fainter outward too. An outer shell drawn as solidly as the core hides it
            // and says the packet is where its tail is.
            var opacity = (float)(0.46 - (0.10 * (shell.DecadesBelowPeak - 1)));

            Viewport.Items.Add(new MeshGeometryModel3D
            {
                Geometry = new MeshGeometry3D
                {
                    Positions = positions,
                    Normals = normals,
                    Indices = new IntCollection(shell.Triangles),
                    TextureCoordinates = null,
                },

                Material = new PhongMaterial
                {
                    DiffuseColor = new Color4(
                        (float)r, (float)g, (float)b, Math.Max(opacity, 0.12f)),
                    SpecularColor = new Color4(0.10f, 0.10f, 0.10f, 1f),
                    SpecularShininess = 8f,
                },

                IsTransparent = true,

                // Both faces: a shell is looked into from outside, and the far wall of one
                // is part of what says how deep the packet is.
                CullMode = SharpDX.Direct3D11.CullMode.None,
            });
        }
    }

    /// <summary>The launch point and the detector plane, so the picture has a start and a finish.</summary>
    /// <remarks>
    /// <para>
    /// <b>A marker for the source and a quad for the detector</b>, both sized from the
    /// instrument rather than in fixed millimetres, because a marker that reads on a 600 mm
    /// analyser is invisible on a 20 mm lens. The size comes from the command layer, where
    /// the extent is already known.
    /// </para>
    /// <para>
    /// <b>Both are drawing conventions and not dimensions</b> (GRD-12). A source is a point
    /// and a detector is an unbounded plane; what is drawn has a size chosen to be seen, and
    /// a reader must not take the quad's edges for the detector's extent. The launch arrow is
    /// drawn because direction is the half of a source a point cannot show - and on an
    /// analyser whose ions reverse, it is the only thing distinguishing the end they leave
    /// from the end they return to.
    /// </para>
    /// <para>
    /// <b>Built by hand rather than with a mesh library helper.</b> Two solids of a dozen
    /// triangles each are less code than establishing which of a toolkit's namespaces holds
    /// its builder, and they carry no version question.
    /// </para>
    /// </remarks>
    private void DrawEnds(ViewportViewModel viewport)
    {
        if (viewport.Ends is not { } ends)
        {
            return;
        }

        var span = (float)ends.SpanMm;

        var source = new Vector3(
            (float)ends.SourceMm[0], (float)ends.SourceMm[1], (float)ends.SourceMm[2]);

        var along = Vector3.Normalize(new Vector3(
            (float)ends.LaunchDirection[0],
            (float)ends.LaunchDirection[1],
            (float)ends.LaunchDirection[2]));

        // An octahedron at the launch point and a spike along the launch direction: the
        // point says where, the spike says which way.
        var marker = Solid(
            [
                source + new Vector3(span * 0.4f, 0, 0), source - new Vector3(span * 0.4f, 0, 0),
                source + new Vector3(0, span * 0.4f, 0), source - new Vector3(0, span * 0.4f, 0),
                source + new Vector3(0, 0, span * 0.4f), source - new Vector3(0, 0, span * 0.4f),
                source + (along * span * 2.0f),
            ],
            [
                0, 2, 4,  2, 1, 4,  1, 3, 4,  3, 0, 4,
                2, 0, 5,  1, 2, 5,  3, 1, 5,  0, 3, 5,
                0, 2, 6,  2, 1, 6,  1, 3, 6,  3, 0, 6,
            ]);

        Viewport.Items.Add(new MeshGeometryModel3D
        {
            Geometry = marker,
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.13f, 0.42f, 0.18f, 1f),
                SpecularColor = new Color4(0.25f, 0.25f, 0.25f, 1f),
                SpecularShininess = 24f,
            },
            CullMode = SharpDX.Direct3D11.CullMode.None,
        });

        // The detector plane as a square about its declared point. The two in-plane axes
        // come from whichever world axis is least parallel to the normal, so the cross
        // product never degenerates.
        var normal = Vector3.Normalize(new Vector3(
            (float)ends.DetectorNormal[0],
            (float)ends.DetectorNormal[1],
            (float)ends.DetectorNormal[2]));

        var seed = Math.Abs(normal.X) < 0.9f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
        var u = Vector3.Normalize(Vector3.Cross(normal, seed));
        var v = Vector3.Cross(normal, u);

        var centre = new Vector3(
            (float)ends.DetectorMm[0], (float)ends.DetectorMm[1], (float)ends.DetectorMm[2]);

        var half = span * 1.4f;

        Viewport.Items.Add(new MeshGeometryModel3D
        {
            Geometry = Solid(
                [
                    centre - (u * half) - (v * half),
                    centre + (u * half) - (v * half),
                    centre + (u * half) + (v * half),
                    centre - (u * half) + (v * half),
                ],
                [0, 1, 2, 0, 2, 3]),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.16f, 0.34f, 0.70f, 0.45f),
                SpecularColor = new Color4(0.20f, 0.20f, 0.20f, 1f),
                SpecularShininess = 16f,
            },
            IsTransparent = true,
            CullMode = SharpDX.Direct3D11.CullMode.None,
        });
    }

    /// <summary>A marker where each ion stopped, coloured by what stopped it.</summary>
    /// <remarks>
    /// <para>
    /// <b>The fate was computed and only ever shown as text.</b> A line reading "1 arrived"
    /// under the viewport is true and does not answer the question a viewer is actually
    /// asking, which is whether <em>that</em> path reached <em>that</em> plane — and on an
    /// analyser whose ions reverse and come back past their own launch point, the end of a
    /// trajectory is genuinely hard to find by eye.
    /// </para>
    /// <para>
    /// <b>Green for arrived, red for struck, amber for anything else</b> — which is almost
    /// always the flight-time ceiling, and is neither a success nor a collision. Three
    /// outcomes and three colours, because collapsing the third into either of the others is
    /// how "the run stopped early" comes to look like "the ion was confined". The detector
    /// plane is blue rather than red for the same reason: red means an ion hit something.
    /// </para>
    /// </remarks>
    private void DrawFates(ViewportViewModel viewport)
    {
        if (viewport.Ends is not { } ends)
        {
            return;
        }

        var span = (float)ends.SpanMm * 0.30f;

        foreach (var path in viewport.Trajectories)
        {
            if (path.PointsMm.Count == 0)
            {
                continue;
            }

            var last = path.PointsMm[^1];
            var at = new Vector3((float)last[0], (float)last[1], (float)last[2]);

            var colour = path.Fate switch
            {
                "arrived" => new Color4(0.11f, 0.60f, 0.22f, 1f),
                "MaximumFlightTimeReached" => new Color4(0.85f, 0.60f, 0.10f, 1f),
                "StepSizeUnderflow" or "StepBudgetExhausted" => new Color4(0.85f, 0.60f, 0.10f, 1f),
                _ => new Color4(0.75f, 0.14f, 0.14f, 1f),
            };

            Viewport.Items.Add(new MeshGeometryModel3D
            {
                Geometry = Solid(
                    [
                        at + new Vector3(span, 0, 0), at - new Vector3(span, 0, 0),
                        at + new Vector3(0, span, 0), at - new Vector3(0, span, 0),
                        at + new Vector3(0, 0, span), at - new Vector3(0, 0, span),
                    ],
                    [
                        0, 2, 4,  2, 1, 4,  1, 3, 4,  3, 0, 4,
                        2, 0, 5,  1, 2, 5,  3, 1, 5,  0, 3, 5,
                    ]),
                Material = new PhongMaterial
                {
                    DiffuseColor = colour,
                    SpecularColor = new Color4(0.25f, 0.25f, 0.25f, 1f),
                    SpecularShininess = 24f,
                },
                CullMode = SharpDX.Direct3D11.CullMode.None,
            });
        }
    }

    /// <summary>A mesh from positions and triangles, with normals from the faces.</summary>
    /// <remarks>
    /// Per-vertex normals accumulated from the faces that share the vertex, which is what
    /// makes a marker read as a solid rather than as a flat silhouette. A degenerate face
    /// contributes nothing rather than a zero-length normal, since normalising one of those
    /// paints the vertex black.
    /// </remarks>
    private static MeshGeometry3D Solid(Vector3[] positions, int[] triangles)
    {
        var normals = new Vector3[positions.Length];

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var (a, b, c) = (triangles[i], triangles[i + 1], triangles[i + 2]);
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);

            if (face.LengthSquared() <= 0.0f)
            {
                continue;
            }

            face = Vector3.Normalize(face);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }

        var built = new Vector3Collection();
        var facing = new Vector3Collection();

        for (var i = 0; i < positions.Length; i++)
        {
            built.Add(positions[i]);
            facing.Add(
                normals[i].LengthSquared() > 0.0f
                    ? Vector3.Normalize(normals[i])
                    : new Vector3(0, 1, 0));
        }

        return new MeshGeometry3D
        {
            Positions = built,
            Normals = facing,
            Indices = new IntCollection(triangles),
            TextureCoordinates = null,
        };
    }

   private void DrawConductors(ViewportViewModel viewport)
    {
        if (ShowElectrodes.IsChecked != true)
        {
            return;
        }

        var seeThrough = Transparent.IsChecked == true;

        foreach (var conductor in viewport.Conductors)
        {
            if (conductor.Triangles.Count == 0)
            {
                continue;
            }

            var positions = new Vector3Collection(conductor.VerticesMm.Count / 3);
            var normals = new Vector3Collection(conductor.VerticesMm.Count / 3);

            for (var v = 0; v + 2 < conductor.VerticesMm.Count; v += 3)
            {
                positions.Add(new Vector3(
                    (float)conductor.VerticesMm[v],
                    (float)conductor.VerticesMm[v + 1],
                    (float)conductor.VerticesMm[v + 2]));

                normals.Add(new Vector3(
                    (float)conductor.Normals[v],
                    (float)conductor.Normals[v + 1],
                    (float)conductor.Normals[v + 2]));
            }

            // The peak its drive reaches, not the DC it sits at. A quadrupole's rods hold
            // zero volts of DC and all of their potential as drive, so colouring by the DC
            // alone paints a mass filter as an earthed box - a mistake this project has
            // made five times in other places. The amplitude is signed, so adding it
            // separates the two phases rather than collapsing them.
            var (r, g, b) = ColourRamp.Diverging(
                viewport.Potential(conductor.PotentialVolts + conductor.DriveAmplitudeVolts));

            Viewport.Items.Add(new MeshGeometryModel3D
            {
                Geometry = new MeshGeometry3D
                {
                    Positions = positions,
                    Normals = normals,
                    Indices = new IntCollection(conductor.Triangles),
                    TextureCoordinates = null,
                },

                Material = new PhongMaterial
                {
                    DiffuseColor = new Color4(
                        (float)r, (float)g, (float)b, seeThrough ? 0.62f : 1.0f),
                    SpecularColor = new Color4(0.30f, 0.30f, 0.30f, 1f),
                    SpecularShininess = 24f,
                },

                IsTransparent = seeThrough,

                // Both faces, because a cross-section's prism is deliberately open at its
                // ends and the inside of a ring is a surface a reader looks straight at.
                CullMode = SharpDX.Direct3D11.CullMode.None,
            });
        }
    }

    /// <summary>Equipotentials on the section plane, coloured by their level.</summary>
    private void DrawField(ViewportViewModel viewport)
    {
        if (ShowField.IsChecked != true)
        {
            return;
        }

        var positions = new Vector3Collection();
        var indices = new IntCollection();
        var colours = new Color4Collection();

        foreach (var level in viewport.Equipotentials)
        {
            var (r, g, b) = ColourRamp.Diverging(viewport.Potential(level.PotentialVolts));

            foreach (var path in level.PathsMm)
            {
                for (var i = 0; i + 2 < path.Count; i += 3)
                {
                    positions.Add(new Vector3(
                        (float)path[i], (float)path[i + 1], (float)path[i + 2]));

                    colours.Add(new Color4((float)r, (float)g, (float)b, 1f));

                    if (i > 0)
                    {
                        indices.Add(positions.Count - 2);
                        indices.Add(positions.Count - 1);
                    }
                }
            }
        }

        if (positions.Count > 0)
        {
            Viewport.Items.Add(new LineGeometryModel3D
            {
                Geometry = new LineGeometry3D
                {
                    Positions = positions,
                    Indices = indices,
                    Colors = colours,
                },
                Color = Colors.White,
                Thickness = 0.6,
            });
        }
    }

    /// <summary>The trajectory bundle, coloured by energy.</summary>
    private void DrawPaths(ViewportViewModel viewport)
    {
        if (ShowPaths.IsChecked != true)
        {
            return;
        }

        var positions = new Vector3Collection();
        var indices = new IntCollection();
        var colours = new Color4Collection();

        foreach (var path in viewport.Trajectories)
        {
            for (var i = 0; i < path.PointsMm.Count; i++)
            {
                var point = path.PointsMm[i];

                positions.Add(new Vector3(
                    (float)point[0], (float)point[1], (float)point[2]));

                var (r, g, b) = ColourRamp.At(viewport.Fraction(path.EnergyEv[i]));

                colours.Add(new Color4((float)r, (float)g, (float)b, 1f));

                if (i > 0)
                {
                    indices.Add(positions.Count - 2);
                    indices.Add(positions.Count - 1);
                }
            }
        }

        if (positions.Count > 0)
        {
            Viewport.Items.Add(new LineGeometryModel3D
            {
                Geometry = new LineGeometry3D
                {
                    Positions = positions,
                    Indices = indices,
                    Colors = colours,
                },

                // White, because the shader multiplies it by the per-vertex colour and
                // anything else would tint the whole scale.
                Color = Colors.White,
                Thickness = 1.2,
            });
        }
    }

    /// <summary>The two colour scales, shown only where they mean something.</summary>
    private void Scales(ViewportViewModel viewport)
    {
        ScaleBar.Visibility = viewport.HasBundle ? Visibility.Visible : Visibility.Collapsed;
        EnergyLabel.Visibility = ScaleBar.Visibility;

        ScaleLow.Text = viewport.HasBundle ? Number(viewport.LowestEnergyEv) : string.Empty;
        ScaleHigh.Text = viewport.HasBundle
            ? Number(viewport.HighestEnergyEv) + " eV"
            : string.Empty;

        if (viewport.HasBundle)
        {
            ScaleSwatch.Fill = RampBrush(ColourRamp.At);
        }

        VoltsBar.Visibility = viewport.HasField ? Visibility.Visible : Visibility.Collapsed;
        PotentialLabel.Visibility = VoltsBar.Visibility;

        // The ends of the scale the colours are on, which is symmetric about earth rather
        // than the observed range - a legend showing the range would not describe the
        // picture.
        var widest = Math.Max(
            Math.Abs(viewport.LowestPotentialVolts), Math.Abs(viewport.HighestPotentialVolts));

        VoltsLow.Text = viewport.HasField ? Number(-widest) : string.Empty;
        VoltsHigh.Text = viewport.HasField ? Number(widest) + " V" : string.Empty;

        if (viewport.HasField)
        {
            VoltsSwatch.Fill = RampBrush(ColourRamp.Diverging);
        }
    }

    /// <summary>What the picture cannot be read without (GRD-2, RND-8).</summary>
    /// <remarks>
    /// The warnings say it first when they say it at all: <c>render.no-trajectories</c> is
    /// the engine's own words for why a diffusive model has no paths, and printing a second
    /// paraphrase above it reads as two separate problems. The summary is the fallback for
    /// a bundle that is missing with nothing on the record - which ViewportCommand does not
    /// currently produce, and that is exactly why this must not depend on its not doing so.
    /// </remarks>
    private void Notes(ViewportViewModel viewport)
    {
        var notes = new List<string>(viewport.Warnings);

        if (!viewport.HasBundle && notes.Count == 0)
        {
            notes.Add(viewport.Status);
        }

        ViewportNoteText.Text = string.Join(Environment.NewLine, notes);
        ViewportNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A number for a scale end, in as few characters as carry it.</summary>
    private static string Number(double value) =>
        value.ToString(Math.Abs(value) >= 1000.0 ? "G4" : "G3", CultureInfo.InvariantCulture);

    /// <summary>A colour scale as a brush, for the legend.</summary>
    private static LinearGradientBrush RampBrush(Func<double, (double R, double G, double B)> ramp)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };

        for (var i = 0; i <= 16; i++)
        {
            var fraction = i / 16.0;
            var (r, g, b) = ramp(fraction);

            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)), fraction));
        }

        return brush;
    }

    /// <summary>Puts the camera on a named view, or frames what is drawn.</summary>
    /// <remarks>
    /// <b>Named views rather than only gestures.</b> Rotate, pan and zoom have worked from
    /// the first version and nothing said so; but for an instrument the named views matter
    /// more than the gestures, because ion optics is read as an axial section and two
    /// transverse ones, and getting to one by dragging is approximate where a button is
    /// exact.
    /// </remarks>
    private void OnView(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string which })
        {
            SetView(which);
        }
    }

    /// <summary>Puts the camera on a named view, or frames what is drawn.</summary>
    /// <remarks>
    /// One place decides where the camera points, called by the buttons and by the
    /// constructor alike - the alternative is a starting view set in one place and named
    /// views set in another, which is how a default ends up being whatever happened rather
    /// than something anyone chose.
    /// </remarks>
    private void SetView(string which)
    {
        if (which != "fit")
        {
            // A whole camera, assigned at once, rather than three properties written into
            // the live one. Each property raises its own change notification, so the
            // control sees a camera whose look and up are momentarily inconsistent and
            // re-derives one of them - which is why the first named view after startup
            // came out as the top view whichever button was pressed.
            Viewport.Camera = Camera(which);
        }

        // A frame later, because the control computes the scene bounds while it renders -
        // so fitting in the same breath as assigning a camera fits to whatever bounds it
        // had before. On a quadrupole that meant framing the 32 mm cross-section and
        // running the 420 mm of rod off both edges. Without animation, so the camera the
        // next line of code sees is the one on screen.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () => Viewport.ZoomExtents(0.0));
    }

    /// <summary>A camera for a named view.</summary>
    /// <remarks>
    /// <para>
    /// Orthographic. A perspective camera foreshortens the drift, and an ion-optics drawing
    /// is read for where things are along the axis - the one thing perspective distorts.
    /// </para>
    /// <para>
    /// The distance is irrelevant to an orthographic camera's framing - the width sets
    /// that, and ZoomExtents sets the width. It only has to be far enough out that nothing
    /// is behind the eye.
    /// </para>
    /// <para>
    /// <b>Oblique is the default, and chosen rather than fallen into.</b> A straight-on
    /// section is how ion optics is usually drawn and is one click away, but it is also
    /// exactly the view in which a ring and a rectangle look the same - so the first thing
    /// a person sees should be the one that says the geometry is three-dimensional.
    /// </para>
    /// </remarks>
    private static OrthographicCamera Camera(string which)
    {
        var (look, up) = which switch
        {
            "side" => (new Vector3D(0, 0, -1), new Vector3D(0, 1, 0)),
            "top" => (new Vector3D(0, -1, 0), new Vector3D(0, 0, 1)),
            "front" => (new Vector3D(-1, 0, 0), new Vector3D(0, 1, 0)),
            _ => (new Vector3D(-1, -0.55, -0.8), new Vector3D(0, 1, 0)),
        };

        const double Far = 1000.0;

        return new OrthographicCamera
        {
            Position = new Point3D(-look.X * Far, -look.Y * Far, -look.Z * Far),
            LookDirection = look * Far,
            UpDirection = up,
            NearPlaneDistance = -100_000,
            FarPlaneDistance = 100_000,
        };
    }

    /// <summary>Shows the model tree, the journal and whether the model validates.</summary>
    private void Show(ModelTreeViewModel? tree)
    {
        if (tree is null)
        {
            return;
        }

        ParameterGrid.ItemsSource = tree.Parameters;
        JournalList.ItemsSource = tree.Journal;
        CommandList.ItemsSource = tree.Commands;

        StatusText.Text = tree.Status;

        // Visible without opening anything. A person who has to look for the fact that
        // their model no longer validates is a person who will not.
        StatusBar.Background = tree.Valid
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(0xF6, 0xE0, 0xE0));
    }

    /// <summary>Runs or previews the model and reads its figures by class.</summary>
    /// <remarks>
    /// A run is not instant - it is the same work <c>einzel run</c> does - so the button
    /// is a button rather than something that happens on opening the tab. Section 16's
    /// results view is a thing a person asks for, not a thing that happens to them while
    /// they are editing a parameter.
    /// </remarks>
    private void OnResults(object sender, RoutedEventArgs e)
    {
        if (_results is null || sender is not System.Windows.Controls.Button { Tag: string tier })
        {
            return;
        }

        Cursor = System.Windows.Input.Cursors.Wait;

        try
        {
            _results.Refresh(preview: tier == "preview");
        }
        finally
        {
            Cursor = null;
        }

        ResultsStatus.Text = _results.Status;

        // GRD-5's taint, where it cannot be missed rather than in a column somebody may
        // have scrolled past.
        ResultsTaint.Visibility = _results.Tainted ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Walks the path and reads the regime at each step.</summary>
    private void OnRegime(object sender, RoutedEventArgs e)
    {
        if (_regime is null)
        {
            return;
        }

        Cursor = System.Windows.Input.Cursors.Wait;

        try
        {
            _regime.Refresh();
        }
        finally
        {
            Cursor = null;
        }

        RegimeStatus.Text = _regime.Status;
    }

    /// <summary>Re-reads the project.</summary>
    private void OnProject(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        _project.Refresh();

        ProjectStatus.Text = _project.Status;
    }

    /// <summary>Reads the declared timeline.</summary>
    private void OnSequence(object sender, RoutedEventArgs e)
    {
        if (_sequence is null)
        {
            return;
        }

        _sequence.Refresh();

        SequenceStatus.Text = _sequence.Status;
    }

    /// <summary>Turns a layer on or off and redraws.</summary>
    private void OnLayer(object sender, RoutedEventArgs e) => Draw(_viewport);

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_tree is null
            || e.EditAction != DataGridEditAction.Commit
            || e.Row.Item is not ParameterRow row
            || e.EditingElement is not TextBox box)
        {
            return;
        }

        if (!row.Editable)
        {
            // A derived parameter's value is its expression's, so there is nothing to
            // set. Said rather than silently ignored.
            StatusText.Text =
                $"{row.Name} is derived, so its value is its expression's - "
                + "set one of the parameters the expression is over";

            e.Cancel = true;

            return;
        }

        if (!double.TryParse(
            box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            StatusText.Text =
                $"'{box.Text}' is not a number. The value is in {row.Unit}, so write the "
                + "magnitude alone";

            e.Cancel = true;

            return;
        }

        // A refusal leaves the model as it was, so the cell must not keep what was
        // typed - two different values for one parameter with nothing saying which is
        // real is exactly what a model tree exists to prevent. `Status` already carries
        // AGT-3's explanation by the time this returns.
        if (!_tree.Set(row.Name, value))
        {
            e.Cancel = true;
        }

        // Re-read rather than trust the grid: an edit moves every derived parameter that
        // depends on it, which is the whole reason a model has a parameter surface.
        Dispatcher.BeginInvoke(() =>
        {
            Show(_tree);

            // The bundle is redrawn too: watching the paths move is the reason to change
            // a parameter with the window open rather than in a text editor.
            Draw(_viewport);
        });
    }
}
