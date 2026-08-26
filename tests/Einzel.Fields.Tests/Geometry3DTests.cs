using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// Three-dimensional geometry: cut cells at a curved surface, and the interpolant
/// that samples the result.
/// </summary>
/// <remarks>
/// <para>
/// The sphere is the three-dimensional analogue of the coaxial check that made cut
/// cells provable in the plane. Between concentric spheres the potential goes as
/// 1/r, and a rasterised sphere is a staircase - so agreeing with the closed form,
/// at second order, is exactly what the sub-cell boundary is responsible for.
/// </para>
/// <para>
/// The interpolant is checked separately and against a <em>sampled</em> exact field
/// rather than a solved one. A solved field carries its own discretisation error,
/// and on a coarse grid that error is larger than the interpolation error it is
/// supposed to be a backdrop for - which once made bicubic look sixty times worse
/// than bilinear.
/// </para>
/// </remarks>
public sealed class Geometry3DTests(ITestOutputHelper output)
{
    private const double Applied = 100.0;

    /// <summary>A charged sphere at the centre of a grounded cubic box.</summary>
    private static Geometry3D Sphere(double radius, double halfWidth, int cells) => new(
        -halfWidth, -halfWidth, -halfWidth,
        halfWidth, halfWidth, halfWidth,
        2.0 * halfWidth / cells,
        [
            new CompiledElectrode3D
            {
                Name = "bead",
                Shape = Electrode3DShape.Sphere,
                CentreX = 0.0,
                CentreY = 0.0,
                CentreZ = 0.0,
                Radius = radius,
                Potential = Applied,
            },
        ],
        Tolerance: 1e-8);

    /// <summary>The exact potential between a charged sphere and a grounded one.</summary>
    private static double Exact(double r, double inner, double outer) =>
        Applied * inner * ((1.0 / r) - (1.0 / outer)) / (1.0 - (inner / outer));

    /// <summary>
    /// Worst departure from the 1/r law, sampled close to the bead.
    /// </summary>
    /// <remarks>
    /// Close, because the grounded wall is a cube and the closed form is for a
    /// sphere. Near the bead that difference is small and the discretisation error
    /// dominates, which is what a convergence study needs; out by the wall it is the
    /// other way round and the study would measure the shape of the box.
    /// </remarks>
    private static double WorstNearBead(double radius, double halfWidth, int cells, out SolveReport report)
    {
        (var field, report) = GeometryBuilder3D.Build(Sphere(radius, halfWidth, cells));

        var worst = 0.0;

        for (var s = 0; s <= 6; s++)
        {
            var r = radius * (1.15 + (0.35 * s / 6.0));
            var point = new Vec3(r, 0.0, 0.0);

            worst = Math.Max(worst, Math.Abs(field.PotentialAt(in point) - Exact(r, radius, halfWidth)));
        }

        return worst;
    }

    [Fact]
    public void ACurvedConductorIsResolvedBelowOneCell()
    {
        // A rasterised sphere is a staircase, and a staircase is not a sphere at any
        // mesh: its error does not fall to second order, it wobbles at the level of
        // half a cell. Cut cells put the surface where it actually is.
        const double Radius = 0.003;
        const double HalfWidth = 0.012;

        var worst = WorstNearBead(Radius, HalfWidth, 32, out var report);

        output.WriteLine($"{report.Cycles} cycles at factor {report.ConvergenceFactor:F4}");
        output.WriteLine($"worst departure from the 1/r law {worst:F4} V of {Applied:F0} applied, near the bead");

        Assert.True(report.Converged, "the solve did not converge");
        Assert.True(worst < 3.0, $"departed from the closed form by {worst:F3} V");
    }

    [Fact]
    public void RefiningTheSphereReducesTheError()
    {
        // Powers of two, because the grid rounds its interval count up to one -
        // asking for 24 and asking for 32 give the same mesh, and a study that did
        // not know that would report an observed order of exactly zero and look like
        // a solver that had stopped converging.
        const double Radius = 0.003;
        const double HalfWidth = 0.012;

        output.WriteLine("intervals    worst error      ratio");

        var errors = new List<double>();

        foreach (var cells in new[] { 16, 32 })
        {
            var worst = WorstNearBead(Radius, HalfWidth, cells, out _);
            errors.Add(worst);

            var ratio = errors.Count > 1 ? errors[^2] / worst : double.NaN;

            output.WriteLine(
                $"{cells,9}    {worst,11:E4}    {(double.IsNaN(ratio) ? string.Empty : ratio.ToString("F2")),8}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("a cubic wall against a spherical closed form leaves a floor, so this is a");
        output.WriteLine("reduction rather than a clean order; the operator's order is measured on the");
        output.WriteLine("harmonic functions in Solver3DTests, where there is no geometry mismatch.");

        Assert.True(errors[1] < 0.6 * errors[0], $"refining took {errors[0]:F3} V only to {errors[1]:F3} V");
    }

    [Fact]
    public void TheMaximumPrincipleHoldsAtTheNodes()
    {
        // A harmonic function attains its extremes on the boundary, so no *node*
        // may exceed the applied value. The cheapest exact detector of a diverged
        // solve, and the one that caught interior-electrode coarsening failing here.
        //
        // At the nodes, not through the interpolant: a cubic through a sharp step
        // overshoots by construction, and about a per cent of it near a conductor
        // surface is the interpolant behaving normally rather than the solve
        // misbehaving. Testing the interpolated value would confuse the two.
        var geometry = Sphere(0.003, 0.012, 24);
        var grid = GeometryBuilder3D.BuildGrid(geometry);
        var mask = GeometryBuilder3D.BuildMask(geometry, grid);

        var (potential, report) = PoissonSolver3D.Solve(
            mask, geometry.Tolerance, coarsen: coarse => GeometryBuilder3D.BuildMask(geometry, coarse));

        var peak = 0.0;

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    peak = Math.Max(peak, Math.Abs(potential[i, j, k]));
                }
            }
        }

