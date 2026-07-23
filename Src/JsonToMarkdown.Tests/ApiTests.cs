using System.Text.Json;
using System.Text.Json.Nodes;
using JsonToMarkdown;

namespace JsonToMarkdown.Tests;

[TestClass]
public sealed class ApiTests
{
    [TestMethod]
    public void ConvertText_SimpleObject()
    {
        Assert.AreEqual("# Results\n\n## hello\n\nworld\n", JsonMarkdown.ConvertText("""{ "hello": "world" }"""));
    }

    [TestMethod]
    public void ConvertText_PreservesNumericLexeme()
    {
        Assert.AreEqual("# Results\n\n1.00\n", JsonMarkdown.ConvertText("1.00"));
        Assert.AreEqual("# Results\n\n9007199254740993\n", JsonMarkdown.ConvertText("9007199254740993"));
        Assert.AreEqual("# Results\n\n2E+3\n", JsonMarkdown.ConvertText("2E+3"));
    }

    [TestMethod]
    public void ConvertValue_JsonNode_MatchesText()
    {
        const string src = """{"people":[{"name":"Ada","age":36},{"name":"Lin"}]}""";
        string viaText = JsonMarkdown.ConvertText(src);
        string viaNode = JsonMarkdown.ConvertValue(JsonNode.Parse(src));
        Assert.AreEqual(viaText, viaNode);
    }

    [TestMethod]
    public void ConvertValue_JsonElement_MatchesText()
    {
        const string src = """{"a":[{"b":1},{"b":{"z":9}}]}""";
        using JsonDocument doc = JsonDocument.Parse(src);
        Assert.AreEqual(JsonMarkdown.ConvertText(src), JsonMarkdown.ConvertValue(doc.RootElement));
    }

    [TestMethod]
    public void ConvertValueFromText_PreservesLexeme()
    {
        // System.Text.Json keeps the raw number token, so the value path preserves it too.
        Assert.AreEqual("# Results\n\n1.00\n", JsonMarkdown.ConvertValueFromText("1.00"));
    }

    [TestMethod]
    public void ConvertValue_NullNode_RendersNull()
    {
        Assert.AreEqual("# Results\n\n`null`\n", JsonMarkdown.ConvertValue((JsonNode?)null));
    }

    [TestMethod]
    public void ConvertText_UrlBecomesLink()
    {
        Assert.AreEqual(
            "# Results\n\n[https://example.com/x](https://example.com/x)\n",
            JsonMarkdown.ConvertText("\"https://example.com/x\""));
    }

    [TestMethod]
    public void ConvertText_EmbeddedUrlIsNotLink()
    {
        Assert.AreEqual("# Results\n\nSee https://example.com\n", JsonMarkdown.ConvertText("\"See https://example.com\""));
    }

    [TestMethod]
    public void ConvertText_DuplicateMemberThrows()
    {
        var ex = Assert.ThrowsExactly<JsonToMarkdownException>(() => JsonMarkdown.ConvertText("""{"name":"a","name":"b"}"""));
        Assert.AreEqual(JsonToMarkdownErrorCode.DuplicateMemberName, ex.Code);
        Assert.IsNotNull(ex.Location);
        Assert.IsNotNull(ex.FirstLocation);
        Assert.AreEqual(12, ex.Location!.Value.Offset);
        Assert.AreEqual(1, ex.FirstLocation!.Value.Offset);
    }

    [TestMethod]
    public void ConvertText_SyntaxErrorReportsLocation()
    {
        var ex = Assert.ThrowsExactly<JsonToMarkdownException>(() => JsonMarkdown.ConvertText("{"));
        Assert.AreEqual(JsonToMarkdownErrorCode.InvalidJsonSyntax, ex.Code);
        Assert.IsNotNull(ex.Location);
    }

    [TestMethod]
    public void ConvertText_DeeplyNestedDoesNotOverflow()
    {
        // Deeply nested arrays exercise the parser's and renderer's explicit heap
        // stacks. A naive recursive implementation would StackOverflow well before
        // this depth. (Nested empty arrays keep the output size manageable — a deep
        // chain of tables/objects would grow JSON pointers or indentation to O(n^2).)
        const int depth = 5_000;
        string src = new string('[', depth) + new string(']', depth);

        string md = JsonMarkdown.ConvertText(src);
        StringAssert.StartsWith(md, "# Results\n");
        StringAssert.Contains(md, "`[]`", "innermost empty array should render inline");
    }

    [TestMethod]
    public void ConvertText_NullArgumentThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => JsonMarkdown.ConvertText(null!));
    }
}
