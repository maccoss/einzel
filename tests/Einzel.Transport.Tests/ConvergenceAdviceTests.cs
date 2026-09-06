using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// <c>CONVERGENCE_ORDER_BELOW_NOMINAL</c> told every reader the fix was a finer grid, and
/// for an analytic field there is no grid.
/// </summary>
/// <remarks>
/// <para>
/// Found by an agent working the acceptance suite on an analytic reflectron: the warning
/// fired on 578 of 1505 evaluations prescribing a remedy that model cannot take, and the
/// agent spent a deliberate second pass establishing that the number it was about to quote
/// was sound. GRD-3 makes this class unsuppressible, which is exactly why it must not cry
/// wolf - an unsuppressible warning that cannot be acted on teaches people to skim the one
/// class that must never be skimmed.
/// </para>
/// <para>
/// The two runs here differ in <em>nothing but what the field says about its own
/// resolution</em>. Same geometry, same straddled discontinuity, same ladder. That is what
/// makes this a test of the branch rather than of two unrelated models.
/// </para>
/// </remarks>
public sealed class ConvergenceAdviceTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>
    /// A real field with its discontinuity concealed, and a resolution it can be told to
    /// claim.
    /// </summary>
    /// <remarks>
    /// Concealing the surface is how the ladder is put on a floor honestly rather than by
    /// injecting noise: every stage of a step lands on whichever side its own sample falls,
    /// so the error stops shrinking with tolerance. It is the second case the new message
    /// names - "a discontinuity the ladder is straddling" - reproduced rather than described.
    /// </remarks>
    private sealed class Rough(IElectrostaticField inner, double resolution) : IElectrostaticField
    {
        // One part in ten million, on ten microns - far below any step the controller will
        // take over a 300 mm path, and far above double precision. Which stages happen to
        // land in which half then depends on the step sequence, and the step sequence changes
        // with the tolerance, so the flight time moves by a fixed amount however tight the
        // tolerance goes. That is a floor other than the integrator, which is what the
        // warning is for.
        private const double Amplitude = 1.0e-7;
        private const double Period = 1.0e-5;

        private static double Wobble(double x) =>
            1.0 + (Amplitude * Math.Sign(Math.Sin(2.0 * Math.PI * x / Period)));

        public Vec3 ElectricFieldAt(in Vec3 position) =>
            inner.ElectricFieldAt(position) * Wobble(position.X);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            inner.FieldFreeRunLength(position, direction);

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            inner.SignedDistanceToDiscontinuity(position);

        public double ResolutionLength => resolution;
    }

    private static (bool Fired, string Message) Ladder(double resolution)
    {
        var reflectron = IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

        var study = FlightTimeStudy.Run(
            reflectron.LaunchState(), reflectron.Species,
            new Rough(reflectron.Field, resolution),
            reflectron.Settings(), reflectron.DetectorPlane());

        var warning = study.FlightTime.Warnings.FirstOrDefault(
            w => w.Code == "CONVERGENCE_ORDER_BELOW_NOMINAL");

        return (warning is not null, warning?.Message ?? string.Empty);
    }

    /// <summary>An analytic field is not told to refine a grid it does not have.</summary>
    [Fact]
    public void AnAnalyticFieldIsNotToldToRefineAGrid()
    {
        var (fired, message) = Ladder(double.PositiveInfinity);
        output.WriteLine(message);

        Assert.True(fired, "the ladder did not reach its floor, so there is no advice to check");
        Assert.DoesNotContain("finer grid", message, StringComparison.Ordinal);
        Assert.Contains("analytic", message, StringComparison.Ordinal);

        // And it names what the floor actually is, so the reader has somewhere to go.
        Assert.Contains("discontinuity", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A gridded field still is, which is the control. Fixing one arm by silencing the
    /// other would be no fix: in a solved field the interpolation error is the usual floor
    /// and refining is genuinely the remedy.
    /// </summary>
    [Fact]
    public void AGriddedFieldIsStillToldToRefine()
    {
        var (fired, message) = Ladder(1.0e-3);
        output.WriteLine(message);

        Assert.True(fired, "the ladder did not reach its floor, so there is no advice to check");
        Assert.Contains("finer grid", message, StringComparison.Ordinal);
        Assert.DoesNotContain("no grid to refine", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each remedy appears on exactly one of the two, and the finding appears on both.
    /// </summary>
    /// <remarks>
    /// Written after a weaker version of this test survived the mutation. It had asserted
    /// only that the two messages differ - which they do even with the branch removed,
    /// because each quotes its own observed order, so a numeric difference was standing in
    /// for the difference under test. Asserting that each remedy appears exactly once is
    /// what actually discriminates.
    /// </remarks>
    [Fact]
    public void EachRemedyBelongsToExactlyOneKindOfField()
    {
        var analytic = Ladder(double.PositiveInfinity).Message;
        var gridded = Ladder(1.0e-3).Message;

        const string Shared = "the flight time stopped improving as fast as the tolerance tightened";
        const string Refine = "finer grid";
        const string NoGrid = "no grid to refine";

        // The finding is the same in both: the ladder hit a floor.
        Assert.Contains(Shared, analytic, StringComparison.Ordinal);
        Assert.Contains(Shared, gridded, StringComparison.Ordinal);

        // The remedy is not.
        var refines = new[] { analytic, gridded }.Count(m => m.Contains(Refine, StringComparison.Ordinal));
        var noGrids = new[] { analytic, gridded }.Count(m => m.Contains(NoGrid, StringComparison.Ordinal));

        Assert.Equal(1, refines);
        Assert.Equal(1, noGrids);
    }
}
