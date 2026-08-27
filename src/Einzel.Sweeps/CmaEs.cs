namespace Einzel.Sweeps;

/// <summary>
/// Covariance Matrix Adaptation Evolution Strategy, in the normalised box.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 13's choice for "larger and rougher" problems. Each generation
/// samples a population from a multivariate normal, keeps the better half,
/// and moves the mean toward them while updating the covariance so the
/// distribution stretches along the directions that have been paying off. After a
/// few generations the sampling ellipsoid has aligned itself with the local shape
/// of the objective, which is what lets it follow a curved valley that a simplex
/// crawls along one contraction at a time.
/// </para>
/// <para>
/// Two properties matter more here than the elegance. It is robust to a noisy
/// objective, and every objective in this codebase is noisy: each evaluation ends
/// in a field solve at a finite tolerance, so the objective has numerical grit on
/// it at some level, and a simplex will happily contract onto a grain of it.
/// And it needs no derivatives and no scaling, since the normalised box and the
/// adapted covariance between them supply the scale.
/// </para>
/// <para>
/// The parameters below are Hansen's defaults, unmodified. They are not tuning
/// knobs: the whole claim of the method is that they work across problems without
/// being touched, and changing one without a measurement is how that claim gets
/// quietly broken.
/// </para>
/// </remarks>
internal static class CmaEs
{
    /// <summary>The initial step size, as a fraction of the box.</summary>
    /// <remarks>
    /// A quarter of the box, so the first generation samples most of it without
    /// spending itself on the corners. Hansen's advice is that sigma should be
    /// roughly a quarter of the range the optimum is expected within, and with a
    /// normalised box that is exactly what this is.
    /// </remarks>
    private const double InitialSigma = 0.25;

