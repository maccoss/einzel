using Einzel.Core.Errors;
using Einzel.Core.Units;
using Einzel.Sweeps;

namespace Einzel.Commands;

/// <summary>One parameter a tolerance study varies, as it appears in a file.</summary>
public sealed record ChannelDocument
{
    /// <summary>The declared parameter this channel perturbs.</summary>
    public string? Parameter { get; init; }

    /// <summary>Half-width of the perturbation.</summary>
    public double HalfWidth { get; init; }

    /// <summary>Unit of the half-width; must match the parameter's dimension.</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Either <c>uniform</c> or <c>normal</c>.
    /// </summary>
    /// <remarks>
    /// A machining tolerance quoted as plus or minus something is a statement
    /// about a bound, not about a distribution, and which one is assumed changes
    /// the answer. Uniform treats the bound as equally likely anywhere inside,
    /// which is what a tolerance usually means when it is written on a drawing.
    /// Normal takes the half-width as one standard deviation, which puts about a
    /// third of the draws outside the stated bound - that is the point of asking
    /// for it, and it is worth being deliberate about which one a study means.
    /// </remarks>
    public string Distribution { get; init; } = "uniform";
}

/// <summary>One parameter an optimisation searches, as it appears in a file.</summary>
public sealed record VariableDocument
{
    /// <summary>The declared parameter this variable searches.</summary>
    public string? Parameter { get; init; }

    /// <summary>Lower bound. Omit to use the bound the model declares.</summary>
    public double? Minimum { get; init; }

    /// <summary>Upper bound. Omit to use the bound the model declares.</summary>
    public double? Maximum { get; init; }

    /// <summary>Unit of the bounds; required when either is given.</summary>
    public string? Unit { get; init; }
}

/// <summary>
/// A study: what to vary, how much, and what to record.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 13: "A study is a file in studies/ declaring perturbation channels
/// with distributions, a draw count, a seed, an ensemble specification, and
/// figures of merit to record."
/// </para>
/// <para>
/// One document type covers both a tolerance sweep and an optimisation, because
/// they differ only in what they do with the parameters - and keeping them in one
/// schema means a study that established which parameter binds first can be turned
/// into the search that tunes it by changing one word.
/// </para>
/// </remarks>
public sealed record StudyDocument
{
    /// <summary>The schema version this document is written against.</summary>
    public string SchemaVersion { get; init; } = "0.1";

    /// <summary>A short name, used in output and in generated file names.</summary>
    public string? Name { get; init; }

    /// <summary>Prose description, carried through to results.</summary>
    public string? Description { get; init; }

    /// <summary>The model to study, relative to this file.</summary>
    public string? Model { get; init; }

    /// <summary>Which figure of merit to record. See <see cref="FiguresOfMerit"/>.</summary>
    public string? FigureOfMerit { get; init; }

    /// <summary>Fractional energy spread for the ensemble figures of merit.</summary>
    public double EnergySpread { get; init; } = FiguresOfMerit.DefaultEnergySpread;

    /// <summary>How many ions the ensemble figures of merit launch.</summary>
    public int Ions { get; init; } = FiguresOfMerit.DefaultIons;

    /// <summary>Tolerance sweep: the parameters to perturb.</summary>
    public IReadOnlyList<ChannelDocument>? Channels { get; init; }

    /// <summary>Tolerance sweep: how many stochastic draws.</summary>
    public int Draws { get; init; } = 200;

    /// <summary>Tolerance sweep: whether to run the one-at-a-time attribution pass.</summary>
    public bool OneAtATime { get; init; } = true;

    /// <summary>Seed for anything stochastic, so a run is regenerable (PRJ-3).</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Optimisation: the parameters to search.</summary>
    public IReadOnlyList<VariableDocument>? Variables { get; init; }

    /// <summary>Optimisation: <c>nelderMead</c> or <c>cmaEs</c>.</summary>
    public string Algorithm { get; init; } = "nelderMead";

    /// <summary>Optimisation: <c>minimise</c>, <c>maximise</c>, or omit to use the figure's own sense.</summary>
    public string? Sense { get; init; }

    /// <summary>Optimisation: ceiling on objective evaluations.</summary>
    public int MaximumEvaluations { get; init; } = 200;

    /// <summary>Optimisation: convergence tolerance on the parameters, as a fraction of the box.</summary>
    public double ParameterTolerance { get; init; } = 1e-4;

    /// <summary>
    /// Optimisation: convergence tolerance on the objective.
    /// </summary>
    /// <remarks>
    /// Set this to the objective's own noise level and not below. Every evaluation
    /// here ends in a field solve at a finite tolerance, so the objective has grit
    /// on it; a tolerance under that asks the search to resolve noise, and it will
    /// spend its whole budget doing so and then report that it never converged.
    /// </remarks>
    public double ObjectiveTolerance { get; init; } = 1e-8;
}

