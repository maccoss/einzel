using Einzel.Core.Errors;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Io.Tests;

/// <summary>
/// Reading VTK ImageData, which is how a neutral velocity field gets in (GAS-1).
/// </summary>
/// <remarks>
/// The format this engine already writes, so the reader is checked against the
/// writer as well as against hand-written documents. Reading a <em>format</em>
/// carries no licence obligation; linking a library would (RND-13), which is why
/// there is a reader here at all.
/// </remarks>
public sealed class VtkImageDataTests(ITestOutputHelper output)
{
    private static string Document(
        string arrayAttributes, string payload, string extent = "0 1 0 1 0 0") =>
        $"""
        <?xml version="1.0"?>
        <VTKFile type="ImageData" version="1.0" byte_order="LittleEndian">
          <ImageData WholeExtent="{extent}" Origin="1 2 3" Spacing="0.5 0.25 1">
            <Piece Extent="{extent}">
              <PointData>
                <DataArray {arrayAttributes}>
        {payload}
                </DataArray>
              </PointData>
              <CellData/>
            </Piece>
          </ImageData>
        </VTKFile>
        """;

    [Fact]
    public void ItReadsAVectorArrayInTheOrderVtkWritesOne()
    {
        // x fastest, then y, then z. Getting this backwards transposes the field,
        // which on a symmetric grid produces a plausible-looking flow pointing the
        // wrong way - so the samples here are deliberately asymmetric.
        var text = Document(
            "type=\"Float64\" Name=\"velocity\" NumberOfComponents=\"3\" format=\"ascii\"",
            "10 0 0  20 0 0\n30 0 0  40 0 0");

        var array = VtkImageData.Read(text, "velocity");

        output.WriteLine($"{array.Name}: {array.CountX}x{array.CountY}x{array.CountZ}, "
            + $"{array.Components} components");
        output.WriteLine($"origin {array.OriginSi}, spacing {array.SpacingSi}");

        Assert.Equal("velocity", array.Name);
        Assert.Equal(3, array.Components);
        Assert.Equal(2, array.CountX);
        Assert.Equal(2, array.CountY);
        Assert.Equal(1, array.CountZ);

        Assert.Equal((1.0, 2.0, 3.0), array.OriginSi);
        Assert.Equal((0.5, 0.25, 1.0), array.SpacingSi);

        // Node (1,0) is the second sample, node (0,1) the third.
        Assert.Equal(10.0, array.Values[0], 1e-12);
        Assert.Equal(20.0, array.Values[3], 1e-12);
        Assert.Equal(30.0, array.Values[6], 1e-12);
        Assert.Equal(40.0, array.Values[9], 1e-12);
    }

    [Fact]
    public void ItReadsWhatThisEngineItselfWrote()
    {
        // The round trip that matters most: `einzel export` writes ImageData, and a
        // format the engine can write and not read is a format with a seam in it.
        var grid = Fields.Solved.Grid2D.OverBox(0.0, 0.0, 0.03, 0.02, 8, 4);
        var field = new Fields.Solved.ScalarField2D(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                field[i, j] = (100.0 * i) + j;
            }
        }

        var array = VtkImageData.Read(VtuWriter.WriteScalarField(field, "potential_V"));

        Assert.Equal("potential_V", array.Name);
        Assert.Equal(1, array.Components);
        Assert.Equal(grid.CountX, array.CountX);
        Assert.Equal(grid.CountY, array.CountY);

        Assert.Equal(grid.SpacingX, array.SpacingSi.X, 1e-15);
        Assert.Equal(grid.SpacingY, array.SpacingSi.Y, 1e-15);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                Assert.Equal(field[i, j], array.Values[(j * grid.CountX) + i], 1e-12);
            }
        }

        output.WriteLine($"round-tripped {array.Values.Length} nodes exactly");
    }

    [Fact]
    public void ANamedArrayIsFoundAmongSeveral()
    {
        // A CFD export usually carries pressure, density and velocity, so taking
        // whichever came first would be as likely to read a pressure as a flow.
        var text = """
        <?xml version="1.0"?>
        <VTKFile type="ImageData" version="1.0" byte_order="LittleEndian">
          <ImageData WholeExtent="0 1 0 0 0 0" Origin="0 0 0" Spacing="1 1 1">
            <Piece Extent="0 1 0 0 0 0">
              <PointData>
                <DataArray type="Float64" Name="pressure" format="ascii">7 8</DataArray>
                <DataArray type="Float64" Name="velocity" NumberOfComponents="3" format="ascii">
                  1 2 3  4 5 6
                </DataArray>
              </PointData>
            </Piece>
          </ImageData>
        </VTKFile>
        """;

        var velocity = VtkImageData.Read(text, "velocity");

        Assert.Equal(3, velocity.Components);
        Assert.Equal([1.0, 2.0, 3.0, 4.0, 5.0, 6.0], velocity.Values);

        // And the first, when nothing is named, which is why naming matters.
        Assert.Equal("pressure", VtkImageData.Read(text).Name);
    }

    [Fact]
    public void ABinaryPayloadIsRefusedByName()
    {
        // A stated subset, in the same sense as EXT-7's JSON Schema subset. The
        // alternative is base64 and zlib inside a reader whose whole job is to get
        // a few thousand numbers into an array, and a file this cannot read is
        // refused rather than misread.
        var failure = Assert.Throws<EinzelException>(() => VtkImageData.Read(
            Document("type=\"Float64\" Name=\"velocity\" format=\"binary\"", "AAAA")));

        output.WriteLine(failure.Error.Constraint);
        output.WriteLine(failure.Error.Suggestion!);

        Assert.Contains("ascii", failure.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("ParaView", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtentThatDisagreesWithThePayloadIsRefused()
    {
        // A truncated file is the usual cause, and reading it anyway would shear
        // every value by one node - which looks like a flow rather than an error.
        var failure = Assert.Throws<EinzelException>(() => VtkImageData.Read(
            Document("type=\"Float64\" Name=\"v\" NumberOfComponents=\"3\" format=\"ascii\"",
                "1 2 3  4 5 6  7 8 9")));

        output.WriteLine(failure.Error.Constraint);

        Assert.Contains("12", failure.Error.Constraint, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingArrayNamesTheOnesThatArePresent()
    {
        // AGT-3: an error is a recovery instruction. Knowing what is in the file is
        // most of what a caller needs to fix the name.
        var failure = Assert.Throws<EinzelException>(() => VtkImageData.Read(
            Document("type=\"Float64\" Name=\"pressure\" format=\"ascii\"", "1 2 3 4"),
            "velocity"));

        output.WriteLine(failure.Error.Constraint);
        output.WriteLine(failure.Error.Suggestion!);

        Assert.Contains("velocity", failure.Error.Constraint, StringComparison.Ordinal);
        Assert.Contains("pressure", failure.Error.Suggestion!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnstructuredGridIsRefusedWithTheReason()
    {
        var failure = Assert.Throws<EinzelException>(() => VtkImageData.Read(
            "<VTKFile type=\"UnstructuredGrid\"></VTKFile>"));

        output.WriteLine(failure.Error.Suggestion!);

        Assert.Contains("ImageData", failure.Error.Constraint, StringComparison.Ordinal);
    }
}
