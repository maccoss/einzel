using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A run's figures of merit, grouped the way §12 groups them (§16).
/// </summary>
public sealed class ResultsTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-results", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private string Example(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);

        return path;
    }

    /// <summary>§12's order: one packet, then a population, then a boundary.</summary>
    private static readonly string[] OutwardFromTheIon = ["T", "S", "B", "-"];

    /// <summary>Every figure is placed in exactly one of §12's classes.</summary>
    /// <remarks>
    /// <para>
    /// The grouping is not decoration: a Class T figure describes one packet's arrival, a
    /// Class S figure a population, a Class B figure where a boundary in operating space
    /// lies. They are computed differently, cost differently, and answer different
    /// questions, which is why §16 asks for results sorted this way rather than listed.
    /// </para>
    /// <para>
    /// A figure appearing twice, or in none, would make the sort mean less — so the
    /// assertion is a partition, not a lookup.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFigureIsInExactlyOneClass()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var placed = outcome.Classes.SelectMany(c => c.Figures).Select(f => f.Name).ToList();

        foreach (var group in outcome.Classes)
        {
            output.WriteLine(
                $"Class {group.Class} - {group.What}: "
                + string.Join(", ", group.Figures.Select(f => f.Name)));
        }

        Assert.Equal(FiguresOfMerit.All.Count, placed.Count);
        Assert.Equal(placed.Distinct(StringComparer.Ordinal).Count(), placed.Count);

        Assert.Equal(
            FiguresOfMerit.All.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal),
            placed.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>The classes are ordered outward from the ion, not alphabetically.</summary>
    /// <remarks>
    /// §12 lists T, then S, then B, and the order is the argument: one packet, then a
    /// population, then where a boundary lies. Alphabetical would put B first and read as
    /// though the sort were arbitrary.
    /// </remarks>
    [Fact]
    public void TheClassesAreOrderedOutwardFromTheIon()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var order = outcome.Classes.Select(c => c.Class).ToList();

        output.WriteLine(string.Join(" then ", order));

        Assert.Equal(OutwardFromTheIon, order);
    }

    /// <summary>A figure with an envelope carries all of GRD-1's parts.</summary>
    /// <remarks>
    /// Value, unit, uncertainty, what stands behind it, and any warnings — §16's rule is
    /// that these sit alongside the value and never behind a disclosure control, which
    /// only means anything if they are all present to sit there.
    /// </remarks>
    [Fact]
    public void AReportedFigureCarriesItsWholeEnvelope()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var flight = Assert.Single(
            outcome.Classes.SelectMany(c => c.Figures),
            f => f.Name == "flightTime");

        var measured = Assert.IsType<Io.MeasuredJson>(flight.Measured);

        output.WriteLine(
            $"{flight.Name} = {measured.Value:F6} {measured.Unit} "
            + $"[{measured.Uncertainty.Lower:G8}, {measured.Uncertainty.Upper:G8}] at {measured.Uncertainty.ConfidenceLevel:P0}");

        Assert.Equal("T", flight.Class);
        Assert.Equal("us", measured.Unit);
        Assert.True(measured.Value > 0.0);
        Assert.NotNull(measured.Uncertainty);
        Assert.NotNull(measured.Evidence);
        Assert.Null(flight.Absent);
    }

    /// <summary>
    /// A figure this build has no envelope for is absent and says so (GRD-1, GRD-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this view exists to expose.</b> GRD-1 says the API offers no way to
    /// obtain a scalar alone, and <c>FiguresOfMerit.Evaluator</c> is a deliberate, argued
    /// exception for ranking. The consequence is that most figures exist <em>only</em> in
    /// the excepted form: there is no way to ask this build for a turn-around time with an
    /// uncertainty on it.
    /// </para>
    /// <para>
    /// Reporting them as bare numbers would put unqualified values in the one view whose
    /// whole purpose is showing the envelope. So they are absent, and the reason — which
    /// is a property of the platform, not of the model — is on the outcome where a reader
    /// will meet it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFigureWithNoEnvelopeIsAbsentAndTheGapIsNamed()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var figures = outcome.Classes.SelectMany(c => c.Figures).ToList();

        var withEnvelope = figures.Where(f => f.Measured is not null).ToList();
        var without = figures.Where(f => f.Measured is null).ToList();

        output.WriteLine(
            $"{withEnvelope.Count} of {figures.Count} figures carry a GRD-1 envelope: "
            + string.Join(", ", withEnvelope.Select(f => f.Name)));

        Assert.NotEmpty(withEnvelope);
        Assert.NotEmpty(without);

        // Absent with a reason, never a zero that reads as a measurement.
        Assert.All(without, f => Assert.False(string.IsNullOrWhiteSpace(f.Absent)));

        var named = Assert.Single(outcome.Warnings, w => w.Code == "results.no-envelope");

        output.WriteLine(named.Message);

        foreach (var figure in without)
        {
            Assert.Contains(figure.Name, named.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>The preview tier is marked as one (GRD-5).</summary>
    /// <remarks>
    /// AGT-5's cheap loop is what a window wants while somebody drags a slider, and GRD-5
    /// marks the result permanently. A preview number that looks like a run number is the
    /// failure the tier exists to prevent, so the flag is on the outcome rather than left
    /// for the caller to remember.
    /// </remarks>
    [Fact]
    public void ThePreviewTierIsMarkedAsOne()
    {
        var model = Example("single-stage-reflectron");

        var previewed = ResultsCommand.Execute(model, preview: true);
        var ran = ResultsCommand.Execute(model);

        Assert.True(previewed.Preview);
        Assert.False(ran.Preview);

        var previewFlight = Assert.Single(
            previewed.Classes.SelectMany(c => c.Figures), f => f.Name == "flightTime");

        output.WriteLine($"preview warnings: "
            + string.Join(", ", previewFlight.Measured!.Warnings.Select(w => w.Code)));

        // The taint is on the figure itself, not only on the outcome - a figure copied out
        // of this view carries it.
        Assert.Contains(previewFlight.Measured.Warnings, w => w.Code.Contains("preview"));
    }
}
