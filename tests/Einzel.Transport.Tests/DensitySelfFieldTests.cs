using Einzel.Fields.Solved;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The potential an ion density makes for itself, against the closed forms for a charged
/// column and a charged slab.
/// </summary>
/// <remarks>
/// <para>
/// Space charge for the diffusive mode. Both checks are configurations Gauss's law solves
/// exactly, so what is compared is a solved field against arithmetic this code had no part
/// in - and the charge on each side is the <em>discrete</em> charge the grid actually holds,
/// summed from the density, so the only error left is the discretisation of the Laplacian
/// rather than of the distribution.
/// </para>
/// <para>
/// The cylindrical case is the one the instruments need: a column of ions on the axis of a
/// grounded bore is what a mobility analyser holds, and the wall a millimetre away is what
/// screens it. The Cartesian slab is the control, because the cylindrical operator carries a
/// radial weighting that the plane one does not and a wrong weighting converges contentedly to
/// the wrong answer.
/// </para>
/// </remarks>
public sealed class DensitySelfFieldTests(ITestOutputHelper output)
{
    private const double Charge = 1.602176634e-19;
    private const double Epsilon0 = DensitySelfField.VacuumPermittivitySi;

    /// <summary>Reflecting along the axis, so the solve has no axial variation to resolve.</summary>
    private static readonly DriftDiffusion.DomainEdges Tube = new(
        Escape.Absorbing, Escape.Reflecting, Escape.Absorbing, Escape.Absorbing);

    private static readonly DriftDiffusion.DomainEdges Slab = new(
        Escape.Absorbing, Escape.Absorbing, Escape.Absorbing, Escape.Absorbing);

