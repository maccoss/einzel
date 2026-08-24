using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// ACC-3, and the claim behind it: on a trajectory the interpolant, not the
/// integrator, sets the error floor.
/// </summary>
public sealed class InterpolationTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static HarmonicReference Reference => new(amplitude: 100.0, wavenumber: Math.PI / 0.1);

    /// <summary>Launches across the box and stops just before the far edge.</summary>
    private static (PhaseState Launch, TrajectoryStopFunction Stop, IntegrationSettings Settings) Traverse()
    {
        var speed = Peptide.SpeedAfterAcceleration(Quantity.From(1.0, "kV")).SiValue;
        var launch = new PhaseState(new Vec3(0.01, 0.05, 0.0), new Vec3(speed, 0.0, 0.0));

        TrajectoryStopFunction stop = (in PhaseState s) => 0.09 - s.Position.X;
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-4 };

        return (launch, stop, settings);
    }

    [Fact]
    public void BicubicBeatsBilinearOnATrajectoryByOrdersOfMagnitude()
    {
        // The spec's reasoning, measured rather than asserted. An ion crossing a
        // gridded potential picks up error at every cell boundary from the
        // interpolant's derivative jump, and because the sign of that jump follows
        // the direction of travel the error accumulates instead of cancelling.
        var reference = Reference;
        var (launch, stop, settings) = Traverse();

        var exact = TrajectoryIntegrator.Integrate(launch, Peptide, reference, settings, stop);
        Assert.Equal(TrajectoryOutcome.StopConditionMet, exact.Outcome);

        output.WriteLine($"analytic field: {exact.FlightTimeSeconds * 1e9:F6} ns, {exact.AcceptedSteps} steps");
        output.WriteLine(string.Empty);
        output.WriteLine("intervals   cells crossed   bicubic (C1)      bilinear (C0)     ratio");

        var bicubicErrors = new List<(int Intervals, double Error)>();
        var bilinearErrors = new List<(int Intervals, double Error)>();

        foreach (var intervals in new[] { 64, 128, 256 })
        {
            var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.1, intervals);

            // The exact potential on the grid nodes, not a solved one: this test
            // is about the interpolant, and a solved field would contribute its
            // own O(h^2) discretisation error on top.
            var sampled = reference.SampleOn(grid);

            var bicubic = new SolvedField2D(sampled, new BicubicInterpolant(sampled));

            // The escape hatch exists only here: measuring what the forbidden
            // interpolant costs is the reason it is forbidden.
            var bilinear = new SolvedField2D(
                sampled, new BilinearInterpolant(sampled), allowDiscontinuousDerivatives: true);

            var withBicubic = TrajectoryIntegrator.Integrate(launch, Peptide, bicubic, settings, stop);
            var withBilinear = TrajectoryIntegrator.Integrate(launch, Peptide, bilinear, settings, stop);

            var bicubicError = RelativeError(withBicubic.FlightTimeSeconds, exact.FlightTimeSeconds);
            var bilinearError = RelativeError(withBilinear.FlightTimeSeconds, exact.FlightTimeSeconds);

            output.WriteLine(
                $"{intervals,9}   {0.08 / grid.Spacing,12:F0}   {bicubicError,14:E3}   {bilinearError,14:E3}"
                + $"   {bilinearError / bicubicError,7:F1}x");

            bicubicErrors.Add((intervals, bicubicError));
            bilinearErrors.Add((intervals, bilinearError));

            Assert.True(
                bicubicError < bilinearError,
                $"at {intervals} intervals bicubic ({bicubicError:E3}) was not better than bilinear "
                + $"({bilinearError:E3})");

            // ACC-3 budgets the interpolation contribution at half of ACC-1, so
            // 5e-7 relative. The permitted interpolant meets it on every grid here,
            // including the coarsest.
            Assert.True(
                bicubicError < 5e-7,
                $"bicubic contributed {bicubicError:E3} at {intervals} intervals, over the ACC-3 budget of 5e-7");
        }

        // And the measured cost of the forbidden one, which is why the spec bans
        // it rather than merely discouraging it: on the coarsest grid here it is
        // not only over the ACC-3 interpolation budget but over the entire ACC-1
        // flight-time budget of 1 ppm, by more than an order of magnitude.
        Assert.True(
            bilinearErrors[0].Error > 1e-6,
            $"bilinear contributed only {bilinearErrors[0].Error:E3} at {bilinearErrors[0].Intervals} intervals, "
            + "which would undercut the case for banning it");

        // And the permitted interpolant converges as the grid refines, which is
        // what makes ACC-3 a budget one can meet by refining rather than a floor
        // one is stuck with.
        for (var k = 1; k < bicubicErrors.Count; k++)
        {
            Assert.True(
                bicubicErrors[k].Error < bicubicErrors[k - 1].Error,
                $"bicubic error grew from {bicubicErrors[k - 1].Error:E3} at {bicubicErrors[k - 1].Intervals} "
                + $"to {bicubicErrors[k].Error:E3} at {bicubicErrors[k].Intervals} intervals");
        }
    }

    [Fact]
    public void ABilinearInterpolantIsRefusedOnATrajectoryPath()
    {
        // ACC-3 stated as a type error rather than a comment. The escape hatch is
        // deliberately awkward to reach and named for what it does.
        var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.1, intervalsX: 32);
        var (solved, _) = Reference.SolveOn(grid);

        var failure = Assert.Throws<ArgumentException>(
            () => new SolvedField2D(solved, new BilinearInterpolant(solved)));

        Assert.Contains("ACC-3", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BicubicHasContinuousFirstDerivativesAcrossCellBoundaries()
    {
        // The property ACC-3 actually requires. Sampling either side of a node
        // shows the field jump the two schemes produce; a discontinuous field is
        // what turns interpolation error systematic.
        var grid = Grid2D.OverBox(0.0, 0.0, 0.1, 0.1, intervalsX: 64);
        var (solved, _) = Reference.SolveOn(grid);

        var bicubic = new BicubicInterpolant(solved);
        var bilinear = new BilinearInterpolant(solved);

        var worstBicubic = 0.0;
        var worstBilinear = 0.0;
        const double epsilon = 1e-9;

        // Walk across interior cell boundaries along a line of constant y.
        for (var i = 8; i < grid.CountX - 8; i++)
        {
            var x = grid.X(i);
            const double y = 0.0503;

            worstBicubic = Math.Max(worstBicubic, Jump(bicubic, x, y, epsilon));
            worstBilinear = Math.Max(worstBilinear, Jump(bilinear, x, y, epsilon));
        }

        output.WriteLine($"worst dPhi/dx jump across a node: bicubic {worstBicubic:E3}, bilinear {worstBilinear:E3} V/m");

        Assert.True(
            worstBicubic < 1e-3,
            $"bicubic should be C1, but dPhi/dx jumps by {worstBicubic:E3} V/m at a cell boundary");

        Assert.True(
            worstBilinear > 100.0 * Math.Max(worstBicubic, 1e-6),
            "the comparison is meaningless unless bilinear really is discontinuous here; "
            + $"it jumped by only {worstBilinear:E3} V/m");

        static double Jump(IFieldInterpolant interpolant, double x, double y, double epsilon)
        {
            interpolant.Gradient(x - epsilon, y, out var beforeX, out _);
            interpolant.Gradient(x + epsilon, y, out var afterX, out _);
            return Math.Abs(afterX - beforeX);
        }
    }

    private static double RelativeError(double value, double reference) =>
        Math.Abs(value - reference) / Math.Abs(reference);
}
