using System.Globalization;
using System.Text;

using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;

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
    /// The point lies outside the declared domain of every element, so every element
    /// contributes nothing: zero is the model's answer and not a measurement.
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
    string? Note);

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
/// </remarks>
public sealed class FieldProbe
{
    private readonly IElectrostaticField _field;
    private readonly CompiledModel _model;

    private FieldProbe(
        CompiledModel model,
        IElectrostaticField field,
        IReadOnlyList<ValidityWarning> warnings,
        string modelPath)
    {
        _model = model;
        _field = field;
        Warnings = warnings;
        ModelPath = modelPath;
        Elements =
        [
            .. model.Fields.Select((e, i) => new ProbeElement(
                i,
                e.Kind.ToString(),
                e.Solve is not null || e.Solve3D is not null ? "solved" : "analytic",
                (e.Solve?.CellSize * 1e3) ?? (e.Solve3D?.CellSize * 1e3),
                CanCheckDomain(e))),
        ];
    }

    /// <summary>The model, as an absolute path.</summary>
    public string ModelPath { get; }

    /// <summary>What the assembly said about the field.</summary>
    public IReadOnlyList<ValidityWarning> Warnings { get; }

    /// <summary>The elements and how they are known.</summary>
    public IReadOnlyList<ProbeElement> Elements { get; }

    /// <summary>Whether the assembled field varies with time.</summary>
    public bool TimeVarying => _field is ITimeVaryingField;

    /// <summary>Validates and assembles the field of a model file.</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>A probe over its field.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    public static FieldProbe Open(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var (field, warnings) = FieldAssembly.BuildReported(model);

