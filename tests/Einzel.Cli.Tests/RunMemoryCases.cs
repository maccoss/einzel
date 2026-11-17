using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Numerics;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Testing;
using Einzel.Transport.Diffusion;

namespace Einzel.Cli.Tests;

/// <summary>
/// The diffusive runs the memory tests measure, by name, for the shared child process
/// (<see cref="MeasuringChild"/>) to run and measure.
/// </summary>
/// <remarks>
/// <para>
/// A kind is a geometry and whether the density carries its own charge - <c>tube-plain</c>,
/// <c>tube-charged</c>, <c>bore-plain</c>, <c>bore-charged</c> - and a size is the density grid's
/// intervals along each axis. The field is assembled when the case is made, before anything is
/// measured: what is measured is the density run, not the solve beneath it, which is how
/// <c>einzel estimate</c> separates the two terms as well.
/// </para>
/// <para>
/// <b>The run has to refresh its self-field inside the stepping loop</b>, and each case reports
/// how often it solved so that the test can assert it did. The first Poisson solve is done
/// before the loop, when the step's own arrays do not exist yet; every later refresh solves on
/// top of them, and that is the peak.
/// </para>
/// </remarks>
internal static class RunMemoryCases
{
    /// <summary>The declared flight, in microseconds: long enough to refresh a self-field several times.</summary>
    internal const double FlightMicroseconds = 6.0;

    /// <summary>What this test assembly tells the child: its command, its runs, and whose code they run.</summary>
    internal static ChildHost Host { get; } = new(
        "run-memory", Case, [typeof(DiffusionRun), typeof(DriftDiffusion), typeof(FieldAssembly), typeof(GridArrays)]);

    /// <summary>The model document a kind and a size name.</summary>
    internal static string Document(string kind, int intervals)
    {
        var spaceCharge = kind.EndsWith("-charged", StringComparison.Ordinal) ? "meanField" : null;

        return kind switch
        {
            "tube-plain" or "tube-charged" => DriftTubes.Cartesian(
                DriftTubes.Ion, DriftTubes.Mobility, spaceCharge: spaceCharge,
                intervalsX: intervals, intervalsY: intervals, flightMicroseconds: FlightMicroseconds),
            "bore-plain" or "bore-charged" => DriftTubes.Bore(spaceCharge, intervals, intervals, FlightMicroseconds),
            _ => throw new ArgumentException($"no run is called '{kind}'", nameof(kind)),
        };
    }

    private static MeasuredCase Case(string kind, double size)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Document(kind, (int)size)), null, null);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"{kind} does not validate: {string.Join("; ", validation.Errors.Select(e => e.ToString()))}");
        }

        var model = validation.Model!;
        var (field, warnings) = FieldAssembly.BuildReported(model);
        var solves = 0;
        var steps = 0;

        return new MeasuredCase(
            () =>
            {
                var outcome = DiffusionRun.Execute(model, field, warnings);
                solves = outcome.Result.SelfFieldSolves;
                steps = outcome.Result.Steps;
                return outcome;
            },
            () => new()
            {
                ["solves"] = solves,
                ["steps"] = steps,
                ["nodes"] = DiffusionRun.GridFor(model).NodeCount,
            });
    }
}

/// <summary>
/// The test assembly's entry point: the child process the memory tests measure runs in.
/// </summary>
/// <remarks>
/// The test host loads this assembly as a library and never calls this.
/// <see cref="MeasuringChild"/> starts it as a process of its own.
/// </remarks>
internal static class MeasuringEntry
{
    private static int Main(string[] args) => MeasuringChild.Enter(RunMemoryCases.Host, args);
}

/// <summary>
/// Every run this assembly's memory tests measure, measured once (<see cref="MeasuredCases"/>).
/// </summary>
public sealed class MeasuredRuns() : MeasuredCases(RunMemoryCases.Host, OptimizedCases, FirstTierCases)
{
    private static readonly (string Kind, double Size)[] OptimizedCases =
    [
        ("tube-plain", 128), ("tube-charged", 128), ("tube-plain", 256), ("tube-charged", 256),
        ("bore-plain", 128), ("bore-charged", 128), ("bore-plain", 256), ("bore-charged", 256),
    ];

    private static readonly (string Kind, double Size)[] FirstTierCases = [];
}
