using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The PNNL 100-electrode ion funnel, as shipped: the published layout, and what the
/// published cutoff theory predicts from the template's own numbers.
/// </summary>
/// <remarks>
/// <para>
/// The flights that compare this template with the measurements take minutes each and live
/// in the handoff (sections 76 and 77) and the literature register (section 5); what is
/// asserted here is the part that costs seconds and would silently break the comparison if it
/// drifted: the geometry is the drawn one, a hundred rings and two DC plates reduce to two
/// basis solves, and the closed form the paper fits its data with, evaluated from the
/// template's parameters, lands where the paper puts it.
/// </para>
/// </remarks>
public sealed class PnnlIonFunnelTests(ITestOutputHelper output)
{
    private static CompiledModel Compile()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("pnnl-ion-funnel"));
        var (model, errors) = ModelValidator.Validate(document);

        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return model!;
    }

    /// <summary>A hundred rings, a conductance limit and an extraction plate are two basis solves.</summary>
    /// <remarks>
    /// Adjacent rings carry opposite RF phases and every ring's DC is a point on one resistor
    /// chain, so the whole stack is one RF spatial pattern and one DC pattern. The two DC-only
    /// plates behind it join the DC pattern. An RF amplitude scan or a frequency scan re-solves
    /// nothing, which is what makes the transmission-against-amplitude comparison affordable.
    /// </remarks>
    [Fact]
    public void TheWholeFunnelIsTwoBasisSolves()
    {
        var model = Compile();
        var solve = model.Fields[0].Solve!;

        var channels = GeometryBuilder.SolveChannels(solve);

        output.WriteLine($"electrodes   {solve.Electrodes.Count}");
        output.WriteLine($"basis solves {channels.Count}");
        foreach (var c in channels)
        {
            output.WriteLine($"  {c.Report.Cycles} cycles at factor {c.Report.ConvergenceFactor:F4}, converged {c.Report.Converged}");
        }

        Assert.Equal(102, solve.Electrodes.Count);    // 100 rings, conductance limit, extraction plate
        Assert.Equal(2, channels.Count);
        Assert.All(channels, c => Assert.True(c.Report.Converged));
    }

    /// <summary>The taper is the drawn one: 25.4 mm through ring 57, 2.5 mm at ring 99.</summary>
    [Fact]
    public void TheRingsTaperAsPublished()
    {
        var model = Compile();
        var solve = model.Fields[0].Solve!;

        var rings = solve.Electrodes.Where(e => e.Name.StartsWith("ring", StringComparison.Ordinal)).ToList();
        Assert.Equal(100, rings.Count);

        static double InnerRadiusMm(CompiledElectrode e)
        {
            Assert.Equal(ElectrodeShape.Rectangle, e.Shape);   // a ring is a rectangle in the half-plane
            return e.MinY * 1e3;
        }

        var first = rings.Single(e => e.Name == "ring-0");
        var lastConstant = rings.Single(e => e.Name == "ring-57");
        var firstTapered = rings.Single(e => e.Name == "ring-58");
        var last = rings.Single(e => e.Name == "ring-99");

        output.WriteLine($"ring 0 {InnerRadiusMm(first):F3} mm, ring 57 {InnerRadiusMm(lastConstant):F3}, ring 58 {InnerRadiusMm(firstTapered):F3}, ring 99 {InnerRadiusMm(last):F3}");

        Assert.Equal(12.7, InnerRadiusMm(first), 9);
        Assert.Equal(12.7, InnerRadiusMm(lastConstant), 9);
        Assert.Equal(12.7 - (12.7 - 1.25) / 42.0, InnerRadiusMm(firstTapered), 9);
        Assert.Equal(1.25, InnerRadiusMm(last), 9);

        // The pitch is a millimetre: ring k starts at k mm.
        var ring40 = rings.Single(e => e.Name == "ring-40");
        Assert.Equal(40.0, ring40.MinX * 1e3, 9);
    }

    /// <summary>
    /// The published cutoff formula, from the template's own parameters, puts m/z 118 at
    /// 19.1 V/cm near 500 kHz - the paper's own prediction - and scales as the square root
    /// of the DC gradient.
    /// </summary>
    /// <remarks>
    /// Page et al. 2006, eq. 7: (m/z)_low = 8 e E_n / (m_u w^2 delta), with delta the ring pitch
    /// over pi and E_n = E_DC sin A the DC field's component normal to the tapered wall. The
    /// paper takes tan A = 0.25; the template's linear taper from 25.4 to 2.5 mm over 42 rings
    /// gives 0.273, which is the 4 per cent between the paper's 511 kHz and the value here.
    /// This is arithmetic on published numbers, not a flight - the flights are in the handoff,
    /// and they put the cutoff about 20 per cent below both the formula and the measurement.
    /// </remarks>
    [Fact]
    public void ThePublishedCutoffFormulaFromTheTemplatesOwnParameters()
    {
        var model = Compile();
        var pitch = model.Parameters["pitch"].SiValue;
        var taperPerRing = model.Parameters["taperPerRing"].SiValue;
        var delta = pitch / Math.PI;
        var sinA = Math.Sin(Math.Atan(taperPerRing / pitch));

        static double CutoffHz(double eDc, double sinA, double delta, double mz)
        {
            const double e = 1.602176634e-19, mu = 1.66053906660e-27;
            var omega2 = 8.0 * e * eDc * sinA / (mu * mz * delta);
            return Math.Sqrt(omega2) / (2.0 * Math.PI);
        }

        var f9 = CutoffHz(900.0, sinA, delta, 118.2);
        var f19 = CutoffHz(1910.0, sinA, delta, 118.2);
        var f29 = CutoffHz(2910.0, sinA, delta, 118.2);

        output.WriteLine($"delta {delta * 1e3:F4} mm, sin A {sinA:F4} (paper 0.2425)");
        output.WriteLine($"eq. 7 cutoff for m/z 118.2: {f9 / 1e3:F0} / {f19 / 1e3:F0} / {f29 / 1e3:F0} kHz at 9.0 / 19.1 / 29.1 V/cm");
        output.WriteLine("measured (Page 2006, Fig. 3, 50% points): 425 / 485 / 565 kHz");

        Assert.InRange(f19 / 1e3, 480.0, 560.0);
        Assert.Equal(Math.Sqrt(2910.0 / 900.0), f29 / f9, 6);
    }
}
