using Einzel.Core.Errors;
using Einzel.Core.Model;

using Xunit.Abstractions;

namespace Einzel.Core.Tests;

/// <summary>
/// Two conductors in one place at two potentials, refused in a volume.
/// </summary>
/// <remarks>
/// <para>
/// The plane check has existed since a multipole guide was built with a rod ratio
/// carried over from a different pole count, solved it, converged in eight cycles and
/// produced an acceptance measurement that was really a measurement of rods closing in
/// on the axis. Every volume geometry - box, sphere, cylinder, prism, revolve - was
/// unchecked, and the shipped Astral had exactly that defect in it.
/// </para>
/// <para>
/// <b>What each test here has to discriminate is stated on it.</b> A check of this kind
/// fails in two directions and they need different evidence: refusing a legitimate
/// geometry is caught by the templates, and missing a real overlap is caught by the
/// deliberate ones. A test that only ever sees clean geometries measures nothing.
/// </para>
/// </remarks>
public sealed class ElectrodeOverlap3DTests(ITestOutputHelper output)
{
    private static CompiledElectrode3D Box(
        string name,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ,
        double potential) =>
        new()
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            MinX = minX,
            MinY = minY,
            MinZ = minZ,
            MaxX = maxX,
            MaxY = maxY,
            MaxZ = maxZ,
            Potential = potential,
        };

    private static CompiledElectrode3D Sphere(
        string name, double x, double y, double z, double radius, double potential) =>
        new()
        {
            Name = name,
            Shape = Electrode3DShape.Sphere,
            CentreX = x,
            CentreY = y,
            CentreZ = z,
            Radius = radius,
            Potential = potential,
        };

    private static List<EinzelError> Check(params CompiledElectrode3D[] electrodes)
    {
        var errors = new List<EinzelError>();

        ElectrodeOverlap3D.Check(electrodes, [], "/fields/0/solve3d", errors);

        return errors;
    }

    private static List<EinzelError> Sequenced(
        CompiledElectrode3D[] electrodes, params CompiledStage3D[] stages)
    {
        var errors = new List<EinzelError>();

        ElectrodeOverlap3D.Check(electrodes, stages, "/fields/0/solve3d", errors);

        return errors;
    }

    [Fact]
    public void TwoBoxesSharingMetalAtDifferentPotentialsAreRefused()
    {
        var errors = Check(
            Box("left", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 100.0),
            Box("right", 0.005, 0.0, 0.0, 0.015, 0.010, 0.010, -100.0));

        var error = Assert.Single(errors);

        Assert.Equal(ErrorCodes.SchemaInvalid, error.Code);
        Assert.Contains("'left' and 'right'", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("100 V", error.Constraint, StringComparison.Ordinal);

        output.WriteLine(error.Constraint);
    }

    /// <summary>
    /// A shared face is a legitimate design and must pass.
    /// </summary>
    /// <remarks>
    /// This is not a corner case - it is how every segmented electrode chain in this
    /// library is written. The Astral's drift stripes, the segmented quadrupole's
    /// sections and the linear ion trap's half-rods all meet face to face and hold
    /// different potentials on purpose. A check that refused tangency would refuse
    /// every one of them.
    /// </remarks>
    [Fact]
    public void TwoBoxesSharingOnlyAFaceAreAllowed() =>
        Assert.Empty(Check(
            Box("left", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 100.0),
            Box("right", 0.010, 0.0, 0.0, 0.020, 0.010, 0.010, -100.0)));

    /// <summary>
    /// A face reached by two different expressions is still one face.
    /// </summary>
    /// <remarks>
    /// <b>The flat-face lesson, met again.</b> A template writes its faces as
    /// expressions over the parameter surface, so two that are meant to coincide agree
    /// to a few ulps rather than exactly - the C-trap reaches one contact as
    /// <c>bendRadius + rodHalfWidth</c> and the other as
    /// <c>bendRadius + inscribedRadius * sqrt(1 + 0)</c>. An exact test on a computed
    /// quantity is what made a symmetric electrode solve to an asymmetric field; here
    /// it would refuse a geometry whose author did nothing wrong. Nudged by two ulps in
    /// the overlapping direction, which is the direction that would be refused.
    /// </remarks>
    [Fact]
    public void AFaceFlushOnlyToRoundingIsStillFlush()
    {
        var meeting = 0.010;
        var overlapping = Math.BitDecrement(Math.BitDecrement(meeting));

        Assert.NotEqual(meeting, overlapping);
        Assert.True(meeting - overlapping < 1e-17);

        Assert.Empty(Check(
            Box("left", 0.0, 0.0, 0.0, meeting, 0.010, 0.010, 100.0),
            Box("right", overlapping, 0.0, 0.0, 0.020, 0.010, 0.010, -100.0)));
    }

    /// <summary>Overlapping conductors that hold the same thing are a fillet, not a fault.</summary>
    [Fact]
    public void TwoBoxesDeeplyOverlappingAtOnePotentialAreAllowed() =>
        Assert.Empty(Check(
            Box("shoulder", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 100.0),
            Box("fillet", 0.002, 0.002, 0.002, 0.008, 0.008, 0.008, 100.0)));

    /// <summary>
    /// Agreement is over every tap, not over the first.
    /// </summary>
    /// <remarks>
    /// The plane check had this defect once: <c>DriveAmplitude</c> became <em>the first
    /// tap's</em> amplitude when a second generator landed, and two electrodes agreeing
    /// about the main RF and differing about a supplementary excitation were judged
    /// identical. The one check that exists to prevent a field of a geometry nobody
    /// described had become a route to one. It is written from the start here, and this
    /// is what holds it.
    /// </remarks>
    [Fact]
    public void ElectrodesAgreeingOnTheFirstTapAndNotTheSecondAreRefused()
    {
        var a = Box("a", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 0.0) with
        {
            Taps = [new CompiledTap(0, 500.0, 0.0), new CompiledTap(1, 5.0, 0.0)],
        };

        var b = Box("b", 0.004, 0.0, 0.0, 0.014, 0.010, 0.010, 0.0) with
        {
            Taps = [new CompiledTap(0, 500.0, 0.0), new CompiledTap(1, -5.0, 0.0)],
        };

        var error = Assert.Single(Check(a, b));

        Assert.Contains("drive 1", error.Constraint, StringComparison.Ordinal);

        // And the control: identical taps make the same overlap a fillet.
        Assert.Empty(Check(a, b with { Taps = a.Taps }));
    }

    /// <summary>
    /// A bounding-box screen is not enough, and this is the geometry that says so.
    /// </summary>
    /// <remarks>
    /// A sphere at the centre of a hollow shell's bounding box shares that box entirely
    /// and shares no metal at all. The C-trap is the shipped case: its five rods are
    /// nested arcs about one axis, so <c>rodInnerUpper</c>'s box sits wholly inside
    /// <c>rodOuter</c>'s while the metal is nowhere near it. A check that refused on boxes
    /// would refuse the C-trap.
    /// </remarks>
    [Fact]
    public void TwoSpheresWhoseBoxesMeetDiagonallyShareNoMetal()
    {
        // Offset along a diagonal, which is what separates the two questions: two
        // spheres of radius r are boxed together whenever either offset is under 2r,
        // and share metal only when the DISTANCE is - so a diagonal offset of
        // (15, 15, 0) mm overlaps the boxes over a 5 mm corner and leaves the surfaces
        // 1.2 mm apart. On axis the two conditions coincide and prove nothing.
        Assert.Empty(Check(
            Sphere("one", 0.0, 0.0, 0.0, 0.010, 100.0),
            Sphere("two", 0.015, 0.015, 0.0, 0.010, -100.0)));

        // The control, with the same boxes: pulled onto the axis they do share metal.
        Assert.Single(Check(
            Sphere("one", 0.0, 0.0, 0.0, 0.010, 100.0),
            Sphere("two", 0.015, 0.0, 0.0, 0.010, -100.0)));
    }

    /// <summary>
    /// A sliver is what a witness search can miss, and this says how thin it may be.
    /// </summary>
    /// <remarks>
    /// The search proves an overlap by finding a point inside both, and the thinner the
    /// shared metal the harder that point is to find. It is not a limit of the bound -
    /// the pruning is sound - but of the probe budget. Recorded as a measurement rather
    /// than asserted as a guarantee: a micron of shared metal on a centimetre box is
    /// found, and this is the test to extend if a real geometry is ever missed.
    /// </remarks>
    [Theory]
    [InlineData(1e-3)]
    [InlineData(1e-4)]
    [InlineData(1e-5)]
    [InlineData(1e-6)]
    public void ASliverOfSharedMetalIsStillFound(double sliver)
    {
        var errors = Check(
            Box("left", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 100.0),
            Box("right", 0.010 - sliver, 0.0, 0.0, 0.020, 0.010, 0.010, -100.0));

        output.WriteLine($"{sliver * 1e6:G4} um of shared metal: {errors.Count} refusal");

        Assert.Single(errors);
    }

    /// <summary>One report per geometry, as in the plane.</summary>
    /// <remarks>
    /// An offset that is wrong makes every adjacent pair wrong, and a list of identical
    /// complaints is harder to read than one.
    /// </remarks>
    [Fact]
    public void ManyOverlappingPairsAreReportedOnce()
    {
        var electrodes = Enumerable
            .Range(0, 8)
            .Select(i => Box(
                $"ring-{i}", 0.0, 0.0, i * 0.004, 0.010, 0.010, (i * 0.004) + 0.006, i * 10.0))
            .ToArray();

        Assert.Single(Check(electrodes));
    }

    /// <summary>An electrode is never compared with itself.</summary>
    [Fact]
    public void OneElectrodeIsNotAnOverlap() =>
        Assert.Empty(Check(Box("only", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 100.0)));

    /// <summary>
    /// Two conductors that agree while the instrument holds and differ while it pushes
    /// are still two conductors in one place at two potentials.
    /// </summary>
    /// <remarks>
    /// <b>The proxy this project keeps meeting.</b> "Do these two agree" is a question
    /// with as many answers as the instrument has states, and asking it of the declared
    /// state alone is a proxy that stops being equivalent the moment a document declares
    /// a sequence. A stage may not move metal - <c>SameGeometry3D</c> enforces that - so
    /// the overlap is settled once and only the excitations vary; what would otherwise
    /// happen is a field of a geometry nobody described for exactly the duration of the
    /// push, on a document that validated cleanly.
    /// </remarks>
    [Fact]
    public void ConductorsThatAgreeAtRestAndDifferDuringAStageAreRefused()
    {
        CompiledElectrode3D[] Held(double pushVolts) =>
        [
            Box("plate", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 0.0),
            Box("shoulder", 0.004, 0.0, 0.0, 0.014, 0.010, 0.010, pushVolts),
        ];

        var held = Held(0.0);

        // At rest they agree, so the overlap is a fillet and nothing is reported.
        Assert.Empty(Sequenced(held, new CompiledStage3D("hold", 1e-6, Held(0.0))));

        // Push, and the same metal is at 0 V and 400 V at once.
        var error = Assert.Single(Sequenced(
            held,
            new CompiledStage3D("hold", 1e-6, Held(0.0)),
            new CompiledStage3D("push", 2e-6, Held(400.0))));

        Assert.Contains("during 'push'", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("400 V", error.Constraint, StringComparison.Ordinal);

        output.WriteLine(error.Constraint);
    }

    /// <summary>A ramp's far end is a state too.</summary>
    /// <remarks>
    /// A phase that ramps carries the electrodes as they stand at both ends, and the
    /// disagreement may live only at the end it is walking toward - which is the state
    /// a check reading the phase's opening excitations would never see.
    /// </remarks>
    [Fact]
    public void ADisagreementOnlyAtTheEndOfARampIsRefused()
    {
        CompiledElectrode3D[] At(double volts) =>
        [
            Box("plate", 0.0, 0.0, 0.0, 0.010, 0.010, 0.010, 0.0),
            Box("shoulder", 0.004, 0.0, 0.0, 0.014, 0.010, 0.010, volts),
        ];

        var stage = new CompiledStage3D("elute", 8e-3, At(0.0)) { EndElectrodes = At(60.0) };

        var error = Assert.Single(Sequenced(At(0.0), stage));

        Assert.Contains("during 'elute'", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("60 V", error.Constraint, StringComparison.Ordinal);
    }
}
