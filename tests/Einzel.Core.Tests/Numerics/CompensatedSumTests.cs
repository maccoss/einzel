using Einzel.Core.Numerics;

namespace Einzel.Core.Tests.Numerics;

public sealed class CompensatedSumTests
{
    [Fact]
    public void RecoversPrecisionNaiveSummationLoses()
    {
        // The shape of the problem in spec section 8: a microsecond-scale flight
        // accumulated from picosecond steps. Naive summation loses low-order bits
        // once the running total dwarfs the increment.
        const int terms = 1_000_000;
        const double increment = 1e-12;

        var naive = 0.0;
        var compensated = default(CompensatedSum);

        for (var i = 0; i < terms; i++)
        {
            naive += increment;
            compensated.Add(increment);
        }

        var exact = terms * increment;
        var naiveError = Math.Abs(naive - exact) / exact;
        var compensatedError = Math.Abs(compensated.Total - exact) / exact;

        Assert.True(compensatedError <= naiveError, "compensation must not be worse than naive summation");
        Assert.True(compensatedError < 1e-15, $"compensated relative error {compensatedError:E3}");
        Assert.NotEqual(0.0, compensated.Compensation);
    }

    [Fact]
    public void HandlesATermLargerThanTheRunningTotal()
    {
        // The case plain Kahan drops: the incoming term dominates the total. It
        // happens on the very first step, and again whenever an analytic drift
        // advance jumps the total.
        var sum = default(CompensatedSum);

        sum.Add(1e-20);
        sum.Add(1.0);
        sum.Add(1e-20);

        Assert.Equal(1.0 + 2e-20, sum.Total, 1e-30);
    }

    [Fact]
    public void CancellingTermsReturnExactlyToZero()
    {
        var sum = default(CompensatedSum);

        sum.Add(1e16);
        sum.Add(1.0);
        sum.Add(-1e16);

        // Naive summation gives 0 here, because the 1.0 is lost against 1e16.
        Assert.Equal(1.0, sum.Total, 1e-9);
    }

    [Fact]
    public void ReadingTheTotalDoesNotDisturbAccumulation()
    {
        var sampled = default(CompensatedSum);
        var unsampled = default(CompensatedSum);

        for (var i = 0; i < 1000; i++)
        {
            sampled.Add(0.1);
            unsampled.Add(0.1);
            _ = sampled.Total;
        }

        Assert.Equal(unsampled.Total, sampled.Total);
    }
}
