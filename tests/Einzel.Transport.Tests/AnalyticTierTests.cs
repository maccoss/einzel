using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Transport;
using Einzel.Transport.Fields;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Tests;

/// <summary>
/// The analytic test tier of spec section 19: closed-form fields whose exact
/// trajectories are known, so any discrepancy is the integrator's.
/// </summary>
public sealed class AnalyticTierTests
{
    /// <summary>ACC-1: numerical flight-time error over a full analyzer.</summary>
    private const double Acc1FlightTimeBudget = 1e-6;

    /// <summary>ACC-4: energy drift in a static field.</summary>
    private const double Acc4EnergyDriftBudget = 1e-6;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    [Fact]
    public void FreeFlightIsExactToMachinePrecision()
    {
        // Spec section 19: "free-flight timing to machine precision". With
        // analytic drift the whole path is one exact advance, so there is no
        // integration error to accumulate at all.
        var species = Peptide;
        var speed = species.SpeedAfterAcceleration(Quantity.From(4.0, "kV")).SiValue;
        var distance = 7.55;

        var launch = new PhaseState(Vec3.Zero, new Vec3(speed, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3 };
        TrajectoryStopFunction detector = (in PhaseState s) => distance - s.Position.X;

        var result = TrajectoryIntegrator.Integrate(
            launch, species, FieldFreeSpace.Instance, settings, detector);

        var exact = distance / speed;
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.True(relative < 1e-15, $"free flight relative error {relative:E3} exceeds machine precision");

        // Nothing was integrated: the flight was one analytic advance.
        Assert.Equal(0, result.AcceptedSteps);
        Assert.Equal(distance, result.AnalyticDriftDistance, distance * 1e-12);
    }

    [Fact]
    public void UniformFieldTurnaroundMatchesClosedForm()
    {
        // Parabolic motion with a turning point and no discontinuity anywhere:
        // t = 2v/a exactly.
        var species = Peptide;
        var speed = species.SpeedAfterAcceleration(Quantity.From(4.0, "kV")).SiValue;
        var gradient = 80000.0;

        var field = UniformField.Create(Quantity.Si(gradient, Dimension.ElectricField), -Vec3.UnitX);
        var acceleration = Math.Abs(species.ChargeSi) * gradient / species.MassSi;
        var exact = 2.0 * speed / acceleration;

        var launch = new PhaseState(Vec3.Zero, new Vec3(speed, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 100.0 * exact };
        TrajectoryStopFunction detector = (in PhaseState s) => s.Position.X;

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        // Runge-Kutta of order five integrates constant acceleration exactly, so
        // the only residual is round-off. Measured at 1.3e-15.
        Assert.True(relative < 1e-13, $"uniform-field turnaround relative error {relative:E3}");
        Assert.True(
            result.MaximumRelativeEnergyDrift < 1e-12,
            $"energy drift {result.MaximumRelativeEnergyDrift:E3} is above round-off");
    }

    [Fact]
    public void ReflectronFlightTimeMeetsAcc1()
    {
        var reflectron = IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(),
            reflectron.Species,
            reflectron.Field,
            reflectron.Settings(),
            reflectron.DetectorPlane());

        var exact = reflectron.ExactFlightTime();
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.True(
            relative < Acc1FlightTimeBudget,
            $"flight-time error {relative * 1e6:F4} ppm exceeds the ACC-1 budget of 1 ppm");

        // Measured at 1.3e-10 with the default settings, so the assertion above
        // passes with about four orders of margin. The residual is not truncation:
        // it is the field discontinuity at the mirror entrance, discussed on
        // IElectrostaticField.SignedDistanceToDiscontinuity.
        Assert.True(relative < 1e-8, $"flight-time error {relative:E3} regressed against the measured 1.3e-10");
    }

    [Fact]
    public void ReflectronEnergyDriftMeetsAcc4()
    {
        var reflectron = IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(),
            reflectron.Species,
            reflectron.Field,
            reflectron.Settings(),
            reflectron.DetectorPlane());

        Assert.True(
            result.MaximumRelativeEnergyDrift < Acc4EnergyDriftBudget,
            $"energy drift {result.MaximumRelativeEnergyDrift:E3} exceeds the ACC-4 budget of 1 ppm");
    }

