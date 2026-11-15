using Einzel.Core.Geometry;

namespace Einzel.Fields.Magnetic;

/// <summary>
/// A static magnetic flux density B, which enters the equation of motion as the
/// velocity-dependent half of the Lorentz force, <c>q v x B</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate interface rather than a member added to <see cref="IElectrostaticField"/>,</b>
/// for the reason <see cref="ITimeVaryingField"/> is separate: code that has been taught
/// about a magnetic field asks for it by type, and code that has not keeps its fast path
/// and cannot be handed a B it will quietly ignore through a default member. Every engine
/// path that consumes a field and cannot honour a velocity-dependent force asks
/// <c>field is IMagneticField</c> and refuses (see <see cref="MagneticRefusal"/>).
/// </para>
/// <para>
/// <b>Where time would go.</b> This is a static field: the position is the only argument.
/// A time-varying B (a swept magnet, an RF magnetic component) would be a second interface
/// deriving from this one with a <c>MagneticFieldAt(position, timeSeconds)</c> overload,
/// exactly as <see cref="ITimeVaryingField"/> derives from <see cref="IElectrostaticField"/>,
/// and <c>DormandPrince54.Derivative</c> would test for it the way it tests for a driven
/// electric field. A solved or imported B map would implement this interface as it stands.
/// </para>
/// <para>
/// B does no work, so the total-energy diagnostic of a flight through a static magnetic
/// field stays meaningful: kinetic plus electric potential energy is still conserved.
/// </para>
/// </remarks>
public interface IMagneticField
{
    /// <summary>The magnetic flux density at a point, in tesla.</summary>
    /// <param name="position">Position, in metres.</param>
    /// <returns>B, in tesla.</returns>
    Vec3 MagneticFieldAt(in Vec3 position);

    /// <summary>
    /// An upper bound on |B| anywhere, in tesla. Sets the cyclotron-period step cap.
    /// </summary>
    /// <remarks>
    /// A bound rather than the exact maximum: a sum of elements reports the sum of their
    /// bounds, which is never smaller than the true peak, so the cap it implies is never
    /// looser than the physics needs.
    /// </remarks>
    double MaximumFluxDensity { get; }

    /// <summary>
    /// How far an ion may travel in a straight line from <paramref name="position"/> along
    /// <paramref name="direction"/> before any magnetic field acts on it, in metres.
    /// </summary>
    /// <param name="position">Start of the ray, in metres.</param>
    /// <param name="direction">Unit direction of the ray.</param>
    /// <returns>
    /// Zero where B acts at the start point; positive infinity where the ray never meets a
    /// field.
    /// </returns>
    /// <remarks>
    /// Named differently from <see cref="IElectrostaticField.FieldFreeRunLength"/> because a
    /// combined field implements both and the two are different questions: one is about E,
    /// this is about B, and the analytic drift may be taken only for the shorter.
    /// </remarks>
    double MagnetFreeRunLength(in Vec3 position, in Vec3 direction);

    /// <summary>
    /// Signed distance to the nearest surface across which B jumps, in metres; positive
    /// infinity for a smooth field.
    /// </summary>
    /// <param name="position">Position, in metres.</param>
    /// <returns>The signed distance; the sign changes on crossing.</returns>
    double SignedDistanceToMagneticDiscontinuity(in Vec3 position) => double.PositiveInfinity;
}

/// <summary>The refusal every path that cannot honour a magnetic field raises.</summary>
/// <remarks>
/// <para>
/// One function, so the message is the same wherever it is met and so the guard is a
/// one-line call at each consumer rather than a paraphrase. The model validator refuses the
/// same configurations earlier with a JSON pointer; this is the backstop for a caller that
/// builds a field and hands it to a consumer without going through the validator.
/// </para>
/// <para>
/// <b>Why refuse rather than ignore:</b> a magnetic field dropped by a consumer does not
/// fail, it answers - an ion that should have turned through a sector flies straight, and
/// nothing in the result says the magnet was skipped. That is the "capability wired into
/// N-1 of N paths" shape this project has met many times.
/// </para>
/// </remarks>
public static class MagneticRefusal
{
    /// <summary>Throws when <paramref name="field"/> carries a magnetic field.</summary>
    /// <param name="field">The field the consumer was handed.</param>
    /// <param name="consumer">What the consumer is, in words, for the message.</param>
    /// <exception cref="Core.Errors.EinzelException">
    /// The field carries a magnetic component this consumer would ignore.
    /// </exception>
    public static void ThrowIfMagnetic(object? field, string consumer)
    {
        if (field is IMagneticField)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = "/fields",
                Constraint = $"{consumer} cannot honour a magnetic field: the v x B force is "
                    + "applied only by single-ion trajectory integration",
                Suggestion = "fly the model with transport mode \"trajectory\", no space charge "
                    + "and no diffusive phase, or remove the magnetic element",
            });
        }
    }
}
