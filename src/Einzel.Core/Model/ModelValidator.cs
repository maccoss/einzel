using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>The outcome of validating a model document.</summary>
/// <param name="Model">The compiled model, or null when validation failed.</param>
/// <param name="Errors">Every error found, in document order.</param>
public sealed record ModelValidation(CompiledModel? Model, IReadOnlyList<EinzelError> Errors)
{
    /// <summary>Whether the document validated.</summary>
    public bool IsValid => Model is not null && Errors.Count == 0;
}

/// <summary>
/// Validates a model document and compiles it to SI.
/// </summary>
/// <remarks>
/// <para>
/// Every error is collected rather than thrown on the first failure. AGT-3 makes
/// errors recovery instructions, and the recovery an agent wants is the full
/// list: fixing one unit at a time across five round trips is the behaviour this
/// avoids.
/// </para>
/// <para>
/// Checks are ordered so that later ones can assume earlier ones passed. Where a
/// prerequisite is missing the dependent check is skipped rather than reported as
/// a second failure, so one mistake produces one error.
/// </para>
/// </remarks>
public static class ModelValidator
{
    /// <summary>Validates and compiles a document.</summary>
    /// <param name="document">The document to validate.</param>
    /// <returns>The compiled model, or the errors that prevented it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static ModelValidation Validate(ModelDocument document) => Validate(document, overrides: null);

