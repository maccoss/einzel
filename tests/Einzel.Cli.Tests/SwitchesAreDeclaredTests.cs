using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Every verb refuses a malformed command line, naming what is wrong with it.
/// </summary>
/// <remarks>
/// Which flags are switches is each verb's own registry entry, held to its handler by
/// <see cref="VerbFlagsTests"/>; the parser asks the verb named.
/// </remarks>
public sealed class SwitchesAreDeclaredTests(ITestOutputHelper output)
{
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
