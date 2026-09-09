using Einzel.Core.Geometry;
using Einzel.Fields.Solved;

namespace Einzel.Transport.Diffusion;

/// <summary>Holds the oscillating well on a density grid while its defining RF state is unchanged.</summary>
/// <remarks>Spatial probes cannot prove that a field is unchanged between them. Reuse requires
/// equality of the immutable oscillation definitions and of the species, gas and sampling settings.
/// An unknown field therefore rebuilds rather than silently keeping a stale well.</remarks>
internal sealed class PonderomotiveWellCache
{
    /// <summary>The offsets the well is held at: the node, then plus and minus on each axis.</summary>
    private const int Offsets = 7;

    private readonly Grid2D _grid;
    private readonly double[][] _well;

    private PonderomotiveField _field;

    /// <summary>Fills the cache from a field, which is one rebuild.</summary>
    /// <param name="grid">The grid the density is tracked on.</param>
    /// <param name="field">The effective field at the first instant.</param>
    public PonderomotiveWellCache(Grid2D grid, PonderomotiveField field)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(field);

        _grid = grid;
        _field = field;
        _well = new double[Offsets][];

        var count = grid.CountX * grid.CountY;

        for (var offset = 0; offset < Offsets; offset++)
        {
            _well[offset] = new double[count];
        }

        Fill(field);
    }

    /// <summary>How many times the well was computed over the whole grid.</summary>
    /// <remarks>
    /// One where the ramp left the oscillating field alone, which is the case this
    /// exists for; one per assembly where it did not, which is what the run would have
    /// cost anyway.
    /// </remarks>
    public int Rebuilds { get; private set; }

    /// <summary>
    /// Points the cache at the field of this step, rebuilding if the well has moved.
    /// </summary>
    /// <param name="field">The effective field at this instant.</param>
    /// <returns>Whether the well had to be recomputed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public bool Refresh(PonderomotiveField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var holds = _field.HasSameWellAs(field);
        _field = field;

        if (holds)
        {
            return false;
        }

        Fill(field);

        return true;
    }

    /// <summary>The effective potential at a node, in volts.</summary>
    /// <param name="node">The node's flat index.</param>
    /// <param name="position">The node's position, in metres.</param>
    /// <returns>The potential.</returns>
    /// <remarks>
    /// The same sum <see cref="PonderomotiveField.PotentialAt"/> forms, with the well
    /// read rather than recomputed.
    /// </remarks>
    public double PotentialAt(int node, in Vec3 position) => Total(0, node, in position);

    /// <summary>The effective field at a node, in volts per metre.</summary>
    /// <param name="node">The node's flat index.</param>
    /// <param name="position">The node's position, in metres.</param>
    /// <returns>The field vector.</returns>
    /// <remarks>
    /// Written out rather than delegated, because it has to be the same arithmetic in
    /// the same order as <see cref="PonderomotiveField.ElectricFieldAt"/> - a central
    /// difference of the total potential, then negated - with only the well read from
    /// the cache. Any other grouping would move the last bits.
    /// </remarks>
    public Vec3 ElectricFieldAt(int node, in Vec3 position)
    {
        var h = _field.DifferencingStepM;

        var x = (Total(1, node, position + new Vec3(h, 0.0, 0.0))
            - Total(2, node, position - new Vec3(h, 0.0, 0.0))) / (2.0 * h);

        var y = (Total(3, node, position + new Vec3(0.0, h, 0.0))
            - Total(4, node, position - new Vec3(0.0, h, 0.0))) / (2.0 * h);

        var z = (Total(5, node, position + new Vec3(0.0, 0.0, h))
            - Total(6, node, position - new Vec3(0.0, 0.0, h))) / (2.0 * h);

        return new Vec3(-x, -y, -z);
    }

    private double Total(int offset, int node, in Vec3 position) =>
        _field.DirectPotentialAt(in position) + _well[offset][node];

    private void Fill(PonderomotiveField field)
    {
        var h = field.DifferencingStepM;

        for (var j = 0; j < _grid.CountY; j++)
        {
            for (var i = 0; i < _grid.CountX; i++)
            {
                var node = (j * _grid.CountX) + i;
                var point = new Vec3(_grid.X(i), _grid.Y(j), 0.0);

                // The offsets are formed exactly as the central difference forms them -
                // a Vec3 added and a Vec3 subtracted, not a signed component added -
                // because the point the well is held at has to be the point the
                // gradient asks about, to the bit.
                _well[0][node] = field.WellAt(in point);

                var xPlus = point + new Vec3(h, 0.0, 0.0);
                var xMinus = point - new Vec3(h, 0.0, 0.0);
                var yPlus = point + new Vec3(0.0, h, 0.0);
                var yMinus = point - new Vec3(0.0, h, 0.0);
                var zPlus = point + new Vec3(0.0, 0.0, h);
                var zMinus = point - new Vec3(0.0, 0.0, h);

                _well[1][node] = field.WellAt(in xPlus);
                _well[2][node] = field.WellAt(in xMinus);
                _well[3][node] = field.WellAt(in yPlus);
                _well[4][node] = field.WellAt(in yMinus);
                _well[5][node] = field.WellAt(in zPlus);
                _well[6][node] = field.WellAt(in zMinus);

            }
        }

        Rebuilds++;
    }
}
