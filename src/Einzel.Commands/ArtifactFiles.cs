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
    public static void CheckAll(IReadOnlyList<(string? Flag, string Path)> destinations, params string?[] inputs) =>
        Checked(destinations, inputs);

    // The checks, returning each destination as resolved, so a caller that goes on to write them
    // follows each link once rather than again for every file.
    private static string[] Checked(IReadOnlyList<(string? Flag, string Path)> destinations, string?[] inputs)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        var resolved = new string[destinations.Count];

        // Each destination resolved once and looked up, not compared with every other: a film is
        // hundreds of frames, and every comparison follows links on disk.
        var seen = new Dictionary<string, int>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        for (var i = 0; i < destinations.Count; i++)
        {
            var (flag, path) = destinations[i];

            Check(flag, path);
            CheckNotAnInput(flag, path, inputs);

            resolved[i] = Resolved(path);
            var target = Path.TrimEndingDirectorySeparator(resolved[i]);

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

        return resolved;
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

        // Every destination refused before any is written, where all of them are known up front -
        // and checked once: each is staged as already resolved, not checked and followed again.
        //
        // So a link is followed when the set is checked, not when its file is staged, and a link
        // retargeted while a long film is being written is written through to where it pointed
        // then. Left so on purpose: catching two names for one file has to happen before any
        // frame is made, which needs every link resolved up front, and resolving again at staging
        // is the second resolution per file this was written to remove. The window is the user's
        // own file system changing under a command they are running - the same as retargeting the
        // link a moment after the command finished - and not a boundary anything relies on.
        var resolved = Checked(destinations, inputs ?? []);

        using var set = Begin(inputs ?? []);

        for (var i = 0; i < destinations.Count; i++)
        {
            var index = i;
            set.StageResolved(destinations[i].Flag, destinations[i].Path, resolved[i], stream => write(index, stream));
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

            string resolved;
            string key;

            try
            {
                Check(flag, path);
                CheckNotAnInput(flag, path, _inputs);
                resolved = Resolved(path);
                key = Path.TrimEndingDirectorySeparator(resolved);

                // A second file at one place: asked here, of a set built a file at a time.
                // WriteAll asks it once, of the whole set, in Checked.
                if (_seen.TryGetValue(key, out var j))
                {
                    throw Refusal(
                        flag,
                        Path.GetFullPath(path),
                        (_files[j].Flag is { } other ? $"--{other}" : "another output") + " names the same file, and one would be written over the other",
                        flag is null ? "name the output with its flag, a file of its own" : $"give --{flag} a file of its own");
                }
            }
            catch
            {
                Abort();
                throw;
            }

            StageResolved(flag, path, resolved, write);
            _seen[key] = _files.Count - 1;
        }

        // Stages a destination the caller has already checked and resolved - every check, a second
        // file at one place included (Stage, or Checked for WriteAll) - so only what checking cannot
        // know ahead, a read-only file, is asked here.
        internal void StageResolved(string? flag, string path, string full, Action<Stream> write)
        {
            ObjectDisposedException.ThrowIf(_finished, this);

            string? temporary = null;

            try
            {
                var target = Writable(flag, full);
                var folder = Path.GetDirectoryName(target)!;
                Create(folder, _created);
                temporary = ShortName(folder, ".tmp");

                Failing.Value?.Invoke("write", _files.Count);

                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                }

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

            // The file being replaced while File.Replace runs, and its backup: on Windows the
            // replace is two moves, and one that fails after moving the original to the backup name
            // (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2) leaves no file at the target and the user's file
            // under the backup name - not yet in placed, so the rollback never knew it was there.
            (string Target, string Backup)? replacing = null;

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
                        replacing = (target, backup);
                        Replacing.Value?.Invoke(target, backup);
                        File.Replace(temporary, target, backup, ignoreMetadataErrors: true);

                        // Placed the moment the replace returns, with its backup, before anything
                        // else can throw: registered after a hook, a failure there left the new
                        // bytes in place and the original stranded under its backup name.
                        replacing = null;
                        placed.Add((target, backup));
                        Failing.Value?.Invoke("replaced", at);
                    }
                    else
                    {
                        // One rename over whatever is there, or into an empty place.
                        File.Move(temporary, target, overwrite: true);
                        placed.Add((target, null));
                    }
                }
            }
            catch (Exception failure)
            {
                // What became of each file, said per file in the refusal: the one copy of a user's
                // file may be under a backup name, and advice that fits one state misleads in another.
                var report = new List<string>();
                var advice = new HashSet<string>(StringComparer.Ordinal);
                var keepStaging = false;

                // The file being replaced when it failed, and what the failure left of it.
                var same = Sameness.Different;

                if (replacing is { } failed && File.Exists(failed.Backup))
                {
                    same = File.Exists(failed.Target) ? Compare(failed.Backup, failed.Target) : Sameness.Different;
                }

                if (same == Sameness.Unknown)
                {
                    // Whether the target was replaced cannot be told - a file locked against reading
                    // - so nothing is put back, here or anywhere in the set: restoring the others
                    // while this one may hold the new bytes would leave a mixed set and call it
                    // rolled back. Every backup is kept, and every file's state is said.
                    var (unclear, copy) = replacing!.Value;
                    report.Add($"it could not be told whether {unclear} was replaced: a copy of its original is at {copy}");
                    advice.Add("compare a file with the copy named beside it and keep the one you want");

                    foreach (var (target, backup) in placed)
                    {
                        report.Add(backup is null
                            ? $"{target} was written and was not there before"
                            : $"{target} holds the new file and its original is at {backup}");

                        if (backup is not null)
                        {
                            advice.Add("move an original back over the file named beside it to undo it");
                        }
                    }

                    // The new bytes of the uncertain file and of every file not yet placed are the
                    // only copies of what the command made: kept beside their destinations under
                    // names that say what they are, so the set can be completed by hand.
                    for (var i = at; i < count; i++)
                    {
                        var (_, _, target, temporary) = _files[i];
                        var kept = KeepAside(temporary, target, out var keepFailure);

                        if (keepFailure is not null)
                        {
                            // Said, not raised: an exception here would leave this catch and take
                            // the report of every file with it.
                            report.Add(kept is null
                                ? $"could not keep the new version of {target}: {keepFailure}"
                                : $"could not keep the new version of {target}: {keepFailure}; it is still at {kept}");
                        }

                        if (i > at)
                        {
                            report.Add(kept is null ? $"{target} is unchanged" : $"{target} is unchanged and its new version is at {kept}");
                        }
                        else if (kept is not null)
                        {
                            report.Add($"the new version of {target} is at {kept}");
                        }

                        if (kept is not null && kept == temporary)
                        {
                            // Left at its staging name, which is named after nothing: the advice
                            // must not send anyone looking for a file named after the target.
                            advice.Add("move each new version from where the refusal says it is over the file it is the new version of, to complete the set");
                        }
                        else if (kept is not null)
                        {
                            advice.Add("move a new version over the file it is named after to complete the set");
                        }
                    }

                    keepStaging = true;

                    // Kept: the backups are the originals now, and the finally below must not delete them.
                    placed.Clear();
                }
                else
                {
                    if (replacing is { } risk && File.Exists(risk.Backup))
                    {
                        if (same == Sameness.Same)
                        {
                            // The replace failed with both still there and the target as it was:
                            // the backup is a copy of it, and nothing needs it.
                            TryDelete(risk.Backup);
                        }
                        else if (!TryRestore(risk.Backup, risk.Target))
                        {
                            // The target is gone, or holds something else - most likely the new
                            // bytes, with the original under the backup name. Put back as a placed
                            // file is; where it cannot be, the refusal names both.
                            report.Add($"the original of {risk.Target} could not be put back and is at {risk.Backup}");
                            advice.Add("move an original back over the file named beside it");
                        }
                    }

                    // None of them, then, whatever went wrong: newest first, each back as it was.
                    for (var i = placed.Count - 1; i >= 0; i--)
                    {
                        var (target, backup) = placed[i];

                        if (backup is not null)
                        {
                            if (!TryRestore(backup, target) && File.Exists(backup))
                            {
                                report.Add($"the original of {target} could not be put back and is at {backup}");
                                advice.Add("move an original back over the file named beside it");
                            }
                        }
                        else
                        {
                            TryDelete(target);
                        }
                    }

                    placed.Clear();
                }

                var (flag, path, _, _) = _files[Math.Min(at, count - 1)];

                // The staging files not yet renamed, and the folders this set made - except where they
                // were kept aside as the only copies of the new files.
                for (var i = at; i < count && !keepStaging; i++)
                {
                    TryDelete(_files[i].Temporary);
                }

                _files.Clear();
                RemoveCreatedFolders();
                _finished = true;

                if (report.Count == 0
                    && failure is not (IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException))
                {
                    // Not a destination that cannot be written: rolled back, and raised as itself.
                    throw;
                }

                throw Refusal(
                    flag,
                    Path.GetFullPath(path),
                    failure.Message + (report.Count == 0 ? string.Empty : "; and it could not be rolled back as it was: " + string.Join("; ", report)),
                    advice.Count == 0 ? null : string.Join("; ", advice) + "; nothing of any original was deleted");
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

    /// <summary>
    /// Called with a file's destination and its backup name just before File.Replace: a test's way
    /// to leave what a failed replace can leave - the target and a backup both - and fail there.
    /// </summary>
    internal static readonly AsyncLocal<Action<string, string>?> Replacing = new();

    // A destination already resolved to where its bytes go - the file a symbolic link names,
    // followed to the end, or the path itself - refused if it is an existing read-only file,
    // before anything is written.
    private static string Writable(string? flag, string full)
    {
        var info = new FileInfo(full);

        if (info.Exists && info.IsReadOnly)
        {
            throw Refusal(flag, full, "it is read-only", flag is null
                ? $"make {full} writable, or remove it"
                : $"make it writable, or give --{flag} another file");
        }

        return full;
    }

    /// <summary>
    /// Called with a staging file and the name it is about to be kept under: a test's way to make
    /// keeping a new version aside fail for any reason, not only the file system's.
    /// </summary>
    internal static readonly AsyncLocal<Action<string, string>?> Keeping = new();

    // A staging file moved beside its destination as "<name>.new" (or .new2, ...), where it is
    // the only copy of a file the command made: where it is now, or null where there was nothing
    // to keep. Never throws - it is called while a failure's report is being written, and an
    // exception would lose that report - so what went wrong is handed back in failure.
    private static string? KeepAside(string temporary, string target, out string? failure)
    {
        failure = null;

        if (!File.Exists(temporary))
        {
            return null;
        }

        for (var n = 1; n < 1000; n++)
        {
            var kept = target + (n == 1 ? ".new" : ".new" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (File.Exists(kept) || Directory.Exists(kept))
            {
                continue;
            }

#pragma warning disable CA1031 // Any failure is reported in the refusal, never raised over it.
            try
            {
                Keeping.Value?.Invoke(temporary, kept);
                File.Move(temporary, kept);
                return kept;
            }
            catch (Exception moving)
            {
                if (moving is IOException && File.Exists(temporary) && (File.Exists(kept) || Directory.Exists(kept)))
                {
                    // The name was taken between the look and the move: try the next one.
                    continue;
                }

                failure = moving.Message;
                return File.Exists(temporary) ? temporary : null;
            }
#pragma warning restore CA1031
        }

        failure = "every name from .new to .new999 is taken";
        return temporary;
    }

    // What a comparison of two files found: the same bytes, different bytes, or nothing either way.
    private enum Sameness
    {
        Same,
        Different,
        Unknown,
    }

    // In fixed-size chunks through two streams, so a film's frame is not read whole twice. A file
    // that cannot be read is Unknown - never "different", which would send the backup over a
    // target that may be the untouched original - and nothing escapes the rollback it is part of.
    private static Sameness Compare(string first, string second)
    {
#pragma warning disable CA1031 // Inside a rollback: whatever goes wrong, the answer is "not known".
        try
        {
            Comparing.Value?.Invoke(first, second);

            using var a = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var b = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (a.Length != b.Length)
            {
                return Sameness.Different;
            }

            var x = new byte[81920];
            var y = new byte[81920];

            while (true)
            {
                var read = a.ReadAtLeast(x, x.Length, throwOnEndOfStream: false);

                if (read == 0)
                {
                    return Sameness.Same;
                }

                if (b.ReadAtLeast(y, read, throwOnEndOfStream: false) != read)
                {
                    return Sameness.Unknown;
                }

                if (!x.AsSpan(0, read).SequenceEqual(y.AsSpan(0, read)))
                {
                    return Sameness.Different;
                }
            }
        }
        catch (Exception)
        {
            return Sameness.Unknown;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Called with each pair of files a rollback compares: a test's way to make the comparison
    /// fail, as a file locked against reading does.
    /// </summary>
    internal static readonly AsyncLocal<Action<string, string>?> Comparing = new();

    // Whether the backup went back; false where it did not, and the caller says where it is.
    private static bool TryRestore(string backup, string path)
    {
        try
        {
            File.Move(backup, path, overwrite: true);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Left beside where it was, under its .bak name, rather than lost.
            return false;
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
