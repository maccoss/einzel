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
/// <para>
/// Built as a drift-diffusion solve on a grid: Scharfetter-Gummel fluxes, explicit
/// in time, with mobility as a declared input (TRN-1) and a density field as the
/// output (TRN-2). What is <em>not</em> built is the wiring that lets a model
/// document select it - a source has to become an initial density, a detector a
/// collecting boundary, an electrode an absorbing one - so the mode is available to
/// code and not yet to a model file.
/// </para>
/// <para>
/// It is a peer of trajectory integration rather than a fallback from it. Above
/// about 10^-2 mbar there are no trajectories to compute: each ion has forgotten
/// where it came from long before it arrives, and what survives is a distribution.
/// </para>
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
    public bool IsAvailable => true;

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
                Suggestion = "this mode exists in the engine but is not reachable from a model "
                    + "document yet",
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
