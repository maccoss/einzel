using Einzel.Core.Model;using Einzel.Project;

namespace Einzel.Commands;

/// <summary>Which of the two things the acceptance suite measures a task belongs to.</summary>
public enum AgentTrack
{
    /// <summary>Can an agent do the thing at all, from prose and the CLI.</summary>
    Capability,

    /// <summary>
    /// Does an agent act on a warning rather than reporting past it.
    /// </summary>
    /// <remarks>
    /// Tracked separately because it is a different failure and a much worse one.
    /// A capability failure produces no answer; a warnings failure produces a
    /// confident answer that is wrong, and nothing downstream can tell.
    /// </remarks>
    Warnings,
}

/// <summary>One thing checked about what an agent produced.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Passed">Whether it held.</param>
/// <param name="Detail">What was found.</param>
public sealed record AgentCheck(string Name, bool Passed, string Detail);

/// <summary>How an attempt at one task went.</summary>
public sealed record TaskScore
{
    /// <summary>The task.</summary>
    public required string Task { get; init; }

    /// <summary>Which track it belongs to.</summary>
    public required string Track { get; init; }

    /// <summary>Every check, in a fixed order.</summary>
    public required IReadOnlyList<AgentCheck> Checks { get; init; }

    /// <summary>Whether every check held.</summary>
    public bool Passed => Checks.Count > 0 && Checks.All(c => c.Passed);
}

/// <summary>A scripted prose task, and how to tell whether it was done.</summary>
public sealed record AgentTask
{
    /// <summary>A short name, used to ask for the task and to report on it.</summary>
    public required string Name { get; init; }

    /// <summary>Which track it belongs to.</summary>
    public required AgentTrack Track { get; init; }

    /// <summary>
    /// The prose handed to the agent, and the only description of the task it
    /// gets.
    /// </summary>
    /// <remarks>
    /// Written as if for someone who has never seen this software, because that is
    /// the situation being measured. A prompt that names a CLI verb or a JSON key
    /// is testing whether an agent can follow instructions, which is not in doubt.
    /// </remarks>
    public required string Prompt { get; init; }

    /// <summary>What the agent must leave behind, relative to the project root.</summary>
    public required string Deliverable { get; init; }

    /// <summary>What this task discriminates, and why it is in the suite.</summary>
    public required string Rationale { get; init; }

    /// <summary>Scores an attempt.</summary>
    public required Func<ProjectLayout, IReadOnlyList<AgentCheck>> Score { get; init; }

    /// <summary>Puts the project into the state the agent starts from.</summary>
    public Action<ProjectLayout>? Setup { get; init; }

    /// <summary>
    /// What a correct approach leaves behind, used to prove the task is doable.
    /// </summary>
    /// <remarks>
    /// A task whose reference solution does not score full marks is a broken task
    /// - either impossible, or checked for something other than what it asks. CI
    /// runs every reference and refuses to ship one that fails.
    /// </remarks>
    public AgentSolution? Reference { get; init; }

    /// <summary>
    /// Plausible wrong approaches, each of which must fail scoring.
    /// </summary>
    /// <remarks>
    /// The part that makes the suite a measurement. A check that passes the
    /// reference proves nothing on its own; it has to reject the wrong answers
    /// too, or it is testing that a file exists rather than that the task was
    /// done.
    /// </remarks>
    public IReadOnlyList<AgentSolution> Distractors { get; init; } = [];
}

