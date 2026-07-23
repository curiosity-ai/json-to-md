using System.Collections.Generic;

namespace JsonToMarkdown;

/// <summary>
/// Parse exactly one JSON document into a <see cref="DocNode"/> without executing
/// caller code. Iterative to satisfy the no-nesting-limit contract; preserves
/// numeric lexemes, member encounter order, and rejects duplicate member names.
/// Operates on UTF-16 code units (C# <see cref="char"/>), matching the reference
/// implementation's offsets and locations exactly.
/// </summary>
internal static class SerializedParser
{
    private static SourceLocation LocationAt(string src, int offset)
    {
        int line = 1;
        int column = 1;
        int end = Math.Min(offset, src.Length);
        for (int i = 0; i < end; i++)
        {
            if (src[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return new SourceLocation(offset, line, column);
    }

    private static Exception SyntaxError(string src, string message, int offset) =>
        new JsonToMarkdownException(JsonToMarkdownErrorCode.InvalidJsonSyntax, message, location: LocationAt(src, offset));

    private static bool IsWs(char c) => c is ' ' or '\t' or '\n' or '\r';

    private static int SkipWs(string src, int pos)
    {
        int n = src.Length;
        while (pos < n && IsWs(src[pos])) pos++;
        return pos;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private readonly struct Scanned
    {
        internal readonly DocNode Node;
        internal readonly int End;
        internal Scanned(DocNode node, int end)
        {
            Node = node;
            End = end;
        }
    }

    private static (string Value, int End) ParseString(string src, int pos)
    {
        int n = src.Length;
        int i = pos + 1;
        int start = i;
        System.Text.StringBuilder? outb = null;

        while (i < n)
        {
            char c = src[i];
            if (c == '"')
            {
                if (outb is null) return (src.Substring(start, i - start), i + 1);
                outb.Append(src, start, i - start);
                return (outb.ToString(), i + 1);
            }
            if (c == '\\')
            {
                outb ??= new System.Text.StringBuilder();
                outb.Append(src, start, i - start);
                i++;
                char e = i < n ? src[i] : '\0';
                switch (e)
                {
                    case '"': outb.Append('"'); break;
                    case '\\': outb.Append('\\'); break;
                    case '/': outb.Append('/'); break;
                    case 'b': outb.Append('\b'); break;
                    case 'f': outb.Append('\f'); break;
                    case 'n': outb.Append('\n'); break;
                    case 'r': outb.Append('\r'); break;
                    case 't': outb.Append('\t'); break;
                    case 'u':
                    {
                        int hexStart = i + 1;
                        if (hexStart + 4 > n || !TryParseHex4(src, hexStart, out int code))
                        {
                            throw SyntaxError(src, "Invalid \\u escape in string", i - 1);
                        }
                        outb.Append((char)code);
                        i += 4;
                        break;
                    }
                    default:
                        throw SyntaxError(src, "Invalid escape sequence in string", i - 1);
                }
                i++;
                start = i;
                continue;
            }
            if (c < 0x20)
            {
                throw SyntaxError(src, "Unescaped control character in string", i);
            }
            i++;
        }
        throw SyntaxError(src, "Unterminated string", pos);
    }

    private static bool TryParseHex4(string src, int start, out int value)
    {
        value = 0;
        for (int k = 0; k < 4; k++)
        {
            char c = src[start + k];
            int d;
            if (c >= '0' && c <= '9') d = c - '0';
            else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
            else return false;
            value = (value << 4) | d;
        }
        return true;
    }

    private static Scanned ParseNumber(string src, int pos)
    {
        int n = src.Length;
        int i = pos;
        if (src[i] == '-') i++;

        char first = i < n ? src[i] : '\0';
        if (first == '0')
        {
            i++;
        }
        else if (first >= '1' && first <= '9')
        {
            i++;
            while (i < n && IsDigit(src[i])) i++;
        }
        else
        {
            throw SyntaxError(src, "Invalid number", pos);
        }

        if (i < n && src[i] == '.')
        {
            i++;
            if (!(i < n && IsDigit(src[i]))) throw SyntaxError(src, "Invalid number: missing fraction digits", i);
            while (i < n && IsDigit(src[i])) i++;
        }

        char exp = i < n ? src[i] : '\0';
        if (exp is 'e' or 'E')
        {
            i++;
            char sign = i < n ? src[i] : '\0';
            if (sign is '+' or '-') i++;
            if (!(i < n && IsDigit(src[i]))) throw SyntaxError(src, "Invalid number: missing exponent digits", i);
            while (i < n && IsDigit(src[i])) i++;
        }

        return new Scanned(new NumberNode(src.Substring(pos, i - pos)), i);
    }

    private static Scanned ParseScalar(string src, int pos)
    {
        char c = src[pos];
        if (c == '"')
        {
            var s = ParseString(src, pos);
            return new Scanned(new StringNode(s.Value), s.End);
        }
        if (c == '-' || IsDigit(c)) return ParseNumber(src, pos);
        if (Matches(src, pos, "true")) return new Scanned(BooleanNode.True, pos + 4);
        if (Matches(src, pos, "false")) return new Scanned(BooleanNode.False, pos + 5);
        if (Matches(src, pos, "null")) return new Scanned(NullNode.Instance, pos + 4);
        throw SyntaxError(src, "Unexpected token; expected a JSON value", pos);
    }

    private static bool Matches(string src, int pos, string literal)
    {
        if (pos + literal.Length > src.Length) return false;
        for (int k = 0; k < literal.Length; k++)
        {
            if (src[pos + k] != literal[k]) return false;
        }
        return true;
    }

    private abstract class Frame;

    private sealed class ObjFrame : Frame
    {
        internal readonly List<KeyValuePair<string, DocNode>> Entries = new();
        internal readonly Dictionary<string, int> Seen = new(StringComparer.Ordinal);
        internal int State; // 0 start, 1 value, 2 comma
        internal string Key = "";
    }

    private sealed class ArrFrame : Frame
    {
        internal readonly List<DocNode> Items = new();
        internal int State; // 0 start, 1 value
    }

    internal static DocNode Parse(string source)
    {
        int n = source.Length;
        var stack = new List<Frame>();
        DocNode? pending = null;
        bool hasPending = false;
        int pos = SkipWs(source, 0);

        if (pos >= n) throw SyntaxError(source, "Unexpected end of input; expected a JSON value", pos);

        pos = StartValue(source, pos, stack, ref pending, ref hasPending);

        for (; ; )
        {
            Frame? frame = stack.Count > 0 ? stack[^1] : null;

            if (frame is null)
            {
                pos = SkipWs(source, pos);
                if (pos != n) throw SyntaxError(source, "Unexpected trailing content after JSON value", pos);
                return pending!;
            }

            if (frame is ArrFrame arr)
            {
                if (hasPending)
                {
                    arr.Items.Add(pending!);
                    hasPending = false;
                    pending = null;
                    arr.State = 1;
                    continue;
                }
                pos = SkipWs(source, pos);
                char c = pos < n ? source[pos] : '\0';
                if (arr.State == 0)
                {
                    if (c == ']')
                    {
                        pos++;
                        CloseFrame(stack, ref pending, ref hasPending);
                        continue;
                    }
                    pos = StartValue(source, pos, stack, ref pending, ref hasPending);
                    continue;
                }
                // state == value
                if (c == ',')
                {
                    pos = StartValue(source, pos + 1, stack, ref pending, ref hasPending);
                    continue;
                }
                if (c == ']')
                {
                    pos++;
                    CloseFrame(stack, ref pending, ref hasPending);
                    continue;
                }
                throw SyntaxError(source, "Expected ',' or ']' in array", pos);
            }
            else
            {
                var obj = (ObjFrame)frame;
                if (hasPending)
                {
                    obj.Entries.Add(new KeyValuePair<string, DocNode>(obj.Key, pending!));
                    hasPending = false;
                    pending = null;
                    obj.State = 1;
                    continue;
                }
                pos = SkipWs(source, pos);
                char c = pos < n ? source[pos] : '\0';
                if (obj.State == 0)
                {
                    if (c == '}')
                    {
                        pos++;
                        CloseFrame(stack, ref pending, ref hasPending);
                        continue;
                    }
                    pos = ReadMember(source, pos, obj, stack, ref pending, ref hasPending);
                    continue;
                }
                if (obj.State == 1)
                {
                    if (c == ',')
                    {
                        obj.State = 2;
                        pos++;
                        continue;
                    }
                    if (c == '}')
                    {
                        pos++;
                        CloseFrame(stack, ref pending, ref hasPending);
                        continue;
                    }
                    throw SyntaxError(source, "Expected ',' or '}' in object", pos);
                }
                // state == comma: expect the next member key.
                pos = ReadMember(source, pos, obj, stack, ref pending, ref hasPending);
            }
        }
    }

    private static int StartValue(string source, int from, List<Frame> stack, ref DocNode? pending, ref bool hasPending)
    {
        int n = source.Length;
        int p = SkipWs(source, from);
        if (p >= n) throw SyntaxError(source, "Unexpected end of input; expected a JSON value", p);
        char c = source[p];
        if (c == '{')
        {
            stack.Add(new ObjFrame());
            return p + 1;
        }
        if (c == '[')
        {
            stack.Add(new ArrFrame());
            return p + 1;
        }
        var scanned = ParseScalar(source, p);
        pending = scanned.Node;
        hasPending = true;
        return scanned.End;
    }

    private static void CloseFrame(List<Frame> stack, ref DocNode? pending, ref bool hasPending)
    {
        Frame frame = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        pending = frame is ArrFrame a
            ? new ArrayNode(a.Items)
            : new ObjectNode(((ObjFrame)frame).Entries);
        hasPending = true;
    }

    private static int ReadMember(string source, int pos, ObjFrame frame, List<Frame> stack, ref DocNode? pending, ref bool hasPending)
    {
        int p = SkipWs(source, pos);
        if (p >= source.Length || source[p] != '"') throw SyntaxError(source, "Expected a string object key", p);
        int keyStart = p;
        var key = ParseString(source, p);
        if (frame.Seen.TryGetValue(key.Value, out int prev))
        {
            throw new JsonToMarkdownException(
                JsonToMarkdownErrorCode.DuplicateMemberName,
                $"Duplicate object member name {Quote(key.Value)}.",
                location: LocationAt(source, keyStart),
                firstLocation: LocationAt(source, prev));
        }
        frame.Seen[key.Value] = keyStart;
        frame.Key = key.Value;
        p = SkipWs(source, key.End);
        if (p >= source.Length || source[p] != ':') throw SyntaxError(source, "Expected ':' after object key", p);
        return StartValue(source, p + 1, stack, ref pending, ref hasPending);
    }

    /// <summary>Minimal JSON-style quoting for the duplicate-name message (matches JSON.stringify of a string).</summary>
    private static string Quote(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
