using System.Reflection;
using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Results;

/// <summary>
/// Guards GRD-1: "The API offers no way to obtain the value alone."
/// </summary>
/// <remarks>
/// The spec is explicit about why this is stated absolutely — a convenience
/// accessor returning the scalar "will be added by someone eventually, and then
/// used everywhere". A prose rule cannot stop that; a failing build can. These
/// tests inspect the public surface of <see cref="Measured"/> by reflection, so
/// the guard applies to members that do not exist yet.
/// </remarks>
public sealed class MeasuredApiSurfaceTests
{
    private static readonly Type[] ForbiddenReturnTypes =
    [
        typeof(double),
        typeof(float),
        typeof(decimal),
        typeof(Quantity),
    ];

    [Fact]
    public void NoPublicPropertyExposesTheValueAlone()
    {
        var offenders = typeof(Measured)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(p => ForbiddenReturnTypes.Contains(p.PropertyType))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "GRD-1: Measured must expose no property returning a bare magnitude. "
            + $"Offending members: {string.Join(", ", offenders)}. "
            + "Use Deconstruct, which returns the uncertainty, evidence, and warnings with it.");
    }

    [Fact]
    public void NoPublicMethodReturnsTheValueAlone()
    {
        var offenders = typeof(Measured)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => !m.IsSpecialName)
            .Where(m => ForbiddenReturnTypes.Contains(m.ReturnType))
            .Select(m => m.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "GRD-1: Measured must expose no method returning a bare magnitude. "
            + $"Offending members: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void NoPublicFieldsAtAll()
    {
        var offenders = typeof(Measured)
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(f => f.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"GRD-1: Measured must expose no public fields. Offending members: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void DeconstructHandsBackTheWholeEnvelope()
    {
        var deconstructors = typeof(Measured)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(Measured.Deconstruct))
            .ToArray();

        var single = Assert.Single(deconstructors);
        var parameters = single.GetParameters();

        Assert.Equal(4, parameters.Length);
        Assert.All(parameters, p => Assert.True(p.IsOut, $"{p.Name} must be an out parameter"));

        // The point of the rule: you cannot take the value without also being
        // handed what qualifies it.
        Assert.Contains(parameters, p => p.ParameterType == typeof(Quantity).MakeByRefType());
        Assert.Contains(parameters, p => p.ParameterType == typeof(UncertaintyInterval).MakeByRefType());
        Assert.Contains(parameters, p => p.ParameterType == typeof(Evidence).MakeByRefType());
        Assert.Contains(parameters, p => p.ParameterType == typeof(IReadOnlyList<ValidityWarning>).MakeByRefType());
    }

    [Fact]
    public void ConstructionRequiresUncertaintyAndEvidence()
    {
        // There is no constructor overload that omits either, so a caller cannot
        // produce an unqualified result even by accident.
        var constructors = typeof(Measured).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.All(constructors, c =>
        {
            var types = c.GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.Contains(typeof(UncertaintyInterval), types);
            Assert.Contains(typeof(Evidence), types);
        });
    }
}
