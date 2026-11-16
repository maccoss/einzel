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
/// literal - a capacity or a collection to copy - whether written <c>new List&lt;T&gt;(n)</c> or
/// <c>new(n)</c> where the declaration names the type.</item>
/// </list>
/// <para>
/// <b>What is not</b>: an array of literal length, <c>stackalloc</c>, a collection expression
/// without a spread, and growth by <c>Add</c> into a list made with no capacity - a list filled
/// one node at a time is a per-node allocation this cannot see, and is bounded by the memory
/// tests as slack instead. The test reads syntax, not types, so the calls are matched by name:
/// it errs toward catching, and what it catches that is not the size of a grid is listed below
/// with the reason. A listed line that no longer exists fails too, so the list cannot outlive
/// what it excuses.
/// </para>
/// </remarks>
public sealed partial class GridArraySourceTests(ITestOutputHelper output)
{
    /// <summary>
    /// What is caught and is not the size of a grid: the file relative to <c>src/Einzel.Fields</c>,
    /// the expression as written with its whitespace collapsed, and why.
    /// </summary>
    private static readonly (string File, string Code, string Why)[] Allowed =
    [
        ("DrivenSuperposedField.cs", "[.. _elements]", "one entry a field element or a phase"),
        ("DrivenSuperposedField.cs", "[.. elements]", "one entry a field element or a phase"),
        ("FieldAssembly.cs", "[.. element.Phases.Select(build)]", "one entry a field element or a phase"),
        ("FieldAssembly.cs", "[.. elements]", "one entry a field element or a phase"),
        ("FieldAssembly.cs", "new List<IElectrostaticField>(model.Fields.Count)", "one entry a field element or a phase"),
        ("FieldAssembly.cs", "warnings .Where(w => w.Severity == Core.Results.WarningSeverity.ValidityViolation) .ToList()", "one entry a warning"),
        ("Magnetic/UniformMagneticField.cs", "[.. elements]", "one entry a field element or a phase"),
        ("SequencedField.cs", "[.. _states]", "one entry a phase of the sequence"),
        ("Solved/BasisFieldSet.cs", "new ScalarField2D[electrodes.Count]", "one entry an electrode"),
        ("Solved/BasisFieldSet.cs", "new SolveReport[electrodes.Count]", "one entry an electrode"),
        ("Solved/DriveChannels.cs", "[ .. existing.Harmonics, new WeightTerm(supply.Drive, scale * supply.Amplitude, supply.Phase), ]", "weight terms, a few a basis channel"),
        ("Solved/DriveChannels.cs", "[.. harmonics[index], .. group.Harmonics]", "weight terms, a few a basis channel"),
        ("Solved/DriveChannels.cs", "new Dictionary<string, double>(coefficients.Count, StringComparer.Ordinal)", "one entry a supply"),
        ("Solved/DriveChannels.cs", "new HashSet<string>(StringComparer.Ordinal)", "its argument is a comparer, not a size"),
        ("Solved/DriveChannels.cs", "new List<IReadOnlyList<WeightTerm>>( Enumerable.Range(0, channels.Count).Select(_ => (IReadOnlyList<WeightTerm>)[]))", "one entry a basis channel"),
        ("Solved/DriveChannels.cs", "new List<double>(new double[channels.Count])", "one entry a basis channel"),
        ("Solved/DriveChannels.cs", "new double[channels.Count]", "one entry a basis channel"),
        ("Solved/DrivenSolvedField.cs", ".Select(h => h.ToArray()).ToArray()", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", ".ToArray()", "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "[.. boundaries]", "one entry a stage boundary"),
        ("Solved/DrivenSolvedField.cs", "[.. channels]", "one entry a basis channel"),
        ("Solved/DrivenSolvedField.cs", "[.. direct]", "one entry a basis channel or a drive"),
        ("Solved/DrivenSolvedField.cs", "[.. frequenciesHz]", "one entry a basis channel or a drive"),
        ("Solved/DrivenSolvedField.cs", "[.. harmonics.Select(h => h.ToArray())]", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "[.. stageDirect.Select(d => d.ToArray())]", "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "[.. stageEndDirect.Select(d => d?.ToArray())]", "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "[.. stageEndHarmonics.Select(stage => stage?.Select(h => h.ToArray()).ToArray())]", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "[.. stageHarmonics.Select(stage => stage.Select(h => h.ToArray()).ToArray())]", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "[.. terms.Select((term, k) => term with { Amplitude = term.Amplitude + (ends[channel][k].Amplitude - term.Amplitude) * fraction, })]", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "[.. waveforms]", "one entry a basis channel or a drive"),
        ("Solved/DrivenSolvedField.cs", "d.ToArray()", "one weight a basis channel, a stage at a time"),
        ("Solved/DrivenSolvedField.cs", "h.ToArray()", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "new WeightTerm[][]?[_boundaries.Length]", "weight terms, a few a basis channel"),
        ("Solved/DrivenSolvedField.cs", "new WeightTerm[_channels.Length][]", "one entry a basis channel"),
        ("Solved/DrivenSolvedField.cs", "new double[]?[_boundaries.Length]", "one entry a stage boundary"),
        ("Solved/DrivenSolvedField.cs", "stage.Select(h => h.ToArray()).ToArray()", "weight terms, a few a basis channel"),
        ("Solved/GalerkinHierarchy3D.cs", "[.. levels]", "one entry a coarse level"),
        ("Solved/GeometryBuilder.cs", "[ .. DriveChannels .DecomposeStates(States(states), quadrature) .Select(group => (Func<CompiledElectrode, double>?)Weighted(group.Pattern)), ]", "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "[.. electrode.Taps.Select( t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]", "one entry a drive tap of an electrode"),
        ("Solved/GeometryBuilder.cs", "[.. keys.Select(k => Pick(end, k))]", "one entry a drive phase"),
        ("Solved/GeometryBuilder.cs", "[.. keys.Select(k => Pick(start, k))]", "one entry a drive phase"),
        ("Solved/GeometryBuilder.cs", "[.. solve.Electrodes.Select(e => Held(e, potentialOf))]", "one entry an electrode"),
        ("Solved/GeometryBuilder.cs", "[.. stage.Electrodes.Select(Excited)]", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "[.. stage.EndElectrodes.Select(Excited)]", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "[.. state.Select(Excited)]", "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder.cs", "[.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])]", "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder.cs", "electrodes .Where(e => e.Shape == ElectrodeShape.EdgeProfile) .Select(e => e.Edge) .ToHashSet()", "one entry an edge-profiled electrode"),
        ("Solved/GeometryBuilder.cs", "new List<ChannelSolve>(patterns.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "new List<IElectrostaticField>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "new List<IReadOnlyList<CompiledElectrode>>(2 * solve.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(solve.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(solve.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "new List<IReadOnlyList<WeightTerm>>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "new List<IReadOnlyList<double>>(solve.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "new List<IReadOnlyList<double>?>(solve.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder.cs", "new List<RfWaveform>(drives.Count)", "one entry a drive"),
        ("Solved/GeometryBuilder.cs", "new List<bool>(drives.Count)", "one entry a drive"),
        ("Solved/GeometryBuilder.cs", "new List<double>(drives.Count)", "one entry a drive"),
        ("Solved/GeometryBuilder.cs", "new List<double>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder.cs", "new List<double>(solve.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "[.. electrode.Taps.Select( t => new DriveTap(t.Drive, t.Amplitude, t.Phase))]", "one entry a drive tap of an electrode"),
        ("Solved/GeometryBuilder3D.cs", "[.. geometry.Electrodes.Select(Weight)]", "one entry an electrode"),
        ("Solved/GeometryBuilder3D.cs", "[.. geometry.Electrodes.Select(e => e.Potential)]", "one entry an electrode"),
        ("Solved/GeometryBuilder3D.cs", "[.. list]", "the volume cull's candidate lists, one a block of 512 nodes: under 0.2 bytes a node, dropped with the mask builder before the solve"),
        ("Solved/GeometryBuilder3D.cs", "[.. solves.Select(channel => channel with { Report = channel.Report with { Warnings = warnings } })]", "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "[.. stage.Electrodes.Select(Excited)]", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "[.. stage.EndElectrodes.Select(Excited)]", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "[.. state.Select(Excited)]", "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder3D.cs", "[.. states.Select(state => (IReadOnlyList<Excitation>)[.. state.Select(Excited)])]", "one entry an electrode, a state of the instrument at a time"),
        ("Solved/GeometryBuilder3D.cs", "new List<Analytic.RfWaveform>(drives.Count)", "one entry a drive"),
        ("Solved/GeometryBuilder3D.cs", "new List<ChannelSolve>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "new List<IElectrostaticField>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>>(geometry.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "new List<IReadOnlyList<IReadOnlyList<WeightTerm>>?>(geometry.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "new List<IReadOnlyList<WeightTerm>>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "new List<IReadOnlyList<double>>(geometry.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "new List<IReadOnlyList<double>?>(geometry.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "new List<bool>(drives.Count)", "one entry a drive"),
        ("Solved/GeometryBuilder3D.cs", "new List<double>(drives.Count)", "one entry a drive"),
        ("Solved/GeometryBuilder3D.cs", "new List<double>(geometry.Stages.Count)", "one entry a stage, or an electrode of one"),
        ("Solved/GeometryBuilder3D.cs", "new List<double>(groups.Count)", "one entry a basis channel"),
        ("Solved/GeometryBuilder3D.cs", "new List<int>?[blocksX * blocksY * blocksZ]", "the volume cull's candidate lists, one a block of 512 nodes: under 0.2 bytes a node, dropped with the mask builder before the solve"),
        ("Solved/GeometryBuilder3D.cs", "new NodeBox[electrodes.Count]", "one entry an electrode"),
        ("Solved/GeometryBuilder3D.cs", "new int[lists.Length][]", "the volume cull's candidate lists, one a block of 512 nodes: under 0.2 bytes a node, dropped with the mask builder before the solve"),
        ("Solved/PoissonSolver2D.cs", ".Clone()", "a field's own Clone, which allocates through its constructor and so through GridArrays"),
        ("Solved/PoissonSolver3D.cs", ".Clone()", "a field's own Clone, which allocates through its constructor and so through GridArrays"),
        ("Solved/SolvedField2D.cs", "[.. conductors.Where(e => e.Shape != Core.Model.ElectrodeShape.EdgeProfile)]", "one entry an electrode"),
        ("Solved/SolvedField3D.cs", "[.. conductors]", "one entry an electrode"),
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

        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var caught = new List<string>();
        var excused = new HashSet<int>();
        var through = 0;

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

            Assert.True(
                errors.Length == 0,
                $"{relative} does not parse, so it cannot be checked: {string.Join("; ", errors.Select(e => e.ToString()))}");

            if (relative == "Solved/GridArrays.cs")
            {
                continue;
            }

            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (IsGridArrays(node))
                {
                    through++;
                    continue;
                }

                if (!Allocates(node))
                {
                    continue;
                }

                var code = Collapse(node.ToString());
                var excuse = Array.FindIndex(Allowed, a => a.File == relative && a.Code == code);

                if (excuse >= 0)
                {
                    excused.Add(excuse);
                }
                else
                {
                    var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    caught.Add($"(\"{relative}\", \"{code}\", \"\"), // line {line}");
                }
            }
        }

        output.WriteLine($"{files.Length} files, {through} allocations through GridArrays, {excused.Count} excused");

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
            + string.Join("; ", stale.Select(index => $"{Allowed[index].File}: {Allowed[index].Code}")));
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
    public void WhatHasAWrittenSizeIsNotCaught(string statement)
    {
        Assert.True(Count(statement) == 0, $"caught: {statement}");
    }

    private static int Count(string statement)
    {
        var tree = CSharpSyntaxTree.ParseText($"class C {{ void M() {{ {statement} }} }}");

        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        return tree.GetRoot().DescendantNodes().Count(node => !IsGridArrays(node) && Allocates(node));
    }

    /// <summary>Whether a node allocates storage whose size is decided at run time.</summary>
    private static bool Allocates(SyntaxNode node) => node switch
    {
        ArrayCreationExpressionSyntax array => array.Type.RankSpecifiers
            .SelectMany(rank => rank.Sizes)
            .Any(size => size is not OmittedArraySizeExpressionSyntax and not LiteralExpressionSyntax),

        CollectionExpressionSyntax collection => collection.Elements.Any(e => e is SpreadElementSyntax),

        InvocationExpressionSyntax call => CopyingCalls.Contains(NameOf(call.Expression) ?? string.Empty),

        ObjectCreationExpressionSyntax creation =>
            Collections.Contains(TypeName(creation.Type) ?? string.Empty) && SizedAtRunTime(creation.ArgumentList),

        ImplicitObjectCreationExpressionSyntax implicitCreation =>
            Collections.Contains(DeclaredTypeName(implicitCreation) ?? string.Empty)
            && SizedAtRunTime(implicitCreation.ArgumentList),

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

    /// <summary>The type a target-typed <c>new(...)</c> is given by its declaration, where one names it.</summary>
    private static string? DeclaredTypeName(ImplicitObjectCreationExpressionSyntax creation) => creation.Parent switch
    {
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } =>
            TypeName(declaration.Type),
        EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => TypeName(property.Type),
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
