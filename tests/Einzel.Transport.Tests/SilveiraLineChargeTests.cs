using Einzel.Fields.Solved;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The published line-charge estimate for a trapped-ion-mobility analyser's stored population,
/// and what the electrodes it neglects take out of it.
/// </summary>
/// <remarks>
/// <para>
/// Silveira and colleagues estimate the field a stored population exerts on itself by treating
/// it as a uniformly charged line in <em>free space</em>, explicitly neglecting the electrodes,
/// and quote the radial field 2 mm from the axis of a million charges on a 23 mm line as
/// roughly 0.6 V/cm - about two orders of magnitude below the analytical field, which is the
/// basis of their conclusion that 10^6 to 10^7 charges can be stored without harming
/// performance.
/// </para>
/// <para>
/// <b>I expected the real bore to screen that, and it does not.</b> The prediction was that a
/// grounded wall 4 mm from the axis would pull the field at 2 mm well below the free-space
/// value, making their estimate conservative by a computable factor. It came out at 62.28 V/m
/// against their 61.68 - the same to one per cent - and moving the wall out to 32 mm changed
/// nothing beyond grid noise.
/// </para>
/// <para>
/// <b>Gauss's law is why, and it means their neglect of the electrodes is not an
/// approximation for this quantity at all.</b> The induced charge on an axisymmetric bore
/// sits at larger radius than the point the field is read at, and a cylindrical shell of
/// charge contributes exactly nothing to the field inside itself. So the wall cannot alter
/// the radial field at any smaller radius, however close it is; for a finite line it is only
/// nearly axisymmetric in its induced distribution, which is the residual per cent. What the
/// wall does set is the <em>potential</em>: at the very same 2 mm, moving the wall from 4 mm
/// out to 32 mm trebles it while leaving the field alone. That pair is what these tests
/// assert, because the field alone would leave it unclear whether the bore had entered the
/// solve at all - a boundary condition that changed nothing would look identical.
/// </para>
/// </remarks>
public sealed class SilveiraLineChargeTests(ITestOutputHelper output)
{
    private const double Charge = 1.602176634e-19;
    private const double Epsilon0 = DensitySelfField.VacuumPermittivitySi;

    /// <summary>The published configuration: a million charges over 23 mm.</summary>
    private const double Ions = 1.0e6;

    private const double LineLengthM = 23.0e-3;

    /// <summary>Where they quote the field, on the bisector of the line.</summary>
    private const double AtRadiusM = 2.0e-3;

    private static readonly DriftDiffusion.DomainEdges Open = new(
        Escape.Absorbing, Escape.Reflecting, Escape.Absorbing, Escape.Absorbing);

    /// <summary>
    /// Their Eq. 4: the radial field on the bisector of a uniformly charged line of total
    /// charge q and length l, in free space.
    /// </summary>
    private static double PublishedField(double chargeSi, double lengthM, double radiusM) =>
        chargeSi / (2.0 * Math.PI * Epsilon0 * radiusM)
        / Math.Sqrt((lengthM * lengthM) + (4.0 * radiusM * radiusM));

    /// <summary>
    /// The potential of the same line on its bisector, referenced to infinity: the integral of
    /// the field they quote, and not a quantity this engine had any hand in.
    /// </summary>
    /// <param name="chargeSi">Total charge on the line, in coulombs.</param>
    /// <param name="lengthM">The line's length.</param>
    /// <param name="radiusM">Where to read it.</param>
    /// <returns>Volts.</returns>
    private static double FreeSpacePotential(double chargeSi, double lengthM, double radiusM)
    {
        var half = 0.5 * lengthM;
        var slant = Math.Sqrt((half * half) + (radiusM * radiusM));

        return chargeSi / lengthM / (4.0 * Math.PI * Epsilon0)
            * Math.Log((slant + half) / (slant - half));
    }

