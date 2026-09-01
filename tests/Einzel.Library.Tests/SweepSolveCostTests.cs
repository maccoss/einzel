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

    /// <summary>And it computes exactly what it computed before.</summary>
    /// <remarks>
    /// The values are from the build before the solve was hoisted, to full round-trip
    /// precision. A saving that moved a number would not be a saving, and these are
    /// bit-identical rather than close: the field is the same field and the launch is
    /// computed by the same function.
    /// </remarks>
    [Theory]
    [InlineData("planar-mirror-pair", 3, 90176.37243580694)]
    [InlineData("planar-mirror-pair", 9, 136915.00595510416)]
    [InlineData("planar-mirror-pair", 21, 123107.75087866254)]
    [InlineData("einzel-lens", 9, 27.315480249008885)]
    public void TheSweepIsBitIdenticalToSolvingPerMember(string template, int ions, double expected)
    {
        var r = FiguresOfMerit.Evaluator("resolvingPower", energySpread: 0.03, ions: ions)(Model(template));

        output.WriteLine($"{template} at {ions} ions   {r:R}");

        Assert.Equal(expected, r!.Value);
    }
}
