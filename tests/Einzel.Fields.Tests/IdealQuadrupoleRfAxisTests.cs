using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The analytic RF quadrupole may lie across any axis, and across z it is what it always was.
/// </summary>
/// <remarks>
/// <para>
/// A mass filter's cross-section lies across z by convention and every document written
/// before the attribute existed says nothing, so the first assertion is that saying nothing
/// computes the same numbers to the bit. The reason the attribute exists is the other two:
/// every axisymmetric tunnel here is solved in a half-plane whose axis of rotation is x, and
/// a trapped-ion-mobility analyser confines with a quadrupole <em>across</em> that axis. The
/// quadrupole is not axisymmetric; its pseudopotential is, which is what lets it enter the
/// r-z density solve at all.
/// </para>
/// <para>
/// Checked as a permutation rather than against a formula - the field across x at (x, y, z)
/// must be the field across z at (y, z, x), component for component - because a formula
/// written here would be a second copy of the one under test.
/// </para>
/// </remarks>
public sealed class IdealQuadrupoleRfAxisTests(ITestOutputHelper output)
{
    private static readonly Quantity Direct = Quantity.From(3.0, "V");
    private static readonly Quantity Amplitude = Quantity.From(150.0, "V");
    private static readonly Quantity Frequency = Quantity.From(0.85, "MHz");
    private static readonly Quantity Radius = Quantity.From(4.0, "mm");

    private static readonly Vec3[] Points =
    [
        new(0.0012, -0.0007, 0.0021),
        new(-0.0031, 0.0016, -0.0004),
        new(0.0, 0.0, 0.0),
        new(0.0025, 0.0, 0.0),
        new(0.0, 0.0018, -0.0033),
    ];

    private static readonly double[] Instants = [0.0, 1.7e-7, 4.1e-7, 9.3e-7];

