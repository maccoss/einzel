using Einzel.Core.Model;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The Astral's converging mirrors, as two oppositely rotated cross-sections.
/// </summary>
/// <remarks>
/// <para>
/// The template used to declare a tilt on sixteen three-dimensional boxes and solve the
/// tilted geometry. That cannot work at any affordable mesh - the whole signal is the field
/// anisotropy <c>Ez/Ex = tan(alpha)</c>, 2.9e-4 at the published convergence, which is well
/// below the field error of a second-order solve - and measured against the closed form it
/// returned anywhere from -0.57 to +3.54 of the truth depending on the width of the gaps
/// between mirror strips. See docs/astral-handoff.md sections 12 and 13.
/// </para>
/// <para>
/// What replaced it is exact: each mirror is a cross-section whose extrusion axis is
/// rotated, one by <c>+mirrorTilt</c> and the other by <c>-mirrorTilt</c>. The load-bearing
/// property is the one the last test here asserts - each element carries exactly one mirror
/// at potential with the other grounded, which makes the pair the ordinary basis
/// decomposition <c>phi = sum_k V_k psi_k</c> and therefore exact rather than approximate.
/// Both mirrors live in one element, or one mirror missing from the other, and it silently
/// stops being that.
/// </para>
/// </remarks>
public sealed class AstralMirrorDecompositionTests(ITestOutputHelper output)
{
    private static IReadOnlyList<FieldDocument> Elements()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        Assert.NotNull(document.Fields);
        return document.Fields!;
    }

    private static SolvedFieldDocument Solve(FieldDocument f)
    {
        Assert.Equal("solved2d", f.Type);
        Assert.NotNull(f.Solve);
        return f.Solve!;
    }

    /// <summary>Two cross-sections, and no three-dimensional solve anywhere.</summary>
    [Fact]
    public void TheMirrorsAreTwoCrossSectionsAndNotAVolumeSolve()
    {
        var elements = Elements();
        Assert.Equal(3, elements.Count);   // two mirror cross-sections plus the foil

        foreach (var f in elements.Take(2))
        {
            Assert.Null(f.Solve3d);
            var solve = Solve(f);
            Assert.Equal(16, solve.Electrodes!.Count);

            // The tilt belongs to the extrusion axis, not to the electrodes. An electrode
            // tilt here would mean the geometry is being rotated again, which is the thing
            // that did not work.
            Assert.All(solve.Electrodes!, e => Assert.Null(e.Repeat));
        }
    }

    /// <summary>The two extrusion axes are rotated oppositely, and in the right sense.</summary>
    /// <remarks>
    /// Asserted as exact strings rather than as "opposite", because "opposite" is precisely
    /// what a global sign flip satisfies - and the signs were inverted for weeks, which made
    /// the mirrors diverge along the drift while an unrelated discretisation artefact
    /// supplied a convergence-shaped answer. The near stack is at low x and takes
    /// <c>+mirrorTilt</c>; in <c>ToLocal</c> a box rotated by <c>+t</c> about y has its
    /// faces at <c>x = x0 - (z - zc) tan(pi t)</c>, so the near mouth moves toward +x and
    /// the far toward -x as z increases, which is convergence.
    /// </remarks>
    [Fact]
    public void TheTwoExtrusionAxesAreRotatedOppositelyAndInTheRightSense()
    {
        var elements = Elements();
        var near = Solve(elements[0]);
        var far = Solve(elements[1]);

        Assert.Equal("mirrorTilt", near.TiltHalfTurns?.Expression);
        Assert.Equal("-mirrorTilt", far.TiltHalfTurns?.Expression);

        // Both rotate about the same axis, or they are not one instrument.
        foreach (var solve in new[] { near, far })
        {
            Assert.Equal("midPlane", solve.TiltCentreX?.Expression);
            Assert.Equal("driftLength / 2", solve.TiltCentreZ?.Expression);
        }
    }

    /// <summary>
    /// Each element carries exactly one mirror at potential, with the other grounded.
    /// </summary>
    /// <remarks>
    /// This is what makes the pair exact. Two elements each holding both mirrors at
    /// potential would double the field; two each holding only their own would omit the
    /// other's grounded metal from the basis solve. Neither would announce itself - both
    /// produce a plausible converging analyser - so the decomposition is asserted rather
    /// than trusted.
    /// </remarks>
    [Fact]
    public void EachElementCarriesOneMirrorLiveAndTheOtherGrounded()
    {
        var elements = Elements();

        foreach (var (index, live, dead) in new[] { (0, "near", "far"), (1, "far", "near") })
        {
            var solve = Solve(elements[index]);
            var liveCount = 0;

            foreach (var e in solve.Electrodes!)
            {
                var expression = e.Potential?.Expression;
                Assert.NotNull(expression);

                if (e.Name!.StartsWith(live, StringComparison.Ordinal))
                {
                    liveCount++;
                    Assert.Contains("ionEnergy", expression!, StringComparison.Ordinal);
                }
                else
                {
                    Assert.StartsWith(dead, e.Name!, StringComparison.Ordinal);
                    Assert.Equal("0", expression);
                }
            }

            output.WriteLine($"element {index}: {liveCount} of {solve.Electrodes!.Count} live ({live})");
            Assert.Equal(8, liveCount);   // four stages, two boards
        }
    }

    /// <summary>
    /// The ion foil is present as a third element, graded along the drift, with every
    /// mirror strip grounded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The foil does NOT supply the drift reversal - the mirror tilt alone does, which this
    /// document once concluded otherwise and the detector paper settles. What the foil is
    /// for is countering the time-of-flight aberration the converging mirrors induce, and
    /// the profile that does that is not known here. It ships at zero bias.
    /// </para>
    /// <para>
    /// The mirror strips must be present and <b>grounded</b>, or the element is not the
    /// basis field <c>psi_foil</c> at all. And the bias must ship at <b>zero</b>, so that
    /// the reversal this template reproduces is unambiguously the tilt's and not a foil
    /// contribution standing in for a geometry error - which is exactly the mistake that
    /// produced the superseded sections 14 and 15 of the handoff.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFoilIsAThirdElementGradedAlongTheDriftWithTheMirrorsGrounded()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        var elements = document.Fields!;
        Assert.Equal(3, elements.Count);

        var foilElement = elements[2];
        Assert.Equal("solved3d", foilElement.Type);
        var solve = foilElement.Solve3d;
        Assert.NotNull(solve);

        var plates = solve!.Electrodes!
            .Where(e => e.Name!.StartsWith("foil", StringComparison.Ordinal)).ToList();
        var grounded = solve!.Electrodes!
            .Where(e => !e.Name!.StartsWith("foil", StringComparison.Ordinal)).ToList();

        Assert.Equal(4, plates.Count);
        Assert.Equal(16, grounded.Count);
        Assert.All(grounded, e => Assert.Equal(0.0, e.Potential?.Value));
        Assert.All(plates, e => Assert.Contains("foilGrade", e.Potential!.Expression!, StringComparison.Ordinal));

        // Shipped at zero bias, so the foil contributes exactly nothing and the drift
        // reversal is the mirror tilt alone - which is what the detector paper says does
        // it. Zero is inside the published 0 to -20 V range. The geometry is kept because
        // countering the mirrors' time-of-flight aberration is the foil's published job and
        // the profile that does it is an unrun optimisation.
        Assert.Equal(0.0, document.Parameters!["foilVolts"].Value);

        output.WriteLine($"foil: {plates.Count} plates graded, {grounded.Count} mirror strips grounded");
    }

    /// <summary>The strip gap the tilted geometry needed is gone.</summary>
    /// <remarks>
    /// Nothing is rotated in the geometry any more - the tilt is a property of the field -
    /// so abutting strips cost nothing and the gap that once had to be at least a cell wide
    /// is not a parameter of this device.
    /// </remarks>
    [Fact]
    public void TheStripGapIsGone()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        Assert.DoesNotContain("stripGap", document.Parameters!.Keys);
        Assert.All(
            Elements().Take(2),
            f => Assert.DoesNotContain(
                Solve(f).Electrodes!,
                e => e.Name!.StartsWith("foil", StringComparison.Ordinal)));
    }
}