/// <summary>
/// The agent acceptance suite: scripted prose tasks, and what they measure.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 19: "Scripted prose tasks run against an agent given a project
/// directory, the CLI, and nothing else. Success rate tracked as a release metric,
/// with a separate track measuring whether agents act on warnings, by seeding
/// tasks whose obvious approach is regime-invalid and scoring whether the agent
/// notices." Section 23 lists what it measures and what pass rate gates a release
/// as open, and needing to be settled before Phase 1 ends.
/// </para>
/// <para>
/// The whole platform rests on a claim that is otherwise untested: that an agent
/// can drive this from a folder and a command line, with no tutorials and no
/// window. SIMION has thirty years of forum posts and example files in the
/// training data of every model anyone would use; Einzel has none of that, so it
/// has to be able to explain itself. This is the measurement of whether it does.
/// </para>
/// <para>
/// Two decisions shape the design. Tasks score <em>actions</em>, not self-reports:
/// asking an agent which warnings it saw measures whether it can copy a list.
/// Asking whether it widened the bound and re-ran measures whether it understood.
/// And every task carries a worked solution and several plausible-but-wrong ones,
/// so the suite can check itself - the right answer must pass and the wrong
/// answers must fail, because a check that accepts everything measures nothing.
/// </para>
/// </remarks>
public static class AgentSuite
{
    /// <summary>Every task, ordered by name.</summary>
    public static IReadOnlyList<AgentTask> All =>
        [.. Tasks.OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Prepares a project for a task: what a real user would have, and the task's
    /// own starting files.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="root">Where to prepare it.</param>
    /// <returns>The project layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// One place, called by the CLI verb and by the tests, because it was two and
    /// they drifted: both created the directories and neither wrote AGENTS.md, so
    /// the harness handed an agent less than <c>einzel init</c> gives a real user -
    /// and the suite that exists to notice guidance drifting was the one place
    /// AGENTS.md was never present to drift.
    /// </para>
    /// <para>
    /// Duplicated setup is how a harness stops measuring the thing it is named
    /// after, quietly, while every test it owns keeps passing.
    /// </para>
    /// </remarks>
    public static ProjectLayout Prepare(AgentTask task, string root)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));

        layout.CreateDirectories();
        File.WriteAllText(layout.AgentsFile, AgentsFile.Generate());

        task.Setup?.Invoke(layout);

        return layout;
    }

    /// <summary>Looks a task up by name.</summary>
    /// <param name="name">The task name.</param>
    /// <returns>The task.</returns>
    /// <exception cref="Core.Errors.EinzelException">No task by that name.</exception>
    public static AgentTask Find(string name)
    {
        foreach (var task in Tasks)
        {
            if (string.Equals(task.Name, name, StringComparison.Ordinal))
            {
                return task;
            }
        }

        throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
        {
            Code = Core.Errors.ErrorCodes.SchemaInvalid,
            Path = "/task",
            Constraint = $"there is no acceptance task called '{name}'",
            Suggestion = $"available: {string.Join(", ", All.Select(t => t.Name))}",
        });
    }

    /// <summary>Scores an attempt at one task.</summary>
    /// <param name="name">The task name.</param>
    /// <param name="root">The project directory the agent worked in.</param>
    /// <returns>The scorecard.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">No task by that name.</exception>
    public static TaskScore Score(string name, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var task = Find(name);
        var layout = new ProjectLayout(Path.GetFullPath(root));

        return new TaskScore
        {
            Task = task.Name,
            Track = task.Track.ToString(),
            Checks = task.Score(layout),
        };
    }

    private static IReadOnlyList<AgentTask> Tasks =>
    [
        DriftTube,
        FixTheUnits,
        QuadrupoleFromTemplate,
        WhichDimensionBindsFirst,
        QuoteAResult,
        TheOptimumOnABound,
    ];

    // ---------------------------------------------------------------- capability

    private static AgentTask DriftTube => new()
    {
        Name = "drift-tube",
        Track = AgentTrack.Capability,
        Deliverable = "models/drift-tube.json",
        Rationale =
            "The floor. Nothing here is subtle: no field to solve, no geometry to get wrong, one closed-form "
            + "answer. What it actually tests is whether the format can be discovered at all - units on every "
            + "quantity, the shape of a source and a detector - from the schema and the error messages and "
            + "nothing else. An agent that cannot do this cannot do anything else in the suite.",
        Prompt =
            """
            Build a model of a simple drift tube and save it as models/drift-tube.json.

            A singly charged ion of mass-to-charge 500 is accelerated through 2000 volts
            and then flies in a straight line through a field-free region 300 millimetres
            long, ending at a detector.

            There are no lenses, no mirrors, and no electric field along the flight path -
            the ion is given its energy at the start and coasts.

            When you are done, the model should validate and running it should report a
            flight time.
            """,
        Score = layout =>
        {
            var checks = new List<AgentCheck>();
            var path = Path.Combine(layout.Models, "drift-tube.json");

            if (!Exists(path, checks, "the model was written"))
            {
                return checks;
            }

            if (Compile(path, checks) is not { } model)
            {
                return checks;
            }

            // A 500 Da singly charged ion at 2 kV covers 300 mm in 15.269 us. The
            // number is arithmetic, not a golden file: v = sqrt(2qV/m), t = L/v.
            var flight = FiguresOfMerit.Evaluator("flightTime")(model);

            checks.Add(Close(
                "the flight time matches the closed form",
                flight,
                DriftTubeSeconds,
                0.01,
                "s"));

            return checks;
        },
        Reference = new AgentSolution(
            "the drift tube as specified",
            "passes: 300 mm at 2 kV is 10.798 us",
            layout => AgentFixtures.Write(
                Path.Combine(layout.Models, "drift-tube.json"), AgentFixtures.DriftTube)),
        Distractors =
        [
            new AgentSolution(
                "nothing written",
                "fails: an agent that gave up leaves no file",
                _ => { }),

            new AgentSolution(
                "the potential read as an energy in electronvolts of the wrong size",
                "fails the closed form: the geometry is right and the flight time is not",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Models, "drift-tube.json"),
                    AgentFixtures.DriftTube.Replace(
                        "\"value\": 2000, \"unit\": \"V\"",
                        "\"value\": 2000, \"unit\": \"kV\"",
                        StringComparison.Ordinal))),

            new AgentSolution(
                "the drift length taken as the radius rather than the length",
                "fails the closed form: a plausible misreading of the prose",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Models, "drift-tube.json"),
                    AgentFixtures.DriftTube.Replace(
                        "\"value\": [300, 0, 0]",
                        "\"value\": [150, 0, 0]",
                        StringComparison.Ordinal))),
        ],
    };

    /// <summary>
    /// The closed-form flight time for the drift tube task, in seconds.
    /// </summary>
    /// <remarks>
    /// v = sqrt(2qV/m) then t = L/v. For m/z 500 singly charged through 2000 V
    /// over 300 mm: 27.783 km/s and 10.798 us. Written here rather than measured
    /// from a run, because a task whose expected value came from the engine tests
    /// that the engine has not changed rather than that it is right.
    /// </remarks>
    private const double DriftTubeSeconds = 1.07981e-5;

    private static AgentTask FixTheUnits => new()
    {
        Name = "fix-the-units",
        Track = AgentTrack.Capability,
        Deliverable = "models/broken.json",
        Rationale =
            "Recovery from an error, which is what AGT-3 is for. The suite seeds a model with a quantity in a "
            + "unit of the wrong dimension. Everything needed to fix it is in the error message - the offending "
            + "path, the dimension required, the dimension supplied - and nothing else in the project says it. "
            + "This measures whether errors are recovery instructions or merely complaints.",
        Prompt =
            """
            The file models/broken.json does not validate. Find out why and fix it, leaving
            the model at the same path.

            Change as little as possible: the geometry and the ion are correct as written,
            and only the thing the validator objects to should move.
            """,
        Score = layout =>
        {
            var checks = new List<AgentCheck>();
            var path = Path.Combine(layout.Models, "broken.json");

            if (!Exists(path, checks, "the model is still there"))
            {
                return checks;
            }

            if (Compile(path, checks) is not { } model)
            {
                return checks;
            }

            // The seeded fault is an acceleration potential given in millimetres.
            // Fixing it by deleting the field, or by changing the geometry to suit,
            // would also validate - so the physics is checked too.
            var flight = FiguresOfMerit.Evaluator("flightTime")(model);

            checks.Add(Close(
                "the geometry was left alone",
                flight,
                DriftTubeSeconds,
                0.01,
                "s"));

            return checks;
        },
        Setup = layout => AgentFixtures.Write(
            Path.Combine(layout.Models, "broken.json"), AgentFixtures.BrokenDriftTube),
        Reference = new AgentSolution(
            "the unit corrected to volts",
            "passes: one token changed, the physics untouched",
            layout => AgentFixtures.Write(
                Path.Combine(layout.Models, "broken.json"), AgentFixtures.DriftTube)),
        Distractors =
        [
            new AgentSolution(
                "left as it was found",
                "fails validation: the task was not attempted",
                _ => { }),

            new AgentSolution(
                "made to validate by moving the detector instead",
                "fails the closed form: valid, and a different instrument",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Models, "broken.json"),
                    AgentFixtures.DriftTube.Replace(
                        "\"value\": [300, 0, 0]",
                        "\"value\": [500, 0, 0]",
                        StringComparison.Ordinal))),

            new AgentSolution(
                "made to validate by reading 2000 mm as 2000 V of a different ion",
                "fails the closed form: the fault was fixed and the ion was not left alone",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Models, "broken.json"),
                    AgentFixtures.DriftTube.Replace(
                        "\"value\": 500, \"unit\": \"Da\"",
                        "\"value\": 1000, \"unit\": \"Da\"",
                        StringComparison.Ordinal))),
        ],
    };

    private static AgentTask QuadrupoleFromTemplate => new()
    {
        Name = "quadrupole-from-template",
        Track = AgentTrack.Capability,
        Deliverable = "models/quadrupole.json",
        Rationale =
            "Whether the platform's own catalogue is discoverable. Building a quadrupole from scratch is a "
            + "day's work and reproducing one from a shipped template is a minute, and the difference is "
            + "entirely whether the agent finds out the template exists. It is the closest thing here to the "
            + "thirty years of example files SIMION has and Einzel does not.",
        Prompt =
            """
            Model a quadrupole mass filter cross-section with an inscribed radius of 4
            millimetres and 120 volts on the rods, and save it as models/quadrupole.json.

            The inscribed radius is the distance from the axis to the nearest rod surface.
            You do not need to design the rod geometry yourself.

            The model should validate, and solving it should converge.
            """,
        Score = layout =>
        {
            var checks = new List<AgentCheck>();
            var path = Path.Combine(layout.Models, "quadrupole.json");

            if (!Exists(path, checks, "the model was written"))
            {
                return checks;
            }

            if (Compile(path, checks) is not { } model)
            {
                return checks;
            }

            checks.Add(Near(
                "the inscribed radius is 4 mm",
                Parameter(model, "inscribedRadius"),
                0.004,
                1e-9,
                "m"));

            checks.Add(Near(
                "the rods are at 120 V",
                Parameter(model, "rodPotential"),
                120.0,
                1e-9,
                "V"));

            try
            {
                var solve = SolveCommand.Execute(path);

                checks.Add(new AgentCheck(
                    "the field solves and converges",
                    solve.Converged && solve.Elements.Count > 0,
                    solve.Elements.Count == 0
                        ? "the model has no field to solve, so this is not a quadrupole cross-section"
                        : $"{solve.Elements.Count} element(s), converged {solve.Converged}"));
            }
            catch (Core.Errors.EinzelException failure)
            {
                checks.Add(new AgentCheck("the field solves and converges", false, failure.Error.Constraint));
            }

            return checks;
        },
        Reference = new AgentSolution(
            "the shipped template with its two parameters overridden",
            "passes: the catalogue was found and used",
            layout => AgentFixtures.Write(
                Path.Combine(layout.Models, "quadrupole.json"),
                Library.DeviceTemplates.Read("quadrupole")
                    .Replace("\"value\": 5.0, \"unit\": \"mm\"", "\"value\": 4.0, \"unit\": \"mm\"", StringComparison.Ordinal)
                    .Replace("\"value\": 100.0, \"unit\": \"V\"", "\"value\": 120.0, \"unit\": \"V\"", StringComparison.Ordinal))),
        Distractors =
        [
            new AgentSolution(
                "nothing written",
                "fails: the template was never found",
                _ => { }),

            new AgentSolution(
                "the template copied without changing anything",
                "fails the radius: the catalogue was found and the prompt ignored",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Models, "quadrupole.json"),
                    Library.DeviceTemplates.Read("quadrupole"))),

            new AgentSolution(
                "a drift tube renamed",
                "fails the solve: nothing to solve, so it is not a cross-section",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Models, "quadrupole.json"), AgentFixtures.DriftTube)),
        ],
    };

    private static AgentTask WhichDimensionBindsFirst => new()
    {
        Name = "which-dimension-binds-first",
        Track = AgentTrack.Capability,
        Deliverable = "studies/tolerance.json",
        Rationale =
            "The question the tolerance machinery exists to answer, asked the way an instrument builder asks "
            + "it. Section 13 calls the ranking 'the actual deliverable', and this measures whether an agent "
            + "gets to it - which needs the study format, a figure of merit, and units on a half-width, none "
            + "of which the prompt names.",
        Prompt =
            """
            The reflectron in models/reflectron.json will be machined and wired by hand.
            Two things will be slightly off from the drawing: the depth of the mirror,
            which is good to about a fifth of a millimetre, and the voltage on its back
            plate, which is good to about five volts.

            Work out which of those two matters more for the arrival time of the ion, and
            leave the study you used at studies/tolerance.json.
            """,
        Score = layout =>
        {
            var checks = new List<AgentCheck>();
            var path = Path.Combine(layout.Studies, "tolerance.json");

            if (!Exists(path, checks, "the study was written"))
            {
                return checks;
            }

            try
            {
                var outcome = StudyCommand.Sweep(path, layout);

                checks.Add(new AgentCheck(
                    "the study varies both things named",
                    outcome.Sensitivity.Count >= 2,
                    $"{outcome.Sensitivity.Count} channel(s): "
                    + string.Join(", ", outcome.Sensitivity.Select(s => s.Parameter))));

                // The answer is the mirror depth, by a factor of three. An agent
                // that ran the study and reported the other one has done the work
                // and misread it, which is a different failure worth separating.
                var binding = outcome.Sensitivity.Count > 0 ? outcome.Sensitivity[0].Parameter : "(none)";

                checks.Add(new AgentCheck(
                    "the mirror depth is found to bind first",
                    string.Equals(binding, "turningDepth", StringComparison.Ordinal),
                    $"largest swing: {binding}"));
            }
            catch (Core.Errors.EinzelException failure)
            {
                checks.Add(new AgentCheck("the study runs", false, failure.Error.Constraint));
            }

            return checks;
        },
        Setup = AgentFixtures.SeedReflectron,
        Reference = new AgentSolution(
            "both channels swept, with units",
            "passes: the mirror depth binds first, by a factor of three",
            layout => AgentFixtures.Write(
                Path.Combine(layout.Studies, "tolerance.json"), AgentFixtures.ToleranceStudy)),
        Distractors =
        [
            new AgentSolution(
                "nothing written",
                "fails: no study",
                _ => { }),

            new AgentSolution(
                "only the voltage swept",
                "fails: one channel cannot rank two things",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Studies, "tolerance.json"), AgentFixtures.OneChannelStudy)),

            new AgentSolution(
                "half-widths written without units",
                "fails: refused at the boundary, which is the intended behaviour",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Studies, "tolerance.json"),
                    AgentFixtures.ToleranceStudy.Replace(", \"unit\": \"mm\"", string.Empty, StringComparison.Ordinal)
                        .Replace(", \"unit\": \"V\"", string.Empty, StringComparison.Ordinal))),
        ],
    };

    // ------------------------------------------------------------------ warnings

    private static AgentTask QuoteAResult => new()
    {
        Name = "quote-a-result",
        Track = AgentTrack.Warnings,
        Deliverable = "results/reflectron.manifest.json",
        Rationale =
            "The obvious approach is the wrong one. 'preview' is faster, is mentioned in the help ahead of "
            + "'run', and gives an answer to four figures that looks entirely quotable - while carrying a "
            + "non-suppressible mark saying it is not, because there is no convergence study behind it. "
            + "Scoring the action rather than a self-report: a quotable number means a manifest exists.",
        Prompt =
            """
            Compute the arrival time of the ion in models/reflectron.json.

            This number is going into a paper, so it needs to be one you would defend:
            quoted with its uncertainty, and reproducible by someone else later from what
            the project records.
            """,
        Score = layout =>
        {
            var checks = new List<AgentCheck>();
            var manifest = Path.Combine(layout.Results, "reflectron.manifest.json");

            checks.Add(new AgentCheck(
                "the result is one that can be reproduced",
                File.Exists(manifest),
                File.Exists(manifest)
                    ? "a run manifest was recorded"
                    : "no manifest: nothing here determines how the number was produced, so it cannot be "
                        + "regenerated. A preview leaves no manifest, deliberately"));

            var result = Path.Combine(layout.Results, "reflectron.result.json");

            if (File.Exists(result))
            {
                var text = File.ReadAllText(result);

                checks.Add(new AgentCheck(
                    "the quoted number is not preview tier",
                    !text.Contains("preview-tier", StringComparison.Ordinal),
                    text.Contains("preview-tier", StringComparison.Ordinal)
                        ? "the stored result carries the preview taint"
                        : "no preview taint on the stored result"));
            }

            return checks;
        },
        Setup = AgentFixtures.SeedReflectron,
        Reference = new AgentSolution(
            "run, which records a manifest",
            "passes: the number can be regenerated by someone else later",
            layout => RunCommand.Execute(
                Path.Combine(layout.Models, "reflectron.json"),
                layout,
                exportVtu: false,
                timestampUtc: DateTimeOffset.UnixEpoch)),
        Distractors =
        [
            new AgentSolution(
                "nothing computed",
                "fails: no result at all",
                _ => { }),

            new AgentSolution(
                "preview, whose number looks quotable and is not",
                "fails: no manifest, so nothing determines how the number was produced",
                layout => PreviewCommand.Execute(Path.Combine(layout.Models, "reflectron.json"))),
        ],
    };

    private static AgentTask TheOptimumOnABound => new()
    {
        Name = "optimum-on-a-bound",
        Track = AgentTrack.Warnings,
        Deliverable = "studies/tune.json",
        Rationale =
            "The seeded trap. The search interval given in the prompt does not contain the optimum, so the "
            + "obvious study returns the edge of its own box - a perfectly good number that means something "
            + "entirely different from 'the best value', and looks identical. The optimiser says so in a "
            + "non-suppressible warning. Acting on it means widening the interval and running again, which is "
            + "observable in the study file that was left behind.",
        Prompt =
            """
            Find the back-plate voltage that gives models/reflectron.json the best
            resolving power across its energy acceptance.

            Try somewhere between 3600 and 3900 volts to begin with. Leave the study at
            studies/tune.json.

            Report the voltage you would actually build to, and say how confident you are
            that it is the best one.
            """,
        Score = layout =>
        {
            var checks = new List<AgentCheck>();
            var path = Path.Combine(layout.Studies, "tune.json");

            if (!Exists(path, checks, "the study was written"))
            {
                return checks;
            }

            try
            {
                var outcome = StudyCommand.Optimise(path, layout);
                var atBound = outcome.Best.Values.Any(m => m.Warnings.Any(
                    w => string.Equals(w.Code, "optimiser.optimum-at-bound", StringComparison.Ordinal)));

                // The whole task. Leaving the interval where the prompt suggested
                // returns 3900 V with a warning saying it is a bound and not an
                // optimum; the agent has to widen it and run again.
                checks.Add(new AgentCheck(
                    "the search interval was widened past the suggestion",
                    !atBound,
                    atBound
                        ? "the optimum sits on a bound, so what came back is where the search was stopped by "
                            + "the interval rather than where the objective turns"
                        : "the optimum is interior to the interval"));

                var best = outcome.Best.TryGetValue("capPotential", out var measured) ? measured : null;

                checks.Add(new AgentCheck(
                    "the voltage found is above the suggested interval",
                    best is not null && best.Value > 3900.0,
                    best is null
                        ? "the study does not search the back-plate voltage"
                        : $"{best.Value:F1} {best.Unit}"));
            }
            catch (Core.Errors.EinzelException failure)
            {
                checks.Add(new AgentCheck("the study runs", false, failure.Error.Constraint));
            }

            return checks;
        },
        Setup = AgentFixtures.SeedReflectron,
        Reference = new AgentSolution(
            "the interval widened after the warning",
            "passes: the optimum is interior, so it is where the objective turns",
            layout => AgentFixtures.Write(
                Path.Combine(layout.Studies, "tune.json"), AgentFixtures.WidenedTuneStudy)),
        Distractors =
        [
            new AgentSolution(
                "nothing written",
                "fails: no study",
                _ => { }),

            new AgentSolution(
                "the suggested interval, reported as found",
                "fails: the answer is the edge of the box, and the optimiser said so",
                layout => AgentFixtures.Write(
                    Path.Combine(layout.Studies, "tune.json"), AgentFixtures.NarrowTuneStudy)),
        ],
    };

    // ------------------------------------------------------------------- helpers

    private static bool Exists(string path, List<AgentCheck> checks, string name)
    {
        var found = File.Exists(path);
        checks.Add(new AgentCheck(name, found, found ? path : $"not found: {path}"));
        return found;
    }

    private static CompiledModel? Compile(string path, List<AgentCheck> checks)
    {
        try
        {
            var validation = ModelValidator.Validate(
                Io.ModelJson.Parse(File.ReadAllText(path)),
                null,
                Path.GetDirectoryName(Path.GetFullPath(path)));

            checks.Add(new AgentCheck(
                "the model validates",
                validation.IsValid,
                validation.IsValid
                    ? "no problems"
                    : $"{validation.Errors.Count} problem(s), first: {validation.Errors[0].Constraint}"));

            return validation.Model;
        }
        catch (Core.Errors.EinzelException failure)
        {
            checks.Add(new AgentCheck("the model validates", false, failure.Error.Constraint));
            return null;
        }
    }

    private static double? Parameter(CompiledModel model, string name) =>
        model.Parameters.Parameters.TryGetValue(name, out var parameter) ? parameter.Value.SiValue : null;

    private static AgentCheck Close(string name, double? observed, double expected, double tolerance, string unit)
    {
        if (observed is not { } value)
        {
            return new AgentCheck(name, false, "nothing arrived at the detector, so there is no value");
        }

        var error = Math.Abs(value - expected) / Math.Abs(expected);

        return new AgentCheck(
            name,
            error <= tolerance,
            $"{value:G6} {unit} against {expected:G6}, off by {error:P2} of {tolerance:P0} allowed");
    }

    private static AgentCheck Near(string name, double? observed, double expected, double tolerance, string unit)
    {
        if (observed is not { } value)
        {
            return new AgentCheck(name, false, "the model does not declare this parameter");
        }

        return new AgentCheck(
            name,
            Math.Abs(value - expected) <= tolerance,
            $"{value:G6} {unit} against {expected:G6}");
    }
}
