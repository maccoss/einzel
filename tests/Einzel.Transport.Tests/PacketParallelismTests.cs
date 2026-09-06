using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A packet flight may spread its applied-field evaluations across cores. Two properties
/// make that safe to do by default, and both are asserted rather than argued: the answer
/// does not depend on how many threads ran it, and the inner loop allocates nothing.
/// </summary>
public sealed class PacketParallelismTests(ITestOutputHelper output)
{
    private static TrajectoryStopFunction Never => (in PhaseState _) => 1.0;

    /// <summary>
    /// A field whose evaluation is deliberately slow, so the run is worth parallelising
    /// and the arithmetic is still exact and reproducible. The busy loop is what a
    /// tricubic gather over a solved volume costs in practice.
    /// </summary>
    private sealed class SlowUniformField(Vec3 fieldSi, int work) : IElectrostaticField
    {
        public double ResolutionLength => double.PositiveInfinity;

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

        public double PotentialAt(in Vec3 position) => -Vec3.Dot(fieldSi, position);

        public Vec3 ElectricFieldAt(in Vec3 position)
        {
            // Deterministic and unused: it costs time without changing the answer.
            var burn = 0.0;
            for (var i = 1; i <= work; i++)
            {
                burn += 1.0 / i;
            }

            return burn > -1.0 ? fieldSi : fieldSi;
        }
    }

    private static PhaseState[] Cloud(int n, int seed)
    {
        var random = new Random(seed);
        var launch = new PhaseState[n];
        for (var i = 0; i < n; i++)
        {
            launch[i] = new PhaseState(
                new Vec3(
                    (random.NextDouble() - 0.5) * 0.3e-3,
                    (random.NextDouble() - 0.5) * 0.3e-3,
                    (random.NextDouble() - 0.5) * 3.0e-3),
                new Vec3((random.NextDouble() - 0.5) * 10.0, 0.0, 0.0));
        }

        return launch;
    }

    private static CoulombInteraction Push(int n, IonSpecies species) =>
        new(2.0e5, n, species.ChargeSi, species.MassSi,
            CoulombInteraction.SpacingSoftening(0.1e-3, 0.1e-3, 1.0e-3, n));

    /// <summary>
    /// The same flight, serial and parallel, to the last bit. Nothing is summed across
    /// members, so no rounding order can change - and if that ever stops being true, the
    /// symptom would be a result that drifts with the machine's core count, which is the
    /// hardest kind of discrepancy to chase.
    /// </summary>
    [Fact]
    public void SerialAndParallelAgreeToTheBit()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        const int N = 96;
        var launch = Cloud(N, 21);
        var field = new SlowUniformField(new Vec3(0.0, 0.0, 250.0), work: 400);

        PacketResult Fly(PacketParallelism how) => PacketIntegrator.Fly(
            launch, species, field, Push(N, species),
            new IntegrationSettings { MaximumFlightTime = 3e-6, RelativeTolerance = 1e-9, MaximumSteps = 100_000, Members = how },
            Never);

        var serial = Fly(PacketParallelism.Serial);
        var parallel = Fly(PacketParallelism.Parallel);

        Assert.Equal(serial.Steps, parallel.Steps);
        Assert.Equal(serial.MaximumInteractionImbalance, parallel.MaximumInteractionImbalance);

        var moved = 0.0;
        for (var k = 0; k < N; k++)
        {
            var a = serial.Members[k].FinalState;
            var b = parallel.Members[k].FinalState;

            moved = Math.Max(moved, (a.Position - launch[k].Position).Length);

            Assert.Equal(a.Position.X, b.Position.X);
            Assert.Equal(a.Position.Y, b.Position.Y);
            Assert.Equal(a.Position.Z, b.Position.Z);
            Assert.Equal(a.Velocity.X, b.Velocity.X);
            Assert.Equal(a.Velocity.Y, b.Velocity.Y);
            Assert.Equal(a.Velocity.Z, b.Velocity.Z);
            Assert.Equal(serial.Members[k].FlightTimeSeconds, parallel.Members[k].FlightTimeSeconds);
        }

        output.WriteLine(
            $"{N} members, {serial.Steps} steps, {Environment.ProcessorCount} cores: every final state identical to the bit, "
            + $"over a flight that moved them up to {moved * 1e3:F4} mm");

        Assert.True(moved > 1e-6, "the packet must actually have moved, or agreeing proves nothing");
    }

    /// <summary>
    /// CMP-1: the inner loop allocates nothing, so a garbage collection cannot interrupt a
    /// run. Measured as a floor - the cheapest of several runs - because the runtime
    /// charges one-off costs to whichever window they land in.
    /// </summary>
    /// <remarks>
    /// Serial on purpose: the counter is per thread, so a parallel run would hide worker
    /// allocations rather than measure them. What is under test is the buffers, and those
    /// are shared by both paths.
    /// </remarks>
    [Fact]
    public void TheInnerLoopDoesNotAllocatePerStep()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        const int N = 24;
        var launch = Cloud(N, 33);

        long Measure(double flightSeconds, out int steps)
        {
            var best = long.MaxValue;
            var lastSteps = 0;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var result = PacketIntegrator.Fly(
                    launch, species, FieldFreeSpace.Instance, Push(N, species),
                    new IntegrationSettings
                    {
                        MaximumFlightTime = flightSeconds,
                        RelativeTolerance = 1e-9,
                        MaximumStep = 1e-9,
                        MaximumSteps = 100_000,
                        Members = PacketParallelism.Serial,
                    },
                    Never);

                best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
                lastSteps = result.Steps;
            }

            steps = lastSteps;
            return best;
        }

        var shortRun = Measure(2e-8, out var fewSteps);
        var longRun = Measure(1e-6, out var manySteps);

        output.WriteLine($"{fewSteps,6} steps: {shortRun,8} bytes");
        output.WriteLine($"{manySteps,6} steps: {longRun,8} bytes");
        output.WriteLine($"{(longRun - shortRun) / (double)(manySteps - fewSteps):F2} bytes per additional step");

        Assert.True(manySteps > 10 * fewSteps, $"the long run must be much longer: {fewSteps} against {manySteps}");

        // A constant slack, not a per-step slope, and the same 256 bytes the single-ion
        // test allows. A slope permits a real per-step allocation to hide behind it while
        // the test still claims the loop allocates nothing; what is being asserted is that
        // a forty-five-fold longer flight costs the same, and the measured difference is
        // zero rather than small.
        Assert.True(
            longRun <= shortRun + 256,
            $"allocation grew with step count: {shortRun} bytes over {fewSteps} steps, "
            + $"{longRun} bytes over {manySteps} steps");
    }
}
