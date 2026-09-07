# GPU work: a handoff

**Read this before writing any GPU code.** It exists because the specification says a
quadrupole stability scan is "GPU-bound; why ILGPU is in the stack", and measuring that on
the machine this project was written on found both halves of it wrong — the scan is not
GPU-bound, and the card's advantage in double precision is 1.4x rather than the 5x the
obvious benchmark shows.

That is a statement about **one machine**, not about GPUs. What this page is for is making
the same measurement on a better one, in about ten minutes, before anything is committed to.

Everything below is measured on an Intel i9-9900K (8 cores, 16 threads, AVX2) with a GeForce
GTX 1650 and an Intel UHD 630, on 2026-09-06.

---

## 1. Run the probe first

It answers, on whatever machine it runs on: which backends exist, which devices the machine
offers, whether each does double precision at all, at what rate, and how that compares with
the CPU **allowed the same parallelism**. Three commands and no repository checkout:

```bash
dotnet new console -o gpu-probe
cd gpu-probe && dotnet add package ILGPU --version 1.5.3
dotnet run -c Release
```

Paste the listing in section 6 into `Program.cs` first. Release matters: a Debug build is
3.27x slower here, and it would be the CPU side that suffered.

**What to look for, in order:**

1. **Does FP64 run at all?** Some devices refuse the kernel or fall back to emulation. The
   probe catches and reports that rather than crashing.
2. **The FP64 : FP32 ratio.** This is the number that decides everything, and it does not
   track a card's headline speed.
3. **`vs CPU vectorised`, not `vs CPU scalar`.** The probe prints both, and the second is
   labelled "the flattering one" for a reason given in section 3.

---

## 2. What is already measured

```
CPU serial              1.2 GFLOP/s FP64
CPU parallel           17.3 GFLOP/s FP64  (16 logical)
CPU vectorised         62.7 GFLOP/s FP64  (width 4)  <-- the fair baseline

OpenCL: Intel(R) UHD Graphics 630
   FP64       56.2 GFLOP/s      FP64 : FP32   1 : 3.9     vs CPU vectorised  0.90x
   FP32      218.4 GFLOP/s

Cuda: NVIDIA GeForce GTX 1650
   FP64       90.8 GFLOP/s      FP64 : FP32   1 : 36.9    vs CPU vectorised  1.45x
   FP32     3350.1 GFLOP/s
```

Two things in that table are worth more than the headline.

**The Intel integrated GPU has a nine times better FP64 ratio than the NVIDIA card** — 1:3.9
against 1:36.9 — and is slower anyway, because its single-precision base is fifteen times
smaller. Ratio and absolute throughput pull in opposite directions, which is why "get a more
capable GPU" is ambiguous advice: consumer cards have been getting steadily worse at the
ratio while getting much faster at both.

**And this is an upper bound on what any real kernel reaches.** The probe is chained fused
multiply-adds with no memory traffic. A tricubic gather reads sixty-four nodes per sample and
a multigrid sweep does almost no arithmetic per node touched, so both are limited by memory
rather than by flops. **Bandwidth is the stronger argument for a discrete card than FP64 is,
and this probe does not measure it.** Adding a streaming-triad kernel to the probe would, and
is worth ten minutes.

---

## 3. The trap that produced the wrong answer first

The first version of this measurement compared a **scalar** CPU loop against the GPU and got
5.25x. The CPU kernel was one dependent chain per lane: each multiply-add needs the previous
result, so it used neither SIMD nor the second FMA port. The GPU kernel is the same dependent
chain — but thousands of them at once, which is exactly what the CPU version was not allowed
to do.

Whether that is fair is a question about the physics, and here the physics is clear: one
ion's Runge-Kutta stages **are** a dependent chain, but ions are independent of each other,
so the CPU can vectorise across them exactly as a GPU parallelises across threads. Allowed
that, the CPU gives 62.7 rather than 17.3 and the GPU's advantage falls to 1.45x. **The
missing factor is 3.6, and the AVX2 lane count for doubles is 4.**

