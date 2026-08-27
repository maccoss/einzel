using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The travelling-wave guide: a stack of rings whose drive phase ramps along it.
/// </summary>
/// <remarks>
/// <para>
/// The seventh device template, and the first one that could not be written at all
/// until something below <c>Einzel.Library</c> changed. LIB-1 says that when a new
/// device needs a change lower down the abstraction is usually wrong, and to
/// believe it. Here it was right and narrow: <c>drivePhase</c> was a plain number
/// while every other placement was an expression, so a phase could not depend on
/// the repeat index - and a phase that cannot depend on the index cannot ramp. The
/// field's own documentation had said "a travelling wave is a ramp from zero to one
/// along its length" since it was written; that was the one device it could not
/// express.
/// </para>
/// <para>
/// Two claims are checked here, and they are separable. That the structure costs
/// two solves however many distinct phases it carries, which is SYM-1's argument
/// and the reason the device is affordable at all. And that what comes out is a
/// wave travelling at the speed the geometry declares, which is a closed form
/// rather than a picture.
/// </para>
/// </remarks>
public sealed class TravellingWaveGuideStudy(ITestOutputHelper output)
{
    /// <summary>Injection speeds the transit is scanned over, as fractions of the wave speed.</summary>
    private static readonly double[] SpeedRatios = [0.6, 0.8, 1.0, 1.2, 1.4];

    private static ModelDocument Template() =>
        Io.ModelJson.Parse(DeviceTemplates.Read("travelling-wave-guide"));

    private static ModelDocument With(ModelDocument document, params (string Name, double Value)[] overrides)
    {
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);
        Assert.True(validation.Model is not null, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        return validation.Model!;
    }

    private static CompiledSolvedField Solve(CompiledModel model) =>
        model.Fields[0].Solve ?? throw new InvalidOperationException("the template is not a solved2d field");

    [Fact]
    public void TheSolveCountDoesNotGrowWithTheRingCount()
    {
        // SYM-1's argument, measured, and the comparison is against what the same
        // decomposition does without the quadrature step rather than against a
        // number quoted from the documentation.
        //
        // A sinusoid is the special case that makes this work: A cos(2 pi (f t +
        // phi)) is exactly A cos(2 pi phi) cos(2 pi f t) - A sin(2 pi phi)
        // sin(2 pi f t), a fixed pair of time functions with constant
        // coefficients, so every phase in the structure resolves into the same two
        // supplies.
        output.WriteLine("rings   supplies without quadrature   basis solves with it");

        foreach (var rings in new[] { 12.0, 24.0, 48.0, 96.0 })
        {
            var model = Compile(With(Template(), ("ringCount", rings)));
            var solve = Solve(model);

            var excitations = solve.Electrodes
                .Select(e => new Excitation(e.Name, e.Potential, e.DriveAmplitude, e.DrivePhase))
                .ToArray();

            var naive = DriveChannels.Decompose(excitations, quadrature: false).Count;
            var channels = GeometryBuilder.SolveChannels(solve).Count;

            output.WriteLine($"{rings,5:F0}   {naive,27}   {channels,20}");

            Assert.Equal((int)rings, solve.Electrodes.Count);
            Assert.Equal(2, channels);

            // The naive count grows, which is what makes the two meaningful. It
            // does not grow to exactly the ring count: a phase of 7/6 and one of
            // 1/6 are the same angle and land in the same supply only when they
            // agree to the bit, and after the wrap they do not - so the count that
            // would actually have been paid is somewhere between the number of
            // distinct angles and the number of rings, and it is not knowable
            // without running it. That unpredictability is its own argument.
            Assert.True(
                naive > 2,
                $"{rings:F0} rings decomposed into {naive} supplies without quadrature, so this "
                + "comparison is measuring nothing");
        }
    }

    [Fact]
    public void ThePotentialIsAWaveTravellingAtTheDeclaredSpeed()
    {
        var model = Compile(Template());

        var expected = model.Parameters["waveSpeed"].In("m/s");
        var measured = Speed(model, output);

        output.WriteLine($"measured {measured:F1} m/s against {expected:F1} declared, "
            + $"{(measured / expected) - 1.0:P2}");

        Assert.Equal(expected, measured, Math.Abs(expected) * 0.02);
    }

