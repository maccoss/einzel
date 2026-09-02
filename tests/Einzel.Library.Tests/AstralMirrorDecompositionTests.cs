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
        Assert.Equal(2, elements.Count);

        foreach (var f in elements)
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
    /// The ion foil is deliberately absent, and so is the strip gap it needed.
    /// </summary>
    /// <remarks>
    /// The foil is not invariant along the drift, so a cross-section cannot hold it, and a
    /// grounded foil's shadowing of the mirror field is therefore missing from this model.
    /// That effect measured -185 m/s per reflection, 5.7 times too strong to be the
    /// published mechanism, so its absence is a stated limitation rather than a regression.
    /// Asserted so that its return is a deliberate act: the foil belongs back as a third,
    /// three-dimensional element, not as more rectangles in these cross-sections.
    /// </remarks>
    [Fact]
    public void TheFoilAndTheStripGapAreBothGone()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));

        Assert.DoesNotContain("stripGap", document.Parameters!.Keys);
        Assert.All(
            Elements(),
            f => Assert.DoesNotContain(
                Solve(f).Electrodes!,
                e => e.Name!.StartsWith("foil", StringComparison.Ordinal)));
    }
}
