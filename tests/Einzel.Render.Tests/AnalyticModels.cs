using Einzel.Core.Model;
using Einzel.Io;

namespace Einzel.Render.Tests;

/// <summary>Models with no declared solve domain, where the flight sets the page.</summary>
internal static class AnalyticModels
{
    /// <summary>
    /// A reflectron with no declared domain, launched and caught in the same place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Analytic on purpose.</b> A solved geometry takes its extent from its declared
    /// domain, so nothing about the flight can change the page - which makes a device
    /// template useless for testing anything about how the flight and the page relate.
    /// The first version of the fixed-page test below used the einzel lens and passed
    /// with the bug restored, for exactly that reason.
    /// </para>
    /// <para>
    /// Source and detector coincide, which is what a reflectron does and what made the
    /// extent wrong in the first place: the two points that used to define the page are
    /// the same point, while the ion travels 1.3 m between them.
    /// </para>
    /// </remarks>
    public static CompiledModel Reflectron()
    {
        const string json = """
        {
          "schemaVersion": "0.1",
          "name": "analytic-reflectron",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-100, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 4, "unit": "kV" }
          },
          "fields": [
            {
              "type": "halfSpaceUniform",
              "planePoint": { "value": [500, 0, 0], "unit": "mm" },
              "inwardNormal": { "value": [1, 0, 0] },
              "capPotential": { "value": 4, "unit": "kV" },
              "turningDepth": { "value": 200, "unit": "mm" }
            }
          ],
          "detector": {
            "planePoint": { "value": [-100, 0, 0], "unit": "mm" },
            "normal": { "value": [1, 0, 0] }
          },
          "transport": {
            "mode": "trajectory",
            "maximumFlightTime": { "value": 100, "unit": "us" }
          }
        }
        """;

        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

}
