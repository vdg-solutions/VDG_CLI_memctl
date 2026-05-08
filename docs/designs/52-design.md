# Technical Design: General Event Logging System (EventLog)

**Spec:** docs/specs/52-spec.md
**Task:** 52
**Date:** 2026-05-08
**Status:** Draft

---

## 1. Architecture Overview

A horizontal concern across the Operators layer. `EventLog.cs` already exists and is fully functional — this task adds 7 call sites to write-path operators that currently have none, and fixes a pre-existing SQL bug in `SqliteNoteIndex.SearchBm25` where `archived` notes are not filtered.

No new files. No new layers. No interface changes.

### System Context

Every write-path operator ends with `return MemctlOutcome.Ok(...)`. EventLog call is inserted immediately before that return — after the state mutation succeeds. Pattern: fire-and-forget (best-effort try/catch inside `EventLog.Record`). Operator execution never blocked.

`SqliteNoteIndex.SearchBm25` is used by: user-facing `memctl search`, `DistillOperator` (link validation). Adding `AND n.archived = 0` makes it consistent with `GetAll()` and prevents event notes from surfacing in either context after `memctl ingest`.

---

## 2. File Changes

### New Files

None.

### Modified Files

| File | Change | FR |
|------|--------|----|
| `src/memctl/Operators/AddOperator.cs` | Add `EventLog.Record` before final return | FR-001 |
| `src/memctl/Operators/DeleteOperator.cs` | Add `EventLog.Record` before final return | FR-002 |
| `src/memctl/Operators/WeightOperator.cs` | Add `EventLog.Record` before final return | FR-003 |
| `src/memctl/Operators/DecayOperator.cs` | Add `EventLog.Record` before final return (non-dryRun guard) | FR-004 |
| `src/memctl/Operators/OrganizeOperator.cs` | Add `EventLog.Record` before final return | FR-005 |
| `src/memctl/Operators/MigrateTagsOperator.cs` | Add `EventLog.Record` before final return (non-dryRun guard) | FR-006 |
| `src/memctl/Operators/DistillOperator.cs` | Add `EventLog.Record` before final return (non-dryRun + pending>0 guard) | FR-007 |
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs` | Add `AND n.archived = 0` to `SearchBm25` SQL | FR-008, FR-009 |

### Integration Code Blocks

---

#### `AddOperator.cs` → `ExecuteAsync()`

```
// INTEGRATION: AddOperator.cs → ExecuteAsync()
// old_string:
        return MemctlOutcome.Ok("add", $"Added note: {note.Title}", withEmbed);

// new_string:
        EventLog.Record(vaultPath, "operator_run", "info", "add", $"Added {note.Title} → {filePath}");
        return MemctlOutcome.Ok("add", $"Added note: {note.Title}", withEmbed);
```

---

#### `DeleteOperator.cs` → `Execute()`

```
// INTEGRATION: DeleteOperator.cs → Execute()
// old_string:
        index.Delete(noteId);
        return MemctlOutcome.Ok("delete", $"Deleted: {note.FilePath}", note);

// new_string:
        index.Delete(noteId);
        EventLog.Record(vaultPath, "operator_run", "info", "delete", $"Deleted {note.FilePath}");
        return MemctlOutcome.Ok("delete", $"Deleted: {note.FilePath}", note);
```

---

#### `WeightOperator.cs` → `Execute()`

```
// INTEGRATION: WeightOperator.cs → Execute()
// old_string:
        var rounded = (float)Math.Round(clamped, 2);
        return MemctlOutcome.Ok("weight", $"Weight set to {rounded}",
            new WeightChange(note.Id, note.FilePath, rounded));

// new_string:
        var rounded = (float)Math.Round(clamped, 2);
        EventLog.Record(vaultPath, "operator_run", "info", "weight", $"{note.FilePath} → {rounded}");
        return MemctlOutcome.Ok("weight", $"Weight set to {rounded}",
            new WeightChange(note.Id, note.FilePath, rounded));
