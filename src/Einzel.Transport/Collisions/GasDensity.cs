using Einzel.Core.Errors;
using Einzel.Core.Geometry;

namespace Einzel.Transport.Collisions;

/// <summary>
/// How much gas there is, as a number density at a point.
/// </summary>
/// <remarks>
/// <para>
/// The last thing about a gas this engine held as a single number for the whole
/// model. GAS-1 got its velocity field and its ions moved; the <em>density</em>
/// stayed uniform, so an imported flow gave the neutrals a velocity everywhere and
/// the same number of them everywhere - which is not a differentially pumped
/// instrument. A funnel behind an inlet capillary spans two decades of pressure
/// between its entrance and its exit, and every collision rate, mean free path,
/// mobility and diffusion coefficient in it varies with that.
/// </para>
/// <para>
/// <b>A field of pressure, read as a density.</b> Pressure is what a CFD code
/// writes and what a gauge reads; number density is what every consumer here
/// wants, and n = p/kT converts between them once at the boundary. The model
/// carries one temperature, so the two fields are the same field up to a constant -
/// which is a stated modelling assumption rather than a general truth, and it is
/// the assumption a single declared temperature already made.
/// </para>
/// <para>
/// <b>The highest density anywhere is not a curiosity, it is the majorant.</b> The
/// event-driven models schedule collisions before they know where the ion will be
/// when the event lands, so the rate they schedule at has to bound the true rate
/// everywhere along the step. That is exactly the null-collision method, which
/// this engine already runs for a speed-dependent hard-sphere rate; a
/// position-dependent density is the same mechanism reached a second way.
/// </para>
/// </remarks>
public interface IGasDensity
{
    /// <summary>Whether the density is the same everywhere.</summary>
    /// <remarks>
    /// Asked rather than inferred, for the same reason <see cref="IGasFlow.IsMoving"/>
    /// is: a caller that can skip the position lookup entirely wants to know cheaply,
    /// and a sampled field would have to scan itself to answer.
    /// </remarks>
    bool IsUniform { get; }

    /// <summary>The most gas anywhere, in reciprocal cubic metres.</summary>
    /// <remarks>
    /// What a null-collision bound and a worst-case regime check are taken against.
    /// A rate that bounds the densest region bounds every region.
    /// </remarks>
    double HighestNumberDensitySi { get; }

    /// <summary>The least gas anywhere, in reciprocal cubic metres.</summary>
    /// <remarks>
    /// Reported rather than used: the pair says how far the instrument is from the
    /// uniform gas a single declared pressure describes, which is the number a
    /// reader needs to judge whether importing a field changed anything.
    /// </remarks>
    double LowestNumberDensitySi { get; }

    /// <summary>Number density at a point, in reciprocal cubic metres.</summary>
    /// <param name="point">Where, in metres.</param>
    /// <returns>The density.</returns>
    double NumberDensityAt(in Vec3 point);

    /// <summary>
    /// Whether this field actually has data at a point, rather than extrapolating.
    /// </summary>
    /// <param name="point">The point, in metres.</param>
    /// <returns><see langword="true"/> where the density is defined.</returns>
    /// <remarks>
    /// True everywhere for a density given as a number, and only inside its own box
    /// for one imported from a file. The same choice the flow makes and for the same
    /// reason: outside, the edge value continues, and a caller flying through that
    /// region should be able to say so rather than reporting it as data.
    /// </remarks>
    bool Covers(in Vec3 point) => true;
}

/// <summary>
/// A gas at one density everywhere.
/// </summary>
/// <param name="NumberDensitySi">The density, in reciprocal cubic metres.</param>
/// <remarks>
/// What <c>transport.gas.pressure</c> means, and what every model meant before a
/// field could be imported. Right wherever the instrument is one pumped volume.
/// </remarks>
public sealed record UniformGasDensity(double NumberDensitySi) : IGasDensity
{
    /// <inheritdoc/>
    public bool IsUniform => true;

    /// <inheritdoc/>
    public double HighestNumberDensitySi => NumberDensitySi;

    /// <inheritdoc/>
    public double LowestNumberDensitySi => NumberDensitySi;

    /// <inheritdoc/>
    public double NumberDensityAt(in Vec3 point) => NumberDensitySi;
}

/// <summary>
/// A gas whose density is sampled from a grid.
/// </summary>
/// <remarks>
/// <para>
/// The other half of a differentially pumped instrument, and the half a velocity
/// field alone cannot supply. A jet carries ions; a pressure gradient decides how
/// hard it carries them, how far an ion goes between collisions, and whether the
/// region is one trajectory integration or statistical diffusion describes.
/// </para>
/// <para>
/// <b>Trilinear, for the reason the flow is.</b> ACC-3 forbids the cheap
/// interpolant on a trajectory path because its discontinuous derivatives
/// accumulate into the timing budget over many cell crossings. The density's
/// derivative is never taken: it scales a rate and a mobility, both read as values,
/// and a CFD field arrives with a discretisation error far above anything the
/// interpolant adds.
/// </para>
/// </remarks>
public sealed class SampledGasDensity : IGasDensity
{
    private readonly SampledGrid _grid;

