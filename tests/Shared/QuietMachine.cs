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
/// that never came would hang a test run, so past the bound the turn is taken anyway and the
/// test runs as it would have without this.
/// </para>
/// </remarks>
internal static class QuietMachine
{
    /// <summary>The longest a test waits for its turn before taking it anyway.</summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromMinutes(20);

    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "einzel-tests-quiet-machine.lock");

    /// <summary>Waits for the machine-wide turn and holds it until disposed.</summary>
    /// <returns>What to dispose to give the turn back; nothing to dispose if it was never had.</returns>
    internal static IDisposable Hold()
    {
        var clock = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (clock.Elapsed < Patience)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return new Nothing();
            }
            catch (UnauthorizedAccessException)
            {
                // A temporary directory this process may not write to: run without a turn.
                return new Nothing();
            }
        }
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
