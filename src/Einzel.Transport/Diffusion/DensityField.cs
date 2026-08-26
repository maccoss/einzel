using Einzel.Fields.Solved;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// Where the ions are, as a density rather than as a list of positions.
/// </summary>
/// <remarks>
/// <para>
/// TRN-2: diffusive transport emits a time-resolved density field rather than
/// trajectories, because that is what it computes. There are no trajectories in
/// here to draw even if something wanted to - RND-8 forbids drawing lines through a
/// funnel for exactly this reason, and the reason is that the lines would depict
/// something the model never produced.
/// </para>
/// <para>
/// Held on the same <see cref="Grid2D"/> the field solver uses, so a density and
/// the potential driving it are sampled at the same nodes and no interpolation sits
/// between them.
/// </para>
/// </remarks>
public sealed class DensityField
{
    private readonly double[] _values;

    /// <summary>Creates an empty density on a grid.</summary>
    /// <param name="grid">The grid.</param>
    /// <param name="cylindrical">
    /// Whether y is a radius rather than a second Cartesian coordinate, which
    /// changes what a cell's volume is.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public DensityField(Grid2D grid, bool cylindrical = false)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Grid = grid;
        Cylindrical = cylindrical;
        _values = new double[grid.CountX * grid.CountY];
    }

    /// <summary>The grid this density lives on.</summary>
    public Grid2D Grid { get; }

    /// <summary>Whether y is a radius.</summary>
    public bool Cylindrical { get; }

    /// <summary>Density at a node, in ions per cubic metre.</summary>
    /// <param name="i">Column.</param>
    /// <param name="j">Row.</param>
    /// <returns>The density.</returns>
    public double this[int i, int j]
    {
        get => _values[(j * Grid.CountX) + i];
        set => _values[(j * Grid.CountX) + i] = value;
    }

    /// <summary>The raw values, row-major.</summary>
    public double[] Values => _values;

    /// <summary>
    /// The volume a node's cell occupies, in cubic metres.
    /// </summary>
    /// <param name="j">Row.</param>
    /// <returns>The volume.</returns>
    /// <remarks>
    /// In a cylindrical solve a cell is a ring, so its volume grows with radius and
    /// a uniform density holds far more ions at the wall than on the axis. Getting
    /// this wrong does not make the density visibly odd - it makes every integrated
    /// quantity wrong by a factor that varies across the domain, which is much
    /// harder to notice.
    /// </remarks>
    public double CellVolume(int j)
    {
        var area = Grid.SpacingX * Grid.SpacingY;

        if (!Cylindrical)
        {
            // A metre of depth in the invariant direction, so a "volume" here is an
            // area per metre and every integral below is per metre of device.
            return area;
        }

        var radius = Grid.Y(j);

        // The ring between the cell's faces. On the axis the inner face has zero
        // radius and the cell is a disc, which this gives without a special case.
        var outer = radius + (0.5 * Grid.SpacingY);
        var inner = Math.Max(0.0, radius - (0.5 * Grid.SpacingY));

        return Math.PI * ((outer * outer) - (inner * inner)) * Grid.SpacingX;
    }

    /// <summary>How many ions the field holds.</summary>
    /// <returns>The total population.</returns>
    public double Population()
    {
        var total = 0.0;

        for (var j = 0; j < Grid.CountY; j++)
        {
            var volume = CellVolume(j);

            for (var i = 0; i < Grid.CountX; i++)
            {
                total += this[i, j] * volume;
            }
        }

        return total;
    }

    /// <summary>The population-weighted mean position, in metres.</summary>
    /// <returns>Mean x and mean y.</returns>
    public (double X, double Y) Centroid()
    {
        var total = 0.0;
        var sumX = 0.0;
        var sumY = 0.0;

        for (var j = 0; j < Grid.CountY; j++)
        {
            var volume = CellVolume(j);
            var y = Grid.Y(j);

            for (var i = 0; i < Grid.CountX; i++)
            {
                var weight = this[i, j] * volume;

                total += weight;
                sumX += weight * Grid.X(i);
                sumY += weight * y;
            }
        }

        return total > 0.0 ? (sumX / total, sumY / total) : (0.0, 0.0);
    }

    /// <summary>The population-weighted standard deviation of position, in metres.</summary>
    /// <returns>Spread in x and in y.</returns>
    /// <remarks>
    /// What replaces a packet's size when there are no particles to take a variance
    /// over. For a cloud spreading by diffusion alone this grows as the square root
    /// of time, which is the sharpest check the solver has.
    /// </remarks>
    public (double X, double Y) Spread()
    {
        var (meanX, meanY) = Centroid();

        var total = 0.0;
        var sumX = 0.0;
        var sumY = 0.0;

        for (var j = 0; j < Grid.CountY; j++)
        {
            var volume = CellVolume(j);
            var dy = Grid.Y(j) - meanY;

            for (var i = 0; i < Grid.CountX; i++)
            {
                var weight = this[i, j] * volume;
                var dx = Grid.X(i) - meanX;

                total += weight;
                sumX += weight * dx * dx;
                sumY += weight * dy * dy;
            }
        }

        return total > 0.0
            ? (Math.Sqrt(sumX / total), Math.Sqrt(sumY / total))
            : (0.0, 0.0);
    }

    /// <summary>A copy.</summary>
    /// <returns>An independent density with the same values.</returns>
    public DensityField Clone()
    {
        var copy = new DensityField(Grid, Cylindrical);

        Array.Copy(_values, copy._values, _values.Length);

        return copy;
    }
}
