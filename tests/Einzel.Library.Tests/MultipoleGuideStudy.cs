using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// One template for every even multipole order, and what it cost to write.
/// </summary>
/// <remarks>
/// <para>
/// LIB-1: "If supporting a new device requires a change below Einzel.Library,
/// either it is genuinely novel physics or the abstraction is wrong. Almost always
/// the second." A multipole above four rods needed exactly one thing below the
/// library, and it was small and general: the expression grammar had no
/// trigonometry, so <c>2n</c> rods at <c>pi/n</c> intervals could not be written at
/// all. With <c>cosPi</c> and <c>sinPi</c> it is one template with
/// <c>poleCount</c> as a parameter, rather than three near-identical files.
/// </para>
/// <para>
/// Half turns rather than radians, which is the convention the drive decomposition
/// already chose and for the same reason: <c>Math.Cos(Math.PI / 2)</c> is 6.1e-17
/// rather than zero, so a rod placed at a quarter turn lands a hair off axis and
/// the multipole carries a spurious dipole made of rounding.
/// </para>
/// </remarks>
public sealed class MultipoleGuideStudy(ITestOutputHelper output)
{
    /// <summary>Denison's classical rod ratio for a quadrupole.</summary>
    private const double DenisonRatio = 1.1468;

    private static ModelDocument Guide(int poles)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("multipole-guide"));

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal)
        {
            ["poleCount"] = document.Parameters!["poleCount"] with { Value = poles },
        };

        return document with { Parameters = parameters };
    }

    private static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => e.Constraint)));

        return validation.Model!;
    }

    [Fact]
    public void TheRodsFitWhateverTheOrder()
    {
        // The geometric constraint, against its closed form. Rod centres sit on a
        // circle of r0 + rodRadius, adjacent centres are 2(r0 + rodRadius) sin(pi/N)
        // apart, and that must be at least twice the rod radius - which rearranges
        // to rodRatio <= sin(pi/N) / (1 - sin(pi/N)).
        //
        // This is what makes an overlapping geometry inexpressible rather than
        // merely refused: rodFill is a fraction of that maximum, so the rods cannot
        // be asked to intersect.
        output.WriteLine("poles   max ratio   closed form   actual ratio   nearest gap / mm");

        foreach (var poles in new[] { 4, 6, 8, 10, 12 })
        {
            var model = Compile(Guide(poles));

            var maximum = model.Parameters["maximumRodRatio"].In("1");
            var ratio = model.Parameters["rodRatio"].In("1");
            var radius = model.Parameters["rodRadius"].In("mm");
            var centre = model.Parameters["rodCentre"].In("mm");

            var sine = Math.Sin(Math.PI / poles);
            var expected = sine / (1.0 - sine);

            // The gap between neighbouring rod surfaces. Positive means they fit.
            var gap = (2.0 * centre * sine) - (2.0 * radius);

            output.WriteLine(
                $"{poles,5}   {maximum,9:F5}   {expected,11:F5}   {ratio,12:F5}   {gap,16:F4}");

            Assert.Equal(expected, maximum, 1e-12);
            Assert.True(gap > 0.0, $"the rods intersect at {poles} poles");
        }
    }

    [Fact]
    public void AtFourRodsItReproducesDenison()
    {
        // A published number reached through the derived-parameter chain rather than
        // written into it: rodFill 0.475 times sin(pi/4)/(1 - sin(pi/4)) = 2.41421
        // gives 1.14675, against Denison's 1.1468. That is a sharp check on cosPi
        // and sinPi as well as on the geometry, because a trigonometric function off
        // by a rounding would show here first.
        var ratio = Compile(Guide(4)).Parameters["rodRatio"].In("1");

        output.WriteLine($"rodRatio at four poles: {ratio:F5} against Denison's {DenisonRatio}");

        Assert.Equal(DenisonRatio, ratio, 1e-4);
    }

    [Fact]
    public void EveryOrderReducesToOneBasisSolve()
    {
        // The property that makes a multipole affordable, and it is not obvious:
        // adjacent rods alternate in phase, so they are exact negatives of one
        // another however many there are, and the whole structure is one spatial
        // pattern whose weight is a function of time. Twelve rods cost what four do.
        //
        // Exact negation is what does it, which is why the drive amplitude is
        // written as rfAmplitude * (1 - 2 mod(pole, 2)) rather than as a cosine of
        // the pole index: the second would be right to a rounding and would split
        // into two channels.
        output.WriteLine("poles   electrodes   basis solves   cycles   convergence");

        foreach (var poles in new[] { 4, 6, 8, 12 })
        {
            var model = Compile(Guide(poles));
            var solve = model.Fields[0].Solve!;

            var channels = GeometryBuilder.SolveChannels(solve);

            output.WriteLine(
                $"{poles,5}   {solve.Electrodes.Count,10}   {channels.Count,12}   "
                + $"{channels[0].Report.Cycles,6}   {channels[0].Report.ConvergenceFactor,11:F4}");

            Assert.Equal(poles, solve.Electrodes.Count);
            Assert.Single(channels);
            Assert.True(channels[0].Report.Converged);
        }
    }

    [Fact]
    public void EveryOrderGuidesAnIon()
    {
        // The floor: each order confines an ion entering off axis and delivers it.
        // Deliberately not compared *between* orders - see the note below.
        foreach (var poles in new[] { 4, 6, 8 })
        {
            var model = Compile(Guide(poles));
            var field = FieldAssembly.Build(model);
            var species = IonSpecies.FromModel(model);

            var launch = new PhaseState(
                model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

            var point = model.DetectorPoint;
            var normal = model.DetectorNormal;

            TrajectoryStopFunction detector =
                (in PhaseState state) => Core.Geometry.Vec3.Dot(state.Position - point, normal);

            var result = TrajectoryIntegrator.Integrate(
                launch,
                species,
                field,
                new IntegrationSettings
                {
                    RelativeTolerance = 1e-9,
                    MaximumFlightTime = model.MaximumFlightTimeSi,
                },
                detector);

            var radius = Math.Sqrt(
                (result.FinalState.Position.X * result.FinalState.Position.X)
                + (result.FinalState.Position.Y * result.FinalState.Position.Y));

            output.WriteLine(
                $"{poles} poles: {result.Outcome} at r = {radius * 1e3:F3} mm "
                + $"in {result.AcceptedSteps} steps");

            Assert.Equal(TrajectoryOutcome.StopConditionMet, result.Outcome);
        }
    }

    // What is deliberately NOT asserted here, and why.
    //
    // The obvious question about a multipole is whether a higher order accepts a
    // larger offset, and this template can be made to answer it - a boundary search
    // on launchOffset costs eleven evaluations per order. It is not asserted because
    // the measurement as set up is confounded.
    //
    // The template launches at (offset, offset), a 45 degree diagonal. For a
    // quadrupole, with rods on the axes, that is the *widest* gap between rods: an
    // ion enters at r = 4.95 mm and still arrives, outside the 4 mm inscribed
    // radius. For a hexapole the same diagonal falls between rods at 0 and 60
    // degrees, a narrower gap. So the comparison measures the angular gap the launch
    // point happens to sit in at least as much as it measures the multipole order.
    //
    // Measured anyway, for the record: at 200 V the hexapole accepts 0.68 r0 and the
    // octupole 0.58, and at 300 V that reverses to 0.46 and 0.48. A non-monotone
    // ordering that flips with amplitude is a sign the variable being scanned is not
    // the one that matters. Settling it needs a scan over launch *angle* as well as
    // radius, and an acceptance defined as a solid angle rather than one ray.
}
