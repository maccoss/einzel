using System.Globalization;
using Einzel.Core.Errors;
using Einzel.Core.Units;

namespace Einzel.Core.Numerics;

/// <summary>
/// The arithmetic of a volume mesh: how many intervals a requested cell size gives an axis,
/// how many nodes that makes, and how many nodes a solve may hold.
/// </summary>
/// <remarks>
/// <para>
/// <b>Here rather than beside <c>Grid3D</c>, because three places must agree and one of them
/// cannot see that assembly.</b> The grid rounds its interval counts with this rule, the
/// solver refuses a grid past <see cref="MaximumNodes"/>, and the model validator refuses a
/// document whose mesh would be one - and the validator lives in <c>Einzel.Core</c>, below
/// the field solver. Two copies of a rounding rule are exactly the pair that stops agreeing,
/// and a validator that rounded differently from the grid would pass a model the solver then
/// refused, which is the failure this exists to prevent.
/// </para>
/// <para>
/// <b>The mesh is arithmetic on the document, so its size is known before anything is
/// built.</b> That is what makes refusing an oversized one a validation error rather than a
/// run-time surprise: the solver's own guard fired only after a conductor mask of the same
/// node count had been assembled, which on the Astral at a 1 mm cell was minutes of work and
/// several gigabytes, and was then reported as a defect in the engine.
/// </para>
/// </remarks>
public static class VolumeMesh
{
    /// <summary>The most nodes a volume solve may hold.</summary>
    /// <remarks>
    /// <para>
    /// A judgement rather than a measurement, and stated as one. It was set when the volume
    /// solver was written, against the observation that 1024 cubed is a billion nodes and
    /// eight gigabytes for a single field. A field is the <em>smallest</em> per-node array a
    /// solve holds, though: the finest level also carries a conductor mask, six cut-link arms
    /// of two doubles each and an operator stencil of eight, so the resident total is several
    /// times the fields alone. The number is kept where it was rather than re-derived here,
    /// because what this change fixes is how it is reported, not where it sits.
    /// </para>
    /// <para>
    /// <b>One constant, read by the grid, the solver's allocation guard and the model
    /// validator</b>, so <c>einzel validate</c>, <c>einzel estimate</c> and <c>einzel run</c>
    /// cannot disagree about which models are refused.
    /// </para>
    /// </remarks>
    public const long MaximumNodes = 64_000_000;

    /// <summary>The largest interval count this arithmetic reports, a power of two.</summary>
    /// <remarks>
    /// A saturation rather than a limit anybody meets: a count this large is refused whatever
    /// it is, and saturating keeps a cell size of a picometre over a metre from overflowing the
    /// doubling - which, as an <see cref="int"/>, wrapped to zero and never terminated.
    /// </remarks>
    private const long LargestIntervals = 1L << 62;

    /// <summary>How many intervals a requested cell size gives one axis.</summary>
    /// <param name="span">The axis extent, in metres.</param>
    /// <param name="cellSize">The requested node spacing, in metres.</param>
    /// <returns>A power of two, at least two, covering the span at no coarser than asked.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The span or the cell size is not positive.</exception>
    /// <remarks>
    /// <b>The grid's own rule, not a restatement of it</b>: <c>Grid3D.OverBox</c> calls this.
    /// Each axis rounds its own count <em>up</em> to a power of two from the same requested
    /// size, so no direction is coarser than asked and the node count is the product of three
    /// such roundings - which is why cost is a step function of the cell size.
    /// </remarks>
    public static long Intervals(double span, double cellSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(span);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);

        var ratio = Math.Ceiling(span / cellSize);

        // NaN and infinity fail this comparison too, which is the point of writing it this way
        // round.
        if (!(ratio <= LargestIntervals))
        {
            return LargestIntervals;
        }

        var wanted = Math.Max(2L, (long)ratio);
        var intervals = 2L;

        while (intervals < wanted)
        {
            intervals *= 2;
        }

