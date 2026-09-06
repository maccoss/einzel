using System.Diagnostics;
using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>Scratch measurement: where does a pushed packet's time actually go?</summary>
public sealed class BenchScratch(ITestOutputHelper output)
{
    private static TrajectoryStopFunction Never => (in PhaseState _) => 1.0;

    [Fact]
    public void WhereTheTimeGoes()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        foreach (var n in new[] { 60, 240, 1000, 2000 })
        {
            var random = new Random(3);
            var positions = new Vec3[n];
            var active = new bool[n];
            var acc = new Vec3[n];
            for (var i = 0; i < n; i++)
            {
                positions[i] = new Vec3(
                    (random.NextDouble() - 0.5) * 1e-3,
                    (random.NextDouble() - 0.5) * 1e-3,
                    (random.NextDouble() - 0.5) * 10e-3);
                active[i] = true;
            }

            var interaction = new CoulombInteraction(
                1e5, n, species.ChargeSi, species.MassSi,
                CoulombInteraction.SpacingSoftening(0.05e-3, 0.05e-3, 1e-3, n));

            // warm
            for (var w = 0; w < 3; w++)
            {
                Array.Clear(acc);
                interaction.Accumulate(positions, active, acc);
            }

            var reps = Math.Max(3, 2_000_000 / (n * n));
            var sw = Stopwatch.StartNew();
            for (var r = 0; r < reps; r++)
            {
                Array.Clear(acc);
                interaction.Accumulate(positions, active, acc);
            }

            sw.Stop();
            var per = sw.Elapsed.TotalMilliseconds / reps;
            var pairs = n * (n - 1) / 2.0;
            output.WriteLine($"N={n,5}  Accumulate {per,9:F4} ms   {pairs / per / 1e6:F1} Mpair/s   ({reps} reps)");
        }
    }

    [Fact]
    public void FractionOfAPacketFlightInTheInteraction()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        const int N = 240;
        var random = new Random(7);
        var launch = new PhaseState[N];
        for (var i = 0; i < N; i++)
        {
            launch[i] = new PhaseState(
                new Vec3((random.NextDouble() - 0.5) * 0.1e-3, (random.NextDouble() - 0.5) * 0.1e-3, (random.NextDouble() - 0.5) * 2e-3),
                Vec3.Zero);
        }

        var settings = new IntegrationSettings { MaximumFlightTime = 20e-6, RelativeTolerance = 1e-8, MaximumSteps = 2_000_000 };
        var push = new CoulombInteraction(
            1e5, N, species.ChargeSi, species.MassSi,
            CoulombInteraction.SpacingSoftening(0.05e-3, 0.05e-3, 1e-3, N));

        var swFree = Stopwatch.StartNew();
        var free = PacketIntegrator.Fly(launch, species, FieldFreeSpace.Instance, null, settings, Never);
        swFree.Stop();

        var swPush = Stopwatch.StartNew();
        var pushed = PacketIntegrator.Fly(launch, species, FieldFreeSpace.Instance, push, settings, Never);
        swPush.Stop();

        output.WriteLine($"free   {swFree.Elapsed.TotalMilliseconds,9:F1} ms over {free.Steps} steps");
        output.WriteLine($"pushed {swPush.Elapsed.TotalMilliseconds,9:F1} ms over {pushed.Steps} steps");
        output.WriteLine($"the interaction is {(1.0 - (swFree.Elapsed.TotalMilliseconds / swPush.Elapsed.TotalMilliseconds)) * 100:F1}% of the pushed flight (steps differ, so this is indicative)");
        output.WriteLine($"per step: free {swFree.Elapsed.TotalMilliseconds / free.Steps * 1000:F1} us, pushed {swPush.Elapsed.TotalMilliseconds / pushed.Steps * 1000:F1} us");
        output.WriteLine($"vector width: {System.Numerics.Vector<double>.Count} doubles; hardware accelerated: {System.Numerics.Vector.IsHardwareAccelerated}");
    }
}
