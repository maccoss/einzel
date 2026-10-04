using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The compact Astral against the published instrument it is scaled from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Electrostatic similarity is exact, so the checks are exact.</b> Multiply every length
/// by s at fixed potentials and the ion follows the same path scaled by s, arriving s times
/// sooner. The meshes are lengths too, so the compact solve is the full-size discrete
/// problem in smaller units: the same interval counts, the same nodes inside the same
/// conductors, the same cut fractions. Asserting that at the level of the mesh is what
/// makes it cheap enough to run on every change - the flight it implies takes a minute.
/// </para>
/// <para>
/// <b>And the mesh check found that the published template was not a well-posed discrete
/// problem.</b> Adjacent foil slices hold different potentials and share a face, and at the
/// shipped mesh three of those faces sat exactly on a row of nodes. Whether such a node
/// belonged to one slice, the other, or neither was then decided by the last bit of the
/// face's arithmetic - so a scale of 0.2, whose arithmetic rounds differently from 1.0,
/// moved the flight time by 0.27 percent, and 0.1999999 by a different 0.03. The fix is a
/// mesh that is moved along the drift so no shared face lands on a node, and the test that
/// keeps it moved is <see cref="NoFaceSharedByTwoFoilSlicesSitsOnANode"/>.
/// </para>
/// </remarks>
public sealed class CompactAstralTests(ITestOutputHelper output)
{
    /// <summary>The scale the template ships at: one fifth of the published instrument.</summary>
    private const double Shipped = 0.2;

