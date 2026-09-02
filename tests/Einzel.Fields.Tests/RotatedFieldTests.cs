using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields.Analytic;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A field rigidly rotated about y, which is how a converging mirror pair is built.
/// </summary>
/// <remarks>
/// <para>
/// These exist because declaring the tilt on the electrodes and solving the tilted
/// geometry in three dimensions does not work. The signal is the anisotropy
/// <c>Ez/Ex = tan(alpha)</c>, 2.9e-4 for the Astral's 200 micron convergence, and a
/// second-order solve on the meshes that fit in memory carries more field error than
/// that. Measured against the closed form, the solved route returned 3.54, 0.011 and
/// -0.57 of the true value depending only on the width of the gaps between mirror
/// strips.
/// </para>
/// <para>
/// The property that matters, and that the solved route cannot deliver, is the last
/// test here: the anisotropy is exactly the tangent of the angle at every point,
/// because it is constructed from the geometry rather than resolved by differencing.
/// </para>
/// </remarks>
public sealed class RotatedFieldTests(ITestOutputHelper output)
{
    private const double Gradient = 80000.0;

    private static HalfSpaceUniformField Mirror(Vec3 normal) =>
        HalfSpaceUniformField.Create(
            Vec3.Zero, normal, Quantity.Si(Gradient, Dimension.ElectricField));

    private static IEnumerable<Vec3> Samples()
    {
        foreach (var x in new[] { -0.02, -0.001, 0.0005, 0.01, 0.05, 0.13 })
        {
            foreach (var y in new[] { -0.018, 0.0, 0.011 })
            {
                foreach (var z in new[] { -0.4, -0.05, 0.0, 0.17, 0.5 })
                {
                    yield return new Vec3(x, y, z);
                }
            }
        }
    }

    /// <summary>A rotation of nothing changes nothing, to the last bit.</summary>
    [Fact]
    public void ZeroRotationIsTheIdentity()
    {
        var inner = Mirror(Vec3.UnitX);
        var rotated = new RotatedField(inner, 0.0, 0.3125, 0.175);

        foreach (var p in Samples())
        {
            Assert.Equal(inner.PotentialAt(in p), rotated.PotentialAt(in p));
            var a = inner.ElectricFieldAt(in p);
            var b = rotated.ElectricFieldAt(in p);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);
        }
    }

    /// <summary>
    /// Rotating a half-space gives the half-space that was rotated - two independent
    /// routes to one field.
    /// </summary>
    /// <remarks>
    /// The control that makes the rest meaningful. If this passed only because both
    /// sides were computing zero, the anisotropy test below would still fail.
    /// </remarks>
    [Theory]
    [InlineData(9.0929e-5)]   // the Astral's own tilt: 200 micron over 350 mm
    [InlineData(1.0e-3)]
    [InlineData(0.02)]
    public void RotatingAHalfSpaceGivesTheHalfSpaceThatWasRotated(double halfTurns)
    {
        var alpha = double.Pi * halfTurns;
        var rotated = new RotatedField(Mirror(Vec3.UnitX), halfTurns, 0.0, 0.0);
        var declared = Mirror(new Vec3(Math.Cos(alpha), 0.0, -Math.Sin(alpha)));

        var worst = 0.0;
        foreach (var p in Samples())
        {
            var a = rotated.ElectricFieldAt(in p);
            var b = declared.ElectricFieldAt(in p);
            worst = Math.Max(worst, (a - b).Length / Gradient);
            Assert.Equal(declared.PotentialAt(in p), rotated.PotentialAt(in p), 1e-9);
        }

        output.WriteLine($"half turns {halfTurns}: worst field difference {worst:E3} of the gradient");
        Assert.True(worst < 1e-12, $"worst relative field difference {worst:E3}");
    }

    /// <summary>Rotating back recovers the original field.</summary>
    [Fact]
    public void RotatingBackRecoversTheOriginal()
    {
        var inner = Mirror(Vec3.UnitX);
        var there = new RotatedField(inner, 0.013, 0.3125, 0.175);
        var back = new RotatedField(there, -0.013, 0.3125, 0.175);

        foreach (var p in Samples())
        {
            Assert.Equal(inner.PotentialAt(in p), back.PotentialAt(in p), 1e-9);
            var a = inner.ElectricFieldAt(in p);
            var b = back.ElectricFieldAt(in p);
            Assert.Equal(0.0, (a - b).Length / Gradient, 1e-12);
        }
    }

    /// <summary>
    /// The anisotropy is exactly the tangent of the angle, everywhere inside the field.
    /// </summary>
    /// <remarks>
    /// This is the whole point, and the property a solved tilted geometry gets wrong by
    /// factors of several. <c>Ez/Ex</c> is what turns the drift around in an
    /// asymmetric-track analyser, and here it is constructed rather than resolved, so it
    /// carries no discretisation error at all - the assertion is at 1e-14, not at a
    /// tolerance chosen to pass.
    /// </remarks>
    [Theory]
    [InlineData(9.0929e-5)]
    [InlineData(1.0e-3)]
    [InlineData(0.02)]
    public void TheAnisotropyIsExactlyTheTangentOfTheAngle(double halfTurns)
    {
        var rotated = new RotatedField(Mirror(Vec3.UnitX), halfTurns, 0.0, 0.0);
        var expected = -Math.Tan(double.Pi * halfTurns);

        var worst = 0.0;
        var counted = 0;
        foreach (var p in Samples())
        {
            var e = rotated.ElectricFieldAt(in p);
            if (Math.Abs(e.X) < 1.0)
            {
                continue;   // outside the field region, where the ratio is 0/0
            }

            counted++;
            worst = Math.Max(worst, Math.Abs((e.Z / e.X) - expected));
        }

        output.WriteLine($"half turns {halfTurns}: Ez/Ex = {expected:E9} over {counted} points, worst error {worst:E3}");
        Assert.True(counted > 10, $"only {counted} points were inside the field region");
        Assert.True(worst < 1e-14, $"worst anisotropy error {worst:E3}");
    }
}
