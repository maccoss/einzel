using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Sweeps;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The optimiser against a published design constant: the round-rod ratio that
/// best approximates a hyperbolic quadrupole field.
/// </summary>
/// <remarks>
/// <para>
/// A quadrupole made from round rods is not a hyperbolic field. With the
/// four-fold symmetry and the x-y antisymmetry the rods impose, the potential
/// expands in multipoles of order 2, 6, 10, 14 - the wanted quadrupole, then the
/// 12-pole, then the 20-pole - and the classical design question is what rod
/// radius makes the 12-pole vanish. The published answer is r/r0 = 1.1468
/// (Denison 1971), with 1.1487 also in circulation from a different criterion.
/// </para>
/// <para>
/// It is a good target for two independent reasons. It is a real number from the
/// literature rather than a self-consistency check, which spec section 19 leans on
/// heavily now that the cross-code tier is unavailable. And the measurement is
/// only possible at all because the rod surfaces are cut cells: a rasterised
/// circle is a staircase, and a staircase radiates harmonics of its own into
/// exactly the multipoles being measured. The quantity here is four parts in ten
/// thousand of the main term, which a rasterised boundary would bury.
/// </para>
/// </remarks>
public sealed class QuadrupoleRodRatioStudy(ITestOutputHelper output)
{
    /// <summary>
    /// The multipole content of the field on a circle, by discrete cosine
    /// transform.
    /// </summary>
    /// <remarks>
    /// Sampled well inside the inscribed radius. The expansion converges
    /// everywhere inside the rods, but each multipole grows as (r/r0) to its own
    /// order, so sampling too close to the rods weights the high orders up and too
    /// close to the axis buries them in interpolation error. Six tenths is a
    /// compromise, and the answer is checked against a second radius to make sure
    /// it is not an artefact of the choice.
    /// </remarks>
    private static (double A2, double A6, double A10) Multipoles(IElectrostaticField field, double radius)
    {
        const int Samples = 512;
        double a2 = 0.0, a6 = 0.0, a10 = 0.0;

        for (var k = 0; k < Samples; k++)
        {
            var theta = 2.0 * Math.PI * k / Samples;
            var point = new Vec3(radius * Math.Cos(theta), radius * Math.Sin(theta), 0.0);
            var phi = field.PotentialAt(in point);

            a2 += phi * Math.Cos(2.0 * theta);
            a6 += phi * Math.Cos(6.0 * theta);
            a10 += phi * Math.Cos(10.0 * theta);
        }

        var scale = 2.0 / Samples;
        return (a2 * scale, a6 * scale, a10 * scale);
    }

    private static double TwelvePoleFraction(CompiledModel model, double fraction)
    {
        var field = FieldAssembly.Build(model);
        var r0 = model.Parameters["inscribedRadius"].In("m");
        var (a2, a6, _) = Multipoles(field, fraction * r0);

        return a6 / a2;
    }

    [Fact]
    public void TheOptimiserRecoversTheClassicalRodRatio()
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole"));

        // Minimising the magnitude puts a kink at the optimum rather than a smooth
        // basin, which is the shape Nelder-Mead handles and a gradient method does
        // not. Squaring would smooth it and flatten it at the same time, which is
        // the wrong trade when the objective already carries solver noise.
        var result = Optimiser.Run(
            document,
            [new DesignVariable("rodRatio")],
            model => Math.Abs(TwelvePoleFraction(model, 0.6)),
            ObjectiveSense.Minimise,
            OptimisationAlgorithm.NelderMead,
            new OptimisationSettings
            {
                MaximumEvaluations = 60,
                ParameterTolerance = 2e-3,

                // Set to the objective's own noise floor, not below it. Each
                // evaluation ends in a multigrid solve at a finite tolerance and
                // a bicubic interpolation around a circle, and what comes out has
                // grit on it at a few parts in ten million of the quadrupole term.
                // A tolerance under that asks the search to resolve noise, and it
                // will spend its whole budget doing so and then report that it
                // never converged.
                ObjectiveTolerance = 1e-7,
                Restarts = 0,
            });

        var (ratio, uncertainty, evidence, warnings) = result.Best["rodRatio"];
        var (residual, _, _, _) = result.Objective;

        output.WriteLine($"rodRatio = {result.Best["rodRatio"].Format("1")}");
        output.WriteLine($"12-pole fraction there: {residual.In("1"):E3}");
        output.WriteLine(
            $"{result.Evaluations} evaluations, {result.Iterations} iterations, converged {result.Converged}");
        output.WriteLine($"published: 1.1468 (Denison 1971); 1.1487 also reported");

