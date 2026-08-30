using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The data an interactive viewport draws (§16), and the rule about when it draws none.
/// </summary>
/// <remarks>
/// The third thing the window has needed that no command returned, after the model tree
/// needed <c>outline</c> — and for the same reason. UI-1 puts physics outside the shell,
/// so a viewport can no more fly its own ions than a model tree can parse its own
/// document.
/// </remarks>
public sealed class ViewportTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-viewport", Guid.NewGuid().ToString("N"));

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

    private string Example(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);

        return path;
    }

    /// <summary>A trajectory model yields paths with positions, energies and fates.</summary>
    /// <remarks>
    /// Energy is per point rather than per path because §16 asks for bundles coloured by
    /// energy, and an ion that has crossed a mirror has had several — one number per path
    /// would be a colour for a quantity that varied along it.
    /// </remarks>
    [Fact]
    public void ATrajectoryModelYieldsPathsToDraw()
    {
        var outcome = ViewportCommand.Execute(Example("single-stage-reflectron"));

        Assert.True(outcome.ProducesTrajectories);

        var path = Assert.Single(outcome.Trajectories);

        output.WriteLine($"{path.PointsMm.Count} points, fate '{path.Fate}'");
        output.WriteLine(
            $"energy {path.EnergyEv.Min():F1} to {path.EnergyEv.Max():F1} eV");

        Assert.Equal(path.PointsMm.Count, path.EnergyEv.Count);
        Assert.All(path.PointsMm, p => Assert.Equal(3, p.Count));

        // The turn-around is resolved, which is the thing a picture of a reflectron is
        // for: the ion decelerates to rest in the mirror and comes back. Asserted rather
        // than a point count, because the analytic field-free drift crosses the whole
        // drift in one step whatever cadence is asked for - a straight line needs two
        // points, and the samples land where the physics is.
        var x = path.PointsMm.Select(p => p[0]).ToList();

        output.WriteLine($"x from {x.Min():F1} to {x.Max():F1} mm, ends at {x[^1]:F1}");

        Assert.True(path.EnergyEv.Min() < 1.0, $"lowest energy {path.EnergyEv.Min():F1} eV");
        Assert.True(path.EnergyEv.Max() > 3900.0);

        // Non-monotone: it goes out and comes back, which one step could not show.
        Assert.True(
            x.Max() > x[0] + 1.0 && x[^1] < x.Max() - 1.0,
            "the path does not turn round");

        Assert.Equal("arrived", path.Fate);
    }

    /// <summary>
    /// A diffusive model yields no paths, and says why (RND-8, TRN-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule that matters most in a viewport.</b> Above about 1e-2 mbar the model
    /// computes a density and no trajectories exist, so lines through a funnel would
    /// depict something the model never computed — which is worse than drawing nothing,
    /// because a picture is the artifact most likely to be shown with none of the
    /// uncertainty apparatus attached.
    /// </para>
    /// <para>
    /// Asked of the transport mode rather than of the pressure. A viewport that decided
    /// from the pressure would be re-deriving a decision the mode already owns, and the
    /// two would part company at the first model that declared them inconsistently.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusiveModelYieldsNoPathsAndSaysWhy()
    {
        var outcome = ViewportCommand.Execute(Example("drift-tube-diffusion"));

        output.WriteLine($"produces trajectories: {outcome.ProducesTrajectories}");

        foreach (var warning in outcome.Warnings)
        {
            output.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        Assert.False(outcome.ProducesTrajectories);
        Assert.Empty(outcome.Trajectories);

        // Not an empty result to be filled in later: a statement that there is nothing of
        // this kind to draw, and what the model has instead.
        var said = Assert.Single(outcome.Warnings, w => w.Code == "render.no-trajectories");

        Assert.Contains("density", said.Message, StringComparison.Ordinal);
    }

    /// <summary>A cloud yields one path per ion, each with its own fate.</summary>
    /// <remarks>
    /// §16 asks for bundles coloured by fate, and ACC-5's argument applies to a picture as
    /// much as to a number: "struck rodYPlus" is a thing to move, "lost" is not.
    /// </remarks>
    [Fact]
    public void ACloudYieldsOnePathPerIonEachWithItsFate()
    {
        var outcome = ViewportCommand.Execute(Example("turn-around-time"));

        output.WriteLine($"{outcome.Trajectories.Count} paths");

        foreach (var fate in outcome.Trajectories.Select(t => t.Fate).Distinct(StringComparer.Ordinal))
        {
            output.WriteLine(
                $"  {outcome.Trajectories.Count(t => t.Fate == fate),4} x {fate}");
        }

        Assert.True(
            outcome.Trajectories.Count > 1,
            "a model declaring an ion cloud should give a bundle, not a single path");

        Assert.All(outcome.Trajectories, t => Assert.False(string.IsNullOrWhiteSpace(t.Fate)));
    }

    /// <summary>The colour scale spans the bundle, not each path (§16).</summary>
    /// <remarks>
    /// <para>
    /// <b>The hazard this exists to prevent.</b> §16 asks for bundles coloured by energy.
    /// A scale taken per path gives every ion the same colours whatever its energy, so
    /// two ions a kilovolt apart look identical and the picture says they are the same.
    /// The scale has to be anchored over everything drawn — the same failure the
    /// animation's contour levels had in the other axis, where anchoring per frame made a
    /// film of a spreading packet show a packet doing nothing.
    /// </para>
    /// <para>
    /// Checked by requiring the reported range to contain what <em>every</em> path holds,
    /// and to be wider than the widest single one — which the reflectron gives, since its
    /// ions turn round at different depths.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheColourScaleSpansTheBundleNotEachPath()
    {
        var outcome = ViewportCommand.Execute(Example("turn-around-time"));

        var low = Assert.IsType<double>(outcome.LowestEnergyEv);
        var high = Assert.IsType<double>(outcome.HighestEnergyEv);

        output.WriteLine($"bundle spans {low:G6} to {high:G6} eV over "
            + $"{outcome.Trajectories.Count} paths");

        foreach (var path in outcome.Trajectories)
        {
            Assert.True(path.EnergyEv.Min() >= low, "a path goes below the reported low");
            Assert.True(path.EnergyEv.Max() <= high, "a path goes above the reported high");
        }

        // The discriminating half, and it is a stronger statement than "wider than the
        // widest path": no single path owns both ends of the scale. Any per-path
        // anchoring reports some one path's own extremes, so it fails this whatever the
        // magnitudes happen to be - which a comparison of spreads does not, when the
        // paths nearly coincide.
        var owns = outcome.Trajectories.Count(
            p => p.EnergyEv.Min() <= low && p.EnergyEv.Max() >= high);

        output.WriteLine($"{owns} of {outcome.Trajectories.Count} paths span the whole scale");

        Assert.Equal(0, owns);
    }

    /// <summary>A model with no bundle reports no range rather than zero (§16).</summary>
    /// <remarks>
    /// Absent, not zero — the policy the rest of this surface reached the hard way, after
    /// an undefined Twiss orientation reached the serialiser as NaN. Zero is a real
    /// energy and a reader cannot tell the two apart if both print as zero; a colour
    /// scale from zero to zero divides.
    /// </remarks>
    [Fact]
    public void AModelWithNoBundleReportsNoRange()
    {
        var outcome = ViewportCommand.Execute(Example("drift-tube-diffusion"));

        Assert.Empty(outcome.Trajectories);
        Assert.Null(outcome.LowestEnergyEv);
        Assert.Null(outcome.HighestEnergyEv);
    }

    /// <summary>The field's warnings ride out with the picture (GRD-2).</summary>
    /// <remarks>
    /// A viewport is a number a person reads with their eyes. A bundle drawn through a
    /// field that never converged looks exactly like one drawn through a field that did,
    /// so the evidence has to travel with it — this is the seam this project has dropped
    /// evidence at six times.
    /// </remarks>
    [Fact]
    public void TheFieldsWarningsRideOutWithThePicture()
    {
        // A solved model, so there is a solve report with something to say.
        var outcome = ViewportCommand.Execute(Example("einzel-lens"));

        output.WriteLine($"{outcome.Warnings.Count} warnings carried");

        foreach (var warning in outcome.Warnings)
        {
            output.WriteLine($"  [{warning.Severity}] {warning.Code}");
        }

        // Whatever the solve had to say is here rather than discarded at the seam. The
        // assertion is that the channel exists and is used, not that this model happens
        // to be strained: a clean solve legitimately says nothing.
        Assert.True(outcome.ProducesTrajectories);
        Assert.NotEmpty(outcome.Trajectories);
    }
}
