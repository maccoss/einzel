using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Results;

public sealed class MeasuredTests
{
    private static Measured ResolvingPower(params ValidityWarning[] warnings) => new(
        Quantity.Number(19800),
        UncertaintyInterval.Symmetric(Quantity.Number(19800), Quantity.Number(400), 0.95),
        new Evidence.Ensemble(EnsembleSize: 1000, Converged: true),
        warnings);

    [Fact]
    public void DeconstructionYieldsTheWholeEnvelope()
    {
        var (value, uncertainty, evidence, warnings) = ResolvingPower();

        Assert.Equal(19800.0, value.SiValue);
        Assert.Equal(19400.0, uncertainty.LowerSi);
        Assert.Equal(20200.0, uncertainty.UpperSi);
        Assert.Equal(0.95, uncertainty.ConfidenceLevel);
        Assert.Equal(1000, Assert.IsType<Evidence.Ensemble>(evidence).EnsembleSize);
        Assert.Empty(warnings);
    }

    [Fact]
    public void FormattingCarriesUncertaintyAndEvidence()
    {
        // The spec's own worked contrast: 19,800 alone versus 19,800 with n,
        // an interval, and any warnings.
        var text = ResolvingPower().Format("1");

        Assert.Contains("19800", text, StringComparison.Ordinal);
        Assert.Contains("400", text, StringComparison.Ordinal);
        Assert.Contains("95", text, StringComparison.Ordinal);
        Assert.Contains("n = 1000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidityViolationsAreNotSuppressible()
    {
        // GRD-3: validity violations cannot be silenced by any caller, including
        // in batch mode.
        var warning = new ValidityWarning(
            "PSEUDOPOTENTIAL_VALIDITY_EXCEEDED",
            "pseudopotential validity exceeded over part of the path",
            WarningSeverity.ValidityViolation);

        Assert.False(warning.IsSuppressible);
        Assert.True(ResolvingPower(warning).HasNonSuppressibleWarnings);
    }

    [Fact]
    public void PreviewAndDefectTaintAreNotSuppressible()
    {
        // GRD-5 and GRD-11 ride in the warning list at Provenance severity.
        var preview = new ValidityWarning(
            "PREVIEW_TIER", "preview result; cannot be quoted or exported", WarningSeverity.Provenance);
        var defect = new ValidityWarning(
            "ENGINE_BELOW_FLOOR", "produced by a version below the published floor", WarningSeverity.Provenance);

        Assert.False(preview.IsSuppressible);
        Assert.False(defect.IsSuppressible);
    }

    [Fact]
    public void OnlyAdvisoryWarningsAreSuppressible()
    {
        var advisory = new ValidityWarning("COARSE_MESH", "mesh is coarse", WarningSeverity.Advisory);

        Assert.True(advisory.IsSuppressible);
        Assert.False(ResolvingPower(advisory).HasNonSuppressibleWarnings);
    }

    [Fact]
    public void WarningsAccumulateRatherThanReplace()
    {
        // GRD-2: warnings travel with the result through every layer, so a
        // transformation must carry the originals forward.
        var first = new ValidityWarning("A", "first", WarningSeverity.Qualified);
        var second = new ValidityWarning("B", "second", WarningSeverity.ValidityViolation);

        var result = ResolvingPower(first).WithWarning(second);

        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal("A", result.Warnings[0].Code);
        Assert.Equal("B", result.Warnings[1].Code);
    }

    [Fact]
    public void UnconvergedEnsemblesSaySoInTheRendering()
    {
        var unconverged = new Measured(
            Quantity.Number(0.92),
            UncertaintyInterval.Symmetric(Quantity.Number(0.92), Quantity.Number(0.05), 0.95),
            new Evidence.Ensemble(EnsembleSize: 50, Converged: false));

        Assert.Contains("NOT CONVERGED", unconverged.Format("1"), StringComparison.Ordinal);
    }

    [Fact]
    public void FormatsInAnyUnitOfTheRightDimension()
    {
        var flightTime = new Measured(
            Quantity.From(192.0, "µs"),
            UncertaintyInterval.Symmetric(Quantity.From(192.0, "µs"), Quantity.From(4.8, "ns"), 0.95),
            new Evidence.Convergence("grid spacing", ObservedOrder: 3.9, NominalOrder: 4.0, ResidualSi: 1e-13));

        var text = flightTime.Format("µs");

        Assert.Contains("192", text, StringComparison.Ordinal);
        Assert.Contains("grid spacing", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringNeverThrowsForAnyDimension()
    {
        // Mobility has a registered coherent SI symbol; a synthetic dimension
        // does not, and must fall back rather than throw.
        var exotic = new Measured(
            Quantity.Si(1.0, new Dimension(length: 5, luminous: -3)),
            UncertaintyInterval.Symmetric(
                Quantity.Si(1.0, new Dimension(length: 5, luminous: -3)),
                Quantity.Si(0.1, new Dimension(length: 5, luminous: -3)),
                0.95),
            new Evidence.Analytic("synthetic"));

        Assert.False(string.IsNullOrWhiteSpace(exotic.ToString()));
    }

    [Fact]
    public void UncertaintyRejectsANegativeHalfWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UncertaintyInterval.Symmetric(Quantity.Number(1.0), Quantity.Number(-0.1), 0.95));
    }

    [Fact]
    public void UncertaintyRejectsAnOutOfRangeConfidenceLevel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UncertaintyInterval.Symmetric(Quantity.Number(1.0), Quantity.Number(0.1), 1.5));
    }
}
