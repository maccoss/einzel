using Einzel.Core.Errors;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>The outcome of creating a model from something that ships.</summary>
public sealed record NewOutcome
{
    /// <summary>Where the model was written, as an absolute path.</summary>
    public required string Path { get; init; }

    /// <summary>What it was made from.</summary>
    public required string Source { get; init; }

    /// <summary>Whether anything was actually written.</summary>
    /// <remarks>False under <c>--dry-run</c> (CLI-4).</remarks>
    public required bool Written { get; init; }

    /// <summary>
    /// Where the example's test was written, or null when there was none to write.
    /// </summary>
    /// <remarks>
    /// An example ships the assertion that makes it a reference model rather than a
    /// file that parses (EX-1), so instantiating one brings its test along. Null for
    /// a device template, whose parameters are meant to be changed - pinning them
    /// with an assertion would defeat the point of starting from one.
    /// </remarks>
    public string? TestPath { get; init; }
}

/// <summary>One thing checked about the environment.</summary>
/// <param name="Check">What was looked at.</param>
/// <param name="Ok">Whether it is as it should be.</param>
/// <param name="Detail">What was found.</param>
public sealed record DoctorCheck(string Check, bool Ok, string Detail);

/// <summary>The outcome of a health check.</summary>
public sealed record DoctorOutcome
{
    /// <summary>Every check run, in a fixed order.</summary>
    public required IReadOnlyList<DoctorCheck> Checks { get; init; }

    /// <summary>Whether everything passed.</summary>
    public bool Healthy => Checks.All(c => c.Ok);
}

