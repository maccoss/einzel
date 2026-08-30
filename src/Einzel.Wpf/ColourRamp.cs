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
    /// <summary>How far the dark end of the ramp is lifted toward white.</summary>
    /// <remarks>
    /// <para>
    /// <b>The viewport draws on a dark ground, and a near-black line on it is not a line.</b>
    /// Viridis begins at a very dark purple - right for a filled heat map, wrong for a
    /// one-pixel trajectory, and the ions it would hide are the slow ones at a turning
    /// point, which are the interesting ones.
    /// </para>
    /// <para>
    /// <b>No ground fixes it, which is the part worth knowing.</b> A sequential ramp spans
    /// dark to light by construction, so it passes through <em>every</em> background
    /// luminance: measured across grounds from #101010 to #D0D0D0, the worst contrast
    /// anywhere on viridis never rises above 1.25. Nor does truncating help much - skipping
    /// the darkest 60% still only reaches 2.83, because the ramp's whole lower half is
    /// dark. What works is lifting it: blending toward white by an amount that falls to
    /// zero at the bright end keeps the hue progression and the ordering, and moves the
    /// floor off the ground.
    /// </para>
    /// <para>
    /// <b>0.44 is where the two requirements meet.</b> Lifting further breaks monotone
    /// lightness - the lifted floor rises above the mid-ramp and the scale dips in the
    /// middle - which is the property the ramp was chosen for in the first place. At 0.44,
    /// against the viewport's ground, the worst contrast anywhere on the ramp is
    /// <b>4.70 against 1.01</b> unlifted.
    /// </para>
    /// </remarks>
    private const double DarkEndLifted = 0.44;

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

    /// <summary>A diverging scale, blue through grey to red.</summary>
    /// <param name="fraction">Where on the scale, from zero to one.</param>
    /// <returns>Red, green and blue, each from zero to one.</returns>
    /// <remarks>
    /// <para>
    /// <b>A potential is signed and a sequential ramp cannot say so.</b> Viridis is right
    /// for an energy, which has a floor at zero; a quadrupole's rods sit at plus and minus
    /// the same voltage about an earth that is the interesting value, and a scale with no
    /// middle puts that middle at an arbitrary colour. This one is pale at the centre and
    /// saturated at both ends, so earth reads as earth.
    /// </para>
    /// <para>
    /// Cool for negative and warm for positive, which is the convention in every field plot
    /// this platform's users have seen, and it survives the commonest colour vision
    /// deficiencies because the hues differ in more than one channel.
    /// </para>
    /// <para>
    /// <b>Bright at both ends, because the viewport draws on a dark ground.</b> The
    /// print-standard cool-warm ramp runs from a dark navy to a dark crimson, and both of
    /// those sink into a dark background - the ends of the scale, which are the electrodes
    /// doing the most, become the hardest things to see. Every anchor here is above about
    /// half lightness. The same ramp on paper would be too pale; that figure is drawn by
    /// <c>Einzel.Render</c> and does not use this.
    /// </para>
    /// </remarks>
    public static (double R, double G, double B) Diverging(double fraction)
    {
        if (double.IsNaN(fraction))
        {
            fraction = 0.5;
        }

        var t = Math.Clamp(fraction, 0.0, 1.0);

        // Cool-warm for a dark ground: a saturated cyan-blue, a light blue, a near-white
        // neutral at earth, a warm apricot, a saturated coral.
        (double R, double G, double B)[] anchors =
        [
            (0.259, 0.616, 0.980),
            (0.529, 0.788, 1.000),
            (0.925, 0.925, 0.925),
            (1.000, 0.780, 0.510),
            (1.000, 0.478, 0.361),
        ];

        var scaled = t * (anchors.Length - 1);
        var lower = Math.Min((int)scaled, anchors.Length - 2);
        var step = scaled - lower;

        var a = anchors[lower];
        var b = anchors[lower + 1];

        return (
            a.R + ((b.R - a.R) * step),
            a.G + ((b.G - a.G) * step),
            a.B + ((b.B - a.B) * step));
    }

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

        var f = Math.Clamp(fraction, 0.0, 1.0);

        var scaled = f * (Anchors.Length - 1);
        var lower = Math.Min((int)scaled, Anchors.Length - 2);
        var t = scaled - lower;

        var a = Anchors[lower];
        var b = Anchors[lower + 1];

        // Lifted toward white by an amount that falls to zero at the bright end, so the
        // ramp keeps its hue order and its top colour exactly.
        var lift = DarkEndLifted * (1.0 - f);

        double Toward(double low, double high)
        {
            var value = low + ((high - low) * t);

            return value + ((1.0 - value) * lift);
        }

        return (Toward(a.R, b.R), Toward(a.G, b.G), Toward(a.B, b.B));
    }
}
