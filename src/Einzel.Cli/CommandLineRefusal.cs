using Einzel.Core.Errors;

namespace Einzel.Cli;

/// <summary>A command line refused before its verb ran, as <c>--json</c> reports it.</summary>
public sealed record CommandLineRefusal
{
    /// <summary>Always false: nothing ran.</summary>
    public bool Ran { get; init; }

    /// <summary>What was wrong with the command line, each with the flag or argument it names.</summary>
    public required IReadOnlyList<EinzelError> Errors { get; init; }
}
