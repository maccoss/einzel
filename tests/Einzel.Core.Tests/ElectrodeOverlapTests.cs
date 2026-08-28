using Einzel.Core.Errors;
using Einzel.Core.Model;
using Xunit.Abstractions;

namespace Einzel.Core.Tests;

/// <summary>
/// Two conductors in one place at two potentials.
/// </summary>
/// <remarks>
/// A Dirichlet mask is built by writing each electrode's nodes in turn, so where
/// two overlap the last one written wins. Where they hold the same thing that is
/// harmless and often deliberate - a shape assembled from overlapping primitives is
/// how a fillet or a shoulder gets built. Where they disagree the region is
/// simultaneously at two potentials, the solve silently picks one, and the field it
/// returns is the field of a geometry nobody described.
/// </remarks>
public sealed class ElectrodeOverlapTests(ITestOutputHelper output)
{
    private static CompiledElectrode Disc(
        string name, double x, double y, double radius, double potential = 0.0, double drive = 0.0) =>
        new()
        {
            Name = name,
            Shape = ElectrodeShape.Disc,
            CentreX = x,
            CentreY = y,
            Radius = radius,
            Potential = potential,
            DriveAmplitude = drive,
        };

    private static CompiledElectrode Box(
        string name, double minX, double minY, double maxX, double maxY, double potential = 0.0) =>
        new()
        {
            Name = name,
            Shape = ElectrodeShape.Rectangle,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            Potential = potential,
        };

    private static List<EinzelError> Check(params CompiledElectrode[] electrodes)
    {
        var errors = new List<EinzelError>();

        ElectrodeOverlap.Check(electrodes, "/fields/0/solve", errors);

        return errors;
    }

    [Fact]
    public void TwoDiscsAtDifferentPotentialsAreRefused()
    {
        var errors = Check(
            Disc("rodA", 0.0, 0.0, 0.010, potential: 100.0),
            Disc("rodB", 0.015, 0.0, 0.010, potential: -100.0));

        var error = Assert.Single(errors);

        output.WriteLine(error.Constraint);

        Assert.Contains("rodA", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("rodB", error.Constraint, StringComparison.Ordinal);
        Assert.Equal("/fields/0/solve/electrodes", error.Path);
    }

    [Fact]
    public void TwoDiscsAtTheSamePotentialAreAllowed()
    {
        // A shape assembled from overlapping primitives is legitimate, and refusing
        // it would be refusing a fillet. What is ill-posed is the disagreement, not
        // the overlap.
        Assert.Empty(Check(
            Disc("shoulderA", 0.0, 0.0, 0.010, potential: 100.0),
            Disc("shoulderB", 0.015, 0.0, 0.010, potential: 100.0)));
    }

    [Fact]
    public void TangentDiscsAreAllowed()
    {
        // Exactly touching is a design, not a mistake, and refusing on a
        // floating-point equality would make a legitimate geometry depend on which
        // way the last bit rounded.
        Assert.Empty(Check(
            Disc("left", 0.0, 0.0, 0.010, potential: 100.0),
            Disc("right", 0.020, 0.0, 0.010, potential: -100.0)));
    }

    [Fact]
    public void DiscsThatDisagreeOnlyInTheirDriveAreRefused()
    {
        // The case that produced this check: adjacent rods of a multipole hold the
        // same DC and opposite drive, so a potential-only comparison would have
        // called them compatible and let them intersect.
        var errors = Check(
            Disc("rod-0", 0.0, 0.0, 0.010, drive: 300.0),
            Disc("rod-1", 0.015, 0.0, 0.010, drive: -300.0));

        var error = Assert.Single(errors);

        output.WriteLine(error.Constraint);

        Assert.Contains("drive", error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void RectanglesAndDiscsAreCheckedToo()
    {
        // A rod through a plate is as ill-posed as a rod through a rod.
        Assert.Single(Check(
            Box("plate", -0.050, -0.001, 0.050, 0.001, potential: 0.0),
            Disc("rod", 0.0, 0.0, 0.005, potential: 500.0)));

        // And the same pair moved apart is not.
        Assert.Empty(Check(
            Box("plate", -0.050, -0.001, 0.050, 0.001, potential: 0.0),
            Disc("rod", 0.0, 0.020, 0.005, potential: 500.0)));

        // Two plates crossing.
        Assert.Single(Check(
            Box("horizontal", -0.050, -0.001, 0.050, 0.001, potential: 0.0),
            Box("vertical", -0.001, -0.050, 0.001, 0.050, potential: 500.0)));
    }

    [Fact]
    public void OneComplaintPerGeometryRatherThanOnePerPair()
    {
        // A rod ratio that is wrong makes every adjacent pair wrong, and a list of
        // nine identical complaints is harder to act on than one.
        var errors = Check(
            Disc("a", 0.0, 0.0, 0.010, potential: 1.0),
            Disc("b", 0.005, 0.0, 0.010, potential: 2.0),
            Disc("c", 0.010, 0.0, 0.010, potential: 3.0),
            Disc("d", 0.015, 0.0, 0.010, potential: 4.0));

        Assert.Single(errors);
    }

    [Fact]
    public void AnEdgeProfileIsSkippedRatherThanGuessedAt()
    {
        // Stated gap. An edge profile lives on the domain boundary, and a boundary
        // profile touching an interior electrode is a different question from two
        // interior conductors intersecting. A check that guessed would sometimes
        // refuse a legitimate geometry, which is worse than one that sometimes
        // misses.
        var profile = new CompiledElectrode
        {
            Name = "wall",
            Shape = ElectrodeShape.EdgeProfile,
            Edge = GridEdge.Left,
            Potential = 500.0,
        };

        Assert.Empty(Check(profile, Disc("rod", 0.0, 0.0, 0.010, potential: -500.0)));
    }
}
