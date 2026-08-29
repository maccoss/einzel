using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Render;

/// <summary>One playback phase, as it appears in a render spec file.</summary>
/// <remarks>
/// Quantities rather than bare doubles, which is the rule the model format already
/// enforces and for the same reason: <c>{"until": 10}</c> could be ten microseconds
/// or ten milliseconds, and an agent writing a spec from prose is the actor most
/// likely to mean one and write the other.
/// </remarks>
public sealed record AnimationPhaseDocument
{
    /// <summary>Simulated time this phase runs to.</summary>
    public QuantityValue? Until { get; init; }

    /// <summary>
    /// How much simulated time passes per second of playback - "us/s", "ns/s", "ms/s".
    /// </summary>
    /// <remarks>
    /// Dimensionless, because it is a time over a time. What makes it a rate is that
    /// the denominator is a second of playback rather than a second of flight, and no
    /// dimension can carry that distinction - which is why the field is called
    /// <c>rate</c> and the unit is written with the playback second explicit.
    /// </remarks>
    public QuantityValue? Rate { get; init; }

    /// <summary>What this stretch is, shown on every frame of it.</summary>
    public string? Label { get; init; }
}

/// <summary>A declared time mapping, as it appears in a render spec file.</summary>
/// <remarks>
/// <para>
/// <b>An animation can only be asked for through a file, and that is RND-7 enforcing
/// itself through the interface.</b> The requirement says the mapping is not optional;
/// making it declarable only in a spec means there is no command line that produces an
/// animation without one. A <c>--rate</c> flag would have been the obvious convenience
/// and would have made the hidden-compression case the easy one.
/// </para>
/// <para>
/// It also follows AGT-2 and RND-2: the figure composer edits a text spec that the CLI
/// executes identically, so an animation a person builds in a window is a file an agent
/// can read, diff and re-run.
/// </para>
/// </remarks>
public sealed record AnimationDocument
{
    /// <summary>Frames emitted per second of playback.</summary>
    public int FramesPerSecond { get; init; } = 30;

    /// <summary>Simulated time the animation starts at. The launch when omitted.</summary>
    public QuantityValue? Start { get; init; }

    /// <summary>The phases, in order, each running to its own <c>until</c>.</summary>
    public IReadOnlyList<AnimationPhaseDocument>? Phases { get; init; }

    /// <summary>Resolves the declared quantities into SI.</summary>
    /// <returns>The spec.</returns>
    /// <exception cref="EinzelException">
    /// A phase is missing a time or a rate, or one of them is of the wrong dimension.
    /// </exception>
    public AnimationSpec Compile()
    {
        var phases = new List<AnimationPhase>(Phases?.Count ?? 0);

        for (var i = 0; i < (Phases?.Count ?? 0); i++)
        {
            var phase = Phases![i];
            var path = $"/animation/phases/{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            phases.Add(new AnimationPhase(
                Resolve(phase.Until, path + "/until", Dimension.TimeDimension, "us"),
                Resolve(phase.Rate, path + "/rate", Dimension.Dimensionless, "us/s"),
                phase.Label));
        }

        return new AnimationSpec
        {
            FramesPerSecond = FramesPerSecond,
            StartSeconds = Start is null
                ? 0.0
                : Resolve(Start, "/animation/start", Dimension.TimeDimension, "us"),
            Phases = phases,
        };
    }

    private static double Resolve(
        QuantityValue? value, string path, Dimension dimension, string example)
    {
        if (value is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path,
                Constraint = "this is required and was not given",
                Suggestion = $"write it as {{\"value\": ..., \"unit\": \"{example}\"}}",
            });
        }

        if (string.IsNullOrWhiteSpace(value.Unit))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsRequired,
                Path = path,
                Constraint = "a bare number here is ambiguous",
                Suggestion = $"add a unit, for example \"{example}\". This is the same rule that "
                    + "makes {\"energy\": 4000} a validation error in a model document",
            });
        }

        var quantity = Quantity.From(value.Value, value.Unit!);

        if (quantity.Dimension != dimension)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = path,
                Constraint = $"'{value.Unit}' is not of the expected dimension",
                Suggestion = $"use something like \"{example}\"",
            });
        }

        return quantity.SiValue;
    }
}
