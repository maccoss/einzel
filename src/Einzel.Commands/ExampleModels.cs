namespace Einzel.Commands;

/// <summary>
/// Reference models shipped with the platform.
/// </summary>
/// <remarks>
/// <para>
/// The beginning of the corpus EX-1 calls for: "at least thirty validated
/// reference models spanning every device class, each with a prose description,
/// expected results, and assertion tolerances." The reasoning behind it is worth
/// keeping in view — SIMION has decades of forum posts and published geometries
/// in the training data of every model an agent might run on, and Einzel has none
/// of that. Shipping models an agent can pull into context is the counter.
/// </para>
/// <para>
/// One model so far. EX-2 makes the corpus a release gate once there are enough
/// to gate on.
/// </para>
/// </remarks>
public static class ExampleModels
{
    private static readonly Dictionary<string, string> All = new(StringComparer.Ordinal)
    {
        ["single-stage-reflectron"] = SingleStageReflectron,
    };

    /// <summary>The examples that ship, by name.</summary>
    public static IReadOnlyList<string> Names => [.. All.Keys.OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>The text of one example.</summary>
    /// <param name="name">Which example.</param>
    /// <returns>The model JSON.</returns>
    /// <exception cref="KeyNotFoundException">No example by that name.</exception>
    public static string Read(string name) => All[name];

    /// <summary>
    /// An ideal single-stage reflectron at the first-order energy focus.
    /// </summary>
    /// <remarks>
    /// The geometry sets the total field-free path to four penetration depths,
    /// which is the classic condition for dT/dv to vanish. Expected flight time is
    /// 10.1805 microseconds, and the arrival time should be flat to first order
    /// across the plus or minus 3 to 5 percent energy acceptance the companion
    /// memo asks for.
    /// </remarks>
    public const string SingleStageReflectron =
        """
        {
          "schemaVersion": "0.1",
          "name": "single-stage-reflectron",
          "description": "Ideal single-stage reflectron at the first-order energy focus, where the total field-free path is four penetration depths. Analytic flight time 10.1805 us for m/z 500 at 4 keV. Arrival time is flat to first order in energy, so sweeping /source/energyFraction over -0.05 to 0.05 should move the flight time by only the second-order term.",
          "ion": {
            "massToCharge": { "value": 500, "unit": "Da" },
            "chargeNumber": 1
          },
          "source": {
            "position": { "value": [-100, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 4, "unit": "kV" },
            "energyFraction": 0
          },
          "fields": [
            {
              "type": "halfSpaceUniform",
              "planePoint": { "value": [0, 0, 0], "unit": "mm" },
              "inwardNormal": { "value": [1, 0, 0] },
              "capPotential": { "value": 4, "unit": "kV" },
              "turningDepth": { "value": 50, "unit": "mm" }
            }
          ],
          "detector": {
            "planePoint": { "value": [-100, 0, 0], "unit": "mm" },
            "normal": { "value": [1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "relativeTolerance": 1e-11,
            "maximumFlightTime": { "value": 1, "unit": "ms" },
            "sampleInterval": { "value": 20, "unit": "ns" }
          }
        }

        """;
}
