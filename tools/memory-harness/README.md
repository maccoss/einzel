# memory-harness

What a solve or a diffusive run holds, per node, in a fresh process - as a **live set**, which is
what `einzel estimate` states and what has to fit, and as a **working set**, which is what the
operating system supplies to an unconstrained process and what a task manager shows. The same
run measured both ways, one process each.

It exists so the estimate's memory statements can be checked from outside the test suite, and so
the gap between the two quantities is a number rather than a remark. Not part of `Einzel.slnx`,
and with its own `Directory.Build.props` so the engine's analyzers do not apply to it.

```bash
dotnet build tools/memory-harness -c Release
python tools/memory-harness/sweep.py plane            # the working set; likewise driven, volume,
python tools/memory-harness/sweep.py plane --live     #   channels, diffuse, species, meanfield
python tools/memory-harness/sweep.py table            # bytes a node, one row per run
```

## The two quantities, and why one process each

**`--live`: the peak live set, read from inside the work.** What a solve or a run holds can only
rise at an allocation, and every array the size of a grid is allocated through one internal
helper (`GridArrays`), so the harness reads the live set there - a forced full collection, then
the collector's own record of what survived it - on the work's own thread, at each array that
could raise the peak. It is the memory tests' own measurement, linked in as source
(`tests/Shared/LiveSet.cs`), and nothing in it samples: it used to be a second thread forcing
collections as fast as it could while the work ran, which has to land inside the peak and on a
short run does not. This is what `SolveMemoryModel` and the diffusive constants state.

A live run reports the whole measurement and a verdict on it, by the tests' own logic
(`tests/Shared/LiveVerdict.cs`):

| field | what it is |
| --- | --- |
| `liveSetPeakMiB`, `liveSetBoundMiB` | the least the work can have held at its most, and the most |
| `liveHeldMiB`, `liveGridArraysMiB` | the largest reading of the process, and the work's own grid arrays at their most |
| `gridArrays`, `gridArraysRead` | how many grid arrays it allocated, and how many were read at |
| `liveOutsideGridArraysBytes` | what the work's thread allocated outside them, live or not |
| `liveForeignBytes`, `liveForeignBurstBytes` | what other threads allocated, in all and at most between two readings |
| `liveRetainedBytes` | the work's grid arrays still alive after it returned and its result was let go |
| `liveInvalid` | why the measurement does not cover the work - a grid array allocated on another thread - or null |
| `statedMiB` | what the estimate states for the work measured |
| `liveVerdict` | "within 0.90 to 1.02 of what the estimate states", or the sentence a memory test would fail with: not covered, not the work's alone, past an edge, or inconclusive where the interval straddles an edge and other threads allocated enough to be the difference |

**A live run pins what the tests' children pin, and warms up once.** It starts itself again
with a workstation, non-concurrent collector and with tiered compilation off, so that the only
full collections are the ones it forces and nothing is held by first-tier code, and it refuses
to report if the collector is anything else. The work is run once unmeasured first, so that
what a first call allocates for itself and keeps is in the baseline. The project sets no
collector of its own for this reason: one written into the project goes into
`runtimeconfig.json` as `System.GC.Concurrent`, and that setting is not overridden by
`DOTNET_gcConcurrent` - measured on .NET 10, a program built with it true reports `Interactive`
under `DOTNET_gcConcurrent=0` and `Batch` with nothing in its configuration - so a live run could
then not turn concurrent collection off. (`DOTNET_gcServer` does override `System.GC.Server`; the
two settings do not behave alike.) A one-step diffusive run with a self-field reads under its
statement, which is a whole flight's; pass `declared`.

**Default: the peak working set**, `Process.PeakWorkingSet64` after the work minus before it. That
is the operating system's own peak counter, so nothing has to be sampled to get it. **One thing
here still samples, and it is not a figure anything is held to**: beside the working set, a
thread polls the managed heap's size every millisecond without provoking a collection
(`polledHeapPeakMiB`, the `heap` column) to show how much of the working set was garbage not yet
reclaimed. A poll that misses a peak reads low; the column describes a run and nothing is
asserted on it. Garbage the collector has not yet reclaimed occupies physical memory exactly as
live data does, so the working set is what an unconstrained run actually takes.

They need separate processes because measuring the first disturbs the second: forcing a full
collection at every array is what makes the live peak exact, and it is also what keeps the
working set down. And the peak working set only ever rises, so a second run in one
process would read the first one's peak. `sweep.py` starts one process per point, sequentially.

**The working set is not a bound.** The collector frees large arrays lazily when memory is
plentiful and gives them back when it is not: under a hard heap limit the Astral at 4 mm completed
at 279 MiB and ran out at 275, against a live peak of 259 (`docs/cli.md`). So the live set plus a
few percent is what a run needs, and the working set is what it will take if nothing stops it.

