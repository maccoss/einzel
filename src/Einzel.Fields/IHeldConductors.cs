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

    /// <summary>What a wrapper forwards: the inner field's held potential for a conductor, if it holds any.</summary>
    /// <param name="inner">The wrapped field.</param>
    /// <param name="conductor">The conductor's name.</param>
    /// <param name="timeSeconds">The instant, on the inner field's clock.</param>
    /// <returns>The potential, or null where the inner field holds no such conductor.</returns>
    public static double? Forward(IElectrostaticField inner, string conductor, double timeSeconds) =>
        inner is IHeldConductors held ? held.HeldPotentialOf(conductor, timeSeconds) : null;

    /// <summary>What a wrapper forwards for a cycle mean.</summary>
    /// <param name="inner">The wrapped field.</param>
    /// <param name="conductor">The conductor's name.</param>
    /// <param name="fromSeconds">Where the window opens, on the inner field's clock.</param>
    /// <param name="periodSeconds">How long it is.</param>
    /// <param name="samples">How many instants, where sampled.</param>
    /// <returns>The mean, or null where the inner field holds no such conductor.</returns>
    public static double? ForwardMean(
        IElectrostaticField inner, string conductor, double fromSeconds, double periodSeconds, int samples) =>
        inner is IHeldConductors held ? held.HeldCycleMeanOf(conductor, fromSeconds, periodSeconds, samples) : null;

    /// <summary>
    /// What a sum's members say a conductor of this name holds, when they agree; null when no
    /// member holds one or when two hold different values.
    /// </summary>
    /// <param name="members">The members.</param>
    /// <param name="conductor">The name.</param>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The one value every member holding the name gives, or null.</returns>
    /// <remarks>
    /// A name is unique within one element and no further, so two elements may each have a
    /// <c>plate</c>, one at +100 V and one at -100 V. Answering with the first member that has
    /// the name gave both +100 V to anything asking the sum. A sum answers only where the name
    /// cannot mean two things: one member has it, or every member that has it agrees - which is
    /// the case a reflected solve makes, its one element seen twice. A caller that knows which
    /// element it means asks that element, as the probe does.
    /// </remarks>
    public static double? Agreed(IReadOnlyList<IElectrostaticField> members, string conductor, double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(members);

        double? agreed = null;

        foreach (var member in members)
        {
            if (member is IHeldConductors held && held.HeldPotentialOf(conductor, timeSeconds) is { } value)
            {
                if (agreed is { } other && other != value)
                {
                    return null;
                }

                agreed = value;
            }
        }

        return agreed;
    }

    /// <summary>The same as <see cref="Agreed"/>, for a cycle mean.</summary>
    /// <param name="members">The members.</param>
    /// <param name="conductor">The name.</param>
    /// <param name="fromSeconds">Where the window opens.</param>
    /// <param name="periodSeconds">How long it is.</param>
    /// <param name="samples">How many instants, where sampled.</param>
    /// <returns>The one mean every member holding the name gives, or null.</returns>
    public static double? AgreedMean(
        IReadOnlyList<IElectrostaticField> members, string conductor, double fromSeconds, double periodSeconds, int samples)
    {
        ArgumentNullException.ThrowIfNull(members);

        double? agreed = null;

        foreach (var member in members)
        {
            if (member is IHeldConductors held && held.HeldCycleMeanOf(conductor, fromSeconds, periodSeconds, samples) is { } value)
            {
                if (agreed is { } other && other != value)
                {
                    return null;
                }

                agreed = value;
            }
        }

        return agreed;
    }
}
