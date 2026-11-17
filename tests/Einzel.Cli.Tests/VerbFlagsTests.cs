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
    private static readonly Regex ReadPattern = new(@"(?:\.Has|\.Value|\.Values)\(""([a-z0-9-]+)""\)|Try\w*Flag\(options, ""([a-z0-9-]+)""", RegexOptions.Compiled);
    private static readonly Regex ValuePattern = new(@"(?:\.Value|\.Values)\(""([a-z0-9-]+)""\)|" + Readers + @"\(options, ""([a-z0-9-]+)""", RegexOptions.Compiled);
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
    public void TheSubVerbIsTheFirstWordThatIsNoFlag(string line, string key)
    {
        Assert.Equal(key, VerbFlags.For(line.Split(' '))?.Key);
    }
}
