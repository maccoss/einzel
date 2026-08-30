using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>One model in a project, and what state it is in.</summary>
/// <param name="Path">Where it is, relative to the project root.</param>
/// <param name="Valid">Whether it validates as it stands.</param>
/// <param name="Problem">Why not, where it does not.</param>
/// <param name="TransportMode">Which description it is written in.</param>
/// <param name="Ran">Whether any stored result names it.</param>
/// <param name="Current">Whether every result naming it still stands.</param>
/// <param name="Drift">What makes those results no longer the answer.</param>
/// <param name="Notes">True of them but not invalidating.</param>
/// <remarks>
/// <b><see cref="Ran"/> is the field <c>einzel verify</c> cannot supply.</b> Verify walks
/// the manifests, so a model that has never been run leaves no trace for it to find and is
/// reported by neither its success nor its failure. That is the commonest state a model in
/// a project is actually in, and the one a person opening the folder most needs to see.
/// </remarks>
public sealed record ProjectModel(
    string Path,
    bool Valid,
    string? Problem,
    string? TransportMode,
    bool Ran,
    bool Current,
    IReadOnlyList<string> Drift,
    IReadOnlyList<string> Notes);

/// <summary>A stored result whose model is gone.</summary>
/// <param name="Manifest">The manifest, relative to the project root.</param>
/// <param name="Model">The model it names, as recorded.</param>
/// <remarks>
/// Reported rather than swept up, because a result with no model is not regenerable and
/// PRJ-4's whole argument for treating <c>results/</c> as disposable rests on its being
/// regenerable. One of these is the case where that argument does not hold.
/// </remarks>
public sealed record OrphanedResult(string Manifest, string Model);

/// <summary>What a project holds, and the state of each part of it.</summary>
/// <param name="Root">The project root, as an absolute path.</param>
/// <param name="Models">The models, ordered by path.</param>
/// <param name="Studies">Study documents, relative to the root.</param>
/// <param name="Figures">Render specs, relative to the root.</param>
/// <param name="Tests">Test documents, relative to the root.</param>
/// <param name="ExtensionNames">Extensions registered here.</param>
/// <param name="Orphans">Results whose model is gone.</param>
/// <param name="Warnings">What a reader needs alongside it (GRD-2).</param>
public sealed record ProjectOutcome(
    string Root,
    IReadOnlyList<ProjectModel> Models,
    IReadOnlyList<string> Studies,
    IReadOnlyList<string> Figures,
    IReadOnlyList<string> Tests,
    IReadOnlyList<string> ExtensionNames,
    IReadOnlyList<OrphanedResult> Orphans,
    IReadOnlyList<ValidityWarning> Warnings)
{
    /// <summary>How many models have never been run.</summary>
    public int NeverRun => Models.Count(m => !m.Ran);

    /// <summary>How many models have a result that no longer stands.</summary>
    public int Drifted => Models.Count(m => m.Ran && !m.Current);
}

