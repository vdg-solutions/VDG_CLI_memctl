# Technical Design: Add --folder Filter to Search Commands

**Spec:** docs/specs/2-spec.md
**Task:** #2
**Date:** 2026-04-17
**Status:** Draft

---

## 1. Architecture Overview

This is a horizontal concern touching all 3 search paths. No new layer is introduced — changes propagate downward from CLI option → Operator → INoteIndex port → SqliteNoteIndex implementation.

### System Context

```
CLI parse --folder <prefix>
  └─ SearchOperator / SearchSemanticOperator / SearchTextOperator
       └─ INoteIndex.SearchBm25(query, limit, folderPrefix)
          INoteIndex.SearchSemantic(embedding, limit, scopeIds, folderPrefix)
               └─ SqliteNoteIndex: SQL WHERE file_path LIKE 'prefix/%'
```

Data flow for folder-filtered search:
1. User passes `--folder crypto` → `folderPrefix = "crypto"`
2. Program.cs reads option, passes to operator Execute()
3. Operator calls index with folderPrefix
4. SqliteNoteIndex.NormalizePrefix converts `"crypto"` → `"crypto/%"`
5. SQL filters candidates before BM25 scoring / embedding load
6. Results returned — only notes with `file_path` starting with `crypto/`

---

## 2. File Changes

### New Files

None.

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/CoreAbstractions/Ports/INoteIndex.cs` | Add `folderPrefix` param to `SearchBm25` and `SearchSemantic` | FR-010, FR-011 |
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs` | Implement folder filter in SQL; add `NormalizePrefix` helper | FR-010, FR-011, FR-012 |
| `src/memctl/Operators/SearchTextOperator.cs` | Add `folderPrefix` param, pass to index | FR-003 |
| `src/memctl/Operators/SearchSemanticOperator.cs` | Add `folderPrefix` param, pass to index | FR-002 |
| `src/memctl/Operators/SearchOperator.cs` | Add `folderPrefix` param, pass to both BM25 and semantic | FR-001 |
| `src/memctl/Bootstrap/Program.cs` | Add `--folder` Option to `search`, `search-semantic`, `search-text` commands | FR-001, FR-002, FR-003 |

### Integration Code Blocks

---

#### INTEGRATION: INoteIndex.cs → SearchBm25 and SearchSemantic

```
// INTEGRATION: INoteIndex.cs → SearchBm25()
// old_string:
    IReadOnlyList<SearchHit> SearchBm25(string query, int limit);

// new_string:
    IReadOnlyList<SearchHit> SearchBm25(string query, int limit, string? folderPrefix = null);
```

```
// INTEGRATION: INoteIndex.cs → SearchSemantic()
// old_string:
    IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null);

// new_string:
    IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null, string? folderPrefix = null);
```

---

#### INTEGRATION: SqliteNoteIndex.cs → SearchBm25()

```
// INTEGRATION: SqliteNoteIndex.cs → SearchBm25()
// old_string:
    public IReadOnlyList<SearchHit> SearchBm25(string query, int limit)
    {
        using var cmd = Cmd(@"
            SELECT n.*, bm25(notes_fts) AS rank
            FROM notes_fts f
            JOIN notes n ON n.rowid = f.rowid
            WHERE notes_fts MATCH @q
            ORDER BY rank
            LIMIT @limit",
            ("@q", EscapeFts(query)),
            ("@limit", limit));

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
        var prefix = NormalizePrefix(folderPrefix);
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
```

---

#### INTEGRATION: SqliteNoteIndex.cs → SearchSemantic()

