# Technical Design: Vault Write Tools for Bidirectional Agent Access

**Spec:** docs/specs/7-spec.md
**Task:** 7
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

This task adds the write side of the memctl MCP server. The existing architecture already has all required ports (`IVaultReader.WriteNote`, `INoteIndex.Upsert`, `INoteIndex.SetWeight`) — no new abstractions are needed. The change is purely in the Operators layer: one new operator (`VaultWriteOperator`) and four new tool handlers in `McpServerOperator`.

**Layers affected:** Operators only. CoreAbstractions, Implementations, Bootstrap, Boundary — all unchanged.

### System Context

MCP write flow (create/update/append):
1. MCP client sends `tools/call {name: "create"|"update"|"append", arguments: {...}}`
2. `McpServerOperator.HandleToolsCallAsync` routes to `CallCreateAsync` / `CallUpdateAsync` / `CallAppendAsync`
3. Each handler calls `await GetEmbeddingAsync()` (lazy-init, cached), then constructs `VaultWriteOperator` and delegates
4. `VaultWriteOperator` validates path safety → writes to disk via `IVaultReader.WriteNote` → re-indexes via `INoteIndex.Upsert`
5. Returns `MemctlOutcome` → wrapped in `ToolResult` JSON

MCP write flow (`set_weight`):
1. `McpServerOperator.CallSetWeight` called (sync, inline — no operator)
2. Lookup note → `index.SetWeight` → return outcome

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/VaultWriteOperator.cs` | Vault write operations: create, update, append | `VaultWriteOperator` class |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Operators/McpServerOperator.cs` | Add FloatArg helper; add 4 tools to HandleToolsList; add 4 cases to HandleToolsCallAsync; add 4 call methods | FR-001–019 |

### Integration Code Blocks

#### INTEGRATION: McpServerOperator.cs → HandleToolsList()

```
// old_string:
    private static object HandleToolsList(object id) => new

// new_string:
    private static object HandleToolsList(object id) => new
    {
        jsonrpc = "2.0",
        id,
        result = new
        {
            tools = new object[]
            {
                MakeTool("search",
                    "Hybrid semantic+BM25 search over vault notes, sorted by relevance",
                    req: [("query",  "string",  "Search query text")],
                    opt: [("limit",  "integer", "Max results (default 10)"),
                          ("folder", "string",  "Filter to folder prefix (e.g. crypto)")]),

                MakeTool("get",
                    "Retrieve a note by ID or file path; increments access_count",
                    req: [("id", "string", "Note ID or relative file path")],
                    opt: []),

                MakeTool("list",
                    "List notes sorted by importance (weight DESC, access_count DESC)",
                    req: [],
                    opt: [("limit", "integer", "Max results (default 10)"),
                          ("tag",   "string",  "Filter by single tag")]),

                MakeTool("search_semantic",
                    "Pure vector similarity search over embedded notes",
                    req: [("query",  "string",  "Search query text")],
                    opt: [("limit",  "integer", "Max results (default 10)"),
                          ("folder", "string",  "Filter to folder prefix"),
                          ("scope",  "string",  "Comma-separated note IDs to restrict to")]),

                MakeTool("search_tags",
                    "Find notes that have specific tag(s)",
                    req: [("tags", "string", "Comma-separated tag list")],
                    opt: [("match", "string",  "any or all (default: any)"),
                          ("limit", "integer", "Max results (default 10)")]),

                MakeTool("search_date",
                    "Find notes by creation date range",
                    req: [],
                    opt: [("from",  "string",  "ISO 8601 start date (inclusive)"),
                          ("to",    "string",  "ISO 8601 end date (inclusive)"),
                          ("limit", "integer", "Max results (default 10)")]),

                MakeTool("search_links",
                    "Find notes linked to or from a given note (wikilinks graph)",
                    req: [("id", "string", "Note ID or file path")],
                    opt: [("depth", "integer", "Link traversal depth (default 1)")]),

                MakeTool("get_identity",
                    "Retrieve the vault identity note — load this first in every session for context",
                    req: [],
                    opt: []),

                MakeTool("create",
                    "Create a new note in the vault and index it immediately",
                    req: [("content", "string", "Note body text")],
                    opt: [("title",    "string",  "Note title (extracted from content if omitted)"),
                          ("folder",   "string",  "Subfolder path relative to vault root (e.g. notes)"),
                          ("filename", "string",  "Filename without extension (default: sanitized title)")]),

                MakeTool("update",
                    "Replace the content of an existing note by ID or file path",
                    req: [("id",      "string", "Note ID or relative file path"),
                          ("content", "string", "New note body text")],
                    opt: []),

                MakeTool("append",
                    "Append text to an existing note without overwriting existing content",
                    req: [("id",      "string", "Note ID or relative file path"),
                          ("content", "string", "Text to append")],
                    opt: []),

                MakeTool("set_weight",
                    "Set note importance weight (0.0-1.0); affects list order",
                    req: [("id",     "string", "Note ID or relative file path"),
                          ("weight", "number", "Importance weight 0.0-1.0")],
                    opt: []),
            },
        },
    };
```

