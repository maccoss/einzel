using Einzel.Commands;
using Einzel.Core.Results;

namespace Einzel.Cli.Tests;

/// <summary>A name holding a comma or a quote stays one cell in every CSV a command writes.</summary>
/// <remarks>
/// The surface table quoted its conductor names and the probe's profile wrote them bare, so a
/// conductor called <c>plate, "upper"</c> moved every column after it one along in the profile.
/// </remarks>
public sealed class CsvFieldTests
{
    private const string Awkward = "plate, \"upper\"";

    [Theory]
    [InlineData("ring-3", "ring-3")]
    [InlineData(Awkward, "\"plate, \"\"upper\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData(null, "")]
    public void AFieldIsQuotedOnlyWhereItMustBe(string? name, string written) =>
        Assert.Equal(written, Csv.Field(name));

    /// <summary>
    /// A text cell beginning like a formula is written as text a spreadsheet will not evaluate: a
    /// single quote before it, inside quotes. A cell that only holds such a character later is not.
    /// </summary>
    /// <remarks>
    /// A conductor named <c>=HYPERLINK(...)</c> was written bare, and a spreadsheet opening the
    /// table ran it.
    /// </remarks>
    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-plate", "\"'-plate\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\t=1", "\"'\t=1\"")]
    [InlineData("\r=1", "\"'\r=1\"")]
    [InlineData("ring=3", "ring=3")]
    [InlineData("", "")]
    public void ACellBeginningLikeAFormulaIsWrittenAsText(string name, string written) =>
        Assert.Equal(written, Csv.Field(name));

    /// <summary>
    /// The guard is on text cells only: a profile row's negative coordinates keep their minus,
    /// and only the conductor's name is guarded.
    /// </summary>
    [Fact]
    public void AProfileGuardsANameAndLeavesNegativeNumbersAlone()
    {
        var outcome = new ProbeOutcome
        {
            ModelPath = "m.json",
            TimeVarying = false,
            Elements = [],
            Samples =
            [
                new ProbeSample(0, [-1.5, -2.0, 3.0], -5.0, [0.0, 0.0, 0.0], 0.0, "insideConductor", "=cmd", 0.0, [], null),
            ],
            Warnings = Array.Empty<ValidityWarning>(),
        };

        var row = ProbeCommand.ToCsv(outcome).Split('\n')[1];

        Assert.Contains("-1.5,-2", row, StringComparison.Ordinal);
        Assert.Contains(",-5", row, StringComparison.Ordinal);
        Assert.EndsWith(",insideConductor,\"'=cmd\"", row, StringComparison.Ordinal);
    }

    [Fact]
    public void AProbeProfileQuotesAConductorsName()
    {
        var outcome = new ProbeOutcome
        {
            ModelPath = "m.json",
            TimeVarying = false,
            Elements = [],
            Samples =
            [
                new ProbeSample(0, [1.0, 2.0, 3.0], 5.0, [0.0, 0.0, 0.0], 0.0, "insideConductor", Awkward, 0.0, [], null),
            ],
            Warnings = Array.Empty<ValidityWarning>(),
        };

        var row = ProbeCommand.ToCsv(outcome).Split('\n')[1];
        var header = ProbeCommand.ToCsv(outcome).Split('\n')[0];

        Assert.EndsWith(",insideConductor,\"plate, \"\"upper\"\"\"", row, StringComparison.Ordinal);

        // As many cells as the header names, once the quoted one is read as one.
        Assert.Equal(header.Split(',').Length, row.Replace("\"plate, \"\"upper\"\"\"", "x", StringComparison.Ordinal).Split(',').Length);
    }
}
