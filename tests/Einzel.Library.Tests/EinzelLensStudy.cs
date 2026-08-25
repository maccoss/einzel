using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The lens the platform is named after, and the first device that needed the
/// solve to be axisymmetric.
/// </summary>
/// <remarks>
/// <para>
/// Three coaxial tubes, the outer two earthed and the middle one at a voltage.
/// Until cylindrical symmetry existed this could not be modelled at all: what makes
/// it a lens is that each electrode wraps the whole way round, and the same
/// declaration in a translational cross-section is three pairs of bars, which
/// deflect rather than focus.
/// </para>
/// <para>
/// The checks are chosen to be things a wrong field would fail. Unipotential
/// operation is exact and has nothing to do with the lens strength. Converging for
/// <em>either</em> sign of the middle voltage is the classic non-obvious property -
/// it does not follow from the field being roughly right, it follows from the ion
/// spending longer in the converging half than in the diverging one. And spherical
/// aberration in the right direction says the radial dependence is real rather than
/// paraxial by construction.
/// </para>
/// </remarks>
public sealed class EinzelLensStudy(ITestOutputHelper output)
{
    private static ModelDocument Template() => Io.ModelJson.Parse(DeviceTemplates.Read("einzel-lens"));

    private static ModelDocument With(ModelDocument document, params (string Name, double Value)[] overrides)
    {
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        return validation.Model!;
    }

    /// <summary>
    /// Flies one ray parallel to the axis and reports where it crosses.
    /// </summary>
    /// <returns>
    /// The axial position of the crossing in millimetres, or null when the ray
    /// never crosses - a diverging lens, or one too weak to bring it back.
    /// </returns>
    private static (double? CrossingMm, TrajectoryResult Result) Focus(ModelDocument document)
    {
        var model = Compile(document);
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var settings = new IntegrationSettings
        {
            RelativeTolerance = 1e-11,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // Positive while the ray is still on the side it started, negative once it
        // has crossed. The integrator lands exactly on the zero, so the crossing is
        // a measurement rather than the last sample before one.
        TrajectoryStopFunction axis = (in PhaseState state) => state.Position.Y;

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, axis);

        return result.Outcome == TrajectoryOutcome.StopConditionMet
            ? (result.FinalState.Position.X * 1e3, result)
            : (null, result);
    }

    private static double MidplaneMm(ModelDocument document)
    {
        var model = Compile(document);

        return 0.5
            * (model.Parameters["centreStart"].In("mm") + model.Parameters["centreEnd"].In("mm"));
    }

    [Fact]
    public void ItFocuses()
    {
        var document = Template();
        var (crossing, result) = Focus(document);

        var midplane = MidplaneMm(document);

        output.WriteLine($"lens midplane at {midplane:F1} mm");
        output.WriteLine($"ray launched at 1.0 mm crosses the axis at {crossing:F2} mm");
        output.WriteLine($"so the focal length is about {crossing - midplane:F1} mm, "
            + $"{(crossing - midplane) / 5.0:F1} bore radii");
        output.WriteLine($"outcome {result.Outcome}, energy drift {result.MaximumRelativeEnergyDrift:E2}");

        Assert.NotNull(crossing);

        // Downstream of the lens, not inside it: a crossing within the middle
        // electrode would mean the ray was bent far harder than this voltage can.
        Assert.True(crossing > midplane, $"the ray crossed at {crossing:F2} mm, before the lens midplane");
    }

    [Theory]
    [InlineData(500.0)]
    [InlineData(-500.0)]
    public void ItConvergesForEitherSignOfTheMiddleVoltage(double centrePotential)
    {
        // The property that is not obvious and that a merely plausible field would
        // fail. Whichever way the middle electrode is driven, the ion passes through
        // one converging gap and one diverging gap - but it is slower in the
        // converging one when the middle decelerates it, and faster in the diverging
        // one, and the asymmetry always favours convergence.
        var document = With(Template(), ("centrePotential", centrePotential));
        var (crossing, _) = Focus(document);

        output.WriteLine($"centre at {centrePotential,+7:F0} V   crosses at {crossing:F2} mm");

        Assert.NotNull(crossing);
        Assert.True(crossing > MidplaneMm(document), "the ray did not converge");
    }

    [Fact]
    public void RaisingTheVoltageShortensTheFocus()
    {
        // A lens that focuses but does not respond to its own voltage is a lens
        // whose field came from somewhere else.
        output.WriteLine("centre / V    crossing / mm    focal length / mm");

        var previous = double.PositiveInfinity;

        foreach (var volts in new[] { 300.0, 400.0, 500.0, 600.0 })
        {
            var document = With(Template(), ("centrePotential", volts));
            var (crossing, _) = Focus(document);

            Assert.NotNull(crossing);

            var focal = crossing!.Value - MidplaneMm(document);
            output.WriteLine($"{volts,10:F0}    {crossing,13:F2}    {focal,17:F1}");

            Assert.True(
                focal < previous,
                $"raising the centre to {volts:F0} V lengthened the focus from {previous:F1} to {focal:F1} mm");

            previous = focal;
        }
    }

