using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport.Collisions;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A driven geometry inside a diffusive phase of a sequence.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fifth occurrence of one defect, and the one I introduced.</b> A driven field
/// answers the time-free interface without failing, so a diffusive phase stepped a
/// density through the RF at the phase's first instant — a field that exists for no
/// length of time. It has happened in <c>einzel solve</c>, in the wholly diffusive mode,
/// in <c>SuperposedField</c>, in the renderer, and now in the sequenced path.
/// </para>
/// <para>
/// What a slow ion in a gas actually feels is the cycle average, which is what
/// <c>PonderomotiveField</c> computes. The wrapper is now shared with the wholly
/// diffusive path rather than written twice, which is what stops there being a sixth.
/// </para>
/// </remarks>
public sealed class SequencedDrivenTests(ITestOutputHelper output)
{
    /// <summary>
    /// A real quadrupole - four rods, pairs in antiphase - with the packet released off
    /// axis in a gas.
    /// </summary>
    /// <remarks>
    /// <b>It has to be four rods, and that is the point.</b> A first version of this test
    /// used two plates, which give a nearly <em>uniform</em> field between them - and the
    /// ponderomotive force goes as the gradient of E squared, so there was no well and the
    /// packet moved 0.1% whether the drive was on or off. The test passed, and passed for
    /// a reason that had nothing to do with what it claimed to check.
    /// </remarks>
    private const string DrivenTrap = """
    {
      "schemaVersion": "0.6",
      "name": "driven-diffusive-phase",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "parameters": {
        "rf": { "value": {{RF}}, "unit": "V" },
        "r0": { "value": 4, "unit": "mm" },
        "rodRadius": { "expression": "r0 * 1.1468", "unit": "mm" },
        "rodCentre": { "expression": "r0 + rodRadius", "unit": "mm" },
        "half": { "expression": "rodCentre + rodRadius * 1.2", "unit": "mm" }
      },
      "source": {
        "position": { "value": [0, 1.5, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 1, "unit": "V" },
        "cloud": {
          "ions": 40,
          "seed": 3,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.2, "unit": "mm" },
          "longitudinalSpread": { "value": 0.2, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "confine", "duration": { "value": 60, "unit": "us" }, "mode": "diffusion" },
        { "name": "eject",   "duration": { "value": 1, "unit": "us" },  "mode": "trajectory" }
      ],
      "fields": [{
        "type": "solved2d",
        "solve": {
          "minX": { "expression": "-half", "unit": "mm" },
          "minY": { "expression": "-half", "unit": "mm" },
          "maxX": { "expression": "half", "unit": "mm" },
          "maxY": { "expression": "half", "unit": "mm" },
          "cellSize": { "value": 0.4, "unit": "mm" },
          "drive": { "frequency": { "value": 1, "unit": "MHz" } },
          "electrodes": [
            { "name": "rodXPlus", "shape": "disc",
              "centreX": { "expression": "rodCentre", "unit": "mm" },
              "centreY": { "value": 0, "unit": "mm" },
              "radius": { "expression": "rodRadius", "unit": "mm" },
              "potential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "expression": "rf", "unit": "V" } },
            { "name": "rodXMinus", "shape": "disc",
              "centreX": { "expression": "-rodCentre", "unit": "mm" },
              "centreY": { "value": 0, "unit": "mm" },
              "radius": { "expression": "rodRadius", "unit": "mm" },
              "potential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "expression": "rf", "unit": "V" } },
            { "name": "rodYPlus", "shape": "disc",
              "centreX": { "value": 0, "unit": "mm" },
              "centreY": { "expression": "rodCentre", "unit": "mm" },
              "radius": { "expression": "rodRadius", "unit": "mm" },
              "potential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "expression": "-rf", "unit": "V" } },
            { "name": "rodYMinus", "shape": "disc",
              "centreX": { "value": 0, "unit": "mm" },
              "centreY": { "expression": "-rodCentre", "unit": "mm" },
              "radius": { "expression": "rodRadius", "unit": "mm" },
              "potential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "expression": "-rf", "unit": "V" } }
          ]
        }
      }],
      "detector": {
        "planePoint": { "value": [40, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "mobility": { "zeroField": { "value": 0.02, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
          "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 64
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 2, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    /// <summary>The same instrument at a chosen drive amplitude.</summary>
    /// <remarks>
    /// A placeholder rather than a search-and-replace on a literal. The previous version
    /// looked for "300" after the model had already moved to 400 V, so the control ran
    /// the driven case twice - caught only because the test asserted the two differed.
    /// </remarks>
    private static string At(double rfVolts) =>
        DrivenTrap.Replace(
            "{{RF}}", rfVolts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    private static SequencedOutcome Run(string document)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(document));

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        var model = validation.Model!;
        var (field, _) = FieldAssembly.BuildReported(model);

        return SequencedRun.Execute(model, field, BackgroundGas.FromModel(model.Gas));
    }

    /// <summary>
    /// The RF confines the density during a diffusive phase, rather than pushing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The discriminating check, and only the cycle average passes it.</b> A
    /// ponderomotive well is symmetric about the axis and pulls the packet toward it
    /// whichever side it starts on. A snapshot of the RF at the phase's first instant is
    /// a plain transverse field that pushes it one way — and the drive is at its peak at
    /// t = 0, so a snapshot is the worst case rather than an average one.
    /// </para>
    /// <para>
    /// So the sign of the packet's motion separates the two. Released 1.2 mm off axis, a
    /// confined packet comes back toward zero; a pushed one does not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDriveConfinesTheDensityRatherThanPushingIt()
    {
        var driven = Run(At(400));
        var confine = driven.Phases[0];

        output.WriteLine($"released at y = 1.5 mm");
        output.WriteLine($"after 60 us of RF at 400 V: y = {confine.CentroidMm[1]:F4} mm");

        // Toward the axis, not away from it. A snapshot at peak drive would move it in
        // one direction with no reference to where the axis is.
        // Materially toward the axis, not a rounding away from where it started. The
        // two-plate version of this test moved 0.1% and passed, which is what a
        // threshold set at "less than where it began" buys you.
        Assert.True(
            confine.CentroidMm[1] < 1.2,
            $"an RF well should pull the packet materially toward the axis, and it went "
            + $"to {confine.CentroidMm[1]:F4} mm from 1.5 mm");
    }

    /// <summary>
    /// Turning the drive off leaves the packet where it was, which is the control.
    /// </summary>
    /// <remarks>
    /// Without this, "the packet moved toward the axis" is equally consistent with
    /// diffusion smearing a distribution that was already near it. With the drive at zero
    /// there is no field at all — the electrodes hold no DC — so anything that moves the
    /// centroid is the RF.
    /// </remarks>
    [Fact]
    public void WithTheDriveOffThePacketStaysWhereItWas()
    {
        var quiet = Run(At(0));
        var driven = Run(At(400));

        Assert.NotEqual(
            driven.Phases[0].CentroidMm[1],
            quiet.Phases[0].CentroidMm[1]);

        output.WriteLine($"drive on:  y = {driven.Phases[0].CentroidMm[1]:F4} mm");
        output.WriteLine($"drive off: y = {quiet.Phases[0].CentroidMm[1]:F4} mm");

        // Undriven, nothing moves it: no DC anywhere, and diffusion is symmetric.
        Assert.Equal(1.5, quiet.Phases[0].CentroidMm[1], 0.15);

        // And the gap between them is the whole point - the drive has to do something
        // an order larger than the noise, or this measures nothing.
        Assert.True(
            quiet.Phases[0].CentroidMm[1] - driven.Phases[0].CentroidMm[1] > 0.2,
            "the drive must move the packet materially more than diffusion does");
    }
}
