using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// That being watched does not change a solve, that not being watched costs nothing, and that
/// the measurement built on the watch bounds what it cannot see.
/// </summary>
/// <remarks>
/// <see cref="GridArrays"/> sits in the solver so that the memory tests can read what a build
/// holds from inside it. It is in code that ships, so the two things it must not do are asserted
/// rather than assumed: move a number, and cost anything when nobody is watching.
/// </remarks>
[Collection(nameof(MemoryMeasurement))]
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

            GridArrays.StartWatching(_ =>
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
                GridArrays.StopWatching();
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

        Assert.Equal(direct, through);
    }

    /// <summary>
    /// A grid array allocated on another thread while a build is being measured is refused by
    /// name, and allowed again once the measurement ends.
    /// </summary>
    /// <remarks>
    /// The live set is read on the measuring thread at its own allocations. A solver that
    /// allocated a scratch array on a worker and dropped it would be read low and nothing would
    /// say so: the array is neither read at nor bounded, and one of them is too small a part of
    /// a build for the count of other threads' bytes to show.
    /// </remarks>
    [Fact]
    public void AGridArrayAllocatedOnAnotherThreadWhileMeasuringIsRefused()
    {
        Exception? onWorker = null;

        var peak = LiveSet.Peak(() =>
        {
            var worker = new Thread(() =>
            {
                try
                {
                    _ = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));
                }
                catch (InvalidOperationException refused)
                {
                    onWorker = refused;
                }
            });

            worker.Start();
            worker.Join();

            return new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));
        });

        Assert.NotNull(onWorker);
        Assert.Contains("is measuring what a build holds", onWorker.Message, StringComparison.Ordinal);
        Assert.Equal(1, peak.Readings);
        Assert.False(GridArrays.Watched);

        // The control: with no measurement running, any thread may allocate.
        Exception? unwatched = null;

        var free = new Thread(() =>
        {
            try
            {
                _ = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));
            }
            catch (InvalidOperationException refused)
            {
                unwatched = refused;
            }
        });

        free.Start();
        free.Join();

        Assert.Null(unwatched);
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

        output.WriteLine(
            $"kept {kept}: {peak.Held:N0} B held, {peak.Bound:N0} at most, {peak.Unreported:N0} unreported between two readings");

        Assert.Equal(2, peak.Readings);
        Assert.InRange(peak.Unreported, bytes, bytes + 4096);

        // Held through the second reading or not, the build may have held it on top of the first.
        Assert.True(peak.Bound >= (8.0 * 33 * 33 * 33) + bytes, $"the bound is {peak.Bound:N0}");
    }

    /// <summary>
    /// What another thread allocates and keeps during a measurement is taken off the least a
    /// build can have held, and never off the most.
    /// </summary>
    /// <remarks>
    /// The build here holds one array of a known size. Another thread allocates and keeps
    /// blocks throughout, so every reading is too high by however many of them exist: the
    /// interval must still contain the truth, which it does only if everything of that thread's
    /// that could be in a reading has been counted by the time it is taken off.
    /// </remarks>
    [Fact]
    public void AnotherThreadsMemoryWidensTheIntervalDownwardOnly()
    {
        const long truth = 8L * 129 * 129 * 129;

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

        var small = 24 * 8L * 9 * 9 * 9;

        output.WriteLine(
            $"truth {truth + small:N0} B: least {peak.Least:N0}, read {peak.Held:N0}, at most {peak.Bound:N0}; "
            + $"{peak.Foreign:N0} B from the other thread, {kept.Count} blocks kept, {peak.Retaken} retaken");

        Assert.True(peak.Foreign > 0, "the other thread allocated nothing during the measurement");
        Assert.True(peak.Least <= truth + small + 4096, $"the least is {peak.Least:N0}, above what the build held");
        Assert.True(peak.Bound >= truth + small, $"the bound is {peak.Bound:N0}, under what the build held");
    }
}
