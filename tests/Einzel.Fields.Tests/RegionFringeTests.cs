using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A bounded element may carry a fringe: its potential rises from nothing at the region's
/// face to full strength a declared distance inside, and the field is the gradient of that.
/// </summary>
/// <remarks>
/// <para>
/// A region with no fringe steps the potential at its face, which every bounded element has
/// reported since regions existed. For a confining RF the step is worse than an energy
/// bookkeeping problem: an ion approaching the hard edge of a pseudopotential well meets
/// the whole well at once, at whatever radius it arrives, and is held against it - the
/// front-end template's funnel delivered its packet to the tunnel entrance and 42 per cent
/// of it stopped in the last two millimetres before the RF began. A fringe squeezes the
/// packet toward the axis as the well grows under it, which is what a real electrode's
/// decaying field does.
/// </para>
/// <para>
/// Three things are asserted: the potential is continuous and reaches the element's own a
/// fringe inside; the field is the gradient of the fringed potential, checked by central
/// differences rather than against a formula written here; and no fringe is bit-identical
/// to what a bounded element always was.
/// </para>
/// </remarks>
public sealed class RegionFringeTests(ITestOutputHelper output)
{
    private static readonly IElectrostaticField Uniform = UniformField.Create(new Vec3(1000.0, 250.0, 0.0));

    private static FieldRegion Box(double fringeM) => new(-0.010, 0.010, -0.010, 0.010, -0.010, 0.010, fringeM);

    /// <summary>Zero at the face, the element's own a fringe inside, linear between.</summary>
    [Fact]
    public void ThePotentialRisesLinearlyThroughTheFringe()
    {
        var fringed = BoundedField.Around(Uniform, Box(0.003));

        var face = new Vec3(-0.010, 0.002, 0.001);
        var half = new Vec3(-0.0085, 0.002, 0.001);
        var deep = new Vec3(-0.007, 0.002, 0.001);
        var deeper = new Vec3(0.0, 0.002, 0.001);

        Assert.Equal(0.0, fringed.PotentialAt(in face));
        Assert.Equal(0.5 * Uniform.PotentialAt(in half), fringed.PotentialAt(in half), 1e-12);
        Assert.Equal(Uniform.PotentialAt(in deep), fringed.PotentialAt(in deep), 1e-12);
        Assert.Equal(Uniform.PotentialAt(in deeper), fringed.PotentialAt(in deeper));

        output.WriteLine($"face {fringed.PotentialAt(in face):F4} V, half way {fringed.PotentialAt(in half):F4} V, "
            + $"a fringe in {fringed.PotentialAt(in deep):F4} V against the element's {Uniform.PotentialAt(in deep):F4} V");
    }

    /// <summary>The field is minus the gradient of the fringed potential, through the fringe band too.</summary>
    [Fact]
    public void TheFieldIsTheGradientOfTheFringedPotential()
    {
        var fringed = BoundedField.Around(Uniform, Box(0.003));
        const double h = 1e-7;

        foreach (var point in new[]
        {
            new Vec3(-0.0092, 0.0031, -0.0012),   // in the x fringe band
            new Vec3(0.0011, 0.0084, 0.0),        // in the y fringe band
            new Vec3(-0.0004, -0.0007, -0.0091),  // in the z fringe band
            new Vec3(0.0021, -0.0013, 0.0007),    // deep inside
        })
        {
            var numeric = new Vec3(
                -(fringed.PotentialAt(point + new Vec3(h, 0.0, 0.0)) - fringed.PotentialAt(point - new Vec3(h, 0.0, 0.0))) / (2.0 * h),
                -(fringed.PotentialAt(point + new Vec3(0.0, h, 0.0)) - fringed.PotentialAt(point - new Vec3(0.0, h, 0.0))) / (2.0 * h),
                -(fringed.PotentialAt(point + new Vec3(0.0, 0.0, h)) - fringed.PotentialAt(point - new Vec3(0.0, 0.0, h))) / (2.0 * h));

            var analytic = fringed.ElectricFieldAt(in point);

            output.WriteLine($"({point.X * 1e3:F1}, {point.Y * 1e3:F1}, {point.Z * 1e3:F1}) mm: "
                + $"E = ({analytic.X:F3}, {analytic.Y:F3}, {analytic.Z:F3}) against differences ({numeric.X:F3}, {numeric.Y:F3}, {numeric.Z:F3}) V/m");

            Assert.Equal(numeric.X, analytic.X, 1e-6 * Math.Max(1.0, Math.Abs(numeric.X)));
            Assert.Equal(numeric.Y, analytic.Y, 1e-6 * Math.Max(1.0, Math.Abs(numeric.Y)));
            Assert.Equal(numeric.Z, analytic.Z, 1e-6 * Math.Max(1.0, Math.Abs(numeric.Z)));
        }
    }

