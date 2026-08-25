using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Library;
using Einzel.Transport;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The rectilinear trap: what flat plates cost, and what a slot costs on top.
/// </summary>
/// <remarks>
/// <para>
/// The cross-section of the Stewart/Grinfeld Ion Processor, and the third device
/// template. It shares no code with the mirror or the quadrupole; it is a
/// different arrangement of the same rectangle primitive, which is the whole of
/// LIB-1.
/// </para>
/// <para>
/// Two questions, and they are separate. With the trapping potentials applied,
/// how well do four flat plates approximate a hyperbolic field - the same
/// multipole measurement the round-rod quadrupole gets, so the two are directly
/// comparable. With the extraction potentials applied, how far does a slotted
/// plate depart from the uniform field that the turn-around-time closed form
/// assumes.
/// </para>
/// <para>
/// The second question is the one the literature target needs, and it is the one
/// that cannot be answered by assuming anything.
/// </para>
/// </remarks>
public sealed class RectilinearTrapStudy(ITestOutputHelper output)
{
    private const double BoltzmannSi = 1.380649e-23;

    private static ModelDocument Template() => Io.ModelJson.Parse(DeviceTemplates.Read("rectilinear-trap"));

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

    [Fact]
    public void TheTrapSolvesAndObeysTheMaximumPrinciple()
    {
        // The cheapest exact check that a solve has not diverged: a harmonic
        // function attains its extremes on the boundary, so no potential anywhere
        // may exceed the largest applied value. Interior electrodes are where
        // multigrid coarsening goes wrong, and five of them in a box is exactly
        // the case that has failed before.
        var model = Compile(Template());
        var field = FieldAssembly.Build(model);

        var applied = model.Parameters["pushPotential"].In("V");
        var r0 = model.Parameters["inscribedRadius"].In("m");

        var peak = 0.0;

        for (var i = -40; i <= 40; i++)
        {
            for (var j = -40; j <= 40; j++)
            {
                var point = new Vec3(r0 * i / 45.0, r0 * j / 45.0, 0.0);
                peak = Math.Max(peak, Math.Abs(field.PotentialAt(in point)));
            }
        }

        output.WriteLine($"applied {applied:F0} V, peak inside the aperture {peak:F1} V");

        Assert.True(peak <= Math.Abs(applied), $"potential reached {peak:F1} V against an applied {applied:F0} V");
        Assert.True(peak > 0.05 * Math.Abs(applied), "the aperture is at essentially zero, so nothing was solved");
    }

