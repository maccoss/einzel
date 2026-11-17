using System.Runtime.CompilerServices;

using Einzel.Core.Numerics;
using Einzel.Fields.Solved;
using Einzel.Testing;

using Xunit.Abstractions;
using Xunit.Sdk;

namespace Einzel.Fields.Tests;

/// <summary>
/// That being watched does not change a solve, that not being watched costs nothing, and that
/// the measurement built on the watch gives an interval that holds what it claims.
/// </summary>
/// <remarks>
/// <see cref="GridArrays"/> sits in the solver so that the memory tests can read what a build
/// holds from inside it. It is in code that ships, so the things it must not do are asserted
/// rather than assumed: move a number, cost anything when nobody is watching, or make anything
/// throw because something is.
/// </remarks>
[Collection(nameof(WatchedInProcess))]
public sealed class GridArraysTests(ITestOutputHelper output)
{
    /// <summary>
    /// Each kind of solve, watched with a full collection forced at every array, gives the
    /// potential it gives unwatched, to the last bit of every node.
    /// </summary>
    [Fact]
    public void AWatchedSolveIsTheSameSolve()
    {
        var sphere = SolveMemoryTests.Sphere(0.000625);
        var slabs = SolveMemoryTests.Plates(0.0005);
        var rods = SolveMemoryTests.Rods(0.0002);

        (string Name, Func<double[]> Solve)[] solves =
        [
            ("volume, rediscretized", () => Volume(sphere, galerkin: false)),
            ("volume, galerkin", () => Volume(slabs, galerkin: true)),
            ("plane", () => Plane(rods)),
        ];

        foreach (var (name, solve) in solves)
        {
            var alone = solve();

            var arrays = 0;
            double[] watched;

            GridArrays.StartWatching((_, _) =>
            {
                arrays++;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            });

            try
            {
                watched = solve();
            }
            finally
            {
                Assert.Null(GridArrays.StopWatching());
            }

            var differing = 0;

            for (var node = 0; node < alone.Length; node++)
            {
                if (BitConverter.DoubleToInt64Bits(alone[node]) != BitConverter.DoubleToInt64Bits(watched[node]))
                {
                    differing++;
                }
            }

            output.WriteLine($"{name}: {alone.Length:N0} nodes, {arrays} arrays, {differing} nodes differ");

            // A solve that allocated nothing through GridArrays was not watched, and would pass
            // the comparison.
            Assert.True(arrays > 10, $"the {name} solve allocated {arrays} arrays through GridArrays");
            Assert.Equal(alone.Length, watched.Length);
            Assert.Equal(0, differing);
        }

        static double[] Volume(Geometry3D geometry, bool galerkin)
        {
            var grid = GeometryBuilder3D.BuildGrid(geometry);
            var mask = GeometryBuilder3D.BuildMask(geometry, grid);
            var (potential, _) = PoissonSolver3D.Solve(
                mask, 1e-10, 200, GeometryBuilder3D.Coarsener(geometry), galerkin: galerkin);

            return potential.Values.ToArray();
        }

        static double[] Plane(Core.Model.CompiledSolvedField solve)
        {
            var grid = GeometryBuilder.BuildGrid(solve);
            var mask = GeometryBuilder.BuildMask(solve, grid);
            var (potential, _) = PoissonSolver2D.Solve(
                mask, solve.Tolerance, maximumCycles: 400, coarsen: coarse => GeometryBuilder.BuildMask(solve, coarse));

            return potential.Values.ToArray();
        }
    }

