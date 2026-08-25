using System.Text.Json;
using System.Text.Json.Serialization;
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
    };

    /// <summary>Serialises a command result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="value">The result.</param>
    /// <returns>The JSON text, newline terminated.</returns>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";
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
            File.WriteAllText(testPath, ExampleModels.SingleStageReflectronTest);
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
