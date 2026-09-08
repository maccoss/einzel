using Einzel.Core.Geometry;
using Einzel.Fields.Solved;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// The ponderomotive well at every point a coefficient sample asks about, kept across
/// the steps of a ramp and checked rather than assumed to still hold.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> A ramped diffusive phase re-samples its drift and rebuilds
/// its face operator at every step, and where the field is driven every sample is a
/// cycle average: one pass of potential evaluations for the direct term and two passes
/// of field evaluations for the well. In an elution scan the ramp moves <em>only DC</em>
/// - the RF amplitudes are held for the whole scan - so the oscillating field is the
/// same at every step and so is its mean square. The direct term is not, and is
/// recomputed.
/// </para>
/// <para>
/// <b>Seven points per node, not one.</b> The sample asks for a field as well as a
/// potential, and the effective field is a central difference of the effective
/// potential at plus and minus a step on each axis. Caching only the node value would
/// force that difference to be split into a direct part and a well part, and
/// <c>(D+ + W+) - (D- + W-)</c> is not bit-identically
/// <c>(D+ - D-) + (W+ - W-)</c>. Holding the well at all seven points lets the
/// difference be taken exactly as <see cref="PonderomotiveField.ElectricFieldAt"/>
/// takes it, so a reused well moves nothing.
/// </para>
/// <para>
/// <b>Verified, not assumed.</b> A document that really does ramp an RF amplitude must
/// get the right answer, just without the saving. A fixed spread of nodes is re-computed
/// from scratch at every step and compared with what is held; one relative disagreement
/// past <see cref="Tolerance"/> throws the whole cache away and rebuilds it. That is
/// cheap - sixteen nodes against tens of thousands - and it is the difference between an
/// optimisation and an assumption about what a caller's ramp moves.
/// </para>
/// </remarks>
internal sealed class PonderomotiveWellCache
{
    /// <summary>How far a probe may drift before the whole cache is thrown away.</summary>
    /// <remarks>
    /// <para>
    /// Tight enough that a real amplitude ramp of any size fails it at once, and loose
    /// enough not to be tripped by the last bits of two evaluations of one unchanged
    /// field - the same argument the phase-level <c>Changed</c> probe makes one level up.
    /// </para>
    /// <para>
    /// <b>Relative to the deepest well on the grid</b>, not to the probe's own value. A
    /// well has nulls in it - on the axis of a quadrupole the oscillating field is
    /// exactly zero - and a probe that lands in one is comparing two roundings of
    /// nothing, so its own value as a scale makes the test read a relative change of
    /// order one where the absolute change is 1e-34 volts. What that gives up is a
    /// change confined to a region where the well is negligible, which is a change the
    /// drift cannot feel; what it buys is that a probe cannot fire on its own noise.
    /// </para>
    /// </remarks>
    private const double Tolerance = 1e-12;

    /// <summary>The offsets the well is held at: the node, then plus and minus on each axis.</summary>
    private const int Offsets = 7;

    private readonly Grid2D _grid;
    private readonly int[] _probes;
    private readonly double[][] _well;

    private PonderomotiveField _field;
    private double _deepest;

    /// <summary>Fills the cache from a field, which is one rebuild.</summary>
    /// <param name="grid">The grid the density is tracked on.</param>
    /// <param name="field">The effective field at the first instant.</param>
    public PonderomotiveWellCache(Grid2D grid, PonderomotiveField field)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(field);

        _grid = grid;
        _field = field;
        _probes = ProbeNodes(grid);
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

        _field = field;

        if (Holds(field))
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

    /// <summary>Sixteen nodes spread over the grid, chosen once.</summary>
    /// <remarks>
    /// A four by four lattice at odd eighths of each axis rather than a run along one
    /// row: an amplitude that moves in one corner of a funnel and nowhere else is
    /// exactly the change a row of probes would walk straight past. Duplicates are
    /// dropped so a grid of a handful of nodes does not probe the same one repeatedly.
    /// </remarks>
    private static int[] ProbeNodes(Grid2D grid)
    {
        var nodes = new List<int>(16);

        for (var a = 0; a < 4; a++)
        {
            var i = Math.Min(grid.CountX - 1, ((((2 * a) + 1) * grid.CountX) / 8));

            for (var b = 0; b < 4; b++)
            {
                var j = Math.Min(grid.CountY - 1, ((((2 * b) + 1) * grid.CountY) / 8));

                nodes.Add((j * grid.CountX) + i);
            }
        }

        return [.. nodes.Distinct()];
    }

    private double Total(int offset, int node, in Vec3 position) =>
        _field.DirectPotentialAt(in position) + _well[offset][node];

    private bool Holds(PonderomotiveField field)
    {
        foreach (var node in _probes)
        {
            var i = node % _grid.CountX;
            var j = node / _grid.CountX;

            var point = new Vec3(_grid.X(i), _grid.Y(j), 0.0);

            var fresh = field.WellAt(in point);
            var held = _well[0][node];

            // The deepest well on the grid, or either value where one has grown past it,
            // so a well that has collapsed to zero is caught as surely as one that has
            // grown. A grid of wells all exactly zero passes, which is right: nothing
            // has changed.
            var scale = Math.Max(_deepest, Math.Max(Math.Abs(fresh), Math.Abs(held)));

            if (Math.Abs(fresh - held) > Tolerance * scale)
            {
                return false;
            }
        }

        return true;
    }

    private void Fill(PonderomotiveField field)
    {
        var h = field.DifferencingStepM;

        _deepest = 0.0;

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

                _deepest = Math.Max(_deepest, Math.Abs(_well[0][node]));
            }
        }

        Rebuilds++;
    }
}
