using System.Text.Json;
using System.Text.Json.Serialization;
using Einzel.Core.Errors;

namespace Einzel.Extensions;

/// <summary>An extension found on disk, with where it was found.</summary>
/// <param name="Manifest">What it declares.</param>
/// <param name="Directory">The folder holding the manifest and the entry file.</param>
/// <param name="ManifestPath">The manifest file itself.</param>
public sealed record InstalledExtension(
    ExtensionManifest Manifest, string Directory, string ManifestPath);

/// <summary>
/// Finds the extensions a project declares, and says which ones this engine can run.
/// </summary>
/// <remarks>
/// A project is a directory (spec section 3), so extensions are found by looking
/// rather than by registration in a database somewhere. <c>einzel ext register</c>
/// scaffolds one; nothing has to be told about it afterwards, and copying a folder
/// in is a complete install.
/// </remarks>
public static class ExtensionCatalogue
{
    /// <summary>The options extension manifests are read with.</summary>
    /// <remarks>
    /// Case-insensitive on purpose. A manifest is hand-written far more often than
    /// a model is, and refusing <c>OutputSchema</c> because the format says
    /// <c>outputSchema</c> is a poor first experience for a format nobody has read
    /// yet.
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        NewLine = "\n",
    };

    /// <summary>The file an extension declares itself in.</summary>
    public const string ManifestName = "extension.json";

    /// <summary>Finds every extension under a folder.</summary>
    /// <param name="extensionsRoot">The project's extensions directory.</param>
    /// <returns>What was found, ordered by name.</returns>
    /// <exception cref="ArgumentException"><paramref name="extensionsRoot"/> is null or blank.</exception>
    /// <exception cref="EinzelException">A manifest exists and does not parse.</exception>
    public static IReadOnlyList<InstalledExtension> Discover(string extensionsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionsRoot);

        if (!Directory.Exists(extensionsRoot))
        {
            return [];
        }

        var found = new List<InstalledExtension>();

        foreach (var folder in Directory.EnumerateDirectories(extensionsRoot).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(folder, ManifestName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            found.Add(new InstalledExtension(Read(manifestPath), folder, manifestPath));
        }

        // By declared name rather than by folder, since the name is what a model or
        // a study selects it by. CLI-5 wants deterministic ordering everywhere.
        return [.. found.OrderBy(e => e.Manifest.Name ?? string.Empty, StringComparer.Ordinal)];
    }

    /// <summary>Reads one manifest.</summary>
    /// <param name="manifestPath">Path to the manifest.</param>
    /// <returns>What it declares.</returns>
    /// <exception cref="ArgumentException"><paramref name="manifestPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">It does not parse, or omits something required.</exception>
    public static ExtensionManifest Read(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        ExtensionManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(
                File.ReadAllText(manifestPath), Options);
        }
        catch (JsonException malformed)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = malformed.Path ?? "/",
                Constraint = malformed.Message,
                Suggestion = "'kind' is one of: geometry, analysis, objective, sequence, "
                    + "interchange. 'trust' is one of: sandboxed, trusted",
            });
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/name",
                Constraint = $"{manifestPath} does not declare a name",
                Suggestion = "an extension is selected by name from a model or a study, so it "
                    + "needs one that is unique within the project",
            });
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/entry",
                Constraint = $"'{manifest.Name}' does not say which file to run",
                Suggestion = "add \"entry\": \"extension.py\"",
            });
        }

        return manifest;
    }

    /// <summary>
    /// Whether an extension declares itself compatible with an engine version.
    /// </summary>
    /// <param name="manifest">The extension.</param>
    /// <param name="engineVersion">The running engine version.</param>
    /// <returns>Why it is incompatible, or null when it is compatible.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is null.</exception>
    /// <remarks>
    /// EXT-8 makes the updater report which installed extensions fall outside a new
    /// engine's range <em>before</em> the update is applied. That check is this
    /// function; the updater that calls it is not built.
    /// </remarks>
    public static string? Incompatibility(ExtensionManifest manifest, string engineVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var engine = Parse(engineVersion);

        if (engine is null)
        {
            return null;
        }

        if (Parse(manifest.EngineMinimum) is { } least && Compare(engine.Value, least) < 0)
        {
            return $"needs engine {manifest.EngineMinimum} or later; this is {engineVersion}";
        }

        if (Parse(manifest.EngineMaximum) is { } most && Compare(engine.Value, most) >= 0)
        {
            return $"declares itself untested from engine {manifest.EngineMaximum}; this is {engineVersion}";
        }

        return null;
    }

    /// <summary>Parses the leading numeric part of a version, ignoring build metadata.</summary>
    private static (int Major, int Minor, int Patch)? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var core = version.Split('+', '-')[0].Split('.');

        if (core.Length == 0 || !int.TryParse(core[0], out var major))
        {
            return null;
        }

        var minor = core.Length > 1 && int.TryParse(core[1], out var m) ? m : 0;
        var patch = core.Length > 2 && int.TryParse(core[2], out var p) ? p : 0;

        return (major, minor, patch);
    }

    private static int Compare((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch) right)
    {
        if (left.Major != right.Major)
        {
            return left.Major.CompareTo(right.Major);
        }

        return left.Minor != right.Minor
            ? left.Minor.CompareTo(right.Minor)
            : left.Patch.CompareTo(right.Patch);
    }
}
