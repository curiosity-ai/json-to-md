using System.Text;
using System.Text.Json;
using JsonToMarkdown;

namespace JsonToMarkdown.Tests;

/// <summary>
/// Runs the shared parity corpus. For every case, <see cref="JsonMarkdown.ConvertText"/>
/// must produce bytes identical to the reference TypeScript and Go implementations, or
/// fail with the contractual error code and UTF-16 location.
/// </summary>
[TestClass]
public sealed class CorpusTests
{
    private static string CorpusRoot => Path.Combine(AppContext.BaseDirectory, "corpus");

    public static IEnumerable<object[]> Cases()
    {
        string root = CorpusRoot;
        Assert.IsTrue(Directory.Exists(root), $"Corpus not found at {root}");
        foreach (string input in Directory.EnumerateFiles(root, "*.input.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string @base = input[..^".input.json".Length];
            string name = Path.GetRelativePath(root, @base).Replace(Path.DirectorySeparatorChar, '/');
            yield return new object[] { name, input };
        }
    }

    public static string GetCaseName(System.Reflection.MethodInfo _, object?[]? data) => $"corpus: {data?[0]}";

    [TestMethod]
    [DynamicData(nameof(Cases), DynamicDataDisplayName = nameof(GetCaseName))]
    public void Corpus(string name, string inputPath)
    {
        string @base = inputPath[..^".input.json".Length];
        string expectedMd = @base + ".expected.md";
        string errorJson = @base + ".error.json";

        // Read the serialized JSON exactly as bytes, then decode as UTF-8 (the source is UTF-8).
        string source = Encoding.UTF8.GetString(File.ReadAllBytes(inputPath));

        if (File.Exists(expectedMd))
        {
            byte[] expected = File.ReadAllBytes(expectedMd);
            string actual = JsonMarkdown.ConvertText(source);
            byte[] actualBytes = Encoding.UTF8.GetBytes(actual);
            CollectionAssert.AreEqual(expected, actualBytes,
                $"Byte mismatch for {name}.\n--- expected ---\n{Encoding.UTF8.GetString(expected)}\n--- actual ---\n{actual}");
        }
        else if (File.Exists(errorJson))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(errorJson));
            JsonElement root = doc.RootElement;
            string expectedCode = root.GetProperty("code").GetString()!;

            JsonToMarkdownException ex = Assert.ThrowsExactly<JsonToMarkdownException>(
                () => JsonMarkdown.ConvertText(source), $"Expected {expectedCode} for {name}");

            Assert.AreEqual(expectedCode, ToWireCode(ex.Code), $"Wrong error code for {name}");

            if (root.TryGetProperty("pointer", out JsonElement ptr))
            {
                Assert.AreEqual(ptr.GetString(), ex.Pointer, $"Wrong pointer for {name}");
            }
            if (root.TryGetProperty("location", out JsonElement loc))
            {
                AssertLocation(loc, ex.Location, $"{name} location");
            }
            if (root.TryGetProperty("firstLocation", out JsonElement first))
            {
                AssertLocation(first, ex.FirstLocation, $"{name} firstLocation");
            }
        }
        else
        {
            Assert.Fail($"Case {name} has neither .expected.md nor .error.json");
        }
    }

    private static void AssertLocation(JsonElement expected, SourceLocation? actual, string label)
    {
        Assert.IsNotNull(actual, $"{label}: expected a location");
        Assert.AreEqual(expected.GetProperty("offset").GetInt32(), actual.Value.Offset, $"{label}: offset");
        Assert.AreEqual(expected.GetProperty("line").GetInt32(), actual.Value.Line, $"{label}: line");
        Assert.AreEqual(expected.GetProperty("column").GetInt32(), actual.Value.Column, $"{label}: column");
    }

    private static string ToWireCode(JsonToMarkdownErrorCode code) => code switch
    {
        JsonToMarkdownErrorCode.InvalidJsonSyntax => "INVALID_JSON_SYNTAX",
        JsonToMarkdownErrorCode.DuplicateMemberName => "DUPLICATE_MEMBER_NAME",
        JsonToMarkdownErrorCode.InvalidParsedValue => "INVALID_PARSED_VALUE",
        JsonToMarkdownErrorCode.CyclicReference => "CYCLIC_REFERENCE",
        JsonToMarkdownErrorCode.SparseArray => "SPARSE_ARRAY",
        _ => code.ToString(),
    };
}
