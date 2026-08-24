using Einzel.Core.Results;

namespace Einzel.Io;

/// <summary>The GRD-1 envelope, in the shape it takes on the wire.</summary>
/// <remarks>
/// <para>
/// Built only through <see cref="From"/>, which obtains the value by
/// deconstructing a <see cref="Measured"/> — and deconstruction hands back the
/// uncertainty, the evidence, and the warnings in the same call. There is
/// therefore no way to write a serialiser here that emits the number alone, which
/// is GRD-1 holding at the boundary rather than only inside the engine.
/// </para>
/// <para>
/// GRD-2 continues past this point: the warnings travel into CLI output, MCP
/// responses, exported files, and rendered figures unchanged.
/// </para>
/// </remarks>
public sealed record MeasuredJson
{
    /// <summary>The magnitude, expressed in <see cref="Unit"/>.</summary>
    public required double Value { get; init; }

    /// <summary>The unit the magnitude is expressed in.</summary>
    public required string Unit { get; init; }

    /// <summary>The uncertainty interval.</summary>
    public required UncertaintyJson Uncertainty { get; init; }

    /// <summary>What stands behind the value.</summary>
    public required EvidenceJson Evidence { get; init; }

    /// <summary>Active warnings.</summary>
    public required IReadOnlyList<WarningJson> Warnings { get; init; }

    /// <summary>Projects a result envelope into a named unit.</summary>
    /// <param name="measured">The envelope.</param>
    /// <param name="unit">The unit to express the value in.</param>
    /// <returns>The wire form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measured"/> is null.</exception>
    public static MeasuredJson From(Measured measured, string unit)
    {
        ArgumentNullException.ThrowIfNull(measured);

        var (value, uncertainty, evidence, warnings) = measured;
        var scale = Core.Units.UnitRegistry.Resolve(unit).SiFactor;

        return new MeasuredJson
        {
            Value = value.In(unit),
            Unit = unit,
            Uncertainty = new UncertaintyJson
            {
                Lower = uncertainty.LowerSi / scale,
                Upper = uncertainty.UpperSi / scale,
                ConfidenceLevel = uncertainty.ConfidenceLevel,
            },
            Evidence = EvidenceJson.From(evidence),
            Warnings = [.. warnings.Select(w => new WarningJson
            {
                Code = w.Code,
                Message = w.Message,
                Severity = w.Severity.ToString(),
                Suppressible = w.IsSuppressible,
            })],
        };
    }
}

/// <summary>An uncertainty interval on the wire.</summary>
public sealed record UncertaintyJson
{
    /// <summary>Lower bound, in the envelope's unit.</summary>
    public required double Lower { get; init; }

    /// <summary>Upper bound, in the envelope's unit.</summary>
    public required double Upper { get; init; }

    /// <summary>
    /// Confidence level as a fraction. A value of 1 means a deterministic
    /// convergence bound rather than a statistical interval.
    /// </summary>
    public required double ConfidenceLevel { get; init; }
}

/// <summary>Evidence on the wire, flattened with a discriminator.</summary>
public sealed record EvidenceJson
{
    /// <summary>One of <c>ensemble</c>, <c>convergence</c>, or <c>analytic</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Ensemble only: number of ions.</summary>
    public int? EnsembleSize { get; init; }

    /// <summary>Ensemble only: whether the convergence criterion was met.</summary>
    public bool? Converged { get; init; }

    /// <summary>Convergence only: what was refined.</summary>
    public string? Measure { get; init; }

    /// <summary>Convergence only: the order observed across refinements.</summary>
    public double? ObservedOrder { get; init; }

    /// <summary>Convergence only: the order the scheme should achieve.</summary>
    public double? NominalOrder { get; init; }

    /// <summary>Convergence only: residual between the two finest refinements, in SI.</summary>
    public double? Residual { get; init; }

    /// <summary>Analytic only: the derivation or published result relied on.</summary>
    public string? Reference { get; init; }

    /// <summary>Projects engine evidence onto the wire form.</summary>
    /// <param name="evidence">The evidence.</param>
    /// <returns>The wire form.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The evidence kind is unhandled.</exception>
    public static EvidenceJson From(Evidence evidence) => evidence switch
    {
        Evidence.Ensemble e => new EvidenceJson
        {
            Kind = "ensemble",
            EnsembleSize = e.EnsembleSize,
            Converged = e.Converged,
        },
        Evidence.Convergence c => new EvidenceJson
        {
            Kind = "convergence",
            Measure = c.Measure,
            ObservedOrder = double.IsFinite(c.ObservedOrder) ? c.ObservedOrder : null,
            NominalOrder = c.NominalOrder,
            Residual = c.ResidualSi,
        },
        Evidence.Analytic a => new EvidenceJson { Kind = "analytic", Reference = a.Reference },
        _ => throw new ArgumentOutOfRangeException(nameof(evidence), evidence, "unhandled evidence kind"),
    };
}

/// <summary>A warning on the wire.</summary>
public sealed record WarningJson
{
    /// <summary>Stable machine-readable code.</summary>
    public required string Code { get; init; }

    /// <summary>What is wrong, in physical terms.</summary>
    public required string Message { get; init; }

    /// <summary>Severity name.</summary>
    public required string Severity { get; init; }

    /// <summary>Whether a caller may silence it. False for anything above advisory (GRD-3).</summary>
    public required bool Suppressible { get; init; }
}
