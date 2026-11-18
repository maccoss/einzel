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
    /// <para>
    /// A spreadsheet opening the file evaluates a cell beginning with <c>=</c>, <c>+</c>, <c>-</c>
    /// or <c>@</c> - or with a tab or carriage return before one - as a formula, so a conductor
    /// named <c>=HYPERLINK(...)</c> would run in whoever opens the table. Such a cell is written
    /// with a single quote before it, inside quotes: <c>"'=HYPERLINK(...)"</c>, which a spreadsheet
    /// shows as the text and a CSV reader reads with the quote as its first character.
    /// </para>
    /// <para>
    /// <c>=</c>, <c>@</c>, a tab and a carriage return are always guarded. A leading <c>+</c> or
    /// <c>-</c> is guarded only where the cell reads as an expression - a digit or a point after the
    /// sign, or an operator or parenthesis anywhere after it - so <c>-1+1</c> and <c>+SUM(A1)</c> are
    /// guarded and a plain name such as <c>-x</c> or <c>-xRod</c> is written exactly. The guard
    /// alters what a CSV reader sees; the <c>--json</c> form of the same command carries every name
    /// exactly and is the one to read a name from. For text cells only: numbers are written by the
    /// commands directly, never through here, so a negative number keeps its leading minus.
    /// </para>
    /// </remarks>
    public static string Field(string? field)
    {
        if (field is null)
        {
            return string.Empty;
        }

        if (ReadsAsFormula(field))
        {
            return "\"'" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return field.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? field
            : "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static readonly System.Buffers.SearchValues<char> Operators = System.Buffers.SearchValues.Create("+-*/^&=<>%()");

    /// <summary>A comment line, every line of it a comment.</summary>
    /// <param name="text">The text, which may run over several lines.</param>
    /// <returns><c># </c> before each of its lines, ending in a line break.</returns>
    /// <remarks>
    /// A warning's message may run over several lines, and a second line written bare would be read
    /// as a row of the table. One rule for every CSV a command writes: the surface table had it and
    /// the probe's profile did not.
    /// </remarks>
    public static string Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "# " + text.ReplaceLineEndings("\n# ") + "\n";
    }

    // Whether a spreadsheet would take the cell for a formula: see Field.
    private static bool ReadsAsFormula(string field)
    {
        if (field.Length == 0)
        {
            return false;
        }

        if (field[0] is '=' or '@' or '\t' or '\r')
        {
            return true;
        }

        if (field[0] is not ('+' or '-') || field.Length == 1)
        {
            return false;
        }

        return char.IsAsciiDigit(field[1]) || field[1] == '.' || field.AsSpan(1).IndexOfAny(Operators) >= 0;
    }
}
