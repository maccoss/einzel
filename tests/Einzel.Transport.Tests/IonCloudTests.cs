using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Clouds of ions rather than single ones, against the closed forms that describe
/// them.
/// </summary>
/// <remarks>
/// <para>
/// The accuracy class the specification calls Class S - transmission, acceptance,
/// efficiency, converged to a stated confidence interval - and the thing that
/// removes the caveat every resolving power here carries. A single ion launched
/// down the axis measures how much energy spread alone smears a peak. An
/// instrument is smeared by where the ions were, which way they were going, and
/// how fast, and only a cloud carries those.
/// </para>
/// <para>
/// Turn-around time is the sharpest test available because it has an exact answer
/// that owes nothing to this code: an ion moving away from the detector when the
/// extraction field arrives is stopped and brought back, arriving 2mv/qE later
/// than one that was moving toward it, and over a thermal distribution that makes
/// a Gaussian of width sqrt(mkT)/qE.
/// </para>
/// </remarks>
public sealed class IonCloudTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    [Fact]
    public void TurnAroundTimeMatchesItsClosedForm()
    {
        // A uniform extraction field, ions starting at rest apart from their
        // thermal motion, a detector 40 mm downstream. Everything about the
        // arrival-time spread is imposed before the ion leaves, so the flight
        // afterwards should not enter the answer - which is what makes this
        // checkable against a formula with no flight in it.
        const double FieldVoltsPerMetre = 1.0e6;
        const double TemperatureK = 300.0;
        const int Ions = 4000;

        var species = Peptide;
        var field = UniformField.Create(new Vec3(FieldVoltsPerMetre, 0.0, 0.0));

        var nominal = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0));

        var cloud = IonCloud.Draw(
            in nominal,
            species,
            new IonCloudSettings { Ions = Ions, Seed = 1, TemperatureK = TemperatureK });

        var arrivals = Fly(cloud, species, field, 0.040);

        var peak = ArrivalTimePeak.FromArrivals(arrivals, Ions);
        var observed = peak.GaussianEquivalentFwhmSeconds;

        var expected = IonCloud.TurnAroundFwhm(
            species,
            Quantity.Si(TemperatureK, Quantity.From(1.0, "K").Dimension),
            Quantity.Si(FieldVoltsPerMetre, Quantity.From(1.0, "V/m").Dimension));

        var exact = expected.In("s");
        var error = Math.Abs(observed - exact) / exact;

        output.WriteLine($"closed form  {exact * 1e9:F4} ns");
        output.WriteLine($"ensemble     {observed * 1e9:F4} ns from {peak.Arrived} of {Ions} ions");
        output.WriteLine($"difference   {error:P2}");

        // The published Ion Processor figure is 0.8 to 1.2 ns across m/z 195 to
        // 2722; this geometry and ion sit inside that, which is the point of
        // choosing them.
        output.WriteLine($"(the Ion Processor paper reports 0.8-1.2 ns for a comparable extraction)");

        Assert.Equal(Ions, peak.Arrived);

        // Four thousand ions estimate a width to about a per cent, so three is the
        // honest bar. Tightening it would be asserting that the sampler got lucky.
        Assert.True(error < 0.03, $"the ensemble width is off the closed form by {error:P2}");
    }

    [Theory]
    [InlineData(195.0)]
    [InlineData(500.0)]
    [InlineData(2722.0)]
    public void TurnAroundScalesAsTheSquareRootOfMass(double massToCharge)
    {
        // The scaling is the interesting part, and it is why a trap's turn-around
        // is quoted as a range across m/z rather than as one number: the width
        // goes as sqrt(m), so the heaviest ion in a spectrum sets the limit.
        const double FieldVoltsPerMetre = 1.0e6;
        const double TemperatureK = 300.0;
        const int Ions = 3000;

        var species = IonSpecies.FromMassToCharge(massToCharge, 1);
        var field = UniformField.Create(new Vec3(FieldVoltsPerMetre, 0.0, 0.0));
        var nominal = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0));

        var cloud = IonCloud.Draw(
            in nominal, species, new IonCloudSettings { Ions = Ions, Seed = 2, TemperatureK = TemperatureK });

        var peak = ArrivalTimePeak.FromArrivals(Fly(cloud, species, field, 0.040), Ions);

        var exact = IonCloud.TurnAroundFwhm(
            species,
            Quantity.Si(TemperatureK, Quantity.From(1.0, "K").Dimension),
            Quantity.Si(FieldVoltsPerMetre, Quantity.From(1.0, "V/m").Dimension)).In("s");

        var observed = peak.GaussianEquivalentFwhmSeconds;

        output.WriteLine(
            $"m/z {massToCharge,6:F0}: closed form {exact * 1e9:F3} ns, ensemble {observed * 1e9:F3} ns");

        Assert.True(
            Math.Abs(observed - exact) / exact < 0.04,
            $"m/z {massToCharge}: {observed * 1e9:F3} ns against {exact * 1e9:F3} ns");
    }

    [Fact]
    public void AColdCloudOnTheAxisIsStillASingleIon()
    {
        // The degenerate case has to stay degenerate. A cloud with no temperature,
        // no spatial spread, and no energy spread is one ion repeated, and if it
        // is not then every existing single-ion result silently changes the moment
        // a count is added to a model.
        var species = Peptide;
        var nominal = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(1000.0, 0.0, 0.0));

        var cloud = IonCloud.Draw(in nominal, species, new IonCloudSettings { Ions = 16, Seed = 3 });

        foreach (var state in cloud)
        {
            Assert.Equal(nominal.Position.X, state.Position.X, 1e-15);
            Assert.Equal(nominal.Position.Y, state.Position.Y, 1e-15);
            Assert.Equal(nominal.Position.Z, state.Position.Z, 1e-15);
            Assert.Equal(nominal.Velocity.X, state.Velocity.X, 1e-12);
            Assert.Equal(nominal.Velocity.Y, state.Velocity.Y, 1e-12);
            Assert.Equal(nominal.Velocity.Z, state.Velocity.Z, 1e-12);
        }
    }

    [Fact]
    public void TheSameSeedDrawsTheSameCloud()
    {
        // Spec section 8 requires run-to-run reproducibility on one machine, and a
        // statistical result that cannot be compared against itself is not one.
        var species = Peptide;
        var nominal = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(1000.0, 0.0, 0.0));

        var settings = new IonCloudSettings
        {
            Ions = 64,
            Seed = 11,
            TemperatureK = 400.0,
            TransverseSpreadM = 5e-4,
            EnergyFractionSpread = 0.01,
        };

        var first = IonCloud.Draw(in nominal, species, settings);
        var again = IonCloud.Draw(in nominal, species, settings);
        var different = IonCloud.Draw(in nominal, species, settings with { Seed = 12 });

        for (var k = 0; k < first.Length; k++)
        {
            Assert.Equal(first[k].Velocity.X, again[k].Velocity.X);
            Assert.Equal(first[k].Position.Y, again[k].Position.Y);
        }

        Assert.NotEqual(first[0].Velocity.X, different[0].Velocity.X);
    }

    [Fact]
    public void AThermalCloudHasTheWidthItsTemperatureImplies()
    {
        // Straight from the sampler, before any flight: each velocity component of
        // a Maxwell-Boltzmann distribution is Gaussian of width sqrt(kT/m). If
        // this is wrong then everything downstream is wrong by the same factor and
        // still looks self-consistent.
        const double TemperatureK = 300.0;
        const int Ions = 20000;

        var species = Peptide;
        var nominal = new PhaseState(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0));

        var cloud = IonCloud.Draw(
            in nominal, species, new IonCloudSettings { Ions = Ions, Seed = 5, TemperatureK = TemperatureK });

        var expected = Math.Sqrt(IonCloud.BoltzmannSi * TemperatureK / species.MassSi);

        foreach (var (name, component) in new (string, Func<PhaseState, double>)[]
        {
            ("x", s => s.Velocity.X),
            ("y", s => s.Velocity.Y),
            ("z", s => s.Velocity.Z),
        })
        {
            var values = cloud.Select(component).ToArray();
            var mean = values.Average();
            var sigma = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1));

            output.WriteLine($"{name}: sigma {sigma:F3} m/s against {expected:F3}, mean {mean:F3}");

            Assert.True(
                Math.Abs(sigma - expected) / expected < 0.03,
                $"{name} component width {sigma:F3} m/s against an expected {expected:F3}");

            // Isotropic and centred: a mean drifting away from zero would be a
            // directed velocity masquerading as a temperature.
            Assert.True(Math.Abs(mean) < 0.1 * expected, $"{name} component mean is {mean:F3} m/s");
        }
    }

    private static double[] Fly(PhaseState[] cloud, IonSpecies species, IElectrostaticField field, double distance)
    {
        var settings = new IntegrationSettings { RelativeTolerance = 1e-10, MaximumFlightTime = 1e-3 };
        // Positive while flying, negative once past: the integrator stops on the
        // sign change, and writing it the other way round means it never fires and
        // every ion times out instead.
        TrajectoryStopFunction detector = (in PhaseState s) => distance - s.Position.X;

        var arrivals = new List<double>(cloud.Length);

        foreach (var start in cloud)
        {
            var result = TrajectoryIntegrator.Integrate(start, species, field, settings, detector);

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(result.FlightTimeSeconds);
            }
        }

        return [.. arrivals];
    }
}

