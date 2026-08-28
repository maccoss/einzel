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

    /// <summary>
    /// An arbitrary periodic waveform, as a sum of harmonics of the drive.
    /// </summary>
    /// <param name="Terms">
    /// The harmonics. Order 0 is a constant and contributes only to
    /// <see cref="Mean"/>; order 1 is the fundamental.
    /// </param>
    /// <remarks>
    /// <para>
    /// Spec section 9 lists an arbitrary waveform among the excitations an electrode
    /// may carry, and a Fourier series is not a restriction on that: every periodic
    /// waveform is one. What it buys over a sampled table is that the description is
    /// in the <em>same terms</em> as the thing being designed. The Class B figure
    /// section 12 asks for is isolation efficiency against <strong>notch
    /// width</strong>, and a notch is a statement about a spectrum - written as
    /// harmonics it is a list with a gap in it, and written as samples it is a
    /// waveform someone has to inverse-transform first and then argue about.
    /// </para>
    /// <para>
    /// <strong>Smooth by construction, which matters to the integrator.</strong> A
    /// sampled table is piecewise something, and a discontinuity in the drive is a
    /// discontinuity in the acceleration that a Runge-Kutta step will average across
    /// without complaint - the same reason a rectangular wave needs the
    /// step-per-cycle cap. A finite harmonic sum has no jumps and no corners, so the
    /// error estimator sees what it is being asked to integrate.
    /// </para>
    /// <para>
    /// <strong>The cost is one solve, however many harmonics.</strong> A supply's
    /// potential is a scalar function of time multiplying a fixed spatial pattern,
    /// and a sum of harmonics is still one scalar function of time - so basis
    /// superposition is untouched and an arbitrary waveform costs exactly what a
    /// sinusoid costs.
    /// </para>
    /// </remarks>
    public sealed record Harmonic(IReadOnlyList<HarmonicTerm> Terms) : RfWaveform
    {
        /// <inheritdoc/>
        /// <exception cref="ArgumentException">No terms were given.</exception>
        public override double At(double phase)
        {
            if (Terms is null || Terms.Count == 0)
            {
                throw new ArgumentException(
                    "an arbitrary waveform needs at least one harmonic - an empty series is a "
                    + "drive of zero volts, which is better said by not declaring a drive");
            }

            var total = 0.0;

            foreach (var term in Terms)
            {
                // CosPi rather than Cos of a scaled argument, the same choice the
                // drive decomposition makes: Math.Cos(Math.PI) is -1 to a rounding
                // and a half-turn phase should be exact, or an antiphase term picks
                // up a quadrature component made of round-off.
                total += term.Amplitude * double.CosPi(2.0 * ((term.Order * phase) + term.Phase));
            }

            return total;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Only order zero survives averaging: every other harmonic integrates to
        /// nothing over a cycle by construction. So a DC offset is expressible here
        /// as a term rather than needing a separate supply, exactly as a rectangular
        /// wave's duty cycle expresses one.
        /// </remarks>
        public override double Mean
        {
            get
            {
                var total = 0.0;

                foreach (var term in Terms)
                {
                    if (term.Order == 0)
                    {
                        total += term.Amplitude * double.CosPi(2.0 * term.Phase);
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// A comb of harmonics with a band removed: what isolates one ion and ejects
        /// the rest.
        /// </summary>
        /// <param name="lowOrder">The lowest harmonic in the comb.</param>
        /// <param name="highOrder">The highest, inclusive.</param>
        /// <param name="notchLow">The first order to leave out.</param>
        /// <param name="notchHigh">The last order to leave out, inclusive.</param>
        /// <param name="amplitude">The amplitude of each surviving harmonic.</param>
        /// <returns>The waveform.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The comb is empty, runs backwards, or starts below order one.
        /// </exception>
        /// <exception cref="ArgumentException">The notch removes every harmonic.</exception>
        /// <remarks>
        /// <para>
        /// The excitation a stored-waveform isolation applies: every secular
        /// frequency in a band is driven <em>except</em> the one the ion of interest
        /// sits at, so everything else is resonantly pumped out of the trap and it
        /// is not. The notch width is the design variable, and the trade is the one
        /// section 12 asks to be measured - too narrow and the ion of interest is
        /// excited along with its neighbours, too wide and neighbours survive.
        /// </para>
        /// <para>
        /// Phases are the <strong>Schroeder</strong> quadratic sweep,
        /// <c>φ_k = k² / N</c>, rather than all zero. That is not cosmetic: with
        /// every harmonic in phase the terms add coherently once per cycle and the
        /// waveform is a spike of amplitude <c>N</c> that is nearly zero elsewhere,
        /// so the peak voltage grows with the harmonic count while the useful
        /// excitation does not. The quadratic sweep spreads the energy evenly across
        /// the cycle, and the crest factor stops depending on how many harmonics
        /// there are.
        /// </para>
        /// </remarks>
        public static Harmonic NotchedComb(
            int lowOrder, int highOrder, int notchLow, int notchHigh, double amplitude = 1.0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(lowOrder, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(highOrder, lowOrder);

            var terms = new List<HarmonicTerm>();
            var count = highOrder - lowOrder + 1;

            for (var order = lowOrder; order <= highOrder; order++)
            {
                if (order >= notchLow && order <= notchHigh)
                {
                    continue;
                }

                terms.Add(new HarmonicTerm(order, amplitude, (double)(order * order) / count));
            }

            if (terms.Count == 0)
            {
                throw new ArgumentException(
                    $"the notch [{notchLow}, {notchHigh}] removes every harmonic of the comb "
                    + $"[{lowOrder}, {highOrder}], leaving no excitation at all - which is a "
                    + "waveform of zero rather than a narrow one");
            }

            return new Harmonic(terms);
        }
    }
}

/// <summary>One harmonic of an arbitrary waveform.</summary>
/// <param name="Order">
/// Which multiple of the drive frequency. Zero is a constant offset, one the
/// fundamental.
/// </param>
/// <param name="Amplitude">Its amplitude, relative to the declared drive amplitude.</param>
/// <param name="Phase">
/// Its phase, in turns rather than radians - so a half is exactly antiphase and a
/// quarter exactly quadrature, with no rounding in either.
/// </param>
public readonly record struct HarmonicTerm(int Order, double Amplitude, double Phase);
