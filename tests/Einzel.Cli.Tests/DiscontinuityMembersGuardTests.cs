using System.Reflection;
using Einzel.Fields;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// Every field that holds another field passes that field's discontinuity members through.
/// </summary>
/// <remarks>
/// A wrapper left at the interface's default is one member, its combined measure - a product of
/// signs, under which two surfaces at one place cancel. Driven and sequenced reflected solves went
/// through such a wrapper, so the members their halves declared were combined again one level up,
/// and the reflection's surfaces at the plane cancelled there. Scanned by reflection over every
/// einzel assembly, so a wrapper added later is held to the rule the day it compiles.
/// </remarks>
public sealed class DiscontinuityMembersGuardTests(ITestOutputHelper output)
{
    private static readonly string[] MemberMethods =
    [
        "get_" + nameof(IElectrostaticField.DiscontinuityMembers),
        nameof(IElectrostaticField.MemberDistanceToDiscontinuity),
        nameof(IElectrostaticField.MemberDistancesToDiscontinuity),
        nameof(IElectrostaticField.DiscontinuityMemberGates),
    ];

    // Whether a field's type holds a field: itself one, or an array or sequence of them.
    private static bool HoldsAField(Type type)
    {
        if (typeof(IElectrostaticField).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.IsArray)
        {
            return HoldsAField(type.GetElementType()!);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(HoldsAField);
    }

    [Fact]
    public void EveryFieldHoldingAFieldPassesItsMembersThrough()
    {
        // Every einzel assembly beside the tests, loaded from disk: naming a type in each loaded
        // nothing, so the scan reached only the assemblies something had already pulled in.
        var assemblies = Directory.GetFiles(AppContext.BaseDirectory, "Einzel.*.dll")
            .Where(f => !Path.GetFileName(f).Contains(".Tests", StringComparison.Ordinal))
            .Select(f => Assembly.LoadFrom(f))
            .ToList();

        Assert.Contains(assemblies, a => a.GetName().Name == "Einzel.Transport");
        Assert.Contains(assemblies, a => a.GetName().Name == "Einzel.Library");

        var wrappers = new List<Type>();
        var missing = new List<string>();

        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            if (!type.IsClass || type.IsAbstract || !typeof(IElectrostaticField).IsAssignableFrom(type))
            {
                continue;
            }

            var holds = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Concat(Ancestors(type).SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
                .Any(f => HoldsAField(f.FieldType));

            if (!holds)
            {
                continue;
            }

            wrappers.Add(type);
            var map = type.GetInterfaceMap(typeof(IElectrostaticField));

            foreach (var name in MemberMethods)
            {
                var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == name);
                var target = map.TargetMethods[index];

                if (target.DeclaringType == typeof(IElectrostaticField))
                {
                    missing.Add($"{type.FullName} leaves {name} at the interface's default");
                }
            }
        }

        output.WriteLine($"scanned {string.Join(", ", assemblies.Select(x => x.GetName().Name).Order(StringComparer.Ordinal))}");
        output.WriteLine($"{wrappers.Count} fields holding a field: {string.Join(", ", wrappers.Select(t => t.Name).Order(StringComparer.Ordinal))}");

        // The scan finds something: a guard over nothing passes for any code.
        Assert.Contains(wrappers, t => t.Name == "SuperposedField");
        Assert.Contains(wrappers, t => t.Name == "DrivenSolvedField");
        Assert.Contains(wrappers, t => t.Name == "PonderomotiveField");
        Assert.Empty(missing);
    }

    private static readonly string[] MagneticMemberMethods =
    [
        "get_" + nameof(Einzel.Fields.Magnetic.IMagneticField.MagneticDiscontinuityMembers),
        nameof(Einzel.Fields.Magnetic.IMagneticField.MagneticMemberDistanceToDiscontinuity),
        nameof(Einzel.Fields.Magnetic.IMagneticField.MagneticMemberDistancesToDiscontinuity),
        nameof(Einzel.Fields.Magnetic.IMagneticField.MagneticDiscontinuityMemberGates),
    ];

    /// <summary>The same rule on the magnetic side: a magnet holding a magnet passes its members through.</summary>
    /// <remarks>
    /// Folded into one sign, the shared face of two abutting magnets cancelled, and a box hid an
    /// inner magnet's surface wherever its own face was nearer.
    /// </remarks>
    [Fact]
    public void EveryMagnetHoldingAMagnetPassesItsMembersThrough()
    {
        var magnetic = typeof(Einzel.Fields.Magnetic.IMagneticField);
        var assemblies = Directory.GetFiles(AppContext.BaseDirectory, "Einzel.*.dll")
            .Where(f => !Path.GetFileName(f).Contains(".Tests", StringComparison.Ordinal))
            .Select(f => Assembly.LoadFrom(f))
            .ToList();

        bool Holds(Type type) =>
            magnetic.IsAssignableFrom(type)
            || (type.IsArray && Holds(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericArguments().Any(Holds));

        var wrappers = new List<Type>();
        var missing = new List<string>();

        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            if (!type.IsClass || type.IsAbstract || !magnetic.IsAssignableFrom(type))
            {
                continue;
            }

            var holds = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Concat(Ancestors(type).SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
                .Any(f => Holds(f.FieldType));

            if (!holds)
            {
                continue;
            }

            wrappers.Add(type);
            var map = type.GetInterfaceMap(magnetic);

            foreach (var name in MagneticMemberMethods)
            {
                var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == name);

                if (map.TargetMethods[index].DeclaringType == magnetic)
                {
                    missing.Add($"{type.FullName} leaves {name} at the interface's default");
                }
            }
        }

        output.WriteLine($"{wrappers.Count} magnets holding a magnet: {string.Join(", ", wrappers.Select(t => t.Name).Order(StringComparer.Ordinal))}");

        Assert.Contains(wrappers, t => t.Name == "BoundedMagneticField");
        Assert.Contains(wrappers, t => t.Name == "SuperposedMagneticField");
        Assert.Contains(wrappers, t => t.Name == "MagnetizedField");
        Assert.Empty(missing);
    }

    private static IEnumerable<Type> Ancestors(Type type)
    {
        for (var t = type.BaseType; t is not null && t != typeof(object); t = t.BaseType)
        {
            yield return t;
        }
    }
}
