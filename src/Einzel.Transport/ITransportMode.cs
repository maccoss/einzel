using Einzel.Core.Errors;

namespace Einzel.Transport;

/// <summary>
/// How ions are moved through a field: the seam REG-1 requires.
/// </summary>
/// <remarks>
/// <para>
/// REG-1 makes trajectory integration and statistical diffusion <em>peer</em>
/// implementations rather than one being a special case of the other, and spec
/// figure 4 is emphatic about why: these are different descriptions of different
/// physics, not the same calculation at different settings. Above about 10^-2 mbar
/// there are no trajectories to compute; below it there is no density field.
/// </para>
/// <para>
/// The seam exists before its second implementation does, on purpose. A mode
/// selected by name against a registry that knows what is missing gives an agent a
/// refusal that names the gap; a mode selected by an <c>if</c> somewhere in the run
/// command gives it a silent fall-through to whatever was built first.
/// </para>
/// </remarks>
public interface ITransportMode
{
    /// <summary>The name this mode is selected by in a model document.</summary>
    string Name { get; }

    /// <summary>Whether this mode can run.</summary>
    bool IsAvailable { get; }

    /// <summary>Lowest pressure this mode describes, in millibar.</summary>
    double LowerPressureMbar { get; }

    /// <summary>Highest pressure this mode describes, in millibar.</summary>
    double UpperPressureMbar { get; }

    /// <summary>Whether this mode produces trajectories at all.</summary>
    /// <remarks>
    /// TRN-2 and RND-8: a diffusive region emits a time-resolved density field
    /// because that is what it computes, and drawing lines through it would depict
    /// something the model never produced. A renderer asks this rather than
    /// inferring it from the pressure.
    /// </remarks>
    bool ProducesTrajectories { get; }
}

/// <summary>Trajectory integration: one ion at a time, through a deterministic field.</summary>
public sealed class TrajectoryTransport : ITransportMode
{
    /// <summary>The single instance.</summary>
    public static TrajectoryTransport Instance { get; } = new();

    private TrajectoryTransport()
    {
    }

    /// <inheritdoc/>
    public string Name => "trajectory";

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public double LowerPressureMbar => 0.0;

    /// <inheritdoc/>
    public double UpperPressureMbar => Collisions.RegimeDiagnostics.DiffusiveMbar;

    /// <inheritdoc/>
    public bool ProducesTrajectories => true;
}

/// <summary>
/// Statistical diffusion: a density field evolving in time, with no trajectories.
/// </summary>
/// <remarks>
/// Declared and not implemented. It is here rather than absent because the
/// difference between "this mode does not exist" and "you spelled it wrong" is
/// exactly what an agent cannot recover on its own, and because a model that
/// selects it should be refused with the reason rather than run in the other mode.
/// </remarks>
public sealed class DiffusiveTransport : ITransportMode
{
    /// <summary>The single instance.</summary>
    public static DiffusiveTransport Instance { get; } = new();

    private DiffusiveTransport()
    {
    }

    /// <inheritdoc/>
    public string Name => "diffusion";

    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public double LowerPressureMbar => Collisions.RegimeDiagnostics.OverlapMbar;

    /// <inheritdoc/>
    public double UpperPressureMbar => double.PositiveInfinity;

    /// <inheritdoc/>
    public bool ProducesTrajectories => false;
}

/// <summary>The transport modes this engine has, and what it does not.</summary>
public static class TransportModes
{
    /// <summary>Every declared mode, built or not.</summary>
    public static IReadOnlyList<ITransportMode> All { get; } =
        [TrajectoryTransport.Instance, DiffusiveTransport.Instance];

    /// <summary>Resolves a mode by the name a model document uses.</summary>
    /// <param name="name">The declared mode name.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="EinzelException">
    /// The name is unknown, or names a mode that is declared and not built.
    /// </exception>
    /// <remarks>
    /// The two failures are different errors with different suggestions, per AGT-3.
    /// A typo is corrected by spelling it right; an unbuilt mode is not corrected at
    /// all, and saying so is more useful than a list of alternatives that do not do
    /// what was asked.
    /// </remarks>
    public static ITransportMode Resolve(string name)
    {
        foreach (var mode in All)
        {
            if (!string.Equals(mode.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (mode.IsAvailable)
            {
                return mode;
            }

            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.RegimeInvalid,
                Path = "/transport/mode",
                Constraint = $"transport mode '{mode.Name}' is declared but not built",
                Suggestion = "statistical diffusion computes a time-resolved density field rather "
                    + "than trajectories, and needs a mobility input and a density solver that this "
                    + "build does not have. Trajectory integration is the only mode available; it "
                    + "is valid below about 1e-2 mbar and will warn if the declared gas puts the "
                    + "run outside that",
            });
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/transport/mode",
            Constraint = $"'{name}' is not a transport mode",
            Suggestion = $"one of: {string.Join(", ", All.Select(m => m.Name))}",
        });
    }
}
