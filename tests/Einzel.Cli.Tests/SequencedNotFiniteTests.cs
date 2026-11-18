using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A density that is not finite is refused by the conversion unlocated, and located in the
/// sequence - with what to change - by the run that knows where the density came from.
/// </summary>
public sealed class SequencedNotFiniteTests(ITestOutputHelper output)
{
    /// <summary>The conversion's refusal, located at the sequence and naming the phase it entered.</summary>
    [Fact]
    public void ARefusedDensityIsLocatedInTheSequenceByTheRun()
    {
        var grid = new Grid2D(0.0, 0.0, 0.0005, 0.0005, 9, 9);
        var density = new DensityField(grid);

        density[2, 2] = 1.0e12;
        density[5, 3] = double.NaN;

        var gas = new BackgroundGas
        {
            Model = CollisionModel.Langevin,
            PressureSi = 100.0,
            TemperatureK = 300.0,
            MassSi = 28.0134 * 1.66053906892e-27,
            PolarizabilitySi = 1.74e-30,
            CrossSectionSi = 250e-20,
        };
        var ion = IonSpecies.FromMassToCharge(500.0, 1);

        var refused = Assert.Throws<NonFiniteDensityException>(() => PacketConversion.ToTrajectories(
            density, 10, ion, gas, FieldFreeSpace.Instance, Mobility.FromCrossSection(gas, ion), seed: 1));

        Assert.Equal("/", refused.Error.Path);

        var located = SequencedRun.NotFiniteAtBoundary(refused.Error, "/sequence", 2, "trap");

        output.WriteLine(located.ToString());

        // The phase by its index, since names need not be unique.
        Assert.Equal("/sequence/2", located.Path);
        Assert.Equal(refused.Error.Constraint, located.Constraint);
        Assert.Contains("'trap'", located.Suggestion, StringComparison.Ordinal);
        Assert.Contains("densityStep", located.Suggestion, StringComparison.Ordinal);
    }
}

/// <summary>
/// Entering the density description from trajectories lets go of the trajectories, says it
/// converted, and adds the conversion's warnings before anything the leg adds.
/// </summary>
public sealed class SequencedEntryTests(ITestOutputHelper output)
{
    /// <summary>
    /// The caller's reference to the states is cleared by the conversion, so the frame that walks
    /// the timeline does not keep a cloud alive through the diffusive leg after it.
    /// </summary>
    [Fact]
    public void EnteringFromTrajectoriesLetsGoOfThem()
    {
        var model = Core.Model.ModelValidator.Validate(
            Io.ModelJson.Parse(RunMemoryCases.Document("tube-entered", 16)), null, null).Model!;
        var grid = DiffusionRun.GridFor(model);

        PhaseState[]? states = Cloud();
        var warnings = new List<Core.Results.ValidityWarning>();

        var density = SequencedRun.Enter(model, grid, ref states, 1000.0, warnings, out var converted);

        output.WriteLine($"{density.Population():G6} ions deposited, {warnings.Count} warning(s): "
            + string.Join(", ", warnings.Select(w => w.Code)));

        Assert.True(converted);
        Assert.Null(states);
        Assert.Equal(32_000.0, density.Population(), 6);
        Assert.NotEmpty(warnings);
    }

    private static PhaseState[] Cloud() =>
        [.. Enumerable.Range(0, 32).Select(i => new PhaseState(new Vec3(0.004 + (i * 1e-4), 0.0, 0.0), new Vec3(0.0, 0.0, 0.0)))];

    /// <summary>Entering from nothing seeds the source and says it converted nothing.</summary>
    [Fact]
    public void EnteringFromNothingSeedsAndConvertsNothing()
    {
        var model = Core.Model.ModelValidator.Validate(
            Io.ModelJson.Parse(RunMemoryCases.Document("tube-plain", 16)), null, null).Model!;
        PhaseState[]? states = null;
        var warnings = new List<Core.Results.ValidityWarning>();

        var density = SequencedRun.Enter(model, DiffusionRun.GridFor(model), ref states, 1.0, warnings, out var converted);

        Assert.False(converted);
        Assert.Empty(warnings);
        Assert.True(density.Population() > 0.0);
    }
}