The rule, now in `docs/lessons.md`: when comparing two devices, both sides must be allowed
the parallelism the problem actually has. A scalar baseline against a parallel candidate
measures the baseline's restraint, and the ratio it produces is roughly the candidate's
width — a number about the benchmark rather than about the hardware.

---

## 4. Which kernel, and how to decide

There are two candidates and they want different hardware. **Do not guess between them:
profile a representative study first**, because porting the wrong one is the expensive
mistake.

### The trajectory scan — embarrassingly parallel, FP64-throughput bound

500,000 independent ions through one cached field. This is the classic GPU shape, and it is
what PERF-5 names.

**It is already inside its budget on the CPU.** Measured: 500 amplitudes by 1,000 ions in
**2,809 s — 46.8 minutes against a two-hour budget** — while sharing the machine with a test
suite for the first third. And that run is the *expensive* end, since its amplitude range
lies entirely inside the stable region so every ion flew the full length; a scan that crosses
the cut-off loses half its ions early and costs less.

So this kernel does not need a GPU. What it needs, and has not had, is **the CPU vector path**
— which is the measured 3.6x above, reuses the existing integrator and interpolant, and is
also what makes any future GPU comparison honest.

### The field solve — memory-bandwidth bound

The Astral run is **94% solve**. Study-level parallelism tops out near 5x on eight cores
while a pure-arithmetic control reaches 6.7x and keeps gaining from hyperthreading, which is
what identifies bandwidth rather than machinery as the ceiling. A threaded smoother will
disappoint for the same reason.

**Bandwidth is the one resource a discrete card brings a step change in** — roughly 25x this
CPU on a current high-end part. So if a GPU is worth building here, this is the kernel, and
the measurement that decides it is a streaming bandwidth test rather than an FMA test.

### The profile to run

```bash
einzel estimate <study>.json          # says solve vs flight per evaluation, measured
```

The basis line names both terms. If the solve dominates, port the smoother; if the flight
dominates, do the CPU vector path and stop.

---

## 5. Constraints that are not negotiable

**Double precision throughout.** ACC-1 is 1 ppm on a flight time, and FP32 carries about
1e-7 relative — a flight accumulating thousands of steps would blow that on its own. The
engine already uses Neumaier compensation on time accumulation because plain doubles are not
enough there, which is the measure of how little headroom exists. A mixed-precision kernel is
not a shortcut available here.

**CMP-1: the scalar reference is never deleted or allowed to rot.** The pair sum is the
pattern to copy — `CoulombInteraction.Kernel` makes the scalar path *selectable* rather than
merely retained, because a reference nothing can run is a reference nobody can check, and the
two are compared over every size that exercises a different part of the loop. A GPU path
needs the same: a switch, and a test that runs both.

**Determinism, and it is sharper than it looks.** A seeded sweep must be reproducible, and
this project has already been caught by a shared-state ordering bug that produced identical
counts and a different exemplar. On a GPU the hazard is reductions: any sum across ions
reorders, so it is not bit-reproducible. The existing parallel field loop is bit-identical
however many cores run it *because nothing is summed across members* — keep that property.
The mutual-force sum is deliberately not parallelised for exactly this reason.

**Architecture invariant 1.** Nothing may reference the shell, and everything above it builds
and runs on Linux, where CI runs. A GPU backend must degrade to the CPU path when no device
is present, or CI goes red on every runner.

**LIC-1.** ILGPU 1.5.3 is University of Illinois/NCSA — permissive, and there is no GPL
anywhere in its closure. Its only dependencies are `System.Collections.Immutable` and
`System.Reflection.Metadata`, both MIT. Verified from the package's own nuspec and licence
files, not from documentation.

---

## 6. AMD and Intel

**Yes, and no code changes.** ILGPU 1.5.3 ships three backends, confirmed by enumerating the
assembly's own device types rather than from its documentation:

