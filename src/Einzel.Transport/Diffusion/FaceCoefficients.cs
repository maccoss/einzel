using Einzel.Fields.Solved;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// The Scharfetter-Gummel flux written as a matrix, assembled once for a whole run.
/// </summary>
/// <remarks>
/// <para>
/// The flux out of a cell across one face is linear in the two densities it joins:
/// <c>J = a n_here - b n_there</c>, with <c>a</c> and <c>b</c> both non-negative
/// because the Bernoulli function is positive everywhere. Everything that decides
/// those two numbers - the mesh, the mobility, the field, the gas, the face weight -
/// is fixed for the whole run, so they are computed once here rather than twice per
/// face per step.
/// </para>
/// <para>
/// <b>That the coefficients are non-negative is the load-bearing property, not the
/// speed.</b> It makes the backward-Euler matrix
/// <c>(1 + dt sum a) n' = n + dt sum b n'</c> an M-matrix, so a Gauss-Seidel sweep is
/// a non-negative combination of non-negative numbers and <b>a partially converged
/// implicit solve is still a non-negative density</b>. A scheme that could go negative
/// halfway to convergence would be unusable however stable it was, because a negative
/// density is a quantity that has stopped meaning anything.
/// </para>
/// <para>
/// Assembling them also removes two <c>exp</c> calls per face from the time loop,
/// which the explicit path was paying once per step - and the driven funnel this was
/// built for takes on the order of a million of them.
/// </para>
/// </remarks>
public sealed class FaceCoefficients
{
    /// <summary>How many faces a cell has: +x, -x, +y, -y.</summary>
    public const int Faces = 4;

    private readonly double[] _scale;
    private readonly double[] _weight;
    private readonly double[] _here;
    private readonly double[] _there;
    private readonly double[] _outward;
    private readonly bool[] _leaving;
    private readonly bool[] _collects;
    private readonly string?[] _names;
    private readonly int _countX;

    private FaceCoefficients(int cells, int countX)
    {
        _scale = new double[cells * Faces];
        _weight = new double[cells * Faces];
        _here = new double[cells * Faces];
        _there = new double[cells * Faces];
        _outward = new double[cells];
        _leaving = new bool[cells * Faces];
        _collects = new bool[cells * Faces];
        _names = new string?[cells * Faces];
        _countX = countX;
    }

