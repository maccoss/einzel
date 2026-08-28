using Einzel.Transport.Diffusion;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The owner map that tells the diffusive solver which nodes are metal.
/// </summary>
/// <remarks>
/// Small, and worth its own tests because both of the properties below were wrong in
/// the same direction: they answered a question adjacent to the one they were asked.
/// </remarks>
public sealed class AbsorbingCellsTests(ITestOutputHelper output)
{
    [Fact]
    public void AnIndexThatNamesNoSurfaceIsRefusedAtBothEnds()
    {
        // The upper bound was checked and the lower was not, so -2 read as an open
        // node - an owner map built wrong in that direction would have produced a
        // field with fewer absorbers than the geometry declared, and nothing would
        // have said so. -1 is the one negative value that means anything.
        var tooHigh = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbsorbingCells([0, 1, 2], ["a", "b"]));

        var tooLow = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbsorbingCells([0, -2, 1], ["a", "b"]));

        output.WriteLine(tooHigh.Message.Split('\n')[0]);
        output.WriteLine(tooLow.Message.Split('\n')[0]);

        // And -1 is fine, everywhere.
        var open = new AbsorbingCells([-1, -1, -1], ["a"]);

        Assert.False(open.Blocks(1));
        Assert.Null(open.NameAt(1));
    }

    [Fact]
    public void TheRefusalStaysMeaningfulWhenThereAreNoAbsorbersAtAll()
    {
        // AGT-3: an error is a recovery instruction, and "the only values that mean
        // anything are -1 and 0 to -1" instructs nobody. The upper bound is one less
        // than the name count, which is -1 when there are no names.
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbsorbingCells([0], []));

        output.WriteLine(error.Message.Split('\n')[0]);

        Assert.DoesNotContain("0 to -1", error.Message, StringComparison.Ordinal);
        Assert.Contains("no absorbers were given", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnythingAbsorbsMeansANodeIsOwnedNotThatAMapWasSupplied()
    {
        // An electrode declared outside the density grid gives a named absorber and
        // an owner map with nothing in it. Reporting that as "something absorbs"
        // would be describing the document rather than the solve.
        var nothingOwned = new AbsorbingCells([-1, -1, -1, -1], ["farAway"]);
        var somethingOwned = new AbsorbingCells([-1, 0, -1, -1], ["wall"]);

        output.WriteLine($"named but unoccupied: Any = {nothingOwned.Any}");
        output.WriteLine($"one node owned:       Any = {somethingOwned.Any}");

        Assert.False(nothingOwned.Any);
        Assert.True(somethingOwned.Any);

        Assert.False(AbsorbingCells.None.Any);
    }

    [Fact]
    public void ANodeReportsWhichSurfaceOwnsIt()
    {
        // ACC-5's currency: a loss itemised by the name the model author wrote, so
        // the answer is a thing to move rather than a percentage.
        var cells = new AbsorbingCells([-1, 0, 1, -1], ["frontPlate", "backPlate"]);

        Assert.Null(cells.NameAt(0));
        Assert.Equal("frontPlate", cells.NameAt(1));
        Assert.Equal("backPlate", cells.NameAt(2));

        Assert.True(cells.Blocks(1));
        Assert.False(cells.Blocks(3));
    }
}
