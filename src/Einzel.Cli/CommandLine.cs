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
    private readonly List<(string Name, string? Value)> _all = [];
    private readonly List<string> _positional = [];

    private CommandLine()
    {
    }

    /// <summary>
    /// The flags that never take a value, so the argument after one is never taken as its value.
    /// </summary>
    /// <remarks>
    /// The parser cannot tell a switch from an option by looking: "a following argument that is
    /// not itself an option is this option's value". So <c>render surface --cycle-mean
    /// models/q.json</c> read the model as the switch's value and printed the usage, and
    /// <c>--no-scale out.png</c> dropped the file without a word. Every switch any verb reads is
    /// named here; <c>SwitchesAreDeclaredTests.EverySwitchAVerbReadsIsDeclared</c> reads the verbs' source and fails when one is
    /// read with <c>Has</c> alone and is missing from this list.
    /// </remarks>
    public static readonly IReadOnlySet<string> Switches = new HashSet<string>(StringComparer.Ordinal)
    {
        "composite",
        "cycle-mean",
        "dry-run",
        "help",
        "json",
        "mesh",
        "no-calibrate",
        "no-scale",
        "no-trajectory",
        "see-through",
        "study",
        "vtu",
    };

    /// <summary>Positional arguments, excluding the verb.</summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>Every flag given, without its dashes, in the order first given.</summary>
    public IReadOnlyList<string> Flags => [.. _all.Select(o => o.Name).Distinct()];

    /// <summary>Whether a flag was supplied.</summary>
    /// <param name="name">The flag name, without dashes.</param>
    /// <returns><see langword="true"/> when present.</returns>
    public bool Has(string name) => _options.ContainsKey(name);

    /// <summary>The value of an option, or null when absent or valueless.</summary>
    /// <param name="name">The option name, without dashes.</param>
    /// <returns>The value, or null.</returns>
    public string? Value(string name) => _options.TryGetValue(name, out var value) ? value : null;

    /// <summary>Every occurrence of a repeatable option, in the order given.</summary>
    /// <param name="name">The option name, without the leading dashes.</param>
    /// <returns>The values, which may be empty.</returns>
    /// <remarks>
    /// <see cref="Value"/> keeps the last occurrence, which is the usual convention and is
    /// right for an option that means one thing. It is wrong for one that means "and also":
    /// `--set a=1 --set b=2` under that rule applies b and drops a, with exit 0 and nothing
    /// said, which is how an agent that batched two edits lost one. An option that may be
    /// repeated must read its occurrences through this.
    /// </remarks>
    public IReadOnlyList<string?> Values(string name) =>
        [.. _all.Where(o => string.Equals(o.Name, name, StringComparison.Ordinal)).Select(o => o.Value)];

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
                parsed._all.Add((name[..separator], name[(separator + 1)..]));
                continue;
            }

            // A following argument that is not itself an option is this option's
            // value - unless this is a switch, which never has one.
            if (!Switches.Contains(name) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._options[name] = args[++i];
                parsed._all.Add((name, args[i]));
            }
            else
            {
                parsed._options[name] = null;
                parsed._all.Add((name, null));
            }
        }

        return parsed;
    }
}
