using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Einzel.Transport.Interaction;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// In a field that is linear in position - an ideal quadrupole, RF or DC - the centre of mass
/// of a packet does not feel the mutual force at all: the pair forces cancel by the third
/// law, and the applied force on the centre of mass is the applied force at the centre of
/// mass. So a dipole excitation, which drives the centre of mass, sees the same resonance
/// however many ions are in the trap. This is the generalised Kohn theorem, and it is why a
/// resonance-ejection scan of a cooled cloud in a near-harmonic trap shows almost no
/// space-charge shift: the shift has to come from the anharmonic part of the field and
/// from the ejecting ion's view of the cloud once it has left it.
/// </summary>
public sealed class PacketCentreOfMassTests(ITestOutputHelper output)
{
    private static TrajectoryStopFunction Never => (in PhaseState _) => 1.0;

    /// <summary>
    /// A dense packet pushed hard in an ideal RF quadrupole: every macroparticle's path is
    /// changed by the push, and their centre of mass is not.
    /// </summary>
    [Fact]
    public void TheCentreOfMassIgnoresTheMutualForceInAHarmonicTrap()
    {
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, 0.5, species.Mass(), species.Charge(), Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"));

        const int Macro = 24;
        const double Population = 2.0e5;
        const double Sigma = 0.2e-3;
        var offset = new Vec3(0.5e-3, 0.3e-3, 0.0);

        var random = new Random(17);
        var launch = new PhaseState[Macro];
        for (var i = 0; i < Macro; i++)
        {
            launch[i] = new PhaseState(
                offset + new Vec3(Gaussian(random) * Sigma, Gaussian(random) * Sigma, Gaussian(random) * Sigma),
                Vec3.Zero);
        }

        var settings = new IntegrationSettings { MaximumFlightTime = 30e-6, RelativeTolerance = 1e-10, MaximumSteps = 2_000_000 };
        var push = new CoulombInteraction(
            Population, Macro, species.ChargeSi, species.MassSi,
            CoulombInteraction.SpacingSoftening(Sigma, Sigma, Sigma, Macro));

        var free = PacketIntegrator.Fly(launch, species, field, interaction: null, settings, Never);
        var pushed = PacketIntegrator.Fly(launch, species, field, push, settings, Never);

        Assert.All(free.Members, m => Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, m.Outcome));
        Assert.All(pushed.Members, m => Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, m.Outcome));

        var centreFree = Centre(free.Members);
        var centrePushed = Centre(pushed.Members);
        var centreShift = (centrePushed - centreFree).Length;

        var individual = 0.0;
        for (var i = 0; i < Macro; i++)
        {
            individual = Math.Max(individual, (pushed.Members[i].FinalState.Position - free.Members[i].FinalState.Position).Length);
        }

        var excursion = Math.Max(centreFree.Length, offset.Length);

        output.WriteLine($"centre of mass after 30 us: free {centreFree.X * 1e3:F6}, {centreFree.Y * 1e3:F6} mm; pushed {centrePushed.X * 1e3:F6}, {centrePushed.Y * 1e3:F6} mm");
        output.WriteLine($"centre-of-mass shift {centreShift * 1e6:E3} um against a largest individual shift of {individual * 1e6:F3} um; third-law imbalance {pushed.MaximumInteractionImbalance:E2}");
        output.WriteLine($"steps: free {free.Steps}, pushed {pushed.Steps}");

        Assert.True(individual > 50e-6, $"the push must move the ions, or the centre staying put proves nothing: {individual} m");
        Assert.True(centreShift < 1e-6 * excursion, $"centre of mass moved {centreShift} m by the mutual force in a linear field");
        Assert.True(pushed.MaximumInteractionImbalance < 1e-9);
    }

    private static Vec3 Centre(IReadOnlyList<PacketMember> members)
    {
        var sum = default(Vec3);
        foreach (var member in members)
        {
            sum += member.FinalState.Position;
        }

        return sum * (1.0 / members.Count);
    }

    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
