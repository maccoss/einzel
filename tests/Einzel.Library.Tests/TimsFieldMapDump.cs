using System.Text.Json.Nodes;

using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The solved potential through the ring stack, on a grid, and how much of the ring-to-ring
/// ripple survives to the axis.
/// </summary>
/// <remarks>
/// <para>
/// A stack of discrete rings cannot produce on its axis the potential its electrodes are held
/// at: between one ring and the next the axis sees a weighted average, and the further from
/// the wall the smoother it gets. That smoothing is the whole reason a ring stack works as a
/// field-gradient device at all, and it is what the closed-form comparison in
/// <c>TimsAnalyzerTests</c> is measuring the size of.
/// </para>
/// <para>
/// This dumps the potential over the bore so it can be drawn, and the ripple amplitude at
/// three radii so the smoothing can be stated as a number rather than left to the eye.
/// </para>
/// </remarks>
public sealed class TimsFieldMapDump(ITestOutputHelper output)
{
    [Fact]
    public void PotentialAcrossTheBore()
    {
        // The drive is taken to zero, and that is not a convenience.
        //
        // `PotentialAt` is the time-FREE interface, and a driven field answers it at an
        // arbitrary instant - so mapping the shipped template gives the DC gradient plus a
        // snapshot of the 100 V quadrupole, which climbs to 138 V at the bore wall and swamps
        // the 57 V the tunnel is actually separating with. That is the same defect this
        // project has recorded six times in the engine, met a seventh time in a measurement.
        //
        // The two fields do different jobs and are separable: the DC gradient is what holds
        // ions against the gas and is what this maps, and the RF is a radial well that a slow
        // ion feels only as a cycle average. The confinement is measured in
        // `TimsConfinementTests`, against its own closed form.
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-analyzer"))!;

        document["parameters"]!["rfAmplitude"]!["value"] = 0.0;

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        var model = validation.Model!;
        var built = FieldAssembly.BuildReported(model);

        var parameters = model.Parameters.Parameters;
        var lengthMm = parameters["tunnelLength"].Value.SiValue * 1e3;
        var boreMm = parameters["boreRadius"].Value.SiValue * 1e3;
        var pitchMm = parameters["ringPitch"].Value.SiValue * 1e3;

        double Phi(double xMm, double yMm) =>
            built.Field.PotentialAt(new Vec3(xMm * 1e-3, yMm * 1e-3, 0.0));

        output.WriteLine($"tunnel {lengthMm:F1} mm, bore radius {boreMm:F1} mm, ring pitch {pitchMm:F3} mm");

        // ── the map, for drawing ────────────────────────────────────────────
        // Sampled finely enough along x that the ring pitch is resolved: 0.25 mm is
        // seven samples per ring.
        output.WriteLine("");
        output.WriteLine("MAP mm,y_mm,phi_V");

        // 24 rows across the bore resolves the near-wall structure, which decays with a
        // scale of pitch/2pi = 0.27 mm - so half-millimetre rows would alias exactly the
        // feature this is dumped to show.
        for (var yi = 0; yi <= 24; yi++)
        {
            var y = boreMm * yi / 24.0;

            for (var x = 0.0; x <= lengthMm + 1e-9; x += 0.2)
            {
                output.WriteLine($"MAP {x:F2},{y:F4},{Phi(x, y):F5}");
            }
        }

        // ── how much ripple survives to each radius ─────────────────────────
        // Against a moving average over exactly ONE ring pitch, which removes anything
        // periodic at the pitch and leaves the trend.
        //
        // A first version used a second difference over the pitch and reported 0.166 V of
        // "ripple" on the axis. That is not ripple: the potential goes as z^2 BY DESIGN, so
        // the field is linear in position, and a second difference of a quadratic returns its
        // curvature. Predicted from the design alone, 0.156 V - which is what it measured.
        // A trend-removing metric has to remove the trend the device is built around.
        output.WriteLine("");
        output.WriteLine("RIPPLE y_mm,y_over_bore,peak_to_peak_V,fraction_of_span,ppm_of_span");

        var span = Math.Abs(Phi(lengthMm, 0.0) - Phi(0.0, 0.0));

        foreach (var y in new[] { 0.0, boreMm * 0.25, boreMm * 0.5, boreMm * 0.75, boreMm * 0.95 })
        {
            var ripple = Ripple(y);

            output.WriteLine($"RIPPLE {y:F3},{y / boreMm:F2},{ripple:G4},{ripple / span:G4},"
                + $"{ripple / span * 1e6:F0}");
        }

        var atAxis = Ripple(0.0);
        var atWall = Ripple(boreMm * 0.95);

        output.WriteLine("");
        output.WriteLine($"span {span:F2} V; ripple {atAxis:G3} V on the axis against {atWall:G3} V at "
            + $"0.95 of the bore - {atWall / Math.Max(atAxis, 1e-12):F0}x");

        // The axis is smoother than the wall. That is the property the device depends on: an
        // ion near the axis sees a gradient, not a staircase.
        Assert.True(
            atWall > 5.0 * atAxis,
            $"the ripple is {atWall:G3} V at the wall against {atAxis:G3} V on the axis, which is not "
            + "the smoothing a ring stack depends on");

        /// <summary>Peak-to-peak departure from a one-pitch moving average, at a radius.</summary>
        double Ripple(double y)
        {
            const int Within = 24;

            double Smooth(double x)
            {
                var sum = 0.0;

                for (var k = 0; k < Within; k++)
                {
                    sum += Phi(x + (pitchMm * ((k + 0.5) / Within - 0.5)), y);
                }

                return sum / Within;
            }

            double lowest = double.MaxValue, highest = double.MinValue;

            for (var x = pitchMm * 3.0; x <= lengthMm - (pitchMm * 3.0); x += 0.05)
            {
                var d = Phi(x, y) - Smooth(x);
                lowest = Math.Min(lowest, d);
                highest = Math.Max(highest, d);
            }

            return highest - lowest;
        }
    }
}
