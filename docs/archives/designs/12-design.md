# Technical Design: G2 Proactive Injection — memctl context-inject

**Spec:** docs/specs/12-spec.md
**Task:** 12
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Architecture Overview

This is a new vertical slice in the Operators layer. `ContextInjectOperator` is a read-only operator that queries the vault index and returns a formatted markdown string. No data is written; no new ports or index methods are needed.

The feature wires into the Claude Code `UserPromptSubmit` hook as a `before-prompt` event handler. The hook protocol is already documented in `docs/memctl.md` — this task implements the `context-inject` CLI command that fulfils the G2 side of that contract.

### System Context

Control flow:

1. Claude Code fires `UserPromptSubmit` hook → spawns `memctl context-inject` with stdin = user prompt text
2. `Program.cs` handler reads stdin, optionally extracts prompt from JSON, passes to `ContextInjectOperator.Execute()`
3. Operator initialises index (same NeedsIngest/Initialize pattern as `ListOperator`)
4. Operator extracts keywords, runs per-keyword BM25 union, merges with list secondary, deduplicates
5. Operator returns formatted `## Memory Context` block (or `null` if nothing to inject)
6. Handler writes block to `Console.Out` (or nothing); exits 0 always
7. Claude Code prepends stdout to the prompt before the AI call

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/ContextInjectOperator.cs` | Read vault, format context block for hook injection | `ContextInjectOperator` class |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Bootstrap/Program.cs` | Add `context-inject` subcommand + `ExtractPromptText` helper | FR-001..FR-023 wiring |
| `docs/memctl.md` | Verify G2 section complete (already present) | FR-024 |

### Integration Code Blocks

#### INTEGRATION: Program.cs → context-inject subcommand insertion

```
// INTEGRATION: Program.cs — insert context-inject subcommand before capture
// old_string (exact — unique anchor):
// --- capture ---

// new_string (complete replacement block):
// --- context-inject ---
var ciDryRunOpt      = new Option<bool>("--dry-run", "Same as live run (read-only command)");
var contextInjectCmd = new Command("context-inject", "Inject relevant vault context for UserPromptSubmit hook");
contextInjectCmd.AddOption(ciDryRunOpt);
contextInjectCmd.SetHandler(async ctx =>
{
    try
    {
        var g = G(ctx);

        if (!Console.IsInputRedirected) { ctx.ExitCode = 0; return; }

        string stdinText;
        try   { stdinText = await Console.In.ReadToEndAsync(); }
        catch { ctx.ExitCode = 0; return; }

        if (string.IsNullOrWhiteSpace(stdinText)) { ctx.ExitCode = 0; return; }

        stdinText = ExtractPromptText(stdinText);
        if (string.IsNullOrWhiteSpace(stdinText)) { ctx.ExitCode = 0; return; }

        var vaultPath = VaultLocator.FindVault(g.Vault);
        if (vaultPath is null) { ctx.ExitCode = 0; return; }

        var context = new ContextInjectOperator(vaultReader, noteIndex)
            .Execute(vaultPath, stdinText);

        if (context is not null)
            Console.Write(context);
    }
    catch { /* NFR-002: never crash hook */ }
    ctx.ExitCode = 0;
});
root.AddCommand(contextInjectCmd);

// --- capture ---
```

#### INTEGRATION: Program.cs → ExtractPromptText helper after return

```
// INTEGRATION: Program.cs — insert ExtractPromptText after last DTO
// old_string (exact — unique tail of file):
internal sealed record TranscriptTurn(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);

// new_string:
internal sealed record TranscriptTurn(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);

// Extract plain-text prompt from stdin — plain text or first string field from JSON
static string ExtractPromptText(string raw)
{
    var trimmed = raw.TrimStart();
    if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[')) return raw;
    try
    {
        using var doc = JsonDocument.Parse(trimmed);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString() ?? "";
            return ""; // JSON with no string fields
        }
    }
    catch { /* not valid JSON — use raw */ }
    return raw;
}
```

### Deleted Files

None.

---

## 3. Data Model

No schema changes. Read-only operator — uses existing `INoteIndex` methods only.

### Data Flow

```
stdin (prompt text or JSON)
  → ExtractPromptText() → promptText: string
  → ExtractKeywords(promptText) → keywords: List<string>
  → SearchKeywords(keywords) → searchNotes: List<Note>   [per-keyword BM25 union]
  → GetAll().Where(!inSearch).Take(3) → listNotes: List<Note>   [secondary]
  → FormatContext(results) → contextBlock: string
  → Console.Write(contextBlock) → stdout
```

---

## 4. API Design

Not applicable — CLI command, not an HTTP endpoint.

---

## 5. UI Components

Not applicable.

---

## 6. Business Logic

### FR-001/002: Stdin Parsing

Plain text stdin is used as-is. If stdin starts with `{` or `[`:
1. Attempt `JsonDocument.Parse(stdinText.TrimStart())`
2. If successful and root is Object: iterate properties, return first `JsonValueKind.String` value
3. If no string properties: return `""` (empty → handler exits 0 silently)
4. If parse fails: use raw stdin text (FR-004 — treat malformed JSON as plain text)

