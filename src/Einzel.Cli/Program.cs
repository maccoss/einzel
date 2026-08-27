using System.Globalization;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Results;
using Einzel.Project;
using Einzel.Render;

namespace Einzel.Cli;

/// <summary>The CLI entry point.</summary>
/// <remarks>
/// <para>
/// Spec section 15 makes this the primary surface, and section 6 makes it a peer
/// of the MCP server and the shell rather than a layer under them. Everything
/// here is argument parsing and formatting; the work happens in Einzel.Commands,
/// which the other two surfaces call identically (AGT-2).
/// </para>
/// <para>
/// Arguments are parsed by hand rather than with a library. The surface is small,
/// and PERF-8 budgets 500 ms from cold start to first output with no network call
/// in that path — a budget worth not spending on a parser's reflection.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Runs the CLI.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A process exit code (CLI-3).</returns>
    public static int Main(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (EinzelException failure)
        {
            Console.Error.WriteLine(failure.Error.ToString());
            return (int)ExitCode.ValidationFailure;
        }
        catch (FileNotFoundException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.ValidationFailure;
        }
        catch (DirectoryNotFoundException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.ValidationFailure;
        }
        catch (IOException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.InternalError;
        }
        catch (UnauthorizedAccessException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.InternalError;
        }
#pragma warning disable CA1031 // The process boundary is exactly where a catch-all belongs.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // A defect in the platform, which AGT-3 classes as always a bug
            // report. The type and the stack are what makes it reportable, so they
            // are printed rather than swallowed - but on stderr, and behind a
            // distinct exit code, so a caller can tell an engine defect from a bad
            // model without parsing anything.
            Console.Error.WriteLine($"{ErrorCodes.InternalError}: {failure.GetType().Name}: {failure.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("This is a defect in einzel, not in your model. Please report it with the");
            Console.Error.WriteLine($"command you ran and this trace, against engine {EngineBuild.Version}:");
            Console.Error.WriteLine();
            Console.Error.WriteLine(failure.StackTrace);
            return (int)ExitCode.InternalError;
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return (int)ExitCode.Success;
        }

        if (args[0] is "--version")
        {
            Console.Out.WriteLine(EngineBuild.Version);
            return (int)ExitCode.Success;
        }

        var options = CommandLine.Parse(args);

        return args[0] switch
        {
            "init" => Init(options),
            "new" => New(options),
            "validate" => Validate(options),
            "estimate" => Estimate(options),
            "solve" => Solve(options),
            "run" => Run(options),
            "sweep" => Sweep(options),
            "optimise" or "optimize" => Optimise(options),
            "preview" => Preview(options),
            "test" => Test(options),
            "verify" => Verify(options),
            "export" => Export(options),
            "render" => Render(args, options),
            "ext" => Ext(args, options),
            "compare" => Compare(options),
            "schema" => Schema(options),
            "templates" => Catalog(options, "template"),
            "examples" => Catalog(options, "example"),
            "agents-md" => AgentsMd(options),
            "agents" => Agents(args, options),
            "doctor" => Doctor(options),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'");
        Console.Error.Write(Usage);
        return (int)ExitCode.ValidationFailure;
    }

    /// <summary>
    /// Writes a command result as JSON, for CLI-2.
    /// </summary>
    /// <remarks>
    /// Results on stdout so a caller may pipe it straight to a parser without
    /// filtering. Diagnostics go to stderr everywhere else in this file for the
    /// same reason.
    /// </remarks>
    private static int Emit<T>(T outcome, ExitCode code = ExitCode.Success)
    {
        Console.Out.Write(CommandJson.Write(outcome));
        return (int)code;
    }

    private static int Schema(CommandLine options)
    {
        // The schema is JSON whether or not --json was asked for; the flag is
        // accepted and ignored rather than refused, because an agent that passes
        // it to every verb should not have to special-case this one.
        if (options.Has("study"))
        {
            Console.Out.Write(CatalogCommand.StudySchema());
            return (int)ExitCode.Success;
        }

        Console.Out.Write(CatalogCommand.Schema());
        return (int)ExitCode.Success;
    }

    private static int Sweep(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel sweep <study.json> [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var outcome = StudyCommand.Sweep(studyPath, new ProjectLayout(root), options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would sweep {outcome.Draws} draws of {outcome.FigureOfMerit.Name} over {outcome.ModelPath}"));

            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.FigureOfMerit.Name}  nominal {outcome.Nominal:G8} {outcome.FigureOfMerit.Unit}, "
            + $"{outcome.Succeeded} of {outcome.Draws} draws arrived"));

        if (outcome.Distribution is { } distribution)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {distribution.Value:G6} +/- "
                + $"{(distribution.Uncertainty.Upper - distribution.Uncertainty.Lower) / 2.0:G3} "
                + $"{distribution.Unit} ({distribution.Uncertainty.ConfidenceLevel:P0} CI)"));

            // GRD-2: warnings travel to every surface, including this one.
            foreach (var warning in distribution.Warnings)
            {
                var stream = warning.Suppressible ? Console.Out : Console.Error;
                stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
            }
        }

        // What the draws themselves earned, which is a different set: the
        // distribution's warnings are about the distribution, and these are about
        // the flights it was computed from.
        Warn(outcome.Warnings);

        if (outcome.Sensitivity.Count > 0)
        {
            // Section 13 calls this the actual deliverable: not whether the
            // tolerance suffices, but which parameter binds first.
            Console.Out.WriteLine();
            Console.Out.WriteLine(
                $"which parameter binds first (figure in {outcome.FigureOfMerit.Unit}, "
                + "swing is the larger departure from nominal):");

            foreach (var channel in outcome.Sensitivity)
            {
                // The two ends as well as the swing, because a swing alone does not
                // say which direction hurts, and a channel whose low end is missing
                // is one where the ion stopped arriving - which is the finding, not
                // a gap in the table.
                var band = channel is { Low: { } low, High: { } high }
                    ? string.Create(invariant, $"{low:G8} .. {high:G8}")
                    : "one end did not arrive";

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {channel.Parameter,-24} swing {channel.Swing:G4}   [{band}]"));
            }
        }

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)ExitCode.Success;
    }

    private static int Optimise(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel optimise <study.json> [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var outcome = StudyCommand.Optimise(studyPath, new ProjectLayout(root), options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(
                $"would {outcome.Sense.ToLowerInvariant()} {outcome.FigureOfMerit.Name} "
                + $"by {outcome.Algorithm} over {outcome.ModelPath}");

            return (int)ExitCode.Success;
        }

        foreach (var (name, measured) in outcome.Best)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"{name,-24} {measured.Value:G8} +/- "
                + $"{(measured.Uncertainty.Upper - measured.Uncertainty.Lower) / 2.0:G3} {measured.Unit}"));
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.FigureOfMerit.Name,-24} {outcome.Objective.Value:G6} {outcome.Objective.Unit} "
            + $"({outcome.Sense.ToLowerInvariant()}d)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.Algorithm}: {outcome.Evaluations} evaluations, {outcome.Failures} failed, "
            + $"converged {outcome.Converged}"));

        foreach (var warning in outcome.Objective.Warnings)
        {
            var stream = warning.Suppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        // What the evaluations earned on the way. An optimiser walks towards
        // whatever scores best, so a corner of the box where the solve stops
        // converging is somewhere it will actively go - and it used to arrive there
        // silently.
        Warn(outcome.Warnings);

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)(outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
    }

    /// <summary>A population limit, rendered so a limit below ten ions keeps a figure.</summary>
    /// <remarks>
    /// The same rendering the warning uses, so the summary line and the warning
    /// beneath it do not quote the same quantity as 5 and as 4.7.
    /// </remarks>
    private static string Capacity(double limit) => limit switch
    {
        < 1.0 => limit.ToString("F2", CultureInfo.InvariantCulture),
        < 10.0 => limit.ToString("F1", CultureInfo.InvariantCulture),
        _ => limit.ToString("N0", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Prints warnings, advisories on stdout and everything else on stderr.
    /// </summary>
    /// <remarks>
    /// CLI-2 puts results on stdout and diagnostics on stderr, and GRD-3 makes only
    /// an advisory suppressible - so the split by severity is the same split by
    /// stream, and a caller redirecting stdout still sees what qualified the number.
    /// </remarks>
    private static void Warn(IReadOnlyList<ValidityWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            var stream = warning.IsSuppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }
    }

    private static int Catalog(CommandLine options, string kind)
    {
        // With a name, emit that artifact; without, list what there is.
        if (options.Positional.Count > 0)
        {
            Console.Out.Write(CatalogCommand.Read(kind, options.Positional[0]));
            return (int)ExitCode.Success;
        }

        var outcome = kind == "template" ? CatalogCommand.Templates() : CatalogCommand.Examples();

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var entry in outcome.Entries)
        {
            Console.Out.WriteLine(entry.Name);

            if (!string.IsNullOrEmpty(entry.Description))
            {
                Console.Out.WriteLine($"    {Wrap(entry.Description)}");
            }
        }

        return (int)ExitCode.Success;
    }

    /// <summary>Truncates a description to one readable terminal line.</summary>
    private static string Wrap(string text)
    {
        const int Width = 96;

        if (text.Length <= Width)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', Width);
        return text[..(cut > 0 ? cut : Width)] + "...";
    }

    private static int New(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel new <model.json> --from-template <name> | --from-example <name> [--dry-run]");
            return (int)ExitCode.ValidationFailure;
        }

        var (kind, name) = options.Value("from-template") is { } template
            ? ("template", template)
            : options.Value("from-example") is { } example
                ? ("example", example)
                : (null, null);

        if (kind is null || name is null)
        {
            Console.Error.WriteLine("give one of --from-template <name> or --from-example <name>");
            Console.Error.WriteLine("run 'einzel templates' or 'einzel examples' to see what there is");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = ProjectCommands.New(options.Positional[0], kind, name, options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written
            ? $"wrote {outcome.Path} from {outcome.Source}"
            : $"would write {outcome.Path} from {outcome.Source}");

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// The agent acceptance suite: list the tasks, set one up, score an attempt.
    /// </summary>
    /// <remarks>
    /// A development and release tool rather than something a modelling session
    /// reaches for, but it lives behind the same command objects as everything
    /// else (AGT-2) so a harness can drive it the same way. The agent under test
    /// is never given this verb - it gets a project directory and the rest of the
    /// CLI, which is the situation being measured.
    /// </remarks>
    private static int Agents(string[] args, CommandLine options)
    {
        var action = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : "tasks";

        return action switch
        {
            "tasks" => AgentTasks(options),
            "setup" => AgentSetup(options),
            "score" => AgentScore(options),
            _ => AgentUsage(action),
        };
    }

    private static int AgentUsage(string action)
    {
        Console.Error.WriteLine($"unknown agents action '{action}'");
        Console.Error.WriteLine("usage: einzel agents tasks [name]");
        Console.Error.WriteLine("       einzel agents setup <task> <dir>");
        Console.Error.WriteLine("       einzel agents score <task> <dir>");
        return (int)ExitCode.ValidationFailure;
    }

    private static int AgentTasks(CommandLine options)
    {
        // "agents tasks" leaves the action in the positional list, so a named
        // task is the second one.
        var name = options.Positional.Count > 1 ? options.Positional[1] : null;

        if (name is not null)
        {
            var task = AgentSuite.Find(name);

            if (options.Has("json"))
            {
                return Emit(new
                {
                    task.Name,
                    Track = task.Track.ToString(),
                    task.Deliverable,
                    task.Rationale,
                    task.Prompt,
                });
            }

            // The prompt alone on stdout, so a harness can pipe it straight to an
            // agent without stripping anything.
            Console.Out.Write(task.Prompt);
            return (int)ExitCode.Success;
        }

        if (options.Has("json"))
        {
            return Emit(AgentSuite.All.Select(t => new
            {
                t.Name,
                Track = t.Track.ToString(),
                t.Deliverable,
                t.Rationale,
                t.Prompt,
            }));
        }

        foreach (var task in AgentSuite.All)
        {
            Console.Out.WriteLine($"{task.Name,-28} {task.Track,-11} -> {task.Deliverable}");
        }

        return (int)ExitCode.Success;
    }

    private static int AgentSetup(CommandLine options)
    {
        if (options.Positional.Count < 3)
        {
            Console.Error.WriteLine("usage: einzel agents setup <task> <dir>");
            return (int)ExitCode.ValidationFailure;
        }

        var task = AgentSuite.Find(options.Positional[1]);
        var root = Path.GetFullPath(options.Positional[2]);

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine($"would prepare {root} for '{task.Name}'");
            return (int)ExitCode.Success;
        }

        var layout = AgentSuite.Prepare(task, root);

        Console.Out.WriteLine($"prepared {layout.Root} for '{task.Name}'");
        Console.Out.WriteLine($"the agent should leave {task.Deliverable}");
        return (int)ExitCode.Success;
    }

    private static int AgentScore(CommandLine options)
    {
        if (options.Positional.Count < 3)
        {
            Console.Error.WriteLine("usage: einzel agents score <task> <dir>");
            return (int)ExitCode.ValidationFailure;
        }

        var score = AgentSuite.Score(options.Positional[1], options.Positional[2]);

        if (options.Has("json"))
        {
            return Emit(score, score.Passed ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        foreach (var check in score.Checks)
        {
            var mark = check.Passed ? "ok  " : "FAIL";
            var stream = check.Passed ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {check.Name}");
            stream.WriteLine($"       {check.Detail}");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"{score.Task} ({score.Track}): {(score.Passed ? "passed" : "failed")}");

        return (int)(score.Passed ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    private static int AgentsMd(CommandLine options)
    {
        var root = options.Value("project") ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");
        var outcome = ProjectCommands.AgentsMd(root, options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written
            ? $"wrote {outcome.Path} ({outcome.Source})"
            : $"would write {outcome.Path} ({outcome.Source})");

        return (int)ExitCode.Success;
    }

    private static int Doctor(CommandLine options)
    {
        var root = options.Value("project") ?? (options.Positional.Count > 0 ? options.Positional[0] : null);

        if (root is null && Directory.Exists("models"))
        {
            root = ".";
        }

        var outcome = ProjectCommands.Doctor(root);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Healthy ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        foreach (var check in outcome.Checks)
        {
            var mark = check.Ok ? "ok  " : "WARN";
            var stream = check.Ok ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {check.Check,-20} {check.Detail}");
        }

        return (int)(outcome.Healthy ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    private static int Estimate(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel estimate <model.json> [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = EstimateCommand.Execute(options.Positional[0]);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        foreach (var element in outcome.Elements)
        {
            var size = element.Nodes.Count == 2
                ? string.Create(invariant, $"{element.Nodes[0]}x{element.Nodes[1]}")
                : "analytic";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"field {element.Index}  {element.Type,-14} {size,-12} {element.Seconds,8:F2} s  {element.MemoryMiB,7:F1} MiB"));
        }

        Console.Out.WriteLine(string.Create(
            invariant, $"total         {outcome.Seconds:F2} s, peak {outcome.MemoryMiB:F1} MiB"));

        Console.Out.WriteLine();
        Console.Out.WriteLine($"basis: {outcome.Basis}");

        if (outcome.AboveThreshold)
        {
            // GRD-8: above the threshold this is a refusal to proceed silently,
            // not a warning printed on the way past.
            Console.Error.WriteLine(string.Create(
                invariant,
                $"this is above the {outcome.ThresholdSeconds:F0} s cost threshold"));

            return (int)ExitCode.CostGateRefused;
        }

        return (int)ExitCode.Success;
    }

    private static int Solve(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel solve <model.json> [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = SolveCommand.Execute(options.Positional[0]);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Elements.Count == 0)
        {
            Console.Out.WriteLine("no solved field elements in this model; nothing to solve");
            return (int)ExitCode.Success;
        }

        foreach (var element in outcome.Elements)
        {
            var shape = element.SquareCells ? "square" : "stretched";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"field {element.Index}  {element.Nodes[0]}x{element.Nodes[1]} at "
                + $"{element.SpacingMm[0]:F4} x {element.SpacingMm[1]:F4} mm ({shape})"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Electrodes} electrode(s), {element.FixedNodes} fixed node(s), "
                + $"{element.CutLinks} cut link(s)"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Cycles} cycles at factor {element.ConvergenceFactor:F4}, "
                + $"residual {element.RelativeResidual:E2} of initial"));

            Console.Out.WriteLine(string.Create(
                invariant, $"          peak |phi| {element.PeakPotentialVolts:G6} V"));

            if (!element.Converged)
            {
                Console.Error.WriteLine($"field {element.Index} did not converge");
            }
        }

        Console.Out.WriteLine(string.Create(invariant, $"solved in {outcome.ElapsedMs:F0} ms"));

        return (int)(outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
    }

    private static int Preview(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel preview <model.json> [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = PreviewCommand.Execute(options.Positional[0]);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Outcome == "StopConditionMet"
                ? ExitCode.Success
                : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        Console.Out.WriteLine(string.Create(
            invariant,
            $"flight time   {outcome.FlightTime.Value:F4} {outcome.FlightTime.Unit} (preview)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"              {outcome.AcceptedSteps} steps at tolerance {outcome.RelativeTolerance:G3}, "
            + $"{outcome.ElapsedMs:F0} ms"));

        // GRD-5: the mark is not suppressible and does not depend on the caller
        // having thought to look for it.
        foreach (var warning in outcome.FlightTime.Warnings)
        {
            Console.Error.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)(outcome.Outcome == "StopConditionMet"
            ? ExitCode.Success
            : ExitCode.ConvergenceFailure);
    }

    private static int Test(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = TestCommand.Execute(root);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.AllPassed ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        if (outcome.Tests.Count == 0)
        {
            // A green tick standing for no evidence at all - which this said in a
            // comment while returning success anyway. A caller that gates on the
            // exit code cannot tell "everything passed" from "there was nothing to
            // run", and those are opposite states.
            Console.Error.WriteLine($"no tests under {Path.Combine(outcome.Root, "tests")}");
            Console.Error.WriteLine(
                "a project with no tests is not a passing project, so this is not exit 0. "
                + "'einzel init' scaffolds one; 'einzel schema' describes what a test file holds");

            return (int)ExitCode.ValidationFailure;
        }

        var invariant = CultureInfo.InvariantCulture;

        foreach (var test in outcome.Tests)
        {
            var mark = test.Passed ? "ok  " : "FAIL";
            var stream = test.Passed ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {test.Name}");

            if (test.Failure is { } failure)
            {
                Console.Error.WriteLine($"       {failure}");
                continue;
            }

            foreach (var assertion in test.Assertions)
            {
                if (assertion.Observed is not { } observed)
                {
                    Console.Error.WriteLine(string.Create(
                        invariant,
                        $"       {assertion.FigureOfMerit}: nothing arrived, so there is no value to compare"));

                    continue;
                }

                var line = string.Create(
                    invariant,
                    $"       {assertion.FigureOfMerit} {observed:G8} {assertion.Unit}, expected "
                    + $"{assertion.Expected:G8}, off by {assertion.RelativeError:E2} of "
                    + $"{assertion.Tolerance:E2} allowed");

                (assertion.Passed ? Console.Out : Console.Error).WriteLine(line);
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"{outcome.Passed} of {outcome.Tests.Count} tests passed");

        return (int)(outcome.AllPassed ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    private static int Verify(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = VerifyCommand.Execute(root);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.AllCurrent ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        if (outcome.Results.Count == 0)
        {
            Console.Out.WriteLine($"no stored results under {outcome.Root}");
            return (int)ExitCode.Success;
        }

        foreach (var result in outcome.Results)
        {
            var mark = result.Current ? "ok  " : "STALE";
            var stream = result.Current ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {result.Manifest}");

            foreach (var drift in result.Drift)
            {
                Console.Error.WriteLine($"       {drift}");
            }

            foreach (var note in result.Notes)
            {
                Console.Out.WriteLine($"       note: {note}");
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"{outcome.Current} of {outcome.Results.Count} results still stand");

        // A stale result is not an engine failure, it is a project that has moved
        // on; the exit code says so without a caller parsing anything.
        return (int)(outcome.AllCurrent ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    private static int Export(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel export <model.json> [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("writes the solved potential field as VTK ImageData for ParaView");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(modelPath);
        var outcome = ExportCommand.Vtu(modelPath, new ProjectLayout(root), options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine(outcome.Written ? $"wrote {artifact}" : $"would write {artifact}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Draws a model, headlessly, into a vector file.
    /// </summary>
    /// <remarks>
    /// Section only, for now. <c>still</c> is a raster projection and <c>animation</c>
    /// is a frame sequence with the non-linear time mapping RND-7 requires; both are
    /// named here and refused with a reason rather than left to fail as an unknown
    /// verb, because "not built yet" and "you spelled it wrong" are different
    /// problems and an agent should not have to guess which it hit.
    /// </remarks>
    private static int Render(string[] args, CommandLine options)
    {
        var kind = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : null;

        if (kind is "still" or "animation")
        {
            Console.Error.WriteLine(
                $"'einzel render {kind}' is not built yet. Vector sections are: "
                + "'einzel render section <model.json>'.");

            Console.Error.WriteLine(
                kind == "still"
                    ? "A still is a raster projection; nothing in this build rasterises."
                    : "An animation is a frame sequence with an explicit non-linear time mapping, "
                        + "which needs the sequencer's timeline and a frame writer.");

            return (int)ExitCode.ValidationFailure;
        }

        var positional = kind is null ? options.Positional : options.Positional.Skip(1).ToList();

        if (kind is not (null or "section") || positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render section <model.json | figures/spec.json> [--out <file>]");
            Console.Error.WriteLine(
                "       [--format svg|pdf] [--equipotentials N] [--width-mm W] [--no-trajectory]");
            Console.Error.WriteLine(
                "       [--caption <text>] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("draws a plane through the instrument as line work");

            return (int)ExitCode.ValidationFailure;
        }

        var given = Path.GetFullPath(positional[0]);

        // A render spec names its own model (RND-2), so either may be handed in and
        // the spec is the one that travels with the paper.
        var isSpec = ReadsAsSpec(given);

        var (spec, modelPath) = isSpec
            ? RenderCommand.ReadSpec(given)
            : (new RenderSpec { Model = given }, ModelPath: given);

        spec = spec with
        {
            Format = (options.Value("format") ?? spec.Format.ToString()).ToUpperInvariant() switch
            {
                "PDF" => FigureFormat.Pdf,
                _ => FigureFormat.Svg,
            },
            WidthMm = options.Value("width-mm") is { } width
                ? double.Parse(width, CultureInfo.InvariantCulture)
                : spec.WidthMm,
            Equipotentials = options.Value("equipotentials") is { } count
                ? int.Parse(count, CultureInfo.InvariantCulture)
                : spec.Equipotentials,
            Trajectory = !options.Has("no-trajectory") && spec.Trajectory,
            Caption = options.Value("caption") ?? spec.Caption,
        };

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        var outcome = RenderCommand.Section(
            modelPath, new ProjectLayout(root), spec, options.Value("out"), options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine(outcome.Written ? $"wrote {artifact}" : $"would write {artifact}");
        }

        Console.Out.WriteLine(
            $"{outcome.PageMm[0]:F0} by {outcome.PageMm[1]:F0} mm, "
            + $"{outcome.Paths.Values.Sum()} paths, {outcome.TextRuns} labels");

        Console.Out.WriteLine(
            $"decimated to {outcome.DecimationToleranceMm:G3} mm; trajectory "
            + $"{outcome.TrajectoryPointsSampled} points to {outcome.TrajectoryPoints}");

        // GRD-2: onto stderr, so a warning is not lost in a pipe that keeps stdout.
        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>Whether a file is a render spec rather than a model.</summary>
    /// <remarks>
    /// By what it declares, not by where it sits: a spec carries
    /// <c>renderSpecVersion</c> and a model carries <c>schemaVersion</c>. Guessing
    /// from the folder would make <c>figures/</c> load-bearing, and a file moved is
    /// then a file broken.
    /// </remarks>
    private static bool ReadsAsSpec(string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            return document.RootElement.TryGetProperty("renderSpecVersion", out _)
                || document.RootElement.TryGetProperty("kind", out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>The extension authoring loop: list, test, register.</summary>
    private static int Ext(string[] args, CommandLine options)
    {
        var action = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "list";
        var rest = options.Positional.Skip(1).ToList();

        var root = options.Value("project") ?? InferProjectRoot(Directory.GetCurrentDirectory());
        var project = new ProjectLayout(root);

        switch (action)
        {
            case "list":
            {
                var outcome = ExtensionCommand.List(project);

                if (options.Has("json"))
                {
                    return Emit(outcome);
                }

                Console.Out.WriteLine(
                    $"engine {outcome.EngineVersion}, interpreter {outcome.Interpreter ?? "NOT FOUND"}");

                if (outcome.Extensions.Count == 0)
                {
                    Console.Out.WriteLine("no extensions installed");
                }

                foreach (var entry in outcome.Extensions)
                {
                    Console.Out.WriteLine(
                        $"  {entry.Name} {entry.Version}  {entry.Kind}/{entry.Trust}"
                        + $"{(entry.Incompatibility is null ? string.Empty : "  INCOMPATIBLE: " + entry.Incompatibility)}");
                }

                // EXT-3 asks for OS-level isolation this build does not apply, and
                // somebody deciding whether to run agent-authored code needs to know
                // that before they run it rather than after.
                Console.Error.WriteLine();
                Console.Error.WriteLine("the subprocess runner does NOT enforce:");

                foreach (var gap in outcome.UnenforcedContainment)
                {
                    Console.Error.WriteLine($"  - {gap}");
                }

                return (int)ExitCode.Success;
            }

            case "test":
            {
                if (rest.Count == 0)
                {
                    Console.Error.WriteLine(
                        "usage: einzel ext test <name> [--input <file.json>] [--project <dir>] [--json]");
                    return (int)ExitCode.ValidationFailure;
                }

                var payload = options.Value("input") is { } file
                    ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.GetFullPath(file)))
                    : new System.Text.Json.Nodes.JsonObject();

                var outcome = ExtensionCommand.Test(project, rest[0], payload);

                if (options.Has("json"))
                {
                    return Emit(outcome);
                }

                Console.Out.WriteLine(
                    $"{outcome.Name} {outcome.Version} returned in {outcome.ElapsedMs:F0} ms:");

                Console.Out.WriteLine(outcome.Output?.ToJsonString() ?? "null");

                if (outcome.Diagnostics is { } diagnostics)
                {
                    Console.Error.Write(diagnostics);
                }

                foreach (var warning in outcome.Warnings)
                {
                    Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
                }

                return (int)ExitCode.Success;
            }

            case "register":
            {
                if (rest.Count == 0)
                {
                    Console.Error.WriteLine(
                        "usage: einzel ext register <name> [--kind objective|analysis|geometry|"
                        + "sequence|interchange] [--project <dir>] [--dry-run] [--json]");
                    return (int)ExitCode.ValidationFailure;
                }

                var kind = Enum.TryParse<Einzel.Extensions.ExtensionKind>(
                    options.Value("kind") ?? "objective", ignoreCase: true, out var parsed)
                    ? parsed
                    : Einzel.Extensions.ExtensionKind.Objective;

                var created = ExtensionCommand.Register(project, rest[0], kind, options.Has("dry-run"));

                foreach (var file in created)
                {
                    Console.Out.WriteLine(options.Has("dry-run") ? $"would write {file}" : $"wrote {file}");
                }

                return (int)ExitCode.Success;
            }

            default:
                Console.Error.WriteLine($"unknown 'ext' action '{action}'");
                Console.Error.WriteLine("usage: einzel ext list | test <name> | register <name>");
                return (int)ExitCode.ValidationFailure;
        }
    }

    /// <summary>
    /// Runs both transport modes on one model and reports the disagreement.
    /// </summary>
    /// <remarks>
    /// REG-3 makes this a supported operation with its own report rather than
    /// something a careful user assembles by hand. In the overlap band both
    /// descriptions run and neither is obviously right; the engine's job is to say
    /// by how much they differ, not to pick one.
    /// </remarks>
    private static int Compare(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel compare <model.json> [--ions N] [--json]");
            Console.Error.WriteLine(
                "runs trajectory integration and statistical diffusion on the same model");
            return (int)ExitCode.ValidationFailure;
        }

        var ions = options.Value("ions") is { } count
            ? int.Parse(count, CultureInfo.InvariantCulture)
            : 60;

        var outcome = ModeComparison.Execute(Path.GetFullPath(options.Positional[0]), ions);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.PressureMbar:G3} mbar, "
            + $"{(outcome.InOverlapBand ? "inside" : "OUTSIDE")} the band where both modes apply"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"trajectory   {outcome.TrajectoryTransitUs:F3} +/- {outcome.TrajectoryStandardErrorUs:F3} us "
            + $"({outcome.Ions} ions, {outcome.TrajectoryTransmission:P0} arrived)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"diffusion    {outcome.DiffusionTransitUs:F3} us "
            + $"({outcome.DiffusionTransmission:P0} arrived)"));

        if (outcome.DifferenceUs is not null)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"disagreement {outcome.DifferenceUs:F3} us, {outcome.RelativeDifference:P2}, "
                + $"{outcome.StandardErrors:F2} standard errors"));
        }

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    private static int Init(CommandLine options)
    {
        var root = options.Positional.Count > 0 ? options.Positional[0] : ".";

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine($"would create a project at {Path.GetFullPath(root)}");
            return (int)ExitCode.Success;
        }

        var outcome = InitCommand.Execute(root, withGit: options.Value("vcs") == "git");

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(outcome));
            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(outcome.AlreadyExisted
            ? $"Project already present at {outcome.Root}; missing pieces added."
            : $"Created project at {outcome.Root}");

        foreach (var item in outcome.Created)
        {
            Console.Out.WriteLine($"  {item}");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine("Next: einzel run models/reflectron.json --vtu");
        return (int)ExitCode.Success;
    }

    private static int Validate(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel validate <model.json> [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = RunCommand.Validate(options.Positional[0]);

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(outcome));
            return (int)outcome.ExitCode;
        }

        if (outcome.Valid)
        {
            Console.Out.WriteLine($"OK  {outcome.ModelPath}");
            Console.Out.WriteLine($"    schema {outcome.SchemaVersion}, {outcome.ModelHash}");
            return (int)ExitCode.Success;
        }

        Console.Error.WriteLine($"{outcome.Errors.Count} problem(s) in {outcome.ModelPath}:");
        Console.Error.WriteLine();

        foreach (var error in outcome.Errors)
        {
            WriteError(error);
        }

        return (int)outcome.ExitCode;
    }

    private static int Run(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel run <model.json> [--vtu] [--json] [--project <dir>]");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(modelPath);
        var project = new ProjectLayout(root);

        var (run, validation) = RunCommand.Execute(
            modelPath, project, exportVtu: options.Has("vtu"), timestampUtc: DateTimeOffset.UtcNow);

        if (run is null)
        {
            if (options.Has("json"))
            {
                Console.Out.Write(CommandJson.Write(validation));
            }
            else
            {
                Console.Error.WriteLine($"{validation.Errors.Count} problem(s) in {validation.ModelPath}:");
                Console.Error.WriteLine();

                foreach (var error in validation.Errors)
                {
                    WriteError(error);
                }
            }

            return (int)validation.ExitCode;
        }

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(run));
        }
        else
        {
            WriteRun(run);
        }

        // A regime violation outranks an incomplete flight, because it explains it:
        // an ion that never reaches the detector at 1 mbar has not failed to
        // converge, it has been described by the wrong physics. CLI-3 gives the two
        // distinct codes so a caller can tell them apart without parsing anything,
        // and REG-2 is what code 2 is for.
        if (run.FlightTime.Warnings.Any(
            w => w.Code.StartsWith("regime.", StringComparison.Ordinal)
                && w.Severity == nameof(WarningSeverity.ValidityViolation)))
        {
            return (int)ExitCode.RegimeViolation;
        }

        // A run that ended anywhere but the detector is a convergence failure, not
        // a success with a caveat. A diffusive run has no detector to end at - it
        // evolves a density for the declared time and reports where the ions went -
        // so it succeeds by finishing, and what it transmitted is a figure rather
        // than an outcome.
        return run.Outcome is "StopConditionMet" or "DensityEvolved"
            ? (int)ExitCode.Success
            : (int)ExitCode.ConvergenceFailure;
    }

    private static void WriteRun(RunOutcome run)
    {
        var invariant = CultureInfo.InvariantCulture;
        var flight = run.FlightTime;
        var halfWidth = (flight.Uncertainty.Upper - flight.Uncertainty.Lower) / 2.0;

        // GRD-1 in the terminal: the value never appears without what qualifies it.
        // ASCII rather than a plus-minus sign, because the console encoding is not
        // ours to assume and a mangled character in a reported uncertainty is
        // exactly the wrong place to be clever.
        Console.Out.WriteLine(string.Create(
            invariant,
            $"flight time   {flight.Value:F6} +/- {halfWidth:G3} {flight.Unit}"));

        // A null observed order means the refinements agreed to the last bit, so
        // there is no order to report. Saying "converged to round-off" is the
        // honest rendering; printing an empty number would read as a missing
        // measurement rather than a perfect one.
        var convergence = flight.Evidence.ObservedOrder is { } order
            ? string.Create(invariant, $"observed order {order:G3} of {flight.Evidence.NominalOrder:G3}")
            : "residual at round-off, no order to resolve";

        Console.Out.WriteLine(
            $"              {flight.Evidence.Kind} in {flight.Evidence.Measure}, {convergence}");

        Console.Out.WriteLine(string.Create(
            invariant, $"energy drift  {run.MaximumRelativeEnergyDrift:E2} relative (ACC-4 budget 1e-6)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"steps         {run.AcceptedSteps}, {run.AnalyticDriftDistanceM:F4} m advanced analytically"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"final x       {run.FinalPositionMm[0]:F6} mm"));

        if (run.Ensemble is { } ensemble)
        {
            Console.Out.WriteLine();

            Console.Out.WriteLine(string.Create(
                invariant,
                $"cloud         {ensemble.Arrived} of {ensemble.Launched} ions arrived, transmission "
                + $"{ensemble.Transmission.Value:P1} +/- "
                + $"{(ensemble.Transmission.Uncertainty.Upper - ensemble.Transmission.Uncertainty.Lower) / 2.0:P1}"));

            // Two widths, both named. They agree only for a Gaussian peak, and the
            // gap between them is the skew - so printing one of them beside a
            // resolving power computed from the other invites exactly the wrong
            // reconciliation.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"peak          {ensemble.CentralWidthNs:F3} ns central half, "
                + $"{ensemble.GaussianFwhmNs:F3} ns Gaussian FWHM, skew {ensemble.Skewness:+0.00;-0.00;0.00}"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"turn-around   {ensemble.TurnAroundFwhmNs:F3} ns of that Gaussian width"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"resolving     {ensemble.ResolvingPower.Value:G6} +/- "
                + $"{(ensemble.ResolvingPower.Uncertainty.Upper - ensemble.ResolvingPower.Uncertainty.Lower) / 2.0:G3}"
                + $" (from the central half)"));

            // ACC-5: never a bare percentage. A named surface says which one to
            // move; "transmission is 51 percent" says only that something is wrong.
            foreach (var loss in ensemble.Losses)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  lost        {loss.Ions:N0} on {loss.Surface} "
                    + $"({(double)loss.Ions / ensemble.Launched:P1})"));
            }

            if (ensemble.EmittanceMmMrad is { } major)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"emittance     {major:G4} x {ensemble.EmittanceMinorMmMrad:G4} mm.mrad, "
                    + $"normalised {ensemble.NormalisedEmittanceMmMrad:G4}"));

                // No alpha means no ellipse: every ion exactly parallel, so there
                // is no waist to be on one side of.
                var orientation = ensemble.PacketTwissAlpha switch
                {
                    > 0.0 => "still converging",
                    < 0.0 => "past the waist",
                    _ => "parallel",
                };

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"packet        {ensemble.PacketRadiusMm:F3} mm rms, alpha "
                    + $"{ensemble.PacketTwissAlpha:+0.00;-0.00;0.00} ({orientation})"));
            }

            // Reported whether or not it crosses a threshold: a number that only
            // appears when it is bad teaches nobody where the edge is.
            if (ensemble.SpaceChargePopulationLimit > 0.0
                && ensemble.SpaceChargeTimingFraction is { } timingFraction)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"space charge  {timingFraction / 1e-6:F2} ppm from "
                    + $"{ensemble.Population:N0} ions; this packet holds "
                    + $"{Capacity(ensemble.SpaceChargePopulationLimit)} within the 1 ppm budget"));
            }

            foreach (var warning in ensemble.ResolvingPower.Warnings
                .Concat(ensemble.Transmission.Warnings)
                .GroupBy(w => w.Code, StringComparer.Ordinal)
                .Select(g => g.First()))
            {
                var stream = warning.Suppressible ? Console.Out : Console.Error;
                stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
            }

            Console.Out.WriteLine();
        }

        Console.Out.WriteLine($"engine        {run.Manifest.EngineVersion}, model {run.Manifest.ModelHash[..14]}...");

        // GRD-2: warnings travel with the result to every surface, including this
        // one, and are never folded away behind a flag.
        foreach (var warning in flight.Warnings)
        {
            var stream = warning.Suppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        Console.Out.WriteLine();

        foreach (var artifact in run.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }
    }

    private static void WriteError(EinzelError error)
    {
        Console.Error.WriteLine($"  {error.Code}");
        Console.Error.WriteLine($"    at         {error.Path}");
        Console.Error.WriteLine($"    constraint {error.Constraint}");

        if (error.Observed is not null)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    observed   {error.Observed.Value:G6} {error.Observed.Unit}"));
        }

        if (!string.IsNullOrEmpty(error.Suggestion))
        {
            Console.Error.WriteLine($"    try        {error.Suggestion}");
        }

        Console.Error.WriteLine();
    }

    /// <summary>
    /// Walks up from a model file looking for a project root, so that
    /// <c>einzel run models/x.json</c> works from anywhere inside a project.
    /// </summary>
    private static string InferProjectRoot(string modelPath)
    {
        var directory = new FileInfo(modelPath).Directory;

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "models")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private const string Usage =
        """
        einzel - open-source, agent-native ion optics

        usage: einzel <command> [arguments]

        starting out:
          doctor [dir]                  check the installation, and a project if given
          schema [--study]              the model format, or the study format, as JSON Schema
          templates [name]              list device templates, or print one
          examples [name]               list example models, or print one

        working:
          init [dir] [--vcs git]        create a project directory
          new <model.json>              create a model from a template or an example
                --from-template <name> | --from-example <name>
          validate <model.json>         check units, bounds, and regime validity
          estimate <model.json>         what a run will cost, without running it
          solve <model.json>            solve the fields only, and report how they went
          run <model.json> [--vtu]      run a model; --vtu also writes a ParaView trajectory
          preview <model.json>          a fast, deliberately inexact look
          test [dir]                    run the project's tests
          verify [dir]                  are the stored results still the answer?
          sweep <study.json>            tolerance Monte Carlo, and which parameter binds first
          optimise <study.json>         search the declared parameters for a better design
          export <model.json>           write the solved field as VTK ImageData
          compare <model.json>          run both transport modes and report the disagreement
          render section <model.json>   draw a plane through the instrument as SVG or PDF
          ext list | test | register    the extension authoring loop
          agents-md [dir]               regenerate the platform layer of AGENTS.md

        release tooling:
          agents tasks [name]           the acceptance suite: list tasks, or print one's prompt
          agents setup <task> <dir>     prepare a project for an agent to attempt a task
          agents score <task> <dir>     score what the agent left behind
          --version                     print the engine version

        options:
          --json                        machine-readable output
          --dry-run                     say what would be written, and write nothing
          --project <dir>               project root; inferred from the model path otherwise

        Results go to stdout, diagnostics to stderr. Exit codes: 0 success,
        1 validation failure, 2 regime violation, 3 cost-gate refusal,
        4 convergence failure, 5 engine-pin mismatch, 6 internal error.

        """;
}
