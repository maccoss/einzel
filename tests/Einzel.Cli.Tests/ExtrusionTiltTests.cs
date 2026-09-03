using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A cross-section whose extrusion axis is tilted, declared from a document.
/// </summary>
/// <remarks>
/// The wiring rather than the arithmetic, which is the half that keeps breaking here.
/// <c>RotatedFieldTests</c> establishes that the rotation is exact; these establish that
/// a document can ask for it, that the field an ion actually flies through carries it,
/// and that the two ways of asking for nothing give the same nothing.
/// </remarks>
public sealed class ExtrusionTiltTests(ITestOutputHelper output)
{
    private static string Model(string tilt) => """
    {
      "schemaVersion": "0.8",
      "name": "tilted cross-section",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [10, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 1, "unit": "kV" }
      },
      "fields": [ { "type": "solved2d", "solve": {
        "minX": { "value": -5, "unit": "mm" }, "maxX": { "value": 65, "unit": "mm" },
        "minY": { "value": -15, "unit": "mm" }, "maxY": { "value": 15, "unit": "mm" },
        "cellSize": { "value": 0.5, "unit": "mm" },
        TILT
        "electrodes": [
          { "name": "backA", "shape": "rectangle",
            "minX": { "value": 40, "unit": "mm" }, "maxX": { "value": 60, "unit": "mm" },
            "minY": { "value": 6, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
            "potential": { "value": 2000, "unit": "V" } },
          { "name": "backB", "shape": "rectangle",
            "minX": { "value": 40, "unit": "mm" }, "maxX": { "value": 60, "unit": "mm" },
            "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": -6, "unit": "mm" },
            "potential": { "value": 2000, "unit": "V" } }
        ] } } ],
      "detector": {
        "planePoint": { "value": [10, 0, 0], "unit": "mm" },
        "normal": { "value": [1, 0, 0] }
      },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 50, "unit": "us" } }
    }
    """.Replace("TILT", tilt, StringComparison.Ordinal);

    private static IElectrostaticField Build(string tilt)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Model(tilt)));
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Code + " " + e.Constraint)));
        var (field, _) = FieldAssembly.BuildReported(validation.Model!);
        return field;
    }

    /// <summary>A declared tilt reaches the field as exactly that anisotropy.</summary>
    /// <remarks>
    /// Ez/Ex is compared against tan of the declared angle rather than against a number
    /// this engine produced, and the tolerance is 1e-9 because the rotation is exact -
    /// what is being checked is that the document's value arrived, not how well it solved.
    /// </remarks>
    [Fact]
    public void ADeclaredTiltReachesTheFieldAsTheAnisotropy()
    {
        const double HalfTurns = 0.004;
        var field = Build($"\"tiltHalfTurns\": {{ \"value\": {HalfTurns}, \"unit\": \"1\" }},");
        var straight = Build(string.Empty);
        var expected = -Math.Tan(double.Pi * HalfTurns);

        var worst = 0.0;
        var counted = 0;
        foreach (var x in new[] { 0.042, 0.048, 0.055 })
        {
            foreach (var z in new[] { -0.05, 0.0, 0.12 })
            {
                var p = new Vec3(x, 0.0, z);
                var e = field.ElectricFieldAt(in p);
                if (Math.Abs(e.X) < 100.0)
                {
                    continue;
                }

                counted++;
                worst = Math.Max(worst, Math.Abs((e.Z / e.X) - expected));
            }
        }

        // The control: without the tilt the same points carry no z field at all, so the
        // rows above cannot be passing on some pre-existing anisotropy of the solve.
        var mid = new Vec3(0.048, 0.0, 0.0);
        Assert.Equal(0.0, straight.ElectricFieldAt(in mid).Z, 1e-9);

        output.WriteLine($"declared {HalfTurns} half turns: Ez/Ex = {expected:E9} over {counted} points, worst error {worst:E3}");
        Assert.True(counted >= 6, $"only {counted} points were inside the field");
        Assert.True(worst < 1e-9, $"worst anisotropy error {worst:E3}");
    }

    /// <summary>Declaring a zero tilt is the same field as declaring none, to the bit.</summary>
    [Fact]
    public void AZeroTiltIsBitIdenticalToNoTilt()
    {
        var zero = Build("\"tiltHalfTurns\": { \"value\": 0, \"unit\": \"1\" },");
        var absent = Build(string.Empty);

        foreach (var x in new[] { 0.02, 0.045, 0.058 })
        {
            var p = new Vec3(x, 0.003, 0.09);
            Assert.Equal(absent.PotentialAt(in p), zero.PotentialAt(in p));
            Assert.Equal(absent.ElectricFieldAt(in p).Z, zero.ElectricFieldAt(in p).Z);
        }
    }

    /// <summary>An axisymmetric solve may not tilt its extrusion axis.</summary>
    /// <remarks>
    /// Refused rather than ignored: a half-plane stands for a body of revolution about x,
    /// and rotating the half-plane about y does not rotate that body, so the document is
    /// asking for something that does not exist. Silently honouring it would produce a
    /// field of no geometry at all.
    /// </remarks>
    [Fact]
    public void AnAxisymmetricSolveMayNotTiltItsExtrusionAxis()
    {
        var json = Model("\"tiltHalfTurns\": { \"value\": 0.004, \"unit\": \"1\" }, \"symmetry\": \"cylindrical\",");
        var validation = ModelValidator.Validate(ModelJson.Parse(json));

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors, e => e.Code == "TILT_NOT_AVAILABLE");
        output.WriteLine($"{error.Code} at {error.Path}: {error.Constraint}");
        Assert.Contains("tiltHalfTurns", error.Path, StringComparison.Ordinal);
    }
}
