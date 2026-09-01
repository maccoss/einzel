using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A tilt a thousandth of a cell reaches the solved field, because the surface is a cut cell.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the claim the capability rests on.</b> An asymmetric-track analyser's mirrors
/// converge by a couple of hundred microns over a third of a metre — a sixth of a cell on
/// the mesh such a thing is solved at. A rasterised boundary would round that to nothing in
/// every cell and the two mirrors would come out exactly parallel: the model would validate,
/// solve, converge, and produce a drift that never reverses, which is the one behaviour the
/// device exists for.
/// </para>
/// <para>
/// Shortley–Weller stores how far a conductor is as a fraction of a cell, so it survives.
/// The same property that made FLD-1's shape derivative legible — where a rasterised
/// boundary made the discretisation a staircase function of electrode position, invisible
/// below one cell and percent-level above it — met again in a different place.
/// </para>
/// </remarks>
public sealed class TiltedElectrodeSolveTests(ITestOutputHelper output)
{
    private const double Cell = 0.0005;

    /// <summary>A quarter cell, which takes every conductor face off the node lattice.</summary>
    /// <remarks>
    /// <b>Deliberate, and the reason is a finding of its own</b> — see
    /// <see cref="AFaceExactlyOnTheNodeLatticeIsDegenerate"/>. Without it the plate faces
    /// land exactly on grid nodes, which is a degenerate configuration: introducing any tilt
    /// at all then produces a fixed offset worth about seventeen microns of convergence, on
    /// top of the proportional response. Real geometry is not lattice aligned, and a test
    /// that was would be measuring the alignment.
    /// </remarks>
    private const double Offset = 0.000125;

    /// <summary>Two plates across a gap, the upper one optionally tilted about y.</summary>
    /// <remarks>
    /// Tilting about y means the gap varies along x, so the field between the plates
    /// acquires an x dependence that a parallel pair does not have. That is the quantity
    /// measured below.
    /// </remarks>
    private static Geometry3D Plates(double halfTurns, double offset = Offset) => new(
        -0.006, -0.006, -0.005, 0.006, 0.006, 0.005, Cell,
        [
            new CompiledElectrode3D
            {
                Name = "lower",
                Shape = Electrode3DShape.Box,
                MinX = -0.004, MinY = -0.004, MinZ = -0.0035 + offset,
                MaxX = 0.004, MaxY = 0.004, MaxZ = -0.0025 + offset,
                Potential = 100.0,
            },
            new CompiledElectrode3D
            {
                Name = "upper",
                Shape = Electrode3DShape.Box,
                MinX = -0.004, MinY = -0.004, MinZ = 0.0025 + offset,
                MaxX = 0.004, MaxY = 0.004, MaxZ = 0.0035 + offset,
                Potential = 0.0,
                TiltAxis = CylinderAxis.Y,
                TiltHalfTurns = halfTurns,
            },
        ]);

    private static ScalarField3D Solve(Geometry3D geometry)
    {
        var grid = GeometryBuilder3D.BuildGrid(geometry);
        var mask = GeometryBuilder3D.BuildMask(geometry, grid);

        var (potential, _) = PoissonSolver3D.Solve(
            mask, geometry.Tolerance, maximumCycles: 200,
            coarsen: GeometryBuilder3D.Coarsener(geometry), galerkin: true);

        return potential;
    }

    /// <summary>The mid-plane potential at two places along the tilt direction.</summary>
    private static (double Low, double High) Across(ScalarField3D potential, Grid3D grid)
    {
        var midY = grid.CountY / 2;
        var midZ = grid.CountZ / 2;
        var quarter = grid.CountX / 4;

        return (potential[quarter, midY, midZ], potential[3 * quarter, midY, midZ]);
    }

    /// <summary>Half turns for a given convergence across the plate.</summary>
    private static double HalfTurnsFor(double convergence) =>
        Math.Atan(convergence / 0.008) / Math.PI;

    /// <summary>A parallel pair is symmetric about its centre; a tilted one is not.</summary>
    /// <remarks>
    /// <para>
    /// <b>The control is the whole test.</b> With the plates parallel the geometry is
    /// symmetric in x, so the mid-plane potential at one quarter and at three quarters must
    /// agree — and it does, to round-off, which says the mesh and the solver introduce no
    /// asymmetry of their own. Any difference in the tilted case is therefore the tilt.
    /// </para>
    /// <para>
    /// Note what the control does <i>not</i> prove: the untilted problem is exactly
    /// symmetric, so it would report zero asymmetry however badly converged it was.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASubCellTiltReachesTheSolvedFieldAndAParallelPairIsSymmetric()
    {
        var grid = GeometryBuilder3D.BuildGrid(Plates(0.0));

        const double Convergence = 200e-6;

        output.WriteLine(
            $"cell {Cell * 1e3:F2} mm, convergence {Convergence * 1e6:F0} um over 8 mm "
            + $"= {HalfTurnsFor(Convergence):E3} half turns");
        output.WriteLine(
            $"each end moves {Convergence * 0.5 * 1e6:F0} um, which is "
            + $"{Convergence * 0.5 / Cell:F3} of a cell");
        output.WriteLine(string.Empty);

        var flat = Across(Solve(Plates(0.0)), grid);
        var tilted = Across(Solve(Plates(HalfTurnsFor(Convergence))), grid);

        var flatAsymmetry = Math.Abs(flat.High - flat.Low);
        var tiltedAsymmetry = Math.Abs(tilted.High - tilted.Low);

        output.WriteLine($"parallel   {flat.Low:F6} / {flat.High:F6} V   "
            + $"asymmetry {flatAsymmetry:E3} V");
        output.WriteLine($"tilted     {tilted.Low:F6} / {tilted.High:F6} V   "
            + $"asymmetry {tiltedAsymmetry:E3} V");

        Assert.True(
            flatAsymmetry < 1e-9,
            $"a parallel pair reported {flatAsymmetry:E3} V of asymmetry across the mid-plane, "
            + "so the mesh is introducing one and the tilted measurement would be measuring "
            + "the mesh");

        Assert.True(
            tiltedAsymmetry > 1e-3,
            $"a {Convergence * 1e6:F0} um convergence — {Convergence * 0.5 / Cell:F3} of a cell "
            + $"at each end — produced only {tiltedAsymmetry:E3} V of asymmetry. A rasterised "
            + "boundary rounds a sub-cell displacement to nothing; this needs the cut cell");
    }