    [Fact]
    public void ReversingTheDeclaredDirectionReversesTheWave()
    {
        // The control that makes the speed mean something. A measurement that only
        // ever produced one sign would be satisfied by a stack that oscillated in
        // place and a tracker that drifted; running the ramp the other way has to
        // send the wave upstream, and nothing but a real phase ramp does that.
        var forward = Speed(Compile(Template()), output);
        var backward = Speed(Compile(With(Template(), ("waveDirection", -1.0))), output);

        output.WriteLine($"downstream {forward:F1} m/s, upstream {backward:F1} m/s");

        Assert.True(forward > 0.0, "the wave did not travel downstream");
        Assert.True(backward < 0.0, "reversing the declared direction did not reverse the wave");

        // Same magnitude: the geometry is unchanged and only the sign of the ramp
        // differs, so an asymmetry would mean the tracker is measuring something
        // other than the wave.
        Assert.Equal(forward, -backward, forward * 0.02);
    }

    [Fact]
    public void TheWaveCarriesTheIonAtItsOwnSpeed()
    {
        // What the device is for, and the signature that says it is happening: the
        // transit time stops depending on how the ion was injected and becomes the
        // wave's own. An ion merely being shaken by a passing wave still arrives
        // when its injection energy says it will.
        //
        // The comparison that matters is against ballistic flight, not between the
        // measurements. Two transits close to each other prove nothing if their
        // ballistic values were close too - which is how a first version of this
        // test concluded, wrongly, that nothing was being carried.
        var model = Compile(Template());

        var wave = model.Parameters["waveSpeed"].In("m/s");
        var crest = Span(model) / wave;

        output.WriteLine($"wave {wave:F0} m/s, and a crest crosses the ion's path in {crest * 1e6:F3} us");
        output.WriteLine($"{"v/vwave",8} {"ballistic/us",13} {"measured/us",12}");

        var measured = new List<double>();

        foreach (var ratio in SpeedRatios)
        {
            var transit = Transit(With(Template(), ("speedRatio", ratio)));

            Assert.True(transit > 0.0, $"the ion injected at {ratio:F1} of the wave speed never arrived");

            measured.Add(transit);

            output.WriteLine($"{ratio,8:F1} {crest / ratio * 1e6,13:F3} {transit * 1e6,12:F3}");
        }

        var ballistic = SpeedRatios.Select(r => crest / r).ToArray();

        var measuredSpread = measured.Max() - measured.Min();
        var ballisticSpread = ballistic.Max() - ballistic.Min();

        output.WriteLine(
            $"spread {measuredSpread * 1e6:F3} us measured against {ballisticSpread * 1e6:F3} us "
            + $"ballistic, a factor of {ballisticSpread / measuredSpread:F1}");

        // Injection speed varied by more than a factor of two; the arrival should
        // barely notice.
        Assert.True(
            measuredSpread < 0.1 * ballisticSpread,
            $"transit spread {measuredSpread * 1e6:F3} us against a ballistic {ballisticSpread * 1e6:F3} "
            + "us: the wave is not carrying these ions, it is only perturbing them");

        // And what they all arrive at is the wave's transit, not some other number
        // that happens to be stable.
        Assert.Equal(crest, measured.Average(), crest * 0.05);
    }

    [Fact]
    public void TooShallowAWellOnlyPerturbsTheIon()
    {
        // The control, and the reason the shipped amplitude is not a round number
        // chosen for looks. Capture is a threshold: the well has to be deep enough
        // to hold an ion against its own velocity mismatch, and below that the ion
        // is pulled towards the wave without ever being caught by it.
        //
        // Without this the test above passes on any build where the wave happens to
        // dominate, and says nothing about whether the amplitude matters.
        var model = Compile(Template());
        var crest = Span(model) / model.Parameters["waveSpeed"].In("m/s");

        var shallow = new[] { SpeedRatios[0], SpeedRatios[^1] }
            .Select(r => Transit(With(Template(), ("speedRatio", r), ("rfAmplitude", 20.0))))
            .ToArray();

        Assert.All(shallow, t => Assert.True(t > 0.0, "an ion never arrived"));

        var spread = Math.Abs(shallow[0] - shallow[1]);
        var ballistic = Math.Abs((crest / SpeedRatios[0]) - (crest / SpeedRatios[^1]));

        output.WriteLine(
            $"at a third of the shipped amplitude: spread {spread * 1e6:F3} us against a ballistic "
            + $"{ballistic * 1e6:F3} us");

        Assert.True(
            spread > 0.2 * ballistic,
            $"a well a third as deep still captured the ions ({spread * 1e6:F3} us of spread), so "
            + "the shipped amplitude is not where the threshold is and the parameter's description "
            + "is wrong");
    }

