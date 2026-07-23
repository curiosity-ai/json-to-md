# TODO — C# / .NET 10 port of json-to-md

Tracks the port of the TypeScript/Go `json-to-md` to modern C#.

## Done

- [x] Study the reference TypeScript (`reference/src`) and Go (`reference/go`) sources and the parity `corpus/`.
- [x] Move the existing source into `reference/`.
- [x] Create this TODO list and `CLAUDE.md`.
- [x] Port the internal document model (`Document.cs`).
- [x] Port the typed error (`JsonToMarkdownException.cs`, `JsonToMarkdownErrorCode`, `SourceLocation`).
- [x] Port the untrusted serialized-text parser (`SerializedParser.cs`) — iterative, lexeme-preserving, duplicate detection, UTF-16 locations.
- [x] Port string escaping / scalar rendering (`MarkdownText.cs`).
- [x] Port `github-slugger` parity, incl. JS `toLowerCase` + Final_Sigma (`Slug.cs`) and the strip ranges (`SlugRanges.cs`, generated from the Go data).
- [x] Port the iterative renderer (`Renderer.cs`).
- [x] Add the System.Text.Json value entry point (`ParsedNormalizer.cs`) — `JsonNode` and `JsonElement`.
- [x] Public API façade (`JsonMarkdown.cs`): `ConvertText`, `ConvertValue`, `ConvertValueFromText`.
- [x] Library project targeting `net10.0`, packable as NuGet `JsonToMarkdown`.
- [x] Test project: byte-identical `corpus` parity (MSTest) + Markdig GFM validation + API tests.
- [x] New `README.md` for the C# project.
- [x] Azure DevOps `build-nuget.yml`, modeled on the hnsw-sharp repo.
- [x] Build green; full corpus passes byte-for-byte.
- [x] Commit and push to the feature branch.

## Possible follow-ups (not required)

- [ ] A small CLI wrapper (`json-to-md file.json > out.md`) mirroring the Go CLI.
- [ ] A file-based benchmark harness to compare throughput with the Go/TS numbers.
- [ ] SourceLink / deterministic-build metadata for the NuGet package.
