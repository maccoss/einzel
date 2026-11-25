using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>Each documentation comment in the source says one thing about one member.</summary>
/// <remarks>
/// A member gained a second <c>&lt;summary&gt;</c> when its neighbour's was left behind in a move:
/// the compiler takes the first and drops the rest without a warning, so one member was
/// described twice and the other lost its description, and nothing failed.
/// </remarks>
public sealed class DocumentationCommentTests(ITestOutputHelper output)
{
    // Found when this test was written (2026-11-17), all older than the PR that added it and
    // left for their own change: each needs its owner to say which member the orphan described.
    // Keyed by file and the declaration the doubled comment sits on, so moving code does not
    // break the list, and a new doubled comment fails.
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "src/Einzel.Cli/Program.cs|private static string InferProjectRoot(string modelPath)",
        "src/Einzel.Commands/DiffusionRun.cs|internal static Transport.Diffusion.PonderomotiveField? Effective(",
        "src/Einzel.Commands/FiguresOfMerit.cs|private static List<(double EnergyFraction, double FlightTime)> EnergyScan(",
        "src/Einzel.Commands/FiguresOfMerit.cs|private static double? Transit(",
        "src/Einzel.Commands/FiguresOfMerit.cs|private static double? Oscillation(",
        "src/Einzel.Commands/FiguresOfMerit.cs|private static int Distinct(",
        "src/Einzel.Commands/FiguresOfMerit.cs|private static double? MeanKineticEnergy(",
        "src/Einzel.Commands/RenderCommand.cs|private static IReadOnlyList<Core.Results.ValidityWarning> TransportWarnings(",
        "src/Einzel.Commands/ReportPage.cs|private const string Converted =",
        "src/Einzel.Commands/RunCommand.cs|private static IReadOnlyList<ValidityWarning> GridResolutionWarnings(",
        "src/Einzel.Commands/RunCommand.cs|private static List<ValidityWarning> ProvenanceWarnings(CompiledModel model)",
        "src/Einzel.Commands/RunCommand.cs|public static bool Eluted(double collected, double launched)",
        "src/Einzel.Commands/RunCommand.cs|internal static double SmallestAperture(CompiledModel model)",
        "src/Einzel.Commands/SequencedRun.cs|private static IEnumerable<Vec3> Probes(DensityField density, Grid2D grid)",
        "src/Einzel.Commands/TestCommand.cs|public bool AllPassed => Tests.Count > 0 && Tests.All(t => t.Passed);",
        "src/Einzel.Core/Model/ModelValidator.cs|private static readonly Dictionary<string, Quantity> NoParameters = new(StringComparer.Ordinal);",
        "src/Einzel.Core/Model/ModelValidator.cs|private static CompiledDensityStep DensityStep(",
        "src/Einzel.Fields/FieldAssembly.cs|private static IElectrostaticField Analytic(CompiledField element)",
        "src/Einzel.Fields/Solved/PoissonSolver3D.cs|private static int SolveCoarsest(",
        "src/Einzel.Render/Surfaces.cs|public static SurfaceMesh FromSignedDistance(",
        "src/Einzel.Transport/Diffusion/DriftDiffusion.cs|public static (double Seconds, string Limit) StepFor(",
    };

    /// <summary>No comment block has two summaries.</summary>
    [Fact]
    public void NoCommentHasTwoSummaries()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var found = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            var summaries = 0;
            var inherits = 0;

            for (var i = 0; i <= lines.Length; i++)
            {
                var line = i < lines.Length ? lines[i].TrimStart() : string.Empty;

                if (line.StartsWith("///", StringComparison.Ordinal))
                {
                    summaries += line.Contains("<summary>", StringComparison.Ordinal) ? 1 : 0;
                    inherits += line.Contains("<inheritdoc", StringComparison.Ordinal) ? 1 : 0;
                    continue;
                }

                // An inherited comment said twice is the same orphan in another spelling.
                if (inherits > 1)
                {
                    found.Add(Path.GetRelativePath(root, file).Replace('\\', '/') + "|" + line + $" (line {i + 1}, inheritdoc twice)");
                }

                inherits = 0;

                if (summaries > 1)
                {
                    var key = Path.GetRelativePath(root, file).Replace('\\', '/') + "|" + line;

                    if (!Known.Contains(key))
                    {
                        found.Add($"{key} (line {i + 1})");
                    }
                }

                summaries = 0;
            }
        }

        output.WriteLine($"{files.Count} files read");

        Assert.True(files.Count > 100, "the source was not found");
        Assert.Empty(found);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SPEC.md")))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }
}
