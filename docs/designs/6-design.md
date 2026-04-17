# Technical Design: Add Layer 0 Identity Note for MCP Context Bootstrapping

**Spec:** docs/specs/6-spec.md
**Task:** 6
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

Layer 0 is a cross-cutting concern touching the Operators and Bootstrap layers only — no CoreAbstractions or Implementations changes needed. The SQLite `metadata` table (added in task #4) already stores arbitrary key/value pairs; we store `identity_note_id` there.

### System Context

**CLI flow (identity set):**
1. `Program.cs` wires `identity set <id>` → `IdentityOperator.ExecuteSet`
2. Operator looks up note by ID or path → calls `SetMetadata("identity_note_id", note.Id)` + `SetWeight(note.Id, 1.0f)`

**CLI flow (identity get):**
1. `identity get` → `IdentityOperator.ExecuteGet`
2. `GetMetadata("identity_note_id")` → if null, return friendly message; else `GetById` → return `NoteToData`

**MCP flow:**
1. MCP client connects → sends `initialize`
2. `HandleInitialize` calls `GetIdentityContent()` → `GetMetadata` → `GetById` → `note.Content`
3. `serverInfo.instructions` = note content (null → field omitted by `WhenWritingNull`)
4. Client with no auto-load support: calls `tools/call get_identity` → `CallGetIdentity()`

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/IdentityOperator.cs` | CLI identity set/get logic | `IdentityOperator` with `ExecuteSet`, `ExecuteGet` |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Operators/McpServerOperator.cs` | Add `GetIdentityContent()`, inject into `HandleInitialize`, add `get_identity` 8th tool + dispatch + `CallGetIdentity()` | FR-007–013 |
| `src/memctl/Bootstrap/Program.cs` | Add `identity` subcommand group with `set` and `get` | FR-001–006 |

### Integration Code Blocks

---

#### `IdentityOperator.cs` — new file (no old_string needed)

Complete new file content (see §2 New Files implementation below).

---

#### `McpServerOperator.cs` → `HandleInitialize()`

```
// INTEGRATION: McpServerOperator.cs → HandleInitialize()
// old_string:
    private static object HandleInitialize(object id) => new

// new_string (remove static — needs instance field index via GetIdentityContent):
    private object HandleInitialize(object id) => new
    {
        jsonrpc = "2.0",
        id,
        result  = new
        {
            protocolVersion = "2024-11-05",
            capabilities    = new { tools = new { } },
            serverInfo      = new { name = "memctl", version = "1.0.0", instructions = GetIdentityContent() },
        },
    };
```

---

#### `McpServerOperator.cs` → `HandleToolsList()`

```
// INTEGRATION: McpServerOperator.cs → HandleToolsList()
// old_string:
    private static object HandleToolsList(object id) => new

// new_string (full replacement — adds get_identity as 8th tool):
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
                    req: [(\"query\",  \"string\",  \"Search query text\")],
                    opt: [(\"limit\",  \"integer\", \"Max results (default 10)\"),
                          (\"folder\", \"string\",  \"Filter to folder prefix (e.g. crypto)\")]),

                MakeTool("get",
                    "Retrieve a note by ID or file path; increments access_count",
                    req: [(\"id\", \"string\", \"Note ID or relative file path\")],
                    opt: []),

                MakeTool("list",
                    "List notes sorted by importance (weight DESC, access_count DESC)",
                    req: [],
                    opt: [(\"limit\", \"integer\", \"Max results (default 10)\"),
                          (\"tag\",   \"string\",  \"Filter by single tag\")]),

                MakeTool("search_semantic",
                    "Pure vector similarity search over embedded notes",
                    req: [(\"query\",  \"string\",  \"Search query text\")],
                    opt: [(\"limit\",  \"integer\", \"Max results (default 10)\"),
                          (\"folder\", \"string\",  \"Filter to folder prefix\"),
                          (\"scope\",  \"string\",  \"Comma-separated note IDs to restrict to\")]),

                MakeTool("search_tags",
                    "Find notes that have specific tag(s)",
                    req: [(\"tags\", \"string\", \"Comma-separated tag list\")],
                    opt: [(\"match\", \"string\",  \"any or all (default: any)\"),
                          (\"limit\", \"integer\", \"Max results (default 10)\")]),

                MakeTool("search_date",
                    "Find notes by creation date range",
                    req: [],
                    opt: [(\"from\",  \"string\",  \"ISO 8601 start date (inclusive)\"),
                          (\"to\",    \"string\",  \"ISO 8601 end date (inclusive)\"),
                          (\"limit\", \"integer\", \"Max results (default 10)\")]),

                MakeTool("search_links",
                    "Find notes linked to or from a given note (wikilinks graph)",
                    req: [(\"id\", \"string\", \"Note ID or file path\")],
                    opt: [(\"depth\", \"integer\", \"Link traversal depth (default 1)\")]),

                MakeTool("get_identity",
                    "Retrieve the vault identity note — load this first in every session for context",
                    req: [],
                    opt: []),
            },
        },
    };
```

---

#### `McpServerOperator.cs` → `HandleToolsCallAsync()` (switch statement only)

```
// INTEGRATION: McpServerOperator.cs → HandleToolsCallAsync() — switch block
// old_string:
            MemctlOutcome? outcome = name switch
            {
                "search"          => await CallSearchAsync(args, ct),
                "get"             => CallGet(args),
                "list"            => CallList(args),
                "search_semantic" => await CallSearchSemanticAsync(args, ct),
                "search_tags"     => CallSearchTags(args),
                "search_date"     => CallSearchDate(args),
                "search_links"    => CallSearchLinks(args),
                _                 => null,
            };

// new_string:
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
                _                 => null,
            };
```

---

#### `McpServerOperator.cs` — add `CallGetIdentity()` and `GetIdentityContent()` after `CallSearchLinks`

```
// INTEGRATION: McpServerOperator.cs — append after CallSearchLinks()
// old_string (anchor — end of CallSearchLinks method, unique):
        return new SearchLinksOperator(vaultReader, index).Execute(vaultPath, id, depth);
    }

// new_string:
        return new SearchLinksOperator(vaultReader, index).Execute(vaultPath, id, depth);
    }

    private MemctlOutcome CallGetIdentity()
    {
        var noteId = index.GetMetadata("identity_note_id");
        if (noteId is null)
            return MemctlOutcome.Ok("get_identity", "No identity note set", null);
        var note = index.GetById(noteId);
        if (note is null)
            return MemctlOutcome.Ok("get_identity", "Identity note not found (may have been deleted)", null);
        index.IncrementAccess(note.Id);
        return MemctlOutcome.Ok("get_identity", "Identity note", GetOperator.NoteToData(note));
    }

    private string? GetIdentityContent()
    {
        var noteId = index.GetMetadata("identity_note_id");
        if (noteId is null) return null;
        var note = index.GetById(noteId);
        return note?.Content;
    }
```

---

#### `Program.cs` — add identity subcommand group before `return await root.InvokeAsync(args)`

```
// INTEGRATION: Program.cs — append identity command group
// old_string (anchor — weight command block end, unique):
root.AddCommand(weightCmd);

return await root.InvokeAsync(args);

// new_string:
root.AddCommand(weightCmd);

// --- identity ---
var identityCmd = new Command("identity", "Manage vault identity note (Layer 0 context)");

var idSetIdArg = new Argument<string>("id", "Note ID or file path");
var idSetCmd   = new Command("set", "Designate a note as the vault identity note");
idSetCmd.AddArgument(idSetIdArg);
idSetCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var outcome = new IdentityOperator(vaultReader, noteIndex).ExecuteSet(
        vault,
        ctx.ParseResult.GetValueForArgument(idSetIdArg));
    ResultPrinter.Print(outcome);
    ctx.ExitCode = outcome.Success ? 0 : 1;
});
identityCmd.AddCommand(idSetCmd);

var idGetCmd = new Command("get", "Retrieve current identity note");
idGetCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    ResultPrinter.Print(new IdentityOperator(vaultReader, noteIndex).ExecuteGet(vault));
});
identityCmd.AddCommand(idGetCmd);

root.AddCommand(identityCmd);

return await root.InvokeAsync(args);
```

---

### Deleted Files

None.

---

## 3. Data Model

### New Models/Schemas

None — uses existing `metadata` table:
```sql
-- Already exists (task #4):
CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)
```

New key stored:
```
key: "identity_note_id"
value: <note id string, e.g. "20240101120000-notes-me">
```

### Migrations Needed

None — `metadata` table already created by `ApplySchema()`.

### Data Flow

**Set identity:**
```
CLI → IdentityOperator.ExecuteSet → index.SetMetadata("identity_note_id", note.Id)
                                  → index.SetWeight(note.Id, 1.0f)
```

**MCP initialize:**
```
MCP client connect → HandleInitialize → GetIdentityContent()
  → index.GetMetadata("identity_note_id") → null? return null
  → index.GetById(noteId) → null? return null
  → return note.Content → serverInfo.instructions
```

---

## 4. API Design

N/A — CLI tool and MCP stdio protocol, not HTTP API.

### MCP Protocol Changes

**Modified message: `initialize` response**

Before (task #3):
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": { "tools": {} },
    "serverInfo": { "name": "memctl", "version": "1.0.0" }
  }
}
```

After (with identity set):
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": { "tools": {} },
    "serverInfo": {
      "name": "memctl",
      "version": "1.0.0",
      "instructions": "<identity note content here>"
    }
  }
}
```

