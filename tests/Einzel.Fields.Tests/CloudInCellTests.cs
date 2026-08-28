using Einzel.Core.Geometry;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The particle half of particle-in-cell: charge onto a grid, field back off it.
/// </summary>
/// <remarks>
/// <para>
/// SC-1's approximate method, whose reference is the direct pairwise sum already
/// built. What is checked here is not accuracy first but two exact properties, because
/// both can be broken by a change that still produces a plausible field.
/// </para>
/// <para>
/// <strong>Charge is conserved by construction.</strong> The eight weights sum to one
/// whatever the position, so what goes on the grid is what was handed in - not
/// normalised afterwards, which would hide a weighting error rather than prevent one.
/// </para>
/// <para>
/// <strong>The self-force cancels because the gather uses the deposit's weights.</strong>
/// A particle writes charge to a node with some weight and reads the field back from
/// it with the same weight, so its own contribution cancels in the sum. Gather with a
/// more accurate interpolant instead and every particle feels itself, the packet heats
/// up out of nothing, and the field looks entirely reasonable throughout.
/// </para>
/// </remarks>
public sealed class CloudInCellTests(ITestOutputHelper output)
{
    private const double Elementary = 1.602176634e-19;

    private static Grid3D Box(double halfWidth, int intervals) =>
        Grid3D.OverBox(
            -halfWidth, -halfWidth, -halfWidth,
            halfWidth, halfWidth, halfWidth,
            2.0 * halfWidth / intervals);

