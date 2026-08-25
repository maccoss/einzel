using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Emittance: the phase-space area a packet occupies, and what it takes to change it.
/// </summary>
/// <remarks>
/// <para>
/// Half of these are figure-of-merit tests and half are integrator tests, because
/// emittance is both. Liouville's theorem says a conservative force cannot change
/// phase-space area, so a drift and an ideal lens must leave it exactly where it
/// was. That is a conserved quantity independent of energy - energy conservation
/// is blind to a map that shears phase space, and this is not - so it checks an
/// axis of the integrator that ACC-4's energy drift does not reach.
/// </para>
/// <para>
/// The tests are built so that each one has an exact answer. Where a sampled cloud
/// is used the closed form is quoted and the tolerance is set by the sampling; where
/// the point is a conservation law the cloud is deterministic and every ion shares
/// the axial dynamics, so the map is exactly linear and the bar is machine
/// precision.
/// </para>
/// </remarks>
public sealed class EmittanceTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static Vec3 Axis => new(1.0, 0.0, 0.0);

    private static Vec3 Across => new(0.0, 1.0, 0.0);

    [Fact]
    public void UncorrelatedWidthsMultiply()
    {
        // The definitional case, on a deterministic lattice so the answer is
        // arithmetic rather than a sample. Three positions by three divergences,
        // uncorrelated: <y^2> = (2/3)s^2, <y'^2> = (2/3)d^2, <yy'> = 0, so the
        // emittance is exactly (2/3) s d.
        const double SizeM = 1.0e-3;
        const double DivergenceRad = 5.0e-3;
        const double AxialSpeed = 1.0e4;

        var states = new List<PhaseState>();

        foreach (var y in new[] { -SizeM, 0.0, SizeM })
        {
            foreach (var slope in new[] { -DivergenceRad, 0.0, DivergenceRad })
            {
                states.Add(new PhaseState(
                    new Vec3(0.0, y, 0.0), new Vec3(AxialSpeed, slope * AxialSpeed, 0.0)));
            }
        }

        var emittance = Emittance.FromPacket(states, Across, Axis);
        var exact = 2.0 / 3.0 * SizeM * DivergenceRad;

        output.WriteLine($"exact    {exact * 1e6:F6} mm.mrad");
        output.WriteLine($"measured {emittance.MillimetreMilliradian:F6} mm.mrad");

        Assert.Equal(exact, emittance.GeometricM, 15);

        // Uncorrelated means at a waist, and a waist is where alpha vanishes.
        Assert.Equal(0.0, emittance.TwissAlpha, 12);
    }

    [Fact]
    public void AThermalCloudMatchesItsClosedForm()
    {
        // A cloud of known spatial width at known temperature has an emittance
        // that can be written down: the transverse velocity spread of a
        // Maxwell-Boltzmann distribution is sqrt(kT/m) per component, the
        // divergence is that over the axial speed, and with position and velocity
        // drawn independently the two widths simply multiply.
        const double TemperatureK = 300.0;
        const double SpreadM = 5.0e-4;
        const int Ions = 6000;

        var species = Peptide;
        var axialSpeed = SpeedForEnergy(species, volts: 10.0);

        var cloud = IonCloud.Draw(
            new PhaseState(Vec3.Zero, new Vec3(axialSpeed, 0.0, 0.0)),
            species,
            new IonCloudSettings
            {
                Ions = Ions,
                Seed = 7,
                TemperatureK = TemperatureK,
                TransverseSpreadM = SpreadM,
            });

        var emittance = Emittance.FromPacket(cloud, Across, Axis);

        const double BoltzmannSi = 1.380649e-23;
        var thermalSpeed = Math.Sqrt(BoltzmannSi * TemperatureK / species.MassSi);
        var exact = SpreadM * thermalSpeed / axialSpeed;

        var error = Math.Abs(emittance.GeometricM - exact) / exact;

        output.WriteLine($"axial speed    {axialSpeed:F1} m/s at 10 V");
        output.WriteLine($"thermal speed  {thermalSpeed:F2} m/s at {TemperatureK:F0} K");
        output.WriteLine($"closed form    {exact * 1e6:F4} mm.mrad");
        output.WriteLine($"ensemble       {emittance.MillimetreMilliradian:F4} mm.mrad from {Ions} ions");
        output.WriteLine($"difference     {error:P2}");

        var (value, interval, _, _) = emittance.Geometric();
        output.WriteLine(
            $"as reported    {value.SiValue * 1e6:F4} +/- {interval.WidthSi * 0.5e6:F4} mm.mrad (68%)");

        // Six thousand ions place a second moment to about a per cent.
        Assert.True(error < 0.03, $"the ensemble emittance is off the closed form by {error:P2}");
    }

    [Fact]
    public void ADriftPreservesEmittanceExactly()
    {
        // Liouville, and the sharpest form of it available here. Over a field-free
        // drift to a plane a distance L away, every ion moves across by exactly
        // its own divergence times L - the axial speed cancels, because the ion
        // that crosses more slowly also has longer to do it. That is a shear of
        // unit determinant, so the area is unchanged to machine precision and any
        // departure is the integrator's.
        const double TemperatureK = 400.0;
        const double SpreadM = 3.0e-4;
        const int Ions = 800;
        const double DistanceM = 0.150;

        var species = Peptide;
        var axialSpeed = SpeedForEnergy(species, volts: 20.0);

        // No longitudinal spread on purpose. Ions starting at different axial
        // positions drift for different distances, which makes the shear
        // ion-dependent and mixes longitudinal spread into transverse emittance -
        // a real effect, but not the one under test.
        var cloud = IonCloud.Draw(
            new PhaseState(Vec3.Zero, new Vec3(axialSpeed, 0.0, 0.0)),
            species,
            new IonCloudSettings
            {
                Ions = Ions,
                Seed = 11,
                TemperatureK = TemperatureK,
                TransverseSpreadM = SpreadM,
            });

        var before = Emittance.FromPacket(cloud, Across, Axis);
        var after = Emittance.FromPacket(
            FlyToPlane(cloud, species, FieldFreeSpace.Instance, DistanceM), Across, Axis);

        var drift = Math.Abs(after.GeometricM - before.GeometricM) / before.GeometricM;

        output.WriteLine($"before  {before.MillimetreMilliradian:F6} mm.mrad, "
            + $"size {before.RmsSizeM * 1e3:F4} mm, alpha {before.TwissAlpha:F4}");
        output.WriteLine($"after   {after.MillimetreMilliradian:F6} mm.mrad, "
            + $"size {after.RmsSizeM * 1e3:F4} mm, alpha {after.TwissAlpha:F4}");
        output.WriteLine($"change  {drift:E3}");

        // The packet must have grown - a diverging beam over 150 mm - or the test
        // is passing because nothing happened.
        Assert.True(after.RmsSizeM > 2.0 * before.RmsSizeM, "the packet did not actually spread");

        // Diverging, so past its waist, so alpha is negative.
        Assert.True(after.TwissAlpha < 0.0, $"alpha should be negative past the waist, got {after.TwissAlpha}");

        Assert.True(drift < 1.0e-9, $"a field-free drift changed the emittance by {drift:E3}");
    }

    [Fact]
    public void AccelerationDampsTheGeometricEmittanceButNotTheNormalised()
    {
        // Adiabatic damping, and the reason the normalised emittance is the one
        // worth quoting. An axial field leaves transverse velocity alone while
        // raising the axial velocity, so every divergence angle shrinks by the
        // speed ratio and the packet looks better without anything having
        // improved. Multiplying by beta-gamma takes exactly that factor back out.
        //
        // The cloud is deterministic and every ion shares its axial dynamics, so
        // the map is exactly linear and the ratio is exactly the speed ratio.
        const double FieldVoltsPerMetre = 2.0e5;
        const double DistanceM = 0.050;

        var species = Peptide;
        var initialSpeed = SpeedForEnergy(species, volts: 10.0);

        var cloud = Lattice(initialSpeed, sizeM: 5.0e-4, divergenceRad: 2.0e-3);

        var before = Emittance.FromPacket(cloud, Across, Axis);

        var field = UniformField.Create(new Vec3(FieldVoltsPerMetre, 0.0, 0.0));
        var after = Emittance.FromPacket(
            FlyToPlane(cloud, species, field, DistanceM), Across, Axis);

        // Energy conservation gives the final speed with no reference to the run.
        var gained = species.ChargeSi * FieldVoltsPerMetre * DistanceM;
        var finalSpeed = Math.Sqrt((initialSpeed * initialSpeed) + (2.0 * gained / species.MassSi));

        var expectedRatio = initialSpeed / finalSpeed;
        var observedRatio = after.GeometricM / before.GeometricM;

        var normalisedChange =
            Math.Abs(after.NormalisedM - before.NormalisedM) / before.NormalisedM;

        // The naive normalisation, kept as a control. It is the geometric
        // emittance times beta-gamma, and it is *not* invariant: beta-gamma is
        // built from the total speed while a divergence is measured against the
        // axial one, and the paraxial term between them shrinks as the beam is
        // damped. It should miss by about half the mean squared divergence.
        var naiveChange =
            Math.Abs((after.GeometricM * after.BetaGamma) - (before.GeometricM * before.BetaGamma))
            / (before.GeometricM * before.BetaGamma);

        output.WriteLine($"speed        {initialSpeed:F1} -> {finalSpeed:F1} m/s");
        output.WriteLine($"geometric    {before.MillimetreMilliradian:F6} -> "
            + $"{after.MillimetreMilliradian:F6} mm.mrad");
        output.WriteLine($"ratio        {observedRatio:F9}, expected {expectedRatio:F9}");
        output.WriteLine($"normalised   changed by {normalisedChange:E3}");
        output.WriteLine($"  via beta-gamma instead: {naiveChange:E3}");

        Assert.True(
            Math.Abs(observedRatio - expectedRatio) / expectedRatio < 1.0e-8,
            $"geometric emittance fell by {observedRatio:F9}, expected {expectedRatio:F9}");

        Assert.True(
            normalisedChange < 1.0e-13,
            $"the normalised emittance changed by {normalisedChange:E3} across an accelerating stage");

        // Half the mean squared divergence, which for this lattice is 2 mrad
        // scaled by the lattice's own mean square. Asserted rather than remarked
        // on, so that if the momentum form is ever quietly replaced by the
        // beta-gamma one the difference is a failure and not a rounding change.
        var paraxial = 0.5 * before.RmsDivergenceRad * before.RmsDivergenceRad;

        Assert.True(
            naiveChange > 0.3 * paraxial,
            $"the beta-gamma normalisation should miss by about {paraxial:E3}, missed by {naiveChange:E3}");
    }

    [Fact]
    public void ALinearLensLeavesEmittanceAloneAndAberrationDoesNot()
    {
        // The property that makes emittance the right figure of merit rather than
        // just another width: it is blind to anything optics can undo and
        // sensitive to everything they cannot. A thin lens whose focal power is
        // the same for every ion is a linear kick, so the area survives. Give the
        // same lens a cubic term - spherical aberration, which every real lens has
        // - and the area grows, permanently, because no downstream element can
        // take it back out.
        //
        // Applied as an impulse rather than integrated through a field, so that
        // what is measured is the metric and not the solver.
        const double FocalLengthM = 0.100;
        const double AxialSpeed = 1.0e4;

        var cloud = Lattice(AxialSpeed, sizeM: 1.0e-3, divergenceRad: 1.0e-3);
        var before = Emittance.FromPacket(cloud, Across, Axis);

        var linear = Kick(cloud, y => -y / FocalLengthM);

        // A cubic term sized to matter at the edge of the packet and to be
        // negligible at its centre, which is what spherical aberration is.
        var aberrated = Kick(cloud, y => (-y / FocalLengthM) - (4.0e6 * y * y * y));

        var linearEmittance = Emittance.FromPacket(linear, Across, Axis);
        var aberratedEmittance = Emittance.FromPacket(aberrated, Across, Axis);

        var linearChange = Math.Abs(linearEmittance.GeometricM - before.GeometricM) / before.GeometricM;
        var growth = aberratedEmittance.GeometricM / before.GeometricM;

        output.WriteLine($"before     {before.MillimetreMilliradian:F6} mm.mrad");
        output.WriteLine($"ideal lens {linearEmittance.MillimetreMilliradian:F6} mm.mrad "
            + $"(changed by {linearChange:E3})");
        output.WriteLine($"aberrated  {aberratedEmittance.MillimetreMilliradian:F6} mm.mrad "
            + $"({growth:F3}x)");

        // Both lenses converge the packet, so both should read alpha positive.
        output.WriteLine($"alpha      {linearEmittance.TwissAlpha:F4} after the ideal lens");
        Assert.True(
            linearEmittance.TwissAlpha > 0.0,
            $"a converging lens should give a positive alpha, got {linearEmittance.TwissAlpha}");

        Assert.True(linearChange < 1.0e-12, $"an ideal thin lens changed the emittance by {linearChange:E3}");
        Assert.True(growth > 1.05, $"spherical aberration grew the emittance by only {growth:F4}x");
    }

    [Fact]
    public void AnEmittanceNeedsIonsGoingSomewhere()
    {
        // The divergence is a ratio, and an ion at rest has no axial speed to
        // divide by. Excluding it rather than dividing is a choice, so it is
        // asserted rather than left to be discovered.
        var stalled = new[]
        {
            new PhaseState(Vec3.Zero, Vec3.Zero),
            new PhaseState(new Vec3(0.0, 1.0e-3, 0.0), Vec3.Zero),
        };

        Assert.Throws<ArgumentException>(() => Emittance.FromPacket(stalled, Across, Axis));
    }

    /// <summary>A deterministic packet: every ion shares the axial speed.</summary>
    private static PhaseState[] Lattice(double axialSpeed, double sizeM, double divergenceRad)
    {
        var states = new List<PhaseState>();

        for (var i = -4; i <= 4; i++)
        {
            for (var j = -4; j <= 4; j++)
            {
                var y = sizeM * i / 4.0;
                var slope = divergenceRad * j / 4.0;

                states.Add(new PhaseState(
                    new Vec3(0.0, y, 0.0), new Vec3(axialSpeed, slope * axialSpeed, 0.0)));
            }
        }

        return [.. states];
    }

    /// <summary>Applies an impulse that changes transverse velocity by position.</summary>
    private static PhaseState[] Kick(PhaseState[] cloud, Func<double, double> slopeChange)
    {
        var kicked = new PhaseState[cloud.Length];

        for (var k = 0; k < cloud.Length; k++)
        {
            var state = cloud[k];
            var change = slopeChange(state.Position.Y) * state.Velocity.X;

            kicked[k] = state with
            {
                Velocity = new Vec3(state.Velocity.X, state.Velocity.Y + change, state.Velocity.Z),
            };
        }

        return kicked;
    }

    private static PhaseState[] FlyToPlane(
        PhaseState[] cloud, IonSpecies species, IElectrostaticField field, double distance)
    {
        var settings = new IntegrationSettings { RelativeTolerance = 1e-12, MaximumFlightTime = 1e-2 };
        TrajectoryStopFunction plane = (in PhaseState s) => distance - s.Position.X;

        var arrived = new List<PhaseState>(cloud.Length);

        foreach (var start in cloud)
        {
            var result = TrajectoryIntegrator.Integrate(start, species, field, settings, plane);

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrived.Add(result.FinalState);
            }
        }

        Assert.Equal(cloud.Length, arrived.Count);
        return [.. arrived];
    }

    private static double SpeedForEnergy(IonSpecies species, double volts) =>
        Math.Sqrt(2.0 * Math.Abs(species.ChargeSi) * volts / species.MassSi);
}
