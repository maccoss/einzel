using System.Globalization;

using Einzel.Core.Errors;

namespace Einzel.Transport;

/// <summary>
/// A density that is not a number in some cell, or whose population overflows, refused where it
/// is converted to trajectories.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own type, so that a caller recognises it by what it is.</b> The conversion is general
/// and does not know which part of a document the density came from, so the error it carries is
/// unlocated (<c>/</c>) and its suggestion says only what went wrong. A caller that knows - a
/// sequence, which knows the phase that stepped the density - catches this type and locates it.
/// Recognising it by its code and an unlocated path instead would couple the caller to a
/// convention nothing states.
/// </para>
/// <para>
/// An <see cref="EinzelException"/> all the same, so a caller that does not locate it still
/// reports the AGT-3 error.
/// </para>
/// </remarks>
public sealed class NonFiniteDensityException : EinzelException
{
    /// <summary>Refuses a density by the cell it is not finite in.</summary>
    /// <param name="i">The cell's index along x.</param>
    /// <param name="j">The cell's index along y.</param>
    /// <param name="x">The cell's x, in metres.</param>
    /// <param name="y">The cell's y, in metres.</param>
    /// <param name="value">What the density held there, per cubic metre.</param>
    /// <param name="overflow">
    /// What overflowed where the density itself is finite, or <see cref="Overflow.None"/> where
    /// the density is not a number.
    /// </param>
    public NonFiniteDensityException(int i, int j, double x, double y, double value, Overflow overflow = Overflow.None)
        : base(new EinzelError
        {
            Code = ErrorCodes.ConvergenceFailed,
            Path = "/",
            Constraint = Sentence(i, j, x, y, overflow),
            Observed = new ObservedValue(value, "m^-3"),
            Suggestion = overflow == Overflow.None
                ? "whatever produced this density did not stay finite in that cell; "
                    + "ions are drawn only from a density that is a number everywhere"
                : "the density is a number but far larger than any packet: check the population the "
                    + "phase before started from and what stepped it, since a density this large is a "
                    + "density that ran away rather than one to draw ions from",
        })
    {
        I = i;
        J = j;
        What = overflow;
    }

    /// <summary>What overflowed, where the density itself was finite.</summary>
    public enum Overflow
    {
        /// <summary>Nothing: the density itself is not a number.</summary>
        None,

        /// <summary>The ions in one cell, the density times the cell's volume.</summary>
        CellPopulation,

        /// <summary>The ions summed over the cells up to this one.</summary>
        TotalPopulation,
    }

    /// <summary>What overflowed, or <see cref="Overflow.None"/> where the density is not a number.</summary>
    public Overflow What { get; }

    private static string Sentence(int i, int j, double x, double y, Overflow overflow) => overflow switch
    {
        Overflow.None => string.Create(
            CultureInfo.InvariantCulture,
            $"a density converted to trajectories is finite in every cell, and it is not in cell ({i}, {j}), at x = {x:G6} m, y = {y:G6} m"),
        Overflow.CellPopulation => string.Create(
            CultureInfo.InvariantCulture,
            $"the ions in a cell, its density times its volume, fit in a double, and in cell ({i}, {j}), at x = {x:G6} m, y = {y:G6} m, they overflow though the density is finite"),
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"the ions summed over the cells fit in a double, and they overflow at cell ({i}, {j}), at x = {x:G6} m, y = {y:G6} m, though every density is finite"),
    };

    /// <summary>The cell's index along x.</summary>
    public int I { get; }

    /// <summary>The cell's index along y.</summary>
    public int J { get; }
}
