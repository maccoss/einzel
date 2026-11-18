using System.Text.Json.Nodes;

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
/// A kind is a geometry and a configuration, and a size is the density grid's intervals along
/// each axis. The geometry is a Cartesian <c>tube</c> with nothing in it, or a cylindrical
/// <c>bore</c> whose earthed wall lies inside the density grid. The configurations are the ones
/// <c>einzel estimate</c> bills differently, one for each term of its statement:
/// </para>
/// <list type="bullet">
/// <item><c>plain</c> - explicit, one species, nothing else: the run's own arrays.</item>
/// <item><c>charged</c> - mean-field space charge: the self-field and its Poisson solve.</item>
/// <item><c>implicit</c> - the implicit scheme, stepping past the stability limit.</item>
/// <item><c>driven</c> - an RF quadrupole across the tube: the cycle-averaged well's cache.</item>
/// <item><c>snapshots</c> - the density recorded at four instants, as a render asks for.</item>
/// <item><c>mixture</c> and <c>mixture-charged</c> - two species, and their shared self-field.</item>
/// <item><c>ramped</c> - a sequence whose second phase ramps the wall's potential, so the
/// operator is re-assembled as it goes, through the sequenced path.</item>
/// <item><c>entered</c> - a trajectory model whose sequence flies a small cloud briefly and then
/// enters the diffusive description, converting it: the density's working set on a model
/// whose own mode is not diffusive. The cloud is the same size on both grids, so what the
/// trajectory leg holds cancels in the slope.</item>
/// </list>
/// <para>
/// The field is assembled when the case is made, before anything is measured: what is measured
/// is the density run, not the solve beneath it, which is how the estimate separates the two.
/// </para>
/// <para>
/// <b>A charged run has to refresh its self-field inside the stepping loop</b>, and each case
/// reports how often it solved so that the test can assert it did. The first Poisson solve is
/// done before the loop, when the step's own arrays do not exist yet; every later refresh
/// solves on top of them, and that is the peak.
/// </para>
/// </remarks>
internal static class RunMemoryCases
{
    /// <summary>The declared flight, in microseconds: long enough to refresh a self-field several times.</summary>
    internal const double FlightMicroseconds = 6.0;

    /// <summary>The instants a <c>snapshots</c> case records the density at, in seconds.</summary>
    internal static readonly double[] SnapshotSeconds = [1e-6, 2e-6, 3e-6, 4e-6];

    /// <summary>What this test assembly tells the child: its command, its runs, and whose code they run.</summary>
    internal static ChildHost Host { get; } = new(
        "run-memory", Case, [typeof(DiffusionRun), typeof(DriftDiffusion), typeof(FieldAssembly), typeof(GridArrays)]);

    /// <summary>The model document a kind and a size name.</summary>
    internal static string Document(string kind, int intervals)
    {
        var dash = kind.IndexOf('-', StringComparison.Ordinal);
        var (geometry, configuration) = (kind[..dash], kind[(dash + 1)..]);

        var charged = configuration is "charged" or "mixture-charged" ? "meanField" : null;
        var mixture = configuration is "mixture" or "mixture-charged";

        var text = geometry switch
        {
            "tube" when mixture => DriftTubes.Cartesian(
                species: TwoSpecies, spaceCharge: charged,
                intervalsX: intervals, intervalsY: intervals, flightMicroseconds: FlightMicroseconds),
            "tube" => DriftTubes.Cartesian(
                DriftTubes.Ion, DriftTubes.Mobility, spaceCharge: charged,
                intervalsX: intervals, intervalsY: intervals, flightMicroseconds: FlightMicroseconds),
            "bore" when !mixture => DriftTubes.Bore(charged, intervals, intervals, FlightMicroseconds),
            _ => throw new ArgumentException($"no run is called '{kind}'", nameof(kind)),
        };

        var document = JsonNode.Parse(text)!;

        switch (configuration)
        {
            case "plain" or "charged" or "mixture" or "mixture-charged" or "snapshots":
                break;

            case "implicit":
                document["transport"]!["densityStep"] = JsonNode.Parse("""{ "scheme": "implicit", "gain": 4 }""");
                break;

            case "driven":
                document["fields"]!.AsArray().Add(JsonNode.Parse("""
                    { "type": "idealQuadrupoleRf",
                      "directPotential": { "value": 0.0, "unit": "V" },
                      "driveAmplitude": { "value": 20.0, "unit": "V" },
                      "driveFrequency": { "value": 1.0, "unit": "MHz" },
                      "inscribedRadius": { "value": 4.0, "unit": "mm" } }
                    """));
                break;

            case "ramped" when geometry == "bore":
                document["parameters"] = JsonNode.Parse("""{ "wallVolts": { "value": 0.0, "unit": "V" } }""");

                document["fields"]![1]!["solve"]!["electrodes"]![0]!["potential"] =
                    JsonNode.Parse("""{ "expression": "wallVolts", "unit": "V" }""");

                document["sequence"] = JsonNode.Parse("""
                    [ { "name": "hold", "duration": { "value": 2, "unit": "us" } },
                      { "name": "ramp", "duration": { "value": 4, "unit": "us" },
                        "ramp": { "wallVolts": { "value": 5.0, "unit": "V" } } } ]
                    """);
                break;

            case "entered" when geometry == "tube":
                document["transport"]!["mode"] = "trajectory";
                document["source"]!["cloud"]!["ions"] = 64;
                document["sequence"] = JsonNode.Parse(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $$"""
                    [ { "name": "fly", "duration": { "value": 0.05, "unit": "us" }, "mode": "trajectory" },
                      { "name": "drift", "duration": { "value": {{FlightMicroseconds - 0.05}}, "unit": "us" }, "mode": "diffusion" } ]
                    """));
                break;

            default:
                throw new ArgumentException($"no run is called '{kind}'", nameof(kind));
        }

