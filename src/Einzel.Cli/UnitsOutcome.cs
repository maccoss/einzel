namespace Einzel.Cli;

/// <summary>The unit symbols a document may use, as <c>einzel schema --units --json</c> reports them.</summary>
public sealed record UnitsOutcome
{
    /// <summary>Every unit, grouped by dimension and then by size.</summary>
    public required IReadOnlyList<UnitJson> Units { get; init; }
}

/// <summary>One unit symbol.</summary>
public sealed record UnitJson
{
    /// <summary>The symbol as a document writes it. Case-sensitive.</summary>
    public required string Symbol { get; init; }

    /// <summary>Its dimension, in SI base units: <c>m kg s^-2</c>, or <c>1</c> for a dimensionless unit.</summary>
    public required string Dimension { get; init; }

    /// <summary>What one of it is in SI: a value in this unit times this is the value in SI.</summary>
    public required double SiFactor { get; init; }
}
