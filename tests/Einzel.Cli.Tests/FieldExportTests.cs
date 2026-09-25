using System.Globalization;
using System.Text.Json;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel export</c> writes what was solved - every basis channel, in both dimensions -
/// and carries the solve's warnings into the file and onto the outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two-dimensional path built one mask from each electrode's DC potential.</b> For
/// the shipped RF quadrupole, whose every rod holds zero DC and all of its potential as
/// drive, that exported the field of a grounded box: every node zero, exit 0, a file
/// ParaView opens without complaint. It is the defect <c>einzel solve</c> had and lost
/// (<c>docs/lessons.md</c>, "Reading the DC of an electrode that holds none"). Measured over
/// every template, six driven cross-sections exported zeros that way and four - the funnels
/// and the TIMS front ends - their DC chain without their RF. The three-dimensional path
/// already wrote one file per channel.
/// </para>
/// <para>
/// <b>And neither path carried a warning into the file</b>, which GRD-2 names as one of the
/// places a warning must reach - a volume is the artifact most likely to be opened by
/// somebody who never saw the command that wrote it.
/// </para>
/// </remarks>
public sealed class FieldExportTests : IDisposable
{
    private const string SharedFace = "mesh.node-on-shared-face";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

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

    private string FromTemplate(string template)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", $"{template}.json");
        Assert.Equal(0, Run("new", model, "--from-template", template).ExitCode);
        return model;
    }

    /// <summary>Exports a model and returns the outcome's artifacts and warnings.</summary>
    private static (List<string> Artifacts, List<(string Code, string Message)> Warnings) Export(string model)
    {
        var (exitCode, stdout, stderr) = Run("export", model, "--json");
        Assert.True(exitCode == 0, stderr);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        return (
            [.. root.GetProperty("artifacts").EnumerateArray().Select(a => a.GetString()!)],
            [
                .. root.GetProperty("warnings").EnumerateArray().Select(w => (
                    w.GetProperty("code").GetString()!,
                    w.GetProperty("message").GetString()!)),
            ]);
    }

    /// <summary>The comment block ahead of the data: provenance and warnings.</summary>
    private static string Header(string vti) =>
        vti[..vti.IndexOf("-->", StringComparison.Ordinal)];

    /// <summary>Every value in the file's one data array.</summary>
    private static double[] Values(string vti)
    {
        var start = vti.IndexOf("format=\"ascii\">", StringComparison.Ordinal) + 15;
        var end = vti.IndexOf("</DataArray>", StringComparison.Ordinal);

        return
        [
            .. vti[start..end]
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)),
        ];
    }

    /// <summary>
    /// The RF quadrupole exports its rods rather than a grounded box.
    /// </summary>
    /// <remarks>
    /// Exactly plus and minus one at the extremes, because a basis channel is its spatial
    /// pattern with the leading rod at one volt and the rods are Dirichlet nodes - a sharper
    /// statement than "not zero", and the one that says the file is the pattern and not some
    /// other nonzero thing. The array is named as a basis for the same reason: read as a
    /// potential, a quadrupole driven at 500 V would appear to be driven at 1.
    /// </remarks>
    [Fact]
    public void ADrivenCrossSectionExportsItsDriveRatherThanAGroundedBox()
    {
        var model = FromTemplate("quadrupole-rf");

        var (artifacts, _) = Export(model);

        var path = Assert.Single(artifacts);
        Assert.EndsWith("quadrupole-rf.field.vti", path, StringComparison.Ordinal);

        var vti = File.ReadAllText(path);
        var values = Values(vti);

        Assert.Equal(1.0, values.Max());
        Assert.Equal(-1.0, values.Min());

        Assert.Contains("Name=\"channel0_V_per_V\"", vti, StringComparison.Ordinal);
        Assert.Contains("volts per volt of this channel's weight", Header(vti), StringComparison.Ordinal);
        Assert.Contains("rodXPlus 1 V", Header(vti), StringComparison.Ordinal);
        Assert.Contains("rodYPlus -1 V", Header(vti), StringComparison.Ordinal);
    }

    /// <summary>
    /// A driven cross-section with two spatial patterns writes a file for each, named by
    /// channel, and each says which pattern it is.
    /// </summary>
    /// <remarks>
    /// The funnel is the case: an RF pattern alternating ring to ring and a DC chain rising
    /// along it, which share nothing and so are two solves. Named in the header because two
    /// files called <c>channel0</c> and <c>channel1</c> do not otherwise say which is which.
    /// </remarks>
    [Fact]
    public void TwoPatternsInACrossSectionAreTwoFiles()
    {
        var model = FromTemplate("ion-funnel");

        var (artifacts, _) = Export(model);

        Assert.Equal(2, artifacts.Count);
        Assert.EndsWith("ion-funnel.field.channel0.vti", artifacts[0], StringComparison.Ordinal);
        Assert.EndsWith("ion-funnel.field.channel1.vti", artifacts[1], StringComparison.Ordinal);

        var alternating = Header(File.ReadAllText(artifacts[0]));
        var chain = Header(File.ReadAllText(artifacts[1]));

        Assert.Contains("ring-0 1 V, ring-1 -1 V", alternating, StringComparison.Ordinal);
        Assert.Contains("ring-1 1 V, ring-2 2 V", chain, StringComparison.Ordinal);
        Assert.Contains("axisymmetric", chain, StringComparison.Ordinal);
    }

    /// <summary>
    /// A static model's single file is still its potential, in volts, under the name it
    /// always had - the change is to driven geometry and must not move anything else.
    /// </summary>
    [Fact]
    public void AStaticCrossSectionIsStillAPotentialInVolts()
    {
        var model = FromTemplate("quadrupole");

        var (artifacts, warnings) = Export(model);

        var vti = File.ReadAllText(Assert.Single(artifacts));

        Assert.Contains("Name=\"potential_V\"", vti, StringComparison.Ordinal);
        Assert.Contains("every electrode at its declared potential", Header(vti), StringComparison.Ordinal);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// The planar mirror pair exports with its printed boards on, and says it is half a
    /// reflected solve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The boards are edge profiles, whose volts live in the profile while the scalar
    /// potential is zero. Moving export onto <c>SolveChannels</c> meant going through a
    /// static arm that read the scalar, so the boards would have been rasterized at zero -
    /// the defect <c>einzel solve</c> was carrying on this template. The top row of the grid
    /// <em>is</em> the top board, so it must hold the profile, reaching the cap's 4800 V at
    /// the mirror's back.
    /// </para>
    /// <para>
    /// And the solve covers one mirror and reflects it, so the file holds half the instrument;
    /// without a word in the header it reads as an analyzer cut off at a plane it does not have.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMirrorPairKeepsItsBoardsAndSaysItIsHalf()
    {
        var model = FromTemplate("planar-mirror-pair");

        var (artifacts, _) = Export(model);

        var vti = File.ReadAllText(Assert.Single(artifacts));

        var extent = vti[(vti.IndexOf("WholeExtent=\"", StringComparison.Ordinal) + 13)..];
        var countX = int.Parse(extent.Split(' ')[1], CultureInfo.InvariantCulture) + 1;
        var topRow = Values(vti)[^countX..];

        // Counted rather than the peak alone: the cap is 4800 V and touches the top row, so
        // a peak of 4800 is there with the board missing - which is exactly how einzel solve
        // printed a clean-looking peak for a mirror with no boards. The profile holds nearly a
        // hundred nodes of this row off ground; a missing board holds the cap's and no more.
        var live = topRow.Count(v => Math.Abs(v) > 1.0);

        Assert.True(live > 10, $"{live} nodes of the top board's row are off ground");
        Assert.Equal(4800.0, topRow.Max());
        Assert.Contains("topBoard 1 x its profile", Header(vti), StringComparison.Ordinal);
        Assert.Contains("only the solved half is written", Header(vti), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two rectangles at opposite potentials sharing the face at <paramref name="face"/>
    /// millimeters, in a 16 mm box at a 1 mm cell - so a face at 2 mm is on a line of nodes.
    /// </summary>
    private static string Abutting(string face) => $$"""
    {
      "schemaVersion": "0.3",
      "name": "abutting",
      "description": "Two plates at opposite potentials sharing a face, and an ion flying past them.",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [6.5, -7, 0], "unit": "mm" },
        "direction": { "value": [0, 1, 0] },
        "accelerationPotential": { "value": 1000, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved2d",
          "solve": {
            "minX": { "value": -8, "unit": "mm" },
            "minY": { "value": -8, "unit": "mm" },
            "maxX": { "value": 8, "unit": "mm" },
            "maxY": { "value": 8, "unit": "mm" },
            "cellSize": { "value": 1, "unit": "mm" },
            "tolerance": 1e-8,
            "electrodes": [
              {
                "name": "left", "shape": "rectangle",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": -4, "unit": "mm" },
                "maxX": { "value": {{face}}, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              },
              {
                "name": "right", "shape": "rectangle",
                "minX": { "value": {{face}}, "unit": "mm" },
                "minY": { "value": -4, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "potential": { "value": -100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 7, 0], "unit": "mm" },
        "normal": { "value": [0, -1, 0] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
    }
    """;

    private string WriteAbutting(string face)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var path = Path.Combine(_root, "models", "abutting.json");
        File.WriteAllText(path, Abutting(face));
        return path;
    }

    /// <summary>
    /// A cross-section whose mesh puts nodes on a shared face carries the warning into the
    /// file's header and onto the outcome.
    /// </summary>
    [Fact]
    public void ASharedFaceOnANodeTravelsIntoTheFileAndTheOutcome()
    {
        var model = WriteAbutting("2");

        var (artifacts, warnings) = Export(model);

        var warning = Assert.Single(warnings);
        Assert.Equal(SharedFace, warning.Code);
        Assert.StartsWith("field element 0 (solved2d): ", warning.Message, StringComparison.Ordinal);

        // With its severity, as run --vtu writes a density's header: a reader deciding whether
        // to trust a volume needs to know which lines are notes and which are caveats.
        Assert.Contains($"Qualified: {SharedFace}: ", Header(File.ReadAllText(Assert.Single(artifacts))),
            StringComparison.Ordinal);
    }

    /// <summary>The control: the same plates a quarter cell off the nodes are quiet.</summary>
    /// <remarks>
    /// Without it the test above would pass against an export that stamped the warning on
    /// every file, which is a mark that marks nothing.
    /// </remarks>
    [Fact]
    public void TheSamePlatesAQuarterCellOffTheNodesAreQuiet()
    {
        var model = WriteAbutting("2.25");

        var (artifacts, warnings) = Export(model);

        Assert.Empty(warnings);
        Assert.DoesNotContain(SharedFace, File.ReadAllText(Assert.Single(artifacts)), StringComparison.Ordinal);
    }

    /// <summary>
    /// The terminal form prints the warning on stderr and leaves stdout to the files written
    /// (CLI-2), so a pipe that keeps only the file list does not also lose the caveat.
    /// </summary>
    [Fact]
    public void TheTerminalFormPrintsItOnStandardError()
    {
        var model = WriteAbutting("2");

        var (exitCode, stdout, stderr) = Run("export", model);

        Assert.Equal(0, exitCode);
        Assert.Contains($"[Qualified] {SharedFace}", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(SharedFace, stdout, StringComparison.Ordinal);
        Assert.Contains("wrote ", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two boxes stacked along z meeting at 2 mm, in a 16 mm box at a 1 mm cell - the volume
    /// the shared-face surface tests use.
    /// </summary>
    private const string Stacked = """
    {
      "schemaVersion": "0.3",
      "name": "stacked",
      "description": "Two boxes at opposite potentials sharing a face, and an ion flying past them.",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [6.5, 0, -7], "unit": "mm" },
        "direction": { "value": [0, 0, 1] },
        "accelerationPotential": { "value": 1000, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved3d",
          "solve3d": {
            "minX": { "value": -8, "unit": "mm" },
            "minY": { "value": -8, "unit": "mm" },
            "minZ": { "value": -8, "unit": "mm" },
            "maxX": { "value": 8, "unit": "mm" },
            "maxY": { "value": 8, "unit": "mm" },
            "maxZ": { "value": 8, "unit": "mm" },
            "cellSize": { "value": 1, "unit": "mm" },
            "tolerance": 1e-8,
            "electrodes": [
              {
                "name": "lower", "shape": "box",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": -4, "unit": "mm" },
                "minZ": { "value": -4, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "maxZ": { "value": 2, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              },
              {
                "name": "upper", "shape": "box",
                "minX": { "value": -4, "unit": "mm" },
                "minY": { "value": -4, "unit": "mm" },
                "minZ": { "value": 2, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" },
                "maxY": { "value": 4, "unit": "mm" },
                "maxZ": { "value": 4, "unit": "mm" },
                "potential": { "value": -100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": {
        "planePoint": { "value": [0, 0, 7], "unit": "mm" },
        "normal": { "value": [0, 0, -1] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
    }
    """;

    /// <summary>The volume path carries it the same way.</summary>
    [Fact]
    public void AVolumeCarriesItTheSameWay()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);
        var model = Path.Combine(_root, "models", "stacked.json");
        File.WriteAllText(model, Stacked);

        var (artifacts, warnings) = Export(model);

        var warning = Assert.Single(warnings);
        Assert.Equal(SharedFace, warning.Code);
        Assert.StartsWith("field element 0 (solved3d): 81 mesh nodes", warning.Message, StringComparison.Ordinal);

        Assert.Contains($"Qualified: {SharedFace}: ", Header(File.ReadAllText(Assert.Single(artifacts))),
            StringComparison.Ordinal);
    }
}
