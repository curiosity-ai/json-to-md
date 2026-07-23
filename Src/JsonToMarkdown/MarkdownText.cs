using System.Text;

namespace JsonToMarkdown;

/// <summary>
/// String escaping and Scalar Value rendering. All routines operate on UTF-16
/// code units (C# <see cref="char"/>), matching the reference implementation.
/// </summary>
internal static class MarkdownText
{
    private const string EmptyString = "`\"\"`";

    /// <summary>
    /// Escape the character at <paramref name="i"/> for inline, one-line display:
    /// control characters become visible JSON-style escapes, valid surrogate pairs
    /// pass through, lone surrogates become visible, and Markdown/HTML metacharacters
    /// are neutralised. A literal space returns unchanged (callers handle space runs).
    /// Returns the escaped text and the number of code units consumed.
    /// </summary>
    private static (string Text, int Consumed) EscapeInlineChar(string str, int i)
    {
        int code = str[i];
        switch (code)
        {
            case 0x09: return ("\\t", 1);
            case 0x08: return ("\\b", 1);
            case 0x0c: return ("\\f", 1);
            case 0x0a: return ("\\n", 1);
            case 0x0d: return ("\\r", 1);
        }
        if (code < 0x20 || (code >= 0x7f && code <= 0x9f)) return ("\\u" + Document.Hex4(code), 1);

        if (code >= 0xd800 && code <= 0xdbff)
        {
            int next = i + 1 < str.Length ? str[i + 1] : 0;
            if (next >= 0xdc00 && next <= 0xdfff) return (str.Substring(i, 2), 2);
            return ("\\u" + Document.Hex4(code), 1);
        }
        if (code >= 0xdc00 && code <= 0xdfff) return ("\\u" + Document.Hex4(code), 1);

        return (char)code switch
        {
            '\\' => ("\\\\", 1),
            '`' => ("\\`", 1),
            '*' => ("\\*", 1),
            '_' => ("\\_", 1),
            '[' => ("\\[", 1),
            ']' => ("\\]", 1),
            '|' => ("\\|", 1),
            '~' => ("\\~", 1),
            '&' => ("&amp;", 1),
            '<' => ("&lt;", 1),
            '>' => ("&gt;", 1),
            _ => (str[i].ToString(), 1),
        };
    }

    /// <summary>
    /// Escape one line segment of a Literal String. Newlines are handled by the
    /// caller; this sees text with no <c>\n</c>/<c>\r</c>.
    /// </summary>
    private static string RenderSegment(string seg)
    {
        var outb = new StringBuilder();
        int n = seg.Length;
        int i = 0;
        bool atStart = true;

        while (i < n)
        {
            char code = seg[i];

            // Space runs: keep a single interior space, protect everything Markdown
            // would collapse (leading, trailing, additional repeats) with `&nbsp;`.
            if (code == ' ')
            {
                int j = i;
                while (j < n && seg[j] == ' ') j++;
                int runLen = j - i;
                bool interior = i > 0 && j < n;
                if (interior)
                {
                    outb.Append(' ');
                    for (int k = 0; k < runLen - 1; k++) outb.Append("&nbsp;");
                }
                else
                {
                    for (int k = 0; k < runLen; k++) outb.Append("&nbsp;");
                }
                i = j;
                atStart = false;
                continue;
            }

            // Block markers only bite at the very start of a segment.
            if (atStart)
            {
                if (code >= '0' && code <= '9')
                {
                    int j = i;
                    while (j < n)
                    {
                        char c = seg[j];
                        if (c >= '0' && c <= '9') j++;
                        else break;
                    }
                    char punct = j < n ? seg[j] : '\0';
                    if (j < n && (punct == '.' || punct == ')'))
                    {
                        outb.Append(seg, i, j - i).Append('\\').Append(punct);
                        i = j + 1;
                        atStart = false;
                        continue;
                    }
                }
                else if (code == '#' || code == '-' || code == '+')
                {
                    outb.Append('\\').Append(seg[i]);
                    i++;
                    atStart = false;
                    continue;
                }
            }

            var (text, consumed) = EscapeInlineChar(seg, i);
            outb.Append(text);
            i += consumed;
            atStart = false;
        }

        return outb.ToString();
    }

