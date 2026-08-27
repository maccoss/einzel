using System.Text.Json;
using System.Text.Json.Serialization;

namespace Einzel.Commands;

/// <summary>
/// Writes a quantity that has no value as absent, rather than failing to write it
/// at all.
/// </summary>
/// <remarks>
/// <para>
/// JSON has no not-a-number and no infinity. Every result surface here is JSON, and
/// a single non-finite double anywhere in a document does not degrade it - it takes
/// the whole thing down, at the serialiser, after the run has already succeeded.
/// That has happened four times in this codebase, on four different fields: a
/// convergence residual, a Twiss orientation, a space-charge fraction, and the
/// energy drift of a driven field. Each was fixed where it was found.
/// </para>
/// <para>
/// This is the fix for the class rather than the instance. A non-finite double is
/// written as <c>null</c>, which is the policy the rest of the surface already
/// follows: an undefined measurement is <em>absent</em>, not zero, because zero is
/// a real answer and a reader cannot tell the two apart if both print as zero.
/// </para>
/// <para>
/// Reading is the mirror, so a document round-trips: null comes back as
/// not-a-number rather than throwing. That matters because <c>verify</c> reads
/// stored results back, and a result that cannot be re-read is not regenerable.
/// </para>
/// </remarks>
public sealed class FiniteDoubleConverter : JsonConverter<double>
{
    /// <inheritdoc/>
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? double.NaN : reader.GetDouble();

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (double.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>The same policy for a double that was already optional.</summary>
/// <remarks>
/// Needed separately because a converter for <c>double</c> does not apply to
/// <c>double?</c>, and a nullable field carrying a not-a-number is exactly as
/// unwritable as a non-nullable one.
/// </remarks>
public sealed class FiniteNullableDoubleConverter : JsonConverter<double?>
{
    /// <inheritdoc/>
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble();

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is { } number && double.IsFinite(number))
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
