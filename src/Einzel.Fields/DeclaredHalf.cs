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

    // Whether this half carries the plane as a discontinuity member of its own: made only for a
    // union - where the halves overlap there is no side to gate, and the sum uses the declared half
    // as it is (Reflection.Declared).
    private readonly bool _plane = reflection.Overlaps
        ? throw new ArgumentException("a declared half is gated only in a union; where the halves overlap it is used as it is", nameof(reflection))
        : reflection.DeclaresPlane;

    private readonly int _own = Math.Max(1, inner.DiscontinuityMembers);

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
    /// the gated one is too - cut where the ray crosses the plane, if the plane is a surface: a
    /// drift that reached past it would jump the plane member's change of sign.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var run = inner.FieldFreeRunLength(in position, in direction);

        if (!_plane || !(run > 0.0) || direction.X == 0.0)
        {
            return run;
        }

        var toPlane = (reflection.PlaneX - position.X) / direction.X;
        return toPlane > 0.0 && toPlane < run ? toPlane : run;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Its members combined (<see cref="DiscontinuityDistance.Combine"/>): the inner field's own
    /// and, where the plane is a surface, the plane.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var own = reflection.DeclaredAnswersAt(position.X) ? inner.SignedDistanceToDiscontinuity(in position) : double.PositiveInfinity;
        return _plane ? DiscontinuityDistance.Combine(own, reflection.PlaneMeasure(position.X)) : own;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The inner field's own members where this half answers, and on the image's side an infinity
    /// - no surface there - with the sign the member has on the plane straight across: the image
    /// declares their mirror there (<see cref="ReflectedField"/>, gated the same way). Read on both
    /// sides, as they were, a member changed sign on the image's side wherever the inner field had a
    /// surface past the plane, and an ion there landed on a surface neither half declares. And,
    /// where the edge on the plane is not a mirror, the plane, as one member of its own, on both
    /// sides. Tracked separately, a surface of the half's own that lies on the plane and the plane
    /// itself are one landing there, never two that cancel; and the plane beyond the box, where
    /// nothing jumps, costs a landing and cannot cancel a neighbouring element's surface there.
    /// </remarks>
    public int DiscontinuityMembers => _own + (_plane ? 1 : 0);

    /// <inheritdoc/>
    public double MemberDistanceToDiscontinuity(int member, in Vec3 position)
    {
        if (member >= _own)
        {
            return reflection.PlaneMeasure(position.X);
        }

        if (reflection.DeclaredAnswersAt(position.X))
        {
            return inner.MemberDistanceToDiscontinuity(member, in position);
        }

        var onPlane = new Vec3(reflection.PlaneX, position.Y, position.Z);
        return Math.CopySign(double.PositiveInfinity, inner.MemberDistanceToDiscontinuity(member, in onPlane));
    }

    /// <inheritdoc/>
    public void MemberDistancesToDiscontinuity(in Vec3 position, Span<double> distances)
    {
        if (reflection.DeclaredAnswersAt(position.X))
        {
            inner.MemberDistancesToDiscontinuity(in position, distances[.._own]);
        }
        else
        {
            var onPlane = new Vec3(reflection.PlaneX, position.Y, position.Z);
            var own = distances[.._own];
            inner.MemberDistancesToDiscontinuity(in onPlane, own);

            for (var k = 0; k < own.Length; k++)
            {
                own[k] = double.IsNaN(own[k]) ? double.NaN : Math.CopySign(double.PositiveInfinity, own[k]);
            }
        }

        if (_plane)
        {
            distances[_own] = reflection.PlaneMeasure(position.X);
        }
    }

    /// <inheritdoc/>
    public void DiscontinuityMemberGates(Span<int> gates)
    {
        inner.DiscontinuityMemberGates(gates[.._own]);

        if (_plane)
        {
            gates[_own] = -1;
        }
    }

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