    /// <summary>Flight time to the detector, or zero if the ion never arrives.</summary>
    private static double Transit(ModelDocument document)
    {
        var model = Compile(document);
        var field = FieldAssembly.Build(model);

        var launch = new Transport.PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        Transport.Integration.TrajectoryStopFunction detector =
            (in Transport.PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var result = Transport.Integration.TrajectoryIntegrator.Integrate(
            launch,
            Transport.IonSpecies.FromModel(model),
            field,
            new Transport.Integration.IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector);

        return result.Outcome == Transport.Integration.TrajectoryOutcome.StopConditionMet
            ? result.FlightTimeSeconds
            : 0.0;
    }

    /// <summary>How far the ion travels from its launch point to the detector.</summary>
    private static double Span(CompiledModel model) =>
        model.DetectorPoint.X - model.SourcePosition.X;

    /// <summary>
    /// Crest velocity on the axis, signed, from the phase of the fundamental
    /// spatial harmonic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not by following the tallest point, which was the first attempt and does not
    /// work: the sampling window holds two wavelengths, so the tallest point jumps
    /// to the next crest as the first one leaves and a straight line through the
    /// jump reported 4,200 m/s against a true 3,000.
    /// </para>
    /// <para>
    /// Projecting onto exp(2 pi i x / lambda) instead uses the whole window and has
    /// no crest to lose. For a wave running downstream the surviving term goes as
    /// exp(+2 pi i f t) and for one running upstream as exp(-2 pi i f t), so the
    /// direction is in the sign of the phase advance and the speed is that advance
    /// times the wavelength over two pi.
    /// </para>
    /// </remarks>
    private static double Speed(CompiledModel model, ITestOutputHelper output)
    {
        var driven = (ITimeVaryingField)FieldAssembly.Build(model);

        var period = 1.0 / model.Parameters["driveFrequency"].In("Hz");
        var pitch = model.Parameters["ringPitch"].In("m");
        var entry = model.Parameters["entryGap"].In("m");
        var rings = model.Parameters["ringCount"].In("1");
        var wavelength = model.Parameters["ringsPerWave"].In("1") * pitch;

        // Inside the stack, clear of both ends, where the wave is formed rather
        // than leaking out of the last ring.
        var lower = entry + (0.25 * rings * pitch);
        var upper = entry + (0.75 * rings * pitch);

        // An eighth of a period, so the phase advances by a quarter turn: far
        // enough to measure and well short of the half turn where the sign of the
        // advance would stop being readable.
        var step = period / 8.0;

        var before = Harmonic(driven, lower, upper, wavelength, 0.0);
        var after = Harmonic(driven, lower, upper, wavelength, step);

        var advance = after - before;

        while (advance > Math.PI)
        {
            advance -= 2.0 * Math.PI;
        }

        while (advance < -Math.PI)
        {
            advance += 2.0 * Math.PI;
        }

        output.WriteLine(
            $"window {lower * 1e3:F2} to {upper * 1e3:F2} mm, wavelength {wavelength * 1e3:F2} mm, "
            + $"phase advance {advance:F4} rad in {step * 1e6:F3} us");

        return wavelength * advance / (2.0 * Math.PI) / step;
    }

    /// <summary>Phase of the fundamental spatial harmonic on the axis, in radians.</summary>
    private static double Harmonic(
        ITimeVaryingField field, double lower, double upper, double wavelength, double time)
    {
        const int Samples = 4000;

        var real = 0.0;
        var imaginary = 0.0;

        for (var s = 0; s < Samples; s++)
        {
            var x = lower + ((upper - lower) * s / Samples);
            var potential = field.PotentialAt(new Vec3(x, 0.0, 0.0), time);
            var angle = 2.0 * Math.PI * x / wavelength;

            real += potential * Math.Cos(angle);
            imaginary += potential * Math.Sin(angle);
        }

        return Math.Atan2(imaginary, real);
    }
}
