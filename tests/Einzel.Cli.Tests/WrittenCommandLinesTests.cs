using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Every <c>einzel ...</c> command line written down in the source and in the documentation
/// names flags its verb reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>A suggestion that points at a refused spelling is worse than none.</b> An unknown unit's
/// error said "see the unit list from 'einzel schema --units'" for as long as the error has
/// existed, and there was no such flag: <c>schema</c> ignored it, and once unknown flags were
/// refused the suggestion led straight to a second error. AGT-3 makes an error a recovery
/// instruction; this holds every written one to the registry the CLI itself checks against
/// (<see cref="VerbFlags.ByVerb"/>).
/// </para>
/// <para>
/// What is checked is the flags, since that is what a registry can decide from text: a usage
/// line's <c>&lt;model.json&gt;</c> and <c>[--out &lt;file&gt;]</c> are placeholders, so how
/// many arguments it has and whether a flag has its value are not read off it.
/// </para>
/// </remarks>
public sealed class WrittenCommandLinesTests(ITestOutputHelper output)
{
    /// <summary>
    /// Command lines written down on purpose with a flag the verb refuses - as examples of what
    /// is refused - each with where it is and why it is there.
    /// </summary>
    private static readonly Dictionary<(string Verb, string Flag), string> Refused = new()
    {
    };

    /// <summary>The check itself.</summary>
    [Fact]
    public void EveryWrittenCommandLineNamesFlagsItsVerbReads()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "Einzel.Cli", "Program.cs")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        var files = Directory.GetFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.GetFiles(Path.Combine(root.FullName, "docs"), "*.md", SearchOption.AllDirectories)
                .Where(f => !History.Contains(Path.GetFileName(f), StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        var wrong = new List<string>();
        var met = new HashSet<(string, string)>();
        var checkedLines = 0;
        var checkedFlags = 0;

        foreach (var file in files)
        {
            foreach (var (verb, flags) in CommandLines(File.ReadAllText(file)))
            {
                checkedLines++;

                foreach (var flag in flags)
                {
                    checkedFlags++;

                    if (VerbFlags.ByVerb[verb].Knows(flag) || flag == "help")
                    {
                        continue;
                    }

                    if (Refused.ContainsKey((verb, flag)))
                    {
                        met.Add((verb, flag));
                        continue;
                    }

                    wrong.Add($"{Path.GetRelativePath(root.FullName, file)}: einzel {verb} ... --{flag}");
                }
            }
        }

        output.WriteLine($"{checkedLines} command lines, {checkedFlags} flags, in {files.Count} files");

        // The control: the scan reads command lines, and the one that started this is among them.
        Assert.True(checkedLines > 200, $"only {checkedLines} command lines were found");
        Assert.Contains(
            CommandLines("see the unit list from 'einzel schema --units' for known symbols"),
            line => line.Verb == "schema" && line.Flags.SequenceEqual(["units"]));
        Assert.Null(Program.CommandLineProblem(["schema", "--units"]));

        Assert.True(wrong.Count == 0, $"{wrong.Count} written command lines name a flag their verb does not read:\n" + string.Join("\n", wrong.Distinct()));

        // An exemption nothing uses any more is a line somebody fixed, or a typo in this list.
        Assert.Empty(Refused.Keys.Except(met));
    }

    /// <summary>
    /// The pages that record what was the case when they were written - a changelog entry quotes
    /// the command line of its day - and are not instructions.
    /// </summary>
    private static readonly string[] History = ["changelog.md", "astral-log.md"];

    /// <summary>The command lines in a text: each one's verb, as the registry keys it, and its flags.</summary>
    private static IEnumerable<(string Verb, IReadOnlyList<string> Flags)> CommandLines(string text)
    {
        foreach (Match match in Regex.Matches(text, @"(?<![\w/.\\-])einzel ([a-z][a-z-]*(?: [^'""`\n]*?)?)(?=</c>|['""`\n])"))
        {
            var words = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var verb = words[0] is "render" or "ext" or "agents" && words.Length > 1 && VerbFlags.ByVerb.ContainsKey(words[0] + " " + words[1])
                ? words[0] + " " + words[1]
                : words[0];

            if (!VerbFlags.ByVerb.ContainsKey(verb))
            {
                // Prose ("einzel lens"), a verb with a sub-verb written as a choice, or no verb.
                continue;
            }

            var flags = new List<string>();

            foreach (var word in words)
            {
                // [--out <file>], --plane=xy, --json, and a choice written --a|--b.
                foreach (Match flag in Regex.Matches(word, @"(?<![\w-])--([a-z0-9][a-z0-9-]*)"))
                {
                    flags.Add(flag.Groups[1].Value);
                }
            }

            yield return (verb, flags);
        }
    }
}
