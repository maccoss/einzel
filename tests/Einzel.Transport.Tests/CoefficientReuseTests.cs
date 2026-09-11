using Einzel.Fields.Solved;
using Einzel.Transport.Diffusion;

namespace Einzel.Transport.Tests;

/// <summary>Scratch reuse changes allocation, not the Scharfetter–Gummel operator.</summary>
public sealed class CoefficientReuseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedBuffersMatchFreshBuffersIncludingSkippedFaces(bool cylindrical)
    {
        var grid = Grid2D.OverBox(0, 0, 0.032, 0.016, 32, 16);
        var density = new DensityField(grid, cylindrical);
        var cells = grid.CountX * grid.CountY;
        var zero = new double[cells];
        var diffusion = Enumerable.Repeat(1e-3, cells).ToArray();
        var potential = Enumerable.Range(0, cells).Select(i => (double)(i % grid.CountX)).ToArray();
        var open = new DriftDiffusion.DomainEdges(Escape.Absorbing, Escape.Collecting, Escape.Absorbing, Escape.Collecting);
        var closed = new DriftDiffusion.DomainEdges(Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);
        FaceCoefficients Build(DriftDiffusion.DomainEdges edges, FaceCoefficients? reuse = null) =>
            FaceCoefficients.Assemble(density, grid, zero, zero, zero, zero, diffusion, potential,
                0.025, edges, AbsorbingCells.None, reuse);
        var scratch = Build(open);
        var actual = Build(closed, scratch);
        var expected = Build(closed);
        Assert.Same(scratch, actual);
        for (var i = 0; i < cells; i++)
        {
            Assert.Equal(expected.Outward(i), actual.Outward(i));
            for (var f = 0; f < 4; f++)
            {
                Assert.Equal(expected.Flux(i, f, 0.37, 0.91), actual.Flux(i, f, 0.37, 0.91));
                Assert.Equal(expected.Leaves(i, f), actual.Leaves(i, f));
                Assert.Equal(expected.Collects(i, f), actual.Collects(i, f));
                Assert.Equal(expected.NameOf(i, f), actual.NameOf(i, f));
            }
        }

        long Allocated(bool reuse)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 10; k++) Build(open, reuse ? scratch : null);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        _ = Allocated(true);
        var freshBytes = Allocated(false);
        var reusedBytes = Allocated(true);
        Assert.True(reusedBytes < freshBytes / 100, $"fresh: {freshBytes}; reused: {reusedBytes}");
    }
}
