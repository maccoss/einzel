using Einzel.Analysis;

using Xunit.Abstractions;

namespace Einzel.Analysis.Tests;

/// <summary>
/// A peak whose arrivals carry no spread has an <i>unbounded</i> resolving power, and it
/// used to be reported as zero.
/// </summary>
/// <remarks>
/// <para>
/// Resolving power is <c>t / 2dt</c>, so a width of zero makes it infinite — the best
/// conceivable value. The run printed <c>resolving 0</c>, the worst, beside a warning
/// saying "the resolving power is unbounded". The two contradicted each other on the same
/// screen.
/// </para>
/// <para>
/// It reached a shipped example. <c>slit-transmission</c> grounds both jaws so the field is
/// exactly zero and the transmission is pure geometry — which also means every ion arrives
/// at the same instant, so the peak is degenerate by construction and the wrong number was
/// printed on every run of it.
/// </para>
/// </remarks>
public sealed class UnresolvedPeakTests(ITestOutputHelper output)
{
    /// <summary>A degenerate peak reports no resolving power rather than the worst one.</summary>
    /// <remarks>
    /// NaN rather than infinity: absent is what this surface means by "there is no answer
    /// here", <c>FiniteDoubleConverter</c> writes either as null, and NaN does not invite
    /// arithmetic that would propagate silently.
    /// </remarks>
    [Fact]
    public void ArrivalsWithNoSpreadHaveAnUndefinedResolvingPower()
    {
        // Every ion at the same instant, which is what a field-free slit does.
        var peak = ArrivalTimePeak.FromArrivals(Enumerable.Repeat(1.0e-6, 500), launched: 500);

        var (resolving, _, _, warnings) = peak.ResolvingPower();

        output.WriteLine($"resolving power {resolving.SiValue}");

        foreach (var warning in warnings)
        {
            output.WriteLine($"  [{warning.Severity}] {warning.Code}");
        }

        Assert.True(
            double.IsNaN(resolving.SiValue),
            $"a peak with no width has an unbounded resolving power, and this reported "
            + $"{resolving.SiValue:G6} - which for this quantity is the WORST possible "
            + "value standing in for the best");

        // The warning still travels, because the reader has to be told why there is no
        // number rather than left to infer it from an absence.
        Assert.Contains(warnings, w => w.Code == "PEAK_UNRESOLVED");
    }

    /// <summary>An ordinary peak still reports a real resolving power.</summary>
    /// <remarks>
    /// The control that stops the fix above from being "return NaN always". Arrivals spread
    /// over 10 ns about 1 us give a resolving power of order a hundred, and what is asserted
    /// is that it is finite and positive rather than a particular value — the value belongs
    /// to whichever definition of width the figure uses, and this test is about the
    /// degenerate case.
    /// </remarks>
    [Fact]
    public void AnOrdinaryPeakStillHasOne()
    {
        var arrivals = Enumerable
            .Range(0, 501)
            .Select(k => 1.0e-6 + ((k - 250) * 4.0e-11))
            .ToList();

        var (resolving, _, _, warnings) = ArrivalTimePeak
            .FromArrivals(arrivals, launched: arrivals.Count)
            .ResolvingPower();

        output.WriteLine($"resolving power {resolving.SiValue:G6}");

        Assert.False(
            double.IsNaN(resolving.SiValue),
            "a peak with a width has a resolving power and it must be reported");

        Assert.True(resolving.SiValue > 0.0);

        Assert.DoesNotContain(warnings, w => w.Code == "PEAK_UNRESOLVED");
    }
}
