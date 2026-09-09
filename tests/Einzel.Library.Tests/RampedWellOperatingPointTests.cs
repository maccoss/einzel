using System.Text.Json.Nodes;

using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The elution ramp stays out of the analyser's pseudopotential well.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured on the device the defect was found on</b>, rather than on a synthetic field.
/// `PonderomotiveRampLeakTests` establishes the closed form on a uniform drive, where every
/// term is known; this establishes that the real analyser - a solved DC ring stack whose exit
/// potential ramps, superposed with an analytic quadrupole RF inside a fringed region - is
/// actually the configuration that closed form describes, and that holding the operating point
/// reaches it through the three wrappers in between.
/// </para>
/// <para>
/// The unheld path is explicitly refused. The held path is compared against the arithmetic
/// floor measured on the same geometry with a phase that holds instead of ramping; an
/// absolute 1e-13 threshold would instead test this machine's arithmetic.
/// </para>
/// </remarks>
public sealed class RampedWellOperatingPointTests(ITestOutputHelper output)
{
    /// <summary>Where the well is probed: off axis, where a quadrupole RF is not zero.</summary>
    private static readonly Vec3 Probe = new(0.021, 0.001, 0.0);

    /// <summary>The shipped analyser, with a sequence spliced in if one is given.</summary>
    private static CompiledModel Analyser(string? sequence)
    {
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-analyzer"))!;

        if (sequence is not null)
        {
            document["sequence"] = JsonNode.Parse(sequence);
        }

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid
                ? string.Empty
                : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return validation.Model!;
    }

    /// <summary>A phase that walks the exit potential down: an elution scan.</summary>
    /// <remarks>
    /// A tenth of the shipped ramp's duration over the same voltage span, so the rate is ten
    /// times the shipped one and the leak is ten times as visible. A deliberate amplification
    /// of the thing being measured rather than a change of mechanism - the leak is linear in
    /// the rate, so the shipped run's is a tenth of what is reported here.
    /// </remarks>
    private const string Walk = """
        [
          {
            "name": "walk",
            "duration": { "value": 800, "unit": "us" },
            "set":  { "exitPotential": { "value": 60.0, "unit": "V" } },
            "ramp": { "exitPotential": { "value": 0.0,  "unit": "V" } }
          }
        ]
        """;

    /// <summary>The same phase holding instead of ramping: the arithmetic floor.</summary>
    private const string Hold = """
        [
          {
            "name": "hold",
            "duration": { "value": 800, "unit": "us" },
            "set": { "exitPotential": { "value": 60.0, "unit": "V" } }
          }
        ]
        """;

    /// <summary>The well at the probe, seen from an instant, with the ramp held or running.</summary>
    private static double WellAt(
        ITimeVaryingField field, CompiledModel model, double atSeconds, bool held)
    {
        var species = IonSpecies.FromModel(model);

        ITimeVaryingField seen = new TimeShiftedField(field, atSeconds);

        if (held)
        {
            seen = seen.AtOperatingPoint(atSeconds);
        }

        // The model's own declared mobility, so the momentum-transfer rate is the one a run
        // uses. An earlier version hardcoded 1e-4 under a comment calling it "the real one";
        // this template declares 0.042802, so that was 428 times out. It changed no
        // conclusion - the rate is a constant at a fixed probe and divides out of a relative
        // comparison - but it made the printed wells incomparable with a run's while the
        // comment said they were the same quantity.
        var rate = PonderomotiveField.CollisionRateFromMobility(
            species.ChargeSi, species.MassSi, model.Mobility!.ZeroFieldSi);

        return new PonderomotiveField(seen, species.ChargeSi, species.MassSi, rate)
            .WellAt(in Probe);
    }

    /// <summary>
    /// The largest relative movement in the well across a cycle, sampled at instants that are
    /// deliberately not whole drive periods.
    /// </summary>
    /// <remarks>
    /// A diffusive step is set by a stability limit and never lands on the drive, so each
    /// assembly opens its averaging window somewhere else in the RF cycle - which is the
    /// variation being measured. Sampling at whole periods would pin the phase and report the
    /// leak as absent, which is exactly how the synthetic version of this measurement first
    /// went wrong.
    /// </remarks>
    private static double Spread(ITimeVaryingField field, CompiledModel model, bool held)
    {
        var period = field.ShortestPeriodSeconds;
        var lowest = double.MaxValue;
        var highest = double.MinValue;

        for (var s = 0; s < 24; s++)
        {
            var well = WellAt(field, model, 100e-6 + (s * period / 7.0), held);

            lowest = Math.Min(lowest, well);
            highest = Math.Max(highest, well);
        }

        return (highest - lowest) / Math.Abs(highest);
    }

