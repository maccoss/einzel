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
public sealed record CloudFlight(
    ArrivalTimePeak Peak,
    IReadOnlyList<PhaseState> Arrived,
    IReadOnlyList<LossChannel> Losses);
