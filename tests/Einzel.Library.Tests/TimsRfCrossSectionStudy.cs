using System.Globalization;

using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The confining RF of a trapped-ion-mobility tunnel is a quadrupole alternating between the
/// four segments of each ring. This solves that cross-section and measures how much of a
/// quadrupole it is.
/// </summary>
/// <remarks>
/// <para>
/// The RF is the same on every ring, so a cross-section is the right solve for it rather than
/// an approximation of one. What comes out is the number the shipped template carries as
/// <c>rfQuadrupoleFraction</c>: the quadrupole term four 90-degree segments at ±V produce,
/// relative to the hyperbolic ideal <c>V (x² − y²) / r0²</c> at the same electrode potential.
/// </para>
/// <para>
/// <b>There is a closed form to hold it against.</b> With no gaps the potential on the bore
/// is a square wave in angle, ±V, whose Fourier series inside the bore is
/// <c>(4V/π) Σ (−1)^k (r/r0)^n cos(nθ) / (n/2)</c> over n = 2, 6, 10, … — so the quadrupole
/// term is <b>4/π = 1.273</b> of the ideal at the same voltage (a square wave's fundamental
/// is larger than the square wave), and the first unwanted term is a 12-pole at one third
/// of it on the bore, falling as <c>(r/r0)^4</c> inward. A gap between segments takes a
/// little of the fundamental away, so the measured fraction has to fall short of 4/π and
/// approach it as the gap closes. That is a convergence check rather than a fit.
/// </para>
/// <para>
/// <b>And the reason the pseudopotential may enter an axisymmetric solve at all</b> is
/// measured rather than asserted: the field magnitude of a pure quadrupole depends on
/// radius alone, and the 12-pole breaks that by an amount going as <c>(r/r0)^4</c>. At the
/// radius the confined cloud actually occupies that variation is parts in ten thousand; at
/// the bore it is tens of per cent, where the density is nothing.
/// </para>
/// </remarks>
public sealed class TimsRfCrossSectionStudy(ITestOutputHelper output)
{
    private const double BoreMm = 4.0;
    private const double OuterMm = 13.0;
    private const double SegmentVolts = 1.0;

    /// <summary>Orders the four-fold antisymmetry forbids: everything but 2, 6, 10.</summary>
    private static readonly int[] Forbidden = [1, 3, 4, 5, 7, 8, 9, 11, 12];

