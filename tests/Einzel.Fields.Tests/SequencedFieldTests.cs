using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// One element, switched between compiled states by the instrument's timeline.
/// </summary>
/// <remarks>
/// The generic form of what a sequence does to an element that has no channels to
/// re-weight. A solved geometry carries its phases inside <c>DrivenSolvedField</c>; an
/// analytic one needs a switch, and this is it.
/// </remarks>
public sealed class SequencedFieldTests(ITestOutputHelper output)
{
    private static UniformField Uniform(double voltsPerMetre) =>
        UniformField.Create(new Vec3(voltsPerMetre, 0.0, 0.0));

    private static SequencedField Two() => new(
        [Uniform(100.0), Uniform(900.0)],
        [1.0e-4, 1.1e-4]);

    /// <summary>The field is the phase's, and the switch is where the boundary is.</summary>
    [Fact]
    public void EachPhaseHoldsItsOwnField()
    {
        var field = Two();

        foreach (var t in new[] { 0.0, 5e-5, 9.9e-5 })
        {
            Assert.Equal(100.0, field.ElectricFieldAt(Vec3.Zero, t).X, 1e-12);
        }

        foreach (var t in new[] { 1.0e-4, 1.05e-4 })
        {
            Assert.Equal(900.0, field.ElectricFieldAt(Vec3.Zero, t).X, 1e-12);
        }

        output.WriteLine("switched exactly at the boundary, not a step either side of it");
    }

    /// <summary>
    /// A time exactly on a boundary belongs to the phase that is starting.
    /// </summary>
    /// <remarks>
    /// Not a tie-break. The integrator lands exactly on switch instants by design — it
    /// asks <see cref="SequencedField.NextSwitchAfter"/> and refuses to step past it — so
    /// the sample at the boundary is one that really happens, every time. The switch has
    /// happened by then.
    /// </remarks>
    [Fact]
    public void AnInstantOnABoundaryBelongsToThePhaseThatIsStarting()
    {
        var field = Two();

        Assert.Equal(900.0, field.ElectricFieldAt(Vec3.Zero, 1.0e-4).X, 1e-12);
        Assert.Equal(100.0, field.ElectricFieldAt(Vec3.Zero, Math.BitDecrement(1.0e-4)).X, 1e-12);
    }

    /// <summary>The last phase holds after the sequence ends.</summary>
    /// <remarks>
    /// A physics statement rather than a bookkeeping one, and the same rule the solved
    /// path enforces: an instrument left alone stays where it was put. A field switching
    /// off at the end would make every ion still in flight suddenly coast, which is a
    /// change of physics disguised as the end of a list.
    /// </remarks>
    [Fact]
    public void TheLastPhaseHoldsAfterTheSequenceEnds()
    {
        var field = Two();

        Assert.Equal(900.0, field.ElectricFieldAt(Vec3.Zero, 1.0).X, 1e-12);
        Assert.Equal(double.PositiveInfinity, field.NextSwitchAfter(1.0));
    }

    /// <summary>The switches are declared, so the integrator can land on them exactly.</summary>
    /// <remarks>
    /// Unlike a boundary in space, the time is known in advance and needs no root-find.
    /// </remarks>
    [Fact]
    public void TheSwitchesAreDeclaredInAdvance()
    {
        var field = Two();

        Assert.Equal(1.0e-4, field.NextSwitchAfter(0.0));
        Assert.Equal(1.0e-4, field.NextSwitchAfter(9.9e-5));
        Assert.Equal(1.1e-4, field.NextSwitchAfter(1.0e-4));
        Assert.Equal(double.PositiveInfinity, field.NextSwitchAfter(1.1e-4));
    }

    /// <summary>
    /// It gives up the analytic drift rather than promising a run it cannot keep.
    /// </summary>
    /// <remarks>
    /// A field-free run length is a promise about a whole straight segment. A switch
    /// part-way along one would break it silently, and the analytic drift is an
    /// optimisation — so giving it up costs speed and never accuracy.
    /// </remarks>
    [Fact]
    public void ItPromisesNoFieldFreeRun()
    {
        var field = new SequencedField(
            [FieldFreeSpace.Instance, FieldFreeSpace.Instance], [1.0e-4, 2.0e-4]);

        Assert.Equal(0.0, field.FieldFreeRunLength(Vec3.Zero, Vec3.UnitX));
    }

    /// <summary>A time-free caller gets the first phase, stated rather than accidental.</summary>
    /// <remarks>
    /// This is the interface a driven field answers at an arbitrary instant without
    /// failing — the defect this project has found four times, in `einzel solve`, in the
    /// diffusive mode, in `SuperposedField` and in the renderer. Here the answer is the
    /// instrument as it starts, which is at least a defensible instant to have picked.
    /// </remarks>
    [Fact]
    public void ATimeFreeCallerGetsTheInstrumentAsItStarts()
    {
        var field = Two();

        Assert.Equal(100.0, ((IElectrostaticField)field).ElectricFieldAt(Vec3.Zero).X, 1e-12);
    }

    /// <summary>Boundaries must increase, and a mismatch is refused rather than guessed at.</summary>
    [Fact]
    public void AMalformedScheduleIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SequencedField([], []));

        Assert.Throws<ArgumentException>(
            () => new SequencedField([Uniform(1.0), Uniform(2.0)], [1.0e-4]));

        var backwards = Assert.Throws<ArgumentException>(
            () => new SequencedField([Uniform(1.0), Uniform(2.0)], [2.0e-4, 1.0e-4]));

        output.WriteLine(backwards.Message);
    }
}
