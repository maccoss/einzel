using System.Diagnostics;

using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The measurements here force full collections and read the whole process's live set, so they
/// must not share the process with anything else allocating: another test's arrays would be
/// counted as this build's. xUnit runs a collection with parallelization disabled after every
/// parallel one.
/// </summary>
[CollectionDefinition(nameof(MemoryMeasurement), DisableParallelization = true)]
public sealed class MemoryMeasurement : ICollectionFixture<MeasuredBuilds>;

/// <summary>
/// What a build says it will hold, against what a build actually holds.
/// </summary>
/// <remarks>
/// <para>
/// <c>einzel estimate</c> reported 48 bytes a node for every solve, from a comment listing
/// three fields and some restriction buffers, and nothing measured it. A volume solve held
/// five times that: the conductor mask and cut links storing all six arms of every node were
/// 105 bytes a node before a field was allocated, and the Galerkin hierarchy stores the fine
/// operator and forms its first coarse level from it. The Astral at a 1 mm cell was billed
/// 3.1 GiB and needed 15. With the cut links stored over the cut arms alone it needs about 9.5.
/// </para>
/// <para>
/// <b>Read from inside the build, at each allocation</b> (<see cref="LiveSet"/>): a build's
/// live set can only rise at an allocation, so the solver allocates each per-node array through
/// <see cref="GridArrays"/> and the live set is read there, on the build's thread, after a forced full collection. The largest
/// reading is the peak. This was a second thread sampling the heap, which has to land inside
/// the peak - a millisecond on the smaller grids here - and on loaded CI runners did not; a
/// missed peak read low, and a slope between two builds turned a low reading of the smaller
/// one into an excess.
/// </para>
/// <para>
/// <b>Each measurement is an interval, and each edge is asked of the end that can only fail
/// truly.</b> <see cref="LivePeak.Least"/> was held; <see cref="LivePeak.Bound"/> adds whatever
/// the thread allocated between readings without reporting it, and the build cannot have held
/// more. An upper edge is asserted on the bound and a lower edge on what was held, so a
/// per-node allocation somebody adds without a report fails the upper edge as slack.
/// </para>
/// <para>
/// <b>As a slope between two mesh sizes where a build has a cost that does not scale</b> - the
/// geometry, small arrays, a first level that is a little more than an eighth - so that it
/// cancels and what is compared is bytes a node. With readings that cannot miss, a slope is
/// the difference of two exact figures and has no sign to get wrong.
/// </para>
/// <para>
/// <b>Against the arithmetic with the geometry's real cut arms</b>, not the billed quarter of
/// an arm a node: the allowance is 5.5 bytes a node where a sphere or two slabs store under 2,
/// and on the rediscretized hierarchy's 51 that is 7 percent of a 10 percent band.
/// </para>
/// <para>
/// <b>The upper edge is the one that matters.</b> An estimate that runs under is discovered by
/// the run failing twenty minutes in, which is the failure GRD-8's gate exists to prevent. If
/// a solver change adds a per-node array, this is what fails, and the fix is a term in
/// <see cref="SolveMemoryModel"/> beside the reason for the array - and a report to
/// <see cref="GridArrays"/>, which is where it has to be allocated.
/// </para>
/// </remarks>
[Collection(nameof(MemoryMeasurement))]
public sealed class SolveMemoryTests(MeasuredBuilds builds, ITestOutputHelper output)
{
    /// <summary>The Galerkin hierarchy's peak, per node, against two thin slabs at two meshes.</summary>
    [Fact]
    public void AGalerkinBuildHoldsWhatItsStatementSays()
    {
        var slope = Slope("slabs", Plates, 0.0005, 0.00025);

        Assert.True(slope.Galerkin, "thin slabs leave the rediscretized hierarchy a bottom the size of the mesh");
        slope.AssertWithin(0.90, 1.02);
    }

