namespace Einzel.Cli;

/// <summary>
/// A minimal argument parser: positional arguments, and <c>--name</c> flags with
/// optional values.
/// </summary>
/// <remarks>
/// Hand-rolled because the surface is small and PERF-8 budgets 500 ms from cold
/// start to first output. It accepts <c>--name value</c> and <c>--name=value</c>,
/// and treats a bare <c>--name</c> as a flag.
/// </remarks>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);
    private readonly List<string> _positional = [];

    private CommandLine()
    {
    }

    /// <summary>Positional arguments, excluding the verb.</summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>Whether a flag was supplied.</summary>
    /// <param name="name">The flag name, without dashes.</param>
    /// <returns><see langword="true"/> when present.</returns>
    public bool Has(string name) => _options.ContainsKey(name);

    /// <summary>The value of an option, or null when absent or valueless.</summary>
    /// <param name="name">The option name, without dashes.</param>
    /// <returns>The value, or null.</returns>
    public string? Value(string name) => _options.TryGetValue(name, out var value) ? value : null;

    /// <summary>Parses arguments, skipping the leading verb.</summary>
    /// <param name="args">The raw arguments.</param>
    /// <returns>The parsed command line.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is null.</exception>
    public static CommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new CommandLine();

        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                parsed._positional.Add(argument);
                continue;
            }

            var name = argument[2..];
            var separator = name.IndexOf('=', StringComparison.Ordinal);

            if (separator >= 0)
            {
                parsed._options[name[..separator]] = name[(separator + 1)..];
                continue;
            }

            // A following argument that is not itself an option is this option's
            // value. Flags such as --json simply have none.
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._options[name] = args[++i];
            }
            else
            {
                parsed._options[name] = null;
            }
        }

        return parsed;
    }
}
