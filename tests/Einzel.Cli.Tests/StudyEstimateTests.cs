using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// GRD-8's cost gate, applied to the operation somebody actually plans against.
/// </summary>
/// <remarks>
/// <para>
/// <c>estimate</c> took a model, and a model is one evaluation out of a study that
/// declares hundreds - so the number it gave was short by the evaluation count, silently.
/// A study file states its own extent, so the multiplier needs no pilot and no run.
/// </para>
/// <para>
/// <b>The two terms scale differently and that is the whole arithmetic.</b> An evaluation
/// solves the field once and flies every ensemble member through it, so a study costs
/// <c>evaluations x (solve + members x flight)</c>. Costing it as
/// <c>evaluations x model total</c> would overstate a nine-member figure by most of eight
/// solves per evaluation, and costing it as <c>evaluations x solve</c> would understate it
/// by the flights.
/// </para>
/// </remarks>
public sealed class StudyEstimateTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-study-estimate", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static int Cli(params string[] args)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private const string Template = "planar-mirror-pair";

    /// <summary>Writes a project holding the template and a study over it.</summary>
    private string Study(string body)
    {
        var model = Path.Combine(_root, "models", Template + ".json");

        if (!File.Exists(model))
        {
            Assert.Equal(0, Cli("init", _root));
            Assert.Equal(0, Cli("new", model, "--from-template", Template));
        }

        var path = Path.Combine(_root, "studies", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(
            path, body.Replace("MODEL", "../models/" + Template + ".json", StringComparison.Ordinal));

        return path;
    }

    private string Scan(int points) => Study(
        "{ \"schemaVersion\": \"0.1\", \"name\": \"separation\", \"model\": \"MODEL\", "
        + "\"figureOfMerit\": \"resolvingPower\", \"ions\": 9, \"scan\": { "
        + "\"parameter\": \"capToCap\", \"from\": 700.0, \"to\": 800.0, \"unit\": \"mm\", "
        + "\"points\": " + points.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + " } }");

    /// <summary>A study costs its evaluation count, and says what that count is.</summary>
    /// <remarks>
    /// Two scans differing only in their point count, so everything machine-specific
    /// cancels: the ratio of the totals must be the ratio of the points. Asserting an
    /// absolute time would be asserting a machine (SPEC.md Amendment 27).
    /// <para>
    /// <b>Both are above the sampling threshold, and they have to be.</b> A study long
    /// enough to absorb it has its flight sampled across its own range and a shorter one
    /// does not, so the two are costed by different methods and their per-evaluation figures
    /// legitimately differ. A first version of this test used 5 and 40, straddled the gate,
    /// and reported 10.77x for an eightfold difference in points - the test finding a real
    /// discontinuity in the thing it was measuring across, rather than a defect.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStudyCostsItsEvaluationCount()
    {
        var few = EstimateCommand.ForStudy(Scan(25));
        var many = EstimateCommand.ForStudy(Scan(50));

        output.WriteLine($"25 points  {few.Seconds,7:F2} s   {few.Study!.Evaluations} evaluations");
        output.WriteLine($"50 points  {many.Seconds,7:F2} s   {many.Study!.Evaluations} evaluations");

        Assert.Equal(25, few.Study.Evaluations);
        Assert.Equal(50, many.Study.Evaluations);
        Assert.Equal("scan", many.Study.Kind);

        // Both are calibrated on the same machine in the same run and sampled the same way,
        // so the per-evaluation costs agree closely and the totals stand in the ratio of
        // the point counts.
        var ratio = many.Seconds / few.Seconds;

        output.WriteLine($"ratio      {ratio:F2}x against 2x expected");

        Assert.InRange(ratio, 1.7, 2.3);
    }

    /// <summary>An evaluation solves once, however many members it flies.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arithmetic the estimate would most plausibly get wrong.</b> The
    /// obvious costing is <c>evaluations x</c> whatever <c>estimate</c> says a model
    /// costs - and a model's cost already contains one flight, so that reads as though
    /// each evaluation flies one ion. Multiplying it by the member count instead charges a
    /// whole solve per member, which is what the energy sweep used to do and no longer
    /// does.
    /// </para>
    /// <para>
    /// So the bound is stated against the naive figure: one evaluation must cost strictly
    /// less than a whole model estimate per member, and at least its own solve.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEvaluationSolvesOnceAndFliesMany()
    {
        var study = Scan(11);

        var model = EstimateCommand.Execute(
            Path.Combine(_root, "models", Template + ".json"));

        var costed = EstimateCommand.ForStudy(study);
        var members = costed.Study!.Members;

        var naive = model.Seconds * members;

        output.WriteLine($"model total          {model.Seconds * 1000,8:F0} ms");
        output.WriteLine($"  of which flight    {model.TrajectorySeconds * 1000,8:F0} ms");
        output.WriteLine($"members              {members,8}");
        output.WriteLine($"per evaluation       {costed.Study.SecondsPerEvaluation * 1000,8:F0} ms");
        output.WriteLine($"naive, x members     {naive * 1000,8:F0} ms");

        Assert.True(members > 1, "the study declares nine ions, so there is a distinction to make");

        Assert.True(
            costed.Study.SecondsPerEvaluation < naive,
            $"one evaluation was costed at {costed.Study.SecondsPerEvaluation:F3} s against "
            + $"{naive:F3} s for {members} whole model runs. A figure of merit solves the "
            + "field once and flies every member through it, so only the flight term is "
            + "multiplied");

        // And it is not the other error either: the flights are counted, not dropped.
        Assert.True(
            costed.Study.SecondsPerEvaluation >= model.Seconds - model.TrajectorySeconds,
            "an evaluation costs at least its solve");
    }

    /// <summary>A long study samples the range it will visit; a short one does not.</summary>
    /// <remarks>
    /// <para>
    /// <b>A study that varies the geometry varies its own cost.</b> A mirror separation scan
    /// crosses a focusing condition and runs 2.2x dearer at one end than the other, so an
    /// estimate taken at the model's declared values alone came out at <b>0.57x</b> of the
    /// real scan - the direction that matters, since an estimate that runs under is worse
    /// than one that runs over.
    /// </para>
    /// <para>
    /// <b>And the gate is the point of the test.</b> Sampling is not free - a sample is one
    /// solve and one flight - so it is worth doing only when the study is long enough to
    /// absorb it. Both halves are asserted, because sampling always would make the estimate
    /// a meaningful fraction of the work it is estimating, and sampling never is the defect
    /// this fixes.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALongStudySamplesTheRangeAndAShortOneDoesNot()
    {
        var brief = EstimateCommand.ForStudy(Scan(4));
        var long_ = EstimateCommand.ForStudy(Scan(60));

        const string Sampled = "sampled at";

        output.WriteLine($" 4 points   sampled: {brief.Basis.Contains(Sampled, StringComparison.Ordinal)}");
        output.WriteLine($"60 points   sampled: {long_.Basis.Contains(Sampled, StringComparison.Ordinal)}");

        Assert.DoesNotContain(Sampled, brief.Basis, StringComparison.Ordinal);
        Assert.Contains(Sampled, long_.Basis, StringComparison.Ordinal);

        // And the short one says why it did not, rather than silently costing at the
        // nominal and letting that read as a measurement of the range.
        Assert.Contains("too short to be worth sampling", brief.Basis, StringComparison.Ordinal);
    }

    /// <summary>A sample flies a whole flight, so nothing is extrapolated.</summary>
    /// <remarks>
    /// <b>The mistake this guards is subtle and cost 3.4x.</b> A pilot that flies a
    /// <i>fraction</i> of a flight has to scale up, and the only length available to scale
    /// against is the declared <b>maximum</b> flight time - which is a ceiling, not an
    /// expectation. The nominal ion arrived inside the fraction and the extremes did not, so
    /// they were scaled by the whole ceiling.
    /// <para>
    /// So the bound is against that ceiling: the sampled flight must be far below the
    /// declared maximum, because the real flight is. Restoring the fractional pilot puts it
    /// within a small factor of the ceiling and fails.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASampledFlightIsNotExtrapolatedAgainstTheFlightTimeCeiling()
    {
        var costed = EstimateCommand.ForStudy(Scan(60));

        // What the document allows, against what one flight actually costs to compute.
        // These are different quantities - one is simulated microseconds, the other is
        // wall-clock seconds - so what is asserted is that the flight term stayed the
        // size of a flight rather than inflating toward a ceiling's worth of them.
        var perEvaluation = costed.Study!.SecondsPerEvaluation;
        var flights = costed.Study.Members * costed.TrajectorySeconds;

        output.WriteLine($"per evaluation   {perEvaluation * 1000,8:F0} ms");
        output.WriteLine($"nominal flight   {costed.TrajectorySeconds * 1000,8:F0} ms");
        output.WriteLine($"sampled term     {perEvaluation * 1000,8:F0} ms total");

        Assert.True(
            perEvaluation < 20.0 * costed.TrajectorySeconds * costed.Study.Members,
            $"one evaluation was costed at {perEvaluation:F2} s against a nominal flight of "
            + $"{costed.TrajectorySeconds:F3} s. A sampled flight that did not arrive inside "
            + "its window and was scaled against the declared maximum flight time inflates "
            + "by the ratio of the ceiling to the real flight - fly the whole flight instead");
    }

    /// <summary>A ceiling is reported as a ceiling.</summary>
    /// <remarks>
    /// A scan computes every point it declares; an optimiser stops when it converges and a
    /// bisection when its bracket closes. Reporting a ceiling as a certainty overstates a
    /// search that usually converges early - and reporting a certainty as a ceiling would
    /// let somebody plan for less work than there is, which is the worse direction.
    /// </remarks>
    [Fact]
    public void ASearchBudgetIsACeilingAndAScansPointsAreNot()
    {
        var optimisation = Study(
            "{ \"schemaVersion\": \"0.1\", \"name\": \"shape\", \"model\": \"MODEL\", "
            + "\"figureOfMerit\": \"resolvingPower\", \"ions\": 9, \"algorithm\": \"nelderMead\", "
            + "\"maximumEvaluations\": 120, \"variables\": [ { \"parameter\": \"capToCap\", "
            + "\"minimum\": 700, \"maximum\": 800, \"unit\": \"mm\" } ] }");

        var search = EstimateCommand.ForStudy(optimisation).Study!;
        var grid = EstimateCommand.ForStudy(Scan(9)).Study!;

        output.WriteLine($"{search.Kind,-13} {search.Evaluations,4} evaluations, "
            + $"ceiling {search.EvaluationsAreACeiling}");
        output.WriteLine($"{grid.Kind,-13} {grid.Evaluations,4} evaluations, "
            + $"ceiling {grid.EvaluationsAreACeiling}");

        Assert.Equal("optimisation", search.Kind);
        Assert.Equal(120, search.Evaluations);
        Assert.True(search.EvaluationsAreACeiling);

        Assert.Equal("scan", grid.Kind);
        Assert.False(grid.EvaluationsAreACeiling);
    }
}
