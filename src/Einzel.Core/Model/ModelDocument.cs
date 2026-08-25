namespace Einzel.Core.Model;

/// <summary>
/// A model document: the declarative, schema-versioned, diffable description of
/// a modelling problem.
/// </summary>
/// <remarks>
/// <para>
/// AGT-1: "The model is text. Declarative, schema-validated, diffable JSON. A
/// model file plus referenced artifacts fully determines a run."
/// </para>
/// <para>
/// Schema v0.1 is deliberately narrow. It expresses what the analytic tier needs
/// — an ion, a source, a stack of closed-form field elements, a detector, and
/// transport settings — and nothing more. Spec section 21 makes schema stability
/// a Phase 1 review gate and section 22 lists schema churn as a risk that breaks
/// agent workflows, so the format grows by adding validated cases rather than by
/// reinterpreting existing ones. Every bump ships a migration and a test that the
/// prior corpus still loads (section 14).
/// </para>
/// <para>
/// No device class appears anywhere in this document. A reflectron is a
/// half-space field plus a drift length plus a detector placement, and that
/// arrangement is what a template in Einzel.Library will name — not something
/// the schema knows about (LIB-1).
/// </para>
/// </remarks>
public sealed record ModelDocument
{
    /// <summary>The schema version this document is written against.</summary>
    public string SchemaVersion { get; init; } = ModelSchema.CurrentVersion;

    /// <summary>A short name. Used in output and in generated file names.</summary>
    public string? Name { get; init; }

    /// <summary>Prose description, carried through to results and figures.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The declared parameter surface (LIB-1). Names, units, bounds, and derived
    /// expressions; what a sweep varies and an optimiser searches.
    /// </summary>
    public IReadOnlyDictionary<string, ParameterDocument>? Parameters { get; init; }

    /// <summary>The ion being tracked.</summary>
    public IonDocument? Ion { get; init; }

    /// <summary>Where the ion starts, and with what energy.</summary>
    public SourceDocument? Source { get; init; }

    /// <summary>
    /// The field elements, superposed. Order is irrelevant to the physics and is
    /// preserved only so a diff stays readable.
    /// </summary>
    public IReadOnlyList<FieldDocument>? Fields { get; init; }

    /// <summary>The surface that ends the flight.</summary>
    public DetectorDocument? Detector { get; init; }

    /// <summary>Transport mode and its settings.</summary>
    public TransportDocument? Transport { get; init; }

    /// <summary>Element-wise equality, including the field list.</summary>
    /// <param name="other">The document to compare against.</param>
    /// <returns><see langword="true"/> when the documents describe the same model.</returns>
    /// <remarks>
    /// The compiler-generated equality would compare <see cref="Fields"/> by
    /// reference, so two documents parsed from identical text would be unequal.
    /// Note that model identity for drift detection is the content hash of the
    /// text (PRJ-2, GRD-10), not this — but an equality operator that silently
    /// means something other than what it reads as is worth not shipping.
    /// </remarks>
    public bool Equals(ModelDocument? other) =>
        other is not null
        && ParametersEqual(Parameters, other.Parameters)
        && string.Equals(SchemaVersion, other.SchemaVersion, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && Equals(Ion, other.Ion)
        && Equals(Source, other.Source)
        && Equals(Detector, other.Detector)
        && Equals(Transport, other.Transport)
        && (ReferenceEquals(Fields, other.Fields)
            || (Fields is not null && other.Fields is not null && Fields.SequenceEqual(other.Fields)));

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Description, StringComparer.Ordinal);
        hash.Add(Ion);
        hash.Add(Source);
        hash.Add(Detector);
        hash.Add(Transport);

        foreach (var field in Fields ?? [])
        {
            hash.Add(field);
        }

        foreach (var parameter in (Parameters ?? new Dictionary<string, ParameterDocument>())
            .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            hash.Add(parameter.Key, StringComparer.Ordinal);
            hash.Add(parameter.Value);
        }

