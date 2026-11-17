using System.Globalization;

using Einzel.Core.Errors;

namespace Einzel.Fields;

/// <summary>Whether an instant can be placed in a field's drive cycle.</summary>
public static class FieldInstants
{
    /// <summary>
    /// The finest fraction of the fastest drive period an instant must be placeable to: past it,
    /// the instant is so large that a double cannot say where in the cycle it falls.
    /// </summary>
    public const double ResolutionFraction = 1e-3;

    /// <summary>Whether a double can place an instant in a cycle of the given period.</summary>
    /// <param name="seconds">The instant.</param>
    /// <param name="periodSeconds">The period.</param>
    /// <returns>
    /// True where neighbouring doubles at the instant are no further apart than
    /// <see cref="ResolutionFraction"/> of the period.
    /// </returns>
    public static bool Resolvable(double seconds, double periodSeconds) =>
        Math.Abs(double.BitIncrement(seconds) - seconds) <= ResolutionFraction * periodSeconds;

    /// <summary>
    /// Refuses an instant so late that where it falls in a field's drive cycle is decided by
    /// rounding.
    /// </summary>
    /// <param name="atSeconds">The instant, in seconds, or null for none.</param>
    /// <param name="field">The field, as assembled.</param>
    /// <param name="path">Where the error is located: the flag, as a JSON Pointer.</param>
    /// <exception cref="EinzelException">The instant is refused.</exception>
    /// <remarks>
    /// Asks how far apart neighbouring doubles are at that instant: when it is more than
    /// <see cref="ResolutionFraction"/> of the fastest drive period, the phase drawn is
    /// whichever one the rounding lands on - at 1e300 microseconds on a 1 MHz drive, any phase
    /// at all. A field with no drive has no phase to lose and is never refused here. One check
    /// for every verb that draws or samples a field at an instant, so the rule cannot differ
    /// between them - except a cycle mean, which draws no phase: there the instant only picks
    /// the operating point, and the caller does not ask.
    /// </remarks>
    public static void CheckResolution(double? atSeconds, IElectrostaticField field, string path)
    {
        ArgumentNullException.ThrowIfNull(field);

        // Through a magnet's wrapper to the electric field it carries: a drive inside is still a
        // drive whatever answers for the sum. The assembly's own wrapper is driven when what it
        // wraps is (MagnetizedField.Around); one made another way need not be.
        var electric = field is Magnetic.MagnetizedField magnetized ? magnetized.Electric : field;

        if (atSeconds is not { } seconds || !double.IsFinite(seconds)
            || (field as ITimeVaryingField ?? electric as ITimeVaryingField) is not { } driven
            || !double.IsFinite(driven.ShortestPeriodSeconds))
        {
            return;
        }

        var spacing = Math.Abs(double.BitIncrement(seconds) - seconds);
        var period = driven.ShortestPeriodSeconds;

        if (!Resolvable(seconds, period))
        {
            var us = seconds * 1e6;

            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = string.Create(
                    CultureInfo.InvariantCulture,
                    $"the instant {us:G6} us is too late to place in the drive's cycle: neighbouring instants there are {spacing * 1e6:G3} us apart, more than {ResolutionFraction:G1} of the fastest drive period ({period * 1e6:G6} us), so the phase drawn would be decided by rounding"),
                Observed = new ObservedValue(us, "us"),
                Suggestion = "give an earlier instant at the same phase: a driven field repeats every period, so it draws the same field",
            });
        }
    }
}
