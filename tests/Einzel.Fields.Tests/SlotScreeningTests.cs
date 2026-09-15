using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A slot through a conductor screens an electrostatic field from behind it, and the decay
/// length is the slot's own height over pi.
/// </summary>
/// <remarks>
/// <para>
/// This is the physics under the linear ion trap's ejection slot, isolated from it. Laplace's
/// equation in a channel of full height h between earthed walls separates into modes
/// <c>sin(n pi y / h) exp(-n pi z / h)</c>; every one of them vanishes on the walls, so a
/// potential applied at the channel's mouth decays inward with a length of <b>h / pi</b> at
/// the slowest. A 0.25 mm slot therefore admits an external field over 0.08 mm, and a rod is
/// seven millimetres thick.
/// </para>
/// <para>
/// What that settles is a modelling question the trap template records: whether the extraction
/// field a real detector sits behind could rescue the ejection efficiency. It cannot reach the
/// slot mouth, where the aperture lens acts - at the mouth a kilovolt is worth six tenths of a
/// millivolt - so the efficiency stays a property of the slot's profile, which the 2002 paper
/// does not give.
/// </para>
/// </remarks>
public sealed class SlotScreeningTests(ITestOutputHelper output)
{
    private const double PlateVolts = -1000.0;
    private const double SlabFront = 5.0e-3;
    private const double SlabBack = 12.0e-3;

    /// <summary>A slotted slab with a plate behind it: two earthed jaws and one live plate.</summary>
    private static CompiledSolvedField Slotted(double halfHeightMetres) =>
        new()
        {
            MinX = 0.0, MaxX = 20.0e-3, MinY = -10.0e-3, MaxY = 10.0e-3,
            CellSize = 0.125e-3, Tolerance = 1e-10,
            Electrodes =
            [
                Plate("upperJaw", SlabFront, SlabBack, halfHeightMetres, 10.0e-3, 0.0),
                Plate("lowerJaw", SlabFront, SlabBack, -10.0e-3, -halfHeightMetres, 0.0),
                Plate("pullPlate", 14.0e-3, 16.0e-3, -10.0e-3, 10.0e-3, PlateVolts),
            ],
            Drives = [],
        };

    private static CompiledElectrode Plate(
        string name, double minX, double maxX, double minY, double maxY, double volts) =>
        new()
        {
            Name = name, Shape = ElectrodeShape.Rectangle,
            MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY,
            Potential = volts, Taps = [],
        };

    /// <summary>The decay length of the potential along the slot's own line, by least squares.</summary>
    private static double DecayLength(IElectrostaticField field, double fromMetres, double toMetres)
    {
        double n = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;

        for (var x = fromMetres; x <= toMetres; x += 0.05e-3)
        {
            var v = Math.Abs(field.PotentialAt(new Vec3(x, 0.0, 0.0)));

            if (v < 1e-9)
            {
                continue;   // at the solver's floor there is nothing left to fit
            }

            var y = Math.Log(v);
            n++; sx += x; sy += y; sxx += x * x; sxy += x * y;
        }

        // The potential grows toward the plate, so the slope is positive and its reciprocal
        // is the length over which the field falls by e going inward.
        return 1.0 / (((n * sxy) - (sx * sy)) / ((n * sxx) - (sx * sx)));
    }

    /// <summary>
    /// The lowest mode sets the decay, so the length is h / pi. Two heights, because one
    /// would be consistent with any constant that happened to fit.
    /// </summary>
    [Theory]
    [InlineData(1.0e-3)]    // a 2 mm channel: h/pi = 0.6366 mm
    [InlineData(2.0e-3)]    // a 4 mm channel: h/pi = 1.2732 mm
    public void TheFieldDecaysIntoASlotWithALengthOfItsHeightOverPi(double halfHeightMetres)
    {
        var (field, _) = GeometryBuilder.Build(Slotted(halfHeightMetres));

        // Fitted over the back half of the slab, clear of the mouth at 5 mm where the far
        // end's boundary condition still tells, and clear of the back face itself.
        var measured = DecayLength(field, 7.0e-3, 11.0e-3);
        var predicted = 2.0 * halfHeightMetres / Math.PI;

        output.WriteLine($"h = {2.0 * halfHeightMetres * 1e3:F2} mm: measured {measured * 1e3:F4} mm, "
                         + $"h/pi {predicted * 1e3:F4} mm, ratio {measured / predicted:F4}");

        Assert.Equal(1.0, measured / predicted, 0.05);
    }

    /// <summary>
    /// And the consequence: a kilovolt behind a slot is worth almost nothing at its mouth, so
    /// no achievable extraction voltage opposes the field an ion is leaving.
    /// </summary>
    [Fact]
    public void AKilovoltBehindASlotIsWorthAlmostNothingAtItsMouth()
    {
        var (field, _) = GeometryBuilder.Build(Slotted(1.0e-3));

        var atMouth = Math.Abs(field.PotentialAt(new Vec3(SlabFront, 0.0, 0.0)));
        var atBack = Math.Abs(field.PotentialAt(new Vec3(SlabBack, 0.0, 0.0)));

        output.WriteLine($"mouth {atMouth:E3} V, back face {atBack:E3} V, "
                         + $"mouth is {atMouth / Math.Abs(PlateVolts):E2} of the plate");

        // Seven millimetres of a 2 mm slot is eleven decay lengths, so the mouth sees about
        // 1e-5 of the plate. The back face is the control: it sees a healthy fraction, so
        // the smallness at the mouth is the channel rather than a solve that found nothing.
        Assert.True(atMouth < 1e-3 * Math.Abs(PlateVolts),
            $"the mouth sees {atMouth:E3} V of a {PlateVolts} V plate");
        Assert.True(atBack > 0.05 * Math.Abs(PlateVolts),
            $"the back face sees only {atBack:E3} V, so the plate is not reaching the slab at all");
    }
}
