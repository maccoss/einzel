using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Fields;

/// <summary>
/// Several field elements acting at once, summed.
/// </summary>
/// <remarks>
/// <para>
/// Superposition is exact for electrostatics, so summing element fields and
/// element potentials is the whole implementation. Spec section 10 leans on the
/// same property much harder: solving once per electrode at unit potential turns
/// voltage optimisation from a solve-per-iteration problem into arithmetic.
/// </para>
/// <para>
/// The two structural queries do not sum, and each is handled conservatively. A
/// run is field-free only where every element is field-free, so the shortest run
/// wins. Discontinuities are trickier — see
/// <see cref="SignedDistanceToDiscontinuity"/>.
/// </para>
/// </remarks>
public sealed class SuperposedField : IElectrostaticField, IConductorBounded
{
    private readonly IElectrostaticField[] _elements;

    /// <summary>Creates a superposition.</summary>
    /// <param name="elements">The elements to sum.</param>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
    public SuperposedField(IEnumerable<IElectrostaticField> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _elements = [.. elements];
    }

    /// <summary>The elements being summed.</summary>
    public IReadOnlyList<IElectrostaticField> Elements => _elements;

    /// <inheritdoc/>
    /// <remarks>
    /// The nearest conductor over every element that has any. Potentials
    /// superpose; solid bodies do not, they simply coexist, so this is a union
    /// rather than a sum - which is why it cannot ride on the same loop as the
    /// field itself.
    /// </remarks>
    public double SignedDistanceToConductor(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            if (element is IConductorBounded bounded)
            {
                var distance = bounded.SignedDistanceToConductor(in position);

                if (distance < nearest)
                {
                    nearest = distance;
                }
            }
        }

