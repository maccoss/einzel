using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// A volume solve may declare a face a mirror, which until now only the solver could do.
/// </summary>
/// <remarks>
/// <para>
/// <b>A grounded domain boundary is a third electrode.</b> That is right for a device inside
/// a housing and wrong for one whose geometry is invariant along an axis — a stripe electrode
/// running the length of an analyser's drift makes the field independent of the drift
/// direction, so grounding those faces imposes an axial field the real instrument does not
/// have.
/// </para>
/// <para>
/// <b>The gap this closes is the one this project keeps finding.</b> The three-dimensional
/// solver has supported Neumann faces since it was written — <c>DirichletMask3D</c> carries
/// all six as settable conditions and <c>OperatorStencil3D</c> honours them — and <i>no
/// document could ask for one</i>. The plane path has had <c>rightEdge</c> throughout. Same
/// shape as <c>ITransportMode</c> named only in a csproj, and as <c>drivePhase</c> being a
/// plain double until a travelling wave needed a ramp.
/// </para>
/// <para>
/// It was found by an ion drifting <b>backwards</b>: at a 3.5 per cent injection angle an
/// Astral skeleton should drift at +1375 m/s and measured −480, because its stripe electrodes
/// spanned the whole domain in z and collided with a grounded wall they touched.
/// </para>
/// </remarks>
public sealed class NeumannFaceTests(ITestOutputHelper output)
{
    /// <summary>
    /// Two charged rails spanning the whole domain in z, so the geometry repeats along it.
    /// </summary>
    /// <remarks>
    /// The rails run the full z extent and touch both z faces, which is what a stripe
    /// electrode does. With those faces grounded the boundary is an extra conductor at zero
    /// abutting one at a kilovolt; with them mirrored the structure is infinite in z, which
    /// is what the geometry means.
    /// </remarks>
    private static string Document(bool mirrored) =>
        $$"""
        {
          "schemaVersion": "0.7",
          "name": "rails",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 10], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 100, "unit": "V" }
          },
          "fields": [
            {
              "type": "solved3d",
              "solve3d": {
                "minX": { "value": -10, "unit": "mm" },
                "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" },
                "maxY": { "value": 10, "unit": "mm" },
                "minZ": { "value": 0, "unit": "mm" },
                "maxZ": { "value": 40, "unit": "mm" },
                "cellSize": { "value": 1.25, "unit": "mm" },
        {{(mirrored ? "\"lowerZEdge\": \"neumann\", \"upperZEdge\": \"neumann\"," : "")}}
                "electrodes": [
                  {
                    "name": "upper",
                    "shape": "box",
                    "minX": { "value": -6, "unit": "mm" },
                    "maxX": { "value": 6, "unit": "mm" },
                    "minY": { "value": 4, "unit": "mm" },
                    "maxY": { "value": 6, "unit": "mm" },
                    "minZ": { "value": 0, "unit": "mm" },
                    "maxZ": { "value": 40, "unit": "mm" },
                    "potential": { "value": 1000, "unit": "V" }
                  },
                  {
                    "name": "lower",
                    "shape": "box",
                    "minX": { "value": -6, "unit": "mm" },
                    "maxX": { "value": 6, "unit": "mm" },
                    "minY": { "value": -6, "unit": "mm" },
                    "maxY": { "value": -4, "unit": "mm" },
                    "minZ": { "value": 0, "unit": "mm" },
                    "maxZ": { "value": 40, "unit": "mm" },
                    "potential": { "value": 1000, "unit": "V" }
                  }
                ]
              }
            }
          ],
          "detector": {
            "planePoint": { "value": [9, 0, 10], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "maximumFlightTime": { "value": 50, "unit": "us" },
            "relativeTolerance": 1e-10
          }
        }
        """;

