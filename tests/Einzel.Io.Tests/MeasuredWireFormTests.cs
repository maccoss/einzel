using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// The GRD-1 envelope where it leaves the process.
/// </summary>
/// <remarks>
/// The rule is only worth anything if it survives the boundary. A result that
/// carries its uncertainty, its evidence, and its warnings inside the engine and
/// arrives on the wire as a bare number would satisfy GRD-1 in the place it is
/// easiest to satisfy and break it in the place it matters.
/// </remarks>
public sealed class MeasuredWireFormTests(ITestOutputHelper output)
{
    private static Measured Envelope(Evidence evidence, params ValidityWarning[] warnings) => new(
        Quantity.From(19800.0, "1"),
        UncertaintyInterval.Symmetric(Quantity.From(19800.0, "1"), Quantity.From(400.0, "1"), 0.95),
        evidence,
        warnings);

    public static TheoryData<Evidence, string> EveryEvidenceKind => new()
    {
        { new Evidence.Ensemble(1000, Converged: true), "ensemble" },
        { new Evidence.Convergence("grid spacing", 1.996, 2.0, 1.2e-9), "convergence" },
        { new Evidence.Search(45, Converged: true, SpreadSi: 6.1e-6), "search" },
        { new Evidence.Analytic("single-stage reflectron"), "analytic" },
    };

    [Theory]
    [MemberData(nameof(EveryEvidenceKind))]
    public void EveryEvidenceKindHasAWireForm(Evidence evidence, string expected)
    {
        // EvidenceJson.From throws on an unhandled kind rather than emitting
        // something lossy, so a case added to the hierarchy and forgotten here
        // fails loudly. This is what makes that guarantee worth having.
        var wire = MeasuredJson.From(Envelope(evidence), "1");

        output.WriteLine($"{evidence.GetType().Name} -> {wire.Evidence.Kind}");

        Assert.Equal(expected, wire.Evidence.Kind);
    }

    [Fact]
    public void ASearchCarriesItsEvaluationsAndItsSpread()
    {
        var wire = MeasuredJson.From(
            Envelope(new Evidence.Search(45, Converged: false, SpreadSi: 6.1e-6)), "1");

        Assert.Equal("search", wire.Evidence.Kind);
        Assert.Equal(45, wire.Evidence.EnsembleSize);
        Assert.False(wire.Evidence.Converged);
        Assert.Equal(6.1e-6, wire.Evidence.Residual!.Value, 1e-12);
    }

    [Fact]
    public void WarningsReachTheWireWithTheirSuppressibilityIntact()
    {
        // GRD-2 and GRD-3. A consumer downstream has to be able to tell that it
        // may not silence something, and the only way it can is if the flag
        // travels rather than being re-derived from a severity name it might not
        // recognise.
        var wire = MeasuredJson.From(
            Envelope(
                new Evidence.Search(45, Converged: false, SpreadSi: 6.1e-6),
                new ValidityWarning("optimiser.optimum-at-bound", "sits on a bound", WarningSeverity.Qualified),
                new ValidityWarning("advice", "consider a finer mesh", WarningSeverity.Advisory)),
            "1");

        foreach (var warning in wire.Warnings)
        {
            output.WriteLine($"{warning.Code} [{warning.Severity}] suppressible {warning.Suppressible}");
        }

        Assert.Equal(2, wire.Warnings.Count);
        Assert.False(wire.Warnings[0].Suppressible);
        Assert.True(wire.Warnings[1].Suppressible);
    }

    [Fact]
    public void ConvergenceWithNothingToReportSerialisesRatherThanThrowing()
    {
        // JSON has no NaN and System.Text.Json throws rather than inventing one,
        // so a single unreportable number takes the whole document down instead of
        // the field. A convergence with no order and no residual is an ordinary
        // thing - a preview has no study behind it, and a run whose refinements
        // agreed to the last bit has no order to resolve.
        var wire = MeasuredJson.From(
            Envelope(new Evidence.Convergence("integrator tolerance", double.NaN, 5.0, double.NaN)), "1");

        Assert.Null(wire.Evidence.ObservedOrder);
        Assert.Null(wire.Evidence.Residual);
        Assert.Equal(5.0, wire.Evidence.NominalOrder);

        // Serialising without throwing is the assertion; before the guard this
        // line threw and took the document with it.
        var text = System.Text.Json.JsonSerializer.Serialize(wire);
        output.WriteLine(text);

        Assert.DoesNotContain("NaN", text, StringComparison.Ordinal);

        using var parsed = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, parsed.RootElement.ValueKind);
    }

    [Fact]
    public void TheIntervalIsConvertedWithTheValue()
    {
        // An interval left in SI beside a value converted to millimetres is a
        // plausible-looking envelope that is wrong by three orders, and nothing
        // about the number would say so.
        var length = new Measured(
            Quantity.From(290.4, "mm"),
            UncertaintyInterval.Symmetric(Quantity.From(290.4, "mm"), Quantity.From(0.5, "mm"), 0.95),
            new Evidence.Search(45, Converged: true, SpreadSi: 1e-3));

        var wire = MeasuredJson.From(length, "mm");

        output.WriteLine($"{wire.Value:F3} [{wire.Uncertainty.Lower:F3}, {wire.Uncertainty.Upper:F3}] {wire.Unit}");

        Assert.Equal(290.4, wire.Value, 1e-9);
        Assert.Equal(289.9, wire.Uncertainty.Lower, 1e-9);
        Assert.Equal(290.9, wire.Uncertainty.Upper, 1e-9);
    }
}
