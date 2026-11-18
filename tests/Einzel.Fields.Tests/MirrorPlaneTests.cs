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
        var image = new Reflection(Plane, true).Image(half);
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
        var whole = new SuperposedField([half, new Reflection(plane, declaredBelow).Image(half)]);

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
    /// <summary>
    /// A plane strictly inside its own solve's box: the half runs past the plane and overlaps its
    /// image, so the two are summed everywhere and the field is symmetric about the plane - in the
    /// plane and in a volume.
    /// </summary>
    /// <remarks>
    /// Every reflected solve used to be given a side, so here the declared half alone answered
    /// below the plane and both above it: asymmetric about its own mirror.
    /// </remarks>
    [Fact]
    public void APlaneInsideItsBoxIsSymmetricAboutIt()
    {
        const double plane = 0.010;

        var solve = new CompiledSolvedField
        {
            MinX = 0.0, MaxX = 0.013, MinY = 0.0, MaxY = 0.008,
            CellSize = 0.25e-3, Tolerance = 1e-10,
            ReflectAboutX = plane,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "plate", Shape = ElectrodeShape.Rectangle,
                    MinX = 0.004, MaxX = 0.006, MinY = 0.003, MaxY = 0.005, Potential = 100.0,
                },
            ],
        };

        var (plain, _) = GeometryBuilder.Build(solve);
        AssertSymmetric(plain, plane, 0.004, 0.0);

        var geometry = new Geometry3D(
            0.0, -3.0e-3, -3.0e-3, 0.013, 3.0e-3, 3.0e-3, 0.5e-3,
            [
                new CompiledElectrode3D
                {
                    Name = "block", Shape = Electrode3DShape.Box,
                    MinX = 0.004, MaxX = 0.006, MinY = -1.0e-3, MaxY = 1.0e-3, MinZ = -1.0e-3, MaxZ = 1.0e-3,
                    Potential = 100.0,
                },
            ],
            1e-9)
        {
            ReflectAboutX = plane,
        };

        var (volume, _) = GeometryBuilder3D.BuildField(geometry);
        AssertSymmetric(volume, plane, 1.5e-3, 0.5e-3);
    }

    private static void AssertSymmetric(IElectrostaticField field, double plane, double y, double z)
    {
        foreach (var a in new[] { 0.5e-3, 1.0e-3, 2.0e-3, 2.9e-3 })
        {
            var below = field.PotentialAt(new Vec3(plane - a, y, z));
            var above = field.PotentialAt(new Vec3(plane + a, y, z));

            Assert.True(Math.Abs(below) > 1e-6, $"a potential to compare at {a}");
            Assert.Equal(below, above, 1e-9 * Math.Abs(below));
        }
    }

    // Metal from 1 to 3 mm past the plane: a shape the declared half carries over its mirror.
    private sealed class MetalPastThePlane : IElectrostaticField, IConductorBounded
    {
        public double PotentialAt(in Vec3 position) => 0.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => default;

        public double SignedDistanceToConductor(in Vec3 position) =>
            Math.Max((Plane + 0.001) - position.X, position.X - (Plane + 0.003));

        public string? ConductorAt(in Vec3 position) => "past";

        public double NearestConductor(in Vec3 position, out string? name)
        {
            name = "past";
            return SignedDistanceToConductor(in position);
        }
    }

    /// <summary>
    /// The image's conductors are where its field is: none on the declared side, so an image of
    /// metal carried past the plane cannot stop an ion where the image answers nothing.
    /// </summary>
    [Fact]
    public void TheImagesMetalIsOnlyWhereItsFieldIs()
    {
        var image = new Reflection(Plane, true).Image(new MetalPastThePlane());
        var declaredSide = new Vec3(Plane - 0.002, 0.0, 0.0);

        Assert.Equal(double.PositiveInfinity, image.SignedDistanceToConductor(in declaredSide));
        Assert.Null(image.ConductorAt(in declaredSide));
        Assert.Equal(double.PositiveInfinity, image.NearestConductor(in declaredSide, out var name));
        Assert.Null(name);

        // On its own side it is the reflection of the inner shape, as before.
        var imageSide = new Vec3(Plane + 0.002, 0.0, 0.0);
        Assert.Equal(new MetalPastThePlane().SignedDistanceToConductor(new Vec3(Plane - 0.002, 0.0, 0.0)), image.SignedDistanceToConductor(in imageSide));

        // With no side the two overlap, and the image's metal stands wherever its field does.
        var overlap = new Reflection(Plane, null).Image(new MetalPastThePlane());
        Assert.True(overlap.SignedDistanceToConductor(in declaredSide) < 0.0);
    }
    // A declared half below the plane that is field-free beyond its own edge, with a
    // discontinuity at a chosen x.
    private sealed class Edged(double surfaceX) : IElectrostaticField
    {
        public double PotentialAt(in Vec3 position) => position.X <= Plane ? 100.0 : 0.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => position.X <= Plane ? new Vec3(1000.0, 0.0, 0.0) : default;

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            position.X > Plane ? double.PositiveInfinity : 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) => position.X - surfaceX;
    }

    /// <summary>
    /// Where the image answers nothing it is field-free only as far as the plane, and declares no
    /// surface but the plane itself where the inner field has one there.
    /// </summary>
    /// <remarks>
    /// Both were asked of the inner field at the reflected point regardless. A half free beyond its
    /// own edge made the image claim a free run straight through the plane into its field, so the
    /// integrator would have skipped it; and a surface the half declares beyond its edge landed on
    /// the declared side as a discontinuity the image does not have.
    /// </remarks>
    [Fact]
    public void WhereTheImageIsAbsentItShortensNoRunAndAddsNoSurface()
    {
        var plusX = new Vec3(1.0, 0.0, 0.0);
        var image = new Reflection(Plane, true).Image(new Edged(Plane));
        var declaredSide = new Vec3(Plane - 0.003, 0.0, 0.0);

        // Free as far as the plane along a ray heading for it, and without limit along one leaving.
        Assert.Equal(0.003, image.FieldFreeRunLength(in declaredSide, in plusX), 12);
        Assert.Equal(double.PositiveInfinity, image.FieldFreeRunLength(in declaredSide, new Vec3(-1.0, 0.0, 0.0)));

        // The inner field's face is the plane: one surface there, its sign changing across it.
        var before = image.SignedDistanceToDiscontinuity(new Vec3(Plane - 0.001, 0.0, 0.0));
        var after = image.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0.0, 0.0));
        Assert.True(Math.Sign(before) != Math.Sign(after), $"{before} then {after}");
        Assert.Equal(0.001, Math.Abs(before), 12);

        // A surface the inner field declares 2 mm past its edge is no surface on the declared side:
        // the measure there is the image's own at the plane, constant, so it changes sign neither
        // on this side nor at the plane - where nothing jumps.
        var phantom = new Reflection(Plane, true).Image(new Edged(Plane + 0.002));
        var near = phantom.SignedDistanceToDiscontinuity(new Vec3(Plane - 0.001, 0.0, 0.0));
        var far = phantom.SignedDistanceToDiscontinuity(new Vec3(Plane - 0.003, 0.0, 0.0));
        var beyond = phantom.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.0005, 0.0, 0.0));
        Assert.Equal(near, far);
        Assert.Equal(Math.Sign(near), Math.Sign(beyond));

        // On the image's own side it is the reflection, as before.
        Assert.Equal(
            new Edged(Plane + 0.002).SignedDistanceToDiscontinuity(new Vec3(Plane - 0.001, 0.0, 0.0)),
            phantom.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0.0, 0.0)));
    }
    // A declared half at kilometre scale whose box edge is the plane to a rounding: its measure on
    // the plane is 1e-10 m, which is a face at that scale and was not one by a fixed 1e-12 m.
    private sealed class FarEdge : IElectrostaticField
    {
        public const double Mirror = 1000.0;

        public double PotentialAt(in Vec3 position) => 0.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => default;

        public double SignedDistanceToDiscontinuity(in Vec3 position) => (Mirror + 1e-10) - position.X;
    }

    /// <summary>
    /// The plane's face test and the point past it where the sign is read scale with the geometry:
    /// a face on the plane to a rounding at kilometre scale is a face, so the image declares the
    /// plane as the surface it is.
    /// </summary>
    [Fact]
    public void TheFaceTestScalesWithTheGeometry()
    {
        var image = new Reflection(FarEdge.Mirror, true).Image(new FarEdge());

        var below = image.SignedDistanceToDiscontinuity(new Vec3(FarEdge.Mirror - 1.0, 0.0, 0.0));
        var above = image.SignedDistanceToDiscontinuity(new Vec3(FarEdge.Mirror + 1.0, 0.0, 0.0));

        Assert.Equal(1.0, Math.Abs(below), 6);
        Assert.NotEqual(Math.Sign(below), Math.Sign(above));
    }

    // A half that is not nothing outside its box, as a solve with a non-zero outside potential is:
    // a ramp inside 0 <= x <= the plane, and a constant potential and a field of its own outside.
    private sealed class OutsideValue : IElectrostaticField
    {
        private static bool Inside(in Vec3 p) => p.X >= 0.0 && p.X <= Plane;

        public double PotentialAt(in Vec3 position) => Inside(in position) ? -1000.0 * position.X : 37.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => Inside(in position) ? new Vec3(1000.0, 5.0, 0.0) : new Vec3(3.0, 11.0, 0.0);
    }

    /// <summary>
    /// A declared half that is not nothing outside its box sums symmetrically about its mirror:
    /// its potential and field are gated on the image's side as the image's are on its own.
    /// </summary>
    /// <remarks>
    /// Only the image was gated, so on the image's side the declared half's outside value stood
    /// as well: 37 V more there than at the mirror point, and a field that did not reflect.
    /// </remarks>
    [Theory]
    [InlineData(0.001)]
    [InlineData(0.02)]
    [InlineData(0.039)]
    [InlineData(0.05)]
    [InlineData(0.2)]
    public void ADeclaredHalfWithAnOutsideValueSumsSymmetrically(double distance)
    {
        var sum = new Reflection(Plane, ImageAbove: true).Sum(new OutsideValue());

        var below = new Vec3(Plane - distance, 0.002, 0.0);
        var above = new Vec3(Plane + distance, 0.002, 0.0);

        Assert.Equal(sum.PotentialAt(in below), sum.PotentialAt(in above), 9);

        var eBelow = sum.ElectricFieldAt(in below);
        var eAbove = sum.ElectricFieldAt(in above);

        Assert.Equal(eBelow.X, -eAbove.X, 9);
        Assert.Equal(eBelow.Y, eAbove.Y, 9);
    }

    /// <summary>
    /// No public constructor takes a side: an image's side is the reflection's to decide, so a
    /// caller cannot give one the rule would not.
    /// </summary>
    [Fact]
    public void AnImagesSideIsGivenOnlyThroughAReflection()
    {
        var sides = typeof(ReflectedField).GetConstructors()
            .Where(c => c.GetParameters().Any(p => p.ParameterType == typeof(bool?) || p.ParameterType == typeof(bool)))
            .ToList();

        Assert.Empty(sides);
        Assert.Contains(typeof(ReflectedField).GetConstructors(), c => c.GetParameters().Any(p => p.ParameterType == typeof(Reflection)));
    }
}
