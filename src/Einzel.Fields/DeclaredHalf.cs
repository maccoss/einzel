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

    // The declared half owns the plane: where its edge there is not continuous by construction,
    // it is the one member that declares the surface (PlaneFace).
    // Made only for a union: where the halves overlap there is no side to gate, and the sum uses
    // the declared half as it is (Reflection.Declared).
    private readonly PlaneFace _face = reflection.Overlaps
        ? throw new ArgumentException("a declared half is gated only in a union; where the halves overlap it is used as it is", nameof(reflection))
        : reflection.FaceOf(inner, ownsPlane: true);

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
    /// The inner field's run - gating only takes field away, so wherever the inner field is free
    /// the gated one is too - cut at the plane where the ray crosses it and this half declares a
    /// surface there, or the union's measure otherwise changes sign there
    /// (<see cref="PlaneFace.ChangesSignAt"/>): a drift jumping a change of sign skips a landing. Over
    /// the part of the plane the solve's box covers, and beyond it only where a cross-section's own
    /// edge on the plane leaves the one unavoidable change there. An analytic drift ends where a surface is,
    /// and a run that reached past the plane on the strength of an inner field free on both sides
    /// of it would jump the one surface the sum declares there.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var run = inner.FieldFreeRunLength(in position, in direction);

        if (!(run > 0.0) || direction.X == 0.0)
        {
            return run;
        }

        var toPlane = (reflection.PlaneX - position.X) / direction.X;

        if (!(toPlane > 0.0) || !(toPlane < run))
        {
            return run;
        }

        var y = position.Y + (toPlane * direction.Y);
        var z = position.Z + (toPlane * direction.Z);
        return _face.ChangesSignAt(y, z) ? toPlane : run;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Gated as the image's is, by one rule (<see cref="PlaneFace"/>): on the image's side, where
    /// this half contributes nothing, it declares none of its own surfaces - only the plane, where
    /// the edge on it is not a mirror, and it is the one member of the sum that does. On its own
    /// side the plane is added to its own surfaces, so a half that declares none still lands there.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        reflection.DeclaredAnswersAt(position.X)
            ? _face.Answering(in position, inner.SignedDistanceToDiscontinuity(in position))
            : _face.At(in position);

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
