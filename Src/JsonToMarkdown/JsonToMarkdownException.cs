namespace JsonToMarkdown;

/// <summary>Machine-readable classification of a conversion failure.</summary>
public enum JsonToMarkdownErrorCode
{
    /// <summary>The serialized JSON text was not well-formed.</summary>
    InvalidJsonSyntax,

    /// <summary>An object contained the same member name twice.</summary>
    DuplicateMemberName,

    /// <summary>A caller-supplied parsed value was not JSON-compatible.</summary>
    InvalidParsedValue,

    /// <summary>A parsed value graph contained a cycle.</summary>
    CyclicReference,

    /// <summary>A parsed array contained a hole (cannot occur from System.Text.Json input).</summary>
    SparseArray,
}

/// <summary>A position in the serialized source, counted in UTF-16 code units to match the reference implementations.</summary>
/// <param name="Offset">Zero-based UTF-16 code-unit index.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Column">One-based UTF-16 code-unit position within the line.</param>
public readonly record struct SourceLocation(int Offset, int Line, int Column);

/// <summary>
/// Thrown when a document cannot be converted. Conversion fails atomically at the
/// first error in encounter order and never returns partial Markdown.
/// </summary>
public sealed class JsonToMarkdownException : Exception
{
    /// <summary>The machine-readable error classification.</summary>
    public JsonToMarkdownErrorCode Code { get; }

    /// <summary>JSON Pointer to the offending value, when locatable.</summary>
    public string? Pointer { get; }

    /// <summary>Source location, for serialized-syntax failures.</summary>
    public SourceLocation? Location { get; }

    /// <summary>First occurrence, reported alongside <see cref="JsonToMarkdownErrorCode.DuplicateMemberName"/>.</summary>
    public SourceLocation? FirstLocation { get; }

    internal JsonToMarkdownException(
        JsonToMarkdownErrorCode code,
        string message,
        string? pointer = null,
        SourceLocation? location = null,
        SourceLocation? firstLocation = null)
        : base(message)
    {
        Code = code;
        Pointer = pointer;
        Location = location;
        FirstLocation = firstLocation;
    }
}
