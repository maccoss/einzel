using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// The declared half of a reflected solve as it stands in the sum: its field unchanged, its
/// conductors only where the half answers.
/// </summary>
/// <remarks>
/// The solve's own conductor queries ignore its box, so a declared electrode running past the
/// mirror plane stopped ions on the image's side, where the image's metal - the reflection of the
/// near side - is what stands. The image's conductors were already gated on the declared side;
/// this is the other half of the same rule (<see cref="Reflection.DeclaredAnswersAt"/>), so the
/// absorber is symmetric about the plane. Made only by <see cref="Reflection.Declared"/>, for a
/// union: where the halves overlap there is nothing to gate.
/// </remarks>
internal sealed class DeclaredHalf(IElectrostaticField inner, Reflection reflection) : IElectrostaticField, IConductorBounded
{
    private readonly IConductorBounded? _metal = inner as IConductorBounded;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(in position);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) => inner.PotentialAt(in position);

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => inner.FieldFreeRunLength(in position, in direction);

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) => inner.SignedDistanceToDiscontinuity(in position);

    /// <inheritdoc/>
    public double ResolutionLength => inner.ResolutionLength;

    /// <inheritdoc/>
    public double SignedDistanceToConductor(in Vec3 position) =>
        _metal is not null && reflection.DeclaredAnswersAt(position.X) ? _metal.SignedDistanceToConductor(in position) : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        _metal is not null && reflection.DeclaredAnswersAt(position.X) ? _metal.ConductorAt(in position) : null;

    /// <inheritdoc/>
    public double NearestConductor(in Vec3 position, out string? name)
    {
        name = null;

        return _metal is not null && reflection.DeclaredAnswersAt(position.X)
            ? _metal.NearestConductor(in position, out name)
            : double.PositiveInfinity;
    }
}
