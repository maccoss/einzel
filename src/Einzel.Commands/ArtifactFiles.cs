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

    /// <summary>
    /// Refuses a destination that is one of the files the command reads: the model, a render
    /// spec, a study.
    /// </summary>
    /// <param name="flag">The flag that names the destination, without its dashes.</param>
    /// <param name="path">The destination.</param>
    /// <param name="inputs">The files the command reads.</param>
    /// <exception cref="EinzelException">The destination is one of them.</exception>
    /// <remarks>
    /// <c>--out models/lens.json</c> was accepted, and the picture was written over the model it
    /// was a picture of. Compared as the file system compares names: without regard to case on
    /// Windows and macOS.
    /// </remarks>
    public static void CheckNotAnInput(string flag, string path, params string?[] inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(inputs);

        foreach (var input in inputs)
        {
            if (input is not null && Same(path, input))
            {
                throw Refusal(
                    flag,
                    Path.GetFullPath(path),
                    "it is the file this command reads, and writing there would replace it",
                    $"give --{flag} a file of its own");
            }
        }
    }

    /// <summary>Whether two paths name one file.</summary>
    /// <param name="first">One path.</param>
    /// <param name="second">The other.</param>
    /// <returns>True where they are the same file name, as the file system compares names.</returns>
    public static bool Same(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Writes one file, whole or not at all.</summary>
    /// <param name="flag">The flag that names it, without its dashes.</param>
    /// <param name="path">The destination.</param>
    /// <param name="content">Its bytes.</param>
    /// <param name="inputs">The files the command reads, none of which the destination may be.</param>
    /// <exception cref="EinzelException">It could not be written.</exception>
    public static void Write(string flag, string path, byte[] content, params string?[] inputs) =>
        WriteAll([(flag, path, content)], inputs);

    /// <summary>Writes one text file as UTF-8 without a byte-order mark, whole or not at all.</summary>
    /// <param name="flag">The flag that names it, without its dashes.</param>
    /// <param name="path">The destination.</param>
    /// <param name="text">Its text.</param>
    /// <param name="inputs">The files the command reads, none of which the destination may be.</param>
    /// <exception cref="EinzelException">It could not be written.</exception>
    public static void WriteText(string flag, string path, string text, params string?[] inputs) =>
        Write(flag, path, Utf8(text), inputs);

    /// <summary>Text as the bytes <see cref="WriteText"/> writes.</summary>
    /// <param name="text">The text.</param>
    /// <returns>UTF-8, no byte-order mark.</returns>
    public static byte[] Utf8(string text) => new System.Text.UTF8Encoding(false).GetBytes(text);

    /// <summary>
    /// Refuses a set of destinations any of which cannot be a file, is a file the command reads,
    /// or is the same file as another of them - without touching any.
    /// </summary>
    /// <param name="destinations">Each destination's flag and path.</param>
    /// <param name="inputs">The files the command reads.</param>
    /// <exception cref="EinzelException">One of them is refused.</exception>
    public static void CheckAll(IReadOnlyList<(string Flag, string Path)> destinations, params string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        for (var i = 0; i < destinations.Count; i++)
        {
            var (flag, path) = destinations[i];

            Check(flag, path);
            CheckNotAnInput(flag, path, inputs);

            for (var j = 0; j < i; j++)
            {
                if (Same(path, destinations[j].Path))
                {
                    throw Refusal(
                        flag,
                        Path.GetFullPath(path),
                        $"--{destinations[j].Flag} names the same file, and one would be written over the other",
                        $"give --{flag} a file of its own");
                }
            }
        }
    }

    /// <summary>Writes several files: every one of them, or none.</summary>
    /// <param name="files">Each file's flag, destination and bytes.</param>
    /// <param name="inputs">The files the command reads, none of which a destination may be.</param>
    /// <exception cref="EinzelException">
    /// One could not be written. Nothing this call did is left behind: a file it put in place is
    /// taken away again, and a file that was there before the call is put back as it was.
    /// </exception>
    public static void WriteAll(IReadOnlyList<(string Flag, string Path, byte[] Content)> files, params string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(files);

        CheckAll([.. files.Select(f => (f.Flag, f.Path))], inputs);

        var temporary = new string?[files.Count];

        // What this call has put in place, and for each the file it displaced, if there was one.
        var placed = new List<(string Path, string? Displaced)>(files.Count);
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

            // ...and only then into place, each one's predecessor moved aside first, so that a
            // later failure can put it back. Deleting what this call placed is not undoing it
            // where there was a file before: that deleted somebody's earlier picture.
            for (at = 0; at < files.Count; at++)
            {
                var full = Path.GetFullPath(files[at].Path);
                string? aside = null;

                if (File.Exists(full))
                {
                    aside = $"{full}.{Guid.NewGuid():N}.bak";
                    File.Move(full, aside);
                }

                try
                {
                    File.Move(temporary[at]!, full);
                }
                catch
                {
                    if (aside is not null)
                    {
                        TryRestore(aside, full);
                    }

                    throw;
                }

                temporary[at] = null;
                placed.Add((full, aside));
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            // None of them, then.
            foreach (var (path, displaced) in placed)
            {
                TryDelete(path);

                if (displaced is not null)
                {
                    TryRestore(displaced, path);
                }
            }

            placed.Clear();

            var (flag, target, _) = files[Math.Min(at, files.Count - 1)];

            throw Refusal(flag, Path.GetFullPath(target), failure.Message, null);
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

            // All in place: what was displaced is no longer wanted.
            foreach (var (_, displaced) in placed)
            {
                if (displaced is not null)
                {
                    TryDelete(displaced);
                }
            }
        }
    }

    private static void TryRestore(string aside, string path)
    {
        try
        {
            File.Move(aside, path, overwrite: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Left beside where it was, under its .bak name, rather than lost.
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