After (no identity set) — `instructions` key absent:
```json
{
  "result": {
    "serverInfo": { "name": "memctl", "version": "1.0.0" }
  }
}
```

**New tool: `get_identity`**

`tools/call` request:
```json
{ "name": "get_identity", "arguments": {} }
```

Response (identity set):
```json
{
  "content": [{
    "type": "text",
    "text": "{\"success\":true,\"action\":\"get_identity\",\"message\":\"Identity note\",\"data\":{\"id\":\"...\",\"title\":\"...\",\"content\":\"...\"}}"
  }],
  "isError": false
}
```

Response (no identity):
```json
{
  "content": [{ "type": "text", "text": "{\"success\":true,\"action\":\"get_identity\",\"message\":\"No identity note set\",\"data\":null}" }],
  "isError": false
}
```

---

## 5. UI Components

N/A — CLI tool.

---

## 6. Business Logic

### FR-001/002: identity set

1. NeedsIngest guard → ingest if stale
2. `index.Initialize(DbPath(vaultPath))` — idempotent (Initialize guard)
3. `index.GetById(idOrPath)` — try direct ID first
4. `?? index.GetByFilePath(idOrPath)` — fall back to file path
5. If null → `MemctlOutcome.Fail("identity", "Note not found: {idOrPath}")`
6. `index.SetMetadata("identity_note_id", note.Id)` — persist designation
7. `index.SetWeight(note.Id, 1.0f)` — auto-pin weight
8. Return `Ok("identity", "Identity note set: {note.Title}", { id, file, title })`

