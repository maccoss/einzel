using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>What the conductors of a static solve hold: each at its declared potential.</summary>
/// <remarks>
/// <para>
/// <b>A held source of its own, handed to the leaf with the conductors' shape</b>
/// (<see cref="ConductorLeaf.Of"/>), rather than a role the solved field plays when a builder
/// remembers to set a flag on it. The solved field was the holder, gated by an init-only
/// <c>HoldsDeclaredPotentials</c>: a builder that forgot it got a field whose conductors held
/// nothing, silently, and the probe drew the interpolant inside the metal. Now a static solve's
/// leaf cannot be made without saying what its conductors hold, and a driven solve's leaf is
/// held by the driven field, whose coefficients are required.
/// </para>
/// <para>
/// <b>By name, in one lookup.</b> The potentials are gathered into a map once, where each
/// sample in metal used to scan the electrode list by name - up to a million times a surface.
/// </para>
/// <para>
/// <b>An edge-profiled board holds no single potential</b>, so it has no entry and is null
/// here, as the driven path leaves it out of its coefficients. Its own <c>potential</c> is null
/// in every shipped model and compiles to zero, so an entry would claim the board holds 0 V.
/// </para>
/// </remarks>
public sealed class StaticHeldConductors : IHeldConductors
{
    private readonly Dictionary<string, double> _potentials;

    private StaticHeldConductors(Dictionary<string, double> potentials) => _potentials = potentials;

    /// <summary>The held source of a static cross-section or axisymmetric solve.</summary>
    /// <param name="electrodes">The solve's electrodes, as declared.</param>
    /// <returns>Each electrode at its declared potential; an edge-profiled board absent.</returns>
    public static StaticHeldConductors Of(IReadOnlyList<CompiledElectrode> electrodes)
    {
        ArgumentNullException.ThrowIfNull(electrodes);

        var potentials = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var electrode in electrodes)
        {
            if (electrode.Shape != ElectrodeShape.EdgeProfile)
            {
                // Names are unique within a solve (the validator refuses a repeat); the first
                // is kept, as the walk of the conductors keeps the first on a tie.
                potentials.TryAdd(electrode.Name, electrode.Potential);
            }
        }

        return new StaticHeldConductors(potentials);
    }

    /// <summary>The held source of a static volume solve.</summary>
    /// <param name="electrodes">The solve's electrodes, as declared.</param>
    /// <returns>Each electrode at its declared potential.</returns>
    public static StaticHeldConductors Of(IReadOnlyList<CompiledElectrode3D> electrodes)
    {
        ArgumentNullException.ThrowIfNull(electrodes);

        var potentials = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var electrode in electrodes)
        {
            potentials.TryAdd(electrode.Name, electrode.Potential);
        }

        return new StaticHeldConductors(potentials);
    }

    /// <inheritdoc/>
    public double? HeldPotentialOf(string conductor, double timeSeconds) =>
        _potentials.TryGetValue(conductor, out var potential) ? potential : null;

    /// <inheritdoc/>
    /// <remarks>A static solve holds the same potential at every instant, so its mean is that potential.</remarks>
    public double? HeldCycleMeanOf(string conductor, double fromSeconds, double periodSeconds, int samples) =>
        HeldPotentialOf(conductor, fromSeconds);
}