    /// <summary>The rediscretized hierarchy's peak, per node, against a fat sphere at two meshes.</summary>
    /// <remarks>
    /// At 35,937 and 274,625 nodes rather than 4,913 and 35,937, as it was sized when the live
    /// set was sampled and the smaller pair's slope scattered by 50 kB in 1.3 MiB.
    /// </remarks>
    [Fact]
    public void ARediscretizedBuildHoldsWhatItsStatementSays()
    {
        var slope = Slope("sphere", Sphere, 0.000625, 0.0003125);

        Assert.False(slope.Galerkin, "a fat sphere coarsens to a small bottom and keeps the cheap hierarchy");
        slope.AssertWithin(0.90, 1.02);
    }

    /// <summary>
    /// A rediscretized solve that builds no coarse level holds the bottom solve's scratch over
    /// the whole fine grid, and is charged it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sphere smaller than two cells stops the descent before it starts, so the V-cycle is the
    /// bottom solve on the fine grid, and <c>SolveCoarsest</c> allocates a residual the size of
    /// that grid for it: 46.5 bytes a node with the billed cut arms, where the deep grid's
    /// formula, which has no bottom term worth having, gave 38.5. One cycle is enough, because
    /// there is no coarse mask for a second cycle to find already built.
    /// </para>
    /// <para>
    /// <b>Driven through <see cref="PoissonSolver3D.Solve"/> with the hierarchy forced</b>
    /// (<see cref="MemoryCases.Direct"/>), at
    /// one cell size with the box doubled, rather than through a template: a rediscretized solve
    /// with no coarse level is chosen by the solver only below twenty thousand nodes, too small
    /// to measure, while the arithmetic is the same on any grid with the same descent.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARediscretizedSolveWithNoCoarseLevelIsChargedItsBottomScratch()
    {
        var reading = DirectSlope("ball-flat", 0.016, 0.032);

        Assert.Equal(0, reading.Levels);
        reading.AssertWithin(0.90, 1.02);
    }

    /// <summary>A rediscretized solve one coarse level deep, against its statement.</summary>
    /// <remarks>
    /// A sphere three cells across is resolved by one coarsening and not by two. At one level
    /// the fine restriction holds 17 bytes a coarse node and the bottom 33, so the two instants
    /// are within two bytes a node of each other and both are worked out.
    /// </remarks>
    [Fact]
    public void ARediscretizedSolveOneLevelDeepHoldsWhatItsStatementSays()
    {
        var reading = DirectSlope("ball-one-level", 0.016, 0.032);

        Assert.Equal(1, reading.Levels);
        reading.AssertWithin(0.90, 1.02);
    }

    /// <summary>
    /// A rediscretized solve on a grid thin on two axes, whose levels shrink by much less than an
    /// eighth, holds every memoised coarse mask from its second cycle on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With no interior electrode the descent goes as far as the grid allows: 8193 by 9 by 9,
    /// then 4097 by 5 by 5 (0.154 of the fine grid) and 2049 by 3 by 3 (0.028). The coarsener
    /// keeps both masks for the whole solve, so the fine restriction from the second cycle on
    /// holds the second level's mask as well - 0.25 bytes a node that charging only the masks
    /// down to the level being restricted onto left out, and which this measures: 43.88 bytes a
    /// node against 43.87 stated with the real cut arms, and 43.62 without the second mask.
    /// A Poisson problem with a uniform source, since with no electrode a Laplace problem is
    /// solved before it starts.
    /// </para>
    /// <para>
    /// <b>The test that showed what sampling cost.</b> The peak is the fine restriction: the
    /// fine residual and the first level's restricted residual alive together for as long as
    /// <c>Restrict</c> takes, about a millisecond on these grids, once in a two-cycle solve. A
    /// sampling thread descheduled across it read the fine residual alone, 8 x 0.154 = 1.24
    /// bytes a node low. This test doubles one axis, so the difference in nodes between its
    /// builds is the smaller build, and the slope took the smaller build's miss at full weight
    /// and the wrong sign: builds printed at 42.64 and 43.88 made 45.11, 1.028 of the
    /// statement, on a Linux CI runner whose every step was sound. Read at the allocation there
    /// is nothing to miss.
    /// </para>
    /// <para>
    /// Two cycles, because the second level's mask is memoised during the first and is held at
    /// the fine restriction from the second on; <see cref="DirectSlope"/> fails if the solve
    /// stops before it has run them.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARediscretizedSolveOnAThinGridHoldsWhatItsStatementSays()
    {
        var reading = DirectSlope("thin", 4.096, 8.192);

        Assert.Equal(2, reading.Levels);
        reading.AssertWithin(0.90, 1.02);

        // And to a tenth of a byte, which this geometry can be held to: a grid with no conductor
        // has no cut arms to bill. Charging the masks only down to the level being restricted
        // onto read 0.25 over.
        Assert.True(
            reading.Upper <= reading.Predicted + 0.15,
            $"the slope read at most {reading.Upper:F2} B/node against a prediction of {reading.Predicted:F2}: something the cycle holds is not charged");
    }