    /// <summary>Saying nothing is saying z, to the bit.</summary>
    [Fact]
    public void AnUndeclaredAxisIsZAndUnchanged()
    {
        var undeclared = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius);
        var declared = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius, axis: CylinderAxis.Z);

        Assert.Equal(CylinderAxis.Z, undeclared.Axis);

        foreach (var point in Points)
        {
            foreach (var t in Instants)
            {
                Assert.Equal(declared.PotentialAt(in point, t), undeclared.PotentialAt(in point, t));
                Assert.Equal(declared.ElectricFieldAt(in point, t), undeclared.ElectricFieldAt(in point, t));

                // And the closed form it has always been, so the permutation machinery
                // has not moved the z case: drive (x^2 - y^2) / r0^2.
                var drive = undeclared.DriveAt(t);
                var expected = drive * ((point.X * point.X) - (point.Y * point.Y)) / (0.004 * 0.004);
                Assert.Equal(expected, undeclared.PotentialAt(in point, t), 1e-12 * Math.Max(1.0, Math.Abs(expected)));
            }
        }
    }

    /// <summary>Across x, the field is the z field with the coordinates cycled once.</summary>
    [Fact]
    public void AcrossXIsTheCyclicPermutationOfAcrossZ()
    {
        var z = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius);
        var x = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius, axis: CylinderAxis.X);

        foreach (var point in Points)
        {
            var cycled = new Vec3(point.Y, point.Z, point.X);

            foreach (var t in Instants)
            {
                Assert.Equal(z.PotentialAt(in cycled, t), x.PotentialAt(in point, t));

                var ez = z.ElectricFieldAt(in cycled, t);
                var ex = x.ElectricFieldAt(in point, t);

                // Nothing along the axis, exactly - a permutation, not a rotation.
                Assert.Equal(0.0, ex.X);
                Assert.Equal(ez.X, ex.Y);
                Assert.Equal(ez.Y, ex.Z);
            }
        }

        var sample = x.ElectricFieldAt(new Vec3(0.0, 0.002, 0.0), 0.0);
        output.WriteLine($"across x, 2 mm off axis along y at t = 0: E = ({sample.X:F1}, {sample.Y:F1}, {sample.Z:F1}) V/m");
    }

    /// <summary>Across y, the remaining cyclic permutation.</summary>
    [Fact]
    public void AcrossYIsTheOtherCyclicPermutation()
    {
        var z = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius);
        var y = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius, axis: CylinderAxis.Y);

        foreach (var point in Points)
        {
            var cycled = new Vec3(point.Z, point.X, point.Y);

            foreach (var t in Instants)
            {
                Assert.Equal(z.PotentialAt(in cycled, t), y.PotentialAt(in point, t));

                var ez = z.ElectricFieldAt(in cycled, t);
                var ey = y.ElectricFieldAt(in point, t);

                Assert.Equal(0.0, ey.Y);
                Assert.Equal(ez.X, ey.Z);
                Assert.Equal(ez.Y, ey.X);
            }
        }
    }

    /// <summary>
    /// The pseudopotential of a quadrupole across x depends on the distance from the x axis
    /// alone, which is the property that lets it into an axisymmetric density solve.
    /// </summary>
    [Fact]
    public void TheFieldMagnitudeAcrossXDependsOnRadiusAlone()
    {
        var x = IdealQuadrupoleRf.Create(Direct, Amplitude, Frequency, Radius, axis: CylinderAxis.X);

        const double R = 0.0015;
        var reference = x.ElectricFieldAt(new Vec3(0.01, R, 0.0), 0.0).LengthSquared;

        for (var k = 1; k < 12; k++)
        {
            var theta = 2.0 * Math.PI * k / 12;
            var point = new Vec3(-0.02 + (0.003 * k), R * Math.Cos(theta), R * Math.Sin(theta));
            var here = x.ElectricFieldAt(in point, 0.0).LengthSquared;

            Assert.Equal(reference, here, reference * 1e-12);
        }

        output.WriteLine($"|E|^2 at r = 1.5 mm, twelve azimuths and axial positions: {reference:E6} (V/m)^2 throughout");
    }

    private const string Head = """
        {
          "schemaVersion": "0.11",
          "name": "axis",
          "ion": { "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 1, "unit": "V" }
          },
          "detector": { "planePoint": { "value": [50, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "maximumFlightTime": { "value": 1, "unit": "us" } },
          "fields": [
        """;

    private const string Tail = """
          ]
        }
        """;

    /// <summary>A document declares the axis and the compiled field carries it.</summary>
    [Fact]
    public void ADocumentMayDeclareTheAxis()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Head + """
            { "type": "idealQuadrupoleRf", "axis": "x",
              "directPotential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "value": 100, "unit": "V" },
              "driveFrequency": { "value": 850, "unit": "kHz" },
              "inscribedRadius": { "value": 4, "unit": "mm" } }
            """ + Tail));

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        Assert.Equal(CylinderAxis.X, validation.Model!.Fields[0].Axis);

        // And through the assembly, the built field lies across x.
        var built = FieldAssembly.BuildReported(validation.Model).Field;
        var onAxis = built.ElectricFieldAt(new Vec3(0.01, 0.0, 0.0));
        var offAxis = built.ElectricFieldAt(new Vec3(0.01, 0.002, 0.0));

        Assert.Equal(0.0, onAxis.Length, 1e-9);
        Assert.Equal(0.0, offAxis.X);
        Assert.NotEqual(0.0, offAxis.Y);
    }

    /// <summary>An axis that is not x, y or z is refused, naming the path.</summary>
    [Fact]
    public void AnUnknownAxisIsRefused()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Head + """
            { "type": "idealQuadrupoleRf", "axis": "r",
              "directPotential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "value": 100, "unit": "V" },
              "driveFrequency": { "value": 850, "unit": "kHz" },
              "inscribedRadius": { "value": 4, "unit": "mm" } }
            """ + Tail));

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors, e => e.Path.EndsWith("/axis", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Contains("'x', 'y' or 'z'", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// An axis on an element that has no use for one is refused rather than ignored - a
    /// property read by nothing is the shape of a silent wrong model.
    /// </summary>
    [Fact]
    public void AnAxisOnAnotherElementIsRefused()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Head + """
            { "type": "uniform", "axis": "x", "field": { "value": [1000, 0, 0], "unit": "V/m" } }
            """ + Tail));

        Assert.False(validation.IsValid);
        var error = Assert.Single(validation.Errors, e => e.Path.EndsWith("/axis", StringComparison.Ordinal));
        output.WriteLine($"{error.Code} {error.Path}: {error.Constraint}");
        Assert.Contains("only an idealQuadrupoleRf", error.Constraint, StringComparison.Ordinal);
    }
}
