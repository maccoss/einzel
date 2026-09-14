using Einzel.Core.Errors;

namespace Einzel.Core.Tests;

/// <summary>
/// An error raised where the location is unknown is located by whoever caught it.
/// </summary>
/// <remarks>
/// <para>
/// AGT-3 makes an error a recovery instruction: a machine-readable code, the offending
/// path, the violated constraint, the observed value and a suggested correction. A path
/// of <c>/</c> satisfies the letter and none of the point.
/// </para>
/// <para>
/// It happens because arithmetic cannot know where it is. A dimension mismatch is raised
/// inside <c>Quantity</c>, which has two operands and no document, so it names the root
/// as a placeholder; the frame that catches it was resolving a named parameter or a
/// numbered electrode's face and does know. Measured on a real document before the fix:
/// a thickness written without a unit gave <b>64 identical errors, every one located at
/// <c>/</c></b> - naming neither which electrode nor which face, and indistinguishable
/// from what a single mistake would print. After it, each names its own face, down to
/// the repeat index.
/// </para>
/// </remarks>
public sealed class ErrorLocationTests
{
    private static EinzelError Raised(string path) => new()
    {
        Code = ErrorCodes.UnitsIncompatible,
        Path = path,
        Constraint = "cannot add quantities of dimension m and 1",
    };

    [Fact]
    public void APlaceholderPathIsFilledInByTheCatcher() =>
        Assert.Equal(
            "/fields/2/solve3d/electrodes/0/repeat/7/maxY",
            Raised("/").At("/fields/2/solve3d/electrodes/0/repeat/7/maxY").Path);

    /// <summary>
    /// A path that is already specific survives, which is the half that keeps this safe.
    /// </summary>
    /// <remarks>
    /// Locating an error must never make it <em>less</em> located. An expression
    /// evaluator is handed the path it is working on and raises its own failures with it,
    /// so a catcher one frame out - which knows only the enclosing element - would
    /// otherwise coarsen every one of them on the way past.
    /// </remarks>
    [Fact]
    public void APathThatIsAlreadySpecificIsNotOverwritten() =>
        Assert.Equal(
            "/parameters/bendRadius",
            Raised("/parameters/bendRadius").At("/fields/0").Path);

    /// <summary>Nothing else about the error moves.</summary>
    [Fact]
    public void OnlyTheLocationChanges()
    {
        var raised = Raised("/") with
        {
            Observed = new ObservedValue(2.0, "1"),
            Suggestion = "supply the right-hand operand with dimension m",
            Severity = ErrorSeverity.Error,
        };

        var located = raised.At("/parameters/astray");

        Assert.Equal(raised with { Path = "/parameters/astray" }, located);
    }
}