    private static CompiledModel Compile(string template, double? scale = null)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read(template));

        if (scale is { } s)
        {
            var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
            parameters["scale"] = parameters["scale"] with { Value = s };
            document = document with { Parameters = parameters };
        }

        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return validation.Model!;
    }

    /// <summary>
    /// The power of the scale a quantity of this dimension carries, at fixed potentials.
    /// </summary>
    /// <remarks>
    /// A length and a time each carry one. A current carries minus one, because a charge is
    /// held fixed and the time it passes in is not - which is what makes a potential
    /// (length two, time minus three, current minus one) carry none, a field strength minus
    /// one, and a speed none.
    /// </remarks>
    private static int ScalePower(Dimension dimension) =>
        dimension.Length + dimension.Time - dimension.Current;

    /// <summary>
    /// At its shipped scale every resolved quantity is the published one times the scale to
    /// its own power: lengths and times by a fifth, potentials and angles not at all.
    /// </summary>
    /// <remarks>
    /// Over the resolved surface rather than the declared one, so the derived quantities are
    /// checked too - the mirror separation, the stripe positions, the detector plane - and a
    /// derived length written with a literal inside it would be found here.
    /// </remarks>
    [Fact]
    public void EveryQuantityScalesAsItsDimensionSays()
    {
        var full = Compile("astral-3d").Parameters.Parameters;
        var compact = Compile("compact-astral-3d").Parameters.Parameters;

        var scaled = 0;
        var fixedByScale = 0;

        foreach (var (name, published) in full)
        {
            Assert.True(compact.ContainsKey(name), $"{name} is missing from the compact analyzer");

            var power = ScalePower(published.Value.Dimension);
            var expected = published.Value.SiValue * Math.Pow(Shipped, power);
            var actual = compact[name].Value.SiValue;

            Assert.True(
                Math.Abs(actual - expected) <= 1e-12 * Math.Max(Math.Abs(expected), 1e-300),
                $"{name}: {actual} against {expected} ({published.Value.Dimension}, scale power {power})");

            if (power == 0)
            {
                fixedByScale++;
            }
            else
            {
                scaled++;
            }
        }

        output.WriteLine($"{scaled} quantities scale, {fixedByScale} do not");

        // The guard against a comparison that checked nothing: a surface with no lengths in
        // it would pass the loop above. So one of each kind is named, rather than a count
        // that would move with the template.
        Assert.Equal(1, ScalePower(full["driftLength"].Value.Dimension));
        Assert.Equal(1, ScalePower(full["capToCap"].Value.Dimension));
        Assert.Equal(0, ScalePower(full["v0"].Value.Dimension));
        Assert.Equal(0, ScalePower(full["injectionAngle"].Value.Dimension));
    }

    /// <summary>At a scale of one the compact analyzer is the published one, to the bit.</summary>
    /// <remarks>
    /// Multiplying by exactly one is exact, so any difference here is a quantity the
    /// generator failed to route through the scale, or routed through it twice.
    /// </remarks>
    [Fact]
    public void AtAScaleOfOneItIsThePublishedInstrumentToTheBit()
    {
        var full = Compile("astral-3d");
        var compact = Compile("compact-astral-3d", scale: 1.0);

        foreach (var (name, published) in full.Parameters.Parameters)
        {
            Assert.Equal(published.Value.SiValue, compact.Parameters.Parameters[name].Value.SiValue);
        }

        Assert.Equal(full.SourcePosition, compact.SourcePosition);
        Assert.Equal(full.DetectorPoint, compact.DetectorPoint);
        Assert.Equal(full.MaximumFlightTimeSi, compact.MaximumFlightTimeSi);
    }

    /// <summary>
    /// The compact solve is the full-size discrete problem: every mesh has the same interval
    /// counts, the same nodes are fixed at the same potentials, and every cut link is cut at
    /// the same fraction of its cell by a conductor at the same potential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the statement that the compact analyzer is exactly similar rather than
    /// approximately, and it is what failed on the template as first generated: the two
    /// mirror cross-sections agreed to the last node, and in the foil's volume 454 nodes and
    /// 1,586 of 244,664 cut links changed hands at the 0.2 scale, because the faces two foil
    /// slices share sat exactly on nodes and the scaled arithmetic rounded to the other side.
    /// </para>
    /// <para>
    /// Cut fractions are compared to 1e-9 of a cell rather than exactly: a fraction is a
    /// ratio of two scaled lengths, and each rounds on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCompactSolveIsTheSameDiscreteProblem()
    {
        var full = Compile("astral-3d");
        var compact = Compile("compact-astral-3d");

        Assert.Equal(full.Fields.Count, compact.Fields.Count);

        for (var f = 0; f < full.Fields.Count; f++)
        {
            if (full.Fields[f].Solve is { } plane)
            {
                ComparePlane(f, plane, compact.Fields[f].Solve!);
            }
            else if (full.Fields[f].Solve3D is { } volume)
            {
                CompareVolume(f, volume, compact.Fields[f].Solve3D!);
            }
        }
    }

    private void ComparePlane(int field, CompiledSolvedField full, CompiledSolvedField compact)
    {
        var fullGrid = GeometryBuilder.BuildGrid(full);
        var compactGrid = GeometryBuilder.BuildGrid(compact);

        Assert.Equal(fullGrid.CountX, compactGrid.CountX);
        Assert.Equal(fullGrid.CountY, compactGrid.CountY);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var a = GeometryBuilder.BuildMask(full, fullGrid);
        var b = GeometryBuilder.BuildMask(compact, compactGrid);
        output.WriteLine($"field {field}: two masks built in {clock.Elapsed.TotalSeconds:0.0} s");

        var nodes = 0;
        var links = 0;
        var cut = 0;

        for (var i = 0; i < fullGrid.CountX; i++)
        {
            for (var j = 0; j < fullGrid.CountY; j++)
            {
                if (a.IsFixed(i, j) != b.IsFixed(i, j)
                    || (a.IsFixed(i, j) && a.ValueAt(i, j) != b.ValueAt(i, j)))
                {
                    nodes++;
                }

                foreach (var direction in Enum.GetValues<StencilDirection>())
                {
                    var fa = a.Cuts?.Fraction(i, j, direction) ?? 1.0;
                    var fb = b.Cuts?.Fraction(i, j, direction) ?? 1.0;
                    var pa = a.Cuts?.Potential(i, j, direction) ?? 0.0;
                    var pb = b.Cuts?.Potential(i, j, direction) ?? 0.0;

                    cut += fa < 1.0 ? 1 : 0;
                    links += Math.Abs(fa - fb) > 1e-9 || (fa < 1.0 && pa != pb) ? 1 : 0;
                }
            }
        }

        output.WriteLine(
            $"field {field}: {fullGrid.CountX}x{fullGrid.CountY}, {nodes} nodes and {links} of {cut} cut links differ");

        Assert.True(cut > 0, $"field {field} has no cut links, so this compared nothing");
        Assert.Equal(0, nodes);
        Assert.Equal(0, links);
    }

    private void CompareVolume(int field, CompiledSolvedField3D full, CompiledSolvedField3D compact)
    {
        static Geometry3D Of(CompiledSolvedField3D solve) => new(
            solve.MinX, solve.MinY, solve.MinZ,
            solve.MaxX, solve.MaxY, solve.MaxZ,
            solve.CellSize, solve.Electrodes, solve.Tolerance)
        {
            Drives = solve.Drives,
            Faces = Geometry3D.FacesOf(solve.Faces),
            ReflectAboutX = solve.ReflectAboutX,
        };

        var fullGrid = GeometryBuilder3D.BuildGrid(Of(full));
        var compactGrid = GeometryBuilder3D.BuildGrid(Of(compact));

        Assert.Equal(fullGrid.CountX, compactGrid.CountX);
        Assert.Equal(fullGrid.CountY, compactGrid.CountY);
        Assert.Equal(fullGrid.CountZ, compactGrid.CountZ);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var a = GeometryBuilder3D.BuildMask(Of(full), fullGrid);
        var b = GeometryBuilder3D.BuildMask(Of(compact), compactGrid);
        output.WriteLine($"field {field}: two masks built in {clock.Elapsed.TotalSeconds:0.0} s");

        var nodes = 0;
        var links = 0;
        var cut = 0;

        for (var i = 0; i < fullGrid.CountX; i++)
        {
            for (var j = 0; j < fullGrid.CountY; j++)
            {
                for (var k = 0; k < fullGrid.CountZ; k++)
                {
                    if (a.IsFixed(i, j, k) != b.IsFixed(i, j, k)
                        || (a.IsFixed(i, j, k) && a.Value(i, j, k) != b.Value(i, j, k)))
                    {
                        nodes++;
                    }

                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        var pa = 0.0;
                        var pb = 0.0;
                        var fa = a.Cuts?.Fraction(i, j, k, arm, out pa) ?? 1.0;
                        var fb = b.Cuts?.Fraction(i, j, k, arm, out pb) ?? 1.0;

                        cut += fa < 1.0 ? 1 : 0;
                        links += Math.Abs(fa - fb) > 1e-9 || (fa < 1.0 && pa != pb) ? 1 : 0;
                    }
                }
            }
        }

        output.WriteLine(
            $"field {field}: {fullGrid.CountX}x{fullGrid.CountY}x{fullGrid.CountZ}, "
            + $"{nodes} nodes and {links} of {cut} cut links differ");

        Assert.True(cut > 0, $"field {field} has no cut links, so this compared nothing");
        Assert.Equal(0, nodes);
        Assert.Equal(0, links);
    }

    /// <summary>
    /// No face two foil slices share lies on a row of the foil solve's nodes, in either the
    /// published instrument or the compact one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node exactly on a face between two conductors at different potentials belongs to
    /// whichever of them the face's last bit favors, or to neither - and a free node with a
    /// vanishing arm to each side takes whatever mixture of the two the rounding set. That is
    /// not a discretization error the mesh can be refined away from; it is a coin toss, and at
    /// the shipped mesh it was worth 0.27 percent of the flight time.
    /// </para>
    /// <para>
    /// A tenth of a cell is the best any placement can do here: the faces are 28.125 mm apart
    /// and the nodes 2.93 mm, so the faces' positions within a cell repeat every fifth of one.
    /// The assertion is a twentieth, a margin rather than the limit.
    /// </para>
    /// </remarks>
    /// <param name="template">The template.</param>
    [Theory]
    [InlineData("astral-3d")]
    [InlineData("compact-astral-3d")]
    public void NoFaceSharedByTwoFoilSlicesSitsOnANode(string template)
    {
        var solve = Compile(template).Fields.Single(f => f.Solve3D is not null).Solve3D!;

        var grid = Grid3D.OverBox(
            solve.MinX, solve.MinY, solve.MinZ,
            solve.MaxX, solve.MaxY, solve.MaxZ,
            solve.CellSize);

        var foil = solve.Electrodes.Where(e => e.Name.StartsWith("foil", StringComparison.Ordinal)).ToList();

        Assert.Equal(64, foil.Count);   // four plates of sixteen slices

        var closest = double.PositiveInfinity;
        var at = string.Empty;

        foreach (var slice in foil)
        {
            foreach (var (face, z) in new[] { ("minZ", slice.MinZ), ("maxZ", slice.MaxZ) })
            {
                var position = (z - grid.OriginZ) / grid.SpacingZ;
                var distance = Math.Abs(position - Math.Round(position));

                if (distance < closest)
                {
                    closest = distance;
                    at = $"{slice.Name}.{face} at {z * 1e3:0.######} mm, node {position:0.#########}";
                }
            }
        }

        output.WriteLine($"{template}: closest shared face is {closest:0.####} of a cell from a node - {at}");

        Assert.True(closest >= 0.05, $"{at} sits {closest:0.####} of a cell from a node");
    }
}