| backend | covers |
| --- | --- |
| `ILGPU.Runtime.Cuda` | NVIDIA |
| `ILGPU.Runtime.OpenCL` | **AMD and Intel**, and anything else with an OpenCL driver |
| `ILGPU.Runtime.CPU` | a CPU accelerator, for debugging a kernel without a device |

The OpenCL path is not theoretical here: the probe already ran both kernels on the Intel UHD
630 through it, and got 56.2 GFLOP/s FP64. So a kernel written once runs on all three.

**What to check on the target machine, because it varies far more than the vendor does:**

- **Does the device advertise FP64 at all?** OpenCL makes double precision an optional
  extension. A device without it will refuse the kernel, and the probe reports that.
- **Is FP64 native or emulated?** Some recent consumer parts emulate it in software, which is
  catastrophically slow rather than merely slower, and the probe's ratio column makes that
  obvious — an emulated device lands orders below its FP32 rather than a factor of tens.
- **AMD spans the widest range of any vendor here**, from around 1:16 on consumer parts to
  1:2 on compute cards, so the model number matters more than the brand.

Nothing above is asserted from a specification sheet. Run the probe.

---

## 7. What done looks like

1. The probe has been run on the target machine and its output is recorded in this document.
2. A representative study has been profiled and the dominant term named.
3. `Einzel.Compute` exists with at least two paths behind one dispatch — scalar and one
   accelerated — selectable rather than automatic, so a test can run both.
4. The two agree to a stated tolerance on every size that exercises a different part of the
   loop, and a deliberate mutation fails the comparison.
5. The speedup is reported **against the vectorised CPU baseline**, and the crossing point is
   stated rather than an asymptotic ratio — the particle-in-cell work is the precedent, where
   the honest answer was "it starts paying at about 850 macroparticles".
6. CI is green on a runner with no GPU.

---

## 8. The probe

Self-contained. `dotnet new console`, add ILGPU 1.5.3, paste this over `Program.cs`.

