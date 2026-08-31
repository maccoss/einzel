using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Energy drift is a <i>relative</i> quantity, so it needs a scale — and when there is
/// none it must be absent rather than zero.
/// </summary>
/// <remarks>
/// <para>
/// The scale is the ion's total energy at launch. An ion released from rest at a point
/// where the potential is zero has a total energy of exactly zero, so there is nothing to
/// be relative to and the tracker does nothing — which used to leave the reported drift at
/// the <c>0.0</c> it was initialised to, and print
/// <c>energy drift 0.00E+000 relative (ACC-4 budget 1e-6)</c>.
/// </para>
/// <para>
/// <b>That reads as four orders inside budget and means "not measured"</b>, which is the
/// more dangerous of the two. Every at-rest launch here was affected: the accelerating gap,
/// the sequenced extraction, the Paul trap, the rectilinear trap. Absent rather than zero
/// is the rule this project already applies to an undefined Twiss orientation and to a peak
/// width with fewer than two arrivals.
/// </para>
/// </remarks>
public sealed class EnergyDriftScaleTests(ITestOutputHelper output)
{
    private static readonly IonSpecies Peptide = IonSpecies.FromMassToCharge(500.0, 1);

    private static TrajectoryResult Fly(double launchSpeed)
    {
        // A uniform field along x, and a stop plane 100 mm downstream. Static, so nothing
        // here is doing work deliberately — the only question is whether there is a scale.
        var field = UniformField.Create(new Vec3(10000.0, 0.0, 0.0));

        var launch = new PhaseState(Vec3.Zero, new Vec3(launchSpeed, 0.0, 0.0));

        var plane = new Vec3(0.100, 0.0, 0.0);
        var normal = new Vec3(-1.0, 0.0, 0.0);

        return TrajectoryIntegrator.Integrate(
            launch,
            Peptide,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-12,
                MaximumFlightTime = 100e-6,
            },
            (in PhaseState state) => Vec3.Dot(state.Position - plane, normal));
    }

    /// <summary>An ion launched at rest has no energy scale, so no drift is reported.</summary>
    /// <remarks>
    /// <b>NaN rather than 0, and the distinction is the whole point.</b> Zero is the best
    /// possible answer for this quantity, so "not computed" and "perfect" used to print
    /// identically — a reader who sees a blank asks, and a reader who sees the ideal result
    /// stops looking. When a diagnostic's not-computed value coincides with its ideal value
    /// the two have to be separated where the computation is skipped, not left to the
    /// reader.
    /// </remarks>
    [Fact]
    public void AnIonLaunchedAtRestReportsNoDriftRatherThanZeroDrift()
    {
        var result = Fly(launchSpeed: 0.0);

        output.WriteLine(
            $"at rest:   drift {result.MaximumRelativeEnergyDrift}, "
            + $"flight {result.FlightTimeSeconds * 1e6:F6} us");

        Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);

        Assert.True(
            double.IsNaN(result.MaximumRelativeEnergyDrift),
            $"an ion released from rest at zero potential has no energy scale, so the "
            + $"relative drift is undefined - but it reported "
            + $"{result.MaximumRelativeEnergyDrift:E3}, which for this quantity is the best "
            + "possible answer and would stop a reader looking");
    }

    /// <summary>An ion with energy still reports a real, small drift.</summary>
    /// <remarks>
    /// The control, and it is what stops the fix above from being "return NaN always". The
    /// same field, the same integrator, the same stop plane — only the launch differs, and
    /// with a scale to be relative to the diagnostic works and is tiny.
    /// </remarks>
    [Fact]
    public void AnIonWithEnergyStillReportsAMeasuredDrift()
    {
        // 100 eV along the field, so the total energy at launch is non-zero.
        var speed = Math.Sqrt(2.0 * Math.Abs(Peptide.ChargeSi) * 100.0 / Peptide.MassSi);

        var result = Fly(speed);

        output.WriteLine(
            $"100 eV:    drift {result.MaximumRelativeEnergyDrift:E3}, "
            + $"flight {result.FlightTimeSeconds * 1e6:F6} us");

        Assert.False(
            double.IsNaN(result.MaximumRelativeEnergyDrift),
            "an ion with energy has a scale, so the drift is measurable and must be measured");

        Assert.True(
            result.MaximumRelativeEnergyDrift < 1e-9,
            $"a uniform field is conservative and the integrator is exact on it to round-off, "
            + $"so {result.MaximumRelativeEnergyDrift:E3} is too large to be round-off");
    }

    /// <summary>Starting at rest somewhere the potential is not zero still has a scale.</summary>
    /// <remarks>
    /// The sharper half of the control: it is not "at rest" that removes the scale, it is a
    /// total energy of zero. An ion at rest 50 mm into a uniform field sits at −500 V of
    /// potential, so it has 500 eV of energy to be relative to and the drift is measurable —
    /// which is what says the guard keys on the right quantity.
    /// </remarks>
    [Fact]
    public void AtRestIsNotWhatRemovesTheScale()
    {
        var field = UniformField.Create(new Vec3(10000.0, 0.0, 0.0));

        var launch = new PhaseState(new Vec3(0.050, 0.0, 0.0), Vec3.Zero);
        var plane = new Vec3(0.150, 0.0, 0.0);
        var normal = new Vec3(-1.0, 0.0, 0.0);

        var result = TrajectoryIntegrator.Integrate(
            launch,
            Peptide,
            field,
            new IntegrationSettings { RelativeTolerance = 1e-12, MaximumFlightTime = 100e-6 },
            (in PhaseState state) => Vec3.Dot(state.Position - plane, normal));

        output.WriteLine(
            $"at rest at -500 V: drift {result.MaximumRelativeEnergyDrift:E3}");

        Assert.False(
            double.IsNaN(result.MaximumRelativeEnergyDrift),
            "this ion is at rest but not at zero total energy, so it has a scale");

        Assert.True(result.MaximumRelativeEnergyDrift < 1e-9);
    }
}
