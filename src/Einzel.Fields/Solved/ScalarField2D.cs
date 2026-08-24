namespace Einzel.Fields.Solved;

/// <summary>Scalar values on a <see cref="Grid2D"/>, row-major.</summary>
/// <remarks>
/// A flat array rather than a jagged one: the solver sweeps it in index order and
/// the inner loop should not chase a pointer per row.
/// </remarks>
public sealed class ScalarField2D
{
    private readonly double[] _values;

    /// <summary>Creates a zero-filled field.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public ScalarField2D(Grid2D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Grid = grid;
        _values = new double[grid.NodeCount];
    }

    /// <summary>The grid these values live on.</summary>
    public Grid2D Grid { get; }

    /// <summary>The underlying values, row-major.</summary>
    public Span<double> Values => _values;

    /// <summary>Indexes a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <returns>The value at that node.</returns>
    public double this[int i, int j]
    {
        get => _values[Grid.Index(i, j)];
        set => _values[Grid.Index(i, j)] = value;
    }

    /// <summary>Creates an independent copy.</summary>
    /// <returns>The copy.</returns>
    public ScalarField2D Clone()
    {
        var copy = new ScalarField2D(Grid);
        _values.CopyTo(copy._values, 0);
        return copy;
    }

    /// <summary>Adds a scaled field in place.</summary>
    /// <param name="other">The field to add; must share this grid.</param>
    /// <param name="scale">The factor to scale it by.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">The grids differ.</exception>
    /// <remarks>
    /// The whole of basis superposition (spec section 10) is this operation
    /// repeated: once the per-electrode solves exist, applying a voltage set is
    /// arithmetic rather than a solve.
    /// </remarks>
    public void AddScaled(ScalarField2D other, double scale)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!ReferenceEquals(other.Grid, Grid) && other._values.Length != _values.Length)
        {
            throw new ArgumentException("fields must share a grid to be superposed", nameof(other));
        }

        for (var k = 0; k < _values.Length; k++)
        {
            _values[k] += other._values[k] * scale;
        }
    }
}

/// <summary>How the solver treats one edge of the grid.</summary>
public enum EdgeCondition
{
    /// <summary>The edge holds the potential written into the boundary values.</summary>
    Dirichlet,

    /// <summary>
    /// Zero normal derivative: a symmetry plane, or a wall far enough away that
    /// the field is parallel to it.
    /// </summary>
    Neumann,
}

/// <summary>
/// Which nodes hold a fixed potential, and what it is.
/// </summary>
/// <remarks>
/// <para>
/// Electrodes are Dirichlet nodes. Representing them as a mask over the grid
/// rather than as geometry keeps the solver free of any notion of what an
/// electrode is for — architecture invariant 2 in its numerical form.
/// </para>
/// <para>
/// The mask coarsens by injection for multigrid: a coarse node is fixed when the
/// fine node it sits on is fixed. That is exact where electrodes are grid-aligned
/// and thicker than the coarsest spacing, which is the case for stripe
/// electrodes, and degrades to slower convergence rather than a wrong answer
/// where it is not.
/// </para>
/// </remarks>
public sealed class DirichletMask
{
    private readonly bool[] _fixed;
    private readonly double[] _value;

