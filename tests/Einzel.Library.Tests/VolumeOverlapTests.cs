using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The volume overlap check against the geometries it must not refuse, and against the
/// one it did.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are the must-not-refuse half of the check.</b> A guard that refuses a
/// legitimate geometry is worse than one that sometimes misses, so the shipped
/// templates are the regression that matters: four volume geometries built from four
/// different primitives, several of them with conductors deliberately touching.
/// </para>
/// <para>
/// <b>And the Astral is the one it caught.</b> Every drift stripe was extruded outward
/// by its own declared thickness, through the inner face of the grounded board it is
/// printed on and 1.285 mm into the metal behind it - two conductors in one place at
/// two potentials, which is exactly the condition the plane check has refused since a
/// multipole guide was built with rods through one another.
/// </para>
/// </remarks>
public sealed class VolumeOverlapTests(ITestOutputHelper output)
{
    private static CompiledSolvedField3D Solve(string template)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read(template)));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!.Fields.Single(f => f.Solve3D is not null).Solve3D!;
    }

    /// <summary>Every shipped volume geometry passes, and passing is the claim.</summary>
    /// <remarks>
    /// The C-trap is the sharpest of the four: its five rods are nested arcs about one
    /// axis, so <c>rodInnerUpper</c>'s bounding box lies wholly inside
    /// <c>rodOuter</c>'s while the metal is nowhere near it. A check that screened
    /// on boxes alone would refuse it.
    /// </remarks>
    [Theory]
    [InlineData("c-trap")]
    [InlineData("astral-3d")]
    [InlineData("linear-ion-trap-3d")]
    [InlineData("segmented-quadrupole")]
    public void AShippedVolumeGeometryIsNotRefused(string template)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var solve = Solve(template);
        started.Stop();

        output.WriteLine(
            $"{template}: {solve.Electrodes.Count} electrodes, validated in "
            + $"{started.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Each drift stripe now meets the board's inner face exactly, rather than crossing
    /// it.
    /// </summary>
    /// <remarks>
    /// Stated as a face coincidence rather than as "no error was raised", because the
    /// second passes equally well if the check stops looking. The board's inner face is
    /// <c>halfGap</c> and the stripe's outer face is now the same expression, so the two
    /// are tangent - which is allowed, and is how a printed conductor on a board is
    /// built.
    /// </remarks>
    [Fact]
    public void TheAstralDriftStripesMeetTheBoardFaceRatherThanCrossingIt()
    {
        var solve = Solve("astral-3d");

        var board = solve.Electrodes.Single(e => e.Name == "near0topGround");
        var stripes = solve.Electrodes
            .Where(e => e.Name.StartsWith("foilNearAbove", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(16, stripes.Count);

        foreach (var stripe in stripes)
        {
            Assert.Equal(board.MinY, stripe.MaxY);
            Assert.True(stripe.MinY < stripe.MaxY);
        }

        output.WriteLine(
            $"stripe {stripes[0].MinY * 1e3:0.####} .. {stripes[0].MaxY * 1e3:0.####} mm, "
            + $"board {board.MinY * 1e3:0.####} .. {board.MaxY * 1e3:0.####} mm");
    }

    /// <summary>
    /// The declaration changed and the discretisation did not, at any mesh - so every
    /// Astral number measured from this template stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what makes the fix safe rather than merely legal.</b> Two mechanisms
    /// combine, and between them the buried metal was never part of the problem: a cut
    /// link records the <em>nearest</em> surface along its arm, so the stripe's front
    /// face at 20 mm wins over anything behind it; and a node inside both goes to the
    /// board, because the boards are written last. Whatever the document said about the
    /// 1.285 mm behind that face, the solve has always seen a stripe face at 20 mm with
    /// grounded board beyond.
    /// </para>
    /// <para>
    /// <b>Over a ladder of meshes, because one would not settle it.</b> At the shipped
    /// 4 mm cell no node lies inside the stripe at all - it is 0.715 mm thick and the
    /// nodes nearest it are at 19.20 and 23.04 mm - so a single-mesh comparison could
    /// only show that a sub-cell feature is sub-cell. Down to a 0.234 mm cell in y the
    /// overlap is fully resolved, and the two masks are still identical. The
    /// interpenetration was therefore <em>latent</em>: a refinement study on this
    /// template would not have silently changed the geometry, which is the thing worth
    /// knowing and is not what one would guess.
    /// </para>
    /// <para>
    /// Its teeth are measured rather than asserted: extruding the stripe by 8 mm
    /// instead of 2 - far enough to leave the back of the board - fails it.
    /// </para>
    /// </remarks>
    /// <param name="cellSize">The cell to compare the two declarations on, in metres.</param>
    [Theory]
    [InlineData(4e-3)]
    [InlineData(2e-3)]
    [InlineData(1e-3)]
    [InlineData(0.5e-3)]
    [InlineData(0.25e-3)]
    public void SayingSoChangesNoNodeAndNoCutLinkAtAnyMesh(double cellSize)
    {
        var solve = Solve("astral-3d");

        // The declaration as it stood: extruded outward by the thickness it used to
        // declare, which is what drove it into the board.
        const double WasThick = 2e-3;

        var was = solve.Electrodes
            .Select(e => !e.Name.StartsWith("foil", StringComparison.Ordinal)
                ? e
                : e.MaxY > 0.0
                    ? e with { MaxY = e.MinY + WasThick }
                    : e with { MinY = e.MaxY - WasThick })
            .ToList();

        var stripe = solve.Electrodes.First(e => e.Name == "foilNearAbove-0");

        Assert.NotEqual(stripe.MaxY, was.First(e => e.Name == "foilNearAbove-0").MaxY);

        // A box around one stripe and the board behind it, which is where the two
        // declarations differ - the rest of a 730 mm analyser is identical by
        // construction and costs a hundred times as much to compare.
        var grid = Grid3D.OverBox(
            stripe.MinX - 0.004,
            0.015,
            stripe.MinZ + 0.004,
            stripe.MinX + 0.012,
            0.030,
            stripe.MinZ + 0.012,
            cellSize);

        Geometry3D Of(IReadOnlyList<CompiledElectrode3D> electrodes) => new(
            solve.MinX, solve.MinY, solve.MinZ,
            solve.MaxX, solve.MaxY, solve.MaxZ,
            solve.CellSize, electrodes, solve.Tolerance)
        {
            Drives = solve.Drives,
            Faces = Geometry3D.FacesOf(solve.Faces),
            ReflectAboutX = solve.ReflectAboutX,
        };

        var now = GeometryBuilder3D.BuildMask(Of(solve.Electrodes), grid);
        var then = GeometryBuilder3D.BuildMask(Of(was), grid);

        var differingNodes = 0;
        var differingLinks = 0;
        var stripeLinks = 0;

        for (var i = 0; i < grid.CountX; i++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var k = 0; k < grid.CountZ; k++)
                {
                    Assert.Equal(then.IsFixed(i, j, k), now.IsFixed(i, j, k));

                    if (now.IsFixed(i, j, k) && now.Value(i, j, k) != then.Value(i, j, k))
                    {
                        differingNodes++;
                    }

                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        var held = 0.0;
                        var a = now.Cuts?.Fraction(i, j, k, arm, out held) ?? 1.0;
                        var b = then.Cuts?.Fraction(i, j, k, arm, out _) ?? 1.0;

                        if (a < 1.0 && held != 0.0)
                        {
                            stripeLinks++;
                        }

                        if (a != b)
                        {
                            differingLinks++;
                        }
                    }
                }
            }
        }

        output.WriteLine(
            $"cell {cellSize * 1e3:0.###} mm gives {grid.SpacingY * 1e3:0.####} mm in y over "
            + $"{grid.CountX}x{grid.CountY}x{grid.CountZ}: {differingNodes} nodes and "
            + $"{differingLinks} links differ, {stripeLinks} links hold the stripe");

        // The guard against a vacuous comparison, and it has to be specific: at the
        // shipped cell the stripe holds no NODE at all, so "some conductor is in the
        // box" would be satisfied by the grounded board alone and the comparison would
        // be of a region the two declarations agree about by construction. What is
        // asserted is that the stripe itself is represented here - a cut link carrying
        // a potential the board does not hold.
        Assert.True(stripeLinks > 0, "the comparison box does not reach the foil stripe");

        Assert.Equal(0, differingNodes);
        Assert.Equal(0, differingLinks);
    }
}
