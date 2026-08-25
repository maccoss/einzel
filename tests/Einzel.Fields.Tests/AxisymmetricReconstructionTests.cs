using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The half-plane solve, presented as the field in space.
/// </summary>
/// <remarks>
/// SYM-1 asks that "the interpolant reconstructs the full field transparently".
/// These check the two halves of that: that a rectangle in the half-plane really
/// behaves as a ring in space, and that the reconstruction has no seam - no
/// preferred azimuth, and nothing peculiar on the axis where every azimuth meets.
/// </remarks>
public sealed class AxisymmetricReconstructionTests(ITestOutputHelper output)
{
    private const double Applied = 100.0;
    private const double TubeRadius = 0.01;

    /// <summary>
    /// A grounded tube with a charged cap across one end.
    /// </summary>
    /// <remarks>
    /// Chosen because it has an exact answer. Inside a grounded cylinder the field
    /// from an end cap decays along the axis as exp(-j x / R) with j the first zero
    /// of the Bessel function J0, 2.40483 - the radial eigenfunction of the
    /// cylindrical Laplacian, so the decay rate is a direct readout of whether the
    /// radial operator is the right one. The plane Laplacian gives pi/2 = 1.5708
    /// for the same geometry, which is 35% away and unmistakable.
    /// </remarks>
    private static IElectrostaticField Tube(int cellsPerRadius = 96)
    {
        var solve = new CompiledSolvedField
        {
            MinX = 0.0,
            MaxX = 0.05,
            MinY = 0.0,
            MaxY = TubeRadius,
            CellSize = TubeRadius / cellsPerRadius,
            Symmetry = SolveSymmetry.Cylindrical,
            Tolerance = 1e-12,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "cap",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = 0.0,
                    MaxX = 0.0,
                    MinY = 0.0,
                    MaxY = TubeRadius,
                    Potential = Applied,
                },
            ],
        };

        return GeometryBuilder.Build(solve).Field;
    }

    [Fact]
    public void FieldPenetrationDecaysAtTheFirstBesselZero()
    {
        // The closed form that is specific to this geometry. Any radial operator
        // gives an exponential decay; only the cylindrical one gives this rate.
        //
        // The field inside the tube is a sum of radial modes, each decaying at its
        // own Bessel zero - 2.405, then 5.520, then 8.654. Near the cap they are all
        // present, so the local rate is faster than the first alone; by two radii in
        // the second mode is down by exp(-11) and the first is all that is left. So
        // the test is that the rate *converges* to j01 as the higher modes die,
        // which is a sharper statement than any single number would be.
        const double FirstBesselZero = 2.404825557695773;

        var field = Tube();

        output.WriteLine("   x / R      phi on axis      local decay rate");

        var rates = new List<(double Position, double Rate)>();
        var previous = 0.0;

        for (var k = 8; k <= 28; k++)
        {
            var x = TubeRadius * k / 8.0;
            var point = new Vec3(x, 0.0, 0.0);
            var phi = field.PotentialAt(in point);

            if (k > 8)
            {
                // d(ln phi)/d(x/R), which the first mode alone makes constant.
                var rate = -Math.Log(phi / previous) / (1.0 / 8.0);
                rates.Add((x / TubeRadius, rate));

                output.WriteLine($"{x / TubeRadius,8:F3}  {phi,15:E4}  {rate,17:F4}");
            }

            previous = phi;
        }

        var far = rates.Where(r => r.Position >= 2.5).Select(r => r.Rate).ToList();
        var mean = far.Average();

        output.WriteLine(string.Empty);
        output.WriteLine($"beyond two and a half radii the rate is {mean:F5} per radius");
        output.WriteLine($"first Bessel zero                        {FirstBesselZero:F5}");
        output.WriteLine($"the plane operator would give pi/2 =     {Math.PI / 2.0:F5}");

        // Monotone toward the limit, and from below. The cap is a disc at uniform
        // potential, whose expansion coefficients go as 1 / (j_n J1(j_n)) - and
        // J1 alternates in sign at its zeros, so the second mode enters negative.
        // Near the cap it subtracts, which makes the sum decay more slowly than the
        // first mode alone, and the apparent rate climbs to j01 as it dies out.
        // A rate that wandered would be numerical noise rather than a mode spectrum.
        for (var k = 1; k < rates.Count; k++)
        {
            Assert.True(
                rates[k].Rate > rates[k - 1].Rate - 1e-6,
                $"the decay rate fell from {rates[k - 1].Rate:F4} to {rates[k].Rate:F4} at "
                + $"{rates[k].Position:F2} radii, so it is not a decaying mode sum");
        }

        Assert.True(
            rates[0].Rate < FirstBesselZero,
            $"the rate starts at {rates[0].Rate:F4}, above j01, so the second mode is not entering negative");

        Assert.True(
            Math.Abs(mean - FirstBesselZero) / FirstBesselZero < 0.005,
            $"far-field decay rate {mean:F5} against the first Bessel zero {FirstBesselZero:F5}");

        // And unmistakably not the plane operator, which would give pi/2.
        Assert.True(
            Math.Abs(mean - (Math.PI / 2.0)) > 0.5,
            "the measured rate is close to pi/2, which is what a plane Laplacian would give");
    }

    [Fact]
    public void TheFieldHasNoPreferredAzimuth()
    {
        // The reconstruction must have no seam. Sampling the same radius all the
        // way round has to give the same potential and a radial field of the same
        // magnitude pointing outward along each azimuth.
        var field = Tube();

        const double Radius = 0.006;
        var x = 0.008;

        var potentials = new List<double>();
        var radials = new List<double>();

        foreach (var degrees in new[] { 0.0, 37.0, 90.0, 143.0, 180.0, 251.0, 300.0 })
        {
            var theta = degrees * Math.PI / 180.0;
            var point = new Vec3(x, Radius * Math.Cos(theta), Radius * Math.Sin(theta));

            var strength = field.ElectricFieldAt(in point);

            potentials.Add(field.PotentialAt(in point));

            // The component along the outward radial direction, which is the whole
            // of the transverse field if the reconstruction is right.
            var outward = (strength.Y * Math.Cos(theta)) + (strength.Z * Math.Sin(theta));
            radials.Add(outward);

            // And nothing azimuthal: an axisymmetric field has no way to make one.
            var azimuthal = (-strength.Y * Math.Sin(theta)) + (strength.Z * Math.Cos(theta));

            output.WriteLine(
                $"{degrees,6:F0} deg   phi {potentials[^1],10:F5}   E_r {outward,12:E4}   E_theta {azimuthal,10:E2}");

            Assert.True(
                Math.Abs(azimuthal) < 1e-9 * Math.Max(1.0, Math.Abs(outward)),
                $"an azimuthal field of {azimuthal:E3} V/m appeared at {degrees:F0} degrees");
        }

        Assert.Equal(potentials[0], potentials.Max(), 10);
        Assert.Equal(potentials[0], potentials.Min(), 10);
        Assert.Equal(radials[0], radials.Max(), 10);
        Assert.Equal(radials[0], radials.Min(), 10);
    }

    [Fact]
    public void AnIonOnTheAxisStaysOnIt()
    {
        // The consequence that matters for a run rather than for a plot. If the
        // reconstruction left any transverse field on the axis, an ion launched
        // exactly along it would drift off - slowly, plausibly, and in whichever
        // direction the interpolant happened to lean.
        var field = Tube();

        for (var k = 1; k <= 10; k++)
        {
            var point = new Vec3(0.05 * k / 11.0, 0.0, 0.0);
            var strength = field.ElectricFieldAt(in point);

            Assert.Equal(0.0, strength.Y);
            Assert.Equal(0.0, strength.Z);
            Assert.True(Math.Abs(strength.X) > 0.0, "there is no axial field either, so nothing was solved");
        }

        output.WriteLine("transverse field is exactly zero at every sampled point on the axis");
    }

    [Fact]
    public void ARectangleInTheHalfPlaneIsARingInSpace()
    {
        // What makes an axisymmetric template able to express a lens element or a
        // funnel plate: the electrode wraps all the way round. A cross-section
        // solve would make the same declaration into a pair of bars.
        var solve = new CompiledSolvedField
        {
            MinX = -0.01,
            MaxX = 0.01,
            MinY = 0.0,
            MaxY = 0.01,
            CellSize = 0.01 / 48.0,
            Symmetry = SolveSymmetry.Cylindrical,
            Tolerance = 1e-10,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "ring",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = -0.001,
                    MaxX = 0.001,
                    MinY = 0.004,
                    MaxY = 0.006,
                    Potential = Applied,
                },
            ],
        };

        var field = GeometryBuilder.Build(solve).Field;
        var bounded = Assert.IsAssignableFrom<IConductorBounded>(field);

        foreach (var degrees in new[] { 0.0, 45.0, 90.0, 200.0, 315.0 })
        {
            var theta = degrees * Math.PI / 180.0;

            // Mid-ring, at 5 mm radius: inside the conductor at every azimuth.
            var inside = new Vec3(0.0, 0.005 * Math.Cos(theta), 0.005 * Math.Sin(theta));

            // And on the axis of the same ring: through the hole, at every azimuth.
            var through = new Vec3(0.0, 0.001 * Math.Cos(theta), 0.001 * Math.Sin(theta));

            var inDistance = bounded.SignedDistanceToConductor(in inside);
            var throughDistance = bounded.SignedDistanceToConductor(in through);

            output.WriteLine(
                $"{degrees,6:F0} deg   at 5 mm {inDistance * 1e3,8:F3} mm   at 1 mm {throughDistance * 1e3,8:F3} mm");

            Assert.True(inDistance < 0.0, $"the ring has a gap at {degrees:F0} degrees");
            Assert.True(throughDistance > 0.0, $"the ring has no hole at {degrees:F0} degrees");
            Assert.Equal("ring", bounded.ConductorAt(in inside));
        }
    }
}
