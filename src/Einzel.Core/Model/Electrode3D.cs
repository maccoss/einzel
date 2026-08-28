namespace Einzel.Core.Model;

/// <summary>The shapes a three-dimensional electrode can take.</summary>
/// <remarks>
/// Three primitives, chosen because between them they build the devices the
/// specification's table asks for: a box is a plate, a segment wall or a housing; a
/// cylinder is a rod, a tube or a ring; a sphere is a bead or a rounded end. A
/// device that needs a fourth is a fair reason to add one, and a device that needs
/// arbitrary geometry is what mesh import is for.
/// </remarks>
public enum Electrode3DShape
{
    /// <summary>An axis-aligned rectangular box.</summary>
    Box,

    /// <summary>A sphere.</summary>
    Sphere,

    /// <summary>A capped cylinder along one coordinate axis.</summary>
    Cylinder,
}

/// <summary>Which coordinate axis a cylinder runs along.</summary>
public enum CylinderAxis
{
    /// <summary>Along x.</summary>
    X,

    /// <summary>Along y.</summary>
    Y,

    /// <summary>Along z.</summary>
    Z,
}

/// <summary>A three-dimensional electrode, validated and reduced to SI.</summary>
/// <remarks>
/// One record with a shape discriminator rather than a hierarchy, for the same
/// reason the two-dimensional one is: an unknown or misspelled shape produces a
/// single clear error naming the permitted values, instead of a deserialiser
/// exception naming a .NET type nobody has heard of.
/// </remarks>
public sealed record CompiledElectrode3D
{
    /// <summary>A name, used in reporting and as the basis-field label.</summary>
    public required string Name { get; init; }

