using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Io.Tests;

/// <summary>
/// Three things the model format could not say until a trap needed them.
/// </summary>
/// <remarks>
/// Each was found by writing the rectilinear-trap template rather than by reading
/// the schema, which is the point of spec section 21 phase 5: a second, unrelated
/// instrument is what tests whether the format is general or merely fits the
/// device it was written beside. All three are device-independent and none of them
/// required a change inside <c>Einzel.Library</c>, which is LIB-1 holding.
/// </remarks>
public sealed class TrapSourceTests
{
    private const string Trap =
        """
        {
          "schemaVersion": "0.3",
          "name": "t",
          "parameters": {
            "gap": { "value": 4, "unit": "mm" },
            "drift": { "expression": "gap * 3", "unit": "mm" }
          },
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 0, "unit": "V" }
          },
          "fields": [{
            "type": "uniform",
            "field": { "value": [100000, 0, 0], "unit": "V/m" }
          }],
          "detector": {
            "planePoint": { "expression": ["drift", "0", "0"], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
        }
        """;

    private static ModelValidation Validate(string json) =>
        ModelValidator.Validate(ModelJson.Parse(json));

    [Fact]
    public void APacketMayStartAtRestWhenAFieldCanAccelerateIt()
    {
        // A pulsed extraction trap holds its packet at rest and then switches a
        // field on. That is the entire mechanism, and until this it could not be
        // written down: the source was required to carry its own energy, which is
        // an assumption about beams that a trap does not meet.
        var validation = Validate(Trap);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(0.0, validation.Model!.AccelerationPotentialSi);
        Assert.Equal(0.0, validation.Model!.LaunchSpeedSi());
    }

    [Fact]
    public void APacketAtRestInAnEmptyModelIsStillRefused()
    {
        // The check is narrowed, not removed. With nothing that could move the ion,
        // a zero accelerating potential really does mean it sits there, and that is
        // worth an error rather than a run that times out.
        var validation = Validate(Trap.Replace(
            """
            {
                "type": "uniform",
                "field": { "value": [100000, 0, 0], "unit": "V/m" }
              }
            """.Trim(),
            """{ "type": "fieldFree" }""",
            StringComparison.Ordinal));

        Assert.False(validation.IsValid);

        var error = Assert.Single(
            validation.Errors,
            e => string.Equals(e.Path, "/source/accelerationPotential", StringComparison.Ordinal));

        // AGT-3: the error says what to do about it, and one of the two things to
        // do is the one a trap author wants.
        Assert.Contains("accelerates the ion from rest", error.Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void AVectorPlacementMayBeParametric()
    {
        // Spec section 9 says every placement is a parametric expression rather
        // than a baked number, and scalars have always been. Vectors were not, so
        // any device whose detector is not at the origin had to bake coordinates -
        // which both shipped templates did.
        var model = Validate(Trap).Model!;

        // gap * 3 = 12 mm, on axis in the other two.
        Assert.Equal(0.012, model.DetectorPoint.X, 1e-15);
        Assert.Equal(0.0, model.DetectorPoint.Y);
        Assert.Equal(0.0, model.DetectorPoint.Z);
    }

    [Fact]
    public void AVectorPlacementFollowsTheParameterItIsDerivedFrom()
    {
        // The reason it must be parametric at all: perturb the parameter and the
        // placement moves with it. Bake it and "widen the gap and re-solve" stops
        // being sayable, which is what the whole sweep machinery rests on.
        var document = ModelJson.Parse(Trap);

        var widened = document with
        {
            Parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal)
            {
                ["gap"] = document.Parameters!["gap"] with { Value = 5.0 },
            },
        };

        Assert.Equal(0.015, ModelValidator.Validate(widened).Model!.DetectorPoint.X, 1e-15);
    }

    [Fact]
    public void ADimensionlessZeroIsAcceptedButADimensionlessOneIsNot()
    {
        // The grammar has no unit literals, so a bare number in an expression is
        // dimensionless and there is no other way to write "on axis". Zero is the
        // one value where that is safe, because it is the only one whose unit
        // conversion is the identity - and the ambiguity that makes units mandatory
        // here, is 4000 volts or kilovolts, does not exist at zero.
        var wrong = Validate(Trap.Replace(
            """["drift", "0", "0"]""", """["drift", "1", "0"]""", StringComparison.Ordinal));

        Assert.False(wrong.IsValid);

        Assert.Contains(
            wrong.Errors,
            e => e.Path!.StartsWith("/detector/planePoint/expression/1", StringComparison.Ordinal));
    }
}
