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

    /// <summary>
    /// What a radial face's flux must be scaled by to conserve ions across it.
    /// </summary>
    /// <param name="j">The row the flux leaves.</param>
    /// <param name="direction">+1 for the outward face, -1 for the inward one.</param>
    /// <returns>The weight, which is exactly 1 on a Cartesian grid.</returns>
    /// <remarks>
    /// <para>
    /// A flux computed per unit area is conservative between two cells only if they
    /// have the same volume. In a cylindrical solve a cell is a ring, so they do not:
    /// the face area is <c>2 pi r_face hx</c> and the volume is <c>pi (r_out^2 -
    /// r_in^2) hx</c>, and a scheme that ignores the difference creates ions on one
    /// side of every radial face and destroys them on the other.
    /// </para>
    /// <para>
    /// The weight is <c>A_face hy / V</c>, which is identically 1 in the plane -
    /// so an isotropic solve multiplies by one and is unchanged to the last bit -
    /// and <c>1 + hy / (2r)</c> outward, <c>1 - hy / (2r)</c> inward, in a
    /// cylindrical one. Both cells sharing a face take the same <c>r_face</c>, which
    /// is what makes the exchange balance.
    /// </para>
    /// <para>
    /// <strong>On the axis it is 4.</strong> The inner face has zero area, so the
    /// cell is a disc rather than a ring and the outward weight is 4 rather than 1.
    /// That is the same factor of four the cylindrical Laplacian carries on the axis,
    /// arrived at from the same geometry - and the field solver had it while this did
    /// not. It is also where the error was largest, which is exactly where a funnel
    /// concentrates its ions.
    /// </para>
    /// </remarks>
    public double RadialFaceWeight(int j, int direction)
    {
        if (!Cylindrical)
        {
            return 1.0;
        }

        var faceRadius = Grid.Y(j) + (0.5 * direction * Grid.SpacingY);

        if (faceRadius <= 0.0)
        {
            // The axis. No area, so nothing crosses, which is the physical statement
            // as well as the arithmetic one: there is no radial direction there.
            return 0.0;
        }

        var area = 2.0 * Math.PI * faceRadius * Grid.SpacingX;

        return area * Grid.SpacingY / CellVolume(j);
    }

    /// <summary>
    /// The largest radial face weight anywhere on this grid.
    /// </summary>
    /// <returns>The weight, which is 1 on a Cartesian grid and 4 on an axis.</returns>
    /// <remarks>
    /// What a stability limit has to be taken against: the explicit step is set by
    /// the largest outward coefficient, and weighting a face scales that coefficient
    /// with it. Taking the step from the unweighted rate on a cylindrical grid means
    /// stepping up to four times too far on the axis.
    /// </remarks>
    public double LargestRadialWeight()
    {
        if (!Cylindrical)
        {
            return 1.0;
        }

        var largest = 1.0;

        for (var j = 0; j < Grid.CountY; j++)
        {
            largest = Math.Max(largest, RadialFaceWeight(j, +1));
            largest = Math.Max(largest, RadialFaceWeight(j, -1));
        }

        return largest;
    }

    /// <summary>
    /// The density at a point, in ions per cubic metre.
    /// </summary>
    /// <param name="x">Axial coordinate, in metres.</param>
    /// <param name="y">The other coordinate, or the radius in a cylindrical solve.</param>
    /// <returns>The density, and zero anywhere outside the tracked region.</returns>
    /// <remarks>
    /// <para>
    /// Bilinear, which is a deliberate difference from how a <em>field</em> is
    /// sampled. ACC-3 forbids trilinear interpolation on a trajectory path because
    /// the interpolant's discontinuous derivatives accumulate into the timing budget
    /// over a hundred thousand cell crossings. Nothing integrates through a density:
    /// it is read once per picture or once per query, its derivative is not used,
    /// and a higher-order interpolant would overshoot into negative values at the
    /// edge of a packet - which is the one thing this whole scheme is built to avoid.
    /// </para>
    /// <para>
    /// Zero outside rather than clamped to the edge value. A density is a quantity
    /// with a total, and repeating the boundary outward invents ions.
    /// </para>
    /// </remarks>
    public double SampleAt(double x, double y)
    {
        if (Cylindrical)
        {
            y = Math.Abs(y);
        }

        var u = (x - Grid.OriginX) / Grid.SpacingX;
        var v = (y - Grid.OriginY) / Grid.SpacingY;

        if (u < 0.0 || v < 0.0 || u > Grid.CountX - 1 || v > Grid.CountY - 1)
        {
            return 0.0;
        }

        var i = Math.Min((int)u, Grid.CountX - 2);
        var j = Math.Min((int)v, Grid.CountY - 2);

        var fu = u - i;
        var fv = v - j;

        return ((1.0 - fu) * (1.0 - fv) * this[i, j])
            + (fu * (1.0 - fv) * this[i + 1, j])
            + ((1.0 - fu) * fv * this[i, j + 1])
            + (fu * fv * this[i + 1, j + 1]);
    }

    /// <summary>The largest density anywhere, in ions per cubic metre.</summary>
    public double Peak()
    {
        var peak = 0.0;

        foreach (var value in _values)
        {
            peak = Math.Max(peak, value);
        }

        return peak;
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