        return new FieldProbe(model, field, warnings, absolute);
    }

    /// <summary>The union of the solved elements' domains, in millimetres, when there is one.</summary>
    /// <returns>Min and max corners, or null when no element has a solved domain.</returns>
    /// <remarks>
    /// A cross-section's z and an axisymmetric solve's radius are not bounded in the way a box is, so
    /// the box is the declared one: a cross-section spans its plane, z from -radius to +radius of
    /// its own x-y extent, and an axisymmetric solve is a cylinder whose box spans plus or minus its
    /// radius.
    /// </remarks>
    public (Vec3 Min, Vec3 Max)? DomainMm()
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        var any = false;

        void Take(double x0, double x1, double y0, double y1, double z0, double z1)
        {
            any = true;
            minX = Math.Min(minX, x0);
            maxX = Math.Max(maxX, x1);
            minY = Math.Min(minY, y0);
            maxY = Math.Max(maxY, y1);
            minZ = Math.Min(minZ, z0);
            maxZ = Math.Max(maxZ, z1);
        }

        foreach (var element in _model.Fields)
        {
            if (element.Solve3D is { } v)
            {
                Take(v.MinX, v.MaxX, v.MinY, v.MaxY, v.MinZ, v.MaxZ);
            }
            else if (element.Solve is { } plane)
            {
                var half = Math.Max(Math.Abs(plane.MinY), Math.Abs(plane.MaxY));

                if (plane.Symmetry == SolveSymmetry.Cylindrical)
                {
                    Take(plane.MinX, plane.MaxX, -plane.MaxY, plane.MaxY, -plane.MaxY, plane.MaxY);
                }
                else
                {
                    Take(plane.MinX, plane.MaxX, plane.MinY, plane.MaxY, -half, half);
                }
            }
        }

        return any
            ? (new Vec3(minX * 1e3, minY * 1e3, minZ * 1e3), new Vec3(maxX * 1e3, maxY * 1e3, maxZ * 1e3))
            : null;
    }

    /// <summary>Evaluates the field at a point.</summary>
    /// <param name="index">The row number.</param>
    /// <param name="positionMm">Where, in millimetres.</param>
    /// <param name="atSeconds">The instant, used only by a time-varying field.</param>
    /// <returns>The sample; a refused point is a row with status refused, never an exception.</returns>
    public ProbeSample At(int index, Vec3 positionMm, double atSeconds)
    {
        var point = new Vec3(positionMm.X * 1e-3, positionMm.Y * 1e-3, positionMm.Z * 1e-3);
        var position = new[] { positionMm.X, positionMm.Y, positionMm.Z };

        var outside = new List<int>();

        for (var i = 0; i < _model.Fields.Count; i++)
        {
            if (Elements[i].HasDomain && OutsideDomain(_model.Fields[i], point))
            {
                outside.Add(i);
            }
        }

        double potential;
        Vec3 field;

        try
        {
            if (_field is ITimeVaryingField driven)
            {
                potential = driven.PotentialAt(in point, atSeconds);
                field = driven.ElectricFieldAt(in point, atSeconds);
            }
            else
            {
                potential = _field.PotentialAt(in point);
                field = _field.ElectricFieldAt(in point);
            }
        }
        catch (ArgumentException refusal)
        {
            // An analytic field may refuse a point rather than return a huge number there (the
            // quadro-logarithmic one does on its singular axis). That is an answer about this point,
            // and a profile crossing it must still report every other row.
            return new ProbeSample(
                index, position, null, null, null, ProbeStatus.Refused, null, null, outside,
                refusal.Message);
        }

        double? distance = null;
        string? conductor = null;

        if (_field is IConductorBounded bounded)
        {
            var d = bounded.SignedDistanceToConductor(in point);

            if (double.IsFinite(d))
            {
                distance = d * 1e3;
                conductor = bounded.ConductorAt(in point);
            }
        }

        // Outside every element only when every element is one whose domain we can check: an
        // unbounded analytic element fills all space, so the point is never outside it.
        var allBounded = Elements.Count > 0 && Elements.All(e => e.HasDomain);
        var status = distance is < 0.0
            ? ProbeStatus.InsideConductor
            : allBounded && outside.Count == Elements.Count
                ? ProbeStatus.OutsideDomain
                : ProbeStatus.Ok;

        // Adding zero turns a negative zero (the sign of a zero component of -grad phi) into a plain
        // one, so a table does not print -0 for a component that is simply zero.
        potential += 0.0;
        field = new Vec3(field.X + 0.0, field.Y + 0.0, field.Z + 0.0);

        var magnitude = Math.Sqrt((field.X * field.X) + (field.Y * field.Y) + (field.Z * field.Z));

        return new ProbeSample(
            index, position, potential, [field.X, field.Y, field.Z], magnitude, status,
            conductor, distance, outside, null);
    }

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

    private static bool CanCheckDomain(CompiledField element) =>
        element.Region is not null
        || (element.Solve is { } plane && plane.TiltHalfTurns == 0.0 && plane.ReflectAboutX is null)
        || (element.Solve3D is { } volume && volume.ReflectAboutX is null);

    private static bool OutsideDomain(CompiledField element, in Vec3 p)
    {
        if (element.Region is { } region && region.SignedDistance(in p) > 0.0)
        {
            return true;
        }

        if (element.Solve is { } plane)
        {
            if (p.X < plane.MinX || p.X > plane.MaxX)
            {
                return true;
            }

            // Cylindrical: x is the axis, y the radius, so the second coordinate is the distance
            // from the axis in the y-z plane; a cross-section keeps y and ignores z.
            var second = plane.Symmetry == SolveSymmetry.Cylindrical
                ? Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z))
                : p.Y;

            return second < plane.MinY || second > plane.MaxY;
        }

        if (element.Solve3D is { } v)
        {
            return p.X < v.MinX || p.X > v.MaxX || p.Y < v.MinY || p.Y > v.MaxY
                || p.Z < v.MinZ || p.Z > v.MaxZ;
        }

        return false;
    }
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

        text.Append("x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,status,conductor\n");

        foreach (var s in outcome.Samples)
        {
            static string Cell(double? value) =>
                value is { } v ? v.ToString("G17", CultureInfo.InvariantCulture) : string.Empty;

            text.Append(
                invariant,
                $"{s.PositionMm[0]:G17},{s.PositionMm[1]:G17},{s.PositionMm[2]:G17},{Cell(s.PotentialV)},"
                + $"{Cell(s.FieldVPerM?[0])},{Cell(s.FieldVPerM?[1])},{Cell(s.FieldVPerM?[2])},"
                + $"{s.Status},{s.Conductor}\n");
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
                $"{outside} of {samples.Count} point(s) lie outside every element's declared domain: "
                + "zero potential and field there is the model's answer (nothing was solved or "
                + "declared at that place), not a measurement of vacuum",
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

        if (request.AtUs is { } instant && !double.IsFinite(instant))
        {
            throw Refusal("/probe/at-us", "the instant must be a finite number of microseconds");
        }

        if (request.ToMm is not null
            && (request.Samples < 2 || request.Samples > MaximumSamples))
        {
            throw Refusal(
                "/probe/samples",
                $"a line needs between 2 and {MaximumSamples} samples, both ends included; got {request.Samples}");
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
