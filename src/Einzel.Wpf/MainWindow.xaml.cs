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

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Viewport.EffectsManager = new DefaultEffectsManager();

        // Orthographic, looking along -z with x across and y up, which is how every
        // instrument drawing in the memo and in the literature is laid out. A
        // perspective camera foreshortens the drift, and an ion-optics drawing is read
        // for where things are along the axis - the one thing perspective distorts.
        Viewport.Camera = new OrthographicCamera
        {
            Position = new Point3D(0, 0, 1000),
            LookDirection = new Vector3D(0, 0, -1000),
            UpDirection = new Vector3D(0, 1, 0),
            NearPlaneDistance = -100_000,
            FarPlaneDistance = 100_000,
        };

        Loaded += (_, _) =>
        {
            Show(_tree);
            Draw(_viewport);
        };
    }

    /// <summary>Opens a model in the window.</summary>
    /// <param name="tree">The tree over the session.</param>
    /// <param name="viewport">The viewport over the same session.</param>
    /// <exception cref="ArgumentNullException">Either is null.</exception>
    public void Open(ModelTreeViewModel tree, ViewportViewModel viewport)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(viewport);

        _tree = tree;
        _viewport = viewport;

        Show(tree);
        Draw(viewport);
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

    /// <summary>Draws the bundle, or says why there is none.</summary>
    /// <remarks>
    /// <para>
    /// <b>One line geometry for the whole bundle, not one per ion.</b> §16's reason for
    /// requiring a DirectX path is 10^4 trajectories drawn interactively, and 10^4 scene
    /// nodes is what makes that impossible - so every path goes into one vertex buffer
    /// with its own colours, and the scene holds a single model.
    /// </para>
    /// <para>
    /// The window computes nothing about the ions. It receives paths, positions and
    /// energies and turns them into vertices (UI-1).
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
                Thickness = 1.0,
            });

            // After layout, not during it. ZoomExtents needs the viewport's own size,
            // and at this point the control has been given a model but not yet measured -
            // so fitting here fits to whatever it was before, and the flight runs off the
            // edge. That is what it did.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                () => Viewport.ZoomExtents());
        }

        ScaleBar.Visibility = viewport.HasBundle ? Visibility.Visible : Visibility.Collapsed;

        if (viewport.HasBundle)
        {
            ScaleLow.Text = viewport.LowestEnergyEv.ToString("G4", CultureInfo.InvariantCulture);
            ScaleHigh.Text = viewport.HighestEnergyEv.ToString("G4", CultureInfo.InvariantCulture)
                + " eV";
            ScaleSwatch.Fill = RampBrush();
        }

        // The warnings and the reason there is no bundle share one place, because both
        // are things the picture cannot be read without (GRD-2, RND-8).
        //
        // The warnings say it first when they say it at all: `render.no-trajectories` is
        // the engine's own words for why a diffusive model has no paths, and printing a
        // second paraphrase above it reads as two separate problems. The summary is the
        // fallback for a bundle that is missing with nothing on the record - which
        // ViewportCommand does not currently produce, and that is exactly why this must
        // not depend on its not doing so.
        var notes = new List<string>(viewport.Warnings);

        if (!viewport.HasBundle && notes.Count == 0)
        {
            notes.Add(viewport.Status);
        }

        ViewportNoteText.Text = string.Join(Environment.NewLine, notes);
        ViewportNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The colour scale as a brush, for the legend.</summary>
    private static LinearGradientBrush RampBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };

        for (var i = 0; i <= 16; i++)
        {
            var fraction = i / 16.0;
            var (r, g, b) = ColourRamp.At(fraction);

            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)), fraction));
        }

        return brush;
    }

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
