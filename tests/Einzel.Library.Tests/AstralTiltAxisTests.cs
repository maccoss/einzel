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
    /// <summary>The four foil plates: two either side of the mid-plane, each doubled in y.</summary>
    private static readonly string[] ExpectedFoilPlates =
        ["foilFarAbove", "foilFarBelow", "foilNearAbove", "foilNearBelow"];

    private static IReadOnlyList<Electrode3DDocument> Electrodes()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        var solve = document.Fields!.Single().Solve3d;

        Assert.NotNull(solve);

        return solve!.Electrodes!;
    }

    /// <summary>The mirror stacks, which are the electrodes these assertions are about.</summary>
    /// <remarks>
    /// <para>
    /// Scoped by name rather than taken as "all electrodes", because the model also carries
    /// the ion foil, which is deliberately <em>not</em> tilted — its shape varies in the
    /// plane of the drift instead. Asserting over everything was right while the mirrors were
    /// the only electrodes and became wrong the moment something else was added.
    /// </para>
    /// <para>
    /// <b>The count is asserted, so the scoping cannot quietly empty the set.</b> A rename
    /// that no longer matched would leave these tests passing over nothing, which is the
    /// vacuous truth this project has met four times.
    /// </para>
    /// </remarks>
    private static List<Electrode3DDocument> Mirrors()
    {
        var mirrors = Electrodes()
            .Where(e => e.Name!.StartsWith("near", StringComparison.Ordinal)
                || e.Name!.StartsWith("far", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(16, mirrors.Count);

        return mirrors;
    }

    /// <summary>Every mirror electrode is rotated about y, which is what tilts a mirror.</summary>
    [Fact]
    public void TheMirrorsAreTiltedAboutY()
    {
        var electrodes = Mirrors();

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
        var electrodes = Mirrors();

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

        // Not merely opposite: the direction matters, and "opposite" is exactly the assertion a
        // global sign flip passes. In Electrode3D.ToLocal a box tilted by +t about y has its faces
        // at x = x0 - (z - zc) tan(pi t) - moving toward -x as z increases - so the near (low-x)
        // stack must carry +mirrorTilt and the far -mirrorTilt for both mouths to approach the
        // mid-plane along the drift. The signs were the other way round until 2026-09-01 and the
        // mirrors diverged; the artefact of section 12 hid it. Handoff section 12.
        Assert.Equal("mirrorTilt", a);
        Assert.Equal("-mirrorTilt", b);
    }

    /// <summary>Adjacent mirror strips are separated by a vacuum gap, and the outer faces are not.</summary>
    /// <remarks>
    /// Load-bearing rather than cosmetic: a tilt about y moves only the edges between strips, and
    /// an edge with metal on both sides has no cut-cell representation, so without a gap of at
    /// least one cell the solver cannot see the convergence at all - measured at 0.447 of the
    /// specular kick abutting against 1.045 gapped. The mouth and cap faces are metal-to-vacuum
    /// already and must not be moved, or the mirror depth changes. Handoff section 12.
    /// </remarks>
    [Fact]
    public void AdjacentStripsAreSeparatedByAGapAndTheOuterFacesAreNot()
    {
        foreach (var e in Mirrors())
        {
            var m = System.Text.RegularExpressions.Regex.Match(e.Name!, "^(near|far)([1-4])");
            var near = m.Groups[1].Value == "near";
            var k = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var minX = e.MinX!.Expression!;
            var maxX = e.MaxX!.Expression!;

            // interior faces carry the gap; the mouth (near k=1 max, far k=1 min) and the cap
            // (near k=4 min, far k=4 max) do not.
            var minIsInterior = near ? k <= 3 : k >= 2;
            var maxIsInterior = near ? k >= 2 : k <= 3;
            Assert.Equal(minIsInterior, minX.Contains("stripGap", StringComparison.Ordinal));
            Assert.Equal(maxIsInterior, maxX.Contains("stripGap", StringComparison.Ordinal));
        }
    }

    /// <summary>The ion foil is four plates, contoured along the drift, and untilted.</summary>
    /// <remarks>
    /// <para>
    /// Four because the published schematic shows two plates straddling the analyser
    /// mid-plane in the mirror-oscillation direction, and the ions oscillate straight
    /// through those positions — so each must be duplicated above and below the ion plane
    /// in the board gap for the packet to pass between them.
    /// </para>
    /// <para>
    /// Untilted because the foil is shaped by where its inner edge sits along the drift,
    /// not by a gap taper: a taper inside a channel this long swings the on-axis potential
    /// by 0.0003 V against a requirement of 2.9 to 3.7 V.
    /// </para>
    /// <para>
    /// Asserted so that scoping the mirror tests by name cannot become a way of quietly
    /// dropping electrodes out of any check at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFoilIsPresentAndIsNotTilted()
    {
        var foils = Electrodes()
            .Where(e => e.Name!.StartsWith("foil", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(4, foils.Count);

        // Two either side of the mid-plane in x, each duplicated above and below in y.
        Assert.Equal(
            ExpectedFoilPlates,
            foils.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        foreach (var foil in foils)
        {
            Assert.True(
                string.IsNullOrEmpty(foil.TiltAxis),
                $"{foil.Name} declares a tilt about '{foil.TiltAxis}'. The foil is shaped by "
                + "where its inner edge sits along the drift, not by a gap taper - a taper "
                + "inside a channel this long swings the on-axis potential by 0.0003 V");

            Assert.NotNull(foil.Repeat);
        }

        // Every electrode is either a mirror or a foil: nothing has fallen out of both.
        Assert.Equal(Electrodes().Count, Mirrors().Count + foils.Count);
    }
}