        output.WriteLine($"{report.Cycles} cycles at factor {report.ConvergenceFactor:F4}");
        output.WriteLine($"peak nodal potential {peak:F6} V against {Applied:F0} applied");

        Assert.True(peak <= Applied * (1.0 + 1e-9), $"a node reached {peak:F6} V");
    }

    [Fact]
    public void TheInterpolantOvershootsALittleAtAConductorSurface()
    {
        // Stated and bounded rather than discovered later. A cubic through the step
        // from vacuum to a charged surface overshoots; it is a property of the
        // interpolant, it is about a per cent here, and it happens just outside the
        // metal where an ion is about to be absorbed anyway.
        var (field, _) = GeometryBuilder3D.Build(Sphere(0.003, 0.012, 24));

        var peak = 0.0;

        for (var s = -30; s <= 30; s++)
        {
            var point = new Vec3(0.0004 * s, 0.0, 0.0);
            peak = Math.Max(peak, field.PotentialAt(in point));
        }

        output.WriteLine($"peak interpolated potential {peak:F3} V against {Applied:F0} applied");

        Assert.InRange(peak, Applied, Applied * 1.05);
    }

    [Fact]
    public void TheInterpolantIsExactOnALinearField()
    {
        // Against a sampled exact field, not a solved one. A cubic interpolant
        // reproduces a linear function exactly everywhere including the boundary
        // cells - and only if the ghost node extrapolates rather than clamping,
        // which is the corner case that once cost 7.5 ppm of a flight time.
        var grid = Grid3D.OverBox(-0.01, -0.01, -0.01, 0.01, 0.01, 0.01, 0.02 / 16.0);
        var field = new ScalarField3D(grid);

        double Linear(double x, double y, double z) => (300.0 * x) - (120.0 * y) + (45.0 * z) + 7.0;

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    field[i, j, k] = Linear(grid.X(i), grid.Y(j), grid.Z(k));
                }
            }
        }

        var interpolant = new TricubicInterpolant(field);

        var worstValue = 0.0;
        var worstGradient = 0.0;

        // Including points inside the boundary cells, which is where a clamped
        // ghost would show up and an interior-only sweep would miss it.
        for (var s = 0; s <= 40; s++)
        {
            var t = -0.0099 + (0.0198 * s / 40.0);
            var u = 0.0099 - (0.0198 * s / 40.0);
            var v = -0.0099 + (0.0198 * ((s * 7) % 41) / 40.0);

            worstValue = Math.Max(worstValue, Math.Abs(interpolant.Value(t, u, v) - Linear(t, u, v)));

            interpolant.Gradient(t, u, v, out var gx, out var gy, out var gz);

            worstGradient = Math.Max(
                worstGradient,
                Math.Max(Math.Abs(gx - 300.0), Math.Max(Math.Abs(gy + 120.0), Math.Abs(gz - 45.0))));
        }

        output.WriteLine($"worst value error    {worstValue:E3} V");
        output.WriteLine($"worst gradient error {worstGradient:E3} V/m");

        Assert.True(worstValue < 1e-11, $"a linear field came out {worstValue:E3} wrong");
        Assert.True(worstGradient < 1e-7, $"the gradient of a linear field came out {worstGradient:E3} wrong");
    }

    [Fact]
    public void ACylinderIsSolidAllTheWayRoundAndOpenBeyondItsEnds()
    {
        // The primitive a rod is made of, and the one where an axis-aligned test
        // would pass on a shape that was wrong off-axis.
        var rod = new CompiledElectrode3D
        {
            Name = "rod",
            Shape = Electrode3DShape.Cylinder,
            Axis = CylinderAxis.Z,
            CentreX = 0.004,
            CentreY = 0.0,
            Radius = 0.002,
            Lower = -0.005,
            Upper = 0.005,
            Potential = Applied,
        };

        foreach (var degrees in new[] { 0.0, 40.0, 90.0, 175.0, 260.0, 330.0 })
        {
            var theta = degrees * Math.PI / 180.0;

            // On the axis of the rod, a millimetre in from its surface.
            var inside = (0.004 + (0.001 * Math.Cos(theta)), 0.001 * Math.Sin(theta));

            Assert.True(
                rod.Contains(inside.Item1, inside.Item2, 0.0),
                $"the rod has a gap at {degrees:F0} degrees");

            // And three millimetres out, which is outside a two-millimetre radius.
            var outside = (0.004 + (0.003 * Math.Cos(theta)), 0.003 * Math.Sin(theta));

            Assert.False(
                rod.Contains(outside.Item1, outside.Item2, 0.0),
                $"the rod extends too far at {degrees:F0} degrees");
        }

        // Capped: on the rod's own centreline but past its end.
        Assert.True(rod.Contains(0.004, 0.0, 0.004));
        Assert.False(rod.Contains(0.004, 0.0, 0.006));

        // And a segment along the axis enters it exactly at the cap.
        var entry = rod.FirstEntry(0.004, 0.0, 0.010, 0.004, 0.0, 0.000);

        Assert.NotNull(entry);
        Assert.Equal(0.5, entry!.Value, 9);

        output.WriteLine("solid at every azimuth, open past the caps, and entered exactly at one");
    }
}
