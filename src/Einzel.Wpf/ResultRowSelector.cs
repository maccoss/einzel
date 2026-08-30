using System.Windows;
using System.Windows.Controls;

namespace Einzel.Wpf;

/// <summary>Picks the template for a results row by what the row is.</summary>
/// <remarks>
/// <para>
/// The results list holds two kinds of thing - a class heading and a figure - and choosing
/// between them by type is what lets each be exactly its own shape. The alternative is one
/// row type carrying a flag and both sets of fields, half of them empty on every row, which
/// is how a heading ends up with an uncertainty column.
/// </para>
/// <para>
/// A row of neither kind falls back to whatever the list would have done, rather than
/// throwing: a selector is presentation, and presentation failing loudly in the middle of a
/// results view would hide the results to report its own problem.
/// </para>
/// </remarks>
public sealed class ResultRowSelector : DataTemplateSelector
{
    /// <summary>The template for a class heading.</summary>
    public DataTemplate? Header { get; set; }

    /// <summary>The template for a figure.</summary>
    public DataTemplate? Figure { get; set; }

    /// <inheritdoc/>
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item switch
        {
            FigureClassHeader => Header,
            FigureRow => Figure,
            _ => base.SelectTemplate(item, container),
        };
}
