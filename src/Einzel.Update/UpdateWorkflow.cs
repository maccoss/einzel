namespace Einzel.Update;

/// <summary>Where the launch-time update check stands.</summary>
public enum UpdateStage
{
    /// <summary>Not yet asked.</summary>
    Idle,

    /// <summary>Not a copy Velopack manages (a development build or an unpacked archive), so nothing is checked.</summary>
    NotManaged,

    /// <summary>The feed is being asked.</summary>
    Checking,

    /// <summary>The feed has nothing newer.</summary>
    UpToDate,

    /// <summary>The feed could not be reached or read. Logged, never shown.</summary>
    CheckFailed,

    /// <summary>A newer release exists and is offered; nothing has been downloaded.</summary>
    Available,

    /// <summary>The person asked for it and it is downloading.</summary>
    Downloading,

    /// <summary>The person asked for it and the download failed. Shown, because they asked.</summary>
    DownloadFailed,

    /// <summary>Downloaded and staged: applied on restart, or at the next launch.</summary>
    Ready,

    /// <summary>The person closed the notice; it comes back at the next launch.</summary>
    Dismissed,

    /// <summary>The offered release is one the person asked to skip.</summary>
    Skipped,
}

/// <summary>
/// The shell's update check: once, at launch, offered and never imposed (section 18).
/// </summary>
/// <remarks>
/// <para>
/// <b>Once per process, and only when asked at launch (UPD-1).</b> LabOps re-checks every four
/// hours; Einzel deliberately does not. An agent may be issuing hundreds of <c>einzel</c>
/// commands against the installed CLI, and AGT-8 says it sees one version throughout - so there
/// is no timer here, and a second call to <see cref="CheckAtLaunchAsync"/> does nothing.
/// </para>
/// <para>
/// <b>Offered, never imposed (UPD-3, UPD-4).</b> A newer release is a notice, not a dialog;
/// nothing is downloaded until <see cref="DownloadAsync"/> is called from a person's click, and
/// nothing is applied until they restart or next launch the window. A failed check is logged and
/// not shown, because no network is an ordinary state and not something a person can act on.
/// </para>
/// <para>
/// <b>Never while a run is active (UPD-6).</b> The restart is refused while the window reports a
/// run in flight, and the download is staged rather than lost, so it applies at the next launch.
/// </para>
/// <para>
/// Every decision lives here rather than in the window, so it is tested without one. Members
/// may be called from the window's thread; <see cref="Changed"/> may be raised from another,
/// and the window marshals it.
/// </para>
/// </remarks>
public sealed class UpdateWorkflow
{
    private readonly IUpdateFeed? _feed;
    private readonly ISkippedVersionStore _skipped;
    private readonly Action<string> _log;
    private int _asked;

    /// <summary>Creates the workflow.</summary>
    /// <param name="feed">The installed copy's feed, or null for a copy Velopack does not manage.</param>
    /// <param name="skipped">Where a skipped version is remembered.</param>
    /// <param name="log">Where failures are recorded.</param>
    public UpdateWorkflow(IUpdateFeed? feed, ISkippedVersionStore skipped, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        ArgumentNullException.ThrowIfNull(log);

        _feed = feed;
        _skipped = skipped;
        _log = log;
    }

    /// <summary>Raised whenever <see cref="Stage"/>, the progress or the message changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Where the check stands.</summary>
    public UpdateStage Stage { get; private set; } = UpdateStage.Idle;

    /// <summary>The version offered, once one has been found.</summary>
    public string? AvailableVersion { get; private set; }

    /// <summary>Download progress, percent.</summary>
    public int Percent { get; private set; }

    /// <summary>Why the download failed, when it did.</summary>
    public string? Error { get; private set; }

    /// <summary>Whether a restart was asked for and refused because a run was active.</summary>
    public bool RestartDeferred { get; private set; }

    /// <summary>Whether the window should show the notice.</summary>
    public bool IsVisible => Stage is UpdateStage.Available or UpdateStage.Downloading
        or UpdateStage.DownloadFailed or UpdateStage.Ready;

    /// <summary>Whether a download may be started now.</summary>
    public bool CanDownload => Stage is UpdateStage.Available or UpdateStage.DownloadFailed;

    /// <summary>Whether the offered release may be skipped now.</summary>
    public bool CanSkip => Stage is UpdateStage.Available or UpdateStage.DownloadFailed;

