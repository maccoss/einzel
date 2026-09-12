using System.Text.Json;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A diffusive run says how many cells its packet is wide (REG-2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by a study rather than by a test, which is why it is worth one.</b> Every study
/// model in this project declares 256 intervals in x. On the shipped TIMS analyser's own
/// operating point that is 2.7 cells across the packet, and refining to 512 and 1024 moved
/// the arrival width from 133.52 to 114.55 to 107.31 us - an observed order of 1.39
/// extrapolating to 102.8. So the published resolving powers are low by about 30 per cent,
/// and nothing anywhere said the mesh was the reason.
/// </para>
/// <para>
/// <b>The elution voltage is converged at all three</b> - 21.119, 21.141, 21.149 V, a spread
/// of 0.14 per cent - so the error is in the width alone. That is what the note reports
/// rather than a general caution: a reader can tell whether the figure they care about is
/// the one at risk.
/// </para>
/// </remarks>
public sealed class PacketResolutionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-packet-resolution", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
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

    /// <summary>Runs the corpus drift tube at a chosen axial mesh.</summary>
    private (string Message, bool UnderResolved) Run(int intervalsX)
    {
        var path = Path.Combine(_root, "models", $"tube-{intervalsX}.json");

        if (!File.Exists(Path.Combine(_root, "models", "drift-tube-diffusion.json")))
        {
            Assert.Equal(0, Cli("init", _root).ExitCode);
            Assert.Equal(0, Cli(
                "new", Path.Combine(_root, "models", "drift-tube-diffusion.json"),
                "--from-example", "drift-tube-diffusion").ExitCode);
        }

        var text = File.ReadAllText(
            Path.Combine(_root, "models", "drift-tube-diffusion.json"));

        // The replacement is asserted, because a test that edits a file and does not check
        // the edit is a test that can silently stop testing anything - and neither mesh here
        // is the example's own 128, because an identity replacement satisfies the assertion
        // while changing nothing. This project has recorded that exact trap once already.
        var edited = text.Replace(
            "\"intervalsX\": 128", $"\"intervalsX\": {intervalsX}", StringComparison.Ordinal);

        Assert.NotEqual(text, edited);
        File.WriteAllText(path, edited);

        var (exit, stdout, _) = Cli("run", path, "--json", "--progress", "0");

        Assert.Equal(0, exit);

        var warnings = JsonDocument.Parse(stdout).RootElement
            .GetProperty("flightTime").GetProperty("warnings").EnumerateArray().ToList();

        var note = warnings.Single(w =>
            w.GetProperty("code").GetString() == "diffusion.packet-resolution");

        return (note.GetProperty("message").GetString()!,
            warnings.Any(w =>
                w.GetProperty("code").GetString() == "diffusion.packet-under-resolved"));
    }

    [Fact]
    public void ACoarseMeshIsCalledOutAndAFineOneIsNot()
    {
        var coarse = Run(64);
        var fine = Run(512);

        output.WriteLine("coarse: " + coarse.Message);
        output.WriteLine("fine:   " + fine.Message);

        // THE DISCRIMINATOR. A note on every run is REG-2; a violation only where the mesh
        // cannot carry the width is what makes the note worth reading rather than skimming.
        Assert.True(coarse.UnderResolved, "64 intervals is about one and a half cells across the packet");
        Assert.False(fine.UnderResolved, "512 intervals is about twelve");
    }

    [Fact]
    public void TheCountScalesWithTheMesh()
    {
        // Eight times the intervals is eight times the cells across the same packet.
        // Asserted as a ratio rather than as two values, because the packet's own width is
        // the model's business and only the count is this check's.
        var coarse = Cells(Run(64).Message);
        var fine = Cells(Run(512).Message);

        output.WriteLine($"{coarse:F1} cells at 64 intervals, {fine:F1} at 512");

        Assert.Equal(8.0, fine / coarse, 0);
    }

    [Fact]
    public void AnEmptiedRunMeasuresItsSeedAndSaysSo()
    {
        // THE DEFECT THIS GUARD EXISTS FOR, and it shipped in the first draft. The corpus
        // drift tube collects almost everything, leaving a sliver against the collecting
        // face - and a second moment over that is the width of a residue, not of a packet.
        // It read 0.3 cells where the seed is 3.0. The same shape this project has met twice
        // before: contouring a density orders below one ion, and reading an elution onset off
        // a Boltzmann tail.
        var message = Run(64).Message;

        output.WriteLine(message);

        Assert.Contains("the seed alone", message, StringComparison.Ordinal);
        Assert.Contains("residue", message, StringComparison.Ordinal);
    }

    private static double Cells(string message)
    {
        // "the packet is 3.0 cells wide"
        var words = message.Split(' ');
        var at = Array.IndexOf(words, "cells");

        return double.Parse(words[at - 1], System.Globalization.CultureInfo.InvariantCulture);
    }
}
