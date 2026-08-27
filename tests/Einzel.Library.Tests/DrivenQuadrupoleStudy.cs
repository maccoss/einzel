using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The stability diagram on real rods rather than an ideal hyperbola.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 19 calls recovering the a-q diagram "the best single test that the
/// RF path is correct", and it has been recovered - but against
/// <c>IdealQuadrupoleRf</c>, an analytic field that is exactly quadrupolar by
/// construction. That tests the integrator and the drive. It does not test the
/// field solver, because there is no solve in it.
/// </para>
/// <para>
/// This is the same measurement on four round rods with a mesh, cut cells, and a
/// grounded housing, driven through the model format. It is the first time the
/// solved field and the time-domain integrator have had to be right together, and
/// the number it produces is a property of the geometry rather than of a formula:
/// round rods carry a 12-pole component that a hyperbola does not, and the boundary
/// moves with it.
/// </para>
/// </remarks>
public sealed class DrivenQuadrupoleStudy(ITestOutputHelper output)
{
    /// <summary>The tabulated low-mass cut-off for the ideal Mathieu equation.</summary>
    private const double IdealCutOff = 0.90804;

    /// <summary>What this engine gets for the same boundary on an analytic field.</summary>
    private const double AnalyticCutOff = 0.90684;

    private static ModelDocument Template() => Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole-rf"));

    private static ModelDocument With(ModelDocument document, params (string Name, double Value)[] overrides)
    {
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
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        return validation.Model!;
    }

    /// <summary>Volts per unit of Mathieu q, for the template's ion and geometry.</summary>
    /// <remarks>
    /// q = 4 z e V / (m omega^2 r0^2), so the amplitude that puts an ion at a given
    /// q is that q times this. Computed from the model rather than hard-coded, so
    /// changing the template cannot silently move the working point.
    /// </remarks>
    private static double VoltsPerQ(CompiledModel model)
    {
        var species = IonSpecies.FromModel(model);
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].In("Hz");
        var r0 = model.Parameters["inscribedRadius"].In("m");

