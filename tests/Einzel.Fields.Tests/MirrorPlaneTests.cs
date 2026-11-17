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
}
