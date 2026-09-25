namespace Einzel.Commands;

/// <summary>
/// The viewport's camera over one scene: which named view it looks from, and a frame that
/// holds everything drawn, including what a watched run has drifted into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Here rather than in the window's GL control, because the control cannot be tested.</b>
/// The first version of this bookkeeping lived in <c>SceneView</c> and unioned the frame
/// fitted for a new view with the frame of the previous view. The two are boxes in different
/// view coordinates, so the union carried the old box's rotated corners across: the frame
/// never shrank and grew on every click of a named view. A cube filled about half the frame
/// after one iso-to-side switch and less after each round trip. Nothing caught it, because
/// the only code that could was inside a control that needs a GL context.
/// </para>
/// <para>
/// <b>What a watch adds is kept as a box in the model's own coordinates</b>, which does not
/// depend on the view. Turning the camera fits the scene afresh at the new angles and takes
/// that box in at those same angles, so switching views and back gives the frame you started
/// with. Within one view a new frame of a watch is taken in exactly, since two boxes at the
/// same angles have a union that is a box.
/// </para>
/// <para>
/// Locked, because a named view is chosen on the UI thread and a watched frame arrives on the
/// render thread.
/// </para>
/// </remarks>
public sealed class ViewportCamera
{
    private readonly Lock _gate = new();
    private readonly ViewportOutcome _scene;

    private (double Azimuth, double Elevation) _view;
    private Framing _framing;
    private ((double X, double Y, double Z) Low, (double X, double Y, double Z) High)? _watched;

    /// <summary>Frames a scene from a view.</summary>
    /// <param name="scene">What the command layer measured.</param>
    /// <param name="view">Azimuth and elevation, in degrees.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public ViewportCamera(ViewportOutcome scene, (double Azimuth, double Elevation) view)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _scene = scene;
        _view = view;
        _framing = Framing.Measure(scene, view.Azimuth, view.Elevation);
    }

    /// <summary>The view the camera looks from, in degrees of azimuth and elevation.</summary>
    public (double Azimuth, double Elevation) View
    {
        get
        {
            lock (_gate)
            {
                return _view;
            }
        }
    }

    /// <summary>The frame, at the current view.</summary>
    public Framing Framing
    {
        get
        {
            lock (_gate)
            {
                return _framing;
            }
        }
    }

    /// <summary>Turns the camera to another view and fits the frame to what it sees.</summary>
    /// <param name="view">Azimuth and elevation, in degrees.</param>
    /// <returns>The new frame.</returns>
    public Framing Turn((double Azimuth, double Elevation) view)
    {
        lock (_gate)
        {
            _view = view;

            var fitted = Framing.Measure(_scene, view.Azimuth, view.Elevation);

            _framing = _watched is { } box
                ? fitted.Union(Framing.Around(Corners(box), view.Azimuth, view.Elevation))
                : fitted;

            return _framing;
        }
    }

    /// <summary>Takes in a frame of a run that is still going.</summary>
    /// <param name="frame">The run as it stands.</param>
    /// <returns>The frame, grown to hold it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <remarks>
    /// The frame only grows. A packet that drifts out of the box it was framed in leaves an
    /// empty viewport, and re-measuring per frame would make the camera breathe with the
    /// packet instead of letting the packet move.
    /// </remarks>
    public Framing Take(ViewportOutcome frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            foreach (var (x, y, z) in Framing.Points(frame))
            {
                _watched = _watched is { } box
                    ? ((Math.Min(box.Low.X, x), Math.Min(box.Low.Y, y), Math.Min(box.Low.Z, z)),
                       (Math.Max(box.High.X, x), Math.Max(box.High.Y, y), Math.Max(box.High.Z, z)))
                    : ((x, y, z), (x, y, z));
            }

            _framing = _framing.Union(Framing.Measure(frame, _view.Azimuth, _view.Elevation));

            return _framing;
        }
    }

    private static IEnumerable<(double X, double Y, double Z)> Corners(
        ((double X, double Y, double Z) Low, (double X, double Y, double Z) High) box)
    {
        foreach (var x in (double[])[box.Low.X, box.High.X])
        {
            foreach (var y in (double[])[box.Low.Y, box.High.Y])
            {
                foreach (var z in (double[])[box.Low.Z, box.High.Z])
                {
                    yield return (x, y, z);
                }
            }
        }
    }
}
