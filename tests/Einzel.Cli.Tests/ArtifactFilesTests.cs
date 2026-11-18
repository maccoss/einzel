using Einzel.Commands;
using Einzel.Core.Errors;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// How a file a command was asked for is put in place: in one step, through a link rather than
/// over it, never over a read-only file, and refused where the user can act on the refusal.
/// </summary>
/// <remarks>
/// Each test was written against a defect a review found in the first version, which moved the
/// old file aside and then moved the new one in: a kill between the two left no file at all and
/// a stray <c>.bak</c>, a symbolic link was replaced by an ordinary file, a read-only file was
/// replaced as though it were not, and a file nobody named by a flag was refused at a flag.
/// </remarks>
public sealed class ArtifactFilesTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-artifacts", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// A write that fails part way for any reason - not only the file system's - puts back what
    /// was there and leaves nothing beside it.
    /// </summary>
    /// <remarks>
    /// The rollback caught only file-system exceptions; anything else skipped it, and the backups
    /// were deleted on the way out, so the first file stood replaced and its original was lost.
    /// </remarks>
    [Fact]
    public void AnyFailurePartWayPutsBackWhatWasThere()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.png");
        var second = Path.Combine(_root, "second.csv");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        ArtifactFiles.BeforePlacing.Value = index =>
        {
            if (index == 1)
            {
                throw new InvalidOperationException("failed on purpose");
            }
        };

        try
        {
            Assert.Throws<InvalidOperationException>(() => ArtifactFiles.WriteAll([("out", first, [8]), ("csv", second, [9])]));
        }
        finally
        {
            ArtifactFiles.BeforePlacing.Value = null;
        }

        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2], File.ReadAllBytes(second));
        Assert.Equal(["first.png", "second.csv"], Directory.GetFiles(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>A read-only destination is refused and left as it was.</summary>
    [Fact]
    public void AReadOnlyDestinationIsRefusedAndKept()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "kept.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var refusal = Assert.Throws<EinzelException>(() => ArtifactFiles.Write("out", path, [9]));

        output.WriteLine(refusal.Error.Constraint);

        Assert.Equal("/args/--out", refusal.Error.Path);
        Assert.Contains("read-only", refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.Equal(["kept.png"], Directory.GetFiles(_root).Select(Path.GetFileName));

        // In a pair, too: the first, writable, is not replaced either.
        var first = Path.Combine(_root, "first.png");
        File.WriteAllBytes(first, [4]);

        Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([("out", first, [8]), ("csv", path, [9])]));
        Assert.Equal([4], File.ReadAllBytes(first));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    /// <summary>A destination that is a symbolic link is written through: the link stays a link.</summary>
    [Fact]
    public void ALinkIsWrittenThroughNotReplaced()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "real.png");
        var link = Path.Combine(_root, "link.png");
        File.WriteAllBytes(target, [1]);

        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Windows without developer mode cannot make one; Linux CI always can.
            output.WriteLine($"no symbolic link here ({failure.Message}); nothing to check");
            return;
        }

        ArtifactFiles.Write("out", link, [7]);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal([7], File.ReadAllBytes(target));
        Assert.Equal([7], File.ReadAllBytes(link));

        // Two files, one a link: both written, the link still a link, nothing left beside them.
        var other = Path.Combine(_root, "other.csv");
        File.WriteAllBytes(other, [2]);
        ArtifactFiles.WriteAll([("out", link, [5]), ("csv", other, [6])]);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal([5], File.ReadAllBytes(target));
        Assert.Equal([6], File.ReadAllBytes(other));
        Assert.Equal(
            ["link.png", "other.csv", "real.png"],
            Directory.GetFiles(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A destination that is a link to a file the command reads is that file, and two
    /// destinations that are one file through a link are one file.
    /// </summary>
    /// <remarks>
    /// The writes followed the link while the checks compared names, so <c>--out link.png</c>,
    /// where <c>link.png</c> names the model, passed the check and replaced the model. Needs a
    /// symbolic link: on Windows without developer mode there is none to make and the test says
    /// so and checks nothing; Linux CI always runs it.
    /// </remarks>
    [Fact]
    public void ALinkToAnInputIsThatInput()
    {
        Directory.CreateDirectory(_root);
        var model = Path.Combine(_root, "model.json");
        var link = Path.Combine(_root, "link.png");
        var other = Path.Combine(_root, "other.csv");
        File.WriteAllText(model, "{}");

        try
        {
            File.CreateSymbolicLink(link, model);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"no symbolic link here ({failure.Message}); nothing to check");
            return;
        }

        var refusal = Assert.Throws<EinzelException>(() => ArtifactFiles.Write("out", link, [1], model));
        Assert.Equal("/args/--out", refusal.Error.Path);
        Assert.Equal("{}", File.ReadAllText(model));

        // Two outputs that are one file through a link.
        File.CreateSymbolicLink(other, Path.Combine(_root, "real.csv"));
        var both = Assert.Throws<EinzelException>(
            () => ArtifactFiles.WriteAll([("out", Path.Combine(_root, "real.csv"), [1]), ("csv", other, [2])]));
        Assert.Equal("/args/--csv", both.Error.Path);
    }

    /// <summary>
    /// A replaced file is never absent: a reader watching the destination through the whole write
    /// always finds a file there, old or new.
    /// </summary>
    /// <remarks>
    /// The destination was moved aside and the new file moved in after it, so between the two
    /// there was nothing there - which a process killed at that moment left for good.
    /// </remarks>
    [Fact]
    public void AReplacedFileIsNeverAbsent()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "watched.bin");
        var content = new byte[1 << 16];
        File.WriteAllBytes(path, content);

        using var stop = new CancellationTokenSource();
        var absent = 0;

        var watcher = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                // By listing the folder, which opens no file: asking the file itself opens it for
                // an instant, and on Windows a rename onto an open file is refused.
                if (!Directory.EnumerateFiles(_root, "watched.bin").Any())
                {
                    Interlocked.Increment(ref absent);
                }
            }
        });

        watcher.Start();

        for (var i = 0; i < 300; i++)
        {
            ArtifactFiles.Write("out", path, content);
        }

        stop.Cancel();
        watcher.Join();

        output.WriteLine($"absent {absent} time(s) in 300 replacements");

        Assert.Equal(0, absent);
        Assert.Equal(["watched.bin"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    /// <summary>
    /// A file the user named by no flag - a default destination, a file in the project's scratch
    /// folder - is refused with the file in the message and no flag pointer.
    /// </summary>
    [Fact]
    public void AFileNamedByNoFlagIsRefusedAtTheFileNotAFlag()
    {
        Directory.CreateDirectory(Path.Combine(_root, "in-the-way"));

        var refusal = Assert.Throws<EinzelException>(
            () => ArtifactFiles.Write(null, Path.Combine(_root, "in-the-way"), [1]));

        output.WriteLine($"{refusal.Error.Path}: {refusal.Error.Constraint}; {refusal.Error.Suggestion}");

        Assert.Equal("/", refusal.Error.Path);
        Assert.Contains(Path.Combine(_root, "in-the-way"), refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.DoesNotContain("--", refusal.Error.Suggestion, StringComparison.Ordinal);
    }

    /// <summary>
    /// Export writes its mesh into the project's scratch folder, a file no flag named: where it
    /// cannot be written the refusal names that file, not <c>--project</c>.
    /// </summary>
    [Fact]
    public void AnExportThatCannotWriteNamesTheFile()
    {
        var project = Path.Combine(_root, "project");
        var model = Path.Combine(_root, "slit.json");
        Directory.CreateDirectory(_root);
        Assert.Equal(0, Program.Main(["new", model, "--from-example", "slit-transmission"]));

        // Where the mesh goes, a folder.
        var blocked = Path.Combine(project, ".einzel", "slit.conductors.obj");
        Directory.CreateDirectory(blocked);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (previousOut, previousError) = (Console.Out, Console.Error);
        int code;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            code = Program.Main(["export", model, "--mesh", "--project", project, "--json"]);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        output.WriteLine(stdout.ToString());

        Assert.Equal(1, code);
        using var document = System.Text.Json.JsonDocument.Parse(stdout.ToString());
        var error = document.RootElement.GetProperty("errors")[0];

        Assert.Equal("/", error.GetProperty("path").GetString());
        Assert.Contains("slit.conductors.obj", error.GetProperty("constraint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("--project", error.GetProperty("suggestion").GetString(), StringComparison.Ordinal);
    }
}
