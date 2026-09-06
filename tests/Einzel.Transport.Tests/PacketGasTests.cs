using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The packet integrator flies through a gas: each macroparticle collides on its own
/// schedule and the shared step lands on whichever collision comes next. Checked the way
/// the single-ion path was - against equipartition, which the code does not know - and
/// then with the mutual force on, where the gas has to damp the expansion the packet's own
/// charge drives.
/// </summary>
public sealed class PacketGasTests(ITestOutputHelper output)
{
    private const double ElementaryCharge = 1.602176634e-19;

    private static BackgroundGas Nitrogen(double pressurePa) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * 1.66053906660e-27,
        CrossSectionSi = 250e-20,
        PolarizabilitySi = 1.74e-30,
    };

    private static TrajectoryStopFunction Never => (in PhaseState _) => 1.0;

    /// <summary>
    /// A packet launched hot in a gas relaxes to (3/2)kT, exactly as a single ion does: the
    /// samplers are the same, the kinematics are the same, only the stepping is shared.
    /// </summary>
    [Fact]
    public void APacketThermalisesToTheGasTemperature()
    {
        var gas = Nitrogen(1.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var target = 1.5 * BackgroundGas.BoltzmannSi * gas.TemperatureK;
        var launchSpeed = Math.Sqrt(2.0 * 5.0 * ElementaryCharge / species.MassSi);

        const int Ions = 200;
        var launch = new PhaseState[Ions];
        var samplers = new CollisionSampler[Ions];
        for (var i = 0; i < Ions; i++)
        {
            // Spread out so the members are distinct macroparticles; no mutual force here.
            launch[i] = new PhaseState(new Vec3(0.0, i * 1e-3, 0.0), new Vec3(launchSpeed, 0.0, 0.0));
            samplers[i] = new CollisionSampler(gas, species.MassSi, species.ChargeSi, 9000 + i);
        }

        var result = PacketIntegrator.Fly(
            launch, species, FieldFreeSpace.Instance, interaction: null,
            new IntegrationSettings { MaximumFlightTime = 2e-3, RelativeTolerance = 1e-8, MaximumSteps = 5_000_000 },
            Never, samplers);

        var energy = result.Members.Sum(m => 0.5 * species.MassSi * m.FinalState.Velocity.LengthSquared) / Ions;
        var collisions = samplers.Sum(s => s.Collisions);

        output.WriteLine($"settled at {energy / ElementaryCharge * 1e3:F4} meV against 3/2 kT = {target / ElementaryCharge * 1e3:F4} meV, ratio {energy / target:F4}");
        output.WriteLine($"{collisions / (double)Ions:F0} collisions per macroparticle over {result.Steps} shared steps; the integrator counted {result.Collisions}");

        Assert.Equal(collisions, result.Collisions);
        Assert.True(collisions > 100 * Ions, "two milliseconds at a pascal is a few hundred collisions per ion");
        // A single ion's kinetic energy has a spread of 82% of its mean at equilibrium; 200
        // members carry about 6% of statistical error.
        Assert.InRange(energy / target, 0.85, 1.15);
    }

    /// <summary>A sampler count that does not match the packet is refused rather than partly applied.</summary>
    [Fact]
    public void ASamplerPerMacroparticleIsRequired()
    {
        var gas = Nitrogen(1.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var launch = new[] { new PhaseState(Vec3.Zero, Vec3.Zero), new PhaseState(new Vec3(1e-3, 0.0, 0.0), Vec3.Zero) };
        var one = new[] { new CollisionSampler(gas, species.MassSi, species.ChargeSi, 1) };

        Assert.Throws<ArgumentException>(() => PacketIntegrator.Fly(
            launch, species, FieldFreeSpace.Instance, null, new IntegrationSettings { MaximumFlightTime = 1e-6 }, Never, one));
    }

    /// <summary>
    /// The two together: a dense packet released from rest blows itself apart, and a gas
    /// damps the expansion. The control is the same packet with the same push in vacuum,
    /// because "smaller than it started" would also be true of a packet that never moved.
    /// </summary>
    [Fact]
    public void AGasDampsTheExpansionThePacketsOwnChargeDrives()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        const int Macro = 60;
        const double Population = 6.0e4;
        const double Radius = 0.2e-3;

        var random = new Random(41);
        var launch = new PhaseState[Macro];
        for (var i = 0; i < Macro; i++)
        {
            // Uniform in a ball of the given radius, at rest.
            Vec3 p;
            do
            {
                p = new Vec3(
                    ((2.0 * random.NextDouble()) - 1.0) * Radius,
                    ((2.0 * random.NextDouble()) - 1.0) * Radius,
                    ((2.0 * random.NextDouble()) - 1.0) * Radius);
            }
            while (p.LengthSquared > Radius * Radius);

            launch[i] = new PhaseState(p, Vec3.Zero);
        }

        var settings = new IntegrationSettings { MaximumFlightTime = 100e-6, RelativeTolerance = 1e-8, MaximumSteps = 5_000_000 };
        CoulombInteraction Push() => new(
            Population, Macro, species.ChargeSi, species.MassSi,
            CoulombInteraction.SpacingSoftening(Radius, Macro));

        var inVacuum = PacketIntegrator.Fly(launch, species, FieldFreeSpace.Instance, Push(), settings, Never);

        // Ten pascals of nitrogen: a Langevin collision every few microseconds, so the
        // hundred microseconds is tens of collisions per macroparticle.
        var gas = Nitrogen(10.0);
        var samplers = Enumerable.Range(0, Macro)
            .Select(i => new CollisionSampler(gas, species.MassSi, species.ChargeSi, 500 + i))
            .ToList();
        var inGas = PacketIntegrator.Fly(launch, species, FieldFreeSpace.Instance, Push(), settings, Never, samplers);

        var start = RmsRadius(launch);
        var vacuum = RmsRadius(inVacuum.Members.Select(m => m.FinalState).ToArray());
        var damped = RmsRadius(inGas.Members.Select(m => m.FinalState).ToArray());

        output.WriteLine($"RMS radius: launched {start * 1e3:F3} mm; after 100 us in vacuum {vacuum * 1e3:F3} mm, in 10 Pa of nitrogen {damped * 1e3:F3} mm");
        output.WriteLine($"{inGas.Collisions / (double)Macro:F1} collisions per macroparticle; vacuum {inVacuum.Steps} steps, gas {inGas.Steps}");

        Assert.True(inGas.Collisions > 10 * Macro, "the gas must have acted");
        Assert.True(vacuum > 3.0 * start, "the push alone must blow the packet up, or the control proves nothing");
        Assert.True(damped < 0.7 * vacuum, $"the gas must damp the expansion: {damped} against {vacuum} in vacuum");
        Assert.True(damped > start, "and not hold the packet where it was, which is what a switched-off push would look like");
    }

    private static double RmsRadius(PhaseState[] cloud)
    {
        var centre = default(Vec3);
        foreach (var one in cloud)
        {
            centre += one.Position;
        }

        centre *= 1.0 / cloud.Length;
        var sum = 0.0;
        foreach (var one in cloud)
        {
            sum += (one.Position - centre).LengthSquared;
        }

        return Math.Sqrt(sum / cloud.Length);
    }
}
