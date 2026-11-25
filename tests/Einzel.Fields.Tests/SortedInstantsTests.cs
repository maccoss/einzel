using Einzel.Core.Geometry;
using Einzel.Fields.Analytic;

namespace Einzel.Fields.Tests;

/// <summary>
/// The bisection every switch lookup shares answers exactly as the front-to-back scans it replaced.
/// </summary>
public sealed class SortedInstantsTests
{
    /// <summary>
    /// A sequence's state and next switch, on every boundary, an ulp either side and between, and
    /// at NaN, are what a scan for the first boundary the instant falls short of gives.
    /// </summary>
    [Fact]
    public void ASequenceAnswersAsTheScanDid()
    {
        var random = new Random(20261124);
        var ends = new double[40];
        var at = 0.0;

        for (var k = 0; k < ends.Length; k++)
        {
            at += Math.Exp(random.NextDouble() * Math.Log(1e3)) * 1e-9;
            ends[k] = at;
        }

        var field = new SequencedField(
            ends.Select((_, k) => UniformField.Create(new Vec3(k + 1.0, 0.0, 0.0))).ToArray(), ends);

        var instants = new List<double> { 0.0, -1.0, at * 2.0, double.NaN };

        foreach (var end in ends)
        {
            instants.AddRange([end, Math.BitIncrement(end), Math.BitDecrement(end), end * random.NextDouble()]);
        }

        foreach (var t in instants)
        {
            Assert.Equal(Scanned(ends, t, before: false) + 1.0, field.ElectricFieldAt(Vec3.Zero, t).X);
            Assert.Equal(Scanned(ends, t, before: true) + 1.0, field.ElectricFieldBefore(Vec3.Zero, t).X);

            var next = ends.FirstOrDefault(b => b > t, double.PositiveInfinity);
            Assert.Equal(next, field.NextSwitchAfter(t));
        }
    }

    /// <summary>Both searches against a scan over random increasing instants.</summary>
    [Fact]
    public void BothSearchesAreTheScans()
    {
        var random = new Random(7);

        for (var draw = 0; draw < 200; draw++)
        {
            var count = random.Next(0, 20);
            var values = Enumerable.Range(0, count).Select(_ => random.NextDouble()).Order().Distinct().ToArray();
            var probes = values.Concat([0.0, 1.0, double.NaN, random.NextDouble()]).ToArray();

            foreach (var t in probes)
            {
                Assert.Equal(Index(values, v => v > t), SortedInstants.FirstAfter(values, t));
                Assert.Equal(Index(values, v => v >= t), SortedInstants.FirstAtOrAfter(values, t));
            }
        }
    }

    private static int Index(double[] values, Func<double, bool> passes)
    {
        for (var k = 0; k < values.Length; k++)
        {
            if (passes(values[k]))
            {
                return k;
            }
        }

        return values.Length;
    }

    // The loop SequencedField.At ran: the first boundary the instant falls short of, or the last state.
    private static int Scanned(double[] ends, double t, bool before)
    {
        for (var i = 0; i < ends.Length; i++)
        {
            if (t < ends[i] || (before && t == ends[i]))
            {
                return i;
            }
        }

        return ends.Length - 1;
    }
}
