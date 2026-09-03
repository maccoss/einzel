using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A cloud flies once per ion, and the estimate has to charge for all of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>It charged for one.</b> `einzel estimate` costed a single trajectory whatever the
/// source declared, so a model with the shipped rectilinear trap's 2,000-ion cloud was
/// reported at a two-thousandth of its cost — silently, by the one command whose entire
/// job is saying what a run will cost before it is started (GRD-8).
/// </para>
/// <para>
/// <b>The same defect had already been fixed one path away.</b> The study estimate was
/// short by the evaluation count and was corrected; the model estimate was short by the ion
/// count and was not, because the fix was made while thinking about studies. When a
/// quantity is multiplied by a count in one path, every other path computing that quantity
/// needs the same question asked of it.
/// </para>
/// </remarks>
public sealed class CloudEstimateTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-cloud-estimate", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>An analytic uniform field, so the pilot integrates but nothing is solved.</summary>
    /// <remarks>
    /// <para>
    /// Analytic on purpose: the estimate's trajectory term is measured by flying a pilot, and
    /// this test is about what that term is multiplied by rather than how it is measured. A
    /// solved geometry would put a field solve between the two numbers being compared.
    /// </para>
    /// <para>
    /// <b>Uniform rather than field-free, because a field-free drift is advanced in closed
    /// form</b> - one analytic step across the whole gap, a pilot of 0 steps in 0 ms, and
    /// nothing to multiply. The first version of this test used one and compared zero against
    /// zero.
    /// </para>
    /// </remarks>
    private string Model(int ions)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, $"cloud-{ions}.json");

        var cloud = ions <= 1
            ? string.Empty
            : $$"""
                ,
                    "cloud": {
                      "ions": {{ions}},
                      "seed": 1,
                      "transverseSpread": { "value": 0.2, "unit": "mm" }
                    }
                """;

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.7",
              "name": "cloud-cost",
              "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 4000, "unit": "V" }{{cloud}}
              },
              "fields": [
                {
                  "type": "uniform",
                  "field": { "value": [-2000, 0, 0], "unit": "V/m" }
                }
              ],
              "detector": {
                "planePoint": { "value": [500, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "maximumFlightTime": { "value": 100, "unit": "us" },
                "relativeTolerance": 1e-10
              }
            }
            """);

        return path;
    }

    /// <summary>The estimate grows with the ion count, by the flight it measured.</summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted as a relationship between two numbers from the same run</b>, not against a
    /// wall-clock constant. A duration is a statement about a machine (SPEC.md's amendment on
    /// PERF-7); what has to hold on every machine is that N ions cost N flights more than one
    /// does, whatever a flight happens to cost here.
    /// </para>
    /// <para>
    /// The tolerance is loose because the pilot is re-measured for each estimate and the two
    /// runs need not agree to the millisecond. What it cannot survive is the defect: charging
    /// for one ion makes the difference zero.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACloudCostsItsIonCount()
    {
        const int Ions = 40;

        var one = EstimateCommand.Execute(Model(1));
        var many = EstimateCommand.Execute(Model(Ions));

        output.WriteLine($"one ion      {one.Seconds,8:F3} s   flight {one.TrajectorySeconds:F4} s");
        output.WriteLine($"{Ions} ions     {many.Seconds,8:F3} s   flight {many.TrajectorySeconds:F4} s");

        // The pilot has to have measured something, or there is no term to multiply and the
        // test would pass over zeroes - the vacuous truth this project has met four times.
        Assert.True(
            many.TrajectorySeconds > 0.0,
            "the pilot flight measured nothing, so this test is comparing zero against zero");

        var extra = many.Seconds - one.Seconds;
        var expected = (Ions - 1) * many.TrajectorySeconds;

        output.WriteLine($"extra        {extra,8:F3} s against {expected:F3} expected");

        Assert.True(
            extra > 0.5 * expected,
            $"{Ions} ions cost {extra:F3} s more than one, against {expected:F3} s of flight "
            + "they add. Charging a single trajectory whatever the source declares makes this "
            + "difference zero");

        Assert.True(
            extra < 2.0 * expected,
            $"{Ions} ions cost {extra:F3} s more than one, against {expected:F3} s expected - "
            + "so something other than the flight is being multiplied");
    }

    /// <summary>And it says so, because a reader plans against the number.</summary>
    /// <remarks>
    /// GRD-12: the basis states what was multiplied. A cost that grows by a factor of forty
    /// with no explanation reads as a defect in the estimate rather than as the run being
    /// forty flights.
    /// </remarks>
    [Fact]
    public void TheBasisSaysTheCloudWasCharged()
    {
        var many = EstimateCommand.Execute(Model(40));

        output.WriteLine(many.Basis);

        Assert.Contains("cloud of 40 ions", many.Basis, StringComparison.Ordinal);
        Assert.Contains("the flight 40 times", many.Basis, StringComparison.Ordinal);
    }

    /// <summary>A source with no cloud is charged for one flight, as it always was.</summary>
    /// <remarks>
    /// The control. Without it, "the estimate scales with ions" would pass for a command
    /// that multiplied by something else entirely, and a single-ion model is the case every
    /// other estimate test in this suite depends on.
    /// </remarks>
    [Fact]
    public void ASingleIonIsStillChargedOnce()
    {
        var one = EstimateCommand.Execute(Model(1));

        output.WriteLine($"one ion {one.Seconds:F3} s, flight {one.TrajectorySeconds:F4} s");

        Assert.DoesNotContain("cloud of", one.Basis, StringComparison.Ordinal);

        // The whole estimate is that one flight plus whatever the fields cost, and this
        // model has no solved field at all - so the two should be close together.
        Assert.True(
            one.Seconds < 3.0 * Math.Max(one.TrajectorySeconds, 1e-6),
            $"an analytic single-ion model estimated {one.Seconds:F3} s against a "
            + $"{one.TrajectorySeconds:F4} s flight, so something is being charged that "
            + "neither the field nor the trajectory accounts for");
    }
}