```

---

#### `DecayOperator.cs` → `Execute()`

```
// INTEGRATION: DecayOperator.cs → Execute()
// old_string:
        return MemctlOutcome.Ok("decay", $"Decayed {decayed} notes, archived {newlyArchived}",
            new DecayReport(decayed, newlyArchived, unchanged, alreadyArchived, null, dryRun));

// new_string:
        if (!dryRun)
            EventLog.Record(vaultPath, "operator_run", "info", "decay", $"Decayed {decayed}, archived {newlyArchived}");
        return MemctlOutcome.Ok("decay", $"Decayed {decayed} notes, archived {newlyArchived}",
            new DecayReport(decayed, newlyArchived, unchanged, alreadyArchived, null, dryRun));
```

---

#### `OrganizeOperator.cs` → `ExecuteAsync()`

```
// INTEGRATION: OrganizeOperator.cs → ExecuteAsync()
// old_string:
        return MemctlOutcome.Ok("organize", $"Organized {updated} notes",
            new OrganizeReport(updated, errors, vaultPath));

// new_string:
        EventLog.Record(vaultPath, "operator_run", "info", "organize", $"Organized {updated} notes");
        return MemctlOutcome.Ok("organize", $"Organized {updated} notes",
            new OrganizeReport(updated, errors, vaultPath));
```

---

#### `MigrateTagsOperator.cs` → `Execute()`

```
// INTEGRATION: MigrateTagsOperator.cs → Execute()
// old_string:
        var msg = dryRun
            ? $"Dry run: would modify {notesModified}/{notesScanned} notes"
            : $"Migrated tags in {notesModified}/{notesScanned} notes";
        return MemctlOutcome.Ok("migrate-tags", msg, report);

// new_string:
        var msg = dryRun
            ? $"Dry run: would modify {notesModified}/{notesScanned} notes"
            : $"Migrated tags in {notesModified}/{notesScanned} notes";
        if (!dryRun)
            EventLog.Record(vaultPath, "operator_run", "info", "migrate-tags",
                $"Migrated {notesModified}/{notesScanned} notes, {tagsReplaced} replaced, {tagsRemoved} removed");
        return MemctlOutcome.Ok("migrate-tags", msg, report);
```

---

#### `DistillOperator.cs` → `ExecuteAsync()`

```
// INTEGRATION: DistillOperator.cs → ExecuteAsync()
// old_string:
        var msg = dryRun
            ? $"dry-run: {pending.Count} conversations, {totalExtracted} extractions"
            : $"{pending.Count} conversations distilled, {totalExtracted} notes extracted";
        return MemctlOutcome.Ok("distill", msg);

// new_string:
        var msg = dryRun
            ? $"dry-run: {pending.Count} conversations, {totalExtracted} extractions"
            : $"{pending.Count} conversations distilled, {totalExtracted} notes extracted";
        if (!dryRun && pending.Count > 0)
            EventLog.Record(vaultPath, "operator_run", "info", "distill", msg);
        return MemctlOutcome.Ok("distill", msg);
```

---

#### `SqliteNoteIndex.cs` → `SearchBm25()`

```
// INTEGRATION: SqliteNoteIndex.cs → SearchBm25()
// old_string:
    public IReadOnlyList<SearchHit> SearchBm25(string query, int limit, string? folderPrefix = null)
    {
        var prefix       = NormalizePrefix(folderPrefix);
        var folderClause = prefix is not null ? " AND n.file_path LIKE @prefix" : "";
        using var cmd = Cmd($@"
            SELECT n.*, bm25(notes_fts) AS rank
            FROM notes_fts f
            JOIN notes n ON n.rowid = f.rowid
            WHERE notes_fts MATCH @q{folderClause}
            ORDER BY rank
            LIMIT @limit",
            ("@q", EscapeFts(query)),
            ("@limit", limit));
        if (prefix is not null) cmd.Parameters.AddWithValue("@prefix", prefix);

        var hits = new List<SearchHit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var note = ReadNote(r);
            hits.Add(new SearchHit
            {
                Note    = note,
                Score   = r.IsDBNull(r.GetOrdinal("rank")) ? 0f : (float)Math.Abs(r.GetDouble(r.GetOrdinal("rank"))),
                Snippet = Snippet(note.Content, query),
            });
        }
        return hits;
    }

