using Einzel.Core.Geometry;

namespace Einzel.Core.Model;

/// <summary>
/// A validated model, with every quantity converted to SI exactly once.
/// </summary>
/// <remarks>
/// <para>
/// The boundary spec section 9 describes: units are explicit in the document and
/// resolved here, so nothing downstream re-derives them. Everything on this
/// record is SI, and every value has already been checked for dimension, range,
/// and internal consistency.
/// </para>
/// <para>
/// It holds primitives rather than engine objects because Einzel.Core is the
/// innermost assembly and cannot reference the field or transport types built
/// from it. Assembling those is the job of the layer that owns them.
/// </para>
/// </remarks>
public sealed record CompiledModel
{
    /// <summary>The directory the model document was read from, or null.</summary>
    /// <remarks>
    /// <para>
    /// A model may reference files - an imported gas velocity or pressure field - and
    /// PRJ-2 says it references them rather than embedding them, resolved against the
    /// model document's own directory so that a model means the same thing wherever
    /// the command is run from. Carrying that directory on the compiled model is what
    /// lets any consumer resolve one.
    /// </para>
    /// <para>
    /// <b>Null is the safe value, not the convenient one.</b> A model compiled from a
    /// string in memory has no directory, and a consumer handed one is refused rather
    /// than run in a gas the document does not describe. So a loader that forgets to
    /// set this degrades to the refusal rather than to a silent wrong answer, which is
    /// the direction a mistake here should fail in.
    /// </para>
    /// </remarks>
    public string? SourceDirectory { get; init; }

    /// <summary>The document this was compiled from, for hashing and round-trip.</summary>
    public required ModelDocument Source { get; init; }

    /// <summary>Ion mass, in kilograms.</summary>
    public required double MassSi { get; init; }

    /// <summary>Ion charge, in coulombs. Signed.</summary>
    public required double ChargeSi { get; init; }

    /// <summary>Starting position, in metres.</summary>
    public required Vec3 SourcePosition { get; init; }

    /// <summary>Direction of travel, normalised.</summary>
    public required Vec3 SourceDirection { get; init; }

    /// <summary>Accelerating potential, in volts.</summary>
    public required double AccelerationPotentialSi { get; init; }

    /// <summary>Fractional offset from nominal energy.</summary>
    public required double EnergyFraction { get; init; }

    /// <summary>The field elements, superposed.</summary>
    public required IReadOnlyList<CompiledField> Fields { get; init; }

    /// <summary>A point on the detector plane, in metres.</summary>
    public required Vec3 DetectorPoint { get; init; }

    /// <summary>The detector plane normal, pointing back into the flight volume.</summary>
    public required Vec3 DetectorNormal { get; init; }

    /// <summary>Transport mode.</summary>
    public required string TransportMode { get; init; }

    /// <summary>Relative tolerance for the integrator.</summary>
    public required double RelativeTolerance { get; init; }

    /// <summary>Flight-time ceiling, in seconds.</summary>
    public required double MaximumFlightTimeSi { get; init; }

    /// <summary>Trajectory sampling interval, in seconds.</summary>
    public required double SampleIntervalSi { get; init; }

    /// <summary>The background gas; vacuum when none is declared.</summary>
    public CompiledGas Gas { get; init; } = CompiledGas.Vacuum;

    /// <summary>Ion mobility, for the diffusive mode. Null when none applies.</summary>
    public CompiledMobility? Mobility { get; init; }

    /// <summary>The density grid, for the diffusive mode. Null when none applies.</summary>
    public CompiledDensityGrid? DensityGrid { get; init; }

    /// <summary>
    /// The resolved parameter surface this model was compiled from. What a sweep
    /// perturbs and an optimiser searches.
    /// </summary>
    public required ParameterSurface Parameters { get; init; }

    /// <summary>
    /// How wide a cloud the source launches. A single ion on the axis by default.
    /// </summary>
    public IonCloudSettings Cloud { get; init; } = new();

    /// <summary>
    /// Whether the packet's ions push on each other: <c>none</c> or <c>direct</c>.
    /// </summary>
    public string SpaceChargeMode { get; init; } = "none";

    /// <summary>Whether the mutual Coulomb force is being modelled.</summary>
    public bool ModelsSpaceCharge =>
        string.Equals(SpaceChargeMode, "direct", StringComparison.Ordinal)
        || string.Equals(SpaceChargeMode, "pic", StringComparison.Ordinal);

    /// <summary>
    /// The grid a particle-in-cell solve uses, or null where the method is not it.
    /// </summary>
    public CompiledSpaceChargeGrid? SpaceChargeGrid { get; init; }