    internal static (double[] Best, double[] Spread, int Iterations, bool Converged) Search(SearchProblem problem)
    {
        var n = problem.Dimension;
        var settings = problem.Settings;
        var random = new Random(settings.Seed);

        var lambda = 4 + (int)Math.Floor(3.0 * Math.Log(n));
        var mu = lambda / 2;

        // Log-decreasing recombination weights: the best of the selected offspring
        // counts for more than the worst of them.
        var weights = new double[mu];

        for (var k = 0; k < mu; k++)
        {
            weights[k] = Math.Log(mu + 0.5) - Math.Log(k + 1.0);
        }

        var weightSum = weights.Sum();

        for (var k = 0; k < mu; k++)
        {
            weights[k] /= weightSum;
        }

        var muEffective = 1.0 / weights.Sum(w => w * w);

        var cSigma = (muEffective + 2.0) / (n + muEffective + 5.0);
        var dSigma = 1.0 + (2.0 * Math.Max(0.0, Math.Sqrt((muEffective - 1.0) / (n + 1.0)) - 1.0)) + cSigma;
        var cc = (4.0 + (muEffective / n)) / (n + 4.0 + (2.0 * muEffective / n));
        var c1 = 2.0 / (((n + 1.3) * (n + 1.3)) + muEffective);
        var cMu = Math.Min(
            1.0 - c1,
            2.0 * (muEffective - 2.0 + (1.0 / muEffective)) / (((n + 2.0) * (n + 2.0)) + muEffective));

        // Expected length of a standard normal vector, which is what the step-size
        // rule compares the evolution path against.
        var chiN = Math.Sqrt(n) * (1.0 - (1.0 / (4.0 * n)) + (1.0 / (21.0 * n * n)));

        var mean = (double[])problem.Start.Clone();
        var sigma = InitialSigma;

        var pSigma = new double[n];
        var pc = new double[n];
        var covariance = Identity(n);
        var (basis, scales) = Decompose(covariance);

        var generation = 0;
        var converged = false;
        var spread = new double[n];
        Array.Fill(spread, sigma);

        // Refreshing the eigendecomposition every generation is wasteful and every
        // hundred is stale; Hansen's rule ties it to how fast the covariance can
        // actually change.
        var refreshEvery = Math.Max(1, (int)(1.0 / (10.0 * n * (c1 + cMu))));

        while (!problem.BudgetSpent)
        {
            var offspring = new double[lambda][];
            var steps = new double[lambda][];
            var values = new double[lambda];

            for (var k = 0; k < lambda; k++)
            {
                var z = new double[n];

                for (var d = 0; d < n; d++)
                {
                    z[d] = Gaussian(random);
                }

                // y = B D z: a sample from the current ellipsoid, before sigma.
                var y = new double[n];

                for (var d = 0; d < n; d++)
                {
                    var sum = 0.0;

                    for (var e = 0; e < n; e++)
                    {
                        sum += basis[d][e] * scales[e] * z[e];
                    }

                    y[d] = sum;
                }

                var point = new double[n];

                for (var d = 0; d < n; d++)
                {
                    point[d] = mean[d] + (sigma * y[d]);
                }

                steps[k] = y;
                offspring[k] = point;
                values[k] = problem.Evaluate(point);
            }

            generation++;

            var order = Enumerable.Range(0, lambda).OrderBy(k => values[k]).ToArray();

            // Where the mean moves to, and the same move expressed in the
            // pre-sigma coordinates the paths are accumulated in.
            var previousMean = (double[])mean.Clone();
            var meanStep = new double[n];

            for (var k = 0; k < mu; k++)
            {
                for (var d = 0; d < n; d++)
                {
                    meanStep[d] += weights[k] * steps[order[k]][d];
                }
            }

            for (var d = 0; d < n; d++)
            {
                mean[d] = previousMean[d] + (sigma * meanStep[d]);
            }

            // The conjugate evolution path, in the sphered coordinates C^(-1/2)
            // maps onto: this is what makes the step-size rule independent of the
            // covariance the search has learned.
            var sphered = ApplyInverseSqrt(basis, scales, meanStep);
            var cSigmaFactor = Math.Sqrt(cSigma * (2.0 - cSigma) * muEffective);

            for (var d = 0; d < n; d++)
            {
                pSigma[d] = ((1.0 - cSigma) * pSigma[d]) + (cSigmaFactor * sphered[d]);
            }

            var pSigmaNorm = Math.Sqrt(pSigma.Sum(v => v * v));

            // Hansen's hsig. When the conjugate path is unusually long the search
            // is travelling in a consistent direction and sigma is about to grow;
            // feeding the rank-one path at the same time would let sigma and the
            // covariance inflate each other, so the path update is held back for
            // that generation and the covariance decay is corrected to compensate.
            var pathIsShortEnough = pSigmaNorm
                / Math.Sqrt(1.0 - Math.Pow(1.0 - cSigma, 2.0 * (generation + 1)))
                / chiN < 1.4 + (2.0 / (n + 1.0));

            var ccFactor = Math.Sqrt(cc * (2.0 - cc) * muEffective);

            for (var d = 0; d < n; d++)
            {
                pc[d] = ((1.0 - cc) * pc[d]) + (pathIsShortEnough ? ccFactor * meanStep[d] : 0.0);
            }

            var correction = pathIsShortEnough ? 0.0 : c1 * cc * (2.0 - cc);

            for (var a = 0; a < n; a++)
            {
                for (var b = 0; b < n; b++)
                {
                    var rankMu = 0.0;

                    for (var k = 0; k < mu; k++)
                    {
                        rankMu += weights[k] * steps[order[k]][a] * steps[order[k]][b];
                    }

                    covariance[a][b] = ((1.0 - c1 - cMu + correction) * covariance[a][b])
                        + (c1 * pc[a] * pc[b])
                        + (cMu * rankMu);
                }
            }

            sigma *= Math.Exp(cSigma / dSigma * ((pSigmaNorm / chiN) - 1.0));

            if (generation % refreshEvery == 0)
            {
                Symmetrise(covariance);
                (basis, scales) = Decompose(covariance);
            }

            // The population's own spread, which is what the result reports as the
            // sharpness of the optimum.
            for (var d = 0; d < n; d++)
            {
                var low = double.PositiveInfinity;
                var high = double.NegativeInfinity;

                for (var k = 0; k < mu; k++)
                {
                    low = Math.Min(low, offspring[order[k]][d]);
                    high = Math.Max(high, offspring[order[k]][d]);
                }

                spread[d] = high - low;
            }

            var best = values[order[0]];
            var worst = values[order[mu - 1]];
            var scale = Math.Max(Math.Abs(best), Math.Abs(worst));

            if (spread.Max() <= problem.Settings.ParameterTolerance
                && worst - best <= Math.Max(problem.Settings.ObjectiveTolerance * scale,
                    problem.Settings.ObjectiveTolerance))
            {
                converged = true;
                break;
            }

            if (!double.IsFinite(sigma) || sigma <= 0.0)
            {
                // The step size has collapsed or blown up; either way the search
                // has stopped being one and there is nothing to gain by continuing.
                break;
            }
        }

        return (problem.BestPoint, spread, generation, converged && !problem.BudgetSpent);
    }

