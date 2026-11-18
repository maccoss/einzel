using System.Globalization;

namespace Einzel.Cli;

/// <summary>What a command line counts as a number.</summary>
/// <remarks>
/// <para>
/// <b>One rule, asked by every place that needs it</b>: the parser, deciding whether the word
/// after a flag whose value may be left off is that value; <see cref="VerbFlags.SubVerb"/>,
/// deciding the same thing to find the sub-verb; and the readers, before they ask whether the
/// number is one the flag takes. Two copies of the rule had drifted: the parser asked for a finite
/// number and the reader for any, so <c>--progress 1e400</c> - an overflow, which parses as
/// infinity - was read as a model called 1e400 rather than refused as an interval.
/// </para>
/// <para>
/// <b>Parsing and taking are separate questions.</b> Anything that parses is a number here,
/// infinity and NaN included, so it reaches the flag's reader and is refused there by name;
/// whether it is finite, in range, or whole is the reader's.
/// </para>
/// </remarks>
public static class CommandNumber
{
    /// <summary>Whether a word parses as a number, in the invariant culture.</summary>
    /// <param name="word">The word.</param>
    /// <param name="value">The number, which may be infinite or NaN.</param>
    /// <returns>
    /// True where the whole word parses, overflow to infinity and the texts "NaN" and "Infinity"
    /// included; false for a word with leading or trailing white space.
    /// </returns>
    public static bool Parses(string word, out double value)
    {
        ArgumentNullException.ThrowIfNull(word);

        // Typed as a number means the whole word: NumberStyles.Float would take ' 5' and '5 ' too,
        // a quoted word with spaces in it that nobody typed as a number.
        if (word.Length == 0 || char.IsWhiteSpace(word[0]) || char.IsWhiteSpace(word[^1]))
        {
            value = double.NaN;
            return false;
        }

        return double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Whether a word parses as a number; <see cref="Parses(string, out double)"/> without the value.</summary>
    /// <param name="word">The word.</param>
    /// <returns>True where it parses.</returns>
    public static bool Parses(string word) => Parses(word, out _);
}