        foreach (var warning in warnings)
        {
            output.WriteLine($"  {warning}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("search history:");

        foreach (var step in result.History)
        {
            output.WriteLine($"  eval {step.Evaluation,3}: rodRatio {step.Parameters["rodRatio"],8:F5} -> {step.Objective:E3}");
        }

        var found = ratio.In("1");

        // Within one per cent of the published value, at this mesh. The remaining
        // difference is not search error and it is mostly not the housing either,
        // which was the first guess: refining from 16 to 32 to 64 cells across the
        // inscribed radius moves the answer 1.14148, 1.14426, 1.14487, which is
        // second-order convergence toward about 1.1451. The grounded box is worth
        // roughly 0.002 on top of that, and 1.1451 plus 0.002 is the published
        // 1.1468. See TheOptimumSharpensUnderRefinement below.
        Assert.InRange(found, 1.135, 1.160);

        // And the objective really is near zero there, not merely the best of a
        // bad set. The 12-pole at nominal is already small; cancelling it takes it
        // to a few parts in a hundred thousand of the quadrupole term.
        Assert.True(
            Math.Abs(residual.In("1")) < 3e-5,
            $"the 12-pole fraction at the optimum is {residual.In("1"):E3}, which is not a cancellation");

        Assert.True(result.Failures == 0, $"{result.Failures} evaluations failed");

        // The optimum is interior, so no bound warning: the answer is a property of
        // the field rather than of the search interval.
        Assert.DoesNotContain(result.Warnings, w => w.Code == "optimiser.optimum-at-bound");

        var (_, converged, spread) = evidence switch
        {
            Core.Results.Evidence.Search s => (s.Evaluations, s.Converged, s.SpreadSi),
            _ => throw new InvalidOperationException("an optimiser result should carry search evidence"),
        };

        Assert.True(converged, "the search hit its budget rather than its tolerance");
        Assert.True(spread > 0.0, "a converged search still has a final simplex with some width");
        Assert.True(uncertainty.WidthSi > 0.0, "the envelope should carry the width of that simplex");
    }

    [Fact]
    public void TheOptimumSharpensUnderRefinement()
    {
        // A disagreement with a published number is only interesting once it is
        // known which way it moves under refinement. Here it moves toward the
        // published value and slows down doing it, which says the difference is
        // discretisation rather than a modelling error - and that is a different
        // conclusion from "we are half a per cent out".
        //
        // Two mesh densities here rather than three. The 64-cell case is a 513 by
        // 513 solve per evaluation and takes minutes; it was run once and is
        // recorded in docs/optimisation.md rather than shipped in the suite.
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole"));
        const double Published = 1.1468;

        output.WriteLine("cells per r0   optimum rodRatio   distance from 1.1468");

        var optima = new List<double>();

        foreach (var cells in new[] { 16.0, 32.0 })
        {
            var refined = document with
            {
                Parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal)
                {
                    ["cellsPerRadius"] = document.Parameters!["cellsPerRadius"] with { Value = cells },
                },
            };

            var result = Optimiser.Run(
                refined,
                [new DesignVariable("rodRatio")],
                model => Math.Abs(TwelvePoleFraction(model, 0.6)),
                ObjectiveSense.Minimise,
                OptimisationAlgorithm.NelderMead,
                new OptimisationSettings
                {
                    MaximumEvaluations = 45,
                    ParameterTolerance = 2e-3,
                    ObjectiveTolerance = 1e-7,
                    Restarts = 0,
                });

            var (ratio, _, _, _) = result.Best["rodRatio"];
            optima.Add(ratio.In("1"));

            output.WriteLine($"{cells,12:F0}   {ratio.In("1"),16:F5}   {Math.Abs(ratio.In("1") - Published):F5}");
        }

        output.WriteLine("64 cells gives 1.14487, measured once out of suite; the three extrapolate to 1.1451");

        Assert.True(
            Math.Abs(optima[1] - Published) < Math.Abs(optima[0] - Published),
            $"refining moved the optimum from {optima[0]:F5} to {optima[1]:F5}, which is away from the "
            + $"published {Published}, so the difference is not discretisation");
    }

    [Fact]
    public void TheCancellationIsNotAnArtefactOfTheSamplingRadius()
    {
        // A multipole fraction is a property of the field, not of the circle it is
        // measured on: A6/A2 at radius r scales as (r/r0) to the fourth, so the
        // radius where it crosses zero must not move. If it did, what is being
        // measured would be an interpolation artefact rather than a multipole.
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole"));

        output.WriteLine("sampling radius   optimum rodRatio   12-pole there");

        var optima = new List<double>();

        foreach (var fraction in new[] { 0.45, 0.6, 0.75 })
        {
            var result = Optimiser.Run(
                document,
                [new DesignVariable("rodRatio")],
                model => Math.Abs(TwelvePoleFraction(model, fraction)),
                ObjectiveSense.Minimise,
                OptimisationAlgorithm.NelderMead,
                new OptimisationSettings { MaximumEvaluations = 30, ParameterTolerance = 3e-3, Restarts = 0 });

            var (ratio, _, _, _) = result.Best["rodRatio"];
            var (residual, _, _, _) = result.Objective;

            optima.Add(ratio.In("1"));
            output.WriteLine($"{fraction,15:F2}   {ratio.In("1"),16:F5}   {residual.In("1"):E3}");
        }

        var spread = optima.Max() - optima.Min();
        output.WriteLine($"spread across sampling radii: {spread:F5}");

        Assert.True(
            spread < 0.01,
            $"the optimum moved by {spread:F5} with the sampling radius, so it is not a property of the field");
    }
}
