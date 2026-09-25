using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Numerics;
using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// A volume grid too large to solve is refused as a refusal about the model, and before the
/// memory it would cost is spent.
/// </summary>
/// <remarks>
/// <para>
/// The guard used to be an <see cref="ArgumentOutOfRangeException"/> in the field constructor,
/// so the CLI reported it as <c>INTERNAL_ERROR</c> - "a defect in einzel, not in your model" -
/// on the internal-error exit code. And it fired late: a solve builds its conductor mask before
/// any field, so on the Astral at a 1 mm cell the refusal came after minutes of assembling a
/// mask of the very node count it existed to refuse.
/// </para>
/// <para>
/// The model validator now refuses such a mesh from the document, which is the refusal a user
/// meets. These are the backstop underneath it, for a grid that reaches the solver some other
/// way: it throws the same <see cref="ErrorCodes.GridTooLarge"/>, from the <em>first</em>
/// per-node allocation, and allocates nothing first.
/// </para>
/// </remarks>
public sealed class GridTooLargeTests
{
    /// <summary>The smallest cube over the limit: 401 cubed is 64.48 M nodes.</summary>
    private static Grid3D JustOver() => new(0.0, 0.0, 0.0, 1e-3, 1e-3, 1e-3, 401, 401, 401);

    /// <summary>Runs a constructor and returns its refusal and what it allocated first.</summary>
    private static (EinzelError Error, long Allocated) Refused(Action construct)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var thrown = Assert.Throws<EinzelException>(construct);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return (thrown.Error, allocated);
    }

    [Fact]
    public void TheGridIsJustOverTheLimit() =>
        Assert.True(JustOver().NodeCount > VolumeMesh.MaximumNodes
            && new Grid3D(0.0, 0.0, 0.0, 1e-3, 1e-3, 1e-3, 400, 400, 400).NodeCount <= VolumeMesh.MaximumNodes);

    /// <summary>
    /// Each per-node allocation refuses, with the code, and before allocating anything worth
    /// the name.
    /// </summary>
    /// <remarks>
    /// A megabyte is generous for an error message and a suggestion search, and six hundred
    /// times smaller than the mask alone would be at this size - so an allocation made before
    /// the guard cannot hide under it.
    /// </remarks>
    [Fact]
    public void EveryPerNodeAllocationRefusesBeforeAllocating()
    {
        var grid = JustOver();

        foreach (var construct in new Action[]
        {
            () => _ = new DirichletMask3D(grid),
            () => _ = new CutLinks3D(grid),
            () => _ = new ScalarField3D(grid),
        })
        {
            var (error, allocated) = Refused(construct);

            Assert.Equal(ErrorCodes.GridTooLarge, error.Code);
            Assert.Equal(401.0 * 401 * 401, error.Observed!.Value);
            Assert.True(allocated < 1_000_000, $"allocated {allocated:N0} bytes before refusing");
        }
    }

    /// <summary>
    /// A solve is refused before its conductor mask exists, which is where the cost was.
    /// </summary>
    /// <remarks>
    /// The end-to-end form of the ordering above: a geometry over the limit, handed to the
    /// builder the way a run hands it, refuses in the first thing it allocates. With the guard
    /// in the field constructor alone this assembled a 64 M-node mask and its cut links - six
    /// arms of two doubles a node, about six gigabytes - and then refused.
    /// </remarks>
    [Fact]
    public void ASolveOverTheLimitIsRefusedBeforeItsMaskIsBuilt()
    {
        // The Astral's shape at 1 mm: 1025 x 65 x 1025, among the smallest meshes over the limit
        // that power-of-two rounding can produce.
        var geometry = new Geometry3D(
            0.0, 0.0, 0.0, 1.0, 0.06, 1.0, 1e-3,
            [
                new CompiledElectrode3D
                {
                    Name = "bead",
                    Shape = Electrode3DShape.Sphere,
                    CentreX = 0.5,
                    CentreY = 0.03,
                    CentreZ = 0.5,
                    Radius = 0.01,
                    Potential = 100.0,
                },
            ],
            Tolerance: 1e-6);

        var grid = GeometryBuilder3D.BuildGrid(geometry);

        Assert.Equal((1025, 65, 1025), (grid.CountX, grid.CountY, grid.CountZ));

        var (error, allocated) = Refused(() => GeometryBuilder3D.BuildField(geometry));

        Assert.Equal(ErrorCodes.GridTooLarge, error.Code);
        Assert.True(allocated < 1_000_000, $"allocated {allocated:N0} bytes before refusing");
    }

    /// <summary>A node count that would overflow saturates, and is refused rather than wrapped.</summary>
    /// <remarks>
    /// <c>OverBox</c> builds axes of up to 2^30 + 1 nodes, and the count was an unchecked product:
    /// 1073741825 x 1073741825 x 9 wrapped to a negative number, which the guard read as within
    /// its limit and passed to an allocation that failed as a defect. The nine is chosen for
    /// that - seventeen would have wrapped to a large positive count and been refused anyway,
    /// which is a test that passes with the bug in.
    /// </remarks>
    [Fact]
    public void ANodeCountThatWouldOverflowIsRefusedNotWrapped()
    {
        var grid = new Grid3D(0.0, 0.0, 0.0, 1e-9, 1e-9, 1e-9, 1_073_741_825, 1_073_741_825, 9);

        Assert.Equal(long.MaxValue, grid.NodeCount);
        Assert.Equal(ErrorCodes.GridTooLarge, Refused(() => _ = new ScalarField3D(grid)).Error.Code);
    }

    /// <summary>
    /// The suggestion from the backstop is a cell size that fits the grid's own extents.
    /// </summary>
    [Fact]
    public void TheBackstopSuggestsACellSizeThatFits()
    {
        var error = Refused(() => _ = new ScalarField3D(JustOver())).Error;

        Assert.Contains("is the finest that fits", error.Suggestion, StringComparison.Ordinal);
        Assert.Equal("/", error.Path);
    }

    /// <summary>
    /// An absurd cell size is refused promptly, where the old doubling never returned.
    /// </summary>
    /// <remarks>
    /// <c>OverBox</c> doubled an <see cref="int"/> interval count until it covered the span, and
    /// past 2^30 the doubling wrapped to zero and stayed there: a picometer over a meter hung the
    /// solve. It now counts in the shared, saturating arithmetic and refuses a count an index
    /// cannot hold. Run on another thread with a deadline, because the regression this guards
    /// against is a hang rather than a wrong answer.
    /// </remarks>
    [Fact]
    public async Task APicometerCellIsRefusedRatherThanHanging()
    {
        EinzelException? thrown = null;

        var worker = Task.Run(() =>
        {
            try
            {
                _ = Grid3D.OverBox(0.0, 0.0, 0.0, 1.0, 1.0, 1.0, 1e-12);
            }
            catch (EinzelException failure)
            {
                thrown = failure;
            }
        });

        var finished = await Task.WhenAny(worker, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.True(finished == worker, "OverBox did not return");
        Assert.Equal(ErrorCodes.GridTooLarge, thrown?.Error.Code);
    }

    /// <summary>A grid the old rule built, the new rule builds identically.</summary>
    /// <remarks>
    /// The rounding moved from <c>Grid3D</c> into <c>Einzel.Core</c> so the validator could share
    /// it. Every validated volume number depends on it being the same rule, so the counts and
    /// spacings are pinned on the case the estimate's mesh note was built around.
    /// </remarks>
    [Fact]
    public void TheSharedRoundingBuildsTheGridItAlwaysDid()
    {
        var grid = Grid3D.OverBox(0.0, -0.024, 0.0, 0.635, 0.024, 0.350, 1e-3);

        Assert.Equal((1025, 65, 513), (grid.CountX, grid.CountY, grid.CountZ));
        Assert.Equal(0.635 / 1024, grid.SpacingX);
        Assert.Equal(0.048 / 64, grid.SpacingY);
        Assert.Equal(0.350 / 512, grid.SpacingZ);
    }
}
