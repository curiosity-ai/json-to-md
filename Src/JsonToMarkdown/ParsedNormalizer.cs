using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonToMarkdown;

/// <summary>
/// Validates caller-trusted, already-parsed data (a System.Text.Json object model)
/// and normalises it into a <see cref="DocNode"/>. Iterative to satisfy the
/// no-nesting-limit contract: never relies on the call stack. Numeric values keep
/// their original JSON token when the value was parsed from text (System.Text.Json
/// preserves the raw lexeme), matching <see cref="JsonMarkdown.ConvertText"/>.
/// </summary>
internal static class ParsedNormalizer
{
    private static Exception Invalid(string pointer, string what) =>
        new JsonToMarkdownException(
            JsonToMarkdownErrorCode.InvalidParsedValue,
            $"Value at {(pointer.Length == 0 ? "the root" : pointer)} is not JSON-compatible: {what}.",
            pointer: pointer);

    // ----- JsonNode entry point -----

    internal static DocNode Normalize(JsonNode? root)
    {
        DocNode? rootScalar = TryScalarNode(root, "");
        if (rootScalar is not null) return rootScalar;

        var onPath = new HashSet<JsonNode>(ReferenceEqualityComparer.Instance) { root! };
        var stack = new List<NodeFrame> { MakeNodeFrame(root!, "") };
        DocNode? finished = null;

        while (stack.Count > 0)
        {
            NodeFrame frame = stack[^1];

            if (frame.Index >= frame.Children.Count)
            {
                DocNode node = frame.IsArray
                    ? new ArrayNode(frame.Results)
                    : new ObjectNode(frame.Entries);
                onPath.Remove(frame.Source);
                stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0) finished = node;
                else if (stack[^1].IsArray) stack[^1].Results.Add(node);
                else stack[^1].Entries.Add(new KeyValuePair<string, DocNode>(frame.KeyForParent!, node));
                continue;
            }

            int i = frame.Index++;
            string key = frame.IsArray ? i.ToString(CultureInfo.InvariantCulture) : frame.Keys![i];
            string childPtr = Document.ChildPointer(frame.Pointer, key);
            JsonNode? childValue = frame.Children[i];

            DocNode? scalar = TryScalarNode(childValue, childPtr);
            if (scalar is not null)
            {
                if (frame.IsArray) frame.Results.Add(scalar);
                else frame.Entries.Add(new KeyValuePair<string, DocNode>(key, scalar));
                continue;
            }

            JsonNode childObj = childValue!;
            if (!onPath.Add(childObj))
            {
                throw new JsonToMarkdownException(JsonToMarkdownErrorCode.CyclicReference, $"Cyclic reference at {childPtr}.", pointer: childPtr);
            }
            stack.Add(MakeNodeFrame(childObj, childPtr, frame.IsArray ? null : key));
        }

