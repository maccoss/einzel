using Einzel.Commands;
using Einzel.Shell;

using Xunit.Abstractions;

namespace Einzel.Shell.Tests;

/// <summary>
/// The live path end to end: a run that is still going hands the viewport frames, and the
/// frames move.
/// </summary>
/// <remarks>
/// <para>
/// This is the claim the window exists for - "run the analysis and see it happen" - and it
/// is checked without a window, so it holds on every platform the engine runs on. What a
/// GUI adds on top is a control that calls this and a control that draws the result.
/// </para>
/// <para>
/// The equivalent for the WPF shell targets net10.0-windows, so a solution-wide test run
/// walks past it on Linux and the claim was checked on one platform.
/// </para>
/// </remarks>
public sealed class WatchedRunTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _model = Path.Combine(
        Path.GetTempPath(), $"einzel-watch-{Guid.NewGuid():N}.json");

    /// <summary>A packet drifting down a tube in a gas: small, and genuinely diffusive.</summary>
    /// <remarks>
    /// A coarse grid on purpose. What is being checked is that frames arrive and change,
    /// which needs the transport to run and does not need it to be converged - and a test
    /// that took a minute would be a test nobody runs.
    /// </remarks>
    private const string Model = """
    {
      "schemaVersion": "0.4",
      "name": "watched-drift-tube",
      "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [2, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 0.001, "unit": "V" },
        "cloud": {
          "ions": 1, "population": 10000,
          "transverseSpread": { "value": 1.0, "unit": "mm" },
          "longitudinalSpread": { "value": 1.0, "unit": "mm" }
        }
      },
      "fields": [{ "type": "uniform", "field": { "value": [2000.0, 0, 0], "unit": "V/m" } }],
      "detector": {
        "planePoint": { "value": [40.0, 0.0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "diffusion",
        "maximumFlightTime": { "value": 400, "unit": "us" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 16
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "angstrom^2" }
        }
      }
    }
    """;

    /// <summary>The viewport is filled repeatedly, and the packet is somewhere new each time.</summary>
    /// <remarks>
    /// <b>Two assertions, and the second is what makes the first mean anything.</b> That
    /// frames arrive says the callback is wired; that the packet's centre MOVES says they
    /// are frames of a run rather than the same instant handed over repeatedly, which is
    /// what a watch that had accidentally captured its first frame would look like.
    /// </remarks>
    [Fact]
    public void AWatchedRunDeliversFramesAndThePacketMoves()
    {
        File.WriteAllText(_model, Model);

        var centres = new List<double>();

        var watcher = new Watcher(
            frame =>
            {
                if (frame.Density.Count > 0)
                {
                    // The leading edge of the deepest contour stands in for where the
                    // packet is: the outcome carries shells rather than a centroid.
                    centres.Add(frame.Density.Max(d => Reach(d.VerticesMm)));
                }
            },
            TimeSpan.FromMilliseconds(20));

        var final = ViewportCommand.Watch(_model, watcher);

        output.WriteLine($"{centres.Count} frames with a packet; "
            + $"leading edge {string.Join(" -> ", centres.Take(6).Select(c => c.ToString("F2")))}");
        output.WriteLine($"final frame has {final.Density.Count} contours");

        Assert.True(centres.Count >= 2, $"only {centres.Count} frames carried a packet");

        // It drifts, so the leading edge advances. Asserted as a real distance rather than
        // as "not equal", because two frames differing in the last bit would pass that.
        Assert.True(
            centres[^1] - centres[0] > 1.0,
            $"the packet's leading edge moved {centres[^1] - centres[0]:F3} mm across the run");
    }

    /// <summary>A trajectory model is refused, with a reason.</summary>
    /// <remarks>
    /// <b>The refusal is the design.</b> A flight finishes faster than a viewport could draw
    /// it part way through, so the whole bundle arriving at once is sooner than the first
    /// frame of a watch would be - and a button that appeared to do nothing would be worse
    /// than one that says why.
    /// </remarks>
    [Fact]
    public void AFlightIsRefusedRatherThanWatched()
    {
        File.WriteAllText(_model, Model.Replace("\"mode\": \"diffusion\"", "\"mode\": \"trajectory\"", StringComparison.Ordinal));

        var error = Assert.ThrowsAny<Exception>(
            () => ViewportCommand.Watch(_model, new Watcher(_ => { }, TimeSpan.Zero)));

        output.WriteLine(error.Message);

        Assert.Contains("density", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_model))
        {
            File.Delete(_model);
        }
    }

    private static double Reach(IReadOnlyList<double> verticesMm)
    {
        var far = double.MinValue;

        for (var i = 0; i + 2 < verticesMm.Count; i += 3)
        {
            far = Math.Max(far, verticesMm[i]);
        }

        return far == double.MinValue ? 0.0 : far;
    }
}
