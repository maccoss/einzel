using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// What a solve holds when its V-cycle runs as first-tier code, measured in a child process
/// whose methods are never promoted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a child process.</b> First-tier JIT code keeps every local alive to the end of its
/// method, so a V-cycle that holds a level's residual in a local of the frame that recurses
/// holds it through every coarser level - one array of the fine grid more than the statement
/// charges - for exactly as long as the solver has not been promoted. In a test process that
/// is the first build or two, which is why <c>SelfFieldMemoryTests</c> saw it on a slow runner
/// and outvoted it on a fast one, and why no measurement in this process can be relied on to
/// see it: whether the code under test is still first-tier by the time it is measured is the
/// machine's choice. <c>DOTNET_TC_CallCounting=0</c> makes it the test's: nothing is ever
/// counted, so nothing is ever promoted, and every build in the child is first-tier throughout.
/// </para>
/// <para>
/// <b>What it guards.</b> The residual is restricted in a frame of its own
/// (<c>RestrictedResidual</c>) so it is dead before the recursion starts, in both solvers. Put
/// it back inline and the plane reads 143.4 bytes a node here against a statement of 135.25,
/// and the sphere 6 percent over its own - on every machine, since the tier is pinned. A new
/// array added to the cycle and kept across the recursion fails the same way, which a
/// structural check of the source could not promise.
/// </para>
/// <para>
/// <b>Small builds, because first-tier code is slow</b>: a sixty-six-thousand-node plane and a
/// thirty-six-thousand-node sphere, each built once unmeasured and then read from inside the
/// build (<see cref="LiveSet"/>), take a few seconds in all. A warm-up cannot promote anything
/// here, and the child checks that it has not: it tests its tier before the first build and
/// after the last (<see cref="JitTier"/>). The upper edge is the one that matters, as in <see cref="SolveMemoryTests"/>; it is 1.03
/// rather than 1.02 because a single build at these sizes carries the build's fixed allocations
/// per node where a slope would cancel them, and the hold it exists to catch is 1.06.
/// </para>
/// </remarks>
[Collection(nameof(MemoryMeasurement))]
public sealed class FirstTierMemoryTests(MeasuredBuilds builds, ITestOutputHelper output)
{
    [Fact]
    public void AFirstTierSolveHoldsWhatItsStatementSays()
    {
        var plane = SolveMemoryTests.Rods(0.0001);
        var planePlan = GeometryBuilder.Memory(plane);

        Check("plane", builds.FirstTier(output, "rods", 0.0001).Peak, planePlan.Nodes, planePlan.PeakBytes);

        var sphere = SolveMemoryTests.Sphere(0.000625);
        var spherePlan = GeometryBuilder3D.Memory(sphere);

        Check(
            "volume",
            builds.FirstTier(output, "sphere", 0.000625).Peak,
            spherePlan.Nodes,
            SolveMemoryTests.WithRealCutArms(sphere, spherePlan));
    }

    private void Check(string name, LivePeak peak, long nodes, double stated)
    {
        output.WriteLine($"{name} {nodes:N0} nodes, stated {stated / nodes:F2}: {SolveMemoryTests.Describe(peak, nodes)}");

        MemoryVerdict.Covered($"the first-tier {name} build", peak);

        // 1.03 rather than 1.02: a single build at these sizes carries its fixed allocations per
        // node, where a slope would cancel them. The hold this exists to catch reads 1.06.
        MemoryVerdict.Within(
            $"the first-tier {name} build",
            peak.Least / nodes,
            peak.Bound / nodes,
            0.85 * stated / nodes,
            1.03 * stated / nodes,
            nodes,
            peak.Foreign);
    }
}
