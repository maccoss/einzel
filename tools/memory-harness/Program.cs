// What a solve or a diffusive step holds, per node, in a fresh process.
//
//   memory-harness nodes   <model.json>                    what `einzel estimate` says, and the grids
//   memory-harness fields  <model.json> [live]             assemble every field element, as `einzel run` does
//   memory-harness diffuse <model.json> [live] [declared]  one step of the model's density, or with
//                                                          `declared` its whole declared flight
//
// Two quantities, one per process, because measuring one disturbs the other:
//
//   default  the peak WORKING SET above the process's baseline, with the managed heap polled
//            lightly beside it. What the operating system had to supply to an unconstrained
//            process - garbage the collector had not yet reclaimed included.
//   live     the peak LIVE SET: what survived a forced full collection, read on the work's own
//            thread at each grid-sized array it allocates (tests/Shared/LiveSet.cs, the memory
//            tests' own measurement). What `einzel estimate` reports, and what has to fit.
//
// Forcing a collection at every array is what makes the live peak exact and is also what keeps
// the working set down, so a run that read both would report a working set no ordinary run
// reaches. One JSON line on stdout. See README.md.

using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Testing;

var options = args.Skip(2).ToHashSet(StringComparer.Ordinal);

// The mode is checked before anything runs: an unknown one used to fall through to the diffusive
// branch and fail there on a field that was never built.
if (args.Length < 2 || args[0] is not ("nodes" or "fields" or "diffuse") || options.Except(["live", "declared"]).Any())
{
    Console.Error.WriteLine("usage: memory-harness nodes|fields|diffuse <model.json> [live] [declared]");
    return 2;
}

var mode = args[0];
var path = Path.GetFullPath(args[1]);
var live = options.Contains("live");

// A LIVE FIGURE IS READ UNDER THE COLLECTOR THE MEMORY TESTS READ UNDER: workstation, and not
// concurrent, so that the only full collections are the ones the measurement forces, with the
// JIT compiling everything fully at once so that nothing is held by first-tier code. Those are
// settled before a runtime starts, so a live run starts itself again with them set - the
// default run keeps the runtime's own collector, whose working set is the thing it measures.
const string Pinned = "MEMORY_HARNESS_PINNED";

if (live && Environment.GetEnvironmentVariable(Pinned) != "1")
{
    var again = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };

    foreach (var argument in args)
    {
        again.ArgumentList.Add(argument);
    }

    again.Environment["DOTNET_gcServer"] = "0";
    again.Environment["DOTNET_gcConcurrent"] = "0";
    again.Environment["DOTNET_TieredCompilation"] = "0";
    again.Environment[Pinned] = "1";

    using var pinned = Process.Start(again)!;

    pinned.WaitForExit();

    return pinned.ExitCode;
}

if (live && (GCSettings.IsServerGC || GCSettings.LatencyMode != GCLatencyMode.Batch))
{
    Console.Error.WriteLine(
        $"a live figure is read under a workstation, non-concurrent collector, and this process has "
        + $"{(GCSettings.IsServerGC ? "server" : "workstation")} {GCSettings.LatencyMode}: something set it otherwise");

    return 3;
}

// The figure under test, and the grids it was computed from.
var estimate = EstimateCommand.Execute(path, calibrate: false);

if (mode == "nodes")
{
    Console.WriteLine(JsonSerializer.Serialize(Elements(estimate)));
    return 0;
}

var validation = ModelValidator.Validate(
    ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(path));

if (!validation.IsValid)
{
    Console.Error.WriteLine(validation.Errors[0]);
    return 1;
}

var model = validation.Model!;
IElectrostaticField? field = null;
IReadOnlyList<Einzel.Core.Results.ValidityWarning>? warnings = null;

if (mode == "diffuse")
{
    // The field is built BEFORE the baseline is taken: what is measured is the density run, not
    // the solve beneath it - which is how `einzel estimate` separates the two terms as well.
    (field, warnings) = FieldAssembly.BuildReported(model);

    // ONE STEP, unless asked for the declared flight. A plain run allocates everything it holds
    // before or during its first step - the coefficients and the face operator - and every
    // later step reuses it, so one step is its peak and the drift tube's 1,500 us would only
    // repeat it, for minutes under the live sampler.
    //
    // NOT SO WITH A SELF-FIELD, and one step under-reads it. The first Poisson solve is done
    // before the loop, when the step's own arrays do not exist yet; every later refresh solves
    // inside the loop, on top of them. So a mean-field run is measured over a declared flight
    // long enough to refresh several times - `declared`.
    if (!options.Contains("declared"))
    {
        model = model with { MaximumFlightTimeSi = 1e-12 };
    }
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var self = Process.GetCurrentProcess();
self.Refresh();

var peakBefore = self.PeakWorkingSet64;
var commitBefore = self.PeakPagedMemorySize64;
var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);

long heapPeak = 0;
var stop = 0;

Func<object> work = mode == "fields"
    ? () => FieldAssembly.BuildReported(model)
    : model.IsMixture
        ? () => DiffusionRun.ExecuteMixture(model, field!, warnings!)
        : () => DiffusionRun.Execute(model, field!, warnings!);

var watch = Stopwatch.StartNew();
object? kept = null;
LivePeak? livePeak = null;

