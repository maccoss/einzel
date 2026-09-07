using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A geometry that switches between DC states and has no drive has no clock, and says so.
/// </summary>
/// <remarks>
/// <para>
/// It used to be given a placeholder clock at one hertz, with a comment saying nothing used
/// its frequency. Two things did. <c>ShortestPeriodSeconds</c> came out as one second rather
/// than infinite, and the diffusive path's pseudopotential wrapper - which gates on a finite
/// period, because a DC ramp is not an oscillation and has no cycle to average over - saw a
/// finite period and cycle-averaged a TIMS elution ramp over a one-second cycle, most of
/// which is zero field. A tunnel that measurably held its density for a millisecond let it
/// all drift out at gas speed during a 300 us hold, and reported nothing, because the leg
/// that did the averaging discarded the wrapper's own warnings.
/// </para>
/// <para>
/// The period is the value every downstream reader keys on, so it is the value asserted
/// here: infinite with no drive, finite with one, and the driven case is the control that
/// says the assertion is about the drive rather than about stages.
/// </para>
/// </remarks>
public sealed class StagedWithoutDriveTests(ITestOutputHelper output)
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

    /// <summary>DC stages alone: time-varying, and no period at all.</summary>
    [Fact]
    public void StagesWithNoDriveHaveNoPeriod()
    {
        var hold = new CompiledStage("hold", 10.0e-6, [Plate(60.0)]);
        var ramp = new CompiledStage("ramp", 10.0e-6, [Plate(60.0)]) { EndElectrodes = [Plate(0.0)] };

        var (field, _) = GeometryBuilder.Build(Sequenced([hold, ramp]));
        var driven = Assert.IsType<DrivenSolvedField>(field);

        output.WriteLine($"stages only: shortest period {driven.ShortestPeriodSeconds}");

        Assert.True(driven.HasRamp);
        Assert.True(
            double.IsPositiveInfinity(driven.ShortestPeriodSeconds),
            $"a geometry with no drive reports a period of {driven.ShortestPeriodSeconds} s - a "
            + "placeholder clock that every reader will take at its word");
    }

    /// <summary>And the same stages with a drive have that drive's period - the control.</summary>
    [Fact]
    public void StagesWithADriveHaveItsPeriod()
    {
        var drive = new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5);
        var hold = new CompiledStage("hold", 10.0e-6, [Plate(0.0, new CompiledTap(0, 100.0, 0.0))]);
        var ramp = new CompiledStage("ramp", 10.0e-6, [Plate(0.0, new CompiledTap(0, 100.0, 0.0))])
        {
            EndElectrodes = [Plate(0.0, new CompiledTap(0, 200.0, 0.0))],
        };

        var (field, _) = GeometryBuilder.Build(Sequenced([hold, ramp], drive));
        var driven = Assert.IsType<DrivenSolvedField>(field);

        output.WriteLine($"with a 1 MHz drive: shortest period {driven.ShortestPeriodSeconds * 1e9:F1} ns");

        Assert.Equal(1.0e-6, driven.ShortestPeriodSeconds, 1e-15);
    }
}
