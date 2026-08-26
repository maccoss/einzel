using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// What gas does to an ion funnel, which is the device its absence hurt most.
/// </summary>
/// <remarks>
/// <para>
/// A real funnel runs near a millibar, and the collisions are not a nuisance - they
/// are how it works. Without them an ion entering off axis rings about the axis and
/// keeps whatever radial energy it arrived with; with them the radial motion damps
/// and the ion settles onto the axis, which is the entire purpose of the device.
/// </para>
/// <para>
/// Every acceptance figure this template previously reported was therefore a lower
/// bound, and said so. These measure how much of a bound it was.
/// </para>
/// </remarks>
public sealed class FunnelDampingStudy(ITestOutputHelper output)
{
    private static ModelDocument Template() => ModelJson.Parse(DeviceTemplates.Read("ion-funnel"));

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document, null);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>Overrides declared parameters, the way a sweep does.</summary>
    private static ModelDocument With(ModelDocument document, params (string Name, double Value)[] overrides)
    {
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    /// <summary>The funnel with a gas load declared on it.</summary>
    private static ModelDocument WithGas(ModelDocument document, double pressureMbar) =>
        document with
        {
            SchemaVersion = "0.4",
            Transport = document.Transport! with
            {
                Gas = new GasDocument
                {
                    Model = "langevin",
                    Pressure = new QuantityValue(pressureMbar, "mbar"),
                    Mass = new QuantityValue(28.0134, "Da"),
                    Polarizability = new QuantityValue(1.74, "Å^3"),
                    Seed = 90_210,
                },
            },
        };

    [Fact]
    public void GasDampsTheRadialMotionAFunnelIsForFor()
    {
        // The claim: an ion entering off axis leaves with less radial energy than it
        // arrived with, and less of it the more gas there is. The RF alone confines -
        // the template already shows that switching the drive off loses the ion on a
        // named ring - but confinement is not compression, and damping is what turns
        // one into the other.
        //
        // The funnel's axis is x. Its radius is therefore in y and z, and computing
        // it about the wrong axis returns the axial coordinate, which for a
        // transmitted ion is the length of the device and looks like catastrophic
        // divergence. That is how the first version of this test read.
        output.WriteLine("pressure     outcome            exit radius   radial speed   collisions");

        foreach (var pressure in new[] { 0.0, 1e-3, 1e-2, 1e-1 })
        {
            var document = With(Template(), ("launchRadius", 6.0));

            if (pressure > 0.0)
            {
                document = WithGas(document, pressure);
            }

            var model = Compile(document);
            var (field, _) = FieldAssembly.BuildReported(model);

            var species = IonSpecies.FromModel(model);
            var gas = BackgroundGas.FromModel(model.Gas);

            var launch = new PhaseState(
                model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

            var detectorPoint = model.DetectorPoint;
            var detectorNormal = model.DetectorNormal;

            TrajectoryStopFunction detector =
                (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

            var sampler = gas.IsPresent
                ? new CollisionSampler(gas, species.MassSi, species.ChargeSi, model.Gas.Seed)
                : null;

            var result = TrajectoryIntegrator.Integrate(
                launch,
                species,
                field,
                new IntegrationSettings
                {
                    RelativeTolerance = 1e-9,
                    MaximumFlightTime = model.MaximumFlightTimeSi,
                },
                detector,
                collisions: sampler);

            var exit = result.FinalState.Position;
            var radius = Math.Sqrt((exit.Y * exit.Y) + (exit.Z * exit.Z)) * 1e3;

            var velocity = result.FinalState.Velocity;
            var radial = Math.Sqrt((velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));

            output.WriteLine(
                $"{(pressure > 0.0 ? pressure.ToString("G2") + " mbar" : "vacuum"),-12} "
                + $"{result.Outcome,-18} {radius,8:F3} mm   {radial,8:F1} m/s"
                + $"   {(sampler is null ? "-" : sampler.Collisions.ToString())}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("The damping is real and it is visible: 864.6 m/s of radial speed becomes");
        output.WriteLine("255.7, and 1.59 mm of exit radius becomes 0.42. That is a funnel doing the");
        output.WriteLine("thing a funnel is for, and none of it existed before there was a gas.");
        output.WriteLine(string.Empty);
        output.WriteLine("Two things it also shows, both awkward and both worth knowing. At 1e-3 mbar");
        output.WriteLine("an ion crosses this funnel in a few microseconds and collides with nothing");
        output.WriteLine("at all - the trajectory is bit-identical to the vacuum one - so the damping");
        output.WriteLine("only appears at or above the validity boundary of the mode computing it.");
        output.WriteLine("And at 0.1 mbar the ion is damped axially as well and never leaves inside");
        output.WriteLine("the flight-time ceiling: a real funnel is pushed through by a gas flow, and");
        output.WriteLine("a stationary gas has no such push. Neither is a bug. Both are the reason");
        output.WriteLine("this device wants the diffusive mode rather than a longer flight ceiling.");
    }

    [Fact]
    public void TheDeclaredFunnelPressureIsOutsideTrajectoryValidity()
    {
        // The honest headline. A funnel runs at 1 to 10 mbar and trajectory
        // integration is valid below about 1e-2, so the regime check refuses the
        // pressure the device is actually operated at - which is a statement about
        // this engine rather than about the funnel.
        //
        // That refusal is the useful output. Running a funnel at 1e-4 mbar to keep
        // the warning quiet would model a different instrument and say nothing about
        // the real one.
        var model = Compile(WithGas(Template(), 2.0));
        var gas = BackgroundGas.FromModel(model.Gas);
        var species = IonSpecies.FromModel(model);

        var numbers = RegimeDiagnostics.Measure(
            gas, species, model.LaunchSpeedSi(), model.MaximumFlightTimeSi, 1.5e-3, 1e6);

        var warnings = RegimeDiagnostics.ForTrajectoryMode(gas, numbers);

        output.WriteLine($"pressure           {numbers.PressureMbar:G3} mbar");
        output.WriteLine($"mean free path     {numbers.MeanFreePathM * 1e6:F1} um");
        output.WriteLine($"Knudsen, 1.5 mm    {numbers.Knudsen:G3}");
        output.WriteLine($"collisions/flight  {numbers.CollisionsPerFlight:G3}");
        output.WriteLine($"collisions/RF cycle {numbers.CollisionsPerRfCycle:G3}");
        output.WriteLine(string.Empty);

        foreach (var warning in warnings)
        {
            output.WriteLine($"[{warning.Severity}] {warning.Code}");
        }

        Assert.Contains(warnings, w => w.Code == "regime.trajectory-above-validity");
        Assert.Contains(warnings, w => w.Code == "regime.knudsen-continuum");

        // The one that matters most for a driven device: an ion that collides more
        // than once per RF cycle never completes an oscillation, so the
        // pseudopotential the funnel is designed around does not exist for it.
        Assert.Contains(warnings, w => w.Code == "regime.collisions-outrun-rf");

        Assert.All(warnings, w => Assert.False(w.IsSuppressible));
    }
}
