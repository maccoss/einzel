using Einzel.Core.Errors;

namespace Einzel.Core.Model;

/// <summary>
/// Refuses two conductors that occupy the same space and disagree about what they
/// hold.
/// </summary>
/// <remarks>
/// <para>
/// A Dirichlet mask is built by writing each electrode's nodes in turn, so where
/// two overlap the last one written wins. Where both hold the same potential and
/// the same drive that is harmless and often deliberate - a shape assembled from
/// overlapping primitives is a legitimate way to build a fillet or a shoulder. Where
/// they <em>disagree</em> it is ill-posed: the region is simultaneously at +V and
/// -V, the solve silently picks one, and the field it returns is the field of a
/// geometry nobody described.
/// </para>
/// <para>
/// Found by building a multipole guide. Denison's rod ratio of 1.1468 is the
/// classical value for a <em>quadrupole</em>, and applying it to six or eight rods
/// puts them through one another - the rods at 1.1468 need a centre circle 9.17 mm
/// across and a hexapole gives them 8.59 mm. That solved, converged in eight cycles,
/// and produced an acceptance measurement that was really a measurement of rods
/// closing in on the axis.
/// </para>
/// <para>
/// Only the shape pairs that can be tested exactly are tested: disc against disc,
/// rectangle against rectangle, and disc against rectangle. An edge profile lives on
/// the domain boundary and is skipped, which is a stated gap rather than an
/// oversight - a boundary profile and an interior electrode that touch are a
/// different question from two interior conductors intersecting.
/// </para>
/// </remarks>
public static class ElectrodeOverlap
{
    /// <summary>Checks a solved 2D geometry for contradictory overlaps.</summary>
    /// <param name="electrodes">The compiled electrodes, in declaration order.</param>
    /// <param name="path">JSON Pointer to the solve block, for the error object.</param>
    /// <param name="errors">Where a violation is recorded.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static void Check(
        IReadOnlyList<CompiledElectrode> electrodes, string path, List<EinzelError> errors)
    {
        ArgumentNullException.ThrowIfNull(electrodes);
        ArgumentNullException.ThrowIfNull(errors);

        for (var i = 0; i < electrodes.Count; i++)
        {
            for (var j = i + 1; j < electrodes.Count; j++)
            {
                var a = electrodes[i];
                var b = electrodes[j];

                if (Agrees(a, b) || !Intersects(a, b))
                {
                    continue;
                }

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/electrodes",
                    Constraint =
                        $"'{a.Name}' and '{b.Name}' occupy the same space and hold different "
                        + $"excitations: {Describe(a)} against {Describe(b)}",
                    Suggestion =
                        "two conductors cannot be in one place at two potentials, and a mask built "
                        + "from them keeps whichever was written last - so the solve would return "
                        + "the field of a geometry nobody described. Move them apart or make them "
                        + "agree. A common cause is a rod ratio carried over from a different pole "
                        + "count: the largest non-overlapping ratio is sin(pi/N) / (1 - sin(pi/N)), "
                        + "which is 2.414 for four rods, 1.000 for six and 0.620 for eight",
                });

                // One report per geometry rather than one per pair: a ratio that is
                // wrong makes every adjacent pair wrong, and a list of nine
                // identical complaints is harder to read than one.
                return;
            }
        }
    }

    /// <summary>Whether two electrodes hold the same thing, so overlapping is harmless.</summary>
    private static bool Agrees(CompiledElectrode a, CompiledElectrode b) =>
        a.Potential == b.Potential
        && a.DriveAmplitude == b.DriveAmplitude
        && (a.DriveAmplitude == 0.0 || a.DrivePhase == b.DrivePhase);

    private static string Describe(CompiledElectrode e) =>
        e.IsDriven
            ? $"{e.Potential:G6} V DC with {e.DriveAmplitude:G6} V of drive at phase {e.DrivePhase:G4}"
            : $"{e.Potential:G6} V";

    /// <summary>Whether two electrodes share any point.</summary>
    /// <remarks>
    /// Exact for the pairs it handles, and false for the pairs it does not. A test
    /// that guessed at an edge profile would be a test that sometimes refuses a
    /// legitimate geometry, which is worse than one that sometimes misses.
    /// </remarks>
    private static bool Intersects(CompiledElectrode a, CompiledElectrode b) =>
        (a.Shape, b.Shape) switch
        {
            (ElectrodeShape.Disc, ElectrodeShape.Disc) => DiscDisc(a, b),
            (ElectrodeShape.Rectangle, ElectrodeShape.Rectangle) => RectangleRectangle(a, b),
            (ElectrodeShape.Disc, ElectrodeShape.Rectangle) => DiscRectangle(a, b),
            (ElectrodeShape.Rectangle, ElectrodeShape.Disc) => DiscRectangle(b, a),
            _ => false,
        };

    private static bool DiscDisc(CompiledElectrode a, CompiledElectrode b)
    {
        var dx = a.CentreX - b.CentreX;
        var dy = a.CentreY - b.CentreY;
        var reach = a.Radius + b.Radius;

        // Strictly inside, so two rods exactly touching are allowed: tangency is a
        // legitimate design and a floating-point equality is a poor thing to refuse
        // on.
        return (dx * dx) + (dy * dy) < reach * reach * (1.0 - 1e-12);
    }

    private static bool RectangleRectangle(CompiledElectrode a, CompiledElectrode b) =>
        a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;

    private static bool DiscRectangle(CompiledElectrode disc, CompiledElectrode rectangle)
    {
        var x = Math.Clamp(disc.CentreX, rectangle.MinX, rectangle.MaxX);
        var y = Math.Clamp(disc.CentreY, rectangle.MinY, rectangle.MaxY);

        var dx = disc.CentreX - x;
        var dy = disc.CentreY - y;

        return (dx * dx) + (dy * dy) < disc.Radius * disc.Radius * (1.0 - 1e-12);
    }
}
