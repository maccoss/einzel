using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Solved;

namespace Einzel.Library;

/// <summary>
/// The potential a printed-circuit mirror applies along its stripes, as a
/// piecewise-linear profile in depth.
/// </summary>
/// <remarks>
/// <para>
/// The memo's mirror is stripe electrodes printed on two facing boards, running
/// along the drift direction, so the applied potential is a function of depth
/// alone. That makes the design surface exactly this profile: what the stripe at
/// each depth is held at.
/// </para>
/// <para>
/// One linear segment is a single-stage mirror, which cancels the first-order
/// energy term and leaves the second. Two segments — a short steep decelerating
/// stage followed by a longer shallow reflecting stage — is the Mamyrin
/// arrangement, which can cancel the second order as well.
/// </para>
/// </remarks>
/// <param name="Breakpoints">
/// Depths at which the gradient changes, in metres from the mirror entrance,
/// strictly increasing and beginning at zero.
/// </param>
/// <param name="Potentials">
/// The potential at each breakpoint, in volts. The first is the entrance
/// potential, normally zero; the last is the cap.
/// </param>
public sealed record MirrorProfile(IReadOnlyList<double> Breakpoints, IReadOnlyList<double> Potentials)
{
    /// <summary>A single-stage mirror: one linear ramp from entrance to cap.</summary>
    /// <param name="depth">Total mirror depth, in metres.</param>
    /// <param name="capPotential">Potential at the cap, in volts.</param>
    /// <returns>The profile.</returns>
    public static MirrorProfile SingleStage(double depth, double capPotential) =>
        new([0.0, depth], [0.0, capPotential]);

    /// <summary>
    /// A two-stage mirror: a steep first stage, then a shallower reflecting stage.
    /// </summary>
    /// <param name="firstStageDepth">Depth of the decelerating stage, in metres.</param>
    /// <param name="firstStagePotential">Potential at the end of the first stage, in volts.</param>
    /// <param name="totalDepth">Total mirror depth, in metres.</param>
    /// <param name="capPotential">Potential at the cap, in volts.</param>
    /// <returns>The profile.</returns>
    public static MirrorProfile TwoStage(
        double firstStageDepth, double firstStagePotential, double totalDepth, double capPotential) =>
        new([0.0, firstStageDepth, totalDepth], [0.0, firstStagePotential, capPotential]);

    /// <summary>Total depth of the mirror, in metres.</summary>
    public double Depth => Breakpoints[^1];

    /// <summary>The potential at a depth, in volts, by linear interpolation.</summary>
    /// <param name="depth">Depth from the entrance, in metres.</param>
    /// <returns>The applied potential.</returns>
    public double PotentialAtDepth(double depth)
    {
        if (depth <= Breakpoints[0])
        {
            return Potentials[0];
        }

        for (var k = 1; k < Breakpoints.Count; k++)
        {
            if (depth <= Breakpoints[k])
            {
                var span = Breakpoints[k] - Breakpoints[k - 1];
                var t = span > 0.0 ? (depth - Breakpoints[k - 1]) / span : 0.0;
                return Potentials[k - 1] + (t * (Potentials[k] - Potentials[k - 1]));
            }
        }

        return Potentials[^1];
    }
}

/// <summary>
/// A planar printed-circuit ion mirror: stripe electrodes on two facing boards,
/// with a field-free run printed on the same boards beyond the entrance.
/// </summary>
/// <remarks>
/// <para>
/// LIB-1: device templates live here and nowhere lower. Everything beneath this
/// assembly sees a Dirichlet mask, a solve, and an interpolated field; the word
/// mirror appears only at this level.
/// </para>
/// <para>
/// The geometry follows the memo's figure 4. Boards face each other across a gap,
/// the ion travels near the mid-plane, and the stripes run along the drift
/// direction so the potential is independent of it. Solving in the plane of the
/// oscillation and the gap therefore loses nothing, which is the reduction
/// Einzel.Fields was built for.
/// </para>
/// <para>
/// One consequence of solving rather than assuming: the applied stripe profile
/// and the potential on the ion's path are not the same function. A kink in the
/// profile — the stage boundary of a two-stage mirror — is smoothed over roughly
/// the board gap by the time it reaches the mid-plane, because the boundary value
/// problem damps every Fourier component of the profile by the cosh of its
/// wavenumber times the half-gap. A design that assumed the ion sees the printed
/// profile would be designing a mirror it does not have.
/// </para>
/// </remarks>
public sealed class PlanarMirror
{
    private PlanarMirror(
        MirrorProfile profile,
        double boardGap,
        double fieldFreeRun,
        ScalarField2D potential,
        SolveReport report)
    {
        Profile = profile;
        BoardGap = boardGap;
        FieldFreeRun = fieldFreeRun;
        Potential = potential;
        Report = report;
    }

    /// <summary>The applied stripe profile.</summary>
    public MirrorProfile Profile { get; }

    /// <summary>Distance between the facing boards, in metres.</summary>
    public double BoardGap { get; }

    /// <summary>Field-free run printed beyond the entrance, in metres.</summary>
    public double FieldFreeRun { get; }