        return nearest;
    }

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position)
    {
        string? nearestName = null;
        var nearest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            if (element is IConductorBounded bounded)
            {
                var distance = bounded.SignedDistanceToConductor(in position);

                if (distance < nearest)
                {
                    nearest = distance;
                    nearestName = bounded.ConductorAt(in position);
                }
            }
        }

        return nearestName;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var total = Vec3.Zero;

        foreach (var element in _elements)
        {
            total += element.ElectricFieldAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var total = 0.0;

        foreach (var element in _elements)
        {
            total += element.PotentialAt(in position);
        }

        return total;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The finest member governs. A sum is resolved no better than its
    /// least-resolved term, and a step that outruns any one element's grid is
    /// stepping over structure that element holds.
    /// </remarks>
    public double ResolutionLength
    {
        get
        {
            var finest = double.PositiveInfinity;

            foreach (var element in _elements)
            {
                finest = Math.Min(finest, element.ResolutionLength);
            }

            return finest;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A run is field-free only where every element is, so the shortest run
    /// governs. Returning less than the true run length is always safe; returning
    /// more would advance an ion in a straight line through a region where it
    /// should have been accelerating.
    /// </remarks>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction)
    {
        var shortest = double.PositiveInfinity;

        foreach (var element in _elements)
        {
            shortest = Math.Min(shortest, element.FieldFreeRunLength(in position, in direction));

            if (shortest <= 0.0)
            {
                return 0.0;
            }
        }

        return shortest;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The magnitude is the distance to the nearest declared discontinuity; the
    /// sign is the product of the element signs, so crossing any single boundary
    /// flips it and the integrator localises the crossing by bisection.
    /// </para>
    /// <para>
    /// The known limit: a step crossing two boundaries at once returns the sign to
    /// where it started and the crossing is missed. That is benign rather than
    /// silent — the step-size controller sees the resulting error and refines
    /// until the crossings separate — but it means the exact-landing guarantee
    /// holds for one boundary per step, not two. Nothing in schema v0.1 can build
    /// such a geometry; a stacked-ring mirror could, and the fix then is to track
    /// element regions rather than to sum signs.
    /// </para>
    /// </remarks>
    public double SignedDistanceToDiscontinuity(in Vec3 position)
    {
        var nearest = double.PositiveInfinity;
        var sign = 1;

        foreach (var element in _elements)
        {
            var distance = element.SignedDistanceToDiscontinuity(in position);

            if (!double.IsFinite(distance))
            {
                continue;
            }

            nearest = Math.Min(nearest, Math.Abs(distance));
            sign *= distance < 0.0 ? -1 : 1;
        }

        return double.IsPositiveInfinity(nearest) ? double.PositiveInfinity : nearest * sign;
    }
}

/// <summary>
/// Builds engine field objects from a validated model.
/// </summary>
/// <remarks>
/// The seam between the declarative document and the engine. It lives here
/// rather than in Einzel.Core because Core is the innermost assembly and cannot
/// reference the field types built from it.
/// </remarks>
public static class FieldAssembly
{
    /// <summary>Builds the field described by a compiled model.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The assembled field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">A solved element did not converge.</exception>
    /// <remarks>
    /// Throws on a solve that missed its tolerance, rather than handing back a field
    /// that is indistinguishable from a converged one. Callers that produce a
    /// reportable result should use <see cref="BuildReported"/> instead and carry the
    /// warning onto the number, which is what GRD-2 asks for; there is nowhere to
    /// attach a taint on a bare field, so the only honest alternatives here are to
    /// throw or to hide it.
    /// </remarks>
    public static IElectrostaticField Build(CompiledModel model)
    {
        var (field, warnings) = BuildReported(model);

        if (warnings.Count > 0)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.ConvergenceFailed,
                Path = "/fields",
                Constraint = warnings[0].Message,
                Suggestion = "run 'einzel solve' to see the residual and the convergence factor "
                    + "for every element and channel, or use FieldAssembly.BuildReported to carry "
                    + "the warning onto a result instead of failing",
            });
        }

        return field;
    }

    /// <summary>Builds the field, and the warnings its solves earned.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The assembled field, and any non-suppressible warnings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// A solved field that missed its tolerance is not distinguishable from one that
    /// met it by looking at it, so the evidence has to travel separately. The
    /// segmented quadrupole lost its ion at the wrong working point for a whole
    /// revision because the report saying so was discarded at this seam.
    /// </remarks>
    public static (IElectrostaticField Field, IReadOnlyList<Core.Results.ValidityWarning> Warnings)
        BuildReported(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var elements = new List<IElectrostaticField>(model.Fields.Count);
        var warnings = new List<Core.Results.ValidityWarning>();

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var element = model.Fields[index];

            switch (element.Kind)
            {
                case CompiledFieldKind.FieldFree:
                    // Contributes nothing to the sum, and adding it would only make
                    // FieldFreeRunLength do redundant work.
                    break;

                case CompiledFieldKind.Uniform:
                    elements.Add(UniformField.Create(element.Field));
                    break;

                case CompiledFieldKind.HalfSpaceUniform:
                    elements.Add(HalfSpaceUniformField.Create(
                        element.PlanePoint,
                        element.InwardNormal,
                        Quantity.Si(element.PotentialGradientSi, Dimension.ElectricField)));
                    break;

                case CompiledFieldKind.Solved3D:
                {
                    var solve = element.Solve3D!;

                    var geometry = new Solved.Geometry3D(
                        solve.MinX, solve.MinY, solve.MinZ,
                        solve.MaxX, solve.MaxY, solve.MaxZ,
                        solve.CellSize,
                        solve.Electrodes,
                        solve.Tolerance)
                    {
                        Drive = solve.Drive,
                        Stages = solve.Stages,
                    };

                    var (built, report3d) = Solved.GeometryBuilder3D.BuildField(geometry);
                    elements.Add(built);
                    Note(warnings, report3d, index, "solved3d");
                    break;
                }

                case CompiledFieldKind.Solved2D:
                {
                    // The solve happens here, once per build. Nothing about what
                    // the electrodes add up to is known at this level.
                    var (plane, report2d) = Solved.GeometryBuilder.Build(element.Solve!);
                    elements.Add(plane);
                    Note(warnings, report2d, index, "solved2d");
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(model), element.Kind, "unhandled field element kind");
            }
        }

        // Chosen by what the sum contains, not by what the caller asks for. A
        // SuperposedField satisfies only IElectrostaticField, and a driven member
        // answers that interface at t = 0 - so a driven element summed with anything
        // else would silently become a snapshot of the RF at the top of its cycle,
        // with no exception and nothing in the result to say so. Same failure the
        // diffusive mode was found stepping a density through.
        var field = elements.Count switch
        {
            0 => FieldFreeSpace.Instance,
            1 => elements[0],
            _ when elements.Exists(e => e is ITimeVaryingField) =>
                new DrivenSuperposedField(elements),
            _ => (IElectrostaticField)new SuperposedField(elements),
        };

        return (field, warnings);
    }

    /// <summary>Records a solve that missed its tolerance, per GRD-2.</summary>
    /// <remarks>
    /// A validity violation rather than an advisory, so it cannot be suppressed. An
    /// unconverged field is not a result with wider error bars - the residual says
    /// nothing about how far the potential is from the true one, only that the
    /// iteration stopped moving - so every number downstream of it is unquantified
    /// rather than imprecise.
    /// </remarks>
    private static void Note(
        List<Core.Results.ValidityWarning> warnings, Solved.SolveReport report, int index, string kind)
    {
        if (report.Converged)
        {
            return;
        }

        var relative = report.InitialResidual > 0.0
            ? report.FinalResidual / report.InitialResidual
            : 0.0;

        warnings.Add(new Core.Results.ValidityWarning(
            "field.not-converged",
            $"field element {index} ({kind}) stopped at a relative residual of {relative:G3} after "
            + $"{report.Cycles} cycles, at a convergence factor of {report.ConvergenceFactor:F3}. The "
            + "potential it produced is not known to be the solution of the geometry that was declared, "
            + "so nothing computed through it carries its stated accuracy",
            Core.Results.WarningSeverity.ValidityViolation));
    }
}
