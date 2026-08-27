namespace Einzel.Sweeps;

/// <summary>
/// The Nelder-Mead simplex, in the normalised box.
/// </summary>
/// <remarks>
/// <para>
/// A simplex of n+1 vertices crawls downhill by reflecting its worst vertex
/// through the centroid of the rest, expanding when that helps a lot, contracting
/// when it does not help at all, and shrinking toward the best vertex when even
/// the contraction fails. Standard coefficients: reflect 1, expand 2, contract
/// 1/2, shrink 1/2.
/// </para>
/// <para>
/// It is the right first choice for the problems here - a handful of variables,
/// no derivatives, an objective that costs a field solve - and its weakness is
/// well known: on a curved valley the simplex flattens along the ridge and
/// converges to a point that is not a minimum. Restarting from the best vertex
/// with a fresh full-size simplex is the standard remedy and is on by default,
/// because the failure is silent otherwise. A restart that finds nothing better
/// is also the cheapest confirmation available that the point is real.
/// </para>
/// </remarks>
internal static class NelderMead
{
    private const double Reflect = 1.0;
    private const double Expand = 2.0;
    private const double Contract = 0.5;
    private const double Shrink = 0.5;

    /// <summary>The initial simplex edge, as a fraction of the box.</summary>
    /// <remarks>
    /// A quarter of the box. Small enough not to start by evaluating the extremes,
    /// large enough that a search does not spend its first iterations discovering
    /// the objective's scale. The normalised box is what makes one number
    /// defensible across a length, a voltage, and a ratio.
    /// </remarks>
    private const double InitialEdge = 0.25;

    internal static (double[] Best, double[] Spread, int Iterations, bool Converged) Search(SearchProblem problem)
    {
        var n = problem.Dimension;
        var start = problem.Start;
        var iterations = 0;
        var converged = false;
        double[] spread = new double[n];

        for (var attempt = 0; attempt <= problem.Settings.Restarts; attempt++)
        {
            if (problem.BudgetSpent)
            {
                break;
            }

            var (vertices, values) = Build(problem, attempt == 0 ? start : problem.BestPoint);
            var (took, met, finalSpread) = Descend(problem, vertices, values);

            iterations += took;
            spread = finalSpread;
            converged = met;

            // A restart only earns its keep when the previous run stopped because
            // it had converged. If the budget ran out there is nothing to confirm.
            if (!met)
            {
                break;
            }
        }

        return (problem.BestPoint, spread, iterations, converged && !problem.BudgetSpent);
    }

    private static (double[][] Vertices, double[] Values) Build(SearchProblem problem, double[] centre)
    {
        var n = problem.Dimension;
        var vertices = new double[n + 1][];
        var values = new double[n + 1];

        vertices[0] = [.. centre];

        for (var k = 0; k < n; k++)
        {
            var vertex = (double[])centre.Clone();

            // Step toward the middle of the box rather than always upward, so a
            // start near a face does not put the whole simplex outside it.
            vertex[k] += centre[k] > 0.5 ? -InitialEdge : InitialEdge;
            vertices[k + 1] = vertex;
        }

        for (var k = 0; k <= n; k++)
        {
            values[k] = problem.Evaluate(vertices[k]);
        }

        return (vertices, values);
    }