        return finished!;
    }

    private static DocNode? TryScalarNode(JsonNode? value, string pointer)
    {
        if (value is null) return NullNode.Instance;
        switch (value.GetValueKind())
        {
            case JsonValueKind.Null:
                return NullNode.Instance;
            case JsonValueKind.True:
                return BooleanNode.True;
            case JsonValueKind.False:
                return BooleanNode.False;
            case JsonValueKind.String:
                return new StringNode(value.GetValue<string>());
            case JsonValueKind.Number:
                return new NumberNode(NumberLexemeFromNode((JsonValue)value));
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return null; // descend
            default:
                throw Invalid(pointer, "an unsupported JSON value");
        }
    }

    private static string NumberLexemeFromNode(JsonValue value)
    {
        if (value.TryGetValue(out JsonElement el)) return el.GetRawText();
        // Programmatically-constructed value: format the CLR number invariantly.
        if (value.TryGetValue(out double d)) return d.ToString("R", CultureInfo.InvariantCulture);
        if (value.TryGetValue(out decimal m)) return m.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue(out long l)) return l.ToString(CultureInfo.InvariantCulture);
        return value.ToJsonString();
    }

    private sealed class NodeFrame
    {
        internal required JsonNode Source;
        internal required string Pointer;
        internal required bool IsArray;
        internal required string? KeyForParent;
        internal int Index;
        internal required IReadOnlyList<JsonNode?> Children;
        internal required IReadOnlyList<string>? Keys;
        internal readonly List<DocNode> Results = new();
        internal readonly List<KeyValuePair<string, DocNode>> Entries = new();
    }

    private static NodeFrame MakeNodeFrame(JsonNode value, string pointer, string? keyForParent = null)
    {
        if (value is JsonArray arr)
        {
            var arrChildren = new List<JsonNode?>(arr.Count);
            foreach (JsonNode? item in arr) arrChildren.Add(item);
            return new NodeFrame
            {
                Source = value,
                Pointer = pointer,
                IsArray = true,
                KeyForParent = keyForParent,
                Children = arrChildren,
                Keys = null,
            };
        }
        var obj = (JsonObject)value;
        var keys = new List<string>(obj.Count);
        var children = new List<JsonNode?>(obj.Count);
        foreach (var kvp in obj)
        {
            keys.Add(kvp.Key);
            children.Add(kvp.Value);
        }
        return new NodeFrame
        {
            Source = value,
            Pointer = pointer,
            IsArray = false,
            KeyForParent = keyForParent,
            Children = children,
            Keys = keys,
        };
    }

    // ----- JsonElement entry point -----

    internal static DocNode Normalize(JsonElement root)
    {
        DocNode? rootScalar = TryScalarElement(root);
        if (rootScalar is not null) return rootScalar;

        var stack = new List<ElementFrame> { MakeElementFrame(root, "") };
        DocNode? finished = null;

        while (stack.Count > 0)
        {
            ElementFrame frame = stack[^1];

            if (frame.Index >= frame.Children.Count)
            {
                DocNode node = frame.IsArray
                    ? new ArrayNode(frame.Results)
                    : new ObjectNode(frame.Entries);
                stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0) finished = node;
                else if (stack[^1].IsArray) stack[^1].Results.Add(node);
                else stack[^1].Entries.Add(new KeyValuePair<string, DocNode>(frame.KeyForParent!, node));
                continue;
            }

            int i = frame.Index++;
            string key = frame.IsArray ? i.ToString(CultureInfo.InvariantCulture) : frame.Keys![i];
            JsonElement childValue = frame.Children[i];

            DocNode? scalar = TryScalarElement(childValue);
            if (scalar is not null)
            {
                if (frame.IsArray) frame.Results.Add(scalar);
                else frame.Entries.Add(new KeyValuePair<string, DocNode>(key, scalar));
                continue;
            }

            stack.Add(MakeElementFrame(childValue, Document.ChildPointer(frame.Pointer, key), frame.IsArray ? null : key));
        }

        return finished!;
    }

    private static DocNode? TryScalarElement(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => NullNode.Instance,
        JsonValueKind.Undefined => NullNode.Instance,
        JsonValueKind.True => BooleanNode.True,
        JsonValueKind.False => BooleanNode.False,
        JsonValueKind.String => new StringNode(value.GetString()!),
        JsonValueKind.Number => new NumberNode(value.GetRawText()),
        _ => null,
    };

    private sealed class ElementFrame
    {
        internal required string Pointer;
        internal required bool IsArray;
        internal required string? KeyForParent;
        internal int Index;
        internal required IReadOnlyList<JsonElement> Children;
        internal required IReadOnlyList<string>? Keys;
        internal readonly List<DocNode> Results = new();
        internal readonly List<KeyValuePair<string, DocNode>> Entries = new();
    }

    private static ElementFrame MakeElementFrame(JsonElement value, string pointer, string? keyForParent = null)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var children = new List<JsonElement>();
            foreach (JsonElement item in value.EnumerateArray()) children.Add(item);
            return new ElementFrame
            {
                Pointer = pointer,
                IsArray = true,
                KeyForParent = keyForParent,
                Children = children,
                Keys = null,
            };
        }
        var keys = new List<string>();
        var objChildren = new List<JsonElement>();
        foreach (JsonProperty prop in value.EnumerateObject())
        {
            keys.Add(prop.Name);
            objChildren.Add(prop.Value);
        }
        return new ElementFrame
        {
            Pointer = pointer,
            IsArray = false,
            KeyForParent = keyForParent,
            Children = objChildren,
            Keys = keys,
        };
    }
}
