using Einzel.Core.Geometry;

namespace Einzel.Transport.Interaction;

/// <summary>
/// The force a packet exerts on itself, however it is computed.
/// </summary>
/// <remarks>
/// <para>
/// SC-1 names two methods and a relationship between them: a direct pairwise sum as
/// the reference, and an approximate method validated against it. They are peers
/// behind this interface for the same reason <c>ITransportMode</c> exists — a caller
/// that had to know which one it held would end up knowing why, and the choice would
/// stop being the model's.
/// </para>
/// <para>
/// The contract is deliberately the same shape as the direct sum's inner loop:
/// positions in, accelerations accumulated out. That is what makes an approximate
/// method substitutable at all, and it is what lets the two be run on identical
/// configurations and differenced — which is the only way "validated against" means
/// anything.
/// </para>
/// <para>
/// <strong>Accumulated, not assigned.</strong> The applied field has already written
/// its acceleration into the span by the time this is called, and a method that
/// overwrote it would silently delete the instrument.
/// </para>
/// </remarks>
public interface ISelfField
{
    /// <summary>Real ions represented by one computed trajectory.</summary>
    /// <remarks>
    /// A macroparticle carries the charge <em>and</em> the mass of everything it
    /// stands for, so its charge-to-mass ratio is unchanged and its motion in the
    /// applied field is identical. The weight touches only the mutual force.
    /// </remarks>
    double Weight { get; }

    /// <summary>
    /// A short description of how this method computes the force, for a result to
    /// carry.
    /// </summary>
    /// <remarks>
    /// GRD-1's spirit: an approximate self-field is a modelling choice that changes
    /// the answer, so a number computed with one should be able to say which it was
    /// without the reader inspecting the model.
    /// </remarks>
    string Method { get; }

    /// <summary>Adds each macroparticle's share of the packet's self-force.</summary>
    /// <param name="positions">Where every macroparticle is, in metres.</param>
    /// <param name="active">
    /// Which are still in flight. An absorbed macroparticle stops contributing:
    /// that is physics rather than bookkeeping, since an ion that has struck an
    /// electrode has been neutralised and is no longer part of the packet's charge.
    /// </param>
    /// <param name="accelerations">
    /// Accelerations in metres per second squared, already holding the applied
    /// field's contribution and added to here.
    /// </param>
    /// <exception cref="System.ArgumentException">The spans are of different lengths.</exception>
    void Accumulate(
        ReadOnlySpan<Vec3> positions, ReadOnlySpan<bool> active, Span<Vec3> accelerations);
}
