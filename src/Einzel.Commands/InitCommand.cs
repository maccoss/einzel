using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Einzel.Project;

namespace Einzel.Commands;

/// <summary>Serialiser settings shared by every command result.</summary>
/// <remarks>
/// CLI-1 requires structured output from every command, and CLI-5 requires that
/// ordering be deterministic. Property order follows declaration order, which is
/// stable across runs and machines.
/// </remarks>
public static class CommandJson
{
    /// <summary>The options used for all command output.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // A non-finite double is written as null rather than taking the whole
        // document down at the serialiser. See FiniteDoubleConverter for why this
        // is a property of the surface rather than a guard on each field.
        Converters = { new FiniteDoubleConverter(), new FiniteNullableDoubleConverter() },
        WriteIndented = true,
        NewLine = "\n",

        // OMISSION IS HOW THIS SURFACE SPELLS NULL, SO A PROPERTY THAT MAY BE NULL IS NOT
        // REQUIRED ON THE WIRE. `WhenWritingNull` above omits a null, and C#'s `required`
        // becomes a *deserialisation* requirement as well as a construction one - so a
        // record carrying `required double? EmittanceMmMrad`, which is exactly how this
        // surface spells "the construction site must decide and the answer may be nothing",
        // wrote a document that would not read back into its own type.
        //
        // Found by `einzel report`, the first thing here ever to read a result document
        // back: `verify` reads only manifests, so nothing had tried. A run wrote
        // `paul-trap-held.result.json` and this same build could not parse it, which makes
        // PRJ-3's "regenerate and compare" impossible for every ensemble run there has ever
        // been - and nothing said so, because writing succeeded.
        //
        // THE FIRST FIX WAS THE OTHER ONE AND IT WAS WRONG. Writing every required property
        // including its nulls also round-trips, and a test caught what it cost: this surface
        // states, in its own words, that "an undefined measurement is absent, not zero" and
        // that a consumer tells "no orientation" from "zero" by the key not being there. So
        // that fix changed the published document for every ensemble run and would have
        // broken any consumer using key presence the way the surface told it to.
        //
        // The precise statement is about which requirement is which. Absence of
        // `required int Launched` really is a malformed document. Absence of
        // `required double? EmittanceMmMrad` is this surface's own encoding of no value, so
        // demanding it on the way in is demanding that the encoding not be used. The wire
        // format does not change at all; only the reader stops refusing it.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { NullableRequiredPropertiesAreOptionalOnTheWire },
        },
    };

    /// <summary>
    /// Lets a required property that may be null round-trip, by not demanding it on read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Type level rather than per property</b>, because the next <c>required</c> nullable
    /// would be declared without an attribute and nobody would notice until something read
    /// it back - and until <c>einzel report</c> nothing here ever did.
    /// </para>
    /// <para>
    /// <b>Nullability is asked of the declaration, not of the runtime type.</b> A
    /// <c>double?</c> is detectable from <see cref="Nullable{T}"/> alone, but
    /// <c>JsonNode?</c> is a nullable reference type and is erased - so the annotation is
    /// read through <see cref="NullabilityInfoContext"/>, which is what the compiler wrote
    /// down. A property whose type does not admit null keeps its requirement, since absence
    /// there is a malformed document rather than an encoded nothing.
    /// </para>
    /// </remarks>
    private static void NullableRequiredPropertiesAreOptionalOnTheWire(JsonTypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Not static: the context caches per instance and is explicitly not thread-safe,
        // so one per call rather than one shared. A type's metadata is built once.
        var nullability = new NullabilityInfoContext();

        foreach (var property in type.Properties)
        {
            if (property.IsRequired && MayBeNull(nullability, property))
            {
                property.IsRequired = false;
            }
        }
    }

    /// <summary>Whether a property's declaration admits null.</summary>
    private static bool MayBeNull(NullabilityInfoContext context, JsonPropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        // A nullable reference type is an annotation rather than a type, so it has to be
        // read off the declaration. Absent an accessible declaration the property keeps its
        // requirement, which is the conservative direction: a document that refuses to read
        // says so, where one that reads a missing value as nothing is silently thinner.
        return property.AttributeProvider is PropertyInfo declaration
            && context.Create(declaration).ReadState == NullabilityState.Nullable;
    }

    /// <summary>Serialises a command result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="value">The result.</param>
    /// <returns>The JSON text, newline terminated.</returns>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    /// <summary>Reads a command document.</summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="json">The JSON text.</param>
    /// <returns>The document, or null if the text is a JSON null.</returns>
    /// <remarks>
    /// The same options as <see cref="Write{T}"/>, so a document this platform wrote
    /// reads back as what it was - which is what a stored render spec depends on.
    /// </remarks>
    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>The outcome of creating a project.</summary>
