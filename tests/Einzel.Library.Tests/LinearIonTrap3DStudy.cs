using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The 2002 linear ion trap as a volume: three axial sections of hyperbolic prisms, and
/// the two things the paper says about them that a cross-section cannot check - the end
/// sections make an axial well, and applying the excitation across all three sections
/// keeps it free of an axial component in the centre.
/// </summary>
/// <remarks>
/// <para>
/// Schwartz, Senko and Syka 2002, figure 2: "The ability to avoid fringe field distortions
/// for the dipole resonance excitation field is shown ... Application of appropriate
/// potentials to the end sections restricts ions to the center section of the trap, where
/// the dipole field has no axial component. For the single section trap ... the axial
/// trapping potential has to be created by voltages on the end lenses", which distorts it.
/// </para>
/// <para>
/// Solved at a millimetre cell here, so the gate stays affordable (a volume of this size at
/// half a millimetre takes minutes); the numbers the docs quote were taken at half a
/// millimetre and are within a few per cent of these. The assertions are about signs and
/// ratios, which the cell does not move.
/// </para>
/// </remarks>
public sealed class LinearIonTrap3DStudy(ITestOutputHelper output)
{
    private static CompiledModel Compile(params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("linear-ion-trap-3d"));
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
        parameters["cellSize"] = parameters["cellSize"] with { Value = 1.0 };
        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        var validation = ModelValidator.Validate(document with { Parameters = parameters });
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return validation.Model!;
    }

    /// <summary>Thirty-two electrodes: twenty-four half-rod prisms in three sections and two four-box lenses.</summary>
    [Fact]
    public void TheVolumeTrapIsThreeSectionsOfPrisms()
    {
        var model = Compile();
        var solve = model.Fields[0].Solve3D!;

        var prisms = solve.Electrodes.Where(e => e.Shape == Electrode3DShape.Prism).ToList();
        var boxes = solve.Electrodes.Where(e => e.Shape == Electrode3DShape.Box).ToList();
        Assert.Equal(24, prisms.Count);
        Assert.Equal(8, boxes.Count);

        var centre = prisms.Where(e => e.Name.StartsWith("centre", StringComparison.Ordinal)).ToList();
        var front = prisms.Where(e => e.Name.StartsWith("front", StringComparison.Ordinal)).ToList();
        Assert.Equal(8, centre.Count);
        Assert.All(centre, e => Assert.Equal(37.0e-3, e.Upper - e.Lower, 9));
        Assert.All(front, e => Assert.Equal(12.0e-3, e.Upper - e.Lower, 9));

        // The slot is cut in the centre section's +x rod only: its halves start 0.125 mm off
        // the axis where every other rod's halves meet on it.
        var slotted = centre.Single(e => e.Name == "centreXPlusUpper");
        var solid = front.Single(e => e.Name == "frontXPlusUpper");
        Assert.Equal(0.125e-3, slotted.Vertices.Min(v => v.Y), 12);
        Assert.Equal(0.0, solid.Vertices.Min(v => v.Y), 12);

        // The end sections sit above the centre by the declared offset on all four rods - a
        // common offset, which is what makes an axial well. Written quadrupolar, x up and y
        // down, it is zero on the axis and makes none; that was the first draft.
        Assert.Equal(3.0, front.Single(e => e.Name == "frontXPlusUpper").Potential, 9);
        Assert.Equal(3.0, front.Single(e => e.Name == "frontYPlusRight").Potential, 9);
        Assert.Equal(0.0, slotted.Potential, 9);

        output.WriteLine($"{prisms.Count} prisms, {boxes.Count} lens boxes; centre section {(centre[0].Upper - centre[0].Lower) * 1e3:F0} mm, ends {(front[0].Upper - front[0].Lower) * 1e3:F0} mm");
    }

    /// <summary>
    /// The end sections' DC offset makes an axial well along the axis, and the excitation
    /// field on the axis of the centre section has no axial component while a single-section
    /// trap confined by its lenses does.
    /// </summary>
    [Fact]
    public void TheEndSectionsMakeAWellAndTheExcitationStaysTransverse()
    {
        // Three sections as published. Sampled at t = 0 so the RF is at its peak; the RF
        // pattern is antisymmetric in x and y and vanishes on the axis, so what the axis
        // sees is the DC and the excitation. The excitation's own field is the difference
        // between the same trap with it on and off, so the DC well's own axial field - which
        // is supposed to exist - does not masquerade as an axial component of the excitation.
        var threeOn = FieldAssembly.Build(Compile(("exciteAmplitude", 2.0), ("endOffset", 3.0), ("lensPotential", 3.0), ("rfAmplitude", 0.0)));
        var threeOff = FieldAssembly.Build(Compile(("exciteAmplitude", 0.0), ("endOffset", 3.0), ("lensPotential", 3.0), ("rfAmplitude", 0.0)));

        // The same rods with no section offsets: the axial confinement is the lenses' alone.
        var singleOn = FieldAssembly.Build(Compile(("exciteAmplitude", 2.0), ("endOffset", 0.0), ("lensPotential", 20.0), ("rfAmplitude", 0.0)));
        var singleOff = FieldAssembly.Build(Compile(("exciteAmplitude", 0.0), ("endOffset", 0.0), ("lensPotential", 20.0), ("rfAmplitude", 0.0)));

        output.WriteLine("z [mm]   well3 [V]  excEx3 [V/m]  excEz3 [V/m] |  well1 [V]  excEx1 [V/m]  excEz1 [V/m]   (on the axis)");
        var zs = new[] { 0.0, 5.0, 10.0, 15.0, 18.0, 22.0, 26.0, 30.0 };
        var ex3 = new List<double>();
        var ez3 = new List<double>();
        var ex1 = new List<double>();
        var ez1 = new List<double>();
        var phi3 = new List<double>();
        foreach (var zMm in zs)
        {
            var point = new Vec3(0.0, 0.0, zMm * 1e-3);
            var e3 = threeOn.ElectricFieldAt(in point) - threeOff.ElectricFieldAt(in point);
            var e1 = singleOn.ElectricFieldAt(in point) - singleOff.ElectricFieldAt(in point);
            var p3 = threeOff.PotentialAt(in point);
            var p1 = singleOff.PotentialAt(in point);
            ex3.Add(e3.X); ez3.Add(e3.Z); ex1.Add(e1.X); ez1.Add(e1.Z); phi3.Add(p3);
            output.WriteLine($"{zMm,5:F0}   {p3,8:F4}   {e3.X,11:F2}   {e3.Z,11:F3} |  {p1,8:F4}   {e1.X,11:F2}   {e1.Z,11:F3}");
        }

        // A well: the axis potential rises from the centre into the end sections, by a
        // useful fraction of the 3 V applied, and is already rising where the centre ends.
        Assert.True(phi3[^1] > phi3[0] + 0.5, $"no axial well: {phi3[0]:F3} V at the centre against {phi3[^1]:F3} V at 30 mm");
        Assert.True(phi3[3] > phi3[0], "the well should already be rising at the end of the centre section");

        // The excitation field is one solved pattern and the DC another, so the two
        // configurations have the SAME excitation field; what differs is where the ions
        // are. The contrast the paper draws is that the end sections' well holds the cloud
        // in the part of the trap where the excitation is clean. Measured as the axial
        // extent a thermal ion reaches - where the well climbs to kT/e, 25.85 mV - and the
        // excitation's variation over that extent.
        const double ThermalVolts = 0.02585;
        static double Extent(IReadOnlyList<double> zs, List<double> well)
        {
            for (var i = 1; i < zs.Count; i++)
            {
                if (well[i] - well[0] >= ThermalVolts)
                {
                    var f = (ThermalVolts - (well[i - 1] - well[0])) / (well[i] - well[i - 1]);
                    return zs[i - 1] + (f * (zs[i] - zs[i - 1]));
                }
            }

            return zs[^1];
        }

        var phi1 = new List<double>();
        foreach (var zMm in zs)
        {
            var point = new Vec3(0.0, 0.0, zMm * 1e-3);
            phi1.Add(singleOff.PotentialAt(in point));
        }

        var extent3 = Extent(zs, phi3);
        var extent1 = Extent(zs, phi1);
        static double SpreadTo(IReadOnlyList<double> zs, List<double> ex, double extent)
        {
            var worst = 0.0;
            for (var i = 0; i < zs.Count && zs[i] <= extent; i++)
            {
                worst = Math.Max(worst, Math.Abs((ex[i] / ex[0]) - 1.0));
            }

            return worst;
        }

        var spread3 = SpreadTo(zs, ex3, extent3);
        var spread1 = SpreadTo(zs, ex1, extent1);
        output.WriteLine($"a thermal ion reaches z = {extent3:F1} mm with the end sections at 3 V, {extent1:F1} mm with 20 V lenses alone");
        output.WriteLine($"excitation Ex varies {spread3:P3} over the first, {spread1:P3} over the second; largest |Ez|/|Ex| over the centre 15 mm {ez3.Take(4).Max(v => Math.Abs(v)) / Math.Abs(ex3[0]):E2}");

        Assert.True(extent3 < extent1, "the end sections should confine the cloud more tightly than the lenses alone");
        Assert.True(spread3 < 1e-3, $"the excitation varies {spread3:P2} over the three-section cloud");
        Assert.True(spread3 < spread1, "the excitation should be cleaner over the tighter cloud");
        Assert.True(ez3.Take(4).Max(v => Math.Abs(v)) / Math.Abs(ex3[0]) < 0.01, "the excitation carries an axial component in the centre section");
    }
}