    /// <summary>
    /// Escape a string for one-line inline display (a Key Label or a JSON Pointer):
    /// the same space-run and metacharacter handling as a Literal String segment, but
    /// newlines stay as <c>\n</c>/<c>\r</c> so the text never leaves its line, and no
    /// block-start markers are escaped.
    /// </summary>
    internal static string EscapeInline(string str)
    {
        var outb = new StringBuilder();
        int n = str.Length;
        int i = 0;
        while (i < n)
        {
            if (str[i] == ' ')
            {
                int j = i;
                while (j < n && str[j] == ' ') j++;
                int runLen = j - i;
                bool interior = i > 0 && j < n;
                if (interior)
                {
                    outb.Append(' ');
                    for (int k = 0; k < runLen - 1; k++) outb.Append("&nbsp;");
                }
                else
                {
                    for (int k = 0; k < runLen; k++) outb.Append("&nbsp;");
                }
                i = j;
                continue;
            }
            var (text, consumed) = EscapeInlineChar(str, i);
            outb.Append(text);
            i += consumed;
        }
        return outb.ToString();
    }

    /// <summary>Render an untrusted string as visible Literal String text (newlines become <c>&lt;br&gt;</c>).</summary>
    internal static string EscapeText(string value)
    {
        string normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] segments = normalized.Split('\n');
        var outb = new StringBuilder();
        for (int s = 0; s < segments.Length; s++)
        {
            if (s > 0) outb.Append("<br>");
            outb.Append(RenderSegment(segments[s]));
        }
        return outb.ToString();
    }

    /// <summary>A string is a URL String only if its whole, unmodified content is one absolute http(s) URL.</summary>
    internal static bool IsUrlString(string value)
    {
        foreach (char c in value)
        {
            if (IsJsWhitespace(c)) return false;
        }
        if (value.Length < 7) return false;
        if (!(StartsWithIgnoreCase(value, "http://") || StartsWithIgnoreCase(value, "https://"))) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url)) return false;
        if (url.Scheme != "http" && url.Scheme != "https") return false;
        return url.Host.Length > 0;
    }

    private static bool StartsWithIgnoreCase(string value, string prefix)
    {
        if (value.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (char.ToLowerInvariant(value[i]) != prefix[i]) return false;
        }
        return true;
    }

    // Mirrors the code points matched by the JS regular-expression `\s` class.
    private static bool IsJsWhitespace(char c)
    {
        switch ((int)c)
        {
            case 0x09: // \t
            case 0x0a: // \n
            case 0x0b: // \v
            case 0x0c: // \f
            case 0x0d: // \r
            case 0x20: // space
            case 0xa0: // no-break space
            case 0x1680:
            case 0x2028: // line separator
            case 0x2029: // paragraph separator
            case 0x202f:
            case 0x205f:
            case 0x3000:
            case 0xfeff: // BOM / zero-width no-break space
                return true;
            default:
                return c >= 0x2000 && c <= 0x200a;
        }
    }

    /// <summary>Escape a link destination so it survives a table cell.</summary>
    private static string EscapeDestination(string url)
    {
        var sb = new StringBuilder(url.Length);
        foreach (char c in url)
        {
            if (c is '\\' or '(' or ')' or '|') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The literal, escaped display of an object member name. Unlike a Literal String,
    /// a Key Label keeps control characters as visible JSON-style escapes so the label
    /// stays on one line inside a heading or table header.
    /// </summary>
    internal static string KeyLabel(string name) => name.Length == 0 ? EmptyString : EscapeInline(name);

    /// <summary>
    /// The inline representation of a Scalar Value, or of an empty container shown
    /// inline as <c>`[]`</c> / <c>`{}`</c>. Same rendering in every context.
    /// </summary>
    internal static string ScalarText(DocNode node) => node switch
    {
        NullNode => "`null`",
        BooleanNode b => b.Value ? "true" : "false",
        NumberNode num => num.Lexeme,
        StringNode s => StringScalar(s.Value),
        ArrayNode => "`[]`",
        ObjectNode => "`{}`",
        _ => throw new InvalidOperationException("Unknown node kind."),
    };

    private static string StringScalar(string value)
    {
        if (value.Length == 0) return EmptyString;
        if (IsUrlString(value))
        {
            return $"[{EscapeText(value)}]({EscapeDestination(value)})";
        }
        return EscapeText(value);
    }
}
