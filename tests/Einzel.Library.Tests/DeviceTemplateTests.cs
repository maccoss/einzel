using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// LIB-1, tested where it can actually fail: two devices that share no code, only
/// the schema.
/// </summary>
public sealed class DeviceTemplateTests(ITestOutputHelper output)
{
    private static CompiledModel Compile(
        string template, IReadOnlyDictionary<string, Quantity>? overrides = null)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read(template));
        var validation = ModelValidator.Validate(document, overrides);

        Assert.True(
            validation.IsValid,
            $"{template} failed to validate: {string.Join("; ", validation.Errors.Select(e => e.ToString()))}");

        return validation.Model!;
    }

    [Fact]
    public void EveryShippedTemplateValidates()
    {
        var names = DeviceTemplates.Names();
        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            var model = Compile(name);
            output.WriteLine($"{name}: {model.Parameters.Parameters.Count} parameters, {model.Fields.Count} field(s)");
        }
    }

    [Fact]
    public void ATemplateDeclaresAParameterSurfaceWithBounds()
    {
        // The half of LIB-1 that is not the geometry: what a caller may vary, and
        // over what range, declared by the template rather than restated by every
        // study that uses it.
        var model = Compile("planar-mirror-pair");
        var free = model.Parameters.FreeParameters;

        Assert.Contains(free, p => p.Name == "capToCap" && p.Minimum is not null && p.Maximum is not null);
        Assert.Contains(free, p => p.Name == "mirrorDepth" && p.Description is not null);

        // Derived parameters are not free: an optimiser must not be handed a knob
        // that is really a consequence of another knob.
        Assert.DoesNotContain(free, p => p.Name == "midPlane");
        Assert.True(model.Parameters.Parameters["midPlane"].IsDerived);
    }

    [Fact]
    public void DerivedParametersFollowAnOverriddenOne()
    {
        // The property that makes a sweep meaningful: perturb one parameter and
        // everything expressed in terms of it moves too.
        var nominal = Compile("planar-mirror-pair");
        var widened = Compile("planar-mirror-pair", new Dictionary<string, Quantity>(StringComparer.Ordinal)
        {
            ["capToCap"] = Quantity.From(900.0, "mm"),
        });

        Assert.Equal(0.3835, nominal.Parameters["midPlane"].In("m"), 1e-6);
        Assert.Equal(0.45, widened.Parameters["midPlane"].In("m"), 1e-9);
    }

    [Fact]
    public void AQuadrupoleIsADocument()
    {
        // Spec section 21 phase 5 asks for "a second, unrelated instrument". This
        // is that test in miniature: nothing about a quadrupole exists anywhere in
        // the codebase. It is four discs and a box in a JSON file.
        var model = Compile("quadrupole");
        var field = FieldAssembly.Build(model);

        var r0 = model.Parameters["inscribedRadius"].In("m");
        var applied = model.Parameters["rodPotential"].In("V");

        output.WriteLine($"r0 = {r0 * 1e3:F2} mm, rods at {applied:F0} V");
        output.WriteLine("   r (mm)     phi(x) (V)    phi(y) (V)     Ex/x (V/m^2)    Ey/y (V/m^2)");

        var ratios = new List<double>();

        for (var fraction = 0.1; fraction <= 0.45; fraction += 0.05)
        {
            var r = fraction * r0;
            var onX = new Vec3(r, 0.0, 0.0);
            var onY = new Vec3(0.0, r, 0.0);

            var phiX = field.PotentialAt(in onX);
            var phiY = field.PotentialAt(in onY);
            var ex = field.ElectricFieldAt(in onX).X / r;
            var ey = field.ElectricFieldAt(in onY).Y / r;

            output.WriteLine(
                $"{r * 1e3,9:F3}   {phiX,11:F4}   {phiY,11:F4}   {ex,14:E4}   {ey,14:E4}");

            ratios.Add(ex);

            // The defining property of a quadrupole: the potential is odd under
            // exchanging x and y, so a point on one axis sees the negative of the
            // matching point on the other.
            Assert.Equal(-phiY, phiX, Math.Abs(phiX) * 0.02);
        }

        // And the restoring force is linear, which is what E/r being constant
        // means. This is what makes a quadrupole a mass filter once the potential
        // is made to oscillate — it is the premise of the Mathieu equation.
        var mean = ratios.Average();
        var spread = ratios.Max() - ratios.Min();

        output.WriteLine(string.Empty);
        output.WriteLine($"Ex/x = {mean:E4} V/m^2, spread {spread / Math.Abs(mean):P2} across the central 45% of r0");

        Assert.True(
            spread / Math.Abs(mean) < 0.05,
            $"the field should be linear in displacement near the axis, but Ex/x varies by "
            + $"{spread / Math.Abs(mean):P1} over the sampled range");

        // Magnitude check against the ideal hyperbolic field, phi = V (x^2 - y^2) / r0^2,
        // whose gradient gives Ex/x = -2V/r0^2. Round rods approximate that to a
        // few percent, which is the whole reason 1.1468 is the classical ratio.
        var ideal = -2.0 * applied / (r0 * r0);
        output.WriteLine($"ideal hyperbolic Ex/x = {ideal:E4} V/m^2, ratio {mean / ideal:F4}");

        Assert.InRange(mean / ideal, 0.9, 1.1);
    }

    [Fact]
    public void TheMirrorTemplateBuildsAFieldThatTurnsAnIon()
    {
        var model = Compile("planar-mirror-pair");
        var field = FieldAssembly.Build(model);

        var capToCap = model.Parameters["capToCap"].In("m");
        var energy = model.Parameters["ionEnergy"].In("V");

        // Field-free in the middle, retarding at both ends, and symmetric: the
        // reflection is doing its job.
        var middle = new Vec3(capToCap / 2.0, 0.0, 0.0);
        var nearFirst = new Vec3(-0.02, 0.0, 0.0);
        var nearSecond = new Vec3(capToCap + 0.02, 0.0, 0.0);

        Assert.Equal(0.0, field.ElectricFieldAt(in middle).X, 1.0);
        Assert.True(field.ElectricFieldAt(in nearFirst).X > 0.0, "the first mirror should push back toward +x");
        Assert.True(field.ElectricFieldAt(in nearSecond).X < 0.0, "the second mirror should push back toward -x");

        Assert.Equal(
            field.PotentialAt(in nearFirst), field.PotentialAt(in nearSecond),
            Math.Abs(field.PotentialAt(in nearFirst)) * 1e-6);

        // And it is deep enough to turn the ion it was designed for.
        var deep = new Vec3(-model.Parameters["mirrorDepth"].In("m") * 0.999, 0.0, 0.0);

        Assert.True(
            field.PotentialAt(in deep) > energy,
            $"the cap reaches only {field.PotentialAt(in deep):F0} V on the mid-plane, "
            + $"which will not turn a {energy:F0} eV ion");
    }
}
