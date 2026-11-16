using Einzel.Core.Geometry;

namespace Einzel.Fields.Tests;

/// <summary>What a field sum says a named conductor holds.</summary>
public sealed class HeldSumTests
{
    /// <summary>
    /// A member that has the name and no single potential for it makes the sum's answer null,
    /// whatever another member holds under the same name.
    /// </summary>
    /// <remarks>
    /// An edge-profiled <c>plate</c> answers "no single value" with null, and so does a member
    /// with no <c>plate</c> at all; a sum that skipped every null gave the other element's 100 V.
    /// </remarks>
    [Fact]
    public void AMemberWithTheNameAndNoSingleValueMakesTheAnswerNull()
    {
        var profiled = new Held(has: true, value: null);
        var fixedPlate = new Held(has: true, value: 100.0);
        var unrelated = new Held(has: false, value: null);

        Assert.Null(new SuperposedField([profiled, fixedPlate]).HeldPotentialOf("plate", 0.0));
        Assert.Null(new SuperposedField([fixedPlate, profiled]).HeldCycleMeanOf("plate", 0.0, 1e-6, 4));

        // The control: a member with no conductor of that name changes nothing.
        Assert.Equal(100.0, new SuperposedField([unrelated, fixedPlate]).HeldPotentialOf("plate", 0.0));
        Assert.True(new SuperposedField([unrelated, profiled]).HasConductor("plate"));
    }

    private sealed class Held(bool has, double? value) : IElectrostaticField, IHeldConductors
    {
        public double ResolutionLength => double.PositiveInfinity;

        public Vec3 ElectricFieldAt(in Vec3 position) => Vec3.Zero;

        public double PotentialAt(in Vec3 position) => 0.0;

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) => 0.0;

        public double SignedDistanceToDiscontinuity(in Vec3 position) => double.PositiveInfinity;

        public double? HeldPotentialOf(string conductor, double timeSeconds) => has ? value : null;

        public bool HasConductor(string conductor) => has;
    }
}
