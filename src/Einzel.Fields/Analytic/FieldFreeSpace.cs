using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// Empty space. The field is identically zero everywhere.
/// </summary>
/// <remarks>
/// Trivial, and the sharpest test the integrator has: a straight line at constant
/// speed has an exact answer, so any error the driver reports here is entirely
/// its own. It is also the case where analytic drift should skip the whole flight
/// in a single advance.
/// </remarks>
public sealed class FieldFreeSpace : IElectrostaticField
{
    /// <summary>The single shared instance.</summary>
    public static FieldFreeSpace Instance { get; } = new();

    private FieldFreeSpace()
    {
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position) => Vec3.Zero;

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position) => 0.0;

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => double.PositiveInfinity;
}
