using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The trapped-ion-mobility analyser tunnel: an ion is held where the electric force
/// balances the drag of a counterflowing gas, so each mobility parks at its own position.
/// </summary>
/// <remarks>
/// <para>
/// That balance is the whole device. Ridgeway writes it as <c>v_d + v_g = 0</c> and the
/// elution relation <c>E_e = v_g / K</c> follows from it directly, so a model that parks a
/// density at the right place has reproduced the separating principle rather than a number.
/// </para>
/// <para>
/// <b>The two questions are separated deliberately.</b> Whether the ring stack produces the
/// field the ring potentials imply is a question about the <em>geometry</em>, and it is
/// asked here against the closed form. Whether the density settles where that field balances
/// the gas is a question about the <em>physics</em>, and it is asked against the solved
/// field's own balance point rather than against the closed form - so a discretisation error
/// in the first cannot be absorbed into the second, or hide there.
/// </para>
/// <para>
/// This stage carries no RF, so nothing confines the density radially and about 99 per cent
/// of it reaches the bore wall over three milliseconds. That does not move the parking point,
/// which is an axial balance - and Hernandez is explicit that the elution voltage is
/// independent of the ramp while the peak <em>width</em> is what the radial confinement sets.
/// </para>
/// </remarks>
public sealed class TimsAnalyzerTests(ITestOutputHelper output)
{
    /// <summary>Mobility the shipped template declares, in m^2/(V s) at 2.6 mbar.</summary>
    private const double Mobility = 0.042802;

    /// <summary>The counterflow the template declares, in m/s.</summary>
    private const double GasVelocity = 50.0;

    private static CompiledModel Compile()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read("tims-analyzer")));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>Axial field magnitude on the axis, sampled from the solved field.</summary>
    private static double AxialField(IElectrostaticField field, double xMetres) =>
        Math.Abs(field.ElectricFieldAt(new Vec3(xMetres, 0.0, 0.0)).X);

    /// <summary>Where the solved field holds an ion of the given mobility against the gas.</summary>
    /// <remarks>
    /// Bisection rather than a scan, because the field rises monotonically through the tunnel
    /// and the crossing is what is wanted rather than the curve.
    /// </remarks>
    private static double BalancePoint(IElectrostaticField field, double mobility)
    {
        double low = 0.003, high = 0.043;

        Assert.True(
            (mobility * AxialField(field, low)) - GasVelocity < 0.0
            && (mobility * AxialField(field, high)) - GasVelocity > 0.0,
            "the balance point is not bracketed by the tunnel, so there is nothing to find");

        for (var i = 0; i < 60; i++)
        {
            var middle = 0.5 * (low + high);

            if ((mobility * AxialField(field, middle)) - GasVelocity < 0.0)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return 0.5 * (low + high);
    }

    /// <summary>
    /// The ring stack reproduces the field its potentials imply: the rings are set to a
    /// potential going as the square of position, so the axial field is linear in position.
    /// </summary>
    /// <remarks>
    /// This is the geometry question. A ring stack does not reproduce the potential of its own
    /// electrodes on the axis exactly - the bore is 8 mm across and the pitch 1.725 mm, so the
    /// axis sees a smoothed version - and how well it does is what this measures.
    /// </remarks>
    [Fact]
    public void TheRingStackReproducesTheIntendedFieldGradient()
    {
        var model = Compile();
        var field = FieldAssembly.BuildReported(model).Field;

        var length = model.Parameters.Parameters["tunnelLength"].Value.SiValue;
        var exit = model.Parameters.Parameters["exitPotential"].Value.SiValue;

        // phi = V (x/L)^2 gives E = 2 V x / L^2, and the balance mu E = v_g then puts the
        // parking point at v_g L^2 / (2 mu V) with no reference to the solve at all.
        var closedForm = GasVelocity * length * length / (2.0 * Mobility * exit);
        var solved = BalancePoint(field, Mobility);

        output.WriteLine($"closed form  {closedForm * 1e3:F4} mm");
        output.WriteLine($"solved field {solved * 1e3:F4} mm   ({(solved - closedForm) / closedForm * 100:F3} %)");

        Assert.Equal(closedForm, solved, closedForm * 0.01);
    }

    /// <summary>
    /// Where the balance sits goes as one over the mobility, which is the separation.
    /// </summary>
    /// <remarks>
    /// One parking point agreeing with a formula could be a coincidence of two numbers. That
    /// the position scales as <c>1/K</c> across mobilities is the device doing what it is for,
    /// and it is exactly the elution relation <c>E_e = v_g / K</c>: the field is linear in
    /// position, so a position proportional to the field is a position proportional to 1/K.
    /// </remarks>
    [Fact]
    public void ParkingPositionGoesAsOneOverMobility()
    {
        var field = FieldAssembly.BuildReported(Compile()).Field;

        var reference = BalancePoint(field, Mobility);

        foreach (var ratio in new[] { 1.5, 1.25, 0.75 })
        {
            var moved = BalancePoint(field, Mobility * ratio);
            var expected = reference / ratio;

            output.WriteLine(
                $"K x {ratio:F2}: {moved * 1e3:F4} mm against {expected * 1e3:F4} mm expected "
                + $"({(moved - expected) / expected * 100:F3} %)");

            Assert.Equal(expected, moved, expected * 0.02);
        }
    }

    /// <summary>
    /// The field rises through the trapping region, so the balance there is stable - and it
    /// stops rising before the tunnel ends, which bounds the mobility range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sign of the gradient is what makes this a trap rather than a slope. An ion pushed
    /// downstream must meet a <em>stronger</em> field opposing it; where the field falls
    /// instead, the balance is unstable and an ion displaced downstream keeps going.
    /// </para>
    /// <para>
    /// <b>It stops rising about five millimetres short of the tunnel exit</b>, because the
    /// exit element stands in for the exit funnel at a single potential and flattens the
    /// gradient as it is approached. That is a property of the device rather than of the
    /// solve - a real exit funnel does the same - and it means the usable mobility range is
    /// set by the field at the peak rather than by the field at the last ring. The peak is
    /// measured here rather than asserted at a chosen place, because where it falls is the
    /// answer and not the question.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFieldRisesThroughTheTrappingRegionAndThenStops()
    {
        var model = Compile();
        var field = FieldAssembly.BuildReported(model).Field;

        var length = model.Parameters.Parameters["tunnelLength"].Value.SiValue;

        double peakAt = 0.0, peak = 0.0;
        var rising = 0.0;

        for (var x = 0.001; x < length; x += 0.0002)
        {
            var here = AxialField(field, x);

            if (here > peak)
            {
                (peak, peakAt) = (here, x);
                rising = x;
            }
        }

        var parked = BalancePoint(field, Mobility);

        output.WriteLine($"field peaks at {peakAt * 1e3:F2} mm, {peak:F0} V/m");
        output.WriteLine($"usable fraction of the {length * 1e3:F1} mm tunnel: {rising / length * 100:F1} %");
        output.WriteLine($"widest mobility this can hold: K such that mu = {GasVelocity / peak:F6} m^2/(V s)");
        output.WriteLine($"the reference ion parks at {parked * 1e3:F2} mm, inside it");

        // The trapping region has to reach most of the way down the tunnel, or the device is
        // shorter than it is built to be.
        Assert.True(
            peakAt > 0.75 * length,
            $"the field peaks at {peakAt * 1e3:F1} mm of a {length * 1e3:F1} mm tunnel, so most "
            + "of the analyser cannot hold anything");

        // And the reference ion must sit inside the rising part, or its balance is unstable.
        Assert.True(parked < peakAt, "the reference ion parks past the field maximum");
    }
}