public sealed record InitOutcome
{
    /// <summary>The project root.</summary>
    public required string Root { get; init; }

    /// <summary>Files and directories created, relative to the root.</summary>
    public required IReadOnlyList<string> Created { get; init; }

    /// <summary>Whether an existing project was found and left alone.</summary>
    public required bool AlreadyExisted { get; init; }
}

/// <summary>
/// Creates a project directory.
/// </summary>
/// <remarks>
/// PRJ-4: a plain folder is the default and fully supported. Nothing here
/// touches version control; <c>--vcs git</c> (PRJ-5) adds an ignore file and
/// nothing else, and its absence changes no behaviour anywhere in the platform.
/// </remarks>
public static class InitCommand
{
    /// <summary>Creates the project layout, an example model, and AGENTS.md.</summary>
    /// <param name="root">The project root.</param>
    /// <param name="withGit">Whether to scaffold a git ignore file (PRJ-5).</param>
    /// <returns>What was created.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    public static InitOutcome Execute(string root, bool withGit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var project = new ProjectLayout(root);
        var existed = project.Exists;
        var created = new List<string>();

        project.CreateDirectories();

        foreach (var directory in project.TrackedDirectories)
        {
            created.Add(Path.GetRelativePath(project.Root, directory) + "/");
        }

        created.Add(ProjectLayout.ScratchDirectoryName + "/");

        var examplePath = Path.Combine(project.Models, "reflectron.json");

        if (!File.Exists(examplePath))
        {
            File.WriteAllText(examplePath, ExampleModels.SingleStageReflectron);
            created.Add(Path.GetRelativePath(project.Root, examplePath));
        }

        var testPath = Path.Combine(project.Tests, "reflectron.json");

        if (!File.Exists(testPath))
        {
            // The shipped test names the model by its corpus name; init writes it as
            // reflectron.json, so the reference is rewritten to what actually landed.
            // A scaffolded project whose one test cannot find its one model is the
            // worst possible first minute.
            File.WriteAllText(
                testPath,
                ExampleModels.SingleStageReflectronTest.Replace(
                    $"../models/{ExampleModels.ScaffoldName}.json",
                    "../models/reflectron.json",
                    StringComparison.Ordinal));
            created.Add(Path.GetRelativePath(project.Root, testPath));
        }

        if (!File.Exists(project.AgentsFile))
        {
            File.WriteAllText(project.AgentsFile, AgentsFile.Generate());
            created.Add(Path.GetRelativePath(project.Root, project.AgentsFile));
        }

        if (withGit)
        {
            var ignorePath = Path.Combine(project.Root, ".gitignore");

            if (!File.Exists(ignorePath))
            {
                File.WriteAllText(
                    ignorePath,
                    "# Field caches, trajectories, frames. Large, binary, and regenerable\n"
                    + "# from the run manifest, which is why discarding this loses nothing.\n"
                    + ProjectLayout.ScratchDirectoryName + "/\n");

                created.Add(".gitignore");
            }
        }

        return new InitOutcome { Root = project.Root, Created = created, AlreadyExisted = existed };
    }
}
