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
/// The elution ramp stops leaking into the analyser's pseudopotential well.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured on the device the defect was found on</b>, rather than on a synthetic field.
/// `PonderomotiveRampLeakTests` establishes the closed form on a uniform drive, where every
/// term is known; this establishes that the real analyser - a solved DC ring stack whose exit
/// potential ramps, superposed with an analytic quadrupole RF inside a fringed region - is
/// actually the configuration that closed form describes, and that the fix reaches it through
/// the wrappers in between.
/// </para>
/// <para>
/// <b>The control is the whole test.</b> "The well no longer moves" is what a field that was
/// never sampled would also report, so the unfrozen path is measured in the same run and has
/// to move by an amount that matters. The fix is not that the numbers got smaller; it is that
/// they collapse to the floating-point floor while the same measurement without it does not.
/// </para>
/// </remarks>
public sealed class RampedWellOperatingPointTests(ITestOutputHelper output)
{
    /// <summary>Where the well is probed: off axis, where a quadrupole RF is not zero.</summary>
    private static readonly Vec3 Probe = new(0.021, 0.001, 0.0);

    /// <summary>The shipped analyser with a short elution ramp on its exit potential.</summary>
    /// <remarks>
    /// A tenth of the shipped ramp's duration over the same voltage span, so the rate is ten
    /// times the shipped one and the leak is ten times as visible. That is a deliberate
    /// amplification of the thing being measured rather than a change of mechanism: the leak
    /// is linear in the rate, so the shipped run's is a tenth of what is reported here.
    /// </remarks>
    private static CompiledModel Ramped()
    {
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-analyzer"))!;

        document["sequence"] = JsonNode.Parse("""
            [
              {
                "name": "walk",
                "duration": { "value": 800, "unit": "us" },
                "set":  { "exitPotential": { "value": 60.0, "unit": "V" } },
                "ramp": { "exitPotential": { "value": 0.0,  "unit": "V" } }
              }
            ]
            """);

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid
                ? string.Empty
                : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return validation.Model!;
    }

    /// <summary>The well at the probe, seen from an instant, with the ramp held or running.</summary>
    private static double WellAt(
        ITimeVaryingField field, IonSpecies species, double rate, double atSeconds, bool held)
    {
        ITimeVaryingField seen = new TimeShiftedField(field, atSeconds);

        if (held)
        {
            seen = seen.AtOperatingPoint(atSeconds);
        }

        return new PonderomotiveField(seen, species.ChargeSi, species.MassSi, rate)
            .WellAt(in Probe);
    }

    [Fact]
    public void HoldingTheOperatingPointKeepsTheRampOutOfTheWell()
    {
        var model = Ramped();
        var built = FieldAssembly.BuildReported(model);

        var field = Assert.IsAssignableFrom<ITimeVaryingField>(built.Field);

        var species = IonSpecies.FromMassToCharge(622.0, 1);
        var period = field.ShortestPeriodSeconds;

        Assert.True(double.IsFinite(period) && period > 0.0, "the analyser reported no drive");

        // The rate a real ion in this gas damps at. Any positive value does - it scales the
        // well and divides straight back out of a relative comparison - but taking the real
        // one keeps the numbers below comparable with a run's.
        var rate = PonderomotiveField.CollisionRateFromMobility(
            species.ChargeSi, species.MassSi, 1e-4);

        // Deliberately NOT whole drive periods. A diffusive step is set by a stability limit
        // and never lands on the drive, so each assembly opens its averaging window somewhere
        // else in the RF cycle - which is the variation being measured. Sampling at whole
        // periods would pin the phase and report the leak as absent, which is exactly how the
        // synthetic version of this measurement first went wrong.
        var start = 100e-6;

        double Spread(bool held)
        {
            var lowest = double.MaxValue;
            var highest = double.MinValue;

            for (var s = 0; s < 24; s++)
            {
                var well = WellAt(field, species, rate, start + (s * period / 7.0), held);

                lowest = Math.Min(lowest, well);
                highest = Math.Max(highest, well);
            }

            return (highest - lowest) / Math.Abs(highest);
        }

        var running = Spread(held: false);
        var frozen = Spread(held: true);

        output.WriteLine($"drive period                 {period * 1e9:F1} ns");
        output.WriteLine($"ramp still running in window {running:G6}");
        output.WriteLine($"operating point held         {frozen:G6}");
        output.WriteLine($"reduced by                   {running / Math.Max(frozen, 1e-300):G4}x");

        // The control: without the fix the well really does move, and by an amount that
        // matters against the cache's 1e-12 tolerance. Without this the test would pass on a
        // field nobody sampled.
        Assert.True(
            running > 1e-9,
            $"the unfrozen well moved by only {running:G3}, so this configuration does not "
            + "carry the defect and the comparison below asserts nothing");

        // And with the operating point held there is nothing left but arithmetic.
        Assert.True(
            frozen < 1e-13,
            $"holding the operating point left {frozen:G3} of movement in the well, which is "
            + "above the floating-point floor and so is still the ramp");
    }
}
