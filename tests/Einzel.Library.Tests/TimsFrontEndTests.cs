using System.Text.Json.Nodes;

using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Library;
using Einzel.Transport.Diffusion;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The entrance funnel delivers a wide packet into the tunnel, and the gate in front of the
/// tunnel decides whether it gets in.
/// </summary>
/// <remarks>
/// <para>
/// The analyser template starts its packet inside the tunnel. The instrument does not: ions
/// arrive through a 50 mm funnel whose bore tapers from 26 mm to the tunnel's 8 mm, held off
/// the plates by an RF alternating plate to plate, and the fill-trap-ramp sequence Hernandez
/// draws opens and closes an entrance gate around them. What is asked here is whether the
/// funnel's effective wall actually delivers, which is a transmission question a
/// cross-section could not ask, and whether raising the gate keeps a packet out.
/// </para>
/// <para>
/// Both are asked against their controls. Delivery is measured with the funnel RF on and
/// off, because a funnel whose RF is doing nothing would deliver whatever the taper alone
/// does; the gate is measured open and closed on the same packet.
/// </para>
/// </remarks>
public sealed class TimsFrontEndTests(ITestOutputHelper output)
{
    /// <summary>The shipped front end as a single held fill, coarse enough for a test.</summary>
    private static (DiffusiveOutcome Outcome, CompiledModel Model) Fill(double funnelRfVolts, double gateVolts, double fillUs)
    {
        var document = JsonNode.Parse(DeviceTemplates.Read("tims-front-end"))!;

        document["parameters"]!["funnelRfAmplitude"]!["value"] = funnelRfVolts;
        document["parameters"]!["gatePotential"]!["value"] = gateVolts;
        document["transport"]!["maximumFlightTime"] = JsonNode.Parse($$$"""{ "value": {{{fillUs}}}, "unit": "us" }""");
        document["transport"]!["densityGrid"]!["intervalsX"] = 256;
        document["transport"]!["densityGrid"]!["intervalsY"] = 32;

        // The fill phase alone: the sequence's later phases are the analyser's own business.
        document.AsObject().Remove("sequence");

        var validation = ModelValidator.Validate(ModelJson.Parse(document.ToJsonString()));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : string.Join("; ", validation.Errors.Select(e => $"{e.Path}: {e.Constraint}")));

        var built = FieldAssembly.BuildReported(validation.Model!);
        var outcome = DiffusionRun.Execute(validation.Model!, built.Field, built.Warnings, scheme: StepScheme.Implicit, stepGain: 64.0);
        return (outcome, validation.Model!);
    }

    /// <summary>
    /// The shipped front end as shipped - funnel RF on, gate open - run once and shared, since
    /// both tests need it as their control and a 4 ms fill on this grid is minutes.
    /// </summary>
    private static readonly Lazy<(DiffusiveOutcome Outcome, CompiledModel Model)> Shipped =
        new(() => Fill(100.0, 0.0, 4000.0), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>How much of the density sits past the tunnel entrance, as a fraction of what was launched.</summary>
    private static double InsideTheTunnel(DiffusiveOutcome outcome)
    {
        var density = outcome.Result.Density;
        var inside = 0.0;

        for (var j = 0; j < density.Grid.CountY; j++)
        {
            var volume = density.CellVolume(j);

            for (var i = 0; i < density.Grid.CountX; i++)
            {
                if (density.Grid.X(i) > 0.0)
                {
                    inside += density[i, j] * volume;
                }
            }
        }

        return inside / outcome.Launched;
    }

    /// <summary>
    /// With the funnel's RF on, essentially the whole packet reaches the tunnel; with it off,
    /// a third of it ends on the last plates and the gate.
    /// </summary>
    [Fact]
    public void TheFunnelDeliversThePacketWhenItsRfIsOn()
    {
        var (on, _) = Shipped.Value;
        var (off, _) = Fill(0.0, 0.0, 4000.0);

        var survivedOn = on.Result.Remaining / on.Launched;
        var survivedOff = off.Result.Remaining / off.Launched;
        var insideOn = InsideTheTunnel(on);
        var (cx, cr) = on.Result.Density.Centroid();

        output.WriteLine($"funnel RF on:  {survivedOn:P3} survive, {insideOn:P2} inside the tunnel, centre x {cx * 1e3:F2} mm r {cr * 1e3:F2} mm");
        output.WriteLine($"funnel RF off: {survivedOff:P3} survive; lost on " + string.Join(", ",
            off.Result.Lost.Where(l => l.Value > 100).OrderByDescending(l => l.Value).Select(l => $"{l.Key} {l.Value:F0}")));
        output.WriteLine("warnings: " + string.Join(", ", on.Warnings.Select(w => w.Code)));

        Assert.True(survivedOn > 0.995, $"the funnel delivered only {survivedOn:P2} with its RF on");
        Assert.True(insideOn > 0.98, $"only {insideOn:P1} of the packet had reached the tunnel after 4 ms");
        // Delivered to the analyser's own balance point, and compressed to its RF's Boltzmann radius.
        Assert.Equal(21.10, cx * 1e3, 1.0);
        Assert.True(cr < 0.5e-3, $"the delivered packet's mean radius is {cr * 1e3:F2} mm, so the tunnel RF has not taken it");

        // The control: without the RF the taper alone loses a large part of the packet on the
        // narrow end of the funnel and the gate, and every loss is named.
        Assert.True(survivedOff < 0.8, $"the funnel delivered {survivedOff:P2} with its RF off, so the RF is not what delivers");
        Assert.Contains(off.Result.Lost, l => l.Key.StartsWith("funnelPlate", StringComparison.Ordinal) && l.Value > 1000.0);
    }

    /// <summary>A raised gate keeps the funnel's packet out of the tunnel.</summary>
    [Fact]
    public void TheGateKeepsThePacketOutWhenRaised()
    {
        var (open, _) = Shipped.Value;
        var (closed, _) = Fill(100.0, 30.0, 4000.0);

        var insideOpen = InsideTheTunnel(open);
        var insideClosed = InsideTheTunnel(closed);
        var survivedClosed = closed.Result.Remaining / closed.Launched;

        output.WriteLine($"gate at 0 V:  {insideOpen:P2} of the packet inside the tunnel after 4 ms");
        output.WriteLine($"gate at 30 V: {insideClosed:P3} inside; {survivedClosed:P2} still in flight; lost on " + string.Join(", ",
            closed.Result.Lost.Where(l => l.Value > 100).OrderByDescending(l => l.Value).Select(l => $"{l.Key} {l.Value:F0}")));

        Assert.True(insideOpen > 0.8, "the open gate let in too little for the closed one to be a control");
        Assert.True(insideClosed < 0.01, $"{insideClosed:P2} of the packet passed a gate at 30 V");
    }
}
