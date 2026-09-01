using Einzel.Core.Model;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The Astral's mirrors are tilted about y, and a rotation about x is a different device.
/// </summary>
/// <remarks>
/// <para>
/// An asymmetric-track analyser reverses its drift because the two mirrors converge along
/// it: a tilted mirror gives each reflection a z-impulse of <c>θ</c> times its x-impulse,
/// and the x-impulse is fixed at <c>2·m·v_x</c>, so <c>Δv_z = −2·v_x·θ</c> per reflection.
/// The mirror surfaces have their normals along <b>x</b>, so the rotation that tilts them
/// is about <b>y</b>.
/// </para>
/// <para>
/// <b>The template shipped with <c>tiltAxis: "x"</c> for months.</b> That rotation mixes y
/// and z, so it closed the gap between the two <i>boards</i> instead and left the mirror
/// normals exactly where they were — a model containing none of the mechanism it exists to
/// demonstrate. It was not obviously broken: converging boards also decelerate a drift,
/// through the transverse confinement stiffening, about three times more weakly. It needed
/// a 1.58 mm spacer where mirror convergence needs 0.27 against a published 0.200.
/// </para>
/// <para>
/// So this is a structural guard rather than a physical one. The physical test — that the
/// drift reverses at the published spacer — is minutes of volume solving and lives in
/// <c>docs/astral-handoff.md</c> as measurements. What can be asserted in milliseconds is
/// that nobody has quietly put the rotation back on the wrong axis.
/// </para>
/// </remarks>
public sealed class AstralTiltAxisTests(ITestOutputHelper output)
{
    private static IReadOnlyList<Electrode3DDocument> Electrodes()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        var solve = document.Fields!.Single().Solve3d;

        Assert.NotNull(solve);

        return solve!.Electrodes!;
    }

    /// <summary>Every mirror electrode is rotated about y, which is what tilts a mirror.</summary>
    [Fact]
    public void TheMirrorsAreTiltedAboutY()
    {
        var electrodes = Electrodes();

        Assert.NotEmpty(electrodes);

        foreach (var electrode in electrodes)
        {
            Assert.True(
                electrode.TiltAxis == "y",
                $"{electrode.Name} is tilted about '{electrode.TiltAxis}'. A rotation about x "
                + "mixes y and z, so it converges the boards and leaves the mirror normals - "
                + "which lie along x - untouched, giving a model with none of the drift "
                + "reversal this template exists to demonstrate");
        }

        output.WriteLine($"{electrodes.Count} electrodes, all tilted about y");
    }

    /// <summary>The near and far stacks lean opposite ways, or they do not converge.</summary>
    /// <remarks>
    /// <b>The sign is the whole mechanism and it is measurable.</b> One sign shortens the
    /// transit — the mirrors diverge and the drift accelerates — and the other lengthens it
    /// and reverses the drift. Two mirrors leaning the same way are parallel, and parallel
    /// mirrors cannot reverse a drift however they are tuned.
    /// </remarks>
    [Fact]
    public void TheNearAndFarStacksLeanOppositeWays()
    {
        var electrodes = Electrodes();

        var near = electrodes.Where(e => e.Name!.StartsWith("near", StringComparison.Ordinal)).ToList();
        var far = electrodes.Where(e => e.Name!.StartsWith("far", StringComparison.Ordinal)).ToList();

        // Both halves must exist, or "they differ" is vacuously true over an empty set -
        // the failure this project has met four times.
        Assert.NotEmpty(near);
        Assert.NotEmpty(far);
        Assert.Equal(electrodes.Count, near.Count + far.Count);

        var nearTilts = near.Select(e => e.TiltHalfTurns?.Expression).Distinct().ToList();
        var farTilts = far.Select(e => e.TiltHalfTurns?.Expression).Distinct().ToList();

        output.WriteLine($"near ({near.Count}): {string.Join(", ", nearTilts)}");
        output.WriteLine($"far  ({far.Count}): {string.Join(", ", farTilts)}");

        // Each stack leans as a unit - a mirror is one object, not a stack of independently
        // tilted rings.
        Assert.Single(nearTilts);
        Assert.Single(farTilts);

        var a = nearTilts[0];
        var b = farTilts[0];

        Assert.NotNull(a);
        Assert.NotNull(b);

        // Opposite signs of the same parameter: "-mirrorTilt" against "mirrorTilt".
        Assert.True(
            a!.TrimStart('-') == b!.TrimStart('-') && a.StartsWith('-') != b.StartsWith('-'),
            $"the near stack is tilted by '{a}' and the far by '{b}'. They must be opposite "
            + "signs of one parameter, or the two mirrors do not converge and the drift "
            + "cannot reverse");
    }
}