    /// <summary>Creates a density field from samples of number density.</summary>
    /// <param name="grid">One component per node, in reciprocal cubic metres.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="ArgumentException">The grid does not carry one component.</exception>
    /// <exception cref="EinzelException">A sample is not a positive number.</exception>
    public SampledGasDensity(SampledGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (grid.Components != 1)
        {
            throw new ArgumentException(
                $"a density needs one component and this grid carries {grid.Components}",
                nameof(grid));
        }

        _grid = grid;

        var values = grid.Values;
        var lowest = double.PositiveInfinity;
        var highest = 0.0;

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];

            // Refused rather than clamped, and non-positive rather than merely
            // negative. Mobility goes as the reciprocal of density, so a zero is an
            // infinite mobility and a stability limit of zero - a run that never
            // finishes rather than one that answers wrongly. A region with no gas in
            // it is not one the diffusive mode describes at all, which is REG-2's
            // own argument, so the honest response is to say which mode does.
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = "/transport/gas/pressureField",
                    Constraint = $"sample {i} is {value:G6}, and a density must be a positive "
                        + "finite number",
                    Suggestion = "a pressure of zero is not a thin gas, it is no gas - mobility "
                        + "goes as 1/n, so it is an infinite drift and a stability limit of zero. "
                        + "Clamp the exported field to the lowest pressure the instrument actually "
                        + "reaches, or model that region with \"mode\": \"trajectory\", which is "
                        + "what describes a collisionless volume",
                });
            }

            lowest = Math.Min(lowest, value);
            highest = Math.Max(highest, value);
        }

        LowestNumberDensitySi = lowest;
        HighestNumberDensitySi = highest;
    }

    /// <summary>Reads a field of pressures as one of number densities.</summary>
    /// <param name="pascals">One component per node, in pascals.</param>
    /// <param name="temperatureK">The gas temperature, in kelvin.</param>
    /// <returns>The density field.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pascals"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The temperature is not positive.</exception>
    /// <remarks>
    /// <para>
    /// n = p/kT, applied once to the samples rather than on every read. Because kT
    /// is a constant the two orders are the same arithmetic, so converting the array
    /// costs one pass and saves a division per lookup.
    /// </para>
    /// <para>
    /// <b>One temperature, stated.</b> A real differentially pumped instrument has a
    /// temperature gradient as well as a pressure one, and this model carries a
    /// single declared temperature - so what is imported is a density field derived
    /// from a pressure field under an isothermal assumption. That assumption was
    /// already made by there being one <c>temperature</c> in the document; importing
    /// pressure does not add it, it inherits it.
    /// </para>
    /// </remarks>
    public static SampledGasDensity FromPressure(SampledGrid pascals, double temperatureK)
    {
        ArgumentNullException.ThrowIfNull(pascals);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(temperatureK);

        var scale = 1.0 / (BackgroundGas.BoltzmannSi * temperatureK);
        var source = pascals.Values;
        var densities = new double[source.Length];

        for (var i = 0; i < source.Length; i++)
        {
            densities[i] = source[i] * scale;
        }

        return new SampledGasDensity(pascals.WithValues(1, densities));
    }

    /// <inheritdoc/>
    public bool IsUniform => LowestNumberDensitySi == HighestNumberDensitySi;

    /// <inheritdoc/>
    public double HighestNumberDensitySi { get; }

    /// <inheritdoc/>
    public double LowestNumberDensitySi { get; }

    /// <summary>The lower corner of the sampled region, in metres.</summary>
    public Vec3 MinimumSi => _grid.MinimumSi;

    /// <summary>The upper corner, in metres.</summary>
    public Vec3 MaximumSi => _grid.MaximumSi;

    /// <inheritdoc/>
    public double NumberDensityAt(in Vec3 point) => _grid.ScalarAt(in point);

    /// <inheritdoc/>
    public bool Covers(in Vec3 point) => _grid.Covers(in point);

    /// <summary>
    /// How much of a box lies outside the sampled region, as a volume fraction.
    /// </summary>
    /// <param name="minimumSi">Lower corner of the box, in metres.</param>
    /// <param name="maximumSi">Upper corner.</param>
    /// <returns>Zero when the box is wholly inside, one when it is wholly outside.</returns>
    /// <remarks>
    /// What a caller warns with. Outside the imported extent the edge value is
    /// continued, and nothing in the samples says whether that is right - a stream
    /// really does carry on, and the end of a jet does not.
    /// </remarks>
    public double FractionOutside(Vec3 minimumSi, Vec3 maximumSi) =>
        _grid.FractionOutside(minimumSi, maximumSi);
}