    /// <summary>
    /// The rediscretized statement's arithmetic at every depth, worked out here from node counts.
    /// </summary>
    [Fact]
    public void TheRediscretizedStatementChargesEveryMaskAndTheBottom()
    {
        const double fixedPart = 9 + 5.5 + 24;

        // No coarse level: the fixed part and the bottom's scratch over the fine grid.
        var flat = new Grid3D(0, 0, 0, 1, 1, 1, 33, 33, 33);
        Assert.Equal(fixedPart + 8, SolveMemoryModel.VolumeRediscretizedPeakOn(flat, []), 12);

        // 8193 by 9 by 9, two levels: the fine restriction holds the fine residual, the first
        // level's restricted residual, and both masks.
        var thin = new Grid3D(0, 0, 0, 1, 1, 1, 8193, 9, 9);
        var first = thin.Coarsen();
        var second = first.Coarsen();
        var r1 = (double)first.NodeCount / thin.NodeCount;
        var r2 = (double)second.NodeCount / thin.NodeCount;
        var restriction = 8 + (8 * r1);
        var bottom = (16 * (r1 + r2)) + (8 * r2);

        Assert.Equal(
            fixedPart + (9 * (r1 + r2)) + Math.Max(restriction, bottom),
            SolveMemoryModel.VolumeRediscretizedPeakOn(thin, [first, second]),
            12);

        // A deep grid's constant: the fine restriction and every mask of a series of eighths.
        Assert.Equal(fixedPart + 8 + 1 + (9.0 / 7.0), SolveMemoryModel.VolumeRediscretizedPeak, 12);
    }

    /// <summary>
    /// A second basis channel costs one kept potential, not a second geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Channels are solved one at a time, so the peak is one working set plus the potentials
    /// already solved - 8 bytes a node a channel. Twice that was not true. The channel loop
    /// kept each finished channel's mask in the record it returned, 105 bytes a node; and once
    /// that was removed, a temporary in the loop still held the previous channel's cut links,
    /// because a method run once executes as first-tier JIT code and first-tier code keeps
    /// every temporary reachable until its slot is reused. The linear ion trap measured 353,
    /// then 344, bytes a node for two channels, against 248 - when the cut links stored every
    /// arm.
    /// </para>
    /// <para>
    /// Asserted as the difference between a two-channel build and the same geometry with its
    /// drive removed, at one mesh: everything else is the same build, so what is left is what
    /// the second channel costs.
    /// </para>
    /// <para>
    /// <b>At 275 thousand nodes, after a warm-up build of each kind.</b> With the cut links
    /// stored over their cut arms a kept mask is 11 bytes a node rather than 105, and at the
    /// 36 thousand nodes this ran at, that is 400 kB - the size of what a first build allocates
    /// for itself and charges to whichever measurement comes first. Restoring the kept mask read
    /// 11.4 there, under the bound, because the one-channel reference had read 8 high.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondChannelCostsAPotentialNotAMask()
    {
        const double cell = 0.00025;

        var one = Plates(cell);
        var two = TwoChannelPlates(cell);

        var planOne = GeometryBuilder3D.Memory(one);
        var planTwo = GeometryBuilder3D.Memory(two);

        Assert.Equal(1, planOne.Channels);
        Assert.Equal(2, planTwo.Channels);

        var peakOne = builds.Optimized(output, "slabs", cell).Peak;
        var peakTwo = builds.Optimized(output, "driven-slabs", cell).Peak;

        // The most the second channel can have cost, and the least.
        var extra = (peakTwo.Bound - peakOne.Least) / planOne.Nodes;
        var least = (peakTwo.Least - peakOne.Bound) / planOne.Nodes;

        output.WriteLine(
            $"{planOne.Nodes:N0} nodes: one channel {Describe(peakOne, planOne.Nodes)}; two {Describe(peakTwo, planOne.Nodes)}; "
            + $"the second costs {least:F2} to {extra:F2} "
            + $"(predicted {(planTwo.PeakBytes - planOne.PeakBytes) / planOne.Nodes:F1})");

        // A kept mask is the conductor mask's 9 bytes a node and its cut links' 2 - an index
        // of 1.5 and these slabs' cut arms - so 11 on top of the potential's 8, and the bound
        // sits between. It was 30 while a kept mask was 105. Kept cut links alone are 2 now,
        // inside this measurement's noise: the defect that held them is defused by the store
        // shrinking rather than caught here.
        Assert.True(least >= 0.0, $"a second channel cost {least:F2} B/node at least: less than nothing");
        Assert.True(extra <= 14.0, $"a second channel cost up to {extra:F2} B/node: more than its potential");
    }