    /// <summary>What the notice says.</summary>
    public string Message => Stage switch
    {
        UpdateStage.Available =>
            $"Einzel {AvailableVersion} is available (this copy is {_feed?.CurrentVersion}). Nothing is downloaded until you choose.",
        UpdateStage.Downloading => $"Downloading Einzel {AvailableVersion}: {Percent}%",
        UpdateStage.DownloadFailed => $"Einzel {AvailableVersion} could not be downloaded: {Error}",
        UpdateStage.Ready when RestartDeferred =>
            $"Einzel {AvailableVersion} is downloaded. A run is in progress, so it was not applied: restart when the run ends, or it is installed the next time Einzel starts.",
        UpdateStage.Ready =>
            $"Einzel {AvailableVersion} is downloaded. Restart to use it, or it is installed the next time Einzel starts.",
        _ => string.Empty,
    };

    /// <summary>Checks the feed, once per process.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the check has finished or been skipped.</returns>
    /// <remarks>Never throws: a check that fails is logged and the window carries on.</remarks>
    public async Task CheckAtLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _asked, 1) == 1)
        {
            return;
        }

        if (_feed is null)
        {
            Set(UpdateStage.NotManaged);

            return;
        }

        Set(UpdateStage.Checking);

        string? newer;

        try
        {
            newer = await _feed.FindNewerAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any failure to reach the feed is the same outcome: no offer, a log line.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"update check failed: {ex.GetType().Name}: {ex.Message}");
            Set(UpdateStage.CheckFailed);

            return;
        }

        if (newer is null)
        {
            Set(UpdateStage.UpToDate);

            return;
        }

        AvailableVersion = newer;

        Set(string.Equals(_skipped.Read(), newer, StringComparison.Ordinal)
            ? UpdateStage.Skipped
            : UpdateStage.Available);
    }

    /// <summary>Downloads the offered release, because a person asked.</summary>
    /// <param name="restartWhenDone">Restart into it as soon as it is staged, unless a run is active.</param>
    /// <param name="runActive">Asked when the download finishes: whether the window has a run in flight.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when the download has finished or failed.</returns>
    public async Task DownloadAsync(bool restartWhenDone, Func<bool> runActive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runActive);

        if (_feed is null || !CanDownload)
        {
            return;
        }

        Error = null;
        Percent = 0;
        Set(UpdateStage.Downloading);

        try
        {
            await _feed.DownloadAsync(
                p =>
                {
                    Percent = Math.Clamp(p, 0, 100);
                    Changed?.Invoke(this, EventArgs.Empty);
                },
                cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Shown to the person who asked, and logged; the window carries on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"downloading {AvailableVersion} failed: {ex.GetType().Name}: {ex.Message}");
            Error = ex.Message;
            Set(UpdateStage.DownloadFailed);

            return;
        }

        Set(UpdateStage.Ready);

        if (restartWhenDone)
        {
            RestartNow(runActive);
        }
    }

    /// <summary>Applies the staged release and restarts, unless a run is active.</summary>
    /// <param name="runActive">Whether the window has a run in flight.</param>
    /// <returns>False when nothing is staged or a run is active; on success the process ends.</returns>
    public bool RestartNow(Func<bool> runActive)
    {
        ArgumentNullException.ThrowIfNull(runActive);

        if (_feed is null || Stage != UpdateStage.Ready)
        {
            return false;
        }

        if (runActive())
        {
            RestartDeferred = true;
            Changed?.Invoke(this, EventArgs.Empty);

            return false;
        }

        RestartDeferred = false;
        _feed.ApplyAndRestart();

        return true;
    }

    /// <summary>Closes the notice until the next launch. A staged release stays staged.</summary>
    public void Dismiss()
    {
        if (IsVisible && Stage != UpdateStage.Downloading)
        {
            Set(UpdateStage.Dismissed);
        }
    }

    /// <summary>Never offers this release again; a later one is still offered.</summary>
    public void Skip()
    {
        if (!CanSkip || AvailableVersion is null)
        {
            return;
        }

        try
        {
            _skipped.Write(AvailableVersion);
        }
#pragma warning disable CA1031 // Failing to remember is not worth an error; the notice still closes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"could not remember skipping {AvailableVersion}: {ex.Message}");
        }

        Set(UpdateStage.Skipped);
    }

    private void Set(UpdateStage stage)
    {
        Stage = stage;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
