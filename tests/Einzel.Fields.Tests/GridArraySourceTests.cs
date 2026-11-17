using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// Nothing in <c>Einzel.Fields</c> or <c>Einzel.Transport</c> allocates storage the size of a
/// grid except through <c>GridArrays</c>.
/// </summary>
/// <remarks>
/// <para>
/// The memory tests read what a solve or a density run holds at each allocation
/// <c>GridArrays</c> makes, and bound what is allocated any other way. This stops such an
/// allocation being written. It parses the sources with the C# compiler's own parser, compiles
/// them, and follows the size of every allocation back to where it comes from
/// (<see cref="GridSizeScan"/>, whose remarks state the rule and what it cannot show).
/// </para>
/// <para>
/// <b>Which assemblies.</b> Refused in <c>Einzel.Fields</c> and <c>Einzel.Transport</c>: between
/// them they allocate every array the size of a field's grid or a density's - the solvers'
/// fields, masks, cut links and operators; the densities, step coefficients, face operator,
/// self-field, well cache and conductor map of a diffusive run; the particle-in-cell grid.
/// Those are the operations <c>einzel estimate</c> bills by the node and the memory tests
/// measure. The other engine assemblies are compiled with them, so that a size passed in from
/// <c>Einzel.Commands</c> can be followed to where it was made, but nothing is refused there:
/// what <c>Einzel.Commands</c> and <c>Einzel.Io</c> allocate by the node - a field sampled for
/// export, a viewport's contours, an imported gas field - is not part of a solve or a run's
/// working set, is not billed as one, and is not measured.
/// </para>
/// <para>
/// <b>What is looked at</b>: an array creation with a size that is not a constant; a collection
/// expression with a spread; a call to <c>ToArray</c>, <c>ToList</c>, <c>ToHashSet</c>,
/// <c>ToDictionary</c>, <c>Clone</c>, <c>Resize</c>, <c>AllocateArray</c>,
/// <c>AllocateUninitializedArray</c>, <c>Rent</c> or <c>CreateInstance</c> that is not a method
/// of the sources' own; and a <c>List</c>, <c>HashSet</c>, <c>Dictionary</c>, <c>Queue</c>,
/// <c>Stack</c>, <c>SortedSet</c>, <c>SortedDictionary</c> or <c>LinkedList</c> constructed
/// with an argument, written out or as a target-typed <c>new</c>. Each is refused or passed by
/// the rule, not by a list: a list of drives is passed because it is a list of drives.
/// </para>
/// <para>
/// What the rule refuses and is nevertheless right is listed below with the reason. A listed
/// line that no longer exists fails too, so the list cannot outlive what it excuses.
/// </para>
/// </remarks>
public sealed partial class GridArraySourceTests(ITestOutputHelper output)
{
    /// <summary>The assemblies in which an allocation is refused.</summary>
    private static readonly string[] Covered = ["Einzel.Fields", "Einzel.Transport"];

    /// <summary>The assemblies compiled beside them, for a size to be followed into.</summary>
    private static readonly string[] Beside =
    [
        "Einzel.Core", "Einzel.Io", "Einzel.Analysis", "Einzel.Sweeps", "Einzel.Library", "Einzel.Project",
        "Einzel.Extensions", "Einzel.Render", "Einzel.Commands",
    ];

