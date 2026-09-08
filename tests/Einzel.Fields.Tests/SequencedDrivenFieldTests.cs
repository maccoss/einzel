using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A sequence whose states are DRIVEN, which is the case the class was written without.
/// </summary>
/// <remarks>
/// <para>
/// <c>FieldAssembly.Sequenced</c> wraps whatever the per-phase build returns, so an analytic
/// element carrying a drive and a sequence becomes a <see cref="SequencedField"/> of driven
/// states. The class was written for static ones and said so - "a sequence of static states
/// has nothing oscillating in it" - and three things followed from the assumption, all silent:
/// </para>
/// <para>
/// It selected the state by time and then read it through the TIME-FREE accessor, so the
/// drive was pinned at whatever that answers rather than oscillating. It reported an infinite
/// shortest period, so the diffusive path never cycle-averaged such a field and step control
/// had no drive period to cap against. And it had no way to hold its operating point, so a
/// cycle average opening near a phase boundary blended two states.
/// </para>
/// <para>
/// The static case is asserted alongside each of these, because it is what must not move:
/// almost every sequenced model in this project has static states.
/// </para>
/// </remarks>
public sealed class SequencedDrivenFieldTests(ITestOutputHelper output)
{
    private const double FrequencyHz = 1.0e6;

    private const double PeriodSeconds = 1.0 / FrequencyHz;

    /// <summary>The boundary between the two states, well clear of a drive period.</summary>
    private const double BoundarySeconds = 100.0 * PeriodSeconds;

    private static readonly Vec3 Probe = new(0.001, 0.0, 0.0);

    private static OscillatingUniformField Driven(double voltsPerMetre) =>
        OscillatingUniformField.Create(
            new Vec3(voltsPerMetre, 0.0, 0.0), Quantity.From(FrequencyHz, "Hz"));

    private static UniformField Static(double voltsPerMetre) =>
        UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    /// <summary>Two driven states, 100 then 900 V/m, switching at the boundary.</summary>
    private static SequencedField TwoDriven() => new(
        [Driven(100.0), Driven(900.0)],
        [BoundarySeconds, 2.0 * BoundarySeconds]);

    private static SequencedField TwoStatic() => new(
        [Static(100.0), Static(900.0)],
        [BoundarySeconds, 2.0 * BoundarySeconds]);

    /// <summary>The extremes of the axial field over one drive period from an instant.</summary>
    private static (double Lowest, double Highest) Swing(
        ITimeVaryingField field, double fromSeconds)
    {
        var lowest = double.MaxValue;
        var highest = double.MinValue;

        for (var s = 0; s < 64; s++)
        {
            var x = field.ElectricFieldAt(in Probe, fromSeconds + (s * PeriodSeconds / 64.0)).X;

            lowest = Math.Min(lowest, x);
            highest = Math.Max(highest, x);
        }

        return (lowest, highest);
    }

    /// <summary>
    /// A driven state oscillates, which is the defect: the state was chosen by time and then
    /// read through the time-free accessor, so its drive stood still.
    /// </summary>
    /// <remarks>
    /// The extremes over a period are asserted rather than a phase convention, so this does
    /// not depend on whether the waveform starts at its peak. What it discriminates is the
    /// bug: a pinned drive gives one value at every sample and a swing of zero.
    /// </remarks>
    [Fact]
    public void ADrivenStateOscillates()
    {
        var (lowest, highest) = Swing(TwoDriven(), 10.0 * PeriodSeconds);

        output.WriteLine($"first state, over one period: {lowest:F3} to {highest:F3} V/m");

        Assert.Equal(-100.0, lowest, 0.5);
        Assert.Equal(100.0, highest, 0.5);
    }

    /// <summary>
    /// And a static state does not, which is the control that says the fix did not turn a
    /// held state into an oscillating one.
    /// </summary>
    [Fact]
    public void AStaticStateStillHolds()
    {
        var (lowest, highest) = Swing(TwoStatic(), 10.0 * PeriodSeconds);

        output.WriteLine($"first static state: {lowest:F3} to {highest:F3} V/m");

        Assert.Equal(100.0, lowest, 1e-12);
        Assert.Equal(100.0, highest, 1e-12);
    }