#### INTEGRATION: McpServerOperator.cs → HandleToolsCallAsync()

```
// old_string:
    private async Task<object> HandleToolsCallAsync(object id, JsonElement prms, CancellationToken ct)

// new_string:
    private async Task<object> HandleToolsCallAsync(object id, JsonElement prms, CancellationToken ct)
    {
        if (!prms.TryGetProperty("name", out var nameProp))
            return RpcError(id, -32602, "Missing 'name'");

        var name = nameProp.GetString() ?? "";
        var args = prms.TryGetProperty("arguments", out var a) ? a : default;

        try
        {
            MemctlOutcome? outcome = name switch
            {
                "search"          => await CallSearchAsync(args, ct),
                "get"             => CallGet(args),
                "list"            => CallList(args),
                "search_semantic" => await CallSearchSemanticAsync(args, ct),
                "search_tags"     => CallSearchTags(args),
                "search_date"     => CallSearchDate(args),
                "search_links"    => CallSearchLinks(args),
                "get_identity"    => CallGetIdentity(),
                "create"          => await CallCreateAsync(args, ct),
                "update"          => await CallUpdateAsync(args, ct),
                "append"          => await CallAppendAsync(args, ct),
                "set_weight"      => CallSetWeight(args),
                _                 => null,
            };

            if (outcome is null)
                return RpcError(id, -32601, $"Unknown tool: {name}");

            return ToolResult(id, outcome);
        }
        catch (Exception ex)
        {
            return ToolResultError(id, ex.Message);
        }
    }
```

#### INTEGRATION: McpServerOperator.cs → Int() helper — add new call methods + FloatArg after existing helpers

```
// old_string:
    private static int Int(JsonElement args, string key, int defaultVal) =>
        args.ValueKind != JsonValueKind.Undefined
        && args.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : defaultVal;
}

// new_string:
    private static int Int(JsonElement args, string key, int defaultVal) =>
        args.ValueKind != JsonValueKind.Undefined
        && args.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : defaultVal;

    private static float? FloatArg(JsonElement args, string key) =>
        args.ValueKind != JsonValueKind.Undefined
        && args.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetSingle() : null;

    // --- write tool implementations ---

    private async Task<MemctlOutcome> CallCreateAsync(JsonElement args, CancellationToken _)
    {
        var content  = Str(args, "content")  ?? throw new InvalidOperationException("'content' is required");
        var title    = Str(args, "title");
        var folder   = Str(args, "folder");
        var filename = Str(args, "filename");
        var emb      = await GetEmbeddingAsync().ConfigureAwait(false);
        return new VaultWriteOperator(vaultReader, index, emb).ExecuteCreate(vaultPath, content, title, folder, filename);
    }

    private async Task<MemctlOutcome> CallUpdateAsync(JsonElement args, CancellationToken _)
    {
        var id      = Str(args, "id")      ?? throw new InvalidOperationException("'id' is required");
        var content = Str(args, "content") ?? throw new InvalidOperationException("'content' is required");
        var emb     = await GetEmbeddingAsync().ConfigureAwait(false);
        return new VaultWriteOperator(vaultReader, index, emb).ExecuteUpdate(vaultPath, id, content);
    }

    private async Task<MemctlOutcome> CallAppendAsync(JsonElement args, CancellationToken _)
    {
        var id      = Str(args, "id")      ?? throw new InvalidOperationException("'id' is required");
        var content = Str(args, "content") ?? throw new InvalidOperationException("'content' is required");
        var emb     = await GetEmbeddingAsync().ConfigureAwait(false);
        return new VaultWriteOperator(vaultReader, index, emb).ExecuteAppend(vaultPath, id, content);
    }

    private MemctlOutcome CallSetWeight(JsonElement args)
    {
        var id     = Str(args,      "id")     ?? throw new InvalidOperationException("'id' is required");
        var weight = FloatArg(args, "weight") ?? throw new InvalidOperationException("'weight' is required");
        var note   = index.GetById(id) ?? index.GetByFilePath(id);
        if (note is null) return MemctlOutcome.Fail("set_weight", $"Note not found: {id}");
        var clamped = Math.Clamp(weight, 0f, 1f);
        index.SetWeight(note.Id, clamped);
        return MemctlOutcome.Ok("set_weight", $"Weight set to {(float)Math.Round(clamped, 2)}",
            new { id = note.Id, file = note.FilePath, weight = (float)Math.Round(clamped, 2) });
    }
}
```

