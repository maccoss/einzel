using System.Text.Json;

using Einzel.Commands;
using Einzel.Core.Model;

namespace Einzel.Cli.Tests;

/// <summary>
/// That a declared gas takes part in a figure of merit, as it does in a run.
/// </summary>
/// <remarks>
/// <para>
/// It did not. Every figure reached through the single-ion path flew in vacuum however
/// much gas the document declared, because the setup that built the launch, the field
/// and the detector never built a collision sampler. So <c>einzel run</c> and
/// <c>einzel test</c> gave different answers for the same model: 4904.4862 us against
/// 5000, on the corpus example whose entire subject is a gas carrying an ion.
/// </para>
/// <para>
/// <b>That example could not have caught it</b>, which is why this test exists
/// separately. It launches its ion at exactly the gas velocity so that the transit is
/// <c>L/u</c> by arithmetic — and in vacuum an ion launched at 200 m/s covers a metre in
/// exactly 5000 us too. The vacuum answer is not merely close to the expected one, it
/// <em>is</em> the expected one, and closer to it than the physical answer. A test whose
/// two branches agree cannot distinguish them however tight its tolerance.
/// </para>
/// <para>
/// This is the same shape as a defect already recorded here: <c>run</c> and <c>test</c>
/// computing the same flight time two ways and disagreeing, which was fixed by
/// collapsing them to one implementation. It came back for the gas.
/// </para>
/// </remarks>
public sealed class FigureOfMeritGasTests
{
    /// <summary>A dense gas changes the flight time a figure of merit reports.</summary>
    /// <remarks>
    /// Deliberately not the corpus geometry. The ion is launched fast into a still,
    /// dense gas, so collisions damp it and the transit lengthens by far more than any
    /// tolerance — the two branches have to be unable to agree by accident.
    /// </remarks>
    [Fact]
    public void ADeclaredGasChangesTheFlightTime()
    {
        var vacuum = Evaluate(Tube(withGas: false));
        var gassy = Evaluate(Tube(withGas: true));

        Assert.NotNull(vacuum);

        // The vacuum ion coasts: 1 V gives m/z 500 a speed of 621.24 m/s, so 40 mm
        // takes 64.387 us and nothing in the model can change it. In seconds, because
        // an evaluator returns SI and the catalogue unit is applied above it.
        Assert.Equal(64.387e-6, vacuum.Value, 1e-8);

        // With gas it is damped hard enough that it does not arrive at all inside the
        // flight-time ceiling, which is the least ambiguous form the difference can
        // take: a figure that ignored the gas would return the vacuum number.
        Assert.True(
            gassy is null || gassy.Value > 1.5 * vacuum.Value,
            $"the gas should change the answer: vacuum {vacuum.Value * 1e6:F3} us against "
            + $"{(gassy is null ? "no arrival" : (gassy.Value * 1e6).ToString("F3"))}");
    }

    /// <summary>Two evaluations of one model give the same flight, not two draws.</summary>
    /// <remarks>
    /// The sampler is seeded from the document, so a gas does not make a figure
    /// stochastic between calls. Without this a project test in a gas would be flaky
    /// and the flakiness would read as a regression in whatever it measured.
    /// </para>
    /// <para>
    /// A thin gas on purpose - about two collisions over the flight - so the ion still
    /// arrives and there is a number to compare. The point is reproducibility, not
    /// magnitude.
    /// </remarks>
    [Fact]
    public void TwoEvaluationsOfOneModelAgree()
    {
        var model = Compile(Tube(withGas: true, pressureMbar: 0.001));

        var figure = FiguresOfMerit.Evaluator("flightTime")(model);

        Assert.NotNull(figure);

        // Re-evaluating must give the same number: the sampler is seeded from the
        // document, so two evaluations of one model are the same flight and not two
        // draws from a distribution. A figure that reseeded per call would make every
        // project test flaky in a gas.
        var again = FiguresOfMerit.Evaluator("flightTime")(model);

        Assert.Equal(figure.Value, again!.Value, 1e-12);
    }

    private static double? Evaluate(string json) =>
        FiguresOfMerit.Evaluator("flightTime")(Compile(json));

    private static CompiledModel Compile(string json)
    {
        var document = JsonSerializer.Deserialize<ModelDocument>(json, Io.ModelJson.Options)!;
        var (model, errors) = ModelValidator.Validate(document);

        Assert.True(
            errors.Count == 0,
            string.Join("; ", errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return model!;
    }

    private static string Tube(bool withGas, double pressureMbar = 1.0)
    {
        var gas = !withGas
            ? string.Empty
            : $$"""
            ,
            "gas": {
              "model": "hardSphere",
              "pressure": { "value": {{pressureMbar}}, "unit": "mbar" },
              "temperature": { "value": 300.0, "unit": "K" },
              "mass": { "value": 28.0134, "unit": "Da" },
              "crossSection": { "value": 2.5e-18, "unit": "m^2" }
            }
            """;

        return $$"""
        {
          "schemaVersion": "0.5",
          "name": "gas-visibility",
          "ion": { "massToCharge": { "value": 500.0, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0.0, 0.0, 0.0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 1.0, "unit": "V" }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [40.0, 0.0, 0.0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "relativeTolerance": 1e-09,
            "maximumFlightTime": { "value": 500.0, "unit": "us" }{{gas}}
          }
        }
        """;
    }
}