## Measured

Release, branch `fix/vcycle-residual-frame` at `cfcc49a` (the V-cycle's fine residual in a frame of
its own, the rediscretized statement worked out instant by instant), an Intel Core i9-9900K
(8 cores, 16 threads) with 63.8 GB; bytes a node; `est` is what `einzel estimate` states. The rows
are what `sweep.py plane`, `volume` and `diffuse` run - the volumes at their 1 and 0.5 mm cells
and the drift tube at 512 and 1024 intervals - once without `--live` and once with it. The linear
ion trap with its excitation on is three channels now, where it was two when these rows were
first taken; the rest are the same models.

| case | nodes | live | working set | working set / live | est | kept |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| plane, quadrupole | 1,050,625 | 135.3 | 298.1 | 2.20 | 135.2 | 8.0 |
| plane, quadrupole | 4,198,401 | 135.3 | 309.4 | 2.29 | 135.2 | 8.0 |
| plane, quadrupole | 16,785,409 | 135.3 | 295.0 | 2.18 | 135.2 | 8.0 |
| volume, segmented quadrupole | 140,481 | 47.1 | 110.7 | 2.35 | 48.9 | 8.0 |
| volume, segmented quadrupole | 1,085,825 | 46.0 | 206.7 | 4.49 | 48.9 | 8.0 |
| volume, c-trap | 139,425 | 45.7 | 116.1 | 2.54 | 48.8 | 8.0 |
| volume, c-trap | 1,081,665 | 45.3 | 181.5 | 4.01 | 48.8 | 8.0 |
| volume, linear ion trap, three channels | 140,481 | 166.6 | 671.4 | 4.03 | 167.4 | 24.1 |
| volume, linear ion trap, three channels | 1,085,825 | 164.1 | 501.2 | 3.05 | 166.4 | 24.0 |
| diffusive step, drift tube | 263,169 | 248.1 | 280.1 | 1.13 | 263.0 | 8.2 |
| diffusive step, drift tube | 1,050,625 | 248.0 | 256.2 | 1.03 | 263.0 | 8.0 |

The diffusive rows' `est` is what the estimate stated when they were taken, 263; it states 248
now, which is the live column, and 4 more where a solved element has electrodes
(`docs/cli.md`). Their working set was taken while the stepping loop still made 224 bytes of
garbage a node a step and has not been taken again since it stopped, so read 1.03 to 1.13 as an
upper figure for that ratio.

The plane read 143.3 live against an estimate of 143.2, and the two rediscretized volumes 47.8 to
49.7 against 50.9 to 51.5, at `de73ec8`: one array of the fine grid more, which the V-cycle held in
a local of the frame that recursed while the solver ran as first-tier code, and which the
statements then charged (`docs/lessons.md`, "A reading that falls as a process warms up").
Mean-field space charge, live, over a 5 us flight so the self-field refreshes inside the stepping
loop (`sweep.py meanfield --live`), same machine and commit:

| case | 66,049 | 263,169 | 1,050,625 |
| --- | ---: | ---: | ---: |
| drift tube | 248.5 | 248.1 | 248.0 |
| drift tube, mean field | 309.9 | 309.4 | 309.3 |
| two species | 496.6 | 496.2 | - |
| two species, mean field | 565.9 | 565.4 | - |

Within a tenth of a byte of what they read at `de73ec8`, before the V-cycle's fix: these runs did
not show the residual it held while first-tier, which reached the self-field's memory test on a
slow runner. A measurement that happened not to see a hold is not evidence there was none, which is
why `FirstTierMemoryTests` pins the tier.

## What the tables say

- **The estimate states the live set, and agrees with an independent measurement of it** to within
  six percent on every case here: over it on the volumes (the cut-arm allowance), a tenth of a
  byte under it on the plane, and - since it was restated from 263 to 248 - on it for the
  diffusive step.
- **An unconstrained solve's working set is two to four and a half times its live set**, and on
  the rediscretized volumes the ratio grows with size (2.3 to 4.2, 2.8 to 4.5 across eightfold).
  The multigrid cycle allocates fresh buffers every cycle and the collector frees them when it
  chooses. A diffusive step is nearly all live data, 1.03 to 1.12.
- **What a finished field keeps is 8 bytes a node a channel**: the potential.
- **The self-field is 61.3 to 61.4 on top of the step, once whatever the species count** (69.3 to
  69.4 with two, the extra 8 being the summed charge). Measured over one step it reads 25, because
  the first Poisson solve precedes the loop; see `docs/lessons.md`.

**Re-run it when an allocation pattern changes** - pooling the V-cycle's buffers would close most
of the gap between the two quantities, and nothing else would notice.
