using System.Text;

namespace JsonToMarkdown;

/// <summary>
/// Internal representation shared by both entry points.
///
/// Both <see cref="JsonMarkdown.ConvertText"/> and <see cref="JsonMarkdown.ConvertValue(System.Text.Json.Nodes.JsonNode?)"/>
/// normalise their input into this form so that a single renderer produces the
/// Output Document. A number keeps its Numeric Lexeme as text: the serialized
/// path stores the original token, the parsed path stores the host-formatted value.
/// </summary>
internal abstract class DocNode
{
}

internal sealed class NullNode : DocNode
{
    internal static readonly NullNode Instance = new();
    private NullNode() { }
}

internal sealed class BooleanNode : DocNode
{
    internal static readonly BooleanNode True = new(true);
    internal static readonly BooleanNode False = new(false);
    internal bool Value { get; }
    private BooleanNode(bool value) => Value = value;
    internal static BooleanNode Of(bool value) => value ? True : False;
}

internal sealed class NumberNode : DocNode
{
    internal string Lexeme { get; }
    internal NumberNode(string lexeme) => Lexeme = lexeme;
}

internal sealed class StringNode : DocNode
{
    internal string Value { get; }
    internal StringNode(string value) => Value = value;
}

internal sealed class ArrayNode : DocNode
{
    internal IReadOnlyList<DocNode> Items { get; }
    internal ArrayNode(IReadOnlyList<DocNode> items) => Items = items;
}

internal sealed class ObjectNode : DocNode
{
    /// <summary>Member entries in encounter order; duplicate names are rejected upstream.</summary>
    internal IReadOnlyList<KeyValuePair<string, DocNode>> Entries { get; }
    internal ObjectNode(IReadOnlyList<KeyValuePair<string, DocNode>> entries) => Entries = entries;
}

internal static class Document
{
    internal static bool IsContainer(DocNode node) => node is ArrayNode or ObjectNode;

    internal static bool IsEmptyContainer(DocNode node) => node switch
    {
        ArrayNode a => a.Items.Count == 0,
        ObjectNode o => o.Entries.Count == 0,
        _ => false,
    };

    /// <summary>A value shown inline (as a paragraph or list-item scalar) rather than as its own structure.</summary>
    internal static bool RendersInline(DocNode node) => !IsContainer(node) || IsEmptyContainer(node);

    /// <summary>Zero-padded four-digit hex for a <c>\uXXXX</c> escape.</summary>
    internal static string Hex4(int code) => code.ToString("x4");

    /// <summary>RFC 6901 pointer segment escaping.</summary>
    internal static string PointerSegment(string name) => name.Replace("~", "~0").Replace("/", "~1");

    internal static string ChildPointer(string pointer, string name)
    {
        var sb = new StringBuilder(pointer.Length + name.Length + 1);
        sb.Append(pointer).Append('/').Append(PointerSegment(name));
        return sb.ToString();
    }
}
