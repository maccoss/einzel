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
        // Whether anything has been written to stdout, so that a refusal raised after a verb wrote
        // its own result does not add a second document to it.
        var stdout = Console.Out;
        var tally = new StdoutTally(stdout);
        Console.SetOut(tally);

        try
        {
            return Dispatch(args);
        }
        catch (EinzelException failure)
        {
            // Every error, not the first: a command that validated on its way to something
            // else found them all, and a caller fixing one per round trip is a caller who was
            // told less than the platform knew.
            foreach (var error in failure.Errors)
            {
                Console.Error.WriteLine(error.ToString());
            }

            // And under --json the same errors on stdout (AGT-3), whichever layer raised them.
            // A refusal made here in the command line was already JSON; one thrown from a command
            // - a relief of zero, a box written backwards, a destination that cannot be written -
            // left an agent an empty stream and an exit code.
            if (args.Contains("--json") && !tally.Written)
            {
                tally.Write(CommandJson.Write(new CommandLineRefusal { Errors = [.. failure.Errors] }));
            }

            // CLI-3 wants a distinct exit code per failure class, and the class is
            // in the error rather than in how it reached here. A regime violation
            // raised as a refusal is the same finding as one raised as a warning on
            // a completed run, and a caller branching on the exit code should not
            // have to know which route it took.
            return (int)(failure.Error.Code switch
            {
                ErrorCodes.RegimeInvalid => ExitCode.RegimeViolation,
                ErrorCodes.ConvergenceFailed => ExitCode.ConvergenceFailure,
                _ => ExitCode.ValidationFailure,
            });
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
        finally
        {
            Console.SetOut(stdout);
        }
    }

    /// <summary>Standard output, with a note of whether anything has been written to it.</summary>
    private sealed class StdoutTally(TextWriter inner) : TextWriter
    {
        public bool Written { get; private set; }

        public override System.Text.Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            Written = true;
            inner.Write(value);
        }

        public override void Write(string? value)
        {
            Written |= !string.IsNullOrEmpty(value);
            inner.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Written |= count > 0;
            inner.Write(buffer, index, count);
        }

        public override void Flush() => inner.Flush();
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

        if (Malformed(args, options))
        {
            return (int)ExitCode.ValidationFailure;
        }

        return args[0] switch
        {
            "init" => Init(options),
            "new" => New(options),
            "validate" => Validate(options),
            "outline" => Outline(options),
            "project" => Project(options),
            "estimate" => Estimate(options),
            "solve" => Solve(options),
            "probe" => Probe(options),
            "run" => Run(options),
            "sweep" => Sweep(options),
            "optimise" or "optimize" => Optimise(options),
            "scan" => Scan(options),
            "boundary" => Boundary(options),
            "preview" => Preview(options),
            "test" => Test(options),
            "verify" => Verify(options),
            "report" => Report(options),
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

    /// <summary>
    /// Refuses, before a known verb does anything, a flag it does not read, a value-taking flag
    /// given no value, and more positional arguments than it takes - naming each.
    /// </summary>
    /// <param name="args">The raw arguments, verb first.</param>
    /// <param name="options">The parsed command line.</param>
    /// <returns>True when the command line was refused; the reason is on stderr, and on stdout as JSON under --json.</returns>
    /// <remarks>
    /// Asked of the verb's own flags (<see cref="VerbFlags.ByVerb"/>), so a flag is called
    /// "unknown" only where that verb does not read it and "takes a value" only where it is one
    /// that verb reads as a value. A verb this does not know - an unknown command, <c>render</c>
    /// with no sub-verb - and any command line asking for <c>--help</c> are left to the verb, as
    /// they always were.
    /// </remarks>
    private static bool Malformed(string[] args, CommandLine options) =>
        CommandLineProblem(args, options) is { } refusal && !Refuse(options, refusal);

    /// <summary>
    /// What is wrong with a command line before its verb runs, or null where nothing is: a flag
    /// the verb does not read, a flag that takes a value given none, an argument too many.
    /// </summary>
    /// <param name="args">The raw arguments, verb first.</param>
    /// <returns>The refusal, located at the flag or the argument; null for a command line the verb is handed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    /// <remarks>
    /// Public so that a command line written down somewhere - in a suggestion, a usage line, a
    /// page of the documentation - can be held to what the CLI accepts: an error that said "see
    /// 'einzel schema --units'" named a flag nothing read.
    /// </remarks>
    public static EinzelError? CommandLineProblem(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return CommandLineProblem(args, CommandLine.Parse(args));
    }

    private static EinzelError? CommandLineProblem(string[] args, CommandLine options)
    {
        if (options.Has("help") || VerbFlags.For(args) is not var (key, flags))
        {
            return null;
        }

        EinzelError? refusal = null;

        foreach (var flag in options.Flags)
        {
            if (!flags.Knows(flag))
            {
                refusal = new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"/args/--{flag}",
                    Constraint = $"unknown flag --{flag} for {key}",
                    Suggestion = $"the flags {key} reads are "
                        + string.Join(", ", flags.Switches.Concat(flags.Values).Concat(flags.Optional).Concat(VerbFlags.Global).Order(StringComparer.Ordinal).Select(f => "--" + f)),
                };
                break;
            }

            if (flags.Values.Contains(flag) && options.Value(flag) is null)
            {
                refusal = new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"/args/--{flag}",
                    Constraint = $"--{flag} takes a value, and none was given (the next argument is a flag, or there is none)",
                    Suggestion = $"give --{flag} its value, as --{flag} <value>",
                };
                break;
            }
        }

        if (refusal is null && options.Positional.Count > flags.Positionals)
        {
            refusal = new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"/args/{flags.Positionals}",
                Constraint = $"{key} was given {string.Join(", ", options.Positional.Skip(flags.Positionals).Select(p => $"'{p}'"))}, which it does not take",
                Suggestion = PositionalSuggestion(key, flags),
            };
        }

        return refusal;
    }

    /// <summary>
    /// What to do about an argument a verb does not take, said in that verb's own flags.
    /// </summary>
    /// <remarks>
    /// One sentence for every verb named <c>--out</c> and <c>--no-scale</c>, which most verbs do
    /// not have: <c>einzel run m.json --vtu out.vtu</c> was told to use <c>--out</c>, which
    /// <c>run</c> then refused. The stray argument is nearly always a value given to a switch
    /// or a file given without its flag, so the suggestion names the verb's own switches and
    /// its own flags that take a value.
    /// </remarks>
    private static readonly string[] FileFlags = ["out", "csv", "input"];

    internal static string PositionalSuggestion(string key, VerbFlags flags)
    {
        static string Listed(IEnumerable<string> names) =>
            string.Join(", ", names.Order(StringComparer.Ordinal).Select(n => "--" + n));

        var switches = flags.Switches.Concat(VerbFlags.Global).ToList();
        var valued = flags.Values.Concat(flags.Optional).ToList();
        var file = FileFlags.FirstOrDefault(flags.Values.Contains);

        var text = $"{key}'s switches take no value ({Listed(switches)}), so an argument after one is a stray";

        if (valued.Count > 0)
        {
            text += $"; its flags that take a value are {Listed(valued)}";
        }

        if (file is not null)
        {
            text += $", and a file to write is --{file} <file>";
        }

        return text;
    }

    /// <summary>
    /// Refuses a command line for what one of its flags says, naming the flag.
    /// </summary>
    /// <param name="options">The parsed command line.</param>
    /// <param name="flag">The flag at fault, without its dashes.</param>
    /// <param name="constraint">What the flag takes, and what it was given, in the user's own text.</param>
    /// <param name="suggestion">What to type instead, where there is something to say.</param>
    /// <returns>Always false, so a reader can return it.</returns>
    /// <remarks>
    /// The one way a flag's value is refused, so every such refusal exits 1, is located at
    /// <c>/args/--flag</c>, and under <c>--json</c> is on stdout as JSON as well (AGT-3) - a
    /// reader that wrote its own sentence to stderr left an agent an empty stream to parse.
    /// </remarks>
    private static bool Refuse(CommandLine options, string flag, string constraint, string? suggestion = null) =>
        Refuse(
            options,
            new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"/args/--{flag}",
                Constraint = constraint,
                Suggestion = suggestion,
            });

    private static bool Refuse(CommandLine options, EinzelError refusal)
    {
        // CLI-2's split, and AGT-3's: the reason on stderr, and under --json the same error as
        // JSON on stdout, so an agent gets a machine-readable refusal and not an empty stream.
        Console.Error.WriteLine(refusal.ToString());

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(new CommandLineRefusal { Errors = [refusal] }));
        }

        return false;
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

        // The unit symbols a document may use, from the registry that reads them. An unknown
        // unit's error has always said "see the unit list from 'einzel schema --units'", and
        // there was no such list: the flag was read by nothing.
        if (options.Has("units"))
        {
            var units = Einzel.Core.Units.UnitRegistry.All
                .OrderBy(u => u.Dimension.ToString(), StringComparer.Ordinal)
                .ThenBy(u => u.SiFactor)
                .ThenBy(u => u.Symbol, StringComparer.Ordinal)
                .Select(u => new UnitJson { Symbol = u.Symbol, Dimension = u.Dimension.ToString(), SiFactor = u.SiFactor })
                .ToList();

            if (options.Has("json"))
            {
                return Emit(new UnitsOutcome { Units = units });
            }

            Console.Out.WriteLine($"{"symbol",-12} {"dimension (SI base units)",-28} one of it in SI");

            foreach (var unit in units)
            {
                Console.Out.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"{unit.Symbol,-12} {unit.Dimension,-28} {unit.SiFactor}"));
            }

            Console.Out.WriteLine("symbols are case-sensitive; the micro sign may be written µ or μ");
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

    private static int Scan(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel scan <study.json> [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var outcome = StudyCommand.Scan(studyPath, new ProjectLayout(root), options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would scan {outcome.Parameter} over {outcome.FigureOfMerit.Name} in "
                + $"{outcome.ModelPath}"));

            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.Parameter} / {outcome.Unit}      {outcome.FigureOfMerit.Name} / "
            + $"{outcome.FigureOfMerit.Unit}"));

        foreach (var point in outcome.Points)
        {
            // A point with no figure prints its reason rather than a blank. On a
            // stability scan that reason is the result - the ion was lost, and where.
            var figure = point.FigureOfMerit is { } value
                ? string.Create(invariant, $"{value,18:G8}")
                : $"{"-",18}  {Short(point.Failure)}";

            Console.Out.WriteLine(string.Create(invariant, $"{point.Value,14:G8}  {figure}"));
        }

        Console.Out.WriteLine();

        if (outcome.Nominal is { } nominal)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"nominal {nominal:G8} {outcome.FigureOfMerit.Unit} at the model's own value"));
        }

        if (outcome.Steepest is { } steepest)
        {
            // Not a boundary: where the figure moves fastest on the grid actually
            // computed, and how coarse that grid is there. ACC-6 wants one part in
            // five hundred of the scan, so the fraction is what says whether this
            // scan resolved anything.
            var change = steepest.FigureVanishes
                ? "the figure stops existing"
                : string.Create(invariant, $"the figure moves {steepest.Change:G4}");

            Console.Out.WriteLine(string.Create(
                invariant,
                $"steepest between {steepest.Low:G8} and {steepest.High:G8} {outcome.Unit}: {change}"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"  that interval is 1 part in {1.0 / Math.Max(steepest.WidthFraction, 1e-12):F0} of the "
                + $"scan; ACC-6 asks for 1 in 500"));
        }

        Warn(outcome.Warnings);

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)ExitCode.Success;
    }

    private static int Boundary(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel boundary <study.json> [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var outcome = StudyCommand.Boundary(studyPath, new ProjectLayout(root), options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would bisect for {outcome.Parameter} where {outcome.FigureOfMerit.Name} crosses "
                + $"{outcome.Threshold:G6} in {outcome.ModelPath}"));

            return (int)ExitCode.Success;
        }

        if (outcome.Boundary is { } boundary)
        {
            // The bracket, not a value with an error bar. A bisection proves
            // containment; the midpoint is a convention laid over it, and printing
            // the midpoint alone would quote the boundary more precisely than it was
            // measured.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"{outcome.Parameter,-14} {boundary.Value:G8} {outcome.Unit}, "
                + $"bracketed [{boundary.Uncertainty.Lower:G8}, {boundary.Uncertainty.Upper:G8}]"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{"where",-14} {outcome.FigureOfMerit.Name} crosses {outcome.Threshold:G6} "
                + $"{outcome.FigureOfMerit.Unit} (inside is {outcome.Inside})"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{"resolved",-14} 1 part in {1.0 / Math.Max(outcome.ResolvedFraction, 1e-12):F0} of "
                + $"the range in {outcome.Evaluations} evaluations; ACC-6 asks for 1 in 500, and a "
                + $"grid would cost 501"));

            // Whether or not the check found anything. A reader who sees the line
            // knows the single-crossing assumption was tested; one who sees nothing
            // cannot tell that from its never having been tested (REG-2).
            Console.Out.WriteLine(outcome.SecondCrossingSi is { } flip
                ? string.Create(
                    invariant,
                    $"{"but",-14} the figure crosses back at {flip:G8} SI, so this is one edge of "
                    + $"several rather than the edge of the region ({outcome.Probes} probe(s))")
                : string.Create(
                    invariant,
                    $"{"confirmed",-14} {outcome.Probes} probe(s) outside the bracket found no second "
                    + $"crossing"));
        }

        Warn(outcome.Warnings);

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return outcome.Converged ? (int)ExitCode.Success : (int)ExitCode.ConvergenceFailure;
    }

    /// <summary>The first line of a failure, for a table cell.</summary>
    private static string Short(string? failure)
    {
        if (string.IsNullOrWhiteSpace(failure))
        {
            return "no figure";
        }

        var line = failure.ReplaceLineEndings(" ").Trim();

        return line.Length <= 72 ? line : line[..69] + "...";
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

        // An example brings its own assertion, so say where it landed - otherwise
        // the file appears in tests/ with nothing having mentioned it.
        if (outcome.TestPath is { } test)
        {
            Console.Out.WriteLine(outcome.Written
                ? $"wrote {test} - run 'einzel test' to check it"
                : $"would write {test}");
        }

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

    /// <summary>Whether a file is a study rather than a model.</summary>
    /// <remarks>
    /// A study names the model it studies; a model names no model. That one property is
    /// an exact discriminator and needs no schema version, so a file from any earlier
    /// version is classified the same way.
    /// </remarks>
    private static bool IsStudy(string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out var model)
                && model.ValueKind == System.Text.Json.JsonValueKind.String;
        }
        catch (Exception failure) when (
            failure is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // Not readable or not JSON: let the model path report it properly, with
            // AGT-3's code and suggestion, rather than failing here as "not a study".
            return false;
        }
    }

    private static int Estimate(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel estimate <model.json|study.json> [--threshold <seconds>] [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var path = options.Positional[0];

        // GRD-8 asks for a *configurable* threshold and it was a constant. Somebody who
        // knows their study is worth an hour needs a way to say so; without one the
        // observed response was to make the study smaller.
        var threshold = EstimateCommand.ThresholdSeconds;

        if (!TryNumberFlag(options, "threshold", "a positive number of seconds", out var seconds))
        {
            return (int)ExitCode.ValidationFailure;
        }

        if (seconds is { } declared)
        {
            if (declared <= 0.0)
            {
                Refuse(options, "threshold", $"--threshold takes a positive number of seconds, and '{options.Value("threshold")}' is not a positive number of seconds");
                return (int)ExitCode.ValidationFailure;
            }

            threshold = declared;
        }

        // Calibration is on unless refused: an estimate is worth having about the
        // machine that will do the work. --no-calibrate keeps PERF-8's cold-start
        // budget, which a pilot solve on a volume geometry does not.
        var calibrate = !options.Has("no-calibrate");

        // A study is costed as a study. Detected rather than declared by a flag,
        // because the file already says which it is and a flag would let the two
        // disagree - the cost of getting that wrong is a plan short by a factor of
        // the evaluation count.
        var outcome = IsStudy(path)
            ? EstimateCommand.ForStudy(path, calibrate, threshold)
            : EstimateCommand.Execute(path, calibrate, threshold);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        foreach (var element in outcome.Elements)
        {
            // Any rank, not just two. A volume element used to fall through to
            // "analytic" here - which is not a shape, it is a claim that there is
            // nothing to solve, printed beside the largest solve in the model.
            var size = element.Nodes.Count > 0
                ? string.Join("x", element.Nodes)
                : "analytic";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"field {element.Index}  {element.Type,-14} {size,-12} {element.Seconds,8:F2} s  {EstimateCommand.MemorySize(element.MemoryMiB),11}"));
        }

        if (outcome.Study is { } study)
        {
            var flown = study.Members == 1 ? "trajectory" : "trajectories";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"per eval      {study.SecondsPerEvaluation:F2} s  (one solve, {study.Members} {flown})"));

            var bound = study.EvaluationsAreACeiling ? " at most" : string.Empty;

            Console.Out.WriteLine(string.Create(
                invariant, $"{study.Kind,-13} {study.Evaluations} evaluations{bound}"));

            // Beside the count, because the time multiplies by that and the memory by this.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"in flight     {study.InFlight} at once, {EstimateCommand.MemorySize(study.MemoryMiBPerEvaluation)} each"));
        }

        Console.Out.WriteLine(string.Create(
            invariant, $"total         {EstimateCommand.Duration(outcome.Seconds)}, peak {EstimateCommand.MemorySize(outcome.MemoryMiB)}"));

        Console.Out.WriteLine();
        Console.Out.WriteLine($"basis: {outcome.Basis}");

        if (outcome.AboveThreshold)
        {
            // GRD-8: above the threshold this is a refusal to proceed *silently*, not a
            // refusal to proceed. `sweep` and the other study verbs run whatever this says;
            // what the gate buys is that the cost was seen first.
            //
            // Saying only "this is above the threshold" and exiting on a code named
            // cost-gate refusal reads as a prohibition, and an agent in the acceptance run
            // obeyed it: it cut a 2000-draw study to 1500 to get past a number that was
            // itself 26x too high. A gate that makes somebody shrink their work is doing
            // the opposite of its job, so it now says what it means and how to move it.
            var limit = string.Create(invariant, $"{outcome.ThresholdSeconds:F0}");

            Console.Error.WriteLine(
                $"this is above the {limit} s cost threshold, which is why this exits 3. "
                + "The study itself is not blocked - sweep, scan, boundary and optimise will "
                + "run it. Pass --threshold <seconds> to say the cost is expected");

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

        // No empty case to print: SolveCommand refuses a model with nothing to solve
        // rather than returning an empty list, and a branch here answering exit 0 for
        // one would disagree with --json, whose Converged is false over no elements.

        // A driven element is one solve per basis channel, and --json says which is
        // which. Here each channel prints its own block, so without its number two
        // channels of one element read as the same solve printed twice, and a
        // "did not converge" cannot say which spatial pattern failed. The number is
        // the one --json carries, counted from zero. A static element has a single
        // channel and prints no number, since there is nothing to tell apart.
        var channels = outcome.Elements
            .GroupBy(e => e.Index)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var element in outcome.Elements)
        {
            var label = channels[element.Index] > 1
                ? string.Create(invariant, $"field {element.Index} channel {element.Channel}")
                : string.Create(invariant, $"field {element.Index}");

            var shape = !element.SquareCells ? "stretched"
                : element.Dimensions == 3 ? "cubic"
                : "square";

            // Every axis the element has, not the first two. A volume printed as its
            // x and y alone reads as a plane: a 257x17x257 solve reported as 257x17
            // understates the node count by the whole third axis, and drops the z
            // spacing that says whether the cells are the size that was asked for.
            var nodes = string.Join("x", element.Nodes.Select(n => n.ToString(invariant)));
            var spacing = string.Join(
                " x ", element.SpacingMm.Select(s => s.ToString("F4", invariant)));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{label}  {nodes} at {spacing} mm ({shape})"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Electrodes} electrode(s), {element.FixedNodes} fixed node(s), "
                + $"{element.CutLinks} cut link(s)"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Cycles} cycles at factor {element.ConvergenceFactor:F4}, "
                + $"residual {element.RelativeResidual:E2} of initial"));

            // A cycle is not a unit of work, and it reads as one. At zero levels the
            // V-cycle never coarsened and the whole solve was relaxation on the finest
            // grid, which is a different answer to "how did it go" than a factor
            // alone suggests.
            var descent = element.Levels == 0
                ? "  <- not multigrid: it never coarsened"
                : string.Empty;

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Levels} coarse level(s), {element.Sweeps:N0} sweep(s), "
                + $"coarsest {element.CoarsestNodes:N0} node(s){descent}"));

            Console.Out.WriteLine(string.Create(
                invariant, $"          peak |phi| {element.PeakPotentialVolts:G6} V"));

            if (!element.Converged)
            {
                Console.Error.WriteLine($"{label} did not converge");
            }
        }

        Console.Out.WriteLine(string.Create(invariant, $"solved in {outcome.ElapsedMs:F0} ms"));

        Warn(outcome.Warnings);

        return (int)(outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
    }


    private const string ProbeUsage =
        "usage: einzel probe <model.json> --at-mm x,y,z [--at-us <t>] [--json]\n"
        + "       einzel probe <model.json> --line-mm x,y,z --to-mm x,y,z [--samples N] [--at-us <t>]\n"
        + "                                 [--csv <file>] [--dry-run] [--json]\n"
        + "the potential and electric field of the field a run flies through, at one point or along a\n"
        + "line (both ends included, N >= 2, 21 by default)\n"
        + "  --at-mm / --line-mm / --to-mm  coordinates in millimetres: the unit is in the flag's name\n"
        + "  --at-us  the instant, in microseconds, for a driven or sequenced field (it is the field at\n"
        + "           t = 0 and says so when not given)\n";

    private static bool TryVec3(string? text, out Core.Geometry.Vec3 value)
    {
        value = default;

        if (!TryNumbers(text, 3, out var v))
        {
            return false;
        }

        value = new Core.Geometry.Vec3(v[0], v[1], v[2]);
        return true;
    }

    /// <summary>Reads exactly <paramref name="count"/> comma-separated numbers, in the invariant culture.</summary>
    /// <remarks>One parser for a point, a rectangle and a box, so the three cannot come to read a number differently.</remarks>
    private static bool TryNumbers(string? text, int count, out double[] values)
    {
        values = [];

        if (text is null)
        {
            return false;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != count)
        {
            return false;
        }

        var parsed = new double[count];

        for (var i = 0; i < count; i++)
        {
            if (!TryNumber(parts[i], out parsed[i]))
            {
                return false;
            }
        }

        values = parsed;
        return true;
    }

    /// <summary>
    /// One number as a command line writes it: the invariant culture, and finite.
    /// </summary>
    /// <remarks>
    /// The only place this file parses a real number, and <see cref="TryWhole"/> the only place
    /// it parses a whole one (<c>FlagValuesAreReadByTheReadersTests</c> holds it to that): a
    /// <c>Parse</c> beside them throws a typo through to the handler for defects in the engine,
    /// exit 6, and a second <c>TryParse</c> is a second rule for what a number looks like.
    /// "NaN" and "Infinity" parse and are no measurement, so they are refused with the typos.
    /// </remarks>
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>
    /// One whole number as a command line writes it: digits and nothing else, within a range.
    /// </summary>
    /// <remarks>
    /// Digits alone - no sign, no space, no exponent, no separator. No whole-number flag here has
    /// a negative in its domain (each is a count or a size), so a sign is a typo; and "+241" or
    /// " 1600" accepted is a second spelling of a number nobody documented.
    /// </remarks>
    private static bool TryWhole(string text, int minimum, int maximum, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value >= minimum
        && value <= maximum;

    private static string WholeRange(int minimum, int maximum) =>
        maximum == int.MaxValue
            ? string.Create(CultureInfo.InvariantCulture, $"{minimum} or more")
            : string.Create(CultureInfo.InvariantCulture, $"from {minimum} to {maximum}");

    /// <summary>
    /// Reads an optional numeric flag. False, with the reason on stderr, when it was given and
    /// is not a number, or given with no value; true with a null value when it was not given.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <param name="flag">The flag, without its dashes.</param>
    /// <param name="what">What it takes, as the message says it: "a number", "a time in microseconds".</param>
    /// <param name="value">The number, or null when the flag is absent.</param>
    /// <param name="suggestion">What to type instead, where there is something to say.</param>
    private static bool TryNumberFlag(CommandLine options, string flag, string what, out double? value, string? suggestion = null)
    {
        value = null;

        if (options.Value(flag) is not { } text)
        {
            return true;
        }

        if (TryNumber(text, out var parsed))
        {
            value = parsed;
            return true;
        }

        return Refuse(options, flag, $"--{flag} takes {what}, and '{text}' is not one.", suggestion);
    }

    /// <summary>
    /// Reads an optional whole-number flag: digits alone, within the flag's own range.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <param name="flag">The flag, without its dashes.</param>
    /// <param name="otherwise">The value when the flag is absent.</param>
    /// <param name="minimum">The least it may be: 0 for a count that may be none, 1 for one that may not.</param>
    /// <param name="maximum">The most it may be.</param>
    /// <param name="value">The number, or <paramref name="otherwise"/>.</param>
    /// <param name="what">What it counts, as the message says it: "a whole number of pixels".</param>
    private static bool TryWholeFlag(
        CommandLine options, string flag, int otherwise, int minimum, int maximum, out int value, string what = "a whole number")
    {
        value = otherwise;

        if (options.Value(flag) is not { } text)
        {
            return true;
        }

        if (TryWhole(text, minimum, maximum, out var parsed))
        {
            value = parsed;
            return true;
        }

        return Refuse(
            options,
            flag,
            $"--{flag} takes {what} {WholeRange(minimum, maximum)}, written as digits alone, and '{text}' is not one.");
    }

    /// <summary>
    /// Reads a flag that is one of a few words. False, refused by name with the words it takes,
    /// when it was given anything else; true with a null value when it was not given.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <param name="flag">The flag, without its dashes.</param>
    /// <param name="choices">The words it takes, as documented.</param>
    /// <param name="value">The word as documented, whatever case it was typed in; null when absent.</param>
    /// <param name="exact">True where case matters, as it does for a view or a plane.</param>
    /// <remarks>
    /// A flag that fell back to a default on an unknown word - <c>--format pfd</c> drew an SVG,
    /// <c>--kind objectiv</c> registered an objective, <c>--vcs hg</c> made no repository - said
    /// nothing about having ignored what was typed.
    /// </remarks>
    private static bool TryChoiceFlag(
        CommandLine options, string flag, IEnumerable<string> choices, out string? value, bool exact = false)
    {
        value = null;

        if (options.Value(flag) is not { } text)
        {
            return true;
        }

        var comparison = exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        value = choices.FirstOrDefault(c => string.Equals(c, text, comparison));

        return value is not null
            || Refuse(options, flag, $"--{flag} is one of {string.Join(", ", choices)}, and '{text}' is none of them.");
    }

    /// <summary>
    /// <c>--samples N</c> along the longer side, or <c>--samples NxM</c> for the longer side and
    /// then the shorter.
    /// </summary>
    private static bool TrySurfaceSamples(CommandLine options, out int samples, out int? deep)
    {
        samples = SurfaceCommand.DefaultSamples;
        deep = null;

        if (options.Value("samples") is not { } text)
        {
            return true;
        }

        var parts = text.Split('x');

        // How it is written, and no more: how many samples a surface may have is the surface's
        // own rule (SurfaceCommand.Check), stated there once.
        if (parts.Length is 1 or 2
            && TryWhole(parts[0], 0, int.MaxValue, out var longer))
        {
            samples = longer;

            if (parts.Length == 1)
            {
                return true;
            }

            if (TryWhole(parts[1], 0, int.MaxValue, out var shorter))
            {
                deep = shorter;
                return true;
            }
        }

        return Refuse(
            options,
            "samples",
            $"--samples takes a whole number, or NxM for the longer side and then the shorter, written as digits alone, and '{text}' is neither.");
    }

    /// <summary>The pixel size of a raster picture, from <c>--width-px</c> and <c>--height-px</c>.</summary>
    /// <param name="options">The command line.</param>
    /// <param name="what">What is being drawn, for the message: "a still", "a surface picture".</param>
    /// <param name="width">Width in pixels, 1600 unless given.</param>
    /// <param name="height">Height in pixels, 1000 unless given.</param>
    private static bool TryPictureSize(CommandLine options, string what, out int width, out int height)
    {
        height = 0;
        var pixels = $"a whole number of pixels ({what} is between 16 and 8192 pixels on each side)";

        return TryWholeFlag(options, "width-px", 1600, 16, 8192, out width, pixels)
            && TryWholeFlag(options, "height-px", 1000, 16, 8192, out height, pixels);
    }

    private static int Probe(CommandLine options)
    {
        // The bare spellings are refused by name rather than ignored: a number with no unit is the
        // ambiguity this project refuses everywhere else, and an agent that types --at 5,0,0 has
        // to be told which unit the flag wants, not given a silent default.
        foreach (var bare in new[] { "at", "line", "to" })
        {
            if (options.Has(bare))
            {
                Refuse(options, bare, $"--{bare} has no unit.", $"Say it in the flag's name: --{bare}-mm x,y,z (millimetres).");
                return (int)ExitCode.ValidationFailure;
            }
        }

        var invariant = CultureInfo.InvariantCulture;
        var hasPoint = options.Has("at-mm");
        var hasLine = options.Has("line-mm");

        if (options.Positional.Count == 0 || hasPoint == hasLine)
        {
            Console.Error.Write(ProbeUsage);
            return (int)ExitCode.ValidationFailure;
        }

        var fromFlag = hasPoint ? "at-mm" : "line-mm";

        if (!TryVec3(options.Value(fromFlag), out var from))
        {
            Refuse(options, fromFlag, $"--{fromFlag} takes three numbers, x,y,z, in millimetres (no spaces), and '{options.Value(fromFlag)}' is not that.");
            return (int)ExitCode.ValidationFailure;
        }

        Core.Geometry.Vec3? to = null;
        var samples = 21;

        if (hasLine)
        {
            if (!TryVec3(options.Value("to-mm"), out var end))
            {
                Refuse(
                    options,
                    "to-mm",
                    options.Value("to-mm") is { } given
                        ? $"--to-mm takes three numbers, x,y,z, in millimetres (no spaces), and '{given}' is not that."
                        : "--line-mm needs --to-mm x,y,z, the other end, in millimetres.");
                return (int)ExitCode.ValidationFailure;
            }

            to = end;

            if (!TryWholeFlag(options, "samples", samples, 2, ProbeCommand.MaximumSamples, out samples, "a whole number of samples, both ends included,"))
            {
                return (int)ExitCode.ValidationFailure;
            }
        }
        else if (options.Has("to-mm") || options.Has("samples"))
        {
            Refuse(options, options.Has("to-mm") ? "to-mm" : "samples", "--to-mm and --samples describe a line; a point takes only --at-mm.");
            return (int)ExitCode.ValidationFailure;
        }

        if (!TryNumberFlag(options, "at-us", "a time in microseconds", out var at))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = ProbeCommand.Execute(options.Positional[0], new ProbeRequest(from, to, samples, at));

        if (options.Value("csv") is { } csv)
        {
            var target = Path.GetFullPath(csv);

            if (!options.Has("dry-run"))
            {
                ArtifactFiles.WriteText("csv", target, ProbeCommand.ToCsv(outcome), options.Positional[0]);
            }

            Console.Error.WriteLine(options.Has("dry-run") ? $"would write {target}" : $"wrote {target}");
        }

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        if (outcome.InstantUs is { } t)
        {
            Console.Out.WriteLine(string.Create(invariant, $"field at t = {t:G6} us"));
        }

        // B columns only where the model declares a magnet, which adds no phi and no E.
        var magnetic = outcome.Samples.Any(s => s.FluxDensityT is not null);

        Console.Out.WriteLine(
            "  #       x_mm       y_mm       z_mm     phi_V        Ex_V/m        Ey_V/m        Ez_V/m   "
            + (magnetic ? "      Bx_T          By_T          Bz_T   " : string.Empty) + "status");

        foreach (var s in outcome.Samples)
        {
            var phi = s.PotentialV is { } p ? p.ToString("G8", invariant) : "-";
            var e = s.FieldVPerM;
            var bt = s.FluxDensityT;
            var bColumns = !magnetic
                ? string.Empty
                : string.Create(
                    invariant,
                    $"{(bt is null ? "-" : bt[0].ToString("G6", invariant)),10} "
                    + $"{(bt is null ? "-" : bt[1].ToString("G6", invariant)),13} "
                    + $"{(bt is null ? "-" : bt[2].ToString("G6", invariant)),13}   ");
            var label = s.Status
                + (s.Conductor is not null && s.Status == ProbeStatus.InsideConductor
                    ? " (" + s.Conductor + ")"
                    : string.Empty)
                + (s.Note is null ? string.Empty : "  " + s.Note);

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{s.Index,3} {s.PositionMm[0],10:G6} {s.PositionMm[1],10:G6} {s.PositionMm[2],10:G6} "
                + $"{phi,12} {(e is null ? "-" : e[0].ToString("G6", invariant)),13} "
                + $"{(e is null ? "-" : e[1].ToString("G6", invariant)),13} "
                + $"{(e is null ? "-" : e[2].ToString("G6", invariant)),13}   "
                + $"{bColumns}{label}"));
        }

        // GRD-2: warnings reach this surface too.
        Warn(outcome.Warnings);

        return (int)ExitCode.Success;
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
            return Emit(
                outcome,
                outcome.Completed ? ExitCode.Success : ExitCode.ConvergenceFailure);
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

        return (int)(outcome.Completed ? ExitCode.Success : ExitCode.ConvergenceFailure);
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

    /// <summary>What a project holds, and the state of each part of it.</summary>
    /// <remarks>
    /// <para>
    /// A peer of <c>verify</c> rather than a replacement for it. Verify answers "do the
    /// stored results still stand" and walks the manifests to do it, which makes it
    /// structurally blind to a model nobody has run - the state most models in a working
    /// project are in.
    /// </para>
    /// <para>
    /// Exit code follows the same rule verify uses: a drifted result is a project that has
    /// moved on rather than an engine failure, and a model that does not validate is a
    /// thing to fix. A model that has simply never been run is neither, so it does not
    /// change the exit code.
    /// </para>
    /// </remarks>
    private static int Project(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = ProjectCommand.Execute(root);

        var wrong = outcome.Models.Count(m => !m.Valid) + outcome.Drifted;
        var code = wrong == 0 ? ExitCode.Success : ExitCode.ValidationFailure;

        if (options.Has("json"))
        {
            // Warnings go out in the wire form every other verb uses: the severity as a
            // name and GRD-3's suppressibility spelled out. Serialising the engine's own
            // record here would emit the severity as a bare integer and drop
            // `suppressible` entirely - a reader could not tell a note from a violation,
            // which is the one thing GRD-3 is about.
            //
            // The outcome keeps the engine record rather than the wire one because the
            // shell reads it too, and a property typed from Einzel.Io would pull the file
            // format into the window (UI-1). Same reason ResultsCommand has FigureEnvelope.
            return Emit(
                new
                {
                    outcome.Root,
                    outcome.Models,
                    outcome.Studies,
                    outcome.Figures,
                    outcome.Tests,
                    outcome.ExtensionNames,
                    outcome.Orphans,
                    outcome.NeverRun,
                    outcome.Drifted,
                    Warnings = outcome.Warnings.Select(w => new Io.WarningJson
                    {
                        Code = w.Code,
                        Message = w.Message,
                        Severity = w.Severity.ToString(),
                        Suppressible = w.IsSuppressible,
                    }).ToList(),
                },
                code);
        }

        Console.Out.WriteLine(outcome.Root);
        Console.Out.WriteLine();

        if (outcome.Models.Count == 0)
        {
            Console.Out.WriteLine("no models");
        }

        foreach (var model in outcome.Models)
        {
            // Three states, kept apart because they call for different things: invalid is
            // a thing to fix, stale is a thing to re-run, and never-run is neither - it is
            // simply where a model starts.
            var mark =
                !model.Valid ? "INVALID"
                : !model.Ran ? "not run"
                : model.Current ? "ok     "
                : "STALE  ";

            var stream = model.Valid && (model.Current || !model.Ran)
                ? Console.Out
                : Console.Error;

            stream.WriteLine(
                $"{mark} {model.Path}"
                + (model.TransportMode is { } mode and not "trajectory" ? $"  [{mode}]" : string.Empty));

            if (model.Problem is { } why)
            {
                Console.Error.WriteLine($"        {why}");
            }

            foreach (var drift in model.Drift)
            {
                Console.Error.WriteLine($"        {drift}");
            }

            foreach (var note in model.Notes)
            {
                Console.Out.WriteLine($"        note: {note}");
            }
        }

        foreach (var orphan in outcome.Orphans)
        {
            Console.Error.WriteLine($"ORPHAN  {orphan.Manifest} - {orphan.Model}");
        }

        Console.Out.WriteLine();

        foreach (var (label, items) in new[]
        {
            ("studies", outcome.Studies),
            ("figures", outcome.Figures),
            ("tests", outcome.Tests),
            ("extensions", outcome.ExtensionNames),
        })
        {
            if (items.Count > 0)
            {
                Console.Out.WriteLine($"{items.Count} {label}: {string.Join(", ", items)}");
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"{outcome.Models.Count} models, {outcome.NeverRun} never run, "
            + $"{outcome.Drifted} with a result that no longer stands");

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)code;
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

    /// <summary>An account of a project's runs, for a person to read (Amendment 43).</summary>
    /// <remarks>
    /// <para>
    /// A verb rather than a shell view, so AGT-2 holds: nothing exists only in the window,
    /// and an agent asking what a night of runs produced gets the same account through
    /// <c>--json</c> that a person gets as a page.
    /// </para>
    /// <para>
    /// <b>The page is written and not opened.</b> Launching a browser would make this the
    /// one verb with a side effect outside the project, and a report generated on a
    /// headless machine over ssh is exactly the case that wants the file and not the
    /// window.
    /// </para>
    /// </remarks>
    private static int Report(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = ReportCommand.Execute(root);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        // The page goes at the project root rather than into results/, and that is not
        // cosmetic: results/ is what `verify` walks and what this reads, so a report
        // written there would become an input to the next one.
        var target = options.Value("out")
            ?? Path.Combine(outcome.Root, "report.html");

        var page = ReportPage.Write(outcome, Path.GetFileName(outcome.Root.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine(
                $"would write {target} ({outcome.Runs.Count} run(s), {page.Length:N0} bytes)");

            return (int)ExitCode.Success;
        }

        // A named directory is created rather than refused, since `--out reports/tonight.html`
        // is the obvious way to keep a series of them and failing on the first is a poor
        // answer. The default target's directory is the project root and always exists.
        ArtifactFiles.WriteText("out", target, page);

        foreach (var run in outcome.Runs)
        {
            var mark = run.Unreadable is not null ? "BROKEN"
                : run.NotRendered is not null ? "study"
                : run.Result is null ? "NOANSWER"
                : run.Current ? "ok" : "STALE";

            // A study's answer is not a fault, so it does not go to stderr with the ones
            // that need doing something about.
            var stream = mark is "ok" or "study" ? Console.Out : Console.Error;

            stream.WriteLine(
                $"{mark,-9}{run.Manifest,-46}{run.Numbers.Count,3} number(s), "
                + $"{run.Warnings.Count} warning(s)"
                // Only where there is a timeline, since a beamline has none and a count of
                // zero phases on every ordinary run is the kind of line a reader learns to
                // skip past - taking the ones that mean something with it.
                + (run.Phases.Count > 0 ? $", {run.Phases.Count} phase(s)" : ""));
        }

        // Diagnostics on stderr and the result on stdout (CLI-2), so a caller piping this
        // gets the path and nothing else.
        if (outcome.Runs.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"{outcome.Current} of {outcome.Runs.Count} run(s) still stand"
                + (outcome.WithoutResult > 0
                    ? $"; {outcome.WithoutResult} stored no result document"
                    : "")
                + (outcome.Unreadable > 0
                    ? $"; {outcome.Unreadable} stored one this build cannot read"
                    : "")
                + (outcome.NotRendered > 0
                    ? $"; {outcome.NotRendered} study answer(s) this page does not draw"
                    : "")
                + (outcome.Interrupted > 0
                    ? $"; {outcome.Interrupted} did not finish, or has not yet"
                    : ""));
        }

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        Console.Out.WriteLine(target);

        return (int)ExitCode.Success;
    }

    private static int Export(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel export <model.json> [--mesh | --composite] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("writes the solved potential field as VTK ImageData for ParaView");
            Console.Error.WriteLine("  --mesh  write the conductor surfaces as Wavefront OBJ instead,");
            Console.Error.WriteLine("          one named object per electrode, for any 3D renderer");
            Console.Error.WriteLine("  --composite  sample the field a run flies through (all elements summed) on a");
            Console.Error.WriteLine("          regular grid and write potential and E together, at one instant:");
            Console.Error.WriteLine("          [--box-mm x0,y0,z0,x1,y1,z1] [--step-mm s] [--at-us t]");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(modelPath);
        var layout = new ProjectLayout(root);
        var dryRun = options.Has("dry-run");

        // A mesh and a field volume are different artifacts about the same model, so this
        // is a choice of what to export rather than of how - which is why it is a flag on
        // 'export' and not a verb of its own.
        ExportOutcome outcome;

        if (options.Has("composite"))
        {
            Core.Geometry.Vec3? boxMin = null;
            Core.Geometry.Vec3? boxMax = null;

            if (options.Value("box-mm") is { } box)
            {
                if (!TryNumbers(box, 6, out var v))
                {
                    Refuse(options, "box-mm", $"--box-mm takes six numbers, x0,y0,z0,x1,y1,z1, in millimetres (no spaces), and '{box}' is not that.");
                    return (int)ExitCode.ValidationFailure;
                }

                boxMin = new Core.Geometry.Vec3(v[0], v[1], v[2]);
                boxMax = new Core.Geometry.Vec3(v[3], v[4], v[5]);
            }

            if (!TryNumberFlag(options, "step-mm", "a number of millimetres", out var step)
                || !TryNumberFlag(options, "at-us", "a number of microseconds", out var instant))
            {
                return (int)ExitCode.ValidationFailure;
            }

            outcome = ExportCommand.Composite(modelPath, layout, boxMin, boxMax, step, instant, dryRun);
        }
        else
        {
            outcome = options.Has("mesh")
                ? ExportCommand.Mesh(modelPath, layout, dryRun)
                : ExportCommand.Vtu(modelPath, layout, dryRun);
        }

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine(outcome.Written ? $"wrote {artifact}" : $"would write {artifact}");
        }

        if (outcome.Grid is { } grid)
        {
            Console.Out.WriteLine(grid);
        }

        // The files carry these in their headers; the terminal says them too, on stderr, so
        // somebody who never opens the header in a text editor still hears them (GRD-2).
        Warn(outcome.Warnings);

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Draws a model, headlessly: vector sections and animations, and raster stills.
    /// </summary>
    /// <remarks>
    /// <c>still</c> was named here and refused for as long as nothing rasterized, because
    /// "not built yet" and "you spelled it wrong" are different problems. It is built now,
    /// and it draws what the interactive viewport draws.
    /// </remarks>
    private static int Render(string[] args, CommandLine options)
    {
        var kind = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : null;

        if (kind is "still")
        {
            return Still(options);
        }

        if (kind is "animation")
        {
            return Animation(options);
        }

        if (kind is "surface")
        {
            return Surface(options);
        }

        var positional = kind is null ? options.Positional : options.Positional.Skip(1).ToList();

        if (kind is not (null or "section") || positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render section <model.json | figures/spec.json> [--out <file>] "
                + "[--at-us <t>] [--progress <seconds>]");
            Console.Error.WriteLine(
                "       [--format svg|pdf] [--equipotentials N] [--width-mm W] [--no-trajectory]");
            Console.Error.WriteLine(
                "       [--caption <text>] [--at-us <t>] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("draws a plane through the instrument as line work");
            Console.Error.WriteLine(
                "  --at-us  the instant to draw a driven field, or a diffusive density, at");
            Console.Error.WriteLine(
                "  --width-mm  the page width in millimetres of paper (160 is a page, 80 a column), not a window onto the model");

            return (int)ExitCode.ValidationFailure;
        }

        // TryParse rather than Parse: "5us" is the caller's mistake, and Parse threw it through
        // to the handler for defects in the engine, exit 6.
        if (!TryNumberFlag(options, "at-us", "a time in microseconds", out var sectionAt)
            || !TryNumberFlag(options, "width-mm", "a width in millimetres", out var sectionWidth)
            || !TryWholeFlag(options, "equipotentials", 0, 0, 1000, out var sectionLevels, "a whole number of contour levels")
            || !TryChoiceFlag(options, "format", FormatNames, out var sectionFormat))
        {
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
            Format = FormatOf(sectionFormat, spec.Format),
            WidthMm = sectionWidth ?? spec.WidthMm,
            Equipotentials = options.Has("equipotentials") ? sectionLevels : spec.Equipotentials,
            Trajectory = !options.Has("no-trajectory") && spec.Trajectory,
            Caption = options.Value("caption") ?? spec.Caption,

            // The unit is in the flag's name, which is how --width-mm already does it.
            // A bare --at would be ambiguous between microseconds and seconds by a
            // factor of a million, and this is the same rule that makes {"energy": 4000}
            // a validation error in a model document.
            AtSeconds = sectionAt is { } at ? at * 1e-6 : spec.AtSeconds,
            InstantFromFlag = sectionAt is not null,
        };

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        if (!TryProgress(options, out var sectionProgress))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = RenderCommand.Section(
            modelPath, new ProjectLayout(root), spec, options.Value("out"), options.Has("dry-run"),
            sectionProgress);

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

    /// <summary>Draws a model to a PNG, as the interactive viewport would show it.</summary>
    /// <remarks>
    /// <para>
    /// The pixel sizes carry their unit in the flag's name, the way <c>--width-mm</c> and
    /// <c>--at-us</c> do: a bare <c>--width</c> beside a section's millimetres would be one
    /// word meaning two things in one verb.
    /// </para>
    /// <para>
    /// Warnings go to stderr (CLI-2) and into the PNG itself (GRD-2), and a validity
    /// violation also puts a hatched band across the bottom of the picture (RND-11).
    /// </para>
    /// </remarks>
    private static int Still(CommandLine options)
    {
        var positional = options.Positional.Skip(1).ToList();

        if (positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render still <model.json> [--out <file.png>] "
                + "[--view " + string.Join("|", ViewportPicture.Views.Keys) + "]");
            Console.Error.WriteLine(
                "       [--width-px N] [--height-px N] [--see-through] [--no-scale] [--dry-run] [--json]");

            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = positional[0];
        if (!TryChoiceFlag(options, "view", ViewportPicture.Views.Keys, out var named, exact: true))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var view = named ?? "iso";

        if (!TryPictureSize(options, "a still", out var width, out var height))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        var outcome = StillCommand.Execute(
            modelPath,
            new ProjectLayout(root),
            view,
            width,
            height,
            options.Has("see-through"),
            options.Value("out"),
            options.Has("dry-run"),
            scale: !options.Has("no-scale"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written ? $"wrote {outcome.Artifact}" : $"would write {outcome.Artifact}");

        Console.Out.WriteLine(
            $"{outcome.Width} by {outcome.Height} px, {outcome.View} view"
            + (outcome.SeeThrough ? ", conductors translucent" : string.Empty)
            + $"; {outcome.Electrodes} electrodes, {outcome.FieldLevels} equipotential levels, "
            + $"{outcome.Trajectories} trajectories, {outcome.DensityShells} density contours"
            + (outcome.ScaleMm is { } arm
                ? $"; ruler arms {ScaleIndicator.Format(arm)} along {string.Join(", ", outcome.ScaleAxes)}, {outcome.ScaleCorner}"
                : string.Empty)
            + (outcome.Tainted ? "; marked with a hatched band for a validity violation" : string.Empty));

        // GRD-2: onto stderr, so a warning is not lost in a pipe that keeps stdout.
        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>The usage of <c>einzel render surface</c>.</summary>
    private const string SurfaceUsage =
        """
        usage: einzel render surface <model.json> [--plane xy|xz|yz] [--offset-mm d]
               [--box-mm a0,b0,a1,b1] [--samples N|NxM] [--mesh-lines N]
               [--height potential|energy] [--relief F] [--height-mm-per-v X | --height-mm-per-ev X]
               [--at-us t] [--cycle-mean] [--view iso|side|front|top]
               [--width-px N] [--height-px N] [--out <file.png>] [--csv <file>] [--no-scale] [--dry-run] [--json]
        draws the potential on a plane as height above it (SIMION's potential-energy view)
          --plane        the plane; a cross-section or axisymmetric model defaults to its own (xy),
                         a volume to the plane through its middle normal to its thinnest side
          --offset-mm    how far along the plane's normal; by default the middle of the domain
                         (z = 0 for a 2-D model's own xy plane)
          --box-mm       the rectangle sampled, in the plane's own axis order (for xz: x0,z0,x1,z1)
          --samples      along the longer side (241), or NxM for the longer side and then the
                         shorter; by default the shorter gets square cells, and never fewer than 61
          --height       potential (volts, the default) or energy: q*phi for the model's ion, in eV
          --relief       the drawn height of the whole range as a fraction of the longer side (0.35)
          --at-us        the instant to draw a driven or sequenced field at
          --cycle-mean   the potential averaged over one drive cycle: for a sinusoidal drive, the DC;
                         refused for a field with no drive, which has no cycle to average
          --no-scale     leave the vertical scale off the picture (it is also left off, and
                         reported absent, in --view top and where a small picture has no room)
          --csv          also write the sampled values, one row per node

        """;

    /// <summary>Draws a model's potential on a plane as a height field, to a PNG.</summary>
    /// <remarks>
    /// Every number carries its unit in the flag's name, as <c>probe</c> does; the height scale
    /// is in the PNG's text, in <c>--json</c>, and on the picture.
    /// </remarks>
    private static int Surface(CommandLine options)
    {
        var positional = options.Positional.Skip(1).ToList();

        if (positional.Count == 0)
        {
            Console.Error.Write(SurfaceUsage);
            return (int)ExitCode.ValidationFailure;
        }

        foreach (var bare in new[] { "offset", "box", "at", "height-mm" })
        {
            if (options.Has(bare))
            {
                Refuse(options, bare, $"--{bare} has no unit; see the usage for the spelling with the unit in its name.");
                Console.Error.Write(SurfaceUsage);
                return (int)ExitCode.ValidationFailure;
            }
        }

        var invariant = CultureInfo.InvariantCulture;

        if (!TryNumberFlag(options, "offset-mm", "a number of millimetres", out var offset)
            || !TryNumberFlag(options, "relief", "a number", out var relief)
            || !TryNumberFlag(options, "height-mm-per-v", "a number of millimetres per volt", out var perVolt)
            || !TryNumberFlag(options, "height-mm-per-ev", "a number of millimetres per electronvolt", out var perEv)
            || !TryNumberFlag(options, "at-us", "a time in microseconds", out var at)
            || !TrySurfaceSamples(options, out var samples, out var deepSamples)
            || !TryWholeFlag(options, "mesh-lines", SurfaceCommand.DefaultMeshLines, 0, int.MaxValue, out var meshLines, "a whole number of mesh lines")
            || !TryPictureSize(options, "a surface picture", out var width, out var height)
            || !TryChoiceFlag(options, "height", SurfaceHeight.Spellings, out var heightGiven, exact: true)
            || !TryChoiceFlag(options, "view", SurfaceCommand.Views.Keys, out var named, exact: true))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var heightKind = SurfaceHeight.Parse(heightGiven ?? SurfaceHeight.Spellings[0]);

        // Two ways of saying how tall, and the usage offers them as alternatives: with an
        // explicit scale the relief changed nothing and said nothing.
        if (relief is not null && (perVolt ?? perEv) is not null)
        {
            var scaleFlag = perVolt is not null ? "height-mm-per-v" : "height-mm-per-ev";

            Refuse(
                options,
                "relief",
                $"--relief and --{scaleFlag} both say how tall the surface is drawn, and only one can: with --{scaleFlag} the relief would be ignored.",
                $"give --relief, a fraction of the longer side, or --{scaleFlag}, an explicit scale - not both");
            return (int)ExitCode.ValidationFailure;
        }

        // The unit of an explicit scale must match what the height is, or it is the ambiguity
        // a unit in the flag's name exists to prevent.
        if ((perVolt is not null && heightKind == SurfaceHeight.Energy)
            || (perEv is not null && heightKind == SurfaceHeight.Potential))
        {
            Refuse(
                options,
                perVolt is not null ? "height-mm-per-v" : "height-mm-per-ev",
                "--height-mm-per-v goes with --height potential and --height-mm-per-ev with --height energy.");
            return (int)ExitCode.ValidationFailure;
        }

        IReadOnlyList<double>? box = null;

        if (options.Value("box-mm") is { } boxText)
        {
            if (!TryNumbers(boxText, 4, out var numbers))
            {
                Refuse(options, "box-mm", $"--box-mm takes four numbers, a0,b0,a1,b1, in millimetres in the plane's own axis order (no spaces), and '{boxText}' is not that.");
                return (int)ExitCode.ValidationFailure;
            }

            box = numbers;
        }

        var view = named ?? "iso";

        var request = new SurfaceRequest(
            options.Value("plane"),
            offset,
            box,
            samples,
            meshLines,
            heightKind,
            relief ?? SurfaceCommand.DefaultRelief,
            perVolt ?? perEv,
            at,
            options.Has("cycle-mean"),
            deepSamples);

        var modelPath = positional[0];
        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        SurfaceOutcome outcome;

        try
        {
            outcome = SurfaceCommand.Execute(
                modelPath,
                new ProjectLayout(root),
                request,
                view,
                width,
                height,
                options.Value("out"),
                options.Has("dry-run"),
                scale: !options.Has("no-scale"),
                csvPath: options.Value("csv"));
        }
        catch (EinzelException refused) when (refused.Errors.Any(e => e.Path.StartsWith("/surface/", StringComparison.Ordinal)))
        {
            // What a surface may be asked for is the surface's own rule, stated once, in
            // SurfaceCommand; here it is located at the flag that asked: /surface/relief is
            // --relief.
            throw new EinzelException(
            [
                .. refused.Errors.Select(e => e.Path.StartsWith("/surface/", StringComparison.Ordinal)
                    ? e with { Path = "/args/--" + e.Path["/surface/".Length..] }
                    : e),
            ]);
        }

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written ? $"wrote {outcome.Artifact}" : $"would write {outcome.Artifact}");

        if (outcome.Csv is { } csvWritten)
        {
            Console.Out.WriteLine(outcome.Written ? $"wrote {csvWritten}" : $"would write {csvWritten}");
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.Height} on the {outcome.Plane} plane at {outcome.OffsetMm:G6} mm: {outcome.AcrossAxis} {outcome.AcrossMm[0]:G6} to {outcome.AcrossMm[1]:G6} mm across, {outcome.DeepAxis} {outcome.DeepMm[0]:G6} to {outcome.DeepMm[1]:G6} mm deep, {outcome.Samples[0]} by {outcome.Samples[1]} samples ({outcome.StepsMm[0]:G4} by {outcome.StepsMm[1]:G4} mm)"));
        var when = outcome.CycleMean
            ? string.Create(invariant, $"; cycle mean over {outcome.CycleMeanWindowUs:G6} us")
            : outcome.InstantUs is { } t ? string.Create(invariant, $"; at t = {t:G6} us") : string.Empty;
        Console.Out.WriteLine(string.Create(
            invariant,
            $"height {outcome.HeightMmPerUnit:G4} mm per {outcome.Unit} ({outcome.UnitsPerMm:G4} {outcome.Unit} per drawn mm); values {outcome.Lowest:G6} to {outcome.Highest:G6} {outcome.Unit}{when}"));

        if (!outcome.ScaleDrawn && !options.Has("no-scale"))
        {
            Console.Out.WriteLine(
                "the vertical scale is not on the picture: it does not show in this view or does not fit at this size; "
                + "the scale above is in the PNG's text");
        }

        foreach (var plateau in outcome.Plateaus)
        {
            Console.Out.WriteLine(string.Create(
                invariant, $"  plateau {plateau.Conductor}: {plateau.PotentialV:G6} V ({plateau.Samples} samples)"));
        }

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Draws a flight as a numbered sequence of vector frames (RND-7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A render spec, never a bare model, and that is the requirement enforcing
    /// itself through the interface.</b> RND-7 says an animation "declares an explicit
    /// non-linear time mapping ... neither part is optional", so there is deliberately
    /// no <c>--rate</c> flag: the only way to ask for an animation is to have written
    /// down how it compresses time. A convenience flag would have made the hidden
    /// compression the easy case.
    /// </para>
    /// <para>
    /// <c>--fps</c> is offered because a frame rate is a property of the playback
    /// device rather than of the physics, and changing it changes no claim the
    /// animation makes.
    /// </para>
    /// </remarks>
    private static int Animation(CommandLine options)
    {
        var positional = options.Positional.Skip(1).ToList();

        if (positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render animation <spec.json> [--out <dir>] [--format svg|pdf] "
                + "[--fps <n>] [--progress <seconds>]");
            Console.Error.WriteLine(
                "       [--fps N] [--width-mm W] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine(
                "draws a flight as numbered vector frames on the spec's declared time mapping");
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "An animation needs a render spec, not a bare model: RND-7 requires the time "
                + "mapping to be declared, so there is no flag that supplies one.");

            return (int)ExitCode.ValidationFailure;
        }

        // Before the file is opened, as the section reads its own: a typo in a flag is the
        // caller's mistake whatever the file holds, and Parse threw it through to the handler
        // for defects in the engine, exit 6.
        if (!TryNumberFlag(options, "width-mm", "a width in millimetres", out var width)
            || !TryWholeFlag(options, "fps", 0, 1, 1000, out var fps, "a whole number of frames per second")
            || !TryChoiceFlag(options, "format", FormatNames, out var format)
            || !TryProgress(options, out var animationProgress))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var given = Path.GetFullPath(positional[0]);

        if (!ReadsAsSpec(given))
        {
            Console.Error.WriteLine(
                $"{given} reads as a model, and an animation needs a render spec that declares "
                + "its time mapping.");

            Console.Error.WriteLine(
                "Write one with an \"animation\" block: {\"model\": \"...\", \"animation\": "
                + "{\"framesPerSecond\": 30, \"phases\": [{\"until\": {\"value\": 10, "
                + "\"unit\": \"us\"}, \"rate\": {\"value\": 1, \"unit\": \"us/s\"}}]}}");

            return (int)ExitCode.ValidationFailure;
        }

        var (spec, modelPath) = RenderCommand.ReadSpec(given);

        spec = spec with
        {
            Format = FormatOf(format, spec.Format),
            WidthMm = width ?? spec.WidthMm,
            Caption = options.Value("caption") ?? spec.Caption,
            Animation = options.Has("fps") && spec.Animation is { } declared
                ? declared with { FramesPerSecond = fps }
                : spec.Animation,
        };

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        var outcome = RenderCommand.Animation(
            modelPath, new ProjectLayout(root), spec, options.Value("out"), options.Has("dry-run"),
            animationProgress);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        // The directory and a count, not three hundred lines of "wrote". A frame list
        // is what --json is for.
        var directory = Path.GetDirectoryName(outcome.Artifacts[0]);

        Console.Out.WriteLine(
            outcome.Written
                ? $"wrote {outcome.Frames} frames to {directory}"
                : $"would write {outcome.Frames} frames to {directory}");

        Console.Out.WriteLine(
            $"{outcome.PlaybackSeconds:F2} s of playback, "
            + $"{outcome.PageMm[0]:F0} by {outcome.PageMm[1]:F0} mm each");

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

        // Each action is a handler of its own, as each of render's kinds is: the flags one reads
        // are its own, and the registry is keyed by the action.
        if (action == "list")
        {
            return ExtList(options);
        }

        if (action == "test")
        {
            return ExtTest(options);
        }

        if (action == "register")
        {
            return ExtRegister(options);
        }

        Console.Error.WriteLine($"unknown 'ext' action '{action}'");
        Console.Error.WriteLine("usage: einzel ext list | test <name> | register <name>");
        return (int)ExitCode.ValidationFailure;
    }

    private static ProjectLayout ExtProject(CommandLine options) =>
        new(options.Value("project") ?? InferProjectRoot(Directory.GetCurrentDirectory()));

    private static int ExtList(CommandLine options)
    {
        var project = ExtProject(options);

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
            // LIC-2. An extension is third-party code run against this engine,
            // and a missing licence is printed as MISSING rather than left off the
            // line: the one extension worth asking about must not look like the
            // ones that answered.
            Console.Out.WriteLine(
                $"  {entry.Name} {entry.Version}  {entry.Kind}/{entry.Trust}"
                + $"  licence {entry.Licence ?? "NOT DECLARED"}"
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

    private static int ExtTest(CommandLine options)
    {
        var project = ExtProject(options);
        var rest = options.Positional.Skip(1).ToList();

        if (rest.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel ext test <name> [--input <file.json>] [--project <dir>] [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        System.Text.Json.Nodes.JsonNode? payload = new System.Text.Json.Nodes.JsonObject();

        if (options.Value("input") is { } file)
        {
            var text = File.ReadAllText(Path.GetFullPath(file));

            try
            {
                payload = System.Text.Json.Nodes.JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException malformed)
            {
                // The caller's file, not a defect in the engine: it was exit 6.
                Refuse(options, "input", $"--input takes a JSON file, and '{file}' does not read as JSON: {malformed.Message}");
                return (int)ExitCode.ValidationFailure;
            }
        }

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

    private static int ExtRegister(CommandLine options)
    {
        var project = ExtProject(options);
        var rest = options.Positional.Skip(1).ToList();

        if (rest.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel ext register <name> [--kind objective|analysis|geometry|"
                + "sequence|interchange] [--project <dir>] [--dry-run] [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var kinds = Enum.GetValues<Einzel.Extensions.ExtensionKind>();

        if (!TryChoiceFlag(options, "kind", kinds.Select(k => k.ToString().ToLowerInvariant()), out var kindName))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var kind = kindName is null
            ? Einzel.Extensions.ExtensionKind.Objective
            : kinds.First(k => string.Equals(k.ToString(), kindName, StringComparison.OrdinalIgnoreCase));

        var created = ExtensionCommand.Register(project, rest[0], kind, options.Has("dry-run"));

        foreach (var file in created)
        {
            Console.Out.WriteLine(options.Has("dry-run") ? $"would write {file}" : $"wrote {file}");
        }

        return (int)ExitCode.Success;
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

        if (!TryWholeFlag(options, "ions", 60, 1, 1_000_000, out var ions, "a whole number of ions"))
        {
            return (int)ExitCode.ValidationFailure;
        }

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

        if (!TryChoiceFlag(options, "vcs", VcsNames, out var vcs, exact: true))
        {
            return (int)ExitCode.ValidationFailure;
        }

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine($"would create a project at {Path.GetFullPath(root)}");
            return (int)ExitCode.Success;
        }

        var outcome = InitCommand.Execute(root, withGit: vcs == "git");

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

    /// <summary>The model's declared parameter surface, and whether it validates.</summary>
    /// <remarks>
    /// <para>
    /// <b>A verb rather than a shell method, because AGT-2 says nothing exists only in
    /// the shell.</b> The window needs this to build a model tree without parsing the
    /// document itself, which UI-1 forbids it from knowing how to do - and an agent needs
    /// exactly the same thing for exactly the same reason. `einzel schema` says what a
    /// model <em>may</em> contain; this says what one <em>does</em>.
    /// </para>
    /// <para>
    /// It answers for a document that does not validate too, with the errors alongside.
    /// That is what live validation needs: a person editing a parameter into an invalid
    /// state must still see the tree, rather than have it vanish until they undo what
    /// they typed.
    /// </para>
    /// </remarks>
    private static int Outline(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel outline <model.json> [--set <parameter>=<value>]... [--json] [--dry-run]");

            return (int)ExitCode.ValidationFailure;
        }

        var model = options.Positional[0];

        // Setting one is the same verb, because reading the knobs and turning one are the
        // same conversation. The value is in the parameter's own declared unit: a person
        // editing a 5 mm radius types 7, and demanding 0.007 would ask them to do the
        // conversion the format exists to make unnecessary.
        // `--set` with nothing after it never reaches here: it is a flag that takes a value
        // given none, which the command line refuses for every verb before the verb runs.
        // Every occurrence, not the last. `--set a=1 --set b=2` under a last-wins read
        // applies b, drops a, exits 0 and says nothing - which is how an agent that
        // batched two edits lost one and believed both had landed.
        var assignments = options.Values("set");

        if (assignments.Count > 0)
        {
            var edited = File.ReadAllText(Path.GetFullPath(model));
            var applied = new List<(string Name, double Value)>(assignments.Count);

            foreach (var assignment in assignments)
            {
                var split = assignment?.IndexOf('=', StringComparison.Ordinal) ?? -1;

                if (assignment is null || split <= 0)
                {
                    Refuse(options, "set", $"--set takes <parameter>=<value>, for example --set inscribedRadius=7, and '{assignment}' is not that");
                    return (int)ExitCode.ValidationFailure;
                }

                var name = assignment[..split];

                if (!TryNumber(assignment[(split + 1)..], out var value))
                {
                    Refuse(
                        options,
                        "set",
                        $"'{assignment[(split + 1)..]}' is not a number. The value is in the "
                        + "parameter's own declared unit, so write the magnitude alone");

                    return (int)ExitCode.ValidationFailure;
                }

                edited = OutlineCommand.WithParameter(model, name, value, edited);
                applied.Add((name, value));
            }

            // CLI-4: --dry-run says what would be written and writes nothing. This wrote,
            // and two agents in the acceptance run hit it - one of them lost a model
            // mid-task to the flag they had reached for in order to be careful. A
            // --dry-run that mutates is worse than none, because it is the flag people
            // use when they want to be safe.
            if (options.Has("dry-run"))
            {
                foreach (var (name, value) in applied)
                {
                    Console.Error.WriteLine(
                        string.Create(CultureInfo.InvariantCulture, $"would set {name} to {value:G6}"));
                }

                Console.Error.WriteLine($"would write {model} - nothing was written");
            }
            else
            {
                File.WriteAllText(Path.GetFullPath(model), edited);

                // A mutating command whose output is indistinguishable from a read-only
                // one is how an edit goes unnoticed, and it is what made the dry-run bug
                // invisible for three commands.
                foreach (var (name, value) in applied)
                {
                    Console.Error.WriteLine(
                        string.Create(CultureInfo.InvariantCulture, $"set {name} to {value:G6}"));
                }

                Console.Error.WriteLine($"wrote {model}");
            }
        }

        var outline = OutlineCommand.Execute(model);

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(outline));

            return (int)(outline.Valid ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        Console.Out.WriteLine($"{outline.Name ?? "(unnamed)"}  schema {outline.SchemaVersion}");

        foreach (var parameter in outline.Parameters)
        {
            // The value and its unit are two halves of one statement and never appear
            // apart, which is GRD-1's habit applied to an input rather than a result.
            var shown = parameter.Expression is { } expression
                ? expression
                : string.Create(invariant, $"{parameter.Value:G6}");

            var bounds = OutlineCommand.BoundsText(parameter);

            Console.Out.WriteLine(string.Create(
                invariant,
                $"  {parameter.Name,-20} {shown,-32} {parameter.Unit,-8}"
                + $"{(bounds is null ? string.Empty : "  " + bounds)}"
                + $"{(parameter.Editable ? string.Empty : "  (derived)")}"));
        }

        if (outline.Valid)
        {
            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine();

        foreach (var error in outline.Errors)
        {
            Console.Error.WriteLine($"  [{error.Code}] {error.Path}: {error.Constraint}");
        }

        return (int)ExitCode.ValidationFailure;
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
            Console.Error.WriteLine(
                "usage: einzel run <model.json> [--vtu] [--json] [--project <dir>] "
                + "[--progress <seconds>]");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(modelPath);
        var project = new ProjectLayout(root);

        // WATCHED BY DEFAULT, and that is the decision rather than the plumbing. A flag
        // somebody has to remember is a flag that is not set on the run that gets killed -
        // and three attempts at the TIMS front-end sequence were lost with nothing on disk
        // to say how far they had got, the last of them to a Windows update. Thirty
        // seconds, so a run of a few seconds says one thing and an eight-hour run says a
        // thousand; `--progress 0` asks for silence.
        if (!TryProgress(options, out var progress))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var (run, validation) = RunCommand.Execute(
            modelPath,
            project,
            exportVtu: options.Has("vtu"),
            timestampUtc: DateTimeOffset.UtcNow,

            // Diagnostics on stderr (CLI-2), so a caller piping `--json` gets the result
            // document and nothing else however long the run took to produce it.
            progress: progress);

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
        // than an outcome. A sequenced run is the same: it succeeds by completing its
        // sequence, and where the ions went is in the phase table.
        //
        // This list is now three long, and every entry was added after a working run
        // reported itself as a failure - the diffusive one, then this. A run that
        // finished what it was asked to do is a success, and the thing to ask is
        // whether it finished rather than which mode it was in.
        // The question, rather than a list of the outcome names that answered it when
        // this line was written. That list had to be widened once for diffusive runs and
        // once for sequenced ones, and still called six of the thirty-seven shipped
        // examples failures - three traps and thermalisations ending at their declared
        // hold, and three deliberate losses that are the control halves of pairs.
        return run.Completed ? (int)ExitCode.Success : (int)ExitCode.ConvergenceFailure;
    }

    private static void WriteRun(RunOutcome run)
    {
        var invariant = CultureInfo.InvariantCulture;
        var flight = run.FlightTime;
        var halfWidth = (flight.Uncertainty.Upper - flight.Uncertainty.Lower) / 2.0;

        // A density has no flight time, no energy drift and no final position: it
        // is a field over a whole grid, not an ion with a history. Those three
        // lines used to be printed anyway - the flight time as "NaN +/- NaN", and
        // the final position by indexing an empty list, which threw and reported a
        // diffusive run as a defect in einzel.
        //
        // Absent rather than not-a-number, which is the rule the rest of this surface
        // already follows: a reader cannot tell a missing measurement from a failed one
        // if both print the same way.
        //
        // Asked of the run rather than derived from which blocks it filled. This line was
        // `run.Diffusion is null`, then `run.Diffusion is null && run.Sequence is null` -
        // each revision adding the mode that had just been caught printing
        // `flight time NaN +/- NaN`, and a mixture would have been the third addition. A
        // list of the modes known when the line was written is not the question it is
        // asking, so the run answers the question instead.
        var trajectory = run.HasFlightTime;

        if (trajectory)
        {
            // GRD-1 in the terminal: the value never appears without what qualifies
            // it. ASCII rather than a plus-minus sign, because the console encoding
            // is not ours to assume and a mangled character in a reported
            // uncertainty is exactly the wrong place to be clever.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"flight time   {flight.Value:F6} +/- {halfWidth:G3} {flight.Unit}"));
        }

        // A null observed order means the refinements agreed to the last bit, so
        // there is no order to report. Saying "converged to round-off" is the
        // honest rendering; printing an empty number would read as a missing
        // measurement rather than a perfect one.
        var convergence = flight.Evidence.ObservedOrder is { } order
            ? string.Create(invariant, $"observed order {order:G3} of {flight.Evidence.NominalOrder:G3}")
            : "residual at round-off, no order to resolve";

        if (trajectory)
        {
            Console.Out.WriteLine(
                $"              {flight.Evidence.Kind} in {flight.Evidence.Measure}, {convergence}");

            // Said in words rather than printed as NaN. There are two ways to have no
            // energy invariant to measure - a driven field does work deliberately, and an
            // ion launched at rest at zero potential has no scale to be relative to - and
            // both used to reach this line as a number that meant nothing.
            Console.Out.WriteLine(
                double.IsNaN(run.MaximumRelativeEnergyDrift)
                    ? "energy drift  not measured: no conserved scale, because the field "
                      + "does work or the ion started at rest at zero potential"
                    : string.Create(
                        invariant,
                        $"energy drift  {run.MaximumRelativeEnergyDrift:E2} relative (ACC-4 budget 1e-6)"));
        }

        if (run.Sequence is null)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"steps         {run.AcceptedSteps}, {run.AnalyticDriftDistanceM:F4} m advanced analytically"));
        }

        if (run.FinalPositionMm.Count > 0)
        {
            // A sequenced run has no single ion, so what this is is the packet's centre
            // - said in the label rather than left for the reader to assume.
            var what = run.Sequence is null ? "final x      " : "packet centre";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{what} {run.FinalPositionMm[0]:F6} mm"));
        }

        if (run.Sequence is { } sequence)
        {
            Console.Out.WriteLine();

            foreach (var phase in sequence.Phases)
            {
                // The trajectory count and the population are different quantities, and
                // a conversion re-samples the first while carrying the second - so both
                // are shown, and a diffusive phase shows a dash rather than a zero,
                // because it has no trajectories rather than none left.
                var carried = phase.Mode == "trajectory"
                    ? phase.Trajectories.ToString(invariant)
                    : "-";
                // The width beside the centre, because for a mobility analyser the two
                // together are the measurement: where a packet parks is which mobility it
                // has, and how wide it is when the ramp releases it is the floor on the
                // resolving power. A dash rather than a zero where there is nothing to take
                // a width over, on the same argument as the trajectory count above.
                var wide = phase.SpreadMm is { Count: > 0 } spread
                    ? string.Create(invariant, $"{spread[0],7:F3}")
                    : "      -";

                // A phase the packet never reached has no centre to report, which is a
                // different statement from a centre at the origin - the same argument the
                // dash above makes for a trajectory count. Padded to the width of the
                // populated form so the `converted` marker stays in one column: this is a
                // table, and a row that is shorter than its neighbours moves the column.
                var centre = phase.CentroidMm.Count > 0
                    ? string.Create(
                        invariant, $"x {phase.CentroidMm[0],8:F3} +- {wide} mm")
                    : "packet empty";

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {phase.Name,-12} {phase.Mode,-11} to {phase.EndsAtUs,8:F2} us  "
                    + $"{phase.Population,10:G6} ions in {carried,5} trajectories  "
                    + $"{centre,-24}"
                    + $"{(phase.Converted ? "  converted" : string.Empty)}"));
            }

            Console.Out.WriteLine();

            Console.Out.WriteLine(string.Create(
                invariant,
                $"sequence      {sequence.Conversions} mode conversion(s), "
                + $"{sequence.ArrivedIons:G6} ions arrived"));

            foreach (var loss in sequence.Losses)
            {
                Console.Out.WriteLine(string.Create(
                    invariant, $"  lost on {loss.Surface}: {loss.Ions:G6} ions"));
            }
        }

        if (run.Mixture is { } mixture)
        {
            Console.Out.WriteLine();

            // Per species, because that is the whole of what a mixture run has to say: one
            // line of totals would be a report of the sum of two populations that were kept
            // apart on purpose.
            foreach (var member in mixture.Species)
            {
                var derived = member.MobilityDerived ? " (derived)" : string.Empty;

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {member.Name,-14} K {member.MobilitySi:G6} m2/(V s){derived,-10}  "
                    + $"{member.Remaining:G6} of {member.Launched:G6} ions held, "
                    + $"{member.Transmission:P1} through, x {member.CentroidMm:F3} mm"));

                foreach (var loss in member.Losses)
                {
                    Console.Out.WriteLine(string.Create(
                        invariant, $"                 lost on {loss.Surface}: {loss.Ions} ions"));
                }
            }

            Console.Out.WriteLine();

            Console.Out.WriteLine(string.Create(
                invariant,
                $"mixture       {mixture.Species.Count} populations, {mixture.Steps} shared steps of "
                + $"{mixture.StepUs * 1e3:F3} ns set by '{mixture.StepSetBy}'"));

            // Absent rather than zero: a run that did not model the populations' charge and one
            // that modelled it and found none are different runs, and the second is worth
            // knowing about.
            Console.Out.WriteLine(
                mixture.SelfFieldSolves is { } solves
                    ? string.Create(
                        invariant,
                        $"space charge  {solves} solve(s), peak {mixture.PeakSelfPotentialVolts:G4} V "
                        + $"over a net {mixture.NetChargeSi:G4} C")
                    : "space charge  not modelled: the populations do not feel one another, so this "
                      + "is what separate runs would give");
        }

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
            //
            // Absent, not zero, where fewer than two ions arrived: a peak needs two
            // points to have a width, and a printed 0.000 ns reads as an infinitely
            // sharp one. The transmission and the itemised losses above are still
            // reported, and they are the whole of what such a run has to say.
            if (ensemble.CentralWidthNs is { } central && ensemble.GaussianFwhmNs is { } gaussian)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"peak          {central:F3} ns central half, "
                    + $"{gaussian:F3} ns Gaussian FWHM, skew {ensemble.Skewness:+0.00;-0.00;0.00}"));

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"turn-around   {ensemble.TurnAroundFwhmNs:F3} ns of that Gaussian width"));
            }
            else
            {
                Console.Out.WriteLine(
                    "peak          none: fewer than two ions arrived, so there is no width to "
                    + "measure");
            }

            // Only when there is something to say. A beamline holds nothing at the end
            // of its run, and a line of zeros on every ordinary model would be noise -
            // but a TRAP arrives nowhere by design, and without this it reports a
            // transmission of zero and nothing else, which is what a trap that lost
            // every ion reports too.
            if (ensemble.Confined.Value is > 0.0)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"confined      {ensemble.Confined.Value:P1} still inside at the end "
                    + $"of the run, having struck nothing"));
            }

            if (ensemble.ResolvingPower is { } resolving)
            {
                // Said in words when the peak has no width to resolve. The value is NaN
                // there rather than a number, because the honest answer is unbounded and
                // the previous one - zero - was the worst conceivable value standing in
                // for the best.
                Console.Out.WriteLine(
                    double.IsNaN(resolving.Value)
                        ? "resolving     unbounded: the arrivals carry no spread to resolve"
                        : string.Create(
                            invariant,
                            $"resolving     {resolving.Value:G6} +/- "
                            + $"{(resolving.Uncertainty.Upper - resolving.Uncertainty.Lower) / 2.0:G3}"
                            + $" (from the central half)"));
            }

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

            foreach (var warning in (ensemble.ResolvingPower?.Warnings ?? [])
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
    /// <summary>
    /// The project a file belongs to: the nearest ancestor holding a
    /// <c>models</c> directory, or failing that the file's own directory.
    /// </summary>
    /// <remarks>
    /// The fallback used to be the working directory, which put results wherever
    /// the caller happened to be standing rather than anywhere near the thing they
    /// ran. A study kept in a scratch folder and run from a source tree wrote
    /// <c>results/</c> into the source tree and said only "wrote results\x.json",
    /// which is both surprising and hard to trace back. A file's own directory is
    /// the closest thing to a project when it is not in one - PRJ-1's unit of work
    /// is a directory, and this picks the directory that at least contains the
    /// input.
    /// </remarks>
    private static string InferProjectRoot(string modelPath)
    {
        var directory = new FileInfo(modelPath).Directory;

        for (var at = directory; at is not null; at = at.Parent)
        {
            if (Directory.Exists(Path.Combine(at.FullName, "models")))
            {
                return at.FullName;
            }
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private const string Usage =
        """
        einzel - open-source, agent-native ion optics

        usage: einzel <command> [arguments]

        starting out:
          doctor [dir]                  check the installation, and a project if given
          schema [--study | --units]    the model format, or the study format, as JSON Schema; or the unit symbols
          templates [name]              list device templates, or print one
          examples [name]               list example models, or print one

        working:
          init [dir] [--vcs git]        create a project directory
          new <model.json>              create a model from a template or an example
                --from-template <name> | --from-example <name>
          validate <model.json>         check units, bounds, and regime validity
          outline <model.json>          the declared parameters, with units and bounds
                --set <name>=<value>    change one, in its own declared unit
          estimate <model.json>         what a run will cost, without running it
          solve <model.json>            solve the fields only, and report how they went
          probe <model.json>            the potential and field at a point, or along a line
                --at-mm x,y,z | --line-mm x,y,z --to-mm x,y,z [--samples N] [--at-us t]
          run <model.json> [--vtu]      run a model; --vtu also writes a ParaView trajectory
          preview <model.json>          a fast, deliberately inexact look
          test [dir]                    run the project's tests
          verify [dir]                  are the stored results still the answer?
          project [dir]                 what the project holds, and the state of each model
          report [dir] [--out <f>]      an account of what has been run, as one HTML page
          sweep <study.json>            tolerance Monte Carlo, and which parameter binds first
          scan <study.json>             one parameter across a range, one row per point
          boundary <study.json>         bisect onto a stability boundary (Class B, ACC-6)
          optimise <study.json>         search the declared parameters for a better design
          export <model.json>           write the solved field as VTK ImageData
                --composite             the summed field and E at one instant, on a grid
          compare <model.json>          run both transport modes and report the disagreement
          render section <model.json>   draw a plane through the instrument as SVG or PDF
          render animation <spec.json>  frames on a declared playback mapping (RND-7)
          render surface <model.json>   the potential on a plane drawn as height, as a PNG
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

    /// <summary>How often a render's own transport should report, from the command line.</summary>
    /// <remarks>
    /// <b>Watched by default, at the same thirty seconds a run uses.</b> A render of a
    /// sequenced model runs that model's whole sequence, so the flag that is not set is the
    /// flag that is missing from the render that gets killed - the same argument that made
    /// `einzel run` watch by default. Diagnostics on stderr (CLI-2), so `--json` still gets
    /// the result document and nothing else. `--progress 0` asks for silence.
    /// </remarks>
    /// <param name="options">The parsed command line.</param>
    /// <param name="progress">The interval and where to send it.</param>
    /// <returns>True where the flag parsed, false where it did not and was complained about.</returns>
    private static bool TryProgress(CommandLine options, out RunProgress? progress)
    {
        progress = null;

        // Bare, --progress is the default interval, as it is when absent: reporting is on
        // unless turned off, so the flag alone says nothing new and is not a mistake. That it
        // may be bare is the registry's statement (VerbFlags.Optional), which is what lets the
        // command line through; read here, a flag with no value is simply absent.
        if (!TryNumberFlag(options, "progress", "an interval in seconds", out var interval, "Use 0 to turn it off."))
        {
            return false;
        }

        progress = new RunProgress(interval ?? 30.0, Console.Error.WriteLine);
        return true;
    }

    /// <summary>The figure formats <c>--format</c> names.</summary>
    private static readonly string[] FormatNames = ["svg", "pdf"];

    /// <summary>What <c>init --vcs</c> names: a repository, or none, which is also the default.</summary>
    private static readonly string[] VcsNames = ["git", "none"];

    private static FigureFormat FormatOf(string? named, FigureFormat otherwise) =>
        named is null ? otherwise : named == "pdf" ? FigureFormat.Pdf : FigureFormat.Svg;

}
