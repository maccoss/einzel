using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// The mirror plane of a reflected solve belongs to exactly one of its halves.
/// </summary>
/// <remarks>
/// A reflected solve is the declared half summed with its image, and a point on the mirror
/// plane is its own image: the declared half's domain includes its edge, so both answered for
/// it and the sum there was twice the potential - a half einzel lens with a 500 V electrode read
/// 999.5 V on the plane. Either side of the plane one of the two answers; on it, the declared
/// half does and the image is nothing.
/// </remarks>
public sealed class MirrorPlaneTests
{
    private const double Plane = 0.04;

    // A field that is 500 V and 2 kV/m along y inside x <= the plane, and nothing outside:
    // a declared half whose domain ends on the mirror, as a solve's does.
    private sealed class Half : IElectrostaticField
    {
        public double PotentialAt(in Vec3 position) => position.X <= Plane ? 500.0 : 0.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => position.X <= Plane ? new Vec3(0.0, 2000.0, 0.0) : default;
    }

    /// <summary>The sum is the half's own value on the plane, as it is either side of it.</summary>
    [Fact]
    public void ThePlaneIsCountedOnce()
    {
        var half = new Half();
        var image = new ReflectedField(half, Plane);
        var whole = new SuperposedField([half, image]);

        foreach (var x in new[] { 0.03, double.BitDecrement(Plane), Plane, double.BitIncrement(Plane), 0.05 })
        {
            var point = new Vec3(x, 0.001, 0.0);

            Assert.Equal(500.0, whole.PotentialAt(in point));
            Assert.Equal(2000.0, whole.ElectricFieldAt(in point).Y);
        }

        // The image alone: nothing on the plane and on the declared side, the half beyond it.
        Assert.Equal(0.0, image.PotentialAt(new Vec3(Plane, 0.0, 0.0)));
        Assert.Equal(default, image.ElectricFieldAt(new Vec3(Plane, 0.0, 0.0)));
        Assert.Equal(0.0, image.PotentialAt(new Vec3(0.03, 0.0, 0.0)));
        Assert.Equal(500.0, image.PotentialAt(new Vec3(0.05, 0.0, 0.0)));
    }

