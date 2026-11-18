using Einzel.Testing;

namespace Einzel.Fields.Tests;

/// <summary>The machine-wide turn the measuring children and the timed tests take.</summary>
public sealed class QuietMachineTests
{
    /// <summary>A turn held keeps another out until it is given back, and is given back by disposing it.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task ATurnHeldKeepsAnotherOutUntilItIsGivenBack()
    {
        Task<IDisposable> second;

        using (QuietMachine.Hold())
        {
            second = Task.Run(QuietMachine.Hold);

            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500))));
        }

        // Given back, so the second, waiting on another thread, now has it.
        Assert.Same(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMinutes(1))));

        (await second).Dispose();
    }
}
