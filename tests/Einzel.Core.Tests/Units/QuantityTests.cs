using Einzel.Core.Errors;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Units;

public sealed class QuantityTests
{
    [Fact]
    public void ConvertsIntoSiAtTheBoundary()
    {
        // The memo's design point B: 4 keV ions.
        var energy = Quantity.From(4.0, "keV");
        var expected = 4000 * 1.602176634e-19;

        Assert.Equal(Dimension.Energy, energy.Dimension);
        Assert.Equal(expected, energy.SiValue, expected * 1e-12);
    }

    [Fact]
    public void RoundTripsThroughANamedUnit()
    {
        var path = Quantity.From(7.55, "m");
        Assert.Equal(7550.0, path.In("mm"), 9);
    }

    [Theory]
    [InlineData(1.0, "mbar", 100.0)]
    [InlineData(1.0, "Torr", 133.32236842105263)]
    [InlineData(1.0, "u", 1.66053906892e-27)]
    [InlineData(1.0, "e", 1.602176634e-19)]
    [InlineData(300.0, "Å^2", 3e-18)]
    [InlineData(1.0, "deg", Math.PI / 180.0)]
    public void ConvertsKnownUnitsToSi(double value, string unit, double expectedSi)
    {
        // Relative tolerance: these span 1e-27 to 1e2, so a fixed number of
        // decimal places would be vacuous at one end and impossible at the other.
        Assert.Equal(expectedSi, Quantity.From(value, unit).SiValue, Math.Abs(expectedSi) * 1e-12);
    }

    [Fact]
    public void UnitLookupIsCaseSensitive()
    {
        // mm and Mm differ by nine orders of magnitude. A case-insensitive
        // registry would silently accept the wrong one.
        var error = Assert.Throws<EinzelException>(() => Quantity.From(1.0, "MM"));

        Assert.Equal(ErrorCodes.UnitsUnknown, error.Error.Code);
        Assert.Contains("case-sensitive", error.Error.Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingUnitIsAnError()
    {
        // Spec section 9: {"energy": 4000} is a validation error, on purpose.
        var error = Assert.Throws<EinzelException>(() => Quantity.From(4000, string.Empty));

        Assert.Equal(ErrorCodes.UnitsRequired, error.Error.Code);
        Assert.Equal(ErrorSeverity.Error, error.Error.Severity);
    }

    [Fact]
    public void AddingIncompatibleDimensionsIsAnError()
    {
        var length = Quantity.From(1.0, "mm");
        var time = Quantity.From(1.0, "us");

        var error = Assert.Throws<EinzelException>(() => _ = length + time);

        Assert.Equal(ErrorCodes.UnitsIncompatible, error.Error.Code);
        Assert.NotNull(error.Error.Suggestion);
    }

    [Fact]
    public void ExpressingAValueInTheWrongDimensionIsAnError()
    {
        var energy = Quantity.From(4.0, "keV");

        var error = Assert.Throws<EinzelException>(() => energy.In("mm"));

        Assert.Equal(ErrorCodes.UnitsIncompatible, error.Error.Code);
    }

    [Fact]
    public void ArithmeticCombinesDimensions()
    {
        var distance = Quantity.From(3.77, "m");
        var time = Quantity.From(192.0, "µs");

        var velocity = distance / time;

        Assert.Equal(Dimension.Velocity, velocity.Dimension);
        Assert.Equal(3.77 / 192e-6, velocity.SiValue, 6);
    }

    [Fact]
    public void RecoversFlightTimeForTheMemoDesignPoint()
    {
        // Memo section 1: m/z 500 singly charged at 4 keV gives v = 3.93e4 m/s,
        // and design point B is 7.55 m of path in about 192 us. This exercises
        // the whole unit stack on numbers the project has to reproduce anyway.
        var mass = Quantity.From(500.0, "u");
        var energy = Quantity.From(4.0, "keV");

        var speed = Quantity.Si(Math.Sqrt(2.0 * energy.SiValue / mass.SiValue), Dimension.Velocity);
        var flightTime = Quantity.From(7.55, "m") / speed;

        // The memo quotes v to three significant figures and t to the microsecond.
        Assert.Equal(3.93e4, speed.SiValue, 100.0);
        Assert.Equal(192.0, flightTime.In("µs"), 0.5);
    }

    [Fact]
    public void GreekMuNormalisesToTheMicroSign()
    {
        var withGreekMu = Quantity.From(1.0, "μs");
        var withMicroSign = Quantity.From(1.0, "µs");

        Assert.Equal(withMicroSign.SiValue, withGreekMu.SiValue);
    }

    [Fact]
    public void EveryRegisteredUnitResolves()
    {
        // Also proves the registry initialised without a duplicate-key collision.
        Assert.All(UnitRegistry.KnownSymbols, symbol => Assert.NotNull(UnitRegistry.Resolve(symbol)));
        Assert.NotEmpty(UnitRegistry.All);
    }
}
