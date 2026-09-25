using System.Globalization;

namespace Einzel.Commands;

/// <summary>One arm of a scale indicator.</summary>
/// <param name="Axis">The model axis the arm lies along: <c>x</c>, <c>y</c> or <c>z</c>.</param>
/// <param name="TipPx">Where the arm ends, in pixels from the picture's top left.</param>
/// <param name="LetterPx">Where the axis letter is centered, just beyond the tip.</param>
public sealed record ScaleArm(string Axis, (double X, double Y) TipPx, (double X, double Y) LetterPx);

/// <summary>
/// A ruler in the corner of a picture: one arm along each model axis, all the same round
/// length, drawn with the picture's own projection.
/// </summary>
/// <param name="OriginPx">Where the arms meet, in pixels from the picture's top left.</param>
/// <param name="Arms">The arms that are drawn; an arm pointing at the viewer is left out.</param>
/// <param name="LengthMm">The true length of every arm, in millimeters.</param>
/// <param name="Label">That length as a reader sees it, with its unit.</param>
/// <param name="LabelPx">Where the label is centered.</param>
/// <param name="TextPx">The text height the layout was made for, in pixels.</param>
/// <remarks>
/// <para>
/// <b>A ruler in the scene rather than a bar on the screen, and the angled view is why.</b> In
/// the side, top and front views the camera looks straight down a model axis, so a flat bar is
/// exact for every length on the page. In the iso view each model axis is foreshortened by a
/// different amount - at the shipped angles x by 0.87, y by 0.91 and z by 0.63 - so a flat bar
/// would state one scale for three. Arms along the model's own axes, projected by the same
/// matrix as the electrodes, shorten exactly as the instrument does, and in a named view the
/// same construction is an ordinary L-shaped scale bar.
/// </para>
/// <para>
/// <b>In a corner rather than on the instrument, which the projection allows.</b> The camera is
/// orthographic, so a segment along a model axis has the same length on screen wherever it is
/// placed; the ruler can sit in clear space without being any less true. Drawn on the
/// instrument it would overlap an electrode in most pictures and would widen the frame.
/// </para>
/// <para>
/// <b>One length for every arm.</b> Per-axis lengths would carry more information and read
/// worse: arms of 200, 10 and 200 mm side by side invite the reader to compare arms that are
/// not comparable. With one length, the arms' different screen lengths <em>are</em> the
/// foreshortening, which is what an angled view hides.
/// </para>
/// <para>
/// <b>Decided here, for the window and for <c>einzel render still</c> alike</b>, like every
/// other decision about the picture (<see cref="ViewportPicture"/>): the round length, which
/// arms show and where they sit. Each consumer only turns lines and text into its own pixels.
/// </para>
/// </remarks>
public sealed record ScaleIndicator(
    (double X, double Y) OriginPx,
    IReadOnlyList<ScaleArm> Arms,
    double LengthMm,
    string Label,
    (double X, double Y) LabelPx,
    double TextPx)
{
    /// <summary>The color the ruler and its labels are drawn in: a near-black that reads on white.</summary>
    public static readonly (double R, double G, double B) Ink = (0.20, 0.20, 0.22);

    /// <summary>The width of an arm, in pixels: the width the picture's own lines are drawn at.</summary>
    public const double LineWidthPx = 1.5;

    /// <summary>How long the longest arm may be, as a fraction of the picture's smaller side.</summary>
    public const double TargetFraction = 0.16;

    /// <summary>
    /// How short an arm may be, against the longest, before it is taken to point at the viewer
    /// and left out.
    /// </summary>
    /// <remarks>
    /// A quarter. Straight down an axis the arm is exactly zero and drawing a dot labeled with
    /// a length would be a claim about nothing; at the shipped iso angles the shortest arm is
    /// 0.69 of the longest, so every arm shows there.
    /// </remarks>
    public const double HiddenBelow = 0.25;

    /// <summary>The label height a picture of this size gets.</summary>
    /// <param name="widthPx">The picture's width, in pixels.</param>
    /// <param name="heightPx">The picture's height, in pixels.</param>
    /// <returns>2.6 percent of the smaller side, and never under 12 pixels.</returns>
    /// <remarks>
    /// One rule for the window and the still, so a still of a window-sized picture carries the
    /// same label the window does.
    /// </remarks>
    public static double TextHeightFor(double widthPx, double heightPx) =>
        Math.Max(12.0, Math.Round(0.026 * Math.Min(widthPx, heightPx)));

    /// <summary>Lays out a scale indicator for a picture.</summary>
    /// <param name="matrix">Column-major 4x4 from millimeters to clip space, as the picture is drawn with.</param>
    /// <param name="widthPx">The picture's width, in pixels.</param>
    /// <param name="heightPx">The picture's height, in pixels.</param>
    /// <param name="textPx">The height of the label text, in pixels.</param>
    /// <param name="reserveBottomPx">Space at the bottom edge the ruler must stay clear of.</param>
    /// <returns>The indicator, or null where the projection has no finite scale.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="matrix"/> is null.</exception>
    /// <exception cref="ArgumentException">The matrix is not 4x4.</exception>
    /// <remarks>
    /// The matrix alone decides the scale, because it is the thing the picture was drawn with:
    /// a ruler computed from anything else would be a second account of the camera, and the
    /// two would disagree the first time the framing changed.
    /// </remarks>
    public static ScaleIndicator? For(
        IReadOnlyList<double> matrix,
        double widthPx,
        double heightPx,
        double textPx = 12.0,
        double reserveBottomPx = 0.0)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (matrix.Count != 16)
        {
            throw new ArgumentException("a projection is a 4x4 matrix", nameof(matrix));
        }

        if (!(widthPx > 0.0) || !(heightPx > 0.0))
        {
            return null;
        }

        // Screen pixels per millimeter along each model axis: clip x spans the width over
        // two units, and clip y runs up where a picture's rows run down.
        (string Axis, double X, double Y)[] perMm =
        [
            ("x", matrix[0] * widthPx / 2.0, -matrix[1] * heightPx / 2.0),
            ("y", matrix[4] * widthPx / 2.0, -matrix[5] * heightPx / 2.0),
            ("z", matrix[8] * widthPx / 2.0, -matrix[9] * heightPx / 2.0),
        ];

        var longest = perMm.Max(a => Math.Sqrt((a.X * a.X) + (a.Y * a.Y)));

        if (!(longest > 0.0) || !double.IsFinite(longest))
        {
            return null;
        }

        var lengthMm = RoundDown(TargetFraction * Math.Min(widthPx, heightPx) / longest);

        var shown = perMm
            .Where(a => Math.Sqrt((a.X * a.X) + (a.Y * a.Y)) >= HiddenBelow * longest)
            .Select(a => (a.Axis, X: a.X * lengthMm, Y: a.Y * lengthMm))
            .ToList();

        // Where each axis letter sits, just beyond its tip along the arm, relative to where
        // the arms meet.
        var letterPx = 0.8 * textPx;
        var letters = shown
            .Select(a =>
            {
                var length = Math.Sqrt((a.X * a.X) + (a.Y * a.Y));
                var reach = length > 0.0 ? (length + letterPx) / length : 1.0;

                return (X: a.X * reach, Y: a.Y * reach);
            })
            .ToList();

        // The ruler's own box relative to where the arms meet - letters included - so the whole
        // of it can be put in the corner whichever way the arms point: in the front view +z runs
        // to the left, and a ruler anchored at its origin would draw that arm off the picture.
        // The letters count because in the angled view the x arm dips below the origin, and a
        // label placed under the arms alone sat beside that arm's letter and read as "10 mm x".
        var half = 0.5 * letterPx;
        var left = Math.Min(Math.Min(0.0, shown.Min(a => a.X)), letters.Min(l => l.X) - half);
        var right = Math.Max(Math.Max(0.0, shown.Max(a => a.X)), letters.Max(l => l.X) + half);
        var bottom = Math.Max(Math.Max(0.0, shown.Max(a => a.Y)), letters.Max(l => l.Y) + half);

        var margin = Math.Max(12.0, 0.04 * Math.Min(widthPx, heightPx));

        // Room under the box for the label.
        var originX = margin - left;
        var originY = heightPx - reserveBottomPx - margin - (1.6 * textPx) - bottom;

        var arms = shown
            .Zip(letters, (a, l) => new ScaleArm(
                a.Axis,
                (originX + a.X, originY + a.Y),
                (originX + l.X, originY + l.Y)))
            .ToList();

        return new ScaleIndicator(
            (originX, originY),
            arms,
            lengthMm,
            Format(lengthMm),
            (originX + ((left + right) / 2.0), originY + bottom + (0.9 * textPx)),
            textPx);
    }

    /// <summary>The largest 1, 2 or 5 times a power of ten not above a length.</summary>
    /// <param name="mm">The length, in millimeters; positive.</param>
    /// <returns>A round length, in millimeters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive.</exception>
    /// <remarks>
    /// Down rather than to the nearest, so the ruler never outgrows the room it was sized for.
    /// A ruler of 23.7 mm would be arithmetically correct and useless - the same rule the
    /// vector section's scale bar has always followed.
    /// </remarks>
    public static double RoundDown(double mm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mm);

        var decade = Math.Pow(10.0, Math.Floor(Math.Log10(mm)));
        var leading = mm / decade;

        // The logarithm of a power of ten can land a hair either side of the integer, which
        // puts the leading digit at 9.9999999 or 10.0000001 rather than at 1.
        if (leading >= 10.0 - 1e-9)
        {
            decade *= 10.0;
            leading /= 10.0;
        }
        else if (leading < 1.0 - 1e-9)
        {
            decade /= 10.0;
            leading *= 10.0;
        }

        // A rounding error must not drop a length that IS round a whole step: 200 mm arrives
        // as 199.99999999999997 often enough to matter.
        var step = leading >= 5.0 - 1e-9 ? 5.0 : leading >= 2.0 - 1e-9 ? 2.0 : 1.0;

        return step * decade;
    }

    /// <summary>A length as a reader wants it: in the unit that keeps the number small.</summary>
    /// <param name="mm">The length, in millimeters.</param>
    /// <returns>For example <c>200 mm</c>, <c>500 µm</c>, <c>2 m</c>.</returns>
    public static string Format(double mm)
    {
        var (value, unit) = mm switch
        {
            >= 1000.0 => (mm / 1000.0, "m"),
            >= 1.0 => (mm, "mm"),
            >= 1e-3 => (mm * 1e3, "µm"),
            _ => (mm * 1e6, "nm"),
        };

        // Fixed rather than general: "G3" writes a thousand as 1E+03, and a ruler label is read
        // by eye and drawn in a font that has no E.
        return value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
    }
}
