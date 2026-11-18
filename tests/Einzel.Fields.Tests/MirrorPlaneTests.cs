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

        // The inner field's face is the plane: the image does not change sign there - the declared
        // half owns that surface - so the sum has exactly one surface at the plane, not two that
        // cancel.
        var before = image.SignedDistanceToDiscontinuity(new Vec3(Plane - 0.001, 0.0, 0.0));
        var after = image.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0.0, 0.0));
        Assert.Equal(Math.Sign(before), Math.Sign(after));
        Assert.Equal(0.001, Math.Abs(before), 12);

        var sum = new Reflection(Plane, true).Sum(new Edged(Plane));
        Assert.NotEqual(
            Math.Sign(sum.SignedDistanceToDiscontinuity(new Vec3(Plane - 0.001, 0.0, 0.0))),
            Math.Sign(sum.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0.0, 0.0))));

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

        // A field through the plane, so the face there is a jump and declared.
        public Vec3 ElectricFieldAt(in Vec3 position) => new(1.0, 0.0, 0.0);

        public double SignedDistanceToDiscontinuity(in Vec3 position) => (Mirror + 1e-10) - position.X;
    }

    /// <summary>
    /// The plane's face test and the point past it where the sign is read scale with the geometry:
    /// a face on the plane to a rounding at kilometre scale is a face, so the declared half - which
    /// owns the plane - declares it as the surface it is.
    /// </summary>
    [Fact]
    public void TheFaceTestScalesWithTheGeometry()
    {
        var owner = new Reflection(FarEdge.Mirror, true).Declared(new FarEdge());

        var below = owner.SignedDistanceToDiscontinuity(new Vec3(FarEdge.Mirror - 1.0, 0.0, 0.0));
        var above = owner.SignedDistanceToDiscontinuity(new Vec3(FarEdge.Mirror + 1.0, 0.0, 0.0));

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

    /// <summary>
    /// A type that is both a field and bounded by conductors - every solved field is - can be
    /// handed to either gate without a cast or an ambiguous call, and both gate its metal alike.
    /// </summary>
    /// <remarks>
    /// The conductor gate was a second overload of <c>Declared</c>, so this call did not compile
    /// (CS0121); it is <c>DeclaredMetal</c> now.
    /// </remarks>
    [Fact]
    public void BothGatesTakeASolvedLikeTypeAndAgree()
    {
        var reflection = new Reflection(Plane, ImageAbove: true);
        var half = new MetalPastThePlane();

        var field = reflection.Declared(half);
        var metal = reflection.DeclaredMetal(half);
        var inside = new Vec3(Plane + 0.002, 0.0, 0.0);

        // Past the plane, on the image's side: neither answers for the declared half's metal.
        Assert.Equal(double.PositiveInfinity, ((IConductorBounded)field).SignedDistanceToConductor(in inside));
        Assert.Equal(double.PositiveInfinity, metal.SignedDistanceToConductor(in inside));
        Assert.Null(metal.ConductorAt(in inside));
    }

    /// <summary>
    /// The declared half declares no surface where it contributes nothing - on the image's side -
    /// but the plane itself where it has a face there, as the image does on the declared side.
    /// </summary>
    /// <remarks>
    /// Its field and potential were gated on the image's side and its discontinuities were not, so
    /// a surface the inner field declares beyond its edge stood there as a phantom.
    /// </remarks>
    [Fact]
    public void WhereTheDeclaredHalfIsAbsentItAddsNoSurface()
    {
        double Measure(IElectrostaticField field, double x) => field.SignedDistanceToDiscontinuity(new Vec3(x, 0.0, 0.0));

        // A surface 2 mm past the plane, on the image's side: no change of sign across it, behind a
        // mirror edge (no plane surface either) ...
        var phantom = new Reflection(Plane, ImageAbove: true, EdgeIsContinuous: true).Declared(new Edged(Plane + 0.002));
        Assert.Equal(Math.Sign(Measure(phantom, Plane + 0.001)), Math.Sign(Measure(phantom, Plane + 0.003)));
        Assert.Equal(Measure(phantom, Plane + 0.001), Measure(phantom, Plane + 0.003));

        // ... and behind any other edge, where the plane is the one surface: a change at the plane,
        // none across the inner field's surface beyond it, the measure the plane's distance.
        var edged = new Reflection(Plane, ImageAbove: true).Declared(new Edged(Plane + 0.002));
        Assert.NotEqual(Math.Sign(Measure(edged, Plane - 0.0005)), Math.Sign(Measure(edged, Plane + 0.001)));
        Assert.Equal(Math.Sign(Measure(edged, Plane + 0.001)), Math.Sign(Measure(edged, Plane + 0.003)));
        Assert.Equal(0.003, Math.Abs(Measure(edged, Plane + 0.003)), 12);

        // The control: the inner field alone does change sign there.
        Assert.NotEqual(Math.Sign(new Edged(Plane + 0.002).SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0, 0))),
            Math.Sign(new Edged(Plane + 0.002).SignedDistanceToDiscontinuity(new Vec3(Plane + 0.003, 0, 0))));

        // A face on the plane is kept: one surface there, its sign changing across it.
        var faced = new Reflection(Plane, ImageAbove: true).Declared(new Edged(Plane));
        Assert.NotEqual(Math.Sign(Measure(faced, Plane - 0.001)), Math.Sign(Measure(faced, Plane + 0.001)));
        Assert.Equal(0.001, Math.Abs(Measure(faced, Plane + 0.001)), 12);
    }

    // A half with a face on the plane and a field through it or along it: its box is 0 to the
    // plane, its field the given vector inside, and - for the run test - free outside in every
    // direction, as a careless analytic field might claim. Its measure beyond the face may be
    // signed by y, so a sign read once and kept is caught.
    private sealed class Through(Vec3 inside, bool signedByY = false) : IElectrostaticField
    {
        private static bool In(in Vec3 p) => p.X >= 0.0 && p.X <= Plane;

        public double PotentialAt(in Vec3 position) => 0.0;

        public Vec3 ElectricFieldAt(in Vec3 position) => In(in position) ? inside : default;

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            position.X > Plane ? double.PositiveInfinity : 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            (signedByY && position.Y < 0.0 ? -1.0 : 1.0) * (Plane - position.X);
    }

    /// <summary>
    /// Whether the plane is a surface is the edge's: a continuous edge - a mirror - gets none
    /// whatever the field there; any other edge with a face on the plane gets exactly one.
    /// </summary>
    /// <remarks>
    /// Decided first from the field's normal component at each point, which read a driven solve's
    /// first channel rather than its sum, flipped where the normal field crossed zero along the
    /// plane, and was left to round-off. The edge condition is known when the solve is built.
    /// </remarks>
    [Fact]
    public void APlaneIsASurfaceUnlessItsEdgeIsContinuous()
    {
        double Sign(IElectrostaticField field, double x, double y = 0.001) => Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(x, y, 0.0)));

        // A mirror edge: no surface, even with a field through it as its declared value says.
        foreach (var inside in new[] { new Vec3(0.0, 1000.0, 0.0), new Vec3(1000.0, 0.0, 0.0) })
        {
            var mirror = new Reflection(Plane, ImageAbove: true, EdgeIsContinuous: true).Sum(new Through(inside));
            Assert.Equal(Sign(mirror, Plane - 0.0005), Sign(mirror, Plane + 0.0005));
        }

        // Any other edge: one surface, whatever the field there - an extra landing where it
        // happens to be continuous, never a missed jump - and the same on either side of y = 0.
        foreach (var inside in new[] { new Vec3(0.0, 1000.0, 0.0), new Vec3(1000.0, 0.0, 0.0) })
        {
            var edged = new Reflection(Plane, ImageAbove: true).Sum(new Through(inside));
            Assert.NotEqual(Sign(edged, Plane - 0.0005), Sign(edged, Plane + 0.0005));
            Assert.NotEqual(Sign(edged, Plane - 0.0005, -0.001), Sign(edged, Plane + 0.0005, -0.001));
        }
    }

    /// <summary>
    /// A solved, reflected cross-section takes the plane's continuity from its edge: a Neumann edge
    /// is no surface, a Dirichlet one is - and a driven solve answers the same for every channel,
    /// since the answer is the geometry's.
    /// </summary>
    /// <remarks>
    /// Decided by the plane edge's own condition: <c>boundaryIsDiscontinuous</c> speaks for the
    /// box's far edges and cannot vouch for the edge on the mirror, so a grounded edge there is a
    /// surface with the flag false too - which is also what a compiled solve built without the flag
    /// has. An edge an ulp short of the plane - meant to lie on it - is on it: a stop-short test
    /// comparing the two exactly took it for a gap and declared nothing.
    /// </remarks>
    [Theory]
    [InlineData(BoundaryKind.Neumann, true, 0.0, false)]
    [InlineData(BoundaryKind.Dirichlet, true, 0.0, true)]
    [InlineData(BoundaryKind.Neumann, false, 0.0, false)]
    [InlineData(BoundaryKind.Dirichlet, false, 0.0, true)]
    [InlineData(BoundaryKind.Dirichlet, true, 1e-14, true)]
    public void ASolvedEdgeDecidesThePlane(BoundaryKind edge, bool discontinuous, double shortBy, bool surface)
    {
        var solve = new CompiledSolvedField
        {
            MinX = 0.0, MaxX = Plane - shortBy, MinY = 0.0, MaxY = 0.012,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            RightEdge = edge,
            BoundaryIsDiscontinuous = discontinuous,
            ReflectAboutX = Plane,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "plate", Shape = ElectrodeShape.Rectangle,
                    MinX = 0.01, MaxX = 0.015, MinY = 0.0, MaxY = 0.004, Potential = 500.0,
                },
            ],
        };

        var (field, _) = GeometryBuilder.Build(solve);
        var before = Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane - 2e-4, 0.006, 0.0)));
        var after = Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane + 2e-4, 0.006, 0.0)));

        Assert.Equal(surface, before != after);
        Assert.Equal(!surface, GeometryBuilder.ReflectionOf(solve)!.EdgeIsContinuous);

        // Above the box, where neither half has field, the plane is no surface - except for the one
        // change of sign a cross-section declaring its own edges cannot avoid, with a grounded edge
        // on the plane (see AReflectedCrossSectionsRimIsOneSurfaceEach).
        var phantom = surface && discontinuous;
        Assert.Equal(
            phantom,
            Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane - 2e-4, 0.02, 0.0)))
                != Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane + 2e-4, 0.02, 0.0))));
    }

    // How many times a field's discontinuity measure changes sign along a segment, sampled finely.
    private static int SignChanges(IElectrostaticField field, Vec3 from, Vec3 to, int samples = 4000)
    {
        var changes = 0;
        var last = 0;

        for (var i = 0; i <= samples; i++)
        {
            var t = (double)i / samples;
            var p = new Vec3(from.X + (t * (to.X - from.X)), from.Y + (t * (to.Y - from.Y)), from.Z + (t * (to.Z - from.Z)));
            var sign = Math.Sign(field.SignedDistanceToDiscontinuity(in p));

            if (sign != 0 && last != 0 && sign != last)
            {
                changes++;
            }

            if (sign != 0)
            {
                last = sign;
            }
        }

        return changes;
    }

    /// <summary>
    /// A reflected cross-section declaring its own edges keeps each box edge as one surface across
    /// the rim where the plane's part ends, on both sides of the plane and whatever the plane edge
    /// is; with a grounded plane edge the one change of sign no surface stands behind is the plane
    /// beyond the box, and a drift is cut there.
    /// </summary>
    /// <remarks>
    /// The member answering nothing on a side held its own measure at the plane, which changes sign
    /// along the rim exactly where the answering half's box edge does; the sum multiplies its
    /// members' signs, so the two cancelled and both top edges were lost - a box edge an ion flew
    /// through without landing, on a mirror edge as on a grounded one.
    /// </remarks>
    [Theory]
    [InlineData(BoundaryKind.Neumann)]
    [InlineData(BoundaryKind.Dirichlet)]
    public void AReflectedCrossSectionsRimIsOneSurfaceEach(BoundaryKind edge)
    {
        var solve = new CompiledSolvedField
        {
            MinX = 0.0, MaxX = Plane, MinY = 0.0, MaxY = 0.012,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            RightEdge = edge,
            BoundaryIsDiscontinuous = true,
            ReflectAboutX = Plane,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "plate", Shape = ElectrodeShape.Rectangle,
                    MinX = 0.01, MaxX = 0.015, MinY = 0.0, MaxY = 0.004, Potential = 500.0,
                },
            ],
        };

        var (field, _) = GeometryBuilder.Build(solve);

        // Up through the top edge, on the declared side and on the image's: one surface each.
        foreach (var x in new[] { Plane - 0.002, Plane + 0.002 })
        {
            Assert.Equal(1, SignChanges(field, new Vec3(x, 0.006, 0.0), new Vec3(x, 0.03, 0.0)));
        }

        // Along x inside the box: the far edges, and the plane where it is grounded.
        Assert.Equal(edge == BoundaryKind.Dirichlet ? 1 : 0, SignChanges(field, new Vec3(Plane - 0.002, 0.006, 0.0), new Vec3(Plane + 0.002, 0.006, 0.0)));

        // Along x above the box: nothing on a mirror; the unavoidable one at the plane on a grounded
        // edge, and the drift cut there rather than jumped.
        var above = SignChanges(field, new Vec3(Plane - 0.002, 0.02, 0.0), new Vec3(Plane + 0.002, 0.02, 0.0));
        var run = field.FieldFreeRunLength(new Vec3(Plane - 0.002, 0.02, 0.0), new Vec3(1.0, 0.0, 0.0));

        Assert.Equal(edge == BoundaryKind.Dirichlet ? 1 : 0, above);
        Assert.Equal(edge == BoundaryKind.Dirichlet, run <= 0.002 + 1e-12);
    }

    /// <summary>
    /// Where the halves overlap there is no plane face: asking for one is refused, and the image
    /// is built without one and still answers everywhere.
    /// </summary>
    /// <remarks>
    /// The image built a face for an overlapping reflection and never used it; read, it would have
    /// taken the declared side's sign from the wrong side of the plane.
    /// </remarks>
    [Fact]
    public void AnOverlappingReflectionHasNoPlaneFace()
    {
        var overlapping = new Reflection(Plane, ImageAbove: null);
        var inner = new Through(new Vec3(1000.0, 0.0, 0.0));

        Assert.Throws<ArgumentException>(() => overlapping.FaceOf(inner, ownsPlane: true));
        Assert.Throws<ArgumentException>(() => overlapping.FaceOf(inner, ownsPlane: false));

        var image = new ReflectedField(inner, overlapping);
        var mirrored = new Vec3((2.0 * Plane) - 0.01, 0.0, 0.0);
        Assert.Equal(inner.SignedDistanceToDiscontinuity(new Vec3(0.01, 0.0, 0.0)), image.SignedDistanceToDiscontinuity(in mirrored), 12);
    }

    /// <summary>
    /// A solved cross-section whose box stops short of the plane leaves a gap with no field on
    /// either side, so the plane is no surface whatever its edge: its edge is its own surface, a
    /// gap's width from the plane.
    /// </summary>
    [Fact]
    public void ASolveStoppingShortOfThePlaneIsNoSurfaceThere()
    {
        var solve = new CompiledSolvedField
        {
            MinX = 0.0, MaxX = Plane - 0.002, MinY = 0.0, MaxY = 0.012,
            CellSize = 0.5e-3, Tolerance = 1e-10,
            RightEdge = BoundaryKind.Dirichlet,
            BoundaryIsDiscontinuous = true,
            ReflectAboutX = Plane,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "plate", Shape = ElectrodeShape.Rectangle,
                    MinX = 0.008, MaxX = 0.012, MinY = 0.0, MaxY = 0.004, Potential = 500.0,
                },
            ],
        };

        var (field, _) = GeometryBuilder.Build(solve);
        double Sign(double x) => Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(x, 0.006, 0.0)));

        Assert.True(GeometryBuilder.ReflectionOf(solve)!.EdgeIsContinuous);
        Assert.Equal(Sign(Plane - 2e-4), Sign(Plane + 2e-4));

        // Its own edge, a gap's width short of the plane, and that edge's image, are surfaces.
        Assert.NotEqual(Sign(Plane - 0.002 - 2e-4), Sign(Plane - 0.002 + 2e-4));
        Assert.NotEqual(Sign(Plane + 0.002 - 2e-4), Sign(Plane + 0.002 + 2e-4));
    }

    /// <summary>
    /// A reflected volume solve has the plane as a surface where its face there is not a mirror,
    /// though a volume solve declares no surfaces of its own; on a Neumann face, none.
    /// </summary>
    /// <remarks>
    /// The plane was declared only where the half's own measure had a face there, and a volume
    /// solve has none anywhere: a grounded face on the plane, a plate driving a normal field
    /// through it, and the union reporting no surface.
    /// </remarks>
    [Theory]
    [InlineData(BoundaryKind.Neumann, 0.0, false)]
    [InlineData(BoundaryKind.Dirichlet, 0.0, true)]
    [InlineData(BoundaryKind.Dirichlet, 1e-14, true)]
    public void AVolumeSolveTakesThePlaneFromItsFace(BoundaryKind face, double shortBy, bool surface)
    {
        var faces = new List<BoundaryKind> { BoundaryKind.Dirichlet, face, BoundaryKind.Dirichlet, BoundaryKind.Dirichlet, BoundaryKind.Dirichlet, BoundaryKind.Dirichlet };
        var geometry = new Einzel.Fields.Solved.Geometry3D(
            0.0, -0.006, -0.006, Plane - shortBy, 0.006, 0.006, 1e-3,
            [
                new CompiledElectrode3D
                {
                    Name = "plate", Shape = Electrode3DShape.Box,
                    MinX = 0.004, MaxX = 0.006, MinY = -0.003, MaxY = 0.003, MinZ = -0.003, MaxZ = 0.003, Potential = 500.0,
                },
            ],
            Tolerance: 1e-8)
        {
            Faces = Einzel.Fields.Solved.Geometry3D.FacesOf(faces),
            ReflectAboutX = Plane,
        };

        var (field, _) = Einzel.Fields.Solved.GeometryBuilder3D.BuildField(geometry);

        // Between the plate and the plane, where the normal field is not zero on a grounded face.
        Assert.True(Math.Abs(field.ElectricFieldAt(new Vec3(Plane - 2e-3, 0.0, 0.0)).X) > 1.0);

        var before = Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane - 2e-4, 0.0, 0.0)));
        var after = Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane + 2e-4, 0.0, 0.0)));

        Assert.Equal(surface, before != after);

        // Beyond the box in y or z, where neither half has field: no surface.
        foreach (var (y, z) in new[] { (0.01, 0.0), (0.0, 0.01) })
        {
            Assert.Equal(
                Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane - 2e-4, y, z))),
                Math.Sign(field.SignedDistanceToDiscontinuity(new Vec3(Plane + 2e-4, y, z))));
        }

        // Across the rim. A volume declares no surfaces of its own, so on the declared side crossing
        // the box's edge changes nothing; on the image's side the plane's part is closed by the
        // image box's faces - where the field does drop to nothing - so crossing the edge there is
        // one surface, and beyond the image box's far face it is none.
        var declared = SignChanges(field, new Vec3(Plane - 0.002, 0.0, 0.0), new Vec3(Plane - 0.002, 0.012, 0.0));
        var image = SignChanges(field, new Vec3(Plane + 0.002, 0.0, 0.0), new Vec3(Plane + 0.002, 0.012, 0.0));
        var beyond = SignChanges(field, new Vec3((2.0 * Plane) + 0.002, 0.0, 0.0), new Vec3((2.0 * Plane) + 0.002, 0.012, 0.0));

        Assert.Equal(0, declared);
        Assert.Equal(surface ? 1 : 0, image);
        Assert.Equal(0, beyond);

        // And along x through the image box: the plane, then its far face.
        Assert.Equal(surface ? 2 : 0, SignChanges(field, new Vec3(Plane - 0.002, 0.0, 0.0), new Vec3((2.0 * Plane) + 0.002, 0.0, 0.0)));
    }

    /// <summary>
    /// The sign beyond the face is read at each point's own place, and the face holds no state a
    /// query writes - so a packet's members sharing the field cannot see one another's answers.
    /// </summary>
    [Fact]
    public void ThePlanesSignIsReadWhereThePointIsAndNothingIsHeld()
    {
        var owner = new Reflection(Plane, ImageAbove: true).Declared(new Through(new Vec3(1000.0, 0.0, 0.0), signedByY: true));

        var above = owner.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0.001, 0.0));
        var below = owner.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, -0.001, 0.0));

        Assert.NotEqual(Math.Sign(above), Math.Sign(below));

        // Asked again in the other order, the same answers.
        Assert.Equal(below, owner.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, -0.001, 0.0)));
        Assert.Equal(above, owner.SignedDistanceToDiscontinuity(new Vec3(Plane + 0.001, 0.001, 0.0)));

        Assert.All(
            typeof(PlaneFace).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public),
            f => Assert.True(f.IsInitOnly, $"PlaneFace.{f.Name} can be written after construction"));
    }

    /// <summary>
    /// A field-free run toward a plane the declared half declares as a surface ends at the plane,
    /// however far the inner field claims to be free.
    /// </summary>
    [Fact]
    public void AFreeRunEndsAtAPlaneThatIsASurface()
    {
        var reflection = new Reflection(Plane, ImageAbove: true);
        var minusX = new Vec3(-1.0, 0.0, 0.0);
        var from = new Vec3(Plane + 0.002, 0.001, 0.0);

        var jumping = reflection.Declared(new Through(new Vec3(1000.0, 0.0, 0.0)));
        Assert.Equal(0.002, jumping.FieldFreeRunLength(in from, in minusX), 12);

        // Where the plane is no surface, the run is the inner field's.
        var mirror = (reflection with { EdgeIsContinuous = true }).Declared(new Through(new Vec3(1000.0, 0.0, 0.0)));
        Assert.Equal(double.PositiveInfinity, mirror.FieldFreeRunLength(in from, in minusX));
    }
}
