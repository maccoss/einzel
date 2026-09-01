using System.Text.Json.Nodes;
using Einzel.Core.Errors;
using Einzel.Extensions;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>One installed extension, on the wire.</summary>
public sealed record ExtensionEntry
{
    /// <summary>The name a model or study selects it by.</summary>
    public required string Name { get; init; }

    /// <summary>Its own version, carried onto every result it produces.</summary>
    public required string Version { get; init; }

    /// <summary>What it extends.</summary>
    public required string Kind { get; init; }

    /// <summary>Which runner it asks for.</summary>
    public required string Trust { get; init; }

    /// <summary>One sentence saying what it does.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// What the extension is offered under, or null when it does not say (LIC-2).
    /// </summary>
    /// <remarks>
    /// Null rather than a placeholder, so a caller cannot mistake "did not declare one"
    /// for a licence it recognises. The renderers say "not declared" in words.
    /// </remarks>
    public string? Licence { get; init; }

    /// <summary>Where it lives, relative to the project root.</summary>
    public required string Directory { get; init; }

    /// <summary>Why this engine cannot run it, or null when it can (EXT-8).</summary>
    public string? Incompatibility { get; init; }
}

/// <summary>What the extension catalogue holds.</summary>
public sealed record ExtensionListOutcome
{
    /// <summary>The engine version the compatibility check was made against.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>The interpreter found, or null when none was.</summary>
    public string? Interpreter { get; init; }

    /// <summary>Containment measures this build does not enforce (EXT-3).</summary>
    public required IReadOnlyList<string> UnenforcedContainment { get; init; }

    /// <summary>What is installed, ordered by name.</summary>
    public required IReadOnlyList<ExtensionEntry> Extensions { get; init; }
}

/// <summary>What one extension test call produced.</summary>
public sealed record ExtensionTestOutcome
{
    /// <summary>The extension that was called.</summary>
    public required string Name { get; init; }

    /// <summary>Its version.</summary>
    public required string Version { get; init; }

    /// <summary>What it was handed.</summary>
    public required JsonNode? Input { get; init; }

    /// <summary>What it returned.</summary>
    public required JsonNode? Output { get; init; }

    /// <summary>Wall-clock milliseconds for the round trip (PERF-7).</summary>
    public required double ElapsedMs { get; init; }

    /// <summary>Whatever it wrote to standard error.</summary>
    public string? Diagnostics { get; init; }

    /// <summary>Warnings the result carries, per GRD-2 and GRD-6.</summary>
    public required IReadOnlyList<Io.WarningJson> Warnings { get; init; }
}

/// <summary>
/// The extension authoring loop: list what is installed, scaffold a new one, call
/// one and see what comes back.
/// </summary>
/// <remarks>
/// Section 15 names this loop <c>ext test</c> and <c>ext register</c>. It matters
/// more than it looks: an agent that cannot see what its extension returned has to
/// debug it through whatever consumed it, and that is the loop that decides whether
/// anybody writes a second extension.
/// </remarks>
public static class ExtensionCommand
{
    /// <summary>Lists what is installed.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    public static ExtensionListOutcome List(ProjectLayout project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entries = new List<ExtensionEntry>();

        foreach (var installed in ExtensionCatalogue.Discover(project.Extensions))
        {
            entries.Add(new ExtensionEntry
            {
                Name = installed.Manifest.Name!,
                Version = installed.Manifest.Version,
                Kind = installed.Manifest.Kind.ToString(),
                Trust = installed.Manifest.Trust.ToString(),
                Description = installed.Manifest.Description,
                Licence = installed.Manifest.Licence,
                Directory = Path.GetRelativePath(project.Root, installed.Directory),
                Incompatibility =
                    ExtensionCatalogue.Incompatibility(installed.Manifest, EngineBuild.Version),
            });
        }