    /// <summary>How a diffusive run advances its density in time.</summary>
    public CompiledDensityStep DensityStep { get; init; } = new("explicit", 1.0);

    /// <summary>The ion's launch speed, in metres per second.</summary>
    /// <returns>The speed after acceleration, including the energy offset.</returns>
    /// <remarks>
    /// Derived rather than stored, so the document cannot state an energy and a
    /// velocity that disagree.
    /// </remarks>
    public double LaunchSpeedSi()
    {
        var energy = Math.Abs(ChargeSi) * Math.Abs(AccelerationPotentialSi) * (1.0 + EnergyFraction);
        return Math.Sqrt(2.0 * energy / MassSi);
    }
}

/// <summary>The kinds of field element schema v0.1 can express.</summary>
public enum CompiledFieldKind
{
    /// <summary>Empty space.</summary>
    FieldFree,

    /// <summary>A uniform field filling all space.</summary>
    Uniform,

    /// <summary>Field-free on one side of a plane, uniform and retarding on the other.</summary>
    HalfSpaceUniform,

    /// <summary>
    /// A field solved from a Dirichlet geometry given in the document. The element
    /// that lets a device be a template rather than a class (LIB-1).
    /// </summary>
    Solved2D,

    /// <summary>
    /// The same, in three dimensions, for a device with no symmetry to exploit.
    /// </summary>
    Solved3D,
}

/// <summary>A validated field element, in SI.</summary>
public sealed record CompiledField
{
    /// <summary>Which kind of element this is.</summary>
    public required CompiledFieldKind Kind { get; init; }

    /// <summary>
    /// This element as it stands during each phase of the instrument's timeline, when
    /// the timeline changes it. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For the analytic kinds, which have nowhere else to put a phase.</b> A solved
    /// geometry carries its phases in <see cref="CompiledSolvedField.Stages"/>, because
    /// there a phase re-weights channels that are already solved. An analytic element has
    /// no channels: a phase simply gives it different numbers, so it needs a whole
    /// compiled copy per phase.
    /// </para>
    /// <para>
    /// <b>Empty when the timeline does not reach this element</b>, which is a real
    /// distinction rather than an optimisation. An element whose expressions do not
    /// depend on any parameter a phase sets is genuinely static, and wrapping it in a
    /// switch would give the assembly extra switch instants to land on and make a
    /// static element answer a time-varying interface for no reason.
    /// </para>
    /// </remarks>
    public IReadOnlyList<CompiledField> Phases { get; init; } = [];

    /// <summary>
    /// The instant each phase ends, cumulative from zero. Empty when there is no
    /// timeline, and otherwise one entry per phase.
    /// </summary>
    public IReadOnlyList<double> PhaseBoundariesSeconds { get; init; } = [];

    /// <summary>Uniform only: the field vector, in volts per metre.</summary>
    public Vec3 Field { get; init; }

    /// <summary>Half-space only: a point on the boundary plane, in metres.</summary>
    public Vec3 PlanePoint { get; init; }

    /// <summary>Half-space only: unit normal pointing into the field region.</summary>
    public Vec3 InwardNormal { get; init; }

    /// <summary>Half-space only: potential gradient inside the region, in volts per metre.</summary>
    public double PotentialGradientSi { get; init; }

    /// <summary>Half-space only: the turning depth the gradient was derived from, in metres.</summary>
    public double TurningDepthSi { get; init; }

    /// <summary>Solved-3D only: the geometry to solve.</summary>
    public CompiledSolvedField3D? Solve3D { get; init; }

    /// <summary>Solved only: the geometry to solve.</summary>
    public CompiledSolvedField? Solve { get; init; }
}

/// <summary>
/// The background gas, compiled to SI.
/// </summary>
/// <remarks>
/// Lives in Einzel.Core because the model format declares it and validation has to
/// check it; the collision machinery that consumes it lives in Einzel.Transport,
/// which is where an <c>ITransportMode</c> implementation belongs.
/// </remarks>
public sealed record CompiledGas
{
    /// <summary>No gas at all.</summary>
    public static CompiledGas Vacuum { get; } = new();

    /// <summary><c>none</c>, <c>hardSphere</c>, or <c>langevin</c>.</summary>
    public string Model { get; init; } = "none";

    /// <summary>Pressure, in pascals.</summary>
    public double PressureSi { get; init; }

    /// <summary>Temperature, in kelvin.</summary>
    public double TemperatureK { get; init; } = 300.0;

    /// <summary>Mass of one neutral, in kilograms.</summary>
    public double MassSi { get; init; }

