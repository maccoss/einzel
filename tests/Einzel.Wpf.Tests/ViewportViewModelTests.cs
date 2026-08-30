using System.IO;

using Einzel.Commands;
using Einzel.Wpf;

using Xunit.Abstractions;

namespace Einzel.Wpf.Tests;

/// <summary>
/// The viewport's own decisions: the colour scale, and what it says when there is
/// nothing to draw.
/// </summary>
/// <remarks>
/// The shell computes nothing about ions — <see cref="ViewportCommand"/> does, and its
/// tests are in Einzel.Cli.Tests, which runs on Linux. What is left here is presentation,
/// and it has two hazards in it worth pinning: a degenerate colour range, and an empty
/// viewport that says nothing about why it is empty.
/// </remarks>
public sealed class ViewportViewModelTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-shell-viewport", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Example(string name)
    {
        Assert.Equal(0, Einzel.Cli.Program.Main(["init", _root]));

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Einzel.Cli.Program.Main(["new", path, "--from-example", name]));

        return path;
    }

    private static ViewportViewModel Over(string modelPath) =>
        new(new ShellSession(modelPath, new JournalAuthor("test", AuthorKind.Human)));

    /// <summary>A bundle is offered with a scale spanning all of it.</summary>
    [Fact]
    public void ABundleComesWithAScaleSpanningIt()
    {
        var viewport = Over(Example("single-stage-reflectron"));

        Assert.True(viewport.Refresh());

        output.WriteLine(viewport.Status);

        Assert.True(viewport.HasBundle);
        Assert.NotEmpty(viewport.Trajectories);

        Assert.Equal(0.0, viewport.Fraction(viewport.LowestEnergyEv));
        Assert.Equal(1.0, viewport.Fraction(viewport.HighestEnergyEv));
    }

    /// <summary>A degenerate range gives a colour, not a division (§16).</summary>
    /// <remarks>
    /// <para>
    /// A packet whose ions all carry the same energy has a range of zero width, and that
    /// is not an exotic case: it is a monoenergetic beam in a field-free drift, the
    /// simplest model anyone writes. A scale that divided by the width would paint the
    /// whole bundle NaN, which is the same family as the four non-finite doubles that
    /// took the JSON surface down one at a time.
    /// </para>
    /// <para>
    /// Half rather than zero or one, because there is no top or bottom of a scale with no
    /// width, and either end would read as a statement about the energy that is not being
    /// made.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADegenerateRangeGivesAColourNotADivision()
    {
        var viewport = Over(Example("single-stage-reflectron"));

        // Nothing refreshed, so both ends are zero: the state the viewport is in before
        // it has been given anything, and the state a one-energy bundle would put it in.
        Assert.Equal(0.5, viewport.Fraction(0.0));
        Assert.Equal(0.5, viewport.Fraction(4000.0));

        Assert.All(
            new[] { 0.0, 4000.0, double.NaN },
            e =>
            {
                var (r, g, b) = ColourRamp.At(viewport.Fraction(e));

                Assert.All(new[] { r, g, b }, c => Assert.True(double.IsFinite(c)));
            });
    }

    /// <summary>A diffusive model offers no paths and says what it has instead (RND-8).</summary>
    /// <remarks>
    /// An empty viewport and one whose ions were all lost look identical, and only one of
    /// them is a statement about the physics. The reason has to be on the face of the
    /// window rather than inferrable from its being blank.
    /// </remarks>
    [Fact]
    public void ADiffusiveModelSaysWhatItHasInsteadOfPaths()
    {
        var viewport = Over(Example("drift-tube-diffusion"));

        Assert.False(viewport.Refresh());

        output.WriteLine(viewport.Status);

        foreach (var warning in viewport.Warnings)
        {
            output.WriteLine($"  {warning}");
        }

        Assert.False(viewport.HasBundle);
        Assert.Empty(viewport.Trajectories);

        Assert.Contains("density", viewport.Status, StringComparison.Ordinal);
        Assert.Contains(viewport.Warnings, w => w.StartsWith("render.no-trajectories", StringComparison.Ordinal));
    }

    /// <summary>Looking at the model is journalled as a command (Amendment 25).</summary>
    /// <remarks>
    /// Every shell action must be expressible as a CLI invocation. A viewport that
    /// acquired its own private path to the engine would be the moment that breaks, and
    /// it would look like a convenience at the time.
    /// </remarks>
    [Fact]
    public void LookingAtTheModelIsJournalledAsACommand()
    {
        var session = new ShellSession(
            Example("single-stage-reflectron"), new JournalAuthor("test", AuthorKind.Human));

        new ViewportViewModel(session).Refresh();

        foreach (var action in session.Actions)
        {
            output.WriteLine(action.Command);
        }

        Assert.Contains(
            session.Actions,
            a => a.Command.StartsWith("einzel render section ", StringComparison.Ordinal));
    }

    /// <summary>The ramp is ordered and stays inside the unit cube.</summary>
    /// <remarks>
    /// <para>
    /// <b>Monotone lightness is the property the choice was made for.</b> A rainbow ramp
    /// — the default almost everywhere — has non-monotone lightness, so it invents
    /// boundaries where the data is smooth and collapses under the commonest colour
    /// vision deficiencies. Asserting it here is what stops the ramp being "improved" into
    /// one.
    /// </para>
    /// <para>
    /// Luminance by the Rec. 709 weights, which is what an eye does rather than what a
    /// mean of the channels does.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRampRisesInLightnessThroughout()
    {
        var previous = double.NegativeInfinity;

        for (var i = 0; i <= 64; i++)
        {
            var (r, g, b) = ColourRamp.At(i / 64.0);

            Assert.All(new[] { r, g, b }, c => Assert.InRange(c, 0.0, 1.0));

            var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

            Assert.True(
                luminance > previous,
                $"lightness fell at {i / 64.0:F3}: {luminance:F4} after {previous:F4}");

            previous = luminance;
        }

        output.WriteLine($"lightness rises to {previous:F4} over 65 samples");

        // Out of range is clamped rather than refused: the scale is a display decision and
        // a value past its end is still a value to draw.
        Assert.Equal(ColourRamp.At(0.0), ColourRamp.At(-5.0));
        Assert.Equal(ColourRamp.At(1.0), ColourRamp.At(5.0));
    }
}