/// <summary>
/// Creating a model from a shipped template or example, and checking that an
/// installation is sane.
/// </summary>
public static class ProjectCommands
{
    /// <summary>Writes a shipped template or example into a project as a new model.</summary>
    /// <param name="destination">Where to write it.</param>
    /// <param name="kind">Either <c>template</c> or <c>example</c>.</param>
    /// <param name="name">Which one.</param>
    /// <param name="dryRun">Report what would happen and write nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is null or blank.</exception>
    /// <exception cref="EinzelException">No such template or example, or the destination exists.</exception>
    public static NewOutcome New(string destination, string kind, string name, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var text = CatalogCommand.Read(kind, name);
        var absolute = Path.GetFullPath(destination);

        // Refusing rather than overwriting. A model is the input a whole study
        // hangs off, and silently replacing one is not a recoverable mistake.
        if (File.Exists(absolute))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/destination",
                Constraint = $"{absolute} already exists",
                Suggestion = "choose another path, or delete the existing model first. This command will not "
                    + "overwrite a model, because a model is the input a study hangs off",
            });
        }

        // An example ships the assertion that makes it a reference model rather than
        // a file that parses, so instantiating one brings the test with it. Written
        // beside the model in tests/, where `einzel test` looks, so the loop from
        // `new` to a green tick has no step the user has to know about.
        //
        // Only for examples. A device template is a starting point to edit, and its
        // parameters are meant to be changed - shipping an assertion with one would
        // pin the very numbers it exists to let you move.
        string? testPath = null;

        if (kind == "example" && ExampleModels.HasTest(name))
        {
            var project = ProjectLayout.Find(Path.GetDirectoryName(absolute) ?? absolute);

            if (project is not null)
            {
                var candidate = Path.Combine(
                    project.Tests, Path.GetFileNameWithoutExtension(absolute) + ".json");

                // The same refusal the model gets, for the same reason, and quietly:
                // an existing test is someone's own assertion and this command has no
                // business replacing it.
                if (!File.Exists(candidate))
                {
                    testPath = candidate;
                }
            }
        }

        if (!dryRun)
        {
            var directory = Path.GetDirectoryName(absolute);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolute, text);

            // Beside the model, because that is what its own path is resolved
            // against. An example needing a data file is one whose model does not
            // run without it, so writing one and not the other produces a project
            // that validates and refuses at run - the shape of half-done that is
            // worse than not writing anything.
            if (kind == "example")
            {
                ExampleModels.WriteAssets(name, Path.GetDirectoryName(absolute)!);
            }

            if (testPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(testPath)!);

                // The shipped test names the model as ../models/<example>.json. The
                // file may have been given another name, so the reference is
                // rewritten to whatever it actually landed as - otherwise the test
                // points at a model that is not there.
                File.WriteAllText(
                    testPath,
                    ExampleModels.ReadTest(name).Replace(
                        $"../models/{name}.json",
                        "../models/" + Path.GetFileName(absolute),
                        StringComparison.Ordinal));
            }
        }

        return new NewOutcome
        {
            Path = absolute,
            Source = $"{kind}:{name}",
            Written = !dryRun,
            TestPath = testPath,
        };
    }

    /// <summary>Regenerates the platform layer of a project's AGENTS.md.</summary>
    /// <param name="root">The project root.</param>
    /// <param name="dryRun">Report what would happen and write nothing (CLI-4).</param>
    /// <returns>The path, and whether it was written.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    /// <remarks>
    /// PRJ-6: the platform layer is generated and version-stamped, never
    /// hand-written. Instructions shipped with one version that describe another
    /// are worse than none, because an agent trusts them and cannot detect the
    /// drift - which is exactly why this is a command rather than a file someone
    /// remembers to update.
    /// </remarks>
    public static NewOutcome AgentsMd(string root, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));
        var path = layout.AgentsFile;
        var generated = AgentsFile.Generate();

        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        var merged = existing is null ? generated : Regenerate(existing, generated);

        if (!dryRun)
        {
            File.WriteAllText(path, merged);
        }

        return new NewOutcome
        {
            Path = path,
            Source = $"generated by engine {EngineBuild.Version}",
            Written = !dryRun,
        };
    }

    /// <summary>
    /// Replaces the generated region of an existing AGENTS.md, leaving everything
    /// outside the markers alone.
    /// </summary>
    /// <remarks>
    /// The file has two authors. The platform owns the region between the markers
    /// and regenerates it; the project owns everything else and writes it by hand.
    /// Regenerating by overwriting the file would satisfy PRJ-6 and destroy the
    /// half of the file that is the reason anyone opens it - so a missing or
    /// damaged marker pair means the hand-written part is prepended rather than
    /// discarded, which is the recoverable failure of the two.
    /// </remarks>
    private static string Regenerate(string existing, string generated)
    {
        var start = existing.IndexOf(AgentsFile.BeginMarker, StringComparison.Ordinal);
        var end = existing.IndexOf(AgentsFile.EndMarker, StringComparison.Ordinal);

        if (start < 0 || end < 0 || end < start)
        {
            return generated + "\n" + existing.TrimStart('\n');
        }

        var generatedStart = generated.IndexOf(AgentsFile.BeginMarker, StringComparison.Ordinal);
        var generatedEnd = generated.IndexOf(AgentsFile.EndMarker, StringComparison.Ordinal);
        var replacement = generated[generatedStart..(generatedEnd + AgentsFile.EndMarker.Length)];

        return existing[..start] + replacement + existing[(end + AgentsFile.EndMarker.Length)..];
    }

    /// <summary>Checks that the installation and, optionally, a project are sane.</summary>
    /// <param name="root">A project root to check, or null to check only the engine.</param>
    /// <returns>The checks and their results.</returns>
    /// <remarks>
    /// AGT-8 says the environment is stable within a session, and UPD-2 says the
    /// CLI never touches the network. Nothing here does either: every check is a
    /// local question with a local answer, which is also what makes it usable as
    /// the first thing an agent runs.
    /// </remarks>
    public static DoctorOutcome Doctor(string? root)
    {
        var checks = new List<DoctorCheck>
        {
            new("engine", true, EngineBuild.Version),
            new("runtime", true, Environment.Version.ToString()),
            new("platform", true, System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()),
        };

        // The schema is generated from XML documentation that ships beside the
        // assembly. Its absence is not fatal and does not stop a run, but it makes
        // the schema descriptions disappear, and an agent that had them yesterday
        // should be told why it does not have them today.
        var xml = Path.ChangeExtension(typeof(Core.Model.ModelDocument).Assembly.Location, ".xml");

        checks.Add(new DoctorCheck(
            "schema descriptions",
            File.Exists(xml),
            File.Exists(xml)
                ? "available"
                : "unavailable: no XML documentation beside the engine, so 'einzel schema' will emit "
                    + "structure without descriptions"));

        checks.Add(new DoctorCheck(
            "templates",
            Library.DeviceTemplates.Names().Count > 0,
            string.Join(", ", Library.DeviceTemplates.Names().OrderBy(n => n, StringComparer.Ordinal))));

        checks.Add(new DoctorCheck(
            "examples",
            ExampleModels.Names.Count > 0,
            string.Join(", ", ExampleModels.Names)));

        // Reported as a check rather than assumed, because EXT-6 wants a vendored
        // interpreter and this build discovers one. Not healthy when absent: an
        // extension cannot run without it, and finding that out at the first
        // 'ext test' is later than finding it out here.
        var interpreter = Extensions.SubprocessRunner.Discover();

        checks.Add(new DoctorCheck(
            "python interpreter",
            interpreter is not null,
            interpreter is not null
                ? $"{interpreter} (discovered, not vendored - EXT-6 wants one shipped)"
                : "not found: extensions cannot run. Install python3, or put it on the path"));

        checks.Add(new DoctorCheck(
            "extension isolation",
            true,
            "subprocess, scrubbed environment, isolated interpreter, wall-clock timeout. NOT "
            + "enforced: " + string.Join("; ", Extensions.Sandbox.Unenforced.Select(c => c.Name))));

        if (root is not null)
        {
            var layout = new ProjectLayout(Path.GetFullPath(root));
            var models = Directory.Exists(layout.Models);

            checks.Add(new DoctorCheck(
                "project",
                models,
                models ? layout.Root : $"no models directory under {layout.Root}; run 'einzel init' there"));

            if (models)
            {
                var count = Directory.EnumerateFiles(layout.Models, "*.json").Count();
                checks.Add(new DoctorCheck("models", count > 0, $"{count} model file(s)"));
                checks.Add(GuidanceDrift(layout));
            }
        }

        return new DoctorOutcome { Checks = checks };
    }

    /// <summary>
    /// Whether a project's generated guidance matches the engine that is running.
    /// </summary>
    /// <remarks>
    /// The failure PRJ-6 exists to prevent, made visible. Guidance written for
    /// v1.0 sitting in a project driven by v1.2 is worse than no guidance, because
    /// an agent trusts it and has no way to notice it is stale. The version stamp
    /// in the generated region is what makes the staleness detectable at all, and
    /// this is the thing that looks.
    /// </remarks>
    private static DoctorCheck GuidanceDrift(ProjectLayout layout)
    {
        if (!File.Exists(layout.AgentsFile))
        {
            return new DoctorCheck(
                "AGENTS.md",
                false,
                "missing; run 'einzel agents-md' to generate the platform layer");
        }

        var recorded = AgentsFile.RecordedVersion(File.ReadAllText(layout.AgentsFile));
        var running = EngineBuild.Version;

        if (recorded is null)
        {
            return new DoctorCheck(
                "AGENTS.md",
                false,
                "present but carries no version stamp, so whether its guidance matches this engine cannot "
                    + "be established; run 'einzel agents-md'");
        }

        return string.Equals(recorded, running, StringComparison.Ordinal)
            ? new DoctorCheck("AGENTS.md", true, $"generated by {recorded}, which is what is running")
            : new DoctorCheck(
                "AGENTS.md",
                false,
                $"generated by {recorded} but this engine is {running}; the guidance may describe behaviour "
                    + "this build does not have. Run 'einzel agents-md'");
    }
}
