namespace Einzel.Fields.Solved;

/// <summary>Which of the six stencil arms a cut is recorded on.</summary>
public enum Arm3D
{
    /// <summary>Toward increasing x.</summary>
    East,

    /// <summary>Toward decreasing x.</summary>
    West,

    /// <summary>Toward increasing y.</summary>
    North,

    /// <summary>Toward decreasing y.</summary>
    South,

    /// <summary>Toward increasing z.</summary>
    Up,

    /// <summary>Toward decreasing z.</summary>
    Down,
}

/// <summary>
/// Where a conductor surface cuts between nodes, and what potential it holds there.
/// </summary>
/// <remarks>
/// <para>
/// The three-dimensional form of the Shortley-Weller data. Without it a boundary
/// snaps to the nearest node, which makes the discrete operator a staircase
/// function of where an electrode sits: invisible below one cell and percent-level
/// above one, with no step size in between. That failure was measured in two
/// dimensions and it is not a property of the dimension count.
/// </para>
/// <para>
/// Six arms per node rather than four, and stored as flat arrays because a
/// per-node object would be one allocation per node and there are millions.
/// </para>
/// </remarks>
public sealed class CutLinks3D
{
    private readonly double[] _fraction;
    private readonly double[] _potential;

    /// <summary>Creates an uncut set of links.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public CutLinks3D(Grid3D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Grid = grid;

        var count = grid.NodeCount * 6;

        _fraction = new double[count];
        _potential = new double[count];

        Array.Fill(_fraction, 1.0);
    }

    /// <summary>The grid these links cover.</summary>
    public Grid3D Grid { get; }

    /// <summary>How many arms are cut.</summary>
    public int CutCount { get; private set; }

    /// <summary>The shortest fraction recorded, for reporting how close to a node a surface came.</summary>
    public double SmallestFraction { get; private set; } = 1.0;

    /// <summary>
    /// The shortest arm a cut is allowed to be, as a fraction of a cell.
    /// </summary>
    /// <remarks>
    /// A surface passing within a thousandth of a node makes that arm's coefficient
    /// enormous and the iteration ill-conditioned. Clamping trades a sub-cell
    /// placement error for a conditioning one, and at this size the clamp is the
    /// only residual error left in a swept-boundary test.
    /// </remarks>
    public const double MinimumFraction = 1e-3;

    /// <summary>Records a cut on one arm.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <param name="arm">Which arm.</param>
    /// <param name="fraction">Distance to the surface, as a fraction of a cell.</param>
    /// <param name="potential">The potential the surface holds, in volts.</param>
    public void Cut(int i, int j, int k, Arm3D arm, double fraction, double potential)
    {
        var clamped = Math.Max(MinimumFraction, Math.Min(1.0, fraction));
        var slot = (Grid.Index(i, j, k) * 6) + (int)arm;

        if (clamped >= _fraction[slot])
        {
            // An arm already cut closer wins: the nearest surface is the one the
            // stencil must reach, and a second electrode further along the same arm
            // is behind the first.
            return;
        }

        if (_fraction[slot] == 1.0)
        {
            CutCount++;
        }

        _fraction[slot] = clamped;
        _potential[slot] = potential;

        SmallestFraction = Math.Min(SmallestFraction, clamped);
    }

    /// <summary>Reads one arm.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <param name="arm">Which arm.</param>
    /// <param name="potential">The potential at the cut, when there is one.</param>
    /// <returns>The fraction of a cell to the surface; one when the arm is not cut.</returns>
    public double Fraction(int i, int j, int k, Arm3D arm, out double potential)
    {
        var slot = (Grid.Index(i, j, k) * 6) + (int)arm;

        potential = _potential[slot];
        return _fraction[slot];
    }
}

/// <summary>Which nodes hold a fixed potential, and the conditions on the six faces.</summary>
public sealed class DirichletMask3D
{
    private readonly bool[] _fixed;
    private readonly double[] _value;

    /// <summary>Creates a mask with nothing fixed and every face Dirichlet at zero.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public DirichletMask3D(Grid3D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Grid = grid;
        _fixed = new bool[grid.NodeCount];
        _value = new double[grid.NodeCount];
    }

    /// <summary>The grid this mask covers.</summary>
    public Grid3D Grid { get; }

    /// <summary>Where conductor surfaces cut between nodes, when the geometry carried them.</summary>
    public CutLinks3D? Cuts { get; set; }

    /// <summary>
    /// The smallest half-extent of any interior electrode, in metres.
    /// </summary>
    /// <remarks>
    /// How far a V-cycle may coarsen. Set from the geometry rather than counted
    /// from the nodes, because a node count cannot distinguish an electrode that is
    /// merely coarsely represented from one that has stopped being represented at
    /// all - and with sub-cell surfaces the count stays positive long after the
    /// arms have become too short to condition anything.
    /// </remarks>
    public double SmallestFeature { get; set; } = double.PositiveInfinity;

    /// <summary>How many nodes hold a fixed potential.</summary>
    public int FixedCount { get; private set; }

    /// <summary>How many fixed nodes lie away from the domain faces.</summary>
    /// <remarks>
    /// The number that decides how far a V-cycle may coarsen. A boundary surface
    /// keeps a fraction of its nodes per level and survives; an interior electrode
    /// is a volume and loses seven eighths of its nodes each time, which a total
    /// count would call healthy right up to the level where it vanishes.
    /// </remarks>
    public int InteriorFixedCount { get; private set; }

    /// <summary>Condition on the x-minimum face.</summary>
    public EdgeCondition LowerX { get; set; }

    /// <summary>Condition on the x-maximum face.</summary>
    public EdgeCondition UpperX { get; set; }

    /// <summary>Condition on the y-minimum face.</summary>
    public EdgeCondition LowerY { get; set; }

    /// <summary>Condition on the y-maximum face.</summary>
    public EdgeCondition UpperY { get; set; }

    /// <summary>Condition on the z-minimum face.</summary>
    public EdgeCondition LowerZ { get; set; }

    /// <summary>Condition on the z-maximum face.</summary>
    public EdgeCondition UpperZ { get; set; }

    /// <summary>Whether a node holds a fixed potential.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns><see langword="true"/> when fixed.</returns>
    public bool IsFixed(int i, int j, int k) => _fixed[Grid.Index(i, j, k)];

    /// <summary>The potential a fixed node holds.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns>The potential, in volts.</returns>
    public double Value(int i, int j, int k) => _value[Grid.Index(i, j, k)];

    /// <summary>Fixes a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <param name="potential">The potential to hold, in volts.</param>
    public void Fix(int i, int j, int k, double potential)
    {
        var index = Grid.Index(i, j, k);

        if (!_fixed[index])
        {
            FixedCount++;

            var onFace = i == 0 || j == 0 || k == 0
                || i == Grid.CountX - 1 || j == Grid.CountY - 1 || k == Grid.CountZ - 1;

            if (!onFace)
            {
                InteriorFixedCount++;
            }
        }

        _fixed[index] = true;
        _value[index] = potential;
    }

    /// <summary>Writes every fixed value into a field.</summary>
    /// <param name="field">The field to stamp.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public void ApplyTo(ScalarField3D field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var values = field.Values;

        for (var index = 0; index < _fixed.Length; index++)
        {
            if (_fixed[index])
            {
                values[index] = _value[index];
            }
        }
    }
}
