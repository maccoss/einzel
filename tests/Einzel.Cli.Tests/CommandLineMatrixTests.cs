using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Every verb, held to one table of command lines: its help, each flag with a value it takes,
/// each value flag bare, each with a value that is none, a flag it does not read, and one
/// positional argument too many - with and without <c>--json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because the gaps were being found one flag at a time.</b> <c>--fps 30fps</c> was
/// an internal error, exit 6, three review rounds after <c>--width-px 1600px</c> stopped being
/// one; a bare <c>--progress</c> was refused the round after bare flags began to be. Each was a
/// property of the whole surface checked at one flag.
/// </para>
/// <para>
/// <b>Driven by the registry</b> (<see cref="VerbFlags.ByVerb"/>), so a verb or a flag added to
/// it is in the matrix the day it is added - and fails here until it has a command line to be
/// run with and a sample value, which is the point: the table cannot fall behind the CLI.
/// </para>
/// <para>
/// What it asserts: nothing exits 6; every refusal exits 1 with an error located at the flag
/// or the argument; under <c>--json</c> a refusal is JSON on stdout carrying the same path,
/// and without it stdout is empty; and a form the verb documents is never refused as a
/// command line.
/// </para>
/// </remarks>
public sealed class CommandLineMatrixTests(ITestOutputHelper output) : IDisposable
{
    private const string Model = "{root}/models/reflectron.json";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-matrix", Guid.NewGuid().ToString("N")).Replace('\\', '/');

    /// <summary>The command line each verb is run with, flags aside.</summary>
    private static readonly Dictionary<string, string[]> Base = new(StringComparer.Ordinal)
    {
        ["init"] = ["{fresh}"],
        ["new"] = ["{root}/models/made.json", "--from-example", "slit-transmission"],
        ["validate"] = [Model],
        ["outline"] = [Model],
        ["project"] = ["{root}"],
        ["estimate"] = [Model, "--no-calibrate"],
        ["solve"] = [Model],
        ["probe"] = [Model, "--at-mm", "0,0,0"],
        ["run"] = [Model, "--progress", "0"],
        ["sweep"] = ["{root}/studies/sweep.json", "--dry-run"],
        ["optimise"] = ["{root}/studies/optimise.json", "--dry-run"],
        ["optimize"] = ["{root}/studies/optimise.json", "--dry-run"],
        ["scan"] = ["{root}/studies/scan.json", "--dry-run"],
        ["boundary"] = ["{root}/studies/boundary.json", "--dry-run"],
        ["preview"] = [Model],
        ["test"] = ["--project", "{root}"],
        ["verify"] = ["--project", "{root}"],
        ["report"] = ["--project", "{root}", "--dry-run"],
        ["export"] = [Model, "--dry-run"],
        ["compare"] = [Model],
        ["schema"] = [],
        ["templates"] = [],
        ["examples"] = [],
        ["agents-md"] = ["--project", "{root}", "--dry-run"],
        ["agents"] = [],
        ["agents tasks"] = [],
        ["agents setup"] = ["no-such-task", "{fresh}", "--dry-run"],
        ["agents score"] = ["no-such-task", "{root}"],
        ["doctor"] = ["--project", "{root}"],
        ["ext"] = ["--project", "{root}"],
        ["ext list"] = ["--project", "{root}"],
        ["ext test"] = ["made", "--project", "{root}"],
        ["ext register"] = ["made", "--project", "{root}", "--dry-run"],
        ["render section"] = [Model, "--dry-run"],
        ["render still"] = [Model, "--dry-run"],
        ["render surface"] = [Model, "--dry-run", "--box-mm", "0,0,10,10"],
        ["render animation"] = ["{root}/figures/film.json", "--dry-run"],
    };

    /// <summary>
    /// For each value flag, a value it takes and one it does not. The second is null where any
    /// text is a value (a caption, a file to write) or names something looked up (a project, an
    /// example): whether that is right depends on what is on disk, not on how it is written.
    /// </summary>
    private static readonly Dictionary<string, (string Good, string? Bad)> Samples = new(StringComparer.Ordinal)
    {
        ["vcs"] = ("none", "hg"),
        ["from-example"] = ("slit-transmission", null),
        ["from-template"] = ("einzel-lens", null),
        ["set"] = ("turningDepth=50", "turningDepth"),
        ["project"] = ("{root}", null),
        ["threshold"] = ("100", "abc"),
        ["at-mm"] = ("0,0,0", "1,2"),
        ["line-mm"] = ("0,0,0", "abc"),
        ["to-mm"] = ("10,0,0", "abc"),
        ["samples"] = ("5", "2.5"),
        ["at-us"] = ("1", "5us"),
        ["csv"] = ("{root}/out.csv", null),
        ["progress"] = ("0", "soon"),
        ["out"] = ("{root}/out.bin", null),
        ["box-mm"] = ("0,0,0,1,1,1", "1,2"),
        ["step-mm"] = ("1", "abc"),
        ["ions"] = ("5", "many"),
        ["input"] = ("{root}/input.json", null),
        ["kind"] = ("analysis", "bogus"),
        ["caption"] = ("a caption", null),
        ["equipotentials"] = ("5", "-3"),
        ["format"] = ("pdf", "pfd"),
        ["width-mm"] = ("80", "wide"),
        ["height-px"] = ("64", "1000px"),
        ["width-px"] = ("64", "+64"),
        ["view"] = ("top", "upward"),
        ["height"] = ("energy", "tall"),
        ["height-mm-per-ev"] = ("1", "abc"),
        ["height-mm-per-v"] = ("1", "abc"),
        ["mesh-lines"] = ("10", "-1"),
        ["offset-mm"] = ("0", "abc"),
        ["plane"] = ("xy", "zz"),
        ["relief"] = ("0.2", "abc"),
        ["fps"] = ("24", "30fps"),
    };

