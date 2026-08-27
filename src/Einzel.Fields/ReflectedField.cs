using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field reflected through a plane normal to x.
/// </summary>
/// <remarks>
/// The second mirror of a pair is the first one turned around, so it is built by
/// reflection rather than solved again. That is not only cheaper: it makes the
/// two halves identical by construction, so a difference between the inbound and
/// outbound legs of a trajectory cannot come from the two mirrors having been
/// meshed differently.
/// </remarks>
public sealed class ReflectedField(IElectrostaticField inner, double planeX)
    : IElectrostaticField, IConductorBounded
{
    /// <inheritdoc/>
    /// <remarks>
    /// The inner geometry seen through the mirror, so an electrode in the original
    /// half has a solid twin in the reflected one. Leaving this out would make a
    /// reflected mirror pair solid on one side and transparent on the other, which
    /// is worse than transparent on both.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position) =>
        inner is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(Reflect(in position))
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        inner is IConductorBounded bounded ? bounded.ConductorAt(Reflect(in position)) : null;


    private readonly IElectrostaticField _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private Vec3 Reflect(in Vec3 position) => new((2.0 * planeX) - position.X, position.Y, position.Z);

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        var field = _inner.ElectricFieldAt(in mirrored);

        // x flips with the coordinate; y and z do not.
        return new Vec3(-field.X, field.Y, field.Z);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        return _inner.PotentialAt(in mirrored);
    }

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var mirrored = Reflect(in position);
        var mirroredDirection = new Vec3(-direction.X, direction.Y, direction.Z);
        return _inner.FieldFreeRunLength(in mirrored, in mirroredDirection);
    }

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var mirrored = Reflect(in position);
        return _inner.SignedDistanceToDiscontinuity(in mirrored);
    }

    /// <inheritdoc/>
    public double ResolutionLength => _inner.ResolutionLength;
}
