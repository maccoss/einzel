using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Einzel.Shell;

/// <summary>The application object: theme, and the window it opens.</summary>
public sealed class ShellApp : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    /// <remarks>
    /// The model path arrives as the first argument, which is what a file association and
    /// a terminal both hand over. A shell started with none opens empty rather than
    /// refusing: the window is where somebody goes to look at a model they have not named
    /// yet.
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = desktop.Args is { Length: > 0 } args ? args[0] : null;
            desktop.MainWindow = new MainWindow(model);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