    /// <summary>
    /// A thin uniform column of the given total charge, centred in the grid, one cell wide so
    /// it stands for a line.
    /// </summary>
    private static DensityField Line(Grid2D grid, double lengthM)
    {
        var density = new DensityField(grid, cylindrical: true);
        var centre = 0.5 * (grid.OriginX + grid.X(grid.CountX - 1));

        // The innermost ring only, so the column's radius is one cell and small against the
        // two millimetres the field is read at.
        var occupied = 0.0;

        for (var i = 0; i < grid.CountX; i++)
        {
            if (Math.Abs(grid.X(i) - centre) <= 0.5 * lengthM)
            {
                density[i, 0] = 1.0;
                occupied += density.CellVolume(0);
            }
        }

        // Normalised so the grid holds exactly the declared ion count, taken from the cell
        // volumes rather than from a formula: a column whose ends land between nodes must not
        // make the two sides of the comparison disagree about how much charge there is.
        for (var i = 0; i < grid.CountX; i++)
        {
            if (density[i, 0] > 0.0)
            {
                density[i, 0] = Ions / occupied;
            }
        }

        return density;
    }

    /// <summary>The node on the line's bisector nearest a radius.</summary>
    private static (int I, int J) Bisector(Grid2D grid, double radiusM)
    {
        var centre = 0.5 * (grid.OriginX + grid.X(grid.CountX - 1));

        return (Enumerable.Range(0, grid.CountX).MinBy(k => Math.Abs(grid.X(k) - centre)),
                Enumerable.Range(1, grid.CountY - 2).MinBy(k => Math.Abs(grid.Y(k) - radiusM)));
    }

    /// <summary>The radial self-field at a radius, on the bisector, by central difference.</summary>
    private static double RadialField(Grid2D grid, DensitySelfField self, double radiusM)
    {
        var (i, j) = Bisector(grid, radiusM);

        return -(self.Potential[((j + 1) * grid.CountX) + i] - self.Potential[((j - 1) * grid.CountX) + i])
            / (2.0 * grid.SpacingY);
    }

    /// <summary>
    /// The self-potential at a radius, on the bisector - read at the same node the field is,
    /// so the two say something about one point rather than about two.
    /// </summary>
    /// <remarks>
    /// The peak on the axis would not do: it sits in the innermost ring, whose radius is half
    /// a cell rather than a dimension of the problem, so it carries the discretisation of a
    /// line that has been given a width. The node at 2 mm is resolved by sixteen cells, and is
    /// the radius the paper quotes.
    /// </remarks>
    private static double PotentialAt(Grid2D grid, DensitySelfField self, double radiusM)
    {
        var (i, j) = Bisector(grid, radiusM);

        return self.Potential[(j * grid.CountX) + i];
    }

    /// <summary>
    /// In the real bore the solver reproduces their published free-space estimate.
    /// </summary>
    [Fact]
    public void TheSolverReproducesThePublishedEstimate()
    {
        var published = PublishedField(Ions * Charge, LineLengthM, AtRadiusM);

        output.WriteLine($"their Eq. 4 for {Ions:G3} charges on {LineLengthM * 1e3:F0} mm, at {AtRadiusM * 1e3:F0} mm: "
            + $"{published:F2} V/m = {published / 100.0:F3} V/cm (they quote 'roughly 0.6 V/cm')");

        // Their own quoted value, so the closed form here is not a restatement of the solver.
        Assert.Equal(0.6, published / 100.0, 0.05);

        var grid = Grid2D.OverBox(0.0, 0.0, 32.0e-3, 4.0e-3, 256, 32);
        var self = new DensitySelfField(grid, cylindrical: true, AbsorbingCells.None, Open, Charge);

        Assert.True(self.Refresh(Line(grid, LineLengthM)));

        var measured = RadialField(grid, self, AtRadiusM);

        output.WriteLine($"solved in the 4 mm bore: {measured:F2} V/m = {measured / 100.0:F3} V/cm, "
            + $"{measured / published:F3} of theirs");
        output.WriteLine($"peak self-potential on the axis: {self.PeakVolts:G4} V");

        Assert.Equal(published, measured, published * 0.03);
    }

