# Technical Design: A.D.D V3 Contract First — Operators return MemctlResult Boundary DTO

**Spec:** docs/specs/14-spec.md
**Task:** 14
**Date:** 2026-04-30
**Status:** Draft

---

## 1. Architecture Overview

This is an **architectural refactor**, not a new feature. It rewires the response data path so that:

```
Operator Layer                Mapping Layer (Operators/Mapping)        Wire Adapters
────────────                  ─────────────────────────────────        ──────────────
Operator.Execute()                                                     ResultPrinter (CLI)
  → MemctlOutcome              → MemctlResultMapper.ToResult            → JSON to stdout
    .Data = Entity/Carrier         (switch on Data runtime type)
                                                                       McpServerOperator (MCP)
                                 → MemctlResult                          → MCP envelope
                                   .Data = typed Boundary DTO
```

**Layers affected:**

| Layer | Change |
|---|---|
| **Boundary** | Add 2 new DTOs (`SearchTagsResultDto`, `SearchLinksResultDto`) to `MemctlResult.cs` |
| **CoreAbstractions/Entities** | Add 3 new entities: `GrepHit`, `TagCount`, `VaultStats`. Add 3 internal carrier records: `SearchHitsResult`, `SearchTagsHitsResult`, `SearchLinksHitsResult` |
| **Operators** | New static `Operators/Mapping/MemctlResultMapper.cs`. 14 Operators have their `MemctlOutcome.Ok(... new {...})` rewritten to use Entities or pass-through |
| **Bootstrap** | `ResultPrinter.Print(MemctlOutcome)` rewires through `MemctlResultMapper.ToResult` and serializes the resulting `MemctlResult` directly |

