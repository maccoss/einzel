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
    /// <summary>The ground both ramps are drawn against, as red, green and blue.</summary>
    /// <remarks>
    /// <b>Here rather than in the XAML, because the ground and the ramps are one decision.</b>
    /// A sequential ramp passes through every background luminance, so what makes either
    /// legible is the pairing: ramps pushed away from the ground, and the ground put as far
    /// from them as it goes. A light ground with the dark-ground ramps measures 1.09 at
    /// worst, which is worse than either arrangement — so the two must move together, and
    /// keeping them in separate files is how they come not to.
    /// </remarks>
    public static readonly (double R, double G, double B) Ground = (1.0, 1.0, 1.0);

    /// <summary>How far the bright end of the ramp is darkened toward black.</summary>
    /// <remarks>
    /// <para>
    /// <b>The viewport draws on white, and a pale line on it is not a line.</b> Viridis ends
    /// at a bright yellow — right for a filled heat map, wrong for a one-pixel trajectory,
    /// and the ions it would hide are the fast ones.
    /// </para>
    /// <para>
    /// <b>No ground fixes it, which is the part worth knowing.</b> A sequential ramp spans
    /// dark to light by construction, so it passes through <em>every</em> background
    /// luminance: measured across grounds from #101010 to #D0D0D0, the worst contrast
    /// anywhere on viridis never rises above 1.25. What works is pushing the ramp away from
    /// the ground and then putting the ground as far from it as it will go.
    /// </para>
    /// <para>
    /// <b>This is the mirror of what was here before</b>, and the reason for the flip is not
    /// taste. <c>Einzel.Render</c> draws the publication figure on white, and Amendment 25
    /// says every shell action is expressible as a CLI invocation — so a viewport that looks
    /// nothing like the figure it previews is an inconsistency rather than a preference. The
    /// ground moved from #081019 to white and the ramps had to move with it; they are a pair
    /// and a light ground with dark-ground ramps is worse than either arrangement.
    /// </para>
    /// <para>
    /// <b>0.50 is where the two requirements meet.</b> Darkening further breaks monotone
    /// lightness — the darkened ceiling falls below the mid-ramp and the scale humps in the
    /// middle — which is the property the ramp was chosen for. Measured against white, the
    /// worst contrast anywhere on the ramp is <b>4.79</b> at 0.50 and <b>1.26</b> undarkened;
    /// at 0.60 it reaches 6.74 and is no longer monotone.
    /// </para>
    /// </remarks>
    private const double BrightEndDarkened = 0.50;

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
    /// <b>Saturated at both ends and grey in the middle, because the viewport draws on
    /// white.</b> A ramp bright at both ends washes out against a light ground - the ends of
    /// the scale, which are the electrodes doing the most, become the hardest things to see -
    /// and a near-white neutral makes earth invisible, which is the one value a reader looks
    /// for first.
    /// </para>
    /// </remarks>
    public static (double R, double G, double B) Diverging(double fraction)
    {
        if (double.IsNaN(fraction))
        {
            fraction = 0.5;
        }

        var t = Math.Clamp(fraction, 0.0, 1.0);

        // Cool-warm for a light ground: a deep blue, a mid blue, a MID-GREY neutral at
        // earth, a warm terracotta, a deep red.
        //
        // The neutral is the anchor that decides this. A near-white centre is right on a
        // dark ground and disappears on a light one - and it is earth, the value a reader
        // looks for first. Measured against white, worst contrast anywhere on the ramp is
        // 3.03 here, against 1.09 for the bright dark-ground anchors and 1.26 for the
        // print-standard cool-warm, whose neutral is a light grey for paper that is being
        // printed on rather than displayed against.
        (double R, double G, double B)[] anchors =
        [
            (0.129, 0.259, 0.639),
            (0.325, 0.478, 0.796),
            (0.545, 0.545, 0.560),
            (0.831, 0.451, 0.365),
            (0.647, 0.075, 0.110),
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

        // Darkened by an amount that falls to zero at the dark end, so the ramp keeps its
        // hue order and its bottom colour exactly.
        var shade = BrightEndDarkened * f;

        double Toward(double low, double high)
        {
            var value = low + ((high - low) * t);

            return value * (1.0 - shade);
        }

        return (Toward(a.R, b.R), Toward(a.G, b.G), Toward(a.B, b.B));
    }
}
