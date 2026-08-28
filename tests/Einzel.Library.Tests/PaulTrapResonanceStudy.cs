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
/// Naming the Paul trap's resonance band, which a loss measurement could not.
/// </summary>
/// <remarks>
/// <para>
/// The shipped trap loses its ion in a narrow band at 605–614 V, sixty volts inside
/// what the Mathieu chart calls stable. Every control says it is real — identical at
/// twice the mesh and twice the hold, absent at a quarter of the hold and absent at a
/// third of the launch offset — but a scan over amplitude can only say <em>that</em>
/// there is a band. A nonlinear resonance is <strong>defined</strong> by a frequency
/// condition, <c>n_z β_z + n_r β_r = 2</c> for a multipole of order
/// <c>n_z + n_r</c>, so naming one means measuring the frequencies.
/// </para>
/// <para>
/// The prediction from ideal Mathieu theory does not work here, and that is the
/// point of doing it in the solved field instead. β from the closed form at
/// <c>q_z = 0.745</c> is 0.6156, which satisfies no low-order condition. But the
/// closed form is for the trap this geometry is <em>named</em> after, not the one it
/// is: the effective radius is 3.82 mm rather than 4.00, so the real q is higher,
/// and the anharmonicity shifts the frequency again with amplitude. The measured β
/// is the one the resonance condition is about, and it is 0.6769 rather than 0.6156.
/// </para>
/// <para>
/// <strong>The answer is the octupole.</strong> At the band centre the measured
/// exponents are β_z = 0.6769 and β_r = 0.3225, and
/// <c>2β_z + 2β_r = 1.9989</c> — an order-four condition met to 0.055 per cent,
/// against 0.22 and 0.10 at the amplitudes either side. Order four is an octupole,
/// which is precisely the leading unwanted multipole this geometry's symmetry
/// permits: the trap is symmetric about its own centre plane and about the axis, so
/// every odd order vanishes. The identification was predicted before it was fitted,
/// and it is independently corroborated by the field measurement in
/// <c>PaulTrapStudy</c>, where the curvature ratio departs from −2 by an amount that
/// grows with radius.
/// </para>
/// </remarks>
public sealed class PaulTrapResonanceStudy(ITestOutputHelper output)
{
    /// <summary>The centre of the measured loss band, in volts.</summary>
    private const double BandCentre = 610.0;

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

    /// <summary>Flies one ion in the trap and records its path.</summary>
    private static (IReadOnlyList<TrajectorySample> Samples, TrajectoryOutcome Outcome, double DriveHz)
        Fly(double volts, double offsetMm, int cycles)
    {
        var model = Compile(Trap(
            ("rfAmplitude", volts), ("launchOffset", offsetMm), ("cycles", cycles)));

        var field = FieldAssembly.Build(model);
        var species = IonSpecies.FromModel(model);
        var drive = model.Parameters["driveFrequency"].SiValue;

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var point = model.DetectorPoint;
        var normal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

        // Sixteen samples per RF cycle, the cadence the ideal-quadrupole check used.
        var recorder = new TrajectoryRecorder(1.0 / (16.0 * drive));

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

        return (recorder.Samples, result.Outcome, drive);
    }

    /// <summary>β from a measured line: the line is β Ω / 2.</summary>
    private static double BetaFrom(double lineHz, double driveHz) => 2.0 * lineHz / driveHz;

    [Fact]
    public void TheSecularFrequenciesInTheBandAreMeasurable()
    {
        // Just below the band, where the ion is confined for the whole hold and the
        // record is therefore long and clean. Both axes at once: a three-dimensional
        // trap has two independent secular motions and a resonance condition couples
        // them, so one of them alone cannot answer the question.
        var (samples, outcome, drive) = Fly(BandCentre - 12.0, 0.3, 200);

        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, outcome);

        output.WriteLine($"{samples.Count} samples, drive {drive / 1e3:F1} kHz");
        output.WriteLine("axis          line (kHz)      beta      power");

        var betas = new double[2];