/// <summary>
/// What ensemble size the accuracy budget actually demands.
/// </summary>
/// <remarks>
/// Spec ACC-5 asks for a Class S transmission interval within one per cent
/// absolute at 95%, and says it "drives minimum ensemble size per point". That
/// sentence has been unactionable because nothing launched an ensemble. It is
/// arithmetic once something does.
/// </remarks>
public sealed class EnsembleSizeTests(ITestOutputHelper output)
{
    /// <summary>The 95% coverage factor for a normal approximation.</summary>
    private const double NinetyFive = 1.959964;

    [Fact]
    public void AccFiveDrivesTheEnsembleSize()
    {
        // The binomial standard error is sqrt(p(1-p)/n), widest at p = 0.5, so the
        // worst case sets the floor and everything else is cheaper.
        output.WriteLine("transmission   ions for a +/-1% interval at 95%");

        var worst = 0;

        foreach (var p in new[] { 0.5, 0.7, 0.9, 0.95, 0.99 })
        {
            var needed = (int)Math.Ceiling(p * (1.0 - p) * NinetyFive * NinetyFive / (0.01 * 0.01));
            worst = Math.Max(worst, needed);

            output.WriteLine($"{p,12:P0}   {needed,8}");
        }

        output.WriteLine($"worst case: {worst} ions");

        // Roughly ten thousand at the worst point, which is the number worth
        // knowing: it is what a transmission-versus-parameter scan costs per
        // point, and it is why such a scan is a study rather than a run.
        Assert.InRange(worst, 9000, 10000);
    }