    /// <summary>An array from <see cref="GridArrays"/> with nobody watching costs the array and nothing else.</summary>
    [Fact]
    public void AnUnwatchedAllocationAllocatesOnlyTheArray()
    {
        Assert.False(GridArrays.Watched);

        const int arrays = 1_000_000;

        long Allocated(Func<double[]> allocate)
        {
            // Once, so that anything a first call does is done.
            GC.KeepAlive(allocate());

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var array = 0; array < arrays; array++)
            {
                GC.KeepAlive(allocate());
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var direct = Allocated(() => new double[4]);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var through = Allocated(() => GridArrays.New<double>(4));

        // Printed and not asserted: a time is a statement about a machine. A solve allocates a
        // few hundred arrays.
        output.WriteLine(
            $"{through / (double)arrays:F1} bytes an array through GridArrays, {direct / (double)arrays:F1} with new; "
            + $"{clock.Elapsed.TotalNanoseconds / arrays:F1} ns an array, allocation included");

        // Less than a byte an array over what plain arrays cost. Anything the helper allocated a
        // call - a delegate, a closure, a box - would be at least 24 bytes an array; a few bytes
        // in all is the runtime doing something once inside one window, which it did, 32 bytes,
        // in one run in three when this asserted equality.
        Assert.InRange(through - direct, -arrays, arrays);
    }

    /// <summary>
    /// A grid array allocated on another thread while a build is measured is allocated as ever -
    /// nothing throws - and recorded, and the measurement says so by name.
    /// </summary>
    /// <remarks>
    /// The live set is read on the measuring thread at its own allocations, so an array a solver
    /// allocated on a worker would be neither read at nor bounded. Throwing there would make a
    /// production constructor fail because a test is measuring; recording it fails the
    /// measurement instead, which is where the problem is.
    /// </remarks>
    [Fact]
    public void AGridArrayAllocatedOnAnotherThreadWhileMeasuringIsRecordedNotRefused()
    {
        Exception? onWorker = null;
        double[]? allocated = null;

        var peak = LiveSet.Peak(() =>
        {
            var worker = new Thread(() =>
            {
                try
                {
                    allocated = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9)).Values.ToArray();
                }
                catch (Exception thrown)
                {
                    onWorker = thrown;
                }
            });

            worker.Start();
            worker.Join();

            return new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));
        });

        Assert.Null(onWorker);
        Assert.NotNull(allocated);
        Assert.NotNull(peak.Invalid);
        Assert.Contains("1 grid array(s), 5,832 bytes", peak.Invalid, StringComparison.Ordinal);
        Assert.Equal(1, peak.Readings);
        Assert.False(GridArrays.Watched);

        var named = Assert.Throws<FailException>(() => MemoryVerdict.Covered("a build", peak));
        Assert.Contains("while thread", named.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A watcher that throws does not make an allocation throw: the array is allocated, the
    /// failure is recorded, the watcher is not called again, and the watch reports it.
    /// </summary>
    [Fact]
    public void AWatcherThatThrowsDoesNotReachTheAllocation()
    {
        var calls = 0;

        GridArrays.StartWatching((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("the watcher's own failure");
        });

        double[] first, second;
        string? invalid;

        try
        {
            first = GridArrays.New<double>(10);
            second = GridArrays.New<double>(20);
        }
        finally
        {
            invalid = GridArrays.StopWatching();
        }

        Assert.Equal(10, first.Length);
        Assert.Equal(20, second.Length);
        Assert.Equal(1, calls);
        Assert.NotNull(invalid);
        Assert.Contains("the watcher's own failure", invalid, StringComparison.Ordinal);
    }

    /// <summary>A build that allocates nothing outside GridArrays reports nothing outside it.</summary>
    /// <remarks>
    /// Each array's header and padding are part of what <see cref="GridArrays"/> allocated, and
    /// counting only its elements booked them as allocated outside it - 24 bytes an array, never
    /// zero for a clean build.
    /// </remarks>
    [Fact]
    public void ABuildAllocatingOnlyGridArraysReportsNothingOutsideThem()
    {
        var holder = new object[3];

        object Build()
        {
            holder[0] = GridArrays.New<double>(1_000);
            holder[1] = GridArrays.New<byte>(7);
            holder[2] = GridArrays.New<Wide>(33);
            return holder;
        }

        // Once first, so that what the runtime does the first time a generic method is called
        // with a new type is not in the measurement.
        Build();

        var peak = LiveSet.Peak(Build, collectEveryArray: true);

        output.WriteLine(SolveMemoryTests.Describe(peak, 1));

        Assert.Equal(3, peak.Readings);
        Assert.Equal(0, peak.Silent);
        Assert.Equal(0, peak.Unreported);
        Assert.Equal(
            ArraySize.Of(8_000) + ArraySize.Of(7) + ArraySize.Of(33L * Unsafe.SizeOf<Wide>()),
            peak.Tracked);
    }

    /// <summary>
    /// Reading only where the live set could pass what has been read gives the same least as
    /// reading at every array, on each kind of solve, with far fewer collections.
    /// </summary>
    /// <remarks>
    /// A reading passed over is one at which an upper bound on the live set was already no
    /// more than the least, so it could not have raised the least; the bound stays a bound
    /// because that upper bound is folded into it. This is the check that the argument holds in
    /// practice: the same least, to the byte, from many fewer full collections.
    /// </remarks>
    [Fact]
    public void PassingOverReadingsThatCannotRaiseThePeakChangesNothing()
    {
        (string Name, Func<object> Build)[] builds =
        [
            ("volume, rediscretized", () => GeometryBuilder3D.BuildField(SolveMemoryTests.Sphere(0.000625)).Field),
            ("volume, galerkin", () => GeometryBuilder3D.BuildField(SolveMemoryTests.Plates(0.0005)).Field),
            ("plane", () => GeometryBuilder.Build(SolveMemoryTests.Rods(0.0002)).Field),
        ];

        foreach (var (name, build) in builds)
        {
            build();

            var every = LiveSet.Peak(build, collectEveryArray: true);
            var some = LiveSet.Peak(build);

            output.WriteLine($"{name}, every array: {SolveMemoryTests.Describe(every, 1)}");
            output.WriteLine($"{name}, where it could matter: {SolveMemoryTests.Describe(some, 1)}");

            Assert.Equal(every.Readings, some.Readings);
            Assert.Equal(every.Readings, every.Collections);
            Assert.True(some.Collections < every.Collections, $"{name}: no reading was passed over");
            Assert.Equal(every.Tracked, some.Tracked);

            // The process's own readings move by what the test host's other threads hold at each
            // instant - by more than 32 kB, once, in a full Debug run - so the least is compared
            // through the part only the build moves: the grid arrays, equal above, which the least
            // is never under.
            Assert.True(some.Least >= some.Tracked, $"{name}: the least is under the build's own arrays");
            Assert.True(some.Bound >= some.Least, $"{name}: the bound is under the least");
        }
    }

    /// <summary>
    /// An array a build allocates without <see cref="GridArrays"/> is in the bound, whether it is
    /// kept or dropped at once.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnArrayAllocatedDirectlyIsBoundedAsHeld(bool kept)
    {
        const int length = 200_000;
        const long bytes = sizeof(double) * (long)length;

        var peak = LiveSet.Peak(() =>
        {
            var through = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 33, 33, 33));
            var silent = new double[length];

            silent[0] = 1.0;

            // A second array through GridArrays, so that the silent one falls between two
            // readings and not in the stretch after the last.
            var later = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));

            return kept ? new object[] { through, silent, later } : new object[] { through, later };
        });

        output.WriteLine($"kept {kept}: {SolveMemoryTests.Describe(peak, 1)}");

        Assert.Equal(2, peak.Readings);
        Assert.InRange(peak.Unreported, bytes, bytes + 4096);

        // Held through the second reading or not, the build may have held it on top of the first.
        Assert.True(peak.Bound >= (8.0 * 33 * 33 * 33) + bytes, $"the bound is {peak.Bound:N0}");

        // Not asserted: that the least includes it when kept. An array outside GridArrays is
        // seen only in the process's reading, less what other threads allocated, and in this
        // test host they allocate enough to hide it - which a full Debug run did, once.
    }

    /// <summary>
    /// Memory another thread allocates and keeps during a measurement moves neither end of the
    /// interval: it stays tight about what the build held.
    /// </summary>
    /// <remarks>
    /// The build holds arrays of known size. Another thread allocates and keeps 32 kB blocks
    /// throughout, so every reading of the process is high by however many exist - hundreds of
    /// kilobytes. Read from the process alone, the least would fall by everything that thread
    /// allocated and the bound would rise by everything it kept. Read from the build's own grid
    /// arrays and its own thread's allocation, neither moves.
    /// </remarks>
    [Fact]
    public void AnotherThreadsMemoryDoesNotMoveTheInterval()
    {
        var truth = (24 * ArraySize.Of(8L * 9 * 9 * 9)) + ArraySize.Of(8L * 129 * 129 * 129);

        var kept = new List<byte[]>();
        var stop = 0;

        var other = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                lock (kept)
                {
                    kept.Add(new byte[32 * 1024]);
                }

                Thread.SpinWait(2_000);
            }
        });

        other.Start();

        LivePeak peak;

        try
        {
            peak = LiveSet.Peak(() =>
            {
                var fields = new object[24];

                // Many readings, each a full collection, with the other thread allocating
                // across all of them.
                for (var field = 0; field < fields.Length; field++)
                {
                    fields[field] = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));
                }

                return new object[] { fields, new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 129, 129, 129)) };
            });
        }
        finally
        {
            Volatile.Write(ref stop, 1);
            other.Join();
        }

        output.WriteLine($"truth {truth:N0} B, {kept.Count} blocks kept: {SolveMemoryTests.Describe(peak, 1)}");

        // The thread did disturb the process's reading: otherwise this tests nothing.
        Assert.True(peak.Foreign > 64 * 1024, $"the other thread allocated only {peak.Foreign:N0} bytes");
        Assert.True(peak.Held > truth + (32 * 1024), $"the process read {peak.Held:N0}, not disturbed");

        // And the interval holds the truth, a few kilobytes wide - the arrays' headers, the
        // field and grid objects, and what the build allocated outside GridArrays.
        Assert.InRange(peak.Least, truth - 4096, truth);
        Assert.InRange(peak.Bound, truth, truth + 8192);
    }

    /// <summary>
    /// What other threads allocate while the baseline settles is not charged to the build, and
    /// what they allocate after it is.
    /// </summary>
    /// <remarks>
    /// A finalizer that allocates runs while the baseline waits for pending finalizers. What it
    /// allocates is in the baseline or dead by the baseline's last collection, so it can be in no
    /// reading; counted from before the first collection, it was 256 kB of other threads' memory
    /// charged to a build that held a few kilobytes.
    /// </remarks>
    [Fact]
    public void AFinalizerRunWhileTheBaselineSettlesIsNotChargedToTheBuild()
    {
        Abandon();

        var peak = LiveSet.Peak(() => new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9)));

        output.WriteLine($"{AllocatesWhenFinalized.Finalized} finalized: {SolveMemoryTests.Describe(peak, 1)}");

        Assert.Equal(1, AllocatesWhenFinalized.Finalized);
        Assert.True(peak.Foreign < 64 * 1024, $"{peak.Foreign:N0} bytes of other threads were charged to the build");

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Abandon() => _ = new AllocatesWhenFinalized();
    }

    /// <summary>A struct element is sized by what it is, not by what the array type says it holds.</summary>
    [Fact]
    public void AStructArrayIsSizedByItsElement()
    {
        var sizes = new List<long>();

        GridArrays.StartWatching((_, bytes) => sizes.Add(bytes));

        try
        {
            GC.KeepAlive(GridArrays.New<Wide>(1_000));
            GC.KeepAlive(GridArrays.New<byte>(1_000));
        }
        finally
        {
            Assert.Null(GridArrays.StopWatching());
        }

        Assert.Equal([1_000L * Unsafe.SizeOf<Wide>(), 1_000L], sizes);
        Assert.Equal(24, Unsafe.SizeOf<Wide>());
    }

    /// <summary><see cref="GridArrays.Resize{T}"/> does what <see cref="Array.Resize{T}"/> does.</summary>
    [Fact]
    public void ResizeIsArrayResize()
    {
        var allocated = 0;

        GridArrays.StartWatching((_, _) => allocated++);

        try
        {
            double[]? none = null;
            GridArrays.Resize(ref none, 3);
            Assert.Equal(3, none.Length);
            Assert.Equal(1, allocated);

            var same = none;
            GridArrays.Resize(ref same, 3);
            Assert.Same(none, same);
            Assert.Equal(1, allocated);

            double[] grown = [1.0, 2.0, 3.0];
            GridArrays.Resize(ref grown, 5);
            Assert.Equal([1.0, 2.0, 3.0, 0.0, 0.0], grown);
            Assert.Equal(2, allocated);

            GridArrays.Resize(ref grown, 2);
            Assert.Equal([1.0, 2.0], grown);
            Assert.Equal(3, allocated);

            Assert.Throws<ArgumentOutOfRangeException>(() => GridArrays.Resize(ref grown, -1));
            Assert.Equal(3, allocated);

            // The reference it copies: Array.Resize, the same calls, the same arrays.
            double[]? reference = null;
            Array.Resize(ref reference, 3);
            var referenceSame = reference;
            Array.Resize(ref referenceSame, 3);
            Assert.Same(reference, referenceSame);
        }
        finally
        {
            Assert.Null(GridArrays.StopWatching());
        }
    }

    /// <summary>
    /// A figure past its edge fails as a finding about the solver when other threads could not
    /// account for the excess, and as a disturbed measurement, by name, when they could.
    /// </summary>
    [Fact]
    public void AnEdgeOtherThreadsCouldExplainIsInconclusiveNotAFinding()
    {
        // 0.5 B/node over an edge, on 400,000 nodes: 200 kB.
        var finding = Assert.Throws<FailException>(
            () => MemoryVerdict.Within("a slope", 44.0, 44.5, 43.0, 44.0, 400_000, foreign: 10_000));
        var disturbed = Assert.Throws<FailException>(
            () => MemoryVerdict.Within("a slope", 44.0, 44.5, 43.0, 44.0, 400_000, foreign: 300_000));
        var shortfall = Assert.Throws<FailException>(
            () => MemoryVerdict.Within("a slope", 42.0, 43.5, 43.0, 44.0, 400_000, foreign: 10_000));

        Assert.Contains("not billed", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MemoryVerdict.Disturbed, finding.Message, StringComparison.Ordinal);
        Assert.StartsWith(MemoryVerdict.Disturbed, disturbed.Message, StringComparison.Ordinal);
        Assert.Contains("bills what is not held", shortfall.Message, StringComparison.Ordinal);

        MemoryVerdict.Within("a slope", 43.5, 43.9, 43.0, 44.0, 400_000, foreign: 1_000_000);
    }

    /// <summary>Three doubles: an element type <see cref="Buffer.ByteLength"/> cannot size.</summary>
    private readonly record struct Wide(double X, double Y, double Z);

    /// <summary>An object whose finalizer allocates, on the finalizer thread.</summary>
    private sealed class AllocatesWhenFinalized
    {
        private static int finalized;

        public static int Finalized => Volatile.Read(ref finalized);

        ~AllocatesWhenFinalized()
        {
            GC.KeepAlive(new byte[256 * 1024]);
            Interlocked.Increment(ref finalized);
        }
    }
}
