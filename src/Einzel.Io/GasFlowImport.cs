using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Transport.Collisions;

namespace Einzel.Io;

/// <summary>
/// Loads the neutral velocity field a model declares.
/// </summary>
/// <remarks>
/// <para>
/// GAS-1 and §21's "gas velocity import". The path is resolved against the model
/// document's own directory, because that is where a modelling effort keeps the
/// things it references - a project is a directory (PRJ-4), and a path relative to
/// the working directory would mean a different file depending on where the command
/// was run from.
/// </para>
/// <para>
/// This lives in <c>Einzel.Io</c> rather than in <c>Einzel.Transport</c> because it
/// reads a file, and the transport assembly does not. What it returns is an ordinary
/// <see cref="IGasFlow"/> that the solver cannot tell from a uniform one.
/// </para>
/// </remarks>
public static class GasFlowImport
{
    /// <summary>Loads the field a compiled gas names, if it names one.</summary>
    /// <param name="gas">The compiled gas.</param>
    /// <param name="modelDirectory">
    /// The directory the model document lives in, which relative paths resolve
    /// against.
    /// </param>
    /// <returns>The flow, or null when the model declares none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// The file is missing, is not readable ImageData, or holds a scalar where a
    /// vector is needed.
    /// </exception>
    public static SampledGasFlow? Load(CompiledGas gas, string modelDirectory)
    {
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        if (!gas.HasVelocityField)
        {
            return null;
        }

        var path = Path.IsPathRooted(gas.VelocityFieldPath!)
            ? gas.VelocityFieldPath!
            : Path.GetFullPath(Path.Combine(modelDirectory, gas.VelocityFieldPath!));

        if (!File.Exists(path))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/gas/velocityField/path",
                Constraint = $"there is no file at {path}",
                Suggestion = "the path is resolved against the model document's own directory, not "
                    + "the working directory, so that a model means the same thing wherever the "
                    + "command is run from",
            });
        }

        var array = VtkImageData.Read(
            File.ReadAllText(path), gas.VelocityFieldArray, "/transport/gas/velocityField");

        if (array.Components != 3)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/gas/velocityField/array",
                Constraint = $"array '{array.Name}' has {array.Components} component(s), and a "
                    + "velocity needs three",
                Suggestion = "name the vector array explicitly with \"array\": \"...\". A CFD export "
                    + "usually carries several, and the first one in the file is as likely to be a "
                    + "pressure as a velocity",
            });
        }

        return new SampledGasFlow(
            array.CountX,
            array.CountY,
            array.CountZ,
            new Vec3(array.OriginSi.X, array.OriginSi.Y, array.OriginSi.Z),
            new Vec3(array.SpacingSi.X, array.SpacingSi.Y, array.SpacingSi.Z),
            array.Values);
    }

    /// <summary>Builds the runtime gas a model describes, field and all.</summary>
    /// <param name="gas">The compiled gas.</param>
    /// <param name="modelDirectory">Where relative paths resolve against.</param>
    /// <returns>The gas.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    /// <exception cref="EinzelException">The declared field cannot be read.</exception>
    public static BackgroundGas Resolve(CompiledGas gas, string modelDirectory)
    {
        // WithoutImportedFields, not FromModel: this function is the one that reads
        // the files, so it is the one place where taking the declared gas alone is
        // correct rather than a silent omission. FromModel refuses here by design.
        var background = BackgroundGas.WithoutImportedFields(gas);
        var flow = Load(gas, modelDirectory);
        var density = LoadDensity(gas, modelDirectory);

        if (flow is not null)
        {
            background = background with { Flow = flow };
        }

        if (density is not null)
        {
            background = background with { Density = density };
        }

        return background;
    }

    /// <summary>Loads the pressure field a compiled gas names, if it names one.</summary>
    /// <param name="gas">The compiled gas.</param>
    /// <param name="modelDirectory">Where relative paths resolve against.</param>
    /// <returns>The density field, or null when the model declares none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gas"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// The file is missing, is not readable ImageData, holds a vector where a scalar
    /// is needed, or carries a pressure that is not positive.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The pressure is converted to a number density here rather than at every read,
    /// through n = p/kT at the model's single declared temperature. Because kT is a
    /// constant the two orders are the same arithmetic; what the ordering buys is one
    /// pass instead of a division per lookup.
    /// </para>
    /// <para>
    /// <b>Isothermal, stated.</b> A real differentially pumped instrument has a
    /// temperature gradient as well as a pressure one, and this model carries one
    /// temperature - so what is imported is a density field derived from a pressure
    /// field under that assumption. It was already made by there being a single
    /// <c>temperature</c> in the document; importing a pressure inherits it rather
    /// than adding it.
    /// </para>
    /// </remarks>
    public static SampledGasDensity? LoadDensity(CompiledGas gas, string modelDirectory)
    {
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        if (!gas.HasPressureField)
        {
            return null;
        }

        var path = Path.IsPathRooted(gas.PressureFieldPath!)
            ? gas.PressureFieldPath!
            : Path.GetFullPath(Path.Combine(modelDirectory, gas.PressureFieldPath!));

        if (!File.Exists(path))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/gas/pressureField/path",
                Constraint = $"there is no file at {path}",
                Suggestion = "the path is resolved against the model document's own directory, not "
                    + "the working directory, so that a model means the same thing wherever the "
                    + "command is run from",
            });
        }

        var array = VtkImageData.Read(
            File.ReadAllText(path), gas.PressureFieldArray, "/transport/gas/pressureField");

        if (array.Components != 1)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/gas/pressureField/array",
                Constraint = $"array '{array.Name}' has {array.Components} component(s), and a "
                    + "pressure needs one",
                Suggestion = "name the scalar array explicitly with \"array\": \"...\". A CFD "
                    + "export usually carries several, and the first one in the file is as likely "
                    + "to be a velocity as a pressure",
            });
        }

        // Into pascals, from whatever the document said the file holds. Validation
        // resolved the symbol, so this is a multiplication rather than a lookup.
        var pascals = new double[array.Values.Length];

        for (var i = 0; i < pascals.Length; i++)
        {
            pascals[i] = array.Values[i] * gas.PressureFieldScale;
        }

        return SampledGasDensity.FromPressure(
            new SampledGrid(
                1,
                array.CountX,
                array.CountY,
                array.CountZ,
                new Vec3(array.OriginSi.X, array.OriginSi.Y, array.OriginSi.Z),
                new Vec3(array.SpacingSi.X, array.SpacingSi.Y, array.SpacingSi.Z),
                pascals),
            gas.TemperatureK);
    }
}