### FR-004/005: identity get

1. NeedsIngest guard → ingest if stale
2. Initialize index
3. `index.GetMetadata("identity_note_id")` → null → `Ok("identity", "No identity note set", null)`
4. `index.GetById(noteId)` → null → `Ok("identity", "Identity note not found (may have been deleted)", null)`
5. Return `Ok("identity", "Identity note", GetOperator.NoteToData(note))`

### FR-007/008/009: MCP initialize instructions

`GetIdentityContent()` (instance method on McpServerOperator):
1. `index.GetMetadata("identity_note_id")` → null → return null
2. `index.GetById(noteId)` → null → return null (stale ID edge case FR-009)
3. Return `note.Content`

`serverInfo.instructions = GetIdentityContent()` → null omitted by `WhenWritingNull` (JsonOpts already configured).

### Validation Rules

| Input | Rule | Error |
|-------|------|-------|
| `idOrPath` for `identity set` | Must resolve to an existing note via GetById or GetByFilePath | "Note not found: {idOrPath}" |

---

## 7. Error Handling Strategy

| Scenario | Handling | Message | FR |
|----------|----------|---------|-----|
| Note not found in identity set | Fail outcome, exit 1 | "Note not found: {id}" | FR-006 |
| No identity set in identity get | Success outcome, exit 0 | "No identity note set" | FR-005 |
| Stale identity ID (note deleted) | Success outcome, null data | "Identity note not found (may have been deleted)" | FR-009, §5 edge case 1 |
| No vault flag | RequireVault guard, exit 1 | "--vault is required" | §5 edge case 4 |
| MCP — no identity set | `instructions` omitted (null) | — (transparent to client) | FR-008 |
| MCP get_identity — no identity | isError: false, data: null | "No identity note set" | FR-012 |

---

## 8. Security Considerations

- **Input validation**: `idOrPath` is looked up via `GetById`/`GetByFilePath` — both use parameterized SQL, no injection risk
- **Metadata key**: hardcoded `"identity_note_id"` string — no user-controlled key lookup
- **Content exposure**: `serverInfo.instructions` exposes note content to MCP client — same as the `get` tool; acceptable since vault is local user data

