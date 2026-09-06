using System.Text.Json;

using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Results;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>energyPlateau</c>: the peak-to-peak excursion of the flight time across the energy
/// scan, as a fraction of the nominal flight time.
/// </summary>
/// <remarks>
/// <para>
/// Why it exists: a multi-reflection mirror is designed to a plateau condition - the
/// period stationary at several energies across the acceptance - and what that condition
/// bounds is the excursion, not the half-maximum width of an arrival peak. Optimising
/// <c>resolvingPower</c> for a mirror near its focus optimises a parabola's waist, which
/// is flat near its own maximum; a six-knob search on it returned its starting point to
/// thirteen digits while the third-order coefficient it was meant to remove sat at 1.2.
/// </para>
/// <para>
/// The expectations here are arithmetic the engine had no part in. The corpus's
/// single-stage reflectron is an ideal uniform retarding field with a field-free drift, so
/// its flight time is <c>2L/v + 2v/a</c> exactly, and the relative flight time against
/// energy is a weighted sum of <c>(1+d)^(-1/2)</c> and <c>(1+d)^(1/2)</c> with weights
/// set by the drift-to-depth ratio alone. At the first-order focus (drift twice the
/// penetration depth) the weights are equal and the first order cancels; at three depths
/// it does not, and the excursion is fifty times larger over the same window.
/// </para>
/// </remarks>
public sealed class FigureOfMeritPlateauTests(ITestOutputHelper output)
{
    private const double Window = 0.03;
    private const int Points = 9;

    /// <summary>
    /// The focused reflectron reproduces its closed form, and the off-focus one is far wider.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The closed form alone would pass a figure that returned the
    /// right number for the wrong reason on one model; the ratio alone would pass one off
    /// by a constant factor. Neither example's expectation involves this engine.
    /// </remarks>
    [Theory]
    [InlineData("single-stage-reflectron", 2.0)]
    [InlineData("reflectron-off-focus", 3.0)]
    public void TheIdealReflectronReproducesTheClosedFormExcursion(string example, double driftOverDepth)
    {
        var measured = FiguresOfMerit.Measure("energyPlateau", Compile(example), Window, Points);

        Assert.NotNull(measured);
        var (value, interval, evidence, _) = measured!;

        var expected = ClosedFormExcursion(driftOverDepth);

        output.WriteLine(
            $"{example}: excursion {value.SiValue:E4} against closed form {expected:E4} "
            + $"({value.SiValue / expected - 1.0:+0.0e+00}); grid bound +/-{interval.WidthSi / 2:E2}; {evidence}");

        // An analytic field: the only error is the integrator's, at parts in 1e10 of the
        // flight time, so the excursion is known to parts in 1e6 of itself.
        Assert.Equal(expected, value.SiValue, expected * 1e-4);

        // GRD-1: the envelope is the largest adjacent step, which cannot exceed the whole
        // excursion and must not be zero - a zero here would mean two samples coincided.
        Assert.InRange(interval.WidthSi / 2, 1e-12, value.SiValue);
        Assert.Equal(1.0, interval.ConfidenceLevel);

        var ensemble = Assert.IsType<Evidence.Ensemble>(evidence);
        Assert.Equal(Points, ensemble.EnsembleSize);
        Assert.True(ensemble.Converged);
    }

    /// <summary>The focused mirror's plateau is an order of magnitude narrower than the off-focus one.</summary>
    [Fact]
    public void FirstOrderFocusNarrowsThePlateauByAnOrderOfMagnitude()
    {
        var focused = Plateau("single-stage-reflectron");
        var offFocus = Plateau("reflectron-off-focus");

        output.WriteLine($"focused {focused:E3}, off focus {offFocus:E3}, ratio {offFocus / focused:F1}");

        // Closed form: 6.01e-3 / 1.16e-4 = 51.8. Asserted loosely because the ratio is not
        // the measurement, the closed forms above are; this is the sanity check that the
        // figure orders two designs the way a designer would.
        Assert.InRange(offFocus / focused, 40.0, 60.0);
    }

