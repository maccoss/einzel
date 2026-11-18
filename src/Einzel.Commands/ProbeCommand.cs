using System.Diagnostics;
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
    /// tilted cross-section's rotated extent included), or inside a conductor's declared shape
    /// where the shape runs on past the edge of its own solve - metal in the document that no
    /// solve holds at this point, whatever other element covers it (its own element's mirror image
    /// does hold it, as vacuum). The value reported is what the field
    /// returns there - a solve's value outside its grid, nothing from a bounded analytic element,
    /// another element's answer - which is not that conductor's and need not be zero. This wins
    /// over <see cref="InsideConductor"/> and is never <see cref="Ok"/>; a sample naming its
    /// conductor says which shape in its note.
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
/// <param name="Conductor">
/// The nearest conductor's name, among the conductors of elements solved at the point where there
/// are any (outside every domain, among all declared conductors); absent where the field knows none.
/// </param>
/// <param name="ConductorDistanceMm">
/// Signed distance to that conductor's surface in millimetres, negative inside - so negative only
/// with the status inside-conductor where any element was solved at the point; absent where the
/// field declares no conductors.
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
    // The conductor-bearing leaves of the field, as the assembly that built it handed them out:
    // where each one's metal is, the solve that says what it holds, its element, and which half
    // of a reflected solve it is.
    private readonly IReadOnlyList<FieldLeaf> _leaves;
    private readonly bool _hasOperatingPoints;
    private readonly Einzel.Fields.Magnetic.IMagneticField? _magnetic;
    private readonly CompiledModel _model;
    private readonly RotatedField?[] _tilts;
    private readonly bool _allBounded;

    // Each element's reflection, read once off its image leaf - the reflection the builder made
    // the field's sum with - and null for an element that is not reflected. Every question this
    // probe asks about a mirror (which half, whose box, whether the halves overlap) reads it here
    // rather than re-deriving it from the model per sample.
    private readonly Reflection?[] _reflections;

    private FieldProbe(
        CompiledModel model,
        IElectrostaticField field,
        IReadOnlyList<ValidityWarning> warnings,
        IReadOnlyList<FieldLeaf> leaves,
        string modelPath,
        string modelHash)
    {
        // Checked here, where the leaves are used, and not where they are made: the assembly
        // builds every run's field, and a run reads no leaf. An element kind that solves a
        // geometry and hands out no leaf would be probed and drawn with its metal missing.
        for (var index = 0; index < model.Fields.Count; index++)
        {
            if ((model.Fields[index].Solve is not null || model.Fields[index].Solve3D is not null)
                && !leaves.Any(l => l.Element == index))
            {
                throw new InvalidOperationException(
                    $"field element {index} ({model.Fields[index].Kind}) is a solved geometry and the assembly handed out no leaf for its conductors");
            }
        }

        _leaves = leaves;
        _reflections = new Reflection?[model.Fields.Count];

        foreach (var leaf in leaves)
        {
            if (leaf.Image is { } image)
            {
                _reflections[leaf.Element] = image;
            }
        }

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

        // The rotation each tilted cross-section is evaluated through: the element's own rotated
        // field, handed out on its leaves, so "is this point in that solve" is asked of the point
        // the field itself asks the solve about - one rotation, not a second built to agree.
        _tilts = new RotatedField?[model.Fields.Count];

        foreach (var leaf in leaves)
        {
            if (leaf.Tilt is { } tilt)
            {
                _tilts[leaf.Element] = tilt;
            }
        }

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

    // How many elements' domain flags live on the stack; a model with more borrows a buffer, so
    // the rule that a conductor counts only where its element was solved holds at any size.
    private const int StackElements = 256;

    // Which elements' solves (or regions) contain a point: true for an element with no domain
    // to check, which fills all space. The same test the outside count reports.
    private void Solved(in Vec3 point, Span<bool> inside)
    {
        for (var i = 0; i < inside.Length; i++)
        {
            inside[i] = !(Elements[i].HasDomain && !InsideDomain(i, in point));
        }
    }

    // ONE WALK of the leaves' conductors, giving two answers: the nearest conductor of any leaf
    // (what a probe row names, and its distance), and the nearest among the leaves whose own
    // half was solved at the point (the conductor the point is IN). Only those compete for
    // the second: a declared shape running past its solve would otherwise win wherever it was
    // nearest, and hide a conductor another element really has there. The rule is the
    // smallest signed distance, the first on a tie.
    //
    // The names come with the distances, from the same walk of each leaf's conductors: asking
    // the leaf again for the name walked them a second time for every sample in metal. A caller
    // that wants no name - a grid of potentials - is not charged for one.
    private void Locate(
        in Vec3 point,
        ReadOnlySpan<bool> inside,
        bool named,
        out int nearest,
        out double nearestDistance,
        out string? nearestName,
        out int held,
        out double heldDistance,
        out string? heldName)
    {
        nearest = held = -1;
        nearestDistance = heldDistance = double.PositiveInfinity;
        nearestName = heldName = null;

        for (var i = 0; i < _leaves.Count; i++)
        {
            string? name = null;
            var d = named
                ? _leaves[i].Shape.NearestConductor(in point, out name)
                : _leaves[i].Shape.SignedDistanceToConductor(in point);

            if (d < nearestDistance)
            {
                nearestDistance = d;
                nearest = i;
                nearestName = name;
            }

            // Whether this leaf's own half was solved at the point, from the element's answer
            // already taken (Solved): the same test for an unreflected element, so it is not made
            // twice per point. A reflected element's answer covers both halves, and only then is
            // the leaf's own half asked.
            if (d < heldDistance && inside[_leaves[i].Element]
                && (_reflections[_leaves[i].Element] is null || InsideLeaf(_leaves[i], in point)))
            {
                heldDistance = d;
                held = i;
                heldName = name;
            }
        }
    }

    // How near zero a signed distance is "on the surface", in metres: far below any cell and far
    // above the rounding of a coordinate.
    private const double SurfaceTolerance = 1e-12;

    // Whether a point lies on the edge of a leaf's own half of its solve.
    private bool OnDomainEdge(FieldLeaf leaf, in Vec3 p)
    {
        static bool At(double value, double face) => Math.Abs(value - face) <= SurfaceTolerance;

        var element = _model.Fields[leaf.Element];

        if (element.Solve is { } plane)
        {
            var q = _tilts[leaf.Element] is { } tilt ? tilt.ToLocal(in p) : p;
            var x = leaf.Image is { } mirror ? mirror.ReflectX(q.X) : q.X;
            var second = plane.Symmetry == SolveSymmetry.Cylindrical ? Math.Sqrt((q.Y * q.Y) + (q.Z * q.Z)) : q.Y;

            return At(x, plane.MinX) || At(x, plane.MaxX) || At(second, plane.MinY) || At(second, plane.MaxY);
        }

        if (element.Solve3D is { } volume)
        {
            var x = leaf.Image is { } mirror ? mirror.ReflectX(p.X) : p.X;

            return At(x, volume.MinX) || At(x, volume.MaxX) || At(p.Y, volume.MinY) || At(p.Y, volume.MaxY)
                || At(p.Z, volume.MinZ) || At(p.Z, volume.MaxZ);
        }

        return false;
    }

    /// <summary>
    /// The range the potential can take anywhere, from what the conductors hold: for a model
    /// whose every field element is a solved geometry, the sum over elements of the lowest and
    /// highest potential any of its conductors holds, earth included.
    /// </summary>
    /// <param name="atSeconds">The instant; for a cycle mean, the operating point.</param>
    /// <param name="cycleMean">Whether the range is of the mean over one drive cycle.</param>
    /// <returns>
    /// The range in volts, or null where it cannot be stated: an analytic element fills all space
    /// and has no such bound, and a driven edge-profiled board has no single potential to take
    /// it from.
    /// </returns>
    /// <remarks>
    /// A solved field has no charge in it, so it obeys the maximum principle: nowhere in the
    /// domain is it above the highest potential on its boundary or below the lowest, and the
    /// boundary is the conductors and the earthed edge. A reading outside that range is not the
    /// field's - it is the interpolant overshooting beside a step, which a cubic does by a few
    /// percent - so this is what a picture's scale and its stated range are held to.
    /// </remarks>
    public (double Low, double High)? AppliedRange(double atSeconds, bool cycleMean)
    {
        RefuseMeanWithoutCycle(atSeconds, cycleMean);

        if (ElementRanges(atSeconds, cycleMean) is not { } ranges)
        {
            return null;
        }

        // A reflected solve whose plane lies inside its own box overlaps its image, and somewhere
        // the field is the two summed: within twice its range there. Summed over elements only
        // where they can overlap: two solves side by side are never both answering at one point.
        return Combined([.. ranges.Select(r => _reflections[r.Index] is { Overlaps: true } ? (r.Index, 2.0 * r.Low, 2.0 * r.High) : r)]);
    }

    /// <summary>
    /// The range the potential can take at one point, from what the conductors hold: the sum, over
    /// the elements answering there, of each one's range - a reflected solve's counted once for
    /// each of its halves that answers at the point.
    /// </summary>
    /// <param name="pointMetres">The point, in metres.</param>
    /// <param name="atSeconds">The instant; for a cycle mean, the operating point.</param>
    /// <param name="cycleMean">Whether the range is of the mean over one drive cycle.</param>
    /// <returns>The range in volts, earth included; null where <see cref="AppliedRange"/> is null.</returns>
    /// <remarks>
    /// <see cref="AppliedRange"/> is a bound for anywhere, and so for an overlapping reflected
    /// solve it is twice the element's range everywhere - which let a 560 V overshoot beside a
    /// 500 V face, far from where the halves overlap, pass under a bound of 1000 V. Asked per
    /// point, it is doubled only where both halves answer, and an element whose solve does not
    /// reach the point adds nothing (a solve answers zero outside its box, which earth already
    /// covers). Never under what the field can be: each half's sum is within its range wherever
    /// it answers, and it answers nowhere else.
    /// </remarks>
    public (double Low, double High)? AppliedRangeAt(in Vec3 pointMetres, double atSeconds, bool cycleMean)
    {
        RefuseMeanWithoutCycle(atSeconds, cycleMean);

        if (ElementRanges(atSeconds, cycleMean) is not { } ranges)
        {
            return null;
        }

        double low = 0.0, high = 0.0;

        foreach (var (index, elementLow, elementHigh) in ranges)
        {
            var halves = HalvesAnswering(index, in pointMetres);
            low += halves * elementLow;
            high += halves * elementHigh;
        }

        return (low, high);
    }

    // How many halves of an element's solve answer at a point: one or none for a solve that is not
    // reflected, and for a reflected one the declared half where it holds the point and the image
    // where it does and answers there (Reflection.ImageAnswersAt) - two only where they overlap.
    private int HalvesAnswering(int index, in Vec3 p)
    {
        var element = _model.Fields[index];
        var mirror = _reflections[index];

        if (element.Solve is { } plane)
        {
            var q = _tilts[index] is { } tilt ? tilt.ToLocal(in p) : p;
            var declared = InPlane(plane, q.X, q.Y, q.Z) && (mirror is null || mirror.DeclaredAnswersAt(q.X)) ? 1 : 0;
            var image = mirror is { } m && m.ImageAnswersAt(q.X) && InPlane(plane, m.ReflectX(q.X), q.Y, q.Z) ? 1 : 0;
            return declared + image;
        }

        if (element.Solve3D is { } v)
        {
            var declared = InVolume(v, p.X, p.Y, p.Z) && (mirror is null || mirror.DeclaredAnswersAt(p.X)) ? 1 : 0;
            var image = mirror is { } m && m.ImageAnswersAt(p.X) && InVolume(v, m.ReflectX(p.X), p.Y, p.Z) ? 1 : 0;
            return declared + image;
        }

        return InsideDomain(index, in p) ? 1 : 0;
    }

    // Each element's own range - one half's, for a reflected solve - once per instant: a surface
    // asks at every node, and each element's range is a walk of its conductors' held potentials.
    // Null where any element has none to state.
    private IReadOnlyList<(int Index, double Low, double High)>? ElementRanges(double atSeconds, bool cycleMean)
    {
        if (Volatile.Read(ref _ranges) is { } cached && cached.At == atSeconds && cached.CycleMean == cycleMean)
        {
            return cached.Ranges;
        }

        var computed = ComputeElementRanges(atSeconds, cycleMean);
        Volatile.Write(ref _ranges, new RangeState(atSeconds, cycleMean, computed));
        return computed;
    }

    private sealed record RangeState(double At, bool CycleMean, IReadOnlyList<(int Index, double Low, double High)>? Ranges);

    private RangeState? _ranges;

    private List<(int Index, double Low, double High)>? ComputeElementRanges(double atSeconds, bool cycleMean)
    {
        var ranges = new List<(int Index, double Low, double High)>();

        for (var index = 0; index < _model.Fields.Count; index++)
        {
            var element = _model.Fields[index];

            if (element.Kind is CompiledFieldKind.FieldFree or CompiledFieldKind.UniformMagnetic)
            {
                continue;
            }

            IEnumerable<(string Name, bool Profiled, IEnumerable<double> Profile)> electrodes;
            bool isStatic;

            if (element.Solve is { } plane)
            {
                electrodes = plane.Electrodes.Select(e => (
                    e.Name, e.Shape == ElectrodeShape.EdgeProfile, e.Profile.Select(point => point.Potential)));
                isStatic = plane.Drives.Count == 0 && plane.Stages.Count == 0;
            }
            else if (element.Solve3D is { } volume)
            {
                electrodes = volume.Electrodes.Select(e => (e.Name, false, Enumerable.Empty<double>()));
                isStatic = volume.Drives.Count == 0 && volume.Stages.Count == 0;
            }
            else
            {
                return null;
            }

            var member = -1;

            for (var k = 0; k < _leaves.Count && member < 0; k++)
            {
                member = _leaves[k].Element == index ? k : -1;
            }

            // Earth is on every solve's boundary, or may be: including it can only widen the range.
            double elementLow = 0.0, elementHigh = 0.0;

            foreach (var (name, profiled, profile) in electrodes)
            {
                if (profiled)
                {
                    if (!isStatic)
                    {
                        return null;
                    }

                    foreach (var volts in profile)
                    {
                        elementLow = Math.Min(elementLow, volts);
                        elementHigh = Math.Max(elementHigh, volts);
                    }

                    continue;
                }

                if (member < 0 || HeldOf(member, name, atSeconds, cycleMean) is not { } holds || !double.IsFinite(holds))
                {
                    return null;
                }

                elementLow = Math.Min(elementLow, holds);
                elementHigh = Math.Max(elementHigh, holds);
            }

            ranges.Add((index, elementLow, elementHigh));
        }

        return ranges;
    }

    // Elements whose occupied boxes meet - touching counts, since a shared face is in both - are
    // summed; groups that do not meet are each their own range, and the whole is their union. A
    // range always contains earth, so the union is the lowest low and the highest high.
    private (double Low, double High) Combined(List<(int Index, double Low, double High)> ranges)
    {
        var group = Enumerable.Range(0, ranges.Count).ToArray();

        int Find(int i) => group[i] == i ? i : group[i] = Find(group[i]);

        // A cross-section repeats along its invariant axis without end, so it meets anything at
        // its x and y whatever z the drawing gives it; tilted, that axis is turned into x as well,
        // so it is taken to meet everything. Erring wide only sums more, never clamps a real value.
        var boxes = ranges.Select(r => _model.Fields[r.Index].Solve is { Symmetry: not SolveSymmetry.Cylindrical } section
            ? section.TiltHalfTurns != 0.0
                ? [(new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity), new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity))]
                : Occupied(r.Index).Select(b => (new Vec3(b.Min.X, b.Min.Y, double.NegativeInfinity), new Vec3(b.Max.X, b.Max.Y, double.PositiveInfinity))).ToList()
            : Occupied(r.Index).ToList()).ToList();

        static bool Meet((Vec3 Min, Vec3 Max) a, (Vec3 Min, Vec3 Max) b) =>
            a.Min.X <= b.Max.X && b.Min.X <= a.Max.X
            && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y
            && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

        for (var i = 0; i < ranges.Count; i++)
        {
            for (var j = i + 1; j < ranges.Count; j++)
            {
                if (boxes[i].Any(a => boxes[j].Any(b => Meet(a, b))))
                {
                    group[Find(i)] = Find(j);
                }
            }
        }

        double low = 0.0, high = 0.0;

        foreach (var members in Enumerable.Range(0, ranges.Count).GroupBy(Find))
        {
            low = Math.Min(low, members.Sum(i => ranges[i].Low));
            high = Math.Max(high, members.Sum(i => ranges[i].High));
        }

        return (low, high);
    }

    // Whether a leaf's own half of its solve holds the point. The declared half is solved in
    // the declared box and its image in that box's reflection, and each is metal only there: a
    // shape declared running past the mirror plane is replaced, beyond the plane, by the image
    // of what is on the near side. A tilted cross-section is asked in its own unrotated frame.
    // Faces are inside: a face node holds a solved value.
    private bool InsideLeaf(FieldLeaf leaf, in Vec3 p)
    {
        var element = _model.Fields[leaf.Element];

        if (element.Solve is { } plane)
        {
            var q = _tilts[leaf.Element] is { } tilt ? tilt.ToLocal(in p) : p;
            var x = leaf.Image is { } mirror ? mirror.ReflectX(q.X) : q.X;

            return InPlane(plane, x, q.Y, q.Z);
        }

        if (element.Solve3D is { } volume)
        {
            var x = leaf.Image is { } mirror ? mirror.ReflectX(p.X) : p.X;

            return InVolume(volume, x, p.Y, p.Z);
        }

        return InsideDomain(leaf.Element, in p);
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
        var (field, warnings, leaves) = FieldAssembly.BuildMapped(model);

        return new FieldProbe(model, field, warnings, leaves, absolute, ContentHash.OfText(modelText));
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
    /// Where a cycle mean at an operating point opens its window: the instant itself, or zero
    /// where a double cannot place the instant in the drive's cycle.
    /// </summary>
    /// <param name="atSeconds">The operating point.</param>
    /// <returns>The window's opening in seconds; null where there is no drive cycle.</returns>
    /// <remarks>
    /// The field is held at the operating point either way, and a whole period of a periodic
    /// drive has one mean wherever it opens - but a caller that states its window says where, so
    /// it is told when the window had to move rather than finding it moved.
    /// </remarks>
    public double? CycleMeanWindowOpensAt(double atSeconds) =>
        _driven is not null && HeldAt(atSeconds) is { Window: not null } state ? state.From : null;

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
    /// The instant; for a cycle mean, the operating point the mean is taken at, and the opening of
    /// its window where a double can place the instant in the cycle (zero otherwise - a whole
    /// period has the same mean wherever it opens).
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
            in point, atSeconds, cycleMean, outside, Resolve.Nearest, wantField: true,
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
                note is null ? "cycle mean: potential only" : "cycle mean: potential only; " + note);
        }

        var magnitude = Math.Sqrt((field.X * field.X) + (field.Y * field.Y) + (field.Z * field.Z));

        return new ProbeSample(
            index, position, potential, [field.X, field.Y, field.Z], magnitude, status,
            conductor, distance * 1e3, outside, note)
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
            in pointMetres, atSeconds, cycleMean: false, outside: null, Resolve.None, wantField: true,
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
        bool wantField,
        out double potential,
        out Vec3 field,
        out double? distance,
        out string? note,
        out bool averaged,
        out string? conductor,
        out int? element,
        out double? held)
    {
        // Each element's domain once: the outside count, the outside list and which elements'
        // conductors may compete all come from these flags.
        var count = Elements.Count;
        bool[]? rented = count > StackElements ? System.Buffers.ArrayPool<bool>.Shared.Rent(count) : null;

        try
        {
            Span<bool> inside = rented is null ? stackalloc bool[StackElements] : rented;
            inside = inside[..count];
            Solved(in point, inside);

            return SampleSolved(
                in point, inside, atSeconds, cycleMean, outside, resolve, wantField,
                out potential, out field, out distance, out note, out averaged, out conductor, out element, out held);
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<bool>.Shared.Return(rented);
            }
        }
    }

    private string SampleSolved(
        in Vec3 point,
        ReadOnlySpan<bool> inside,
        double atSeconds,
        bool cycleMean,
        List<int>? outside,
        Resolve resolve,
        bool wantField,
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

        for (var i = 0; i < inside.Length; i++)
        {
            if (!inside[i])
            {
                outsideCount++;
                outside?.Add(i);
            }
        }

        try
        {
            if (cycleMean && _driven is not null && HeldAt(atSeconds) is { Window: { } window } at)
            {
                potential = at.Field.CycleMeanPotentialAt(in point, at.From, window, CycleMeanSamples);
                field = default;
                averaged = true;
            }
            else if (_driven is not null)
            {
                // A surface wants the potential alone: the field is not evaluated to be dropped.
                potential = _driven.PotentialAt(in point, atSeconds);
                field = wantField ? _driven.ElectricFieldAt(in point, atSeconds) : default;
            }
            else
            {
                potential = _field.PotentialAt(in point);
                field = wantField ? _field.ElectricFieldAt(in point) : default;
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

        Locate(
            in point, inside, resolve != Resolve.None,
            out var nearest, out var d, out var nearestName, out var member, out var depth, out var memberName);

        var allOutside = _allBounded && outsideCount == Elements.Count;

        // A conductor counts only where its element was solved: a declared shape runs on past
        // the edge of its solve - along a cross-section's invariant axis, past a face the shape
        // was carried over, into a box wider than the domain - and a point out there is in no
        // conductor of that element, whatever the shape says. Locate has already left those out.
        // Or on its surface, where the surface is the edge of the solve: a conductor carried to
        // the domain's edge has its face on the last row of nodes, which hold what the conductor
        // holds, and a sample there is at distance zero - "not inside" by a strict test, so it
        // was drawn at whatever the interpolant read beside the step.
        var inMetal = member >= 0
            && (depth < 0.0 || (depth <= SurfaceTolerance && OnDomainEdge(_leaves[member], in point)));

        // What the row says about conductors agrees with its status. Where an element that has
        // conductors was solved at the point, the distance and the name are of the nearest
        // conductor among those elements' - so a point inside a shape that runs past its solve
        // does not read "ok" beside a negative distance to a conductor it is "in". Where none
        // was - outside every solve, whether or not an unbounded analytic element (which has no
        // conductors) fills all space - the row reports the nearest declared conductor, as it
        // always has.
        var (reported, reportedDistance) = member >= 0 ? (member, depth) : (nearest, d);

        if (double.IsFinite(reportedDistance))
        {
            distance = reportedDistance;
        }

        if (inMetal && resolve != Resolve.None)
        {
            conductor = memberName;
        }
        else if (resolve == Resolve.Nearest && reported >= 0 && distance is not null)
        {
            conductor = member >= 0 ? memberName : nearestName;
        }

        // A point inside a declared shape whose element was not solved there: metal in the
        // document and nothing in the solve. It is outside what that solve holds, so its status
        // is outside-domain and never ok - ok beside "inside the shape of an electrode" is a
        // reading of a field nobody solved for that metal - and it says so whoever is asking: a
        // surface or a grid of potentials is told as well as a probe row. The name is asked for
        // here when the walk did not fetch it, which is only on this path. Its own element must not
        // have been solved here: a shape declared past a mirror plane lies in its image's half,
        // which that element's solve does hold - as vacuum, the image of the declared side.
        var pastShape = !inMetal && d < 0.0 && nearest >= 0 && !inside[_leaves[nearest].Element];

        if (pastShape)
        {
            var past = nearestName ?? _leaves[nearest].Shape.ConductorAt(in point) ?? "a conductor";

            note = string.Create(
                CultureInfo.InvariantCulture,
                $"inside the declared shape of '{past}' (element {_leaves[nearest].Element}), which runs past the edge of its solve: nothing was solved for it here");
        }

        if (inMetal && resolve == Resolve.Held && conductor is not null)
        {
            element = _leaves[member].Element;
            held = HeldOf(member, conductor, atSeconds, cycleMean);
        }

        // Outside every domain wins: an electrode's declared shape runs on along a
        // cross-section's invariant axis and past the edge of its solve, so a point out there can
        // be "inside" a shape nothing was solved for, and the value reported is the field's
        // answer outside its domain, not the electrode's potential. Outside every element only
        // when every element is one whose domain can be checked: an unbounded analytic element
        // fills all space, so the point is never outside it. And outside the domain, too, where
        // the point is in a declared shape past its own solve even though another element covers
        // it: what the solves hold there is not that metal, and "ok" would say it is free space.
        var status = allOutside || pastShape
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

        // Each leaf's solve held on its own, so leaf k of the held field is leaf k held - by
        // construction, not by taking the held composite apart.
        // Held once per solve, not once per leaf: a reflected solve's two leaves share one held
        // source, and holding it twice copied a sequenced solve twice per instant.
        var pieces = new IHeldConductors[_leaves.Count];
        var heldOnce = new Dictionary<IHeldConductors, IHeldConductors>(ReferenceEqualityComparer.Instance);

        for (var k = 0; k < pieces.Length; k++)
        {
            var source = _leaves[k].Held;

            if (!heldOnce.TryGetValue(source, out var at))
            {
                at = source is ITimeVaryingField timed && timed.AtOperatingPoint(atSeconds) is IHeldConductors piece ? piece : source;
                heldOnce[source] = at;
            }

            pieces[k] = at;
        }

        var from = window is { } w && FieldInstants.Resolvable(atSeconds, w) ? atSeconds : 0.0;
        var state = new HeldState(atSeconds, held, window, pieces, from);
        Volatile.Write(ref _held, state);

        return state;
    }

    // `From` is where a cycle mean's window opens: the instant itself where a double can place it in
    // the cycle, otherwise zero. The held field keeps the operating point whatever instant it is
    // asked at, and a whole period of a periodic drive has the same mean wherever it starts, so
    // the instant only picks the operating point; a window opened at an instant too late to
    // resolve would sample one rounded phase over and over.
    private sealed record HeldState(double At, ITimeVaryingField Field, double? Window, IReadOnlyList<IHeldConductors> Leaves, double From);

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
            in pointMetres, atSeconds, cycleMean, outside: null, Resolve.Held, wantField: false,
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
            return _leaves[member].Held.HeldPotentialOf(conductor, atSeconds);
        }

        var state = HeldAt(atSeconds);

        return state.Window is { } window
            ? state.Leaves[member].HeldCycleMeanOf(conductor, state.From, window, CycleMeanSamples)
            : null;
    }

    /// <summary>The assembled field the probe samples, as every run assembles it.</summary>
    /// <remarks>For a question about the field as a whole - its drive period, say - asked of the field itself.</remarks>
    public IElectrostaticField Field => _field;

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

            if (_reflections[index] is { } mirror)
            {
                yield return (new Vec3(mirror.ReflectX(v.MaxX), v.MinY, v.MinZ), new Vec3(mirror.ReflectX(v.MinX), v.MaxY, v.MaxZ));
            }
        }
        else if (element.Solve is { } plane)
        {
            var half = Math.Max(Math.Abs(plane.MinY), Math.Abs(plane.MaxY));
            var (y0, y1, z0, z1) = plane.Symmetry == SolveSymmetry.Cylindrical
                ? (-plane.MaxY, plane.MaxY, -plane.MaxY, plane.MaxY)
                : (plane.MinY, plane.MaxY, -half, half);

            var boxes = new List<(double X0, double X1)> { (plane.MinX, plane.MaxX) };

            if (_reflections[index] is { } mirror)
            {
                boxes.Add((mirror.ReflectX(plane.MaxX), mirror.ReflectX(plane.MinX)));
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
                || (_reflections[index] is { } mirror && InPlane(plane, mirror.ReflectX(q.X), q.Y, q.Z));
        }

        if (element.Solve3D is { } v)
        {
            return InVolume(v, p.X, p.Y, p.Z)
                || (_reflections[index] is { } mirror && InVolume(v, mirror.ReflectX(p.X), p.Y, p.Z));
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
        FieldInstants.CheckResolution(request.AtUs * 1e-6, probe.Field, FlagPath.Of("at-us"));
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

        // A message may run over several lines; each one stays a comment, or the second line would
        // be read as a row of the table.
        foreach (var warning in outcome.Warnings)
        {
            text.Append(Csv.Comment(warning));
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
                + $"{b}{s.Status},{Csv.Field(s.Conductor)}\n");
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
                $"{outside} of {samples.Count} point(s) lie outside every element's domain or inside a conductor's "
                + "declared shape past the edge of its solve (the row's note names it): the "
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
            // At the flag that carried the number that is not one.
            var flag = request.ToMm is null ? "at-mm" : Finite(request.FromMm) ? "to-mm" : "line-mm";
            throw Refusal(FlagPath.Of(flag), "coordinates must be finite numbers in millimetres");
        }

        CheckInstant(request.AtUs);

        if (request.ToMm is not null
            && (request.Samples < 2 || request.Samples > MaximumSamples))
        {
            throw Refusal(
                FlagPath.Of("samples"),
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
    /// <remarks>
    /// Located at <c>--at-us</c> by <see cref="FlagPath"/>, whichever verb asks: the instant is
    /// the same flag on every verb that takes it, so the same mistake is located the same way.
    /// </remarks>
    public static void CheckInstant(double? atUs)
    {
        if (atUs is { } instant && (!double.IsFinite(instant) || instant < 0.0))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = FlagPath.Of("at-us"),
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
