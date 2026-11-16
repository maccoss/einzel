using Einzel.Core.Geometry;

namespace Einzel.Fields;

/// <summary>
/// A field that knows what potential each of its conductors holds, not only where they are.
/// </summary>
/// <remarks>
/// <para>
/// <b>A conductor's potential is what the solve pinned there</b>, and reading it back off the
/// field by sampling is wrong two ways. An interpolant overshoots inside a thin conductor -
/// a 20 V lens read 20.13 V a sample inside it - because its stencil reaches across the
/// surface. And a sum of elements carries every element's potential inside every conductor,
/// so an analytic element superposed over a solved geometry - which has no conductors and
/// fills all space - put a 260 V RF quadrupole inside a ring the solve held at 10 V.
/// </para>
/// <para>
/// So the answer comes from the same coefficients the mask was built with: a static solve
/// holds each electrode at its declared potential, and a driven or staged one holds it at the
/// sum of its channels' weights at the instant times the electrode's coefficient in each
/// channel's pattern. Exact, not interpolated, and blind to anything summed alongside.
/// </para>
/// </remarks>
public interface IHeldConductors
{
    /// <summary>The potential the conductor containing a point holds at an instant, in volts.</summary>
    /// <param name="position">A point, in metres, inside the conductor.</param>
    /// <param name="timeSeconds">The instant; ignored by a static field.</param>
    /// <returns>
    /// The potential; null where no conductor of this field contains the point, and where the
    /// conductor has no single potential - an edge-profiled board, whose potential varies along
    /// it.
    /// </returns>
    double? HeldPotentialAt(in Vec3 position, double timeSeconds);

    /// <summary>The mean of <see cref="HeldPotentialAt"/> over a window.</summary>
    /// <param name="position">A point, in metres, inside the conductor.</param>
    /// <param name="fromSeconds">Where the window opens.</param>
    /// <param name="periodSeconds">How long it is.</param>
    /// <param name="samples">How many instants to average where it cannot be taken in closed form.</param>
    /// <returns>The mean; null where <see cref="HeldPotentialAt"/> is.</returns>
    /// <remarks>
    /// Sampled by default. A driven solve takes it in closed form from its channels' mean
    /// weights, as its potential's cycle mean is, so a conductor carrying only RF averages to
    /// exactly zero rather than to the rounding of a sum.
    /// </remarks>
    double? HeldCycleMeanAt(in Vec3 position, double fromSeconds, double periodSeconds, int samples)
    {
        var total = 0.0;

        for (var j = 0; j < samples; j++)
        {
            if (HeldPotentialAt(in position, fromSeconds + (periodSeconds * j / samples)) is not { } value)
            {
                return null;
            }

            total += value;
        }

        return total / samples;
    }
}
