using System.Diagnostics;

using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The culled volume mask against the reference loops it replaced, node for node and arm
/// for arm.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim is bit-identity, so the comparison is of bits.</b> Every fixed flag, every
/// fixed value, every cut fraction and every cut potential is compared as its IEEE pattern
/// rather than with a tolerance, together with the counts the solver reads off the mask.
/// A cull that dropped one node the reference fixes, or handed one arm to a different
/// electrode, fails here whatever it would have done to the field.
/// </para>
/// <para>
/// <b>Each electrode holds a potential no other holds.</b> The masks are built with every
/// electrode labelled by its position in the list rather than with its declared potential,
/// because two grounded electrodes swapping a node between them change nothing a declared
/// potential can show. With distinct labels, equal values mean the same electrode won every
/// node and every arm - and since which electrode wins is a matter of geometry alone, that
/// settles the comparison for any potentials a basis solve hands the builder.
/// </para>
/// <para>
/// <b>At the shipped mesh, and at every coarse level beneath it.</b> Both are built by the
/// same rasterizer, so both are culled; the coarse levels carry no cuts and are cheap.
/// </para>
/// <para>
/// <b>One exception, and it is the reference's cost rather than the claim's.</b> The linear
/// ion trap is compared at twice its shipped cell. At the shipped cell its unculled mask is
/// about two hundred million prism entry tests, each allocating a list of crossings - 29 s
/// on a desktop and 2 min 38 s on a four-core CI runner, where it starved the parallel test
/// assemblies badly enough that an MCP handshake timed out. It was compared at the shipped
/// cell once, bit for bit, when the cull was written; what this keeps checking is the same
/// geometry, primitives and range arithmetic on an eighth of the nodes.
/// </para>
/// </remarks>
public sealed class VolumeMaskCullingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every shipped volume geometry: four device templates and one corpus example, each with
    /// the multiple of its shipped cell it is compared at.
    /// </summary>
    public static TheoryData<string, string, double> Models => new()
    {
        { "template", "c-trap", 1.0 },
        { "template", "astral-3d", 1.0 },
        { "template", "linear-ion-trap-3d", 2.0 },
        { "template", "segmented-quadrupole", 1.0 },
        { "example", "parallel-plate-gap-3d", 1.0 },
    };

    /// <summary>A shipped volume geometry gives the reference mask exactly, culled.</summary>
    /// <param name="source">Whether the model is a device template or a corpus example.</param>
    /// <param name="name">Its name.</param>
    /// <param name="cellScale">The multiple of the shipped cell size to compare at.</param>
    [Theory]
    [MemberData(nameof(Models))]
    public void AShippedVolumeGeometryGivesTheReferenceMaskBitForBit(
        string source, string name, double cellScale)
    {
        var shipped = GeometryOf(source == "template" ? DeviceTemplates.Read(name) : ExampleModels.Read(name));
        var geometry = shipped with { CellSize = shipped.CellSize * cellScale };
        var label = Labels(geometry);
        var grid = GeometryBuilder3D.BuildGrid(geometry);

        var clock = Stopwatch.StartNew();
        var reference = GeometryBuilder3D.BuildMask(geometry, grid, label, MaskCulling.None);
        var referenceMs = clock.ElapsedMilliseconds;

        clock.Restart();
        var culled = GeometryBuilder3D.BuildMask(geometry, grid, label, MaskCulling.Bounds);
        var culledMs = clock.ElapsedMilliseconds;

        output.WriteLine(
            $"{name}: {grid.CountX}x{grid.CountY}x{grid.CountZ} nodes, "
            + $"{geometry.Electrodes.Count} electrodes, {reference.FixedCount} fixed, "
            + $"{reference.Cuts?.CutCount ?? 0} cut arms; mask {referenceMs} ms unculled, "
            + $"{culledMs} ms culled");

        // Against a vacuous comparison: an empty mask agrees with any cull. The labels
        // count how many distinct electrodes the reference actually represents.
        Assert.True(reference.InteriorFixedCount > 0, "the reference fixes no interior node");
        Assert.True(reference.Cuts?.CutCount > 0, "the reference cuts no arm");
        Assert.True(
            Represented(reference) > 1,
            "the reference represents fewer than two electrodes, so no winner can swap");

        AssertIdentical(reference, culled, $"{name}, fine");

        var coarseReference = GeometryBuilder3D.Coarsener(geometry, label, MaskCulling.None);
        var coarseCulled = GeometryBuilder3D.Coarsener(geometry, label, MaskCulling.Bounds);

        var levels = 0;
        var coarseReferenceMs = 0L;
        var coarseCulledMs = 0L;

        for (var coarse = grid; coarse.CanCoarsen;)
        {
            coarse = coarse.Coarsen();
            levels++;

            clock.Restart();
            var unculledLevel = coarseReference(coarse);
            coarseReferenceMs += clock.ElapsedMilliseconds;

            clock.Restart();
            var culledLevel = coarseCulled(coarse);
            coarseCulledMs += clock.ElapsedMilliseconds;

            AssertIdentical(unculledLevel, culledLevel, $"{name}, coarse level {levels}");
        }

        output.WriteLine(
            $"{name}: {levels} coarse levels identical as well; {coarseReferenceMs} ms unculled, "
            + $"{coarseCulledMs} ms culled");
    }

    /// <summary>
    /// A geometry whose faces lie exactly on nodes, which is where a cull one cell too tight
    /// is caught whatever the shipped meshes happen to do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The shipped templates are a regression, not a proof of teeth.</b> A node exactly on
    /// a face is inside, because <c>Contains</c> is a signed distance at most zero, and a cull
    /// that rounds its range the wrong way drops it - but only if some face of some electrode
    /// lands exactly on a node of the mesh it is solved on, which a shipped template may or
    /// may not do. Here every flat face is placed on a node by construction, the sphere and
    /// the cylinder have extreme points on nodes, and the rest cover what else the range
    /// arithmetic has to get right: a tilted box whose bounds come from a rotation, a
    /// revolved sector, an electrode running off the grid, one wholly outside it, and a
    /// box sharing a face with another at a different potential, so that the order in
    /// which the two are written is visible in the values.
    /// </para>
    /// <para>
    /// <b>Every axis a shape can take, not only the one the templates happen to use.</b>
    /// Every shipped template runs its cylinders, prisms and revolves along z, and each
    /// shape's bounding box is a switch on its axis - so a transposed arm for a y-axis
    /// cylinder would cull away nodes the metal occupies, and nothing shipped would notice.
    /// Each of cylinder, prism, revolve and tilt appears here on all three axes, and every
    /// electrode that touches the grid is asserted to be represented in the reference, so
    /// none of them can pass by contributing nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void FacesLyingExactlyOnNodesAreCulledExactlyAsTheReferenceAsksThem()
    {
        var grid = Grid3D.OverBox(0.0, 0.0, 0.0, 0.032, 0.032, 0.032, 0.001);

        Assert.Equal(33, grid.CountX);

        double X(int i) => grid.X(i);
        double Y(int j) => grid.Y(j);
        double Z(int k) => grid.Z(k);

        List<CompiledElectrode3D> electrodes =
        [
            new()
            {
                Name = "box", Shape = Electrode3DShape.Box,
                MinX = X(3), MaxX = X(6), MinY = Y(2), MaxY = Y(5), MinZ = Z(4), MaxZ = Z(9),
            },

            // Sharing the first box's upper x face, so the node plane between them is
            // fixed twice and the later electrode's label is the one that must survive.
            new()
            {
                Name = "abutting", Shape = Electrode3DShape.Box,
                MinX = X(6), MaxX = X(8), MinY = Y(2), MaxY = Y(5), MinZ = Z(4), MaxZ = Z(9),
            },

            new()
            {
                Name = "sphere", Shape = Electrode3DShape.Sphere,
                CentreX = X(11), CentreY = Y(11), CentreZ = Z(11), Radius = X(14) - X(11),
            },

            new()
            {
                Name = "cylinder", Shape = Electrode3DShape.Cylinder, Axis = CylinderAxis.Z,
                CentreX = X(4), CentreY = Y(12), Radius = X(6) - X(4), Lower = Z(2), Upper = Z(8),
            },

            new()
            {
                Name = "prism", Shape = Electrode3DShape.Prism, Axis = CylinderAxis.X,
                Lower = X(9), Upper = X(15),
                Vertices = [(Y(1), Z(12)), (Y(4), Z(12)), (Y(4), Z(15)), (Y(1), Z(15))],
            },

            new()
            {
                Name = "tilted", Shape = Electrode3DShape.Box,
                MinX = X(10), MaxX = X(14), MinY = Y(3), MaxY = Y(7), MinZ = Z(2), MaxZ = Z(3),
                TiltAxis = CylinderAxis.Y, TiltHalfTurns = 0.03,
            },

            new()
            {
                Name = "sector", Shape = Electrode3DShape.Revolve, Axis = CylinderAxis.Z,
                Vertices = [(X(2), Z(13)), (X(4), Z(13)), (X(4), Z(15)), (X(2), Z(15))],
                FromHalfTurns = 0.0, ToHalfTurns = 0.5,
            },

            // Running off the grid on three sides, so the range is clamped rather than taken.
            new()
            {
                Name = "overhanging", Shape = Electrode3DShape.Box,
                MinX = X(30), MaxX = X(32) + 0.004, MinY = Y(29), MaxY = Y(32) + 0.004,
                MinZ = -0.004, MaxZ = Z(1),
            },

            // The same shapes on the axes the templates do not use.
            new()
            {
                Name = "cylinder-x", Shape = Electrode3DShape.Cylinder, Axis = CylinderAxis.X,
                CentreY = Y(20), CentreZ = Z(4), Radius = Y(22) - Y(20), Lower = X(18), Upper = X(24),
            },

            new()
            {
                Name = "cylinder-y", Shape = Electrode3DShape.Cylinder, Axis = CylinderAxis.Y,
                CentreX = X(28), CentreZ = Z(4), Radius = X(30) - X(28), Lower = Y(18), Upper = Y(24),
            },

            new()
            {
                Name = "prism-y", Shape = Electrode3DShape.Prism, Axis = CylinderAxis.Y,
                Lower = Y(26), Upper = Y(31),
                Vertices = [(X(18), Z(10)), (X(21), Z(10)), (X(21), Z(13)), (X(18), Z(13))],
            },

            // A triangle, so the outline is not its own bounding box.
            new()
            {
                Name = "prism-z", Shape = Electrode3DShape.Prism, Axis = CylinderAxis.Z,
                Lower = Z(10), Upper = Z(15),
                Vertices = [(X(24), Y(18)), (X(28), Y(18)), (X(24), Y(22))],
            },

            // About the x axis through the origin, from 45 to 225 degrees: the sweep crosses
            // two quarter turns, and part of it lies below the grid.
            new()
            {
                Name = "revolve-x", Shape = Electrode3DShape.Revolve, Axis = CylinderAxis.X,
                Vertices = [(0.002, X(26)), (0.004, X(26)), (0.004, X(30)), (0.002, X(30))],
                FromHalfTurns = 0.25, ToHalfTurns = 1.25,
            },

            new()
            {
                Name = "revolve-y", Shape = Electrode3DShape.Revolve, Axis = CylinderAxis.Y,
                Vertices = [(0.002, Y(26)), (0.004, Y(26)), (0.004, Y(30)), (0.002, Y(30))],
                FromHalfTurns = 0.0, ToHalfTurns = 0.5,
            },

            new()
            {
                Name = "tilted-x", Shape = Electrode3DShape.Box,
                MinX = X(18), MaxX = X(22), MinY = Y(26), MaxY = Y(28), MinZ = Z(20), MaxZ = Z(26),
                TiltAxis = CylinderAxis.X, TiltHalfTurns = 0.04,
            },

            new()
            {
                Name = "tilted-z", Shape = Electrode3DShape.Box,
                MinX = X(26), MaxX = X(30), MinY = Y(24), MaxY = Y(28), MinZ = Z(20), MaxZ = Z(22),
                TiltAxis = CylinderAxis.Z, TiltHalfTurns = -0.05,
            },

            // Nowhere on the grid at all: an empty range, and on a coarse level the pin.
            new()
            {
                Name = "outside", Shape = Electrode3DShape.Sphere,
                CentreX = 0.05, CentreY = 0.05, CentreZ = 0.05, Radius = 0.002,
            },
        ];

        var geometry = new Geometry3D(0.0, 0.0, 0.0, 0.032, 0.032, 0.032, 0.001, electrodes);
        var label = Labels(geometry);

        var reference = GeometryBuilder3D.BuildMask(geometry, grid, label, MaskCulling.None);
        var culled = GeometryBuilder3D.BuildMask(geometry, grid, label, MaskCulling.Bounds);

        // The construction does what it claims: nodes on faces are fixed, and the shared
        // plane went to the electrode written second.
        Assert.True(reference.IsFixed(3, 2, 4), "a box's corner node is not fixed");
        Assert.True(reference.IsFixed(14, 11, 11), "the sphere's extreme node is not fixed");
        Assert.Equal(label(electrodes[1]), reference.Value(6, 3, 5));

        // Against an electrode that passes by contributing nothing: each one that touches
        // the grid holds a node or a cut arm in the reference.
        var present = Labelled(reference);

        foreach (var electrode in electrodes.Where(e => e.Name != "outside"))
        {
            Assert.True(
                present.Contains(label(electrode)),
                $"{electrode.Name} is represented nowhere in the reference mask");
        }

        AssertIdentical(reference, culled, "synthetic, fine");

        var coarseReference = GeometryBuilder3D.Coarsener(geometry, label, MaskCulling.None);
        var coarseCulled = GeometryBuilder3D.Coarsener(geometry, label, MaskCulling.Bounds);

        for (var coarse = grid; coarse.CanCoarsen;)
        {
            coarse = coarse.Coarsen();
            AssertIdentical(coarseReference(coarse), coarseCulled(coarse), $"synthetic, {coarse.CountX}");
        }
    }

    /// <summary>The volume solve a model declares, as the field assembly would build it.</summary>
    private static Geometry3D GeometryOf(string document)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(document));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        var solve = validation.Model!.Fields.Single(f => f.Solve3D is not null).Solve3D!;

        return new Geometry3D(
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

    /// <summary>Each electrode labelled by one plus its position, keyed by identity.</summary>
    /// <remarks>
    /// By reference rather than by value: two electrodes declared identically are equal
    /// records, and a dictionary keyed by value would give them one label between them.
    /// </remarks>
    private static Func<CompiledElectrode3D, double> Labels(Geometry3D geometry)
    {
        var labels = new Dictionary<CompiledElectrode3D, double>(ReferenceEqualityComparer.Instance);

        for (var index = 0; index < geometry.Electrodes.Count; index++)
        {
            labels.Add(geometry.Electrodes[index], index + 1.0);
        }

        return electrode => labels[electrode];
    }

    /// <summary>Every electrode label a mask holds, on a fixed node or on a cut arm.</summary>
    private static HashSet<double> Labelled(DirichletMask3D mask)
    {
        var grid = mask.Grid;
        var seen = new HashSet<double>();

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k))
                    {
                        seen.Add(mask.Value(i, j, k));
                    }

                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        if (mask.Cuts is { } cuts && cuts.Fraction(i, j, k, arm, out var held) < 1.0)
                        {
                            seen.Add(held);
                        }
                    }
                }
            }
        }

        return seen;
    }

    /// <summary>How many distinct electrode labels a mask holds on its fixed nodes.</summary>
    private static int Represented(DirichletMask3D mask)
    {
        var grid = mask.Grid;
        var seen = new HashSet<double>();

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (mask.IsFixed(i, j, k) && mask.Value(i, j, k) != 0.0)
                    {
                        seen.Add(mask.Value(i, j, k));
                    }
                }
            }
        }

        return seen.Count;
    }

    /// <summary>Asserts two masks are the same bits everywhere the solver can read them.</summary>
    private static void AssertIdentical(DirichletMask3D reference, DirichletMask3D culled, string where)
    {
        Assert.Same(reference.Grid, culled.Grid);

        var grid = reference.Grid;
        var differences = 0;
        var first = new List<string>();

        void Differs(string what)
        {
            if (differences++ < 5)
            {
                first.Add(what);
            }
        }

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (reference.IsFixed(i, j, k) != culled.IsFixed(i, j, k)
                        || Bits(reference.Value(i, j, k)) != Bits(culled.Value(i, j, k)))
                    {
                        Differs(
                            $"node ({i},{j},{k}): fixed {reference.IsFixed(i, j, k)} at "
                            + $"{reference.Value(i, j, k)} against {culled.IsFixed(i, j, k)} at "
                            + $"{culled.Value(i, j, k)}");
                    }

                    if (reference.Cuts is null)
                    {
                        continue;
                    }

                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        var a = reference.Cuts.Fraction(i, j, k, arm, out var heldA);
                        var b = culled.Cuts!.Fraction(i, j, k, arm, out var heldB);

                        if (Bits(a) != Bits(b) || Bits(heldA) != Bits(heldB))
                        {
                            Differs($"arm {arm} of ({i},{j},{k}): {a} at {heldA} against {b} at {heldB}");
                        }
                    }
                }
            }
        }

        Assert.True(
            differences == 0,
            $"{where}: {differences} differences from the reference, first {string.Join("; ", first)}");

        Assert.Equal(reference.FixedCount, culled.FixedCount);
        Assert.Equal(reference.InteriorFixedCount, culled.InteriorFixedCount);
        Assert.Equal(Bits(reference.SmallestFeature), Bits(culled.SmallestFeature));

        Assert.Equal(reference.LowerX, culled.LowerX);
        Assert.Equal(reference.UpperX, culled.UpperX);
        Assert.Equal(reference.LowerY, culled.LowerY);
        Assert.Equal(reference.UpperY, culled.UpperY);
        Assert.Equal(reference.LowerZ, culled.LowerZ);
        Assert.Equal(reference.UpperZ, culled.UpperZ);

        Assert.Equal(reference.Cuts is null, culled.Cuts is null);

        if (reference.Cuts is not null)
        {
            Assert.Equal(reference.Cuts.CutCount, culled.Cuts!.CutCount);
            Assert.Equal(Bits(reference.Cuts.SmallestFraction), Bits(culled.Cuts.SmallestFraction));
        }
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
}