    /// <summary>
    /// A solved half lens: the potential on the mirror plane is the limit from either side, and
    /// is within the range its electrodes hold.
    /// </summary>
    [Fact]
    public void ASolvedHalfIsContinuousAcrossItsMirror()
    {
        var solve = new CompiledSolvedField
        {
            MinX = 0.0, MaxX = Plane, MinY = 0.0, MaxY = 0.012,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            Symmetry = SolveSymmetry.Cylindrical,
            BottomEdge = BoundaryKind.Neumann,
            RightEdge = BoundaryKind.Neumann,
            ReflectAboutX = Plane,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "centre", Shape = ElectrodeShape.Rectangle,
                    MinX = 0.03, MaxX = Plane, MinY = 0.005, MaxY = 0.012, Potential = 500.0,
                },
            ],
        };

        var (field, _) = GeometryBuilder.Build(solve);

        foreach (var radius in new[] { 0.0, 0.002, 0.004 })
        {
            var on = field.PotentialAt(new Vec3(Plane, radius, 0.0));
            var before = field.PotentialAt(new Vec3(Plane - 1e-7, radius, 0.0));
            var after = field.PotentialAt(new Vec3(Plane + 1e-7, radius, 0.0));

            Assert.InRange(on, 100.0, 500.0);
            Assert.Equal(before, on, 2);
            Assert.Equal(after, on, 2);
            Assert.Equal(before, after, 9);
        }
    }

    /// <summary>The same in a volume.</summary>
    [Fact]
    public void ASolvedVolumeIsContinuousAcrossItsMirror()
    {
        var geometry = new Geometry3D(
            -4.0e-3, -4.0e-3, -4.0e-3, 4.0e-3, 4.0e-3, 4.0e-3, 0.5e-3,
            [
                new CompiledElectrode3D
                {
                    Name = "block", Shape = Electrode3DShape.Box,
                    MinX = 2.0e-3, MaxX = 4.0e-3, MinY = -1.0e-3, MaxY = 1.0e-3, MinZ = 1.0e-3, MaxZ = 3.0e-3,
                    Potential = 100.0,
                },
            ],
            1e-9)
        {
            ReflectAboutX = 4.0e-3,
            Faces = Geometry3D.FacesOf(
            [
                BoundaryKind.Dirichlet, BoundaryKind.Neumann, BoundaryKind.Dirichlet,
                BoundaryKind.Dirichlet, BoundaryKind.Dirichlet, BoundaryKind.Dirichlet,
            ]),
        };

        var (field, _) = GeometryBuilder3D.BuildField(geometry);

        var on = field.PotentialAt(new Vec3(4.0e-3, 0.0, -1.0e-3));
        var before = field.PotentialAt(new Vec3(4.0e-3 - 1e-8, 0.0, -1.0e-3));
        var after = field.PotentialAt(new Vec3(4.0e-3 + 1e-8, 0.0, -1.0e-3));

        Assert.InRange(on, 1.0, 100.0);
        Assert.Equal(before, on, 2);
        Assert.Equal(after, on, 2);
    }
    // Planes at three powers of two, where one ulp below the plane and one above differ in size,
    // and one that is not.
    public static TheoryData<double, bool> Planes => new()
    {
        { 0.125, true }, { 0.25, true }, { 0.5, true }, { 0.3, true },
        { 0.125, false }, { 0.25, false }, { 0.5, false }, { 0.3, false },
    };

    // A declared half that is 500 V and 2 kV/m along y on its own side of the plane and on the
    // plane, and nothing beyond: below the plane where `below`, above it otherwise.
    private sealed class SidedHalf(double plane, bool below) : IElectrostaticField
    {
        private bool Inside(in Vec3 p) => below ? p.X <= plane : p.X >= plane;

        public double PotentialAt(in Vec3 position) => Inside(in position) ? 500.0 : 0.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => Inside(in position) ? new Vec3(0.0, 2000.0, 0.0) : default;
    }

    /// <summary>
    /// One ulp either side of the plane, the sum is the half's own value: a reflection that rounds
    /// back onto the plane does not count it twice.
    /// </summary>
    /// <remarks>
    /// One ulp below a power-of-two plane, <c>2p - x</c> is half an ulp above it and rounds onto
    /// it, so a rule that gave the image nothing only where <c>x == p</c> let both halves answer
    /// there and read 1000 V.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Planes))]
    public void NoRoundingOfTheReflectionCountsThePlaneTwice(double plane, bool declaredBelow)
    {
        var half = new SidedHalf(plane, declaredBelow);
        var whole = new SuperposedField([half, new ReflectedField(half, plane, imageAbove: declaredBelow)]);

        foreach (var x in new[] { double.BitDecrement(plane), plane, double.BitIncrement(plane) })
        {
            var point = new Vec3(x, 0.001, 0.0);

            Assert.Equal(500.0, whole.PotentialAt(in point));
            Assert.Equal(2000.0, whole.ElectricFieldAt(in point).Y);
        }
    }

    /// <summary>The same through a solved cross-section, the half on either side of its mirror.</summary>
    [Theory]
    [MemberData(nameof(Planes))]
    public void ASolvedHalfCountsItsPlaneOnceToTheUlp(double plane, bool declaredBelow)
    {
        var width = 0.008;
        var (minX, maxX) = declaredBelow ? (plane - width, plane) : (plane, plane + width);

        var solve = new CompiledSolvedField
        {
            MinX = minX, MaxX = maxX, MinY = 0.0, MaxY = 0.008,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            LeftEdge = declaredBelow ? BoundaryKind.Dirichlet : BoundaryKind.Neumann,
            RightEdge = declaredBelow ? BoundaryKind.Neumann : BoundaryKind.Dirichlet,
            ReflectAboutX = plane,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "plate", Shape = ElectrodeShape.Rectangle,
                    MinX = minX + 0.002, MaxX = maxX - 0.002, MinY = 0.003, MaxY = 0.005, Potential = 100.0,
                },
            ],
        };

        var (field, _) = GeometryBuilder.Build(solve);

        AssertOnce(field, plane, new Vec3(0.0, 0.006, 0.0));
    }

    /// <summary>The same through a solved volume.</summary>
    [Theory]
    [MemberData(nameof(Planes))]
    public void ASolvedVolumeCountsItsPlaneOnceToTheUlp(double plane, bool declaredBelow)
    {
        var width = 0.006;
        var (minX, maxX) = declaredBelow ? (plane - width, plane) : (plane, plane + width);

        var geometry = new Geometry3D(
            minX, -3.0e-3, -3.0e-3, maxX, 3.0e-3, 3.0e-3, 0.5e-3,
            [
                new CompiledElectrode3D
                {
                    Name = "block", Shape = Electrode3DShape.Box,
                    MinX = minX + 1.5e-3, MaxX = maxX - 1.5e-3, MinY = -1.0e-3, MaxY = 1.0e-3, MinZ = -1.0e-3, MaxZ = 1.0e-3,
                    Potential = 100.0,
                },
            ],
            1e-9)
        {
            ReflectAboutX = plane,
            Faces = Geometry3D.FacesOf(
            [
                declaredBelow ? BoundaryKind.Dirichlet : BoundaryKind.Neumann,
                declaredBelow ? BoundaryKind.Neumann : BoundaryKind.Dirichlet,
                BoundaryKind.Dirichlet, BoundaryKind.Dirichlet, BoundaryKind.Dirichlet, BoundaryKind.Dirichlet,
            ]),
        };

        var (field, _) = GeometryBuilder3D.BuildField(geometry);

        AssertOnce(field, plane, new Vec3(0.0, 1.5e-3, 0.5e-3));
    }

    // On the plane and one ulp either side the field reads the same to far better than a doubling,
    // and lies within what the electrodes hold.
    private static void AssertOnce(IElectrostaticField field, double plane, Vec3 offset)
    {
        var on = field.PotentialAt(new Vec3(plane, offset.Y, offset.Z));
        var below = field.PotentialAt(new Vec3(double.BitDecrement(plane), offset.Y, offset.Z));
        var above = field.PotentialAt(new Vec3(double.BitIncrement(plane), offset.Y, offset.Z));

        Assert.InRange(on, 1.0, 100.0);
        Assert.Equal(on, below, 1e-6 * Math.Abs(on));
        Assert.Equal(on, above, 1e-6 * Math.Abs(on));
    }
}
