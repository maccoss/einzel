using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Fields.Magnetic;

namespace Einzel.Fields.Tests;

/// <summary>Whether an instant can be placed in a field's drive cycle, asked through wrappers.</summary>
public sealed class FieldInstantsTests
{
    /// <summary>
    /// A drive behind a magnet's wrapper is still a drive: an instant too late to place in its
    /// cycle is refused whatever answers for the sum.
    /// </summary>
    /// <remarks>
    /// The check asked only whether the field it was handed was time-varying, so a wrapper that
    /// is not - but carries a driven field inside - skipped it.
    /// </remarks>
    [Fact]
    public void ADriveBehindAWrapperIsStillChecked()
    {
        var driven = IdealQuadrupoleRf.Create(
            Quantity.From(0.0, "V"), Quantity.From(100.0, "V"), Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"));
        var wrapped = new Wrapped(driven, new UniformMagneticField(new Vec3(0.0, 0.0, 0.1)));

        Assert.False(typeof(ITimeVaryingField).IsAssignableFrom(wrapped.GetType()), "the control: this wrapper does not answer for the drive");

        Assert.Throws<EinzelException>(() => FieldInstants.CheckResolution(1e294, wrapped, "/x"));
        FieldInstants.CheckResolution(2.5e-6, wrapped, "/x");
        FieldInstants.CheckResolution(1e294, FieldFreeSpace.Instance, "/x");
    }

    private sealed class Wrapped(IElectrostaticField electric, IMagneticField magnetic)
        : MagnetizedField(electric, magnetic);
}
