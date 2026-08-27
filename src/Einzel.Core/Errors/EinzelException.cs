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
    }

    /// <summary>Creates an exception carrying a structured error and an inner cause.</summary>
    /// <param name="error">The structured error.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EinzelException(EinzelError error, Exception? innerException)
        : base(error?.ToString() ?? throw new ArgumentNullException(nameof(error)), innerException)
    {
        Error = error;
    }

    /// <summary>The structured error. Never <see langword="null"/>.</summary>
    public EinzelError Error { get; }
}