    /// <summary>
    /// The shortest period is the states' own, so a driven sequence is cycle-averaged and
    /// step-controlled rather than reporting that it has no timescale at all.
    /// </summary>
    /// <remarks>
    /// The static case returning infinity is the other half: <c>DiffusionRun.Effective</c>
    /// takes an early return on a non-finite period, and that early return is what correctly
    /// stops a DC ramp being averaged over a cycle it does not have.
    /// </remarks>
    [Fact]
    public void ThePeriodIsTheStatesOwnAndInfiniteWhenThereIsNone()
    {
        var driven = TwoDriven().ShortestPeriodSeconds;
        var stationary = TwoStatic().ShortestPeriodSeconds;

        output.WriteLine($"driven states:  {driven * 1e9:F1} ns");
        output.WriteLine($"static states:  {stationary}");

        Assert.Equal(PeriodSeconds, driven, 1e-18);
        Assert.Equal(double.PositiveInfinity, stationary);
    }

    /// <summary>
    /// Holding the operating point pins WHICH state, and leaves the drive oscillating.
    /// </summary>
    /// <remarks>
    /// Both halves matter and only together. Pinning without the drive would be the original
    /// defect wearing a new name; oscillating without pinning is what let a cycle average
    /// opening near a boundary blend two states. The two amplitudes are ninefold apart so a
    /// blend is not mistakable for either.
    /// </remarks>
    [Fact]
    public void HoldingTheOperatingPointPinsTheStateAndKeepsTheDrive()
    {
        var field = TwoDriven();

        // An operating point inside the FIRST state, then sampled at instants inside the
        // second state's window. Unheld those give 900 V/m; held they must stay at 100.
        var held = field.AtOperatingPoint(10.0 * PeriodSeconds);

        var (heldLow, heldHigh) = Swing(held, BoundarySeconds + (10.0 * PeriodSeconds));
        var (freeLow, freeHigh) = Swing(field, BoundarySeconds + (10.0 * PeriodSeconds));

        output.WriteLine($"held at the first state:  {heldLow:F3} to {heldHigh:F3} V/m");
        output.WriteLine($"not held:                 {freeLow:F3} to {freeHigh:F3} V/m");

        // The control: unheld, the same instants really are in the second state.
        Assert.Equal(900.0, freeHigh, 5.0);

        // Held: still the first state's amplitude, and still swinging.
        Assert.Equal(-100.0, heldLow, 0.5);
        Assert.Equal(100.0, heldHigh, 0.5);
    }

    /// <summary>
    /// A single-state sequence has one answer for the selection, so holding it returns the
    /// same instance and costs nothing.
    /// </summary>
    [Fact]
    public void ASingleStateSequenceIsHeldForFree()
    {
        var one = new SequencedField([Static(100.0)], [BoundarySeconds]);

        Assert.Same(one, one.AtOperatingPoint(10.0 * PeriodSeconds));
    }

    /// <summary>
    /// A non-finite operating point is refused rather than selecting the last state and
    /// carrying NaN into everything downstream.
    /// </summary>
    /// <remarks>
    /// A comparison against NaN is false, so <c>At</c> would fall through its whole loop and
    /// return the final state - a plausible field for the wrong phase, with nothing raised.
    /// This project has four recorded cases of a non-finite double reaching a result
    /// silently, one of which took a whole result document down at the serialiser after the
    /// run had succeeded.
    /// </remarks>
    [Fact]
    public void ANonFiniteOperatingPointIsRefused()
    {
        var field = TwoDriven();

        foreach (var bad in (double[])[double.NaN, double.PositiveInfinity, double.NegativeInfinity])
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => field.AtOperatingPoint(bad));

            Assert.Contains("finite", thrown.Message, StringComparison.Ordinal);
        }
    }
}