if (live)
{
    // Once unmeasured, as the memory tests do, so that what a first call allocates for itself
    // and keeps - statics, tables - is in the baseline and not in the reading.
    work();

    // Read from inside the work, at each array it allocates: nothing samples, so there is no
    // peak to miss however short it is. What the work returns is let go, so that what is left
    // of its arrays afterwards is what something outliving it kept.
    watch.Restart();
    livePeak = LiveSet.Peak(work);
    watch.Stop();
}
else
{
    // The working set is the operating system's own peak counter and needs no sampling. Only the
    // managed heap's size, garbage included, is polled beside it, as a description of the run
    // rather than a figure anything is held to.
    var monitor = new Thread(PollHeap)
    {
        IsBackground = true,
        Priority = ThreadPriority.AboveNormal,
    };

    monitor.Start();

    try
    {
        kept = work();
    }
    finally
    {
        watch.Stop();
        Volatile.Write(ref stop, 1);
        monitor.Join();
    }
}

self.Refresh();

var peakAfter = self.PeakWorkingSet64;
var commitAfter = self.PeakPagedMemorySize64;
var allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;

// What the finished work still holds, with the result kept alive until after the collection.
var retained = GC.GetTotalMemory(forceFullCollection: true) - heapBefore;
GC.KeepAlive(kept);
GC.KeepAlive(field);

const double Mebi = 1024.0 * 1024.0;

// What the estimate states for the work that was measured: every solved element for `fields`,
// and for `diffuse` the run's own part, the field having been built before the baseline.
var solved = estimate.Elements.Sum(e => e.MemoryMiB);
var statedMiB = mode == "fields" ? solved : estimate.MemoryMiB - solved;
var perNodes = mode == "fields" ? estimate.Elements.Sum(e => (double)e.NodeCount) : DiffusionRun.GridFor(model).NodeCount;

// The memory tests' own verdict, in their words: whether the measurement covers the work and
// was the work's alone, and then whether the interval lies within 0.90 to 1.02 of the statement
// - a finding where it is wholly past an edge, inconclusive where it straddles one and other
// threads allocated enough to be the difference. A one-step diffusive run with a self-field
// reads under its statement, which is the statement of a whole flight: ask for `declared`.
var verdict = livePeak is null || perNodes <= 0
    ? null
    : LiveVerdict.NotCovered("the work", livePeak)
        ?? LiveVerdict.NotItsOwn("the work", livePeak)
        ?? LiveVerdict.Against(
            "the work",
            livePeak.Least / perNodes,
            livePeak.Bound / perNodes,
            0.90 * statedMiB * Mebi / perNodes,
            1.02 * statedMiB * Mebi / perNodes,
            perNodes,
            livePeak.Foreign)
        ?? "within 0.90 to 1.02 of what the estimate states";

Console.WriteLine(JsonSerializer.Serialize(new
{
    mode,
    quantity = live ? "live" : "workingSet",
    model = Path.GetFileName(path),

    // Nodes the work was done on: the field grids, or the density grid for a diffusive run.
    gridNodes = mode == "diffuse" ? DiffusionRun.GridFor(model).NodeCount : 0L,
    species = Math.Max(1, model.Species.Count),
    elements = Elements(estimate),
    estimateMiB = estimate.MemoryMiB,

    seconds = watch.Elapsed.TotalSeconds,

    // Peak working set and peak commit above what the process held before the work began.
    // In a live run these are depressed by the measurement's own collections - read them from
    // a default run.
    deltaPeakWorkingSetMiB = (peakAfter - peakBefore) / Mebi,
    deltaPeakCommitMiB = (commitAfter - commitBefore) / Mebi,
    baselinePeakWorkingSetMiB = peakBefore / Mebi,

    // The managed heap's peak above its start, dead objects included (default runs only).
    polledHeapPeakMiB = live ? (double?)null : (heapPeak - heapBefore) / Mebi,

    // The live set's peak above its start (live runs only): the least the work can have held
    // at its most, and the most. The two differ by what the work allocated outside GridArrays
    // between two of its arrays, which is a few kilobytes.
    liveSetPeakMiB = livePeak?.Least / Mebi,
    liveSetBoundMiB = livePeak?.Bound / Mebi,

    // The rest of the measurement, as the memory tests have it: the largest reading of the
    // process, the work's own grid arrays at their most, how many there were and how many were
    // read at, what the work's thread allocated outside them, what other threads allocated in
    // all and at most between two occasions to read, and what of the work's arrays was still alive after
    // it returned and its result was let go.
    liveHeldMiB = livePeak?.Held / Mebi,
    liveGridArraysMiB = livePeak?.Tracked / Mebi,
    gridArrays = livePeak?.Readings,
    gridArraysRead = livePeak?.Collections,
    liveOutsideGridArraysBytes = livePeak?.Silent,
    liveForeignBytes = livePeak?.Foreign,
    liveForeignBurstBytes = livePeak?.ForeignBurst,
    liveRetainedBytes = livePeak?.Retained,

    // Why the live measurement does not cover the work - grid arrays allocated on another
    // thread - or null.
    liveInvalid = livePeak?.Invalid,

    // What the estimate states for the work measured, and the verdict on the interval against it.
    statedMiB,
    liveVerdict = verdict,

    // Everything allocated over the run: how much the collector had to free.
    allocatedMiB = allocated / Mebi,

    // What the finished work still holds.
    retainedMiB = retained / Mebi,

    gen2Collections = GC.CollectionCount(2),
    serverGc = GCSettings.IsServerGC,
    gcLatencyMode = GCSettings.LatencyMode.ToString(),
}));

return 0;

// Light polling: what the heap holds, garbage included. Does not provoke a collection.
void PollHeap()
{
    while (Volatile.Read(ref stop) == 0)
    {
        heapPeak = Math.Max(heapPeak, GC.GetTotalMemory(forceFullCollection: false));
        Thread.Sleep(1);
    }
}

static object[] Elements(EstimateOutcome estimate) =>
    [.. estimate.Elements.Select(e => new { e.Type, e.NodeCount, e.MemoryMiB })];
