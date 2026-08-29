using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A field seen from an instant part-way through the instrument's timeline.
/// </summary>
/// <remarks>
/// The integrator always starts at t = 0 and a leg of a sequenced run does not, so a
/// trajectory phase resuming after a diffusive one has to see the field at the instant it
/// actually begins. Wrapped rather than given a start time on the integrator, which is
/// the precedent <c>AxisymmetricField</c> and <c>PonderomotiveField</c> set: the
/// transport core carries every validated number here, and refactoring it to add a case
/// beside it is how those get quietly lost.
/// </remarks>
public sealed class TimeShiftedFieldTests(ITestOutputHelper output)
{
    private static SequencedField Two() => new(
        [UniformField.Create(new Vec3(100.0, 0.0, 0.0)),
         UniformField.Create(new Vec3(900.0, 0.0, 0.0))],
        [1.0e-4, 1.1e-4]);

    /// <summary>The leg's own clock starts where the offset says.</summary>
    [Fact]
    public void TheLegSeesTheInstrumentAtItsOwnOffset()
    {
        var shifted = new TimeShiftedField(Two(), 1.0e-4);

        // t = 0 for this leg is 100 us on the instrument, which is the push.
        Assert.Equal(900.0, shifted.ElectricFieldAt(Vec3.Zero, 0.0).X, 1e-12);

        // And an unshifted leg sees the hold there.
        Assert.Equal(100.0, Two().ElectricFieldAt(Vec3.Zero, 0.0).X, 1e-12);

        output.WriteLine("the same instrument, two legs, two instants");
    }

    /// <summary>
    /// A switch is reported in the leg's clock, so the integrator can land on it.
    /// </summary>
    /// <remarks>
    /// The integrator refuses to step past what <c>NextSwitchAfter</c> returns. Reported
    /// in the instrument's clock it would be a time the leg never reaches — or worse, a
    /// time already behind it, which would stall the step.
    /// </remarks>
    [Fact]
    public void ASwitchIsReportedInTheLegsOwnClock()
    {
        var shifted = new TimeShiftedField(Two(), 5.0e-5);

        // The instrument switches at 100 us; this leg starts at 50, so it meets it at 50.
        Assert.Equal(5.0e-5, shifted.NextSwitchAfter(0.0), 1e-18);

        // And the one at 110 us is 60 us into the leg.
        Assert.Equal(6.0e-5, shifted.NextSwitchAfter(5.0e-5), 1e-18);

        Assert.Equal(double.PositiveInfinity, shifted.NextSwitchAfter(6.0e-5));
    }

    /// <summary>A switch already behind the offset is not one this leg will meet.</summary>
    /// <remarks>
    /// It must not come back negative. The integrator would read a past instant as a
    /// step it has to take backwards, and stall.
    /// </remarks>
    [Fact]
    public void ASwitchBehindTheOffsetIsNotReported()
    {
        var shifted = new TimeShiftedField(Two(), 1.05e-4);

        var next = shifted.NextSwitchAfter(0.0);

        output.WriteLine($"next switch {next:E3} s into a leg starting at 105 us");

        Assert.True(next > 0.0, "a switch this leg has already passed is not ahead of it");
        Assert.Equal(5.0e-6, next, 1e-18);
    }

    /// <summary>A zero offset changes nothing at all.</summary>
    /// <remarks>
    /// The control that makes the rest meaningful: the first leg of any sequence is
    /// unshifted, so wrapping must be the identity there rather than nearly so.
    /// </remarks>
    [Fact]
    public void AZeroOffsetIsTheIdentity()
    {
        var inner = Two();
        var shifted = new TimeShiftedField(inner, 0.0);

        foreach (var t in new[] { 0.0, 5e-5, 1.0e-4, 1.05e-4, 2.0e-4 })
        {
            Assert.Equal(
                inner.ElectricFieldAt(Vec3.Zero, t).X,
                shifted.ElectricFieldAt(Vec3.Zero, t).X);

            Assert.Equal(inner.NextSwitchAfter(t), shifted.NextSwitchAfter(t));
        }
    }

    /// <summary>A time-free caller gets the leg's start, stated rather than accidental.</summary>
    /// <remarks>
    /// A driven field answers the time-free interface without failing, which is the
    /// defect this project has found four times — in `einzel solve`, the diffusive mode,
    /// `SuperposedField` and the renderer. Here the instant it answers at is the one the
    /// leg begins at, which is at least defensible.
    /// </remarks>
    [Fact]
    public void ATimeFreeCallerGetsTheLegsStart()
    {
        var shifted = new TimeShiftedField(Two(), 1.0e-4);

        Assert.Equal(900.0, ((IElectrostaticField)shifted).ElectricFieldAt(Vec3.Zero).X, 1e-12);
    }

    /// <summary>A negative or non-finite offset is refused.</summary>
    [Fact]
    public void AnImpossibleOffsetIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeShiftedField(Two(), -1.0e-6));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeShiftedField(Two(), double.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeShiftedField(Two(), double.NaN));
    }
}
