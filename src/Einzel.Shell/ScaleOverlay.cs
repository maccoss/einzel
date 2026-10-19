using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>
/// The scale indicator, drawn over the viewport as ordinary lines and text.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing.</b> The round length, which arms show and where everything sits come
/// from <see cref="ScaleIndicator.For"/>, laid out from the matrix the viewport is drawing with
/// and the picture it is drawing - the same call <c>einzel render still</c> makes, so a still and
/// the window put the same ruler in the same corner.
/// </para>
/// <para>
/// <b>Over the GL control rather than inside it</b>, because Avalonia draws text and the shader
/// does not, and an annotation is not part of the scene: nothing in the instrument may hide it.
/// It takes no pointer input, so it never stands between a reader and the picture.
/// </para>
/// <para>
/// <b>Text is sized by the height of a digit, as the still sizes it.</b> The layout's text
/// height is a digit's height, which is what <c>StrokeFont</c> draws at, while a font is sized
/// by its em, which is taller - about a half again in the default typeface. Handing the one
/// number to the other as it stood made every label in the window about two thirds the size of
/// the same label in a still of the same picture, which a look at the live window caught.
/// </para>
/// </remarks>
public sealed class ScaleOverlay : Control
{
    private readonly SceneView _view;

    /// <summary>Draws a scale indicator over a viewport.</summary>
    /// <param name="view">The viewport whose camera the ruler measures.</param>
    public ScaleOverlay(SceneView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _view = view;
        IsHitTestVisible = false;

        // The framing can change on the render thread - a watched packet grows the frame - and
        // a control may only be invalidated on the UI thread.
        _view.FramingChanged += (_, _) => Dispatcher.UIThread.Post(InvalidateVisual);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // The ruler is sized from the picture's smaller side, so a resize changes its length.
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;

        if (!(width > 0.0) || !(height > 0.0) || _view.CurrentMatrix(width / height) is not { } matrix)
        {
            return;
        }

        if (ScaleIndicator.For(matrix, width, height, ScaleIndicator.TextHeightFor(width, height), picture: _view.Picture) is not { } ruler)
        {
            return;
        }

        var (r, g, b) = ScaleIndicator.Ink;
        var brush = new SolidColorBrush(Color.FromRgb(Byte(r), Byte(g), Byte(b)));
        var pen = new Pen(brush, ScaleIndicator.LineWidthPx);
        var origin = new Point(ruler.OriginPx.X, ruler.OriginPx.Y);

        foreach (var arm in ruler.Arms)
        {
            context.DrawLine(pen, origin, new Point(arm.TipPx.X, arm.TipPx.Y));
            Text(context, arm.Axis, arm.LetterPx, ruler.LetterTextPx, brush);
        }

        Text(context, ruler.Label, ruler.LabelPx, ruler.TextPx, brush);
    }

    /// <summary>
    /// A line of text whose digits are <paramref name="digitPx"/> tall, centered on a point as the
    /// layout placed it.
    /// </summary>
    /// <remarks>
    /// Centered on the digits rather than on the line box, as the still centers it: a line box
    /// carries the font's ascent and descent, which sit unevenly about the digits, so centering
    /// the box would drop the label below where the layout put it.
    /// </remarks>
    private static void Text(DrawingContext context, string text, (double X, double Y) center, double digitPx, IBrush brush)
    {
        var (perEm, topPerEm) = DigitMetrics.Value;
        var em = digitPx / perEm;
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            em,
            brush);

        var top = center.Y - (0.5 * digitPx) - (topPerEm * em);
        context.DrawText(formatted, new Point(center.X - (formatted.Width / 2.0), top));
    }

    /// <summary>
    /// How tall the default typeface's digits are, and how far below the top of the line box they
    /// start, both per unit of em.
    /// </summary>
    /// <remarks>
    /// Measured from the glyphs' own outline rather than read from the font's metrics, which
    /// carry an ascent and a descent but no digit height. Measured once: the typeface does not
    /// change while the window is open.
    /// </remarks>
    private static readonly Lazy<(double PerEm, double TopPerEm)> DigitMetrics = new(() =>
    {
        const double em = 100.0;
        var digits = new FormattedText(
            "0123456789",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            em,
            Brushes.Black);

        var bounds = digits.BuildGeometry(default)?.Bounds;

        // A typeface that cannot outline its digits is drawn with the em taken as the digit height.
        return bounds is { Height: > 0.0 } b ? (b.Height / em, b.Top / em) : (1.0, 0.0);
    });

    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
