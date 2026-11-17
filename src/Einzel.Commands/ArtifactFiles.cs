using Einzel.Core.Errors;

namespace Einzel.Commands;

/// <summary>
/// Writes the files a command was asked for, or refuses by the flag that named them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A destination that cannot be written is the caller's mistake, not a defect in the
/// engine.</b> <c>--out</c> naming a directory, or a file in a folder nobody may write to, came
/// out of <c>File.WriteAllBytes</c> as an <see cref="UnauthorizedAccessException"/> and was
/// reported as an internal error, exit 6, with a stack trace and a request to file a bug. Here
/// it is a refusal located at the flag (AGT-3).
/// </para>
/// <para>
/// <b>Checked before the work where that is cheap to know</b> (<see cref="Check"/>): a path
/// that is a directory, or whose folder is a file, is known without solving anything.
/// </para>
/// <para>
/// <b>All of them or none</b> (<see cref="WriteAll"/>): each file is written whole to a
/// temporary file beside its destination, and only when every one of those is on disk are they
/// moved into place. A command asked for a picture and a table used to write the picture and
/// then fail on the table, leaving one of the two.
/// </para>
/// </remarks>
public static class ArtifactFiles
{
    /// <summary>Refuses a destination that cannot be a file, without touching it.</summary>
    /// <param name="flag">The flag that names it, without its dashes.</param>
    /// <param name="path">The destination.</param>
    /// <exception cref="EinzelException">The path is a directory, or a folder on the way to it is a file.</exception>
    public static void Check(string flag, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);

        if (Directory.Exists(full))
        {
            throw Refusal(flag, full, "it is a directory", $"give --{flag} a file inside it");
        }

        for (var folder = Path.GetDirectoryName(full); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            if (File.Exists(folder))
            {
                throw Refusal(flag, full, $"{folder} is a file, not a folder", null);
            }

            if (Directory.Exists(folder))
            {
                break;
            }
        }
    }

    /// <summary>Writes one file, whole or not at all.</summary>
    /// <param name="flag">The flag that names it, without its dashes.</param>
    /// <param name="path">The destination.</param>
    /// <param name="content">Its bytes.</param>
    /// <exception cref="EinzelException">It could not be written.</exception>
    public static void Write(string flag, string path, byte[] content) => WriteAll([(flag, path, content)]);

    /// <summary>Writes one text file as UTF-8 without a byte-order mark, whole or not at all.</summary>
    /// <param name="flag">The flag that names it, without its dashes.</param>
    /// <param name="path">The destination.</param>
    /// <param name="text">Its text.</param>
    /// <exception cref="EinzelException">It could not be written.</exception>
    public static void WriteText(string flag, string path, string text) => Write(flag, path, Utf8(text));

    /// <summary>Text as the bytes <see cref="WriteText"/> writes.</summary>
    /// <param name="text">The text.</param>
    /// <returns>UTF-8, no byte-order mark.</returns>
    public static byte[] Utf8(string text) => new System.Text.UTF8Encoding(false).GetBytes(text);

    /// <summary>Writes several files: every one of them, or none.</summary>
    /// <param name="files">Each file's flag, destination and bytes.</param>
    /// <exception cref="EinzelException">One could not be written; none is left behind by this call.</exception>
    public static void WriteAll(IReadOnlyList<(string Flag, string Path, byte[] Content)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (var (flag, path, _) in files)
        {
            Check(flag, path);
        }

        var temporary = new string?[files.Count];
        var moved = new List<string>(files.Count);
        var at = 0;

        try
        {
            // Every file whole on disk first, beside where it is going...
            for (at = 0; at < files.Count; at++)
            {
                var full = Path.GetFullPath(files[at].Path);

                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                temporary[at] = $"{full}.{Guid.NewGuid():N}.tmp";
                File.WriteAllBytes(temporary[at]!, files[at].Content);
            }

            // ...and only then into place.
            for (at = 0; at < files.Count; at++)
            {
                var full = Path.GetFullPath(files[at].Path);

                File.Move(temporary[at]!, full, overwrite: true);
                temporary[at] = null;
                moved.Add(full);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            // None of them, then: what this call put in place is taken away again.
            foreach (var written in moved)
            {
                TryDelete(written);
            }

            var (flag, path, _) = files[Math.Min(at, files.Count - 1)];

            throw Refusal(flag, Path.GetFullPath(path), failure.Message, null);
        }
        finally
        {
            foreach (var left in temporary)
            {
                if (left is not null)
                {
                    TryDelete(left);
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do for a file that cannot be removed; the refusal is still raised.
        }
    }

    private static EinzelException Refusal(string flag, string path, string why, string? suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = $"/args/--{flag}",
            Constraint = $"cannot write {path}: {why}",
            Suggestion = suggestion ?? $"give --{flag} a file that can be written",
        });
}
