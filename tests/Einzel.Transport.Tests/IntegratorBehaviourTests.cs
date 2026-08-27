using Einzel.Core.Geometry;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Transport;
using Einzel.Fields;
using Einzel.Transport.Integration;

namespace Einzel.Transport.Tests;

public sealed class IntegratorBehaviourTests
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static IdealSingleStageReflectron Reflectron() =>
        IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

    [Fact]
    public void ExactBoundaryLandingDominatesTheErrorBudget()
    {
        // Regression guard on the finding that motivated
        // SignedDistanceToDiscontinuity. Before the integrator landed on the
        // mirror boundary, a step straddled the field jump and the flight-time
        // error sat on a floor near 5e-10 that barely responded to tolerance.
        // Landing exactly on the boundary took the same case to 1.7e-16.
        var reflectron = Reflectron();
        var settings = reflectron.Settings() with { TurningPointStepFactor = 0.0 };

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field, settings, reflectron.DetectorPlane());

        var exact = reflectron.ExactFlightTime();
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.True(relative < 1e-13, $"relative error {relative:E3} regressed against the measured 1.7e-16");
    }

    [Fact]
    public void AnalyticDriftCoversTheWholeFieldFreePath()
    {
        var reflectron = Reflectron();

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        // Out and back along the drift region, advanced exactly rather than
        // integrated. In the memo's design point B this is most of a 7.55 m path.
        var expected = 2.0 * reflectron.DriftLength;
        Assert.Equal(expected, result.AnalyticDriftDistance, expected * 1e-9);
    }

    [Fact]
    public void DisablingAnalyticDriftStillAgreesWithTheClosedForm()
    {
        // Analytic drift is an optimisation, not a correction: switching it off
        // must change cost, not the answer.
        var reflectron = Reflectron();
        var stop = reflectron.DetectorPlane();

        var withDrift = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), stop);

        var withoutDrift = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings() with { UseAnalyticDrift = false }, stop);

        Assert.Equal(0.0, withoutDrift.AnalyticDriftDistance);
        Assert.Equal(
            withDrift.FlightTimeSeconds,
            withoutDrift.FlightTimeSeconds,
            withDrift.FlightTimeSeconds * 1e-9);
    }

    [Fact]
    public void FinalStateLandsOnTheDetectorPlaneRatherThanPastIt()
    {
        var reflectron = Reflectron();

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        // The crossing time is the measurement, so the flight must end on the
        // surface, not at the end of whichever step happened to pass it.
        Assert.Equal(-reflectron.DriftLength, result.FinalState.Position.X, 1e-12);
    }

    [Fact]
    public void AnUnboundedRunIsRefusedRatherThanRunForever()
    {
        var settings = new IntegrationSettings();
        var launch = new PhaseState(Vec3.Zero, new Vec3(1000.0, 0.0, 0.0));

        var error = Assert.Throws<ArgumentException>(() => TrajectoryIntegrator.Integrate(
            launch, Peptide, FieldFreeSpace.Instance, settings));

        Assert.Contains("MaximumFlightTime", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllocationDoesNotGrowWithStepCount()
    {
        // Spec section 22 lists GC pauses in long runs as a risk "preventable
        // with allocation discipline from the start", and section 11 requires the
        // inner loop to allocate nothing. Testing a byte threshold would encode
        // today's fixed overhead; testing that cost is flat in step count tests
        // the actual property.
        var reflectron = Reflectron();
        var stop = reflectron.DetectorPlane();

        var few = reflectron.Settings() with { TurningPointStepFactor = 0.05 };
        var many = reflectron.Settings() with { TurningPointStepFactor = 0.0005 };

        // Warm up: first-call JIT and static initialisation are not per-step cost.
        var warmFew = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field, few, stop);
        var warmMany = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field, many, stop);

        Assert.True(
            warmMany.AcceptedSteps > warmFew.AcceptedSteps * 10,
            $"the two settings should differ greatly in step count: {warmFew.AcceptedSteps} vs {warmMany.AcceptedSteps}");

        var beforeFew = GC.GetAllocatedBytesForCurrentThread();
        TrajectoryIntegrator.Integrate(reflectron.LaunchState(), reflectron.Species, reflectron.Field, few, stop);
        var costFew = GC.GetAllocatedBytesForCurrentThread() - beforeFew;

        var beforeMany = GC.GetAllocatedBytesForCurrentThread();
        TrajectoryIntegrator.Integrate(reflectron.LaunchState(), reflectron.Species, reflectron.Field, many, stop);
        var costMany = GC.GetAllocatedBytesForCurrentThread() - beforeMany;

        Assert.True(
            costMany <= costFew + 256,
            $"allocation grew with step count: {costFew} bytes over {warmFew.AcceptedSteps} steps, "
            + $"{costMany} bytes over {warmMany.AcceptedSteps} steps");
    }

    [Fact]
    public void FlightTimeStudyReportsAConvergenceBoundedResult()
    {
        var reflectron = Reflectron();

        var study = FlightTimeStudy.Run(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        var (value, uncertainty, evidence, warnings) = study.FlightTime;

        var exact = reflectron.ExactFlightTime();
        Assert.Equal(exact, value.SiValue, exact * 1e-8);

        // A deterministic convergence bound, not a statistical interval.
        Assert.Equal(1.0, uncertainty.ConfidenceLevel);
        Assert.True(uncertainty.WidthSi > 0.0, "an interval of exactly zero is not a bound");

        var convergence = Assert.IsType<Evidence.Convergence>(evidence);
        Assert.Equal("integrator tolerance", convergence.Measure);

        Assert.DoesNotContain(warnings, w => w.Severity == WarningSeverity.ValidityViolation);
        Assert.Equal(3, study.Runs.Count);
    }

    [Fact]
    public void AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact()
    {
        // GRD-1 in its sharpest form. On a flight this engine gets right to the last
        // bit, the two finest refinements agree exactly, and the residual between
        // them - which is what the interval is built from - is zero. Printed, that
        // reads as "10.180506 +/- 0 us", which a reader takes for an exact number.
        //
        // It is not exact. It is an uncertainty smaller than a comparison of two
        // doubles can see, and the two statements are not interchangeable: one is
        // defensible in a paper and the other is not. An agent asked to quote a
        // result refused this one and measured its own tolerance ladder instead,
        // which was the right instinct and should not have been necessary.
        var reflectron = Reflectron();

        var study = FlightTimeStudy.Run(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        var finest = study.Runs[^1];

        // The premise, asserted rather than assumed: this is the case where the
        // pair comparison really does collapse. If the integrator ever stops being
        // this good the test would otherwise pass while measuring nothing.
        Assert.Equal(study.Runs[^2].FlightTimeSeconds, finest.FlightTimeSeconds);

        var (_, uncertainty, evidence, warnings) = study.FlightTime;
        var convergence = Assert.IsType<Evidence.Convergence>(evidence);

        Assert.True(convergence.ResidualSi > 0.0, "the residual collapsed to zero");
        Assert.True(uncertainty.WidthSi > 0.0);

        // And it says the interval is a floor rather than a measurement, because a
        // number this small is only honest with that attached. Provenance, not a
        // validity violation: nothing is wrong with the answer.
        var floor = Assert.Single(warnings, w => w.Code == "convergence.at-resolution");

        Assert.Equal(WarningSeverity.Provenance, floor.Severity);

        // One ulp of the answer is the smallest it may ever claim.
        var ulp = Math.BitIncrement(finest.FlightTimeSeconds) - finest.FlightTimeSeconds;

        Assert.True(convergence.ResidualSi >= ulp);
    }

    [Fact]
    public void AnIncompleteTrajectoryIsAValidityViolation()
    {
        // GRD-4: validity is checked, not assumed. A run that hit its ceiling
        // instead of the detector must not report a flight time that looks clean.
        var reflectron = Reflectron();
        var truncated = reflectron.Settings() with { MaximumFlightTime = reflectron.ExactFlightTime() * 0.25 };

        var study = FlightTimeStudy.Run(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            truncated, reflectron.DetectorPlane());

        Assert.True(study.FlightTime.HasNonSuppressibleWarnings);
        Assert.Contains(study.FlightTime.Warnings, w => w.Code == "TRAJECTORY_INCOMPLETE");
    }
}
