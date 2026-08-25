using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Sweeps;

/// <summary>A parameter an optimiser may vary, and the interval it may vary it over.</summary>
/// <param name="Parameter">The declared parameter's name.</param>
/// <param name="Minimum">
/// Lower bound. Omit to use the bound the model declares.
/// </param>
/// <param name="Maximum">
/// Upper bound. Omit to use the bound the model declares.
/// </param>
/// <remarks>
/// <para>
/// A design variable is a <em>box</em>, and the box is not optional. Both
/// algorithms here are derivative-free box methods: Nelder-Mead needs an initial
/// scale, CMA-ES needs an initial step size, and both need somewhere to stop. A
/// search with no box would have to invent one from the nominal value, which is a
/// silent guess about a physical dimension, and the guess would be invisible in
/// the answer.
/// </para>
/// <para>
/// The bound usually comes from the model, which is the point of schema 0.2
/// declaring it. A device template already says a quadrupole's inscribed radius
/// is between 1 and 50 mm, and an optimiser should be told that by the device
/// rather than by whoever is driving it.
/// </para>
/// </remarks>
public sealed record DesignVariable(string Parameter, Quantity? Minimum = null, Quantity? Maximum = null)
{
    /// <summary>Checks that this variable names a free, bounded parameter of a surface.</summary>
    /// <param name="surface">The model's parameter surface.</param>
    /// <param name="path">JSON Pointer to this variable, for the error object.</param>
    /// <returns>The parameter, and the box in SI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// No such parameter, it is derived rather than free, a bound has the wrong
    /// dimension, the box is empty, or nothing bounds it at all.
    /// </exception>
    public (ResolvedParameter Parameter, double LowSi, double HighSi) Bind(ParameterSurface surface, string path)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.Parameters.TryGetValue(Parameter, out var parameter))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = $"'{Parameter}' is not a parameter of this model",
                Suggestion = surface.FreeParameters.Count == 0
                    ? "the model declares no free parameters to optimise"
                    : $"free parameters are: {string.Join(", ", surface.FreeParameters.Select(p => p.Name))}",
            });
        }

        if (parameter.IsDerived)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = $"'{Parameter}' is derived from other parameters and cannot be varied directly",
                Suggestion = "optimise the parameters its expression depends on instead",
            });
        }

        var low = Resolve(Minimum, parameter.Minimum, parameter, path, "minimum");
        var high = Resolve(Maximum, parameter.Maximum, parameter, path, "maximum");

        if (high <= low)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = $"'{Parameter}' has an empty search interval [{low:G6}, {high:G6}] SI",
                Observed = new ObservedValue(high - low, "SI"),
                Suggestion = "the upper bound must exceed the lower one",
            });
        }

        return (parameter, low, high);
    }

    private double Resolve(
        Quantity? given, Quantity? declared, ResolvedParameter parameter, string path, string which)
    {
        var bound = given ?? declared;

        if (bound is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = $"'{Parameter}' declares no {which} and none was supplied",
                Suggestion = $"add a \"{which}\" to the parameter in the model, or give one on the design "
                    + "variable. A box optimiser has no scale to work at without one, and inventing a range "
                    + "from the nominal value would be a guess that does not appear in the answer",
            });
        }

        if (bound.Value.Dimension != parameter.Value.Dimension)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = path,
                Constraint = $"the {which} bound must have the same dimension as '{Parameter}': "
                    + $"{bound.Value.Dimension} against {parameter.Value.Dimension}",
                Suggestion = "give the bound in a unit of the parameter's own dimension",
            });
        }

        return bound.Value.SiValue;
    }
}
