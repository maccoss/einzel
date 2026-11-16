using System.Globalization;
using System.Text;

using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>What a probe could say about one point.</summary>
public static class ProbeStatus
{
    /// <summary>An ordinary point in free space: the field there is the one a run flies through.</summary>
    public const string Ok = "ok";

    /// <summary>
    /// The point is inside a conductor. The potential reported is the field's own value
    /// there, which for a solved element is the electrode's potential, and the field is the
    /// interpolant's, which inside metal is not a physical field.
    /// </summary>
    public const string InsideConductor = "inside-conductor";

    /// <summary>
    /// The point lies outside the domain of every element (a reflected solve's mirror image and a
    /// tilted cross-section's rotated extent included). The value reported is what the field
    /// returns there - a solve's value outside its grid, nothing from a bounded analytic element -
    /// which is not a solved or declared value and need not be zero. This wins over
    /// <see cref="InsideConductor"/>: an electrode's declared shape can run on past the solve.
    /// </summary>
    public const string OutsideDomain = "outside-domain";

    /// <summary>The field declined to answer at this point (for example a singular axis).</summary>
    public const string Refused = "refused";
}

/// <summary>One probed point.</summary>
/// <param name="Index">Position in the request, from zero.</param>
/// <param name="PositionMm">Where, in millimetres (x, y, z).</param>
/// <param name="PotentialV">The potential in volts, or absent where the field refused.</param>
/// <param name="FieldVPerM">The electric field vector in volts per metre, or absent where refused.</param>
/// <param name="FieldMagnitudeVPerM">The field's magnitude in volts per metre, or absent where refused.</param>
/// <param name="Status">One of the <see cref="ProbeStatus"/> values.</param>
/// <param name="Conductor">The nearest conductor's name where the field knows conductors, else absent.</param>
/// <param name="ConductorDistanceMm">
/// Signed distance to the nearest conductor surface in millimetres, negative inside; absent where
/// the field declares no conductors.
/// </param>
/// <param name="OutsideElements">
/// Field elements whose declared domain does not contain this point, by element index. An element
/// outside its domain contributes nothing to the sum.
/// </param>
/// <param name="Note">Why a point was refused, or any other word the row needs.</param>
public sealed record ProbeSample(
    int Index,
    IReadOnlyList<double> PositionMm,
    double? PotentialV,
    IReadOnlyList<double>? FieldVPerM,
    double? FieldMagnitudeVPerM,
    string Status,
    string? Conductor,
    double? ConductorDistanceMm,
    IReadOnlyList<int> OutsideElements,
    string? Note)
{
    /// <summary>
    /// The magnetic flux density vector in tesla where the model declares a magnet; absent
    /// where it declares none (so a model without one reads exactly as it always did) and where
    /// the point was refused.
    /// </summary>
    /// <remarks>
    /// A magnet adds no potential and no electric field, so a probe that reported only those
    /// would print 0 V and 0 V/m inside a 0.5 T sector with status ok - a field that turns
    /// every ion through it, reported as nothing at all.
    /// </remarks>
    public IReadOnlyList<double>? FluxDensityT { get; init; }
}

/// <summary>What each field element is made of and how well it is known.</summary>
/// <param name="Index">Element index in the model.</param>
/// <param name="Kind">The element kind.</param>
/// <param name="Basis">
/// "analytic" - a closed form, exact to arithmetic - or "solved", a numerical solution on a grid.
/// </param>
/// <param name="CellMm">The solve's cell in millimetres, or absent for an analytic element.</param>
/// <param name="HasDomain">Whether an outside-domain check can be made for it.</param>
public sealed record ProbeElement(int Index, string Kind, string Basis, double? CellMm, bool HasDomain);

/// <summary>Where and when to probe.</summary>
/// <param name="FromMm">The point, or the start of the line, in millimetres.</param>
/// <param name="ToMm">The end of the line in millimetres, or absent for a single point.</param>
/// <param name="Samples">Points along a line, both ends included; ignored for a single point.</param>
/// <param name="AtUs">The instant, in microseconds, or absent when none was asked for.</param>
public sealed record ProbeRequest(Vec3 FromMm, Vec3? ToMm = null, int Samples = 2, double? AtUs = null);

/// <summary>The outcome of a probe.</summary>
public sealed record ProbeOutcome
{
    /// <summary>The model, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Whether the field varies with time (driven or sequenced).</summary>
    public required bool TimeVarying { get; init; }

    /// <summary>
    /// The instant every point was evaluated at, in microseconds, or absent for a static field. For
    /// a time-varying field with no instant asked for this is 0 and a warning says so.
    /// </summary>
    public double? InstantUs { get; init; }

    /// <summary>The field elements, with their basis.</summary>
    public required IReadOnlyList<ProbeElement> Elements { get; init; }

    /// <summary>The probed points, in request order.</summary>
    public required IReadOnlyList<ProbeSample> Samples { get; init; }

    /// <summary>What applies to every number here (GRD-2).</summary>
    public required IReadOnlyList<ValidityWarning> Warnings { get; init; }

    /// <summary>
    /// Why no uncertainty interval accompanies the values. A probed potential is an evaluation of a
    /// field already computed, not a measurement with its own ensemble: its quality is the solve's
    /// (the warnings, and each element's cell size), and inventing an interval here would state a
    /// precision nothing measured.
    /// </summary>
    public string Evidence { get; init; } =
        "evaluation of the assembled field, not a measurement: analytic elements are exact to "
        + "arithmetic; a solved element's error is its discretisation, stated by its cell size and "
        + "by the warnings, and no interval is claimed";
}

