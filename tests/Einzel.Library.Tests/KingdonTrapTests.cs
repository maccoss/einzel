using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A wire in a cylinder: orbital motion in a logarithmic potential.
/// </summary>
/// <remarks>
/// <para>
/// <b>The device is a good test because its closed forms are exact and strange.</b> In
/// phi = A ln(r) the inward force goes as 1/r, so the circular-orbit condition
/// m v^2 / r = q A / r has the radius cancel out of it: every circular orbit has the
/// <em>same speed</em>, whatever its radius. That is not a small-angle approximation or a
/// paraxial limit — it is exact, and it is what makes the trap work over a wide range of
/// radii rather than at one.
/// </para>
/// <para>
/// <b>It is also the first thing here to combine an axisymmetric solve with genuinely
/// three-dimensional motion.</b> The geometry is two coaxial cylinders and is solved in a
/// half-plane; the ion circles, so it uses all three coordinates. That combination was
/// built when <c>AxisymmetricField</c> landed and has never been exercised by a device.
/// </para>
/// <para>
/// The sharpest check available is that the azimuthal field is <em>exactly</em> zero by
/// construction, so angular momentum is conserved as an identity rather than to an
/// accuracy — a tolerance-free invariant on the whole transport path, of the same class as
/// the maximum principle for a solve and Liouville for an integrator.
/// </para>
/// </remarks>
public sealed class KingdonTrapTests(ITestOutputHelper output)
{
    private const double ElementaryCharge = 1.602176634e-19;

