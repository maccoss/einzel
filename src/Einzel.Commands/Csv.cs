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
    /// Every text cell beginning with one of those characters is guarded, as OWASP's guidance on CSV
    /// injection has it - a plain name such as <c>-xRod</c> included, since what a spreadsheet makes
    /// of a cell is the spreadsheet's to decide. The guard alters what a CSV reader sees; the
    /// <c>--json</c> form of the same command carries every name exactly and is the one to read a
    /// name from. For text cells only: numbers are written by the commands directly, never through
    /// here, so a negative number keeps its leading minus.
    /// </para>
    /// </remarks>
    public static string Field(string? field)
    {
        if (field is null)
        {
            return string.Empty;
        }

        if (field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            return "\"'" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        // Quoted wherever it holds a comma, a quote, or any line break ReplaceLineEndings
        // recognises - the set a comment is broken at - so a reader that splits rows at NEL, LS, PS
        // or FF does not split one inside a field.
        return field.AsSpan().IndexOfAny(Quoted) < 0
            ? field
            : "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    // The line breaks string.ReplaceLineEndings recognises: CR, LF, NEL, LS, PS and FF.
    private static readonly char[] LineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029', '\f'];

    // What makes a field quoted: a comma, a quote, or one of those line breaks.
    private static readonly System.Buffers.SearchValues<char> Quoted =
        System.Buffers.SearchValues.Create(",\"\r\n\u0085\u2028\u2029\f");

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

        // A message ending in a line break would otherwise end in an empty "# " line - any line break
        // ReplaceLineEndings recognises, so a trailing one of those is no "# " line either.
        return "# " + text.TrimEnd(LineBreaks).ReplaceLineEndings("\n# ") + "\n";
    }

    /// <summary>A warning as comment lines: its severity, code and message, in the invariant culture.</summary>
    /// <param name="warning">The warning.</param>
    /// <returns>The comment, every line of it a comment.</returns>
    /// <remarks>One spelling for every CSV a command writes, so the probe's profile and the surface table cannot drift apart.</remarks>
    public static string Comment(Einzel.Core.Results.ValidityWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return Comment(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{warning.Severity}: {warning.Code}: {warning.Message}"));
    }
}
