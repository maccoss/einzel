namespace Einzel.Fields.Solved;

/// <summary>
/// One electrode's geometry, as the nodes it pins.
/// </summary>
/// <param name="Name">A name for reporting.</param>
/// <param name="Nodes">The grid nodes this electrode holds.</param>
public sealed record ElectrodeNodes(string Name, IReadOnlyList<(int I, int J)> Nodes);

/// <summary>
/// The per-electrode unit solutions, and the superposition that applies a voltage
/// set to them.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 10: "Solve once per electrode at unit potential with others
/// grounded; any voltage set is a weighted sum. Essential, because it turns
/// voltage optimization from a solve-per-iteration problem into arithmetic."
/// </para>
/// <para>
/// The scale of what that buys is easy to understand and easy to underrate. A
/// voltage optimisation evaluating a thousand candidate tunings on a geometry
/// that takes minutes to solve is a campaign of days; with basis fields it is a
/// thousand weighted sums over a grid, which is seconds. Every optimiser and
/// sweep in section 13 depends on it.
/// </para>
/// <para>
/// And where it stops, which the spec is equally clear about: superposition is
/// exact for a voltage change and breaks the moment the geometry changes, because
/// the basis fields are solutions on one mesh. That is what makes the tolerance
/// work of section 10 need sensitivity fields rather than more superposition.
/// </para>
/// </remarks>
public sealed class BasisFieldSet
{
    private readonly ScalarField2D[] _basis;

    private BasisFieldSet(Grid2D grid, IReadOnlyList<ElectrodeNodes> electrodes, ScalarField2D[] basis, IReadOnlyList<SolveReport> reports)
    {
        Grid = grid;
        Electrodes = electrodes;
        _basis = basis;
        Reports = reports;
    }

    /// <summary>The grid every basis field lives on.</summary>
    public Grid2D Grid { get; }

    /// <summary>The electrodes, in the order their voltages are supplied.</summary>
    public IReadOnlyList<ElectrodeNodes> Electrodes { get; }

    /// <summary>The solve report for each basis field.</summary>
    public IReadOnlyList<SolveReport> Reports { get; }

    /// <summary>
    /// Solves once per electrode, at unit potential with the others grounded.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="electrodes">The electrodes.</param>
    /// <param name="configure">
    /// Applies the edge conditions and any always-grounded structure to a fresh
    /// mask, before the electrode under test is raised to one volt.
    /// </param>
    /// <param name="tolerance">Relative residual for each solve.</param>
    /// <returns>The basis set.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">No electrodes were supplied.</exception>
    public static BasisFieldSet Solve(
        Grid2D grid,
        IReadOnlyList<ElectrodeNodes> electrodes,
        Action<DirichletMask> configure,
        double tolerance = 1e-10)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(electrodes);
        ArgumentNullException.ThrowIfNull(configure);

        if (electrodes.Count == 0)
        {
            throw new ArgumentException("a basis set needs at least one electrode", nameof(electrodes));
        }

        var basis = new ScalarField2D[electrodes.Count];
        var reports = new SolveReport[electrodes.Count];

        for (var e = 0; e < electrodes.Count; e++)
        {
            var mask = new DirichletMask(grid);
            configure(mask);

            // Every electrode is pinned; only the one under test is at one volt.
            for (var other = 0; other < electrodes.Count; other++)
            {
                var potential = other == e ? 1.0 : 0.0;

                foreach (var (i, j) in electrodes[other].Nodes)
                {
                    mask.Fix(i, j, potential);
                }
            }

            var (field, report) = PoissonSolver2D.Solve(mask, tolerance);
            basis[e] = field;
            reports[e] = report;
        }

        return new BasisFieldSet(grid, electrodes, basis, reports);
    }

    /// <summary>The unit solution for one electrode.</summary>
    /// <param name="index">The electrode index.</param>
    /// <returns>The basis field.</returns>
    public ScalarField2D Basis(int index) => _basis[index];

    /// <summary>Applies a voltage set, by weighted sum.</summary>
    /// <param name="volts">One potential per electrode, in volts.</param>
    /// <returns>The superposed potential.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="volts"/> is null.</exception>
    /// <exception cref="ArgumentException">The count does not match the electrode count.</exception>
    public ScalarField2D Superpose(IReadOnlyList<double> volts)
    {
        ArgumentNullException.ThrowIfNull(volts);

        if (volts.Count != _basis.Length)
        {
            throw new ArgumentException(
                $"expected {_basis.Length} potentials, one per electrode, but got {volts.Count}", nameof(volts));
        }

        var total = new ScalarField2D(Grid);

        for (var e = 0; e < _basis.Length; e++)
        {
            if (volts[e] != 0.0)
            {
                total.AddScaled(_basis[e], volts[e]);
            }
        }

        return total;
    }
}
