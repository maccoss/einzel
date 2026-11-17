using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Einzel.Fields.Tests;

/// <summary>What the scan can show about how large some storage is.</summary>
internal enum Extent
{
    /// <summary>Shown to be a count of things written in the source or declared in a model.</summary>
    Small,

    /// <summary>Not shown to be anything.</summary>
    Unknown,

    /// <summary>Shown to derive from the dimensions of a grid.</summary>
    Grid,
}

/// <summary>An extent, and the expression that decided it.</summary>
/// <param name="Extent">What was shown.</param>
/// <param name="Why">The expression or symbol the answer rests on, for the message.</param>
internal readonly record struct Shown(Extent Extent, string Why)
{
    /// <summary>The worse of two: a grid outranks not knowing, which outranks small.</summary>
    internal Shown Or(Shown other) => other.Extent > Extent ? other : this;
}

/// <summary>An allocation the scan refuses.</summary>
/// <param name="Node">The expression that allocates.</param>
/// <param name="Shown">Why it is refused.</param>
internal sealed record Refused(SyntaxNode Node, Shown Shown);

/// <summary>
/// Follows the size of every allocation in a set of sources back to where it comes from, and
/// says which are the size of a grid, or cannot be shown not to be.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> Storage sized at run time is one of two kinds, by what it holds.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Storage for bare numbers</b> - <c>double</c>, <c>float</c>, the integers, <c>bool</c>,
/// <c>char</c>, an enum, a tuple, <c>string</c>, a type parameter - is what a field over a grid
/// is made of. It is refused unless its size is <b>shown small</b>.
/// </item>
/// <item>
/// <b>Storage for things</b> - references to objects, and values of a struct this codebase
/// declares (a vector, a weight term, a phase state) - holds one entry a thing, and things are
/// electrodes, drives, stages, channels, levels, ions: declared or counted, not nodes. It is
/// refused only if its size is <b>shown to be a grid's</b>.
/// </item>
/// </list>
/// <para>
/// <b>A size is a grid's</b> when, followed back through the code, it reaches a member of
/// <c>Grid2D</c> or <c>Grid3D</c>, or the length of an array <c>GridArrays</c> allocated.
/// <b>A size is small</b> when every path back ends in a constant, in the length of a collection
/// of things or of a string, or in the processor count. Anything else is not shown.
/// </para>
/// <para>
/// <b>Followed back</b> means: through arithmetic and <c>Math</c>; through a local to everything
/// assigned to it in its member; through a parameter to the argument at every call in the
/// sources compiled, a method passed as a delegate or reached through an interface or an
/// override being not shown; through a field or property to its initializer, to everything
/// assigned to it anywhere, and to its getter; through a call to what the method returns;
/// through <c>Length</c> and <c>Count</c> to the collection, and through a LINQ call that keeps
/// or shrinks a sequence to the sequence it was called on. A collection made empty or with a
/// capacity and filled by <c>Add</c> is as large as the loops its <c>Add</c>s are in.
/// </para>
/// <para>
/// <b>What this cannot show, and what covers it.</b>
/// </para>
/// <list type="bullet">
/// <item>Per-node storage of <i>things</i> whose size does not trace to a grid - a list of one
/// object a node filled from a file, say. Each such object is allocated outside
/// <c>GridArrays</c>, which the memory tests bound and fail by name.</item>
/// <item>A collection given a small capacity and then grown past it by something other than
/// <c>Add</c> in a loop the scan can read - through a reference handed to other code.</item>
/// <item>A size that reaches the scanned assemblies from one not compiled here: it is not
/// shown, and so refused, which errs toward catching.</item>
/// <item>Recursion and cycles: a size that depends on itself is taken as small on the way
/// round, so a counter grown by its own value is judged by where it started.</item>
/// <item><c>stackalloc</c>, which is not the heap, and anything allocated by code this does
/// not parse - the runtime, and assemblies outside the scan.</item>
/// </list>
/// </remarks>
internal sealed class GridSizeScan
{
    private static readonly HashSet<string> GridTypes = ["Grid2D", "Grid3D"];

    private static readonly HashSet<string> CopyingCalls =
    [
        "ToArray", "ToList", "ToHashSet", "ToDictionary", "Clone", "Resize",
        "AllocateArray", "AllocateUninitializedArray", "Rent", "CreateInstance",
    ];

    private static readonly HashSet<string> Collections =
    [
        "List", "HashSet", "Dictionary", "Queue", "Stack", "SortedSet", "SortedDictionary", "LinkedList",
    ];

    // Calls that return a sequence no longer than the one they are called on.
    private static readonly HashSet<string> Keeps =
    [
        "Select", "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending",
        "Distinct", "DistinctBy", "Reverse", "Cast", "OfType", "Skip", "SkipWhile", "SkipLast", "Take", "TakeWhile",
        "TakeLast", "AsEnumerable", "ToArray", "ToList", "ToHashSet", "ToDictionary", "AsReadOnly", "DefaultIfEmpty",
        "GroupBy", "Clone", "AsSpan", "AsMemory", "ToImmutableArray", "ToImmutableList", "Slice",
    ];

    // Calls that return a sequence no longer than theirs and their arguments' together.
    private static readonly HashSet<string> Joins = ["Concat", "Union", "Append", "Prepend", "Zip", "Intersect", "Except"];

    private static readonly HashSet<string> Picks =
        ["First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault", "ElementAt", "ElementAtOrDefault", "Max", "Min"];

    private static readonly HashSet<string> Grows =
        ["Add", "AddRange", "Insert", "InsertRange", "Push", "Enqueue", "TryAdd", "AddLast", "AddFirst", "UnionWith"];

    private const int Deepest = 40;

    private readonly CSharpCompilation _compilation;
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    // Every call of a method or constructor declared in the sources, by its definition.
    private readonly Dictionary<ISymbol, List<IOperation>> _calls = new(SymbolEqualityComparer.Default);

    // Methods used as delegates: their parameters have callers the scan cannot see.
    private readonly HashSet<ISymbol> _asDelegates = new(SymbolEqualityComparer.Default);

    // Everything written to a field, property or local.
    private readonly Dictionary<ISymbol, List<Written>> _writes = new(SymbolEqualityComparer.Default);

    // Every call that grows a collection held in a symbol, and every store into one by index.
    private readonly Dictionary<ISymbol, List<InvocationExpressionSyntax>> _growth = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, List<ExpressionSyntax>> _stores = new(SymbolEqualityComparer.Default);

    // Every call that grows an element of a collection held in a symbol: held[k].Add(x).
    private readonly Dictionary<ISymbol, List<InvocationExpressionSyntax>> _innerGrowth = new(SymbolEqualityComparer.Default);

    private readonly HashSet<(ISymbol, string)> _active = [];
    private readonly Dictionary<(ISymbol, string), Shown> _settled = [];
    private int _cycles;

