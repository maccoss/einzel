using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;

namespace Einzel.Library.Tests;

/// <summary>
/// A driven field with no channel at all answers every question a field is asked, and the
/// builders never make one.
/// </summary>
/// <remarks>
/// Where the metal is, how fine the mesh is and where the domain ends were all asked of the
/// first channel, unconditionally - an index out of range, through the public conductor
/// interfaces, for a field with none. Here rather than beside the other field tests because the
/// constructor that can make such a field is internal, and this is the test assembly it is
/// visible to.
/// </remarks>
public sealed class DrivenWithoutChannelsTests
{
    /// <summary>The field of nothing: no metal, no mesh limit, no edge, zero everywhere.</summary>
    [Fact]
    public void ADrivenFieldWithNoChannelAnswersWithoutAnIndexOutOfRange()
    {
        var nothing = new DrivenSolvedField([], [], [], [1.0e6], [new RfWaveform.Sinusoid()])
        {
            ConductorCoefficients = new Dictionary<string, double[]> { ["post"] = [] },
        };

        var point = new Vec3(1.0e-3, 2.0e-3, 0.0);

        Assert.Equal(0, nothing.ChannelCount);
        Assert.Equal(double.PositiveInfinity, nothing.SignedDistanceToConductor(point));
        Assert.Null(nothing.ConductorAt(point));
        Assert.Equal(double.PositiveInfinity, nothing.SignedDistanceToDiscontinuity(point));
        Assert.Equal(double.PositiveInfinity, nothing.ResolutionLength);
        Assert.Equal(0.0, nothing.PotentialAt(point, 0.25e-6));
        Assert.Equal(0.0, nothing.CycleMeanPotentialAt(point, 0.0, 1.0e-6, 8));
        Assert.Equal(0.0, nothing.HeldPotentialOf("post", 0.0));
        Assert.Equal(0.0, nothing.HeldCycleMeanOf("post", 0.0, 1.0e-6, 8));
        Assert.Null(nothing.HeldPotentialOf("absent", 0.0));
    }

    /// <summary>
    /// The control, and why that field is not one a model can reach: a driven solve with nothing
    /// on any electrode - or no electrode - is still built with one channel.
    /// </summary>
    /// <param name="withPost">Whether the box has an electrode in it.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBuildersAlwaysMakeAChannel(bool withPost)
    {
        var solve = new CompiledSolvedField
        {
            MinX = -6.0e-3, MaxX = 6.0e-3, MinY = -6.0e-3, MaxY = 6.0e-3,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            ReflectAboutX = 6.0e-3,
            Electrodes = withPost
                ?
                [
                    new CompiledElectrode
                    {
                        Name = "post", Shape = ElectrodeShape.Rectangle,
                        MinX = -1.0e-3, MaxX = 1.0e-3, MinY = -5.0e-3, MaxY = -3.0e-3,
                        Potential = 0.0, Taps = [],
                    },
                ]
                : [],
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
        };

        var (field, _, leaves) = GeometryBuilder.BuildLeaved(solve);
        var driven = Assert.IsType<DrivenSolvedField>(field);

        Assert.Equal(1, driven.ChannelCount);
        Assert.True(double.IsFinite(driven.ResolutionLength));

        // Its leaves are the two halves of the box, held by the solve.
        Assert.Equal([false, true], leaves.Select(l => l.Image is not null));
        Assert.All(leaves, l => Assert.Same(driven, l.Held));

        var inPost = new Vec3(0.0, -4.0e-3, 0.0);
        var inImage = new Vec3(12.0e-3, -4.0e-3, 0.0);

        Assert.Equal(withPost ? "post" : null, leaves[0].Shape.ConductorAt(inPost));
        Assert.Equal(withPost ? "post" : null, leaves[1].Shape.ConductorAt(inImage));
        Assert.Equal(withPost ? 0.0 : null, driven.HeldPotentialOf("post", 0.0));
    }
    /// <summary>
    /// A conductor's held cycle mean is taken the way the potential's is: sampled wherever any
    /// channel has no closed form for the window, not only a channel the conductor taps.
    /// </summary>
    /// <remarks>
    /// The held mean asked only the conductor's own channels, so here - a rectangular drive the
    /// window fits on the conductor's channel, a 0.7 MHz drive it does not on another - the
    /// plateau took the closed form, -4 V, while the field around it was sampled, 0 V at four
    /// samples: a step at the conductor's edge that was the averaging, not the physics.
    /// </remarks>
    [Fact]
    public void AHeldMeanIsTakenAsThePotentialsIs()
    {
        var field = new DrivenSolvedField(
            [FieldFreeSpace.Instance, FieldFreeSpace.Instance],
            [0.0, 0.0],
            [[new WeightTerm(0, 10.0, 0.0)], [new WeightTerm(1, 10.0, 0.0)]],
            [1.0e6, 0.7e6],
            [new RfWaveform.Rectangular(0.3), new RfWaveform.Sinusoid()])
        {
            ConductorCoefficients = new Dictionary<string, double[]> { ["a"] = [1.0, 0.0] },
        };

        var held = field.HeldCycleMeanOf("a", 0.0, 1.0e-6, 4);
        var sampled = HeldConductors.SampledMean(field, "a", 0.0, 1.0e-6, 4);

        Assert.Equal(sampled, held);
        Assert.NotEqual(-4.0, held!.Value, 9);
    }
}
