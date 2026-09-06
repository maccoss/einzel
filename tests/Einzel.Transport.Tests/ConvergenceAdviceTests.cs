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
/// <b>The advice is tested directly and the firing is not, and that is deliberate.</b> A
/// first version drove the whole ladder onto a floor and asserted the message. It passed on
/// Windows and failed on Linux, because reaching a floor means the flight time is being set
/// by something other than the tolerance, and which side of the order threshold that lands
/// on is a fit to noise - amplified by <c>Math.Pow</c> in the step controller, which is not
/// bit-identical across platforms. Which advice a <em>field</em> earns is exactly determined;
/// whether a given model floors is not. So the branch is tested for what it is, and the
/// end-to-end test asks only that <em>some</em> configuration of several floors, which takes
/// one of eight rather than a particular one.
/// </para>
/// </remarks>
public sealed class ConvergenceAdviceTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private const string Refine = "finer grid";
    private const string NoGrid = "no grid to refine";

    /// <summary>A field that claims whatever resolution it is told to.</summary>
    private sealed class Claiming(IElectrostaticField inner, double resolution) : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            inner.FieldFreeRunLength(position, direction);

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            inner.SignedDistanceToDiscontinuity(position);

        public double ResolutionLength => resolution;
    }

    private static IdealSingleStageReflectron Reflectron() =>
        IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

    /// <summary>An analytic field is not told to refine a grid it does not have.</summary>
    [Fact]
    public void AnAnalyticFieldIsNotToldToRefineAGrid()
    {
        var advice = FlightTimeStudy.ConvergenceFloorAdvice(Reflectron().Field);
        output.WriteLine(advice);

        Assert.DoesNotContain(Refine, advice, StringComparison.Ordinal);
        Assert.Contains("analytic", advice, StringComparison.Ordinal);

        // And it names what the floor actually is, so the reader has somewhere to go
        // instead of somewhere they cannot go.
        Assert.Contains("discontinuity", advice, StringComparison.Ordinal);
    }

    /// <summary>
    /// A gridded field still is, which is the control. Fixing one arm by silencing the other
    /// would be no fix: in a solved field the interpolation error is the usual floor and
    /// refining is genuinely the remedy.
    /// </summary>
    [Fact]
    public void AGriddedFieldIsStillToldToRefine()
    {
        var advice = FlightTimeStudy.ConvergenceFloorAdvice(
            new Claiming(Reflectron().Field, 1.0e-3));

        output.WriteLine(advice);

        Assert.Contains(Refine, advice, StringComparison.Ordinal);
        Assert.DoesNotContain(NoGrid, advice, StringComparison.Ordinal);
    }

    /// <summary>Each remedy belongs to exactly one kind of field.</summary>
    /// <remarks>
    /// Written after a weaker version survived the mutation. It had asserted only that the
    /// two messages differ - which they do even with the branch removed, because each quotes
    /// its own observed order, so a numeric difference was standing in for the difference
    /// under test.
    /// </remarks>
    [Fact]
    public void EachRemedyBelongsToExactlyOneKindOfField()
    {
        var field = Reflectron().Field;

        string[] both =
        [
            FlightTimeStudy.ConvergenceFloorAdvice(field),
            FlightTimeStudy.ConvergenceFloorAdvice(new Claiming(field, 1.0e-3)),
        ];

        Assert.Equal(1, both.Count(a => a.Contains(Refine, StringComparison.Ordinal)));
        Assert.Equal(1, both.Count(a => a.Contains(NoGrid, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Zero is not a resolution. A field reporting it is claiming no length scale at all
    /// rather than an infinitely fine one, and gets the analytic advice.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void OnlyAFinitePositiveResolutionCountsAsAGrid(double resolution)
    {
        var advice = FlightTimeStudy.ConvergenceFloorAdvice(
            new Claiming(Reflectron().Field, resolution));

        Assert.Contains(NoGrid, advice, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the warning carries the advice its own field earns, rather than a second copy of
    /// the sentence that could drift from it.
    /// </summary>
    /// <remarks>
    /// The claimed resolution is swept rather than fixed because whether a particular one
    /// puts the ladder on a floor is not stable - see the note on this class. What is
    /// asserted is that at least one of seventeen does, and that every one that fires ends
    /// with the advice for the field it was flown through.
    /// </remarks>
    [Fact]
    public void TheWarningCarriesTheAdviceItsOwnFieldEarns()
    {
        double[] claims =
        [
            double.PositiveInfinity,
            3.0e-2, 2.0e-2, 1.5e-2, 1.0e-2, 7.0e-3, 5.0e-3, 3.0e-3, 2.0e-3,
            1.5e-3, 1.0e-3, 7.0e-4, 5.0e-4, 3.0e-4, 2.0e-4, 1.5e-4, 1.0e-4,
        ];

        var fired = 0;

        foreach (var claim in claims)
        {
            var reflectron = Reflectron();
            var field = new Claiming(reflectron.Field, claim);

            var study = FlightTimeStudy.Run(
                reflectron.LaunchState(), reflectron.Species, field,
                reflectron.Settings(), reflectron.DetectorPlane());

            var warning = study.FlightTime.Warnings.FirstOrDefault(
                w => w.Code == "CONVERGENCE_ORDER_BELOW_NOMINAL");

            if (warning is null)
            {
                continue;
            }

            fired++;
            output.WriteLine($"claimed {claim}: {warning.Message}");

            Assert.EndsWith(
                FlightTimeStudy.ConvergenceFloorAdvice(field), warning.Message,
                StringComparison.Ordinal);
        }

        Assert.True(fired > 0, "no configuration reached a floor, so nothing was asserted");
        output.WriteLine($"{fired} of {claims.Length} reached a floor");
    }
}
