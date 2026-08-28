namespace Einzel.Transport.Diffusion;

/// <summary>
/// Grid cells that swallow whatever reaches them, each named by what it is.
/// </summary>
/// <remarks>
/// <para>
/// Interior geometry as a boundary. In the trajectory description an electrode
/// stops an ion by being a surface the flight lands on; in this one there are no
/// flights, so an electrode is a region the density flows into and does not come
/// back from. Same requirement - ACC-5 wants losses itemised by the surface name
/// the model author wrote - reached by a different mechanism.
/// </para>
/// <para>
/// Deliberately not an electrode, a shape, or anything that knows about geometry.
/// The solver is told which nodes absorb and what to call them; deciding that a
/// node is inside a ring is the caller's job, which keeps architecture invariant 2
/// intact - a solver that could test a point against an electrode is one step from
/// a solver that knows what a funnel is.
/// </para>
/// </remarks>
public sealed class AbsorbingCells
{
    private readonly int[] _owner;

    /// <summary>Nothing absorbs.</summary>
    public static AbsorbingCells None { get; } = new([], []);

    /// <summary>Creates a set of absorbing cells.</summary>
    /// <param name="owner">
    /// One entry per grid node, row-major: the index into <paramref name="names"/>
    /// of whatever occupies it, or -1 where the node is open.
    /// </param>
    /// <param name="names">What each absorber is called.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An owner index names no absorber.</exception>
    public AbsorbingCells(int[] owner, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(names);

        foreach (var index in owner)
        {
            // Both ends. Only the upper bound was checked, so -2 read as open and an
            // owner map built wrong in that direction would have produced a field
            // with fewer absorbers than the geometry declared, silently. -1 is the
            // one negative value that means anything here.
            if (index < -1 || index >= names.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(owner),
                    index,
                    $"an absorbing cell names surface {index}; "
                    + (names.Count == 0
                        ? "no absorbers were given, so -1 for an open node is the only value "
                            + "that means anything"
                        : $"the only values that mean anything are -1 for an open node and "
                            + $"0 to {names.Count - 1}"));
            }
        }

        _owner = owner;
        Names = names;
    }

    /// <summary>What each absorber is called.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Whether anything absorbs at all.</summary>
    /// <remarks>
    /// Whether any <em>node</em> is owned, not whether a map was supplied. An
    /// electrode declared outside the density grid gives a named absorber and an
    /// owner map with nothing in it, and reporting that as "something absorbs" would
    /// be describing the document rather than the solve.
    /// </remarks>
    public bool Any => Array.Exists(_owner, index => index >= 0);

    /// <summary>Whether a node is inside an absorber.</summary>
    /// <param name="index">The node, row-major.</param>
    /// <returns>True when it is.</returns>
    public bool Blocks(int index) => _owner.Length > 0 && _owner[index] >= 0;

    /// <summary>Which absorber occupies a node.</summary>
    /// <param name="index">The node, row-major.</param>
    /// <returns>The name, or null where the node is open.</returns>
    public string? NameAt(int index) =>
        _owner.Length > 0 && _owner[index] >= 0 ? Names[_owner[index]] : null;
}
