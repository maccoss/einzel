using System.Reflection;

namespace Einzel.Commands;

/// <summary>
/// The corpus of validated reference models shipped with the platform.
/// </summary>
/// <remarks>
/// <para>
/// EX-1: "Ship at least thirty validated reference models spanning every device
/// class, each with a prose description, expected results, and assertion
/// tolerances." The reasoning is worth keeping in view - SIMION has decades of
/// forum posts and published geometries in the training data of every model an
/// agent might run on, and Einzel has none of that. Shipping models an agent can
/// pull into context is the counter, and it is the half of the agent thesis that
/// no amount of correct physics substitutes for.
/// </para>
/// <para>
/// Data, not code, and discovered by a resource glob for the same reason the
/// device templates are: adding one is a pair of files and nothing else, and a
/// registry that has to be edited alongside is a registry that will one day be
/// out of date. Each example is <c>name.json</c>, the model, plus
/// <c>name.test.json</c>, what it must produce.
/// </para>
/// <para>
/// <strong>Every expectation is a closed form or a published value</strong>,
/// never a number this engine produced once and then enshrined. A test whose
/// expectation came from the code it tests establishes that the code has not
/// changed, which is a different and much weaker claim than that it is right. The
/// description of each example says where its number comes from, because an
/// expectation whose provenance is not written down decays into exactly that.
/// </para>
/// </remarks>
public static class ExampleModels
{
    private const string Prefix = "Einzel.Commands.Examples.";
    private const string TestSuffix = ".test.json";

    private static Assembly Assembly => typeof(ExampleModels).Assembly;

    private static IEnumerable<string> Resources() =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.Ordinal));

    /// <summary>The examples that ship, by name, in a deterministic order (CLI-5).</summary>
    public static IReadOnlyList<string> Names =>
    [
        .. Resources()
            .Where(n => !n.EndsWith(TestSuffix, StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^".json".Length])
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The text of one example's model.</summary>
    /// <param name="name">Which example.</param>
    /// <returns>The model JSON.</returns>
    /// <exception cref="KeyNotFoundException">No example by that name.</exception>
    public static string Read(string name) => ReadResource(Prefix + name + ".json", name);

    /// <summary>
    /// The text of one example's test: what the model must produce, and why.
    /// </summary>
    /// <param name="name">Which example.</param>
    /// <returns>The test JSON.</returns>
    /// <exception cref="KeyNotFoundException">No example by that name.</exception>
    /// <remarks>
    /// Every example has one. An example with no assertion is a file that parses,
    /// which is a weaker thing than a reference model and reads like a stronger one
    /// - so the corpus refuses to contain any, and
    /// <c>ExampleCorpusTests.EveryExampleShipsATest</c> is what enforces it.
    /// </remarks>
    public static string ReadTest(string name) =>
        ReadResource(Prefix + name + TestSuffix, name);

    /// <summary>Whether an example ships a test.</summary>
    /// <param name="name">Which example.</param>
    /// <returns>True when it does.</returns>
    public static bool HasTest(string name) =>
        Assembly.GetManifestResourceInfo(Prefix + name + TestSuffix) is not null;

    private static string ReadResource(string resource, string name)
    {
        using var stream = Assembly.GetManifestResourceStream(resource);

        if (stream is null)
        {
            throw new KeyNotFoundException(
                $"no example named '{name}'; available: {string.Join(", ", Names)}");
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>The example a fresh project is scaffolded with.</summary>
    /// <remarks>
    /// The floor of the corpus and the first thing anyone runs, so it is the one
    /// with no geometry to solve and a flight time in closed form: <c>init</c> to
    /// <c>test</c> works from the first minute and the expected value is arithmetic
    /// rather than something this engine once produced.
    /// </remarks>
    public const string ScaffoldName = "single-stage-reflectron";

    /// <summary>The model a fresh project is scaffolded with.</summary>
    public static string SingleStageReflectron => Read(ScaffoldName);

    /// <summary>The test a fresh project is scaffolded with.</summary>
    public static string SingleStageReflectronTest => ReadTest(ScaffoldName);
}
