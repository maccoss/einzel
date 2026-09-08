using System.Text.Json.Nodes;

using Einzel.Commands;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The tunnel's quadrupolar RF holds the density off the bore wall, and the width it holds
/// it to is the Boltzmann width in the collisional pseudopotential.
/// </summary>
/// <remarks>
/// <para>
/// Without confinement the axial balance is right and almost nothing survives to be
/// measured: the density diffuses across the 8 mm bore in a millisecond or two, and an
/// elution scan delivered 45 of 97,770 ions. The RF is what makes the transmission and
/// the peak width the instrument's rather than the bore's, so this is the stage that
/// makes a resolving power comparable with anything published.
/// </para>
/// <para>
/// <b>The width is the sharp check</b>, because the well is built to be exactly harmonic
/// (the field is an ideal quadrupole scaled by the solved segment fraction) and the density
/// solver's zero-flux state is exactly Boltzmann, so the equilibrium radial profile is a
/// Gaussian whose second moment is a closed form with the engine's own suppression factor
/// in it. The collisionless well that is usually quoted is a fifth deeper here - the damping
/// rate is two thirds of the drive frequency at 2.6 mbar - and predicts a width that is
/// measurably wrong, which is the control that says the collisional form is being used.
/// </para>
/// </remarks>
public sealed class TimsConfinementTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;
    private const double ElementaryCharge = 1.602176634e-19;
    private const double Boltzmann = 1.380649e-23;

    /// <summary>The shipped template with the packet at its parking point, held for a while.</summary>
    private static (CompiledModel Model, IElectrostaticField Field, IReadOnlyList<ValidityWarning> Warnings) Compile(
        double rfAmplitudeVolts, double holdUs)
    {
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-analyzer"))!;

        document["parameters"]!["sourceX"]!["value"] = 21.1036;
        document["parameters"]!["rfAmplitude"]!["value"] = rfAmplitudeVolts;
        document["transport"]!["maximumFlightTime"] = JsonNode.Parse($$$"""{ "value": {{{holdUs}}}, "unit": "us" }""");

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var built = FieldAssembly.BuildReported(validation.Model!);
        return (validation.Model!, built.Field, built.Warnings);
    }

    private static (DiffusiveOutcome Outcome, CompiledModel Model) Hold(double rfAmplitudeVolts, double holdUs)
    {
        var (model, field, warnings) = Compile(rfAmplitudeVolts, holdUs);
        return (DiffusionRun.Execute(model, field, warnings, scheme: StepScheme.Implicit, stepGain: 16.0), model);
    }

    /// <summary>The root-mean-square radius of an axisymmetric density, ring volumes and all.</summary>
    private static double RmsRadius(DensityField density)
    {
        double weight = 0.0, second = 0.0;

        for (var j = 0; j < density.Grid.CountY; j++)
        {
            var r = density.Grid.Y(j);
            var volume = density.CellVolume(j);

            for (var i = 0; i < density.Grid.CountX; i++)
            {
                var w = density[i, j] * volume;
                weight += w;
                second += w * r * r;
            }
        }

        return Math.Sqrt(second / weight);
    }

    /// <summary>With the RF on the bore takes nothing; with it off, the bore takes the density.</summary>
    [Fact]
    public void TheRfKeepsTheDensityOffTheWall()
    {
        var confined = Hold(100.0, 600.0).Outcome;
        var unconfined = Hold(0.0, 600.0).Outcome;

        var lostConfined = 1.0 - (confined.Result.Remaining / confined.Launched);
        var lostUnconfined = 1.0 - (unconfined.Result.Remaining / unconfined.Launched);

        output.WriteLine($"confined:   {lostConfined:P4} of the density left the packet in 600 us ({confined.Result.Steps} steps, {confined.Result.Assemblies} assembly)");
        output.WriteLine($"unconfined: {lostUnconfined:P4}");
        output.WriteLine("warnings: " + string.Join(", ", confined.Warnings.Select(w => w.Code)));

        Assert.True(lostUnconfined > 0.02, $"the unconfined tunnel lost only {lostUnconfined:P2}, so there is nothing for the RF to prevent");
        Assert.True(lostConfined < 1e-3, $"the RF left {lostConfined:P3} of the density on the wall");
        Assert.True(lostConfined < 0.01 * lostUnconfined, "the confinement is not doing most of the work");

        // The confinement is reported as what it is - a cycle average with a stated
        // suppression - and does not trip the mesh check: the RF is analytic and the DC
        // field's own cell is coarser than the quiver.
        Assert.Contains(confined.Warnings, w => w.Code == "rf.effective-potential");
        Assert.DoesNotContain(confined.Warnings, w => w.Code == "rf.quiver-exceeds-mesh");
    }

    /// <summary>The RF has no axial component, so the packet parks where it parked before.</summary>
    [Fact]
    public void TheParkingPointDoesNotMove()
    {
        var confined = Hold(100.0, 600.0).Outcome;
        var (x, _) = confined.Result.Density.Centroid();

        output.WriteLine($"confined packet centre after 600 us: {x * 1e3:F4} mm against a balance at 21.1036 mm");

        Assert.Equal(21.1036, x * 1e3, 0.03);
    }

    /// <summary>
    /// The held width is the Boltzmann width in the collisional well, and not in the
    /// collisionless one.
    /// </summary>
    [Fact]
    public void TheConfinedWidthIsBoltzmannInTheCollisionalWell()
    {
        var (confined, model) = Hold(100.0, 600.0);

        var parameters = model.Parameters.Parameters;
        var amplitude = parameters["rfAmplitude"].Value.SiValue * parameters["rfQuadrupoleFraction"].Value.SiValue;
        var r0 = parameters["boreRadius"].Value.SiValue;
        var omega = 2.0 * Math.PI * parameters["rfFrequency"].Value.SiValue;
        var exitVolts = parameters["exitPotential"].Value.SiValue;
        var length = parameters["tunnelLength"].Value.SiValue;

        var mass = 622.0 * Dalton;
        var mobility = model.Mobility!.ZeroFieldSi;
        var nu = ElementaryCharge / (mass * mobility);
        var kTOverQ = Boltzmann * 300.0 / ElementaryCharge;

        // The well: Psi / q = q E0'^2 r^2 / (4 m (Omega^2 + nu^2)) with E0' = 2 V / r0^2 the
        // field per unit radius, zero to peak. The DC tunnel field is x^2-shaped along the
        // axis, so by Laplace it is -r^2/2 shaped across it and pushes ions OUT at V r / L^2,
        // which is a few per cent of the well and is subtracted.
        var slope = 2.0 * amplitude / (r0 * r0);
        var collisional = ElementaryCharge * slope * slope / (4.0 * mass * ((omega * omega) + (nu * nu)));
        var collisionless = ElementaryCharge * slope * slope / (4.0 * mass * omega * omega);
        var defocus = exitVolts / (2.0 * length * length);

        // Boltzmann in phi_eff = c r^2 is a two-dimensional Gaussian with <r^2> = kT / (q c).
        var predicted = Math.Sqrt(kTOverQ / (collisional - defocus));
        var naive = Math.Sqrt(kTOverQ / (collisionless - defocus));
        var measured = RmsRadius(confined.Result.Density);

        output.WriteLine($"damping {nu:E3} /s against drive {omega:E3} rad/s: suppression {omega * omega / ((omega * omega) + (nu * nu)):F4}");
        output.WriteLine($"rms radius: measured {measured * 1e3:F4} mm, collisional well {predicted * 1e3:F4} mm ({(measured - predicted) / predicted:+P2}), collisionless {naive * 1e3:F4} mm ({(measured - naive) / naive:+P2})");

        Assert.Equal(predicted, measured, predicted * 0.05);
        Assert.True(Math.Abs(measured - naive) > 0.10 * naive, "the collisionless well predicts the width too - the suppression is not being applied");
    }
}
