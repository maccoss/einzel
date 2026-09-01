using Einzel.Core.Geometry;
using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>A three-dimensional solved field, sampled by tricubic interpolation.</summary>
/// <remarks>
/// The first field in this engine with no symmetry behind it. A cross-section
/// assumes the geometry is the same all along the third axis and an axisymmetric
/// solve assumes it is the same all the way round; this assumes nothing, which is
/// what a segmented quadrupole, an auxiliary DC wedge or a bent flight tube needs
/// and what neither of the others can express.
/// </remarks>
public sealed class SolvedField3D : IElectrostaticField, IConductorBounded
{
    private readonly TricubicInterpolant _interpolant;
    private readonly Grid3D _grid;
    private readonly CompiledElectrode3D[] _conductors;

    /// <summary>Wraps a solved potential.</summary>
    /// <param name="potential">The nodal potentials.</param>
    /// <param name="conductors">
    /// The electrodes, as solid bodies an ion can strike. Omit for a geometry not
    /// meant to block anything.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="potential"/> is null.</exception>
    public SolvedField3D(ScalarField3D potential, IReadOnlyList<CompiledElectrode3D>? conductors = null)
    {
        ArgumentNullException.ThrowIfNull(potential);

        _grid = potential.Grid;
        _interpolant = new TricubicInterpolant(potential);
        _conductors = conductors is null ? [] : [.. conductors];
    }

    /// <summary>The grid the potential was solved on.</summary>
    public Grid3D Grid => _grid;

    /// <inheritdoc/>
    public double ResolutionLength => _grid.MinimumSpacing;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        // OUTSIDE THE BOX THERE IS NO FIELD, RATHER THAN AN EXTRAPOLATED ONE.
        //
        // A tricubic asked for a point it was never fitted over does not decline - it
        // continues the cubic, and a cubic continued explodes. Measured on a 20 mm box
        // holding one plate at 100 V: half a millimetre out it reported -1.6 V, fourteen
        // millimetres out -283 V, and 180 mm out -486,643 V at 8.1 MV/m. That is four
        // orders past the applied potential and violates the maximum principle, which is
        // the cheapest exact check there is that a solve has not gone wrong.
        //
        // The consequence is not academic: an ion that leaves a volume domain was being
        // accelerated by a field nobody declared. An Astral skeleton whose ion escaped its
        // 635 mm analyser was found 4,643 mm away, which reads as coasting and was not.
        //
        // The plane path has always done this - `SolvedField2D` carries an outside
        // potential and returns zero field beyond its grid - and it is what makes
        // superposing a solved half with its mirror image a union rather than a sum.
        if (!_grid.Contains(position.X, position.Y, position.Z))
        {
            return Vec3.Zero;
        }

        _interpolant.Gradient(position.X, position.Y, position.Z, out var gx, out var gy, out var gz);

        return new Vec3(-gx, -gy, -gz);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Zero outside the solved box, for the reason <see cref="ElectricFieldAt"/> gives.
    /// Zero rather than the nearest boundary value, because a volume solve's domain is
    /// declared to enclose the geometry: what is outside it is vacuum the document did not
    /// describe, and a constant offset there would add an energy no electrode supplied.
    /// </remarks>
    public double PotentialAt(in Vec3 position) =>
        _grid.Contains(position.X, position.Y, position.Z)
            ? _interpolant.Value(position.X, position.Y, position.Z)
            : 0.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Zero, always. A solved field is nowhere exactly field-free, and claiming
    /// otherwise is a guarantee this cannot honour - the guarantee is that the field
    /// is identically zero over the whole run, not merely small.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    /// <inheritdoc/>
    /// <remarks>
    /// Positive infinity: the domain edge is not declared a discontinuity. A solve
    /// whose field has decayed at its boundary has no jump there, and two phantom
    /// surfaces a few microns apart defeat the sign-product tracking that
    /// superposition uses - which cost an ion 2.6e-4 of its energy in two dimensions
    /// and presented as an intermittent transmission loss.
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;

    /// <inheritdoc/>
    public double SignedDistanceToConductor(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;

        foreach (var conductor in _conductors)
        {
            var distance = conductor.SignedDistance(position.X, position.Y, position.Z);

            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position)
    {
        string? nearestName = null;
        var nearest = double.PositiveInfinity;

        foreach (var conductor in _conductors)
        {
            var distance = conductor.SignedDistance(position.X, position.Y, position.Z);

            if (distance < nearest)
            {
                nearest = distance;
                nearestName = conductor.Name;
            }
        }

        return nearestName;
    }
}
