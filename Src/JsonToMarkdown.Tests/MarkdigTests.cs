using System.Text;
using Markdig;
using JsonToMarkdown;

namespace JsonToMarkdown.Tests;

/// <summary>
/// Validates that the produced Markdown is well-formed GitHub Flavored Markdown by
/// parsing and rendering it with Markdig (the de-facto .NET CommonMark/GFM engine).
/// The corpus guarantees the exact bytes; these tests guarantee those bytes are valid
/// GFM that a real renderer accepts and structures as intended.
/// </summary>
[TestClass]
public sealed class MarkdigTests
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static string CorpusRoot => Path.Combine(AppContext.BaseDirectory, "corpus");

    public static IEnumerable<object[]> RenderableCases()
    {
        foreach (string input in Directory.EnumerateFiles(CorpusRoot, "*.input.json", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string @base = input[..^".input.json".Length];
            if (!File.Exists(@base + ".expected.md")) continue; // skip error cases
            string name = Path.GetRelativePath(CorpusRoot, @base).Replace(Path.DirectorySeparatorChar, '/');
            // The `deep` group is a depth stress test whose ~100-level nesting exceeds
            // Markdig's own parser depth guard. Our output is valid GFM; Markdig simply
            // refuses to parse pathologically deep input, so exclude it from rendering.
            if (name.StartsWith("deep/", StringComparison.Ordinal)) continue;
            yield return new object[] { name, input };
        }
    }

    public static string GetCaseName(System.Reflection.MethodInfo _, object?[]? data) => $"gfm: {data?[0]}";

    [TestMethod]
    [DynamicData(nameof(RenderableCases), DynamicDataDisplayName = nameof(GetCaseName))]
    public void OutputIsValidGfm(string name, string inputPath)
    {
        string source = Encoding.UTF8.GetString(File.ReadAllBytes(inputPath));
        string md = JsonMarkdown.ConvertText(source);

        // Must render to HTML without throwing.
        string html = Markdown.ToHtml(md, Pipeline);
        Assert.IsFalse(string.IsNullOrEmpty(html), $"{name}: empty HTML");

        // Every document opens with the H1 "Results" heading.
        StringAssert.Contains(html, "<h1", $"{name}: missing H1");
        StringAssert.Contains(html, "Results", $"{name}: missing Results heading text");
    }

    [TestMethod]
    public void TabularArrayRendersAsHtmlTable()
    {
        string md = JsonMarkdown.ConvertText("""{"people":[{"name":"Ada","age":36},{"name":"Lin"}]}""");
        string html = Markdown.ToHtml(md, Pipeline);
        StringAssert.Contains(html, "<table", "expected a GFM table element");
        StringAssert.Contains(html, "<th", "expected table headers");
        StringAssert.Contains(html, "Ada", "expected cell content");
    }

    [TestMethod]
    public void DetailLinkResolvesToHeadingAnchor()
    {
        // The in-cell link target must match the Detail Section heading's generated id.
        string md = JsonMarkdown.ConvertText("""{"t":[{"obj":{"deep":1}}]}""");
        string html = Markdown.ToHtml(md, Pipeline);
        StringAssert.Contains(html, "href=\"#t0obj\"", "expected detail link");
        StringAssert.Contains(html, "id=\"t0obj\"", "expected matching heading anchor id");
    }

    [TestMethod]
    public void StringNeverInjectsMarkup()
    {
        string md = JsonMarkdown.ConvertText("\"<script>alert(1)</script>\"");
        string html = Markdown.ToHtml(md, Pipeline);
        StringAssert.DoesNotMatch(html, new System.Text.RegularExpressions.Regex("<script>"), "raw HTML must be neutralised");
        StringAssert.Contains(html, "&lt;script&gt;", "angle brackets should be entity-escaped");
    }
}