// new_string:
    public IReadOnlyList<SearchHit> SearchBm25(string query, int limit, string? folderPrefix = null)
    {
        var prefix       = NormalizePrefix(folderPrefix);
        var folderClause = prefix is not null ? " AND n.file_path LIKE @prefix" : "";
        using var cmd = Cmd($@"
            SELECT n.*, bm25(notes_fts) AS rank
            FROM notes_fts f
            JOIN notes n ON n.rowid = f.rowid
            WHERE notes_fts MATCH @q
              AND n.archived = 0{folderClause}
            ORDER BY rank
            LIMIT @limit",
            ("@q", EscapeFts(query)),
            ("@limit", limit));
        if (prefix is not null) cmd.Parameters.AddWithValue("@prefix", prefix);

        var hits = new List<SearchHit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var note = ReadNote(r);
            hits.Add(new SearchHit
            {
                Note    = note,
                Score   = r.IsDBNull(r.GetOrdinal("rank")) ? 0f : (float)Math.Abs(r.GetDouble(r.GetOrdinal("rank"))),
                Snippet = Snippet(note.Content, query),
            });
        }
        return hits;
    }
```

---

## 3. Data Model

No new models or migrations. `EventLog.Record` writes markdown files to `events/` — no schema change.

---

## 4. API Design

CLI-only. No new commands or options.

---

## 5. UI Components

N/A — CLI tool.

---

## 6. Business Logic

### Call Site Pattern (all 7 operators)

Insert `EventLog.Record(...)` immediately before the final `return MemctlOutcome.Ok(...)`.
Never before early-exit Fail returns. Never on dryRun paths (Decay, MigrateTags, Distill).

**Guard rules:**
- `DecayOperator`: `if (!dryRun)` — skip on dry run
- `MigrateTagsOperator`: `if (!dryRun)` — skip on dry run
- `DistillOperator`: `if (!dryRun && pending.Count > 0)` — skip on dry run AND no-op

### SearchBm25 Fix

Append `AND n.archived = 0` as a hard filter on the `notes` table (not on FTS). Position after `WHERE notes_fts MATCH @q` and before any `folderClause` suffix. The `{folderClause}` string already starts with ` AND` so ordering is safe.

---

## 7. Error Handling Strategy

| Scenario | Handling | FR |
|----------|----------|----|
| `events/` dir unwritable | `EventLog.Record` has outer try/catch — swallows silently, operator proceeds | NFR-001 |
| Vault path null/empty | Same try/catch — best-effort | NFR-001 |
| SearchBm25 with archived note | `AND n.archived = 0` filters it — returns empty, never throws | FR-008 |

---

## 8. Security Considerations

No auth surface changes. `EventLog.Record` uses string interpolation — payload is internal operator output, not user-supplied raw input at this call site.

---

## 9. Performance Considerations

`EventLog.Record` is file I/O (one small markdown write) with no index interaction. Overhead is negligible per operation.

Adding `AND n.archived = 0` to `SearchBm25` SQL: the `notes` table has an `archived` INTEGER column. If the table grows large a partial index on `archived = 0` would help, but at current scale (thousands of notes) the filter is effectively free.

---

## 10. Testing Strategy

| Level | What | Count |
|-------|------|-------|
| Unit | `EventLog.Record` called for each operator — verify `events/` file exists with correct `source:` frontmatter | 7 tests |
| Unit | dryRun operators produce no event file | 2 tests (Decay, MigrateTags) |
| Unit | Distill no-op (pending=0) produces no event file | 1 test |
| Unit | `SearchBm25` excludes `archived=1` notes | 1 test |
| Unit | `SearchBm25` and `GetAll()` return consistent non-archived set | 1 test |

### Key Test Cases

1. `AddOperator` — call `ExecuteAsync`, assert `Directory.EnumerateFiles(vault/events/)` has 1 file, read frontmatter, assert `source: add`.
2. `DecayOperator dryRun=true` — assert `events/` directory empty (or unchanged).
3. `DecayOperator dryRun=false` — assert `events/` has file with `source: decay`.
4. `SearchBm25 archived filter` — insert note with `archived=1` via `index.Upsert`, call `SearchBm25`, assert result count = 0.
5. `DistillOperator pending=0` — assert no event file (no-op path).

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected | FR |
|----------|---------|----------|----|
| add creates event | `memctl add "test" --vault {tmp}` then `ls {tmp}/events/` | 1 file present | FR-001 |
| search excludes archived | `memctl ingest --vault {tmp}` then `memctl search "operator_run" --vault {tmp}` | 0 results for event notes | FR-008 |

---

## 11. Dependencies

No new packages. All changes are in-project.

---

## 12. Implementation Order

1. `SqliteNoteIndex.cs` — `SearchBm25` SQL fix (isolated, no dependencies)
2. `AddOperator.cs` — add EventLog call
3. `DeleteOperator.cs` — add EventLog call
4. `WeightOperator.cs` — add EventLog call
5. `DecayOperator.cs` — add EventLog call with dryRun guard
6. `OrganizeOperator.cs` — add EventLog call
7. `MigrateTagsOperator.cs` — add EventLog call with dryRun guard
8. `DistillOperator.cs` — add EventLog call with dryRun + pending guard
9. Unit tests for all 8 changes

---

## 13. Assumptions & Open Design Decisions

- `DistillOperator` no-op path (line 29: `"no conversations to distill"`) intentionally skipped — nothing changed, no event needed. Confirmed by spec §5.
- `SearchBm25` archived filter also affects `DistillOperator`'s internal link validation (line 64). This is correct — archived notes are weight ≤ 0, should not be linked to in extracted notes.
- `HookLog` entirely untouched — `HookStatusOperator` and `Program.cs` hook sites unchanged.

---

## 14. Traceability Matrix

| Requirement | Section | Files | Tests |
|-------------|---------|-------|-------|
| FR-001 | §2 AddOperator block | `AddOperator.cs` | add-event test |
| FR-002 | §2 DeleteOperator block | `DeleteOperator.cs` | delete-event test |
| FR-003 | §2 WeightOperator block | `WeightOperator.cs` | weight-event test |
| FR-004 | §2 DecayOperator block | `DecayOperator.cs` | decay-event + dryRun test |
| FR-005 | §2 OrganizeOperator block | `OrganizeOperator.cs` | organize-event test |
| FR-006 | §2 MigrateTagsOperator block | `MigrateTagsOperator.cs` | migrate-event + dryRun test |
| FR-007 | §2 DistillOperator block | `DistillOperator.cs` | distill-event + noop test |
| FR-008 | §2 SearchBm25 block | `SqliteNoteIndex.cs` | bm25-archived test |
| FR-009 | §2 SearchBm25 block | `SqliteNoteIndex.cs` | bm25-getall consistency test |
| FR-010 | §13 (no change) | `HookLog.cs`, `HookStatusOperator.cs` | existing tests unchanged |
| FR-011 | §13 (no change) | `Program.cs` | capture single-event test |
| NFR-001 | §7 | `EventLog.cs` (unchanged) | error-handling test |
| NFR-002 | §6 | `EventLog.cs` (unchanged) | disk-only test |
| NFR-004 | §10.5 | `SqliteNoteIndex.cs` | smoke: search excludes archived |
