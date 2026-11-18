namespace Einzel.Commands;

/// <summary>How a text cell is written into the CSV files commands produce.</summary>
/// <remarks>
/// A conductor is named by the model's author, and nothing stops a name holding a comma or a
/// quote; written bare, <c>plate, upper</c> splits into two columns and every column after it
/// moves one along, so the file still parses and reads wrong. The surface table quoted its names
/// and the probe's profile did not, which is why the rule lives here rather than in either.
/// </remarks>
public static class Csv
{
    /// <summary>A CSV field, quoted per RFC 4180 where it holds a comma, a quote or a line break.</summary>
    /// <param name="field">The text, or null for an empty cell.</param>
    /// <returns>The field as it goes into a row.</returns>
    public static string Field(string? field) =>
        field is null
            ? string.Empty
            : field.AsSpan().IndexOfAny(",\"\r\n") < 0
                ? field
                : "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
