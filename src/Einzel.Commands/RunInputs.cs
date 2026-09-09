using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>Snapshots the external inputs before computing, not when writing the result (PRJ-3).</summary>
internal static class RunInputs
{
    private static IEnumerable<(string Path, string Pointer)> References(ModelDocument document)
    {
        if (document.Transport?.Gas?.VelocityField?.Path is string velocity && !string.IsNullOrWhiteSpace(velocity))
            yield return (velocity, "/transport/gas/velocityField/path");
        if (document.Transport?.Gas?.PressureField?.Path is string pressure && !string.IsNullOrWhiteSpace(pressure))
            yield return (pressure, "/transport/gas/pressureField/path");
    }

    internal static IEnumerable<string> ImportedPaths(ModelDocument document) =>
        References(document).Select(reference => reference.Path);

    internal static IReadOnlyDictionary<string, string> Capture(
        ModelDocument document, string modelPath, string root, string? studyPath = null)
    {
        var paths = References(document)
            .Select(reference => (Path: Path.GetFullPath(reference.Path, Path.GetDirectoryName(modelPath)!), reference.Pointer));
        if (studyPath is not null) paths = paths.Append((studyPath, "/study"));
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, pointer) in paths)
        {
            try
            {
                hashes[RunManifest.Portable(Path.GetRelativePath(root, path))] = ContentHash.OfFile(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = pointer,
                    Constraint = $"the input file {path} must be readable before its contents can be recorded",
                    Suggestion = $"check the path and read permissions: {ex.Message}",
                });
            }
        }
        return hashes;
    }

    internal static IReadOnlyDictionary<string, string> Checked(
        IReadOnlyDictionary<string, string> hashes, string root)
    {
        foreach (var (relative, expected) in hashes)
        {
            var path = Path.GetFullPath(RunManifest.Local(relative), root);
            if (!File.Exists(path) || ContentHash.OfFile(path) != expected)
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = relative,
                    Constraint = "an input file changed during the calculation; its consumed content cannot be established",
                    Suggestion = "restore stable input files and rerun before publishing a result",
                });
        }
        return hashes;
    }
}
