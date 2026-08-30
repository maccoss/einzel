using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Render;

/// <summary>A dimension to draw on a figure, as it appears in a render spec.</summary>
/// <remarks>
/// <para>
/// The memo's own figures are line drawings <em>with dimensions</em>, and a section
/// without them is a picture rather than a drawing: it says what the instrument looks
/// like and not how big any of it is.
/// </para>
/// <para>
/// <b>The number is measured, never written down.</b> What a dimension declares is the
/// two points it spans; the length is the distance between them, computed when the
/// figure is drawn. A typed number would be a second statement of something the model
/// already says, and the two would part company at the first parameter change - which is
/// exactly what a dimensioned drawing exists to prevent. <c>label</c> names the span
/// ("drift", "bore"); it does not carry the value.
/// </para>
/// <para>
/// <b>And the points may be expressions</b>, over the same parameters the model is
/// written in, so a dimension follows the geometry rather than describing where it used
/// to be. Section 9's rule for the model - "every placement is a parametric expression,
/// never a baked number" - is not weaker for a drawing of it.
/// </para>
/// </remarks>
public sealed record DimensionDocument
{
    /// <summary>One end of the span, as a position or an expression.</summary>
    public VectorValue? From { get; init; }

    /// <summary>The other end.</summary>
    public VectorValue? To { get; init; }

    /// <summary>What the span is called. Not the value, which is measured.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// How far off the span the dimension line sits, in page millimetres.
    /// </summary>
    /// <remarks>
    /// Signed, so two dimensions over the same feature can go to opposite sides. The
    /// offset is perpendicular to the span, anticlockwise for a positive value.
    /// </remarks>
    public double OffsetMm { get; init; } = 8.0;
}

/// <summary>A dimension with its ends resolved into space.</summary>
/// <param name="FromSi">One end, in metres.</param>
/// <param name="ToSi">The other end, in metres.</param>
/// <param name="Label">What the span is called, or null.</param>
/// <param name="OffsetMm">How far off the span the dimension line sits.</param>
public sealed record CompiledDimension(
    Vec3 FromSi,
    Vec3 ToSi,
    string? Label,
    double OffsetMm)
{
    /// <summary>The length being dimensioned, in metres.</summary>
    /// <remarks>
    /// Measured from the ends rather than declared beside them. This is the whole
    /// argument for dimensions being a render-time computation: a number written into a
    /// figure is a claim that has to be maintained, and it will not be.
    /// </remarks>
    public double LengthSi => (ToSi - FromSi).Length;

    /// <summary>How the dimension reads on the page.</summary>
    /// <returns>The label, if any, and the measured length in a readable unit.</returns>
    /// <remarks>
    /// The unit is chosen from the magnitude, because one figure may dimension a 300 mm
    /// drift and a 50 µm channel and neither is readable in the other's unit.
    /// </remarks>
    public string Text()
    {
        var (scale, unit) = LengthSi switch
        {
            < 1e-6 => (1e9, "nm"),
            < 1e-3 => (1e6, "µm"),
            < 1.0 => (1e3, "mm"),
            _ => (1.0, "m"),
        };

        var measured = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{LengthSi * scale:G5} {unit}");

        return string.IsNullOrWhiteSpace(Label) ? measured : $"{Label} {measured}";
    }
}

/// <summary>Resolves declared dimensions against a model's parameters.</summary>
public static class DimensionCompiler
{
    /// <summary>Resolves every dimension a spec declares.</summary>
    /// <param name="spec">The render spec.</param>
    /// <param name="model">The validated model, whose parameters the expressions name.</param>
    /// <returns>The dimensions, in the order declared.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// An end is missing, is not a length, or names a parameter the model does not have.
    /// </exception>
    public static IReadOnlyList<CompiledDimension> Compile(RenderSpec spec, CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(model);

        if (spec.Dimensions is not { Count: > 0 })
        {
            return [];
        }

        var parameters = model.Parameters.Values();
        var compiled = new List<CompiledDimension>(spec.Dimensions.Count);

        for (var i = 0; i < spec.Dimensions.Count; i++)
        {
            var declared = spec.Dimensions[i];
            var at = $"/dimensions/{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            compiled.Add(new CompiledDimension(
                End(declared.From, at + "/from", parameters),
                End(declared.To, at + "/to", parameters),
                declared.Label,
                declared.OffsetMm));
        }

        return compiled;
    }

    private static Vec3 End(
        VectorValue? value, string path, IReadOnlyDictionary<string, Quantity> parameters)
    {
        if (value is null)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = "a dimension names both of the points it spans",
                Suggestion = "give it as {\"value\": [x, y, z], \"unit\": \"mm\"}, or as "
                    + "{\"expression\": [\"...\", \"...\", \"...\"]} over the model's parameters "
                    + "so that the dimension follows the geometry rather than describing where "
                    + "it used to be",
            });
        }

        return value.ToVec3(path, Dimension.LengthDimension, parameters);
    }
}