/// <summary>
/// The project as a whole: what is in it, and what state each part is in (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>A project is a directory</b> (§3), so the view of one is a view of a folder. What
/// makes it more than a file listing is the state: a model that does not validate, a model
/// nobody has run, a result the model has moved out from under.
/// </para>
/// <para>
/// <b>Built on <c>einzel verify</c> rather than beside it.</b> Verify already computes
/// model drift and engine drift, and separates the two - an edited model or a changed
/// solver-behaviour version invalidates a result, while a different engine build with
/// identical numerics does not. Recomputing either here would be a second implementation
/// of a distinction that took thought to get right.
/// </para>
/// <para>
/// <b>What verify cannot answer is what has never been run.</b> It walks the manifests, so
/// a model with no result is invisible to it - reported by neither its success nor its
/// failure. That is the state most models in a working project are in, and the one a
/// person opening the folder most wants to see, so it is the field this adds.
/// </para>
/// <para>
/// <b>Validation is run here, not read from a result.</b> A model can be edited into an
/// invalid state after its last successful run, and a view that reported it as current on
/// the strength of a stale manifest would be saying the opposite of the truth.
/// </para>
/// </remarks>
public static class ProjectCommand
{
    /// <summary>Reads a project and the state of everything in it.</summary>
    /// <param name="root">The project root.</param>
    /// <returns>What it holds.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is blank.</exception>
    /// <exception cref="EinzelException">There is no project there.</exception>
    public static ProjectOutcome Execute(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));

        if (!Directory.Exists(layout.Root))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"there is no directory at '{layout.Root}'",
                Suggestion = "run `einzel init <dir>` to create a project there",
                Severity = ErrorSeverity.Error,
            });
        }

        var warnings = new List<ValidityWarning>();

        // One verify pass for the whole project, indexed by the model each result names.
        // A model may have several results - a run and a study write separate manifests -
        // and it is current only if all of them are.
        var verified = VerifyCommand.Execute(layout.Root);

        var byModel = new Dictionary<string, List<VerifiedResult>>(StringComparer.OrdinalIgnoreCase);
        var orphans = new List<OrphanedResult>();

        foreach (var result in verified.Results)
        {
            if (result.Model is not { } model)
            {
                // Verify reports a null model for a manifest whose model is gone. It is
                // the one state in which a result cannot be regenerated, which is exactly
                // the assumption PRJ-4 rests on, so it is surfaced rather than skipped.
                // Named where the manifest recorded one. An older manifest carries no
                // path, and saying so is better than a placeholder that reads as a name.
                orphans.Add(new OrphanedResult(
                    result.Manifest,
                    result.RecordedModel ?? "(this manifest records no model path)"));
                continue;
            }

            if (!byModel.TryGetValue(model, out var list))
            {
                byModel[model] = list = [];
            }

            list.Add(result);
        }

        var models = new List<ProjectModel>();

        if (Directory.Exists(layout.Models))
        {
            // Ordered, for CLI-5: a listing that reorders between runs makes every diff
            // of it noisy for no reason.
            var files = Directory.GetFiles(layout.Models, "*.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);

            foreach (var file in files)
            {
                // A model's own test document sits in tests/, but a project may keep other
                // json beside its models; anything that does not parse as a model is
                // reported as such rather than hidden, since a file in models/ that is not
                // one is a thing to know about.
                models.Add(Describe(layout, file, byModel));
            }
        }

        var everRun = models.Count(m => m.Ran);

        if (models.Count > 0 && everRun == 0)
        {
            warnings.Add(new ValidityWarning(
                "project.nothing-run",
                $"none of the {models.Count} models here has a stored result. A project "
                + "carries its results in `results/`, which are regenerable and may simply "
                + "have been discarded (PRJ-4) - `einzel run` writes one",
                WarningSeverity.Provenance));
        }

        if (orphans.Count > 0)
        {
            warnings.Add(new ValidityWarning(
                "project.orphaned-results",
                $"{orphans.Count} stored result(s) name a model that is no longer here, so "
                + "they cannot be regenerated. That is the one case where discarding "
                + "`results/` loses something",
                WarningSeverity.Qualified));
        }

        return new ProjectOutcome(
            layout.Root,
            models,
            Relative(layout, layout.Studies),
            Relative(layout, layout.Figures),
            Relative(layout, layout.Tests),
            [.. ExtensionNames(layout)],
            orphans,
            warnings);
    }

    /// <summary>Reads the project a model belongs to.</summary>
    /// <param name="modelPath">Any model in it.</param>
    /// <returns>What the enclosing project holds.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <remarks>
    /// <para>
    /// The walking lives here rather than in the caller because UI-1 puts project layout
    /// outside the shell along with everything else about the file format - a window that
    /// knew where <c>models/</c> sits would have its own idea of what a project is, and the
    /// two would come to disagree.
    /// </para>
    /// <para>
    /// A model outside any project falls back to its own directory rather than to the
    /// working directory, which is the rule <c>InferProjectRoot</c> was corrected to follow
    /// after a study wrote its results into whatever tree the caller was standing in.
    /// </para>
    /// </remarks>
    public static ProjectOutcome ForModel(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);

        return Execute(
            ProjectLayout.Find(absolute)?.Root
            ?? Path.GetDirectoryName(absolute)
            ?? ".");
    }

    /// <summary>One model's state.</summary>
    private static ProjectModel Describe(
        ProjectLayout layout,
        string file,
        Dictionary<string, List<VerifiedResult>> byModel)
    {
        var relative = Path.GetRelativePath(layout.Root, file);

        var valid = false;
        string? problem = null;
        string? mode = null;

        try
        {
            var validation = ModelValidator.Validate(
                Io.ModelJson.Parse(File.ReadAllText(file)),
                null,
                Path.GetDirectoryName(file));

            valid = validation.IsValid;
            problem = valid ? null : Describe(validation.Errors[0]);
            mode = validation.Model?.TransportMode;
        }
        catch (EinzelException failure)
        {
            problem = failure.Error.Constraint;
        }
        catch (Exception failure) when (
            failure is System.Text.Json.JsonException or IOException)
        {
            // A file in models/ that is not a model at all. Reported rather than skipped:
            // a listing that quietly omitted it would leave somebody looking for a model
            // they can see in the folder and cannot see here.
            problem = failure.Message;
        }

        // Matched on the relative path, which is what a manifest records. A model that has
        // never been run appears in neither list, which is the whole point of this view.
        var results = byModel.TryGetValue(relative, out var found) ? found : [];

        return new ProjectModel(
            relative,
            valid,
            problem,
            mode,
            results.Count > 0,
            results.Count > 0 && results.All(r => r.Current),
            [.. results.SelectMany(r => r.Drift).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)],
            [.. results.SelectMany(r => r.Notes).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)]);
    }

    /// <summary>One validation error as a single line.</summary>
    /// <remarks>
    /// <para>
    /// <b>Constraint and observed value together</b>, because AGT-3 asks for both and the
    /// constraint alone can be the less useful half. An unknown schema version reports
    /// "this build reads schema versions 0.1 ... 0.6" and, without the observed value, does
    /// not say what the file claimed - so a reader has to open it to find out what is
    /// wrong with it.
    /// </para>
    /// <para>
    /// The observed value carries a unit, and where the quantity is not numeric the unit
    /// slot holds the whole of it - a schema version is "0.0 9.9" read literally. Printing
    /// the number in that case would be printing a zero nobody wrote, so it is omitted
    /// where it is not a real quantity.
    /// </para>
    /// </remarks>
    private static string Describe(EinzelError error)
    {
        if (error.Observed is not { } observed)
        {
            return error.Constraint;
        }

        var value = observed.Value == 0.0 && observed.Unit.Length > 0
            ? observed.Unit
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{observed.Value:G6} {observed.Unit}");

        return $"{error.Constraint}; this one is {value}";
    }

    /// <summary>The json documents in one project directory, relative and ordered.</summary>
    private static IReadOnlyList<string> Relative(ProjectLayout layout, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        return [.. files.Select(f => Path.GetRelativePath(layout.Root, f))];
    }

    /// <summary>The extensions registered in this project, by directory name.</summary>
    /// <remarks>
    /// By directory rather than by reading each manifest, because a project view lists what
    /// is there and <c>einzel ext list</c> is the verb that inspects one - including the
    /// non-suppressible note about what the sandbox does not enforce, which would be wrong
    /// to paraphrase here.
    /// </remarks>
    private static IEnumerable<string> ExtensionNames(ProjectLayout layout)
    {
        if (!Directory.Exists(layout.Extensions))
        {
            return [];
        }

        var directories = Directory.GetDirectories(layout.Extensions);
        Array.Sort(directories, StringComparer.Ordinal);

        return directories.Select(Path.GetFileName)!;
    }
}
