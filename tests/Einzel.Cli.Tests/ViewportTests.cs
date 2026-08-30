using Einzel.Commands;
using Einzel.Core.Errors;

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

    /// <summary>An axisymmetric electrode is drawn as the ring it is (SYM-1).</summary>
    /// <remarks>
    /// <para>
    /// <b>The half-plane is not a picture of the geometry.</b> An axisymmetric solve says
    /// the geometry repeats all the way round, so a rectangle in the half-plane is a tube
    /// in space — and drawing the half-plane instead would be drawing the model's
    /// coordinates rather than its instrument.
    /// </para>
    /// <para>
    /// Checked by asking whether the surface reaches every azimuth. A half-plane profile
    /// would sit entirely at z = 0 and in the positive half of y, so this fails on both
    /// counts if the revolution is skipped.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAxisymmetricElectrodeIsDrawnAsTheRingItIs()
    {
        var outcome = ViewportCommand.Execute(Example("einzel-lens"));

        Assert.NotEmpty(outcome.Conductors);

        foreach (var conductor in outcome.Conductors)
        {
            var y = Axis(conductor, 1);
            var z = Axis(conductor, 2);

            output.WriteLine(
                $"{conductor.Name,-14} {conductor.TriangleCount(),6} triangles, "
                + $"{conductor.PotentialVolts,8:F1} V, "
                + $"y {y.Min:F1} to {y.Max:F1} mm, z {z.Min:F1} to {z.Max:F1} mm");

            // A ring reaches out to the same radius in every direction, so it must be
            // present above and below the axis and in front of and behind the plane.
            Assert.True(y.Min < -1e-3, $"{conductor.Name} does not reach below the axis");
            Assert.True(y.Max > 1e-3, $"{conductor.Name} does not reach above it");
            Assert.True(z.Min < -1e-3, $"{conductor.Name} is flat in z");
            Assert.True(z.Max > 1e-3, $"{conductor.Name} is flat in z");
        }
    }

    /// <summary>An axisymmetric domain below the axis is refused, not drawn (SYM-1).</summary>
    /// <remarks>
    /// <para>
    /// The second coordinate of an axisymmetric solve is a <em>radius</em>, and a radius is
    /// not negative — so a domain declared below zero describes a region that does not
    /// exist. Revolving a profile found there would draw the same surface a second time,
    /// coincident with the first: invisible except as z-fighting, and twice the geometry.
    /// </para>
    /// <para>
    /// <b>The viewport had a clamp for that, and this test is why it does not any more.</b>
    /// The case cannot occur — <c>ModelValidator</c> refuses such a document outright — and
    /// the test could not construct the input it was written to exercise. A second, weaker
    /// copy of a rule that already holds for every consumer is worse than none: it reads as
    /// though a case exists, and the next person has to work out which of the two is
    /// load-bearing.
    /// </para>
    /// <para>
    /// So what is asserted is the rule at the place it actually lives, including that the
    /// refusal is a recovery instruction (AGT-3) rather than a complaint.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAxisymmetricDomainBelowTheAxisIsRefused()
    {
        var path = Example("einzel-lens");

        // The lens as shipped, with its domain on the axis.
        Assert.NotEmpty(ViewportCommand.Execute(path).Conductors);

        const string Axis = "\"minY\": {\n          \"value\": 0,";
        const string Below = "\"minY\": {\n          \"value\": -8,";

        // Line endings normalised, because the corpus is written with \n and
        // Environment.NewLine here is \r\n - a test matching either one directly would
        // pass on one machine and fail on another for a reason that has nothing to do
        // with what it is checking.
        var model = File.ReadAllText(path).ReplaceLineEndings("\n");

        Assert.Contains(Axis, model, StringComparison.Ordinal);

        File.WriteAllText(path, model.Replace(Axis, Below, StringComparison.Ordinal));

        var refusal = Assert.Throws<EinzelException>(() => ViewportCommand.Execute(path));

        output.WriteLine($"{refusal.Error.Code} at {refusal.Error.Path}");
        output.WriteLine($"  {refusal.Error.Constraint}");
        output.WriteLine($"  {refusal.Error.Suggestion}");

        Assert.Equal("/fields/0/solve/minY", refusal.Error.Path);
        Assert.Contains("radius", refusal.Error.Constraint, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Error.Suggestion));
    }

    /// <summary>
    /// A cross-section's electrode is a prism, drawn to a stated depth (SYM-1, GRD-12).
    /// </summary>
    /// <remarks>
    /// A translational solve asserts the geometry is invariant along the third axis, so
    /// the electrode extends past anything drawn. Where the prism stops is a drawing
    /// convention, and a convention that is not stated is indistinguishable from a
    /// dimension of the instrument.
    /// </remarks>
    [Fact]
    public void ACrossSectionsElectrodeIsAPrismDrawnToAStatedDepth()
    {
        var outcome = ViewportCommand.Execute(Example("quadrupole-rf-stable"));

        Assert.NotEmpty(outcome.Conductors);

        foreach (var conductor in outcome.Conductors)
        {
            var z = Axis(conductor, 2);

            output.WriteLine(
                $"{conductor.Name,-12} {conductor.TriangleCount(),5} triangles, "
                + $"DC {conductor.PotentialVolts,7:F1} V, drive "
                + $"{conductor.DriveAmplitudeVolts,7:F1} V, z {z.Min:F1} to {z.Max:F1} mm");

            Assert.True(z.Max - z.Min > 1.0, $"{conductor.Name} has no depth");
        }

        var said = Assert.Single(outcome.Warnings, w => w.Code == "render.extruded-depth");

        output.WriteLine(said.Message);

        Assert.Contains("drawing convention", said.Message, StringComparison.Ordinal);

        // Drawn as far as the ions go, because the invariant axis is the one the beam
        // travels along - so the rods run the length of the flight rather than the
        // transverse span, which made them 32 mm of a 200 mm instrument.
        Assert.Contains("as far as the ions reach", said.Message, StringComparison.Ordinal);

        var reach = outcome.Trajectories
            .SelectMany(t => t.PointsMm)
            .Max(p => Math.Abs(p[2]));

        var drawn = outcome.Conductors.Max(c => Axis(c, 2).Max);

        output.WriteLine($"ions reach {reach:F1} mm along z, conductors drawn to {drawn:F1} mm");

        Assert.True(
            drawn >= reach,
            $"the conductors stop at {drawn:F1} mm and the ions reach {reach:F1} mm");
    }

    /// <summary>A driven electrode is not reported as earthed.</summary>
    /// <remarks>
    /// <b>The fifth appearance of one mistake, guarded rather than repeated.</b> A
    /// quadrupole's rods hold zero volts of DC and all of their potential as drive, so an
    /// electrode reported by its DC alone paints a mass filter as an earthed box —
    /// exactly what <c>einzel solve</c> did for every driven 2-D geometry, and what
    /// <c>CanDoWork</c> did three times.
    /// </remarks>
    [Fact]
    public void ADrivenElectrodeIsNotReportedAsEarthed()
    {
        var outcome = ViewportCommand.Execute(Example("quadrupole-rf-stable"));

        var driven = outcome.Conductors.Where(c => c.DriveAmplitudeVolts != 0.0).ToList();

        output.WriteLine($"{driven.Count} of {outcome.Conductors.Count} conductors are driven");

        Assert.NotEmpty(driven);

        // Rods in antiphase: the amplitudes are exact negatives, which is also what
        // collapses four rods to one basis solve.
        Assert.Contains(driven, c => c.DriveAmplitudeVolts > 0.0);
        Assert.Contains(driven, c => c.DriveAmplitudeVolts < 0.0);

        // And the scale spans what the drive reaches, not just the DC it sits at.
        var low = Assert.IsType<double>(outcome.LowestPotentialVolts);
        var high = Assert.IsType<double>(outcome.HighestPotentialVolts);

        output.WriteLine($"potential scale {low:F1} to {high:F1} V");

        Assert.True(high - low > 1.0, "the potential scale collapsed to the DC");
    }

    /// <summary>Equipotentials are drawn, and sit inside the potential scale.</summary>
    /// <remarks>
    /// §16 asks for equipotential surfaces or slices. Levels between the extremes rather
    /// than at them, because a contour at the exact minimum of a sampled field is either
    /// empty or the whole boundary.
    /// </remarks>
    [Fact]
    public void EquipotentialsAreDrawnInsideTheScale()
    {
        var outcome = ViewportCommand.Execute(Example("einzel-lens"));

        var low = Assert.IsType<double>(outcome.LowestPotentialVolts);
        var high = Assert.IsType<double>(outcome.HighestPotentialVolts);

        output.WriteLine($"{outcome.Equipotentials.Count} levels over {low:F1} to {high:F1} V");

        Assert.NotEmpty(outcome.Equipotentials);

        foreach (var level in outcome.Equipotentials)
        {
            output.WriteLine(
                $"  {level.PotentialVolts,8:F2} V - {level.PathsMm.Count} path(s), "
                + $"{level.PathsMm.Sum(p => p.Count / 3)} points");

            Assert.InRange(level.PotentialVolts, low, high);
            Assert.All(level.PathsMm, path => Assert.True(path.Count >= 6));
            Assert.All(level.PathsMm, path => Assert.Equal(0, path.Count % 3));
        }

        // Ascending and distinct, so a reader can put them on a scale.
        var values = outcome.Equipotentials.Select(e => e.PotentialVolts).ToList();

        Assert.Equal(values.OrderBy(v => v), values);
        Assert.Equal(values.Distinct().Count(), values.Count);
    }

    /// <summary>A diffusive model still shows its field, over its own region (RND-8).</summary>
    /// <remarks>
    /// <para>
    /// <b>RND-8 forbids trajectories through a diffusive region, not the instrument they
    /// would have flown through.</b> A funnel drawn with no rings and no field is a picture
    /// of nothing at all, and the requirement was never about that — so the geometry is
    /// built on that path too and only the paths are withheld.
    /// </para>
    /// <para>
    /// The region comes from the declared density grid, which for this kind of model is
    /// the only thing that says where it reaches: a drift tube in a uniform field declares
    /// no solve domain and produces no trajectories, so every other source of an extent is
    /// empty and the field would be drawn over nothing.
    /// </para>
    /// <para>
    /// No conductors are asserted because <em>no diffusive example declares any</em>. That
    /// is a gap in the corpus rather than in the code — the device this mode exists for is
    /// a funnel, which is nothing but electrodes — and it is recorded in
    /// <c>docs/pressure.md</c> rather than papered over with a model written here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusiveModelStillShowsItsField()
    {
        var outcome = ViewportCommand.Execute(Example("drift-tube-diffusion"));

        Assert.False(outcome.ProducesTrajectories);
        Assert.Empty(outcome.Trajectories);

        var low = Assert.IsType<double>(outcome.LowestPotentialVolts);
        var high = Assert.IsType<double>(outcome.HighestPotentialVolts);

        output.WriteLine(
            $"{outcome.Equipotentials.Count} equipotentials over {low:F1} to {high:F1} V");

        Assert.NotEmpty(outcome.Equipotentials);
        Assert.All(
            outcome.Equipotentials, e => Assert.InRange(e.PotentialVolts, low, high));
    }

    /// <summary>Every mesh is well formed: indices in range, one normal per vertex.</summary>
    /// <remarks>
    /// The cheapest check that nothing downstream will read off the end of a buffer, run
    /// over every shipped symmetry at once — a malformed index reaches a graphics driver
    /// rather than an exception, and what it does there is not defined.
    /// </remarks>
    [Theory]
    [InlineData("einzel-lens")]
    [InlineData("quadrupole-rf-stable")]
    [InlineData("paul-trap-held")]
    [InlineData("parallel-plate-gap-3d")]
    public void EveryMeshIsWellFormed(string example)
    {
        var outcome = ViewportCommand.Execute(Example(example));

        Assert.NotEmpty(outcome.Conductors);

        foreach (var conductor in outcome.Conductors)
        {
            output.WriteLine(
                $"{conductor.Name,-14} {conductor.VerticesMm.Count / 3,6} vertices, "
                + $"{conductor.TriangleCount(),6} triangles");

            Assert.Equal(0, conductor.VerticesMm.Count % 3);
            Assert.Equal(conductor.VerticesMm.Count, conductor.Normals.Count);
            Assert.Equal(0, conductor.Triangles.Count % 3);

            Assert.All(
                conductor.Triangles,
                i => Assert.InRange(i, 0, (conductor.VerticesMm.Count / 3) - 1));

            Assert.All(conductor.VerticesMm, v => Assert.True(double.IsFinite(v)));
            Assert.All(conductor.Normals, n => Assert.True(double.IsFinite(n)));
        }
    }

    /// <summary>The span of one coordinate over a conductor's vertices, in millimetres.</summary>
    private static (double Min, double Max) Axis(ConductorSurface conductor, int axis)
    {
        var min = double.MaxValue;
        var max = double.MinValue;

        for (var v = axis; v < conductor.VerticesMm.Count; v += 3)
        {
            min = Math.Min(min, conductor.VerticesMm[v]);
            max = Math.Max(max, conductor.VerticesMm[v]);
        }

        return (min, max);
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

/// <summary>Reading helpers for the tests above.</summary>
internal static class ConductorReading
{
    /// <summary>How many triangles a conductor's mesh holds.</summary>
    internal static int TriangleCount(this ConductorSurface conductor) =>
        conductor.Triangles.Count / 3;
}
