using Einzel.Project;

namespace Einzel.Commands;

/// <summary>One stored result, checked against the world as it is now.</summary>
public sealed record VerifiedResult
{
    /// <summary>The manifest file, relative to the project root.</summary>
    public required string Manifest { get; init; }

    /// <summary>The model the manifest names, relative to the project root, or null when it is gone.</summary>
    public string? Model { get; init; }

    /// <summary>Whether the model still hashes to what the manifest recorded.</summary>
    public required bool ModelMatches { get; init; }

    /// <summary>Whether the engine that produced it is the one installed.</summary>
    public required bool EngineMatches { get; init; }

    /// <summary>Whether the numerical behaviour version still matches (FLD-3).</summary>
    public required bool SolverMatches { get; init; }

    /// <summary>Whether the run happened on this machine.</summary>
    public required bool SameMachine { get; init; }

    /// <summary>
    /// What makes this result no longer the answer, in the order a reader wants it.
    /// </summary>
    public required IReadOnlyList<string> Drift { get; init; }

    /// <summary>
    /// True of the run but not invalidating.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Drift"/> deliberately. A result made by a
    /// different engine build whose numerical behaviour is identical still stands,
    /// and filing that beside a model that has been edited underneath a result
    /// would train a reader to ignore both.
    /// </remarks>
    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>Whether the result still stands for the model and engine as they are.</summary>
    public bool Current => Drift.Count == 0;
}

/// <summary>The outcome of verifying a project's stored results.</summary>
public sealed record VerifyOutcome
{
    /// <summary>The project root.</summary>
    public required string Root { get; init; }

    /// <summary>One entry per manifest found, ordered by path.</summary>
    public required IReadOnlyList<VerifiedResult> Results { get; init; }

    /// <summary>How many results still stand.</summary>
    public int Current => Results.Count(r => r.Current);

    /// <summary>Whether every result still stands.</summary>
    public bool AllCurrent => Results.All(r => r.Current);
}

/// <summary>
/// Checks stored results against the model and the engine as they are now.
/// </summary>
/// <remarks>
/// <para>
/// GRD-10 asks for drift detection in both directions, and PRJ-3 is what makes it
/// possible: a manifest fully determines its run, so a result carries enough to
/// say whether it is still the answer. Two ways it stops being one, and they fail
/// differently.
/// </para>
/// <para>
/// The <em>model</em> moved on: someone edited the geometry after the run, and the
/// number in results/ answers a question nobody is asking any more. The hash
/// catches that exactly.
/// </para>
/// <para>
/// The <em>engine</em> moved on: the model is untouched but the solver is not the
/// one that produced the answer. The solver-behaviour version is separate from the
/// engine version precisely so this does not fire on a release that changed
/// nothing physical - FLD-3 calls it "not optional, since after an engine update a
/// cache computed by the previous solver is silently wrong and nothing else would
/// catch it".
/// </para>
/// <para>
/// Nothing is recomputed. That is the point: verification has to be cheap enough
/// to run on a whole project, and a check that costs as much as the run it is
/// checking would not be run.
/// </para>
/// </remarks>
public static class VerifyCommand
{
    /// <summary>Verifies every stored result in a project.</summary>
    /// <param name="root">The project root.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    public static VerifyOutcome Execute(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));
        var results = new List<VerifiedResult>();

        if (!Directory.Exists(layout.Results))
        {
            return new VerifyOutcome { Root = layout.Root, Results = results };
        }

        // Ordered, for CLI-5: a report that reorders between runs makes every diff
        // of it noisy for no reason.
        var manifests = Directory.GetFiles(layout.Results, "*.manifest.json");
        Array.Sort(manifests, StringComparer.Ordinal);

        foreach (var path in manifests)
        {
            results.Add(Verify(layout, path));
        }

        return new VerifyOutcome { Root = layout.Root, Results = results };
    }

    private static VerifiedResult Verify(ProjectLayout layout, string manifestPath)
    {
        var relative = Path.GetRelativePath(layout.Root, manifestPath);
        RunManifest? manifest;

        try
        {
            manifest = RunManifest.FromJson(File.ReadAllText(manifestPath));
        }
        catch (System.Text.Json.JsonException)
        {
            manifest = null;
        }

        if (manifest is null)
        {
            return new VerifiedResult
            {
                Manifest = relative,
                ModelMatches = false,
                EngineMatches = false,
                SolverMatches = false,
                SameMachine = false,
                Drift = ["this manifest cannot be read, so nothing about the result it describes can be established"],
                Notes = [],
            };
        }

        // A manifest is named for its model: reflectron.manifest.json came from
        // reflectron.json. The manifest records the hash rather than the path,
        // because a hash survives a rename and a path does not.
        var stem = Path.GetFileNameWithoutExtension(relative);
        stem = stem.EndsWith(".manifest", StringComparison.Ordinal) ? stem[..^".manifest".Length] : stem;

        var modelPath = Path.Combine(layout.Models, stem + ".json");
        var modelExists = File.Exists(modelPath);

        var modelMatches = modelExists
            && string.Equals(
                ContentHash.OfText(File.ReadAllText(modelPath)), manifest.ModelHash, StringComparison.Ordinal);

        var engineMatches = string.Equals(
            manifest.EngineVersion, EngineBuild.Version, StringComparison.Ordinal);

        var solverMatches = manifest.SolverBehaviourVersion == EngineBuild.SolverBehaviourVersion;
        var sameMachine = string.Equals(manifest.Machine, Environment.MachineName, StringComparison.Ordinal);

        var drift = new List<string>();
        var notes = new List<string>();

        if (!modelExists)
        {
            drift.Add($"the model this result came from is gone: expected {Path.GetRelativePath(layout.Root, modelPath)}");
        }
        else if (!modelMatches)
        {
            drift.Add("the model has been edited since this result was computed, so it answers a question "
                + "about a geometry that no longer exists");
        }

        if (!solverMatches)
        {
            // The one that would otherwise be silent. An engine update that changed
            // numerical behaviour makes a stored result wrong in a way nothing
            // about the file would show.
            drift.Add($"the solver has changed behaviour since this run: version {manifest.SolverBehaviourVersion} "
                + $"produced it, this build is {EngineBuild.SolverBehaviourVersion}");
        }
        else if (!engineMatches)
        {
            notes.Add($"a different engine build produced this: {manifest.EngineVersion}, against "
                + $"{EngineBuild.Version} installed. The solver-behaviour version is the same, so the "
                + "numbers stand");
        }

        if (!sameMachine)
        {
            // Section 8 requires run-to-run reproducibility on one machine and
            // explicitly does not require bit-reproducibility across machines, so
            // a comparison that crosses one needs to know it did.
            notes.Add($"produced on {manifest.Machine}, not on {Environment.MachineName}. Results are "
                + "reproducible run to run on one machine and not bit-identical across machines");
        }

        return new VerifiedResult
        {
            Manifest = relative,
            Model = modelExists ? Path.GetRelativePath(layout.Root, modelPath) : null,
            ModelMatches = modelMatches,
            EngineMatches = engineMatches,
            SolverMatches = solverMatches,
            SameMachine = sameMachine,
            Drift = drift,
            Notes = notes,
        };
    }
}