    /// <summary>
    /// A uniform column of radius a on the axis of a grounded bore of radius b:
    /// <c>phi(0) = (lambda / 4 pi eps0) (1 + 2 ln(b/a))</c>.
    /// </summary>
    [Fact]
    public void AChargedColumnInAGroundedBoreMatchesGauss()
    {
        const double B = 4.0e-3;                       // bore radius
        const double A = 1.0e-3;                       // column radius, on a node by construction
        const double NumberDensity = 1.0e13;           // ions per cubic metre

        var grid = Grid2D.OverBox(0.0, 0.0, 32.0e-3, B, 256, 32);   // square cells, 1.25e-4 m
        var density = new DensityField(grid, cylindrical: true);

        for (var j = 0; j < grid.CountY; j++)
        {
            if (grid.Y(j) >= A)
            {
                continue;
            }

            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] = NumberDensity;
            }
        }

        // The charge the grid actually holds, per metre of column: one axial slice of ring
        // volumes, divided by the slice's own thickness. Taken from the density rather than
        // from pi a squared, so a node sitting on the edge of the column cannot make the two
        // sides of the comparison disagree about how much charge there is.
        var perLength = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            perLength += Charge * density[0, j] * density.CellVolume(j) / grid.SpacingX;
        }

        var self = new DensitySelfField(grid, cylindrical: true, AbsorbingCells.None, Tube, Charge);

        Assert.True(self.Refresh(density));
        output.WriteLine($"solve: converged {self.Report!.Converged}, {self.Report.Cycles} cycles, residual {self.Report.FinalResidual:E3} from {self.Report.InitialResidual:E3}, factor {self.Report.ConvergenceFactor:F4}");

        // Read on the axis, half way along, where the reflecting ends cannot reach.
        var middle = grid.CountX / 2;
        var onAxis = self.Potential[(0 * grid.CountX) + middle];
        var effectiveRadius = Math.Sqrt(perLength / (Math.PI * Charge * NumberDensity));
        var exact = perLength / (4.0 * Math.PI * Epsilon0) * (1.0 + (2.0 * Math.Log(B / effectiveRadius)));

        output.WriteLine($"charge per metre {perLength:E4} C/m over an effective radius of {effectiveRadius * 1e3:F4} mm");
        output.WriteLine($"on the axis: solved {onAxis:F5} V, Gauss {exact:F5} V ({(onAxis - exact) / exact:+0.00 %;-0.00 %})");

        Assert.Equal(exact, onAxis, Math.Abs(exact) * 0.02);

        // And the profile outside the column, which is a logarithm and is what a wrong radial
        // weighting gets wrong: a plane operator would give a straight line here.
        foreach (var radius in new[] { 1.5e-3, 2.0e-3, 3.0e-3 })
        {
            var j = (int)Math.Round(radius / grid.SpacingY);
            var solved = self.Potential[(j * grid.CountX) + middle];
            var outside = perLength / (2.0 * Math.PI * Epsilon0) * Math.Log(B / grid.Y(j));

            output.WriteLine($"  r = {grid.Y(j) * 1e3:F3} mm: solved {solved:F5} V, Gauss {outside:F5} V");

            Assert.Equal(outside, solved, Math.Abs(outside) * 0.03);
        }

        // The wall screens: the self-potential vanishes on it, which is the boundary condition
        // that makes any of the above true.
        Assert.Equal(0.0, self.Potential[((grid.CountY - 1) * grid.CountX) + middle], 1e-12);
    }

    /// <summary>
    /// The Cartesian control: a slab of half-width a between grounded planes at +/- b, where
    /// <c>phi(0) = rho a (b - a) / eps0 + rho a^2 / (2 eps0)</c>.
    /// </summary>
    [Fact]
    public void AChargedSlabBetweenGroundedPlanesMatchesGauss()
    {
        const double B = 4.0e-3;
        const double A = 1.0e-3;
        const double NumberDensity = 1.0e13;

        var grid = Grid2D.OverBox(0.0, -B, 32.0e-3, B, 256, 64);   // square cells
        var density = new DensityField(grid, cylindrical: false);

        for (var j = 0; j < grid.CountY; j++)
        {
            if (Math.Abs(grid.Y(j)) >= A)
            {
                continue;
            }

            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] = NumberDensity;
            }
        }

        // The discrete half-width, from the charge the grid holds in one column.
        var sheet = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            sheet += density[0, j] * grid.SpacingY;
        }

        var half = sheet / (2.0 * NumberDensity);
        var rho = Charge * NumberDensity;
        var exact = (rho * half * (B - half) / Epsilon0) + (rho * half * half / (2.0 * Epsilon0));

        var self = new DensitySelfField(grid, cylindrical: false, AbsorbingCells.None, Slab, Charge);

        Assert.True(self.Refresh(density));
        output.WriteLine($"solve: converged {self.Report!.Converged}, {self.Report.Cycles} cycles, residual {self.Report.FinalResidual:E3} from {self.Report.InitialResidual:E3}, factor {self.Report.ConvergenceFactor:F4}");

        var middle = grid.CountX / 2;
        var centre = grid.CountY / 2;
        var atCentre = self.Potential[(centre * grid.CountX) + middle];

        output.WriteLine($"discrete half-width {half * 1e3:F4} mm against a declared {A * 1e3:F1} mm");
        output.WriteLine($"at the mid-plane: solved {atCentre:F5} V, Gauss {exact:F5} V ({(atCentre - exact) / exact:+0.00 %;-0.00 %})");

        Assert.Equal(exact, atCentre, Math.Abs(exact) * 0.02);

        // Outside the slab the potential is a straight line - the field is constant there -
        // which is the shape the plane operator must give and the cylindrical one must not.
        var quarter = self.Potential[(((centre + grid.CountY) / 2) * grid.CountX) + middle];
        var slope = perUnit(grid, self, middle, (centre + grid.CountY) / 2);

        output.WriteLine($"outside the slab: {quarter:F5} V, local field {slope:F2} V/m against rho a / eps0 = {rho * half / Epsilon0:F2} V/m");

        Assert.Equal(rho * half / Epsilon0, Math.Abs(slope), Math.Abs(rho * half / Epsilon0) * 0.05);

        static double perUnit(Grid2D grid, DensitySelfField self, int i, int j) =>
            (self.Potential[((j + 1) * grid.CountX) + i] - self.Potential[((j - 1) * grid.CountX) + i])
            / (2.0 * grid.SpacingY);
    }

    /// <summary>
    /// A conductor inside the region screens: the self-potential vanishes on it, and a cloud
    /// behind it does not reach past.
    /// </summary>
    [Fact]
    public void AConductorInsideTheRegionScreens()
    {
        var grid = Grid2D.OverBox(0.0, 0.0, 20.0e-3, 4.0e-3, 64, 32);
        var density = new DensityField(grid, cylindrical: true);

        // A wall across the tube half way along, as an absorber - which is how an electrode
        // reaches the density solve.
        var owner = new int[grid.CountX * grid.CountY];
        Array.Fill(owner, -1);

        for (var j = 0; j < grid.CountY; j++)
        {
            owner[(j * grid.CountX) + (grid.CountX / 2)] = 0;
        }

        var absorbers = new AbsorbingCells(owner, ["wall"]);

        for (var j = 0; j < grid.CountY / 4; j++)
        {
            for (var i = 2; i < grid.CountX / 4; i++)
            {
                density[i, j] = 1.0e13;
            }
        }

        var self = new DensitySelfField(grid, cylindrical: true, absorbers, Tube, Charge);
        Assert.True(self.Refresh(density));

        var wall = grid.CountX / 2;
        var behind = self.Potential[(0 * grid.CountX) + (grid.CountX / 8)];
        var onWall = self.Potential[(0 * grid.CountX) + wall];
        var beyond = self.Potential[(0 * grid.CountX) + (3 * grid.CountX / 4)];

        output.WriteLine($"on the axis: {behind:E4} V behind the wall, {onWall:E4} V on it, {beyond:E4} V past it");

        Assert.True(behind > 1e-4, $"the cloud makes no potential at all: {behind:E3} V");
        Assert.Equal(0.0, onWall, 1e-12);
        Assert.True(Math.Abs(beyond) < 1e-6 * behind, $"{beyond:E3} V leaked past a grounded wall");
    }

    /// <summary>
    /// A held density is solved once; a moving one is solved again, and only when it has
    /// moved enough.
    /// </summary>
    [Fact]
    public void TheSolveIsRepeatedOnlyWhenTheDensityMoves()
    {
        var grid = Grid2D.OverBox(0.0, 0.0, 20.0e-3, 4.0e-3, 32, 16);
        var density = new DensityField(grid, cylindrical: true);

        for (var i = 4; i < 12; i++)
        {
            density[i, 0] = 1.0e13;
        }

        var self = new DensitySelfField(grid, cylindrical: true, AbsorbingCells.None, Tube, Charge, refreshTolerance: 0.05);

        Assert.True(self.Refresh(density));
        Assert.Equal(1, self.Solves);

        // Unchanged: nothing to solve.
        Assert.False(self.Refresh(density));
        Assert.Equal(1, self.Solves);

        // A one per cent change is below the tolerance and is carried.
        density[6, 0] *= 1.01;
        Assert.False(self.Refresh(density));

        // Moving a whole cell's worth is not.
        density[20, 0] = 1.0e13;
        Assert.True(self.Refresh(density));
        Assert.Equal(2, self.Solves);

        output.WriteLine($"{self.Solves} solves, peak {self.PeakVolts:E4} V, charge {self.ChargeSi:E4} C");
    }
}
