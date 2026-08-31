using Einzel.Core.Geometry;
using Einzel.Core.Model;

namespace Einzel.Fields.Analytic;

/// <summary>
/// An analytic field confined to a box, contributing nothing outside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>An analytic field has no extent, because a formula does not.</b> That is harmless
/// while such a field is an idealisation of a whole instrument — a uniform field, a
/// retarding half-space — and stops being harmless the moment one is an exact statement of
/// a real device sitting <i>next to</i> another. A quadro-logarithmic potential grows as z
/// squared, so an orbital analyser declared beside the trap that injects it puts an
/// enormous field across that trap, and the two instruments cannot be composed even though
/// superposition is exact and the sequencer can express the handover.
/// </para>
/// <para>
/// <b>The boundary is a field discontinuity, and the potential does not generally match
/// across it.</b> A box is not an equipotential of anything interesting, so an ion crossing
/// one gains or loses the potential it left. That is not hidden: the assembly measures the
/// largest potential anywhere on the boundary and reports it as a non-suppressible
/// violation, in volts, because it is exactly the energy error per crossing. Placing the
/// box where the field has decayed is what makes it small, and a region is a statement
/// about where an idealisation applies rather than a conductor.
/// </para>
/// <para>
/// Wrapping rather than teaching every analytic field about bounds — the precedent
/// <c>AxisymmetricField</c> and <c>PonderomotiveField</c> set, and for the
/// reason this project has recorded twice: the transport core carries every validated
/// number here, and refactoring it to add a case beside one is how those get quietly lost.
/// </para>
/// </remarks>
public class BoundedField : IElectrostaticField
{
    private readonly IElectrostaticField _inner;

    /// <summary>Wraps a field so that it is silent outside a box.</summary>
    /// <param name="inner">The field to bound.</param>
    /// <param name="region">The box, in metres.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    protected BoundedField(IElectrostaticField inner, FieldRegion region)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(region);

        _inner = inner;
        Region = region;
    }

    /// <summary>The box outside which this element contributes nothing.</summary>
    public FieldRegion Region { get; }

    /// <summary>The field this one bounds.</summary>
    protected IElectrostaticField Inner => _inner;

    /// <summary>
    /// Wraps a field so it is silent outside a box, keeping any time dependence.
    /// </summary>
    /// <param name="inner">The field to bound.</param>
    /// <param name="region">The box, in metres.</param>
    /// <returns>A bounded field of the same kind as the one given.</returns>
    /// <remarks>
    /// <b>The driven case is chosen by what the field IS, not by what the caller asks
    /// for.</b> A driven field also answers the time-free interface, at whatever instant it
    /// happens to be handed — and this project has now found that same defect five times,
    /// most recently in <c>SuperposedField</c>, where summing a driven member silently
    /// produced a snapshot of the RF at the top of its cycle. Returning a plain
    /// <see cref="BoundedField"/> around a driven field would be the sixth.
    /// </remarks>
    public static BoundedField Around(IElectrostaticField inner, FieldRegion region) =>
        inner is ITimeVaryingField driven
            ? new DrivenBoundedField(driven, region)
            : new BoundedField(inner, region);

    /// <inheritdoc />
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        Region.Contains(in position) ? _inner.ElectricFieldAt(in position) : Vec3.Zero;

    /// <inheritdoc />
    public double PotentialAt(in Vec3 position) =>
        Region.Contains(in position) ? _inner.PotentialAt(in position) : 0.0;

    /// <inheritdoc />
    /// <remarks>
    /// Zero, which means "no guarantee", rather than the inner field's answer. Outside the
    /// box the field really is zero and a long analytic drift would be exact — but only as
    /// far as the boundary, and a run length is a promise about the <i>whole</i> run. The
    /// same reason <c>AxisymmetricField</c> gives one up.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc />
    /// <remarks>
    /// The box surface, so the integrator brackets its zero and lands exactly on it — the
    /// same first-class event a declared field discontinuity already is (spec section 11).
    /// The inner field's own discontinuity, if it has one, is reported only inside: outside
    /// the box this element is not there, so it has no surfaces out there either.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var toBoundary = Region.SignedDistance(in position);

        if (!Region.Contains(in position))
        {
            return toBoundary;
        }

        var inner = _inner.SignedDistanceToDiscontinuity(in position);

        return double.IsPositiveInfinity(inner)
            ? toBoundary
            : Math.Abs(inner) < Math.Abs(toBoundary) ? inner : toBoundary;
    }

    /// <inheritdoc />
    public double ResolutionLength => _inner.ResolutionLength;
}

/// <summary>A driven analytic field confined to a box.</summary>
/// <remarks>
/// Separate from <see cref="BoundedField"/> so that a driven field wrapped in a region
/// still answers the time-varying interface. Reached through
/// <see cref="BoundedField.Around"/> rather than constructed directly, so the choice is
/// made by what the inner field is.
/// </remarks>
public sealed class DrivenBoundedField : BoundedField, ITimeVaryingField
{
    private readonly ITimeVaryingField _driven;

    internal DrivenBoundedField(ITimeVaryingField inner, FieldRegion region)
        : base(inner, region) => _driven = inner;

    /// <inheritdoc />
    public double ShortestPeriodSeconds => _driven.ShortestPeriodSeconds;

    /// <inheritdoc />
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
        Region.Contains(in position)
            ? _driven.ElectricFieldAt(in position, timeSeconds)
            : Vec3.Zero;

    /// <inheritdoc />
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        Region.Contains(in position) ? _driven.PotentialAt(in position, timeSeconds) : 0.0;
}