    [Fact]
    public void TheReportedIntervalIsTheBinomialOne()
    {
        // Checking the implementation against the formula rather than against
        // itself. A transmission interval that does not narrow as the square root
        // of the ensemble is not a statistical interval.
        var narrow = Transmission(10000, 9000);
        var wide = Transmission(100, 90);

        output.WriteLine($"90% of 100 ions:   +/- {wide:P2}");
        output.WriteLine($"90% of 10000 ions: +/- {narrow:P2}");

        var expected = Math.Sqrt(10000.0 / 100.0);
        var observed = wide / narrow;

        output.WriteLine($"ratio {observed:F3} against the expected sqrt(100) = {expected:F3}");

        Assert.Equal(expected, observed, 0.05);

        // And ACC-5 is met at ten thousand and missed at a hundred, which is the
        // practical statement of the same thing.
        Assert.True(narrow * NinetyFive <= 0.01, "ten thousand ions should meet ACC-5");
        Assert.False(wide * NinetyFive <= 0.01, "a hundred ions should not");
    }

    private static double Transmission(int launched, int arrived)
    {
        var arrivals = Enumerable.Range(0, arrived).Select(k => 1e-5 + (k * 1e-12)).ToArray();
        var peak = ArrivalTimePeak.FromArrivals(arrivals, launched);
        var (_, uncertainty, _, _) = peak.Transmission();

        return (uncertainty.UpperSi - uncertainty.LowerSi) / 2.0;
    }
}