### Deleted Files
None.

---

## 3. Data Model

### New Models/Schemas
None — `Note` record is used as-is. `with {}` syntax creates modified copies.

### Migrations Needed
None — SQLite schema unchanged. `INoteIndex.Upsert` already handles insert-or-replace.

### Data Flow

**create:** `content + title? + folder? + filename?` → build `Note` record → `embedding.Embed(title+content)` → `vaultReader.WriteNote(note, vaultPath, relativeFile)` → `index.Upsert(note with { Embedding })` → return `id, file, title`

**update:** `id` → `index.GetById/ByFilePath` → `existing with { Content, Modified }` → embed → `WriteNote(updated, vaultPath, existing.FilePath)` → `Upsert` → return `id, file, title`

**append:** same as update but `Content = existing.Content + separator + appendContent`

**set_weight:** `id` → `index.GetById/ByFilePath` → `Math.Clamp(weight, 0f, 1f)` → `index.SetWeight` → return `id, file, weight`

---

## 4. API Design

### MCP Tools — Write Side

| Tool | Required Args | Optional Args | Returns | FR |
|------|--------------|---------------|---------|-----|
| `create` | `content` | `title`, `folder`, `filename` | `id`, `file`, `title` | FR-001–007 |
| `update` | `id`, `content` | — | `id`, `file`, `title` | FR-008–011 |
| `append` | `id`, `content` | — | `id`, `file`, `title` | FR-012–015 |
| `set_weight` | `id`, `weight` (number) | — | `id`, `file`, `weight` | FR-016–019 |

---

## 5. UI Components
N/A — CLI/MCP tool.

---

## 6. Business Logic

### VaultWriteOperator — New File

**`ExecuteCreate(vaultPath, content, title?, folder?, filename?)`**
1. Vault guard: `IngestOperator.NeedsIngest` → auto-ingest if needed; `index.Initialize`
2. Resolve title: `title ?? ExtractTitle(content)`
3. Build relative path: `BuildRelativePath(folder, filename, resolvedTitle)` → `"folder/name.md"` or `"name.md"`
4. Absolute path check: `Path.GetFullPath(Path.Combine(vaultPath, relativeFile))` must start with `Path.GetFullPath(vaultPath) + DirectorySeparatorChar`
5. Build `Note` with new `Guid.NewGuid().ToString("N")[..16]` ID, `FilePath = relativeFile`, `Created = Modified = UtcNow`
6. `embedding.Embed($"{title}\n{content}")` → `float[]`
7. `vaultReader.WriteNote(note with { Embedding }, vaultPath, relativeFile)`
8. `index.Upsert(note with { Embedding })`
9. Return `MemctlOutcome.Ok("create", ..., new { id, file, title })`

**`ExecuteUpdate(vaultPath, id, content)`**
1. Vault guard + index init
2. `index.GetById(id) ?? index.GetByFilePath(id)` → null → `Fail("update", "Note not found: {id}")`
3. `existing with { Content = content, Modified = UtcNow }`
4. Embed → `Upsert` → `WriteNote(updated, vaultPath, existing.FilePath)`
5. Return `Ok("update", ..., new { id, file, title })`

**`ExecuteAppend(vaultPath, id, content)`**
1–2. Same as update
3. `separator = existing.Content.EndsWith('\n') ? "" : "\n"`
4. `existing with { Content = existing.Content + separator + content, Modified = UtcNow }`
5. Embed → `WriteNote` → `Upsert`
6. Return `Ok("append", ..., new { id, file, title })`

### Validation Rules

| Input | Rule | Error |
|-------|------|-------|
| `folder` in create | Resolved abs path starts with vaultPath + sep | `"Path traversal detected"` |
| `id` in update/append/set_weight | `GetById ?? GetByFilePath != null` | `"Note not found: {id}"` |
| `weight` in set_weight | `FloatArg != null` | thrown as InvalidOperationException |
| `weight` range | `Math.Clamp(0f, 1f)` — silent clamp | value in response reflects clamped |

---

## 7. Error Handling Strategy

| Scenario | Handling | Response |
|----------|----------|----------|
| Path traversal in `folder` | `return MemctlOutcome.Fail` immediately, no disk write | `success:false`, `"Path traversal detected"` |
| Note not found (update/append/set_weight) | `return MemctlOutcome.Fail` | `success:false`, `"Note not found: {id}"` |
| Missing required arg | `throw InvalidOperationException` → caught by `HandleToolsCallAsync` | `ToolResultError` with message |
| Embedding failure | Exception propagates → `ToolResultError` | `isError:true` |
| Disk write failure | Exception propagates → `ToolResultError` | `isError:true` |

---

## 8. Security Considerations

