using Einzel.Fields.Solved;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// The potential an ion density makes for itself, solved on the density's own grid.
/// </summary>
/// <remarks>
/// <para>
/// Space charge for the diffusive mode. The trajectory mode has had it since SC-1 - a direct
/// pairwise sum and a particle-in-cell method - and the density mode has had none: a density
/// was stepped through whatever applied field it was handed and its own charge never entered
/// that field. That is the missing half wherever charge capacity is the design question, which
/// for a mobility analyser it is: the whole reason the second-generation TIMS tunnel is twice
/// as long is to hold more charge on its rising edge.
/// </para>
/// <para>
/// <b>It is cheaper here than it was for particles, and the reason is worth stating.</b> A
/// particle method must first decide how to turn a set of positions into a charge distribution
/// on a grid, and getting the deposit and the gather to share their weights is what stops an
/// ion feeling its own field. A density has no such step: it already <em>is</em> a charge
/// distribution, on the grid the solve already uses. So this is
/// <c>rho = q n</c>, one Poisson solve, and the potential added to the applied one.
/// </para>
/// <para>
/// <b>Mean field, and named for it.</b> What comes out is the potential of the smooth density,
/// which is what a drift-diffusion description has to be consistent with - the same
/// approximation that makes the density a density. It carries no correlations between ions and
/// no discrete-charge fluctuations, so it is not the direct pairwise sum's answer with a
/// coarser method: those are different physics, and above a few thousand ions in a millimetre
/// the mean field is the right one. The name says which.
/// </para>
/// <para>
/// The coupling is exact where the scheme is. Scharfetter-Gummel builds its flux from the
/// potential difference across a face, so a self-potential enters as an ordinary per-node
/// scalar and the flux stays antisymmetric between two cells - the conservation the scheme has
/// is untouched. Where the drift is needed on its own, at an open edge and in the stability
/// limit, the self-field's gradient is added to the applied field's before the mobility is
/// applied, so both come from one total field rather than two.
/// </para>
/// </remarks>
public sealed class DensitySelfField
{
    /// <summary>The permittivity of vacuum, in farads per metre.</summary>
    public const double VacuumPermittivitySi = 8.8541878188e-12;

    private readonly Grid2D _grid;
    private readonly DirichletMask _mask;
    private readonly double _chargeSi;
    private readonly double _tolerance;
    private readonly double[] _potential;
    private double[]? _solvedFor;
    private double _solvedCharge;

    /// <summary>Builds the self-field solver for a tracked region.</summary>
    /// <param name="grid">The density's grid.</param>
    /// <param name="cylindrical">Whether the grid is a half-plane about an axis of rotation.</param>
    /// <param name="absorbers">The conductors inside the region, which screen the self-field.</param>
    /// <param name="edges">What each domain edge does to ions, which decides its condition here.</param>
    /// <param name="chargeSi">The ion's charge, in coulombs.</param>
    /// <param name="refreshTolerance">
    /// Relative change in the density, as a fraction of the charge present, at which the
    /// potential is solved again.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The tolerance is not positive.</exception>
    /// <remarks>
    /// <b>Where the boundary conditions come from, and what they assume.</b> A conductor is an
    /// equipotential whose applied potential is already in the applied field, so the
    /// <em>self</em> potential vanishes on it: the metal screens. That is exact, and it is the
    /// condition that matters, because a cloud in a bore is dominated by the wall a millimetre
    /// away rather than by anything further off. A reflecting edge is a symmetry plane and gets
    /// Neumann, which is exact too. An edge ions leave through is earthed here, and that one is
    /// an approximation: the instrument continues past it and the real field leaks into
    /// whatever is next. It is the same approximation the applied solve makes at its own
    /// domain edge, and for a long cloud in a tube it is small - but it is why this is a
    /// self-field over a tracked region rather than over an instrument.
    /// </remarks>
    public DensitySelfField(
        Grid2D grid,
        bool cylindrical,
        AbsorbingCells absorbers,
        DriftDiffusion.DomainEdges edges,
        double chargeSi,
        double refreshTolerance = 0.05)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(absorbers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshTolerance);

        _grid = grid;
        _chargeSi = chargeSi;
        _tolerance = refreshTolerance;
        _potential = new double[grid.CountX * grid.CountY];

        _mask = new DirichletMask(grid)
        {
            Symmetry = cylindrical
                ? Core.Model.SolveSymmetry.Cylindrical
                : Core.Model.SolveSymmetry.Translational,
            LeftEdge = Condition(edges.MinX),
            RightEdge = Condition(edges.MaxX),
            BottomEdge = cylindrical ? EdgeCondition.Neumann : Condition(edges.MinY),
            TopEdge = Condition(edges.MaxY),
        };

