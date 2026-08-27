using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Einzel.Transport.Diffusion;

namespace Einzel.Transport.Tests;

/// <summary>
/// The RF a slow ion in a gas actually feels: the cycle-averaged effective field.
/// </summary>
/// <remarks>
/// <para>
/// The reason this exists is that between about 1e-2 and 10 mbar neither transport
/// mode could describe a driven structure. Trajectory integration is outside its
/// validity there, and the diffusive mode steps a density through one static field
/// - which a driven structure does not have. That band is where ion funnels,
/// travelling-wave guides and collision cells actually run.
/// </para>
/// <para>
/// The checks here are closed forms, because the ideal quadrupole has one: its RF
/// field is exactly linear in position, so the effective potential is exactly
/// harmonic and its curvature is the textbook secular frequency. Nothing about that
/// number comes from this code.
/// </para>
/// </remarks>
public sealed class PonderomotiveFieldTests
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private const double AmplitudeVolts = 300.0;
    private const double FrequencyHz = 1.0e6;
    private const double InscribedRadiusM = 4.0e-3;

    private static IdealQuadrupoleRf Quadrupole() =>
        IdealQuadrupoleRf.Create(
            Quantity.From(0.0, "V"),
            Quantity.From(AmplitudeVolts, "V"),
            Quantity.From(FrequencyHz, "Hz"),
            Quantity.From(InscribedRadiusM, "m"));

    /// <summary>Peak of the oscillating field at a point, asked of the field itself.</summary>
    /// <remarks>
    /// Rather than restating the quadrupole's own convention. A first version of
    /// these tests wrote the amplitude as V r / r0^2 and every closed form came out
    /// four times too small, because this field's potential is V (x^2 - y^2) / r0^2
    /// and its gradient therefore carries a factor of two. Sampling removes a whole
    /// class of that mistake: what is under test is the relation between the field
    /// and the well, not who spells the field which way.
    /// </remarks>
    private static double PeakField(IdealQuadrupoleRf field, Vec3 at)
    {
        var period = 1.0 / FrequencyHz;
        var peak = 0.0;

        for (var s = 0; s < 720; s++)
        {
            peak = Math.Max(peak, field.ElectricFieldAt(in at, period * s / 720.0).Length);
        }

        return peak;
    }

    [Fact]
    public void TheCollisionlessWellIsTheDehmeltPseudopotential()
    {
        // An ideal quadrupole's RF field is E = (V/r0^2)(x, -y, 0) cos(Omega t), so
        // its amplitude squared is (V/r0^2)^2 r^2 and the effective potential is
        // exactly harmonic:
        //
        //   Psi = q^2 V^2 r^2 / (4 m Omega^2 r0^4)
        //
        // Checked as a potential in volts, which is Psi/q.
        var species = Peptide;

        var field = new PonderomotiveField(
            Quadrupole(), species.ChargeSi, species.MassSi, collisionRateSi: 0.0);

        Assert.Equal(1.0, field.Suppression, 1e-15);

        var omega = 2.0 * Math.PI * FrequencyHz;
        var quadrupole = Quadrupole();

        foreach (var r in new[] { 0.2e-3, 0.5e-3, 1.0e-3 })
        {
            var on = new Vec3(r, 0.0, 0.0);
            var peak = PeakField(quadrupole, on);

            var expected = species.ChargeSi * peak * peak
                / (4.0 * species.MassSi * omega * omega);

            // On the x axis and on the y axis: the well is the same either way,
            // which is the whole point of a pseudopotential and is not true of the
            // instantaneous field at any phase.
            Assert.Equal(expected, field.PotentialAt(on), expected * 1e-6);
            Assert.Equal(expected, field.PotentialAt(new Vec3(0.0, r, 0.0)), expected * 1e-6);
        }

        // And it is a well, not a hill: the ponderomotive force points towards
        // weaker field from every direction.
        //
        // Finite as well as negative. An analytic field reports a resolution of
        // positive infinity - meaning it has no resolution limit, not that it has an
        // enormous one - and reading that as a differencing step gave infinity minus
        // infinity and a field of NaN, while every potential above stayed correct.
        var at = new Vec3(0.5e-3, 0.0, 0.0);
        var inward = field.ElectricFieldAt(at);

        Assert.True(
            double.IsFinite(inward.X) && inward.X < 0.0,
            $"the effective field at 0.5 mm was {inward.X:E6} V/m, which is not an inward push");
    }

    [Fact]
    public void ItsCurvatureIsTheTextbookSecularFrequency()
    {
        // omega_secular = q Omega / sqrt(8), the standard low-q result, written in
        // the Mathieu parameter rather than in volts - because q is what every
        // published quadrupole result is quoted in, and it is the one spelling of
        // this geometry that nothing in this test gets to choose.
        //
        // Recovered from the well by matching Psi to (1/2) m omega^2 r^2, so it
        // tests the coefficient rather than restating it.
        var species = Peptide;

        const double MathieuQ = 0.2;

        var quadrupole = IdealQuadrupoleRf.FromMathieu(
            a: 0.0,
            q: MathieuQ,
            Quantity.Si(species.MassSi, Quantity.From(1.0, "kg").Dimension),
            Quantity.Si(species.ChargeSi, Quantity.From(1.0, "C").Dimension),
            Quantity.From(FrequencyHz, "Hz"),
            Quantity.From(InscribedRadiusM, "m"));

        var field = new PonderomotiveField(
            quadrupole, species.ChargeSi, species.MassSi, collisionRateSi: 0.0);

        const double R = 0.5e-3;

        var energy = species.ChargeSi * field.PotentialAt(new Vec3(R, 0.0, 0.0));
        var measured = Math.Sqrt(2.0 * energy / species.MassSi) / R;

        var omega = 2.0 * Math.PI * FrequencyHz;
        var expected = MathieuQ * omega / Math.Sqrt(8.0);

        Assert.Equal(expected, measured, expected * 1e-6);
    }

    [Fact]
    public void CollisionsWeakenTheWellByExactlyTheDampingFactor()
    {
        // The part that is not the textbook formula, and the reason the effective
        // potential could not simply be added as a one-line term. A damped quiver
        // is smaller, so the round trip through the field gradient leaves less net
        // force: the well goes as Omega^2/(Omega^2 + nu^2).
        //
        // At nu = Omega that is exactly one half, which is a value no fitting could
        // produce by accident.
        var species = Peptide;
        var omega = 2.0 * Math.PI * FrequencyHz;

        var free = new PonderomotiveField(
            Quadrupole(), species.ChargeSi, species.MassSi, collisionRateSi: 0.0);

        var damped = new PonderomotiveField(
            Quadrupole(), species.ChargeSi, species.MassSi, collisionRateSi: omega);

        Assert.Equal(0.5, damped.Suppression, 1e-15);

        var at = new Vec3(0.5e-3, 0.0, 0.0);

        Assert.Equal(0.5 * free.PotentialAt(at), damped.PotentialAt(at), 1e-15);

        // And ten times the drive frequency all but removes it, which is the regime
        // an ion funnel runs in and the finding the whole class exists to make
        // visible.
        var heavy = new PonderomotiveField(
            Quadrupole(), species.ChargeSi, species.MassSi, collisionRateSi: 10.0 * omega);

        Assert.Equal(1.0 / 101.0, heavy.Suppression, 1e-12);
    }

    [Fact]
    public void TheDampingRateComesFromTheMobilityRatherThanTheCollisionCount()
    {
        // nu = q/(m mu), which is the momentum-transfer rate. Deriving it from the
        // number of collisions instead would over-damp by roughly the ion-to-neutral
        // mass ratio, because a heavy ion in a light gas gives up only that fraction
        // of its momentum per collision - and it would also be a second estimate of
        // a quantity the drift term already fixes, free to disagree with it.
        var species = Peptide;

        // A mobility typical of nitrogen at a few mbar.
        const double MobilitySi = 0.05;

        var rate = PonderomotiveField.CollisionRateFromMobility(
            species.ChargeSi, species.MassSi, MobilitySi);

        // Drude, stated the other way round: a steady field must give a drift of
        // mu E, and it does only if nu is this.
        var drift = species.ChargeSi * 1000.0 / (species.MassSi * rate);

        Assert.Equal(MobilitySi * 1000.0, drift, MobilitySi * 1000.0 * 1e-12);

        // An infinite mobility is a collisionless ion, and gives the Dehmelt well
        // rather than a division by zero.
        Assert.Equal(0.0, PonderomotiveField.CollisionRateFromMobility(
            species.ChargeSi, species.MassSi, 0.0));
    }

    [Fact]
    public void TheQuiverAmplitudeIsWhatSaysTheAverageMeansAnything()
    {
        // The effective potential averages over an excursion and only describes
        // something if the field is roughly linear across it. The amplitude is
        // q E0 / (m Omega sqrt(Omega^2 + nu^2)), and it has to fall when collisions
        // damp the quiver - the same factor that weakens the well.
        var species = Peptide;
        var omega = 2.0 * Math.PI * FrequencyHz;

        var free = new PonderomotiveField(
            Quadrupole(), species.ChargeSi, species.MassSi, collisionRateSi: 0.0);

        var at = new Vec3(1.0e-3, 0.0, 0.0);

        var expected = species.ChargeSi * PeakField(Quadrupole(), at)
            / (species.MassSi * omega * omega);

        Assert.Equal(expected, free.QuiverAmplitude(at), expected * 1e-6);

        var damped = new PonderomotiveField(
            Quadrupole(), species.ChargeSi, species.MassSi, collisionRateSi: omega);

        // sqrt(Omega^2 + nu^2) is sqrt(2) Omega at nu = Omega.
        Assert.Equal(expected / Math.Sqrt(2.0), damped.QuiverAmplitude(at), expected * 1e-6);
    }
}
