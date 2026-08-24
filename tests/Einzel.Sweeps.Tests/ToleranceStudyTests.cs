using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Sweeps;

namespace Einzel.Sweeps.Tests;

/// <summary>
/// The sweep driver, exercised against a model whose figure of merit is an
/// arithmetic function of its parameters, so the right answer is known exactly
/// and the driver is the only thing under test.
/// </summary>
public sealed class ToleranceStudyTests
{
    /// <summary>
    /// A minimal valid model with three free parameters. The physics is irrelevant
    /// here — what matters is that the parameter surface is real and that
    /// validation, bounds, and derived expressions all behave as they would on a
    /// device.
    /// </summary>
    private static ModelDocument Model() => new()
    {
        SchemaVersion = "0.2",
        Name = "sweep-fixture",
        Parameters = new Dictionary<string, ParameterDocument>(StringComparer.Ordinal)
        {
            ["alpha"] = new() { Value = 100.0, Unit = "mm", Minimum = 50.0, Maximum = 150.0 },
            ["beta"] = new() { Value = 10.0, Unit = "mm", Minimum = 0.0, Maximum = 20.0 },
            ["gamma"] = new() { Value = 4000.0, Unit = "V", Minimum = 1000.0, Maximum = 8000.0 },
            ["derived"] = new() { Expression = "alpha + beta", Unit = "mm" },
        },
        Ion = new IonDocument { MassToCharge = new QuantityValue(500.0, "Da"), ChargeNumber = 1 },
        Source = new SourceDocument
        {
            Position = new VectorValue([-100.0, 0.0, 0.0], "mm"),
            Direction = new DirectionValue([1.0, 0.0, 0.0]),
            AccelerationPotential = new QuantityValue(4.0, "kV"),
        },
        Fields = [new FieldDocument { Type = "fieldFree" }],
        // The detector sits ahead of the source with its normal pointing back, so
        // the ion flies toward it. A source on the plane moving away from it, with
        // no field to turn it around, is exactly what the geometry check refuses.
        Detector = new DetectorDocument
        {
            PlanePoint = new VectorValue([0.0, 0.0, 0.0], "mm"),
            Normal = new DirectionValue([-1.0, 0.0, 0.0]),
        },
        Transport = new TransportDocument { MaximumFlightTime = new QuantityValue(1.0, "ms") },
    };

    /// <summary>alpha dominates, beta contributes a tenth, gamma nothing at all.</summary>
    private static double? Figure(CompiledModel model) =>
        (model.Parameters["alpha"].In("mm") * 1.0)
        + (model.Parameters["beta"].In("mm") * 0.1);

    private static PerturbationChannel Channel(string name, double halfWidth, string unit) =>
        new(name, Quantity.From(halfWidth, unit));

    [Fact]
    public void DerivedParametersFollowEveryDraw()
    {
        // The property the parameter surface exists to provide, checked through
        // the sweep rather than in isolation.
        var seen = new List<double>();

        ToleranceStudy.Run(
            Model(),
            [Channel("alpha", 10.0, "mm")],
            m =>
            {
                seen.Add(m.Parameters["derived"].In("mm") - m.Parameters["alpha"].In("mm"));
                return Figure(m);
            },
            draws: 20,
            oneAtATime: false);

        // derived = alpha + beta, and beta was never perturbed, so the difference
        // must be beta's nominal on every single draw.
        Assert.All(seen, d => Assert.Equal(10.0, d, 1e-9));
    }

    [Fact]
    public void OneAtATimeRanksTheBindingParameterFirst()
    {
        // Spec section 13's actual deliverable: not whether the tolerance suffices
        // but which parameter binds first.
        var result = ToleranceStudy.Run(
            Model(),
            [Channel("beta", 5.0, "mm"), Channel("alpha", 5.0, "mm"), Channel("gamma", 500.0, "V")],
            Figure,
            draws: 50);

        Assert.Equal("alpha", result.BindingChannel!.Parameter);
        Assert.Equal(["alpha", "beta", "gamma"], result.Sensitivity.Select(s => s.Parameter));

        // alpha moves the figure by its full half-width; beta by a tenth of it;
        // gamma not at all.
        Assert.Equal(5.0, result.Sensitivity[0].Swing, 1e-9);
        Assert.Equal(0.5, result.Sensitivity[1].Swing, 1e-9);
        Assert.Equal(0.0, result.Sensitivity[2].Swing, 1e-9);
    }

    [Fact]
    public void AChannelThatBreaksTheModelRanksAsMaximallySensitive()
    {
        // A geometry that does not work at all is not insensitive. Ranking it low
        // because it produced no number would hide the parameter most worth
        // controlling.
        var result = ToleranceStudy.Run(
            Model(),
            [Channel("alpha", 5.0, "mm"), Channel("beta", 5.0, "mm")],
            m => m.Parameters["alpha"].In("mm") > 104.0 ? null : Figure(m),
            draws: 10);

        Assert.Equal("alpha", result.BindingChannel!.Parameter);
        Assert.True(double.IsPositiveInfinity(result.BindingChannel.Swing));
    }