        // Earthing an edge means fixing its nodes at zero, not only declaring the condition.
        // The condition tells the stencil and the interpolant that the edge is not a mirror;
        // it constrains nothing on its own. A mask with every edge declared Dirichlet and no
        // node actually fixed is a pure Neumann problem, which with a net charge inside it has
        // no solution at all - and what that looks like is not a refusal but a V-cycle
        // diverging to 1e141 volts over two hundred cycles, which is how this was found.
        _mask.FixDirichletEdges(static (_, _) => 0.0);

        // The conductors. Node by node rather than as rectangles, because what is known here
        // is which nodes the density is absorbed at, and that is exactly the set the metal
        // occupies.
        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (absorbers.Blocks((j * grid.CountX) + i))
                {
                    _mask.Fix(i, j, 0.0);
                }
            }
        }

        // And with nothing fixed anywhere the problem is still singular, so it is refused
        // rather than run. A self-potential is measured against something: with every
        // boundary a symmetry plane and no metal in the region, a charge has nowhere for its
        // field to terminate and the question has no answer to be approximate about.
        if (_mask.FixedCount == 0)
        {
            throw new ArgumentException(
                "a self-field needs somewhere for its field to terminate: every edge of this "
                + "region is a symmetry plane and it holds no conductor, so the potential of a "
                + "charge inside it is not defined. Give the region an edge ions leave through, "
                + "or a conductor.",
                nameof(edges));
        }
    }

    /// <summary>A reflecting edge is a symmetry plane; one ions leave through is earthed.</summary>
    private static EdgeCondition Condition(Escape escape) =>
        escape == Escape.Reflecting ? EdgeCondition.Neumann : EdgeCondition.Dirichlet;

    /// <summary>How many times the potential has been solved.</summary>
    /// <remarks>
    /// Reported for the same reason the operator assembly count is: a self-consistent solve
    /// per step is affordable on a small grid and not on a large one, and a reader deciding
    /// whether to believe a long run needs to know how often the field was actually brought up
    /// to date rather than carried.
    /// </remarks>
    public int Solves { get; private set; }

    /// <summary>The largest self-potential anywhere in the region at the last solve, in volts.</summary>
    public double PeakVolts { get; private set; }

    /// <summary>How the last self-field solve went.</summary>
    public SolveReport? Report { get; private set; }

    /// <summary>The charge the last solve was made for, in coulombs.</summary>
    public double ChargeSi => _solvedCharge;

    /// <summary>The self-potential per node, row-major, from the last solve.</summary>
    public ReadOnlySpan<double> Potential => _potential;

    /// <summary>
    /// Brings the self-potential up to date if the density has moved enough since the last
    /// solve, and reports whether it did.
    /// </summary>
    /// <param name="density">The density as it stands.</param>
    /// <returns>True when the potential was solved again.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="density"/> is null.</exception>
    /// <remarks>
    /// The criterion is a change in the density itself rather than a step count, for the
    /// reason the particle-in-cell refresh uses shape: a held packet's field is the same field
    /// however long it is held, and an eluting one's changes every step. Measured as the
    /// absolute change summed over the grid against the charge present, so a packet that has
    /// merely been collected does not trigger a solve of nothing.
    /// </remarks>
    public bool Refresh(DensityField density)
    {
        ArgumentNullException.ThrowIfNull(density);

        var cells = _grid.CountX * _grid.CountY;

        if (_solvedFor is null)
        {
            _solvedFor = new double[cells];
        }
        else
        {
            double change = 0.0, present = 0.0;

            for (var j = 0; j < _grid.CountY; j++)
            {
                var volume = density.CellVolume(j);

                for (var i = 0; i < _grid.CountX; i++)
                {
                    var k = (j * _grid.CountX) + i;
                    change += Math.Abs(density[i, j] - _solvedFor[k]) * volume;
                    present += density[i, j] * volume;
                }
            }

            if (present <= 0.0 || change <= _tolerance * present)
            {
                return false;
            }
        }

        var source = new ScalarField2D(_grid);

        for (var j = 0; j < _grid.CountY; j++)
        {
            for (var i = 0; i < _grid.CountX; i++)
            {
                var k = (j * _grid.CountX) + i;
                _solvedFor[k] = density[i, j];

                // grad^2 phi = -rho / epsilon0, the convention the solver's residual fixes.
                source[i, j] = -_chargeSi * density[i, j] / VacuumPermittivitySi;
            }
        }

        var (solved, report) = PoissonSolver2D.Solve(
            _mask, tolerance: 1e-10, maximumCycles: 200, source: source);

        // Kept, not discarded. A self-field that stopped short of its tolerance is
        // indistinguishable from one that met it, and this engine has dropped a SolveReport at
        // a seam often enough to know what that costs.
        Report = report;

        var peak = 0.0;
        var charge = 0.0;

        for (var j = 0; j < _grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < _grid.CountX; i++)
            {
                var value = solved[i, j];
                _potential[(j * _grid.CountX) + i] = value;
                peak = Math.Max(peak, Math.Abs(value));
                charge += _chargeSi * density[i, j] * volume;
            }
        }

        PeakVolts = peak;
        _solvedCharge = charge;
        Solves++;
        return true;
    }
}
