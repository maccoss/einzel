using System.Reflection;

namespace Einzel.Fields.Tests;

/// <summary>Every field that knows where its conductors are also says what they hold.</summary>
public sealed class HeldConductorsTests
{
    /// <summary>
    /// Every type in the fields assembly that is <see cref="IConductorBounded"/> is also
    /// <see cref="IHeldConductors"/>.
    /// </summary>
    /// <remarks>
    /// A wrapper that forwards where its conductors are and not what they hold makes every
    /// conductor behind it a conductor with no potential: a surface drawn through it has no
    /// plateaus at all, and nothing fails. The next wrapper is caught here rather than by a
    /// picture someone notices.
    /// </remarks>
    [Fact]
    public void EveryConductorBoundedFieldAlsoSaysWhatItsConductorsHold()
    {
        var bounded = typeof(IConductorBounded).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IConductorBounded).IsAssignableFrom(t))
            .ToList();

        Assert.True(bounded.Count >= 10, $"the control: the scan found {bounded.Count} conductor-bounded fields");
        Assert.Empty(bounded.Where(t => !typeof(IHeldConductors).IsAssignableFrom(t)).Select(t => t.FullName));
    }
}