    /// <summary>
    /// A thin volume is charged its first coarse level at the size it really is, not an eighth.
    /// </summary>
    /// <remarks>
    /// Coarsening halves interval counts, not node counts, so each axis keeps one node more than
    /// half: 32769 by 5 by 5 coarsens to 16385 by 3 by 3, which is 0.18 of the fine grid. At
    /// twenty-seven coefficients a node that is 38.9 bytes a fine node where an eighth is 27.
    /// The expectation is worked out here from the node counts, not asked of the model.
    /// </remarks>
    [Fact]
    public void AThinVolumeIsChargedItsRealFirstCoarseLevel()
    {
        // 32.768 m by 4 mm by 4 mm at 1 mm: 32768 intervals by 4 by 4. A box filling the cross
        // section is 2 mm in half-width, so it is still resolved at the first coarse level and
        // the bottom - 147,465 nodes - leaves the Galerkin hierarchy as the cheaper one.
        var thin = new Geometry3D(
            0.0, 0.0, 0.0, 32.768, 0.004, 0.004, 0.001,
            [Box("rail", 0.0, 0.0, 0.0, 0.010, 0.004, 0.004, 100.0)]);

        var plan = GeometryBuilder3D.Memory(thin);

        Assert.Equal(32769L * 5 * 5, plan.Nodes);
        Assert.True(plan.Galerkin, "a 147,465-node bottom is above the rediscretized limit");

        const double firstLevel = (16385.0 * 3 * 3) / (32769.0 * 5 * 5);
        // The mask 9; the cut links' index 1.5 and a quarter of a 16-byte cut arm billed; the
        // fields 24, the stored stencil 64 and the forming scratch 20.
        const double expected = 9 + 1.5 + (0.25 * 16) + 24 + 64 + 20 + (27.0 * 8 * firstLevel);

        output.WriteLine(
            $"first level {firstLevel:F4} of the fine grid: {plan.PeakBytes / plan.Nodes:F2} B/node, "
            + $"against {SolveMemoryModel.VolumeGalerkinPeak:F2} for an eighth");

        Assert.Equal(expected, plan.PeakBytes / plan.Nodes, 9);
        Assert.InRange(expected - SolveMemoryModel.VolumeGalerkinPeak, 11.8, 11.9);
    }