    /// <summary>Which shape this is.</summary>
    public required Electrode3DShape Shape { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MinX { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MinY { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MinZ { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MaxX { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MaxY { get; init; }

    /// <summary>Box bounds, in metres.</summary>
    public double MaxZ { get; init; }

    /// <summary>Sphere or cylinder centre, in metres.</summary>
    public double CentreX { get; init; }

    /// <summary>Sphere or cylinder centre, in metres.</summary>
    public double CentreY { get; init; }

    /// <summary>Sphere or cylinder centre, in metres.</summary>
    public double CentreZ { get; init; }

    /// <summary>Sphere or cylinder radius, in metres.</summary>
    public double Radius { get; init; }

    /// <summary>Which axis a cylinder runs along.</summary>
    public CylinderAxis Axis { get; init; }

    /// <summary>Lower end of a cylinder along its axis, in metres.</summary>
    public double Lower { get; init; }

    /// <summary>Upper end of a cylinder along its axis, in metres.</summary>
    public double Upper { get; init; }

    /// <summary>The potential held, in volts. The DC part when driven.</summary>
    public double Potential { get; init; }

    /// <summary>Every generator this electrode is tapped off, in declaration order.</summary>
    public IReadOnlyList<CompiledTap> Taps { get; init; } = [];

    /// <summary>This electrode's share of the first drive it taps, in volts.</summary>
    public double DriveAmplitude => Taps.Count > 0 ? Taps[0].Amplitude : 0.0;

    /// <summary>Where in that drive's cycle this electrode sits, as a fraction of one.</summary>
    public double DrivePhase => Taps.Count > 0 ? Taps[0].Phase : 0.0;

    /// <summary>Whether this electrode's potential varies in time.</summary>
    public bool IsDriven => Taps.Any(t => t.Amplitude != 0.0);

    /// <summary>
    /// The smallest half-extent of this electrode, in metres.
    /// </summary>
    /// <remarks>
    /// What decides how far a multigrid hierarchy may coarsen before the electrode
    /// stops being represented. A conductor is representable while a cell is no
    /// larger than it is; past that the sub-cell machinery is still recording a
    /// surface, but on arms so short that the coefficients it produces are enormous
    /// and the coarse operator is ill-conditioned rather than merely coarse.
    /// </remarks>
    public double CharacteristicSize => Shape switch
    {
        Electrode3DShape.Sphere => Radius,
        Electrode3DShape.Cylinder => Math.Min(Radius, 0.5 * Math.Abs(Upper - Lower)),
        Electrode3DShape.Box => Math.Min(
            Math.Abs(MaxX - MinX),
            Math.Min(Math.Abs(MaxY - MinY), Math.Abs(MaxZ - MinZ))) * 0.5,
        _ => throw Unhandled(),
    };

    /// <summary>
    /// A point guaranteed to be inside this electrode, for a level too coarse to
    /// contain one of its nodes.
    /// </summary>
    /// <remarks>
    /// Its centre, which every one of these shapes is convex about. Used so that a
    /// coarse multigrid level can still say the electrode is <em>there</em>: an
    /// electrode that rasterises to no nodes at all has stopped being part of the
    /// problem, and the coarse grid then solves a different one.
    /// </remarks>
    public (double X, double Y, double Z) Centre => Shape switch
    {
        Electrode3DShape.Sphere => (CentreX, CentreY, CentreZ),

        Electrode3DShape.Box => (
            0.5 * (MinX + MaxX), 0.5 * (MinY + MaxY), 0.5 * (MinZ + MaxZ)),

        Electrode3DShape.Cylinder => Axis switch
        {
            CylinderAxis.X => (0.5 * (Lower + Upper), CentreY, CentreZ),
            CylinderAxis.Y => (CentreX, 0.5 * (Lower + Upper), CentreZ),
            _ => (CentreX, CentreY, 0.5 * (Lower + Upper)),
        },

        _ => throw Unhandled(),
    };

    /// <summary>
    /// The failure for a shape no member of this type knows about.
    /// </summary>
    /// <remarks>
    /// Every shape-dispatching member here names all three cases and throws on the
    /// rest, rather than letting one of them stand as a default. Defaults were how
    /// two of them came to disagree - the size switch fell through to a box and the
    /// centre switch to a sphere - and a fourth shape would have been sized as one
    /// thing and centred as another with no diagnostic anywhere. Unreachable through
    /// the document format, which rejects an unknown shape at parse.
    /// </remarks>
    private ArgumentOutOfRangeException Unhandled() =>
        new(nameof(Shape), Shape, "unhandled electrode shape");

    /// <summary>Whether a point lies within this electrode's conductor.</summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <returns><see langword="true"/> when inside or on the surface.</returns>
    public bool Contains(double x, double y, double z) => SignedDistance(x, y, z) <= 0.0;

    /// <summary>
    /// Signed distance to this electrode's surface: negative inside, positive
    /// outside, zero on it.
    /// </summary>
    /// <param name="x">x, in metres.</param>
    /// <param name="y">y, in metres.</param>
    /// <param name="z">z, in metres.</param>
    /// <returns>The signed distance, in metres.</returns>
    public double SignedDistance(double x, double y, double z)
    {
        switch (Shape)
        {
            case Electrode3DShape.Sphere:
            {
                var dx = x - CentreX;
                var dy = y - CentreY;
                var dz = z - CentreZ;

                return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) - Radius;
            }

            case Electrode3DShape.Box:
            {
                var dx = Math.Max(MinX - x, x - MaxX);
                var dy = Math.Max(MinY - y, y - MaxY);
                var dz = Math.Max(MinZ - z, z - MaxZ);

                if (dx <= 0.0 && dy <= 0.0 && dz <= 0.0)
                {
                    return Math.Max(dx, Math.Max(dy, dz));
                }

                var ox = Math.Max(dx, 0.0);
                var oy = Math.Max(dy, 0.0);
                var oz = Math.Max(dz, 0.0);

                return Math.Sqrt((ox * ox) + (oy * oy) + (oz * oz));
            }

            case Electrode3DShape.Cylinder:
            {
                var (along, a, b) = Resolve(x, y, z);

                var da = a - CentreOf(0);
                var db = b - CentreOf(1);

                var radial = Math.Sqrt((da * da) + (db * db)) - Radius;
                var axial = Math.Max(Lower - along, along - Upper);

                if (radial <= 0.0 && axial <= 0.0)
                {
                    return Math.Max(radial, axial);
                }

                var orad = Math.Max(radial, 0.0);
                var oax = Math.Max(axial, 0.0);

                return Math.Sqrt((orad * orad) + (oax * oax));
            }

            default:
                throw Unhandled();
        }
    }

    /// <summary>
    /// Where a segment first enters this electrode's conductor, as a fraction of it.
    /// </summary>
    /// <param name="fromX">Segment start x, in metres.</param>
    /// <param name="fromY">Segment start y, in metres.</param>
    /// <param name="fromZ">Segment start z, in metres.</param>
    /// <param name="toX">Segment end x, in metres.</param>
    /// <param name="toY">Segment end y, in metres.</param>
    /// <param name="toZ">Segment end z, in metres.</param>
    /// <returns>The fraction at which the conductor is first met, or null when missed.</returns>
    /// <remarks>
    /// Entry rather than crossing, and closed form rather than bisection, for the
    /// same two reasons as in the plane. Entry, because an electrode thinner than a
    /// cell lies wholly between two nodes and a straddle test reports nothing -
    /// which is every coarse level of a multigrid hierarchy. Closed form, because
    /// bisection can only find a crossing it already knows is bracketed.
    /// </remarks>
    public double? FirstEntry(
        double fromX, double fromY, double fromZ, double toX, double toY, double toZ)
    {
        double low;
        double high;

        switch (Shape)
        {
            case Electrode3DShape.Box:
            {
                if (!Slab(fromX, toX, MinX, MaxX, out var xLow, out var xHigh)
                    || !Slab(fromY, toY, MinY, MaxY, out var yLow, out var yHigh)
                    || !Slab(fromZ, toZ, MinZ, MaxZ, out var zLow, out var zHigh))
                {
                    return null;
                }

                low = Math.Max(xLow, Math.Max(yLow, zLow));
                high = Math.Min(xHigh, Math.Min(yHigh, zHigh));
                break;
            }

            case Electrode3DShape.Sphere:
            {
                if (!Quadratic(
                    fromX - CentreX, toX - fromX,
                    fromY - CentreY, toY - fromY,
                    fromZ - CentreZ, toZ - fromZ,
                    Radius, out low, out high))
                {
                    return null;
                }

                break;
            }

            case Electrode3DShape.Cylinder:
            {
                var (fromAlong, fromA, fromB) = Resolve(fromX, fromY, fromZ);
                var (toAlong, toA, toB) = Resolve(toX, toY, toZ);

                if (!Quadratic(
                    fromA - CentreOf(0), toA - fromA,
                    fromB - CentreOf(1), toB - fromB,
                    0.0, 0.0,
                    Radius, out var radialLow, out var radialHigh))
                {
                    return null;
                }

                if (!Slab(fromAlong, toAlong, Lower, Upper, out var axialLow, out var axialHigh))
                {
                    return null;
                }

                low = Math.Max(radialLow, axialLow);
                high = Math.Min(radialHigh, axialHigh);
                break;
            }

            default:
                throw Unhandled();
        }

        if (low > high || high < 0.0 || low > 1.0)
        {
            return null;
        }

        return Math.Max(low, 0.0);
    }

    /// <summary>The segment parameters over which one coordinate lies within a slab.</summary>
    private static bool Slab(double from, double to, double lower, double upper, out double low, out double high)
    {
        var delta = to - from;

        if (delta == 0.0)
        {
            // Parallel to the slab: either wholly inside it for the whole segment
            // or wholly outside for all of it.
            low = 0.0;
            high = 1.0;

            return from >= lower && from <= upper;
        }

        var a = (lower - from) / delta;
        var b = (upper - from) / delta;

        low = Math.Min(a, b);
        high = Math.Max(a, b);

        return true;
    }

    /// <summary>The segment parameters over which a point lies within a radius.</summary>
    private static bool Quadratic(
        double px, double dx, double py, double dy, double pz, double dz,
        double radius, out double low, out double high)
    {
        low = 0.0;
        high = 0.0;

        var a = (dx * dx) + (dy * dy) + (dz * dz);

        if (a == 0.0)
        {
            // No motion in the plane the radius is measured in. Inside for the
            // whole segment, or outside for all of it.
            if ((px * px) + (py * py) + (pz * pz) <= radius * radius)
            {
                low = 0.0;
                high = 1.0;
                return true;
            }

            return false;
        }

        var b = 2.0 * ((dx * px) + (dy * py) + (dz * pz));
        var c = (px * px) + (py * py) + (pz * pz) - (radius * radius);

        var discriminant = (b * b) - (4.0 * a * c);

        if (discriminant < 0.0)
        {
            return false;
        }

        var root = Math.Sqrt(discriminant);

        low = (-b - root) / (2.0 * a);
        high = (-b + root) / (2.0 * a);

        return true;
    }

    /// <summary>Splits a point into the coordinate along a cylinder's axis and the two across it.</summary>
    private (double Along, double A, double B) Resolve(double x, double y, double z) => Axis switch
    {
        CylinderAxis.X => (x, y, z),
        CylinderAxis.Y => (y, x, z),
        _ => (z, x, y),
    };

    /// <summary>The cylinder centre's component across its axis.</summary>
    private double CentreOf(int which) => Axis switch
    {
        CylinderAxis.X => which == 0 ? CentreY : CentreZ,
        CylinderAxis.Y => which == 0 ? CentreX : CentreZ,
        _ => which == 0 ? CentreX : CentreY,
    };
}
