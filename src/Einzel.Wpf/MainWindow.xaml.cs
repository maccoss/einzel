using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Einzel.Wpf;

/// <summary>The shell window.</summary>
/// <remarks>
/// Layout and input, and nothing else. Every question about what a model contains, what
/// is wrong with it, or what an edit does goes to <see cref="ModelTreeViewModel"/> and
/// through it to the command layer (UI-1).
/// </remarks>
public partial class MainWindow : Window
{
    private ModelTreeViewModel? _tree;

    /// <summary>Creates the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => Show(_tree);
    }

    /// <summary>Opens a model in the window.</summary>
    /// <param name="tree">The tree over the session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is null.</exception>
    public void Open(ModelTreeViewModel tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        _tree = tree;

        Show(tree);
    }

    private void Show(ModelTreeViewModel? tree)
    {
        if (tree is null)
        {
            return;
        }

        ParameterGrid.ItemsSource = tree.Parameters;
        JournalList.ItemsSource = tree.Journal;
        CommandList.ItemsSource = tree.Commands;

        StatusText.Text = tree.Status;

        // Visible without opening anything. A person who has to look for the fact that
        // their model no longer validates is a person who will not.
        StatusBar.Background = tree.Valid
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(0xF6, 0xE0, 0xE0));
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_tree is null
            || e.EditAction != DataGridEditAction.Commit
            || e.Row.Item is not ParameterRow row
            || e.EditingElement is not TextBox box)
        {
            return;
        }

        if (!row.Editable)
        {
            // A derived parameter's value is its expression's, so there is nothing to
            // set. Said rather than silently ignored.
            StatusText.Text =
                $"{row.Name} is derived, so its value is its expression's - "
                + "set one of the parameters the expression is over";

            e.Cancel = true;

            return;
        }

        if (!double.TryParse(
            box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            StatusText.Text =
                $"'{box.Text}' is not a number. The value is in {row.Unit}, so write the "
                + "magnitude alone";

            e.Cancel = true;

            return;
        }

        // A refusal leaves the model as it was, so the cell must not keep what was
        // typed - two different values for one parameter with nothing saying which is
        // real is exactly what a model tree exists to prevent. `Status` already carries
        // AGT-3's explanation by the time this returns.
        if (!_tree.Set(row.Name, value))
        {
            e.Cancel = true;
        }

        // Re-read rather than trust the grid: an edit moves every derived parameter that
        // depends on it, which is the whole reason a model has a parameter surface.
        Dispatcher.BeginInvoke(() => Show(_tree));
    }
}