/// <summary>
/// The composite field a run flies through, sampled at points (ion-optics designers ask "what is the
/// potential, what is E, here").
/// </summary>
/// <remarks>
/// Built with <see cref="FieldAssembly.BuildReported"/>, the same assembly every run uses, so
/// superposed elements, regions, mirrors, tilts and the axisymmetric mapping are all honoured and
/// the assembly's warnings come along. A driven or sequenced field is read through its
/// time-varying interface at an explicit instant: reading it through the static one would answer
/// at t = 0 without saying so, which is a defect this project has met six times.
/// <para>
/// <b>Safe to share across threads.</b> Every sampling member may be called concurrently, at the
/// same instant or different ones: the only mutable state is the field held at the last
/// operating point asked for, kept as one immutable object replaced by reference, so a reader
/// sees a whole state and never half of one.
/// </para>
/// </remarks>
public sealed class FieldProbe
{
    // The model's own factor for a millimetre (UnitRegistry: "mm" is 1e-3 of a metre, applied
    // by multiplication). A face declared as 9 mm is the double 9 * 1e-3, which is not the
    // correctly rounded 0.009; converting a typed 9 mm the same way lands on that same double,
    // where a correctly rounded division would land an ulp inside or outside it. So the probe
    // converts exactly as the document does, and DomainMm hands out millimetres that come back
    // to the SI bound under this very multiplication.
    private const double MetresPerMm = 1e-3;

    private readonly IElectrostaticField _field;
    private readonly ITimeVaryingField? _driven;
    // The leaves of the electric sum - every field that has conductors of its own - and the
    // model element each came from, built by one function (Leaves) so the two cannot disagree.
    private readonly IReadOnlyList<IElectrostaticField> _members;
    private readonly IReadOnlyList<int> _leafElements;
    private readonly bool _hasOperatingPoints;
    private readonly Einzel.Fields.Magnetic.IMagneticField? _magnetic;
    private readonly CompiledModel _model;
    private readonly RotatedField?[] _tilts;
    private readonly bool _allBounded;
    private readonly IReadOnlyList<int> _memberElements;

    private FieldProbe(
        CompiledModel model,
        IElectrostaticField field,
        IReadOnlyList<ValidityWarning> warnings,
        IReadOnlyList<int> memberElements,
        string modelPath,
        string modelHash)
    {
        _memberElements = memberElements;
        (_members, _leafElements) = Leaves(field, memberElements);

        // Once: asking a sequenced field for its operating point allocates a held copy of it.
        _hasOperatingPoints = field is ITimeVaryingField timed
            && (!ReferenceEquals(timed.AtOperatingPoint(0.0), timed)
                || double.IsFinite(timed.NextSwitchAfter(0.0)));
        _model = model;
        _field = field;
        _driven = field as ITimeVaryingField;
        _magnetic = field as Einzel.Fields.Magnetic.IMagneticField;
        Warnings = warnings;
        ModelPath = modelPath;
        ModelHash = modelHash;

        // The rotation each tilted cross-section is evaluated through, built with the very
        // arguments FieldAssembly gives it, so "is this point in that solve" is asked of the
        // point the field itself asks the solve about.
        _tilts =
        [
            .. model.Fields.Select(e => e.Solve is { TiltHalfTurns: not 0.0 } plane
                ? new RotatedField(FieldFreeSpace.Instance, plane.TiltHalfTurns, plane.TiltCentreX, plane.TiltCentreZ)
                : null),
        ];

        Elements =
        [
            .. model.Fields.Select((e, i) => new ProbeElement(
                i,
                e.Kind.ToString(),
                e.Solve is not null || e.Solve3D is not null ? "solved" : "analytic",
                (e.Solve?.CellSize * 1e3) ?? (e.Solve3D?.CellSize * 1e3),
                CanCheckDomain(e))),
        ];

        _allBounded = Elements.Count > 0 && Elements.All(e => e.HasDomain);
    }

    /// <summary>
    /// Which field element holds the conductor a point is in, by its index in the model: a
    /// conductor's name is unique only within one solve, so two elements may each have a
    /// "plate" and the name alone does not say which this is.
    /// </summary>
    /// <param name="positionMm">Where, in millimetres.</param>
    /// <returns>The element's index, or null where the field knows no conductor there.</returns>
    public int? ConductorElementAt(Vec3 positionMm)
    {
        var point = MillimetresToMetres(positionMm);
        Span<bool> inside = stackalloc bool[Math.Min(Elements.Count, 256)];
        var solved = Elements.Count <= 256 ? Solved(in point, inside) : default;

        Locate(in point, solved, out _, out _, out var held, out _);

        return held >= 0 ? _leafElements[held] : null;
    }

    // THE ONE PLACE the electric sum is taken apart, recording for every leaf the element it
    // came from. The assembly's sum holds one member per element that adds one
    // (FieldAssembly.BuildMapped); a member may itself be a sum - a reflected solve is its own
    // field plus that field's mirror image - and both of its leaves belong to its element. A
    // version that unwrapped every sum and mapped the leaves by position against the
    // assembly's per-member list gave a reflected model two leaves and one entry, so the
    // mirrored half of every reflected solve read as free space.
    private static (IReadOnlyList<IElectrostaticField> Leaves, IReadOnlyList<int> Elements) Leaves(
        IElectrostaticField field, IReadOnlyList<int> memberElements)
    {
        var electric = field is Einzel.Fields.Magnetic.MagnetizedField magnetized ? magnetized.Electric : field;
        var leaves = new List<IElectrostaticField>();
        var elements = new List<int>();

        void Add(IElectrostaticField member, int element)
        {
            IReadOnlyList<IElectrostaticField>? parts = member switch
            {
                SuperposedField sum => sum.Elements,
                DrivenSuperposedField sum => sum.Elements,
                _ => null,
            };

            if (parts is null)
            {
                leaves.Add(member);
                elements.Add(element);
                return;
            }

            foreach (var part in parts)
            {
                Add(part, element);
            }
        }

        // More than one member: the top of the field is the assembly's own sum, one member per
        // entry. One member (or none): the field is that member, whatever it is.
        IReadOnlyList<IElectrostaticField> top = memberElements.Count > 1
            ? electric switch
            {
                SuperposedField sum => sum.Elements,
                DrivenSuperposedField sum => sum.Elements,
                _ => [electric],
            }
            : [electric];

        for (var i = 0; i < top.Count; i++)
        {
            Add(top[i], i < memberElements.Count ? memberElements[i] : -1);
        }

        return (leaves, elements);
    }

