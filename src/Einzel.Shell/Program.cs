using Avalonia;

namespace Einzel.Shell;

/// <summary>The shell's entry point.</summary>
public static class Program
{
    /// <summary>
    /// Starts the window, optionally on a model named on the command line.
    /// </summary>
    /// <param name="args">
    /// The model document to open, if any. A shell started with none opens empty and
    /// waits, which is what a person double-clicking the binary gets.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>No platform is named here.</b> <c>UsePlatformDetect</c> chooses Win32, X11 or
    /// Cocoa at run time, which is the whole reason this project exists beside the WPF
    /// one: the engine runs where .NET runs and the window should too.
    /// </para>
    /// </remarks>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>The application, configured but not started.</summary>
    /// <returns>A builder the entry point starts and a designer can reuse.</returns>
    /// <remarks>
    /// Separate from <see cref="Main"/> because Avalonia's tooling looks for exactly this
    /// shape, and because a test can build the application without starting a message loop.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ShellApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