    /// <summary>Compiles sources and indexes every call and assignment in them.</summary>
    internal GridSizeScan(IEnumerable<SyntaxTree> trees)
    {
        var usings = CSharpSyntaxTree.ParseText(
            "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; "
            + "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
            Options);

        _compilation = CSharpCompilation.Create(
            "Scanned",
            [usings, .. trees],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        foreach (var tree in _compilation.SyntaxTrees)
        {
            Index(tree);
        }
    }

    // The framework, and nothing of Einzel's: its types are the ones in the sources.
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !Path.GetFileName(path).StartsWith("Einzel.", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path)),
    ]);

    /// <summary>The parse options every source is read with.</summary>
    internal static CSharpParseOptions Options { get; } = new(LanguageVersion.Latest);

    /// <summary>The allocations in a tree that are the size of a grid, or not shown not to be.</summary>
    internal IEnumerable<Refused> Refusals(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (Judge(node) is { Extent: not Extent.Small } shown && Matters(node, shown))
            {
                yield return new Refused(node, shown);
            }
        }
    }

    /// <summary>How many allocations sized at run time a tree has, refused or passed, and the refused ones.</summary>
    /// <remarks>One pass: judging an allocation is following its size, which is the cost of the scan.</remarks>
    internal (int Looked, List<Refused> Refused) Judged(SyntaxTree tree)
    {
        var looked = 0;
        var refused = new List<Refused>();

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (Judge(node) is not { } shown)
            {
                continue;
            }

            looked++;

            if (shown.Extent != Extent.Small && Matters(node, shown))
            {
                refused.Add(new Refused(node, shown));
            }
        }

        return (looked, refused);
    }

    /// <summary>How many target-typed <c>new</c> expressions a tree has, for the test to show they were read.</summary>
    internal static int TargetTyped(SyntaxTree tree) =>
        tree.GetRoot().DescendantNodes().Count(node => node is ImplicitObjectCreationExpressionSyntax);

    /// <summary>A call to <c>GridArrays</c>.</summary>
    internal static bool IsGridArrays(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "GridArrays" } },
        };

    private SemanticModel Model(SyntaxNode node)
    {
        if (!_models.TryGetValue(node.SyntaxTree, out var model))
        {
            _models[node.SyntaxTree] = model = _compilation.GetSemanticModel(node.SyntaxTree);
        }

        return model;
    }

    // ---- what allocates, and how large -------------------------------------------------

    /// <summary>How large an allocating expression's storage is, or null if the node does not allocate.</summary>
    private Shown? Judge(SyntaxNode node)
    {
        switch (node)
        {
            case ArrayCreationExpressionSyntax array:
                var sizes = array.Type.RankSpecifiers
                    .SelectMany(rank => rank.Sizes)
                    .Where(size => size is not OmittedArraySizeExpressionSyntax)
                    .ToArray();

                return sizes.Length == 0 ? null : sizes.Select(size => Size(size, 0)).Aggregate((a, b) => a.Or(b));

            case CollectionExpressionSyntax collection when collection.Elements.Any(e => e is SpreadElementSyntax):
                return collection.Elements
                    .OfType<SpreadElementSyntax>()
                    .Select(spread => Length(spread.Expression, 0))
                    .Aggregate((a, b) => a.Or(b));

            case InvocationExpressionSyntax call when !IsGridArrays(call) && CopyingCalls.Contains(NameOf(call.Expression) ?? ""):
                return Copy(call);

            case ObjectCreationExpressionSyntax creation when Collections.Contains(TypeName(creation.Type) ?? ""):
                return Arguments(creation.ArgumentList);

            case ImplicitObjectCreationExpressionSyntax creation:
                var created = Model(creation).GetTypeInfo(creation).Type;

                // One whose type cannot be resolved is judged as a collection, not passed.
                return created is null or { TypeKind: TypeKind.Error } || Collections.Contains(created.Name)
                    ? Arguments(creation.ArgumentList)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>Whether a refusal stands: things are refused only when shown to be a grid's.</summary>
    private bool Matters(SyntaxNode node, Shown shown)
    {
        if (shown.Extent == Extent.Grid)
        {
            return true;
        }

        var type = node switch
        {
            ArrayCreationExpressionSyntax array => Model(array).GetTypeInfo(array.Type.ElementType).Type,
            ExpressionSyntax expression =>
                Element(Model(expression).GetTypeInfo(expression).Type ?? Model(expression).GetTypeInfo(expression).ConvertedType)
                ?? Element(Model(expression).GetTypeInfo(expression).ConvertedType),
            _ => null,
        };

        // An array of arrays is refused by its own size: what it holds is judged where it is made.
        if (node is ArrayCreationExpressionSyntax { Type.RankSpecifiers.Count: > 1 })
        {
            return false;
        }

        return !IsThing(type);
    }

    private Shown Copy(InvocationExpressionSyntax call)
    {
        var model = Model(call);
        var name = NameOf(call.Expression)!;

        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol method)
        {
            return new(Extent.Unknown, $"{name}, which does not resolve");
        }

        // A method of the sources' own - a field's Clone - allocates in its body, which is scanned.
        if (method.DeclaringSyntaxReferences.Length > 0)
        {
            return new(Extent.Small, name);
        }

        return name switch
        {
            "ToArray" or "ToList" or "ToHashSet" or "ToDictionary" or "Clone" => Receiver(call) is { } receiver
                ? Length(receiver, 0)
                : new(Extent.Unknown, call.ToString()),
            _ => Arguments(call.ArgumentList),
        };
    }

    /// <summary>The worst of a constructor's or allocator's arguments: a capacity, or a collection to copy.</summary>
    private Shown Arguments(BaseArgumentListSyntax? arguments)
    {
        var shown = new Shown(Extent.Small, "no size");

        foreach (var argument in arguments?.Arguments ?? [])
        {
            var expression = argument.Expression;
            var type = Model(expression).GetTypeInfo(expression).Type;

            shown = shown.Or(
                argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ? new(Extent.Small, "a reference")
                : type is null or { TypeKind: TypeKind.Error } ? new(Extent.Unknown, expression.ToString())
                : IsNumber(type) ? Size(expression, 0)
                : Element(type) is not null && type.SpecialType != SpecialType.System_String ? Length(expression, 0)
                : new(Extent.Small, "not a size"));
        }

        return shown;
    }

    // ---- how large a number is ------------------------------------------------------------

    /// <summary>What a numeric expression derives from.</summary>
    private Shown Size(ExpressionSyntax expression, int depth)
    {
        if (depth > Deepest)
        {
            return new(Extent.Unknown, expression.ToString());
        }

        var model = Model(expression);

        if (model.GetConstantValue(expression).HasValue)
        {
            return new(Extent.Small, "a constant");
        }

        switch (expression)
        {
            case ParenthesizedExpressionSyntax inner:
                return Size(inner.Expression, depth + 1);
            case CastExpressionSyntax cast:
                return Size(cast.Expression, depth + 1);
            case CheckedExpressionSyntax check:
                return Size(check.Expression, depth + 1);
            case PrefixUnaryExpressionSyntax unary when !unary.IsKind(SyntaxKind.PreIncrementExpression) && !unary.IsKind(SyntaxKind.PreDecrementExpression):
                return Size(unary.Operand, depth + 1);
            case PostfixUnaryExpressionSyntax suppress when suppress.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return Size(suppress.Operand, depth + 1);
            case BinaryExpressionSyntax binary:
                return Size(binary.Left, depth + 1).Or(Size(binary.Right, depth + 1));
            case ConditionalExpressionSyntax either:
                return Size(either.WhenTrue, depth + 1).Or(Size(either.WhenFalse, depth + 1));
            case ConditionalAccessExpressionSyntax access when access.WhenNotNull is MemberBindingExpressionSyntax { Name.Identifier.Text: "Count" or "Length" or "LongLength" }:
                return Length(access.Expression, depth + 1);
            case InvocationExpressionSyntax call:
                return SizeOfCall(call, depth);
            case MemberAccessExpressionSyntax or IdentifierNameSyntax:
                return SizeOfSymbol(expression, depth);
            default:
                return new(Extent.Unknown, expression.ToString());
        }
    }

    private Shown SizeOfCall(InvocationExpressionSyntax call, int depth)
    {
        if (Model(call).GetSymbolInfo(call).Symbol is not IMethodSymbol method)
        {
            return new(Extent.Unknown, call.ToString());
        }

        var type = method.ContainingType?.ToDisplayString() ?? "";

        if (type is "System.Math" or "System.MathF" or "System.Numerics.BitOperations" or "System.Convert"
            || (method.ContainingType?.SpecialType is not (null or SpecialType.None) && method.Name is "Min" or "Max" or "Clamp" or "Abs"))
        {
            return call.ArgumentList.Arguments
                .Select(argument => Size(argument.Expression, depth + 1))
                .Aggregate(new Shown(Extent.Small, "a constant"), (a, b) => a.Or(b));
        }

        if (method.Name is "Count" or "LongCount" && Receiver(call) is { } counted)
        {
            return Length(counted, depth + 1);
        }

        if (method.Name == "SizeOf" && type == "System.Runtime.CompilerServices.Unsafe")
        {
            return new(Extent.Small, "the size of a type");
        }

        return Returns(method, call.ToString(), "size", value => Size(value, depth + 1));
    }

    private Shown SizeOfSymbol(ExpressionSyntax expression, int depth)
    {
        var model = Model(expression);
        var symbol = model.GetSymbolInfo(expression).Symbol;

        if (symbol is null)
        {
            return new(Extent.Unknown, expression.ToString());
        }

        if (OfAGrid(symbol) is { } grid)
        {
            return grid;
        }

        // An element of a tuple is whatever was put in that place.
        if (symbol is IFieldSymbol { ContainingType.IsTupleType: true } part && expression is MemberAccessExpressionSyntax whole)
        {
            return Parts(whole.Expression, Place(part), 0) is { } parts
                ? parts.Select(value => Size(value, depth + 1)).Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b))
                : new(Extent.Unknown, expression.ToString());
        }

        // The count of anything declared outside the sources - an array, a list, a dictionary, a string.
        if (symbol is IPropertySymbol { Name: "Length" or "Count" or "LongLength", DeclaringSyntaxReferences.Length: 0 }
            && expression is MemberAccessExpressionSyntax measured)
        {
            return Length(measured.Expression, depth + 1);
        }

        if (symbol is IPropertySymbol { Name: "ProcessorCount", ContainingType.Name: "Environment" })
        {
            return new(Extent.Small, "the processor count");
        }

        return Follow(symbol, expression, "size", value => Size(value, depth + 1), _ => new(Extent.Unknown, expression.ToString()));
    }

    // ---- how long a collection is ---------------------------------------------------------

    /// <summary>What the length of a collection-valued expression derives from.</summary>
    private Shown Length(ExpressionSyntax expression, int depth)
    {
        if (depth > Deepest)
        {
            return new(Extent.Unknown, expression.ToString());
        }

        var model = Model(expression);
        var info = model.GetTypeInfo(expression);
        var type = info.Type ?? info.ConvertedType;

        if (type?.SpecialType == SpecialType.System_String)
        {
            return new(Extent.Small, "a string");
        }

        if (expression.IsKind(SyntaxKind.NullLiteralExpression) || expression.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            return new(Extent.Small, "nothing");
        }

        // A collection of things has one entry a thing, whatever it was built from - unless it
        // can be followed to a grid, which is asked first.
        var things = type is not null && IsThing(Element(type));

        var shown = LengthOf(expression, depth);

        return things && shown.Extent == Extent.Unknown ? new(Extent.Small, $"{expression}, a collection of things") : shown;
    }

    private Shown LengthOf(ExpressionSyntax expression, int depth)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax inner:
                return Length(inner.Expression, depth + 1);
            case CastExpressionSyntax cast:
                return Length(cast.Expression, depth + 1);
            case PostfixUnaryExpressionSyntax suppress when suppress.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return Length(suppress.Operand, depth + 1);
            case ConditionalExpressionSyntax either:
                return Length(either.WhenTrue, depth + 1).Or(Length(either.WhenFalse, depth + 1));
            case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                return Length(coalesce.Left, depth + 1).Or(Length(coalesce.Right, depth + 1));
            case ConditionalAccessExpressionSyntax access when access.WhenNotNull is InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } && Keeps.Contains(binding.Name.Identifier.Text):
                return Length(access.Expression, depth + 1);

            case ArrayCreationExpressionSyntax array:
                var sizes = array.Type.RankSpecifiers.SelectMany(rank => rank.Sizes).Where(size => size is not OmittedArraySizeExpressionSyntax).ToArray();

                return sizes.Length == 0
                    ? new(Extent.Small, "written out")
                    : Size(sizes[0], depth + 1);
            case ImplicitArrayCreationExpressionSyntax or InitializerExpressionSyntax:
                return new(Extent.Small, "written out");
            case CollectionExpressionSyntax collection:
                return collection.Elements
                    .OfType<SpreadElementSyntax>()
                    .Select(spread => Length(spread.Expression, depth + 1))
                    .Aggregate(new Shown(Extent.Small, "written out"), (a, b) => a.Or(b));
            case ObjectCreationExpressionSyntax creation:
                return Arguments(creation.ArgumentList);
            case ImplicitObjectCreationExpressionSyntax creation:
                return Arguments(creation.ArgumentList);

            case InvocationExpressionSyntax call:
                return LengthOfCall(call, depth);

            case MemberAccessExpressionSyntax or IdentifierNameSyntax:
                return LengthOfSymbol(expression, depth);

            case ElementAccessExpressionSyntax element:
                return Inner(element.Expression, depth + 1);

            default:
                return new(Extent.Unknown, expression.ToString());
        }
    }

    private Shown LengthOfCall(InvocationExpressionSyntax call, int depth)
    {
        if (IsGridArrays(call))
        {
            return new(Extent.Grid, "an array from GridArrays");
        }

        var name = NameOf(call.Expression) ?? "";

        if (Model(call).GetSymbolInfo(call).Symbol is not IMethodSymbol method)
        {
            return new(Extent.Unknown, call.ToString());
        }

        if (method.DeclaringSyntaxReferences.Length > 0)
        {
            return Returns(method, call.ToString(), "length", value => Length(value, depth + 1));
        }

        var receiver = Receiver(call);

        if (method.ContainingType?.Name == "Enumerable" && name is "Range" or "Repeat" && call.ArgumentList.Arguments.Count == 2)
        {
            return Size(call.ArgumentList.Arguments[1].Expression, depth + 1);
        }

        if (name == "Empty")
        {
            return new(Extent.Small, "empty");
        }

        if (receiver is not null && Keeps.Contains(name))
        {
            return Length(receiver, depth + 1);
        }

        if (receiver is not null && Joins.Contains(name))
        {
            return call.ArgumentList.Arguments
                .Where(argument => argument.Expression != receiver)
                .Select(argument => Element(Model(argument.Expression).GetTypeInfo(argument.Expression).Type) is null
                    ? new Shown(Extent.Small, "one more")
                    : Length(argument.Expression, depth + 1))
                .Aggregate(Length(receiver, depth + 1), (a, b) => a.Or(b));
        }

        if (receiver is not null && name == "SelectMany" && call.ArgumentList.Arguments.LastOrDefault()?.Expression is LambdaExpressionSyntax each)
        {
            return Length(receiver, depth + 1).Or(Body(each, value => Length(value, depth + 1)));
        }

        if (receiver is not null && Picks.Contains(name))
        {
            return Inner(receiver, depth + 1);
        }

        // Text taken apart is text.
        if (receiver is not null && Model(receiver).GetTypeInfo(receiver).Type?.SpecialType == SpecialType.System_String)
        {
            return new(Extent.Small, "a string");
        }

        return new(Extent.Unknown, call.ToString());
    }

    private Shown LengthOfSymbol(ExpressionSyntax expression, int depth)
    {
        var model = Model(expression);
        var symbol = model.GetSymbolInfo(expression).Symbol;

        if (symbol is null)
        {
            return new(Extent.Unknown, expression.ToString());
        }

        if (symbol is IFieldSymbol { ContainingType.IsTupleType: true } part && expression is MemberAccessExpressionSyntax whole)
        {
            return Parts(whole.Expression, Place(part), 0) is { } parts
                ? parts.Select(value => Length(value, depth + 1)).Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b))
                : new(Extent.Unknown, expression.ToString());
        }

        // The keys and values of a dictionary are as many as its entries.
        if (symbol is IPropertySymbol { Name: "Keys" or "Values" } && expression is MemberAccessExpressionSyntax of)
        {
            return Length(of.Expression, depth + 1);
        }

        return Follow(
            symbol,
            expression,
            "length",
            value => Length(value, depth + 1),
            element => Inner(element, depth + 1),
            grown: true);
    }

    /// <summary>What the length of an element of a collection of collections derives from.</summary>
    private Shown Inner(ExpressionSyntax collection, int depth)
    {
        if (depth > Deepest)
        {
            return new(Extent.Unknown, collection.ToString());
        }

        var model = Model(collection);
        var info = model.GetTypeInfo(collection);
        var element = Element(info.Type ?? info.ConvertedType);

        if (element?.SpecialType == SpecialType.System_String)
        {
            return new(Extent.Small, "a string");
        }

        if (collection.IsKind(SyntaxKind.NullLiteralExpression) || collection.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            return new(Extent.Small, "nothing");
        }

        var things = IsThing(Element(element));
        var shown = InnerOf(collection, depth);

        return things && shown.Extent == Extent.Unknown ? new(Extent.Small, $"an element of {collection}, a collection of things") : shown;
    }

    private Shown InnerOf(ExpressionSyntax collection, int depth)
    {
        switch (collection)
        {
            case ParenthesizedExpressionSyntax inner:
                return Inner(inner.Expression, depth + 1);
            case CastExpressionSyntax cast:
                return Inner(cast.Expression, depth + 1);
            case PostfixUnaryExpressionSyntax suppress when suppress.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return Inner(suppress.Operand, depth + 1);
            case ConditionalExpressionSyntax either:
                return Inner(either.WhenTrue, depth + 1).Or(Inner(either.WhenFalse, depth + 1));

            case CollectionExpressionSyntax written:
                return written.Elements
                    .Select(item => item switch
                    {
                        SpreadElementSyntax spread => Inner(spread.Expression, depth + 1),
                        ExpressionElementSyntax one => Length(one.Expression, depth + 1),
                        _ => new Shown(Extent.Unknown, item.ToString()),
                    })
                    .Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b));

            case ArrayCreationExpressionSyntax { Initializer: { } items }:
                return items.Expressions.Select(item => Length(item, depth + 1)).Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b));
            case ImplicitArrayCreationExpressionSyntax implicitArray:
                return implicitArray.Initializer.Expressions.Select(item => Length(item, depth + 1)).Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b));

            // Made empty, or with room: what it holds is what is put into it, asked at the symbol.
            case ArrayCreationExpressionSyntax or ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } or ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 }:
                return new(Extent.Small, "empty");
            case ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                var arguments = ((BaseObjectCreationExpressionSyntax)collection).ArgumentList!.Arguments;

                return arguments
                    .Select(argument => Element(Model(argument.Expression).GetTypeInfo(argument.Expression).Type) is null
                        ? new Shown(Extent.Small, "a capacity")
                        : Inner(argument.Expression, depth + 1))
                    .Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b));

            case InvocationExpressionSyntax call:
                var name = NameOf(call.Expression) ?? "";
                var receiver = Receiver(call);

                if (Model(call).GetSymbolInfo(call).Symbol is IMethodSymbol { DeclaringSyntaxReferences.Length: > 0 } own)
                {
                    return Returns(own, call.ToString(), "inner", value => Inner(value, depth + 1));
                }

                if (receiver is not null && name == "Select" && call.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } projection)
                {
                    return projection is LambdaExpressionSyntax lambda
                        ? Body(lambda, value => Length(value, depth + 1))
                        : Model(projection).GetSymbolInfo(projection).Symbol is IMethodSymbol group
                            ? Returns(group, projection.ToString(), "length", value => Length(value, depth + 1))
                            : new(Extent.Unknown, call.ToString());
                }

                if (name == "Repeat" && call.ArgumentList.Arguments.Count == 2)
                {
                    return Length(call.ArgumentList.Arguments[0].Expression, depth + 1);
                }

                if (receiver is not null && (Keeps.Contains(name) || Joins.Contains(name)))
                {
                    return call.ArgumentList.Arguments
                        .Where(argument => Joins.Contains(name) && argument.Expression != receiver)
                        .Select(argument => Inner(argument.Expression, depth + 1))
                        .Aggregate(Inner(receiver, depth + 1), (a, b) => a.Or(b));
                }

                return new(Extent.Unknown, call.ToString());

            case MemberAccessExpressionSyntax or IdentifierNameSyntax:
                var symbol = Model(collection).GetSymbolInfo(collection).Symbol;

                if (symbol is null)
                {
                    return new(Extent.Unknown, collection.ToString());
                }

                if (symbol is IFieldSymbol { ContainingType.IsTupleType: true } part && collection is MemberAccessExpressionSyntax whole)
                {
                    return Parts(whole.Expression, Place(part), 0) is { } parts
                        ? parts.Select(value => Inner(value, depth + 1)).Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b))
                        : new(Extent.Unknown, collection.ToString());
                }

                var shown = Follow(
                    symbol, collection, "inner", value => Inner(value, depth + 1), _ => new(Extent.Unknown, collection.ToString()));

                // An element added to in place - held[k].Add(x) - is as long as the loops that does it in.
                foreach (var grow in _innerGrowth.GetValueOrDefault(symbol.OriginalDefinition) ?? [])
                {
                    shown = shown.Or(Loops(grow));
                }

                // And whatever is put into it afterwards.
                foreach (var grow in _growth.GetValueOrDefault(symbol) ?? [])
                {
                    var added = grow.ArgumentList.Arguments.LastOrDefault()?.Expression;

                    shown = shown.Or(
                        added is null ? new(Extent.Unknown, grow.ToString())
                        : NameOf(grow.Expression) is "AddRange" or "InsertRange" or "UnionWith" ? Inner(added, depth + 1)
                        : Length(added, depth + 1));
                }

                foreach (var stored in _stores.GetValueOrDefault(symbol) ?? [])
                {
                    shown = shown.Or(Length(stored, depth + 1));
                }

                return shown;

            default:
                return new(Extent.Unknown, collection.ToString());
        }
    }

    // ---- following a symbol back -----------------------------------------------------------

    /// <summary>
    /// What a symbol's value derives from: everything assigned to it, every argument passed for
    /// it, or what its getter returns.
    /// </summary>
    /// <param name="symbol">The local, parameter, field or property.</param>
    /// <param name="use">Where it is read, for the message.</param>
    /// <param name="question">Which question is being asked, to keep the answers apart.</param>
    /// <param name="ask">The question, of an expression the symbol is given.</param>
    /// <param name="element">The question, of a collection the symbol is an element of.</param>
    /// <param name="grown">Whether calls that grow a collection held in the symbol count.</param>
    private Shown Follow(
        ISymbol symbol,
        ExpressionSyntax use,
        string question,
        Func<ExpressionSyntax, Shown> ask,
        Func<ExpressionSyntax, Shown> element,
        bool grown = false)
    {
        symbol = symbol.OriginalDefinition;

        var key = (symbol, question);

        if (_settled.TryGetValue(key, out var settled))
        {
            return settled;
        }

        // Round a cycle: taken as small, so that what else the symbol is given decides.
        if (!_active.Add(key))
        {
            _cycles++;
            return new(Extent.Small, "itself");
        }

        var cyclesBefore = _cycles;

        try
        {
            var shown = symbol switch
            {
                IFieldSymbol { IsConst: true } => new Shown(Extent.Small, "a constant"),
                IParameterSymbol parameter => Parameter(parameter, use, ask, element),
                ILocalSymbol or IFieldSymbol or IPropertySymbol => Variable(symbol, use, question, ask, element),
                _ => new Shown(Extent.Unknown, use.ToString()),
            };

            if (grown && shown.Extent != Extent.Grid)
            {
                shown = shown.Or(Growth(symbol));
            }

            // Settled only if nothing in it was taken on trust round a cycle.
            if (_cycles == cyclesBefore)
            {
                _settled[key] = shown;
            }

            return shown;
        }
        finally
        {
            _active.Remove(key);
        }
    }

    private Shown Variable(ISymbol symbol, ExpressionSyntax use, string question, Func<ExpressionSyntax, Shown> ask, Func<ExpressionSyntax, Shown> element)
    {
        // A property with a body is what the body returns.
        if (symbol is IPropertySymbol { GetMethod: { } getter } property
            && property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax declared
            && (declared.ExpressionBody is not null || declared.AccessorList?.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null) == true))
        {
            return Returns(getter, use.ToString(), question, ask);
        }

        // A record's positional property, or a primary constructor's parameter kept as one.
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is ParameterSyntax positional
            && Model(positional).GetDeclaredSymbol(positional) is IParameterSymbol parameter)
        {
            return Parameter(parameter, use, ask, element).Or(Writes(symbol, use, ask, element, required: false));
        }

        return Writes(symbol, use, ask, element, required: true);
    }

    private Shown Writes(ISymbol symbol, ExpressionSyntax use, Func<ExpressionSyntax, Shown> ask, Func<ExpressionSyntax, Shown> element, bool required)
    {
        if (!_writes.TryGetValue(symbol, out var writes))
        {
            return required
                ? new(Extent.Unknown, $"{use}, which nothing in the sources assigns")
                : new(Extent.Small, "unassigned");
        }

        var shown = new Shown(Extent.Small, "unassigned");

        foreach (var write in writes)
        {
            shown = shown.Or(write switch
            {
                Assigned assigned => ask(assigned.Value),
                ElementOf from => element(from.Collection),
                Counted counted => Loops(counted.At),
                PartOf part => Parts(part.Tuple, part.Place, 0) is { } parts
                    ? parts.Select(ask).Aggregate(new Shown(Extent.Small, "empty"), (a, b) => a.Or(b))
                    : new(Extent.Unknown, $"{use}, taken out of a tuple the scan cannot read"),
                _ => new(Extent.Unknown, $"{use}, which is written in a way the scan does not read"),
            });
        }

        return shown;
    }

    private Shown Parameter(IParameterSymbol parameter, ExpressionSyntax use, Func<ExpressionSyntax, Shown> ask, Func<ExpressionSyntax, Shown> element)
    {
        // A lambda's parameter is an element of what the lambda is applied to.
        if (parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction })
        {
            return parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is { } declared
                && declared.FirstAncestorOrSelf<LambdaExpressionSyntax>() is { Parent: ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax applied } } }
                && Receiver(applied) is { } over
                && parameter.Ordinal == 0
                && Element(Model(over).GetTypeInfo(over).Type) is { } item
                && SymbolEqualityComparer.Default.Equals(item, parameter.Type)
                    ? element(over)
                    : new(Extent.Unknown, $"{use}, a lambda's parameter");
        }

        if (parameter.ContainingSymbol is not IMethodSymbol method)
        {
            return new(Extent.Unknown, use.ToString());
        }

        method = method.OriginalDefinition;

        if (_asDelegates.Contains(method))
        {
            return new(Extent.Unknown, $"{use}, a parameter of {method.Name}, which is passed as a delegate");
        }

        if (method.IsOverride || method.IsVirtual || method.IsAbstract || method.ContainingType.TypeKind == TypeKind.Interface
            || method.ExplicitInterfaceImplementations.Length > 0 || ImplementsAnInterface(method))
        {
            return new(Extent.Unknown, $"{use}, a parameter of {method.Name}, which is called through an interface or a base");
        }

        if (!_calls.TryGetValue(method, out var calls))
        {
            return new(Extent.Unknown, $"{use}, a parameter of {method.ContainingType.Name}.{method.Name}, which nothing in the sources calls");
        }

        var shown = new Shown(Extent.Small, "uncalled");

        foreach (var call in calls)
        {
            var arguments = call switch
            {
                IInvocationOperation invocation => invocation.Arguments,
                IObjectCreationOperation creation => creation.Arguments,
                _ => [],
            };

            var passed = arguments.FirstOrDefault(a => a.Parameter is { } p && p.Ordinal == parameter.Ordinal && p.Name == parameter.Name);

            shown = shown.Or(
                passed is null ? new(Extent.Unknown, $"{use}, a parameter of {method.Name}, at {call.Syntax}")
                : passed.ArgumentKind == ArgumentKind.DefaultValue ? new(Extent.Small, "a default")
                : passed.ArgumentKind == ArgumentKind.ParamArray || passed.Value.Syntax is not ExpressionSyntax
                    ? new(Extent.Small, "written out")
                : ask((ExpressionSyntax)passed.Value.Syntax));
        }

        return shown;
    }

    private static bool ImplementsAnInterface(IMethodSymbol method) =>
        method.ContainingType.AllInterfaces
            .SelectMany(contract => contract.GetMembers().OfType<IMethodSymbol>())
            .Any(member => SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method));

    /// <summary>How many times a collection held in a symbol is added to: the loops its adds are in.</summary>
    private Shown Growth(ISymbol symbol)
    {
        var shown = new Shown(Extent.Small, "not grown");

        foreach (var grow in _growth.GetValueOrDefault(symbol) ?? [])
        {
            if (NameOf(grow.Expression) is "AddRange" or "InsertRange" or "UnionWith"
                && grow.ArgumentList.Arguments.LastOrDefault()?.Expression is { } range)
            {
                shown = shown.Or(Length(range, 1));
            }

            shown = shown.Or(Loops(grow));
        }

        // A dictionary is added to by a store under a new key; a list or an array is not.
        var held = symbol switch
        {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null,
        };

        if (held?.Name.Contains("Dictionary", StringComparison.Ordinal) == true)
        {
            foreach (var stored in _stores.GetValueOrDefault(symbol) ?? [])
            {
                shown = shown.Or(Loops(stored));
            }
        }

        return shown;
    }

    /// <summary>How many times a statement runs: the extent of every loop round it.</summary>
    private Shown Loops(SyntaxNode node)
    {
        var shown = new Shown(Extent.Small, "once");

        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case ForEachStatementSyntax each:
                    shown = shown.Or(Length(each.Expression, 1));
                    break;
                case ForStatementSyntax { Condition: BinaryExpressionSyntax until } loop:
                    var counter = loop.Declaration?.Variables.FirstOrDefault()?.Identifier.Text;
                    var bound = until.Left is IdentifierNameSyntax left && left.Identifier.Text == counter ? until.Right : until.Left;

                    shown = shown.Or(Size(bound, 1));
                    break;
                case ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForEachVariableStatementSyntax:
                    return new(Extent.Unknown, $"a loop round {Shorten(node)} whose length the scan does not read");
                // A local function runs as often as it is called: the loops round each call.
                case LocalFunctionStatementSyntax local
                    when Model(local).GetDeclaredSymbol(local) is IMethodSymbol function
                        && !_asDelegates.Contains(function.OriginalDefinition)
                        && _calls.TryGetValue(function.OriginalDefinition, out var calls)
                        && _active.Add((function.OriginalDefinition, "loops")):
                    try
                    {
                        return calls.Aggregate(shown, (worst, call) => worst.Or(Loops(call.Syntax)));
                    }
                    finally
                    {
                        _active.Remove((function.OriginalDefinition, "loops"));
                    }

                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax:
                    return new(Extent.Unknown, $"{Shorten(node)}, inside a function called from elsewhere");
                case MemberDeclarationSyntax:
                    return shown;
            }
        }

        return shown;
    }

    /// <summary>What a method declared in the sources returns.</summary>
    private Shown Returns(IMethodSymbol method, string use, string question, Func<ExpressionSyntax, Shown> ask)
    {
        method = method.OriginalDefinition;

        if (OfAGrid(method) is { } grid)
        {
            return grid;
        }

        if (method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } declared
            || method.IsAbstract || method.IsVirtual || method.IsOverride || method.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return new(Extent.Unknown, use);
        }

        var key = ((ISymbol)method, "returns " + question);

        if (_settled.TryGetValue(key, out var settled))
        {
            return settled;
        }

        if (!_active.Add(key))
        {
            _cycles++;
            return new(Extent.Small, "itself");
        }

        var cyclesBefore = _cycles;

        try
        {
            var returned = declared switch
            {
                ArrowExpressionClauseSyntax arrow => [arrow.Expression],
                BaseMethodDeclarationSyntax { ExpressionBody: { } arrow } => [arrow.Expression],
                LocalFunctionStatementSyntax { ExpressionBody: { } arrow } => [arrow.Expression],
                AccessorDeclarationSyntax { ExpressionBody: { } arrow } => [arrow.Expression],
                PropertyDeclarationSyntax { ExpressionBody: { } arrow } => [arrow.Expression],
                _ => ReturnedBy(declared),
            };

            var shown = returned is null || returned.Count == 0
                ? new Shown(Extent.Unknown, use)
                : returned.Select(ask).Aggregate((a, b) => a.Or(b));

            if (_cycles == cyclesBefore)
            {
                _settled[key] = shown;
            }

            return shown;
        }
        finally
        {
            _active.Remove(key);
        }
    }

    /// <summary>What a lambda returns.</summary>
    private static Shown Body(LambdaExpressionSyntax lambda, Func<ExpressionSyntax, Shown> ask) =>
        lambda.ExpressionBody is { } expression
            ? ask(expression)
            : ReturnedBy(lambda) is { Count: > 0 } returned
                ? returned.Select(ask).Aggregate((a, b) => a.Or(b))
                : new(Extent.Unknown, lambda.ToString());

    /// <summary>The expressions a body returns, or null if it yields.</summary>
    private static List<ExpressionSyntax>? ReturnedBy(SyntaxNode declared)
    {
        var body = declared
            .DescendantNodes(node => node == declared || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .ToArray();

        return body.Any(node => node is YieldStatementSyntax)
            ? null
            : [.. body.OfType<ReturnStatementSyntax>().Select(statement => statement.Expression).OfType<ExpressionSyntax>()];
    }

    // ---- the index -------------------------------------------------------------------------

    /// <summary>One way a variable is given a value.</summary>
    private abstract record Written;

    /// <summary>Assigned an expression.</summary>
    private sealed record Assigned(ExpressionSyntax Value) : Written;

    /// <summary>Made an element of a collection, by a foreach.</summary>
    private sealed record ElementOf(ExpressionSyntax Collection) : Written;

    /// <summary>Counted up or added to: as many times as the loops it is in run.</summary>
    private sealed record Counted(SyntaxNode At) : Written;

    /// <summary>Taken out of one place of a tuple, by deconstruction.</summary>
    private sealed record PartOf(ExpressionSyntax Tuple, int Place) : Written;

    /// <summary>Written in a way the scan does not read: by reference, by a pattern, by deconstruction.</summary>
    private sealed record Unread : Written;

    private void Index(SyntaxTree tree)
    {
        var model = _compilation.GetSemanticModel(tree);

        _models[tree] = model;

        void Write(ISymbol? symbol, Written value)
        {
            if (symbol is null)
            {
                return;
            }

            symbol = symbol.OriginalDefinition;

            if (!_writes.TryGetValue(symbol, out var writes))
            {
                _writes[symbol] = writes = [];
            }

            writes.Add(value);
        }

        void Called(ISymbol? method, IOperation call)
        {
            if (method is IMethodSymbol target)
            {
                var definition = (target.ReducedFrom ?? target).OriginalDefinition;

                if (!_calls.TryGetValue(definition, out var calls))
                {
                    _calls[definition] = calls = [];
                }

                calls.Add(call);
            }
        }

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax
                    or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax:
                    switch (model.GetOperation(node))
                    {
                        case IInvocationOperation invocation:
                            Called(invocation.TargetMethod, invocation);
                            break;
                        case IObjectCreationOperation creation:
                            Called(creation.Constructor, creation);
                            break;
                    }

                    if (node is InvocationExpressionSyntax grow && Grows.Contains(NameOf(grow.Expression) ?? "") && Receiver(grow) is { } onto)
                    {
                        // held[k]!.Add(x) and (held[k] ??= []).Add(x) add to held[k].
                        while (true)
                        {
                            if (onto is PostfixUnaryExpressionSyntax sure && sure.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                            {
                                onto = sure.Operand;
                            }
                            else if (onto is ParenthesizedExpressionSyntax wrapped)
                            {
                                onto = wrapped.Expression;
                            }
                            else if (onto is AssignmentExpressionSyntax made && made.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                            {
                                onto = made.Left;
                            }
                            else
                            {
                                break;
                            }
                        }

                        var (sink, grown) = onto is ElementAccessExpressionSyntax at ? (_innerGrowth, at.Expression) : (_growth, onto);

                        if (model.GetSymbolInfo(grown).Symbol is ILocalSymbol or IFieldSymbol or IPropertySymbol or IParameterSymbol)
                        {
                            var held = model.GetSymbolInfo(grown).Symbol!.OriginalDefinition;

                            if (!sink.TryGetValue(held, out var grows))
                            {
                                sink[held] = grows = [];
                            }

                            grows.Add(grow);
                        }
                    }

                    // Anything passed by reference may be written by what it is passed to.
                    foreach (var argument in (node as InvocationExpressionSyntax)?.ArgumentList.Arguments ?? [])
                    {
                        if (!argument.RefKindKeyword.IsKind(SyntaxKind.None) && !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword))
                        {
                            Write(
                                argument.Expression is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax made }
                                    ? model.GetDeclaredSymbol(made)
                                    : model.GetSymbolInfo(argument.Expression).Symbol,
                                new Unread());
                        }
                    }

                    break;

                case VariableDeclaratorSyntax declarator when declarator.Initializer is { } initializer:
                    Write(model.GetDeclaredSymbol(declarator), new Assigned(initializer.Value));
                    break;

                case PropertyDeclarationSyntax { Initializer: { } initial } property:
                    Write(model.GetDeclaredSymbol(property), new Assigned(initial.Value));
                    break;

                case AssignmentExpressionSyntax assignment:
                    var target = model.GetSymbolInfo(assignment.Left).Symbol;

                    if (assignment.Left is ElementAccessExpressionSyntax into
                        && model.GetSymbolInfo(into.Expression).Symbol is { } indexed
                        && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        indexed = indexed.OriginalDefinition;

                        if (!_stores.TryGetValue(indexed, out var stores))
                        {
                            _stores[indexed] = stores = [];
                        }

                        stores.Add(assignment.Right);
                    }
                    else if (assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax)
                    {
                        // var (a, b) = ..., (var a, var b) = ..., (a, b) = ...: each is its place of the right.
                        SyntaxNode[] places = assignment.Left switch
                        {
                            DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax all } => [.. all.Variables],
                            TupleExpressionSyntax tuple => [.. tuple.Arguments.Select(a => a.Expression)],
                            _ => [],
                        };

                        for (var place = 0; place < places.Length; place++)
                        {
                            var symbol = places[place] switch
                            {
                                SingleVariableDesignationSyntax made => model.GetDeclaredSymbol(made),
                                DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax made } => model.GetDeclaredSymbol(made),
                                IdentifierNameSyntax or MemberAccessExpressionSyntax => model.GetSymbolInfo(places[place]).Symbol,
                                _ => null,
                            };

                            Write(symbol, new PartOf(assignment.Right, place));

                            // A nested one is not followed.
                            foreach (var nested in places[place].DescendantNodes().OfType<SingleVariableDesignationSyntax>())
                            {
                                if (places[place] is not DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax })
                                {
                                    Write(model.GetDeclaredSymbol(nested), new Unread());
                                }
                            }
                        }
                    }
                    else
                    {
                        Write(target, new Assigned(assignment.Right));

                        // A compound assignment adds to what was there, as often as its loops run.
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                        {
                            Write(target, new Counted(assignment));
                        }
                    }

                    break;

                case PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
                    when node.IsKind(SyntaxKind.PreIncrementExpression) || node.IsKind(SyntaxKind.PostIncrementExpression)
                        || node.IsKind(SyntaxKind.PreDecrementExpression) || node.IsKind(SyntaxKind.PostDecrementExpression):
                    var counted = node is PrefixUnaryExpressionSyntax prefix ? prefix.Operand : ((PostfixUnaryExpressionSyntax)node).Operand;

                    // Counting: as many as the loops it is counted in.
                    Write(model.GetSymbolInfo(counted).Symbol, new Counted(node));
                    break;

                case ForEachStatementSyntax each:
                    Write(model.GetDeclaredSymbol(each), new ElementOf(each.Expression));
                    break;

                case ForEachVariableStatementSyntax each:
                    foreach (var made in each.Variable.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
                    {
                        Write(model.GetDeclaredSymbol(made), new Unread());
                    }

                    break;

                case IsPatternExpressionSyntax { Pattern: var pattern } matched:
                    foreach (var made in pattern.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
                    {
                        // The whole pattern's own variable is the expression matched; one inside it is not followed.
                        Write(
                            model.GetDeclaredSymbol(made),
                            made.Parent == pattern || (made.Parent is RecursivePatternSyntax { PositionalPatternClause: null, PropertyPatternClause.Subpatterns.Count: 0 } whole && whole == pattern)
                                ? new Assigned(matched.Expression)
                                : new Unread());
                    }

                    break;

                case SwitchExpressionArmSyntax or CasePatternSwitchLabelSyntax:
                    foreach (var made in node.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
                    {
                        Write(model.GetDeclaredSymbol(made), new Unread());
                    }

                    break;

                // A method named where a value goes is passed as a delegate.
                case IdentifierNameSyntax or MemberAccessExpressionSyntax
                    when node.Parent is ArgumentSyntax or EqualsValueClauseSyntax or ReturnStatementSyntax or ArrowExpressionClauseSyntax
                            or ConditionalExpressionSyntax or CastExpressionSyntax or ExpressionElementSyntax or InitializerExpressionSyntax
                            or AssignmentExpressionSyntax or BinaryExpressionSyntax or ParenthesizedExpressionSyntax
                        && model.GetSymbolInfo(node).Symbol is IMethodSymbol group:
                    _asDelegates.Add((group.ReducedFrom ?? group).OriginalDefinition);
                    break;
            }
        }
    }

    // ---- small helpers ---------------------------------------------------------------------

    /// <summary>A whole-number member of a grid: a count of its nodes along an axis, or of all of them.</summary>
    private static Shown? OfAGrid(ISymbol symbol)
    {
        var type = symbol switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null,
        };

        return symbol.ContainingType is { } owner && GridTypes.Contains(owner.Name) && type is not null && IsWhole(type)
            ? new(Extent.Grid, $"{owner.Name}.{symbol.Name}")
            : null;
    }

    private static bool IsWhole(ITypeSymbol type) =>
        IsNumber(type) && type.SpecialType is not (SpecialType.System_Double or SpecialType.System_Single);

    /// <summary>Which place of its tuple a tuple's element is.</summary>
    private static int Place(IFieldSymbol part)
    {
        var elements = part.ContainingType.TupleElements;

        for (var at = 0; at < elements.Length; at++)
        {
            if (elements[at].Name == part.Name || elements[at].CorrespondingTupleField?.Name == part.Name)
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>The expressions put in one place of a tuple, or null if the tuple cannot be read.</summary>
    private List<ExpressionSyntax>? Parts(ExpressionSyntax tuple, int place, int depth)
    {
        if (place < 0 || depth > Deepest)
        {
            return null;
        }

        switch (tuple)
        {
            case ParenthesizedExpressionSyntax inner:
                return Parts(inner.Expression, place, depth + 1);
            case PostfixUnaryExpressionSyntax suppress when suppress.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return Parts(suppress.Operand, place, depth + 1);
            case TupleExpressionSyntax written when place < written.Arguments.Count:
                return [written.Arguments[place].Expression];
            case ConditionalExpressionSyntax either:
                return Parts(either.WhenTrue, place, depth + 1) is { } whenTrue && Parts(either.WhenFalse, place, depth + 1) is { } whenFalse
                    ? [.. whenTrue, .. whenFalse]
                    : null;

            case InvocationExpressionSyntax call:
                if (Model(call).GetSymbolInfo(call).Symbol is not IMethodSymbol { DeclaringSyntaxReferences.Length: > 0 } method
                    || method.IsAbstract || method.IsVirtual || method.IsOverride
                    || method.OriginalDefinition.DeclaringSyntaxReferences[0].GetSyntax() is not { } declared)
                {
                    return null;
                }

                var returned = declared switch
                {
                    BaseMethodDeclarationSyntax { ExpressionBody: { } arrow } => [arrow.Expression],
                    LocalFunctionStatementSyntax { ExpressionBody: { } arrow } => [arrow.Expression],
                    _ => ReturnedBy(declared),
                };

                return Gather(returned, place, depth);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                var kept = Model(tuple).GetSymbolInfo(tuple).Symbol;

                if (kept is not (ILocalSymbol or IFieldSymbol or IPropertySymbol)
                    || !_writes.TryGetValue(kept.OriginalDefinition, out var writes)
                    || writes.Any(write => write is not Assigned))
                {
                    return null;
                }

                return Gather([.. writes.OfType<Assigned>().Select(write => write.Value)], place, depth);

            default:
                return null;
        }
    }

    private List<ExpressionSyntax>? Gather(List<ExpressionSyntax>? tuples, int place, int depth)
    {
        if (tuples is null || tuples.Count == 0)
        {
            return null;
        }

        var parts = new List<ExpressionSyntax>();

        foreach (var tuple in tuples)
        {
            if (Parts(tuple, place, depth + 1) is not { } some)
            {
                return null;
            }

            parts.AddRange(some);
        }

        return parts;
    }

    /// <summary>What a collection type holds, or null if the type is not a collection.</summary>
    private static ITypeSymbol? Element(ITypeSymbol? type) => type switch
    {
        null => null,
        IArrayTypeSymbol array => array.ElementType,
        INamedTypeSymbol { SpecialType: SpecialType.System_String } => null,
        INamedTypeSymbol { IsGenericType: true, Name: "Dictionary" or "IDictionary" or "IReadOnlyDictionary" or "SortedDictionary" or "KeyValuePair" } => null,
        INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } named => Element(named.TypeArguments[0]),
        INamedTypeSymbol { IsGenericType: true, Name: "Span" or "ReadOnlySpan" or "Memory" or "ReadOnlyMemory" } named => named.TypeArguments[0],
        INamedTypeSymbol named => named.AllInterfaces
            .Concat([named])
            .FirstOrDefault(contract => contract is { IsGenericType: true, Name: "IEnumerable" })?.TypeArguments[0],
        _ => null,
    };

    /// <summary>
    /// Whether a type is a thing rather than a bare number: a reference to an object, or a
    /// struct this codebase declares.
    /// </summary>
    private static bool IsThing(ITypeSymbol? type) => type switch
    {
        null or { TypeKind: TypeKind.Error or TypeKind.TypeParameter or TypeKind.Enum } => false,
        { SpecialType: not SpecialType.None } => false,
        INamedTypeSymbol { IsTupleType: true } => false,
        INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable => IsThing(nullable.TypeArguments[0]),
        { IsReferenceType: true } => true,
        { TypeKind: TypeKind.Struct } => type.DeclaringSyntaxReferences.Length > 0,
        _ => false,
    };

    private static bool IsNumber(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32
            or SpecialType.System_UInt64 or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Byte
            or SpecialType.System_SByte or SpecialType.System_Double or SpecialType.System_Single;

    /// <summary>The sequence a call is made on: its receiver, or its first argument if it is called statically.</summary>
    private ExpressionSyntax? Receiver(InvocationExpressionSyntax call)
    {
        // items?.Select(...): the sequence is what the ?. is applied to.
        if (call.Expression is MemberBindingExpressionSyntax)
        {
            return call.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
                .FirstOrDefault(conditional => conditional.WhenNotNull.Contains(call))?.Expression;
        }

        if (call.Expression is not MemberAccessExpressionSyntax access)
        {
            return null;
        }

        // Enumerable.Select(items, ...) and Array.Resize(ref items, ...): the type is not the sequence.
        return Model(access.Expression).GetSymbolInfo(access.Expression).Symbol is INamedTypeSymbol
            ? call.ArgumentList.Arguments.FirstOrDefault()?.Expression
            : access.Expression;
    }

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

    private static string Shorten(SyntaxNode node)
    {
        var text = node.ToString();

        return text.Length <= 60 ? text : text[..60] + "...";
    }
}
