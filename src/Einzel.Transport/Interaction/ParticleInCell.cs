using Einzel.Core.Geometry;
using Einzel.Fields.Solved;

namespace Einzel.Transport.Interaction;

/// <summary>
/// The packet's self-force from a grid: deposit the charge, solve Poisson, gather
/// the field back.
/// </summary>
/// <remarks>
/// <para>
/// SC-1's <em>approximate</em> method, and the reference it is validated against is
/// <see cref="CoulombInteraction"/>, which was built first for exactly that reason.
/// The direct sum costs <c>O(N^2)</c> per evaluation; this costs one solve plus
/// <c>O(N)</c>, and the solve is not done every evaluation.
/// </para>
/// <para>
/// <strong>The grid is the packet's, not the instrument's.</strong> A packet drifting
/// down a metre-long analyzer cannot have a grid over the whole instrument at any
/// resolution that resolves the packet, so the box is built around the packet itself
/// and rebuilt as it changes. That makes the boundary condition a modelling choice
/// rather than a physical one: a real packet in flight is in free space, and this
/// puts it in an earthed box. Centring the box on the packet is what keeps that
/// cheap - a centred charge distribution in a symmetric earthed box induces almost no
/// field at its own centre, so the error is second order in how far the packet sits
/// off centre and in how much the box departs from a sphere. <see cref="Padding"/>
/// buys it down and is reported.
/// </para>
/// <para>
/// <strong>The grid lives in the packet's frame, centred on the centroid.</strong>
/// Every deposit and every gather is done at the position relative to the current
/// centroid, so a packet in uniform translation is <em>exact</em> - its self-field
/// really does travel with it - and translation never costs anything. What ages is
/// the packet's <em>shape</em>, which is why the refresh criterion is written on
/// shape rather than on position or on a step count: re-solve when the RMS radius
/// has moved by more than <see cref="RefreshTolerance"/>. That is a statement about
/// the approximation rather than a number chosen to make something finish.
/// </para>
/// <para>
/// <strong>A refresh usually reuses the grid and the previous answer.</strong>
/// Solving at every Runge-Kutta stage would be seven solves a step and defeat the
/// point, but even one solve per refresh is expensive when a packet expanding
/// fivefold refreshes thirty times. The box is rebuilt only when the packet has
/// outgrown it or rattles around inside it; otherwise the same grid is solved again
/// with the previous potential as the initial guess, and the multigrid starts from
/// something nearly right rather than from zero.
/// </para>
/// <para>
/// <strong>Trilinear, and ACC-3 is not violated.</strong> That requirement forbids
/// trilinear interpolation on a trajectory path. This is not one: it is the gather of
/// a self-consistent field whose accuracy the deposit already bounds, and the gather
/// must use the deposit's own weights or a particle feels its own charge and the
/// packet heats out of nothing. The applied field the ion flies through is still
/// tricubic.
/// </para>
/// </remarks>
public sealed class ParticleInCell : ISelfField
{
    private readonly double _chargeSi;
    private readonly double _chargeToMassSi;
    private readonly int _nodes;

    private Grid3D? _grid;
    private DirichletMask3D? _mask;
    private ScalarField3D? _potential;
    private double _boxHalfWidth;
    private double _solvedRadius;

