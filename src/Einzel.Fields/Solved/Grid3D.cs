namespace Einzel.Fields.Solved;

/// <summary>
/// A uniform three-dimensional node grid, with independent spacing per axis.
/// </summary>
/// <remarks>
/// <para>
/// Written beside <see cref="Grid2D"/> rather than generalising it. The
/// two-dimensional path carries every validated number this engine has - the
/// reflectron at 1.3e-13, cut cells at 3.1e-10, the coaxial and Bessel closed forms
/// - and refactoring a numerical core that is known to be right, in order to add a
/// case beside it, is how those numbers get quietly lost. The duplication is the
/// price and it is the cheaper one.
/// </para>
/// <para>
/// Interval counts are powers of two on each axis so a multigrid hierarchy
/// coarsens cleanly, and each axis rounds its own count up from the same requested
/// cell size - so the domain is meshed exactly, no direction is ever coarser than
/// asked, and the worst aspect ratio is two to one.
/// </para>
/// </remarks>
public sealed class Grid3D
{
    /// <summary>Creates a grid with independent spacings.</summary>
    /// <param name="originX">x of node 0, in metres.</param>
    /// <param name="originY">y of node 0, in metres.</param>
    /// <param name="originZ">z of node 0, in metres.</param>
    /// <param name="spacingX">Node spacing along x, in metres.</param>
    /// <param name="spacingY">Node spacing along y, in metres.</param>
    /// <param name="spacingZ">Node spacing along z, in metres.</param>
    /// <param name="countX">Nodes along x, at least three.</param>
    /// <param name="countY">Nodes along y, at least three.</param>
    /// <param name="countZ">Nodes along z, at least three.</param>
    /// <exception cref="ArgumentOutOfRangeException">A spacing is not positive, or a count is below three.</exception>
    public Grid3D(
        double originX, double originY, double originZ,
        double spacingX, double spacingY, double spacingZ,
        int countX, int countY, int countZ)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacingZ);
        ArgumentOutOfRangeException.ThrowIfLessThan(countX, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(countY, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(countZ, 3);

        OriginX = originX;
        OriginY = originY;
        OriginZ = originZ;
        SpacingX = spacingX;
        SpacingY = spacingY;
        SpacingZ = spacingZ;
        CountX = countX;
        CountY = countY;
        CountZ = countZ;
    }

    /// <summary>x of node 0, in metres.</summary>
    public double OriginX { get; }

    /// <summary>y of node 0, in metres.</summary>
    public double OriginY { get; }

    /// <summary>z of node 0, in metres.</summary>
    public double OriginZ { get; }

    /// <summary>Node spacing along x, in metres.</summary>
    public double SpacingX { get; }

    /// <summary>Node spacing along y, in metres.</summary>
    public double SpacingY { get; }

    /// <summary>Node spacing along z, in metres.</summary>
    public double SpacingZ { get; }

    /// <summary>Nodes along x.</summary>
    public int CountX { get; }

    /// <summary>Nodes along y.</summary>
    public int CountY { get; }

    /// <summary>Nodes along z.</summary>
    public int CountZ { get; }

    /// <summary>Total nodes.</summary>
    public long NodeCount => (long)CountX * CountY * CountZ;

    /// <summary>The finest spacing, which is what a step may not outrun.</summary>
    public double MinimumSpacing => Math.Min(SpacingX, Math.Min(SpacingY, SpacingZ));

    /// <summary>x of a node index.</summary>
    /// <param name="i">Node index along x.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double X(int i) => OriginX + (i * SpacingX);

    /// <summary>y of a node index.</summary>
    /// <param name="j">Node index along y.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double Y(int j) => OriginY + (j * SpacingY);

    /// <summary>z of a node index.</summary>
    /// <param name="k">Node index along z.</param>
    /// <returns>The coordinate, in metres.</returns>
    public double Z(int k) => OriginZ + (k * SpacingZ);

    /// <summary>The flat index of a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns>The index into a row-major buffer.</returns>
    public int Index(int i, int j, int k) => i + (CountX * (j + (CountY * k)));

    /// <summary>Whether a multigrid level below this one exists.</summary>
    public bool CanCoarsen =>
        (CountX - 1) % 2 == 0 && (CountY - 1) % 2 == 0 && (CountZ - 1) % 2 == 0
        && (CountX - 1) / 2 >= 2 && (CountY - 1) / 2 >= 2 && (CountZ - 1) / 2 >= 2;

    /// <summary>The next coarser grid, with twice the spacing and the same corners.</summary>
    /// <returns>The coarsened grid.</returns>
    /// <exception cref="InvalidOperationException">The grid cannot be coarsened further.</exception>
    public Grid3D Coarsen()
    {
        if (!CanCoarsen)
        {
            throw new InvalidOperationException(
                $"a {CountX} by {CountY} by {CountZ} grid cannot be coarsened further");
        }

        return new Grid3D(
            OriginX, OriginY, OriginZ,
            2.0 * SpacingX, 2.0 * SpacingY, 2.0 * SpacingZ,
            ((CountX - 1) / 2) + 1, ((CountY - 1) / 2) + 1, ((CountZ - 1) / 2) + 1);
    }

    /// <summary>
    /// A grid covering a box, meshed at about a requested cell size.
    /// </summary>
    /// <param name="minX">Lower x, in metres.</param>
    /// <param name="minY">Lower y, in metres.</param>
    /// <param name="minZ">Lower z, in metres.</param>
    /// <param name="maxX">Upper x, in metres.</param>
    /// <param name="maxY">Upper y, in metres.</param>
    /// <param name="maxZ">Upper z, in metres.</param>
    /// <param name="cellSize">Requested node spacing, in metres.</param>
    /// <returns>The grid.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The box is empty, or the cell size is not positive.</exception>
    /// <remarks>
    /// Each axis rounds its own interval count <em>up</em> to a power of two from
    /// the same requested cell size. Deriving one axis from another - which the
    /// two-dimensional builder once did - silently stretches the domain, and it did:
    /// a 60 by 20 mm box became 60 by 30 mm, and the solve was of a different
    /// instrument.
    /// </remarks>
    public static Grid3D OverBox(
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ,
        double cellSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxX, minX);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxY, minY);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxZ, minZ);

        var nx = IntervalsFor(maxX - minX, cellSize);
        var ny = IntervalsFor(maxY - minY, cellSize);
        var nz = IntervalsFor(maxZ - minZ, cellSize);

        return new Grid3D(
            minX, minY, minZ,
            (maxX - minX) / nx, (maxY - minY) / ny, (maxZ - minZ) / nz,
            nx + 1, ny + 1, nz + 1);
    }

    private static int IntervalsFor(double span, double cellSize)
    {
        var wanted = Math.Max(2, (int)Math.Ceiling(span / cellSize));

        var intervals = 2;

        while (intervals < wanted)
        {
            intervals *= 2;
        }

        return intervals;
    }
}
