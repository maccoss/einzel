using System.Diagnostics;

namespace Einzel.Testing;

/// <summary>
/// A machine-wide turn for the tests that load the machine or time it: the memory tests'
/// measuring children, and the tests whose assertion is a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The test assemblies run in parallel, each its own process, on a CI
/// runner's two to four cores. A measuring child is a whole process, fully optimised and forcing
/// full collections, for seconds to minutes; while one runs, a test that times an extension's
/// timeout or compares two of its own timings is measuring the child as much as the code. A
/// collection cannot keep them apart, because they are in different assemblies - so they take
/// turns through a lock file in the temporary directory, which every test process on the machine
/// sees.
/// </para>
/// <para>
/// <b>A file, not a named mutex</b>: a mutex must be released by the thread that took it, and a
/// lock file is released by whoever disposes it, on any thread. Holding is exclusive: one memory
/// measurement or one timed test at a time, machine-wide.
/// </para>
/// <para>
/// <b>Waiting is bounded</b>, at <see cref="Patience"/>: a holder that dies leaves the file
/// closed by the operating system, and one that hangs is stopped by its own budget, but a turn
/// that never came would hang a test run. Past the bound the caller gets a turn that says it was
/// not had - <see cref="Turn.Quiet"/> false, and why - and must not let a reading taken that way
/// pass as one taken on a quiet machine: it used to come back as a silent no-op, and the reading
/// went on as though it had the machine to itself.
/// </para>
/// </remarks>
internal static class QuietMachine
{
    /// <summary>The longest a test waits for its turn before taking it anyway.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromMinutes(20);

    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "einzel-tests-quiet-machine.lock");

    /// <summary>Waits for the machine-wide turn and holds it until disposed.</summary>
    /// <param name="patience">How long to wait; <see cref="Patience"/> if null.</param>
    /// <param name="lockPath">
    /// The lock file; the machine-wide one if null. A test of the turn itself takes its own, so
    /// it neither waits on nor blocks the measurements the machine-wide one exists to keep apart.
    /// </param>
    /// <returns>
    /// The turn, to dispose to give it back - or, where it could not be had, a turn that says so,
    /// which the caller must not read a measurement under as though the machine were quiet.
    /// </returns>
    internal static Turn Hold(TimeSpan? patience = null, string? lockPath = null)
    {
        var allowed = patience ?? Patience;
        var path = lockPath ?? LockPath;
        var clock = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new Turn(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None), null);
            }
            catch (IOException) when (clock.Elapsed < allowed)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return new Turn(
                    null,
                    $"the machine was not quiet: another test held the machine-wide turn ({path}) "
                    + $"for all of the {allowed.TotalMinutes:G3} minutes this one waited");
            }
            catch (UnauthorizedAccessException)
            {
                return new Turn(
                    null,
                    $"the machine was not quiet: the machine-wide turn ({path}) could not be taken, "
                    + "because this process may not write to the temporary directory");
            }
        }
    }

    /// <summary>A machine-wide turn, held or not.</summary>
    internal sealed class Turn : IDisposable
    {
        private readonly FileStream? _held;

        /// <summary>Takes a turn, held through a lock file or not had at all.</summary>
        /// <param name="held">The lock file, or null where the turn was not had.</param>
        /// <param name="notQuietBecause">Why it was not had, or null where it was.</param>
        internal Turn(FileStream? held, string? notQuietBecause)
        {
            _held = held;
            NotQuietBecause = notQuietBecause;
        }

        /// <summary>Whether the turn was had: nothing else measuring or timing ran meanwhile.</summary>
        internal bool Quiet => _held is not null;

        /// <summary>Why the turn was not had, or null where it was.</summary>
        internal string? NotQuietBecause { get; }

        /// <inheritdoc/>
        public void Dispose() => _held?.Dispose();
    }
}
