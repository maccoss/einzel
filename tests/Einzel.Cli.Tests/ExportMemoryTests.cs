using System.Globalization;
using System.Text.Json.Nodes;
using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// An export of several solved elements holds one element's solves at a time, while still
/// putting every file in place together or not at all.
/// </summary>
/// <remarks>
/// Every channel's file used to be queued as a closure until the last element was solved, so
/// all-or-none held every element's solves alive together and the peak was their sum - two
/// mirror solves on the Astral, where the estimate bills one. The files are now staged on disk as
/// each element is solved and renamed into place at the end.
/// </remarks>
[Collection(nameof(RunsAlone))]
public sealed class ExportMemoryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-export-memory", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <remarks>
    /// <para>
    /// <b>Measured within one export, against itself.</b> A large element and then a small one: at
    /// the small one's file only the small solve is alive, a fraction of what the large one's file
    /// held - and queued, both were alive at both files, so the two readings were equal.
    /// </para>
    /// <para>
    /// The first version compared the pair with a separate export of one element, each above a
    /// baseline taken before its own run, and on Windows CI (run 44244801141) the one-element
    /// reading came out at 4.2 MiB against 8.2 MiB for the pair's first element - at a file where
    /// no second element had been solved yet, so nothing could be held twice. The reference was
    /// what read low: a baseline taken before a run includes whatever an earlier test in the same
    /// process left that the run then releases, and that came off the reference alone. Two
    /// readings of one run share one baseline, so whatever it contains cancels in their ratio. Not
    /// reproduced locally: run after the whole assembly, the old form read 8.24 against 7.19 and
    /// 7.18 MiB.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondElementIsSolvedWithoutTheFirstStillHeld()
    {
        Directory.CreateDirectory(_root);
        var model = Path.Combine(_root, "pair.json");
        Assert.Equal(0, Program.Main(["new", model, "--from-example", "parallel-plate-gap-3d"]));

        // The same element twice, at two cell sizes: half a millimetre, about a million nodes and
        // megabytes of solve, then two millimetres, a sixty-fourth of that.
        var document = JsonNode.Parse(File.ReadAllText(model))!;
        var fields = document["fields"]!.AsArray();
        fields.Add(fields[0]!.DeepClone());
        fields[0]!["solve3d"]!["cellSize"]!["value"] = 0.5;
        fields[1]!["solve3d"]!["cellSize"]!["value"] = 2.0;
        File.WriteAllText(model, document.ToJsonString());

        var held = Held(model, 2);
        var (large, small) = (held[0], held[1]);

        output.WriteLine(FormattableString.Invariant(
            $"held {large / 1048576.0:F2} MiB at the large element's file and {small / 1048576.0:F2} MiB at the small one's"));

        // Enough at the first file to tell one solve from two, and at the second a fraction of it:
        // queued, the large solve was alive at both files and the two readings were the same.
        Assert.True(large > 4 * 1048576, $"{large} bytes held at the large element's file - too little to tell one solve from two");
        Assert.True(
            small < 0.25 * large,
            FormattableString.Invariant($"{small} bytes held at the small element's file against {large} at the large one's: the large solve is still held"));
    }

    // What the export holds above its own start, at the moment each element's file is staged.
    private long[] Held(string model, int elements)
    {
        var project = Path.Combine(_root, Path.GetFileNameWithoutExtension(model));
        var held = new long[elements];
        var baseline = Live();

        ArtifactFiles.Failing.Value = (phase, index) =>
        {
            if (phase == "write" && index < elements)
            {
                held[index] = Live() - baseline;
            }
        };

        try
        {
            Assert.Equal(0, Program.Main(["export", model, "--project", project]));
        }
        finally
        {
            ArtifactFiles.Failing.Value = null;
        }

        output.WriteLine(model + ": " + string.Join(", ", held.Select(h => (h / 1048576.0).ToString("F2", CultureInfo.InvariantCulture))) + " MiB");
        return held;
    }

    private static long Live()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        var live = 0L;

        foreach (var generation in info.GenerationInfo)
        {
            live += generation.SizeAfterBytes - generation.FragmentationAfterBytes;
        }

        return live;
    }
}
