namespace Einzel.Core.Errors;

/// <summary>
/// An exception carrying a structured <see cref="EinzelError"/>.
/// </summary>
/// <remarks>
/// The engine throws this rather than a bare exception type so that every layer
/// above it — command objects, CLI, MCP — can serialise the AGT-3 object without
/// reconstructing it by parsing a message string.
/// </remarks>
public class EinzelException : Exception
{
    /// <summary>Creates an exception carrying a structured error.</summary>
    /// <param name="error">The structured error.</param>
    public EinzelException(EinzelError error)
        : base(error?.ToString() ?? throw new ArgumentNullException(nameof(error)))
    {
        Error = error;
        Errors = [error];
    }

    /// <summary>Creates an exception carrying a structured error and an inner cause.</summary>
    /// <param name="error">The structured error.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EinzelException(EinzelError error, Exception? innerException)
        : base(error?.ToString() ?? throw new ArgumentNullException(nameof(error)), innerException)
    {
        Error = error;
        Errors = [error];
    }

    /// <summary>Creates an exception carrying every error a validation found.</summary>
    /// <param name="errors">The errors, in document order; at least one.</param>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is null or empty.</exception>
    /// <remarks>
    /// A command that validates and then refuses used to throw the first error alone, so a
    /// document with two mistakes was told about one, fixed it, and was then told about the
    /// second - two round trips where validation had already found both. The recovery an
    /// agent wants is the whole list, which is what <c>einzel validate</c> prints; a verb that
    /// validates on its way to doing something else should say no less.
    /// </remarks>
    public EinzelException(IReadOnlyList<EinzelError> errors)
        : this(errors is { Count: > 0 }
            ? errors[0]
            : throw new ArgumentException("an exception needs at least one error", nameof(errors)))
    {
        Errors = errors;
    }

    /// <summary>The structured error - the first, where there are several. Never <see langword="null"/>.</summary>
    public EinzelError Error { get; }

    /// <summary>Every error, in document order; <see cref="Error"/> is the first.</summary>
    public IReadOnlyList<EinzelError> Errors { get; }
}
