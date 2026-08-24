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
            "validate" => Validate(options),
            "run" => Run(options),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'");
        Console.Error.Write(Usage);
        return (int)ExitCode.ValidationFailure;
    }

    private static int Init(CommandLine options)
    {
        var root = options.Positional.Count > 0 ? options.Positional[0] : ".";
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

          init [dir] [--vcs git]        create a project directory
          validate <model.json>         check units, bounds, and regime validity
          run <model.json> [--vtu]      run a model; --vtu also writes a ParaView trajectory
          --version                     print the engine version

        options:
          --json                        machine-readable output
          --project <dir>               project root; inferred from the model path otherwise

        Results go to stdout, diagnostics to stderr. Exit codes: 0 success,
        1 validation failure, 2 regime violation, 3 cost-gate refusal,
        4 convergence failure, 5 engine-pin mismatch, 6 internal error.

        """;
}
