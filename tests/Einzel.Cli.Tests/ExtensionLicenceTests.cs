using System.Text.Json.Nodes;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// LIC-2: extensions carry their own licences and the manager surfaces them.
/// </summary>
/// <remarks>
/// <para>
/// An extension is third-party code run against this engine, and LIC-1 is absolute about
/// what may enter the default build. Somebody deciding whether to install one needs to
/// know what it is offered under <i>before</i> they install it. The manifest carried trust
/// level, versions and a compatible range, and nothing about the licence.
/// </para>
/// <para>
/// <b>The load-bearing half is the undeclared case.</b> A missing licence is exactly where
/// care is needed, so it is reported as missing rather than omitted from the line —
/// otherwise the one extension worth asking about looks like the ones that answered.
/// </para>
/// </remarks>
public sealed class ExtensionLicenceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-licence", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>Registers one extension and returns its manifest path.</summary>
    private string Register(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var registered = Cli("ext", "register", name, "--project", _root);

        output.WriteLine(registered.Stdout.Trim());

        Assert.Equal(0, registered.ExitCode);

        var manifest = Directory
            .EnumerateFiles(Path.Combine(_root, "extensions"), "*.json", SearchOption.AllDirectories)
            .Single();

        return manifest;
    }

    /// <summary>A scaffolded extension declares a licence from the first minute.</summary>
    /// <remarks>
    /// The same argument as <c>einzel init</c> writing a model that runs: a field that has
    /// to be added later is one that gets left out. An author who wants something other
    /// than the repository's own licence edits one line, which is a better prompt than an
    /// absence.
    /// </remarks>
    [Fact]
    public void AScaffoldedExtensionDeclaresItsLicence()
    {
        Register("declared");

        var listed = Cli("ext", "list", "--project", _root, "--json");

        Assert.Equal(0, listed.ExitCode);

        var licence = JsonNode.Parse(listed.Stdout)!["extensions"]![0]!["licence"]?.GetValue<string>();

        output.WriteLine($"scaffolded licence: {licence}");

        Assert.Equal("Apache-2.0", licence);
    }

    /// <summary>An extension that does not say is reported as not saying.</summary>
    /// <remarks>
    /// <para>
    /// <b>Null in the machine-readable form and words in the human one.</b> A placeholder
    /// would let a caller mistake "did not declare one" for a licence it recognises, which
    /// is the failure this field exists to prevent — and it is the same rule this engine
    /// applies to an undefined measurement, mattering more here because the reader cannot
    /// recompute the answer for themselves.
    /// </para>
    /// <para>
    /// The line has to say it too. Omitting the licence where there is none would make the
    /// undeclared extension the only one whose line is short, which is a difference nobody
    /// reads as a warning.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnExtensionThatDeclaresNoLicenceSaysSoRatherThanLookingLikeTheOthers()
    {
        var manifest = Register("silent");

        var original = File.ReadAllText(manifest);

        var document = JsonNode.Parse(original)!.AsObject();

        // Asserted, because a replacement that matched nothing would leave the scaffolded
        // licence in place and this test would pass for the wrong reason.
        Assert.True(document.Remove("licence"), "the scaffold should have written one to remove");

        File.WriteAllText(manifest, document.ToJsonString());

        var json = Cli("ext", "list", "--project", _root, "--json");
        var human = Cli("ext", "list", "--project", _root);

        var line = human.Stdout
            .Split('\n')
            .Single(l => l.Contains("silent", StringComparison.Ordinal));

        output.WriteLine(line.Trim());

        // Absent, not a placeholder.
        Assert.Null(JsonNode.Parse(json.Stdout)!["extensions"]![0]!["licence"]);

        // And visible, on the same line and in the same place as a declared one.
        Assert.Contains("NOT DECLARED", line, StringComparison.Ordinal);
    }

    /// <summary>The two cases are told apart on the surface a person reads.</summary>
    /// <remarks>
    /// The control. Both halves above could pass with a renderer that printed the same
    /// thing for every extension; what LIC-2 needs is that a reader can tell which is
    /// which without opening the manifest.
    /// </remarks>
    [Fact]
    public void ADeclaredLicenceAndAMissingOneReadDifferently()
    {
        var manifest = Register("subject");

        var declared = Cli("ext", "list", "--project", _root).Stdout;

        var document = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();

        Assert.True(document.Remove("licence"));

        File.WriteAllText(manifest, document.ToJsonString());

        var undeclared = Cli("ext", "list", "--project", _root).Stdout;

        output.WriteLine($"declared:   {declared.Split('\n').Single(l => l.Contains("subject", StringComparison.Ordinal)).Trim()}");
        output.WriteLine($"undeclared: {undeclared.Split('\n').Single(l => l.Contains("subject", StringComparison.Ordinal)).Trim()}");

        Assert.NotEqual(declared, undeclared);

        Assert.Contains("Apache-2.0", declared, StringComparison.Ordinal);
        Assert.DoesNotContain("Apache-2.0", undeclared, StringComparison.Ordinal);
    }
}
