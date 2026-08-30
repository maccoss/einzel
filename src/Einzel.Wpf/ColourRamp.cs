namespace Einzel.Wpf;

/// <summary>A colour scale for a scalar, as red, green and blue in zero to one.</summary>
/// <remarks>
/// <para>
/// <b>Viridis, and the reason is not taste.</b> §16 asks for trajectory bundles coloured
/// by energy, m/z or fate, which makes the ramp part of how a quantity is read. A
/// rainbow — the default almost everywhere, and what a naive blue-to-red gives — has
/// non-monotone lightness, so it invents boundaries where the data is smooth and hides
/// them where it is not, and it collapses under the commonest colour vision deficiencies.
/// Viridis is monotone in lightness and stays ordered under deuteranopia and protanopia,
/// so a reader who cannot distinguish two hues can still tell which is larger.
/// </para>
/// <para>
/// <b>Presentation, so it lives in the shell.</b> UI-1 gives the shell layout and the
/// interactive viewport; the <em>range</em> the ramp is stretched over is not the shell's
/// and is reported by <c>ViewportCommand</c> over the whole bundle, because a scale taken
/// per path would give every ion the same colours whatever its energy.
/// </para>
/// <para>
/// Eight anchors linearly interpolated rather than the published 256-entry table: the
/// deviation from the reference map is under two per cent of the range, which is well
/// inside what a screen and an eye resolve, and it is a table anyone can read.
/// </para>
/// </remarks>
public static class ColourRamp
{
    /// <summary>Viridis at eight points, evenly spaced from zero to one.</summary>
    private static readonly (double R, double G, double B)[] Anchors =
    [
        (0.267, 0.005, 0.329),
        (0.283, 0.141, 0.458),
        (0.254, 0.265, 0.530),
        (0.207, 0.372, 0.553),
        (0.164, 0.471, 0.558),
        (0.135, 0.659, 0.518),
        (0.478, 0.821, 0.318),
        (0.993, 0.906, 0.144),
    ];

    /// <summary>The colour at a point on the scale.</summary>
    /// <param name="fraction">Where on the scale, from zero to one.</param>
    /// <returns>Red, green and blue, each from zero to one.</returns>
    /// <remarks>
    /// A fraction outside the scale is clamped rather than refused, because the scale is
    /// a display decision and a value past its end is still a value to draw. What must
    /// not happen is a non-finite fraction painting the bundle black — so
    /// <see cref="double.NaN"/> lands at the bottom rather than propagating.
    /// </remarks>
    public static (double R, double G, double B) At(double fraction)
    {
        if (double.IsNaN(fraction))
        {
            fraction = 0.0;
        }

        var scaled = Math.Clamp(fraction, 0.0, 1.0) * (Anchors.Length - 1);
        var lower = Math.Min((int)scaled, Anchors.Length - 2);
        var t = scaled - lower;

        var a = Anchors[lower];
        var b = Anchors[lower + 1];

        return (
            a.R + ((b.R - a.R) * t),
            a.G + ((b.G - a.G) * t),
            a.B + ((b.B - a.B) * t));
    }
}
