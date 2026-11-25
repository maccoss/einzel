using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Analytic;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A field seen from an instant part-way through the instrument's timeline.
/// </summary>
/// <remarks>
/// The integrator always starts at t = 0 and a leg of a sequenced run does not, so a
/// trajectory phase resuming after a diffusive one has to see the field at the instant it
/// actually begins. Wrapped rather than given a start time on the integrator, which is
/// the precedent <c>AxisymmetricField</c> and <c>PonderomotiveField</c> set: the
/// transport core carries every validated number here, and refactoring it to add a case
/// beside it is how those get quietly lost.
/// </remarks>
public sealed class TimeShiftedFieldTests(ITestOutputHelper output)
{
    private static SequencedField Two() => new(
        [UniformField.Create(new Vec3(100.0, 0.0, 0.0)),
         UniformField.Create(new Vec3(900.0, 0.0, 0.0))],
        [1.0e-4, 1.1e-4]);

    /// <summary>The leg's own clock starts where the offset says.</summary>
    [Fact]
    public void TheLegSeesTheInstrumentAtItsOwnOffset()
    {
        var shifted = new TimeShiftedField(Two(), 1.0e-4);

        // t = 0 for this leg is 100 us on the instrument, which is the push.
        Assert.Equal(900.0, shifted.ElectricFieldAt(Vec3.Zero, 0.0).X, 1e-12);

        // And an unshifted leg sees the hold there.
        Assert.Equal(100.0, Two().ElectricFieldAt(Vec3.Zero, 0.0).X, 1e-12);

        output.WriteLine("the same instrument, two legs, two instants");
    }

    /// <summary>
    /// A switch is reported in the leg's clock, so the integrator can land on it.
    /// </summary>
    /// <remarks>
    /// The integrator refuses to step past what <c>NextSwitchAfter</c> returns. Reported
    /// in the instrument's clock it would be a time the leg never reaches — or worse, a
    /// time already behind it, which would stall the step.
    /// </remarks>
    [Fact]
    public void ASwitchIsReportedInTheLegsOwnClock()
    {
        var shifted = new TimeShiftedField(Two(), 5.0e-5);

        // The instrument switches at 100 us; this leg starts at 50, so it meets it at 50.
        Assert.Equal(5.0e-5, shifted.NextSwitchAfter(0.0), 1e-18);

        // And the one at 110 us is 60 us into the leg.
        Assert.Equal(6.0e-5, shifted.NextSwitchAfter(5.0e-5), 1e-18);

        Assert.Equal(double.PositiveInfinity, shifted.NextSwitchAfter(6.0e-5));
    }

    /// <summary>A switch already behind the offset is not one this leg will meet.</summary>
    /// <remarks>
    /// It must not come back negative. The integrator would read a past instant as a
    /// step it has to take backwards, and stall.
    /// </remarks>
    [Fact]
    public void ASwitchBehindTheOffsetIsNotReported()
    {
        var shifted = new TimeShiftedField(Two(), 1.05e-4);

        var next = shifted.NextSwitchAfter(0.0);

        output.WriteLine($"next switch {next:E3} s into a leg starting at 105 us");

        Assert.True(next > 0.0, "a switch this leg has already passed is not ahead of it");
        Assert.Equal(5.0e-6, next, 1e-18);
    }

    /// <summary>A zero offset changes nothing at all.</summary>
    /// <remarks>
    /// The control that makes the rest meaningful: the first leg of any sequence is
    /// unshifted, so wrapping must be the identity there rather than nearly so.
    /// </remarks>
    [Fact]
    public void AZeroOffsetIsTheIdentity()
    {
        var inner = Two();
        var shifted = new TimeShiftedField(inner, 0.0);

        foreach (var t in new[] { 0.0, 5e-5, 1.0e-4, 1.05e-4, 2.0e-4 })
        {
            Assert.Equal(
                inner.ElectricFieldAt(Vec3.Zero, t).X,
                shifted.ElectricFieldAt(Vec3.Zero, t).X);

            Assert.Equal(inner.NextSwitchAfter(t), shifted.NextSwitchAfter(t));
        }
    }

