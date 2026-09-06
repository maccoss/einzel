using Einzel.Core.Geometry;

namespace Einzel.Transport.Interaction;

/// <summary>
/// The mutual Coulomb force in a packet, summed over every pair.
/// </summary>
/// <remarks>
/// <para>
/// SC-1 asks for an approximate space-charge method validated against direct
/// summation on a reference population. This is that direct summation, and it is
/// built first for the reason the requirement implies: an approximation cannot be
/// validated against something that does not exist.
/// </para>
/// <para>
/// It is also useful in its own right. A pulsed extraction packet is thousands of
/// ions, not billions, and at that size the exact sum is affordable — the packet
/// this engine warns about at 1 ppm holds about 5,600 ions. Particle-in-cell
/// exists because a plasma has 10^20 of them; a TOF bunch does not.
/// </para>
/// <para>
/// <b>Macroparticles.</b> Each computed trajectory carries a weight: a packet of
/// 10,000 ions modelled with 500 trajectories gives each of them 20 ions' worth of
/// charge and 20 ions' worth of mass. Charge-to-mass is therefore unchanged, so
/// motion in the applied field is bit-identical to the unweighted case and the
/// only thing the weight touches is the mutual force — which is the property that
/// makes the substitution honest.
/// </para>
/// </remarks>
public sealed class CoulombInteraction : ISelfField
{
    /// <summary>Coulomb's constant, 1/(4 pi eps0), in N m^2 / C^2.</summary>
    public const double CoulombConstantSi = 1.0 / (4.0 * Math.PI * SpaceCharge.PermittivitySi);

    private readonly double _chargePerMacroparticleSi;
    private readonly double _massPerMacroparticleSi;
    private readonly double _softeningSquaredSi;