    /// <summary>Collision cross section, in square metres.</summary>
    public double CrossSectionSi { get; init; }

    /// <summary>Polarizability volume, in cubic metres.</summary>
    public double PolarizabilitySi { get; init; }

    /// <summary>Bulk gas velocity, in metres per second.</summary>
    public Vec3 DriftVelocitySi { get; init; }

    /// <summary>
    /// Path to an imported velocity field, as declared, or null.
    /// </summary>
    /// <remarks>
    /// Carried as written rather than resolved, because resolving it needs the
    /// model file's own directory and validation does not read files. Whoever loads
    /// the model resolves it; a caller that cannot is refused rather than quietly
    /// given a gas that stands still - see <c>DiffusionRun.Execute</c>.
    /// </remarks>
    public string? VelocityFieldPath { get; init; }

    /// <summary>Which array in that file holds the velocity, or null for the first.</summary>
    public string? VelocityFieldArray { get; init; }

    /// <summary>Whether a velocity field was declared.</summary>
    public bool HasVelocityField => !string.IsNullOrWhiteSpace(VelocityFieldPath);

    /// <summary>Path to an imported pressure field, as declared, or null.</summary>
    /// <remarks>
    /// Carried as written, for the reason the velocity field's path is: resolving it
    /// needs the model file's own directory and validation does not read files.
    /// </remarks>
    public string? PressureFieldPath { get; init; }

    /// <summary>Which array in that file holds the pressure, or null for the first.</summary>
    public string? PressureFieldArray { get; init; }

    /// <summary>
    /// What one of the file's numbers is in pascals - 1 for Pa, 100 for mbar.
    /// </summary>
    /// <remarks>
    /// Resolved at validation, where the unit registry is, so that a bad unit is an
    /// AGT-3 error against the document rather than a surprise at load time. The
    /// array itself stays in whatever the file wrote.
    /// </remarks>
    public double PressureFieldScale { get; init; } = 1.0;

    /// <summary>Whether a pressure field was declared.</summary>
    public bool HasPressureField => !string.IsNullOrWhiteSpace(PressureFieldPath);

    /// <summary>Seed for the collision random stream.</summary>
    public int Seed { get; init; } = 20_240_101;

    /// <summary>Whether this gas does anything.</summary>
    public bool IsPresent => Model != "none" && PressureSi > 0.0;
}

/// <summary>Ion mobility, compiled to SI.</summary>
/// <param name="ZeroFieldSi">Zero-field mobility, in square metres per volt-second.</param>
/// <param name="Alpha">Quadratic coefficient of the reduced-field expansion.</param>
/// <param name="ValidToTownsend">The reduced field the expansion was fitted to.</param>
/// <param name="Derived">
/// Whether this was derived from a cross section rather than declared. A derived
/// value carries the cross section's uncertainty plus a first-order Chapman-Enskog
/// approximation, and TRN-1 wants the number declared, so a result computed from a
/// derived one says which it was.
/// </param>
public sealed record CompiledMobility(
    double ZeroFieldSi,
    double Alpha,
    double ValidToTownsend,
    bool Derived);

/// <summary>The region a density is tracked over, compiled to SI.</summary>
/// <param name="MinX">Lower x, in metres.</param>
/// <param name="MinY">Lower y, in metres.</param>
/// <param name="MaxX">Upper x, in metres.</param>
/// <param name="MaxY">Upper y, in metres.</param>
/// <param name="IntervalsX">Cells across x.</param>
/// <param name="IntervalsY">Cells across y.</param>
public sealed record CompiledDensityGrid(
    double MinX, double MinY, double MaxX, double MaxY, int IntervalsX, int IntervalsY);

/// <summary>A particle-in-cell space-charge grid, validated and in SI.</summary>
/// <param name="Nodes">Nodes across the box.</param>
/// <param name="Padding">Box half-width as a multiple of the packet's RMS radius.</param>
/// <param name="RefreshTolerance">Fractional change in RMS radius that forces a solve.</param>
public sealed record CompiledSpaceChargeGrid(int Nodes, double Padding, double RefreshTolerance);

/// <summary>A density time-stepping choice, validated.</summary>
/// <param name="Scheme"><c>explicit</c> or <c>implicit</c>.</param>
/// <param name="Gain">How many times the explicit stability limit to step.</param>
public sealed record CompiledDensityStep(string Scheme, double Gain)
{
    /// <summary>Whether the implicit scheme was asked for.</summary>
    public bool IsImplicit => string.Equals(Scheme, "implicit", StringComparison.Ordinal);
}