```
// INTEGRATION: SqliteNoteIndex.cs → SearchSemantic()
// old_string:
    public IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null)
    {
        // Load all notes with embeddings, compute cosine sim in-memory
        var sql = scopeIds is { Count: > 0 }
            ? $"SELECT * FROM notes WHERE embedding IS NOT NULL AND id IN ({string.Join(',', scopeIds.Select((_, i) => $"@id{i}"))})"
            : "SELECT * FROM notes WHERE embedding IS NOT NULL";

        using var cmd = Cmd(sql);
        if (scopeIds is { Count: > 0 })
            for (var i = 0; i < scopeIds.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", scopeIds[i]);

        var candidates = new List<(Note note, float score)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var note = ReadNote(r);
            if (note.Embedding is null) continue;
            var score = CosineSimilarity(queryEmbedding, note.Embedding);
            candidates.Add((note, score));
        }

        return [.. candidates
            .OrderByDescending(c => c.score)
            .Take(limit)
            .Select(c => new SearchHit { Note = c.note, Score = c.score, Snippet = null })];
    }

// new_string:
    public IReadOnlyList<SearchHit> SearchSemantic(float[] queryEmbedding, int limit, IReadOnlyList<string>? scopeIds = null, string? folderPrefix = null)
    {
        var prefix = NormalizePrefix(folderPrefix);
        var conditions = new List<string> { "embedding IS NOT NULL" };
        if (prefix is not null) conditions.Add("file_path LIKE @prefix");
        if (scopeIds is { Count: > 0 }) conditions.Add($"id IN ({string.Join(',', scopeIds.Select((_, i) => $"@id{i}"))})");

        var sql = $"SELECT * FROM notes WHERE {string.Join(" AND ", conditions)}";
        using var cmd = Cmd(sql);
        if (prefix is not null) cmd.Parameters.AddWithValue("@prefix", prefix);
        if (scopeIds is { Count: > 0 })
            for (var i = 0; i < scopeIds.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", scopeIds[i]);

        var candidates = new List<(Note note, float score)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var note = ReadNote(r);
            if (note.Embedding is null) continue;
            var score = CosineSimilarity(queryEmbedding, note.Embedding);
            candidates.Add((note, score));
        }

        return [.. candidates
            .OrderByDescending(c => c.score)
            .Take(limit)
            .Select(c => new SearchHit { Note = c.note, Score = c.score, Snippet = null })];
    }
```

---

#### INTEGRATION: SqliteNoteIndex.cs — Add NormalizePrefix helper

Add after the `private void Dispose()` line (near the `// --- helpers ---` section):

```
// INTEGRATION: SqliteNoteIndex.cs → NormalizePrefix() [NEW private static method]
// old_string:
    // --- helpers ---

// new_string:
    // --- helpers ---

    private static string? NormalizePrefix(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.TrimEnd('/') + "/%";
```

---

#### INTEGRATION: SearchTextOperator.cs → Execute()

```
// INTEGRATION: SearchTextOperator.cs → Execute()
// old_string:
    public MemctlOutcome Execute(string vaultPath, string query, int limit)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var hits = index.SearchBm25(query, limit);

        return MemctlOutcome.Ok("search-text", $"{hits.Count} results", new
        {
            query,
            count   = hits.Count,
            results = hits.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }

// new_string:
    public MemctlOutcome Execute(string vaultPath, string query, int limit, string? folderPrefix = null)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, null).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));
        var hits = index.SearchBm25(query, limit, folderPrefix);

        return MemctlOutcome.Ok("search-text", $"{hits.Count} results", new
        {
            query,
            count   = hits.Count,
            results = hits.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }
```

---

#### INTEGRATION: SearchSemanticOperator.cs → Execute()

```
// INTEGRATION: SearchSemanticOperator.cs → Execute()
// old_string:
    public MemctlOutcome Execute(string vaultPath, string query, int limit, string[]? scopeIds)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var mismatch = ModelGuard.Check(index, embedding, "search-semantic");
        if (mismatch is not null) return mismatch;

        var qEmb = embedding.Embed(query);
        var hits = index.SearchSemantic(qEmb, limit, scopeIds);

        return MemctlOutcome.Ok("search-semantic", $"{hits.Count} results", new
        {
            query,
            count   = hits.Count,
            results = hits.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }

// new_string:
    public MemctlOutcome Execute(string vaultPath, string query, int limit, string[]? scopeIds, string? folderPrefix = null)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var mismatch = ModelGuard.Check(index, embedding, "search-semantic");
        if (mismatch is not null) return mismatch;

        var qEmb = embedding.Embed(query);
        var hits = index.SearchSemantic(qEmb, limit, scopeIds, folderPrefix);

        return MemctlOutcome.Ok("search-semantic", $"{hits.Count} results", new
        {
            query,
            count   = hits.Count,
            results = hits.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }
```

---

#### INTEGRATION: SearchOperator.cs → Execute()

