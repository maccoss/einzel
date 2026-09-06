using Einzel.Analysis;
using Einzel.Transport;

namespace Einzel.Commands;

/// <summary>
/// Where one ion ended up, when it did not reach the detector.
/// </summary>
/// <param name="Surface">
/// The electrode it struck, or a description of how else it was lost.
/// </param>
/// <param name="Ions">How many ions were lost that way.</param>
/// <remarks>
/// ACC-5: "Transmission itemized by loss surface and mechanism, with intervals.
/// Never 92 percent." A bare percentage says an instrument loses ions; a named
/// surface says which one to move.
/// </remarks>
public sealed record LossChannel(string Surface, int Ions);

/// <summary>
/// What became of one ion of a cloud, and when: the raw ledger the arrival counts,
/// the losses by surface and the arrival-time peak are all computed from.
/// </summary>
/// <param name="Ion">The ion's index in the cloud, from zero.</param>
/// <param name="Outcome">The integrator's outcome name: <c>StopConditionMet</c> for an arrival, <c>StruckElectrode</c>, <c>MaximumFlightTimeReached</c>, and so on.</param>
/// <param name="Surface">The electrode struck, by the name the model author wrote; null unless the outcome is a strike.</param>
/// <param name="TimeSeconds">When the flight ended, in seconds from launch.</param>
/// <remarks>
/// Kept per ion rather than summarised because a mass scan is read off exactly this: an
/// ion's ejection instant is its mass on the scan's axis, so a spectrum is a histogram
/// of these times and cannot be recovered from a peak width and a transmission. The end
/// position says where on a surface an ion struck, which is how an ejection slot's
/// efficiency is attributed - to the slot's width, or to the ions' spread.
/// </remarks>
public sealed record IonEvent(int Ion, string Outcome, string? Surface, double TimeSeconds)
{
    /// <summary>Where the flight ended, in metres: the impact point for a strike, the crossing for an arrival.</summary>
    public Core.Geometry.Vec3 Position { get; init; }
}

/// <summary>
/// What one flight of a source cloud produced.
/// </summary>
/// <param name="Peak">The arrival-time peak the cloud formed at the detector.</param>
/// <param name="Arrived">
/// The ions that reached it, in the state they reached it in. Shorter than the
/// launched cloud whenever the geometry lost some, which is the point.
/// </param>
/// <param name="Losses">
/// Where the rest went, by surface, largest first and then alphabetical so the
/// ordering is deterministic (CLI-5).
/// </param>
/// <remarks>
/// Three views of one run. Arrival times give peak shape, resolving power and
/// transmission; the final states give emittance and everything else about where
/// the packet is and which way it is going; the losses say which surface to blame.
/// Keeping all three means an ensemble run answers "how sharp", "will it fit" and
/// "where did the rest go" without flying three times.
/// </remarks>
/// <param name="Collisions">
/// How many collisions the whole ensemble made. Zero in vacuum.
/// </param>
/// <remarks>
/// <see cref="Peak"/> is null when fewer than two ions arrived, because an arrival
/// peak needs two points to have a width. <strong>The flight is still a result.</strong>
/// A packet that loses everything is exactly the case ACC-5 exists for - the losses
/// are itemised by the surface the model author named, and they are most worth
/// reading when the transmission is zero. Treating no-peak as no-flight threw that
/// away at the one point it mattered.
/// </remarks>
/// <param name="ScatteredIons">
/// How many ions collided at least once. Reported beside the arrival times because
/// COL-1 keeps scattered ions that stay within acceptance rather than discarding
/// them, so a count of zero and a count equal to the ensemble mean very different
/// peaks and neither is visible from a transmission figure.
/// </param>
public sealed record CloudFlight(
    ArrivalTimePeak? Peak,
    IReadOnlyList<PhaseState> Arrived,
    IReadOnlyList<LossChannel> Losses,
    int Collisions = 0,
    int ScatteredIons = 0)
{
    /// <summary>
    /// Members still in flight at the time limit, having struck nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The states a packet that never arrives still has. A thermalised cloud in a gas
    /// has no preferred direction and reaches no detector, so everything measurable
    /// about it is here rather than in <see cref="Arrived"/> - and a figure that read
    /// the arrivals would be selecting the fast members and reporting a temperature too
    /// high.
    /// </para>
    /// <para>
    /// Distinct from the loss channel of the same name, which counts them. This carries
    /// what they were doing.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PhaseState> Remaining { get; init; } = [];

    /// <summary>Every launched ion's outcome and end time, in launch order.</summary>
    public IReadOnlyList<IonEvent> Events { get; init; } = [];
}
