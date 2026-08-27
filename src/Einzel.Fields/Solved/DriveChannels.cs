namespace Einzel.Fields.Solved;

/// <summary>What one electrode is asked to hold, over time.</summary>
/// <param name="Name">The electrode's name.</param>
/// <param name="Direct">Its DC potential, in volts.</param>
/// <param name="Amplitude">Its share of the drive, zero to peak, in volts.</param>
/// <param name="Phase">Where in the cycle it sits, as a fraction of one.</param>
public readonly record struct Excitation(string Name, double Direct, double Amplitude, double Phase);

/// <summary>One solved basis, and the time-varying weight it is multiplied by.</summary>
/// <param name="Pattern">
/// Electrode name to the relative potential it holds in this solve, normalised so
/// the leading non-zero entry is one.
/// </param>
/// <param name="Direct">The constant part of the weight, in volts.</param>
/// <param name="Harmonics">Amplitude and phase of each drive term in the weight.</param>
public sealed record DriveChannel(
    Dictionary<string, double> Pattern,
    double Direct,
    List<(double Amplitude, double Phase)> Harmonics);

/// <summary>
/// Reduces a driven geometry to the fewest solves that can express it.
/// </summary>
/// <remarks>
/// <para>
/// Two steps, and the order matters. First every electrode's potential is split
/// into the supplies feeding it: a constant one, and one per distinct drive phase.
/// A funnel's two hundred rings reach three supplies - a DC chain and two RF phases
/// - however many distinct voltages the chain holds, because what makes a supply
/// one supply is that every electrode on it moves <em>together</em>, not that they
/// move to the same place.
/// </para>
/// <para>
/// Then supplies are grouped by their <em>spatial pattern</em> rather than by their
/// time dependence. A quadrupole run with DC has two supplies - a steady one and an
/// oscillating one - but both put the x pair up and the y pair down by the same
/// relative amounts, so they are the same solved field with two weights, and the
/// whole filter is one solve.
/// </para>
/// <para>
/// <b>A sinusoid is a special case, and it is the one that makes a travelling wave
/// affordable.</b> A cos(2 pi (f t - phi)) is exactly
/// A cos(2 pi phi) cos(2 pi f t) + A sin(2 pi phi) sin(2 pi f t) — a fixed pair of
/// time functions with constant coefficients. So however many distinct phases a
/// structure carries, a sinusoidal drive reaches exactly <em>two</em> supplies. A
/// sixty-ring travelling-wave guide with sixty distinct phases is two solves, not
/// sixty.
/// </para>
/// <para>
/// It holds only for a sinusoid. A rectangular wave shifted by a quarter cycle is
/// not a combination of the unshifted wave and one shifted by a quarter, so there
/// each distinct phase really is its own supply and the solve count says so.
/// </para>
/// <para>
/// Written over names and numbers rather than over an electrode type, so the same
/// decomposition serves two dimensions and three. Nothing about it is dimensional:
/// what a channel is depends on how electrodes are wired, not on where they are.
/// </para>
/// </remarks>
public static class DriveChannels
{
    /// <summary>Groups excitations into the channels a solve needs.</summary>
    /// <param name="excitations">What each electrode holds, in declaration order.</param>
    /// <param name="quadrature">
    /// Whether the drive is a sinusoid, in which case every phase resolves into two
    /// fixed components rather than into a supply of its own.
    /// </param>
    /// <returns>The channels, each with a pattern and a weight.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="excitations"/> is null.</exception>
    public static List<DriveChannel> Decompose(
        IReadOnlyList<Excitation> excitations, bool quadrature = false)
    {
        ArgumentNullException.ThrowIfNull(excitations);

        var supplies = new List<Supply>();

        Supply Reach(double direct, double amplitude, double phase)
        {
            var existing = supplies.FirstOrDefault(t =>
                t.Direct == direct && t.Amplitude == amplitude && t.Phase == phase);

            if (existing is null)
            {
                existing = new Supply([], direct, amplitude, phase);
                supplies.Add(existing);
            }

            return existing;
        }

        foreach (var excitation in excitations)
        {
            // The constant supply reaches every electrode that holds a DC
            // potential, each at its own volts. One supply, however many voltages.
            if (excitation.Direct != 0.0)
            {
                Reach(1.0, 0.0, 0.0).Coefficients[excitation.Name] = excitation.Direct;
            }

            if (excitation.Amplitude == 0.0)
            {
                continue;
            }

            var phase = excitation.Phase - Math.Floor(excitation.Phase);

            if (!quadrature)
            {
                Reach(0.0, 1.0, phase).Coefficients[excitation.Name] = excitation.Amplitude;
                continue;
            }

            // CosPi and SinPi rather than Cos and Sin of a scaled argument, because
            // they are exact at the quarter turns. Math.Sin(Math.PI) is 1.2e-16, not
            // zero, and an antiphase electrode would otherwise acquire a quadrature
            // component made entirely of round-off - which becomes a spurious third
            // channel carrying a field of nothing.
            var inPhase = excitation.Amplitude * double.CosPi(2.0 * phase);
            var outOfPhase = excitation.Amplitude * double.SinPi(2.0 * phase);

            if (inPhase != 0.0)
            {
                Reach(0.0, 1.0, 0.0).Coefficients[excitation.Name] = inPhase;
            }

            if (outOfPhase != 0.0)
            {
                // A quarter cycle late: cos(2 pi (f t - 1/4)) is sin(2 pi f t).
                Reach(0.0, 1.0, 0.25).Coefficients[excitation.Name] = outOfPhase;
            }
        }

        var channels = new List<DriveChannel>();

        foreach (var supply in supplies)
        {
            var (pattern, scale) = Normalise(supply.Coefficients, excitations);

            // Exact comparison. Two supplies share a solve when their applied
            // potentials are exactly proportional, which is what a real instrument
            // produces because the electrodes are the same metal in the same places.
            // A tolerance would merge two shapes that were meant to differ, and the
            // field would be plausible.
            var existing = channels.FirstOrDefault(c => SamePattern(c.Pattern, pattern));

            if (existing is null)
            {
                existing = new DriveChannel(pattern, 0.0, []);
                channels.Add(existing);
            }

            var index = channels.IndexOf(existing);

            channels[index] = supply.Amplitude == 0.0
                ? existing with { Direct = existing.Direct + (scale * supply.Direct) }
                : existing with
                {
                    Harmonics = [.. existing.Harmonics, (scale * supply.Amplitude, supply.Phase)],
                };
        }

        // A geometry whose every electrode is grounded still needs one solve, or
        // there is no field object to return at all.
        if (channels.Count == 0)
        {
            channels.Add(new DriveChannel([], 0.0, []));
        }

        return channels;
    }

