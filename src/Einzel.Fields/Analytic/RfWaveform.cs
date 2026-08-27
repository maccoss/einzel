namespace Einzel.Fields.Analytic;

/// <summary>
/// The shape of an RF drive over one cycle.
/// </summary>
/// <remarks>
/// <para>
/// A sinusoid is what a resonant circuit produces and what the Mathieu equation
/// describes. A rectangular wave is what a switching supply produces, and it
/// changes the physics rather than merely the engineering: the equation of motion
/// becomes Meissner's rather than Mathieu's, and the stability boundaries move.
/// </para>
/// <para>
/// Modelled as a shape sampled by phase rather than as a field of its own, because
/// the drive and the geometry are independent. The same waveform drives a
/// quadrupole, a trap, or an ion guide, and the same geometry accepts any drive.
/// </para>
/// </remarks>
public abstract record RfWaveform
{
    private RfWaveform()
    {
    }

    /// <summary>The waveform value at a phase, in the range minus one to one.</summary>
    /// <param name="phase">Phase through the cycle, in [0, 1).</param>
    /// <returns>The dimensionless drive value.</returns>
    public abstract double At(double phase);

    /// <summary>
    /// The mean value over a cycle, which acts as a DC component.
    /// </summary>
    /// <remarks>
    /// Zero for anything symmetric. A rectangular wave whose duty cycle is not one
    /// half has a non-zero mean, and that mean does the job a DC supply would -
    /// which is the whole trick of a digital mass filter: resolution without a
    /// second supply, tuned by switching times rather than by volts.
    /// </remarks>
    public abstract double Mean { get; }

    /// <summary>The sinusoid a resonant drive produces. Gives the Mathieu equation.</summary>
    public sealed record Sinusoid : RfWaveform
    {
        /// <inheritdoc/>
        public override double At(double phase) => Math.Cos(2.0 * Math.PI * phase);

        /// <inheritdoc/>
        public override double Mean => 0.0;
    }

    /// <summary>
    /// The rectangular wave a switching drive produces. Gives the Meissner equation.
    /// </summary>
    /// <param name="DutyCycle">
    /// The fraction of the cycle spent at the positive level, in (0, 1). One half
    /// is a symmetric square wave.
    /// </param>
    /// <remarks>
    /// <para>
    /// Switching between two levels rather than sweeping between them, which is
    /// what makes the drive frequency independent of any resonant circuit and the
    /// mass range a matter of switching speed. Schrader, Anderson and Russell
    /// (JASMS 2024) use exactly this on a segmented quadrupole.
    /// </para>
    /// <para>
    /// The duty cycle is the second knob. At one half the wave is balanced and the
    /// working point sits on the a = 0 line; away from one half the mean is
    /// 2d - 1, which enters the equation of motion exactly where a DC offset would
    /// and moves the working point up the stability diagram without a DC supply
    /// existing anywhere in the instrument.
    /// </para>
    /// </remarks>
    public sealed record Rectangular(double DutyCycle) : RfWaveform
    {
        /// <inheritdoc/>
        /// <exception cref="ArgumentOutOfRangeException">The duty cycle is not in (0, 1).</exception>
        public override double At(double phase)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DutyCycle);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(DutyCycle, 1.0);

            // Wrapped rather than assumed in range: a phase arrives from an
            // accumulated flight time and will not be tidy.
            var wrapped = phase - Math.Floor(phase);
            return wrapped < DutyCycle ? 1.0 : -1.0;
        }

        /// <inheritdoc/>
        public override double Mean => (2.0 * DutyCycle) - 1.0;
    }
}