    // Which elements' solves (or regions) contain a point: true for an element with no domain
    // to check, which fills all space. The same test the outside count reports.
    private Span<bool> Solved(in Vec3 point, Span<bool> inside)
    {
        for (var i = 0; i < Elements.Count; i++)
        {
            inside[i] = !(Elements[i].HasDomain && !InsideDomain(i, in point));
        }

        return inside;
    }

    // ONE WALK of the leaves' conductors, giving two answers: the nearest conductor of any leaf
    // (what a probe row names, and its distance), and the nearest among the leaves whose
    // element was solved at the point (the conductor the point is IN). Only those compete for
    // the second: a declared shape running past its solve would otherwise win wherever it was
    // nearest, and hide a conductor another element really has there. The rule is the
    // smallest signed distance, the first on a tie.
    private void Locate(
        in Vec3 point, ReadOnlySpan<bool> solved,
        out int nearest, out double nearestDistance, out int held, out double heldDistance)
    {
        nearest = held = -1;
        nearestDistance = heldDistance = double.PositiveInfinity;

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i] is not IConductorBounded bounded)
            {
                continue;
            }

            var d = bounded.SignedDistanceToConductor(in point);

            if (d < nearestDistance)
            {
                nearestDistance = d;
                nearest = i;
            }

            var element = _leafElements[i];

            if (d < heldDistance && (element < 0 || solved.IsEmpty || solved[element]))
            {
                heldDistance = d;
                held = i;
            }
        }
    }

    /// <summary>The model, as an absolute path.</summary>
    public string ModelPath { get; }

    /// <summary>The validated model the field was assembled from.</summary>
    public CompiledModel Model => _model;

    /// <summary>The content hash of the model text this probe was built from (PRJ-3).</summary>
    /// <remarks>
    /// Taken from the text that was validated and assembled, not from a second read of the file,
    /// so a header carrying it cannot name one revision of the model over another's field.
    /// </remarks>
    public string ModelHash { get; }

    /// <summary>What the assembly said about the field.</summary>
    public IReadOnlyList<ValidityWarning> Warnings { get; }

    /// <summary>The elements and how they are known.</summary>
    public IReadOnlyList<ProbeElement> Elements { get; }

    /// <summary>Whether the assembled field varies with time.</summary>
    public bool TimeVarying => _driven is not null;

    /// <summary>Whether the model declares a magnetic field, so B is reported beside phi and E.</summary>
    public bool Magnetic => _magnetic is not null;

    /// <summary>Validates and assembles the field of a model file.</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>A probe over its field.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    public static FieldProbe Open(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        return Open(absolute, File.ReadAllText(absolute));
    }

    /// <summary>Validates and assembles the field of model text already read.</summary>
    /// <param name="modelPath">Where the text came from; imported files resolve against its directory.</param>
    /// <param name="modelText">The model document.</param>
    /// <returns>A probe over its field, whose <see cref="ModelHash"/> is the hash of this text.</returns>
    /// <exception cref="ArgumentException">A argument is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    /// <remarks>
    /// For a caller that also records the model's hash: reading the file once and handing the
    /// text here means the hash and the field are of one revision (PRJ-3), however the file is
    /// edited meanwhile.
    /// </remarks>
    public static FieldProbe Open(string modelPath, string modelText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(modelText);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(modelText), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var (field, warnings, memberElements) = FieldAssembly.BuildMapped(model);

        return new FieldProbe(model, field, warnings, memberElements, absolute, ContentHash.OfText(modelText));
    }

    /// <summary>The box the model's bounded elements occupy, in millimetres, when there is one.</summary>
    /// <returns>Min and max corners, or null when no element has a domain.</returns>
    /// <remarks>
    /// <para>
    /// The union of what each element actually occupies: a solve's declared box together with
    /// its mirror image where it is reflected, a tilted cross-section by the bounding box of its
    /// rotated extent, and an analytic element's declared region. A cross-section's z and an
    /// axisymmetric solve's radius are not bounded the way a box is, so a cross-section spans z
    /// from minus to plus the larger of its |y| bounds and an axisymmetric solve is the box round
    /// its cylinder.
    /// </para>
    /// <para>
    /// The corners come back to the SI bounds exactly under the probe's own millimetre
    /// conversion, so a point taken from them is on the face and not an ulp beyond it.
    /// </para>
    /// </remarks>
    public (Vec3 Min, Vec3 Max)? DomainMm() =>
        DomainSi() is { } si ? (ToMm(si.Min, inward: +1), ToMm(si.Max, inward: -1)) : null;

    /// <summary>The same box as <see cref="DomainMm"/>, in metres, taken from the bounds themselves.</summary>
    /// <returns>Min and max corners in metres, or null when no element has a domain.</returns>
    public (Vec3 Min, Vec3 Max)? DomainSi()
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        var any = false;

        void Take(Vec3 a, Vec3 b)
        {
            any = true;
            minX = Math.Min(minX, a.X);
            maxX = Math.Max(maxX, b.X);
            minY = Math.Min(minY, a.Y);
            maxY = Math.Max(maxY, b.Y);
            minZ = Math.Min(minZ, a.Z);
            maxZ = Math.Max(maxZ, b.Z);
        }

        for (var index = 0; index < _model.Fields.Count; index++)
        {
            foreach (var (a, b) in Occupied(index))
            {
                Take(a, b);
            }
        }

        return any ? (new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ)) : null;
    }

    /// <summary>Evaluates the field at a point.</summary>
    /// <param name="index">The row number.</param>
    /// <param name="positionMm">Where, in millimetres.</param>
    /// <param name="atSeconds">The instant, used only by a time-varying field.</param>
    /// <returns>The sample; a refused point is a row with status refused, never an exception.</returns>
    public ProbeSample At(int index, Vec3 positionMm, double atSeconds) =>
        At(index, positionMm, atSeconds, cycleMean: false);

    /// <summary>
    /// Whether the field has time dependence besides its oscillation - a sequence of phases, or a
    /// ramp - so that even its cycle mean depends on the instant.
    /// </summary>
    public bool HasOperatingPoints => _hasOperatingPoints;

    /// <summary>
    /// The window a cycle mean is taken over at an operating point, in seconds: the drive's
    /// period where the field held there oscillates at one frequency, one period of the fastest
    /// drive where it carries several; absent where nothing oscillates there - a static field,
    /// or a sequence whose state at that instant is static - which has no cycle to average over.
    /// </summary>
    /// <param name="atSeconds">The operating point: the instant the sequence and ramps are held at.</param>
    /// <returns>The window, or null where there is no drive at that operating point.</returns>
    /// <remarks>
    /// Asked of the field HELD at the operating point, which is the field the mean is taken on.
    /// A sequence that is not held has no one state to take a period from: it reports the
    /// shortest period of any of its states, and no single frequency at all, so reading it
    /// unheld gave a single-frequency sequenced model a window from another phase and a
    /// warning that it carried more than one frequency.
    /// </remarks>
    public double? CycleMeanWindowSecondsAt(double atSeconds) =>
        _driven is null ? null : HeldAt(atSeconds).Window;

    /// <summary>
    /// Whether the field held at an operating point oscillates at one frequency (or not at all),
    /// so one period is its whole cycle.
    /// </summary>
    /// <param name="atSeconds">The operating point.</param>
    /// <returns>True for a static field and for a single frequency.</returns>
    public bool MonochromaticAt(double atSeconds) =>
        _driven is null || !double.IsNaN(HeldAt(atSeconds).Field.MonochromaticPeriodSeconds);

    /// <summary>How many instants a cycle mean samples where it cannot be taken in closed form.</summary>
    public const int CycleMeanSamples = 64;

    /// <summary>Evaluates the field at a point, optionally as its mean over one drive cycle.</summary>
    /// <param name="index">The row number.</param>
    /// <param name="positionMm">Where, in millimetres.</param>
    /// <param name="atSeconds">
    /// The instant; for a cycle mean, the operating point the mean is taken at and the opening of
    /// its window.
    /// </param>
    /// <param name="cycleMean">
    /// Whether to report the potential averaged over one period of the fastest drive, held at the
    /// operating point <paramref name="atSeconds"/> - for a sinusoidal drive, exactly the part of
    /// the potential that does not oscillate. The field vector is then absent: what a mean
    /// potential's gradient would be is not what this method measures.
    /// </param>
    /// <returns>The sample; a refused point is a row with status refused, never an exception.</returns>
    /// <exception cref="InvalidOperationException">
    /// A cycle mean was asked of a field with no drive period at that operating point
    /// (<see cref="CycleMeanWindowSecondsAt"/> is absent). It is not answered with the instant: a
    /// caller that asked for a mean and was handed an instantaneous value could not tell.
    /// </exception>
    public ProbeSample At(int index, Vec3 positionMm, double atSeconds, bool cycleMean)
    {
        RefuseMeanWithoutCycle(atSeconds, cycleMean);

        var point = new Vec3(positionMm.X * MetresPerMm, positionMm.Y * MetresPerMm, positionMm.Z * MetresPerMm);
        var position = new[] { positionMm.X, positionMm.Y, positionMm.Z };
        var outside = new List<int>();

        var status = SampleCore(
            in point, atSeconds, cycleMean, outside, Resolve.Nearest,
            out var potential, out var field, out var distance, out var note, out var averaged,
            out var conductor, out _, out _);

        if (status == ProbeStatus.Refused)
        {
            return new ProbeSample(index, position, null, null, null, status, null, null, outside, note);
        }

        if (averaged)
        {
            return new ProbeSample(
                index, position, potential, null, null, status, conductor, distance * 1e3, outside,
                "cycle mean: potential only");
        }

        var magnitude = Math.Sqrt((field.X * field.X) + (field.Y * field.Y) + (field.Z * field.Z));

        return new ProbeSample(
            index, position, potential, [field.X, field.Y, field.Z], magnitude, status,
            conductor, distance * 1e3, outside, null)
        {
            FluxDensityT = _magnetic is null ? null : FluxDensityAt(in point) is var b ? [b.X, b.Y, b.Z] : null,
        };
    }

    /// <summary>
    /// The same evaluation as <see cref="Sample(in Vec3, double, out double, out Vec3)"/>, with the
    /// magnetic flux density as well: zero where the model declares no magnet, NaN where refused.
    /// </summary>
    /// <param name="pointMetres">Where, in metres.</param>
    /// <param name="atSeconds">The instant, used only by a time-varying field.</param>
    /// <param name="potential">The potential in volts; NaN where the point was refused.</param>
    /// <param name="field">The electric field in volts per metre; NaN components where refused.</param>
    /// <param name="fluxDensity">B in tesla; NaN components where refused.</param>
    /// <returns>One of the <see cref="ProbeStatus"/> values.</returns>
    public string Sample(
        in Vec3 pointMetres, double atSeconds, out double potential, out Vec3 field, out Vec3 fluxDensity)
    {
        var status = Sample(in pointMetres, atSeconds, out potential, out field);

        fluxDensity = ReferenceEquals(status, ProbeStatus.Refused)
            ? new Vec3(double.NaN, double.NaN, double.NaN)
            : FluxDensityAt(in pointMetres);

        return status;
    }

    // B is static in this build, so there is no instant to ask it at. Adding zero turns a
    // negative zero into a plain one, as for E.
    private Vec3 FluxDensityAt(in Vec3 point)
    {
        if (_magnetic is null)
        {
            return Vec3.Zero;
        }

        var b = _magnetic.MagneticFieldAt(in point);
        return new Vec3(b.X + 0.0, b.Y + 0.0, b.Z + 0.0);
    }

    /// <summary>
    /// Evaluates the field at a point given in metres, with no allocation: the path for sampling
    /// a whole grid, where only the potential, the field and the status are wanted.
    /// </summary>
    /// <param name="pointMetres">Where, in metres.</param>
    /// <param name="atSeconds">The instant, used only by a time-varying field.</param>
    /// <param name="potential">The potential in volts; NaN where the point was refused.</param>
    /// <param name="field">The electric field in volts per metre; NaN components where refused.</param>
    /// <returns>One of the <see cref="ProbeStatus"/> values, the same one <see cref="At(int, Vec3, double)"/> gives.</returns>
    public string Sample(in Vec3 pointMetres, double atSeconds, out double potential, out Vec3 field) =>
        SampleCore(
            in pointMetres, atSeconds, cycleMean: false, outside: null, Resolve.None,
            out potential, out field, out _, out _, out _, out _, out _, out _);

    // The one evaluation every public sampler goes through: which domains the point is outside
    // (collected into `outside` when a row wants them, counted otherwise), the potential and
    // field or their cycle mean, the conductor distance, and the status. A refused point comes
    // back with NaN potential and field and no distance, so a caller that does not build a row
    // has nothing to remember.
    // How much a caller wants to know about the conductor at a point.
    private enum Resolve
    {
        // Nothing: a grid of potentials and fields.
        None,

        // The conductor the point is inside, its element, and what it holds: a surface.
        Held,

        // The nearest conductor's name, inside or not: a probe row.
        Nearest,
    }

    private string SampleCore(
        in Vec3 point,
        double atSeconds,
        bool cycleMean,
        List<int>? outside,
        Resolve resolve,
        out double potential,
        out Vec3 field,
        out double? distance,
        out string? note,
        out bool averaged,
        out string? conductor,
        out int? element,
        out double? held)
    {
        distance = null;
        note = null;
        averaged = false;
        conductor = null;
        element = null;
        held = null;

        var outsideCount = 0;

        for (var i = 0; i < _model.Fields.Count; i++)
        {
            if (Elements[i].HasDomain && !InsideDomain(i, in point))
            {
                outsideCount++;
                outside?.Add(i);
            }
        }

        try
        {
            if (cycleMean && _driven is not null && HeldAt(atSeconds) is { Window: { } window } at)
            {
                potential = at.Field.CycleMeanPotentialAt(in point, atSeconds, window, CycleMeanSamples);
                field = default;
                averaged = true;
            }
            else if (_driven is not null)
            {
                potential = _driven.PotentialAt(in point, atSeconds);
                field = _driven.ElectricFieldAt(in point, atSeconds);
            }
            else
            {
                potential = _field.PotentialAt(in point);
                field = _field.ElectricFieldAt(in point);
            }
        }
        catch (PointRefusedException refusal)
        {
            // A field may refuse a point rather than return a huge number there (the
            // quadro-logarithmic one does on its singular axis). That is an answer about this
            // point, and a profile crossing it must still report every other row. Only that
            // refusal: any other argument exception is a defect or a malformed request, and
            // turning it into a row would report a million refused points with exit 0.
            potential = double.NaN;
            field = new Vec3(double.NaN, double.NaN, double.NaN);
            note = refusal.Message;
            return ProbeStatus.Refused;
        }

        Span<bool> inside = stackalloc bool[Math.Min(Elements.Count, 256)];
        var solved = Elements.Count <= 256 ? Solved(in point, inside) : default;

        Locate(in point, solved, out var nearest, out var d, out var member, out var depth);

        if (double.IsFinite(d))
        {
            distance = d;
        }

        // A conductor counts only where its element was solved: a declared shape runs on past
        // the edge of its solve - along a cross-section's invariant axis, past a face the shape
        // was carried over, into a box wider than the domain - and a point out there is in no
        // conductor of that element, whatever the shape says. Locate has already left those out.
        var inMetal = depth < 0.0 && member >= 0;

        if (inMetal)
        {
            conductor = (_members[member] as IConductorBounded)?.ConductorAt(in point);
        }
        else if (resolve == Resolve.Nearest && nearest >= 0 && distance is not null)
        {
            conductor = (_members[nearest] as IConductorBounded)?.ConductorAt(in point);
        }

        if (inMetal && resolve == Resolve.Held && conductor is not null)
        {
            element = _leafElements[member] is var e and >= 0 ? e : null;
            held = HeldOf(member, conductor, atSeconds, cycleMean);
        }

        // Outside every domain wins: an electrode's declared shape runs on along a
        // cross-section's invariant axis and past the edge of its solve, so a point out there can
        // be "inside" a shape nothing was solved for, and the value reported is the field's
        // answer outside its domain, not the electrode's potential. Outside every element only
        // when every element is one whose domain can be checked: an unbounded analytic element
        // fills all space, so the point is never outside it.
        var status = _allBounded && outsideCount == Elements.Count
            ? ProbeStatus.OutsideDomain
            : inMetal
                ? ProbeStatus.InsideConductor
                : ProbeStatus.Ok;

        // Adding zero turns a negative zero (the sign of a zero component of -grad phi) into a plain
        // one, so a table does not print -0 for a component that is simply zero.
        potential += 0.0;
        field = new Vec3(field.X + 0.0, field.Y + 0.0, field.Z + 0.0);

        return status;
    }

    private HeldState HeldAt(double atSeconds)
    {
        // One held field per instant, not per point: a sequenced geometry allocates a held copy,
        // and asking a composite for its period walks every member. The cache is one immutable
        // object replaced by reference, so a thread reading it sees an old state or a new one
        // and never half of each; two threads asking at once may both build it, which costs a
        // duplicate and changes no answer.
        if (Volatile.Read(ref _held) is { } cached && cached.At == atSeconds)
        {
            return cached;
        }

        var held = _driven!.AtOperatingPoint(atSeconds);
        var single = held.MonochromaticPeriodSeconds;
        var shortest = held.ShortestPeriodSeconds;

        // One frequency: its period. Several (NaN): the fastest. Nothing oscillating at this
        // operating point (infinity): no cycle - unless the field has no operating points at
        // all, where a drive declared at zero amplitude still names a period to average over.
        double? window = double.IsFinite(single)
            ? single
            : double.IsPositiveInfinity(single) && HasOperatingPoints
                ? null
                : double.IsFinite(shortest) ? shortest : null;

        var state = new HeldState(atSeconds, held, window);
        Volatile.Write(ref _held, state);

        return state;
    }

    private sealed record HeldState(double At, ITimeVaryingField Field, double? Window);

    private HeldState? _held;

    private void RefuseMeanWithoutCycle(double atSeconds, bool cycleMean)
    {
        if (cycleMean && CycleMeanWindowSecondsAt(atSeconds) is null)
        {
            throw new InvalidOperationException(
                "a cycle mean was asked of a field with no drive period at that operating point; "
                + "check CycleMeanWindowSecondsAt first");
        }
    }

    /// <summary>
    /// The potential at a point given in metres, with the conductor the point is inside and the
    /// element that conductor belongs to, and no allocation: the path for a surface, which asks
    /// at every node of a grid and wants neither the field vector nor a row.
    /// </summary>
    /// <param name="pointMetres">Where, in metres.</param>
    /// <param name="atSeconds">The instant; for a cycle mean, the operating point.</param>
    /// <param name="cycleMean">Whether to report the mean over one drive cycle, as <see cref="At(int, Vec3, double, bool)"/> does.</param>
    /// <param name="potential">The potential in volts; NaN where the point was refused.</param>
    /// <param name="conductorDistance">
    /// Signed distance to the nearest conductor surface in metres, negative inside; null where
    /// the field declares no conductors or the point was refused.
    /// </param>
    /// <param name="conductor">The conductor the point is inside; null unless the status is inside-conductor.</param>
    /// <param name="element">The model element that conductor belongs to; null unless the status is inside-conductor.</param>
    /// <param name="held">
    /// What that conductor holds - what the solve pinned there, not what the summed field reads
    /// there - at the instant or as its cycle mean; null unless the status is inside-conductor,
    /// and null for a conductor with no single potential (an edge-profiled board). See
    /// <see cref="IHeldConductors"/>.
    /// </param>
    /// <returns>One of the <see cref="ProbeStatus"/> values, the same one <see cref="At(int, Vec3, double)"/> gives.</returns>
    /// <exception cref="InvalidOperationException">A cycle mean was asked where there is no drive period.</exception>
    /// <remarks>
    /// A point inside a declared shape but outside the solve that shape belongs to is not inside
    /// a conductor: nothing was solved there.
    /// </remarks>
    public string SamplePotential(
        in Vec3 pointMetres,
        double atSeconds,
        bool cycleMean,
        out double potential,
        out double? conductorDistance,
        out string? conductor,
        out int? element,
        out double? held)
    {
        RefuseMeanWithoutCycle(atSeconds, cycleMean);

        return SampleCore(
            in pointMetres, atSeconds, cycleMean, outside: null, Resolve.Held,
            out potential, out _, out conductorDistance, out _, out _, out conductor, out element, out held);
    }

    // What the conductor a member names holds, at the instant or as its cycle mean - asked of
    // that member alone and by name: inside metal the potential is the conductor's, whatever
    // else the model sums there. For a mean, the member of the field held at the operating
    // point, which is the field the mean is taken on.
    private double? HeldOf(int member, string conductor, double atSeconds, bool cycleMean)
    {
        if (!cycleMean || _driven is null)
        {
            return _members[member] is IHeldConductors held ? held.HeldPotentialOf(conductor, atSeconds) : null;
        }

        var state = HeldAt(atSeconds);
        var (members, _) = Leaves(state.Field, _memberElements);

        return member < members.Count && members[member] is IHeldConductors at && state.Window is { } window
            ? at.HeldCycleMeanOf(conductor, atSeconds, window, CycleMeanSamples)
            : null;
    }

    /// <summary>
    /// The finest fraction of the fastest drive period an instant must be placeable to: past it,
    /// the instant is so large that a double cannot say where in the cycle it falls.
    /// </summary>
    public const double InstantResolutionFraction = FieldInstants.ResolutionFraction;

    /// <summary>
    /// Refuses an instant so late that where it falls in this field's drive cycle is decided by
    /// rounding.
    /// </summary>
    /// <param name="atUs">The instant asked for, in microseconds, or null for none.</param>
    /// <param name="path">Where the error is located: the flag, as a JSON Pointer.</param>
    /// <exception cref="EinzelException">The instant is refused.</exception>
    /// <remarks>
    /// Asks how far apart neighbouring doubles are at that instant: when it is more than
    /// <see cref="InstantResolutionFraction"/> of the fastest drive period, the phase drawn is
    /// whichever one the rounding lands on - at 1e300 microseconds on a 1 MHz drive, any phase
    /// at all. A field with no drive has no phase to lose and is never refused here; a negative
    /// or non-finite instant is <see cref="ProbeCommand.CheckInstant"/>'s to refuse.
    /// </remarks>
    public void CheckInstantResolution(double? atUs, string path) =>
        FieldInstants.CheckResolution(atUs * 1e-6, _field, path);

    /// <summary>The warning a time-varying field earns when no instant was asked for.</summary>
    /// <param name="requested">The instant asked for, in microseconds, or absent.</param>
    /// <returns>The warnings to add: empty for a static field or an explicit instant.</returns>
    public IReadOnlyList<ValidityWarning> InstantWarnings(double? requested) =>
        TimeVarying && requested is null
            ?
            [
                new ValidityWarning(
                    "probe.field-at-instant",
                    "this field varies with time (it is driven or sequenced) and no --at-us was "
                    + "given, so every value is the field at t = 0 us and not a field the "
                    + "instrument holds; give --at-us to choose the instant",
                    WarningSeverity.Provenance),
            ]
            : [];

    /// <summary>Converts a point typed in millimetres to metres the way the model's own lengths are.</summary>
    /// <param name="mm">The point in millimetres.</param>
    /// <returns>The point in metres.</returns>
    public static Vec3 MillimetresToMetres(Vec3 mm) =>
        new(mm.X * MetresPerMm, mm.Y * MetresPerMm, mm.Z * MetresPerMm);

    private static bool CanCheckDomain(CompiledField element) =>
        element.Region is not null || element.Solve is not null || element.Solve3D is not null;

    // Millimetres that come back to exactly `si` under * MetresPerMm, or failing that the nearest
    // value on the inside of the face (`inward` is +1 for a lower corner, -1 for an upper one), so
    // a box taken from DomainMm never reaches past the domain it was taken from.
    private static Vec3 ToMm(Vec3 si, int inward) =>
        new(ToMm(si.X, inward), ToMm(si.Y, inward), ToMm(si.Z, inward));

    private static double ToMm(double si, int inward)
    {
        if (!double.IsFinite(si))
        {
            return si * 1e3;
        }

        var mm = si * 1e3;

        for (var step = 0; step < 8 && mm * MetresPerMm != si; step++)
        {
            mm = mm * MetresPerMm < si ? double.BitIncrement(mm) : double.BitDecrement(mm);
        }

        if (mm * MetresPerMm == si)
        {
            return mm;
        }

        // No millimetre value lands on the face exactly; take the nearest one inside it.
        while (inward > 0 ? mm * MetresPerMm < si : mm * MetresPerMm > si)
        {
            mm = inward > 0 ? double.BitIncrement(mm) : double.BitDecrement(mm);
        }

        return mm;
    }

    // The boxes element `index` occupies in the world, in metres: none for an unbounded analytic
    // element, one for a solve or a region, two for a reflected solve.
    private IEnumerable<(Vec3 Min, Vec3 Max)> Occupied(int index)
    {
        var element = _model.Fields[index];

        if (element.Region is { } region)
        {
            yield return (new Vec3(region.MinX, region.MinY, region.MinZ), new Vec3(region.MaxX, region.MaxY, region.MaxZ));
        }
        else if (element.Solve3D is { } v)
        {
            yield return (new Vec3(v.MinX, v.MinY, v.MinZ), new Vec3(v.MaxX, v.MaxY, v.MaxZ));

            if (v.ReflectAboutX is { } p)
            {
                yield return (new Vec3((2.0 * p) - v.MaxX, v.MinY, v.MinZ), new Vec3((2.0 * p) - v.MinX, v.MaxY, v.MaxZ));
            }
        }
        else if (element.Solve is { } plane)
        {
            var half = Math.Max(Math.Abs(plane.MinY), Math.Abs(plane.MaxY));
            var (y0, y1, z0, z1) = plane.Symmetry == SolveSymmetry.Cylindrical
                ? (-plane.MaxY, plane.MaxY, -plane.MaxY, plane.MaxY)
                : (plane.MinY, plane.MaxY, -half, half);

            var boxes = new List<(double X0, double X1)> { (plane.MinX, plane.MaxX) };

            if (plane.ReflectAboutX is { } p)
            {
                boxes.Add(((2.0 * p) - plane.MaxX, (2.0 * p) - plane.MinX));
            }

            foreach (var (x0, x1) in boxes)
            {
                if (_tilts[index] is not { } tilt)
                {
                    yield return (new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
                    continue;
                }

                // The bounding box of the rotated extent: the rotation is about an axis along y,
                // so the four x-z corners carry it.
                double ax = double.PositiveInfinity, az = double.PositiveInfinity;
                double bx = double.NegativeInfinity, bz = double.NegativeInfinity;

                foreach (var (cx, cz) in (ReadOnlySpan<(double, double)>)[(x0, z0), (x0, z1), (x1, z0), (x1, z1)])
                {
                    var world = tilt.FromLocal(new Vec3(cx, 0.0, cz));
                    ax = Math.Min(ax, world.X);
                    bx = Math.Max(bx, world.X);
                    az = Math.Min(az, world.Z);
                    bz = Math.Max(bz, world.Z);
                }

                yield return (new Vec3(ax, y0, az), new Vec3(bx, y1, bz));
            }
        }
    }

    // Whether element `index` has a solved or declared value at p. A reflected solve holds the
    // point if it or its mirror image is in the declared half; a tilted cross-section is asked
    // about the point in its own unrotated frame. Faces are inside: a face node holds a solved
    // value.
    private bool InsideDomain(int index, in Vec3 p)
    {
        var element = _model.Fields[index];

        if (element.Region is { } region)
        {
            return !(region.SignedDistance(in p) > 0.0);
        }

        if (element.Solve is { } plane)
        {
            var q = _tilts[index] is { } tilt ? tilt.ToLocal(in p) : p;

            return InPlane(plane, q.X, q.Y, q.Z)
                || (plane.ReflectAboutX is { } mirror && InPlane(plane, (2.0 * mirror) - q.X, q.Y, q.Z));
        }

        if (element.Solve3D is { } v)
        {
            return InVolume(v, p.X, p.Y, p.Z)
                || (v.ReflectAboutX is { } mirror && InVolume(v, (2.0 * mirror) - p.X, p.Y, p.Z));
        }

        return true;
    }

    private static bool InPlane(CompiledSolvedField plane, double x, double y, double z)
    {
        if (x < plane.MinX || x > plane.MaxX)
        {
            return false;
        }

        // Cylindrical: x is the axis, y the radius, so the second coordinate is the distance from
        // the axis in the y-z plane; a cross-section keeps y and ignores z.
        var second = plane.Symmetry == SolveSymmetry.Cylindrical ? Math.Sqrt((y * y) + (z * z)) : y;

        return second >= plane.MinY && second <= plane.MaxY;
    }

    private static bool InVolume(CompiledSolvedField3D v, double x, double y, double z) =>
        x >= v.MinX && x <= v.MaxX && y >= v.MinY && y <= v.MaxY && z >= v.MinZ && z <= v.MaxZ;
}

