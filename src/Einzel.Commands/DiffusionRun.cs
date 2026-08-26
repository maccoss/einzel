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

        Absorb(model, grid, density);

        var edges = EdgesFor(model, grid);

        var result = DriftDiffusion.Run(
            density, field, gas, mobility, species, model.MaximumFlightTimeSi, edges);

        var warnings = new List<ValidityWarning>(fieldWarnings);

        warnings.AddRange(RegimeWarnings(gas, mobility, field, grid, declared));

        return new DiffusiveOutcome(result, used, grid, launched, warnings);
    }

    /// <summary>The grid the density is tracked on.</summary>
    /// <remarks>
    /// The declared one, or the solved field's own domain, which is nearly always
    /// what is wanted and is the only choice an analytic model cannot make for
    /// itself. A model with neither is refused rather than given a guessed box: a
    /// density tracked over the wrong region loses its ions to a boundary that is
    /// not there in the instrument.
    /// </remarks>
    private static Grid2D GridFor(CompiledModel model)
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

    /// <summary>Empties the cells inside conductors, so an electrode absorbs.</summary>
    /// <remarks>
    /// ACC-5 wants transmission itemised by loss surface, and in this description a
    /// loss is a cell that stopped holding ions rather than an ion that stopped
    /// moving. Zeroing the interior each step would be the full treatment; zeroing
    /// the seed is what stops a source placed inside metal from starting there,
    /// which is the case that reads as an instrument losing everything.
    /// </remarks>
    private static void Absorb(CompiledModel model, Grid2D grid, DensityField density)
    {
        foreach (var element in model.Fields)
        {
            foreach (var electrode in element.Solve?.Electrodes ?? [])
            {
                for (var j = 0; j < grid.CountY; j++)
                {
                    for (var i = 0; i < grid.CountX; i++)
                    {
                        if (electrode.Contains(grid.X(i), grid.Y(j)))
                        {
                            density[i, j] = 0.0;
                        }
                    }
                }
            }
        }
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