    /// <summary>The weight each already-solved channel carries for a given set of excitations.</summary>
    /// <param name="channels">The channels the whole sequence was decomposed into.</param>
    /// <param name="excitations">What each electrode holds during this stage.</param>
    /// <param name="quadrature">
    /// Whether the drive is a sinusoid. Must match what <see cref="Decompose"/> was
    /// given, or the patterns this looks up were built a different way and none of
    /// them will be found.
    /// </param>
    /// <returns>The constant and oscillating parts of each channel's weight.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static (List<double> Direct, List<IReadOnlyList<(double Amplitude, double Phase)>> Harmonics)
        Weigh(
            List<DriveChannel> channels,
            IReadOnlyList<Excitation> excitations,
            bool quadrature = false)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(excitations);

        var direct = new List<double>(new double[channels.Count]);

        var harmonics = new List<IReadOnlyList<(double Amplitude, double Phase)>>(
            Enumerable.Range(0, channels.Count)
                .Select(_ => (IReadOnlyList<(double Amplitude, double Phase)>)[]));

        foreach (var group in Decompose(excitations, quadrature))
        {
            var index = channels.FindIndex(c => SamePattern(c.Pattern, group.Pattern));

            if (index < 0)
            {
                // Cannot happen: the patterns were gathered from every stage. Left
                // to contribute nothing rather than throwing, because a stage that
                // silently lost a supply is a defect worth finding in a test rather
                // than an exception in a user's run.
                continue;
            }

            direct[index] += group.Direct;
            harmonics[index] = [.. harmonics[index], .. group.Harmonics];
        }

        return (direct, harmonics);
    }

    private sealed record Supply(
        Dictionary<string, double> Coefficients, double Direct, double Amplitude, double Phase);

    /// <summary>
    /// Scales a supply's potentials so the leading non-zero one is exactly one, and
    /// returns the factor taken out.
    /// </summary>
    /// <remarks>
    /// Leading rather than largest, and in the order the document declared, so the
    /// same shape always normalises the same way. Dividing a value by itself is
    /// exactly one in floating point, which is what lets two supplies that really
    /// are proportional compare equal without a tolerance.
    /// </remarks>
    private static (Dictionary<string, double> Pattern, double Scale) Normalise(
        Dictionary<string, double> coefficients, IReadOnlyList<Excitation> order)
    {
        var leading = 0.0;

        foreach (var excitation in order)
        {
            if (coefficients.TryGetValue(excitation.Name, out var value) && value != 0.0)
            {
                leading = value;
                break;
            }
        }

        if (leading == 0.0)
        {
            return (coefficients, 1.0);
        }

        var pattern = new Dictionary<string, double>(coefficients.Count, StringComparer.Ordinal);

        foreach (var (name, value) in coefficients)
        {
            pattern[name] = value / leading;
        }

        return (pattern, leading);
    }

    private static bool SamePattern(Dictionary<string, double> left, Dictionary<string, double> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }
}
