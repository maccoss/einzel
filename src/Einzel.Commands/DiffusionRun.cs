using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

namespace Einzel.Commands;

/// <summary>What a diffusive run produced.</summary>
/// <param name="Result">The raw solver outcome.</param>
/// <param name="Mobility">The mobility used, and whether it was declared or derived.</param>
/// <param name="Grid">The grid the density was tracked on.</param>
/// <param name="Launched">Ions in the initial density.</param>
/// <param name="Warnings">Warnings the result carries.</param>
public sealed record DiffusiveOutcome(
    DiffusionResult Result,
    CompiledMobility Mobility,
    Grid2D Grid,
    double Launched,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>
/// Turns a model document into a density problem and runs it.
/// </summary>
/// <remarks>
/// <para>
/// The wiring REG-1's second mode needed and did not have: a source becomes an
/// initial density, a detector becomes a collecting boundary, and an electrode
/// becomes a region ions are absorbed in. None of that is physics - the solver was
/// already validated - but without it the mode was reachable from code and not from
/// a model file, which is the difference between a capability and a feature.
/// </para>
/// <para>
/// Two dimensions only. A diffusive run is a time-stepped solve over a whole grid
/// rather than one path through it, so the third dimension costs the cube rather
/// than the square - and the devices this mode is for, funnels and stacked rings,
/// are axisymmetric anyway.
/// </para>
/// </remarks>
public static class DiffusionRun
{
    /// <summary>Runs a model in the diffusive mode.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="field">The field, already built.</param>
    /// <param name="fieldWarnings">Warnings the field carries.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">The model cannot be expressed as a density problem.</exception>
    public static DiffusiveOutcome Execute(
        CompiledModel model, IElectrostaticField field, IReadOnlyList<ValidityWarning> fieldWarnings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(field);

        var species = IonSpecies.FromModel(model);
        var gas = BackgroundGas.FromModel(model.Gas);

        var declared = model.Mobility
            ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/mobility",
                Constraint = "the diffusive mode needs a mobility",
                Suggestion = "declare one, or give the gas a cross section to derive it from",
            });

        // A derived mobility is computed here rather than in validation, because
        // Mason-Schamp needs the ion and validation does not have it assembled yet.
        var mobility = declared.Derived
            ? Mobility.FromCrossSection(gas, species)
            : new Mobility(declared.ZeroFieldSi, declared.Alpha, declared.ValidToTownsend);

        var used = declared with { ZeroFieldSi = mobility.ZeroFieldSi };

        var grid = GridFor(model);
        var cylindrical = model.Fields.Any(f => f.Solve?.Symmetry == SolveSymmetry.Cylindrical);

        var density = Seed(model, grid, cylindrical);
        var launched = density.Population();

        var (absorbers, seedLoss) = Absorb(model, grid, density);

        var edges = EdgesFor(model, grid);

        var warnings = new List<ValidityWarning>(fieldWarnings);

        // A driven structure has no static field to step a density through, and
        // sampling one at a chosen instant gives the RF at that phase - a field that
        // exists for no length of time. What a slow ion in a gas experiences is the
        // cycle average, so the driven field is presented as its effective one.
        //
        // This is what the 1e-2 to 10 mbar band needed. Trajectory integration is
        // outside its validity there and this mode could not see a drive at all, so
        // an ion funnel or a travelling-wave guide - which is to say the devices
        // that actually run at those pressures - had no mode that described them.
        var effective = field as Transport.Diffusion.PonderomotiveField;

        if (field is ITimeVaryingField driven)
        {
            var rate = Transport.Diffusion.PonderomotiveField.CollisionRateFromMobility(
                species.ChargeSi, species.MassSi, mobility.ZeroFieldSi);

            effective = new Transport.Diffusion.PonderomotiveField(
                driven, species.ChargeSi, species.MassSi, rate);

            field = effective;
        }

        var result = DriftDiffusion.Run(
            density, field, gas, mobility, species, model.MaximumFlightTimeSi, edges, absorbers);

        // The seed's overlap with metal joins the same ledger the run fills, so the
        // itemisation adds back up to the launched population.
        if (seedLoss.Count > 0)
        {
            var merged = new Dictionary<string, double>(result.Lost, StringComparer.Ordinal);

            foreach (var (where, ions) in seedLoss)
            {
                merged[where] = merged.GetValueOrDefault(where) + ions;
            }

            result = result with { Lost = merged };
        }

        warnings.AddRange(RegimeWarnings(gas, mobility, field, grid, declared));

        if (effective is not null)
        {
            warnings.AddRange(EffectiveFieldWarnings(effective, grid));
        }

        return new DiffusiveOutcome(result, used, grid, launched, warnings);
    }

    /// <summary>The grid the density is tracked on.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The grid.</returns>
    /// <exception cref="EinzelException">There is no region to track a density over.</exception>
    /// <remarks>
    /// The declared one, or the solved field's own domain, which is nearly always
    /// what is wanted and is the only choice an analytic model cannot make for
    /// itself. A model with neither is refused rather than given a guessed box: a
    /// density tracked over the wrong region loses its ions to a boundary that is
    /// not there in the instrument.
    /// </remarks>
    public static Grid2D GridFor(CompiledModel model)
    {
        if (model.DensityGrid is { } declared)
        {
            return Grid2D.OverBox(
                declared.MinX, declared.MinY, declared.MaxX, declared.MaxY,
                RoundUp(declared.IntervalsX), RoundUp(declared.IntervalsY));
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } solve)
            {
                return Grid2D.OverBox(solve.MinX, solve.MinY, solve.MaxX, solve.MaxY, 256, 128);
            }
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/transport/densityGrid",
            Constraint = "there is no region to track a density over: the model declares no "
                + "solved2d field to take a domain from, and no densityGrid of its own",
            Suggestion = "add a densityGrid with minX, minY, maxX and maxY",
        });
    }

    private static int RoundUp(int intervals)
    {
        var count = 4;

        while (count < intervals)
        {
            count *= 2;
        }

        return count;
    }

    /// <summary>
    /// The source cloud as an initial density.
    /// </summary>
    /// <remarks>
    /// A Gaussian at the source position with the cloud's declared spreads, holding
    /// the declared population. A cloud with no spread becomes one cell, which is
    /// the honest translation - the model said the ions start at a point, and the
    /// cell is as close to a point as the grid can express.
    /// </remarks>
    private static DensityField Seed(CompiledModel model, Grid2D grid, bool cylindrical)
    {
        var density = new DensityField(grid, cylindrical);

        var population = model.Cloud.Population ?? Math.Max(1, model.Cloud.Ions);

        var centreX = model.SourcePosition.X;
        var centreY = model.SourcePosition.Y;

        // The spreads are declared transverse and longitudinal to the beam, and the
        // beam here runs along whichever axis the source direction mostly does.
        var alongX = Math.Abs(model.SourceDirection.X) >= Math.Abs(model.SourceDirection.Y);

        var spreadX = alongX ? model.Cloud.LongitudinalSpreadM : model.Cloud.TransverseSpreadM;
        var spreadY = alongX ? model.Cloud.TransverseSpreadM : model.Cloud.LongitudinalSpreadM;

        spreadX = Math.Max(spreadX, 0.5 * grid.SpacingX);
        spreadY = Math.Max(spreadY, 0.5 * grid.SpacingY);

        var total = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            var dy = (grid.Y(j) - centreY) / spreadY;

            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = (grid.X(i) - centreX) / spreadX;
                var weight = Math.Exp(-0.5 * ((dx * dx) + (dy * dy)));

                density[i, j] = weight;
                total += weight * density.CellVolume(j);
            }
        }

        if (total <= 0.0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/position",
                Constraint = "the source is outside the region the density is tracked over",
                Suggestion = "move the source inside the density grid, or widen the grid",
            });
        }

        // Normalised so the field holds exactly the declared population, whatever
        // the grid did to the Gaussian at its edges.
        var scale = population / total;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                density[i, j] *= scale;
            }
        }

        return density;
    }

    /// <summary>
    /// The conductors, as cells that keep absorbing, plus whatever the seed already
    /// had inside them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ACC-5 wants transmission itemised by loss surface, and in this description a
    /// loss is density that flowed into metal rather than an ion that stopped moving.
    /// The mask is built once and handed to the solver, which empties those cells at
    /// every step - so an electrode is a boundary for the whole run rather than only
    /// for the seed. Before this, a funnel's rings shaped the field and then let the
    /// density pass straight through them, which made every diffusive transmission
    /// figure an upper bound with nothing saying so.
    /// </para>
    /// <para>
    /// The seed's own overlap is returned rather than discarded. It used to be
    /// silently deleted after the launched population had already been counted, so
    /// launched, collected, remaining and the named losses did not add up - and an
    /// itemisation that does not add up is worse than none, because it reads as
    /// complete.
    /// </para>
    /// </remarks>
    private static (AbsorbingCells Cells, IReadOnlyDictionary<string, double> SeedLoss) Absorb(
        CompiledModel model, Grid2D grid, DensityField density)
    {
        var names = new List<string>();
        var owner = new int[grid.CountX * grid.CountY];

        Array.Fill(owner, -1);

        var seedLoss = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var element in model.Fields)
        {
            foreach (var electrode in element.Solve?.Electrodes ?? [])
            {
                var index = names.Count;
                var claimed = false;

                for (var j = 0; j < grid.CountY; j++)
                {
                    var volume = density.CellVolume(j);

                    for (var i = 0; i < grid.CountX; i++)
                    {
                        if (!electrode.Contains(grid.X(i), grid.Y(j)))
                        {
                            continue;
                        }

                        var k = (j * grid.CountX) + i;

                        // First claim wins, so two overlapping electrodes do not
                        // both bill for the same ions. Which one is arbitrary and
                        // does not matter: the cell absorbs either way, and the
                        // total is right.
                        if (owner[k] >= 0)
                        {
                            continue;
                        }

                        owner[k] = index;
                        claimed = true;

                        if (density[i, j] > 0.0)
                        {
                            seedLoss[electrode.Name] =
                                seedLoss.GetValueOrDefault(electrode.Name)
                                + (density[i, j] * volume);

                            density[i, j] = 0.0;
                        }
                    }
                }

                if (claimed)
                {
                    names.Add(electrode.Name);
                }
            }
        }

        return names.Count > 0
            ? (new AbsorbingCells(owner, names), seedLoss)
            : (AbsorbingCells.None, seedLoss);
    }

    /// <summary>
    /// What happens at each edge, from where the detector is.
    /// </summary>
    /// <remarks>
    /// The edge the detector faces collects; a cylindrical solve reflects at its
    /// axis, because there is nowhere for an ion on the axis to go; everything else
    /// absorbs. Reflecting where the instrument has a wall would make ions bounce
    /// off vacuum.
    /// </remarks>
    private static DriftDiffusion.DomainEdges EdgesFor(CompiledModel model, Grid2D grid)
    {
        var cylindrical = model.Fields.Any(f => f.Solve?.Symmetry == SolveSymmetry.Cylindrical);

        var axis = cylindrical || grid.OriginY >= 0.0 ? Escape.Reflecting : Escape.Absorbing;

        var normal = model.DetectorNormal;

        // The detector normal points back toward the source, so the edge it faces is
        // the one in the opposite direction.
        if (Math.Abs(normal.X) >= Math.Abs(normal.Y))
        {
            return normal.X < 0.0
                ? new DriftDiffusion.DomainEdges(Escape.Absorbing, Escape.Collecting, axis, Escape.Absorbing)
                : new DriftDiffusion.DomainEdges(Escape.Collecting, Escape.Absorbing, axis, Escape.Absorbing);
        }

        return normal.Y < 0.0
            ? new DriftDiffusion.DomainEdges(Escape.Absorbing, Escape.Absorbing, axis, Escape.Collecting)
            : new DriftDiffusion.DomainEdges(Escape.Absorbing, Escape.Absorbing, Escape.Collecting, Escape.Absorbing);
    }

    /// <summary>
    /// What the effective-potential approximation is doing, and where it stops
    /// describing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both reported whether or not they cross a threshold, per REG-2: a reader who
    /// sees a suppression of 0.98 knows the question was asked, and one who sees
    /// nothing cannot tell that from its not having been asked.
    /// </para>
    /// <para>
    /// The suppression is the interesting one. Every textbook writes the
    /// pseudopotential as q^2 E0^2 / (4 m Omega^2), and at the pressures these
    /// devices run at that is an overestimate by the factor reported here - the
    /// quiver is damped, so the round trip through the field gradient leaves less
    /// net force. Quoting the collisionless well for a funnel at a few mbar is a
    /// mistake this makes visible rather than one it commits.
    /// </para>
    /// </remarks>
    private static List<ValidityWarning> EffectiveFieldWarnings(
        Transport.Diffusion.PonderomotiveField field, Grid2D grid)
    {
        var warnings = new List<ValidityWarning>();

        // The worst quiver anywhere on the grid, against the cell it sits in. The
        // effective potential averages over that excursion and only describes
        // something if the field is roughly linear across it.
        var worst = 0.0;

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                worst = Math.Max(
                    worst, field.QuiverAmplitude(new Vec3(grid.X(i), grid.Y(j), 0.0)));
            }
        }

        var cell = Math.Min(grid.SpacingX, grid.SpacingY);

        warnings.Add(new ValidityWarning(
            "rf.effective-potential",
            $"the drive is modelled as an effective potential: the cycle-averaged well a slow ion "
            + $"feels, not the field at any instant. Collisions damp the quiver and weaken that well "
            + $"by a factor of {field.Suppression:G4} against the collisionless "
            + $"q^2 E^2 / (4 m Omega^2) that is usually quoted, at a momentum-transfer rate of "
            + $"{field.CollisionRateSi:G4} /s against a drive of {field.AngularFrequencySi:G4} rad/s. "
            + $"The largest quiver on this grid is {worst * 1e3:G3} mm, against a cell of "
            + $"{cell * 1e3:G3} mm",
            WarningSeverity.Provenance));

        if (worst > cell)
        {
            warnings.Add(new ValidityWarning(
                "rf.quiver-exceeds-mesh",
                $"the ion is swept {worst * 1e3:G3} mm back and forth by the drive, which is further "
                + $"than the {cell * 1e3:G3} mm cell the effective potential is resolved on. Averaging "
                + "over an excursion only describes something if the field is roughly linear across "
                + "it, and here the excursion is larger than the mesh that represents the field. "
                + "Refine the density grid, or raise the drive frequency, or accept that the ion's "
                + "real motion is the whole story rather than a wobble about a drift",
                WarningSeverity.ValidityViolation));
        }

        return warnings;
    }

    /// <summary>
    /// What the neutral gas is doing, reported whether or not it is doing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GAS-1 asks a gas region for a bulk velocity field, and spec figure 4 makes it
    /// required rather than optional above about 1e-2 mbar. The specification is
    /// unusually direct about why: the field "is easy to omit and hard to notice
    /// missing: at funnel pressures the neutral jet off the inlet capillary drags
    /// ions and frequently dominates the axial DC gradient".
    /// </para>
    /// <para>
    /// So both cases are named. A model that declares a flow gets the number that
    /// says whether the gas or the field is carrying its ions; one that declares
    /// none, at a pressure where the specification says it should have, gets told -
    /// because a stationary gas is a modelling choice and it does not look like one
    /// in the output. It is exactly REG-2's argument: a reader who sees the ratio
    /// knows the question was asked, and one who sees nothing cannot tell that from
    /// its not having been asked.
    /// </para>
    /// </remarks>
    private static List<ValidityWarning> FlowWarnings(
        BackgroundGas gas, Mobility mobility, double strongestFieldSi, double pressureMbar)
    {
        var warnings = new List<ValidityWarning>();

        // The fastest the field can push an ion anywhere on this grid, which is what
        // a bulk gas speed has to be read against.
        var drift = mobility.ZeroFieldSi * strongestFieldSi;

        if (gas.IsFlowing)
        {
            var bulk = gas.FastestBulkSpeedSi;

            warnings.Add(new ValidityWarning(
                "gas.flow",
                $"the neutral gas moves at up to {bulk:G4} m/s, against a field drift of at most "
                + $"{drift:G4} m/s on this grid"
                + (drift > 0.0
                    ? $" - a ratio of {bulk / drift:G3}. "
                        + (bulk > drift
                            ? "The gas is carrying these ions, not the field"
                            : "The field dominates, and the flow is a correction")
                    : ". There is no field here, so the flow is the whole transport"),
                WarningSeverity.Provenance));

            return warnings;
        }

        if (pressureMbar > Transport.Collisions.RegimeDiagnostics.DiffusiveMbar)
        {
            warnings.Add(new ValidityWarning(
                "gas.stationary-above-flow-threshold",
                $"at {pressureMbar:G3} mbar this model's gas is standing still, and spec figure 4 "
                + $"puts a neutral velocity field among the things a description above "
                + $"{Transport.Collisions.RegimeDiagnostics.DiffusiveMbar:G1} mbar requires rather "
                + "than merely benefits from. At these pressures the jet off an inlet capillary "
                + "frequently dominates the axial DC gradient, so a stationary gas can understate "
                + "the transport badly - a funnel is pushed through by its gas, and this one is "
                + "not being pushed. Declare 'transport.gas.driftVelocity' if the instrument has a "
                + "flow through it",
                WarningSeverity.Qualified));
        }

        return warnings;
    }

    /// <summary>Warnings a diffusive run carries, per REG-2 and TRN-1.</summary>
    private static List<ValidityWarning> RegimeWarnings(
        BackgroundGas gas,
        Mobility mobility,
        IElectrostaticField field,
        Grid2D grid,
        CompiledMobility declared)
    {
        var warnings = new List<ValidityWarning>();

        if (declared.Derived)
        {
            warnings.Add(new ValidityWarning(
                "mobility.derived",
                $"the mobility used, {mobility.ZeroFieldSi:G4} m^2/(V s), was derived from the gas "
                + "cross section by Mason-Schamp rather than declared. It carries the cross "
                + "section's uncertainty plus a first-order Chapman-Enskog approximation, and it "
                + "is field-independent, so it will overstate the drift wherever the ion is "
                + "heated. TRN-1 wants this measured and declared",
                WarningSeverity.Qualified));
        }

        var pressureMbar = gas.PressureSi / 1e2;

        if (pressureMbar < RegimeDiagnostics.OverlapMbar)
        {
            warnings.Add(new ValidityWarning(
                "regime.diffusion-below-validity",
                $"at {pressureMbar:G3} mbar an ion may cross this instrument without colliding at "
                + "all, and a density that diffuses is the wrong description of that. Trajectory "
                + "integration is the mode for this pressure",
                WarningSeverity.ValidityViolation));
        }

        // The worst reduced field anywhere on the grid, which is where a low-field
        // mobility stops describing the ion.
        var worst = 0.0;

        for (var j = 0; j < grid.CountY; j += 4)
        {
            for (var i = 0; i < grid.CountX; i += 4)
            {
                var point = new Vec3(grid.X(i), grid.Y(j), 0.0);
                var electric = field.ElectricFieldAt(in point);

                worst = Math.Max(worst, Math.Sqrt((electric.X * electric.X) + (electric.Y * electric.Y)));
            }
        }

        warnings.AddRange(FlowWarnings(gas, mobility, worst, pressureMbar));

        if (!mobility.IsWithinFit(worst, gas.NumberDensitySi))
        {
            var townsend = worst / (gas.NumberDensitySi * Mobility.Townsend);

            warnings.Add(new ValidityWarning(
                "mobility.outside-fit",
                $"the field reaches {townsend:G3} townsend somewhere on this grid, past the "
                + $"{mobility.ValidToTownsend:G3} the mobility was fitted to. Beyond it the ion is "
                + "heated by the field and drifts more slowly than a thermal mobility says - a "
                + "low-field value overstated the drift by 1.4 times at 166 townsend in this "
                + "engine's own cross-mode check",
                WarningSeverity.ValidityViolation));
        }

        return warnings;
    }
}
