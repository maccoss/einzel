namespace Einzel.Core.Errors;

/// <summary>
/// Where an error about a command-line flag is located: <c>/args/--flag</c>, the one spelling
/// every verb uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stated once, in the layer that raises the error.</b> The command layer had located flag
/// errors at pointers of its own - <c>/surface/at-us</c>, <c>/probe/at-us</c>,
/// <c>/render/at-us</c> - and the CLI translated only the surface's, so one mistake,
/// <c>--at-us -1</c>, was located three different ways by three verbs, and an export's was
/// located at the probe's flag because it borrowed the probe's check. AGT-3 makes the path the
/// part of an error a caller acts on, so a path that depends on which verb raised it is a path a
/// caller cannot rely on.
/// </para>
/// <para>
/// A value read from a document - a render spec's <c>atSeconds</c>, a study's <c>scan</c> - is
/// located in that document, not here: the path says where to go and change it.
/// </para>
/// </remarks>
public static class FlagPath
{
    /// <summary>The pointer for a flag.</summary>
    /// <param name="flag">The flag, without its dashes.</param>
    /// <returns><c>/args/--</c> and the flag.</returns>
    public static string Of(string flag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);

        return "/args/--" + flag;
    }
}
