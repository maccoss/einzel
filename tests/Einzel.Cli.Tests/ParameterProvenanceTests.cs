using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A parameter can say where its number came from, and a run says which of its numbers are
/// not the model author's own choice.
/// </summary>
/// <remarks>
/// <para>
/// The Astral reconstruction is the case that forced it. Three of its numbers are solved for,
/// thirteen are read off a published drawing, and the rest are published outright - and which
/// results would move if a better source turned up depends entirely on which is which. That
/// question was asked repeatedly and answered each time by re-reading prose, because the
/// parameter surface could carry a unit, bounds and a description and not a provenance.
/// </para>
/// <para>
/// It is also the one thing a study cannot infer. A sweep can perturb a parameter and an
/// optimiser can fit one; neither can say whether the nominal was measured or invented.
/// </para>
/// </remarks>
public sealed class ParameterProvenanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-provenance", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A scaffolded model with one parameter's provenance rewritten.</summary>
    private string Model(string provenance, string? source)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Run("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", "reflectron.json");

        // Edited as JSON rather than as text. A first version anchored on the string
        // "capPotential": { and matched twice - the parameter, and the half-space element's
        // own cap - so it declared a provenance on a QuantityValue and every test failed on
        // an unrecognised property in a place nobody had looked at.
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        var parameter = document["parameters"]!["capPotential"]!.AsObject();

        parameter["provenance"] = provenance;
        if (source is not null)
        {
            parameter["source"] = source;
        }

        File.WriteAllText(path, document.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        return path;
    }

    /// <summary>The provenances that make a claim must say what backs it.</summary>
    /// <remarks>
    /// The same argument section 9 makes for units. A claim of authority with nothing behind
    /// it is worse than no claim, because the reader cannot recompute it and has no way to
    /// tell it apart from one that is backed.
    /// </remarks>
    [Theory]
    [InlineData("published")]
    [InlineData("drawn")]
    [InlineData("fitted")]
    public void AClaimOfAuthorityMustSayWhatBacksIt(string provenance)
    {
        var (exit, _, stderr) = Run("validate", Model(provenance, source: null));
        output.WriteLine(stderr.Trim());

        Assert.NotEqual(0, exit);
        Assert.Contains("must say what it came from", stderr, StringComparison.Ordinal);
        Assert.Contains("/parameters/capPotential/source", stderr, StringComparison.Ordinal);
    }

    /// <summary>And the ones that make no claim may not supply one.</summary>
    /// <remarks>
    /// A chosen or guessed value has no source by definition, so a document declaring one is
    /// saying two things at once - the same reason a solve may not declare both drive and
    /// drives, and a model may not declare both sequence and stages.
    /// </remarks>
    [Theory]
    [InlineData("chosen")]
    [InlineData("guess")]
    public void AProvenanceWithNoSourceMayNotSupplyOne(string provenance)
    {
        var (exit, _, stderr) = Run("validate", Model(provenance, "Author et al. 2024"));
        output.WriteLine(stderr.Trim());

        Assert.NotEqual(0, exit);
        Assert.Contains("has no source by definition", stderr, StringComparison.Ordinal);
    }

    /// <summary>A backed claim validates, and reaches the outline with its source.</summary>
    [Fact]
    public void ABackedClaimReachesTheOutline()
    {
        var path = Model("published", "Author et al., J. Am. Soc. Mass Spectrom. 2024, table 1");

        var (exit, stdout, _) = Run("outline", path, "--json");
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(stdout);
        var cap = document.RootElement.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "capPotential");

        output.WriteLine(cap.ToString());

        Assert.Equal("Published", cap.GetProperty("provenance").GetString());
        Assert.Contains("table 1", cap.GetProperty("source").GetString()!, StringComparison.Ordinal);

        // Everything else defaults rather than being absent: a parameter nobody has
        // classified is a value its author chose, which is a real answer and not a gap.
        var depth = document.RootElement.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "turningDepth");

        Assert.Equal("Chosen", depth.GetProperty("provenance").GetString());
    }

    /// <summary>A run says which of its numbers are guesses.</summary>
    [Fact]
    public void ARunSaysWhichNumbersAreGuesses()
    {
        var path = Model("guess", source: null);

        var (exit, stdout, _) = Run("run", path, "--json");
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(stdout);
        var warning = document.RootElement.GetProperty("flightTime").GetProperty("warnings")
            .EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "parameters.guessed");

        var message = warning.GetProperty("message").GetString()!;
        output.WriteLine(message);

        Assert.Contains("capPotential", message, StringComparison.Ordinal);

        // Provenance, not a violation: a guessed dimension does not make a result invalid,
        // it makes it a result about a geometry somebody assumed.
        Assert.Equal("Provenance", warning.GetProperty("severity").GetString());
    }

    /// <summary>And which were fitted, which is a different qualification.</summary>
    /// <remarks>
    /// A fitted parameter makes the model agree with whatever it was fitted against by
    /// construction, so that agreement is not evidence. That is worth saying separately from
    /// a guess, which is not agreeing with anything.
    /// </remarks>
    [Fact]
    public void ARunSaysWhichNumbersWereFitted()
    {
        var path = Model("fitted", "fitted against the published period-slope curve");

        var (exit, stdout, _) = Run("run", path, "--json");
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(stdout);
        var message = document.RootElement.GetProperty("flightTime").GetProperty("warnings")
            .EnumerateArray()
            .Single(w => w.GetProperty("code").GetString() == "parameters.fitted")
            .GetProperty("message").GetString()!;

        output.WriteLine(message);

        Assert.Contains("capPotential", message, StringComparison.Ordinal);
        Assert.Contains("not evidence", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model with nothing to declare says nothing, which is the control.
    /// </summary>
    /// <remarks>
    /// Published and drawn are deliberately silent too: a run resting on cited values is the
    /// ordinary case for a reconstruction, and a line on every model is noise rather than
    /// information. What is reported is the two that qualify a result.
    /// </remarks>
    [Fact]
    public void AModelWithNothingToDeclareIsSilent()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Path.Combine(_root, "models", "reflectron.json");

        var (exit, stdout, _) = Run("run", path, "--json");
        Assert.Equal(0, exit);

        using var document = JsonDocument.Parse(stdout);
        var codes = document.RootElement.GetProperty("flightTime").GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToList();

        output.WriteLine(string.Join(", ", codes));

        Assert.DoesNotContain("parameters.guessed", codes);
        Assert.DoesNotContain("parameters.fitted", codes);
    }

    /// <summary>An unrecognised provenance is refused, not read as the default.</summary>
    [Fact]
    public void AnUnrecognisedProvenanceIsRefused()
    {
        var (exit, _, stderr) = Run("validate", Model("hearsay", source: null));
        output.WriteLine(stderr.Trim());

        Assert.NotEqual(0, exit);
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
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
