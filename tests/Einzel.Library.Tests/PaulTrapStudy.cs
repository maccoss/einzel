using System.Globalization;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The three-dimensional quadrupole trap, and the two things that separate its
/// measured ejection boundary from the tabulated one.
/// </summary>
/// <remarks>
/// <para>
/// A Paul trap is the device the whole <c>ITransportMode</c> / figure-of-merit
/// apparatus is least suited to by default, because everything else here is
/// measured by ions <em>arriving</em>. A trapped ion never arrives anywhere, so a
/// transmission is zero for a trap that works and zero again for one that lost
/// everything, and no figure that counts arrivals can tell those apart. What a trap
/// is measured by is the complement - <c>confined</c>, the fraction still inside
/// when the hold ends - and this study exists as much to exercise that as to
/// measure the device.
/// </para>
/// <para>
/// Axisymmetric, so it is a half-plane solve rather than a volume: SYM-1 is what
/// makes a 3-D trap cost what a 2-D cross-section costs. The classical geometry has
/// <c>r0^2 = 2 z0^2</c>, which collapses
/// <c>q_z = 8 z e V / (m omega^2 (r0^2 + 2 z0^2))</c> to
/// <c>4 z e V / (m omega^2 r0^2)</c> - the same volts per unit q as a linear
/// quadrupole of the same inscribed radius, so the two are directly comparable. The
/// tabulated ejection boundary on the <c>a = 0</c> line is <c>q_z = 0.90804</c>.
/// </para>
/// </remarks>
public sealed class PaulTrapStudy(ITestOutputHelper output)
{
    /// <summary>The tabulated Mathieu boundary on the a = 0 line.</summary>
    private const double TabulatedQ = 0.90804;

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

    /// <summary>The stability parameter this geometry's declared radius implies.</summary>
    private static double NominalQ(CompiledModel model, double volts)
    {
        var species = IonSpecies.FromModel(model);
        var radius = model.Parameters["inscribedRadius"].SiValue;
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].SiValue;

