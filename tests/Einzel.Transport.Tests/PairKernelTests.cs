using Einzel.Core.Geometry;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The vectorised pair sum against the scalar reference it replaces. CMP-1 requires the
/// scalar implementation never to be deleted or allowed to rot, and a reference nothing
/// runs is one that rots quietly - so it is selectable, and every case the vectorised
/// path handles differently is compared against it here.
/// </summary>
/// <remarks>
/// They cannot agree to the last bit and are not asked to: the additions happen in a
/// different order, so the two differ by rounding. What is asserted is agreement to a
/// few parts in 10^13 of the acceleration scale, which is far tighter than any physical
/// claim made from this sum and far looser than bit equality.
/// </remarks>
public sealed class PairKernelTests(ITestOutputHelper output)
{
    private const double Population = 2.0e5;

    private static (Vec3[] Positions, bool[] Active) Packet(int n, int seed, double inactiveFraction = 0.0)
    {
        var random = new Random(seed);
        var positions = new Vec3[n];
        var active = new bool[n];

        for (var i = 0; i < n; i++)
        {
            // Long and thin, which is the shape a linear trap's cloud actually has and
            // the one where the separations span the widest range.
            positions[i] = new Vec3(
                (random.NextDouble() - 0.5) * 0.1e-3,
                (random.NextDouble() - 0.5) * 0.1e-3,
                (random.NextDouble() - 0.5) * 4.0e-3);
            active[i] = random.NextDouble() >= inactiveFraction;
        }

        return (positions, active);
    }

    private static CoulombInteraction Interaction(int n, CoulombInteraction.PairKernel kernel) =>
        new(Population, n, 1.602176634e-19, 500.0 * 1.66053906660e-27,
            CoulombInteraction.SpacingSoftening(0.05e-3, 0.05e-3, 1.0e-3, n))
        { Kernel = kernel };

    private static double WorstRelative(Vec3[] a, Vec3[] b, out double scale)
    {
        scale = 0.0;
        foreach (var one in a)
        {
            scale = Math.Max(scale, one.Length);
        }

        if (scale <= 0.0)
        {
            return 0.0;
        }

        var worst = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            worst = Math.Max(worst, (a[i] - b[i]).Length / scale);
        }

