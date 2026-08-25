using Einzel.Analysis;
using Einzel.Transport;

namespace Einzel.Commands;

/// <summary>
/// What one flight of a source cloud produced.
/// </summary>
/// <param name="Peak">The arrival-time peak the cloud formed at the detector.</param>
/// <param name="Arrived">
/// The ions that reached it, in the state they reached it in. Shorter than the
/// launched cloud whenever the geometry lost some, which is the point.
/// </param>
/// <remarks>
/// Two views of one run. Arrival times give peak shape, resolving power and
/// transmission; the final states give emittance and everything else about where
/// the packet is and which way it is going. Keeping both means an ensemble run
/// answers "how sharp" and "will it fit" without flying twice.
/// </remarks>
public sealed record CloudFlight(ArrivalTimePeak Peak, IReadOnlyList<PhaseState> Arrived);
