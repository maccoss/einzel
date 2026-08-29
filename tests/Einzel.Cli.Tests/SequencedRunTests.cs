using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport.Collisions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A run whose phases are not all in the same transport description (SEQ-1).
/// </summary>
/// <remarks>
/// The instrument this exists for is ordinary: ions are collected and thermalised in a
/// gas-filled trap, where the right description is a density, then extracted into vacuum
/// and flown, where it is trajectories. The two modes have been peers since REG-1's seam
/// was built; what was missing was a run that could hold both.
/// </remarks>
public sealed class SequencedRunTests(ITestOutputHelper output)
{
    private const string Model = """
    {
      "schemaVersion": "0.6",
      "name": "trap-then-extract",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [10, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 5, "unit": "V" },
        "cloud": {
          "ions": 200,
          "seed": 7,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.5, "unit": "mm" },
          "longitudinalSpread": { "value": 0.5, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "settle",     "duration": { "value": 1, "unit": "us" },
          "mode": "trajectory" },
        { "name": "thermalise", "duration": { "value": 20, "unit": "us" },
          "mode": "diffusion" },
        { "name": "extract",    "duration": { "value": 5, "unit": "us" },
          "mode": "trajectory" }
      ],
      "fields": [{ "type": "fieldFree" }],
      "detector": {
        "planePoint": { "value": [60, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 32
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    private static (CompiledModel Model, IElectrostaticField Field, BackgroundGas Gas) Load()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model));

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var compiled = validation.Model!;
        var (field, _) = FieldAssembly.BuildReported(compiled);

        return (compiled, field, BackgroundGas.FromModel(compiled.Gas));
    }

    /// <summary>Each phase runs in its own mode, and the boundaries convert.</summary>
    /// <remarks>
    /// Three phases and two mode changes, so both directions of the conversion are
    /// exercised in one run: trajectories become a density, and the density becomes
    /// trajectories again.
    /// </remarks>
    [Fact]
    public void EachPhaseRunsInItsOwnModeAndTheBoundariesConvert()
    {
        var (model, field, gas) = Load();

        Assert.True(model.ChangesTransportMode);

        var outcome = SequencedRun.Execute(model, field, gas);

        foreach (var phase in outcome.Phases)
        {
            output.WriteLine(
                $"{phase.Name,-12} {phase.Mode,-11} ends {phase.EndsAtSeconds * 1e6,6:F1} us  "
                + $"population {phase.Population,10:G6}  "
                + $"centroid x {phase.CentroidMm[0],7:F3} mm  "
                + (phase.Converted ? "converted" : string.Empty));
        }

        Assert.Equal(3, outcome.Phases.Count);
        Assert.Equal(["trajectory", "diffusion", "trajectory"], outcome.Phases.Select(p => p.Mode));

        // The first phase starts from the source, so it converts nothing. The next two
        // each cross a boundary where the mode changes.
        Assert.Equal([false, true, true], outcome.Phases.Select(p => p.Converted));
        Assert.Equal(2, outcome.Conversions);
    }

    /// <summary>
    /// The packet stops travelling when it becomes a density, and that is the physics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A conversion changes the description, and here it visibly changes what the
    /// packet does.</b> Flying, the ions carry the momentum they were launched with and
    /// the centroid advances. As a density in a field-free region the drift is muE with
    /// E = 0, so it does not advance at all — a thermalised packet with nothing pushing
    /// it stays where it is.
    /// </para>
    /// <para>
    /// That is not a defect of the conversion, it is what the conversion <em>means</em>:
    /// the diffusive description has no inertia, because drift-diffusion holds precisely
    /// when the velocity distribution has relaxed. The velocity really is discarded, and
    /// this is what discarding it looks like from outside.
    /// </para>
    /// <para>
    /// It also pins the half that must survive. Position is the one thing both
    /// descriptions carry, so a conversion that deposited or sampled off-centre would
    /// move the centroid — and half a millimetre is well inside the packet while being
    /// far outside a deposit's half-cell.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePacketStopsTravellingWhenItBecomesADensity()
    {
        var (model, field, gas) = Load();

        var outcome = SequencedRun.Execute(model, field, gas);

        var settled = outcome.Phases[0].CentroidMm[0];
        var thermalised = outcome.Phases[1].CentroidMm[0];

        output.WriteLine($"launched at      x = 10.0000 mm");
        output.WriteLine($"after settle     x = {settled:F4} mm  (flying, 1 us)");
        output.WriteLine($"after thermalise x = {thermalised:F4} mm  (a density, 20 us)");

        // Flying, it advanced: 5 V gives m/z 500 about 1.39 mm/us.
        Assert.True(
            settled > 11.0,
            $"a packet launched at 5 V should advance about 1.4 mm in 1 us, and reached {settled}");

        // As a density in no field it does not, over twenty times as long.
        Assert.Equal(settled, thermalised, 0.5);
    }

    /// <summary>
    /// The conversions say what they cost, and none of it can be silenced.
    /// </summary>
    /// <remarks>
    /// SEQ-1's third clause: the conversion is "named as a source of uncertainty". A
    /// caller who reads a number computed after a boundary and does not know the
    /// velocities were invented there has been misled by the platform, which is what
    /// GRD-3 exists to prevent.
    /// </remarks>
    [Fact]
    public void TheConversionsSayWhatTheyCostAndCannotBeSilenced()
    {
        var (model, field, gas) = Load();

        var outcome = SequencedRun.Execute(model, field, gas);

        foreach (var warning in outcome.Warnings.DistinctBy(w => w.Code))
        {
            output.WriteLine($"[{warning.Severity}] {warning.Code}");
        }

        // Trajectories became a density: the velocity distribution is gone.
        Assert.Contains(outcome.Warnings, w =>
            w.Code == "transport.mode-changed" && !w.IsSuppressible);

        // And the density became trajectories: the velocities were invented.
        Assert.Contains(outcome.Warnings, w =>
            w.Code == "transport.velocity-assumed" && !w.IsSuppressible);

        // Plus the run's own statement that it crossed a boundary at all, which is what
        // a reader comparing anything across one needs to see first.
        var crossed = Assert.Single(
            outcome.Warnings, w => w.Code == "transport.mode-changed-in-sequence");

        Assert.False(crossed.IsSuppressible);
        Assert.Contains("2 boundary", crossed.Message, StringComparison.Ordinal);
    }

    /// <summary>A model with no sequence is refused rather than run as one phase.</summary>
    /// <remarks>
    /// `einzel run` already runs an unsequenced model, and doing it a second way here
    /// would be two implementations of one thing - which is how `run` and `test` came to
    /// disagree by five orders in energy drift.
    /// </remarks>
    [Fact]
    public void AModelWithNoSequenceIsRefused()
    {
        var (model, field, gas) = Load();

        var unsequenced = model with { Phases = [] };

        var refusal = Assert.Throws<Core.Errors.EinzelException>(
            () => SequencedRun.Execute(unsequenced, field, gas));

        output.WriteLine(refusal.Error.Constraint);

        Assert.Equal("/sequence", refusal.Error.Path);
    }

    /// <summary>Every trajectory is accounted for within a phase (ACC-5).</summary>
    /// <remarks>
    /// <para>
    /// <b>A leg bounded by time is where this is easiest to get wrong.</b> It ends with
    /// some ions still flying, some arrived at the detector and some struck; only the
    /// first group is handed to the next phase, so keeping just those would make the
    /// packet shrink between phases with nothing saying where the rest went. The first
    /// version of this orchestrator did exactly that.
    /// </para>
    /// <para>
    /// <b>The identity is within a phase, not across a conversion</b>, and that is
    /// physics rather than bookkeeping. A density is re-sampled into however many
    /// trajectories are asked for, so on the far side of a boundary the trajectory count
    /// is a numerical choice while the population is what carries across — the same
    /// <c>ions</c> against <c>population</c> distinction the space-charge work had to
    /// draw, met from the other direction.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryTrajectoryIsAccountedForWithinAPhase()
    {
        var (model, field, gas) = Load();

        var outcome = SequencedRun.Execute(model, field, gas);

        var entering = model.Cloud.Ions;

        foreach (var phase in outcome.Phases)
        {
            if (phase.Trajectories == 0 && phase.Mode == "diffusion")
            {
                output.WriteLine(
                    $"{phase.Name,-12} density, {phase.Population:G6} ions - no trajectories");

                // Whatever the density hands back is a fresh count, so the chain of
                // trajectory identities restarts after it.
                entering = -1;
                continue;
            }

            var leaving = phase.Trajectories + phase.Arrived + phase.Losses.Sum(l => l.Ions);

            output.WriteLine(
                $"{phase.Name,-12} in {entering,4}  flying {phase.Trajectories,4}  "
                + $"arrived {phase.Arrived,4}  lost {phase.Losses.Sum(l => l.Ions),4}  "
                + $"= {leaving}");

            if (entering >= 0)
            {
                Assert.Equal(entering, leaving);
            }

            entering = phase.Trajectories;
        }
    }

    /// <summary>An ion that reaches the detector is counted, not dropped.</summary>
    /// <remarks>
    /// The control for the ledger above, which a run where nothing arrives would satisfy
    /// trivially - and this run's first version did, with every ion still flying at the
    /// end. The detector is moved close enough that the packet crosses it during the
    /// first phase.
    /// </remarks>
    [Fact]
    public void AnIonThatReachesTheDetectorIsCounted()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(
            Model.Replace(
                "\"planePoint\": { \"value\": [60, 0, 0], \"unit\": \"mm\" }",
                "\"planePoint\": { \"value\": [12, 0, 0], \"unit\": \"mm\" }",
                StringComparison.Ordinal)));

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var compiled = validation.Model!;
        var (field, _) = FieldAssembly.BuildReported(compiled);

        var outcome = SequencedRun.Execute(
            compiled, field, BackgroundGas.FromModel(compiled.Gas));

        output.WriteLine($"arrived {outcome.Arrived:G6} ions of {compiled.Cloud.Ions} launched");

        Assert.True(
            outcome.Arrived > 0.0,
            "a detector 2 mm ahead of a packet moving 1.4 mm/us should catch some of it");

        // The first phase's own ledger still closes, arrivals included.
        var first = outcome.Phases[0];

        Assert.Equal(
            compiled.Cloud.Ions,
            first.Trajectories + first.Arrived + first.Losses.Sum(l => l.Ions));
    }

    /// <summary>The same instrument, but starting in the trap.</summary>
    /// <remarks>
    /// Written out rather than patched from <see cref="Model"/>. An edit that matches
    /// nothing is a test that silently stops testing anything, which has now happened
    /// three times in this session alone.
    /// </remarks>
    private const string TrapFirst = """
    {
      "schemaVersion": "0.6",
      "name": "trap-then-extract",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [10, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 5, "unit": "V" },
        "cloud": {
          "ions": 200,
          "seed": 7,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.5, "unit": "mm" },
          "longitudinalSpread": { "value": 0.5, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "trap",    "duration": { "value": 20, "unit": "us" }, "mode": "diffusion" },
        { "name": "settle",  "duration": { "value": 5, "unit": "us" },  "mode": "diffusion" },
        { "name": "extract", "duration": { "value": 5, "unit": "us" },  "mode": "trajectory" }
      ],
      "fields": [{ "type": "fieldFree" }],
      "detector": {
        "planePoint": { "value": [60, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 32
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    /// <summary>
    /// The trap-then-extract instrument: the first phase is the trap, and it is diffusive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the instrument SEQ-1 exists for</b>, and the ordering is not incidental
    /// to it: ions are collected and thermalised in a gas-filled trap, where the right
    /// description is a density, and only then extracted into vacuum and flown. A build
    /// that could only start in the trajectory description could not express the device
    /// the requirement was written about.
    /// </para>
    /// <para>
    /// The seed is <c>DiffusionRun.Seed</c> — the same function a wholly diffusive
    /// <c>einzel run</c> uses — rather than a second one written here. <c>run</c> and
    /// <c>test</c> once computed one flight time two ways and disagreed by 1.3e-10, and
    /// the fix was to collapse them to one implementation.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFirstPhaseMayBeTheTrap()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(TrapFirst));

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var compiled = validation.Model!;
        var (field, _) = FieldAssembly.BuildReported(compiled);

        var outcome = SequencedRun.Execute(
            compiled, field, BackgroundGas.FromModel(compiled.Gas));

        foreach (var phase in outcome.Phases)
        {
            output.WriteLine(
                $"{phase.Name,-8} {phase.Mode,-11} population {phase.Population,10:G6}  "
                + $"x {phase.CentroidMm[0],7:F3} mm  "
                + (phase.Converted ? "converted" : "carried"));
        }

        // Two diffusive phases back to back, then one trajectory phase. The first
        // converts nothing - it seeds - and the second needs no conversion either,
        // because the description has not changed.
        Assert.Equal(["diffusion", "diffusion", "trajectory"], outcome.Phases.Select(p => p.Mode));
        Assert.Equal([false, false, true], outcome.Phases.Select(p => p.Converted));
        Assert.Equal(1, outcome.Conversions);

        // The packet is where the source put it, not at the origin: a seed that ignored
        // the source position would still produce a plausible-looking density.
        Assert.Equal(10.0, outcome.Phases[0].CentroidMm[0], 0.5);
    }

    /// <summary>
    /// A phase that does not change the mode converts nothing.
    /// </summary>
    /// <remarks>
    /// The control on the conversion count. Every boundary would look like a conversion
    /// if the orchestrator asked "is this a new phase" rather than "is this a new
    /// description", and a run that converted at every phase boundary would lose the
    /// velocity distribution repeatedly for no reason.
    /// </remarks>
    [Fact]
    public void APhaseThatKeepsTheModeConvertsNothing()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(
            Model.Replace("\"mode\": \"diffusion\"", "\"mode\": \"trajectory\"",
                StringComparison.Ordinal)));

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var compiled = validation.Model!;

        Assert.False(compiled.ChangesTransportMode);

        var (field, _) = FieldAssembly.BuildReported(compiled);

        var outcome = SequencedRun.Execute(
            compiled, field, BackgroundGas.FromModel(compiled.Gas));

        Assert.Equal(0, outcome.Conversions);
        Assert.All(outcome.Phases, p => Assert.False(p.Converted));

        // And nothing claims a conversion cost that was never paid.
        Assert.DoesNotContain(outcome.Warnings, w => w.Code == "transport.velocity-assumed");
        Assert.DoesNotContain(outcome.Warnings, w => w.Code == "transport.mode-changed-in-sequence");
    }
}
