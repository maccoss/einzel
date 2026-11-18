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

    [Fact]
    public void ASecondElementIsSolvedWithoutTheFirstStillHeld()
    {
        Directory.CreateDirectory(_root);
        var single = Path.Combine(_root, "single.json");
        Assert.Equal(0, Program.Main(["new", single, "--from-example", "parallel-plate-gap-3d"]));

        // Half-millimetre cells: about a million nodes an element, so a solve held is megabytes
        // against a baseline that moves by far less.
        var document = JsonNode.Parse(File.ReadAllText(single))!;
        document["fields"]![0]!["solve3d"]!["cellSize"]!["value"] = 0.5;
        File.WriteAllText(single, document.ToJsonString());

        // The same element twice, so each solve weighs the same.
        var pair = Path.Combine(_root, "pair.json");
        var fields = document["fields"]!.AsArray();
        fields.Add(fields[0]!.DeepClone());
        File.WriteAllText(pair, document.ToJsonString());

        var one = Held(single, 1);
        var two = Held(pair, 2);

        output.WriteLine(FormattableString.Invariant(
            $"one element held {one[0] / 1048576.0:F2} MiB at its file; two elements held {two[0] / 1048576.0:F2} and {two[1] / 1048576.0:F2} MiB at theirs"));

        // Each element's own solve, never both: queued until the last, the first file was written
        // with both elements alive, twice what one element holds.
        Assert.True(one[0] > 4 * 1048576, $"one element held {one[0]} bytes - too little to tell one from two");
        Assert.All(two, held => Assert.True(
            held < 1.5 * one[0],
            FormattableString.Invariant($"{held} bytes held at an element's file, against {one[0]} for one element alone")));
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
