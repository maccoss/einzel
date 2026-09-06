using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The direct sum's softening comes from the packet's shape, not its radius: a line of
/// macroparticles is softened at the spacing of points filling its actual volume, which
/// for a long thin packet is far below what its RMS radius says.
/// </summary>
/// <remarks>
/// Forty macroparticles along 10 mm of a linear trap's axis and 50 um across it had an
/// RMS radius of 5.8 mm and a radius-rule softening of 1.7 mm - thirty-four times the
/// transverse size - so the force across the packet was switched off and a scan with
/// 4,000 ions came back identical to one with none.
/// </remarks>
public sealed class SofteningShapeTests(ITestOutputHelper output)
{
    /// <summary>An isotropic Gaussian packet gives the radius rule's number exactly, so nothing published from it moves.</summary>
    [Fact]
    public void AnIsotropicPacketReproducesTheRadiusRule()
    {
        const double sigma = 0.5e-3;
        var radius = Math.Sqrt(5.0) * sigma; // sqrt(5/3) times the RMS distance sqrt(3) sigma
        var byRadius = CoulombInteraction.SpacingSoftening(radius, 400);
        var byShape = CoulombInteraction.SpacingSoftening(sigma, sigma, sigma, 400);

        output.WriteLine($"radius rule {byRadius * 1e6:F6} um, shape rule {byShape * 1e6:F6} um");
        Assert.Equal(byRadius, byShape, 15);
        Assert.True(Math.Abs(byRadius - byShape) <= 4 * double.Epsilon + 1e-15 * byRadius, $"{byRadius} against {byShape}");
    }

    /// <summary>
    /// The linear trap's line cloud: 10 mm along the axis, 50 um across, 40 macroparticles.
    /// The shape rule is an order of magnitude below the radius rule and is the closed form;
    /// it is still above the transverse size, because forty points along ten millimetres
    /// are a quarter of a millimetre apart, and the count the warning names brings it under.
    /// </summary>
    [Fact]
    public void ALineCloudIsSoftenedAtItsOwnSpacingNotItsLength()
    {
        const double transverse = 0.05e-3;
        const double longitudinal = 10.0e-3;
        const int macroparticles = 40;

        var rms = Math.Sqrt((2.0 * transverse * transverse) + (longitudinal * longitudinal));
        var byRadius = CoulombInteraction.SpacingSoftening(Math.Sqrt(5.0 / 3.0) * rms, macroparticles);
        var byShape = CoulombInteraction.SpacingSoftening(transverse, transverse, longitudinal, macroparticles);
        var closedForm = Math.Sqrt(5.0) * Math.Cbrt(transverse * transverse * longitudinal) / Math.Cbrt(macroparticles);

        var ratio = byShape / transverse;
        var needed = (int)Math.Ceiling(macroparticles * ratio * ratio * ratio);
        var withNeeded = CoulombInteraction.SpacingSoftening(transverse, transverse, longitudinal, needed);

        output.WriteLine($"radius rule {byRadius * 1e3:F3} mm ({byRadius / transverse:F1}x the transverse size); shape rule {byShape * 1e3:F4} mm ({ratio:F2}x); {needed} macroparticles bring it to {withNeeded / transverse:F3}x");

        Assert.Equal(closedForm, byShape, 15);
        Assert.True(byRadius > 10.0 * byShape, "the shape rule must be an order of magnitude below the radius rule on a line");
        Assert.True(ratio > 1.0 && ratio < 5.0, $"{ratio}");
        Assert.True(withNeeded <= transverse * (1.0 + 1e-9), $"{withNeeded} against {transverse}");
        Assert.InRange(needed, 1000, 3000);
    }

    /// <summary>A packet declared with no spread along an axis is still softened at something finite.</summary>
    [Fact]
    public void AVanishingExtentIsFlooredRatherThanZero()
    {
        var flat = CoulombInteraction.SpacingSoftening(0.0, 1.0e-3, 1.0e-3, 100);
        var none = CoulombInteraction.SpacingSoftening(0.0, 0.0, 0.0, 100);

        output.WriteLine($"one flat axis {flat * 1e6:F4} um; no extent {none:E3} m");
        Assert.True(flat > 0.0 && flat < 1.0e-3);
        Assert.Equal(double.Epsilon, none);
    }

    /// <summary>A negative standard deviation is a sign error, refused rather than floored into a small positive spread.</summary>
    [Fact]
    public void ANegativeExtentIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CoulombInteraction.SpacingSoftening(-1.0e-3, 1.0e-3, 1.0e-3, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoulombInteraction.SpacingSoftening(1.0e-3, -1.0e-3, 1.0e-3, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoulombInteraction.SpacingSoftening(1.0e-3, 1.0e-3, -1.0e-3, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoulombInteraction.SpacingSoftening(1.0e-3, 1.0e-3, 1.0e-3, 0));
    }
}
