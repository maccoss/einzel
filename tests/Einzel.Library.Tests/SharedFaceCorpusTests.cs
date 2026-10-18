using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// No shipped model lets its mesh sample a face two disagreeing conductors share.
/// </summary>
/// <remarks>
/// <para>
/// Every device template and every corpus example, every solved element, through the same
/// detector the geometry builders run before a solve. The survey that wrote this test found
/// all of them clean once <c>astral-3d</c> had moved its foil mesh, which is the state worth
/// keeping: a template edit that lands a shared face on a node is a coin toss nobody would see,
/// because no refinement ladder can.
/// </para>
/// <para>
/// <b>The control is what makes a clean sweep mean anything.</b> <c>astral-3d</c> with its
/// mesh shift taken out has to trip, and on exactly the 180 stencil arms the survey found -
/// three shared foil faces on node planes, four plates, fifteen nodes across each stripe - and
/// on no node, since the stripe is thinner than a cell and holds none. A detector that asked
/// only about nodes, which is how this check was first specified, reports that case clean.
/// </para>
/// </remarks>
public sealed class SharedFaceCorpusTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _assets = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_assets))
        {
            Directory.Delete(_assets, recursive: true);
        }
    }

    [Fact]
    public void NoShippedModelSamplesASharedFace()
    {
        var inspected = 0;
        var tripped = new List<string>();

        foreach (var name in DeviceTemplates.Names())
        {
            inspected += Inspect($"template {name}", DeviceTemplates.Read(name), null, tripped);
        }

        foreach (var name in ExampleModels.Names)
        {
            var directory = Path.Combine(_assets, name);
            Directory.CreateDirectory(directory);
            ExampleModels.WriteAssets(name, directory);
            inspected += Inspect($"example {name}", ExampleModels.Read(name), directory, tripped);
        }

        output.WriteLine($"{inspected} solved elements inspected");

        // A sweep that inspected nothing would pass; the corpus has dozens of solved elements.
        Assert.True(inspected > 30, $"only {inspected} solved elements were inspected");
        Assert.Empty(tripped);
    }

    [Fact]
    public void TheAstralWithItsMeshShiftRemovedTrips()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
        parameters["meshShiftZ"] = parameters["meshShiftZ"] with { Value = 0.0 };

        var validation = ModelValidator.Validate(document with { Parameters = parameters });
        Assert.True(validation.IsValid);

        var solve = validation.Model!.Fields.Single(f => f.Solve3D is not null).Solve3D!;
        var geometry = Volume(solve);
        var found = GeometryBuilder3D.NodesOnSharedFaces(geometry, GeometryBuilder3D.BuildGrid(geometry));

        Assert.NotNull(found);
        output.WriteLine(found.ToWarning().Message);

        Assert.Equal(0, found.Nodes);
        Assert.Equal(180, found.Arms);
        Assert.StartsWith("foil", found.First, StringComparison.Ordinal);
    }

    private static int Inspect(string label, string json, string? directory, List<string> tripped)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null, directory);

        Assert.True(
            validation.IsValid,
            $"{label}: {(validation.IsValid ? string.Empty : validation.Errors[0].Constraint)}");

        var inspected = 0;

        for (var index = 0; index < validation.Model!.Fields.Count; index++)
        {
            var element = validation.Model.Fields[index];
            SharedFaceNodes? found;

            if (element.Solve is { } plane)
            {
                found = GeometryBuilder.NodesOnSharedFaces(plane, GeometryBuilder.BuildGrid(plane));
            }
            else if (element.Solve3D is { } volume)
            {
                var geometry = Volume(volume);
                found = GeometryBuilder3D.NodesOnSharedFaces(geometry, GeometryBuilder3D.BuildGrid(geometry));
            }
            else
            {
                continue;
            }

            inspected++;

            if (found is not null)
            {
                tripped.Add($"{label}, field {index}: {found.ToWarning().Message}");
            }
        }

        return inspected;
    }

    private static Geometry3D Volume(CompiledSolvedField3D solve) => new(
        solve.MinX, solve.MinY, solve.MinZ,
        solve.MaxX, solve.MaxY, solve.MaxZ,
        solve.CellSize, solve.Electrodes, solve.Tolerance)
    {
        Drives = solve.Drives,
        Stages = solve.Stages,
        Faces = Geometry3D.FacesOf(solve.Faces),
        ReflectAboutX = solve.ReflectAboutX,
    };
}