### FR-007/009/010: Keyword Extraction

```
text.ToLowerInvariant()
  → Regex.Split(@"\W+")      // split on non-word chars
  → Where(t.Length >= 2)     // drop single chars
  → Where(!StopWords.Contains) // drop stop words
  → Distinct()                // dedup
  → ToList()
```

Stop words: 48-word English set covering articles, conjunctions, prepositions, pronouns, auxiliary verbs.
If result is empty: no search, go directly to list fallback (FR-010).

### FR-011/014: Search Strategy

**CRITICAL:** `SqliteNoteIndex.SearchBm25` uses `EscapeFts()` which wraps the query string in double-quotes → FTS5 phrase search. Multi-word queries would require all words adjacent — too strict. Solution: one `SearchBm25` call per keyword.

```
foreach keyword in keywords:
    hits = index.SearchBm25(keyword, SearchLimitPerKeyword=3)
    union results by note.Id (first occurrence wins)
    stop at SearchLimit=6 total
```

If search returns 0 notes → fallback: `GetAll().Take(6)` (FR-014).
If keywords empty → fallback: `GetAll().Take(6)` (FR-010/014).

### FR-012/013: Secondary List Merge

When search returns results:
1. `searchIds = searchNotes.Select(n.Id).ToHashSet()`
2. `listNotes = GetAll().Where(!searchIds.Contains).Take(3)`
3. `results = [.. searchNotes, .. listNotes]`

Dedup by id — ensures no note appears twice. Search order preserved; list appended after.

### FR-016/018: Output Format

```
## Memory Context
\n
### {note.Title}
{content if ≤ 500 chars, else content[..500] + "..."}
\n
[repeat per note]
```

`note.Content.Length > 500` → truncate; `== 500` → no truncation (FR-018 spec §5, edge case 10).

### IncrementAccess

After building the results list (and after the `Count == 0` check), call `index.IncrementAccess(note.Id)` for each note. Signals relevance; contributes to future GetAll weight+access ordering.

---

## 7. Error Handling Strategy

| Error Scenario | Handling | Notes |
|---------------|----------|-------|
| stdin not redirected (interactive) | `Console.IsInputRedirected == false` → exit 0, empty stdout | FR-021 |
| stdin read throws | `catch { ctx.ExitCode = 0; return; }` | FR-021 |
| Empty/whitespace stdin | early return, exit 0 | FR-003/FR-021 |
| JSON with no string fields | `ExtractPromptText` returns `""` → early return, exit 0 | FR-003 |
| Vault not found | `vaultPath is null` → exit 0, empty stdout | FR-006/FR-021 |
| Index returns 0 results | operator returns `null` → handler writes nothing | FR-015 |
| Any unhandled exception | outer `try/catch` → exit 0 silently | NFR-002/FR-023 |

---

## 8. Security Considerations

- **Input:** Stdin content is never executed; only used for keyword tokenization via regex. No injection risk.
- **Output:** Context block written directly to stdout — no shell expansion.
- **File access:** Read-only index queries. NeedsIngest may trigger a write-only ingest, same as existing operators.

---

## 9. Performance Considerations

- **BM25 per-keyword calls:** Each call hits an FTS5 virtual table index → < 5 ms. For 6 keywords × SearchLimitPerKeyword=3: 6 queries. Total expected < 50 ms (NFR-001 < 200 ms).
- **GetAll():** Returns all notes sorted in DB, then `.Take(N)` cuts in memory. For < 1000 notes: negligible.
- **No embedding:** No GemmaEmbeddingEngine needed → no model load latency.
- **Double break in SearchKeywords:** Stops early when SearchLimit reached — amortises cost for high-recall first keywords.

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Unit | Keyword extraction, stop-word filter, JSON extraction, format output, dedup logic | xUnit | ~12 tests |
| Unit | Execute() paths: empty keywords, search empty → fallback, search+list merge | xUnit (fake index) | ~8 tests |
| E2E (smoke) | CLI invokes, plain text stdin, vault detected, context block on stdout | `dotnet run` + pipe | 3 scenarios |

### Key Test Cases

1. `ExtractKeywords("fix the authentication bug")` → `["fix", "authentication", "bug"]` (stop words filtered)
2. `ExtractKeywords("the is and or")` → `[]` (all stop words → empty)
3. `Execute` with empty keywords → fallback path calls `GetAll().Take(6)` not `SearchBm25`
4. `Execute` with search returning 0 → fallback path
5. `Execute` with search returning 4 → secondary list adds up to 3 (dedup by id)
6. `FormatContext` with content of exactly 500 chars → no `...` appended
7. `FormatContext` with content of 501 chars → truncated to 500 + `...`
8. `ExtractPromptText` with `{"prompt":"fix bug"}` → returns `"fix bug"`
9. `ExtractPromptText` with `{"count":42}` (no string fields) → returns `""`
10. `ExtractPromptText` with plain text → returns raw text unchanged
11. Smoke: `echo "typescript async" | dotnet run -- context-inject` → stdout contains `## Memory Context`
12. Smoke: vault missing → stdout empty, exit 0

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5 — executed by /qc)