/// <summary>
/// The screening estimate for space charge, which the engine does not model.
/// </summary>
/// <remarks>
/// Every ion here flies through a field that does not know about the other ions.
/// For a sparse beam that is exactly right; for a dense packet it is wrong, and
/// wrong invisibly, because the answer looks the same either way. These check the
/// number that makes it visible.
/// </remarks>
public sealed class SpaceChargeTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static IonCloudSettings Packet(int population, double transverseMm) => new()
    {
        Ions = 100,
        Population = population,
        TransverseSpreadM = transverseMm * 1e-3,
    };

    [Fact]
    public void TheEstimateScalesWithChargeAndInverselyWithSize()
    {
        // Nq/(8 pi eps0 R): linear in the population, inverse in the radius. If
        // either scaling is wrong the threshold is wrong everywhere and the
        // estimate still looks plausible at the one point it was checked.
        var baseline = SpaceCharge.Estimate(Packet(1000, 0.3), Peptide, 4000.0);
        var denser = SpaceCharge.Estimate(Packet(4000, 0.3), Peptide, 4000.0);
        var larger = SpaceCharge.Estimate(Packet(1000, 0.6), Peptide, 4000.0);

        output.WriteLine($"1000 ions, 0.3 mm: {baseline.PotentialVolts * 1e3:F3} mV");
        output.WriteLine($"4000 ions, 0.3 mm: {denser.PotentialVolts * 1e3:F3} mV");
        output.WriteLine($"1000 ions, 0.6 mm: {larger.PotentialVolts * 1e3:F3} mV");

        Assert.Equal(4.0, denser.PotentialVolts / baseline.PotentialVolts, 1e-9);
        Assert.Equal(0.5, larger.PotentialVolts / baseline.PotentialVolts, 1e-9);

        // Time goes as the inverse square root of energy, so a fractional energy
        // spread is half of it in flight time. A factor of two here is the whole
        // difference between meeting the budget and missing it.
        Assert.Equal(0.5 * baseline.EnergyFraction, baseline.TimingFraction, 1e-15);
    }

    [Fact]
    public void ThePopulationLimitInvertsTheEstimate()
    {
        // The more useful direction to be told. "Over budget" invites "by how much
        // can I load it", and an answer that anticipates its own follow-up is
        // worth more than one that does not.
        var species = Peptide;
        const double Volts = 4000.0;
        var radius = Quantity.From(0.5, "mm");

        var limit = SpaceCharge.PopulationLimit(radius, species, Volts, 1e-6);
        output.WriteLine($"0.5 mm at {Volts:F0} V holds {limit:N0} ions within 1 ppm");

        // Loading exactly the limit should land on the budget.
        var at = SpaceCharge.Estimate(
            new IonCloudSettings
            {
                Ions = 1,
                Population = (int)Math.Round(limit),
                // The estimate maps Gaussian widths onto a uniform sphere, so the
                // width that produces this radius is what the packet must declare.
                TransverseSpreadM = radius.SiValue / Math.Sqrt(5.0 / 3.0) / Math.Sqrt(2.0),
            },
            species,
            Volts);

        output.WriteLine($"loading {at.Population:N0} gives {at.TimingFraction / 1e-6:F3} ppm");

        Assert.Equal(1e-6, at.TimingFraction, 1e-9);
    }

    [Fact]
    public void SamplingHarderIsNotLoadingHeavier()
    {
        // The distinction the whole fix exists for. Ten thousand samples of a
        // single-ion experiment is a better statistic; ten thousand ions in a
        // bunch is a different experiment. Conflating them makes a dense packet
        // silently sparse.
        var sampled = SpaceCharge.Estimate(
            new IonCloudSettings { Ions = 10000, Population = 1, TransverseSpreadM = 3e-4 },
            Peptide,
            4000.0);

        var loaded = SpaceCharge.Estimate(
            new IonCloudSettings { Ions = 10000, TransverseSpreadM = 3e-4 },
            Peptide,
            4000.0);

        output.WriteLine($"10000 samples of one ion: {sampled.TimingFraction / 1e-6:F3} ppm");
        output.WriteLine($"a bunch of 10000 ions:    {loaded.TimingFraction / 1e-6:F3} ppm");

        Assert.Equal(0.0, sampled.TimingFraction);
        Assert.True(loaded.TimingFraction > 1e-6, "a bunch of ten thousand should be over budget");

        // And the default is the conservative reading: unstated means the ions
        // simulated are the ions present.
        Assert.Equal(10000, loaded.Population);
    }

    [Fact]
    public void APacketWithNoExtentIsUnbounded()
    {
        // Not a small error, an infinite one, and easy to write by declaring a
        // population without any spread.
        var estimate = SpaceCharge.Estimate(
            new IonCloudSettings { Ions = 500 }, Peptide, 4000.0);

        Assert.True(estimate.IsPointLike);

        // A single ion at a point is fine: it has nothing to push against.
        var one = SpaceCharge.Estimate(new IonCloudSettings { Ions = 1 }, Peptide, 4000.0);
        Assert.False(one.IsPointLike);
    }

    [Fact]
    public void TheThresholdMatchesTheHandCalculation()
    {
        // Worked by hand before any of this was written, which is the point of
        // writing it down: 0.5 mm at 4 kV holds a few thousand ions, and that is a
        // number someone would type without thinking.
        var limit = SpaceCharge.PopulationLimit(Quantity.From(0.5, "mm"), Peptide, 4000.0, 1e-6);

        output.WriteLine($"limit at 0.5 mm, 4 kV: {limit:N0} ions");

        Assert.InRange(limit, 5000, 6000);
    }
}