    /// <summary>No fringe is what a bounded element always was, to the bit.</summary>
    [Fact]
    public void NoFringeIsUnchanged()
    {
        var hard = BoundedField.Around(Uniform, Box(0.0));
        var random = new Random(7);

        for (var k = 0; k < 200; k++)
        {
            var p = new Vec3(
                (random.NextDouble() - 0.5) * 0.024,
                (random.NextDouble() - 0.5) * 0.024,
                (random.NextDouble() - 0.5) * 0.024);

            var inside = Box(0.0).Contains(in p);

            Assert.Equal(inside ? Uniform.PotentialAt(in p) : 0.0, hard.PotentialAt(in p));
            Assert.Equal(inside ? Uniform.ElectricFieldAt(in p) : Vec3.Zero, hard.ElectricFieldAt(in p));
        }
    }

    /// <summary>A driven element scales the same way at every instant.</summary>
    [Fact]
    public void ADrivenElementIsScaledAtEveryInstant()
    {
        var quadrupole = IdealQuadrupoleRf.Create(
            Quantity.From(0.0, "V"), Quantity.From(100.0, "V"), Quantity.From(850.0, "kHz"), Quantity.From(4.0, "mm"),
            axis: CylinderAxis.X);

        var fringed = (ITimeVaryingField)BoundedField.Around(quadrupole, new FieldRegion(0.0, 0.050, -0.018, 0.018, -0.018, 0.018, 0.004));
        var point = new Vec3(0.001, 0.0024, 0.0);   // a quarter of the way into the x fringe

        foreach (var t in new[] { 0.0, 1.3e-7, 4.9e-7, 1.0e-6 })
        {
            Assert.Equal(0.25 * quadrupole.PotentialAt(in point, t), fringed.PotentialAt(in point, t), 1e-12);
        }

        // Deep inside the element is itself.
        var deep = new Vec3(0.020, 0.0024, 0.0);
        Assert.Equal(quadrupole.PotentialAt(in deep, 2.2e-7), fringed.PotentialAt(in deep, 2.2e-7));
        Assert.Equal(quadrupole.ElectricFieldAt(in deep, 2.2e-7), fringed.ElectricFieldAt(in deep, 2.2e-7));
    }

    private const string Head = """
        {
          "schemaVersion": "0.12",
          "name": "fringe",
          "ion": { "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 1, "unit": "V" }
          },
          "detector": { "planePoint": { "value": [50, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "maximumFlightTime": { "value": 1, "unit": "us" } },
          "fields": [
            { "type": "uniform", "field": { "value": [1000, 0, 0], "unit": "V/m" },
              "region": { "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
                          "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                          "minZ": { "value": -10, "unit": "mm" }, "maxZ": { "value": 10, "unit": "mm" },
                          "fringe": { "value": FRINGE, "unit": "mm" } } }
          ]
        }
        """;

    /// <summary>A document declares the fringe, and it reaches the compiled region and the built field.</summary>
    [Fact]
    public void ADocumentMayDeclareAFringe()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Head.Replace("FRINGE", "3", StringComparison.Ordinal)));

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(0.003, validation.Model!.Fields[0].Region!.FringeSi, 1e-15);

        var built = FieldAssembly.BuildReported(validation.Model);

        Assert.Equal(0.0, built.Field.PotentialAt(new Vec3(-0.010, 0.0, 0.0)));
        Assert.Contains(built.Warnings, w => w.Code == "field.region-fringe");
        Assert.DoesNotContain(built.Warnings, w => w.Code == "field.region-potential-step");

        output.WriteLine(string.Join("; ", built.Warnings.Select(w => w.Code)));
    }

    /// <summary>A fringe deeper than half the region's smallest extent is refused: the element would never be whole.</summary>
    [Fact]
    public void AFringeDeeperThanHalfTheRegionIsRefused()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Head.Replace("FRINGE", "12", StringComparison.Ordinal)));

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors, e => e.Path.EndsWith("/region/fringe", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Contains("half its smallest extent", error.Constraint, StringComparison.Ordinal);
    }
}