    /// <summary>Validates and compiles a document with parameter overrides applied.</summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="overrides">
    /// Replacement values for free parameters, as a sweep or optimiser supplies.
    /// Derived parameters re-evaluate against them.
    /// </param>
    /// <returns>The compiled model, or the errors that prevented it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static ModelValidation Validate(
        ModelDocument document, IReadOnlyDictionary<string, Quantity>? overrides)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<EinzelError>();

        ValidateSchemaVersion(document, errors);

        var surface = ParameterSurface.Resolve(document.Parameters, overrides, errors);

        if (surface is null)
        {
            return new ModelValidation(null, errors);
        }

        var p = surface.Values();

        var (mass, charge) = ValidateIon(document.Ion, p, errors);
        // Fields are compiled before the source because whether a source may start
        // at rest depends on whether anything else can accelerate it: a beam
        // carries its own energy, a trapped packet is accelerated by the
        // instrument. Their errors go to a separate list and are spliced back
        // afterwards, so the reordering does not leak into the reported order -
        // ModelValidation promises errors in document order and /source precedes
        // /fields.
        var fieldErrors = new List<EinzelError>();

        // A stage sets parameters, so resolving one means resolving the whole
        // surface again with those values layered on - derived parameters and all.
        // Passed as a closure because only the solve branch needs it, and threading
        // the declared parameters through every field kind to reach it would put
        // the sequencer in the signature of things that have nothing to do with it.
        IReadOnlyDictionary<string, Quantity>? Restage(
            IReadOnlyDictionary<string, Quantity> set, List<EinzelError> into)
        {
            var merged = new Dictionary<string, Quantity>(StringComparer.Ordinal);

            if (overrides is not null)
            {
                foreach (var (name, value) in overrides)
                {
                    merged[name] = value;
                }
            }

            foreach (var (name, value) in set)
            {
                merged[name] = value;
            }

            return ParameterSurface.Resolve(document.Parameters, merged, into)?.Values();
        }

        var fields = ValidateFields(document.Fields, p, Restage, fieldErrors);

        // A field that failed to compile is not evidence that nothing can
        // accelerate the ion, it is evidence that we cannot tell. Saying otherwise
        // adds a second error advising the author to declare a field they did
        // declare, and one mistake should produce one error.
        var canAccelerate = fieldErrors.Count > 0 || CanAccelerate(fields);

        var source = ValidateSource(document.Source, p, errors, canAccelerate);
        errors.AddRange(fieldErrors);

        var detector = ValidateDetector(document.Detector, p, errors);
        var transport = ValidateTransport(document.Transport, p, errors);

        if (errors.Count > 0 || mass is null || charge is null
            || source is null || detector is null || transport is null)
        {
            return new ModelValidation(null, errors);
        }

        var model = new CompiledModel
        {
            Source = document,
            MassSi = mass.Value,
            ChargeSi = charge.Value,
            SourcePosition = source.Position,
            SourceDirection = source.Direction,
            Cloud = source.Cloud,
            AccelerationPotentialSi = source.Potential,
            EnergyFraction = source.EnergyFraction,
            Fields = fields,
            DetectorPoint = detector.Value.Point,
            DetectorNormal = detector.Value.Normal,
            TransportMode = transport.Mode,
            RelativeTolerance = transport.RelativeTolerance,
            MaximumFlightTimeSi = transport.MaximumFlightTime,
            SampleIntervalSi = transport.SampleInterval,
            Gas = transport.Gas,
            Mobility = transport.Mobility,
            DensityGrid = transport.DensityGrid,
            SpaceChargeMode = transport.SpaceCharge,
            Parameters = surface,
        };

        ValidateGeometryConsistency(model, errors);

        return errors.Count > 0 ? new ModelValidation(null, errors) : new ModelValidation(model, errors);
    }

    private static void ValidateSchemaVersion(ModelDocument document, List<EinzelError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            errors.Add(Missing("/schemaVersion", "a model document must declare its schema version",
                $"add \"schemaVersion\": \"{ModelSchema.CurrentVersion}\""));
            return;
        }

        if (!ModelSchema.SupportedVersions.Contains(document.SchemaVersion, StringComparer.Ordinal))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/schemaVersion",
                Constraint = $"this build reads schema versions {string.Join(", ", ModelSchema.SupportedVersions)}",
                Observed = new ObservedValue(0.0, document.SchemaVersion),
                Suggestion = $"this build writes version {ModelSchema.CurrentVersion}",
            });
        }
    }

    private static (double? Mass, double? Charge) ValidateIon(IonDocument? ion, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (ion is null)
        {
            errors.Add(Missing("/ion", "a model must declare the ion being tracked",
                "add an \"ion\" object with \"massToCharge\" and \"chargeNumber\""));
            return (null, null);
        }

        if (ion.ChargeNumber == 0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/ion/chargeNumber",
                Constraint = "an ion cannot have zero charge",
                Observed = new ObservedValue(0, "1"),
                Suggestion = "use 1 for a singly charged cation",
            });
        }

        if (ion.MassToCharge is null)
        {
            errors.Add(Missing("/ion/massToCharge", "the ion's mass-to-charge ratio is required",
                "add {\"value\": 500, \"unit\": \"Da\"}"));
            return (null, null);
        }

        var massToCharge = TryQuantity(ion.MassToCharge, "/ion/massToCharge", Dimension.MassDimension, p, errors);

        if (massToCharge is null || ion.ChargeNumber == 0)
        {
            return (null, null);
        }

        if (massToCharge.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/ion/massToCharge",
                Constraint = "mass-to-charge must be positive",
                Observed = new ObservedValue(ion.MassToCharge.Value, ion.MassToCharge.Unit),
                Suggestion = "supply a positive mass-to-charge ratio",
            });
            return (null, null);
        }

        var mass = massToCharge.Value.SiValue * Math.Abs(ion.ChargeNumber);
        var charge = Quantity.From(ion.ChargeNumber, "e").SiValue;

        return (mass, charge);
    }

    private sealed record SourceValues(
        Vec3 Position, Vec3 Direction, double Potential, double EnergyFraction, IonCloudSettings Cloud);

    /// <summary>
    /// Whether any declared field could put energy into an ion that starts at rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Magnitude rather than kind. A uniform field of zero, or a solved geometry
    /// whose every electrode is grounded, is field-free in everything but its type
    /// discriminator - and an ion at rest in one sits there until the flight-time
    /// ceiling expires, which is exactly the outcome the zero-potential check
    /// exists to prevent. Testing the kind alone would narrow that check into
    /// uselessness rather than narrowing it correctly.
    /// </para>
    /// <para>
    /// Also the predicate <see cref="ValidateGeometryConsistency"/> needs for
    /// "something can turn the ion around", so it is written once.
    /// </para>
    /// </remarks>
    internal static bool CanAccelerate(IReadOnlyList<CompiledField>? fields) =>
        fields is not null && fields.Any(CanDoWork);

    private static bool CanDoWork(CompiledField field) => field.Kind switch
    {
        CompiledFieldKind.FieldFree => false,
        CompiledFieldKind.Uniform => field.Field.LengthSquared > 0.0,
        CompiledFieldKind.HalfSpaceUniform => field.PotentialGradientSi != 0.0,

        // A solve with every electrode at the same potential has no gradient
        // anywhere, and grounded boundaries make that potential zero.
        CompiledFieldKind.Solved2D =>
            field.Solve is { } solve && solve.Electrodes.Any(e => e.Potential != 0.0),

        _ => true,
    };

    private static SourceValues? ValidateSource(
        SourceDocument? source,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        bool canAccelerate)
    {
        if (source is null)
        {
            errors.Add(Missing("/source", "a model must declare where the ion starts",
                "add a \"source\" object with \"position\", \"direction\", and \"accelerationPotential\""));
            return null;
        }

        var position = TryVector(source.Position, "/source/position", Dimension.LengthDimension, p, errors);
        var direction = TryDirection(source.Direction, "/source/direction", errors);
        var potential = TryQuantity(
            source.AccelerationPotential, "/source/accelerationPotential", Dimension.ElectricPotential, p, errors);

        if (source.EnergyFraction <= -1.0 || !double.IsFinite(source.EnergyFraction))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/energyFraction",
                Constraint = "the energy offset must be finite and greater than -1, so the ion has positive energy",
                Observed = new ObservedValue(source.EnergyFraction, "1"),
                Suggestion = "use 0 for nominal energy, or 0.05 for 5 percent high",
            });
            return null;
        }

        if (position is null || direction is null || potential is null)
        {
            return null;
        }

        // Zero is legal when the instrument does the accelerating. A pulsed
        // extraction trap holds its packet at rest and then switches a field on,
        // which is the entire mechanism, and a model that cannot say so cannot
        // describe one. It stays an error when nothing in the model could move
        // the ion, because then it really does sit there.
        if (potential.Value.SiValue == 0.0 && !canAccelerate)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/accelerationPotential",
                Constraint =
                    "the accelerating potential may only be zero when a field can accelerate the ion, "
                    + "and this model declares none that can",
                Observed = new ObservedValue(source.AccelerationPotential!.Value, source.AccelerationPotential.Unit),
                Suggestion =
                    "supply a non-zero potential, for example {\"value\": 4, \"unit\": \"kV\"}, "
                    + "or declare a field that accelerates the ion from rest",
            });
            return null;
        }

        var cloud = ValidateCloud(source.Cloud, p, errors);

        return cloud is null
            ? null
            : new SourceValues(
                position.Value, direction.Value, potential.Value.SiValue, source.EnergyFraction, cloud);
    }

    /// <summary>
    /// Reads the source cloud, or returns the single-ion default when none is
    /// declared.
    /// </summary>
    /// <remarks>
    /// Every spread is optional and every default is zero, so a model that says
    /// nothing about a cloud launches exactly what it launched before. That is not
    /// only backward compatibility: a spread that appeared by default would change
    /// every existing result silently, and a resolving power quietly getting worse
    /// is indistinguishable from a bug.
    /// </remarks>
    private static IonCloudSettings? ValidateCloud(
        CloudDocument? cloud, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (cloud is null)
        {
            return new IonCloudSettings();
        }

        if (cloud.Ions < 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/cloud/ions",
                Constraint = "a cloud launches at least one ion",
                Observed = new ObservedValue(cloud.Ions, "1"),
                Suggestion = "ACC-5 wants a transmission interval within one per cent at 95%, which needs of "
                    + "order a thousand ions; fewer gives an honest error bar too wide to design against",
            });

            return null;
        }

        if (cloud.EnergyFractionSpread < 0.0 || !double.IsFinite(cloud.EnergyFractionSpread))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/cloud/energyFractionSpread",
                Constraint = "an energy spread is a non-negative fraction of the nominal energy",
                Observed = new ObservedValue(cloud.EnergyFractionSpread, "1"),
                Suggestion = "use 0.01 for one per cent, or omit it for a monoenergetic cloud",
            });

            return null;
        }

        var temperature = Optional(
            cloud.Temperature, "/source/cloud/temperature", Dimension.TemperatureDimension, p, errors);

        var transverse = Optional(
            cloud.TransverseSpread, "/source/cloud/transverseSpread", Dimension.LengthDimension, p, errors);

        var longitudinal = Optional(
            cloud.LongitudinalSpread, "/source/cloud/longitudinalSpread", Dimension.LengthDimension, p, errors);

        if (temperature is null || transverse is null || longitudinal is null)
        {
            return null;
        }

        foreach (var (value, path) in new[]
        {
            (temperature.Value, "/source/cloud/temperature"),
            (transverse.Value, "/source/cloud/transverseSpread"),
            (longitudinal.Value, "/source/cloud/longitudinalSpread"),
        })
        {
            if (value < 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = path,
                    Constraint = "a spread cannot be negative",
                    Observed = new ObservedValue(value, "SI"),
                    Suggestion = "omit it for zero",
                });

                return null;
            }
        }

        if (cloud.Population is { } declared && declared < 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/cloud/population",
                Constraint = "a packet holds at least one ion",
                Observed = new ObservedValue(declared, "1"),
                Suggestion = "omit it to model the ions you launch as the ions that are there, or set 1 to "
                    + "sample an intrinsic source property one ion at a time",
            });

            return null;
        }

        return new IonCloudSettings
        {
            Ions = cloud.Ions,
            Population = cloud.Population,
            Seed = cloud.Seed,
            TemperatureK = temperature.Value,
            TransverseSpreadM = transverse.Value,
            LongitudinalSpreadM = longitudinal.Value,
            EnergyFractionSpread = cloud.EnergyFractionSpread,
        };
    }

    /// <summary>A quantity that may be absent, reading as zero when it is.</summary>
    private static double? Optional(
        QuantityValue? value,
        string path,
        Dimension expected,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (value is null)
        {
            return 0.0;
        }

        var quantity = TryQuantity(value, path, expected, p, errors);
        return quantity?.SiValue;
    }

    private static List<CompiledField> ValidateFields(
        IReadOnlyList<FieldDocument>? fields,
        IReadOnlyDictionary<string, Quantity> p,
        StageResolver restage,
        List<EinzelError> errors)
    {
        if (fields is null || fields.Count == 0)
        {
            errors.Add(Missing("/fields", "a model must declare at least one field element",
                "add [{\"type\": \"fieldFree\"}] for a drift-only model"));
            return [];
        }

        var compiled = new List<CompiledField>(fields.Count);

        for (var i = 0; i < fields.Count; i++)
        {
            var element = CompileField(fields[i], $"/fields/{i}", p, restage, errors);

            if (element is not null)
            {
                compiled.Add(element);
            }
        }

        return compiled;
    }

    /// <summary>Resolves the parameter surface as it stands during one stage.</summary>
    /// <param name="set">The values the stage holds, with units.</param>
    /// <param name="into">Where to report a value that does not resolve.</param>
    /// <returns>The surface, or null when the stage could not be resolved.</returns>
    internal delegate IReadOnlyDictionary<string, Quantity>? StageResolver(
        IReadOnlyDictionary<string, Quantity> set, List<EinzelError> into);

    private static CompiledField? CompileField(
        FieldDocument field,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        StageResolver restage,
        List<EinzelError> errors)
    {
        switch (field.Type)
        {
            case "fieldFree":
                return new CompiledField { Kind = CompiledFieldKind.FieldFree };

            case "uniform":
            {
                var vector = TryVector(field.Field, $"{path}/field", Dimension.ElectricField, p, errors);
                return vector is null ? null : new CompiledField
                {
                    Kind = CompiledFieldKind.Uniform,
                    Field = vector.Value,
                };
            }

            case "halfSpaceUniform":
            {
                var point = TryVector(field.PlanePoint, $"{path}/planePoint", Dimension.LengthDimension, p, errors);
                var normal = TryDirection(field.InwardNormal, $"{path}/inwardNormal", errors);
                var cap = TryQuantity(field.CapPotential, $"{path}/capPotential", Dimension.ElectricPotential, p, errors);
                var depth = TryQuantity(field.TurningDepth, $"{path}/turningDepth", Dimension.LengthDimension, p, errors);

                if (point is null || normal is null || cap is null || depth is null)
                {
                    return null;
                }

                if (depth.Value.SiValue <= 0.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/turningDepth",
                        Constraint = "the turning depth must be positive",
                        Observed = new ObservedValue(field.TurningDepth!.Value, field.TurningDepth.Unit),
                        Suggestion = "supply a positive depth, for example {\"value\": 50, \"unit\": \"mm\"}",
                    });
                    return null;
                }

                return new CompiledField
                {
                    Kind = CompiledFieldKind.HalfSpaceUniform,
                    PlanePoint = point.Value,
                    InwardNormal = normal.Value,
                    PotentialGradientSi = cap.Value.SiValue / depth.Value.SiValue,
                    TurningDepthSi = depth.Value.SiValue,
                };
            }

            case "solved2d":
                return CompileSolvedField(field.Solve, $"{path}/solve", p, restage, errors);

            case "solved3d":
                return CompileSolved3D(field.Solve3d, $"{path}/solve3d", p, restage, errors);

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/type",
                    Constraint =
                        "a field element must declare one of: fieldFree, uniform, halfSpaceUniform, solved2d",
                    Observed = new ObservedValue(0.0, field.Type ?? "null"),
                    Suggestion = "use \"halfSpaceUniform\" for an ideal single-stage ion mirror",
                });
                return null;
        }
    }

    /// <summary>
    /// Compiles the stages a solve is operated through, checking that they only
    /// change what a sequence is allowed to change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stage sets parameters, and parameters reach everything - which is what
    /// makes them the right vocabulary and also what makes a check necessary. Move
    /// a plate between stages and the mask changes, so every stage would need its
    /// own basis solves and its own grid; the field would still be computed, and it
    /// would be wrong in a way nothing else would catch.
    /// </para>
    /// <para>
    /// So the rule is exact and stated: a stage may change what an electrode
    /// <em>holds</em> - its potential, its drive amplitude, its phase - and nothing
    /// about where it is. Anything else is refused, naming the electrode and the
    /// stage.
    /// </para>
    /// </remarks>
    /// <summary>
    /// An empty surface, for values that are literals rather than expressions.
    /// </summary>
    /// <remarks>
    /// A stage duration is a wall-clock time, not a design dimension, so it does
    /// not name parameters - and giving it the surface would let it name one that
    /// the stage itself is about to change.
    /// </remarks>
    private static readonly Dictionary<string, Quantity> NoParameters = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, QuantityValue> NoOverrides = new(StringComparer.Ordinal);

    private static List<CompiledStage> CompileStages(
        SolvedFieldDocument solve,
        string path,
        IReadOnlyList<CompiledElectrode> baseline,
        StageResolver restage,
        List<EinzelError> errors)
    {
        var stages = new List<CompiledStage>();

        if (solve.Stages is not { Count: > 0 } declared || solve.Electrodes is not { } declaredElectrodes)
        {
            return stages;
        }

        for (var k = 0; k < declared.Count; k++)
        {
            var stage = declared[k];
            var stagePath = $"{path}/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var name = stage.Name ?? $"stage {k}";

            var duration = TryQuantity(
                stage.Duration,
                $"{stagePath}/duration",
                Dimension.TimeDimension,
                NoParameters,
                errors);

            if (duration is null || duration.Value.SiValue <= 0.0)
            {
                if (duration is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{stagePath}/duration",
                        Constraint = "a stage must last a positive time",
                        Observed = new ObservedValue(duration.Value.SiValue, "s"),
                        Suggestion = "give the stage a duration, for example {\"value\": 100, \"unit\": \"us\"}",
                    });
                }

                continue;
            }

            var set = new Dictionary<string, Quantity>(StringComparer.Ordinal);

            foreach (var (parameter, value) in stage.Set ?? NoOverrides)
            {
                try
                {
                    set[parameter] = Quantity.From(value.Value, value.Unit);
                }
                catch (EinzelException failure)
                {
                    errors.Add(failure.Error with { Path = $"{stagePath}/set/{parameter}" });
                }
            }

            var surface = restage(set, errors);

            if (surface is null)
            {
                continue;
            }

            var electrodes = new List<CompiledElectrode>();

            for (var i = 0; i < declaredElectrodes.Count; i++)
            {
                Expand(declaredElectrodes[i], $"{stagePath}/electrodes/{i}", surface, electrodes, errors);
            }

            if (!SameGeometry(baseline, electrodes, name, stagePath, errors))
            {
                continue;
            }

            stages.Add(new CompiledStage(name, duration.Value.SiValue, electrodes));
        }

        return stages;
    }

    /// <summary>Whether two compilations put the same metal in the same places.</summary>
    private static bool SameGeometry(
        IReadOnlyList<CompiledElectrode> baseline,
        List<CompiledElectrode> staged,
        string stage,
        string path,
        List<EinzelError> errors)
    {
        if (baseline.Count != staged.Count)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/set",
                Constraint =
                    $"stage '{stage}' changes how many electrodes there are, from {baseline.Count} to {staged.Count}",
                Observed = new ObservedValue(staged.Count, "electrodes"),
                Suggestion = "a stage may change what an electrode holds, not whether it exists",
            });

            return false;
        }

        for (var i = 0; i < baseline.Count; i++)
        {
            var a = baseline[i];
            var b = staged[i];

            var moved = a.Shape != b.Shape
                || a.MinX != b.MinX || a.MaxX != b.MaxX
                || a.MinY != b.MinY || a.MaxY != b.MaxY
                || a.CentreX != b.CentreX || a.CentreY != b.CentreY || a.Radius != b.Radius;

            if (moved)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/set",
                    Constraint =
                        $"stage '{stage}' moves electrode '{a.Name}', and a sequence may only change "
                        + "what an electrode holds, not where it is",
                    Observed = new ObservedValue(0.0, a.Name),
                    Suggestion =
                        "set only the parameters that reach potentials, amplitudes and phases; a stage that "
                        + "moves metal would need its own solve and its own grid",
                });

                return false;
            }
        }

        return true;
    }

    /// <summary>Compiles a three-dimensional solved field element.</summary>
    /// <summary>Compiles one declared 3D electrode into the electrodes it stands for.</summary>
    private static void Expand3D(
        Electrode3DDocument declared,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<CompiledElectrode3D> into,
        List<EinzelError> errors)
    {
        if (declared.Repeat is not { } repeat)
        {
            var single = CompileElectrode3D(declared, path, p, errors);

            if (single is not null)
            {
                into.Add(single);
            }

            return;
        }

        var count = TryQuantity(repeat.Count, $"{path}/repeat/count", Dimension.Dimensionless, p, errors);

        if (count is null)
        {
            return;
        }

        var copies = (int)Math.Round(count.Value.SiValue);

        if (copies < 1 || Math.Abs(count.Value.SiValue - copies) > 1e-9)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/repeat/count",
                Constraint = "a repeat count must be a whole number of at least one",
                Observed = new ObservedValue(count.Value.SiValue, "1"),
                Suggestion = "use a parameter that evaluates to a whole number, for example 3",
            });

            return;
        }

        var index = repeat.Index ?? "index";

        if (p.ContainsKey(index))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/repeat/index",
                Constraint = $"'{index}' is already a declared parameter, and binding it here would shadow it",
                Observed = new ObservedValue(0.0, index),
                Suggestion = "choose another index name, such as 'rod' or 'section'",
            });

            return;
        }

        var name = declared.Name ?? "electrode";

        for (var k = 0; k < copies; k++)
        {
            var scoped = new Dictionary<string, Quantity>(p, StringComparer.Ordinal)
            {
                [index] = Quantity.Si(k, Dimension.Dimensionless),
            };

            var copy = CompileElectrode3D(
                declared with
                {
                    Repeat = null,
                    Name = $"{name}-{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                },
                $"{path}/repeat/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                scoped,
                errors);

            if (copy is not null)
            {
                into.Add(copy);
            }
        }
    }

    private static CompiledElectrode3D? CompileElectrode3D(
        Electrode3DDocument electrode,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var length = Dimension.LengthDimension;
        var volt = Dimension.ElectricPotential;
        var name = electrode.Name ?? "electrode";

        var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);

        var amplitude = electrode.DriveAmplitude is null
            ? 0.0
            : TryQuantity(electrode.DriveAmplitude, $"{path}/driveAmplitude", volt, p, errors)?.SiValue ?? 0.0;

        if (potential is null)
        {
            return null;
        }

        var common = new CompiledElectrode3D
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            Potential = potential.Value.SiValue,
            DriveAmplitude = amplitude,
            DrivePhase = Phase(electrode.DrivePhase, $"{path}/drivePhase", p, errors),
        };

        switch (electrode.Shape)
        {
            case "box":
            {
                var minX = TryQuantity(electrode.MinX, $"{path}/minX", length, p, errors);
                var minY = TryQuantity(electrode.MinY, $"{path}/minY", length, p, errors);
                var minZ = TryQuantity(electrode.MinZ, $"{path}/minZ", length, p, errors);
                var maxX = TryQuantity(electrode.MaxX, $"{path}/maxX", length, p, errors);
                var maxY = TryQuantity(electrode.MaxY, $"{path}/maxY", length, p, errors);
                var maxZ = TryQuantity(electrode.MaxZ, $"{path}/maxZ", length, p, errors);

                if (minX is null || minY is null || minZ is null
                    || maxX is null || maxY is null || maxZ is null)
                {
                    return null;
                }

                if (minX.Value.SiValue > maxX.Value.SiValue
                    || minY.Value.SiValue > maxY.Value.SiValue
                    || minZ.Value.SiValue > maxZ.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint =
                            $"box '{name}' is inverted, and an inverted box vanishes from the solve "
                            + "rather than failing",
                        Observed = new ObservedValue(maxX.Value.SiValue - minX.Value.SiValue, "m"),
                        Suggestion = "check the expressions that derive the bounds",
                    });

                    return null;
                }

                return common with
                {
                    Shape = Electrode3DShape.Box,
                    MinX = minX.Value.SiValue,
                    MinY = minY.Value.SiValue,
                    MinZ = minZ.Value.SiValue,
                    MaxX = maxX.Value.SiValue,
                    MaxY = maxY.Value.SiValue,
                    MaxZ = maxZ.Value.SiValue,
                };
            }

            case "sphere":
            case "cylinder":
            {
                var centreX = TryQuantity(electrode.CentreX, $"{path}/centreX", length, p, errors);
                var centreY = TryQuantity(electrode.CentreY, $"{path}/centreY", length, p, errors);
                var radius = TryQuantity(electrode.Radius, $"{path}/radius", length, p, errors);

                var centreZ = electrode.CentreZ is null
                    ? Quantity.Si(0.0, length)
                    : TryQuantity(electrode.CentreZ, $"{path}/centreZ", length, p, errors);

                if (centreX is null || centreY is null || centreZ is null || radius is null)
                {
                    return null;
                }

                if (radius.Value.SiValue <= 0.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/radius",
                        Constraint = $"'{name}' must have positive radius",
                        Observed = new ObservedValue(radius.Value.SiValue, "m"),
                        Suggestion = "supply a positive radius",
                    });

                    return null;
                }

                if (electrode.Shape == "sphere")
                {
                    return common with
                    {
                        Shape = Electrode3DShape.Sphere,
                        CentreX = centreX.Value.SiValue,
                        CentreY = centreY.Value.SiValue,
                        CentreZ = centreZ.Value.SiValue,
                        Radius = radius.Value.SiValue,
                    };
                }

                var lower = TryQuantity(electrode.Lower, $"{path}/lower", length, p, errors);
                var upper = TryQuantity(electrode.Upper, $"{path}/upper", length, p, errors);

                if (lower is null || upper is null)
                {
                    return null;
                }

                var axis = electrode.Axis switch
                {
                    null or "z" => CylinderAxis.Z,
                    "x" => CylinderAxis.X,
                    "y" => CylinderAxis.Y,
                    _ => (CylinderAxis?)null,
                };

                if (axis is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/axis",
                        Constraint = "a cylinder axis must be 'x', 'y' or 'z'",
                        Observed = new ObservedValue(0.0, electrode.Axis ?? "(none)"),
                        Suggestion = "'z' when omitted; the centre is then given as centreX and centreY",
                    });

                    return null;
                }

                if (upper.Value.SiValue <= lower.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint = $"cylinder '{name}' must have upper above lower along its axis",
                        Observed = new ObservedValue(upper.Value.SiValue - lower.Value.SiValue, "m"),
                        Suggestion = "check the expressions that derive the ends",
                    });

                    return null;
                }

                return common with
                {
                    Shape = Electrode3DShape.Cylinder,
                    CentreX = centreX.Value.SiValue,
                    CentreY = centreY.Value.SiValue,
                    CentreZ = centreZ.Value.SiValue,
                    Radius = radius.Value.SiValue,
                    Axis = axis.Value,
                    Lower = lower.Value.SiValue,
                    Upper = upper.Value.SiValue,
                };
            }

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/shape",
                    Constraint = "an electrode shape must be 'box', 'sphere' or 'cylinder'",
                    Observed = new ObservedValue(0.0, electrode.Shape ?? "(none)"),
                    Suggestion =
                        "a box is a plate or a housing, a cylinder is a rod or a tube, a sphere is a bead",
                });

                return null;
        }
    }

    private static CompiledField? CompileSolved3D(
        SolvedField3DDocument? solve,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        StageResolver restage,
        List<EinzelError> errors)
    {
        if (solve is null)
        {
            errors.Add(Missing(path, "a solved3d field needs a solve3d block",
                "give it a box, a cell size, and at least one electrode"));
            return null;
        }

        var length = Dimension.LengthDimension;

        var minX = TryQuantity(solve.MinX, $"{path}/minX", length, p, errors);
        var minY = TryQuantity(solve.MinY, $"{path}/minY", length, p, errors);
        var minZ = TryQuantity(solve.MinZ, $"{path}/minZ", length, p, errors);
        var maxX = TryQuantity(solve.MaxX, $"{path}/maxX", length, p, errors);
        var maxY = TryQuantity(solve.MaxY, $"{path}/maxY", length, p, errors);
        var maxZ = TryQuantity(solve.MaxZ, $"{path}/maxZ", length, p, errors);
        var cell = TryQuantity(solve.CellSize, $"{path}/cellSize", length, p, errors);

        if (minX is null || minY is null || minZ is null
            || maxX is null || maxY is null || maxZ is null || cell is null)
        {
            return null;
        }

        if (maxX.Value.SiValue <= minX.Value.SiValue
            || maxY.Value.SiValue <= minY.Value.SiValue
            || maxZ.Value.SiValue <= minZ.Value.SiValue)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a solve domain must have positive extent on every axis",
                Observed = new ObservedValue(maxX.Value.SiValue - minX.Value.SiValue, "m"),
                Suggestion = "check that each max exceeds its min",
            });

            return null;
        }

        if (cell.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/cellSize",
                Constraint = "a cell size must be positive",
                Observed = new ObservedValue(cell.Value.SiValue, "m"),
                Suggestion = "supply a positive spacing, for example {\"value\": 0.2, \"unit\": \"mm\"}",
            });

            return null;
        }

        var electrodes = new List<CompiledElectrode3D>();

        for (var i = 0; i < (solve.Electrodes?.Count ?? 0); i++)
        {
            Expand3D(solve.Electrodes![i], $"{path}/electrodes/{i}", p, electrodes, errors);
        }

        if (electrodes.Count == 0 && errors.Count == 0)
        {
            errors.Add(Missing($"{path}/electrodes",
                "a solved3d field needs at least one electrode",
                "add a box, a sphere or a cylinder"));
        }

        var drive = Drive(solve.Drive, $"{path}/drive", p, errors);
        var stages = CompileStages3D(solve, $"{path}/stages", electrodes, restage, errors);

        if (drive is null && electrodes.Any(e => e.IsDriven))
        {
            var driven = electrodes.First(e => e.IsDriven);

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/drive",
                Constraint =
                    $"electrode '{driven.Name}' declares a drive amplitude, but the solve declares no drive",
                Observed = new ObservedValue(driven.DriveAmplitude, "V"),
                Suggestion = "add a drive block with a frequency, or use 'potential' for a static electrode",
            });
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new CompiledField
        {
            Kind = CompiledFieldKind.Solved3D,
            Solve3D = new CompiledSolvedField3D
            {
                MinX = minX.Value.SiValue,
                MinY = minY.Value.SiValue,
                MinZ = minZ.Value.SiValue,
                MaxX = maxX.Value.SiValue,
                MaxY = maxY.Value.SiValue,
                MaxZ = maxZ.Value.SiValue,
                CellSize = cell.Value.SiValue,
                Tolerance = solve.Tolerance,
                Drive = drive,
                Stages = stages,
                Electrodes = electrodes,
            },
        };
    }

    private static List<CompiledStage3D> CompileStages3D(
        SolvedField3DDocument solve,
        string path,
        IReadOnlyList<CompiledElectrode3D> baseline,
        StageResolver restage,
        List<EinzelError> errors)
    {
        var stages = new List<CompiledStage3D>();

        if (solve.Stages is not { Count: > 0 } declared || solve.Electrodes is not { } declaredElectrodes)
        {
            return stages;
        }

        for (var k = 0; k < declared.Count; k++)
        {
            var stage = declared[k];
            var stagePath = $"{path}/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var name = stage.Name ?? $"stage {k}";

            var duration = TryQuantity(
                stage.Duration, $"{stagePath}/duration", Dimension.TimeDimension, NoParameters, errors);

            if (duration is null || duration.Value.SiValue <= 0.0)
            {
                if (duration is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{stagePath}/duration",
                        Constraint = "a stage must last a positive time",
                        Observed = new ObservedValue(duration.Value.SiValue, "s"),
                        Suggestion = "give the stage a duration, for example {\"value\": 100, \"unit\": \"us\"}",
                    });
                }

                continue;
            }

            var set = new Dictionary<string, Quantity>(StringComparer.Ordinal);

            foreach (var (parameter, value) in stage.Set ?? NoOverrides)
            {
                try
                {
                    set[parameter] = Quantity.From(value.Value, value.Unit);
                }
                catch (EinzelException failure)
                {
                    errors.Add(failure.Error with { Path = $"{stagePath}/set/{parameter}" });
                }
            }

            var surface = restage(set, errors);

            if (surface is null)
            {
                continue;
            }

            var electrodes = new List<CompiledElectrode3D>();

            for (var i = 0; i < declaredElectrodes.Count; i++)
            {
                Expand3D(declaredElectrodes[i], $"{stagePath}/electrodes/{i}", surface, electrodes, errors);
            }

            if (!SameGeometry3D(baseline, electrodes, name, stagePath, errors))
            {
                continue;
            }

            stages.Add(new CompiledStage3D(name, duration.Value.SiValue, electrodes));
        }

        return stages;
    }

    /// <summary>Whether two compilations put the same metal in the same places.</summary>
    private static bool SameGeometry3D(
        IReadOnlyList<CompiledElectrode3D> baseline,
        List<CompiledElectrode3D> staged,
        string stage,
        string path,
        List<EinzelError> errors)
    {
        if (baseline.Count != staged.Count)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/set",
                Constraint =
                    $"stage '{stage}' changes how many electrodes there are, from {baseline.Count} to {staged.Count}",
                Observed = new ObservedValue(staged.Count, "electrodes"),
                Suggestion = "a stage may change what an electrode holds, not whether it exists",
            });

            return false;
        }

        for (var i = 0; i < baseline.Count; i++)
        {
            var a = baseline[i];
            var b = staged[i];

            var moved = a.Shape != b.Shape || a.Axis != b.Axis
                || a.MinX != b.MinX || a.MaxX != b.MaxX
                || a.MinY != b.MinY || a.MaxY != b.MaxY
                || a.MinZ != b.MinZ || a.MaxZ != b.MaxZ
                || a.CentreX != b.CentreX || a.CentreY != b.CentreY || a.CentreZ != b.CentreZ
                || a.Radius != b.Radius || a.Lower != b.Lower || a.Upper != b.Upper;

            if (moved)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/set",
                    Constraint =
                        $"stage '{stage}' moves electrode '{a.Name}', and a sequence may only change "
                        + "what an electrode holds, not where it is",
                    Observed = new ObservedValue(0.0, a.Name),
                    Suggestion =
                        "set only the parameters that reach potentials, amplitudes and phases; a stage that "
                        + "moves metal would need its own solve and its own grid",
                });

                return false;
            }
        }

        return true;
    }

    private static CompiledField? CompileSolvedField(
        SolvedFieldDocument? solve,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        StageResolver restage,
        List<EinzelError> errors)
    {
        if (solve is null)
        {
            errors.Add(Missing(path, "a solved field must declare its domain and electrodes",
                "add a \"solve\" object with bounds, cellSize, and electrodes"));
            return null;
        }

        var length = Dimension.LengthDimension;
        var minX = TryQuantity(solve.MinX, $"{path}/minX", length, p, errors);
        var minY = TryQuantity(solve.MinY, $"{path}/minY", length, p, errors);
        var maxX = TryQuantity(solve.MaxX, $"{path}/maxX", length, p, errors);
        var maxY = TryQuantity(solve.MaxY, $"{path}/maxY", length, p, errors);
        var cell = TryQuantity(solve.CellSize, $"{path}/cellSize", length, p, errors);

        if (minX is null || minY is null || maxX is null || maxY is null || cell is null)
        {
            return null;
        }

        if (maxX.Value.SiValue <= minX.Value.SiValue || maxY.Value.SiValue <= minY.Value.SiValue)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "the solve domain must have positive extent in both directions",
                Suggestion = "check that maxX exceeds minX and maxY exceeds minY",
            });
            return null;
        }

        if (cell.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/cellSize",
                Constraint = "the cell size must be positive",
                Observed = new ObservedValue(cell.Value.SiValue, "m"),
                Suggestion = "about a thirtieth of the smallest feature is a reasonable start",
            });
            return null;
        }

        if (solve.Electrodes is null || solve.Electrodes.Count == 0)
        {
            errors.Add(Missing($"{path}/electrodes", "a solved field needs at least one electrode",
                "add an electrode with a shape and a potential"));
            return null;
        }

        var electrodes = new List<CompiledElectrode>();

        for (var i = 0; i < solve.Electrodes.Count; i++)
        {
            Expand(solve.Electrodes[i], $"{path}/electrodes/{i}", p, electrodes, errors);
        }

        var reflect = solve.ReflectAboutX is null
            ? (double?)null
            : TryQuantity(solve.ReflectAboutX, $"{path}/reflectAboutX", length, p, errors)?.SiValue;

        var drive = Drive(solve.Drive, $"{path}/drive", p, errors);
        var stages = CompileStages(solve, $"{path}/stages", electrodes, restage, errors);

        var symmetry = Symmetry(solve.Symmetry, $"{path}/symmetry", errors);

        // The axis is at y = 0 and a radius cannot be negative. Refused rather
        // than folded, because a domain drawn across the axis is a different
        // intent from one drawn beside it and guessing which would be worse than
        // asking.
        if (symmetry == SolveSymmetry.Cylindrical && minY is { } radialFloor && radialFloor.SiValue < 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/minY",
                Constraint = "a cylindrical solve has y as the radius, so the domain cannot reach below zero",
                Observed = new ObservedValue(radialFloor.SiValue, "m"),
                Suggestion =
                    "set minY to 0 to include the axis, or to a positive radius for an annulus; "
                    + "x is the axis of rotation",
            });
        }

        var left = Boundary(solve.LeftEdge, $"{path}/leftEdge", errors);
        var right = Boundary(solve.RightEdge, $"{path}/rightEdge", errors);
        var bottom = Boundary(solve.BottomEdge, $"{path}/bottomEdge", errors);
        var top = Boundary(solve.TopEdge, $"{path}/topEdge", errors);

        // An amplitude with no generator behind it is a document that thinks it
        // declared RF and did not. Silence here is the expensive kind.
        if (drive is null && electrodes.Any(e => e.IsDriven))
        {
            var driven = electrodes.First(e => e.IsDriven);

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/drive",
                Constraint =
                    $"electrode '{driven.Name}' declares a drive amplitude, but the solve declares no drive",
                Observed = new ObservedValue(driven.DriveAmplitude, "V"),
                Suggestion =
                    "add a drive block with a frequency, or remove the amplitude and use "
                    + "'potential' for a static electrode",
            });
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new CompiledField
        {
            Kind = CompiledFieldKind.Solved2D,
            Solve = new CompiledSolvedField
            {
                MinX = minX.Value.SiValue,
                MinY = minY.Value.SiValue,
                MaxX = maxX.Value.SiValue,
                MaxY = maxY.Value.SiValue,
                CellSize = cell.Value.SiValue,
                Symmetry = symmetry,
                Drive = drive,
                Stages = stages,
                LeftEdge = left,
                RightEdge = right,
                BottomEdge = bottom,
                TopEdge = top,
                Electrodes = electrodes,
                BoundaryIsDiscontinuous = solve.BoundaryIsDiscontinuous,
                Tolerance = solve.Tolerance,
                ReflectAboutX = reflect,
            },
        };
    }

    /// <summary>How one electrode taps the drive: amplitude and phase.</summary>
    /// <remarks>
    /// Read for every electrode whether or not the geometry declares a drive, so
    /// that an amplitude on a static solve is caught where it is written rather
    /// than silently ignored - which is the failure mode that makes someone spend
    /// an afternoon wondering why the RF is not doing anything.
    /// </remarks>
    private static (double Amplitude, double Phase) Tap(
        ElectrodeDocument electrode,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var amplitude = electrode.DriveAmplitude is null
            ? 0.0
            : TryQuantity(
                electrode.DriveAmplitude, $"{path}/driveAmplitude",
                Dimension.ElectricPotential, p, errors)?.SiValue ?? 0.0;

        return (amplitude, Phase(electrode.DrivePhase, $"{path}/drivePhase", p, errors));
    }

    /// <summary>
    /// A drive phase, as a fraction of a cycle, resolved through the parameters.
    /// </summary>
    /// <remarks>
    /// Dimensionless, because a fraction of a cycle is. That makes it one of the
    /// few places a bare number is legitimate, and the units grammar already treats
    /// "1" as the dimensionless unit - so an expression over the repeat index reads
    /// as <c>{"expression": "ring / ringsPerWave"}</c> with no unit to argue about.
    /// </remarks>
    private static double Phase(
        QuantityValue? declared,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (declared is null)
        {
            return 0.0;
        }

        var resolved = TryQuantity(declared, path, Dimension.Dimensionless, p, errors);

        if (resolved is null)
        {
            return 0.0;
        }

        var phase = resolved.Value.SiValue;

        if (!double.IsFinite(phase))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a drive phase is a fraction of a cycle and must be finite",
                Observed = new ObservedValue(phase, "1"),
                Suggestion = "use 0 for in phase and 0.5 for antiphase",
            });
        }

        return phase;
    }

    private static CompiledDrive? Drive(
        DriveDocument? drive,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (drive is null)
        {
            return null;
        }

        var frequency = TryQuantity(drive.Frequency, $"{path}/frequency", Dimension.Frequency, p, errors);

        if (frequency is null)
        {
            return null;
        }

        if (frequency.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/frequency",
                Constraint = "a drive frequency must be positive",
                Observed = new ObservedValue(frequency.Value.SiValue, "Hz"),
                Suggestion = "supply a positive frequency, for example {\"value\": 1, \"unit\": \"MHz\"}",
            });

            return null;
        }

        var waveform = drive.Waveform switch
        {
            null or "sinusoid" => DriveWaveform.Sinusoid,
            "rectangular" => DriveWaveform.Rectangular,
            _ => (DriveWaveform?)null,
        };

        if (waveform is null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/waveform",
                Constraint = "a drive waveform must be 'sinusoid' or 'rectangular'",
                Observed = new ObservedValue(0.0, drive.Waveform ?? "(none)"),
                Suggestion =
                    "'sinusoid' is what a resonant circuit produces and gives the Mathieu equation; "
                    + "'rectangular' is what a switching supply produces and gives Meissner's",
            });

            return null;
        }

        var duty = drive.DutyCycle ?? 0.5;

        if (duty is <= 0.0 or >= 1.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/dutyCycle",
                Constraint = "a duty cycle is the fraction of a cycle spent high, strictly between 0 and 1",
                Observed = new ObservedValue(duty, "1"),
                Suggestion = "0.5 is a balanced square wave; 0.61 is a typical digital mass-filter setting",
            });

            return null;
        }

        return new CompiledDrive(frequency.Value.SiValue, waveform.Value, duty);
    }

    private static SolveSymmetry Symmetry(string? declared, string path, List<EinzelError> errors)
    {
        switch (declared)
        {
            case null or "translational":
                return SolveSymmetry.Translational;

            case "cylindrical":
                return SolveSymmetry.Cylindrical;

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = path,
                    Constraint = "a solve symmetry must be 'translational' or 'cylindrical'",
                    Observed = new ObservedValue(0.0, declared),
                    Suggestion =
                        "'translational' extrudes the cross-section along the third axis and is the "
                        + "default; 'cylindrical' rotates it about the x axis, with y as the radius",
                });

                return SolveSymmetry.Translational;
        }
    }

    private static BoundaryKind Boundary(string? declared, string path, List<EinzelError> errors)
    {
        switch (declared)
        {
            case null or "dirichlet":
                return BoundaryKind.Dirichlet;

            case "neumann":
                return BoundaryKind.Neumann;

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = path,
                    Constraint = "an edge condition must be 'dirichlet' or 'neumann'",
                    Observed = new ObservedValue(0.0, declared),
                    Suggestion = "'neumann' is a symmetry plane; omit the field for 'dirichlet'",
                });
                return BoundaryKind.Dirichlet;
        }
    }

    /// <summary>
    /// Compiles one declared electrode into the electrodes it stands for.
    /// </summary>
    /// <remarks>
    /// One, unless it declares a repeat, in which case the index is bound as an
    /// ordinary parameter and every expression on the electrode sees it. That is
    /// what keeps a stack of two hundred rings a parametric document rather than a
    /// generated one: the placements are still expressions, so a sweep can still
    /// move them.
    /// </remarks>
    private static void Expand(
        ElectrodeDocument declared,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<CompiledElectrode> into,
        List<EinzelError> errors)
    {
        if (declared.Repeat is not { } repeat)
        {
            var single = CompileElectrode(declared, path, p, errors);

            if (single is not null)
            {
                into.Add(single);
            }

            return;
        }

        var count = TryQuantity(repeat.Count, $"{path}/repeat/count", Dimension.Dimensionless, p, errors);

        if (count is null)
        {
            return;
        }

        var copies = (int)Math.Round(count.Value.SiValue);

        if (copies < 1 || Math.Abs(count.Value.SiValue - copies) > 1e-9)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/repeat/count",
                Constraint = "a repeat count must be a whole number of at least one",
                Observed = new ObservedValue(count.Value.SiValue, "1"),
                Suggestion = "use a parameter that evaluates to a whole number, for example 24",
            });

            return;
        }

        var index = repeat.Index ?? "index";

        if (p.ContainsKey(index))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/repeat/index",
                Constraint = $"'{index}' is already a declared parameter, and binding it here would shadow it",
                Observed = new ObservedValue(0.0, index),
                Suggestion = "choose another index name, such as 'ring' or 'plate'",
            });

            return;
        }

        var name = declared.Name ?? "electrode";

        for (var k = 0; k < copies; k++)
        {
            var scoped = new Dictionary<string, Quantity>(p, StringComparer.Ordinal)
            {
                [index] = Quantity.Si(k, Dimension.Dimensionless),
            };

            // Named by position so a loss itemisation, a channel report or an error
            // says which one - "ring-17" rather than "ring", seventeen times.
            var copy = CompileElectrode(
                declared with { Repeat = null, Name = $"{name}-{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}" },
                $"{path}/repeat/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                scoped,
                errors);

            if (copy is not null)
            {
                into.Add(copy);
            }
        }
    }

    private static CompiledElectrode? CompileElectrode(
        ElectrodeDocument electrode, string path, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        var length = Dimension.LengthDimension;
        var volt = Dimension.ElectricPotential;
        var name = electrode.Name ?? "electrode";

        switch (electrode.Shape)
        {
            case "rectangle":
            {
                var minX = TryQuantity(electrode.MinX, $"{path}/minX", length, p, errors);
                var minY = TryQuantity(electrode.MinY, $"{path}/minY", length, p, errors);
                var maxX = TryQuantity(electrode.MaxX, $"{path}/maxX", length, p, errors);
                var maxY = TryQuantity(electrode.MaxY, $"{path}/maxY", length, p, errors);
                var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);
                var drive = Tap(electrode, path, p, errors);

                if (minX is null || minY is null || maxX is null || maxY is null || potential is null)
                {
                    return null;
                }

                // An inverted rectangle is not an empty electrode, it is a
                // disappeared one: the rasteriser walks from a higher index to a
                // lower one, marks nothing, and the solve proceeds as though the
                // electrode were never declared. That is reachable from ordinary
                // parameter arithmetic - a derived half-width that goes negative
                // when a gap grows past a radius - so it is exactly the kind of
                // silent geometry failure a tolerance sweep would attribute to
                // physics. The disc case has always rejected a non-positive
                // radius; this is the same check for the other primitive.
                //
                // Equality is allowed. A rectangle of zero extent in one axis is a
                // line segment, which is how an infinitely thin plate is written -
                // the mirror template's cap is one - and cut cells resolve it
                // exactly. Only min strictly greater than max is nonsense.
                if (minX.Value.SiValue > maxX.Value.SiValue
                    || minY.Value.SiValue > maxY.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint =
                            $"rectangle '{name}' is inverted: it needs minX <= maxX and minY <= maxY, "
                            + "and an inverted one vanishes from the solve rather than failing",
                        Observed = new ObservedValue(
                            maxX.Value.SiValue - minX.Value.SiValue,
                            "m of width, by "
                            + (maxY.Value.SiValue - minY.Value.SiValue).ToString(
                                "G6", System.Globalization.CultureInfo.InvariantCulture)
                            + " m of height"),
                        Suggestion =
                            "check the expressions that derive the bounds; a derived half-width "
                            + "can go negative when one parameter grows past another",
                    });

                    return null;
                }

                return new CompiledElectrode
                    {
                        Name = name,
                        Shape = ElectrodeShape.Rectangle,
                        MinX = minX.Value.SiValue,
                        MinY = minY.Value.SiValue,
                        MaxX = maxX.Value.SiValue,
                        MaxY = maxY.Value.SiValue,
                        Potential = potential.Value.SiValue,
                        DriveAmplitude = drive.Amplitude,
                        DrivePhase = drive.Phase,
                    };
            }

            case "disc":
            {
                var centreX = TryQuantity(electrode.CentreX, $"{path}/centreX", length, p, errors);
                var centreY = TryQuantity(electrode.CentreY, $"{path}/centreY", length, p, errors);
                var radius = TryQuantity(electrode.Radius, $"{path}/radius", length, p, errors);
                var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);
                var drive = Tap(electrode, path, p, errors);

                if (centreX is null || centreY is null || radius is null || potential is null)
                {
                    return null;
                }

                if (radius.Value.SiValue <= 0.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/radius",
                        Constraint = "a disc electrode must have positive radius",
                        Observed = new ObservedValue(radius.Value.SiValue, "m"),
                        Suggestion = "supply a positive radius",
                    });
                    return null;
                }

                return new CompiledElectrode
                {
                    Name = name,
                    Shape = ElectrodeShape.Disc,
                    CentreX = centreX.Value.SiValue,
                    CentreY = centreY.Value.SiValue,
                    Radius = radius.Value.SiValue,
                    Potential = potential.Value.SiValue,
                    DriveAmplitude = drive.Amplitude,
                    DrivePhase = drive.Phase,
                };
            }

            case "edgeProfile":
            {
                if (electrode.Profile is null || electrode.Profile.Count < 2)
                {
                    errors.Add(Missing($"{path}/profile",
                        "an edge profile needs at least two points to interpolate between",
                        "add a list of {at, potential} pairs"));
                    return null;
                }

                var edge = electrode.Edge switch
                {
                    "left" => GridEdge.Left,
                    "right" => GridEdge.Right,
                    "bottom" => GridEdge.Bottom,
                    "top" => GridEdge.Top,
                    _ => (GridEdge?)null,
                };

                if (edge is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/edge",
                        Constraint = "an edge profile must name one of: left, right, bottom, top",
                        Observed = new ObservedValue(0.0, electrode.Edge ?? "null"),
                        Suggestion = "'top' and 'bottom' are the facing boards of a planar geometry",
                    });
                    return null;
                }

                var points = new List<(double At, double Potential)>(electrode.Profile.Count);

                for (var k = 0; k < electrode.Profile.Count; k++)
                {
                    var at = TryQuantity(electrode.Profile[k].At, $"{path}/profile/{k}/at", length, p, errors);
                    var potential = TryQuantity(
                        electrode.Profile[k].Potential, $"{path}/profile/{k}/potential", volt, p, errors);

                    if (at is not null && potential is not null)
                    {
                        points.Add((at.Value.SiValue, potential.Value.SiValue));
                    }
                }

                if (points.Count != electrode.Profile.Count)
                {
                    return null;
                }

                points.Sort((a, b) => a.At.CompareTo(b.At));

                return new CompiledElectrode
                {
                    Name = name,
                    Shape = ElectrodeShape.EdgeProfile,
                    Edge = edge.Value,
                    Profile = points,
                };
            }

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/shape",
                    Constraint = "an electrode must declare one of: rectangle, disc, edgeProfile",
                    Observed = new ObservedValue(0.0, electrode.Shape ?? "null"),
                    Suggestion = "'disc' is a rod in cross-section; 'edgeProfile' is a printed board",
                });
                return null;
        }
    }

    private static (Vec3 Point, Vec3 Normal)? ValidateDetector(DetectorDocument? detector, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (detector is null)
        {
            errors.Add(Missing("/detector", "a model must declare the surface that ends the flight",
                "add a \"detector\" object with \"planePoint\" and \"normal\""));
            return null;
        }

        var point = TryVector(detector.PlanePoint, "/detector/planePoint", Dimension.LengthDimension, p, errors);
        var normal = TryDirection(detector.Normal, "/detector/normal", errors);

        return point is null || normal is null ? null : (point.Value, normal.Value);
    }

    private sealed record TransportValues(
        string Mode,
        double RelativeTolerance,
        double MaximumFlightTime,
        double SampleInterval,
        CompiledGas Gas,
        CompiledMobility? Mobility,
        CompiledDensityGrid? DensityGrid,
        string SpaceCharge);

    private static TransportValues? ValidateTransport(TransportDocument? transport, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (transport is null)
        {
            errors.Add(Missing("/transport", "a model must declare its transport mode and limits",
                "add a \"transport\" object with \"mode\" and \"maximumFlightTime\""));
            return null;
        }

        if (transport.Mode is not ("trajectory" or "diffusion"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/mode",
                Constraint = "a transport mode is one of 'trajectory' or 'diffusion'",
                Observed = new ObservedValue(0.0, transport.Mode),
                Suggestion = transport.Mode == "statisticalDiffusion"
                    ? "statistical diffusion is spelled \"diffusion\""
                    : "'trajectory' below about 1e-2 mbar, 'diffusion' above about 1e-3",
            });
            return null;
        }

        if (transport.RelativeTolerance is <= 0.0 or > 1e-3 || !double.IsFinite(transport.RelativeTolerance))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/relativeTolerance",
                Constraint = "the relative tolerance must lie in (0, 1e-3]",
                Observed = new ObservedValue(transport.RelativeTolerance, "1"),
                Suggestion = "1e-11 is the default and meets ACC-1 with margin on an analytic mirror",
            });
            return null;
        }

        if (transport.MaximumFlightTime is null)
        {
            errors.Add(Missing("/transport/maximumFlightTime",
                "a flight-time ceiling is required, so a mis-specified model cannot run forever",
                "add {\"value\": 1, \"unit\": \"ms\"}"));
            return null;
        }

        var ceiling = TryQuantity(
            transport.MaximumFlightTime, "/transport/maximumFlightTime", Dimension.TimeDimension, p, errors);

        if (ceiling is null)
        {
            return null;
        }

        if (ceiling.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/maximumFlightTime",
                Constraint = "the flight-time ceiling must be positive",
                Observed = new ObservedValue(transport.MaximumFlightTime.Value, transport.MaximumFlightTime.Unit),
                Suggestion = "supply a positive ceiling, for example {\"value\": 1, \"unit\": \"ms\"}",
            });
            return null;
        }

        var sample = ceiling.Value.SiValue / 2000.0;

        if (transport.SampleInterval is not null)
        {
            var declared = TryQuantity(
                transport.SampleInterval, "/transport/sampleInterval", Dimension.TimeDimension, p, errors);

            if (declared is null)
            {
                return null;
            }

            if (declared.Value.SiValue <= 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/transport/sampleInterval",
                    Constraint = "the trajectory sampling interval must be positive",
                    Observed = new ObservedValue(transport.SampleInterval.Value, transport.SampleInterval.Unit),
                    Suggestion = "omit it to sample the flight at about two thousand points",
                });
                return null;
            }

            sample = declared.Value.SiValue;
        }

        var gas = ValidateGas(transport.Gas, p, errors);

        if (gas is null)
        {
            return null;
        }

        var mobility = ValidateMobility(transport, gas, p, errors);
        var densityGrid = ValidateDensityGrid(transport.DensityGrid, p, errors);

        if (transport.Mode == "diffusion")
        {
            if (!gas.IsPresent)
            {
                errors.Add(Missing("/transport/gas",
                    "the diffusive mode describes ions moving through a gas, so there has to be one",
                    "add a gas block, or use \"mode\": \"trajectory\" for vacuum"));
                return null;
            }

            if (mobility is null)
            {
                errors.Add(Missing("/transport/mobility",
                    "the diffusive mode needs a mobility, and the gas declares no cross section "
                    + "to derive one from",
                    "add {\"zeroField\": {\"value\": 2.0, \"unit\": \"cm^2/(V s)\"}}, or give the "
                    + "gas a crossSection"));
                return null;
            }
        }

        if (transport.SpaceCharge is not ("none" or "direct"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/spaceCharge",
                Constraint = "space charge is modelled by one of the methods this build has",
                Observed = new ObservedValue(0.0, transport.SpaceCharge),
                Suggestion = "\"none\" flies each ion through a field that does not know the others "
                    + "exist; \"direct\" sums every pair, which is the reference method and costs "
                    + "the square of the trajectory count",
            });

            return null;
        }

        return new TransportValues(
            transport.Mode, transport.RelativeTolerance, ceiling.Value.SiValue, sample,
            gas, mobility, densityGrid, transport.SpaceCharge);
    }

    /// <summary>
    /// Refuses a space-charge model that has nothing to compute or cannot be run.
    /// </summary>
    /// <remarks>
    /// Three ways to ask for the mutual force and not get it, all of which would
    /// otherwise run and report a number: a single trajectory has nobody to push
    /// on; a packet with no spatial extent has an unbounded self-field rather than
    /// a large one; and the packet integrator has no collision hook, so a declared
    /// gas would be silently dropped. Refusing is better than any of the three,
    /// because each would produce a result that looks like the one asked for.
    /// </remarks>
    private static void ValidateSpaceChargeIsComputable(CompiledModel model, List<EinzelError> errors)
    {
        if (!model.ModelsSpaceCharge)
        {
            return;
        }

        if (model.Cloud.Ions < 2)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/spaceCharge",
                Constraint = "the mutual force needs at least two trajectories to act between",
                Observed = new ObservedValue(model.Cloud.Ions, "1"),
                Suggestion = "declare a source cloud with \"ions\" of 2 or more, or set "
                    + "\"spaceCharge\": \"none\"",
            });
        }

        var extent = (model.Cloud.TransverseSpreadM * model.Cloud.TransverseSpreadM * 2.0)
            + (model.Cloud.LongitudinalSpreadM * model.Cloud.LongitudinalSpreadM);

        if (extent <= 0.0 && model.Cloud.Ions >= 2)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/cloud",
                Constraint = "a packet at a single point has an unbounded self-field, not a large one",
                Observed = new ObservedValue(0.0, "m"),
                Suggestion = "give the cloud a transverseSpread or a longitudinalSpread",
            });
        }

        if (model.Gas.IsPresent)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.RegimeInvalid,
                Path = "/transport/spaceCharge",
                Constraint = "the direct space-charge method advances the whole packet in lockstep and "
                    + "has no collision hook, so a declared gas would take no part in the run",
                Observed = new ObservedValue(model.Gas.PressureSi, "Pa"),
                Suggestion = "remove the gas, or set \"spaceCharge\": \"none\" and read the screening "
                    + "estimate the run reports instead",
            });
        }
    }

    /// <summary>Validates the declared mobility, or derives one from the gas.</summary>
    /// <remarks>
    /// TRN-1 wants it declared. Deriving it from a cross section is offered because a
    /// model that already declares one for the event-driven mode should not have to
    /// declare a second independent number to run the diffusive one - and because the
    /// two modes then describe the same gas, which is what REG-3's comparison needs
    /// to mean anything. The derivation is marked, so a result computed from it can
    /// say so.
    /// </remarks>
    private static CompiledMobility? ValidateMobility(
        TransportDocument transport,
        CompiledGas gas,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (transport.Mobility?.ZeroField is null)
        {
            // Mason-Schamp, from the cross section, when there is one.
            if (!gas.IsPresent || gas.CrossSectionSi <= 0.0)
            {
                return null;
            }

            return new CompiledMobility(0.0, 0.0, 50.0, Derived: true);
        }

        var declared = transport.Mobility;

        var zeroField = TryQuantity(
            declared.ZeroField, "/transport/mobility/zeroField", Dimension.Mobility, p, errors);

        if (zeroField is null)
        {
            return null;
        }

        if (zeroField.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/mobility/zeroField",
                Constraint = "a mobility must be positive",
                Suggestion = "an ion drifts along the field, not against it; the charge sign is "
                    + "carried by the ion rather than by the mobility",
            });
            return null;
        }

        return new CompiledMobility(
            zeroField.Value.SiValue, declared.Alpha, declared.ValidToTownsend, Derived: false);
    }

    /// <summary>Validates the declared density grid, if there is one.</summary>
    private static CompiledDensityGrid? ValidateDensityGrid(
        DensityGridDocument? grid, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (grid is null)
        {
            return null;
        }

        var minX = TryQuantity(grid.MinX, "/transport/densityGrid/minX", Dimension.LengthDimension, p, errors);
        var minY = TryQuantity(grid.MinY, "/transport/densityGrid/minY", Dimension.LengthDimension, p, errors);
        var maxX = TryQuantity(grid.MaxX, "/transport/densityGrid/maxX", Dimension.LengthDimension, p, errors);
        var maxY = TryQuantity(grid.MaxY, "/transport/densityGrid/maxY", Dimension.LengthDimension, p, errors);

        if (minX is null || minY is null || maxX is null || maxY is null)
        {
            return null;
        }

        if (maxX.Value.SiValue <= minX.Value.SiValue || maxY.Value.SiValue <= minY.Value.SiValue)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/densityGrid",
                Constraint = "a density grid needs a positive extent in both directions",
                Suggestion = "check that maxX exceeds minX and maxY exceeds minY",
            });
            return null;
        }

        return new CompiledDensityGrid(
            minX.Value.SiValue, minY.Value.SiValue, maxX.Value.SiValue, maxY.Value.SiValue,
            Math.Max(4, grid.IntervalsX), Math.Max(4, grid.IntervalsY));
    }

    /// <summary>Validates the background gas, if one is declared.</summary>
    /// <remarks>
    /// Every field a model may omit has a defensible default except the ones that
    /// decide the physics. A gas with a model and no pressure, or a hard-sphere
    /// model and no cross section, is refused rather than silently treated as
    /// vacuum - a run that quietly ignores a declared gas is the failure that
    /// looks most like success.
    /// </remarks>
    private static CompiledGas? ValidateGas(
        GasDocument? gas, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (gas is null || gas.Model == "none")
        {
            return CompiledGas.Vacuum;
        }

        if (gas.Model is not ("hardSphere" or "langevin"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/gas/model",
                Constraint = "a collision model is one of 'none', 'hardSphere', or 'langevin'",
                Observed = new ObservedValue(0.0, gas.Model),
                Suggestion = "'hardSphere' below about 1e-5 mbar, 'langevin' from there to about "
                    + "1e-2 mbar; the mobility description above that is not built",
            });
            return null;
        }

        var pressure = Required(
            gas.Pressure, "/transport/gas/pressure", Dimension.Pressure, p, errors,
            "a gas must declare its pressure", "add {\"value\": 1e-6, \"unit\": \"mbar\"}");

        var mass = Required(
            gas.Mass, "/transport/gas/mass", Dimension.MassDimension, p, errors,
            "a gas must declare the mass of one neutral",
            "add {\"value\": 28.0134, \"unit\": \"Da\"} for nitrogen");

        var temperature = gas.Temperature is null
            ? Quantity.Si(300.0, Dimension.TemperatureDimension)
            : TryQuantity(gas.Temperature, "/transport/gas/temperature",
                Dimension.TemperatureDimension, p, errors);

        var crossSection = gas.CrossSection is null
            ? Quantity.Si(0.0, Dimension.Area)
            : TryQuantity(gas.CrossSection, "/transport/gas/crossSection", Dimension.Area, p, errors);

        var polarizability = gas.Polarizability is null
            ? Quantity.Si(0.0, Dimension.Volume)
            : TryQuantity(gas.Polarizability, "/transport/gas/polarizability", Dimension.Volume, p, errors);

        if (pressure is null || mass is null || temperature is null
            || crossSection is null || polarizability is null)
        {
            return null;
        }

        if (pressure.Value.SiValue <= 0.0 || temperature.Value.SiValue <= 0.0 || mass.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/gas",
                Constraint = "a gas needs a positive pressure, temperature, and neutral mass",
                Suggestion = "set \"model\": \"none\" for vacuum rather than a pressure of zero",
            });
            return null;
        }

        if (gas.Model == "hardSphere" && crossSection.Value.SiValue <= 0.0)
        {
            errors.Add(Missing("/transport/gas/crossSection",
                "the hard-sphere model needs a collision cross section",
                "add {\"value\": 250, \"unit\": \"Å^2\"}, which is a mid-size peptide in nitrogen"));
            return null;
        }

        if (gas.Model == "langevin" && polarizability.Value.SiValue <= 0.0)
        {
            errors.Add(Missing("/transport/gas/polarizability",
                "the Langevin model needs the neutral's polarizability volume",
                "add {\"value\": 1.74, \"unit\": \"Å^3\"} for nitrogen"));
            return null;
        }

        var drift = gas.DriftVelocity is null
            ? Vec3.Zero
            : TryVector(gas.DriftVelocity, "/transport/gas/driftVelocity", Dimension.Velocity, p, errors)
                ?? Vec3.Zero;

        return new CompiledGas
        {
            Model = gas.Model,
            PressureSi = pressure.Value.SiValue,
            TemperatureK = temperature.Value.SiValue,
            MassSi = mass.Value.SiValue,
            CrossSectionSi = crossSection.Value.SiValue,
            PolarizabilitySi = polarizability.Value.SiValue,
            DriftVelocitySi = drift,
            Seed = gas.Seed,
        };
    }

    private static Quantity? Required(
        QuantityValue? value,
        string path,
        Dimension dimension,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        string constraint,
        string suggestion)
    {
        if (value is null)
        {
            errors.Add(Missing(path, constraint, suggestion));
            return null;
        }

        return TryQuantity(value, path, dimension, p, errors);
    }

    /// <summary>
    /// Refuses a source that starts on or inside a conductor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GRD-4: validity is checked, not assumed. This is knowable from the declared
    /// geometry alone - an electrode's signed distance is arithmetic on the numbers
    /// in the document - so there is no reason to leave it to the integrator, which
    /// finds out by absorbing the ion at step zero.
    /// </para>
    /// <para>
    /// Left to the integrator it produced the worst shape of answer this project
    /// has: <c>validate</c> said OK and exit 0, <c>solve</c> said converged and
    /// exit 0, and only <c>run</c> objected. An agent asked to produce a model that
    /// validates and solves would have shipped one whose ion dies immediately and
    /// had two clean bills of health saying otherwise.
    /// </para>
    /// <para>
    /// Found by an agent attempting the acceptance suite, on the shipped quadrupole
    /// template, by changing the one parameter that template exists to have changed.
    /// </para>
    /// </remarks>
    private static void ValidateSourceIsNotInsideMetal(CompiledModel model, List<EinzelError> errors)
    {
        var source = model.SourcePosition;

        foreach (var element in model.Fields)
        {
            foreach (var electrode in element.Solve?.Electrodes ?? [])
            {
                if (!electrode.Contains(source.X, source.Y))
                {
                    continue;
                }

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/source/position",
                    Constraint =
                        $"the source starts inside electrode '{electrode.Name}', so the ion is "
                        + "absorbed before it moves",
                    Observed = new ObservedValue(source.X, "m"),
                    Suggestion = "move the source into the space the ions fly through. If this "
                        + "model came from a template, check whether a placement is written as a "
                        + "bare length while the geometry around it is parametric - changing the "
                        + "geometry then moves the metal and leaves the source behind",
                });

                return;
            }

            foreach (var electrode in element.Solve3D?.Electrodes ?? [])
            {
                if (!electrode.Contains(source.X, source.Y, source.Z))
                {
                    continue;
                }

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/source/position",
                    Constraint =
                        $"the source starts inside electrode '{electrode.Name}', so the ion is "
                        + "absorbed before it moves",
                    Observed = new ObservedValue(source.X, "m"),
                    Suggestion = "move the source into the space the ions fly through",
                });

                return;
            }
        }
    }

    private static void ValidateGeometryConsistency(CompiledModel model, List<EinzelError> errors)
    {
        ValidateSourceIsNotInsideMetal(model, errors);
        ValidateSpaceChargeIsComputable(model, errors);

        // GRD-4: validity is checked, not assumed. An ion launched on the wrong
        // side of its own detector never flies, and the resulting zero flight time
        // looks like a physics answer rather than a geometry mistake.
        var signedDistance = Vec3.Dot(model.SourcePosition - model.DetectorPoint, model.DetectorNormal);

        if (signedDistance < 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/position",
                Constraint = "the source must start on the positive side of the detector plane",
                Observed = new ObservedValue(signedDistance, "m"),
                Suggestion = "reverse /detector/normal, or move the source to the other side of the plane",
            });
        }

        if (Vec3.Dot(model.SourceDirection, model.DetectorNormal) > 0.0 && signedDistance >= 0.0)
        {
            // Launching directly away from the detector is legal — a reflectron
            // does exactly that — but only if something turns the ion around.
            var hasMirror = CanAccelerate(model.Fields);

            if (!hasMirror)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/source/direction",
                    Constraint =
                        "the ion is launched away from the detector and no field element can turn it around",
                    Observed = new ObservedValue(Vec3.Dot(model.SourceDirection, model.DetectorNormal), "1"),
                    Suggestion = "reverse /source/direction, or add a retarding field element",
                });
            }
        }
    }

    private static Quantity? TryQuantity(
        QuantityValue? value, string path, Dimension expected,
        IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (value is null)
        {
            errors.Add(Missing(path, $"a quantity of dimension {expected} is required here",
                "supply {\"value\": ..., \"unit\": \"...\"}"));
            return null;
        }

        try
        {
            return value.ToQuantity(path, expected, p);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error);
            return null;
        }
    }

    private static Vec3? TryVector(
        VectorValue? value, string path, Dimension expected,
        IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (value is null)
        {
            errors.Add(Missing(path, $"a vector of dimension {expected} is required here",
                "supply {\"value\": [x, y, z], \"unit\": \"...\"}"));
            return null;
        }

        try
        {
            return value.ToVec3(path, expected, p);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error);
            return null;
        }
    }

    private static Vec3? TryDirection(DirectionValue? value, string path, List<EinzelError> errors)
    {
        if (value is null)
        {
            errors.Add(Missing(path, "a direction is required here", "supply {\"value\": [1, 0, 0]}"));
            return null;
        }

        try
        {
            return value.ToUnitVector(path);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error);
            return null;
        }
    }

    private static EinzelError Missing(string path, string constraint, string suggestion) => new()
    {
        Code = ErrorCodes.SchemaInvalid,
        Path = path,
        Constraint = constraint,
        Suggestion = suggestion,
    };
}
