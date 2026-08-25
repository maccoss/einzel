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
    /// A test for the shipped reflectron, asserting its closed-form flight time.
    /// </summary>
    /// <remarks>
    /// A fresh project has something to run from the first minute, and it is the
    /// right something: the expected value is a closed form rather than a number
    /// this engine produced once and then enshrined. A test whose expectation came
    /// from the code it tests establishes that the code has not changed, which is
    /// a different and much weaker claim than that it is right.
    /// </remarks>
    public const string SingleStageReflectronTest =
        """
        {
          "schemaVersion": "0.1",
          "name": "reflectron-analytic-flight-time",
          "description": "An ideal single-stage reflectron at the first-order energy focus has a closed-form flight time: 2L/v for the field-free path plus 2v/a for the turnaround. For m/z 500 at 4 keV with a 50 mm penetration depth and 100 mm of drift each way, that is 10.180505718 us. The tolerance is ACC-1's one part per million.",
          "model": "../models/reflectron.json",
          "expect": [
            {
              "figureOfMerit": "flightTime",
              "value": 10.180505718,
              "unit": "us",
              "tolerance": 1e-6
            },
            {
              "figureOfMerit": "energyDrift",
              "value": 0,
              "unit": "1",
              "tolerance": 1e-6
            }
          ]
        }

        """;

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
          "schemaVersion": "0.3",
          "name": "single-stage-reflectron",
          "description": "Ideal single-stage reflectron at the first-order energy focus, where the total field-free path is four penetration depths. Analytic flight time 10.1805 us for m/z 500 at 4 keV. Arrival time is flat to first order in energy, so sweeping the energy acceptance moves the flight time by only the second-order term. The mirror depth and the cap potential are declared parameters, so this model can be swept and optimised as it stands.",

          "parameters": {
            "turningDepth": {
              "value": 50, "unit": "mm", "minimum": 5, "maximum": 200,
              "description": "How far into the mirror the potential reaches capPotential. The penetration depth of an ion at the full acceleration potential."
            },
            "capPotential": {
              "value": 4, "unit": "kV", "minimum": 0.1, "maximum": 20,
              "description": "Potential at the back of the mirror. Equal to the acceleration potential here, so the ion turns exactly at turningDepth; a supply drifting off that moves the turning point and the flight time with it."
            },
            "acceleration": {
              "value": 4, "unit": "kV", "minimum": 0.1, "maximum": 20,
              "description": "Acceleration potential the ion is launched at."
            },
            "gradient": {
              "expression": "capPotential / turningDepth", "unit": "V/m",
              "description": "The mirror field. Derived, because it is a consequence of the two above rather than a knob of its own."
            }
          },

          "ion": {
            "massToCharge": { "value": 500, "unit": "Da" },
            "chargeNumber": 1
          },

          "source": {
            "position": { "value": [-100, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "expression": "acceleration", "unit": "kV" },
            "energyFraction": 0
          },

          "fields": [
            {
              "type": "halfSpaceUniform",
              "planePoint": { "value": [0, 0, 0], "unit": "mm" },
              "inwardNormal": { "value": [1, 0, 0] },
              "capPotential": { "expression": "capPotential", "unit": "kV" },
              "turningDepth": { "expression": "turningDepth", "unit": "mm" }
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