    /// <summary>Builds the interaction for a weighted packet.</summary>
    /// <param name="population">Ions in the physical packet.</param>
    /// <param name="macroparticles">Trajectories actually computed.</param>
    /// <param name="chargeSi">Charge of one real ion, in coulombs.</param>
    /// <param name="massSi">Mass of one real ion, in kilograms.</param>
    /// <param name="softeningLengthSi">
    /// Plummer softening. Two macroparticles at the same point would otherwise
    /// exert an infinite force on each other, which is an artefact of replacing a
    /// smooth cloud by points and not physics.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A non-positive population, macroparticle count, mass, or softening length.
    /// </exception>
    public CoulombInteraction(
        double population, int macroparticles, double chargeSi, double massSi, double softeningLengthSi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(population);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(macroparticles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massSi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(softeningLengthSi);

        Weight = population / macroparticles;

        _chargePerMacroparticleSi = chargeSi * Weight;
        _massPerMacroparticleSi = massSi * Weight;
        _softeningSquaredSi = softeningLengthSi * softeningLengthSi;

        SofteningLengthSi = softeningLengthSi;
    }

    /// <inheritdoc/>
    public double Weight { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// The reference SC-1 asks an approximate method to be validated against, so it
    /// says so rather than naming an implementation.
    /// </remarks>
    public string Method => "direct";

    /// <summary>The Plummer softening length, in metres.</summary>
    /// <remarks>
    /// Reported rather than hidden, because it is a modelling choice that changes
    /// the answer: the force between two macroparticles closer together than this
    /// is deliberately not the Coulomb force. Below it, a cloud of points is not a
    /// description of a smooth packet at all.
    /// </remarks>
    public double SofteningLengthSi { get; }

    /// <summary>
    /// A softening length from the packet's own size: the mean spacing between
    /// macroparticles.
    /// </summary>
    /// <param name="radiusSi">The packet's effective radius.</param>
    /// <param name="macroparticles">How many trajectories share it.</param>
    /// <returns>A softening length, in metres.</returns>
    /// <remarks>
    /// The scale below which the macroparticle description has nothing to say. A
    /// smaller softening resolves structure that is sampling noise; a larger one
    /// smooths away structure the packet really has.
    /// </remarks>
    public static double SpacingSoftening(double radiusSi, int macroparticles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(macroparticles);

        return radiusSi <= 0.0
            ? double.Epsilon
            : radiusSi / Math.Cbrt(macroparticles);
    }

    /// <summary>
    /// A softening length from the packet's shape: the mean spacing between
    /// macroparticles in a packet whose standard deviations along the three axes are
    /// given, which for an isotropic packet is exactly <see cref="SpacingSoftening(double, int)"/>
    /// of its effective radius and for a long thin one is far smaller.
    /// </summary>
    /// <param name="sigmaX">Standard deviation of position along x, in metres.</param>
    /// <param name="sigmaY">Along y.</param>
    /// <param name="sigmaZ">Along z.</param>
    /// <param name="macroparticles">How many trajectories share the packet.</param>
    /// <returns>A softening length, in metres.</returns>
    /// <remarks>
    /// <para>
    /// The radius rule divides the packet's RMS radius by the cube root of the count, and
    /// the RMS radius of a line is its length: forty macroparticles along ten millimetres
    /// of a linear trap's axis and fifty microns across it were softened at 1.7 mm,
    /// thirty-four times the transverse size, and the force across the packet was switched
    /// off. The spacing of points filling a box is the cube root of its volume over the
    /// count, and the volume goes as the product of the three extents, not the cube of the
    /// largest. Written so that an isotropic packet gives the radius rule's number exactly:
    /// the effective radius of a Gaussian packet is root five times its sigma, so this is
    /// root five times the geometric mean sigma, over the cube root of the count.
    /// </para>
    /// <para>
    /// A vanishing extent is floored at a thousandth of the largest, because a packet
    /// declared with no spread along an axis still has to be softened at something.
    /// </para>
    /// </remarks>
    public static double SpacingSoftening(double sigmaX, double sigmaY, double sigmaZ, int macroparticles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(macroparticles);

        var largest = Math.Max(sigmaX, Math.Max(sigmaY, sigmaZ));
        if (!(largest > 0.0))
        {
            return double.Epsilon;
        }

        var floor = 1e-3 * largest;
        var geometricMean = Math.Cbrt(
            Math.Max(sigmaX, floor) * Math.Max(sigmaY, floor) * Math.Max(sigmaZ, floor));

        return Math.Sqrt(5.0) * geometricMean / Math.Cbrt(macroparticles);
    }

    /// <summary>
    /// Adds the mutual acceleration of every active pair into an accumulator.
    /// </summary>
    /// <param name="positions">Position of each macroparticle.</param>
    /// <param name="active">Which macroparticles are still in the packet.</param>
    /// <param name="accelerations">Accumulator, added to rather than overwritten.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The arrays are not the same length.</exception>
    /// <remarks>
    /// <para>
    /// Each pair is visited once and the equal and opposite accelerations applied
    /// together, so Newton's third law holds by construction rather than by
    /// cancellation of two separately computed sums. Total momentum is then
    /// conserved to round-off, which is the cheapest exact check there is that the
    /// sum has not been written with a sign or an index wrong.
    /// </para>
    /// <para>
    /// An absorbed macroparticle stops contributing. That is physics, not
    /// bookkeeping: an ion that has struck an electrode has been neutralised and is
    /// no longer part of the packet's charge.
    /// </para>
    /// </remarks>
    public void Accumulate(
        ReadOnlySpan<Vec3> positions, ReadOnlySpan<bool> active, Span<Vec3> accelerations)
    {
        if (positions.Length != active.Length || positions.Length != accelerations.Length)
        {
            throw new ArgumentException(
                $"positions ({positions.Length}), active ({active.Length}) and accelerations "
                + $"({accelerations.Length}) must be the same length");
        }

        // Force between two macroparticles is k q^2 / r^2; dividing by the mass of
        // the one being accelerated gives k q^2 / m per unit inverse-square, and
        // both are the same here because a packet is one species.
        var strength = CoulombConstantSi
            * _chargePerMacroparticleSi * _chargePerMacroparticleSi
            / _massPerMacroparticleSi;

        for (var i = 0; i < positions.Length; i++)
        {
            if (!active[i])
            {
                continue;
            }

            for (var j = i + 1; j < positions.Length; j++)
            {
                if (!active[j])
                {
                    continue;
                }

                var separation = positions[i] - positions[j];

                var distanceSquared =
                    (separation.X * separation.X)
                    + (separation.Y * separation.Y)
                    + (separation.Z * separation.Z)
                    + _softeningSquaredSi;

                // Plummer: the magnitude is k q^2 / (r^2 + eps^2), and the unit
                // vector costs another power of the softened distance.
                var distance = Math.Sqrt(distanceSquared);
                var scale = strength / (distanceSquared * distance);

                var push = separation * scale;

                accelerations[i] += push;
                accelerations[j] -= push;
            }
        }
    }

    /// <summary>
    /// The electrostatic potential at a point, from every active macroparticle.
    /// </summary>
    /// <param name="point">Where to evaluate.</param>
    /// <param name="positions">Position of each macroparticle.</param>
    /// <param name="active">Which macroparticles are still in the packet.</param>
    /// <returns>The potential, in volts.</returns>
    /// <exception cref="ArgumentException">The arrays are not the same length.</exception>
    /// <remarks>
    /// Not needed to advance anything. It exists so the sum can be checked against
    /// the closed form the screening estimate uses — a uniformly charged sphere is
    /// 3Q/(8 pi eps0 R) from centre to surface — which is two independent routes to
    /// one number and the kind of check this engine trusts most.
    /// </remarks>
    public double PotentialAt(in Vec3 point, ReadOnlySpan<Vec3> positions, ReadOnlySpan<bool> active)
    {
        if (positions.Length != active.Length)
        {
            throw new ArgumentException(
                $"positions ({positions.Length}) and active ({active.Length}) must be the same length");
        }

        var total = 0.0;

        for (var k = 0; k < positions.Length; k++)
        {
            if (!active[k])
            {
                continue;
            }

            var separation = point - positions[k];

            var distanceSquared =
                (separation.X * separation.X)
                + (separation.Y * separation.Y)
                + (separation.Z * separation.Z)
                + _softeningSquaredSi;

            total += 1.0 / Math.Sqrt(distanceSquared);
        }

        return CoulombConstantSi * _chargePerMacroparticleSi * total;
    }
}
