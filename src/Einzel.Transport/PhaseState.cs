using Einzel.Core.Geometry;

namespace Einzel.Transport;

/// <summary>
/// An ion's position and velocity: the integrated state, in SI.
/// </summary>
/// <param name="Position">Position, in metres.</param>
/// <param name="Velocity">Velocity, in metres per second.</param>
/// <remarks>
/// Time is not a member. It is accumulated separately with Neumaier compensation
/// (<see cref="Core.Numerics.CompensatedSum"/>) because it is the one scalar in
/// the problem whose absolute accuracy is budgeted at 1 ppm, and carrying it
/// inside the Runge-Kutta state would subject it to the same ordinary
/// floating-point addition as everything else.
/// </remarks>
public readonly record struct PhaseState(Vec3 Position, Vec3 Velocity)
{
    /// <summary>The ion's speed.</summary>
    public double Speed => Velocity.Length;
}

/// <summary>
/// The time derivative of a <see cref="PhaseState"/>.
/// </summary>
/// <param name="Velocity">The rate of change of position.</param>
/// <param name="Acceleration">The rate of change of velocity.</param>
public readonly record struct PhaseDerivative(Vec3 Velocity, Vec3 Acceleration);
