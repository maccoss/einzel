using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The trajectory a run writes with <c>--vtu</c> is the flight the run reports, gas included.
/// </summary>
/// <remarks>
/// <para>
/// It was not. The reportable flight time comes from a convergence study that flies the
/// declared gas; the trajectory file came from a second integration in the same method
/// that never attached the collision sampler. So a <c>--vtu</c> of any collisional run drew
/// a vacuum flight beside a collisional result: on the PNNL funnel at 2.5 mbar, an ion
/// crossing eighty millimetres on the axis in ten microseconds, next to a result of 589 us
/// and a strike on the exit plate. The drawing is the artifact most likely to be looked at
/// without the numbers, which is what makes this the bad direction to be wrong in.
/// </para>
/// <para>
/// This is the same defect the project has recorded for the figure-of-merit path (a declared
/// gas took no part in <c>einzel test</c>) and for the regime inspector (the path flown in
/// vacuum and the gas numbers reported along it). A shared entry point is not a shared
/// computation; the sampler has to be handed to every integration that claims to be the run.
/// </para>
/// </remarks>
public sealed class TrajectoryFileGasTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// The trajectory file's last instant is the reported flight time, and both are the gas's.
    /// </summary>
    /// <remarks>
    /// A drift tube in 1 mbar of nitrogen under 2 kV/m: in vacuum the ion covers the 38 mm in
    /// about 14 us, accelerating the whole way; in the gas it drifts at the mobility-limited
    /// speed and takes some two hundred. The two are fifteen-fold apart, so the assertion has
    /// no tolerance to hide in - and the same seed on both integrations means the file's flight
    /// and the reported one share every collision instant, so they agree to the integrator's
    /// tolerance rather than to the stochastic spread.
    /// </remarks>
    [Fact]
    public void TheWrittenTrajectoryIsTheCollisionalFlightTheRunReports()
    {
        var (init, _, _) = Run("init", _root);
        Assert.Equal(0, init);

        var modelPath = Path.Combine(_root, "models", "gas-drift.json");
        File.WriteAllText(modelPath, DriftTubeInGas);

        var (exit, stdout, stderr) = Run("run", modelPath, "--vtu", "--json");
        output.WriteLine(stderr);
        Assert.True(exit == 0 || exit == 2, $"exit {exit}: {stderr}");

        using var result = JsonDocument.Parse(stdout);
        var reportedUs = result.RootElement.GetProperty("flightTime").GetProperty("value").GetDouble();

        var vtuPath = Path.Combine(_root, ".einzel", "gas-drift.trajectory.vtu");
        Assert.True(File.Exists(vtuPath), "no trajectory file was written");
        var vtu = File.ReadAllText(vtuPath);

        var times = Regex.Match(vtu, "Name=\"time\"[^>]*>(.*?)</DataArray>", RegexOptions.Singleline)
            .Groups[1].Value
            .Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.Parse(t, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.True(times.Length > 10, $"expected a sampled flight, got {times.Length} samples");
        var drawnUs = times[^1] * 1e6;

        // Vacuum would be sqrt(2 d m / (q E)): 38 mm, m/z 500, 2 kV/m.
        var vacuumUs = Math.Sqrt(2 * 0.038 * 500 * 1.66053906660e-27 / (1.602176634e-19 * 2000.0)) * 1e6;

        output.WriteLine(
            $"reported {reportedUs:F1} us, drawn {drawnUs:F1} us over {times.Length} samples; "
            + $"vacuum would be {vacuumUs:F1} us");

        Assert.True(drawnUs > 5 * vacuumUs, $"the drawn flight ({drawnUs:F1} us) is the vacuum one ({vacuumUs:F1} us)");
        Assert.InRange(drawnUs / reportedUs, 0.97, 1.03);
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

    private const string DriftTubeInGas = """
        {
          "schemaVersion": "0.5",
          "name": "gas-drift",
          "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [2.0, 0.0, 0.0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 0.01, "unit": "V" }
          },
          "fields": [ { "type": "uniform", "field": { "value": [2000.0, 0, 0], "unit": "V/m" } } ],
          "detector": {
            "planePoint": { "value": [40.0, 0.0, 0.0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 2000.0, "unit": "us" },
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": 1.0, "unit": "mbar" },
              "temperature": { "value": 300.0, "unit": "K" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "crossSection": { "value": 250, "unit": "angstrom^2" },
              "seed": 3
            }
          }
        }
        """;
}
