using System.Globalization;
using Avalonia.Media;
using Einzel.Commands;

namespace Einzel.Shell;

/// <summary>One row of the viewport's legend, ready to bind.</summary>
/// <remarks>
/// <para>
/// <b>A binding adapter and nothing more.</b> What the legend says — which scales exist,
/// what each one's ends are, and which ramp it is on — is
/// <see cref="ViewportPicture.Scales"/>'s decision, taken in the command layer where it can
/// be checked without a window and where <c>einzel render still</c> can reach the same
/// answer. This turns one of those into a label, two numbers and a brush.
/// </para>
/// <para>
/// <b>The swatch is sampled rather than approximated by two stops.</b> Viridis is not a
/// linear interpolation between its ends — that is the whole reason it is used, since its
/// lightness rises monotonically where a two-stop gradient between dark blue and yellow
/// does not — so a legend drawn from the ends alone would show a ramp the picture is not
/// painted with.
/// </para>
/// </remarks>
public sealed class LegendRow
{
    /// <summary>How many stops the swatch gradient is sampled at.</summary>
    /// <remarks>
    /// Enough that the sampling is invisible at the eleven pixels the bar is tall, and few
    /// enough that building one costs nothing. The ramps are smooth, so this is about the
    /// eye rather than about accuracy.
    /// </remarks>
    private const int Stops = 32;

    /// <summary>Builds a row from a scale the picture is drawn on.</summary>
    /// <param name="scale">One of <see cref="ViewportPicture.Scales"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scale"/> is null.</exception>
    public LegendRow(ColorScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        Quantity = scale.Quantity;
        Low = Text(scale.Low);
        High = Text(scale.High) + " " + scale.Unit;
        Swatch = Gradient(scale.Diverging);
    }

    /// <summary>What the colors stand for: energy, field, electrodes or potential.</summary>
    public string Quantity { get; }

    /// <summary>The value at the left end of the swatch, with no unit.</summary>
    public string Low { get; }

    /// <summary>The value at the right end, carrying the unit for the row.</summary>
    public string High { get; }

    /// <summary>The ramp itself, left to right.</summary>
    public IBrush Swatch { get; }

    /// <summary>A number a person reads off a legend.</summary>
    /// <remarks>
    /// <para>
    /// <b>Never in exponent form, which is what the obvious spelling gives.</b> A general
    /// format at three significant figures writes a one-kilovolt beam's energy as
    /// <c>1E+03</c> - correct, and nothing a person reads a scale by. The precision is
    /// chosen from the magnitude instead: whole volts and electronvolts where there are
    /// tens of them, two decimals below that, and the general format only where a value is
    /// small enough that a fixed one would print it as zero.
    /// </para>
    /// <para>
    /// The invariant culture is explicit even though <see cref="Program.UseInvariantNumbers"/>
    /// has already made it the current one: this is a number beside a model whose file writes
    /// a decimal point, and the two must agree whatever a later change does to the process.
    /// </para>
    /// </remarks>
    private static string Text(double value)
    {
        var size = Math.Abs(value);

        var format = size == 0.0 ? "0"
            : size >= 10.0 ? "F0"
            : size >= 0.1 ? "F2"
            : "G3";

        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static LinearGradientBrush Gradient(bool diverging)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0.5, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 0.5, Avalonia.RelativeUnit.Relative),
        };

        for (var s = 0; s < Stops; s++)
        {
            var at = s / (double)(Stops - 1);

            var (r, g, b) = diverging ? ColorRamp.Diverging(at) : ColorRamp.At(at);

            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb(Byte(r), Byte(g), Byte(b)), at));
        }

        return brush;
    }

    private static byte Byte(double channel) =>
        (byte)Math.Clamp(Math.Round(channel * 255.0), 0.0, 255.0);
}
