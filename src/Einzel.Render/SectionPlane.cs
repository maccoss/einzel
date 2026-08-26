using Einzel.Core.Geometry;

namespace Einzel.Render;

/// <summary>
/// A plane through the instrument, and the two directions that lie in it.
/// </summary>
/// <remarks>
/// <para>
/// A section is the figure ion optics is nearly always drawn as: the memo's own
/// figures are line drawings of a plane through the axis. Everything below works
/// in the plane's own two coordinates, so a two-dimensional solve and a plane cut
/// through a three-dimensional one produce the same kind of drawing and share one
/// pipeline.
/// </para>
/// <para>
/// The in-plane axes are chosen rather than declared. A caller who had to supply
/// them could supply two that are not orthogonal, or not in the plane, and the
/// figure would come out sheared with nothing to catch it.
/// </para>
/// </remarks>
public sealed class SectionPlane
{
    /// <summary>Creates a plane through a point with a given normal.</summary>
    /// <param name="through">A point the plane passes through.</param>
    /// <param name="normal">The plane normal; need not be a unit vector.</param>
    /// <param name="rightHint">
    /// A direction to align the plane's own x axis with where possible, so a
    /// section of an instrument along z comes out with z running across the page.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The normal has no length.</exception>
    public SectionPlane(Vec3 through, Vec3 normal, Vec3? rightHint = null)
    {
        var length = normal.Length;

        if (length <= 0.0 || !double.IsFinite(length))
        {
            throw new ArgumentOutOfRangeException(
                nameof(normal), normal, "a section plane needs a normal with a direction");
        }

        Through = through;
        Normal = normal / length;

        var hint = rightHint ?? new Vec3(0.0, 0.0, 1.0);

        // Project the hint into the plane. If it is parallel to the normal it
        // projects to nothing, so fall back to whichever axis the normal is least
        // aligned with - which always leaves something to project.
        var right = hint - (Normal * Vec3.Dot(hint, Normal));

        if (right.Length < 1e-9)
        {
            var ax = Math.Abs(Normal.X);
            var ay = Math.Abs(Normal.Y);
            var az = Math.Abs(Normal.Z);

            var fallback = ax <= ay && ax <= az
                ? new Vec3(1.0, 0.0, 0.0)
                : ay <= az ? new Vec3(0.0, 1.0, 0.0) : new Vec3(0.0, 0.0, 1.0);

            right = fallback - (Normal * Vec3.Dot(fallback, Normal));
        }

        Right = right / right.Length;
        Up = Vec3.Cross(Normal, Right);
    }

    /// <summary>A point the plane passes through.</summary>
    public Vec3 Through { get; }

    /// <summary>The unit normal.</summary>
    public Vec3 Normal { get; }

    /// <summary>The in-plane direction that runs right across the page.</summary>
    public Vec3 Right { get; }

    /// <summary>The in-plane direction that runs up the page.</summary>
    public Vec3 Up { get; }

    /// <summary>Where a pair of in-plane coordinates sits in space.</summary>
    /// <param name="u">Distance along <see cref="Right"/>, in metres.</param>
    /// <param name="v">Distance along <see cref="Up"/>, in metres.</param>
    /// <returns>The point, in metres.</returns>
    public Vec3 At(double u, double v) => Through + (Right * u) + (Up * v);

    /// <summary>Where a point in space projects to in the plane.</summary>
    /// <param name="point">The point, in metres.</param>
    /// <returns>Its in-plane coordinates, in metres.</returns>
    /// <remarks>
    /// Orthographic, per RND-3: the projection that keeps a dimension measurable
    /// off the page, which is what a figure with dimensioned callouts is for.
    /// </remarks>
    public (double U, double V) Project(in Vec3 point)
    {
        var offset = point - Through;

        return (Vec3.Dot(offset, Right), Vec3.Dot(offset, Up));
    }

    /// <summary>How far a point lies off the plane.</summary>
    /// <param name="point">The point, in metres.</param>
    /// <returns>Signed distance along the normal, in metres.</returns>
    public double Offset(in Vec3 point) => Vec3.Dot(point - Through, Normal);
}
