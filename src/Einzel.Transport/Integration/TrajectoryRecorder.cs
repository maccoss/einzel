using Einzel.Core.Geometry;

namespace Einzel.Transport.Integration;

/// <summary>One sampled point on a trajectory.</summary>
/// <param name="TimeSeconds">Elapsed flight time.</param>
/// <param name="Position">Position, in metres.</param>
/// <param name="Velocity">Velocity, in metres per second.</param>
public readonly record struct TrajectorySample(double TimeSeconds, Vec3 Position, Vec3 Velocity);

/// <summary>
/// Records a trajectory for rendering and export, on its own cadence.
/// </summary>
/// <remarks>
/// <para>
/// TRJ-1: "Trajectory output for rendering is a separately sampled stream with
/// its own cadence, independent of integration steps. Integration steps cluster
/// where the physics is hard, which is not where a picture needs points; and a
/// full step record for 10^4 ions is unrenderable regardless."
/// </para>
/// <para>
/// Both halves of that show up here. Inside a mirror the integrator may take
/// hundreds of steps across a few millimetres, which a figure does not need; over
/// a metres-long field-free drift it takes one analytic advance, which a figure
/// does need both ends of. So the recorder samples on elapsed time, and
/// additionally forces a sample at each end of an analytic advance so a straight
/// segment keeps its endpoints.
/// </para>
/// <para>
/// Every sample carries the true time, position, and velocity at that point. The
/// cadence controls how often a sample is taken, never what it claims.
/// </para>
/// <para>
/// One limit worth stating plainly, because the requirement says "independent of
/// integration steps" and this is independent in only one direction. Samples are
/// offered at accepted steps and at the ends of analytic advances, so the stream
/// can be <em>coarser</em> than the steps but never <em>finer</em>: asking for a
/// 1 ns cadence across a region the integrator crosses in 50 ns steps yields
/// samples every 50 ns, not every 1 ns. Full independence needs dense output —
/// the interpolating polynomial Dormand-Prince can evaluate anywhere inside a
/// step — which is not implemented here. In practice the gap is narrow: where
/// steps are long the motion is either field-free, and advanced exactly as a
/// straight line that needs only its two endpoints, or smooth enough that the
/// controller had no reason to refine.
/// </para>
/// <para>
/// Supplying a recorder makes the integration allocate, in proportion to the
/// sample count. The allocation-free guarantee in
/// <see cref="TrajectoryIntegrator"/> applies when no recorder is supplied, which
/// is the case for ensembles and optimisation, where nothing is being drawn.
/// </para>
/// </remarks>
public sealed class TrajectoryRecorder
{
    private readonly List<TrajectorySample> _samples = [];
    private double _nextSampleTime;

    /// <summary>Creates a recorder sampling at the given cadence.</summary>
    /// <param name="intervalSeconds">Nominal interval between samples, in seconds.</param>
    /// <param name="capacity">
    /// Ceiling on retained samples, as a guard against an unrenderable file.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The interval or capacity is not positive.</exception>
    public TrajectoryRecorder(double intervalSeconds, int capacity = 200_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        IntervalSeconds = intervalSeconds;
        Capacity = capacity;
    }

    /// <summary>The nominal sampling interval, in seconds.</summary>
    public double IntervalSeconds { get; }

    /// <summary>Ceiling on retained samples.</summary>
    public int Capacity { get; }

    /// <summary>The samples, in flight order.</summary>
    public IReadOnlyList<TrajectorySample> Samples => _samples;

    /// <summary>
    /// Whether the capacity was reached and later samples were dropped. Reported
    /// rather than silent: spec section 22 warns against caps that read as
    /// complete coverage when they are not.
    /// </summary>
    public bool Truncated { get; private set; }

    internal void Offer(double time, in PhaseState state, bool force)
    {
        // Never two samples at the same instant. The launch point and the start of
        // the first analytic advance are the same state, and a duplicated vertex
        // makes a zero-length segment in the exported polyline.
        if (_samples.Count > 0 && time <= _samples[^1].TimeSeconds)
        {
            return;
        }

        if (!force && _samples.Count > 0 && time < _nextSampleTime)
        {
            return;
        }

        if (_samples.Count >= Capacity)
        {
            Truncated = true;
            return;
        }

        _samples.Add(new TrajectorySample(time, state.Position, state.Velocity));
        _nextSampleTime = time + IntervalSeconds;
    }
}
