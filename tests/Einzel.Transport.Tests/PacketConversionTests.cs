using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Converting a packet between the two transport descriptions (SEQ-1).
/// </summary>
/// <remarks>
/// A phase boundary may change transport mode, and the conversion must be "explicit,
/// reported, and named as a source of uncertainty". These tests are about the third
/// clause as much as the first two: the two descriptions do not hold the same
/// information, so one direction discards and the other invents, and both have to say so.
/// </remarks>
public sealed class PacketConversionTests(ITestOutputHelper output)
{
    private static readonly IonSpecies Ion = IonSpecies.FromMassToCharge(500.0, 1);

    private const double Dalton = 1.66053906892e-27;

    /// <summary>Nitrogen at a millibar, which is where a diffusive stage lives.</summary>
    private static BackgroundGas Gas(double kelvin = 300.0) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = 100.0,
        TemperatureK = kelvin,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    /// <summary>The mobility that gas and this ion imply.</summary>
    private static Mobility Mu(BackgroundGas gas) => Mobility.FromCrossSection(gas, Ion);

    /// <summary>The deposit conserves the packet exactly, whatever the positions.</summary>
    /// <remarks>
    /// The four bilinear weights sum to exactly one for any position, so the deposited
    /// population is the declared one by construction rather than by a normalising pass.
    /// Normalising afterwards would pass this same test while hiding a weighting error
    /// rather than preventing one — the argument the cloud-in-cell deposit already makes.
    /// </remarks>
    [Fact]
    public void TheDepositConservesThePacket()
    {
        var grid = new Grid2D(-0.05, -0.05, 0.001, 0.001, 101, 101);
        var random = new Random(11);

        var states = new PhaseState[500];

        for (var n = 0; n < states.Length; n++)
        {
            states[n] = new PhaseState(
                new Vec3(
                    (random.NextDouble() - 0.5) * 0.06,
                    (random.NextDouble() - 0.5) * 0.06,
                    0.0),
                Vec3.Zero);
        }

        var converted = PacketConversion.ToDensity(states, 4.0e6, grid, cylindrical: false);

        output.WriteLine($"declared 4.0e6, deposited {converted.Density.Population():G10}");

        Assert.Equal(1.0, converted.DepositedFraction);
        Assert.Equal(4.0e6, converted.Density.Population(), 4.0e6 * 1e-12);
    }

    /// <summary>A Gaussian cloud deposits to a density with the same moments.</summary>
    /// <remarks>
    /// The check that the deposit is a density and not merely a histogram of something.
    /// Centroid and spread are what the diffusive mode reports about a packet, so if those
    /// do not survive the boundary nothing downstream of it means anything.
    /// </remarks>
    [Fact]
    public void AGaussianCloudKeepsItsCentroidAndSpread()
    {
        const double CentreX = 0.010;
        const double Sigma = 0.004;
        const int Ions = 20_000;

        var grid = new Grid2D(-0.05, -0.05, 0.0005, 0.0005, 201, 201);
        var random = new Random(7);
        var states = new PhaseState[Ions];

        for (var n = 0; n < Ions; n++)
        {
            states[n] = new PhaseState(
                new Vec3(CentreX + (Sigma * Normal(random)), Sigma * Normal(random), 0.0),
                Vec3.Zero);
        }

        var density = PacketConversion
            .ToDensity(states, 1.0e6, grid, cylindrical: false).Density;

        var (cx, cy) = density.Centroid();
        var (sx, sy) = density.Spread();

        output.WriteLine($"centroid {cx * 1e3:F4}, {cy * 1e3:F4} mm (asked {CentreX * 1e3}, 0)");
        output.WriteLine($"spread   {sx * 1e3:F4}, {sy * 1e3:F4} mm (asked {Sigma * 1e3})");

        // Sampling error on a mean of N is sigma/sqrt(N); three of those is the band.
        var band = 3.0 * Sigma / Math.Sqrt(Ions);

        Assert.Equal(CentreX, cx, band);
        Assert.Equal(0.0, cy, band);

        // The deposit smooths by up to a cell, so the spread comes out marginally wide.
        Assert.Equal(Sigma, sx, 0.02 * Sigma);
        Assert.Equal(Sigma, sy, 0.02 * Sigma);
    }

