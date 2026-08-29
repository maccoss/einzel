using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

namespace Einzel.Transport;

/// <summary>
/// Converting a packet between the two transport descriptions (SEQ-1).
/// </summary>
/// <remarks>
/// <para>
/// <b>An instrument is a timed state machine, and a phase boundary may change transport
/// mode.</b> That is a real instrument's ordinary behaviour rather than an exotic case:
/// ions are collected and thermalised in a gas-filled trap, where the description is a
/// density, and then extracted into vacuum and flown, where it is trajectories. Until
/// now the two modes were peers that could not hand anything to each other.
/// </para>
/// <para>
/// <b>SEQ-1 asks that the conversion be "explicit, reported, and named as a source of
/// uncertainty", and the third clause is the substance.</b> These are not two encodings
/// of the same state. Going one way discards information; going the other way requires
/// information the source does not have, and the only honest thing to do is assume it and
/// say so. Neither direction is lossless and neither is a round trip.
/// </para>
/// <para>
/// So every conversion carries warnings, and the ones that name an assumption are
/// violations rather than advisories: a caller who reads a flight time computed from
/// invented velocities and does not know they were invented has been misled by the
/// platform, which is precisely what GRD-3 exists to prevent.
/// </para>
/// </remarks>
public static class PacketConversion
{
    /// <summary>Trajectories become a density: deposit the positions, discard the rest.</summary>
    /// <param name="states">Where the ions are.</param>
    /// <param name="populationSi">How many real ions the packet holds.</param>
    /// <param name="grid">The grid to deposit onto.</param>
    /// <param name="cylindrical">Whether y is a radius rather than a second cartesian axis.</param>
    /// <returns>The density, and what the conversion cost.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The population is not positive.</exception>
    /// <remarks>
    /// <para>
    /// <b>What is lost is the velocity, entirely, and it is not recoverable.</b> A density
    /// field is a scalar per cell; there is nowhere for a velocity distribution to live.
    /// That is not an implementation limit but what the diffusive description *is* — the
    /// drift-diffusion equation holds because the velocity distribution has relaxed to the
    /// local equilibrium, so carrying one would be carrying a quantity the model assumes
    /// away.
    /// </para>
    /// <para>
    /// Also lost: which ion was where. Two ions that arrive at the same cell become
    /// indistinguishable, so anything correlating a starting condition with an outcome
    /// cannot survive the boundary.
    /// </para>
    /// <para>
    /// <b>Bilinear deposit, and population is conserved by construction</b> — the four
    /// weights sum to exactly one whatever the position, so the deposited population
    /// equals the declared one without a normalising pass. Normalising afterwards would
    /// pass the same test while hiding a weighting error rather than preventing one, which
    /// is the argument the cloud-in-cell deposit already makes one file away.
    /// </para>
    /// <para>
    /// An ion outside the grid is <b>counted, not clamped</b>. Clamping would pile the
    /// escaped population onto the boundary and make a leaky instrument look like a
    /// confining one; dropping it silently would make the density quietly too thin.
    /// </para>
    /// </remarks>
    public static DensityConversion ToDensity(
        IReadOnlyList<PhaseState> states, double populationSi, Grid2D grid, bool cylindrical)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(populationSi);

        if (states.Count == 0)
        {
            throw new ArgumentException(
                "a packet with no ions cannot be converted to a density", nameof(states));
        }

        var density = new DensityField(grid, cylindrical);
        var perIon = populationSi / states.Count;
        var outside = 0;

        foreach (var state in states)
        {
            // The same mapping an axisymmetric field uses: y is a radius, so the two
            // transverse components collapse into one and the azimuth is not represented.
            var y = cylindrical
                ? Math.Sqrt((state.Position.Y * state.Position.Y)
                    + (state.Position.Z * state.Position.Z))
                : state.Position.Y;

            if (!Deposit(density, state.Position.X, y, perIon))
            {
                outside++;
            }
        }

        var escaped = (double)outside / states.Count;
        var warnings = new List<ValidityWarning>
        {
            new(
                "transport.mode-changed",
                $"{states.Count} trajectories became a density of {populationSi:G6} ions. "
                + "The velocity distribution and the identity of individual ions are not "
                + "carried across: a density has nowhere to hold them, and the "
                + "drift-diffusion description assumes the velocities have relaxed to the "
                + "local equilibrium. Nothing downstream of this boundary can correlate an "
                + "outcome with a starting condition.",
                WarningSeverity.ValidityViolation),
        };

        // A sampled density is an estimate, and how good an estimate is a property of
        // the count rather than of the grid. Stated, because the number that follows
        // looks exactly as smooth as one computed from a continuum.
        warnings.Add(new ValidityWarning(
            "transport.sampled-density",
            $"the density is a deposit of {states.Count} samples, so a cell holding a "
            + $"fraction f of the packet carries a relative sampling error of about "
            + $"{1.0 / Math.Sqrt(states.Count):G3}/sqrt(f). Refining the grid does not "
            + "reduce it.",
            WarningSeverity.Advisory));

