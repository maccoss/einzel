using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// An analytic field's own strength can be ramped by a sequence, which is how an orbital
/// analyser captures.
/// </summary>
/// <remarks>
/// <para>
/// A real orbital trap does not simply switch on around an ion that is already in the right
/// orbit — it takes a packet injected at a large radius and <b>ramps its voltage</b>, which
/// squeezes the orbit inward and captures. Working out what a single document flying an ion
/// from a C-trap into an analyser still needs, this looked like the missing piece.
/// </para>
/// <para>
/// <b>It is not missing.</b> A quadro-logarithmic field's <c>curvature</c> is an expression
/// over the parameter surface, and a phase sets parameters — so the ramp is already
/// expressible, and it demonstrably drives the ion. What remains untested is whether a
/// capture can be <i>tuned</i>: matching injection radius, energy and timing to the ramp is
/// a study, not a feature.
/// </para>
/// <para>
/// A discrete ramp rather than a continuous one, since a phase holds its values for its
/// duration. More phases approximate a smoother ramp; nothing here says how many are
/// enough, and that is part of the same untested study.
/// </para>
/// </remarks>
public sealed class RampedAnalyticFieldTests(ITestOutputHelper output)
{
    /// <summary>Three phases, optionally all at the same curvature.</summary>
    private static string Document(double second, double third) =>
        $$"""
        {
          "schemaVersion": "0.7",
          "name": "ramped-orbital-trap",
          "parameters": {
            "curvature": { "value": 5.0, "unit": "V/mm^2", "minimum": 0.1, "maximum": 100.0 }
          },
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 10, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 100, "unit": "V" }
          },
          "sequence": [
            {
              "duration": { "value": 2, "unit": "us" },
              "set": { "curvature": { "value": 5.0, "unit": "V/mm^2" } }
            },
            {
              "duration": { "value": 2, "unit": "us" },
              "set": { "curvature": { "value": {{second}}, "unit": "V/mm^2" } }
            },
            {
              "duration": { "value": 2, "unit": "us" },
              "set": { "curvature": { "value": {{third}}, "unit": "V/mm^2" } }
            }
          ],
          "fields": [
            {
              "type": "quadroLogarithmic",
              "curvature": { "expression": "curvature", "unit": "V/mm^2" },
              "characteristicRadius": { "value": 20, "unit": "mm" },
              "centre": { "value": [0, 0, 0], "unit": "mm" }
            }
          ],
          "detector": {
            "planePoint": { "value": [0, 0, 60], "unit": "mm" },
            "normal": { "value": [0, 0, -1] }
          },
          "transport": {
            "maximumFlightTime": { "value": 6, "unit": "us" },
            "relativeTolerance": 1e-10
          }
        }
        """;

    private static TrajectoryResult Fly(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        var model = validation.Model!;
        var field = FieldAssembly.Build(model);

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        return TrajectoryIntegrator.Integrate(
            launch,
            IonSpecies.FromModel(model),
            field,
            new IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal));
    }

    /// <summary>Ramping the curvature changes the orbit; holding it does not.</summary>
    /// <remarks>
    /// <b>The flat sequence is the control and it is the whole test.</b> A ramped run on its
    /// own proves only that a model with a sequence in it runs — a sequence that was
    /// silently ignored would produce exactly the same trajectory and look just as
    /// convincing. The two documents differ in two numbers and nothing else.
    /// </remarks>
    [Fact]
    public void RampingAnAnalyticFieldsOwnStrengthChangesTheOrbit()
    {
        var ramped = Fly(Document(second: 12.0, third: 20.0));
        var flat = Fly(Document(second: 5.0, third: 5.0));

        var separation = Math.Sqrt(
            Vec3.Dot(
                ramped.FinalState.Position - flat.FinalState.Position,
                ramped.FinalState.Position - flat.FinalState.Position));

        output.WriteLine(
            $"ramped  final {ramped.FinalState.Position.Y * 1e3,8:F3}, "
            + $"{ramped.FinalState.Position.Z * 1e3,8:F3} mm, {ramped.AcceptedSteps} steps");

        output.WriteLine(
            $"flat    final {flat.FinalState.Position.Y * 1e3,8:F3}, "
            + $"{flat.FinalState.Position.Z * 1e3,8:F3} mm, {flat.AcceptedSteps} steps");

        output.WriteLine($"separation {separation * 1e3:F3} mm");

        // Both stay in the trap for the whole hold, which is what an orbital field does.
        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, ramped.Outcome);
        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, flat.Outcome);

        // And the ramp is doing the work: millimetres apart after six microseconds, from
        // two documents that differ in two numbers.
        Assert.True(
            separation > 1.0e-3,
            $"the ramped and flat sequences ended {separation * 1e3:F4} mm apart, which is "
            + "too close to say the sequence reached the analytic field at all");

        // A stiffer well is a faster orbit, so it costs more steps. Not a tolerance, a
        // direction: this is the sign that says which way the ramp went.
        Assert.True(
            ramped.AcceptedSteps > flat.AcceptedSteps,
            $"ramping the curvature up took {ramped.AcceptedSteps} steps against "
            + $"{flat.AcceptedSteps} held flat, and a stiffer well should cost more");
    }
}