    /// <summary>
    /// Sampling a cylindrical density draws cells by population, not by density.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The discriminating test, and the one a plausible implementation fails.</b> In an
    /// axisymmetric field a cell is a ring whose volume grows with radius, so a uniform
    /// density holds far more ions at the wall than on the axis. Drawing cells by their
    /// density value alone would over-sample the axis, and the resulting packet would look
    /// entirely reasonable — a cloud, in the right place, of the right extent.
    /// </para>
    /// <para>
    /// What separates the two is a closed form. For a uniform density in a cylinder of
    /// radius R the radial distribution is p(r) proportional to r, so the mean radius is
    /// 2R/3. Weighting by density alone gives a uniform p(r) and a mean of R/2 — a third
    /// smaller, and nothing about the picture says which one you have.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACylindricalDensityIsSampledByPopulationRatherThanByDensity()
    {
        const double Radius = 0.02;
        const int Count = 40_000;

        // Uniform density out to R, on the half-plane: x along the axis, y the radius.
        var grid = new Grid2D(0.0, 0.0, 0.0005, 0.0005, 21, 41);
        var density = new DensityField(grid, cylindrical: true);

        for (var j = 0; j < grid.CountY; j++)
        {
            if (grid.Y(j) > Radius)
            {
                continue;
            }

            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] = 1.0e12;
            }
        }

        var converted = PacketConversion.ToTrajectories(
            density, Count, Ion, Gas(), FieldFreeSpace.Instance, Mu(Gas()), seed: 3);

        var mean = converted.States
            .Average(s => Math.Sqrt(
                (s.Position.Y * s.Position.Y) + (s.Position.Z * s.Position.Z)));

        output.WriteLine($"mean radius {mean * 1e3:F4} mm");
        output.WriteLine($"2R/3 = {2.0 * Radius / 3.0 * 1e3:F4} mm  (population-weighted)");
        output.WriteLine($"R/2  = {Radius / 2.0 * 1e3:F4} mm  (density-weighted - wrong)");

        // The grid's outermost occupied ring is centred at R, so it extends half a cell
        // beyond; the closed form is for a sharp edge and lands a per cent or so below.
        Assert.Equal(2.0 * Radius / 3.0, mean, 0.02 * Radius);

        // And decisively not the wrong answer, which is a third away.
        Assert.True(
            Math.Abs(mean - (Radius / 2.0)) > 0.1 * Radius,
            $"mean radius {mean:G6} is close to R/2, which is the density-weighted answer");
    }

    /// <summary>The azimuth is drawn, so a sampled ring is a ring rather than a line.</summary>
    [Fact]
    public void ACylindricalSampleIsSpreadOverItsAzimuth()
    {
        var grid = new Grid2D(0.0, 0.0, 0.001, 0.001, 11, 11);
        var density = new DensityField(grid, cylindrical: true);

        density[5, 5] = 1.0e12;

        var states = PacketConversion.ToTrajectories(
            density, 2000, Ion, Gas(), FieldFreeSpace.Instance, Mu(Gas()), seed: 5)
            .States;

        // Uniform on the ring means the transverse mean is zero and the two transverse
        // variances are equal - neither of which a fixed azimuth would give.
        var meanY = states.Average(s => s.Position.Y);
        var meanZ = states.Average(s => s.Position.Z);
        var varY = states.Average(s => s.Position.Y * s.Position.Y);
        var varZ = states.Average(s => s.Position.Z * s.Position.Z);

        output.WriteLine($"mean y {meanY * 1e3:F5} mm, mean z {meanZ * 1e3:F5} mm");
        output.WriteLine($"<y^2>/<z^2> = {varY / varZ:F4}");

        Assert.Equal(0.0, meanY, 3.0 * Math.Sqrt(varY / states.Length));
        Assert.Equal(0.0, meanZ, 3.0 * Math.Sqrt(varZ / states.Length));
        Assert.Equal(1.0, varY / varZ, 0.15);
    }

    /// <summary>
    /// The velocities are not carried, they are drawn - thermal, at the gas temperature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A density says nothing whatever about how fast anything is moving, so this is the
    /// conversion inventing information. What it invents is the assumption the diffusive
    /// description already made — a Maxwellian at the gas temperature — and equipartition
    /// is the sharp check on it, because (3/2)kT is exact and is not a number the code
    /// knows.
    /// </para>
    /// <para>
    /// Run at two temperatures, because a single one is consistent with a thermal draw and
    /// with a constant that happens to match.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(300.0)]
    [InlineData(1200.0)]
    public void VelocitiesAreDrawnThermalAtTheGasTemperature(double kelvin)
    {
        const int Count = 20_000;

        var grid = new Grid2D(0.0, -0.005, 0.001, 0.001, 11, 11);
        var density = new DensityField(grid, cylindrical: false);

        density[5, 5] = 1.0e12;

        var states = PacketConversion.ToTrajectories(
            density, Count, Ion, Gas(kelvin), FieldFreeSpace.Instance,
            Mu(Gas(kelvin)), seed: 13).States;

        var mean = states.Average(s => 0.5 * Ion.MassSi * s.Velocity.LengthSquared);
        var expected = 1.5 * IonCloud.BoltzmannSi * kelvin;

        output.WriteLine(
            $"{kelvin} K: mean KE {mean:E4} J against (3/2)kT {expected:E4} J "
            + $"-> {mean / expected:F4}");

        // The standard error on a mean of N chi-squared-with-3 draws is sqrt(2/3)/sqrt(N).
        var relative = Math.Sqrt(2.0 / 3.0) / Math.Sqrt(Count);

        Assert.Equal(1.0, mean / expected, 4.0 * relative);
    }

    /// <summary>A field puts the drift into the drawn velocities, on top of the thermal part.</summary>
    /// <remarks>
    /// Taken as a difference between two runs at the same seed, so the thermal draw cancels
    /// and what is left is the drift alone. Checked against mu E, which is arithmetic the
    /// conversion has no part in.
    /// </remarks>
    [Fact]
    public void TheLocalDriftIsAddedToTheThermalDraw()
    {
        const double FieldSi = 200.0;

        var gas = Gas();
        var mu = Mu(gas);

        var grid = new Grid2D(0.0, -0.005, 0.001, 0.001, 11, 11);
        var density = new DensityField(grid, cylindrical: false);

        density[5, 5] = 1.0e12;

        var still = PacketConversion.ToTrajectories(
            density, 4000, Ion, gas, FieldFreeSpace.Instance, mu, seed: 17).States;

        var driven = PacketConversion.ToTrajectories(
            density, 4000, Ion, gas,
            UniformField.Create(new Vec3(FieldSi, 0.0, 0.0)), mu, seed: 17).States;

        var carried = driven.Average(s => s.Velocity.X) - still.Average(s => s.Velocity.X);

        var expected = mu.At(FieldSi, gas.NumberDensitySi) * FieldSi;

        output.WriteLine($"carried {carried:F6} m/s against mu E = {expected:F6} m/s");

        Assert.Equal(expected, carried, Math.Abs(expected) * 1e-9);
    }

    /// <summary>Both directions say what they cost, and neither can be silenced.</summary>
    /// <remarks>
    /// SEQ-1's "named as a source of uncertainty". These are violations rather than
    /// advisories because a caller who reads a flight time computed from invented
    /// velocities, and does not know they were invented, has been misled by the platform -
    /// which is what GRD-3 exists to prevent.
    /// </remarks>
    [Fact]
    public void EveryConversionNamesWhatItCostAndCannotBeSilenced()
    {
        var grid = new Grid2D(0.0, -0.005, 0.001, 0.001, 11, 11);

        var toDensity = PacketConversion.ToDensity(
            [new PhaseState(new Vec3(0.005, 0.0, 0.0), new Vec3(1000.0, 0.0, 0.0))],
            1.0e6,
            grid,
            cylindrical: false);

        var density = new DensityField(grid, cylindrical: false);
        density[5, 5] = 1.0e12;

        var toStates = PacketConversion.ToTrajectories(
            density, 10, Ion, Gas(), FieldFreeSpace.Instance, Mu(Gas()), seed: 1);

        foreach (var w in toDensity.Warnings.Concat(toStates.Warnings))
        {
            output.WriteLine($"[{w.Severity}] {w.Code}");
        }

        Assert.Contains(toDensity.Warnings, w =>
            w.Code == "transport.mode-changed" && !w.IsSuppressible);

        Assert.Contains(toStates.Warnings, w =>
            w.Code == "transport.velocity-assumed" && !w.IsSuppressible);

        Assert.Contains(toStates.Warnings, w =>
            w.Code == "transport.mode-changed" && !w.IsSuppressible);
    }

    /// <summary>Ions outside the grid are counted rather than piled onto its edge.</summary>
    /// <remarks>
    /// Clamping would make a leaky instrument look confining, which is the failure that
    /// matters: the population would be right and it would be in the wrong place. Counted,
    /// the loss is visible in the deposited fraction and in a non-suppressible warning.
    /// </remarks>
    [Fact]
    public void IonsOffTheGridAreCountedNotClamped()
    {
        var grid = new Grid2D(0.0, -0.005, 0.001, 0.001, 11, 11);

        PhaseState At(double x) => new(new Vec3(x, 0.0, 0.0), Vec3.Zero);

        var converted = PacketConversion.ToDensity(
            [At(0.002), At(0.004), At(0.006), At(0.500)], 4.0e6, grid, cylindrical: false);

        output.WriteLine($"deposited {converted.DepositedFraction:P2}");

        Assert.Equal(0.75, converted.DepositedFraction, 1e-12);

        // Three of the four are on the grid, and the density holds exactly those three.
        Assert.Equal(3.0e6, converted.Density.Population(), 3.0e6 * 1e-12);

        Assert.Contains(converted.Warnings, w =>
            w.Code == "transport.deposited-outside-grid" && !w.IsSuppressible);
    }

    /// <summary>
    /// A round trip preserves the distribution and nothing else, which is the point.
    /// </summary>
    /// <remarks>
    /// Worth asserting explicitly because the natural expectation of a "conversion" is
    /// that it round-trips. It does not, and the ways it fails to are what SEQ-1 asks be
    /// reported: the positions come back as a distribution, and the velocities come back
    /// as something else entirely — a 4 km/s beam leaves as a thermal cloud two orders
    /// slower.
    /// </remarks>
    [Fact]
    public void ARoundTripKeepsTheDistributionAndDiscardsTheVelocities()
    {
        const int Ions = 20_000;
        const double Beam = 4000.0;

        var grid = new Grid2D(-0.05, -0.05, 0.0005, 0.0005, 201, 201);
        var random = new Random(29);
        var states = new PhaseState[Ions];

        for (var n = 0; n < Ions; n++)
        {
            states[n] = new PhaseState(
                new Vec3(0.004 * Normal(random), 0.004 * Normal(random), 0.0),
                new Vec3(Beam, 0.0, 0.0));
        }

        var density = PacketConversion
            .ToDensity(states, 1.0e6, grid, cylindrical: false).Density;

        var back = PacketConversion.ToTrajectories(
            density, Ions, Ion, Gas(), FieldFreeSpace.Instance,
            Mu(Gas()), seed: 31).States;

        var beforeX = Sigma(states.Select(s => s.Position.X));
        var afterX = Sigma(back.Select(s => s.Position.X));

        var beforeSpeed = states.Average(s => s.Velocity.X);
        var afterSpeed = back.Average(s => s.Velocity.X);

        output.WriteLine($"position sigma {beforeX * 1e3:F4} -> {afterX * 1e3:F4} mm");
        output.WriteLine($"mean vx        {beforeSpeed:F1} -> {afterSpeed:F1} m/s");

        // The distribution survives, to sampling error and a cell of deposit smoothing.
        Assert.Equal(beforeX, afterX, 0.05 * beforeX);

        // The beam does not. This is the loss, asserted rather than described.
        Assert.True(
            Math.Abs(afterSpeed) < 0.02 * Beam,
            $"the {Beam} m/s beam should not survive a density, and came back at {afterSpeed}");
    }

    private static double Sigma(IEnumerable<double> values)
    {
        var list = values.ToArray();
        var mean = list.Average();

        return Math.Sqrt(list.Average(v => (v - mean) * (v - mean)));
    }

    private static double Normal(Random random)
    {
        var u = 1.0 - random.NextDouble();
        var v = random.NextDouble();

        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }
}