    private static IElectrostaticField Field(bool mirrored)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(Document(mirrored)));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return FieldAssembly.Build(validation.Model!);
    }

    /// <summary>The axial field is zero when the faces are mirrors, and is not when they are walls.</summary>
    /// <remarks>
    /// <para>
    /// <b>The claim, and its control, in one measurement.</b> Stripe electrodes invariant
    /// along z produce a field with no z component — that is what "invariant" means. With
    /// the faces mirrored the solve reproduces it; with them grounded the boundary is a
    /// conductor at zero abutting one at a kilovolt, and there is a large axial field
    /// pointing at it.
    /// </para>
    /// <para>
    /// Sampled near a face rather than at the centre, because that is where a wall bites and
    /// where an injected ion actually is: the Astral skeleton launched 20 mm from its z
    /// boundary and drifted the wrong way.
    /// </para>
    /// </remarks>
    [Fact]
    public void MirroredFacesRemoveTheAxialFieldAGroundedWallImposes()
    {
        var mirrored = Field(mirrored: true);
        var grounded = Field(mirrored: false);

        // On the axis, a quarter of the way in from the lower z face.
        var near = new Vec3(0.0, 0.0, 0.005);
        var middle = new Vec3(0.0, 0.0, 0.020);

        var mirroredNear = mirrored.ElectricFieldAt(near).Z;
        var groundedNear = grounded.ElectricFieldAt(near).Z;

        output.WriteLine($"axial field 5 mm from the face");
        output.WriteLine($"  faces mirrored   {mirroredNear,14:F4} V/m");
        output.WriteLine($"  faces grounded   {groundedNear,14:F4} V/m");
        output.WriteLine($"  ratio            {Math.Abs(groundedNear / Math.Max(Math.Abs(mirroredNear), 1e-30)):E2}");

        // A geometry invariant along z has no axial field. Not small: zero, to round-off.
        Assert.True(
            Math.Abs(mirroredNear) < 1e-6,
            $"with both z faces mirrored the structure repeats forever along z, so the axial "
            + $"field must vanish - it was {mirroredNear:E3} V/m");

        // And the control: with walls there is a large one, so the test is not passing
        // because the geometry has no axial field to begin with.
        Assert.True(
            Math.Abs(groundedNear) > 1.0,
            $"a grounded z face abutting a 1 kV rail should impose a substantial axial field, "
            + $"and only {groundedNear:E3} V/m appeared - if this has become small the "
            + "control has stopped controlling anything");

        // The mirrored solve is z-invariant everywhere, not only near the face - sampled
        // OFF AXIS, because on the axis two symmetric rails give zero transverse field by
        // symmetry and the comparison would be 0 against 0. A first version did exactly
        // that and would have passed against a solve with no z invariance at all.
        var offAxisNear = mirrored.ElectricFieldAt(new Vec3(0.002, 0.002, 0.005));
        var offAxisMiddle = mirrored.ElectricFieldAt(new Vec3(0.002, 0.002, 0.020));

        output.WriteLine(string.Empty);
        output.WriteLine($"mirrored, off-axis transverse at z =  5 mm  {offAxisNear.Y,12:F4} V/m");
        output.WriteLine($"mirrored, off-axis transverse at z = 20 mm  {offAxisMiddle.Y,12:F4} V/m");

        Assert.True(
            Math.Abs(offAxisNear.Y) > 1.0,
            "the off-axis sample must see a real transverse field, or comparing the two "
            + "places proves nothing");

        Assert.Equal(offAxisMiddle.Y, offAxisNear.Y, 6);
    }

    /// <summary>A tilt on a shape that cannot be tilted is refused, not dropped.</summary>
    /// <remarks>
    /// The tilt properties live on the shared electrode document, so a sphere or cylinder
    /// declaring one binds cleanly and the unrecognised-property refusal never fires. Left
    /// alone, the box branch never runs and the model solves as an axis-aligned cylinder -
    /// validating, converging, and answering a different question than it appears to ask,
    /// which is the failure strict key checking exists to prevent.
    /// </remarks>
    [Fact]
    public void ATiltOnANonBoxShapeIsRefused()
    {
        // The control: the two rails are boxes, and a box may be tilted. Without this the
        // test would pass equally if tilts were refused everywhere.
        Assert.True(ModelValidator.Validate(ModelJson.Parse(Document(mirrored: true))).IsValid);

        var validation = ModelValidator.Validate(ModelJson.Parse(TiltedCylinder));

        Assert.False(validation.IsValid);

        var error = validation.Errors[0];

        output.WriteLine($"{error.Code} at {error.Path}");
        output.WriteLine($"  {error.Constraint}");
        output.WriteLine($"  {error.Suggestion}");

        Assert.Contains("tiltHalfTurns", error.Path, StringComparison.Ordinal);
        Assert.Contains("only a box may be tilted", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>A cylinder that declares a tilt, which is meaningless for its shape.</summary>
    private const string TiltedCylinder = """
        {
          "schemaVersion": "0.7",
          "name": "tilted-cylinder",
          "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 100, "unit": "V" }
          },
          "fields": [ { "type": "solved3d", "solve3d": {
              "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
              "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
              "minZ": { "value": -10, "unit": "mm" }, "maxZ": { "value": 10, "unit": "mm" },
              "cellSize": { "value": 1.25, "unit": "mm" },
              "electrodes": [ {
                "name": "rod", "shape": "cylinder", "axis": "z",
                "centreX": { "value": 0, "unit": "mm" },
                "centreY": { "value": 0, "unit": "mm" },
                "radius": { "value": 2, "unit": "mm" },
                "lower": { "value": -8, "unit": "mm" },
                "upper": { "value": 8, "unit": "mm" },
                "tiltHalfTurns": { "value": 0.1, "unit": "1" },
                "potential": { "value": 100, "unit": "V" }
              } ] } } ],
          "detector": {
            "planePoint": { "value": [9, 0, 0], "unit": "mm" },
            "normal": { "value": [-1, 0, 0] }
          },
          "transport": {
            "maximumFlightTime": { "value": 50, "unit": "us" },
            "relativeTolerance": 1e-10
          }
        }
        """;


    /// <summary>An unknown face condition is refused, naming what is permitted.</summary>
    /// <remarks>
    /// AGT-3: an error is a recovery instruction. A misspelled condition silently treated as
    /// the default would give a model that validates, solves, and answers a different
    /// question — which is the whole argument for refusing an unrecognised property rather
    /// than ignoring it.
    /// </remarks>
    [Fact]
    public void AnUnknownFaceConditionIsRefused()
    {
        var document = ModelJson.Parse(
            Document(mirrored: false).Replace(
                "\"cellSize\": { \"value\": 1.25, \"unit\": \"mm\" },",
                "\"cellSize\": { \"value\": 1.25, \"unit\": \"mm\" }, \"lowerZEdge\": \"mirror\",",
                StringComparison.Ordinal));

        var validation = ModelValidator.Validate(document);

        Assert.False(validation.IsValid);

        var error = validation.Errors[0];

        output.WriteLine($"{error.Code} at {error.Path}");
        output.WriteLine($"  {error.Constraint}");
        output.WriteLine($"  {error.Suggestion}");

        Assert.Contains("lowerZEdge", error.Path, StringComparison.Ordinal);
        Assert.Contains("neumann", error.Suggestion ?? string.Empty, StringComparison.Ordinal);
    }
}