| Scenario | Command | Expected Output | FR |
|----------|---------|-----------------|-----|
| Context block output | `echo "typescript sqlite notes" \| dotnet run --project src/memctl -- context-inject` | stdout contains `## Memory Context`, exit 0 | FR-016 |
| Empty stdin exit 0 | `echo "" \| dotnet run --project src/memctl -- context-inject` | stdout is empty, exit 0 | FR-003 |
| Vault missing exit 0 | `echo "some prompt" \| dotnet run --project src/memctl -- context-inject --vault /nonexistent` | stdout is empty, exit 0 | FR-006 |

> Note: Smoke scenario 1 requires a vault at the project's cwd. Run from `H:/repos/VDG_repos/CLIs/VDG_CLI_memctl` which has an Obsidian vault with `.obsidian/` marker.

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| `System.Text.RegularExpressions` | Built-in | Keyword tokenization | No |
| `System.Text.Json` | Built-in | JSON stdin parsing | No |

No new NuGet packages.

---

## 12. Implementation Order

1. **NEW FILE:** `src/memctl/Operators/ContextInjectOperator.cs` — full implementation (constants, StopWords, Execute, SearchKeywords, ExtractKeywords, FormatContext)
2. **MODIFY:** `src/memctl/Bootstrap/Program.cs` — Edit 1: insert context-inject subcommand before `// --- capture ---`
3. **MODIFY:** `src/memctl/Bootstrap/Program.cs` — Edit 2: append `ExtractPromptText` static local function after last DTO record
4. **Build + verify:** `dotnet build src/memctl`
5. **Write tests:** `src/memctl.Tests/ContextInjectOperatorTests.cs`

---

## 13. Assumptions & Open Design Decisions

- [x] **IncrementAccess on inject:** Assumed YES — accessing via injection counts as a read, signals relevance for future GetAll ordering. If too noisy, this can be disabled per-call without interface change.
- [x] **Single keyword min-length = 2:** Tokens of length 1 (e.g., `"a"`) are in stop words anyway; length ≥ 2 filter is a belt-and-suspenders safety.
- [x] **`--dry-run` flag declared but ignored:** Context-inject is already read-only; dry-run has identical behaviour. Flag exists for CLI consistency with capture.
- [x] **docs/memctl.md already has UserPromptSubmit example at line 444:** FR-024 is effectively already met by the existing doc. No new section needed.

---

## 14. Traceability Matrix

| Requirement | Design Section | Files | Test Cases |
|-------------|---------------|-------|------------|
| FR-001 (plain text stdin) | 6.1 | Program.cs handler | TC-10 |
| FR-002 (JSON first string field) | 6.1 | Program.cs ExtractPromptText | TC-08, TC-09 |
| FR-003 (empty stdin → exit 0) | 7 | Program.cs handler | TC-12, smoke-2 |
| FR-004 (malformed JSON → raw text) | 6.1 | ExtractPromptText catch | TC-10 |
| FR-005 (vault auto-detect) | 6 / §1 | Program.cs VaultLocator.FindVault | smoke-1 |
| FR-006 (vault missing → exit 0) | 7 | Program.cs handler | TC-12, smoke-3 |
| FR-007/008/009 (keyword extraction) | 6.2 | ContextInjectOperator.ExtractKeywords | TC-01, TC-02 |
| FR-010 (empty keywords → fallback) | 6.3 | ContextInjectOperator.Execute | TC-03 |
| FR-011 (primary search) | 6.3 | ContextInjectOperator.SearchKeywords | TC-04 |
| FR-012 (secondary list) | 6.4 | ContextInjectOperator.Execute | TC-05 |
| FR-013 (dedup) | 6.4 | ContextInjectOperator.Execute | TC-05 |
| FR-014 (fallback list-only) | 6.3 | ContextInjectOperator.Execute | TC-03, TC-04 |
| FR-015 (zero results → empty stdout) | 7 | ContextInjectOperator.Execute returns null | TC-03 (empty vault) |
| FR-016/017 (output header + note format) | 6.5 | ContextInjectOperator.FormatContext | TC-11 |
| FR-018 (content truncation at 500) | 6.5 | ContextInjectOperator.FormatContext | TC-06, TC-07 |
| FR-019/020 (stdout + dry-run) | §1 data flow | Program.cs Console.Write | smoke-1 |
| FR-021/022/023 (exit 0, no error stdout) | 7 | Program.cs try/catch + ExitCode=0 | all smoke |
| FR-024 (docs hook config) | 13 assumptions | docs/memctl.md (already present line 444) | review check |
| NFR-001 (< 200ms) | 9 | per-keyword BM25, FTS5 indexed | smoke timing |
| NFR-002 (hook safety) | 7 | outer try/catch | all smoke exit 0 |
| NFR-003 (stdout purity) | 7 | Console.Write only | smoke stdout check |
| NFR-004 (single keyword) | 6.2 | ExtractKeywords length >= 2 filter | TC-01 |
