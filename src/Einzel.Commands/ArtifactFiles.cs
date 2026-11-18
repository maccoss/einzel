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
/// <b>All of them or none</b> (<c>WriteAll</c>): each file is written whole to a
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

        WriteAll([.. files.Select(f => (f.Flag, f.Path))], (index, stream) => stream.Write(files[index].Content), inputs);
    }

    /// <summary>
    /// Writes several files, each streamed by the caller as it comes: every one of them, or none,
    /// as <see cref="WriteAll(IReadOnlyList{ValueTuple{string, string, byte[]}}, string[])"/>,
    /// holding one file in memory at a time.
    /// </summary>
    /// <param name="destinations">Each file's flag (null where the user named none) and destination.</param>
    /// <param name="write">Writes file <c>index</c> to the stream it is handed.</param>
    /// <param name="inputs">The files the command reads, none of which a destination may be.</param>
    /// <exception cref="EinzelException">One could not be written, or a destination is refused.</exception>
    /// <remarks>
    /// For a film of hundreds of frames, which buffered whole came to the film's size in memory:
    /// each frame is now made and written to its staging file in turn. The staging files sit beside
    /// their destinations under short names, so a rename puts each in place and a path near the
    /// platform's limit is not pushed past it by a long suffix. What is on disk at the commit is
    /// the new set and the old one, the old kept by renaming rather than copying - the least an
    /// all-or-none replacement can hold.
    /// </remarks>
    public static void WriteAll(
        IReadOnlyList<(string? Flag, string Path)> destinations, Action<int, Stream> write, params string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(write);

        // Every destination refused before any is written, where all of them are known up front.
        CheckAll(destinations, inputs);

        using var set = Begin(inputs);

        for (var i = 0; i < destinations.Count; i++)
        {
            var index = i;
            set.Stage(destinations[i].Flag, destinations[i].Path, stream => write(index, stream));
        }

        set.Commit();
    }

    /// <summary>
    /// Starts a set of files to be written all or none, staged one at a time as the caller makes
    /// them and put in place together by <see cref="Staged.Commit"/>.
    /// </summary>
    /// <param name="inputs">The files the command reads, none of which a destination may be.</param>
    /// <returns>The set; disposing it uncommitted removes everything it staged.</returns>
    /// <remarks>
    /// For a caller that cannot know every destination, or cannot make every file, before the
    /// first is written - an export solving one element at a time. Each file goes to disk as soon
    /// as it is made, so what the caller holds in memory is one file's worth however many there
    /// are, and what is on disk until the commit is staging files no reader looks at.
    /// </remarks>
    public static Staged Begin(params string?[] inputs) => new(inputs);

    /// <summary>
    /// Files staged beside their destinations, put in place together or not at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Each file is replaced in one step.</b> Its bytes are written whole to a temporary file
    /// beside it, and the temporary is renamed over the destination. A process killed at any
    /// instant leaves the old file or the new one at the destination, never neither.
    /// </para>
    /// <para>
    /// <b>Across several files the guarantee is weaker, and this is what it is.</b> Every file is
    /// first written whole beside its destination; at the commit they are put in place one after
    /// another, each replaced file kept under a backup name until the last is in place. If one
    /// fails, those already placed are put back from their backups (or removed, where there was
    /// nothing before) and the commit refuses. What cannot be covered is the process dying
    /// between the first rename and the last: then the earlier destinations hold the new files,
    /// the later ones the old, and each replaced file is left beside its destination under a
    /// <c>.bak</c> name rather than lost.
    /// </para>
    /// </remarks>
    public sealed class Staged : IDisposable
    {
        private readonly string?[] _inputs;

        // Each staged file: its flag, the path as given, where its bytes go, and its staging file.
        private readonly List<(string? Flag, string Path, string Target, string Temporary)> _files = [];

        // Each destination resolved once and looked up, not compared with every other: a film is
        // hundreds of frames, and every comparison follows links on disk.
        private readonly Dictionary<string, int> _seen = new(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        // The folders this set made, so a failure leaves none of them behind.
        private readonly List<string> _created = [];

        private bool _finished;

        internal Staged(string?[] inputs) => _inputs = inputs ?? [];

        /// <summary>How many files are staged.</summary>
        public int Count => _files.Count;

        /// <summary>Checks a destination and writes its file whole to a staging file beside it.</summary>
        /// <param name="flag">The flag that names it, without its dashes; null where the user named none.</param>
        /// <param name="path">The destination.</param>
        /// <param name="write">Writes the file to the stream it is handed.</param>
        /// <exception cref="EinzelException">
        /// The destination is refused, or its file could not be staged. Everything this set has
        /// staged is removed first: a refused set is no set.
        /// </exception>
        public void Stage(string? flag, string path, Action<Stream> write)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(write);
            ObjectDisposedException.ThrowIf(_finished, this);

            string? temporary = null;

            try
            {
                Check(flag, path);
                CheckNotAnInput(flag, path, _inputs);

                var resolved = Path.TrimEndingDirectorySeparator(Resolved(path));

                if (_seen.TryGetValue(resolved, out var j))
                {
                    throw Refusal(
                        flag,
                        Path.GetFullPath(path),
                        (_files[j].Flag is { } other ? $"--{other}" : "another output") + " names the same file, and one would be written over the other",
                        flag is null ? "name the output with its flag, a file of its own" : $"give --{flag} a file of its own");
                }

                var target = Destination(flag, path);
                var folder = Path.GetDirectoryName(target)!;
                Create(folder, _created);
                temporary = ShortName(folder, ".tmp");

                Failing.Value?.Invoke("write", _files.Count);

                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                }

                _seen[resolved] = _files.Count;
                _files.Add((flag, path, target, temporary));
                temporary = null;
            }
            catch (Exception failure)
            {
                if (temporary is not null)
                {
                    TryDelete(temporary);
                }

                Abort();

                if (failure is not (IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException))
                {
                    // A refusal already, or not a destination that cannot be written: raised as itself.
                    throw;
                }

                throw Refusal(flag, Path.GetFullPath(path), failure.Message, null);
            }
        }

        /// <summary>Puts every staged file in place: all of them, or none.</summary>
        /// <exception cref="EinzelException">
        /// One could not be put in place. Those already placed are put back as they were, and
        /// every staging file is removed.
        /// </exception>
        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_finished, this);

            var count = _files.Count;
            var placed = new List<(string Target, string? Backup)>(count);
            var keepBackups = count > 1;
            var at = 0;

            try
            {
                for (at = 0; at < count; at++)
                {
                    Failing.Value?.Invoke("place", at);

                    var (_, _, target, temporary) = _files[at];
                    string? backup = null;

                    if (keepBackups && File.Exists(target))
                    {
                        // Replaced in one step, the old file kept under a backup name until every
                        // other file is in place, so a later failure can put it back.
                        backup = ShortName(Path.GetDirectoryName(target)!, ".bak");
                        File.Replace(temporary, target, backup, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        // One rename over whatever is there, or into an empty place.
                        File.Move(temporary, target, overwrite: true);
                    }

                    placed.Add((target, backup));
                }
            }
            catch (Exception failure)
            {
                // None of them, then, whatever went wrong: newest first, each back as it was.
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

                var (flag, path, _, _) = _files[Math.Min(at, count - 1)];

                // The staging files not yet renamed, and the folders this set made.
                for (var i = at; i < count; i++)
                {
                    TryDelete(_files[i].Temporary);
                }

                _files.Clear();
                RemoveCreatedFolders();
                _finished = true;

                if (failure is not (IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException))
                {
                    // Not a destination that cannot be written: rolled back, and raised as itself.
                    throw;
                }

                throw Refusal(flag, Path.GetFullPath(path), failure.Message, null);
            }
            finally
            {
                // All in place: what was replaced is no longer wanted.
                foreach (var (_, backup) in placed)
                {
                    if (backup is not null)
                    {
                        TryDelete(backup);
                    }
                }
            }

            _files.Clear();
            _finished = true;
        }

        /// <summary>Removes everything staged and not committed.</summary>
        public void Dispose() => Abort();

        private void Abort()
        {
            if (_finished)
            {
                return;
            }

            foreach (var file in _files)
            {
                TryDelete(file.Temporary);
            }

            _files.Clear();
            RemoveCreatedFolders();
            _finished = true;
        }

        // Deepest first, where nothing else has been put in them.
        private void RemoveCreatedFolders()
        {
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                TryDeleteEmptyFolder(_created[i]);
            }

            _created.Clear();
        }
    }

    // Makes a folder and every missing one above it, noting each it made, outermost first.
    private static void Create(string folder, List<string> created)
    {
        var missing = new Stack<string>();

        for (var at = Path.GetFullPath(folder); !string.IsNullOrEmpty(at) && !Directory.Exists(at); at = Path.GetDirectoryName(at))
        {
            missing.Push(at);
        }

        foreach (var dir in missing)
        {
            Directory.CreateDirectory(dir);
            created.Add(dir);
        }
    }

    // A short name in a folder for a staging or backup file: a dot, eight hex digits and the
    // suffix, so the path is the folder's length and fourteen characters, whatever the
    // destination's name - a long name with a GUID appended could pass the platform's limit.
    private static string ShortName(string folder, string suffix)
    {
        while (true)
        {
            var name = Path.Combine(folder, "." + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() + suffix);

            if (!File.Exists(name) && !Directory.Exists(name))
            {
                return name;
            }
        }
    }

    private static void TryDeleteEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Left, empty or not; the refusal is still raised.
        }
    }

    /// <summary>
    /// Called before each file is written ("write") and before each is put in place ("place"),
    /// with its index: a test's way to fail a set part way through for a reason that is not the
    /// file system's. Per async flow, so tests running beside each other do not see each other's.
    /// </summary>
    internal static readonly AsyncLocal<Action<string, int>?> Failing = new();

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
            Path = flag is null ? "/" : FlagPath.Of(flag),
            Constraint = $"cannot write {path}: {why}",
            Suggestion = suggestion ?? (flag is null
                ? $"make {Path.GetDirectoryName(path)} a folder that can be written, or move what is in the way"
                : $"give --{flag} a file that can be written"),
        });
}