- **Path traversal:** `IsPathSafe` in `VaultWriteOperator` resolves symlinks via `Path.GetFullPath` before comparison. Suffix `+ DirectorySeparatorChar` prevents false-positive where vault path is a prefix of a sibling directory.
- **Input validation:** No size limits enforced (out of scope per spec §6). Content is written as-is to markdown.
- **No auth:** MCP stdio is local-only; no additional auth needed.

---

## 9. Performance Considerations

- **Embedding on every write:** `GemmaEmbeddingEngine.Embed()` is CPU-bound (~50–200ms). Acceptable for interactive agent writes (not batch).
- **`index.Upsert`:** Single-row INSERT OR REPLACE — O(1). No full re-ingest.
- **`vaultReader.WriteNote`:** Single `File.WriteAllText` — O(content size).

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Layer 2.5 smoke | All [e2e]-tagged FRs via MCP JSON-RPC | `dotnet run -- mcp` + stdin/stdout | 10 scenarios |

No test project exists (pre-existing). Layer 2.5 smoke covers all [e2e] FRs.

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool (MCP stdio mode)

### Smoke Scenarios (Layer 2.5)

| Scenario | MCP Request | Expected | FR |
|----------|-------------|----------|----|
| tools/list has 4 new tools | `{"method":"tools/list"}` | response contains `create`, `update`, `append`, `set_weight` | FR-001,008,012,016 |
| create note | `tools/call create {content:"Hello world"}` | `success:true`; file exists on disk | FR-002 |
| create with folder | `tools/call create {content:"x", folder:"notes", title:"My Note"}` | file at `<vault>/notes/My_Note.md` | FR-004 |
| create path traversal | `tools/call create {content:"x", folder:"../../etc"}` | `success:false` | FR-007 |
| get after create | `tools/call get {id: <id from create>}` | same content returned | FR-003 |
| update existing | `tools/call update {id, content:"New content"}` | `success:true`; get returns new content | FR-009 |
| update nonexistent | `tools/call update {id:"bad999", content:"x"}` | `success:false` | FR-011 |
| append to note | `tools/call append {id, content:" appended"}` | get returns original + newline + appended | FR-013 |
| append nonexistent | `tools/call append {id:"bad999", content:"x"}` | `success:false` | FR-015 |
| set_weight valid | `tools/call set_weight {id, weight:0.9}` | `success:true`, `weight:0.9` | FR-017 |
| set_weight clamped | `tools/call set_weight {id, weight:2.5}` | `success:true`, `weight:1.0` | FR-018 |
| set_weight nonexistent | `tools/call set_weight {id:"bad999", weight:0.5}` | `success:false` | FR-019 |

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| `Memctl.Implementations.Embedding` | existing | `GemmaEmbeddingEngine` for write embedding | No |
| All others | existing | `IVaultReader`, `INoteIndex`, `Note` | No |

---

## 12. Implementation Order

1. **NEW FILE:** `src/memctl/Operators/VaultWriteOperator.cs` — `ExecuteCreate`, `ExecuteUpdate`, `ExecuteAppend`
2. **MODIFY:** `src/memctl/Operators/McpServerOperator.cs`:
   a. Replace `HandleToolsList` (add 4 new tools)
   b. Replace `HandleToolsCallAsync` (add 4 new switch cases)
   c. Replace closing `}` block after `Int()` helper (add `FloatArg` + 4 call methods)

---

## 13. Assumptions & Open Design Decisions

- [x] `set_weight` inline in McpServerOperator, not a separate operator — consistent with `CallGetIdentity` pattern
- [x] No CLI commands for write operations — MCP-only per spec §6
- [x] Filename collision = last-write-wins (no error) — per spec §5 edge case 2
- [x] Embedding truncation at 512 tokens is silent — per spec §5 edge case 6

---

## 14. Traceability Matrix

| Requirement | Section | Files |
|-------------|---------|-------|
| FR-001–007 (create) | 6, 10.5 | `VaultWriteOperator.cs`, `McpServerOperator.cs` |
| FR-008–011 (update) | 6, 10.5 | `VaultWriteOperator.cs`, `McpServerOperator.cs` |
| FR-012–015 (append) | 6, 10.5 | `VaultWriteOperator.cs`, `McpServerOperator.cs` |
| FR-016–019 (set_weight) | 6, 10.5 | `McpServerOperator.cs` |
| NFR-001 (path safety) | 8 | `VaultWriteOperator.IsPathSafe` |
| NFR-002 (immediate indexing) | 3, 6 | `index.Upsert` after every write |
| NFR-003 (no new ports) | 1 | IVaultReader + INoteIndex unchanged |
| NFR-004 (conventions) | 6 | `VaultWriteOperator` follows `AddOperator` pattern |
