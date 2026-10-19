using System.Globalization;
using System.Text.RegularExpressions;
using Einzel.Core.Errors;
using Einzel.Core.Numerics;
using Einzel.Core.Units;

namespace Einzel.Core.Tests.Numerics;

/// <summary>
/// The arithmetic a volume mesh is refused by, and the cell size the refusal suggests.
/// </summary>
/// <remarks>
/// <para>
/// A mesh too large to solve was an argument exception deep in the solver, reported by the CLI
/// as a defect in the engine, after <c>validate</c> had passed the model and <c>estimate</c>
/// had priced it. It is now refused from the document, which needs the grid's rounding rule
/// and the limit available below the solver - here.
/// </para>
/// <para>
/// <b>The suggestion is the part with traps in it.</b> The finest fitting cell size sits
/// exactly on a power-of-two boundary, so printing it naively can land a rounding below the
/// boundary and name the refused mesh again, or round far enough up to cross a second
/// boundary and name a mesh with half the nodes. Both are tested with boxes chosen to spring
/// them.
/// </para>
/// </remarks>
public sealed class VolumeMeshTests
{
    private static double Mm(double value) => Quantity.From(value, "mm").SiValue;

    /// <summary>The rule the grid has always used: up to a power of two, at least two.</summary>
    [Theory]
    [InlineData(10.0, 1.0, 16L)]
    [InlineData(8.0, 1.0, 8L)]
    [InlineData(8.5, 1.0, 16L)]
    [InlineData(1.0, 1.0, 2L)]
    [InlineData(0.1, 1.0, 2L)]
    [InlineData(635.0, 1.0, 1024L)]
    public void IntervalsRoundUpToAPowerOfTwoAndNeverBelowTwo(double span, double cell, long expected) =>
        Assert.Equal(expected, VolumeMesh.Intervals(span, cell));

    /// <summary>A picometer over a meter saturates rather than overflowing.</summary>
    /// <remarks>
    /// The doubling this replaced was an <see cref="int"/>, and past 2^30 it wrapped to zero
    /// and never terminated. That was harmless while only a solve called it; a validator that
    /// counts nodes calls it on every document, and would have hung on this one.
    /// </remarks>
    [Fact]
    public void AnAbsurdCellSizeSaturatesRatherThanOverflowing()
    {
        Assert.Equal(1L << 62, VolumeMesh.Intervals(1.0, 1e-300));
        Assert.Equal(long.MaxValue, VolumeMesh.Nodes(1.0, 1.0, 1.0, 1e-12));
    }

    /// <summary>
    /// The finest fitting cell size is what a brute-force scan of cell sizes finds.
    /// </summary>
    /// <remarks>
    /// Checked two ways against a scan in steps of one part in ten thousand: the answer fits,
    /// anything a hair finer does not, and the first scanned size that fits lies within one
    /// step above it. A small limit, so every box here crosses it at a handful of cells.
    /// </remarks>
    [Theory]
    [InlineData(10.0, 3.0, 7.0, 0.05, 20_000L)]
    [InlineData(635.0, 48.0, 350.0, 0.2, 5_000_000L)]
    [InlineData(1.0, 1.0, 1.0, 0.001, 100_000L)]
    [InlineData(22.0, 5000.0, 5000.0, 2.7, 64_000_000L)]
    public void TheFinestFittingCellIsWhatAScanFinds(
        double spanX, double spanY, double spanZ, double requested, long limit)
    {
        var finest = VolumeMesh.FinestFittingCell(spanX, spanY, spanZ, requested, limit);

        Assert.True(VolumeMesh.Nodes(spanX, spanY, spanZ, finest) <= limit);

        if (finest > requested)
        {
            Assert.True(
                VolumeMesh.Nodes(spanX, spanY, spanZ, finest * (1.0 - 1e-9)) > limit,
                "a size a hair finer than the answer also fits, so it is not the finest");
        }

        var scanned = requested;

        while (VolumeMesh.Nodes(spanX, spanY, spanZ, scanned) > limit)
        {
            scanned *= 1.0001;
        }

        Assert.InRange(scanned, finest, finest * 1.0001 * (1.0 + 1e-12));
    }

    /// <summary>
    /// The printed suggestion, typed back into a document, gives the mesh it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The box is chosen so the obvious printing is wrong.</b> Spanning -1.5 to 20.5 mm, its
    /// eight-interval boundary is exactly 2.75 mm - and 2.75 mm converted through the unit
    /// gives 22 mm over it of 8.000000000000002, which rounds up to sixteen intervals and is
    /// the refused mesh. Found by searching boxes for one where that happens, because an
    /// assertion on a box where it does not happen cannot tell a checked suggestion from an
    /// unchecked one.
    /// </para>
    /// <para>
    /// The other two axes are sized to put the whole box just over the limit at the request
    /// and just under it at the boundary, so the node limit that decides is the real one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASuggestionOnAnExactDecimalBoundaryIsCheckedNotTrusted()
    {
        var spanX = Mm(20.5) - Mm(-1.5);
        var spanYz = Mm(5000.0);
        var requested = Mm(2.7);

        // The trap exists on this box, or the test has stopped testing anything.
        Assert.True(Math.Ceiling(spanX / Mm(2.75)) > 8.0);

        var (x, y, z) = VolumeMesh.Counts(spanX, spanYz, spanYz, requested);
        Assert.True(VolumeMesh.Nodes(spanX, spanYz, spanYz, requested) > VolumeMesh.MaximumNodes);

        var error = VolumeMesh.Refusal("/fields/0/solve3d/cellSize", x, y, z, spanX, spanYz, spanYz, requested, "mm");
        var suggested = Suggested(error);
        var finest = VolumeMesh.FinestFittingCell(spanX, spanYz, spanYz, requested);

        Assert.Equal(
            VolumeMesh.Counts(spanX, spanYz, spanYz, finest),
            VolumeMesh.Counts(spanX, spanYz, spanYz, Mm(suggested)));

        Assert.True(suggested > 2.75, $"suggested {suggested} mm");
    }