        return document.ToJsonString();
    }

    private const string TwoSpecies = """
        "species": [
          { "name": "a", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
            "mobility": { "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } }, "population": 1000000 },
          { "name": "b", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
            "mobility": { "zeroField": { "value": 0.04, "unit": "m^2/(V s)" } }, "population": 1000000 } ],
        """;

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
        var assemblies = 0;
        var conversions = 0;

        Func<object> run = kind switch
        {
            _ when model.IsMixture => () =>
            {
                var outcome = DiffusionRun.ExecuteMixture(model, field, warnings, progress: new BetweenSteps());
                solves = outcome.Result.SelfFieldSolves;
                steps = outcome.Result.Steps;
                return outcome;
            },
            _ when model.Phases.Count > 0 => () =>
            {
                var outcome = SequencedRun.Execute(model, field, GasFlowImport.Resolve(model.Gas, "."), new BetweenSteps());
                assemblies = outcome.Phases.Sum(phase => phase.Assemblies);
                conversions = outcome.Conversions;
                return outcome;
            },
            _ => () =>
            {
                var outcome = DiffusionRun.Execute(
                    model, field, warnings,
                    snapshotSeconds: kind.EndsWith("-snapshots", StringComparison.Ordinal) ? SnapshotSeconds : null,
                    progress: new BetweenSteps());

                solves = outcome.Result.SelfFieldSolves;
                steps = outcome.Result.Steps;
                assemblies = outcome.Result.Assemblies;
                return outcome;
            },
        };

        return new MeasuredCase(
            run,
            () => new()
            {
                ["solves"] = solves,
                ["steps"] = steps,
                ["assemblies"] = assemblies,
                ["conversions"] = conversions,
                ["nodes"] = DiffusionRun.GridFor(model).NodeCount,
            });
    }
}

/// <summary>
/// A run's progress listener that wants no report and offers the measurement a reading at
/// every step (<see cref="LiveSet.Checkpoint"/>).
/// </summary>
/// <remarks>
/// A run asks its listener at each step whether it wants a report, on the thread that steps.
/// A ramped phase re-assembles its operator every step, which allocates a few kilobytes and no
/// grid array, so without this there is nowhere to read between the run's last array and its
/// end, and the most it can have held is bounded by everything those steps allocated. A run
/// that is watched and one that is not are the same run to the last bit, which the engine's own
/// tests hold.
/// </remarks>
internal sealed class BetweenSteps : IRunProgress
{
    /// <inheritdoc/>
    public bool Wants(int steps, double timeSeconds)
    {
        LiveSet.Checkpoint();
        return false;
    }

    /// <inheritdoc/>
    public void Reached(DensityProgressReport report)
    {
    }

    /// <inheritdoc/>
    public void Entering(string phase, int index, int count, double ofSeconds)
    {
    }

    /// <inheritdoc/>
    public void Completed(PhaseOutcome phase)
    {
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
/// The runs this assembly's memory tests measure, each measured once (<see cref="MeasuredCases"/>).
/// </summary>
public sealed class MeasuredRuns() : MeasuredCases(RunMemoryCases.Host);
