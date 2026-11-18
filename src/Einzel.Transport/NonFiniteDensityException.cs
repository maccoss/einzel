using System.Globalization;

using Einzel.Core.Errors;

namespace Einzel.Transport;

/// <summary>
/// A density that is not a number in some cell, refused where it is converted to trajectories.
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
    public NonFiniteDensityException(int i, int j, double x, double y, double value)
        : base(new EinzelError
        {
            Code = ErrorCodes.ConvergenceFailed,
            Path = "/",
            Constraint = string.Create(
                CultureInfo.InvariantCulture,
                $"a density converted to trajectories is finite in every cell, and it is not in cell ({i}, {j}), at x = {x:G6} m, y = {y:G6} m"),
            Observed = new ObservedValue(value, "m^-3"),
            Suggestion = "whatever produced this density did not stay finite in that cell; "
                + "ions are drawn only from a density that is a number everywhere",
        })
    {
        I = i;
        J = j;
    }

    /// <summary>The cell's index along x.</summary>
    public int I { get; }

    /// <summary>The cell's index along y.</summary>
    public int J { get; }
}