    [Fact]
    public void ItIsUnipotentialSoTheIonLeavesWithTheEnergyItArrivedWith()
    {
        // The defining property of an einzel lens, and the reason it can sit in a
        // beamline without changing anything downstream. It is exact: both outer
        // electrodes are earthed, so an ion that starts and ends inside them has
        // fallen through no net potential whatever the middle electrode did to it
        // on the way.
        //
        // Which makes it an ACC-4 check with a guaranteed answer rather than a
        // budgeted one, on a path that passes through a strong field twice.
        var document = Template();
        var model = Compile(document);
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var settings = new IntegrationSettings
        {
            RelativeTolerance = 1e-12,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        var entryPoint = launch.Position;
        var exitPoint = result.FinalState.Position;

        var entryKinetic = 0.5 * species.MassSi * launch.Velocity.LengthSquared;
        var exitKinetic = 0.5 * species.MassSi * result.FinalState.Velocity.LengthSquared;

        var entryPotential = species.ChargeSi * field.PotentialAt(in entryPoint);
        var exitPotential = species.ChargeSi * field.PotentialAt(in exitPoint);

        var scale = entryKinetic + entryPotential;

        var kineticChange = Math.Abs(exitKinetic - entryKinetic) / entryKinetic;
        var totalChange = Math.Abs((exitKinetic + exitPotential) - scale) / scale;

        output.WriteLine($"entry speed {Math.Sqrt(launch.Velocity.LengthSquared):F1} m/s, "
            + $"exit {Math.Sqrt(result.FinalState.Velocity.LengthSquared):F1} m/s");
        output.WriteLine($"kinetic energy changed by  {kineticChange:E3}");
        output.WriteLine($"total energy changed by    {totalChange:E3}");
        output.WriteLine(string.Empty);
        output.WriteLine($"potential at launch {field.PotentialAt(in entryPoint) * 1e3:F4} mV");
        output.WriteLine($"potential at exit   {field.PotentialAt(in exitPoint) * 1e3:F4} mV");

        // Total energy is exactly conserved in a static field, so this is the
        // assertion with a guaranteed answer, on a path that crosses a strong field
        // twice. ACC-4's diagnostic reports the same thing continuously.
        Assert.True(
            result.MaximumRelativeEnergyDrift < 1e-6,
            $"energy drift {result.MaximumRelativeEnergyDrift:E3} is over the ACC-4 budget");

        Assert.True(totalChange < 1e-8, $"total energy changed by {totalChange:E3} in a static field");

        // The *kinetic* energy comes back to within a few parts per million rather
        // than exactly, and that is the instrument rather than the integrator: the
        // ion is launched a quarter of the way down the entrance tube, where the
        // middle electrode's field has not quite finished decaying. The residual
        // goes as exp(-2.405 L / r), which is the same Bessel decay a grounded tube
        // always has - so a lens is unipotential only to the extent its tubes are
        // long, and how long is a design question this can answer.
        output.WriteLine(string.Empty);
        output.WriteLine(
            $"kinetic energy returns to {kineticChange:E2}, not exactly, because the launch point sits "
            + "in the tail of the field leaking down the entrance tube");

        Assert.True(kineticChange < 1e-4, $"the ion gained or lost {kineticChange:E3} of its kinetic energy");
    }

    [Fact]
    public void OuterRaysFocusShorterWhichIsSphericalAberration()
    {
        // Every real lens has it, and it is the reason a beam focuses to a blur
        // rather than a point. A paraxial field - one that varied only linearly
        // with radius - would put every ray at the same place, so measuring the
        // spread is measuring that the radial dependence is real.
        output.WriteLine("launch radius / mm    crossing / mm");

        var crossings = new List<double>();

        foreach (var radius in new[] { 0.5, 1.0, 1.5, 2.0 })
        {
            var document = With(Template(), ("launchRadius", radius));
            var (crossing, _) = Focus(document);

            Assert.NotNull(crossing);

            crossings.Add(crossing!.Value);
            output.WriteLine($"{radius,18:F2}    {crossing,13:F2}");
        }

        var spread = crossings[0] - crossings[^1];

        output.WriteLine(string.Empty);
        output.WriteLine($"the outermost ray focuses {spread:F2} mm shorter than the innermost");

        // Shorter, monotonically. Longer would be the wrong sign, which is what a
        // sign error in the radial field would produce while still focusing.
        for (var k = 1; k < crossings.Count; k++)
        {
            Assert.True(
                crossings[k] < crossings[k - 1],
                $"the ray at a larger radius focused longer, at {crossings[k]:F2} against {crossings[k - 1]:F2} mm");
        }

        Assert.True(spread > 0.1, $"the focus moved only {spread:F3} mm across a factor of four in radius");
    }
}
