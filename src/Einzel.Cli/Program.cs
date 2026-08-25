using System.Globalization;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Project;

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
            "schema" => Schema(options),
            "templates" => Catalog(options, "template"),
            "examples" => Catalog(options, "example"),
            "agents-md" => AgentsMd(options),
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

        if (outcome.Sensitivity.Count > 0)
        {
            // Section 13 calls this the actual deliverable: not whether the
            // tolerance suffices, but which parameter binds first.
            Console.Out.WriteLine();
            Console.Out.WriteLine("which parameter binds first:");

            foreach (var channel in outcome.Sensitivity)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {channel.Parameter,-24} swing {channel.Swing:G4} {outcome.FigureOfMerit.Unit}"));
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

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)(outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
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
            // Not a failure, but worth saying plainly. A test run that asserts
            // nothing and reports success is a green tick standing for no
            // evidence at all.
            Console.Out.WriteLine($"no tests under {Path.Combine(outcome.Root, "tests")}");
            return (int)ExitCode.Success;
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

        // A run that ended anywhere but the detector is a convergence failure, not
        // a success with a caveat.
        return run.Outcome == "StopConditionMet"
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
          agents-md [dir]               regenerate the platform layer of AGENTS.md
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
