using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>
/// A neutral gas whose bulk velocity is sampled from a grid.
/// </summary>
/// <remarks>
/// <para>
/// The half of GAS-1 a single declared vector cannot express, and the one spec
/// figure 4 makes a requirement rather than a benefit above 10^-2 mbar: "the
/// neutral jet off the inlet capillary drags ions and frequently dominates the
/// axial DC gradient". A jet is not uniform across a ring stack, and a funnel
/// modelled in a gas that moves all in one piece is a funnel whose gas is not
/// doing what a funnel's gas does.
/// </para>
/// <para>
/// Einzel consumes a velocity field; it does not compute one. That boundary is
/// deliberate and is the same one §17 draws around visualisation: computing a
/// compressible flow through a differentially pumped stack is a CFD problem, and a
/// half-hearted one inside an ion-optics engine would be worse than none because
/// it would look like an answer.
/// </para>
/// <para>
/// <strong>Trilinear, not tricubic.</strong> ACC-3 forbids the cheap interpolant on
/// a trajectory path because the interpolant's discontinuous derivatives accumulate
/// into the timing budget over a hundred thousand cell crossings. That argument
/// does not transfer: the gas velocity's derivative is never used, it enters the
/// drift-diffusion flux as a value at a face, and a CFD field arrives with its own
/// discretisation error far above anything the interpolant adds.
/// </para>
/// <para>
/// <strong>Clamped outside, and the overhang is measurable.</strong> A flow that
/// stopped at the edge of its imported box would put a shear where the instrument
/// has none. Clamping continues the edge value, which is right for a stream and
/// wrong for the end of a jet - so <see cref="FractionOutside"/> exists for a caller
/// to warn with, because the honest statement is how much of the tracked region was
/// never measured rather than a silent extrapolation either way.
/// </para>
/// </remarks>
public sealed class SampledGasFlow : IGasFlow
{
    private readonly SampledGrid _grid;

    /// <summary>Creates a flow from samples on a uniform grid.</summary>
    /// <param name="countX">Nodes along x, at least one.</param>
    /// <param name="countY">Nodes along y.</param>
    /// <param name="countZ">Nodes along z.</param>
    /// <param name="originSi">Position of node (0,0,0), in metres.</param>
    /// <param name="spacingSi">Node spacing, in metres. Zero on an axis with one node.</param>
    /// <param name="values">
    /// Three components per node, x fastest then y then z - the order VTK reads an
    /// extent in and the order this engine writes one.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is not positive.</exception>
    /// <exception cref="ArgumentException">The sample count does not match the extent.</exception>
    public SampledGasFlow(
        int countX, int countY, int countZ, Vec3 originSi, Vec3 spacingSi, double[] values)
        : this(new SampledGrid(3, countX, countY, countZ, originSi, spacingSi, values))
    {
    }

    /// <summary>Creates a flow from a three-component sampled grid.</summary>
    /// <param name="grid">The samples.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="ArgumentException">The grid does not carry three components.</exception>
    public SampledGasFlow(SampledGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (grid.Components != 3)
        {
            throw new ArgumentException(
                $"a velocity needs three components and this grid carries {grid.Components}",
                nameof(grid));
        }

        _grid = grid;

        var values = grid.Values;
        var fastest = 0.0;
        var moving = false;

        for (var i = 0; i + 2 < values.Length; i += 3)
        {
            var speed = Math.Sqrt(
                (values[i] * values[i])
                + (values[i + 1] * values[i + 1])
                + (values[i + 2] * values[i + 2]));

            fastest = Math.Max(fastest, speed);
            moving |= speed > 0.0;
        }

        FastestSpeedSi = fastest;
        IsMoving = moving;
    }

    /// <inheritdoc/>
    public bool IsMoving { get; }

    /// <inheritdoc/>
    public double FastestSpeedSi { get; }

    /// <summary>The lower corner of the sampled region, in metres.</summary>
    public Vec3 MinimumSi => _grid.MinimumSi;

    /// <summary>The upper corner, in metres.</summary>
    public Vec3 MaximumSi => _grid.MaximumSi;

    /// <inheritdoc/>
    public Vec3 VelocityAt(in Vec3 point)
    {
        Span<double> v = stackalloc double[3];

        _grid.SampleInto(in point, v);

        return new Vec3(v[0], v[1], v[2]);
    }

    /// <inheritdoc/>
    public bool Covers(in Vec3 point) => _grid.Covers(in point);

    /// <summary>
    /// How much of a box lies outside the sampled region, as a volume fraction.
    /// </summary>
    /// <param name="minimumSi">Lower corner of the box, in metres.</param>
    /// <param name="maximumSi">Upper corner.</param>
    /// <returns>Zero when the box is wholly inside, one when it is wholly outside.</returns>
    /// <remarks>
    /// What a caller warns with. Outside the imported extent the edge value is
    /// continued, which is right for a stream and wrong for the end of a jet - and
    /// there is no way to tell which from the samples alone, so the honest output is
    /// the size of the region where the answer was extrapolated rather than
    /// measured.
    /// </remarks>
    public double FractionOutside(Vec3 minimumSi, Vec3 maximumSi) =>
        _grid.FractionOutside(minimumSi, maximumSi);
}
