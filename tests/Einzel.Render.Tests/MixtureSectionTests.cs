using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Render;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// A model of several ion populations is drawn as several populations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by rendering a four-population model and getting one blob.</b> The section
/// renderer took a single density, so a mixture reached it as whichever one the command layer
/// happened to compute - and the command layer, asking the single-species transport for a
/// mixture, got a packet that was none of the declared populations: the first one's mass
/// against a mobility derived from the gas cross-section. Nothing failed and nothing on the
/// figure said so.
/// </para>
/// <para>
/// That is the recurring shape in this project - a capability wired into one path and not the
/// other, producing output that looks correct - and the drawing is the worst place for it,
/// because a figure is the artifact most likely to be looked at by someone who never saw the
/// result it came from.
/// </para>
/// </remarks>
public sealed class MixtureSectionTests(ITestOutputHelper output)
{
    /// <summary>A packet of the given width centred where asked, on a shared grid.</summary>
    private static DensityField Packet(Grid2D grid, double centreXm, double widthM, double peak)
    {
        var density = new DensityField(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = grid.X(i) - centreXm;
                var dy = grid.Y(j);

                density[i, j] = peak * Math.Exp(-((dx * dx) + (dy * dy)) / (2.0 * widthM * widthM));
            }
        }

        return density;
    }

    private static CompiledModel Model()
    {
        var validation = ModelValidator.Validate(Io.ModelJson.Parse(Library.DeviceTemplates.Read("einzel-lens")));

        Assert.True(validation.IsValid);

        return validation.Model!;
    }

    /// <summary>
    /// Every population is drawn, and they are drawn where they are rather than merged.
    /// </summary>
    [Fact]
    public void EveryPopulationIsDrawn()
    {
        var model = Model();
        var spec = new RenderSpec { WidthMm = 160.0, Equipotentials = 0, DensityContours = 4 };

        var grid = Grid2D.OverBox(-0.02, -0.006, 0.06, 0.006, 128, 32);

        double[] centres = [0.0, 0.02, 0.04];
        var packets = centres.Select(c => Packet(grid, c, 2.5e-3, 1e12)).ToArray();

        var one = SectionRenderer.Render(model, spec, null, [packets[0]]);
        var all = SectionRenderer.Render(model, spec, null, packets);

        var drawnOne = one.Scene.Paths.Count(p => p.Layer == "density");
        var drawnAll = all.Scene.Paths.Count(p => p.Layer == "density");

        output.WriteLine($"one population: {drawnOne} density paths");
        output.WriteLine($"three:          {drawnAll} density paths");

        Assert.True(drawnOne > 0, "the single-population control drew nothing, so this asserts nothing");

        // Not merely "more": three packets of one width should give about three times the
        // contours of one, and a renderer that drew the first and ignored the rest would
        // give exactly the control's count.
        Assert.True(
            drawnAll >= 3 * drawnOne,
            $"three populations drew {drawnAll} paths against {drawnOne} for one, so they are not all "
            + "being drawn");
    }

    /// <summary>
    /// And they share one contour ladder, so a small population is not drawn as densely as a
    /// large one.
    /// </summary>
    /// <remarks>
    /// The same argument that anchors an animation's levels once across its frames rather than
    /// per frame: levels taken per population would put the same number of rings on every one
    /// of them whatever its peak, and a figure of a mixture would show four equal packets
    /// where the model has one large and three faint.
    /// </remarks>
    [Fact]
    public void OneContourLadderAcrossThePopulations()
    {
        var model = Model();
        var spec = new RenderSpec { WidthMm = 160.0, Equipotentials = 0, DensityContours = 4 };

        var grid = Grid2D.OverBox(-0.02, -0.006, 0.06, 0.006, 128, 32);

        // A hundredfold apart in peak: on a shared ladder of four decades the faint one
        // reaches only the lowest levels, and on a per-population ladder it would reach all.
        var bright = Packet(grid, 0.0, 2.5e-3, 1e12);
        var faint = Packet(grid, 0.04, 2.5e-3, 1e10);

        var together = SectionRenderer.Render(model, spec, null, [bright, faint]);
        var alone = SectionRenderer.Render(model, spec, null, [faint]);

        var drawnTogether = together.Scene.Paths.Count(p => p.Layer == "density");
        var drawnAlone = alone.Scene.Paths.Count(p => p.Layer == "density");

        output.WriteLine($"the faint packet alone:         {drawnAlone} paths");
        output.WriteLine($"both, on one shared ladder:     {drawnTogether} paths");
        output.WriteLine($"twice the bright packet's own:  {2 * (drawnTogether - drawnAlone)}");

        Assert.True(drawnAlone > 0, "the faint control drew nothing on its own ladder");

        // On a shared ladder anchored to the bright packet, the faint one is a hundred times
        // down and reaches fewer levels than it does alone - so the pair draws FEWER paths
        // than two independently-anchored packets would.
        Assert.True(
            drawnTogether < 2 * drawnAlone,
            $"the pair drew {drawnTogether} paths against {drawnAlone} for the faint one alone, which is "
            + "what per-population ladders would give");
    }
}