```
// INTEGRATION: SearchOperator.cs → Execute()
// old_string:
    public MemctlOutcome Execute(string vaultPath, string query, int limit)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var mismatch = ModelGuard.Check(index, embedding, "search");
        if (mismatch is not null) return mismatch;

        var bm25Hits     = index.SearchBm25(query, limit * 2);
        var qEmb         = embedding.Embed(query);
        var semanticHits = index.SearchSemantic(qEmb, limit * 2);

        var scores = new Dictionary<string, float>();

        AddRrfScores(scores, bm25Hits,     weight: 1.0f);
        AddRrfScores(scores, semanticHits, weight: 1.0f);

        var allNotes = bm25Hits.Concat(semanticHits)
            .GroupBy(h => h.Note.Id)
            .ToDictionary(g => g.Key, g => g.First().Note);

        var results = scores
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new SearchHit
            {
                Note    = allNotes[kv.Key],
                Score   = kv.Value,
                Snippet = bm25Hits.FirstOrDefault(h => h.Note.Id == kv.Key)?.Snippet,
            })
            .ToList();

        return MemctlOutcome.Ok("search", $"{results.Count} results", new
        {
            query,
            count   = results.Count,
            results = results.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }

// new_string:
    public MemctlOutcome Execute(string vaultPath, string query, int limit, string? folderPrefix = null)
    {
        if (IngestOperator.NeedsIngest(vaultPath))
            new IngestOperator(vaultReader, index, embedding).Execute(vaultPath);

        index.Initialize(IngestOperator.DbPath(vaultPath));

        var mismatch = ModelGuard.Check(index, embedding, "search");
        if (mismatch is not null) return mismatch;

        var bm25Hits     = index.SearchBm25(query, limit * 2, folderPrefix);
        var qEmb         = embedding.Embed(query);
        var semanticHits = index.SearchSemantic(qEmb, limit * 2, folderPrefix: folderPrefix);

        var scores = new Dictionary<string, float>();

        AddRrfScores(scores, bm25Hits,     weight: 1.0f);
        AddRrfScores(scores, semanticHits, weight: 1.0f);

        var allNotes = bm25Hits.Concat(semanticHits)
            .GroupBy(h => h.Note.Id)
            .ToDictionary(g => g.Key, g => g.First().Note);

        var results = scores
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => new SearchHit
            {
                Note    = allNotes[kv.Key],
                Score   = kv.Value,
                Snippet = bm25Hits.FirstOrDefault(h => h.Note.Id == kv.Key)?.Snippet,
            })
            .ToList();

        return MemctlOutcome.Ok("search", $"{results.Count} results", new
        {
            query,
            count   = results.Count,
            results = results.Select(h => GetOperator.NoteToData(h.Note, h.Score)),
        });
    }
```

---

#### INTEGRATION: Program.cs — search command

```
// INTEGRATION: Program.cs → search command handler
// old_string:
// --- search (hybrid RRF) ---
var searchQueryArg = new Argument<string>("query");
var searchCmd      = new Command("search", "Hybrid search (BM25 + semantic, RRF fusion)");
searchCmd.AddArgument(searchQueryArg);
searchCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb = await GetEmbedding(g);
    var op  = new SearchOperator(vaultReader, noteIndex, emb);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(searchQueryArg), g.Limit));
});
root.AddCommand(searchCmd);

// new_string:
// --- search (hybrid RRF) ---
var searchQueryArg = new Argument<string>("query");
var searchFolderOpt = new Option<string?>("--folder", "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)");
var searchCmd      = new Command("search", "Hybrid search (BM25 + semantic, RRF fusion)");
searchCmd.AddArgument(searchQueryArg);
searchCmd.AddOption(searchFolderOpt);
searchCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb    = await GetEmbedding(g);
    var op     = new SearchOperator(vaultReader, noteIndex, emb);
    var folder = ctx.ParseResult.GetValueForOption(searchFolderOpt);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(searchQueryArg), g.Limit, folder));
});
root.AddCommand(searchCmd);
```

---

#### INTEGRATION: Program.cs — search-semantic command

```
// INTEGRATION: Program.cs → search-semantic command handler
// old_string:
// --- search-semantic ---
var semQueryArg = new Argument<string>("query");
var semScopeOpt = new Option<string?>("--scope", "Comma-separated note IDs to restrict search");
var semCmd      = new Command("search-semantic", "Semantic vector search");
semCmd.AddArgument(semQueryArg);
semCmd.AddOption(semScopeOpt);
semCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb   = await GetEmbedding(g);
    var op    = new SearchSemanticOperator(vaultReader, noteIndex, emb);
    var scope = ctx.ParseResult.GetValueForOption(semScopeOpt)
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(semQueryArg), g.Limit, scope));
});
root.AddCommand(semCmd);

// new_string:
// --- search-semantic ---
var semQueryArg  = new Argument<string>("query");
var semScopeOpt  = new Option<string?>("--scope",  "Comma-separated note IDs to restrict search");
var semFolderOpt = new Option<string?>("--folder", "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)");
var semCmd       = new Command("search-semantic", "Semantic vector search");
semCmd.AddArgument(semQueryArg);
semCmd.AddOption(semScopeOpt);
semCmd.AddOption(semFolderOpt);
semCmd.SetHandler(async ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var emb    = await GetEmbedding(g);
    var op     = new SearchSemanticOperator(vaultReader, noteIndex, emb);
    var scope  = ctx.ParseResult.GetValueForOption(semScopeOpt)
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var folder = ctx.ParseResult.GetValueForOption(semFolderOpt);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(semQueryArg), g.Limit, scope, folder));
});
root.AddCommand(semCmd);
```

