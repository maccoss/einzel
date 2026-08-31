using System.Text.Json.Nodes;

using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Render;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A figure of a model that declares a gas draws the flight through that gas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by enumerating GRD-2's layers, and it was not a dropped warning.</b> The
/// question being asked was whether a validity warning reaches the rendered figure. The
/// answer turned out to be that the figure was not computing the same thing a run
/// computes at all: <c>SectionRenderer</c> and <c>AnimationRenderer</c> both integrated
/// through <c>TrajectoryIntegrator.Integrate</c>'s optional <c>collisions</c> parameter
/// without supplying one, so a figure of a model at a millibar drew the vacuum flight.
/// </para>
/// <para>
/// <b>The third time the gas has reached one path and not another</b> — after the
/// figure-of-merit path that made <c>einzel test</c> disagree with <c>einzel run</c>, and
/// the regime inspector's own first draft. The shape is always the same: an optional
/// parameter whose default is a <em>different physics</em> rather than an absence, so
/// forgetting it produces a plausible answer instead of a failure.
/// </para>
/// <para>
/// It matters most here. A figure is the artifact most likely to be shown detached from
/// the result that would have contradicted it — which is the argument RND-11 already
/// makes — and nothing on the page said which gas the ion had flown.
/// </para>
/// </remarks>
public sealed class GasFigureTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-gas-figure", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>The corpus example, optionally with its declared gas removed.</summary>
    /// <remarks>
    /// The two documents differ in exactly one declaration, which is what makes the
    /// comparison below a control rather than two unrelated drawings.
    /// </remarks>
    private CompiledModel Compile(string example, bool stripGas)
    {
        if (!Directory.Exists(Path.Combine(_root, "models")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
        }

        var path = Path.Combine(_root, "models", $"{example}.json");

        if (!File.Exists(path))
        {
            Assert.Equal(0, Cli("new", path, "--from-example", example).ExitCode);
        }

        var text = File.ReadAllText(path);

        if (stripGas)
        {
            var document = JsonNode.Parse(text)!;
            var transport = document["transport"]!.AsObject();

            Assert.True(
                transport.Remove("gas"),
                $"{example} declares no gas, so this comparison would be of a model "
                + "against itself — which passes against the very defect it is for");

            text = document.ToJsonString();
        }

        var validation = ModelValidator.Validate(
            ModelJson.Parse(text), null, Path.GetDirectoryName(path));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>The horizontal span of the drawn trajectory, on the page.</summary>
    /// <remarks>
    /// Page millimetres rather than model ones, which is sound here because the two
    /// figures are of the same instrument on the same page and so at the same scale.
    /// What is compared is one drawing against the other.
    /// </remarks>
    private static double DrawnSpanMm(SectionRenderer.Figure figure)
    {
        var trajectory = figure.Scene.Paths
            .Where(p => p.Points.Count >= 2)
            .OrderByDescending(p => p.Points.Count)
            .FirstOrDefault();

        Assert.NotNull(trajectory);

        return trajectory!.Points.Max(p => p.X) - trajectory.Points.Min(p => p.X);
    }

    /// <summary>A thermalising ion is drawn thermalising, not flying off.</summary>
    /// <remarks>
    /// <para>
    /// <b>The control is the test.</b> Before the fix these two figures were byte-identical
    /// — every path, every coordinate — because the gas took no part in the drawing. An
    /// assertion about the gas figure alone would have passed against a renderer that
    /// ignored the gas entirely, since a vacuum trajectory is a perfectly well-formed
    /// drawing of something.
    /// </para>
    /// <para>
    /// <c>thermalisation</c> is the corpus example that discriminates, and the size of the
    /// gap is why: the run has the ion reach <b>154.79 mm</b> as it gives its energy to the
    /// gas, and in vacuum it reaches <b>2778.28 mm</b> — eighteen times further. On the
    /// page that was 2.05 mm of drawn span against 32.41.
    /// </para>
    /// <para>
    /// The point count is the qualitative half, and it is asserted because it holds
    /// whatever the magnitudes are: a damped ion decimates to a curve, and an undamped one
    /// in a field-free drift decimates to the two points a straight line needs. A drawn
    /// straight line is the signature of the defect on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void AThermalisingIonIsDrawnThermalising()
    {
        var spec = new RenderSpec { Equipotentials = 0 };

        var inGas = SectionRenderer.Render(Compile("thermalisation", stripGas: false), spec);
        var inVacuum = SectionRenderer.Render(Compile("thermalisation", stripGas: true), spec);

        var gasSpan = DrawnSpanMm(inGas);
        var vacuumSpan = DrawnSpanMm(inVacuum);

        output.WriteLine(
            $"in gas:    {gasSpan,7:F2} mm of page over {inGas.TrajectoryPoints} points");
        output.WriteLine(
            $"in vacuum: {vacuumSpan,7:F2} mm of page over {inVacuum.TrajectoryPoints} points");

        Assert.True(
            gasSpan < 0.5 * vacuumSpan,
            $"the ion is drawn flying {gasSpan:F2} mm of page with a gas declared and "
            + $"{vacuumSpan:F2} mm without. For a flight the run puts at 155 mm against "
            + "2778 mm those are far too close — the figure is not flying the gas");

        Assert.True(
            inGas.TrajectoryPoints > inVacuum.TrajectoryPoints,
            $"the drawn path has {inGas.TrajectoryPoints} points with a gas and "
            + $"{inVacuum.TrajectoryPoints} without; a damped ion curves and an undamped "
            + "one in a field-free drift decimates to a straight line");
    }

    /// <summary>A model with no gas is drawn exactly as it was before.</summary>
    /// <remarks>
    /// The change must be invisible where there is no gas, or every figure this project
    /// has published has moved. A vacuum model gets no sampler at all rather than one that
    /// never fires, so this is bit-identical rather than close — asserted on the emitted
    /// SVG, which is the artifact that would have changed.
    /// </remarks>
    [Fact]
    public void AVacuumModelIsDrawnUnchanged()
    {
        var spec = new RenderSpec { Equipotentials = 0 };
        var model = Compile("thermalisation", stripGas: true);

        var first = SvgWriter.Write(SectionRenderer.Render(model, spec).Scene);
        var second = SvgWriter.Write(SectionRenderer.Render(model, spec).Scene);

        output.WriteLine($"{first.Length} characters, drawn twice");

        Assert.Equal(first, second, StringComparer.Ordinal);
    }
}
