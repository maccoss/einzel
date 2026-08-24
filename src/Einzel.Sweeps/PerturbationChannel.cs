using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Sweeps;

/// <summary>How a perturbation channel is distributed.</summary>
public enum PerturbationDistribution
{
    /// <summary>
    /// Flat between the bounds. What a machining tolerance usually means when it
    /// is quoted as "plus or minus 100 microns" with nothing else said.
    /// </summary>
    Uniform,

    /// <summary>
    /// Normal, with the half-width taken as one standard deviation and draws
    /// truncated at three.
    /// </summary>
    Normal,
}

/// <summary>
/// One parameter a study varies, and how far.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 9 calls these perturbation channels, and section 13 makes them
/// the content of a study: "perturbation channels with distributions, a draw
/// count, a seed, an ensemble specification, and figures of merit to record".
/// </para>
/// <para>
/// A channel names a parameter and a half-width, not an absolute range, because
/// tolerance is quoted that way — a stripe placed to plus or minus 100 microns,
/// a supply stable to 50 ppm. The nominal comes from the model, so the same study
/// applies unchanged to a template instantiated at a different size.
/// </para>
/// </remarks>
/// <param name="Parameter">Name of a free parameter in the model's surface.</param>
/// <param name="HalfWidth">
/// Half the range for a uniform channel, or one standard deviation for a normal
/// one. Same dimension as the parameter.
/// </param>
/// <param name="Distribution">How draws are distributed within it.</param>
public sealed record PerturbationChannel(
    string Parameter,
    Quantity HalfWidth,
    PerturbationDistribution Distribution = PerturbationDistribution.Uniform)
{
    /// <summary>Draws a perturbed value about a nominal.</summary>
    /// <param name="nominal">The unperturbed value.</param>
    /// <param name="random">The source of randomness.</param>
    /// <returns>The perturbed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    public Quantity Draw(Quantity nominal, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var offset = Distribution switch
        {
            PerturbationDistribution.Uniform => ((2.0 * random.NextDouble()) - 1.0) * HalfWidth.SiValue,
            PerturbationDistribution.Normal => TruncatedNormal(random) * HalfWidth.SiValue,
            _ => throw new ArgumentOutOfRangeException(nameof(random), Distribution, "unhandled distribution"),
        };

        return Quantity.Si(nominal.SiValue + offset, nominal.Dimension);
    }

    /// <summary>The extreme values of this channel, for a one-at-a-time scan.</summary>
    /// <param name="nominal">The unperturbed value.</param>
    /// <returns>The low and high ends.</returns>
    /// <remarks>
    /// One-at-a-time uses the ends rather than draws because it is attributing
    /// variance, not sampling it: the question is how much this channel alone can
    /// move the answer, and the ends bound that.
    /// </remarks>
    public (Quantity Low, Quantity High) Extremes(Quantity nominal) =>
        (Quantity.Si(nominal.SiValue - HalfWidth.SiValue, nominal.Dimension),
         Quantity.Si(nominal.SiValue + HalfWidth.SiValue, nominal.Dimension));

    private static double TruncatedNormal(Random random)
    {
        // Box-Muller, rejected beyond three sigma. A machining tolerance with a
        // six-sigma tail is not a machining tolerance, and an unbounded draw would
        // occasionally hand the solver a geometry that does not exist.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var u1 = random.NextDouble();
            var u2 = random.NextDouble();

            if (u1 <= double.Epsilon)
            {
                continue;
            }

            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            if (Math.Abs(z) <= 3.0)
            {
                return z;
            }
        }

        return 0.0;
    }

    /// <summary>Checks that this channel names a free parameter of a surface.</summary>
    /// <param name="surface">The model's parameter surface.</param>
    /// <param name="path">JSON Pointer to this channel, for the error object.</param>
    /// <returns>The parameter it names.</returns>
    /// <exception cref="EinzelException">
    /// No such parameter, it is derived rather than free, or the half-width has the
    /// wrong dimension.
    /// </exception>
    public ResolvedParameter Bind(ParameterSurface surface, string path)
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
                    ? "the model declares no free parameters to perturb"
                    : $"free parameters are: {string.Join(", ", surface.FreeParameters.Select(p => p.Name))}",
            });
        }

        if (parameter.IsDerived)
        {
            // Perturbing a derived parameter would be perturbing a consequence,
            // and whatever it is derived from would immediately overwrite it.
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = $"'{Parameter}' is derived from other parameters and cannot be perturbed directly",
                Suggestion = "perturb the parameters its expression depends on instead",
            });
        }

        if (HalfWidth.Dimension != parameter.Value.Dimension)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = path,
                Constraint =
                    $"the half-width has dimension {HalfWidth.Dimension} but '{Parameter}' has dimension "
                    + $"{parameter.Value.Dimension}",
                Observed = new ObservedValue(HalfWidth.SiValue, HalfWidth.Dimension.ToString()),
                Suggestion = $"supply a half-width of dimension {parameter.Value.Dimension}",
            });
        }

        if (HalfWidth.SiValue < 0.0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a perturbation half-width may not be negative",
                Observed = new ObservedValue(HalfWidth.SiValue, HalfWidth.Dimension.ToString()),
                Suggestion = "use zero to hold a parameter fixed",
            });
        }

        return parameter;
    }
}