/// <summary>Reads the potential and electric field of a model at a point or along a line.</summary>
public static class ProbeCommand
{
    /// <summary>The most samples one line may ask for.</summary>
    public const int MaximumSamples = 1_000_000;

    /// <summary>Probes a model.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="request">Where and when.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="EinzelException">The model does not validate, or the request is malformed.</exception>
    public static ProbeOutcome Execute(string modelPath, ProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Check(request);

        var probe = FieldProbe.Open(modelPath);
        probe.CheckInstantResolution(request.AtUs, "/probe/at-us");
        var at = (request.AtUs ?? 0.0) * 1e-6;
        var samples = new List<ProbeSample>();

        if (request.ToMm is not { } to)
        {
            samples.Add(probe.At(0, request.FromMm, at));
        }
        else
        {
            var from = request.FromMm;
            var last = request.Samples - 1;

            for (var i = 0; i < request.Samples; i++)
            {
                // Ends returned exactly: an interpolated end lands an ulp off and a profile whose
                // last row is not the point asked for cannot be compared with another tool's.
                var position = i == 0
                    ? from
                    : i == last
                        ? to
                        : Lerp(from, to, (double)i / last);

                samples.Add(probe.At(i, position, at));
            }
        }

        return new ProbeOutcome
        {
            ModelPath = probe.ModelPath,
            TimeVarying = probe.TimeVarying,
            InstantUs = probe.TimeVarying ? request.AtUs ?? 0.0 : null,
            Elements = probe.Elements,
            Samples = samples,
            Warnings = [.. probe.Warnings, .. probe.InstantWarnings(request.AtUs), .. SampleWarnings(samples)],
        };
    }

