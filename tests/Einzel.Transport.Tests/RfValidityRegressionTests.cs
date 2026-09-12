using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Transport.Diffusion;

namespace Einzel.Transport.Tests;

/// <summary>Adversarial controls for RF averaging and well reuse (REG-2).</summary>
public sealed class RfValidityRegressionTests
{
    private static readonly IonSpecies Ion = IonSpecies.FromMassToCharge(500, 1);
    private static IdealQuadrupoleRf Rf(double amplitude, double frequency = 1e6) =>
        IdealQuadrupoleRf.Create(Quantity.From(0, "V"), Quantity.From(amplitude, "V"),
            Quantity.From(frequency, "Hz"), Quantity.From(4, "mm"));
    private static PonderomotiveField Well(ITimeVaryingField field) => new(field, Ion.ChargeSi, Ion.MassSi, 0);

    /// <summary>An inert clock must not change either the well or the cycle mean.</summary>
    [Fact]
    public void ZeroAmplitudeGeneratorCannotChangeTheAnswer()
    {
        var reference = Well(Rf(100));
        var actual = Well(new DrivenSuperposedField([Rf(100), Rf(0, 2e6)]));
        var point = new Vec3(1e-3, 0, 0);
        Assert.Equal(reference.PeriodSeconds, actual.PeriodSeconds);
        Assert.Equal(reference.PotentialAt(point), actual.PotentialAt(point));
        Assert.Equal(reference.ElectricFieldAt(point), actual.ElectricFieldAt(point));
    }

    /// <summary>Until spectrally weighted averaging exists, a polychromatic well is refused.</summary>
    [Fact]
    public void ActiveSecondFrequencyIsRefusedRatherThanMisweighted()
    {
        var error = Assert.Throws<EinzelException>(() => Well(new DrivenSuperposedField([Rf(100), Rf(100, 2e6)])));
        Assert.Equal(ErrorCodes.RegimeInvalid, error.Error.Code);
    }

    /// <summary>Equal frequencies must combine coherently before their amplitude is squared.</summary>
    [Fact]
    public void EqualFrequencyFieldsCombineBeforeSquaring()
    {
        var point = new Vec3(1e-3, 0, 0);
        var single = Well(Rf(100)).WellAt(point);
        Assert.InRange(Well(new DrivenSuperposedField([Rf(100), Rf(100)])).WellAt(point) / single, 3.999999999, 4.000000001);
        Assert.Equal(0, Well(new DrivenSuperposedField([Rf(100), Rf(-100)])).WellAt(point));
    }

    /// <summary>A small bounded drive can change wholly between the old spatial probes.</summary>
    [Fact]
    public void LocalisedAmplitudeChangeInvalidatesTheCache()
    {
        PonderomotiveField Local(double amplitude) => Well((ITimeVaryingField)BoundedField.Around(
            Rf(amplitude), new FieldRegion(-0.002, 0.002, -0.002, 0.002, -0.002, 0.002)));
        var grid = Grid2D.OverBox(-0.01, -0.01, 0.01, 0.01, 64, 64);
        var cache = new PonderomotiveWellCache(grid, Local(100));
        var updated = Local(200);
        Assert.True(cache.Refresh(updated));
        Assert.Equal(2, cache.Rebuilds);
        var point = new Vec3(grid.X(36), grid.Y(32), 0);
        Assert.Equal(updated.PotentialAt(point), cache.PotentialAt(32 * grid.CountX + 36, point));
        Assert.True(updated.WellAt(point) > 0);
    }

    /// <summary>Relative phase changes must not be mistaken for a common clock shift.</summary>
    [Fact]
    public void RelativePhaseChangeInvalidatesTheCache()
    {
        var first = Rf(100);
        var second = Rf(100);
        var destructive = Well(new DrivenSuperposedField([first, new TimeShiftedField(second, 0.5e-6)]));
        var constructive = Well(new DrivenSuperposedField([first, new TimeShiftedField(second, 0)]));
        var grid = Grid2D.OverBox(-0.002, -0.002, 0.002, 0.002, 16, 16);
        var cache = new PonderomotiveWellCache(grid, destructive);
        Assert.True(cache.Refresh(constructive));
        var point = new Vec3(0.001, 0, 0);
        Assert.True(constructive.WellAt(point) > 1e10 * destructive.WellAt(point));
    }

    /// <summary>A time shift changes the drive instant, not the location of the metal.</summary>
    [Fact]
    public void TimeShiftPreservesConductorQueries()
    {
        var shifted = Assert.IsAssignableFrom<IConductorBounded>(new TimeShiftedField(new Wall(), 4e-6));
        var point = new Vec3(0.25, 0, 0);
        Assert.Equal(-0.25, shifted.SignedDistanceToConductor(point));
        Assert.Equal("wall", shifted.ConductorAt(point));
    }

    private sealed class Wall : ITimeVaryingField, IConductorBounded
    {
        public double ShortestPeriodSeconds => 1e-6;
        public Vec3 ElectricFieldAt(in Vec3 p) => default;
        public Vec3 ElectricFieldAt(in Vec3 p, double t) => default;
        public double PotentialAt(in Vec3 p) => 0;
        public double PotentialAt(in Vec3 p, double t) => 0;
        public double FieldFreeRunLength(in Vec3 p, in Vec3 d) => 0;
        public double SignedDistanceToDiscontinuity(in Vec3 p) => double.PositiveInfinity;
        public double SignedDistanceToConductor(in Vec3 p) => -p.X;
        public string ConductorAt(in Vec3 p) => "wall";
        public double ResolutionLength => 1;
    }
}
