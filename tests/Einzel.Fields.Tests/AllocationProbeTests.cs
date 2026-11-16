using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// That being watched does not change a solve, and that not being watched costs nothing.
/// </summary>
/// <remarks>
/// <see cref="AllocationProbe"/> sits in the solver so that the memory tests can read what a
/// build holds from inside it. It is in code that ships, so the two things it must not do are
/// asserted rather than assumed: move a number, and cost anything when nobody is observing.
/// </remarks>
[Collection(nameof(MemoryMeasurement))]
public sealed class AllocationProbeTests(ITestOutputHelper output)
{
    /// <summary>
    /// Each kind of solve, observed with a full collection forced at every report, gives the
    /// potential it gives unobserved, to the last bit of every node.
    /// </summary>
    [Fact]
    public void AnObservedSolveIsTheSameSolve()
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

            var reports = 0;
            double[] watched;

            AllocationProbe.Observer = _ =>
            {
                reports++;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            };

            try
            {
                watched = solve();
            }
            finally
            {
                AllocationProbe.Observer = null;
            }

            var differing = 0;

            for (var node = 0; node < alone.Length; node++)
            {
                if (BitConverter.DoubleToInt64Bits(alone[node]) != BitConverter.DoubleToInt64Bits(watched[node]))
                {
                    differing++;
                }
            }

            output.WriteLine($"{name}: {alone.Length:N0} nodes, {reports} reports, {differing} nodes differ");

            // A solve that reported nothing was not watched, and would pass the comparison.
            Assert.True(reports > 10, $"the {name} solve reported {reports} allocations");
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

    /// <summary>A report nobody is observing allocates nothing.</summary>
    [Fact]
    public void AReportNobodyObservesAllocatesNothing()
    {
        Assert.Null(AllocationProbe.Observer);

        var array = new double[4];
        var other = new int[4];

        // Once, so that anything a first call does is done.
        AllocationProbe.Allocated(array, other);

        const int reports = 100_000_000;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var report = 0; report < reports; report++)
        {
            AllocationProbe.Allocated(array);
            AllocationProbe.Allocated(array, other);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Printed and not asserted: a time is a statement about a machine. A solve makes a few
        // hundred reports.
        output.WriteLine($"{clock.Elapsed.TotalNanoseconds / (2.0 * reports):F2} ns a report with nobody observing");

        Assert.Equal(0L, allocated);
    }

    /// <summary>An observer is this thread's: a solve on another thread is not reported to it.</summary>
    [Fact]
    public void AnObserverIsToldOnlyAboutItsOwnThread()
    {
        var reports = 0;

        AllocationProbe.Observer = _ => reports++;

        try
        {
            var other = new Thread(() => _ = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9)));

            other.Start();
            other.Join();

            Assert.Equal(0, reports);

            _ = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));

            Assert.Equal(1, reports);
        }
        finally
        {
            AllocationProbe.Observer = null;
        }
    }

    /// <summary>
    /// An array a build allocates without reporting is in the bound, whether it is kept or
    /// dropped at once.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnreportedArrayIsBoundedAsHeld(bool kept)
    {
        const int length = 200_000;
        const long bytes = sizeof(double) * (long)length;

        var peak = LiveSet.Peak(() =>
        {
            var reported = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 33, 33, 33));
            var silent = new double[length];

            silent[0] = 1.0;

            // A second report, so that the silent array falls between two readings and not in
            // the stretch after the last.
            var later = new ScalarField3D(new Grid3D(0, 0, 0, 1, 1, 1, 9, 9, 9));

            return kept ? new object[] { reported, silent, later } : new object[] { reported, later };
        });

        output.WriteLine(
            $"kept {kept}: {peak.Held:N0} B held, {peak.Bound:N0} at most, {peak.Unreported:N0} unreported between two readings");

        Assert.Equal(2, peak.Readings);
        Assert.InRange(peak.Unreported, bytes, bytes + 4096);

        // Held through the second reading or not, the build may have held it on top of the first.
        Assert.True(peak.Bound >= (8.0 * 33 * 33 * 33) + bytes, $"the bound is {peak.Bound:N0}");
    }

    /// <summary>
    /// A report cannot be used to hide an allocation: an array reported twice, or one allocated
    /// before the last report, is refused by name.
    /// </summary>
    /// <remarks>
    /// The observer takes what the reported arrays hold off what the thread allocated and calls
    /// the rest unreported. While a site stated a byte count, a count 800 kB too large beside
    /// 800 kB allocated silently came to nothing unreported, and passed. A site hands over the
    /// arrays now, so the two ways left to report more than was allocated are these.
    /// </remarks>
    [Fact]
    public void AReportCannotHideAnAllocation()
    {
        var twice = Assert.Throws<InvalidOperationException>(() => LiveSet.Peak(() =>
        {
            // 800 kB reported twice beside 800 kB unreported: the sizes cancel exactly.
            var array = new double[100_000];

            AllocationProbe.Allocated(array);

            var silent = new double[100_000];

            AllocationProbe.Allocated(array);

            return new object[] { silent, array };
        }));

        Assert.Contains("twice", twice.Message, StringComparison.Ordinal);
        Assert.Null(AllocationProbe.Observer);

        var old = new double[100_000];

        var earlier = Assert.Throws<InvalidOperationException>(() => LiveSet.Peak(() =>
        {
            AllocationProbe.Allocated(old);

            return old;
        }));

        Assert.Contains("allocated earlier", earlier.Message, StringComparison.Ordinal);
        Assert.Null(AllocationProbe.Observer);

        // The control: the same build reporting each array once, as it allocates it, is
        // measured, and the silent array is in its bound.
        var peak = LiveSet.Peak(() =>
        {
            var array = new double[100_000];

            AllocationProbe.Allocated(array);

            var silent = new double[100_000];
            var last = new double[2_000];

            AllocationProbe.Allocated(last);

            return new object[] { silent, array, last };
        });

        Assert.Equal(2, peak.Readings);
        Assert.InRange(peak.Unreported, 800_000, 800_000 + 4096);
    }
}
