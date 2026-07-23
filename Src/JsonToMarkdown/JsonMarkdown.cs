using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonToMarkdown;

/// <summary>
/// Converts one JSON document into deterministic, human-readable
/// <see href="https://github.github.com/gfm/">GitHub Flavored Markdown</see>.
///
/// <para>
/// Output is a readable <em>projection</em>, not a reversible serialization. Every
/// document begins with a <c># Results</c> heading and uses canonical spacing: LF
/// endings, one blank line between blocks, no trailing spaces, one final newline.
/// </para>
///
/// <para>
/// There are two entry points. <see cref="ConvertText"/> parses untrusted serialized
/// JSON text with a dedicated parser that preserves numeric lexemes and member order
/// and reports UTF-16 source locations — its output is byte-identical to the reference
/// TypeScript and Go implementations. <see cref="ConvertValue(JsonNode?)"/> and its
/// overloads accept an already-parsed System.Text.Json object model.
/// </para>
///
/// All conversions are pure, never mutate their input, share no state, and are safe to
/// run concurrently. On invalid input they throw <see cref="JsonToMarkdownException"/>.
/// </summary>
public static class JsonMarkdown
{
    /// <summary>
    /// Convert untrusted <b>serialized</b> JSON text into a deterministic GFM document.
    /// Parses without executing caller code, preserves each number's original spelling,
    /// keeps object-member and array order intact, and rejects duplicate member names.
    /// </summary>
    /// <param name="source">The serialized JSON text (a single JSON document).</param>
    /// <returns>The complete Markdown document.</returns>
    /// <exception cref="JsonToMarkdownException">The text was not a single well-formed JSON document.</exception>
    public static string ConvertText(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Renderer.RenderDocument(SerializedParser.Parse(source));
    }

    /// <summary>
    /// Convert an already-parsed System.Text.Json value into a deterministic GFM document.
    /// A <see langword="null"/> node renders as JSON <c>null</c>.
    /// </summary>
    /// <param name="value">The parsed value, e.g. from <see cref="JsonNode.Parse(string, JsonNodeOptions?, JsonDocumentOptions)"/>.</param>
    /// <returns>The complete Markdown document.</returns>
    /// <exception cref="JsonToMarkdownException">The value graph was not JSON-compatible (e.g. a cycle).</exception>
    public static string ConvertValue(JsonNode? value) => Renderer.RenderDocument(ParsedNormalizer.Normalize(value));

    /// <summary>
    /// Convert an already-parsed <see cref="JsonElement"/> (e.g. from <see cref="JsonDocument"/>)
    /// into a deterministic GFM document.
    /// </summary>
    /// <param name="value">The parsed element.</param>
    /// <returns>The complete Markdown document.</returns>
    public static string ConvertValue(JsonElement value) => Renderer.RenderDocument(ParsedNormalizer.Normalize(value));

    /// <summary>
    /// Read a JSON object model from an input string with System.Text.Json and convert it.
    /// Equivalent to parsing <paramref name="source"/> into a <see cref="JsonNode"/> and calling
    /// <see cref="ConvertValue(JsonNode?)"/>. System.Text.Json preserves the raw numeric token,
    /// so numbers keep their original spelling.
    /// </summary>
    /// <param name="source">The serialized JSON text.</param>
    /// <returns>The complete Markdown document.</returns>
    /// <exception cref="JsonException">The text was not valid JSON (thrown by System.Text.Json).</exception>
    /// <exception cref="JsonToMarkdownException">The parsed value graph was not JSON-compatible.</exception>
    public static string ConvertValueFromText(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        JsonNode? node = JsonNode.Parse(source);
        return ConvertValue(node);
    }
}
