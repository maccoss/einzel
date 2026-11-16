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
/// <para>
/// <b>Asked by name, not by point.</b> Which conductor a point is in, and whether the point is
/// where that conductor's element was solved at all, are the caller's questions - the probe
/// asks them once, with the same domain test it reports, and hands the answer down. A
/// declared shape runs on past the edge of its solve, so a field answering "the potential at
/// this point" from the shape alone drew a plateau where nothing had been solved.
/// </para>
/// </remarks>
public interface IHeldConductors
{
    /// <summary>The potential a named conductor of this field holds at an instant, in volts.</summary>
    /// <param name="conductor">The conductor's name, as <see cref="IConductorBounded.ConductorAt"/> gives it.</param>
    /// <param name="timeSeconds">The instant; ignored by a static field.</param>
    /// <returns>
    /// The potential; null where this field has no conductor of that name, and where the
    /// conductor has no single potential - an edge-profiled board, whose potential varies
    /// along it.
    /// </returns>
    double? HeldPotentialOf(string conductor, double timeSeconds);

    /// <summary>The mean of <see cref="HeldPotentialOf"/> over a window.</summary>
    /// <param name="conductor">The conductor's name.</param>
    /// <param name="fromSeconds">Where the window opens.</param>
    /// <param name="periodSeconds">How long it is.</param>
    /// <param name="samples">How many instants to average where it cannot be taken in closed form.</param>
    /// <returns>The mean; null where <see cref="HeldPotentialOf"/> is.</returns>
    /// <remarks>
    /// Sampled by default. A driven solve takes it in closed form from its channels' mean
    /// weights, as its potential's cycle mean is, so a conductor carrying only RF averages to
    /// exactly zero rather than to the rounding of a sum.
    /// </remarks>
    double? HeldCycleMeanOf(string conductor, double fromSeconds, double periodSeconds, int samples) =>
        HeldConductors.SampledMean(this, conductor, fromSeconds, periodSeconds, samples);
}

/// <summary>What the implementations of <see cref="IHeldConductors"/> share.</summary>
public static class HeldConductors
{
    /// <summary>The mean of a conductor's held potential over a window, by sampling it.</summary>
    /// <param name="field">The field.</param>
    /// <param name="conductor">The conductor's name.</param>
    /// <param name="fromSeconds">Where the window opens.</param>
    /// <param name="periodSeconds">How long it is.</param>
    /// <param name="samples">How many instants.</param>
    /// <returns>The mean, or null where the field holds no such conductor.</returns>
    public static double? SampledMean(
        IHeldConductors field, string conductor, double fromSeconds, double periodSeconds, int samples)
    {
        ArgumentNullException.ThrowIfNull(field);

        var total = 0.0;

        for (var j = 0; j < samples; j++)
        {
            if (field.HeldPotentialOf(conductor, fromSeconds + (periodSeconds * j / samples)) is not { } value)
            {
                return null;
            }

            total += value;
        }

        return total / samples;
    }

    /// <summary>
    /// Which member of a sum holds the conductor at a point: the one whose conductors the
    /// point is deepest inside, or nearest to - the smallest signed distance, the first on a tie.
    /// </summary>
    /// <param name="members">The members of the sum, in order.</param>
    /// <param name="position">The point, in metres.</param>
    /// <returns>The member's index, or -1 where no member has conductors.</returns>
    /// <remarks>
    /// One rule for every composite, static and driven. They used to differ - the static sum
    /// took the nearest, the driven one the first member containing the point - so adding an RF
    /// element to a model could change which of two overlapping conductors a point was credited
    /// to.
    /// </remarks>
    public static int Nearest(IReadOnlyList<IElectrostaticField> members, in Vec3 position)
    {
        ArgumentNullException.ThrowIfNull(members);

        var index = -1;
        var nearest = double.PositiveInfinity;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is IConductorBounded bounded)
            {
                var distance = bounded.SignedDistanceToConductor(in position);

                if (distance < nearest)
                {
                    nearest = distance;
                    index = i;
                }
            }
        }

        return index;
    }

    /// <summary>The first member of a sum that holds a conductor of this name.</summary>
    /// <param name="members">The members.</param>
    /// <param name="conductor">The name.</param>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>Its held potential, or null where none does.</returns>
    public static double? FirstHolding(IReadOnlyList<IElectrostaticField> members, string conductor, double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(members);

        foreach (var member in members)
        {
            if (member is IHeldConductors held && held.HeldPotentialOf(conductor, timeSeconds) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The first member of a sum whose cycle mean for a conductor of this name exists.</summary>
    /// <param name="members">The members.</param>
    /// <param name="conductor">The name.</param>
    /// <param name="fromSeconds">Where the window opens.</param>
    /// <param name="periodSeconds">How long it is.</param>
    /// <param name="samples">How many instants, where sampled.</param>
    /// <returns>The mean, or null where no member holds the conductor.</returns>
    public static double? FirstMean(
        IReadOnlyList<IElectrostaticField> members, string conductor, double fromSeconds, double periodSeconds, int samples)
    {
        ArgumentNullException.ThrowIfNull(members);

        foreach (var member in members)
        {
            if (member is IHeldConductors held && held.HeldCycleMeanOf(conductor, fromSeconds, periodSeconds, samples) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
