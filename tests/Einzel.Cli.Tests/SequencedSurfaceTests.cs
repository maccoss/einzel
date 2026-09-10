using System.Text.Json;

using Einzel.Cli;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A mode-changing run, through the CLI (SEQ-1).
/// </summary>
/// <remarks>
/// The engine can cross a transport-mode boundary; this is about whether a model author
/// can reach that. A capability nothing can invoke is the "named in a csproj and nowhere
/// else" state this project keeps finding and criticising.
/// </remarks>
public sealed class SequencedSurfaceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-seq", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private const string TrapThenExtract = """
    {
      "schemaVersion": "0.6",
      "name": "trap-then-extract",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [10, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 5, "unit": "V" },
        "cloud": {
          "ions": 100,
          "seed": 7,
          "temperature": { "value": 300, "unit": "K" },
          "transverseSpread": { "value": 0.5, "unit": "mm" },
          "longitudinalSpread": { "value": 0.5, "unit": "mm" }
        }
      },
      "sequence": [
        { "name": "trap",    "duration": { "value": 20, "unit": "us" }, "mode": "diffusion" },
        { "name": "extract", "duration": { "value": 5, "unit": "us" },  "mode": "trajectory" }
      ],
      "fields": [{ "type": "fieldFree" }],
      "detector": {
        "planePoint": { "value": [60, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "trajectory",
        "maximumFlightTime": { "value": 1, "unit": "ms" },
        "mobility": { "zeroField": { "value": 0.09, "unit": "m^2/(V s)" } },
        "densityGrid": {
          "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
          "intervalsX": 64, "intervalsY": 32
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    private string Project()
    {
        Assert.Equal(0, Run("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", "seq.json");

        File.WriteAllText(path, TrapThenExtract);

        return path;
    }

    /// <summary>A model whose phases change mode runs, and reports each phase.</summary>
    [Fact]
    public void AModeChangingModelRunsAndReportsEachPhase()
    {
        var (exit, stdout, _) = Run("run", Project(), "--json");

        Assert.Equal(0, exit);

        var result = JsonDocument.Parse(stdout).RootElement;
        var sequence = result.GetProperty("sequence");

        output.WriteLine(sequence.ToString());

        Assert.Equal(1, sequence.GetProperty("conversions").GetInt32());

        var phases = sequence.GetProperty("phases").EnumerateArray().ToArray();

        Assert.Equal(2, phases.Length);
        Assert.Equal("diffusion", phases[0].GetProperty("mode").GetString());
        Assert.Equal("trajectory", phases[1].GetProperty("mode").GetString());

        // A diffusive phase has no trajectories at all, which is different from having
        // none left - the population is what carries across a boundary.
        Assert.Equal(0, phases[0].GetProperty("trajectories").GetInt32());
        Assert.True(phases[1].GetProperty("trajectories").GetInt32() > 0);
    }

    /// <summary>
    /// There is no flight time, and it is absent rather than not-a-number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sequenced run ends when its sequence ends, not when an ion arrives. This project
    /// already fixed exactly this for the diffusive mode — a density has no flight time,
    /// and printing NaN made a missing measurement indistinguishable from a failed one —
    /// but that fix was gated on <c>run.Diffusion is null</c>, so a third kind of run
    /// walked straight back into it.
    /// </para>
    /// <para>
    /// The JSON side is the finite-double policy: a non-finite number is written as null,
    /// because absent and zero are different answers and a reader cannot tell them apart
    /// if both print as a number.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThereIsNoFlightTimeAndItIsAbsentRatherThanNotANumber()
    {
        var model = Project();

        var json = JsonDocument.Parse(Run("run", model, "--json").Stdout).RootElement;
        var flight = json.GetProperty("flightTime");

        Assert.Equal(JsonValueKind.Null, flight.GetProperty("value").ValueKind);

        var human = Run("run", model).Stdout;

        output.WriteLine(human);

        Assert.DoesNotContain("NaN", human, StringComparison.Ordinal);
        Assert.DoesNotContain("flight time", human, StringComparison.Ordinal);

        // What it says instead: the packet's centre, labelled as a centre because there
        // is no single ion whose final position it could be.
        Assert.Contains("packet centre", human, StringComparison.Ordinal);
    }

    /// <summary>Every conversion warning reaches the caller (GRD-2).</summary>
    /// <remarks>
    /// The seam this project has dropped evidence at four times. A reader who takes a
    /// number from after a boundary without knowing the velocities were invented there
    /// has been misled by the platform, which is what GRD-3 exists to prevent — so these
    /// are violations and cannot be silenced.
    /// </remarks>
    [Fact]
    public void EveryConversionWarningReachesTheCaller()
    {
        var model = Project();

        var json = JsonDocument.Parse(Run("run", model, "--json").Stdout).RootElement;

        var codes = json.GetProperty("flightTime").GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString())
            .ToArray();

        output.WriteLine(string.Join("\n", codes));

        Assert.Contains("transport.velocity-assumed", codes);
        Assert.Contains("transport.mode-changed", codes);
        Assert.Contains("transport.mode-changed-in-sequence", codes);

        // And in the terminal too, not only the machine-readable surface - on stderr,
        // because CLI-2 puts results on stdout and diagnostics on stderr. Asserting on
        // stdout here passed my own manual check only because I had merged the streams.
        Assert.Contains(
            "transport.velocity-assumed", Run("run", model).Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every phase reports how wide the packet was, in one field on both sides of a
    /// conversion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The density solver computed this and the report dropped it</b> - `DensityField`
    /// has had `Spread()` since the mode was built, and a sequenced phase carried only the
    /// centroid. That is the recurring shape here: a thing the mode computed that nothing
    /// downstream could see. Found because the TIMS front-end study's open question is
    /// exactly "how wide is the packet when the ramp starts", and there was no way to ask.
    /// </para>
    /// <para>
    /// <b>One field on both sides, which is SEQ-1's own subject.</b> Position is the one
    /// thing both descriptions carry - a conversion to a density discards the velocities
    /// entirely - so a width is comparable across a phase boundary where almost nothing
    /// else is. Reporting it in two differently-named fields would make the comparison
    /// somebody's arithmetic instead of the report's.
    /// </para>
    /// <para>
    /// The physics is checked elsewhere, against a closed form, in
    /// <c>MobilityBalanceWidthTests</c>: a packet held against a moving gas settles to
    /// <c>sqrt((kT/q)/|dE/dx|)</c>. What this checks is that the number reaches a reader.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPhaseReportsHowWideThePacketWas()
    {
        var json = JsonDocument.Parse(Run("run", Project(), "--json").Stdout).RootElement;
        var phases = json.GetProperty("sequence").GetProperty("phases").EnumerateArray().ToArray();

        Assert.Equal(2, phases.Length);

        foreach (var phase in phases)
        {
            var spread = phase.GetProperty("spreadMm").EnumerateArray()
                .Select(v => v.GetDouble())
                .ToArray();

            output.WriteLine(
                $"{phase.GetProperty("name").GetString(),-8} "
                + $"{phase.GetProperty("mode").GetString(),-11} "
                + $"x {phase.GetProperty("centroidMm")[0].GetDouble(),8:F3} "
                + $"+- {spread[0]:F4}, {spread[1]:F4} mm");

            // A width, not a placeholder: this cloud is declared with a 0.5 mm spread on
            // both axes, so a phase reporting zero would be reporting a point source.
            Assert.Equal(2, spread.Length);
            Assert.True(spread[0] > 0.0, "the axial width is not positive");
            Assert.True(spread[1] > 0.0, "the radial width is not positive");
        }

        // The diffusive phase comes first and the trajectory phase second, so the field is
        // filled from both descriptions in one run - which is what makes it one quantity
        // rather than two that happen to share a name.
        Assert.Equal("diffusion", phases[0].GetProperty("mode").GetString());
        Assert.Equal("trajectory", phases[1].GetProperty("mode").GetString());

        // And the terminal shows it beside the centre, because for a mobility analyser the
        // two together are the measurement.
        Assert.Contains("+-", Run("run", Project()).Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// The width at every phase reaches the report, as the timeline rather than as a
    /// scalar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seam dropped it a second time, one level up.</b> The phase record gained a
    /// width and <c>einzel report</c> - written the day before - showed a sequenced run as
    /// five scalars: phases, conversions, arrived, mean arrival, arrival spread. So the
    /// study's whole subject was computed by the solver, carried through the result
    /// document, and absent from the page a person reads. The same shape twice in two days,
    /// which is why this asserts the reader's surface and not the record's.
    /// </para>
    /// <para>
    /// <b>A timeline, not more scalars.</b> A flat name/value list has nowhere to put the
    /// instant a number belongs to, and what a sequenced run answers is how a quantity
    /// moved through the phases - so a hold split into phases turns this table into a
    /// relaxation curve with no new capability at all. That is the whole reason it is worth
    /// carrying per phase.
    /// </para>
    /// <para>
    /// <b>Equality against the run's own numbers is what makes this a view.</b> A report
    /// that recomputed the packet, or rounded to its own taste, would pass a test that only
    /// asked for a positive number here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWidthAtEveryPhaseReachesTheReport()
    {
        var run = JsonDocument.Parse(Run("run", Project(), "--json").Stdout).RootElement;
        var ran = run.GetProperty("sequence").GetProperty("phases").EnumerateArray().ToArray();

        var (exit, stdout, _) = Run("report", _root, "--json");

        Assert.Equal(0, exit);

        var reported = JsonDocument.Parse(stdout).RootElement
            .GetProperty("runs").EnumerateArray().Single()
            .GetProperty("phases").EnumerateArray().ToArray();

        Assert.Equal(ran.Length, reported.Length);

        for (var i = 0; i < ran.Length; i++)
        {
            var widths = ran[i].GetProperty("spreadMm");

            output.WriteLine(
                $"{reported[i].GetProperty("name").GetString(),-8} "
                + $"{reported[i].GetProperty("mode").GetString(),-11} "
                + $"ends {reported[i].GetProperty("endsAtUs").GetString(),8} us  "
                + $"x {reported[i].GetProperty("centroidMm").GetString(),9} "
                + $"+- {reported[i].GetProperty("axialSpreadMm").GetString()}, "
                + $"{reported[i].GetProperty("radialSpreadMm").GetString()} mm");

            Assert.Equal(
                ran[i].GetProperty("name").GetString(),
                reported[i].GetProperty("name").GetString());

            // The same number the run reported, to the four decimals the page shows. Not
            // "a positive width": this command's one property is that it cannot say
            // something the stored document does not.
            Assert.Equal(
                widths[0].GetDouble().ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                reported[i].GetProperty("axialSpreadMm").GetString());

            Assert.Equal(
                widths[1].GetDouble().ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                reported[i].GetProperty("radialSpreadMm").GetString());
        }

        // A DENSITY IS NOT A COUNT OF TRAJECTORIES, so the diffusive phase carries none -
        // absent rather than zero, which is this surface's rule for a quantity that has no
        // value. Zero beside a population of tens of thousands reads as an instrument that
        // lost everything, and that is RND-8's argument met on a number instead of on a
        // drawing.
        Assert.False(reported[0].TryGetProperty("trajectories", out _));
        Assert.True(reported[1].GetProperty("trajectories").GetInt32() > 0);

        // The conversion is marked on the phase it happened at, because the widths either
        // side of it are two measurements of one packet by two machineries.
        Assert.False(reported[0].GetProperty("converted").GetBoolean());
        Assert.True(reported[1].GetProperty("converted").GetBoolean());

        // And it reaches the page, which is the surface a person actually reads.
        Assert.Equal(0, Run("report", _root).ExitCode);

        var page = File.ReadAllText(Path.Combine(_root, "report.html"));

        Assert.Contains("The timeline it walked", page, StringComparison.Ordinal);
        Assert.Contains("axial width", page, StringComparison.Ordinal);
        Assert.Contains(
            reported[0].GetProperty("axialSpreadMm").GetString()!,
            page,
            StringComparison.Ordinal);
    }

    /// <summary>The manifest records every mode the run used (PRJ-3).</summary>
    /// <remarks>
    /// A manifest fully determines its run. Recording one mode for a run that used two
    /// would make it claim to determine a run it does not describe — and transport mode
    /// is one of the fields §14 names explicitly.
    /// </remarks>
    [Fact]
    public void TheManifestRecordsEveryModeTheRunUsed()
    {
        var json = JsonDocument.Parse(Run("run", Project(), "--json").Stdout).RootElement;

        var mode = json.GetProperty("manifest").GetProperty("transportMode").GetString();

        output.WriteLine(mode!);

        Assert.Contains("diffusion", mode!, StringComparison.Ordinal);
        Assert.Contains("trajectory", mode!, StringComparison.Ordinal);
    }
}