        return new ExtensionListOutcome
        {
            EngineVersion = EngineBuild.Version,
            Interpreter = SubprocessRunner.Discover(),
            UnenforcedContainment = [.. Sandbox.Unenforced.Select(c => $"{c.Name}: {c.How}")],
            Extensions = entries,
        };
    }

    /// <summary>Calls one extension with a payload and reports what came back.</summary>
    /// <param name="project">The project.</param>
    /// <param name="name">Which extension.</param>
    /// <param name="input">The document to hand it, or null.</param>
    /// <returns>What it returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="EinzelException">It is not installed, or the call failed.</exception>
    public static ExtensionTestOutcome Test(ProjectLayout project, string name, JsonNode? input)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var installed = ExtensionCatalogue.Discover(project.Extensions)
            .FirstOrDefault(e => string.Equals(e.Manifest.Name, name, StringComparison.Ordinal))
            ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"no extension named '{name}' is installed",
                Suggestion = "run 'einzel ext list' to see what is, or 'einzel ext register "
                    + $"{name}' to scaffold it",
            });

        var interpreter = SubprocessRunner.Discover()
            ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.InternalError,
                Path = "/",
                Constraint = "no Python 3 interpreter was found",
                Suggestion = "EXT-6 wants a vendored interpreter and this build discovers one "
                    + "instead, so extensions need python3 on the path",
            });

        // The declared input schema is checked before the call rather than after, so
        // a payload that was never going to work fails here with a path into the
        // payload instead of somewhere inside somebody's Python.
        SchemaCheck.Validate(input, installed.Manifest.InputSchema, installed.Manifest.Name!);

        var result = new SubprocessRunner(interpreter).Run(
            installed.Manifest, installed.Directory, input, Path.Combine(project.Scratch, "ext"));

        return new ExtensionTestOutcome
        {
            Name = installed.Manifest.Name!,
            Version = installed.Manifest.Version,
            Input = input,
            Output = result.Output,
            ElapsedMs = result.ElapsedMs,
            Diagnostics = string.IsNullOrWhiteSpace(result.Diagnostics) ? null : result.Diagnostics,
            Warnings =
            [
                .. result.Warnings.Select(w => new Io.WarningJson
                {
                    Code = w.Code,
                    Message = w.Message,
                    Severity = w.Severity.ToString(),
                    Suppressible = w.IsSuppressible,
                }),
            ],
        };
    }

    /// <summary>Scaffolds a new extension that runs before it does anything useful.</summary>
    /// <param name="project">The project.</param>
    /// <param name="name">What to call it.</param>
    /// <param name="kind">What it extends.</param>
    /// <param name="dryRun">Report what would be written without writing it.</param>
    /// <returns>The files created, relative to the project root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="EinzelException">Something is already installed under that name.</exception>
    /// <remarks>
    /// The scaffold is a working extension rather than a stub, for the same reason
    /// <c>einzel init</c> writes a model that runs: an agent with a template that
    /// works can change one thing and see what happens, where an agent with a
    /// template that does not has to fix somebody else's code before it can start.
    /// </remarks>
    public static IReadOnlyList<string> Register(
        ProjectLayout project, string name, ExtensionKind kind, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var folder = Path.Combine(project.Extensions, name);
        var manifestPath = Path.Combine(folder, ExtensionCatalogue.ManifestName);
        var entryPath = Path.Combine(folder, "extension.py");

        if (File.Exists(manifestPath))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"{Path.GetRelativePath(project.Root, manifestPath)} already exists",
                Suggestion = "pick another name, or edit the extension that is there",
            });
        }

        var created = new[]
        {
            Path.GetRelativePath(project.Root, manifestPath),
            Path.GetRelativePath(project.Root, entryPath),
        };

        if (dryRun)
        {
            return created;
        }

        Directory.CreateDirectory(folder);

        var manifest = new ExtensionManifest
        {
            Name = name,
            Version = "0.1.0",
            Description = $"An objective composing the engine's own figures of merit.",

            // Scaffolded with a licence already in it, for the reason `init` writes a
            // model that runs: a field that has to be added later is one that gets left
            // out. Apache-2.0 matches this repository's own, and an author who wants
            // something else edits one line - which is a better prompt than an absence.
            Licence = "Apache-2.0",
            Kind = kind,
            Trust = ExtensionTrust.Sandboxed,
            Entry = "extension.py",
            Function = "run",
            Figures = ["resolvingPower", "flightTime"],
            EngineMinimum = EngineBuild.Version.Split('+')[0],
            OutputSchema = JsonNode.Parse("""
            {
              "type": "object",
              "required": ["value"],
              "properties": { "value": { "type": "number" } }
            }
            """),
        };

        File.WriteAllText(
            manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, ExtensionCatalogue.Options) + "\n");

        File.WriteAllText(entryPath, """
        \"\"\"An objective extension.

        Called once per design, never per integration step (EXT-4). The payload
        carries the model's declared parameters in SI and whichever figures of merit
        the manifest asked for; a figure that could not be computed is None rather
        than missing, so losing the beam is distinguishable from not asking.

        Return a dict with a numeric 'value'. An optimiser minimises it.
        \"\"\"


        def run(payload):
            figures = payload["figures"]

            resolving_power = figures.get("resolvingPower")

            # A design that loses its beam has no resolving power to report. Return
            # a large finite number rather than infinity: a simplex reflecting onto
            # infinity learns nothing about which way to go.
            if resolving_power is None or resolving_power <= 0.0:
                return {"value": 1e9, "reason": "no ions arrived"}

            return {"value": -resolving_power}
        """.Replace("\\\"\\\"\\\"", "\"\"\"", StringComparison.Ordinal));

        return created;
    }
}
