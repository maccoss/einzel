using Einzel.Core.Errors;

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

    /// <summary>Positional arguments, excluding the verb.</summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>Every flag given, without its dashes, in the order first given.</summary>
    public IReadOnlyList<string> Flags => [.. _all.Select(o => o.Name).Distinct()];

    /// <summary>
    /// Where a flag whose value may be left off is followed by more words than the verb takes,
    /// the refusal saying so; null where the line can be read one way.
    /// </summary>
    public EinzelError? Ambiguity { get; private set; }

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

        // Which flags never take a value is the verb's own statement (VerbFlags.ByVerb), read
        // before anything else: the parser cannot tell a switch from an option by looking - "a
        // following argument that is not itself an option is this option's value" - so
        // `render surface --cycle-mean models/q.json` read the model as the switch's value.
        // Asked of the verb named, so a name may be a switch for one verb and take a value for
        // another. A command line naming no verb this knows has only --json and --help, which
        // every verb takes; whatever it is, it is refused or answered with a usage.
        var verb = VerbFlags.For(args);
        var flags = verb?.Flags;

        // A command line naming no sub-verb this knows - `render --dry-run m.json`, an implicit
        // section - is read with the switches every sub-verb of its verb has, the set SubVerb
        // skips by: read with none, --dry-run took the model's path as its value.
        var family = flags is null && args.Length > 0 ? VerbFlags.Family(args[0]) : null;

        bool IsSwitch(string flag) =>
            flag is "json" or "help" || (flags?.Switches.Contains(flag) ?? family?.Switches.Contains(flag) ?? false);
        bool IsOptional(string flag) => flags?.Optional.Contains(flag) ?? family?.Optional.Contains(flag) ?? false;

        // Each word after a flag whose value may be left off that does not look like that value:
        // held as a positional for now, and given to its flag after all only if it falls past the
        // positionals the verb takes. Every one held, not the last: a second bare flag of the
        // kind overwrote the first's.
        var tentative = new List<(int Positional, int Option, string Name)>();

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
            var next = !IsSwitch(name) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);

            if (next && IsOptional(name) && !VerbFlags.LooksLikeOptionalValue(args[i + 1]))
            {
                // A flag whose value may be left off, followed by a word that does not look like
                // its value: `run --progress models/r.json` is the default interval and a model,
                // not an interval called models/r.json. Bare for now; settled below.
                parsed._options[name] = null;
                parsed._all.Add((name, null));
                tentative.Add((parsed._positional.Count, parsed._all.Count - 1, name));
                parsed._positional.Add(args[++i]);
            }
            else if (next)
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

        // Settled without guessing. Where the count fits, the held words are positionals. Where
        // the verb has more positionals than it takes, some held word was meant as a value or some
        // positional is a stray, and nothing on the line says which: `run --progress m.json
        // extra.json` may be an interval called m.json or a model and a stray. Every reading
        // that picks one runs a model somebody may not have meant or blames a word somebody did
        // mean - three rounds of review moved the blame from word to word - so the line is
        // refused, naming the flag and every word that could be the one meant.
        if (flags is not null && tentative.Count > 0 && parsed._positional.Count > flags.Positionals)
        {
            parsed.Ambiguity = Ambiguous(verb!.Value.Key, flags, [.. tentative.Select(t => t.Name).Distinct(StringComparer.Ordinal)], parsed._positional);
        }

        return parsed;
    }

    // The refusal for a line whose words could be read more than one way: the flags, every
    // candidate word - the positionals past the sub-verb, any of which may be the one meant - and
    // the two spellings that cannot be misread.
    private static EinzelError Ambiguous(string key, VerbFlags flags, IReadOnlyList<string> names, IReadOnlyList<string> positional)
    {
        static string Count(int n) => n switch
        {
            1 => "one",
            2 => "two",
            3 => "three",
            4 => "four",
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        static string Joined(IReadOnlyList<string> items) => items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + $" and {items[^1]}",
        };

        var sub = key.Count(c => c == ' ');
        var candidates = positional.Skip(sub).Select(p => $"'{p}'").ToList();
        var takes = flags.Positionals - sub;
        var flagged = names.Select(n => "--" + n).ToList();
        var first = flagged[0];

        return new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = FlagPath.Of(names[0]),
            Constraint = $"{Joined(flagged)} {(names.Count == 1 ? "takes an optional value" : "take optional values")}, and "
                + $"{Joined(candidates)} are {Count(candidates.Count)} words where {key} takes {Count(takes)}: "
                + $"which {(names.Count == 1 ? "word is " + first + "'s value" : "words are values")} and which "
                + $"{(takes == 1 ? "is the argument" : "are the arguments")} cannot be told from the line",
            Suggestion = $"give the value with an equals sign, as {first}=<value>, or put {first} last on the line; "
                + $"then every word is an argument, and {key} takes {Count(takes)}",
        };
    }
}
