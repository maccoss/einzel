using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// Dumps the solved on-axis axial field of the analyser tunnel, so a figure can be drawn from
/// the solve rather than sketched over it.
/// </summary>
/// <remarks>
/// <para>
/// A drawing whose caption says "from the solved field" has to be from the solved field. A
/// hand-drawn curve anchored at two measured points is a schematic, and calling it a plot is
/// the kind of small overclaim that survives into places where it is relied on.
/// </para>
/// <para>
/// This is a dump rather than a test: it asserts only the two things a reader of the figure
/// would be misled by if they were wrong - that the profile has one interior maximum, and
/// that it lies where the template's own study measured it.
/// </para>
/// </remarks>
public sealed class TimsFieldProfileDump(ITestOutputHelper output)
{
    [Fact]
    public void OnAxisFieldAlongTheTunnel()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read("tims-analyzer")));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        var model = validation.Model!;
        var built = FieldAssembly.BuildReported(model);

        var parameters = model.Parameters.Parameters;
        var tunnelLength = parameters["tunnelLength"].Value.SiValue;

        // Sampled from the tunnel entrance to its exit, on the axis, at half-millimetre steps.
        var samples = new List<(double Mm, double VoltsPerMetre)>();

        var lengthMm = tunnelLength * 1e3;

        // The exit is sampled explicitly. A fixed 0.5 mm step stops at the last half-millimetre
        // BEFORE it - 46.5 of 46.6 - so a profile drawn from this would stop short of the
        // tunnel it is drawn against, by an amount that depends on the length rather than on
        // anything physical. Raised by review.
        for (var mm = 0.0; mm < lengthMm; mm += 0.5)
        {
            var e = built.Field.ElectricFieldAt(new Vec3(mm * 1e-3, 0.0, 0.0));
            samples.Add((mm, Math.Abs(e.X)));
        }

        var atExit = built.Field.ElectricFieldAt(new Vec3(tunnelLength, 0.0, 0.0));

        samples.Add((lengthMm, Math.Abs(atExit.X)));

        var peak = samples.MaxBy(s => s.VoltsPerMetre);
        var most = peak.VoltsPerMetre;

        output.WriteLine($"tunnel {tunnelLength * 1e3:F1} mm, {samples.Count} samples, "
            + $"peak {most:F1} V/m at {peak.Mm:F1} mm");
        output.WriteLine("");
        output.WriteLine("mm,V_per_m,normalised");

        foreach (var (mm, v) in samples)
        {
            output.WriteLine($"{mm:F1},{v:F2},{v / most:F4}");
        }

        // One interior maximum, and it is where the template's own study puts it. Anything
        // else and the figure would be drawing a different device.
        Assert.True(peak.Mm > 1.0 && peak.Mm < (tunnelLength * 1e3) - 1.0,
            $"the field peaks at {peak.Mm:F1} mm, which is at an end rather than inside the tunnel");

        Assert.Equal(41.2, peak.Mm, 1.0);
    }
}