    /// <summary>The profile as CSV, one row per point, with the unit in each column's name.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The CSV text.</returns>
    public static string ToCsv(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        if (outcome.InstantUs is { } instant)
        {
            text.Append(invariant, $"# field at t = {instant:G17} us\n");
        }

        foreach (var warning in outcome.Warnings)
        {
            text.Append(invariant, $"# {warning.Severity}: {warning.Code}: {warning.Message}\n");
        }

        // B columns only for a model that declares a magnet, so every other model's CSV is the
        // file it always was.
        var magnetic = outcome.Samples.Any(s => s.FluxDensityT is not null);

        text.Append(magnetic
            ? "x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,Bx_T,By_T,Bz_T,status,conductor\n"
            : "x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,status,conductor\n");

        foreach (var s in outcome.Samples)
        {
            static string Cell(double? value) =>
                value is { } v ? v.ToString("G17", CultureInfo.InvariantCulture) : string.Empty;

            var b = magnetic
                ? $"{Cell(s.FluxDensityT?[0])},{Cell(s.FluxDensityT?[1])},{Cell(s.FluxDensityT?[2])},"
                : string.Empty;

            text.Append(
                invariant,
                $"{s.PositionMm[0]:G17},{s.PositionMm[1]:G17},{s.PositionMm[2]:G17},{Cell(s.PotentialV)},"
                + $"{Cell(s.FieldVPerM?[0])},{Cell(s.FieldVPerM?[1])},{Cell(s.FieldVPerM?[2])},"
                + $"{b}{s.Status},{s.Conductor}\n");
        }

        return text.ToString();
    }