    /// <summary>A time-free caller gets the leg's start, stated rather than accidental.</summary>
    /// <remarks>
    /// A driven field answers the time-free interface without failing, which is the
    /// defect this project has found four times — in `einzel solve`, the diffusive mode,
    /// `SuperposedField` and the renderer. Here the instant it answers at is the one the
    /// leg begins at, which is at least defensible.
    /// </remarks>
    [Fact]
    public void ATimeFreeCallerGetsTheLegsStart()
    {
        var shifted = new TimeShiftedField(Two(), 1.0e-4);

        Assert.Equal(900.0, ((IElectrostaticField)shifted).ElectricFieldAt(Vec3.Zero).X, 1e-12);
    }

    /// <summary>A negative or non-finite offset is refused.</summary>
    [Fact]
    public void AnImpossibleOffsetIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeShiftedField(Two(), -1.0e-6));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeShiftedField(Two(), double.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimeShiftedField(Two(), double.NaN));
    }

    /// <summary>
    /// The switches a leg holds answer exactly as a scan of the inner field's boundaries did, on
    /// every boundary, a few ulps either side of each, and between them.
    /// </summary>
    /// <remarks>
    /// The reference is the scan this wrapper used to run on every query - sixteen decrements and
    /// a walk from there - written out here so a lookup that answers differently anywhere fails.
    /// </remarks>
    [Fact]
    public void HeldSwitchesAnswerAsTheScanDid()
    {
        var random = new Random(20261124);
        var compared = 0;

        for (var draw = 0; draw < 60; draw++)
        {
            var count = 2 + random.Next(30);
            var ends = new double[count];
            var at = 0.0;

            for (var k = 0; k < count; k++)
            {
                at += Math.Exp(random.NextDouble() * Math.Log(1e4)) * 1e-9;
                ends[k] = at;
            }

            var inner = new SequencedField(
                ends.Select((_, k) => UniformField.Create(new Vec3(k + 1.0, 0.0, 0.0))).ToArray(), ends);
            var offset = draw % 3 == 0 ? 0.0 : ends[random.Next(count)] * random.NextDouble();
            var shifted = new TimeShiftedField(inner, offset);

            var instants = new List<double> { 0.0, at * 2.0 };

            foreach (var end in ends)
            {
                var reported = end - offset;
                instants.Add(reported);

                for (var ulp = 1; ulp <= 20; ulp++)
                {
                    instants.Add(Step(reported, ulp));
                    instants.Add(Step(reported, -ulp));
                }

                instants.Add(reported * random.NextDouble());
            }

            foreach (var t in instants.Where(t => t >= 0.0))
            {
                Assert.Equal(ScannedNextSwitchAfter(inner, offset, t), shifted.NextSwitchAfter(t));
                Assert.Equal(
                    inner.ElectricFieldAt(Vec3.Zero, ScannedOnTheInstrument(inner, offset, t)).X,
                    shifted.ElectricFieldAt(Vec3.Zero, t).X);
                Assert.Equal(
                    inner.ElectricFieldBefore(Vec3.Zero, ScannedOnTheInstrument(inner, offset, t)).X,
                    shifted.ElectricFieldBefore(Vec3.Zero, t).X);
                compared++;
            }
        }

        output.WriteLine($"{compared} instants answered as the scan did");
    }

    /// <summary>A query asks the inner field nothing about its switches, and allocates nothing.</summary>
    /// <remarks>
    /// The integrator asks a leg for its next switch on every step and for its field at every
    /// stage; each of those used to scan the inner field's boundaries from sixteen ulps back.
    /// </remarks>
    [Fact]
    public void AQueryNeitherScansNorAllocates()
    {
        var ends = Enumerable.Range(1, 40).Select(k => k * 1e-6).ToArray();
        var inner = new CountingSwitches(new SequencedField(
            ends.Select(_ => UniformField.Create(new Vec3(1.0, 0.0, 0.0))).ToArray(), ends));
        var shifted = new TimeShiftedField(inner, 3.5e-6);

        // The first query reads the leg's switches; every one after it reads them from the leg.
        var total = 0.0;
        total += shifted.NextSwitchAfter(0.0) + shifted.ElectricFieldAt(Vec3.Zero, 1e-6).X;
        var asked = inner.Asked;

        double Queries()
        {
            var sum = 0.0;

            for (var k = 0; k < 10_000; k++)
            {
                var t = k * 3.3e-9;
                sum += shifted.NextSwitchAfter(t);
                sum += shifted.ElectricFieldAt(Vec3.Zero, t).X;
                sum += shifted.ElectricFieldBefore(Vec3.Zero, shifted.NextSwitchAfter(t)).X;
            }

            return sum;
        }

        // CHEAPEST OF FIVE, after a warm pass. The property is a floor: the runtime charges one-off
        // work - a method promoted to its next tier, an on-stack replacement of this very loop - to
        // whichever window it fires in, and a Windows CI run once read 4,160 bytes over 30,000
        // queries that way, once, with every query allocating nothing. A query that allocated
        // would allocate in every window, thirty thousand times.
        total += Queries();
        var allocated = long.MaxValue;

        for (var window = 0; window < 5; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            total += Queries();
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        output.WriteLine($"{inner.Asked - asked} inner switch queries and {allocated} bytes over 30,000 queries, cheapest of five ({total:G3})");

        Assert.Equal(asked, inner.Asked);
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// Making a leg allocates the leg and nothing else, and a leg whose field reports no switch
    /// allocates nothing to answer that it has none.
    /// </summary>
    /// <remarks>
    /// The ramped diffusive path makes a leg at every density step and at once holds it at its
    /// operating point, which makes another; the first is never asked anything, and the held one's
    /// field has no switch. Reading the switches when the leg was made scanned the instrument
    /// and allocated two arrays per leg, per step.
    /// </remarks>
    [Fact]
    public void MakingALegAllocatesOnlyTheLeg()
    {
        var ends = Enumerable.Range(1, 40).Select(k => k * 1e-6).ToArray();
        var sequenced = new SequencedField(
            ends.Select(_ => UniformField.Create(new Vec3(1.0, 0.0, 0.0))).ToArray(), ends);
        var oscillating = OscillatingUniformField.Create(new Vec3(1.0, 0.0, 0.0), Core.Units.Quantity.From(1.0, "MHz"));

        _ = new TimeShiftedField(sequenced, 1e-6);
        _ = new TimeShiftedField(oscillating, 1e-6).NextSwitchAfter(0.0);

        const int legs = 1000;
        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var k = 0; k < legs; k++)
        {
            total += new TimeShiftedField(sequenced, (k + 1) * 1e-9).OffsetSeconds;
        }

        var made = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)legs;
        before = GC.GetAllocatedBytesForCurrentThread();

        for (var k = 0; k < legs; k++)
        {
            total += new TimeShiftedField(oscillating, (k + 1) * 1e-9).NextSwitchAfter(0.0);
        }

        var asked = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)legs;

        output.WriteLine($"{made:F0} bytes a leg made, {asked:F0} bytes a switchless leg asked ({total:G3})");

        // The leg itself: a header, its field and window references, its offset and end, and a flag.
        Assert.True(made <= 56, $"{made:F0} bytes to make a leg");
        Assert.True(asked <= 56, $"{asked:F0} bytes to make a switchless leg and ask it");
    }

    /// <summary>
    /// A leg of a long staircase reads its own few switches, not the whole timeline, and still
    /// answers past them.
    /// </summary>
    /// <remarks>
    /// Two hundred thousand phases is more than a leg holds, so an unbounded leg gave up holding
    /// and scanned the instrument on every query - from sixteen ulps back, through every switch
    /// before the leg. Bounded at its own end, a five-phase leg reads its seven or so switches
    /// once and asks the instrument nothing after that.
    /// </remarks>
    [Fact]
    public void ALegOfALongStaircaseReadsOnlyItsOwnSwitches()
    {
        const int phases = 200_000;
        const double phase = 1e-6;
        var ends = Enumerable.Range(1, phases).Select(k => k * phase).ToArray();
        var level = UniformField.Create(new Vec3(1.0, 0.0, 0.0));
        var inner = new CountingSwitches(new SequencedField(ends.Select(_ => level).ToArray(), ends));
        var start = 100_000 * phase;
        var leg = new TimeShiftedField(inner, start, start + (5 * phase));

        // The leg's instants, first: every one answered as the scan answers it.
        for (var k = 0; k < 600; k++)
        {
            var t = k * 1e-8;
            Assert.Equal(ScannedNextSwitchAfter(inner, start, t), leg.NextSwitchAfter(t));
        }

        var asked = inner.Asked;
        var before = asked;

        for (var k = 0; k < 600; k++)
        {
            _ = leg.NextSwitchAfter(k * 1e-8);
            _ = leg.ElectricFieldAt(Vec3.Zero, k * 1e-8);
        }

        asked = inner.Asked - before;

        // And past its end, by scanning, still right.
        Assert.Equal(ScannedNextSwitchAfter(inner, start, 7.5 * phase), leg.NextSwitchAfter(7.5 * phase));

        output.WriteLine($"{asked} inner switch queries over 1,200 leg queries");

        Assert.Equal(0, asked);
    }

    /// <summary>
    /// A leg with no end over a timeline of more switches than a window holds asks the instrument
    /// about a switch about once per switch it passes, not once or twice per evaluation.
    /// </summary>
    /// <remarks>
    /// Past what it held, a leg used to scan the instrument on every evaluation - two queries or
    /// so each, and each of those a scan of the whole staircase from its start.
    /// </remarks>
    [Fact]
    public void ALongTimelineIsReadAWindowAtATime()
    {
        const int phases = 70_000;
        const double phase = 1e-6;
        var ends = Enumerable.Range(1, phases).Select(k => k * phase).ToArray();
        var level = UniformField.Create(new Vec3(1.0, 0.0, 0.0));
        var inner = new CountingSwitches(new SequencedField(ends.Select(_ => level).ToArray(), ends));
        var leg = new TimeShiftedField(inner, 1000 * phase);

        const int evaluations = 50_000;
        var span = 8000 * phase;
        var before = inner.Asked;

        for (var k = 0; k < evaluations; k++)
        {
            var t = span * k / evaluations;
            _ = leg.NextSwitchAfter(t);
            _ = leg.ElectricFieldAt(Vec3.Zero, t);
        }

        var asked = inner.Asked - before;

        output.WriteLine($"{asked} inner switch queries over {2 * evaluations} evaluations across 8,000 switches");

        Assert.True(asked < 20_000, $"{asked} inner switch queries for {2 * evaluations} evaluations");

        // And the answers are the scan's.
        for (var k = 0; k < 2000; k++)
        {
            var t = 69_000 * phase * k / 2000;
            Assert.Equal(ScannedNextSwitchAfter(inner, 1000 * phase, t), leg.NextSwitchAfter(t));
        }
    }

    /// <summary>
    /// Two instrument switches that report at one leg instant are not lost: before it the leg is
    /// in the state before both, at it the state after both, and it says so.
    /// </summary>
    /// <remarks>
    /// One ulp apart on the instrument's clock, and 1.08e-7 s of offset makes them equal on the
    /// leg's. The leg used to hand the inner field the first boundary at that instant - the
    /// one-ulp phase between them - and report no switch after it, so it flew that phase for
    /// the rest of the leg and the instrument's real next state never came.
    /// </remarks>
    [Fact]
    public void SwitchesThatCollapseOnTheLegsClockAreNotLost()
    {
        const double first = 6.897330734315204e-07;
        const double offset = 1.0822964451916893e-07;
        var second = Math.BitIncrement(first);

        Assert.Equal(first - offset, second - offset);

        var inner = new SequencedField(
            [UniformField.Create(new Vec3(1.0, 0.0, 0.0)), UniformField.Create(new Vec3(2.0, 0.0, 0.0)), UniformField.Create(new Vec3(3.0, 0.0, 0.0))],
            [first, second, 2e-6]);
        var leg = new TimeShiftedField(inner, offset);
        var at = first - offset;

        Assert.Equal(at, leg.NextSwitchAfter(0.0));
        Assert.Equal(1.0, leg.ElectricFieldBefore(Vec3.Zero, at).X);
        Assert.Equal(3.0, leg.ElectricFieldAt(Vec3.Zero, at).X);
        Assert.Equal(2e-6 - offset, leg.NextSwitchAfter(at));
        Assert.True(leg.HasCollapsedSwitches);

        // The control: a leg whose switches stay apart says nothing.
        var apart = new TimeShiftedField(inner, 0.0, double.PositiveInfinity);
        _ = apart.NextSwitchAfter(0.0);
        Assert.False(apart.HasCollapsedSwitches);
    }

    /// <summary>A leg refuses an end before its start, and a missing field before anything else.</summary>
    [Fact]
    public void ALegsEndAndFieldAreChecked()
    {
        var two = Two();

        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeShiftedField(two, 2e-6, 1e-6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeShiftedField(two, 1e-6, double.NaN));
        Assert.Throws<ArgumentNullException>(() => new TimeShiftedField(null!, double.NaN, double.NaN));
        _ = new TimeShiftedField(two, 1e-6, 1e-6);
    }

    private static double Step(double value, int ulps)
    {
        for (var k = 0; k < Math.Abs(ulps); k++)
        {
            value = ulps > 0 ? Math.BitIncrement(value) : Math.BitDecrement(value);
        }

        return value;
    }

    private static double SixteenBefore(double instant) => Step(instant, -16);

    // The scan TimeShiftedField ran on every query before it held its switches.
    private static double ScannedOnTheInstrument(ITimeVaryingField inner, double offset, double t)
    {
        var instrument = t + offset;
        var boundary = inner.NextSwitchAfter(SixteenBefore(instrument));

        while (double.IsFinite(boundary))
        {
            var reported = boundary - offset;

            if (reported == t)
            {
                return boundary;
            }

            if (reported > t)
            {
                break;
            }

            boundary = inner.NextSwitchAfter(boundary);
        }

        return instrument;
    }

    private static double ScannedNextSwitchAfter(ITimeVaryingField inner, double offset, double t)
    {
        var boundary = inner.NextSwitchAfter(SixteenBefore(t + offset));

        while (double.IsFinite(boundary) && boundary - offset <= t)
        {
            boundary = inner.NextSwitchAfter(boundary);
        }

        return double.IsFinite(boundary) ? boundary - offset : double.PositiveInfinity;
    }

    private sealed class CountingSwitches(ITimeVaryingField inner) : ITimeVaryingField
    {
        public long Asked { get; private set; }

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public double NextSwitchAfter(double timeSeconds)
        {
            Asked++;
            return inner.NextSwitchAfter(timeSeconds);
        }

        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) => inner.ElectricFieldAt(position, timeSeconds);

        public double PotentialAt(in Vec3 position, double timeSeconds) => inner.PotentialAt(position, timeSeconds);

        public Vec3 ElectricFieldBefore(in Vec3 position, double switchSeconds) => inner.ElectricFieldBefore(position, switchSeconds);

        public double PotentialBefore(in Vec3 position, double switchSeconds) => inner.PotentialBefore(position, switchSeconds);

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(position);

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position);
    }
}