        return worst;
    }

    /// <summary>
    /// Every size that exercises a different part of the loop: below one vector, exactly
    /// one, one plus a tail, and packets large enough that the row is mostly vectors.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(31)]
    [InlineData(240)]
    [InlineData(1001)]
    public void TheVectorisedSumAgreesWithTheScalarReference(int n)
    {
        var (positions, active) = Packet(n, 4_000 + n);

        var fromScalar = new Vec3[n];
        var fromVector = new Vec3[n];

        Interaction(n, CoulombInteraction.PairKernel.Scalar).Accumulate(positions, active, fromScalar);
        Interaction(n, CoulombInteraction.PairKernel.Vector).Accumulate(positions, active, fromVector);

        var worst = WorstRelative(fromScalar, fromVector, out var scale);
        output.WriteLine($"N={n,5}  worst disagreement {worst:E3} of an acceleration scale of {scale:E3} m/s^2");

        Assert.True(worst < 1e-13, $"N={n}: {worst}");
        Assert.True(scale > 0.0, "the packet must actually push on itself, or agreeing proves nothing");
    }

    /// <summary>
    /// With members switched off. The vectorised path compacts the active members before
    /// it starts, which is a whole extra mechanism the scalar path does not have, and the
    /// mapping back is where an off-by-one would land.
    /// </summary>
    [Theory]
    [InlineData(240, 0.25)]
    [InlineData(240, 0.75)]
    [InlineData(37, 0.5)]
    public void TheyAgreeWithMembersInactive(int n, double inactiveFraction)
    {
        var (positions, active) = Packet(n, 9_100 + n, inactiveFraction);
        var live = active.Count(a => a);

        var fromScalar = new Vec3[n];
        var fromVector = new Vec3[n];

        Interaction(n, CoulombInteraction.PairKernel.Scalar).Accumulate(positions, active, fromScalar);
        Interaction(n, CoulombInteraction.PairKernel.Vector).Accumulate(positions, active, fromVector);

        var worst = WorstRelative(fromScalar, fromVector, out _);
        output.WriteLine($"N={n}, {live} active: worst disagreement {worst:E3}");

        Assert.True(worst < 1e-13, $"{worst}");

        // And an inactive member is pushed by nobody, in both paths.
        for (var i = 0; i < n; i++)
        {
            if (!active[i])
            {
                Assert.Equal(0.0, fromVector[i].Length);
                Assert.Equal(0.0, fromScalar[i].Length);
            }
        }
    }

    /// <summary>
    /// The accumulator is added to, not assigned. The integrator passes one buffer that
    /// may already carry a contribution, and a vectorised path that wrote instead of
    /// adding would silently discard it.
    /// </summary>
    [Fact]
    public void TheVectorisedSumAddsToWhatIsAlreadyThere()
    {
        const int N = 64;
        var (positions, active) = Packet(N, 55);
        var seeded = new Vec3(1.0, -2.0, 3.0);

        var alone = new Vec3[N];
        var onTop = new Vec3[N];
        Array.Fill(onTop, seeded);

        var interaction = Interaction(N, CoulombInteraction.PairKernel.Vector);
        interaction.Accumulate(positions, active, alone);
        interaction.Accumulate(positions, active, onTop);

        for (var i = 0; i < N; i++)
        {
            Assert.Equal((alone[i] + seeded).X, onTop[i].X, 12);
            Assert.Equal((alone[i] + seeded).Y, onTop[i].Y, 12);
            Assert.Equal((alone[i] + seeded).Z, onTop[i].Z, 12);
        }
    }

    /// <summary>
    /// Newton's third law in the vectorised path: the accelerations sum to nothing, which
    /// is exactly true for a mutual force whatever the arrangement. This is the check that
    /// would catch a lane written to the wrong member.
    /// </summary>
    [Theory]
    [InlineData(240)]
    [InlineData(1001)]
    public void TheVectorisedSumBalances(int n)
    {
        var (positions, active) = Packet(n, 77_000 + n);
        var acc = new Vec3[n];

        Interaction(n, CoulombInteraction.PairKernel.Vector).Accumulate(positions, active, acc);

        var total = default(Vec3);
        var magnitude = 0.0;
        foreach (var one in acc)
        {
            total += one;
            magnitude += one.Length;
        }

        var imbalance = total.Length / magnitude;
        output.WriteLine($"N={n}: imbalance {imbalance:E3} of an accumulated magnitude of {magnitude:E3}");
        Assert.True(imbalance < 1e-12, $"{imbalance}");
    }

    /// <summary>
    /// Repeated calls give the same answer. The vectorised path keeps pooled scratch
    /// across calls and grows it on demand, so a buffer left dirty by a larger packet
    /// would show up here and nowhere else.
    /// </summary>
    [Fact]
    public void PooledScratchDoesNotCarryBetweenCalls()
    {
        var interaction = Interaction(1000, CoulombInteraction.PairKernel.Vector);

        var (big, bigActive) = Packet(1000, 12);
        var bigAcc = new Vec3[1000];
        interaction.Accumulate(big, bigActive, bigAcc);

        var (small, smallActive) = Packet(37, 13);
        var first = new Vec3[37];
        var second = new Vec3[37];
        interaction.Accumulate(small, smallActive, first);
        interaction.Accumulate(small, smallActive, second);

        // Built with the SAME weighting as the pooled one, not with the small packet's:
        // the charge and softening per macroparticle come from the declared count, so a
        // reference constructed for 37 is a different physical packet and would disagree
        // for a reason that has nothing to do with pooling. An earlier version of this
        // test did exactly that and reported the correct code as broken.
        var reference = new Vec3[37];
        Interaction(1000, CoulombInteraction.PairKernel.Scalar).Accumulate(small, smallActive, reference);

        Assert.Equal(0.0, WorstRelative(first, second, out _));
        Assert.True(WorstRelative(reference, first, out _) < 1e-13);
    }
}
