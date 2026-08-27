using Einzel.Project;

namespace Einzel.Commands;

/// <summary>
/// What a task starts from, a worked solution, and several wrong ones.
/// </summary>
/// <param name="Name">A short name for the solution.</param>
/// <param name="Expectation">What scoring it should conclude, and why.</param>
/// <param name="Apply">What an agent taking this approach would leave behind.</param>
/// <remarks>
/// An action rather than a set of files, because some correct approaches are not a
/// file at all. The right answer to "compute a number you would defend" is to run
/// a command, and what makes it right is the manifest that command leaves.
/// </remarks>
public sealed record AgentSolution(string Name, string Expectation, Action<ProjectLayout> Apply);

/// <summary>
/// The models a task seeds, and the approaches used to check that scoring works.
/// </summary>
/// <remarks>
/// The distractors are the load-bearing part. A check that passes the worked
/// solution proves nothing on its own - it has to also reject the plausible wrong
/// answers, or it is measuring whether a file exists rather than whether the task
/// was done. Each one here is a mistake an agent would credibly make.
/// </remarks>
internal static class AgentFixtures
{
    /// <summary>
    /// A drift tube: no field, one closed-form answer.
    /// </summary>
    /// <remarks>
    /// A singly charged ion of m/z 500 through 2000 V reaches 27.78 km/s, and
    /// covers 300 mm in 10.798 us. Nothing here is a golden value: it is
    /// v = sqrt(2qV/m) then t = L/v.
    /// </remarks>
    internal const string DriftTube =
        """
        {
          "schemaVersion": "0.2",
          "name": "drift-tube",
          "description": "A field-free drift tube, 300 mm, for an ion accelerated through 2 kV.",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 2000, "unit": "V" }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [300, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
        }

        """;

    /// <summary>
    /// The same drift tube with its acceleration potential in millimetres.
    /// </summary>
    /// <remarks>
    /// The fault is one token. Everything needed to repair it is in the error the
    /// validator raises - the JSON Pointer to the field, the dimension required,
    /// the dimension supplied - and nothing else in the project hints at it, which
    /// is the point of the task.
    /// </remarks>
    internal const string BrokenDriftTube =
        """
        {
          "schemaVersion": "0.2",
          "name": "drift-tube",
          "description": "A field-free drift tube, 300 mm, for an ion accelerated through 2 kV.",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 2000, "unit": "mm" }
          },
          "fields": [ { "type": "fieldFree" } ],
          "detector": {
            "planePoint": { "value": [300, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
        }

        """;

    /// <summary>Writes a file into a project, creating the directory if needed.</summary>
    internal static void Write(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, contents);
    }

    /// <summary>Puts the shipped reflectron in a project, for tasks that study it.</summary>
    internal static void SeedReflectron(ProjectLayout layout) =>
        Write(Path.Combine(layout.Models, "reflectron.json"), ExampleModels.SingleStageReflectron);

    /// <summary>The tolerance study a correct answer to the machining question leaves.</summary>
    internal const string ToleranceStudy =
        """
        {
          "name": "machining-tolerance",
          "model": "../models/reflectron.json",
          "figureOfMerit": "flightTime",
          "draws": 60,
          "seed": 1,
          "channels": [
            { "parameter": "turningDepth", "halfWidth": 0.2, "unit": "mm" },
            { "parameter": "capPotential", "halfWidth": 5.0, "unit": "V" }
          ]
        }

        """;

    /// <summary>A tolerance study that varies only one of the two things asked about.</summary>
    internal const string OneChannelStudy =
        """
        {
          "name": "machining-tolerance",
          "model": "../models/reflectron.json",
          "figureOfMerit": "flightTime",
          "draws": 60,
          "seed": 1,
          "channels": [
            { "parameter": "capPotential", "halfWidth": 5.0, "unit": "V" }
          ]
        }

        """;

    /// <summary>A tuning study left at the interval the prompt suggested.</summary>
    /// <remarks>
    /// The trap. The optimum is above 3900 V, so this returns the top of its own
    /// box with a non-suppressible warning saying exactly that, and an agent that
    /// reports the number and stops has been told and has not listened.
    /// </remarks>
    internal const string NarrowTuneStudy =
        """
        {
          "name": "tune",
          "model": "../models/reflectron.json",
          "figureOfMerit": "resolvingPower",
          "variables": [
            { "parameter": "capPotential", "minimum": 3600, "maximum": 3900, "unit": "V" }
          ],
          "algorithm": "nelderMead",
          "maximumEvaluations": 25,
          "objectiveTolerance": 1e-4
        }

        """;

    /// <summary>The same study after acting on the warning and widening the interval.</summary>
    internal const string WidenedTuneStudy =
        """
        {
          "name": "tune",
          "model": "../models/reflectron.json",
          "figureOfMerit": "resolvingPower",
          "variables": [
            { "parameter": "capPotential", "minimum": 3600, "maximum": 4600, "unit": "V" }
          ],
          "algorithm": "nelderMead",
          "maximumEvaluations": 40,
          "objectiveTolerance": 1e-4
        }

        """;
}