        return hash.ToHashCode();
    }

    private static bool ParametersEqual(
        IReadOnlyDictionary<string, ParameterDocument>? left,
        IReadOnlyDictionary<string, ParameterDocument>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || !Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Schema identity and version policy.</summary>
public static class ModelSchema
{
    /// <summary>The schema version this build writes.</summary>
    /// <remarks>
    /// 0.3 adds the source cloud. Additive, so every 0.2 document still reads, but
    /// a document that declares a cloud genuinely is not a 0.2 document and saying
    /// so is cheaper than an older build rejecting it with a schema error that
    /// names the wrong cause.
    /// </remarks>
    public const string CurrentVersion = "0.3";

    /// <summary>Versions this build can read.</summary>
    public static IReadOnlyList<string> SupportedVersions { get; } = ["0.1", "0.2", "0.3"];
}

/// <summary>The ion being tracked.</summary>
public sealed record IonDocument
{
    /// <summary>
    /// Mass-to-charge ratio, in daltons per elementary charge. The form mass
    /// spectrometry quotes.
    /// </summary>
    public QuantityValue? MassToCharge { get; init; }

    /// <summary>
    /// Charge number. Positive for cations, negative for anions, never zero.
    /// </summary>
    public int ChargeNumber { get; init; } = 1;
}

/// <summary>Where the ion starts, and with what energy.</summary>
public sealed record SourceDocument
{
    /// <summary>Starting position.</summary>
    public VectorValue? Position { get; init; }

    /// <summary>Direction of travel; normalised on load.</summary>
    public DirectionValue? Direction { get; init; }

    /// <summary>
    /// The potential the ion was accelerated through. Converted to a speed using
    /// the ion's own mass and charge, so the document never states a velocity
    /// that could disagree with the energy.
    /// </summary>
    public QuantityValue? AccelerationPotential { get; init; }

    /// <summary>
    /// Fractional offset from the nominal energy, for acceptance studies. Zero by
    /// default; the memo asks for plus or minus 3 to 5 percent.
    /// </summary>
    public double EnergyFraction { get; init; }

    /// <summary>
    /// How wide a cloud of ions to launch. Omit for a single ion on the axis.
    /// </summary>
    public CloudDocument? Cloud { get; init; }
}

/// <summary>
/// The spread of a launched ion cloud, as it appears in a model document.
/// </summary>
/// <remarks>
/// <para>
/// Without this every result is an answer about one ion rather than about an
/// instrument, which is why every resolving power in this project has carried the
/// same caveat: energy aberration only, no spatial spread, no angular spread, no
/// turn-around time.
/// </para>
/// <para>
/// There is deliberately no angular-divergence setting. A thermal cloud already
/// has one - an ion with sideways thermal velocity is an ion launched at an
/// angle - and offering both would let a document say two things about the same
/// physics and be believed twice.
/// </para>
/// </remarks>
public sealed record CloudDocument
{
    /// <summary>How many ions to launch.</summary>
    public int Ions { get; init; } = 1;

    /// <summary>Seed for the draw, so a run is regenerable.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// Source temperature. Sets the thermal velocity, and with it the turn-around
    /// time that limits a pulsed extraction.
    /// </summary>
    public QuantityValue? Temperature { get; init; }

    /// <summary>Gaussian width of the cloud across the direction of travel.</summary>
    public QuantityValue? TransverseSpread { get; init; }

    /// <summary>Gaussian width of the cloud along the direction of travel.</summary>
    public QuantityValue? LongitudinalSpread { get; init; }

    /// <summary>
    /// Gaussian width of the acceleration energy, as a fraction of nominal.
    /// </summary>
    /// <remarks>
    /// Supply ripple rather than temperature: it varies the energy without varying
    /// the direction, which a temperature cannot express.
    /// </remarks>
    public double EnergyFractionSpread { get; init; }
}

/// <summary>A field element. The discriminator is <see cref="Type"/>.</summary>
/// <remarks>
/// A single record with a discriminator rather than a polymorphic hierarchy, so
/// that an unknown or misspelled type produces one clear AGT-3 error naming the
/// permitted values, instead of a deserialiser exception that names a .NET type
/// an agent has never heard of.
/// </remarks>
public sealed record FieldDocument
{
    /// <summary>
    /// One of <c>fieldFree</c>, <c>uniform</c>, <c>halfSpaceUniform</c>, or
    /// <c>solved2d</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>Solved only: the domain, electrodes, and boundary conditions.</summary>
    public SolvedFieldDocument? Solve { get; init; }

    /// <summary>Uniform only: the field vector.</summary>
    public VectorValue? Field { get; init; }

    /// <summary>Half-space only: a point on the boundary plane.</summary>
    public VectorValue? PlanePoint { get; init; }

    /// <summary>Half-space only: unit normal pointing into the field region.</summary>
    public DirectionValue? InwardNormal { get; init; }

    /// <summary>Half-space only: the potential reached at <see cref="TurningDepth"/>.</summary>
    public QuantityValue? CapPotential { get; init; }

    /// <summary>
    /// Half-space only: the depth at which <see cref="CapPotential"/> is reached.
    /// An ion accelerated through that potential turns exactly here, which is how
    /// an ion mirror is actually designed.
    /// </summary>
    public QuantityValue? TurningDepth { get; init; }
}

/// <summary>The surface that ends the flight.</summary>
public sealed record DetectorDocument
{
    /// <summary>A point on the detector plane.</summary>
    public VectorValue? PlanePoint { get; init; }

    /// <summary>
    /// The plane normal, pointing back toward the flight volume. The flight ends
    /// when the ion crosses from the positive side to the negative side.
    /// </summary>
    public DirectionValue? Normal { get; init; }
}

/// <summary>Transport mode and its settings.</summary>
public sealed record TransportDocument
{
    /// <summary>
    /// <c>trajectory</c> or <c>statisticalDiffusion</c>. REG-1 makes these peer
    /// implementations; only the first exists in this build, and asking for the
    /// second is a clear error rather than a silent substitution.
    /// </summary>
    public string Mode { get; init; } = "trajectory";

    /// <summary>Relative tolerance for the adaptive integrator.</summary>
    public double RelativeTolerance { get; init; } = 1e-11;

    /// <summary>Ceiling on simulated flight time. Required, as a runaway guard.</summary>
    public QuantityValue? MaximumFlightTime { get; init; }

    /// <summary>
    /// Interval at which the trajectory is sampled for rendering and export.
    /// TRJ-1: this stream has its own cadence, independent of integration steps.
    /// </summary>
    public QuantityValue? SampleInterval { get; init; }
}