        return species.MassSi * omega * omega * r0 * r0 / (4.0 * Math.Abs(species.ChargeSi));
    }

    /// <summary>
    /// Whether an ion at this working point stays in the filter.
    /// </summary>
    /// <remarks>
    /// The criterion is physical rather than geometric: the ion is unstable when it
    /// strikes a rod. On an ideal hyperbolic field there is nothing to strike and
    /// the test has to invent an aperture; here the rods are solid, so an unstable
    /// ion ends on a named surface, which is what happens in the instrument.
    /// </remarks>
    private static bool IsStable(ModelDocument document, double q, int cycles, out string? struck)
    {
        var model = Compile(document);
        var amplitude = q * VoltsPerQ(model);

        var driven = Compile(With(document, ("rfAmplitude", amplitude)));
        var field = FieldAssembly.Build(driven);

        var species = IonSpecies.FromModel(driven);
        var launch = new PhaseState(driven.SourcePosition, driven.SourceDirection * driven.LaunchSpeedSi());

        var period = 1.0 / driven.Parameters["driveFrequency"].In("Hz");

        var result = TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-9, MaximumFlightTime = cycles * period });

        struck = result.StruckSurface;

        return result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached;
    }

    [Fact]
    public void TheGeometryReducesToOneBasisSolve()
    {
        // The claim that makes RF on solved geometry cheap. The two pairs are exact
        // negatives, so the entire structure is one channel whose weight is a
        // function of time - and a q scan or a mass scan re-solves nothing at all,
        // because the basis does not depend on the amplitude.
        var model = Compile(Template());
        var field = FieldAssembly.Build(model);

        // The model has one field element, so FieldAssembly may or may not have
        // wrapped it in a superposition. Either way the driven field is in there.
        var driven = field as DrivenSolvedField
            ?? (DrivenSolvedField)((SuperposedField)field).Elements[0];

        output.WriteLine($"four rods reduced to {driven.ChannelCount} basis solve(s)");
        output.WriteLine($"drive {driven.FrequencyHz / 1e6:F3} MHz, period {driven.ShortestPeriodSeconds * 1e9:F1} ns");

        Assert.Equal(1, driven.ChannelCount);

        // And the weight really does swing: a channel that sat still would give the
        // same count for the wrong reason.
        var quarter = driven.ShortestPeriodSeconds / 4.0;

        var atZero = driven.WeightAt(0, 0.0);
        var atQuarter = driven.WeightAt(0, quarter);
        var atHalf = driven.WeightAt(0, 2.0 * quarter);

        output.WriteLine($"channel weight: {atZero:F1} V at t=0, {atQuarter:F1} V at a quarter, {atHalf:F1} V at a half");

        Assert.Equal(500.0, atZero, 6);
        Assert.Equal(0.0, atQuarter, 6);
        Assert.Equal(-500.0, atHalf, 6);
    }

    [Fact]
    public void RoundRodsPutTheLowMassCutOffCloseToTheIdealOne()
    {
        // The measurement. Bisect on q along the a = 0 line for the largest q at
        // which the ion still survives.
        const int Cycles = 150;

        var document = Template();

        var low = 0.80;
        var high = 1.00;

        Assert.True(IsStable(document, low, Cycles, out _), "the scan should start inside the stable region");
        Assert.False(IsStable(document, high, Cycles, out _), "the scan should end outside it");

        for (var k = 0; k < 12; k++)
        {
            var mid = 0.5 * (low + high);

            if (IsStable(document, mid, Cycles, out _))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var cutOff = 0.5 * (low + high);

        output.WriteLine($"solved round rods    q = {cutOff:F5}");
        output.WriteLine($"analytic hyperbola   q = {AnalyticCutOff:F5}  (this engine, ideal field)");
        output.WriteLine($"tabulated Mathieu    q = {IdealCutOff:F5}");
        output.WriteLine(string.Empty);
        output.WriteLine($"solved against tabulated: {(cutOff - IdealCutOff) / IdealCutOff:P2}");

        // Close to the ideal, because the rod ratio is the one that cancels the
        // leading non-ideal multipole - but not on it, because cancelling the
        // 12-pole leaves the 20-pole and a grounded housing.
        Assert.InRange(cutOff, 0.85, 0.96);
    }

    [Fact]
    public void AnUnstableIonEndsOnANamedRod()
    {
        // What "unstable" means once electrodes are solid. On an ideal field an
        // unstable ion simply leaves the aperture, which is a modelling convention;
        // here it hits metal, and the run says which rod.
        var struckAny = false;

        foreach (var q in new[] { 1.05, 1.2, 1.5 })
        {
            var stable = IsStable(Template(), q, cycles: 60, out var struck);

            output.WriteLine($"q = {q:F2}   stable {stable,-5}   struck {struck ?? "nothing"}");

            Assert.False(stable, $"q = {q:F2} is well past the cut-off and should not be stable");

            if (struck is not null)
            {
                struckAny = true;
                Assert.StartsWith("rod", struck, StringComparison.Ordinal);
            }
        }

        Assert.True(struckAny, "no unstable ion ended on a rod, so the rods are not stopping anything");
    }

    [Fact]
    public void TheRodRatioMovesTheBoundary()
    {
        // The reason for doing this on solved geometry at all. An ideal hyperbolic
        // field has one stability boundary and no way to move it; a real filter's
        // boundary depends on how well its rods approximate that hyperbola, and
        // that dependence is the thing a formula cannot supply.
        output.WriteLine("rod ratio    cut-off q");

        var measured = new List<(double Ratio, double CutOff)>();

        foreach (var ratio in new[] { 1.1468, 1.30 })
        {
            var document = With(Template(), ("rodRatio", ratio));

            var low = 0.70;
            var high = 1.05;

            for (var k = 0; k < 10; k++)
            {
                var mid = 0.5 * (low + high);

                if (IsStable(document, mid, cycles: 120, out _))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            var cutOff = 0.5 * (low + high);
            measured.Add((ratio, cutOff));

            output.WriteLine($"{ratio,9:F4}    {cutOff,9:F5}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine(
            $"a rod ratio {measured[1].Ratio - measured[0].Ratio:F4} away moves the cut-off by "
            + $"{Math.Abs(measured[1].CutOff - measured[0].CutOff):F5}");

        // It has to move, or the solve is not seeing the rods at all - which is
        // exactly what would happen if the field had quietly come from a formula.
        Assert.True(
            Math.Abs(measured[1].CutOff - measured[0].CutOff) > 0.005,
            $"changing the rod ratio moved the cut-off by only "
            + $"{Math.Abs(measured[1].CutOff - measured[0].CutOff):F5}");
    }
}
