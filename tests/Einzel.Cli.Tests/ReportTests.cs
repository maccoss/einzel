using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// An account of a project's runs that a person can read (Amendment 43).
/// </summary>
/// <remarks>
/// <para>
/// The inputs all existed and nothing rendered them: <c>results/*.result.json</c> carries
/// the numbers with their GRD-1 envelopes and the manifests carry the provenance PRJ-3 says
/// determines a run, all of it for a consumer that parses. What these check is the property
/// that makes a report trustworthy rather than merely present — that it is a <em>view</em>,
/// so it cannot say something the stored documents do not.
/// </para>
/// <para>
/// <b>It found two defects on its first run against a real project</b>, both recorded in
/// the code it now guards: the sequenced run path stored a manifest and no result at all,
/// and result documents did not read back into the record that wrote them. Neither was
/// visible to anything else here, because nothing else read a result.
/// </para>
/// </remarks>
public sealed class ReportTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-report", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
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

    /// <summary>A field-free drift with a cloud, so there is an ensemble to report.</summary>
    private string Model(string name, int detectorMm = 100)
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.13",
              "name": "{{name}}",
              "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 1000, "unit": "V" },
                "cloud": {
                  "ions": 16,
                  "seed": 3,
                  "temperature": { "value": 300, "unit": "K" },
                  "transverseSpread": { "value": 0.3, "unit": "mm" }
                }
              },
              "detector": {
                "planePoint": { "value": [{{detectorMm}}, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "trajectory",
                "maximumFlightTime": { "value": 1, "unit": "ms" }
              },
              "fields": [{ "type": "fieldFree" }]
            }
            """);

        return path;
    }

    /// <summary>Every stored run appears, with the numbers it stored.</summary>
    [Fact]
    public void EveryStoredRunIsAccountedFor()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        var (exit, stdout, stderr) = Run("report", _root, "--json");

        Assert.True(exit == 0, stdout + stderr);

        var report = JsonDocument.Parse(stdout).RootElement;
        var runs = report.GetProperty("runs").EnumerateArray().ToArray();

        Assert.Single(runs);

        var run = runs[0];

        // The provenance PRJ-3 names, and the answer beside it. Either alone is what the
        // platform already had.
        Assert.Equal("trajectory", run.GetProperty("transportMode").GetString());
        Assert.StartsWith("sha256:", run.GetProperty("modelHash").GetString()!, StringComparison.Ordinal);
        Assert.True(run.GetProperty("current").GetBoolean());

        var names = run.GetProperty("numbers").EnumerateArray()
            .Select(n => n.GetProperty("name").GetString()!)
            .ToArray();

        output.WriteLine(string.Join(", ", names));

        Assert.Contains("flight time", names);
        Assert.Contains("transmission", names);

        // Every number carries its unit, which is GRD-1's own subject: this surface
        // offers no way to obtain the scalar alone, so a report of it may not either.
        foreach (var number in run.GetProperty("numbers").EnumerateArray())
        {
            var name = number.GetProperty("name").GetString();

            Assert.False(
                string.IsNullOrEmpty(number.GetProperty("value").GetString()),
                $"'{name}' has no value");

            Assert.True(
                number.TryGetProperty("unit", out _)
                    || name is "launched" or "arrived" or "steps",
                $"'{name}' names no unit and is not a count");
        }
    }

    /// <summary>
    /// A run that stored no answer is reported as that, not as a run with no numbers.
    /// </summary>
    /// <remarks>
    /// <b>The defect this command was built on top of.</b> A manifest with no result beside
    /// it and a run that genuinely produced no reportable quantity look identical from a
    /// list of numbers, and only one of them is a statement about the physics. So the
    /// distinction is a field rather than an inference, and it is warned about at the top of
    /// the report — a reader looking at a run with nothing under it should be told why.
    /// </remarks>
    [Fact]
    public void AManifestWithNoResultIsSaidToBeOne()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        // The state the sequenced run path was in for every TIMS study this project ran.
        var resultPath = Path.Combine(_root, "results", "drift.result.json");

        Assert.True(File.Exists(resultPath), "the run stored no result to remove");
        File.Delete(resultPath);

        var (exit, stdout, stderr) = Run("report", _root, "--json");

        Assert.True(exit == 0, stdout + stderr);

        var report = JsonDocument.Parse(stdout).RootElement;
        var run = report.GetProperty("runs").EnumerateArray().Single();

        Assert.False(run.TryGetProperty("result", out _));
        Assert.Equal(1, report.GetProperty("withoutResult").GetInt32());

        var codes = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        output.WriteLine(string.Join(", ", codes));

        Assert.Contains("report.manifest-without-result", codes);

        // And the page says it in words rather than leaving a run with an empty table.
        Assert.Equal(0, Run("report", _root).ExitCode);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));

        Assert.Contains("no result stored", page, StringComparison.Ordinal);
        Assert.Contains("its answer is nowhere", page, StringComparison.Ordinal);
    }

    /// <summary>A run the model has moved out from under is marked, with verify's reason.</summary>
    /// <remarks>
    /// Drift is not recomputed here. <c>einzel verify</c> already separates an edited model
    /// from a changed engine build — the distinction FLD-3 calls "not optional" — and a
    /// second implementation of it would eventually disagree with the first.
    /// </remarks>
    [Fact]
    public void ADriftedRunIsMarkedWithTheReasonVerifyGives()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        var edited = File.ReadAllText(model)
            .Replace("\"value\": 1000", "\"value\": 1200", StringComparison.Ordinal);

        File.WriteAllText(model, edited);

        var report = JsonDocument.Parse(Run("report", _root, "--json").Stdout).RootElement;
        var run = report.GetProperty("runs").EnumerateArray().Single();

        Assert.False(run.GetProperty("current").GetBoolean());
        Assert.Equal(0, report.GetProperty("current").GetInt32());

        var drift = run.GetProperty("drift").EnumerateArray()
            .Select(d => d.GetString()!)
            .ToArray();

        output.WriteLine(string.Join("\n", drift));

        Assert.NotEmpty(drift);

        // The numbers are still reported. Taint, never block: a superseded result is
        // still what the run measured, and hiding it would make the report less use than
        // the file it reads.
        Assert.NotEmpty(run.GetProperty("numbers").EnumerateArray().ToArray());

        Assert.Equal(0, Run("report", _root).ExitCode);
        Assert.Contains(
            "superseded",
            File.ReadAllText(Path.Combine(_root, "report.html")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// It records nothing, which is the property that keeps it honest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRJ-4 puts the durable record of a design in the model document and its history,
    /// with <c>results/</c> regenerable. A report that kept state would be a second account
    /// of the same events, and the two would part company — the failure the generated half
    /// of <c>AGENTS.md</c> exists to prevent, one level up.
    /// </para>
    /// <para>
    /// Two halves, and the second is the one with teeth: nothing is added to
    /// <c>results/</c>, <em>and</em> two reports over the same runs are the same page but
    /// for the instant they were rendered at. A recorder would pass the first.
    /// </para>
    /// </remarks>
    [Fact]
    public void ItRecordsNothingOfItsOwn()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        var before = Directory.GetFiles(Path.Combine(_root, "results"))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(0, Run("report", _root).ExitCode);

        var first = File.ReadAllText(Path.Combine(_root, "report.html"));

        Assert.Equal(0, Run("report", _root).ExitCode);

        var second = File.ReadAllText(Path.Combine(_root, "report.html"));

        var after = Directory.GetFiles(Path.Combine(_root, "results"))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(before, after);

        // Same page but for the render instant, which is the one line that is about the
        // report rather than about the runs.
        static string WithoutTheClock(string page) => string.Join(
            "\n",
            page.Split('\n').Where(l => !l.Contains("rendered <b>", StringComparison.Ordinal)));

        Assert.Equal(WithoutTheClock(first), WithoutTheClock(second));

        output.WriteLine(
            $"{before.Length} file(s) in results/ before and after, "
            + $"{first.Length:N0}-byte page reproduced");
    }

    /// <summary>
    /// The page and <c>--json</c> carry the same numbers (AGT-2).
    /// </summary>
    /// <remarks>
    /// Nothing may exist only in one surface. The formatting happens in the command rather
    /// than in the page for the same reason: a page that formatted its own numbers would be
    /// a second opinion about how many digits a quantity deserves, and an agent and a person
    /// would be reading different figures off the same run.
    /// </remarks>
    [Fact]
    public void ThePageAndTheMachineSurfaceAgree()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        var report = JsonDocument.Parse(Run("report", _root, "--json").Stdout).RootElement;

        Assert.Equal(0, Run("report", _root).ExitCode);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));
        var checked_ = 0;

        foreach (var run in report.GetProperty("runs").EnumerateArray())
        {
            foreach (var number in run.GetProperty("numbers").EnumerateArray())
            {
                var value = number.GetProperty("value").GetString()!;

                Assert.Contains(value, page, StringComparison.Ordinal);
                checked_++;
            }
        }

        // The control: a page that shared no formatting with the command would fail the
        // loop above, and a loop over nothing would pass it.
        Assert.True(checked_ >= 4, $"only {checked_} numbers were compared");

        output.WriteLine($"{checked_} formatted numbers appear in both surfaces");
    }

    /// <summary>
    /// The page stands on its own, with no asset and no script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A report is the artifact most likely to be sent to somebody or opened months later,
    /// and one that needed a stylesheet beside it is one that arrives broken. The style is
    /// inline, as both of this project's design documents are.
    /// </para>
    /// <para>
    /// No script, which is a claim about what the page is rather than a security posture: a
    /// view over stored numbers has nothing to compute, and a page that computed something
    /// would be able to disagree with the documents it was made from.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePageStandsOnItsOwn()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);
        Assert.Equal(0, Run("report", _root).ExitCode);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));

        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<style>", page, StringComparison.Ordinal);

        // Every external reference is a font stylesheet. Anything else would be an asset
        // the page cannot carry, and the fonts degrade to a declared fallback stack.
        foreach (var href in System.Text.RegularExpressions.Regex
            .Matches(page, "href=\"(?<url>[^\"]+)\"")
            .Select(m => m.Groups["url"].Value)
            .Where(u => u.Contains("//", StringComparison.Ordinal)))
        {
            Assert.StartsWith("https://fonts.g", href, StringComparison.Ordinal);
        }

        Assert.Contains("IBM Plex", page, StringComparison.Ordinal);

        // A fallback for every face, so the page reads with no network at all - which
        // matters because AGT-8 keeps the CLI off the network and a page that only
        // rendered online would be a network dependency by the back door.
        Assert.Contains("ui-monospace", page, StringComparison.Ordinal);
        Assert.Contains("Georgia", page, StringComparison.Ordinal);

        output.WriteLine($"{page.Length:N0} bytes, no script, style inline");
    }

    /// <summary>A project with nothing in it says so rather than rendering an empty page.</summary>
    [Fact]
    public void AProjectWithNoRunsSaysSo()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var (exit, stdout, stderr) = Run("report", _root, "--json");

        Assert.True(exit == 0, stdout + stderr);

        var report = JsonDocument.Parse(stdout).RootElement;

        Assert.Empty(report.GetProperty("runs").EnumerateArray().ToArray());

        var codes = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        Assert.Contains("report.nothing-run", codes);

        Assert.Equal(0, Run("report", _root).ExitCode);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));

        // An ordinary state rather than a loss, and the page says which - results are
        // regenerable by design (PRJ-4), so an empty results/ is not a missing thing.
        Assert.Contains("Nothing has been run", page, StringComparison.Ordinal);
        Assert.Contains("regenerable", page, StringComparison.Ordinal);

        output.WriteLine(stderr.Trim());
    }

    /// <summary>
    /// A study's answer is a stored answer, and is not reported as a run that stored none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>results/</c> holds two kinds of answer, and the first version of this command
    /// knew one.</b> A run writes <c>X.result.json</c> beside <c>X.manifest.json</c>; a
    /// study writes <c>X.json</c> beside it, with its own record shape - a distribution, a
    /// table of points, an optimum, a bracket. Looking only for the first reported every
    /// study in a project as a run that had stored nothing, with the warning to match: the
    /// same "wired into N-1 of N paths" mistake this command was written to expose, made
    /// in the command itself, and loudest on the projects with the most work in them.
    /// </para>
    /// <para>
    /// <b>Named rather than drawn, and the two are different states.</b> A study's answer
    /// that this page does not render is not broken and is not missing - reading four more
    /// record types to draw a distribution or a bisection bracket is a real extension of
    /// this report. So the page says the answer is there and it does not draw it yet, which
    /// is what makes the gap a stated one rather than a wrong report.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStudysAnswerIsNotAMissingOne()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        // A study manifest and its answer, in exactly the shape `einzel sweep` writes them:
        // `tol.sweep.manifest.json` beside `tol.sweep.json`. Written directly rather than by
        // running a sweep, because the scaffolded drift model declares no parameters to
        // perturb - and what is under test is how the report reads the *pair of names*, not
        // how a sweep produces them. The manifest is a real one, copied off the run above,
        // so nothing about it is a fixture's approximation of the format.
        var results = Path.Combine(_root, "results");
        var manifest = File.ReadAllText(Path.Combine(results, "drift.manifest.json"));

        File.WriteAllText(Path.Combine(results, "tol.sweep.manifest.json"), manifest);
        File.WriteAllText(
            Path.Combine(results, "tol.sweep.json"),
            """{ "draws": 8, "seed": 7 }""");

        var (exit, stdout, stderr) = Run("report", _root, "--json");

        Assert.True(exit == 0, stdout + stderr);

        var report = JsonDocument.Parse(stdout).RootElement;

        var study_ = report.GetProperty("runs").EnumerateArray()
            .Single(r => r.GetProperty("manifest").GetString()!.Contains(
                "tol.sweep", StringComparison.Ordinal));

        // Not counted as a run that stored nothing, which is the defect. The real run
        // beside it stored one, so this is zero because both are accounted for rather
        // than because nothing ran.
        Assert.Equal(2, report.GetProperty("runs").GetArrayLength());
        Assert.Equal(0, report.GetProperty("withoutResult").GetInt32());
        Assert.Equal(1, report.GetProperty("notRendered").GetInt32());
        Assert.False(study_.TryGetProperty("unreadable", out _));

        var reason = study_.GetProperty("notRendered").GetString()!;

        output.WriteLine(reason);

        Assert.Contains("study", reason, StringComparison.Ordinal);

        var codes = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        Assert.Contains("report.study-not-rendered", codes);
        Assert.DoesNotContain("report.manifest-without-result", codes);

        // And the page says which of the two it is, in its own words.
        Assert.Equal(0, Run("report", _root).ExitCode);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));

        Assert.Contains("a study&#39;s answer", page, StringComparison.Ordinal);
        Assert.DoesNotContain("its answer is nowhere", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The counts and the diagnostics cannot disagree with each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>They did.</b> Each state was counted by a property and warned about by a locally
    /// spelled-out predicate, and when a third state arrived only one of the two learned
    /// about it - so one report said <c>withoutResult: 0</c> and, in the same document,
    /// warned that a run had stored no result. Two implementations of one quantity is the
    /// defect that made <c>run</c> and <c>test</c> disagree twice in this project; a
    /// predicate is small enough to look harmless spelled twice, which is why it was.
    /// </para>
    /// <para>
    /// So the invariant is asserted rather than the arithmetic: a warning is present exactly
    /// when its count is non-zero, in a project holding one run of every state at once.
    /// Checking one state at a time is what let the third slip through.
    /// </para>
    /// <para>
    /// <b>It does not catch the original slip, and saying so is the point.</b> Restoring the
    /// two-clause predicate fails <see cref="AStudysAnswerIsNotAMissingOne"/> and passes this,
    /// because in a fixture that has a genuine manifest-without-result *as well* as a study's
    /// answer both spellings return one. What this holds is the shape of the next slip - a
    /// fourth state, or a count and a warning parting company somewhere else - and a test
    /// whose teeth are claimed rather than measured is what this project keeps writing down.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCountsAndTheWarningsAgree()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        var results = Path.Combine(_root, "results");
        var manifest = File.ReadAllText(Path.Combine(results, "drift.manifest.json"));

        // One of every state in one project: a good run, a study's answer, a manifest with
        // nothing beside it, and a result this build cannot read.
        File.WriteAllText(Path.Combine(results, "tol.sweep.manifest.json"), manifest);
        File.WriteAllText(Path.Combine(results, "tol.sweep.json"), """{ "draws": 8 }""");

        File.WriteAllText(Path.Combine(results, "gone.manifest.json"), manifest);

        File.WriteAllText(Path.Combine(results, "junk.manifest.json"), manifest);
        File.WriteAllText(Path.Combine(results, "junk.result.json"), """{ "outcome": 7 }""");

        var report = JsonDocument.Parse(Run("report", _root, "--json").Stdout).RootElement;

        var codes = report.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()!)
            .ToArray();

        output.WriteLine(string.Join(", ", codes));

        var pairs = new (string Count, string Code)[]
        {
            ("withoutResult", "report.manifest-without-result"),
            ("notRendered", "report.study-not-rendered"),
            ("unreadable", "report.result-unreadable"),
        };

        foreach (var (count, code) in pairs)
        {
            var n = report.GetProperty(count).GetInt32();

            Assert.Equal(n > 0, codes.Contains(code));

            output.WriteLine($"{count} = {n}, '{code}' {(codes.Contains(code) ? "present" : "absent")}");
        }

        // The premise: all four states really are in play, so each pair above was tested
        // against a non-zero count rather than against two zeros agreeing.
        Assert.Equal(4, report.GetProperty("runs").GetArrayLength());
        Assert.Equal(1, report.GetProperty("withoutResult").GetInt32());
        Assert.Equal(1, report.GetProperty("notRendered").GetInt32());
        Assert.Equal(1, report.GetProperty("unreadable").GetInt32());
    }

    /// <summary>Nothing is written under a dry run (CLI-4).</summary>
    [Fact]
    public void ADryRunWritesNothing()
    {
        var model = Model("drift");

        Assert.Equal(0, Run("run", model).ExitCode);

        var (exit, stdout, _) = Run("report", _root, "--dry-run");

        Assert.Equal(0, exit);
        Assert.False(File.Exists(Path.Combine(_root, "report.html")));
        Assert.Contains("would write", stdout, StringComparison.Ordinal);

        output.WriteLine(stdout.Trim());
    }
}
