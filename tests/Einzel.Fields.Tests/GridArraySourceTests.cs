using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// Nothing in <c>Einzel.Fields</c> allocates storage whose size is not written in the source,
/// except through <c>GridArrays</c>.
/// </summary>
/// <remarks>
/// <para>
/// The memory tests read what a build holds at each allocation <c>GridArrays</c> makes, and
/// bound what is allocated any other way. This stops such an allocation being written: it
/// parses every source file under <c>src/Einzel.Fields</c> with the C# compiler's own parser
/// and walks the syntax trees, so a form split across lines, a generic or struct element type,
/// a subdirectory or a file at the assembly's root is read like any other. Every file must
/// parse without an error, so a syntax this parser cannot read fails here rather than being
/// skipped.
/// </para>
/// <para>
/// <b>What is caught</b>, outside <c>GridArrays.cs</c>:
/// </para>
/// <list type="bullet">
/// <item>an array creation, of any element type, with a dimension that is not a numeric literal;</item>
/// <item>a collection expression with a spread (<c>[.. items]</c>), whose length is another collection's;</item>
/// <item>a call named <c>ToArray</c>, <c>ToList</c>, <c>ToHashSet</c>, <c>ToDictionary</c>, <c>Clone</c>,
/// <c>Resize</c>, <c>AllocateArray</c>, <c>AllocateUninitializedArray</c>, <c>Rent</c> or <c>CreateInstance</c>;</item>
/// <item>a <c>List</c>, <c>HashSet</c>, <c>Dictionary</c>, <c>Queue</c>, <c>Stack</c>, <c>SortedSet</c>,
/// <c>SortedDictionary</c> or <c>LinkedList</c> constructed with an argument that is not a numeric
/// literal - a capacity or a collection to copy - whether written <c>new List&lt;T&gt;(n)</c> or as
/// a target-typed <c>new(n)</c> anywhere: the type a target-typed <c>new</c> creates is asked of the
/// compiler's semantic model, over a compilation of all the sources, and one whose type cannot be
/// resolved is caught if it has a non-literal argument, rather than passed.</item>
/// </list>
/// <para>
/// <b>What is not</b>: an array of literal length, <c>stackalloc</c>, a collection expression
/// without a spread, and growth by <c>Add</c> into a list made with no capacity - a list filled
/// one node at a time is a per-node allocation this cannot see, and is bounded by the memory
/// tests as slack instead. Calls are matched by name, not by what they bind to:
/// it errs toward catching, and what it catches that is not the size of a grid is listed below
/// with the reason. A listed line that no longer exists fails too, so the list cannot outlive
/// what it excuses.
/// </para>
/// </remarks>
public sealed partial class GridArraySourceTests(ITestOutputHelper output)
{
    /// <summary>
    /// What is caught and is not the size of a grid: the file relative to <c>src/Einzel.Fields</c>,
    /// the member it is in (its type, then the method, constructor, property or field), the
    /// expression as written with its whitespace collapsed, how many times that expression occurs
    /// in that member, and why.
    /// </summary>
    /// <remarks>
    /// Anchored to the member and counted, so that an excuse covers what it was written for and
    /// nothing else: the same expression written a second time in the same member, or moved to
    /// another, is caught again.
    /// </remarks>
    private static readonly (string File, string Member, string Code, int Count, string Why)[] Allowed =
    [
        ("DrivenSuperposedField.cs", "DrivenSuperposedField..ctor", "[.. elements]", 1, "one entry a field element or a phase"),
        ("DrivenSuperposedField.cs", "DrivenSuperposedField.AtOperatingPoint", "[.. _elements]", 1, "one entry a field element or a phase"),
        ("FieldAssembly.cs", "FieldAssembly.Build", "warnings .Where(w => w.Severity == Core.Results.WarningSeverity.ValidityViolation) .ToList()", 1, "one entry a warning"),
        ("FieldAssembly.cs", "FieldAssembly.BuildReported", "new List<IElectrostaticField>(model.Fields.Count)", 1, "one entry a field element or a phase"),
        ("FieldAssembly.cs", "FieldAssembly.Sequenced", "[.. element.Phases.Select(build)]", 1, "one entry a field element or a phase"),
        ("FieldAssembly.cs", "SuperposedField..ctor", "[.. elements]", 1, "one entry a field element or a phase"),
        ("Magnetic/UniformMagneticField.cs", "SuperposedMagneticField..ctor", "[.. elements]", 1, "one entry a field element or a phase"),
        ("SequencedField.cs", "SequencedField.AtOperatingPoint", "[.. _states]", 1, "one entry a phase of the sequence"),
        ("Solved/BasisFieldSet.cs", "BasisFieldSet.Solve", "new ScalarField2D[electrodes.Count]", 1, "one entry an electrode"),
        ("Solved/BasisFieldSet.cs", "BasisFieldSet.Solve", "new SolveReport[electrodes.Count]", 1, "one entry an electrode"),
        ("Solved/DriveChannels.cs", "DriveChannels.Decompose", "[ .. existing.Harmonics, new WeightTerm(supply.Drive, scale * supply.Amplitude, supply.Phase), ]", 1, "weight terms, a few a basis channel"),
        ("Solved/DriveChannels.cs", "DriveChannels.Decompose", "new HashSet<string>(StringComparer.Ordinal)", 1, "its argument is a comparer, not a size"),
        ("Solved/DriveChannels.cs", "DriveChannels.Normalise", "new Dictionary<string, double>(coefficients.Count, StringComparer.Ordinal)", 1, "one entry a supply"),
        ("Solved/DriveChannels.cs", "DriveChannels.Weigh", "[.. harmonics[index], .. group.Harmonics]", 1, "weight terms, a few a basis channel"),
        ("Solved/DriveChannels.cs", "DriveChannels.Weigh", "new List<IReadOnlyList<WeightTerm>>( Enumerable.Range(0, channels.Count).Select(_ => (IReadOnlyList<WeightTerm>)[]))", 1, "one entry a basis channel"),
        ("Solved/DriveChannels.cs", "DriveChannels.Weigh", "new List<double>(new double[channels.Count])", 1, "one entry a basis channel"),
        ("Solved/DriveChannels.cs", "DriveChannels.Weigh", "new double[channels.Count]", 1, "one entry a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", ".Select(h => h.ToArray()).ToArray()", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", ".ToArray()", 1, "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. boundaries]", 1, "one entry a stage boundary"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. channels]", 1, "one entry a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. direct]", 1, "one entry a basis channel or a drive"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. frequenciesHz]", 1, "one entry a basis channel or a drive"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. harmonics.Select(h => h.ToArray())]", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. stageDirect.Select(d => d.ToArray())]", 1, "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. stageEndDirect.Select(d => d?.ToArray())]", 1, "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. stageEndHarmonics.Select(stage => stage?.Select(h => h.ToArray()).ToArray())]", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. stageHarmonics.Select(stage => stage.Select(h => h.ToArray()).ToArray())]", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "[.. waveforms]", 1, "one entry a basis channel or a drive"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "d.ToArray()", 1, "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "h.ToArray()", 3, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "new WeightTerm[][]?[_boundaries.Length]", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "new double[]?[_boundaries.Length]", 1, "one entry a stage boundary"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField..ctor", "stage.Select(h => h.ToArray()).ToArray()", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField.RfTerms", "[.. terms.Select((term, k) => term with { Amplitude = term.Amplitude + (ends[channel][k].Amplitude - term.Amplitude) * fraction, })]", 1, "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "DrivenSolvedField.RfTermsAt", "new WeightTerm[_channels.Length][]", 1, "one entry a basis channel"),
        ("Solved/GalerkinHierarchy3D.cs", "GalerkinHierarchy3D.Build", "[.. levels]", 1, "one entry a coarse level"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Align", "[.. keys.Select(k => Pick(end, k))]", 1, "one entry a drive phase"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Align", "[.. keys.Select(k => Pick(start, k))]", 1, "one entry a drive phase"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "[.. stage.Electrodes.Select(Excited)]", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "[.. stage.EndElectrodes.Select(Excited)]", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<IElectrostaticField>(groups.Count)", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(solve.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(solve.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<IReadOnlyList<WeightTerm>>(groups.Count)", 3, "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<IReadOnlyList<double>>(solve.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<IReadOnlyList<double>?>(solve.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<double>(groups.Count)", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.BuildDriven", "new List<double>(solve.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Clocks", "new List<RfWaveform>(drives.Count)", 1, "one entry a drive"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Clocks", "new List<bool>(drives.Count)", 1, "one entry a drive"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Clocks", "new List<double>(drives.Count)", 1, "one entry a drive"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Excited", "[.. electrode.Taps.Select( t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]", 1, "one entry a drive tap of an electrode"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.NodesOnGroundedEdges", "electrodes .Where(e => e.Shape == ElectrodeShape.EdgeProfile) .Select(e => e.Edge) .ToHashSet()", 1, "one entry an edge-profiled electrode"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.Patterns", "[ .. DriveChannels .DecomposeStates(States(states), quadrature) .Select(group => (Func<CompiledElectrode, double>?)Weighted(group.Pattern)), ]", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.SolveChannels", "[.. solve.Electrodes.Select(e => Held(e, potentialOf))]", 1, "one entry an electrode"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.SolveChannels", "new List<ChannelSolve>(patterns.Count)", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.StageStates", "new List<IReadOnlyList<CompiledElectrode>>(2 * solve.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.States", "[.. state.Select(Excited)]", 1, "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder.cs", "GeometryBuilder.States", "[.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])]", 1, "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "[.. stage.Electrodes.Select(Excited)]", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "[.. stage.EndElectrodes.Select(Excited)]", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<IElectrostaticField>(groups.Count)", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(geometry.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(geometry.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<IReadOnlyList<WeightTerm>>(groups.Count)", 3, "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<IReadOnlyList<double>>(geometry.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<IReadOnlyList<double>?>(geometry.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<double>(geometry.Stages.Count)", 1, "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.BuildField", "new List<double>(groups.Count)", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Candidates", "[.. list]", 1, "the volume cull's candidate lists, one a block of 512 nodes: under 0.2 bytes a node, dropped with the mask builder before the solve"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Candidates", "new List<int>?[blocksX * blocksY * blocksZ]", 1, "the volume cull's candidate lists, one a block of 512 nodes: under 0.2 bytes a node, dropped with the mask builder before the solve"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Candidates", "new int[lists.Length][]", 1, "the volume cull's candidate lists, one a block of 512 nodes: under 0.2 bytes a node, dropped with the mask builder before the solve"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Clocks", "new List<Analytic.RfWaveform>(drives.Count)", 1, "one entry a drive"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Clocks", "new List<bool>(drives.Count)", 1, "one entry a drive"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Clocks", "new List<double>(drives.Count)", 1, "one entry a drive"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Excited", "[.. electrode.Taps.Select( t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]", 1, "one entry a drive tap of an electrode"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Groups", "[.. state.Select(Excited)]", 1, "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Groups", "[.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])]", 1, "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.Reach", "new NodeBox[electrodes.Count]", 1, "one entry an electrode"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.SolveChannels", "[.. geometry.Electrodes.Select(e => e.Potential)]", 1, "one entry an electrode"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.SolveChannels", "[.. solves.Select(channel => channel with { Report = channel.Report with { Warnings = warnings } })]", 1, "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.SolveGroup", "[.. geometry.Electrodes.Select(Weight)]", 1, "one entry an electrode"),
        ("Solved/GeometryBuilder3D.cs", "GeometryBuilder3D.SolveGroups", "new List<ChannelSolve>(groups.Count)", 1, "one entry a basis channel"),
        ("Solved/PoissonSolver2D.cs", "PoissonSolver2D.Solve", ".Clone()", 1, "a field's own Clone, which allocates through its constructor and so through GridArrays"),
        ("Solved/PoissonSolver3D.cs", "PoissonSolver3D.Solve", ".Clone()", 1, "a field's own Clone, which allocates through its constructor and so through GridArrays"),
        ("Solved/SolvedField2D.cs", "SolvedField2D..ctor", "[.. conductors.Where(e => e.Shape != Core.Model.ElectrodeShape.EdgeProfile)]", 1, "one entry an electrode"),
        ("Solved/SolvedField3D.cs", "SolvedField3D..ctor", "[.. conductors]", 1, "one entry an electrode"),
    ];

    private static readonly HashSet<string> CopyingCalls =
    [
        "ToArray", "ToList", "ToHashSet", "ToDictionary", "Clone", "Resize",
        "AllocateArray", "AllocateUninitializedArray", "Rent", "CreateInstance",
    ];

    private static readonly HashSet<string> Collections =
    [
        "List", "HashSet", "Dictionary", "Queue", "Stack", "SortedSet", "SortedDictionary", "LinkedList",
    ];

    [Fact]
    public void EveryAllocationSizedAtRunTimeInFieldsComesFromGridArrays()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "Einzel.Fields");
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "obj" or "bin"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var trees = new List<(string Relative, SyntaxTree Tree)>();

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), Options, file);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

            Assert.True(
                errors.Length == 0,
                $"{relative} does not parse, so it cannot be checked: {string.Join("; ", errors.Select(e => e.ToString()))}");

            trees.Add((relative, tree));
        }

        var compilation = Compile(trees.Select(t => t.Tree));
        var found = new Dictionary<(string File, string Member, string Code), (int Count, int Line)>();
        var through = 0;
        var targetTyped = 0;

        foreach (var (relative, tree) in trees)
        {
            if (relative == "Solved/GridArrays.cs")
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);

            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (IsGridArrays(node))
                {
                    through++;
                    continue;
                }

                targetTyped += node is ImplicitObjectCreationExpressionSyntax ? 1 : 0;

                if (!Allocates(node, model))
                {
                    continue;
                }

                var key = (relative, Member(node), Collapse(node.ToString()));
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                found[key] = found.TryGetValue(key, out var seen) ? (seen.Count + 1, seen.Line) : (1, line);
            }
        }

        var caught = new List<string>();
        var excused = new HashSet<int>();

        foreach (var ((file, member, code), (count, line)) in found.OrderBy(f => f.Key))
        {
            var excuse = Array.FindIndex(Allowed, a => a.File == file && a.Member == member && a.Code == code);

            if (excuse >= 0 && Allowed[excuse].Count == count)
            {
                excused.Add(excuse);
                continue;
            }

            var listed = excuse >= 0 ? $" // listed {Allowed[excuse].Count} times, found {count}" : "";

            caught.Add($"(\"{file}\", \"{member}\", \"{Escape(code)}\", {count}, \"\"), // line {line}{listed}");

            if (excuse >= 0)
            {
                excused.Add(excuse);
            }
        }

        output.WriteLine(
            $"{files.Length} files, {through} allocations through GridArrays, {targetTyped} target-typed news typed by "
            + $"the semantic model, {excused.Count} listed expressions");

        // A scan of the wrong directory, or of nothing, finds nothing wrong.
        Assert.True(files.Length >= 40, $"only {files.Length} files were read under {root}");
        Assert.True(through >= 20, $"only {through} allocations go through GridArrays");

        Assert.True(
            caught.Count == 0,
            "storage sized at run time is allocated in Einzel.Fields without GridArrays, so the memory tests do not read "
            + "the live set at it. Allocate an array with GridArrays.New or grow it with GridArrays.Resize; if it is not "
            + "the size of a grid, list it in this test with the reason." + Environment.NewLine
            + string.Join(Environment.NewLine, caught));

        var stale = Enumerable.Range(0, Allowed.Length).Where(index => !excused.Contains(index)).ToArray();

        Assert.True(
            stale.Length == 0,
            "a listed exception no longer matches anything: "
            + string.Join("; ", stale.Select(index => $"{Allowed[index].File} {Allowed[index].Member}: {Allowed[index].Code}")));
    }

    /// <summary>
    /// An expression listed for one member is caught when it is written a second time there, or
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

        var tree = CSharpSyntaxTree.ParseText(source, Options);
        var model = Compile([tree]).GetSemanticModel(tree);

        var found = tree.GetRoot().DescendantNodes()
            .Where(node => Allocates(node, model))
            .GroupBy(node => (Member(node), Collapse(node.ToString())))
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(1, found[("C.Once", "new List<double>(n)")]);
        Assert.Equal(2, found[("C.Twice", "new List<double>(n)")]);
    }

    /// <summary>Each form the test refuses, written as it would be, is caught.</summary>
    [Theory]
    [InlineData("var a = new double[n];")]
    [InlineData("var a = new T[n];")]
    [InlineData("var a = new Vec3[grid.NodeCount];")]
    [InlineData("var a = new double[n][];")]
    [InlineData("var a = new double[\n    grid.NodeCount];")]
    [InlineData("double[] a = [.. values];")]
    [InlineData("var a = values.ToArray();")]
    [InlineData("var a = field.Clone();")]
    [InlineData("Array.Resize(ref a, n);")]
    [InlineData("var a = GC.AllocateUninitializedArray<double>(n);")]
    [InlineData("var a = ArrayPool<double>.Shared.Rent(n);")]
    [InlineData("var a = new List<double>(n);")]
    [InlineData("List<double> a = new(n);")]
    [InlineData("var a = new HashSet<int>(nodes);")]
    [InlineData("_cells = new(n);")]
    [InlineData("member List<double> M() { return new(n); }")]
    [InlineData("member List<double> M() => new(n);")]
    [InlineData("member void M() { Use(new(n)); } void Use(List<double> cells) { }")]
    [InlineData("Unknown u = new(n);")]
    public void EachRefusedFormIsCaught(string statement)
    {
        Assert.True(Count(statement) == 1, $"not caught: {statement}");
    }

    /// <summary>What is not the size of anything at run time is not caught.</summary>
    [Theory]
    [InlineData("var a = new double[4];")]
    [InlineData("var a = new[] { 1.0, 2.0 };")]
    [InlineData("var a = new double[] { 1.0, 2.0 };")]
    [InlineData("double[] a = [1.0, 2.0, 3.0];")]
    [InlineData("Span<double> a = stackalloc double[n];")]
    [InlineData("var a = new List<double>();")]
    [InlineData("var a = new List<double>(4);")]
    [InlineData("var a = GridArrays.New<double>(n);")]
    [InlineData("GridArrays.Resize(ref a, n);")]
    [InlineData("_cells = new();")]
    [InlineData("P p = new(n, n);")]
    [InlineData("member P M() => new(n, n);")]
    public void WhatHasAWrittenSizeIsNotCaught(string statement)
    {
        Assert.True(Count(statement) == 0, $"caught: {statement}");
    }

    private static int Count(string statement)
    {
        // Members as well as statements: a target-typed new in an assignment to a field, a return
        // or an argument needs somewhere to be declared.
        var body = statement.StartsWith("member ", StringComparison.Ordinal)
            ? statement["member ".Length..]
            : $"void M() {{ {statement} }}";

        var source = "using System; using System.Buffers; using System.Collections.Generic; using System.Linq; "
            + "class C<T> { List<double> _cells = null!; int n; struct P { public P(int a, int b) { } } "
            + body + " }";

        var tree = CSharpSyntaxTree.ParseText(source, Options);

        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var model = Compile([tree]).GetSemanticModel(tree);

        return tree.GetRoot().DescendantNodes().Count(node => !IsGridArrays(node) && Allocates(node, model));
    }

    private static readonly CSharpParseOptions Options = new(LanguageVersion.Latest);

    /// <summary>
    /// A compilation of the given sources against the running framework and Einzel.Core, with the
    /// SDK's implicit usings, for the semantic model to say what a target-typed <c>new</c> creates.
    /// </summary>
    private static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees)
    {
        var usings = CSharpSyntaxTree.ParseText(
            "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; "
            + "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
            Options);

        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        string[] references = [.. platform, typeof(Core.Model.CompiledSolvedField).Assembly.Location];

        return CSharpCompilation.Create(
            "Fields",
            [usings, .. trees],
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
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

    /// <summary>Whether a node allocates storage whose size is decided at run time.</summary>
    private static bool Allocates(SyntaxNode node, SemanticModel model) => node switch
    {
        ArrayCreationExpressionSyntax array => array.Type.RankSpecifiers
            .SelectMany(rank => rank.Sizes)
            .Any(size => size is not OmittedArraySizeExpressionSyntax and not LiteralExpressionSyntax),

        CollectionExpressionSyntax collection => collection.Elements.Any(e => e is SpreadElementSyntax),

        InvocationExpressionSyntax call => CopyingCalls.Contains(NameOf(call.Expression) ?? string.Empty),

        ObjectCreationExpressionSyntax creation =>
            Collections.Contains(TypeName(creation.Type) ?? string.Empty) && SizedAtRunTime(creation.ArgumentList),

        ImplicitObjectCreationExpressionSyntax implicitCreation =>
            SizedAtRunTime(implicitCreation.ArgumentList)
            && model.GetTypeInfo(implicitCreation).Type is var created
            && (created is null or { TypeKind: TypeKind.Error } || Collections.Contains(created.Name)),

        _ => false,
    };

    /// <summary>A call to <c>GridArrays</c>, or the invocation that one is part of.</summary>
    private static bool IsGridArrays(SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "GridArrays" } } };

    private static bool SizedAtRunTime(ArgumentListSyntax? arguments) =>
        arguments is not null && arguments.Arguments.Any(a => a.Expression is not LiteralExpressionSyntax);

    private static string? NameOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    private static string? TypeName(TypeSyntax type) => type switch
    {
        SimpleNameSyntax name => name.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        _ => null,
    };

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