    [Fact]
    public void FirstOrderEnergyFocusingIsRecovered()
    {
        // The physics test, not an arithmetic one. At a total field-free path of
        // four penetration depths, dT/dv vanishes: an ion 3 percent fast and an
        // ion 3 percent slow arrive together to first order. Getting this right
        // requires the field, the integrator, and the geometry all to agree.
        var reflectron = IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

        Assert.Equal(4.0, 2.0 * reflectron.DriftLength / reflectron.PenetrationDepth, 12);

        var derivative = IntegratedVelocityDerivative(reflectron, 1e-4);
        var scale = reflectron.ExactFlightTime();

        // Exactly zero analytically. What survives is the finite difference's own
        // second-order term plus the amplified discontinuity artifact. The claim
        // that matters is comparative: this is about five orders of magnitude
        // below the first-order term the detuned geometry shows in the next test.
        Assert.True(
            Math.Abs(derivative) < 1e-5 * scale,
            $"dT/dv at focus is {derivative:E3} s against a flight time of {scale:E3} s");
    }

    [Fact]
    public void DetunedReflectronReproducesTheAnalyticDerivative()
    {
        // The complement of the previous test: move the drift length away from
        // focus and the first-order term reappears with the magnitude theory
        // predicts. Without this, a bug that simply returned a constant flight
        // time would pass the focusing test.
        var reflectron = IdealSingleStageReflectron.Detuned(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"), driftInDepths: 1.5);

        var integrated = IntegratedVelocityDerivative(reflectron, 1e-4);
        var expected = reflectron.ExactFlightTimeVelocityDerivative();

        Assert.True(Math.Abs(expected) > 0.0, "the detuned geometry should have a non-zero first-order term");

        // Measured agreement is 1.4e-6 relative. What limits it is not the
        // physics but the central difference: it divides by 2e-4, so the
        // discontinuity artifact on each flight time — which behaves as noise
        // around 1e-10 relative rather than as a controlled error — arrives here
        // multiplied by ten thousand. The tolerance is set from that, with margin.
        Assert.Equal(expected, integrated, Math.Abs(expected) * 1e-4);
    }

    [Theory]
    [InlineData(-0.05)]
    [InlineData(-0.03)]
    [InlineData(0.0)]
    [InlineData(0.03)]
    [InlineData(0.05)]
    public void FlightTimeTracksClosedFormAcrossTheEnergyAcceptance(double energyFraction)
    {
        // The memo asks for an energy acceptance of plus or minus 3 to 5 percent,
        // so the integrator has to hold ACC-1 across that band, not only at the
        // nominal energy.
        var reflectron = IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(energyFraction),
            reflectron.Species,
            reflectron.Field,
            reflectron.Settings(),
            reflectron.DetectorPlane());

        var exact = reflectron.ExactFlightTime(energyFraction);
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.True(relative < Acc1FlightTimeBudget, $"error {relative * 1e6:F4} ppm at {energyFraction:P0}");
    }

    private static double IntegratedVelocityDerivative(IdealSingleStageReflectron reflectron, double epsilon)
    {
        // A central difference divides by 2 epsilon, so it amplifies whatever
        // error each flight time carries by 1 / 2e-4 here. With the turning-point
        // cap on, the boundary artifact described in
        // ExactBoundaryLandingDominatesTheErrorBudget is around 1e-10 relative and
        // swamps the measurement; with it off the flight times are at machine
        // precision and what remains is the finite difference's own truncation,
        // which is second order in epsilon and around 3e-8 relative here.
        var settings = reflectron.Settings() with { TurningPointStepFactor = 0.0 };

        var fast = TrajectoryIntegrator.Integrate(
            reflectron.LaunchAtSpeedFraction(epsilon), reflectron.Species, reflectron.Field,
            settings, reflectron.DetectorPlane());

        var slow = TrajectoryIntegrator.Integrate(
            reflectron.LaunchAtSpeedFraction(-epsilon), reflectron.Species, reflectron.Field,
            settings, reflectron.DetectorPlane());

        Assert.Equal(TrajectoryOutcome.StopConditionMet, fast.Outcome);
        Assert.Equal(TrajectoryOutcome.StopConditionMet, slow.Outcome);

        return (fast.FlightTimeSeconds - slow.FlightTimeSeconds) / (2.0 * epsilon);
    }
}
