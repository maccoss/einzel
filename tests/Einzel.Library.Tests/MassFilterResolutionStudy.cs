using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Sweeps;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A mass filter's transmission against its resolution, along the classical scan
/// line.
/// </summary>
/// <remarks>
/// <para>
/// Spec §21 makes "quadrupole transmission-against-resolution curve" an acceptance
/// criterion for Phase 3, and §12 puts "mass filter peak shape and resolution
/// against scan line" in Class B. Both are the same measurement: hold U/V fixed,
/// scan V, and the width of the stability band <em>is</em> the width in mass -
/// because q goes as V/m, so a band of relative width dV/V passes a band of
/// relative width dm/m, and the resolution is its reciprocal.
/// </para>
/// <para>
/// Both edges are located by <see cref="BoundarySearch"/> rather than by a grid.
/// ACC-6 asks for one part in five hundred of the scan and a grid costs 501
/// evaluations to reach it; bisection costs about eleven, which is what makes a
/// curve of seven working points affordable at all.
/// </para>
/// <para>
/// <strong>Every number compared against here is tabulated, not produced by this
/// engine.</strong> The apex of the first stability region is at a = 0.23699,
/// q = 0.70600, so the scan line runs out at U/V = a/2q = 0.16785; the a = 0
/// low-mass cut-off is q = 0.90804.
/// </para>
/// </remarks>
public sealed class MassFilterResolutionStudy(ITestOutputHelper output)
{
    /// <summary>Tabulated apex of the first stability region.</summary>
    private const double ApexQ = 0.70600;

    /// <summary>The same apex in a.</summary>
    private const double ApexA = 0.23699;

    /// <summary>Where the scan line runs out: U/V = a / 2q at the apex.</summary>
    private const double ApexRatio = ApexA / (2.0 * ApexQ);