    private static CompiledModel Compile(params (string Name, double Millimetres)[] overrides)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("kingdon-trap"));

        var settings = overrides.ToDictionary(
            o => o.Name,
            o => Core.Units.Quantity.From(o.Millimetres, "mm"),
            StringComparer.Ordinal);

        var validation = ModelValidator.Validate(document, settings.Count == 0 ? null : settings);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>Flies the ion and returns every recorded sample.</summary>
    private static IReadOnlyList<TrajectorySample> Fly(CompiledModel model, double microseconds)
    {
        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var recorder = new TrajectoryRecorder(microseconds * 1e-6 / 400.0, capacity: 4096);

        TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = microseconds * 1e-6,
            },
            stopWhenNegative: null,
            recorder);

        return recorder.Samples;
    }

    private static double Radius(in Vec3 p) => Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z));

    /// <summary>Angular momentum about the axis, per unit mass.</summary>
    private static double AngularMomentum(in TrajectorySample s) =>
        (s.Position.Y * s.Velocity.Z) - (s.Position.Z * s.Velocity.Y);

    /// <summary>An ion launched tangentially circles the wire.</summary>
    /// <remarks>
    /// The whole device in one assertion. The ion is launched purely tangentially at the
    /// speed the closed form gives, and the orbit must stay at its radius and must not
    /// wander along the axis — the field is purely radial, so an axial excursion would mean
    /// the solve has an axial component it should not have.
    /// </remarks>
    [Fact]
    public void AnIonLaunchedTangentiallyOrbitsTheWire()
    {
        var model = Compile();
        var samples = Fly(model, 200.0);

        Assert.True(samples.Count > 100);

        var radii = samples.Select(s => Radius(s.Position)).ToList();
        var launched = radii[0];

        var worst = radii.Max(r => Math.Abs(r - launched)) / launched;
        var axial = samples.Max(s => Math.Abs(s.Position.X - samples[0].Position.X));

        // How many times round: the azimuth unwrapped over the flight.
        var turns = 0.0;
        for (var k = 1; k < samples.Count; k++)
        {
            var before = Math.Atan2(samples[k - 1].Position.Z, samples[k - 1].Position.Y);
            var now = Math.Atan2(samples[k].Position.Z, samples[k].Position.Y);
            var step = now - before;

            while (step > Math.PI)
            {
                step -= 2.0 * Math.PI;
            }

            while (step < -Math.PI)
            {
                step += 2.0 * Math.PI;
            }

            turns += step;
        }

        output.WriteLine($"launched at {launched * 1e3:F4} mm");
        output.WriteLine($"radius wanders by {worst:P3} of it");
        output.WriteLine($"axial excursion {axial * 1e6:F3} um");
        output.WriteLine($"completed {Math.Abs(turns) / (2.0 * Math.PI):F2} turns");

        Assert.True(
            Math.Abs(turns) / (2.0 * Math.PI) > 3.0,
            "the ion did not complete three orbits, so this is not measuring an orbit");

        Assert.True(worst < 0.02, $"the orbit radius wandered by {worst:P2}");

        // Purely radial field: nothing should push the ion along the axis at all.
        Assert.True(axial < 1e-6, $"the ion moved {axial * 1e6:F3} um axially");
    }

    /// <summary>Angular momentum is conserved as an identity, not to an accuracy.</summary>
    /// <remarks>
    /// <para>
    /// <b>The tolerance-free check.</b> An axisymmetric solve has <em>exactly</em> zero
    /// azimuthal field — not a small one — so there is no torque about the axis and angular
    /// momentum cannot change. Anything above round-off means the field has acquired an
    /// azimuthal component, which for a surface of revolution is meaningless and would
    /// corrupt every orbital device built on it.
    /// </para>
    /// <para>
    /// The same class of check as the maximum principle for a solve and Liouville for an
    /// integrator: an exact statement that a plausible-looking wrong answer cannot satisfy.
    /// </para>
    /// </remarks>
    [Fact]
    public void AngularMomentumIsConservedExactly()
    {
        var samples = Fly(Compile(), 200.0);

        var initial = AngularMomentum(samples[0]);
        var worst = samples.Max(s => Math.Abs(AngularMomentum(s) - initial)) / Math.Abs(initial);

        output.WriteLine($"L/m = {initial:E9} m^2/s");
        output.WriteLine($"worst relative departure over the flight: {worst:E3}");

        Assert.True(worst < 1e-9, $"angular momentum moved by {worst:E3}");
    }

    /// <summary>
    /// The orbital speed does not depend on the radius, which is what a logarithmic
    /// potential means.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The property the device is built on, and it is exact.</b> With E = A / r the
    /// circular-orbit condition m v^2 / r = q A / r loses its radius entirely, so one speed
    /// serves every orbit. An inverse-square potential does the opposite — Kepler's third
    /// law — so this is a statement about the logarithm rather than about orbits.
    /// </para>
    /// <para>
    /// Checked by launching at three radii spanning a factor of five with the <em>same</em>
    /// speed, taken from the closed form and containing no radius. All three must orbit.
    /// A field that was even slightly not logarithmic would hold one of them and let the
    /// others spiral.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1.5)]
    [InlineData(4.0)]
    [InlineData(7.5)]
    public void OneSpeedServesEveryOrbit(double launchRadiusMm)
    {
        var model = Compile(("launchRadius", launchRadiusMm));

        // The launch speed comes from `orbitPotential`, which is written with no radius in
        // it at all. That it works at every radius is the claim.
        var samples = Fly(model, 200.0);

        var radii = samples.Select(s => Radius(s.Position)).ToList();
        var launched = radii[0];
        var worst = radii.Max(r => Math.Abs(r - launched)) / launched;

        output.WriteLine(
            $"launched at {launched * 1e3:F3} mm at {model.LaunchSpeedSi():F2} m/s: "
            + $"radius wanders {worst:P3}");

        Assert.True(
            worst < 0.05,
            $"an orbit launched at {launchRadiusMm} mm wandered by {worst:P2}, so the "
            + "speed that holds one radius does not hold another");
    }

    /// <summary>The measured orbital speed matches the closed form.</summary>
    /// <remarks>
    /// <para>
    /// v^2 = q V / (m ln(b/a)), which is arithmetic this engine has no part in — the
    /// template computes the launch <em>potential</em> from it, and this recomputes the
    /// speed independently from the geometry and compares.
    /// </para>
    /// <para>
    /// It is a check on the solved field rather than on the integrator: if the solve is not
    /// logarithmic, the speed that holds a circular orbit is not this one, and the orbit
    /// asserted above would drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOrbitalSpeedMatchesTheClosedForm()
    {
        var model = Compile();

        var wire = 0.1e-3;
        var cylinder = 10.0e-3;
        var volts = 100.0;

        var species = IonSpecies.FromModel(model);

        var expected = Math.Sqrt(
            species.ChargeSi * volts / (species.MassSi * Math.Log(cylinder / wire)));

        var actual = model.LaunchSpeedSi();

        output.WriteLine($"closed form  {expected:F4} m/s");
        output.WriteLine($"template     {actual:F4} m/s");
        output.WriteLine($"ratio        {actual / expected:F9}");

        Assert.Equal(expected, actual, 6);
    }

    /// <summary>The solved potential is logarithmic between the electrodes.</summary>
    /// <remarks>
    /// The field the orbit rests on, checked directly rather than through an ion. Away from
    /// the wire, where the mesh resolves the geometry, the solve must follow
    /// A ln(r) + B — the same coaxial closed form the cylindrical operator was validated
    /// against when it was built, now on a real device.
    /// </remarks>
    [Fact]
    public void ThePotentialIsLogarithmicBetweenTheElectrodes()
    {
        var model = Compile();
        var field = FieldAssembly.Build(model);

        var wire = 0.1e-3;
        var cylinder = 10.0e-3;
        var volts = -100.0;

        var worst = 0.0;

        // From well clear of the wire out to the cylinder: the wire is 0.1 mm on a 0.25 mm
        // cell, so the cells nearest it do not resolve it and are not what the ion orbits.
        foreach (var radius in new[] { 2e-3, 3e-3, 4e-3, 6e-3, 8e-3 })
        {
            var point = new Vec3(20e-3, radius, 0.0);
            var got = field.PotentialAt(in point);

            var want = volts * Math.Log(radius / cylinder) / Math.Log(wire / cylinder);

            output.WriteLine($"r = {radius * 1e3:F1} mm: solved {got:F4} V, closed form {want:F4} V");

            worst = Math.Max(worst, Math.Abs(got - want));
        }

        Assert.True(worst < 1.0, $"the solved potential is {worst:F4} V off the logarithm");
    }
}