    /// <summary>
    /// Four annular sectors round an 8 mm bore, adjacent ones at opposite potential, in a
    /// plane solve. The gap is measured along the bore.
    /// </summary>
    /// <remarks>
    /// A polygon with two vertex runs — the inner arc one way, the outer arc back — repeated
    /// four times with the segment index binding the angle. Angles are in half turns, as
    /// <c>cosPi</c> takes them, so a segment centred on a quarter turn is centred to the bit.
    /// </remarks>
    private static string Document(double gapMm, double cellMm) =>
        $$"""
        {
          "schemaVersion": "0.11",
          "name": "tims-rf-cross-section",
          "parameters": {
            "boreRadius":   { "value": {{BoreMm.ToString(CultureInfo.InvariantCulture)}}, "unit": "mm", "minimum": 1, "maximum": 20 },
            "outerRadius":  { "value": {{OuterMm.ToString(CultureInfo.InvariantCulture)}}, "unit": "mm", "minimum": 5, "maximum": 60 },
            "segmentGap":   { "value": {{gapMm.ToString(CultureInfo.InvariantCulture)}}, "unit": "mm", "minimum": 0, "maximum": 4 },
            "segmentVolts": { "value": {{SegmentVolts.ToString(CultureInfo.InvariantCulture)}}, "unit": "V", "minimum": -1000, "maximum": 1000 },
            "halfSpan":     { "expression": "0.25 - segmentGap / (2 * boreRadius) / 3.141592653589793", "unit": "1" },
            "arcPoints":    { "value": 25, "unit": "1", "minimum": 3, "maximum": 200 },
            "domainHalf":   { "value": 15, "unit": "mm", "minimum": 5, "maximum": 60 },
            "cellSize":     { "value": {{cellMm.ToString(CultureInfo.InvariantCulture)}}, "unit": "mm", "minimum": 0.02, "maximum": 2 }
          },
          "ion": { "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 1, "unit": "V" }
          },
          "detector": { "planePoint": { "value": [3, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "maximumFlightTime": { "value": 1, "unit": "us" } },
          "fields": [
            {
              "type": "solved2d",
              "solve": {
                "minX": { "expression": "-domainHalf", "unit": "mm" },
                "minY": { "expression": "-domainHalf", "unit": "mm" },
                "maxX": { "expression": "domainHalf", "unit": "mm" },
                "maxY": { "expression": "domainHalf", "unit": "mm" },
                "cellSize": { "expression": "cellSize", "unit": "mm" },
                "electrodes": [
                  {
                    "name": "segment",
                    "shape": "polygon",
                    "repeat": { "count": { "value": 4, "unit": "1" }, "index": "k" },
                    "potential": { "expression": "segmentVolts * (1 - 2 * mod(k, 2))", "unit": "V" },
                    "vertices": [
                      {
                        "count": { "expression": "arcPoints" }, "index": "i",
                        "x": { "expression": "boreRadius * cosPi(k / 2 - halfSpan + 2 * halfSpan * i / (arcPoints - 1))", "unit": "mm" },
                        "y": { "expression": "boreRadius * sinPi(k / 2 - halfSpan + 2 * halfSpan * i / (arcPoints - 1))", "unit": "mm" }
                      },
                      {
                        "count": { "expression": "arcPoints" }, "index": "i",
                        "x": { "expression": "outerRadius * cosPi(k / 2 + halfSpan - 2 * halfSpan * i / (arcPoints - 1))", "unit": "mm" },
                        "y": { "expression": "outerRadius * sinPi(k / 2 + halfSpan - 2 * halfSpan * i / (arcPoints - 1))", "unit": "mm" }
                      }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

    private static IElectrostaticField Solve(double gapMm, double cellMm)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Document(gapMm, cellMm)));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        return FieldAssembly.BuildReported(validation.Model!).Field;
    }

    /// <summary>Fourier magnitudes of the potential round a circle, by order.</summary>
    private static double[] Multipoles(IElectrostaticField field, double radiusM, int highestOrder)
    {
        const int Samples = 2048;
        var cosine = new double[highestOrder + 1];
        var sine = new double[highestOrder + 1];

        for (var k = 0; k < Samples; k++)
        {
            var theta = 2.0 * Math.PI * k / Samples;
            var phi = field.PotentialAt(new Vec3(radiusM * Math.Cos(theta), radiusM * Math.Sin(theta), 0.0));

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

    /// <summary>The quadrupole term relative to the hyperbolic ideal at the same electrode potential.</summary>
    private static double QuadrupoleFraction(IElectrostaticField field, double radiusM) =>
        Multipoles(field, radiusM, 2)[2] / (SegmentVolts * (radiusM / (BoreMm * 1e-3)) * (radiusM / (BoreMm * 1e-3)));

    /// <summary>
    /// Four segments are more of a quadrupole than hyperbolae at the same voltage — 4/π of
    /// one with no gap — and less by what the gap takes, which shrinks as the gap does.
    /// </summary>
    [Fact]
    public void FourSegmentsMakeFourOverPiOfAQuadrupoleLessTheGap()
    {
        const double Ideal = 4.0 / Math.PI;
        var fractions = new Dictionary<double, double>();

        foreach (var gap in new[] { 1.0, 0.5, 0.25 })
        {
            var field = Solve(gap, 0.1);
            var at2 = QuadrupoleFraction(field, 0.002);
            var at1 = QuadrupoleFraction(field, 0.001);
            fractions[gap] = at2;

            output.WriteLine($"gap {gap:F2} mm: quadrupole fraction {at2:F4} at r = 2 mm, {at1:F4} at r = 1 mm ({(at2 - Ideal) / Ideal * 100:+0.00} % from 4/pi)");

            // The quadrupole term goes as r^2, so the fraction is the same number at any
            // radius inside the bore - which is what makes it one number the template can carry.
            Assert.Equal(at2, at1, at2 * 0.01);

            Assert.True(at2 < Ideal, $"a gap of {gap} mm gave {at2:F4}, above the gapless 4/pi");
        }

        // Monotone in the gap, and closing on 4/pi as the gap closes: a fit could land on
        // one of these numbers, but not on the ordering.
        Assert.True(fractions[0.25] > fractions[0.5] && fractions[0.5] > fractions[1.0], "the fraction does not rise as the gap closes");
        Assert.Equal(Ideal, fractions[0.25], Ideal * 0.05);
    }

    /// <summary>
    /// The first unwanted term is a 12-pole, at one third of the quadrupole on the bore and
    /// falling as the fourth power of radius inward — the square wave's own series.
    /// </summary>
    [Fact]
    public void TheFirstUnwantedTermIsATwelvePoleFallingAsTheFourthPower()
    {
        var field = Solve(0.5, 0.1);

        foreach (var radiusMm in new[] { 1.0, 2.0, 3.0 })
        {
            var m = Multipoles(field, radiusMm * 1e-3, 12);
            var ratio = m[6] / m[2];
            var closedForm = (1.0 / 3.0) * Math.Pow(radiusMm / BoreMm, 4);

            // Every odd order and every even order that is not 2 mod 4 must vanish by the
            // four-fold antisymmetry; the largest of them is the discretisation's own noise.
            var forbidden = Forbidden.Max(n => m[n]) / m[2];

            output.WriteLine($"r = {radiusMm:F1} mm: A6/A2 = {ratio:F5} against (1/3)(r/r0)^4 = {closedForm:F5}; A10/A2 = {m[10] / m[2]:E2}; largest forbidden order {forbidden:E2} of A2");

            Assert.Equal(closedForm, ratio, closedForm * 0.25);
            Assert.True(forbidden < 1e-3, $"a forbidden multipole is {forbidden:E2} of the quadrupole");
        }
    }

    /// <summary>
    /// The field magnitude, which the pseudopotential is built from, depends on radius alone
    /// where the cloud sits — and measurably does not at the bore, which is where the
    /// axisymmetric treatment is an approximation and where there are no ions to notice.
    /// </summary>
    [Fact]
    public void TheFieldMagnitudeIsAxisymmetricWhereTheCloudIs()
    {
        var field = Solve(0.5, 0.1);

        foreach (var (radiusMm, tolerance) in new[] { (0.3, 2e-3), (1.0, 2e-2), (3.5, 1.0) })
        {
            double least = double.MaxValue, most = 0.0;

            for (var k = 0; k < 72; k++)
            {
                var theta = 2.0 * Math.PI * k / 72;
                var e = field.ElectricFieldAt(new Vec3(radiusMm * 1e-3 * Math.Cos(theta), radiusMm * 1e-3 * Math.Sin(theta), 0.0)).LengthSquared;
                least = Math.Min(least, e);
                most = Math.Max(most, e);
            }

            var variation = (most - least) / most;
            output.WriteLine($"r = {radiusMm:F1} mm: |E|^2 varies by {variation:P3} round the circle");

            Assert.True(variation < tolerance, $"|E|^2 varies by {variation:P2} at r = {radiusMm} mm");
        }
    }

    /// <summary>
    /// The shipped template carries the fraction this solve produces, so the template and
    /// the study cannot part company without a test noticing.
    /// </summary>
    [Fact]
    public void TheShippedTemplateCarriesTheSolvedFraction()
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read("tims-analyzer")));
        Assert.True(validation.IsValid);

        var shipped = validation.Model!.Parameters.Parameters["rfQuadrupoleFraction"].Value.SiValue;
        var solved = QuadrupoleFraction(Solve(0.5, 0.1), 0.002);

        output.WriteLine($"template {shipped:F4}, solved with 0.5 mm gaps {solved:F4}");

        Assert.Equal(solved, shipped, 0.01);
    }
}
