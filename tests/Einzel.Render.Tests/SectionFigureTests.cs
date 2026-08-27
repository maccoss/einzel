using System.Globalization;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Library;
using Einzel.Render;
using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// A section figure of a real device, drawn headlessly.
/// </summary>
/// <remarks>
/// RND-1 requires a publication figure to come out on Linux, in CI, with no
/// display attached. These run in exactly that environment, which is the point:
/// a renderer that needs a window is a shell feature no matter where its code
/// lives.
/// </remarks>
public sealed class SectionFigureTests(ITestOutputHelper output)
{
    private static CompiledModel Compile(string template)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read(template));
        var validation = ModelValidator.Validate(document, null);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    [Fact]
    public void TheEinzelLensDrawsAsLineWork()
    {
        var spec = new RenderSpec
        {
            WidthMm = 170.0,
            Equipotentials = 14,
            SampleColumns = 300,
            Caption = "Einzel lens: three coaxial tubes, 500 V on the centre one.",
        };

        var figure = SectionRenderer.Render(Compile("einzel-lens"), spec);

        output.WriteLine($"page {figure.Scene.WidthMm:F1} x {figure.Scene.HeightMm:F1} mm");
        output.WriteLine($"{figure.Scene.Paths.Count} paths, {figure.Scene.Texts.Count} text runs");
        output.WriteLine(
            $"trajectory {figure.TrajectoryPointsBeforeDecimation} points decimated to "
            + $"{figure.TrajectoryPoints} at {figure.DecimationToleranceMm:G3} mm");

        foreach (var layer in figure.Scene.Paths.Select(p => p.Layer).Distinct().Order())
        {
            output.WriteLine($"  layer {layer}: {figure.Scene.Paths.Count(p => p.Layer == layer)} paths");
        }

        // Conductors, equipotentials and a trajectory: the three things a section
        // figure of an ion-optical element is for.
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "conductors");
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "equipotentials");
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "trajectory");

        // Three tubes, each appearing above and below the axis because a
        // cylindrical solve is a half-plane and a ring is two conductors on the
        // page. Drawn from the signed distance rather than from any knowledge of
        // what a tube is.
        var conductors = figure.Scene.Paths.Count(p => p.Layer == "conductors");

        output.WriteLine($"conductor runs {conductors}, of which closed "
            + $"{figure.Scene.Paths.Count(p => p.Layer == "conductors" && p.Closed)}");

        Assert.Equal(6, conductors);
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "axis");

        Assert.Empty(figure.Warnings);
    }

    [Fact]
    public void EverySvgCoordinateLandsOnThePage()
    {
        // The cheapest exact check that the world-to-page map is right. A sign error
        // or a mismatched origin puts geometry off the page, where it is invisible
        // in a viewer that clips and mysterious in one that does not.
        var spec = new RenderSpec { WidthMm = 150.0, Equipotentials = 8, SampleColumns = 200 };
        var figure = SectionRenderer.Render(Compile("quadrupole"), spec);

        foreach (var path in figure.Scene.Paths)
        {
            foreach (var point in path.Points)
            {
                Assert.InRange(point.X, -0.001, figure.Scene.WidthMm + 0.001);
                Assert.InRange(point.Y, -0.001, figure.Scene.HeightMm + 0.001);
            }
        }

        output.WriteLine(
            $"{figure.Scene.Paths.Sum(p => p.Points.Count)} vertices, all inside "
            + $"{figure.Scene.WidthMm:F1} x {figure.Scene.HeightMm:F1} mm");
    }

    [Fact]
    public void LabelsStayTextInBothFormats()
    {
        // RND-6. A figure has to be relabellable for a different venue without
        // regenerating it, which means the characters are characters in the output
        // and not outlines that happen to look like letters.
        var spec = new RenderSpec { WidthMm = 120.0, Caption = "Quadrupole section" };
        var figure = SectionRenderer.Render(Compile("quadrupole"), spec);

        var svg = SvgWriter.Write(figure.Scene);
        var pdf = System.Text.Encoding.Latin1.GetString(PdfWriter.Write(figure.Scene));

        Assert.Contains("<text ", svg, StringComparison.Ordinal);
        Assert.Contains("Quadrupole section</text>", svg, StringComparison.Ordinal);

        Assert.Contains("(Quadrupole section) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Helvetica", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePdfIsStructurallyValid()
    {
        // Written by hand, so the cross-reference table is the part most likely to be
        // wrong and the part a reader refuses on. Every offset must name the object
        // it claims to.
        var spec = new RenderSpec { WidthMm = 120.0, Equipotentials = 6, SampleColumns = 150 };
        var figure = SectionRenderer.Render(Compile("quadrupole"), spec);

        var bytes = PdfWriter.Write(figure.Scene);
        var text = System.Text.Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);

        var xref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var declared = int.Parse(
            text[(xref + 9)..text.IndexOf("%%EOF", xref, StringComparison.Ordinal)].Trim(),
            CultureInfo.InvariantCulture);

        Assert.Equal("xref", text.Substring(declared, 4));

        // Each offset in the table has to land on that object's header.
        var table = text[(declared + 4)..];
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var found = 0;

        for (var i = 2; i < lines.Length && lines[i].EndsWith(" n ", StringComparison.Ordinal); i++)
        {
            var offset = int.Parse(lines[i][..10], CultureInfo.InvariantCulture);

            Assert.Equal($"{i - 1} 0 obj", text.Substring(offset, $"{i - 1} 0 obj".Length));
            found++;
        }

        output.WriteLine($"{bytes.Length:N0} bytes, {found} objects, xref at {declared}");

        Assert.Equal(6, found);
    }

    [Fact]
    public void AFigureFromATaintedFieldSaysSoOnTheFace()
    {
        // RND-11 and GRD-5: a preview-tier or otherwise qualified result must be
        // visually distinguishable in rendered output, not merely noted in metadata
        // nobody opens. A figure is the artifact most likely to be shown to an
        // audience with none of the uncertainty apparatus attached.
        var document = ModelJson.Parse(DeviceTemplates.Read("quadrupole"));

        // A tolerance below round-off: the solve stalls and reports not-converged.
        var strained = document with
        {
            Fields =
            [
                document.Fields![0] with { Solve = document.Fields[0].Solve! with { Tolerance = 1e-30 } },
            ],
        };

        var validation = ModelValidator.Validate(strained, null);
        Assert.True(validation.IsValid);

        var figure = SectionRenderer.Render(
            validation.Model!, new RenderSpec { WidthMm = 120.0, Equipotentials = 4, SampleColumns = 120 });

        var warning = Assert.Single(figure.Warnings);
        Assert.Equal("field.not-converged", warning.Code);

        // On the page, not only in the comment block.
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "taint");
        Assert.Contains(figure.Scene.Texts, t => t.Text.StartsWith("QUALIFIED", StringComparison.Ordinal));

        var svg = SvgWriter.Write(figure.Scene);
        Assert.Contains("field.not-converged", svg, StringComparison.Ordinal);

        output.WriteLine(figure.Scene.Provenance[^1]);
    }
}
