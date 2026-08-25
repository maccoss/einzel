using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Ions stop when they hit metal.
/// </summary>
/// <remarks>
/// <para>
/// Until this, an electrode was a boundary condition on the potential and nothing
/// else: an ion flew through a plate as readily as through the hole next to it,
/// and transmission was 100% by construction. That makes an aperture scenery and
/// a slot decorative, and it is the reason ACC-5's "transmission itemised by loss
/// surface" had no loss surfaces.
/// </para>
/// <para>
/// The sharpest test here is the aperture one, because a straight line through a
/// slit has an exact answer that owes nothing to this code: the fraction of a
/// Gaussian inside the opening, which is an error function. Everything else -
/// where the ion lands, which surface it is attributed to - is checked against
/// geometry rather than against a previous run.
/// </para>
/// </remarks>
public sealed class ElectrodeAbsorptionTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>
    /// A grounded box with a grounded plate across it, pierced by a slit.
    /// </summary>
    /// <remarks>
    /// Everything at zero volts on purpose. The field is then identically zero, so
    /// ions fly in straight lines and whether one gets through is pure geometry -
    /// which is what makes the closed form exact rather than approximate.
    /// </remarks>
    private static IElectrostaticField Aperture(double slitHalfWidth, double plateX)
    {
        var solve = new CompiledSolvedField
        {
            MinX = -0.01,
            MinY = -0.01,
            MaxX = 0.03,
            MaxY = 0.01,
            CellSize = 5e-4,
            Tolerance = 1e-10,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "plateLower",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = plateX,
                    MaxX = plateX + 5e-4,
                    MinY = -0.008,
                    MaxY = -slitHalfWidth,
                    Potential = 0.0,
                },
                new CompiledElectrode
                {
                    Name = "plateUpper",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = plateX,
                    MaxX = plateX + 5e-4,
                    MinY = slitHalfWidth,
                    MaxY = 0.008,
                    Potential = 0.0,
                },
            ],
        };

        return GeometryBuilder.Build(solve).Field;
    }

    private static TrajectoryResult Fly(IElectrostaticField field, PhaseState start, double detectorX)
    {
        var settings = new IntegrationSettings { RelativeTolerance = 1e-10, MaximumFlightTime = 1e-3 };
        TrajectoryStopFunction detector = (in PhaseState s) => detectorX - s.Position.X;

        return TrajectoryIntegrator.Integrate(start, Peptide, field, settings, detector);
    }

    private static PhaseState Launch(double y, double speed) =>
        new(new Vec3(-0.005, y, 0.0), new Vec3(speed, 0.0, 0.0));

    [Fact]
    public void AnIonAimedAtMetalIsAbsorbedAndTheSurfaceIsNamed()
    {
        var field = Aperture(slitHalfWidth: 5e-4, plateX: 0.005);
        var result = Fly(field, Launch(0.003, 1e4), detectorX: 0.02);

        output.WriteLine($"outcome {result.Outcome}, surface '{result.StruckSurface}'");
        output.WriteLine($"stopped at x = {result.FinalState.Position.X * 1e3:F4} mm");

        Assert.Equal(TrajectoryOutcome.StruckElectrode, result.Outcome);
        Assert.Equal("plateUpper", result.StruckSurface);
    }

    [Fact]
    public void AnIonThroughTheSlitReachesTheDetector()
    {
        // The control. Without it the test above passes for a geometry that stops
        // everything, which is not an aperture, it is a wall.
        var field = Aperture(slitHalfWidth: 5e-4, plateX: 0.005);
        var result = Fly(field, Launch(0.0, 1e4), detectorX: 0.02);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Null(result.StruckSurface);
    }

    [Fact]
    public void TheIonLandsOnTheSurfaceRatherThanNearIt()
    {
        // An electrode is an event, not a test applied after the fact, so the ion
        // should stop on the surface to the tolerance the integrator works at - not
        // a step short of it and not a step inside it. Stopping short would leave
        // an ion hovering in vacuum; stopping inside would put it in the metal, and
        // both would make the impact position useless for anything downstream.
        var field = Aperture(slitHalfWidth: 5e-4, plateX: 0.005);
        var bounded = Assert.IsAssignableFrom<IConductorBounded>(field);

        foreach (var y in new[] { 0.0015, 0.003, 0.005, -0.002 })
        {
            var result = Fly(field, Launch(y, 1e4), detectorX: 0.02);
            Assert.Equal(TrajectoryOutcome.StruckElectrode, result.Outcome);

            var impact = result.FinalState.Position;
            var distance = bounded.SignedDistanceToConductor(in impact);

            output.WriteLine($"launched at y = {y * 1e3,+6:F2} mm, landed {distance * 1e9:+0.000;-0.000} nm from the surface");

            // Nanometres, against a 0.5 mm plate. The sign is allowed either way:
            // the root-find brackets the surface and lands on whichever side of the
            // zero the last iterate fell.
            Assert.True(
                Math.Abs(distance) < 1e-8,
                $"landed {distance * 1e6:F3} um from the surface, which is not on it");
        }
    }

    [Fact]
    public void TransmissionThroughASlitMatchesTheErrorFunction()
    {
        // The closed form. Everything is grounded so the field is identically zero
        // and the ions fly straight, which makes the fraction that gets through the
        // fraction of the launch distribution inside the opening - an error
        // function, and nothing to do with this code.
        const double SlitHalfWidth = 5e-4;
        const double SpreadM = 6e-4;
        const int Ions = 20000;

        var field = Aperture(SlitHalfWidth, plateX: 0.005);
        var species = Peptide;

        var cloud = IonCloud.Draw(
            new PhaseState(new Vec3(-0.005, 0.0, 0.0), new Vec3(1e4, 0.0, 0.0)),
            species,
            new IonCloudSettings { Ions = Ions, Seed = 17, TransverseSpreadM = SpreadM });

        var arrived = 0;
        var onPlate = 0;

        foreach (var start in cloud)
        {
            var result = Fly(field, start, detectorX: 0.02);

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrived++;
            }
            else if (result.Outcome == TrajectoryOutcome.StruckElectrode)
            {
                onPlate++;
            }
        }

        var measured = (double)arrived / Ions;

        // erf(a / (sigma sqrt 2)) is the fraction of a Gaussian within +/- a. No
        // thermal velocity is declared, so the ions travel parallel and their
        // transverse position at the plate is the one they were launched with.
        var exact = Erf(SlitHalfWidth / (SpreadM * Math.Sqrt(2.0)));

        output.WriteLine($"slit +/-{SlitHalfWidth * 1e3:F2} mm, beam sigma {SpreadM * 1e3:F2} mm");
        output.WriteLine($"closed form  {exact:P2}");
        output.WriteLine($"measured     {measured:P2} ({arrived} through, {onPlate} on the plate)");

        // Every ion is accounted for: through, or on a named surface.
        Assert.Equal(Ions, arrived + onPlate);

        // Twenty thousand draws place a proportion near 0.6 to 0.35% at one sigma.
        // A binomial interval is the honest bar here, and it is what ACC-5 asks
        // transmission to be reported with in the first place.
        var oneSigma = Math.Sqrt(exact * (1.0 - exact) / Ions);

        output.WriteLine($"one sigma    {oneSigma:P2}, so this is {Math.Abs(measured - exact) / oneSigma:F2} of it");

        Assert.True(
            Math.Abs(measured - exact) < 3.0 * oneSigma,
            $"transmission was {measured:P2} against a closed-form {exact:P2}, which is "
            + $"{Math.Abs(measured - exact) / oneSigma:F1} sigma and too far to be sampling");
    }

    [Fact]
    public void ASourceInsideAnElectrodeIsReportedRatherThanFlown()
    {
        // Easy to write by accident and impossible to notice afterwards: the ion
        // would be absorbed on its first step and the run would read as an
        // instrument that loses everything rather than a model with its source in
        // the metal.
        var field = Aperture(slitHalfWidth: 5e-4, plateX: 0.005);

        var result = Fly(
            field, new PhaseState(new Vec3(0.00525, 0.003, 0.0), new Vec3(1e4, 0.0, 0.0)), detectorX: 0.02);

        Assert.Equal(TrajectoryOutcome.StruckElectrode, result.Outcome);
        Assert.Equal("plateUpper", result.StruckSurface);
        Assert.Equal(0.0, result.FlightTimeSeconds);
        Assert.Equal(0, result.AcceptedSteps);
    }

    [Fact]
    public void AFieldWithNoConductorsStopsNothing()
    {
        // The analytic fields have no surfaces anywhere - a uniform field is an
        // expression valid everywhere - so nothing about this may change for them.
        // Asserted rather than assumed, because adding an event to the inner loop
        // is exactly the kind of change that alters a case nobody was looking at.
        IElectrostaticField field = UniformField.Create(new Vec3(1e4, 0.0, 0.0));

        Assert.False(field is IConductorBounded);

        var result = Fly(field, Launch(0.003, 1e4), detectorX: 0.02);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        Assert.Null(result.StruckSurface);
    }

    /// <summary>Abramowitz and Stegun 7.1.26, to about 1.5e-7.</summary>
    private static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);

        var t = 1.0 / (1.0 + (0.3275911 * x));

        var y = 1.0 - ((((((1.061405429 * t) - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
            + 0.254829592) * t * Math.Exp(-x * x);

        return sign * y;
    }
}
