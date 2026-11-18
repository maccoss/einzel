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
/// <para>
/// <b>Only the solves' held sources implement this</b> - a static solve's
/// (<see cref="Solved.StaticHeldConductors"/>, built from the declared potentials) and a driven
/// one (<see cref="Solved.DrivenSolvedField"/>, from its required channel coefficients), in the
/// plane and in a volume. A wrapper that mirrors, rotates or sums fields does not: the assembly
/// hands out each conductor-bearing leaf with the solve that holds it
/// (<see cref="FieldLeaf"/>), so nothing has to forward the question through whatever stands
/// between, and a sum is never asked what a name shared by two elements "holds".
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
}

/// <summary>
/// One conductor-bearing piece of a solved element: where its metal is, and the solve that says
/// what the metal holds.
/// </summary>
/// <param name="Shape">Where the conductors are, in space as the element stands in the model.</param>
/// <param name="Held">The solve that pinned them, asked by conductor name.</param>
/// <param name="Image">
/// Null for the half the solve was declared in; for its image in the mirror plane of a reflected
/// solve, the reflection it is the image through - the same one the field's sum was built with,
/// so the leaf's metal answers exactly where the field's image does. Each is real metal only in
/// its own half: the declared half inside the declared solve box, the image inside that box's
/// reflection.
/// </param>
/// <remarks>
/// Handed out by the builder that made the field, which is the only place that knows a
/// reflected solve is one solved half and its image. Reconstructing that afterwards from the
/// assembled field meant recognising each wrapper type, and treated the declared half's shape
/// as metal in the image's half - an electrode declared running past the mirror plane drew a
/// plateau over the part of it that the mirror replaces.
/// </remarks>
public sealed record ConductorLeaf(IConductorBounded Shape, IHeldConductors Held, Reflection? Image)
{
    /// <summary>The leaves of one solve: its declared half, and its image where it is reflected.</summary>
    /// <param name="direct">The field over the declared half, before any reflection.</param>
    /// <param name="reflection">The solve's reflection, or null where it is not reflected.</param>
    /// <param name="held">The solve that says what the conductors hold.</param>
    /// <returns>One leaf, or two; none where the field has no conductors.</returns>
    /// <remarks>
    /// The image leaf is <see cref="Reflection.Image"/> of the declared half - the very field the
    /// sum adds - so its conductors are gated as the image's field is. It used to be built with no
    /// side, and named image metal on the declared side where the field's image answered nothing.
    /// </remarks>
    public static IReadOnlyList<ConductorLeaf> Of(IElectrostaticField direct, Reflection? reflection, IHeldConductors held)
    {
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(held);

        if (direct is not IConductorBounded shape)
        {
            return [];
        }

        return reflection is { } mirror
            ? [new ConductorLeaf(mirror.Declared(shape), held, null), new ConductorLeaf(mirror.Image(direct), held, mirror)]
            : [new ConductorLeaf(shape, held, null)];
    }
}

/// <summary>A <see cref="ConductorLeaf"/> of an assembled model, with the field element it belongs to.</summary>
/// <param name="Shape">Where the conductors are, in the model's frame - a tilted cross-section's tilt applied.</param>
/// <param name="Held">The solve that pinned them.</param>
/// <param name="Element">The index of the element in <see cref="Core.Model.CompiledModel.Fields"/>.</param>
/// <param name="Image">The reflection this leaf is the image half through; null for a declared half.</param>
/// <param name="Tilt">
/// The rotation a tilted cross-section's element is evaluated through - the element's own
/// rotated field, whose <see cref="RotatedField.ToLocal"/> says where in the unrotated solve a
/// point is asked; null where the element is not tilted.
/// </param>
public sealed record FieldLeaf(IConductorBounded Shape, IHeldConductors Held, int Element, Reflection? Image, RotatedField? Tilt = null);
