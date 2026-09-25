using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Einzel.Core.Errors;
using Einzel.Library;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A mesh too large to solve is refused as a mistake in the model, by every verb that reads the
/// model, before anything is built.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reported case, exactly</b>: the shipped Astral with its volume solve at a 1 mm cell.
/// Each axis rounds its interval count up to a power of two, so that is 1025 x 65 x 1025 =
/// 68.3 M nodes against a 64 M limit. <c>validate</c> said OK, <c>estimate</c> priced it at
/// 35 minutes, and <c>run</c> spent minutes assembling a conductor mask of that size before the
/// field constructor threw an argument exception - which the CLI can only print as
/// <c>INTERNAL_ERROR</c>, "a defect in einzel, not in your model", on the internal-error exit
/// code. All three were wrong in the same direction: the model was the problem, and it was
/// knowable from the document.
/// </para>
/// <para>
/// <b>What discriminates is the path.</b> The solver still refuses such a grid as a backstop,
/// with the same code and exit code, but it has no document and says <c>/</c>. Only a refusal
/// raised by validation names <c>/fields/2/solve3d/cellSize</c> - so asserting the code alone
/// would pass with the validator's check deleted.
/// </para>
/// </remarks>
public sealed class GridTooLargeSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private const string CellSizePath = "/fields/2/solve3d/cellSize";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-grid-too-large", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ValidateRefusesTheMeshAndLocatesTheCellSize()
    {
        var (exit, stdout, stderr) = Cli("validate", Astral(1.0), "--json");

        output.WriteLine(stdout);

        Assert.Equal((int)ExitCode.ValidationFailure, exit);
        Assert.DoesNotContain(ErrorCodes.InternalError, stderr, StringComparison.Ordinal);

        var error = OnlyGridError(stdout);

        Assert.Equal(CellSizePath, error.GetProperty("path").GetString());
        Assert.Equal(1025.0 * 65 * 1025, error.GetProperty("observed").GetProperty("value").GetDouble());
        Assert.Contains("64,000,000", error.GetProperty("constraint").GetString(), StringComparison.Ordinal);
        Assert.Contains("1025 x 65 x 1025", error.GetProperty("constraint").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>run</c> refuses it as a validation failure, not as an engine defect.
    /// </summary>
    [Fact]
    public void RunRefusesItAsAModelErrorNotAnEngineDefect()
    {
        var model = Astral(1.0);
        var (exit, _, stderr) = Cli("run", model, "--progress", "0");

        output.WriteLine(stderr);

        Assert.Equal((int)ExitCode.ValidationFailure, exit);
        Assert.Contains(ErrorCodes.GridTooLarge, stderr, StringComparison.Ordinal);
        Assert.Contains(CellSizePath, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(ErrorCodes.InternalError, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("defect in einzel", stderr, StringComparison.Ordinal);

        // Refused before anything was written: a manifest would say a run happened.
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(model)!, "results")));
    }

    /// <summary>
    /// <c>estimate</c> says the solve will be refused, rather than pricing it.
    /// </summary>
    /// <remarks>
    /// It validates first, so it meets the same refusal from the same check against the same
    /// limit - one source, and not a second copy of the limit that could drift from the first.
    /// It used to report "field 2 Solved3D 1025x65x1025 2074.94 s 3126.1 MiB, total 35 min" and
    /// exit 3 on the cost gate, which reads as a plan.
    /// </remarks>
    [Fact]
    public void EstimateSaysTheSolveWillBeRefused()
    {
        var (exit, stdout, stderr) = Cli("estimate", Astral(1.0), "--no-calibrate");

        output.WriteLine(stderr);

        Assert.Equal((int)ExitCode.ValidationFailure, exit);
        Assert.Contains(ErrorCodes.GridTooLarge, stderr, StringComparison.Ordinal);
        Assert.Contains(CellSizePath, stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("total", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The suggested cell size, pasted back into the document, is accepted and gives the mesh
    /// the suggestion names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The advice is what makes a refusal a recovery instruction (AGT-3), so it is tested the
    /// way it will be used. On this model the finest fitting size is 750 mm over 512 = 1.46484
    /// mm - a power-of-two boundary on the z axis - and x has its own at 752 mm over 512 =
    /// 1.46875 mm, 0.27 per cent above it.
    /// A first version printed three figures, 1.47 mm, which crosses x's boundary too and names
    /// a mesh with half the nodes while calling it the finest that fits.
    /// </para>
    /// <para>
    /// And the control: the size the suggestion rounded up from is still refused, so the
    /// suggestion is not merely somewhere coarser than the request.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSuggestedCellSizeIsAcceptedAndGivesTheMeshItNames()
    {
        var (_, stdout, _) = Cli("validate", Astral(1.0), "--json");
        var suggestion = OnlyGridError(stdout).GetProperty("suggestion").GetString()!;

        output.WriteLine(suggestion);

        var match = Regex.Match(suggestion, @"a cell size of ([0-9.]+) mm is the finest that fits, giving (\d+) x (\d+) x (\d+)");

        Assert.True(match.Success, suggestion);

        var suggested = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var named = new[] { match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value }
            .Select(int.Parse).ToArray();

        var (accepted, estimate, stderr) = Cli("estimate", Astral(suggested), "--no-calibrate", "--json", "--threshold", "1e12");

        Assert.True(accepted == 0, stderr);

        using var document = JsonDocument.Parse(estimate);
        var volume = document.RootElement.GetProperty("elements").EnumerateArray()
            .Single(e => e.GetProperty("type").GetString() == "Solved3D");

        Assert.Equal(named, volume.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray());
        Assert.Equal([1025, 65, 513], named);

        // Four parts in a hundred thousand below the boundary the suggestion rounded up from,
        // the mesh is still over the limit.
        Assert.Equal((int)ExitCode.ValidationFailure, Cli("validate", Astral(1.4648)).ExitCode);
    }

    /// <summary>
    /// The space-charge grid is a volume solve too, and meets the same limit at its own path.
    /// </summary>
    /// <remarks>
    /// A particle-in-cell grid allocates the same field type, so 300 nodes across - which rounds
    /// up to 512 intervals and 135 M nodes - would have reached the same argument exception
    /// mid-run, after allocating its mask. 256 is the most that fits, and is the control.
    /// </remarks>
    [Fact]
    public void AnOversizedSpaceChargeGridIsRefusedAtItsOwnPath()
    {
        var (exit, stdout, _) = Cli("validate", SpaceCharge(300), "--json");

        output.WriteLine(stdout);

        Assert.Equal((int)ExitCode.ValidationFailure, exit);

        var error = OnlyGridError(stdout);

        Assert.Equal("/transport/spaceChargeGrid/nodes", error.GetProperty("path").GetString());
        Assert.Equal(513.0 * 513 * 513, error.GetProperty("observed").GetProperty("value").GetDouble());
        Assert.StartsWith("256 is the most that fits", error.GetProperty("suggestion").GetString(), StringComparison.Ordinal);

        Assert.Equal(0, Cli("validate", SpaceCharge(256)).ExitCode);
    }

    /// <summary>
    /// A cell size or bound that is not a number is refused as one, not crashed on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A literal is refused as non-finite when it is read, but a derived bound or cell size
    /// is an expression's result, and a division by a parameter set to zero or the square
    /// root of a negative ratio gives infinity or NaN. Every comparison with NaN is false, so
    /// the positivity checks let it through, and the new mesh arithmetic then threw from
    /// inside validation: <c>einzel validate</c> itself printed <c>INTERNAL_ERROR</c>, where it
    /// had printed OK before and left the crash to <c>run</c>.
    /// </para>
    /// <para>
    /// Infinity was quieter and worse: it passed every check and solved on a mesh of two
    /// intervals an axis, silently. All three are refused at the offending field.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("cellSize", "domainY / zeroRatio")]
    [InlineData("cellSize", "domainY * sqrt(negativeRatio)")]
    [InlineData("minZ", "zPad * sqrt(negativeRatio)")]
    public void ANonFiniteCellSizeOrBoundIsRefusedNotCrashedOn(string field, string expression)
    {
        var model = Astral(4.0, document =>
        {
            var parameters = document["parameters"]!.AsObject();
            parameters["zeroRatio"] = new JsonObject { ["value"] = 0.0, ["unit"] = "1" };
            parameters["negativeRatio"] = new JsonObject { ["value"] = -1.0, ["unit"] = "1" };
            document["fields"]![2]!["solve3d"]![field] = new JsonObject
            {
                ["expression"] = expression,
                ["unit"] = "mm",
            };
        });

        var (exit, stdout, stderr) = Cli("validate", model, "--json");

        output.WriteLine(stdout);

        Assert.Equal((int)ExitCode.ValidationFailure, exit);
        Assert.DoesNotContain(ErrorCodes.InternalError, stderr, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(stdout);
        Assert.Contains(
            document.RootElement.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("code").GetString() == ErrorCodes.ValueOutOfBounds
                && e.GetProperty("path").GetString() == $"/fields/2/solve3d/{field}");
    }

    /// <summary>
    /// The space-charge advice never names a grid the validator refuses.
    /// </summary>
    /// <remarks>
    /// The warning names the node count that would match the packet, and that count can be
    /// past what a volume solve holds: at padding 100 and 40 trajectories it is 1024, and a
    /// grid of 1024 across is refused. So the advice is capped at the most that fits and says
    /// where the rest has to come from.
    /// </remarks>
    [Fact]
    public void TheSpaceChargeAdviceNeverNamesAGridTheValidatorRefuses()
    {
        var (exit, _, stderr) = Cli("run", SpaceCharge(32, padding: 100.0), "--progress", "0");

        output.WriteLine(stderr);

        Assert.True(exit == 0, stderr);
        Assert.Contains($"Try {Einzel.Core.Numerics.VolumeMesh.MostNodesAcrossACube} nodes", stderr, StringComparison.Ordinal);
        Assert.Contains("the most a volume solve can hold", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Try 1024 nodes", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>estimate</c> reports every error validation found, so a refused mesh is never hidden
    /// behind an earlier mistake.
    /// </summary>
    /// <remarks>
    /// It threw the first error alone - as eleven other call sites did - so a model with a bad
    /// plane cell size and an oversized volume was told about the plane, fixed it, and only
    /// then learned its solve would be refused.
    /// </remarks>
    [Fact]
    public void EstimateReportsEveryErrorNotOnlyTheFirst()
    {
        var model = Astral(1.0, document =>
            document["fields"]![0]!["solve"]!["cellSize"] = new JsonObject { ["value"] = -1.0, ["unit"] = "mm" });

        var (exit, _, stderr) = Cli("estimate", model, "--no-calibrate");

        output.WriteLine(stderr);

        Assert.Equal((int)ExitCode.ValidationFailure, exit);
        Assert.Contains("/fields/0/solve/cellSize", stderr, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.GridTooLarge, stderr, StringComparison.Ordinal);
        Assert.Contains(CellSizePath, stderr, StringComparison.Ordinal);
    }

    private static JsonElement OnlyGridError(string validateJson)
    {
        using var document = JsonDocument.Parse(validateJson);
        var errors = document.RootElement.GetProperty("errors").EnumerateArray()
            .Where(e => e.GetProperty("code").GetString() == ErrorCodes.GridTooLarge)
            .ToArray();

        return Assert.Single(errors).Clone();
    }

    /// <summary>The shipped Astral with its volume solve at a given cell size, in mm.</summary>
    private string Astral(double cellMm, Action<JsonNode>? edit = null)
    {
        Directory.CreateDirectory(_root);

        var model = JsonNode.Parse(DeviceTemplates.Read("astral-3d"))!;
        var solve = model["fields"]![2]!["solve3d"]!;

        Assert.Equal("solved3d", model["fields"]![2]!["type"]!.GetValue<string>());

        solve["cellSize"] = new JsonObject { ["value"] = cellMm, ["unit"] = "mm" };
        edit?.Invoke(model);

        var path = Path.Combine(
            _root, "astral-" + cellMm.ToString("R", CultureInfo.InvariantCulture).Replace('.', 'p')
            + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");

        File.WriteAllText(path, model.ToJsonString());

        return path;
    }

    private string SpaceCharge(int nodes, double padding = 4.0)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, $"pic-{nodes}-{padding:F0}.json");
        var paddingText = padding.ToString("R", CultureInfo.InvariantCulture);

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.5",
              "name": "pic-{{nodes}}",
              "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0.0, 0.0, 0.0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 4000.0, "unit": "V" },
                "cloud": {
                  "ions": 40,
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
                "spaceCharge": "pic",
                "spaceChargeGrid": { "nodes": {{nodes}}, "padding": {{paddingText}} }
              }
            }
            """);

        return path;
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