    /// <summary>The solved potential.</summary>
    public ScalarField2D Potential { get; }

    /// <summary>How the solve went.</summary>
    public SolveReport Report { get; }

    /// <summary>
    /// Builds and solves a mirror occupying x in [-depth, fieldFreeRun], entered
    /// from positive x and reflecting back that way.
    /// </summary>
    /// <param name="profile">The applied stripe profile.</param>
    /// <param name="boardGap">Distance between the facing boards, in metres.</param>
    /// <param name="fieldFreeRun">
    /// How far the grounded boards continue past the entrance. Long enough that
    /// the fringe field has decayed before the domain ends.
    /// </param>
    /// <param name="cellsPerGap">Grid cells across the board gap; sets resolution.</param>
    /// <param name="tolerance">Relative residual for the solve.</param>
    /// <returns>The solved mirror.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    public static PlanarMirror Solve(
        MirrorProfile profile,
        double boardGap,
        double fieldFreeRun,
        int cellsPerGap = 32,
        double tolerance = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boardGap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldFreeRun);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellsPerGap);

        var spacing = boardGap / cellsPerGap;
        var totalLength = profile.Depth + fieldFreeRun;

        var intervalsX = (int)System.Numerics.BitOperations.RoundUpToPowerOf2(
            (uint)Math.Max(4, (int)Math.Ceiling(totalLength / spacing)));

        var grid = Grid2D.OverBox(-profile.Depth, -boardGap / 2.0, -profile.Depth + (intervalsX * spacing),
            boardGap / 2.0, intervalsX);

        var mask = new DirichletMask(grid);

        // The boards themselves: the stripe potential at each depth, on both
        // faces. Depth is measured back from the entrance at x = 0.
        for (var i = 0; i < grid.CountX; i++)
        {
            var x = grid.X(i);
            var applied = x <= 0.0 ? profile.PotentialAtDepth(-x) : 0.0;

            mask.Fix(i, 0, applied);
            mask.Fix(i, grid.CountY - 1, applied);
        }

        // The cap closes the far end of the mirror.
        for (var j = 0; j < grid.CountY; j++)
        {
            mask.Fix(0, j, profile.Potentials[^1]);
        }

        // The far end of the printed field-free run opens into the drift region.
        // Zero normal derivative rather than a pinned potential: pinning it would
        // put a grid there, and the whole point of a printed-circuit mirror is
        // that it is gridless.
        mask.RightEdge = EdgeCondition.Neumann;

        var (potential, report) = PoissonSolver2D.Solve(mask, tolerance, maximumCycles: 400);

        return new PlanarMirror(profile, boardGap, fieldFreeRun, potential, report);
    }

    /// <summary>The mirror as an electrostatic field.</summary>
    /// <returns>The field, with the bicubic interpolant ACC-3 requires.</returns>
    /// <remarks>
    /// The domain boundary is declared smooth. That is what the printed field-free
    /// run beyond the entrance is for: the fringe has decayed to nothing well
    /// before the solve ends, so the edge of the box is not a place where the
    /// field jumps and an integrator should not be made to stop there.
    /// </remarks>
    public IElectrostaticField Field() =>
        new SolvedField2D(Potential, new BicubicInterpolant(Potential), boundaryIsDiscontinuous: false);

    /// <summary>
    /// The potential the ion actually sees along the mid-plane, as against the
    /// profile printed on the boards.
    /// </summary>
    /// <param name="depth">Depth from the entrance, in metres.</param>
    /// <returns>The mid-plane potential, in volts.</returns>
    public double MidPlanePotential(double depth)
    {
        var interpolant = new BicubicInterpolant(Potential);
        return interpolant.Value(-depth, 0.0);
    }

    /// <summary>
    /// Depth at which an ion of the given energy turns, in metres, found by
    /// bisection on the solved mid-plane potential.
    /// </summary>
    /// <param name="kineticEnergy">Kinetic energy at the entrance.</param>
    /// <param name="chargeNumber">Charge number of the ion.</param>
    /// <returns>The turning depth.</returns>
    /// <exception cref="InvalidOperationException">The ion is not turned by this mirror.</exception>
    /// <remarks>
    /// Read off the solve rather than from the printed profile, so it reports the
    /// depth the ion reaches in the mirror that was built, not the one that was
    /// drawn.
    /// </remarks>
    public double TurningDepth(Quantity kineticEnergy, int chargeNumber)
    {
        var electronvolts = kineticEnergy.In("eV");
        var required = electronvolts / Math.Abs(chargeNumber);

        if (MidPlanePotential(Profile.Depth) < required)
        {
            throw new InvalidOperationException(
                $"an ion of {electronvolts:G6} eV is not turned by a mirror whose cap reaches "
                + $"{MidPlanePotential(Profile.Depth):G6} V on the mid-plane");
        }

        var low = 0.0;
        var high = Profile.Depth;

        for (var i = 0; i < 200 && high - low > 1e-12; i++)
        {
            var mid = 0.5 * (low + high);

            if (MidPlanePotential(mid) < required)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return 0.5 * (low + high);
    }
}
