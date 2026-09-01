using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// An energy sweep solves the field once, not once per member.
/// </summary>
/// <remarks>
/// <para>
/// The deterministic energy sweep behind <c>resolvingPower</c> and <c>transmission</c>
/// called <c>Setup</c> — and so <see cref="FieldAssembly"/> — <b>inside</b> its member
/// loop. An energy offset changes how fast the ion is launched and nothing whatever about
/// the field it flies through, so that was the same solve every time.
/// </para>
/// <para>
/// On the shipped mirror pair it cost <b>249 ms per ion, flat</b>, against a whole solve
/// of about 270 ms — so twenty-one ions took 5.23 s to do one solve's worth of work and
/// twenty-one flights. Hoisting it took the same figure to 1.53 s with every value
/// <b>bit-identical</b>, and the per-ion cost now falls with the ion count as the one
/// fixed solve amortises.
/// </para>
/// <para>
/// It matters most where this project is going. The saving is the solve, so it grows with
/// it: on a volume geometry whose solve is seconds and whose flight is milliseconds it
/// approaches the member count. A 240-evaluation optimiser run at nine members was paying
/// 2,160 solves for 240 distinct fields.
/// </para>
/// </remarks>
public sealed class SweepSolveCostTests(ITestOutputHelper output)
{
    private static CompiledModel Model(string template)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read(template)));
        Assert.True(validation.IsValid, validation.IsValid ? string.Empty : validation.Errors[0].Constraint);
        return validation.Model!;
    }

    /// <summary>Cheapest of several, because the property is a floor.</summary>
    /// <remarks>
    /// The same statistic <c>AllocationDoesNotGrowWithStepCount</c> settled on, for the
    /// same reason: the runtime charges one-off costs to whichever window they fire in,
    /// and what is being measured is the work itself, so the minimum is the right one.
    /// </remarks>
    private static double Cheapest(Action work, int of = 3)
    {
        var best = double.PositiveInfinity;

        for (var k = 0; k < of; k++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            work();
            best = Math.Min(best, clock.Elapsed.TotalSeconds);
        }

        return best;
    }

    /// <summary>Adding an energy member must not cost a field solve.</summary>
    /// <remarks>
    /// <para>
    /// <b>Self-calibrating, which is what makes it a test rather than a threshold.</b> A
    /// bare wall-clock bound is a statement about a machine (SPEC.md Amendment 27); this
    /// compares two things measured on the same machine in the same run — the marginal
    /// cost of one more energy member, against one whole solve of the same model.
    /// </para>
    /// <para>
    /// If the sweep solves per member the marginal member costs a solve <i>plus</i> a
    /// flight and cannot pass at any margin. If it solves once the marginal member is a
    /// flight alone, which here is about a fifth of a solve.
    /// </para>
    /// </remarks>
    [Fact]
    public void AddingAnEnergyMemberDoesNotCostAFieldSolve()
    {
        var model = Model("planar-mirror-pair");

        // Warm the path so the first timed window is not paying for jitting it.
        FiguresOfMerit.Evaluator("resolvingPower", energySpread: 0.03, ions: 3)(model);

        var solve = Cheapest(() => FieldAssembly.Build(model));

        var few = Cheapest(() => FiguresOfMerit.Evaluator("resolvingPower", 0.03, 3)(model));
        var many = Cheapest(() => FiguresOfMerit.Evaluator("resolvingPower", 0.03, 21)(model));

        var marginal = (many - few) / (21 - 3);

        output.WriteLine($"one solve            {solve * 1000,8:F1} ms");
        output.WriteLine($"resolvingPower,  3   {few * 1000,8:F1} ms");
        output.WriteLine($"resolvingPower, 21   {many * 1000,8:F1} ms");
        output.WriteLine($"marginal per member  {marginal * 1000,8:F1} ms "
            + $"({marginal / solve:F2} solves)");

        // Half a solve, which sits between the two states rather than hard against one:
        // measured at 0.18 solves hoisted and 1.16 with the solve put back in the loop, so
        // a factor of about six separates them. Solving per member costs a whole solve per
        // member by construction, so any bound under one catches it.
        Assert.True(
            marginal < 0.5 * solve,
            $"one more energy member cost {marginal * 1000:F1} ms against {solve * 1000:F1} ms "
            + "for a whole solve of the same model, so the sweep is solving per member. An "
            + "energy offset changes the launch speed and nothing about the field: solve "
            + "once, outside the loop");
    }

    /// <summary>Hoisting the solve did not move the answer.</summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted on flight time, not on resolving power, and the reason is the whole
    /// point.</b> A first version pinned resolving power to the values this build produced
    /// before the hoist, to full round-trip precision, and <b>failed on Linux</b>: 122623.78
    /// against 123107.75, four parts in a thousand.
    /// </para>
    /// <para>
    /// That is not a regression and not a platform bug. Resolving power is <c>t / 2dt</c>
    /// where <c>dt</c> is a difference of nearly-equal flight times, so <b>catastrophic
    /// cancellation amplifies a last-bit difference into a per-mille one</b> — and last-bit
    /// differences across platforms are ordinary, since <c>exp</c>, <c>pow</c> and friends are
    /// not bit-specified and the JIT makes its own vectorisation and FMA choices.
    /// </para>
    /// <para>
    /// So the regression guard is on the quantity that does <i>not</i> cancel. A flight time
    /// is an accumulation, not a difference, and agrees across platforms to round-off.
    /// </para>
    /// <para>
    /// <b>What it guards is the refactor, not the hoist</b>, and the distinction is worth
    /// being exact about: solving per member gives the <i>identical</i> answer — that is the
    /// whole point of the hoist being bit-identical — so no value assertion can detect it.
    /// The cost test above is what guards the hoist. This guards what the refactor actually
    /// touched: <c>LaunchAt</c>, extracted so one implementation of the energy-to-speed square
    /// root serves both callers, and the field construction moved outside the loop. A wrong
    /// square root there moves a flight time grossly.
    /// </para>
    /// <para>
    /// The bit-identity of the hoist itself was verified at the time, on one machine, by
    /// running both paths — which is the right way to check it and is not something a
    /// hardcoded constant can preserve. <b>Pinning a number this engine produced once, as a
    /// permanent cross-platform assertion, is the anti-pattern the examples corpus exists to
    /// avoid.</b>
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("planar-mirror-pair", 2.796764383083119e-05)]
    [InlineData("einzel-lens", 8.394351041578162e-06)]
    public void HoistingTheSolveDidNotMoveTheAnswer(string template, double flightSeconds)
    {
        var model = Model(template);

        var flight = FiguresOfMerit.Evaluator("flightTime")(model);

        output.WriteLine($"{template,-20} flight {flight:R} s   expected {flightSeconds:R}");

        Assert.NotNull(flight);

        // An accumulation rather than a difference, so a real bound rather than a band
        // chosen to fit. Loose enough for cross-platform libm and JIT differences at the
        // integrator's own 1e-10 tolerance, and orders tighter than a wrong launch speed:
        // treating the energy offset as a velocity fraction is a factor of two in the
        // linear term, which is what this exists to catch.
        Assert.Equal(flightSeconds, flight!.Value, 1e-7 * Math.Abs(flightSeconds));
    }

    /// <summary>The sweep still forms a peak, and its width is not degenerate.</summary>
    /// <remarks>
    /// The complement to the flight-time guard: that checks the launch and the field, this
    /// checks that the members really do differ from one another. A sweep that collapsed to
    /// one energy would give an infinitely sharp peak and an enormous resolving power, which
    /// is the failure a flight time alone cannot see.
    /// </remarks>
    [Theory]
    [InlineData("planar-mirror-pair", 9, 136915.0)]
    [InlineData("planar-mirror-pair", 21, 123108.0)]
    [InlineData("einzel-lens", 9, 27.3155)]
    public void TheSweepFormsThePeakItFormedBefore(string template, int ions, double expected)
    {
        var r = FiguresOfMerit.Evaluator("resolvingPower", energySpread: 0.03, ions: ions)(Model(template));

        Assert.NotNull(r);

        output.WriteLine(
            $"{template,-20} {ions,3} ions   R = {r!.Value,12:N2}   expected {expected,12:N2}"
            + $"   ({Math.Abs(r.Value - expected) / expected:P3})");

        // One per cent, from the cancellation rather than from taste. Linux and Windows
        // differ by up to 0.4 per cent here for the reason in the class remarks, while a
        // sweep that had stopped varying the energy would report an enormous R - so the
        // band is wide enough to be portable and narrow enough to catch that.
        Assert.Equal(expected, r.Value, 0.01 * expected);
    }
}
