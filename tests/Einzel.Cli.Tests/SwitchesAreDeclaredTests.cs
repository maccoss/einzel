using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The command line's switches - flags that never take a value - are every switch any verb
/// reads, and no others.
/// </summary>
/// <remarks>
/// The parser cannot tell a switch from an option by looking at it, so
/// <see cref="CommandLine.Switches"/> is the only thing that stops a switch swallowing the next
/// argument, in every verb. This reads the verbs' own source.
/// </remarks>
public sealed class SwitchesAreDeclaredTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every flag a verb reads with <c>Has</c> alone is a switch, and the parser knows it.
    /// </summary>
    /// <remarks>
    /// The parser cannot tell a switch from an option by looking at it, so the list is the
    /// only thing that stops a switch swallowing the next argument; this reads the verbs' own
    /// source and fails when a switch is added to a verb and not to the list.
    /// </remarks>
    [Fact]
    public void EverySwitchAVerbReadsIsDeclared()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Einzel.Cli", "Program.cs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var source = string.Concat(Directory.GetFiles(Path.Combine(directory!.FullName, "src", "Einzel.Cli"), "*.cs").Select(File.ReadAllText));
        var has = System.Text.RegularExpressions.Regex.Matches(source, @"\.Has\(""([a-z0-9-]+)""\)").Select(m => m.Groups[1].Value).ToHashSet();
        var valued = System.Text.RegularExpressions.Regex.Matches(source, @"(?:\.Values?\(|Try\w*Flag\(options, )""([a-z0-9-]+)""").Select(m => m.Groups[1].Value)
            .ToHashSet();

        // Read as a value under a name chosen at run time, or refused by name for having no unit.
        string[] notSwitches = ["at-mm", "line-mm", "at", "line", "to", "offset", "box", "height-mm"];

        var switches = has.Except(valued).Except(notSwitches).Order().ToList();
        output.WriteLine("switches: " + string.Join(", ", switches));

        Assert.True(switches.Count >= 8, "the control: the scan found the switches");
        Assert.Empty(switches.Except(CommandLine.Switches));
        Assert.Empty(CommandLine.Switches.Except(switches));
    }


    /// <summary>
    /// Every verb refuses a flag that takes a value and was given none, and an argument it does
    /// not take, naming it - before it does anything.
    /// </summary>
    /// <param name="args">The command line, with {model} for a model file.</param>
    /// <param name="named">What the refusal names.</param>
    [Theory]
    [InlineData("render section {model} --out --json", "--out")]
    [InlineData("render section {model} --no-trajectory out.svg --dry-run", "'out.svg'")]
    [InlineData("validate {model} other.json", "'other.json'")]
    [InlineData("run {model} --json --vtu extra", "'extra'")]
    [InlineData("estimate {model} --threshold", "--threshold")]
    [InlineData("schema extra", "'extra'")]
    [InlineData("render surface {model} --dry-run --no-scale out.png", "'out.png'")]
    [InlineData("probe {model} --at-mm 0,0,0 --at-us --json", "--at-us")]
    [InlineData("render surface {model} --dry-run --no-scales", "unknown flag --no-scales for render surface")]
    [InlineData("validate {model} --dry-run --json", "unknown flag --dry-run for validate")]
    public void EveryVerbRefusesAMalformedCommandLine(string args, string named)
    {
        var root = Path.Combine(Path.GetTempPath(), "einzel-switches", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var model = Path.Combine(root, "slit.json");
            Assert.Equal(0, Program.Main(["new", model, "--from-example", "slit-transmission"]));

            var words = args.Replace("{model}", model, StringComparison.Ordinal).Split(' ');
            var (code, stdout, stderr) = Cli(words);
            output.WriteLine(stderr);

            Assert.Equal(1, code);
            Assert.Contains(named, stderr, StringComparison.Ordinal);
            if (words.Contains("--json"))
            {
                // AGT-3: the refusal as JSON, naming what it refuses.
                using var document = System.Text.Json.JsonDocument.Parse(stdout);
                var error = document.RootElement.GetProperty("errors")[0];
                Assert.Equal("SCHEMA_INVALID", error.GetProperty("code").GetString());
                Assert.StartsWith("/args/", error.GetProperty("path").GetString(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(string.Empty, stdout);
            }

            // The same command lines, well formed, are accepted: a switch before the model.
            Assert.Equal(0, Cli(["validate", "--json", model]).ExitCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
}
