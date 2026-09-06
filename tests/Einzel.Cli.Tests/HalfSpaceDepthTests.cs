using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A half-space is a ramp with no far side, and an ion carrying more than the cap turns
/// round behind the plate - self-consistently, to full precision, with nothing said.
/// </summary>
/// <remarks>
/// <para>
/// Found by an agent exploring a reflectron's focus in the acceptance run. It lowered the
/// cap below the beam energy, the model validated, the run converged, and the flight time it
/// reported was for an ion turning 5.6 mm inside the metal. Every number was arithmetically
/// correct; the region they describe is one the document does not claim.
/// </para>
/// <para>
/// The same shape as the bounded-region warning, and <c>Qualified</c> for the same reason:
/// the model is exactly what its author wrote, and what is wrong is the reading of it.
/// </para>
/// </remarks>
public sealed class HalfSpaceDepthTests(ITestOutputHelper output) : IDisposable
{
    private const string Code = "field.beyond-declared-depth";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-half-space", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Reflectron()
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Run("init", _root).ExitCode);
        }

        return Path.Combine(_root, "models", "reflectron.json");
    }

    /// <summary>Edits one parameter of the scaffolded model, asserting the edit landed.</summary>
    private static void Set(string path, string parameter, double value)
    {
        var text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        var before = document.RootElement.GetProperty("parameters")
            .GetProperty(parameter).GetProperty("value").GetDouble();

        // Through the CLI rather than by string replacement, which is how three tests here
        // once silently stopped testing anything when the corpus was reformatted.
        Assert.Equal(0, Run(
            "outline", path, "--set",
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{parameter}={value}")).ExitCode);

        Assert.NotEqual(before, value);
        Assert.Equal(value, Parameter(path, parameter));
    }

    private static double Parameter(string path, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("parameters").GetProperty(name)
            .GetProperty("value").GetDouble();
    }

    /// <summary>The half-space warnings on a run, by code.</summary>
    private static IReadOnlyList<string> Warnings(string stdout)
    {
        using var document = JsonDocument.Parse(stdout);
        return
        [
            .. document.RootElement.GetProperty("flightTime").GetProperty("warnings")
                .EnumerateArray()
                .Where(w => w.GetProperty("code").GetString() == Code)
                .Select(w => w.GetProperty("message").GetString() ?? string.Empty),
        ];
    }

    /// <summary>An ion carrying more than the cap is told where it actually turned.</summary>
    [Fact]
    public void ABeamHotterThanTheCapIsReported()
    {
        var path = Reflectron();
        Set(path, "capPotential", 3.6);

        var (exit, stdout, _) = Run("run", path, "--json");
        var warnings = Warnings(stdout);
        output.WriteLine(warnings.Count > 0 ? warnings[0] : "(none)");

        Assert.Equal(0, exit);
        var message = Assert.Single(warnings);

        // 4000 V of beam against a 3600 V cap on a 50 mm depth turns at 55.56 mm, so the
        // overshoot is 5.56 mm - arithmetic the engine had no part in.
        Assert.Contains("5.56 mm beyond the 50.00 mm", message, StringComparison.Ordinal);
        Assert.Contains("cap of 3600.0 V", message, StringComparison.Ordinal);

        // AGT-3: it says what to change, not only that something is wrong.
        Assert.Contains("Raise the cap potential", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cap above the beam is silent, which is the control. So is a cap exactly equal to
    /// it - the scaffolded model's own configuration, whose description says the ion turns
    /// exactly at the declared depth.
    /// </summary>
    /// <remarks>
    /// The equal case is the one that needs the guard: the gradient is cap over depth, a
    /// division, so multiplying it back by the depth returns the cap only to within an ulp
    /// or two. Without a floor on the overshoot this warning would fire or not fire on the
    /// shipped example depending on which way that rounding fell.
    /// </remarks>
    [Theory]
    [InlineData(4.0)]
    [InlineData(5.0)]
    public void ACapAtOrAboveTheBeamIsSilent(double capKilovolts)
    {
        var path = Reflectron();

        if (capKilovolts != Parameter(path, "capPotential"))
        {
            Set(path, "capPotential", capKilovolts);
        }

        var (exit, stdout, _) = Run("run", path, "--json");

        Assert.Equal(0, exit);
        Assert.Empty(Warnings(stdout));
    }

    /// <summary>
    /// An overshoot that is round-off in the derived gradient is not reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gradient is cap over depth, so multiplying it back by the depth returns the cap
    /// only to within an ulp or two - and which way it falls depends on the numbers. A cap
    /// of 4 kV on 50 mm round-trips exactly, which is why the scaffolded model did not expose
    /// this; 1 kV on 7 mm rounds low, and without a floor on the
    /// overshoot the warning fires on a model whose ion turns exactly where it says.
    /// </para>
    /// <para>
    /// This is not exotic. Sweeping caps from 0.1 to 20 kV and depths from 5 to 200 mm at
    /// the granularity a person actually types, thousands of pairs round low. The floor is a
    /// millionth of the depth - 7 nm here - and an ion turning 7 nm past a plate is
    /// not the finding this warning exists to report.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARoundOffOvershootIsNotReported()
    {
        var path = Reflectron();

        Set(path, "turningDepth", 7.0);
        Set(path, "capPotential", 1.0);
        Set(path, "acceleration", 1.0);

        var (exit, stdout, _) = Run("run", path, "--json");

        Assert.Equal(0, exit);
        Assert.Empty(Warnings(stdout));
    }

    /// <summary>
    /// A cloud is measured at its energy tail, not at its centre.
    /// </summary>
    /// <remarks>
    /// The tail is the population that overshoots first, and while only the tail is past the
    /// plate the effect is a selective loss of the fastest ions rather than a wrong flight
    /// time - which is the harder thing to notice and the more likely to be read as physics.
    /// A warning that waited for the nominal ion to cross would stay silent through that
    /// whole range.
    /// </remarks>
    [Fact]
    public void ACloudIsMeasuredAtItsEnergyTailRatherThanItsCentre()
    {
        var path = Reflectron();

        // Nominal 4000 V against a 4200 V cap: the centre of the distribution is inside the
        // plate by 5%. Three sigma of a 3% spread is 9%, so the tail is not.
        Set(path, "capPotential", 4.2);

        var text = File.ReadAllText(path);
        var withCloud = text.Replace(
            "\"energyFraction\": 0",
            "\"energyFraction\": 0,\n    \"cloud\": { \"ions\": 8, \"seed\": 3, \"energyFractionSpread\": 0.03 }",
            StringComparison.Ordinal);
        Assert.NotEqual(text, withCloud);
        File.WriteAllText(path, withCloud);

        var (exit, stdout, stderr) = Run("run", path, "--json");
        output.WriteLine(stderr.Trim());

        Assert.Equal(0, exit);
        var message = Assert.Single(Warnings(stdout));
        output.WriteLine(message);

        Assert.Contains("the fastest ion this run launches", message, StringComparison.Ordinal);
        Assert.Contains("three sigma", message, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = Program.Main(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
