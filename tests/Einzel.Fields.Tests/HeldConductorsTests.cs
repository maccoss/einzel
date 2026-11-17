using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// What a conductor holds is asked of the solve that pinned it, which the assembly hands out
/// with the conductor's shape - not forwarded through whatever wraps the solve.
/// </summary>
public sealed class HeldConductorsTests(ITestOutputHelper output)
{
    /// <summary>
    /// Only the solves say what their conductors hold: no wrapper and no sum forwards the
    /// question.
    /// </summary>
    /// <remarks>
    /// Every wrapper used to forward it - three members each on seven types - and a sum answered
    /// for a name two of its elements might share. The assembly now hands out each leaf with its
    /// solve (<see cref="FieldLeaf"/>), so there is nothing to forward and nothing for a new
    /// wrapper to forget.
    /// </remarks>
    [Fact]
    public void OnlyTheSolvesSayWhatTheirConductorsHold()
    {
        var holders = typeof(IHeldConductors).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IHeldConductors).IsAssignableFrom(t))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["DrivenSolvedField", "SolvedField2D", "SolvedField3D"], holders);
    }

    /// <summary>
    /// Every field type that wraps another field is one the assembly's leaf map is known to
    /// cover, with the reason written beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A leaf's shape has to stand where the wrapped field does: a solve seen through a mirror,
    /// a rotation or an axis of revolution has its metal moved with it. The assembly and the
    /// builders make the leaves as they wrap the field, so each wrapper they apply is applied to
    /// the leaf's shape too. A wrapper type added to the assembly and not to the leaf map would
    /// draw and probe the metal where it was before wrapping, and nothing would fail.
    /// </para>
    /// <para>
    /// So a new wrapper fails here until somebody says how the leaves follow it.
    /// <c>FieldAssembly.BuildMapped</c> also refuses, as it builds, any element that added a
    /// field with conductors and no leaf.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFieldThatWrapsAnotherIsCoveredByTheLeafMap()
    {
        var covered = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReflectedField"] = "the image leaf of a reflected solve is the declared half's field through this (ConductorLeaf.Of)",
            ["AxisymmetricField"] = "applied by the plane builder before it makes the leaves, so the leaf's shape is the wrapped field",
            ["RotatedField"] = "applied by the assembly to a tilted cross-section and to each of its leaves' shapes alike",
            ["SuperposedField"] = "a sum: of a solve and its image (both leaves handed out by the builder), or of elements (each with its own leaves)",
            ["DrivenSuperposedField"] = "a sum of elements, each with its own leaves",
            ["DrivenSolvedField"] = "a driven solve over its channels: itself what its leaves' conductors are held by",
            ["MagnetizedField"] = "wraps the finished sum; moves no conductor",
            ["DrivenMagnetizedField"] = "wraps the finished sum; moves no conductor",
        };

        var wrappers = typeof(IElectrostaticField).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IElectrostaticField).IsAssignableFrom(t))
            .Where(t => t.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Any(c => c.GetParameters().Any(p =>
                    typeof(IElectrostaticField).IsAssignableFrom(p.ParameterType)
                    || (p.ParameterType.IsGenericType
                        && p.ParameterType.GetGenericArguments().Any(a => typeof(IElectrostaticField).IsAssignableFrom(a))))))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        output.WriteLine(string.Join(", ", wrappers));

        // Wrappers the assembly never applies to a field with conductors.
        var elsewhere = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BoundedField"] = "bounds an analytic element to a region; a solved element may not declare one",
            ["DrivenBoundedField"] = "the same, for a driven analytic element",
            ["SequencedField"] = "switches an analytic element between phases; a solve is sequenced by its own stages",
            ["TimeShiftedField"] = "a later leg of a sequenced run, made by the run and not by the assembly",
        };

        Assert.True(wrappers.Count >= 7, $"the control: the scan found {wrappers.Count} wrapping fields");
        Assert.Empty(wrappers.Except(covered.Keys).Except(elsewhere.Keys));
        Assert.Empty(covered.Keys.Concat(elsewhere.Keys).Except(wrappers));
    }
}
