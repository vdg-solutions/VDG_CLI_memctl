# Technical Design: Prune stale DB entries on ingest when vault files are deleted

**Spec:** docs/specs/34-spec.md
**Task:** 34
**Date:** 2026-05-04
**Status:** Draft

## 1. Architecture Overview

Single horizontal concern in the Operators layer. No new files — all changes fit within existing `IngestOperator.Execute()` method, with supporting additions to index interface, implementation, report record, DTO, and mapper.

Data flow: `EnumerateMarkdownFiles(vaultPath)` → build `HashSet` of relative paths on disk → `index.GetAllFilePaths()` → compare → `index.Delete(stale_id)` → then normal upsert loop.

### System Context

1. User (or auto-capture Stop hook) calls `memctl ingest` or `memctl capture` (which auto-calls ingest)
2. `IngestOperator.Execute()` runs prune phase BEFORE upsert loop
3. Stale entries deleted from `SqliteNoteIndex` (SQLite `notes` table)
4. Normal upsert loop proceeds as before
5. `IngestReport` now carries `Pruned` count
6. CLI prints `"pruned": N` in JSON output

## 2. File Changes

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/CoreAbstractions/Ports/INoteIndex.cs` | Add `GetAllFilePaths()` signature | Needed to diff index vs disk |
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs` | Implement `GetAllFilePaths()` | Single `SELECT id, file_path` |
| `src/memctl/Operators/IngestOperator.cs` | Add prune phase before upsert loop | FR-1, FR-2, FR-3 |
| `src/memctl/CoreAbstractions/Entities/IngestReport.cs` | Add `int Pruned` field | FR-4 |
| `src/memctl/Boundary/MemctlResult.cs` | Add `Pruned` to `IngestReportDto` | FR-4 |
| `src/memctl/Operators/Mapping/MemctlResultMapper.cs` | Map `Pruned` field | FR-4 |

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `tests/memctl.Tests/Operators/IngestOperatorPruneTests.cs` | 3 unit tests | `Prune_RemovesDeletedFileEntry`, `Prune_KeepsExistingFiles`, `Prune_NoOpWhenNoDeletions` |

### Integration Code Blocks

#### INTEGRATION: INoteIndex.cs → interface body (add method)

```
// old_string (unique anchor):
    void    IncrementAccess(string noteId);

// new_string:
    void    IncrementAccess(string noteId);
    IReadOnlyList<(string Id, string FilePath)> GetAllFilePaths();
```

#### INTEGRATION: SqliteNoteIndex.cs → Delete() method (add method after)

```
// old_string (unique anchor):
    public void Delete(string noteId) =>
        Exec("DELETE FROM notes WHERE id = @id", ("@id", noteId));

// new_string:
    public void Delete(string noteId) =>
        Exec("DELETE FROM notes WHERE id = @id", ("@id", noteId));

    public IReadOnlyList<(string Id, string FilePath)> GetAllFilePaths()
    {
        if (_db is null) return [];
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, file_path FROM notes";
        var results = new List<(string, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }
```

#### INTEGRATION: IngestOperator.cs → Execute() method (complete replacement)

```
// old_string (unique anchor):
    public MemctlOutcome Execute(string vaultPath)

// new_string:
    public MemctlOutcome Execute(string vaultPath)
    {
        if (!Directory.Exists(vaultPath))
            return MemctlOutcome.Fail("ingest", $"Vault not found: {vaultPath}");

        var dbPath = DbPath(vaultPath);
        index.Initialize(dbPath);

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

        var files = vault.EnumerateMarkdownFiles(vaultPath).ToList();
        var added = 0;

        foreach (var file in files)
        {
            try
            {
                var note = vault.ParseNote(file, vaultPath);
                if (embedding != null)
                {
                    var emb = embedding.Embed($"{note.Title}\n{note.Content}");
                    note = note with { Embedding = emb };
                }
                index.Upsert(note);
                added++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  skip {file}: {ex.Message}");
            }
        }

        if (embedding != null)
        {
            index.SetMetadata("model_name", embedding.ModelName);
            index.SetMetadata("model_dim",  embedding.Dim.ToString());
        }

        var model = embedding?.ModelName ?? "none";

        string? semanticHint = null;
        var lastLint = index.GetMetadata("last_semantic_lint");
        if (lastLint is null)
        {
            semanticHint = "Semantic lint: never run. Run: memctl lint --semantic";
        }
        else if (DateTime.TryParse(lastLint, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lastLintDate))
        {
            var daysSince = (DateTime.UtcNow - lastLintDate).TotalDays;
            if (daysSince > SemanticOverdueDays)
                semanticHint = $"Semantic lint not run in {(int)daysSince} days. Run: memctl lint --semantic";
        }
        else
        {
            semanticHint = "Semantic lint: never run. Run: memctl lint --semantic";
        }

        return MemctlOutcome.Ok("ingest", $"Indexed {added}/{files.Count} notes, pruned {pruned}",
            new IngestReport(added, files.Count, vaultPath, model, semanticHint, pruned));
    }
```

