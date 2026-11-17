using Einzel.Testing;

namespace Einzel.Fields.Tests;

/// <summary>
/// The test assembly's entry point: the child process the memory tests measure builds in.
/// </summary>
/// <remarks>
/// The test host loads this assembly as a library and never calls this. <see cref="MeasuringChild"/>
/// starts it as a process of its own, with the JIT's tiering pinned, so that the live set it
/// reads is a build's and nothing else's.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args) => MeasuringChild.Enter(MemoryCases.Host, args);
}