    /// <summary>Creates a mask with nothing fixed and all edges Dirichlet at zero.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public DirichletMask(Grid2D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Grid = grid;
        _fixed = new bool[grid.NodeCount];
        _value = new double[grid.NodeCount];
    }

    /// <summary>The grid this mask covers.</summary>
    public Grid2D Grid { get; }

    /// <summary>Condition on the x = minimum edge.</summary>
    public EdgeCondition LeftEdge { get; set; } = EdgeCondition.Dirichlet;

    /// <summary>Condition on the x = maximum edge.</summary>
    public EdgeCondition RightEdge { get; set; } = EdgeCondition.Dirichlet;

    /// <summary>Condition on the y = minimum edge.</summary>
    public EdgeCondition BottomEdge { get; set; } = EdgeCondition.Dirichlet;

    /// <summary>Condition on the y = maximum edge.</summary>
    public EdgeCondition TopEdge { get; set; } = EdgeCondition.Dirichlet;

    /// <summary>Whether a node holds a fixed potential.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <returns><see langword="true"/> when fixed.</returns>
    public bool IsFixed(int i, int j) => _fixed[Grid.Index(i, j)];

    /// <summary>The fixed potential at a node, in volts.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <returns>The potential.</returns>
    public double ValueAt(int i, int j) => _value[Grid.Index(i, j)];

    /// <summary>Fixes a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="potential">The potential to hold, in volts.</param>
    public void Fix(int i, int j, double potential)
    {
        var index = Grid.Index(i, j);
        _fixed[index] = true;
        _value[index] = potential;
    }

    /// <summary>Fixes an axis-aligned rectangle of nodes, in node indices.</summary>
    /// <param name="i0">First node index along x, inclusive.</param>
    /// <param name="j0">First node index along y, inclusive.</param>
    /// <param name="i1">Last node index along x, inclusive.</param>
    /// <param name="j1">Last node index along y, inclusive.</param>
    /// <param name="potential">The potential to hold, in volts.</param>
    public void FixRectangle(int i0, int j0, int i1, int j1, double potential)
    {
        for (var j = Math.Max(0, j0); j <= Math.Min(Grid.CountY - 1, j1); j++)
        {
            for (var i = Math.Max(0, i0); i <= Math.Min(Grid.CountX - 1, i1); i++)
            {
                Fix(i, j, potential);
            }
        }
    }

    /// <summary>Fixes every node on an edge whose condition is Dirichlet.</summary>
    /// <param name="potentialAt">Potential as a function of position, in volts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="potentialAt"/> is null.</exception>
    public void FixDirichletEdges(Func<double, double, double> potentialAt)
    {
        ArgumentNullException.ThrowIfNull(potentialAt);

        for (var i = 0; i < Grid.CountX; i++)
        {
            if (BottomEdge == EdgeCondition.Dirichlet)
            {
                Fix(i, 0, potentialAt(Grid.X(i), Grid.Y(0)));
            }

            if (TopEdge == EdgeCondition.Dirichlet)
            {
                Fix(i, Grid.CountY - 1, potentialAt(Grid.X(i), Grid.Y(Grid.CountY - 1)));
            }
        }

        for (var j = 0; j < Grid.CountY; j++)
        {
            if (LeftEdge == EdgeCondition.Dirichlet)
            {
                Fix(0, j, potentialAt(Grid.X(0), Grid.Y(j)));
            }

            if (RightEdge == EdgeCondition.Dirichlet)
            {
                Fix(Grid.CountX - 1, j, potentialAt(Grid.X(Grid.CountX - 1), Grid.Y(j)));
            }
        }
    }

    /// <summary>Projects the mask onto the next coarser grid by injection.</summary>
    /// <returns>The coarsened mask.</returns>
    public DirichletMask Coarsen()
    {
        var coarseGrid = Grid.Coarsen();

        var coarse = new DirichletMask(coarseGrid)
        {
            LeftEdge = LeftEdge,
            RightEdge = RightEdge,
            BottomEdge = BottomEdge,
            TopEdge = TopEdge,
        };

        for (var j = 0; j < coarseGrid.CountY; j++)
        {
            for (var i = 0; i < coarseGrid.CountX; i++)
            {
                if (IsFixed(i * 2, j * 2))
                {
                    // The correction scheme solves for the error, which is zero
                    // wherever the potential is pinned, so the coarse value is zero
                    // rather than the fine potential.
                    coarse.Fix(i, j, 0.0);
                }
            }
        }

        return coarse;
    }

    /// <summary>Writes the fixed potentials into a field, leaving free nodes alone.</summary>
    /// <param name="field">The field to seed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public void ApplyTo(ScalarField2D field)
    {
        ArgumentNullException.ThrowIfNull(field);

        for (var j = 0; j < Grid.CountY; j++)
        {
            for (var i = 0; i < Grid.CountX; i++)
            {
                if (IsFixed(i, j))
                {
                    field[i, j] = ValueAt(i, j);
                }
            }
        }
    }
}
