using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// That the method a document declares is the method that runs, and that the
/// approximate one agrees with the reference it was validated against.
/// </summary>
/// <remarks>
/// <para>
/// Wiring a switch and asserting the document parses is not the same as asserting the
/// switch does anything. The rule this codebase reached when the direct sum was wired
/// applies again: <b>a switch that runs different code and produces the same number is
/// not a feature</b>. So the control here is <c>"none"</c>, which must differ, and the
/// comparison is against <c>"direct"</c>, which must not.
/// </para>
/// <para>
/// The packet is deliberately dense and the drift long, so the effect is fourteen-fold
/// rather than marginal. A test where the two methods agree to a per cent on an effect
/// of a per cent is a test that passes when the interaction is never invoked.
/// </para>
/// </remarks>
public sealed class SpaceChargeMethodTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-spacecharge", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A packet expands under its own charge, and the grid says what the sum says.
    /// </summary>
    [Fact]
    public void TheDeclaredMethodIsTheMethodThatRuns()
    {
        var free = Widen("none");
        var reference = Widen("direct");
        var grid = Widen("pic");

        // The control. Nothing else in this model can move an ion off its launch
        // radius: the field is field-free and the cloud has no temperature, so
        // without a mutual force the packet arrives the width it left.
        //
        // The band is the sampling error on an RMS from N draws, 1/sqrt(2N), which is
        // 7% here - not a tolerance chosen to fit. Two decimal places passed at 200
        // draws and failed at 96 for that reason alone.
        Assert.Equal(0.5, free, 0.05);

        // Both methods find the same packet, fourteen times wider.
        Assert.True(reference > 10.0 * free, $"the reference barely moved it: {reference:F3} mm");

        // Measured at 2.5% on this configuration, and it converges: tightening the
        // refresh tolerance from 0.30 to 0.15 to 0.05 to 0.02 gives 18.0%, 8.9%, 2.5%,
        // 1.8%. Always wide rather than scattered, because a field held across a
        // refresh is the field of a denser packet than the one being pushed.
        Assert.Equal(reference, grid, 0.05 * reference);

        // And they are not the same computation. This assertion is what gives the one
        // above teeth: agreement alone is exactly what a run that ignored the declared
        // method and used the reference for both would show, and it would show it
        // perfectly. The grid smooths at the cell where the sum softens at short
        // range, so approximating well and approximating identically are different
        // claims - only the first is being made.
        Assert.NotEqual(reference, grid);
    }

    /// <summary>
    /// A cloud below the crossing is told the reference method is faster here.
    /// </summary>
    /// <remarks>
    /// GRD-8's number is only half of what a reader needs. Particle-in-cell is linear
    /// where the sum is quadratic, so quoting the asymptotics alone would recommend it
    /// everywhere - including the majority of clouds, where it loses to the method it
    /// approximates.
    /// </remarks>
    [Fact]
    public void TheEstimateNamesTheCrossingRatherThanTheAsymptotics()
    {
        var project = Project();

        File.WriteAllText(Path.Combine(project, "models", "small.json"), Model("pic", ions: 100));

        var (exit, stdout, _) = Run("estimate", Path.Combine(project, "models", "small.json"));

        // GRD-8 refuses above the threshold rather than warning past it, and a
        // hundred-trajectory grid run is above it. That the gate fires is the point:
        // the basis has to be readable on the path where someone is being stopped.
        Assert.Equal(3, exit);

        // Both scalings, because quoting only the linear one is what would recommend
        // the approximation everywhere.
        Assert.Contains(
            "LINEAR IN THE TRAJECTORY COUNT AND CUBIC IN THE NODE COUNT",
            stdout,
            StringComparison.Ordinal);

        Assert.Contains("cross near", stdout, StringComparison.Ordinal);

        // And the actionable half: at a hundred trajectories the method being asked
        // for is the slower one, and the faster one is also the reference.
        Assert.Contains("pairwise sum is about", stdout, StringComparison.Ordinal);
        Assert.Contains("reference method", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The run's provenance names the grid, not the sum's softening length.
    /// </summary>
    /// <remarks>
    /// GRD-1 wants the approximation stated, and the two approximate different things:
    /// the sum softens at short range, the grid smooths at the cell and stands the
    /// packet in an earthed box. One line saying "space charge was modelled" is not
    /// enough to read either number by.
    /// </remarks>
    [Fact]
    public void TheProvenanceNamesTheGridItSolvedOn()
    {
        var project = Project();
        var path = Path.Combine(project, "models", "prov.json");

        File.WriteAllText(path, Model("pic", ions: 40, nodes: 16, padding: 3.0));

        // CLI-2: results on stdout, diagnostics on stderr - and provenance is a
        // diagnostic, so this is where a reader's tooling finds it.
        var (exit, _, stderr) = Run("run", path);

        Assert.Equal(0, exit);
        Assert.Contains("16 nodes", stderr, StringComparison.Ordinal);
        Assert.Contains("3.0 RMS radii", stderr, StringComparison.Ordinal);
        Assert.Contains("earthed box", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("summation over every pair", stderr, StringComparison.Ordinal);
    }

    /// <summary>Flies the packet and returns the RMS radius it arrives with, in mm.</summary>
    private double Widen(string method)
    {
        var project = Project();
        var path = Path.Combine(project, "models", $"{method}.json");

        // The widening is set by "population", not by how many trajectories sample it,
        // so the trajectory count is free to be small. It is the grid method that is
        // slow at this size - which is the crossing this same class asserts about.
        File.WriteAllText(path, Model(method, ions: 96));

        var (exit, stdout, stderr) = Run("run", path, "--json");

        Assert.True(exit == 0, stderr);

        using var document = JsonDocument.Parse(stdout);

        return document.RootElement
            .GetProperty("ensemble")
            .GetProperty("packetRadiusMm")
            .GetDouble();
    }

    private static string Model(
        string method, int ions, int? nodes = null, double? padding = null)
    {
        var grid = method != "pic"
            ? string.Empty
            : $$"""
                ,
                "spaceChargeGrid": {
                  "nodes": {{nodes ?? 32}},
                  "padding": {{(padding ?? 4.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}}
                }
                """;

        return $$"""
        {
          "schemaVersion": "0.5",
          "name": "space-charge-{{method}}",
          "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0.0, 0.0, 0.0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 4000.0, "unit": "V" },
            "cloud": {
              "ions": {{ions}},
              "population": 4000000,
              "seed": 7,
              "transverseSpread": { "value": 0.5, "unit": "mm" },
              "longitudinalSpread": { "value": 0.5, "unit": "mm" }
            }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [500.0, 0.0, 0.0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "relativeTolerance": 1e-9,
            "maximumFlightTime": { "value": 100.0, "unit": "us" },
            "spaceCharge": "{{method}}"{{grid}}
          }
        }
        """;
    }

    private string Project()
    {
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));

        Assert.Equal(0, Run("init", root).ExitCode);

        return root;
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
}
