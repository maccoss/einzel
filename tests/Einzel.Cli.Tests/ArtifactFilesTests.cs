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

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "place" && index == 1)
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
            ArtifactFiles.Failing.Value = null;
        }

        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2], File.ReadAllBytes(second));
        Assert.Equal(["first.png", "second.csv"], Directory.GetFiles(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A failed set leaves no folder it made, and stages each file under a short name beside its
    /// destination rather than the destination's name with a GUID appended.
    /// </summary>
    /// <remarks>
    /// The rollback put the files back and left the folders it had made; and a name of 37 more
    /// characters than the destination's could take a path near the platform's limit past it.
    /// </remarks>
    [Fact]
    public void AFailedSetLeavesNoFolderAndStagesUnderShortNames()
    {
        Directory.CreateDirectory(_root);
        var deep = Path.Combine(_root, "made", "deeper");
        var first = Path.Combine(deep, "a-rather-long-picture-name.png");
        var second = Path.Combine(deep, "a-rather-long-table-name.csv");
        var staged = new List<string>();

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "place" && index == 1)
            {
                staged.AddRange(Directory.GetFiles(deep).Select(Path.GetFileName)!);
                throw new IOException("failed on purpose");
            }
        };

        try
        {
            Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([("out", first, [1]), ("csv", second, [2])]));
        }
        finally
        {
            ArtifactFiles.Failing.Value = null;
        }

        output.WriteLine(string.Join(", ", staged));

        Assert.NotEmpty(staged);
        Assert.All(staged.Where(n => n.EndsWith(".tmp", StringComparison.Ordinal)), n => Assert.Equal(13, n.Length));
        Assert.False(Directory.Exists(Path.Combine(_root, "made")), "a folder the failed write made was left behind");
    }

    /// <summary>
    /// An export of several channels is written all together or not at all: a failure part way
    /// leaves the earlier export's channel files as they were, none mixed in.
    /// </summary>
    [Fact]
    public void AnExportOfSeveralChannelsIsWrittenWholeOrNotAtAll()
    {
        var project = Path.Combine(_root, "project");
        var model = Path.Combine(_root, "funnel.json");
        Directory.CreateDirectory(_root);
        Assert.Equal(0, Program.Main(["new", model, "--from-template", "ion-funnel"]));
        Assert.Equal(0, Program.Main(["export", model, "--project", project]));

        var files = Directory.GetFiles(Path.Combine(project, ".einzel"), "funnel*.vti").Order(StringComparer.Ordinal).ToArray();
        Assert.True(files.Length > 1, $"{files.Length} channel file(s): the template should export several");

        foreach (var file in files)
        {
            File.WriteAllText(file, "earlier export");
        }

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "place" && index == 1)
            {
                throw new IOException("failed on purpose");
            }
        };

        try
        {
            Assert.Equal(1, Program.Main(["export", model, "--project", project]));
        }
        finally
        {
            ArtifactFiles.Failing.Value = null;
        }

        Assert.All(files, f => Assert.Equal("earlier export", File.ReadAllText(f)));
        Assert.Equal(files, Directory.GetFiles(Path.Combine(project, ".einzel"), "funnel*.vti").Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A replace that fails after moving the original to its backup name leaves the target empty;
    /// the original is put back, not left as a stray backup beside a refusal claiming all or none.
    /// </summary>
    /// <remarks>
    /// On Windows File.Replace is two moves, and ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 is the failure
    /// between them. The target was added to what had been placed only after Replace returned, so
    /// the rollback did not know it existed. Simulated here: the replace succeeds, the target is
    /// then removed - the state that failure leaves - and the set fails.
    /// </remarks>
    [Fact]
    public void AReplaceThatFailsHalfWayPutsTheOriginalBack()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.vti");
        var second = Path.Combine(_root, "second.vti");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "replaced" && index == 1)
            {
                File.Delete(second);
                throw new IOException("unable to move the replacement file to the file to be replaced");
            }
        };

        try
        {
            Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([(null, first, [8]), (null, second, [9])]));
        }
        finally
        {
            ArtifactFiles.Failing.Value = null;
        }

        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2], File.ReadAllBytes(second));
        Assert.Equal(["first.vti", "second.vti"], Directory.GetFiles(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A failure after a replace has succeeded - the target holding the new bytes, the original
    /// under its backup name - puts the original back: the entry is registered the moment the
    /// replace returns.
    /// </summary>
    [Fact]
    public void AFailureAfterAReplacePutsTheOriginalBack()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.vti");
        var second = Path.Combine(_root, "second.vti");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "replaced" && index == 1)
            {
                throw new InvalidOperationException("failed on purpose, with the new file in place");
            }
        };

        try
        {
            Assert.ThrowsAny<Exception>(() => ArtifactFiles.WriteAll([(null, first, [8]), (null, second, [9])]));
        }
        finally
        {
            ArtifactFiles.Failing.Value = null;
        }

        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2], File.ReadAllBytes(second));
        Assert.Equal(["first.vti", "second.vti"], Directory.GetFiles(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// An original that cannot be put back is not swallowed: the refusal names where it now is,
    /// and it is there.
    /// </summary>
    [Fact]
    public void AnOriginalThatCannotGoBackIsNamed()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.vti");
        var second = Path.Combine(_root, "second.vti");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "replaced" && index == 1)
            {
                // Something now stands where the original would go back: a folder, which no
                // rename of a file can replace on any platform.
                File.Delete(second);
                Directory.CreateDirectory(second);
                throw new IOException("failed on purpose");
            }
        };

        EinzelException refusal;

        try
        {
            refusal = Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([(null, first, [8]), (null, second, [9])]));
        }
        finally
        {
            ArtifactFiles.Failing.Value = null;
        }

        output.WriteLine(refusal.Error.Constraint);

        var backup = Directory.GetFiles(_root, "*.bak").Single();

        Assert.Contains($"the original of {second} could not be put back and is at {backup}", refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.Equal([2], File.ReadAllBytes(backup));
        Assert.Equal([1], File.ReadAllBytes(first));

        Directory.Delete(second);
    }

    /// <summary>
    /// A replace that fails leaving a backup: a backup that copies the intact target is removed;
    /// one that differs is put back over the target, which most likely holds the new bytes; and
    /// where that cannot be done, the refusal names the target and where its original is.
    /// </summary>
    /// <remarks>
    /// A differing backup was named and left, with the new bytes standing at the target and the
    /// refusal not naming the target at all.
    /// </remarks>
    [Theory]
    [InlineData("copy")]
    [InlineData("original")]
    [InlineData("blocked")]
    [InlineData("unreadable")]
    public void AFailedReplaceLeavingABackupIsResolved(string left)
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.vti");
        var second = Path.Combine(_root, "second.vti");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);
        string? backupAt = null;

        ArtifactFiles.Replacing.Value = (target, backup) =>
        {
            if (target != second)
            {
                return;
            }

            backupAt = backup;

            switch (left)
            {
                case "copy":
                    File.WriteAllBytes(backup, [2]);
                    break;
                case "original":
                    // As a replace failing half way leaves it: the original under the backup
                    // name, and the new bytes at the target.
                    File.Move(target, backup);
                    File.WriteAllBytes(target, [9]);
                    break;
                case "unreadable":
                    // A backup beside an untouched target that cannot be compared with it - a
                    // file locked against reading. Whether it was replaced cannot be told.
                    File.WriteAllBytes(backup, [7]);
                    ArtifactFiles.Comparing.Value = (_, _) => throw new IOException("locked");
                    break;
                default:
                    // And with something at the target no rename of a file can replace.
                    File.Move(target, backup);
                    Directory.CreateDirectory(target);
                    break;
            }

            throw new IOException("the replace failed part way");
        };

        EinzelException refusal;

        try
        {
            refusal = Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([(null, first, [8]), (null, second, [9])]));
        }
        finally
        {
            ArtifactFiles.Replacing.Value = null;
            ArtifactFiles.Comparing.Value = null;
        }

        output.WriteLine(refusal.Error.Constraint);
        output.WriteLine(refusal.Error.Suggestion);

        if (left == "unreadable")
        {
            // Nothing put back anywhere in the set, and every backup kept: restoring the first file
            // while the second may hold the new bytes would leave a mixed set and call it rolled
            // back. Each file's state is said, with advice that fits it.
            Assert.Equal([2], File.ReadAllBytes(second));
            Assert.Equal([7], File.ReadAllBytes(backupAt!));
            Assert.Equal([8], File.ReadAllBytes(first));

            var firstOriginal = Directory.GetFiles(_root, "*.bak").Single(b => b != backupAt);
            Assert.Equal([1], File.ReadAllBytes(firstOriginal));

            Assert.Contains($"it could not be told whether {second} was replaced: a copy of its original is at {backupAt}", refusal.Error.Constraint, StringComparison.Ordinal);
            Assert.Contains($"{first} holds the new file and its original is at {firstOriginal}", refusal.Error.Constraint, StringComparison.Ordinal);
            // And the new version of the uncertain file is kept - the only copy of what the
            // command made for it - beside it, under a name that says what it is.
            Assert.Equal([9], File.ReadAllBytes(second + ".new"));
            Assert.Contains($"the new version of {second} is at {second}.new", refusal.Error.Constraint, StringComparison.Ordinal);
            Assert.Contains("move a new version over the file it is named after to complete the set", refusal.Error.Suggestion, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
            Assert.Contains("compare a file with the copy named beside it", refusal.Error.Suggestion, StringComparison.Ordinal);
            Assert.Contains("move an original back over the file named beside it to undo it", refusal.Error.Suggestion, StringComparison.Ordinal);
            return;
        }

        Assert.Equal([1], File.ReadAllBytes(first));

        if (left == "blocked")
        {
            Assert.Contains($"the original of {second} could not be put back and is at {backupAt}", refusal.Error.Constraint, StringComparison.Ordinal);
            Assert.Contains("move an original back over the file named beside it", refusal.Error.Suggestion, StringComparison.Ordinal);
            Assert.DoesNotContain("compare", refusal.Error.Suggestion, StringComparison.Ordinal);
            Assert.Equal([2], File.ReadAllBytes(backupAt!));
            Directory.Delete(second);
            return;
        }

        Assert.Equal([2], File.ReadAllBytes(second));
        Assert.Equal(["first.vti", "second.vti"], Directory.GetFiles(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Where a new version cannot be kept aside, for any reason, the refusal still reports every
    /// file and says why that one was not kept and where its bytes are.
    /// </summary>
    /// <remarks>
    /// Keeping aside caught only the file system's own failures; anything else escaped the catch
    /// that was writing the report, and the report of every file went with it.
    /// </remarks>
    [Fact]
    public void ANewVersionThatCannotBeKeptIsReportedNotRaised()
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.vti");
        var second = Path.Combine(_root, "second.vti");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);
        string? backupAt = null;

        ArtifactFiles.Replacing.Value = (target, backup) =>
        {
            if (target != second)
            {
                return;
            }

            backupAt = backup;
            File.WriteAllBytes(backup, [7]);
            ArtifactFiles.Comparing.Value = (_, _) => throw new IOException("locked");
            throw new IOException("the replace failed part way");
        };
        ArtifactFiles.Keeping.Value = (_, _) => throw new InvalidOperationException("not a file system failure");

        EinzelException refusal;

        try
        {
            refusal = Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([(null, first, [8]), (null, second, [9])]));
        }
        finally
        {
            ArtifactFiles.Replacing.Value = null;
            ArtifactFiles.Comparing.Value = null;
            ArtifactFiles.Keeping.Value = null;
        }

        output.WriteLine(refusal.Error.Constraint);
        var constraint = refusal.Error.Constraint;

        // Every file's state is still said.
        Assert.Contains($"it could not be told whether {second} was replaced: a copy of its original is at {backupAt}", constraint, StringComparison.Ordinal);
        Assert.Contains($"{first} holds the new file and its original is at", constraint, StringComparison.Ordinal);

        // And why the new version is not beside its destination, and where it is instead.
        const string Lead = "could not keep the new version of ";
        Assert.Contains($"{Lead}{second}: not a file system failure; it is still at ", constraint, StringComparison.Ordinal);
        Assert.False(File.Exists(second + ".new"));
        var still = constraint[(constraint.IndexOf("it is still at ", StringComparison.Ordinal) + "it is still at ".Length)..].Split(';')[0].Trim();
        Assert.Equal([9], File.ReadAllBytes(still));
        File.Delete(still);
    }

    // The second of two files fails to replace in a way that cannot be told, so the new versions
    // are kept aside; keeping runs through the given hook. The refusal, and the second file's path.
    private (EinzelException Refusal, string Second) KeptAside(Action<string, string> keeping)
    {
        Directory.CreateDirectory(_root);
        var first = Path.Combine(_root, "first.vti");
        var second = Path.Combine(_root, "second.vti");
        File.WriteAllBytes(first, [1]);
        File.WriteAllBytes(second, [2]);

        ArtifactFiles.Replacing.Value = (target, backup) =>
        {
            if (target != second)
            {
                return;
            }

            File.WriteAllBytes(backup, [7]);
            ArtifactFiles.Comparing.Value = (_, _) => throw new IOException("locked");
            throw new IOException("the replace failed part way");
        };
        ArtifactFiles.Keeping.Value = keeping;

        try
        {
            var refusal = Assert.Throws<EinzelException>(() => ArtifactFiles.WriteAll([(null, first, [8]), (null, second, [9])]));
            output.WriteLine(refusal.Error.Constraint);
            output.WriteLine(refusal.Error.Suggestion);
            return (refusal, second);
        }
        finally
        {
            ArtifactFiles.Replacing.Value = null;
            ArtifactFiles.Comparing.Value = null;
            ArtifactFiles.Keeping.Value = null;
        }
    }

    /// <summary>
    /// A name taken between the look and the move is passed over for the next one, as a name
    /// already taken is.
    /// </summary>
    /// <remarks>Keeping gave up at the first failed move, leaving the new version at a staging name.</remarks>
    [Fact]
    public void ANameTakenUnderTheMoveIsPassedOver()
    {
        var (refusal, second) = KeptAside((_, kept) =>
        {
            if (kept.EndsWith(".new", StringComparison.Ordinal))
            {
                File.WriteAllBytes(kept, [5]);
                throw new IOException("taken under the move");
            }
        });

        Assert.Equal([5], File.ReadAllBytes(second + ".new"));
        Assert.Equal([9], File.ReadAllBytes(second + ".new2"));
        Assert.Contains($"the new version of {second} is at {second}.new2", refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.DoesNotContain("could not keep", refusal.Error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where every name is taken, the refusal says where the new version is - its staging name -
    /// and its advice does not send anyone to a file named after the target.
    /// </summary>
    [Fact]
    public void ANewVersionLeftAtItsStagingNameIsSaidToBeThere()
    {
        var (refusal, second) = KeptAside((_, kept) =>
        {
            File.WriteAllBytes(kept, [5]);
            throw new IOException("taken under the move");
        });

        const string Still = "it is still at ";
        var constraint = refusal.Error.Constraint;
        Assert.Contains($"could not keep the new version of {second}: every name from .new to .new999 is taken; {Still}", constraint, StringComparison.Ordinal);
        var staged = constraint[(constraint.IndexOf(Still, StringComparison.Ordinal) + Still.Length)..].Split(';')[0].Trim();
        Assert.Equal([9], File.ReadAllBytes(staged));
        Assert.Contains($"the new version of {second} is at {staged}", constraint, StringComparison.Ordinal);
        Assert.DoesNotContain("named after", refusal.Error.Suggestion, StringComparison.Ordinal);
        Assert.Contains("from where the refusal says it is", refusal.Error.Suggestion, StringComparison.Ordinal);

        foreach (var taken in Directory.GetFiles(_root, "second.vti.new*"))
        {
            File.Delete(taken);
        }

        File.Delete(staged);
    }

    /// <summary>
    /// A set staged one file at a time puts nothing in place until it is committed, and a set
    /// abandoned part way leaves nothing behind: no file, no staging file, no folder it made.
    /// </summary>
    [Fact]
    public void AStagedSetIsInPlaceOnlyWhenCommitted()
    {
        var folder = Path.Combine(_root, "made");
        var first = Path.Combine(folder, "a.vti");
        var second = Path.Combine(folder, "b.vti");

        using (var set = ArtifactFiles.Begin())
        {
            set.Stage(null, first, s => s.Write([1]));
            set.Stage(null, second, s => s.Write([2]));

            Assert.False(File.Exists(first));
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }

        // Abandoned: the staging files and the folder made for them are gone.
        Assert.False(Directory.Exists(folder));

        using (var set = ArtifactFiles.Begin())
        {
            set.Stage(null, first, s => s.Write([1]));
            set.Stage(null, second, s => s.Write([2]));
            set.Commit();
        }

        Assert.Equal([1], File.ReadAllBytes(first));
        Assert.Equal([2], File.ReadAllBytes(second));
        Assert.Equal(["a.vti", "b.vti"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // A refusal part way abandons the set: the file staged before it is not left on disk.
        using (var set = ArtifactFiles.Begin())
        {
            set.Stage(null, Path.Combine(folder, "c.vti"), s => s.Write([3]));
            Assert.Throws<EinzelException>(() => set.Stage(null, Path.Combine(folder, "C.vti".ToLowerInvariant()), s => s.Write([4])));
        }

        Assert.Equal(["a.vti", "b.vti"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
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
