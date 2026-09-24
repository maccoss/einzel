using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>
/// Where the camera sits and how much of the instrument it holds.
/// </summary>
/// <param name="CenterMm">The middle of everything drawn, in millimetres.</param>
/// <param name="RadiusMm">Half the diagonal of what is drawn, in millimetres.</param>
/// <param name="Azimuth">Turn about the vertical, in degrees. Zero is the side view.</param>
/// <param name="Elevation">Tilt above the horizontal, in degrees.</param>
/// <remarks>
/// <para>
/// <b>Orthographic, not perspective, and that is a decision rather than a simplification.</b>
/// An ion-optics drawing is read for where things are <em>along the axis</em> - a mirror's
/// penetration depth, where a packet turns round, how far a slit is from the rod's back -
/// and perspective is precisely what distorts that. The WPF viewport opened perspective by
/// default and the axial positions could not be read off it.
/// </para>
/// <para>
/// Measured once, from the scene. The projection is rebuilt per frame because it depends on
/// the window's shape, but the instrument's own extent does not change under a resize.
/// </para>
/// </remarks>
public sealed record Framing(
    (double X, double Y, double Z) CenterMm,
    double RadiusMm,
    double Azimuth,
    double Elevation)
{
    /// <summary>Measures a scene, including its trajectories.</summary>
    /// <param name="scene">What the command layer produced.</param>
    /// <param name="azimuth">Turn about the vertical, in degrees.</param>
    /// <param name="elevation">Tilt above the horizontal, in degrees.</param>
    /// <returns>A framing that holds everything drawn.</returns>
    /// <remarks>
    /// <para>
    /// <b>The flight is measured too, not only the metal.</b> A reflectron's ion turns round
    /// outside every conductor, and a page chosen from the conductors alone puts the turning
    /// point off it - which is how the scaffolded model once drew its turning point a hundred
    /// metres off a 160 mm page.
    /// </para>
    /// <para>
    /// <b>And so is the density, for the same reason one dimension over.</b> A diffusive model
    /// has no trajectories by construction (RND-8), and it need not have electrodes either -
    /// a drift tube in a declared uniform field has neither. Measured from conductors and
    /// paths alone such a scene is empty, the framing falls back to a millimetre at the
    /// origin, and the packet is drawn entirely outside the frustum: a blank viewport for
    /// exactly the model class the watch exists to serve.
    /// </para>
    /// </remarks>
    public static Framing Measure(ViewportOutcome scene, double azimuth, double elevation)
    {
        ArgumentNullException.ThrowIfNull(scene);

        double lowX = double.MaxValue, lowY = double.MaxValue, lowZ = double.MaxValue;
        double highX = double.MinValue, highY = double.MinValue, highZ = double.MinValue;
        var seen = false;

        void Take(double x, double y, double z)
        {
            seen = true;
            lowX = Math.Min(lowX, x); highX = Math.Max(highX, x);
            lowY = Math.Min(lowY, y); highY = Math.Max(highY, y);
            lowZ = Math.Min(lowZ, z); highZ = Math.Max(highZ, z);
        }

        foreach (var conductor in scene.Conductors)
        {
            for (var i = 0; i + 2 < conductor.VerticesMm.Count; i += 3)
            {
                Take(conductor.VerticesMm[i], conductor.VerticesMm[i + 1], conductor.VerticesMm[i + 2]);
            }
        }

        foreach (var path in scene.Trajectories)
        {
            foreach (var point in path.PointsMm)
            {
                if (point.Count >= 3)
                {
                    Take(point[0], point[1], point[2]);
                }
            }
        }

        // The outermost contour is the packet's own extent, so measuring every shell and
        // measuring the widest give the same box - but a frame of a run still going may not
        // carry the widest, and the cost is a pass over vertices already in memory.
        foreach (var shell in scene.Density)
        {
            for (var i = 0; i + 2 < shell.VerticesMm.Count; i += 3)
            {
                Take(shell.VerticesMm[i], shell.VerticesMm[i + 1], shell.VerticesMm[i + 2]);
            }
        }

        if (!seen)
        {
            return new Framing((0.0, 0.0, 0.0), 1.0, azimuth, elevation);
        }

        var center = ((lowX + highX) / 2.0, (lowY + highY) / 2.0, (lowZ + highZ) / 2.0);
        var radius = 0.5 * Math.Sqrt(
            ((highX - lowX) * (highX - lowX))
            + ((highY - lowY) * (highY - lowY))
            + ((highZ - lowZ) * (highZ - lowZ)));

        return new Framing(center, Math.Max(radius, 1e-6), azimuth, elevation);
    }

    /// <summary>A framing that holds this one and another.</summary>
    /// <param name="other">The framing to take in.</param>
    /// <returns>The smallest framing containing both, at this one's camera angles.</returns>
    /// <remarks>
    /// <para>
    /// <b>A watched packet moves, and the frame is measured before it does.</b> A TIMS
    /// elution is seeded near the entrance and elutes forty millimetres away, so a camera
    /// framed from the first instant loses the packet it was opened to watch. Re-measuring
    /// per frame is the obvious answer and is worse: the box would breathe with the packet,
    /// which reads as a camera lurching rather than as a packet drifting.
    /// </para>
    /// <para>
    /// So the frame only ever grows. The bounding sphere of two spheres is exact, which is
    /// what makes this composable without drifting outward on repeated application - a union
    /// of boxes taken through centre and radius would not be.
    /// </para>
    /// </remarks>
    public Framing Union(Framing other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var (ax, ay, az) = CenterMm;
        var (bx, by, bz) = other.CenterMm;

        double dx = bx - ax, dy = by - ay, dz = bz - az;
        var apart = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        // One already contains the other, including the degenerate case of equal centers.
        if (apart + other.RadiusMm <= RadiusMm)
        {
            return this;
        }

        if (apart + RadiusMm <= other.RadiusMm)
        {
            return new Framing(other.CenterMm, other.RadiusMm, Azimuth, Elevation);
        }

        var radius = (RadiusMm + other.RadiusMm + apart) / 2.0;
        var along = (radius - RadiusMm) / apart;

        return new Framing(
            (ax + (dx * along), ay + (dy * along), az + (dz * along)),
            radius,
            Azimuth,
            Elevation);
    }

    /// <summary>The identity, for a control with nothing to draw.</summary>
    /// <returns>A column-major 4x4 identity.</returns>
    public static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    /// <summary>The model-view-projection matrix for a viewport of this shape.</summary>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>A column-major 4x4, as OpenGL wants it.</returns>
    public float[] Project(double aspect)
    {
        var a = Azimuth * Math.PI / 180.0;
        var e = Elevation * Math.PI / 180.0;

        double ca = Math.Cos(a), sa = Math.Sin(a);
        double ce = Math.Cos(e), se = Math.Sin(e);

        // Rotate about y by the azimuth, then about x by the elevation. Written out rather
        // than multiplied at run time: three rows is shorter than a matrix library, and the
        // sign of every term is visible.
        double r00 = ca, r01 = 0.0, r02 = -sa;
        double r10 = se * sa, r11 = ce, r12 = se * ca;
        double r20 = ce * sa, r21 = -se, r22 = ce * ca;

        // A tenth of margin, so the instrument does not touch the edge of the frame.
        var half = RadiusMm * 1.1;
        var halfX = aspect >= 1.0 ? half * aspect : half;
        var halfY = aspect >= 1.0 ? half : half / aspect;

        // Depth spans the whole sphere either way, so nothing clips whatever the turn.
        var depth = RadiusMm * 4.0;

        double sx = 1.0 / halfX, sy = 1.0 / halfY, sz = -1.0 / depth;

        var (cx, cy, cz) = CenterMm;

        // Column-major: element [column * 4 + row].
        return
        [
            (float)(sx * r00), (float)(sy * r10), (float)(sz * r20), 0f,
            (float)(sx * r01), (float)(sy * r11), (float)(sz * r21), 0f,
            (float)(sx * r02), (float)(sy * r12), (float)(sz * r22), 0f,
            (float)(-sx * ((r00 * cx) + (r01 * cy) + (r02 * cz))),
            (float)(-sy * ((r10 * cx) + (r11 * cy) + (r12 * cz))),
            (float)(-sz * ((r20 * cx) + (r21 * cy) + (r22 * cz))),
            1f,
        ];
    }
}