    /// <summary>
    /// A suggestion is printed with as many digits as it needs, not three.
    /// </summary>
    /// <remarks>
    /// The Astral's own box at 1 mm: two boundaries 0.27 per cent apart - 750 mm over 512 on z
    /// and 752 mm over 512 on x - so rounding the finer up to three figures, 1.47 mm, crosses the
    /// coarser as well and names a mesh with half the nodes, while calling it the finest that
    /// fits. The first version said exactly that.
    /// </remarks>
    [Fact]
    public void ASuggestionDoesNotRoundPastASecondBoundary()
    {
        double spanX = Mm(752.0), spanY = Mm(61.43), spanZ = Mm(750.0), requested = Mm(1.0);

        var (x, y, z) = VolumeMesh.Counts(spanX, spanY, spanZ, requested);
        var error = VolumeMesh.Refusal("/", x, y, z, spanX, spanY, spanZ, requested, "mm");
        var suggested = Suggested(error);

        Assert.Equal((1025L, 65L, 513L), VolumeMesh.Counts(spanX, spanY, spanZ, Mm(suggested)));
        Assert.Contains("1025 x 65 x 513", error.Suggestion, StringComparison.Ordinal);
        Assert.Equal(1.465, suggested);
    }

    /// <summary>The refusal is an AGT-3 error: code, path, the count, the limit, a correction.</summary>
    [Fact]
    public void TheRefusalNamesTheCountTheLimitAndTheMesh()
    {
        double spanX = Mm(752.0), spanY = Mm(61.43), spanZ = Mm(750.0), requested = Mm(1.0);
        var (x, y, z) = VolumeMesh.Counts(spanX, spanY, spanZ, requested);

        var error = VolumeMesh.Refusal("/fields/2/solve3d/cellSize", x, y, z, spanX, spanY, spanZ, requested, "mm");

        Assert.Equal(ErrorCodes.GridTooLarge, error.Code);
        Assert.Equal("/fields/2/solve3d/cellSize", error.Path);
        Assert.Equal(ErrorSeverity.Error, error.Severity);
        Assert.Equal(1025.0 * 65 * 1025, error.Observed!.Value);
        Assert.Equal("nodes", error.Observed.Unit);
        Assert.Contains("64,000,000", error.Constraint, StringComparison.Ordinal);
        Assert.Contains("1025 x 65 x 1025", error.Constraint, StringComparison.Ordinal);
    }

    /// <summary>The suggestion is in the unit the document wrote, so it can be pasted back.</summary>
    [Fact]
    public void TheSuggestionIsInTheDocumentsOwnUnit()
    {
        double span = 0.4, requested = 1e-3;
        var (x, y, z) = VolumeMesh.Counts(span, span, span, requested);

        var inMicrons = VolumeMesh.Refusal("/", x, y, z, span, span, span, requested, "um");
        var unitless = VolumeMesh.Refusal("/", x, y, z, span, span, span, requested, "V");

        Assert.Matches(" um is the finest", inMicrons.Suggestion!);
        Assert.Matches(" mm is the finest", unitless.Suggestion!);
    }

    /// <summary>A suggestion that nothing accepts is reported as such, never printed unchecked.</summary>
    /// <remarks>
    /// The printing search used to return the value it was given when no rounded candidate
    /// passed the check - so the one path where the check failed was the one path that printed
    /// a number without it. Two spans an ulp apart can make that window too narrow for any
    /// decimal; the refusal then falls back to a size that is checked to fit.
    /// </remarks>
    [Fact]
    public void APrintingSearchThatFindsNothingSaysSo()
    {
        Assert.Null(VolumeMesh.PrintableAtLeast(1.4648, _ => false));
        Assert.Equal(1.47, VolumeMesh.PrintableAtLeast(1.4648, value => value >= 1.47));
    }

    /// <summary>The largest cube that fits is a power of two, and 256 at the present limit.</summary>
    /// <remarks>
    /// 257 cubed is 17 M nodes and 513 cubed is 135 M, and nothing between rounds to anything
    /// else, since each side rounds up to a power of two.
    /// </remarks>
    [Fact]
    public void TheLargestCubeThatFitsIsTwoHundredAndFiftySixAcross()
    {
        var most = VolumeMesh.MostNodesAcrossACube;

        Assert.Equal(256, most);
        Assert.True(VolumeMesh.Nodes(2.0, 2.0, 2.0, 2.0 / most) <= VolumeMesh.MaximumNodes);
        Assert.True(VolumeMesh.Nodes(2.0, 2.0, 2.0, 2.0 / (most + 1)) > VolumeMesh.MaximumNodes);
    }

    private static double Suggested(EinzelError error)
    {
        var match = Regex.Match(error.Suggestion!, @"a cell size of ([0-9.Ee+-]+) mm");

        Assert.True(match.Success, error.Suggestion);

        return double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
