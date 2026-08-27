using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;

namespace Einzel.Transport.Tests;

/// <summary>
/// Ions pushing on each other: the direct sum, and the packet integrator that
/// makes it possible to apply.
/// </summary>
/// <remarks>
/// <para>
/// SC-1 asks for an approximate space-charge method validated against direct
/// summation on a reference population. This is the direct summation, built first
/// because an approximation cannot be validated against something that does not
/// exist. What is checked here is therefore not "does it agree with the fast
/// method" but "is it right at all", against closed forms and conservation laws.
/// </para>
/// <para>
/// Three kinds of check, deliberately. A conservation law that must hold to
/// round-off and fails loudly on a sign or an index error. A closed form the sum
/// must reproduce. And a control with the interaction switched off, so the
/// integrator is shown to be an integrator before it is asked to be a new physics
/// model — without that one, every number below could be wrong in the same
/// direction and all three would still agree.
/// </para>
/// </remarks>
public sealed class SpaceChargeInteractionTests
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static IdealSingleStageReflectron Reflectron() =>
        IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

    /// <summary>A ball of macroparticles, uniformly filled, from a fixed seed.</summary>
    private static Vec3[] Ball(int count, double radius, int seed)
    {
        var random = new Random(seed);
        var points = new Vec3[count];

        for (var k = 0; k < count; k++)
        {
            // Rejection sampling: the cube-root trick gets the radial density right
            // but this is shorter and the count is small.
            while (true)
            {
                var x = (2.0 * random.NextDouble()) - 1.0;
                var y = (2.0 * random.NextDouble()) - 1.0;
                var z = (2.0 * random.NextDouble()) - 1.0;

                if ((x * x) + (y * y) + (z * z) <= 1.0)
                {
                    points[k] = new Vec3(x * radius, y * radius, z * radius);
                    break;
                }
            }
        }

        return points;
    }

    [Fact]
    public void InternalForcesDoNotMoveTheCentreOfMass()
    {
        // The cheapest exact statement there is about a pairwise sum. Newton's
        // third law is built in - each pair is visited once and the equal and
        // opposite accelerations applied together - so the total is zero by
        // construction, and any sign or index error breaks it immediately.
        var positions = Ball(64, 0.5e-3, seed: 7);
        var active = new bool[positions.Length];

        Array.Fill(active, true);

        var interaction = new CoulombInteraction(
            population: 10_000, macroparticles: positions.Length,
            chargeSi: Peptide.ChargeSi, massSi: Peptide.MassSi,
            softeningLengthSi: CoulombInteraction.SpacingSoftening(0.5e-3, positions.Length));

        var acceleration = new Vec3[positions.Length];

        interaction.Accumulate(positions, active, acceleration);

        var total = default(Vec3);
        var magnitude = 0.0;

        foreach (var one in acceleration)
        {
            total += one;
            magnitude += one.Length;
        }

        Assert.True(magnitude > 0.0, "nothing pushed on anything, so this proves nothing");

        // Relative to the scale of the individual accelerations, not to zero: a
        // sum of 64 numbers of size 1e12 cannot cancel to better than round-off
        // times that size, and asserting against an absolute zero would be
        // asserting that floating-point addition is exact.
        Assert.True(
            total.Length / magnitude < 1e-14,
            $"the accelerations summed to {total.Length:E3} against a scale of {magnitude:E3}");
    }

    [Fact]
    public void TheSumReproducesTheUniformSpherePotentialTheScreenAssumes()
    {
        // Two independent routes to one number, which is the kind of check this
        // engine trusts most. The screening estimate models a packet as a
        // uniformly charged sphere and uses the closed form for its centre-to-
        // surface potential; the direct sum knows nothing about spheres and adds
        // up point charges. They have to agree.
        const int Macroparticles = 4000;
        const double Radius = 0.5e-3;
        const double Population = 10_000;

        var positions = Ball(Macroparticles, Radius, seed: 11);
        var active = new bool[Macroparticles];

        Array.Fill(active, true);

        // Softening well below the mean spacing, because here it is a source of
        // error rather than a modelling choice: the closed form has none.
        var interaction = new CoulombInteraction(
            Population, Macroparticles, Peptide.ChargeSi, Peptide.MassSi,
            softeningLengthSi: 1e-9);

        var centre = interaction.PotentialAt(Vec3.Zero, positions, active);
        var surface = interaction.PotentialAt(new Vec3(Radius, 0.0, 0.0), positions, active);

        var charge = Population * Peptide.ChargeSi;

        // Centre 3Q/(8 pi eps0 R), surface Q/(4 pi eps0 R): the difference is
        // Q/(8 pi eps0 R), which is what the screening estimate calls the packet's
        // self-potential.
        var expected = charge / (8.0 * Math.PI * SpaceCharge.PermittivitySi * Radius);
        var measured = centre - surface;

        // Four thousand points sampling a continuum: the disagreement is sampling
        // noise going as one over the square root of the count, not a systematic
        // error, and three per cent at this count is what that predicts.
        Assert.Equal(expected, measured, expected * 0.05);

        // And the screen, driven through its own interface. It takes a cloud's
        // Gaussian spreads and derives an effective radius from them, so the
        // spreads are chosen to make that radius the ball's: a uniform sphere of
        // radius R has an rms radius of sqrt(3/5) R, and the screen inverts that.
        var estimate = SpaceCharge.Estimate(
            EquivalentCloud((int)Population, Radius), Peptide, accelerationPotentialVolts: 4000.0);

        Assert.Equal(Radius, estimate.EffectiveRadiusM, Radius * 1e-12);
        Assert.Equal(estimate.PotentialVolts, measured, estimate.PotentialVolts * 0.05);
    }

    [Fact]
    public void TwoIonsReleasedFromRestReachTheSpeedEnergyConservationSaysTheyShould()
    {
        // A closed form with no sampling in it at all. Two charges at rest a
        // distance d apart carry k q^2 / d of potential energy; at large
        // separation that is all kinetic, split evenly.
        const double Separation = 1e-6;

        var species = Peptide;

        var interaction = new CoulombInteraction(
            population: 2, macroparticles: 2, chargeSi: species.ChargeSi, massSi: species.MassSi,
            softeningLengthSi: 1e-12);

        var launch = new[]
        {
            new PhaseState(new Vec3(-0.5 * Separation, 0.0, 0.0), Vec3.Zero),
            new PhaseState(new Vec3(0.5 * Separation, 0.0, 0.0), Vec3.Zero),
        };

        // Stop them a thousand separations apart, where the remaining potential
        // energy is a thousandth of what it started with.
        const double Far = 500.0 * Separation;

        TrajectoryStopFunction stop = (in PhaseState state) => Far - Math.Abs(state.Position.X);

        var result = PacketIntegrator.Fly(
            launch, species, FieldFreeSpace.Instance, interaction,
            new IntegrationSettings { MaximumFlightTime = 1e-3, RelativeTolerance = 1e-12 },
            stop);

        Assert.All(
            result.Members,
            m => Assert.Equal(TrajectoryOutcome.StopConditionMet, m.Outcome));

        var speed = result.Members[0].FinalState.Velocity.Length;

        var released = CoulombInteraction.CoulombConstantSi * species.ChargeSi * species.ChargeSi
            * ((1.0 / Separation) - (1.0 / (2.0 * Far)));

        var expected = Math.Sqrt(released / species.MassSi);

        Assert.Equal(expected, speed, expected * 1e-6);

        // And they left in opposite directions at the same speed, which is the
        // momentum statement again in a form a reader can check by eye.
        Assert.Equal(speed, result.Members[1].FinalState.Velocity.Length, speed * 1e-12);
        Assert.True(result.MaximumInteractionImbalance < 1e-12);
    }

    [Fact]
    public void WithNoInteractionThePacketIntegratorAgreesWithTheSingleIonPath()
    {
        // The control, and the most important test here. A new integrator that
        // gets space charge wrong in the same direction as its own validation
        // would pass everything above; this one asks whether it can integrate at
        // all, against the path that carries every validated number in the engine.
        var reflectron = Reflectron();

        var single = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        var packet = PacketIntegrator.Fly(
            [reflectron.LaunchState()], reflectron.Species, reflectron.Field,
            interaction: null, reflectron.Settings(), reflectron.DetectorPlane());

        var member = Assert.Single(packet.Members);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, member.Outcome);

        var exact = reflectron.ExactFlightTime();

        // Against the closed form rather than against the other integrator, so a
        // shared error would have to be a coincidence rather than an inheritance.
        // Looser than the single-ion path's 1e-10 on purpose: this one has no
        // analytic drift and lands on the stopping surface by linear interpolation
        // within the step rather than by a bracketed root-find, which is the price
        // of a shared step and is stated rather than hidden.
        Assert.Equal(exact, member.FlightTimeSeconds, exact * 1e-6);
        Assert.Equal(single.FlightTimeSeconds, member.FlightTimeSeconds, exact * 1e-6);

        // No interaction, so nothing to conserve and nothing measured.
        Assert.Equal(0.0, packet.MaximumInteractionImbalance);
    }

    [Fact]
    public void SpaceChargeWidensAPacketInFreeFlightAndTheScreenBoundsIt()
    {
        // The unambiguous sign test. In free flight the leading ions are pushed
        // further ahead and the trailing ones further behind, so the arrival spread
        // can only grow. Anything else is a sign error, and this is the geometry
        // where nothing else can be blamed.
        const double Radius = 0.5e-3;
        const int Macroparticles = 120;
        const double Population = 40_000;
        const double Drift = 0.2;

        var species = Peptide;
        var speed = Math.Sqrt(2.0 * species.ChargeSi * 4000.0 / species.MassSi);

        var launch = Launch(Ball(Macroparticles, Radius, seed: 3), new Vec3(speed, 0.0, 0.0));

        TrajectoryStopFunction detector = (in PhaseState state) => Drift - state.Position.X;

        var settings = new IntegrationSettings
        {
            MaximumFlightTime = 10.0 * Drift / speed,
            RelativeTolerance = 1e-10,
        };

        var free = PacketIntegrator.Fly(
            launch, species, FieldFreeSpace.Instance, interaction: null, settings, detector);

        var pushed = PacketIntegrator.Fly(
            launch, species, FieldFreeSpace.Instance, Interaction(Population, Macroparticles, Radius),
            settings, detector);

        Assert.True(pushed.MaximumInteractionImbalance < 1e-9, "the mutual force did not balance");

        var spreadFree = Spread(free);
        var spreadPushed = Spread(pushed);

        Assert.True(
            spreadPushed > spreadFree,
            $"space charge narrowed a free-flying packet: {spreadPushed:E3} s against {spreadFree:E3} s");

        var estimate = SpaceCharge.Estimate(
            EquivalentCloud((int)Population, Radius), species, accelerationPotentialVolts: 4000.0);

        var predicted = estimate.TimingFraction!.Value * (Drift / speed);
        var measured = spreadPushed - spreadFree;

        // The screen converts the whole self-potential into a free-flight timing
        // error, which is what this flight is - so here it should be close rather
        // than merely bounding. Within a factor of three either way: the screen
        // models a uniform sphere that stays a sphere, and a real one expands as it
        // flies, so its own field falls during the drift.
        Assert.InRange(measured, predicted / 3.0, 3.0 * predicted);
    }

    [Fact]
    public void AReflectronPartlyUndoesTheSpreadSpaceChargeAdds()
    {
        // The result that looked like a sign error and is not. In a mirror at
        // first-order energy focus, space charge makes the packet's arrival spread
        // *smaller*, and the mechanism is the one the mirror is built on.
        //
        // The push correlates position with energy: the ion at the front of the
        // packet is accelerated along its direction of travel and the one at the
        // back is retarded. A leading, faster ion penetrates the mirror deeper and
        // spends longer in it - which is exactly the compensation a reflectron
        // exists to provide, applied to a spread that happens to have been created
        // by the packet's own charge.
        //
        // A plausible story is not a finding, so it is tested rather than told:
        // detune the drift length away from the focusing condition and the
        // compensation has to weaken. That is the control that distinguishes this
        // from an integrator with a sign wrong, which would narrow the packet at
        // every drift length.
        const double Radius = 0.5e-3;
        const int Macroparticles = 120;
        const double Population = 40_000;

        var focused = Ratio(IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm")),
            Radius, Macroparticles, Population);

        var detuned = Ratio(IdealSingleStageReflectron.Detuned(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"), driftInDepths: 8.0),
            Radius, Macroparticles, Population);

        Assert.True(
            focused < 1.0,
            $"at first-order focus the packet was not narrowed: ratio {focused:F3}");

        Assert.True(
            detuned > focused,
            $"detuning the mirror did not weaken the compensation: {detuned:F3} against {focused:F3} at "
            + "focus, so the narrowing is not the focusing condition doing it");
    }

    /// <summary>Arrival spread with the mutual force on, over the spread without it.</summary>
    private static double Ratio(
        IdealSingleStageReflectron reflectron, double radius, int macroparticles, double population)
    {
        var nominal = reflectron.LaunchState();
        var launch = Launch(Ball(macroparticles, radius, seed: 3), nominal.Velocity, nominal.Position);

        var settings = reflectron.Settings() with { RelativeTolerance = 1e-9 };

        var free = PacketIntegrator.Fly(
            launch, reflectron.Species, reflectron.Field, interaction: null,
            settings, reflectron.DetectorPlane());

        var pushed = PacketIntegrator.Fly(
            launch, reflectron.Species, reflectron.Field,
            Interaction(population, macroparticles, radius), settings, reflectron.DetectorPlane());

        Assert.True(pushed.MaximumInteractionImbalance < 1e-9, "the mutual force did not balance");

        return Spread(pushed) / Spread(free);
    }

    private static CoulombInteraction Interaction(double population, int macroparticles, double radius) =>
        new(
            population, macroparticles, Peptide.ChargeSi, Peptide.MassSi,
            CoulombInteraction.SpacingSoftening(radius, macroparticles));

    private static PhaseState[] Launch(Vec3[] offsets, Vec3 velocity, Vec3 origin = default)
    {
        var launch = new PhaseState[offsets.Length];

        for (var k = 0; k < offsets.Length; k++)
        {
            launch[k] = new PhaseState(origin + offsets[k], velocity);
        }

        return launch;
    }

    /// <summary>
    /// A cloud whose screening radius is exactly a stated uniform-sphere radius.
    /// </summary>
    /// <remarks>
    /// The screen takes sqrt(5/3) times the rms of sqrt(2 sigma_t^2 + sigma_l^2).
    /// With all three spreads equal that is sqrt(5) sigma, so sigma = R / sqrt(5).
    /// Derived rather than tuned, and asserted at the call site, because a test
    /// that compares two radii which are not the same radius compares nothing.
    /// </remarks>
    private static Core.Model.IonCloudSettings EquivalentCloud(int population, double radius) =>
        new()
        {
            Ions = population,
            Population = population,
            TransverseSpreadM = radius / Math.Sqrt(5.0),
            LongitudinalSpreadM = radius / Math.Sqrt(5.0),
        };

    private static double Spread(PacketResult result)
    {
        var arrived = result.Members
            .Where(m => m.Outcome == TrajectoryOutcome.StopConditionMet)
            .Select(m => m.FlightTimeSeconds)
            .ToArray();

        Assert.True(arrived.Length > 10, $"only {arrived.Length} macroparticles arrived");

        return arrived.Max() - arrived.Min();
    }
}
