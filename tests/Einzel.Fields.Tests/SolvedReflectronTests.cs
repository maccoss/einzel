using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The cross-check the whole stage exists for: the same instrument computed two
/// independent ways.
/// </summary>
/// <remarks>
/// <para>
/// One path is the closed form — a half-space of uniform gradient, whose
/// trajectory is exactly solvable. The other goes through the full numerical
/// stack: a Dirichlet geometry, a multigrid solve, a bicubic interpolant, and the
/// adaptive integrator. Nothing is shared between them but the physics, so
/// agreement is evidence and disagreement localises a bug.
/// </para>
/// <para>
/// This is what spec section 19's cross-code tier would otherwise provide. With
/// no SIMION licence available, agreement between an analytic path and a solved
/// path carries that weight instead, which is a reason to keep the analytic tier
/// sharp rather than to treat it as a formality.
/// </para>
/// </remarks>
public sealed class SolvedReflectronTests(ITestOutputHelper output)
{
    // The memo's design point B: m/z 500 at 4 keV, turning 50 mm into the mirror.
    private const double AccelerationVolts = 4000.0;
    private const double TurningDepth = 0.05;
    private const double GradientVoltsPerMetre = AccelerationVolts / TurningDepth;
    private const double DriftLength = 2.0 * TurningDepth;

    // The mirror is solved a little deeper than the ion turns, so the turning
    // point sits inside the domain rather than on its far boundary.
    private const double MirrorDepth = 0.06;

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static double ExactFlightTime()
    {
        var species = Peptide;
        var speed = Math.Sqrt(2.0 * Math.Abs(species.ChargeSi) * AccelerationVolts / species.MassSi);
        var acceleration = Math.Abs(species.ChargeSi) * GradientVoltsPerMetre / species.MassSi;

        return (2.0 * DriftLength / speed) + (2.0 * speed / acceleration);
    }

    private static (ScalarField2D Potential, SolveReport Report) SolveMirror(int intervalsX)
    {
        var grid = Grid2D.OverBox(0.0, -0.05, MirrorDepth, 0.05, intervalsX);
        var mask = new DirichletMask(grid)
        {
            // No y dependence is imposed, so the exact interior solution is a pure
            // ramp in x — the ideal single-stage mirror, built out of a boundary
            // value problem instead of asserted as a formula.
            TopEdge = EdgeCondition.Neumann,
            BottomEdge = EdgeCondition.Neumann,
        };

        for (var j = 0; j < grid.CountY; j++)
        {
            mask.Fix(0, j, 0.0);
            mask.Fix(grid.CountX - 1, j, GradientVoltsPerMetre * MirrorDepth);
        }

        return PoissonSolver2D.Solve(mask, tolerance: 1e-13, maximumCycles: 400);
    }

    [Fact]
    public void SolvedMirrorReproducesTheAnalyticFlightTime()
    {
        var (potential, report) = SolveMirror(intervalsX: 128);
        Assert.True(report.Converged, $"mirror solve did not converge: {report}");

        var field = new SolvedField2D(potential, new BicubicInterpolant(potential));
        var species = Peptide;
        var speed = Math.Sqrt(2.0 * Math.Abs(species.ChargeSi) * AccelerationVolts / species.MassSi);

        var launch = new PhaseState(new Vec3(-DriftLength, 0.0, 0.0), new Vec3(speed, 0.0, 0.0));
        TrajectoryStopFunction detector = (in PhaseState s) => s.Position.X + DriftLength;

        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3 };
        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);

        var exact = ExactFlightTime();
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        output.WriteLine($"analytic  {exact * 1e6:F9} us");
        output.WriteLine($"solved    {result.FlightTimeSeconds * 1e6:F9} us");
        output.WriteLine($"relative  {relative:E3}  ({relative * 1e6:F4} ppm against the ACC-1 budget of 1 ppm)");
        output.WriteLine($"energy    {result.MaximumRelativeEnergyDrift:E3} drift");
        output.WriteLine($"steps     {result.AcceptedSteps}, {result.AnalyticDriftDistance:F4} m analytic");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        Assert.True(
            relative < 1e-6,
            $"the solved mirror missed the closed form by {relative * 1e6:F4} ppm, over the ACC-1 budget");

        Assert.True(
            result.MaximumRelativeEnergyDrift < 1e-6,
            $"energy drift {result.MaximumRelativeEnergyDrift:E3} exceeds the ACC-4 budget");
    }

    [Fact]
    public void SolvedAndAnalyticMirrorsAgreeOnTheFieldItself()
    {
        // Before comparing trajectories, compare what they are trajectories
        // through: a disagreement here localises the fault to the solver or the
        // interpolant rather than the integrator.
        var (potential, _) = SolveMirror(intervalsX: 128);
        var solved = new SolvedField2D(potential, new BicubicInterpolant(potential));

        var analytic = HalfSpaceUniformField.Create(
            Vec3.Zero, Vec3.UnitX, Quantity.Si(GradientVoltsPerMetre, Dimension.ElectricField));

        var worstField = 0.0;
        var worstPotential = 0.0;

        for (var depth = 0.002; depth < MirrorDepth - 0.002; depth += 0.001)
        {
            for (var y = -0.03; y <= 0.03; y += 0.01)
            {
                var point = new Vec3(depth, y, 0.0);

                var solvedField = solved.ElectricFieldAt(in point);
                var analyticField = analytic.ElectricFieldAt(in point);

                worstField = Math.Max(worstField, (solvedField - analyticField).Length);
                worstPotential = Math.Max(
                    worstPotential, Math.Abs(solved.PotentialAt(in point) - analytic.PotentialAt(in point)));
            }
        }

        output.WriteLine($"worst field difference     {worstField:E3} V/m on {GradientVoltsPerMetre:G6} V/m");
        output.WriteLine($"worst potential difference {worstPotential:E3} V");

        Assert.True(
            worstField / GradientVoltsPerMetre < 1e-9,
            $"solved and analytic fields differ by {worstField:E3} V/m");
    }

    [Fact]
    public void RefiningTheGridDoesNotMoveTheAnswer()
    {
        // Grid convergence as a first-class test, per spec section 8. For this
        // geometry the exact solution is linear, which both the five-point stencil
        // and the bicubic interpolant represent exactly, so refinement should
        // change nothing — and a result that drifts with resolution would mean
        // something in the stack is resolution-dependent that should not be.
        var exact = ExactFlightTime();
        var species = Peptide;
        var speed = Math.Sqrt(2.0 * Math.Abs(species.ChargeSi) * AccelerationVolts / species.MassSi);

        var launch = new PhaseState(new Vec3(-DriftLength, 0.0, 0.0), new Vec3(speed, 0.0, 0.0));
        TrajectoryStopFunction detector = (in PhaseState s) => s.Position.X + DriftLength;
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3 };

        foreach (var intervals in new[] { 32, 64, 128 })
        {
            var (potential, report) = SolveMirror(intervals);
            Assert.True(report.Converged);

            var field = new SolvedField2D(potential, new BicubicInterpolant(potential));
            var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);
            var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

            output.WriteLine($"{intervals,4} intervals (h = {potential.Grid.SpacingX * 1e3:F4} mm): {relative:E3}");

            Assert.True(
                relative < 1e-6,
                $"at {intervals} intervals the solved mirror missed the closed form by {relative * 1e6:F4} ppm");
        }
    }
}