    /// <summary>
    /// What the rule refuses and is right as it is: the file under <c>src</c>, the member it is
    /// in, the expression as written with its whitespace collapsed, how many times it occurs in
    /// that member, and why.
    /// </summary>
    /// <remarks>
    /// Anchored to the member and counted, so that an excuse covers what it was written for and
    /// nothing else: the same expression written a second time in the same member, or moved to
    /// another, is refused again.
    /// </remarks>
    private static readonly (string File, string Member, string Code, int Count, string Why)[] Allowed =
    [
        // The volume cull's candidate lists: for each block of 512 nodes, the electrodes a link
        // from it could meet. One reference a block and one index an electrode near it - under
        // 0.2 bytes a node - made and dropped with the mask builder, before the solve. They are
        // the size of the grid divided by 512, which is the size of a grid, and are left as they
        // are: read at, they would be a reading a block for storage that is in the bound already.
        ("Einzel.Fields/Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Candidates", "new List<int>?[blocksX * blocksY * blocksZ]", 1, "one reference a block of 512 nodes"),
        ("Einzel.Fields/Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Candidates", "new int[lists.Length][]", 1, "one reference a block of 512 nodes"),
        ("Einzel.Fields/Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Candidates", "[.. list]", 1, "one index an electrode near the block: the loops round its Add choose the block, they do not fill one list"),
    ];

    [Fact]
    public void EveryGridSizedAllocationComesFromGridArrays()
    {
        var root = Path.Combine(RepositoryRoot(), "src");
        var trees = new List<(string Relative, SyntaxTree Tree)>();

        foreach (var assembly in Covered.Concat(Beside))
        {
            var directory = Path.Combine(root, assembly);

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (relative.Split('/').Any(part => part is "obj" or "bin"))
                {
                    continue;
                }

                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), GridSizeScan.Options, file);
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

                Assert.True(
                    errors.Length == 0,
                    $"{relative} does not parse, so it cannot be checked: {string.Join("; ", errors.Select(e => e.ToString()))}");

