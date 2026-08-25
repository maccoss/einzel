using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Fields.Analytic;

/// <summary>
/// The ideal two-dimensional quadrupole field, driven.
/// </summary>
/// <remarks>
/// <para>
/// Phi(x, y, t) = (U + V cos(omega t)) (x^2 - y^2) / r0^2, the hyperbolic field a
/// real quadrupole approximates with round rods. Exactly quadrupolar by
/// construction, which is the point: it separates whether the RF <em>path</em> is
/// right from whether a particular set of rods produces a good field, and those
/// are different questions with different failure modes.
/// </para>
/// <para>
/// The equation of motion it produces is the Mathieu equation, whose stability is
/// known in closed form. Spec section 19 calls recovering the a-q diagram "the
/// best single test that the RF path is correct", and this is the field that makes
/// that test about the integrator rather than about a mesh.
/// </para>
/// <para>
/// Two dimensional and infinite along z, which is what a mass filter's
/// cross-section is. An ion drifts along the axis at whatever speed it was given
/// and the transverse motion does not care.
/// </para>
/// </remarks>
public sealed class IdealQuadrupoleRf : ITimeVaryingField
{
    private readonly double _inscribedRadiusSquared;

    private IdealQuadrupoleRf(
        double directVolts,
        double amplitudeVolts,
        double angularFrequency,
        double inscribedRadius,
        RfWaveform waveform)
    {
        DirectVolts = directVolts;
        AmplitudeVolts = amplitudeVolts;
        AngularFrequency = angularFrequency;
        InscribedRadiusM = inscribedRadius;
        Waveform = waveform;
        _inscribedRadiusSquared = inscribedRadius * inscribedRadius;
    }

    /// <summary>The shape of the drive. A sinusoid unless another is given.</summary>
    public RfWaveform Waveform { get; }

    /// <summary>Creates a driven quadrupole.</summary>
    /// <param name="direct">The DC component applied to the x pair.</param>
    /// <param name="amplitude">The RF amplitude, zero to peak.</param>
    /// <param name="frequency">The drive frequency.</param>
    /// <param name="inscribedRadius">Axis to nearest electrode surface, conventionally r0.</param>
    /// <param name="waveform">The drive shape. A sinusoid when omitted.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The frequency or the radius is not positive.</exception>
    public static IdealQuadrupoleRf Create(
        Quantity direct,
        Quantity amplitude,
        Quantity frequency,
        Quantity inscribedRadius,
        RfWaveform? waveform = null)
    {
        var hertz = frequency.In("Hz");
        var radius = inscribedRadius.In("m");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        return new IdealQuadrupoleRf(
            direct.In("V"),
            amplitude.In("V"),
            2.0 * Math.PI * hertz,
            radius,
            waveform ?? new RfWaveform.Sinusoid());
    }

    /// <summary>
    /// Creates the quadrupole that puts an ion at a stated point of the stability
    /// diagram.
    /// </summary>
    /// <param name="a">The Mathieu a parameter, which the DC sets.</param>
    /// <param name="q">The Mathieu q parameter, which the RF sets.</param>
    /// <param name="mass">The ion's mass.</param>
    /// <param name="charge">The ion's charge.</param>
    /// <param name="frequency">The drive frequency.</param>
    /// <param name="inscribedRadius">Axis to nearest electrode surface.</param>
    /// <param name="waveform">The drive shape. A sinusoid when omitted.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The frequency or the radius is not positive.</exception>
    /// <remarks>
    /// <para>
    /// a = 8 z e U / (m omega^2 r0^2) and q = 4 z e V / (m omega^2 r0^2), the
    /// standard mapping. Provided in this direction because the stability diagram
    /// is drawn in a and q and a test that scans them should say so, rather than
    /// scanning volts and converting in a comment.
    /// </para>
    /// <para>
    /// It also puts the mapping in one place. Every published quadrupole result is
    /// quoted in a and q, so an error in the conversion would make every
    /// comparison wrong by the same factor and each one would look self-consistent.
    /// </para>
    /// </remarks>
    public static IdealQuadrupoleRf FromMathieu(
        double a,
        double q,
        Quantity mass,
        Quantity charge,
        Quantity frequency,
        Quantity inscribedRadius,
        RfWaveform? waveform = null)
    {
        var hertz = frequency.In("Hz");
        var radius = inscribedRadius.In("m");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        // Mass and charge rather than an IonSpecies: that type lives in the
        // transport layer, which sits above this one, and a field reaching upward
        // for it would invert the dependency for the sake of a constructor.
        var omega = 2.0 * Math.PI * hertz;
        var scale = mass.In("kg") * omega * omega * radius * radius / Math.Abs(charge.In("C"));

        return new IdealQuadrupoleRf(
            a * scale / 8.0, q * scale / 4.0, omega, radius, waveform ?? new RfWaveform.Sinusoid());
    }

