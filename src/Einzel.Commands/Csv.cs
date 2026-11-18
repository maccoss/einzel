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
    /// <summary>
    /// A text CSV field, quoted per RFC 4180 where it holds a comma, a quote or a line break, and
    /// kept from being read as a formula where it begins like one.
    /// </summary>
    /// <param name="field">The text, or null for an empty cell.</param>
    /// <returns>The field as it goes into a row.</returns>
    /// <remarks>
    /// A spreadsheet opening the file evaluates a cell beginning with <c>=</c>, <c>+</c>, <c>-</c>
    /// or <c>@</c> - or with a tab or carriage return before one - as a formula, so a conductor
    /// named <c>=HYPERLINK(...)</c> would run in whoever opens the table. Such a cell is written
    /// with a single quote before it, inside quotes: <c>"'=HYPERLINK(...)"</c>, which a spreadsheet
    /// shows as the text and a CSV reader reads with the quote as its first character. For text
    /// cells only: numbers are written by the commands directly, never through here, so a
    /// negative number keeps its leading minus.
    /// </remarks>
    public static string Field(string? field)
    {
        if (field is null)
        {
            return string.Empty;
        }

        var formula = field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

        if (formula)
        {
            return "\"'" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return field.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? field
            : "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