        return 4.0 * species.ChargeSi * volts / (species.MassSi * omega * omega * radius * radius);
    }

    [Fact]
    public void TheWholeTrapIsOneBasisSolve()
    {
        var model = Compile(Trap());
        var solve = model.Fields[0].Solve!;

        var channels = GeometryBuilder.SolveChannels(solve);

        output.WriteLine($"electrodes    {solve.Electrodes.Count}");
        output.WriteLine($"basis solves  {channels.Count}");
        output.WriteLine(
            $"convergence   {channels[0].Report.Cycles} cycles at factor "
            + $"{channels[0].Report.ConvergenceFactor:F4}");

        // Three electrodes, one spatial pattern: the endcaps are grounded, so the
        // only thing that moves is the ring and there is nothing to superpose.
        Assert.Equal(3, solve.Electrodes.Count);
        Assert.Single(channels);
        Assert.True(channels[0].Report.Converged);
    }

    [Fact]
    public void ARestingIonNeedsNoAcceleratingPotential()
    {
        // A trap holds its ions still, so the source starts at rest and the drive is
        // the only thing that can move it. That is legal precisely when a field can
        // do work - and asking only about the DC potential, which is how this check
        // was first written, declares the archetypal start-at-rest device incapable
        // of moving anything: every electrode here holds zero volts of DC and all of
        // its potential as drive.
        var document = Trap();

        Assert.Equal(0.0, document.Source!.AccelerationPotential!.Value);

        foreach (var electrode in document.Fields![0].Solve!.Electrodes!)
        {
            Assert.True(
                electrode.Potential is null || electrode.Potential.Value == 0.0,
                $"{electrode.Name} was expected to hold no DC");
        }

        Assert.True(ModelValidator.Validate(document).IsValid);
    }

    [Fact]
    public void TheFieldIsQuadrupolarAndItsEffectiveRadiusIsSmallerThanDeclared()
    {
        // Why the measured cut-off comes in below the tabulated one, measured rather
        // than asserted. The electrodes here are flat annuli, and a flat annulus at
        // the nominal r0 (or z0) lies *inside* the hyperbola that shares its vertex
        // everywhere except at that vertex: at z = 2.23 mm the ring hyperbola would
        // be at r = 5.09 mm and this ring is at 4.00, and at r = 3.4 mm the endcap
        // hyperbola would be at z = 3.71 mm and this endcap is at 2.83. Metal closer
        // in means a stronger field at the centre than the declared radius implies,
        // which is a smaller effective r0, which is a larger q per volt - so
        // ejection happens at a lower amplitude. The sign is the check.
        var model = Compile(Trap());
        var field = (ITimeVaryingField)FieldAssembly.Build(model);

        var declared = model.Parameters["inscribedRadius"].SiValue;
        var volts = model.Parameters["rfAmplitude"].SiValue;

        output.WriteLine("     delta      dEz/dz      dEr/dr       ratio     r0_eff");

        var effective = double.NaN;
        var anharmonicity = new List<double>();

        foreach (var delta in new[] { 0.4e-3, 0.6e-3, 0.8e-3 })
        {
            // The drive is a cosine, so t = 0 is the top of the cycle and the ring
            // sits at its full declared amplitude. Central differences either side
            // of the centre, several cells wide - the grid here is 0.13 mm and a
            // difference narrower than a few cells measures the interpolant.
            var axial =
                (field.ElectricFieldAt(new Vec3(delta, 0.0, 0.0), 0.0).X
                    - field.ElectricFieldAt(new Vec3(-delta, 0.0, 0.0), 0.0).X)
                / (2.0 * delta);

            var radial =
                (field.ElectricFieldAt(new Vec3(0.0, delta, 0.0), 0.0).Y
                    - field.ElectricFieldAt(new Vec3(0.0, -delta, 0.0), 0.0).Y)
                / (2.0 * delta);

            // phi = V (r^2 - 2 z^2) / (2 r0^2) gives dEz/dz = +2V/r0^2 and
            // dEr/dr = -V/r0^2, so the ratio is exactly -2 wherever the quadratic
            // term dominates. That is Laplace's equation in cylindrical coordinates,
            // and it is the check that the expansion is valid at this radius at all.
            // It drifts off -2 as the sampling radius grows, by about a per cent
            // over this range, which is the anharmonicity flat electrodes buy: a
            // hyperbolic trap would hold -2 everywhere by construction.
            var radius = Math.Sqrt(2.0 * volts / axial);

            output.WriteLine(
                $"{delta * 1e3,10:F2}  {axial,10:E3}  {radial,10:E3}  "
                + $"{axial / radial,10:F4}  {radius * 1e3,9:F4}");

            anharmonicity.Add(Math.Abs((axial / radial) + 2.0));

            // The innermost sample, where the quadratic term is least contaminated.
            effective = double.IsNaN(effective) ? radius : effective;
        }

        // Quadrupolar at the centre, to 0.7 percent - and less so further out, by a
        // factor of four across a doubling of the sampling radius. That growth is
        // the assertion worth making rather than a blanket tolerance: a hyperbolic
        // trap holds the ratio at exactly -2 everywhere by construction, so a
        // departure that *grows with radius* is the higher multipole flat electrodes
        // buy, and one that did not would be discretisation or a bug.
        output.WriteLine(
            "anharmonicity " + string.Join(
                ", ", anharmonicity.Select(a => a.ToString("F4", CultureInfo.InvariantCulture))));

        Assert.True(anharmonicity[0] < 0.02, $"the centre is not quadrupolar: {anharmonicity[0]:F4}");

        Assert.True(
            anharmonicity[0] < anharmonicity[1] && anharmonicity[1] < anharmonicity[2],
            "the departure from -2 should grow with sampling radius, as a multipole does");

        output.WriteLine($"declared r0   {declared * 1e3:F4} mm");
        output.WriteLine($"effective r0  {effective * 1e3:F4} mm");
        output.WriteLine($"(r0/r0_eff)^2 {Math.Pow(declared / effective, 2.0):F4}");

        // And by enough to matter. If a scale factor were the *whole* departure from
        // the ideal trap, the boundary would sit at the tabulated q scaled by
        // (r0_eff/r0)^2 - that is 0.828, or 677.5 V, against 908 V for a trap that
        // really had r0 = 4 mm. So the effective radius accounts for the sign and
        // most of the 9.4 per cent shortfall.
        //
        // It is not the whole story, and the study notes below say why: the measured
        // edge is amplitude-dependent (q_z = 0.860 at a 0.1 mm launch, 0.824 at
        // 0.3 mm, 0.635 at 0.6 mm, all converged in hold time), which an ideal trap's
        // cannot be. What is asserted here is therefore the field property, which is
        // sharp, rather than the agreement, which is partly coincidence.
        var scaled = TabulatedQ * Math.Pow(effective / declared, 2.0);
        var perVolt = NominalQ(model, 1.0);

        output.WriteLine($"q at boundary {scaled:F5} (tabulated {TabulatedQ:F5})");
        output.WriteLine(
            $"implies       {scaled / perVolt:F2} V, against 674 V measured at a 0.3 mm launch "
            + "and 700 to 704 V at 0.1 mm");

        Assert.True(
            effective < declared,
            $"flat electrodes should give an effective radius below the declared "
            + $"{declared * 1e3:F3} mm, and this one is {effective * 1e3:F3}");

        // Between the tabulated boundary and the amplitude-dependent measurements,
        // which is where a scale-factor-only account has to land.
        Assert.InRange(scaled / perVolt, 640.0, 720.0);
    }

    [Fact]
    public void ItHoldsAnIonWellInsideTheBoundaryAndEjectsItAxiallyOutside()
    {
        // The two ends of the measurement, with the loss named. An ion ejected from
        // a 3-D trap on the a = 0 line goes *axially* first, because q_r is half
        // q_z, and that is checkable rather than assumed: the ion ends on an endcap
        // at exactly z0, not on the ring. Which endcap is not asserted - the trap is
        // symmetric about its centre and the phase of the drive when the growth wins
        // decides, so pinning the direction would be pinning an accident.
        foreach (var (volts, held) in new[] { (400.0, true), (760.0, false) })
        {
            var model = Compile(Trap(("rfAmplitude", volts)));
            var field = FieldAssembly.Build(model);
            var species = IonSpecies.FromModel(model);

            var launch = new PhaseState(
                model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

            var point = model.DetectorPoint;
            var normal = model.DetectorNormal;

            TrajectoryStopFunction detector =
                (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

            var result = TrajectoryIntegrator.Integrate(
                launch,
                species,
                field,
                new IntegrationSettings
                {
                    RelativeTolerance = 1e-9,
                    MaximumFlightTime = model.MaximumFlightTimeSi,
                },
                detector);

            output.WriteLine(
                $"{volts,6:F0} V  q_z {NominalQ(model, volts):F4}  {result.Outcome}  "
                + $"{result.StruckSurface ?? "-"}  x = {result.FinalState.Position.X * 1e3:F4} mm  "
                + $"{result.AcceptedSteps} steps");

            if (held)
            {
                Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
                Assert.Null(result.StruckSurface);
            }
            else
            {
                Assert.Equal(TrajectoryOutcome.StruckElectrode, result.Outcome);
                Assert.Contains("endcap", result.StruckSurface!, StringComparison.Ordinal);

                var z0 = model.Parameters["endcapHalfSpacing"].SiValue;

                Assert.Equal(z0, Math.Abs(result.FinalState.Position.X), 1e-6);
            }
        }
    }

    // What this study measures and does not assert, and why.
    //
    // What this study measures and does not assert, and why. None of the numbers
    // below is asserted: a single boundary costs about thirty two-hundred-cycle
    // flights, and they belong in docs/device-templates.md where the controls can
    // sit beside them rather than in a test that runs on every build.
    //
    // The ejection boundary is 672 to 674 V at a 0.3 mm launch, 200 RF cycles, on a
    // 128 x 64 grid - q_z = 0.8218 to 0.8236 against a tabulated 0.90804. It is
    // mesh-converged (the identical 672-674 at 256 x 128) and hold-converged (the
    // identical 674 at 800 cycles).
    //
    // It is NOT amplitude-converged, and that is the finding. Hold-converged edges:
    //
    //     launch    edge        q_z
    //     0.1 mm    700-704 V   0.855-0.860
    //     0.3 mm    674 V       0.8236
    //     0.6 mm    ~520 V      0.635
    //
    // An ideal trap's boundary cannot depend on amplitude - the Mathieu equation is
    // linear, so a trajectory scaled by a constant is another trajectory. This one
    // depends on it strongly, which is the anharmonicity measured above doing its
    // work. It also means the edge cannot be reduced to the effective radius alone:
    // the scale factor predicts 0.828, which matches the 0.3 mm figure and not the
    // 0.1 mm one, so that agreement is partly coincidence. A measurement that only
    // reaches an electrode by growing to z0 is never a small-amplitude measurement,
    // whatever it was launched at.
    //
    // The finite window, which is the part worth remembering. At 60 cycles the
    // boundary is not a boundary at all but a ragged strip: confined at 674, lost at
    // 676 and 678, confined again at 680, lost at 682, confined at 684, and so on to
    // a solid loss from 690. At 200 cycles the same scan is a clean step. Nothing
    // about the design changed - the growth rate goes to zero at the stability edge,
    // so whether a marginally unstable ion reaches an electrode inside the hold is a
    // property of the hold. A boundary quoted without its observation window is not
    // quoted.
    //
    // And a narrow band of loss at 605 to 614 V (q_z = 0.739 to 0.750), well inside
    // the stable region. Every control says it is real: identical at 256 x 128,
    // identical at 400 cycles, absent at 60 (so the growth is slow and secular
    // rather than exponential), and absent at a 0.1 mm launch (so it is driven by
    // the field's higher multipoles, which a linear boundary cannot be). Which
    // resonance it is is NOT established - beta_z there is 0.615, which lands on no
    // n_z beta_z + n_r beta_r = 2 for any multipole order up to six - and settling
    // that needs a frequency analysis of the secular motion rather than a loss test.
    //
    // That band is also why BoundarySearch now walks outward from its converged
    // bracket. It found this one on its first real use, from a bracket whose
    // bisection had converged cleanly onto the main edge 60 V above it.
}
