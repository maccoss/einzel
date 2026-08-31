using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Whether an integration finished is a different question from whether the instrument
/// performed, and the two used to share an answer.
/// </summary>
/// <remarks>
/// <para>
/// The exit code for <c>einzel run</c> was a list of the outcome names that meant success
/// when the line was written. It had to be widened once for diffusive runs, when a working
/// density evolution reported itself as a convergence failure, and once for sequenced ones.
/// It was still wrong: <b>six of the thirty-seven shipped examples exited with a failure
/// code while behaving exactly as designed</b> — three that end at their declared hold
/// (a Paul trap holding its ion, a thermalisation, an orbital trap measured over forty
/// turns) and three deliberate losses that are the control halves of pairs.
/// </para>
/// <para>
/// The question is whether the ENGINE finished. An ion that strikes an electrode, or that
/// is still held when the flight time elapses, is a result — the transmission, the itemised
/// losses and <c>confined</c> say what became of it. An integrator that underflowed its step
/// floor or exhausted its step budget did not finish, and its trajectory stops part way with
/// no bound on how wrong it is.
/// </para>
/// </remarks>
public sealed class TrajectoryCompletionTests(ITestOutputHelper output)
{
    /// <summary>Each outcome is a completed run or a failure to produce one.</summary>
    [Theory]
    [InlineData(TrajectoryOutcome.StopConditionMet, true)]
    [InlineData(TrajectoryOutcome.MaximumFlightTimeReached, true)]
    [InlineData(TrajectoryOutcome.StruckElectrode, true)]
    [InlineData(TrajectoryOutcome.MaximumStepsExceeded, false)]
    [InlineData(TrajectoryOutcome.StepSizeUnderflow, false)]
    public void EachOutcomeSaysWhetherTheEngineFinished(TrajectoryOutcome outcome, bool expected)
    {
        output.WriteLine($"{outcome,-26} completed {outcome.Completed()}");

        Assert.Equal(expected, outcome.Completed());
    }

    /// <summary>Every outcome there is has an answer, and a new one has to choose.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the test that stops the defect recurring</b>, and it is why the
    /// classification is a switch with a throw rather than a list of the successful cases.
    /// A list silently classifies anything it has not heard of as a failure, which is
    /// exactly how a working diffusive run and then a working sequenced run came to report
    /// themselves as convergence failures.
    /// </para>
    /// <para>
    /// Enumerating the enum rather than restating it: a sixth outcome added tomorrow fails
    /// here, and fails loudly, until somebody decides which kind it is.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryOutcomeIsClassifiedAndANewOneMustChoose()
    {
        var outcomes = Enum.GetValues<TrajectoryOutcome>();

        output.WriteLine($"{outcomes.Length} outcomes");

        foreach (var outcome in outcomes)
        {
            // The throw is the point: an unclassified outcome must not default to either
            // answer, because both defaults are wrong in a way that is hard to see.
            var completed = outcome.Completed();

            output.WriteLine($"  {outcome,-26} {(completed ? "a result" : "a failure to compute")}");
        }

        // A guard on the count, so that adding an outcome and forgetting to decide is
        // caught here rather than by whichever caller happens to run first.
        Assert.Equal(5, outcomes.Length);
    }

    /// <summary>The two kinds are not the same set, which is the whole content.</summary>
    /// <remarks>
    /// Without this, a classification that answered <c>true</c> for everything would pass
    /// every case above that expects <c>true</c> and the exit code would be vacuous — which
    /// is the failure mode a change like this invites, since it starts by making things
    /// stop failing.
    /// </remarks>
    [Fact]
    public void TheClassificationDiscriminates()
    {
        var outcomes = Enum.GetValues<TrajectoryOutcome>();

        Assert.Contains(outcomes, o => o.Completed());
        Assert.Contains(outcomes, o => !o.Completed());
    }
}
