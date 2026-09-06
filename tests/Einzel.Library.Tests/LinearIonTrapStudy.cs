using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The linear ion trap template: the published cross-section as declared, what its two
/// generators cost in basis solves, and the field fault of the ejection slot against the
/// 0.75 mm stretch the paper applied to compensate it.
/// </summary>
/// <remarks>
/// <para>
/// The flights - resonance ejection against RF amplitude, the mass scan's peak width, the
/// dual-pressure comparison - take minutes each and live in the handoff and the literature
/// register. What is asserted here costs seconds and would silently break those comparisons
/// if it drifted: the geometry is the drawn one, the excitation is a second spatial pattern
/// and not a third, and the multipole content of the RF field is what the slot and the
/// stretch imply.
/// </para>
/// <para>
/// Source: Schwartz, Senko, Syka, <em>A two-dimensional quadrupole ion trap mass
/// spectrometer</em>, J. Am. Soc. Mass Spectrom. 2002, 13, 659. Hyperbolic rods at r0 = 4 mm;
/// a 0.25 mm slot through one x rod; "the rod with the slot and the rod opposite it (the X
/// rods) were moved out from the center 0.75 mm beyond their normal position".
/// </para>
/// </remarks>
public sealed class LinearIonTrapStudy(ITestOutputHelper output)
{
    private static ModelDocument Trap(params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("linear-ion-trap"));
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return validation.Model!;
    }

    /// <summary>Eight half-rods as polygons, each face a run of 25 points on the hyperbola.</summary>
    [Fact]
    public void TheRodsAreHyperbolicAndTheSlotIsWhereThePaperCutIt()
    {
        var model = Compile(Trap());
        var solve = model.Fields[0].Solve!;
        var r0 = model.Parameters["inscribedRadius"].SiValue;
        var stretch = model.Parameters["xStretch"].SiValue;

        Assert.Equal(11, solve.Electrodes.Count);   // eight half-rods and three housing walls
        Assert.All(solve.Electrodes.Where(e => e.Name.StartsWith("rod", StringComparison.Ordinal)), e => Assert.Equal(ElectrodeShape.Polygon, e.Shape));
        // 25 on the face, the back corner, and the slot channel and relief: 29 on the slotted
        // rod's halves, 28 on the others, where the closed slot puts two corners on one point
        // and they merge.
        foreach (var e in solve.Electrodes.Where(e => e.Name.StartsWith("rod", StringComparison.Ordinal)))
        {
            Assert.Equal(e.Name.StartsWith("rodXPlus", StringComparison.Ordinal) ? 29 : 28, e.Vertices.Count);
        }


        var xPlusUpper = solve.Electrodes.Single(e => e.Name == "rodXPlusUpper");
        var xPlusLower = solve.Electrodes.Single(e => e.Name == "rodXPlusLower");
        var yPlusRight = solve.Electrodes.Single(e => e.Name == "rodYPlusRight");

        // Every face vertex lies on its hyperbola: (x - stretch)^2 - y^2 = r0^2 for the x rods.
        foreach (var (x, y) in xPlusUpper.Vertices.Take(25))
        {
            Assert.Equal(r0 * r0, ((x - stretch) * (x - stretch)) - (y * y), 12);
        }

        foreach (var (x, y) in yPlusRight.Vertices.Take(25))
        {
            Assert.Equal(r0 * r0, (y * y) - (x * x), 12);
        }

        // The slot: the +x rod's halves start 0.125 mm either side of the axis, at the
        // stretched vertex; the y rods meet on their axis.
        Assert.Equal(0.125e-3, xPlusUpper.MinY, 12);
        Assert.Equal(-0.125e-3, xPlusLower.MaxY, 12);
        // The nearest point of the upper half to the axis is the slot corner, which sits
        // at the stretched vertex plus the slot's own half-height, in quadrature.
        var nearest = xPlusUpper.SignedDistance(0.0, 0.0);
        Assert.InRange(nearest - (r0 + stretch), 0.0, 5.0e-6);   // 1.6 um from the corner's height, 2.0 um from the hyperbola's own rise
        Assert.Equal(0.0, yPlusRight.MinX, 12);

        output.WriteLine($"r0 {r0 * 1e3:F3} mm, x pair stretched {stretch * 1e3:F2} mm, slot {2 * xPlusUpper.MinY * 1e3:F3} mm high");
        output.WriteLine($"axis to +x vertex {(r0 + stretch) * 1e3:F3} mm, axis to +y vertex {yPlusRight.Vertices.Take(25).Min(v => v.Y) * 1e3:F3} mm");
    }

    /// <summary>
    /// With the excitation off the eight electrodes are one basis solve; with it on they
    /// are two - the RF pattern and the dipole across the x rods - never three.
    /// </summary>
    /// <remarks>
    /// The two x half-rods carry the same excitation and the two y pairs none, so the
    /// excitation is one spatial pattern however many electrodes tap it. A count of three
    /// would mean the grouping had split the pattern by frequency rather than by shape.
    /// </remarks>
    [Fact]
    public void TheExcitationIsASecondPatternNotAThird()
    {
        var quiet = GeometryBuilder.SolveChannels(Compile(Trap()).Fields[0].Solve!);
        var excited = GeometryBuilder.SolveChannels(Compile(Trap(("exciteAmplitude", 13.5))).Fields[0].Solve!);

        output.WriteLine($"excitation off: {quiet.Count} basis solve(s); on: {excited.Count}");
        foreach (var c in excited)
        {
            output.WriteLine($"  {c.Report.Cycles} cycles at factor {c.Report.ConvergenceFactor:F4}, converged {c.Report.Converged}");
        }

        Assert.Single(quiet);
        Assert.Equal(2, excited.Count);
        Assert.All(excited, c => Assert.True(c.Report.Converged));
    }

    /// <summary>
    /// The slot's field fault and the stretch that compensates it, as multipoles of the
    /// RF field at half the inscribed radius.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four geometries: ideal (no slot, no stretch), slot alone, stretch alone, and the
    /// published trap (both). A slot through one rod breaks the symmetry between +x and
    /// -x, so it radiates into the odd orders - a dipole and a hexapole - as well as
    /// weakening that rod's share of the quadrupole; moving the x pair outward is
    /// symmetric and touches only the even orders. So the stretch cannot cancel the odd
    /// terms the slot creates, and the test does not ask it to. What it asks is the
    /// paper's own claim: the stretch has "analogous effects to the stretch in most
    /// commercial 3D ion traps", which is an octupole added deliberately. The numbers
    /// are printed for the register; the assertions are about signs and orderings.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSlotsFieldFaultAndTheStretchThatAnswersIt()
    {
        var cases = new (string Name, double Slot, double Stretch)[]
        {
            ("ideal", 0.0, 0.0),
            ("slot only", 0.125, 0.0),
            ("stretch only", 0.0, 0.75),
            ("published", 0.125, 0.75),
        };

        var results = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var model0 = Compile(Trap());
        var r0 = model0.Parameters["inscribedRadius"].SiValue;

        output.WriteLine("geometry        A1/A2      A3/A2      A4/A2      A6/A2      A2 [V]");
        foreach (var (name, slot, stretch) in cases)
        {
            var model = Compile(Trap(("slotXPlus", slot), ("xStretch", stretch), ("rfAmplitude", 100.0)));
            var field = FieldAssembly.Build(model);
            var terms = Multipoles(field, 0.5 * r0, 10);
            results[name] = terms;
            output.WriteLine($"{name,-14} {terms[1] / terms[2],10:E2} {terms[3] / terms[2],10:E2} {terms[4] / terms[2],10:E2} {terms[6] / terms[2],10:E2} {terms[2],10:F4}");
        }

        var ideal = results["ideal"];
        var slotOnly = results["slot only"];
        var stretchOnly = results["stretch only"];
        var published = results["published"];

        // The ideal geometry is four-fold symmetric: no odd orders, no octupole.
        Assert.True(ideal[1] / ideal[2] < 1e-4, $"ideal dipole {ideal[1] / ideal[2]:E2}");
        Assert.True(ideal[4] / ideal[2] < 1e-4, $"ideal octupole {ideal[4] / ideal[2]:E2}");

        // The slot alone creates a dipole and a hexapole the stretch leaves alone.
        Assert.True(slotOnly[1] / slotOnly[2] > 10.0 * ideal[1] / ideal[2]);
        Assert.True(stretchOnly[1] / stretchOnly[2] < 1e-4, $"stretch-only dipole {stretchOnly[1] / stretchOnly[2]:E2}");

        // The stretch alone adds an octupole, and the published geometry carries it too.
        Assert.True(stretchOnly[4] / stretchOnly[2] > 10.0 * ideal[4] / ideal[2]);
        Assert.True(published[4] / published[2] > 10.0 * ideal[4] / ideal[2]);
    }

    /// <summary>
    /// The published trap's Mathieu q per volt from the template's own numbers: 600 V
    /// peak rod-to-ground puts m/z 587 at q = 0.623, the paper's own calibration.
    /// </summary>
    [Fact]
    public void ThePapersCalibrationPointIsReproducedFromTheParameters()
    {
        var model = Compile(Trap());
        var r0 = model.Parameters["inscribedRadius"].SiValue;
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].SiValue;
        const double E = 1.602176634e-19, Mu = 1.66053906660e-27;

        var q = 4.0 * E * 600.0 / (587.0 * Mu * omega * omega * r0 * r0);
        output.WriteLine($"600 V rod-to-ground, m/z 587: q = {q:F4} (paper: 0.623)");
        Assert.InRange(q, 0.620, 0.627);

        // And the excitation frequency shipped is the secular frequency at q = 0.88,
        // beta = 0.84268 of half the drive.
        var beta = Beta(0.88);
        var secular = beta * model.Parameters["driveFrequency"].SiValue / 2.0;
        output.WriteLine($"beta(0.88) = {beta:F5}, secular {secular / 1e3:F1} kHz; template exciteFrequency {model.Parameters["exciteFrequency"].SiValue / 1e3:F1} kHz");
        Assert.Equal(secular, model.Parameters["exciteFrequency"].SiValue, secular * 1e-3);

        // The paper's isolation point: q = 0.83 "corresponded to a frequency of 368 kHz".
        var isolation = Beta(0.83) * model.Parameters["driveFrequency"].SiValue / 2.0;
        output.WriteLine($"beta(0.83) gives {isolation / 1e3:F1} kHz (paper: 368 kHz)");
        Assert.InRange(isolation / 1e3, 367.0, 369.0);
    }

    /// <summary>
    /// The quadrupole coefficient per applied volt against rod truncation and housing
    /// distance. Infinite hyperbolae give exactly one; a truncated rod with a flat
    /// back and a grounded housing gives within a tenth of a per cent of it, so the
    /// nominal Mathieu q is the real one to that precision.
    /// </summary>
    /// <remarks>
    /// The paper calibrates q from the ideal formula and finds the secular frequency
    /// at q = 0.83 to be 368 kHz - the ideal value to a tenth of a per cent - which is
    /// consistent with this. The first expectation, that wider rods bring the
    /// coefficient monotonically toward one, was wrong: measured, it drifts from
    /// 0.9994 to 0.9976 as the truncation widens from 6 to 12 mm, and the housing
    /// distance changes nothing at the fourth figure. The 12-pole is a few parts per
    /// million throughout, which is what a hyperbola buys over round rods.
    /// </remarks>
    [Fact]
    public void TheQuadrupoleCoefficientIsWithinAPerCentOfOne()
    {
        var model0 = Compile(Trap());
        var r0 = model0.Parameters["inscribedRadius"].SiValue;
        var table = new List<(double HalfWidth, double Depth, double Clearance, double A2PerVolt)>();

        output.WriteLine("halfWidth  depth  clearance   A2/V      A6/A2      A10/A2");
        foreach (var (halfWidth, depth, clearance) in new[]
        {
            (6.0, 12.0, 1.5), (8.0, 14.0, 1.5), (10.0, 16.0, 1.5), (8.0, 14.0, 6.0), (10.0, 16.0, 6.0), (12.0, 18.0, 6.0),
        })
        {
            var model = Compile(Trap(
                ("rodHalfWidth", halfWidth), ("rodDepth", depth), ("housingClearance", clearance),
                ("rfAmplitude", 100.0), ("slotXPlus", 0.0), ("xStretch", 0.0)));
            var terms = Multipoles(FieldAssembly.Build(model), 0.5 * r0, 10);
            var a2 = terms[2] / (100.0 * 0.25);   // phi = A2 (r/r0)^2 cos 2theta, sampled at r0/2
            table.Add((halfWidth, depth, clearance, a2));
            output.WriteLine($"{halfWidth,8:F1} {depth,6:F1} {clearance,9:F1}   {a2:F4}   {terms[6] / terms[2],9:E2}  {terms[10] / terms[2],9:E2}");
        }

        Assert.All(table, row => Assert.InRange(row.A2PerVolt, 0.995, 1.001));
        Assert.Equal(table[1].A2PerVolt, table[3].A2PerVolt, 3);   // the housing distance does not enter at this precision
    }

    /// <summary>
    /// The stretch sets the Mathieu q per volt: with the x pair moved out 0.75 mm the
    /// quadrupole term is 0.82 of what the ideal formula with r0 = 4 mm gives, so a
    /// nominal q overstates the real one by a sixth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found by flying the trap at a nominal q of 0.92 and watching the ion stay
    /// confined; the same document with round rods and no stretch loses it in three
    /// microseconds. The ideal formula assumes every rod's vertex is at r0, and the
    /// paper's stretched x pair is at 4.75 mm. Measured two ways that share nothing but
    /// the solved field: the quadrupole coefficient from a multipole projection at half
    /// the inscribed radius, and the field gradient on the axis at half a millimetre,
    /// which carries the octupole's contribution as well and comes out slightly lower.
    /// </para>
    /// <para>
    /// The paper quotes the isolation point as "a q of 0.83, which corresponded to a
    /// frequency of 368 kHz" - the ideal beta(0.83) to a tenth of a per cent - so its q
    /// scale is the effective one, inferred from frequency as every trap's is, and the
    /// voltage that reaches a given q in this model is the ideal formula's divided by
    /// this coefficient. The study driver uses it that way.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStretchSetsTheQPerVolt()
    {
        var ideal = Compile(Trap(("slotXPlus", 0.0), ("xStretch", 0.0), ("rfAmplitude", 100.0)));
        var published = Compile(Trap(("rfAmplitude", 100.0)));
        var r0 = ideal.Parameters["inscribedRadius"].SiValue;

        var a2Ideal = Multipoles(FieldAssembly.Build(ideal), 0.5 * r0, 4)[2];
        var a2Published = Multipoles(FieldAssembly.Build(published), 0.5 * r0, 4)[2];
        var coefficient = a2Published / a2Ideal;

        var driven = Assert.IsAssignableFrom<ITimeVaryingField>(FieldAssembly.Build(published));
        var probe = new Vec3(0.5e-3, 0.0, 0.0);
        var onAxis = Math.Abs(driven.ElectricFieldAt(in probe, 0.0).X) * r0 * r0 / (2.0 * 100.0 * 0.5e-3);
        var quarterCycle = driven.ElectricFieldAt(in probe, 0.25e-6).X;

        output.WriteLine($"A2 ideal {a2Ideal:F4} V, published {a2Published:F4} V per 100 V applied: q per volt is {coefficient:F4} of the ideal formula");
        output.WriteLine($"on-axis gradient at 0.5 mm gives {onAxis:F4}; field at a quarter cycle {quarterCycle:E2} V/m");
        output.WriteLine($"so the published q = 0.88 needs {0.88 / coefficient:F4} nominal, {0.88 / coefficient * 858.1:F1} V at m/z 524.3");

        Assert.InRange(coefficient, 0.80, 0.85);
        Assert.InRange(onAxis, 0.80, 0.85);
        Assert.True(onAxis < coefficient, "the on-axis gradient carries the octupole and must come out lower");
        Assert.True(Math.Abs(quarterCycle) < 1e-9, "a sinusoidal drive is exactly zero at a quarter cycle");
    }

    /// <summary>
    /// The Stellar's trap - the Velos Pro structure with a four-fold stretch of 0.76 mm and
    /// slots in all four rods - as a second template from the same generator: symmetric
    /// again, so the slot dipoles cancel, and a weaker quadrupole per volt than the 2002 trap.
    /// </summary>
    /// <remarks>
    /// Remes et al. 2024 give the geometry in one sentence: "a 4.0 mm field radius and a
    /// 4-fold symmetric stretch of 0.76 mm". Moving both pairs out is what an ideal formula
    /// with r0 = 4.76 mm would describe, so the coefficient is expected near (4/4.76)^2 =
    /// 0.706 - with the truncated faces and the slots moving it a little. The symmetry is
    /// the sharper check: four slots at four-fold symmetry leave no dipole and no hexapole,
    /// where the 2002 trap's single slot leaves both.
    /// </remarks>
    [Fact]
    public void TheStellarTrapIsSymmetricAndItsQPerVoltIsMeasured()
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("stellar-ion-trap"));
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
        parameters["rfAmplitude"] = parameters["rfAmplitude"] with { Value = 100.0 };
        var stellar = Compile(document with { Parameters = parameters });
        var ltq = Compile(Trap(("rfAmplitude", 100.0)));
        var ideal = Compile(Trap(("slotXPlus", 0.0), ("xStretch", 0.0), ("rfAmplitude", 100.0)));

        Assert.Equal(0.76e-3, stellar.Parameters["xStretch"].SiValue, 12);
        Assert.Equal(0.76e-3, stellar.Parameters["yStretch"].SiValue, 12);
        foreach (var name in SlotParameters)
        {
            Assert.Equal(0.125e-3, stellar.Parameters[name].SiValue, 12);
        }

        var r0 = stellar.Parameters["inscribedRadius"].SiValue;
        var s = Multipoles(FieldAssembly.Build(stellar), 0.5 * r0, 10);
        var l = Multipoles(FieldAssembly.Build(ltq), 0.5 * r0, 10);
        var i = Multipoles(FieldAssembly.Build(ideal), 0.5 * r0, 10);

        output.WriteLine("trap       A2/A2ideal   A1/A2      A3/A2      A4/A2      A6/A2");
        output.WriteLine($"ideal      {i[2] / i[2],10:F4} {i[1] / i[2],10:E2} {i[3] / i[2],10:E2} {i[4] / i[2],10:E2} {i[6] / i[2],10:E2}");
        output.WriteLine($"2002 LTQ   {l[2] / i[2],10:F4} {l[1] / l[2],10:E2} {l[3] / l[2],10:E2} {l[4] / l[2],10:E2} {l[6] / l[2],10:E2}");
        output.WriteLine($"Stellar    {s[2] / i[2],10:F4} {s[1] / s[2],10:E2} {s[3] / s[2],10:E2} {s[4] / s[2],10:E2} {s[6] / s[2],10:E2}");
        output.WriteLine($"(4.0 / 4.76)^2 = {Math.Pow(4.0 / 4.76, 2):F4}");

        // Four-fold symmetry: no odd orders and no octupole, against the 2002 trap's dipole.
        Assert.True(s[1] / s[2] < 1e-5, $"Stellar dipole {s[1] / s[2]:E2}");
        Assert.True(s[3] / s[2] < 1e-5, $"Stellar hexapole {s[3] / s[2]:E2}");
        Assert.True(s[4] / s[2] < 1e-5, $"Stellar octupole {s[4] / s[2]:E2}");
        Assert.True(l[1] / l[2] > 1e-4, "the 2002 trap's single slot leaves a dipole");

        // Weaker per volt than the 2002 trap, near what r0 = 4.76 mm would give.
        Assert.InRange(s[2] / i[2], 0.66, 0.76);
        Assert.True(s[2] < l[2]);
    }

    private static readonly string[] SlotParameters = ["slotXPlus", "slotXMinus", "slotYPlus", "slotYMinus"];

    /// <summary>Mathieu characteristic exponent on the a = 0 line, by the continued fraction.</summary>
    private static double Beta(double q)
    {
        var b = Math.Sqrt(q * q / 2.0);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            double Tail(int sign)
            {
                var v = 0.0;
                for (var n = 12; n >= 1; n--)
                {
                    var d = b + (sign * 2.0 * n);
                    v = q * q / ((d * d) - v);
                }

                return v;
            }

            var next = Math.Sqrt(Tail(+1) + Tail(-1));
            if (Math.Abs(next - b) < 1e-14)
            {
                return next;
            }

            b = 0.5 * (b + next);
        }

        return b;
    }

    private static double[] Multipoles(IElectrostaticField field, double radius, int highestOrder)
    {
        const int Samples = 2048;
        var cosine = new double[highestOrder + 1];
        var sine = new double[highestOrder + 1];

        for (var k = 0; k < Samples; k++)
        {
            var theta = 2.0 * Math.PI * k / Samples;
            var point = new Vec3(radius * Math.Cos(theta), radius * Math.Sin(theta), 0.0);
            var phi = field.PotentialAt(in point);
            for (var order = 0; order <= highestOrder; order++)
            {
                cosine[order] += phi * Math.Cos(order * theta);
                sine[order] += phi * Math.Sin(order * theta);
            }
        }

        var magnitude = new double[highestOrder + 1];
        for (var order = 0; order <= highestOrder; order++)
        {
            magnitude[order] = Math.Sqrt((cosine[order] * cosine[order]) + (sine[order] * sine[order])) * 2.0 / Samples;
        }

        return magnitude;
    }
}
