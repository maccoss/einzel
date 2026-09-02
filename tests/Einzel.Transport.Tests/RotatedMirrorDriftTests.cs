using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The drift impulse a tilted mirror delivers, against its closed form.
/// </summary>
/// <remarks>
/// <para>
/// Three facts fix the answer with nothing left over. The speed is conserved, because the
/// ion returns to the potential it left. The component along n is conserved, because an
/// electrostatic structure invariant under translation along a unit vector n conserves
/// v.n, and a mirror rotated about y is invariant along <c>(sin a, 0, cos a)</c>. And the
/// component along the mirror normal reverses. Solving those three gives
/// <c>v_z = V sin(2a)</c> and <c>v_x = -V cos(2a)</c> after one reflection - the drift
/// impulse is <c>V sin(2a)</c> exactly. Nothing in that refers to the mirror's potentials,
/// electrode depths, apertures or internal structure.
/// </para>
/// <para>
/// The familiar <c>2 V tan(a)</c> is the small-angle form and is short by a factor of
/// <c>cos^2(a)</c>; the tests below found that, at 0.999013 against a predicted 0.999013.
/// For the Astral's own tilt the two differ by 8e-8.
/// </para>
/// <para>
/// This is the mechanism an asymmetric-track analyser turns its drift around with, and the
/// closed form is worth having as a test because it settles a question the literature
/// leaves open. The electrode geometry cannot be tuned to change the deceleration: it is
/// fixed by the tilt alone. A model that reports otherwise has a defect, and one did -
/// see the RotatedField remarks.
/// </para>
/// </remarks>
public sealed class RotatedMirrorDriftTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static (double Predicted, double Measured) Reflect(double halfTurns, double gradient)
    {
        var species = Peptide;
        var speed = species.SpeedAfterAcceleration(Quantity.From(4.0, "kV")).SiValue;

        var straight = HalfSpaceUniformField.Create(
            Vec3.Zero, Vec3.UnitX, Quantity.Si(gradient, Dimension.ElectricField));
        var field = new RotatedField(straight, halfTurns, 0.0, 0.0);

        var launch = new PhaseState(Vec3.Zero, new Vec3(speed, 0.0, 0.0));
        var settings = new IntegrationSettings { MaximumFlightTime = 1e-3, RelativeTolerance = 1e-12 };
        TrajectoryStopFunction detector = (in PhaseState s) => s.Position.X;

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);
        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        return (speed * Math.Sin(2.0 * double.Pi * halfTurns), result.FinalState.Velocity.Z);
    }

    /// <summary>One reflection changes the drift velocity by exactly V sin(2a).</summary>
    [Theory]
    [InlineData(9.0929e-5)]   // the Astral's own tilt: a 200 micron spacer over 350 mm
    [InlineData(1.0e-3)]
    [InlineData(1.0e-2)]
    public void OneReflectionDeliversVSineTwoAlpha(double halfTurns)
    {
        var (predicted, measured) = Reflect(halfTurns, 80000.0);
        var ratio = measured / predicted;

        output.WriteLine($"half turns {halfTurns}: predicted {predicted:F6} m/s, measured {measured:F6}, ratio {ratio:F9}");
        Assert.Equal(1.0, ratio, 1e-7);
    }

    /// <summary>
    /// And it does not depend on the mirror's field strength, which is what makes it an
    /// invariant rather than a fit.
    /// </summary>
    /// <remarks>
    /// The discriminating half. A model that merely got the magnitude right at one
    /// operating point would still fail here: changing the gradient eightfold moves the
    /// turning depth and the flight time by the same factor and must leave the impulse
    /// alone. It is also the reason fitting an analyser's electrode depths against its
    /// drift reversal is void as a plan - there is nothing there to fit.
    /// </remarks>
    [Fact]
    public void TheImpulseDoesNotDependOnTheMirrorDesign()
    {
        const double HalfTurns = 9.0929e-5;
        var soft = Reflect(HalfTurns, 20000.0);
        var hard = Reflect(HalfTurns, 160000.0);

        output.WriteLine($"gradient 20 kV/m: {soft.Measured:F6} m/s;  160 kV/m: {hard.Measured:F6} m/s");
        Assert.Equal(soft.Predicted, hard.Predicted, 1e-12);
        Assert.Equal(1.0, hard.Measured / soft.Measured, 1e-6);
    }
}
