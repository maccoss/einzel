using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// What a field answers from before a switch when it is held at an operating point, and when a
/// rectangular drive's edge lies on the switch.
/// </summary>
/// <remarks>
/// <para>
/// A field held at an operating point is one state at every instant - that is what holding it
/// means, and why a cycle average near a phase boundary cannot blend two. Asked from before a
/// switch it answered with the state ending there, so a held field had two states after all,
/// one on each side of every boundary; and it went on reporting switches it does not have.
/// </para>
/// <para>
/// A rectangular wave jumps at its edges, and every waveform's "from before" was the value at
/// the instant - the level after the edge, which is the next phase's. A step that ends on a
/// switch lying on an edge belongs to the level before it.
/// </para>
/// </remarks>
public sealed class HeldAndEdgeLimitTests(ITestOutputHelper output)
{
    private static readonly Vec3 Probe = new(0.0, 1.0e-3, 0.0);

    private static UniformField Uniform(double voltsPerMetre) => UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    /// <summary>A held sequence is one state on both sides of every boundary, and reports no switch.</summary>
    [Fact]
    public void AHeldSequenceIsOneStateAtEveryInstant()
    {
        var sequence = new SequencedField([Uniform(1.0), Uniform(2.0), Uniform(3.0)], [1e-6, 2e-6, 3e-6]);
        // Held on a boundary, which is where "before" could pick the other state: the state that
        // starts there, at every instant and from either side.
        var held = sequence.AtOperatingPoint(1e-6);

        foreach (var boundary in new[] { 1e-6, 2e-6, 3e-6 })
        {
            Assert.Equal(2.0, held.ElectricFieldAt(Probe, boundary).X);
            Assert.Equal(2.0, held.ElectricFieldBefore(Probe, boundary).X);
            Assert.Equal(held.PotentialAt(Probe, boundary), held.PotentialBefore(Probe, boundary));
        }

        Assert.Equal(double.PositiveInfinity, held.NextSwitchAfter(0.0));

        // The control: unheld, the same field has two states at each boundary and its switches.
        Assert.Equal(1.0, sequence.ElectricFieldBefore(Probe, 1e-6).X);
        Assert.Equal(2.0, sequence.ElectricFieldAt(Probe, 1e-6).X);
        Assert.Equal(1e-6, sequence.NextSwitchAfter(0.0));
    }

    /// <summary>A held staged solve is one stage on both sides of its boundary, and reports no switch.</summary>
    [Fact]
    public void AHeldStagedSolveIsOneStageAtEveryInstant()
    {
        var field = (ITimeVaryingField)GeometryBuilder.Build(Pusher(
            [new CompiledStage("hold", 1e-6, [Plate(10.0, [])]), new CompiledStage("push", 1e-6, [Plate(4000.0, [])])],
            [Plate(10.0, [])],
            [])).Field;
        // Held on the boundary, the push that starts there - from either side of it.
        var held = field.AtOperatingPoint(1e-6);

        var at = held.ElectricFieldAt(Probe, 1e-6).X;
        var before = held.ElectricFieldBefore(Probe, 1e-6).X;

        output.WriteLine($"held in the push: at the switch {at:G6} V/m, from before {before:G6} V/m");

        Assert.Equal(at, before);
        Assert.Equal(held.PotentialAt(Probe, 1e-6), held.PotentialBefore(Probe, 1e-6));
        Assert.Equal(double.PositiveInfinity, held.NextSwitchAfter(0.0));

        // The control: unheld, the two sides of the boundary differ by the stages' 400-fold step.
        Assert.NotEqual(field.ElectricFieldAt(Probe, 1e-6).X, field.ElectricFieldBefore(Probe, 1e-6).X);
        Assert.Equal(1e-6, field.NextSwitchAfter(0.0));
    }

    /// <summary>A sequenced field bounded to a region reports its switches through the region.</summary>
    [Fact]
    public void ABoundedSequenceReportsItsSwitches()
    {
        var sequence = new SequencedField([Uniform(1.0), Uniform(2.0)], [1e-6, 2e-6]);
        var bounded = (ITimeVaryingField)BoundedField.Around(sequence, new FieldRegion(-1.0, 1.0, -1.0, 1.0, -1.0, 1.0));

        Assert.Equal(1e-6, bounded.NextSwitchAfter(0.0));
        Assert.Equal(2e-6, bounded.NextSwitchAfter(1e-6));
        Assert.Equal(double.PositiveInfinity, bounded.NextSwitchAfter(2e-6));
    }

