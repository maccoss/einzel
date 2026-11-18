using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The registry of each verb's flags (<see cref="VerbFlags.ByVerb"/>) is the flags each verb's
/// handler actually reads, and the command line holds every verb to it.
/// </summary>
public sealed class VerbFlagsTests(ITestOutputHelper output)
{
    // Which handler reads each verb's flags, and which of the handlers it calls belong to a
    // different verb (render's sub-verbs are dispatched from Render).
    private static readonly Dictionary<string, (string Handler, string[] Skip)> Handlers = new(StringComparer.Ordinal)
    {
        ["init"] = ("Init", []), ["new"] = ("New", []), ["validate"] = ("Validate", []), ["outline"] = ("Outline", []),
        ["project"] = ("Project", []), ["estimate"] = ("Estimate", []), ["solve"] = ("Solve", []), ["probe"] = ("Probe", []),
        ["run"] = ("Run", []), ["sweep"] = ("Sweep", []), ["optimise"] = ("Optimise", []), ["optimize"] = ("Optimise", []),
        ["scan"] = ("Scan", []), ["boundary"] = ("Boundary", []), ["preview"] = ("Preview", []), ["test"] = ("Test", []),
        ["verify"] = ("Verify", []), ["report"] = ("Report", []), ["export"] = ("Export", []), ["compare"] = ("Compare", []),
        ["schema"] = ("Schema", []), ["templates"] = ("Catalog", []), ["examples"] = ("Catalog", []),
        ["agents-md"] = ("AgentsMd", []), ["doctor"] = ("Doctor", []),
        ["agents"] = ("AgentTasks", []), ["agents tasks"] = ("AgentTasks", []),
        ["agents setup"] = ("AgentSetup", []), ["agents score"] = ("AgentScore", []),
        ["ext"] = ("ExtList", []), ["ext list"] = ("ExtList", []),
        ["ext test"] = ("ExtTest", []), ["ext register"] = ("ExtRegister", []),
        ["render section"] = ("Render", ["Still", "Surface", "Animation"]),
        ["render still"] = ("Still", []), ["render surface"] = ("Surface", []), ["render animation"] = ("Animation", []),
    };

    // Read as a value under a name chosen at run time (`Value(hasPoint ? "at-mm" : "line-mm")`),
    // which a scan for a literal cannot see.
    private static readonly HashSet<string> ReadDynamically = new(StringComparer.Ordinal) { "at-mm", "line-mm" };

    /// <summary>
    /// Every verb's registered flags are exactly the flags its handler reads, through every
    /// helper it calls; the value-taking ones are the ones it reads as values.
    /// </summary>
    /// <remarks>
    /// The registry is what lets the command line call a flag unknown, or say it takes a value,
    /// for a particular verb. A handler that grew a flag the registry did not list would refuse
    /// its own flag; a registry listing a flag no handler reads would accept one that does
    /// nothing. Either fails here.
    /// </remarks>
    [Fact]
    public void EveryVerbsFlagsAreTheOnesItsHandlerReads()
    {
        var source = Source();
        var bodies = Bodies(source);

        Assert.Equal(VerbFlags.ByVerb.Keys.Order(StringComparer.Ordinal), Handlers.Keys.Order(StringComparer.Ordinal));

        foreach (var (verb, (handler, skip)) in Handlers)
        {
            var read = Walk(handler, bodies, new HashSet<string>(skip, StringComparer.Ordinal), ReadPattern);
            var valued = Walk(handler, bodies, new HashSet<string>(skip, StringComparer.Ordinal), ValuePattern);
            var refused = Walk(handler, bodies, new HashSet<string>(skip, StringComparer.Ordinal), BarePattern);
            var flags = VerbFlags.ByVerb[verb];

            output.WriteLine($"{verb}: reads {string.Join(", ", read.Order(StringComparer.Ordinal))}");

            var registered = flags.Switches.Concat(flags.Values).Concat(flags.Optional).ToHashSet(StringComparer.Ordinal);
            read.ExceptWith(VerbFlags.Global);

            Assert.True(registered.SetEquals(read), $"{verb}: registered {string.Join(",", registered.Order())} but the handler reads {string.Join(",", read.Order())}");
            // A flag read as a value is registered as one that takes a value or may; which of
            // the two is the registry's statement and nobody else's - the handler reads both
            // the same way, and a bare one is simply absent to it.
            var takesValue = flags.Values.Concat(flags.Optional).ToHashSet(StringComparer.Ordinal);

            Assert.True(valued.IsSubsetOf(takesValue), $"{verb}: reads {string.Join(",", valued.Except(takesValue))} as values, registered otherwise");
            Assert.Empty(takesValue.Except(valued).Except(ReadDynamically));
            Assert.True(flags.Refused.SetEquals(refused), $"{verb}: refuses {string.Join(",", refused)} but registers {string.Join(",", flags.Refused)}");

            // The third kind is neither of the other two.
            Assert.Empty(flags.Optional.Intersect(flags.Values));
            Assert.Empty(flags.Optional.Intersect(flags.Switches));
        }

        // The control: the third kind exists, so the scan for it is not matching nothing.
        Assert.Contains("progress", VerbFlags.ByVerb["run"].Optional);
    }