    /// <summary>
    /// And the bore does not screen it: the field at 2 mm is the same wherever the wall is,
    /// while the potential at that very same point trebles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves together are the assertion, and they are read at one node. A field that
    /// did not move could mean the wall never entered the solve; a potential that trebles at
    /// the same point says it did, and that what a grounded wall sets is the reference rather
    /// than the enclosed-charge field. This is Gauss's law for a cylindrical shell, exact for
    /// an infinite line and holding here to the extent the induced charge on a finite one is
    /// axisymmetric. The predicted ratio is the difference of the free-space potential at the
    /// two wall radii, which is nothing like eightfold because the line is 23 mm long and a
    /// wall at 32 mm is already past its end.
    /// </para>
    /// <para>
    /// <b>Which boundary condition applies is not a free choice, and swapping it shows why.</b>
    /// Made no-flux instead of earthed - a mirror rather than a wall - the same bore takes 31
    /// per cent out of the field at 2 mm, because a mirror images the line charge and an image
    /// is not a shell. So the absence of screening is a property of a conductor held at a
    /// fixed potential, which is what a real bore is, rather than an artefact of a boundary
    /// picked for convenience. Both mutations - that one, and solving the same problem as a
    /// plane - fail both tests in this class.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGroundedBoreDoesNotScreenTheRadialFieldInsideIt()
    {
        var published = PublishedField(Ions * Charge, LineLengthM, AtRadiusM);
        var measured = new List<(double WallMm, double Field, double Potential)>();

        foreach (var wallMm in new[] { 4.0, 8.0, 16.0, 32.0 })
        {
            // Square cells at every wall position, so nothing here turns on the grid coarsening
            // as the domain grows. At a fixed interval count it did, and gave a non-monotone
            // approach that read as physics.
            var cells = (int)Math.Round(wallMm / 0.125);
            var grid = Grid2D.OverBox(0.0, 0.0, 32.0e-3, wallMm * 1.0e-3, 256, cells);
            var self = new DensitySelfField(grid, cylindrical: true, AbsorbingCells.None, Open, Charge);

            Assert.True(self.Refresh(Line(grid, LineLengthM)));

            measured.Add((wallMm,
                RadialField(grid, self, AtRadiusM),
                PotentialAt(grid, self, AtRadiusM)));
        }

        foreach (var (wallMm, field, potential) in measured)
        {
            output.WriteLine($"  wall at {wallMm:F0} mm: field at 2 mm {field:F2} V/m "
                + $"({field / published:F3} of theirs), potential at 2 mm {potential:F4} V");
        }

        var least = measured.Min(m => m.Field);
        var most = measured.Max(m => m.Field);
        var fieldSpread = (most - least) / least;
        var potentialRatio = measured[^1].Potential / measured[0].Potential;

        // What moving the wall is worth to the potential, from the closed form: the free-space
        // potential falls from the near wall's radius to the far one's, and that difference is
        // what grounding the far wall rather than the near one adds at 2 mm.
        var atRadius = FreeSpacePotential(Ions * Charge, LineLengthM, AtRadiusM);
        var predicted =
            (atRadius - FreeSpacePotential(Ions * Charge, LineLengthM, 32.0e-3))
            / (atRadius - FreeSpacePotential(Ions * Charge, LineLengthM, 4.0e-3));

        output.WriteLine($"across an eightfold range of wall radius: the field at 2 mm moves {fieldSpread:P1}, "
            + $"the potential at the same point by {potentialRatio:F2}x against {predicted:F2}x predicted");

        Assert.True(
            fieldSpread < 0.05,
            $"the field at 2 mm moved {fieldSpread:P1} as the wall went from 4 to 32 mm, which a shell of "
            + "induced charge outside that radius cannot do");

        // The control, at the same node: the wall is in the solve, and it sets the potential.
        Assert.Equal(predicted, potentialRatio, 0.4);

        Assert.True(
            potentialRatio > 2.0,
            $"the potential at 2 mm moved only {potentialRatio:F2}x, so the boundary may not be entering the "
            + "solve at all and the field's flatness would then say nothing");
    }
}