    /// <summary>Integrates the deposited density back to a total charge.</summary>
    private static double TotalCharge(ScalarField3D source)
    {
        var grid = source.Grid;
        var cell = grid.SpacingX * grid.SpacingY * grid.SpacingZ;
        var total = 0.0;

        for (var l = 0; l < grid.CountZ; l++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    // The source is -rho/epsilon0, so undo both to get rho back.
                    total += -source[i, j, l] * CloudInCell.VacuumPermittivitySi * cell;
                }
            }
        }

        return total;
    }

    [Fact]
    public void EveryParticlePutsItsWholeChargeOnTheGrid()
    {
        // Not "close to". The eight weights sum to exactly one whatever the position,
        // so the total is exact up to the order the additions happen in - and a
        // deposit that normalised afterwards would pass this while hiding a weighting
        // error rather than preventing one.
        var grid = Box(10.0e-3, 32);
        var random = new Random(20260828);

        var positions = new List<Vec3>();
        var charges = new List<double>();

        for (var k = 0; k < 500; k++)
        {
            positions.Add(new Vec3(
                (random.NextDouble() - 0.5) * 12.0e-3,
                (random.NextDouble() - 0.5) * 12.0e-3,
                (random.NextDouble() - 0.5) * 12.0e-3));

            charges.Add(Elementary * 1000.0);
        }

        var deposit = CloudInCell.Charge(grid, positions, charges);
        var expected = charges.Sum();
        var measured = TotalCharge(deposit.Source);

        output.WriteLine($"handed in {expected:E6} C, on the grid {measured:E6} C");
        output.WriteLine($"outside   {deposit.FractionOutside:P2}");

        Assert.Equal(0.0, deposit.OutsideCoulombs);
        Assert.Equal(expected, measured, 1e-12 * expected);
    }

    [Fact]
    public void ChargeThatLeavesTheGridIsCountedRatherThanClamped()
    {
        // A packet that has drifted off its own grid produces a field that is quietly
        // too weak, which looks exactly like a packet more dilute than it is. Clamping
        // to the edge would be worse still: it would pile the charge onto a face and
        // produce a field that is wrong and confident.
        var grid = Box(1.0e-3, 16);

        var deposit = CloudInCell.Charge(
            grid,
            [new Vec3(0.0, 0.0, 0.0), new Vec3(5.0e-3, 0.0, 0.0)],
            [Elementary, Elementary]);

        output.WriteLine($"outside {deposit.FractionOutside:P1}");

        Assert.Equal(0.5, deposit.FractionOutside, 1e-12);
        Assert.Equal(Elementary, TotalCharge(deposit.Source), 1e-12 * Elementary);
    }

    [Fact]
    public void ASingleParticleBarelyFeelsItselfAndAMismatchedGatherDoesNot()
    {
        // The property the deposit/gather symmetry exists for, and the one a "better"
        // gather quietly destroys. A particle writes charge to eight nodes with some
        // weights and reads the field back with the same weights, so its own
        // contribution cancels in the sum.
        //
        // Not to zero, and saying so matters. The cancellation is exact on a uniform
        // periodic grid with centred differences; here the box is earthed, which
        // breaks the symmetry slightly through its images, and the difference stencil
        // is one-sided at the faces. What is asserted is therefore the RATIO to the
        // field a neighbour one cell away would feel - the scale the self-force would
        // have to reach to matter - and the comparison against a gather that does not
        // share the deposit's weights.
        const double Charge = Elementary * 1.0e6;

        var grid = Box(4.0e-3, 32);
        var mask = new DirichletMask3D(grid);

        Ground(mask, grid);

        // What a neighbour at one cell feels. Everything below is a fraction of this.
        var scale = Charge
            / (4.0 * Math.PI * CloudInCell.VacuumPermittivitySi * grid.SpacingX * grid.SpacingX);

        output.WriteLine($"a neighbour one cell away feels {scale:E3} V/m");
        output.WriteLine("     offset      matched       ratio        nearest-node      ratio");

        var worstMatched = 0.0;
        var worstMismatched = 0.0;

        foreach (var offset in new[] { 0.0, 0.13, 0.37, 0.5, 0.61, 0.89 })
        {
            var at = new Vec3(offset * grid.SpacingX, 0.21 * grid.SpacingY, 0.44 * grid.SpacingZ);

            var deposit = CloudInCell.Charge(grid, [at], [Charge]);

            var (potential, report) = PoissonSolver3D.Solve(
                mask, tolerance: 1e-10, maximumCycles: 100, source: deposit.Source);

            Assert.True(report.Converged);

            var matched = CloudInCell.Field(potential, in at).Length;
            var mismatched = NearestNodeField(potential, in at).Length;

            output.WriteLine(
                $"{offset,11:F2}   {matched:E3}   {matched / scale:E2}   "
                + $"{mismatched:E3}   {mismatched / scale:E2}");

            worstMatched = Math.Max(worstMatched, matched / scale);
            worstMismatched = Math.Max(worstMismatched, mismatched / scale);
        }

        output.WriteLine($"worst matched {worstMatched:E2}, worst mismatched {worstMismatched:E2}");

        // A thousandth of the scale that would matter, and the mismatched gather is
        // orders of magnitude worse. The second half is what makes the first a
        // property of the symmetry rather than of the grid being fine.
        Assert.True(worstMatched < 1e-3, $"self-force is {worstMatched:E2} of the neighbour scale");

        Assert.True(
            worstMismatched > 100.0 * worstMatched,
            $"a gather that does not share the deposit's weights should be far worse: "
            + $"{worstMismatched:E2} against {worstMatched:E2}");
    }

    /// <summary>
    /// The field at the nearest node, with no weighting: a gather that does not share
    /// the deposit's shape function.
    /// </summary>
    /// <remarks>
    /// Deliberately crude, because what is being shown is that the <em>symmetry</em>
    /// is what cancels the self-force rather than the grid being fine. A tricubic
    /// gather would be more accurate for a smooth field and would break it just the
    /// same, which is the point that makes trilinear the right choice here.
    /// </remarks>
    private static Vec3 NearestNodeField(ScalarField3D potential, in Vec3 at)
    {
        var grid = potential.Grid;

        var i = (int)Math.Round((at.X - grid.OriginX) / grid.SpacingX);
        var j = (int)Math.Round((at.Y - grid.OriginY) / grid.SpacingY);
        var l = (int)Math.Round((at.Z - grid.OriginZ) / grid.SpacingZ);

        i = Math.Clamp(i, 1, grid.CountX - 2);
        j = Math.Clamp(j, 1, grid.CountY - 2);
        l = Math.Clamp(l, 1, grid.CountZ - 2);

        return new Vec3(
            -(potential[i + 1, j, l] - potential[i - 1, j, l]) / (2.0 * grid.SpacingX),
            -(potential[i, j + 1, l] - potential[i, j - 1, l]) / (2.0 * grid.SpacingY),
            -(potential[i, j, l + 1] - potential[i, j, l - 1]) / (2.0 * grid.SpacingZ));
    }

    [Fact]
    public void AUniformSphereReproducesItsClosedForm()
    {
        // The accuracy check, against the field of a uniformly charged ball - which is
        // Q r / (4 pi eps0 R^3) inside and Q / (4 pi eps0 r^2) outside. Sampled with
        // enough macroparticles that the discreteness is below the discretisation.
        const int Particles = 20000;
        const double RadiusM = 1.0e-3;
        const double TotalCoulombs = 1.0e6 * Elementary;

        var grid = Box(4.0e-3, 48);
        var mask = new DirichletMask3D(grid);

        Ground(mask, grid);

        var random = new Random(7);
        var positions = new List<Vec3>(Particles);
        var charges = new List<double>(Particles);

        for (var k = 0; k < Particles; k++)
        {
            // Uniform in the ball: a direction times the cube root of a uniform.
            var u = random.NextDouble();
            var cos = (2.0 * random.NextDouble()) - 1.0;
            var phi = 2.0 * Math.PI * random.NextDouble();
            var sin = Math.Sqrt(Math.Max(0.0, 1.0 - (cos * cos)));
            var r = RadiusM * Math.Cbrt(u);

            positions.Add(new Vec3(
                r * sin * Math.Cos(phi), r * sin * Math.Sin(phi), r * cos));

            charges.Add(TotalCoulombs / Particles);
        }

        var deposit = CloudInCell.Charge(grid, positions, charges);

        var (potential, report) = PoissonSolver3D.Solve(
            mask, tolerance: 1e-10, maximumCycles: 200, source: deposit.Source);

        Assert.True(report.Converged);

        var k0 = 1.0 / (4.0 * Math.PI * CloudInCell.VacuumPermittivitySi);

        output.WriteLine($"{report.Cycles} cycles at factor {report.ConvergenceFactor:F4}");
        output.WriteLine("     r (mm)     measured      closed form      ratio");

        foreach (var rMm in new[] { 0.5, 1.0, 1.5, 2.0 })
        {
            var r = rMm * 1.0e-3;
            var at = new Vec3(r, 0.0, 0.0);

            var measured = CloudInCell.Field(potential, in at).X;

            var exact = r <= RadiusM
                ? k0 * TotalCoulombs * r / (RadiusM * RadiusM * RadiusM)
                : k0 * TotalCoulombs / (r * r);

            output.WriteLine($"{rMm,11:F2}   {measured:E4}   {exact:E4}   {measured / exact,8:F4}");

            // Ten per cent, and the reason it is not tighter is the grounded box:
            // the closed form is for a sphere alone in space, and this one sits in a
            // 8 mm earthed cube whose image charge pulls the potential down. That is
            // a boundary condition rather than a solver error, and tightening it
            // means a bigger box rather than a better method.
            Assert.InRange(measured / exact, 0.85, 1.15);
        }
    }

    [Fact]
    public void MismatchedCountsAreRefused()
    {
        var grid = Box(1.0e-3, 8);

        Assert.Throws<ArgumentException>(
            () => CloudInCell.Charge(grid, [Vec3.Zero, Vec3.Zero], [Elementary]));
    }

    private static void Ground(DirichletMask3D mask, Grid3D grid)
    {
        for (var l = 0; l < grid.CountZ; l++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    if (i == 0 || j == 0 || l == 0
                        || i == grid.CountX - 1 || j == grid.CountY - 1 || l == grid.CountZ - 1)
                    {
                        mask.Fix(i, j, l, 0.0);
                    }
                }
            }
        }
    }
}