#### INTEGRATION: IngestReport.cs → record definition (complete replacement)

```
// old_string (unique anchor):
public sealed record IngestReport(

// new_string:
public sealed record IngestReport(
    int     Indexed,
    int     Total,
    string  Vault,
    string  Model,
    string? SemanticLintHint,
    int     Pruned = 0);
```

#### INTEGRATION: MemctlResult.cs → IngestReportDto class (add field)

```
// old_string (unique anchor):
    [JsonPropertyName("semantic_lint_hint")] public string? SemanticLintHint { get; init; }

// new_string:
    [JsonPropertyName("semantic_lint_hint")] public string? SemanticLintHint { get; init; }
    [JsonPropertyName("pruned")]             public int     Pruned            { get; init; }
```

#### INTEGRATION: MemctlResultMapper.cs → MapIngest() method (add field)

```
// old_string (unique anchor):
        SemanticLintHint = i.SemanticLintHint,

// new_string:
        SemanticLintHint = i.SemanticLintHint,
        Pruned           = i.Pruned,
```

## 3. Data Model

### No schema changes

No new tables or columns. Uses existing `notes` table columns `id` and `file_path` for the prune query.

### Data Flow

1. `index.GetAllFilePaths()` → `List<(string Id, string FilePath)>` from `SELECT id, file_path FROM notes`
2. `vault.EnumerateMarkdownFiles(vaultPath)` → disk files, normalize paths with `.Replace('\\', '/')`
3. Build `HashSet<string>` of disk paths (O(1) lookup)
4. For each indexed path not in disk set → `index.Delete(id)`
5. Proceed to normal upsert loop

## 4. API Design

No new endpoints. This is a CLI tool, not a web app.

## 5. UI Components

Not applicable.

## 6. Business Logic

**FR-1: Delete file → ingest prunes entry**

1. User deletes `.md` file from vault directory
2. User runs `memctl ingest`
3. `IngestOperator.Execute()` starts: initialize DB, run prune phase
4. `SELECT id, file_path FROM notes` returns all indexed entries
5. Compare each against `HashSet` built from disk files
6. Stale entries → `DELETE FROM notes WHERE id = @id`
7. Continue to upsert loop — disk files get re-indexed
8. Result: stale entries gone, fresh entries present

**FR-2: context-inject excludes pruned notes**

Same mechanism as FR-1. `context-inject` reads from index → pruned entries absent → output clean.

**FR-3: No-op on unchanged vault**

Second ingest: prune phase finds all disk files match index → `pruned = 0` → upsert loop runs but all unchanged → result identical.

**FR-4: Prune count in output**

`IngestReport` carries `Pruned` → `MemctlResultMapper.MapIngest()` copies to DTO → JSON output includes `"pruned": N`.

## 7. Error Handling Strategy

| Error Scenario | Handling | User-Facing Message | FR |
|---------------|----------|--------------------|----|
| Index not yet initialized | `_db is null` → `GetAllFilePaths()` returns empty `[]` | Silent — prune loop skipped, pruned=0 | FR-3 |
| File deleted between EnumerateMarkdownFiles and ParseNote | `ParseNote()` throws FileNotFoundException → caught by existing try-catch | `"skip {file}: {message}"` on stderr | N/A |
| Empty vault | `GetAllFilePaths()` returns `[]`, `EnumerateMarkdownFiles` returns `[]` | `"Indexed 0/0 notes, pruned 0"` | N/A |

