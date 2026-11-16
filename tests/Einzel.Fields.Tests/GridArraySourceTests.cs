using System.Text.RegularExpressions;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// Nothing in the solver's directory allocates an array of numbers except through
/// <c>GridArrays</c>.
/// </summary>
/// <remarks>
/// <para>
/// The memory tests read what a build holds at each allocation <c>GridArrays</c> makes, and
/// bound what is allocated any other way as slack. That measures an array allocated directly;
/// this stops one being written. It reads the source, because the rule is about how code is
/// spelled: a per-node array allocated with <c>new</c> compiles, runs and solves, and is only
/// invisible.
/// </para>
/// <para>
/// <b>What is refused</b>: <c>new</c> of an array of a numeric type or <c>bool</c> whose length
/// is not a literal, <c>Array.Resize</c>, the collector's own array allocators, and
/// <c>.ToArray()</c> or <c>.Clone()</c>, which copy one. Arrays of references, literal lengths,
/// stack allocation and collection expressions are not: none of them is the size of a grid.
/// The few lines that are caught and are not per-node are listed below with the reason, and a
/// line listed that no longer exists fails too, so the list cannot outlive what it excuses.
/// </para>
/// </remarks>
public sealed partial class GridArraySourceTests(ITestOutputHelper output)
{
    /// <summary>Lines the patterns catch that do not allocate anything the size of a grid.</summary>
    private static readonly (string File, string Code, string Why)[] Allowed =
    [
        ("DriveChannels.cs", "new List<double>(new double[channels.Count])", "one weight a basis channel"),
        ("DrivenSolvedField.cs", "[.. harmonics.Select(h => h.ToArray())]", "weight terms a channel"),
        ("DrivenSolvedField.cs", "[.. stageDirect.Select(d => d.ToArray())]", "one weight a channel a stage"),
        ("DrivenSolvedField.cs", "stage.Select(h => h.ToArray()).ToArray()", "weight terms a channel a stage"),
        ("DrivenSolvedField.cs", "[.. stageEndDirect.Select(d => d?.ToArray())]", "one weight a channel a stage"),
        ("DrivenSolvedField.cs", "stage?.Select(h => h.ToArray()).ToArray()", "weight terms a channel a stage"),
        ("PoissonSolver2D.cs", "initialGuess?.Clone() ?? new ScalarField2D(grid)", "a field's own Clone, which allocates through its constructor"),
        ("PoissonSolver3D.cs", "initialGuess?.Clone() ?? new ScalarField3D(grid)", "a field's own Clone, which allocates through its constructor"),
    ];

    [Fact]
    public void EveryArrayOfNumbersInTheSolverComesFromGridArrays()
    {
        var directory = Path.Combine(RepositoryRoot(), "src", "Einzel.Fields", "Solved");
        var files = Directory.GetFiles(directory, "*.cs");

        var caught = new List<string>();
        var excused = new HashSet<int>();
        var through = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);

            if (name == "GridArrays.cs")
            {
                continue;
            }

            var lines = File.ReadAllLines(file);

            for (var at = 0; at < lines.Length; at++)
            {
                var code = Comment().Replace(lines[at], string.Empty);

                through += GridArray().Count(code);

                if (!Direct().IsMatch(code))
                {
                    continue;
                }

                var excuse = Array.FindIndex(Allowed, a => a.File == name && code.Contains(a.Code, StringComparison.Ordinal));

                if (excuse >= 0)
                {
                    excused.Add(excuse);
                }
                else
                {
                    caught.Add($"{name}({at + 1}): {code.Trim()}");
                }
            }
        }

        output.WriteLine($"{files.Length} files, {through} allocations through GridArrays, {excused.Count} excused lines");

        // A scan of the wrong directory, or of nothing, finds nothing wrong.
        Assert.True(files.Length >= 20, $"only {files.Length} files were read from {directory}");
        Assert.True(through >= 20, $"only {through} allocations go through GridArrays");

        Assert.True(
            caught.Count == 0,
            "an array is allocated in the solver's directory without GridArrays, so the memory tests do not read the "
            + "live set at it. Allocate it with GridArrays.New or grow it with GridArrays.Resize; if it is not the size "
            + "of a grid, list the line in this test with the reason." + Environment.NewLine
            + string.Join(Environment.NewLine, caught));

        var stale = Enumerable.Range(0, Allowed.Length).Where(index => !excused.Contains(index)).ToArray();

        Assert.True(
            stale.Length == 0,
            "a listed exception no longer matches any line: "
            + string.Join("; ", stale.Select(index => $"{Allowed[index].File}: {Allowed[index].Code}")));
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

        // Thrown, not skipped: a test that passes when it cannot find what it checks is worse
        // than one that is absent.
        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }

    /// <summary>A line comment, documentation comments included.</summary>
    [GeneratedRegex(@"//.*$")]
    private static partial Regex Comment();

    /// <summary>An array of numbers allocated or copied without <c>GridArrays</c>.</summary>
    [GeneratedRegex(
        @"\bnew\s+(?:double|float|decimal|int|uint|long|ulong|short|ushort|byte|sbyte|bool|char|nint|nuint|Half)\s*\[\s*(?![0-9_]+\s*\])[^\]]+\](?!\s*\[)"
        + @"|\bArray\s*\.\s*Resize\b"
        + @"|\bGC\s*\.\s*Allocate(?:Uninitialized)?Array\b"
        + @"|\.\s*ToArray\s*\(\s*\)"
        + @"|\.\s*Clone\s*\(\s*\)")]
    private static partial Regex Direct();

    [GeneratedRegex(@"\bGridArrays\s*\.\s*(?:New|Resize)\b")]
    private static partial Regex GridArray();
}
