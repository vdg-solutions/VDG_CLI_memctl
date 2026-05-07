---
id: 34
type: task
title: Prune stale DB entries on ingest when vault files are deleted
status: Done
priority: high
tags:
- bug
- ingest
- data-integrity
- index
created: 2026-05-04
updated: 2026-05-07
---

## Description

`IngestOperator.Execute()` currently only adds or updates entries in SQLite index — it enumerates all `.md` files on disk and calls `index.Upsert()` for each. It never removes rows whose backing files have been deleted. Result: deleted vault notes persist in search, `context-inject`, and `memctl list` output until the index database is manually deleted and re-ingested.

This is a data integrity bug. Users who delete session notes, stale patterns, or old QC scores will still see those notes injected into every Claude Code prompt via the `UserPromptSubmit` hook, consuming context window tokens with dead data.

The fix adds a prune phase before the upsert loop: collect all file paths from the index, compare against files actually on disk, delete unmatched entries. `INoteIndex.Delete(string noteId)` already exists and is tested.

## Implementation

### Step 0 — Prereq fail-fast
- Verify `dotnet test` passes baseline: `dotnet test tests/memctl.Tests/` → 57/57 pass

### Step 1 — Extract file-path index query method to `INoteIndex`
- **File MODIFY:** `src/memctl/CoreAbstractions/Ports/INoteIndex.cs` — add `IReadOnlyList<(string Id, string FilePath)> GetAllFilePaths();` to interface
- **File MODIFY:** `src/memctl/Implementations/Index/SqliteNoteIndex.cs` — implement `GetAllFilePaths()`:
  ```csharp
  public IReadOnlyList<(string Id, string FilePath)> GetAllFilePaths()
  {
      using var cmd = _db!.CreateCommand();
      cmd.CommandText = "SELECT id, file_path FROM notes";
      var results = new List<(string, string)>();
      using var reader = cmd.ExecuteReader();
      while (reader.Read())
          results.Add((reader.GetString(0), reader.GetString(1)));
      return results;
  }
  ```

### Step 2 — Add prune logic to `IngestOperator.Execute()`
- **File MODIFY:** `src/memctl/Operators/IngestOperator.cs` — after `index.Initialize(dbPath)`, before the file enumeration loop:

```csharp
// Prune stale entries: delete index rows whose backing .md files are gone
var indexedPaths = index.GetAllFilePaths();
var diskPaths = vault.EnumerateMarkdownFiles(vaultPath)
    .Select(f => Path.GetRelativePath(vaultPath, f).Replace('\\', '/'))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

int pruned = 0;
foreach (var (id, filePath) in indexedPaths)
{
    if (!diskPaths.Contains(filePath))
    {
        index.Delete(id);
        pruned++;
    }
}
```

### Step 3 — Return prune count in `IngestReport` + update DTO + mapper
- **File MODIFY:** `src/memctl/CoreAbstractions/Entities/IngestReport.cs` — add `int Pruned` field
- **File MODIFY:** `src/memctl/Boundary/MemctlResult.cs` — add `[JsonPropertyName("pruned")] public int Pruned { get; init; }` to `IngestReportDto`
- **File MODIFY:** `src/memctl/Operators/Mapping/MemctlResultMapper.cs` — add `Pruned = i.Pruned` to `MapIngest()`
- **File MODIFY:** `src/memctl/Operators/IngestOperator.cs` — include `pruned` count in `IngestReport` and log message

### Step 4 — Unit tests
- **File CREATE/MODIFY:** `tests/memctl.Tests/Operators/IngestOperatorPruneTests.cs`
  1. `Prune_RemovesDeletedFileEntry`: write note → ingest → delete file → re-ingest → `GetAll()` excludes deleted note
  2. `Prune_KeepsExistingFiles`: write 2 notes → ingest → delete 1 file → re-ingest → remaining note still present
  3. `Prune_NoOpWhenNoDeletions`: write note → ingest → re-ingest → same count, no deletions

### Step 5 — Smoke test
```bash
# Add test note, capture file path
NOTE_ID=$(memctl add "test prune note" --title "test-prune-note" 2>&1 | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['id'])")
NOTE_FILE=$(memctl get "$NOTE_ID" 2>&1 | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['file'])")
memctl list 2>&1 | python3 -c "import sys,json; assert any(n['id']=='$NOTE_ID' for n in json.load(sys.stdin)['data']['notes'])" && echo "PASS: note present"

# Delete file and re-ingest
rm "$NOTE_FILE"
memctl ingest

# Verify removed
memctl list 2>&1 | python3 -c "import sys,json; assert not any(n['id']=='$NOTE_ID' for n in json.load(sys.stdin)['data']['notes'])" && echo "PASS: note pruned"
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | Deleting a vault `.md` file then running `memctl ingest` removes its index entry | `memctl list` output excludes deleted note; `memctl stats` note_count decrements |
| FR-2 | `context-inject` does not return deleted notes | `echo "test prune" \| memctl context-inject` → no deleted note in output |
| FR-3 | Ingest of unchanged vault is a no-op (no false positives) | Run ingest twice → second run `pruned=0` |
| FR-4 | Prune count appears in `IngestReport` and CLI output | `memctl ingest` shows `"pruned": N` in JSON output; N>0 when files were deleted |
| NFR-1 | Pruning does not measurably slow ingest on 1000-note vault | `< 50ms added` — single `SELECT id, file_path` + `HashSet` lookup per entry |
| NFR-2 | `dotnet test` passes full test suite including new tests | `dotnet test tests/memctl.Tests/` → 60+/60+ pass (57 baseline + ≥3 new) |

## Out of Scope
- Batch/prune-only command — no separate `memctl prune` subcommand. Prune is always coupled to ingest.
- Recursive orphan link cleanup — pruned notes' backlinks in other notes are not cleaned up. Scope creep.

## Dependencies
- Blocked by: none
- Soft depend: none

## Risk

| Risk | Mitigation |
|------|-----------|
| Case-sensitivity mismatch Windows vs Linux file paths | Use `StringComparer.OrdinalIgnoreCase` for `HashSet`; `GetRelativePath` normalizes separators |
| Pruning a note still being written (race with concurrent writer) | Ingest is single-threaded per vault; no concurrent writes in current architecture |
| Large vault (10k notes) — `SELECT id, file_path` returns many rows | Query is `O(n)` index scan, no joins. Acceptable for 10k rows (<10ms). Add limit if profiling shows issue |
| Deleted `.md` but external tool re-creates same path before next ingest | Ingest will re-upsert the new file. Prune happens before upsert loop, so briefly absent then re-added. Correct behavior |

## Effort
~3h:
- Interface + SqliteNoteIndex impl: 0.5h
- IngestOperator prune logic: 0.5h
- IngestReport field: 0.25h
- Unit tests (3 cases): 1h
- Smoke test + verify: 0.5h
- PR + review: 0.25h

## Comments

**2026-05-07 10:22 user:** Pipeline complete. Implemented in feat(34) — already merged to main.
