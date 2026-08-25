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
        var fields = ValidateFields(document.Fields, p, errors);

        // Passed in because whether a source may start at rest depends on whether
        // anything else can accelerate it. A beam carries its own energy; a
        // trapped packet is accelerated by the instrument.
        var source = ValidateSource(document.Source, p, errors, CanAccelerate(fields));
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
    /// Field-free space cannot, and a model with nothing else is the one case where
    /// a source at rest is genuinely a mistake rather than a pulsed extraction.
    /// </remarks>
    private static bool CanAccelerate(IReadOnlyList<CompiledField>? fields) =>
        fields is not null && fields.Any(f => f.Kind != CompiledFieldKind.FieldFree);

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
        IReadOnlyList<FieldDocument>? fields, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
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
            var element = CompileField(fields[i], $"/fields/{i}", p, errors);

            if (element is not null)
            {
                compiled.Add(element);
            }
        }

        return compiled;
    }

    private static CompiledField? CompileField(FieldDocument field, string path, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
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
                return CompileSolvedField(field.Solve, $"{path}/solve", p, errors);

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

    private static CompiledField? CompileSolvedField(
        SolvedFieldDocument? solve, string path, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
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
            var electrode = CompileElectrode(solve.Electrodes[i], $"{path}/electrodes/{i}", p, errors);

            if (electrode is not null)
            {
                electrodes.Add(electrode);
            }
        }

        var reflect = solve.ReflectAboutX is null
            ? (double?)null
            : TryQuantity(solve.ReflectAboutX, $"{path}/reflectAboutX", length, p, errors)?.SiValue;

        var left = Boundary(solve.LeftEdge, $"{path}/leftEdge", errors);
        var right = Boundary(solve.RightEdge, $"{path}/rightEdge", errors);
        var bottom = Boundary(solve.BottomEdge, $"{path}/bottomEdge", errors);
        var top = Boundary(solve.TopEdge, $"{path}/topEdge", errors);

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

                return minX is null || minY is null || maxX is null || maxY is null || potential is null
                    ? null
                    : new CompiledElectrode
                    {
                        Name = name,
                        Shape = ElectrodeShape.Rectangle,
                        MinX = minX.Value.SiValue,
                        MinY = minY.Value.SiValue,
                        MaxX = maxX.Value.SiValue,
                        MaxY = maxY.Value.SiValue,
                        Potential = potential.Value.SiValue,
                    };
            }

            case "disc":
            {
                var centreX = TryQuantity(electrode.CentreX, $"{path}/centreX", length, p, errors);
                var centreY = TryQuantity(electrode.CentreY, $"{path}/centreY", length, p, errors);
                var radius = TryQuantity(electrode.Radius, $"{path}/radius", length, p, errors);
                var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);

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
        string Mode, double RelativeTolerance, double MaximumFlightTime, double SampleInterval);

    private static TransportValues? ValidateTransport(TransportDocument? transport, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (transport is null)
        {
            errors.Add(Missing("/transport", "a model must declare its transport mode and limits",
                "add a \"transport\" object with \"mode\" and \"maximumFlightTime\""));
            return null;
        }

        if (transport.Mode != "trajectory")
        {
            errors.Add(new EinzelError
            {
                Code = transport.Mode == "statisticalDiffusion" ? ErrorCodes.RegimeInvalid : ErrorCodes.SchemaInvalid,
                Path = "/transport/mode",
                Constraint = "this build implements the 'trajectory' transport mode only",
                Observed = new ObservedValue(0.0, transport.Mode),
                Suggestion = transport.Mode == "statisticalDiffusion"
                    ? "statistical diffusion arrives with the pressure regime; use 'trajectory' below about 1e-2 mbar"
                    : "use \"mode\": \"trajectory\"",
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

        return new TransportValues(transport.Mode, transport.RelativeTolerance, ceiling.Value.SiValue, sample);
    }

    private static void ValidateGeometryConsistency(CompiledModel model, List<EinzelError> errors)
    {
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
            var hasMirror = model.Fields.Any(f => f.Kind != CompiledFieldKind.FieldFree);

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
