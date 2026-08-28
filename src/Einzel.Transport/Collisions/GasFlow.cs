using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>
/// How the neutral gas is moving, as a velocity at a point.
/// </summary>
/// <remarks>
/// <para>
/// GAS-1 asks a gas region to carry "a bulk velocity field", and spec figure 4
/// makes it required above about 10^-2 mbar rather than optional. The
/// specification is unusually blunt about why: the field "is easy to omit and hard
/// to notice missing: at funnel pressures the neutral jet off the inlet capillary
/// drags ions and frequently dominates the axial DC gradient". A model that leaves
/// it out does not fail, it quietly answers a question about a different
/// instrument - one whose gas is standing still.
/// </para>
/// <para>
/// A seam with one implementation, in the sense section 21 means it. A uniform
/// bulk velocity is what a model document can declare today, and it is enough to
/// change the answer qualitatively: a funnel is pushed through by its gas, and a
/// stationary gas has no such push. What it is <em>not</em> is a jet, which varies
/// across the stack and is what an imported CFD solution would supply.
/// </para>
/// </remarks>
public interface IGasFlow
{
    /// <summary>Whether the gas is moving anywhere at all.</summary>
    /// <remarks>
    /// Asked rather than inferred from a velocity being non-zero somewhere, because
    /// a caller that can skip the whole advection term wants to know cheaply and a
    /// sampled field would have to scan itself to answer.
    /// </remarks>
    bool IsMoving { get; }

    /// <summary>The fastest bulk speed anywhere, in metres per second.</summary>
    /// <remarks>
    /// What a stability limit is taken against. A step small enough for the fastest
    /// gas anywhere is small enough everywhere, and taking the bound from the flow
    /// itself keeps a caller from having to scan a field it does not own.
    /// </remarks>
    double FastestSpeedSi { get; }

    /// <summary>Bulk velocity of the neutral gas at a point, in metres per second.</summary>
    /// <param name="point">Where, in metres.</param>
    /// <returns>The velocity.</returns>
    Vec3 VelocityAt(in Vec3 point);

    /// <summary>
    /// Whether this flow actually has data at a point, rather than extrapolating.
    /// </summary>
    /// <param name="point">The point, in metres.</param>
    /// <returns><see langword="true"/> where the flow is defined.</returns>
    /// <remarks>
    /// True everywhere for a flow given as a formula, and only inside its own box for
    /// one imported from a file. A sampled field clamps to its edge value outside,
    /// which is a choice rather than a measurement - the gas beyond the imported
    /// volume is whatever the last plane of it said - so a caller that flies through
    /// that region should be able to say so rather than reporting it as data.
    /// </remarks>
    bool Covers(in Vec3 point) => true;
}

/// <summary>
/// A gas moving everywhere at one declared velocity.
/// </summary>
/// <param name="VelocitySi">The bulk velocity, in metres per second.</param>
/// <remarks>
/// What <c>transport.gas.driftVelocity</c> means. It is the honest reading of a
/// single declared vector, and it is right wherever the flow is a stream through a
/// tube rather than a jet off an aperture.
/// </remarks>
public sealed record UniformGasFlow(Vec3 VelocitySi) : IGasFlow
{
    /// <summary>A gas that is not moving.</summary>
    public static UniformGasFlow Still { get; } = new(Vec3.Zero);

    /// <inheritdoc/>
    public bool IsMoving => VelocitySi.LengthSquared > 0.0;

    /// <inheritdoc/>
    public double FastestSpeedSi => VelocitySi.Length;

    /// <inheritdoc/>
    public Vec3 VelocityAt(in Vec3 point) => VelocitySi;
}