/// <summary>Turns a study document into the objects the sweep drivers take.</summary>
public static class StudyBinding
{
    /// <summary>The perturbation channels a tolerance sweep runs.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The channels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">A channel is incomplete or names an unknown distribution.</exception>
    public static IReadOnlyList<PerturbationChannel> Channels(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Channels is not { Count: > 0 })
        {
            throw Missing("channels", "a tolerance sweep needs at least one channel to perturb");
        }

        var channels = new List<PerturbationChannel>(study.Channels.Count);

        for (var index = 0; index < study.Channels.Count; index++)
        {
            var declared = study.Channels[index];

            if (string.IsNullOrWhiteSpace(declared.Parameter))
            {
                throw Missing($"channels/{index}/parameter", "every channel names the parameter it perturbs");
            }

            if (string.IsNullOrWhiteSpace(declared.Unit))
            {
                // SI internally, units explicit at every boundary. A bare number
                // here is the commonest source of silent wrongness and an agent
                // writing from prose is the likeliest to introduce it.
                throw Missing(
                    $"channels/{index}/unit",
                    "a half-width needs a unit; '0.1' could be a tenth of a millimetre or a tenth of a metre");
            }

            channels.Add(new PerturbationChannel(
                declared.Parameter,
                Quantity.From(declared.HalfWidth, declared.Unit),
                Distribution(declared.Distribution, index)));
        }

        return channels;
    }

    /// <summary>The design variables an optimisation searches.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The variables.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">A variable is incomplete.</exception>
    public static IReadOnlyList<DesignVariable> Variables(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Variables is not { Count: > 0 })
        {
            throw Missing("variables", "an optimisation needs at least one variable to search");
        }

        var variables = new List<DesignVariable>(study.Variables.Count);

        for (var index = 0; index < study.Variables.Count; index++)
        {
            var declared = study.Variables[index];

            if (string.IsNullOrWhiteSpace(declared.Parameter))
            {
                throw Missing($"variables/{index}/parameter", "every variable names the parameter it searches");
            }

            if ((declared.Minimum is not null || declared.Maximum is not null)
                && string.IsNullOrWhiteSpace(declared.Unit))
            {
                throw Missing($"variables/{index}/unit", "a bound given here needs a unit");
            }

            variables.Add(new DesignVariable(
                declared.Parameter,
                declared.Minimum is { } low ? Quantity.From(low, declared.Unit!) : null,
                declared.Maximum is { } high ? Quantity.From(high, declared.Unit!) : null));
        }

        return variables;
    }

    /// <summary>Which search a study asked for.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The algorithm.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">The algorithm is not one this build has.</exception>
    public static OptimisationAlgorithm Algorithm(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        return study.Algorithm switch
        {
            "nelderMead" => OptimisationAlgorithm.NelderMead,
            "cmaEs" => OptimisationAlgorithm.CmaEs,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/algorithm",
                Constraint = $"'{study.Algorithm}' is not a search this build has",
                Suggestion = "'nelderMead' for a handful of variables, 'cmaEs' for more of them or a "
                    + "rougher objective",
            }),
        };
    }

    /// <summary>Which way an optimisation is better.</summary>
    /// <param name="study">The study.</param>
    /// <param name="figure">The figure of merit being optimised.</param>
    /// <returns>The sense.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">The sense is not one of the two.</exception>
    /// <remarks>
    /// Defaulted from the figure of merit rather than required, because a figure
    /// of merit knows which way is better and making the caller restate it is an
    /// invitation to state it wrongly. A study may still override, since minimising
    /// a resolving power is a legitimate thing to ask for when hunting a bug.
    /// </remarks>
    public static ObjectiveSense Sense(StudyDocument study, FigureOfMeritInfo figure)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(figure);

        return study.Sense switch
        {
            null => figure.LargerIsBetter ? ObjectiveSense.Maximise : ObjectiveSense.Minimise,
            "maximise" or "maximize" => ObjectiveSense.Maximise,
            "minimise" or "minimize" => ObjectiveSense.Minimise,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sense",
                Constraint = $"'{study.Sense}' is not a direction",
                Suggestion = "'minimise' or 'maximise', or omit it and the figure of merit decides",
            }),
        };
    }

    private static PerturbationDistribution Distribution(string declared, int index) => declared switch
    {
        "uniform" => PerturbationDistribution.Uniform,
        "normal" => PerturbationDistribution.Normal,
        _ => throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = $"/channels/{index}/distribution",
            Constraint = $"'{declared}' is not a distribution this build knows",
            Suggestion = "'uniform' treats the half-width as a bound; 'normal' treats it as one standard "
                + "deviation",
        }),
    };

    private static EinzelException Missing(string path, string constraint) => new(new EinzelError
    {
        Code = ErrorCodes.SchemaInvalid,
        Path = "/" + path,
        Constraint = constraint,
        Suggestion = "run 'einzel schema --study' for the shape of a study file",
    });
}
