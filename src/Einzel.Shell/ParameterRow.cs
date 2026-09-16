using System.Globalization;

using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>One row of the model tree, as the window shows it.</summary>
/// <param name="Name">The parameter's name.</param>
/// <param name="Shown">What it holds, already formatted.</param>
/// <param name="Unit">Its unit, as the document declares it.</param>
/// <param name="Description">What the template says it is for.</param>
/// <remarks>
/// <para>
/// <b>Formatting is the shell's, the numbers are not.</b> Every field here comes from
/// <c>einzel outline</c>: UI-1 puts file format knowledge outside the window, so a shell
/// that parsed the document to build this list would grow its own idea of what a model
/// means and the two would come to disagree.
/// </para>
/// <para>
/// <b>A derived parameter shows its expression rather than its number</b>, because that is
/// what it is: the template's whole point is that <c>foilThickness</c> is <c>halfGap -
/// foilGap</c> and moves when either does. Showing only the resolved number would hide the
/// one thing a reader needs in order to know which knob to turn - and this project has a
/// scar from exactly that, where a stripe's thickness was a second knob that could disagree
/// with the geometry it was supposed to follow.
/// </para>
/// </remarks>
public sealed record ParameterRow(string Name, string Shown, string Unit, string? Description)
{
    /// <summary>Formats one parameter of a model's declared surface.</summary>
    /// <param name="outline">The parameter as the command layer resolved it.</param>
    /// <returns>A row the window can show.</returns>
    public static ParameterRow From(ParameterOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var shown = outline.Expression is { Length: > 0 } expression
            ? expression
            : outline.Value is { } value
                ? value.ToString("G6", CultureInfo.InvariantCulture)
                : "-";

        return new ParameterRow(
            outline.Name,
            shown,
            outline.Unit ?? string.Empty,
            outline.Description);
    }
}
