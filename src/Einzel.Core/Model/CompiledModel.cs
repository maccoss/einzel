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
        string.Equals(SpaceChargeMode, "direct", StringComparison.Ordinal);

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
