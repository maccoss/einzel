using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// A packet held still by a field against a moving gas, and how wide it settles.
/// </summary>
/// <remarks>
/// <para>
/// This is what a trapped-ion mobility analyser does, and the width is its resolution
/// floor. Near the point where the field's push balances the gas drag the net drift is
/// linear in displacement — <c>v(x) = K E(x) − u</c>, zero at <c>x0</c> — so the
/// stationary state of drift against diffusion is
/// </para>
/// <code>
///     dn/dx · D = −n · v    →    n ∝ exp( −K |E'| (x − x0)² / 2D )
/// </code>
/// <para>
/// a Gaussian of width <c>σ² = D / (K |E'|)</c>. <b>The Einstein relation then cancels
/// the mobility</b>, leaving <c>σ² = (kT/q) / |dE/dx|</c>: the width depends on the gas
/// temperature and the axial field gradient and on <em>nothing else</em> — not the ion,
/// not the gas speed, not the pressure. Those set <em>where</em> the packet parks and not
/// how wide it is.
/// </para>
/// <para>
/// <b>The cancellation is the discriminating claim, so it is the control.</b> A width
/// matching one formula on one configuration is consistent with several wrong ones; two
/// mobilities parking two millimetres apart and settling to the same width is not.
/// </para>
/// <para>
/// <b>The two assertions catch different failure modes, and I first wrote them the wrong
/// way round.</b> The claim here used to be that a broken drift-to-diffusion ratio would
/// move the width with the mobility. It does not, and the mutations say so exactly:
/// </para>
/// <list type="table">
/// <item><description>
/// <b>Einstein relation inflated 1.44x</b> — parking point 3.4722 mm against 5.0000
/// predicted, which is 1/1.44 to five figures, and the width <b>unchanged at 1.0000 mm</b>.
/// </description></item>
/// <item><description>
/// <b>Thermal voltage in the flux inflated 1.44x</b> — parking point 7.1998 mm, which is
/// 1.44x, and the width 1.2000 mm, which is exactly the square root of 1.44.
/// </description></item>
/// </list>
/// <para>
/// The reason is that Scharfetter–Gummel's zero-flux state is <c>exp(−qφ/kT)</c>: the
/// equilibrium involves <c>kT</c> and <em>not</em> <c>D</c>, so the width is invariant to
/// the diffusion coefficient by construction and <c>D</c> sets only how fast the packet
/// gets there. What the balance point tests is the identity <c>q Δφ/kT ≡ v h / D</c>
/// between the field term of the exponent and the gas term — <em>which is the Einstein
/// relation</em>. So the parking point is the Einstein check and the width is the
/// Boltzmann check, and neither alone covers the other.
/// </para>
/// <para>
/// <b>Distinct from <c>TheBoltzmannDistributionIsExactlyStationary</c>.</b> That test
/// seeds the equilibrium of a static potential and watches it not move, which is a
/// statement about the space discretisation. This one <em>relaxes to</em> an equilibrium
/// that only exists because the gas is moving — the field alone has no minimum here, the
/// balance point is not the field's centre, and the packet has to find it.
/// </para>
/// <para>
/// Written because the TIMS front-end study turned on exactly this number: the measured
/// axial width of a parked packet came out 0.7119 mm against 0.7143 from this closed form
/// evaluated with the solved field's own gradient, and the mobility having cancelled is
/// what makes that a statement about the analyser rather than about the ion in it.
/// </para>
/// </remarks>
public sealed class MobilityBalanceWidthTests(ITestOutputHelper output)
{
    private const double Dalton = 1.66053906892e-27;
    private const double ElementaryCharge = 1.602176634e-19;

    /// <summary>Where the reference ion is to park, which fixes the gas speed.</summary>
    /// <remarks>
    /// Five millimetres from the field's centre and five standard deviations from the
    /// wall, so the equilibrium is the field's and not the domain's. The doubled mobility
    /// parks at half of it, which is what makes the pair two configurations.
    /// </remarks>
    private const double ParkAtSi = 5.0e-3;

    private static BackgroundGas Nitrogen(double pressurePa, double driftSi) => new()
    {
        Model = CollisionModel.HardSphere,
        PressureSi = pressurePa,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        CrossSectionSi = 250e-20,
        DriftVelocitySi = new Vec3(driftSi, 0.0, 0.0),
    };

