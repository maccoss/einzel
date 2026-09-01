using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The cost of a mesh is a step function of the cell size, and nothing said so.
/// </summary>
/// <remarks>
/// <para>
/// Each axis rounds its own interval count up to a power of two, so a request landing just
/// over a power of two pays double on that axis — and the node count is the product of
/// three such roundings. On a 635 x 48 x 350 mm analyser at a requested 1 mm that is
/// 1025 x 65 x 513 nodes at 0.62 x 0.75 x 0.68 mm: <b>34.2 M where the request implies
/// 10.7 M</b>, and asking for 1.5 mm instead costs <b>7.9x less</b>.
/// </para>
/// <para>
/// <b>Nothing there is wrong</b> — the mesh is finer than asked for on every axis and never
/// coarser, which is the documented behaviour. What was missing is that somebody planning a
/// multi-day run had no way to know, from the estimate, that a 50 per cent coarser request
/// costs an eighth. GRD-8 gates on cost; GRD-12 says a number states its provenance.
/// </para>
/// </remarks>
public sealed class MeshCliffTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-mesh-cliff", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A long thin box at a cell size that lands just over a power of two.</summary>
    /// <remarks>
    /// Extents chosen so every axis rounds up: 635 / 1 mm is 635 intervals and rounds to
    /// 1024, 48 rounds to 64, 350 rounds to 512. That is the Astral analyser's own aspect
    /// ratio, which is what made the effect worth reporting.
    /// </remarks>
    private string Model(double cellMm)
    {
        Directory.CreateDirectory(_root);

        // The cell size is made filename-safe BEFORE the extension is appended - applying
        // the replace afterwards eats the dot in ".json" too and writes files that are not
        // recognisably models.
        var path = Path.Combine(_root, $"box-{cellMm:F2}".Replace('.', 'p') + ".json");

        var text = """
            {
              "schemaVersion": "0.7",
              "name": "long-thin-box",
              "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [10, 0, 10], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 4000, "unit": "V" }
              },
              "fields": [
                {
                  "type": "solved3d",
                  "solve3d": {
                    "minX": { "value": 0, "unit": "mm" },
                    "maxX": { "value": 635, "unit": "mm" },
                    "minY": { "value": -24, "unit": "mm" },
                    "maxY": { "value": 24, "unit": "mm" },
                    "minZ": { "value": 0, "unit": "mm" },
                    "maxZ": { "value": 350, "unit": "mm" },
                    "cellSize": { "value": CELL, "unit": "mm" },
                    "electrodes": [
                      {
                        "name": "plate",
                        "shape": "box",
                        "minX": { "value": 0, "unit": "mm" },
                        "maxX": { "value": 20, "unit": "mm" },
                        "minY": { "value": -24, "unit": "mm" },
                        "maxY": { "value": -20, "unit": "mm" },
                        "minZ": { "value": 0, "unit": "mm" },
                        "maxZ": { "value": 350, "unit": "mm" },
                        "potential": { "value": 1000, "unit": "V" }
                      }
                    ]
                  }
                }
              ],
              "detector": {
                "planePoint": { "value": [600, 0, 10], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "maximumFlightTime": { "value": 100, "unit": "us" },
                "relativeTolerance": 1e-10
              }
            }
            """.Replace(
            "CELL", cellMm.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

        File.WriteAllText(path, text);

        return path;
    }

    /// <summary>The estimate reports the mesh it will build, not the one that was asked for.</summary>
    /// <remarks>
    /// The two differ by 1.6x on the longest axis here, and a reader planning against the
    /// requested figure would be planning against a mesh that is never built.
    /// </remarks>
    [Fact]
    public void TheAchievedSpacingIsReportedAndIsFinerThanRequested()
    {
        var estimate = EstimateCommand.Execute(Model(1.0), calibrate: false);
        var element = estimate.Elements[0];

        output.WriteLine($"requested   {element.RequestedCell * 1e3:F3} mm");
        output.WriteLine($"achieved    {string.Join(" x ", element.Spacing.Select(m => $"{m * 1e3:F3}"))} mm");
        output.WriteLine($"nodes       {string.Join(" x ", element.Nodes)} = {element.NodeCount / 1e6:F1} M");

        Assert.Equal(3, element.Spacing.Count);

        // Never coarser than asked, on any axis — that is the guarantee the rounding buys.
        Assert.All(element.Spacing, s => Assert.True(s <= element.RequestedCell * (1.0 + 1e-9)));

        // And here, materially finer, which is the thing worth saying out loud.
        Assert.True(
            element.Spacing.Min() < 0.7 * element.RequestedCell,
            "this geometry was chosen so every axis rounds up; if it no longer does, the "
            + "test has stopped exercising the case it was written for");

        Assert.Contains("rounds its interval count up to a power of two", estimate.Basis, StringComparison.Ordinal);
    }

    /// <summary>The suggested coarser request is real, not a rule of thumb.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the assertion that has teeth.</b> A first version suggested twice the
    /// finest spacing — 1.24 mm against a 1 mm request — which lands exactly on the power-of-two
    /// boundary and rounds the wrong side of it, producing the <i>identical</i> mesh. The
    /// suggestion was a no-op and looked authoritative.
    /// </para>
    /// <para>
    /// So what is asserted is the claim itself: take the estimate's own suggested cell size,
    /// ask for it, and check the node count really falls by about the factor it promised.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSuggestedCellSizeActuallyCostsWhatItSays()
    {
        var estimate = EstimateCommand.Execute(Model(1.0), calibrate: false);

        var basis = estimate.Basis;
        var marker = "asking for ";

        Assert.Contains(marker, basis, StringComparison.Ordinal);

        var after = basis[(basis.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        var suggested = double.Parse(
            after.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture);

        var promised = after[(after.IndexOf("which is ", StringComparison.Ordinal) + 9)..];
        var factor = double.Parse(
            promised.Split('x')[0], System.Globalization.CultureInfo.InvariantCulture);

        var coarser = EstimateCommand.Execute(Model(suggested), calibrate: false);

        var actual = (double)estimate.Elements[0].NodeCount / coarser.Elements[0].NodeCount;

        output.WriteLine($"suggested   {suggested:F3} mm");
        output.WriteLine($"promised    {factor:F1}x less");
        output.WriteLine($"delivered   {actual:F1}x less  "
            + $"({estimate.Elements[0].NodeCount / 1e6:F1} M -> {coarser.Elements[0].NodeCount / 1e6:F1} M)");

        Assert.True(
            actual > 1.5,
            $"the estimate suggested {suggested:F3} mm as a cheaper request and it delivered "
            + $"{actual:F2}x. A candidate sitting exactly on the power-of-two boundary rounds "
            + "the wrong side of it and produces the identical mesh");

        // And the promise matches what it delivers, to the digit it was printed with.
        Assert.Equal(factor, actual, 1);
    }
}