    /// <summary>
    /// Whether a flag is a switch is asked of the verb the command line names, so the argument
    /// after a verb's switch is never its value, and a flag another verb reads as a value is.
    /// </summary>
    /// <remarks>
    /// The parser used to take its switches from every verb at once, which made "no name is a
    /// switch in one verb and a value in another" a rule the registry had to keep and a test
    /// had to police. Parsed against the verb, there is no such rule.
    /// </remarks>
    [Fact]
    public void TheParserAsksTheVerbWhichFlagsAreSwitches()
    {
        // --vtu is run's switch: the model after it is a positional argument.
        var run = CommandLine.Parse(["run", "--vtu", "model.json"]);
        Assert.Equal(["model.json"], run.Positional);
        Assert.Null(run.Value("vtu"));

        // It is nothing to compare, whose registry does not name it: there it is read as any
        // unknown flag is, taking the next argument - and refused by name before the verb runs.
        var compare = CommandLine.Parse(["compare", "--vtu", "model.json"]);
        Assert.Empty(compare.Positional);
        Assert.Equal("model.json", compare.Value("vtu"));

        // A sub-verb's switches are its own.
        var surface = CommandLine.Parse(["render", "surface", "--cycle-mean", "model.json"]);
        Assert.Equal(["surface", "model.json"], surface.Positional);

        // --json and --help are every verb's, and an unknown verb's too.
        Assert.Equal(["x"], CommandLine.Parse(["bogus", "--json", "x"]).Positional);
        Assert.Equal(["x"], CommandLine.Parse(["validate", "--help", "x"]).Positional);
    }

    private const string Readers = "(?:TryNumberFlag|TryWholeFlag|TryChoiceFlag)";
    private static readonly Regex ReadPattern = new(@"(?:\.Has|\.Value|\.PathValue|\.Values)\(""([a-z0-9-]+)""\)|Try\w*Flag\(options, ""([a-z0-9-]+)""", RegexOptions.Compiled);
    private static readonly Regex ValuePattern = new(@"(?:\.Value|\.PathValue|\.Values)\(""([a-z0-9-]+)""\)|" + Readers + @"\(options, ""([a-z0-9-]+)""", RegexOptions.Compiled);
    private static readonly Regex BarePattern = new(@"foreach \(var bare in new\[\] \{([^}]*)\}", RegexOptions.Compiled);

    private static HashSet<string> Walk(string method, Dictionary<string, string> bodies, HashSet<string> seen, Regex pattern)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        if (!seen.Add(method) || !bodies.TryGetValue(method, out var body))
        {
            return found;
        }

        foreach (Match m in pattern.Matches(body))
        {
            if (pattern == BarePattern)
            {
                foreach (Match name in Regex.Matches(m.Groups[1].Value, @"""([a-z0-9-]+)"""))
                {
                    found.Add(name.Groups[1].Value);
                }
            }
            else
            {
                found.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            }
        }

        foreach (Match call in Regex.Matches(body, @"\b(\w+)\("))
        {
            var callee = call.Groups[1].Value;

            if (callee != method && bodies.ContainsKey(callee))
            {
                found.UnionWith(Walk(callee, bodies, seen, pattern));
            }
        }

