using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A model may declare several ion populations, and `einzel run` reports what became of each.
/// </summary>
/// <remarks>
/// <para>
/// The wiring, not the physics - the coupling is measured in <c>MixtureDiffusionTests</c>. What
/// these check is that a document can say "these populations are present", that saying it twice
/// in different ways is refused rather than merged, and that the result carries every population
/// rather than one wearing the name of the run.
/// </para>
/// </remarks>
public sealed class MixtureSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-mixture", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A short drift tube carrying two populations of different mobility.</summary>
    private string Model(
        string species,
        string spaceCharge = "meanField",
        string ion = "",
        string mobility = "",
        string cloudPopulation = "")
    {
        Directory.CreateDirectory(Path.Combine(_root, "models"));
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"mix-{Guid.NewGuid():N}.json");

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.13",
              "name": "two-populations",
              {{ion}}{{species}}
              "source": {
                "position": { "value": [4, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 0, "unit": "V" },
                "cloud": { "ions": 1, {{cloudPopulation}}
                           "transverseSpread": { "value": 0.4, "unit": "mm" },
                           "longitudinalSpread": { "value": 0.4, "unit": "mm" } }
              },
              "detector": {
                "planePoint": { "value": [30, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "diffusion",
                "spaceCharge": "{{spaceCharge}}",
                "maximumFlightTime": { "value": 60, "unit": "us" },
                {{mobility}}
                "densityGrid": {
                  "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                  "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                  "intervalsX": 64, "intervalsY": 32
                },
                "gas": { "model": "hardSphere", "pressure": { "value": 2, "unit": "mbar" },
                         "mass": { "value": 28.0134, "unit": "Da" },
                         "crossSection": { "value": 250, "unit": "angstrom^2" } }
              },
              "fields": [
                { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } }
              ]
            }
            """);

        return path;
    }

    /// <summary>Two populations, the second half as mobile.</summary>
    private const string TwoSpecies = """
              "species": [
                { "name": "fast", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
                  "mobility": { "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } },
                  "population": 2000000 },
                { "name": "slow", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
                  "mobility": { "zeroField": { "value": 0.025, "unit": "m^2/(V s)" } },
                  "population": 2000000 }
              ],
        """;

    /// <summary>Every population is reported, with the mobility it actually ran with.</summary>
    [Fact]
    public void EveryPopulationIsReported()
    {
        var (exit, stdout, stderr) = Run("run", Model(TwoSpecies), "--json");
        Assert.True(exit == 0, stdout + stderr);

        using var document = JsonDocument.Parse(stdout);
        var mixture = document.RootElement.GetProperty("mixture");
        var species = mixture.GetProperty("species");

        output.WriteLine($"{mixture.GetProperty("steps").GetInt32()} shared steps of "
            + $"{mixture.GetProperty("stepUs").GetDouble() * 1e3:F3} ns, set by "
            + $"'{mixture.GetProperty("stepSetBy").GetString()}'");

        var names = new List<string>();

        foreach (var member in species.EnumerateArray())
        {
            var name = member.GetProperty("name").GetString()!;
            names.Add(name);

            output.WriteLine($"  {name}: K {member.GetProperty("mobilitySi").GetDouble():G4}, "
                + $"{member.GetProperty("remaining").GetDouble():G6} held, "
                + $"{member.GetProperty("transmission").GetDouble():P1} through, "
                + $"x {member.GetProperty("centroidMm").GetDouble():F3} mm");
        }

        Assert.Equal(["fast", "slow"], names);

        // The step is the shortest any population needed, and the quicker one needs it.
        Assert.Equal("fast", mixture.GetProperty("stepSetBy").GetString());

        // And the mobilities are the declared ones rather than one shared number, which is the
        // failure that would make a mixture separate nothing while looking like an answer.
        var mobilities = species.EnumerateArray()
            .Select(m => m.GetProperty("mobilitySi").GetDouble()).ToArray();

        Assert.Equal(0.05, mobilities[0], 1e-12);
        Assert.Equal(0.025, mobilities[1], 1e-12);

        // The faster population is further along, which is what a mobility separation is.
        var positions = species.EnumerateArray()
            .Select(m => m.GetProperty("centroidMm").GetDouble()).ToArray();

        output.WriteLine($"separation {positions[0] - positions[1]:F3} mm");

        Assert.True(positions[0] > positions[1],
            $"the faster population ended at {positions[0]:F3} mm and the slower at {positions[1]:F3}");
    }

    /// <summary>
    /// A mixture run has no single flight time, and says so rather than printing one.
    /// </summary>
    /// <remarks>
    /// The defect this guards is old and has recurred: the human printer decided whether to
    /// print a flight time by listing the modes that have none, so each new mode walked back
    /// into <c>flight time NaN +/- NaN</c> until it was caught. It asks the run now.
    /// </remarks>
    [Fact]
    public void AMixtureRunPrintsNoFlightTime()
    {
        var (exit, stdout, stderr) = Run("run", Model(TwoSpecies));
        Assert.True(exit == 0, stdout + stderr);

        output.WriteLine(stdout.Trim());

        Assert.DoesNotContain("NaN", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("flight time", stdout, StringComparison.Ordinal);
        Assert.Contains("mixture", stdout, StringComparison.Ordinal);
        Assert.Contains("space charge", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mixture writes one volume per population, and a result document like every other run.
    /// </summary>
    /// <remarks>
    /// Both were missing when the mixture path was first written. `--vtu` asked for a file and
    /// got silence, which this project has already recorded as the worst of the three options
    /// for exactly this flag on exactly this mode; and no result document meant a mixture run
    /// was regenerable but not verifiable, which is half of what PRJ-3 is for. One volume per
    /// species rather than one called "the density", because a mixture has no single density
    /// and a single file would be a picture of whichever population happened to be first.
    /// </remarks>
    [Fact]
    public void AMixtureWritesAVolumePerPopulationAndAResultDocument()
    {
        var model = Model(TwoSpecies);
        var (exit, stdout, stderr) = Run("run", model, "--vtu", "--json");
        Assert.True(exit == 0, stdout + stderr);

        using var document = JsonDocument.Parse(stdout);

        var artifacts = document.RootElement.GetProperty("artifacts")
            .EnumerateArray().Select(a => a.GetString()!).ToArray();

        foreach (var artifact in artifacts)
        {
            output.WriteLine(artifact);
        }

        Assert.Contains(artifacts, a => a.EndsWith(".fast.density.vti", StringComparison.Ordinal));
        Assert.Contains(artifacts, a => a.EndsWith(".slow.density.vti", StringComparison.Ordinal));
        Assert.Contains(artifacts, a => a.EndsWith(".result.json", StringComparison.Ordinal));
        Assert.Contains(artifacts, a => a.EndsWith(".manifest.json", StringComparison.Ordinal));

        // Every one of them exists, since an artifact list naming a file nobody wrote is worse
        // than one that is short.
        foreach (var artifact in artifacts)
        {
            Assert.True(File.Exists(Path.Combine(_root, artifact)), $"{artifact} was named and not written");
        }

        // And the warnings travel with the volume (GRD-2), including which population it is.
        var volume = File.ReadAllText(
            Path.Combine(_root, artifacts.First(a => a.EndsWith(".fast.density.vti", StringComparison.Ordinal))));

        Assert.Contains("species: fast", volume, StringComparison.Ordinal);
        Assert.Contains("space charge:", volume, StringComparison.Ordinal);
    }

    /// <summary>
    /// Saying it twice is refused, whichever way round, rather than one silently winning.
    /// </summary>
    [Theory]
    [InlineData("ion", "/species", "either one ion or several species")]
    [InlineData("mobility", "/transport/mobility", "a mobility per species")]
    [InlineData("cloudPopulation", "/source/cloud/population", "declared per species")]
    [InlineData("one", "/species", "two or more populations")]
    [InlineData("trajectory", "/species", "diffusive mode")]
    public void SayingItTwiceIsRefused(string how, string path, string expected)
    {
        var model = how switch
        {
            "ion" => Model(TwoSpecies,
                ion: "\"ion\": { \"massToCharge\": { \"value\": 622, \"unit\": \"Da\" }, \"chargeNumber\": 1 },"),
            "mobility" => Model(TwoSpecies,
                mobility: "\"mobility\": { \"zeroField\": { \"value\": 0.05, \"unit\": \"m^2/(V s)\" } },"),
            "cloudPopulation" => Model(TwoSpecies, cloudPopulation: "\"population\": 1000,"),
            "one" => Model("""
                      "species": [
                        { "name": "only", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
                          "mobility": { "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } },
                          "population": 1000 }
                      ],
                """),
            _ => Trajectory(),
        };

        var (exit, _, stderr) = Run("validate", model);

        output.WriteLine(stderr.Trim());

        Assert.NotEqual(0, exit);
        Assert.Contains(path, stderr, StringComparison.Ordinal);
        Assert.Contains(expected, stderr, StringComparison.Ordinal);
    }

    /// <summary>The same two populations, on a trajectory model.</summary>
    /// <remarks>
    /// Written and then edited, with the edit asserted. Three tests in this repository once
    /// edited a scaffolded model by string replacement, matched nothing after the file was
    /// reformatted, and reported the feature they were checking as broken - so an edit that is
    /// not checked is an edit that can silently stop testing anything.
    /// </remarks>
    private string Trajectory()
    {
        var path = Model(TwoSpecies, spaceCharge: "none");
        var text = File.ReadAllText(path);
        var edited = text.Replace(
            "\"mode\": \"diffusion\"", "\"mode\": \"trajectory\"", StringComparison.Ordinal);

        Assert.NotEqual(text, edited);

        File.WriteAllText(path, edited);

        return path;
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