    /// <summary>
    /// The bare evaluator the study drivers use and the GRD-1 measure agree to the bit.
    /// </summary>
    /// <remarks>
    /// The two are one computation behind two doors. If they ever drift - the second time
    /// <c>run</c> and <c>test</c> did exactly that here - an optimiser would minimise one
    /// quantity and a result page would report another.
    /// </remarks>
    [Fact]
    public void TheEvaluatorAndTheMeasureAreTheSameNumber()
    {
        var model = Compile("single-stage-reflectron");

        var bare = FiguresOfMerit.Evaluator("energyPlateau", Window, Points)(model);
        var (value, _, _, _) = FiguresOfMerit.Measure("energyPlateau", model, Window, Points)!;

        Assert.NotNull(bare);
        Assert.Equal(value.SiValue, bare!.Value);
    }

    /// <summary>
    /// Resolving power over the same window can never be below one over twice the plateau.
    /// </summary>
    /// <remarks>
    /// A half-maximum width is never wider than the whole excursion, so the two figures are
    /// bound to each other by an inequality that holds whatever the peak's shape. On a
    /// parabolic plateau the half-maximum figure reads about three times higher, which is
    /// why the two must not be compared as though they were the same quantity; this test
    /// is what stops them being mutually inconsistent.
    /// </remarks>
    [Theory]
    [InlineData("single-stage-reflectron")]
    [InlineData("reflectron-off-focus")]
    public void ResolvingPowerIsNeverBelowOneOverTwiceThePlateau(string example)
    {
        var model = Compile(example);

        var plateau = FiguresOfMerit.Evaluator("energyPlateau", Window, Points)(model)!.Value;
        var resolving = FiguresOfMerit.Evaluator("resolvingPower", Window, Points)(model)!.Value;

        output.WriteLine($"{example}: R {resolving:F0}, 1/(2 x plateau) {1.0 / (2.0 * plateau):F0}");

        Assert.True(resolving >= 1.0 / (2.0 * plateau) * (1.0 - 1e-9));
    }

    /// <summary>No spread, no plateau: absent rather than zero.</summary>
    [Fact]
    public void AZeroSpreadGivesNoFigureRatherThanZero()
    {
        var model = Compile("single-stage-reflectron");

        Assert.Null(FiguresOfMerit.Evaluator("energyPlateau", energySpread: 0.0)(model));
        Assert.Null(FiguresOfMerit.Measure("energyPlateau", model, energySpread: 0.0));
    }

    /// <summary>
    /// The peak-to-peak of <c>w (1+d)^(-1/2) + (1-w)(1+d)^(1/2)</c> over the scan's own
    /// grid, where <c>w = r/(r+2)</c> for a drift of <c>r</c> penetration depths.
    /// </summary>
    private static double ClosedFormExcursion(double driftOverDepth)
    {
        var drift = driftOverDepth / (driftOverDepth + 2.0);
        var mirror = 1.0 - drift;

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var k = 0; k < Points; k++)
        {
            var d = Window * ((2.0 * k / (Points - 1.0)) - 1.0);
            var relative = (drift / Math.Sqrt(1.0 + d)) + (mirror * Math.Sqrt(1.0 + d));
            min = Math.Min(min, relative);
            max = Math.Max(max, relative);
        }

        return max - min;
    }

    private static double Plateau(string example) =>
        FiguresOfMerit.Evaluator("energyPlateau", Window, Points)(Compile(example))!.Value;

    private static CompiledModel Compile(string example)
    {
        var document = JsonSerializer.Deserialize<ModelDocument>(
            ExampleModels.Read(example), Io.ModelJson.Options)!;
        var (model, errors) = ModelValidator.Validate(document);

        Assert.True(
            errors.Count == 0,
            string.Join("; ", errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return model!;
    }
}