    /// <summary>Where a flag belongs to another form of its verb, the command line of that form.</summary>
    private static readonly Dictionary<(string Verb, string Flag), string[]> Forms = new()
    {
        [("new", "from-template")] = ["{root}/models/made.json"],
        [("new", "from-example")] = ["{root}/models/made.json"],
        [("outline", "set")] = [Model, "--dry-run"],
        [("probe", "at-mm")] = [Model],
        [("probe", "line-mm")] = [Model, "--to-mm", "10,0,0"],
        [("probe", "to-mm")] = [Model, "--line-mm", "0,0,0"],
        [("probe", "samples")] = [Model, "--line-mm", "0,0,0", "--to-mm", "10,0,0"],
        [("probe", "csv")] = [Model, "--line-mm", "0,0,0", "--to-mm", "10,0,0", "--dry-run"],
        [("run", "progress")] = [Model],
        [("export", "box-mm")] = [Model, "--dry-run", "--composite"],
        [("export", "step-mm")] = [Model, "--dry-run", "--composite"],
        [("export", "at-us")] = [Model, "--dry-run", "--composite"],
        [("ext", "project")] = [],
        [("ext list", "project")] = [],
        [("ext test", "project")] = ["made"],
        [("ext register", "project")] = ["made", "--dry-run"],
        [("test", "project")] = [],
        [("verify", "project")] = [],
        [("report", "project")] = ["--dry-run"],
        [("agents-md", "project")] = ["--dry-run"],
        [("doctor", "project")] = [],
        [("project", "project")] = [],
        [("render surface", "height-mm-per-ev")] = [Model, "--dry-run", "--box-mm", "0,0,10,10", "--height", "energy"],
    };

    /// <summary>Where a flag's value is written differently in one verb.</summary>
    private static readonly Dictionary<(string Verb, string Flag), (string Good, string? Bad)> Overrides = new()
    {
        [("render surface", "box-mm")] = ("0,0,10,10", "1,2"),
        [("render surface", "samples")] = ("41x9", "5x"),
    };

    private sealed record Case(string Verb, string Kind, string[] Args, string? RefusedAt);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The whole matrix.</summary>
    [Fact]
    public void EveryVerbHoldsToTheMatrix()
    {
        // The table covers the registry, or the matrix is about fewer verbs than the CLI has.
        Assert.Equal(VerbFlags.ByVerb.Keys.Order(StringComparer.Ordinal), Base.Keys.Order(StringComparer.Ordinal));

        foreach (var flag in VerbFlags.ByVerb.Values.SelectMany(f => f.Values.Concat(f.Optional)).Distinct(StringComparer.Ordinal))
        {
            Assert.True(Samples.ContainsKey(flag), $"--{flag} takes a value and the matrix has no sample for it");
        }

        Project();

        var violations = new List<string>();
        var ran = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var given in Cases())
        {
            foreach (var json in new[] { false, true })
            {
                string[] args = json ? [.. given.Args, "--json"] : given.Args;
                var (code, stdout, stderr) = Run(args);
                var line = $"einzel {string.Join(' ', args)}";

                ran[given.Kind] = ran.GetValueOrDefault(given.Kind) + 1;

                if (code == 6)
                {
                    violations.Add($"EXIT 6: {line}\n    {FirstLine(stderr)}");
                    continue;
                }

                if (given.RefusedAt is not { } path)
                {
                    if (stderr.Contains(" at /args/", StringComparison.Ordinal))
                    {
                        violations.Add($"A FORM THE VERB TAKES WAS REFUSED: {line}\n    {FirstLine(stderr)}");
                    }

                    continue;
                }

                if (code != 1 || !stderr.Contains($" at {path}:", StringComparison.Ordinal))
                {
                    violations.Add($"NOT REFUSED AT {path} (exit {code}): {line}\n    {FirstLine(stderr)}");
                    continue;
                }

                if (!json)
                {
                    if (stdout.Length > 0)
                    {
                        violations.Add($"A REFUSAL WROTE TO STDOUT: {line}");
                    }

                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(stdout);
                    var error = document.RootElement.GetProperty("errors")[0];

                    if (error.GetProperty("path").GetString() != path
                        || document.RootElement.GetProperty("ran").GetBoolean())
                    {
                        violations.Add($"THE JSON REFUSAL IS NOT AT {path}: {line}\n    {stdout}");
                    }
                }
                catch (Exception malformed) when (malformed is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    violations.Add($"A --json REFUSAL IS NOT JSON: {line}\n    {FirstLine(stdout)}");
                }
            }
        }

