using System.Diagnostics;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>Scratch: in a pushed packet run, how does a stage split between field and pair sum?</summary>
public sealed class BenchSplitScratch(ITestOutputHelper output)
{
    private static CompiledModel Compile(string template, params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read(template));
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        var validation = ModelValidator.Validate(document with { Parameters = parameters });
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return validation.Model!;
    }

    [Theory]
    [InlineData("linear-ion-trap")]
    [InlineData("linear-ion-trap-3d")]
    public void FieldAgainstPairSum(string template)
    {
        var species = IonSpecies.FromMassToCharge(524.26, 1);
        var model = template.EndsWith("3d", StringComparison.Ordinal)
            ? Compile(template, ("cellSize", 1.0))
            : Compile(template);

        var sw = Stopwatch.StartNew();
        var field = FieldAssembly.Build(model);
        sw.Stop();
        var solve = sw.Elapsed.TotalSeconds;

        foreach (var n in new[] { 240, 1000 })
        {
            var random = new Random(11);
            var positions = new Vec3[n];
            var active = new bool[n];
            var acc = new Vec3[n];
            for (var i = 0; i < n; i++)
            {
                positions[i] = new Vec3(
                    (random.NextDouble() - 0.5) * 0.2e-3,
                    (random.NextDouble() - 0.5) * 0.2e-3,
                    (random.NextDouble() - 0.5) * 2e-3);
                active[i] = true;
            }

            var interaction = new CoulombInteraction(
                1e5, n, species.ChargeSi, species.MassSi,
                CoulombInteraction.SpacingSoftening(0.05e-3, 0.05e-3, 1e-3, n));

            // Warm both paths.
            for (var w = 0; w < 2; w++)
            {
                foreach (var p in positions)
                {
                    _ = field.ElectricFieldAt(in p);
                }

                Array.Clear(acc);
                interaction.Accumulate(positions, active, acc);
            }

            const int Reps = 20;

            var swField = Stopwatch.StartNew();
            for (var r = 0; r < Reps; r++)
            {
                for (var i = 0; i < n; i++)
                {
                    var p = positions[i];
                    _ = field.ElectricFieldAt(in p);
                }
            }

            swField.Stop();

            var swPair = Stopwatch.StartNew();
            for (var r = 0; r < Reps; r++)
            {
                Array.Clear(acc);
                interaction.Accumulate(positions, active, acc);
            }

            swPair.Stop();

            var fieldMs = swField.Elapsed.TotalMilliseconds / Reps;
            var pairMs = swPair.Elapsed.TotalMilliseconds / Reps;
            output.WriteLine(
                $"{template,-22} N={n,5}  solve {solve,6:F2} s | one stage: field {fieldMs,8:F3} ms, pair sum {pairMs,8:F3} ms "
                + $"-> pair sum is {pairMs / (fieldMs + pairMs) * 100,5:F1}% of a stage");
        }
    }
}
