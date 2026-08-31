using System.IO;
using System.Windows;

using Einzel.Commands;
using Einzel.Core.Errors;

namespace Einzel.Wpf;

/// <summary>The shell application.</summary>
/// <remarks>
/// UI-1: this owns layout, input, the interactive viewport and the update check. It owns
/// no physics, no validation rules, no file format knowledge and no render output.
/// </remarks>
public partial class App : System.Windows.Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();

        window.Show();

        if (e.Args.Length == 0)
        {
            // Said rather than shown as an empty window, which is indistinguishable from
            // a model with nothing in it. AGT-3's shape applies to the shell too: what is
            // wrong, and what to do about it.
            window.Refuse(
                "no model. Open the shell with one: einzel-shell models/reflectron.json");

            return;
        }

        Open(window, e.Args[0]);
    }

    /// <summary>Opens a model, or says why it could not be.</summary>
    /// <remarks>
    /// <para>
    /// <b>Who is at the keyboard is declared here and nowhere else.</b> MCP-1's
    /// attribution comes from the party rather than from an argument on each action, for
    /// the same reason the MCP server takes it from the <c>initialize</c> handshake: an
    /// author supplied per action is a signature, and a signature can be filled in with
    /// somebody else's name.
    /// </para>
    /// <para>
    /// The name is the operating-system user, because the shell is a person's window on
    /// their own machine and asking would be ceremony. What matters to the journal is
    /// that a human's edits and an agent's are told apart, which
    /// <see cref="AuthorKind.Human"/> carries.
    /// </para>
    /// </remarks>
    private static void Open(MainWindow window, string modelPath)
    {
        try
        {
            var session = new ShellSession(
                modelPath, new JournalAuthor(Environment.UserName, AuthorKind.Human));

            window.Open(
                new ModelTreeViewModel(session),
                new ViewportViewModel(session),
                new ResultsViewModel(session),
                new RegimeViewModel(session),
                new SequenceViewModel(session),
                new ProjectViewModel(session));
        }
        catch (EinzelException refusal)
        {
            window.Refuse(
                refusal.Error.Constraint
                + (refusal.Error.Suggestion is { } how ? $" - {how}" : string.Empty));
        }
        catch (IOException missing)
        {
            window.Refuse(missing.Message);
        }
    }
}