        return found;
    }

    private static Dictionary<string, string> Bodies(string source)
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var starts = Regex.Matches(source, @"\n    (?:private|public|internal) static [^\n(]*? (\w+)\(").ToList();

        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : source.Length;
            var name = starts[i].Groups[1].Value;
            bodies[name] = (bodies.TryGetValue(name, out var earlier) ? earlier : string.Empty) + source[starts[i].Index..end];
        }

        // The command line's own refusal reads --help for every verb; it is no handler's.
        bodies.Remove("Malformed");

        return bodies;
    }

    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Einzel.Cli", "Program.cs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return string.Concat(Directory.GetFiles(Path.Combine(directory!.FullName, "src", "Einzel.Cli"), "*.cs")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--help</c> after any verb does what it did before the command line was checked: the
    /// verb's own answer, never a refusal of <c>--help</c> itself.
    /// </summary>
    /// <param name="verb">The verb and sub-verb.</param>
    [Theory]
    [InlineData("validate")]
    [InlineData("render surface")]
    [InlineData("render section")]
    [InlineData("probe")]
    [InlineData("estimate")]
    public void HelpAfterAVerbIsTheVerbsOwnAnswer(string verb)
    {
        var words = verb.Split(' ');
        var (helped, _, helpErr) = Cli([.. words, "--help"]);
        var (bare, _, bareErr) = Cli(words);

        output.WriteLine(helpErr);

        Assert.Equal(bare, helped);
        Assert.Equal(bareErr, helpErr);
        Assert.DoesNotContain("takes a value", helpErr, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown flag", helpErr, StringComparison.Ordinal);
    }

    /// <summary>An unknown verb is reported as one, whatever flags follow it.</summary>
    [Fact]
    public void AnUnknownVerbIsAnUnknownCommand()
    {
        var (code, _, stderr) = Cli("bogus", "--plane");

        Assert.Equal(1, code);
        Assert.Contains("unknown command 'bogus'", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("takes a value", stderr, StringComparison.Ordinal);
    }

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
    /// <summary>
    /// The sub-verb is the first word that is neither a flag nor a flag's value, wherever the flags
    /// stand: <c>ext --json list</c> is <c>ext list</c>, as it was before the registry.
    /// </summary>
    [Theory]
    [InlineData("ext --json list", "ext list")]
    [InlineData("ext --project p list", "ext list")]
    [InlineData("ext --project=p list", "ext list")]
    [InlineData("ext --project p", "ext")]
    [InlineData("agents --json tasks", "agents tasks")]
    [InlineData("agents --dry-run setup t d", "agents setup")]
    [InlineData("render --dry-run surface m.json", "render surface")]
    [InlineData("render --out x.png still m.json", "render still")]
    [InlineData("render --cycle-mean --at-us 1 surface m.json", "render surface")]
    [InlineData("ext list", "ext list")]
    [InlineData("render --progress section m.json", "render section")]
    [InlineData("render --progress 5 section m.json", "render section")]
    public void TheSubVerbIsTheFirstWordThatIsNoFlag(string line, string key)
    {
        Assert.Equal(key, VerbFlags.For(line.Split(' '))?.Key);
    }
    /// <summary>
    /// The word after a bare <c>--progress</c> is its value only where the whole word parses as a
    /// number; anything else is an ordinary positional, so a model whose name begins with a digit
    /// or a point is the model.
    /// </summary>
    /// <remarks>
    /// The first characters were read instead, so <c>2026-lens.json</c> and <c>3d-trap.json</c>
    /// were taken for intervals, and three rounds of machinery - words held, then settled, then a
    /// line refused as ambiguous - existed to recover from that guess for one flag.
    /// </remarks>
    [Theory]
    [InlineData("2026-lens.json")]
    [InlineData("3d-trap.json")]
    [InlineData("./models/r.json")]
    [InlineData("../m.json")]
    [InlineData(".hidden.json")]
    [InlineData("1e3.json")]
    [InlineData("5s")]
    [InlineData("0,5")]
    public void AWordThatIsNotWhollyANumberIsAPositional(string word)
    {
        var parsed = CommandLine.Parse(["run", "--progress", word]);

        Assert.Equal([word], parsed.Positional);
        Assert.True(parsed.Has("progress"));
        Assert.Null(parsed.Value("progress"));
        Assert.Null(Program.CommandLineProblem(["run", "--progress", word]));
        Assert.False(CommandNumber.Parses(word));

        // The sub-verb is found by the same rule: a bare --progress before it.
        Assert.Equal("render section", VerbFlags.For(["render", "--progress", "section", word])?.Key);
    }

    /// <summary>
    /// A word that parses as a number is the value - an overflow to infinity and the text NaN
    /// included, so the flag's reader refuses it by name rather than it being opened as a model.
    /// </summary>
    [Theory]
    [InlineData("5")]
    [InlineData("0")]
    [InlineData(".5")]
    [InlineData("2.5")]
    [InlineData("1e1")]
    [InlineData("-1")]
    [InlineData("1e400")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void AWordThatIsWhollyANumberIsTheValue(string word)
    {
        var parsed = CommandLine.Parse(["run", "m.json", "--progress", word]);

        Assert.Equal(["m.json"], parsed.Positional);
        Assert.Equal(word, parsed.Value("progress"));
        Assert.True(CommandNumber.Parses(word));
        Assert.Equal("render section", VerbFlags.For(["render", "--progress", word, "section", "m.json"])?.Key);
    }

    /// <summary>
    /// A word after a bare <c>--progress</c> that is no number is a positional like any other, so
    /// one too many is refused as the stray it is, named and located.
    /// </summary>
    [Theory]
    [InlineData("run m.json --progress often", "'often'")]
    [InlineData("run --progress soon m.json", "'m.json'")]
    [InlineData("run --progress m.json extra.json", "'extra.json'")]
    [InlineData("run --progress ./m.json ./extra.json", "'./extra.json'")]
    public void AStrayWordAfterABareProgressIsAnExtraArgument(string line, string stray)
    {
        var problem = Program.CommandLineProblem(line.Split(' '));

        Assert.NotNull(problem);
        Assert.Equal("/args/1", problem!.Path);
        Assert.Contains("run was given " + stray + ", which it does not take", problem.Constraint, StringComparison.Ordinal);
        Assert.Null(CommandLine.Parse(line.Split(' ')).Value("progress"));
    }

    /// <summary>
    /// A word that did not parse as a number after a bare <c>--progress</c> is mentioned when the
    /// line then fails, and only where the mention can be right: the word is one of the surplus
    /// arguments, or could be a mistyped number. Nothing is reassigned, and the note is
    /// conditional - the word may have been meant either way.
    /// </summary>
    /// <remarks>
    /// <c>run --progress 5s m.json</c> blamed m.json alone, and <c>run --progress 5s</c> said only
    /// that there was no model called 5s. The first note fired for every such word: for a model
    /// path with a typo in it, and for the word that correctly was the model when a later one was
    /// the stray.
    /// </remarks>
    [Theory]
    [InlineData("--progress 5s m.json", "'5s'")]
    [InlineData("--progress 5s", "'5s'")]
    [InlineData("--progress 0,5 m.json", "'0,5'")]
    [InlineData("m.json --progress often", "'often'")]
    [InlineData("--progress modls/r.json", null)]
    [InlineData("--progress 2026-trap.json", null)]
    [InlineData("--progress 2026-trap.json extra", null)]
    [InlineData("--progress 3d/lens", null)]
    [InlineData("--progress m.json extra", null)]
    [InlineData("--progress 1e400 m.json", null)]
    public void AWordThatFollowedABareProgressIsMentionedOnlyWhereItCanBeRight(string flags, string? word)
    {
        var model = Path.Combine(Path.GetTempPath(), "einzel-bare-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(model, "{}");

        try
        {
            var line = (flags.Replace("m.json", model, StringComparison.Ordinal)).Split(' ');
            var (code, _, stderr) = Cli(["run", .. line]);

            Assert.Equal(1, code);

            if (word is null)
            {
                Assert.DoesNotContain("was meant as --progress's value", stderr, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains($"if {word} was meant as --progress's value, it is not a number", stderr, StringComparison.Ordinal);
                Assert.Contains("--progress=<seconds> or --progress <number>", stderr, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(model);
        }
    }

    /// <summary>An overflow is the value, refused by the reader as no interval rather than opened as a model.</summary>
    [Fact]
    public void AnOverflowIsRefusedAsAnInterval()
    {
        var (code, _, stderr) = Cli("run", "--progress", "1e400", "m.json");

        Assert.Equal(1, code);
        Assert.Contains("--progress takes an interval in seconds, and '1e400' is not one", stderr, StringComparison.Ordinal);
    }

    /// <summary>A later explicit value is the value: the last occurrence on the line, as for any flag.</summary>
    [Fact]
    public void ALaterExplicitValueWins()
    {
        var parsed = CommandLine.Parse(["run", "--progress", "m.json", "--progress", "10"]);

        Assert.Equal("10", parsed.Value("progress"));
        Assert.Equal([null, "10"], parsed.Values("progress"));
        Assert.Equal(["m.json"], parsed.Positional);
    }

    /// <summary>
    /// A verb with no --dry-run refuses it saying so and naming what to use instead: on main
    /// `run --dry-run` was accepted and ignored, and the model ran and wrote its results.
    /// </summary>
    [Fact]
    public void ADryRunAVerbDoesNotHavePointsAtPreviewAndEstimate()
    {
        var problem = Program.CommandLineProblem(["run", "m.json", "--dry-run"]);

        Assert.NotNull(problem);
        Assert.Equal("/args/--dry-run", problem!.Path);
        Assert.Contains("no --dry-run", problem.Constraint, StringComparison.Ordinal);
        Assert.Contains("einzel preview", problem.Suggestion, StringComparison.Ordinal);
        Assert.Contains("einzel estimate", problem.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>The control: where the count fits, a bare --progress is bare and the word is the model.</summary>
    [Fact]
    public void AHeldWordThatFitsIsThePositional()
    {
        var parsed = CommandLine.Parse(["run", "--progress", "m.json"]);

        Assert.Equal(["m.json"], parsed.Positional);
        Assert.Null(parsed.Value("progress"));
    }

    /// <summary>
    /// A command line naming no sub-verb is read with the switches its verb's sub-verbs have:
    /// <c>render --dry-run m.json</c> keeps m.json as a positional, as SubVerb reads it.
    /// </summary>
    [Fact]
    public void AnImplicitSectionReadsItsSwitches()
    {
        var parsed = CommandLine.Parse(["render", "--dry-run", "m.json"]);

        Assert.Equal(["m.json"], parsed.Positional);
        Assert.Null(parsed.Value("dry-run"));
    }

    /// <summary>A flag that takes a value is refused bare at any occurrence, not only its last.</summary>
    [Fact]
    public void EveryOccurrenceOfAValueFlagNeedsItsValue()
    {
        var problem = Program.CommandLineProblem(["outline", "m.json", "--set", "--set", "r=7"]);

        Assert.NotNull(problem);
        Assert.Equal("/args/--set", problem!.Path);
    }

    /// <summary>
    /// An implicit section - a model after the flags under <c>render</c> - is held to the
    /// section's flags and its one model: a typo and a stray second model are refused, and what
    /// the section reads is accepted.
    /// </summary>
    /// <remarks>
    /// The registry had no entry for it, so the checks were skipped: <c>render --dry-run
    /// m.json --widht-mm 80</c> drew the default width with exit 0.
    /// </remarks>
    [Theory]
    [InlineData("render --dry-run m.json --widht-mm 80", "/args/--widht-mm")]
    [InlineData("render --dry-run m.json extra.json", "/args/1")]
    [InlineData("render --progress m.json --no-scale", "/args/--no-scale")]
    [InlineData("render --dry-run m.json --width-mm", "/args/--width-mm")]
    [InlineData("render --dry-run m.json --width-mm 80", null)]
    [InlineData("render --progress m.json --width-mm 80", null)]
    [InlineData("render --out x.svg m.json", null)]
    public void AnImplicitSectionIsHeldToTheSectionsFlags(string line, string? path)
    {
        Assert.Equal(path, Program.CommandLineProblem(line.Split(' '))?.Path);
    }

    /// <summary>A sub-verb its verb does not have is refused, naming those it has.</summary>
    [Theory]
    [InlineData("ext bogus --zzz", "an action ext has", "ext's actions are list, register, test")]
    [InlineData("ext --json bogus", "an action ext has", "ext's actions are list, register, test")]
    [InlineData("agents bogus", "an action agents has", "agents' actions are score, setup, tasks")]
    [InlineData("render bogus --zzz", "a kind render has", "render's kinds are animation, section, still, surface")]
    public void AnUnknownSubVerbIsRefusedNamingTheKnownOnes(string line, string constraint, string suggestion)
    {
        var problem = Program.CommandLineProblem(line.Split(' '));

        Assert.NotNull(problem);
        Assert.Equal("/args/0", problem!.Path);
        Assert.Contains("'bogus' is not " + constraint, problem.Constraint, StringComparison.Ordinal);
        Assert.Contains(suggestion, problem.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>
    /// No flag is a switch in one sub-verb of a family and takes a value in another: the sub-verb
    /// is found before its own table is known, by skipping the family's switches, and a flag of
    /// both kinds would make that search and the parse read one line two ways.
    /// </summary>
    [Fact]
    public void NoFlagIsASwitchInOneSubVerbAndTakesAValueInAnother()
    {
        var conflicts = new List<string>();

        foreach (var verb in new[] { "render", "ext", "agents" })
        {
            var members = VerbFlags.ByVerb.Where(e => e.Key == verb || e.Key.StartsWith(verb + " ", StringComparison.Ordinal)).ToList();
            Assert.True(members.Count > 1, $"{verb} has sub-verbs");

            foreach (var (key, flags) in members)
            {
                foreach (var flag in flags.Switches)
                {
                    conflicts.AddRange(members
                        .Where(m => m.Value.Values.Contains(flag) || m.Value.Optional.Contains(flag))
                        .Select(m => $"--{flag}: a switch in '{key}', takes a value in '{m.Key}'"));
                }
            }
        }

        Assert.True(conflicts.Count == 0, string.Join(Environment.NewLine, conflicts));

        // And the family's switches are exactly what that leaves: a flag taking a value anywhere
        // in the family is not skipped as a switch.
        foreach (var verb in new[] { "render", "ext", "agents" })
        {
            var (switches, _) = VerbFlags.Family(verb)!.Value;
            var taking = VerbFlags.ByVerb.Where(e => e.Key.StartsWith(verb + " ", StringComparison.Ordinal) || e.Key == verb)
                .SelectMany(e => e.Value.Values.Concat(e.Value.Optional));

            Assert.Empty(switches.Intersect(taking));
        }
    }

    /// <summary>
    /// An empty value is refused for both spellings, worded for both; a value of spaces is a value
    /// unless it names a file, which spaces cannot.
    /// </summary>
    [Theory]
    [InlineData(new[] { "run", "m.json", "--project=" }, "/args/--project", "--project takes a value, and the one given is empty")]
    [InlineData(new[] { "run", "m.json", "--project", "" }, "/args/--project", "--project takes a value, and the one given is empty")]
    [InlineData(new[] { "run", "m.json", "--progress", "" }, "/args/--progress", "--progress takes a value, and the one given is empty")]
    [InlineData(new[] { "render", "section", "m.json", "--out", "  " }, "/args/--out", "--out takes a path, and '  ' is only spaces")]
    [InlineData(new[] { "render", "section", "m.json", "--caption", " " }, null, null)]
    [InlineData(new[] { "render", "section", "m.json", "--caption= " }, null, null)]
    public void AnEmptyValueIsRefusedAndASpaceIsAValue(string[] line, string? path, string? constraint)
    {
        var problem = Program.CommandLineProblem(line);

        Assert.Equal(path, problem?.Path);

        if (constraint is not null)
        {
            Assert.Equal(constraint, problem!.Constraint);
            Assert.DoesNotContain("'='", problem.Constraint, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every flag read as a path is marked as one: read through <c>PathValue</c>, which refuses an
    /// unmarked flag, and never through <c>Value</c>, which would let it escape the checks a path
    /// flag gets - a value of spaces refused, a stray argument pointed at it.
    /// </summary>
    [Fact]
    public void EveryFlagReadAsAPathIsMarked()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Einzel.Cli", "Program.cs")))
        {
            directory = directory.Parent;
        }

        var source = string.Concat(Directory.GetFiles(Path.Combine(directory!.FullName, "src", "Einzel.Cli"), "*.cs").Select(File.ReadAllText));

        var asPath = Regex.Matches(source, @"\.PathValue\(""([a-z0-9-]+)""\)").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var asValue = Regex.Matches(source, @"\.Value\(""([a-z0-9-]+)""\)").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(asPath);
        Assert.All(asPath, f => Assert.True(VerbFlags.IsPath(f), $"--{f} is read as a path and not marked in VerbFlags.Paths"));
        Assert.DoesNotContain(asValue, VerbFlags.IsPath);

        // And reading an unmarked flag as a path is refused, so a mark left off cannot pass quietly.
        Assert.Throws<InvalidOperationException>(() => CommandLine.Parse(["run", "m.json"]).PathValue("caption"));
    }

    /// <summary>
    /// An unknown template is located where the asking surface spelled it: at the flag for the
    /// command line, and at <c>/name</c> for a caller that passes no path, as the shell does.
    /// </summary>
    [Fact]
    public void AnUnknownTemplateIsLocatedWhereItWasAskedFor()
    {
        var destination = Path.Combine(Path.GetTempPath(), "einzel-new-" + Guid.NewGuid().ToString("N") + ".json");

        var shell = Assert.Throws<Einzel.Core.Errors.EinzelException>(() => Einzel.Commands.ProjectCommands.New(destination, "template", "no-such-template"));
        Assert.Equal("/name", shell.Error.Path);

        var (code, _, stderr) = Cli("new", destination, "--from-template", "no-such-template");
        Assert.Equal(1, code);
        Assert.Contains(" at /args/--from-template:", stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(destination));
    }
}