## 8. Security Considerations

Not applicable — no authentication, no user input, no network. This is a local CLI tool operating on a local SQLite database.

## 9. Performance Considerations

- `SELECT id, file_path FROM notes` is O(n) sequential scan, no joins. < 10ms for 10k rows.
- `HashSet.Contains()` is O(1) per lookup. 10k lookups < 1ms.
- Total prune overhead < 50ms on 1000-note vault. Confirmed by NFR-1.
- No impact on upsert loop (unchanged).

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Unit | GetAllFilePaths returns correct tuples | xUnit + SQLite in-memory | 1 new |
| Unit | Prune removes deleted entries | xUnit + temp vault dir | 3 new |
| Integration | Smoke test via CLI | bash script | 1 |

### Key Test Cases

1. `Prune_RemovesDeletedFileEntry`: Create file → ingest → delete file → re-ingest → `GetAll()` excludes deleted
2. `Prune_KeepsExistingFiles`: Create 2 files → ingest → delete 1 file → re-ingest → remaining file present
3. `Prune_NoOpWhenNoDeletions`: Create file → ingest → re-ingest → pruned=0, all present

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected Output | FR |
|----------|---------|-----------------|-----|
| Prune deleted note | `memctl add "test" --title "smoke"; rm .memctl/smoke.md; memctl ingest` | `"pruned": 1` in JSON | FR-1, FR-4 |
| No-op re-ingest | `memctl ingest` (twice) | Second run `"pruned": 0` | FR-3 |
| Context inject clean | `echo "test" \| memctl context-inject` | No deleted note in `## Memory Context` | FR-2 |

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| Microsoft.Data.Sqlite | (existing) | Execute `SELECT id, file_path` | No |
| xUnit + coverlet | (existing) | 3 new unit tests | No |

## 12. Implementation Order

1. Add `GetAllFilePaths()` to `INoteIndex` interface (contract first)
2. Implement `GetAllFilePaths()` in `SqliteNoteIndex`
3. Add prune phase to `IngestOperator.Execute()`
4. Add `Pruned` field to `IngestReport` record
5. Add `Pruned` to `IngestReportDto`
6. Map `Pruned` in `MemctlResultMapper.MapIngest()`
7. Write 3 unit tests in `IngestOperatorPruneTests.cs`
8. `dotnet test` → verify 60+/60+ pass
9. Smoke test via CLI

## 13. Assumptions & Open Design Decisions

- Using mutable `int pruned = 0` counter in Execute() rather than LINQ `Count()` — avoids double-enumeration of `GetAllFilePaths()`. More explicit, easier to debug.
- `IngestReport` stays a positional record with default `Pruned = 0` — backward compatible with any existing code constructing it without the new field.
- `OrdinalIgnoreCase` for path comparison — Windows filesystem is case-insensitive, Linux is case-sensitive. Normalize to common case-insensitive comparison. DB paths are always `/`-separated (ParseNote normalizes). Disk paths also normalized via `.Replace('\\', '/')`.

## 14. Traceability Matrix

| Requirement | Design Section | Files | Test Cases |
|-------------|---------------|-------|------------|
| FR-1 | 6, 10.5 | IngestOperator.cs, SqliteNoteIndex.cs | Prune_RemovesDeletedFileEntry, smoke |
| FR-2 | 6, 10.5 | IngestOperator.cs | smoke context-inject check |
| FR-3 | 6, 10.5 | IngestOperator.cs | Prune_NoOpWhenNoDeletions, smoke double-ingest |
| FR-4 | 2 (Integration blocks) | IngestReport.cs, MemctlResult.cs, MemctlResultMapper.cs | smoke JSON check |
| NFR-1 | 9 | SqliteNoteIndex.cs | (profiling check) |
| NFR-2 | 10 | All files | dotnet test |
