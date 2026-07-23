# CLAUDE.md — JsonToMarkdown (C# / .NET 10 port)

Guidance for working in this repository. This is a C#/.NET 10 port of
[`json-to-md`](https://github.com/rajnandan1/json-to-md), which converts one JSON
document into deterministic GitHub Flavored Markdown.

## The one rule that governs everything: byte-identical output

The reference TypeScript and Go implementations produce **byte-identical** Markdown,
enforced by the shared fixtures in [`corpus/`](corpus/). This C# port must match those
bytes too. `Src/JsonToMarkdown.Tests/CorpusTests.cs` runs every corpus case and compares
raw UTF-8 bytes. **Never change rendering behavior without a corpus case proving it, and
never edit the corpus except in deliberate lockstep with upstream.**

Why the port can be exact: the reference operates on **UTF-16 code units**, and so does
C# — `char` is a UTF-16 code unit and `string` is a UTF-16 sequence. Source offsets,
escaping, surrogate handling, and duplicate-key locations therefore map one-to-one.
Do not "modernize" the character handling into rune/UTF-8 iteration on the text path —
that would break parity. (Slug generation is the one exception: it is code-point based
in the reference too, so `Slug.cs` iterates `Rune`s.)

## Repository layout

- `Src/JsonToMarkdown/` — the library (`net10.0`, packable as NuGet id `JsonToMarkdown`).
  - `Document.cs` — the internal `DocNode` model shared by both entry points.
  - `SerializedParser.cs` — the untrusted-text parser. Iterative (explicit stack),
    preserves numeric lexemes and member order, rejects duplicates, reports UTF-16
    locations. **This is the byte-identical path.**
  - `ParsedNormalizer.cs` — the System.Text.Json value path (`JsonNode`/`JsonElement`).
  - `MarkdownText.cs` — string escaping and scalar rendering (inline, literal, URL).
  - `Slug.cs` + `SlugRanges.cs` — `github-slugger@2.0.0` parity, including JS `toLowerCase`
    with the Unicode Final_Sigma rule. `SlugRanges.cs` is generated from the reference Go
    data — **do not hand-edit it.**
  - `Renderer.cs` — iterative layout into blocks, then canonical serialization.
  - `JsonMarkdown.cs` — the public API façade.
- `Src/JsonToMarkdown.Tests/` — MSTest suite: corpus parity + Markdig GFM validation.
- `corpus/` — the contract of record. Shared with the reference implementations.
- `reference/` — the original TypeScript (`reference/src`) and Go (`reference/go`) sources,
  kept read-only for parity reference.
- `.devops/build-nuget.yml` — Azure DevOps pipeline (pack + push), modeled on the sibling
  hnsw-sharp repo.

## Public API

```csharp
JsonMarkdown.ConvertText(string source);          // untrusted serialized JSON → byte-identical path
JsonMarkdown.ConvertValue(JsonNode? node);         // already-parsed System.Text.Json model
JsonMarkdown.ConvertValue(JsonElement element);    // already-parsed JsonElement
JsonMarkdown.ConvertValueFromText(string source);  // JsonNode.Parse then ConvertValue
```

All conversions are pure, allocate no shared state, and are safe to run concurrently.
Failures throw `JsonToMarkdownException` (`Code`, `Pointer`, `Location`, `FirstLocation`).

## Build & test

```sh
dotnet build Src/JsonToMarkdown.slnx -c Release
dotnet test  Src/JsonToMarkdown.slnx -c Release
```

The corpus files are copied into the test output directory by the test `.csproj`, so tests
find them at `AppContext.BaseDirectory/corpus`.

## Conventions

- Target `net10.0`, `Nullable` enabled, `TreatWarningsAsErrors` on for the library.
- Keep the algorithms **iterative** (explicit heap stacks). The library promises no
  nesting-depth limit; recursion would reintroduce a stack-overflow ceiling.
- When porting a behavior, cite the reference file it comes from and add/point to the
  corpus case that pins it.