**Out-of-scope cross-cutting concerns** (tracked separately): `McpServerOperator` relocation to `Implementations/Mcp/` (#21), `MemctlResult` versioning (#22), Boundary Request DTO validation (#23).

### System Context

```
1. CLI args → Bootstrap/Program.cs parses → calls Operator
2. Operator runs business logic → returns MemctlOutcome
   - MemctlOutcome.Data is a known Entity / carrier record / null / fallback object
3. ResultPrinter.Print(outcome) → MemctlResultMapper.ToResult(outcome) → MemctlResult
4. JsonSerializer serializes MemctlResult with [JsonPropertyName] attributes
5. JSON written to stdout
```

For MCP path:

```
1. MCP client → JSON-RPC request → McpServerOperator dispatches
2. McpServerOperator calls underlying Operator → MemctlOutcome
3. McpServerOperator calls MemctlResultMapper.ToResult(outcome) → MemctlResult
4. McpServerOperator wraps MemctlResult into MCP tool response envelope
5. JSON-RPC response back to client
```

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/CoreAbstractions/Entities/GrepHit.cs` | Domain entity for grep result | `record GrepHit(string FilePath, int LineNumber, string Content)` |
| `src/memctl/CoreAbstractions/Entities/TagCount.cs` | Domain entity for tag aggregation | `record TagCount(string Tag, int Count)` |
| `src/memctl/CoreAbstractions/Entities/VaultStats.cs` | Domain entity for vault stats | `record VaultStats(int NoteCount, int TagCount, int LinkCount, long IndexBytes, string VaultPath)` |
| `src/memctl/CoreAbstractions/Entities/SearchHitsResult.cs` | Internal carrier for search Operators | `record SearchHitsResult(string Query, IReadOnlyList<SearchHit> Hits)` |
| `src/memctl/CoreAbstractions/Entities/SearchTagsHitsResult.cs` | Internal carrier for search-tags | `record SearchTagsHitsResult(string[] Tags, bool MatchAll, IReadOnlyList<Note> Notes)` |
| `src/memctl/CoreAbstractions/Entities/SearchLinksHitsResult.cs` | Internal carrier for search-links | `record SearchLinksHitsResult(string SourceId, int Depth, IReadOnlyList<Note> Notes)` |
| `src/memctl/Operators/Mapping/MemctlResultMapper.cs` | Static mapper Outcome → Result | `public static class MemctlResultMapper { public static MemctlResult ToResult(MemctlOutcome) }` |
| `tests/memctl.Tests/Mapping/MemctlResultMapperTests.cs` | Unit tests for mapper dispatch (one method per Data runtime type) | xUnit tests |
| `tests/memctl.Tests/snapshots/wire-format-baseline/*.json` | Baseline JSON output for backward compat regression | one file per CLI command |
| `tests/memctl.Tests/WireFormatSnapshotTests.cs` | Regression test asserting baseline diff zero | xUnit tests |

### Modified Files (Boundary)

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Boundary/MemctlResult.cs` | Add `SearchTagsResultDto`, `SearchLinksResultDto` classes | Cover wire shapes that are not just `SearchResultDto` (search-tags adds `tags` + `match_all`; search-links adds `source_id` + `depth`) |

### Modified Files (Operators)

| File Path | Changes | Reason / FR |
|-----------|---------|-------|
| `src/memctl/Operators/AddOperator.cs` | `Data` set to `Note` Entity instead of `new { id, file, title, tags, ... }` | FR-023 |
| `src/memctl/Operators/VaultWriteOperator.cs` | All 4 Execute methods (`ExecuteCreate`, `ExecuteUpdate`, `ExecuteAppend`, `ExecuteDelete`) — `Data = Note` | FR-023 |
| `src/memctl/Operators/GetOperator.cs` | `Data = Note` Entity directly. Keep existing `NoteToData` helper as a fallback used only by ad-hoc Operators that pass NoteDto-shaped anonymous data via FR-020 path | FR-024 |
| `src/memctl/Operators/ListOperator.cs` | `Data = IReadOnlyList<Note>` | FR-025 |
| `src/memctl/Operators/SearchOperator.cs` | `Data = SearchHitsResult(query, hits)` | FR-026 |
| `src/memctl/Operators/SearchSemanticOperator.cs` | `Data = SearchHitsResult(query, hits)` | FR-026 |
| `src/memctl/Operators/SearchTextOperator.cs` | `Data = SearchHitsResult(query, hits)` | FR-026 |
| `src/memctl/Operators/SearchTagsOperator.cs` | `Data = SearchTagsHitsResult(tags, matchAll, notes)` | FR-026 |
| `src/memctl/Operators/SearchLinksOperator.cs` | `Data = SearchLinksHitsResult(sourceId, depth, notes)` | FR-026 |
| `src/memctl/Operators/SearchDateOperator.cs` | `Data = SearchHitsResult` (query carries date range string) | FR-026 |
| `src/memctl/Operators/TagsOperator.cs` | `Data = IReadOnlyList<TagCount>` | FR-027 |
| `src/memctl/Operators/StatsOperator.cs` | `Data = VaultStats` Entity | FR-028 |
| `src/memctl/Operators/GrepOperator.cs` | `Data = IReadOnlyList<GrepHit>` | FR-029 |
| `src/memctl/Operators/IdentityOperator.cs` | `ExecuteSet`: `Data = Note`. `ExecuteGet`: `Data = Note` | FR-030 (typed) |
| `src/memctl/Operators/WeightOperator.cs` | Keep current anonymous `{id, file, weight}` (FR-020 fallback path) — wire shape stable | FR-030 (fallback) |
| `src/memctl/Operators/DecayOperator.cs` | Keep current anonymous (decay-specific shape; not a Boundary DTO; preserved via FR-020 fallback) | FR-030 (fallback) |
| `src/memctl/Operators/StatusOperator.cs` | Keep current anonymous `{model_ready, model_path, ...}` (status-specific; FR-020 fallback) | FR-030 (fallback) |
| `src/memctl/Operators/CaptureOperator.cs` | Keep current anonymous (capture-specific) | FR-030 (fallback) |
| `src/memctl/Operators/IngestOperator.cs` | Keep current anonymous | FR-030 (fallback) |
| `src/memctl/Operators/OrganizeOperator.cs` | Keep current anonymous | FR-030 (fallback) |
| `src/memctl/Operators/ModelDownloadOperator.cs` | Keep current anonymous | FR-030 (fallback) |
| `src/memctl/Operators/ModelListOperator.cs` | Keep current anonymous | FR-030 (fallback) |
| `src/memctl/Operators/ModelUseOperator.cs` | Keep current anonymous | FR-030 (fallback) |
| `src/memctl/Operators/LintOperator.cs` | Keep current anonymous (lint-specific shape; complex nested) | FR-030 (fallback) |
| `src/memctl/Operators/FetchOperator.cs` | Already passes raw markdown string; FR-020 fallback handles | FR-030 (fallback) |
| `src/memctl/Operators/McpServerOperator.cs` | (a) `CallSetWeight`, `CallGetIdentity`, `CallSetIdentity` rewrite Data per pattern. (b) After dispatching to underlying Operator, route response through `MemctlResultMapper.ToResult` before MCP JSON-RPC framing. | FR-032 |
| `src/memctl/Bootstrap/ResultPrinter.cs` | Rewrite `Print(MemctlOutcome)` to call `MemctlResultMapper.ToResult` and serialize the typed `MemctlResult` instead of manual key mapping | FR-031 |

### Integration Code Blocks

#### Bootstrap/ResultPrinter.cs → Print()

```
// INTEGRATION: ResultPrinter.cs → Print()
// old_string (method signature — unique anchor for edit_file):
    public static void Print(MemctlOutcome outcome) =>

// new_string (complete method replacement):
    public static void Print(MemctlOutcome outcome)
    {
        var result = MemctlResultMapper.ToResult(outcome);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
    }
```

The full file content remains:
```csharp
using System.Text.Json;
using Memctl.Boundary;
using Memctl.CoreAbstractions.Entities;
using Memctl.Operators.Mapping;

namespace Memctl.Bootstrap;

public static class ResultPrinter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void Print(MemctlOutcome outcome)
    {
        var result = MemctlResultMapper.ToResult(outcome);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
    }
}
```

#### Operators/AddOperator.cs → ExecuteAsync()

```
// INTEGRATION: AddOperator.cs → ExecuteAsync() return statement
// old_string (anchor — the literal old return block):
        return MemctlOutcome.Ok("add", $"Added note: {note.Title}", new
        {
            id       = note.Id,
            file     = note.FilePath,
            title    = note.Title,
            tags     = note.Tags,

// new_string:
        return MemctlOutcome.Ok("add", $"Added note: {note.Title}", note);
        // (delete remaining lines of the old anonymous block until matching '});')
```

Implementation note for Build agent: replace the entire `MemctlOutcome.Ok("add", ..., new { ... })` block (from `return MemctlOutcome.Ok(` through the closing `});`) with `return MemctlOutcome.Ok("add", $"Added note: {note.Title}", note);`.

#### Operators/SearchOperator.cs → Execute()

```
// INTEGRATION: SearchOperator.cs → Execute() return statement
// old_string (anchor):
        return MemctlOutcome.Ok("search",

// new_string:
        return MemctlOutcome.Ok("search", $"{hits.Count} results",
            new SearchHitsResult(query, hits));
```

Replace the full `return MemctlOutcome.Ok("search", ..., new { query, count, results = ... });` block with the typed carrier.

#### Operators/SearchSemanticOperator.cs → Execute()

Pattern identical to SearchOperator: replace `new { query, count, results }` with `new SearchHitsResult(query, hits)`.

#### Operators/SearchTextOperator.cs → Execute()

Pattern identical to SearchOperator.

#### Operators/SearchDateOperator.cs → Execute()

```csharp
return MemctlOutcome.Ok("search-date", $"{notes.Count} results",
    new SearchHitsResult(BuildDateQuery(from, to), notes.Select(n => new SearchHit { Note = n }).ToList()));
```

#### Operators/SearchTagsOperator.cs → Execute()

```csharp
return MemctlOutcome.Ok("search-tags", $"{notes.Count} results",
    new SearchTagsHitsResult(tags, matchAll, notes));
```

#### Operators/SearchLinksOperator.cs → Execute()

```csharp
return MemctlOutcome.Ok("search-links", $"{linked.Count} linked notes",
    new SearchLinksHitsResult(source.Id, depth, linked));
```

#### Operators/ListOperator.cs → Execute()

```csharp
return MemctlOutcome.Ok("list", $"{notes.Count} notes", notes);
// notes is IReadOnlyList<Note>
```

#### Operators/TagsOperator.cs → Execute()

```csharp
var tagCounts = tags.Select(t => new TagCount(t.Tag, t.Count)).ToList();
return MemctlOutcome.Ok("tags", $"{tags.Count} tags", tagCounts);
```

#### Operators/StatsOperator.cs → Execute()

```csharp
return MemctlOutcome.Ok("stats", "Vault statistics",
    new VaultStats(noteCount, tagCount, linkCount, indexBytes, vaultPath));
```

#### Operators/GrepOperator.cs → Execute()

```csharp
var hits = matches.Select(m => new GrepHit(m.File, m.Line, m.Content)).ToList();
return MemctlOutcome.Ok("grep", $"{hits.Count} matches", hits);
```

#### Operators/GetOperator.cs → Execute()

```csharp
return MemctlOutcome.Ok("get", "Note found", note);
```

#### Operators/IdentityOperator.cs → ExecuteSet()

```csharp
return MemctlOutcome.Ok("identity", $"Identity note set: {note.Title}", note);
```

#### Operators/IdentityOperator.cs → ExecuteGet()

```csharp
return MemctlOutcome.Ok("identity", "Identity note", note);
```

#### Operators/VaultWriteOperator.cs → ExecuteCreate / ExecuteUpdate / ExecuteAppend

Each return statement: replace `new { id, file, title }` (or similar) with the `Note` Entity directly:
```csharp
return MemctlOutcome.Ok("create", $"Created note: {stored.Title}", stored);
```

#### Operators/Mcp/McpServerOperator.cs → DispatchAsync (or HandleToolCall)

After every Operator dispatch within the MCP server, route the resulting `MemctlOutcome` through the mapper before serializing the MCP tool response. Specifically locate the method that converts an Operator's `MemctlOutcome` into the MCP tool result JSON (`tools/call` response) and replace the manual JSON construction with:

```csharp
var memctlResult = MemctlResultMapper.ToResult(outcome);
var serialized   = JsonSerializer.Serialize(memctlResult, McpJsonOpts);
// then frame in MCP tool response envelope as before
```

### Deleted Files

None. `MemctlResult.cs`, `MemctlOutcome.cs`, all 28 Operator files retained.

---

## 3. Data Model

### New Entities (CoreAbstractions/Entities)

```csharp
// GrepHit.cs
namespace Memctl.CoreAbstractions.Entities;
public sealed record GrepHit(string FilePath, int LineNumber, string Content);

// TagCount.cs
namespace Memctl.CoreAbstractions.Entities;
public sealed record TagCount(string Tag, int Count);

// VaultStats.cs
namespace Memctl.CoreAbstractions.Entities;
public sealed record VaultStats(
    int    NoteCount,
    int    TagCount,
    int    LinkCount,
    long   IndexBytes,
    string VaultPath);

// SearchHitsResult.cs (internal carrier — used by 4 search Operators)
namespace Memctl.CoreAbstractions.Entities;
public sealed record SearchHitsResult(
    string                       Query,
    IReadOnlyList<SearchHit>     Hits);

// SearchTagsHitsResult.cs (internal carrier)
namespace Memctl.CoreAbstractions.Entities;
public sealed record SearchTagsHitsResult(
    string[]                Tags,
    bool                    MatchAll,
    IReadOnlyList<Note>     Notes);

// SearchLinksHitsResult.cs (internal carrier)
namespace Memctl.CoreAbstractions.Entities;
public sealed record SearchLinksHitsResult(
    string                  SourceId,
    int                     Depth,
    IReadOnlyList<Note>     Notes);
```

### New Boundary DTOs (Boundary/MemctlResult.cs)

```csharp
public sealed class SearchTagsResultDto
{
    [JsonPropertyName("tags")]      public string[] Tags     { get; init; } = [];
    [JsonPropertyName("match_all")] public bool     MatchAll { get; init; }
    [JsonPropertyName("count")]     public int      Count    { get; init; }
    [JsonPropertyName("results")]   public NoteDto[] Results { get; init; } = [];
}

public sealed class SearchLinksResultDto
{
    [JsonPropertyName("source_id")] public string    SourceId { get; init; } = "";
    [JsonPropertyName("depth")]     public int       Depth    { get; init; }
    [JsonPropertyName("count")]     public int       Count    { get; init; }
    [JsonPropertyName("results")]   public NoteDto[] Results  { get; init; } = [];
}
```

### Migrations Needed

None. No DB schema change.

### Data Flow

```
AddOperator.ExecuteAsync(...)
  ↓
note: Note (Entity)                        ← write to vault, index
  ↓
MemctlOutcome { action="add", message="Added note: X", Data: note }
  ↓
ResultPrinter.Print(outcome)
  ↓
MemctlResultMapper.ToResult(outcome)
  switch (outcome.Data) {
    case Note n              → MemctlResult { Data = MapNote(n) }    // → NoteDto
    case IReadOnlyList<Note> → MemctlResult { Data = ListResultDto(notes) }  // wraps in {count, notes:[]}
    case SearchHitsResult s  → MemctlResult { Data = SearchResultDto { Query, Count, Results=hits.map } }
    case SearchTagsHitsResult → MemctlResult { Data = SearchTagsResultDto }
    case SearchLinksHitsResult → MemctlResult { Data = SearchLinksResultDto }
    case IReadOnlyList<TagCount> → MemctlResult { Data = TagsResultDto { count, tags:[]} }
    case IReadOnlyList<GrepHit>  → MemctlResult { Data = { count, hits:[GrepHitDto] } }   // anonymous OK at mapper boundary OR new GrepResultDto
    case VaultStats             → MemctlResult { Data = StatsDto }
    case null                   → MemctlResult { Data = null }
    default                     → MemctlResult { Data = outcome.Data }   // FR-020 fallback
  }
  ↓
JsonSerializer.Serialize(result)
  ↓
stdout
```

For `IReadOnlyList<TagCount>` and list outputs, the mapper wraps the result in an envelope shape matching the current wire format (`{ count: N, tags: [...] }` or `{ count: N, notes: [...] }`).

---

## 4. API Design

This refactor does **not** add or modify CLI commands or MCP tools. JSON wire format is preserved (NFR-002). No new request shapes; output shapes are stable.

### Wire format examples

**Before (anonymous):**
```json
{
  "success": true,
  "action": "add",
  "message": "Added note: My note",
  "data": { "id": "abc...", "file": "...", "title": "My note", "tags": [] }
}
```

**After (typed):** identical JSON output.

---

## 5. UI Components

N/A.

---

## 6. Business Logic

### Mapper dispatch — chosen approach: **switch expression**

**Decision:** Use C# switch expression on `outcome.Data` runtime type.

**Alternatives considered:**

| Approach | Pros | Cons | Verdict |
|---|---|---|---|
| **switch expression (chosen)** | Compile-time exhaustive feel; readable; allocates nothing extra; idiomatic C# | Adding a new Data type = edit one method | ✅ |
| Type-keyed dictionary `Dictionary<Type, Func<object, object>>` | Open for extension at runtime | Allocation per dispatch, no compile-time safety, no pattern-matching on generic IReadOnlyList | ❌ |
| Visitor pattern with `IDispatchable` | Pure OO, exhaustive | Forces every Entity to implement an interface (intrusive) | ❌ |

The dispatch handles non-generic types (`Note`, `VaultStats`) and generic IReadOnlyList variants via pattern matching with type tests:
```csharp
return outcome.Data switch
{
    null                        => Build(outcome, null),
    Note note                   => Build(outcome, MapNote(note)),
    VaultStats stats            => Build(outcome, MapStats(stats)),
    SearchHitsResult shr        => Build(outcome, MapSearchHits(shr)),
    SearchTagsHitsResult sthr   => Build(outcome, MapSearchTagsHits(sthr)),
    SearchLinksHitsResult slhr  => Build(outcome, MapSearchLinksHits(slhr)),
    IReadOnlyList<Note> notes        => Build(outcome, MapNoteList(notes)),
    IReadOnlyList<TagCount> tags     => Build(outcome, MapTagList(tags)),
    IReadOnlyList<GrepHit> grepHits  => Build(outcome, MapGrepList(grepHits)),
    _                                => Build(outcome, outcome.Data),  // fallback
};
```

### "Query string" carrier — chosen approach: **dedicated carrier records**

**Decision:** Pass query/tags/sourceId/depth fields via internal carrier records (`SearchHitsResult`, `SearchTagsHitsResult`, `SearchLinksHitsResult`) in `CoreAbstractions/Entities`.

**Alternatives considered:**

| Approach | Pros | Cons | Verdict |
|---|---|---|---|
| **Dedicated carrier records (chosen)** | Type-safe; mapper has all fields; minimal change to MemctlOutcome | Three small new files | ✅ |
| Add `Query` field to `MemctlOutcome` | Single field change | Doesn't cover tags/depth/sourceId; pollutes generic Outcome with search-specific data | ❌ |
| Piggyback on `Message` string | Zero new types | Brittle — parsing strings is fragile, breaks i18n | ❌ |

Carriers live in `CoreAbstractions/Entities` (not Boundary) — they are internal Operator-to-mapper plumbing, not external contracts.

### List wrapping — list Entities → wrapped DTO

`IReadOnlyList<Note>` from `ListOperator` maps to `{ count: N, notes: [NoteDto, ...] }` (current wire shape). The mapper builds this via an anonymous object inline OR a `NoteListResultDto` (preferred for typed). Decision: introduce a small `NoteListResultDto` in Boundary to keep typed-all-the-way:
```csharp
public sealed class NoteListResultDto
{
    [JsonPropertyName("count")] public int      Count { get; init; }
    [JsonPropertyName("notes")] public NoteDto[] Notes { get; init; } = [];
}
```
Same pattern for `IReadOnlyList<TagCount>` → `TagsListResultDto { count, tags: TagDto[] }`, and `IReadOnlyList<GrepHit>` → `GrepListResultDto { count, hits: GrepHitDto[] }`.

Add to `Boundary/MemctlResult.cs`:
```csharp
public sealed class NoteListResultDto { ... }
public sealed class TagsListResultDto { ... }
public sealed class GrepListResultDto { ... }
```

### Validation Rules

N/A — no input validation in this refactor (deferred to #23).

---

## 7. Error Handling Strategy

| Error Scenario | Handling | Where | FR |
|---|---|---|---|
| Operator returns `MemctlOutcome.Fail(action, message)` | Mapper: `success=false`, `data=null` | Mapper switch case for `null` Data | FR-038 |
| Operator returns `MemctlOutcome.Ok(...)` with no Data | Mapper: `data=null` | switch null branch | FR-021 |
| Unknown Data runtime type | Mapper passes through to `MemctlResult.Data: object?` (System.Text.Json serializes anonymous types) | switch `_` fallback | FR-020 |
| Mapper internal exception | Bubble up — caller (ResultPrinter / McpServerOperator) handles. Build agent: do not swallow | N/A | NFR design |
| `Note.Embedding` present | NoteDto has no `embedding` field — silently dropped during mapping | `MapNote` | Edge case 6 (spec) |

---

## 8. Security Considerations

- No new external surface. Wire format identical.
- No PII handling change.
- No auth change.

---

## 9. Performance Considerations

- Mapper dispatch is O(1) per outcome (single switch); negligible.
- No new allocations beyond the existing pattern (anonymous object → typed DTO is a wash; both allocate one object per call).
- Snapshot tests run only in CI / local verify; not in production hot path.

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Unit | `MemctlResultMapper` switch dispatch (each branch + fallback + null) | xUnit | ~10 tests |
| Unit | Each new entity is a record with correct fields | xUnit | ~6 tests |
| Integration | `ResultPrinter.Print` produces correct JSON for `MemctlOutcome` of every Data type | xUnit (capture stdout) | ~10 tests |
| Snapshot regression | For each CLI command listed below, produced JSON diffs zero against committed baseline | xUnit (read baseline file, normalize JSON whitespace, compare) | ~25 tests |

### Snapshot baseline corpus (NFR-006)

Generate one `.json` baseline file per command **before** refactor. After refactor, diff:

- `add.json` — add a known note, capture output
- `get.json`
- `list.json` (empty + populated)
- `search.json`, `search_semantic.json`, `search_text.json`, `search_tags.json`, `search_links.json`, `search_date.json`
- `tags.json`
- `stats.json`
- `grep.json`
- `weight.json`
- `decay.json`
- `status.json`
- `model_list.json`
- `identity_set.json`, `identity_get.json`
- `capture.json`
- `lint.json`

Each baseline produced from a fresh isolated test vault under `tests/memctl.Tests/snapshots/wire-format-baseline/`.

### Key Test Cases

1. `MapperDispatch_Note_ReturnsNoteDto` — input `Note` Entity, output `NoteDto` with all 10 fields populated
2. `MapperDispatch_Null_ReturnsNullData` — input `Data=null`, output `MemctlResult.Data == null`
3. `MapperDispatch_UnknownType_PassesThrough` — input `Data="scalar"`, output `MemctlResult.Data == "scalar"` (FR-020)
4. `MapperDispatch_FailOutcome_PreservesSuccessFalse` — input `MemctlOutcome.Fail`, output `success=false, data=null`
5. `Snapshot_AddCommand_DiffsZero` — run `memctl add ...` vs baseline → diff zero

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool (`*.csproj` OutputType=Exe, no AspNetCore)

### Smoke Scenarios (Layer 2.5 — always)

| Scenario | Command | Expected | FR |
|----------|---------|----------|----|
| Build clean | `dotnet build src/memctl/memctl.csproj` | exit 0, "Build succeeded", 0 warning, 0 error | NFR-001 |
| Help works | `dotnet run --project src/memctl/memctl.csproj -- --help` | stdout contains "memctl"; exit 0 | sanity |
| Add note JSON shape | `memctl add "test" --vault <tmp>` | stdout JSON contains `"success": true`, `"action": "add"`, `"data": {"id":` | FR-035 |
| Search returns query field | `memctl search "test" --vault <tmp>` | stdout JSON contains `"query":`, `"count":`, `"results":` | FR-036 |
| Stats returns 5 fields | `memctl stats --vault <tmp>` | stdout JSON `data` contains `"note_count"`, `"tag_count"`, `"link_count"`, `"index_bytes"`, `"vault_path"` | FR-006 |

### Full E2E Test Plan

E2E flag NOT set. Skip full E2E plan. Smoke scenarios above run via Layer 2.5.

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| `System.Text.Json` | (BCL) | DTO serialization | No |
| `xunit` | existing | Tests | No |

No new NuGet packages added (NFR-004).

---

## 12. Implementation Order

1. **Snapshot baseline capture** — run current binary, capture wire format JSON for every command, commit under `tests/memctl.Tests/snapshots/wire-format-baseline/`.
2. **Add new entities** — create 6 new files in `CoreAbstractions/Entities/`.
3. **Add new Boundary DTOs** — extend `Boundary/MemctlResult.cs` with `SearchTagsResultDto`, `SearchLinksResultDto`, `NoteListResultDto`, `TagsListResultDto`, `GrepListResultDto`.
4. **Create mapper** — `Operators/Mapping/MemctlResultMapper.cs` with switch dispatch + per-Entity Map methods.
5. **Mapper unit tests** — one test per dispatch branch + fallback + null + Fail outcome.
6. **Wire ResultPrinter** — change `Print(MemctlOutcome)` to delegate to mapper.
7. **Run snapshot tests** — confirm zero diff before any Operator changes (mapper fallback path covers all current anonymous Data shapes).
8. **Refactor primary Operators** (Note-typed):
   - `AddOperator`, `VaultWriteOperator`, `GetOperator`, `IdentityOperator`
   - Run snapshot tests after each Operator → expect zero diff
9. **Refactor list Operators**:
   - `ListOperator`, `TagsOperator`, `GrepOperator`, `StatsOperator`
   - Snapshot diff after each → zero
10. **Refactor search Operators** (carrier records):
    - `SearchOperator`, `SearchSemanticOperator`, `SearchTextOperator`, `SearchDateOperator`, `SearchTagsOperator`, `SearchLinksOperator`
    - Snapshot diff after each → zero
11. **McpServerOperator integration** — route MCP tool responses through mapper.
12. **Final snapshot pass** + grep checks (FR-022 anonymous-free; NFR-003 layer dependencies).
13. **`dotnet build`** — assert 0 warning, 0 error.
14. **Commit**: each step is its own commit on `feature/14-add-v3-contract-first-operators-return-memctlresult`.

---

## 13. Assumptions & Open Design Decisions

- **Assumption (from Spec §8.1):** Static class for `MemctlResultMapper` instead of Port — no DI swap needed for v1; pure function. **Resolved: static.**
- **Assumption (from Spec §8.2):** Search Operator carries query via dedicated carrier record (not new Outcome field, not piggyback on Message). **Resolved: carrier record.**
- **Assumption (from Spec §8.3):** `SearchHit.Score` round-trips as `score` field in `NoteDto` (existing schema reused). **Resolved: reuse `NoteDto.Score?`.**
- **Open (deferred to review):** Should `WeightOperator` / `DecayOperator` / `StatusOperator` get dedicated typed DTOs in Boundary? Decision: **No for this task** — their wire shapes are command-specific and rarely consumed programmatically; FR-020 fallback preserves output. Future task if needed.
- **Open (deferred):** Whether to remove `GetOperator.NoteToData` helper after refactor. Decision: **keep for now** as a private internal helper since `IdentityOperator.ExecuteGet` and others may still want to construct NoteDto-shaped data outside the typed path. Cleanup task can follow.
- **Open (deferred to review):** Should the MCP path serialize `MemctlResult` directly into the JSON-RPC `result` field (which means MCP clients see the same DTO), or keep the existing per-tool MCP response shape? Decision: **mirror CLI** — same `MemctlResult` content goes into MCP tool response JSON-RPC envelope's `result` field (FR-033 says wire identical).

---

## 14. Traceability Matrix

| Requirement | Design Section | Files | Test Cases |
|-------------|---------------|-------|------------|
| FR-001 | §2 (Modified, ResultPrinter) | `ResultPrinter.cs`, `MemctlResultMapper.cs` | grep `MemctlResult ` ≥ 2 (NFR-005) |
| FR-002 | §3 (Boundary DTO unchanged) | `Boundary/MemctlResult.cs` | Snapshot tests; FR-002 unit test |
| FR-003 to FR-007 | §3 + §4 (DTO shapes) | `Boundary/MemctlResult.cs` | Snapshot per DTO |
| FR-008 to FR-011 | §3 (New entities) | `CoreAbstractions/Entities/*.cs` | Unit test per entity |
| FR-012 to FR-014 | §6 (mapper dispatch) | `Operators/Mapping/MemctlResultMapper.cs` | Unit tests per branch |
| FR-015 to FR-019 | §6 + §3 (list wrapping) | `MemctlResultMapper.cs` | Unit tests per Map method |
| FR-020 | §6 (fallback) + §7 (error) | `MemctlResultMapper.cs` switch `_` | `MapperDispatch_UnknownType_PassesThrough` |
| FR-021 | §6 (null branch) | `MemctlResultMapper.cs` switch `null` | `MapperDispatch_Null_ReturnsNullData` |
| FR-022 | §2 (Operator changes) | All 14 Operator files | Grep regression check |
| FR-023 to FR-029 | §2 (Operator-by-Operator) | per file | Per-Operator unit + snapshot |
| FR-030 | §2 (FR-020 fallback Operators) | 11 files (Weight, Decay, Status, Capture, Ingest, Organize, Model*, Lint, Fetch) | Snapshot tests |
| FR-031 | §2 (ResultPrinter integration) | `ResultPrinter.cs` | Integration test |
| FR-032 | §2 (McpServerOperator) | `McpServerOperator.cs` | MCP integration test |
| FR-033 | §6 (mirror CLI/MCP) | both adapters | Cross-comparison test |
| FR-034 to FR-037 | §10 (snapshot strategy) | `tests/.../snapshots/` | 25 snapshot tests |
| FR-038 | §7 (error handling) | mapper null branch | Unit test |
| NFR-001 | §10.5 (smoke) | n/a | `dotnet build` |
| NFR-002 | §10 (snapshot) | snapshot files | All snapshot tests |
| NFR-003 | §1 (layer rules) | imports | Grep imports per layer |
| NFR-004 | §11 | `memctl.csproj` | git diff check |
| NFR-005 | §10 (mapper unit tests) | `MemctlResultMapperTests.cs` | Coverage assertion |
| NFR-006 | §10 (snapshot dir) | `tests/.../snapshots/` | Directory existence check |
| NFR-007 | §6 (purity) | `MemctlResultMapper.cs` | Manual review |

