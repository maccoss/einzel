using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The axisymmetric operator: what the extra radial term has to get right.
/// </summary>
/// <remarks>
/// <para>
/// SYM-1 asks for cylindrical symmetry, and section 22 calls it load-bearing for
/// funnels. It is not a change of presentation - in cylindrical coordinates the
/// radial part of the Laplacian is (1/r) d/dr (r dphi/dr) rather than d2phi/dr2,
/// because a ring of given thickness has less circumference the closer it sits to
/// the axis. Solve with the plane operator and the answer converges perfectly well
/// to the wrong field.
/// </para>
/// <para>
/// Two closed forms carry these tests. A coaxial pair has phi = A ln(r) + B in both
/// geometries, which sounds like it proves nothing and in fact proves the most: the
/// plane solver gets a *linear* profile there, so agreeing with the logarithm is
/// exactly the thing the radial weighting is responsible for. And a charge-free
/// region on the axis has a potential quadratic in r, which is where the axis
/// stencil either has its factor of four or does not.
/// </para>
/// </remarks>
public sealed class AxisymmetricSolveTests(ITestOutputHelper output)
{
    private const double Applied = 100.0;

    private static readonly int[] Refinements = [32, 64, 128, 256];

    /// <summary>
    /// A coaxial pair: inner conductor at <paramref name="innerRadius"/> held at
    /// <see cref="Applied"/>, grounded outer wall, and no dependence on the axial
    /// coordinate.
    /// </summary>
    /// <remarks>
    /// Neumann on both axial edges so the solve is genuinely one-dimensional in r.
    /// Anything else would put end effects into a comparison against a closed form
    /// that has none.
    /// </remarks>
    private static CompiledSolvedField Coaxial(double innerRadius, double outerRadius, int cells)
    {
        return new CompiledSolvedField
        {
            MinX = 0.0,
            MaxX = 0.02,
            MinY = 0.0,
            MaxY = outerRadius,
            CellSize = outerRadius / cells,
            Symmetry = SolveSymmetry.Cylindrical,
            LeftEdge = BoundaryKind.Neumann,
            RightEdge = BoundaryKind.Neumann,
            BottomEdge = BoundaryKind.Neumann,
            Tolerance = 1e-12,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "inner",
                    Shape = ElectrodeShape.Rectangle,
                    MinX = -1.0,
                    MaxX = 1.0,
                    MinY = -1.0,
                    MaxY = innerRadius,
                    Potential = Applied,
                },
            ],
        };
    }

    [Fact]
    public void ACoaxialSolveRecoversTheLogarithm()
    {
        // The check that the radial weighting exists at all. Between two coaxial
        // cylinders the potential goes as ln(r); across two parallel plates it goes
        // as r. The plane operator produces the second, so this separates them.
        const double Inner = 0.004;
        const double Outer = 0.02;

        var (field, report) = GeometryBuilder.Build(Coaxial(Inner, Outer, cells: 128));

        output.WriteLine($"{report.Cycles} cycles at factor {report.ConvergenceFactor:F4}");
        output.WriteLine("     r / mm      solved        ln law      linear");

        var a = Applied / Math.Log(Inner / Outer);
        var b = -a * Math.Log(Outer);

        var worstLog = 0.0;
        var worstLinear = 0.0;

        for (var k = 1; k <= 8; k++)
        {
            var r = Inner + ((Outer - Inner) * k / 9.0);
            var point = new Core.Geometry.Vec3(0.01, r, 0.0);

            var solved = field.PotentialAt(in point);
            var logLaw = (a * Math.Log(r)) + b;
            var linear = Applied * (Outer - r) / (Outer - Inner);

            worstLog = Math.Max(worstLog, Math.Abs(solved - logLaw));
            worstLinear = Math.Max(worstLinear, Math.Abs(solved - linear));

            output.WriteLine($"{r * 1e3,10:F2}  {solved,10:F4}  {logLaw,12:F4}  {linear,10:F4}");
        }

        output.WriteLine($"\nworst departure from ln    {worstLog:E3} V");
        output.WriteLine($"worst departure from linear {worstLinear:E3} V");

        // The logarithm, not the ramp. The second assertion is the one that would
        // have caught a solve that quietly used the plane operator, because at this
        // radius ratio the two profiles differ by volts.
        Assert.True(worstLog < 0.05, $"departed from the logarithm by {worstLog:F4} V");
        Assert.True(
            worstLinear > 20.0 * worstLog,
            $"the linear profile is only {worstLinear:F4} V away, so this geometry cannot tell them apart");
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void TheAxisymmetricOperatorConvergesAtSecondOrder(int cells)
    {
        // Reported per refinement so the ratio is visible; the ordering assertion
        // is in the test below. Order is the property that says the operator is the
        // right one - a wrong stencil can be accurate at one mesh by coincidence and
        // will not halve its error four times over.
        const double Inner = 0.004;
        const double Outer = 0.02;

        var error = CoaxialError(Inner, Outer, cells);

        output.WriteLine($"{cells,4} cells   worst error {error:E4} V");

        Assert.True(error < 1.0);
    }

    [Fact]
    public void RefiningQuartersTheError()
    {
        const double Inner = 0.004;
        const double Outer = 0.02;

        var errors = Refinements.Select(c => CoaxialError(Inner, Outer, c)).ToArray();

        output.WriteLine("  cells     worst error      observed order");

        for (var k = 0; k < errors.Length; k++)
        {
            var order = k == 0 ? double.NaN : Math.Log2(errors[k - 1] / errors[k]);
            output.WriteLine($"{Refinements[k],7}   {errors[k],13:E4}   {(double.IsNaN(order) ? "" : order.ToString("F2")),16}");
        }

        // Second order for a five-point stencil with cut cells at the conductor.
        // The nominal is 2; below about 1.7 the radial weighting would be only
        // approximately right, which is the failure that looks like accuracy.
        for (var k = 1; k < errors.Length; k++)
        {
            var order = Math.Log2(errors[k - 1] / errors[k]);

            Assert.True(
                order is > 1.7 and < 2.3,
                $"refining from {Refinements[k - 1]} to {Refinements[k]} cells gave order {order:F2}, not two");
        }
    }

    private static double CoaxialError(double inner, double outer, int cells)
    {
        var (field, _) = GeometryBuilder.Build(Coaxial(inner, outer, cells));

        var a = Applied / Math.Log(inner / outer);
        var b = -a * Math.Log(outer);

        var worst = 0.0;

        // Sampled away from the conductor rather than right up against it. The
        // innermost point of a tighter sweep sits inside the cut cell at a coarse
        // mesh, where the interpolant is at its worst, and that interpolation error
        // would be measured and reported as the operator's.
        for (var k = 0; k <= 16; k++)
        {
            var r = inner + ((outer - inner) * (0.15 + (0.8 * k / 16.0)));
            var point = new Core.Geometry.Vec3(0.01, r, 0.0);

            worst = Math.Max(worst, Math.Abs(field.PotentialAt(in point) - ((a * Math.Log(r)) + b)));
        }

        return worst;
    }

    [Fact]
    public void TheFieldVanishesOnTheAxisAndGrowsQuadraticallyOffIt()
    {
        // Every axisymmetric field is flat at the axis - there is no direction for
        // a radial field to point in - so the potential near it goes as r squared
        // and the field as r. This is where the axis stencil earns its factor of
        // four: treat the axis as an ordinary mirror plane and the curvature comes
        // out half what it should be, which is a wrong answer that looks entirely
        // reasonable.
        //
        // A tube with one end at a potential and the rest grounded gives a field
        // that penetrates along the axis and is smooth there.
        var solve = new CompiledSolvedField
        {
            MinX = 0.0,
            MaxX = 0.04,
            MinY = 0.0,
            MaxY = 0.01,
            CellSize = 0.01 / 96.0,
            Symmetry = SolveSymmetry.Cylindrical,
            BottomEdge = BoundaryKind.Neumann,
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
                    MaxY = 0.01,
                    Potential = Applied,
                },
            ],
        };

        var (field, _) = GeometryBuilder.Build(solve);

        var onAxis = new Core.Geometry.Vec3(0.01, 0.0, 0.0);
        var centre = field.PotentialAt(in onAxis);

        output.WriteLine($"on axis {centre:F5} V");
        output.WriteLine("   r / mm    phi - phi(0)      / r^2");

        var quadratic = new List<double>();

        foreach (var r in new[] { 5e-4, 1e-3, 1.5e-3, 2e-3 })
        {
            var point = new Core.Geometry.Vec3(0.01, r, 0.0);
            var delta = field.PotentialAt(in point) - centre;

            quadratic.Add(delta / (r * r));
            output.WriteLine($"{r * 1e3,9:F2}  {delta,14:E4}  {delta / (r * r),12:E4}");
        }

        // A pure r-squared law would make every ratio equal. Higher terms bend it,
        // so the bar is that they agree to a few per cent across a factor of four
        // in radius - which a linear-in-r potential could not do at all.
        var spread = (quadratic.Max() - quadratic.Min()) / Math.Abs(quadratic.Average());

        output.WriteLine($"\nratios agree to {spread:P1} over a factor of four in radius");

        Assert.True(spread < 0.10, $"the potential is not quadratic in r near the axis; ratios spread {spread:P1}");

        // And the radial field must vanish on the axis itself.
        var axisField = field.ElectricFieldAt(in onAxis);

        output.WriteLine($"radial field on axis {axisField.Y:E3} V/m");
        Assert.True(Math.Abs(axisField.Y) < 1e-6, $"the radial field on the axis is {axisField.Y:E3} V/m, not zero");
    }
}
