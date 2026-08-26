using System.Text.Json.Serialization;

namespace Einzel.Extensions;

/// <summary>What an extension extends.</summary>
/// <remarks>
/// Spec figure 2's five extension points, all coarse-grained: whole input in,
/// whole output out, one call per run. What is <em>closed</em> to extensions is
/// per-step physics, the field solver inner loop, and the integrator - and the
/// process boundary makes that structural rather than advisory.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ExtensionKind>))]
public enum ExtensionKind
{
    /// <summary>Emits a model document, or part of one.</summary>
    Geometry,

    /// <summary>Computes a figure of merit from a result.</summary>
    Analysis,

    /// <summary>A scalar an optimiser minimises.</summary>
    Objective,

    /// <summary>Decides what a sequencer does next.</summary>
    Sequence,

    /// <summary>Reads or writes a format the platform does not know.</summary>
    Interchange,
}

/// <summary>How much an extension is trusted, which selects the runner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExtensionTrust>))]
public enum ExtensionTrust
{
    /// <summary>
    /// Agent-authored or third-party. Runs in a subprocess, and is the default.
    /// </summary>
    /// <remarks>
    /// EXT-3 makes this the default rather than something a manifest opts into,
    /// because a trust level that has to be asked for is a trust level that gets
    /// granted by accident. An extension that wants the faster runner has to say so.
    /// </remarks>
    Sandboxed,

    /// <summary>
    /// First-party or explicitly trusted. Runs in-process, with no isolation.
    /// </summary>
    Trusted,
}

/// <summary>What an extension needs to run.</summary>
/// <param name="TimeoutMs">Wall-clock ceiling for one call, in milliseconds.</param>
/// <param name="MaximumOutputBytes">Ceiling on what the extension may return.</param>
/// <remarks>
/// Declared rather than discovered, so a run can be refused before it starts
/// rather than after it has consumed the budget. PERF-7 puts a sandboxed round
/// trip under 50 ms, which sets the granularity floor for EXT-4: anything that has
/// to happen more often than that cannot be an extension.
/// </remarks>
public sealed record ExtensionResources(
    int TimeoutMs = 30_000,
    int MaximumOutputBytes = 8 * 1024 * 1024);

/// <summary>
/// An extension, as it declares itself.
/// </summary>
/// <remarks>
/// <para>
/// EXT-1: type, schemas, trust level, resource needs, and a compatible engine
/// version range. The runtime is an implementation detail of the manifest - which
/// is the point of having one, because it means the same declaration can be run
/// two different ways without the extension knowing which.
/// </para>
/// <para>
/// GRD-6 hangs off the identity here. An extension result carries the extension's
/// name and version and cannot present itself as first-party, so a figure of merit
/// computed by somebody's Python is distinguishable from one the engine computed
/// however far downstream it travels.
/// </para>
/// </remarks>
public sealed record ExtensionManifest
{
    /// <summary>The manifest format version.</summary>
    public string ManifestVersion { get; init; } = "0.1";

    /// <summary>A name, unique within a project, used to select this extension.</summary>
    public string? Name { get; init; }

    /// <summary>The extension's own version, carried onto every result it produces.</summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>One sentence saying what it does.</summary>
    public string? Description { get; init; }

    /// <summary>What it extends.</summary>
    public ExtensionKind Kind { get; init; } = ExtensionKind.Objective;

    /// <summary>How much it is trusted, which selects the runner.</summary>
    public ExtensionTrust Trust { get; init; } = ExtensionTrust.Sandboxed;

    /// <summary>The Python file to run, relative to the manifest.</summary>
    public string? Entry { get; init; }

    /// <summary>The function within it to call.</summary>
    public string Function { get; init; } = "run";

    /// <summary>JSON Schema the input must satisfy, or null to accept anything.</summary>
    public System.Text.Json.Nodes.JsonNode? InputSchema { get; init; }

    /// <summary>
    /// JSON Schema the output must satisfy. EXT-7 validates against it.
    /// </summary>
    public System.Text.Json.Nodes.JsonNode? OutputSchema { get; init; }

    /// <summary>Lowest engine version this extension works against.</summary>
    public string? EngineMinimum { get; init; }

    /// <summary>
    /// Highest engine version this extension works against, exclusive.
    /// </summary>
    /// <remarks>
    /// EXT-8 makes the updater report which installed extensions fall outside a new
    /// engine's range before an update is applied. An open upper bound is allowed
    /// and is what most extensions will declare; what it costs is that nobody finds
    /// out it broke until it does.
    /// </remarks>
    public string? EngineMaximum { get; init; }

    /// <summary>
    /// Built-in figures of merit to compute and hand to an objective extension.
    /// </summary>
    /// <remarks>
    /// Section 13 has an optimiser composing objectives from section 12, which may
    /// be Python extensions - so an extension trading resolving power against
    /// envelope needs both computed for it. Declared rather than inferred, because
    /// each ensemble figure flies a cloud, and computing all of them for every draw
    /// of a thousand-draw study would spend most of the study on numbers nobody
    /// asked for.
    /// </remarks>
    public IReadOnlyList<string> Figures { get; init; } = [];

    /// <summary>What it needs to run.</summary>
    public ExtensionResources Resources { get; init; } = new();
}