    /// <summary>
    /// The width is the closed form, and two mobilities park apart and settle to it alike.
    /// </summary>
    [Fact]
    public void TheParkedWidthIsSetByTemperatureAndGradientAlone()
    {
        // A linear restoring field, so the gradient is one number over the whole domain
        // and the closed form has nothing in it to approximate. The gradient is chosen
        // from the width it implies rather than picked: (kT/q)/sigma^2 at sigma = 1 mm,
        // which is eight cells on the mesh below - wide enough that the answer is the
        // physics rather than the mesh, which the second test then checks.
        const double GradientSi = 25852.0;

        var grid = Grid2D.OverBox(-0.02, -0.006, 0.02, 0.006, 256);
        var field = new LinearRestoringField(GradientSi, 0.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        // THE GAS SPEED IS DERIVED, NOT PICKED. The balance sits where K E + u = 0, so
        // x0 = u / (K |E'|) - and a speed chosen without reference to the mobility this
        // gas and ion actually have puts the balance point anywhere, including outside
        // the domain. A first version did exactly that: 40 m/s predicted a parking point
        // 108 mm out of a 30 mm box, the packet pressed against the wall, and the width
        // came back as the one cell it was squeezed into.
        var reference = Mobility.FromCrossSection(Nitrogen(200.0, 0.0), species).ZeroFieldSi;
        var driftSi = ParkAtSi * reference * GradientSi;

        var gas = Nitrogen(200.0, driftSi);

        var kT = BackgroundGas.BoltzmannSi * gas.TemperatureK / ElementaryCharge;
        var expected = Math.Sqrt(kT / GradientSi);

        output.WriteLine($"K {reference:E3} m^2/(V s), gas {driftSi:F3} m/s chosen to park "
            + $"the reference ion at {ParkAtSi * 1e3:F1} mm");

        output.WriteLine($"kT/q {kT:F6} V, |dE/dx| {GradientSi:F1} V/m^2");
        output.WriteLine($"closed form sigma = sqrt((kT/q)/|dE/dx|) = {expected * 1e3:F4} mm");
        output.WriteLine("");
        output.WriteLine("K / K_thermal   parks at        predicted       sigma_x        of closed form");

        var widths = new List<double>();

        // Two mobilities a factor of two apart. The scaling is applied to the derived
        // value rather than to a cross section, so the gas - and therefore the
        // temperature the closed form uses - is identical between the two runs.
        var thermal = Mobility.FromCrossSection(gas, species).ZeroFieldSi;

        foreach (var scale in new[] { 1.0, 2.0 })
        {
            var mobility = new Mobility(thermal * scale);

            // Released away from where it will end up, so the run has to find the
            // balance rather than being told it. The balance is at -u/(K |E'|), which
            // moves with the mobility - that is the half of this that is NOT invariant.
            var parks = gas.DriftVelocitySi.X / (mobility.ZeroFieldSi * GradientSi);

            var seed = Gaussian(grid, 0.0, 1.5e-3);

            // Long enough to forget the launch: the relaxation time is 1/(K |E'|), and
            // this is about fifteen of them. That it HAS settled is asserted below
            // rather than assumed.
            var seconds = 15.0 / (mobility.ZeroFieldSi * GradientSi);

            var result = DriftDiffusion.Run(
                seed, field, gas, mobility, species, seconds,
                new DriftDiffusion.DomainEdges(
                    Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting));

            var (centre, _) = result.Density.Centroid();
            var (width, _) = result.Density.Spread();

            widths.Add(width);

            output.WriteLine(
                $"{scale,13:F1}   {centre * 1e3,8:F4} mm   {parks * 1e3,8:F4} mm   "
                + $"{width * 1e3,8:F4} mm   {width / expected,14:F4}");

            // Where it parks follows the mobility, which is what makes the two runs
            // genuinely different configurations rather than one run twice - and is the
            // assertion that catches a broken Einstein relation, measured: inflating D by
            // 1.44 moves this to 0.694 of predicted and leaves the width alone.
            Assert.InRange(centre / parks, 0.97, 1.03);

            // And the width is the closed form - to every digit printed, not to the band.
            // The band is for the finite relaxation; the mesh contributes nothing, which
            // the second test measures. What this catches is a wrong thermal voltage:
            // inflating kT in the flux by 1.44 gives 1.2000 mm, its square root.
            Assert.InRange(width / expected, 0.96, 1.04);
        }

        // THE CONTROL, and the reason this test exists. Doubling the mobility halves the
        // parking distance and must leave the width alone. This is the strong half of the
        // pair: the width is invariant to the mobility AND to the diffusion coefficient,
        // because the equilibrium is exp(-q phi / kT) and neither appears in it - so a
        // width that moved with the mobility would mean the exponent had picked up a
        // dependence that does not belong in an equilibrium at all.
        output.WriteLine("");
        output.WriteLine($"width ratio between the two mobilities: {widths[1] / widths[0]:F5}");

        Assert.InRange(widths[1] / widths[0], 0.99, 1.01);
    }

    /// <summary>
    /// The width is a property of the operator rather than of the resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scharfetter–Gummel's zero-flux state is <em>exactly</em> the Boltzmann factor, so
    /// the equilibrium width is the scheme's own answer rather than an approximation
    /// converging to one — and it should therefore not move when the mesh does. Measured
    /// on the TIMS front end first, where a parked packet gave 0.7119 mm at both a 0.47 mm
    /// and a 0.23 mm cell, identical to four decimals. Asserted here on the analytic field
    /// because a mesh-independent width is the property that licenses reading an absolute
    /// width off a run at all.
    /// </para>
    /// <para>
    /// <b>It does not stand alone.</b> Both mutations above leave this test passing - the
    /// width comes back 1.2000 mm at every mesh, internally consistent and absolutely
    /// wrong - so mesh-independence is a claim about the resolution and says nothing about
    /// the value. The closed-form comparison in the test above is what pins that.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheParkedWidthDoesNotMoveWithTheMesh()
    {
        const double GradientSi = 25852.0;

        var field = new LinearRestoringField(GradientSi, 0.0);
        var species = IonSpecies.FromMassToCharge(500.0, 1);

        var reference = Mobility.FromCrossSection(Nitrogen(200.0, 0.0), species).ZeroFieldSi;
        var gas = Nitrogen(200.0, ParkAtSi * reference * GradientSi);
        var mobility = Mobility.FromCrossSection(gas, species);

        var kT = BackgroundGas.BoltzmannSi * gas.TemperatureK / ElementaryCharge;
        var expected = Math.Sqrt(kT / GradientSi);

        output.WriteLine($"closed form {expected * 1e3:F4} mm");
        output.WriteLine("intervals   cell / mm   sigma_x / mm   of closed form");

        var widths = new List<double>();

        foreach (var intervals in new[] { 64, 128, 256 })
        {
            var grid = Grid2D.OverBox(-0.02, -0.006, 0.02, 0.006, intervals);
            var seed = Gaussian(grid, 0.0, 1.5e-3);

            var result = DriftDiffusion.Run(
                seed, field, gas, mobility, species,
                15.0 / (mobility.ZeroFieldSi * GradientSi),
                new DriftDiffusion.DomainEdges(
                    Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting));

            var (width, _) = result.Density.Spread();

            widths.Add(width);

            output.WriteLine(
                $"{intervals,9}   {grid.SpacingX * 1e3,9:F4}   {width * 1e3,12:F4}   "
                + $"{width / expected,14:F4}");
        }

        // A fourfold refinement. The width moves by less than a per cent, where a
        // quantity limited by the cell would move with it.
        var spread = widths.Max() - widths.Min();

        output.WriteLine("");
        output.WriteLine($"spread across a fourfold refinement: {spread * 1e6:F2} um, "
            + $"{spread / widths[0]:P2} of the width");

        Assert.InRange(spread / widths[0], 0.0, 0.01);
    }

    /// <summary>A Gaussian on the axis, normalised to one ion.</summary>
    private static DensityField Gaussian(Grid2D grid, double centreX, double sigma)
    {
        var density = new DensityField(grid);
        var total = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = grid.X(i) - centreX;
                var dy = grid.Y(j);
                var value = Math.Exp(-(dx * dx + dy * dy) / (2.0 * sigma * sigma));

                density[i, j] = value;
                total += value * grid.SpacingX * grid.SpacingY;
            }
        }

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] /= total;
            }
        }

        return density;
    }
}

/// <summary>A field whose axial component is linear in displacement from a centre.</summary>
/// <remarks>
/// <c>E_x = −k (x − c)</c>, so <c>dE/dx = −k</c> everywhere and the closed form the tests
/// above use has one exact number in it rather than a local approximation. The potential
/// is the matching parabola. Radially field-free, so the axial and radial answers do not
/// mix.
/// </remarks>
internal sealed class LinearRestoringField(double gradientSi, double centreSi)
    : IElectrostaticField
{
    public Vec3 ElectricFieldAt(in Vec3 position) =>
        new(-gradientSi * (position.X - centreSi), 0.0, 0.0);

    public double PotentialAt(in Vec3 position)
    {
        var dx = position.X - centreSi;

        return 0.5 * gradientSi * dx * dx;
    }

    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

    public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;

    public double ResolutionLength => double.PositiveInfinity;
}
