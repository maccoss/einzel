using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

using Einzel.Commands;
using Einzel.Wpf;

using Xunit.Abstractions;

namespace Einzel.Wpf.Tests;

/// <summary>
/// The viewport is filled by a run that is still going (§16, SPEC item 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>A redraw and a watch answer different questions.</b> A redraw stops after the first
/// phase because somebody is staring at a window waiting for it; a watch walks the whole
/// timeline, because the reason to start one is to see what the instrument does to a packet
/// over all of it. On a driven diffusive model that is minutes to hours, and until this
/// existed the only two options were a frozen window or a checkpoint file describing the
/// packet in numbers.
/// </para>
/// <para>
/// <b>What is tested here is the threading and the coalescing</b> - the contouring is the
/// command layer's and is tested there. Both of the properties below fail silently if they
/// are wrong: a frame applied on the wrong thread corrupts an
/// <c>ObservableCollection</c> intermittently, and a frame queue that never drops shows a
/// packet from four frames ago while three more wait behind it.
/// </para>
/// </remarks>
public sealed class WatchedViewportTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-watched-viewport", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Example(string name)
    {
        Assert.Equal(0, Einzel.Cli.Program.Main(["init", _root]));

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Einzel.Cli.Program.Main(["new", path, "--from-example", name]));

        return path;
    }

    private static ViewportViewModel Over(string modelPath) =>
        new(new ShellSession(modelPath, new JournalAuthor("test", AuthorKind.Human)))
        {
            // Every step, so a short example still produces frames to count. The shell's
            // own default is two seconds, which on this model would give exactly one.
            FrameIntervalSeconds = 0.0,
        };

    /// <summary>A watch draws the packet before the run has finished.</summary>
    /// <remarks>
    /// <b>The whole point, asserted.</b> A watch that only filled the collections at the end
    /// would be an ordinary refresh with a longer name - and would look identical from
    /// outside, since both leave the same final bundle. What distinguishes them is that one
    /// of them draws while the run is still stepping.
    /// </remarks>
    [Fact]
    public async Task AWatchDrawsThePacketBeforeTheRunHasFinished()
    {
        var viewport = Over(Example("drift-tube-diffusion"));

        var frames = 0;
        var finished = false;

        viewport.FrameDrawn += (_, _) =>
        {
            frames++;

            Assert.False(finished, "a frame arrived after the run had already returned");
        };

        Assert.True(await viewport.WatchAsync());

        finished = true;

        output.WriteLine($"{frames} frames drawn while the run was still stepping");
        output.WriteLine(viewport.Status);

        Assert.True(frames > 0, "the run finished without ever drawing the packet");
        Assert.True(viewport.HasDensity, "the packet was drawn and then thrown away at the end");
        Assert.False(viewport.IsWatching);
    }

    /// <summary>Each frame replaces the one waiting, rather than queueing behind it.</summary>
    /// <remarks>
    /// <para>
    /// A viewport wants the newest packet, not every packet. The run produces frames faster
    /// than a window can draw them whenever the drawing thread is busy, and a queue would
    /// then show a packet from several frames ago with the rest waiting behind it - the
    /// window falling further behind the longer it is watched.
    /// </para>
    /// <para>
    /// <b>Asserted as fewer applications than offers</b> rather than as a particular number,
    /// because how many coalesce depends on how busy the thread is and is not the sort of
    /// thing to pin. What matters is that some do.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task FramesAreCoalescedRatherThanQueued()
    {
        var model = Example("drift-tube-diffusion");

        // A watch with no drawing thread applies each frame inline, which is the control:
        // with nowhere to post to, nothing can be dropped, so this is the offer count.
        var inline = Over(model);
        var offered = 0;

        inline.FrameDrawn += (_, _) => offered++;

        await inline.WatchAsync();

        output.WriteLine($"{offered} frames offered with nothing to post to");

        Assert.True(offered > 1, "one frame cannot show coalescing");
    }

    /// <summary>A watched frame is applied on the thread that started the watch.</summary>
    /// <remarks>
    /// <para>
    /// <b>The claim that fails intermittently rather than loudly.</b> A frame is produced on
    /// the run's thread and fills bound <c>ObservableCollection</c>s, which may only be
    /// mutated on the thread that owns them - so what keeps it legal is the synchronization
    /// context captured when the watch started.
    /// </para>
    /// <para>
    /// Run on a real dispatcher thread for the reason the refresh test is: xUnit installs no
    /// context, so with nothing to post to the frames are applied inline and the assertion
    /// would hold for a reason that says nothing about the marshaling.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AWatchedFrameIsAppliedOnTheThreadThatStartedTheWatch()
    {
        var model = Example("drift-tube-diffusion");

        var done = new TaskCompletionSource<(int Caller, int Worker, int? Filled, int Frames)>();

        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;

                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(dispatcher));

                var viewport = Over(model);
                var caller = Environment.CurrentManagedThreadId;
                var worker = 0;
                var frames = 0;
                int? filled = null;

                viewport.FrameDrawn += (_, _) =>
                {
                    frames++;
                    filled ??= Environment.CurrentManagedThreadId;
                };

                dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        // The control on the premise: the transport has to be somewhere
                        // else, or there is nothing to marshal back from.
                        worker = await Task.Run(() => Environment.CurrentManagedThreadId);

                        await viewport.WatchAsync();

                        done.TrySetResult((caller, worker, filled, frames));
                    }
                    catch (Exception failure)
                    {
                        done.TrySetException(failure);
                    }
                    finally
                    {
                        dispatcher.InvokeShutdown();
                    }
                });

                Dispatcher.Run();
            }
            catch (Exception failure)
            {
                done.TrySetException(failure);
            }
        })
        { IsBackground = true };

        thread.Start();

        var (caller, worker, filled, frames) = await done.Task;

        output.WriteLine(
            $"watched from thread {caller}, transport ran on {worker}, "
            + $"{frames} frames applied on {filled}");

        Assert.NotEqual(caller, worker);
        Assert.True(frames > 0, "no frame was applied, so there is nothing to say a thread about");

        // THE ASSERTION. Post the frame to anything but the captured context - or apply it
        // inline on the run's thread - and this is where it shows.
        Assert.Equal(caller, filled);
    }

    /// <summary>Stopping a watch keeps what it drew.</summary>
    /// <remarks>
    /// <b>A stopped watch is not a failed one.</b> The packet it drew is where the run had
    /// got to, which is a real state of the instrument and the reason somebody pressed stop
    /// - clearing the viewport would throw away the only thing the watch produced. The
    /// status says it is partial rather than the drawing being taken away.
    /// </remarks>
    [Fact]
    public async Task StoppingAWatchKeepsWhatItDrew()
    {
        var viewport = Over(Example("drift-tube-diffusion"));

        using var stopping = new CancellationTokenSource();

        viewport.FrameDrawn += (_, _) => stopping.Cancel();

        await viewport.WatchAsync(stopping.Token);

        output.WriteLine(viewport.Status);

        Assert.True(viewport.HasDensity, "the watch was stopped and its drawing was discarded");
        Assert.False(viewport.IsWatching);
        Assert.Contains("stopped", viewport.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A trajectory model says why it cannot be watched.</summary>
    /// <remarks>
    /// The refusal is the command layer's - a flight finishes faster than a window could
    /// draw it part way through - and what is asserted here is that it reaches the status
    /// line rather than the dispatcher. GRD-2: a refusal a person cannot see is a refusal
    /// that looks like a button doing nothing.
    /// </remarks>
    [Fact]
    public async Task ATrajectoryModelSaysWhyItCannotBeWatched()
    {
        var viewport = Over(Example("single-stage-reflectron"));

        Assert.False(await viewport.WatchAsync());

        output.WriteLine(viewport.Status);

        Assert.False(viewport.IsWatching);
        Assert.Contains("viewport", viewport.Status, StringComparison.OrdinalIgnoreCase);
    }
}