    private static (int Iterations, bool Converged, double[] Spread) Descend(
        SearchProblem problem, double[][] vertices, double[] values)
    {
        var n = problem.Dimension;
        var iterations = 0;

        while (!problem.BudgetSpent)
        {
            Order(vertices, values);

            var spread = Spread(vertices);

            if (spread.Max() <= problem.Settings.ParameterTolerance
                && Converged(values, problem.Settings.ObjectiveTolerance))
            {
                return (iterations, true, spread);
            }

            iterations++;

            var centroid = Centroid(vertices, n);
            var worst = vertices[n];

            var reflected = Combine(centroid, worst, Reflect);
            var reflectedValue = problem.Evaluate(reflected);

            if (reflectedValue < values[0])
            {
                // Better than everything: try going further in the same direction.
                var expanded = Combine(centroid, worst, Expand);
                var expandedValue = problem.Evaluate(expanded);

                Replace(vertices, values, n, expandedValue < reflectedValue ? expanded : reflected,
                    Math.Min(expandedValue, reflectedValue));
            }
            else if (reflectedValue < values[n - 1])
            {
                Replace(vertices, values, n, reflected, reflectedValue);
            }
            else
            {
                // Contract, on whichever side of the centroid is better. Taking the
                // outside contraction when the reflection was an improvement over
                // the worst vertex is what stops the simplex collapsing prematurely.
                var outside = reflectedValue < values[n];
                var contracted = Combine(centroid, outside ? reflected : worst, Contract);
                var contractedValue = problem.Evaluate(contracted);

                if (contractedValue < Math.Min(reflectedValue, values[n]))
                {
                    Replace(vertices, values, n, contracted, contractedValue);
                }
                else
                {
                    ShrinkToward(problem, vertices, values);
                }
            }
        }

        Order(vertices, values);
        return (iterations, false, Spread(vertices));
    }

    private static void Order(double[][] vertices, double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(k => values[k]).ToArray();
        var sortedVertices = order.Select(k => vertices[k]).ToArray();
        var sortedValues = order.Select(k => values[k]).ToArray();

        for (var k = 0; k < values.Length; k++)
        {
            vertices[k] = sortedVertices[k];
            values[k] = sortedValues[k];
        }
    }

    private static double[] Centroid(double[][] vertices, int n)
    {
        var centroid = new double[n];

        for (var k = 0; k < n; k++)
        {
            for (var d = 0; d < n; d++)
            {
                centroid[d] += vertices[k][d];
            }
        }

        for (var d = 0; d < n; d++)
        {
            centroid[d] /= n;
        }

        return centroid;
    }

    private static double[] Combine(double[] centroid, double[] away, double factor)
    {
        var point = new double[centroid.Length];

        for (var d = 0; d < point.Length; d++)
        {
            point[d] = centroid[d] + (factor * (centroid[d] - away[d]));
        }

        return point;
    }

    private static void Replace(double[][] vertices, double[] values, int index, double[] point, double value)
    {
        vertices[index] = point;
        values[index] = value;
    }

    private static void ShrinkToward(SearchProblem problem, double[][] vertices, double[] values)
    {
        var best = vertices[0];

        for (var k = 1; k < vertices.Length; k++)
        {
            for (var d = 0; d < best.Length; d++)
            {
                vertices[k][d] = best[d] + (Shrink * (vertices[k][d] - best[d]));
            }

            values[k] = problem.Evaluate(vertices[k]);
        }
    }

    private static double[] Spread(double[][] vertices)
    {
        var n = vertices[0].Length;
        var spread = new double[n];

        for (var d = 0; d < n; d++)
        {
            var low = double.PositiveInfinity;
            var high = double.NegativeInfinity;

            foreach (var vertex in vertices)
            {
                low = Math.Min(low, vertex[d]);
                high = Math.Max(high, vertex[d]);
            }

            spread[d] = high - low;
        }

        return spread;
    }

    /// <summary>
    /// Whether the objective has stopped varying across the simplex.
    /// </summary>
    /// <remarks>
    /// Relative to the magnitude of the values themselves, with an absolute floor,
    /// so an objective heading for zero - an aberration coefficient being
    /// cancelled, which is the case this was written for - can still converge
    /// rather than chasing a relative tolerance it can never meet.
    /// </remarks>
    private static bool Converged(double[] values, double tolerance)
    {
        var low = values.Min();
        var high = values.Max();
        var scale = Math.Max(Math.Abs(low), Math.Abs(high));

        return high - low <= Math.Max(tolerance * scale, tolerance);
    }
}
