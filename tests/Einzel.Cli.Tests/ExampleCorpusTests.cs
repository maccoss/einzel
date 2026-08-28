using System.Text.Json;
using Einzel.Commands;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The EX-1 reference corpus, run as a corpus.
/// </summary>
/// <remarks>
/// <para>
/// EX-2: "The corpus runs in CI; a failing example blocks release." This is that
/// gate. Every example is materialised into a real project and driven through
/// <c>einzel test</c> - the command surface, not the command objects - because the
/// corpus exists for agents and an agent reaches it through the CLI.
/// </para>
/// <para>
/// What it is really protecting is the <em>expectations</em>. Each is a closed
/// form or a published value, so a failure here means the engine has moved away
/// from arithmetic rather than away from its own past output. That is a different
/// and much stronger signal than a golden-file comparison, and it is why the
/// corpus is worth running on every change rather than at release.
/// </para>
/// </remarks>
public sealed class ExampleCorpusTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-corpus", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
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

    /// <summary>Writes every example and its test into a fresh project.</summary>
    private string Materialise()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        // init scaffolds one of the examples already; remove it so nothing is run
        // twice and the count below means what it says.
        foreach (var scaffolded in new[]
        {
            Path.Combine(_root, "models", "reflectron.json"),
            Path.Combine(_root, "tests", "reflectron.json"),
        })
        {
            File.Delete(scaffolded);
        }

        foreach (var name in ExampleModels.Names)
        {
            File.WriteAllText(Path.Combine(_root, "models", name + ".json"), ExampleModels.Read(name));
            File.WriteAllText(Path.Combine(_root, "tests", name + ".json"), ExampleModels.ReadTest(name));
        }

        return _root;
    }

    [Fact]
    public void EveryExampleShipsATest()
    {
        // An example with no assertion is a file that parses, which is a weaker
        // thing than a reference model and reads like a stronger one. EX-1 asks for
        // "expected results, and assertion tolerances" in the same breath as the
        // prose description, so a corpus entry without one is incomplete rather
        // than merely untested.
        Assert.NotEmpty(ExampleModels.Names);

        foreach (var name in ExampleModels.Names)
        {
            Assert.True(ExampleModels.HasTest(name), $"example '{name}' ships no test");
        }

        output.WriteLine($"{ExampleModels.Names.Count} examples, each with a test");
    }

    [Fact]
    public void EveryExampleDescribesItself()
    {
        // The catalogue reads descriptions out of the documents rather than keeping
        // a table beside them, so an example with none is invisible to an agent
        // browsing `einzel examples` - it appears as a bare name with no way to tell
        // what it is for without opening it.
        foreach (var name in ExampleModels.Names)
        {
            var description = Io.ModelJson.Parse(ExampleModels.Read(name)).Description;

            Assert.False(
                string.IsNullOrWhiteSpace(description),
                $"example '{name}' has no description");

            // Long enough to say what the model is and where its expected number
            // comes from. The threshold is low on purpose: it is a floor against an
            // empty string, not an attempt to legislate prose.
            Assert.True(
                description!.Length > 80,
                $"example '{name}' describes itself in {description.Length} characters");
        }
    }

    [Fact]
    public void EveryExampleValidates()
    {
        var root = Materialise();

        foreach (var name in ExampleModels.Names)
        {
            var path = Path.Combine(root, "models", name + ".json");
            var (exitCode, stdout, stderr) = Run("validate", path, "--json");

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("\"errors\": [{", stdout + stderr, StringComparison.Ordinal);
        }

        output.WriteLine($"{ExampleModels.Names.Count} examples validate");
    }

    [Fact]
    public void TheWholeCorpusPasses()
    {
        // EX-2's gate. Driven through Program.Main rather than TestCommand, because
        // the things most likely to break an agent loop - exit codes, which stream
        // output lands on, whether --json parses - live in the surface.
        var root = Materialise();

        var (exitCode, stdout, stderr) = Run("test", root, "--json");

        using var document = JsonDocument.Parse(stdout);
        var result = document.RootElement;

        var passed = result.GetProperty("passed").GetInt32();
        var total = result.GetProperty("tests").GetArrayLength();

        foreach (var test in result.GetProperty("tests").EnumerateArray())
        {
            var name = test.GetProperty("name").GetString();

            foreach (var assertion in test.GetProperty("assertions").EnumerateArray())
            {
                var figure = assertion.GetProperty("figureOfMerit").GetString();
                var expected = assertion.GetProperty("expected").GetDouble();

                var observed = assertion.TryGetProperty("observed", out var got)
                    && got.ValueKind == JsonValueKind.Number
                        ? got.GetDouble().ToString("G9")
                        : "nothing arrived";

                output.WriteLine(
                    $"{(assertion.GetProperty("passed").GetBoolean() ? "ok  " : "FAIL")} "
                    + $"{name,-30} {figure,-16} {observed} against {expected:G9} "
                    + $"{assertion.GetProperty("unit").GetString()}");
            }

            if (test.TryGetProperty("failure", out var failure)
                && failure.ValueKind == JsonValueKind.String)
            {
                output.WriteLine($"FAIL {name,-28} {failure.GetString()}");
            }
        }

        Assert.Equal(ExampleModels.Names.Count, total);
        Assert.Equal(total, passed);
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Unhandled", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrecognisedPropertyIsRefusedRatherThanIgnored()
    {
        // The defect the corpus found on its first day. A cloud declaring
        // 'transverseWidth' instead of 'transverseSpread' parsed cleanly, produced a
        // packet with no spatial extent, and gave an emittance of 7.1e-8 um where
        // the closed form says 1.798 - a plausible number from a model that reads as
        // though it says something else.
        //
        // This is the same rule as requiring a unit on every quantity, applied to
        // the key rather than the value, and section 22 names its failure mode as
        // the defining risk of the whole thesis.
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "typo.json");

        File.WriteAllText(
            path,
            ExampleModels.Read("free-flight").Replace(
                "\"massToCharge\"", "\"massToChage\"", StringComparison.Ordinal));

        var (exitCode, stdout, stderr) = Run("validate", path, "--json");

        Assert.NotEqual(0, exitCode);

        var text = stdout + stderr;

        // AGT-3: the offending path, by name, and what to do about it.
        Assert.Contains("massToChage", text, StringComparison.Ordinal);
        Assert.Contains("einzel schema", text, StringComparison.Ordinal);
    }
}
