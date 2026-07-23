# JsonToMarkdown

Convert one JSON document into deterministic, human-readable [GitHub Flavored Markdown](https://github.github.com/gfm/).

A modern **C# / .NET 10** port of [`json-to-md`](https://github.com/rajnandan1/json-to-md). The conversion core produces **byte-identical output** to the reference TypeScript and Go implementations — verified against the shared [`corpus/`](corpus/) fixtures on every build. Output is a readable **projection** rather than a reversible serialization format. Every document begins with a `# Results` heading and uses canonical spacing: LF endings, one blank line between blocks, no trailing spaces, one final newline.

## Why convert JSON to Markdown?

If you feed JSON into an LLM, you pay for its punctuation. Every `{`, `}`, `"`, `:`, and `,` is tokens spent on structure the model does not need to read the data. Converting the same document to Markdown headings, lists, and tables is measurably cheaper, tokenizes more efficiently, and stays human-readable in prompts, agent memory, and RAG context.

The tradeoff: this is a one-way, human-facing **projection**, not a reversible serialization. When a downstream step must parse, validate, or store the data, keep the JSON.

## Install

```sh
dotnet add package JsonToMarkdown
```

Targets .NET 10. No third-party runtime dependencies (uses `System.Text.Json`).

## Use

```csharp
using JsonToMarkdown;

// Untrusted serialized JSON text — byte-identical to the reference implementations.
string md = JsonMarkdown.ConvertText("""{ "hello": "world" }""");

// # Results
//
// ## hello
//
// world
```

There are two entry points, plus a typed exception. All conversions are pure, never mutate their input, share no state, and are safe to run concurrently.

### `JsonMarkdown.ConvertText(string source)`

Accepts untrusted **serialized** JSON text. A dedicated parser reads it without executing caller code and preserves each number's original spelling, object-member order, and array order. It rejects duplicate member names and reports UTF-16 source locations. This is the byte-identical parity path.

```csharp
JsonMarkdown.ConvertText("9007199254740993"); // preserved exactly
JsonMarkdown.ConvertText("1.00");              // "1.00" (lexeme preserved)
```

### `JsonMarkdown.ConvertValue(...)` — the System.Text.Json path

Accepts an already-parsed `System.Text.Json` object model. Overloads take a
[`JsonNode`](https://learn.microsoft.com/dotnet/api/system.text.json.nodes.jsonnode) or a
[`JsonElement`](https://learn.microsoft.com/dotnet/api/system.text.json.jsonelement).
Because `System.Text.Json` preserves the raw numeric token, numbers keep their original spelling here too.

```csharp
using System.Text.Json.Nodes;

JsonNode? node = JsonNode.Parse("""{ "hello": "world" }""");
string md = JsonMarkdown.ConvertValue(node);

// Convenience: read a JSON object from a string with System.Text.Json, then convert.
string md2 = JsonMarkdown.ConvertValueFromText("""{ "hello": "world" }""");
```

### Errors

Conversion fails atomically at the first error in encounter order and never returns partial Markdown, throwing `JsonToMarkdownException`:

```csharp
try
{
    JsonMarkdown.ConvertText("""{"name":"a","name":"b"}""");
}
catch (JsonToMarkdownException e)
{
    e.Code;          // JsonToMarkdownErrorCode.DuplicateMemberName
    e.Message;       // human-readable
    e.Location;      // { Offset, Line, Column } for serialized syntax failures (UTF-16 units)
    e.FirstLocation; // first occurrence, for duplicate member names
    e.Pointer;       // JSON Pointer, when the invalid value is locatable
}
```

Codes: `InvalidJsonSyntax`, `DuplicateMemberName`, `InvalidParsedValue`, `CyclicReference`, `SparseArray` (the last cannot occur from `System.Text.Json` input).

## How values render

| Input                               | Rendering                                                                                          |
| ----------------------------------- | -------------------------------------------------------------------------------------------------- |
| Object keys                         | Headings H2–H6, then nested unordered lists once nesting passes H6                                 |
| Array of objects (a Tabular Array)  | One GFM table; also when nested inside a list                                                      |
| Any other array                     | Unordered list in source order                                                                     |
| Non-empty container in a table cell | Link to a Detail Section headed by the value's JSON Pointer, preceded by a `---` thematic break    |
| String                              | Escaped literal text; never injects Markdown or HTML                                               |
| Whole-string absolute `http(s)` URL | Markdown link                                                                                      |
| `null` / `""` / `[]` / `{}`         | `` `null` `` / `` `""` `` / `` `[]` `` / `` `{}` `` (each kept distinct from a missing table cell) |

```csharp
JsonMarkdown.ConvertText("""{"table1":[{"age":14,"degrees":[{"name":"B-Degree","year":"2023"}]}]}""");
```

```markdown
# Results

## table1

| age | degrees                              |
| --- | ------------------------------------ |
| 14  | [/table1/0/degrees](#table10degrees) |

---

### /table1/0/degrees

| name     | year |
| -------- | ---- |
| B-Degree | 2023 |
```

There is no converter-defined input-size, nesting-depth, or table-size limit; parsing, validation, and rendering are iterative, so deeply nested documents do not overflow the stack.

## Design notes

- **UTF-16 fidelity.** The reference implementation operates on UTF-16 code units, and so does this port — C# `char`/`string` are UTF-16, so source offsets, escaping, and surrogate handling map directly, keeping output byte-identical.
- **Heading anchors.** Detail-section links reproduce [`github-slugger`](https://github.com/Flet/github-slugger) exactly, including JS-style `toLowerCase` with the Unicode Final_Sigma rule and the code-point strip table (see `SlugRanges.cs`, generated from the reference Go data).
- **Numeric lexemes.** Both entry points preserve a number's original token (`1.00`, `2E+3`, `9007199254740993`) rather than round-tripping through a floating-point value.

## Repository layout

- `Src/JsonToMarkdown/` — the library.
- `Src/JsonToMarkdown.Tests/` — MSTest suite: the shared parity `corpus/`, plus [Markdig](https://github.com/xoofx/markdig)-based checks that the output is valid GFM.
- `corpus/` — the byte-identical contract of record, shared with the reference implementations.
- `reference/` — the original TypeScript and Go sources, kept for reference.
- `.devops/build-nuget.yml` — Azure DevOps pipeline that packs and publishes the NuGet package.

## Development

```sh
dotnet build  Src/JsonToMarkdown.sln -c Release
dotnet test   Src/JsonToMarkdown.sln -c Release
```

## License

MIT. Ported from [`json-to-md`](https://github.com/rajnandan1/json-to-md) by Raj Nandan Sharma (MIT).
