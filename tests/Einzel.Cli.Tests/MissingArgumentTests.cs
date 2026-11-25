using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A command line a verb cannot run for want of an argument or a flag is refused as JSON on stdout
/// under <c>--json</c>, located among the arguments, and still answered with the usage for a person.
/// </summary>
/// <remarks>
/// Every such return printed the verb's usage to stderr and nothing on stdout, so <c>einzel run
/// --json</c> left an agent an empty stream to parse - the one place CLI-1's contract had no
/// document at all.
/// </remarks>
public sealed class MissingArgumentTests(ITestOutputHelper output)
{
    /// <summary>Every verb in the registry that needs an argument, given none.</summary>
    public static TheoryData<string> VerbsThatNeedAnArgument()
    {
        var data = new TheoryData<string>();

        foreach (var (key, flags) in VerbFlags.ByVerb.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            if (flags.Required > key.Count(c => c == ' '))
            {
                data.Add(key);
            }
        }

        return data;
    }

    /// <summary>The registry's verbs, given too few arguments, are refused at /args under --json.</summary>
    /// <param name="verb">The verb, with its sub-verb.</param>
    [Theory]
    [MemberData(nameof(VerbsThatNeedAnArgument))]
    public void AVerbGivenTooFewArgumentsIsRefusedAsJson(string verb)
    {
        AssertRefused([.. verb.Split(' '), "--json"], "/args");
    }

    /// <summary>
    /// What the registry cannot see: <c>render</c> with nothing, an unknown command, <c>new</c>
    /// with no source, <c>probe</c> with neither a point nor a line, and the implicit section.
    /// </summary>
    /// <param name="line">The command line, space-separated.</param>
    /// <param name="path">Where the refusal is located.</param>
    [Theory]
    [InlineData("render", "/args")]
    [InlineData("no-such-verb", "/args/0")]
    [InlineData("new scratch-model.json", "/args")]
    [InlineData("probe scratch-model.json", "/args")]
    [InlineData("render --dry-run", "/args")]
    public void ARequirementBetweenFlagsOrAVerbWithNoTableIsRefusedAsJson(string line, string path)
    {
        AssertRefused([.. line.Split(' '), "--json"], path);
    }

    /// <summary>Without --json a person still gets the verb's usage, and stdout stays empty.</summary>
    [Fact]
    public void WithoutJsonThePersonStillGetsTheUsage()
    {
        var (code, stdout, stderr) = Cli("run");

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("usage: einzel run", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bare <c>estimate --snapshots</c> with no model is refused by the flag's name: it was
    /// skipped, so the command line fell through to the usage and said nothing about it.
    /// </summary>
    /// <param name="json">Whether --json is given.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABareSnapshotsWithNoModelIsRefusedByName(bool json)
    {
        string[] args = json ? ["estimate", "--snapshots", "--json"] : ["estimate", "--snapshots"];
        var (code, stdout, stderr) = Cli(args);

        output.WriteLine(stderr);

        Assert.Equal(1, code);
        Assert.Contains("at /args/--snapshots:", stderr, StringComparison.Ordinal);

        if (json)
        {
            using var document = JsonDocument.Parse(stdout);
            Assert.Equal("/args/--snapshots", document.RootElement.GetProperty("errors")[0].GetProperty("path").GetString());
        }
    }

    private void AssertRefused(string[] args, string path)
    {
        var (code, stdout, stderr) = Cli(args);

        output.WriteLine($"einzel {string.Join(' ', args)} -> exit {code}\n{stdout}");

        Assert.Equal(1, code);

        using var document = JsonDocument.Parse(stdout);
        Assert.False(document.RootElement.GetProperty("ran").GetBoolean());
        Assert.Equal(path, document.RootElement.GetProperty("errors")[0].GetProperty("path").GetString());
        Assert.False(string.IsNullOrWhiteSpace(stderr), "the reason is on stderr too");
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