---

#### INTEGRATION: Program.cs — search-text command

```
// INTEGRATION: Program.cs → search-text command handler
// old_string:
// --- search-text ---
var stQueryArg = new Argument<string>("query");
var stCmd      = new Command("search-text", "Full-text BM25 search");
stCmd.AddArgument(stQueryArg);
stCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op = new SearchTextOperator(vaultReader, noteIndex);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(stQueryArg), g.Limit));
});
root.AddCommand(stCmd);

// new_string:
// --- search-text ---
var stQueryArg  = new Argument<string>("query");
var stFolderOpt = new Option<string?>("--folder", "Filter results to notes under this folder prefix (e.g. crypto, projects/memctl)");
var stCmd       = new Command("search-text", "Full-text BM25 search");
stCmd.AddArgument(stQueryArg);
stCmd.AddOption(stFolderOpt);
stCmd.SetHandler(ctx =>
{
    var g = G(ctx);
    if (RequireVault(g, ctx) is not { } vault) return;
    var op     = new SearchTextOperator(vaultReader, noteIndex);
    var folder = ctx.ParseResult.GetValueForOption(stFolderOpt);
    ResultPrinter.Print(op.Execute(vault, ctx.ParseResult.GetValueForArgument(stQueryArg), g.Limit, folder));
});
root.AddCommand(stCmd);
```

---

### Deleted Files

None.

---

## 3. Data Model

No schema changes. `file_path` column already exists in the `notes` table and is indexed (UNIQUE constraint implies index). The LIKE `'prefix/%'` query on `file_path` is efficient for prefix scans.

---

## 4. API Design

N/A — CLI tool, no HTTP endpoints.

---

## 5. UI Components

N/A — CLI tool.

---

## 6. Business Logic

### Folder Prefix Normalization

```
NormalizePrefix(null)       → null    (no filter applied)
NormalizePrefix("")         → null    (empty = no filter)
NormalizePrefix("crypto")   → "crypto/%"
NormalizePrefix("crypto/")  → "crypto/%"
NormalizePrefix("a/b/c/")   → "a/b/c/%"
```

Rules:
1. Null or whitespace-only → return null (no filter)
2. Strip trailing slashes
3. Append `/%` for SQLite LIKE prefix match

### SQL Filter Application

**SearchBm25:** FTS5 query already JOINs `notes_fts f JOIN notes n ON n.rowid = f.rowid`, so `n.file_path` is directly available. Append `AND n.file_path LIKE @prefix` before `ORDER BY rank`.

**SearchSemantic:** Build WHERE clause dynamically using a conditions list. Apply in order: embedding IS NOT NULL → folder prefix → scopeIds. All conditions ANDed.

**Non-existent folder:** SQL returns 0 rows → empty result list → `count: 0` in JSON output → exit 0. No error raised.

---

## 7. Error Handling Strategy

| Error Scenario | Handling | FR |
|---------------|----------|----|
| `--folder ""` (empty) | NormalizePrefix returns null → no filter | FR-005 |
| `--folder "nonexistent"` | SQL returns 0 rows → count: 0, exit 0 | FR-009 |
| `--folder "crypto/"` (trailing slash) | NormalizePrefix strips slash → same as "crypto" | FR-007 |

---

## 8. Security Considerations

- `folderPrefix` is passed as a SQL parameter (`@prefix`), never interpolated — no SQL injection risk.

---

## 9. Performance Considerations

- Folder filter is applied at the SQL level before embedding loads — only matching rows' BLOBs are read into memory (NFR-001).
- The `file_path` column has a UNIQUE index (from schema), making LIKE prefix scans efficient.
- No additional indexes needed.

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Unit | NormalizePrefix edge cases | xUnit inline data | ~5 tests |
| Unit | SearchBm25 with folderPrefix returns only matching notes | xUnit + in-memory SQLite | ~3 tests |
| Unit | SearchSemantic with folderPrefix returns only matching notes | xUnit + in-memory SQLite | ~3 tests |
| Unit | SearchSemantic with folderPrefix + scopeIds combined | xUnit + in-memory SQLite | ~2 tests |
| E2E (smoke) | search --folder returns scoped results | dotnet run | 3 scenarios |

