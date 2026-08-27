using Einzel.Commands;
using Einzel.Project;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The acceptance suite checking itself.
/// </summary>
/// <remarks>
/// <para>
/// An actual agent cannot run here - it needs a model, a network, and a great deal
/// of time, and it would not give the same answer twice. What can run here is
/// everything that decides whether the measurement is worth anything.
/// </para>
/// <para>
/// Two properties, and they matter in opposite directions. Every task must be
/// <em>doable</em>: its worked solution scores full marks, so a failure in the
/// field is the agent's and not the task's. And every check must
/// <em>discriminate</em>: each plausible wrong answer fails, so a pass means the
/// task was done rather than that a file exists.
/// </para>
/// <para>
/// The second is the one that decays quietly. A check written against one wrong
/// answer often accepts a different one, and nothing about a green suite says so.
/// </para>
/// </remarks>
public sealed class AgentSuiteTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<string> _roots = [];

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private ProjectLayout FreshProject(AgentTask task)
    {
        var root = Path.Combine(Path.GetTempPath(), "einzel-agent", Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        // The same call the CLI verb makes, so a project prepared for a test is the
        // project an agent actually gets.
        return AgentSuite.Prepare(task, root);
    }

    public static TheoryData<string> EveryTask
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var task in AgentSuite.All)
            {
                data.Add(task.Name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryTask))]
    public void EveryTaskIsDoable(string name)
    {
        // A task whose reference solution does not score full marks is broken:
        // either impossible, or checking for something other than what it asks.
        // Either way a failure in the field would be the suite's fault and would
        // be blamed on the agent.
        var task = AgentSuite.Find(name);

        Assert.NotNull(task.Reference);

        var layout = FreshProject(task);
        task.Reference!.Apply(layout);

        var score = AgentSuite.Score(task.Name, layout.Root);

        foreach (var check in score.Checks)
        {
            output.WriteLine($"{(check.Passed ? "ok  " : "FAIL")} {check.Name}: {check.Detail}");
        }

        Assert.True(
            score.Passed,
            $"the reference solution for '{name}' does not pass its own checks, so the task is broken");
    }

    [Theory]
    [MemberData(nameof(EveryTask))]
    public void EveryCheckDiscriminates(string name)
    {
        // The load-bearing half. A check that passes the reference proves nothing
        // on its own - it has to reject the wrong answers too, or it is measuring
        // whether a file exists.
        var task = AgentSuite.Find(name);

        Assert.NotEmpty(task.Distractors);

        foreach (var distractor in task.Distractors)
        {
            var layout = FreshProject(task);
            distractor.Apply(layout);

            var score = AgentSuite.Score(task.Name, layout.Root);
            var failed = score.Checks.Where(c => !c.Passed).Select(c => c.Name).ToArray();

            output.WriteLine($"{distractor.Name}");
            output.WriteLine($"  expected: {distractor.Expectation}");
            output.WriteLine($"  failed:   {(failed.Length == 0 ? "(nothing)" : string.Join("; ", failed))}");

            Assert.False(
                score.Passed,
                $"'{distractor.Name}' passed every check for '{name}', so the task does not discriminate "
                + $"against it. Expected: {distractor.Expectation}");
        }
    }

    [Fact]
    public void BothTracksArePopulated()
    {
        // The spec asks for a separate track measuring whether agents act on
        // warnings, and a suite that is all capability would report a healthy pass
        // rate while measuring none of it.
        var byTrack = AgentSuite.All.GroupBy(t => t.Track).ToDictionary(g => g.Key, g => g.Count());

        foreach (var (track, count) in byTrack.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
            output.WriteLine($"{track}: {count} task(s)");
        }

        Assert.True(byTrack.TryGetValue(AgentTrack.Capability, out var capability) && capability >= 3);
        Assert.True(byTrack.TryGetValue(AgentTrack.Warnings, out var warnings) && warnings >= 2);
    }

    [Fact]
    public void NoPromptNamesTheToolItIsTesting()
    {
        // The task is to find out how, from the schema and the error messages. A
        // prompt naming a verb or a JSON key tests whether an agent can follow
        // instructions, which is not in doubt and is not what this measures.
        string[] giveaways =
        [
            "einzel ", "--json", "figureOfMerit", "halfWidth", "schemaVersion",
            "capPotential", "turningDepth", "rodRatio", "inscribedRadius",
        ];

        foreach (var task in AgentSuite.All)
        {
            foreach (var giveaway in giveaways)
            {
                Assert.False(
                    task.Prompt.Contains(giveaway, StringComparison.OrdinalIgnoreCase),
                    $"the prompt for '{task.Name}' contains '{giveaway}', which hands over part of the answer");
            }
        }
    }

    [Fact]
    public void EveryTaskSaysWhatItDiscriminates()
    {
        // A task nobody can explain the purpose of is a task nobody will maintain,
        // and this suite is a release gate.
        foreach (var task in AgentSuite.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(task.Rationale), $"'{task.Name}' has no rationale");
            Assert.True(task.Rationale.Length > 80, $"'{task.Name}' has a rationale too short to be one");
            Assert.False(string.IsNullOrWhiteSpace(task.Deliverable));
        }
    }
}