    /// <summary>
    /// The template with the DC tied to the RF, so scanning V holds a/q fixed.
    /// </summary>
    /// <remarks>
    /// This is the whole content of "along a scan line". Scanning the amplitude
    /// with a fixed DC would walk off the line rather than along it, and the band
    /// it swept would not be a mass peak. A derived parameter is the natural way to
    /// say it, and it needs no change to the template: <c>dcPotential</c> was
    /// already an expression over the parameter surface.
    /// </remarks>
    private static ModelDocument ScanLine(double ratio)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole-rf"));

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal)
        {
            ["dcRatio"] = new()
            {
                Value = ratio,
                Unit = "1",
                Minimum = 0.0,
                Maximum = 0.2,
                Description = "U/V along the scan line.",
            },

            ["dcPotential"] = new()
            {
                Expression = "dcRatio * rfAmplitude",
                Unit = "V",
                Description = "Derived, so a scan of the amplitude runs along a line of constant "
                    + "a/q rather than across it.",
            },
        };

        return document with
        {
            Parameters = parameters,

            // Long enough for the ion to cross: 10 V of injection energy gives
            // 1964 m/s, so 200 mm of filter takes 102 us. The ceiling has to clear
            // that, or every working point reads as unstable.
            Transport = (document.Transport ?? new TransportDocument()) with
            {
                MaximumFlightTime = new QuantityValue(200.0, "us"),
                RelativeTolerance = 1e-9,
            },
        };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        return validation.Model!;
    }

    /// <summary>Volts per unit of Mathieu q, from the model rather than hard-coded.</summary>
    private static double VoltsPerQ(CompiledModel model)
    {
        var species = IonSpecies.FromModel(model);
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].In("Hz");
        var r0 = model.Parameters["inscribedRadius"].In("m");

        return species.MassSi * omega * omega * r0 * r0 / (4.0 * Math.Abs(species.ChargeSi));
    }

    /// <summary>One if the ion traverses the filter, zero if it strikes a rod.</summary>
    /// <remarks>
    /// <para>
    /// Physical rather than geometric: the rods are solid, so an unstable ion ends
    /// on a named surface, which is what happens in the instrument. The criterion
    /// is reaching the <em>detector</em>, not surviving a fixed number of cycles -
    /// which is the same thing a transmission means everywhere else here, and it
    /// matters at the edges of the band.
    /// </para>
    /// <para>
    /// A first version stopped after twenty RF cycles and asked whether the ion had
    /// struck anything yet. Near the low-q edge the instability is weak and takes
    /// far longer than twenty cycles to grow past the inscribed radius, so that
    /// version called the whole low-q region stable and the bracket had no edge in
    /// it at all. The window has to be long enough for the slowest instability the
    /// bracket contains, and "long enough" is exactly the transit time.
    /// </para>
    /// </remarks>
    private static double? Survives(CompiledModel model)
    {
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Core.Geometry.Vec3.Dot(state.Position - point, normal);

        var result = TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector);

        return result.Outcome == TrajectoryOutcome.StopConditionMet ? 1.0 : 0.0;
    }

    /// <summary>One edge of the band, in volts, bisected to ACC-6.</summary>
    private static double Edge(ModelDocument document, double fromVolts, double toVolts)
    {
        var result = BoundarySearch.Run(
            document,
            new ScanAxis(
                "rfAmplitude", Quantity.From(fromVolts, "V"), Quantity.From(toVolts, "V"), 2),
            Survives,
            0.5);

        Assert.True(result.MetAccuracyTarget, "the edge was not resolved to ACC-6");

        var (value, _, _, _) = result.Boundary;

        return value.In("V");
    }

    [Fact]
    public void TheStabilityBandClosesOntoTheTabulatedApex()
    {
        // The signature of a mass filter, and the one thing about it that is
        // published rather than measured here: as U/V rises toward a/2q at the
        // apex, the pass band narrows to nothing and its centre walks onto the
        // apex q. Both the narrowing and the destination are asserted, because
        // either alone is much weaker - a filter whose band narrows onto the wrong
        // q is a filter with the wrong geometry, and one that sits at the right q
        // with a band that never narrows is not filtering.
        var voltsPerQ = VoltsPerQ(Compile(ScanLine(0.0)));

        output.WriteLine($"apex, tabulated      a = {ApexA:F5}, q = {ApexQ:F5}, so U/V runs out at {ApexRatio:F5}");
        output.WriteLine($"volts per unit q     {voltsPerQ:F3}");
        output.WriteLine(string.Empty);
        output.WriteLine("   U/V      a/q     q_low    q_high  q_centre     width         R");

        var resolutions = new List<double>();
        var widths = new List<double>();
        var centres = new List<double>();

        // Stops short of the apex ratio on purpose: at the apex the band is a
        // point, and a bisection needs a bracket with width in it.
        foreach (var ratio in new[] { 0.10, 0.13, 0.15, 0.16 })
        {
            var document = ScanLine(ratio);

            // Brackets from the tabulated geometry, not from a previous run: at
            // this ratio the band lies inside (0, apex q) with room either side.
            var low = Edge(document, 0.02 * voltsPerQ, ApexQ * voltsPerQ);
            var high = Edge(document, ApexQ * voltsPerQ, 1.0 * voltsPerQ);

            var centre = 0.5 * (low + high);
            var width = high - low;
            var resolution = centre / width;

            output.WriteLine(
                $"{ratio,7:F3}  {2.0 * ratio,7:F3}  {low / voltsPerQ,8:F5}  {high / voltsPerQ,8:F5}  "
                + $"{centre / voltsPerQ,8:F5}  {width / voltsPerQ,8:F5}  {resolution,8:F1}");

            resolutions.Add(resolution);
            widths.Add(width / voltsPerQ);
            centres.Add(centre / voltsPerQ);
        }

        // The band narrows and the resolution rises, monotonically, all the way.
        for (var i = 1; i < resolutions.Count; i++)
        {
            Assert.True(
                resolutions[i] > resolutions[i - 1],
                $"resolution fell from {resolutions[i - 1]:F1} to {resolutions[i]:F1}");

            Assert.True(
                widths[i] < widths[i - 1],
                $"the band widened from {widths[i - 1]:F5} to {widths[i]:F5} in q");
        }

        // A real filter, not a marginal one: the last working point resolves better
        // than ten, which is where the band is a tenth of its a = 0 width.
        Assert.True(resolutions[^1] > 10.0, $"the tightest point only reached R = {resolutions[^1]:F1}");

        // And the centre walks onto the tabulated apex from below. Round rods carry
        // a 12-pole a hyperbola does not, so the whole diagram sits slightly inside
        // the ideal one - the same direction and the same order as the a = 0
        // cut-off, which comes out 0.33% below its tabulated 0.90804.
        output.WriteLine(string.Empty);
        output.WriteLine(
            $"closest approach     q = {centres[^1]:F5} against the tabulated apex {ApexQ:F5}, "
            + $"{100.0 * (centres[^1] - ApexQ) / ApexQ:+0.00;-0.00}%");

        Assert.True(centres[^1] < ApexQ, "the band centre passed the apex, which is not a place");
        Assert.InRange(centres[^1], 0.97 * ApexQ, ApexQ);

        // Monotone approach, not a wander that happens to end near it.
        for (var i = 1; i < centres.Count; i++)
        {
            Assert.True(
                centres[i] > centres[i - 1],
                $"the band centre moved away from the apex, {centres[i - 1]:F5} to {centres[i]:F5}");
        }
    }
}