    /// <summary>The DC component on the x pair, in volts.</summary>
    public double DirectVolts { get; }

    /// <summary>The RF amplitude, zero to peak, in volts.</summary>
    public double AmplitudeVolts { get; }

    /// <summary>The angular drive frequency, in radians per second.</summary>
    public double AngularFrequency { get; }

    /// <summary>Axis to nearest electrode surface, in metres.</summary>
    public double InscribedRadiusM { get; }

    /// <inheritdoc/>
    public double ShortestPeriodSeconds => 2.0 * Math.PI / AngularFrequency;

    /// <inheritdoc/>
    /// <remarks>An analytic field is defined everywhere and resolves everything.</remarks>
    public double ResolutionLength => double.PositiveInfinity;

    /// <summary>The potential applied to the x pair at an instant, in volts.</summary>
    /// <param name="timeSeconds">The instant.</param>
    /// <returns>The potential.</returns>
    public double DriveAt(double timeSeconds) =>
        DirectVolts + (AmplitudeVolts * Waveform.At(AngularFrequency * timeSeconds / (2.0 * Math.PI)));

    /// <summary>
    /// The Mathieu a a given drive produces, including any the waveform's own mean
    /// contributes.
    /// </summary>
    /// <param name="mass">The ion's mass.</param>
    /// <param name="charge">The ion's charge.</param>
    /// <returns>The a parameter.</returns>
    /// <remarks>
    /// A rectangular wave off half duty carries a mean of 2d - 1, which enters the
    /// equation of motion exactly where a DC offset would. Reporting it here keeps
    /// the two sources of a in one place: a digital filter with no DC supply still
    /// has an a, and it is set by switching times.
    /// </remarks>
    public double MathieuA(Quantity mass, Quantity charge)
    {
        var scale = mass.In("kg") * AngularFrequency * AngularFrequency
            * InscribedRadiusM * InscribedRadiusM / Math.Abs(charge.In("C"));

        return 8.0 * (DirectVolts + (AmplitudeVolts * Waveform.Mean)) / scale;
    }

    /// <summary>The Mathieu q a given drive produces.</summary>
    /// <param name="mass">The ion's mass.</param>
    /// <param name="charge">The ion's charge.</param>
    /// <returns>The q parameter.</returns>
    public double MathieuQ(Quantity mass, Quantity charge)
    {
        var scale = mass.In("kg") * AngularFrequency * AngularFrequency
            * InscribedRadiusM * InscribedRadiusM / Math.Abs(charge.In("C"));

        return 4.0 * AmplitudeVolts / scale;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        // E = -grad Phi, and Phi = drive (x^2 - y^2) / r0^2.
        var scale = 2.0 * DriveAt(timeSeconds) / _inscribedRadiusSquared;
        return new Vec3(-scale * position.X, scale * position.Y, 0.0);
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        DriveAt(timeSeconds)
        * ((position.X * position.X) - (position.Y * position.Y))
        / _inscribedRadiusSquared;

    /// <inheritdoc/>
    /// <remarks>The instantaneous field at the start of the cycle.</remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>The instantaneous potential at the start of the cycle.</remarks>
    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);
}
