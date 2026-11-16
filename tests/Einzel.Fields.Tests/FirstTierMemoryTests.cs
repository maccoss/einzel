using System.Diagnostics;
using System.Globalization;

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
/// thirty-six-thousand-node sphere, each read once from inside the build
/// (<see cref="LiveSet"/>), take a few seconds in all. The upper edge is the one that matters, as in <see cref="SolveMemoryTests"/>; it is 1.03
/// rather than 1.02 because a single build at these sizes carries the build's fixed allocations
/// per node where a slope would cancel them, and the hold it exists to catch is 1.06.
/// </para>
/// </remarks>
[Collection(nameof(MemoryMeasurement))]
public sealed class FirstTierMemoryTests(ITestOutputHelper output)
{
    /// <summary>The argument that makes the test assembly's entry point measure rather than exit.</summary>
    internal const string Command = "first-tier-solve-memory";

    [Fact]
    public void AFirstTierSolveHoldsWhatItsStatementSays()
    {
        var lines = MeasuringChild.Run(output, [Command], JitTier.First);

        // Each line ends "... held <ratio> bound <ratio>": what the build held, and the most it
        // can have held, each over its statement.
        var ratios = lines
            .Select(line => line.Split(' '))
            .ToDictionary(
                parts => parts[0],
                parts => (
                    Held: double.Parse(parts[^3], CultureInfo.InvariantCulture),
                    Bound: double.Parse(parts[^1], CultureInfo.InvariantCulture)));

        Assert.Equal(["plane", "volume"], ratios.Keys.Order());

        foreach (var (name, ratio) in ratios)
        {
            Assert.True(
                ratio.Held >= 0.85,
                $"the {name} build held {ratio.Held:F3} of its statement as first-tier code: the statement bills what is not held");
            Assert.True(
                ratio.Bound <= 1.03,
                $"the {name} build held up to {ratio.Bound:F3} of its statement as first-tier code: an array the cycle "
                + "allocates is held across the recursion by a frame that outlives its last use");
        }
    }

    /// <summary>The child's work: one plane and one volume build, each against its statement.</summary>
    /// <returns>The process exit code.</returns>
    internal static int Measure()
    {
        // Not a test of the tier unless the tier is pinned, and pinned is what the code does,
        // not what a variable says: refuse rather than pass vacuously.
        if (Environment.GetEnvironmentVariable("DOTNET_TC_CallCounting") != "0")
        {
            Console.Error.WriteLine("DOTNET_TC_CallCounting must be 0, so that nothing is promoted");
            return 2;
        }

        if (JitTier.Refusal(JitTier.First) is { } refusal)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }

        var plane = SolveMemoryTests.Rods(0.0001);
        var planePlan = GeometryBuilder.Memory(plane);
        var planePeak = LiveSet.Peak(() => GeometryBuilder.Build(plane).Field);

        Report("plane", planePlan.Nodes, planePeak, planePlan.PeakBytes);

        var sphere = SolveMemoryTests.Sphere(0.000625);
        var spherePlan = GeometryBuilder3D.Memory(sphere);
        var sphereStated = SolveMemoryTests.WithRealCutArms(sphere, spherePlan);
        var spherePeak = LiveSet.Peak(() => GeometryBuilder3D.BuildField(sphere).Field);

        Report("volume", spherePlan.Nodes, spherePeak, sphereStated);

        return 0;

        static void Report(string name, long nodes, LivePeak peak, double stated) =>
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name} {nodes} nodes: {peak.Least / nodes:F2} B/node held and {peak.Bound / nodes:F2} at most in {peak.Readings} readings, "
                + $"{peak.Foreign} B from other threads, {peak.Retaken} retaken, stated {stated / nodes:F2}, "
                + $"held {peak.Least / stated:F4} bound {peak.Bound / stated:F4}"));
    }

}

/// <summary>
/// The test assembly's entry point, which does nothing unless asked to be the first-tier child
/// of <see cref="FirstTierMemoryTests"/>. The test host loads this assembly as a library and
/// never calls it.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => args switch
    {
        [FirstTierMemoryTests.Command] => FirstTierMemoryTests.Measure(),
        [MemoryCases.Command, .. var cases] => MemoryCases.Child(cases),
        _ => 0,
    };
}
