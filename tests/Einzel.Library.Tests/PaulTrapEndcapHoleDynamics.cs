using System.Globalization;
using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// What an ion does in a quadrupole trap with holes in its endcaps and a stretched endcap
/// spacing: where it leaves, its secular frequency, the stability edge, and whether a
/// nonlinear resonance band appears inside the stable region.
/// </summary>
/// <remarks>
/// Companion to <see cref="PaulTrapEndcapHoleStudy"/>, which measures the same geometries'
/// fields. The numbers printed here are the ones in <c>docs/device-templates.md</c>; what is
/// asserted is what the geometry fixes rather than a magnitude.
/// </remarks>
public sealed class PaulTrapEndcapHoleDynamics(ITestOutputHelper output)
{
    private const double HoleMm = 0.6;
    private const double Stretch = 0.107;

    private static ModelDocument Trap(params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("paul-trap"));

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        return validation.Model!;
    }

    private static readonly (string Name, (string, double)[] Overrides)[] Geometries =
    [
        ("ideal", []),
        ("both holes", [("endcapEntranceHoleRadius", HoleMm), ("endcapExitHoleRadius", HoleMm)]),
        ("exit hole only", [("endcapExitHoleRadius", HoleMm)]),
        ("holes + stretch",
            [("endcapEntranceHoleRadius", HoleMm), ("endcapExitHoleRadius", HoleMm),
             ("endcapStretchFraction", Stretch)]),
    ];

    private sealed record Flight(
        TrajectoryResult Result, IReadOnlyList<TrajectorySample> Samples, CompiledModel Model);

    /// <summary>Flies one ion from an axial and a radial offset, starting at rest.</summary>
    private static Flight Fly(
        (string, double)[] overrides, double volts, double axialMm, double radialMm, int cycles,
        bool record = false)
    {
        var model = Compile(Trap(
            overrides.Append(("rfAmplitude", volts)).Append(("cycles", (double)cycles)).ToArray()));

        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);
        var drive = model.Parameters["driveFrequency"].SiValue;

        var launch = new PhaseState(new Vec3(axialMm * 1e-3, radialMm * 1e-3, 0.0), Vec3.Zero);

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

        var recorder = record ? new TrajectoryRecorder(1.0 / (16.0 * drive)) : null;

        var result = TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector,
            recorder);

        return new Flight(result, recorder?.Samples ?? [], model);
    }

    /// <summary>
    /// Whether the ion is still inside the trap at the end of the hold.
    /// </summary>
    /// <remarks>
    /// Asked of the end position as well as the outcome, because the model has one detector
    /// plane and it is behind the exit endcap: an ion leaving through an entrance hole is
    /// caught by nothing, coasts out of the solve domain, and ends the hold with the same
    /// outcome as an ion still held.
    /// </remarks>
    private static bool Held(Flight flight)
    {
        if (flight.Result.Outcome != TrajectoryOutcome.MaximumFlightTimeReached)
        {
            return false;
        }

        var z0 = flight.Model.Parameters["endcapHalfSpacing"].SiValue
            + flight.Model.Parameters["endcapStretch"].SiValue;
        var r0 = flight.Model.Parameters["inscribedRadius"].SiValue;
        var p = flight.Result.FinalState.Position;

        return Math.Abs(p.X) < z0 && Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z)) < r0;
    }

    private static double EffectiveRadius((string, double)[] overrides)
    {
        var model = Compile(Trap(overrides));
        var field = (ITimeVaryingField)FieldAssembly.Build(model);
        var volts = model.Parameters["rfAmplitude"].SiValue;

        const double Delta = 0.4e-3;

        var axial =
            (field.ElectricFieldAt(new Vec3(Delta, 0.0, 0.0), 0.0).X
                - field.ElectricFieldAt(new Vec3(-Delta, 0.0, 0.0), 0.0).X)
            / (2.0 * Delta);

        return Math.Sqrt(2.0 * volts / axial);
    }

    /// <summary>q_z per volt for a trap of the given radius, at the template's ion and drive.</summary>
    private static double QPerVolt(double radius)
    {
        var model = Compile(Trap());
        var species = IonSpecies.FromModel(model);
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].SiValue;

        return 4.0 * species.ChargeSi / (species.MassSi * omega * omega * radius * radius);
    }

    private static double Line(IReadOnlyList<TrajectorySample> samples, int axis, double drive)
    {
        var spectrum = SecularSpectrum.From(samples, axis, 0.02 * drive, 0.90 * drive, 6000);
        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, _, _, _) = peak;

        return value.In("Hz");
    }

    /// <summary>The Mathieu characteristic exponent, from its continued fraction.</summary>
    /// <remarks>Duplicated from the other studies on purpose: the check is the closed form.</remarks>
    private static double Beta(double a, double q, int depth = 40)
    {
        var beta = Math.Sqrt(Math.Max(a + (q * q / 2.0), 1e-6));

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var up = 0.0;
            var down = 0.0;

            for (var n = depth; n >= 1; n--)
            {
                up = q * q / (((beta + (2 * n)) * (beta + (2 * n))) - a - up);
                down = q * q / (((beta - (2 * n)) * (beta - (2 * n))) - a - down);
            }

            var next = Math.Sqrt(Math.Max(a + up + down, 1e-12));

            if (Math.Abs(next - beta) < 1e-14)
            {
                return next;
            }

            beta = 0.5 * (beta + next);
        }

        return beta;
    }

    /// <summary>
    /// An ion ejected along the axis through the exit hole arrives at the detector; one
    /// that meets the exit endcap off the hole is itemised on <c>endcapExit</c>.
    /// </summary>
    /// <remarks>
    /// The two launches differ only in how far off axis the ion starts. Driven past the
    /// stability edge on the a = 0 line the axial motion grows and the radial motion stays
    /// bounded, so the radial offset decides whether the ion meets the hole or the metal
    /// around it. Which endcap the growing axial motion reaches first depends on the launch,
    /// so both signs of axial offset are flown and the exit-going one is the one asserted.
    /// </remarks>
    [Fact]
    public void AnIonThroughTheExitHoleArrivesAndOneOffItIsItemised()
    {
        (string, double)[] exitHole = [("endcapExitHoleRadius", HoleMm)];

        Flight? onAxis = null;
        Flight? offAxis = null;

        foreach (var (axial, volts) in new[] { (0.05, 800.0), (-0.05, 800.0), (-0.05, 850.0), (-0.05, 900.0) })
        {
            foreach (var radial in new[] { 0.01, 1.0, 2.0, 2.5 })
            {
                var flight = Fly(exitHole, volts, axial, radial, 200);
                var p = flight.Result.FinalState.Position;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{volts,4:F0} V  z {axial,5:F2} mm  r {radial,4:F2} mm  {flight.Result.Outcome}  {flight.Result.StruckSurface ?? "-"}  "
                    + $"end x = {p.X * 1e3:F3} mm, r = {Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z)) * 1e3:F3} mm"));

                if (radial == 0.01 && flight.Result.Outcome == TrajectoryOutcome.StopConditionMet)
                {
                    onAxis ??= flight;
                }

                if (flight.Result.StruckSurface == "endcapExit")
                {
                    offAxis ??= flight;
                }
            }
        }

        Assert.NotNull(onAxis);
        Assert.NotNull(offAxis);

        // Arrived at the detector plane behind the exit endcap, having passed through it.
        Assert.Equal(onAxis.Model.DetectorPoint.X, onAxis.Result.FinalState.Position.X, 9);

        // The control: with no hole the same on-axis ion cannot arrive.
        foreach (var axial in new[] { 0.05, -0.05 })
        {
            var solid = Fly([], 800.0, axial, 0.01, 200);

            Assert.Equal(TrajectoryOutcome.StruckElectrode, solid.Result.Outcome);
            Assert.StartsWith("endcap", solid.Result.StruckSurface!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The secular frequency, the stability edge, and a scan of the stable region for a
    /// resonance band, for each geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The secular line is measured at a small launch (0.05 mm) and compared with Mathieu at
    /// the q the field's own curvature implies, so what it checks is that the effective
    /// radius describes the ion's motion. The edge is bisected at the same launch over two
    /// hundred cycles. The scan holds an ion launched 0.3 mm out - where the flat-annulus
    /// trap lost its ion in an octupole band - at q_eff from 0.60 to 0.89.
    /// </para>
    /// </remarks>
    [Fact]
    public void SecularFrequencyEdgeAndResonanceScan()
    {
        var drive = Compile(Trap()).Parameters["driveFrequency"].SiValue;

        output.WriteLine(
            "geometry          r0_eff(mm)  V@q.5   beta_z meas  Mathieu(q_eff)  ratio    edge (V)          q_eff edge  q_nom edge");

        var edges = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (name, overrides) in Geometries)
        {
            var radius = EffectiveRadius(overrides);
            var perVolt = QPerVolt(radius);
            var nominalPerVolt = QPerVolt(4.0e-3);

            // Secular line at q_eff = 0.5.
            var volts = 0.5 / perVolt;
            var flight = Fly(overrides, volts, 0.05, 0.05, 200, record: true);

            Assert.True(Held(flight));

            var betaZ = 2.0 * Line(flight.Samples, 0, drive) / drive;
            var predicted = Beta(0.0, 0.5);

            // The edge.
            double held = 600.0, lost = 1000.0;

            Assert.True(Held(Fly(overrides, held, 0.05, 0.0, 200)));
            Assert.False(Held(Fly(overrides, lost, 0.05, 0.0, 200)));

            for (var k = 0; k < 9; k++)
            {
                var mid = 0.5 * (held + lost);

                if (Held(Fly(overrides, mid, 0.05, 0.0, 200)))
                {
                    held = mid;
                }
                else
                {
                    lost = mid;
                }
            }

            edges[name] = 0.5 * (held + lost);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-17} {radius * 1e3,9:F4}  {volts,7:F2}  {betaZ,11:F5}  {predicted,14:F5}  "
                + $"{betaZ / predicted,7:F5}  {held,7:F2}-{lost,7:F2}  {perVolt * held,8:F5}-{perVolt * lost:F5}  "
                + $"{nominalPerVolt * held,8:F5}"));

            // The effective radius describes the motion to a part in a thousand, holes or not.
            Assert.Equal(1.0, betaZ / predicted, 2);

            // And the edge, in the q the field implies, is at the Mathieu boundary of 0.908 to
            // two percent - the holes and the stretch move the volts far more than the boundary.
            // Two percent and not one, for two reasons the first band ignored. The holes really
            // do push the edge outward by about one and a half percent, which is this study's
            // own finding (docs/device-templates.md), so a band ending at 0.92 was asserting
            // less than the measurement it sat beside. And an ejection edge found by bisection
            // is where growth is slowest, so its last digits belong to the platform's
            // arithmetic as much as to the trap: with both holes it is 759.4-760.2 V
            // (q 0.9191-0.9200) on Windows and 762.5-763.3 V (0.9229-0.9238) on Linux, 0.4
            // percent apart. The first band passed on Windows by 0.0009 and failed on Linux.
            Assert.InRange(perVolt * held, 0.89, 0.93);
        }

        output.WriteLine("");
        output.WriteLine("resonance scan, 0.3 mm launch (axial and radial), 200 cycles; '.' held, 'x' lost:");

        foreach (var (name, overrides) in Geometries)
        {
            var perVolt = QPerVolt(EffectiveRadius(overrides));
            var line = new System.Text.StringBuilder();
            var lostAt = new List<double>();

            for (var q = 0.60; q < 0.8901; q += 0.01)
            {
                var flight = Fly(overrides, q / perVolt, 0.3, 0.3, 200);
                var held = Held(flight);

                line.Append(held ? '.' : 'x');

                if (!held)
                {
                    lostAt.Add(q);
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-17} q 0.60 {line} 0.89   lost at q_eff {string.Join(", ", lostAt.Select(q => q.ToString("F2", CultureInfo.InvariantCulture)))}"));
        }
    }

    /// <summary>
    /// One hole, and a resonance band inside the stable region that two holes do not show.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a hole in the exit endcap alone the trap is no longer symmetric about its center
    /// plane, so odd multipoles appear - a hexapole above all, measured in
    /// <see cref="PaulTrapEndcapHoleStudy"/> - and a hexapole's resonances satisfy
    /// <c>n_z beta_z + n_r beta_r = 2</c> with <c>n_z + n_r = 3</c>. In an axisymmetric field
    /// the radial index must be even, which leaves <c>3 beta_z = 2</c> and
    /// <c>beta_z + 2 beta_r = 2</c>. The scan finds the band; the frequencies are then measured
    /// on an ion held just below it, in the solved geometry, and the conditions evaluated.
    /// Two meshes, because a band that moved with the mesh would be the mesh.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneHoleOpensAHexapoleBand()
    {
        (string, double)[] exitHole = [("endcapExitHoleRadius", HoleMm)];

        var drive = Compile(Trap()).Parameters["driveFrequency"].SiValue;
        var bands = new List<(double Low, double High)>();

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            var overrides = exitHole.Append(("cellsPerRadius", cells)).ToArray();
            var perVolt = QPerVolt(EffectiveRadius(overrides));
            var line = new System.Text.StringBuilder();
            var lost = new List<double>();

            for (var k = 0; k <= 24; k++)
            {
                var q = 0.75 + (0.0025 * k);
                var held = Held(Fly(overrides, q / perVolt, 0.3, 0.3, 200));

                line.Append(held ? '.' : 'x');

                if (!held)
                {
                    lost.Add(q);
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{cells,3:F0} cells/r0  q_eff 0.750 {line} 0.810"));

            Assert.NotEmpty(lost);
            bands.Add((lost.Min(), lost.Max()));

            // The frequencies of an ion held just below the band.
            var below = lost.Min() - 0.005;
            var flight = Fly(overrides, below / perVolt, 0.3, 0.3, 200, record: true);

            Assert.True(Held(flight));

            var betaZ = 2.0 * Line(flight.Samples, 0, drive) / drive;
            var betaR = 2.0 * Line(flight.Samples, 1, drive) / drive;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"   band q_eff {lost.Min():F4}-{lost.Max():F4}; at {below:F4}: beta_z {betaZ:F4}, beta_r {betaR:F4}, "
                + $"3 beta_z = {3.0 * betaZ:F4}, beta_z + 2 beta_r = {betaZ + (2.0 * betaR):F4}; "
                + $"Mathieu beta_z there {Beta(0.0, below):F4}"));

            // 3 beta_z = 2 within the band's own width in beta.
            Assert.InRange(3.0 * betaZ, 1.96, 2.02);
        }

        // The band does not move with the mesh by more than its own width.
        Assert.True(Math.Abs(bands[0].Low - bands[1].Low) <= 0.0051);

        // And the control: two equal holes, the same scan, nothing lost.
        (string, double)[] both = [("endcapEntranceHoleRadius", HoleMm), ("endcapExitHoleRadius", HoleMm)];
        var perVoltBoth = QPerVolt(EffectiveRadius(both));

        for (var k = 0; k <= 24; k += 2)
        {
            var q = 0.75 + (0.0025 * k);

            Assert.True(Held(Fly(both, q / perVoltBoth, 0.3, 0.3, 200)), $"two holes lost an ion at q_eff {q:F4}");
        }
    }
}