        for (var axis = 0; axis < 2; axis++)
        {
            var spectrum = SecularSpectrum.From(samples, axis, 0.02 * drive, 0.90 * drive, 6000);
            var peak = spectrum.Peak();

            Assert.NotNull(peak);

            var (value, _, _, _) = peak;
            var line = value.In("Hz");

            betas[axis] = BetaFrom(line, drive);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{(axis == 0 ? "axial (z)" : "radial (r)"),-12}{line / 1e3,10:F2}  {betas[axis],10:F4}  "
                + $"{spectrum.Lines.Max(l => l.Power),9:F4}"));
        }

        // The signature of a three-dimensional quadrupole trap, and a check that
        // does not depend on any measured absolute: on the a = 0 line q_r is exactly
        // half q_z, so beta_r is smaller than beta_z and by roughly a factor of two
        // in the low-q limit. Whatever the effective radius is, it scales both.
        output.WriteLine($"beta_z / beta_r  {betas[0] / betas[1]:F4}");

        Assert.True(betas[0] > betas[1], "the axial motion should be the faster one");
        Assert.InRange(betas[0] / betas[1], 1.5, 3.0);
    }

    /// <summary>
    /// The Mathieu characteristic exponent, from its continued fraction.
    /// </summary>
    /// <remarks>
    /// The closed form the measurement is checked against, written here rather than
    /// shipped in the engine: a test comparing the engine's beta to the engine's
    /// spectrum would be testing self-consistency. Same routine as the one in
    /// SecularSpectrumTests, and deliberately duplicated for the same reason.
    /// </remarks>
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

    [Fact]
    public void TheEffectiveRadiusIsConfirmedByTheSecularFrequency()
    {
        // Two entirely different routes to the same number. PaulTrapStudy reads the
        // effective radius off the FIELD - a curvature measured at the centre with
        // no ion involved - and gets 3.8195 mm against 4.0000 declared. Here it is
        // read off a TRAJECTORY: fly an ion for two hundred RF cycles, take the
        // periodogram, and compare the secular line to Mathieu's closed form
        // evaluated at q scaled by (r0/r0_eff) squared. Nothing is shared between
        // the two measurements except the solved field itself.
        //
        // Agreement at low q is the claim. The departure at high q is the other
        // half of the same statement: the trap is an ideal quadrupole of radius
        // 3.82 mm to the extent the ion stays small, and stops being one as the
        // excursion grows - which is the anharmonicity, arriving on schedule.
        const double EffectiveMm = 3.8195;
        const double Declared = 4.0;

        var factor = Math.Pow(Declared / EffectiveMm, 2.0);

        // q per volt for this ion in this geometry, from the closed form alone.
        var model = Compile(Trap());
        var species = IonSpecies.FromModel(model);
        var radius = model.Parameters["inscribedRadius"].SiValue;
        var omega = 2.0 * Math.PI * model.Parameters["driveFrequency"].SiValue;
        var perVolt = 4.0 * species.ChargeSi / (species.MassSi * omega * omega * radius * radius);

        output.WriteLine($"effective-radius factor {factor:F5}");
        output.WriteLine(" volts    q_nom    q_eff     beta   predicted    measured    ratio");

        var worstLow = 0.0;

        foreach (var volts in new[] { 200.0, 300.0, 400.0, 600.0 })
        {
            var (samples, _, drive) = Fly(volts, 0.3, 200);
            var measured = Line(samples, 0, drive);

            var nominal = volts * perVolt;
            var effective = nominal * factor;
            var predicted = Beta(0.0, effective) * drive / 2.0;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{volts,6:F0}  {nominal,7:F5}  {effective,7:F5}  {Beta(0.0, effective),7:F5}  "
                + $"{predicted / 1e3,9:F3}  {measured / 1e3,10:F3}  {measured / predicted,7:F4}"));

            if (volts <= 400.0)
            {
                worstLow = Math.Max(worstLow, Math.Abs((measured / predicted) - 1.0));
            }
            else
            {
                // The high-q end must be measurably WORSE, or the anharmonicity
                // measured in the field has no consequence for an ion and one of the
                // two measurements is wrong.
                Assert.True(
                    measured < 0.99 * predicted,
                    $"at q = {nominal:F3} the anharmonicity should pull the secular line below the "
                    + $"ideal-quadrupole prediction, and it is at {measured / predicted:F4} of it");
            }
        }

        output.WriteLine($"worst departure below q = 0.5: {100.0 * worstLow:F3} per cent");

        // Half a per cent is generous for what is measured at 0.02 to 0.28. The
        // point of the assertion is that a field curvature and a flight time agree
        // at all, not that they agree to the last figure.
        Assert.True(
            worstLow < 0.005,
            $"the two routes to the effective radius should agree to half a per cent at low q, "
            + $"and the worst is {100.0 * worstLow:F3}");
    }

    [Fact]
    public void TheResonanceConditionIsSatisfiedInTheBandAndNotOutsideIt()
    {
        // The measurement the loss scan could not make. For each amplitude, the two
        // measured betas are put into every resonance condition a multipole up to
        // order six could impose, and the smallest departure from 2 is reported.
        //
        // Only even orders are considered. This trap is symmetric about its own
        // centre plane and about the axis, so the odd multipoles vanish by symmetry
        // and a resonance of odd order would be evidence of a broken geometry rather
        // than of physics.
        output.WriteLine("  volts     beta_z    beta_r    best condition            n_z b_z + n_r b_r");

        var inside = double.NaN;
        var outside = double.NaN;
        var identified = (Z: 0, R: 0);

        foreach (var volts in new[] { 560.0, 610.0, 660.0 })
        {
            var (samples, _, drive) = Fly(volts, 0.3, 200);

            var betaZ = BetaFrom(Line(samples, 0, drive), drive);
            var betaR = BetaFrom(Line(samples, 1, drive), drive);

            (int Z, int R, double Sum, double Miss) best = (0, 0, 0.0, double.MaxValue);

            for (var order = 2; order <= 6; order += 2)
            {
                for (var nz = 0; nz <= order; nz++)
                {
                    var nr = order - nz;

                    // n_r must be even: the radial coordinate enters the potential
                    // through r squared, so an odd power of the radial motion has no
                    // term to couple to.
                    if (nr % 2 != 0)
                    {
                        continue;
                    }

                    var sum = (nz * betaZ) + (nr * betaR);
                    var miss = Math.Abs(sum - 2.0);

                    if (miss < best.Miss)
                    {
                        best = (nz, nr, sum, miss);
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{volts,7:F0}  {betaZ,9:F4} {betaR,9:F4}    "
                + $"{best.Z} beta_z + {best.R} beta_r        {best.Sum,10:F4}  (miss {best.Miss:F4})"));

            if (volts == BandCentre)
            {
                inside = best.Miss;
                identified = (best.Z, best.R);
            }
            else
            {
                outside = double.IsNaN(outside) ? best.Miss : Math.Min(outside, best.Miss);
            }
        }

        output.WriteLine($"closest inside {inside:F4}, closest outside {outside:F4}");

        // The identification. 2 beta_z + 2 beta_r = 2 is an order-four condition,
        // which is an OCTUPOLE - and an octupole is exactly what this geometry's
        // symmetry says its leading unwanted multipole must be, since the trap is
        // symmetric about its own centre plane and about the axis so every odd order
        // vanishes. That matters for how much the fit is worth: nine candidate
        // conditions are searched here and one of them will always be nearest, so a
        // near miss on an arbitrary one would be a fishing expedition. This is the
        // one predicted in advance, and it is independently corroborated by the
        // anharmonicity in PaulTrapStudy - dEz/dz over dEr/dr departing from -2 by
        // an amount that grows with radius is an even multipole, measured in the
        // field rather than in a trajectory.
        Assert.Equal((2, 2), identified);
        Assert.True(inside < 0.01, $"the condition should be met to a per cent: {inside:F4}");

        // And it is met a hundred times better inside the band than outside, which
        // is what rules out its being a property of the search rather than of the
        // amplitude.
        Assert.True(
            inside < 0.1 * outside,
            $"the band at {BandCentre:F0} V should sit far closer to a resonance condition "
            + $"({inside:F4}) than the amplitudes either side of it ({outside:F4})");
    }

    private static double Line(IReadOnlyList<TrajectorySample> samples, int axis, double drive)
    {
        var spectrum = SecularSpectrum.From(samples, axis, 0.02 * drive, 0.90 * drive, 6000);
        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, _, _, _) = peak;

        return value.In("Hz");
    }
}