    [Fact]
    public void FlatPlatesAreAMuchCruderQuadrupoleThanRoundRods()
    {
        // Both devices measured the same way, on the same quantity, so the answer
        // is a comparison rather than an isolated number. The rectilinear trap is
        // put in its trapping configuration - side plates against front and back -
        // which is what the RF drives when the trap is trapping.
        //
        // Flat plates should be far worse, and that is not a defect: a rectilinear
        // trap is chosen for what it is easy to build and easy to cut a slot in,
        // and it pays for that in field quality. Quantifying the payment is the
        // point.
        var trap = Compile(With(Template(), ("sidePotential", 100.0), ("frontPotential", -100.0), ("pushPotential", -100.0)));
        var quadrupole = Compile(Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole")));

        var trapFraction = Math.Abs(TwelvePoleFraction(trap, 0.5));
        var rodFraction = Math.Abs(TwelvePoleFraction(quadrupole, 0.5));

        output.WriteLine($"rectilinear, flat plates   12-pole/quadrupole = {trapFraction:E3}");
        output.WriteLine($"round rods at 1.1468       12-pole/quadrupole = {rodFraction:E3}");
        output.WriteLine($"ratio                      {trapFraction / rodFraction:F1}x");

        // The trapping configuration must actually be quadrupolar at all, or the
        // fraction is a ratio of two numbers that mean nothing.
        var (a2, _, _) = Multipoles(FieldAssembly.Build(trap), 0.5 * trap.Parameters["inscribedRadius"].In("m"));
        Assert.True(Math.Abs(a2) > 1.0, $"the trapping configuration is not quadrupolar; A2 = {a2:E3}");

        Assert.True(
            trapFraction > 10.0 * rodFraction,
            $"flat plates gave {trapFraction:E3} against round rods at {rodFraction:E3}, which is closer than "
            + "flat plates should manage and suggests the trapping configuration is not what it should be");
    }

    [Fact]
    public void TheExtractionFieldIsNotTheUniformOneTheClosedFormAssumes()
    {
        // The turn-around formula takes a single field strength. A real trap has a
        // slot in the plate the ions leave through, and a slot is a hole in a
        // conductor: the field sags into it. So there are two departures to
        // measure - how far the field at the trap centre is from the naive
        // V / 2 r0, and how much it varies across the packet.
        var model = Compile(Template());
        var field = FieldAssembly.Build(model);

        var r0 = model.Parameters["inscribedRadius"].In("m");
        var push = model.Parameters["pushPotential"].In("V");
        var naive = push / (2.0 * r0);

        var centre = new Vec3(0.0, 0.0, 0.0);
        var atCentre = field.ElectricFieldAt(in centre).Y;

        output.WriteLine($"naive V / 2 r0     {naive / 1e5:F4} x 1e5 V/m");
        output.WriteLine($"solved, on axis    {atCentre / 1e5:F4} x 1e5 V/m  ({atCentre / naive:P1} of naive)");
        output.WriteLine(string.Empty);
        output.WriteLine("across the packet, at +/- 0.5 mm:");

        var strengths = new List<double>();

        foreach (var offset in new[] { -5e-4, -2.5e-4, 0.0, 2.5e-4, 5e-4 })
        {
            var acrossPoint = new Vec3(offset, 0.0, 0.0);
            var alongPoint = new Vec3(0.0, offset, 0.0);

            var across = field.ElectricFieldAt(in acrossPoint).Y;
            var along = field.ElectricFieldAt(in alongPoint).Y;

            strengths.Add(across);
            strengths.Add(along);

            output.WriteLine(
                $"  {offset * 1e3,+5:F2} mm   across {across / 1e5:F4}   along {along / 1e5:F4}");
        }

        var spread = (strengths.Max() - strengths.Min()) / strengths.Average();
        output.WriteLine($"\nfield varies by {spread:P1} over a 1 mm cube at the trap centre");

        // Pushing the right way, or nothing else in this file means anything.
        Assert.True(atCentre > 0.0, "the extraction field does not push toward the slot");

        // The naive estimate is an overestimate, because the slot lets the field
        // sag and the corner gaps let it leak. Asserted as a range rather than a
        // value: the point is that it is neither equal to the naive figure nor
        // wildly different from it.
        Assert.InRange(atCentre / naive, 0.5, 0.95);

        // And it is not uniform, which is the assumption the closed form makes.
        Assert.True(spread > 0.01, $"the field varied by only {spread:P2}, which is suspiciously uniform");
    }

    [Fact]
    public void TurnAroundTimeIsSetByTheSolvedFieldNotTheNaiveOne()
    {
        // The measurement the Ion Processor target needs. A cloud with temperature
        // and nothing else - no spatial spread - so what is measured is turn-around
        // and only turn-around, and it can be compared against the closed form
        // evaluated at two different field strengths: the naive one, and the one
        // the solve actually produces.
        var document = Template();
        var model = Compile(document);
        var field = FieldAssembly.Build(model);

        var species = IonSpecies.FromModel(model);
        var r0 = model.Parameters["inscribedRadius"].In("m");
        var push = model.Parameters["pushPotential"].In("V");

        var centre = new Vec3(0.0, 0.0, 0.0);
        var solved = field.ElectricFieldAt(in centre).Y;

        var measured = TurnAround(document);

        var fromNaive = ClosedForm(species, 300.0, push / (2.0 * r0));
        var fromSolved = ClosedForm(species, 300.0, solved);

        output.WriteLine($"closed form at the naive field    {fromNaive * 1e9:F3} ns");
        output.WriteLine($"closed form at the solved field   {fromSolved * 1e9:F3} ns");
        output.WriteLine($"measured through the geometry     {measured * 1e9:F3} ns");
        output.WriteLine(string.Empty);
        output.WriteLine($"the naive field is wrong by {Math.Abs(fromNaive - measured) / measured:P1}");
        output.WriteLine($"the solved field is wrong by {Math.Abs(fromSolved - measured) / measured:P1}");
        output.WriteLine(string.Empty);
        output.WriteLine("The Ion Processor reports 0.8-1.2 ns across m/z 195-2722.");

        // Using the field the geometry actually produces must beat assuming
        // V / 2 r0. If it did not, solving the geometry bought nothing and the
        // closed form would be the better tool.
        Assert.True(
            Math.Abs(fromSolved - measured) < Math.Abs(fromNaive - measured),
            $"the naive field predicted {fromNaive * 1e9:F3} ns and the solved field {fromSolved * 1e9:F3} ns "
            + $"against a measured {measured * 1e9:F3} ns, so solving the geometry did not help");

        // And the solved field should be close, because once the field strength is
        // right the closed form is exact for a uniform field and this one is nearly
        // uniform over a cold packet that barely moves.
        Assert.InRange(fromSolved / measured, 0.85, 1.15);
    }

    [Theory]
    [InlineData(195.0)]
    [InlineData(500.0)]
    [InlineData(2722.0)]
    public void TurnAroundAcrossTheIonProcessorMassRange(double massToCharge)
    {
        // The mass range the paper reports over. Turn-around goes as the square
        // root of mass, so these should spread by a factor of 3.7 across it - and
        // the reported 0.8 to 1.2 ns does not, which is the open question this
        // target carries.
        var document = Template();

        var ion = document.Ion! with
        {
            MassToCharge = new QuantityValue(massToCharge, "Da"),
        };

        var measured = TurnAround(document with { Ion = ion });

        output.WriteLine($"m/z {massToCharge,6:F0}   turn-around {measured * 1e9:F3} ns");

        Assert.True(measured > 0.0);
        Assert.True(measured < 1e-6, "a turn-around time of over a microsecond means the extraction is not working");
    }

    [Fact]
    public void TurnAroundIsTheSmallestOfTheThreeThingsThatSetTheArrivalSpread()
    {
        // The decomposition that says what a real extraction is limited by. Three
        // properties of the packet reach the arrival time, and they are switched on
        // one at a time so each is measured alone rather than inferred from a
        // total:
        //
        //   thermal velocity   - turn-around, the number the literature quotes
        //   depth              - an ion further from the slot falls through more
        //                        potential and arrives with more energy
        //   width              - an ion off axis falls through a different
        //                        potential, because a slotted plate does not make
        //                        a field that is flat across it
        //
        // In this geometry turn-around is the smallest by an order of magnitude,
        // which matters for reading a published Delta-t: a number near a
        // nanosecond cannot be the arrival spread of a packet a fifth of a
        // millimetre deep, so it is either a much tighter packet or a corrected
        // figure.
        var document = Template();

        var thermal = Fwhm(document, temperature: true, depth: 0.0, width: 0.0);
        var withDepth = Fwhm(document, temperature: true, depth: 0.2, width: 0.0);
        var withWidth = Fwhm(document, temperature: true, depth: 0.0, width: 0.2);
        var everything = Fwhm(document, temperature: true, depth: 0.2, width: 0.2);

        output.WriteLine($"thermal only (turn-around)   {thermal * 1e9,8:F2} ns");
        output.WriteLine($"+ 0.2 mm depth               {withDepth * 1e9,8:F2} ns");
        output.WriteLine($"+ 0.2 mm width               {withWidth * 1e9,8:F2} ns");
        output.WriteLine($"all three                    {everything * 1e9,8:F2} ns");

        // Each pair-wise run already contains the thermal term, so the depth and
        // width contributions are what is left when it is taken back out.
        var fromDepth = Math.Sqrt((withDepth * withDepth) - (thermal * thermal));
        var fromWidth = Math.Sqrt((withWidth * withWidth) - (thermal * thermal));

        var quadrature = Math.Sqrt(
            (thermal * thermal) + (fromDepth * fromDepth) + (fromWidth * fromWidth));

        output.WriteLine(string.Empty);
        output.WriteLine($"  depth alone                {fromDepth * 1e9,8:F2} ns");
        output.WriteLine($"  width alone                {fromWidth * 1e9,8:F2} ns");
        output.WriteLine($"  three in quadrature        {quadrature * 1e9,8:F2} ns");

        // Turn-around is what a published figure names, and here it is the least of
        // the three. Stated as an assertion because it is the conclusion, not an
        // observation about one run.
        Assert.True(
            thermal < 0.2 * everything,
            $"turn-around is {thermal * 1e9:F2} ns of a total {everything * 1e9:F2} ns, which is a larger share "
            + "than this geometry should give and suggests one of the other spreads did not apply");

        // Independent contributions add in quadrature. Agreement says the three
        // really are separate mechanisms rather than one mechanism counted thrice.
        Assert.InRange(quadrature / everything, 0.9, 1.1);
    }

    [Fact]
    public void TheArrivalSpreadGrowsWithDriftBecauseThereIsNoUsefulSpaceFocus()
    {
        // A single-stage extraction has a Wiley-McLaren space focus, where the ion
        // that started deeper catches the one in front. For a uniform field it sits
        // at twice the source depth, which would be about 6 mm here.
        //
        // It is not there. The spread grows linearly with drift over the whole
        // practical range, so the focus - if it is anywhere - is at essentially
        // zero drift, and a detector at any usable distance is far past it. That is
        // what a field varying by a factor of two across the packet does to a
        // first-order focusing condition derived for a uniform one, and it is the
        // reason a real instrument adds a second acceleration stage rather than
        // moving the detector.
        var document = Template();

        output.WriteLine("drift / mm   arrival FWHM / ns");

        var points = new List<(double Drift, double Fwhm)>();

        foreach (var drift in new[] { 2.0, 5.0, 8.0, 11.0 })
        {
            var fwhm = ArrivalSpread(With(document, ("driftLength", drift)), ions: 500);
            points.Add((drift, fwhm));

            output.WriteLine($"{drift,10:F1}   {fwhm * 1e9,17:F2}");
        }

        var slope = (points[^1].Fwhm - points[0].Fwhm) / (points[^1].Drift - points[0].Drift);

        output.WriteLine(string.Empty);
        output.WriteLine($"slope {slope * 1e9 / 1e-3 * 1e-3:F1} ns per mm, and monotone throughout");
        output.WriteLine("an ideal-field first-order focus would be at 2 x 3 mm = 6 mm");

        // Monotone: every step wider than the last. One interior minimum would be a
        // focus, and there is none.
        for (var k = 1; k < points.Count; k++)
        {
            Assert.True(
                points[k].Fwhm > points[k - 1].Fwhm,
                $"the peak narrowed from {points[k - 1].Fwhm * 1e9:F2} to {points[k].Fwhm * 1e9:F2} ns between "
                + $"{points[k - 1].Drift:F1} and {points[k].Drift:F1} mm, so there is a focus after all");
        }
    }

    /// <summary>Arrival width with each source spread switched on or off, in millimetres.</summary>
    private static double Fwhm(ModelDocument document, bool temperature, double depth, double width)
    {
        var configured = document with
        {
            Source = document.Source! with
            {
                Cloud = document.Source!.Cloud! with
                {
                    Ions = 1500,
                    Temperature = temperature ? document.Source!.Cloud!.Temperature : null,
                    LongitudinalSpread = depth > 0.0 ? new QuantityValue(depth, "mm") : null,
                    TransverseSpread = width > 0.0 ? new QuantityValue(width, "mm") : null,
                },
            },
        };

        return Fly(configured).GaussianEquivalentFwhmSeconds;
    }

    /// <summary>Arrival-time width of the full declared cloud, spatial spread and all.</summary>
    private static double ArrivalSpread(ModelDocument document, int ions)
    {
        var sampled = document with
        {
            Source = document.Source! with
            {
                Cloud = document.Source!.Cloud! with { Ions = ions },
            },
        };

        return Fly(sampled).GaussianEquivalentFwhmSeconds;
    }

    /// <summary>The closed form: FWHM = 2 sqrt(2 ln 2) sqrt(m k T) / q E.</summary>
    private static double ClosedForm(IonSpecies species, double temperatureK, double fieldVoltsPerMetre) =>
        2.0 * Math.Sqrt(2.0 * Math.Log(2.0))
        * Math.Sqrt(species.MassSi * BoltzmannSi * temperatureK)
        / (Math.Abs(species.ChargeSi) * fieldVoltsPerMetre);

    /// <summary>Flies a cloud that has temperature and nothing else.</summary>
    private static double TurnAround(ModelDocument document)
    {
        var thermalOnly = document with
        {
            Source = document.Source! with
            {
                Cloud = document.Source!.Cloud! with
                {
                    Ions = 3000,
                    TransverseSpread = null,
                    LongitudinalSpread = null,
                },
            },
        };

        return Fly(thermalOnly).GaussianEquivalentFwhmSeconds;
    }

    /// <summary>Flies a model's declared cloud and returns the peak it forms.</summary>
    private static Analysis.ArrivalTimePeak Fly(ModelDocument document)
    {
        var model = ModelValidator.Validate(document).Model!;
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        // The declared direction matters here in a way it does not for a beam: this
        // packet starts at rest, so nothing in its velocity says which way is out.
        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());
        var cloud = IonCloud.Draw(in launch, species, model.Cloud, model.SourceDirection);

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        var settings = new Transport.Integration.IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        Transport.Integration.TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

        var arrivals = new List<double>(cloud.Length);

        foreach (var start in cloud)
        {
            var result = Transport.Integration.TrajectoryIntegrator.Integrate(
                start, species, field, settings, detector);

            if (result.Outcome == Transport.Integration.TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(result.FlightTimeSeconds);
            }
        }

        return Analysis.ArrivalTimePeak.FromArrivals(arrivals, cloud.Length);
    }

    /// <summary>Multipole content on a circle, by discrete cosine transform.</summary>
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
}
