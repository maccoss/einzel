using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Solved;

namespace Einzel.Sweeps;

/// <summary>
/// The cached first-order response of a solved potential to one perturbation
/// channel.
/// </summary>
/// <param name="Parameter">The channel's parameter.</param>
/// <param name="Derivative">
/// The partial derivative of potential with respect to that parameter, in volts
/// per SI unit of the parameter, on the nominal grid.
/// </param>
/// <param name="Step">The finite-difference step the derivative was taken with.</param>
public sealed record SensitivityField(string Parameter, ScalarField2D Derivative, Quantity Step);

/// <summary>What a linearity check found.</summary>
/// <param name="Draws">How many perturbed geometries were fully re-solved.</param>
/// <param name="WorstRelativeResidual">
/// The largest departure of the linearised potential from a full re-solve,
/// relative to the potential scale of the geometry.
/// </param>
/// <param name="WorstFieldResidual">The same for the electric field.</param>
/// <param name="Budget">The bound the residual was tested against.</param>
/// <param name="Passed">Whether every re-solved draw stayed inside the budget.</param>
public sealed record LinearityCheck(
    int Draws,
    double WorstRelativeResidual,
    double WorstFieldResidual,
    double Budget,
    bool Passed);

/// <summary>
/// First-order sensitivity fields, and the check that gates their use.
/// </summary>
/// <remarks>
/// <para>
/// FLD-1: for each perturbation channel, cache the partial derivative of the
/// potential by finite difference over a full re-solve. Then any perturbed
/// geometry within the tolerance range is a weighted sum rather than a solve.
/// </para>
/// <para>
/// The arithmetic is what makes tolerance work possible at all. Basis
/// superposition handles a change of voltages exactly, and stops the moment the
/// geometry moves, because the basis fields are solutions on one mesh. A
/// thousand-draw study over perturbed geometry would otherwise need a thousand
/// solve campaigns — the specification puts that at roughly three weeks of
/// compute for one study. With sensitivity fields it is one campaign of
/// (channels + 1) solves and then a weighted sum per draw.
/// </para>
/// <para>
/// FLD-2 is the part that keeps it honest, and it **gates rather than annotates**:
/// a stratified subset of draws is fully re-solved and compared against the
/// linearised field, and if the residual exceeds the budget the study is marked
/// invalid rather than qualified. The premise — that a hundred to three hundred
/// microns against a ten millimetre standoff is a one to three percent
/// perturbation and therefore linear — is an assumption and not a theorem, which
/// is exactly why the subset is not optional.
/// </para>
/// </remarks>
public static class SensitivityFields
{
    /// <summary>
    /// Builds a solved geometry from a model at given parameter overrides.
    /// </summary>
    /// <param name="document">The model.</param>
    /// <param name="overrides">Parameter overrides, or null for nominal.</param>
    /// <param name="fieldIndex">Which field element to solve; it must be a solved element.</param>
    /// <returns>The solved potential and the geometry it came from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    /// <exception cref="ArgumentException">The named field element is not a solved one.</exception>
    public static (ScalarField2D Potential, CompiledSolvedField Geometry) SolveAt(
        ModelDocument document, IReadOnlyDictionary<string, Quantity>? overrides, int fieldIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(document);

        var validation = ModelValidator.Validate(document, overrides);

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors[0]);
        }

        var element = validation.Model!.Fields[fieldIndex];

        if (element.Kind != CompiledFieldKind.Solved2D || element.Solve is null)
        {
            throw new ArgumentException(
                $"field element {fieldIndex} is {element.Kind}, not a solved geometry; sensitivity fields "
                + "apply only to geometry that is solved",
                nameof(fieldIndex));
        }

        var grid = GeometryBuilder.BuildGrid(element.Solve);
        var mask = GeometryBuilder.BuildMask(element.Solve, grid);
        var (potential, _) = PoissonSolver2D.Solve(
            mask,
            element.Solve.Tolerance,
            maximumCycles: 400,
            coarsen: coarse => GeometryBuilder.BuildMask(element.Solve, coarse));

        return (potential, element.Solve);
    }

    /// <summary>
    /// Computes one sensitivity field per channel, by central difference over full
    /// re-solves.
    /// </summary>
    /// <param name="document">The model.</param>
    /// <param name="channels">The channels to differentiate with respect to.</param>
    /// <param name="fieldIndex">Which field element to solve.</param>
    /// <param name="stepFraction">
    /// The finite-difference step, as a fraction of each channel's half-width.
    /// </param>
    /// <returns>The nominal potential and one derivative field per channel.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// Central rather than forward difference: the derivative is second-order
    /// accurate for twice the solves, and since this campaign runs once and is
    /// then reused across every draw, accuracy is worth far more than the saving.
    /// </para>
    /// <para>
    /// The step is a fraction of the channel's own half-width rather than a fixed
    /// size, so a channel spanning microns and one spanning volts are both
    /// differentiated at a sensible scale. Too small a step and the difference is
    /// dominated by the solver tolerance; too large and it is not a derivative.
    /// </para>
    /// <para>
    /// Every perturbed solve must land on the same grid as the nominal, or the
    /// fields cannot be subtracted. A perturbation that changes the domain size
    /// enough to change the interval count would silently produce a different
    /// grid, so that is checked rather than assumed.
    /// </para>
    /// </remarks>
    public static (ScalarField2D Nominal, IReadOnlyList<SensitivityField> Fields) Build(
        ModelDocument document,
        IReadOnlyList<PerturbationChannel> channels,
        int fieldIndex = 0,
        double stepFraction = 0.5)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepFraction);

        var (nominal, _) = SolveAt(document, null, fieldIndex);

        var baseline = ModelValidator.Validate(document, null).Model!;
        var derivatives = new List<SensitivityField>(channels.Count);

        for (var c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            var parameter = channel.Bind(baseline.Parameters, $"/channels/{c}");
            var step = channel.HalfWidth.SiValue * stepFraction;

            if (step <= 0.0)
            {
                // A channel held fixed has no sensitivity, and differencing it
                // would divide by zero.
                derivatives.Add(new SensitivityField(
                    channel.Parameter,
                    new ScalarField2D(nominal.Grid),
                    Quantity.Si(0.0, parameter.Value.Dimension)));

                continue;
            }

            var dimension = parameter.Value.Dimension;

            var (high, _) = SolveAt(document, Override(channel.Parameter, parameter.Value, step, dimension), fieldIndex);
            var (low, _) = SolveAt(document, Override(channel.Parameter, parameter.Value, -step, dimension), fieldIndex);

            RequireSameGrid(nominal, high, channel.Parameter);
            RequireSameGrid(nominal, low, channel.Parameter);

            var derivative = new ScalarField2D(nominal.Grid);
            var scale = 1.0 / (2.0 * step);
            var moved = false;

            for (var k = 0; k < derivative.Values.Length; k++)
            {
                var difference = high.Values[k] - low.Values[k];
                derivative.Values[k] = difference * scale;

                if (difference != 0.0)
                {
                    moved = true;
                }
            }

            RequireTheGeometryMoved(moved, channel, step, nominal.Grid.Spacing);

            derivatives.Add(new SensitivityField(
                channel.Parameter, derivative, Quantity.Si(step, dimension)));
        }

        return (nominal, derivatives);
    }

    /// <summary>Builds a perturbed potential by superposition, without solving.</summary>
    /// <param name="nominal">The nominal potential.</param>
    /// <param name="fields">The sensitivity fields.</param>
    /// <param name="offsets">Parameter offsets from nominal, in SI.</param>
    /// <returns>The linearised potential.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static ScalarField2D Linearise(
        ScalarField2D nominal,
        IReadOnlyList<SensitivityField> fields,
        IReadOnlyDictionary<string, double> offsets)
    {
        ArgumentNullException.ThrowIfNull(nominal);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(offsets);

        var result = nominal.Clone();

        foreach (var field in fields)
        {
            if (offsets.TryGetValue(field.Parameter, out var offset) && offset != 0.0)
            {
                result.AddScaled(field.Derivative, offset);
            }
        }

        return result;
    }

    /// <summary>
    /// Re-solves a stratified subset of perturbed geometries and compares them
    /// against the linearised field.
    /// </summary>
    /// <param name="document">The model.</param>
    /// <param name="channels">The channels.</param>
    /// <param name="nominal">The nominal potential.</param>
    /// <param name="fields">The sensitivity fields.</param>
    /// <param name="budget">
    /// Relative residual the linearisation must stay within. ACC-1's 1 ppm is the
    /// value FLD-2 names.
    /// </param>
    /// <param name="draws">How many geometries to re-solve.</param>
    /// <param name="seed">Seed for the draw sequence.</param>
    /// <param name="fieldIndex">Which field element.</param>
    /// <returns>What the check found.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// Draws are stratified toward the tails rather than sampled uniformly. The
    /// linearisation is exact at nominal and worst at the extremes, so sampling
    /// the middle would measure the case that cannot fail and report it as
    /// evidence about the case that can.
    /// </remarks>
    public static LinearityCheck Check(
        ModelDocument document,
        IReadOnlyList<PerturbationChannel> channels,
        ScalarField2D nominal,
        IReadOnlyList<SensitivityField> fields,
        double budget = 1e-6,
        int draws = 8,
        int seed = 1,
        int fieldIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(nominal);
        ArgumentNullException.ThrowIfNull(fields);

        var baseline = ModelValidator.Validate(document, null).Model!;
        var random = new Random(seed);

        var scale = PotentialScale(nominal);
        var worstPotential = 0.0;
        var worstField = 0.0;
        var performed = 0;

        for (var d = 0; d < draws; d++)
        {
            var overrides = new Dictionary<string, Quantity>(StringComparer.Ordinal);
            var offsets = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var channel in channels)
            {
                var parameter = baseline.Parameters[channel.Parameter];

                // Stratified to the tails: the sign is random, the magnitude sits
                // in the outer half of the range where the linearisation is
                // weakest.
                var magnitude = channel.HalfWidth.SiValue * (0.5 + (0.5 * random.NextDouble()));
                var offset = random.Next(2) == 0 ? -magnitude : magnitude;

                offsets[channel.Parameter] = offset;
                overrides[channel.Parameter] = Quantity.Si(parameter.SiValue + offset, parameter.Dimension);
            }

            ScalarField2D exact;

            try
            {
                (exact, _) = SolveAt(document, overrides, fieldIndex);
            }
            catch (Core.Errors.EinzelException)
            {
                // A draw that will not validate cannot be compared; the sweep
                // itself records it as a failed row.
                continue;
            }

            // A moved mesh is a design error in the study, not a failed draw, so
            // it is raised rather than skipped.
            RequireSameGrid(nominal, exact, string.Join(", ", offsets.Keys));

            var linear = Linearise(nominal, fields, offsets);
            performed++;

            for (var k = 0; k < exact.Values.Length; k++)
            {
                worstPotential = Math.Max(worstPotential, Math.Abs(linear.Values[k] - exact.Values[k]) / scale);
            }

            worstField = Math.Max(worstField, FieldResidual(linear, exact, scale));
        }

        return new LinearityCheck(
            performed, worstPotential, worstField, budget,
            performed > 0 && worstPotential <= budget && worstField <= budget);
    }

    /// <summary>
    /// Refuses a channel whose perturbation is invisible to the mesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The most dangerous outcome this code can produce, and it is silent. An
    /// electrode is rasterised onto grid nodes, so moving it by less than a cell
    /// changes which nodes it occupies not at all: the perturbed solve comes back
    /// bit-identical to the nominal, the difference is exactly zero, and the
    /// derivative field is identically zero. A tolerance study built on it would
    /// then report that the parameter has no effect — that the tolerance does not
    /// matter — which is not a small error but the opposite of the truth, arrived
    /// at without a single warning.
    /// </para>
    /// <para>
    /// Measured on a plate in a 0.469 mm mesh: a 0.1 mm half-width gave a residual
    /// of exactly zero, and it would have been read as perfect linearity rather
    /// than as a perturbation the model never saw.
    /// </para>
    /// <para>
    /// So it is an error rather than a warning. There is no correct number to
    /// return, and the caller has to change something real — refine the mesh until
    /// the tolerance spans several cells, or use a boundary representation whose
    /// discrete operator varies smoothly with position rather than in steps.
    /// </para>
    /// </remarks>
    private static void RequireTheGeometryMoved(
        bool moved, PerturbationChannel channel, double step, double spacing)
    {
        if (moved)
        {
            return;
        }

        throw new ArgumentException(
            $"perturbing '{channel.Parameter}' by {step:G4} SI units changed nothing: the finite-difference "
            + $"step is smaller than the mesh spacing of {spacing:G4} m, so the rasterised geometry occupied "
            + "identical nodes and the derivative is identically zero. Left unchecked this reports a parameter "
            + "as having no influence, which is the opposite of what a sub-cell tolerance usually means. Refine "
            + "the mesh until the perturbation spans several cells, or represent the boundary in a way that "
            + "moves continuously with the parameter.",
            nameof(channel));
    }

    private static Dictionary<string, Quantity> Override(
        string name, Quantity nominal, double offset, Core.Units.Dimension dimension) =>
        new(StringComparer.Ordinal) { [name] = Quantity.Si(nominal.SiValue + offset, dimension) };

    /// <summary>
    /// Refuses a perturbation that moved the mesh rather than the geometry on it.
    /// </summary>
    /// <remarks>
    /// Node counts are not enough, and assuming they were produced a silent
    /// wrong answer: a domain whose extent is itself a perturbed parameter keeps
    /// the same interval count while rescaling the spacing, so node <c>k</c> of
    /// the perturbed solve sits at a different physical place from node <c>k</c>
    /// of the nominal. Subtracting them then differences two unrelated points and
    /// reports the result as a derivative. It presented as a residual of 0.23 —
    /// twenty-three percent of the potential scale — at a perturbation of one part
    /// in a thousand, and, tellingly, one that did not grow as the perturbation
    /// grew. Origin and spacing are compared too.
    /// </remarks>
    private static void RequireSameGrid(ScalarField2D nominal, ScalarField2D perturbed, string parameter)
    {
        var a = nominal.Grid;
        var b = perturbed.Grid;

        var moved = b.CountX != a.CountX
            || b.CountY != a.CountY
            || Math.Abs(b.Spacing - a.Spacing) > a.Spacing * 1e-12
            || Math.Abs(b.OriginX - a.OriginX) > a.Spacing * 1e-12
            || Math.Abs(b.OriginY - a.OriginY) > a.Spacing * 1e-12;

        if (moved)
        {
            throw new ArgumentException(
                $"perturbing '{parameter}' moved the mesh, from {a} at ({a.OriginX:G6}, {a.OriginY:G6}) to "
                + $"{b} at ({b.OriginX:G6}, {b.OriginY:G6}). Sensitivity fields are node-by-node differences "
                + "between potentials on one mesh, so the mesh must not move with the parameter. Perturb the "
                + "position of geometry inside a fixed solve domain, not the extent of the domain itself, and "
                + "keep the cell size a constant rather than deriving it from a perturbed dimension.",
                nameof(parameter));
        }
    }

    private static double PotentialScale(ScalarField2D potential)
    {
        var peak = 0.0;

        foreach (var value in potential.Values)
        {
            peak = Math.Max(peak, Math.Abs(value));
        }

        return peak > 0.0 ? peak : 1.0;
    }

    /// <summary>
    /// The largest disagreement in the gradient, which is what an ion actually
    /// feels, relative to the potential scale over a cell.
    /// </summary>
    private static double FieldResidual(ScalarField2D linear, ScalarField2D exact, double scale)
    {
        var grid = linear.Grid;
        var worst = 0.0;

        for (var j = 1; j < grid.CountY - 1; j++)
        {
            for (var i = 1; i < grid.CountX - 1; i++)
            {
                var dxLinear = linear[i + 1, j] - linear[i - 1, j];
                var dxExact = exact[i + 1, j] - exact[i - 1, j];
                var dyLinear = linear[i, j + 1] - linear[i, j - 1];
                var dyExact = exact[i, j + 1] - exact[i, j - 1];

                worst = Math.Max(worst, Math.Abs(dxLinear - dxExact) / (2.0 * scale));
                worst = Math.Max(worst, Math.Abs(dyLinear - dyExact) / (2.0 * scale));
            }
        }

        return worst;
    }
}
