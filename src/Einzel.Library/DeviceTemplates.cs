using System.Reflection;

namespace Einzel.Library;

/// <summary>
/// The device templates shipped with the platform.
/// </summary>
/// <remarks>
/// <para>
/// LIB-1: "Device templates are data in the same schema as any other model, plus
/// a declared parameter surface. If supporting a new device requires a change
/// below Einzel.Library, either it is genuinely novel physics or the abstraction
/// is wrong. Almost always the second."
/// </para>
/// <para>
/// So these are JSON documents embedded in the assembly, not classes. A mirror
/// pair, a quadrupole and a rectilinear trap share no code at all: they name the
/// same three electrode primitives in different arrangements, and everything below
/// reads a Dirichlet mask without knowing which is which. Adding a device is one
/// more file here, and nothing else - they are discovered by a resource glob, so
/// nothing registers them either.
/// </para>
/// <para>
/// They are also the beginning of the corpus EX-1 asks for. Each carries a prose
/// description of what it is and what varying its parameters does, because an
/// agent that has never seen this platform has no forum posts to fall back on.
/// </para>
/// </remarks>
public static class DeviceTemplates
{
    private const string Prefix = "Einzel.Library.Templates.";

    /// <summary>Every template name, in alphabetical order.</summary>
    /// <returns>The names, without extension.</returns>
    public static IReadOnlyList<string> Names()
    {
        var assembly = typeof(DeviceTemplates).Assembly;

        return
        [
            .. assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)
                    && n.EndsWith(".json", StringComparison.Ordinal))
                .Select(n => n[Prefix.Length..^".json".Length])
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Reads a template's document text.</summary>
    /// <param name="name">The template name.</param>
    /// <returns>The JSON document.</returns>
    /// <exception cref="ArgumentException">No such template.</exception>
    public static string Read(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var assembly = typeof(DeviceTemplates).Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + name + ".json");

        if (stream is null)
        {
            throw new ArgumentException(
                $"no template named '{name}'; available: {string.Join(", ", Names())}", nameof(name));
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
