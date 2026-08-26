namespace Einzel.Core.Model;

/// <summary>
/// A three-dimensional solved field, as it appears in a model document.
/// </summary>
/// <remarks>
/// The field type for a device with no symmetry to exploit. Everything a
/// <c>solved2d</c> element carries is here too - a drive, a sequence, sub-cell
/// boundaries - because none of that was ever about the dimension count; what
/// changes is that the domain is a box rather than a rectangle and an electrode is
/// a body rather than a cross-section.
/// </remarks>
public sealed record SolvedField3DDocument
{
    /// <summary>Lower x bound of the solve domain.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Lower z bound.</summary>
    public QuantityValue? MinZ { get; init; }

    /// <summary>Upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Upper z bound.</summary>
    public QuantityValue? MaxZ { get; init; }

    /// <summary>
    /// Node spacing. Each axis rounds its own interval count up to a power of two
    /// from this, so the domain is meshed exactly and no direction is coarser than
    /// asked.
    /// </summary>
    public QuantityValue? CellSize { get; init; }

    /// <summary>Relative residual the solve must reach.</summary>
    public double Tolerance { get; init; } = 1e-9;

    /// <summary>The RF drive this geometry is operated with, if any.</summary>
    public DriveDocument? Drive { get; init; }

    /// <summary>A timed sequence of states it is operated through, if any.</summary>
    public IReadOnlyList<StageDocument>? Stages { get; init; }

    /// <summary>The electrodes.</summary>
    public IReadOnlyList<Electrode3DDocument>? Electrodes { get; init; }
}

/// <summary>A three-dimensional electrode, as it appears in a model document.</summary>
public sealed record Electrode3DDocument
{
    /// <summary>A name, used in reporting and as the basis-field label.</summary>
    public string? Name { get; init; }

    /// <summary>One of <c>box</c>, <c>sphere</c>, or <c>cylinder</c>.</summary>
    public string? Shape { get; init; }

    /// <summary>Repeats this electrode, binding an index its expressions can name.</summary>
    public RepeatDocument? Repeat { get; init; }

    /// <summary>Box: lower x bound.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Box: lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Box: lower z bound.</summary>
    public QuantityValue? MinZ { get; init; }

    /// <summary>Box: upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Box: upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Box: upper z bound.</summary>
    public QuantityValue? MaxZ { get; init; }

    /// <summary>Sphere or cylinder: centre x.</summary>
    public QuantityValue? CentreX { get; init; }

    /// <summary>Sphere or cylinder: centre y.</summary>
    public QuantityValue? CentreY { get; init; }

    /// <summary>Sphere or cylinder: centre z.</summary>
    public QuantityValue? CentreZ { get; init; }

    /// <summary>Sphere or cylinder: radius.</summary>
    public QuantityValue? Radius { get; init; }

    /// <summary>Cylinder: which axis it runs along, one of <c>x</c>, <c>y</c>, <c>z</c>.</summary>
    public string? Axis { get; init; }

    /// <summary>Cylinder: lower end along its axis.</summary>
    public QuantityValue? Lower { get; init; }

    /// <summary>Cylinder: upper end along its axis.</summary>
    public QuantityValue? Upper { get; init; }

    /// <summary>The potential held. The DC part when driven.</summary>
    public QuantityValue? Potential { get; init; }

    /// <summary>This electrode's share of the drive, zero to peak. Signed.</summary>
    public QuantityValue? DriveAmplitude { get; init; }

    /// <summary>Where in the cycle it sits, as a fraction of one.</summary>
    public double DrivePhase { get; init; }
}

/// <summary>A three-dimensional solved field, validated and reduced to SI.</summary>
public sealed record CompiledSolvedField3D
{
    /// <summary>Solve domain, in metres.</summary>
    public required double MinX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinY { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinZ { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxY { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxZ { get; init; }

    /// <summary>Node spacing, in metres.</summary>
    public required double CellSize { get; init; }

    /// <summary>Relative residual the solve must reach.</summary>
    public double Tolerance { get; init; } = 1e-9;

    /// <summary>The drive, or null when static.</summary>
    public CompiledDrive? Drive { get; init; }

    /// <summary>The timed sequence, or empty for one state.</summary>
    public IReadOnlyList<CompiledStage3D> Stages { get; init; } = [];

    /// <summary>The electrodes.</summary>
    public required IReadOnlyList<CompiledElectrode3D> Electrodes { get; init; }
}

/// <summary>One state of a timed sequence in three dimensions, validated.</summary>
/// <param name="Name">What the stage is for.</param>
/// <param name="DurationSeconds">How long it lasts.</param>
/// <param name="Electrodes">The electrodes as they stand during it.</param>
public sealed record CompiledStage3D(
    string Name, double DurationSeconds, IReadOnlyList<CompiledElectrode3D> Electrodes);