        output.WriteLine(string.Join(", ", ran.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Value} {r.Key}")));

        // The control: a matrix that ran nothing of one kind passes every assertion about it.
        foreach (var kind in new[] { "help", "base", "valid", "bare", "bare-optional", "garbage", "unknown", "extra" })
        {
            Assert.True(ran.GetValueOrDefault(kind) > 0, $"the matrix ran no '{kind}' case");
        }

        Assert.True(violations.Count == 0, $"{violations.Count} of {ran.Values.Sum()} command lines:\n" + string.Join("\n", violations));
    }

    private static IEnumerable<Case> Cases()
    {
        foreach (var (verb, flags) in VerbFlags.ByVerb.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            var words = verb.Split(' ');
            var given = Base[verb];

            yield return new Case(verb, "help", [.. words, "--help"], null);
            yield return new Case(verb, "base", [.. words, .. given], null);

            foreach (var flag in flags.Switches.Order(StringComparer.Ordinal))
            {
                // A switch the scratch model cannot be asked for is refused by the command, at
                // the switch, and that refusal is its own test: a cycle mean of a static field.
                if (!given.Contains("--" + flag) && (verb, flag) != ("render surface", "cycle-mean"))
                {
                    yield return new Case(verb, "valid", [.. words, .. given, "--" + flag], null);
                }
            }

            foreach (var flag in flags.Values.Concat(flags.Optional).Order(StringComparer.Ordinal))
            {
                var form = Forms.GetValueOrDefault((verb, flag), given);
                var (good, bad) = Overrides.TryGetValue((verb, flag), out var own) ? own : Samples[flag];
                var optional = flags.Optional.Contains(flag);

                yield return new Case(verb, "valid", [.. words, .. form, "--" + flag, good], null);

                // Bare, a flag that takes a value is refused by name; one whose value may be
                // left off is taken as its default.
                yield return new Case(
                    verb, optional ? "bare-optional" : "bare", [.. words, .. form, "--" + flag], optional ? null : "/args/--" + flag);

                if (bad is not null)
                {
                    yield return new Case(verb, "garbage", [.. words, .. form, "--" + flag, bad], "/args/--" + flag);
                }
            }

            yield return new Case(verb, "unknown", [.. words, .. given, "--no-such-flag"], "/args/--no-such-flag");

            // A verb with sub-verbs, given none: its first argument is read as the sub-verb, and
            // one that is none is the verb's own "unknown action", not an argument too many.
            if (verb is "ext" or "agents")
            {
                continue;
            }

            // One more positional argument than the verb takes, whatever the base already gives.
            var positional = CommandLine.Parse([.. words, .. given]).Positional.Count;
            var extra = Enumerable.Repeat("extra-positional", flags.Positionals - positional + 1);

            yield return new Case(
                verb,
                "extra",
                [.. words, .. given, .. extra],
                "/args/" + flags.Positionals.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A scratch project: the scaffolded model, a study of each kind, an animation spec.</summary>
    private void Project()
    {
        Assert.Equal(0, Program.Main(["init", _root]));

        Directory.CreateDirectory(Path.Combine(_root, "studies"));
        Directory.CreateDirectory(Path.Combine(_root, "figures"));

        const string Study = """{ "schemaVersion": "0.1", "name": "s", "model": "../models/reflectron.json", "figureOfMerit": "flightTime", """;

        File.WriteAllText(
            Path.Combine(_root, "studies", "sweep.json"),
            Study + """ "channels": [{ "parameter": "turningDepth", "halfWidth": 0.1, "unit": "mm", "distribution": "normal" }], "draws": 2, "seed": 1 }""");

        File.WriteAllText(
            Path.Combine(_root, "studies", "optimise.json"),
            Study + """ "variables": [{ "parameter": "turningDepth", "minimum": 45, "maximum": 55, "unit": "mm" }], "maximumEvaluations": 2 }""");

        File.WriteAllText(
            Path.Combine(_root, "studies", "scan.json"),
            Study + """ "scan": { "parameter": "turningDepth", "from": 45, "to": 55, "unit": "mm", "points": 2 } }""");

        File.WriteAllText(
            Path.Combine(_root, "studies", "boundary.json"),
            Study + """ "boundary": { "parameter": "turningDepth", "from": 45, "to": 55, "unit": "mm", "threshold": 1e-5, "inside": "below", "maximumEvaluations": 2 } }""");

        File.WriteAllText(
            Path.Combine(_root, "figures", "film.json"),
            """
            {
              "renderSpecVersion": "0.1",
              "model": "../models/reflectron.json",
              "animation": { "framesPerSecond": 2, "phases": [
                { "until": { "value": 2, "unit": "us" }, "rate": { "value": 1, "unit": "us/s" } } ] }
            }
            """);

        File.WriteAllText(Path.Combine(_root, "input.json"), "{}");
    }

    private (int ExitCode, string Stdout, string Stderr) Run(string[] args)
    {
        var fresh = Path.Combine(_root, "fresh-" + Guid.NewGuid().ToString("N"));

        // What an earlier case wrote, so that each command line meets the same project.
        foreach (var stale in new[] { "models/made.json", "out.csv", "out.bin" })
        {
            File.Delete(Path.Combine(_root, stale));
        }

        string[] resolved =
        [
            .. args.Select(a => a
                .Replace("{root}", _root, StringComparison.Ordinal)
                .Replace("{fresh}", fresh, StringComparison.Ordinal)),
        ];

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var previousDirectory = Directory.GetCurrentDirectory();

        try
        {
            // A verb given no project looks for one from the working directory.
            Directory.SetCurrentDirectory(_root);
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(resolved), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            Directory.SetCurrentDirectory(previousDirectory);

            if (Directory.Exists(fresh))
            {
                Directory.Delete(fresh, recursive: true);
            }
        }
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "(nothing)";

    /// <summary>
    /// A whole-number flag is digits alone, within its own range: no sign, no space, no
    /// exponent, and no count below what the flag can mean.
    /// </summary>
    /// <param name="line">The command line after the model, space separated; <c>_</c> stands for a space inside a value.</param>
    /// <param name="flag">The flag refused.</param>
    /// <remarks>
    /// The shared reader took the place of the still's own, and with it <c>NumberStyles.None</c>
    /// became <c>Integer</c>: <c>--width-px " 1600"</c> and <c>--samples +241</c> began to be
    /// accepted, and <c>--equipotentials -3</c> had never been checked at all.
    /// </remarks>
    [Theory]
    [InlineData("render still {model} --dry-run --width-px _1600", "width-px")]
    [InlineData("render still {model} --dry-run --width-px +1600", "width-px")]
    [InlineData("render still {model} --dry-run --height-px 1e3", "height-px")]
    [InlineData("render still {model} --dry-run --height-px 8", "height-px")]
    [InlineData("render still {model} --dry-run --height-px 99999999999", "height-px")]
    [InlineData("render surface {model} --dry-run --samples +241", "samples")]
    [InlineData("render surface {model} --dry-run --samples 41x+9", "samples")]
    [InlineData("render surface {model} --dry-run --samples 2", "samples")]
    [InlineData("render surface {model} --dry-run --mesh-lines 0", "mesh-lines")]
    [InlineData("render section {model} --dry-run --equipotentials -3", "equipotentials")]
    [InlineData("render section {model} --dry-run --equipotentials 3.0", "equipotentials")]
    [InlineData("probe {model} --line-mm 0,0,0 --to-mm 1,0,0 --samples 1", "samples")]
    [InlineData("probe {model} --line-mm 0,0,0 --to-mm 1,0,0 --samples -5", "samples")]
    [InlineData("compare {model} --ions 0", "ions")]
    [InlineData("compare {model} --ions many", "ions")]
    [InlineData("render animation {film} --dry-run --fps 30fps", "fps")]
    [InlineData("render animation {film} --dry-run --fps 0", "fps")]
    [InlineData("render animation {film} --dry-run --width-mm wide", "width-mm")]
    public void AWholeNumberFlagIsDigitsAloneInItsRange(string line, string flag)
    {
        Project();

        var (code, _, stderr) = Run(Words(line));

        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains($" at /args/--{flag}:", stderr, StringComparison.Ordinal);
    }

    /// <summary>The control for the theory above: the same flags, written as they are documented, are taken.</summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("render still {model} --dry-run --width-px 1600 --height-px 16")]
    [InlineData("render surface {model} --dry-run --box-mm 0,0,10,10 --samples 241 --mesh-lines 1")]
    [InlineData("render surface {model} --dry-run --box-mm 0,0,10,10 --samples 41x2")]
    [InlineData("render section {model} --dry-run --equipotentials 0")]
    [InlineData("render section {model} --dry-run --format PDF")]
    [InlineData("probe {model} --line-mm 0,0,0 --to-mm 1,0,0 --samples 2")]
    [InlineData("render animation {film} --dry-run --fps 24 --width-mm 80")]
    public void TheSameFlagsAsDocumentedAreTaken(string line)
    {
        Project();

        var (code, _, stderr) = Run(Words(line));

        Assert.True(code == 0, stderr);
    }

    /// <summary>
    /// A bare <c>--progress</c> is the default interval, as it was before bare flags were
    /// refused, on each verb that reports progress; with a number it is that interval.
    /// </summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("run {model} --progress")]
    [InlineData("run {model} --progress --json")]
    [InlineData("run {model} --progress 0")]
    [InlineData("render section {model} --dry-run --progress")]
    [InlineData("render section {model} --progress 0.5 --dry-run")]
    [InlineData("render animation {film} --dry-run --progress")]
    [InlineData("render animation {film} --dry-run --progress 2")]
    [InlineData("run --progress {model}")]
    [InlineData("run --progress {model} --json")]
    [InlineData("render section --progress {model} --dry-run")]
    [InlineData("render --progress section {model} --dry-run")]
    [InlineData("render animation --progress {film} --dry-run")]
    [InlineData("run --progress .5 {model}")]
    public void ABareProgressIsTheDefaultInterval(string line)
    {
        Project();

        var (code, _, stderr) = Run(Words(line));

        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("/args/", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The control for the third kind: a value that is no interval is still refused, and a flag
    /// that is not of that kind is still refused bare.
    /// </summary>
    [Fact]
    public void OnlyAnOptionalValueMayBeLeftOff()
    {
        Project();

        var (code, _, stderr) = Run(Words("run {model} --progress soon"));

        Assert.Equal(1, code);
        Assert.Contains(" at /args/--progress:", stderr, StringComparison.Ordinal);
        Assert.Contains("'soon'", stderr, StringComparison.Ordinal);
        Assert.Contains("Use 0 to turn it off", stderr, StringComparison.Ordinal);

        var (bare, _, bareError) = Run(Words("run {model} --project"));

        Assert.Equal(1, bare);
        Assert.Contains("--project takes a value", bareError, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--height</c> takes its two documented words and nothing else, and a refusal quotes
    /// what was typed.
    /// </summary>
    /// <remarks>
    /// It went through a lookup that also took <c>potential-energy</c> - the word a surface's
    /// outcome records, never a documented spelling - and the refusal quoted the value the
    /// lookup mapped to instead of the text given.
    /// </remarks>
    [Fact]
    public void HeightTakesItsDocumentedWordsAndQuotesWhatWasTyped()
    {
        Project();

        foreach (var typed in new[] { "potential-energy", "tall", "Energy" })
        {
            var (code, _, stderr) = Run(Words($"render surface {{model}} --dry-run --height {typed}"));

            Assert.Equal(1, code);
            Assert.Contains(" at /args/--height:", stderr, StringComparison.Ordinal);
            Assert.Contains($"'{typed}'", stderr, StringComparison.Ordinal);
            Assert.Contains("potential, energy", stderr, StringComparison.Ordinal);
        }

        Assert.Equal(["potential", "energy"], Einzel.Commands.SurfaceHeight.Spellings);
        Assert.Equal(Einzel.Commands.SurfaceHeight.Energy, Einzel.Commands.SurfaceHeight.Parse("energy"));
        Assert.Equal(Einzel.Commands.SurfaceHeight.Potential, Einzel.Commands.SurfaceHeight.Parse("potential"));
        Assert.Throws<ArgumentException>(() => Einzel.Commands.SurfaceHeight.Parse("potential-energy"));

        var (taken, stdout, error) = Run(Words("render surface {model} --dry-run --box-mm 0,0,10,10 --height energy --json"));

        Assert.True(taken == 0, error);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(Einzel.Commands.SurfaceHeight.Energy, document.RootElement.GetProperty("height").GetString());
    }

    /// <summary>
    /// A flag that names one of a few words refuses any other, where it used to fall back to a
    /// default and say nothing.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="flag">The flag refused.</param>
    /// <param name="typed">What was typed, which the refusal quotes.</param>
    [Theory]
    [InlineData("render section {model} --dry-run --format pfd", "format", "pfd")]
    [InlineData("render animation {film} --dry-run --format pfd", "format", "pfd")]
    [InlineData("ext register made --project {root} --dry-run --kind objectiv", "kind", "objectiv")]
    [InlineData("ext register made --project {root} --dry-run --kind 1", "kind", "1")]
    [InlineData("init {fresh} --vcs hg", "vcs", "hg")]
    [InlineData("render still {model} --dry-run --view upward", "view", "upward")]
    [InlineData("render surface {model} --dry-run --plane zz", "plane", "zz")]
    public void AWordThatIsNoneOfAFlagsWordsIsRefused(string line, string flag, string typed)
    {
        Project();

        var (code, stdout, stderr) = Run([.. Words(line), "--json"]);

        Assert.Equal(1, code);
        Assert.Contains(" at /args/--" + flag + ":", stderr, StringComparison.Ordinal);
        Assert.Contains($"'{typed}'", stderr, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("/args/--" + flag, document.RootElement.GetProperty("errors")[0].GetProperty("path").GetString());
    }

    /// <summary>
    /// <c>--relief</c> and an explicit height scale are two ways of saying how tall, and giving
    /// both is refused: the relief used to be ignored without a word.
    /// </summary>
    /// <param name="scale">The explicit scale's flag and what else it needs.</param>
    [Theory]
    [InlineData("--height-mm-per-v 1")]
    [InlineData("--height energy --height-mm-per-ev 1")]
    public void ReliefAndAnExplicitScaleAreNotGivenTogether(string scale)
    {
        Project();

        var (code, _, stderr) = Run(Words($"render surface {{model}} --dry-run --box-mm 0,0,10,10 --relief 0.2 {scale}"));

        Assert.Equal(1, code);
        Assert.Contains(" at /args/--relief:", stderr, StringComparison.Ordinal);
        Assert.Contains(scale.Split(' ')[^2], stderr, StringComparison.Ordinal);

        // The control: each alone is taken.
        Assert.Equal(0, Run(Words("render surface {model} --dry-run --box-mm 0,0,10,10 --relief 0.2")).ExitCode);
        Assert.Equal(0, Run(Words($"render surface {{model}} --dry-run --box-mm 0,0,10,10 {scale}")).ExitCode);
    }

    /// <summary>
    /// The suggestion for an argument a verb does not take is made from that verb's own flags.
    /// </summary>
    /// <remarks>
    /// Every verb was told "a file to write is --out &lt;file&gt;, and a switch such as --json or
    /// --no-scale takes no value": <c>einzel run m.json --vtu out.vtu</c> was sent to
    /// <c>--out</c>, which <c>run</c> then refused.
    /// </remarks>
    [Fact]
    public void TheSuggestionForAStrayArgumentNamesTheVerbsOwnFlags()
    {
        Project();

        var (code, _, stderr) = Run(Words("run {model} --vtu out.vtu"));

        Assert.Equal(1, code);
        Assert.Contains("'out.vtu'", stderr, StringComparison.Ordinal);
        Assert.Contains("--vtu", stderr, StringComparison.Ordinal);
        Assert.Contains("--progress", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("--out", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-scale", stderr, StringComparison.Ordinal);

        // Where the verb does write a file where it is told, the suggestion says with which flag.
        var (_, _, surface) = Run(Words("render surface {model} --dry-run --no-scale out.png"));

        Assert.Contains("a file to write is --out <file>", surface, StringComparison.Ordinal);
        Assert.Contains("--no-scale", surface, StringComparison.Ordinal);

        var (_, _, probe) = Run(Words("probe {model} --at-mm 0,0,0 extra"));

        Assert.Contains("a file to write is --csv <file>", probe, StringComparison.Ordinal);

        // Every flag a suggestion names is one the verb reads.
        foreach (var (verb, flags) in VerbFlags.ByVerb.Where(v => v.Key is not ("ext" or "agents")))
        {
            foreach (System.Text.RegularExpressions.Match named in Regex.Matches(Program.CommandLineProblem([.. verb.Split(' '), .. Enumerable.Repeat("x", flags.Positionals + 1)])!.Suggestion!, @"--([a-z0-9-]+)"))
            {
                Assert.True(flags.Knows(named.Groups[1].Value), $"{verb}: the suggestion names --{named.Groups[1].Value}");
            }
        }
    }

    /// <summary>
    /// <c>einzel schema --units</c> lists the unit symbols a document may use, from the registry
    /// that reads them - which is what an unknown unit's error has always told its reader to run.
    /// </summary>
    [Fact]
    public void SchemaUnitsListsTheUnitRegistry()
    {
        Project();

        var (code, stdout, stderr) = Run(["schema", "--units"]);

        Assert.True(code == 0, stderr);
        Assert.Matches(@"(?m)^mm\s+m\s+0\.001\s*$", stdout);
        Assert.Matches(@"(?m)^kV\s", stdout);

        var (jsonCode, json, _) = Run(["schema", "--units", "--json"]);

        Assert.Equal(0, jsonCode);
        using var document = JsonDocument.Parse(json);
        var units = document.RootElement.GetProperty("units").EnumerateArray().ToList();

        // Every symbol the registry knows, and nothing else.
        Assert.Equal(
            Einzel.Core.Units.UnitRegistry.KnownSymbols.Order(StringComparer.Ordinal),
            units.Select(u => u.GetProperty("symbol").GetString()!).Order(StringComparer.Ordinal));

        var mm = units.Single(u => u.GetProperty("symbol").GetString() == "mm");
        Assert.Equal("m", mm.GetProperty("dimension").GetString());
        Assert.Equal(1e-3, mm.GetProperty("siFactor").GetDouble());

        // And the suggestion that names it is a command line the CLI takes.
        var unknown = Assert.Throws<Einzel.Core.Errors.EinzelException>(() => Einzel.Core.Units.UnitRegistry.Resolve("furlong"));
        Assert.Contains("'einzel schema --units'", unknown.Error.Suggestion, StringComparison.Ordinal);
        Assert.Null(Program.CommandLineProblem(["schema", "--units"]));
    }

    /// <summary>
    /// A flag belongs to the sub-verb that reads it: one sub-verb's flag given to another is
    /// refused, where it was accepted and read by nothing.
    /// </summary>
    /// <param name="line">The command line.</param>
    /// <param name="flag">The flag refused.</param>
    [Theory]
    [InlineData("ext list --project {root} --kind objective", "kind")]
    [InlineData("ext list --project {root} --input {root}/input.json", "input")]
    [InlineData("ext --project {root} --dry-run", "dry-run")]
    [InlineData("ext test made --project {root} --kind objective", "kind")]
    [InlineData("ext test made --project {root} --dry-run", "dry-run")]
    [InlineData("ext register made --project {root} --dry-run --input {root}/input.json", "input")]
    [InlineData("agents tasks --dry-run", "dry-run")]
    [InlineData("agents --dry-run", "dry-run")]
    [InlineData("agents score no-such-task {root} --dry-run", "dry-run")]
    public void AFlagBelongsToTheSubVerbThatReadsIt(string line, string flag)
    {
        Project();

        var (code, _, stderr) = Run(Words(line));

        Assert.Equal(1, code);
        Assert.Contains($" at /args/--{flag}:", stderr, StringComparison.Ordinal);
        Assert.Contains("unknown flag", stderr, StringComparison.Ordinal);
    }

    /// <summary>The control for the theory above: each sub-verb takes its own flags.</summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("ext list --project {root}")]
    [InlineData("ext --project {root} --json")]
    [InlineData("ext register made --project {root} --kind analysis --dry-run")]
    [InlineData("agents tasks")]
    [InlineData("agents")]
    [InlineData("ext --json list --project {root}")]
    [InlineData("ext --project {root} list")]
    [InlineData("ext --project {root} --json list")]
    [InlineData("agents --json tasks")]
    [InlineData("render --dry-run section {model}")]
    [InlineData("render --dry-run --width-px 64 --height-px 64 still {model}")]
    public void EachSubVerbTakesItsOwnFlags(string line)
    {
        Project();

        var (code, _, stderr) = Run(Words(line));

        Assert.True(code == 0, stderr);
    }

    /// <summary>
    /// Under <c>--json</c> every refusal is JSON on stdout, whichever layer raised it - and one
    /// document, never two.
    /// </summary>
    /// <param name="line">The command line, refused by the command and not by the command line.</param>
    /// <param name="path">Where the first error is located.</param>
    /// <remarks>
    /// A refusal made in the command line was JSON; one thrown from a command - a relief of
    /// zero, a box written backwards, a cycle mean of a static field, a destination that cannot
    /// be written - left stdout empty, so an agent had an exit code and nothing to parse.
    /// </remarks>
    [Theory]
    [InlineData("render surface {model} --dry-run --box-mm 0,0,10,10 --relief 0", "/args/--relief")]
    [InlineData("render surface {model} --dry-run --box-mm 10,10,0,0", "/args/--box-mm")]
    [InlineData("render surface {model} --dry-run --box-mm 0,0,10,10 --cycle-mean", "/args/--cycle-mean")]
    [InlineData("render surface {model} --dry-run --box-mm 0,0,10,10 --plane zz", "/args/--plane")]
    [InlineData("render surface {model} --dry-run", "/args/--box-mm")]
    [InlineData("render surface {model} --box-mm 0,0,10,10 --samples 21 --out {root}", "/args/--out")]
    [InlineData("render surface {model} --box-mm 0,0,10,10 --samples 21 --out {model}", "/args/--out")]
    [InlineData("render still {model} --width-px 32 --height-px 32 --out {root}", "/args/--out")]
    [InlineData("render still {model} --dry-run --out {root}", "/args/--out")]
    [InlineData("render section {model} --dry-run --out {root}", "/args/--out")]
    [InlineData("render section {film} --dry-run --out {film}", "/args/--out")]
    [InlineData("render animation {film} --dry-run --out {model}", "/args/--out")]
    [InlineData("render animation {film} --dry-run --out {film}", "/args/--out")]
    [InlineData("probe {model} --at-mm 0,0,0 --dry-run --csv {model}", "/args/--csv")]
    [InlineData("probe {model} --at-mm 0,0,0 --dry-run --csv {root}", "/args/--csv")]
    [InlineData("render section {model} --at-us -1 --dry-run", "/render/at-us")]
    [InlineData("probe {model} --at-mm 0,0,0 --at-us -1", "/probe/at-us")]
    [InlineData("new {root}/models/made.json --from-example no-such-example", null)]
    public void EveryRefusalUnderJsonIsOneJsonDocument(string line, string? path)
    {
        Project();

        var (code, stdout, stderr) = Run([.. Words(line), "--json"]);

        output.WriteLine(stderr);

        Assert.Equal(1, code);

        // One document: a second one after it would not parse.
        using var document = JsonDocument.Parse(stdout);
        var errors = document.RootElement.GetProperty("errors");

        Assert.False(document.RootElement.GetProperty("ran").GetBoolean());
        Assert.True(errors.GetArrayLength() > 0);
        Assert.False(string.IsNullOrEmpty(errors[0].GetProperty("code").GetString()));

        if (path is not null)
        {
            Assert.Equal(path, errors[0].GetProperty("path").GetString());
        }

        // And without --json, stdout is left alone, as it was.
        Assert.Equal(string.Empty, Run(Words(line)).Stdout);
    }

    /// <summary>
    /// A refused <c>--mesh-lines</c> states the range the surface takes, which starts at one: the
    /// reader took 0 as its minimum while the request check refused anything below 1, so the
    /// message advertised a value the command then refused.
    /// </summary>
    [Fact]
    public void MeshLinesAdvertisesTheRangeItTakes()
    {
        Project();

        var (code, _, stderr) = Run(Words("render surface {model} --dry-run --box-mm 0,0,10,10 --mesh-lines -1"));
        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains("1 or more", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("0 or more", stderr, StringComparison.Ordinal);

        // And 0 is refused by the reader, with the same range, not by a second rule after it.
        var (zero, _, zeroError) = Run(Words("render surface {model} --dry-run --box-mm 0,0,10,10 --mesh-lines 0"));
        Assert.Equal(1, zero);
        Assert.Contains("1 or more", zeroError, StringComparison.Ordinal);
    }

    /// <summary>
    /// No figure is written over a file the command reads - the render spec it was handed as well
    /// as the model - and a frame folder holding the spec under the manifest's name is refused
    /// before a frame is drawn.
    /// </summary>
    /// <remarks>
    /// The guard covered only the model, so <c>render section fig.json --out fig.json</c> drew the
    /// figure and wrote it over the spec that described it; and an animation's folder was checked
    /// against nothing, so a spec called <c>frames.json</c> beside the frames was replaced by the
    /// manifest.
    /// </remarks>
    [Fact]
    public void NoFigureIsWrittenOverTheSpecItWasDrawnFrom()
    {
        Project();

        var film = Path.Combine(_root, "figures", "film.json");
        var before = File.ReadAllText(film);

        var (code, _, stderr) = Run(Words("render section {film} --out {film}"));
        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains(" at /args/--out:", stderr, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(film));

        // A spec named as the manifest is named, in the folder the frames go to.
        var frames = Path.Combine(_root, "figures", "frames.json");
        File.Copy(film, frames);

        var (filmCode, _, filmError) = Run(Words("render animation {root}/figures/frames.json --out {root}/figures"));
        output.WriteLine(filmError);

        Assert.Equal(1, filmCode);
        Assert.Contains(" at /args/--out:", filmError, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(frames));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "figures"), "frame-*"));

        // The control: the same spec, frames into a folder of their own.
        Assert.Equal(0, Run(Words("render animation {root}/figures/frames.json --out {root}/films")).ExitCode);
        Assert.Equal(before, File.ReadAllText(frames));
    }

    /// <summary>
    /// A verb that writes its own answer under <c>--json</c> writes one document, whether the
    /// answer is a result or a list of what is wrong.
    /// </summary>
    [Fact]
    public void AVerbThatAnswersForItselfWritesOneDocument()
    {
        Project();

        var broken = Path.Combine(_root, "models", "broken.json");
        File.WriteAllText(broken, File.ReadAllText(Path.Combine(_root, "models", "reflectron.json")).Replace("\"kV\"", "\"furlong\"", StringComparison.Ordinal));

        foreach (var verb in new[] { "validate", "run", "solve", "preview" })
        {
            var (code, stdout, _) = Run([verb, broken, "--json"]);

            Assert.NotEqual(0, code);
            Assert.NotEqual(6, code);

            using var document = JsonDocument.Parse(stdout);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }

    private static string[] Words(string line) =>
        [
            .. line
                .Replace("{model}", Model, StringComparison.Ordinal)
                .Replace("{film}", "{root}/figures/film.json", StringComparison.Ordinal)
                .Split(' ')
                .Select(w => w.Replace('_', ' ')),
        ];

    /// <summary>
    /// No number or enumeration on the command line is parsed anywhere but the two readers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>Parse</c> on what somebody typed throws on a typo, and the throw reaches the handler
    /// for defects in the engine: <c>--fps 30fps</c> and <c>--ions many</c> were exit 6 with a
    /// stack trace and a request to report a bug. A <c>TryParse</c> of its own is quieter and
    /// still a second rule - which is how one flag came to take " 1600" and another not.
    /// </para>
    /// <para>
    /// So the CLI's source may parse a number in <c>TryNumber</c> and <c>TryWhole</c> and nowhere
    /// else, and may not call a throwing <c>Parse</c> or <c>Convert</c> on text at all. Read
    /// from the source, like the registry's own test, so that it holds for a flag nobody has
    /// written yet.
    /// </para>
    /// </remarks>
    [Fact]
    public void NumbersOnTheCommandLineAreParsedByTheReadersAlone()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Einzel.Cli", "Program.cs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var source = string.Concat(Directory.GetFiles(Path.Combine(directory!.FullName, "src", "Einzel.Cli"), "*.cs")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText)).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Empty(Throwing(source));

        // Every TryParse sits in one of the two readers.
        var starts = Regex.Matches(source, @"\n    (?:private|public|internal) static [^\n(]*? (\w+)\(").ToList();
        var parsers = new List<string>();

        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : source.Length;

            if (source[starts[i].Index..end].Contains("TryParse(", StringComparison.Ordinal))
            {
                parsers.Add(starts[i].Groups[1].Value);
            }
        }

        output.WriteLine("TryParse in: " + string.Join(", ", parsers));

        // The control: the scan finds the readers, so an empty list above is not a scan that
        // matched nothing.
        Assert.Equal(["TryNumber", "TryWhole"], parsers.Order(StringComparer.Ordinal));

        // And it has teeth: the lines this replaced are caught.
        Assert.Single(Throwing("FramesPerSecond = int.Parse(fps, CultureInfo.InvariantCulture)"));
        Assert.Single(Throwing("? double.Parse(width, CultureInfo.InvariantCulture)"));
        Assert.Single(Throwing("Enum.Parse<ExtensionKind>(text)"));
        Assert.Single(Throwing("Convert.ToInt32(count)"));
        Assert.Empty(Throwing("var options = CommandLine.Parse(args); JsonNode.Parse(text); JsonDocument.Parse(text);"));
    }

    private static List<string> Throwing(string source) =>
        [
            .. Regex.Matches(
                    source,
                    @"\b(?:s?byte|u?short|u?int|u?long|float|double|decimal|bool|U?Int(?:16|32|64)|Single|Double|Decimal|Boolean|Enum|DateTime|DateTimeOffset|TimeSpan|Guid|BigInteger|Half)\.Parse(?:Exact)?\s*[<(]|\bConvert\.(?:To|ChangeType)\w*\s*\(")
                .Select(m => m.Value),
        ];
}
