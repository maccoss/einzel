namespace Einzel.Cli;

/// <summary>The flags one verb reads, and how many positional arguments it takes.</summary>
/// <param name="Switches">Flags that take no value.</param>
/// <param name="Values">Flags that take a value.</param>
/// <param name="Refused">
/// Spellings the verb knows only to refuse with an explanation of its own - a bare
/// <c>--at</c> where <c>--at-mm</c> is meant - so they are neither unknown nor checked for a value
/// here.
/// </param>
/// <param name="Positionals">The most positional arguments the verb takes, its sub-verb included.</param>
/// <param name="Optional">
/// Flags that take a value which may be left off: bare, the flag means its default.
/// <c>--progress</c> is one - reporting is on unless turned off, so the flag alone asks for the
/// default interval, as it always did.
/// </param>
public sealed record VerbFlags(
    IReadOnlySet<string> Switches,
    IReadOnlySet<string> Values,
    IReadOnlySet<string> Refused,
    int Positionals,
    IReadOnlySet<string> Optional)
{
    /// <summary>Whether the verb knows this flag at all.</summary>
    /// <param name="flag">The flag, without its dashes.</param>
    /// <returns>True for a switch, a flag that takes a value or may, a refused spelling, or a global flag.</returns>
    public bool Knows(string flag) =>
        Global.Contains(flag) || Switches.Contains(flag) || Values.Contains(flag) || Optional.Contains(flag) || Refused.Contains(flag);

    /// <summary>
    /// Every flag that never takes a value, in any verb, with <c>--help</c> and <c>--json</c>:
    /// what the parser needs to know before it knows the verb, so that the argument after a
    /// switch is never taken as its value.
    /// </summary>
    /// <remarks>
    /// Derived, so there is one statement of which flags are switches. It is sound only while no
    /// name is a switch in one verb and takes a value in another, which
    /// <c>VerbFlagsTests.NoFlagIsASwitchInOneVerbAndAValueInAnother</c> holds.
    /// </remarks>
    public static IReadOnlySet<string> AllSwitches => Every.Value;

    private static readonly Lazy<HashSet<string>> Every = new(Union);

    /// <summary>
    /// Flags every verb accepts: <c>--json</c> (CLI-1 - a verb with nothing to put in JSON still
    /// accepts it, so an agent need not special-case one). <c>--help</c> is not checked at all: a
    /// command line asking for it is left to the verb, as it always was.
    /// </summary>
    public static readonly IReadOnlySet<string> Global = new HashSet<string>(StringComparer.Ordinal) { "json" };

    /// <summary>
    /// Every verb's flags, keyed by the verb - or, where it has sub-verbs that read different
    /// flags, by the verb and the sub-verb (<c>render surface</c>).
    /// </summary>
    /// <remarks>
    /// The one statement of which flags a verb reads. <c>VerbFlagsTests</c> reads each handler's
    /// source, through every helper it calls, and fails when a handler reads a flag this does not
    /// list or this lists a flag the handler does not read - so it cannot drift from the verbs.
    /// A flag a verb does not read is refused by name: it used to be accepted and ignored, which
    /// is how a mistyped <c>--no-scales</c> drew the scale anyway with exit 0.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, VerbFlags> ByVerb = new Dictionary<string, VerbFlags>(StringComparer.Ordinal)
    {
        ["init"] = Of(["dry-run"], ["vcs"], 1),
        ["new"] = Of(["dry-run"], ["from-example", "from-template"], 1),
        ["validate"] = Of([], [], 1),
        ["outline"] = Of(["dry-run"], ["set"], 1),
        ["project"] = Of([], ["project"], 1),
        ["estimate"] = Of(["no-calibrate"], ["threshold"], 1),
        ["solve"] = Of([], [], 1),
        ["probe"] = Of(["dry-run"], ["at-mm", "line-mm", "to-mm", "samples", "at-us", "csv"], 1, ["at", "line", "to"]),
        ["run"] = Of(["vtu"], ["project"], 1, optional: ["progress"]),
        ["sweep"] = Of(["dry-run"], ["project"], 1),
        ["optimise"] = Of(["dry-run"], ["project"], 1),
        ["optimize"] = Of(["dry-run"], ["project"], 1),
        ["scan"] = Of(["dry-run"], ["project"], 1),
        ["boundary"] = Of(["dry-run"], ["project"], 1),
        ["preview"] = Of([], [], 1),
        ["test"] = Of([], ["project"], 1),
        ["verify"] = Of([], ["project"], 1),
        ["report"] = Of(["dry-run"], ["out", "project"], 1),
        ["export"] = Of(["composite", "dry-run", "mesh"], ["at-us", "box-mm", "project", "step-mm"], 1),
        ["compare"] = Of([], ["ions"], 1),
        ["schema"] = Of(["study"], [], 0),
        ["templates"] = Of([], [], 1),
        ["examples"] = Of([], [], 1),
        ["agents-md"] = Of(["dry-run"], ["project"], 1),
        ["agents"] = Of(["dry-run"], [], 3),
        ["doctor"] = Of([], ["project"], 1),
        ["ext"] = Of(["dry-run"], ["input", "kind", "project"], 2),
        ["render section"] = Of(
            ["dry-run", "no-trajectory"],
            ["at-us", "caption", "equipotentials", "format", "out", "project", "width-mm"],
            2,
            optional: ["progress"]),
        ["render still"] = Of(["dry-run", "no-scale", "see-through"], ["height-px", "out", "project", "view", "width-px"], 2),
        ["render surface"] = Of(
            ["cycle-mean", "dry-run", "no-scale"],
            ["at-us", "box-mm", "csv", "height", "height-mm-per-ev", "height-mm-per-v", "height-px", "mesh-lines",
                "offset-mm", "out", "plane", "project", "relief", "samples", "view", "width-px"],
            2,
            ["offset", "box", "at", "height-mm"]),
        ["render animation"] = Of(["dry-run"], ["caption", "format", "fps", "out", "project", "width-mm"], 2, optional: ["progress"]),
    };

    /// <summary>The flags of the verb a command line names, or null where it names none this knows.</summary>
    /// <param name="args">The raw arguments, verb first.</param>
    /// <returns>The verb's key and its flags, or null.</returns>
    public static (string Key, VerbFlags Flags)? For(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return null;
        }

        // A sub-verb that reads its own flags. `render` alone, or with a word that is no
        // sub-verb, is left to the verb's own usage, as it always was.
        var key = args[0] == "render" && args.Count > 1 && !args[1].StartsWith('-')
            ? "render " + args[1]
            : args[0];

        return ByVerb.TryGetValue(key, out var flags) ? (key, flags) : null;
    }

    private static VerbFlags Of(
        string[] switches, string[] values, int positionals, string[]? refused = null, string[]? optional = null) =>
        new(
            new HashSet<string>(switches, StringComparer.Ordinal),
            new HashSet<string>(values, StringComparer.Ordinal),
            new HashSet<string>(refused ?? [], StringComparer.Ordinal),
            positionals,
            new HashSet<string>(optional ?? [], StringComparer.Ordinal));

    private static HashSet<string> Union()
    {
        // --help is a switch for the parser although no verb is held to it: `run --help
        // model.json` must not read the model as --help's value.
        var all = new HashSet<string>(Global, StringComparer.Ordinal) { "help" };

        foreach (var flags in ByVerb.Values)
        {
            all.UnionWith(flags.Switches);
        }

        return all;
    }
}
