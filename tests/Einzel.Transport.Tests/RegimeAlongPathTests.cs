using Einzel.Core.Geometry;
using Einzel.Transport;
using Einzel.Transport.Collisions;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The regime numbers taken at a point rather than at the worst point (REG-2, §16).
/// </summary>
/// <remarks>
/// <para>
/// <c>RegimeDiagnostics.Measure</c> reports the worst point anywhere in the gas, which is
/// the right answer for a warning: a description that fails somewhere has failed.
/// <c>MeasureAt</c> reports one point, which is what §16's regime inspector needs — so
/// that "outside validity" becomes "outside validity between 12 and 31 millimetres",
/// which is a thing to change rather than a verdict.
/// </para>
/// <para>
/// The two must agree wherever the question is the same, and must differ wherever it is
/// not. Both halves are checked here, because either alone is much weaker: agreement
/// without a case that separates them would pass on an implementation that ignored the
/// point entirely.
/// </para>
/// </remarks>
public sealed class RegimeAlongPathTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>A density that ramps from one value to another across a metre in x.</summary>
    private static SampledGasDensity Ramp(double lowSi, double highSi) =>
        new(new SampledGrid(
            1, 2, 1, 1, new Vec3(-0.5, 0.0, 0.0), new Vec3(1.0, 0.0, 0.0),
            [lowSi, highSi]));

    private static BackgroundGas Nitrogen(double pressurePa, IGasDensity? density = null) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
        Density = density,
    };

    /// <summary>
    /// Where the gas does not vary, the point and the worst point are the same numbers.
    /// </summary>
    /// <remarks>
    /// <b>Bit-identical, not close.</b> A uniform gas makes "here" and "the worst place
    /// anywhere" the same question, so the two routes must give the same answer to the
    /// last bit — and they share a private core precisely so that they cannot drift. An
    /// approximate assertion here would pass on two implementations that had quietly
    /// acquired different terms.
    /// </remarks>
    [Theory]
    [InlineData(1e-4)]
    [InlineData(1.0)]
    [InlineData(100.0)]
    public void AUniformGasAnswersTheSameAtAPointAsAtItsWorst(double pressurePa)
    {
        var gas = Nitrogen(pressurePa);

        var worst = RegimeDiagnostics.Measure(gas, Peptide, 500.0, 1e-3, 2e-3, 1e6);

        foreach (var x in (double[])[-0.4, 0.0, 0.37])
        {
            var point = new Vec3(x, 0.0, 0.0);
            var here = RegimeDiagnostics.MeasureAt(gas, Peptide, 500.0, 1e-3, 2e-3, 1e6, in point);

            output.WriteLine($"at x = {x,6:F2} m: {here.PressureMbar:G6} mbar, Kn {here.Knudsen:G6}");

            Assert.Equal(worst, here);
        }
    }

    /// <summary>
    /// Where the gas varies, the point follows it and the worst point does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The case that makes the view worth having.</b> A funnel whose entrance is at
    /// 10 mbar and whose exit is at 0.1 mbar is in two different regimes; a single verdict
    /// for the run describes neither end. The worst-case number is the same everywhere by
    /// construction — that is what makes it safe for a warning and useless for locating
    /// anything.
    /// </para>
    /// <para>
    /// Checked against the ramp's own arithmetic: at the low end the local pressure is the
    /// low value, at the high end the high value, and the worst-case number is the high
    /// value at both.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGradedGasFollowsThePointWhileTheWorstCaseDoesNot()
    {
        const double Low = 1e20;
        const double High = 1e22;

        var gas = Nitrogen(1.0, Ramp(Low, High));

        var worst = RegimeDiagnostics.Measure(gas, Peptide, 500.0, 1e-3, 2e-3, 1e6);

        var thin = new Vec3(-0.5, 0.0, 0.0);
        var thick = new Vec3(0.5, 0.0, 0.0);

        var atThin = RegimeDiagnostics.MeasureAt(gas, Peptide, 500.0, 1e-3, 2e-3, 1e6, in thin);
        var atThick = RegimeDiagnostics.MeasureAt(gas, Peptide, 500.0, 1e-3, 2e-3, 1e6, in thick);

        output.WriteLine($"worst anywhere  {worst.PressureMbar:G6} mbar, Kn {worst.Knudsen:G6}");
        output.WriteLine($"at the thin end {atThin.PressureMbar:G6} mbar, Kn {atThin.Knudsen:G6}");
        output.WriteLine($"at the thick end {atThick.PressureMbar:G6} mbar, Kn {atThick.Knudsen:G6}");

        // A hundredfold ramp, so the two ends are a hundredfold apart in pressure and in
        // mean free path - and the worst case sits at the thick end, seeing neither.
        Assert.Equal(100.0, atThick.PressureMbar / atThin.PressureMbar, 6);
        Assert.Equal(100.0, atThin.Knudsen / atThick.Knudsen, 6);

        Assert.Equal(worst.PressureMbar, atThick.PressureMbar, 12);
        Assert.NotEqual(worst.PressureMbar, atThin.PressureMbar, 12);
    }

    /// <summary>The reduced field is a local field over a local density.</summary>
    /// <remarks>
    /// <para>
    /// E/N is what decides whether a low-field mobility applies at all, and this project
    /// has already been caught by it: 40 V/m at 1e-2 mbar is 166 townsend, deep into field
    /// heating, where the low-field value overstates the drift by 1.4 times.
    /// </para>
    /// <para>
    /// Both terms are local, so a single figure for a run says less than it appears to —
    /// checked against the definition, which is arithmetic this code had no part in: one
    /// townsend is 1e-21 volt metre squared.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReducedFieldIsLocalInBothOfItsTerms()
    {
        const double Low = 1e20;
        const double High = 1e22;
        const double FieldSi = 4000.0;

        var gas = Nitrogen(1.0, Ramp(Low, High));

        var thin = new Vec3(-0.5, 0.0, 0.0);
        var thick = new Vec3(0.5, 0.0, 0.0);

        var atThin = RegimeDiagnostics.ReducedFieldTd(gas, FieldSi, in thin);
        var atThick = RegimeDiagnostics.ReducedFieldTd(gas, FieldSi, in thick);

        output.WriteLine($"{FieldSi:G3} V/m at {Low:G2} /m3 is {atThin:G6} Td");
        output.WriteLine($"{FieldSi:G3} V/m at {High:G2} /m3 is {atThick:G6} Td");

        // E / n / 1e-21, by the definition of the townsend.
        Assert.Equal(FieldSi / Low / 1e-21, atThin, 6);
        Assert.Equal(FieldSi / High / 1e-21, atThick, 6);

        // Twice the field is twice the reduced field, at a fixed density.
        Assert.Equal(
            2.0 * atThin, RegimeDiagnostics.ReducedFieldTd(gas, 2.0 * FieldSi, in thin), 6);
    }

    /// <summary>A vacuum has an unbounded reduced field rather than a zero one.</summary>
    /// <remarks>
    /// E/N with no N is not nought, it is undefined-and-large: an ion between collisions
    /// that never collides is in the highest-field limit there is. Reporting zero would
    /// read as the safest possible regime, which is the opposite of the truth.
    /// </remarks>
    [Fact]
    public void AVacuumHasAnUnboundedReducedFieldRatherThanAZeroOne()
    {
        var vacuum = Nitrogen(0.0);
        var origin = Vec3.Zero;

        var reduced = RegimeDiagnostics.ReducedFieldTd(vacuum, 1000.0, in origin);

        output.WriteLine($"1000 V/m in vacuum is {reduced} Td");

        Assert.True(double.IsPositiveInfinity(reduced));
    }
}
