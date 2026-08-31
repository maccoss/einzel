using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Transport;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A beam that fills a cone: direction without energy, which a temperature cannot give.
/// </summary>
/// <remarks>
/// <para>
/// The model format already carries <c>energyFractionSpread</c> with the argument that it
/// "varies the energy without varying the direction, which a temperature cannot express".
/// This is the mirror of it, and it was missing: an aperture varies the direction without
/// varying the energy, and a temperature cannot express that either, because a thermal
/// draw moves speed and direction together in a fixed ratio.
/// </para>
/// <para>
/// <b>Why the omission held for so long, and why it stopped holding.</b> The stated reason
/// was that a thermal cloud already has a divergence and offering both would let a document
/// say two things about the same physics. That is right for a <em>source</em> — an ion born
/// warm and then accelerated — and wrong for a beam defined downstream by an aperture,
/// which is the case an einzel lens exists to re-image. The two are separable only if
/// divergence is declarable on its own.
/// </para>
/// </remarks>
public sealed class DivergenceTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>A beam along x at a given speed.</summary>
    private static PhaseState Beam(double speed) =>
        new(Vec3.Zero, new Vec3(speed, 0.0, 0.0));

    /// <summary>The polar angle of each drawn velocity, in degrees.</summary>
    private static double[] Angles(IReadOnlyList<PhaseState> states) =>
        [.. states.Select(s => Math.Atan2(
            Math.Sqrt((s.Velocity.Y * s.Velocity.Y) + (s.Velocity.Z * s.Velocity.Z)),
            s.Velocity.X) * 180.0 / Math.PI)];

    /// <summary>A tilt costs no energy: every ion keeps the speed it was given.</summary>
    /// <remarks>
    /// <b>Exact, not close.</b> Rotating a vector does not change its length, so a cone of
    /// directions at a fixed energy is an identity rather than an approximation — and the
    /// whole reason to have this beside a temperature is that a thermal draw cannot do it.
    /// A version that added a transverse velocity instead of tilting would raise the energy
    /// by 1/cos^2, which at 20 degrees is 13% and would read as a plausible beam.
    /// </remarks>
    [Fact]
    public void ATiltCostsNoEnergy()
    {
        const double Speed = 4000.0;

        var states = IonCloud.Draw(
            Beam(Speed),
            Peptide,
            new IonCloudSettings
            {
                Ions = 2000,
                DivergenceRadians = 20.0 * Math.PI / 180.0,
                Seed = 7,
            });

        var worst = states.Max(s => Math.Abs(s.Velocity.Length - Speed));

        output.WriteLine($"worst departure from {Speed} m/s: {worst:E3} m/s");

        Assert.True(worst < 1e-9, $"a drawn speed is {worst:E3} m/s off the nominal");
    }

    /// <summary>Nothing lies outside the declared half-angle.</summary>
    /// <remarks>
    /// The property that makes this a cone rather than a width. An aperture truncates —
    /// there is a largest angle that gets through and nothing beyond it — so a Gaussian
    /// would put a tail outside the acceptance the number names. Checked on the maximum
    /// over a large draw, because a tail is exactly what a mean would hide.
    /// </remarks>
    [Theory]
    [InlineData(5.0)]
    [InlineData(20.0)]
    [InlineData(45.0)]
    public void NothingLiesOutsideTheCone(double halfAngleDegrees)
    {
        var states = IonCloud.Draw(
            Beam(4000.0),
            Peptide,
            new IonCloudSettings
            {
                Ions = 5000,
                DivergenceRadians = halfAngleDegrees * Math.PI / 180.0,
                Seed = 11,
            });

        var angles = Angles(states);

        output.WriteLine(
            $"declared {halfAngleDegrees} deg: largest drawn {angles.Max():F3}, "
            + $"mean {angles.Average():F3}");

        Assert.True(
            angles.Max() <= halfAngleDegrees + 1e-9,
            $"an ion was drawn at {angles.Max():F3} degrees against a declared "
            + $"half-angle of {halfAngleDegrees}");

        // And it fills the cone rather than hugging the axis, which a draw that clamped
        // instead of sampling would also satisfy the bound above.
        Assert.True(angles.Max() > 0.97 * halfAngleDegrees);
    }

    /// <summary>The cone is filled uniformly in solid angle, not in polar angle.</summary>
    /// <remarks>
    /// <para>
    /// <b>The distinction that decides whether an aberration figure means anything.</b> A
    /// beam filling a round aperture is uniform over its <em>area</em>, so the density per
    /// unit polar angle goes as sin(theta) and most rays sit near the rim. Sampling theta
    /// uniformly would put half the rays inside half the angle and understate every
    /// aberration the cone is declared to probe — spherical aberration goes as the cube of
    /// the angle, so where the rays sit is most of the answer.
    /// </para>
    /// <para>
    /// Checked against the closed form rather than against a shape: for a uniform solid
    /// angle the mean of cos(theta) is (1 + cos(max)) / 2, which is arithmetic this code
    /// has no part in. Uniform-in-theta would give sin(max)/max instead — 0.9798 against
    /// 0.9698 at 20 degrees, a 1% difference, so the assertion is tight enough to tell
    /// them apart and is stated with both values.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheConeIsFilledUniformlyInSolidAngle()
    {
        const double HalfAngle = 20.0;

        var max = HalfAngle * Math.PI / 180.0;

        var states = IonCloud.Draw(
            Beam(4000.0),
            Peptide,
            new IonCloudSettings
            {
                Ions = 200000,
                DivergenceRadians = max,
                Seed = 3,
            });

        var meanCos = states.Average(s => s.Velocity.X / s.Velocity.Length);

        var solidAngle = (1.0 + Math.Cos(max)) / 2.0;   // uniform in solid angle
        var polar = Math.Sin(max) / max;                // uniform in polar angle

        output.WriteLine($"measured mean cos(theta)   {meanCos:F6}");
        output.WriteLine($"uniform in solid angle     {solidAngle:F6}");
        output.WriteLine($"uniform in polar angle     {polar:F6}");

        Assert.Equal(solidAngle, meanCos, 4);

        Assert.True(
            Math.Abs(meanCos - solidAngle) < Math.Abs(meanCos - polar),
            "the draw is closer to uniform-in-polar-angle than to uniform-in-solid-angle");
    }

    /// <summary>Divergence and temperature are separable, and both may be declared.</summary>
    /// <remarks>
    /// <para>
    /// <b>The reason the knob had to exist.</b> A 20 degree cone at 50 eV carries a
    /// transverse energy of 50 sin^2(20) = 5.85 eV, which as a temperature is about
    /// 68,000 K — and that same temperature would add 5.85 eV of <em>longitudinal</em>
    /// spread, turning a 50 +/- 0 eV beam into a 50 +/- 6 eV one. So a temperature cannot
    /// express a monoenergetic diverging beam at all, which is the beam an ion-optics
    /// figure means by "50 eV with a 20 degree spread".
    /// </para>
    /// <para>
    /// Asserted as the separation: the cone alone leaves the energy exactly monoenergetic,
    /// while a temperature chosen to give the same divergence spreads it by per cent.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADivergingBeamCanStillBeMonoenergetic()
    {
        const double Speed = 4000.0;
        const double HalfAngle = 20.0 * Math.PI / 180.0;

        var cone = IonCloud.Draw(
            Beam(Speed),
            Peptide,
            new IonCloudSettings { Ions = 20000, DivergenceRadians = HalfAngle, Seed = 5 });

        // The temperature whose transverse velocity matches the cone's, so the two make
        // comparably divergent beams and differ only in what they do to the energy.
        var transverse = Speed * Math.Sin(HalfAngle) / Math.Sqrt(2.0);
        var temperature = transverse * transverse * Peptide.MassSi / IonCloud.BoltzmannSi;

        var warm = IonCloud.Draw(
            Beam(Speed),
            Peptide,
            new IonCloudSettings { Ions = 20000, TemperatureK = temperature, Seed = 5 });

        static double EnergySpread(IReadOnlyList<PhaseState> states)
        {
            var mean = states.Average(s => s.Velocity.LengthSquared);
            var variance = states.Average(
                s => (s.Velocity.LengthSquared - mean) * (s.Velocity.LengthSquared - mean));

            return Math.Sqrt(variance) / mean;
        }

        var byCone = EnergySpread(cone);
        var byHeat = EnergySpread(warm);

        output.WriteLine($"temperature matched to the cone: {temperature:N0} K");
        output.WriteLine($"energy spread, cone        {byCone:E3}");
        output.WriteLine($"energy spread, temperature {byHeat:E3}");

        Assert.True(byCone < 1e-12, $"the cone spread the energy by {byCone:E3}");
        Assert.True(
            byHeat > 0.05,
            $"the matched temperature spread the energy by only {byHeat:E3}, so this "
            + "comparison is not showing what it claims");
    }

    /// <summary>A cloud declaring only a divergence is still a cloud.</summary>
    /// <remarks>
    /// <c>IsCloud</c> gates every ensemble figure of merit. A beam with a divergence and
    /// nothing else is exactly the beam an ion-optics study launches, and reading it as a
    /// single axial ion would compute every aberration on one ray down the middle — the
    /// one ray that has no aberration.
    /// </remarks>
    [Fact]
    public void ACloudDeclaringOnlyADivergenceIsStillACloud()
    {
        Assert.True(new IonCloudSettings { Ions = 1, DivergenceRadians = 0.1 }.IsCloud);
        Assert.False(new IonCloudSettings { Ions = 1 }.IsCloud);
    }
}