    private static IEnumerable<ValidityWarning> SampleWarnings(List<ProbeSample> samples)
    {
        // Said once, with a count: a row's status carries the detail, and a thousand-row profile
        // should not repeat one sentence a thousand times.
        var inside = samples.Count(s => s.Status == ProbeStatus.InsideConductor);
        var outside = samples.Count(s => s.Status == ProbeStatus.OutsideDomain);
        var refused = samples.Count(s => s.Status == ProbeStatus.Refused);

        if (inside > 0)
        {
            yield return new ValidityWarning(
                "probe.inside-conductor",
                $"{inside} of {samples.Count} point(s) are inside a conductor: the potential there is "
                + "the electrode's and the field is the interpolant's, which is not a physical field "
                + "inside metal",
                WarningSeverity.Qualified);
        }

        if (outside > 0)
        {
            yield return new ValidityWarning(
                "probe.outside-domain",
                $"{outside} of {samples.Count} point(s) lie outside every element's domain: the "
                + "value reported there is what the field returns outside what was solved or "
                + "declared (not necessarily zero), not a solved value and not a measurement of vacuum",
                WarningSeverity.Qualified);
        }

        if (refused > 0)
        {
            yield return new ValidityWarning(
                "probe.refused",
                $"{refused} of {samples.Count} point(s) were refused by the field; the row's note says why",
                WarningSeverity.Qualified);
        }
    }