        if (outside > 0)
        {
            warnings.Add(new ValidityWarning(
                "transport.deposited-outside-grid",
                $"{escaped:P2} of the packet was outside the density grid at the boundary "
                + "and is not in the field. It is counted rather than clamped, because "
                + "piling it onto the edge would make a leaky instrument look confining.",
                WarningSeverity.ValidityViolation));
        }

        return new DensityConversion(density, 1.0 - escaped, warnings);
    }

    /// <summary>A density becomes trajectories: sample the positions, invent the velocities.</summary>
    /// <param name="density">The density to sample.</param>
    /// <param name="count">How many trajectories to produce.</param>
    /// <param name="species">The ion, whose mass sets the thermal speed.</param>
    /// <param name="gas">The gas, whose temperature the velocities are drawn at.</param>
    /// <param name="field">The field, for the local drift.</param>
    /// <param name="mobility">The mobility, for the local drift.</param>
    /// <param name="seed">Makes the draw reproducible.</param>
    /// <returns>The states, and what the conversion assumed.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The count is not positive.</exception>
    /// <remarks>
    /// <para>
    /// <b>The velocity is not in the density and has to be assumed.</b> This is the
    /// sharpest thing about the whole conversion. Position can be sampled — the density
    /// *is* a distribution over position — but a density says nothing whatever about how
    /// fast anything is moving. What is assumed is the assumption the diffusive
    /// description already made: a Maxwellian at the gas temperature, plus the local drift
    /// the mobility and the field imply.
    /// </para>
    /// <para>
    /// That is the right assumption and it is still an assumption. It is exactly right
    /// while the ions remain in the gas that thermalised them, and it is wrong the moment
    /// anything has happened faster than the momentum-transfer time — so a conversion at a
    /// boundary where the gas is switched off in the same instant is describing ions that
    /// were thermal a moment ago and are not any more. `transport.velocity-assumed` is a
    /// violation for that reason.
    /// </para>
    /// <para>
    /// <b>Cells are drawn by population, not by density.</b> In a cylindrical field a cell
    /// is a ring whose volume grows with radius, so drawing by density alone would put far
    /// too many ions on the axis. The weight is value times cell volume, which is exactly
    /// what <see cref="DensityField.Population"/> integrates — and a uniform density in a
    /// cylinder then gives the mean radius 2R/3 it should, rather than the R/2 the wrong
    /// weighting gives.
    /// </para>
    /// <para>
    /// In a cylindrical field the azimuth is drawn uniformly, because an axisymmetric
    /// density genuinely does not distinguish points on a ring. That is information the
    /// conversion creates rather than carries, and it is why a round trip is not the
    /// identity even in distribution for a packet that was never axisymmetric.
    /// </para>
    /// </remarks>
    public static TrajectoryConversion ToTrajectories(
        DensityField density,
        int count,
        IonSpecies species,
        BackgroundGas gas,
        IElectrostaticField field,
        Mobility mobility,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(density);
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var population = density.Population();

        if (population <= 0.0)
        {
            throw new ArgumentException(
                "an empty density cannot be converted to trajectories: there is nothing "
                + "left to sample, which is a result rather than a state to continue from",
                nameof(density));
        }

        var cumulative = Cumulative(density, out var cells);
        var random = new Random(seed);
        var states = new PhaseState[count];

        var thermal = Math.Sqrt(IonCloud.BoltzmannSi * gas.TemperatureK / species.MassSi);
        var grid = density.Grid;

        for (var n = 0; n < count; n++)
        {
            var (i, j) = Pick(cumulative, cells, random.NextDouble() * cumulative[^1], grid);

            // Uniform within the chosen cell. Sub-cell structure is below what the field
            // resolves, so there is nothing better to be said about where in it the ion is.
            var x = grid.X(i) + ((random.NextDouble() - 0.5) * grid.SpacingX);
            var y = grid.Y(j) + ((random.NextDouble() - 0.5) * grid.SpacingY);

            y = Math.Max(y, 0.0);

            var position = density.Cylindrical ? OnRing(x, y, random) : new Vec3(x, y, 0.0);

            var drift = Drift(field, mobility, gas, species, position);

            states[n] = new PhaseState(
                position,
                drift + new Vec3(
                    thermal * Gaussian(random),
                    thermal * Gaussian(random),
                    thermal * Gaussian(random)));
        }

        var warnings = new List<ValidityWarning>
        {
            new(
                "transport.velocity-assumed",
                $"a density carries no velocity, so the {count} trajectories leave this "
                + $"boundary with velocities drawn from a Maxwellian at the gas "
                + $"temperature ({gas.TemperatureK:G4} K) plus the local drift. That is "
                + "the assumption the diffusive description itself makes, and it stops "
                + "being true as soon as anything happens faster than the "
                + "momentum-transfer time.",
                WarningSeverity.ValidityViolation),
            new(
                "transport.mode-changed",
                $"a density of {population:G6} ions became {count} trajectories carrying "
                + $"{population / count:G6} ions each. The sampling adds a statistical "
                + $"error of about {1.0 / Math.Sqrt(count):P2} to any quantity averaged "
                + "over the packet, which was not present in the density.",
                WarningSeverity.ValidityViolation),
        };

        if (density.Cylindrical)
        {
            warnings.Add(new ValidityWarning(
                "transport.azimuth-invented",
                "the density is axisymmetric, so the azimuth of each ion is drawn "
                + "uniformly rather than carried. A packet that was not axisymmetric "
                + "before the boundary does not come back.",
                WarningSeverity.Advisory));
        }

        return new TrajectoryConversion(states, population / count, warnings);
    }

    /// <summary>The drift velocity a density's own model implies at a point.</summary>
    private static Vec3 Drift(
        IElectrostaticField field,
        Mobility mobility,
        BackgroundGas gas,
        IonSpecies species,
        in Vec3 position)
    {
        var e = field.ElectricFieldAt(position);
        var magnitude = e.Length;

        if (magnitude <= 0.0)
        {
            return Vec3.Zero;
        }

        var local = gas.NumberDensityAt(position);
        var mu = mobility.At(magnitude, local, gas.NumberDensitySi);

        return e * (mu * Math.Sign(species.ChargeSi));
    }

    /// <summary>A point on the ring at radius y, at a uniformly drawn azimuth.</summary>
    private static Vec3 OnRing(double x, double y, Random random)
    {
        var angle = 2.0 * Math.PI * random.NextDouble();

        return new Vec3(x, y * Math.Cos(angle), y * Math.Sin(angle));
    }

    /// <summary>Bilinear deposit. False if the ion is off the grid.</summary>
    private static bool Deposit(DensityField density, double x, double y, double perIon)
    {
        var grid = density.Grid;

        var fx = (x - grid.OriginX) / grid.SpacingX;
        var fy = (y - grid.OriginY) / grid.SpacingY;

        var i = (int)Math.Floor(fx);
        var j = (int)Math.Floor(fy);

        if (i < 0 || j < 0 || i + 1 >= grid.CountX || j + 1 >= grid.CountY)
        {
            return false;
        }

        var u = fx - i;
        var v = fy - j;

        // A density is ions per unit volume, so the deposited count is divided by the
        // volume of the cell it lands in - and in a cylindrical field that volume varies
        // with radius, which is the whole reason this is not a plain histogram.
        Add(density, i, j, perIon * (1 - u) * (1 - v));
        Add(density, i + 1, j, perIon * u * (1 - v));
        Add(density, i, j + 1, perIon * (1 - u) * v);
        Add(density, i + 1, j + 1, perIon * u * v);

        return true;
    }

    private static void Add(DensityField density, int i, int j, double ions) =>
        density[i, j] += ions / density.CellVolume(j);

    /// <summary>The cumulative population over occupied cells, for inverse-CDF sampling.</summary>
    private static double[] Cumulative(DensityField density, out (int I, int J)[] cells)
    {
        var grid = density.Grid;
        var list = new List<(int, int)>();
        var running = new List<double>();
        var total = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < grid.CountX; i++)
            {
                var ions = density[i, j] * volume;

                if (ions <= 0.0)
                {
                    continue;
                }

                total += ions;
                list.Add((i, j));
                running.Add(total);
            }
        }

        cells = [.. list];

        return [.. running];
    }

    private static (int I, int J) Pick(
        double[] cumulative, (int I, int J)[] cells, double target, Grid2D grid)
    {
        _ = grid;

        var lo = 0;
        var hi = cumulative.Length - 1;

        while (lo < hi)
        {
            var mid = (lo + hi) / 2;

            if (cumulative[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return cells[lo];
    }

    /// <summary>One standard normal, by Box-Muller.</summary>
    private static double Gaussian(Random random)
    {
        var u = 1.0 - random.NextDouble();
        var v = random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }
}

/// <summary>What became of a packet that crossed into the diffusive description.</summary>
/// <param name="Density">The density it became.</param>
/// <param name="DepositedFraction">
/// The fraction of the packet that landed on the grid. Less than one means ions were
/// outside it, and those ions are not in the density.
/// </param>
/// <param name="Warnings">What the conversion cost, per SEQ-1.</param>
public sealed record DensityConversion(
    DensityField Density,
    double DepositedFraction,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>What became of a density that crossed into the trajectory description.</summary>
/// <param name="States">The trajectories it became.</param>
/// <param name="PopulationPerIon">
/// How many real ions each trajectory stands for, which is what makes a transmission
/// computed after the boundary comparable with one computed before it.
/// </param>
/// <param name="Warnings">What the conversion assumed, per SEQ-1.</param>
public sealed record TrajectoryConversion(
    PhaseState[] States,
    double PopulationPerIon,
    IReadOnlyList<ValidityWarning> Warnings);
