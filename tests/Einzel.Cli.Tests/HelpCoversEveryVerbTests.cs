using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Every verb the CLI dispatches is listed in its help, and every listed verb exists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because one had gone missing.</b> <c>einzel outline</c> — the verb the model
/// tree is built on, and the one an agent would use to read a model's knobs without parsing
/// the document — had no help line at all. It worked, it was documented in
/// <c>docs/cli.md</c>, and it could not be discovered from the tool itself. So could
/// <c>render animation</c>.
/// </para>
/// <para>
/// For a surface whose entire argument is that an agent drives it (§15, AGT-7), a
/// capability that cannot be discovered from the tool is close to one that does not exist.
/// The same reasoning makes the platform layer of <c>AGENTS.md</c> generated rather than
/// hand-written: an instruction set that has drifted is worse than none, because it is
/// trusted.
/// </para>
/// <para>
/// <b>Checked against the dispatcher itself rather than a list kept here.</b> A hardcoded
/// list would drift in exactly the same way and for the same reason — somebody adds a verb
/// and updates neither. The switch in <c>Program.cs</c> is the only place that decides what
/// the CLI accepts, so that is what this reads.
/// </para>
/// </remarks>
public sealed class HelpCoversEveryVerbTests(ITestOutputHelper output)
{
    /// <summary>Verbs whose absence from the help is deliberate.</summary>
    /// <remarks>
    /// <c>--version</c> and the help aliases are options rather than verbs, and the help
    /// lists <c>--version</c> under its own heading. Anything else added here needs a
    /// reason written beside it, which is the point of the list being explicit.
    /// </remarks>
    private static readonly string[] NotVerbs =
        ["--version", "-v", "--help", "-h", "help"];

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>The dispatcher's own switch, read from the source beside the assembly.</summary>
    /// <remarks>
    /// The same mechanism <see cref="ShellBoundaryTests"/> uses to read a csproj: walk up
    /// from the built assembly until the repository layout appears. A scan that found
    /// nothing would pass vacuously, which this project has been caught by more than once,
    /// so the caller asserts it found something.
    /// </remarks>
    private static (IReadOnlyList<string> Primary, IReadOnlyList<string> All) Dispatched()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            var program = Path.Combine(directory.FullName, "src", "Einzel.Cli", "Program.cs");

            if (!File.Exists(program))
            {
                continue;
            }

            var source = File.ReadAllText(program);

            // An arm looks like  "run" => Run(options),  and may carry aliases:
            //   "optimise" or "optimize" => Optimise(options),
            //
            // The first name is the one a reader must be able to find; the rest are
            // spellings kept working on purpose and deliberately not advertised, since
            // listing both would suggest they differ.
            var arms = Regex.Matches(
                source,
                """^\s*"(?<primary>[a-z-]+)"(?<aliases>(?:\s+or\s+"[a-z-]+")*)\s*=>""",
                RegexOptions.Multiline);

            var primary = new List<string>();
            var all = new List<string>();

            foreach (Match arm in arms)
            {
                primary.Add(arm.Groups["primary"].Value);
                all.Add(arm.Groups["primary"].Value);

                foreach (Match alias in Regex.Matches(arm.Groups["aliases"].Value, """"([a-z-]+)""""))
                {
                    all.Add(alias.Groups[1].Value);
                }
            }

            return (
                [.. primary.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                [.. all.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        }

        return ([], []);
    }

    /// <summary>Whether the help lists a verb, as itself or under a parent.</summary>
    /// <remarks>
    /// <c>agents tasks</c>, <c>agents setup</c> and <c>agents score</c> are dispatched by a
    /// second switch inside <c>agents</c> and are listed under it, which is the right way
    /// round for both: one line per thing a person types.
    /// </remarks>
    private static bool Listed(string help, string verb) =>
        Regex.IsMatch(help, $@"^\s+{Regex.Escape(verb)}\b", RegexOptions.Multiline)
        || Regex.IsMatch(help, $@"^\s+[a-z-]+ {Regex.Escape(verb)}\b", RegexOptions.Multiline);

    /// <summary>Every dispatched verb appears in the help.</summary>
    [Fact]
    public void EveryDispatchedVerbAppearsInTheHelp()
    {
        var (verbs, _) = Dispatched();

        // A scan that found nothing passes every assertion below and means nothing. This
        // project has shipped that vacuous truth four times.
        Assert.True(
            verbs.Count > 10,
            $"only {verbs.Count} verbs were found in Program.cs, so this test read the "
            + "wrong file or the switch has been rewritten - it is checking nothing");

        var (_, help, _) = Cli("--help");

        output.WriteLine($"{verbs.Count} verbs dispatched");

        var missing = verbs
            .Where(v => !NotVerbs.Contains(v, StringComparer.Ordinal))
            .Where(v => !Listed(help, v))
            .ToList();

        foreach (var verb in verbs)
        {
            output.WriteLine(
                $"  {(missing.Contains(verb) ? "MISSING" : "listed ")} {verb}");
        }

        Assert.True(
            missing.Count == 0,
            $"these verbs dispatch and are not in `einzel --help`: {string.Join(", ", missing)}. "
            + "A capability an agent cannot discover from the tool is close to one that does "
            + "not exist (AGT-7)");
    }

    /// <summary>Every verb the help lists actually dispatches.</summary>
    /// <remarks>
    /// The other direction, and it fails differently: a help line for a verb that was
    /// renamed or removed sends a reader to a command that answers "unknown". Both halves
    /// are needed — the first alone is satisfied by a help file listing everything
    /// imaginable.
    /// </remarks>
    [Fact]
    public void EveryVerbTheHelpListsActuallyDispatches()
    {
        var (_, verbs) = Dispatched();

        Assert.True(verbs.Count > 10, "the dispatcher was not found");

        var (_, help, _) = Cli("--help");

        // The first word of each indented command line, which is how the help is laid out.
        var listed = Regex
            .Matches(help, @"^  ([a-z][a-z-]+)\b", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(listed);

        var phantom = listed.Where(v => !verbs.Contains(v, StringComparer.Ordinal)).ToList();

        output.WriteLine($"{listed.Count} listed: {string.Join(", ", listed)}");

        Assert.True(
            phantom.Count == 0,
            $"these are listed in the help and do not dispatch: {string.Join(", ", phantom)}");
    }
}