    /// <summary>The response is proportional to the tilt over two hundred fold.</summary>
    /// <remarks>
    /// <para>
    /// <b>What separates a resolved boundary from a staircase.</b> The response to a small
    /// geometric perturbation is linear in it — that is FLD-1's whole premise — so a
    /// discretisation carrying the tilt properly must report a proportional answer, while a
    /// rasterised one is a step function of electrode position: identically zero below one
    /// cell, then percent-level, with nothing in between.
    /// </para>
    /// <para>
    /// <b>A ladder rather than two points</b>, because two points cannot tell proportionality
    /// from an affine response with an offset — and an offset is exactly what the first
    /// version of this test found. Each ratio is asserted against its own step, so nothing
    /// here needs a reference value for any single tilt.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheResponseIsProportionalToTheTiltDownToAThousandthOfACell()
    {
        var grid = GeometryBuilder3D.BuildGrid(Plates(0.0));

        var baseline = Across(Solve(Plates(0.0)), grid);

        double Asymmetry(double convergence)
        {
            var (low, high) = Across(Solve(Plates(HalfTurnsFor(convergence))), grid);

            return (high - low) - (baseline.High - baseline.Low);
        }

        double[] ladder = [1e-6, 2e-6, 5e-6, 12.5e-6, 25e-6, 50e-6, 100e-6, 200e-6];

        var measured = ladder.Select(Asymmetry).ToArray();

        for (var k = 0; k < ladder.Length; k++)
        {
            var expected = k == 0 ? double.NaN : ladder[k] / ladder[k - 1];
            var seen = k == 0 ? double.NaN : measured[k] / measured[k - 1];

            output.WriteLine(
                $"{ladder[k] * 1e6,6:F1} um  ({ladder[k] * 0.5 / Cell,6:F4} cell)  "
                + $"{measured[k],12:E4} V   step {expected,6:F3} -> {seen,7:F4}");

            if (k > 0)
            {
                Assert.Equal(expected, seen, 1);
            }
        }
    }

    /// <summary>A conductor face exactly on the node lattice is degenerate.</summary>
    /// <remarks>
    /// <para>
    /// <b>Found by the linearity ladder above refusing to be linear</b>, and worth pinning
    /// because it is not about tilting: it is about where a conductor sits relative to the
    /// nodes. With the plate faces exactly on grid nodes, the proportional response acquires
    /// a fixed offset worth about <b>seventeen microns of convergence</b> — so a tilt of one
    /// micron and a tilt of ten report nearly the same asymmetry, and the ladder's step
    /// ratios come out at 1.05, 1.16, 1.34 instead of the step itself.
    /// </para>
    /// <para>
    /// Moving every face a quarter cell off the lattice removes it completely: the ratios
    /// become 2.0000 and 2.5000 for steps of 2 and 2.5, over a four-hundred-fold range.
    /// </para>
    /// <para>
    /// <b>The mechanism is a hypothesis, not a finding.</b> A node lying exactly on a
    /// Dirichlet surface is classified inside, so an arbitrarily small tilt moves the surface
    /// off that node on one side and not the other — a whole node's change in classification
    /// rather than a small change in a cut length. What is established is the measurement and
    /// the cure.
    /// </para>
    /// <para>
    /// The practical consequence is a modelling rule: do not place a conductor face exactly
    /// on a cell boundary when the quantity of interest is a small geometric perturbation.
    /// Same shape as the parallel-plate example's two mistakes — the geometry, not the
    /// discretisation, was what needed fixing.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFaceExactlyOnTheNodeLatticeIsDegenerate()
    {
        var grid = GeometryBuilder3D.BuildGrid(Plates(0.0));

        double Asymmetry(double convergence, double offset)
        {
            var (low, high) = Across(Solve(Plates(HalfTurnsFor(convergence), offset)), grid);

            return Math.Abs(high - low);
        }

        // A thousandth of a cell, where the proportional response is tiny and any offset
        // dominates it.
        const double Tiny = 1e-6;

        var aligned = Asymmetry(Tiny, 0.0);
        var offLattice = Asymmetry(Tiny, Offset);

        output.WriteLine($"faces on the node lattice   {aligned:E4} V");
        output.WriteLine($"faces a quarter cell off    {offLattice:E4} V");
        output.WriteLine($"ratio                       {aligned / offLattice:F1}x");

        Assert.True(
            aligned > 10.0 * offLattice,
            "the node-aligned case no longer shows the degeneracy this test documents. If the "
            + "classification of a node lying exactly on a Dirichlet surface has been made "
            + "continuous, this test has done its job and should be rewritten to assert that "
            + "instead");
    }
}
