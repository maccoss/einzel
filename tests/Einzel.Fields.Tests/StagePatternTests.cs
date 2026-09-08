using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A stage that reshapes its electrode potentials, rather than scaling them all by one factor,
/// gets the field it declares.
/// </summary>
/// <remarks>
/// <para>
/// A sequenced geometry is solved once per distinct spatial pattern of potentials, and each
/// stage then re-weights the solved patterns - which is what makes a two-hundred-ring funnel
/// cost two solves. The patterns were gathered by handing every stage's electrodes to the
/// decomposition <em>together</em>. A supply's coefficients are keyed by electrode name, so
/// the same electrode appearing in two states collided and the state processed last won, and
/// what came out was one pattern belonging to no stage at all.
/// </para>
/// <para>
/// <b>The failure was silent and total.</b> Each stage then looked up its own pattern, did not
/// find it, and the code walked past that on the strength of a comment saying it could not
/// happen - so the stage ran with no field whatsoever. A trapped-ion-mobility tunnel whose
/// storage region measurably holds its population for milliseconds let the whole of it out at
/// gas speed, with exit code zero and no warning anywhere.
/// </para>
/// <para>
/// It survived because a stage whose pattern is a <em>uniform scaling</em> of another still
/// matches: every sequence shipped before this - an elution ramp scaling one gradient, a trap
/// switching one extraction voltage - was right. Only reshaping breaks, and holding one region
/// of a two-region instrument while moving the other is exactly reshaping. So the case that
/// matters here is the one where two electrodes move by different factors, and the control is
/// the same geometry scaled uniformly, which was always correct and must stay so.
/// </para>
/// </remarks>
public sealed class StagePatternTests(ITestOutputHelper output)
{
    private static CompiledElectrode Plate(string name, double minX, double maxX, double potential) =>
        new()
        {
            Name = name, Shape = ElectrodeShape.Rectangle,
            MinX = minX, MaxX = maxX, MinY = 2.0e-3, MaxY = 6.0e-3,
            Potential = potential, Taps = [],
        };

    /// <summary>Two plates over a bore, at whatever the stage holds them at.</summary>
    private static CompiledElectrode[] State(double left, double right) =>
        [Plate("left", -6.0e-3, -2.0e-3, left), Plate("right", 2.0e-3, 6.0e-3, right)];

    private static CompiledSolvedField Sequenced(params (string Name, double Left, double Right)[] stages) =>
        new()
        {
            MinX = -12.0e-3, MinY = 0.0, MaxX = 12.0e-3, MaxY = 8.0e-3,
            CellSize = 0.25e-3, Tolerance = 1e-10,
            Electrodes = State(stages[0].Left, stages[0].Right),
            Drives = [],
            Stages = [.. stages.Select(s =>
                new CompiledStage(s.Name, 10.0e-6, State(s.Left, s.Right)))],
        };

    /// <summary>The potential just under a plate, where that plate dominates.</summary>
    private static double Under(ITimeVaryingField field, double xMetres, double atSeconds) =>
        field.PotentialAt(new Vec3(xMetres, 1.5e-3, 0.0), atSeconds);

    /// <summary>
    /// Two stages that move two electrodes by different factors: each stage gets its own
    /// potentials, and the two patterns cost two solves.
    /// </summary>
    [Fact]
    public void AStageThatReshapesItsPotentialsGetsThemBoth()
    {
        // Stage one holds the right plate at earth; stage two lifts it while the left plate
        // stays where it was. No single scaling takes one to the other.
        var solve = Sequenced(("first", 100.0, 0.0), ("second", 100.0, 50.0));
        var (built, _) = GeometryBuilder.Build(solve);
        var field = Assert.IsType<DrivenSolvedField>(built);

        var channels = GeometryBuilder.SolveChannels(solve).Count;

        // Sampled inside each stage rather than on a boundary: a staged field switches at the
        // boundary itself, so the last instant of a stage is inside it.
        const double InFirst = 5.0e-6;
        const double InSecond = 15.0e-6;

        var leftFirst = Under(field, -4.0e-3, InFirst);
        var rightFirst = Under(field, 4.0e-3, InFirst);
        var leftSecond = Under(field, -4.0e-3, InSecond);
        var rightSecond = Under(field, 4.0e-3, InSecond);

        output.WriteLine($"{channels} channels");
        output.WriteLine($"stage one:  under left {leftFirst:F2} V, under right {rightFirst:F2} V");
        output.WriteLine($"stage two:  under left {leftSecond:F2} V, under right {rightSecond:F2} V");

        // Two patterns, because no scaling relates them.
        Assert.Equal(2, channels);

        // The left plate is at 100 V throughout, so the potential under it does not move.
        Assert.Equal(leftFirst, leftSecond, 0.5);
        Assert.True(leftFirst > 40.0, $"the left plate's 100 V reads {leftFirst:F2} V under it - the stage has no field");

        // And the right plate goes from earth to 50 V, which is the whole point.
        Assert.True(rightFirst < 5.0, $"the earthed right plate reads {rightFirst:F2} V under it");
        Assert.True(rightSecond > 20.0, $"the right plate's 50 V reads {rightSecond:F2} V under it in the second stage");
    }

    /// <summary>
    /// The control: two stages related by one scaling share a single solved pattern, which is
    /// the economy the decomposition exists for and was always correct.
    /// </summary>
    [Fact]
    public void AStageThatScalesItsPotentialsSharesOnePattern()
    {
        var solve = Sequenced(("full", 100.0, 40.0), ("half", 50.0, 20.0));
        var (built, _) = GeometryBuilder.Build(solve);
        var field = Assert.IsType<DrivenSolvedField>(built);

        var channels = GeometryBuilder.SolveChannels(solve).Count;

        var first = Under(field, -4.0e-3, 5.0e-6);
        var second = Under(field, -4.0e-3, 15.0e-6);

        output.WriteLine($"{channels} channel(s); under the left plate {first:F2} V then {second:F2} V, ratio {second / first:F4}");

        Assert.Equal(1, channels);   // a count that came from .Count, not a collection
        Assert.Equal(0.5, second / first, 1e-9);
    }

    /// <summary>
    /// And a stage that holds everything where it was costs one pattern too, however many
    /// stages there are.
    /// </summary>
    [Fact]
    public void IdenticalStagesShareOnePattern()
    {
        var solve = Sequenced(("one", 100.0, 40.0), ("two", 100.0, 40.0), ("three", 100.0, 40.0));

        Assert.Single(GeometryBuilder.SolveChannels(solve));

        var (built, _) = GeometryBuilder.Build(solve);
        var field = Assert.IsType<DrivenSolvedField>(built);

        // Bit-identical across the three, since it is one pattern at one weight.
        Assert.Equal(Under(field, -4.0e-3, 5.0e-6), Under(field, -4.0e-3, 25.0e-6));
    }
}