    private static double[][] Identity(int n)
    {
        var matrix = new double[n][];

        for (var d = 0; d < n; d++)
        {
            matrix[d] = new double[n];
            matrix[d][d] = 1.0;
        }

        return matrix;
    }

    private static void Symmetrise(double[][] matrix)
    {
        for (var a = 0; a < matrix.Length; a++)
        {
            for (var b = a + 1; b < matrix.Length; b++)
            {
                var mean = 0.5 * (matrix[a][b] + matrix[b][a]);
                matrix[a][b] = mean;
                matrix[b][a] = mean;
            }
        }
    }

    private static double[] ApplyInverseSqrt(double[][] basis, double[] scales, double[] vector)
    {
        var n = vector.Length;

        // B^T v, scaled by 1/D, then B back: the sphering transform.
        var rotated = new double[n];

        for (var e = 0; e < n; e++)
        {
            var sum = 0.0;

            for (var d = 0; d < n; d++)
            {
                sum += basis[d][e] * vector[d];
            }

            rotated[e] = sum / scales[e];
        }

        var result = new double[n];

        for (var d = 0; d < n; d++)
        {
            var sum = 0.0;

            for (var e = 0; e < n; e++)
            {
                sum += basis[d][e] * rotated[e];
            }

            result[d] = sum;
        }

        return result;
    }

    /// <summary>
    /// Eigendecomposition of a symmetric matrix by cyclic Jacobi rotations.
    /// </summary>
    /// <remarks>
    /// Jacobi rather than anything faster because these matrices are small - one
    /// row per design variable - and it is short enough to be obviously correct.
    /// It is also unconditionally stable for symmetric input, which matters when
    /// the covariance has become nearly singular along a direction the search has
    /// stopped exploring.
    /// </remarks>
    private static (double[][] Basis, double[] Scales) Decompose(double[][] matrix)
    {
        var n = matrix.Length;
        var a = matrix.Select(row => (double[])row.Clone()).ToArray();
        var basis = Identity(n);

        for (var sweep = 0; sweep < 100; sweep++)
        {
            var offDiagonal = 0.0;

            for (var p = 0; p < n; p++)
            {
                for (var q = p + 1; q < n; q++)
                {
                    offDiagonal += a[p][q] * a[p][q];
                }
            }

            if (offDiagonal <= 1e-30)
            {
                break;
            }

            for (var p = 0; p < n; p++)
            {
                for (var q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p][q]) < 1e-300)
                    {
                        continue;
                    }

                    var theta = (a[q][q] - a[p][p]) / (2.0 * a[p][q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1.0));

                    if (theta == 0.0)
                    {
                        t = 1.0;
                    }

                    var c = 1.0 / Math.Sqrt((t * t) + 1.0);
                    var s = t * c;

                    for (var k = 0; k < n; k++)
                    {
                        var akp = a[k][p];
                        var akq = a[k][q];
                        a[k][p] = (c * akp) - (s * akq);
                        a[k][q] = (s * akp) + (c * akq);
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var apk = a[p][k];
                        var aqk = a[q][k];
                        a[p][k] = (c * apk) - (s * aqk);
                        a[q][k] = (s * apk) + (c * aqk);
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var bkp = basis[k][p];
                        var bkq = basis[k][q];
                        basis[k][p] = (c * bkp) - (s * bkq);
                        basis[k][q] = (s * bkp) + (c * bkq);
                    }
                }
            }
        }

        var scales = new double[n];

        for (var d = 0; d < n; d++)
        {
            // A covariance eigenvalue that has gone non-positive is numerical
            // noise, not a direction of negative variance. Flooring it keeps the
            // sampling well defined without pretending the direction is alive.
            scales[d] = Math.Sqrt(Math.Max(a[d][d], 1e-20));
        }

        return (basis, scales);
    }

    /// <summary>A standard normal deviate, by the polar Box-Muller method.</summary>
    private static double Gaussian(Random random)
    {
        double u, v, s;

        do
        {
            u = (2.0 * random.NextDouble()) - 1.0;
            v = (2.0 * random.NextDouble()) - 1.0;
            s = (u * u) + (v * v);
        }
        while (s is <= 0.0 or >= 1.0);

        return u * Math.Sqrt(-2.0 * Math.Log(s) / s);
    }
}
