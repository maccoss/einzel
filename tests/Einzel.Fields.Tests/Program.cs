namespace Einzel.Fields.Tests;

/// <summary>
/// The test assembly's entry point: the child process the memory tests measure builds in.
/// </summary>
/// <remarks>
/// The test host loads this assembly as a library and never calls this. <see cref="MemoryCases"/>
/// starts it as a process of its own, with the JIT's tiering pinned, so that the live set it
/// reads is a build's and nothing else's.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is [MemoryCases.Command, var tier, .. var cases] && JitTier.Named(tier) is { } wanted && cases.Length > 0)
        {
            return MemoryCases.Child(wanted, cases);
        }

        Console.Error.WriteLine(
            $"usage: {MemoryCases.Command} {JitTier.Optimized.Name}|{JitTier.First.Name} <kind>=<size> [<kind>=<size> ...]"
            + Environment.NewLine
            + $"got: {(args.Length == 0 ? "no arguments" : string.Join(' ', args))}");

        return 64;
    }
}
