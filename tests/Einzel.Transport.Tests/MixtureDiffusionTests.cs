using Einzel.Core.Units;
using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// Several ion populations in one gas, stepped together, pushing on one another through their
/// own charge.
/// </summary>
/// <remarks>
/// <para>
/// A mixture is not N runs. Every coefficient a species needs is its own - mobility, diffusion,
/// thermal voltage, and in a driven structure the well its mass and damping give it - and if
/// the field were its own as well the populations would be independent and could be run one
/// after another. They are coupled by exactly one quantity, the potential their total charge
/// raises, so these tests are built around isolating that one coupling and nothing else.
/// </para>
/// <para>
/// <b>Three of them are controls rather than measurements</b>, and they carry most of the
/// weight. One species through the mixture path must be <em>bit-identical</em> to the
/// single-species path, which is what stops the two drifting apart later. A second species
/// present with the self-field switched off must leave the first bit-identical, which is what
/// says the field is the <em>only</em> coupling. And opposite polarities must cancel in the
/// source, which is what says the sum is signed rather than a magnitude.
/// </para>
/// </remarks>
public sealed class MixtureDiffusionTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;

    private static BackgroundGas Nitrogen(double pressurePa) => new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    private static readonly DriftDiffusion.DomainEdges Closed = new(
        Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

    /// <summary>Reflecting at the ends, metal at the transverse walls.</summary>
    /// <remarks>
    /// What a self-field needs and a sealed box cannot give it: with every edge a symmetry
    /// plane and no conductor anywhere, a charge has nowhere for its field to terminate and its
    /// potential is not defined - which <c>DensitySelfField</c> refuses rather than solving, and
    /// which is how the first version of these tests was caught. A real bore has walls, so the
    /// fix is to model them rather than to weaken the refusal.
    /// </remarks>
    private static readonly DriftDiffusion.DomainEdges Bore = new(
        Escape.Reflecting, Escape.Reflecting, Escape.Absorbing, Escape.Absorbing);

    /// <summary>A Gaussian blob of the given population, centred where asked.</summary>
    private static DensityField Blob(Grid2D grid, double centreXm, double widthM, double ions)
    {
        var density = new DensityField(grid);
        var total = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = grid.X(i) - centreXm;
                var dy = grid.Y(j);
                var value = Math.Exp(-((dx * dx) + (dy * dy)) / (2.0 * widthM * widthM));

                density[i, j] = value;
                total += value * volume;
            }
        }

        // Normalised from the cell volumes rather than from a formula, so a blob whose tail
        // runs off the grid still holds exactly the population declared.
        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] *= ions / total;
            }
        }

        return density;
    }

    /// <summary>
    /// A field whose strength rises linearly along x, which is what a mobility separation
    /// needs and what no analytic field here provides.
    /// </summary>
    /// <remarks>
    /// Written here rather than added to the library because it exists to make an expectation
    /// arithmetic: an ion parks where mu E = v_gas, so on E = slope x the parking point is
    /// v_gas / (K slope) and the ratio between two species is the ratio of their mobilities,
    /// with nothing fitted and no geometry in the way.
    /// </remarks>
    private sealed class LinearRamp(double slopeVoltsPerMetreSquared) : IElectrostaticField
    {
        public Vec3 ElectricFieldAt(in Vec3 position) =>
            new(slopeVoltsPerMetreSquared * position.X, 0.0, 0.0);

        // The potential whose negative gradient is that field.
        public double PotentialAt(in Vec3 position) =>
            -0.5 * slopeVoltsPerMetreSquared * position.X * position.X;
    }

    /// <summary>
    /// One species through the mixture path is the single-species path, to the last bit.
    /// </summary>
    /// <remarks>
    /// The control that keeps the two from drifting. The mixture composes the same coefficient
    /// sampler, the same stability limit, the same face assembly and the same stepper the
    /// single-species run calls; only the loop around them is new. So with one member and no
    /// self-field there is nothing left that could differ, and anything that does is a defect
    /// in the loop rather than a difference in the physics. Bit-equality rather than a
    /// tolerance for exactly that reason: a tolerance would let real drift accumulate quietly.
    /// </remarks>
    [Fact]
    public void AOneSpeciesMixtureIsBitIdenticalToASingleSpeciesRun()
    {
        var grid = Grid2D.OverBox(-0.02, -0.01, 0.04, 0.01, 128, 64);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(200.0, 0.0, 0.0));
        var seconds = 5e-5;

        var single = DriftDiffusion.Run(
            Blob(grid, -0.01, 1e-3, 1e6), field, gas, mobility, species, seconds, Closed);

        var mixed = MixtureDiffusion.Run(
            [new MixtureMember("only", species, mobility, Blob(grid, -0.01, 1e-3, 1e6))],
            field, gas, seconds, Closed);

        Assert.Single(mixed.Species);
        Assert.Equal("only", mixed.StepSetBy);
        Assert.Equal(single.Steps, mixed.Steps);
        Assert.Equal(single.ElapsedSeconds, mixed.ElapsedSeconds);
        Assert.Equal(single.StepSeconds, mixed.StepSeconds);

        var one = mixed.Species[0];
        var differences = 0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (!single.Density[i, j].Equals(one.Density[i, j]))
                {
                    differences++;
                }
            }
        }

        output.WriteLine($"{single.Steps} steps either way, step {single.StepSeconds:G6} s");
        output.WriteLine($"{differences} of {grid.CountX * grid.CountY} nodes differ; "
            + $"population {single.Remaining:G17} against {one.Population:G17}");

        Assert.Equal(0, differences);
        Assert.Equal(single.Remaining, one.Population);
        Assert.Equal(single.Collected, one.Collected);
    }

    /// <summary>
    /// And with a self-field it still tracks the single-species path, which the bit-identity
    /// control above cannot see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The control above runs with no charge, so nothing in it exercises the self-field at all.
    /// That left a real defect invisible: the mixture's list of densities was captured once,
    /// while every step swaps each species' density with its scratch buffer - so from the second
    /// step the self-potential was solved from whichever buffer the walker was <em>not</em>
    /// using, alternating between the current density and the previous one.
    /// </para>
    /// <para>
    /// <b>None of the other tests here could see it.</b> The bit-identity control has no
    /// self-field; the blind half of the coupling test has none by construction; and the charged
    /// half asserts a direction and a magnitude, both of which a one-step-stale field still gets
    /// right. What discriminates is running the same single species down both paths <em>with</em>
    /// charge, where the single-species path passes its current density by variable and cannot
    /// go stale.
    /// </para>
    /// <para>
    /// Not bit-equality: the two refresh overloads are deliberately separate arithmetic, so that
    /// the single-species one - which carries every self-field number this engine has published -
    /// is left exactly as it was. What is asserted is that they agree far more closely than a
    /// stale field would allow.
    /// </para>
    /// </remarks>
    [Fact]
    public void AChargedOneSpeciesMixtureTracksTheSingleSpeciesPath()
    {
        var grid = Grid2D.OverBox(-0.01, -0.003, 0.01, 0.003, 128, 32);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(150.0, 0.0, 0.0));
        var seconds = 3e-5;
        var ions = 4e8;

        DensitySelfField Charged() => new(
            grid, cylindrical: false, AbsorbingCells.None, Bore, species.ChargeSi);

        var single = DriftDiffusion.Run(
            Blob(grid, -0.004, 6e-4, ions), field, gas, mobility, species, seconds, Bore,
            selfField: Charged());

        var mixed = MixtureDiffusion.Run(
            [new MixtureMember("only", species, mobility, Blob(grid, -0.004, 6e-4, ions))],
            field, gas, seconds, Bore, selfField: Charged());

        Assert.Equal(single.Steps, mixed.Steps);

        var worst = 0.0;
        var peak = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                peak = Math.Max(peak, single.Density[i, j]);
                worst = Math.Max(worst, Math.Abs(single.Density[i, j] - mixed.Species[0].Density[i, j]));
            }
        }

        var relative = peak > 0.0 ? worst / peak : 0.0;

        output.WriteLine($"{single.Steps} steps, {single.SelfFieldSolves} self-field solves against "
            + $"{mixed.SelfFieldSolves}, peak {single.PeakSelfPotentialVolts:G4} V against "
            + $"{mixed.PeakSelfPotentialVolts:G4} V");
        output.WriteLine($"worst node disagreement {relative:E2} of the peak density");

        Assert.Equal(single.SelfFieldSolves, mixed.SelfFieldSolves);

        Assert.True(
            relative < 1e-9,
            $"the two paths disagree by {relative:E2} of the peak density, which is far more than "
            + "two spellings of the same sum should, and is what a self-field solved from a stale "
            + "buffer looks like");
    }

    /// <summary>
    /// Two mobilities in one gas come to rest at their own balance points, not at an average.
    /// </summary>
    /// <remarks>
    /// The separation a mobility analyser exists for, reduced to its arithmetic: with the gas
    /// pushing one way at a fixed speed and the field pushing back with a strength that rises
    /// along the axis, an ion stops where <c>mu E = v_gas</c>. On <c>E = slope x</c> that puts
    /// it at <c>v_gas / (K slope)</c>, so the ratio of two parking points is the inverse ratio
    /// of the mobilities and contains nothing else - not the gas mass, not the temperature,
    /// not the grid.
    /// </remarks>
    [Fact]
    public void TwoMobilitiesParkAtTheirOwnBalancePoints()
    {
        var grid = Grid2D.OverBox(0.0, -0.004, 0.05, 0.004, 256, 32);

        // A gas streaming along +x, and a field pushing back harder the further out you go.
        var gas = Nitrogen(200.0) with { DriftVelocitySi = new Vec3(60.0, 0.0, 0.0) };
        var slope = 4.0e5;
        var field = new LinearRamp(-slope);

        var light = IonSpecies.FromMassToCharge(300.0, 1);
        var heavy = IonSpecies.FromMassToCharge(300.0, 1);

        // The same ion twice, differing only in the mobility declared for it: what is being
        // measured is what the mobility does, so nothing else is allowed to vary.
        var fast = Mobility.FromCrossSection(gas, light);
        var slow = new Mobility { ZeroFieldSi = fast.ZeroFieldSi * 0.5 };

        var result = MixtureDiffusion.Run(
            [
                new MixtureMember("fast", light, fast, Blob(grid, 0.005, 1.5e-3, 1e5)),
                new MixtureMember("slow", heavy, slow, Blob(grid, 0.005, 1.5e-3, 1e5)),
            ],
            field, gas, 3e-3, Closed, scheme: StepScheme.Implicit, stepGain: 32.0);

        var (fastX, _) = result.Species[0].Density.Centroid();
        var (slowX, _) = result.Species[1].Density.Centroid();

        var fastExpected = 60.0 / (fast.ZeroFieldSi * slope);
        var slowExpected = 60.0 / (slow.ZeroFieldSi * slope);

        output.WriteLine($"fast (K = {fast.ZeroFieldSi:G4} m2/Vs): parked at {fastX * 1e3:F2} mm, "
            + $"v_gas/(K slope) = {fastExpected * 1e3:F2} mm");
        output.WriteLine($"slow (K = {slow.ZeroFieldSi:G4} m2/Vs): parked at {slowX * 1e3:F2} mm, "
            + $"v_gas/(K slope) = {slowExpected * 1e3:F2} mm");
        output.WriteLine($"separation {(slowX - fastX) * 1e3:F2} mm; position ratio {slowX / fastX:F3} "
            + $"against a mobility ratio of {fast.ZeroFieldSi / slow.ZeroFieldSi:F3}");

        Assert.Equal(fastExpected, fastX, fastExpected * 0.1);
        Assert.Equal(slowExpected, slowX, slowExpected * 0.1);

        // And the ratio, which is what says the separation is the mobility rather than two
        // numbers that happen to land near two others.
        Assert.Equal(fast.ZeroFieldSi / slow.ZeroFieldSi, slowX / fastX, 0.15);
    }

    /// <summary>
    /// One species feels the other's charge - and, with the self-field off, does not feel it at
    /// all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pair is the assertion. Adding a second population changes what the first one does
    /// only through the potential their total charge raises, so with the mean field switched
    /// off the first species must be <b>bit-identical</b> whether or not the second is there,
    /// and with it switched on it must move. Either half alone is much weaker: a run that
    /// differs proves only that something changed, and a run that does not proves only that
    /// nothing was wired up.
    /// </para>
    /// <para>
    /// <b>The direction is predicted, not merely the difference.</b> The second population sits
    /// to the right of the first, so it pushes the first to the left. The first species' own
    /// charge is in there too and pushes it outward symmetrically, which spreads it and cannot
    /// move its centre - so a centroid shift is the neighbour's doing and its sign is not a
    /// coin toss.
    /// </para>
    /// <para>
    /// Both species are given the same mobility and charge on purpose. The step is shared and
    /// is the shortest any species needs, so a second species with a different mobility would
    /// change the first one's step sequence and break bit-equality for a reason that has
    /// nothing to do with the coupling being measured.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneSpeciesFeelsTheOthersChargeAndOnlyThroughTheField()
    {
        var grid = Grid2D.OverBox(-0.01, -0.003, 0.01, 0.003, 128, 32);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var mobility = Mobility.FromCrossSection(gas, species);
        var field = UniformField.Create(new Vec3(0.0, 0.0, 0.0));
        var seconds = 2e-5;

        MixtureMember Left() => new("left", species, mobility, Blob(grid, -0.003, 6e-4, 4e8));
        MixtureMember Right() => new("right", species, mobility, Blob(grid, 0.003, 6e-4, 4e8));

        DensitySelfField Charged() => new(
            grid, cylindrical: false, AbsorbingCells.None, Bore, species.ChargeSi);

        // Blind: the second species is present and is not felt.
        var aloneBlind = MixtureDiffusion.Run([Left()], field, gas, seconds, Bore);
        var pairBlind = MixtureDiffusion.Run([Left(), Right()], field, gas, seconds, Bore);

        var blindDifferences = 0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (!aloneBlind.Species[0].Density[i, j].Equals(pairBlind.Species[0].Density[i, j]))
                {
                    blindDifferences++;
                }
            }
        }

        // Charged: it is.
        var aloneCharged = MixtureDiffusion.Run(
            [Left()], field, gas, seconds, Bore, selfField: Charged());
        var pairCharged = MixtureDiffusion.Run(
            [Left(), Right()], field, gas, seconds, Bore, selfField: Charged());

        var (aloneX, _) = aloneCharged.Species[0].Density.Centroid();
        var (pairX, _) = pairCharged.Species[0].Density.Centroid();

        output.WriteLine($"self-field off: {blindDifferences} of {grid.CountX * grid.CountY} nodes of "
            + "'left' differ when 'right' is added");
        output.WriteLine($"self-field on:  'left' centred at {aloneX * 1e6:F2} um alone, "
            + $"{pairX * 1e6:F2} um with 'right' present - displaced {(pairX - aloneX) * 1e6:F2} um");
        output.WriteLine($"peak self-potential {aloneCharged.PeakSelfPotentialVolts:G4} V alone, "
            + $"{pairCharged.PeakSelfPotentialVolts:G4} V as a pair; net charge "
            + $"{pairCharged.NetChargeSi:G4} C");

        Assert.Equal(0, blindDifferences);
        Assert.True(
            pairX < aloneX,
            $"'left' moved to {pairX * 1e6:F2} um from {aloneX * 1e6:F2} um with a like charge sitting to "
            + "its right, which should have pushed it the other way");

        // Large enough to be the neighbour rather than the arithmetic.
        Assert.True(
            Math.Abs(pairX - aloneX) > 1e-6,
            $"'left' moved only {(pairX - aloneX) * 1e9:F2} nm, which is too little to be a force");
    }

    /// <summary>
    /// Opposite polarities cancel in the shared source, which is what says it is a signed sum.
    /// </summary>
    /// <remarks>
    /// The sharpest cheap test of the mixture self-field, because it cannot be passed by an
    /// implementation that sums magnitudes or that solves each species separately and adds the
    /// potentials afterwards - the second would give the same answer here, in fact, but not
    /// once conductors are present, and this is the case that pins the sign. A cation and an
    /// anion of equal charge on top of one another raise no field at all; either alone raises a
    /// great deal.
    /// </remarks>
    [Fact]
    public void OppositePolaritiesCancelInTheSharedSource()
    {
        var grid = Grid2D.OverBox(-0.01, -0.01, 0.01, 0.01, 64, 64);
        var charge = 1.602176634e-19;

        var positive = Blob(grid, 0.0, 1.5e-3, 1e8);
        var negative = Blob(grid, 0.0, 1.5e-3, 1e8);

        var alone = new DensitySelfField(grid, false, AbsorbingCells.None, Bore, charge);
        Assert.True(alone.Refresh([positive], [charge]));

        var neutral = new DensitySelfField(grid, false, AbsorbingCells.None, Bore, charge);
        Assert.True(neutral.Refresh([positive, negative], [charge, -charge]));

        // The scale that decides whether a self-potential matters to a density at all, and the
        // one the mean-field warning already reports against: a well shallower than the
        // temperature does not hold anything. A bar in volts would be a number picked to pass.
        var thermal = BackgroundGas.BoltzmannSi * 300.0 / 1.602176634e-19;

        output.WriteLine($"one polarity:  peak {alone.PeakVolts:G4} V, net charge {alone.ChargeSi:G4} C");
        output.WriteLine($"both, equal:   peak {neutral.PeakVolts:G4} V, net charge {neutral.ChargeSi:G4} C");
        output.WriteLine($"kT/q at 300 K: {thermal:G4} V, so the control is "
            + $"{alone.PeakVolts / thermal:F1}x the scale that would matter");

        Assert.True(
            alone.PeakVolts > 10.0 * thermal,
            $"the control raised {alone.PeakVolts:G4} V against a thermal {thermal:G4} V, which is too "
            + "little for a cancellation of it to mean anything");

        // Exactly zero, not nearly. Each node's source is the signed sum of q_s n_s over
        // species, and two bit-identical densities carrying q and -q contribute terms that are
        // exact negatives, so the sum is exactly 0.0 and the solve returns exactly 0 V. An
        // implementation that solved each species separately and added the potentials
        // afterwards would land near zero with solver round-off in it rather than on it, so
        // this is the assertion that tells the two apart.
        Assert.Equal(0.0, neutral.PeakVolts);
        Assert.Equal(0.0, neutral.ChargeSi);
    }

    /// <summary>
    /// The shared step is the shortest any species needed, and the result names which one.
    /// </summary>
    /// <remarks>
    /// It has to be shared: a mutual field between densities evaluated at different times is
    /// not a field between anything. So a mixture costs what its most demanding member costs,
    /// and that is worth reporting rather than leaving to be inferred - a run ten times dearer
    /// than expected is usually paying for one species, and this is the number that says so.
    /// </remarks>
    [Fact]
    public void TheSharedStepIsTheShortestAnySpeciesNeeded()
    {
        var grid = Grid2D.OverBox(-0.01, -0.003, 0.01, 0.003, 128, 32);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = UniformField.Create(new Vec3(400.0, 0.0, 0.0));

        var quick = Mobility.FromCrossSection(gas, species);
        var sluggish = new Mobility { ZeroFieldSi = quick.ZeroFieldSi * 0.1 };

        var result = MixtureDiffusion.Run(
            [
                new MixtureMember("sluggish", species, sluggish, Blob(grid, -0.005, 8e-4, 1e5)),
                new MixtureMember("quick", species, quick, Blob(grid, -0.005, 8e-4, 1e5)),
            ],
            field, gas, 2e-5, Closed);

        var sluggishStep = result.Species[0].StableStepSeconds;
        var quickStep = result.Species[1].StableStepSeconds;

        output.WriteLine($"sluggish alone would take {sluggishStep:G4} s, quick {quickStep:G4} s");
        output.WriteLine($"the mixture took {result.StepSeconds:G4} s, set by '{result.StepSetBy}' "
            + $"- {sluggishStep / quickStep:F1}x shorter than the sluggish species needed");

        Assert.Equal(Math.Min(sluggishStep, quickStep), result.StepSeconds);
        Assert.Equal("quick", result.StepSetBy);

        // And the two really do differ, or the assertion above is satisfied by a coincidence.
        Assert.True(quickStep < 0.5 * sluggishStep,
            "the two species need near enough the same step, so which one set it says nothing");
    }

    /// <summary>
    /// And every species stays conserved, which is what the shared step is protecting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bookkeeping test above says the step <em>is</em> the shortest; this says what goes
    /// wrong when it is not, and the answer is not the one I expected. <b>An overlong explicit
    /// step does not drive this density negative - it creates ions.</b> Told to take the
    /// sluggish member's step, the mixture finishes with 1.010e6 of the 1e6 ions the quick
    /// member was launched with, a per cent conjured out of nothing, while the lowest density
    /// anywhere stays at zero. A first version of this test asserted non-negativity, which is
    /// the textbook failure mode of an unstable explicit scheme, and it <b>passed with the
    /// wrong-step mutation restored</b>: no teeth at all.
    /// </para>
    /// <para>
    /// So what is asserted is the conservation law, which is the sharper thing anyway - a
    /// population that grows has no defensible reading, where a small negative density might be
    /// argued as round-off. Positivity is checked too, since it costs a pass over the grid, but
    /// it is not what carries this test.
    /// </para>
    /// <para>
    /// The sluggish species is listed <em>first</em> on purpose. An implementation that took
    /// the first member's step, or the last, or an average, is caught here only if the member it
    /// must not outrun is not the one it would happen to pick.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSharedStepKeepsEverySpeciesConserved()
    {
        var grid = Grid2D.OverBox(-0.01, -0.003, 0.01, 0.003, 128, 32);
        var gas = Nitrogen(100.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var field = UniformField.Create(new Vec3(600.0, 0.0, 0.0));
        var launched = 1e6;

        var quick = Mobility.FromCrossSection(gas, species);
        var sluggish = new Mobility { ZeroFieldSi = quick.ZeroFieldSi * 0.05 };

        var result = MixtureDiffusion.Run(
            [
                new MixtureMember("sluggish", species, sluggish, Blob(grid, -0.005, 8e-4, launched)),
                new MixtureMember("quick", species, quick, Blob(grid, -0.005, 8e-4, launched)),
            ],
            field, gas, 1e-5, Closed, scheme: StepScheme.Explicit);

        output.WriteLine($"{result.Steps} step(s) at {result.StepSeconds:G4} s, set by '{result.StepSetBy}'; "
            + $"the sluggish species alone would have taken {result.Species[0].StableStepSeconds:G4} s");

        foreach (var member in result.Species)
        {
            var least = double.PositiveInfinity;

            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    least = Math.Min(least, member.Density[i, j]);
                }
            }

            // Every edge reflects and there is no conductor, so nothing may leave: whatever was
            // launched is still here, and the ledger has to close exactly rather than nearly.
            var accounted = member.Population + member.Collected + member.Losses.Values.Sum();

            output.WriteLine($"'{member.Name}': {accounted:G12} ions accounted of {launched:G12} launched "
                + $"({(accounted - launched) / launched:P4}), lowest density {least:G4} m^-3");

            Assert.Equal(launched, accounted, launched * 1e-9);
            Assert.True(least >= 0.0, $"'{member.Name}' went to {least:G4} m^-3");
        }

        // The two really do want different steps, or nothing was at risk.
        Assert.True(
            result.Species[0].StableStepSeconds > 5.0 * result.Species[1].StableStepSeconds,
            "the two species want near enough the same step, so an implementation that took the wrong "
            + "one would not have been caught here");
    }

    /// <summary>
    /// A driven mixture is refused unless every species brings its own well.
    /// </summary>
    /// <remarks>
    /// The cycle-averaged potential depends on charge, mass and momentum-transfer rate, so one
    /// RF structure presents a different effective well to every species in it - which is how a
    /// driven guide is mass-selective at all. Handed one pseudopotential, a mixture would give
    /// every species the well built for whichever one that wrapper came from, and the result
    /// would look exactly like a correct one. Refused by name, with what to do instead.
    /// </remarks>
    [Fact]
    public void ADrivenMixtureNeedsOneWellPerSpecies()
    {
        var grid = Grid2D.OverBox(-0.002, -0.002, 0.002, 0.002, 32, 32);
        var gas = Nitrogen(100.0);

        var light = IonSpecies.FromMassToCharge(200.0, 1);
        var heavy = IonSpecies.FromMassToCharge(2000.0, 1);
        var mobility = Mobility.FromCrossSection(gas, light);

        var driven = IdealQuadrupoleRf.Create(
            Quantity.From(0.0, "V"), Quantity.From(200.0, "V"),
            Quantity.From(1.0e6, "Hz"), Quantity.From(2.0e-3, "m"));

        var shared = new PonderomotiveField(driven, light.ChargeSi, light.MassSi, 0.0);

        var refused = Assert.Throws<ArgumentException>(() => MixtureDiffusion.Run(
            [
                new MixtureMember("light", light, mobility, Blob(grid, 0.0, 3e-4, 1e5)),
                new MixtureMember("heavy", heavy, mobility, Blob(grid, 0.0, 3e-4, 1e5)),
            ],
            shared, gas, 1e-6, Closed));

        output.WriteLine(refused.Message);

        Assert.Contains("cycle-averaged well built for another species", refused.Message, StringComparison.Ordinal);

        // And with a well each it runs - so the refusal is about the wells, not about driving.
        var perSpecies = MixtureDiffusion.Run(
            [
                new MixtureMember("light", light, mobility, Blob(grid, 0.0, 3e-4, 1e5),
                    new PonderomotiveField(driven, light.ChargeSi, light.MassSi, 0.0)),
                new MixtureMember("heavy", heavy, mobility, Blob(grid, 0.0, 3e-4, 1e5),
                    new PonderomotiveField(driven, heavy.ChargeSi, heavy.MassSi, 0.0)),
            ],
            shared, gas, 1e-6, Closed);

        var lightWell = new PonderomotiveField(driven, light.ChargeSi, light.MassSi, 0.0)
            .PotentialAt(new Vec3(1e-3, 0.0, 0.0));
        var heavyWell = new PonderomotiveField(driven, heavy.ChargeSi, heavy.MassSi, 0.0)
            .PotentialAt(new Vec3(1e-3, 0.0, 0.0));

        output.WriteLine($"at 1 mm off axis the same RF is worth {lightWell:G4} V to m/z 200 and "
            + $"{heavyWell:G4} V to m/z 2000 - a factor of {lightWell / heavyWell:F1}");

        Assert.Equal(2, perSpecies.Species.Count);

        // The well goes as 1/m, which is the whole reason a shared one is refused.
        Assert.Equal(10.0, lightWell / heavyWell, 0.2);
    }
}