    /// <summary>
    /// The statement picks the hierarchy the solver picks, which is what makes its figure the
    /// right one of the two.
    /// </summary>
    [Fact]
    public void TheStatementAndTheSolverChooseTheSameHierarchy()
    {
        foreach (var (name, geometry) in new[]
        {
            ("slabs", Plates(0.0005)),
            ("sphere", Sphere(0.000625)),
            ("two-channel slabs", TwoChannelPlates(0.0005)),
        })
        {
            var plan = GeometryBuilder3D.Memory(geometry);
            var (_, report) = GeometryBuilder3D.BuildField(geometry);

            output.WriteLine($"{name}: statement {(plan.Galerkin ? "galerkin" : "rediscretized")}, solve {(report.Galerkin ? "galerkin" : "rediscretized")}");

            Assert.Equal(report.Galerkin, plan.Galerkin);
        }
    }

    /// <summary>The plane statement against a plane build.</summary>
    /// <remarks>
    /// <para>
    /// <b>A slope between two builds, as the volume tests are</b>, so that what a build holds
    /// whatever its size cancels. It was one build per node, because the plane's peak - the
    /// instant the first coarse level holds two copies of its cut links, a fraction of a
    /// millisecond - was something a sampling thread caught in one build and missed in the
    /// other, and a slope turned that into an error either way.
    /// </para>
    /// <para>
    /// The lower edge was 0.85, for a sampler that saw only the fine level on a loaded runner.
    /// Read at the allocation the slope is 1.001 of the statement's, in Debug and Release, and
    /// the edge is 0.98.
    /// </para>
    /// </remarks>
    [Fact]
    public void APlaneBuildHoldsWhatItsStatementSays()
    {
        var planSmall = GeometryBuilder.Memory(Rods(0.0001));
        var planLarge = GeometryBuilder.Memory(Rods(0.00005));

        var peakSmall = builds.Optimized(output, "rods", 0.0001).Peak;
        var peakLarge = builds.Optimized(output, "rods", 0.00005).Peak;

        var nodes = (double)(planLarge.Nodes - planSmall.Nodes);

        var slope = new SlopeReading(
            (planLarge.PeakBytes - planSmall.PeakBytes) / nodes,
            (peakLarge.Least - peakSmall.Bound) / nodes,
            (peakLarge.Bound - peakSmall.Least) / nodes,
            false,
            0);

        output.WriteLine(
            $"plane {planSmall.Nodes:N0} -> {planLarge.Nodes:N0} nodes: predicted {slope.Predicted:F2} B/node, "
            + $"measured {slope.Lower:F2} to {slope.Upper:F2}");
        output.WriteLine(
            $"  smaller: {Describe(peakSmall, planSmall.Nodes)} against {planSmall.PeakBytes / planSmall.Nodes:F2}; "
            + $"larger: {Describe(peakLarge, planLarge.Nodes)} against {planLarge.PeakBytes / planLarge.Nodes:F2}");

        // 135.4 in every tier of the JIT since the V-cycle's descent has frames of its own; 143.4
        // while first-tier code kept the fine residual, which the upper edge fails.
        slope.AssertWithin(0.98, 1.02);
    }

    /// <summary>A second plane channel costs one kept potential, not a second geometry.</summary>
    /// <remarks>
    /// The driven plane build kept its first channel's mask for the shared-face check after
    /// the channel loop: 73 bytes a node, through every later channel's solve, which the
    /// statement does not count. Asserted as the volume case is, as the difference between a
    /// two-channel build and the same geometry undriven.
    /// </remarks>
    [Fact]
    public void ASecondPlaneChannelCostsAPotentialNotAMask()
    {
        // 66 k nodes: a kept mask is still 73 bytes a node against a limit of 30.
        const double cell = 0.0001;

        var one = Rods(cell);
        var two = Rods(cell, driven: true);

        var planOne = GeometryBuilder.Memory(one);
        var planTwo = GeometryBuilder.Memory(two);

        Assert.Equal(1, planOne.Channels);
        Assert.Equal(2, planTwo.Channels);

        var peakOne = builds.Optimized(output, "rods", cell).Peak;
        var peakTwo = builds.Optimized(output, "driven-rods", cell).Peak;

        var extra = (peakTwo.Bound - peakOne.Least) / planOne.Nodes;
        var least = (peakTwo.Least - peakOne.Bound) / planOne.Nodes;

        output.WriteLine(
            $"{planOne.Nodes:N0} nodes: one channel {Describe(peakOne, planOne.Nodes)}; two {Describe(peakTwo, planOne.Nodes)}; "
            + $"the second costs {least:F2} to {extra:F2} "
            + $"(predicted {(planTwo.PeakBytes - planOne.PeakBytes) / planOne.Nodes:F1})");

        // A kept mask is 73. The lower edge was -10, for a sampler catching the plane's transient
        // in one build and missing it in the other; read at the allocation a second channel
        // costs 7.3 to 8.0, and less than nothing is not a cost.
        Assert.True(least >= 0.0, $"a second channel cost {least:F2} B/node at least: less than nothing");
        Assert.True(extra <= 30.0, $"a second channel cost up to {extra:F2} B/node: more than its potential");
    }

