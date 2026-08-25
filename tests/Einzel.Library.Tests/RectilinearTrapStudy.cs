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
    public void ThisTrapIsAMuchCruderQuadrupoleThanRoundRods()
    {
        // Both devices measured the same way, on the same quantity, so the answer
        // is a comparison rather than an isolated number. The trap is put in its
        // trapping configuration - side plates against front and back - which is
        // what the RF drives when the trap is trapping.
        //
        // The quantity is the largest unwanted multipole rather than the 12-pole
        // specifically. For four identical round rods the 12-pole is the largest by
        // symmetry, and every odd order vanishes identically. This trap has a slot
        // in one plate and not the other, so it is not four-fold symmetric and its
        // odd orders do not vanish - measuring only the 12-pole would name the
        // wrong aberration and attribute it to the plate shape.
        var trap = Compile(With(
            Template(), ("sidePotential", 100.0), ("frontPotential", -100.0), ("pushPotential", -100.0)));

        var quadrupole = Compile(Io.ModelJson.Parse(DeviceTemplates.Read("quadrupole")));

        var trapField = FieldAssembly.Build(trap);
        var r0 = trap.Parameters["inscribedRadius"].In("m");
        var terms = Multipoles(trapField, 0.5 * r0, highestOrder: 10);

        output.WriteLine("rectilinear trap, trapping configuration, at r0/2:");

        for (var order = 1; order <= 10; order++)
        {
            if (order != 2)
            {
                output.WriteLine($"  order {order,2}   {terms[order] / terms[2]:E3} of the quadrupole");
            }
        }

        var (trapOrder, trapFraction) = WorstMultipole(trap, 0.5);
        var (rodOrder, rodFraction) = WorstMultipole(quadrupole, 0.5);

        output.WriteLine(string.Empty);
        output.WriteLine($"trap        worst is order {trapOrder,2} at {trapFraction:E3}");
        output.WriteLine($"round rods  worst is order {rodOrder,2} at {rodFraction:E3}");
        output.WriteLine($"ratio       {trapFraction / rodFraction:F1}x");

        // The trapping configuration must actually be quadrupolar at all, or the
        // fraction is a ratio of two numbers that mean nothing.
        Assert.True(Math.Abs(terms[2]) > 1.0, $"the trapping configuration is not quadrupolar; A2 = {terms[2]:E3}");

        // Round rods are four-fold symmetric, so their worst term should be an even
        // one. If an odd order won there, the measurement is picking up noise.
        Assert.True(rodOrder % 2 == 0, $"the round-rod quadrupole's worst term was order {rodOrder}, which is odd");

        // Which of the two departures from a round-rod quadrupole is responsible?
        // Narrowing the slot leaves the flat plates untouched and takes the
        // y-asymmetry away, so whatever collapses was the slot's.
        var narrowSlot = Compile(With(
            Template(),
            ("sidePotential", 100.0), ("frontPotential", -100.0), ("pushPotential", -100.0),
            ("slotWidth", 0.1)));

        var narrowTerms = Multipoles(
            FieldAssembly.Build(narrowSlot), 0.5 * r0, highestOrder: 10);

        output.WriteLine(string.Empty);
        output.WriteLine("with the slot narrowed from 1.0 mm to 0.1 mm:");
        output.WriteLine($"  order  1   {narrowTerms[1] / narrowTerms[2]:E3}  (was {terms[1] / terms[2]:E3})");
        output.WriteLine($"  order  6   {narrowTerms[6] / narrowTerms[2]:E3}  (was {terms[6] / terms[2]:E3})");

        // The dipole is the slot's; the 12-pole is the plates'. Asserted, because
        // the two are quoted separately and attributing one to the other is the
        // mistake this test exists to prevent.
        Assert.True(
            narrowTerms[1] / narrowTerms[2] < 0.5 * (terms[1] / terms[2]),
            "narrowing the slot did not reduce the dipole, so the dipole is not the slot's");

        Assert.True(
            Math.Abs((narrowTerms[6] / narrowTerms[2]) - (terms[6] / terms[2])) < 0.5 * (terms[6] / terms[2]),
            "narrowing the slot changed the 12-pole substantially, so it is not a property of the plates alone");

        Assert.True(
            trapFraction > 10.0 * rodFraction,
            $"the trap gave {trapFraction:E3} against round rods at {rodFraction:E3}, which is closer than "
            + "flat plates and a slot should manage and suggests the trapping configuration is not what it should be");
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

        // From the compiled cloud rather than a literal. Both sides of the
        // comparison have to be about the same physics, and a template whose
        // temperature was edited would otherwise fail this test for a reason that
        // has nothing to do with the field.
        var temperature = model.Cloud.TemperatureK;

        var fromNaive = ClosedForm(species, temperature, push / (2.0 * r0));
        var fromSolved = ClosedForm(species, temperature, solved);

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

        var thermal = Fwhm(document, depth: 0.0, width: 0.0);
        var withDepth = Fwhm(document, depth: 0.2, width: 0.0);
        var withWidth = Fwhm(document, depth: 0.0, width: 0.2);
        var everything = Fwhm(document, depth: 0.2, width: 0.2);

        output.WriteLine($"thermal only (turn-around)   {thermal * 1e9,8:F2} ns");
        output.WriteLine($"+ 0.2 mm depth               {withDepth * 1e9,8:F2} ns");
        output.WriteLine($"+ 0.2 mm width               {withWidth * 1e9,8:F2} ns");
        output.WriteLine($"all three                    {everything * 1e9,8:F2} ns");

        // Each pair-wise run already contains the thermal term, so the depth and
        // width contributions are what is left when it is taken back out. Asserted
        // rather than clamped: a negative difference means adding a spread made the
        // peak narrower, which is not sampling noise at this size, and taking a
        // square root of it would report the real failure as a NaN out of range.
        Assert.True(
            withDepth > thermal && withWidth > thermal,
            $"adding a spatial spread did not widen the peak: thermal {thermal * 1e9:F2} ns, "
            + $"with depth {withDepth * 1e9:F2} ns, with width {withWidth * 1e9:F2} ns");

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

        // Independent contributions add in quadrature, and these are nearly but not
        // quite independent. The aperture selects on the joint distribution - which
        // ions survive depends on depth and width together - so the population that
        // arrives with all three spreads on is not the population either pair-wise
        // run measured. The residual is that coupling, and it is a property of
        // having a real aperture rather than an error.
        output.WriteLine(
            $"  quadrature is {(quadrature / everything) - 1.0:P1} of the measured total, and the "
            + "gap is the aperture coupling the two spatial spreads");

        Assert.InRange(quadrature / everything, 0.9, 1.1);
    }

    [Fact]
    public void TheArrivalSpreadGrowsWithDriftBecauseThereIsNoUsefulSpaceFocus()
    {
        // A single-stage extraction has a Wiley-McLaren space focus, where the ion
        // that started deeper catches the one in front. For a uniform field it sits
        // at twice the source depth, which would be about 6 mm here.
        //
        // It is not there. The spread grows monotonically over the whole practical
        // range, so the focus - if it is anywhere - is at essentially zero drift
        // and a detector at any usable distance is far past it. That is what a
        // field varying by a factor of two across the packet does to a first-order
        // condition derived for a uniform one, and it is the reason a real
        // instrument adds a second acceleration stage rather than moving the
        // detector.
        //
        // Run at two mesh densities, because changing the drift also changes the
        // solve domain and therefore the cell size: the y extent grows with the
        // drift while the interval count is rounded to a power of two, so a scan at
        // one mesh compares four different discretisations and a trend of the right
        // sign would be indistinguishable from a discretisation artefact. If the
        // slope survives refinement it is physics.
        var document = Template();

        output.WriteLine("drift / mm    20 cells/r0    40 cells/r0");

        var slopes = new List<double>();
        double[] coarse = [];

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            var points = new List<(double Drift, double Fwhm)>();

            foreach (var drift in new[] { 2.0, 5.0, 8.0, 11.0 })
            {
                var fwhm = ArrivalSpread(
                    With(document, ("driftLength", drift), ("cellsPerRadius", cells)), ions: 500);

                points.Add((drift, fwhm));
            }

            for (var k = 1; k < points.Count; k++)
            {
                Assert.True(
                    points[k].Fwhm > points[k - 1].Fwhm,
                    $"at {cells:F0} cells/r0 the peak narrowed from {points[k - 1].Fwhm * 1e9:F2} to "
                    + $"{points[k].Fwhm * 1e9:F2} ns between {points[k - 1].Drift:F1} and {points[k].Drift:F1} mm, "
                    + "so there is a focus after all");
            }

            slopes.Add(
                (points[^1].Fwhm - points[0].Fwhm) / (points[^1].Drift - points[0].Drift));

            if (cells > 20.0)
            {
                for (var k = 0; k < points.Count; k++)
                {
                    output.WriteLine($"{points[k].Drift,10:F1}   {coarse[k] * 1e9,12:F2}   {points[k].Fwhm * 1e9,12:F2}");
                }
            }
            else
            {
                coarse = [.. points.Select(pt => pt.Fwhm)];
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"slope {slopes[0] * 1e9:F1} ns/mm coarse, {slopes[1] * 1e9:F1} ns/mm fine");
        output.WriteLine("an ideal-field first-order focus would be at 2 x 3 mm = 6 mm");

        // Monotone at both meshes is asserted above. Here the quantity that gets
        // published has to survive refinement too, or the number is the mesh's.
        Assert.InRange(slopes[1] / slopes[0], 0.9, 1.1);
    }

    /// <summary>
    /// Arrival width with the spatial spreads set, in millimetres. The temperature
    /// stays as the template declares it, since it is the one contribution that is
    /// always present.
    /// </summary>
    private static double Fwhm(ModelDocument document, double depth, double width)
    {
        var configured = document with
        {
            Source = document.Source! with
            {
                Cloud = document.Source!.Cloud! with
                {
                    Ions = 1500,
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
        // Through Compile, so an override that puts a parameter out of bounds
        // reports the constraint it broke rather than a null reference three lines
        // later.
        var model = Compile(document);
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

    /// <summary>
    /// Multipole content on a circle, by discrete cosine transform, to the order
    /// asked for.
    /// </summary>
    /// <remarks>
    /// Odd orders included, unlike the round-rod version of this helper. A
    /// quadrupole of four identical rods is four-fold symmetric and its odd terms
    /// vanish identically, so measuring them there says nothing. This trap is not:
    /// the front plate is split by a slot and the back plate is solid, so it is
    /// mirror-symmetric in x and not in y, and that asymmetry radiates into the odd
    /// cosines. Projecting only the even ones would measure the wrong aberration
    /// and report it as the whole story.
    /// </remarks>
    private static double[] Multipoles(IElectrostaticField field, double radius, int highestOrder)
    {
        const int Samples = 1024;

        var cosine = new double[highestOrder + 1];
        var sine = new double[highestOrder + 1];

        for (var k = 0; k < Samples; k++)
        {
            var theta = 2.0 * Math.PI * k / Samples;
            var point = new Vec3(radius * Math.Cos(theta), radius * Math.Sin(theta), 0.0);
            var phi = field.PotentialAt(in point);

            for (var order = 0; order <= highestOrder; order++)
            {
                cosine[order] += phi * Math.Cos(order * theta);
                sine[order] += phi * Math.Sin(order * theta);
            }
        }

        var scale = 2.0 / Samples;
        var magnitude = new double[highestOrder + 1];

        for (var order = 0; order <= highestOrder; order++)
        {
            // Both phases, combined into one amplitude. Projecting onto the cosine
            // alone is blind to any asymmetry about the x axis - and the slot in
            // the front plate is exactly that, so a cosine-only projection would
            // report the odd orders as vanishing and the slot as costing nothing.
            magnitude[order] = Math.Sqrt(
                (cosine[order] * cosine[order]) + (sine[order] * sine[order])) * scale;
        }

        return magnitude;
    }

    /// <summary>
    /// The largest unwanted multipole as a fraction of the quadrupole term.
    /// </summary>
    /// <remarks>
    /// Every order but 2, rather than order 6 alone. What matters to an ion is the
    /// biggest departure from a pure quadrupole, and which order that is depends on
    /// the geometry - for round rods it is the 12-pole by symmetry, and for a trap
    /// with a slot in one plate it need not be.
    /// </remarks>
    private static (int Order, double Fraction) WorstMultipole(CompiledModel model, double fraction)
    {
        var field = FieldAssembly.Build(model);
        var r0 = model.Parameters["inscribedRadius"].In("m");
        var terms = Multipoles(field, fraction * r0, highestOrder: 10);

        var worst = (Order: 0, Fraction: 0.0);

        for (var order = 1; order <= 10; order++)
        {
            if (order == 2)
            {
                continue;
            }

            var share = terms[order] / terms[2];

            if (share > worst.Fraction)
            {
                worst = (order, share);
            }
        }

        return worst;
    }
}