```csharp
using System.Diagnostics;
using ILGPU;
using ILGPU.Runtime;

const int Inner = 4096;

static void KernelD(Index1D i, ArrayView<double> a, double c)
{
    double x = a[i], y = 1.0000001;
    for (var k = 0; k < Inner; k++) { x = (x * y) + c; }
    a[i] = x;
}

static void KernelF(Index1D i, ArrayView<float> a, float c)
{
    float x = a[i], y = 1.0000001f;
    for (var k = 0; k < Inner; k++) { x = (x * y) + c; }
    a[i] = x;
}

static double Gflops(long lanes, long inner, double seconds) =>
    lanes * inner * 2.0 / seconds / 1e9;

static double Best(Func<double> run, int reps)
{
    var best = double.MaxValue;
    for (var i = 0; i < reps; i++) { best = Math.Min(best, run()); }
    return best;
}

var n = 1 << 20;
var host = new double[n];
var hostF = new float[n];
for (var i = 0; i < n; i++) { host[i] = 1.0 + (i % 7); hostF[i] = 1.0f + (i % 7); }

var serialLanes = 1 << 14;
var serial = Gflops(serialLanes, Inner, Best(() =>
{
    var a = new double[serialLanes];
    Array.Copy(host, a, serialLanes);
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < serialLanes; i++)
    {
        double x = a[i], y = 1.0000001;
        for (var k = 0; k < Inner; k++) { x = (x * y) + 0.5; }
        a[i] = x;
    }
    sw.Stop();
    GC.KeepAlive(a);
    return sw.Elapsed.TotalSeconds;
}, 5));

var parallel = Gflops(n, Inner, Best(() =>
{
    var a = (double[])host.Clone();
    var sw = Stopwatch.StartNew();
    Parallel.For(0, n, i =>
    {
        double x = a[i], y = 1.0000001;
        for (var k = 0; k < Inner; k++) { x = (x * y) + 0.5; }
        a[i] = x;
    });
    sw.Stop();
    GC.KeepAlive(a);
    return sw.Elapsed.TotalSeconds;
}, 3));

var width = System.Numerics.Vector<double>.Count;
var vectorised = Gflops(n, Inner, Best(() =>
{
    var a = (double[])host.Clone();
    var sw = Stopwatch.StartNew();
    Parallel.For(0, n / width, block =>
    {
        var x = new System.Numerics.Vector<double>(a, block * width);
        var y = new System.Numerics.Vector<double>(1.0000001);
        var c = new System.Numerics.Vector<double>(0.5);
        for (var k = 0; k < Inner; k++) { x = (x * y) + c; }
        x.CopyTo(a, block * width);
    });
    sw.Stop();
    GC.KeepAlive(a);
    return sw.Elapsed.TotalSeconds;
}, 3));

Console.WriteLine($"CPU serial       {serial,10:F1} GFLOP/s FP64");
Console.WriteLine($"CPU parallel     {parallel,10:F1} GFLOP/s FP64  ({Environment.ProcessorCount} logical)");
Console.WriteLine($"CPU vectorised   {vectorised,10:F1} GFLOP/s FP64  (width {width})  <-- the fair baseline");

using var context = Context.Create(b => b.AllAccelerators());

foreach (var device in context.Devices)
{
    if (device.AcceleratorType == AcceleratorType.CPU) { continue; }

    Accelerator accelerator;
    try { accelerator = device.CreateAccelerator(context); }
    catch (Exception e)
    {
        Console.WriteLine($"{Environment.NewLine}{device.AcceleratorType}: {device.Name} - "
            + $"cannot create accelerator ({e.GetType().Name})");
        continue;
    }

    using (accelerator)
    {
        Console.WriteLine($"{Environment.NewLine}{device.AcceleratorType}: {accelerator.Name}, "
            + $"{accelerator.MemorySize / (1024 * 1024)} MiB, warp {accelerator.WarpSize}");

        double gd = double.NaN, gf = double.NaN;

        try
        {
            var kd = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, double>(KernelD);
            using var bufD = accelerator.Allocate1D(host);
            kd(n, bufD.View, 0.5);
            accelerator.Synchronize();
            gd = Gflops(n, Inner, Best(() =>
            {
                var sw = Stopwatch.StartNew();
                kd(n, bufD.View, 0.5);
                accelerator.Synchronize();
                return sw.Elapsed.TotalSeconds;
            }, 3));
            Console.WriteLine($"   FP64 {gd,10:F1} GFLOP/s");
        }
        catch (Exception e) { Console.WriteLine($"   FP64 refused: {e.GetType().Name}"); }

        try
        {
            var kf = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(KernelF);
            using var bufF = accelerator.Allocate1D(hostF);
            kf(n, bufF.View, 0.5f);
            accelerator.Synchronize();
            gf = Gflops(n, Inner, Best(() =>
            {
                var sw = Stopwatch.StartNew();
                kf(n, bufF.View, 0.5f);
                accelerator.Synchronize();
                return sw.Elapsed.TotalSeconds;
            }, 3));
            Console.WriteLine($"   FP32 {gf,10:F1} GFLOP/s");
        }
        catch (Exception e) { Console.WriteLine($"   FP32 refused: {e.GetType().Name}"); }

        if (double.IsFinite(gd) && double.IsFinite(gf))
        {
            Console.WriteLine($"   FP64 : FP32       1 : {gf / gd:F1}");
            Console.WriteLine($"   vs CPU vectorised {gd / vectorised,6:F2}x");
            Console.WriteLine($"   vs CPU scalar     {gd / parallel,6:F2}x   (the flattering one)");
        }
    }
}
```

---

## 9. Results from other machines

Append here rather than replacing section 2, so the comparison across machines survives.

| machine | device | FP64 | FP64:FP32 | vs CPU vectorised |
| --- | --- | --- | --- | --- |
| i9-9900K | GTX 1650 (Cuda) | 90.8 | 1:36.9 | 1.45x |
| i9-9900K | UHD 630 (OpenCL) | 56.2 | 1:3.9 | 0.90x |