                trees.Add((relative, tree));
            }
        }

        var scan = new GridSizeScan(trees.Select(t => t.Tree));
        var found = new Dictionary<(string File, string Member, string Code), (int Count, int Line, Shown Shown)>();
        var files = Covered.ToDictionary(assembly => assembly, _ => 0);
        var through = 0;
        var targetTyped = 0;
        var looked = 0;

        foreach (var (relative, tree) in trees)
        {
            if (Covered.FirstOrDefault(assembly => relative.StartsWith(assembly + "/", StringComparison.Ordinal)) is not { } assembly)
            {
                continue;
            }

            files[assembly]++;
            through += tree.GetRoot().DescendantNodes().Count(GridSizeScan.IsGridArrays);
            targetTyped += GridSizeScan.TargetTyped(tree);
            looked += scan.Looked(tree);

            foreach (var refused in scan.Refusals(tree))
            {
                var key = (relative, Member(refused.Node), Collapse(refused.Node.ToString()));
                var line = refused.Node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                found[key] = found.TryGetValue(key, out var seen) ? (seen.Count + 1, seen.Line, seen.Shown) : (1, line, refused.Shown);
            }
        }

        var caught = new List<string>();
        var excused = new HashSet<int>();

        foreach (var ((file, member, code), (count, line, shown)) in found.OrderBy(f => f.Key))
        {
            var excuse = Array.FindIndex(Allowed, a => a.File == file && a.Member == member && a.Code == code);

            if (excuse >= 0)
            {
                excused.Add(excuse);
            }

            if (excuse >= 0 && Allowed[excuse].Count == count)
            {
                continue;
            }

            var listed = excuse >= 0 ? $" // listed {Allowed[excuse].Count} times, found {count}" : "";
            var why = shown.Extent == Extent.Grid ? $"the size of a grid, through {shown.Why}" : $"not shown small: {Collapse(shown.Why)}";

            caught.Add($"(\"{file}\", \"{member}\", \"{Escape(code)}\", {count}, \"\"), // line {line}: {why}{listed}");
        }

        output.WriteLine(
            $"{string.Join(", ", files.Select(f => $"{f.Value} files of {f.Key}"))}; {through} allocations through GridArrays, "
            + $"{looked} others sized at run time, of which {found.Values.Sum(f => f.Count)} refused and {excused.Count} of those "
            + $"listed; {targetTyped} target-typed news; {trees.Count} files compiled");

        // A scan of the wrong directory, or of nothing, finds nothing wrong.
        Assert.True(files["Einzel.Fields"] >= 40, $"only {files["Einzel.Fields"]} files were read of Einzel.Fields");
        Assert.True(files["Einzel.Transport"] >= 30, $"only {files["Einzel.Transport"]} files were read of Einzel.Transport");
        Assert.True(through >= 40, $"only {through} allocations go through GridArrays");
        Assert.True(looked >= 80, $"only {looked} allocations sized at run time were found to judge");

        Assert.True(
            caught.Count == 0,
            "storage that is the size of a grid, or cannot be shown not to be, is allocated without GridArrays, so the "
            + "memory tests do not read the live set at it. Allocate an array with GridArrays.New or grow it with "
            + "GridArrays.Resize; if it is right as it is, list it in this test with the reason." + Environment.NewLine
            + string.Join(Environment.NewLine, caught));

        var stale = Enumerable.Range(0, Allowed.Length).Where(index => !excused.Contains(index)).ToArray();

        Assert.True(
            stale.Length == 0,
            "a listed exception no longer matches anything: "
            + string.Join("; ", stale.Select(index => $"{Allowed[index].File} {Allowed[index].Member}: {Allowed[index].Code}")));

        // The rule is meant to pass what is small by itself: a list that grows back is the rule failing.
        Assert.True(Allowed.Length < 10, $"{Allowed.Length} exceptions are listed: the rule should pass what is small, not a list");
    }

    /// <summary>Storage whose size follows back to a grid is refused, whatever it holds and however it gets there.</summary>
    [Theory]
    [InlineData("var a = new double[grid.CountX * grid.CountY];")]
    [InlineData("var a = new Vec3[grid.NodeCount];")]
    [InlineData("var n = grid.CountX; var m = n * 4; var a = new bool[m];")]
    [InlineData("var a = new Thing[grid.NodeCount];")]
    [InlineData("var a = new double[grid.CountX][];")]
    [InlineData("var lists = new List<int>?[n]; for (var i = 0; i < grid.NodeCount; i++) { (lists[i % n] ??= []).Add(i); } int[] a = [.. lists[0]!];")]
    [InlineData("var a = new List<Thing>(grid.NodeCount);")]
    [InlineData("var a = new double[Cells()];")]
    [InlineData("var a = new double[cells];")]
    [InlineData("var a = new double[Math.Max(1, grid.CountX)];")]
    [InlineData("var a = new double[field.Length];")]
    [InlineData("double[] a = [.. field];")]
    [InlineData("var a = field.ToArray();")]
    [InlineData("var a = (double[])field.Clone();")]
    [InlineData("var a = new List<double>(field);")]
    [InlineData("var a = new double[Passed(grid.CountX)];")]
    [InlineData("var a = Make(grid.CountX);")]
    [InlineData("var seen = new List<double>(); for (var i = 0; i < grid.NodeCount; i++) { seen.Add(1.0); } var a = seen.ToArray();")]
    [InlineData("var seen = new List<double>(); foreach (var value in field) { seen.Add(value); } double[] a = [.. seen];")]
    [InlineData("var count = 0; foreach (var value in field) { count++; } var a = new double[count];")]
    public void WhatIsTheSizeOfAGridIsRefused(string statement)
    {
        var refused = Refused(statement);

        Assert.True(refused.Count >= 1, $"not refused: {statement}");
        Assert.Contains(refused, r => r.Shown.Extent == Extent.Grid);
    }

    /// <summary>Storage for bare numbers whose size cannot be followed anywhere is refused: not shown small.</summary>
    [Theory]
    [InlineData("var a = new double[n];")]
    [InlineData("var a = new T[n];")]
    [InlineData("var a = new double[\n    n];")]
    [InlineData("double[] a = [.. values];")]
    [InlineData("var a = values.ToArray();")]
    [InlineData("var a = unknown.Clone();")]
    [InlineData("Array.Resize(ref numbers, n);")]
    [InlineData("var a = GC.AllocateUninitializedArray<double>(n);")]
    [InlineData("var a = ArrayPool<double>.Shared.Rent(n);")]
    [InlineData("var a = new List<double>(n);")]
    [InlineData("List<double> a = new(n);")]
    [InlineData("var a = new HashSet<int>(values);")]
    [InlineData("_cells = new(n);")]
    [InlineData("member List<double> M() { return new(n); }")]
    [InlineData("member List<double> M() => new(n);")]
    [InlineData("member void M() { Use(new(n)); } void Use(List<double> cells) { }")]
    [InlineData("Unknown u = new(n);")]
    [InlineData("var seen = new List<double>(); while (Something()) { seen.Add(1.0); } var a = seen.ToArray();")]
    [InlineData("var a = new double[names.Count];")]
    public void WhatIsNotShownSmallIsRefused(string statement)
    {
        Assert.True(Refused(statement).Count == 1, $"not refused, or refused twice: {statement}");
    }

    /// <summary>
    /// What is one entry an electrode, a drive, a stage or a channel is passed by the rule, with
    /// no exception listed: a collection of things, or numbers as many as a collection of things.
    /// </summary>
    [Theory]
    [InlineData("var a = new double[4];")]
    [InlineData("var a = new[] { 1.0, 2.0 };")]
    [InlineData("var a = new double[] { 1.0, 2.0 };")]
    [InlineData("double[] a = [1.0, 2.0, 3.0];")]
    [InlineData("Span<double> a = stackalloc double[n];")]
    [InlineData("var a = new List<double>();")]
    [InlineData("var a = new List<double>(4);")]
    [InlineData("var a = GridArrays.New<double>(n);")]
    [InlineData("GridArrays.Resize(ref numbers, n);")]
    [InlineData("_cells = new();")]
    [InlineData("P p = new(n, n);")]
    [InlineData("member P M() => new(n, n);")]
    [InlineData("var a = new double[drives.Count];")]
    [InlineData("var a = new List<double>(drives.Count);")]
    [InlineData("var a = new Thing[n];")]
    [InlineData("var a = new List<Thing>(n);")]
    [InlineData("var a = new double[n][];")]
    [InlineData("Thing[] a = [.. drives];")]
    [InlineData("var a = drives.ToArray();")]
    [InlineData("var a = drives.Where(d => d is not null).ToList();")]
    [InlineData("double[] a = [.. drives.Select(d => d.Amplitude)];")]
    [InlineData("var a = drives.Select(d => d.Amplitude).ToArray();")]
    [InlineData("var count = drives.Count; var a = new double[2 * count + 1];")]
    [InlineData("var a = new Vec3[points.Length];")]
    [InlineData("var a = points.ToArray();")]
    [InlineData("var weights = new List<double>(drives.Count); foreach (var drive in drives) { weights.Add(drive.Amplitude); } double[] a = [.. weights];")]
    [InlineData("var weights = new List<double>(); foreach (var drive in drives) { weights.Add(drive.Amplitude); } var a = weights.ToArray();")]
    [InlineData("var weights = new List<double>(); for (var k = 0; k < drives.Count; k++) { weights.Add(1.0); } var a = weights.ToArray();")]
    [InlineData("var a = new double[Passed(drives.Count)];")]
    [InlineData("var a = new double[Environment.ProcessorCount];")]
    [InlineData("var a = new string[text.Length];")]
    [InlineData("var a = text.Split(',').ToArray();")]
    [InlineData("var a = own.Clone();")]
    [InlineData("var a = new HashSet<string>(StringComparer.Ordinal);")]
    [InlineData("var a = perDrive.Select(w => w.ToArray()).ToArray();")]
    [InlineData("var a = new Dictionary<string, double>(drives.Count, StringComparer.Ordinal);")]
    public void WhatIsACountOfThingsIsPassed(string statement)
    {
        var refused = Refused(statement);

        Assert.True(
            refused.Count == 0,
            $"refused: {statement}: {string.Join("; ", refused.Select(r => $"{r.Shown.Extent} through {r.Shown.Why}"))}");
    }

    /// <summary>
    /// An expression listed for one member is refused when it is written a second time there, or
    /// in another member.
    /// </summary>
    [Fact]
    public void AnExcuseCoversOneMemberAndOneCount()
    {
        const string source = """
            using System.Collections.Generic;
            class C
            {
                int n;
                void Once() { var a = new List<double>(n); }
                void Twice() { var a = new List<double>(n); var b = new List<double>(n); }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source, GridSizeScan.Options);

        var found = new GridSizeScan([tree]).Refusals(tree)
            .GroupBy(refused => (Member(refused.Node), Collapse(refused.Node.ToString())))
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(1, found[("C.Once", "new List<double>(n)")]);
        Assert.Equal(2, found[("C.Twice", "new List<double>(n)")]);
    }

    /// <summary>The refusals in a statement, written into a class that has a grid, things and numbers to hand.</summary>
    private static List<Refused> Refused(string statement)
    {
        // Members as well as statements: a target-typed new in an assignment to a field, a return
        // or an argument needs somewhere to be declared.
        var body = statement.StartsWith("member ", StringComparison.Ordinal)
            ? statement["member ".Length..]
            : $"void M() {{ {statement} }}";

        var source = """
            using System; using System.Buffers; using System.Collections.Generic; using System.Linq;
            sealed class Grid2D { public int CountX { get; } public int CountY { get; } public int NodeCount => CountX * CountY; }
            sealed class Thing { public double Amplitude { get; } }
            readonly record struct Vec3(double X, double Y, double Z);
            static class GridArrays { public static T[] New<T>(long n) => null!; public static void Resize<T>(ref T[] a, int n) { } }
            sealed class Own { public Own Clone() => this; }
            class C<T>
            {
                // Unassigned, all of them: where each came from is exactly what is not known.
                #pragma warning disable CS0649, CS8618, CS0169
                List<double> _cells;
                int n;
                double[] numbers;
                IEnumerable<double> values;
                Grid2D grid;
                IReadOnlyList<Thing> drives;
                IReadOnlyList<IReadOnlyList<Thing>> perDrive;
                IReadOnlyList<string> names;
                Vec3[] points;
                string text;
                Own own;
                readonly int cells;
                readonly double[] field;
                C(Grid2D g) { cells = g.NodeCount; field = GridArrays.New<double>(g.NodeCount); }
                struct P { public P(int a, int b) { } }
                int Cells() => grid.CountX * grid.CountY;
                static int Passed(int size) => size + 1;
                static double[] Make(int size) => new double[size];
                static double[] Three() => Make(3);
                bool Something() => false;

            """ + body + " }";

        var tree = CSharpSyntaxTree.ParseText(source, GridSizeScan.Options);

        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        return [.. new GridSizeScan([tree]).Refusals(tree)];
    }

    /// <summary>The type and member a node is in, as an excuse names it.</summary>
    private static string Member(SyntaxNode node)
    {
        string? member = null;
        var types = new List<string>();

        foreach (var ancestor in node.Ancestors())
        {
            member ??= ancestor switch
            {
                MethodDeclarationSyntax method => method.Identifier.Text,
                ConstructorDeclarationSyntax => ".ctor",
                PropertyDeclarationSyntax property => property.Identifier.Text,
                IndexerDeclarationSyntax => "this[]",
                FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.Text)),
                OperatorDeclarationSyntax op => "operator " + op.OperatorToken.Text,
                ConversionOperatorDeclarationSyntax => "conversion",
                _ => null,
            };

            if (ancestor is BaseTypeDeclarationSyntax type)
            {
                types.Insert(0, type.Identifier.Text);
            }
        }

        return $"{string.Join(".", types)}.{member ?? "(type)"}";
    }

    private static string Escape(string code) => code.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Collapse(string code) => Whitespace().Replace(code, " ").Trim();

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

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
