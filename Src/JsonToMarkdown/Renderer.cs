using System.Text;

namespace JsonToMarkdown;

/// <summary>
/// Walks a normalised document into an ordered list of blocks and serializes them
/// to canonical GitHub Flavored Markdown: LF endings, one blank line between blocks,
/// no trailing spaces, one final newline. Layout is iterative so arbitrarily deep or
/// wide documents never overflow the stack.
/// </summary>
internal static class Renderer
{
    private const int MaxHeadingLevel = 6;

    // ----- Cells / tables / lists / blocks -----

    private abstract class Cell;

    private sealed class ScalarCell : Cell
    {
        internal readonly string Text;
        internal ScalarCell(string text) => Text = text;
    }

    private sealed class LinkCell : Cell
    {
        internal readonly string Pointer;
        internal readonly string Label;
        internal LinkCell(string pointer, string label)
        {
            Pointer = pointer;
            Label = label;
        }
    }

    private sealed class TableData
    {
        internal readonly IReadOnlyList<string> Headers;
        internal readonly IReadOnlyList<IReadOnlyList<Cell>> Rows;
        internal TableData(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<Cell>> rows)
        {
            Headers = headers;
            Rows = rows;
        }
    }

    private abstract class ListPart;

    private sealed class LinePart : ListPart
    {
        internal readonly string Text;
        internal LinePart(string text) => Text = text;
    }

    private sealed class TablePart : ListPart
    {
        internal readonly int Indent;
        internal readonly TableData Table;
        internal TablePart(int indent, TableData table)
        {
            Indent = indent;
            Table = table;
        }
    }

    private abstract class Block;

    private sealed class HeadingBlock : Block
    {
        internal readonly int Level;
        internal readonly string Text;
        internal readonly string? DetailPointer;
        internal string? Fragment;
        internal HeadingBlock(int level, string text, string? detailPointer = null)
        {
            Level = level;
            Text = text;
            DetailPointer = detailPointer;
        }
    }

    private sealed class TextBlock : Block
    {
        internal readonly string Text;
        internal TextBlock(string text) => Text = text;
    }

    private sealed class HrBlock : Block
    {
        internal static readonly HrBlock Instance = new();
    }

    private sealed class TableBlock : Block
    {
        internal readonly TableData Table;
        internal TableBlock(TableData table) => Table = table;
    }

    private sealed class ListBlock : Block
    {
        internal readonly IReadOnlyList<ListPart> Parts;
        internal ListBlock(IReadOnlyList<ListPart> parts) => Parts = parts;
    }

    private abstract class Task;

    private sealed class ValueTask : Task
    {
        internal readonly DocNode Node;
        internal readonly string Pointer;
        internal readonly int Level;
        internal ValueTask(DocNode node, string pointer, int level)
        {
            Node = node;
            Pointer = pointer;
            Level = level;
        }
    }

    private sealed class EmitTask : Task
    {
        internal readonly Block Block;
        internal EmitTask(Block block) => Block = block;
    }

    /// <summary>Push tasks so they run in the given order under a LIFO stack.</summary>
    private static void PushReversed(List<Task> stack, IReadOnlyList<Task> tasks)
    {
        for (int i = tasks.Count - 1; i >= 0; i--) stack.Add(tasks[i]);
    }