    /// <summary>
    /// Four round rods in a 16 mm box; driven, the y pair carries an RF tap as well, which makes
    /// a second spatial pattern.
    /// </summary>
    internal static CompiledSolvedField Rods(double cell, bool driven = false)
    {
        var tap = driven ? [new CompiledTap(0, 50.0, 0.0)] : Array.Empty<CompiledTap>();

        return new()
        {
            MinX = -0.008,
            MaxX = 0.008,
            MinY = -0.008,
            MaxY = 0.008,
            CellSize = cell,
            Tolerance = 1e-10,
            Electrodes =
            [
                Disc("xPlus", 0.0045, 0.0, 100.0),
                Disc("xMinus", -0.0045, 0.0, 100.0),
                Disc("yPlus", 0.0, 0.0045, -100.0) with { Taps = tap },
                Disc("yMinus", 0.0, -0.0045, -100.0) with { Taps = tap },
            ],
            Drives = driven ? [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)] : [],
        };
    }

    private SlopeReading Slope(string kind, Func<double, Geometry3D> geometry, double coarse, double fine)
    {
        var small = geometry(coarse);
        var large = geometry(fine);

        var planSmall = GeometryBuilder3D.Memory(small);
        var planLarge = GeometryBuilder3D.Memory(large);

        // The statement bills a quarter of a cut arm a node for every geometry, since it does
        // not rasterize; these geometries have a hundredth. What a build holds is the arithmetic
        // over its arrays with the cut arms it really has, so that is what the slope is held
        // to, and the allowance over the real count is CutArmAllowanceTests' claim.
        var armsSmall = WithRealCutArms(small, planSmall);
        var armsLarge = WithRealCutArms(large, planLarge);

        var peakSmall = builds.Optimized(output, kind, coarse).Peak;
        var peakLarge = builds.Optimized(output, kind, fine).Peak;

        var nodes = (double)(planLarge.Nodes - planSmall.Nodes);
        var billed = (planLarge.PeakBytes - planSmall.PeakBytes) / nodes;

        var reading = new SlopeReading(
            (armsLarge - armsSmall) / nodes,
            (peakLarge.Least - peakSmall.Bound) / nodes,
            (peakLarge.Bound - peakSmall.Least) / nodes,
            planLarge.Galerkin,
            0);

        output.WriteLine(
            $"{planSmall.Nodes:N0} -> {planLarge.Nodes:N0} nodes, {(planLarge.Galerkin ? "galerkin" : "rediscretized")}: "
            + $"billed {billed:F2} B/node, predicted with the real cut arms {reading.Predicted:F2}, "
            + $"measured {reading.Lower:F2} to {reading.Upper:F2}");
        output.WriteLine(
            $"  smaller: {Describe(peakSmall, planSmall.Nodes)} against {armsSmall / planSmall.Nodes:F2}; "
            + $"larger: {Describe(peakLarge, planLarge.Nodes)} against {armsLarge / planLarge.Nodes:F2}");

        Assert.Equal(planSmall.Galerkin, planLarge.Galerkin);

        return reading;
    }

    /// <summary>One build's reading in bytes a node, with what bounds it.</summary>
    private static string Describe(LivePeak peak, long nodes) =>
        $"{peak.Least / nodes:F2} B/node held, {peak.Bound / nodes:F2} at most "
        + $"({peak.Readings} readings, {peak.Unreported:N0} B unreported between two, "
        + $"{peak.Foreign:N0} B from other threads and {peak.ForeignBurst:N0} at once, {peak.Retaken} retaken)";

    /// <summary>
    /// A slope between two builds, as the interval the two measurements leave it in, against
    /// the statement's.
    /// </summary>
    /// <param name="Predicted">The statement's slope, bytes a node.</param>
    /// <param name="Lower">The least the slope can be: the larger held, less the smaller's bound.</param>
    /// <param name="Upper">The most it can be: the larger's bound, less what the smaller held.</param>
    /// <param name="Galerkin">Whether the builds took the Galerkin hierarchy.</param>
    /// <param name="Levels">How many coarse levels a forced rediscretized solve descended.</param>
    private sealed record SlopeReading(double Predicted, double Lower, double Upper, bool Galerkin, int Levels)
    {
        /// <summary>Asserts the slope lies within these fractions of the statement's.</summary>
        public void AssertWithin(double least, double most)
        {
            // The upper edge first: an allocation with no report widens the interval both ways,
            // and it is the upper edge's message that says so.
            Assert.True(
                Upper <= most * Predicted,
                $"the slope is at most {Upper:F2} B/node, {Upper / Predicted:F4} of the statement's {Predicted:F2}: something held is not billed, or is allocated without a report");
            Assert.True(
                Lower >= least * Predicted,
                $"the slope is at least {Lower:F2} B/node, {Lower / Predicted:F4} of the statement's {Predicted:F2}: the statement bills what is not held");
        }
    }

    /// <summary>
    /// A rediscretized solve, forced, at two sizes of one geometry, against the statement for
    /// the levels the solver says it descends through.
    /// </summary>
    /// <remarks>
    /// Fails if a solve stops before it has run the cycles asked for - by converging, or by
    /// stalling - since what the statement says is held at its most is held in a particular
    /// cycle, and a solve that never reached it would be measured against the wrong figure.
    /// </remarks>
    private SlopeReading DirectSlope(string kind, double small, double large)
    {
        var direct = MemoryCases.Direct[kind];

        (double Stated, double Real, long Nodes, int Levels) Plan(Geometry3D g)
        {
            var grid = GeometryBuilder3D.BuildGrid(g);
            var mask = GeometryBuilder3D.BuildMask(g, grid);
            var levels = PoissonSolver3D.RediscretizedLevels(grid, mask.SmallestFeature, mask.InteriorFixedCount > 0);
            var stated = SolveMemoryModel.VolumeRediscretizedPeakOn(grid, levels) * grid.NodeCount;
            var real = stated - (SolveMemoryModel.VolumeCutLinks * grid.NodeCount) + (mask.Cuts?.Bytes ?? 0);

            return (stated, real, grid.NodeCount, levels.Count);
        }

        var planSmall = Plan(direct.Geometry(small));
        var planLarge = Plan(direct.Geometry(large));

        MeasuredBuild[] measured = [builds.Optimized(output, kind, small), builds.Optimized(output, kind, large)];
        var peakSmall = measured[0].Peak;
        var peakLarge = measured[1].Peak;

        var nodes = (double)(planLarge.Nodes - planSmall.Nodes);
        var billed = (planLarge.Stated - planSmall.Stated) / nodes;

        var reading = new SlopeReading(
            (planLarge.Real - planSmall.Real) / nodes,
            (peakLarge.Least - peakSmall.Bound) / nodes,
            (peakLarge.Bound - peakSmall.Least) / nodes,
            false,
            planLarge.Levels);

        output.WriteLine(
            $"{planSmall.Nodes:N0} -> {planLarge.Nodes:N0} nodes, {planLarge.Levels} coarse level(s): "
            + $"billed {billed:F2} B/node, predicted with the real cut arms {reading.Predicted:F2}, "
            + $"measured {reading.Lower:F2} to {reading.Upper:F2}");
        output.WriteLine(
            $"  smaller: {Describe(peakSmall, planSmall.Nodes)} against {planSmall.Real / planSmall.Nodes:F2}; "
            + $"larger: {Describe(peakLarge, planLarge.Nodes)} against {planLarge.Real / planLarge.Nodes:F2}");

        Assert.Equal(planSmall.Levels, planLarge.Levels);

        foreach (var build in measured)
        {
            // The descent the statement was worked out for, and every cycle asked for: a solve
            // that converged or stalled early never reached the cycle its peak is held in.
            Assert.Equal(planLarge.Levels, build.Levels);
            Assert.Equal(direct.Cycles, build.Cycles);
        }

        return reading;
    }

    /// <summary>A sphere at the centre of a cube, at one cell size.</summary>
    internal static Geometry3D Ball(double length, double radius, double cell) => new(
        -0.5 * length, -0.5 * length, -0.5 * length, 0.5 * length, 0.5 * length, 0.5 * length, cell,
        [
            new CompiledElectrode3D
            {
                Name = "ball",
                Shape = Electrode3DShape.Sphere,
                Radius = radius,
                Potential = 100.0,
            },
        ]);

    /// <summary>
    /// The statement's peak with this geometry's cut arms in place of the billed allowance.
    /// </summary>
    /// <remarks>
    /// A quarter of a cut arm a node is 5.5 bytes a node; a 3 mm sphere at these meshes stores
    /// 1.6 to 1.9, and two slabs less. Over the rediscretized hierarchy's 51 that is 7 percent
    /// of the band, spent before a build is measured.
    /// </remarks>
    internal static double WithRealCutArms(Geometry3D geometry, SolveMemory plan)
    {
        var cuts = GeometryBuilder3D.BuildMask(geometry, GeometryBuilder3D.BuildGrid(geometry)).Cuts;

        return plan.PeakBytes - (SolveMemoryModel.VolumeCutLinks * plan.Nodes) + (cuts?.Bytes ?? 0);
    }

    /// <summary>Two 1 mm slabs across a 5 mm gap: the geometry the Galerkin hierarchy exists for.</summary>
    internal static Geometry3D Plates(double cell) => new(
        -0.006, -0.006, -0.005, 0.006, 0.006, 0.005, cell,
        [
            Box("lower", -0.004, -0.004, -0.0035, 0.004, 0.004, -0.0025, 100.0),
            Box("upper", -0.004, -0.004, 0.0025, 0.004, 0.004, 0.0035, 0.0),
        ]);

    /// <summary>The same slabs with the upper one driven, which makes a second spatial pattern.</summary>
    internal static Geometry3D TwoChannelPlates(double cell) => new(
        -0.006, -0.006, -0.005, 0.006, 0.006, 0.005, cell,
        [
            Box("lower", -0.004, -0.004, -0.0035, 0.004, 0.004, -0.0025, 100.0),
            Box("upper", -0.004, -0.004, 0.0025, 0.004, 0.004, 0.0035, 0.0) with
            {
                Taps = [new CompiledTap(0, 50.0, 0.0)],
            },
        ])
    {
        Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
    };

    /// <summary>A 3 mm sphere in a 20 mm box: coarsens far enough that the cheap hierarchy wins.</summary>
    internal static Geometry3D Sphere(double cell) => new(
        -0.010, -0.010, -0.010, 0.010, 0.010, 0.010, cell,
        [
            new CompiledElectrode3D
            {
                Name = "ball",
                Shape = Electrode3DShape.Sphere,
                CentreX = 0.0,
                CentreY = 0.0,
                CentreZ = 0.0,
                Radius = 0.003,
                Potential = 100.0,
            },
        ]);

    private static CompiledElectrode3D Box(
        string name, double x0, double y0, double z0, double x1, double y1, double z1, double v) =>
        new()
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            MinX = x0,
            MinY = y0,
            MinZ = z0,
            MaxX = x1,
            MaxY = y1,
            MaxZ = z1,
            Potential = v,
        };

    private static CompiledElectrode Disc(string name, double x, double y, double volts) => new()
    {
        Name = name,
        Shape = ElectrodeShape.Disc,
        CentreX = x,
        CentreY = y,
        Radius = 0.002,
        Potential = volts,
    };
}