    private static Vec3 Lerp(Vec3 a, Vec3 b, double f) =>
        new(a.X + ((b.X - a.X) * f), a.Y + ((b.Y - a.Y) * f), a.Z + ((b.Z - a.Z) * f));

    private static void Check(ProbeRequest request)
    {
        static bool Finite(Vec3 v) =>
            double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        if (!Finite(request.FromMm) || (request.ToMm is { } t && !Finite(t)))
        {
            throw Refusal("/probe", "coordinates must be finite numbers in millimetres");
        }

        CheckInstant(request.AtUs);

        if (request.ToMm is not null
            && (request.Samples < 2 || request.Samples > MaximumSamples))
        {
            throw Refusal(
                "/probe/samples",
                $"a line needs between 2 and {MaximumSamples} samples, both ends included; got {request.Samples}");
        }
    }

    /// <summary>Refuses an instant no field could be asked about, once, before anything is sampled.</summary>
    /// <param name="atUs">The instant in microseconds, or absent.</param>
    /// <exception cref="EinzelException">The instant is not finite, or is before the run's clock starts.</exception>
    /// <remarks>
    /// Checked up front so that a bad instant is one AGT-3 error naming the flag, rather than an
    /// exception raised inside the field stack at every one of a million points. Negative is
    /// refused because a run's clock starts at zero: a sequenced field has no phase before it,
    /// and an answer there would be the first phase's under a time the instrument never reaches.
    /// </remarks>
    /// <param name="path">Where the error is located: the verb's own flag, as a JSON Pointer.</param>
    public static void CheckInstant(double? atUs, string path = "/probe/at-us")
    {
        if (atUs is { } instant && (!double.IsFinite(instant) || instant < 0.0))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = "--at-us must be a finite, non-negative number of microseconds; a run's clock starts at 0",
                Observed = new ObservedValue(instant, "us"),
                Suggestion = "give --at-us 0 or a later instant, in microseconds",
            });
        }
    }

    private static EinzelException Refusal(string path, string constraint) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = "see 'einzel probe' for the flags",
        });
}
