using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The project as a whole: what is in it and the state of each part (§16).
/// </summary>
/// <remarks>
/// <para>
/// A project is a directory (§3), so the view of one is a view of a folder. What makes it
/// more than a listing is the state — and the field that could not come from
/// <c>einzel verify</c> is <b>never run</b>: verify walks the manifests, so a model with
/// no result is reported by neither its success nor its failure, and that is the state
/// most models in a working project are in.
/// </para>
/// <para>
/// Building it found a defect in verify itself, which is the regression below.
/// </para>
/// </remarks>
public sealed class ProjectViewTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-project-view", Guid.NewGuid().ToString("N"));

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

    private void Init()
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }
    }

    private string Example(string name, string as_)
    {
        Init();

        var path = Path.Combine(_root, "models", $"{as_}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);
        }

        return path;
    }

    /// <summary>Nudges a model's first parameter, so its content changes.</summary>
    private static void Edit(string path)
    {
        var text = File.ReadAllText(path);
        var document = System.Text.Json.Nodes.JsonNode.Parse(text)!;

        var parameters = document["parameters"]!.AsObject();
        var first = parameters.First();
        var value = first.Value!["value"]!.GetValue<double>();

        first.Value["value"] = value * 1.01;

        File.WriteAllText(path, document.ToJsonString());

        Assert.NotEqual(text, File.ReadAllText(path));
    }

    /// <summary>A model nobody has run says so, which verify cannot.</summary>
    /// <remarks>
    /// The field the view exists for. <c>einzel verify</c> reports on stored results, so a
    /// model with none is absent from its output entirely — not reported as fine, and not
    /// reported as broken, simply not there.
    /// </remarks>
    [Fact]
    public void AModelNobodyHasRunSaysSo()
    {
        Example("single-stage-reflectron", "refl");

        var outcome = ProjectCommand.Execute(_root);

        foreach (var model in outcome.Models)
        {
            output.WriteLine(
                $"{model.Path,-28} valid {model.Valid,-5} ran {model.Ran,-5} "
                + $"current {model.Current}");
        }

        Assert.NotEmpty(outcome.Models);
        Assert.All(outcome.Models, m => Assert.False(m.Ran));
        Assert.Equal(outcome.Models.Count, outcome.NeverRun);

        var said = Assert.Single(outcome.Warnings, w => w.Code == "project.nothing-run");

        output.WriteLine(said.Message);

        // Verify sees nothing at all here, which is the gap being filled.
        Assert.Empty(VerifyCommand.Execute(_root).Results);
    }

    /// <summary>A run makes its model current; editing it makes it stale.</summary>
    [Fact]
    public void ARunMakesItsModelCurrentAndAnEditMakesItStale()
    {
        var model = Example("single-stage-reflectron", "refl");

        Assert.Equal(0, Cli("run", model).ExitCode);

        var after = ProjectCommand.Execute(_root);
        var ran = Assert.Single(after.Models, m => m.Path.EndsWith("refl.json", StringComparison.Ordinal));

        output.WriteLine($"after running:  ran {ran.Ran}, current {ran.Current}");

        Assert.True(ran.Ran);
        Assert.True(ran.Current);
        Assert.Empty(ran.Drift);

        Edit(model);

        var edited = ProjectCommand.Execute(_root);
        var stale = Assert.Single(edited.Models, m => m.Path.EndsWith("refl.json", StringComparison.Ordinal));

        output.WriteLine($"after editing:  ran {stale.Ran}, current {stale.Current}");
        output.WriteLine($"  {string.Join("; ", stale.Drift)}");

        Assert.True(stale.Ran, "a model whose result exists has been run, edited or not");
        Assert.False(stale.Current);
        Assert.NotEmpty(stale.Drift);
        Assert.Equal(1, edited.Drifted);
    }

    /// <summary>
    /// An identical second model does not absorb the first one's result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The regression, and the defect was in <c>verify</c> rather than in the view.</b>
    /// A manifest recorded only the model's content hash, so verify identified the model by
    /// searching for a file that still hashed to it. Two models may legitimately hold the
    /// same content — a project scaffolded by <c>init</c> and then given a corpus example
    /// of the same device is enough — and then editing the one that was actually run made
    /// its drift <b>disappear</b>: the result silently re-attached to the untouched twin
    /// and reported itself current.
    /// </para>
    /// <para>
    /// Both halves are asserted, because each alone passes on a wrong implementation. The
    /// edited model must be stale, and the twin must still read as never run — a view that
    /// reported both as stale would be equally wrong and would satisfy the first half.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnIdenticalSecondModelDoesNotAbsorbTheResult()
    {
        var first = Example("single-stage-reflectron", "first");

        Init();

        var twin = Path.Combine(_root, "models", "twin.json");

        File.Copy(first, twin);

        // Byte-identical, which is what makes this the case the hash search cannot tell
        // apart. Asserted rather than assumed, because a copy that differed would make the
        // whole test vacuous.
        Assert.Equal(File.ReadAllText(first), File.ReadAllText(twin), StringComparer.Ordinal);

        Assert.Equal(0, Cli("run", first).ExitCode);

        Edit(first);

        var outcome = ProjectCommand.Execute(_root);

        foreach (var model in outcome.Models)
        {
            output.WriteLine(
                $"{model.Path,-28} ran {model.Ran,-5} current {model.Current,-5} "
                + $"{string.Join("; ", model.Drift)}");
        }

        var edited = Assert.Single(
            outcome.Models, m => m.Path.EndsWith("first.json", StringComparison.Ordinal));

        var untouched = Assert.Single(
            outcome.Models, m => m.Path.EndsWith("twin.json", StringComparison.Ordinal));

        Assert.True(
            edited.Ran && !edited.Current,
            "the edited model's result must still be its own, and stale - before this was "
            + "fixed the result re-attached to the twin and the drift vanished");

        Assert.False(
            untouched.Ran,
            "the twin was never run, and a result belonging to another model must not make "
            + "it look as though it was");
    }

    /// <summary>A model that does not validate is reported, with why.</summary>
    /// <remarks>
    /// Validation is run here rather than read from a stored result, because a model can be
    /// edited into an invalid state after its last successful run — and a view reporting it
    /// as current on the strength of a stale manifest would say the opposite of the truth.
    /// </remarks>
    [Fact]
    public void AModelThatDoesNotValidateIsReportedWithWhy()
    {
        Init();

        File.WriteAllText(
            Path.Combine(_root, "models", "broken.json"),
            """{"schemaVersion": "0.6", "nonsense": 1}""");

        var outcome = ProjectCommand.Execute(_root);
        var broken = Assert.Single(
            outcome.Models, m => m.Path.EndsWith("broken.json", StringComparison.Ordinal));

        output.WriteLine($"{broken.Path}: {broken.Problem}");

        Assert.False(broken.Valid);
        Assert.NotNull(broken.Problem);

        // AGT-3: the reason names the offending property rather than saying "invalid".
        Assert.Contains("nonsense", broken.Problem, StringComparison.Ordinal);

        // And it changes the exit code, because an invalid model is a thing to fix.
        var (exitCode, _, stderr) = Cli("project", _root);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken.json", stderr, StringComparison.Ordinal);
    }

    /// <summary>A renamed model is a note, not drift.</summary>
    /// <remarks>
    /// The content is unchanged, so the result still answers the question — what moved is
    /// where the question lives. This is the case the hash search was written for and it
    /// still handles it; what changed is that the recorded path is tried first.
    /// </remarks>
    [Fact]
    public void ARenamedModelIsANoteRatherThanDrift()
    {
        var model = Example("single-stage-reflectron", "before");

        Assert.Equal(0, Cli("run", model).ExitCode);

        File.Move(model, Path.Combine(_root, "models", "after.json"));

        var outcome = ProjectCommand.Execute(_root);
        var renamed = Assert.Single(
            outcome.Models, m => m.Path.EndsWith("after.json", StringComparison.Ordinal));

        output.WriteLine($"ran {renamed.Ran}, current {renamed.Current}");
        output.WriteLine($"  notes: {string.Join("; ", renamed.Notes)}");
        output.WriteLine($"  drift: {string.Join("; ", renamed.Drift)}");

        Assert.True(renamed.Ran);
        Assert.True(renamed.Current, "a rename does not change the answer");
        Assert.Contains(
            renamed.Notes,
            n => n.Contains("no longer there", StringComparison.Ordinal)
                && n.Contains("after.json", StringComparison.Ordinal));
    }

    /// <summary>The recorded model path is portable, and an older manifest still reads.</summary>
    /// <remarks>
    /// <para>
    /// <b>Forward slashes, because a manifest travels.</b> <c>results/</c> is small text and
    /// gets committed, and this project's own CI runs on both Linux and Windows. A backslash
    /// path written on one does not resolve on the other, so verify would miss it, fall back
    /// to the hash, find the model anyway and report a <em>rename</em> — a false alarm on
    /// output the tool produced itself.
    /// </para>
    /// <para>
    /// And the second half: a manifest written before the field existed carries no path at
    /// all and must still verify, through the hash search that was the only mechanism then.
    /// Simulated by deleting the field, which is exactly what such a manifest looks like.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRecordedPathIsPortableAndAnOlderManifestStillReads()
    {
        var model = Example("single-stage-reflectron", "refl");

        Assert.Equal(0, Cli("run", model).ExitCode);

        var manifest = Directory
            .EnumerateFiles(Path.Combine(_root, "results"), "*.manifest.json")
            .Single();

        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!;
        var recorded = document["modelPath"]!.GetValue<string>();

        output.WriteLine($"recorded: {recorded}");

        Assert.Equal("models/refl.json", recorded, StringComparer.Ordinal);
        Assert.DoesNotContain('\\', recorded);

        // Still current with the field present.
        Assert.True(ProjectCommand.Execute(_root).Models.Single(
            m => m.Path.EndsWith("refl.json", StringComparison.Ordinal)).Current);

        // And with it absent, which is every manifest written before this existed.
        document.AsObject().Remove("modelPath");
        File.WriteAllText(manifest, document.ToJsonString());

        var older = ProjectCommand.Execute(_root).Models.Single(
            m => m.Path.EndsWith("refl.json", StringComparison.Ordinal));

        output.WriteLine($"without the field: ran {older.Ran}, current {older.Current}");

        Assert.True(older.Ran, "an older manifest must still be found by its hash");
        Assert.True(older.Current);
        Assert.DoesNotContain(
            older.Notes, n => n.Contains("no longer there", StringComparison.Ordinal));
    }

    /// <summary>A result whose model is gone is named, not swept up.</summary>
    /// <remarks>
    /// <para>
    /// PRJ-4's argument for treating <c>results/</c> as disposable rests on results being
    /// regenerable. A result whose model is gone is the one state where that does not hold,
    /// so it is surfaced — and named, because "an orphaned result" tells nobody which model
    /// to restore.
    /// </para>
    /// <para>
    /// The name comes from the manifest's recorded path, which is exactly what the field
    /// added for verify's benefit makes possible: before it there was nothing to print but
    /// a hash.
    /// </para>
    /// </remarks>
    [Fact]
    public void AResultWhoseModelIsGoneIsNamed()
    {
        var model = Example("single-stage-reflectron", "refl");

        Assert.Equal(0, Cli("run", model).ExitCode);

        File.Delete(model);

        // And the scaffolded reflectron, which is byte-identical to the corpus example -
        // the very coincidence that started this. Left in place, the hash search finds it
        // and the result is a relocation rather than an orphan, which is correct and is a
        // different test.
        File.Delete(Path.Combine(_root, "models", "reflectron.json"));

        var outcome = ProjectCommand.Execute(_root);
        var orphan = Assert.Single(outcome.Orphans);

        output.WriteLine($"{orphan.Manifest} -> {orphan.Model}");

        Assert.EndsWith("refl.json", orphan.Model, StringComparison.Ordinal);

        var said = Assert.Single(outcome.Warnings, w => w.Code == "project.orphaned-results");

        // Qualified rather than provenance, and so not suppressible (GRD-3): a result that
        // cannot be regenerated is a claim about the project that the reader must see.
        Assert.False(said.IsSuppressible);
    }

    /// <summary>A file in models/ that is not a model does not take the view down.</summary>
    /// <remarks>
    /// <para>
    /// A listing walks whatever is in the folder, which is untrusted input: binary, text
    /// that is not JSON, JSON that is not a model, a schema version from the future. One
    /// bad file must not cost the reader every good one — and it must be <em>reported</em>
    /// rather than skipped, since a model somebody can see in the folder and cannot see
    /// here is worse than one shown as broken.
    /// </para>
    /// <para>
    /// Each reason is asserted individually because they arrive through different paths —
    /// a parse failure, a schema check, a units check — and a single "invalid" for all four
    /// would satisfy a catch-all that had stopped saying anything useful.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFileThatIsNotAModelDoesNotTakeTheViewDown()
    {
        Init();

        var models = Path.Combine(_root, "models");

        File.WriteAllBytes(Path.Combine(models, "binary.json"), [0x00, 0x01, 0x02, 0xFF]);
        File.WriteAllText(Path.Combine(models, "text.json"), "not json at all");
        File.WriteAllText(Path.Combine(models, "future.json"), """{"schemaVersion": "9.9"}""");
        File.WriteAllText(
            Path.Combine(models, "badunit.json"),
            """{"schemaVersion": "0.6", "parameters": {"x": {"value": 1, "unit": "nonsense"}}}""");

        var outcome = ProjectCommand.Execute(_root);

        foreach (var model in outcome.Models)
        {
            output.WriteLine($"{(model.Valid ? "ok     " : "INVALID")} {model.Path}: {model.Problem}");
        }

        // The scaffolded model is still there and still fine, which is the half that fails
        // if one bad file aborts the walk.
        Assert.Contains(
            outcome.Models,
            m => m.Valid && m.Path.EndsWith("reflectron.json", StringComparison.Ordinal));

        Assert.Equal(4, outcome.Models.Count(m => !m.Valid));

        // Each says something specific about itself rather than "invalid".
        Assert.Contains("nonsense", Problem(outcome, "badunit.json"), StringComparison.Ordinal);
        // The offending version, not only the supported ones. AGT-3 asks for the observed
        // value and the error carries it; this view dropped it until the assertion below
        // was written, so a reader was told what the build reads and not what the file
        // claimed.
        Assert.Contains("9.9", Problem(outcome, "future.json"), StringComparison.Ordinal);
        Assert.NotEmpty(Problem(outcome, "binary.json"));
        Assert.NotEmpty(Problem(outcome, "text.json"));
    }

    /// <summary>One model's stated problem.</summary>
    private static string Problem(ProjectOutcome outcome, string endsWith) =>
        outcome.Models.Single(m => m.Path.EndsWith(endsWith, StringComparison.Ordinal)).Problem
        ?? string.Empty;

    /// <summary>The verb prints, exits, and carries its warnings on stderr.</summary>
    [Fact]
    public void TheVerbPrintsTheProjectAndItsState()
    {
        Example("single-stage-reflectron", "refl");

        var (exitCode, stdout, stderr) = Cli("project", _root);

        output.WriteLine(stdout);
        output.WriteLine("--- stderr ---");
        output.WriteLine(stderr);

        // Nothing here is wrong: a model nobody has run is where every model starts.
        Assert.Equal(0, exitCode);

        Assert.Contains("not run", stdout, StringComparison.Ordinal);
        Assert.Contains("never run", stdout, StringComparison.Ordinal);

        // CLI-2: diagnostics on stderr, results on stdout.
        Assert.Contains("project.nothing-run", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("project.nothing-run", stdout, StringComparison.Ordinal);
    }
}