        return intervals;
    }

    /// <summary>Nodes along each axis for a requested cell size.</summary>
    /// <param name="spanX">Extent along x, in metres.</param>
    /// <param name="spanY">Extent along y, in metres.</param>
    /// <param name="spanZ">Extent along z, in metres.</param>
    /// <param name="cellSize">Requested node spacing, in metres.</param>
    /// <returns>One more than each axis's interval count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A span or the cell size is not positive.</exception>
    public static (long X, long Y, long Z) Counts(
        double spanX, double spanY, double spanZ, double cellSize) =>
        (Intervals(spanX, cellSize) + 1, Intervals(spanY, cellSize) + 1, Intervals(spanZ, cellSize) + 1);

    /// <summary>Total nodes for a requested cell size.</summary>
    /// <param name="spanX">Extent along x, in metres.</param>
    /// <param name="spanY">Extent along y, in metres.</param>
    /// <param name="spanZ">Extent along z, in metres.</param>
    /// <param name="cellSize">Requested node spacing, in metres.</param>
    /// <returns>The node count, saturating at <see cref="long.MaxValue"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A span or the cell size is not positive.</exception>
    public static long Nodes(double spanX, double spanY, double spanZ, double cellSize)
    {
        var (x, y, z) = Counts(spanX, spanY, spanZ, cellSize);

        return Product(x, y, z);
    }

    /// <summary>
    /// The finest cell size, no finer than requested, whose mesh fits within the limit.
    /// </summary>
    /// <param name="spanX">Extent along x, in metres.</param>
    /// <param name="spanY">Extent along y, in metres.</param>
    /// <param name="spanZ">Extent along z, in metres.</param>
    /// <param name="requested">The cell size asked for, in metres.</param>
    /// <param name="limit">The node limit; <see cref="MaximumNodes"/> unless a test says otherwise.</param>
    /// <returns>
    /// A cell size in metres: the requested one if its mesh already fits, and otherwise one
    /// sitting exactly on a power-of-two boundary.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">A span or the requested size is not positive, or the limit is below 27.</exception>
    /// <remarks>
    /// <para>
    /// <b>Searched over the boundaries, not estimated.</b> The node count only changes where
    /// some axis's extent over the cell size crosses a power of two, and at exactly that
    /// size the axis takes the smaller count. So the finest fitting size is one of those
    /// boundaries, and each is evaluated with <see cref="Nodes"/> itself. A rule of thumb
    /// cannot do this: <c>einzel estimate</c> once offered twice the finest spacing, which
    /// lands exactly on a boundary and produced the identical mesh.
    /// </para>
    /// <para>
    /// <b>Exactly on the boundary, so it must not be printed as it stands.</b> A value
    /// written back into a document with fewer digits, or through a unit conversion, can
    /// land a rounding below the boundary and take the finer count on that axis again. The
    /// suggestion in <see cref="Refusal"/> rounds it <em>up</em> and checks the rounded value.
    /// </para>
    /// </remarks>
    public static double FinestFittingCell(
        double spanX, double spanY, double spanZ, double requested, long limit = MaximumNodes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 27L);

        var candidates = new List<double> { requested };

        foreach (var span in new[] { spanX, spanY, spanZ })
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(span);

            // span / 2^k exactly, by scaling rather than dividing, so that span over it is
            // exactly 2^k and the axis takes exactly that many intervals.
            for (var k = 1; k < 62; k++)
            {
                var boundary = Math.ScaleB(span, -k);

                if (boundary < requested)
                {
                    break;
                }

                candidates.Add(boundary);
            }
        }

        candidates.Sort();

        foreach (var candidate in candidates)
        {
            if (Nodes(spanX, spanY, spanZ, candidate) <= limit)
            {
                return candidate;
            }
        }

        // Unreachable for a limit of at least 27: half the longest span gives every axis two
        // intervals. Kept as a statement rather than a throw, because it is arithmetic.
        return Math.ScaleB(Math.Max(spanX, Math.Max(spanY, spanZ)), -1);
    }

    /// <summary>The refusal of a mesh too large to solve, as an AGT-3 recovery instruction.</summary>
    /// <param name="path">JSON Pointer to the cell size, or <c>/</c> where the caller has no document.</param>
    /// <param name="countX">Nodes along x the mesh would have.</param>
    /// <param name="countY">Nodes along y.</param>
    /// <param name="countZ">Nodes along z.</param>
    /// <param name="spanX">Extent along x, in metres.</param>
    /// <param name="spanY">Extent along y, in metres.</param>
    /// <param name="spanZ">Extent along z, in metres.</param>
    /// <param name="requested">The cell size asked for, in metres.</param>
    /// <param name="unit">
    /// The length unit the document wrote the cell size in, so the suggestion can be pasted
    /// back. Millimetres when absent or not a length.
    /// </param>
    /// <returns>The error.</returns>
    /// <remarks>
    /// <para>
    /// <b>A refusal about the model, not a defect in the engine.</b> It used to be an
    /// <see cref="ArgumentOutOfRangeException"/>, which the CLI can only present as
    /// <c>INTERNAL_ERROR</c> with "this is a defect in einzel, not in your model" - exactly
    /// backwards, and on the wrong exit code for a caller branching on it.
    /// </para>
    /// <para>
    /// The suggestion names a cell size <b>checked to give the finest fitting mesh</b> after
    /// rounding to the digits it is printed with and converting back through the unit -
    /// not merely a mesh that fits - because the finest fitting size sits exactly on a
    /// power-of-two boundary: a value a rounding below it gives the refused mesh again, and a
    /// value rounded too far above it can cross a second axis's boundary and halve the mesh.
    /// </para>
    /// </remarks>
    public static EinzelError Refusal(
        string path,
        long countX, long countY, long countZ,
        double spanX, double spanY, double spanZ,
        double requested,
        string? unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var observed = Product(countX, countY, countZ);
        var symbol = LengthUnitOr(unit, "mm");
        var perUnit = Quantity.From(1.0, symbol).SiValue;

        // Printed on the same step of the mesh as the finest fitting size, not merely somewhere
        // that fits: "the finest that fits" has to be true of the number on the page.
        var finest = FinestFittingCell(spanX, spanY, spanZ, requested);
        var mesh = Counts(spanX, spanY, spanZ, finest);
        var printed = PrintableAtLeast(finest / perUnit, value =>
            Counts(spanX, spanY, spanZ, Quantity.From(value, symbol).SiValue) == mesh);

        var (fx, fy, fz) = Counts(spanX, spanY, spanZ, Quantity.From(printed, symbol).SiValue);
        var fitted = Product(fx, fy, fz);

        var invariant = CultureInfo.InvariantCulture;

        return new EinzelError
        {
            Code = ErrorCodes.GridTooLarge,
            Path = path,
            Constraint = string.Create(
                invariant,
                $"a volume solve may hold at most {MaximumNodes:N0} nodes, and this mesh is "
                + $"{countX} x {countY} x {countZ} = {observed:N0}: each axis rounds its interval "
                + $"count up to a power of two from the requested cell size, so the node count is "
                + $"the product of three such roundings"),
            Observed = new ObservedValue(observed, "nodes"),
            Suggestion = string.Create(
                invariant,
                $"a cell size of {printed.ToString("R", invariant)} {symbol} is the finest that fits, "
                + $"giving {fx} x {fy} x {fz} = {fitted:N0} nodes, and anything coarser fits too; "
                + $"or shrink the domain"),
        };
    }

    /// <summary>A saturating product of three node counts.</summary>
    private static long Product(long x, long y, long z)
    {
        // Past a quintillion the exact value is irrelevant - it is refused either way - and
        // below it the long product cannot overflow.
        return (double)x * y * z > 1e18 ? long.MaxValue : x * y * z;
    }

    /// <summary>The unit a suggestion is written in: the document's own if it is a length.</summary>
    private static string LengthUnitOr(string? unit, string fallback)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return fallback;
        }

        try
        {
            return UnitRegistry.Resolve(unit).Dimension == Dimension.LengthDimension ? unit : fallback;
        }
        catch (EinzelException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// The shortest decimal, no smaller than asked, that a check accepts.
    /// </summary>
    /// <param name="value">The value to round up.</param>
    /// <param name="accepts">Whether a candidate is acceptable.</param>
    /// <returns>The candidate, exactly as it parses from its printed digits.</returns>
    /// <remarks>
    /// <para>
    /// <b>As many digits as it takes, starting from three.</b> Rounding up to a fixed three
    /// figures was the first version, and on the Astral at 1 mm it printed 1.47 mm for a
    /// finest fitting size of 1.46484 - past a <em>second</em> power-of-two boundary, on the
    /// other axis at 1.46875 mm, so the mesh it named had half the nodes and the claim that it
    /// was the finest that fits was false. Each precision is tried in turn and the first whose
    /// rounded-up value the check accepts is taken.
    /// </para>
    /// <para>
    /// <b>Rounded up, then checked rather than trusted</b>: the value sits on a boundary where
    /// one ulp decides the mesh, and both the decimal rounding and the unit conversion move it
    /// by about that much. Built from its own decimal text, so the number checked is the
    /// number a reader types back: "1465E-3" parses to the same double as 1.465 in a
    /// document.
    /// </para>
    /// </remarks>
    private static double PrintableAtLeast(double value, Func<double, bool> accepts)
    {
        var invariant = CultureInfo.InvariantCulture;
        var magnitude = (int)Math.Floor(Math.Log10(value));

        for (var figures = 3; figures <= 17; figures++)
        {
            var exponent = magnitude - (figures - 1);
            var digits = (long)Math.Ceiling(value / Math.Pow(10.0, exponent));

            // The ceiling itself can land a rounding low, so the next two are tried as well.
            for (var nudge = 0; nudge < 3; nudge++)
            {
                var candidate = double.Parse(
                    string.Create(invariant, $"{digits + nudge}E{exponent}"), NumberStyles.Float, invariant);

                if (candidate >= value && accepts(candidate))
                {
                    return candidate;
                }
            }
        }

        return value;
    }
}
