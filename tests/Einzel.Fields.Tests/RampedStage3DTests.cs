using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A ramped stage on a volume solve does what it does on a cross-section: the channel
/// weights interpolate linearly over the stage, the end value holds after the sequence,
/// and a drive amplitude ramps the same way. The volume path was a refusal until the
/// linear-ion-trap's three-section scan needed it.
/// </summary>
public sealed class RampedStage3DTests(ITestOutputHelper output)
{
    private static CompiledElectrode3D Plate(double potential, params CompiledTap[] taps) =>
        new()
        {
            Name = "plate", Shape = Electrode3DShape.Box,
            MinX = -4.0e-3, MinY = -1.0e-3, MinZ = -4.0e-3,
            MaxX = 4.0e-3, MaxY = 1.0e-3, MaxZ = 4.0e-3,
            Potential = potential, Taps = taps,
        };

    private static Geometry3D Sequenced(CompiledStage3D[] stages, params CompiledDrive[] drives) =>
        new(-8.0e-3, -8.0e-3, -8.0e-3, 8.0e-3, 8.0e-3, 8.0e-3, 1.0e-3, [stages[0].Electrodes[0]], 1e-10)
        {
            Drives = drives,
            Stages = stages,
        };

    /// <summary>A plate ramped from 0 to 100 V over 10 us: exactly a quarter, a half and all of the pattern at those points, held after.</summary>
    [Fact]
    public void ADcRampIsLinearInTimeAndHoldsItsEnd()
    {
        var hold = new CompiledStage3D("hold", 10.0e-6, [Plate(0.0)]);
        var ramp = new CompiledStage3D("ramp", 10.0e-6, [Plate(0.0)]) { EndElectrodes = [Plate(100.0)] };

        var (field, report) = GeometryBuilder3D.BuildField(Sequenced([hold, ramp]));
        var driven = Assert.IsType<DrivenSolvedField>(field);
        Assert.True(driven.HasRamp);

        var point = new Vec3(0.0, 3.0e-3, 0.0);
        var during = driven.PotentialAt(in point, 5.0e-6);
        var atQuarter = driven.PotentialAt(in point, 12.5e-6);
        var atMid = driven.PotentialAt(in point, 15.0e-6);
        var atEnd = driven.PotentialAt(in point, 20.0e-6);
        var after = driven.PotentialAt(in point, 35.0e-6);

        output.WriteLine($"{report.Cycles} cycles; potential 3 mm off the plate: hold {during:F6} V, quarter {atQuarter:F6}, mid {atMid:F6}, end {atEnd:F6}, after {after:F6}");

        Assert.Equal(0.0, during);
        Assert.Equal(0.25 * atEnd, atQuarter, 12);
        Assert.Equal(0.5 * atEnd, atMid, 12);
        Assert.Equal(atEnd, after, 15);
        Assert.True(atEnd > 1.0, "the plate at 100 V must be felt 3 mm away");
    }

    /// <summary>
    /// A drive amplitude ramped from zero to 300 V: the pattern is solved because the
    /// ramp's end is one of the states gathered, and at whole microseconds the 1 MHz
    /// sinusoid is at its peak, so the potential midway is that of 150 V applied.
    /// </summary>
    [Fact]
    public void ADriveAmplitudeRampFromZeroIsLinearInTime()
    {
        var drive = new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5);
        var ramp = new CompiledStage3D("ramp", 10.0e-6, [Plate(0.0, new CompiledTap(0, 0.0, 0.0))])
        {
            EndElectrodes = [Plate(0.0, new CompiledTap(0, 300.0, 0.0))],
        };

        var (field, _) = GeometryBuilder3D.BuildField(Sequenced([ramp], drive));
        var driven = Assert.IsType<DrivenSolvedField>(field);

        var point = new Vec3(0.0, 3.0e-3, 0.0);
        var atStart = driven.PotentialAt(in point, 0.0);
        var atMid = driven.PotentialAt(in point, 5.0e-6);
        var atEnd = driven.PotentialAt(in point, 10.0e-6);
        var after = driven.PotentialAt(in point, 17.0e-6);

        output.WriteLine($"start {atStart:F6} V, mid {atMid:F6}, end {atEnd:F6}, held {after:F6}");

        Assert.Equal(0.0, atStart, 12);
        Assert.Equal(0.5 * atEnd, atMid, 10);
        Assert.Equal(atEnd, after, 10);
        Assert.True(atEnd > 3.0, "a 300 V drive at its peak must be felt 3 mm away");
    }
}