    /// <summary>Creates a grid-based self-field.</summary>
    /// <param name="population">Ions in the physical packet.</param>
    /// <param name="macroparticles">Trajectories actually computed.</param>
    /// <param name="chargeSi">Charge of one real ion, in coulombs.</param>
    /// <param name="massSi">Mass of one real ion, in kilograms.</param>
    /// <param name="nodes">
    /// Nodes across the box. Rounded up to a power of two by the grid, so 32 and 48
    /// are the same mesh.
    /// </param>
    /// <param name="padding">
    /// Box half-width as a multiple of the packet's RMS radius.
    /// </param>
    /// <param name="refreshTolerance">
    /// Fractional change in RMS radius that forces a new solve.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is not positive.</exception>
    public ParticleInCell(
        double population,
        int macroparticles,
        double chargeSi,
        double massSi,
        int nodes = 32,
        double padding = 4.0,
        double refreshTolerance = 0.05)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(population);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(macroparticles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massSi);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 8);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(padding, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshTolerance);

        Weight = population / macroparticles;

        _chargeSi = chargeSi * Weight;
        _chargeToMassSi = chargeSi / massSi;
        _nodes = nodes;

        Padding = padding;
        RefreshTolerance = refreshTolerance;
    }

    /// <inheritdoc/>
    public double Weight { get; }

    /// <inheritdoc/>
    public string Method => "particle-in-cell";

    /// <summary>Box half-width as a multiple of the packet's RMS radius.</summary>
    /// <remarks>
    /// The one knob that trades the boundary condition against resolution, and the
    /// reason it is reported rather than hidden: a bigger box is closer to free space
    /// and coarser on the packet, at a fixed node count. It is the parameter a
    /// convergence study turns.
    /// </remarks>
    public double Padding { get; }

    /// <summary>Fractional change in RMS radius that forces a new solve.</summary>
    public double RefreshTolerance { get; }

    /// <summary>
    /// How much bigger than the requested padding a freshly built box is made.
    /// </summary>
    /// <remarks>
    /// Headroom for an expanding packet. Without it a box sized exactly to the
    /// padding is outgrown at the next refresh and every refresh becomes a rebuild,
    /// which throws away the previous answer and the mask with it - so the reuse path
    /// exists and never runs.
    /// </remarks>
    private const double Headroom = 1.15;

    /// <summary>How many Poisson solves have been done.</summary>
    /// <remarks>
    /// The cost of the method, and the thing that distinguishes it from the direct
    /// sum. Reported so a run can say what it actually spent rather than what the
    /// method is supposed to cost.
    /// </remarks>
    public int Solves { get; private set; }

    /// <summary>How many times the field was gathered from a held solve.</summary>
    public int Gathers { get; private set; }

    /// <summary>How many times the box itself had to be rebuilt.</summary>
    /// <remarks>
    /// A rebuild throws away the previous answer, so a solve that follows one starts
    /// from zero; a solve on a kept grid starts from something nearly right. The two
    /// costs are different enough to be worth telling apart when a run is slower than
    /// expected.
    /// </remarks>
    public int Rebuilds { get; private set; }

    /// <summary>Total multigrid cycles across every solve.</summary>
    /// <remarks>
    /// What the method actually spent, rather than what a solve is supposed to cost.
    /// A reused grid with a warm start converges in a fraction of the cycles a cold
    /// one needs, and this is where that shows.
    /// </remarks>
    public int Cycles { get; private set; }

    /// <summary>
    /// The fraction of the packet's charge that fell outside the grid on the last
    /// solve.
    /// </summary>
    /// <remarks>
    /// Should be zero: the box is built from the packet's own extent. Anything else
    /// means the packet grew between the bounding-box calculation and the deposit,
    /// and a field short of charge is quietly too weak - which looks exactly like a
    /// packet more dilute than it is.
    /// </remarks>
    public double ChargeOutside { get; private set; }

    /// <inheritdoc/>
    public void Accumulate(
        ReadOnlySpan<Vec3> positions, ReadOnlySpan<bool> active, Span<Vec3> accelerations)
    {
        if (positions.Length != active.Length || positions.Length != accelerations.Length)
        {
            throw new ArgumentException(
                $"positions ({positions.Length}), active ({active.Length}) and accelerations "
                + $"({accelerations.Length}) must be the same length");
        }

        var live = 0;
        var centroid = Vec3.Zero;

        for (var k = 0; k < positions.Length; k++)
        {
            if (active[k])
            {
                centroid += positions[k];
                live++;
            }
        }

        // One macroparticle is a packet with nobody to push on, and none is not a
        // packet. Either way there is no self-force, and the direct sum returns the
        // same nothing.
        if (live < 2)
        {
            return;
        }

        centroid *= 1.0 / live;

        var spread = 0.0;

        for (var k = 0; k < positions.Length; k++)
        {
            if (active[k])
            {
                spread += (positions[k] - centroid).LengthSquared;
            }
        }

        var radius = Math.Sqrt(spread / live);

        if (!(radius > 0.0))
        {
            // Every macroparticle at one point. The self-field is unbounded rather
            // than large, which is the case the validator refuses at the document
            // level; reached here it is nothing this method can describe.
            return;
        }

        if (NeedsSolve(radius))
        {
            Solve(positions, active, centroid, radius);
        }

        // Sampled in the packet's own frame, so translation costs nothing and is
        // exact rather than approximated.
        for (var k = 0; k < positions.Length; k++)
        {
            if (!active[k])
            {
                continue;
            }

            var at = positions[k] - centroid;

            accelerations[k] +=
                CloudInCell.Field(_potential!, in at, CloudShape.Quadratic) * _chargeToMassSi;
        }

        Gathers++;
    }

    private bool NeedsSolve(double radius) =>
        _potential is null
        || Math.Abs(radius - _solvedRadius) > RefreshTolerance * _solvedRadius;

    private void Solve(
        ReadOnlySpan<Vec3> positions, ReadOnlySpan<bool> active, Vec3 centroid, double radius)
    {
        var wanted = Padding * radius;

        // Rebuilt only when the box is the wrong size for the packet: too small to
        // hold it, or so large that the packet occupies a few cells and the deposit
        // is mostly empty. Between those the same grid is solved again from the
        // previous answer, which is what makes a refresh cheap.
        //
        // Built with headroom above the padding asked for, because a packet that is
        // expanding - which is the only reason a refresh happens at all - outgrows a
        // box sized exactly to it on the very next refresh, and then every refresh is
        // a rebuild and the reuse never fires. The headroom is spent as extra
        // padding, so the box is never tighter than Padding and is sometimes looser.
        var reuse = _grid is not null
            && wanted <= _boxHalfWidth
            && wanted >= 0.5 * _boxHalfWidth;

        if (!reuse)
        {
            _boxHalfWidth = Headroom * wanted;

            _grid = Grid3D.OverBox(
                -_boxHalfWidth, -_boxHalfWidth, -_boxHalfWidth,
                _boxHalfWidth, _boxHalfWidth, _boxHalfWidth,
                2.0 * _boxHalfWidth / _nodes);

            // The mask goes with the grid. Rebuilding it every solve was allocating
            // several arrays the size of the whole box - six cut-link arms among them
            // - for a boundary that has not moved.
            _mask = new DirichletMask3D(_grid);

            Ground(_mask, _grid);

            _potential = null;

            Rebuilds++;
        }

        var grid = _grid!;

        var live = new List<Vec3>();
        var charge = new List<double>();

        for (var k = 0; k < positions.Length; k++)
        {
            if (active[k])
            {
                live.Add(positions[k] - centroid);
                charge.Add(_chargeSi);
            }
        }

        var deposit = CloudInCell.Charge(grid, live, charge, CloudShape.Quadratic);

        ChargeOutside = deposit.FractionOutside;

        var (potential, report) = PoissonSolver3D.Solve(
            _mask!,
            tolerance: 1e-8,
            maximumCycles: 60,
            initialGuess: _potential,
            source: deposit.Source);

        _potential = potential;
        _solvedRadius = radius;

        Solves++;
        Cycles += report.Cycles;
    }

    /// <summary>Holds every face of the box at zero.</summary>
    /// <remarks>
    /// The approximation to free space, and the reason <see cref="Padding"/> exists.
    /// A packet centred in a symmetric earthed box induces almost no field at its own
    /// centre, so this is cheaper than it looks - but it is still a box, and the
    /// residual is what a padding study measures.
    /// </remarks>
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