---

## 9. Performance Considerations

- `GetIdentityContent()` runs on every MCP `initialize` call — two `SELECT` queries (metadata + note by id), both indexed by primary key. Negligible overhead.
- `identity set` auto-sets weight=1.0 — single `UPDATE notes SET weight = @w WHERE id = @id`, O(1).

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| E2E/smoke | CLI identity set/get happy + error paths | `dotnet run --project` smoke | ~4 scenarios |
| Unit (manual) | MCP initialize JSON shape with/without identity | MCP JSON-RPC over stdin/stdout | verify by inspection |

### Key Test Cases

1. `identity set <valid-id>` → success json, weight=1.0 confirmed via `list`
2. `identity set nonexistent` → failure json, exit 1
3. `identity get` after set → note content returned
4. `identity get` with no identity → success json, "No identity note set", exit 0

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected Output | FR |
|----------|---------|-----------------|-----|
| identity set by ID | `dotnet run --project src/memctl -- --vault <vault> identity set <existing-note-id>` | stdout contains `"success":true` and `"action":"identity"`, exit 0 | FR-001 |
| identity set by path | `dotnet run --project src/memctl -- --vault <vault> identity set notes/test.md` | stdout contains `"success":true`, exit 0 | FR-002 |
| identity set nonexistent | `dotnet run --project src/memctl -- --vault <vault> identity set does-not-exist` | stdout contains `"success":false`, exit 1 | FR-006 |
| identity get after set | `dotnet run --project src/memctl -- --vault <vault> identity get` | stdout contains `"success":true` and `"title"`, exit 0 | FR-004 |
| identity get unset | fresh vault — `identity get` | stdout contains `"success":true` and `"No identity"`, exit 0 | FR-005 |

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| (none) | — | All infrastructure already present | No |

---

## 12. Implementation Order

1. **`IdentityOperator.cs`** — new file, no dependencies on other changes
2. **`McpServerOperator.cs`** — modify HandleInitialize (remove static), add GetIdentityContent + CallGetIdentity, extend HandleToolsList + dispatch switch
3. **`Program.cs`** — wire identity subcommand group

Build command: `dotnet build src/memctl --warnaserrors`

---

## 13. Assumptions & Open Design Decisions

- [x] All design decisions resolved via autoresearch (6/6 score)
- Assumption: `GetOperator.NoteToData` is accessible from `IdentityOperator` and `McpServerOperator` (both same assembly, `internal` visibility) — confirmed by grep

---

## 14. Traceability Matrix

| Requirement | Design Section | Files | Notes |
|-------------|---------------|-------|-------|
| FR-001 | §6 identity set | IdentityOperator.cs, Program.cs | ExecuteSet → SetMetadata + SetWeight |
| FR-002 | §6 identity set step 4 | IdentityOperator.cs | GetByFilePath fallback |
| FR-003 | §6 identity set step 7 | IdentityOperator.cs | SetWeight(1.0f) auto-pin |
| FR-004 | §6 identity get | IdentityOperator.cs, Program.cs | ExecuteGet → NoteToData |
| FR-005 | §7 error table | IdentityOperator.cs | null metadata → Ok, exit 0 |
| FR-006 | §7 error table | IdentityOperator.cs | null note → Fail, exit 1 |
| FR-007 | §4 MCP changes, §6 initialize | McpServerOperator.cs | GetIdentityContent → instructions |
| FR-008 | §4 MCP changes | McpServerOperator.cs | null → WhenWritingNull omits field |
| FR-009 | §6 GetIdentityContent step 2 | McpServerOperator.cs | GetById null → return null |
| FR-010 | §2 Integration Blocks HandleToolsList | McpServerOperator.cs | 8th tool entry |
| FR-011 | §4 MCP tool response | McpServerOperator.cs | CallGetIdentity → NoteToData |
| FR-012 | §7 error table | McpServerOperator.cs | null noteId → Ok, isError:false |
| FR-013 | §6 CallGetIdentity | McpServerOperator.cs | IncrementAccess after GetById |
| NFR-001 | §3 data model | SqliteNoteIndex.cs (unchanged) | metadata table survives re-ingest |
| NFR-002 | §7 error table | All operators | No throws on null identity |
| NFR-003 | §2 (no INoteIndex changes) | INoteIndex.cs (unchanged) | Confirmed |
| NFR-004 | §6 | IdentityOperator.cs | Same constructor, same pattern as WeightOperator |
