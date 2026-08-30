using Einzel.Commands;
using Einzel.Core.Errors;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A model's parameter surface, for anything that needs to show it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because UI-1 forbids the shell from having file format knowledge.</b>
/// §16 wants a model tree with parameter editing, live validation and units on every
/// field; a window that parsed the document to build that tree would grow its own idea
/// of what a model is, which is the way the window and the engine come to disagree.
/// </para>
/// <para>
/// It is a command rather than a shell method because AGT-2 says nothing exists only in
/// the shell — so an agent gets the same service, which is arguably the one that matters
/// more.
/// </para>
/// </remarks>
public sealed class OutlineTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-outline", Guid.NewGuid().ToString("N"));

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

    /// <summary>A shipped template, materialised so there is a real model to read.</summary>
    private string Quadrupole()
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "q.json");

        Assert.Equal(0, Cli("new", path, "--from-template", "quadrupole").ExitCode);

        return path;
    }

    /// <summary>
    /// The outline carries what an editor needs: value, unit, bounds, description.
    /// </summary>
    /// <remarks>
    /// Every one of these is already declared, and that is the point — LIB-1 gave
    /// parameters units, bounds and descriptions so a study could perturb them, and they
    /// turn out to be exactly what a person editing one needs to see.
    /// </remarks>
    [Fact]
    public void TheOutlineCarriesWhatAnEditorNeeds()
    {
        var outcome = OutlineCommand.Execute(Quadrupole());

        Assert.True(outcome.Valid, string.Join("; ", outcome.Errors.Select(e => e.Constraint)));
        Assert.NotEmpty(outcome.Parameters);

        foreach (var parameter in outcome.Parameters)
        {
            output.WriteLine(
                $"{parameter.Name,-18} {parameter.Value?.ToString("G6") ?? parameter.Expression,-24} "
                + $"{parameter.Unit,-10} {OutlineCommand.BoundsText(parameter) ?? "-"}");
        }

        var radius = Assert.Single(outcome.Parameters, p => p.Name == "inscribedRadius");

        Assert.Equal("mm", radius.Unit);
        Assert.True(radius.Editable);
        Assert.NotNull(radius.Value);

        // Bounds and a description, both declared, both what a person needs before they
        // drag a slider.
        Assert.NotNull(radius.Minimum);
        Assert.NotNull(radius.Maximum);
        Assert.False(string.IsNullOrWhiteSpace(radius.Description));

        // And what it currently works out to, in SI - which is what the rest of the
        // engine uses, and is worth showing beside the millimetres the person typed.
        //
        // Asserted as the relationship rather than against the template's current
        // default: pinning 5 mm here would fail the day somebody legitimately changes
        // the template, while telling us nothing about the conversion. What matters is
        // that the two halves agree.
        Assert.Equal(radius.Value!.Value / 1000.0, radius.ResolvedSi!.Value, 1e-12);
    }

    /// <summary>A derived parameter shows its expression and what it came to.</summary>
    /// <remarks>
    /// <para>
    /// Both halves matter. A reader wants to see that the rod radius <em>is</em>
    /// <c>inscribedRadius * rodRatio</c>, and also what that currently comes to — showing
    /// only the expression makes them do the arithmetic the engine already did.
    /// </para>
    /// <para>
    /// And it is not editable, because its value is its expression's. Offering it for
    /// edit would offer to edit a consequence, and the two would disagree at the next
    /// resolve.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADerivedParameterShowsItsExpressionAndItsValue()
    {
        var outcome = OutlineCommand.Execute(Quadrupole());

        var derived = outcome.Parameters.Where(p => p.Expression is not null).ToArray();

        Assert.NotEmpty(derived);

        foreach (var parameter in derived)
        {
            output.WriteLine(
                $"{parameter.Name} = {parameter.Expression} -> {parameter.ResolvedSi:G6} SI");

            Assert.Null(parameter.Value);
            Assert.False(parameter.Editable);
            Assert.NotNull(parameter.ResolvedSi);
        }
    }

    /// <summary>
    /// A document that does not validate still has an outline.
    /// </summary>
    /// <remarks>
    /// This is what "live validation" needs. A person editing a parameter into an invalid
    /// state must still see the tree with the error against it, rather than have the tree
    /// vanish until they undo what they typed — which would make the editor unusable at
    /// exactly the moment it is most needed.
    /// </remarks>
    [Fact]
    public void AnInvalidDocumentStillHasAnOutline()
    {
        var path = Quadrupole();

        // Out of its declared bounds: still a parseable document, still a set of
        // parameters, and not a valid model.
        File.WriteAllText(path, OutlineCommand.WithParameter(path, "inscribedRadius", 500.0));

        var outcome = OutlineCommand.Execute(path);

        output.WriteLine($"valid: {outcome.Valid}");

        foreach (var error in outcome.Errors)
        {
            output.WriteLine($"  {error.Path}: {error.Constraint}");
        }

        Assert.False(outcome.Valid);
        Assert.NotEmpty(outcome.Errors);

        // The tree is still there, and still says 500.
        Assert.Equal(500.0, Assert.Single(
            outcome.Parameters, p => p.Name == "inscribedRadius").Value);
    }

    /// <summary>Setting a parameter returns the document rather than writing it.</summary>
    /// <remarks>
    /// What lets the shell put the edit through the shared journal and the CLI write it
    /// directly. A command that wrote the file itself would make a shell edit invisible
    /// to the agent connected to the same session, which is the one-sided session GRD-9
    /// is about.
    /// </remarks>
    [Fact]
    public void SettingAParameterReturnsTheDocumentRatherThanWritingIt()
    {
        var path = Quadrupole();
        var before = File.ReadAllText(path);

        // Seven, not the template's own five. Setting a parameter to the value it
        // already has produces a document that differs only in whitespace, so the
        // "it changed" assertion below would pass on the round-trip's reformatting
        // rather than on the edit - which is what the first version of this did.
        var edited = OutlineCommand.WithParameter(path, "inscribedRadius", 7.0);

        // Untouched on disk.
        Assert.Equal(before, File.ReadAllText(path));

        Assert.NotEqual(before, edited);

        // And the edit is in the parameter's own declared unit, not SI: a person editing
        // a 4 mm radius types 5, and the document says 5 mm.
        File.WriteAllText(path, edited);

        var after = Assert.Single(
            OutlineCommand.Execute(path).Parameters, p => p.Name == "inscribedRadius");

        Assert.Equal(7.0, after.Value);
        Assert.Equal("mm", after.Unit);
        Assert.Equal(0.007, after.ResolvedSi!.Value, 1e-12);

        // And everything derived from it moved with it, which is the whole reason a
        // parameter surface exists rather than a list of electrode settings.
        var rodRadius = Assert.Single(
            OutlineCommand.Execute(path).Parameters, p => p.Name == "rodRadius");

        Assert.Equal(0.007 * 1.1468, rodRadius.ResolvedSi!.Value, 1e-12);
    }

    /// <summary>Editing a derived parameter is refused, naming what derives it.</summary>
    [Fact]
    public void EditingADerivedParameterIsRefused()
    {
        var path = Quadrupole();

        var derived = OutlineCommand.Execute(path).Parameters
            .First(p => p.Expression is not null);

        var refusal = Assert.Throws<EinzelException>(
            () => OutlineCommand.WithParameter(path, derived.Name, 1.0));

        output.WriteLine($"{refusal.Error.Constraint}");

        Assert.Contains("derived from", refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains(derived.Expression!, refusal.Error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>A parameter the model does not declare is refused, listing what it has.</summary>
    /// <remarks>
    /// AGT-3: an error is a recovery instruction. "No such parameter" leaves the caller
    /// guessing; the list of what there is turns it into a correction.
    /// </remarks>
    [Fact]
    public void AnUndeclaredParameterIsRefusedWithTheOnesThatExist()
    {
        var path = Quadrupole();

        var refusal = Assert.Throws<EinzelException>(
            () => OutlineCommand.WithParameter(path, "inscribedRadus", 5.0));

        output.WriteLine($"{refusal.Error.Constraint}\n{refusal.Error.Suggestion}");

        Assert.Contains("inscribedRadius", refusal.Error.Suggestion!, StringComparison.Ordinal);
    }

    /// <summary>A resolved value gets its SI unit, not the word "SI".</summary>
    /// <remarks>
    /// §16 asks for units on every field, and GRD-1's habit is that a value never appears
    /// without one — because unit ambiguity is the commonest source of silent wrongness
    /// and §9 makes <c>{"energy": 4000}</c> a validation error for exactly that reason.
    /// "0.007 SI" leaves a reader to work out which SI unit a given row is in, which for
    /// a tree mixing lengths, voltages and dimensionless ratios is the inference the
    /// format exists to remove.
    /// </remarks>
    [Theory]
    [InlineData("mm", "m")]
    [InlineData("m", "m")]
    [InlineData("kV", "V")]
    [InlineData("us", "s")]
    [InlineData("MHz", "Hz")]
    public void AResolvedValueGetsItsSiUnit(string declared, string expected)
    {
        var si = OutlineCommand.SiUnitOf(declared);

        output.WriteLine($"{declared} -> {si}");

        Assert.Equal(expected, si);
    }

    /// <summary>A unit the registry does not know yields nothing, not a guess.</summary>
    /// <remarks>
    /// The value is then shown bare. Guessing a unit would be worse than omitting one:
    /// a wrong unit beside a right number is the failure mode the whole units apparatus
    /// exists to prevent.
    /// </remarks>
    [Fact]
    public void AnUnknownUnitYieldsNothing()
    {
        Assert.Equal(string.Empty, OutlineCommand.SiUnitOf("furlongs"));
        Assert.Equal(string.Empty, OutlineCommand.SiUnitOf(""));
    }

    /// <summary>An empty --set is a mistake, not a request to do nothing.</summary>
    /// <remarks>
    /// The malformed <c>--set name</c> case was already refused, so listing the outline
    /// silently here would make one kind of malformed set loud and the other silent —
    /// and a caller whose shell variable expanded empty would get a successful listing
    /// with their edit dropped.
    /// </remarks>
    [Fact]
    public void AnEmptySetIsRefusedRatherThanIgnored()
    {
        var path = Quadrupole();
        var before = File.ReadAllText(path);

        var (exit, _, stderr) = Cli("outline", path, "--set");

        output.WriteLine(stderr);

        Assert.NotEqual(0, exit);
        Assert.Contains("--set takes", stderr, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));
    }
}
