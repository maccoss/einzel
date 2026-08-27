namespace Einzel.Analysis;

/// <summary>
/// The energy-focusing behaviour of an analyzer, as the coefficients of its
/// flight time expanded in fractional energy offset.
/// </summary>
/// <param name="Coefficients">
/// The relative coefficients c1, c2, c3 ... in
/// T(d)/T0 = 1 + c1 d + c2 d^2 + c3 d^3 + ..., where d is the fractional energy
/// offset from nominal.
/// </param>
/// <param name="BindingOrder">
/// The lowest order whose coefficient has not been cancelled by the design. One
/// for an uncompensated analyzer, two for a single-stage mirror at its focus,
/// three for a two-stage mirror at second-order focus.
/// </param>
/// <param name="NominalFlightTime">Flight time at nominal energy, in seconds.</param>
/// <param name="ResidualOfFit">
/// Root-mean-square residual of the fit, relative to the nominal flight time.
/// Large means the expansion did not capture the behaviour and the reported
/// order should not be believed.
/// </param>
public sealed record FocusingOrder(
    IReadOnlyList<double> Coefficients,
    int BindingOrder,
    double NominalFlightTime,
    double ResidualOfFit);

/// <summary>
/// Recovers the focusing order of an analyzer from an energy scan.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 12 asks for "time-of-flight focusing order: coefficients reported
/// so the binding aberration order is visible". Visible is the operative word. A
/// resolving power on its own says an analyzer is good or bad; the coefficients
/// say which term is responsible, and therefore whether the fix is a longer
/// flight path, a different mirror, or a narrower energy acceptance.
/// </para>
/// <para>
/// The distinction has direct consequences for the companion memo's analyzer. A
/// single-stage mirror at its first-order focus has c1 = 0 and c2 of order one
/// half, so its resolving power falls as the square of the energy spread; a
/// two-stage mirror cancels c2 as well, and its resolving power falls only as the
/// cube. Across the plus or minus 3 to 5 percent acceptance the memo asks for,
/// that is the difference between reaching 20,000 and falling well short of it.
/// </para>
/// </remarks>
public static class FocusingAnalysis
{
    /// <summary>Fits the flight time against fractional energy offset.</summary>
    /// <param name="samples">
    /// Energy offset and flight time pairs. Must include a point at or near zero
    /// offset and span both signs.
    /// </param>
    /// <param name="maximumOrder">Highest power to fit. Three is usually enough.</param>
    /// <param name="cancellationThreshold">
    /// A coefficient smaller than this is treated as cancelled by design rather
    /// than merely small.
    /// </param>
    /// <returns>The focusing coefficients and the binding order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    /// <exception cref="ArgumentException">Too few samples to fit the requested order.</exception>
    public static FocusingOrder Fit(
        IReadOnlyList<(double EnergyFraction, double FlightTime)> samples,
        int maximumOrder = 3,
        double cancellationThreshold = 1e-3)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumOrder, 1);

        if (samples.Count < maximumOrder + 1)
        {
            throw new ArgumentException(
                $"fitting to order {maximumOrder} needs at least {maximumOrder + 1} samples, "
                + $"but {samples.Count} were supplied",
                nameof(samples));
        }

        // The nominal is the sample closest to zero offset.
        var nominal = samples.MinBy(s => Math.Abs(s.EnergyFraction));
        var t0 = nominal.FlightTime;

        if (t0 <= 0.0)
        {
            throw new ArgumentException("the nominal flight time must be positive", nameof(samples));
        }

        // Least squares on the relative departure, with no constant term: the
        // curve passes through (0, 0) by construction.
        var rows = samples.Count;
        var columns = maximumOrder;

        var design = new double[rows, columns];
        var target = new double[rows];

        for (var r = 0; r < rows; r++)
        {
            var delta = samples[r].EnergyFraction;
            target[r] = (samples[r].FlightTime / t0) - 1.0;

            var power = delta;

            for (var c = 0; c < columns; c++)
            {
                design[r, c] = power;
                power *= delta;
            }
        }

        var coefficients = SolveLeastSquares(design, target, rows, columns);

        var residual = 0.0;

        for (var r = 0; r < rows; r++)
        {
            var predicted = 0.0;
            var power = samples[r].EnergyFraction;

            for (var c = 0; c < columns; c++)
            {
                predicted += coefficients[c] * power;
                power *= samples[r].EnergyFraction;
            }

            var difference = predicted - target[r];
            residual += difference * difference;
        }

        residual = Math.Sqrt(residual / rows);

        var binding = columns + 1;

        for (var c = 0; c < columns; c++)
        {
            if (Math.Abs(coefficients[c]) > cancellationThreshold)
            {
                binding = c + 1;
                break;
            }
        }

        return new FocusingOrder(coefficients, binding, t0, residual);
    }

    /// <summary>
    /// Normal equations with Gaussian elimination and partial pivoting.
    /// </summary>
    /// <remarks>
    /// The normal equations square the condition number, which for a Vandermonde
    /// design is already poor. It is adequate here because the fit is to third
    /// order over offsets of a few percent, where the columns are well separated;
    /// a fit to higher order, or over a narrower span, would want a QR
    /// factorisation instead.
    /// </remarks>
    private static double[] SolveLeastSquares(double[,] design, double[] target, int rows, int columns)
    {
        var normal = new double[columns, columns + 1];

        for (var i = 0; i < columns; i++)
        {
            for (var j = 0; j < columns; j++)
            {
                var sum = 0.0;

                for (var r = 0; r < rows; r++)
                {
                    sum += design[r, i] * design[r, j];
                }

                normal[i, j] = sum;
            }

            var rhs = 0.0;

            for (var r = 0; r < rows; r++)
            {
                rhs += design[r, i] * target[r];
            }

            normal[i, columns] = rhs;
        }

        for (var pivot = 0; pivot < columns; pivot++)
        {
            var best = pivot;

            for (var r = pivot + 1; r < columns; r++)
            {
                if (Math.Abs(normal[r, pivot]) > Math.Abs(normal[best, pivot]))
                {
                    best = r;
                }
            }

            if (best != pivot)
            {
                for (var c = 0; c <= columns; c++)
                {
                    (normal[pivot, c], normal[best, c]) = (normal[best, c], normal[pivot, c]);
                }
            }

            var diagonal = normal[pivot, pivot];

            if (Math.Abs(diagonal) < 1e-300)
            {
                continue;
            }

            for (var r = pivot + 1; r < columns; r++)
            {
                var factor = normal[r, pivot] / diagonal;

                for (var c = pivot; c <= columns; c++)
                {
                    normal[r, c] -= factor * normal[pivot, c];
                }
            }
        }

        var solution = new double[columns];

        for (var r = columns - 1; r >= 0; r--)
        {
            var sum = normal[r, columns];

            for (var c = r + 1; c < columns; c++)
            {
                sum -= normal[r, c] * solution[c];
            }

            solution[r] = Math.Abs(normal[r, r]) < 1e-300 ? 0.0 : sum / normal[r, r];
        }

        return solution;
    }
}