    /// <summary>Cell index offsets for each face, as (di, dj).</summary>
    /// <remarks>
    /// Ordered +x, -x, +y, -y, which is the order the explicit path already summed
    /// them in. Keeping it is what lets the rewrite be checked by asserting the answer
    /// is bit-identical: floating-point addition is not associative, so a different
    /// order is a different number and the check would be against a moving target.
    /// </remarks>
    public static (int Di, int Dj)[] Offsets => [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// The flux across one face, as the exact expression the explicit path used
    /// before this operator existed.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <param name="here">This cell's density.</param>
    /// <param name="there">The neighbour's density, or zero across a leaving face.</param>
    /// <returns>The outward flux per unit volume, in reciprocal seconds times density.</returns>
    /// <remarks>
    /// <b>The factored form is kept deliberately.</b> Storing the two products
    /// <c>scale*B(-P)</c> and <c>scale*B(P)</c> and subtracting them is a different
    /// number from scaling the difference, because floating-point multiplication does
    /// not distribute exactly - and this is a refactor of the code that carries every
    /// validated diffusion figure here. Keeping the association means the rewrite can
    /// be checked by asserting the answer is <em>bit-identical</em> rather than close,
    /// which is the only check with real teeth on a change that is supposed to alter
    /// nothing.
    /// </remarks>
    public double Flux(int cell, int face, double here, double there)
    {
        var f = (cell * Faces) + face;

        return _weight[f] * (_scale[f] * ((_here[f] * here) - (_there[f] * there)));
    }

    /// <summary>The coefficient on this cell's own density, across one face.</summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <returns>A non-negative rate, in reciprocal seconds.</returns>
    public double Out(int cell, int face) => Flux(cell, face, 1.0, 0.0);

    /// <summary>The coefficient on the neighbour's density, across one face.</summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <returns>A non-negative rate, in reciprocal seconds.</returns>
    public double In(int cell, int face) => -Flux(cell, face, 0.0, 1.0);

    /// <summary>The sum of <see cref="Out"/> over a cell's faces.</summary>
    /// <param name="cell">The cell index.</param>
    /// <returns>A non-negative rate, in reciprocal seconds.</returns>
    /// <remarks>
    /// The diagonal of the operator, and the quantity both time steppers need: the
    /// explicit one is stable while <c>dt</c> times this is below one, and the
    /// implicit one divides by <c>1 + dt</c> times it.
    /// </remarks>
    public double Outward(int cell) => _outward[cell];

    /// <summary>Whether a face leads out of the domain or into a conductor.</summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <returns>True where what crosses is lost rather than delivered to a neighbour.</returns>
    /// <remarks>
    /// A leaving face has <see cref="In"/> of zero by construction - there is no
    /// density beyond an open edge and none inside metal - so it needs no separate
    /// treatment in the stepper. It is flagged only so the ledger knows which flux to
    /// attribute to a surface.
    /// </remarks>
    public bool Leaves(int cell, int face) => _leaving[(cell * Faces) + face];

    /// <summary>Where a leaving face's ions are counted.</summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <returns>The surface or edge name, or null where the face keeps its ions.</returns>
    public string? NameOf(int cell, int face) => _names[(cell * Faces) + face];

    /// <summary>Whether a leaving face's ions count as collected rather than lost.</summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <returns>True at a collecting edge.</returns>
    public bool Collects(int cell, int face) => _collects[(cell * Faces) + face];

    /// <summary>Assembles the operator for one run.</summary>
    /// <param name="density">The density field, for the mesh and its cell volumes.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="driftX">Drift along x at each node, in metres per second.</param>
    /// <param name="driftY">Drift along y at each node, in metres per second.</param>
    /// <param name="gasX">Gas velocity along x at each node, in metres per second.</param>
    /// <param name="gasY">Gas velocity along y at each node, in metres per second.</param>
    /// <param name="diffusion">The diffusion coefficient at each node, in m^2/s.</param>
    /// <param name="potential">The potential at each node, in volts.</param>
    /// <param name="thermal">The thermal voltage, signed by the charge.</param>
    /// <param name="edges">What happens at each edge of the domain.</param>
    /// <param name="absorbers">Cells inside a conductor.</param>
    /// <returns>The assembled operator.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    public static FaceCoefficients Assemble(
        DensityField density,
        Grid2D grid,
        double[] driftX,
        double[] driftY,
        double[] gasX,
        double[] gasY,
        double[] diffusion,
        double[] potential,
        double thermal,
        DriftDiffusion.DomainEdges edges,
        AbsorbingCells absorbers)
    {
        ArgumentNullException.ThrowIfNull(density);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(driftX);
        ArgumentNullException.ThrowIfNull(driftY);
        ArgumentNullException.ThrowIfNull(gasX);
        ArgumentNullException.ThrowIfNull(gasY);
        ArgumentNullException.ThrowIfNull(diffusion);
        ArgumentNullException.ThrowIfNull(potential);
        ArgumentNullException.ThrowIfNull(absorbers);

        var cells = grid.CountX * grid.CountY;

        var built = new FaceCoefficients(cells, grid.CountX);

        var offsets = Offsets;

        var faceEdges = new[] { edges.MaxX, edges.MinX, edges.MaxY, edges.MinY };
        var faceNames = new[] { "maxX", "minX", "maxY", "minY" };

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var cell = (j * grid.CountX) + i;

                if (absorbers.Blocks(cell))
                {
                    // A cell inside metal holds nothing and is never stepped, so it
                    // gets no coefficients rather than zero ones - the stepper skips
                    // it and the distinction never has to be re-derived.
                    continue;
                }

                var outward = 0.0;

                for (var face = 0; face < Faces; face++)
                {
                    var (di, dj) = offsets[face];

                    var spacing = di != 0 ? grid.SpacingX : grid.SpacingY;

                    var weight = dj == 0 ? 1.0 : density.RadialFaceWeight(j, dj);

                    var (scale, bHere, bThere, leaves, name, collects) = Face(
                        grid, driftX, driftY, gasX, gasY, diffusion, potential, thermal,
                        i, j, i + di, j + dj, di != 0 ? di : dj, spacing,
                        faceEdges[face], faceNames[face], absorbers, weight, di != 0);

                    built._scale[(cell * Faces) + face] = scale;
                    built._weight[(cell * Faces) + face] = weight;
                    built._here[(cell * Faces) + face] = bHere;
                    built._there[(cell * Faces) + face] = bThere;
                    built._leaving[(cell * Faces) + face] = leaves;
                    built._names[(cell * Faces) + face] = name;
                    built._collects[(cell * Faces) + face] = collects;

                    outward += built.Out(cell, face);
                }

                built._outward[cell] = outward;
            }
        }

        return built;
    }

    /// <summary>The neighbour a face leads to, or -1 where it leaves the domain.</summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="face">Which face, indexing <see cref="Offsets"/>.</param>
    /// <param name="countY">Rows in the grid.</param>
    /// <returns>The neighbour's cell index, or -1.</returns>
    public int Neighbour(int cell, int face, int countY)
    {
        var (di, dj) = Offsets[face];

        var i = cell % _countX;
        var j = cell / _countX;

        var ni = i + di;
        var nj = j + dj;

        return ni < 0 || nj < 0 || ni >= _countX || nj >= countY
            ? -1
            : (nj * _countX) + ni;
    }

    private static (double Scale, double Here, double There, bool Leaves, string? Name, bool Collects) Face(
        Grid2D grid,
        double[] driftX,
        double[] driftY,
        double[] gasX,
        double[] gasY,
        double[] diffusion,
        double[] potential,
        double thermal,
        int i,
        int j,
        int ni,
        int nj,
        int direction,
        double spacing,
        Escape edge,
        string name,
        AbsorbingCells absorbers,
        double faceWeight,
        bool alongX)
    {
        if (faceWeight == 0.0)
        {
            // A face with no area. On the axis of a cylindrical solve this is the
            // whole statement: there is no radial direction there and nothing crosses.
            return (0.0, 0.0, 0.0, false, null, false);
        }

        var outside = ni < 0 || nj < 0 || ni >= grid.CountX || nj >= grid.CountY;

        if (outside && edge == Escape.Reflecting)
        {
            return (0.0, 0.0, 0.0, false, null, false);
        }

        var drift = alongX ? driftX : driftY;
        var gasDrift = alongX ? gasX : gasY;

        var k = (j * grid.CountX) + i;

        // A neighbour inside metal is an open edge with a name. The two cases are the
        // same physics - the density beyond is zero and nothing comes back - so they
        // share the accounting rather than getting a second path through it, and the
        // ions land under the surface's own name rather than under a domain edge they
        // never reached.
        var swallowed = !outside && absorbers.Blocks((nj * grid.CountX) + ni);

        if (swallowed)
        {
            name = absorbers.NameAt((nj * grid.CountX) + ni)!;
            edge = Escape.Absorbing;
        }

        double faceDiffusion;
        double drop;

        if (outside)
        {
            // Beyond an open edge the density is zero: ions that reach it are gone,
            // and nothing comes back. There is no neighbour to take a potential
            // difference against, so the cell's own field stands in for one - which is
            // exact for the linear potential the scheme assumes anyway.
            faceDiffusion = diffusion[k];

            // The exponent directly from the local drift, since P = v h / D, and the
            // gas carries the ions across the edge alongside the field.
            var total = drift[k] + gasDrift[k];

            drop = diffusion[k] > 0.0
                ? direction * total * spacing / diffusion[k]
                : (direction * total > 0.0 ? 40.0 : -40.0);
        }
        else
        {
            var neighbour = (nj * grid.CountX) + ni;

            faceDiffusion = 0.5 * (diffusion[k] + diffusion[neighbour]);

            // P = q (phi_here - phi_there) / kT, with the thermal voltage carrying the
            // sign of the charge. Antisymmetric between the two cells by construction,
            // which is what makes the flux conservative.
            drop = (potential[k] - potential[neighbour]) / thermal;

            // The gas adds a velocity the potential cannot express: advection by a
            // moving neutral is not the gradient of anything, so it enters the
            // exponent directly as P_gas = v.n h / D rather than as a potential
            // difference. That is the same exponent the field term already is - by the
            // Einstein relation q(phi_here - phi_there)/kT *is* v h / D - so the two
            // simply add, and Scharfetter-Gummel stays exact for a linearly varying
            // total drift.
            //
            // Averaged over the two nodes of the face, and signed by which way the
            // face points. Both are what keep it conservative: the neighbour computes
            // the same average with the opposite sign, so the two cells sharing a face
            // agree about how much crossed it. Sampling the gas at the cell centre
            // instead would repeat, exactly, the bug that made a seeded Boltzmann
            // equilibrium drain from the middle.
            var faceGas = 0.5 * (gasDrift[k] + gasDrift[neighbour]);

            if (faceGas != 0.0)
            {
                drop += faceDiffusion > 0.0
                    ? direction * faceGas * spacing / faceDiffusion
                    : (direction * faceGas > 0.0 ? 40.0 : -40.0);
            }
        }

        // Not clamped. Bernoulli already handles a large argument exactly - it is zero
        // above +40 and -x below -40, which are the true limits, not approximations to
        // them - so clamping the argument before calling it does not protect anything
        // and does throw the drift away. What it threw away: for large P the flux tends
        // to (D/h^2) P n_here, which is (v/h) n_here, the correct upwind answer.
        var peclet = double.IsFinite(drop) ? drop : Math.Sign(drop) * 40.0;

        double scale;
        double bHere;
        double bThere;

        if (faceDiffusion > 0.0)
        {
            scale = faceDiffusion / (spacing * spacing);

            bHere = Bernoulli(-peclet);
            bThere = Bernoulli(peclet);
        }
        else
        {
            // No diffusion at all: pure upwinding, which is the limit the Bernoulli
            // form tends to and cannot be evaluated through because the scale is zero.
            // Written in the same factored shape so the stepper needs no second case.
            scale = 1.0 / spacing;

            bHere = peclet > 0.0 ? peclet : 0.0;
            bThere = peclet > 0.0 ? 0.0 : -peclet;
        }

        // The face weight is returned separately rather than folded in, and applied
        // outside the difference, because that is where the old code applied it and
        // (w*s)*x is not w*(s*x). It is area over volume: both cells sharing a face
        // take the same face radius and so the same area, while each divides by its
        // own volume - which is what makes the ion counts balance rather than only the
        // densities. Exactly 1.0 in the plane.
        //
        // Nothing comes back across an open edge or out of metal, and the scheme says
        // so on its own: with the far density held at zero the flux reduces to
        // B(-P) n_here, non-negative for any P. The stepper reads a leaving face's
        // far density as zero rather than looking it up, which is the same statement
        // the old code made by assigning there = 0.
        return outside || swallowed
            ? (scale, bHere, bThere, true, name, edge == Escape.Collecting)
            : (scale, bHere, bThere, false, null, false);
    }

    /// <summary>The Bernoulli function x / (exp(x) - 1), continuous at zero.</summary>
    private static double Bernoulli(double x)
    {
        if (x > 40.0)
        {
            return 0.0;
        }

        if (x < -40.0)
        {
            return -x;
        }

        return Math.Abs(x) < 1e-10 ? 1.0 - (0.5 * x) : x / (Math.Exp(x) - 1.0);
    }
}