    /// <summary>A rectangular wave's left limit is the level ending at an edge.</summary>
    [Fact]
    public void ARectangularWavesLeftLimitIsTheLevelEnding()
    {
        var wave = new RfWaveform.Rectangular(0.25);

        Assert.Equal(-1.0, wave.Before(0.0));
        Assert.Equal(1.0, wave.At(0.0));
        Assert.Equal(1.0, wave.Before(0.25));
        Assert.Equal(-1.0, wave.At(0.25));
        Assert.Equal(-1.0, wave.Before(1.0));
        Assert.Equal(1.0, wave.Before(0.1));
        Assert.Equal(-1.0, wave.Before(0.6));

        var sine = new RfWaveform.Sinusoid();
        Assert.Equal(sine.At(0.3), sine.Before(0.3));
    }

    /// <summary>
    /// A rectangular drive whose edge lies on a stage boundary is asked, from before the switch,
    /// for the level it held through the stage ending - in a staged solve, in an oscillating
    /// uniform field and in an ideal quadrupole.
    /// </summary>
    /// <remarks>
    /// One megahertz at a half duty: the level is high over the first half microsecond and the
    /// edge is at 0.5 us exactly, which is where the stage ends (1e6 times 5e-7 is 0.5 to the bit).
    /// From before the switch the field is the field a quarter microsecond earlier.
    /// </remarks>
    [Fact]
    public void ARectangularEdgeOnASwitchIsAskedTheLevelEnding()
    {
        const double edge = 5e-7;
        var solved = (ITimeVaryingField)GeometryBuilder.Build(Pusher(
            [new CompiledStage("drive", edge, [Plate(0.0, [new CompiledTap(0, 100.0, 0.0)])]), new CompiledStage("quiet", edge, [Plate(0.0, [])])],
            [Plate(0.0, [new CompiledTap(0, 100.0, 0.0)])],
            [new CompiledDrive(1.0e6, DriveWaveform.Rectangular, 0.5)])).Field;

        var inside = solved.ElectricFieldAt(Probe, 0.25 * edge).X;

        output.WriteLine($"staged solve: through the stage {inside:G6}, from before the edge {solved.ElectricFieldBefore(Probe, edge).X:G6} V/m");

        Assert.NotEqual(0.0, inside);
        Assert.Equal(inside, solved.ElectricFieldBefore(Probe, edge).X);
        Assert.Equal(solved.PotentialAt(Probe, 0.25 * edge), solved.PotentialBefore(Probe, edge));

        var uniform = OscillatingUniformField.Create(new Vec3(1e3, 0.0, 0.0), Core.Units.Quantity.From(1.0, "MHz"), new RfWaveform.Rectangular(0.5));

        Assert.Equal(1e3, uniform.ElectricFieldBefore(Probe, edge).X);
        Assert.Equal(-1e3, uniform.ElectricFieldAt(Probe, edge).X);
        Assert.Equal(uniform.PotentialAt(Probe, 0.25 * edge), uniform.PotentialBefore(Probe, edge));

        var quadrupole = IdealQuadrupoleRf.Create(
            Core.Units.Quantity.From(0.0, "V"),
            Core.Units.Quantity.From(100.0, "V"),
            Core.Units.Quantity.From(1.0, "MHz"),
            Core.Units.Quantity.From(5.0, "mm"),
            new RfWaveform.Rectangular(0.5));
        var off = new Vec3(1e-3, 0.0, 0.0);

        // The quadrupole carries its frequency as an angular one, and 2 pi f t / 2 pi lands on the
        // edge exactly at 1.5 us rather than at 0.5 - the phase is 1.5 to the bit there.
        const double quadrupoleEdge = 1.5e-6;

        Assert.NotEqual(quadrupole.ElectricFieldAt(off, quadrupoleEdge).X, quadrupole.ElectricFieldAt(off, 1.25e-6).X);
        Assert.Equal(quadrupole.ElectricFieldAt(off, 1.25e-6).X, quadrupole.ElectricFieldBefore(off, quadrupoleEdge).X);
        Assert.Equal(quadrupole.PotentialAt(off, 1.25e-6), quadrupole.PotentialBefore(off, quadrupoleEdge));
    }

    private static CompiledElectrode Plate(double volts, IReadOnlyList<CompiledTap> taps) => new()
    {
        Name = "pusher",
        Shape = ElectrodeShape.Rectangle,
        MinX = -0.0105,
        MaxX = -0.010,
        MinY = -0.01,
        MaxY = 0.01,
        Potential = volts,
        Taps = taps,
    };

    private static CompiledSolvedField Pusher(
        IReadOnlyList<CompiledStage> stages, IReadOnlyList<CompiledElectrode> electrodes, IReadOnlyList<CompiledDrive> drives) => new()
    {
        MinX = -0.011,
        MinY = -0.011,
        MaxX = 0.011,
        MaxY = 0.011,
        CellSize = 0.011 / 16.0,
        Tolerance = 1e-10,
        Electrodes = electrodes,
        Stages = stages,
        Drives = drives,
    };
}
