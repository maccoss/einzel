using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Ions in gas, checked against closed forms rather than against themselves.
/// </summary>
/// <remarks>
/// Three independent targets, none of which this code produced: the Langevin rate
/// coefficient, equipartition, and Mason-Schamp mobility. A collision model can be
/// plausible and wrong in ways that only show up as a slightly wrong damping rate,
/// which is exactly the kind of error a self-consistency check misses.
/// </remarks>
public sealed class CollisionTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;
    private const double ElementaryCharge = 1.602176634e-19;

    /// <summary>Nitrogen, the gas nearly every instrument here actually contains.</summary>
    private static BackgroundGas Nitrogen(double pressurePa, CollisionModel model) => new()
    {
        Model = model,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    [Fact]
    public void TheLangevinRateMatchesThePublishedCoefficient()
    {
        // k = q sqrt(pi a / (eps0 mu)). For a singly charged ion of m/z 500 in
        // nitrogen this is about 6e-10 cm^3/s, which is the range every published
        // Langevin rate coefficient sits in - they cluster around 1e-9 cm^3/s
        // because the only thing that varies is the reduced mass under a square
        // root.
        var gas = Nitrogen(1.0, CollisionModel.Langevin);
        var ionMass = 500.0 * Dalton;

        var rate = gas.LangevinRateSi(ionMass, ElementaryCharge);

        // The closed form again, spelled differently, so a transcription error in
        // one does not pass by matching itself.
        var reduced = ionMass * gas.MassSi / (ionMass + gas.MassSi);
        var expected = 2.0 * Math.PI * ElementaryCharge
            * Math.Sqrt(1.74e-30 / (4.0 * Math.PI * BackgroundGas.VacuumPermittivitySi * reduced));

        output.WriteLine($"k = {rate:E4} m^3/s = {rate * 1e6:E3} cm^3/s");
        output.WriteLine($"expected {expected:E4} m^3/s");

        Assert.Equal(expected, rate, 1e-12 * expected);
        Assert.InRange(rate * 1e6, 3e-10, 2e-9);
    }

    [Fact]
    public void TheLangevinRateDoesNotDependOnSpeed()
    {
        // Not a convenience: the capture cross section goes as 1/v and the rate is
        // the product, so the rate is a constant. It is why a Langevin collision is
        // a plain exponential draw while a hard-sphere one needs the null method,
        // and why mobility in the polarization limit is temperature-independent.
        var gas = Nitrogen(1e-3, CollisionModel.Langevin);
        var ionMass = 500.0 * Dalton;

        var slow = gas.CollisionRateSi(ionMass, ElementaryCharge, 10.0);
        var fast = gas.CollisionRateSi(ionMass, ElementaryCharge, 10_000.0);

        output.WriteLine($"at 10 m/s: {slow:E6} /s");
        output.WriteLine($"at 10 km/s: {fast:E6} /s");

        Assert.Equal(slow, fast, 1e-12 * slow);

        // A hard-sphere rate at the same pressure is not constant, which is the
        // control that makes the equality above mean something.
        var hard = Nitrogen(1e-3, CollisionModel.HardSphere);

        Assert.NotEqual(
            hard.CollisionRateSi(ionMass, ElementaryCharge, 10.0),
            hard.CollisionRateSi(ionMass, ElementaryCharge, 10_000.0),
            1e-6);
    }

    [Fact]
    public void TheCollisionRateMatchesTheScheduledRate()
    {
        // The scheduler has to reproduce the rate it was given end to end: the
        // exponential draw, the null-collision rejection, and the bound.
        //
        // Short flights and many ions, so that most ions collide once or not at all
        // and the measured rate is not contaminated by ions that have already
        // slowed down. A heavy ion for the same reason.
        var gas = Nitrogen(1e-2, CollisionModel.HardSphere);
        var species = IonSpecies.FromMassToCharge(5000.0, 1);

        var speed = 10_000.0;
        var flight = 1e-5;
        var ions = 20_000;

        // n sigma <g>, with <g> the Maxwell-averaged relative speed. Far above the
        // gas thermal speed that is v(1 + vm^2/2v^2) to better than a part in a
        // thousand, which is why the ion is launched fast: it makes the target a
        // closed form rather than an integral.
        var s2 = gas.ThermalSpeedSi / speed;
        var expectedRate = gas.NumberDensitySi * gas.CrossSectionSi * speed * (1.0 + (0.5 * s2 * s2));

        var total = 0;
        var nulls = 0;

        for (var i = 0; i < ions; i++)
        {
            var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 1000 + i);

            TrajectoryIntegrator.Integrate(
                new PhaseState(Vec3.Zero, new Vec3(speed, 0.0, 0.0)),
                species,
                FieldFreeSpace.Instance,
                new IntegrationSettings { MaximumFlightTime = flight, RelativeTolerance = 1e-9 },
                collisions: sampler);

            Assert.False(sampler.BoundExceeded, "the null-collision bound was exceeded");

            total += sampler.Collisions;
            nulls += sampler.NullEvents;
        }

        var measured = total / (double)ions / flight;

        output.WriteLine($"n sigma <g>   {expectedRate:F1} /s");
        output.WriteLine($"measured      {measured:F1} /s from {total} collisions over {ions} ions");
        output.WriteLine($"ratio         {measured / expectedRate:F4}");
        output.WriteLine($"null events   {nulls} ({nulls / (double)(nulls + total):P1} of scheduled)");

        // About 1% of statistical spread at this event count, so a 4% band is the
        // measurement rather than a shrug.
        Assert.InRange(measured / expectedRate, 0.96, 1.04);
    }

    [Fact]
    public void AnIonThermalisesToTheGasTemperature()
    {
        // The sharpest check available, because equipartition is exact and is not
        // something this code knows: an ion left in a gas long enough must arrive at
        // a mean kinetic energy of (3/2)kT whatever it started with. It tests the
        // scattering kinematics, the Maxwellian draw, and the isotropy of the
        // deflection all at once - get the centre-of-mass share wrong and the ion
        // settles at the wrong temperature.
        var gas = Nitrogen(1.0, CollisionModel.Langevin);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var target = 1.5 * BackgroundGas.BoltzmannSi * gas.TemperatureK;

        // Launched hot: 5 eV is some 200 times the thermal energy.
        var launchSpeed = Math.Sqrt(2.0 * 5.0 * ElementaryCharge / species.MassSi);

        // An ion loses about 2 m_i m_g / (m_i + m_g)^2 of its excess energy per
        // collision, which for m/z 500 in nitrogen is a tenth - so relaxation takes
        // some ten collisions per e-fold. At 1 Pa the Langevin rate is 1.4e5 /s, so
        // 2 ms is a few hundred collisions and thoroughly relaxed. An earlier
        // version of this test ran for 0.2 ms, reached 7.5 times the target, and
        // was measuring the relaxation rate rather than the endpoint.
        var ions = 400;
        var collisions = 0;
        var energy = 0.0;

        for (var i = 0; i < ions; i++)
        {
            var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 7000 + i);

            var result = TrajectoryIntegrator.Integrate(
                new PhaseState(Vec3.Zero, new Vec3(launchSpeed, 0.0, 0.0)),
                species,
                FieldFreeSpace.Instance,
                new IntegrationSettings { MaximumFlightTime = 2e-3, RelativeTolerance = 1e-8 },
                collisions: sampler);

            collisions += sampler.Collisions;
            energy += 0.5 * species.MassSi * result.FinalState.Velocity.LengthSquared;
        }

        var mean = energy / ions;

        output.WriteLine($"launched at        {0.5 * species.MassSi * launchSpeed * launchSpeed / ElementaryCharge:F3} eV");
        output.WriteLine($"settled at         {mean / ElementaryCharge * 1e3:F4} meV");
        output.WriteLine($"3/2 kT at {gas.TemperatureK:F0} K   {target / ElementaryCharge * 1e3:F4} meV");
        output.WriteLine($"ratio              {mean / target:F4}");
        output.WriteLine($"after              {collisions / (double)ions:F0} collisions per ion");

        // A single ion's kinetic energy has a spread of 82% of its own mean at
        // equilibrium, so 400 ions carry about 4% of statistical error.
        Assert.InRange(mean / target, 0.90, 1.10);
    }

    [Fact]
    public void TheDriftVelocityMatchesMasonSchamp()
    {
        // The literature regression. An ion in a uniform field in gas reaches a
        // steady drift velocity, and low-field mobility has a first-order
        // Chapman-Enskog closed form that this code does not use to move anything -
        // the ions get their drift by colliding. So agreement is a statement about
        // the scattering kinematics rather than about the estimate.
        var gas = Nitrogen(100.0, CollisionModel.HardSphere);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var expected = gas.LowFieldMobilitySi(species.MassSi, species.ChargeSi);

        // Low field: E/N well under 10 Td keeps the ion near thermal, which is where
        // the first-order form holds. Above it the ion heats and the closed form
        // needs an effective temperature.
        var townsend = 4.0;
        var fieldStrength = townsend * 1e-21 * gas.NumberDensitySi;

        var field = UniformField.Create(new Vec3(fieldStrength, 0.0, 0.0));

        // A drifting ion also diffuses, and over this flight the diffusion length is
        // comparable to the drift: a single ion's displacement carries some 45% of
        // spread about the mean. So the ensemble reports its own standard error and
        // the assertion is made against that, rather than against a band chosen to
        // fit. A first draft with 40 ions came out at 0.935 and looked like a 6.5%
        // discrepancy; it was one and a half standard errors.
        var ions = 150;
        var flight = 3e-4;

        var drifts = new double[ions];

        for (var i = 0; i < ions; i++)
        {
            var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 31_000 + i);

            var result = TrajectoryIntegrator.Integrate(
                new PhaseState(Vec3.Zero, Vec3.Zero),
                species,
                field,
                new IntegrationSettings { MaximumFlightTime = flight, RelativeTolerance = 1e-6 },
                collisions: sampler);

            drifts[i] = result.FinalState.Position.X / flight / fieldStrength;
        }

        var measured = drifts.Average();
        var variance = drifts.Sum(k => (k - measured) * (k - measured)) / (ions - 1.0);
        var standardError = Math.Sqrt(variance / ions);

        output.WriteLine($"E/N                {townsend:F1} Td, field {fieldStrength:F1} V/m");
        output.WriteLine($"Mason-Schamp K     {expected:E4} m^2/(V s)");
        output.WriteLine($"measured K         {measured:E4} +/- {standardError:E2} ({ions} ions)");
        output.WriteLine($"ratio              {measured / expected:F4} +/- {standardError / expected:F4}");
        output.WriteLine($"discrepancy        {Math.Abs(measured - expected) / standardError:F2} standard errors");

        // Consistent, at the precision the ensemble actually has. First-order
        // Chapman-Enskog is itself approximate - good to a per cent or so for a
        // rigid-sphere interaction at low field - so this is the sharper of the two
        // numbers being tested only because the ensemble is large enough.
        Assert.InRange(measured, expected - (3.0 * standardError), expected + (3.0 * standardError));
    }

    [Fact]
    public void ARunIsReproducibleFromItsSeed()
    {
        // PRJ-3: a manifest fully determines its run. A collisional flight is
        // random, so the seed has to carry it - and each ion draws from its own
        // stream, so adding an ion to an ensemble does not move any ion before it.
        var gas = Nitrogen(1e-2, CollisionModel.HardSphere);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        static TrajectoryResult Fly(BackgroundGas gas, IonSpecies species, int seed, out int collisions)
        {
            var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, seed);

            var result = TrajectoryIntegrator.Integrate(
                new PhaseState(Vec3.Zero, new Vec3(2000.0, 0.0, 0.0)),
                species,
                FieldFreeSpace.Instance,
                new IntegrationSettings { MaximumFlightTime = 5e-4, RelativeTolerance = 1e-9 },
                collisions: sampler);

            collisions = sampler.Collisions;
            return result;
        }

        var first = Fly(gas, species, 4242, out var a);
        var again = Fly(gas, species, 4242, out var b);
        var other = Fly(gas, species, 4243, out var c);

        output.WriteLine($"seed 4242: {a} collisions, ended at {first.FinalState.Position.X * 1e3:F6} mm");
        output.WriteLine($"seed 4242: {b} collisions, ended at {again.FinalState.Position.X * 1e3:F6} mm");
        output.WriteLine($"seed 4243: {c} collisions, ended at {other.FinalState.Position.X * 1e3:F6} mm");

        Assert.Equal(a, b);
        Assert.Equal(first.FinalState.Position.X, again.FinalState.Position.X);
        Assert.Equal(first.FinalState.Velocity.Y, again.FinalState.Velocity.Y);

        Assert.NotEqual(first.FinalState.Position.X, other.FinalState.Position.X);
    }

    [Fact]
    public void AVacuumFlightIsUnchangedToTheLastBit()
    {
        // The control that makes every number this engine already reports safe. A
        // collisional path added to the integrator must not perturb a collisionless
        // one, and "should not" is worth nothing next to an exact comparison.
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-4 };
        var launch = new PhaseState(Vec3.Zero, new Vec3(3000.0, 12.0, -7.0));

        var withoutSampler = TrajectoryIntegrator.Integrate(
            launch, species, FieldFreeSpace.Instance, settings);

        var withEmptyGas = TrajectoryIntegrator.Integrate(
            launch, species, FieldFreeSpace.Instance, settings,
            collisions: new CollisionSampler(BackgroundGas.Vacuum, species.MassSi, species.ChargeSi, 1));

        output.WriteLine($"no sampler   {withoutSampler.FlightTimeSeconds:E17} s");
        output.WriteLine($"vacuum gas   {withEmptyGas.FlightTimeSeconds:E17} s");

        Assert.Equal(withoutSampler.FlightTimeSeconds, withEmptyGas.FlightTimeSeconds);
        Assert.Equal(withoutSampler.FinalState.Position.X, withEmptyGas.FinalState.Position.X);
        Assert.Equal(withoutSampler.AcceptedSteps, withEmptyGas.AcceptedSteps);
    }
}
