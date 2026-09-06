using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// `einzel outline --set`, and the two ways it was wrong. Both were found by agents
/// attempting the acceptance suite rather than by anything written from inside the project,
/// because both are about what the command <em>appears</em> to do.
/// </summary>
/// <remarks>
/// CLI-4 says every mutating command takes `--dry-run` and that it writes nothing. This one
/// took the flag, ignored it, and wrote - and because it also printed nothing about writing,
/// its output was identical either way. One agent lost a model to it mid-task and blamed
/// the wrong command for three steps. A `--dry-run` that mutates is worse than none, since
/// it is the flag somebody reaches for in order to be careful.
/// </remarks>
public sealed class OutlineSetTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-outline-set", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Quadrupole()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "q.json");
        Assert.Equal(0, Run("new", path, "--from-template", "quadrupole").ExitCode);
        return path;
    }

    private static double Parameter(string path, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("parameters").GetProperty(name)
            .GetProperty("value").GetDouble();
    }

    /// <summary>A dry run changes nothing on disk, and says what it would have done.</summary>
    [Fact]
    public void ADryRunWritesNothing()
    {
        var path = Quadrupole();
        var before = File.ReadAllText(path);
        var radius = Parameter(path, "inscribedRadius");

        var (exit, _, stderr) = Run("outline", path, "--set", "inscribedRadius=7", "--dry-run");
        output.WriteLine(stderr.Trim());

        Assert.Equal(0, exit);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(radius, Parameter(path, "inscribedRadius"));

        // And it says so, rather than being silent: the flag's whole value is that a reader
        // can tell the two cases apart.
        Assert.Contains("nothing was written", stderr, StringComparison.Ordinal);
        Assert.Contains("would set inscribedRadius", stderr, StringComparison.Ordinal);
    }

    /// <summary>Writing says that it wrote, so an edit cannot pass for a read.</summary>
    [Fact]
    public void WritingSaysSo()
    {
        var path = Quadrupole();

        var (exit, stdout, stderr) = Run("outline", path, "--set", "inscribedRadius=7");
        output.WriteLine(stderr.Trim());

        Assert.Equal(0, exit);
        Assert.Equal(7.0, Parameter(path, "inscribedRadius"));
        Assert.Contains("wrote", stderr, StringComparison.Ordinal);

        // CLI-2: the table is the result and goes to stdout; what was done to the file is a
        // diagnostic and goes to stderr.
        Assert.Contains("inscribedRadius", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("wrote", stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every `--set` is applied, not the last. The option parser is a dictionary, so a
    /// repeated flag overwrote: two edits went in, one came out, exit 0, nothing said. An
    /// agent that batched two changes believed both had landed.
    /// </summary>
    [Fact]
    public void EverySetIsApplied()
    {
        var path = Quadrupole();

        var (exit, _, stderr) = Run(
            "outline", path, "--set", "inscribedRadius=7", "--set", "rodPotential=250");
        output.WriteLine(stderr.Trim());

        Assert.Equal(0, exit);
        Assert.Equal(7.0, Parameter(path, "inscribedRadius"));
        Assert.Equal(250.0, Parameter(path, "rodPotential"));
    }

    /// <summary>Chained edits start from each other, not from the file each time.</summary>
    /// <remarks>
    /// The failure this guards is subtler than dropping one: applying each edit to the
    /// file's own text would compute two complete documents from the same starting point
    /// and keep whichever was written last, which looks identical to the parser bug from
    /// the outside and needs a different fix.
    /// </remarks>
    [Fact]
    public void ThreeEditsAllSurvive()
    {
        var path = Quadrupole();

        Assert.Equal(0, Run(
            "outline", path,
            "--set", "inscribedRadius=6",
            "--set", "rodPotential=180",
            "--set", "rodRatio=1.2").ExitCode);

        Assert.Equal(6.0, Parameter(path, "inscribedRadius"));
        Assert.Equal(180.0, Parameter(path, "rodPotential"));
        Assert.Equal(1.2, Parameter(path, "rodRatio"));
    }

    /// <summary>A malformed later `--set` refuses the whole edit rather than half-applying it.</summary>
    [Fact]
    public void AMalformedSetLeavesTheModelAlone()
    {
        var path = Quadrupole();
        var before = File.ReadAllText(path);

        var (exit, _, stderr) = Run(
            "outline", path, "--set", "inscribedRadius=7", "--set", "rodPotential=banana");

        output.WriteLine(stderr.Trim());

        Assert.NotEqual(0, exit);
        Assert.Equal(before, File.ReadAllText(path));
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