    [Fact]
    public void HoldingTheOperatingPointKeepsTheRampOutOfTheWell()
    {
        var ramped = Analyser(Walk);
        var rampedField = Assert.IsAssignableFrom<ITimeVaryingField>(
            FieldAssembly.BuildReported(ramped).Field);

        Assert.True(
            double.IsFinite(rampedField.ShortestPeriodSeconds)
                && rampedField.ShortestPeriodSeconds > 0.0,
            "the analyser reported no drive, so there is no cycle to average over");

        // An unheld sequence is no longer certified as monochromatic: averaging a
        // ramp into the RF is refused rather than yielding the old contaminated well.
        var running = Assert.Throws<Einzel.Core.Errors.EinzelException>(
            () => Spread(rampedField, ramped, held: false));
        var frozen = Spread(rampedField, ramped, held: true);

        // THE FLOOR, MEASURED ON THIS MACHINE. The same geometry with a phase that holds
        // instead of ramping has no ramp to leak, so whatever it reports is round-off in the
        // solved channels, the interpolant and the cycle sum. Asserting an absolute 1e-13
        // instead put 1.5x of headroom over a measured 6.5e-14, and a floor asserted that
        // tightly fails on another runner and reads as the fix having regressed - which is
        // what docs/lessons.md records for AllocationDoesNotGrowWithStepCount.
        var held = Analyser(Hold);
        var heldField = Assert.IsAssignableFrom<ITimeVaryingField>(
            FieldAssembly.BuildReported(held).Field);

        var floor = Spread(heldField, held, held: true);

        output.WriteLine($"drive period                   {rampedField.ShortestPeriodSeconds * 1e9:F1} ns");
        output.WriteLine($"ramp still running in window   {running.Error.Code}");
        output.WriteLine($"operating point held           {frozen:G6}");
        output.WriteLine($"arithmetic floor (phase holds) {floor:G6}");

        Assert.Equal(Einzel.Core.Errors.ErrorCodes.RegimeInvalid, running.Error.Code);

        // And with the operating point held there is nothing left but that floor. Eight times
        // rather than exactly it, because the two runs sum different numbers and neither is
        // the other's bound - what is asserted is the same ORDER, against a measurement taken
        // on the same machine in the same test.
        Assert.True(
            frozen <= Math.Max(8.0 * floor, 1e-15),
            $"holding the operating point left {frozen:G3} of movement against an arithmetic "
            + $"floor of {floor:G3} measured on the same geometry, so some of it is still the ramp");
    }

    /// <summary>
    /// A field with no sequence in it is the same instance at every operating point, through
    /// every wrapper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the safety argument for the whole change and it existed only in prose.</b>
    /// Holding an operating point is supposed to be free and invisible for the models that
    /// have no sequence - which is almost all of them, and every published diffusive number -
    /// and the mechanism is that each implementation returns <c>this</c> when it has nothing
    /// to hold. Reference identity is the sharp form of that: a field that returned an equal
    /// copy would also be correct today and would allocate one per density step, and any
    /// derived quantity computed differently in the copy path would move numbers in their last
    /// bits with the whole suite still green.
    /// </para>
    /// <para>
    /// The shipped analyser is a genuine composition - a solved channel set, an analytic
    /// quadrupole RF, a region wrapper and a superposition - so this exercises the identity
    /// short circuit in all four rather than only at the top.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnsequencedFieldIsTheSameInstanceAtEveryOperatingPoint()
    {
        var model = Analyser(sequence: null);

        Assert.Empty(model.Phases);

        var field = Assert.IsAssignableFrom<ITimeVaryingField>(
            FieldAssembly.BuildReported(model).Field);

        output.WriteLine($"top-level field: {field.GetType().Name}");

        foreach (var atSeconds in (double[])[0.0, 1e-9, 100e-6, 8e-3])
        {
            Assert.Same(field, field.AtOperatingPoint(atSeconds));
        }

        // AND IT STILL REFUSES A NON-FINITE INSTANT. Returning the same instance is a
        // shortcut past work, not past validation - a guard that fires only where the field
        // has something to hold makes whether a caller's NaN is caught depend on the shape
        // of the model. Raised by review, on the sequenced class; the same hole was in the
        // solved one, and it is reachable only through a composition like this.
        var refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => field.AtOperatingPoint(double.NaN));

        Assert.Contains("finite", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a field WITH a sequence is held rather than returned unchanged - the control that
    /// stops the test above passing because nothing is ever held.
    /// </summary>
    /// <remarks>
    /// Type-preserving as well as different: the held field has to remain the same kind of
    /// composition, or the region, the conductor bounds and the channel set would be answered
    /// by something other than what the model describes.
    /// </remarks>
    [Fact]
    public void ASequencedFieldIsHeldAndKeepsItsShape()
    {
        var model = Analyser(Walk);

        Assert.NotEmpty(model.Phases);

        var field = Assert.IsAssignableFrom<ITimeVaryingField>(
            FieldAssembly.BuildReported(model).Field);

        var held = field.AtOperatingPoint(100e-6);

        output.WriteLine($"before: {field.GetType().Name}");
        output.WriteLine($"after:  {held.GetType().Name}");

        Assert.NotSame(field, held);
        Assert.IsType(field.GetType(), held);
    }
}