### Key Test Cases

1. `SearchBm25("ethereum", 10, "crypto")` — returns only notes with `file_path LIKE 'crypto/%'`
2. `SearchSemantic(emb, 10, null, "projects/memctl")` — only nested folder notes
3. `SearchSemantic(emb, 10, ["id1"], "crypto")` — folder + ID scope intersection
4. `NormalizePrefix("crypto/")` == `NormalizePrefix("crypto")` == `"crypto/%"`
5. Non-existent folder returns empty list, no exception

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5)

| Scenario | Command | Expected Output | FR |
|----------|---------|-----------------|-----|
| search with folder filter | `dotnet run --project src/memctl -- search --vault {vault} --folder {folder} "{query}"` | stdout contains `"count"`, exit 0; all results have file_path starting with folder prefix | FR-001 |
| search-semantic with folder | `dotnet run --project src/memctl -- search-semantic --vault {vault} --folder {folder} "{query}"` | stdout contains `"count"`, exit 0 | FR-002 |
| search-text with folder | `dotnet run --project src/memctl -- search-text --vault {vault} --folder {folder} "{query}"` | stdout contains `"count"`, exit 0 | FR-003 |

> Note: smoke scenarios require a pre-ingested vault. QC should use the project's own vault (`H:/repos/VDG_repos/CLIs/VDG_CLI_memctl`) or create a minimal test vault.

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| Microsoft.Data.Sqlite | existing | SQLite LIKE queries | No |

---

## 12. Implementation Order

1. **INoteIndex.cs** — add `folderPrefix` param to `SearchBm25` and `SearchSemantic` signatures
2. **SqliteNoteIndex.cs** — add `NormalizePrefix` helper, implement folder filter in both methods
3. **SearchTextOperator.cs** — add `folderPrefix` param, pass to `index.SearchBm25`
4. **SearchSemanticOperator.cs** — add `folderPrefix` param, pass to `index.SearchSemantic`
5. **SearchOperator.cs** — add `folderPrefix` param, pass to both calls (use named arg for semantic)
6. **Program.cs** — add `--folder` option to 3 commands, wire to operator calls
7. **Build + test** — `dotnet build --warnaserrors`

---

## 13. Assumptions & Open Design Decisions

- Assumption: Case-sensitive prefix matching is correct (as per FR-006). File paths in Obsidian are OS-determined; on Windows they may be case-insensitive, but the index stores paths as-ingested. Case-insensitive follow-up is out-of-scope per spec §6.
- Assumption: `NormalizePrefix` lives in `SqliteNoteIndex` (not in operators) because it's a SQL implementation detail. Operators pass the raw string; normalization happens at the SQL boundary.

---

## 14. Traceability Matrix

| Requirement | Design Section | Files | Notes |
|-------------|---------------|-------|-------|
| FR-001 | §2, §6 | SearchOperator.cs, Program.cs | `search --folder` |
| FR-002 | §2, §6 | SearchSemanticOperator.cs, Program.cs | `search-semantic --folder` |
| FR-003 | §2, §6 | SearchTextOperator.cs, Program.cs | `search-text --folder` |
| FR-004 | §6 | SearchSemanticOperator.cs, INoteIndex.cs | `--folder` + `--scope` combined |
| FR-005 | §7 | SqliteNoteIndex.cs | No --folder = no filter |
| FR-006 | §6 | SqliteNoteIndex.cs | LIKE is case-sensitive in SQLite by default |
| FR-007 | §6 | SqliteNoteIndex.NormalizePrefix | TrimEnd('/') |
| FR-008 | §6 | SqliteNoteIndex.cs | LIKE 'a/b/c/%' matches nested |
| FR-009 | §7 | SqliteNoteIndex.cs | Empty result set, no error |
| FR-010 | §2 | INoteIndex.cs, SqliteNoteIndex.cs | SearchBm25 signature + SQL |
| FR-011 | §2 | INoteIndex.cs, SqliteNoteIndex.cs | SearchSemantic signature + SQL |
| FR-012 | §6, §9 | SqliteNoteIndex.cs | SQL-level pre-filter |
| NFR-001 | §9 | SqliteNoteIndex.cs | Only matching BLOBs loaded |
| NFR-002 | §2 | Program.cs | Same description text on all 3 |
| NFR-003 | §2 | INoteIndex.cs | null default, backward compat |
