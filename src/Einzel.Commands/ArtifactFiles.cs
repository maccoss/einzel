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
/// renamed into place, each in one step. A command asked for a picture and a table used to write
/// the picture and then fail on the table, leaving one of the two. What a file system cannot give
/// across two files is said there.
/// </para>
/// <para>
/// <b>Located where the user can act.</b> A file named by a flag is refused at that flag; one the
/// user named no flag for - a default destination, a file in the project's scratch folder - is
/// refused at the root with the file in the message, not at a flag that did not name it.
/// </para>
/// </remarks>
public static class ArtifactFiles
{
    /// <summary>Refuses a destination that cannot be a file, without touching it.</summary>
    /// <param name="flag">The flag that names it, without its dashes; null where the user named none (a default destination).</param>
    /// <param name="path">The destination.</param>
    /// <exception cref="EinzelException">The path is a directory, or a folder on the way to it is a file.</exception>
    public static void Check(string? flag, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);

        if (Directory.Exists(full))
        {
            throw Refusal(flag, full, "it is a directory", flag is null ? $"move the folder {full} out of the way" : $"give --{flag} a file inside it");
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
    /// <param name="flag">The flag that names the destination, without its dashes; null where the user named none.</param>
    /// <param name="path">The destination.</param>
    /// <param name="inputs">The files the command reads.</param>
    /// <exception cref="EinzelException">The destination is one of them.</exception>
    /// <remarks>
    /// <c>--out models/lens.json</c> was accepted, and the picture was written over the model it
    /// was a picture of. Compared as the file system compares names: without regard to case on
    /// Windows and macOS.
    /// </remarks>
    public static void CheckNotAnInput(string? flag, string path, params string?[] inputs)
    {
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
                    flag is null ? "name the output with its flag, a file of its own" : $"give --{flag} a file of its own");
            }
        }
    }

    /// <summary>
    /// Refuses a folder of numbered frames before any is drawn: a folder that is a file, or one
    /// holding a file the command reads under a name a frame or the manifest would take.
    /// </summary>
    /// <param name="flag">The flag that names the folder, without its dashes; null where the user named none.</param>
    /// <param name="directory">The folder the frames go in.</param>
    /// <param name="extension">The frames' extension, with its dot.</param>
    /// <param name="inputs">The files the command reads.</param>
    /// <exception cref="EinzelException">The folder is refused.</exception>
    /// <remarks>
    /// The frames are named <c>frame-00000</c> onwards and the manifest <c>frames.json</c>, so which
    /// names would be taken is known before the frames are counted: a file the command reads under
    /// one of them is refused now rather than after the run that draws them.
    /// </remarks>
    public static void CheckFrames(string? flag, string directory, string extension, params string?[] inputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(inputs);

        var full = Path.GetFullPath(directory);

        if (File.Exists(full))
        {
            throw Refusal(flag, full, "it is a file, and the frames need a folder",
                flag is null ? $"move {full} out of the way" : $"give --{flag} a folder");
        }

        // The manifest's own path settles a folder on the way that is a file.
        Check(flag, Path.Combine(full, "frames.json"));

        foreach (var input in inputs)
        {
            if (input is null || Path.GetDirectoryName(Path.GetFullPath(input)) is not { } folder || !Same(folder, full))
            {
                continue;
            }

            var file = Path.GetFileName(input);
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var frame = file.StartsWith("frame-", comparison)
                && file.EndsWith(extension, comparison)
                && file.Length > "frame-".Length + extension.Length
                && file["frame-".Length..^extension.Length].All(char.IsAsciiDigit);

            if (frame || string.Equals(file, "frames.json", comparison))
            {
                throw Refusal(
                    flag,
                    Path.GetFullPath(input),
                    "it is a file this command reads, and a frame or the manifest would be written over it",
                    flag is null ? "name the folder with --out, one of its own" : $"give --{flag} a folder of its own");
            }
        }
    }

    /// <summary>Whether two paths name one file.</summary>
    /// <param name="first">One path.</param>
    /// <param name="second">The other.</param>
    /// <returns>
    /// True where they are the same file once each symbolic link is followed to the file it
    /// names, as the file system compares names.
    /// </returns>
    /// <remarks>
    /// Compared where the bytes would go, because that is where they are written: a write goes
    /// through a link to its target, so <c>--out link.png</c> with <c>link.png</c> naming the
    /// model replaced the model while a comparison of the names said they differed.
    /// </remarks>
    public static bool Same(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Resolved(first)),
        Path.TrimEndingDirectorySeparator(Resolved(second)),
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // A path as a full path, followed through a symbolic link to the end where it is one.
    private static string Resolved(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);

        return info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } final
            ? Path.GetFullPath(final.FullName)
            : full;
    }

    /// <summary>Writes one file, whole or not at all.</summary>
    /// <param name="flag">The flag that names it, without its dashes; null where the user named none.</param>
    /// <param name="path">The destination.</param>
    /// <param name="content">Its bytes.</param>
    /// <param name="inputs">The files the command reads, none of which the destination may be.</param>
    /// <exception cref="EinzelException">It could not be written.</exception>
    public static void Write(string? flag, string path, byte[] content, params string?[] inputs) =>
        WriteAll([(flag, path, content)], inputs);

    /// <summary>Writes one text file as UTF-8 without a byte-order mark, whole or not at all.</summary>
    /// <param name="flag">The flag that names it, without its dashes; null where the user named none.</param>
    /// <param name="path">The destination.</param>
    /// <param name="text">Its text.</param>
    /// <param name="inputs">The files the command reads, none of which the destination may be.</param>
    /// <exception cref="EinzelException">It could not be written.</exception>
    public static void WriteText(string? flag, string path, string text, params string?[] inputs) =>
        Write(flag, path, Utf8(text), inputs);

    /// <summary>Text as the bytes <see cref="WriteText"/> writes.</summary>
    /// <param name="text">The text.</param>
    /// <returns>UTF-8, no byte-order mark.</returns>
    public static byte[] Utf8(string text) => new System.Text.UTF8Encoding(false).GetBytes(text);

    /// <summary>
    /// Refuses a set of destinations any of which cannot be a file, is a file the command reads,
    /// or is the same file as another of them - without touching any.
    /// </summary>
    /// <param name="destinations">Each destination's flag (null where the user named none) and path.</param>
    /// <param name="inputs">The files the command reads.</param>
    /// <exception cref="EinzelException">One of them is refused.</exception>
    public static void CheckAll(IReadOnlyList<(string? Flag, string Path)> destinations, params string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        // Each destination resolved once and looked up, not compared with every other: a film is
        // hundreds of frames, and every comparison follows links on disk.
        var seen = new Dictionary<string, int>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        for (var i = 0; i < destinations.Count; i++)
        {
            var (flag, path) = destinations[i];

            Check(flag, path);
            CheckNotAnInput(flag, path, inputs);

            var target = Path.TrimEndingDirectorySeparator(Resolved(path));

            if (seen.TryGetValue(target, out var j))
            {
                throw Refusal(
                    flag,
                    Path.GetFullPath(path),
                    (destinations[j].Flag is { } other ? $"--{other}" : "another output") + " names the same file, and one would be written over the other",
                    flag is null ? "name the output with its flag, a file of its own" : $"give --{flag} a file of its own");
            }

            seen[target] = i;
        }
    }

    /// <summary>Writes several files: every one of them, or none, as far as a file system allows.</summary>
    /// <param name="files">Each file's flag (null where the user named no flag for it), destination and bytes.</param>
    /// <param name="inputs">The files the command reads, none of which a destination may be.</param>
    /// <exception cref="EinzelException">
    /// One could not be written, or an existing destination is read-only. Nothing this call did is
    /// left behind: a file it put in place is taken away again, and a file that was there before
    /// the call is put back as it was.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Each file is replaced in one step.</b> Its bytes are written whole to a temporary file
    /// beside it, and the temporary is renamed over the destination - <see cref="File.Move(string, string, bool)"/>
    /// with overwrite, or <see cref="File.Replace(string, string, string?, bool)"/> where the old
    /// file must be kept until other files are in place. A process
    /// killed at any instant leaves the old file or the new one at the destination, never neither:
    /// the first version moved the old file aside and then moved the new one in, and a kill between
    /// the two left no file at all and a stray <c>.bak</c>.
    /// </para>
    /// <para>
    /// <b>A link is written through, not replaced.</b> A destination that is a symbolic link is
    /// resolved to the file it names, and that file is replaced: renaming onto the link itself
    /// would turn it into an ordinary file and leave the file it pointed at as it was.
    /// </para>
    /// <para>
    /// <b>A read-only destination is refused</b>, as a plain write always refused it. A rename does
    /// not ask whether the file it replaces is read-only, so this asks first.
    /// </para>
    /// <para>
    /// <b>Across several files the guarantee is weaker, and this is what it is.</b> No file system
    /// here renames two files in one step. Every file is first written whole beside its
    /// destination; they are then put in place one after another, each replaced file kept under a
    /// backup name until the last is in place. If one fails, those already placed are put back
    /// from their backups (or removed, where there was nothing before) and the call refuses. What
    /// cannot be covered is the process dying between the first rename and the last: then the
    /// earlier destinations hold the new files, the later ones the old, and each replaced file is
    /// left beside its destination under a <c>.bak</c> name rather than lost.
    /// </para>
    /// </remarks>
    public static void WriteAll(IReadOnlyList<(string? Flag, string Path, byte[] Content)> files, params string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(files);

        CheckAll([.. files.Select(f => (f.Flag, f.Path))], inputs);

        var targets = new string[files.Count];
        var temporary = new string?[files.Count];

        // What this call has put in place, and for each the backup of the file it replaced, if any.
        var placed = new List<(string Target, string? Backup)>(files.Count);
        var keepBackups = files.Count > 1;
        var at = 0;

        try
        {
            for (at = 0; at < files.Count; at++)
            {
                targets[at] = Destination(files[at].Flag, files[at].Path);
            }

            // Every file whole on disk first, beside where it is going...
            for (at = 0; at < files.Count; at++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targets[at])!);
                temporary[at] = $"{targets[at]}.{Guid.NewGuid():N}.tmp";
                File.WriteAllBytes(temporary[at]!, files[at].Content);
            }

            // ...and only then into place, each in one rename.
            for (at = 0; at < files.Count; at++)
            {
                var target = targets[at];
                string? backup = null;

                if (keepBackups && File.Exists(target))
                {
                    // Replaced in one step, the old file kept under a backup name until every
                    // other file is in place, so a later failure can put it back.
                    backup = $"{target}.{Guid.NewGuid():N}.bak";
                    File.Replace(temporary[at]!, target, backup, ignoreMetadataErrors: true);
                }
                else
                {
                    // One rename over whatever is there, or into an empty place.
                    File.Move(temporary[at]!, target, overwrite: true);
                }

                temporary[at] = null;
                placed.Add((target, backup));
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            // None of them, then: newest first, each back as it was.
            for (var i = placed.Count - 1; i >= 0; i--)
            {
                var (target, backup) = placed[i];

                if (backup is not null)
                {
                    TryRestore(backup, target);
                }
                else
                {
                    TryDelete(target);
                }
            }

            placed.Clear();

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

            // All in place: what was replaced is no longer wanted.
            foreach (var (_, backup) in placed)
            {
                if (backup is not null)
                {
                    TryDelete(backup);
                }
            }
        }
    }

    // Where a destination's bytes actually go: the file a symbolic link names, followed to the
    // end, or the path itself. An existing read-only file is refused here, before anything is
    // written.
    private static string Destination(string? flag, string path)
    {
        var full = Resolved(path);
        var info = new FileInfo(full);

        if (info.Exists && info.IsReadOnly)
        {
            throw Refusal(flag, full, "it is read-only", flag is null
                ? $"make {full} writable, or remove it"
                : $"make it writable, or give --{flag} another file");
        }

        return full;
    }

    private static void TryRestore(string backup, string path)
    {
        try
        {
            File.Move(backup, path, overwrite: true);
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

    // Located at the flag that named the file; where the user named none - a default destination,
    // a file in the project's scratch folder - at the root, with the file itself in the
    // constraint, since pointing at a flag the user did not give sends them to the wrong place.
    private static EinzelException Refusal(string? flag, string path, string why, string? suggestion) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = flag is null ? "/" : $"/args/--{flag}",
            Constraint = $"cannot write {path}: {why}",
            Suggestion = suggestion ?? (flag is null
                ? $"make {Path.GetDirectoryName(path)} a folder that can be written, or move what is in the way"
                : $"give --{flag} a file that can be written"),
        });
}
