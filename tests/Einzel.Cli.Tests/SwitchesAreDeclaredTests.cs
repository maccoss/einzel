using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The command line's switches - flags that never take a value - are every switch any verb
/// reads, and no others.
/// </summary>
/// <remarks>
/// The parser cannot tell a switch from an option by looking at it, so
/// <see cref="CommandLine.Switches"/> is the only thing that stops a switch swallowing the next
/// argument, in every verb. This reads the verbs' own source.
/// </remarks>
public sealed class SwitchesAreDeclaredTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every flag a verb reads with <c>Has</c> alone is a switch, and the parser knows it.
    /// </summary>
    /// <remarks>
    /// The parser cannot tell a switch from an option by looking at it, so the list is the
    /// only thing that stops a switch swallowing the next argument; this reads the verbs' own
    /// source and fails when a switch is added to a verb and not to the list.
    /// </remarks>
    [Fact]
    public void EverySwitchAVerbReadsIsDeclared()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "Einzel.Cli", "Program.cs")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var source = string.Concat(Directory.GetFiles(Path.Combine(directory!.FullName, "src", "Einzel.Cli"), "*.cs").Select(File.ReadAllText));
        var has = System.Text.RegularExpressions.Regex.Matches(source, @"\.Has\(""([a-z0-9-]+)""\)").Select(m => m.Groups[1].Value).ToHashSet();
        var valued = System.Text.RegularExpressions.Regex.Matches(source, @"(?:\.Values?\(|(?:TryNumberFlag|TryWholeFlag)\(options, )""([a-z0-9-]+)""").Select(m => m.Groups[1].Value)
            .Concat(System.Text.RegularExpressions.Regex.Matches(source, @"MissingValue\(options, ""[^""]*"", ([^)]*)\)").SelectMany(m => System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"""([a-z0-9-]+)""").Select(n => n.Groups[1].Value)))
            .ToHashSet();

        // Read as a value under a name chosen at run time, or refused by name for having no unit.
        string[] notSwitches = ["at-mm", "line-mm", "at", "line", "to", "offset", "box", "height-mm"];

        var switches = has.Except(valued).Except(notSwitches).Order().ToList();
        output.WriteLine("switches: " + string.Join(", ", switches));

        Assert.True(switches.Count >= 8, "the control: the scan found the switches");
        Assert.Empty(switches.Except(CommandLine.Switches));
        Assert.Empty(CommandLine.Switches.Except(switches));
    }

}
