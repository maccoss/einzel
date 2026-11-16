using System.Diagnostics;

using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>Which tier of JIT code a measuring child runs as, set by its parent and checked by it.</summary>
/// <remarks>
/// <para>
/// What a solve holds depends on the tier: first-tier code keeps a local alive to the end of its
/// method and optimized code to its last use. Which a method runs as is otherwise the machine's
/// choice - how often it has been called, how busy the background compiler is - so each child is
/// pinned to one end, and <b>checks which end it is at by what the code does</b> rather than by
/// what the environment says: it drops the last use of an object and sees whether a collection
/// takes it. A shell that exports <c>DOTNET_TieredCompilation=0</c> used to turn the first-tier
/// child into an optimized one that passed for the wrong reason.
/// </para>
/// <para>
/// A Debug build's code keeps its locals in every tier, so there the optimized child cannot be
/// optimized and says so instead of refusing; the first-tier child's premise holds either way.
/// </para>
/// </remarks>
internal sealed class JitTier
{
    private static readonly string[] Variables =
        ["TieredCompilation", "TC_QuickJit", "TC_QuickJitForLoops", "TC_CallCounting", "TieredPGO", "TC_OnStackReplacement", "ReadyToRun"];

    private readonly bool _optimized;

    private JitTier(bool optimized) => _optimized = optimized;

    /// <summary>Everything compiled fully optimized at its first call.</summary>
    internal static JitTier Optimized { get; } = new(true);

    /// <summary>Everything compiled quickly and never promoted.</summary>
    internal static JitTier First { get; } = new(false);

    /// <summary>The name a child is told its tier by.</summary>
    internal string Name => _optimized ? "optimized" : "first";

    /// <summary>The tier a name stands for, or null.</summary>
    internal static JitTier? Named(string name) =>
        name == Optimized.Name ? Optimized : name == First.Name ? First : null;

    /// <summary>The variables to set, and with a null value to remove, for a child of this tier.</summary>
    internal IEnumerable<(string Name, string? Value)> Environment()
    {
        foreach (var variable in Variables)
        {
            yield return ($"COMPlus_{variable}", null);
            yield return ($"DOTNET_{variable}", null);
        }

        if (_optimized)
        {
            yield return ("DOTNET_TieredCompilation", "0");
        }
        else
        {
            // Nothing is counted, so nothing is promoted: every method stays first-tier.
            yield return ("DOTNET_TieredCompilation", "1");
            yield return ("DOTNET_TC_QuickJit", "1");
            yield return ("DOTNET_TC_QuickJitForLoops", "1");
            yield return ("DOTNET_TC_OnStackReplacement", "0");
            yield return ("DOTNET_TC_CallCounting", "0");
            yield return ("DOTNET_TieredPGO", "0");
        }
    }

    /// <summary>
    /// Why this process is not running as the tier asked for, or null if it is.
    /// </summary>
    internal static string? Refusal(JitTier wanted)
    {
        var keeps = ALocalOutlivesItsLastUse();
        var unoptimizedBuild = !IsOptimizedBuild(typeof(PoissonSolver3D)) || !IsOptimizedBuild(typeof(JitTier));

        if (wanted._optimized)
        {
            // A Debug build is unoptimized in every tier, and is measured as what it is.
            return keeps && !unoptimizedBuild
                ? "this process was to run fully optimized code and a local outlives its last use: "
                    + "tiering is on, so what a build holds would depend on what has been promoted"
                : null;
        }

        if (!keeps)
        {
            return "this process was to run first-tier code and a local dies at its last use: the code is optimized "
                + "(DOTNET_TieredCompilation=0, or a tiering variable set outside), so a first-tier hold cannot show";
        }

        // First-tier now is not first-tier after a warm-up build unless nothing is ever promoted,
        // and that the code cannot show: it is the one thing taken from the environment.
        return System.Environment.GetEnvironmentVariable("DOTNET_TC_CallCounting") == "0"
            ? null
            : "this process was to stay first-tier and DOTNET_TC_CallCounting is not 0, so methods are promoted as they are called";
    }

    private static bool IsOptimizedBuild(Type type) =>
        type.Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false) is not [DebuggableAttribute debuggable, ..]
        || !debuggable.IsJITOptimizerDisabled;

    /// <summary>Whether, in the code this process is running, an object survives its local's last use.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool ALocalOutlivesItsLastUse()
    {
        var held = new object();
        var weak = new WeakReference(held);

        Use(held);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        return weak.IsAlive;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Use(object value) => GC.KeepAlive(value);
}