    /// <summary>Detect a Tabular Array: non-empty, every item an object, union of keys is non-empty.</summary>
    private static List<string>? TableColumns(IReadOnlyList<DocNode> items)
    {
        if (items.Count == 0) return null;
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocNode item in items)
        {
            if (item is not ObjectNode obj) return null;
            foreach (var entry in obj.Entries)
            {
                if (seen.Add(entry.Key)) columns.Add(entry.Key);
            }
        }
        return columns.Count > 0 ? columns : null;
    }

    /// <summary>Build a table's rows plus the Detail-cell tasks it spawns, in row-major order.</summary>
    private static (TableData Table, List<Task> Details) BuildTableData(
        IReadOnlyList<DocNode> items,
        IReadOnlyList<string> columns,
        string pointer,
        int detailLevel,
        int detailSubLevel)
    {
        var headers = new List<string>(columns.Count);
        foreach (string c in columns) headers.Add(MarkdownText.KeyLabel(c));
        var rows = new List<IReadOnlyList<Cell>>(items.Count);
        var details = new List<Task>();

        for (int r = 0; r < items.Count; r++)
        {
            var item = (ObjectNode)items[r];
            string rowPointer = Document.ChildPointer(pointer, r.ToString());
            var byKey = new Dictionary<string, DocNode>(StringComparer.Ordinal);
            foreach (var entry in item.Entries) byKey[entry.Key] = entry.Value;
            var row = new List<Cell>(columns.Count);
            foreach (string col in columns)
            {
                if (!byKey.TryGetValue(col, out DocNode? value))
                {
                    row.Add(new ScalarCell("")); // Missing Property: empty cell.
                    continue;
                }
                if (value is ObjectNode or ArrayNode)
                {
                    if (Document.IsEmptyContainer(value))
                    {
                        row.Add(new ScalarCell(MarkdownText.ScalarText(value)));
                    }
                    else
                    {
                        string cellPointer = Document.ChildPointer(rowPointer, col);
                        // The link label and the Detail Heading text must stay byte-identical.
                        string label = MarkdownText.EscapeInline(cellPointer);
                        row.Add(new LinkCell(cellPointer, label));
                        // A thematic break precedes every Detail Heading (and appears nowhere else).
                        details.Add(new EmitTask(HrBlock.Instance));
                        details.Add(new EmitTask(new HeadingBlock(detailLevel, label, cellPointer)));
                        details.Add(new ValueTask(value, cellPointer, detailSubLevel));
                    }
                }
                else
                {
                    row.Add(new ScalarCell(MarkdownText.ScalarText(value)));
                }
            }
            rows.Add(row);
        }

        return (new TableData(headers, rows), details);
    }

    /// <summary>
    /// Build the list block for a non-empty container in list position. A child that is
    /// a Tabular Array is rendered as an indented table and its Detail Sections are returned
    /// to be emitted at block level after the list. Iterative: an explicit heap stack keeps
    /// arbitrarily deep nesting off the call stack.
    /// </summary>
    private static (List<ListPart> Parts, List<Task> Details) BuildListBlock(
        DocNode root,
        string rootPointer,
        int blockLevel)
    {
        int detailLevel = Math.Min(blockLevel, MaxHeadingLevel);
        int detailSubLevel = Math.Min(detailLevel + 1, MaxHeadingLevel);
        var parts = new List<ListPart>();
        var details = new List<Task>();

        var stack = new List<ListFrame> { new(root, rootPointer, 0) };

        // Emit a non-inline container child: an indented table if Tabular, else a nested list.
        void HandleContainer(DocNode child, string childPtr, int indent, string lead)
        {
            if (child is ArrayNode childArr)
            {
                var columns = TableColumns(childArr.Items);
                if (columns is not null)
                {
                    parts.Add(new LinePart(lead));
                    var built = BuildTableData(childArr.Items, columns, childPtr, detailLevel, detailSubLevel);
                    parts.Add(new TablePart(indent, built.Table));
                    foreach (Task task in built.Details) details.Add(task);
                    return;
                }
            }
            parts.Add(new LinePart(lead));
            stack.Add(new ListFrame(child, childPtr, indent));
        }

        while (stack.Count > 0)
        {
            ListFrame frame = stack[^1];
            DocNode node = frame.Node;
            string pad = Repeat("  ", frame.Indent);

            if (node is ObjectNode obj)
            {
                if (frame.Index >= obj.Entries.Count)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                var entry = obj.Entries[frame.Index++];
                string label = MarkdownText.KeyLabel(entry.Key);
                string childPtr = Document.ChildPointer(frame.Pointer, entry.Key);
                if (Document.RendersInline(entry.Value))
                {
                    parts.Add(new LinePart($"{pad}- **{label}:** {MarkdownText.ScalarText(entry.Value)}"));
                }
                else
                {
                    HandleContainer(entry.Value, childPtr, frame.Indent + 1, $"{pad}- **{label}**");
                }
            }
            else if (node is ArrayNode arr)
            {
                if (frame.Index >= arr.Items.Count)
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                int i = frame.Index++;
                DocNode child = arr.Items[i];
                string childPtr = Document.ChildPointer(frame.Pointer, i.ToString());
                if (Document.RendersInline(child))
                {
                    parts.Add(new LinePart($"{pad}- {MarkdownText.ScalarText(child)}"));
                }
                else
                {
                    HandleContainer(child, childPtr, frame.Indent + 1, $"{pad}-");
                }
            }
            else
            {
                // A scalar can only appear as the root of a list when misused; guard anyway.
                parts.Add(new LinePart($"{pad}- {MarkdownText.ScalarText(node)}"));
                stack.RemoveAt(stack.Count - 1);
            }
        }

        return (parts, details);
    }

    private sealed class ListFrame
    {
        internal readonly DocNode Node;
        internal readonly string Pointer;
        internal readonly int Indent;
        internal int Index;
        internal ListFrame(DocNode node, string pointer, int indent)
        {
            Node = node;
            Pointer = pointer;
            Indent = indent;
        }
    }

    /// <summary>Walk the document (iteratively) into an ordered list of blocks.</summary>
    private static List<Block> Layout(DocNode root)
    {
        var blocks = new List<Block> { new HeadingBlock(1, "Results") };
        var stack = new List<Task> { new ValueTask(root, "", 2) };

        while (stack.Count > 0)
        {
            Task task = stack[^1];
            stack.RemoveAt(stack.Count - 1);

            if (task is EmitTask emit)
            {
                blocks.Add(emit.Block);
                continue;
            }

            var value = (ValueTask)task;
            DocNode node = value.Node;
            string pointer = value.Pointer;
            int level = value.Level;

            if (Document.RendersInline(node))
            {
                blocks.Add(new TextBlock(MarkdownText.ScalarText(node)));
                continue;
            }

            if (node is ObjectNode obj)
            {
                if (level > MaxHeadingLevel)
                {
                    var (parts, details) = BuildListBlock(obj, pointer, level);
                    blocks.Add(new ListBlock(parts));
                    PushReversed(stack, details);
                    continue;
                }
                // Expand members: heading then value, in order.
                var expansion = new List<Task>(obj.Entries.Count * 2);
                foreach (var entry in obj.Entries)
                {
                    expansion.Add(new EmitTask(new HeadingBlock(level, MarkdownText.KeyLabel(entry.Key))));
                    expansion.Add(new ValueTask(entry.Value, Document.ChildPointer(pointer, entry.Key), level + 1));
                }
                PushReversed(stack, expansion);
                continue;
            }

            // Array (RendersInline already excluded scalars and empty containers).
            if (node is not ArrayNode arr) continue;
            var cols = TableColumns(arr.Items);
            if (cols is null)
            {
                var (parts, details) = BuildListBlock(arr, pointer, level);
                blocks.Add(new ListBlock(parts));
                PushReversed(stack, details);
                continue;
            }
            int detailLevel = Math.Min(level, MaxHeadingLevel);
            int detailSubLevel = Math.Min(detailLevel + 1, MaxHeadingLevel);
            var (table, tableDetails) = BuildTableData(arr.Items, cols, pointer, detailLevel, detailSubLevel);
            var combined = new List<Task>(tableDetails.Count + 1) { new EmitTask(new TableBlock(table)) };
            combined.AddRange(tableDetails);
            PushReversed(stack, combined);
        }

        return blocks;
    }

    /// <summary>Render a table cell. Scalar text is already table-safe (ScalarText escapes pipes).</summary>
    private static string RenderCell(Cell cell, Dictionary<string, string> fragments)
    {
        if (cell is ScalarCell s) return s.Text;
        var link = (LinkCell)cell;
        string fragment = fragments.GetValueOrDefault(link.Pointer, "");
        return $"[{link.Label}](#{fragment})";
    }

    private static string SerializeTable(TableData table, int indent, Dictionary<string, string> fragments)
    {
        string pad = Repeat("  ", indent);
        var sb = new StringBuilder();
        sb.Append(pad).Append("| ").Append(string.Join(" | ", table.Headers)).Append(" |");
        sb.Append('\n').Append(pad).Append("| ").Append(string.Join(" | ", table.Headers.Select(_ => "---"))).Append(" |");
        foreach (var row in table.Rows)
        {
            sb.Append('\n').Append(pad).Append("| ")
              .Append(string.Join(" | ", row.Select(c => RenderCell(c, fragments)))).Append(" |");
        }
        return sb.ToString();
    }

    private static string SerializeList(IReadOnlyList<ListPart> parts, Dictionary<string, string> fragments)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(parts[i] is LinePart line
                ? line.Text
                : SerializeTable(((TablePart)parts[i]).Table, ((TablePart)parts[i]).Indent, fragments));
        }
        return sb.ToString();
    }

    internal static string RenderDocument(DocNode root)
    {
        List<Block> blocks = Layout(root);

        // Allocate heading fragments in final document order; map detail pointers to them.
        var slugger = new Slugger();
        var fragments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Block block in blocks)
        {
            if (block is HeadingBlock heading)
            {
                heading.Fragment = slugger.Slug(heading.Text);
                if (heading.DetailPointer is not null) fragments[heading.DetailPointer] = heading.Fragment;
            }
        }

        var rendered = new List<string>(blocks.Count);
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case HeadingBlock h:
                    rendered.Add(Repeat("#", h.Level) + " " + h.Text);
                    break;
                case TextBlock t:
                    rendered.Add(t.Text);
                    break;
                case HrBlock:
                    rendered.Add("---");
                    break;
                case TableBlock tb:
                    rendered.Add(SerializeTable(tb.Table, 0, fragments));
                    break;
                case ListBlock lb:
                    rendered.Add(SerializeList(lb.Parts, fragments));
                    break;
            }
        }

        return string.Join("\n\n", rendered) + "\n";
    }

    private static string Repeat(string unit, int count)
    {
        if (count <= 0) return "";
        if (count == 1) return unit;
        var sb = new StringBuilder(unit.Length * count);
        for (int i = 0; i < count; i++) sb.Append(unit);
        return sb.ToString();
    }
}
