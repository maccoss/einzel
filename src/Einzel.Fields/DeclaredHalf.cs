using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// The declared half of a reflected solve as it stands in the sum: its field, its potential and
/// its conductors only where the half answers.
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
    private readonly GatedMetal? _metal = inner is IConductorBounded metal ? new GatedMetal(metal, reflection) : null;

    /// <inheritdoc/>
    /// <remarks>
    /// Gated as the image is, by the one side rule (<see cref="Reflection.DeclaredAnswersAt"/>):
    /// only the image's half was gated, so an inner field that is not nothing outside its box -
    /// a solve's constant outside potential, say - stood on the image's side as well, and the sum
    /// there was the declared half's value plus the image's, where on the declared side it was the
    /// declared half's alone: a sum asymmetric about its own mirror.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        reflection.DeclaredAnswersAt(position.X) ? inner.ElectricFieldAt(in position) : default;

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) =>
        reflection.DeclaredAnswersAt(position.X) ? inner.PotentialAt(in position) : 0.0;

    /// <inheritdoc/>
    /// <remarks>
    /// The inner field's run, unchanged: gating only takes field away, so wherever the inner field
    /// is free the gated one is too, and the inner run is never longer than the true one.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => inner.FieldFreeRunLength(in position, in direction);

    /// <inheritdoc/>
    /// <remarks>
    /// The inner field's, unchanged. A declared half meets its image at the plane only where its
    /// box ends there, and a solve is field-free outside its box, so gating its field adds no jump
    /// the inner field does not already declare at its own edge.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) => inner.SignedDistanceToDiscontinuity(in position);

    /// <inheritdoc/>
    public double ResolutionLength => inner.ResolutionLength;

    /// <inheritdoc/>
    public double SignedDistanceToConductor(in Vec3 position) => _metal?.SignedDistanceToConductor(in position) ?? double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) => _metal?.ConductorAt(in position);

    /// <inheritdoc/>
    public double NearestConductor(in Vec3 position, out string? name)
    {
        name = null;
        return _metal?.NearestConductor(in position, out name) ?? double.PositiveInfinity;
    }
}

/// <summary>
/// The declared half's conductors as they stand in a union: only where the half answers.
/// </summary>
/// <remarks>
/// The conductor half of <see cref="DeclaredHalf"/>, on its own, so a caller holding only the
/// metal (<see cref="ConductorLeaf.Of"/>) gates it by the same rule without asking the field to
/// be something it was not typed as.
/// </remarks>
internal sealed class GatedMetal(IConductorBounded inner, Reflection reflection) : IConductorBounded
{
    /// <inheritdoc/>
    public double SignedDistanceToConductor(in Vec3 position) =>
        reflection.DeclaredAnswersAt(position.X) ? inner.SignedDistanceToConductor(in position) : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        reflection.DeclaredAnswersAt(position.X) ? inner.ConductorAt(in position) : null;

    /// <inheritdoc/>
    public double NearestConductor(in Vec3 position, out string? name)
    {
        name = null;

        return reflection.DeclaredAnswersAt(position.X)
            ? inner.NearestConductor(in position, out name)
            : double.PositiveInfinity;
    }
}