    [Fact]
    public void DrawsAreReproducibleFromTheirSeed()
    {
        // A study whose result changes between runs cannot be compared against
        // itself, which is why the seed is recorded in the manifest.
        var a = ToleranceStudy.Run(Model(), [Channel("alpha", 10.0, "mm")], Figure, draws: 50, seed: 7);
        var b = ToleranceStudy.Run(Model(), [Channel("alpha", 10.0, "mm")], Figure, draws: 50, seed: 7);
        var c = ToleranceStudy.Run(Model(), [Channel("alpha", 10.0, "mm")], Figure, draws: 50, seed: 8);

        Assert.Equal(
            a.Draws.Select(d => d.FigureOfMerit),
            b.Draws.Select(d => d.FigureOfMerit));

        Assert.NotEqual(
            a.Draws.Select(d => d.FigureOfMerit),
            c.Draws.Select(d => d.FigureOfMerit));
    }

    [Fact]
    public void AUniformChannelStaysInsideItsHalfWidth()
    {
        var result = ToleranceStudy.Run(
            Model(), [Channel("alpha", 8.0, "mm")], Figure, draws: 500, oneAtATime: false);

        foreach (var draw in result.Draws)
        {
            var alpha = draw.Parameters["alpha"].In("mm");
            Assert.InRange(alpha, 100.0 - 8.0 - 1e-9, 100.0 + 8.0 + 1e-9);
        }

        // And it actually explores the range rather than sitting near nominal.
        var spread = result.Draws.Max(d => d.Parameters["alpha"].In("mm"))
            - result.Draws.Min(d => d.Parameters["alpha"].In("mm"));

        Assert.True(spread > 14.0, $"500 uniform draws should nearly fill a 16 mm range; spread was {spread:F2} mm");
    }

    [Fact]
    public void ANormalChannelIsTruncatedAtThreeSigma()
    {
        // An unbounded draw would occasionally hand the solver a geometry that
        // does not exist, and a tolerance with a six-sigma tail is not a tolerance.
        var channel = new PerturbationChannel(
            "alpha", Quantity.From(2.0, "mm"), PerturbationDistribution.Normal);

        var result = ToleranceStudy.Run(Model(), [channel], Figure, draws: 2000, oneAtATime: false);

        foreach (var draw in result.Draws)
        {
            Assert.InRange(draw.Parameters["alpha"].In("mm"), 100.0 - 6.0 - 1e-9, 100.0 + 6.0 + 1e-9);
        }
    }

    [Fact]
    public void TheDistributionIsAQualifiedResult()
    {
        var result = ToleranceStudy.Run(
            Model(), [Channel("alpha", 10.0, "mm")], Figure, draws: 400, oneAtATime: false);

        var (value, uncertainty, evidence, warnings) = result.Distribution!;

        // A uniform half-width of 10 has standard deviation 10/sqrt(3) = 5.77, so
        // the 95 percent interval is about plus or minus 11.3.
        Assert.Equal(100.0 + 1.0, value.SiValue, 1.0);
        Assert.Equal(2.0 * 1.96 * 10.0 / Math.Sqrt(3.0), uncertainty.WidthSi, 2.0);
        Assert.Equal(400, Assert.IsType<Evidence.Ensemble>(evidence).EnsembleSize);
        Assert.DoesNotContain(warnings, w => w.Code == "DRAWS_FAILED");
    }

    [Fact]
    public void FailedDrawsBiasTheDistributionAndSaySo()
    {
        // The survivors of a tolerance study are not a fair sample of it: they are
        // the geometries that happened to work.
        var result = ToleranceStudy.Run(
            Model(),
            [Channel("alpha", 10.0, "mm")],
            m => m.Parameters["alpha"].In("mm") > 100.0 ? null : Figure(m),
            draws: 200,
            oneAtATime: false);

        Assert.True(result.Succeeded < 200);

        var (_, _, _, warnings) = result.Distribution!;
        var warning = Assert.Single(warnings, w => w.Code == "DRAWS_FAILED");
        Assert.False(warning.IsSuppressible);
    }

    [Fact]
    public void ADrawOutsideADeclaredBoundIsRecordedNotThrown()
    {
        // The tolerance range reaching past what the template says is buildable is
        // a finding, not a crash.
        var result = ToleranceStudy.Run(
            Model(), [Channel("beta", 40.0, "mm")], Figure, draws: 200, oneAtATime: false);

        Assert.Contains(result.Draws, d => d.Failure is not null && d.Failure.Contains("VALUE_OUT_OF_BOUNDS", StringComparison.Ordinal));
        Assert.Contains(result.Draws, d => d.FigureOfMerit is not null);
    }

    [Fact]
    public void PerturbingADerivedParameterIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(() => ToleranceStudy.Run(
            Model(), [Channel("derived", 1.0, "mm")], Figure, draws: 1));

        Assert.Contains("derived", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void AChannelOfTheWrongDimensionIsRefused()
    {
        var failure = Assert.Throws<EinzelException>(() => ToleranceStudy.Run(
            Model(), [Channel("alpha", 1.0, "kV")], Figure, draws: 1));

        Assert.Equal(ErrorCodes.UnitsIncompatible, failure.Error.Code);
    }

    [Fact]
    public void AnUnknownChannelListsWhatCouldBePerturbed()
    {
        var failure = Assert.Throws<EinzelException>(() => ToleranceStudy.Run(
            Model(), [Channel("alfa", 1.0, "mm")], Figure, draws: 1));

        Assert.Contains("alpha", failure.Error.Suggestion, StringComparison.Ordinal);
    }
}
