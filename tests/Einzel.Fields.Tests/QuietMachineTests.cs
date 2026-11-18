using Einzel.Testing;

namespace Einzel.Fields.Tests;

/// <summary>The machine-wide turn the measuring children and the timed tests take.</summary>
/// <remarks>
/// Each test takes a lock file of its own rather than the machine-wide one: a test of the turn
/// that took the real one would wait on every measurement running in the other test processes,
/// and one that failed holding it would keep them all waiting - which is what happened once, and
/// put every memory test after it twenty minutes behind and then out of its turn.
/// </remarks>
public sealed class QuietMachineTests : IDisposable
{
    private readonly string _lock = Path.Combine(Path.GetTempPath(), $"einzel-quiet-test-{Guid.NewGuid():N}.lock");

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            File.Delete(_lock);
        }
        catch (IOException)
        {
            // Still held by a turn a failed assertion left behind; the directory is a temporary one.
        }
    }

    /// <summary>A turn held keeps another out until it is given back, and is given back by disposing it.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task ATurnHeldKeepsAnotherOutUntilItIsGivenBack()
    {
        Task<QuietMachine.Turn> second;

        using (QuietMachine.Hold(lockPath: _lock))
        {
            second = Task.Run(() => QuietMachine.Hold(lockPath: _lock));

            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500))));
        }

        try
        {
            // Given back, so the second, waiting on another thread, now has it.
            Assert.Same(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMinutes(1))));
            Assert.True((await second).Quiet);
        }
        finally
        {
            // Whatever the assertions said, a turn the second took is given back.
            _ = second.ContinueWith(taken => taken.Result.Dispose(), TaskScheduler.Default);
        }
    }

    /// <summary>
    /// A turn that never came says so, and a measurement asked for under it measures nothing and
    /// fails naming why, rather than reading a busy machine as a quiet one.
    /// </summary>
    [Fact]
    public void ATurnThatNeverCameIsNotQuietAndMeasuresNothing()
    {
        using var held = QuietMachine.Hold(lockPath: _lock);

        Assert.True(held.Quiet);

        using (var waited = QuietMachine.Hold(TimeSpan.FromMilliseconds(300), _lock))
        {
            Assert.False(waited.Quiet);
            Assert.Contains("the machine was not quiet", waited.NotQuietBecause, StringComparison.Ordinal);
        }

        var run = MeasuringChild.Measure(
            MemoryCases.Host, JitTier.Optimized, ["plain=16"], patience: TimeSpan.FromMilliseconds(300), turnPath: _lock);

        Assert.NotNull(run.Failure);
        Assert.Contains("the machine was not quiet", run.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Cases);
    }
}
