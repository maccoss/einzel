using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A ramped stage interpolates the solved field's channel weights linearly over its own
/// duration, holds its end value after the sequence ends, and does the same for a drive
/// amplitude - which is what makes a one-phase mass scan exact.
/// </summary>
public sealed class RampedStageTests(ITestOutputHelper output)
{
    private static CompiledElectrode Plate(double potential, params CompiledTap[] taps) =>
        new()
        {
            Name = "plate", Shape = ElectrodeShape.Rectangle,
            MinX = -4.0e-3, MinY = -1.0e-3, MaxX = 4.0e-3, MaxY = 1.0e-3,
            Potential = potential, Taps = taps,
        };

    private static CompiledSolvedField Sequenced(CompiledStage[] stages, params CompiledDrive[] drives) =>
        new()
        {
            MinX = -10.0e-3, MinY = -10.0e-3, MaxX = 10.0e-3, MaxY = 10.0e-3,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            Electrodes = [stages[0].Electrodes[0]],
            Drives = drives,
            Stages = stages,
        };

    /// <summary>A plate ramped from 0 to 100 V over 10 us: exactly 50 V of pattern midway, 100 at the end and after it.</summary>
    [Fact]
    public void ADcRampIsLinearInTimeAndHoldsItsEnd()
    {
        var hold = new CompiledStage("hold", 10.0e-6, [Plate(0.0)]);
        var ramp = new CompiledStage("ramp", 10.0e-6, [Plate(0.0)]) { EndElectrodes = [Plate(100.0)] };

        var (field, report) = GeometryBuilder.Build(Sequenced([hold, ramp]));
        var driven = Assert.IsType<DrivenSolvedField>(field);
        Assert.True(driven.HasRamp);

        var point = new Vec3(0.0, 3.0e-3, 0.0);
        var atEnd = driven.PotentialAt(in point, 20.0e-6);
        var atQuarter = driven.PotentialAt(in point, 12.5e-6);
        var atMid = driven.PotentialAt(in point, 15.0e-6);
        var during = driven.PotentialAt(in point, 5.0e-6);
        var after = driven.PotentialAt(in point, 35.0e-6);

        output.WriteLine($"{report.Cycles} cycles; potential 3 mm off the plate: hold {during:F6} V, quarter {atQuarter:F6}, mid {atMid:F6}, end {atEnd:F6}, after {after:F6}");

        Assert.Equal(0.0, during);
        Assert.Equal(0.25 * atEnd, atQuarter, 12);
        Assert.Equal(0.5 * atEnd, atMid, 12);
        Assert.Equal(atEnd, after, 15);
        Assert.True(atEnd > 1.0, "the plate at 100 V must be felt 3 mm away");
    }

    /// <summary>
    /// A drive amplitude ramped from 100 to 300 V: at whole microseconds the 1 MHz
    /// sinusoid is at its peak, so the potential midway is that of 200 V applied.
    /// </summary>
    [Fact]
    public void ADriveAmplitudeRampIsLinearInTime()
    {
        var drive = new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5);
        var hold = new CompiledStage("hold", 10.0e-6, [Plate(0.0, new CompiledTap(0, 100.0, 0.0))]);
        var ramp = new CompiledStage("ramp", 10.0e-6, [Plate(0.0, new CompiledTap(0, 100.0, 0.0))])
        {
            EndElectrodes = [Plate(0.0, new CompiledTap(0, 300.0, 0.0))],
        };

        var (field, _) = GeometryBuilder.Build(Sequenced([hold, ramp], drive));
        var driven = Assert.IsType<DrivenSolvedField>(field);

        var point = new Vec3(0.0, 3.0e-3, 0.0);
        var at100 = driven.PotentialAt(in point, 5.0e-6);      // during the hold, at a peak
        var atMid = driven.PotentialAt(in point, 15.0e-6);     // midway through the ramp, at a peak
        var atEnd = driven.PotentialAt(in point, 20.0e-6);
        var quarterCycle = driven.PotentialAt(in point, 15.25e-6);

        output.WriteLine($"peak potential 3 mm off: 100 V gives {at100:F6}, mid-ramp {atMid:F6}, end {atEnd:F6}; a quarter cycle later {quarterCycle:E2}");

        Assert.Equal(2.0 * at100, atMid, 10);
        Assert.Equal(3.0 * at100, atEnd, 10);
        Assert.True(Math.Abs(quarterCycle) < 1e-9 * Math.Abs(atEnd));
    }

    /// <summary>
    /// A supply that is off at one end of a ramp and on at the other: the RF ramped from
    /// zero. The start weights have no oscillating term to interpolate from, and the
    /// alignment must supply a zero one rather than dropping the ramp.
    /// </summary>
    [Fact]
    public void ARampFromZeroAmplitudeWorks()
    {
        var drive = new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5);
        var ramp = new CompiledStage("ramp", 10.0e-6, [Plate(0.0, new CompiledTap(0, 0.0, 0.0))])
        {
            EndElectrodes = [Plate(0.0, new CompiledTap(0, 200.0, 0.0))],
        };

        var (field, _) = GeometryBuilder.Build(Sequenced([ramp], drive));
        var driven = Assert.IsType<DrivenSolvedField>(field);

        var point = new Vec3(0.0, 3.0e-3, 0.0);
        var atStart = driven.PotentialAt(in point, 0.0);
        var atMid = driven.PotentialAt(in point, 5.0e-6);
        var atEnd = driven.PotentialAt(in point, 10.0e-6);

        output.WriteLine($"from zero: start {atStart:E2}, mid {atMid:F6}, end {atEnd:F6}");
        Assert.Equal(0.0, atStart, 12);
        Assert.Equal(0.5 * atEnd, atMid, 10);
        Assert.True(atEnd > 1.0);
    }
}
